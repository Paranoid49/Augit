# 隐藏遮盖准备并行化与关闭竞态（2026-10-14）

## 范围与实现

本批承接[系统录屏整改](pixel-refresh-screen-2026-10-14.md)。首次刷新仍需捕获PNG、创建遮盖控制器和等待图片呈现。只对隐藏遮盖子窗并行启动PNG捕获与控制器创建；可见背景继续先捕获再创建，避免默认底色进入原图。保留透明默认背景、隐藏子窗、置顶、两帧回调及DwmFlush，未削减呈现保护。

新增`ShellSnapshotPreparation`，产品`ShellModalHost.CreateSnapshotAsync`直接使用。两项操作均结束才交付；同步抛错也转为待观察任务。失败或代次过期时释放成功创建的控制器，不在捕获仍写流时退出。关闭立即销毁子窗可能使正在创建的WebView控制器失败；仅当请求已过期且错误属于COM或无效生命周期时返回空结果。当前请求的同类错误，以及未识别错误，仍按原路径报告。捕获、创建各自计时，重叠时间不能相加为总耗时。

修改：`src/Augit.Shell/ShellModalHost.cs`、`src/Augit.Shell/ShellSnapshotPreparation.cs`及`tests/Augit.Shell.Tests/ShellSnapshotPreparationTests.cs`。本批没有新增第三方依赖、网页产品修改或发布镜像修改。

## 失败与验证

前三次真实验证目录`refresh-parallel-lifecycle`、`refresh-parallel-ui`、`refresh-parallel-native`均在遮盖与关闭并发请求中失败，不能计作通过。先前怀疑WhenAll改变执行线程没有得到实证；单纯更换await顺序或内联任务未解决。补齐过期创建失败处理后，`refresh-parallel-generation`真实定向场景通过，包括立即关闭、后续重新打开、嵌套、原图尺寸、遮盖复用、更新合并边界及焦点/草稿。

- 七项准备流程测试通过：并行启动、可见背景顺序、同步捕获/创建失败、资源释放、过期创建失败和有效请求错误保留。
- 窗口生命周期模块收口执行一次Shell完整测试：191通过、0失败，12秒；未运行仓库全量。
- 限定三个C#文件的格式检查、共享UI资产和`git diff --check`通过。

## 新系统录屏

目录位于`artifacts/pixel-followup-2026-10-14/`，分别为`refresh-screen-parallel-dark`和`refresh-screen-parallel-light`。Debug、175%缩放、UI12、1400×930逻辑主窗，录屏条件延续前批。每主题一次冷遮盖及五次复用；延迟仍是宿主状态轮询观测，不是屏幕内容切换时刻。

| 主题 | 首次ms | 后续五次ms | 采样帧 | 探针异常帧 | 最大间隙ms |
| --- | ---: | --- | ---: | ---: | ---: |
| 深色 | 519.49 | 378.15 / 368.66 / 364.74 / 383.37 / 402.64 | 434 | 0 | 50 |
| 浅色 | 506.24 | 382.07 / 376.09 / 402.91 / 345.96 / 372.00 | 438 | 0 | 50 |

此前深浅首次分别572.35/573.59ms，本次样本减少52.86/67.35ms；这是不同运行的观测差，不是稳定性能分布的证明。首次仍超过500ms，延迟不能结案。两份录像均无重复时间戳，但有282/280处超过25ms间隙；分析器`strictTimingPassed`仅表示时间戳顺序正常，不代表显示帧完整。872个采样帧三处探针无异常，`fullPresentationVerified`仍为false，不宣称所有模态或整个表单逐帧无闪隐。

## 命令与资源清理

```powershell
$pixelPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
dotnet test tests/Augit.Shell.Tests --no-restore --filter FullyQualifiedName~ShellSnapshotPreparationTests -v minimal
node tools/audit/run-pixel-remediation-shell.cjs $pixelPlaywright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/refresh-parallel-new --single --reference-fixture --cover-pixels --coalesce-refresh --refresh-samples
node tools/audit/capture-shadow-shell.cjs $pixelPlaywright artifacts/refresh-parallel-dark-new Dark --refresh-video
node tools/audit/capture-shadow-shell.cjs $pixelPlaywright artifacts/refresh-parallel-light-new Light --refresh-video
python -B tools/audit/verify-refresh-video.py artifacts/refresh-parallel-dark-new
python -B tools/audit/verify-refresh-video.py artifacts/refresh-parallel-light-new
dotnet test tests/Augit.Shell.Tests --no-build --no-restore -v minimal
dotnet format Augit.slnx whitespace --no-restore --verify-no-changes --include src/Augit.Shell/ShellSnapshotPreparation.cs src/Augit.Shell/ShellModalHost.cs tests/Augit.Shell.Tests/ShellSnapshotPreparationTests.cs
powershell -NoProfile -File tools/audit/verify-ui-assets.ps1
git diff --check
# 驱动finally自动停止本次实例、录屏进程并删除隔离目录。异常中断时仅使用驱动打印的PID。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```

独立查询确认：本批及前三次失败的Augit PID10436/10824/17516/20636/2372/756，录屏PID7180/1804均无残留；调试端口55398/56038/60192/50443/62226/52117无监听。隔离目录`Augit-pixel-fix-0fj3RK`、`vAWTbS`、`GXFKi1`、`FaIuBh`及`Augit-shadow-pair-rEY41F`、`gMclK2`均不存在。原始取证文件保留，历史拒绝清理的目录没有重试。

分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。四网页MD5前后不变：mockup.js `8BF2C396751DF1FA9A164E6EBC772886`，mockup.css `CB5E64C420FEA13B86EEEE3098BB455C`，live-data.js `DB52470B2BAB434DE4E463086A39FA6D`，bridge.js `F297BF8769E249CA0D804F4F4EEEF5A9`。未回滚、暂存或提交既有混合修改。

整体仍未完成：55页及所有模态同态配对、终端/冲突完整逐行配对、刷新延迟与完整呈现证据、阴影四角和非活动状态仍有缺口。Windows10排除本次实机验收；跨屏DPI保留未验证。
