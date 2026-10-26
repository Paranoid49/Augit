# Augit 1.0.0-rc.2 发布说明

> **2026-10-26 后续**：rc.2 便携版在没有 .NET 10 的新电脑上会先弹出 .NET 宿主错误；rc.3 已把便携版改为 self-contained。本文保留 rc.2 的历史发布记录；当前公开版本见 [rc.3 发布说明](release-notes-1.0.0-rc.3.md)。

- 发布日期：2026-10-26
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：`bada0ebc0f56e81e0767512ae89e38a1527a25fb`（由 GitHub Actions 从 tag 构建；`Augit.exe` 的 ProductVersion 内含该提交号）
- 发布记录 / tag：`v1.0.0-rc.2`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

Augit 是面向「代码由外部 AI、脚本或人工修改」这一协作方式的轻量 Windows 只读项目查看器与 Git 图形工具。界面使用 C#/.NET 10 原生 Win32 外壳 + WebView2，不接入 AI，不提供内置文本编辑和保存。

1.0.0-rc.2 是 Beta 收口后的候选包，用于真实日常使用观察。它**不是正式 1.0**：正式发布仍需 RC 观察结果、已知限制裁决和用户确认。

## 2. 下载

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| [Augit-1.0.0-rc.2-win-x64-setup.exe](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.2/Augit-1.0.0-rc.2-win-x64-setup.exe) | 5,013,781 B | `169e35d60c932de5464b47b69b1e0714bdf4e64026448091890833948a7af6db` |
| [Augit-1.0.0-rc.2-win-x64-portable.zip](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.2/Augit-1.0.0-rc.2-win-x64-portable.zip) | 3,526,571 B | `7dc85daf3b6828a4cab1fcc19fcdc2d26aaf3085ce729bf950be12a94c0c8c6e` |
| [SHA256SUMS.txt](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.2/SHA256SUMS.txt) | 205 B | `93f57e623f647d5706de68581ade436868d741a11299d06cfb17329d12044bc4` |

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.2>

- 上表与 Release 正文、附件 `SHA256SUMS.txt` 一致；发布后已匿名下载复核。
- 安装版需要管理员权限，默认安装到 `%ProgramFiles%\Augit`；安装器检测 .NET 10/WebView2，缺少时经用户确认后从项目锁定的微软官方地址下载并校验哈希与 Microsoft 签名。
- 便携版解压后运行 `Augit.exe`，不写 PATH/右键菜单，不自动安装运行时。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置、最近目录和会话状态。
- **未做代码签名**：Windows SmartScreen 可能提示「未知发布者」。请核对 `SHA256SUMS.txt`；不要因提示直接认定文件损坏。

### 与旧 rc.2 冻结构建的关系

