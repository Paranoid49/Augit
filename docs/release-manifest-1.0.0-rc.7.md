# Augit 1.0.0-rc.7 公开发布清单

本清单记录 `1.0.0-rc.7` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.7> |
| Tag | `v1.0.0-rc.7` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | tag `v1.0.0-rc.7` 指向的提交（`Augit.exe` ProductVersion 内含该提交号） |
| 版本 | `1.0.0-rc.7` |
| 便携版形态 | **self-contained**（自带 .NET 10.0.11 Runtime） |
| 安装版形态 | **framework-dependent**（安装器检测/下载 .NET 10） |
| FileVersion | `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`；.NET SDK 由 `global.json` 锁定；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

rc.7 修复 rc.6 实机使用中的三类 UI 问题：欢迎页在项目窗口打开后未关闭、分支树折叠只改属性不隐藏、分支弹层常驻“当前分支动作”右侧面板。rc.6 的选择目录缓冲区/新窗口参数修复保留。

## 2. 资产

Release 包含：

- `Augit-1.0.0-rc.7-win-x64-setup.exe`（framework-dependent 安装器）
- `Augit-1.0.0-rc.7-win-x64-portable.zip`（self-contained 便携包）
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
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.0.0-rc.7 -OutputDirectory artifacts\release-1.0.0-rc.7
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.7\Augit-1.0.0-rc.7-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.7 -ExpectedVersion 1.0.0-rc.7 -SkipLaunch
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
| Shell 测试 | **271/271** |
| Infrastructure 测试 | **207/207**；另 4 个 ConPTY 交互用例在托管 runner 排除、在真实桌面/Win10 验证 |
| UI 资产一致性 | **PASS** |
| 安装版 runtimeconfig | framework-dependent，无 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` |
| 便携版 runtimeconfig | self-contained，含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 与 `includedFrameworks` |
| 便携包内容/版本/web 树 | **PASS**（流水线 `-SkipLaunch`） |
| 安装器版本资源 | ProductVersion `1.0.0-rc.7`；FileVersion `1.0.0.0` |
| 安装器签名 | `NotSigned` |

本机预构建（提交 `0b80fe5`，仅作发布前同源验证，不是最终 Release 资产）：

- 便携包 `39,135,205 B`，SHA-256 `dfac08b7f75923c7ad808f82ec5b3400446d0b94ddedfa387cb9cf3b8e68f9ea`。
- 安装器 `5,017,559 B`，SHA-256 `5ab4a46652bbcb17c8d8d408c4d48f1e89627c9955345fef499174fae9b6a0c0`。
- 隐藏共享 .NET（`DOTNET_ROOT` 不存在、禁用多级查找）后便携包真实启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现约 488ms、关闭约 103ms、退出码 0、无残留子进程；`includedFrameworks` 为 `Microsoft.NETCore.App 10.0.11`。
- **欢迎页选择目录**：CDP 点击 Choose Directory → 系统选择框 → `D:\tmp\augit-branch-ui`；欢迎进程退出，目标项目窗口启动，最终只有 **1 个** Augit 进程；日志为 `startup args=--workspace D:\tmp\augit-branch-ui`。
- **分支树折叠**：点击 `Local` 后 `codex`/`feature` 等行 `display: none`、高度 0，只保留 HEAD 与 `Tags`；`Local/codex` 单独折叠同样生效。
- **分支弹层**：打开 branch chip 后 DOM 中 `.branch-actions` 数量为 **0**，只有分支/标签列表。
- **欢迎页静置 12 秒**：无 `Cannot Read Workspace Changes` toast；欢迎页不启动 `workspace/changes`。
- 报告：`artifacts/release-1.0.0-rc.7-commit-verify/portable-report.json`（Git 忽略）。

## 4. Windows 10 22H2 证据与边界

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载 `30,604,768` 字节的 .NET 10.0.11、校验并安装。

rc.7 的欢迎页/分支树/分支弹层修复**尚未在该实机复验**；上述证据只覆盖运行代码相同的界面/DPI/安装部分。Windows 11 已完成本轮回归，但 Win10 22H2 仍需用户实测。

## 5. 与 rc.2–rc.6 的关系

- rc.2 便携版 framework-dependent，在没有 .NET 10 的新电脑上会先弹出 .NET 宿主错误。
- rc.3 把便携版改为 self-contained。
- rc.4 增加 Windows 10 22H2 启动加固、欢迎页、退出保护和本地诊断日志。
- rc.5 修复旧 `LastWorkspace` 等于程序目录/欢迎目录时仍恢复该目录。
- rc.6 修复选择目录的 `comctl32` 崩溃和新窗口 `--workspace` 参数错误。
- rc.7 修复欢迎页双窗口、分支树折叠和分支弹层默认右侧面板。
- rc.2–rc.6 的公开 Release 保留，不覆盖、不改写。

## 6. 发布后核对

发布后应：

- 从 Release 匿名下载 zip、setup、`SHA256SUMS.txt`；
- 核对大小与 SHA-256 和发布正文一致；
- 解压 zip，在隐藏/失效共享 .NET 的环境运行 `Augit.exe`，确认 ProductVersion、窗口和退出；
- 在欢迎页 → Choose Directory → 目标目录，确认最终只有一个项目窗口；
- 在 Git Log 分支树点击 `Local`/`codex`，确认折叠后行实际消失；打开 branch chip 确认无右侧常驻动作面板；
- 在 Windows 10 22H2 实机复验首次启动、选择目录、创建仓库和正常退出；
- 把实际结果、构建提交和资产哈希回填到本节。

回填状态：**待发布后核对**。
