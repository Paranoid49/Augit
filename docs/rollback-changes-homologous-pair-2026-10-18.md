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

## 6. 2026-11-25 整改

用户以实机参考要求将 Rollback 改为 IDEA `RollbackChangesDialog` 结构：`Rollback Changes` 标题、按目录分组的勾选文件树、根/目录半选、状态 commit legend、`Delete local copies of added files` 与 `Rollback`/`Close`；同时支持多文件一次性回滚。旧版单文件 `Rollback File` 红色警告 + 比较视图作为历史记录保留在第 1 节，不再代表当前实现。尺寸、截图与交互验证见本轮提交记录。

## 7. 2026-11-26 用户实机复核后的补强

用户反馈三点：改动工具窗两个动作灰、Rollback 窗口外观与真实 IDEA 有差距且“无选中文件时按钮可点”、关闭模态后主界面掉到其它应用后面或闪烁。核对 `intellij-community`：

- `RollbackAction.update` 的可用性来自 `hasReversibleFiles`（工作区存在改动即可），动作目标在非模态提交模式下取 included（勾选）文件；`RollbackFilesAction` 仅在勾选优先模式下要求 `VcsDataKeys.CHANGES`。Augit 原先要求行选中，错误地禁用了两个动作。
- `RollbackChangesDialog.rollbackChanges(project, changes)` 只勾选传入的改动；无传入时不预勾选，`inclusionListener` 在 included 为空时禁用 OK。Augit 原先在无路径时默认勾选全部文件。
- `ChangesBrowserBase` 的 Show Diff 只在存在选中改动时可显示；Rollback 窗口工具条在无选中文件时应禁用 Diff/预览。
- 原生模态关闭顺序参考设置独立窗口：先 `EnableWindow(owner,true)`、`SetForegroundWindow(owner)`，再隐藏/销毁子窗；否则隐藏活动窗时 Windows 会把前台交给其它应用。

本批据此补齐：Rollback 按钮按 included 文件回退、对话框空勾选/footer 禁用、Diff/预览禁用、底栏 30px、根节点显示工作区名；关闭原生模态先激活所有者再隐藏。详细验证与截图见本轮提交记录。
