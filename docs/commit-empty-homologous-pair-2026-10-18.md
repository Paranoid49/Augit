# Commit 空态同态配对（2026-10-18）

## 1. 结论

在干净 clone 上完成 Commit 空态两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一临时 clone（HEAD `e9188bf`、工作树无已跟踪修改）、Commit 工具窗/消息框/Amend、无提交动作、浅色、DPI168、两侧窗口 2880×1716；均**未写 Git**。
- 实际：`No changes to commit` / `The working tree matches HEAD.`；Commit、Commit and Push **禁用**；底部 Git Log 打开，显示 3 个文件/提交详情。
- 参考：`Changes` 分组为空；`Unversioned Files 5 files`；Commit/Commit and Push **仍可用**；README.md 编辑器；无底部 Git Log；右下 “IDE project settings can be added to Git” 通知。
- 页面级同态配对 **26 → 27**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 2100（Round 81）：临时 clone `.tmp/commit-empty-fixture` → Commit。
- 参考 PID 22704（Round 86）：同一临时 clone；为绕开 Trust 后前台限制，临时在参考 `trusted-paths.xml` 增加该 clone 路径，重启后**无 Trust、主窗口正常前台**，Ctrl+K → Commit。
- 取证后 `trusted-paths.xml` 已恢复原文件（SHA256 `6E40F668…D2C9`）；临时 clone 已删除。

## 3. 已知差异

- 空态呈现：实际“No changes to commit”+禁用提交；参考空 Changes 组+Unversioned Files 5+提交仍可用。
- 实际底部 Git Log 打开；参考未打开；参考有 IDE settings 通知；编辑器 README vs 实际空正文。
- 未创建 `.idea` 的实际 vs 参考自动生成 5 个未跟踪文件；未测真实空仓库/无未跟踪文件的变体。
- 未测：暂存/未暂存混合、空消息校验、深色、不同 DPI。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/commit-empty-pair/actual/actual-commit-empty.png`（`B1207639…1895`）。
- 参考：`reference/reference-commit-empty-max.png`（`0A0AD995…F26E`）。

## 5. 清理

- 实际 PID 2100（Round 81）、参考 PID 22704 已关闭；临时 clone 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-108.json`；未提交、未推送。
