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

## 4. 修复与正常入口复核

DOM 测量显示 `.terminal-view` 的 `padding-top` 为 11 CSS，而参考首行只需约 2.5 CSS：

- `web/src/mockup.css` / `docs/ux-mockups/mockup.css`：`padding: 11px 12px` → `2.5px 12px 11px`；
- 正常入口 PID 14596、深色、`bottomPanelHeight=315.4`、同内容复采：
  - header 顶边框 **1176**（参考 1176）；
  - 首条内容 **1187**（参考 1187）；
  - 终端内容区域二维配准最佳 **dx -1px / dy -1px**。
- 新增 `tools/test-terminal-view-padding.mjs`，web/视觉稿 2/2 通过；UI 资产字节一致 PASS。
- 这是终端区域对齐，不是整页像素合格；页面级仍 50、像素 0。

### 浅色回归

正常入口 PID 21408、浅色、同内容复采：

- header 分隔线 **1176**；
- 首条内容 **1187**；
- 与深色结果一致，说明 `.terminal-view` 顶部内距修复是主题无关的，不是只对深色补丁。

## 5. 证据

- 参考：`artifacts/pixel-followup-2026-10-18/terminal-dark-pair/reference/reference-terminal-dark.png`
- 实际 320：`artifacts/pixel-followup-2026-10-19/terminal-dark-match/actual-dark-320.png`
- 实际 315.4：`artifacts/pixel-followup-2026-10-19/terminal-dark-315/actual-dark-315.png`
- 正常入口深色：`artifacts/pixel-followup-2026-10-19/terminal-dark-padding-fix/actual-dark-padding.png`
- 正常入口浅色：`artifacts/pixel-followup-2026-10-19/terminal-light-padding/actual-light-padding.png`

## 6. 状态与清理

- 未改代码；页面级仍 **50**、像素仍 **0**；
- PID 9708 / 11524 正常关闭；临时夹具删除；`cleanup-check-213.json`、`cleanup-check-214.json`。
