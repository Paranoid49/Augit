# 设置 Appearance 页参考/实际取证（2026-10-18）

## 1. 结论

完成设置 **Appearance & Behavior > Appearance** 页的参考/实际同尺寸取证与结构测量：

- **未建立同态配对**，不记入“同态配对完成 2”的计数，也不改变“像素合格整页配对 0”。
- 已确认两端都是模态设置窗口、浅色、Microsoft YaHei UI / Size 12、相同面包屑和底部 OK/Cancel/Apply。
- 第 8 批已把设置左栏分隔线从 414 物理调到 **442–443**，与参考 443–444 在 1px 内；其余分类/控件结构差异仍在。
- 参考的左栏是分类树，包含产品范围外的分类和控件；实际按 `ux-spec §7.17` 只保留四类与受支持设置。两者不是同一信息架构，不能直接以整页结构相同为前提追求像素一致。

## 2. 方法与身份

- 参考：隔离 PyCharm **2026.2.1 / 262.9437.214**，PID 12652，`reference-2026.2.1.properties`，fixture，Islands Light；Ctrl+Alt+S 打开 Settings，点击 Appearance & Behavior > Appearance。
- 实际：`start-augit.ps1 -Workspace D:/JetBrains/PyCharm-2026.2.1-reference/fixture`，PID 7820；点击标题栏 Settings 打开 `Settings — Augit`，页面为 Appearance & Behavior > Appearance。
- 两端 2880×1716、DPI168。用 `capture-settings-clean.ps1` 把设置窗口移到 `(530,193)` 并请求 `1792×1225` 物理像素：
  - 参考接受 1792×1225，再强制为同尺寸后采集；
  - 实际被最小宽度钳到 1792×1225（请求 1662 宽无效），因此共同基线取 **1792×1225**，不能取参考自然宽度 1555。
- 采集：参考为窗口桌面采集（1772×1215 帧，去掉 10px 阴影）；实际为全屏采集后按同一帧原点 `(540,193)` 裁 1772×1215。两端同尺寸对照：
  - `artifacts/pixel-followup-2026-10-18/settings-scoping/reference/appearance-1792.png`（SHA256 `2e57b50e…b2a45`）
  - `artifacts/pixel-followup-2026-10-18/settings-scoping/actual/appearance-1772.png`（SHA256 `d46bb790…f0f10`）
  - 对照图：`appearance-scoping-overview.png`（SHA256 `040d3065…8c210`）

## 3. 测量

| 项目 | 参考 | 实际 |
| --- | --- | --- |
| 设置窗口（物理） | 自然 1555×1215；强制 1792×1225 | 最小 1792×1225 |
| 标题 | `Settings – notes.txt` | `Settings — Augit` |
| 主题 / UI 字体 | Islands Light / Microsoft YaHei UI 12 | Light / Microsoft YaHei UI 12 |
| 左栏分隔线 x（帧内物理） | 443–444 | 414–415（修复前）→ 442–443（第 8 批） |
| 左栏宽（CSS 约） | 253.1 | 236.6 |
| 左栏结构 | 搜索 + 可展开分类树（Python、Jupyter、Project Structure、Appearance & Behavior、Keymap、Editor、Color Scheme 等） | 搜索 + 4 项平铺（Appearance & Behavior 下 Appearance/File View/Git/Terminal） |
| 页面控件 | Theme + Sync with OS、Editor color scheme、Different tool window background、Accessibility（Zoom、Use custom font、屏幕阅读器、对比度滚动条、色觉）、UI Options（Compact mode、Alt 拖放、Smooth scrolling） | Theme、UI font/Size、Restore Reference Appearance、说明文字 |
| 底部 | OK / Cancel / Apply（Apply 禁用灰） | OK / Cancel / Apply（Apply 禁用灰） |
| 内容密度 | 分节线约 y=474/1001/1125 | 页面稀疏，无这些分节 |

## 4. 结构差异与范围判定

- 参考多出的分类和控件与 `ux-spec §7.17` 的“只包含外观与行为、文件查看、Git、终端；不显示插件、市场、键位、解释器、构建、调试和 AI 设置”冲突。按当前产品规范，**这些是明确的产品范围外内容**，不为了整页相似而新增。
- 因此本轮不能把设置页记为一个“合格整页配对”；它需要先定义比较合同：
  1. 只比较受支持控件的组件级视觉（主题/字体行、输入框、下拉、按钮、间距），结构差异登记为产品范围差异；或
  2. 如果要求整页结构相同，必须先由用户/规范扩大设置信息架构，这超出本轮授权。
