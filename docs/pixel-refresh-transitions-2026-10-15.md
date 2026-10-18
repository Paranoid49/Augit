# 背景刷新DWM过渡单变量试验（2026-10-15）

## 结论

上轮补齐原生覆盖顺序，属于证据进展。本轮原拟核查透明子窗标志，读到 pixel-refresh-navigation-2026-10-15.md 已有去掉 WS_EX_TRANSPARENT 的失败证据后未重复。改为仅在模态顶层窗口设置 DWMWA_TRANSITIONS_FORCEDISABLED=TRUE，保留四边1px框架、阴影、首图尺寸、等待与所有绘制逻辑。接口语义核对 Microsoft Learn 的 DWMWINDOWATTRIBUTE 与 DwmSetWindowAttribute 文档。调用检查 HRESULT，实际采集正常完成。

该试验仍失败，并捕获到正文露底；已撤销本批三行设置及对应P/Invoke，恢复后重新构建并补采原实现对照。没有把试验保留在Debug或Release，也没有改系统设置。不能以禁用过渡消除本问题。

## 同机时序证据

两组都是普通入口、Light、界面12、正文JetBrains Mono13、DPR1.75，GDI原始FFV1，完整1820×1341表面，RGB容差2、排除0像素。目录均位于 artifacts/pixel-followup-2026-10-15。

| 条件/目录 | 帧数/变化帧 | 最严重变化 | 首次流程 |
| --- | ---: | --- | ---: |
| 禁用过渡，refresh-no-transitions/light | 431/47 | 第41帧2296447像素，内部2209047 | 578.45ms |
| 恢复原实现，refresh-surface-order/baseline-repeat | 438/48 | 第47帧9717像素，内部0 | 528.88ms |

试验中的内部泄漏采样在首次 modal-refresh 请求后23.143ms、响应前156.857ms，表单隐藏标记设置后约30ms；发生在撤盖之前。前一个cover响应与后一个refresh响应均报告遮盖可见、置顶、图片就绪。这证明该试验样本的露底不是撤盖过早造成，但不能证明两个响应之间的原生状态始终不变，也不能把修改后时序直接推广给原实现。原帧 interior-leak-frame41.png 已无缩放解码，旁有 interior-leak-timeline.json 保留前后观测。

恢复对照只复现边缘闪亮，采样在首次uncover响应后35.009ms；未复现正文泄漏。保留既有原实现DDA内部泄漏证据，不能以这次未复现称为修好。两组最大采样间隔34/50ms，重复时间戳0，sampledWholeSurfacePassed及fullPresentationVerified均false。后续应分开处理捕获期间的遮盖失效与撤盖后的边缘变化，不再假定所有差异属于同一个撤盖时序。

录像SHA256：试验98b0f566d34eaf477495f8c030690147abbaa12464da55f882dd85428af8ad07；恢复对照30e7723372ce8c042923ce4939cb23bb75ce7145b641ea850b488f2c2be1ea1d。原始录像、六轮事件、DOM/原生状态、整表面分析、墙钟关联均在各目录中。未重采样、未裁去失败区域，且不以单个样本判断性能分布。

## 恢复与清理

试验/恢复Debug构建2.64/2.66秒，均0警告0错误。无保留的可执行逻辑修改，未新增依赖或重复全量测试；恢复对照真实开关与关闭清理通过，但像素失败单独保留。ShellModalHost.cs恢复SHA256为3CEA5A1ED2EE7BD55EF6F9AE93CDDE607567167080E795BB459732266756B070，与本轮修改前一致。Release从未装入本试验。

四运行时MD5未变：mockup.js=612E9976704343ABE1BB8778B077508F，mockup.css=089ED8741BD87C8A4F95501E3485526E，live-data.js=8A39A72F1D0F8FC7D53D3F158205B2C7，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。工作区既有混合修改未回滚、暂存或提交。

PID22896/26292、录制器26568/24728、端口52631/62401和临时目录Augit-shadow-pair-XOaO38/xQVvmY已finally清理，独立核验均无残留；分析器及解码进程正常退出，原始证据保留。

```powershell
# 当前已恢复的正常实现复验；不能用此命令声称复现已撤销的过渡试验。
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-shadow-shell.cjs $pw artifacts/refresh-transitions-baseline-new Light --normal-entry --refresh-video --trace-surfaces
D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py artifacts/refresh-transitions-baseline-new
D:/Python/Python3119/python.exe -B tools/audit/correlate-refresh-clock.py artifacts/refresh-transitions-baseline-new
# 驱动finally自动清理；异常时只停止本次输出PID。
taskkill /PID <本次PID> /T /F
# 日常启动；停止请关闭窗口，必要时按输出PID执行上述taskkill。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
```

整体目标保持进行中：55页及全部模态双端配对、背景稳定性及延迟、终端/冲突逐行、完整阴影均未因此完成。Windows10排除本次验证，跨屏DPI保留未验证。
