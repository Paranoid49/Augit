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

## 3. 315.4 复采结果

用 `bottomPanelHeight=315.4` 复采同内容：

- 终端 header 顶边框在 **1176**，与参考一致（320 时是 1168）；
- 但 header 文案/首条文字仍在参考下方约 **14–15px**：
  - 参考 header 文案 1187–1203、首条 1219–1235；
  - 实际 315.4 header 文案 1202–1222、首条 1233–1253。
- 因此 8px 面板顶偏移已由夹具高度消除；剩余是**终端 header/内容顶部约 8 CSS 的内部高度差**，不能用面板高度继续补。

## 4. 下一步

- 用 CDP/DOM 读取 `.terminal-header`、`.terminal-tool` 的实际 content rect 与 padding，定位 8 CSS 差异；
- 不改 CSS 猜测，直到两态同内容、同面板顶、同 header 高度。

## 5. 证据

- 参考：`artifacts/pixel-followup-2026-10-18/terminal-dark-pair/reference/reference-terminal-dark.png`
- 实际 320：`artifacts/pixel-followup-2026-10-19/terminal-dark-match/actual-dark-320.png`
- 实际 315.4：`artifacts/pixel-followup-2026-10-19/terminal-dark-315/actual-dark-315.png`

## 6. 状态与清理

- 未改代码；页面级仍 **50**、像素仍 **0**；
- PID 9708 / 11524 正常关闭；临时夹具删除；`cleanup-check-213.json`、`cleanup-check-214.json`。
