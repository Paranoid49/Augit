# Find in Files 空态同态配对（2026-10-18）

## 1. 结论

完成 Find in Files 空态两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Find in Files 界面；查询为空、无结果；范围 In Project；未执行搜索、未改文件。
- 实际：主窗内居中浮层；标题 `Find in Files`；空输入；刷新、W、`.*`、`Include ignored files`；无空态提示；约 2880 主窗内嵌。
- 参考：独立 `Find in Files` 工具窗（1113×1517）；空输入；`File mask: *.py`；`In Project/Module/Directory/Scope`；中央 `Type search query to find in files`；`Open results in new tab`、`Ctrl+Enter`、`Open in Find Window`（禁用）。
- 页面级同态配对 **37 → 38**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 20360：左侧搜索轨按钮（物理 `34,274`）→ `Find in Files` 浮层；空查询。
- 参考 PID 23800：Ctrl+Shift+A → 输入 `Find in Files` → Enter 打开工具窗；清空查询（点击输入框右侧 ×）后为空态。
- 说明：此前第 104 批 Ctrl+Shift+F 未显示搜索界面；本批改用 Find Action 成功，未再复现快捷键问题，不判缺陷。

## 3. 已知差异与边界

- 实际为居中浮层、无文件掩码/范围标签；参考为右侧工具窗、File mask `*.py`、范围标签和空态文案。
- 实际 `Include ignored files` 对应参考 `File mask`/范围；控件语义不完全等价。
- 未测：有结果、零结果、正则/忽略文件、搜索截断/超时、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/search-empty-pair/actual/actual-search-empty.png`（`AC03DB79…BCD2`）。
- 参考：`reference/reference-search-empty2.png`（`089DD7C5…24E7`）。

## 5. 清理

- 实际 PID 20360（前批）、参考 PID 23800 已结束；`settings.json` 恢复 `B8868855…DC3D`；未改文件；`cleanup-check-126.json`；未提交、未推送。
