# Settings/Terminal 同态配对（2026-10-18）

## 1. 结论

完成设置 Terminal 页两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Settings 的 Terminal 页；终端字体族为 `JetBrains Mono`（实际未在本页重复显示，字体由 Appearance/终端承载）；均未保存。
- 实际：`Settings — Augit`；1820×1330；Shell `Windows PowerShell`；Custom startup command 占位 `For example, wsl.exe -d Ubuntu`；说明“Invalid configuration reports an error; the shell is not changed silently.”；**Apply 禁用、Terminal 无蓝点**（修复后；修复前曾初始可用+蓝点，见 §3/§6）；OK/Cancel/Apply。
- 参考：`Settings – notes.txt`；1772×1215；`Tools > Terminal`；Terminal engine `Reworked 2025`；Command Completion（Only for parameters、Ctrl+Space、Enter）；Project Settings（Start directory `D:\...\fixture`、Environment variables）；Font Settings JetBrains Mono 13.0 / line 1.0 / column 1.0；Apply 禁用。
- 页面级同态配对 **36 → 37**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 17164：正常入口 → 齿轮 → Settings → Terminal。
- 参考 PID 24536：Ctrl+Alt+S → 搜索 `Terminal` → `Tools > Terminal`。
- 两端均未修改、未点 OK/Apply；取证后关闭。

## 3. 已知差异与边界

- 实际是轻量 Shell/自定义启动命令页；参考是完整终端引擎/补全/字体/起始目录配置，不在产品范围，不照搬。
- **实际 Apply 初始可用 + 蓝点（已修复）**：根因是 `settingsDirtyPages()` 用 `String(null)` 与 `String("")` 比较，`terminalCustomCommand` 的 `null`（磁盘）与空输入框 `""` 被判成不同，Terminal 页一打开就脏。修复：可选文本统一把 `null/undefined` 归一为空串再比较；`tools/test-settings-dirty-null.mjs` **7/7 PASS**；正常入口 PID 18236 验证 Apply 禁用、无蓝点。
- 未测：切换 Shell、空 Custom command 报错、修改保存/取消、深色。

## 4. 证据

- 实际（修复后）：`artifacts/pixel-followup-2026-10-18/settings-terminal-page-pair/actual/actual-settings-terminal-fixed.png`（`A5B8603C…4568`）。
- 实际（修复前，仅作根因对照）：`actual/actual-settings-terminal.png`（`E966DBF3…382A`，Apply 可用+蓝点）。
- 参考：`reference/reference-settings-terminal.png`（`326E6491…214C`）。

## 5. 清理

- 实际 PID 17164（修复前）、PID 18236（修复后）、参考 PID 24536 已结束；`settings.json` 恢复 `B8868855…DC3D`；未保存；`cleanup-check-123.json`、`cleanup-check-124.json`；未提交、未推送。

## 6. 修复

- `web/src/mockup.js` 与 `docs/ux-mockups/mockup.js` 同步修改 `settingsDirtyPages()`：可选文本 `null/undefined` 与空串视为相同；数字仍按 Number 比较。
- 两份 `mockup.js` 均为 `F3D0CD53…671D`，`verify-ui-assets.ps1` PASS。
- `tools/test-settings-dirty-null.mjs` 7/7：null↔空串不脏、真实自定义命令脏、字符串/数字修改脏、数字字符串等价、缺键不脏、无 settings 安全。
- 正常入口 PID 18236 已加载并复验。
