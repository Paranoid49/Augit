# 03 · 编辑器标签（EditorTabs）与只读正文查看

本节要点：

1. **编辑器标签栏不是 `JTabbedPane`。** `TabbedPane.tabHeight=40`、`TabbedPane.tabSelectionArc=4`、`TabbedPane.tabInsets=0,12,0,12` 这三个键只被设置对话框等 `JTabbedPane` 的 LaF（`DarculaTabbedPaneUI`）与 Jewel Compose 桥读取，**不驱动编辑器标签栏**；编辑器标签栏由 `JBTabsImpl` 自绘，权威高度是「标签栏 42px（= 标签内容 41px + 顶部 1px 分隔线）」，标签左右内距由 `EditorTabs.tabInsets`（New UI 为 `-7,8,-7,8`）与 `EditorTabs.tabContentInsets*`（4/6px）叠加决定。
2. **New UI 下编辑器标签有两套互斥渲染**：expUI 经典版（标签平铺 + 选中标签底部 4px 圆角下划线，未选中标签文字按 `unselectedBlend` 向底色混色、图标按 `unselectedAlpha=0.75` 降透明）与 Islands 版（不画下划线，改为 28px 高圆角卡片，卡片左右内缩 4px）。两套由主题键 `Islands` 选择；PyCharm 2026.2.1 默认用哪套**需复核实**。
3. **默认溢出行为是单行横向滚动**（`UISettings.scrollTabLayoutInEditor` 默认 `true`，`ide.experimental.ui.editor.tabs.scrollbar` 默认 `true`），不是压缩、也不是换行；放不下的标签进入「更多」下拉，标签条上有 5px 细滚动条且只在鼠标位于标签区内时出现。
4. **状态机很短**：默认 / 悬停 / 选中（活动或非活动）/ 拖动落点。关闭按钮在新 UI 默认只在「选中、悬停、已修改、已固定」四种情况下出现，且鼠标移到关闭按钮上要驻留 150ms 才绘制。
5. **标签正文按需创建**：只有活动标签的正文被真正初始化（`EditorComposite.initDeferred` 在面板首次显示时才完成），其余标签先插入空面板并在选中时才加载；这正是 Augit「启动只创建活动标签」的上游机制。

---

## 0. 取证锚点与 UI 变体判定

| 项 | 值 | 来源 |
| --- | --- | --- |
| 仓库 | `JetBrains/intellij-community` | — |
| 提交 | `576e328` | — |
| `build.txt` | `263.SNAPSHOT` | `build.txt:1` |
| 目标 | PyCharm 2026.2.1 New UI（内部名 expUI） | — |

- New UI 在本 commit 是**默认值**：`earlyInitValue()` 返回 `!epIconMapperSuppressor.hasAnyExtensions()`，即除非有旧主题插件注册了图标映射抑制器，否则 New UI 打开。（来源：platform/platform-impl/src/com/intellij/ui/ExperimentalUIImpl.kt:42）
- `ExperimentalUI.isNewUI()` 委托给 `NewUiValue.isEnabled()`，键名为 `ide.experimental.ui`。（来源：platform/core-ui/src/ui/ExperimentalUI.kt:66；platform/util/src/com/intellij/ui/NewUiValue.java:20）
- New UI 下编辑器标签有两套渲染实现，**互斥**：
  - **expUI 经典**：`JBTabsImpl` + `JBEditorTabsBorder`（画下划线与边框）+ `JBDefaultTabPainter(EditorTabTheme)`。
  - **Islands（多岛）**：`IslandsTabPainter`，`paintUnderline()` 是空实现，改为绘制圆角矩形卡片。
- Islands 由主题键 `Islands` 打开：`IslandsUICustomization.isManyIslandEnabled` 读 `JBUI.getInt("Islands", 0) == 1`；只有 `ManyIslandsLight/Dark/Darcula` 三个主题设置了 `"Islands": 1`，expUI 主题不设置。（来源：platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsUICustomization.kt:142；platform/platform-resources/src/themes/islands/ManyIslandsLight.theme.json:414）
- 切换时替换三个 painter adapter（编辑器 / 通用 / 调试器）。（来源：platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsUICustomization.kt:366）
- ⚠ **需复核实**：PyCharm 2026.2.1 的默认配色方案是哪一套（`Dark`/`Light` 即 expUI，还是 `Islands Dark`/`Islands Light`）。两套的标签几何（42/28）与状态色不同，必须先确认再定稿视觉。

---

## 1. 标签栏与标签几何

### 1.1 权威取值（主题层）

| 键 | 浅色（expUI_light） | 深色（expUI_dark） | 来源 |
| --- | --- | --- | --- |
| `TabbedPane.tabHeight` | `40` | `40` | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:649 |
| `TabbedPane.tabSelectionArc` | `4` | `4` | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:650 |
| `TabbedPane.tabInsets` | `0,12,0,12` | `0,12,0,12` | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:654 |
| `TabbedPane.hoverColor` | `Gray12` | — | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:651 |
| `TabbedPane.contentAreaColor` | `Gray12` | — | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:652 |
| `TabbedPane.focusColor` | `Blue11` | — | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:653 |
| `EditorTabs.underlineHeight` | `4` | `4` | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:319 |
| `EditorTabs.underlineArc` | `4` | `4` | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:318 |
| `EditorTabs.unselectedAlpha` | `0.75` | `0.75` | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:320 |
| `EditorTabs.unselectedBlend` | `0.9` | `0.7` | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:321；platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:330 |
| `EditorTabs.background` | `Gray14` | — | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:322 |
| `EditorTabs.hoverBackground` | `#FFFFFF00`（**全透明**） | — | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:323 |
| `EditorTabs.underlinedTabBackground` | `Gray14` | — | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:326 |
| `EditorTabs.inactiveColoredFileBackground` | `#FFFFFF80` | — | platform/platform-resources/src/themes/expUI/expUI_light.theme.json:325 |

- `EditorTabs.underlineHeight` 的**兜底**来自 `DefaultTabs.underlineHeight`（未设时默认 3），New UI 主题显式写成 4。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:662；platform/util/ui/src/com/intellij/util/ui/JBUI.java:593）
- `EditorTabs.underlineArc` 的**兜底是 0**，但 New UI 下由代码打补丁成 4（`putDefaultsIfAbsent` 只在 `ExperimentalUI.isNewUI()` 时生效）。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:670；platform/platform-impl/src/com/intellij/ide/ui/UIThemeBean.kt:472）
- `EditorTabs.unselectedAlpha` / `unselectedBlend` 的**兜底都是 1.0**，即不设主题键时完全不降透明。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:762；platform/util/ui/src/com/intellij/util/ui/JBUI.java:766）
- Islands 主题在这批键之上追加了：`underTabsBorderColor`、`underlinedBorderColor`、`inactiveUnderlinedTabBorderColor`、`underlinedTabBackground`、`inactiveUnderlinedTabBackground`、`tabInsets: "-6,8,-6,8"`、`tabInsets.compact: "-2,6,-2,4"`、`verticalTabInsets: "-6,8,-6,8"`、`verticalTabInsets.compact: "-2,10,-2,10"`、`tabContentActionsRightInsets: "0,4,0,2"`。（来源：platform/platform-resources/src/themes/islands/ManyIslandsLight.theme.json:664）
- Islands 主题还多一个 `TabbedPane.tabContentMinWidth: 24`（expUI 没有），由 `IslandsUICustomization` 读取。（来源：platform/platform-resources/src/themes/islands/ManyIslandsLight.theme.json:1038；platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsUICustomization.kt:1282）
- `.compact` 后缀的键**只在 UI 密度为 COMPACT 时覆盖基础键**（LaF 加载时整体重映射）。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1480；platform/editor-ui-api/src/com/intellij/ide/ui/UISettings.kt:261）【可直接实现】

### 1.2 `TabbedPane.*` 不是编辑器标签栏的几何来源（重要纠偏）

- `TabbedPane.tabHeight` 在代码里只有两个消费者：`DarculaTabbedPaneUI`（`JTabbedPane` 的 LaF）与插件管理器的 `TabbedPaneHeaderComponent`。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/darcula/ui/DarculaTabbedPaneUI.java:75；platform/platform-impl/src/com/intellij/ide/plugins/newui/TabbedPaneHeaderComponent.java:120）
- `TabbedPane.tabSelectionArc` 只被 `DarculaTabbedPaneUI.paintUnderline` 与 Jewel 桥使用。`JBUI` 里的默认值是 **32（tabHeight）与 0（arc）**，比主题值更旧。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1747；platform/util/ui/src/com/intellij/util/ui/JBUI.java:1752）
- Jewel Compose 桥把 `TabbedPane.*` 映射到 Compose 的 `TabMetrics`：下划线厚度取 `TabbedPane.tabSelectionHeight`（未设时 **2dp**）、内距取 `TabbedPane.tabInsets`（未设时左右 8dp）、`closeContentGap` 与 `tabContentSpacing` 恒为 **4dp**、高度取 `TabbedPane.tabHeight`（未设时 **24dp**）；编辑器标签样式把内容不透明度设为 0.7、选中/悬停/按下为 1.0。（来源：platform/jewel/ide-laf-bridge/src/main/kotlin/org/jetbrains/jewel/bridge/theme/IntUiBridgeTab.kt:46；platform/jewel/ide-laf-bridge/src/main/kotlin/org/jetbrains/jewel/bridge/theme/IntUiBridgeTab.kt:100）【Swing 特有（Compose）】
- 结论：**Augit 的编辑器标签栏应实现 §1.3 的高度公式，而不是 40px。**（需推断）

### 1.3 标签栏高度（权威公式）

- 标签栏高度 = 所有标签 `preferredSize.height` 的最大值 **+ `tabBorder.thickness`（1px）**。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:2640）
- 编辑器标签的高度被 `EditorTabLabel` 硬覆盖为：
  `height = scale(UNSCALED_PREF_HEIGHT − insets.top − insets.bottom) − layoutInsets.top − layoutInsets.bottom`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:807）
- `UNSCALED_PREF_HEIGHT = 28`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/SingleHeightTabs.kt:15）
- 横向标签的 `insets` 取 `EditorTabs.tabInsets`，其 New UI 兜底值（无任何内置主题覆盖它）是 `JBUI.insets(-7, 8)`，即 top/bottom = **−7**、left/right = **8**。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:682）【需复核实：负内距的语义是「标签内容比 28 更高」，合并时除 −1 外不做特殊处理，见 platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:924】
- `layoutInsets = tabBorder.effectiveBorder`，编辑器标签的边框是 `Insets(thickness, 0, 0, 0)`，`thickness = 主题 topBorderThickness`，New UI 为 `scale(1)`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:2349；platform/platform-api/src/com/intellij/ui/tabs/impl/JBEditorTabsBorder.kt:73）
- 代入 1x 缩放：`scale(28 − (−7) − (−7)) = 42`，再减 `layoutInsets` 的上下（本定位下只有 top = 1）→ 标签高 **41**；标签栏高 = 41 + 1 = **42**。（来源：上述四处公式联立）【需推断：本项为公式代入值，未在源码中写死】
- 侧边放置时改用 `EditorTabs.verticalTabInsets`（New UI 兜底 `insets(-2, 8)`）。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:690；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:698）
- 标签栏高度还有一个下限 `minHeaderHeight()`，编辑器取 `ToolWindowHeader.getUnscaledHeight()`：New UI 用 `ToolWindow.headerHeight`，否则用 `SingleHeightTabs.UNSCALED_PREF_HEIGHT = 28`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:655；platform/platform-impl/src/com/intellij/toolWindow/ToolWindowHeader.kt:75）
- **Augit 对照**：`web/src/mockup.css` 的 `--augit-tab-height: 42px` 与该公式一致（`.editor-tab` 高 28px 则对应 Islands 的卡片内高）。（需推断）

