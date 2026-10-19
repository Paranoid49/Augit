# 终端深色状态配对（2026-10-18）

## 1. 结论

完成终端**深色、8 行输出、失焦**状态的同态取证与一次编辑器底色修复：

- 两端 2880×1716、DPI168、深色、JetBrains Mono 13、`notes.txt` 打开、Terminal `Local`、8 行 `Ag 0123456789`、终端失焦。
- **页面级同态配对仍为 2（main-project/text-viewer、terminal 浅色），像素合格仍为 0**；本记录是终端的**深色状态级配对**，不把页面级计数虚增。
- 修复：实际编辑器/行号栏底从工具面板 `#191A1C` 改为参考的 `#1E1F22`；终端文字核心色两端均为 `#BCBEC4`。

## 2. 方法与身份

- 参考：隔离 PyCharm **2026.2.1 / 262.9437.214**，PID 4964；为取得真正的深色编辑器，临时把隔离配置 `laf.xml` 设为 `Islands Dark`、`colors.scheme.xml` 设为 `Dark`，取证后两文件均恢复为 `Islands Light` / `Light`（备份见证据目录）。
- 实际：`start-augit.ps1 -Workspace D:/JetBrains/PyCharm-2026.2.1-reference/fixture`，PID 9612；用设置 Appearance 主题下拉切到 Dark（UIA SetFocus + 空格/下/回车 + UIA 调用 OK），取证后 `settings.json` 恢复 `B8868855…DC3D`（Light）。
- 终端命令两端相同：`Clear-Host; 1..8 | ForEach-Object { 'Ag 0123456789' }`，真实键盘输入；终端失焦（点击编辑区），指针在编辑区 `(1500,800)`。
- 实际端真实拖动底部分隔条 `(1500,1130) → (1500,1099)`，首行到 1196；参考深色面板保持其原高度（面板顶 1097）。

## 3. 测量（物理像素）

| 项目 | 参考 | 实际（修复后） |
| --- | --- | --- |
| 面板顶缘 | 1097–1103 | 1098–1100（+1） |
| 终端标题文字 | 1134–1149 | 1130–1145（-4） |
| 头部下缘 | 1176–1180 | 1171–1175（-5） |
| 首行墨迹 | 1187–1203 | 1196–1212（+9） |
| 八行 / 步长 | 1187…1408，31/32 | 1196…1417，31/32 |
| 提示符 | 1462–1478 | 1448–1464（-14） |
| 状态栏上缘 | 1660–1664 | 1668–1670（+8） |
| 编辑器/行号栏底 | `#1E1F22` | 修复前 `#191A1C` → 修复后 `#1E1F22` |
| 终端正文底 | `#1E1F22` | `#1E1F22` |
| 状态栏底 | `#26282C` | `#26282C` |
| 面板顶边框 | `#212326` | 修复前 `#26282C` → 修复后 `#212326` |
| 文字核心色 | `#BCBEC4` | `#BCBEC4` |
| 首行墨迹像素 | 1148 | 1235（子像素/抗锯齿分布不同） |
| 墨迹左缘 / 宽 | 95 / 约 180 | 93 / 约 180 |

参考深色首行比参考浅色（1196）高 9 物理；实际深浅都停在 1196。该 9px 是**参考主题内的终端正文上边距差异**，本轮没有为了对齐而再拖面板，面板顶仍保持 1px。

## 4. 修复

- 新增令牌 `--augit-editor-bg`：浅色 `#FFFFFF`、深色 `#1E1F22`。
- `.code-view` 背景从 `var(--augit-panel)` 改为 `var(--augit-editor-bg)`，行号栏随同。
- 修复前实际编辑器底 `#191A1C`（工具面板色），修复后 `#1E1F22`，与参考一致。
- `verify-ux-offline-icons.cjs` 增加 `text-viewer.html` 深浅色 `.code-view` 底色断言。
- 第 17 批同时修复终端面板顶边：4px 行间隙由 `.app-main:has(.terminal-tool)` 用 `--augit-terminal-panel-border` 填充（浅 `#F1F2F4`、深 `#212326`），`.terminal-tool` box-shadow 同色；深色实测 1098–1104 `#212326`，参考 1097–1103 `#212326`。

## 5. 仍未同态

- 首行 +9、提示符 -14、状态栏 +8；头部标题文字 -4。面板顶边框已修复为两端 `#212326`（第 17 批）。
- 参考头部 `+`/Shell 下拉/AI Agents 与实际的 `...`/`−` 属产品范围差异；参考提示符块间距更大，属已批准 Reworked 差异。
- 墨迹核心色一致，但参考子像素/抗锯齿像素更多（1148 vs 1235），不等于字形或整窗像素通过。
- 仅覆盖深色 8 行失焦；深色选区/焦点/其他内容未覆盖。

## 6. 附带发现：深色设置 footer 越界已更正

切换主题时曾观察到设置底栏在 UIA 坐标 `y≈1943`、屏幕外；2026-10-18 干净复测（正常入口、未移动窗口）显示设置窗口 1040×760 CSS、深色预览后 OK 在 y=1446、footer 正常可见；受控 resize 请求 884 高时窗口被最小高度钳到 1120 物理（640 CSS），OK y=1234 仍可见，且窗口无 WS_THICKFRAME、用户不能缩放。因此该现象是审计强制尺寸/过渡态伪影，**不是产品缺陷**；详见[设置 scoping §11](settings-pair-scoping-2026-10-18.md)。

## 7. 证据与清理

- 参考官方桌面采集：`reference/reference-terminal-dark.png`（SHA256 `9bb9f447…bfdd`）。
- 实际修复前：`actual/actual-terminal-dark.png`（`2a77cd26…20ac`）；编辑器底修复后 `actual-fixed/actual-terminal-dark-before-border-fix.png`（`b6844415…cd59`）；第 17 批边色修复后 `actual-fixed/actual-terminal-dark.png`（`d65d7480…50e2`）。
- 对照：`terminal-dark-pair-overview.png`（`62f6bbc9…efcf`）、`terminal-dark-content-compare.png`（`d1655c23…80c2`）。
- 参考无效中间图：`reference-terminal-dark-invalid-white-editor.png`（主题深、编辑器仍白；不作为证据）。
- 参考配置备份：`reference/laf-before.xml`、`reference/colors-scheme-before.xml`；均已恢复。
- 清理：参考 PID 4964、实际 PID 9612 正常关闭；`settings.json` 恢复 `B8868855…DC3D`；无端口/应用残留；`cleanup-check-43.json`。未提交、未推送。
