# 冲突列表同态配对（2026-10-18）

## 1. 结论

在隔离 clone 构造 `notes.txt` 合并冲突，完成两端冲突入口取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 `conflict-fixture`（`main` 合并 `feature` 冲突）；1 个冲突文件 `notes.txt`；未解决、未提交；两端均提供继续/中止入口；均**未写用户仓库**。
- 实际：居中 `Merge Conflicts` 模态；`1 conflicting file`、`notes.txt`；底部 `Abort Merge`、`Continue Merge`（禁用）、`Remaining: 1 unresolved conflict`；Project `notes.txt` 带 `!`；底部 Git Log。
- 参考：主窗标题栏 `Merging main` + 绿色继续/红色中止图标；Project `notes.txt` 红色；无独立模态；README 编辑器。
- 页面级同态配对 **31 → 32**；像素合格仍 **0**。

## 2. 取证路径

- 临时 clone `.tmp/conflict-fixture`：feature 分支改 `notes.txt`，main 另改同一行，`git merge feature` 产生 `UU notes.txt`。
- 实际 PID 22028：正常入口启动即弹出 `Merge Conflicts`；关闭未解决。
- 参考 PID 10716：临时预信任该路径后启动；显示 `Merging main` 标题栏；关闭未解决。
- 取证后 `trusted-paths.xml` 恢复 `6E40F668…D2C9`；临时 clone 删除。

## 3. 已知差异与边界

- 实际为独立冲突列表模态；参考为标题栏合并态 + 项目树红色标记，无独立模态。
- 实际 `Continue Merge` 在 1 个未解决冲突时禁用；参考继续图标可用性未逐一验证。
- 未测：解决冲突后 Continue、Abort Merge 的结果、多冲突文件、深色；三栏编辑器已批准移除，不恢复。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/conflict-list-pair/actual/actual-conflict.png`（`AA8CCF1B…98EC`）。
- 参考：`reference/reference-conflict2.png`（`8DC91453…A668`）。

## 5. 清理

- 实际 PID 22028、参考 PID 10716 已结束；临时 clone 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-118.json`；未提交、未推送。
