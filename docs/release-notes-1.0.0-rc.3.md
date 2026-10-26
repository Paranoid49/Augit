# Augit 1.0.0-rc.3 发布说明

> **2026-10-26 后续**：rc.3 在 Windows 10 22H2 上出现首次启动/选择目录后退出的问题；rc.4 增加 CET 兼容、欢迎页、async void 异常边界、`git init` 超时和本地诊断日志。本文保留 rc.3 历史记录；当前公开版本见 [rc.4 发布说明](release-notes-1.0.0-rc.4.md)。

- 发布日期：2026-10-26
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：`1fee1aace3bb29358426ba37860b420b3eb3d4df`（GitHub Actions 从 tag 构建；`Augit.exe` ProductVersion 内含该提交号）
- 发布记录 / tag：`v1.0.0-rc.3`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

Augit 是面向「代码由外部 AI、脚本或人工修改」这一协作方式的轻量 Windows 只读项目查看器与 Git 图形工具。界面使用 C#/.NET 10 原生 Win32 外壳 + WebView2，不接入 AI，不提供内置文本编辑和保存。

rc.3 是 rc.2 的**分发修复版**：运行代码与 rc.2 相同，只把便携版从 framework-dependent 改为 **self-contained**。新电脑不再需要预装 .NET 10，解压后直接运行 `Augit.exe`。

## 2. 下载

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| [Augit-1.0.0-rc.3-win-x64-setup.exe](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.3/Augit-1.0.0-rc.3-win-x64-setup.exe) | 5,014,276 B | `814fea114ade1d6cac69642286e53175a63c97981701d2cad9f80bcca370f769` |
| [Augit-1.0.0-rc.3-win-x64-portable.zip](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.3/Augit-1.0.0-rc.3-win-x64-portable.zip) | 39,131,650 B | `92da12b350628cde6bb408ef8a6518a6394fb44a8b55bd455d93f9309ab0f09a` |
| [SHA256SUMS.txt](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.3/SHA256SUMS.txt) | 205 B | `8b9950ce8acfe6ec1d4b73f246f0626c138b70e56712aff16fc5f8e9f1cfc6c9` |

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.3>

- 上表与 Release 正文、附件 `SHA256SUMS.txt` 一致；发布后已匿名下载复核。
- **便携版**：已内置 .NET 10.0.11 Runtime，不需要预装 .NET；只要求系统已有 Microsoft Edge WebView2 Runtime。不写系统 PATH，不添加资源管理器菜单，不安装或修改共享运行时。
- **安装版**：仍是按需联网的小型安装器，需要管理员权限；缺少 .NET 10/WebView2 时先说明，经确认后从项目锁定的微软官方地址下载并校验哈希与 Microsoft 签名。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置、最近目录和会话状态。
- **未做代码签名**：Windows SmartScreen 可能提示「未知发布者」。请核对 `SHA256SUMS.txt`；不要因提示直接认定文件损坏。

### 与 rc.2 的关系

rc.2 的便携包是 framework-dependent。2026-10-26 在一台没有 .NET 10 的新电脑上运行时，先弹出 .NET 宿主错误对话框，而不是 Augit 自己的提示。rc.3 只修复这个分发问题：

- 便携版改为 self-contained，自带 .NET 10 Runtime；新电脑无需预装 .NET。
- 安装版保持 framework-dependent 的小型联网安装器，.NET 检测/下载逻辑不变。
- 运行时代码没有变化；Windows 10 22H2 的界面、DPI、安装等证据继续适用于 rc.3。
- rc.2 的公开资产保留在 `v1.0.0-rc.2` Release，不覆盖、不改写。

## 3. 本候选包含

- 只读浏览：文本、Markdown、JSON、图片（PNG/JPEG/BMP）、二进制/超限信息态、文件树、快速打开、全仓搜索、大文本窗口化预览。
- Git：Changes/Unversioned、只读 Diff（并排/统一、忽略空白）、提交历史与详情、文件历史、Blame、分支/标签/引用、Commit、Fetch/Pull/Push、Define/Edit Remote、Stash、Worktree、Smart Checkout、Reset/Rollback 等确认链路。
- 冲突：冲突状态与文件列表、普通只读 Diff、外部编辑/暂存后的状态同步，以及 Continue/Skip/Abort；不提供三栏冲突编辑器。
- 内置终端：xterm 简洁形态，PowerShell 默认，可配置其他 Shell，关闭运行中会话前确认并结束进程树。
- Settings：简化自定义结构，主题/字体/字号/面板尺寸/终端等设置；不重建 PyCharm 完整设置树。

## 4. 本版变化

- **便携版 self-contained**：发布目录包含 `coreclr.dll`、`hostfxr.dll`、`hostpolicy.dll` 与 `Augit.runtimeconfig.json` 的 `includedFrameworks`；解压后的 `Augit.exe` 不依赖共享 .NET 10 Runtime（includedFrameworks 10.0.11）。
- 发布脚本同时构建两个发布目录：framework-dependent 安装器输入与 self-contained 便携输入；分别校验依赖形态，禁止把两种运行时形态混装。
- 新增独立的 `packages.selfcontained.lock.json`，self-contained 还原仍使用 `--locked-mode`，不改写主 `packages.lock.json`。
- 本机验证：把 `DOTNET_ROOT` 指向不存在目录并禁用多级查找后，self-contained 便携包仍能启动；窗口类 `Augit.Shell.Window`、退出码 0、无残留子进程。
- README、架构 §7、运行时依赖说明和发布流程已同步为“便携版 self-contained、安装版联网安装器”。

