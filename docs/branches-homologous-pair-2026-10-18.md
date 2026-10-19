# 分支弹层同态配对（2026-10-18）

## 1. 结论

完成分支/动作弹层取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：顶部搜索框；`Update Project…`、`Commit…`、`Push…`、`New Branch…`、`Checkout Tag or Revision…`；`Local` 分组；`main` 分支；浅色、2880×1716、DPI168。
- 实际：搜索框 `Search branches and actions`；`main Current`；同时打开当前分支行菜单（`From main New Branch…`、`Compare with Workspace`、`New Worktree…`、`Push…`、`Rename…`）；无快捷键、无右上关闭/设置图标。
- 参考：`Search for branches and actions`；`main`；动作带 `Ctrl+T / Ctrl+K / Ctrl+Shift+K / Ctrl+Alt+N` 快捷键；右上关闭/设置图标；本次截图未展开当前分支行菜单。
- 页面级同态配对 **9 → 10**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 8292：点击标题栏分支 chip `main`（物理 380,35），打开分支弹层。
- 参考 PID 3460：点击顶部 branch widget `main`（物理 360,35），打开 `Git Branches` 弹层。
- 实际当前分支行菜单是弹层内的额外状态；没有为凑图删除该菜单。

## 3. 已知差异

- `Current` 标签、分支行菜单展开状态。
- 快捷键文案、右上关闭/设置图标、搜索框措辞。
- 弹层宽高/行高、阴影、分组间距；实际底部 Git Log 与参考底部 Git/Log/Console 的既有差异。
- 未测：搜索过滤、新建分支、检出标签/修订、比较、重命名、远端分支、空仓库。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/branches-pair/actual/actual-branches.png`（`C5E98521…824D`）。
- 参考：`reference/reference-branches.png`（`9000D7B2…246D`）。

## 5. 清理

- 实际 PID 8292、参考 PID 3460 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；参考未改主题/配置/文件；未触碰用户原有 PyCharm；无端口/应用残留。
- 本批未修改代码；`cleanup-check-78.json`；未提交、未推送。
