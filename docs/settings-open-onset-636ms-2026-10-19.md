# Settings 打开探针 636ms（2026-10-19）

## 1. 方法

- 正常入口，PID 20968，无 CDP；
- 隔离 settings 副本，真实 `SendInput` 点击 Settings `(2664,38)`；
- 点击时间戳在 **mouse down 之前** 记录，不含 `ui-drive` 进程启动/前台激活；
- 录像开始 `1792419853626`，点击 `1792419859565`，即点击发生在录像开始后 **5.939s**；
- 等待 4s 让应用完成加载后再点击。

## 2. 结果

- 6.5s 帧仍是主窗；
- 6.7s 帧已显示 `Settings — Augit`；
- `analyze-settings-onset.py` 在标题探针 `[550,180,300,70]` 的首个变化为 **6.575s**（第 356 帧）；
- 从真实 mouse down 时间戳到探针首变化：

```text
6.575 - 5.939 = 0.636s
```

即 **约 636ms**。

## 3. 边界

- 这是**标题探针首变化**，不是完整首内容/整窗 presented；
- 单次样本，不能代表分布；未复现 2904ms 历史慢样本；
- 关闭仍未测量（本轮 Alt+F4 前 Settings 仍打开，进程随后结束）；
- 不能据此宣布 Settings 性能通过或修复。

## 4. 证据

- `artifacts/pixel-followup-2026-10-19/settings-no-cdp-cycle11/cycle-screen.mkv`
- `frame-6.5s.png`（未开）、`frame-6.7s.png`（已开）
- `onset-diagnostic.json`
- `open-click.json`（`clickUtcMs=1792419859565`）

## 5. 状态与清理

- 未改产品代码；页面级仍 **50**、像素仍 **0**。
- 无残留 Augit/PyCharm；临时 settings 副本由工具清理；全局设置仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-200.json`。
