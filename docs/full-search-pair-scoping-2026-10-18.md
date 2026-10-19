# 全仓搜索状态取证（2026-10-18）

## 1. 结论

完成实际端全仓搜索取证；参考端 Find in Files 未能可靠驱动，**未建立同态配对**。

- 实际：`Ctrl+Shift+F` → **Find in Files 紧凑浮层**，查询 `Ag`，结果包括 `notes.txt:1` 与 `.idea` 的 xml 行；顶部有 Cc/W/.*/Include ignored。
- 参考：`Ctrl+Shift+F` 先把 `Ag` 输入到 `notes.txt` 编辑区（未保存，已撤销）；改用 Find Action “Find in Files” 打开了一个独立 transient 对话框，但后续输入未进入其搜索框，未捕获到参考结果。参考 Find in Files 的焦点/对话框控制未建立，**不能据此判产品差异**。
- 页面级同态配对仍 **5**，像素合格仍 **0**。

## 2. 证据

- 实际：正常入口 PID 23904；`artifacts/pixel-followup-2026-10-18/full-search-pair/actual/actual-full-search.png`（`b682835e…3db4`）。
- 参考尝试：`reference-full-search.png`（Ag 落到编辑器）、`reference-find-in-files-main.png`（Ag 未保存）、`reference-find-in-files-dialog.png`（未捕获）、`reference-notes-undo2.png`（撤销后 line 4 恢复空白）。
- `notes.txt` 磁盘哈希 `DB680844…B612` 未变；参考未保存。

## 3. 差异与边界

- 实际是**紧凑浮层 + 结果列表**；参考 PyCharm 的 Find in Files 预期是独立搜索对话框/结果树，结构不同；本次没有参考结果，不能做同态配对。
- 未测：空查询/空结果、范围、匹配数量、文件分组、双击打开、大小写/全字/正则、深色、超时/截断。
- 参考的 Find in Files 快捷键/对话框焦点在本次自动化中不可靠；后续可用菜单或改键重新取证，不把工具限制写成产品缺陷。

## 4. 清理

- 实际 PID 23904、参考 PID 17516 正常关闭；实际设置恢复 `B8868855…DC3D`；参考 notes.txt 磁盘未变；未触碰用户原有 PyCharm；无端口/应用残留。
- `cleanup-check-64.json`；未提交、未推送。