### 1.4 标签宽度、间距与最小宽度

- **标签间重叠 1px，不是间隙**：`tabHGap = -tabBorder.thickness`（New UI = −1），布局时 `position += 上一个标签宽 + tabHGap`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:3496；platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayout.java:212）【可直接实现：CSS 用 `margin-right: -1px`】
- **编辑器标签最小宽度 50px**：`SingleRowLayoutStrategy.Horizontal.getLengthIncrement` 对编辑器标签取 `max(标签 preferredWidth, MIN_TAB_WIDTH)`，`MIN_TAB_WIDTH = 50`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayoutStrategy.java:22；platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayoutStrategy.java:152）【可直接实现：CSS `min-width: 50px`】
- **固定标签最大宽度 2000px**（可配置）：`TabLayout.getMaxPinnedTabWidth()` 读 `ide.editor.max.pinned.tab.width`（默认 `2000`），在 `TabLabel.getPreferredSize` 里对 pinned 标签取 `min(2000, 原宽)`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLayout.java:61；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:299；platform/util/resources/misc/registry.properties:169）
- **标签内距（New UI）**：标签自身边框 = `labelInsets` = `(top −7, left 8, bottom −7, right 8)`；内容占位区再叠一层 `contentInsets`：
  - 关闭按钮在右侧：`EditorTabs.tabContentActionsRightInsets`（New UI 兜底 `0,4,0,6`）
  - 关闭按钮在左侧：`EditorTabs.tabContentActionsLeftInsets`（New UI 兜底 `insetsLeft(6)`）
  - 无标签动作：`EditorTabs.tabContentActionsNoneInsets`（New UI 兜底 `insetsLeft(4)`）
  （来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:698；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:614；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:576）
  → 文字相对标签左边缘的有效留白 = 8 + 4 = **12px**（与主题键 `TabbedPane.tabInsets=0,12,0,12` 的观感一致，但来源不同）。【需推断】
- **图标与文字间距 4px**：编辑器标签的 `iconTextGap = JBUI.scale(4)`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:621；默认装饰器同值 platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:3352）
- **标签文字水平居中**：横向编辑器标签在 `applyTabLayout` 里被设置 `setAlignmentToCenter(true)`（`isToCenterTextWhenStretched()` 横向返回 true），所以在被拉到最小宽度 50px 时文字居中而不是贴左。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayout.java:243；platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayoutStrategy.java:96）【可直接实现：`justify-content: center`】
- **标签字体**：New UI 下 `useSmallLabels()` 恒为 false（小字号是旧 UI 特性），字体取 `EditorTabs.font()` = 标准标签字体按 `EditorTabs.fontSizeOffset`（默认 0）放大/缩小。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBEditorTabs.kt:69；platform/util/ui/src/com/intellij/util/ui/JBUI.java:750；platform/util/ui/src/com/intellij/util/ui/JBUI.java:754）
- 标签文字内容 = `VirtualFile.presentableName`（默认是去掉扩展名的文件名）；可在设置里切换为保留/隐藏已知扩展名。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:282；platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:137）【可直接实现】
- 文件类型图标由 `showFileIconInTabs`（默认 `true`）控制；插入标签时先用一个同尺寸的空图标占位，图标异步算出来后替换。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:284；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:352；platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:136）【可直接实现】

### 1.5 关闭按钮（几何、可见性、命中区）

- 图标尺寸：关闭/修改点统一按 **13×13（scaled）** 绘制；修改点圆直径 **6px**、内缩 **3.5px**（居中）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:178；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:183）
- 关闭图标两态：常规 / 悬停各一个 16×16 图标资源，按悬停切换。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:203；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:206）【可直接实现：Augit 用自绘 SVG 的两态】
- **可见性（New UI）**：关闭按钮显示的充要条件是 `showCloseButton（默认 true）|| 已固定 || (New UI && 已修改)`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:66）
- **未选中且未悬停的标签不画关闭按钮**：`ActionPanel.paint` 在 New UI + 编辑器标签 + 非选中 + 非悬停 + 非已修改 + 非已固定时直接 `return`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/ActionPanel.java:91）
- 关闭按钮可以放在标签左侧（系统属性/设置切换），默认在右侧。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/ActionPanel.java:53；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:817；platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:130）【可直接实现】
- **悬停延迟 150ms**：关闭按钮是 `InplaceButton`，其鼠标死区用 `TimedDeadzone.DEFAULT = 150ms`，只有鼠标进入后驻留时间小于该窗口才认为「在按钮上」并绘制悬停态。（来源：platform/platform-api/src/com/intellij/util/ui/TimedDeadzone.java:9；platform/platform-api/src/com/intellij/ui/InplaceButton.java:64；platform/platform-api/src/com/intellij/util/ui/BaseButtonBehavior.java:230）【可直接实现：等价于「指针停住约 0.15s 后按钮才浮现」，Augit 若做 `:hover` 立即出现则构成偏离（需推断）】
- **命中区**：关闭按钮是独立的 `InplaceButton` 子组件，压在标签右侧；点标签时若最深命中组件是 `InplaceButton` 则不触发选中。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:135）
- 关闭按钮在同一标签上最多一个；标签上的动作按钮来自 `EditorTabActionGroup`，其最后一个动作即 `CloseTab`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:280；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:722）

### 1.6 省略规则

- **标签标题不做中间省略号**：在 `platform/platform-impl/.../fileEditor/impl/` 与 `platform/platform-api/.../ui/tabs/` 内检索不到任何 `shortenTextWithEllipsis` / 省略号处理。文字按 `presentableName` 原样设置，宽度不足时直接裁切。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:282；全目录检索无匹配）【需复核实】
- **右边缘用 10px 渐隐代替省略号**：当内容宽度小于「占位区 preferredWidth + `tabHGap`」时，在右侧绘制一段渐变（`ide.editor.tabs.fadeout.width` 默认 **10**），从标签底色渐变到透明。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:368；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:387；platform/util/resources/misc/registry.properties:175）
- 渐隐可整体关闭：`ide.editor.tabs.show.fadeout` 默认 `true`；编辑器标签另外要求 `shouldPaintFadeout()`（非 simplified 模式 + 单行布局）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:820；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:366）
- 左侧渐隐只在标签被滚动到负坐标（部分移出可见区）时出现；滚动布局下最右侧标签也各自渐隐。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:380；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:403）
- **极小宽度时裁图标**：压缩布局下标签宽度小于 `MIN_WIDTH_TO_CROP_ICON = 39` 时，图标宽度按超出量削减，但不少于图标原宽的一半。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:555；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:903）
- **与 Augit 现状的差异（已核对 `web/src/mockup.css`）**：
  - Augit 的普通编辑器标签（`.editor-tab`）**没有**省略号规则，标题靠 `max-width: 230px` + 溢出裁切，方向与 IntelliJ 一致；但 IntelliJ 对**未固定标签没有最大宽度**，只靠滚动，且裁切边缘有 10px 渐隐。**Augit 缺渐隐层**。（来源：web/src/mockup.css:2344）
  - Augit 的**比较标签**（`.comparison-tab` 内的 `.comparison-file` / `.comparison-revision`）使用了 `text-overflow: ellipsis`，会产生「…」；IntelliJ 无此表现。（来源：web/src/mockup.css:2370）
  - Augit 的 `.editor-tab` 还有 `border-radius: 6px`、`gap: 8px`、`padding: 0 8px`；其中圆角只在 Islands 渲染下成立（`MainToolbar.Button.arc` = 12），expUI 经典渲染的标签是**无圆角的平铺矩形**。**需按 §0 结论二选一。**（来源：web/src/mockup.css:2340）【需复核实】

---

## 2. 状态机：默认 / 悬停 / 选中 / 失焦 / 按下

### 2.1 状态取值来源（expUI 经典）

选中标签的前景/底色/下划线色统一由 `EditorTabTheme` 给出，且会被配色方案（`EditorColors`）覆盖：

| 语义 | 取值链 | 来源 |
| --- | --- | --- |
| 标签栏底色 | `EditorTabs.background` → `DefaultTabs.background` | platform/platform-api/src/com/intellij/ui/tabs/impl/themes/TabTheme.kt:64；platform/util/ui/src/com/intellij/util/ui/JBUI.java:726 |
| 标签栏下边框色 | `EditorTabs.underTabsBorderColor` → `EditorTabs.borderColor` → `DefaultTabs.borderColor` → `JBColor.border()` | platform/platform-api/src/com/intellij/ui/tabs/impl/themes/TabTheme.kt:67；platform/util/ui/src/com/intellij/util/ui/JBUI.java:722 |
| 选中下划线色（活动） | 配色方案 `TAB_UNDERLINE` → `EditorTabs.underlineColor` → `DefaultTabs.underlineColor` | platform/platform-api/src/com/intellij/ui/tabs/impl/themes/TabTheme.kt:70；platform/util/ui/src/com/intellij/util/ui/JBUI.java:658 |
| 选中下划线色（失焦） | 配色方案 `TAB_UNDERLINE_INACTIVE` → `EditorTabs.inactiveUnderlineColor` → `DefaultTabs.inactiveUnderlineColor` | platform/platform-api/src/com/intellij/ui/tabs/impl/themes/TabTheme.kt:73；platform/util/ui/src/com/intellij/util/ui/JBUI.java:674 |
| 选中标签底（活动） | 配色方案 `TAB_SELECTED.background` → `EditorTabs.underlinedTabBackground` | platform/platform-api/src/com/intellij/ui/tabs/impl/themes/TabTheme.kt:76 |
| 选中标签底（失焦） | 配色方案 `TAB_SELECTED_INACTIVE.background` → 同活动值 | platform/platform-api/src/com/intellij/ui/tabs/impl/themes/TabTheme.kt:100 |
| 选中标签前景 | 配色方案 `TAB_SELECTED.foreground` → `EditorTabs.underlinedTabForeground` → `DefaultTabs.underlinedTabForeground` | platform/platform-api/src/com/intellij/ui/tabs/impl/themes/TabTheme.kt:79；platform/util/ui/src/com/intellij/util/ui/JBUI.java:746 |
| 未选中悬停底（活动） | `EditorTabs.hoverBackground` → `DefaultTabs.hoverBackground` | platform/util/ui/src/com/intellij/util/ui/JBUI.java:730 |
| 未选中悬停底（失焦） | `EditorTabs.hoverInactiveBackground` | platform/platform-api/src/com/intellij/ui/tabs/impl/themes/TabTheme.kt:91 |
| 选中时悬停底 | `EditorTabs.hoverSelectedBackground` / `hoverSelectedInactiveBackground`，**默认全透明** | platform/util/ui/src/com/intellij/util/ui/JBUI.java:734 |
| 带色标签（非选中） | `EditorTabs.inactiveColoredFileBackground` 与标签色做 alpha 混合 | platform/platform-api/src/com/intellij/ui/tabs/impl/JBDefaultTabPainter.kt:28 |
| 带色标签（选中） | 直接用标签色 | platform/platform-api/src/com/intellij/ui/tabs/impl/JBDefaultTabPainter.kt:40 |

