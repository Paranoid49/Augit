# 设置捕获超时的外部挂起尝试（2026-10-18）

## 目的

尝试在不修改产品代码、不注入测试分支的前提下，用 OS 级短暂挂起本实例 WebView2 renderer/GPU，让 `CapturePreviewAsync` 超过 500ms，从而验证：

- 是否记录 `capture-timeout`；
- 是否在捕获未完成时提前揭示设置窗；
- 提前揭示时用户看到的是已渲染表单、空白/透明帧，还是主窗内容。

## 方法

- 正常入口 `tools/start-augit.ps1` 强制构建、临时设置副本、附 CDP 只用于观察与关闭。
- 预热 PowerShell helper，缓存本实例 `msedge-webview2` 的 renderer/GPU 进程句柄；点击设置后，用 `NtSuspendProcess` 挂起约 1.2 秒，随后 `NtResumeProcess`。
- 在设置页注入只读 `postMessage` 观察器，检测 `window/settings-present`，再延迟 25ms 挂起缓存进程。
- 挂起期间用 `CopyFromScreen` 截取 Settings 窗口矩形，恢复后再截一张；保存 native trace 和报告。
- 工具：`tools/audit/settings-capture-stall.cjs`、`suspend-webview2.ps1`、`suspend-webview2-fast.ps1`。这些是**实验性审计工具**，不是产品功能。

## 结果

| 尝试 | 挂起对象/时机 | 结果 |
| --- | --- | --- |
| 1 | helper 启动后才查 CIM | 挂起发生在捕获结束之后；未触发 timeout |
| 2 | 同上，延迟 360ms | 仍晚于捕获；未触发 timeout |
| 3 | 缓存的 GPU + 主 renderer，窗口出现后 30ms | 设置页 `settings/read` 被挂起 1.44s，捕获尚未开始；恢复后才完成 77ms 捕获。截图矩形显示的是背后的主窗，不是提前揭示的设置窗 |
| 4 | 改为 `window/settings-present` +25ms 挂起缓存进程 | 缓存没有包含设置页新建的 renderer；捕获 85ms 完成，未触发 timeout；`stalled-settings.png` 是完整表单 |
| 5 | CDP 差异定位设置页新 renderer，present +30ms 挂起 | 捕获 93ms 完成，未触发 timeout |
| 6 | 追加 CDP 报告的 GPU，present +30ms 挂起 | 捕获 83ms 完成，未触发 timeout；`stalled-settings.png` 仍是完整表单 |
| 7 | 同上，present +10ms 挂起 | 捕获 81ms 完成，未触发 timeout |

- 所有尝试的 native trace 均**没有** `capture-timeout`。
- 尝试 3 的 trace 中 `settings/read` 从 153ms 推迟到 1536ms，说明挂起击中了设置页初始化，而不是 `CapturePreviewAsync`；因此不能把它当作超时路径。
- 尝试 4–7 的 `stalled-settings.png` 与正常表单一致，说明捕获并未被有效挂起，也不能证明提前揭示无风险。
- 尝试 5–7 表明：挂起 CDP 报告的新 renderer 和 GPU 时，`CapturePreviewAsync` 仍能在 81–93ms 内完成；该 COM 可能直接取用已经合成的表面，不再等待被挂起的 renderer/GPU。此路径不能触发 500ms 分支。
- 结论：外部挂起**没有**触发 500ms 超时分支；仍未取得“超时后提前揭示”的真实证据。此次尝试不构成修复，也不降低该未完成项。

## 具体阻塞与下一步

- 阻塞：`CapturePreviewAsync` 的慢点不在设置页 renderer/GPU 的持续渲染；挂起这些进程后捕获仍完成。要触发 500ms，需要让浏览器侧捕获/合成本身延迟，而这与超时计时同一线程/进程，简单挂起浏览器会让超时也无法执行。
- 下一步（未执行）：等待真实 2904ms 样本，或用不影响浏览器消息循环的同步捕获延迟（例如独立 WGC/DWM 侧阻塞）复现；不再重复 renderer/GPU 挂起。
- 不能把本次“未触发 timeout”写成“超时路径不存在”或“修复有效”。

## 证据与清理

- 尝试目录：`artifacts/pixel-followup-2026-10-18/settings-capture-stall-2/` 至 `-9/`，以及 `settings-process-probe/`。
- `settings-capture-stall-5/stalled-settings.png` 显示主窗内容，证明当时设置窗尚未揭示；`-6/` 至 `-9/` 的 `stalled-settings.png` 是完整表单，证明捕获已完成。
- 所有实验 PID（14120/16640/25244/12060/8588/26488/26224/5312/25704 等）已退出，端口已关闭，`Augit-capture-stall-*` 和 `Augit-settings-process-*` 临时目录已删除。
- 第一次 `settings-capture-stall` 输出因 helper 语法失败未产生 cleanup；实例 16640 已按本批核实 PID 停止，临时目录单独删除。
