# Augit 1.0.0-rc.3 公开发布清单

本清单记录 `1.0.0-rc.3` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.3> |
| Release id | `423918886` |
| Tag | `v1.0.0-rc.3` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | `1fee1aace3bb29358426ba37860b420b3eb3d4df` |
| 版本 | `1.0.0-rc.3` |
| 便携版形态 | **self-contained**（自带 .NET 10.0.11 Runtime） |
| 安装版形态 | **framework-dependent**（安装器检测/下载 .NET 10） |
| FileVersion | `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`；.NET SDK 由 `global.json` 锁定；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 安装器 AppId | `{BC6B9385-88A8-4B7D-9F16-4CD4379F1B13}` |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

rc.3 是 rc.2 的分发修复版：运行代码相同，只把便携版改为 self-contained。Release 正文中的资产表由同一次流水线构建的 `SHA256SUMS.txt` 生成。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.3-win-x64-setup.exe` | 5,014,276 B | `814fea114ade1d6cac69642286e53175a63c97981701d2cad9f80bcca370f769` |
| `Augit-1.0.0-rc.3-win-x64-portable.zip` | 39,131,650 B | `92da12b350628cde6bb408ef8a6518a6394fb44a8b55bd455d93f9309ab0f09a` |
| `SHA256SUMS.txt` | 205 B | `8b9950ce8acfe6ec1d4b73f246f0626c138b70e56712aff16fc5f8e9f1cfc6c9` |

`SHA256SUMS.txt`：

```text
92da12b350628cde6bb408ef8a6518a6394fb44a8b55bd455d93f9309ab0f09a  Augit-1.0.0-rc.3-win-x64-portable.zip
814fea114ade1d6cac69642286e53175a63c97981701d2cad9f80bcca370f769  Augit-1.0.0-rc.3-win-x64-setup.exe
```

资产大小和 SHA-256 以 Release 正文的「资产校验」表和附件 `SHA256SUMS.txt` 为准。发布后核对结果在本文 §6 记录。

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet restore tools\packaging\Augit.Packaging.csproj --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.0.0-rc.3 -OutputDirectory artifacts\release-1.0.0-rc.3
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.3\Augit-1.0.0-rc.3-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.3 -ExpectedVersion 1.0.0-rc.3 -SkipLaunch
```

`tools/release.ps1` 现在分别构建两个发布目录：

- 安装版：framework-dependent，禁止包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll`，`runtimeconfig.json` 必须使用 `framework: Microsoft.NETCore.App`。
- 便携版：self-contained，必须包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 和 `includedFrameworks: Microsoft.NETCore.App`，不得依赖共享框架。
- self-contained 还原使用独立的 `packages.selfcontained.lock.json` 和 `--locked-mode`，不改写主 `packages.lock.json`。

门禁与结果：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **0 警告 / 0 错误** |
| Core 测试 | **72/72** |
| Shell 测试 | **268/268** |
| Infrastructure 测试 | **202/202**，另 4 个 ConPTY 交互用例在托管 runner 排除、在真实桌面/Win10 验证 |
| UI 资产一致性 | **PASS** |
| 安装版 runtimeconfig | framework-dependent，无 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` |
| 便携版 runtimeconfig | self-contained，含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 与 `includedFrameworks` |
| 便携包内容/版本/web 树 | **PASS**（流水线 `-SkipLaunch`） |
| 安装器版本资源 | ProductVersion `1.0.0-rc.3`；FileVersion `1.0.0.0` |
| 安装器签名 | `NotSigned` |

本机预构建（提交 `52709ca`，仅作发布前同源验证，不是最终 Release 资产）：

- 便携包 `39,131,614 B`，SHA-256 `00ff43cc4598a6e13732365010c1a7aa8e6f23adcfe16dd31fade01094ad6c36`。
- 安装器 `5,014,217 B`，SHA-256 `578af06be0873f2e2509da1d82c489f988d8712f1765eec9b6716835fcaccc47`。
- 把 `DOTNET_ROOT` 指向不存在目录、禁用多级查找后，便携包仍真实启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现约 496.8ms、关闭约 100ms、退出码 0、无残留子进程；`includedFrameworks` 为 `Microsoft.NETCore.App 10.0.11`。
- 报告：`artifacts/release-1.0.0-rc.3-commit-verify/portable-report.json`（Git 忽略）。

发布后匿名下载**实际 Release 资产**复验：

- 便携包 `39,131,650 B`、SHA-256 `92da12b3…`；安装器 `5,014,276 B`、SHA-256 `814fea11…`，与发布正文和 `SHA256SUMS.txt` 一致。
- 便携包真实启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现 **487.5ms**、关闭 **109ms**、退出码 0、无残留子进程；ProductVersion `1.0.0-rc.3+1fee1aa…`。
- `includedFrameworks` 为 `Microsoft.NETCore.App 10.0.11`；在 `DOTNET_ROOT` 指向不存在目录、禁用多级查找的环境下启动，证明不依赖共享 .NET。
- 安装器 ProductVersion `1.0.0-rc.3`、FileVersion `1.0.0.0`、`NotSigned`。
- 报告：`artifacts/release-1.0.0-rc.3-gh-verify/portable-report.json`（Git 忽略）。

## 4. Windows 10 22H2 证据

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载 `30,604,768` 字节的 .NET 10.0.11、校验并安装。

rc.3 运行代码与 rc.2 相同，仅便携版改为 self-contained；上述界面/DPI 证据继续适用。详见[22H2 多显示器 DPI 修复与扩展验证](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)。

## 5. 与 rc.2 的关系

rc.2 的便携包是 framework-dependent，在没有 .NET 10 的新电脑上会先弹出 .NET 宿主错误，而不是 Augit 自己的提示。rc.3 只修复该分发问题：

- 便携版 self-contained，自带 .NET 10.0.11；新电脑不需要预装 .NET。
- 安装版保持 framework-dependent 的小型联网安装器，.NET 检测/下载逻辑不变。
- rc.2 的公开 Release 保留在 `v1.0.0-rc.2`，不覆盖、不改写。

## 6. 发布后核对

2026-10-26 已完成：

- Release 标记为 **Pre-release**，三个资产匿名可下载。
- 匿名下载的 zip、setup、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致。
- 实际 Release 便携包在隐藏/失效共享 .NET 的环境真实启动 PASS（见 §3）；ProductVersion `1.0.0-rc.3+1fee1aa…`。
- 安装器版本资源与未签名状态已核对；安装/卸载的 Windows 10 22H2 证据见 §4。
- 默认分支 `main`、tag 和发布正文均指向同一版本；README、Release 正文和 `SHA256SUMS.txt` 的下载链接一致。
- 未完成：干净 Windows 虚拟机/全新系统上的便携与安装/卸载复验。该项继续按“未验证”披露，不因本清单 PASS 而写成已通过。

状态：**PASS**（上述未验证项除外）。