- **按下态没有独立颜色**：`TabTheme` 与 `JBDefaultTabPainter` 里没有 pressed 分支，按下与悬停同色；按下唯一的可见结果是鼠标释放后完成选中。Islands 侧同样没有 pressed 分支。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBDefaultTabPainter.kt:24；platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:254）【可直接实现】
- 带色标签的背景合成顺序（非选中）：先 `alphaBlending(inactiveColoredFileBackground, tabColor)`，再叠悬停色。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBDefaultTabPainter.kt:24）【可直接实现】

### 2.2 悬停判定

- 悬停标签是**单选**：`JBTabsImpl` 只保存一个 `tabLabelAtMouse`；设置新值时会 `revalidate + repaint` 旧标签和新标签。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:780）
- 离开时只在「当前悬停的就是自己」时清空，避免快速移动时误清新悬停标签。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:793）
- **打开右键菜单的标签也按悬停处理**：`isHoveredOrWithPopup(label) = (label === tabLabelAtMouse) || (popupInfo === label.info)`，因此右键菜单弹出期间该标签保持降透明解除与关闭按钮可见。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:804）【可直接实现】
- 关闭按钮悬停也会把其宿主标签标记为悬停（`ActionButton` 把 `mouseEntered/Exited` 转成 `isHovered`）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/ActionButton.java:68）【可直接实现】

### 2.3 未选中标签的「变暗」

- 触发条件：`New UI && 当前标签未被选中 && 未被悬停（也不在右键菜单中）`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:836）
- **文字**：不整体降 alpha，而是把前景色与标签实际底色按 `EditorTabs.unselectedBlend` 做 RGB 混合（浅色 0.9 / 深色 0.7）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:824；platform/util/ui/src/com/intellij/ui/ColorUtil.java:421 的 `blendColorsInRgb`）
- **图标**：按 `EditorTabs.unselectedAlpha`（0.75）绘制半透明图标。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:834；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:261）
- 混合用的底色是 `effectiveBackground` = `getCustomBackground(...)` 与标签栏底色再做一次 alpha 混合的结果，保证带色标签的混色基准正确。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:708）【需推断】
- 只有活动窗口的标签才用「活动」配色：`isActiveTabs()` 由编辑器标签容器自己维护（聚焦事件触发，`SwingUtilities.invokeLater` 再校正一次）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:735；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:749）【可直接实现：`document.hasFocus()`】

### 2.4 选中下划线

- 下划线**不由标签自己画**，而是由标签栏边框在画完所有标签后统一绘制在选中标签的 `bounds` 上。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBEditorTabsBorder.kt:125）
- 几何：`Rectangle(x = 标签.x, y = 标签.y + 标签.高 − 厚度, 宽 = 标签宽, 高 = underlineHeight)`，即**贴标签底边、通铺整宽**；`underlineArc > 0` 时用圆角填充，否则直角。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBDefaultTabPainter.kt:92；platform/platform-api/src/com/intellij/ui/tabs/impl/JBDefaultTabPainter.kt:73）【可直接实现：`border-bottom: 4px solid` + `border-radius` 或 `::after` 绝对定位条】
- 颜色按窗口活动性二选一（活动 = `underlineColor`，失焦 = `inactiveUnderlineColor`）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBDefaultTabPainter.kt:80）
- **切换动画默认关闭**：`ide.editor.tab.selection.animation` 默认 `false`；打开后下划线的左右两条边分别用 100ms 动画过渡，位移方向上的那条边晚 50ms 并改用 EASE_OUT，另一条 0 延迟线性。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBEditorTabsBorder.kt:39；platform/platform-api/src/com/intellij/ui/tabs/impl/JBEditorTabsBorder.kt:143；platform/util/resources/misc/registry.properties:2147）【可直接实现：CSS transition】
- 动画期间如果标签是新增/删除的（宽度为 0）则跳过动画，避免从错误位置起飞。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBEditorTabsBorder.kt:38）
- 左下角「更多」下拉里，当前选中项在列表项左侧也画一条同色竖条：内缩 2px、宽 = `underlineHeight`、高 = 行高 − 2×2，圆角 4，列表项内距 `0,9,0,3`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:1143）【可直接实现】

### 2.5 标签栏下边框

- New UI 下，标签栏顶部先画一条 `MainToolbar.borderColor` 的水平线。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBEditorTabsBorder.kt:79）
- 每一行标签底部各画一条分隔线（多行时每行都有）；New UI 跳过第 0 行（与上边框重复），最后一行是否画取决于 `shouldPaintBottomBorder()`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBEditorTabsBorder.kt:96）
- 编辑器标签的 `shouldPaintBottomBorder()`：选中标签的正文自带宽边框时返回 false；否则交给 `InternalUICustomization`；Islands 下再要求「面包屑不在上方且没有查找替换栏」。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:686；platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsUICustomization.kt:852）【Swing 特有】

### 2.6 Islands（多岛）渲染覆盖

- 主题覆盖：`underlineHeight = 0`、`underlineColor = background`、`inactiveUnderlineColor = background`、`hoverBackground = background`，并禁用所有 `underlinedTab*` 背景 —— 结果是**不画下划线**，选中状态靠卡片底色与描边表达。（来源：platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:88）
- 卡片几何：
  - `fullHeight = 28`（compact 24）；`minVOffset = 8`（侧边 6，compact 4）
  - `vOffset = max(标签高 − fullHeight, minVOffset)`；`y = floor(标签.y + vOffset / 2)`
  - `hOffset = 4`（侧边 6，compact 2）；`x = 标签.x + hOffset`；`width = 标签宽 − 2 × hOffset`；`height = 标签高 − vOffset`
  （来源：platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:168；platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:270）【可直接实现】
- 圆角 = `MainToolbar.Button.arc`（内置默认 12；Islands 主题只覆盖紧凑变体 `MainToolbar.Button.arc.compact = 8`）。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1419；platform/platform-resources/src/themes/islands/ManyIslandsLight.theme.json:1196）
- 状态色（选中优先，其次悬停，最后默认）：
  - 选中 + 活动：底 `EditorTabs.underlinedTabBackground`，边 `EditorTabs.underlinedBorderColor`
  - 选中 + 失焦 + 悬停：底 `EditorTabs.hoverBackground`，边 `EditorTabs.inactiveUnderlinedTabBorderColor`
  - 选中 + 失焦：底 `EditorTabs.inactiveUnderlinedTabBackground`，边 `inactiveUnderlinedTabBorderColor`
  - 悬停：底 `EditorTabs.hoverBackground`（内置兜底 `#E5EEFF80` / `#343E5180`），边 `EditorTabs.hoverBorderColor`
  - 默认：底 `EditorTabs.regularBackground`，边 `EditorTabs.regularBorderColor`
  （来源：platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:227；platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:254）
- 卡片 = 填充 + 描边两步绘制（同一个圆角矩形）。（来源：platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:185）【可直接实现】
- 带色标签：非选中时底色 = 状态底色与 `withAlpha(tabColor, 0.4)` 做 alpha 混合；选中时先在卡片内缩 3px（compact 2px）再画一层内圆角矩形（内圆角 = `arc − offset − 2`，下限 0），活动用原色、失焦用 0.4 alpha。（来源：platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:118；platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:188；platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:247）【可直接实现】
- Islands 把第一个标签起点设为 `firstTabOffset = 3px`，卡片宽度用「所有子组件 bounds 的并集 + 右内距」反推（因为 squeeze 模式下 `preferredSize` 不可靠）。（来源：platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:278；platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:71）【Swing 特有】
- 带色标签在 Islands 下参与渐隐时需要「合成后的不透明底色」，由 `getEditorTabComposedBgColor` 提供。（来源：platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt:209）【Swing 特有】

### 2.7 焦点提示

- 标签只有在「自己是选中标签且组件可聚焦」（屏幕阅读器开启）时才接受焦点；获得焦点时在**文字区域**画一圈点线矩形（不是整标签）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:235；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:724）
- 键盘左右方向键在焦点位于标签上时切换上一个/下一个可用标签，并把焦点移到新选中标签。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:173）【可直接实现】

---

## 3. 选择与关闭

### 3.1 鼠标交互总表

| 操作 | 行为 | 来源 |
| --- | --- | --- |
| 左键单击 | 选中该标签并把焦点交给正文 | platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:130 |
| 左键单击前的判定 | 「选择点击」= `clickCount == 1` 且非右键弹出触发、`button == BUTTON1`、且**没有** Ctrl/Alt/Meta | platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:271 |
| 非选择点击 | 走 `handlePopup`（右键上下文菜单），不改选中 | platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:146 |
| Ctrl+左键单击（Windows） | 在资源管理器中显示该文件 | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:456 |
| 双击（标签上） | 若该标签是预览标签 → 转为正式标签并返回；否则执行「最大化编辑器」（高级设置默认 true） | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:348；platform/platform-impl/resources/intellij.platform.ide.impl.xml:1505 |
| 双击计数 | 点击关闭按钮不计入双击计数（避免「点两次 X」被识别为双击标签） | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:446 |
| 中键单击 / Shift+左键 | 关闭该标签；中键与 Alt+左键组合时关闭「其他所有标签」 | platform/util/ui/src/com/intellij/util/ui/UIUtil.java:1843；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:418 |
| 中键按下时 | 标签自身在 `mousePressed` 阶段直接返回，**不会顺带切换选中** | platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:124 |
| 关闭按钮单击 | 关闭该文件；若该标签已固定且 registry `ide.editor.tabs.interactive.pin.button`（默认 true）→ 改为**取消固定** | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:98；platform/util/resources/misc/registry.properties:171 |
| 关闭按钮 + Alt | 关闭除该文件外的所有**未固定**文件 | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:114 |
| 关闭后仍显示「更多」下拉 | 若关闭操作来自隐藏标签列表，关闭后重新弹出该列表 | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:130 |
| 隐藏标签列表内 Shift+单击 | 关闭该标签并保持列表打开 | platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:1376 |

