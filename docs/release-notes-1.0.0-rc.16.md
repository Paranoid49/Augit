# Augit 1.0.0-rc.16 发布说明

- 发布日期：2026-11-28
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建方式：GitHub Actions 从 tag `v1.0.0-rc.16` 构建；实际构建提交与资产哈希见[发布清单](release-manifest-1.0.0-rc.16.md)
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 本版变化

rc.16 汇总 rc.15 之后的用户可感知修正：

- **右键复制绝对路径**：项目树和 Commit/Changes 右键的 `Copy Path/Reference…` 改为二级菜单；`Absolute Path` 复制工作区完整绝对路径，`Path from Content Root` 保留原相对路径。子项显示路径预览，悬停/`ArrowRight` 展开，`Esc` 逐级收起，右缘自动向左翻。
- **检查更新不再被 GitHub API 限流**：更新发现改读 GitHub Releases Atom 源，不再消耗匿名 REST API 的 60 次/小时配额；公司 NAT/VPN 共享出口下不再因为配额耗尽看到 403。失败仍显示可读原因，不自动下载或安装。
- **Changes 工具栏按 IDEA 启用**：非模态提交模式下 Rollback 只要有改动即可用，无行选中时以已勾选文件为目标；Show Diff 在无行选中时回退到首个已勾选文件。
- **Rollback Changes 对话框补齐 IDEA 行为**：无 included 文件时不再默认勾选整棵树，Rollback 与 Diff/预览禁用；根节点显示工作区名，底栏按钮恢复 30px 紧凑高度，工具条分隔和禁用状态按参考对齐。
- **关闭原生模态不再掉到其它应用后面**：隐藏活动模态前先恢复所有者窗口启用与前台，嵌套退出同样先激活父窗；修复主界面短暂落到其它窗口后面或闪一下才回来的问题。
- rc.15 的原生模态打开闪黑/关闭淡出修复、回滚多文件树、跨工作区标签隔离和缺失文件恢复继续保留。

## 2. 下载

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| [Augit-1.0.0-rc.16-win-x64-portable.zip](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.16/Augit-1.0.0-rc.16-win-x64-portable.zip) | 39,298,494 B | `087171a3072c234f04f3b8e28197a84393b6cb8bb5c00bb196b856152dbc4890` |
| [update.json](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.16/update.json) | 432 B | `c83deafe0220e597018997dd82da432c2d214e39210e56e3ce8c01590c15d028` |
| [update.json.sig](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.16/update.json.sig) | 89 B | `20c1b1f7e09f15878a4ee5b8ab6de7332dd1662f9b127d3c1b304b2454f6feb5` |
| [SHA256SUMS.txt](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.16/SHA256SUMS.txt) | 265 B | `422416bc9a7e0ae952a48a7741ec075b7ff74d296941ca962acd78b6a01700a0` |

2026-11-28 通过匿名 GitHub 下载复核，GitHub 资产 digest 与 `SHA256SUMS.txt` 一致，`update.json` 使用内置公钥验签 **PASS**。

- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只需系统已有 Microsoft Edge WebView2 Runtime。
- **自动更新资产**：`update.json` 和 `update.json.sig` 供应用检查更新使用；普通用户只需下载便携 zip。
- 设置和会话保存在 `%LOCALAPPDATA%\Augit`；替换程序目录不会丢失设置。
- Augit 自身未做 Authenticode 代码签名，SmartScreen 可能提示未知发布者；请用 `SHA256SUMS.txt` 核对下载完整性。更新清单另有独立 ECDSA 签名。

## 3. 验证摘要

