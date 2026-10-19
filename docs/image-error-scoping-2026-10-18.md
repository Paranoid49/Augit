# 损坏图片预览取证（2026-10-18）

> **第 76 批更正**：本页 `broken.png` 是 ASCII 文本，实际按内容签名分类为 Text，不是“无效图片回退文本”的缺口；用带 PNG 签名的 `corrupt.png` 已取得错误页并登记为 [损坏图片错误页同态配对](image-error-homologous-pair-2026-10-18.md)（页面级 22）。本页保留首次误判与更正过程。

## 1. 结论

损坏 PNG 的预览行为在两端不同，**未建立同态配对**，页面级仍 21：

- 实际 PID 13684：打开临时 `broken.png`（14 字节无效内容）后，标签 `broken.png` 按**文本**打开，正文显示 `not a real png`，状态 `UTF-8 / No line endings / Read-only`；没有出现图片错误页。
- 参考 PID 10096：同一文件按**图片**标签打开，正文显示红色错误图标 + `Image not loaded` / `Try to open it externally to fix format problem`，带图片工具条；无文本内容。
- 差异性质：实际对无效图片**回退为文本预览**，参考进入图片错误空态；这是**功能/状态缺口**（`image-error` 视觉页在实时入口未到达），不是同态配对。

## 2. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/image-error-pair/actual/actual-image-error2.png`（`92CD9FE2…3595`）。
- 参考：`reference/reference-image-error2.png`（`501E9C9A…3409`）；参考首次 F5 误触发 Copy 对话框（诊断），未计入。
- 临时文件 `broken.png` 已删除；fixture 恢复 `app.py` 修改 + `.idea/` 未跟踪，无残留。

## 3. 边界与后续

- 未测：有效图片、超大图片、其他图片格式、外部修改后的状态同步、深色。
- 后续应核对产品规格 §图像/不可预览文件的错误态要求，决定是否为无效图片增加错误页而不是文本回退；不把本次取证当作页面配对。

## 4. 清理

- 实际 PID 13684、参考 PID 10096 已结束；`settings.json` 恢复 `B8868855…DC3D`；`broken.png` 已删除；无 Augit/pycharm64 残留；无 CDP 端口。
- 本批未修改代码；`cleanup-check-100.json`；未提交、未推送。
