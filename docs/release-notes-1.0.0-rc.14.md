# Augit 1.0.0-rc.14 发布说明

- 发布日期：2026-11-19
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建方式：GitHub Actions 从 tag `v1.0.0-rc.14` 构建；实际构建提交与资产哈希见[发布清单](release-manifest-1.0.0-rc.14.md)
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 本版变化

rc.14 汇总 rc.13 之后的手动更新入口和原生模态体验修正：

- **手动检查更新**：新增 `File → Check for Updates…`，点击后显示正在检查、发现新版本、已是最新或失败的具体原因。公司网络、代理、签名/清单错误不再被启动检查静默吞掉；隔离实例会明确提示更新检查不可用。
- **原生模态窗口不闪烁**：Format JSON、New Branch / Rename Branch、Push、Reset、Stash、Worktree、远端等所有同类模态打开时先完成 WebView2 首帧再揭示，关闭时先等待表单移除后的帧再重挂所有者，不再透出下层快照。
- **原生模态四角圆角**：原生子窗按共享 CSS 实际圆角裁切，不再在圆角对话框外露出系统方角边框；DPI 和尺寸变化时同步更新。
- rc.13 已有的 SQL/未知扩展名 UTF-16 文本预览、进程内 JSON 临时格式化、通知关闭叉、Recent Workspaces 记录和双击 Shift 文件搜索继续保留。

## 2. 下载

<!-- RELEASE_ASSETS -->

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.14>

- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只需系统已有 Microsoft Edge WebView2 Runtime。
- **自动更新资产**：`update.json` 和 `update.json.sig` 供应用检查更新使用；普通用户只需下载便携 zip。
- 设置和会话保存在 `%LOCALAPPDATA%\Augit`；替换程序目录不会丢失设置。
- Augit 自身未做 Authenticode 代码签名，SmartScreen 可能提示未知发布者；请用 `SHA256SUMS.txt` 核对下载完整性。更新清单另有独立 ECDSA 签名。

## 3. 验证摘要

- Release 构建：0 警告、0 错误。
- 本机全量测试：Core **77/77**、Shell **285/285**、Infrastructure **220/220**，合计 **582/582**。
- UI 资产一致性、JavaScript 语法、文档声明、脚本编码、`dotnet format` 和 `git diff --check` 通过。
- 原生模态修复已逐帧录屏验证：Format JSON 和 New Branch 打开不再透出背景，关闭不再残留半透明旧表单；四角为圆角且无方角外框。
- 手动检查更新已验证：正常版本显示“已是最新”，隔离实例明确显示不可用；网络失败路径显示实际错误。
- 实际 Release 资产的匿名下载、SHA-256、ECDSA 签名、便携包版本与启动验证结果在流水线完成后补入[发布清单](release-manifest-1.0.0-rc.14.md)。

## 4. 自动更新

- rc.13 已内置签名自动更新，可从公开 rc.13 检查并升级到 rc.14；发布后需用真实 Release 资产复核该路径。
- 启动后仍只后台检查一次；`File → Check for Updates…` 提供可随时重试的手动入口。
- 发现更新只显示通知；下载、校验签名/大小/SHA-256 和重启替换都需要用户操作，不静默升级。
- 便携目录必须可写；同一目录还有其它 Augit 窗口运行时更新会被拒绝。失败时保留或尝试恢复旧版本。
- 每次下载完整便携 zip（约 39 MB）；不做增量更新。
- 仍未做 Authenticode 签名；签名清单只保证更新来源和完整性，不代表 Windows SmartScreen 信任。

## 5. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.14.md)。重点：

- **未做 Authenticode 代码签名**，SmartScreen 可能提示未知发布者。
- WebView2 Runtime 仍是系统依赖；便携包不自动安装共享运行时。
- 自动更新需要可写的便携目录；Windows 10 22H2 实机、真实缺 WebView2、断电/强杀更新器等边界仍未完整复验。
- 手动检查更新依赖 GitHub Releases API；公司网络/代理失败时会明确报错，但不会自动切换下载源。
- 原生模态窗口的渲染屏障会带来很短的打开/关闭延迟；Windows 10 22H2 的系统阴影和真实跨屏形态仍需复验。
- 旧版本没有写入的 Recent Workspaces 无法可靠恢复；从 rc.13 起新打开的项目会记录。
- 不提供 Force Push、子模块、平台接口、Git Console 或遥测。

## 6. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段，更新失败时附 `update.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
