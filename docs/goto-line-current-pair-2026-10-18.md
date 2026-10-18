# 跳转行空输入的当前双端几何复核（2026-10-18）

## 样本

- 参考：`artifacts/pixel-followup-2026-10-15/goto-current-pair/reference-empty.png`，PyCharm 2026.2.1 系统 PNG，584×252，Light、Microsoft YaHei UI 12、DPI168、空输入聚焦。
- 实际：`artifacts/pixel-followup-2026-10-18/refresh-current/before.png`，正常入口 `tools/start-augit.ps1` 构建，585×252，Light、Microsoft YaHei UI 12、DPI168、空输入聚焦。该图来自真实刷新批的 Go to Line 原生窗口，不是测试场景参数。
- 同状态：浅色、界面字体/字号、等宽正文 13、175% 缩放、空输入、输入框聚焦、无错误提示、无鼠标光晕。

## 当前几何

- 实际客户端 dialog 334×144 CSS，参考 333×144 CSS；字段均为 200×34 CSS，相对 dialog 左起 113 CSS、顶起 52 CSS；按钮均为 78×34 CSS、间距 6 CSS。
- 字段蓝色聚焦框：参考 `[203,95,546,147]`，实际 `[202,94,545,147]`，宽高相同，仅 1 物理像素左/上偏移。
- 当前实际不再复现 2026-10-15 记录的“字段整体 3 物理像素左移”；旧 3px 主要属于当时窗口框架/捕获原点与抗锯齿组合，不能继续当作未修复的 CSS 布局缺陷。
- dialog 宽度 334 对 333 CSS，是当前剩余的 1 CSS 级差异；未证明它来自可定位的布局错误，暂不按截图硬改。

## 差异与判定

- ROI `[190,85,562,160]`、RGB 容差 3：1598/27900 像素不同，最大通道差 255；差异集中在字段边框/字形抗锯齿。证据见 `comparison.json` 与 `diff-tolerance3.png`。
- 已批准/产品范围内差异：
  - 标题 `Go to Line` 对 `Go to Line:Column`；
  - 标签 `Line number` 对 `[Line] [:column]:`；
  - 品牌图标 `A` 对 PyCharm `PC` 标记。
- 因此不能把该图当作整页像素通过，也不能把 1598 像素差直接归因于字体极限；标题/标签/图标差异本来就不应要求逐像素相同。
- 该样本只证明**空输入、浅色、UI12、175% 下的跳转行字段/按钮相对几何与参考在 1 物理像素内**，不等于跳转行全部状态（错误、有效行、取消、深色、DPI1/2、焦点移动）通过，也不增加 54 页合格配对数量。

## 证据与复验

- 证据目录：`artifacts/pixel-followup-2026-10-18/goto-line-current-pair/`，含 `reference-empty.png`、`actual-empty.png`、`comparison.json`、`diff-tolerance3.png`。
- 复验：重新用正常入口打开 Go to Line（空输入聚焦），以 `capture-screen-region.ps1` 取该模态系统 PNG，再按 `comparison.json` 的 ROI/容差重算；实际尺寸若为 585×252，需先说明窗口框架 1px 差异，不能强行缩放。
- 该复核不修改产品、不改变合格页计数；整体保留页面双端同态仍为未完成。
