# Augit 1.0.0-rc.5 发布说明

> **2026-10-26 后续**：rc.6 修复选择目录后的 comctl32 崩溃和新窗口 --workspace 参数错误；本文保留 rc.5 历史记录，当前版本见 [rc.6 发布说明](release-notes-1.0.0-rc.6.md)。

- 发布日期：2026-10-26
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：`fd71d9f594a34a0f5d8547b99043eea3bd75ca9c`（GitHub Actions 从 tag 构建；`Augit.exe` ProductVersion 内含该提交号）
- 发布记录 / tag：`v1.0.0-rc.5`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

rc.5 是 rc.4 的 **Windows 10 22H2 首次启动补齐修复**：rc.4 已经能在“没有最近目录”时显示「Open Workspace」，但如果 rc.3/rc.4 曾把发布目录 `D:\Augit` 保存为 `LastWorkspace`，rc.4 会继续恢复该目录。rc.5 把 **程序目录和欢迎目录都视为欢迎页**，不再需要用户手动删除 `%LOCALAPPDATA%\Augit\settings.json`。

运行代码与 rc.4 相同，仅修改首次启动工作区判定；便携版仍为 self-contained，安装版仍为 framework-dependent 联网安装器。

## 2. 下载

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| [Augit-1.0.0-rc.5-win-x64-setup.exe](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.5/Augit-1.0.0-rc.5-win-x64-setup.exe) | 5,016,658 B | `9cdf4207e6e359f39c7a68c6d3e28e550229d7f20e76d01bdc17fbd8ba427e35` |
| [Augit-1.0.0-rc.5-win-x64-portable.zip](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.5/Augit-1.0.0-rc.5-win-x64-portable.zip) | 39,134,537 B | `42cc1c42ea85d3de2e1bfda7b34cce5f1354ec6bf202168d2ce68790fb50df9e` |
| [SHA256SUMS.txt](https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.5/SHA256SUMS.txt) | 205 B | `51acff6ded04637d5758ef090fbfa614ee76776609a2134132dcfdf133f9349b` |

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.5>

- 上表与 Release 正文、附件 `SHA256SUMS.txt` 一致；发布后已匿名下载复核。
- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只要求系统已有 Microsoft Edge WebView2 Runtime。
- **安装版**：按需联网的小型安装器，需要管理员权限；缺少 .NET 10/WebView2 时先说明，经确认后下载并校验。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置。

## 3. rc.5 的变化

- `ResolveLaunchWorkspace` 不再只看“当前目录是否程序目录”，而是检查最终工作区：
  - `WorkspaceRoot` 等于程序目录：使用 `%LOCALAPPDATA%\Augit\Welcome` 并显示「Open Workspace」；
  - `WorkspaceRoot` 等于欢迎目录：同上，设置 `welcome=1`，不自动弹出「Create Git Repository」；
  - 其他有效最近目录/显式 `--workspace`：保持原行为。
- 这覆盖了旧版本把发布目录写进 `LastWorkspace` 的升级场景，不需要用户清空设置。
- 其余 rc.4 修复保留：`CETCompat=false`、async void 异常边界、本地 `augit.log`、`git init` 30 秒超时与本地路径校验。

## 4. 验证摘要

- Release 构建：0 警告、0 错误。
- 测试：Core **72/72**、Shell **270/270**、Infrastructure **207/207**；新增“旧 LastWorkspace 等于程序目录时回到欢迎页”测试。
- 发布后匿名下载实际 Release 便携包，隐藏共享 .NET（`DOTNET_ROOT` 不存在）启动 PASS：窗口类 `Augit.Shell.Window`、窗口出现 **338ms**、关闭 **110ms**、退出码 0、无残留子进程；ProductVersion `1.0.0-rc.5+fd71d9f…`。
- 用 `lastWorkspace` 指向发布目录的设置启动，仍显示「Open Workspace」，`augit.log` 记录 `startup-workspace=welcome`；不需要删除旧设置。
- **Windows 10 22H2 实机复验**：rc.5 仍需在用户出问题的机器上验证首次启动/选择目录/创建仓库路径；rc.2/rc.3 的双屏/DPI/安装证据不覆盖该启动路径。
- 仍未验证：干净虚拟机、真实缺少 WebView2 的安装流程、三块及以上显示器、物理拔插显示器。

## 5. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.5.md)。重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- Win10 22H2 启动路径修复待用户实机复验；若仍异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段或 Windows 应用程序日志事件。
- 干净虚拟机、真实缺 WebView2、三屏/物理拔线未验证。
- 整窗像素历史结果仍为 0 张合格 / 50 组页面级配对；500ms 历史慢样本、Settings 567ms、80,000 行软换行、离屏全文连续朗读等边界继续披露。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 6. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
