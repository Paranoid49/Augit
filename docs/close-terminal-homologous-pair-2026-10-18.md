# 关闭运行中终端同态配对（2026-10-18）

## 1. 结论

完成运行中终端关闭确认的两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一 fixture；终端标签 `Local`、Windows PowerShell；运行 `ping -t 127.0.0.1` 并持续输出；关闭标签出现确认；均未终止进程、未写文件。
- 实际：`Close Terminal`；标题“A command is still running in the terminal”；说明“Continuing stops the foreground command, shell, and all child processes.”；`Keep Terminal` / `Stop Command and Close`（红色）；约 1085×508。
- 参考：警告图标；`Process 'Terminal Local' Is Running`；“Do you want to terminate the process 'Terminal Local'?”；`Don't ask again`；`Terminate`（蓝色）/`Cancel`；约 647×327。
- 页面级同态配对 **45 → 46**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 11736：正常入口 → Terminal（轨道物理约 `32,1546`）→ 点击终端正文获得焦点 → `ping -t 127.0.0.1` + Enter → 点击 `Local` 标签关闭 → 确认框。
- 参考 PID 4448：Alt+F12 打开 Terminal → 点击终端正文 → 同一 ping → 点击 `Local` 标签关闭 → 确认框。
- 两端都保持确认框未操作；取证后关闭应用，ping 子进程随之结束。

## 3. 已知差异与边界

- 实际文案偏行为说明，按钮 `Keep Terminal` / `Stop Command and Close`；参考 `Terminate`/`Cancel` + `Don't ask again`。
- 两端的“关闭标签”入口与确认框尺寸/图标不同；未测“Stop/Terminate”后的实际终止与“Don't ask again”持久化。
- 这是“关闭运行中终端”页面；不等同于终端整页内容同态（终端已有独立配对/状态证据）。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/close-terminal-pair/actual/actual-close-confirm.png`（`A2A7565D…C824`）。
- 参考：`reference/reference-close-confirm.png`（`D1278108…5A3A`）。

## 5. 清理

- 实际 PID 11736、参考 PID 4448 已结束；无 ping 残留；`settings.json` 恢复 `B8868855…DC3D`；fixture 未变；`cleanup-check-143.json`；未提交、未推送。