2026-10-25 的旧 rc.2 冻结构建（`8643b9e`）**从未公开上传**，当时文档中的 `e339da49…` / `ecb00134…` 哈希无法复核，也不是本次发布资产。本次公开 rc.2 由包含 Windows 10 22H2 多显示器/DPI 修复的源码重新构建；构建源提交、产物哈希和验证记录以[发布清单](https://github.com/Paranoid49/Augit/blob/main/docs/release-manifest-1.0.0-rc.2.md)为准。

## 3. 本候选包含

- 只读浏览：文本、Markdown、JSON、图片（PNG/JPEG/BMP）、二进制/超限信息态、文件树、快速打开、全仓搜索、大文本窗口化预览。
- Git：Changes/Unversioned、只读 Diff（并排/统一、忽略空白）、提交历史与详情、文件历史、Blame、分支/标签/引用、Commit、Fetch/Pull/Push、Define/Edit Remote、Stash、Worktree、Smart Checkout、Reset/Rollback 等确认链路。
- 冲突：冲突状态与文件列表、普通只读 Diff、外部编辑/暂存后的状态同步，以及 Continue/Skip/Abort；不提供三栏冲突编辑器。
- 内置终端：xterm 简洁形态，PowerShell 默认，可配置其他 Shell，关闭运行中会话前确认并结束进程树。
- Settings：简化自定义结构，主题/字体/字号/面板尺寸/终端等设置；不重建 PyCharm 完整设置树。

## 4. 相对上一候选的变化

- **Windows 10 22H2 多显示器/DPI 修复**：真实鼠标跨屏拖动保持逻辑窗口尺寸；混合 DPI 启动按目标显示器 DPI 解释保存位置；Settings 跟随 Owner 显示器 DPI；200% 窄窗不再压住任务栏。
- 新增 per-monitor 启动 DPI 选择单元测试与窗口尺寸边界测试。
- 大文本活动行向屏幕阅读器暴露行号与正文，修复只播报位置的问题。
- 首次打包启动时先加载随包 `native\WebView2Loader.dll`，避免误报运行时缺失。
- 增加公开 Release 资产、Windows CI、手动发布流水线、发布清单、发布流程、Issue 模板和变更记录。
- README 改写为面向真实用户的下载、安装、便携、验证状态和已知限制说明。

## 5. 验证摘要

- Release 构建：0 警告、0 错误。
- 测试：Core **72/72**、Shell **268/268**、Infrastructure **206/206**（合计 **546**；托管 CI 排除 4 个需要交互式控制台的 ConPTY 用例，它们在真实桌面和 Windows 10 实机验证）。
- UI 资产字节一致性、PowerShell 编码、文档数字自洽、coverage generator 检查通过。
- 发布后匿名下载实际 Release 资产并复验：便携包启动 PASS（窗口类 `Augit.Shell.Window`、窗口出现 419.4ms、关闭 111ms、退出码 0、无残留子进程）；ProductVersion `1.0.0-rc.2+bada0eb…`；安装器 ProductVersion `1.0.0-rc.2`、FileVersion `1.0.0.0`、`NotSigned`。
- Windows 10 22H2 实机：双显示器、100%/125%/150%/175%/200%、负坐标、模拟热插拔、深色窄窗；rc.2 重建包便携启动与静默安装通过，缺少 .NET 10 时安装器完成确认、下载、校验和安装。
- 历史 `0.1.0 → 1.0.0-rc.2` 覆盖升级、启动、PATH 恢复、设置保留和静默卸载通过。

完整命令与结果见[发布清单](https://github.com/Paranoid49/Augit/blob/main/docs/release-manifest-1.0.0-rc.2.md)；Windows 10/DPI 证据见[22H2 验证](https://github.com/Paranoid49/Augit/blob/main/docs/win10-22h2-multimonitor-validation-2026-10-26.md)和[DPI 修复复验](https://github.com/Paranoid49/Augit/blob/main/docs/win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)。

## 6. 系统要求

- Windows 10 22H2 x64 或 Windows 11 x64（不支持 32 位、ARM64、Windows Server、Windows 预览版、网络共享目录和远程文件系统）。
- .NET 10 Runtime（`Microsoft.NETCore.App`）；缺少时安装器先说明，经确认后下载锁定的微软运行时并校验哈希与 Microsoft 签名。
- Microsoft Edge WebView2 Runtime（Evergreen）。
- Git for Windows 2.40 或更高版本（仅 Git 功能需要）；Augit 不捆绑 Git。
- 认证交给 Git Credential Manager、SSH agent 或终端；Augit 不保存凭据。

## 7. 安装、便携、升级与卸载

- 安装版需要管理员权限，默认安装到 `C:\Program Files\Augit`；可选加入系统 PATH 和资源管理器「使用 Augit 打开」。
- 便携版解压后直接运行 `Augit.exe`，不写 PATH/右键菜单，不自动安装运行时；缺少运行时时给出官方下载入口。
- 覆盖升级沿用原安装目录、PATH 与右键菜单选择；保留 `%LOCALAPPDATA%\Augit` 的设置、最近目录和会话状态。
- 卸载删除程序文件、安装器自己加入的 PATH 项和右键菜单，不删除用户设置、工作区或共享运行时。

## 8. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.2.md)，重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- 干净虚拟机/全新系统安装、真实缺少 WebView2 的安装流程、三块及以上显示器、物理拔插显示器未验证。
- 整窗像素历史结果仍为 **0 张合格 / 50 组页面级配对**，未达成原整改目标；只登记持续改进。
- 500ms 历史 611/552/624ms 慢样本本批未复现、未归因；Settings 冷开 1 次 567.2ms，未连续达成 500ms。
- 80,000 行软换行退回 80,000 行 DOM，真实外壳首测 5.23s、页面级 3.62–9.78s；大文件建议关闭软换行。
- 离屏全文不在可访问树，真实屏幕阅读器连续朗读仍受限；Narrator 完整会话、JAWS/Braille 未验证。
- Diff 有 10MB/20MB 与 2000 行实现上限；搜索超时仅受控慢条件验证；Push 认证覆盖按专项记录限定。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 9. 证据与复验

- [发布清单](https://github.com/Paranoid49/Augit/blob/main/docs/release-manifest-1.0.0-rc.2.md)
- [发布收口台账](https://github.com/Paranoid49/Augit/blob/main/docs/release-closure-2026-10-24.md)
- [证据索引](https://github.com/Paranoid49/Augit/blob/main/docs/evidence-index-1.0.0-rc.2.md)
- [Windows 10 22H2 多显示器验证](https://github.com/Paranoid49/Augit/blob/main/docs/win10-22h2-multimonitor-validation-2026-10-26.md)
- [Windows 10 22H2 DPI 修复复验](https://github.com/Paranoid49/Augit/blob/main/docs/win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)
- [发布建议](https://github.com/Paranoid49/Augit/blob/main/docs/release-recommendation-1.0-2026-10-25.md)
- [发布流程](https://github.com/Paranoid49/Augit/blob/main/docs/releasing.md)

原始截图、日志和本机验收报告位于本机 `artifacts/`（Git 忽略），不在公开仓库或 Release 中；Release 只包含便携 zip、安装器和 `SHA256SUMS.txt`。

## 10. 下一步

1. 在真实日常仓库中观察本 RC：浏览、外部修改同步、提交与分支、拉取推送、冲突、终端、重启恢复。
2. 记录实际运行环境、问题、结果与回归依据；不在用户真实仓库制造破坏性测试。
3. 处理或裁定已知限制中的 500ms 慢样本、软换行构建、可访问性连续阅读、干净安装/WebView2 缺失、代码签名。
4. 完成观察后再讨论正式 1.0；本预发布不自动升级为正式 1.0。

## 11. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈，附版本（安装器标题或 `Augit.exe` 文件属性）、复现步骤、期望与实际结果。Augit 不做遥测/崩溃上报，不会自动上传代码、日志或使用数据。