## 5. 验证摘要

- Release 构建：0 警告、0 错误。
- 测试：Core **72/72**、Shell **268/268**、Infrastructure **202/202**（4 个需要交互式控制台的 ConPTY 用例仅在托管 CI 排除，真实桌面/Win10 保留验证）。
- UI 资产字节一致性、PowerShell 编码、文档数字自洽、coverage generator 检查通过。
- 发布后匿名下载实际 Release 便携包并启动 PASS（隐藏共享 .NET 路径）：窗口出现约 487.5ms、关闭约 109ms、退出码 0、无残留子进程；ProductVersion `1.0.0-rc.3+1fee1aa…`，`includedFrameworks` 为 `Microsoft.NETCore.App 10.0.11`。
- 安装器 ProductVersion `1.0.0-rc.3`、FileVersion `1.0.0.0`、`NotSigned`；.NET 检测/下载逻辑沿用 rc.2 的 Windows 10 22H2 实机证据。
- Windows 10 22H2 实机证据（双屏、100–200% DPI、负坐标、模拟热插拔、深色窄窗）继续适用于 rc.3，因为运行代码未变。

## 6. 系统要求

- Windows 10 22H2 x64 或 Windows 11 x64（不支持 32 位、ARM64、Windows Server、Windows 预览版、网络共享目录和远程文件系统）。
- **便携版**：自带 .NET 10 Runtime，无需预装 .NET。
- **安装版**：需要 .NET 10 Runtime；缺少时安装器先说明，经确认后下载锁定的微软运行时并校验哈希与 Microsoft 签名。
- Microsoft Edge WebView2 Runtime（Evergreen），两种包都需要。
- Git for Windows 2.40 或更高版本（仅 Git 功能需要）；Augit 不捆绑 Git。
- 认证交给 Git Credential Manager、SSH agent 或终端；Augit 不保存凭据。

## 7. 安装、便携、升级与卸载

- 安装版需要管理员权限，默认安装到 `C:\Program Files\Augit`；可选加入系统 PATH 和资源管理器「使用 Augit 打开」。
- 便携版解压后直接运行 `Augit.exe`，不写 PATH/右键菜单，不安装 .NET 或 WebView2。
- 覆盖升级沿用原安装目录、PATH 与右键菜单选择；保留 `%LOCALAPPDATA%\Augit` 的设置、最近目录和会话状态。
- 卸载删除程序文件、安装器自己加入的 PATH 项和右键菜单，不删除用户设置、工作区或共享运行时。

## 8. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.3.md)，重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- 干净虚拟机/全新系统安装、真实缺少 WebView2 的安装流程、三块及以上显示器、物理拔插显示器未验证。
- 整窗像素历史结果仍为 **0 张合格 / 50 组页面级配对**，未达成原整改目标；只登记持续改进。
- 500ms 历史 611/552/624ms 慢样本本批未复现、未归因；Settings 冷开 1 次 567.2ms，未连续达成 500ms。
- 80,000 行软换行退回 80,000 行 DOM，真实外壳首测 5.23s、页面级 3.62–9.78s；大文件建议关闭软换行。
- 离屏全文不在可访问树，真实屏幕阅读器连续朗读仍受限；Narrator 完整会话、JAWS/Braille 未验证。
- Diff 有 10MB/20MB 与 2000 行实现上限；搜索超时仅受控慢条件验证；Push 认证覆盖按专项记录限定。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 9. 证据与复验

- [发布清单](https://github.com/Paranoid49/Augit/blob/main/docs/release-manifest-1.0.0-rc.3.md)
- [rc.2 发布说明](https://github.com/Paranoid49/Augit/blob/main/docs/release-notes-1.0.0-rc.2.md)
- [已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.3.md)
- [Windows 10 22H2 多显示器验证](https://github.com/Paranoid49/Augit/blob/main/docs/win10-22h2-multimonitor-validation-2026-10-26.md)
- [Windows 10 22H2 DPI 修复复验](https://github.com/Paranoid49/Augit/blob/main/docs/win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)
- [发布流程](https://github.com/Paranoid49/Augit/blob/main/docs/releasing.md)

原始截图、日志和本机验收报告位于本机 `artifacts/`（Git 忽略），不在公开仓库或 Release 中；Release 只包含便携 zip、安装器和 `SHA256SUMS.txt`。

## 10. 下一步

1. 在真实日常仓库中观察本 RC：浏览、外部修改同步、提交与分支、拉取推送、冲突、终端、重启恢复。
2. 记录实际运行环境、问题、结果与回归依据；不在用户真实仓库制造破坏性测试。
3. 处理或裁定已知限制中的 500ms 慢样本、软换行构建、可访问性连续阅读、干净安装/WebView2 缺失、代码签名。
4. 完成观察后再讨论正式 1.0；本预发布不自动升级为正式 1.0。

## 11. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈，附版本（安装器标题或 `Augit.exe` 文件属性）、复现步骤、期望与实际结果。Augit 不做遥测/崩溃上报，不会自动上传代码、日志或使用数据。
