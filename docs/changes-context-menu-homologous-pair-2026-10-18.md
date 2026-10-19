# Changes 右键菜单同态配对（2026-10-18）

## 1. 结论

用 Round 79 已采集的两端 `app.py` 改动行右键菜单，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 `app.py` 改动行、`Rollback…`、`Show Diff`、复制路径、文件历史/Blame 入口、浅色、DPI168；均**未执行动作**。
- 实际：`Rollback…`、`Show Diff`、`Blame`、`File History`、`Copy Path`、`Show in Explorer`。
- 参考：`Commit File…`、`Rollback…`、`Move to Another Changelist…`、`Show Diff`、`Show Diff in a New Tab`、`Jump to Source`、`Copy Path/Reference…`、`Delete…`、`Add to VCS`、`New Changelist…`、`Edit Changelist…`、`Create Patch from Local Changes…`、`Copy as Patch to Clipboard`、`Shelve Changes…`、`Refresh`、`Local History`、`Git`。
- 页面级同态配对 **24 → 25**；像素合格仍 **0**。

## 2. 入口与安全

- 实际正常入口 PID 22784：Commit 工具窗 Changes → `app.py` 右键；菜单截图后 Esc/关窗，未执行动作。
- 参考 PID 10836：Ctrl+K → Commit → `app.py` 右键；菜单截图后 Esc/关窗，未执行动作。
- Round 79 的 ``Rollback…`` 随后分别打开两端 Rollback 对话框（第 24 配对），未回滚。

## 3. 已知差异

- 实际为轻量动作子集；参考包含 changelist、patch、shelve、VCS、Git 子菜单、Local History 等完整动作。
- 顺序/分组/快捷键/图标/分隔线不同；实际无 `Show Diff in a New Tab`、`Jump to Source`、`Copy Path/Reference` 子菜单。
- 未测：各项动作执行、多选、禁用原因、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/rollback-dialog-pair/actual/actual-changes-menu.png`（`9C09DEC6…5E56`）。
- 参考：`reference/reference-changes-menu.png`（`D955325D…80DE`）。

## 5. 清理

- 复用 Round 79 已关闭的 PID 22784 / 10836；无新进程；`settings.json` 恢复 `B8868855…DC3D`；未写 Git；`cleanup-check-104.json`；未提交、未推送。