- 关闭动作的提示文案是 `Alt+`（mac 为 `⌥`），快捷键继承 `CloseContent`；当文件已修改但关闭按钮被隐藏时，关闭动作被**禁用**（点是蓝点，不是 X）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:81；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:96）

### 3.2 关闭后的选中转移

- `JBTabsImpl` 层的兜底规则：被关标签若不是当前选中、或它不是可见标签、或可见标签只剩 1 个 → **不转移**；否则**优先取左侧最近的可选标签，没有才取右侧最近的**（都不循环）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:1974）
- `EditorWindow` 层的规则**优先于**上面：`computeIndexToSelect` 先看「关闭的是不是当前选中标签」——不是则保持当前选中不动；是则按 `activeMruEditorOnClose`（默认 false）→ 历史中最近访问的文件，否则默认 **index − 1**（左邻居），被关的是第一个标签时返回 −1（无选中）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:817；platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:156）
- 还存在「上一个选中标签」优先：`getTabToSelect` **先**取被关标签的 `previousSelection`，只有当它仍在可见列表中才用它，否则才走 `computeIndexToSelect`。但该字段是在**鼠标按下**某标签时写入、在**鼠标释放**时清空，所以主要覆盖「按住不放 / 程序化切换 / 拖出」这几类时序，普通点击后再关闭时通常已为 null。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:741；platform/platform-api/src/com/intellij/ui/tabs/TabInfo.kt:148；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:133；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:156）【需复核实：普通点击场景下的实际生效时序未实测】
- 转移焦点：如果关闭前焦点在被关标签的内容里，选中新标签后要请求焦点；否则不抢焦点。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:2696）
- **关闭最后一个标签**：可见标签为空时立即清理延迟移除队列（`removeDeferredNow`）；编辑器窗口在 `removeIfEmpty` 中自我移除，主分屏变空则显示空状态组件（欢迎/快捷键提示），新组件加入时销毁它。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:2707；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:766；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorsSplitters.kt:1121；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:776）【可直接实现】
- 标签栏隐藏条件：标签位置设为 `NONE`、演示模式、或「浮动窗口 + 仅一个标签 + 该编辑器是 singleton」。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:598；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:638）【可直接实现】

### 3.3 批量关闭语义

| 动作 | 语义 | 来源 |
| --- | --- | --- |
| Close All Tabs | 有上下文窗口时只关该窗口，否则关所有窗口 | platform/platform-impl/src/com/intellij/ide/actions/CloseAllEditorsAction.java:30 |
| Close Other Tabs | 除当前选中；窗口分支还额外排除固定标签 | platform/platform-impl/src/com/intellij/ide/actions/CloseAllEditorsButActiveAction.java:53 |
| Close All but Pinned | 关闭所有未固定标签 | platform/platform-impl/src/com/intellij/ide/actions/CloseAllUnpinnedEditorsAction.java:19 |
| Close Unmodified Tabs | 未固定且文件状态「未修改」，且要求项目启用了 VCS | platform/vcs-impl/src/com/intellij/ide/actions/CloseAllUnmodifiedEditorsAction.java:17 |
| Close Tabs to the Left/Right | 相对被右击标签的左右邻居且非固定 | platform/platform-impl/src/com/intellij/ide/actions/CloseAllEditorsToTheLeftAction.java:26 |
| Close All Read-Only Tabs | 非固定且文件不可写 | platform/platform-impl/src/com/intellij/ide/actions/CloseAllReadonlyEditorsAction.java:12 |

- 所有批量关闭都先过滤再统一走 `closeFilesWithChecks`，被拒绝的文件只跳过自己。（来源：platform/platform-impl/src/com/intellij/ide/actions/CloseEditorsActionBase.java:60）【可直接实现】
- 「已修改」判定用 `FileStatusManager` 的文件状态，不是 document 的脏标志。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/FileEditorManagerImpl.kt:2070）【可直接实现】

### 3.4 已修改标签的视觉

- New UI：把关闭图标替换为**蓝点**（直径 6px、内缩 3.5px、13×13 图标框）；已固定且已修改 → pin 图标右上角叠一个半径 `3 / pinIconWidth` 的徽标点（相对坐标 0.7, 0.2）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:158）
- 该蓝点**不可点**（关闭动作被限制），因此「不显示关闭按钮」时已修改标签无法用按钮关闭，只能用快捷键/菜单/中键。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:96）【可直接实现】
- 可以改为在标题后加星号（`markModifiedTabsWithAsterisk`，默认 false）。（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:215）
- Augit 是只读查看器，不存在「已修改」状态；这条只作为**映射依据**：Augit 直接用 `UISettings.showCloseButton` 的默认 `true` 分支，即关闭按钮恒可见。（需推断）

### 3.5 超出标签上限时的自动关闭顺序

- 上限 `editorTabLimit` 默认 **30**。（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:68）
- 顺序：① 若焦点仍在被关编辑器内且未修改，先关它（受 `reuseNotModifiedTabs` 控制，默认 false）；② **关闭所有预览标签**；③ 再按「不在历史里 → 未修改 → 历史中较久未用的 → 标签顺序」依次关闭，当前选中标签最后。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:978；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:1013）
- 固定标签与不可关闭标签始终排除。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:1092）【可直接实现】

### 3.6 关闭后可重开（Augit 可选的补救通道）

- 关闭标签时会把 `(文件, 原 index, 是否 pinned)`（`RemovedTabInfo`）压入「已关闭栈」，超出 tabLimit 时丢最早的；重开动作优先按该栈恢复（含原位置与固定状态）并选中+抢焦点。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:697；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:119）
- 栈为空时才退回「历史列表中最后一个文件」。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/ReopenClosedTabAction.kt:31）
- 关闭时若焦点原先在被关标签的正文里，会把焦点交给替代标签；否则不抢焦点。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:2696）【可直接实现】
- Augit 有「最近关闭的文件」需求时可直接照搬这个栈语义。（需推断）

---

## 4. 拖放与重排

### 4.1 触发阈值

- **拖动启动死区 7px**（scaled）：鼠标按下后位移小于该距离不进入拖动。（来源：platform/platform-api/src/com/intellij/ui/MouseDragHelper.java:32；platform/platform-api/src/com/intellij/ui/MouseDragHelper.java:283）
- 拖动的对象是**按下时那个标签**，不是当前选中标签（选中变化可能重排标签）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/DragHelper.java:156）
- 拖动跟随鼠标：被拖标签自身 bounds 的 x（横向）或 y（纵向）直接跟随指针，另一个轴保持。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:2281）
- 拖动前会克隆指针相对标签左上角的偏移（`myHoldDelta`），保证「抓哪跟哪」。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/DragHelper.java:178）
- 需要按 Alt 才能拖动是可配置项：若打开 `dndWithPressedAltOnly`，没有 Alt 时忽略拖动。（来源：platform/platform-api/src/com/intellij/ui/MouseDragHelper.java:67）【可直接实现】
- 编辑器标签默认允许拖动（`isTabDraggingEnabled = true`）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:586）

### 4.2 插入位置与落点指示

- 目标标签通过「被拖矩形与候选标签矩形的重叠面积最大」选出，且候选标签在拖动轴上的长度必须大于「已让出的空隙 + 30%」。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/DragHelper.java:256）
- 插入下标由布局给出：横向时先看指针是否落在两个标签之间的空隙，落在标签上则取该标签下标；被拖标签自身已占据的位置要减 1 修正。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayout.java:273）
- 指针在标签行上方/下方一定范围内仍算「在行内」：容差 `UNSCALED_DROP_TOLERANCE = 15px`，超出容差才判定为拖出。（来源：platform/platform-api/src/com/intellij/ui/tabs/TabsUtil.java:28；platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayoutStrategy.java:107）
- **落点指示 = 直接把落点标签填充成拖放区底色**（`DragAndDrop.Area.BACKGROUND`）；Windows 下若 `isRoundedTabDuringDrag` 为真则画圆角矩形（圆角 = `MainToolbar.Button.arc`，上偏移 6px、下偏移 13px）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:332；platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:361；platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsUICustomization.kt:206）【可直接实现】
- **拖动幽灵图**是标签自身按 preferredSize 离屏绘制出来的位图。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:3456）【Swing 特有】

### 4.3 拖出与分屏

- 横向布局的「拖出」判定：标签被推到父容器左/右边界之外，**或**纵向位移超过标签高的 `ide.tabbedPane.dragOutMultiplier`（默认 **0.1**）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayoutStrategy.java:101；platform/util/resources/misc/registry.properties:557）
- 落点分区（用于分屏）：把标签栏区域切成四个梯形，比例 `r = clamp(ide.tabbedPane.dragToSplitRatio, 0.05, 0.45)`（默认 **0.2**）；四角属于对应方向，中心属于「不分割」。（来源：platform/platform-api/src/com/intellij/ui/tabs/TabsUtil.java:54；platform/util/resources/misc/registry.properties:559）
- 拖出到别的窗口是 `DockManager` 会话：拖动开始时就先选好替代标签并把被拖标签标记为隐藏；**默认是「移动」（原标签关闭），按住 Ctrl 拖才是「复制」**。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:472；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:514；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:522）【Swing 特有】
- 不允许把某个标签栏的可见标签全部拖空。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:775）【可直接实现】
- 字母序排序模式下拖动会临时关闭自动排序（除非设置「始终保持字母序」），并在拖动结束后询问是否恢复。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/DragHelper.java:113）【Swing 特有】

### 4.4 固定（pinned）标签

- 固定状态存在 `EditorComposite.isPinned`（默认 false），同时写成组件客户端属性；`TabInfo.isPinned` 读的就是该属性。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:655；platform/platform-api/src/com/intellij/ui/tabs/TabInfo.kt:235）【Swing 特有】
- 固定标签**始终排到未固定标签之前**，前提是高级设置 `editor.keep.pinned.tabs.on.left`（默认 **true**）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:2412；platform/platform-impl/resources/intellij.platform.ide.impl.xml:1507）【可直接实现】
- **固定标签即使被拖动也会保持最左（重新分组）**；如果它是最后一个 pinned 且下一个标签同行，`isLastPinned` 用于画分隔提示。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:418）【需推断】
- 「固定标签单独一行」是二级开关：`showPinnedTabsInASeparateRow`（默认 **false**）且上面的高级设置为真时才分行。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLayout.java:75；platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:126）【可直接实现】
- 固定标签的视觉标记：关闭按钮位置显示 **pin 图标**（已修改的固定标签显示带蓝点的 pin 图标）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/tabActions/CloseTab.kt:141）【可直接实现】
- 固定标签宽度上限 2000px（可配置）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLayout.java:61）
- 固定标签**可以拖动**：拖动时记录原始 pinned 状态，落到目标窗口后按 `DRAG_START_PINNED_KEY` 恢复。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:371；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:478）
- 所有批量关闭都跳过固定标签；超出上限自动裁剪时固定标签永不关闭。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:1092）【可直接实现】
- **Augit 范围**：Augit 没有分屏与拖出成窗需求，但 pinned 语义（置左 + pin 图标 + 批量关闭跳过）可直接实现。（需推断）

