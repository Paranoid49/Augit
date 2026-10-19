# 大文件限制取证尝试（2026-10-18）

## 1. 结论

在 fixture 临时生成 4.19 MB `large.txt`，实际端未能稳定打开大文件限制页，**未建立配对**，页面级仍 46。

- 临时文件：`large.txt` 60,000 行，4,188,893 字节；fixture 原有 `app.py`/`notes.txt`/`README.md`。
- 实际 PID 22768：Project 树新增 `large.txt`；前两次双击分别落到 `README.md`/`app.py`（行坐标错误），修正到物理 `150,276` 后编辑器仍停在前一文档，**未捕获“文件过大”页面或 toast**（可能在 2.5s 前消失）。
- 参考端未启动；未建立配对。

## 1.1 Quick Open 重试（第 124 批）

- 实际 PID 22420：点击编辑器 → Ctrl+P → 输入 `large` → Enter；`large.txt` 成功在标签打开，`Read-only`，显示第 0–23 行等正文，**4,188,893 字节无“文件过大”限制/提示**。证据 `CF2DD230…73E3`。
- 参考 PID 12328：Ctrl+Shift+N 打开 Search Everywhere（Files 标签），输入 `large` 显示“Use Find in Files…”，未列出 `large.txt`（新建文件索引未更新），未打开/未出现大文件警告。证据 `BF721FB6…A7DA`。
- 结论：实际可无提示打开 4.19MB 文件；参考的大文件阈值未成功取证。这不是同一页面，**不配对**；实际“无限制直接打开”与参考 2.5MB 阈值是产品差异/待确认规范。

## 2. 后续

- 先精确量取 `large.txt` 行矩形（或通过 Quick Open 打开），并在双击后 **0.5s 内**截图，确认是否有“File too large”提示/限制页。
- 若实际直接打开 4MB 文件而不提示，与参考的 2.5MB 大文件阈值存在**产品差异**，需按规范登记，不应靠坐标误判。
- 该页同时关系“不可预览文件/大文件限制”；参考端可用同一文件采大文件提示。

## 3. 清理

- 实际 PID 22768 已结束；临时 `large.txt` 删除；fixture 回到 `M app.py` / `?? .idea` 既有状态；`settings.json` 恢复 `B8868855…DC3D`；无 Augit/pycharm64 残留。
- 本批未修改代码；`cleanup-check-144.json`；未提交、未推送。
