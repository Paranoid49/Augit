# 终端大字号动作区修复（2026-10-19）

## 1. 根因（CDP 实测，非猜测）

在 `textFontSize=40` 的正常入口（CDP 仅用于读取几何，不作为验收输入）测得：

| 元素 | 修复前 |
| --- | --- |
| `.terminal-header` | 1515×63 CSS |
| `.terminal-more` | x 1508–1599，宽 91，bottom 712 |
| `.terminal-hide` | x 1540–1631，宽 91，bottom 712 |
| 结果 | More/Hide 水平重叠 **59px**；两按钮 bottom 712 超出 header bottom 665 |
| `.terminal-session` | 111×28，40px 字体下标签被压扁 |

原因：`--augit-icon-hit` 在 40 号 UI 下为 91px，而 header 高度只按文字行高算到 63px，动作区仍使用固定 `right:40px`/24px 高度。

## 2. 修复

- `web/src/mockup.js` / `docs/ux-mockups/mockup.js`：`measureTerminalHeaders()` 读取 `--augit-icon-hit`；大于 32px 时给 `.terminal-tool` 加 `terminal-large-actions`，header 高度取 `max(38, 文字高度+12, iconHit+8)`，会话标签高度取 `max(28, 文字高度-2)`。
- `web/src/mockup.css` / `docs/ux-mockups/mockup.css`：在大字号类下用命中区定位动作按钮；More 右边距为 `8 + iconHit + 8`，Hide 仍为 8；会话标签和关闭叉使用独立高度。
- 普通字号不添加该类，原 28px 标签、40px 右边距和既有终端配对布局不变。

## 3. 修复后几何

| 元素 | 修复后 |
| --- | --- |
| `.terminal-header` | 1515×99 CSS，bottom 701 |
| `.terminal-session` | 111×49，top 25，bottom 676 |
| `.terminal-session-close` | 20×49，top 25 |
| `.terminal-more` | x 1441–1532，91×91，top 4，bottom 697 |
| `.terminal-hide` | x 1540–1631，91×91，top 4，bottom 697 |
| 结果 | More/Hide 间距 **8px**，两按钮完整位于 header 内；Local/关闭叉完整可见 |

正常入口（不使用 CDP）PID 18692 在 2880×1716 / DPI 168 / `textFontSize=40` 下复验，截图 `actual/actual-terminal-40-fixed-normal.png`（`56797394…EB38`）。

## 4. 验证

- 新增 `tools/test-terminal-large-actions.mjs`：**5/5 PASS**（大字号类、header 高度、会话高度、More/Hide 偏移、两份资产一致）。
- `verify-ui-assets.ps1`：**PASS**。
- `node --check` 两份 `mockup.js`：通过。
- 普通字号路径：代码只在 `iconHit > 32` 时添加 `terminal-large-actions`；普通字号不命中新 CSS 规则。该路径本批只做了静态检查，**未重采普通字号终端完整配对**，不宣称无回归。
- 本项修复只解决“动作区被裁切/重叠”，不改变状态栏 7px、ANSI 蓝、深色首行 +9 等既有差异；页面级仍 **50**、像素仍 **0**。

## 5. 身份与清理

- 新 `mockup.js` `355FC6E3…CAEF`；新 `mockup.css` `923B2400…5097`；`live-data.js` 仍 `12D57E34…1B3D`。
- 实际 PID 23472（CDP 几何）、18692（正常入口截图）均正常关闭；临时夹具删除。
- 全局设置仍 `B8868855…DC3D`；未提交、未推送。
- `cleanup-check-174.json`。
