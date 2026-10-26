# Augit 1.0.0-rc.6 发布说明

- 发布日期：2026-10-26
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：`c7c0b5b0a35ba119a5a78da00d0f1c00000717d2`（GitHub Actions 从 tag 构建；`Augit.exe` ProductVersion 内含该提交号）
- 发布记录 / tag：`v1.0.0-rc.6`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

rc.6 修复两个已确认的 Windows 10/11 选择目录问题：

1. **选择目录后程序消失**：`FolderPicker` 把 `BROWSEINFO.pszDisplayName` 传成 `string.Empty`。该字段是系统写入的 `MAX_PATH` 输出缓冲区，不是只读输入；`SHBrowseForFolder` 在用户确认后写入不可变字符串，在 `comctl32.dll` 内以 `c0000005` 崩溃。Windows 事件日志中的 `Application Error`/`.NET Runtime` 已直接定位到 `Augit.Shell.FolderPicker.SHBrowseForFolder`。
2. **选择目录后弹出 “Augit could not start … ArgumentException (0x80070057)”**：新窗口启动器把工作区路径作为**位置参数**传递，但 `ShellOptions.Parse` 只接受 `--workspace`，于是新进程以“未知的启动参数：D:\...”退出。现在启动器显式传 `--workspace <path>`，同时命令行也正式支持 `augit <目录>`。

rc.5 的首次启动欢迎页、旧 `LastWorkspace` 回退、CETCompat、async void 边界、`git init` 超时和本地诊断日志全部保留。

## 2. 下载

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| [Augit-1.0.0-rc.6-win-x64-setup.exe](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.6/Augit-1.0.0-rc.6-win-x64-setup.exe) | 5,017,116 B | `17bb8641992583dc0a05b41aa7cf7cb99ef06e377443dd8380d280421a28165f` |
| [Augit-1.0.0-rc.6-win-x64-portable.zip](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.6/Augit-1.0.0-rc.6-win-x64-portable.zip) | 39,134,632 B | `a06c44c233ed3f034ffa247eaf09b52606c565834ce5072b35257805d1d9f84a` |
| [SHA256SUMS.txt](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.6/SHA256SUMS.txt) | 205 B | `4c3f09e86b6186d3c821ace73a781b4a333203a5b7fba3801ecbd9c6c918abe0` |

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.6>

- 上表与 Release 正文、附件 `SHA256SUMS.txt` 一致；发布后已匿名下载复核。
- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只要求系统已有 Microsoft Edge WebView2 Runtime。
- **安装版**：按需联网的小型安装器，需要管理员权限；缺少 .NET 10/WebView2 时先说明，经确认后下载并校验。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置。

## 3. rc.6 的变化

### 3.1 修复选择目录后的 `comctl32` 崩溃

- `BROWSEINFO.pszDisplayName` 改为显式非托管 `MAX_PATH` 缓冲区（`Marshal.AllocHGlobal`），确认后释放；不再把 `string.Empty` 交给系统写入。
- 保留 `BIF_NEWDIALOGSTYLE | BIF_EDITBOX` 的现代选择框，不退回旧式树对话框。
- 复现证据：修复前在同一路径选择后，Event Log 记录 `Augit.exe`/`comctl32.dll` `c0000005`，`.NET Runtime` 栈为 `FolderPicker.SHBrowseForFolder`；修复后同一操作正常返回，欢迎窗口保留并启动目标窗口。

### 3.2 修复新窗口 `ArgumentException (0x80070057)`

- `ExternalProgramLauncher.CreateAugitWorkspaceStartInfo` 现在传 `--workspace` + 绝对路径。
- `ShellOptions.Parse` 支持单个位置参数：`augit .`、`augit D:\repo`；重复/未知参数仍抛稳定 `ArgumentException`。
- 集成测试直接解析启动器生成的 `ArgumentList`，防止再次出现“启动器与解析器契约不一致”。

### 3.3 保留 rc.5 的启动修复

- 首次启动/旧 `LastWorkspace` 等于程序目录或欢迎目录时显示「Open Workspace」。
- `CETCompat=false`、async void 异常边界、`%LOCALAPPDATA%\Augit\logs\augit.log`、`git init` 30 秒超时与本地路径校验。
- 便携版 self-contained；安装版 framework-dependent 联网安装器。

## 4. 验证摘要

- Release 构建：0 警告、0 错误。
- 测试：Core **72/72**、Shell **271/271**（新增启动器/解析器集成测试）、Infrastructure **207/207**（启动器参数契约更新）；托管 CI 排除 4 个需要交互式控制台的 ConPTY 用例。
- 发布后匿名下载实际 Release 便携包，隐藏共享 .NET（`DOTNET_ROOT` 不存在）启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现 **498.5ms**、关闭 **101.1ms**、退出码 0、无残留子进程；ProductVersion `1.0.0-rc.6+c7c0b5b…`。
- **真实选择目录复验（Windows 11，实际 Release 包）**：欢迎页 → Choose Directory → `D:\project\ops-server-report-resend` → 系统选择框正常返回，欢迎窗口仍在，目标目录在新窗口启动；日志为 `startup args=--workspace D:\project\ops-server-report-resend`、`options workspace=D:\project\ops-server-report-resend`；事件日志无新的 `Application Error`/`.NET Runtime` Augit 崩溃。
- **Windows 10 22H2 实机复验**：rc.6 仍需在用户出问题的机器上验证；rc.2/rc.3 的双屏/DPI/安装证据不覆盖该启动路径。
- 仍未验证：干净虚拟机、真实缺少 WebView2 的安装流程、三块及以上显示器、物理拔插显示器。

## 5. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.6.md)。重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- Win10 22H2 实机复验待完成；若仍异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段或 Windows 应用程序日志中的 `Application Error`/`.NET Runtime` 事件。
- 干净虚拟机、真实缺 WebView2、三屏/物理拔线未验证。
- 整窗像素历史结果仍为 0 张合格 / 50 组页面级配对；500ms 历史慢样本、Settings 567ms、80,000 行软换行、离屏全文连续朗读等边界继续披露。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 6. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
