# Augit 1.0.0-rc.10 发布说明

- 发布日期：2026-10-29
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：`637221903aeca180ea1a7ce8a858fc8154c7e3b5`（GitHub Actions 从 tag 构建；`Augit.exe` ProductVersion 内含该提交号）
- 发布记录 / tag：`v1.0.0-rc.10`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

rc.10 集中重做终端工具窗，并合入 rc.9 之后的窗口交互修复：

- **同时打开多个终端**：标题栏显示 `Terminal | Local × + ⌄`，标签可切换、单独关闭；首个标签为 `Local`，后续为 `Local (2)`、`Local (3)`，使用稳定编号。
- **不打开设置也能选 Shell**：`+` / `⌄` 直接列出 Windows PowerShell、PowerShell 7、Command Prompt、Git Bash、Ubuntu（WSL）、Custom Command 和 Settings。
- **设置热生效**：修改 Settings → Terminal 的 Shell 或自定义命令后，空闲的默认终端立即切换；不再需要重启应用。用户已经输入过命令的会话不会被静默终止，新建终端使用新配置。
- **JetBrains 风格关闭确认**：仍有前台进程的终端显示 `Process 'Terminal Local (N)' Is Running`，带橙色警告三角、`Don't ask again`、`Terminate` / `Cancel`；无前台进程时直接关闭。
- **保留后台会话**：隐藏终端工具窗不会结束会话；关闭单个标签只结束该标签，关闭全部标签后才收起工具窗。
- **窗口交互修复**：标题栏 File/View/Git 菜单与品牌徽标垂直对齐；普通对话框标题栏 × 恢复关闭；项目窗口标题使用工作区目录名，任务栏悬停和 Alt-Tab 不再统一显示 `Augit`。
- **设置窗口预热**：主窗加载后后台预构建隐藏设置页，点击齿轮直接显示；关闭后重新预热。

rc.9 的 PyCharm 风格分支弹层、非模态弹层点击外部关闭、行号栏、自有徽标与任务栏图标继续保留。

## 2. 下载

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| [Augit-1.0.0-rc.10-win-x64-setup.exe](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.10/Augit-1.0.0-rc.10-win-x64-setup.exe) | 5,059,130 B | `019f7ca5b5613076b872b3159b5b19b0fd23cf95b539992c5e60699b0b430695` |
| [Augit-1.0.0-rc.10-win-x64-portable.zip](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.10/Augit-1.0.0-rc.10-win-x64-portable.zip) | 39,258,295 B | `aedd52308412ab892b025c26ff5ad92f95cf61616c45f7fb4bcdcf3412b79dfc` |
| [SHA256SUMS.txt](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.10/SHA256SUMS.txt) | 207 B | `70e1950ef6a99ec903996b4fce81e1b0c7a5b8a4cdd75b081ec0263764c42798` |

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.10>

- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只要求系统已有 Microsoft Edge WebView2 Runtime。
- **安装版**：按需联网的小型安装器，需要管理员权限；缺少 .NET 10/WebView2 时先说明，经确认后下载并校验。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置。
- Augit 自身未做代码签名，SmartScreen 可能提示未知发布者；请用 `SHA256SUMS.txt` 核对下载完整性。

## 3. 验证摘要

- Release 构建：0 警告、0 错误。
- 本机全量测试：Core **72/72**、Shell **278/278**、Infrastructure **207/207**，合计 **557/557**。
- 托管 CI 排除 4 个需要交互式控制台的 ConPTY 用例；这些用例已在本机真实桌面验证。
- UI 资产一致性、JavaScript 语法、文档声明和 `git diff --check` 通过。
- Windows 11 x64 实机验证：
  - 同时打开 PowerShell、CMD、Git Bash，切换标签、关闭单个标签和关闭全部标签；
  - 终端 `+`/`⌄` Shell 菜单直接启动 CMD、Git Bash；
  - Settings → Terminal 的 `Windows PowerShell → Git Bash → Windows PowerShell` 双向热切换；
  - `ping -t` 前台进程关闭时出现 JetBrains 风格警告，`Cancel` 保留、`Terminate` 结束并收起；
  - 标签编号保持 `Local` / `Local (2)`，关闭后新建不会无限增长。
- 本机预构建随包验证：`1.0.0-rc.10+0b53cd4…`，便携包窗口出现 **483.1ms**、关闭 **103.8ms**、退出码 0、无残留子进程；实际公开发布资产仍以 tag 流水线产物为准。
- **实际 Release 资产复核**：匿名下载的便携包 `39,258,295 B`、SHA-256 `aedd52308412ab892b025c26ff5ad92f95cf61616c45f7fb4bcdcf3412b79dfc`；安装器 `5,059,130 B`、SHA-256 `019f7ca5b5613076b872b3159b5b19b0fd23cf95b539992c5e60699b0b430695`；`SHA256SUMS.txt` `207 B`、SHA-256 `70e1950ef6a99ec903996b4fce81e1b0c7a5b8a4cdd75b081ec0263764c42798`，三者与 Release 正文一致。
- 实际 Release 便携包真实启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现 **380.7ms**、关闭 **104.8ms**、退出码 0、无残留子进程；ProductVersion `1.0.0-rc.10+6372219…`。隐藏共享 .NET 后启动 PASS：窗口出现 **501.6ms**、关闭 **100ms**、退出码 0、无残留子进程。
- 安装器 ProductVersion `1.0.0-rc.10`、FileVersion `1.0.0.0`、`NotSigned`。
- Windows 10 22H2 实机本轮未复验；不把 rc.9 的历史证据写成 rc.10 已通过。

## 4. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.10.md)。重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- 多终端会话只在当前应用进程内保留，不跨重启恢复。
- Shell 设置热切换只处理空闲的默认标签；正在运行命令的标签保持不动，由用户自行关闭后重开。`Don't ask again` 只在当前应用进程内有效。
- 分支行的完整 PyCharm 行内 `NextStep` 子菜单尚未实现；分支行动作走右键/菜单键/Shift+F10。
- Win10 22H2 实机复验待完成；若仍异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段或 Windows 应用程序日志事件。
- 干净虚拟机、真实缺 WebView2、三屏/物理拔线未验证。
- 整窗像素历史结果仍为 0 张合格 / 50 组页面级配对；500ms 历史慢样本、Settings 567ms、80,000 行软换行、离屏全文连续朗读等边界继续披露。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 5. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
