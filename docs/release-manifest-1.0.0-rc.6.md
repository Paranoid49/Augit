# Augit 1.0.0-rc.6 公开发布清单

本清单记录 `1.0.0-rc.6` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.6> |
| Release id | `424002687` |
| Tag | `v1.0.0-rc.6` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | `c7c0b5b0a35ba119a5a78da00d0f1c00000717d2` |
| 版本 | `1.0.0-rc.6` |
| 便携版形态 | **self-contained**（自带 .NET 10.0.11 Runtime） |
| 安装版形态 | **framework-dependent**（安装器检测/下载 .NET 10） |
| FileVersion | `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`；.NET SDK 由 `global.json` 锁定；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

rc.6 修复两个已确认的选择目录缺陷：

1. `BROWSEINFO.pszDisplayName` 被传成 `string.Empty`，系统确认目录后写入不可变字符串，在 `comctl32.dll` 内以 `c0000005` 崩溃。
2. 新窗口启动器把工作区路径作为位置参数，`ShellOptions.Parse` 只接受 `--workspace`，新进程以 `ArgumentException (0x80070057)` 退出。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.6-win-x64-setup.exe` | 5,017,116 B | `17bb8641992583dc0a05b41aa7cf7cb99ef06e377443dd8380d280421a28165f` |
| `Augit-1.0.0-rc.6-win-x64-portable.zip` | 39,134,632 B | `a06c44c233ed3f034ffa247eaf09b52606c565834ce5072b35257805d1d9f84a` |
| `SHA256SUMS.txt` | 205 B | `4c3f09e86b6186d3c821ace73a781b4a333203a5b7fba3801ecbd9c6c918abe0` |

`SHA256SUMS.txt`：

```text
a06c44c233ed3f034ffa247eaf09b52606c565834ce5072b35257805d1d9f84a  Augit-1.0.0-rc.6-win-x64-portable.zip
17bb8641992583dc0a05b41aa7cf7cb99ef06e377443dd8380d280421a28165f  Augit-1.0.0-rc.6-win-x64-setup.exe
```

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet restore tools\packaging\Augit.Packaging.csproj --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.0.0-rc.6 -OutputDirectory artifacts\release-1.0.0-rc.6
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.6\Augit-1.0.0-rc.6-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.6 -ExpectedVersion 1.0.0-rc.6 -SkipLaunch
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
| Shell 测试 | **271/271**（新增启动器/解析器集成测试） |
| Infrastructure 测试 | **207/207**（启动器参数契约更新）；另 4 个 ConPTY 交互用例在托管 runner 排除、在真实桌面/Win10 验证 |
| UI 资产一致性 | **PASS** |
| 安装版 runtimeconfig | framework-dependent，无 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` |
| 便携版 runtimeconfig | self-contained，含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 与 `includedFrameworks` |
| 便携包内容/版本/web 树 | **PASS**（流水线 `-SkipLaunch`） |
| 安装器版本资源 | ProductVersion `1.0.0-rc.6`；FileVersion `1.0.0.0` |
| 安装器签名 | `NotSigned` |

发布后匿名下载**实际 Release 资产**复验：

- 便携包 `39,134,632 B`、SHA-256 `a06c44c2…`；安装器 `5,017,116 B`、SHA-256 `17bb8641…`，与发布正文和 `SHA256SUMS.txt` 一致。
- 便携包真实启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现 **498.5ms**、关闭 **101.1ms**、退出码 0、无残留子进程；ProductVersion `1.0.0-rc.6+c7c0b5b…`。
- `includedFrameworks` 为 `Microsoft.NETCore.App 10.0.11`；在 `DOTNET_ROOT` 指向不存在目录、禁用多级查找的环境下启动，证明不依赖共享 .NET。
- **真实选择目录流程（实际 Release 包，Windows 11）**：欢迎页 → Choose Directory → `D:\project\ops-server-report-resend` → 系统选择框正常返回；欢迎窗口仍在，目标目录在新窗口启动；日志为 `startup args=--workspace D:\project\ops-server-report-resend`、`options workspace=D:\project\ops-server-report-resend`。事件日志无新的 Augit `Application Error`/`.NET Runtime` 事件。
- 修复前同一路径的 Event Log 证据：`comctl32.dll` `c0000005`、偏移 `0xf0873`；`.NET Runtime` 栈为 `Augit.Shell.FolderPicker.SHBrowseForFolder`。
- 安装器 ProductVersion `1.0.0-rc.6`、FileVersion `1.0.0.0`、`NotSigned`。
- 报告：`artifacts/release-1.0.0-rc.6-gh-verify/portable-report.json`（Git 忽略）。

本机预构建（提交 `f62a640`，仅作发布前同源验证）：

- 便携包 `39,134,628 B`，SHA-256 `6b73c4b9c7ace8a96fd1efb95860019e49891de27fbebc76c0d9d1dd47676038`。
- 安装器 `5,017,466 B`，SHA-256 `1377473611ecfc05fe4eb5136d4e7918f5d1f179889c57b7eb43799b4bd4f48a`。

## 4. Windows 10 22H2 证据与边界

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载 `30,604,768` 字节的 .NET 10.0.11、校验并安装。

rc.6 的选择目录/启动路径修复**尚未在该实机复验**；上述证据只覆盖运行代码相同的界面/DPI/安装部分。Windows 11 的复现已确认 `comctl32` 崩溃机制和修复后行为，但 Win10 22H2 仍需用户实测。

## 5. 与 rc.2–rc.5 的关系

- rc.2 便携版 framework-dependent，在没有 .NET 10 的新电脑上会先弹出 .NET 宿主错误。
- rc.3 把便携版改为 self-contained。
- rc.4 增加 Windows 10 22H2 启动加固、欢迎页、退出保护和本地诊断日志。
- rc.5 修复旧 `LastWorkspace` 等于程序目录/欢迎目录时仍恢复该目录。
- rc.6 修复选择目录的 `comctl32` 崩溃和新窗口 `--workspace` 参数错误。
- rc.2–rc.5 的公开 Release 保留，不覆盖、不改写。

## 6. 发布后核对

2026-10-26 已完成：

- Release 标记为 **Pre-release**，三个资产匿名可下载。
- 匿名下载的 zip、setup、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致。
- 实际 Release 便携包在隐藏/失效共享 .NET 的环境真实启动 PASS（见 §3）。
- 实际 Release 包的真实选择目录流程 PASS：系统选择框不崩溃，目标目录在新窗口启动；无新的 Augit 崩溃事件。
- 安装器版本资源与未签名状态已核对。
- 默认分支 `main`、tag 和发布正文均指向同一版本；README、Release 正文和 `SHA256SUMS.txt` 的下载链接一致。
- **未完成**：在 Windows 10 22H2 出问题机器上复验 rc.6 的首次启动/选择目录/创建仓库路径；干净虚拟机/全新系统安装也未验证。两项继续按“未验证”披露。

状态：**PASS**（Win10 22H2 启动路径与干净安装复验除外）。
