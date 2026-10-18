# 背景刷新遮盖确认任务试验（2026-10-15）

## 结果

上轮入口修复属于有效进展；本轮继续原有整体目标，未缩小验收范围。当前普通入口仍未通过背景整表面和首次 500ms 门禁。将遮盖呈现确认从第二次 RAF 回调移至其后的零延迟任务，没有取得足够收益证据，已仅撤销本批试验和相应测试变化，并重建恢复。没有撤销前批 live-data.js 的恢复任务候选，也未改正常启动外观。

## 新证据

两次采集均为普通入口、Light、界面 12、正文 JetBrains Mono 13、DPR 1.75；按原始尺寸比较完整 1820×1341 表面，RGB 容差 2、排除像素 0。

| 条件 | 录像目录（artifacts/pixel-followup-2026-10-15 下） | 帧数/变化帧 | 最坏帧 | 首次流程时间 |
| --- | --- | ---: | --- | ---: |
| 修改前，GDI 墙钟关联 | refresh-normal-clock/light | 424/49 | 第46帧9639像素，内部0 | 529.69ms |
| 遮盖确认改为 RAF 后任务，DDA | refresh-cover-task/light | 373/68 | 第71帧4035像素，内部0 | 539.04ms |

修改前最坏帧在 modal-uncover 请求后33.645ms、响应后30.645ms被采到，没有与在途桥接重叠。依据 refresh-clock-analysis.json 的同机墙钟关联；这只定位本次边缘变化，不证明全部绘制时序或正文露底根因。该录像 SHA256 为 d8021a0a2a49923ce87fe379669e30b4331e71b669fa8a09e40bcbd4c6071061，最大采样间隔50ms。

候选 DDA 录像 SHA256 为 5e3da2d99f14ba0f9461335545be0dbbbbd1cc002f83e3e5f348094a6965ec67，最大采样间隔58ms，无重复时间戳；解码时的 rawvideo DTS 量化提示未隐藏。两次都 sampledWholeSurfacePassed=false / fullPresentationVerified=false。两种采集路径和波动不同，不能把9639降为4035宣称修复收益；本次没拍到正文泄漏也不能推翻已有失败录像。

已从 refresh-restored-normal/light 原录像无缩放提取第62帧为 interior-leak-frame62.png：目视确认整个 Settings 正文消失，显示底层仓库及空文件提示，并非仅字体或角点误差。首次命令因 shell 逗号转义失败，改用 Python 参数数组后取得完整原图。

既有换图隔离证据同时核对：held-repeat 452/159、no-hold 379/87、deferred 369/75，均未记录内部变化；正常流程 normal 364/68 在第58、68、112帧有内部变化。前批 restore-task/light 357/73 内部0，但之后普通入口复采已再次露底。不能继续以单份未复现判断等待时长有效。

## 测试、恢复与清理

本批只执行6项相关脚本测试及两次Debug构建，未跑全量。候选构建8.14秒；恢复构建2.58秒，均0警告0错误；恢复后6项再次通过。ShellSnapshotPresentation.cs 恢复后 SHA256 为 4D886823DDBF722D1FE25B52BDFD8D3FCFF2E5A00978B6100CE01A510AB123A3；ShellModalHost.cs 保持77B43F8E4C009DFFB3C15288B2D88CEC8BCB5537D137557893C138841E330B9A。Release未安装本试验，保持此前已对齐版本。

四运行时MD5均未改变：mockup.js=612E9976704343ABE1BB8778B077508F，mockup.css=089ED8741BD87C8A4F95501E3485526E，live-data.js=8A39A72F1D0F8FC7D53D3F158205B2C7，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

实例17680/9152、录制器19916/3108、端口57852/57380以及临时目录Augit-shadow-pair-Jxuy3l/BrHmsd已由finally清理；独立检查均无残留。分析器和解码子进程已正常退出，录像/原图为保留证据。未回滚、暂存或提交既有混合修改。

```powershell
# 从仓库根目录复验；换一个不存在的输出目录，避免覆盖原始证据。
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-shadow-shell.cjs $pw artifacts/refresh-new-clock Light --normal-entry --refresh-video
D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py artifacts/refresh-new-clock
D:/Python/Python3119/python.exe -B tools/audit/correlate-refresh-clock.py artifacts/refresh-new-clock
# 正常由finally退出；异常只停止本轮日志中的实例或录制器及其子树。
taskkill /PID <本轮PID> /T /F
# 日常启动与停止
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 关闭窗口；必要时 taskkill /PID <启动输出的PID> /T /F
```

下一步应在同一采集时钟内分离“遮盖真实可见”“业务控制器隐藏”“业务控制器恢复”和“遮盖隐藏”，核对两者的重叠区间，不继续仅增加 RAF 或固定等待。系统边缘仍需单独解释。55页/全模态、终端/冲突逐行及完整阴影未因此完成；Windows10按裁决排除，跨屏DPI未验证，目标保持进行中。
