# 深色终端面板顶偏移 8 物理px（2026-10-19）

## 1. 方法

- 正常入口 PID 9708，深色主题，`bottomPanelHeight=320`；
- 打开 Terminal，`Clear-Host` 后输出 8 行 `Ag 0123456789` 与提示符，内容与参考对齐；
- 对比参考 `reference-terminal-dark.png` 与实际 `actual-dark-320.png`。

## 2. 结果

在 x=100 的垂直切片：

| 项 | 参考 | 实际 320 |
| --- | --- | --- |
| 编辑器底部白线结束 | 1149 | 1141 |
| 终端 header 顶边框 | 1176 | 1168 |

- 实际终端整体比参考高 **8 物理px**（约 4.6 CSS）；
- 这解释了此前首行差异的一部分：面板顶/内容整体上移，不能只调首行 padding；
- 8px 与 `bottomPanelHeight=320` 的取值/面板高度换算有关，尚不能判定为产品缺陷。

## 3. 下一步

- 用 `bottomPanelHeight≈315.4` 复采同一内容，确认 header/首行是否同时对齐；
- 若对齐，说明是面板高度夹具差异；若不对齐，再查终端内部 header/内容 padding；
- 不改 CSS 猜测，直到两态同内容、同面板顶。

## 4. 证据

- 参考：`artifacts/pixel-followup-2026-10-18/terminal-dark-pair/reference/reference-terminal-dark.png`
- 实际：`artifacts/pixel-followup-2026-10-19/terminal-dark-match/actual-dark-320.png`

## 5. 状态与清理

- 未改代码；页面级仍 **50**、像素仍 **0**；
- PID 9708 正常关闭；临时夹具删除；`cleanup-check-213.json`。
