# Git Log 提交右键菜单同态配对（2026-10-18）

## 1. 结论

完成提交 `e9188bf` 右键菜单取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：提交 `e9188bf`；`Cherry-pick`、比较（实际 `Compare with Workspace` / 参考 `Compare with Local`）、`Reset Current Branch Here…`、`Revert Commit`、`New Branch…`、`New Tag…`；浅色、DPI168；未执行任何动作。
- 实际：`Copy Commit Hash`、`Cherry-pick`、`Compare with Workspace`、`Reset Current Branch Here...`、`Revert Commit`、`New Branch...`、`New Tag...`。
- 参考：`Copy Revision Number`、`Create Patch…`、`Cherry-Pick`、`Checkout Revision`、`Show Repository at Revision`、`Compare with Local`、`Reset Current Branch to Here…`、`Revert Commit`、`Undo Commit…`、`Edit Commit Message…`、`Fixup…`、`Squash Into…`、`Drop Commit`、`Interactively Rebase from Here…`、`Push All up to Here…`、`New Branch…`、`New Tag…`、`Go to Child Commit`。
- 页面级同态配对 **15 → 16**；像素合格仍 **0**。

## 2. 入口修正

- 先前 `(1000,1255)` 右键/Shift+F10 未命中提交行；CDP 量得提交行 CSS `[364,723,884,26]`、物理中心 **`(1411,1288)`**，在该点右键后菜单正常打开。
- 实际正常入口 PID 4428：Git Log 提交行右键 → 菜单；Esc 关闭，未执行动作。
- 参考菜单来自前批提交右键（`reset-pair/reference/reference-commit-menu.png`）。

## 3. 已知差异

- 实际只保留产品范围内的动作；参考的 Create Patch、Checkout Revision、Show Repository、Undo/Edit/Fixup/Squash/Drop/Interactively Rebase/Push All/Go to Child 未出现，属复杂 Git 能力范围差异。
- `Copy Commit Hash` vs `Copy Revision Number`；`Compare with Workspace` vs `Compare with Local`；`Reset Current Branch Here...` vs `...to Here…`。
- 菜单宽高/分组/快捷键/禁用态未逐项核对；Reset Here 未点击（危险动作不擅自执行）。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/git-log-menu-pair/actual/actual-commit-menu.png`（`7DF62D9E…7C82`）。
- 参考：`reset-pair/reference/reference-commit-menu.png`（`89CF42EE…7ED7`）。
- 误命中筛选/无菜单：`actual-git-log-menu.png`、`actual-commit-menu-key.png`（诊断）。
- 第 66 批补充：点击 `Compare with Workspace`（1490,1337）两次，菜单保持打开、未出现比较视图；未执行 Git 写。该菜单项接线/命中未定位，**不作产品结论**，也不计入配对；证据 `compare-pair/actual/actual-compare.png`（`533732CC…0EB6`），`cleanup-check-93.json`。

## 5. 清理

- 实际 PID 4428 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；未执行 Git 写；无残留；`cleanup-check-91.json`；未提交、未推送。
