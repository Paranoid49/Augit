# Quick Open 空态同态配对（2026-10-18）

## 1. 结论

完成快速打开空态两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：空白快速导航输入；无查询、无结果；未打开文件。
- 实际：主窗内居中 `Open File` 浮层；空输入；无结果列表。
- 参考：`Search Everywhere` 浮层；`All/Classes/Files/Symbols/Actions/Text` 标签；`Project Files`；空输入；无结果；带 `Ctrl+Alt+Shift+E` 提示。
- 页面级同态配对 **40 → 41**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 24036：正常入口 → **先在编辑区点击**（WebView 获得键盘焦点）→ Ctrl+P；打开 `Open File` 空浮层。
- 参考 PID 23172：主窗点击编辑区 → Ctrl+Shift+N；打开 `Search Everywhere` 空浮层（本环境该快捷键落到 Search Everywhere，而非单一 Go to File）。
- 两端均未输入查询、未打开文件。

## 3. 关键教训

- 先前第 109 批 Ctrl+P 未打开，是**WebView 未获得键盘焦点**（只激活了宿主窗口），不是产品快捷键缺失；点击编辑区后 Ctrl+P 正常打开。该批“根因未定”的结论据此更正。
- 实际 `Open File` 与参考 `Search Everywhere` 不是同一范围（参考多 All/Classes/Symbols/Actions/Text）；作为快速导航空态配对，差异登记。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/quick-open-empty-pair/actual/actual-quick-open2.png`（`347D6012…6453`）。
- 参考：`reference/reference-go-to-file.png`（`8D3ADE1D…A4C0`）。

## 5. 清理

- 实际 PID 24036、参考 PID 23172 已结束；`settings.json` 恢复 `B8868855…DC3D`；未打开文件；`cleanup-check-131.json`；未提交、未推送。
