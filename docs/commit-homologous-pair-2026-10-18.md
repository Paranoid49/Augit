# Commit 工具窗口同态配对（2026-10-18）

## 1. 结论

完成 Commit 工具窗口取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：左侧 Commit 工具窗口；`Changes` 1 个文件 `app.py` 勾选；`Unversioned Files` 6 个 `.idea` 文件；Amend；提交信息空；Commit / Commit and Push；`notes.txt` 编辑区；底部 Git Log；浅色、2880×1716、DPI168。
- 实际：`Amend last commit` 无 “1 modified” 标签；提交信息占位为 `Commit message`、无聚焦蓝框；工具栏图标较少。
- 参考：`Amend last commit` 带 “1 modified”和下拉/历史；提交信息 `Commit Message` 且有聚焦蓝框；工具栏多放大/折叠入口。
- 其余布局/文件列表/按钮位置接近；页面级同态配对 **5 → 6**，像素合格仍 **0**。

## 2. 可达性更正

- 实际 Commit 可通过左侧工具轨 `aria-label=Commit` 按钮打开；CDP 实测按钮 CSS 中心约 `(18.5,107)`、物理 `(32,187)`。此前用 `Ctrl+K`/错误轨道坐标未打开，是入口/坐标问题，不是页面缺失。
- 实际正常入口 PID 18592 通过真实鼠标点击该轨道按钮打开 Commit，再打开 notes.txt 对齐参考编辑区。

## 3. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/commit-pair/actual/actual-commit-aligned.png`（`2616d34e…0ba3`）。
- 参考：隔离 2026.2.1，PID 6392，Ctrl+K；`reference/reference-commit-expanded.png`（`b7304a79…e41f`）。
- 实际初始空白编辑区：`actual/actual-commit-normal.png`（`346fc556…0c5`）；参考初始未展开 Unversioned：`reference/reference-commit.png`（`712510e1…e92ee`）。

## 4. 边界

- 未测：提交实际动作（不执行写 Git）、Amend 展开、Commit and Push、文件反选、空 Changes、冲突/失败、深色。
- 已知的 “1 modified”/占位大小写/聚焦蓝框/工具栏图标/底部 Log 选中状态不同；不称像素合格。
- 本轮未修改代码。

## 5. 清理

- 实际 PID 18592、参考 PID 6392 正常关闭；实际设置恢复 `B8868855…DC3D`；参考未改主题/配置；未触碰用户原有 PyCharm；无端口/应用残留。
- `cleanup-check-66.json`；未提交、未推送。
