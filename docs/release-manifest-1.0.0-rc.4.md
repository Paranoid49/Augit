# Augit 1.0.0-rc.4 公开发布清单

本清单记录 `1.0.0-rc.4` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.4> |
| Tag | `v1.0.0-rc.4` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | tag `v1.0.0-rc.4` 指向的提交（`Augit.exe` ProductVersion 内含该提交号） |
| 版本 | `1.0.0-rc.4` |
| 便携版形态 | **self-contained**（自带 .NET 10.0.11 Runtime） |
| 安装版形态 | **framework-dependent**（安装器检测/下载 .NET 10） |
| FileVersion | `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`；.NET SDK 由 `global.json` 锁定；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

rc.4 在 rc.3 的 self-contained 便携包基础上增加 Windows 10 22H2 启动/选目录退出加固。Release 正文中的资产表由同一次流水线构建的 `SHA256SUMS.txt` 生成。

## 2. 资产

Release 包含：

- `Augit-1.0.0-rc.4-win-x64-setup.exe`（framework-dependent 安装器）
- `Augit-1.0.0-rc.4-win-x64-portable.zip`（self-contained 便携包）
- `SHA256SUMS.txt`

最终资产大小和 SHA-256 以 Release 正文的「资产校验」表和附件 `SHA256SUMS.txt` 为准；发布后核对结果在本文 §6 记录。

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet restore tools\packaging\Augit.Packaging.csproj --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.0.0-rc.4 -OutputDirectory artifacts\release-1.0.0-rc.4
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.4\Augit-1.0.0-rc.4-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.4 -ExpectedVersion 1.0.0-rc.4 -SkipLaunch
```

`tools/release.ps1` 分别构建两个发布目录：

- 安装版：framework-dependent，禁止包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll`，`runtimeconfig.json` 必须使用 `framework: Microsoft.NETCore.App`。
- 便携版：self-contained，必须包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 和 `includedFrameworks: Microsoft.NETCore.App`，不得依赖共享框架。
- self-contained 还原使用独立的 `packages.selfcontained.lock.json` 和 `--locked-mode`，不改写主 `packages.lock.json`。

门禁与结果：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **0 警告 / 0 错误** |
| Core 测试 | **72/72** |
| Shell 测试 | **270/270**（新增首次启动回退测试） |
| Infrastructure 测试 | **207/207**（新增显式写超时测试）；另 4 个 ConPTY 交互用例在托管 runner 排除、在真实桌面/Win10 验证 |
| UI 资产一致性 | **PASS** |
| 安装版 runtimeconfig | framework-dependent，无 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` |
| 便携版 runtimeconfig | self-contained，含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 与 `includedFrameworks` |
| 便携包内容/版本/web 树 | **PASS**（流水线 `-SkipLaunch`） |
| 安装器版本资源 | ProductVersion `1.0.0-rc.4`；FileVersion `1.0.0.0` |
| 安装器签名 | `NotSigned` |

本机预构建（提交 `407cd8f`，仅作发布前同源验证，不是最终 Release 资产）：

- 便携包 `39,134,506 B`，SHA-256 `ac6b62af3e2b7f039cdd7e0072f1af4ef8f2035afbd3c58627e0988be7479fb6`。
- 安装器 `5,016,140 B`，SHA-256 `fc2f9066691eacccfc050a3cd977270049e4779497f11901a54eff3bdbaaffc8`。
- 隐藏共享 .NET（`DOTNET_ROOT` 不存在、禁用多级查找）后便携包真实启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现约 491.1ms、关闭约 99ms、退出码 0、无残留子进程；`includedFrameworks` 为 `Microsoft.NETCore.App 10.0.11`。
- 首次启动（无有效最近目录、当前目录为发布目录）显示「Open Workspace」，不再自动弹出「Create Git Repository」；`%LOCALAPPDATA%\Augit\logs\augit.log` 记录 `startup-workspace=welcome`。
- 报告：`artifacts/release-1.0.0-rc.4-commit-verify/portable-report.json`（Git 忽略）。

## 4. Windows 10 22H2 证据与边界

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载 `30,604,768` 字节的 .NET 10.0.11、校验并安装。

rc.4 的 Win10 22H2 启动路径修复（CETCompat、欢迎页、async void 边界、`git init` 超时）**尚未在该实机复验**；上述证据只覆盖运行代码相同的界面/DPI/安装部分。真实复验结果应在发布后补入本节。

## 5. 与 rc.2/rc.3 的关系

- rc.2 的便携包是 framework-dependent，在没有 .NET 10 的新电脑上会先弹出 .NET 宿主错误。
- rc.3 把便携版改为 self-contained，解决缺 .NET；但在 Windows 10 22H2 上出现首次启动/选择目录后卡住并退出的问题。
- rc.4 在 rc.3 基础上增加 Windows 10 22H2 启动加固、首次启动欢迎页、退出保护和本地诊断日志；便携版仍 self-contained，安装版仍联网安装器。
- rc.2、rc.3 的公开 Release 保留，不覆盖、不改写。

## 6. 发布后核对

发布后应：

- 从 Release 匿名下载 zip、setup、`SHA256SUMS.txt`；
- 核对大小与 SHA-256 和发布正文一致；
- 解压 zip，在隐藏/失效共享 .NET 的环境运行 `Augit.exe`，确认 ProductVersion、窗口和退出；
- 在 Windows 10 22H2 实机复验首次启动、选择目录、创建仓库和正常退出；
- 如条件允许，在干净 Windows 10/11 测试机复验便携与安装/卸载；
- 把实际结果、构建提交和资产哈希回填到本节。

回填状态：**待发布后核对**。
