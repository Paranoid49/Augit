# Blame 同态配对（2026-10-18）

## 1. 结论

完成 `app.py` Blame 取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：`app.py` 5 行；第 1–4 行归属 `2026/10/13 PixelAudit`；第 5 行 `# changed for pixel audit` 为未提交修改；行号 1–5；底部 Git Log；浅色、2880×1716、DPI168。
- 实际（修复后 PID 3084）：**Blame 只读文档**，工具栏 `app.py Read-only` + `5 Line Attribution` + Close；1–4 行 `2026/10/13 Pi…`；**第 5 行未提交归属槽为空、仅保留行号 5**；无 usage inlay。
- 参考：**可编辑 app.py 编辑器** + 左侧 Git Blame 注释槽；1–4 行 `2026/10/13 PixelAudit`；**第 5 行注释为空**，未提交行灰显；顶部 `1 usage PixelAudit` inlay；底部 `Git / Log / Console`。
- 页面级同态配对 **8 → 9**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 2312：Project 树右键 `app.py` → **Blame**；打开只读 Blame 文档。
  - 先前对 `.idea` 目录右键再点 Blame 无反应，是目标为目录，不是产品 Blame 缺陷；改选 `app.py` 后正常。
- 参考 PID 17560：打开 `app.py` 编辑器 → Find Action **Annotate with Git Blame**；注释生效后按 Esc 关闭查找层，取得干净图。

## 3. 已知差异

- 实际是只读 Blame 文档（有 `5 Line Attribution`/Close），参考是可编辑编辑器的注释模式。
- 第 5 行未提交行已修复：`blameLineUncommitted()` 把全零 hash / `Not Committed Yet` 行渲染成空白归属槽（不显示日期、作者、提交提示，不响应点击），与参考的空白注释槽一致；测试 5/5 PASS。
- 参考有 usage inlay、`No Python interpreter` 通知；实际没有。
- 底部标签实际缺 Console；工具栏/行号槽/归属槽宽度不同。
- 未测：多提交行、悬停提示、Annotate Previous Revision、关闭 Blame、深色、大文件。

## 4. 证据

- 修复前实际：`artifacts/pixel-followup-2026-10-18/blame-pair/actual/actual-blame-normal2.png`（`FC1B10E5…CF5B`）。
- 修复后实际：`blame-fix/actual-blame-fixed.png`（`DC8DFF19…E441`），第 5 行归属槽空白。
- 参考：`reference/reference-blame-clean.png`（`CB150B37…D356`）；查找层图 `reference-blame.png` 仅诊断。
- 回归：`tools/test-blame-uncommitted.mjs` 5/5 PASS；`verify-ux-offline-icons.cjs` PASS 110 场景/主题（`offline-icons-after-blame-fix/`）；`verify-ui-assets.ps1` PASS。
- 当前 `mockup.js` 新哈希 `4E7E3202…BCE7`；`cleanup-check-77.json`。

## 5. 清理

- 实际 PID 2312、参考 PID 17560 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；参考未改主题/配置/文件；未触碰用户原有 PyCharm；无端口/应用残留。
- 修复后实际 PID 3084 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-77.json`；未提交、未推送。
