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

## 2. 后续

- 参考端先点 Changes 箭头确认 `big.txt` 行可见，再双击该行（或右键 → Show Diff）；目标页应是参考的大 Diff 限制/局部加载提示。
- 实际页已覆盖 `SideTooLarge`；还需覆盖 `OutputTooLarge`、二进制/图片 Diff、部分加载与重试。

## 3. 清理

- 实际 PID 6984、参考 PID 5820 已结束；临时 clone 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；`settings.json` 恢复 `B8868855…DC3D`；fixture 未变。
- 本批未修改代码；`cleanup-check-154.json`；未提交、未推送。
