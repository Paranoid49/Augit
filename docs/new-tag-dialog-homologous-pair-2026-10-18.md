# New Tag 对话框同态配对（2026-10-18）

## 1. 结论

完成提交 `e9188bf` 上的 New Tag 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：`Create New Tag On <hash>` 标题、`Enter the name of new tag` 字段、空输入、取消/主操作、关闭入口；同一提交；均**未创建标签**；浅色、DPI168。
- 实际：标题包含**完整**长哈希并换行；无标题图标/问号；`Create` 主按钮在空输入时仍为蓝色可用；窗口约 700×287。
- 参考：标题哈希**截断为 …**；PyCharm 标题图标 + 问号；`OK` 在空输入时**禁用**；`Cancel`；窗口约 706×270。
- 页面级同态配对 **16 → 17**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 23196：Git Log 提交行右键 → `New Tag...`；Esc 关闭，未 Create。
- 参考 PID 4488：Git Log 提交行右键 → `New Tag…`；Esc 关闭，未 OK。
- 提交行入口物理 `(1411,1288)`（实际）；参考菜单项点击后打开 AWT 输入框。

## 3. 已知差异

- 标题哈希完整换行 vs 截断省略；标题图标/问号；按钮 `Create` vs `OK`、左右顺序。空输入主按钮**已修复**：实际 `Create` 空名称禁用、填名启用，对齐参考 `OK` 禁用（第 70 批，`showCompactInput` 同步禁用态；正常入口 PID 3308 复验）。
- 窗口尺寸/输入框宽度/行距。
- 未测：空名 Create 的提示、非法名、同名标签、force、成功/失败、深色。

## 4. 证据

- 实际（修复前）：`artifacts/pixel-followup-2026-10-18/new-tag-pair/actual/actual-new-tag.png`（`7D7DF665…777C`）；修复后空态：`new-tag-fix/actual-new-tag-empty.png`（`108575AA…185A`）。
- 参考：`reference/reference-new-tag.png`（`2E0C489A…8BE8`）。

## 5. 清理

- 实际 PID 23196、参考 PID 4488 已结束；`settings.json` 恢复 `B8868855…DC3D`；未创建标签、fixture 无 Git 写；无残留；`cleanup-check-92.json`；未提交、未推送。
