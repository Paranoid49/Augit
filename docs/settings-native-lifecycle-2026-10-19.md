# Settings 原生窗口生命周期样本（2026-10-19）

## 1. 方法

- 正常入口，PID 13412，无 CDP；隔离 settings 副本；
- 真实 `SendInput` 点击 Settings `(2664,38)`；
- `EnumWindows` + `IsWindowVisible` 以 5ms 轮询 Settings HWND 出现；
- 真实点击 Cancel `(2098,1471)`，以 5ms 轮询该 HWND 是否销毁；
- 连续 3 次开关；时间戳在 mouse down 前记录。

## 2. 结果

| 周期 | 打开：窗口出现 | 关闭：窗口销毁 |
| --- | --- | --- |
| 1 | 297ms | 63ms |
| 2 | 286ms | 60ms |
| 3 | 268ms | 62ms |

- 打开窗口出现：**268 / 286 / 297ms**（中位 286ms）
- 关闭窗口销毁：**60 / 62 / 63ms**（中位 62ms）

这些是**原生窗口生命周期**，不是完整首内容/整窗 presented。窗口出现早于内容完成，不能当首内容耗时；关闭窗口销毁也不等于界面完全无残留。

## 3. 证据

- `artifacts/pixel-followup-2026-10-19/settings-lifecycle/lifecycle.json`
- `launch.log`、`lifecycle.ps1`
- 三次周期均成功找到 HWND 并关闭，无失败行。

## 4. 边界

- 单机单次 3 周期，不是分布；未复现 2904ms 慢样本；
- 与既有“窗口出现 330–349ms / close 44ms”样本数量级一致，但环境和采集方式不同，不能替换；
- 仍需原生首内容时间线/完整 presented 才能完成设置性能验收。

## 5. 状态与清理

- 未改产品代码；页面级仍 **50**、像素仍 **0**。
- 无残留 Augit/PyCharm；临时 settings 副本删除；全局设置仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-202.json`。
