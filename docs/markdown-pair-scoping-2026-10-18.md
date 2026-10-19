# Markdown 分栏状态取证（2026-10-18）

## 0. 2026-10-18 21:38 更新

已把实际分隔线拖到参考位置（实际 1759–1760、参考 1760–1761，差 1 物理像素），并将该状态登记为**页面级同态配对（已知差异，非像素合格）**；页面级计数 2 → 3，像素合格仍 0。详见[Markdown 同态配对](markdown-homologous-pair-2026-10-18.md)。以下 scoping 保留为取证过程。

## 1. 结论（scoping 阶段）

完成 `README.md` 的 **源码 + 预览分栏**状态取证（浅色、同文件、同内容）：

- **未建立同态配对**：实际是单标签、源码无 Markdown/Python 语法高亮；参考保留 `notes.txt` + `README.md` 双标签、源码有语法高亮和当前行/光标。
- 共同点：同一 `README.md`、同一分栏模式、预览区同样渲染标题/正文/行内代码/代码块；浅色、2880×1716、DPI168。
- 实际默认是**仅预览**三模式之一；点击分栏按钮后与参考的“Editor and Preview”状态可比。参考“仅预览”未在本次操作中可靠到达。
- 不增加页面级同态配对 2，像素合格仍 0；不修改代码。

## 2. 方法与身份

- 实际：正常入口 PID 5996，项目树打开 `README.md`，点击右上分栏按钮；2880×1716、DPI168、浅色。
- 参考：隔离 2026.2.1，PID 10112，打开 `README.md`，Find Action → “Preview” 打开分栏；同尺寸/DPI/浅色。
- 原始图：
  - 实际：`artifacts/pixel-followup-2026-10-18/markdown-pair/actual/actual-readme-split.png`（`2d50f556…91e7`）
  - 参考：`reference/reference-readme-split.png`（`72e65584…fa34b`）
  - 对照：`markdown-split-compare.png`（`971f9526…ea46`）
- 实际仅预览初始图：`actual/actual-readme-initial.png`（`af6f47de…b6c3c`）；参考分栏前源码图：`reference/reference-readme-source2.png`（`3ef27ade…db1d`）。

## 3. 同态与差异

| 项目 | 参考 | 实际 |
| --- | --- | --- |
| 文件/内容 | README.md：`# Pixel reference`、Body `value`、python 代码块 | 同 |
| 模式 | 源码 + 预览分栏 | 源码 + 预览分栏（默认仅预览，可切） |
| 标签 | notes.txt + README.md 双标签 | 仅 README.md 单标签 |
| 源码 | Markdown/Python 语法高亮、当前行底、光标、折叠槽 | 灰色纯文本、无语法高亮、无编辑光标 |
| 分隔线 x | 约 1760 物理 | 约 1784 物理（+24） |
| 预览标题暗像素 bbox | x 1810–2160，y 243–355 | x 1839–2176，y 233–419（+29，−10） |
| 预览内容 | 标题/正文/行内代码/代码块 | 同内容；字体层级目视接近 |

## 4. 边界与下一步

- 未测：仅预览模式的参考截图、滚动/长文档、表格/链接/图片、深色、编辑/预览比例拖动、预览字体切换。
- 参考源码可编辑与实际的只读是产品范围差异；语法高亮/标签模型是实际结构差异，不能在 scoping 中记成通过。
- 若要继续 Markdown 页，应先核对 `ux-spec §7.3` 的三模式/字号归属，再决定是否对齐标签模型或源码高亮。

## 5. 清理

- 实际 PID 5996、参考 PID 10112 正常关闭；实际设置恢复 `B8868855…DC3D`；参考未改主题/配置；未触碰用户原有 PyCharm；无端口/应用残留。
- `cleanup-check-57.json`；未提交、未推送。
