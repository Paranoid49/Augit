# 搜索截断取证尝试（2026-10-18）

## 1. 结论

在 fixture 临时加入 4.19MB `large.txt`，实际端出现 **Too Many Results** 截断页，但参考端未采到对应截断页，**未建立配对**，页面级仍 46。

- 实际 PID 5344：Find in Files 输入 `012345` → 结果包含 `notes.txt` 与 `large.txt` 多行；弹出 **`Too Many Results` — “More than 1000 results found. Continue searching?”**，`Abort`/`Continue`（蓝色）；结果列表被截断。
- 参考 PID 22548：
  - Ctrl+Shift+F 未打开 Find in Files，反而把 `012345` 输入到编辑器 `app.py` 第 1 行；随后已恢复 fixture `app.py` 到基线 `B1986914…B954`（95 字节，无保存）。
  - Find Action 打开 Find in Files，默认查询 `Ag`、File mask `*.py`，显示 `1 match in 1 file`；清空 mask 后搜索 `012345` 时 Enter 把焦点移回主窗，工具窗关闭，未捕获参考截断页。
- 两端的搜索入口/掩码/截断行为不同；没有同态页，不强行配对。

## 2. 后续

- 参考端在 Find in Files 工具窗内用**按钮/放大镜**触发搜索（不要用 Enter），并确认 File mask 为空、`large.txt` 已索引/已包含；目标是参考的“Too many results”提示。
- 实际端可点 `Continue` 验证继续搜索后的行为（本次未点，避免无边界长搜索）。

## 3. 清理

- 实际 PID 5344、参考 PID 22548 已结束；临时 `large.txt` 删除；fixture `app.py` 恢复 `B1986914…B954`、`notes.txt`/`README.md` 未变；`settings.json` 恢复 `B8868855…DC3D`；无 Augit/pycharm64 残留。
- 本批未修改仓库代码；`cleanup-check-146.json`；未提交、未推送。
