# 无 CDP 设置开/关当前构建样本（2026-10-18）

## 1. 方法

- 使用 `tools/audit/settings-no-cdp.cjs <out> --gdi-clock` 启动：正常 `start-augit.ps1`、强制构建、临时 settings 副本、默认 WebView2 profile；`remoteDebugging=false`、`cdpConnections=0`。
- 实际 PID **19096**；`Augit.dll` `F53764A8…`、`mockup.css` `4388373D…`、`live-data.js` `373AF4DC…`，与当前身份一致。
- 60fps gdigrab 全屏 25s FFV1 录像；通过真实鼠标点击标题栏 Settings（物理 2664,38），打开后用 `keybd_event` Esc 关闭设置窗；前台监视 `qualified=true`，允许窗口为主窗 + Settings（`allowedWindows=[6685806,16779172]`，无中断）。

## 2. 原生 trace

| 阶段 | 相对 origin |
| --- | ---: |
| controller-create-ended | 94.4ms |
| settings/read | 229.6ms |
| settings/fonts | 277.2ms |
| git/detect | 285.7ms |
| window/settings-preview | 410.7ms |
| window/settings-ready | 412.8ms |
| prepared | 426.4ms |
| window/settings-present | 487.4ms |
| capture-start→end | 523.8→615.7ms |
| uncloak-start | 644.2ms |
| **presented** | **645.0ms** |
| close-request→destroyed | 44.0ms |

- 645.0ms 落在既有无 CDP 11 窗 599.2–747.5ms 区间内；本次是第 **12** 个有效无 CDP 开窗样本（1 次），close 44.0ms。
- 窗口呈现截图：`artifacts/pixel-followup-2026-10-18/settings-current-2026-10-18/settings-visible.png`（SHA256 `ab5c69b8…6caf`）。

## 3. 输入延迟与采集负载（必须分开）

- PowerShell 记录点击时间 21:31:59.575；原生 Settings 窗口 `created` 的 UTC 21:32:02.230（差 **2.65s**），随后原生 presented 645ms。
- 这 2.65s 发生在窗口创建之前，属于 60fps gdigrab/点击 helper 的输入送达/负载延迟，**不是设置窗口原生呈现时间**；不能把视频中的点击→可见墙钟当作产品 645ms 之外的额外阻塞。
- 录像 25s、262,134,911 字节，全窗口 FFV1；本次未做整帧近白扫描，不宣称无白闪；只保留可见截图和原生呈现证据。

### 3.1 设置窗局部帧扫描（补）

- 从录像 8.5–11.5s 按 60fps 裁出设置矩形 `[530,193,1820,1330]`，180 帧 455×333 JPEG；设置内容在 **9.30s**（第 49 帧）出现（dark 像素 144→232，white 0.771→0.909），之前是主窗；**没有 white>0.98 的整块白帧**。
- 第 103–105 帧（名义 10.20–10.23s，约 50ms）设置内容消失，仅剩 chrome `#E9EAEE`（median 233,234,238；唯一色 208），随后第 106 帧恢复；这不是初始白闪，而是**内容出现约 0.9s 后的短暂空白**。
- 对同时间点重取全帧（`full-0103.png`）显示内容仍在，存在 seek/时间戳口径差异；因此该 3 帧空白是**候选**：可能为 WebView2 重绘空白，也可能是 gdigrab/时间戳采集伪影。**不登记为已确认产品缺陷，也不宣称无闪**；需要第二次独立录像确认。
- 证据：`frame-analysis.txt`、`frames-settings/f_0049.jpg`、`frames-settings/f_0103.jpg`、`full-0103.png`。

### 3.2 既有 repeat 录像交叉核对

- 复用 `settings-no-cdp-repeat`（运行 10：同一进程两次设置开/关，前台门禁合格，60fps gdigrab）。
- 两次设置窗区间分别为 4.78–10.74s、17.66–23.53s；对两次开窗后的前 2.2s 按 60fps 裁设置矩形检查：
  - run10a：132 帧，无 dark<100 的空白帧；
  - run10b：134 帧，无空白帧。
- 因此本批 3 帧内容空白**未在既有两次独立设置开窗中复现**；更可能是 gdigrab/时间戳/一次性采集伪影，而不是可复现的产品闪屏。**仍不宣称无闪**，也不登记为产品缺陷。

### 3.3 ddagrab 独立采集交叉核对

- 用 `settings-no-cdp.cjs` 默认 **ddagrab**（桌面复制，60fps）再做一窗：PID 6068，无 CDP，`Augit.dll`/web hashes 同一当前身份；前台 `qualified=true`；25s、676,003,115 字节 FFV1。
- 原生 trace：presented **808.0ms**，close-request→destroyed **66.6ms**；点击日志到窗口创建的输入送达延迟约 2.74s，仍属采集/输入 helper，不计产品。该窗是第 **13** 个有效无 CDP 开窗样本（ddagrab 负载样本，808ms 高于既有 599–747ms，不替代正常证据）。
- 设置窗局部（10.0–12.5s，151 帧 60fps）：内容在 **10.08s** 出现；**dark<100 的空白帧 0**；无整块白帧。
- 综合：gdi 样本的 3 帧空白在既有 repeat 两次开窗（gdi）与本次 ddagrab 151 帧均未复现，判为 **gdi 采集/时间戳伪影**，不登记产品闪屏；ddagrab 的 808ms 是采集负载下样本，不替代 645ms/599–747ms 正常证据，也不宣称全局无闪。
- 证据：`settings-current-dd-2026-10-18/`（`cycle-screen.mkv`、`native-trace`、`settings-visible.png`、`frames-settings/`、`frame-analysis.txt`）。

## 4. 清理与限制

- Settings 通过 Esc 正常关闭，原生 close 44.0ms；主 Augit 由测试脚本 finally 的 PID 清理结束，**不是正常主窗关闭**，不计入正常关窗样本。
- 临时 settings 副本已删、临时目录删除；实际 `settings.json` 仍 `B8868855…DC3D`；无 Augit/pycharm64 残留、无 CDP 端口。
- 单次样本，不替代 30 窗/11 窗历史证据，不外推所有 DPI/深色/慢开/白闪；录像保留供后续帧分析。

## 5. 证据

- `artifacts/pixel-followup-2026-10-18/settings-current-2026-10-18/`：`launch.json`、`native-trace/*-1/2.json`、`cycle-screen.mkv`（262MB）、`settings-visible.png`、`cycle-screen.json`、`cleanup.json`。
- `cleanup-check-67.json`；未提交、未推送。
