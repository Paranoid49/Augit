# 设置搜索命中只读固定等宽信息（2026-10-20）

## 1. 问题

2026-10-15 用户裁决后，File View 不再提供等宽字体/字号编辑控件，只显示只读的
`JetBrains Mono` / `13`（`[data-fixed-code-font]`、`[data-fixed-code-size]`）。
`settingsPageSearchTexts()` 只索引带 `[data-setting]` 的字段，因此搜索
`Monospaced`、`Size`、`JetBrains Mono` 都找不到 File View，分类被隐藏。
PyCharm 的 `SearchableOptionsRegistrar` 按可见选项名检索，不要求字段可编辑。

## 2. 改动

- `web/src/mockup.js`：固定等宽行加 `data-settings-search="Monospaced font JetBrains Mono Size 13"`。
- `web/src/live-data.js`：`settingsPageSearchTexts()` 在 `[data-setting]` 之外收集
  `[data-settings-search]` 文本，键为 `fixed-code`；该页因此进入命中集合，搜索会自动切到 File View。
- 只读行仍没有编辑控件、不产生 `settings/write`；未恢复等宽字体/字号编辑。

## 3. 测试

- `tools/audit/live-shell.spec.cjs` §7.17：
  - File View 断言改为“只读显示 JetBrains Mono/13、无 `codeFontSize` 控件”；
  - 切页草稿只改界面字号；保存断言写回 `fontSize` 且不写 `codeFontSize`/`monospaceFontFamily`；
  - 搜索 `Monospaced` 命中 File View、`Size` 同时命中 Appearance 与 File View；
  - 结构白名单移除已不存在的等宽字段。
- 空名称紧凑窗口的 Tab 循环按“跳过禁用 OK”断言（`field,cancel,close,field`），
  4 次 Tab 后焦点在 Cancel，Shift+Tab 反向回到输入框。
- 终端 Esc 用例先打开一个文档，确保存在“可聚焦的正文”再验证焦点交接。

## 4. 验证与边界

- `run-17`/`run-18` 已通过 §7.17 设置段落；`run-19` 通过 Ctrl+G 紧凑窗口（用
  `data-compact-dialog` 属性判标题，不再依赖 header 首个 span 的 innerText）。
- 只读行没有 `data-setting`，搜索不会在行上画 spotlight 边框；只保证分类可见与切换。
- 页面级配对仍 **50**、像素合格仍 **0**；本项不增加页面数。
