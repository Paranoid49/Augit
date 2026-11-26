# Augit 1.0.0-rc.15 公开发布清单

本清单记录 `1.0.0-rc.15` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源；本文件在流水线完成后补入实际资产、构建提交和发布 id。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.15> |
| Release id | 待流水线完成后补入 |
| Tag | `v1.0.0-rc.15` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | 待流水线完成后补入 |
| 版本 | `1.0.0-rc.15` |
| 形态 | **self-contained 便携 zip**（自带 .NET 10.0.11 Runtime） |
| 自动更新 | `update.json` + `update.json.sig`，ECDSA P-256 |
| 安装器 | **不提供**；发布单一便携 zip |
| FileVersion | `1.0.0.0` |
| 代码签名 | **NotSigned**（无 Authenticode；更新清单有独立签名） |

rc.15 在 rc.14 的基础上进一步消除原生模态窗口打开闪黑/关闭淡出，按 IDEA `RollbackChangesDialog` 重做 Git 回滚对话框，并隔离跨工作区标签会话、跳过已不存在文件的恢复标签。

## 2. 资产

待发布流水线完成后补入；以下四项是唯一公开资产：

- `Augit-1.0.0-rc.15-win-x64-portable.zip`
- `update.json`
- `update.json.sig`
- `SHA256SUMS.txt`

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.0.0-rc.15 -OutputDirectory artifacts\release-1.0.0-rc.15
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.15\Augit-1.0.0-rc.15-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.15 -ExpectedVersion 1.0.0-rc.15 -SkipLaunch
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/create-release-manifest.ps1 -Version 1.0.0-rc.15 -Tag v1.0.0-rc.15 -ZipPath artifacts\release-1.0.0-rc.15\Augit-1.0.0-rc.15-win-x64-portable.zip -OutputDirectory artifacts\release-1.0.0-rc.15
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/verify-release-manifest.ps1 -ManifestPath artifacts\release-1.0.0-rc.15\update.json -SignaturePath artifacts\release-1.0.0-rc.15\update.json.sig
```

本机发布前门禁（2026-11-26）：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **PASS**：0 警告 / 0 错误 |
| Core 测试 | **PASS**：77/77 |
| Shell 测试 | **PASS**：286/286 |
| Infrastructure 测试 | **PASS**：219/219（排除 4 条 ConPTY 交互集成用例，与 CI 口径一致） |
| UI 资产一致性 / 脚本编码 / 文档声明 / `dotnet format` / `git diff --check` | **PASS** |
| 本地打包与内容校验 | **PASS**：`Augit-1.0.0-rc.15-win-x64-portable.zip` `39,292,896 B`、SHA-256 `1c4a3a04…`；版本 `1.0.0-rc.15+f93288e…`、FileVersion `1.0.0.0`、web 资源一致、无 PDB |
| 官方便携包真实启动 | **PASS**：窗口出现 `487ms`、等待首帧 `2844.8ms`、关闭 `116.6ms`、退出码 `0`、无残留子进程 |
| 新功能真实外壳验证 | **PASS**：原生模态逐帧审计、跨工作区会话隔离、缺失文件恢复/显式打开/后台删除页 |

## 4. 自动更新

- rc.12 起内置签名自动更新；rc.13、rc.14 已完成公开升级路径验证。
- 客户端读取 GitHub Release 的 `update.json` 和 `update.json.sig`，用内置 ECDSA P-256 公钥验签，再校验 tag、版本、GitHub HTTPS 地址、大小和 SHA-256。
- 下载完整 zip 后校验 `Augit.exe` ProductVersion；用户确认后由暂存的新版本执行替换，失败时回滚旧版本。
- rc.14 → rc.15 的公开升级路径在发布后复核并补入本清单。

## 5. Windows 10 22H2 证据与边界

rc.2 时代的 Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器、DPI、负坐标和模拟热插拔历史证据继续有效（见[22H2 记录](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)）。rc.15 的启动、更新和新增功能尚未在用户出问题的 22H2 机器上复验，不把 Windows 11 结果写成 Windows 10 已通过。

## 6. 发布后核对

待发布流水线完成后补入：

- Release 页面标记为 **Pre-release**，四个资产匿名可下载；
- 匿名下载的 zip、`update.json`、`update.json.sig`、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致；
- `update.json` 使用内置公钥验签 **PASS**；
- 官方便携包真实启动与版本核对；
- 便携包内容校验 **PASS**；
- `rc.14 → rc.15` 自动更新端到端结果；
- Windows 10 22H2 实机边界。