- 已确认的共同点可作为后续组件级配对的基础：模态外壳、标题栏/关闭、搜索框、面包屑、左栏/右栏分界、底部三按钮、浅色主题和字体。

## 5. 下一步

1. 以本页“受支持控件”为集合，测量参考的主题下拉、UI 字体下拉、Size 字段、按钮高度/内距/圆角、行距和左右留白；实际只改这些受支持控件的视觉，不增加参考独有控件。
2. 需要决定比较合同（受支持控件组件级 vs 整页结构）后才能宣称设置像素合格；在此之前只记 scoping 完成。
3. 设置性能（慢开、闪烁、重复窗口、无 CDP 一致性）仍是独立优先级，不因本轮 scoping 改变。

## 6. 清理

- 实际 PID 7820 正常关闭；`settings.json` 从 `settings-before.json` 恢复，SHA256 `B8868855…DC3D`。
- 参考 PID 12652 经 Confirm Exit 关闭；参考环境的 Settings 窗口尺寸/位置可能保存在隔离配置中，不影响用户 PyCharm/用户文件。
- 本批未触碰用户原有 PyCharm；无 CDP 端口；无 `Augit`/`pycharm64` 残留。`cleanup-check-37.json`；未提交、未推送。
- 第 8 批：Augit PID 17416 正常关闭，设置恢复 `B8868855…DC3D`；本批未启动参考 PyCharm；`cleanup-check-38.json`。

## 7. 第 8 批：设置左栏宽度对齐（2026-10-18 20:08）

- 参考 Appearance 页左栏分隔线 443–444 物理；实际修复前 414–415。
- 修改 `docs/ux-mockups/mockup.css` 的 `.settings-layout` 末条网格：`minmax(190px, 230px)` → `minmax(190px, 246px)`。列宽取 246 是因为实际分隔线还带有既有 6–7 CSS 的内容偏移。
- 实际重采（PID 17416，同 1792×1225 物理）后分隔线 **442–443**，与参考差 -1 物理。
- 证据：`actual-246/foreground.png`（`de24feea…bcc6`）、`actual-246/appearance-1772.png`（`2325b657…61bc`）、三栏对照 `settings-nav-width-compare.png`（`52411b04…1584`）。
- 只对齐左栏宽度；分类树 vs 4 平铺、参考独有控件、控件密度、主题下拉宽度等仍在。仍不宣称设置页同态配对或像素合格。
- 验证：`verify-ui-assets.ps1` PASS（视觉稿与运行时字节一致）；构建 0 警告 0 错误。

## 8. 第 9 批：Appearance 受支持控件对齐（2026-10-18 20:10）

- 参考 Appearance 实测：标签列约 52 CSS（内容左缘 470 → 主题下拉 561，物理），主题下拉 **561–870 × 163–210**（309×47 物理 ≈ 177×27 CSS）。
- 实际修复前：标签列 110 CSS，主题下拉从 686 一直铺到页面右缘（≥1013 物理宽）；控件整体比参考右移 125 物理。
- 修改（仅 Appearance 页）：
  - `.form-grid` 标签列 `110px` → `42px`；
  - 主题下拉 `width: 180px`；
  - `.font-setting` `max-width: 560px`；
  - 空 label 后的说明文字跨两列，回到正文左缘。
- 实际重采（PID 23380，同 1792×1225 物理）：
  - 主题下拉 **567–873 × 189–241**（306×52 物理 ≈ 175×30 CSS）；起点差 +6 物理、宽度差 -3 物理。
  - UI font 下拉 567–1333，Size 输入 1424–1538；参考该行有 “Use custom font” 复选和独立 Size 下拉，属结构差异。
- 证据：`actual-controls/foreground.png`（`7c43fb20…71e7`）、`actual-controls/appearance-1772.png`（`f38b903d…4ac`）、对照 `appearance-controls-compare.png`（`26da06e0…ae14`）。
- 仍然存在：主题行整体比参考低约 26 物理（页面抬头/间距）、搜索框自动聚焦的蓝环、参考的 Use custom font 复选/Editor color scheme/无障碍、底部 footer 位置、整页空白。**仍不是同态配对，不记像素合格。**
- 验证：`verify-ui-assets.ps1` PASS；构建 0 警告 0 错误；Augit PID 23380 正常关闭，设置恢复 `B8868855…DC3D`；`cleanup-check-39.json`。

## 9. 第 10 批：设置页顶内距（2026-10-18 20:12）

