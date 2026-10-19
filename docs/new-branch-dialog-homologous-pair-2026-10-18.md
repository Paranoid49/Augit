# New Branch 对话框同态配对（2026-10-18）

## 1. 结论

完成 New Branch 对话框取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：模态对话框、分支名输入、Create/Cancel、关闭入口；同一 fixture `main`/`e9188bf`；浅色、DPI168；未创建分支。
- 实际：标题 `New Branch`；`Branch name` 空输入；**无 Checkout branch / Overwrite existing branch**；按钮 `Cancel`（左）/`Create`（右）；无标题图标；窗口约 700×287 物理。
- 参考：标题 `Create New Branch`；`Branch Name:` 预填 `main` 并全选；**Checkout branch 已勾选**、**Overwrite existing branch 未勾选**；按钮 `Create`（左）/`Cancel`（右）；带 PyCharm 标题图标；窗口约 745×284 物理。
- 页面级同态配对 **11 → 12**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 13316：分支弹层 → `New Branch…`；打开模态框，未 Create。
- 参考 PID 15976：Find Action `New Branch` → `Create New Branch` 模态框，未 Create。
- 实际弹层中的 New Branch 行坐标为辅助定位；先前误点到 Checkout 的尝试不计入本页。

## 3. 已知差异

- 标题文案、分支名默认值（空 vs 当前分支 `main` 全选）、按钮文字/左右顺序、标题图标。
- 实际缺少 Checkout branch / Overwrite existing branch 选项——属**功能/结构差异**，不是纯视觉。
- 窗口外部尺寸、输入框/按钮宽度与间距。
- 未测：重名/非法分支名校验、Create 成功/失败、Checkout 分支后的工作区、Overwrite、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/new-branch-dialog/actual/actual-new-branch-dialog.png`（`E51D50BF…BAD2`）。
- 参考：`reference/reference-new-branch-dialog.png`（`1B334515…E06A`）。

## 5. 清理

- 实际 PID 13316、参考 PID 15976 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；未创建分支、fixture 无 Git 改动；未触碰用户原有 PyCharm；无端口/应用残留。
- 本批未修改代码；`cleanup-check-80.json`；未提交、未推送。
