# Augit 1.0.0-rc.12 公开发布清单

本清单记录 `1.0.0-rc.12` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源；本文件在流水线完成后补入实际资产和构建提交。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.12> |
| Release id | `436146783` |
| Tag | `v1.0.0-rc.12` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | `3f52f11aece6939b654e5ca045fa980f64063840` |
| 版本 | `1.0.0-rc.12` |
| 形态 | **self-contained 便携 zip**（自带 .NET 10.0.11 Runtime） |
| 自动更新 | `update.json` + `update.json.sig`，ECDSA P-256 |
| 安装器 | **不再提供**；`tools/packaging` 已移除 |
| FileVersion | `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`；.NET SDK 由 `global.json` 锁定 |
| 代码签名 | **NotSigned**（无 Authenticode；更新清单有独立签名） |

rc.12 加入签名自动更新，改为单一自包含便携版发行；同时合入 rc.11 之后终端标签高亮、Settings 默认终端不影响已打开会话、移除无功能 Tab Options 和固定 UI 字体。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.12-win-x64-portable.zip` | 39,275,373 B | `cea4fb98e928a71254858b1cc544e4e6667e96d8e48e51b4bb31a15b6709587c` |
| `update.json` | 432 B | `8369497c0ce38a24355d45d5546d985d081bde97560d242d6d27070a1f135a83` |
| `update.json.sig` | 89 B | `0052e78c1fde7e6e7b52450c4782058d96d2ba3666bfab6092d48e3d2d386d11` |
| `SHA256SUMS.txt` | 265 B | `3534a64f0cadffc6b419a2bfb310c2c909e7459d4ea4e757948c74a286783c79` |

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.0.0-rc.12 -OutputDirectory artifacts\release-1.0.0-rc.12
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.12\Augit-1.0.0-rc.12-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.12 -ExpectedVersion 1.0.0-rc.12 -SkipLaunch
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/create-release-manifest.ps1 -Version 1.0.0-rc.12 -Tag v1.0.0-rc.12 -ZipPath artifacts\release-1.0.0-rc.12\Augit-1.0.0-rc.12-win-x64-portable.zip -OutputDirectory artifacts\release-1.0.0-rc.12
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/verify-release-manifest.ps1 -ManifestPath artifacts\release-1.0.0-rc.12\update.json -SignaturePath artifacts\release-1.0.0-rc.12\update.json.sig
```

`tools/release.ps1` 只构建 self-contained 便携目录，校验必需文件、第三方许可证、无 PDB 和 `includedFrameworks` 运行时配置；`create-release-manifest.ps1` 从仓库 Secret `AUGIT_UPDATE_SIGNING_KEY` 读取私钥并签名 `update.json`。

本机发布前门禁（2026-11-06）：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **PASS**：0 警告 / 0 错误 |
| Core 测试 | **PASS**：72/72 |
| Shell 测试 | **PASS**：281/281 |
| Infrastructure 测试 | **PASS**：216/216 |
| UI 资产一致性 | **PASS** |
| `dotnet format` / `git diff --check` | **PASS** |
| 便携包内容与版本 | **PASS** |
| 更新清单签名/复核 | **PASS**（本机私钥与发布流水线 Secret 均通过内置公钥复核） |
| 本机更新器端到端 | **PASS**：替换、窗口出现、备份清理 |
### 本机预构建（发布前同源验证）

- 便携包：`39,275,363 B`，SHA-256 `5290bbf8bd5c1a2e19968a78524103c8e9d7d13fb96b0072dedee81bb184aa29`。
- ProductVersion：`1.0.0-rc.12+ce7ec9b…`；FileVersion `1.0.0.0`。
- 真实启动：窗口出现 **471.9ms**、关闭 **136ms**、退出码 0、无残留子进程。
- `update.json` / `update.json.sig` 使用本机密钥签名并通过复核；发布 Secret 与内置公钥的配对由流水线复核。

## 4. 自动更新验证

- 客户端检查 GitHub Releases，选择最新 SemVer，下载 `update.json` 和签名。
- 验签后校验 tag、版本、GitHub HTTPS 地址、大小和 SHA-256。
- 下载的便携 zip 解压后校验 `Augit.exe` ProductVersion。
- `--apply-update` 等待旧进程退出，备份旧目录，复制新版本，等待新窗口出现；失败时回滚并重启旧版本。
- 同一目录多窗口、目标目录不可写、更新暂存目录与目标相同/包含关系均被拒绝。
- 本机端到端测试使用临时目标/暂存目录和测试父进程，不修改用户安装。

## 5. Windows 10 22H2 证据与边界

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机历史完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动。

rc.12 的自动更新、终端标签高亮、Settings 行为、固定 UI 字体和便携替换路径**尚未在 Windows 10 22H2 实机复验**；Windows 11 x64 完成本轮回归。

## 6. 与 rc.2–rc.12 的关系

- rc.2 便携版 framework-dependent。
- rc.3 便携版改为 self-contained。
- rc.4 增加 Windows 10 22H2 启动/选目录退出加固、欢迎页和本地诊断日志。
- rc.5 修复旧 `LastWorkspace` 回退。
- rc.6 修复选择目录 `comctl32` 崩溃和新窗口 `--workspace`。
- rc.7 修复欢迎页双窗口、分支树折叠和分支弹层右侧动作面板。
- rc.8 按 PyCharm 重做分支弹层分组树。
- rc.9 修复非模态弹层外部点击/穿透、标题栏菜单、项目下拉、行号栏，新增自有徽标与任务栏图标。
- rc.10 重做多标签终端、Shell 菜单、设置热切换与 JetBrains 关闭确认。
- rc.11 修复 Shell 菜单灰底、`+` 默认新建、Settings 预热和关闭确认像素。
- rc.12 加入签名自动更新、改为单一便携版发行，并修正终端标签高亮、Settings 默认终端、Tab Options 和 UI 字体。
- rc.2–rc.12 的公开 Release 保留，不覆盖、不改写。

## 7. 发布后核对

2026-11-06 已完成：

- Release 标记为 **Pre-release**，四个资产匿名可下载；Release id `436146783`。
- 匿名下载的 zip、`update.json`、`update.json.sig`、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致。
- `update.json` 使用内置公钥验签 **PASS**；发布流水线的独立验签步骤也通过。
- 实际 Release 便携包真实启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现 **482.1ms**、关闭 **111.1ms**、退出码 0、无残留子进程；ProductVersion `1.0.0-rc.12+3f52f11…`。
- 隐藏共享 .NET 启动 PASS：窗口出现 **468.2ms**、关闭 **111.6ms**、退出码 0、无残留子进程。
- **本机真实自动更新端到端**：用同源、带更新器但版本标为 `1.0.0-rc.11` 的测试便携构建，从公开 GitHub Release 检查到 rc.12，下载并验签 `update.json`，下载 39 MB 便携包，成功后重启替换；目标 `Augit.exe` ProductVersion 变为 `1.0.0-rc.12+3f52f11…`，备份目录清理，`update.log` 记录成功。
- **注意**：公开的 rc.11 本身没有更新器代码，因此 rc.11→rc.12 不是可用的真实升级路径；首个可自动升级的版本是 rc.12，下一次 rc.13 才能从 rc.12 直接升级。
- 校验 `main`、tag 和发布正文指向同一版本。

状态：**PASS**（Windows 10 22H2 实机、真实公网下载失败/断电/杀毒拦截等边界未复验）。
