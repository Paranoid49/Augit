# Find in Files 零结果同态配对（2026-10-18）

## 1. 结论

完成 Find in Files 查询无匹配的两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 fixture、同一查询 `zzzznotfound`；无结果；In Project；均未搜索出文件、未改文件。
- 实际：主窗内浮层；`No results found` + `Try another query or adjust the filters.`；无 File mask。
- 参考：右侧 `Find in Files` 工具窗；`Nothing found`；`File mask: *.py`；`In Project/Module/Directory/Scope`；`Open results in new tab`、`Ctrl+Enter`、`Open in Find Window`。
- 页面级同态配对 **39 → 40**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 24336：左侧搜索轨按钮 → 输入 `zzzznotfound` → Enter；浮层零结果。
- 参考 PID 17156：Ctrl+Shift+A → `Find in Files` → Enter 打开工具窗（HWND 3475234，标题为空）→ 对该工具窗直接输入 `zzzznotfound`；显示 `Nothing found`。
- 教训：参考 Find 工具窗标题为空，不能用标题过滤；应枚举除主窗外的可见窗口并对该 HWND 直接输入。前批先激活主窗导致工具窗消失，本批已规避。

## 3. 已知差异与边界

- 实际零结果文案 `No results found` + 建议；参考 `Nothing found`。
- 实际浮层、无 File mask；参考工具窗、File mask `*.py`、范围标签。
- 未测：正则/忽略文件、结果跳转、搜索截断/超时、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/search-no-results-pair/actual/actual-search-no-results.png`（`7B66F92B…B870`）。
- 参考：`reference/reference-search-no-results4.png`（`7294B07F…E364`）。

## 5. 清理

- 实际 PID 24336、参考 PID 17156 已结束；`settings.json` 恢复 `B8868855…DC3D`；未改文件；`cleanup-check-129.json`；未提交、未推送。
