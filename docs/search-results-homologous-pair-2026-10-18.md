# Find in Files 结果同态配对（2026-10-18）

## 1. 结论

完成 Find in Files 查询 `Ag` 的结果页两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 fixture、同一查询 `Ag`；首条结果均为 `notes.txt 1: Ag 0123456789`；均未改文件。
- 实际：主窗内浮层；共 **5 条**：`notes.txt`、`modules.xml:3`、`fixture.iml:3`、`fixture.iml:8`、`profiles_settings.xml:1`（后四条均来自 `.idea`）；`Include ignored files` 未勾选；无文件掩码。
- 参考：右侧 `Find in Files` 工具窗；**1 match in 1 file**（`notes.txt`）；`File mask: *.py`；In Project；结果 `notes.txt 1` 与正文预览。
- 页面级同态配对 **38 → 39**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 9908：左侧搜索轨按钮 → 输入 `Ag` → Enter；浮层显示 5 条结果。
- 参考 PID 23800（第 105 批）：Find Action 打开 Find in Files → 输入 `Ag`；工具窗显示 1 条结果；本批复用该原始截图。
- 两端均未打开结果、未改文件。

## 3. 已知差异与边界

- 实际搜索包含 `.idea` 下的未跟踪 XML；参考只返回 `notes.txt`（项目内容/掩码 `.py` 的差异）。这是真实的**结果集差异**，不据此判缺陷，需按产品搜索规则（是否搜索未跟踪/IDE 配置目录）核对。
- 实际为浮层、无 File mask/范围标签；参考为工具窗、有 File mask/结果预览。
- 未测：零结果、正则、忽略文件、截断/超时、结果跳转。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/search-results-pair/actual/actual-search-results.png`（`C00CE963…4852`）。
- 参考：`reference/reference-search-results.png`（`9460811E…153D`）。

## 5. 清理

- 实际 PID 9908 已结束；参考进程前批已结束；`settings.json` 恢复 `B8868855…DC3D`；未改文件；`cleanup-check-127.json`；未提交、未推送。
