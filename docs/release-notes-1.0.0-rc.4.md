# Augit 1.0.0-rc.4 发布说明

- 发布日期：2026-10-26
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：tag `v1.0.0-rc.4` 指向的提交（由 GitHub Actions 构建；`Augit.exe` ProductVersion 内含提交号）
- 发布记录 / tag：`v1.0.0-rc.4`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

Augit 是面向「代码由外部 AI、脚本或人工修改」这一协作方式的轻量 Windows 只读项目查看器与 Git 图形工具。界面使用 C#/.NET 10 原生 Win32 外壳 + WebView2，不接入 AI，不提供内置文本编辑和保存。

rc.4 是 **Windows 10 22H2 启动/选择目录退出的加固版**，建立在 rc.3 的 self-contained 便携包之上。它不改变产品功能或 Git 行为，新增启动兼容性、异常边界、诊断日志和 `git init` 超时。

## 2. 下载

<!-- RELEASE_ASSETS -->

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.4>

- 上表由发布流水线从本次实际构建的 `SHA256SUMS.txt` 生成；下载后请以 Release 附件中的 `SHA256SUMS.txt` 复核。
- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只要求系统已有 Microsoft Edge WebView2 Runtime。不写系统 PATH，不添加资源管理器菜单，不安装或修改共享运行时。
- **安装版**：按需联网的小型安装器，需要管理员权限；缺少 .NET 10/WebView2 时先说明，经确认后从项目锁定的微软官方地址下载并校验哈希与 Microsoft 签名。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置、最近目录和会话状态。

## 3. rc.4 的变化

### 3.1 Windows 10 兼容性

- 发布 apphost 显式设置 `<CETCompat>false</CETCompat>`。Windows 10 部分机器/补丁级别对 CET（影子栈）支持不完整；默认开启 CET 的 .NET apphost 可能在启动或首次进入新代码路径时以 `0x80131506` 退出。此开关只影响 apphost 的 CET 兼容标记，不改变 .NET 运行时、技术栈或产品功能。
- 这是对 Windows 10 22H2 支持范围的兼容性修复，不是降低 Windows 安全更新要求。

### 3.2 首次启动不再把程序目录当工作区

- 双击便携包或开始菜单快捷方式时，Windows 会把当前目录设为程序目录。rc.3 会直接把 `D:\Augit` 之类的发布目录当工作区，展示 `Augit.exe`、`coreclr.dll` 等二进制，并进入“创建 Git 仓库”状态。
- rc.4 在没有有效最近目录、且当前目录等于程序目录时，改用 `%LOCALAPPDATA%\Augit\Welcome` 空目录，并在首屏直接显示「Open Workspace」启动页，而不是因为空目录不是 Git 仓库就自动弹出「Create Git Repository」。
- 用户通过「打开目录」选择真实项目；显式 `--workspace`（包括 `augit .`）不受影响。

### 3.3 退出保护与本地诊断

- `OnWebMessageReceived`/`OnNavigationCompleted` 等 async void 事件处理器增加最外层异常边界：处理器内部异常只写本地日志，不再把异常抛回消息循环导致进程终止。
- 新增 `%LOCALAPPDATA%\Augit\logs\augit.log`：记录启动版本、系统、参数、工作区、桥接异常、工作区/Git watcher 初始化异常和 WebView2 进程失败。日志只在本机，不上传，不包含文件内容或 Git 输出/凭据；超过 4 MB 轮换为 `augit.log.1`。
- 如果 rc.4 仍异常退出，请把 `%LOCALAPPDATA%\Augit\logs\augit.log` 的最后一段附到 Issue；若进程直接消失且日志为空，请同时提供 Windows 事件查看器「Windows 日志 > 应用程序」中 `Application Error`/`.NET Runtime` 的事件。

### 3.4 `git init` 边界

- `git/init` 现在先校验目标必须是本机固定磁盘上可访问的本地目录，拒绝网络/远程/无效路径。
- `git init` 增加 **30 秒**显式超时；超时后结束进程并返回可读失败，不再无限停在“Initializing…”。
- `InspectAsync` 仍使用 30 秒查询超时。

## 4. 与前序版本的关系

- rc.3 修复了“新电脑便携版缺 .NET”的问题：便携包 self-contained。
- rc.4 修复/加固 Windows 10 22H2 上“首次启动选择目录后卡住并退出”的问题；便携版仍是 self-contained，安装版仍是联网安装器。
- 运行代码在 rc.4 中新增的是启动/异常边界和 `git init` 超时，不改变文件浏览、Git、终端或 Settings 的产品行为。
- rc.2、rc.3 的公开 Release 保留，不覆盖、不改写。

## 5. 验证摘要

- Release 构建：0 警告、0 错误。
- 测试：Core **72/72**、Shell **270/270**、Infrastructure **207/207**（新增首次启动回退与显式写超时测试；托管 CI 排除 4 个需要交互式控制台的 ConPTY 用例）。
- UI 资产字节一致性、PowerShell 编码、文档数字自洽、coverage generator 检查通过。
- 本机 self-contained 便携包启动 PASS（隐藏 `DOTNET_ROOT`）：窗口类 `Augit.Shell.Window`、退出码 0、无残留子进程；`includedFrameworks` 为 `Microsoft.NETCore.App 10.0.11`。
- **Windows 10 22H2 实机复验**：rc.4 的 CET/首次启动/退出保护需要在用户出问题的机器上复验；rc.3 的 Windows 10 22H2 双屏/DPI/安装证据仅覆盖运行代码相同的部分，不覆盖本版的启动路径修复。
- 仍未验证：干净虚拟机、真实缺少 WebView2 的安装流程、三块及以上显示器、物理拔插显示器。

## 6. 系统要求

- Windows 10 22H2 x64 或 Windows 11 x64（不支持 32 位、ARM64、Windows Server、Windows 预览版、网络共享目录和远程文件系统）。
- **便携版**：自带 .NET 10.0.11 Runtime，无需预装 .NET。
- **安装版**：需要 .NET 10 Runtime；缺少时安装器先说明，经确认后下载锁定的微软运行时并校验哈希与 Microsoft 签名。
- Microsoft Edge WebView2 Runtime（Evergreen），两种包都需要。
- Git for Windows 2.40 或更高版本（仅 Git 功能需要）；Augit 不捆绑 Git。

## 7. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.4.md)。重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- 本次 Windows 10 22H2 修复需要在用户机器上复验；干净虚拟机、真实缺 WebView2、三屏/物理拔线未验证。
- 整窗像素历史结果仍为 **0 张合格 / 50 组页面级配对**；只登记持续改进。
- 500ms 历史慢样本未归因；Settings 冷开 567ms；80,000 行软换行全量 DOM；离屏全文连续朗读受限。
- Diff 有 10MB/20MB 与 2000 行实现上限；搜索超时仅受控慢条件验证；Push 认证覆盖按专项记录限定。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 8. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；若异常退出，附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段或 Windows 应用程序日志中的崩溃事件。Augit 不做遥测/崩溃上报，不会自动上传代码、日志或使用数据。
