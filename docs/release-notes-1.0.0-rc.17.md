# Augit 1.0.0-rc.17 发布说明

- 发布日期：2026-11-28
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建方式：GitHub Actions 从 tag `v1.0.0-rc.17` 构建；实际构建提交与资产哈希见[发布清单](release-manifest-1.0.0-rc.17.md)
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 本版变化

rc.17 是 rc.16 之后的右键选中态修正：

- **右键未选中行先更新高亮**：项目树文件/目录/根行、Changes 文件行和 Git 日志提交行在被右键时，先把当前高亮、键盘焦点和状态路径替换为该行，再打开菜单；菜单动作目标与高亮行一致。
- 右键已选中的行保持当前选择；Changes 右键不会额外打开或刷新 Diff，Git 日志会同步提交详情。
- 引用树原有的右键选中集规则保持；静态 Changes 菜单也同步该规则。
- rc.16 的更新检查 Atom 源、Rollback 工具栏/对话框、模态关闭前台恢复和 Copy Path 绝对路径继续保留。

## 2. 下载

<!-- RELEASE_ASSETS -->

- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只需系统已有 Microsoft Edge WebView2 Runtime。
- **自动更新资产**：`update.json` 和 `update.json.sig` 供应用检查更新使用；普通用户只需下载便携 zip。
- 设置和会话保存在 `%LOCALAPPDATA%\Augit`；替换程序目录不会丢失设置。
- Augit 自身未做 Authenticode 代码签名，SmartScreen 可能提示未知发布者；请用 `SHA256SUMS.txt` 核对下载完整性。更新清单另有独立 ECDSA 签名。

## 3. 验证摘要

- Release 构建：**PASS**，0 警告、0 错误。
- 本机 Release 单元测试（排除托管 runner 无法使用的 4 条 ConPTY 交互集成用例）：Core **77/77**、Shell **286/286**、Infrastructure **221/221**，合计 **584/584**。
- UI 资产一致性、脚本编码、文档声明、覆盖率、`dotnet format`、JavaScript 语法和 `git diff --check`：**PASS**。
- 本版右键选中定向 `live-shell`：**3/3 PASS**（项目树、Changes、Git 日志的 DOM 高亮、状态字段和菜单目标一致）。
- 本机预检包（构建提交 `934357a`）**PASS**：`39,299,170 B`，SHA-256 `e2394ff3…`；ProductVersion `1.0.0-rc.17+934357a…`；真实启动窗口 **313.9ms**、首帧 **594.9ms**、关闭 **145.6ms**、退出码 0、无残留子进程。官方资产以 GitHub Actions 从标签构建的字节为准。
- **全量 `live-shell` 本轮未作为全绿门禁**：历史断言基线仍有已知过期选择器；本版只以本改动范围的 3 条定向断言和真实应用验证为准，不把全量套件写成通过。

## 4. 自动更新

- rc.12 起内置签名自动更新；用户已确认 `rc.12→rc.13`、`rc.13→rc.14`、`rc.14→rc.15` 的公开升级路径完成。
- `rc.15→rc.16` 和 `rc.16→rc.17` 仍需发布后分别复验；本轮不把历史升级成功当作本版已通过。
- 启动后只后台检查一次；`File → Check for Updates…` 提供手动入口。
- 发现更新只显示通知；下载、校验签名/大小/SHA-256 和重启替换都需要用户操作，不静默升级。
- 便携目录必须可写；同一目录还有其它 Augit 窗口运行时更新会被拒绝。失败时保留或尝试恢复旧版本。
- 每次下载完整便携 zip（约 39 MB）；不做增量更新。
- 仍未做 Authenticode 签名；签名清单只保证更新来源和完整性，不代表 Windows SmartScreen 信任。

## 5. 平台与用户确认

- **Windows 10 22H2 x64**：用户于 2026-11-28 确认在公司实机连续运行多个 rc 版本；rc.17 发布后仍需在该实机做一次启动与更新复验。
- **Windows 11 x64**：完成主界面、便携包、屏幕阅读器和性能基线验证。
- 干净虚拟机、真实缺少 WebView2、Narrator/JAWS/Braille、更多显示器拓扑仍按已知限制披露，不冒充通过。

## 6. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.17.md)。重点：

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
