# Stash 对话框取证（2026-10-18）

> **第 57 批更正**：本页的 CDP 取证与“截断”问题已由 [Stash 同态配对](stash-homologous-pair-2026-10-18.md) 取代；两复选已修复为同一行，正常入口 PID 5464 已实采，页面级配对计 13。本页保留为修复前诊断。

## 1. 结论

在**附 CDP 调试实例**中通过 DOM 点击 `data-main-menu-action="stash-create"` 打开了 Stash 对话框，并取得参考端 Stash 对话框；**实际图不是正常入口证据**，因此本批不计页面级配对（仍 12）。

- 实际：`Stash`；`Git root`（可编辑文本框）；`Current branch main`；`Message`；`Keep index`；`Include untracked files`；`Create Stash / Cancel`。窗口约 1085×579 物理。
  - **`Include untracked files` 行被底部 footer 截断**（只露出上半行，右侧有滚动条）；这是实际端的**布局缺陷候选**，需正常入口复验后再定性。
- 参考：`Stash` + PyCharm 图标；`Git Root:` 下拉；`Current Branch: main`；`Message:` + 历史图标；`Keep index`；`Create Stash / Cancel`；底部帮助图标。窗口约 815×499 物理；**没有 Include untracked files**。
- 相同：Stash 标题、Git root、当前分支 `main`、Message、Keep index、Create Stash/Cancel；均未执行 Stash。
- 已知差异：Git root 文本/下拉、Include untracked files（实际独有且截断）、图标/历史/帮助、标题大小写、窗口尺寸/行距。

## 2. 入口与限制

- 正常入口菜单：Git 菜单可见 `Create Stash…`（`data-main-menu-action="stash-create"`），但实际鼠标点击未打开；DOM 点击可打开，说明动作已接线，**鼠标命中/菜单层**未定位。
- 本批实际为 `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` 调试启动；不作为正常入口验收证据，也不计配对。
- 参考通过 Find Action `Stash Changes` 打开 `Stash` AWT 对话框。
- 未执行 Stash；fixture 无 Git 写操作。

## 3. 证据

- 实际（调试）：`artifacts/pixel-followup-2026-10-18/stash-dialog/actual/actual-stash-dialog.png`（`4460B664…F31B`）。
- 参考：`reference/reference-stash-dialog.png`（`3C672911…9621`）。
- 实际 Git 菜单：`actual/actual-git-menu.png`（`F595C3E5…8310`）。

## 4. 后续

- 正常入口用正确键盘/UIA 路径打开 Create Stash，复验 `Include untracked files` 是否仍被截断；确认后再决定修复布局并形成第 13 个页面级配对。
- 不得用本次 CDP 图宣称正常入口通过。

## 5. 清理

- 实际 PID 8560、参考 PID 2632 已结束；`settings.json` 恢复 `B8868855…DC3D`；无 Git 写；无 Augit/pycharm64 残留；无 9333 监听。
- 本批未修改代码；`cleanup-check-83.json`；未提交、未推送。
