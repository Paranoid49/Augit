# Augit 1.0.0-rc.7 发布说明

- 发布日期：2026-10-27
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：tag `v1.0.0-rc.7` 指向的提交（由 GitHub Actions 构建；`Augit.exe` ProductVersion 内含提交号）
- 发布记录 / tag：`v1.0.0-rc.7`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

rc.7 修复 rc.6 实机使用中暴露的三类问题：

1. **首次打开后出现两个窗口**：从欢迎页选择目录后，目标项目在新窗口启动，但欢迎窗口仍留着。现在目标窗口成功启动/激活后，欢迎窗口关闭，只保留项目窗口。
2. **分支列表无法折叠**：`.tree-row` 自己声明 `display: flex`，覆盖了浏览器对 `hidden` 属性的 `display: none`；折叠只改了属性、行仍可见。现在全局恢复 `[hidden] { display: none !important; }`，`Local`/`codex`/标签分组的折叠与搜索过滤都真正生效。
3. **打开分支列表时右侧动作面板无条件出现**：`liveBranchesPopover()` 把“当前分支”的动作常驻在分支列表右侧，遮挡日志且不是 PyCharm 的行为。现在默认只显示分支/标签列表；分支行动作仍通过右键（菜单键/Shift+F10）打开。

同时处理欢迎页的两个派生问题：

- 欢迎空目录不再启动 `workspace/changes` 轮询，也不读取 Git 状态/历史/引用，避免启动页弹出 `host-timeout:workspace/changes` 错误 toast。
- 欢迎窗口在项目窗口打开后自行关闭，不再留下一个 `Welcome` 项目窗口。

rc.6 的选择目录 `comctl32` 缓冲区修复、新窗口 `--workspace` 参数修复、CETCompat、async void 边界和本地日志全部保留。

## 2. 下载

<!-- RELEASE_ASSETS -->

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.7>

- 上表由发布流水线从本次实际构建的 `SHA256SUMS.txt` 生成；下载后请以 Release 附件中的 `SHA256SUMS.txt` 复核。
- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只要求系统已有 Microsoft Edge WebView2 Runtime。
- **安装版**：按需联网的小型安装器，需要管理员权限；缺少 .NET 10/WebView2 时先说明，经确认后下载并校验。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置。

## 3. 验证摘要

- Release 构建：0 警告、0 错误。
- 测试：Core **72/72**、Shell **271/271**、Infrastructure **207/207**；托管 CI 排除 4 个需要交互式控制台的 ConPTY 用例。
- 本机 CDP 实测：
  - 分支树点击 `Local` 后，`codex`/`feature` 等行实际 `display: none`、高度 0，只保留 HEAD 与 `Tags`；`Local/codex` 单独折叠同样生效。
  - 打开分支弹层后 DOM 中 `.branch-actions` 数量为 0，只有分支/标签列表。
  - 欢迎页等待 12 秒无 `Cannot Read Workspace Changes` toast；`workspace/changes` 不再启动。
  - 欢迎页选择 `D:\tmp\augit-branch-ui` 后，欢迎进程退出，目标项目窗口启动；日志为 `startup args=--workspace D:\tmp\augit-branch-ui`。
- 仍待 **Windows 10 22H2 实机**复验；rc.2/rc.3 的双屏/DPI/安装证据不覆盖本版 UI 路径。

## 4. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.7.md)。重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- Win10 22H2 实机复验待完成；若仍异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段或 Windows 应用程序日志事件。
- 分支列表行的右键动作仍可用；行尾 chevron 目前只是视觉提示，不代表二级菜单（PyCharm 的完整行内子菜单尚未实现）。
- 干净虚拟机、真实缺 WebView2、三屏/物理拔线未验证。
- 整窗像素历史结果仍为 0 张合格 / 50 组页面级配对；500ms 历史慢样本、Settings 567ms、80,000 行软换行、离屏全文连续朗读等边界继续披露。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 5. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
