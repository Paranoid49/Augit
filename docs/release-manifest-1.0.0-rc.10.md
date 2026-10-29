# Augit 1.0.0-rc.10 公开发布清单

本清单记录 `1.0.0-rc.10` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源；本文件在流水线完成后补入实际资产和构建提交。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.10> |
| Release id | 流水线完成后填写 |
| Tag | `v1.0.0-rc.10` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | 流水线完成后填写（`Augit.exe` ProductVersion 内含该提交号） |
| 版本 | `1.0.0-rc.10` |
| 便携版形态 | **self-contained**（自带 .NET 10.0.11 Runtime） |
| 安装版形态 | **framework-dependent**（安装器检测/下载 .NET 10） |
| FileVersion | `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`；.NET SDK 由 `global.json` 锁定；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

rc.10 重做终端工具窗：多标签、独立 ConPTY 会话、稳定 `Local` / `Local (2)` 标签、标题栏 Shell 菜单、设置热切换和 JetBrains 风格关闭确认。同时合入 rc.9 之后的标题栏菜单对齐、普通对话框 ×、项目窗口标题和设置窗口预热修复。

## 2. 资产

待 GitHub Actions 流水线完成后填写：

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.10-win-x64-setup.exe` | 待填写 | 待填写 |
| `Augit-1.0.0-rc.10-win-x64-portable.zip` | 待填写 | 待填写 |
| `SHA256SUMS.txt` | 待填写 | 待填写 |

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet restore tools\packaging\Augit.Packaging.csproj --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.0.0-rc.10 -OutputDirectory artifacts\release-1.0.0-rc.10
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.10\Augit-1.0.0-rc.10-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.10 -ExpectedVersion 1.0.0-rc.10 -SkipLaunch
```

`tools/release.ps1` 分别构建两个发布目录：

- 安装版：framework-dependent，禁止包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll`，`runtimeconfig.json` 必须使用 `framework: Microsoft.NETCore.App`。
- 便携版：self-contained，必须包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 和 `includedFrameworks: Microsoft.NETCore.App`，不得依赖共享框架。
- self-contained 还原使用独立的 `packages.selfcontained.lock.json` 和 `--locked-mode`，不改写主 `packages.lock.json`。

本机发布前门禁（2026-10-29）：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **PASS**：0 警告 / 0 错误 |
| Core 测试 | **PASS**：72/72 |
| Shell 测试 | **PASS**：278/278 |
| Infrastructure 测试 | **PASS**：207/207；另 4 个 ConPTY 交互用例在真实桌面验证 |
| UI 资产一致性 | **PASS** |
| JavaScript 语法 / `git diff --check` | **PASS** |

### 本机预构建（发布前同源验证）

本机在 `0b53cd4` 工作树上完成 rc.10 版本号/文档修改后构建，仅用于发布前验证；公开基准以 tag 流水线产出的资产为准。

- 便携包：`39,258,260 B`，SHA-256 `9f950350b866c3a78455992eb8fbbc9677df6ffac9034b3ed19b072e6a6b0a01`。
- 安装器：`5,059,361 B`，SHA-256 `0e88c1736e38789eff110be93643d66f15061cc972509f8daa4c434c1c37b34f`。
- ProductVersion：`1.0.0-rc.10+0b53cd4d2f5bb649191f8b3ee6a90a16d5a88dac`；FileVersion `1.0.0.0`。
- 便携包真实启动：窗口出现 **483.1ms**、关闭 **103.8ms**、退出码 0、无残留子进程。

## 4. 终端修复验证

Windows 11 x64 实机：

- 同时打开 PowerShell、CMD、Git Bash；切换标签、关闭单个标签、关闭全部标签。
- `+` / `⌄` 菜单直接启动 CMD、Git Bash；Git Bash 正常显示提示符。
- Settings → Terminal 的 `Windows PowerShell → Git Bash → Windows PowerShell` 双向热切换，不重启应用。
- `ping -t` 前台进程关闭弹出 `Process 'Terminal Local' Is Running`，`Cancel` 保留、`Terminate` 结束并收起。
- 标签保持 `Local` / `Local (2)` 编号，关闭后新建不无限增长。

## 5. Windows 10 22H2 证据与边界

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载、校验并安装。

rc.10 的多终端、Shell 热切换、JetBrains 关闭确认、标题栏菜单、project widget 下拉、行号栏和徽标/任务栏图标**尚未在 Windows 10 22H2 实机复验**；上述证据只覆盖运行代码相同的界面/DPI/安装部分。Windows 11 x64 已完成本轮回归。

## 6. 与 rc.2–rc.9 的关系

- rc.2 便携版 framework-dependent，在没有 .NET 10 的新电脑上会先弹出 .NET 宿主错误。
- rc.3 把便携版改为 self-contained。
- rc.4 增加 Windows 10 22H2 启动/选目录退出加固、欢迎页、退出保护和本地诊断日志。
- rc.5 修复旧 `LastWorkspace` 等于程序目录/欢迎目录时仍恢复该目录。
- rc.6 修复选择目录的 `comctl32` 崩溃和新窗口 `--workspace` 参数错误。
- rc.7 修复欢迎页双窗口、分支树折叠和分支弹层默认右侧面板。
- rc.8 按 PyCharm 重做分支弹层分组树。
- rc.9 修复非模态弹层外部点击/穿透、标题栏菜单、项目下拉、行号栏，新增自有徽标与任务栏图标。
- rc.10 重做多标签终端、Shell 菜单、设置热切换与 JetBrains 关闭确认；同时合入 rc.9 之后的窗口修复和设置预热。
- rc.2–rc.9 的公开 Release 保留，不覆盖、不改写。

## 7. 发布后核对

待 GitHub Actions 完成后填写：

- Release 标记为 **Pre-release**，三个资产匿名可下载。
- 匿名下载的 zip、setup、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致。
- 实际 Release 便携包真实启动、版本资源与关闭清理。
- 安装器 ProductVersion / FileVersion / 签名状态。
- 默认分支 `main`、tag 和发布正文指向同一版本。

状态：**待发布流水线完成**。
