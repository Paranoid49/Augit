# 项目根节点右键菜单同态配对与修复（2026-10-18）

## 1. 修复

- 根因：`buildVisibleTree()` 用**空路径**表示工作区根节点；`openTreeContextMenu()` 读到空 `data-tree-path` 后直接返回，根节点右键毫无反应（文件行正常）。
- 修复：新增 `treeContextPath(row, live)`——有 `data-tree-path` 用自身路径；`.root-row` 且路径为空时回落到 `live.root`；其他空路径仍返回空，不凭空生成菜单。`openTreeContextMenu()` 改用该函数。
- 测试：`tools/test-project-root-context.mjs` **6/6 PASS**（文件节点、根节点回落、无 root、非根空路径、空行、调用点）。
- 正常入口 PID 12592 验证：项目根节点右键出现 6 项菜单；`live-data.js` `B7A1F7DF…AD7F`。

## 2. 配对

- 相同：同一 fixture、同一项目根节点右键；均出现上下文菜单；均未执行动作、未写文件。
- 实际：6 项 `Copy Path`、`Show in Explorer`、`Open in External Terminal`、`Refresh`、`File History`、`Blame`。
- 参考：完整项目根菜单（New/Cut/Copy/Copy Path/Reference/Paste/Find Usages/Find in Files/Replace in Files/Inspect/Rename/Refactor/Clean Python Compiled Files/Bookmarks/Reformat/Optimize/Open In/Local History/Git/Repair IDE/Reload/Compare/Mark Directory As/Diagrams 等）。
- 页面级同态配对 **33 → 34**；像素合格仍 **0**。

## 3. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/project-root-context-pair/actual/actual-root-menu.png`（`4DDCDB73…66F5`）。
- 参考：`reference/reference-root-menu.png`（`054D80E0…38CA`）。
- 测试：`tools/test-project-root-context.mjs` 6/6。

## 4. 清理

- 实际 PID 12592、参考 PID 14704 已结束；`settings.json` 恢复 `B8868855…DC3D`；fixture 未变；`cleanup-check-120.json`；未提交、未推送。
