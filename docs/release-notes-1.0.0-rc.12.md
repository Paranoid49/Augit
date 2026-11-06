# Augit 1.0.0-rc.12 发布说明

- 发布日期：2026-11-06
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建方式：GitHub Actions 从 `v1.0.0-rc.12` 标签构建；实际提交与产物哈希以发布页和 `SHA256SUMS.txt` 为准
- 发布记录 / tag：`v1.0.0-rc.12`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

rc.12 是第一个只发布 self-contained 便携版、并内置签名自动更新的候选版本：

- **单一便携版发行**：只提供 `Augit-1.0.0-rc.12-win-x64-portable.zip`，不再构建或发布 Inno Setup 安装器。
- **应用内自动更新**：启动后检查 GitHub Release 的签名 `update.json`，下载完整便携 zip，校验 ECDSA 签名、大小和 SHA-256，用户确认后重启替换；失败自动回滚旧版本。
- **不静默升级**：发现更新只显示通知；下载和重启都由用户操作。重启前提示当前窗口和终端会话会关闭。
- **终端交互修正**：当前终端标签显示浅灰圆角卡片，未选中标签透明；Settings 修改默认终端只影响之后新建的标签，不再关闭已经打开的终端。
- **界面清理**：删除编辑标签区无功能的 Tab Options 三点按钮；UI 字体固定为 `Microsoft YaHei UI`，只保留可编辑字号，系统缺字体时静默回退到 Segoe UI。
- **保留 rc.11 终端能力**：多标签、独立 ConPTY、Shell 菜单、设置默认 Shell、JetBrains 风格关闭确认继续保留。

## 2. 下载

<!-- RELEASE_ASSETS -->

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.12>

- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只需系统已有 Microsoft Edge WebView2 Runtime。
- **自动更新资产**：`update.json` 和 `update.json.sig` 供应用检查更新使用；普通用户只需下载便携 zip。
- 设置和会话保存在 `%LOCALAPPDATA%\Augit`；替换程序目录不会丢失设置。
- Augit 自身未做 Authenticode 代码签名，SmartScreen 可能提示未知发布者；请用 `SHA256SUMS.txt` 核对下载完整性。更新清单另有独立 ECDSA 签名。

## 3. 验证摘要

- Release 构建：0 警告、0 错误。
- 本机全量测试：Core **72/72**、Shell **281/281**、Infrastructure **216/216**，合计 **569/569**。
- 托管 CI 排除 4 个需要交互式控制台的 ConPTY 用例；这些用例已在本机真实桌面验证。
- UI 资产一致性、JavaScript 语法、文档声明、脚本编码、`dotnet format` 和 `git diff --check` 通过。
- 本机便携包内容与 web 资源校验：PASS。
- 本机更新器端到端：
  - 目标目录替换成功；
  - 新版窗口出现；
  - 旧目录备份在成功后清理；
  - 等待旧进程退出、替换、重启路径通过。
- 签名清单在本地用与内置公钥配对的 ECDSA P-256 私钥生成并通过复核；发布流水线还会再验一次签名。
- 本机预构建随包验证：`1.0.0-rc.12+ce7ec9b…`，便携包 `39,275,363 B`、SHA-256 `5290bbf8bd5c1a2e19968a78524103c8e9d7d13fb96b0072dedee81bb184aa29`；窗口出现 **471.9ms**、关闭 **136ms**、退出码 0、无残留子进程。
- 实际 Release 便携包和更新资产的匿名下载/哈希/启动结果以本次 tag 流水线产物为准。

## 4. 自动更新边界

- 更新检查需要访问 GitHub Releases API；离线或网络不可达时静默跳过，不影响正常使用。
- 便携目录必须可写；如果放在 `Program Files`、只读盘或受策略保护的目录，更新失败并尝试恢复旧版本。
- 同一目录还有其它 Augit 窗口运行时，更新会被拒绝，必须先关闭全部窗口。
- 每次下载完整便携 zip（约 39 MB）；当前不做增量更新。
- 更新包验签和 SHA-256 校验失败一律拒绝，没有跳过校验入口。
- 仍未做 Authenticode 签名；签名清单只保证更新来源和完整性，不代表 Windows SmartScreen 信任。
- Windows 10 22H2 实机和真实网络下的更新未复验。

## 5. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.12.md)。重点：

- **未做 Authenticode 代码签名**，SmartScreen 可能提示未知发布者。
- WebView2 Runtime 仍是系统依赖；不使用安装器时没有自动安装共享运行时的流程。
- 多终端会话只在当前应用进程内保留，不跨重启恢复。
- 自动更新需要可写的便携目录；设置保存在 `%LOCALAPPDATA%\Augit`。
- 分支行的完整 PyCharm 行内 `NextStep` 子菜单尚未实现。
- Win10 22H2 实机复验待完成；若异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 和 `update.log` 末段。
- 整窗像素历史结果仍为 0 张合格 / 50 组页面级配对；大文件软换行、离屏全文朗读等边界继续披露。
- 不提供 Force Push、子模块、平台接口、Git Console 或遥测。

## 6. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段，更新失败时附 `update.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
