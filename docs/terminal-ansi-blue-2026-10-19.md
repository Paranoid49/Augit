# 终端 ANSI 蓝复核（2026-10-19）

## 1. 结论

本轮只做受控色样采集，**没有改配色**。

- 实际：在终端执行
  `Write-Host "ANSI-BLUE" -ForegroundColor Blue` 和
  `Write-Host "ANSI-DARKBLUE" -ForegroundColor DarkBlue`，
  截图中主导蓝色为 `#298ADE`（334 px），同时存在 `#005DB5`、`#5AB2FF` 等抗锯齿/混合色；
  调色板中的 `#063FDB`、`#2D61F0` 也有少量像素。
- 参考旧证据的路径/强调蓝为 `#0060AF`。
- `#298ADE` 既不等于浅色 ANSI `blue #063FDB`，也不等于 `brightBlue #2D61F0`；它更像 PowerShell 以 24-bit 颜色发出的值（或 24-bit 与抗锯齿混合），不能仅凭截图断言是 `terminal-palette.js` 的 ANSI 槽位。

## 2. 为什么不在本轮改配色

- 若是 24-bit SGR，改 xterm ANSI 调色板不会影响该颜色；
- 改调色板会影响所有普通 ANSI 输出，而当前只有一张受控色样，证据不足；
- 需要读取 xterm buffer 的 SGR/24-bit 属性，或捕获宿主 PTY 原始字节，才能判定颜色来源。

## 3. 证据

- 实际：`artifacts/pixel-followup-2026-10-19/terminal-ansi/actual-ansi.png`（`27B4C19B…F60C`）。
- 浅色终端调色板：`web/src/terminal-palette.js`（`blue #063FDB`、`brightBlue #2D61F0`）。
- 参考：`artifacts/pixel-followup-2026-10-18/terminal-pair-2026-10-18/reference-terminal.png`（路径蓝 `#0060AF`）。

## 4. 边界与清理

- 未改代码；页面级仍 **50**、像素仍 **0**。
- 终端 ANSI 蓝仍未闭环；不能把本轮的“主导色”直接当作调色板槽位。
- 实际 PID 3540 正常关闭；临时夹具删除；全局设置仍 `B8868855…DC3D`；未提交、未推送；`cleanup-check-185.json`。
