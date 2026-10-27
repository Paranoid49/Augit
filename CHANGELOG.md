# Changelog

本文件记录用户可见的发布变化。版本号遵循 `1.0.0-rc.N` 候选线；正式 1.0 之前不承诺兼容性。

## [1.0.0-rc.8] - 2026-10-27

### Changed

- 按 PyCharm 2026.2 实机重做分支弹层：`Recent`/`Local`/`Remote`/`Tags` 默认折叠，`Local` 按 `/` 建目录子树，upstream 只放 tooltip，弹层固定宽度/最大高度并滚动。
- 分支弹层搜索实时过滤并自动展开命中分组；无命中显示 `No matching branches`；顶部动作补回 `Ctrl+T`/`Ctrl+K`/`Ctrl+Shift+K`/`Ctrl+Alt+N` 快捷键提示。
- 分支行不再显示行尾 chevron/upstream 长文本；完整 PyCharm `NextStep` 行内二级菜单仍未实现，行动作走右键/菜单键/Shift+F10。

### Fixed

- 长分支列表不再把弹层拉成一整屏；弹层树区独立滚动。
- 保留 rc.7 欢迎页单窗口、分支树折叠、欢迎页不轮询 changes，以及 rc.6 的选择目录缓冲区/`--workspace` 修复。

## [1.0.0-rc.7] - 2026-10-27

### Fixed

- **首次打开出现两个窗口**：从欢迎页成功启动/激活目标项目后关闭欢迎窗口；目标窗口成为唯一项目窗口。
- **分支列表无法折叠**：全局恢复 `[hidden] { display: none !important; }`，修复 `.tree-row { display:flex }` 覆盖 `hidden` 导致折叠/搜索过滤只改属性、行仍可见的问题；`Local`、`codex` 等分组现在真正隐藏。
- **分支弹层右侧动作面板无条件出现**：移除 `liveBranchesPopover()` 中常驻的“当前分支动作”面板；默认只显示分支/标签列表，行动作仍通过右键/菜单键/Shift+F10 打开。
- **欢迎页 `workspace/changes` 超时 toast**：欢迎空目录不再启动 changes 轮询，也不读取 Git 状态/历史/引用。

### Changed

- 行尾 chevron 暂只作视觉提示，完整行内二级菜单未实现；在已知限制中登记。
- 其余 rc.6 修复保持不变：`SHBrowseForFolder` 输出缓冲区、`--workspace` 新窗口参数、CETCompat、async void 边界、本地日志。

## [1.0.0-rc.6] - 2026-10-26

### Fixed

- **选择目录后 `comctl32.dll` 崩溃**：`BROWSEINFO.pszDisplayName` 从 `string.Empty` 改为显式非托管 `MAX_PATH` 缓冲区；`SHBrowseForFolder` 不再向不可变字符串写入，避免 `c0000005` 在用户确认目录后终止进程。
- **选择目录后新窗口报 `ArgumentException (0x80070057)`**：`CreateAugitWorkspaceStartInfo` 显式传 `--workspace <path>`；`ShellOptions.Parse` 同时支持 `augit <目录>` 位置参数，修复启动器与解析器契约不一致。
- 新增启动器/解析器集成测试，直接解析 `ArgumentList` 验证工作区路径；更新启动器参数契约测试。
- 保留 rc.5 的首次启动/旧 LastWorkspace 欢迎页、CETCompat、async void 边界、本地日志与 `git init` 超时。

### Changed

- 测试总数 Shell **271**、Infrastructure **207**；Windows 11 上真实“欢迎页 → Choose Directory → 目标目录”流程已复验，Win10 22H2 实机仍待复验。

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