- 参考：页面白底从原生标题栏下缘 90 物理开始，面包屑 100–121，主题下拉 163–210。
- 实际修复前：白底从 81 开始，但页顶 12 CSS 内距加 dialog-body 内距后，面包屑 131–152、主题下拉 189–241，主题行低 26 物理。
- 修改：`.settings-page` 的 `padding: 12px 16px` → `0 16px 12px`（共享设置页；左右 16 和底部 12 不变）。
- 实际重采（PID 9744，同 1792×1225）：
  - 面包屑 110–131（仍比参考低 10 物理，属原生标题栏/页面内距差异）；
  - 主题下拉 **168–220**，参考 163–210；顶部差从 +26 收敛到 **+5 物理**，起点/宽度不变（567–873 vs 561–870）。
- 证据：`actual-vertical/foreground.png`（`7afd2984…f315`）、`actual-vertical/appearance-1772.png`（`274a083a…1a704`）、对照 `appearance-vertical-compare.png`（`66f55194…b28877`）。
- 仍存在：搜索框自动聚焦蓝环、参考的 Use custom font 复选/Editor color scheme/无障碍/UI Options、底部 footer 与整页空白。**仍不是同态配对，不记像素合格。**
- 验证：`verify-ui-assets.ps1` PASS；构建 0 警告 0 错误；Augit PID 9744 正常关闭，设置恢复 `B8868855…DC3D`；`cleanup-check-40.json`。

## 10. 搜索框焦点状态（2026-10-18 20:17）

- 实际打开设置时搜索框自动聚焦，蓝环是 `:focus` 边框；参考 Settings 搜索框未聚焦、为灰边框。这是**状态差异**，不是搜索框样式错误。
- 来源：`web/src/live-data.js:13700` 在 `window/settings-present` 前主动 `focus()`，注释明确“焦点会改变合成表面，须纳入宿主最后一次渲染确认，不能在揭示后补做”；`:9567` 重复入口也复用同一焦点。**本批不修改**：它与设置揭示/白闪确认路径绑定，贸然移除会影响呈现证据。
- 测量：聚焦态搜索框蓝行 y=113、`#3871E1`；失焦态同区域蓝像素 0，边框回到灰色。
- 失焦对照：`actual-unfocused/appearance-1772.png`（`bee0cc21…c777`）、`appearance-unfocused-compare.png`（`e86533ae…b1d9`）；聚焦态为 `actual-vertical/appearance-1772.png`（`274a083a…1a704`）。
- 后续视觉对照必须标明焦点状态；失焦态更接近参考，但产品仍保留打开即聚焦。设置页仍不记同态配对。
- 清理：Augit PID 7392 正常关闭，设置恢复 `B8868855…DC3D`；`cleanup-check-42.json`。

## 11. 深色设置窗口 footer 越界：已更正为非干净开窗伪影（2026-10-18）

- 先前在终端深色配对中观察到设置底栏 OK/Cancel/Apply 在 UIA 中位于 `y≈1943`、屏幕外；当时设置窗口已被审计脚本移动/强制调整过尺寸。
- 2026-10-18 干净复测（正常入口，PID 24732，未移动窗口）：设置窗口 **1040×760 CSS（1820×1330 物理）**，深色预览后 UIA OK 在 **y=1446**，footer 在窗口内可见；截图 `artifacts/pixel-followup-2026-10-18/settings-dark-footer/settings-dark-probe.png`（`97ecefd3…0097c`）。UIA OK before/after 同为 `1889,1446`，主题值 Dark。
- 受控 resize 复测（2026-10-18，正常入口 PID 10384，深色预览）：
  - 自然窗口 1820×1330（1040×760 CSS）；`GetWindowLong` 样式 `0x14C80000`，**无 WS_THICKFRAME**，用户不能拖动改尺寸。
  - 请求 1792×1225：实际 1792×1225，UIA OK `y=1341`，footer 可见。
  - 请求 1792×884：实际被**最小高度钳到 1792×1120（640 CSS）**，UIA OK `y=1234`，footer 仍可见；截图 `settings-small-height/dark-1792x884.png`（`16772951…1b95a`）。
- 结论：`y≈1943` 在正常开窗和受控缩窗下均**不复现**，是审计强制尺寸/过渡态伪影；设置窗口有 640 CSS 最小高度且不可由用户缩放，footer 受最小尺寸保护。**不登记为产品缺陷，该 residual 关闭**；但其他屏幕/DPI 的整体设置仍按未验证处理。
- 该更正不改变设置页“仍未建立同态配对”的结论。
