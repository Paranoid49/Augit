# Settings 打开/关闭 cycle12：Cancel 生效（2026-10-19）

## 1. 方法

- 正常入口，PID 8356，无 CDP；
- 隔离 settings 副本；
- 等待应用加载 4s，真实 `SendInput` 点击 Settings `(2664,38)`；
- 通过 `EnumWindows` 找到标题 `Settings — Augit` 的 HWND `15008448`；
- 对该 HWND `SetForegroundWindow` 后真实点击 Cancel `(2098,1471)`；
- 时间戳均在 mouse down 前记录。

## 2. 打开

- 录像开始 `1792420784443`，点击 `1792420790408`（+5.965s）；
- 标题探针首个变化 **6.842s**；
- 打开探针约 **877ms**（6.842 - 5.965）。

这是第二个有效打开样本；前一次 cycle11 为 **636ms**。两次都只是标题探针，不是完整首内容；仍未复现 2904ms。

## 3. 关闭

- 关闭点击时间戳 `1792420795068`（+10.625s）；
- 精确帧：
  - `exact-587`（10.398s）：Settings 仍打开；
  - `exact-598`（10.595s）：Settings 已消失；
- Cancel 点击**实际生效**，设置窗关闭。

精确关闭耗时仍不能给定：首帧已关闭（10.595s）早于记录点击时间戳（10.625s），说明帧时间戳与输入时间戳存在 ≤0.23s 的对齐/编码偏差；只能确定“关闭在点击附近的一个帧区间内”，不能写成 0ms 或精确 44ms。

## 4. 证据

- `artifacts/pixel-followup-2026-10-19/settings-no-cdp-cycle12/cycle-screen.mkv`
- `frame-6.5.png`、`frame-7.png`
- `exact-587.png`（打开）、`exact-598.png`（已关闭）
- `open-click.json`、`settings-window.json`、`close-click.json`
- `onset-diagnostic.json`

## 5. 状态与清理

- 未改产品代码；页面级仍 **50**、像素仍 **0**。
- 无残留 Augit/PyCharm；临时 settings 副本由工具清理；全局设置仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-201.json`。
