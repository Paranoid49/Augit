# Augit 1.0.0-rc.16 公开发布清单

本清单记录 `1.0.0-rc.16` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源；本文件在流水线完成后补入实际资产、构建提交和发布 id。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.16> |
| Release id | 待发布流水线完成后补入 |
| Tag | `v1.0.0-rc.16` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | 待发布流水线完成后补入 |
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
| `Augit-1.0.0-rc.16-win-x64-portable.zip` | 待流水线完成后补入 | 待流水线完成后补入 |
| `update.json` | 待流水线完成后补入 | 待流水线完成后补入 |
| `update.json.sig` | 待流水线完成后补入 | 待流水线完成后补入 |
| `SHA256SUMS.txt` | 待流水线完成后补入 | 待流水线完成后补入 |

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
| 全量 live-shell | **未通过**：历史断言基线含过期选择器（字体固定、分支弹层 `data-popover-action`、后台标签跟随外部变化、标题栏 Settings 入口）；本轮已修正部分，但套件仍被旧选择器挡住，不写成通过，见 §6 |

## 4. 自动更新

- rc.12 起内置签名自动更新；用户已确认 `rc.12→rc.13`、`rc.13→rc.14`、`rc.14→rc.15` 的公开升级路径均完成。
- 客户端读取 GitHub Release 的 `update.json` 和 `update.json.sig`，用内置 ECDSA P-256 公钥验签，再校验 tag、版本、GitHub HTTPS 地址、大小和 SHA-256。
- 下载完整 zip 后校验 `Augit.exe` ProductVersion；用户确认后由暂存的新版本执行替换，失败时回滚旧版本。
- **`rc.15 → rc.16` 的公开升级端到端在发布后验证**；本轮不把历史升级成功当作本版已通过。没有 Authenticode 代码签名，SmartScreen 仍可能提示未知发布者。

## 5. Windows 10 22H2 与用户确认

- 用户于 2026-11-28 确认在公司 Windows 10 22H2 实机连续运行多个 rc 版本，并确认 `rc.14 → rc.15` 公开升级完成。
- 此前 2026-10-26 的双显示器、DPI、负坐标和模拟热插拔证据继续有效（见[22H2 记录](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)）。
- rc.16 启动与更新仍待用户在公司实机复验；不把 Windows 11 结果写成 Windows 10 新包已验证。

## 6. 发布后核对

待发布流水线完成后补入：

- Release 页面标记、Release id、构建提交；
- 四个资产的大小与 SHA-256、匿名下载核对、`update.json` 验签；
- 官方便携包真实启动；
- `rc.15 → rc.16` 公开升级端到端；
- 全量 live-shell 历史测试基线的缺口说明。
