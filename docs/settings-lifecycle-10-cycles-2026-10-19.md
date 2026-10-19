# Settings 原生窗口生命周期 10 周期（2026-10-19）

## 1. 方法

与 3 周期样本相同：

- 正常入口，PID 4176，无 CDP；隔离 settings 副本；
- 真实 `SendInput` 点击 Settings `(2664,38)`；
- `EnumWindows` + `IsWindowVisible` 以 5ms 轮询 Settings HWND 出现；
- 真实点击 Cancel `(2098,1471)`，5ms 轮询该 HWND 销毁；
- 连续 10 次开关，时间戳在 mouse down 前记录。

## 2. 结果

| 周期 | 打开窗口出现 | 关闭窗口销毁 |
| --- | --- | --- |
| 1 | 327ms | 63ms |
| 2 | 300ms | 61ms |
| 3 | 298ms | 64ms |
| 4 | 269ms | 65ms |
| 5 | 271ms | 63ms |
| 6 | 268ms | 63ms |
| 7 | 267ms | 62ms |
| 8 | 268ms | 64ms |
| 9 | 281ms | 62ms |
| 10 | 285ms | 64ms |

统计：

```text
打开：min 267 / median 276 / mean 283.4 / max 327 ms
关闭：min 61  / median 63  / mean 63.1  / max 65 ms
```

与前一版 3 周期（打开 268/286/297，关闭 60/62/63）合并：13 次打开 267–327ms，关闭 60–65ms。

## 3. 边界

- 这是**原生窗口生命周期**，不是完整首内容/整窗 presented；
- 同一机器同一构建，不能外推到其他 DPI/机器；
- 未复现 2904ms 历史慢样本；
- 不能据此宣布 Settings 首内容性能通过。

## 4. 证据

- `artifacts/pixel-followup-2026-10-19/settings-lifecycle10/lifecycle.json`
- `lifecycle.ps1`、`launch.log`

## 5. 状态与清理

- 未改产品代码；页面级仍 **50**、像素仍 **0**。
- 无残留 Augit/PyCharm；临时 settings 副本删除；全局设置仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-203.json`。
