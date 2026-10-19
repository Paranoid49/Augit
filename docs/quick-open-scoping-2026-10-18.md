# Quick Open 空态取证尝试（2026-10-18）

> **第 110 批更新（成立）**：Ctrl+P 未打开的原因是 WebView 未获得键盘焦点（只激活宿主窗口）；**先在编辑区点击**再按 Ctrl+P，实际 `Open File` 空浮层正常打开，参考 Ctrl+Shift+N 打开 `Search Everywhere` 空浮层；已登记为 [Quick Open 空态同态配对](quick-open-empty-homologous-pair-2026-10-18.md)（页面级 41）。本页“根因未定”结论已更正。

## 1. 结论（当时未配对，保留过程）

正常入口下 `Ctrl+P` 未打开 Quick Open，未取得实际空态，**未建立配对**，页面级仍 40。

- 实际 PID 23268：正常入口 → 主窗 Ctrl+P；1.5s 后截图仍是默认主窗（`56D24943…B799`），没有 Quick Open/搜索浮层。
- 左侧搜索轨按钮打开的是 **Find in Files（repository）**，不是 Quick Open；未找到其他正常入口。
- 宿主已关闭 WebView2 浏览器加速键（`ShellWindow.cs:775` `AreBrowserAcceleratorKeysEnabled = false`），所以不是 WebView 的 Ctrl+P 打印抢占；`live-data.js` 也存在 `Ctrl+P → openSearchOverlay("quick")` 处理。为何未打开**根因未定**，不据此判缺陷。
- 参考端 Go to File 空态未采集。

## 2. 后续

- 用 CDP 在正常入口实例调用 `openSearchOverlay("quick")`，确认 Quick Open 页面本身是否存在/可用，再判断是快捷键投递还是功能缺口。
- 参考端用 Ctrl+Shift+N（Go to File）采集空态；实际补到后再配对。

## 3. 边界

- 未测：Quick Open 输入文件名、结果、Esc 关闭、深色。
- 用户 54 页中的“快速打开空态”仍为未完成。

## 4. 清理

- 实际 PID 23268 已结束；`settings.json` 恢复 `B8868855…DC3D`；无 Augit/pycharm64 残留；无 CDP 端口。
- 本批未修改代码；`cleanup-check-130.json`；未提交、未推送。
