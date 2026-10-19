# Git Remotes 对话框同态配对（2026-10-18）

## 1. 结论

完成无远端状态 Git Remotes 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：`Git Remotes` 标题、`+`/`-`/铅笔工具栏、`Name`/`URL` 两列表头、`Nothing to show` 空态、`OK`、无远端；两侧窗口均约 **624×385**、浅色、DPI168；均**未写 Git**。
- 实际：标题图标为 Augit `A` 品牌标记；参考为 PyCharm 产品图标（品牌例外）。
- 其余内容、空态文案、工具栏、表头、按钮位置/尺寸高度接近；仍不据此宣称像素一致。
- 页面级同态配对 **25 → 26**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 21596：Git 菜单 → Push → Push 对话框 `Define remote` → Git Remotes；OK/关闭未创建远端。
- 参考 PID 23292：Find Action `Manage Remotes` → Git Remotes；OK/关闭未创建远端。

## 3. 已知差异与边界

- 标题图标品牌例外（实际 A / 参考 PyCharm）。
- 字体抗锯齿、表头/空态基线、按钮内距等局部像素未做同坐标差分。
- 未测：新建/编辑/删除远端、URL 校验、多远端、深色、不同 DPI。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/define-remote-pair/actual/actual-define-remote.png`（`42EB13E6…467C`）。
- 参考：`reference/reference-remotes.png`（`FA53B74C…5A84`）。

## 5. 清理

- 实际 PID 21596、参考 PID 23292 已结束；`settings.json` 恢复 `B8868855…DC3D`；未写 Git；fixture 未变；`cleanup-check-107.json`；未提交、未推送。
