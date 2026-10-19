# 终端首个同态配对（2026-10-18）

## 1. 结论

`terminal.html` 的**首个同态配对完成**（浅色、Local、Windows PowerShell、8 行 `Ag 0123456789`、终端失焦）：

- 面板顶缘差 1 物理像素；八行墨迹顶差 0/1 交替；字宽 177 一致；左缘差 1 物理像素。
- 提示符与输出之间的 **23 物理像素块间距**是用户已批准的 PyCharm Reworked 差异，不整体平移配准。
- 终端头部 Local 标签/关闭区已在第 5 批修复（§8），面板顶边框色在第 17 批修复（§9）；状态栏 7 物理像素、右侧栏 42 CSS、ANSI 蓝、ui-size 40 头部重叠仍未同态，因此**像素合格整页配对仍记 0**，只记同态配对完成 1。

本记录只覆盖上述一个终端状态，不等于终端全部内容、主题、焦点、选区或 54 页完成。

## 2. 方法与身份

- 参考：隔离 PyCharm **2026.2.1 / 262.9437.214**，PID 19256，`reference-2026.2.1.properties`，fixture，Islands Light，Microsoft YaHei UI 12 / JetBrains Mono 13。
- 实际：`tools/start-augit.ps1 -Workspace D:/JetBrains/PyCharm-2026.2.1-reference/fixture` 正常入口，PID 1804；`Augit.dll` SHA256 `F53764A8…B4E00`，`mockup.js` `78E9EAA1…1758`，`mockup.css` `20EFA054…12E7`。
- 两端 2880×1716、DPI168、浅色；真实鼠标打开终端、SendInput 键盘输入同一命令并回车：

```powershell
Clear-Host; 1..8 | ForEach-Object { 'Ag 0123456789' }
```

- 实际端真实拖动底部分隔条上缘 `(1500,1130) → (1500,1099)`，面板顶从 1129 上移到 1098 物理像素，首行从 1227 上移到 1196；参考首行同为 1196。没有改参考高度、没有场景注入或自定义 Shell 启动命令。
- 两端点击编辑区让终端失焦、指针在编辑区 `(1500,800)` 后采集；原始图：
  - `artifacts/pixel-followup-2026-10-18/terminal-pair-2026-10-18/reference-terminal.png`（SHA256 `2946c1a4…4fdbe`）
  - `actual-terminal.png`（第 17 批后 SHA256 `cacf7df0…2bfc`；边框修复前 `actual-terminal-before-border-fix.png`，`b64b605e…9231d`；更早的标签修复前 `actual-terminal-before-tab-fix.png`，`fa3b3788…931be`）
- 环境与完整测量：`pair-environment.json`；局部对照：`terminal-content-compare.png`；总览：`terminal-pair-overview.png`。

## 3. 测量

| 项目 | 参考 | 实际 | 差 |
| --- | --- | --- | --- |
| 面板顶缘（物理） | 1097–1103 | 1098–1100 | +1 |
| 头部下缘（物理） | 1176–1180 | 1171–1175 | −5 |
| 首行墨迹顶 | 1196 | 1196 | 0 |
| 八行墨迹顶 | 1196/1227/1259/1290/1322/1353/1385/1416 | 1196/1228/1259/1291/1322/1354/1385/1417 | 0/1 交替 |
| 行高 / 步长 | 21 / 31–32 | 21 / 31–32 | 一致 |
| 墨迹左缘 / 宽 | 96 / 177 | 95 / 177 | −1 / 0 |
| 首行墨迹像素 | 637 | 646 | +9（抗锯齿） |
| 提示符顶 | 1469 | 1446 | −23（已批准块间距） |
| 状态栏上缘 | 1661 | 1668 | +7 |
| 终端右缘（非白） | 2793–2812 | 2869–2879 | 右侧栏 42 CSS |

颜色抽查：背景两端均 `#FFFFFF`，状态栏底均 `#E9EAEE`；面板顶边框两端均 `#F1F2F4`（第 17 批修复，见 §9）；提示符/强调蓝参考 `#0060AF`、实际 `#298ADE`。不因黑白/背景一致宣称全配色通过。

## 4. 差异与性质

