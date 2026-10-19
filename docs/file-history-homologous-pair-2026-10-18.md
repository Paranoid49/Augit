# File History 同态配对（2026-10-18）

## 1. 结论

完成 `app.py` File History 取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：`History: app.py` 工具标签；提交 `test: create pixel fixture`、`e9188bf`、`PixelAudit 2026/10/13 20:06`；`Branch: HEAD`；左侧提交行、右侧 `app.py` Diff 预览；`app.py` 5 行全部绿色（该提交新增）；浅色、2880×1716、DPI168。
- 实际：提交列表有 `Revision / Date / Author / Commit message` 列头，行内显示 `e9188bf`；右侧 Diff 头为 `e9188bf^` / `e9188bf`，底部标签 `Git / Log / History`。
- 参考：提交行不再单列 Revision 列（显示作者、日期、消息），右侧头为 `e9188bf3 app.py`，底部标签 `Git / Log / Console / History`。
- 页面级同态配对 **7 → 8**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 11932：Project 树右键 `app.py` → **File History**（菜单项）；打开 `History: app.py`。
- 参考 PID 13240：Project 树选中 `app.py` → Find Action **Show History**；打开 `History: app.py`。
- 参考首次误选 `notes.txt`（Project 行坐标偏差），已重新选中 `app.py` 后取得同文件页；误采图保留但不作为配对依据。

## 3. 已知差异

- 提交列表列头/Revision 列、右侧 Diff 的父/提交头（`e9188bf^`/`e9188bf` vs `e9188bf3`）。
- 底部标签实际缺 `Console`。
- 右侧 Diff 预览宽度、提交列表与 Diff 分隔位置、工具栏图标/间距。
- 未测：多提交历史、分页/加载、日期列宽、提交选择切换、Diff 交互、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/history-pair/actual/actual-file-history3.png`（`933C2E4C…8234`）。
- 参考：`reference/reference-file-history-app2.png`（`D757E14D…0AFC`）。
- 误选 notes：`reference/reference-file-history.png`（仅诊断）。

## 5. 清理

- 实际 PID 11932、参考 PID 13240 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；参考 notes/app.py 未改；未触碰用户原有 PyCharm；无端口/应用残留。
- 本批未修改代码；`cleanup-check-75.json`；未提交、未推送。
