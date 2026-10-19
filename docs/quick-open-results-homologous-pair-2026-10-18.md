# Quick Open 结果同态配对（2026-10-18）

## 1. 结论

完成快速打开查询 `notes` 的结果页两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 fixture、同一查询 `notes`；结果均为 `notes.txt`；均未打开文件。
- 实际：`Open File` 浮层；查询 `notes`；单条结果 `notes.txt`（选中）；右上 `Ctrl+P` 提示；无路径/无标签页。
- 参考：`Search Everywhere` 浮层（Files 标签）；查询 `notes`；结果 `notes.txt`；底部路径 `D:/JetBrains/PyCharm-2026.2.1-reference/fixture/notes.txt`；`Open in Right Split`。
- 页面级同态配对 **41 → 42**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 23744：正常入口 → 点击编辑区获得 WebView 焦点 → Ctrl+P → 输入 `notes`。
- 参考 PID 2424：点击编辑区 → Ctrl+Shift+N（Search Everywhere）→ 输入 `notes`。
- 两端均未选择结果、未打开文件。

## 3. 已知差异与边界

- 实际 Open File 是单列文件结果；参考 Search Everywhere 带 All/Classes/Files/Symbols/Actions/Text 标签、Project Files 范围、路径与 Open in Right Split。
- 未测：选择结果打开、类/符号/动作搜索、无匹配输入、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/quick-open-results-pair/actual/actual-quick-open-results.png`（`FAC33379…5E6A`）。
- 参考：`reference/reference-quick-open-results.png`（`2DBAFAD0…0D21`）。

## 5. 清理

- 实际 PID 23744、参考 PID 2424 已结束；`settings.json` 恢复 `B8868855…DC3D`；未打开文件；`cleanup-check-132.json`；未提交、未推送。
