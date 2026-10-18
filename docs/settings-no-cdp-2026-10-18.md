# 无 CDP 设置开窗复核（2026-10-18）

## 目的与方法

补当前构建的**无远程调试**设置操作证据，区分“宿主原生阶段”和“自动化能否把输入送达 WebView2”。全程使用 `tools/start-augit.ps1` 强制非增量构建、真实设置副本、默认 WebView2 profile；不设置 `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`，`remoteDebugging=false`、`cdpConnections=0`。

输入由 `tools/audit/click-window.ps1` 发送真实系统鼠标：逻辑 `(1522,22)` → 物理 `(2664,38)`；Settings 顶层窗口由 `EnumWindows` 按本实例 PID 和标题识别。开窗后尝试 Esc、Cancel 点击或 WM_CLOSE，成功后正常关闭主窗；失败时只结束本批核实的 PID。五次运行实际映射均为 `Augit.dll` SHA256 `159123F2…EA8559D`、`Augit.Infrastructure.dll` `A92BBE4C…D42894CE`。

## 开窗结果

| 运行 | PID | 原生 presented | 控制器创建 | 字体/主题间隔 | 捕获异步等待 | 前台门禁 | 开窗证据 |
| --- | ---: | ---: | ---: | ---: | ---: | --- | --- |
| 1 | 22304 | 599.2ms | 82.3ms | 71.7ms | 92.1ms | 通过 | Settings 顶层窗口出现，native trace 完整 |
| 2 | 15100 | 730.9ms | 119.0ms | 126.6ms | 92.5ms | 通过 | Settings 顶层窗口出现，native trace 完整 |
| 3 | 15060 | 656.6ms | 82.9ms | 104.5ms | 103.0ms | 失败 | 后续置前/点击改变前台，视频不作视觉通过 |
| 4 | 24736 | 747.5ms | 114.3ms | 123.7ms | 101.8ms | 失败 | 同上，仅保留诊断帧 |
| 5 | 22564 | — | — | — | — | — | 启动后在截图前已退出，未取得 trace，登记为失败 |
| 6 | 4276 | — | — | — | — | — | 启动后在 `capture-screen-region` 前退出；`cleanup.json` 记录临时目录已删 |
| 7 | 25260 | — | — | — | — | — | `record.signal` 后约 41s 才 recording-ready，随后窗口已不在；未取得 Settings |
| 8 | 27104 | 678.3ms | 66.4ms | 120.2ms | 103.3ms | 录像失败/输入成功 | Cancel 真实鼠标点击后 Settings 关闭，主窗正常关窗；录像前景门禁 false |
| 9 | 3152 | 638.0ms | — | — | 102.7ms | 录像失败/输入成功 | 深色；Cancel 真实鼠标点击后关闭，原生 close 72.7ms；录像前景门禁 false |
| 10 | 25920 | 732.6/714.5ms | — | — | 110.2/110.5ms | 通过 | 浅色重复两次开/关；Cancel close 67.9/55.8ms；录像前景门禁 true |
| 11 | 26196 | 663.8ms | — | — | 134.8ms | 录像失败/输入成功 | Save/OK 点击关闭，原生 close 54.8ms |
| 12 | 5668 | 679.6ms | — | — | 120.7ms | 录像失败/输入成功 | 标题栏同路径 WM_CLOSE，原生 close 86.7ms |
| 14 | 26232 | 661.3ms | — | — | 139.9ms | 录像失败/输入成功 | `keybd_event` Esc 关闭，原生 close 48.3ms |

- 十一次有效开窗（运行 10 含两次）原生呈现 **599.2–747.5ms**，均未复现 2904ms；捕获异步等待 92.1–139.9ms，未触发 500ms 超时。
- 运行 5–7 的退出没有在 Application 日志留下 Application Error/Windows Error Reporting 事件；用同一份设置副本直接 `start-augit.ps1` 启动后可存活 >10s 并正常关窗。追加三次直接启动诊断（同一 exe、同一默认工作区/设置副本、无 CDP）均存活 10s 并正常关窗，未复现退出。退出原因未定位，可能是无 CDP 驱动/默认会话恢复/单实例/采集时序的交互，当前不能判为确定产品崩溃，也不能忽略。
- 慢于附 CDP 批次（339–497ms）的主要区间仍是 `git/detect → window/settings-preview`（71.7–126.6ms）和控制器创建（82.3–119.0ms），与历史无 CDP 观察一致；本批不据此断言因果。
- 两次前台合格的录像为 `settings-no-cdp-current/cycle-screen.mkv` 和 `settings-no-cdp-current-2/cycle-screen.mkv`（各约 224MB），含 Settings 可见帧；帧图 `settings-visible.png`。
- 对运行 1 的 530 帧做近白扫描：最大近白像素 3,629,455/4,942,080，达到 4,500,000 的帧为 0；未检出整帧白闪候选。该阈值不能证明设置窗局部无白块，不能外推全场景无闪。
- 运行 3/4 的录像因后续自动化置前导致前景门禁失败，已提取 `settings-visible.png` 后删除 1.38GB/1.43GB 原始失败录像，保留 `failed-video-cleanup.json`、trace、coordinator 日志和窗口列表；运行 5 无录像。

## 关闭尝试与限制

