# Augit 1.0.0-rc.2 公开发布清单

本清单记录 `1.0.0-rc.2` 公开预发布资产的构建源、哈希和本机验证结果。Release 页面以本清单与 `SHA256SUMS.txt` 为发布基准。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.2> |
| Tag | `v1.0.0-rc.2`（指向本文所在提交） |
| 构建源提交 | `ff1795b597802321da4fa57abec085f1bb5d02e9` |
| 版本 | `1.0.0-rc.2` |
| ProductVersion | `1.0.0-rc.2+ff1795b597802321da4fa57abec085f1bb5d02e9` |
| FileVersion | `1.0.0.0` |
| 构建时间 | 2026-10-26 10:59（Asia/Shanghai） |
| 构建环境 | Windows 11 x64 `10.0.26200`；.NET SDK `10.0.201`；Inno Setup `7.1.0`（`Tools.InnoSetup` NuGet） |
| 安装器 AppId | `{BC6B9385-88A8-4B7D-9F16-4CD4379F1B13}` |
| 代码签名 | **NotSigned**（Augit 自身未签名） |

构建源提交包含 Windows 10 22H2 多显示器/DPI 修复；发布记录提交只增加本文、发布说明和证据状态更新，不改变运行代码。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.2-win-x64-setup.exe` | 5,014,082 B | `e723545e4e30e30bf3c508387cadb49e74eec213e4369409159bc74c404fde4d` |
| `Augit-1.0.0-rc.2-win-x64-portable.zip` | 3,526,544 B | `e6827c25f23a89a93cfa8864f562afb6509d2522baeffac9397d274a4c1bf807` |
| `SHA256SUMS.txt` | 205 B | 内容见下 |

`SHA256SUMS.txt`：

```text
e6827c25f23a89a93cfa8864f562afb6509d2522baeffac9397d274a4c1bf807  Augit-1.0.0-rc.2-win-x64-portable.zip
e723545e4e30e30bf3c508387cadb49e74eec213e4369409159bc74c404fde4d  Augit-1.0.0-rc.2-win-x64-setup.exe
```

web 资源清单：`web/` 下 27 个文件，按“文件 SHA-256 + 相对路径”排序后写入 `web-resource-manifest.txt`；该文件字节的 SHA-256 为：

```text
76d8f3d6c8f2016fe4a026ef5b59ad6b4b882709c2c91f167468fba3b5e5532f
```

清单与上一 rc.2 冻结候选一致，说明公开资产的 web 内容没有在发布收口后发生漂移。

## 3. 构建与验证

执行命令：

```powershell
git status --short
dotnet restore Augit.slnx --locked-mode
dotnet restore tools\packaging\Augit.Packaging.csproj --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.0.0-rc.2 -OutputDirectory artifacts\release-1.0.0-rc.2-public
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-portable-package.ps1 -PortableZip artifacts\release-1.0.0-rc.2-public\Augit-1.0.0-rc.2-win-x64-portable.zip -OutDir artifacts\release-1.0.0-rc.2-public-verify -ExpectedVersion 1.0.0-rc.2
```

结果：

| 项 | 结果 |
| --- | --- |
| 工作区 | 构建时除既有未跟踪 `.tmp/` 外干净 |
| Release 构建 | **0 警告 / 0 错误** |
| Core 测试 | **72/72** |
| Shell 测试 | **268/268** |
| Infrastructure 测试 | **206/206**（合计 546） |
| UI 资产一致性 | **PASS**（`mockup.js`、`mockup.css`、`current-find.js`、`image-preview.js` 与视觉稿字节一致） |
| PowerShell 编码 | **PASS**（32 个 `.ps1`） |
| 文档数字自洽 | **DOC_CLAIMS_OK** |
| 便携包内容/版本/web 树 | **PASS**：无缺文件，ProductVersion 为 `1.0.0-rc.2+ff1795b…`，web 树与仓库一致 |
| 便携包真实启动 | **PASS**：窗口类 `Augit.Shell.Window`；窗口出现 353.4ms；2065×1330；前台；关闭 112.5ms；退出码 0；无残留子进程 |
| 安装器版本资源 | ProductVersion `1.0.0-rc.2`；FileVersion `1.0.0.0`；ProductName `Augit` |
| 安装器签名 | `NotSigned` |

便携包验证截图：`artifacts/release-1.0.0-rc.2-public-verify/portable-launch.png`（本机证据，Git 忽略）；报告：`artifacts/release-1.0.0-rc.2-public-verify/portable-report.json`。

## 4. Windows 10 22H2 证据

Windows 10 Pro 22H2 `10.0.19045.6456` 双显示器实机已完成：

- 双屏 2560×1440 + 1920×1080，真实鼠标跨屏拖动。
- 100%/125%/150%/175%/200% 缩放与混合 DPI 启动。
- 负坐标显示器布局、模拟显示器热插拔、深色 1024×640 窄窗。
- rc.2 源码重建包便携启动与静默安装；缺少 .NET 10 时安装器确认、下载 `30,604,768` 字节的 .NET 10.0.11、校验并安装。

详见[22H2 多显示器 DPI 修复与扩展验证](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)。该页记录的早期源码重建包哈希（`3a9d8276…`、`1a0c2d9a…` 等）是不同构建批次，不是本清单资产；以本清单和 Release 为准。

## 5. 与旧 rc.2 冻结构建的关系

2026-10-25 的旧 rc.2 冻结构建（`8643b9e`）从未上传到 GitHub Release、没有 tag，也没有公开资产。旧文档中的便携包 `3,522,601 B / e339da49…` 与安装器 `5,010,098 B / ecb00134…` 是本机构建记录，无法作为公开资产复核。

本次公开 rc.2：

- 由包含 Win10 22H2 多显示器/DPI 修复的源码重建；
- 以本清单的大小和 SHA-256 为唯一发布基准；
- 不冒充旧哈希，也不把旧构建记录改写成新字节。

## 6. 发布后核对

发布后应以匿名 HTTP 下载再次核对：

- 三个 Release 资产 URL 可访问；
- 下载文件大小与 SHA-256 与本清单一致；
- Release 标记为 Pre-release；
- `main` 分支包含本清单和发布说明，tag 指向发布记录提交。

本节的实际下载核对结果记录在发布后的后续文档提交中；tag 本身只保证发布记录和资产哈希，不把“上传成功”写成“下载已复核”。
