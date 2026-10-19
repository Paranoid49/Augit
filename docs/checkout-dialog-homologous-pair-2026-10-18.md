# Checkout Tag or Revision 对话框同态配对（2026-10-18）

## 1. 结论

完成 `Checkout Tag or Revision` 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：标题区、关闭入口、单个引用输入框、空值初始态、主操作/取消；同一 fixture `main`/`e9188bf`；浅色、DPI168。
- 实际：标题 `Checkout Tag or Revision`；标签 `Tag or revision`；按钮 `Cancel`（左）/`Checkout`（右）；无标题图标；窗口约 700×287 物理。
- 参考：标题 `Checkout`；标签 `Enter reference (branch, tag) name or commit hash:`；按钮 `OK`（左）/`Cancel`（右）；带 PyCharm 标题图标；窗口约 564×270 物理。
- 页面级同态配对 **10 → 11**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 11412：分支弹层 → `Checkout Tag or Revision…`；打开原生对话框，未执行 Checkout。
- 参考 PID 23896：Find Action `Checkout Tag or Revision` → 打开 `Checkout` 对话框，未执行 OK。
- 两次都只到空输入初始态；未写入 Git。

## 3. 已知差异

- 标题/标签文案、按钮文字与左右顺序、标题图标。
- 窗口外框尺寸、输入框宽度、按钮宽度/间距。
- 未测：输入预填/自动补全、非法引用错误、Checkout 成功/失败、分离 HEAD、未提交改动提示、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/new-branch-pair/actual/actual-new-branch-dialog.png`（`0A38286F…CB90`；文件名沿用取证目录，内容为 Checkout 对话框）。
- 参考：`reference/reference-checkout-dialog.png`（`15387C1D…B727`）。

## 5. 清理

- 实际 PID 11412、参考 PID 23896 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；fixture 未产生分支/提交改动；未触碰用户原有 PyCharm；无端口/应用残留。
- 本批未修改代码；`cleanup-check-79.json`；未提交、未推送。
