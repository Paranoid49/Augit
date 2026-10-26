# Augit

[![CI](https://github.com/Paranoid49/Augit/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Paranoid49/Augit/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Paranoid49/Augit?include_prereleases&label=release)](https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.5)

Augit 是面向「代码由外部 AI、脚本或人工修改」这一协作方式的轻量 Windows 只读项目查看器与 Git 图形工具。开发者在 Augit 中浏览项目、检查变化并执行 Git 操作；Augit 不接入 AI，也不提供普通代码编辑和保存。

当前公开发布为 **1.0.0-rc.5 预发布候选**（GitHub Pre-release），**不是正式 1.0**。候选包需要完成真实日常使用观察后才进入正式发布。本 README 随安装包与便携包一起分发；实际版本以安装器标题、Windows「设置 > 应用 > 已安装的应用」或 `Augit.exe` 文件属性中的产品版本为准。

## 下载与安装（无需从源码构建）

| 方式 | 下载 | 适合场景 |
| --- | --- | --- |
| 安装版 | [Augit-1.0.0-rc.5-win-x64-setup.exe](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.5/Augit-1.0.0-rc.5-win-x64-setup.exe) | 长期使用；需要开始菜单、系统 PATH 或资源管理器集成时 |
| 便携版 | [Augit-1.0.0-rc.5-win-x64-portable.zip](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.5/Augit-1.0.0-rc.5-win-x64-portable.zip) | 解压即用；不想安装或没有管理员权限时 |
| 校验和 | [SHA256SUMS.txt](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.5/SHA256SUMS.txt) | 下载后核对文件完整性 |

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.5>

变更记录：<https://github.com/Paranoid49/Augit/blob/main/CHANGELOG.md>

- 安装版需要管理员权限；安装器会先检测 .NET 10 Runtime 和 WebView2 Runtime，缺少时列出缺失项，经用户确认后才从项目锁定的微软官方地址下载，并校验固定哈希与 Microsoft Corporation 数字签名。
- 便携版自带 .NET 10 Runtime，解压后直接运行 `Augit.exe`，不写系统 PATH、不添加资源管理器菜单，也不会安装或修改共享运行时；仅 WebView2 Runtime 需要系统已有，缺少时会提示并给出微软官方下载入口。
- 安装版与便携版共用 `%LOCALAPPDATA%\Augit` 下的个人设置、最近目录和会话状态。
- **未做代码签名**：Augit 可执行文件与安装包没有 Augit 自身代码签名，Windows SmartScreen 可能提示「未知发布者」。这不是安装包损坏；如不放心，请先核对 `SHA256SUMS.txt`。

## 系统要求

- Windows 10 22H2 x64 或 Windows 11 x64。不支持 32 位、ARM64、Windows Server、Windows 预览版、网络共享目录和远程文件系统。
- **安装版**：需要 .NET 10 Runtime（`Microsoft.NETCore.App`）。缺少时安装器会说明并给出官方下载；本机实测宿主提示文案为 “.NET Desktop Runtime”，安装任一 .NET 10 x64 运行时即可。
- **便携版**：自带 .NET 10 Runtime（self-contained），**不需要预装 .NET**；只需系统已有 Microsoft Edge WebView2 Runtime。
- Microsoft Edge WebView2 Runtime（Evergreen）。主界面本身由 WebView2 承载，缺少时无法加载界面。
- 使用 Git 功能时需要 Git for Windows 2.40 或更高版本。Augit 不捆绑 Git，优先使用设置中指定的 `git.exe`，否则使用系统 `PATH` 中的 Git。

## 验证状态

本候选已在以下真实机器上完成安装/便携运行验证；未验证项不写成已通过：

- **Windows 11 x64**：主界面、安装/升级/卸载、便携包、屏幕阅读器与性能基线。
- **Windows 10 22H2 x64（`10.0.19045.6456`）**：双显示器、真实鼠标跨屏拖动、混合 DPI 启动、100%/125%/150%/175%/200% 缩放、负坐标显示器布局、模拟显示器热插拔、深色窄窗，以及 rc.2 源码重建包的便携运行与静默安装。rc.3 改为 self-contained 便携包；rc.4/rc.5 增加 Windows 10 22H2 首次启动/CET/异常边界加固与旧 LastWorkspace 回退，该启动路径仍待在用户出问题的 22H2 机器上复验。验证记录见 [`docs/win10-22h2-multimonitor-dpi-fixes-2026-10-26.md`](https://github.com/Paranoid49/Augit/blob/main/docs/win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)。
- 本次公开 rc.5 资产由 GitHub Actions 从 `v1.0.0-rc.5` 标签构建，并包含上述 Windows 10/DPI 修复；2026-10-25 的旧 rc.2 冻结构建从未公开上传，其原始哈希无法复核。本 Release 中的资产与 `SHA256SUMS.txt` 是发布基准。
- rc.5 便携包在本机隐藏共享 .NET 后仍能启动（self-contained），并已验证首次启动显示「Open Workspace」而不是把程序目录当项目；真实全新 Windows 虚拟机仍待复验。

仍未验证：干净虚拟机安装、真实缺少 WebView2 的安装流程、三块及以上显示器、物理拔插显示器、更多 DPI 组合、Narrator/JAWS/Braille 完整会话，以及足够时长的真实日常使用观察。

## 安装版：安装、升级与卸载

- 安装版需要管理员权限，默认安装到 `%ProgramFiles%\Augit`。
- 安装过程中可以按需选择「将 Augit 加入系统 PATH」和「在资源管理器中增加“使用 Augit 打开”」。两项默认不勾选；只有安装器自己加入的 PATH 项会在卸载时移除。
- 安装器先检测 .NET 10 Runtime 和 WebView2 Runtime。存在缺失项时，安装器先列出缺失内容，经用户确认后才从项目锁定的微软官方地址下载；下载文件通过固定哈希和 Microsoft Corporation 数字签名校验后才会执行。拒绝确认、取消下载或校验失败时，Augit 不会安装，也不会留下不完整的程序目录。
- 覆盖升级：先正常关闭 Augit，再运行新版本安装器并安装到原目录。安装器沿用上一次的 PATH 与资源管理器任务选择，并保留 `%LOCALAPPDATA%\Augit` 中的个人设置、最近目录和会话状态。
- 卸载：在 Windows「设置 > 应用 > 已安装的应用」中找到 Augit 并执行卸载。卸载器删除程序文件、开始菜单快捷方式、它加入的系统 PATH 项和资源管理器右键菜单，不会删除工作区内容、Git 配置或 `%LOCALAPPDATA%\Augit` 中的个人设置，也不会卸载共享的 .NET 与 WebView2 运行时。
- 安装包是按需联网的小型安装器，不提供包含完整运行时的离线安装包。

## 便携版：解压即用

- 把 `Augit-1.0.0-rc.5-win-x64-portable.zip` 解压到任意本地目录，运行其中的 `Augit.exe`。
- 便携版已内置 .NET 10 Runtime（self-contained），新电脑无需预装 .NET；不写入系统 PATH，不添加资源管理器菜单，也不会安装或修改共享运行时。
- 首次启动没有可用最近目录时显示「Open Workspace」启动页，不会把 `Augit.exe` 所在的发布目录当成项目；请通过「打开目录」选择真实工作区。
- 仍需系统已有 Microsoft Edge WebView2 Runtime。缺少 WebView2 时 Augit 会明确提示缺失项，并给出微软官方下载入口；补齐后重新运行即可。
- 便携版与安装版共用 `%LOCALAPPDATA%\Augit` 下的个人设置；不会向被浏览的仓库写入工具配置。

## 打开目录

- 在界面中选择「打开目录」或「最近目录」。
- 命令行使用 `augit .` 打开当前目录，或 `augit <目录>` 打开指定目录（安装时加入 PATH 后可用）。
- 安装时选择资源管理器集成后，可在目录（含目录背景）右键菜单中选择「使用 Augit 打开」。

同一目录只保留一个 Augit 窗口；再次打开同一目录时会激活已有窗口并退出新进程。

## 只读边界

- 普通文件、Markdown、JSON、Diff、提交历史和 Blame 均为只读；Augit 不提供内置文本编辑和保存。
- 冲突文件同样只提供冲突状态、文件列表和普通只读 Diff。冲突文本由用户已有的外部编辑器或终端修改；Augit 在外部处理完成后同步状态，并提供 Continue / Skip / Abort。
- 文本文件上限 10 MB；超过上限不读取正文，只显示文件信息并允许用系统默认程序打开。
- 文本 Diff 仅在两侧内容都不超过 10 MB 且生成的 Diff 不超过 20 MB 时展示完整正文；超过上限时只显示文件状态和两侧大小。
- 图片预览只支持 PNG、JPEG/JPG 和 BMP，不支持 GIF、WebP。Markdown 预览不渲染原生 HTML。
- 不提供 Force Push、Git 子模块、GitHub/GitLab 平台接口、内置 Git Console、多 Changelist、代码补全、调试器、插件市场或 AI 集成。
- 不做遥测、崩溃上报、自动更新检查，不启动常驻服务；只有用户主动执行 Git 操作时，才由本机 Git 修改工作区。

## Git 与认证

- 所有 Git 命令都以当前工作区为执行目录，使用该仓库自己的 `.git`、仓库配置、hooks、凭据和 SSH 环境。
- 认证交给 Git Credential Manager、SSH agent 或终端中已有的登录方式。Augit 不保存、不读取明文凭据，也不会在错误信息中输出凭据。
- Push 保留并显示目标 URL；支持非快进失败后由用户外部解决并重试，但不会代替用户强制推送。

## 已知限制

以下限制与当前候选版本的实际验证状态一致；未验证项不写成已通过。

- **未做代码签名**：Augit 可执行文件与安装包没有 Augit 自身代码签名，Windows SmartScreen 可能显示未知发布者提示。安装器下载的微软运行时包会校验 Microsoft Corporation 数字签名。
- **干净虚拟机与真实缺少 WebView2 的安装流程未实测**：Windows 10 22H2 实机已验证安装器在缺少 .NET 10 时提示、下载并安装，但真实缺少 WebView2 的机器和干净虚拟机没有条件复验。
- **三块及以上显示器、物理拔插显示器未验证**：双显示器、负坐标和软件模拟热插拔已通过；三屏与物理拔线已由用户移出本次验收范围。
- **无障碍**：大文本编辑区向辅助技术暴露虚拟化列表（文件总数、绝对行号、活动行与键盘可达），但离屏全文不会同时存在于可访问树中，连续全文朗读仍受虚拟化限制。真实 NVDA 会话和纯 OS 键盘导航已覆盖小文件、2,001 行与 80,001 行抽样。
- **500 毫秒状态同步**：普通文件外部修改与本地 Git 状态刷新按 500 毫秒目标验证；后续 20/20 轮为 236–293ms，但历史 611/552/624ms 慢样本未完成归因，不能写成已稳定达标。
- **Settings 冷开**：3 次全新进程为 491/451/567ms，未连续达成 500ms；500ms 超时与异常路径已有受控单测，2904ms 历史慢样本未复现。
- **超大文本开启软换行**：80,000 行/约 3MB 文件会退回全量行 DOM，页面级构建 3.62–9.78s，真实外壳首测 5.23s；之后滚动、查找、跳转、全选与关闭可用。大文件建议保持软换行关闭。
- **真实鼠标 Continue Rebase 仅覆盖代表性场景**：两文件 rebase 冲突、外部删除一个并编辑暂存另一个后，真实前台 OS 鼠标点击已通过；Skip/Abort、反向解决顺序和二进制冲突未用真实鼠标复验。
- **像素目标未整体达成**：共享功能页与 PyCharm 2026.2.1 的整窗像素对照仍有未通过页面，按持续改进登记，不冒充通过。
- **视觉范围差异**：不提供参考的右侧 42 CSS 工具轨，Settings 为简化自定义结构，终端为简洁 xterm；这些是已批准的范围差异，不是待修复缺陷。
- **Windows 10 22H2 启动退出修复待实机复验**：rc.4/rc.5 增加 CET 兼容标记、首次启动欢迎页、async void 异常边界和 `git init` 超时，但尚未在用户出问题的 22H2 机器上复验。若仍异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段或 Windows 应用程序日志中的崩溃事件。
- **真实日常使用观察仍在进行**：本候选需要真实仓库中的长期使用反馈；Augit 不做遥测，问题请通过 Issue 反馈。

## 从源码构建

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet build Augit.slnx -c Release
dotnet test Augit.slnx -c Release --no-build
powershell -NoProfile -File .\tools\release.ps1 -Version <版本> -OutputDirectory artifacts\<版本输出目录>
```

发布脚本从锁定依赖还原开始，生成 win-x64、**self-contained 的便携压缩包**和 **framework-dependent 的小型联网安装器**，校验必需文件、第三方许可证、无 PDB、self-contained 运行时和运行时下载哈希，并在结束前清理发布暂存目录。self-contained 发布使用独立的 `packages.selfcontained.lock.json`，不改写主 `packages.lock.json`。若还原时无法访问 NuGet 源（离线环境），可追加 `-p:NuGetAudit=false` 跳过漏洞审计。

发布与 CI 流程见 [`docs/releasing.md`](https://github.com/Paranoid49/Augit/blob/main/docs/releasing.md)。

## 项目结构

- `src/Augit.Core`：领域模型与纯逻辑（文档分类、Git 模型与补丁解析、路径规则）。
- `src/Augit.Infrastructure`：Win32 互操作与外部程序集成（Git 命令、工作区文件、ripgrep、ConPTY、设置存储）。
- `src/Augit.Shell`：原生 Win32 外壳 + WebView2 渲染层；界面由 `web` 目录的网页资源渲染。
- `web`：界面资源（HTML/CSS/JS），与 `docs/ux-mockups` 共用同一套样式与场景脚本。
- `docs`：产品规格、架构、UX/设计规范、验收证据与各专项记录。
- `tools`：发布脚本、打包与安装器定义、真实像素采集和界面审计工具。
- `.github/workflows`：Windows CI 与发布流水线。

## 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附上版本（安装器标题或 `Augit.exe` 文件属性）、复现步骤、期望结果和实际结果，必要时附截图。若异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。Augit 不做遥测和崩溃上报，不会自动上传代码、日志或使用数据。

## 许可证

本项目以 [MIT 许可证](LICENSE) 发布。第三方依赖的用途、成本、许可证与移除条件见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)；分发包内同时包含各依赖的许可证原文。
