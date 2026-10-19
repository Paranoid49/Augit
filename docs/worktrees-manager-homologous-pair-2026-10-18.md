# Worktrees 管理页同态配对（2026-10-18）

## 1. 结论

完成 Worktrees 管理取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Worktrees 管理、主工作树 `fixture`、分支 `main`、路径 `D:/JetBrains/PyCharm-2026.2.1-reference/fixture`、主工作树不可移除、新建/刷新/移除动作；均**未写 Git**；浅色、DPI168。
- 实际：独立 `Worktrees` 对话框；工具栏 `+`/删除/刷新；左侧 `Worktree Manager` 列表 `main · D:/JetBrains/PyCharm-2026....`；右侧详情 `main`、`Path`、`Status: Main worktree; cannot be removed`、`Terminal session: No running embedded terminal`；`Open Window`/`New Worktree`/`Remove...`（禁用）/`Close`；约 1627×639。
- 参考：底部 Git 工具窗口新增 `Worktrees` 标签；工具栏 `+`/删除/刷新/眼睛/`?`；单行 `fixture main D:/JetBrains/PyCharm-2026.2.1-reference/fixture`；无右侧详情/Open Window/Close；主窗口整页。
- 页面级同态配对 **20 → 21**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 25464：Git 菜单 → `Worktrees…`；打开独立对话框。
- 参考 PID 7596：Find Action `Worktrees`；打开 Git 工具窗口的 Worktrees 标签。

## 3. 已知差异

- 独立模态对话框 vs 底部工具窗口标签；实际有右侧详情、Open Window、Close；参考只有单行列表。
- 工具栏项目、禁用原因（实际 Remove 灰）、图标、尺寸/位置。
- 未测：多 Worktree、新建/移除/打开窗口、终端会话、错误/成功、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/worktree-manager-pair/actual/actual-worktrees.png`（`ADA1E3E4…AC2B`）。
- 参考：`reference/reference-worktrees.png`（`BA62339A…3D82`）。

## 5. 清理

- 实际 PID 25464、参考 PID 7596 已结束；`settings.json` 恢复 `B8868855…DC3D`；未写 Git；无残留；`cleanup-check-99.json`；未提交、未推送。
