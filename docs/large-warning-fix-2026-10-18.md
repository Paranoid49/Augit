# 大文件 Warning 横幅不可见修复（2026-10-18）

## 1. 根因

CDP 检查 31.4MB `huge.txt` 时，DOM 与状态都正确：

```
status=TextPreview  readOnlyPreview=true  previewBytes=2560000
banner=true  bannerText="Large file (29.9 MB). Showing the first 2.4 MB as a read-only preview.
Hide Notification / Don't Show Again"
```

但横幅的 `getBoundingClientRect().height` 只有 **13.14px**，`getComputedStyle` 显示 `padding:6px 10px` 被压进一行文字：横幅是 `.document-view` 的额外 grid item，而 `.document-view` 只有
`grid-template-rows: var(--augit-document-toolbar-height) minmax(0, 1fr)` 两行，横幅落到隐式行并被正文挤出视口。不是 `readOnlyPreview` 或用户关闭警告的问题。

## 2. 修复

- `web/src/mockup.css` 与 `docs/ux-mockups/mockup.css`（两份一致）：
  - `.editor-notification` 增加 `min-height: 26px; box-sizing: border-box;`；
  - 新增三条网格行规则，把横幅插到工具栏与正文/查找条之间：
    - `.document-view:has(> .editor-notification)`；
    - `.document-view:has(> .editor-notification):has(> .current-find)`；
    - `.document-view.has-target:has(> .editor-notification)`。
- 新增 `tools/test-large-file-banner-css.mjs`：**8/8 PASS**（min-height、border-box、内距、三条网格行、两份一致）。
- `verify-ui-assets.ps1` 通过；`mockup.css` 现为 `53BC7DF7…1A6C`，两份一致。

## 3. 正常入口复验

- 实际 PID 14720：31.4 MB `huge.txt` 打开后，编辑器顶部显示 **`Large file (29.9 MB). Showing the first 2.4 MB as a read-only preview.`**，右侧 **Hide Notification** / **Don't Show Again**；正文从第 0 行开始（证据 `E7B78435…A184`）。
- 修复前同场景无横幅（`0B7B760C…CBC7B`），CDP 横幅高度 13.14px；修复后可见。
- 参考端大文件警告未采；本项为实际侧状态，不构成页面级配对，页面级仍 48。

## 4. 后续（2026-10-19 已闭环）

- “Hide Notification（会话内、按路径）”与“Don't Show Again（应用级、持久化）”已用正常入口、真实鼠标、重开与正常重启验证；详见[真实输入与持久性闭环](large-warning-real-input-2026-10-19.md)。
- 参考 2026.2.1 打开同一 28.5 MB 文件时未显示 Warning 横幅，也没有对应动作；参考默认配置未被改写，故没有可配对的参考警告 UI。此结论不外推到其他文件类型或非默认配置。

## 5. 清理

- 实际 PID 14720 已结束；临时 `huge.txt` 删除；`settings.json` 恢复基线 `B8868855…DC3D`；fixture 未变。
- 本批修改两份 `mockup.css` 并新增测试；`cleanup-check-161.json`；未提交、未推送。
