# Push（有待推提交）同态配对（2026-10-18）

## 1. 结论

在带本地 bare remote 的临时 clone 上构造 1 个待推提交，完成 Push 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Push 对话框、`main` 与 `origin/main`、1 个待推提交 `test: pending push`、`Push tags: All`、`Push` **可用**、`Cancel`；均**未执行 Push**；浅色、DPI168。
- 实际：标题 `Push`；`main → origin/main`；左侧提交行（✓）；右侧 `1 commits`、`Target: origin/main`、credentials 说明；约 1627×865。
- 参考：标题 `Push Commits to push-commit-fixture`；`main → origin : main`；左侧提交行；右侧工具栏 + `1 file` 文件夹 + `notes.txt`；约 1401×949。
- 页面级同态配对 **29 → 30**；像素合格仍 **0**。

## 2. 取证路径

- 临时 bare `.tmp/push-commit-remote.git` + clone `.tmp/push-commit-fixture`；clone 中修改 `notes.txt` 并提交 `test: pending push`（仅临时仓库）。
- 实际 PID 12372：该 clone → Git Push；打开对话框，Cancel/关闭未推。
- 参考 PID 20152：同一 clone；临时预信任路径后启动，Ctrl+Shift+A `Push`；未推。
- 取证后 `trusted-paths.xml` 恢复（`6E40F668…D2C9`）；临时 clone/bare 删除；未写用户 fixture。

## 3. 已知差异

- 标题、`→ origin/main` vs `→ origin : main`、右侧提交/文件树 vs 计数+Target+credentials、工具栏、帮助、尺寸。
- 未测：Push 成功/失败、推送后分支状态、tags、Force Push、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/push-commit-pair/actual/actual-push.png`（`873D9823…F5D`）。
- 参考：`reference/reference-push.png`（`BCFDBE2D…6553`）。

## 5. 清理

- 实际 PID 12372、参考 PID 20152 已结束；临时 clone/bare 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-112.json`；未提交、未推送。
