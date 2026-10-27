# Augit 1.0.0-rc.8 公开发布清单

本清单记录 `1.0.0-rc.8` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.8> |
| Release id | `425164225` |
| Tag | `v1.0.0-rc.8` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建 |
| 构建提交 | `4d300cb208d314716e621f548b6872e64893955c` |
| 版本 | `1.0.0-rc.8` |
| 便携版形态 | **self-contained**（自带 .NET 10.0.11 Runtime） |
| 安装版形态 | **framework-dependent**（安装器检测/下载 .NET 10） |
| FileVersion | `1.0.0.0` |
| 构建环境 | GitHub 托管 `windows-latest`；.NET SDK 由 `global.json` 锁定；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

rc.8 按 PyCharm 2026.2 实机重做分支弹层：顶部动作、默认折叠的 `Recent`/`Local`/`Remote`/`Tags`、目录子树、搜索、固定高度滚动；不再把全部引用平铺成一条高列表，也不再默认显示右侧当前分支动作面板。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.8-win-x64-setup.exe` | 5,018,346 B | `36e656da0ff2dc986589189e37e80f1621ef071d1943d12219cd2128792694a3` |
| `Augit-1.0.0-rc.8-win-x64-portable.zip` | 39,137,822 B | `78bc1f44b7be262fd39f8259e422cf4374ee0a1f11ca70cd2af5c36f922c2f38` |
| `SHA256SUMS.txt` | 205 B | `4fde0ae3b53c8e22288b26d7e9fe621654d0cd403758c92a779b1b4d1c3572fb` |

`SHA256SUMS.txt`：

```text
78bc1f44b7be262fd39f8259e422cf4374ee0a1f11ca70cd2af5c36f922c2f38  Augit-1.0.0-rc.8-win-x64-portable.zip
36e656da0ff2dc986589189e37e80f1621ef071d1943d12219cd2128792694a3  Augit-1.0.0-rc.8-win-x64-setup.exe
```

## 3. 构建与验证

发布流水线执行：

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet restore tools\packaging\Augit.Packaging.csproj --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName!~ConPtyTerminalSessionTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-ui-assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.0.0-rc.8 -OutputDirectory artifacts\release-1.0.0-rc.8
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.8\Augit-1.0.0-rc.8-win-x64-portable.zip -OutDir artifacts\release-verify-1.0.0-rc.8 -ExpectedVersion 1.0.0-rc.8 -SkipLaunch
```

`tools/release.ps1` 分别构建两个发布目录：

- 安装版：framework-dependent，禁止包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll`，`runtimeconfig.json` 必须使用 `framework: Microsoft.NETCore.App`。
- 便携版：self-contained，必须包含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 和 `includedFrameworks: Microsoft.NETCore.App`，不得依赖共享框架。
- self-contained 还原使用独立的 `packages.selfcontained.lock.json` 和 `--locked-mode`，不改写主 `packages.lock.json`。

门禁与结果：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **0 警告 / 0 错误** |
| Core 测试 | **72/72** |
| Shell 测试 | **271/271** |
| Infrastructure 测试 | **207/207**；另 4 个 ConPTY 交互用例在托管 runner 排除、在真实桌面/Win10 验证 |
| UI 资产一致性 | **PASS** |
| 安装版 runtimeconfig | framework-dependent，无 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` |
| 便携版 runtimeconfig | self-contained，含 `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` 与 `includedFrameworks` |
| 便携包内容/版本/web 树 | **PASS**（流水线 `-SkipLaunch`） |
| 安装器版本资源 | ProductVersion `1.0.0-rc.8`；FileVersion `1.0.0.0` |
| 安装器签名 | `NotSigned` |

发布后匿名下载**实际 Release 资产**复验：