| 差异 | 性质 |
| --- | --- |
| 八行顶 0/1、左缘 1 物理像素 | 抗锯齿/栅格相位残差，结构已对齐 |
| 提示符块间距 23 物理像素 | 用户已批准的 PyCharm Reworked 命令块有意差异，保留 |
| 状态栏上缘 7 物理像素 | 状态栏内容/高度产品差异，登记 |
| 终端右缘/面板宽度 | 参考独有右侧工具窗条 42 CSS + 滚动条；结构差异，不删功能凑图 |
| Terminal/Local 标签与关闭区 | 已修复（§8）：改为约 65 CSS 的紧凑标签，关闭叉在同一卡片内；位置差 -4/-2 物理像素、填充宽 108 vs 109 |
| 头部工具条 | 参考 AI Agents/分屏/Shell 下拉等；实际仅 `...`/`−`。产品范围差异，未增加 PyCharm 独有能力 |
| ui-size 40 头部动作 | `terminal-more`/`terminal-hide` 命中区放大到约 91×91，固定 right 偏移导致 Hide 覆盖 More。既有大字号布局缺陷，本批未修 |
| 面板顶边框色 | 已修复（§9）：两端 `#F1F2F4`，间隙高 6 vs 7 物理 |
| ANSI 蓝 | 待核对 `terminal-palette`；本轮不宣称配色同态 |
| 空心方块光标 | 两端失焦后均保留；闪烁相位未比较 |

## 5. 边界

- 只覆盖一个浅色、Local、8 行输出、失焦状态；深色、聚焦、选区、滚动、其他输出、完整缓冲区未配对。
- 编辑区/Project 面板沿用主窗配对的状态，但本记录不重复其测量；右栏和文档工具栏差异同样存在。
- 没有修改产品终端逻辑；本轮只采集和对齐面板高度。

## 6. 复验

```powershell
python artifacts/pixel-followup-2026-10-18/homologous-main-text/measure-edges.py `
  artifacts/pixel-followup-2026-10-18/terminal-pair-2026-10-18/reference-terminal.png `
  --axis y --x0 900 --x1 2700 --y0 1000 --y1 1716 --threshold 2 --fraction 0.5
python artifacts/pixel-followup-2026-10-18/homologous-main-text/measure-text.py `
  artifacts/pixel-followup-2026-10-18/terminal-pair-2026-10-18/actual-terminal.png `
  --x0 50 --x1 600 --y0 1150 --y1 1550 --threshold 120 --gap 5
python artifacts/pixel-followup-2026-10-18/terminal-pair-2026-10-18/measure-terminal-colors.py
```

## 7. 清理

- Augit PID 1804：`CloseMainWindow()` 立即退出；`settings.json` 从 `settings-before-terminal-pair.json` 恢复，SHA256 `B8868855…DC3D`。
- 参考 PyCharm PID 19256：Confirm Exit 约 1.5s 退出；参考配置的终端面板高度仅影响隔离参考环境。
- 本批无 CDP 端口；无 `Augit`/`pycharm64`/WebView2 残留。`cleanup-check-34.json`；未提交、未推送。
- 第 5 批（Local 标签修复）：Augit PID 24260 正常关闭，`settings.json` 从 `settings-before-terminal-pair-2.json` 恢复，SHA256 仍为 `B8868855…DC3D`；测试 Chromium 均已关闭；`cleanup-check-35.json`。

## 8. 第 5 批：Local 标签修复（2026-10-18 19:49）

### 修改

- 修复前：Local 是 124 CSS 宽的下拉式会话框（`#F7F8F9`、双内描边、文字居中），关闭叉是独立的 24/28 CSS 描边按钮。
- 现在：会话段约 45 CSS（文字左对齐、左 8/右 4 内距、填充 `#ECECF0`、边框 `#D6D8DD`、左侧 6px 圆角），关闭段 20×28 CSS（同填充/边框、右侧 6px 圆角、`closeSmall` 图标 `#A8ADBD`），两段无缝拼成约 65 CSS 的标签，X 位于卡片内。令牌 `--augit-terminal-tab-bg`/`--augit-terminal-tab-border`/`--augit-tab-close`。
- `measureTerminalHeaders()` 不再使用 124px 下限，会话宽度按文字 + 内距计算；`terminal-more`/`terminal-hide` 等产品动作不变。
- 测试：`live-icons` 的 Text 条目已在第 2 批加入；本批在 `verify-ux-offline-icons.cjs` 增加 terminal.html 的 Local 标签尺寸/关闭图标/颜色断言，并订正 `verify-ux-frame-buttons.cjs` 的过期 Local/英文 aria/工具轨 `::before` 期望。