- Release 构建：**PASS**，0 警告、0 错误。
- 本机 Release 单元测试（排除托管 runner 无法使用的 4 条 ConPTY 交互集成用例）：Core **77/77**、Shell **286/286**、Infrastructure **221/221**，合计 **584/584**。
- UI 资产一致性、脚本编码、文档声明、覆盖率、`dotnet format`、JavaScript 语法和 `git diff --check`：**PASS**。
- 本版定向 `live-shell`：Copy Path/Reference 上下文菜单 **27/27**、复制失败通知 **4/4**、Rollback 对话框 **18/18**、改动工具栏 **5/5**，合计 **54/54**。
- 本机预检包（构建提交 `0047bbb`）**PASS**：`39,298,431 B`，SHA-256 `c9109912…`；ProductVersion `1.0.0-rc.16+0047bbb…`；真实启动窗口 **300.5ms**、首帧 **597.8ms**、关闭 **126.3ms**、退出码 0、无残留子进程。
- 官方发布资产匿名核对 **PASS**：zip `39,298,494 B` / SHA-256 `087171a3…`，版本 `1.0.0-rc.16+a8983d6…`；官方包真实启动窗口 **296.2ms**、首帧 **566.5ms**、关闭 **153ms**、退出码 0、无残留；`update.json` 验签 **PASS**。
- **全量 `live-shell` 本轮未作为全绿门禁**：历史断言基线已有过期选择器（字体固定、分支弹层改为 `data-popover-action`、后台标签跟随外部变化、标题栏 Settings 入口改为当前控件）。本轮修复了可在改动范围内确认的过期断言，但套件仍在标题栏 Settings 选择器处停下；不把该历史基线写成通过，也不把失败混入本版功能结论。该缺口登记在发布清单中，后续单独整理测试基线。

## 4. 自动更新

- rc.12 起内置签名自动更新；用户已确认 rc.12→rc.13、rc.13→rc.14、rc.14→rc.15 的公开升级路径完成。
- rc.16 发布后继续验证 `rc.15 → rc.16` 的发现、下载、验签、重启替换和版本确认；本轮不再把“历史升级成功”当作本版升级已通过。
- 启动后只后台检查一次；`File → Check for Updates…` 提供手动入口。
- 发现更新只显示通知；下载、校验签名/大小/SHA-256 和重启替换都需要用户操作，不静默升级。
- 便携目录必须可写；同一目录还有其它 Augit 窗口运行时更新会被拒绝。失败时保留或尝试恢复旧版本。
- 每次下载完整便携 zip（约 39 MB）；不做增量更新。
- 仍未做 Authenticode 签名；签名清单只保证更新来源和完整性，不代表 Windows SmartScreen 信任。

## 5. 平台与用户确认

- **Windows 10 22H2 x64**：用户于 2026-11-28 确认在公司实机上连续运行多个 rc 版本；此前 2026-10-26 的双屏/DPI 验证继续有效。rc.16 仍需在该实机做一次启动与更新复验。
- **Windows 11 x64**：完成主界面、便携包、屏幕阅读器和性能基线验证。
- 干净虚拟机、真实缺少 WebView2、Narrator/JAWS/Braille、更多显示器拓扑仍按已知限制披露，不冒充通过。

## 6. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.16.md)。重点：

- **未做 Authenticode 代码签名**，SmartScreen 可能提示未知发布者。
- WebView2 Runtime 仍是系统依赖；便携包不自动安装共享运行时。
- 自动更新需要可写的便携目录；断电/强杀更新器、磁盘满和杀毒软件拦截未完整复验。
- 原生模态窗口的渲染屏障会带来很短的打开/关闭延迟；模态打开期间主界面后台刷新仍可能有 2–4 帧瞬态，设置窗口空白帧是独立路径。
- 80,000 行软换行会退回完整 DOM，首开可能数秒；屏幕阅读器离屏全文不能连续朗读。
- 分支切换等仅 Git 元数据变化而没有文件系统事件时，后台标签可能保留旧正文直到其路径再次变化或被选中。
- 已打开标签被外部删除时保留删除状态页，不自动关闭；这是已登记的产品差异。
- 不提供 Force Push、子模块、平台接口、Git Console 或遥测。

## 7. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段，更新失败时附 `update.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
