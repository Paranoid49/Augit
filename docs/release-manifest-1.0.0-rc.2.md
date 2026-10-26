# Augit 1.0.0-rc.2 公开发布清单

本清单记录 `1.0.0-rc.2` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.2> |
| Tag | `v1.0.0-rc.2` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | tag `v1.0.0-rc.2` 指向的提交（`Augit.exe` ProductVersion 内含该提交号） |
| 版本 | `1.0.0-rc.2` |
| FileVersion | `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`；.NET SDK 由 `global.json` 锁定；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 安装器 AppId | `{BC6B9385-88A8-4B7D-9F16-4CD4379F1B13}` |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

构建提交包含 Windows 10 22H2 多显示器/DPI 修复。Release 正文中的资产表由同一次流水线构建的 `SHA256SUMS.txt` 生成；不要在发布前把本机预构建哈希写成公开资产哈希。

## 2. 资产

Release 包含：

- `Augit-1.0.0-rc.2-win-x64-setup.exe`
- `Augit-1.0.0-rc.2-win-x64-portable.zip`
- `SHA256SUMS.txt`

资产大小和 SHA-256 以 Release 正文的「资产校验」表和附件 `SHA256SUMS.txt` 为准。发布后核对结果在本文 §6 记录。

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet restore tools\packaging\Augit.Packaging.csproj --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.0.0-rc.2 -OutputDirectory artifacts\release-1.0.0-rc.2
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.2\Augit-1.0.0-rc.2-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.2 -ExpectedVersion 1.0.0-rc.2 -SkipLaunch
```

门禁：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **0 警告 / 0 错误** |
| Core 测试 | **72/72** |
| Shell 测试 | **268/268** |
| Infrastructure 测试 | **206/206**，另 4 个 ConPTY 交互用例在托管 runner 排除、在真实桌面/Win10 验证 |
| UI 资产一致性 | **PASS** |
| 便携包内容/版本/web 树 | **PASS**（流水线 `-SkipLaunch`，无交互桌面启动） |
| 安装器版本资源 | ProductVersion `1.0.0-rc.2+<构建提交>`；FileVersion `1.0.0.0` |
| 安装器签名 | `NotSigned` |

本机预构建（提交 `ff1795b`，仅作发布前同源验证，不是最终 Release 资产）曾完成真实便携启动：窗口类 `Augit.Shell.Window`、窗口出现 353.4ms、关闭 112.5ms、退出码 0、无残留子进程；截图与报告在 `artifacts/release-1.0.0-rc.2-public-verify/`（Git 忽略）。

## 4. Windows 10 22H2 证据

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载 `30,604,768` 字节的 .NET 10.0.11、校验并安装。

详见[22H2 多显示器 DPI 修复与扩展验证](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)。该页记录的早期源码重建包哈希（`3a9d8276…`、`1a0c2d9a…` 等）是不同构建批次，不是本清单资产。

## 5. 与旧 rc.2 冻结构建的关系

2026-10-25 的旧 rc.2 冻结构建（`8643b9e`）从未上传到 GitHub Release、没有 tag，也没有公开资产。旧文档中的便携包 `3,522,601 B / e339da49…` 与安装器 `5,010,098 B / ecb00134…` 是本机构建记录，无法作为公开资产复核。

本次公开 rc.2 由包含 Win10 22H2 多显示器/DPI 修复的源码重新构建；以 Release 附件的 `SHA256SUMS.txt` 和发布正文表格为唯一哈希基准，不冒充旧哈希。

## 6. 发布后核对

发布后应：

- 从 Release 匿名下载 zip、setup、`SHA256SUMS.txt`；
- 核对大小与 SHA-256 和发布正文一致；
- 解压 zip，运行 `Augit.exe`，确认 ProductVersion、窗口和退出；
- 如条件允许，在 Windows 10/11 测试机复验安装/卸载；
- 把实际结果、构建提交和资产哈希回填到本节。

回填状态：**待发布后核对**。
