# 大文件只读预览同态配对（2026-10-18）

## 1. 结论

用超过实际内容加载上限（20 MB）的 31,388,893 字节 `huge.txt` 完成两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 31.4 MB 文本；均打开 `huge.txt` 并显示第 0–24 行起的同一内容；均未弹“文件过大”对话框、未写文件；两端的大文件警告均处于此前已被关闭的状态。
- 实际：`huge.txt` 标签、工具栏 `Read-only`；显示只读预览（前 2.5 MB 内容）；无 Warning 横幅（`largeFileWarningDismissed` 已持久化）。
- 参考：`huge.txt` 标签、普通编辑器；显示同一文件开头；无“File is too large”对话框/警告。
- 页面级同态配对 **47 → 48**；像素合格仍 **0**。

## 2. 取证路径

- 临时文件 `fixture/huge.txt`：300,000 行、31,388,893 字节，超过 `DocumentLimits.DefaultContentLoadBytes`（20 MB）。
- 实际 PID 3516：正常入口 → Ctrl+P `huge` → Enter → `huge.txt`；Esc 关闭 Quick Open 后截取。
- 参考 PID 6156：Project 树双击 `huge.txt`；直接打开预览，无对话框。
- 两端均未触发“大文件警告”动作（Hide/Don't show again），未改设置。

## 3. 已知差异与边界

- 实际按产品规范进入**只读预览**（前 `PreviewLimit`=2.5 MB）并标 `Read-only`；参考打开普通编辑器（可编辑）。
- 两端都不显示大文件警告：实际因设置 `largeFileWarningDismissed` 已持久化，参考也有对应的“不再询问/不显示”状态；因此本配对**只覆盖“警告已关闭”的大文件预览状态**，不覆盖警告横幅本身。
- 若需要验证 Warning 横幅（“文件过大…前 2.5 MB 只读预览” + Hide/Don't show again），必须先重置该设置再用 31.4 MB 文件复测；当前实际 `largeFileWarningDismissed` 来自既有用户设置，不应擅自清除。
- 未测深色、图片大文件、扩展名放大限额。

## 3.1 第 140 批：Warning 横幅已修复可见

- 修复前该 31.4MB 页面虽为 `TextPreview`，但 Warning 横幅因 `.document-view` 网格缺少独立行被压成 13.14px、不可见。
- 第 140 批给 `.editor-notification` 加 `min-height:26px`/`box-sizing:border-box` 并新增三条 `:has(> .editor-notification)` 网格行；正常入口 PID 14720 已显示
  `Large file (29.9 MB). Showing the first 2.4 MB as a read-only preview.` + Hide Notification / Don't Show Again。
- 本页第 48 对仍记录“警告已关闭”的预览状态；横幅可见是新增的实际侧状态，参考端大文件警告仍未采，页面级仍 48。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/large-limit-pair/actual/actual-large-limit2.png`（`0B7B760C…CBC7B`）。
- 参考：`reference/reference-large-limit.png`（`91DE8873…83D9`）。

## 5. 清理

- 实际 PID 3516、参考 PID 6156 已结束；临时 `huge.txt` 删除；fixture `app.py` 仍 `B1986914…B954`、`notes.txt`/`README.md` 未变；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-153.json`；未提交、未推送。
