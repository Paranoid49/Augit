# Augit 1.0.0-rc.16 公开发布清单

本清单记录 `1.0.0-rc.16` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源；本文件在流水线完成后补入实际资产、构建提交和发布 id。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.16> |
| Release id | `461385505` |
| Tag | `v1.0.0-rc.16` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建（run `46121678948`，2026-11-28） |
| 构建提交 | `a8983d6c41b8f63e8722b95f5abcee1e76e8fd56` |
| 版本 | `1.0.0-rc.16` |
| 形态 | **self-contained 便携 zip**（自带 .NET 10.0.11 Runtime） |
| 自动更新 | `update.json` + `update.json.sig`，ECDSA P-256 |
| 安装器 | **不提供**；发布单一便携 zip |
| FileVersion | `1.0.0.0` |
| 代码签名 | **NotSigned**（无 Authenticode；更新清单有独立签名） |

rc.16 在 rc.15 的基础上修复更新检查 Atom 源、Changes 工具栏与 Rollback 对话框、关闭原生模态后的前台恢复，并加入右键 `Copy Path/Reference…` 绝对路径复制。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.16-win-x64-portable.zip` | 39,298,494 B | `087171a3072c234f04f3b8e28197a84393b6cb8bb5c00bb196b856152dbc4890` |
| `update.json` | 432 B | `c83deafe0220e597018997dd82da432c2d214e39210e56e3ce8c01590c15d028` |
| `update.json.sig` | 89 B | `20c1b1f7e09f15878a4ee5b8ab6de7332dd1662f9b127d3c1b304b2454f6feb5` |
| `SHA256SUMS.txt` | 265 B | `422416bc9a7e0ae952a48a7741ec075b7ff74d296941ca962acd78b6a01700a0` |

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.0.0-rc.16 -OutputDirectory artifacts\release-1.0.0-rc.16
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.16\Augit-1.0.0-rc.16-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.16 -ExpectedVersion 1.0.0-rc.16 -SkipLaunch
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/create-release-manifest.ps1 -Version 1.0.0-rc.16 -Tag v1.0.0-rc.16 -ZipPath artifacts\release-1.0.0-rc.16\Augit-1.0.0-rc.16-win-x64-portable.zip -OutputDirectory artifacts\release-1.0.0-rc.16
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/verify-release-manifest.ps1 -ManifestPath artifacts\release-1.0.0-rc.16\update.json -SignaturePath artifacts\release-1.0.0-rc.16\update.json.sig
```

本机发布前门禁（2026-11-28）：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **PASS**：0 警告 / 0 错误 |
| Core 测试 | **PASS**：77/77 |
| Shell 测试 | **PASS**：286/286 |
| Infrastructure 测试 | **PASS**：221/221（排除 4 条 ConPTY 交互集成用例，与 CI 口径一致） |
| UI 资产一致性 / 脚本编码 / 文档声明 / 覆盖率 / `dotnet format` / `git diff --check` | **PASS** |
| 本版定向 live-shell | **PASS**：Copy Path/Reference 27、失败通知 4、Rollback 18、改动工具栏 5，合计 54/54 |
| 本地打包与内容校验 | **PASS**：本地 zip `39,298,431 B`、SHA-256 `c910991266c3af495f7ab0bf8468e90eb43f3595ef0b7254462c70d0ae589fe7`；版本 `1.0.0-rc.16+0047bbb…`、FileVersion `1.0.0.0`、web 资源一致、无 PDB |
| 本机自包含便携包真实启动 | **PASS**：窗口出现 `300.5ms`、首帧 `597.8ms`、关闭 `126.3ms`、退出码 0、无残留子进程 |
| 官方下载资产匿名核对 | **PASS**：四个资产大小与 SHA-256 与 `SHA256SUMS.txt` 一致；`update.json` 内置公钥验签 **PASS** |
| 官方自包含便携包真实启动 | **PASS**：窗口出现 `296.2ms`、首帧 `566.5ms`、关闭 `153ms`、退出码 0、无残留子进程；版本 `1.0.0-rc.16+a8983d6…`、FileVersion `1.0.0.0` |
| 全量 live-shell | **未通过**：历史断言基线含过期选择器（字体固定、分支弹层 `data-popover-action`、后台标签跟随外部变化、标题栏 Settings 入口）；本轮已修正部分，但套件仍被旧选择器挡住，不写成通过，见 §6 |

## 4. 自动更新

- rc.12 起内置签名自动更新；用户已确认 `rc.12→rc.13`、`rc.13→rc.14`、`rc.14→rc.15` 的公开升级路径均完成。
- 客户端读取 GitHub Release 的 `update.json` 和 `update.json.sig`，用内置 ECDSA P-256 公钥验签，再校验 tag、版本、GitHub HTTPS 地址、大小和 SHA-256。
- 下载完整 zip 后校验 `Augit.exe` ProductVersion；用户确认后由暂存的新版本执行替换，失败时回滚旧版本。
- **`rc.15 → rc.16` 的公开升级端到端尚未在本机完成**：公开 rc.15 便携包在本机正常入口启动时旧版 WebView2 环境未建立（进程退出，未进入界面），因此无法点击更新通知；rc.16 的公开资产与签名已核对，历史 rc.14→rc.15 公开升级由用户确认。该升级路径仍需在可正常启动 rc.15 的机器上复验，不把它写成通过。没有 Authenticode 代码签名，SmartScreen 仍可能提示未知发布者。

## 5. Windows 10 22H2 与用户确认

- 用户于 2026-11-28 确认在公司 Windows 10 22H2 实机连续运行多个 rc 版本，并确认 `rc.14 → rc.15` 公开升级完成。
- 此前 2026-10-26 的双显示器、DPI、负坐标和模拟热插拔证据继续有效（见[22H2 记录](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)）。
- rc.16 启动与更新仍待用户在公司实机复验；不把 Windows 11 结果写成 Windows 10 新包已验证。

## 6. 发布后核对

2026-11-28 已完成：

- 发布流水线 run `46121678948` 全部步骤 success；Release 页面标记为 **Pre-release**（id `461385505`），四个资产匿名可下载；
- 匿名下载的 zip、`update.json`、`update.json.sig`、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致；
- `update.json` 使用内置公钥验签 **PASS**；
- 官方便携包内容校验 **PASS**：必需文件、web 资源一致、无 PDB；`Augit.exe` 与三个托管程序集 ProductVersion `1.0.0-rc.16+a8983d6…`，FileVersion `1.0.0.0`；
- 官方便携包真实启动 **PASS**：窗口出现 296.2ms、首帧 566.5ms、关闭 153ms、退出码 0、无残留子进程；
- `rc.15 → rc.16` 自动更新端到端：**本机未完成**；公开 rc.15 在本机正常入口启动时 WebView2 环境未建立，未能进入更新界面。该路径仍需用户/可正常启动 rc.15 的机器复验；
- 全量 live-shell：**未通过**，原因是历史 harness 过期选择器；rc.16 定向块 54/54 通过，缺口保留。

状态：**PASS（预发布资产、官方启动与签名）**；`rc.15→rc.16` 公开升级和全量 live-shell 历史基线仍待复验。
