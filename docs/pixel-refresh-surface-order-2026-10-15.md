# 背景刷新覆盖关系与隐藏区间（2026-10-15）

## 本轮进展与结论

上一轮完成新的时钟关联与失败试验恢复，属于证据进展。本轮补充原生子窗顺序、控制器可见性及DOM隐藏标记的同机时间记录，仍以普通入口验证。没有更改产品绘制或延长等待。整体目标未完成。

ShellModalHost.State 新增 captureCoverTopmostChild（GetWindow(window, GW_CHILD) 是否为遮盖子窗）与 liveControllerVisible。前者表示兄弟窗口顺序，隐藏子窗仍可能位于最上层，所以必须和 captureCoverWindowVisible 一起解读；不能单独当作正在遮盖。诊断仅返回布尔值，不复制正文、业务DOM或用户数据。

trace-modal-surfaces.cjs 只在显式 --trace-surfaces 的隔离实例启用，记录三种桥接请求/响应及 data-native-background-capture/data-native-capture 属性变化，时间包括墙钟及单调时钟。记录上限512条，宿主结果只保留五个白名单布尔值，关闭时释放观察器、消息监听和包装。若与延迟撤盖诊断组合，按安装逆序释放，避免恢复已释放的包装函数；该清理修正有独立回归。

## 普通入口实际结果

证据目录：artifacts/pixel-followup-2026-10-15/refresh-surface-order/light。正常入口 Light、界面12、正文JetBrains Mono13、DPR1.75；完整1820×1341客户区，RGB容差2，排除像素0。

- 六轮共12个cover/refresh响应：captureCoverReady、captureCoverWindowVisible、captureCoverTopmostChild、liveControllerVisible均为true。cover应答时DOM尚未隐藏；refresh应答时隐藏数为1；uncover请求和应答时隐藏数已回到0。
- DOM观察所得六个隐藏区间为157/183/175/153/162/155ms。它们是观察回调时间，不冒充合成器实际呈现时间。
- GDI录像428帧，48帧变化，最大采样间隔34ms，无重复时间戳。最坏第47帧变化4035像素（角部123、直边3912、内部0），采样在首次uncover响应后43.76ms。其之前最后观测已是DOM恢复、遮盖隐藏、业务控制器可见。
- 本次未复现正文泄漏；既有DDA录像已证明泄漏，不能因一次未复现撤销失败结论。已记录点不存在遮盖未置顶的问题，也不能排除两个观测之间的短暂状态或合成延迟。
- 六次流程耗时493.85/433.06/445.45/375.85/400.13/377.88ms。单次首次低于500ms不等于延迟分布验收；与此前超过500ms的样本一起保留。

录像SHA256：b36e8f490b1d9bf8942732b00fbbd124041382fbed5c393e59ff6034c4f594dd。modal-surface-trace.json保存原始观测；refresh-clock-analysis.json保存严格墙钟关联；surface-order-summary.json重新读取前两者与逐帧报告，断言六个区间及12个有效覆盖响应。sampledWholeSurfacePassed=false，fullPresentationVerified=false。没有裁去边缘、调整容差或把DOM状态当作像素通过。

## 验证、哈希和清理

最终7项定向测试通过（0.167秒），包含数据白名单、异常消息、有界记录、隐藏/恢复和组合钩子逆序释放；采集驱动语法通过。Debug构建2.53秒、Release构建2.69秒，均0警告0错误；未跑无关全量。Debug和Release都更新诊断字段，网页资产与此前对齐交付保持相同。

ShellModalHost.cs SHA256从77B43F8E4C009DFFB3C15288B2D88CEC8BCB5537D137557893C138841E330B9A变为3CEA5A1ED2EE7BD55EF6F9AE93CDDE607567167080E795BB459732266756B070，仅新增上述状态字段。四运行时MD5均未变：mockup.js=612E9976704343ABE1BB8778B077508F；mockup.css=089ED8741BD87C8A4F95501E3485526E；live-data.js=8A39A72F1D0F8FC7D53D3F158205B2C7；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

实例PID12232、录制器PID22236、CDP端口65316、临时目录Augit-shadow-pair-lOpMZv均finally清理并独立检查不存在。分析器和解码进程正常结束。保留原始录像和JSON证据，未回滚、暂存或提交既有混合修改。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node --test tools/audit/trace-modal-surfaces.test.cjs tools/audit/trace-modal-refresh.test.cjs tools/audit/defer-modal-uncover.test.cjs
node tools/audit/capture-shadow-shell.cjs $pw artifacts/refresh-order-new Light --normal-entry --refresh-video --trace-surfaces
D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py artifacts/refresh-order-new
D:/Python/Python3119/python.exe -B tools/audit/correlate-refresh-clock.py artifacts/refresh-order-new
# 驱动正常finally清理；异常只终止本轮输出的PID及其子树。
taskkill /PID <本轮PID> /T /F
# 日常启动；停止请关闭窗口或按输出PID执行同样的taskkill。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
```

下一步需定位原生框架与遮盖子窗在撤盖后的合成转换，不能重复仅改等待帧数的方案；正文露底仍需在带同机时序的采集中复现。55页/全模态配对、终端/冲突逐行、完整阴影的缺口未因此关闭；Windows10排除本次验证，跨屏DPI仍未验证。
