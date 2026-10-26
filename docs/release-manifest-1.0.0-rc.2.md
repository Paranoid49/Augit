# Augit 1.0.0-rc.2 公开发布清单

本清单记录 `1.0.0-rc.2` 公开预发布资产的构建、哈希、发布后匿名下载核对和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.2> |
| Release id | `423876520` |
| Tag | `v1.0.0-rc.2` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | `bada0ebc0f56e81e0767512ae89e38a1527a25fb` |
| 版本 | `1.0.0-rc.2` |
| 便携包 ProductVersion | `1.0.0-rc.2+bada0ebc0f56e81e0767512ae89e38a1527a25fb` |
| 安装器 ProductVersion / FileVersion | `1.0.0-rc.2` / `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`（Windows Server 2025 镜像）；.NET SDK `10.0.201`；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 安装器 AppId | `{BC6B9385-88A8-4B7D-9F16-4CD4379F1B13}` |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

构建提交包含 Windows 10 22H2 多显示器/DPI 修复。Release 正文中的资产表由同一次流水线构建的 `SHA256SUMS.txt` 生成。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.2-win-x64-setup.exe` | 5,013,781 B | `169e35d60c932de5464b47b69b1e0714bdf4e64026448091890833948a7af6db` |
| `Augit-1.0.0-rc.2-win-x64-portable.zip` | 3,526,571 B | `7dc85daf3b6828a4cab1fcc19fcdc2d26aaf3085ce729bf950be12a94c0c8c6e` |
| `SHA256SUMS.txt` | 205 B | `93f57e623f647d5706de68581ade436868d741a11299d06cfb17329d12044bc4` |

`SHA256SUMS.txt`：

```text
7dc85daf3b6828a4cab1fcc19fcdc2d26aaf3085ce729bf950be12a94c0c8c6e  Augit-1.0.0-rc.2-win-x64-portable.zip
169e35d60c932de5464b47b69b1e0714bdf4e64026448091890833948a7af6db  Augit-1.0.0-rc.2-win-x64-setup.exe
```

web 资源清单：`web/` 下 27 个文件，按“文件 SHA-256 + 相对路径”排序后写入 `web-resource-manifest.txt`；该文件字节的 SHA-256 为：

```text
76d8f3d6c8f2016fe4a026ef5b59ad6b4b882709c2c91f167468fba3b5e5532f
```

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

门禁与结果：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **0 警告 / 0 错误** |
| Core 测试 | **72/72** |
| Shell 测试 | **268/268** |
| Infrastructure 测试 | **202/202**，另 4 个 ConPTY 交互用例在托管 runner 排除、在真实桌面/Win10 验证 |
| UI 资产一致性 | **PASS** |
| 便携包内容/版本/web 树 | **PASS**（流水线 `-SkipLaunch`） |
| 安装器版本资源 | ProductVersion `1.0.0-rc.2`；FileVersion `1.0.0.0` |
| 安装器签名 | `NotSigned` |

发布后本机匿名下载实际 Release 资产并复验：

- 便携包 SHA-256 / 大小与 Release 正文和 `SHA256SUMS.txt` 一致。
- 解压启动 PASS：窗口类 `Augit.Shell.Window`；窗口出现 **419.4ms**；2065×1330；关闭 **111ms**；退出码 **0**；无残留子进程。
- ProductVersion `1.0.0-rc.2+bada0ebc0f56e81e0767512ae89e38a1527a25fb`；FileVersion `1.0.0.0`。
- 安装器 ProductVersion `1.0.0-rc.2`、FileVersion `1.0.0.0`；`NotSigned`。
- 报告：`artifacts/release-1.0.0-rc.2-gh-verify/portable-report.json`（Git 忽略）。

## 4. Windows 10 22H2 证据

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载 `30,604,768` 字节的 .NET 10.0.11、校验并安装。

详见[22H2 多显示器 DPI 修复与扩展验证](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)。该页记录的早期源码重建包哈希（`3a9d8276…`、`1a0c2d9a…` 等）是不同构建批次，不是本清单资产。

## 5. 与旧 rc.2 冻结构建的关系

2026-10-25 的旧 rc.2 冻结构建（`8643b9e`）从未上传到 GitHub Release、没有 tag，也没有公开资产。旧文档中的便携包 `3,522,601 B / e339da49…` 与安装器 `5,010,098 B / ecb00134…` 是本机构建记录，无法作为公开资产复核。

本次公开 rc.2 由包含 Win10 22H2 多显示器/DPI 修复的源码重新构建；以 Release 附件的 `SHA256SUMS.txt` 和本清单为唯一哈希基准，不冒充旧哈希。

## 6. 发布后核对

2026-10-26 已完成：

- Release 标记为 **Pre-release**，三个资产匿名可下载。
- 匿名下载的 zip、setup、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致。
- 实际 Release 便携包本机真实启动 PASS（见 §3）。
- 安装器版本资源与未签名状态已核对；安装/卸载的 Windows 10 22H2 证据见 §4。
- `main` 与 tag 均包含本清单；README、Release 正文和 `SHA256SUMS.txt` 的下载链接一致。

状态：**PASS**。
