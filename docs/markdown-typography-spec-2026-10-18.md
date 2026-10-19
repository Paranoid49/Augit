# Markdown 预览排版规格核对（2026-10-18）

## 1. 结论

Markdown 预览的垂直节奏与参考 PyCharm 不同，但实际实现**遵循** `design-system.md` 第 141 条的既有规格，不是可定位的实现偏差：

- 预览正文跟随界面字号 12px、行距 1.6（19.2px），左右内距 2em（24px）。
- h1：`2.2em`（26.4px）、上外边距 `1em`（26.4px）、下外边距 16px、上内距 `0.6em`（15.84px）。
- 段落：上下外边距 16px。
- 代码块：等宽 13px、行高 1.45（18.85px）、内距 16px、圆角 3px。
- 参考预览更紧凑（标题顶 243、代码块底约 355；实际 233/419）；该差异登记为**规格值 vs 参考 CSS**，不按参考截图硬改。

## 2. 证据

- 脚本：`artifacts/pixel-followup-2026-10-18/markdown-pair/inspect-styles-ui12.cjs`
- 输出：`markdown-typography.json`（UI 12，`markdown-state=preview`；代码块为测量时向静态样本注入，不修改产品文件）。
- 计算样式：h1 26.4px/36.96px，margin 26.4/16，padding-top 15.84；p 12px/19.2px，margin 16；pre 13px/18.85px，padding 16，radius 3，背景 `rgb(247,248,249)`；preview 左右 padding 24。
- 与 `design-system.md` 第 141 条逐项一致。

## 3. 对 Markdown 配对的影响

- [Markdown 分栏同态配对](markdown-homologous-pair-2026-10-18.md) 中“代码块垂直节奏更松/标题 −10”属于上述规格值差异，不再登记为未知 CSS 缺陷。
- 不修改 `markdown-preview` 的排版去迎合参考；如用户希望改成参考 CSS，应先修订设计系统规格，而不是在实现里硬编码。
- 该配对仍为**已知差异、非像素合格**；页面级计数 3、像素 0 不变。

## 4. 边界

- 静态样本的代码块是测量时注入的 DOM，不代表视觉稿内容；字号/间距来自共享 CSS，可信。
- 未测长文档、表格、列表、图片、深色、不同 UI 字号；这些仍需单独状态取证。
- 本批未启动应用，无 PID/端口/进程清理。
