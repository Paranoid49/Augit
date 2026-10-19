# Compare with Workspace 同态配对（2026-10-18）

## 1. 结论

完成 `app.py` 的 main/提交↔工作区比较取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 `app.py`、**两侧均有代码内容**、`1 difference`、拆分视图、绿色新增第 5 行、两侧行号、只读；均**未写 Git**；浅色、DPI168。
- 实际：标签 `Compare: app.py:main ↔ Workspace`；左侧 `main app.py` 内容（`def greet...` 等），右侧 `Workspace` 内容；`LF` / `Mixed line endings`。
- 参考：标签 `Changes: app.py`；左侧 `e9188bf3 app.py` 完整提交内容；右侧 `Current version`；`CRLF` / `LF`；`>> 5` 标记与绿色第 5 行。
- 页面级同态配对 **19 → 20**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 15028：分支 chip → 分支弹层 → `Compare with Workspace`（原计划 New Branch，命中比较项）；比较标签打开。
- 参考 PID 25352：Git Log 提交右键 → `Compare with Local` → 左侧 `Changes Between e9188bf3 and local version` → 双击 `app.py` → `Changes: app.py`。

## 3. 已知差异与缺口

- **更正**：先前预览图误读为“实际左侧基线内容为空”；对 2880×1716 原始 PNG 逐像素采样（步长 3）得左侧暗像素 464、右侧 530，左侧代码实际存在。此条不登记为缺陷，保留采样命令供复验。
- 标签命名 `Compare: app.py:main ↔ Workspace` vs `Changes: app.py`；头 `main` vs `e9188bf3`、`Workspace` vs `Current version`；行尾 `Mixed line endings` vs 明确 `CRLF/LF`；参考有 `>> 5` 标记/勾选。
- 布局/工具栏（实际有搜索框、参考有差异导航/文件数）。
- 未测：多文件、二进制、空白差异、单栏模式、跳转、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/compare-workspace-pair/actual/actual-compare.png`（`E3ED6EBC…F4B7`）。
- 参考：`reference/reference-compare-diff2.png`（`2EF396B6…FD96`）；参考变化列表：`reference-compare.png`（`42AD54EA…D667`）。

## 5. 清理

- 实际 PID 15028、参考 PID 25352 已结束；`settings.json` 恢复 `B8868855…DC3D`；未写 Git；无残留；`cleanup-check-98.json`；未提交、未推送。
