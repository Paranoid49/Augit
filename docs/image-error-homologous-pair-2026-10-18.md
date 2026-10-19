# 损坏图片错误页同态配对（2026-10-18）

## 1. 结论

用**签名有效但内容损坏**的 PNG 取得两端图片错误页，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 `corrupt.png`（PNG 签名 + 垃圾字节，15 B）、标签 `corrupt.png`、图片错误空态、不能预览、提示外部打开；均**未写 Git**；浅色、DPI168。
- 实际：`This file cannot be previewed in Augit`；`corrupt.png · PNG image · 15 B`；`corrupt.png`；`Cannot read image dimensions; preview stopped.`；`Open in Default Application`；状态 `Read-only`。
- 参考：`Image not loaded` / `Try to open it externally to fix format problem`；图片工具栏（适应/缩放等）；无文件大小/按钮/状态。
- 页面级同态配对 **21 → 22**；像素合格仍 **0**。

## 2. 更正上一轮判断

- 第 74 批的 `broken.png` 是 ASCII 文本，`DocumentClassifier` 按**内容签名**分类为 Text（`DocumentClassifier.cs:13-60`），因此实际按文本打开是**内容分类的既定行为**，不是“无效图片回退文本”的功能缺口。
- 参考按扩展名把 `.png` 当图片，故显示图片错误；这是**分类口径差异**，不是缺陷。
- 本次 `corrupt.png` 带 PNG 签名，实际分类为 Png，尺寸读取失败后进入错误页；参考同样显示图片错误，构成配对。

## 3. 取证路径与已知差异

- 实际正常入口 PID 12588：Project 树双击 `corrupt.png`；内容签名识别为 PNG，尺寸读取失败 → 错误页。
- 参考 PID 24048：同一文件双击；扩展名识别为图片，解码失败 → `Image not loaded`。
- 差异：实际强调文件元数据 + `Open in Default Application`；参考只有错误提示 + 图片工具栏；文案/图标/状态栏/布局不同。
- 未测：有效图片、其他格式、超大图片、外部修改同步、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/image-error-pair/actual/actual-corrupt-error2.png`（`5C6C8A89…5C45`）。
- 参考：`reference/reference-corrupt-error.png`（`8998A045…91DD`）。

## 5. 清理

- 实际 PID 12588、参考 PID 24048 已结束；`settings.json` 恢复 `B8868855…DC3D`；临时 `corrupt.png` 已删除；fixture 恢复 `app.py` 修改 + `.idea/` 未跟踪；`cleanup-check-101.json`；未提交、未推送。
