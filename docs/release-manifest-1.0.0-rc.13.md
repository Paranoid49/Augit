# Augit 1.0.0-rc.13 公开发布清单

本清单记录 `1.0.0-rc.13` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源；本文件在流水线完成后已补入实际资产和构建提交。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.13> |
| Release id | `449499595` |
| Tag | `v1.0.0-rc.13` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | `82d8124fc7749e2e90e646f2ad5fdf688f875d65` |
| 版本 | `1.0.0-rc.13` |
| 形态 | **self-contained 便携 zip**（自带 .NET 10.0.11 Runtime） |
| 自动更新 | `update.json` + `update.json.sig`，ECDSA P-256 |
| 安装器 | **不提供**；发布单一便携 zip |
| FileVersion | `1.0.0.0` |
| 代码签名 | **NotSigned**（无 Authenticode；更新清单有独立签名） |

rc.13 在 rc.12 的签名自动更新基础上，加入 SQL/未知扩展名 UTF-16 文本预览、进程内 JSON 临时格式化、通知关闭叉、Recent Workspaces 记录和双击 Shift 文件搜索。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.13-win-x64-portable.zip` | 39,282,490 B | `23534777a29f591136c8041db32aec9df45869554671fa7b6ed16755eae815c2` |
| `update.json` | 432 B | `520204d3d009e336b2c59ad7f9b7d20f2c6c927e843f6f54651aa960cdfe36a2` |
| `update.json.sig` | 89 B | `3dc31f4b7cc6856a3baab547dda8b6a6de6f5be3cebcf0a86f80a0a85a33abd5` |
| `SHA256SUMS.txt` | 265 B | `697d814302e78b54ff8ed397cda61141a56a8bd7000bbb8318b2dcc30eb80f09` |

2026-11-18 通过匿名 GitHub 下载四个资产并按上表复核，SHA-256 与发布正文一致。

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

- rc.12 已内置签名自动更新；本次已用公开 rc.12 便携包完成 `rc.12 → rc.13` 的升级路径验证。
- 客户端读取 GitHub Release 的 `update.json` 和 `update.json.sig`，用内置 ECDSA P-256 公钥验签，再校验 tag、版本、GitHub HTTPS 地址、大小和 SHA-256。
- 下载完整 zip 后校验 `Augit.exe` ProductVersion；用户确认后由暂存的新版本执行替换，失败时回滚旧版本。
- 同一目录多窗口、目标目录不可写、更新暂存目录与目标相同/包含关系均被拒绝。

## 5. Windows 10 22H2 证据与边界

rc.2 时代的 Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器、DPI、负坐标和模拟热插拔历史证据继续有效（见[22H2 记录](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)）。rc.13 的启动、更新和新增功能尚未在用户出问题的 22H2 机器上复验，不把 Windows 11 结果写成 Windows 10 已通过。

## 6. 发布后核对

2026-11-18 已完成：

- Release 标记为 **Pre-release**（id `449499595`），四个资产匿名可下载。
- 匿名下载的 zip、`update.json`、`update.json.sig`、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致。
- `update.json` 使用内置公钥验签 **PASS**。
- 实际 Release 便携包真实启动：主界面加载完成（CDP `window.__augitReady === true`），无启动错误；ProductVersion `1.0.0-rc.13+82d8124…`，FileVersion `1.0.0.0`。
- 便携包内容校验 **PASS**：必需文件、web 资源一致性、无 PDB、`includedFrameworks` 运行时配置。
- **`rc.12 → rc.13` 自动更新端到端 PASS**：公开 rc.12 便携包检查到 rc.13，下载并验签、用户确认后重启替换；目标版本变为 `1.0.0-rc.13+82d8124…`，`update.log` 记录备份、替换、启动与备份清理成功。
- Windows 10 22H2 实机构建/启动/更新未复验。

状态：**PASS**（Windows 10 22H2 实机、真实缺 WebView2 等边界未复验）。
