# Reset Current Branch 对话框同态配对（2026-10-18）

## 1. 结论

完成 Reset 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Reset 对话框、目标提交 `e9188bf`、默认 **Mixed** 模式、说明区、Reset/Cancel；同一 `main`/提交；均**未执行 Reset**；浅色、DPI168。
- 实际：标题 `Reset Current Branch`；`Target commit` 可编辑文本框；`Mode` 下拉 `Mixed · Reset index`；说明面板“Move HEAD and reset the index; keep working tree changes / Staged changes return to the working tree; local files are kept.”；窗口约 1085×504。
- 参考：标题 `Git Reset`；头部 `main -> e9188bf3 "test: create pixel fixture" by PixelAudit`；四档单选 **Soft / Mixed（选中）/ Hard / Keep** 及各自说明；帮助图标；窗口约 827×720。
- 页面级同态配对 **13 → 14**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 24956：Git 菜单 → `Reset Current Branch…`；打开原生对话框，Esc 关闭，未 Reset。
- 参考 PID 7600：Git Log 提交右键 → `Reset Current Branch to Here…`；打开 `Git Reset`，Esc 关闭，未 Reset。
- 先前 Find Action `Reset Current Branch` 在参考端未打开对话框（缺少选中目标），不计为产品失败。

## 3. 已知差异

- 实际目标提交可编辑 + 模式下拉 + 只显示当前模式说明；参考显示四档单选及全部说明、头部提交摘要。
- 标题 `Reset Current Branch` vs `Git Reset`；帮助/图标；窗口尺寸/行距。
- 未测：非法提交、Soft/Hard/Keep、未提交改动保护、Reset 成功/失败、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/reset-pair/actual/actual-reset-dialog.png`（`02278CE2…0E0E`）。
- 参考：`reference/reference-reset-dialog.png`（`B0D497FE…72F5`）。
- 参考提交右键菜单：`reference/reference-commit-menu.png`（诊断）。

## 5. 清理

- 实际 PID 24956、参考 PID 7600 已结束；`settings.json` 恢复 `B8868855…DC3D`；**未执行 Reset**，fixture 无 Git 状态变化；无残留；`cleanup-check-86.json`；未提交、未推送。
