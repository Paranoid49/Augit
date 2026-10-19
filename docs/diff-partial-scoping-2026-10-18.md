# Diff 局部加载取证尝试（2026-10-18）

## 1. 结论

在 fixture 临时加入 4.19MB 未跟踪 `large.txt`，实际端 Diff 完整加载，未出现局部加载/截断提示；参考端未采集，**未建立配对**，页面级仍 46。

- 实际 PID 20176：Commit 视图 → Unversioned Files 中双击 `large.txt` → `Commit: large.txt` 标签，`1 difference`，`HEAD`（空）/`Workspace`（全绿新增），可见第 0–21 行，未出现 `Loading…`/partial/limit 提示；文件可继续滚动。
- 检查过程中行坐标误点曾把 `.idea/fixture.iml` 与 `large.txt` 勾选；取证后未 Commit，`git status` 仍为 `M app.py` + `?? .idea/`，未产生暂存。
- 参考端未启动；未建立配对。

## 2. 后续

- 实际端对 4.19MB 未跟踪文件的 Diff 是**一次性完整加载**；若产品规范要求局部加载/大小阈值，需要在实现层核对后在更大文件上验证，不应把本次“无提示”当作局部加载页已覆盖。
- 参考端可用同一未跟踪文件在 Commit 工具窗打开 Diff，观察是否有 “Diff is too large”/partial 提示。

## 3. 清理

- 实际 PID 20176 已结束；临时 `large.txt` 删除；fixture 回到 `M app.py`（基线 `B1986914…B954`）+ `?? .idea/`；`settings.json` 恢复 `B8868855…DC3D`；无 Augit/pycharm64 残留。
- 本批未修改仓库代码；`cleanup-check-147.json`；未提交、未推送。
