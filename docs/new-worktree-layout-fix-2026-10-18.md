# New Worktree 字段布局修复（2026-10-18）

## 1. 结论

第 68 批实测的 New Worktree 字段重叠/截断已修复：

- 根因：`openWorktreeDialog()` 把 `New Branch` 复选 + 名称输入包在 `span.worktree-new-branch-row`，但该 span 只是 `form-grid` 第 1 列的**一个** grid item；紧随的 `Directory` 标签占同一行第 2 列，目录输入落到下一行第 1 列，形成标签在右、输入在下的错位，并因列宽截断。
- 修复：`.worktree-dialog .form-grid > .worktree-new-branch-row { grid-column: 1 / -1; display:flex; align-items:center; gap:10px; overflow:visible; white-space:normal; }`；`check-line` 不占额外行、`text-field` 可伸缩。
- 修复后：Branch / New Branch（复选 + 名称输入）/ Directory 三行顺序对齐，`New branch name` 与 `D:\projects\repository-worktree` 占位完整可见，无重叠/截断。

## 2. 回归

- `tools/test-worktree-check-row.mjs` **6/6 PASS**（两个共享 CSS 字节一致、标记存在、跨列、flex 同行、输入伸缩、复选不占行）。
- `tools/audit/verify-ui-assets.ps1` PASS。
- `verify-ux-offline-icons.cjs` PASS 110 场景/主题（修后证据 `offline-icons-after-worktree-fix/`）。
- **正常入口 PID 14008**：分支 chip → New Worktree，修复后实采；无 Worktree 创建。
- 修复后 `mockup.css` `5BAA123246D78137743C6BF92A3742FAB9DB2AD66DCF1A39EB76B8E42A678967`（两端一致）。

## 3. 仍存差异（不影响本次布局修复）

- `Branch` 文本框 vs 参考 `From branch` 下拉；无 `Project name`；`Directory` vs `Location`；主按钮 `Create` vs `Create & Open Worktree`；标题图标/尺寸。
- 未测：路径校验、新分支创建、成功/失败、深色。

## 4. 证据与清理

- 修复后：`artifacts/pixel-followup-2026-10-18/worktree-fix/actual-worktree-fixed.png`（`EC073254…FBD9`）。
- 修复前：`worktree-pair/actual/actual-worktree.png`（`2997000A…2D1C`）；参考：`worktree-pair/reference/reference-worktree.png`。
- PID 14008 正常关闭；设置恢复 `B8868855…DC3D`；无 Worktree/Git 写；`cleanup-check-96.json`；未提交、未推送。
