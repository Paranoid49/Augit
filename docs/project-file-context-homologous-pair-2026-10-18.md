# 项目文件右键菜单同态配对（2026-10-18）

## 1. 结论

在 fixture 的 `app.py` 文件行取得两端右键菜单，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 fixture、同一 `app.py` 文件行右键；均出现上下文菜单；均为只读工具不写文件；菜单关闭后无操作。
- 实际：6 项——`Copy Path`、`Show in Explorer`、`Open in External Terminal`、`Refresh`、`File History`、`Blame`。
- 参考：完整 PyCharm 菜单——New/Cut/Copy/Copy Path/Reference/Paste/Find Usages/Inspect Code/Rename/Refactor/Bookmarks/Reformat Code/Optimize Imports/Delete/Override File Type/Run/Debug/More Run/Debug/Open in Right Split/Open In/Local History/Git/Repair IDE/Reload/Compare With/Mark File As/Diagrams/Convert to Jupyter Notebook 等。
- 页面级同态配对 **32 → 33**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 2592：正常入口 → Project 树 `fixture/app.py` 行右键（物理 `150,271`）。
- 参考 PID 22288：隔离参考打开同一 fixture → `app.py` 行右键（物理 `240,223`）。
- 两端均未选择菜单动作；关闭菜单/窗口。
- `fixture` 已在参考 `trusted-paths.xml` 中；本批未修改信任配置。

## 3. 已知差异与边界

- 实际是轻量 6 项；参考是完整 IDE 菜单，Refactor/Run/Debug/Delete/Diagrams 等不在产品范围，不照搬。
- **根节点右键（已修复）**：第 99 批为根节点空 `data-tree-path` 增加 `treeContextPath()` 回落 `live.root`，并补 `tools/test-project-root-context.mjs` 6/6；正常入口已验证根节点菜单。根节点对参见[项目根节点右键菜单同态配对](project-root-context-homologous-pair-2026-10-18.md)。本文件行配对仍以 app.py 为准。
- 未测：目录节点菜单、多选、菜单动作执行、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/project-context-pair/actual/actual-file-menu.png`（`EE5EF49D…9E75`）。
- 参考：`reference/reference-file-menu.png`（`D6A04E42…F845`）。

## 5. 清理

- 实际 PID 2592、参考 PID 22288 已结束；`settings.json` 恢复 `B8868855…DC3D`；fixture 未变；`cleanup-check-119.json`；未提交、未推送。
