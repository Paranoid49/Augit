# Settings/Appearance 同态配对（2026-10-18）

## 1. 结论

完成设置 `Appearance` 页两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Settings 的 `Appearance & Behavior > Appearance` 页；Light 主题；UI 字体 `Microsoft YaHei UI`、Size 12；OK/Cancel/**Apply 禁用**（未修改设置、未保存）。
- 实际：标题 `Settings — Augit`；1820×1330；左栏 Appearance & Behavior/Appearance/File View/Git/Terminal；Theme `Light`；UI font/Size；`Restore Reference Appearance`；OK/Cancel/Apply。
- 参考：标题 `Settings – notes.txt`；1772×1215；完整 IDE 设置树；Theme `Islands Light`；Editor color scheme；`Use custom font` Microsoft YaHei UI 12；Accessibility/UI Options；OK/Cancel/Apply 禁用。
- 页面级同态配对 **34 → 35**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 21584：主窗右上齿轮 → Settings；默认落在 Appearance。
- 参考 PID 5468：Ctrl+Alt+S → Settings；左侧选中 Appearance。
- 两端均未点 OK/Apply；取证后关闭。
- 本页只证明页面/控件呈现，不代表设置慢开、白闪或关闭性能已验收；性能仍按 `settings-no-cdp`/`settings-capture-stall` 等单独证据。

## 3. 已知差异与边界

- 实际是轻量自定义设置页（Appearance/File View/Git/Terminal），参考是完整 IDE 设置；主题名/描述/布局/尺寸不同。
- `Restore Reference Appearance` 是实际专有恢复入口；参考无。
- 未测：File View/Git/Terminal 页面、深色、修改并保存/取消、不同 DPI。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/settings-appearance-pair/actual/actual-settings.png`（`F7AE7F37…9220`）。
- 参考：`reference/reference-settings.png`（`8FC4C009…7E84`）。

## 5. 清理

- 实际 PID 21584、参考 PID 5468 已结束；`settings.json` 恢复 `B8868855…DC3D`；未保存；`cleanup-check-121.json`；未提交、未推送。
