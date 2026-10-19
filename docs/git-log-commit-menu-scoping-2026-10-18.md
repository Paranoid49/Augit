# Git Log 提交右键菜单取证（2026-10-18）

> **第 64 批更正**：提交行物理中心为 `(1411,1288)`，在该点右键已打开菜单并登记为 [Git Log 提交右键菜单同态配对](git-log-context-menu-homologous-pair-2026-10-18.md)（页面级 16）；本页保留入口定位过程。

## 1. 结论

取得参考端 Git Log 提交右键菜单；实际端本次未打开对应菜单，**未建立同态配对**，页面级仍 15。

- 参考（`reference-commit-menu.png`）提交右键菜单包含：
  `Copy Revision Number`、`Create Patch…`、`Cherry-Pick`、`Checkout Revision`、`Show Repository at Revision`、`Compare with Local`、`Reset Current Branch to Here…`、`Revert Commit`、`Undo Commit…`、`Edit Commit Message… (F2)`、`Fixup…`、`Squash Into…`、`Drop Commit`、`Interactively Rebase from Here…`、`Push All up to Here…`、`New Branch… (Ctrl+Alt+N)`、`New Tag…`、`Go to Child Commit` 等。
- 实际：在 Git Log 提交行 `(1000,1255)/(1000,1234)` 右键未出现菜单；一次落在 `Text or hash` 筛选并聚焦，另一次无变化。Commit 工具窗口 `app.py` 右键也未出现 Changes 菜单。
- 第 63 批补充：点击提交行 `(1000,1255)` 后按 `Shift+F10` 仍未出现菜单（截图哈希 `56D24943…B799`，默认界面）；提交行的可聚焦/命中仍未建立，入口不判缺陷。
- 因此本次未到达实际 Git Log 右键菜单/Changes 右键菜单，不据此判“缺失”或“缺陷”；入口坐标/命中层未定位。
- 未执行任何 Git 写操作。

## 2. 证据

- 参考提交菜单：`artifacts/pixel-followup-2026-10-18/reset-pair/reference/reference-commit-menu.png`（`89CF42EE…7ED7`）。
- 实际无菜单：`git-log-menu-pair/actual/actual-git-log-menu2.png`（`6C3215E8…C7BC`）；第一次误命中筛选：`actual/actual-git-log-menu.png`。
- 实际 Changes 右键尝试无菜单：`rollback-pair/actual/actual-changes-menu2.png`。

## 3. 后续

- 用 UIA/键盘菜单键（Shift+F10）定位 Git Log 提交行，确认实际菜单条目与禁用原因；核对产品规格 §7.9/§5.4 的入口要求，不因自动化未命中判缺陷。

## 4. 清理

- 实际 PID 22832 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；无参考进程；无 Git 写；无残留；`cleanup-check-89.json`；未提交、未推送。