### 测量（物理像素）

| 项目 | 参考 | 修复前 | 修复后 |
| --- | --- | --- | --- |
| 标签填充 bbox | x223–331 / y1117–1165 | x204–412 / y1119–1163（宽 209） | x219–326 / y1115–1159（宽 108） |
| 标签总宽 | 约 113 | 约 209 + 独立关闭 | 约 112（45+20 CSS） |
| 关闭叉 | 标签内，约 8 CSS，#A8ADBD | 独立 24/28 CSS 黑叉 | 标签内 20×28 CSS 段，`closeSmall` #A8ADBD |
| 填充/边框 | #ECECF0 / #D6D8DD | #F7F8F9 / 双描边 | #ECECF0 / #D6D8DD |

- 位置差 -4/-2 物理像素、填充宽 108 vs 109；标签高度 49 物理像素（28 CSS）。
- 终端内容/面板测量不变：首行 1196、八行 0/1、左缘 95 vs 96、步长 31/32。

### 验证

- `verify-ui-assets.ps1`：PASS（视觉稿与运行时字节一致）。
- `verify-ux-offline-icons.cjs`：**PASS**，110 个场景/主题、12 个尺寸组合、2 个动态流程，含新增终端 Local 标签断言。
- `node --check`：`mockup.js`、`verify-ux-frame-buttons.cjs` 通过。
- `verify-ux-frame-buttons.cjs` **未通过**：dark pushed 实际 alpha 0.16 vs 期望 0.15；ui-size 40 的 `terminal-more`/`hide` 命中区约 91×91 与固定 right 偏移重叠。两者均为既有问题，不能算本批通过；该套件在本批只用于暴露过期期望，不作为终端标签修复的通过依据。
- 原始图：第 17 批后 `actual-terminal.png`（`cacf7df0…2bfc`）；第 5 批时 `b64b605e…9231d` 已保留为 `actual-terminal-before-border-fix.png`；标签修复前 `actual-terminal-before-tab-fix.png`（`fa3b3788…931be`）。
- Local 标签修复前后对照：`local-tab-compare.png`（上参考、下实际，4× 放大）。

## 9. 第 17 批：终端面板边色（2026-10-18 20:40）

- 根因：编辑区与终端之间的 4px 行间隙由 `.app-main` 的 `row-gap` 形成，透出 chrome（浅 `#E9EAEE`、深 `#26282C`），不是 `.terminal-tool` 自身的边框；只改工具窗 box-shadow 不改变实测顶边。
- 修复：
  - 新增令牌 `--augit-terminal-panel-border`：浅 `#F1F2F4`、深 `#212326`（参考实测）；
  - `.terminal-tool` 的 box-shadow 使用该令牌；
  - `.app-main:has(.terminal-tool)` 背景使用该令牌，填充 4px 行间隙。
- 实测：
  - 浅色参考顶边 1097–1103 `#F1F2F4`；实际修复前 1098–1101 `#E9EAEE`，修复后 1098–1103 `#F1F2F4`（6 vs 7 物理）。
  - 深色参考顶边 `#212326`；实际修复前 `#26282C`，修复后 1098–1104 `#212326`（7 vs 7，整体 +1）。
  - 终端首行/面板顶/状态栏等其余测量不变。
- 图像：浅色 `actual-terminal.png`（`cacf7df0…2bfc`）；深色 `actual-fixed/actual-terminal-dark.png`（`d65d7480…50e2`）；修复前分别保留为 `*-before-border-fix.png`。
- 验证：`verify-ui-assets.ps1` PASS；`verify-ux-offline-icons.cjs` PASS（新增 `.app-main` 间隙色断言，110 场景/主题）。
- 仍未闭环：状态栏 7/8 物理、右侧栏 42 CSS、ANSI 蓝、深色首行 +9；面板边色不再列为剩余差异。