- 运行 1/2：`AppActivate('Settings — Augit')` 返回成功并发送 Esc，但 Settings 仍可见；后续 `CloseMainWindow=false`，最终按本批 PID taskkill。说明合成键盘输入没有可靠送达设置 WebView2，不能计为 Esc 关闭。
- 运行 3/4：按顶层窗口虚拟矩形换算物理坐标点击 Cancel，窗口仍未关闭；像素坐标来自录像帧，点击未送达 WebView2。不能据此判产品 Cancel 失败，也不能计为正常关闭。
- 运行 8（成功）：给 `click-window.ps1` 增加可选 `-WindowHandle`，用 `AttachThreadInput` 把输入队列接到设置窗前台线程，再在设置客户端逻辑坐标 `(900,719)` 点击 Cancel。`foreground_match=true`，Settings 顶层窗消失，主窗 `CloseMainWindow=true` 且进程退出；native trace 有 `close-request→destroyed` **59.1ms**、`controller-closed` 到 `destroyed` 0ms，临时目录删除。
- 运行 8 的 25s GDI 录像 `foreground=false`，原因是 coordinator/click helper 自身置前改变了前景身份；该录像不作为视觉合格证据，但真实系统输入、native trace 和正常关闭结果有效。
- 运行 9（深色）：同一真实点击路径，Settings 正常关闭，原生 close **72.7ms**，主窗正常关窗；录像 `foreground=false`，仅作输入/时序证据。
- 运行 10（浅色重复两次）：同一进程连续两次开/关，presented **732.6/714.5ms**，捕获等待 **110.2/110.5ms**，close **67.9/55.8ms**；录像 `foreground=true`，两次 Settings 均关闭，主窗正常关窗，临时目录删除。这是首次**前景合格的重复无 CDP 开/关**样本。
- 运行 11（Save/OK）：真实点击设置底栏 OK（逻辑 816,733），Settings 关闭，原生 close **54.8ms**；录像 `foreground=false`。
- 运行 12（标题关闭）：向 Settings 顶层窗 `PostMessage(WM_CLOSE)`，走标题栏关闭同一消息路径，Settings 关闭，原生 close **86.7ms**；录像 `foreground=false`。
- Esc 尝试（运行 13）：置前后 `SendInput` 发送 Esc 返回 **0**（未插入输入），Settings 仍可见；随后回退到真实 Cancel 点击才关闭。该次不能计为 Esc 关闭，只证明该 `SendInput` 调用路径被系统拒绝/未送达。
- Esc 成功（运行 14）：同一前台设置窗改用 `keybd_event(VK_ESCAPE)`，Settings 正常关闭，原生 `close-request→destroyed` **48.3ms**；录像 `foreground=false`。因此无 CDP Esc 已取得 1 次成功样本，运行 13 的失败是 `SendInput` 路径限制，不是产品 Esc 失败。
- 目前无 CDP 正常关闭已覆盖：浅色 Cancel（运行 8/10，重复两次）、深色 Cancel（运行 9）、Save/OK（运行 11）、标题 WM_CLOSE（运行 12）、Esc（运行 14）。仍未覆盖不同 DPI。
- 简化夹具周期曾记录“940×560 CSS 窄窗”，该结论已撤回：同配置正常启动截图 `artifacts/pixel-followup-2026-10-18/no-cdp-main.png` 显示窗口实际为 2880×1716、最大化，标题栏 Settings 在 1522,22；此前的 940×560 是审计脚本在 DPI 非感知进程里对 `GetClientRect` 做了二次 1.75 换算，按 816 点击自然未中。不是产品窄窗问题，也不再作为待定位阻塞。
- 因此**无 CDP 正常关闭已取得 7 次样本**：Cancel 4 次（运行 8、9、10 两次，浅色/深色/重复）、Save/OK 1 次（运行 11）、标题 WM_CLOSE 1 次（运行 12）、Esc 1 次（运行 14）；运行 10 录像前景合格。仍需不同 DPI；不能外推为无 CDP 全场景通过。附 CDP 的 30 窗批次仍提供更完整的关闭路径覆盖。
- 设置页面初始化后 `settings-present` 到 `presented` 仍可能接近 750ms；耗时包含字体/主题、控制器创建、离屏捕获和驱动观察，不是单一产品阶段。

## 证据位置与复验

- 运行 1：`artifacts/pixel-followup-2026-10-18/settings-no-cdp-current/`
- 运行 2：`artifacts/pixel-followup-2026-10-18/settings-no-cdp-current-2/`
- 成功关闭样本：`artifacts/pixel-followup-2026-10-18/settings-no-cdp-current-8/`（`cycle-screen.json`、`native-trace/*-2.json`、`coordinator.log`、`mapped-images.json`、`cleanup.json`）。
- 深色关闭：`settings-no-cdp-dark/`；浅色重复两次且前景合格：`settings-no-cdp-repeat/`；Save/OK：`settings-no-cdp-save/`；标题 WM_CLOSE：`settings-no-cdp-title/`；Esc 成功：`settings-no-cdp-esc2/`；Esc 失败/回退：`settings-no-cdp-esc/`。
- 失败/门禁批次：`settings-no-cdp-current-3/`、`-4/`、`-5/`
- 复验：`node tools/audit/settings-no-cdp.cjs <新目录> --gdi-clock`，再按现有 `record.signal` / `exit.signal` 握手发送真实鼠标输入；Cancel 使用 `tools/audit/click-window.ps1 -WindowHandle <Settings HWND> -X 900 -Y 719`，脚本 finally 会停止本批 PID 并删除临时目录。
- 本批运行 1–14 的 PID 均不存在，临时目录均已删除；未触碰用户原有 PyCharm PID 22768。
