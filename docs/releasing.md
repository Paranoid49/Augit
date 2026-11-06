# Augit 发布流程

本文描述 Augit 的可复验发布流程。产品行为以 `docs/product-spec.md` 为准；本文只规定构建、打包、验证和上传步骤。

## 1. 发布身份

当前公开发布为 `1.0.0-rc.12` 预发布候选，不是正式 1.0。发布资产：

- `Augit-<版本>-win-x64-portable.zip`
- `update.json`（签名发布清单）
- `update.json.sig`
- `SHA256SUMS.txt`

构建与打包由 `tools/release.ps1` 完成；更新清单由 `tools/update/create-release-manifest.ps1` 签名。脚本不签名可执行文件、不自动上传，也不修改用户目录。

## 2. 版本策略

- 当前候选线为 `1.0.0-rc.N`；正式 1.0 前不改变产品名和便携目录约定。
- 每次公开资产重建都必须更新发布清单和 `SHA256SUMS.txt`；不得用旧哈希描述新字节。
- 2026-10-25 的旧 rc.2 冻结构建（`8643b9e`）从未公开上传。当前公开 rc.12 由包含 Windows 10 22H2 多显示器/DPI 修复的源码重建；构建源提交见 `docs/release-manifest-1.0.0-rc.12.md`。
- 正式 1.0 需要完成 `docs/known-limitations-1.0.0-rc.12.md` §8 的真实使用观察与用户裁决，不由 CI 自动升级。

## 3. 发布前门禁

在本地或 CI 中依次执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-script-encoding.ps1
node tools\audit\check-doc-claims.cjs
node tools\audit\coverage-generator.test.cjs
```

平台相关验证：

- Windows 11 x64：便携启动、主流程、应用内更新下载/重启替换/回滚。
- Windows 10 22H2 x64：双显示器、混合 DPI、负坐标、模拟热插拔、深色窄窗、便携启动。
- 当前不具备的条件（干净虚拟机、真实缺 WebView2、更多显示器拓扑、Narrator/JAWS/Braille）必须继续标为未验证，不得用自动化或单屏结果替代。

## 4. 本地构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File tools\release.ps1 `
  -Version 1.0.0-rc.12 `
  -OutputDirectory artifacts\release-1.0.0-rc.12
```

脚本会：

1. 以 `--locked-mode` 还原解决方案；
2. 使用 `packages.selfcontained.lock.json` 以 `--locked-mode` 还原 self-contained 依赖图，生成自带 .NET 10 的 win-x64 便携目录；
3. 删除 WebView2 XML 文档与 PDB，复制 README、LICENSE、第三方说明和许可证；
4. 校验便携目录必须包含 self-contained 运行时且 `runtimeconfig.json` 使用 `includedFrameworks`；
5. 校验必需文件、锁定运行时资产、无 PDB；
6. 生成 self-contained 便携 zip 与 `SHA256SUMS.txt`，清理 staging。

输出目录必须位于仓库内。发布脚本不会上传，也不会修改用户安装。`-RuntimeFrameworkVersion` 默认 `10.0.11`，与锁定的 .NET 安全补丁基线一致；self-contained 便携包会把它写入 `Augit.runtimeconfig.json` 的 `includedFrameworks`。

## 5. 本地验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File tools\audit\verify-portable-package.ps1 `
  -PortableZip artifacts\release-1.0.0-rc.12\Augit-1.0.0-rc.12-win-x64-portable.zip `
  -OutDir artifacts\release-1.0.0-rc.12-verify `
  -ExpectedVersion 1.0.0-rc.12
```

有交互桌面时，该脚本会真实启动便携包、截图、关闭并检查进程树；无交互桌面时追加 `-SkipLaunch`，只做内容、版本和 web 资源一致性校验。

self-contained 便携包还应隐藏共享 .NET 后启动，确认不依赖系统共享 .NET：

```powershell
$env:DOTNET_ROOT = 'C:\does-not-exist-dotnet'
$env:DOTNET_ROOT_X64 = 'C:\does-not-exist-dotnet'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
powershell -NoProfile -ExecutionPolicy Bypass `
  -File tools\audit\verify-portable-package.ps1 `
  -PortableZip artifacts\release-1.0.0-rc.12\Augit-1.0.0-rc.12-win-x64-portable.zip `
  -OutDir artifacts\release-1.0.0-rc.12-verify-hidden-dotnet `
  -ExpectedVersion 1.0.0-rc.12
