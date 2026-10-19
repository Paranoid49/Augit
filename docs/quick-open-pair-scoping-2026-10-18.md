# 快速打开状态取证（2026-10-18）

## 1. 结论

完成 `notes` 查询的快速打开取证：

- 实际：`Ctrl+P` 打开 **Open File** 紧凑面板，文件结果 `notes.txt`；不显示类/符号/操作/全文分类。
- 参考：`Ctrl+Shift+N` 打开 **Search Everywhere**，带 `All/Classes/Files/Symbols/Actions/Text` 分类、`Project Files` 过滤、结果 `notes.txt`、底部完整路径与 `Open In Right Split`。
- 相同点：同一 fixture、查询 `notes`、结果 `notes.txt`、浅色。
- `ux-spec` 第 106 行明确 Search Everywhere 只保留 `Ctrl+P` 快速打开文件，删除类、符号、操作和非项目项搜索。实际文件-only 面板是**产品有意简化**；参考全分类弹层属范围外，不登记为实际缺陷，也不建立同态配对。

## 2. 证据

- 实际：正常入口 PID 1032；`actual/actual-quick-open2.png`（`abcd7a1f…ffe5`）。
- 参考：隔离 2026.2.1，PID 23652；`reference/reference-go-to-file.png`（`a402e1a0…1c31`）。
- 几何（目视）：实际面板约 x814–2066 物理；参考约 x835–2040；实际面板更紧凑，参考有标签栏/底部路径栏。
- 参考端该次会话恢复了已删除 `sample.json` 的旧标签（背景残留），不影响弹层取证；实际端无此标签。

## 3. 边界与下一步

- 未测：空查询/空结果、键盘上下选择、Enter 打开、Esc 关闭、无匹配、深色、全仓搜索。
- 实际的“Open File”与参考“Search Everywhere”是**产品状态不同**，不能按同一个弹层做像素配对；若要比较，只能在文件-only 子状态内部比较字段/结果行。
- 不修改快速打开结构；页面级配对仍 4，像素 0。

## 4. 清理

- 实际 PID 1032、参考 PID 23652 正常关闭；实际设置恢复 `B8868855…DC3D`；参考未改主题/配置；未触碰用户原有 PyCharm；无端口/应用残留。
- `cleanup-check-61.json`；未提交、未推送。
