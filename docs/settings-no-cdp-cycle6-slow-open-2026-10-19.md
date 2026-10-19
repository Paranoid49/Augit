# 无 CDP Settings cycle6：有效打开与慢样本（2026-10-19）

## 1. 设置

- 正常入口，PID 10924，无 CDP；
- UIA 确认 Settings 节点 rect `{2637,12,54,53}`，点击中心 `(2664,38)`；
- 录像开始时间 `1792417522536`（ms）；点击时间戳在 **输入 helper 启动前** 记录为 `1792417522573`，即录像开始后 **37ms**；
- 25s 录像、1426 帧，保留 `cycle-screen.mkv` / `cycle-screen.json`。

## 2. 观察

- 2s 帧：Settings 未出现；
- 5.35s 帧：仍未出现；
- 5.55s 帧：`Settings — Augit` 已完整显示；
- `analyze-settings-onset.py` 在标题探针 `[550,180,300,70]` 的首个变化为 **5.447s**（第 306 帧）。

因此从“录像开始 + 点击前时间戳”到设置标题出现，上界约 **5.45s**。

## 3. 边界

- 5.45s **不是纯产品首内容耗时**：时间戳在 `ui-drive` 进程启动、前台激活和真实点击之前，包含输入 helper 与录像负载；与既有 2.65–2.74s helper 差值方向一致；
- 这是一个**慢样本**，可与 2904ms 历史分支对照，但不能直接判定产品回归或修复；
- 关闭尝试：7.482s 点击 `(2097,1470)` 后 8s 帧 Settings 仍打开，关闭未生效，因此没有可用的关闭耗时；
- 需要下一次在**真实输入事件发生时**记录时间戳，或读取 helper/宿主时间线，才能扣除输入开销。

## 4. cycle7 精确点击尝试（失败，不使用）

- 为扣除 helper 开销，写了 `precise-click.ps1`：`SetForegroundWindow` → 设光标 → 记录时间戳 → `mouse_event`。
- cycle7 PID 9272；录像开始 `1792417781438`，点击记录 `1792417783730`（+2.292s）。
- 但 3.2s / 4.5s 帧仍是主窗，Settings 从未打开；`onset` 的 8.538s 不是设置标题。
- 因此 cycle7 **不是有效样本**，不能用来算延迟；自动精确点击与 UIA/手动点击的命中差异仍需排查。

## 5. cycle8/9 UIA Invoke 尝试（失败，不使用）

- 独立手动测试中，用 `AutomationElement.FromHandle` + `InvokePattern.Invoke` 可稳定打开 Settings。
- 但在 `settings-no-cdp.cjs` 启动的录像实例里，`FromHandle` 的 descendants 中找不到 `Settings`（cycle8 PID 21032、cycle9 PID 16860，均抛 `Settings element not found`）。
- 可能原因：录像开始时 WebView/UIA 树尚未就绪，或记录器/前台监视改变了窗口句柄；未继续猜。
- cycle8/9 均未打开设置窗，不能作为延迟样本；cycle9 的 `cycle-screen.mkv` 为 0 字节。
- cycle10（PID 11036）在录像开始后等待 3s 再 Invoke，仍抛 `Settings element not found`；不是“树未就绪”单因，需换 harness 或直接读取宿主时间线。cycle10 同样无效。

## 6. 状态与清理

- 未改产品代码；页面级仍 **50**、像素仍 **0**。
- 无残留 Augit/PyCharm；临时设置副本由工具清理；全局设置仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-191.json`（cycle6）、`cleanup-check-192.json`（cycle7 invalid）、`cleanup-check-193.json`（cycle8/9 invalid）。
