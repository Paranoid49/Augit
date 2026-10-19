# Push 对话框同态配对（2026-10-18）

## 1. 结论

完成无 upstream 状态 Push 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Push 对话框、当前分支 `main`、`Define remote` 链接、无远端/上游、`Push tags` + `All`、`Push` 禁用、`Cancel`；均**未执行 Push**；浅色、DPI168。
- 实际：标题 `Push`；左侧 `main Define remote`；右侧 “Unable to load push preview / The current branch has no upstream; push preview is unavailable.”；无提交；窗口约 1627×917。
- 参考：标题 `Push Commits to fixture`；左侧 `main → Define remote`；右侧 “No commits selected” + 交换/眼睛/铅笔/编辑器工具栏；帮助图标；`Push` 带下拉；窗口约 1401×949。
- 页面级同态配对 **14 → 15**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 24284：Git 菜单 → `Push…`；打开 Push 对话框，Esc 关闭，未 Push。
- 参考 PID 4528：Find Action `Push` → `Push Commits to fixture`；Esc 关闭，未 Push。

## 3. 已知差异

- 标题（`Push` vs `Push Commits to fixture`）、`main →` 箭头、右侧空态措辞（无 upstream 错误 vs No commits selected）、右侧工具栏、帮助/图标、Push 下拉、窗口尺寸/行距。
- 未测：有远端/上游、提交列表、推送标签、Force push、Push 成功/失败、Define remote 返回流程（历史已覆盖嵌套生命周期）。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/push-pair/actual/actual-push-dialog.png`（`3ABEE3CE…83E7`）。
- 参考：`reference/reference-push-dialog.png`（`CAF5ABA6…206F`）。

## 5. 清理

- 实际 PID 24284、参考 PID 4528 已结束；`settings.json` 恢复 `B8868855…DC3D`；未执行 Push、fixture 无远端写；无残留；`cleanup-check-88.json`；未提交、未推送。