---

## 5. 预览标签（preview tab）

### 5.1 什么时候产生

- 总开关 `openInPreviewTabIfPossible` 默认 **false**（设置界面「启用预览标签」）。（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:75；platform/editor-ui-api/src/com/intellij/ide/ui/UISettings.kt:105）
- `shouldReservePreview` 五段判定：① 开关关 → 否；② 文件带 `FORBID_PREVIEW_TAB` 标记 → 否；③ 打开参数 `usePreviewTab = true` → 是；④ `selectAsCurrent = false` 或 `requestFocus = true` → 否；⑤ 否则看当前焦点组件所在层级里是否有组件带客户端属性 `OPEN_IN_PREVIEW_TAB`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:1118；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:1131）
- 两个标记的含义：`OPEN_IN_PREVIEW_TAB` 表示「从这个组件发起的打开请求优先用预览标签」，`FORBID_PREVIEW_TAB` 表示「这个文件永远用正式标签」。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/FileEditorManagerKeys.kt:60；platform/platform-impl/src/com/intellij/openapi/fileEditor/FileEditorManagerKeys.kt:53）【可直接实现】
- 「从哪个面板打开」决定是否预览：项目视图、书签视图、问题视图把自己标成 `OPEN_IN_PREVIEW_TAB`，因此从这些面板单击打开 = 预览；编辑器内部导航打开则不是。（来源：platform/lang-impl/src/com/intellij/ide/projectView/impl/ProjectViewImpl.java:720；platform/bookmarks/src/com/intellij/ide/bookmark/ui/BookmarksView.kt:256）【可直接实现：Augit 可把「文件树单击」映射为预览、「双击/回车」映射为正式】

### 5.2 只有一个预览标签

- 新预览标签的插入位置取「**最后一个预览标签之后**」（`indexOfLast { it.isPreview }`），即预览位固定、就地替换。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:344）【可直接实现】
- 打开新文件时会关闭除本次文件外的**所有**预览标签（`trimToSize` 里的专门一段）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:989）【可直接实现】
- 结果：同一时刻实际只有一个预览标签。（需推断）

### 5.3 什么时候转成正式标签

| 触发 | 说明 | 来源 |
| --- | --- | --- |
| 双击标签 | 置 `isPreview = false` 并刷新文件颜色；只有非预览标签的双击才走最大化 | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:348 |
| 内容被修改 | 文件更新通道里若 `isFileModified(file)`，把所有打开该文件的 composite 的 `isPreview` 置 false | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/FileEditorManagerImpl.kt:248 |
| 被固定 | `setFilePinned(pinned = true)` 时顺带清掉预览标记 | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:957 |
| 导航离开 | **不会**转永久：文档历史项记录预览语义，前进/后退时还原 | platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/IdeDocumentHistoryImpl.kt:580 |

- Augit 是只读的，因此「修改转永久」这条不适用；可用「双击 / 固定 / 手动另存为标签」三条替代。（需推断）

### 5.4 视觉

- **预览标签文字用斜体**：标签文字属性被合并 `Font.ITALIC`（有错误属性时二者 merge）；运行时着色路径与启动恢复路径各一处。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorsSplitters.kt:1667；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorsSplitters.kt:1909）【可直接实现：`font-style: italic`】
- 预览状态变化会触发文件名/标题刷新（监听 `isPreviewFlow` 并跳过初值）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:249）【可直接实现】
- 预览状态会被写进工作区状态（`PREVIEW_ATTRIBUTE`），重启恢复时对非当前标签重新置位。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:1014）【可直接实现】
- 关闭预览标签与关闭普通标签完全一致，且进入「可重开」栈（记录原 index 与 pinned）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:697）【可直接实现】

---

## 6. 标签溢出

### 6.1 布局选择规则（决定溢出行为）

编辑器标签容器按以下顺序选布局（`EditorTabs.createRowLayout`）：

1. 非单行布局 → `WrapMultiRowLayout`（多行换行）
2. 单行且需要固定行分离或用户关闭「必要时隐藏标签」→ `ScrollableMultiRowLayout`
3. 其余 → `CompressibleMultiRowLayout`
4. 否则（单行横向且不需要上面两个条件）→ `ScrollableSingleRowLayout`

（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:658）

- 相关开关默认值：`scrollTabLayoutInEditor = true`（决定 `singleRow`）、`hideTabsIfNeeded = true`、`showPinnedTabsInASeparateRow = false`。（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:122；platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:124；platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:126）
- 代入默认值后条件 1/2/3 都不成立 → **默认就是 `ScrollableSingleRowLayout`，即横向滚动**（这是 Augit 的目标行为）。（需推断）
- 侧边放置（左/右）强制单行。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:3284）

### 6.2 单行滚动布局

- 必需总长 = 各标签 `max(preferredWidth, 50) + tabHGap` 之和 + 内距 + 附加长度；放不下时才显示「更多」按钮。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayout.java:250；platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/ScrollableSingleRowLayout.java:113）
- 滚动偏移在 `[0, 必需长 − 可容长 + 更多按钮宽 + 动作内距]` 内夹取。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/ScrollableSingleRowLayout.java:49）
- **自动把选中标签滚入视野**：鼠标不在标签区内、且滚动条不在拖拽、且滚动条刚活动过 2s 内都不做自动滚动（避免与用户操作打架）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/ScrollableSingleRowLayout.java:68；platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:295）
- 选中标签太长放不下时，**优先保证它的左边缘可见**。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/ScrollableSingleRowLayout.java:89）
- 放不下的标签**先按剩余宽度裁切显示**（`drawPartialOverflowTabs()` 横向返回 true），裁切到负坐标超过 10px 死区才判定为「隐藏」。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayoutStrategy.java:193；platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/ScrollableSingleRowLayout.java:144）
- 滚轮在标签区滚动标签条：滚轮事件被转发到一个隐藏的 `JScrollPane`，横向标签额外叠加 Shift 修饰；单位增量 = 标签字体大小，块增量 = 字体大小 ×10。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:571；platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:549）【可直接实现：`wheel` → `scrollLeft`】

### 6.3 「更多」下拉

- 只在 `requiredLength > toFitLength` 时布局「更多」按钮；按钮位于标签区右侧、正文入口按钮（entry point）之前。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/ScrollableSingleRowLayout.java:113；platform/platform-api/src/com/intellij/ui/tabs/impl/singleRow/SingleRowLayoutStrategy.java:231）
- 「更多」按钮尺寸 = `ActionToolbar.DEFAULT_MINIMUM_BUTTON_SIZE` 各加 `+4 × +2`（源码注释写明「默认单动作横向工具栏大小 = 26×24」）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:1064）
- 动作按钮区的外边距（New UI 横向）：`0,5,0,8`；侧向：`4,8,4,3`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:2899）
- 下拉内容 = 被判定为隐藏的标签，New UI 用**列表弹窗**：每项左侧可画选中竖条，右侧可有操作按钮；列表项的图标-文字间距 = `ActionsList.elementIconGap − 2`；关闭按钮在右时标签文字后留 30px 空白再放动作。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:1100；platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:1168）
- 列表会在「第一个仍可见/第一个已隐藏」之间插入分隔线。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:1101）【可直接实现】

### 6.4 横向滚动条

- 滚动条厚度恒为 **5px**（`TabScrollBarUI(thickness = max = min = 5)`）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:204；platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:559）
- 位置：顶部放置 → `(0, 1, 宽, 5)`，即压在标签条顶端；底部放置 → 贴底；左/右放置 → 5px 宽的竖向条（左放置 New UI 下贴在标签矩形右缘）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:703）
- **只在鼠标位于标签区内时显示**：`mouseMoved` 时按「在标签区内或正在拖拽滚动条」显示，`mouseExited` 隐藏；鼠标离开标签区后再保持 2000ms 才重新布局隐藏。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:619；platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:298）【可直接实现】
- 滚动条的出现由 registry `ide.experimental.ui.editor.tabs.scrollbar`（默认 **true**）与 New UI 共同控制。（来源：platform/core-ui/src/ui/ExperimentalUI.kt:76；platform/util/resources/misc/registry.properties:2018）
- 滚动模型：`maximum = 必需长度`、`value = 当前滚动偏移`、`extent = 可容长度`；extent 为 0 时视为布局未就绪，不显示滚动条。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/JBTabsImpl.kt:2132）【Swing 特有】

### 6.5 压缩布局与多行布局（非默认，供对照）

- 压缩布局（`CompressibleTabsRow`）：先把超出量平均摊到所有标签，优先压缩「角到文字 / 图标间距 / 动作内距」等装饰内距（按比例缩放并设下限），装饰压无可压后再从**最宽的标签开始**等比削宽度。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/multiRow/CompressibleTabsRow.kt:59；platform/platform-api/src/com/intellij/ui/tabs/impl/multiRow/CompressibleTabsRow.kt:130）
- 压缩内距的比例常量：角到文字 0.33（下限 4px）、图标间距 0.5、动作内距 0、角到动作 0.5（中间档 0.75，下限 4px）。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/multiRow/CompressibleTabsRow.kt:273）
- 压缩布局只在「未固定 + 启用了压缩 + 未悬停 + 未选中 + 实际宽度小于 preferred 宽度」时对单个标签生效；选中或悬停的标签保留完整宽度。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/TabLabel.kt:824）
- 多行换行（`WrapMultiRowLayout`）：按 preferredWidth 顺序贪心塞行，塞不下就换行；第一行的可用宽度要扣掉标题与入口工具栏。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/multiRow/WrapMultiRowLayout.kt:12）
- **多行之间没有额外行距**：行 `y = 区域 y + 行号 × rowHeight`，`rowHeight = 标签栏内容高`；行内标签仍共享 −1px 的 `tabHGap`。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/multiRow/MultiRowLayout.kt:62；platform/platform-api/src/com/intellij/ui/tabs/impl/multiRow/MultiRowPassInfo.kt:18）【可直接实现】
- 固定标签单独一行时按「最后一个 pinned 的下标」切两段。（来源：platform/platform-api/src/com/intellij/ui/tabs/impl/multiRow/MultiRowLayout.kt:107）【可直接实现】

---

## 7. 多标签的状态保持与「延迟恢复」

### 7.1 只有活动标签的正文被创建（Augit 规格要求的上游机制）

