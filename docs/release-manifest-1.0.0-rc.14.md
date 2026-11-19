# Augit 1.0.0-rc.14 公开发布清单

本清单记录 `1.0.0-rc.14` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.14> |
| Release id | `450785959` |
| Tag | `v1.0.0-rc.14` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | `342fd8dd19ce1c7b900cffc9beea905b8a58ea32` |
| 版本 | `1.0.0-rc.14` |
| 形态 | **self-contained 便携 zip**（自带 .NET 10.0.11 Runtime） |
| 自动更新 | `update.json` + `update.json.sig`，ECDSA P-256 |
| 安装器 | **不提供**；发布单一便携 zip |
| FileVersion | `1.0.0.0` |
| 代码签名 | **NotSigned**（无 Authenticode；更新清单有独立签名） |

rc.14 在 rc.13 的基础上加入 `File → Check for Updates…` 手动更新检查，并修复原生模态窗口打开/关闭的透明闪烁与方角外框，覆盖 Format JSON、New Branch 及其他同类模态。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.14-win-x64-portable.zip` | 39,284,414 B | `55a241cca0a157648f5c413688e92f8c053e17b5250f3b07e350990044b601c0` |
| `update.json` | 432 B | `145615f854175dba3a8a345f6a988cc48ea48ac49cbe7a9f2cd12d7866add1fa` |
| `update.json.sig` | 89 B | `bcbec3066cdac5ac5cc18129a7801f07b221e25a6a34ef6e4a8d665d13687aa5` |
| `SHA256SUMS.txt` | 265 B | `09bac784645cd6cc07e14f3145c10f7d87684ae8015332053ba238530f081326` |

2026-11-19 通过匿名 GitHub 下载四个资产并按上表复核，SHA-256 与发布正文一致。

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.0.0-rc.14 -OutputDirectory artifacts\release-1.0.0-rc.14
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.14\Augit-1.0.0-rc.14-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.14 -ExpectedVersion 1.0.0-rc.14 -SkipLaunch
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/create-release-manifest.ps1 -Version 1.0.0-rc.14 -Tag v1.0.0-rc.14 -ZipPath artifacts\release-1.0.0-rc.14\Augit-1.0.0-rc.14-win-x64-portable.zip -OutputDirectory artifacts\release-1.0.0-rc.14
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/verify-release-manifest.ps1 -ManifestPath artifacts\release-1.0.0-rc.14\update.json -SignaturePath artifacts\release-1.0.0-rc.14\update.json.sig
```

本机发布前门禁（2026-11-19）：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **PASS**：0 警告 / 0 错误 |
| Core 测试 | **PASS**：77/77 |
| Shell 测试 | **PASS**：285/285 |
| Infrastructure 测试 | **PASS**：220/220 |
| UI 资产一致性 | **PASS** |
| `dotnet format` / `git diff --check` | **PASS** |
| 新功能实机验证 | **PASS**：手动更新检查、原生模态无闪烁、四角圆角、Format JSON、New Branch |

## 4. 自动更新

- rc.13 已内置签名自动更新；已用公开 rc.13 便携包完成 `rc.13 → rc.14` 的升级路径验证。
- 客户端读取 GitHub Release 的 `update.json` 和 `update.json.sig`，用内置 ECDSA P-256 公钥验签，再校验 tag、版本、GitHub HTTPS 地址、大小和 SHA-256。
- 下载完整 zip 后校验 `Augit.exe` ProductVersion；用户确认后由暂存的新版本执行替换，失败时回滚旧版本。
- 同一目录多窗口、目标目录不可写、更新暂存目录与目标相同/包含关系均被拒绝。

## 5. Windows 10 22H2 证据与边界

rc.2 时代的 Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器、DPI、负坐标和模拟热插拔历史证据继续有效（见[22H2 记录](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)）。rc.14 的启动、更新和新增功能尚未在用户出问题的 22H2 机器上复验，不把 Windows 11 结果写成 Windows 10 已通过。

## 6. 发布后核对

2026-11-19 已完成：

- Release 标记为 **Pre-release**（id `450785959`），四个资产匿名可下载。
- 匿名下载的 zip、`update.json`、`update.json.sig`、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致。
- `update.json` 使用内置公钥验签 **PASS**。
- 官方便携包真实启动：主界面加载完成（CDP `window.__augitReady === true`），`File → Check for Updates…` 入口存在；ProductVersion `1.0.0-rc.14+342fd8d…`，FileVersion `1.0.0.0`。
- 便携包内容校验 **PASS**：必需文件、web 资源一致性、无 PDB、`includedFrameworks` 运行时配置。
- **`rc.13 → rc.14` 自动更新端到端 PASS**：公开 rc.13 便携包检查到 rc.14，下载并验签、用户确认后重启替换；目标版本变为 `1.0.0-rc.14+342fd8d…`，`update.log` 记录替换成功。
- Windows 10 22H2 实机构建/启动/更新未复验。

状态：**PASS**（Windows 10 22H2 实机、真实缺 WebView2 等边界未复验）。
