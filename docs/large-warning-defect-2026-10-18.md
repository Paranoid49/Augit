# 大文件 Warning 横幅缺失（2026-10-18）

## 1. 结论

在 `hideLargeFileWarning=false`（设置基线本身即为 false）的全新会话里打开 31.4MB `huge.txt`，实际端只显示只读预览，**没有渲染 Warning 横幅**；这不符合产品规范，登记为缺陷，未配对，页面级仍 48。

- 规范：`docs/ux-spec.md` 第 416 行要求超过内容加载上限的文本给只读预览并在编辑器顶部显示
  “文件过大（文件大小）。这里显示前 预览上限 的只读预览。”，带“隐藏通知”和“不再显示”。
- 实现：`mockup.js` 的 `liveTextDocument()` 已调用 `largeFileBanner(document_, ...)`；`largeFileBanner()` 以 `document_.readOnlyPreview` 为门槛；`live-data.js` 的 `toLiveDocument()` 只对 `payload.status === "TextPreview"` 置 true；宿主 `ReadOnlyDocumentService` 对 >20MB 返回 `DocumentReadStatus.TextPreview`。
- 实际：PID 17412 打开 31.4MB / 300,000 行 `huge.txt`，标签 `huge.txt`、`Read-only`、正文为前 2.5MB 预览；**无 Warning 横幅**（证据 `0B7B760C…CBC7B`，与第 132 批无横幅截图一致）。`settings.json` 的 `hideLargeFileWarning` 为 false，本次也显式确认。
- 因此缺陷在**实时文档到横幅的链路**（`document_.readOnlyPreview` 或渲染路径），不是用户关闭了警告。

## 2. 后续

- 用 CDP/调试实例读取 `window.__augitLive.document` 的 `status`、`readOnlyPreview`、`previewBytes`，确认是 `toLiveDocument()` 映射还是 `largeFileBanner` 挂载条件的问题。
- 修复后补自动化测试（状态到横幅），正常入口复验“横幅可见 → Hide Notification → Don't Show Again”三种状态，再与参考大文件提示配对。
- 在第 48 对的“警告已关闭”状态之外，不得把本缺陷当成已覆盖。

## 3. 清理

- 实际 PID 17412 已结束；临时 `huge.txt` 删除；`settings.json` 恢复 `B8868855…DC3D`（`hideLargeFileWarning` 原值 false）；fixture 未变。
- 本批未修改代码；`cleanup-check-160.json`；未提交、未推送。