- 启动恢复默认走**惰性 composite**：系统属性 `idea.delayed.editor.composite` 默认 `"true"`；非启动恢复路径显式传 `false`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorsSplitters.kt:657；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorsSplitters.kt:613）
- 只有「上次/当前选中的标签」（`fileEntry.currentInTab`）会预取文件内容：文件内容预取任务按 `currentInTab ? DEFAULT : LAZY` 启动，composite 模型的 `isLazy = !currentInTab && isLazyComposite`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorsSplitters.kt:1549；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorsSplitters.kt:1579）
- 非惰性模式下才立刻放行：`if (fileEntry.currentInTab || !isLazyComposite) composite.initDeferred.complete(Unit)`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/FileEditorManagerImpl.kt:2431）
- **关键闸门**：`EditorComposite.initDeferred` 只在面板**第一次被显示**时完成（`UiNotifyConnector.doWhenFirstShown`，非延迟模式）；收集 editor model 的协程先 `initDeferred.await()`，所以**未被显示过的标签不会创建 FileEditor、不会解析文档**。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:224；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:237）
- 选中标签时才等待加载并聚焦：`waitForAvailable()` 会主动 `initDeferred.complete(Unit)` 再等 `availableDeferred`；聚焦前校验「当前 composite 仍是它」以防抢焦点。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:257；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:461）
- 标签**先插入、后填内容**：插入时 component 是 composite 的空面板，配一个同尺寸空图标占位，名称用 `presentableName`，自定义标题/图标异步补齐。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:352；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorsSplitters.kt:1640）
- 未加载期间显示骨架屏（EditorSkeleton），由 `editor.skeleton.enabled`（默认 true）与 500ms 延迟策略控制。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:1094；platform/platform-impl/src/com/intellij/openapi/fileEditor/DefaultEditorSkeletonPolicy.kt:9）【可直接实现】
- 关闭标签才释放编辑器实例（`disposeComposite` → 逐个 `provider.disposeEditor` + `Disposer.dispose`）。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:711；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/FileEditorManagerImpl.kt:2075）【可直接实现】
- ⚠ **纠偏**：「`getComposite` 首次访问才创建 composite / `myComposite` 为 null」的旧说法在本 commit **不成立**：composite 对象与空面板先创建并立即加入标签栏，只是**内容**按需填充。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/FileEditorManagerImpl.kt:1549；需复核实）【需复核实】

### 7.2 切换标签时保留什么

- 切换（不关闭）时 `Editor` 实例**不销毁**，因此滚动位置、光标、选区、折叠状态随 Editor 存活而保留；只有关闭标签才 dispose。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:711；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/FileEditorManagerImpl.kt:2075）【可直接实现：Augit 需在标签切换时把 scrollTop/选区缓存到标签状态里】
- 状态的载体是 `TextEditorState`：多个 caret 状态 + `relativeCaretPosition`（主光标相对视口顶部的像素偏移，即滚动位置）+ 折叠状态；每个 caret 状态含 line/column、leanForward、visualColumnAdjustment 与选区起止。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/text/TextEditorState.java:16；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/text/TextEditorCaretState.java:5）【可直接实现】
- 编辑器尚未加载完成时取/恢复状态会先缓存 `DelayedScrollState(relativeCaretPosition, exactState)`，加载完成后再 `scrollToCaret` 恢复。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/text/TextEditorImpl.kt:236；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/text/AsyncEditorLoader.kt:214）
- **关闭后重开仍保留**：`EditorHistoryManager` 在文件打开时保存每个 provider 的 `getState(FULL)`，重新打开且没有显式 state 时回填历史状态再恢复。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorHistoryManager.kt:170；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:561）
- 该历史**持久化到工作区文件**（`@State editorHistoryManager`，`PRODUCT_WORKSPACE_FILE`），即跨 IDE 会话保留。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorHistoryManager.kt:43）【可直接实现】
- 状态按「编辑器 provider」维度各存一份（同一文件可同时有文本/预览等多种编辑器），并同时保存 selectedProvider 与 isPreview。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:955；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:1004）
- 焦点：每个 composite 用一个 FocusWatcher 记住「最后聚焦的子组件」，但切回标签时明确优先聚焦编辑器组件本身（避免焦点卡在查找框里）；标签栏也声明 `requestFocusOnLastFocusedComponent = true`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:172；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:1257；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:585）【Swing 特有】
- 查找栏是「每编辑器一个 header 组件」，关闭查找时置 null，因此**标签关闭即丢失该标签的查找栏展开状态**；但查找**条件**写回全局 find-in-file 模型，所以新标签会继承上次的搜索条件。（来源：platform/lang-impl/src/com/intellij/find/EditorSearchSession.java:342；platform/lang-impl/src/com/intellij/find/EditorSearchSession.java:203）【可直接实现】

### 7.3 同一文件重复打开 = 复用同一标签

- 标签栏插入前先按文件查已有标签，命中则直接返回它，不新建。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:271）
- `reuseOpen` 语义：优先在当前活动 tab group 找同文件，找不到则遍历所有分屏找已打开该文件的窗口并复用。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/FileEditorManagerImpl.kt:1216）【可直接实现】
- Augit 的「工作区 Diff 跟随选择复用同一比较标签」与 `reuseOpen` 同构：Diff 以「单编辑器窗口 + 复用」方式打开（`isSingletonEditorInWindow = true, reuseOpen = true`），内容替换发生在同一 viewer 内而不是新开标签。（来源：platform/diff-impl/src/com/intellij/diff/editor/DiffEditorTabFilesManagerImpl.kt:25；platform/diff-impl/src/com/intellij/diff/impl/DiffRequestProcessor.java:463）
- 内容替换时旧 state 被销毁、重建视图；若请求对象未变且非强制刷新则直接跳过。（来源：platform/diff-impl/src/com/intellij/diff/impl/DiffRequestProcessor.java:434；platform/diff-impl/src/com/intellij/diff/impl/DiffRequestProcessor.java:465）
- 请求级缓存：diff 请求处理器用 5 硬 + 5 软缓存，命中则立即应用；同一 provider 正在计算则等待并复检。（来源：platform/diff-impl/src/com/intellij/diff/impl/CacheDiffRequestProcessor.java:40；platform/diff-impl/src/com/intellij/diff/impl/CacheDiffRequestProcessor.java:88）【可直接实现】
- 单编辑器窗口（含 diff）只有 1 个标签时标签栏隐藏，判定读 `SINGLETON_EDITOR_IN_WINDOW`。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorTabbedContainer.kt:839；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorWindow.kt:638）【可直接实现】
- 切换「在编辑器中显示 diff」设置时会把所有已打开 diff 标签先全关再重开，目的正是避免复用陈旧标签。（来源：platform/diff-impl/src/com/intellij/diff/editor/DiffEditorTabFilesManagerImpl.kt:95）【可直接实现】
- diff 作为单标签停靠在某个分屏里时，从其他地方打开新文件会被安排到该分屏的**兄弟窗口**（保持 diff 可见），而不是把 diff 挤掉。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/FileEditorManagerImpl.kt:1248）【Swing 特有：Augit 无分屏】
- 预览状态会写进工作区状态（`PREVIEW_ATTRIBUTE`），重启恢复时对非当前标签重新置 `isPreview`；文档历史项也记录预览语义，前进/后退会还原它。（来源：platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/EditorComposite.kt:1014；platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/IdeDocumentHistoryImpl.kt:580）【可直接实现】

---

## 8. 只读正文显示

### 8.1 行号列宽度

- 行号列宽 = `max(初始宽度, 最长行号字符串的像素宽)`；**不是「位数 × 字符宽」**，而是用行号字体对最大行号字符串做一次 `stringWidth` 量测。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:892；platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:2095）
- 最大行号字符串由行号转换器的 `getMaxLineNumberString(editor)` 提供（`getStandardLineNumberConverter` 返回绝对/相对/混合三种实现）；绝对行号下即文档总行数，因此列宽随总行数增长（3 位数 → 4 位数时整列会变宽）。（来源：platform/editor-ui-api/src/com/intellij/openapi/editor/LineNumberConverter.java:46；platform/platform-impl/src/com/intellij/openapi/ui/ex/lineNumber/LineNumberConverters.kt:7）【可直接实现】
- 初始（最小）宽度在 New UI 下 = **断点图标的宽度**（按编辑器缩放），即行号列始终预留一个 gutter 图标位；旧 UI 为 0。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:3149）
- `getAreaWidthWithGap(w)` 只在 `w > 0` 时补 `GAP_BETWEEN_AREAS`；有「附加行号」（diff 的第二套行号）时列宽再叠这一项。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:2059；platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:2075）【可直接实现】
- **关闭行号显示时，New UI 仍保留 `scaleWithEditor(14)` 的宽度**，以免左侧图标区错位。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:2081）
- 行号在列内**右对齐**：x = 列右边界 − 该行号字符串宽度。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:972）【可直接实现】
- 行号字体 = 编辑器 PLAIN 字体 + 高级设置 `editor.gutter.linenumber.font.size.delta`（默认 **−1**，即行号比正文小 1pt，下限 1pt）。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:885；platform/platform-impl/resources/intellij.platform.ide.impl.xml:1495）【可直接实现】
- 区段间距常量：图标间 `GAP_BETWEEN_ICONS = 3`、区段间 `GAP_BETWEEN_AREAS = 5`、注释间 `GAP_BETWEEN_ANNOTATIONS = 5`；主题键 `Editor.Gutter.gapAfterVcsMarkersWidth` / `gapAfterLineNumbersWidth` / `gapAfterIconsWidth` / `emptyAnnotationAreaWidth` 默认均为 **4**。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:264；platform/util/ui/src/com/intellij/util/ui/JBUI.java:885）
- 区段顺序（默认 `isLineNumbersAfterIcons = false`）：注解区 → **行号区** → 图标/断点区 → 折叠区 → 右侧自由绘区。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterLayout.java:203；platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorSettingsState.kt:97）
- `scaleWithEditor(v) = round(v × editor.getScale())`，行号区尺寸随编辑器字号/行高一起缩放。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:1973）
- 【可直接实现】Augit 的等价做法：`width = max(图标位(16px) , 量测(最长行号文本)) + 4px`，文字 `text-align: right`，行号字号 = 正文字号 − 1。（需推断）
- 【需推断】`EditorGutterLayout.java:203` / `:239` 给出的「行号后固定 areaGap(12)」「右侧附加 areaGap(3) + 1px」等细节属于 Swing 布局装配，Augit 不必逐值复刻。

### 8.2 行号与 gutter 颜色

| 键 | 含义 | 默认值 | 来源 |
| --- | --- | --- | --- |
| `LINE_NUMBERS_COLOR` | 普通行号前景色 | Default 亮 `#999999` / 暗 `#606366`；**expUI 亮 `#aeb3c2` / 暗 `#4b5059`** | platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:21；platform/platform-resources/src/DefaultColorSchemesManager.xml:22；platform/platform-resources/src/themes/expUI/expUI_lightScheme.xml:26；platform/platform-resources/src/themes/expUI/expUI_darkScheme.xml:33 |
| `LINE_NUMBER_ON_CARET_ROW_COLOR` | 当前行行号色（null 则不改色） | Default `#a4a3a3`；expUI 亮 `#767a8a` / 暗 `#a1a3ab` | platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:22；platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:939 |
| `CARET_ROW_COLOR` | 当前行底色 | Default 亮 `#fffae3` / 暗 `#323232`；**expUI 亮 `#f5f8fe` / 暗 `#26282e`** | platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:18；platform/platform-resources/src/themes/expUI/expUI_lightScheme.xml:15；platform/platform-resources/src/themes/expUI/expUI_darkScheme.xml:16 |
| `EDITOR_GUTTER_BACKGROUND` | **New UI 专用** gutter 底色；默认 `null` → 回退编辑器背景 | `null` | platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:72 |
| `GUTTER_BACKGROUND` | 旧 UI gutter 底色 | 亮 `#f0f0f0` / 暗 `#313335` | platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:68 |

