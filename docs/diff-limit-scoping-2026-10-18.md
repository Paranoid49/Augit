# Diff 上限取证尝试（2026-10-18）

## 1. 结论

用 12,728,893 字节（超过 10 MB 侧上限）的未暂存修改 `big.txt` 取得实际端大 Diff 限制页；参考端未采到对应页，**未建立配对**，页面级仍 48。

- 隔离 clone：`.tmp/diff-limit-fixture`；`big.txt` 120,000 行、12,728,893 字节，提交后追加一行修改。
- 实际 PID 6984：Commit 视图双击 `big.txt` → `Commit: big.txt` 标签；正文 **`Text diff is unavailable for this file (SideTooLarge).`**，并说明“File content, changes, other tabs, and the current selection are unchanged.”与“Close this comparison or open the file in its default application.”；未生成 diff 正文。证据 `687AE843…79B5`。
- 参考 PID 5820：Commit 工具窗打开并显示 `big.txt`，但展开/双击/Enter 的组合只切换了节点/选中，未打开 diff 页；证据 `6D230EF8…79B6` 只是 Commit 窗口。未建立配对。

## 1.1 参考端第二次尝试（第 134 批）

- 参考 PID 4120：Commit 工具窗 → 点 `Changes` 节点 → Right（展开）→ Down → Enter，`big.txt` 可见但未打开 diff；随后双击 `big.txt` 行（物理 `480,290`）反而折叠 `Changes` 节点。
- 说明参考 Commit 树的节点/文件行命中区域与双击语义需要重新量取；连续两次都未打开 diff 页，不能用 Commit 窗口截图冒充 diff 上限页。
- 后续可改用行右键 → Show Diff、或编辑器/Git 工具窗的 diff 入口，避开树节点双击。

## 1.2 参考端第三次尝试（2026-10-19，成功）

- 使用 25,380,029 字节、270,000 行的 `big.txt`，同一提交 `70a24f0`；参考 2026.2.1 PID 12384 在 Commit 工具窗**单击 `big.txt` 行后按 `Ctrl+D`**，成功打开 `Commit: big.txt`。
- 参考正文显示 `Cannot show diff: Cannot show the contents of '…big.txt'. The file is bigger than 20.97 MB.` + `Reload`，没有 diff 正文；实际同一文件显示 `Text diff is unavailable for this file (SideTooLarge).`。两端都拒绝显示 diff，登记为**状态级配对**，不增加页面级 48。
- 先前双击文件行会折叠/切换节点，未打开 Diff；本轮确认正确路径是“选中行 + Ctrl+D”，不是产品缺少该状态。
- 详见[Diff 上限参考态](diff-limit-reference-2026-10-19.md)。

## 2. 后续

- Diff 上限状态已补齐；仍需 `Diff 局部加载`（慢读/局部骨架/重试）、`OutputTooLarge`、二进制/图片 Diff。
- 实际页已覆盖 `SideTooLarge`；参考的 20.97 MB 阈值与实际的 `SideTooLarge` 文案不同，不能反推两者阈值相等。

## 3. 清理

- 实际 PID 6984、参考 PID 5820 已结束；临时 clone 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；`settings.json` 恢复 `B8868855…DC3D`；fixture 未变。
- 本批未修改代码；`cleanup-check-154.json`；未提交、未推送。
