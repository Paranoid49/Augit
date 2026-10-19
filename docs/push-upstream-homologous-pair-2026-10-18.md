# Push（有 upstream、无待推提交）同态配对（2026-10-18）

## 1. 结论

在带本地 bare remote 的临时 clone 上完成 Push 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Push 对话框、`main` 与 `origin/main` 的上下标关系、无待推提交、`Push tags: All`、`Cancel`；均**未执行 Push**；浅色、DPI168。
- 实际：标题 `Push`；左 `main → origin/main`、`No commits to push`；右 `0 commits`、`Target: origin/main`、`Credentials are handled by local Git; Augit does not store them.`；`Push` **禁用**；约 1627×865。
- 参考：标题 `Push Commits to ops-fixture`；左 `main → origin : main`；右工具栏 + `No commits selected`；`Push` **可用**；约 1401×949。
- 页面级同态配对 **28 → 29**；像素合格仍 **0**。

## 2. 取证路径

- 临时 bare remote `.tmp/ops-remote.git` + 临时 clone `.tmp/ops-fixture`（origin 指向 bare，main 已 push 到 origin）。
- 实际 PID 18076：该 clone → Git 菜单 Push；打开 Push 对话框，Cancel/关闭未推。
- 参考 PID 22228：同一 clone；为绕开 Trust，临时在 `trusted-paths.xml` 预信任该路径，重启后 Ctrl+Shift+A `Push`；未推。
- 取证后 `trusted-paths.xml` 恢复（`6E40F668…D2C9`）；临时 clone/bare 删除。

## 3. 已知差异

- 标题、`→ origin/main` vs `→ origin : main`、右栏空态/工具栏、Push 可用性、参考帮助/图标、窗口尺寸。
- 未测：有待推提交、push 成功/失败、tags、Force Push、credentials、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/push-upstream-pair/actual/actual-push.png`（`5BADD628…955C`）。
- 参考：`reference/reference-push.png`（`A138FDE1…AE8D`）。

## 5. 清理

- 实际 PID 18076、参考 PID 22228 已结束；临时 clone/bare 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-111.json`；未提交、未推送。
