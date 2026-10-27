# Augit 1.0.0-rc.8 发布说明

- 发布日期：2026-10-27
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：tag `v1.0.0-rc.8` 指向的提交（由 GitHub Actions 构建；`Augit.exe` ProductVersion 内含提交号）
- 发布记录 / tag：`v1.0.0-rc.8`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

rc.8 按本机 PyCharm 2026.2 实机与 `D:/github/intellij-community` 重做**分支弹层**，不再把全部引用一次性平铺：

- `Recent` / `Local` / `Remote` / `Tags` 分组**默认全部折叠**，与 PyCharm 打开分支弹层的初始状态一致。
- 展开 `Local` 后按 `/` 建目录子树（如 `codex`、`bulk`、`feature`），分支行只显示名称；upstream 只放在 tooltip，不在行内挤一条很长的箭头文本。
- 弹层固定宽度，树区有最大高度和独立滚动；分支再多也不再把弹层拉成一整屏。
- 搜索框实时过滤分支；命中时自动展开所在分组，无命中显示 `No matching branches`。
- 顶部动作加回 PyCharm 的快捷键提示：`Ctrl+T`、`Ctrl+K`、`Ctrl+Shift+K`、`Ctrl+Alt+N`。
- 默认不再出现右侧“当前分支动作”面板；分支行动作仍通过右键/菜单键/Shift+F10 打开。

rc.7 的欢迎页单窗口、分支树折叠、欢迎页不轮询 `workspace/changes`，以及 rc.6 的选择目录缓冲区/新窗口 `--workspace`、CETCompat、async void 边界、本地日志全部保留。

## 2. 下载

<!-- RELEASE_ASSETS -->

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.8>

- 上表由发布流水线从本次实际构建的 `SHA256SUMS.txt` 生成；下载后请以 Release 附件中的 `SHA256SUMS.txt` 复核。
- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只要求系统已有 Microsoft Edge WebView2 Runtime。
- **安装版**：按需联网的小型安装器，需要管理员权限；缺少 .NET 10/WebView2 时先说明，经确认后下载并校验。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置。

## 3. 验证摘要

- Release 构建：0 警告、0 错误。
- 测试：Core **72/72**、Shell **271/271**、Infrastructure **207/207**；托管 CI 排除 4 个需要交互式控制台的 ConPTY 用例。
- CDP 实测（合成仓库：8 + 80 个本地分支、1 个远端引用、1 个标签）：
  - 弹层打开时 `Recent`/`Local`/`Remote`/`Tags` 全部折叠，弹层高度约 **272px**；
  - 展开 `Local` 后目录组 `bulk`/`codex`/`feature` 正确显示，树区 `overflow-y:auto`，`scrollHeight 2335 > clientHeight 461`；
  - 搜索 `codex` 后只剩 `Local`/`Local/codex` 与 3 个 `codex/*` 行；清空后恢复折叠/展开状态；
  - 无右侧 `.branch-actions` 常驻面板。
- 实际 Release 包仍需在 Windows 10 22H2 实机复验；rc.2/rc.3 的双屏/DPI/安装证据不覆盖本版 UI 路径。

## 4. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.8.md)。重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- 分支行的完整 PyCharm 行内 `NextStep` 子菜单尚未实现；当前分支动作走右键/菜单键/Shift+F10。默认右侧面板已移除。
- Win10 22H2 实机复验待完成；若仍异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段或 Windows 应用程序日志事件。
- 干净虚拟机、真实缺 WebView2、三屏/物理拔线未验证。
- 整窗像素历史结果仍为 0 张合格 / 50 组页面级配对；500ms 历史慢样本、Settings 567ms、80,000 行软换行、离屏全文连续朗读等边界继续披露。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 5. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。
