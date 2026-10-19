# Rollback 对话框同态配对（2026-10-18）

## 1. 结论

完成 `app.py` 的 Rollback 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：回滚 `app.py`、本地改动将被丢弃的警告、`Rollback` 主操作与取消/关闭、`Modified` 状态；均**未执行 Rollback**；浅色、DPI168。
- 实际：标题 `Rollback File · app.py`；红色警告 “All local changes in this file will be discarded / Rollback affects the entire file…”; File `app.py`、Changes `Modified`；`Cancel` / `Rollback File`；约 1627×753。
- 参考：标题 `Rollback Changes`；文件树 `Changes 1 file` / 文件夹 / `app.py`（已勾选）；`1 modified`；`Delete local copies of added files` 禁用；`Rollback` / `Close`；约 666×986。
- 页面级同态配对 **23 → 24**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 22784：Commit 工具窗 Changes → `app.py` 右键 → `Rollback…`；打开 Rollback File 对话框，Esc 关闭，未回滚。
- 参考 PID 10836：Ctrl+K → Commit → `app.py` 右键 → `Rollback…`；打开 Rollback Changes，Esc 关闭，未回滚。

## 3. 已知差异

- 实际单文件警告式对话框；参考多文件勾选树 + “Delete local copies of added files”。
- 标题、按钮（`Rollback File`/`Cancel` vs `Rollback`/`Close`）、警告布局/图标、尺寸。
- 未测：多文件回滚、新增文件删除副本、冲突/失败、成功结果、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/rollback-dialog-pair/actual/actual-rollback-dialog2.png`（`E1156AD2…5FC2`）。
- 参考：`reference/reference-rollback-dialog.png`（`380E3468…DCDD`）；参考 Changes 右键菜单：`reference-changes-menu.png`（`D955325D…80DE`）。

## 5. 清理

- 实际 PID 22784、参考 PID 10836 已结束；`settings.json` 恢复 `B8868855…DC3D`；未写 Git/fixture 未变；无残留；`cleanup-check-103.json`；未提交、未推送。