- 当前行行号改色的条件是「光标所在逻辑行 == 该行」且不在 sticky 绘制中。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:939）【可直接实现】
- gutter 背景被一条「空白分隔线」一分为二：分隔线左侧用 gutter 底色、右侧用编辑器底色。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:557）【可直接实现】
- **New UI 的 gutter 分隔线**在 `x = 宽度 − scale(3)`，颜色 `INDENT_GUIDE_COLOR`，1px 竖线；只在 New UI、允许绘制背景、非失焦模式、非 sticky 时绘制。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:1804）【可直接实现】
- gutter 内的当前行底色同样用 `CARET_ROW_COLOR`，在**整条 gutter 宽度**上按光标视觉行的 Y 区间填充；当前行关闭时 `getCaretRowColor()` 返回 null 从而跳过。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:812；platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:815）
- 失焦（distraction-free）模式下行号改用该行文本前景色，回退 `LINE_NUMBERS_COLOR`，再回退 `JBColor.blue`；gutter 底色直接用编辑器背景色。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:931；platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterColor.java:17）
- ⚠ **纠偏**：本 commit 中**不存在** `EditorGutter.background` / `EditorGutter.foreground` / `EditorGutter.modifiedBackground` / `Editor.LineNumber` 等 UI 键；VCS 变更行的颜色是 `ADDED_LINES_COLOR` / `MODIFIED_LINES_COLOR` / `DELETED_LINES_COLOR`（**行标记**，不是 gutter 底色）。（来源：platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:78）【需复核实】

### 8.3 行高

- 行高 = `ceil(字体度量高 × lineSpacingFactor)`；`lineSpacingFactor = colorsScheme.getLineSpacing()`，`≤ 0` 时取 1。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorView.java:991）【可直接实现】
- `lineSpacing` 的运行时基线默认是 `FontPreferences.DEFAULT_LINE_SPACING = 1.2f`；设置对话框的默认值是 `1f`，允许范围 **0.6–3.0**，写入时被夹取。（来源：platform/editor-ui-api/src/com/intellij/openapi/editor/colors/FontPreferences.java:23；platform/editor-ui-ex/src/com/intellij/application/options/EditorFontsConstants.java:23）【可直接实现】
- 字体基线在行高内**居中**：`descent = 字体 descent + (行高 − 字体度量高) / 2`，`ascent = 行高 − descent`；行号绘制用 `y + ascent` 对齐，所以行号与正文共享同一条基线。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorView.java:853；platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:976）【可直接实现】
- 默认编辑器字号：Windows = **13**（`DEFAULT_FONT_SIZE`）。（来源：platform/editor-ui-api/src/com/intellij/openapi/editor/colors/FontPreferences.java:21）【可直接实现】
- ⚠ **Augit 偏离**：Augit 用「等宽字号 × 1.7」是自有规格，**不是** IntelliJ 取值（IntelliJ 运行时基线为 1.2）。若目标是「100% 复原 PyCharm」，行高应按 `ceil(字体度量高 × 1.2)` 实现；若保留 1.7，必须在 ux-spec 中登记为有意偏离。（需推断，需用户确认）

### 8.4 当前行高亮

- 颜色键是 `CARET_ROW_COLOR`（**不是** `Editor.CaretRow` / `CaretRowBackground`），实现方式是把该色写进光标行的 `TextAttributes` 背景。（来源：platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:18；platform/platform-impl/src/com/intellij/openapi/editor/impl/CaretModelImpl.java:103）【可直接实现】
- `isCaretRowShown()` **硬编码返回 true**，用户无法关闭当前行高亮（Augit 应同样恒开）。（来源：platform/ide-core-impl/src/com/intellij/openapi/editor/ex/EditorSettingsExternalizable.java:629）【可直接实现】
- 水平范围：先按行内文本段填背景，再由 `paintAfterLineEnd` 把「行尾 → 可视区右边缘」整段补满，因此当前行底色**贯穿整个可视宽度**（不是只到最后一个字符，也不是只到右边距）；行首之前也补一段以保证左端连续。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:658；platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:581）【可直接实现】
- 背景先于文本绘制（背景立即填充、文本随后入队），因此当前行高亮**在文字之下**；背景色等于编辑器背景时跳过填充。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:849）【可直接实现】
- 光标行按**视觉行**计算（软换行的续行单独算一行）；文档末尾行会 +1 以包含行尾。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/CaretData.java:18）【可直接实现】
- 右边距线：只在 `RIGHT_MARGIN_COLOR` 非 null 时显示；**只读文件**还需要高级设置 `editor.show.right.margin.in.read.only.files`（默认 **true**）。（来源：platform/util/resources/misc/registry.properties:1720；platform/ide-core-impl/src/com/intellij/openapi/editor/ex/EditorSettingsExternalizable.java:83）【可直接实现：Augit 是只读查看器，默认应显示右边距线】
- 右边距默认是整列直线（`editor.adjust.right.margin` 默认 **false**）；为 true 时逐视觉行贴合折行宽度画成阶梯。（来源：platform/util/resources/misc/registry.properties:1717）【可直接实现】
- sticky lines 面板、renderer 模式、`isStickyLinePainting` 下抑制当前行底色；caret row 属性插在 `HighlighterLayer.CARET_ROW` 层之下，语法高亮在其上。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/IterationState.java:588；platform/platform-impl/src/com/intellij/openapi/editor/impl/view/IterationState.java:658）【Swing 特有】

### 8.5 空白符显示

- 参与渲染的「空白」只有三个字符：普通空格 `' '`、制表符 `'\t'`、表意空格 `'\u3000'`。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/LineWhitespacePaintingStrategy.java:11）【可直接实现】
- 可见性 = 一个总开关 + 四类分区：总开关 `IS_WHITESPACES_SHOWN` 默认 **false**；前导/内部/尾随/选区内四类默认都是 **true**。总开关关则完全不画；总开关开而四类全关也不画。（来源：platform/ide-core-impl/src/com/intellij/openapi/editor/ex/EditorSettingsExternalizable.java:109；platform/platform-impl/src/com/intellij/openapi/editor/impl/view/LineWhitespacePaintingStrategy.java:32）【可直接实现】
- 分区判定按当前逻辑行的 `currentLeadingEdge` / `currentTrailingEdge`：offset 小于前导边界 → 前导类；大于等于尾随边界 → 尾随类；其余 → 内部类；都不满足时若「选中区空白显示」且 offset 在选区内则显示。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/LineWhitespacePaintingStrategy.java:48）【可直接实现】
- 标记形状：
  - 空格 → **实心圆点**，直径 = 空白缩放系数，锚在字符格中心并对齐设备像素中心；
  - 制表符 → 由高级设置 `editor.tab.painting` 决定（`LONG_ARROW` / `ARROW` / `HORIZONTAL_LINE`），**默认 `HORIZONTAL_LINE`**：一条水平线，末端留 `5 × scale` 的间隙；
  - 表意空格 → **描边矩形**，左右各内缩 2px。
  （来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:1063；platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:1118；platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:1138；platform/platform-impl/src/com/intellij/openapi/editor/impl/TabCharacterPaintMode.java:8；platform/platform-impl/resources/intellij.platform.ide.impl.xml:1492）【可直接实现】
- **空白标记是叠加在文字之上的独立绘制**，入队顺序为 背景 → 文本 → 空白标记；**文档内容不被修改**（因此不存在改 `textContent` 的问题，Augit 应同样用绝对定位的标记层）。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:641；platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:652）【可直接实现】
- 颜色键是 `WHITESPACES`（`ColorKey`，纯装饰、不进入文档模型）；制表符色 `TABS` 以 `WHITESPACES` 为 fallback；改动行另有 `WHITESPACES_MODIFIED_LINES_COLOR`。默认色：Default 亮 `#adadad` / 暗 `#606060`；expUI 暗 `#6f737a`（expUI 亮未单独定义，继承 Default）。（来源：platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:27；platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:28；platform/platform-resources/src/DefaultColorSchemesManager.xml:14；platform/platform-resources/src/themes/expUI/expUI_darkScheme.xml:52）【可直接实现】
- 空白标记的缩放系数 = `编辑器字号 / 13`（Windows 基准），字号越大点/线越粗；描边宽度 = `max(1, round(scale))`。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:1142）【可直接实现】
- 折叠占位区域内不画空白标记。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:645）【Swing 特有：Augit 无折叠】

### 8.6 自动换行（软换行）

- 默认状态：**主编辑器默认开启软换行**（设置存储为 `null` 表示「用户未选，用默认」）。（来源：platform/ide-core-impl/src/com/intellij/openapi/editor/ex/EditorSettingsExternalizable.java:306）【可直接实现】
- 换行指示符只画在**光标所在逻辑行**，除非高级设置 `IS_ALL_SOFTWRAPS_SHOWN`（默认 **false**）打开才所有行都画。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:576；platform/ide-core-impl/src/com/intellij/openapi/editor/ex/EditorSettingsExternalizable.java:115）【可直接实现】
- 两个指示符：行尾处画「换行前」符号，续行行首处画「换行后」符号（位置为 `x − 符号宽`）。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:592；platform/platform-impl/src/com/intellij/openapi/editor/impl/view/EditorPainter.java:709）【可直接实现】
- 指示符优先使用 Unicode 字符对，字体都显示不了时回退到手绘箭头；字符颜色取自 `SOFT_WRAP_SIGN_COLOR`，箭头用默认前景色。（来源：platform/platform-impl/src/com/intellij/openapi/editor/colors/EditorColors.java:31）【可直接实现】
- 指示符宽度**参与排版**：换行阈值 = 可视宽度 − 行尾符号宽度；续行左缩进 = 行首符号宽度。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/SoftWrapEngine.java:65）【可直接实现】
- **软换行的续行不显示行号**（只有非「以软换行开头」的视觉行才画行号）。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorGutterComponentImpl.java:917）【可直接实现】
- 续行缩进：`USE_CUSTOM_SOFT_WRAP_INDENT` 默认 true、`CUSTOM_SOFT_WRAP_INDENT` 默认 0 列 → 续行与逻辑行首对齐，默认不额外缩进。（来源：platform/ide-core-impl/src/com/intellij/openapi/editor/ex/EditorSettingsExternalizable.java:679）【可直接实现】
- 默认按文件类型自动开启软换行的掩码是 `*.md; *.txt; *.rst; *.adoc`。（来源：platform/ide-core-impl/src/com/intellij/openapi/editor/ex/EditorSettingsExternalizable.java:51）【可直接实现】
- 软换行是**纯显示层**概念，只产出视觉行映射，不改动文档。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/SoftWrapEngine.java:55）【可直接实现：Augit 用 `white-space: pre-wrap` + 标记层即可】

