# Changelog

本文件记录用户可见的发布变化。版本号遵循 `1.0.0-rc.N` 候选线；正式 1.0 之前不承诺兼容性。

## [1.0.0-rc.5] - 2026-10-26

### Fixed

- 首次启动工作区判定覆盖旧版本保存的 `LastWorkspace`：如果最近目录等于程序目录或 `%LOCALAPPDATA%\Augit\Welcome`，仍显示「Open Workspace」欢迎页，不再恢复发布目录并自动弹出「Create Git Repository」。
- 无需用户手动删除 `%LOCALAPPDATA%\Augit\settings.json` 即可从 rc.3/rc.4 的旧设置升级。

### Changed

- 其余 rc.4 修复保持不变：`CETCompat=false`、async void 异常边界、本地 `augit.log`、`git init` 30 秒超时与本地路径校验；便携版 self-contained、安装版 framework-dependent。
- 新增“旧 LastWorkspace 等于程序目录”测试；Windows 10 22H2 启动路径仍待用户实机复验。

## [1.0.0-rc.4] - 2026-10-26

### Fixed

- Windows 10 22H2 首次启动选择目录后卡住/退出的加固：apphost 显式 `CETCompat=false`；首次启动在程序目录时改用欢迎空目录并显示「Open Workspace」，不再把发布目录当项目或自动弹出「Create Git Repository」。
- `OnWebMessageReceived`/`OnNavigationCompleted` 增加 async void 异常边界，处理器异常只写本地日志，不再终止进程。
- 新增 `%LOCALAPPDATA%\Augit\logs\augit.log` 本地诊断日志（启动、桥接异常、watcher 初始化异常、WebView2 进程失败），不上传、不记录文件内容/Git 输出/凭据，4 MB 轮换。
- `git/init` 增加 30 秒超时和本地固定磁盘路径校验，避免在网络/异常目录上无限停在 “Initializing…”。
- 保留 rc.3 的 self-contained 便携版（.NET 10.0.11），安装版仍为 framework-dependent 联网安装器。

### Changed

- 新增首次启动回退与显式写超时测试；测试总数 Shell 270、Infrastructure 207。
- rc.4 的 Windows 10 22H2 启动路径修复仍待在用户出问题的实机复验；rc.2/rc.3 的 Win10 界面/DPI/安装证据不覆盖本版启动路径。

### Known limitations

完整列表见 [`docs/known-limitations-1.0.0-rc.4.md`](docs/known-limitations-1.0.0-rc.4.md)。重点：Win10 22H2 修复待复验、干净虚拟机/真实缺 WebView2、三屏/物理拔线、80,000 行软换行、离屏全文连续朗读、Settings 冷开 567ms、500ms 历史慢样本未归因、整窗像素目标未整体达成。

## [1.0.0-rc.3] - 2026-10-26

### Fixed

- 便携版改为 **self-contained**，自带 .NET 10 Runtime；没有预装 .NET 的新电脑不再先弹出 .NET 宿主错误，解压后可直接运行 `Augit.exe`。
- 发布脚本分别构建 framework-dependent 安装版和 self-contained 便携版，并拒绝两种运行时形态混装。
- self-contained 还原使用独立的 `packages.selfcontained.lock.json` 与 `--locked-mode`，不改写主 `packages.lock.json`。
- 本机在隐藏/失效共享 .NET 路径后验证便携包真实启动：窗口正常、退出码 0、无残留子进程。

### Changed

- README、架构 §7、运行时依赖说明、发布流程和已知限制同步为“便携版 self-contained、安装版联网安装器”；安装版的 .NET 检测/下载逻辑保持不变。
- 安装版仍是小型联网安装器；便携版仍要求系统已有 WebView2，但不安装或修改共享运行时。
- rc.2 的公开 Release 保留，不覆盖、不改写；rc.3 运行代码与 rc.2 相同。

### Known limitations

完整列表见 [`docs/known-limitations-1.0.0-rc.3.md`](docs/known-limitations-1.0.0-rc.3.md)。重点：干净虚拟机/真实缺 WebView2、三屏/物理拔线、80,000 行软换行、离屏全文连续朗读、Settings 冷开 567ms、500ms 历史慢样本未归因、整窗像素目标未整体达成。

## [1.0.0-rc.2] - 2026-10-26

### Added

- 公开 GitHub 预发布（Pre-release），提供 Windows x64 安装器、便携 zip 和 `SHA256SUMS.txt`，真实用户无需从源码构建。
- Windows CI（`.github/workflows/ci.yml`）与手动发布流水线（`.github/workflows/release.yml`）。
- 发布清单、发布流程、Issue 反馈模板和 `CHANGELOG.md`。
- Windows 10 22H2 多显示器与混合 DPI 验证：100%/125%/150%/175%/200%、负坐标布局、模拟热插拔、深色窄窗。
- 安装器在缺少 .NET 10 的 Windows 10 22H2 实机上完成确认、下载、校验与安装的验证记录。

### Fixed

- 真实鼠标跨屏拖动后窗口逻辑尺寸不再缩小；`WM_DPICHANGED` 按旧/新 DPI 保持逻辑视口。
- 混合 DPI 启动按目标显示器自己的 DPI 解释保存位置，不再放大或回退到主屏默认位置。
- Settings 子窗改用 Owner 当前显示器 DPI，修复 175% 下内容被压缩。
- 200% + 1024×640 窄窗时 Settings 与主窗不再压住任务栏，底部按钮保持可见。
- 大文本活动行向屏幕阅读器暴露行号与正文，不再只播报位置。
- 首次打包启动时 WebView2 探测早于加载器加载导致的启动回归。
- 缺少运行时时在窗口出现前给出缺失项、微软官方地址和便携说明。
- `0.1.0 → 1.0.0-rc.2` 覆盖升级、PATH 恢复、设置保留和卸载清理。

### Changed

- README 改写为面向真实用户的下载、安装、便携、验证状态和已知限制说明；增加 CI/Release 徽章。
- 默认分支与公开发布对齐到 `1.0.0-rc.2`；历史 `8643b9e` 冻结构建从未公开上传，公开资产由当前源码重建并在发布清单记录哈希。
- 发布流水线从 tag 构建并生成带实际哈希的 Release 正文；托管 CI 排除需要交互式控制台的 ConPTY 用例，并为 FileSystemWatcher 设置显式的 runner 预算，本地/真实桌面仍保留 500ms 产品断言。
- 发布说明明确：这是预发布候选，不是正式 1.0；Augit 仍未代码签名。

### Known limitations

完整列表见 [`docs/known-limitations-1.0.0-rc.2.md`](docs/known-limitations-1.0.0-rc.2.md)。重点：80,000 行软换行全量 DOM、屏幕阅读器离屏全文连续朗读、Settings 冷开 567ms、500ms 历史慢样本未归因、整窗像素目标未整体达成、干净虚拟机/真实缺 WebView2 未验证。
