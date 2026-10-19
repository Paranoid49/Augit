# 二进制 Diff 参考复核（2026-10-19）

## 1. 结论

二进制 Diff 仍**没有可配对的参考页**，因此不增加页面级 50。

- 实际：`sample.bin` 在 Commit 视图双击后显示 `Text diff is unavailable for this file (Binary).`，正文说明“File content, changes, other tabs, and the current selection are unchanged.”，提供关闭比较/默认应用入口。
- 参考 2026.2.1：同一 `.bin` 的 `Commit: sample.bin` 显示 `Cannot show diff for an unknown file type`，两侧各有一个 `Associate` 链接；不是二进制摘要页。
- 参考 `.zip`：显示 ZIP 内容比较页（临时解包路径 + 两侧 `Nothing to show`），也不是二进制摘要页。
- 实际二进制摘要页是产品专有实现；参考对未知二进制/归档采用不同入口。登记为**产品差异**，不是同态配对。

## 2. 证据

- 实际：`artifacts/pixel-followup-2026-10-19/binary-diff-closure/actual/actual-binary-diff.png`（`DEDE63A7…542A`）。
- 参考 `.bin`：`reference/reference-binary-diff.png`（`FEB1F09F…140B`）。
- 参考 `.zip`：`reference/reference-zip-diff.png`（`63BD2812…EFB4`）。
- 实际 PID 6084、参考 PID 21560；均正常关闭。

## 3. 边界

- `.bin` 在参考中没有文件类型关联，不能据此证明参考对“二进制文件”没有摘要；但本轮不把“未知类型”改写成“二进制同态”。
- 参考 `.zip` 是归档内容比较，不是摘要；实际不提供该 ZIP 内容页，属产品范围差异。
- 未执行 Rollback；参考 `Rollback Changes` 对话框仅打开后关闭，工作区 `sample.bin`/`sample.zip` 仍为 modified。
- 未修改代码、未提交、未推送；页面级仍 **50**、像素仍 **0**。

## 4. 清理

- 实际 PID 6084、参考 PID 21560 关闭；临时夹具删除；参考配置从启动前快照恢复并哈希一致。
- 全局设置仍 `B8868855…DC3D`；`cleanup-check-177.json`。