### 8.7 滚动与滚动条

- 编辑器垂直滚动条策略 **ALWAYS**，水平 **AS_NEEDED**；默认定位在右侧，左侧用 row header 放 gutter。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorImpl.java:1621）
- **编辑器滚动条默认透明**：`shouldScrollBarBeOpaque() = 无背景图 && !Registry("editor.transparent.scrollbar")`，而 `editor.transparent.scrollbar` 默认 **true**。（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorImpl.java:1188；platform/util/resources/misc/registry.properties:1374）【可直接实现】
- 粗细：`DefaultScrollBarUI` 默认 `thickness = 10`（`ide.scroll.thumb.small.if.opaque=true` 时 13）、`thicknessMax = 14`、`thicknessMin = 10`；`ThinScrollBarUI` 三值均为 **3**。（来源：platform/platform-api/src/com/intellij/ui/components/DefaultScrollBarUI.kt:53；platform/platform-api/src/com/intellij/ui/components/ThinScrollBarUI.kt:10）【可直接实现】
- 悬浮/淡出动画：`TwoWayAnimator` 参数（总帧 11、前进前停 150ms、前进 125ms、后退前停 300ms、后退 125ms）→ 等价于「**悬停进入约 150ms 后滑块显色，移出约 300ms 后淡出**」；颜色在普通色与悬停色之间按动画值混合。（来源：platform/platform-api/src/com/intellij/ui/components/ScrollBarPainter.java:136）【可直接实现】
- 滚动条配色键（均带透明/不透明两组及 hover 变体）：轨道 `ScrollBar.trackColor` / `ScrollBar.Transparent.trackColor`；滑块 `ScrollBar.thumbColor` / `ScrollBar.Transparent.thumbColor`。（来源：platform/platform-api/src/com/intellij/ui/components/ScrollBarPainter.java:50；platform/platform-api/src/com/intellij/ui/components/ScrollBarPainter.java:99）【可直接实现】
- 高对比滚动条 `useContrastScrollBars` 默认 **false**。（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:175）
- 平滑滚动 `smoothScrolling` 默认 **true**；动画滚动 `animatedScrolling` 默认 true（非远程、非 Mac），时长 **Windows = 200ms**。（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:220；platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:229；platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:282）【可直接实现】
- 滚轮改字号默认**关闭**（`IS_WHEEL_FONTCHANGE_ENABLED` = false），Augit 不应实现该交互。（来源：platform/ide-core-impl/src/com/intellij/openapi/editor/ex/EditorSettingsExternalizable.java:124）

---

## 9. 与 Augit 的映射与偏离清单

### 9.1 可直接实现（HTML/CSS/C# 可表达）

| 规则 | 数值 | 来源 |
| --- | --- | --- |
| 标签栏高度 | 42px（公式 28 − (−7) − (−7) + 1） | §1.3 |
| 标签左右内距 | 8px + 4px = 12px（无关闭按钮） | §1.4 |
| 标签间 | 重叠 1px（`margin-right: -1px`） | §1.4 |
| 编辑器标签最小宽度 | 50px | §1.4 |
| 固定标签最大宽度 | 2000px | §1.4 |
| 图标-文字间距 | 4px | §1.4 |
| 标签文字居中 | 被拉伸到最小宽度时文字居中 | §1.4 |
| 标签字体 | 标准标签字体 + `fontSizeOffset`（默认 0），New UI 不用小字号 | §1.4 |
| 选中下划线 | 高 4px、圆角 4px、贴标签底、通铺整宽 | §2.4 |
| 下划线切换动画 | 100ms / 位移侧延迟 50ms / EASE_OUT（默认关闭） | §2.4 |
| 未选中文字混色比 | 浅 0.9 / 深 0.7 | §1.1 |
| 未选中图标不透明度 | 0.75 | §1.1 |
| 关闭按钮图标框 | 13×13；修改点直径 6px、内缩 3.5px | §1.5 |
| 关闭按钮悬停延迟 | 150ms | §1.5 |
| 关闭按钮可见条件 | 选中 ∨ 悬停 ∨ 已修改 ∨ 已固定 | §1.5 |
| 预览标签 | 文字斜体、同一时刻仅一个、双击转正式 | §5 |
| 从文件树单击打开 | 用预览标签（映射 `OPEN_IN_PREVIEW_TAB`） | §5.1 |
| 溢出 | 单行横向滚动 + 5px 滚动条 + 更多下拉 | §6 |
| 滚动条可见性 | 鼠标在标签区内才显示，离开后保留 2000ms | §6.4 |
| 拖动阈值 | 7px | §4.1 |
| 拖出阈值 | 标签高的 0.1（纵向） | §4.3 |
| 边缘渐隐宽度 | 10px | §1.6 |
| 行号列宽 | `max(图标位, 量测(最长行号文本))`，只在有副行号时加 5px | §8.1 |
| 行号右对齐 + 字号 −1pt | `text-align: right`；行号字号 = 正文 − 1 | §8.1 |
| 行号颜色 | expUI 亮 `#aeb3c2` / 暗 `#4b5059`；当前行行号亮 `#767a8a` / 暗 `#a1a3ab` | §8.2 |
| gutter 分隔线 | `width − 3px`，1px，`INDENT_GUIDE_COLOR` | §8.2 |
| 行高 | `ceil(字体度量高 × lineSpacing)`，`lineSpacing` 默认 1.2（可选 0.6–3.0） | §8.3 |
| 当前行底色 | expUI 亮 `#f5f8fe` / 暗 `#26282e`，贯穿可视宽度、绘制在文字之下、用户不可关 | §8.4 |
| 右边距线 | 只读文件默认显示（`editor.show.right.margin.in.read.only.files = true`） | §8.4 |
| 空白符标记 | 空格=实心圆点、制表符=水平线（默认）、表意空格=描边矩形；叠加在文字之上、不改文档 | §8.5 |
| 空白符颜色 | Default 亮 `#adadad` / 暗 `#606060`；expUI 暗 `#6f737a` | §8.5 |
| 空白符缩放 | 直径/线宽按 `字号 / 13` 缩放 | §8.5 |
| 软换行 | 主编辑器默认开启；指示符只画在当前逻辑行；续行不显示行号 | §8.6 |
| 编辑器滚动条 | 垂直恒显、水平按需；默认透明；颜色键 `ScrollBar.thumbColor` / `ScrollBar.Transparent.thumbColor` | §8.7 |
| 滚动条淡入淡出 | 进入约 150ms、移出约 300ms | §8.7 |
| 平滑滚动 | `smoothScrolling` 默认 true；动画时长 Windows 200ms | §8.7 |
| 标签状态保留 | 切换不销毁编辑器 → 滚动/光标/选区自然保留；关闭后重开从历史状态回填 | §7.2 |
| 延迟创建 | 只有活动标签初始化正文 | §7.1 |

### 9.2 Swing 特有（无法照搬，只能借鉴语义）

- `JBTabsBorder.effectiveBorder` / `layoutInsets` 参与标签高度计算（边框同时充当内距）。
- `ShapeTransform`（标签形状变换）、`WindowTabsLayout`（macOS 窗口标签）、`TabSideSplitter`。
- `InplaceButton` / `BaseButtonBehavior` 的按钮行为与 `ActionToolbar` 驱动的标签动作面板（`ActionPanel` / `ActionButton`）。
- 拖出的 `DockManager` / `DockableEditor` 会话与「拖到屏幕边缘开新窗口」；Augit 不做分屏与拖出成窗。
- 拖动的离屏位图幽灵图、`ScreenUtil.moveToFit`。
- `JBScrollBar` / `TabScrollBarUI` 的滚动模型（Augit 用原生 `overflow-x` 即可）。
- `JBTabsImpl.getVisibleInfos()` 里对 pinned 先分组、再按字母序排序（`sortTabsAlphabetically`）。
- 编辑器正文的全部绘制（`EditorPainter` / gutter 组件树 / 折叠 / inlay）。

### 9.3 需推断 / 待确认

1. **PyCharm 2026.2.1 默认主题是 expUI 还是 Islands** —— 决定下划线方案（4px 圆角下划线 vs 圆角卡片无下划线）。（§0）
2. `EditorTabs.tabInsets = -7,8,-7,8` 的负内距在 1x 缩放下的最终标签高（本文件按公式推为 41/42px，未在源码写死）。（§1.3）
3. 标签文字与关闭按钮之间的精确像素间距（结构上为内容区内距 6px，未实测）。（§1.4）
4. 标签标题是否真的不做省略号（`fileEditor/impl` 与 `ui/tabs` 内检索不到省略号代码；但没有逐层确认 `SimpleColoredComponent` 的截断策略）。已核对：Augit 的普通标签也没用省略号，但**缺 10px 渐隐层**；比较标签仍在用省略号。（§1.6）
5. 「关闭后回退到上一个选中标签」的实际生效时序（`previousSelection` 在按下时写入、释放时清空）。（§3.2）
6. **Augit 行高用 1.7 与 IntelliJ 的 1.2 不一致**：应确认是「有意偏离」还是「需要修正」。（§8.3）
7. 标签宽度公式里的右内距在「关闭按钮可见」与「不可见」两态之间会跳动，是否需要预留占位以避免标签宽度抖动 —— IntelliJ 用渐隐+压缩规避，Augit 需自行决定。（需用户确认）
8. Augit `.editor-tab` 的 `max-width: 230px` 与 `border-radius: 6px` 在 IntelliJ 都没有对应（未固定标签无最大宽度；经典 expUI 标签无圆角），需按 §0 结论决定保留或删除。（§1.6）
9. **需复核实**：「`getComposite` 首次访问才创建 composite」的旧说法在本 commit 不成立（改成了「先建空面板 + 按需填充 model」）。（§7.1）
10. **需复核实**：`EditorGutter.background` / `EditorGutter.foreground` / `Editor.LineNumber` 等 UI 键在本 commit 不存在，等价的 `ColorKey` 见 §8.2。
11. **需复核实**：`lineSpacing` 有两处默认值（运行时 `FontPreferences.DEFAULT_LINE_SPACING = 1.2f` 与设置 UI 的 `1f`），实现时应以配色方案的实际值为准。（§8.3）
12. **需复核实**：右边距默认列数随代码风格，没有单一常量。（§8.4）

---

## 10. 合规与范围声明

- 本文只记录**几何参数、颜色键、状态映射与交互时序**，未复制 JetBrains 的图标资源、字体文件、商标或产品标志；文中提到的图标（关闭、固定、文件类型）只记录尺寸与状态映射，Augit 需按 `docs/design-system.md` 自绘等效实现。
- 已跳过 Augit 范围外内容：调试器标签（`DebuggerTabs`）、构建工具窗口标签、代码补全、语言支持、括号匹配、编辑与保存、拼写检查。
- 本文不改变任何产品行为；与 `docs/product-spec.md` / `docs/ux-spec.md` / `docs/design-system.md` 冲突时以那三份为准，冲突需先经用户确认。
