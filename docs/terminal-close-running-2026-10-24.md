# 关闭运行中终端：Stop Command and Close 的真实终止（2026-10-24）

> 结论：真实外壳中运行 `ping -t 127.0.0.1`，关闭 `Local` 标签出现确认框；点击
> `Stop Command and Close` 后确认框和终端工具关闭，`ping.exe` 子进程消失。
> 这是行为证据，不是参考像素；“Don't ask again”和参考文案差异仍未覆盖。

## 1. 方法

- 临时 Git 仓库 + 隔离设置；`start-augit.ps1 -SkipBuild` 正常启动。
- 真实 Win32 点击 Terminal 轨道 → 点击终端正文 → 剪贴板粘贴 `ping -t 127.0.0.1` + Enter。
- 通过 `Win32_Process` 父链确认 `ping.exe` 属于本批 Augit 实例。
- 点击 `Local` 标签关闭 → 等待 `Close Terminal` 确认框 → 点击 `Stop Command and Close`。
- 等待确认框/终端工具消失，并轮询 `ping.exe` 直到消失。
- CDP 只读 DOM/几何和截图。点击使用脚本内嵌的 **DPI-aware** 物理坐标点击：
  `ClientToScreen + GetDpiForWindow`，避免 `click-window.ps1` 在 168 DPI 下把逻辑坐标
  直接交给 `SetCursorPos` 的偏移（本轮第一次尝试因此点空，已清理后重跑）。

## 2. 结果

| 项 | 值 |
| --- | --- |
| Terminal 标签 | `Local` |
| 命令 | `ping -t 127.0.0.1` |
| 运行中的 ping | PID 26408（父 30848） |
| 关闭确认 | 标题 `Close Terminal`；正文 `A command is still running in the terminal` / `Continuing stops the foreground command, shell, and all child processes.`；按钮 `Keep Terminal` / `Stop Command and Close` |
| 点击 Stop | screen `1736,1089`，UTC 19:57:51.337 |
| 点击后 | 确认框消失、`.terminal-tool` 消失、`.terminal-session` 为空 |
| ping 子进程 | **null**（已终止） |
| 实例 | PID 27012 正常关闭，`pidAlive=false` |
| 截图 | `artifacts/terminal-close-running-2026-10-24b/terminal-running.png`、`terminal-close-confirm.png`、`terminal-closed.png` |
| 报告 | `terminal-close-running.json` |

## 3. 已知差异与边界

- 实际确认框文案/按钮与 PyCharm 参考不同：参考 `Process 'Terminal Local' Is Running`、
  `Terminate`/`Cancel` + `Don't ask again`；Augit 为 `Keep Terminal` / `Stop Command and Close`。
  本批只验证 Stop 的真实终止，不把文案差异改写成通过。
- 未测 `Keep Terminal` 后 ping 继续、`Don't ask again` 持久化、多标签/多终端、
  非 ping 子进程或 PowerShell 内嵌套子进程。
- 未采参考像素；终端逐行逐色/选区复制仍是独立缺口。
- 第一次尝试用旧点击辅助器点空，确认框保持、ping 仍运行；关闭应用和 ping 后清理临时目录，
  该失败不写成产品缺陷。

## 4. 复验

```powershell
node tools/audit/terminal-close-running.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/terminal-close-running-2026-10-24b
```

- 脚本正常关闭应用、删除临时仓库；无 ping/Augit 残留。
