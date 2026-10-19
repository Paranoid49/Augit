# 设置开/关窗口级冒烟（2026-10-18）

## 1. 目的

设置外观页在第 8–10 批改了左栏、控件宽度和页顶内距。启动/创建路径没有改，但需要一次正常入口的窗口级冒烟，确认设置窗口仍能稳定打开、关闭，且不出现重复窗口。本记录**不**替代首内容、白闪或慢样本证据。

## 2. 方法

- 正常入口：`start-augit.ps1 -Workspace D:/JetBrains/PyCharm-2026.2.1-reference/fixture`，PID 17496；未启用 CDP。
- 点击标题栏 Settings（物理 `2664,38`），用 `EnumWindows` 按 PID + 可见顶层窗口标题包含 `Settings` 轮询；记录点击到窗口出现的毫秒数。
- 出现后记录该 PID 的可见 Settings 窗口数量；用 `WM_CLOSE` 关闭，轮询到窗口消失，记录关闭毫秒数。
- 连续 3 次；每次关闭后 600ms 再下一次。脚本：`artifacts/pixel-followup-2026-10-18/settings-scoping/open-close-smoke/run-smoke.ps1`。

## 3. 结果

| 运行 | 窗口出现 | WM_CLOSE 消失 | 可见 Settings 窗口 | 重复窗口 |
| --- | --- | --- | --- | --- |
| 1 | 343ms | 30ms | 1 | 0 |
| 2 | 330ms | 38ms | 1 | 0 |
| 3 | 349ms | 24ms | 1 | 0 |

原始数据：`open-close-smoke.json`。

## 4. 解释与边界

- 上述 330–349ms 是**窗口出现**的下界，不是页面首内容、不是无白闪，也不能与既有 599.2–747.5ms 的“无 CDP 首内容/开窗”证据直接比较为改善。
- 24–38ms 是 `WM_CLOSE` 到窗口消失的窗口级时间，不等于既有 48.3–86.7ms 的“原生关闭”口径。
- 3 次都只有 1 个可见 Settings 窗口，未出现重复窗口；这是本批的主要新证据。
- 只有 3 次、单一 Windows 会话、单一 DPI；未覆盖跨屏 DPI、深色白闪、2904ms 慢样本或 500ms 超时分支。

## 5. 清理

- Augit PID 17496 正常关闭；`settings.json` 恢复 `B8868855…DC3D`。
- 本批未启动参考 PyCharm，未触碰用户原有 PyCharm；无 CDP 端口；无残留应用。
- 未提交、未推送；`cleanup-check-41.json`。
