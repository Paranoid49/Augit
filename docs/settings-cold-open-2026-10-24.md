# Settings 冷开与边界关闭（2026-10-24，当前构建）

> 状态：**补到 3 个全新进程的冷开样本与 Esc / Cancel / 标题关闭三条边界路径；有 1 次点击→presented 为 567.2 ms，冷开 500 ms 目标仍未连续达成。**
> 本文只覆盖 Settings 生命周期与首内容，不是 Settings 页面像素验收；真实 500 ms `CapturePreview` 超时仍未自然触发。

## 1. 构建与入口

- 构建：`tools/start-augit.ps1 -PrepareOnly`，ProductVersion
  `0.1.0+faeb0a1572b27f0488db067d14b4925c847e2361`（exe 与 dll 同）。
- 每轮使用全新进程，启动参数与启动器一致：
  `Augit.exe --web-root D:\github\Augit\web --workspace <隔离工作区> --settings-file <本轮隔离设置>`。
  无 CDP、无 `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`、无场景参数。
- 隔离设置从当前全局配置复制；每轮把 `lastWorkspace` 指向隔离工作区、清空恢复标签；
  全局设置 SHA256 批前/批后均为 `8840E7754E244F67B266E300AAB0142F7907E2C30FE6D0DB772C77F9962462CF`。
- 设置入口取主窗 `titlebar [aria-label="Settings"]` 实测矩形中心逻辑 `(1522,22)`；
  真实 Win32 `SetCursorPos`/`mouse_event` 单击。窗口临时置 topmost，因为前台被最大化的 Edge 覆盖；
  每轮设置窗都在点击后出现，未把坐标失败样本混入结果。
- presented 判定：本实例顶层 `Settings` 窗口 `IsWindowVisible` 且
  `DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED=14) == 0`，2 ms 轮询。
- 原生阶段由 `AUGIT_SETTINGS_TRACE_DIRECTORY` 导出，阶段时间戳与 UTC 锚点成对解析；
  点击→presented 同时记录脚本 UTC 与原生 `presented` UTC，二者差 12–20 ms（轮询/脚本开销）。

## 2. 结果

### 2.1 冷开

| 轮次 | PID | 点击→presented | 原生 `created`→`presented` | `CapturePreview` | presented 截图 | +60 ms 同图 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 988 | **491.4 ms** | 411.2 ms | 159.0 ms | 完整 Appearance 页 | 否 |
| 2 | 27240 | **451.1 ms** | 371.0 ms | 89.9 ms | 完整 Appearance 页 | 否 |
| 3 | 25524 | **567.2 ms** | 469.4 ms | 88.5 ms | 完整 Appearance 页 | 是 |

- 三轮都是全新进程；`created`→`presented` 为 371–469 ms，说明冷开的主要耗时仍在 WebView2
  设置页初始化、控制器与捕获，而不是点击轮询。
- 第 3 轮点击→presented **567.2 ms**，冷开 500 ms 目标仍未连续达成；不能拿前两轮平均值写成通过。
- presented 抓图三张都是完整 Appearance 页，不是主窗残影或空白页；没有抓 presented 前的整帧，
  因此不能据此证明“无白闪”。`sameAt60=false` 的两轮差异含搜索框焦点/光标，不是页面缺失。
- 第 2 轮在点击后 15 ms 发送 Tab（首内容前输入），设置窗仍正常出现，未出现重复窗或崩溃；
  该输入是否被页面消费未做进一步断言。

### 2.2 边界关闭

| 路径 | 请求→`destroyed`（原生） | 关闭结果 | 备注 |
| --- | --- | --- | --- |
| Esc | 53.7 ms | 设置窗消失，主窗随后正常退出 | 先真实点击设置内容使窗口 active，再 `keybd_event` Esc |
| Cancel 点击（逻辑 900,719） | 29.1 ms | 设置窗消失，主窗正常退出 | 真实鼠标 |
| 标题 WM_CLOSE | 89.7 ms | 设置窗消失，主窗正常退出 | 与标题关闭同一消息路径 |

- 三条路径都是**设置窗自己的原生 close**；不是先关主窗连带销毁。
- 每轮主窗都用 `WM_CLOSE` 正常退出；脚本结束后 `Get-Process Augit` 无残留。
- 脚本把 `closeMs` 记为“请求时刻→原生 destroyed”，包含发送后的输入/排队时间；
  `close-start`→`destroyed` 本身为 29.1–89.7 ms（同一数值在本批 close 路径中两者接近）。

## 3. 未覆盖

- 真实 500 ms `CapturePreview` 超时：本批 capture 最高 159 ms，未自然触发；仍只有单测覆盖。
- 2904 ms 慢开样本未复现；本批 3 轮不能证明慢样本不存在。
- 首内容前输入的完整行为（焦点、草稿、事件丢失）未做逐项断言；只证明不崩溃/不重复窗。
- 深色、窄窗、其他 DPI、多显示器未在本批冷开中覆盖；深色稳定态已有 2026-10-22 证据。
- presented 前后没有 60fps 全窗视频，不能替代白闪/透明帧验收；`sameAt60` 只是窗口抓图哈希。

## 4. 证据与复验

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  D:/github/Augit/tools/audit/settings-cold-open.ps1 `
  -Runs 3 -OutDir D:/github/Augit/artifacts/settings-cold-open-2026-10-24c
```

- `artifacts/settings-cold-open-2026-10-24c/settings-cold-open.json`：逐轮 PID、点击/presented UTC、
  原生阶段、截图 SHA256、关闭路径与结果。
- 同目录 `run{1..3}-presented.png` / `run{1..3}-plus60.png`；`native-trace/*.json`。
- 本批 PID 988 / 27240 / 25524 均由脚本 `WM_CLOSE` 正常关闭；无残留进程、无监听端口、
  无临时目录（隔离设置随输出目录保留作证据）。
