# Augit 1.0.0-rc.13 发布说明

- 发布日期：2026-11-18
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建方式：GitHub Actions 从 tag `v1.0.0-rc.13` 构建；`Augit.exe` ProductVersion 内含构建提交号，实际提交与资产哈希见[发布清单](release-manifest-1.0.0-rc.13.md)
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 本版变化

rc.13 汇总 rc.12 之后的文件预览、临时工具和交互修正：

- **SQL 和未知扩展名文本预览**：UTF-8（可带 BOM）、UTF-16 LE/BE（BOM 或高置信度无 BOM）和 UTF-32 BOM 文本不再因 NUL 字节被误判为二进制；SQL、迁移脚本等普通文本可直接只读预览，编码、换行和大文件截断按实际内容处理。
- **JSON 临时格式化窗口**：`View → Format JSON…` 或 `Ctrl+Shift+J` 打开两栏模态窗口；左侧粘贴、右侧显示带行号的两空格缩进结果，支持 Format、Copy、Clear、Close 和 `Ctrl+Enter`；非法 JSON 显示行列位置。内容只保存在当前进程内存，关闭后重开保留，重启清空；不创建文件、不写设置、不进入项目树或 Git。
- **通知关闭叉**：右下角通知（包括 `Cannot Open Path` 等错误）增加固定关闭叉；关闭只清除当前通知，不改变失败的文件、Git 或项目状态。
- **Recent Workspaces 修复**：真实工作区窗口启动时把当前目录写入最近工作区（去重置顶、最多 10 个），打开 Open Workspace 前读取最新设置；此前列表一直为空的问题修复。旧版本没有记录的历史目录无法可靠补回。
- **双击 Shift 快速搜索**：双击 `Shift` 与 `Ctrl+P` 一样打开文件快速搜索；输入框、终端、菜单或弹层内不触发。

## 2. 下载

<!-- RELEASE_ASSETS -->

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.13>

- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只需系统已有 Microsoft Edge WebView2 Runtime。
- **自动更新资产**：`update.json` 和 `update.json.sig` 供应用检查更新使用；普通用户只需下载便携 zip。
- 设置和会话保存在 `%LOCALAPPDATA%\Augit`；替换程序目录不会丢失设置。
- Augit 自身未做 Authenticode 代码签名，SmartScreen 可能提示未知发布者；请用 `SHA256SUMS.txt` 核对下载完整性。更新清单另有独立 ECDSA 签名。

## 3. 验证摘要

- Release 构建：0 警告、0 错误。
- 本机全量测试：Core **77/77**、Shell **284/284**、Infrastructure **220/220**，合计 **581/581**。
- UI 资产一致性、JavaScript 语法、文档声明、脚本编码、`dotnet format` 和 `git diff --check` 通过。
- 发布流水线执行锁定依赖还原、Release 构建、测试、便携包内容/版本校验、更新清单签名与签名复核。
- 本版新增功能的实机验证：UTF-16 SQL 文本预览、JSON 临时格式化、错误通知关闭、Recent Workspaces 列表、双击 Shift 文件搜索、浅色/深色主题、关闭后重开状态保留、进程重启状态清空。
- 实际 Release 资产的匿名下载、SHA-256、ECDSA 签名、便携包版本和启动验证结果在流水线完成后补入[发布清单](release-manifest-1.0.0-rc.13.md)。

## 4. 自动更新

- rc.12 已内置签名自动更新，因此可以从公开 rc.12 检查并升级到 rc.13；发布后需用真实 Release 资产复核该路径。
- 启动后后台检查签名 `update.json`，不阻塞首屏；发现更新只显示通知。下载、校验签名/大小/SHA-256 和重启替换都需要用户操作，不静默升级。
- 便携目录必须可写；同一目录还有其它 Augit 窗口运行时更新会被拒绝。失败时保留或尝试恢复旧版本。
- 每次下载完整便携 zip（约 39 MB）；不做增量更新。
- 仍未做 Authenticode 签名；签名清单只保证更新来源和完整性，不代表 Windows SmartScreen 信任。

## 5. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.13.md)。重点：

- **未做 Authenticode 代码签名**，SmartScreen 可能提示未知发布者。
- WebView2 Runtime 仍是系统依赖；便携包不自动安装共享运行时。
- 自动更新需要可写的便携目录；Windows 10 22H2 实机、真实缺 WebView2、断电/强杀更新器等边界仍未完整复验。
- 多终端会话只在当前应用进程内保留，不跨重启恢复。
- 大文件软换行、离屏全文朗读、整窗像素持续改进等既有边界继续披露。
- 旧版本没有写入的 Recent Workspaces 无法可靠恢复；从本版起新打开的项目会记录。
- 不提供 Force Push、子模块、平台接口、Git Console 或遥测。

## 6. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段，更新失败时附 `update.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
