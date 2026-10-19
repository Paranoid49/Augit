# New Worktree 对话框同态配对（2026-10-18）

## 1. 结论

完成 New Worktree 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：`New Worktree` 标题、分支字段、`New Branch` 复选、目录/位置、`Cancel` 与主操作、关闭入口；均**未创建 Worktree**；浅色、DPI168。
- 实际：`Branch` 文本框 `main`；`New Branch` 未选 + 禁用名称输入；`Directory` 输入预填 `D:\projects\repc`（被截断）；布局异常——`Directory` 标签在复选行右侧、目录输入在禁用的新分支输入下方，字段未对齐/截断；`Create` 蓝色；窗口约 1627×639。
- 参考：`From branch:` 下拉 `Select branch`；`New branch:` 复选 + 输入；`Project name:`；`Location:` + 选择目录；提示实际创建目录；`Create & Open Worktree`/`Cancel`；窗口约 855×541。
- 页面级同态配对 **18 → 19**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 16224：分支 chip → 分支弹层 → `New Worktree…`；Esc 关闭，未创建。
- 参考 PID 25280：Find Action `New Worktree`；Esc 关闭，未创建。

## 3. 已知差异

- 实际 `Branch` 文本框 vs 参考 `From branch` 下拉；实际无 `Project name`；`Directory` vs `Location`；参考有目标目录提示。
- **实际字段布局重叠/截断已在第 69 批修复**：`.worktree-new-branch-row` 跨两列、flex 同行；正常入口 PID 14008 复验无重叠/截断，见[布局修复](new-worktree-layout-fix-2026-10-18.md)。其余字段差异仍在。
- 主按钮 `Create` vs `Create & Open Worktree`；标题图标；窗口尺寸/行距。
- 未测：新分支创建、路径已存在/非空、非法名称、成功/失败、深色。

## 4. 证据

- 实际（修复前）：`artifacts/pixel-followup-2026-10-18/worktree-pair/actual/actual-worktree.png`（`2997000A…2D1C`）；修复后：`worktree-fix/actual-worktree-fixed.png`（`EC073254…FBD9`）。
- 参考：`reference/reference-worktree.png`（`4F9D71E1…AB10`）。

## 5. 清理

- 实际 PID 16224、参考 PID 25280 已结束；`settings.json` 恢复 `B8868855…DC3D`；未创建 Worktree、无 Git 写；无残留；`cleanup-check-95.json`；未提交、未推送。
