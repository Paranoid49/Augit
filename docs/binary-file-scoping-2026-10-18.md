# 不可预览文件取证尝试（2026-10-18）

## 1. 结论

取得实际端不可预览二进制页，但参考端对 `.bin` 弹出文件类型关联对话框，**未建立配对**，页面级仍 44。

- 实际 PID 21956：在 fixture 临时加入 14B 二进制 `sample.bin`，Project 树双击 → `This file cannot be previewed in Augit`、`sample.bin · Binary file · 14 B`、`Only a binary summary is available. Open the file in its default application.`、`Open in Default Application`。证据 `5E326AF1…CDC17`。
- 参考 PID 11176：同一 `sample.bin` 双击 → `Register New File Type Association`（`*.bin`，Open as text / Open in PyCharm Text-TOML-TypeScript… / Open in associated application）；未出现二进制摘要页。证据 `C23E6A9B…319E`。
- 这是**文件类型关联差异**，不是同一页面；不据此判缺陷。

## 1.1 `.zip` 重试

- 实际 PID 23148：fixture 临时 `sample.zip`（137B，有效 zip）双击 → 同样的 `This file cannot be previewed in Augit` / `sample.zip · Binary file · 137 B` / Open in Default Application。证据 `6BA34270…1880`。
- 参考 PID 24084：双击 `sample.zip` 只选中 Project 行（底栏显示 sample.zip），编辑器仍是 app.py；没有打开二进制页/标签。证据 `D549E161…2F59`。
- 参考对 `.bin` 弹文件类型关联、对已知 `.zip` 不打开预览；实际都有明确二进制摘要页。**仍无同态页可配对**。

## 1.2 Commit Diff 重试（2026-10-19）

- 实际 PID 6084：Commit 视图双击 `sample.bin` → 打开 `Commit: sample.bin`，正文 `Text diff is unavailable for this file (Binary).`；不是文件预览页，而是二进制 Diff 摘要。
- 参考 PID 21560：同一 `.bin` 的 Commit Diff → `Cannot show diff for an unknown file type` + 两侧 `Associate`；`.zip` → ZIP 归档内容比较页（临时解包路径 + `Nothing to show`）。
- 仍无“二进制摘要”同态参考页；登记为产品差异，不增加页面级 50。详见[二进制 Diff 参考复核](binary-diff-reference-2026-10-19.md)。

## 2. 后续

- 参考端先为 `.bin` 选择 PyCharm 内打开（或改用已知二进制类型如 `.zip`）后再采“Binary file”页；实际不可预览页证据保留。
- 也可对比参考 `Register New File Type Association` 与实际直接二进制摘要的产品差异，登记为已知差异而非配对。

## 3. 清理

- 实际 PID 21956、参考 PID 11176 已结束；临时 `sample.bin` 删除；fixture 未变（`M app.py` / `?? .idea` 为既有状态）；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-135.json`；未提交、未推送。