```

## 6. CI 与发布流水线

- `.github/workflows/ci.yml`：push/PR 到 `main`/`dsh` 时执行还原、Release 构建、全部测试和仓库审计。托管 Windows runner 没有可供 ConPTY 使用的交互式控制台，因此 CI 排除 `ConPtyTerminalSessionTests` 的 4 个交互式集成用例；这些用例在本地真实桌面和 Windows 10 22H2 实机验证。托管 runner 的 `FileSystemWatcher` 延迟也不代表产品路径，CI 显式设置 `AUGIT_WATCHER_BUDGET_MS=1500`；本地默认和产品验收仍断言 500ms。
- 仓库 Secret `AUGIT_UPDATE_SIGNING_KEY` 必须设置为 PKCS#8 私钥的 Base64；缺失时签名步骤会失败，不能发布无签名更新清单。生成与轮换见 [`auto-update.md`](auto-update.md)。
- `.github/workflows/release.yml`：
  - `workflow_dispatch` 手动触发，输入版本号；`publish=true` 时创建或更新 GitHub Release。
  - 推送 `v*` 标签会从该标签构建并发布对应 Pre-release；发布正文中的资产表由流水线根据本次实际 `SHA256SUMS.txt` 生成。
  - Release job 使用 `contents: write` 和仓库自带 `GITHUB_TOKEN`；不使用长期 PAT。
  - 打包后以 `-SkipLaunch` 校验便携包内容，随后用 `AUGIT_UPDATE_SIGNING_KEY` 生成 `update.json`/`update.json.sig` 并写入 `SHA256SUMS.txt`；启动验证仍以真实机器证据为准。发布后必须下载实际资产再复验一次。

## 7. 打标签与创建 Release

推荐由发布流水线构建公开资产，避免“本机字节”和“公开字节”不是同一份：

1. 完成代码、README、CI 和发布文档，提交并推送 `main`。
2. 在发布提交上创建并推送 tag：

```powershell
git tag v1.0.0-rc.12
git push origin v1.0.0-rc.12
git push origin dsh:main
```

3. tag 推送触发 `.github/workflows/release.yml`：构建、测试、打包、校验便携包内容，生成带实际资产表的 Release 正文，然后创建或更新 Pre-release 并上传 zip、`update.json`、`update.json.sig`、`SHA256SUMS.txt`。
4. 也可以手动触发 `Release` workflow，输入版本、`publish=true`；发布逻辑相同。
5. 发布后从 Release 匿名下载全部资产，核对大小和 SHA-256，并把验证结果与构建提交写回 `docs/release-manifest-<版本>.md`（仅文档提交）。
6. 若 GitHub CLI 已授权，手动补发/修复资产可使用：

```powershell
gh release create v1.0.0-rc.12 `
  artifacts\release-1.0.0-rc.12\Augit-1.0.0-rc.12-win-x64-portable.zip `
  artifacts\release-1.0.0-rc.12\update.json `
  artifacts\release-1.0.0-rc.12\update.json.sig `
  artifacts\release-1.0.0-rc.12\SHA256SUMS.txt `
  --title "Augit 1.0.0-rc.12" `
  --notes-file docs\release-notes-1.0.0-rc.12.md `
  --prerelease
```

7. 发布后用匿名 HTTP 请求核对便携包、`update.json`、`update.json.sig` 和 `SHA256SUMS.txt` 的 URL、大小和 SHA-256；不要只核对本机文件。

## 8. 发布后检查

- Release 页面显示 Pre-release，资产可匿名下载。
- `SHA256SUMS.txt` 与下载文件哈希一致。
- `Augit.exe` 的 ProductVersion 包含预期版本号和构建源提交。
- README 的下载链接指向实际 Release；`latest` 链接不适用于 Pre-release。
- 默认分支 `main` 已包含当前 README、CI 和发布清单；tag 指向被构建并发布的提交。
- 已知限制、便携版说明和反馈入口与实际一致。

## 9. 回滚

- 资产错误：在 GitHub Release 中删除或替换错误资产，更新 `SHA256SUMS.txt`、发布清单和 README，再重新验证；不得保留无法解释的旧哈希。
- 版本错误：保留已发布版本记录，发布新的 `rc.N+1`，不要静默复用旧版本号发布不同字节。
- 便携包或应用内更新回归：立即将 Release 标记为 Pre-release 并在发布说明顶部标注问题；修复后重新走完整流程。

## 10. 签名与 SmartScreen

当前没有 Augit 自身 Authenticode 证书，可执行文件仍可能触发 SmartScreen「未知发布者」提示；自动更新使用独立的 ECDSA P-256 发布清单签名，私钥只存于 GitHub Actions Secret `AUGIT_UPDATE_SIGNING_KEY`，客户端内置公钥。获得 Authenticode 证书后，在打包后增加签名步骤，并在发布清单记录指纹与时间戳服务器，不改变本文的验证门禁。
