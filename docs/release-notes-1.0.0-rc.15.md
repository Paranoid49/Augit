# Augit 1.0.0-rc.15 发布说明

- 发布日期：2026-11-26
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建方式：GitHub Actions 从 tag `v1.0.0-rc.15` 构建；实际构建提交与资产哈希见[发布清单](release-manifest-1.0.0-rc.15.md)
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 本版变化

rc.15 汇总 rc.14 之后的体验与稳定性修正：

- **原生模态窗口不再闪黑或淡出**：打开 Format JSON、New Branch、Push、Reset、Stash、Worktree、远端管理、Rollback 等所有原生模态时，背景快照在首图就绪前不再以全尺寸透明表面参与合成，业务 WebView2 在首帧屏障完成后才移出主窗口；关闭时不再播放约 160ms 的系统整窗隐藏过渡，旧表单即时消失。
- **Git 回滚对话框按 IDEA 重做**：按 `RollbackChangesDialog` 改为 `Rollback Changes` 多文件勾选树，支持按目录分组、根/目录半选、状态计数和 `Delete local copies of added files`；新增/未跟踪文件在未勾选删除副本时保留本地文件。Changes 工具栏和右键入口共用该窗口。
- **标签会话按工作区隔离**：打开另一个工作区时不再把上一个工作区的标签按相对路径恢复过来；旧仓库的 `global.json` 等已不存在文件不再变成「文件已被删除、重命名或替换」的幽灵标签，同名文件也不会错误显示旧仓库内容。
- **缺失文件不再留下打不开的标签**：同一工作区恢复时跳过已不存在的文件，并激活首个仍存在的恢复标签；显式打开不存在的文件只在右下提示区说明原因；已经打开的标签被外部删除时按 `ux-spec §7.2` 保留删除状态页，文件恢复后可重新读取。
- **后台标签跟随外部变化**：变化批次命中的后台已打开文档会在后台重新读取，不再保留已被替换或删除的旧正文，也不抢占当前标签。
- rc.14 的手动检查更新、原生模态圆角、rc.13 的 UTF-16 文本预览、进程内 JSON 临时格式化、通知关闭叉、Recent Workspaces 记录和双击 Shift 文件搜索继续保留。

## 2. 下载

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| [Augit-1.0.0-rc.15-win-x64-portable.zip](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.15/Augit-1.0.0-rc.15-win-x64-portable.zip) | 39,292,879 B | `dd507c3cbbf56edc24442f10c3deda62a0b726f8a452d38b8156a61ec0ebb221` |
| [update.json](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.15/update.json) | 432 B | `0b9ba50baff7a2e69cdecdf235855fda071395a978c35aef8ab786bdf2432c8a` |
| [update.json.sig](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.15/update.json.sig) | 89 B | `cb808c5509e2e360d398ca99fa4bb33d0a8555843a3fc28b96efe43eaaee8082` |
| [SHA256SUMS.txt](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.15/SHA256SUMS.txt) | 265 B | `7a267505e912c8be8b82eeef28f5dbdeb19ada9aeb03c84f4cef6f5099a03519` |

2026-11-26 通过匿名 GitHub 下载复核，GitHub 资产 digest 与 `SHA256SUMS.txt` 一致，`update.json` 内置公钥验签 PASS。

- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只需系统已有 Microsoft Edge WebView2 Runtime。
- **自动更新资产**：`update.json` 和 `update.json.sig` 供应用检查更新使用；普通用户只需下载便携 zip。
- 设置和会话保存在 `%LOCALAPPDATA%\Augit`；替换程序目录不会丢失设置。
- Augit 自身未做 Authenticode 代码签名，SmartScreen 可能提示未知发布者；请用 `SHA256SUMS.txt` 核对下载完整性。更新清单另有独立 ECDSA 签名。

## 3. 验证摘要

- Release 构建：0 警告、0 错误。
- 本机全量测试（排除托管 runner 无法使用的 4 条 ConPTY 交互集成用例）：Core **77/77**、Shell **286/286**、Infrastructure **219/219**，合计 **582/582**。
- UI 资产一致性、JavaScript 语法、文档声明、脚本编码、`dotnet format` 和 `git diff --check` 通过。
- 原生模态修复按真实外壳逐帧录屏验证：打开阶段不再出现整块黑帧，关闭阶段不再残留半透明旧表单；浅色/深色和 200%/150%/125% 缩放下审计通过。
- 跨工作区会话修复用真实入口复现：整改前新工作区出现 `global.json`、`src/a.txt` 两个 `Missing` 幽灵标签并错误恢复同名 `package.json`；整改后标签为 0（详细证据见 `artifacts/session-workspace-isolation-2026-11-25/`）。
- 本机自包含便携包真实启动 PASS：窗口出现 **487ms**、WebView2 首帧后截图、关闭 **116.6ms**、退出码 0、无残留子进程；产物版本 `1.0.0-rc.15+f93288e…`。
- 便携启动校验工具改为等待真实首帧内容（最长 20 秒）而不是固定 2.5 秒，避免冷启动首帧较慢时把正常窗口误判为空白；官方启动复验 PASS（窗口 305.7ms、首帧 475.3ms、关闭 76.6ms、退出码 0、无残留）。
- **rc.14 → rc.15 自动更新端到端已完成（用户 2026-11-28 确认）**：公开 rc.14 便携包在真实环境中完成发现更新、下载、验签、替换与重启确认；本机旧包正常入口曾遇到的 WebView2 `COMException` 不再被当作公开升级路径失败。
- 同一工作区缺失文件场景六项检查全部通过：恢复跳过、活动文件回退、恢复项不写回会话、显式打开不建标签并说明原因、已打开标签显示删除状态页、文件恢复后重新读取。

## 4. 自动更新

- rc.12 起内置签名自动更新；rc.13、rc.14 已完成真实公开升级路径验证。
- 启动后只后台检查一次；`File → Check for Updates…` 提供可随时重试的手动入口。
- 发现更新只显示通知；下载、校验签名/大小/SHA-256 和重启替换都需要用户操作，不静默升级。
- 便携目录必须可写；同一目录还有其它 Augit 窗口运行时更新会被拒绝。失败时保留或尝试恢复旧版本。
- 每次下载完整便携 zip（约 39 MB）；不做增量更新。
- 仍未做 Authenticode 签名；签名清单只保证更新来源和完整性，不代表 Windows SmartScreen 信任。

## 5. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.15.md)。重点：

- **未做 Authenticode 代码签名**，SmartScreen 可能提示未知发布者。
- WebView2 Runtime 仍是系统依赖；便携包不自动安装共享运行时。
- 自动更新需要可写的便携目录；Windows 10 22H2 实机、真实缺 WebView2、断电/强杀更新器等边界仍未完整复验。
- 原生模态窗口的渲染屏障会带来很短的打开/关闭延迟；模态打开期间主界面后台刷新仍可能有 2–4 帧瞬态，设置窗口空白帧是独立路径，未纳入本版修复。
- 分支切换等仅 Git 元数据变化而没有文件系统事件时，后台标签可能保留旧正文直到其路径再次变化或被选中；本版只重读变化批次命中的后台标签。
- 已打开标签被外部删除时保留删除状态页，不自动关闭；PyCharm 2026.2.1 实机行为是关闭标签，这是已登记的产品差异。
- Windows 10 22H2 的启动路径、便携运行和自动更新仍未在该实机复验；不提供 Force Push、子模块、平台接口、Git Console 或遥测。

## 6. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段，更新失败时附 `update.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
