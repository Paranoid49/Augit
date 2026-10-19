# Appearance UI 字号保存态（2026-10-18）

## 1. 结论

在 Appearance 页用 UIA 聚焦 + 真实按键把 UI 字号 12→13 并 Apply，**实际端保存成功**；参考端未采，登记为**实际侧状态证据**，不构成配对，页面级仍 48。

- 实际 PID 24232：`ui-font-size` 12 → 13；Apply 由禁用转可用并执行成功。
- `settings.json`：`textFontSize=13`、`fontSize=13`（等宽字号仍固定 13），文件 SHA256 `FF06C052…0F11`；Appearance 页 Size 13、Apply 禁用（证据 `09EC73C0…9822`）。
- 取证后恢复设置基线 `B8868855…DC3D`（`textFontSize=12`）。
- 参考端未启动；参考 Appearance 的 UI 字体/字号设置未采集。

## 2. 方法

- 点击设置齿轮打开 Settings → Appearance。
- UIA 找到 `ui-font-size`（Spinner），`SetFocus()` 后用真实 `SendKeys('^a')`、`SendKeys('13')` 修改；Apply 变可用后 `InvokePattern.Invoke()`。
- 这解决了此前“点击输入框未聚焦、Ctrl+A 选中整页文字”的问题；UIA `SetValue` 单独使用不会触发页面草稿，必须配合真实按键。

## 3. 边界

- 等宽字号仍按 2026-10-15 用户裁决固定 `JetBrains Mono 13`，本项只验证 UI 字号。
- 参考端 UI 字体页/保存态未采，不能宣称 Appearance 保存已同态配对。
- 设置保存后的整体字号布局（标签换行、行高等）只截了设置页，未重新全页复验。

## 4. 清理

- 实际 PID 24232 已结束；`settings.json` 恢复 `B8868855…DC3D`；fixture 未变；无 Augit/pycharm64 残留。
- 本批未修改代码；`cleanup-check-159.json`；未提交、未推送。
