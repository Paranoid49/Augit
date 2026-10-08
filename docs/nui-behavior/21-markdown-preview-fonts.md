# 21 · Markdown 预览字号归属与死令牌（第 314 轮）

权威对照：`/mnt/d/github/intellij-community`（commit `576e32820af82f97529d70f9269726803a27c016`）。
本轮只动 `web/src/mockup.css`（及其字节一致副本 `docs/ux-mockups/mockup.css`），不动产品能力。

## 1. 起因：`--augit-ui-font-size` 是一个从未声明的令牌

- 唯一引用：`web/src/mockup.css:3319`，选择器 `.stripe-expand-label`（左侧工具窗口收起后的竖排「展开」标签）。
- 全仓库（`web/src`、`docs/ux-mockups`、`src/`、`tools/`）**没有任何声明**；`grep -r augit-ui-font-size` 只命中该引用与 `tools/audit/live-shell.spec.cjs:24634` 的读取。
- 后果：`var(--augit-ui-font-size, 13px)` 的回退值恒定生效，界面字号设为 9–40px 时该标签始终保持 13px。
- `tools/audit/live-shell.spec.cjs:24634` 的写法是
  `root.getPropertyValue('--augit-ui-font-size').trim() || getComputedStyle(document.documentElement).fontSize`，
  因为该属性永远为空，断言永远走 `||` 回退 ⇒ **这条断言测不到该令牌**（属空洞断言，按 `architecture.md` §8「断言必须能区分实现正确与实现错误」应改）。
- 修法：改用同一份 CSS 已在用的口径 `font-size: 1rem`（对照 `.current-find`、`.commit-detail h3` 的 `1rem` 写法），
  由 `:root { font-size }`（`applyTypography()` 写入）驱动。

## 2. Markdown 预览正文字号被写死

- 原规则：`.markdown-preview p, .markdown-preview li { font-size: 13px }`（工作区版本 `:3162-3165`）。
- 规范要求跟随界面字号：`design-system.md` §5.3 末「Markdown 预览正文跟随界面字号，代码块跟随等宽字号」、
  §5.4 表格「界面字号 → …和 Markdown 普通正文」、`ux-spec.md` §4.3。
- 实测（本机 Edge + Playwright，`docs/ux-mockups/markdown-preview.html?theme=light&ui-size=20`）：

| | 界面字号 | `.markdown-preview p` |
| --- | --- | --- |
| 修前 | 20px | **13px** |
| 修后 | 20px | **20px** |

- 修法：`font-size: inherit`（默认 13px 下取值不变；`h1`/`h2` 是标题角色字号，不在本次范围）。

## 3. Markdown 代码块此前完全没有规则

- `web/src/markdown.js:18` 生成行内 `<code>`、`:85` 生成 `<pre><code>`；而 `mockup.css` 里
  **不存在任何 `pre` / `code` 选择器**（`grep` 无命中）⇒ 代码块走浏览器 UA 的等宽字体、字号继承界面字号，
  与「代码块跟随等宽字号」正相反。
- 新增规则（与 `.code-view`/`.diff-columns`/`.terminal-view`/`.conflict-block` 取同一组令牌）：

```css
.markdown-preview pre,
.markdown-preview code {
  font-family: var(--augit-code);
  font-size: var(--augit-code-size);
}
```

- 负向/正向对照（合成注入一个 `<pre><code>`，因为**视觉稿本身没有代码块样本，现有验收脚本都覆盖不到这条规则**）：

| `ui-size=20` | 行内 `<code>` 字号/字族 | `<pre>` 字号/字族 |
| --- | --- | --- |
| HEAD 版 | 20px / `monospace` | 20px / `monospace` |
| 修后 | **13px** / `"Cascadia Mono", Consolas, monospace` | **13px** / 同左 |

- 登记缺口：`verify-ux-markdown.cjs`、`live-shell.spec.cjs` 与视觉稿都没有 Markdown 代码块样本 ⇒
  该规则目前**只由本表的人工对照覆盖**。补样本或补断言属于新增验收资产，本轮未做，列入待办（见 §6）。

## 4. 设置窗口标题与分组标题被写死

- `.settings-page h2 { font-size: 18px }`：界面字号调大时标题不放大，与 `design-system.md` §5.3 末
  「设置窗口执行"应用"后自身的文字和输入控件也必须立即使用新字体」不符。
  改为 `calc(18rem / 13)`：默认 13px ⇒ 18px（逐值不变），20px ⇒ **27.69px**（实测）。
- `.settings-group h3 { font-size: 13px }`：`ui-heading` 角色由界面字号设置覆盖，改为 `1rem`。

## 5. 验证命令与结果

```powershell
powershell -NoProfile -File tools/audit/verify-ui-assets.ps1   # PASS（两份 mockup.css 字节一致）
node tools/audit/verify-css-balance.cjs                        # PASS（括号配平）
node tools/audit/check-doc-claims.cjs                           # DOC_CLAIMS_OK
node tools/verify-ux-typography.cjs <playwright> <edge>         # 16 个场景通过
node tools/verify-ux-markdown.cjs  <playwright> <edge>          # 6 组三模式 + 2 组拖动边界通过
node tools/verify-ux-frame-buttons.cjs <playwright> <edge>      # 24/24 通过
```

- 默认界面字号（13px）下的「HEAD 计算样式 vs 当前计算样式」逐元素对照（Playwright `getComputedStyle`）：
  `settings.html`、`markdown-preview.html`、`git-history.html` 三个场景里，**13px 档只有本轮有意改动的
  `.rail-button.active` 底色/前景出现差异**，没有任何字号、行高或颜色意外漂移；20px 档的差异只有
  `.settings-page h2`、`.markdown-preview p` 两项（即本轮的两处修复）。

## 6. 本轮未做（登记，不自行决定）

| 项 | 原因 |
| --- | --- |
| `tools/audit/live-shell.spec.cjs:24634` 的空洞断言 | 该文件当时已有其它在途改动（`git diff` 非空），按「不覆盖既有改动」不在本轮改；建议改为直接断言 `.stripe-expand-label` 的计算字号随 `:root` 变化 |
| Markdown 代码块视觉稿样本 + 断言 | 属新增验收资产；当前 `markdown-preview.html` 没有任何 fenced code block |
| `.commit-detail-body { font: 13px / normal var(--augit-code) }` | 同属「Git 文本详情应由等宽字号驱动」，但它参与 Git 历史详情区的像素与布局断言，本轮未动 |
| `--augit-surface-2`（历史记录） | 已在本轮统一为已登记的 `--augit-diff-separator`（浅 `#E4E6EB`／深 `#2B2D30`），不再是待处理项 |
| 等宽/界面字体回退链与 `design-system.md:127-128` 不一致（缺 `Microsoft Sans Serif`、`Courier New`；用户设过字体后默认族整体被替换） | 需要先裁决「回退链以文档为准还是以实现为准」，并按 §5.4 补「字体缺失显示实际回退字体」 |
| 菜单项高度 30（`design-system.md:581`）/ 28（实现与 `10-backlog.md`）/ 26（权威 `List.rowHeight 24 + outerInsets 1+1` 推导）三值并存 | 本机无法复现参考图 1.5x 定标，无法判定真值 |
