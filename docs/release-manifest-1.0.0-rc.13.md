# Augit 1.0.0-rc.13 公开发布清单

本清单记录 `1.0.0-rc.13` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源；本文件在流水线完成后补入实际资产和构建提交。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.13> |
| Release id | 待流水线完成后补入 |
| Tag | `v1.0.0-rc.13` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | 待流水线完成后补入 |
| 版本 | `1.0.0-rc.13` |
| 形态 | **self-contained 便携 zip**（自带 .NET 10.0.11 Runtime） |
| 自动更新 | `update.json` + `update.json.sig`，ECDSA P-256 |
| 安装器 | **不提供**；发布单一便携 zip |
| FileVersion | `1.0.0.0` |
| 代码签名 | **NotSigned**（无 Authenticode；更新清单有独立签名） |

rc.13 在 rc.12 的签名自动更新基础上，加入 SQL/未知扩展名 UTF-16 文本预览、进程内 JSON 临时格式化、通知关闭叉、Recent Workspaces 记录和双击 Shift 文件搜索。

## 2. 资产

待发布流水线完成后补入；以下四项是唯一公开资产：

- `Augit-1.0.0-rc.13-win-x64-portable.zip`
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
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.0.0-rc.13 -OutputDirectory artifacts\release-1.0.0-rc.13
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.13\Augit-1.0.0-rc.13-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.13 -ExpectedVersion 1.0.0-rc.13 -SkipLaunch
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/create-release-manifest.ps1 -Version 1.0.0-rc.13 -Tag v1.0.0-rc.13 -ZipPath artifacts\release-1.0.0-rc.13\Augit-1.0.0-rc.13-win-x64-portable.zip -OutputDirectory artifacts\release-1.0.0-rc.13
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/verify-release-manifest.ps1 -ManifestPath artifacts\release-1.0.0-rc.13\update.json -SignaturePath artifacts\release-1.0.0-rc.13\update.json.sig
```

本机发布前门禁（2026-11-18）：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **PASS**：0 警告 / 0 错误 |
| Core 测试 | **PASS**：77/77 |
| Shell 测试 | **PASS**：284/284 |
| Infrastructure 测试 | **PASS**：220/220 |
| UI 资产一致性 | **PASS** |
| `dotnet format` / `git diff --check` | **PASS** |
| 新功能实机验证 | **PASS**：UTF-16 SQL 文本、JSON 临时格式化、通知关闭、Recent Workspaces、双击 Shift |

## 4. 自动更新

- rc.12 已内置签名自动更新；rc.13 发布后应验证 `rc.12 → rc.13` 的公开升级路径。
- 客户端读取 GitHub Release 的 `update.json` 和 `update.json.sig`，用内置 ECDSA P-256 公钥验签，再校验 tag、版本、GitHub HTTPS 地址、大小和 SHA-256。
- 下载完整 zip 后校验 `Augit.exe` ProductVersion；用户确认后由暂存的新版本执行替换，失败时回滚旧版本。
- 同一目录多窗口、目标目录不可写、更新暂存目录与目标相同/包含关系均被拒绝。

## 5. Windows 10 22H2 证据与边界

rc.2 时代的 Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器、DPI、负坐标和模拟热插拔历史证据继续有效（见[22H2 记录](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)）。rc.13 的启动、更新和新增功能尚未在用户出问题的 22H2 机器上复验，不把 Windows 11 结果写成 Windows 10 已通过。

## 6. 发布后核对

待发布完成后补入：

- Release 标记为 **Pre-release**，四个资产匿名可下载。
- 匿名下载的 zip、`update.json`、`update.json.sig`、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致。
- `update.json` 使用内置公钥验签 **PASS**。
- 实际 Release 便携包真实启动、ProductVersion、隐藏共享 .NET 启动结果。
- `rc.12 → rc.13` 自动更新端到端结果（若完成）。
