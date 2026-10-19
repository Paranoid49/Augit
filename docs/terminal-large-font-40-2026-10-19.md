# 终端 ui-size 40 大字号布局复验（2026-10-19）

> **2026-10-19 后续**：本记录只保留为失败尝试与回退证据。随后用 CDP 量得 More/Hide 重叠 59px、按钮超出 header 39px，已按命中区放大 header/会话/动作区并完成正常入口复验，见[终端大字号动作区修复](terminal-large-actions-fix-2026-10-19.md)。本页“未修复”结论不再代表当前状态。

## 1. 结论

在正常入口把 `textFontSize` 设为 **40**，打开 Terminal 后，头部布局仍不完整：`Local` 标签和关闭区被放大，右侧只看到一个近似 `...` 的点状动作，`Hide` 没有以可辨识形式出现。仅调整 `terminal-more` 的 `right` 偏移不能解决整体头部布局，因此**本批不登记修复**。

- 实际 PID 14072；2880×1716、DPI 168、浅色；`textFontSize=40`；正常入口。
- 证据：`actual/actual-terminal-40.png`（`203BBDAD…50AB`）、`actual/actual-initial-40.png`（`48445D6A…B910`）。
- 尝试：把 `.terminal-header .terminal-more` 改为 `right: calc(12px + var(--augit-icon-hit, 28px))`，并新增 5/5 静态守护；实采显示 Local/关闭区/More/Hide 的整体布局仍异常，无法证明问题已解决。
- 处理：**已回退**该 CSS 与测试，`mockup.css` 回到 `A654FB57…444C`；`verify-ui-assets.ps1` PASS，`test-smart-checkout-copy.mjs` 5/5。未留下半成品代码。

## 2. 边界

- 只有一张正常入口截图，没有 CDP 几何/UIA 命中区，因此不能把“点状动作”单独判为 More 或 Hide，也不能给出精确重叠矩形。
- 未修改字体大小设置之外的用户配置；夹具与设置均为隔离副本。
- 该记录不改变页面级 50 或像素 0；终端大字号仍是剩余缺口。

## 3. 清理

- PID 14072 正常关闭；临时夹具删除；全局设置仍 `B8868855…DC3D`。
- 未修改代码、未提交、未推送；`cleanup-check-172.json`。
