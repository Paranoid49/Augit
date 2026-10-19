# Push 被拒（真正分叉）同态配对（2026-10-18）

## 1. 结论

构造真正的远端/本地分叉并执行 Push，取得实际与参考的拒绝反馈，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 `pf3-local`；远端 main 已有 `3f2b316 test: other divergent 3`，本地为 `47507e5 test: local divergent 3`（非后代，真正 non-fast-forward）；Push 被拒、未推送；两端都保留错误反馈并允许取消。
- 实际：Push 对话框保持打开；右侧显示原始 Git 拒绝文本（`! [rejected] main -> main (fetch first)` + hint）；`1 commits`、Target origin/main、credentials；底部 Cancel/Push 重试；约 1627×865。
- 参考：独立 `Push Rejected` 模态；人类可读摘要；`Remember the update method...` 复选框；`Merge`/`Rebase`/`Cancel`；约 770×357。
- 页面级同态配对 **30 → 31**；像素合格仍 **0**。

## 2. 取证路径

- 临时 bare `pf3-remote.git`（initial branch main）→ `pf3-seed` 推入初始 main → `pf3-local` 与 `pf3-other` 各自从初始克隆。
- `pf3-local` 提交 local divergent；`pf3-other` 提交 other divergent 并推入 bare；本地 `main...origin/main [ahead 1]` 但本地/远端已分叉。
- 实际 PID 17336：Push → UIA Invoke Push；对话框保持，错误文本显示。
- 参考 PID 24224：预信任 `pf3-local` 后启动；Push → 点击 Push；弹出 `Push Rejected`。
- 取证后 `trusted-paths.xml` 恢复 `6E40F668…D2C9`；临时仓库删除。

## 3. 已知差异与边界

- 实际是“对话框内原始 Git 输出 + 重试”；参考是“摘要模态 + Merge/Rebase/Cancel”。
- 产品保持轻量、不扩展 Merge/Rebase 等复杂 Git；参考的 Merge/Rebase 不在保留范围，不照搬。
- 实际直接展示 Git 原始 hint 是否符合产品文案规范，需按 UX 规范另行判断；本次不修改。
- 未测：Force Push、Push 失败后的重试成功、取消/记住选项、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/push-fail2-pair/actual/actual-push-fail-true.png`（`9CF66FEE…8097`）。
- 参考：`reference/reference-push-fail3.png`（`06572D54…FD7D`）。

## 5. 清理

- 实际 PID 17336、参考 PID 24224 已结束；临时 clone/bare 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-117.json`；未提交、未推送。
