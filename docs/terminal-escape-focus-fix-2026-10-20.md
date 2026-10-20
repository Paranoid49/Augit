# 终端 Esc 交回正文的隐藏目标修复（2026-10-20）

## 1. 现象

`live-shell` §7.16 终端正文按 Esc：按键**没有**送给 Shell（`terminal/write` 为空），但焦点仍停在终端
（`focusInTerminal: true`、`focusInEditor: false`）。用户侧表现为 Esc 被“吃掉”，没有任何可见反应，
与权威 `TerminalEscapeKeyListener` 的「交给编辑器组件并 consume」不符。

## 2. 根因

`focusEditorFromTerminal()` 用 `document.querySelector('.editor-content .code-view, …')` 取第一个节点。
Markdown 预览模式下 `.markdown-source`（`.code-view`）仍在 DOM 中但 `display:none`；对隐藏节点调
`focus()` 是空操作，函数却返回 `true`，于是按键被消费而焦点没有移动。

## 3. 修复

`web/src/live-data.js`：候选节点先按 `getClientRects().length > 0` 过滤，只对**可见**正文目标
（`.code-view`、`.markdown-source` 或 `[tabindex="0"]`，预览模式下即 `.markdown-preview`）调 `focus()`；
没有可见目标时仍返回 `false`，让 Esc 照常送给 Shell（与权威 `toolWindow == null` 守卫一致）。

## 4. 验证

- `live-shell` 终端用例在打开文档后复跑，Esc 断言通过（`run-22` 越过该段，后续停在测试自身的
  `panes` 残留引用；`run-23` 复跑中）。
- 这是桩/无头环境证据，**不等于**正常入口实机通过；实机 Esc 交回正文仍需在正常入口补验。
- 套件整体仍未通过，本项不改变“像素 0 / 页面级 50”的计数。
