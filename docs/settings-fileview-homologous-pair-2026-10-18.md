# Settings/File View（字体）同态配对（2026-10-18）

## 1. 结论

完成设置 `File View` 与参考 `Editor > Font` 两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：只读/编辑字体均为 `JetBrains Mono`、Size 13；均未修改、未保存；Apply 禁用。
- 实际：`Appearance & Behavior > File View`；`Monospaced font JetBrains Mono` + `Size 13`；说明“The monospaced font applies to read-only text, Diff, conflicts, and the terminal; Markdown code blocks follow it.”与“Fonts only affect display; files are not modified.”；OK/Cancel/Apply。
- 参考：`Editor > Font`；Font `JetBrains Mono`；Size `13.0`；Line height `1.2`；Enable ligatures；Typography Settings；右侧字体预览；Apply 禁用。
- 页面级同态配对 **44 → 45**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 1820：齿轮 → Settings → File View。
- 参考 PID 4268：Ctrl+Alt+S → 搜索框输入 `Font` → 左侧 `Editor > Font`。
- 两端均未修改、未点 OK/Apply。

## 3. 已知差异与边界

- 实际把等宽字体放在 `File View`；参考放在 `Editor > Font`，另有 Line height/ligatures/Typography/字体预览。
- 实际 Size 13（整数），参考 13.0；实际无行高/连字/预览。
- 未测：修改字体/字号并保存、字体不可用、深色。

## 3.1 第 136/137 批纠正

- `docs/design-system.md` 第 5 行（2026-10-15 用户裁决）规定等宽正文固定 `JetBrains Mono 13`，并**取消设置页的等宽字体/字号选择**；因此实际 File View 的只读 `Size 13` 是正确产品行为，不应加输入框。
- 第 136 批曾误加 `monospaceFontFamily` 下拉 + `codeFontSize` 输入（违反裁决），第 137 批已回退，`mockup.js` 回到 `F3D0CD53…671D`；`tools/test-file-view-code-size.mjs` 6/6 守护固定展示；正常入口 PID 4852 复验只读页。
- 参考 `Editor > Font` 可编辑是**已知产品差异**，不追求同态；页面级配对仍为第 45 对。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/settings-fileview-pair/actual/actual-settings-fileview2.png`（`E23F1CB2…F6B1`）。
- 参考：`reference/reference-settings-font3.png`（`9A895989…3905`）。

## 5. 清理

- 实际 PID 1820、参考 PID 4268 已结束；`settings.json` 恢复 `B8868855…DC3D`；未保存；`cleanup-check-140.json`；未提交、未推送。
