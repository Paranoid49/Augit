# 搜索截断同态配对（2026-10-18）

## 1. 结论

完成 Find in Files 大量结果截断的两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 fixture；同一查询 `012345`；`notes.txt` 与 4.19MB `large.txt` 均命中；结果数量超阈值被截断；均未写文件。
- 实际：`Find in Files` 结果 + **`Too Many Results`** 模态：“More than 1000 results found. Continue searching?”；`Abort`/`Continue`；结果列表被截断。
- 参考：`Find in Files` 结果页；标题 **`100+ matches in 2+ files`**；`large.txt`/`notes.txt` 结果列表被截断（可继续滚动）；无模态。
- 页面级同态配对 **46 → 47**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 5344：编辑器 → Ctrl+Shift+F → `012345` → Enter → Too Many Results。
- 参考 PID 15200：Find Action `Find in Files` → 清空 File mask → 查询 `012345` → 点击搜索放大镜（不要用 Enter）→ `100+ matches in 2+ files`。
- 两端均未点 Continue/更深结果；未产生写操作。

## 3. 已知差异与边界

- 实际用模态阻塞并询问是否继续；参考用结果页 `100+` 截断计数。
- 阈值不同（实际 1000+，参考 100+）；**超时**（搜索卡住/500ms 捕获超时）本轮没有覆盖，不能把本配对当成“搜索截断/超时”全部完成。
- 未测空查询、文件掩码过滤、排除文件、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/search-truncation-pair/actual/actual-search-truncation.png`（`02503B47…ED8E`）。
- 参考：`reference/reference-truncation2.png`（`CE58E74E…0931`）。

## 5. 清理

- 实际 PID 5344、参考 PID 15200 已结束；临时 `large.txt` 删除；fixture `app.py` 仍 `B1986914…B954`、`notes.txt`/`README.md` 未变；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-148.json`；未提交、未推送。
