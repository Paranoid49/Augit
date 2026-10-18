# 背景换图重叠试验（2026-10-16）

## 结论

新图解码后先插入旧图之后、经过两次帧回调才释放旧图的候选仍出现一帧外框变化，严格整表面验收失败，已精确撤回。没有新增保留的产品逻辑，也没有新增像素通过页数。整体目标保持未完成：55页/全模态、真实刷新无闪、终端与冲突逐行、完整系统阴影仍缺完整证据；Windows10排除，跨屏DPI未验证。

## 假设与实施

ShellModalHost.Snapshot.LoadImageAsync原先在新图onload中replaceWith旧图并立即清除旧src。本批只将这段替换为previous.after(next)，两次requestAnimationFrame后移除旧图、清除旧src并确认loaded版本。保持首图尺寸、遮盖显示协议、控制器数量、原生子窗、DWM边框与阴影不变；不复制业务DOM。

候选只进入Debug实机，未更新Release。试验前保存当前完整文件及SHA256，撤回前验证文件仍等于本次候选哈希，再恢复原字节，未使用Git回滚混合工作区。候选与基线文本作为审计证据保存在artifacts/pixel-followup-2026-10-16/refresh-image-overlap，不是运行时资源。

## 系统采样证据

同目录light下为普通场景入口、Light、DPR1.75、UI12、JetBrains Mono13、隔离配置/profile及参考fixture。computer-use枚举主窗口、激活并观察后释放main-active-ready检查点，未绕过前景身份门禁。第一次手写Windows路径转义导致身份拒绝，没有执行激活；随后直接使用工具返回的窗口对象成功。

DDA/FFV1共331帧，每帧1820×1341、2440620像素，与首帧比较，RGB容差2，排除0像素。唯一失败为第66帧（1.215秒），9717像素变化、最大通道差241：角部236，16px边缘9481，内部0。最大采样间隔41ms、原始重复时间戳0。FFmpeg解码DTS量化警告保留；sampledWholeSurfacePassed及fullPresentationVerified均false，不用采样结果冒充完整Present证明。

录像SHA256：82e9e880cb15be91b21c963095860e2aca93af226a36eaf43caea4d34cc6ab79。frame-0.png、frame-66.png为从原视频直接提取，frame-66-difference.png为派生差异图，已目视核对。edge-colors.json表明变化主要为顶部两排各1820像素、左侧两列各1341像素、底部两排1652/1641像素；最常见颜色转换为灰色边缘变成RGB47/47/47。行列统计存在交叉，不能将两组相加。仅据此定位外框瞬变，不声称已确定DWM内部根因。

六轮耗时474.85、313.80、349.36、336.51、313.76、361.42ms。单次低于500ms不能证明稳定收益，且像素门禁仍失败。前景98次采样、最大间隔95ms、无已记录干扰，不排除采样间干扰。前景清单内中文limitation有既有编码损坏，数值及身份字段有效，不据损坏文本新增结论。

## 恢复与清理

候选构建8.19秒、恢复构建4.22秒，均0警告0错误。恢复后ShellModalHost SHA256为124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348；两套各24项web资源与源目录一致。没有保留可执行改动，因此不重复Shell全量测试或无收益深色试验。git diff --check通过。

Augit21116、前景采样24572、录像10076及分析解码进程已退出；57384无监听，Augit-shadow-pair-rWly3k和检查点已清理。未触碰用户其他进程或历史被拒绝清理目录。原始图、录像、候选文本及分析JSON保留用于审计。

```powershell
# 正常启动；结束使用时正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 异常仅停止本次启动输出的PID及其子进程。
taskkill /PID <本次PID> /T /F
# 正式实现取证命令；本批候选已撤回，不能用此命令冒充重现候选。
node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新目录> Light --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames --activate-main
D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py <新目录> --require-stable
```

下一步须针对实际外框表面的合成变化取得证据；不能继续靠延长图像保留时间、相同显隐排序或排除边缘像素来宣称修复。
