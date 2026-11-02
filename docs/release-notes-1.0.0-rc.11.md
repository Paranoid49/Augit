# Augit 1.0.0-rc.11 发布说明

- 发布日期：2026-11-02
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：`24b089bc14e9f59413e8b8141465a9f561d43f23`（GitHub Actions 从 tag 构建；`Augit.exe` ProductVersion 内含该提交号）
- 发布记录 / tag：`v1.0.0-rc.11`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

rc.11 修复 rc.10 用户反馈的终端菜单、设置打开速度和关闭确认视觉问题，继续保留 rc.10 的多标签终端：

- **Shell 菜单不再是灰色块**：终端 `⌄` 菜单改用标准菜单项，去掉浏览器 `<button>` 默认灰底，只保留悬停/选中蓝底。
- **`+` 与 `⌄` 分工**：`+` 按 Settings 中配置的默认 Shell 直接新建标签；只有 `⌄` 打开 Shell 选择菜单（PowerShell、CMD、Git Bash、WSL、Custom 和 Settings）。
- **Settings 打开速度稳定**：WebView2 环境就绪后立即并行预热，不再等主窗导航完成后再延迟 1.2 秒；点击发生在预热中时直接呈现已构建页面，关闭后立即重新预热。
- **关闭确认按 IntelliJ 像素校正**：390×202 对话框、标题与正文同左缘、无标题分隔线、13px 正文、32px/5px 圆角按钮，以及 IntelliJ 官方的圆角警告三角。
- **保留 rc.10 多终端能力**：独立 ConPTY 会话、`Local` / `Local (2)` 稳定标签、切换/单独关闭、隐藏工具窗保留后台会话、设置改动热应用到空闲默认标签。
- **保留 rc.9/rc.10 窗口修复**：标题栏 File/View/Git 菜单对齐、普通对话框 ×、项目窗口标题、自有徽标、分支弹层和行号栏。

## 2. 下载

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| [Augit-1.0.0-rc.11-win-x64-setup.exe](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.11/Augit-1.0.0-rc.11-win-x64-setup.exe) | 5,059,860 B | `7ae0382c3975a837dc2be486b3a2f560acc52f4d6240ce5965e485db028304a7` |
| [Augit-1.0.0-rc.11-win-x64-portable.zip](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.11/Augit-1.0.0-rc.11-win-x64-portable.zip) | 39,258,988 B | `a2ee734e2b9b022a2f16342044f1567f871fba499cebd7b1fdbbd35859798ece` |
| [SHA256SUMS.txt](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.11/SHA256SUMS.txt) | 207 B | `b3fb31bc2972a9e850bf37fbff4f47509809dd0f324fb7b95d12eb224a6a209a` |

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.11>

- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只要求系统已有 Microsoft Edge WebView2 Runtime。
- **安装版**：按需联网的小型安装器，需要管理员权限；缺少 .NET 10/WebView2 时先说明，经确认后下载并校验。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置。
- Augit 自身未做代码签名，SmartScreen 可能提示未知发布者；请用 `SHA256SUMS.txt` 核对下载完整性。

## 3. 验证摘要

- Release 构建：0 警告、0 错误。
- 本机全量测试：Core **72/72**、Shell **278/278**、Infrastructure **207/207**，合计 **557/557**。
- 托管 CI 排除 4 个需要交互式控制台的 ConPTY 用例；这些用例已在本机真实桌面验证。
- UI 资产一致性、JavaScript 语法、文档声明、`dotnet format` 和 `git diff --check` 通过。
- Windows 11 x64 实机验证：
  - `+` 直接新建默认 Shell 标签，不弹菜单；`⌄` 菜单项为透明背景并含 Settings；
  - 连续打开 Settings 6 次：102 / 485 / 238 / 431 / 240 / 245ms；启动后极早点击最坏 661ms；
  - 终端关闭确认尺寸 390×202，标题/正文/复选框同左缘，按钮 32px 高、5px 圆角；
  - 多标签、Shell 菜单、设置热切换、`ping -t` 关闭确认和标签编号继续通过。
- 本机启动首窗仍在约 367ms 出现，提前预热设置没有拖慢主窗呈现。
- 本机预构建随包验证：`1.0.0-rc.11+9ea4ec7…`，便携包窗口出现 **469.8ms**、关闭 **133.1ms**、退出码 0、无残留子进程。
- **实际 Release 资产复核**：匿名下载的便携包 `39,258,988 B`、SHA-256 `a2ee734e2b9b022a2f16342044f1567f871fba499cebd7b1fdbbd35859798ece`；安装器 `5,059,860 B`、SHA-256 `7ae0382c3975a837dc2be486b3a2f560acc52f4d6240ce5965e485db028304a7`；`SHA256SUMS.txt` `207 B`、SHA-256 `b3fb31bc2972a9e850bf37fbff4f47509809dd0f324fb7b95d12eb224a6a209a`，三者与 Release 正文一致。
- 实际 Release 便携包真实启动 PASS：窗口出现 **463ms**、关闭 **128.6ms**、退出码 0、无残留子进程；ProductVersion `1.0.0-rc.11+24b089b…`。隐藏共享 .NET 后启动 PASS：窗口出现 **451.1ms**、关闭 **127.1ms**、退出码 0、无残留子进程。
- 安装器 ProductVersion `1.0.0-rc.11`、FileVersion `1.0.0.0`、`NotSigned`。

## 4. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.11.md)。重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- 多终端会话只在当前应用进程内保留，不跨重启恢复。
- Shell 设置热切换只处理空闲的默认标签；正在运行命令的标签保持不动。`Don't ask again` 只在当前应用进程内有效。
- 分支行的完整 PyCharm 行内 `NextStep` 子菜单尚未实现；分支行动作走右键/菜单键/Shift+F10。
- Win10 22H2 实机复验待完成；若仍异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段或 Windows 应用程序日志事件。
- 干净虚拟机、真实缺 WebView2、三屏/物理拔线未验证。
- 整窗像素历史结果仍为 0 张合格 / 50 组页面级配对；500ms 历史慢样本、Settings 567ms、80,000 行软换行、离屏全文连续朗读等边界继续披露。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 5. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
