# 工作区 Diff 同态配对（2026-10-18）

## 1. 结论

完成 `app.py` 工作区只读 Diff 取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：左侧 Commit 工具窗口、Changes 1 个 `app.py` 勾选；`Commit: app.py` Diff 标签；左右并排；相同 5 行内容；第 5 行新增 `# changed for pixel audit` 绿色高亮；行号；右上 “1 difference”。
- 实际：左/右标题 `HEAD` / `Workspace`，行尾 `LF` / `Mixed line endings`；无编辑铅笔；工具栏箭头/搜索/复制；底部 Git Log；按范围只读。
- 参考：左/右 `e9188bf3` / `Current version`，`CRLF` / `LF`；有编辑铅笔；工具栏箭头/铅笔/1 file；底部 Git/Log/Console。
- 页面级同态配对 **6 → 7**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 13396：真实鼠标点击工具轨 `aria-label=Commit`（物理 32,187），展开 Changes（点击 114,227），双击 `app.py`（200,274）打开 Diff。
- 参考 PID 18664：`Ctrl+K` 打开 Commit，双击 `app.py` 行；第一次双击只选中行，再次双击（260,260）后才打开 Diff 标签。
- 两端浅色、2880×1716、DPI168；参考未改主题/配置。

## 3. 已知差异

- 标题标签：`HEAD/Workspace` vs 提交哈希/`Current version`。
- 行尾标记：`LF/Mixed line endings` vs `CRLF/LF`。
- 实际无编辑铅笔（产品不提供内置文本编辑）；参考有。
- 工具栏：实际搜索/复制，参考铅笔/1 file；底部实际 Git Log，参考 Git/Log/Console。
- 主窗项目面板/文档工具栏等既有差异同样存在。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/diff-pair/actual/actual-diff2.png`（`cdc0d88c…195e`）。
- 参考：`reference/reference-diff2.png`（`ddc3cd4b…b352`）；首次选中行未打开：`reference-diff.png`。
- 未测：编辑/回退、行内操作、折叠、深色、冲突 Diff、提交间 Diff。

## 6. Unified 状态补充（2026-10-18）

- 两端用真实鼠标点击 Unified 视图按钮；内容仍是 5 行、第 5 行新增绿色、“1 difference”。
- 修复前实际：`HEAD app.py` / `Workspace` 两行标题；行号只显示变更行的 5/6，上下文 1–4 没有行号。
- 修复后实际（PID 10520）：`1/1`、`2/2`、`3/3`、`4/4`、空/`5`，上下文行号恢复；仍为 `HEAD/Workspace`、`Mixed line endings`、无编辑铅笔。
- 参考：`e9188bf3 app.py` / `Current version`；行号成对 `1/1`…`5/5`。
- 修复：`diffBodyParts()` 的 live unified 模板改用 `unified-diff-line`，同时输出 `oldCells/newCells` 两列 `unified-number`；`web/src/mockup.js` 与 `docs/ux-mockups/mockup.js` 字节一致。
- 回归：`node tools/test-diff-unified-line-numbers.mjs` 4/4 PASS；`verify-ui-assets.ps1` PASS；正常入口实采行号 1–5 可见。
- 剩余已知差异：标题文案、行尾标记、无编辑铅笔、工具栏/底部 Log；不改变页面级配对 7、仍非像素合格。
- 证据：修复前 `diff-unified/actual/actual-diff-unified3.png`（`79C78576…47D2`）；修复后 `diff-unified-fix/actual-unified-fixed2.png`（`DF5A4BCB…18C`）；参考 `diff-unified/reference/reference-diff-unified.png`（`2A97EE93…C7F`）。
- `cleanup-check-72.json`（修复前取证）、`cleanup-check-73.json`（修复后验证）。

## 5. 清理

- 实际 PID 13396、参考 PID 18664 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；参考 notes/app.py 未改；未触碰用户原有 PyCharm；无端口/应用残留。
- 本批未修改代码；`cleanup-check-71.json`；未提交、未推送。
