# Define Remote 对话框同态配对（2026-10-18）

## 1. 结论

完成新建远端表单取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：`Define Remote` 标题、`Name: origin`、空 `URL:`、勾选 `Fetch remote`、`OK`/`Cancel`；均**未创建远端**；浅色、DPI168。
- 实际：A 品牌图标；**多一个 `Push URL:` 字段**（用户已批准保留 Push URL）；窗口约 709×449。
- 参考：PyCharm 图标；无 `Push URL`；窗口约 710×363。
- 页面级同态配对 **27 → 28**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 16620：Push → `Define remote` → Git Remotes → `+` → Define Remote；Cancel/关闭未创建。
- 参考 PID 5976：Find Action `Manage Remotes` → Git Remotes → `+` → Define Remote；未创建。

## 3. 已知差异与边界

- `Push URL` 为产品批准保留的额外字段；参考无此字段，属**已知产品差异**，不为了凑图删除。
- 标题图标品牌例外；高度 449 vs 363（Push URL 行造成）；局部间距/字体抗锯齿。
- 未测：URL/Push URL 校验、Fetch remote 取消、成功创建、编辑/删除、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/new-remote-pair/actual/actual-new-remote.png`（`C9C17943…DAAF`）。
- 参考：`reference/reference-define-remote.png`（`3D49B38F…D88B`）。

## 5. 清理

- 实际 PID 16620、参考 PID 5976 已结束；`settings.json` 恢复 `B8868855…DC3D`；未写 Git；fixture 未变；`cleanup-check-109.json`；未提交、未推送。
