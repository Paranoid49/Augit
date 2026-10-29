# Augit 1.0.0-rc.9 公开发布清单

本清单记录 `1.0.0-rc.9` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.9> |
| Release id | 待发布流水线创建后回填 |
| Tag | `v1.0.0-rc.9` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | 以 `v1.0.0-rc.9` 标签指向的提交为准（发布后回填） |
| 版本 | `1.0.0-rc.9` |
| 便携版形态 | **self-contained**（自带 .NET 10.0.11 Runtime） |
| 安装版形态 | **framework-dependent**（安装器检测/下载 .NET 10） |
| FileVersion | `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`；.NET SDK 由 `global.json` 锁定；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

rc.9 集中修复窗口交互与品牌识别：非模态弹层点击外部关闭/穿透、标题栏 File/View/Git hover 菜单、项目名 project widget 下拉、正文行号栏，以及自有徽标和 exe/任务栏图标。rc.8 的 PyCharm 风格分支弹层继续保留。

## 2. 资产

待发布流水线构建完成后回填实际大小与 SHA-256：

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.9-win-x64-setup.exe` | 待回填 | 待回填 |
| `Augit-1.0.0-rc.9-win-x64-portable.zip` | 待回填 | 待回填 |
| `SHA256SUMS.txt` | 待回填 | 待回填 |

`SHA256SUMS.txt`：

```text
待发布流水线生成后回填
```

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet restore tools\packaging\Augit.Packaging.csproj --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.0.0-rc.9 -OutputDirectory artifacts\release-1.0.0-rc.9
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.9\Augit-1.0.0-rc.9-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.9 -ExpectedVersion 1.0.0-rc.9 -SkipLaunch
```

`tools/release.ps1` 分别构建两个发布目录：

- 安装版：framework-dependent，禁止包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll`，`runtimeconfig.json` 必须使用 `framework: Microsoft.NETCore.App`。
- 便携版：self-contained，必须包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 和 `includedFrameworks: Microsoft.NETCore.App`，不得依赖共享框架。
- self-contained 还原使用独立的 `packages.selfcontained.lock.json` 和 `--locked-mode`，不改写主 `packages.lock.json`。

门禁与结果：

| 项 | 结果 |
| --- | --- |
| Release 构建 | 待发布流水线回填 |
| Core 测试 | 待发布流水线回填 |
| Shell 测试 | 待发布流水线回填 |
| Infrastructure 测试 | 待发布流水线回填 |
| UI 资产一致性 | 待发布流水线回填 |
| 安装版 runtimeconfig | 待发布流水线回填 |
| 便携版 runtimeconfig | 待发布流水线回填 |
| 便携包内容/版本/web 树 | 待发布流水线回填 |
| 安装器版本资源 | 待发布流水线回填 |
| 安装器签名 | 待发布流水线回填 |

发布后匿名下载**实际 Release 资产**复验：

- 待发布后回填 zip、setup、`SHA256SUMS.txt` 的大小与 SHA-256。
- 待回填便携包真实启动、窗口类、启动/关闭耗时、退出码、残留进程和 ProductVersion。
- 待回填 `includedFrameworks` 和隐藏共享 .NET 启动结果。
- 待回填 rc.9 实际 Release 包的 CDP 交互验证（外部点击关闭/穿透、标题栏 hover 菜单、项目下拉、行号栏、任务栏图标）。
- 待回填安装器 ProductVersion、FileVersion、签名状态。

### 本机预构建（发布前同源验证）

本机在 `c15806c` 工作树上完成 rc.9 版本号/文档修改后构建，仅用于发布前验证；公开基准以 tag 流水线产出的资产为准。

- 便携包：`39,250,046 B`，SHA-256 `970f7b4cf43c9788805964e344980986542f38ddbc1207fd31dbf67a598a8fa8`。
- 安装器：`5,053,432 B`，SHA-256 `f2798fc3b78a6b7ffe79676cce26709e34ef90be6aed7258c8629d2d05004dcd`。
- ProductVersion：`1.0.0-rc.9+c15806cf143338a23ef7629b00d1f65e028a6ce9`；FileVersion `1.0.0.0`。
- 测试：Core **72/72**、Shell **271/271**、Infrastructure **207/207**；Release 构建 0 警告 / 0 错误；`dotnet format --verify-no-changes` 通过；`verify-ui-assets` PASS。
- 便携包真实启动：窗口类 `Augit.Shell.Window`、窗口出现 **506.8ms**、关闭 **98.6ms**、退出码 0、无残留子进程。
- 便携包隐藏共享 .NET（`DOTNET_ROOT` 指向不存在目录、禁用多级查找）启动：窗口出现 **463.9ms**、关闭 **89.1ms**、退出码 0、无残留子进程。
- 安装器版本资源：ProductVersion `1.0.0-rc.9`、FileVersion `1.0.0.0`、`NotSigned`；exe/安装器图标提取成功。
## 4. Windows 10 22H2 证据与边界

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载 `30,604,768` 字节的 .NET 10.0.11、校验并安装。

rc.9 的非模态弹层外部点击、标题栏菜单、project widget 下拉、行号栏和徽标/任务栏图标**尚未在该实机复验**；上述证据只覆盖运行代码相同的界面/DPI/安装部分。Windows 11 已完成本轮回归，但 Win10 22H2 仍需用户实测。

## 5. 与 rc.2–rc.8 的关系

- rc.2 便携版 framework-dependent，在没有 .NET 10 的新电脑上会先弹出 .NET 宿主错误。
- rc.3 把便携版改为 self-contained。
- rc.4 增加 Windows 10 22H2 启动加固、欢迎页、退出保护和本地诊断日志。
- rc.5 修复旧 `LastWorkspace` 等于程序目录/欢迎目录时仍恢复该目录。
- rc.6 修复选择目录的 `comctl32` 崩溃和新窗口 `--workspace` 参数错误。
- rc.7 修复欢迎页双窗口、分支树折叠和分支弹层默认右侧面板。
- rc.8 按 PyCharm 重做分支弹层分组树。
- rc.9 修复非模态弹层外部点击/穿透、标题栏菜单、项目下拉、行号栏，新增自有徽标与任务栏图标。
- rc.2–rc.8 的公开 Release 保留，不覆盖、不改写。

## 6. 发布后核对

待 `v1.0.0-rc.9` 发布流水线完成后回填：

- Release 是否标记为 **Pre-release**，三个资产是否匿名可下载。
- 匿名下载的 zip、setup、`SHA256SUMS.txt` 大小与 SHA-256 是否和发布正文一致。
- 实际 Release 便携包隐藏/失效共享 .NET 环境启动是否 PASS。
- rc.9 实际 Release 包的外部点击关闭/穿透、标题栏 hover 菜单、项目下拉、行号栏和任务栏图标是否通过 CDP/实机。
- 安装器版本资源与未签名状态。
- 默认分支 `main`、tag 和发布正文是否指向同一版本；README、Release 正文和 `SHA256SUMS.txt` 的下载链接是否一致。
- Windows 10 22H2 出问题机器复验、干净虚拟机/全新系统安装是否仍未完成。

状态：**待发布**。