- 便携包 `39,137,822 B`、SHA-256 `78bc1f44…`；安装器 `5,018,346 B`、SHA-256 `36e656da…`，与发布正文和 `SHA256SUMS.txt` 一致。
- 便携包真实启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现 **460.1ms**、关闭 **108ms**、退出码 0、无残留子进程；ProductVersion `1.0.0-rc.8+4d300cb…`。
- `includedFrameworks` 为 `Microsoft.NETCore.App 10.0.11`；在 `DOTNET_ROOT` 指向不存在目录、禁用多级查找的环境下启动，证明不依赖共享 .NET。
- **实际 Release 包分支弹层 CDP 实测**（合成仓库：8 + 80 个本地分支、1 个远端引用、1 个标签）：
  - 打开时 `Recent`/`Local`/`Remote`/`Tags` 全部折叠，弹层约 434×272px；
  - 展开 `Local` 后目录组 `bulk`/`codex`/`feature`/`fix`/`release` 出现，树区 `overflow-y:auto`，`scrollHeight 2335 > clientHeight 461`；
  - 搜索 `codex` 后只剩 `Local`/`Local/codex` 与 3 个 `codex/*` 行；清空后恢复折叠/展开状态；无命中显示 `No matching branches`；
  - 顶部快捷键 `Ctrl+T`/`Ctrl+K`/`Ctrl+Shift+K`/`Ctrl+Alt+N` 存在；
  - `.branch-actions` 常驻面板数量为 0；分支行动作走右键。
- 安装器 ProductVersion `1.0.0-rc.8`、FileVersion `1.0.0.0`、`NotSigned`。
- 报告：`artifacts/release-1.0.0-rc.8-gh-verify/portable-report.json`（Git 忽略）。

本机预构建（提交 `86c5a03`，仅作发布前同源验证）：

- 便携包 `39,137,791 B`，SHA-256 `e11dcf83f6827c1a78bc59a9db6652564227d2ea11298fe8355f01d38c2bc94a`。
- 安装器 `5,018,406 B`，SHA-256 `e8f6095364f592cf7ab0c945e3c004404f86a708cdf8ae775c697e24215530fb`。

## 4. Windows 10 22H2 证据与边界

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载 `30,604,768` 字节的 .NET 10.0.11、校验并安装。

rc.8 的分支弹层/欢迎页/分支树修复**尚未在该实机复验**；上述证据只覆盖运行代码相同的界面/DPI/安装部分。Windows 11 已完成本轮回归，但 Win10 22H2 仍需用户实测。

## 5. 与 rc.2–rc.7 的关系

- rc.2 便携版 framework-dependent，在没有 .NET 10 的新电脑上会先弹出 .NET 宿主错误。
- rc.3 把便携版改为 self-contained。
- rc.4 增加 Windows 10 22H2 启动加固、欢迎页、退出保护和本地诊断日志。
- rc.5 修复旧 `LastWorkspace` 等于程序目录/欢迎目录时仍恢复该目录。
- rc.6 修复选择目录的 `comctl32` 崩溃和新窗口 `--workspace` 参数错误。
- rc.7 修复欢迎页双窗口、分支树折叠和分支弹层默认右侧面板。
- rc.8 按 PyCharm 重做分支弹层分组树。
- rc.2–rc.7 的公开 Release 保留，不覆盖、不改写。

## 6. 发布后核对

2026-10-27 已完成：

- Release 标记为 **Pre-release**，三个资产匿名可下载。
- 匿名下载的 zip、setup、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致。
- 实际 Release 便携包在隐藏/失效共享 .NET 的环境真实启动 PASS（见 §3）。
- 实际 Release 包的分支弹层默认折叠、目录组、滚动、搜索、无命中提示、快捷键、无右侧面板均通过 CDP。
- 安装器版本资源与未签名状态已核对。
- 默认分支 `main`、tag 和发布正文均指向同一版本；README、Release 正文和 `SHA256SUMS.txt` 的下载链接一致。
- **未完成**：在 Windows 10 22H2 出问题机器上复验 rc.8 的首次启动/选择目录/分支弹层；干净虚拟机/全新系统安装也未验证。

状态：**PASS**（Win10 22H2 实机与干净安装复验除外）。
