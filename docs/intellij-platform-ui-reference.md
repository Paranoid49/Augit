# IntelliJ Platform New UI 数值参考

本文是**取值来源**，不是规格。它记录从 IntelliJ 开源仓库（`JetBrains/intellij-community`）中提取的 New UI 精确配色、尺寸、间距、提交图几何与状态规则，供复原 PyCharm 2026.2.1 New UI 时查证使用。

本文的定位与边界：

- 本文**不改变**任何产品行为或界面规格。产品行为以 [product-spec.md](product-spec.md) 为准，界面规则以 [ux-spec.md](ux-spec.md) 和 [design-system.md](design-system.md) 为准。
- 运行时令牌的**唯一权威仍是 `web/src/mockup.css`**（design-system.md §11.1 未变）。本文是推导这些令牌时使用的上游输入，不取代它。
- 本文只记录**可测量的数值与规则**（色值、像素数、公式、状态映射），不拷贝或分发 JetBrains 的官方资源文件、字体文件、商标或产品标志。图标按 design-system.md §7.0 的要求测量后自绘等效实现。
- 本文与现有规范出现冲突时，**以现有规范为准，不得按本文自行修改实现**；冲突需先经用户确认（见 §7）。

## 1. 取证锚点

| 项 | 值 |
| --- | --- |
| 仓库 | `https://github.com/JetBrains/intellij-community` |
| 提交 | `475ccbc98599e43c38dc55327ffd90dd7afec74e`（master） |
| 提交时间 | 2026-09-21T14:10:48+00:00 |
| `build.txt` | `263.SNAPSHOT` |
| 文件总数 | 281,865（单提交文件树） |
| Bazel 模块 | 1,882 个 `BUILD.bazel` |
| 源码量 | `.kt` 82,694 + `.java` 91,729 = 174,423 个源文件 |

复现方式（只需文件树与按需取出的单个文件，不需要完整构建）：

```bash
git clone --filter=blob:none --depth 1 --no-checkout https://github.com/JetBrains/intellij-community.git
cd intellij-community
git ls-tree -r HEAD --name-only | wc -l          # 281865
git show HEAD:<路径>                              # 按需取出单个文件，触发惰性拉取
git grep -lE "<模式>" HEAD -- <目录>               # 限定目录的内容检索
```

**"New UI" 在仓库中的内部名是 `expUI`（Experimental UI）。** 所有相关主题文件都在 `platform/platform-resources/src/themes/expUI/` 下。

## 2. 权威文件清单

| 路径 | 内容 | 行数 |
| --- | --- | --- |
| `platform/platform-resources/src/themes/expUI/expUI_light.theme.json` | 浅色主题：89 个命名色 + 104 个 `ui` 键 + 24 个 `icons` 键 | 1,310 |
| `platform/platform-resources/src/themes/expUI/expUI_dark.theme.json` | 深色主题：97 个命名色 + `ui` + `icons` | 1,272 |
| `platform/platform-resources/src/themes/expUI/expUI_light_with_light_header.theme.json` | 浅色**浅色头部**变体：覆盖工具栏、菜单、窗口标签、提示、通知 | 295 |
| `platform/platform-resources/src/themes/metadata/IntelliJPlatform.themeMetadata.json` | 全部主题键的合法定义与类型 | 3,759 |
| `plugins/laf/win10/resources/win10intellijlaf.theme.json` | Windows 10 系统观感主题（非 expUI，`parentTheme: IntelliJ`） | 582 |
| `platform/vcs-log/impl/src/com/intellij/vcs/log/paint/PaintParameters.java` | 提交图几何基准值与缩放公式 | 44 |
| `platform/vcs-log/impl/src/com/intellij/vcs/log/paint/HeadNodePainter.kt` | 提交图 HEAD 节点的三同心圆规则 | 66 |
| `platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt` | 轨道定位、描边、虚线相位、命中判定 | 231 |
| `platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java` | `alignToInt` 的取整与奇偶对齐语义（决定常量到渲染值的偏移） | 520 |
| `platform/icons/src/expui/` | New UI 图标集原件（SVG 测量源） | 1,740 个 SVG |
| `platform/icons-api/README.md` | 图标的数据/渲染分离设计（Swing 与 Compose 共用） | — |
| `platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt` | 工具窗口布局、`isUltrawideLayout` 等规则 | 2,020 |

## 3. New UI 存在两套浅色变体（先确认目标）

仓库里有**两个**浅色 New UI 主题，差异集中在主工具栏与菜单栏：

| 键 | `expUI_light`（`parentTheme: IntelliJ`） | `expUI_light_with_light_header`（`parentTheme: ExperimentalLight`） |
| --- | --- | --- |
| `MainToolbar.background` | `Gray2` = `#27282E`（深） | `Gray13` = `#F7F8FA`（浅） |
| `MainMenu.background` | `Gray2` = `#27282E`（深） | `Gray13` = `#F7F8FA`（浅） |
| `MainToolbar.foreground` | `Gray12` = `#EBECF0`（浅） | `Gray1` = `#000000`（深） |
| `MainMenu.foreground` | `Gray12` = `#EBECF0` | `Gray1` = `#000000` |
| `MainMenu.selectionBackground` | `Gray3` = `#383A42` | `Blue11` = `#D4E2FF` |

Augit 当前浅色 `chrome` 为 `#E9EAEE`，与两套变体的权威值**都不相同**（`Gray13` 是 `#F7F8FA`，`Gray2` 是 `#27282E`）。**该疑点已解开，见 §3.1：`#E9EAEE` 不是任何主题键的值，而是"主工具栏项目配色渐变"叠加在基础背景上的混合产物。**

### 3.1 主工具栏的项目配色渐变（机制，已用参考截图实测验证）

New UI 的主工具栏背景**不是一个固定颜色**，而是按项目配色绘制的水平渐变。来源：`platform/platform-impl/src/com/intellij/ide/ProjectWindowCustomizerService.kt`（793 行）。

| 项 | 规则 | 出处 |
| --- | --- | --- |
| 项目配色数量 | `COLOR_COUNT = 9` | `ProjectWindowCustomizerService.kt:134` |
| 渐变起始色 | 每项目配色一个值，由主题键 `RecentProject.Color{1..9}.MainToolbarGradientStart` 提供 | 同上 `:140-153` |
| 代码内默认起始色（浅/深） | `#DB3D3C/#CE443C`、`#F57236/#E27237`、`#2BC8BB/#2DBCAD`、`#359AF2/#3895E1`、`#8379FB/#7B75E8`、`#7E54B5/#7854AD`、`#D63CC8/#8F4593`、`#954294/#C840B9`、`#E75371/#D75370` | 同上 `:141-149` |
| 自定义颜色的柔化 | `mix(PanelBackground, customColor, 0.18)`（`CUSTOM_COLOR_BALANCE = 0.18`） | 同上 `:135`、`:159-161` |
| 渐变半径 | `Registry.intValue("ide.colorful.toolbar.gradient.radius", 300)` —— **默认 300px** | 同上 `:527` |
| 渐变饱和度 | `Registry.doubleValue("ide.colorful.toolbar.gradient.saturation", 0.85).coerceIn(0, 1) × (颜色 alpha ÷ 255)` —— **默认 0.85** | 同上 `:535-536` |
| 起始色合成 | `blendColorsInRgb(parent.background, projectColor, saturation)` —— 项目色以 0.85 混到面板背景上 | 同上 `:537` |
| 渐变中心 | 跟随**项目部件图标中心**：`ProjectWidgetGradientLocationService.gradientOffsetRelativeToRootPane` | 同上 `:528`、`:593-600` |
| 渐变范围 | 中心向左 `min(offset, 300)`px、向右 `300px`；左端在 x=0 处截断 | 同上 `:540-545` |
| 启用条件 | 仅在 `isActive() && InternalUICustomization.isProjectCustomDecorationGradientPaint` 为真时绘制 | 同上 `:505-513` |
| 位置变化重绘 | `repaintWhenProjectGradientOffsetChanged` | `headertoolbar/MainToolbar.kt:8,193` |

**实测验证。** 对 `artifacts/pycharm-baseline-20260919/pycharm-main.png`（PyCharm 2026.2.1 New UI 真实截图，1659×994）做像素采样：主工具栏区域得到 `#E1E9F1`、`#E0E9F1`、`#DDE8F2`、`#DBE7F3` 等冷蓝色阶；工具窗口、编辑区、标签行为纯 `#FFFFFF`（97%）；分隔线为 `#E3E3E3`/`#EBECF0`。

取该窗口的项目配色为 `Color4`（浅色头部变体 `RecentProject.Color4.MainToolbarGradientStart = #CADFEA`，见 §5.3），以 0.85 混到 `Gray13 #F7F8FA` 得起始色：

```
R: 0.85×0xCA + 0.15×0xF7 = 0xD1
G: 0.85×0xDF + 0.15×0xF8 = 0xE3
B: 0.85×0xEA + 0.15×0xFA = 0xEC   →  #D1E3EC
```

在中心右侧 300px 的渐变中段即为 `#E1E9F1` 量级，与实测吻合。**因此 `#E9EAEE` 是渐变的混合产物，不可能在任何主题文件中找到**——这也解释了为什么全仓检索 `#E9EAEE` 零命中。

**对实现的含义：**

- 标题栏/工具栏的基础底应为 `Gray13`（浅色头部变体）/ `Gray2`（深色），而**不是** `#E9EAEE`。
- 参考窗口显示的是"浅色头部变体 + Color4 项目渐变"的组合，所以浅色目标是 `expUI_light_with_light_header`；这一判断另有独立证据：对截图左上角裁切辨认可见 PyCharm 官方 `PC` 标记与项目部件，工具栏整条为浅色。
- 渐变可用 CSS `linear-gradient` 精确复现：中心在项目部件图标中心，左右各 300px 半径，起始色 = 项目色按 0.85 与基础底混合。


## 4. 命名色板

这是 New UI 的语义色阶，所有组件色都引用这些名字。**浅深两套只有 `Gray14`（`#FFFFFF`）色值相同**，其余全部按主题重新定义，因此两套都必须登记。

### 4.1 灰色阶（结构色的主体）

| 令牌 | 浅色 | 深色 | 用途线索 |
| --- | --- | --- | --- |
| `Gray1` | `#000000` | `#1E1F22` | 浅色头部变体的主文字；深色主题的面板底 |
| `Gray2` | `#27282E` | `#2B2D30` | 深色头部变体的工具栏/菜单；深色主题的 chrome |
| `Gray3` | `#383A42` | `#393B40` | 普通分隔线（深色）；菜单选中底 |
| `Gray4` | `#494B57` | `#43454A` | **失焦选中背景**（深色 `*.selectionInactiveBackground`） |
| `Gray5` | `#5A5D6B` | `#4E5157` | 工具窗口按钮前景（浅色） |
| `Gray6` | `#6C707E` | `#5A5D63` | 状态栏文字、次级引用文字 |
| `Gray7` | `#818594` | `#6F737A` | 辅助说明 |
| `Gray8` | `#A8ADBD` | `#868A91` | **禁用文字**（`*.disabledForeground`） |
| `Gray9` | `#C9CCD6` | `#9DA0A8` | `Tree.hash`、`Component.borderColor`、Stripe 分隔线 |
| `Gray10` | `#D3D5DB` | `#B4B8BF` | 窗口标签分隔线 |
| `Gray11` | `#DFE1E5` | `#CED0D6` | **失焦选中背景**（浅色 `*.selectionInactiveBackground`）、悬停底 |
| `Gray12` | `#EBECF0` | `#DFE1E5` | **普通边框**（`*.borderColor`、`Borders.color`、`OnePixelDivider`）、`tabHoverColor` |
| `Gray13` | `#F7F8FA` | `#F0F1F2` | **面板次级表面**：标题栏、工具栏、状态栏、工具窗口背景 |
| `Gray14` | `#FFFFFF` | `#FFFFFF` | 面板主表面（`EditorTabs.background`、`Menu.background`、Tooltip） |
| `windowsPopupBorder` | `#B9BDC9` | — | 仅浅色注册；Windows 弹层边框 |

### 4.2 蓝色阶（焦点与选中）

| 令牌 | 浅色 | 深色 | 用途线索 |
| --- | --- | --- | --- |
| `Blue1` | `#2E55A3` | `#25324D` | 徽章次级前景 |
| `Blue2` | `#315FBD` | `#2E436E` | **深色选中背景**（`*.selectionBackground`）、链接色（浅色头部变体） |
| `Blue3` | `#3369D6` | `#35538F` | 已修改项前景（`*.modifiedItemForeground`） |
| `Blue4` | `#3574F0` | `#375FAD` | **焦点色与下划线色**（`*.focusColor`/`*.underlineColor`/`Component.focusedBorderColor`）、工具窗口选中按钮底 |
| `Blue5` | `#4682FA` | `#366ACE` | 聚焦边框（浅色 `ActionButton.focusedBorderColor`） |
| `Blue6` | `#588CF3` | `#3574F0` | **深色焦点色与下划线色** |
| `Blue7` | `#709CF5` | `#467FF2` | 拖放边框 |
| `Blue8` | `#88ADF7` | `#548AF7` | 深色主题的通知/提示链接色 |
| `Blue9` | `#A0BDF8` | `#6B9BFA` | 拖放区域底 |
| `Blue10` | `#C2D6FC` | `#83ACFC` | 列表按钮悬停底、搜索选项悬停底 |
| `Blue11` | `#D4E2FF` | `#99BBFF` | **浅色选中背景**（`*.selectionBackground`）、菜单选中底、`TabbedPane.focusColor` |
| `Blue12` | `#EDF3FF` | `#B5CEFF` | **`*.hoverBackground`（浅色）** |
| `Blue13` | `#F5F8FE` | `#D1E0FF` | 辅助表面 |

### 4.3 状态色阶

浅色（深色为独立色阶，此处只列对应参考位）：

| 家族 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Green | `#1E6B33` | `#1F7536` | `#1F8039` | `#208A3C` | `#369650` | `#55A76A` | `#89C398` | `#AFDBB8` | `#C5E5CC` | `#E3F7E7` | `#F2FCF3` | — |
| Yellow | `#A46704` | `#C27D04` | `#DF9303` | `#FFAF0F` | `#FDBD3D` | `#FED277` | `#FEE6B1` | `#FFF1D1` | `#FFF5DB` | `#FFFAEB` | — | — |
| Red | `#AD2B38` | `#BC303E` | `#CC3645` | `#DB3B4B` | `#E55765` | `#E46A76` | `#ED99A1` | `#F2B6BB` | `#FAD4D8` | `#FFEBEC` | `#FFF2F3` | `#FFF7F7` |
| Orange | `#A14916` | `#B85516` | `#CE6117` | `#E56D17` | `#EC8F4C` | `#F2B181` | `#F9D2B6` | `#FFEFE3` | `#FFF4EB` | — | — | — |

深色状态色阶（节选对应位）：

| 家族 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Green | `#436946` | `#4E8052` | `#57965C` | `#5FAD65` | `#73BD79` | `#89CC8E` | `#A0DBA5` | `#B9EBBD` | `#D4FAD7` |
| Yellow | `#9E814A` | `#BA9752` | `#D6AE58` | `#F2C55C` | `#F5D273` | `#F7DE8B` | `#FCEBA4` | `#FFF6BD` | — |
| Red | `#7A4343` | `#9C4E4E` | `#BD5757` | `#DB5C5C` | `#E37774` | `#EB938D` | `#F2B1AA` | `#F7CCC6` | `#FAE3DE` |

深色额外注册了浅色没有的 `Green12`、`Yellow11`、`Orange10`、`Orange11`、`Teal10`–`Teal12`、`Purple11`、`Purple12`。

## 5. 结构与尺寸

### 5.1 通用状态映射（`ui."*"`）

| 状态 | 浅色 | 深色 |
| --- | --- | --- |
| `background` | `Gray13` | `Gray2` |
| `borderColor` | `Gray12` | `Gray1` |
| `selectionBackground` | `Blue11` | `Blue2` |
| `selectionInactiveBackground` | `Gray11` | `Gray4` |
| `selectionForeground` | `Gray1` | — |
| `hoverBackground` | `Blue12` | — |
| `focusColor` / `underlineColor` | `Blue4` | `Blue6` |
| `disabledForeground` | `Gray8` | — |
| `infoForeground` / `separatorForeground` | `Gray7` | — |
| `separatorColor` | `Gray12` | — |
| `modifiedItemForeground` | `Blue3` | — |

### 5.2 尺寸、圆角与间距

以下为浅深**完全一致**的数值（唯一例外是 `Breakpoint.iconHoverAlpha`，浅 0.3 / 深 0.35）。

| 键 | 值 | 对应 Augit 概念 |
| --- | --- | --- |
| `TabbedPane.tabHeight` | `40` | **不是**编辑器标签栏的来源；本仓库 `.java/.kt` 中该键零字面消费者，仅与设置对话框的 `JTabbedPane`、插件管理器标签页及 Jewel Compose 桥相关 |
| `TabbedPane.tabInsets` | `0,12,0,12` | 同上，不用于编辑器标签栏 |
| `TabbedPane.tabSelectionArc` | `4` | 同上 |
| `TabbedPane.hoverColor` | `Gray12` | 标签悬停底 |
| `TabbedPane.contentAreaColor` | `Gray12` | 标签栏与正文的分隔 |
| 编辑器标签栏高度 | `JBUI.scale(28)`（`SingleHeightTabs.UNSCALED_PREF_HEIGHT = 28`，经 `JBTabsImpl` 负内距展开后为 **42px**） | 编辑器标签栏高度。Augit 现行 `--augit-tab-height: 42px` 正确 |
| `EditorTabs.background` | `Gray14` = `#FFFFFF` | 标签栏底色（已由参考截图实测确认：标签行 90–142 行几乎全为纯白） |
| `EditorTabs.underlineHeight` | `4` | 选中标签下划线高度 |
| `EditorTabs.underlineArc` | `4` | 下划线端部弧形 |
| `EditorTabs.unselectedAlpha` | `0.75` | 未选中标签图标不透明度 |
| `EditorTabs.unselectedBlend` | `0.9` | 未选中标签文字混色比 |

### 5.2.1 编辑器标签有两套互斥渲染（已用参考截图确定为经典套）

New UI 下编辑器标签由主题键 `Islands` 选择两套互斥渲染，实现前必须先定：

| | expUI 经典（默认主题） | Islands 多岛（仅 `ManyIslands*` 主题） |
| --- | --- | --- |
| 标签形态 | 平铺，无圆角 | 28px 高圆角卡片（`arc` = 12） |
| 选中表达 | 底部 4px 圆角下划线（`EditorTabs.underlineHeight` / `underlineArc`） | 卡片底色 + 描边；`paintUnderline()` 为空实现 |
| 未选中表达 | 文字按 `unselectedBlend` 混色、图标降透明度 | 卡片底色差异 |

**判定为 Islands 多岛（本节推翻原判定）。** 原判定称"对 `artifacts/pycharm-baseline-20260919/pycharm-main.png` 的标签行（y=90–142）逐行采样，780 个横向像素中几乎全部为纯 `#FFFFFF`……不存在 Islands 那种成片的卡片底色带"——该**取样窗口漏掉了选中标签**：这份 1659×994 截图里选中标签 `audit-clone-seq3.bmp` 位于 x≈835 之后，不在那 780px 窗口内。把采样扩到选中标签（y=140，x 1165–1421）即可看到 **257px 长的 `#E9EAEE` 填充带**，左缘还有 3px `#D1D3D9` 描边；直接查看该截图本身也可见选中标签是**圆角卡片 + 描边 + 关闭叉**。

第二份参考（`artifacts/visual-refinement-2026-09-08/pycharm-history-normal.png`，1792×1120）独立复现同一形态：选中标签 `M↓ design-system.md` 为圆角灰底卡片、带 1px 描边与关闭叉。**两份参考一致，判定改为 Islands 多岛。**

这也解释了原来的"遗留待核"：该截图中观察不到整条 4px 下划线，不是因为焦点问题，而是 **Islands 渲染里 `paintUnderline()` 是空实现——本来就没有下划线**。

选中标签底色取主题别名：`tab-selected-bg-active` = `blue-150` = `#E3EBFE`（聚焦档）、`tab-selected-bg-inactive` = `gray-150` = `#E9EAEE`（未聚焦档）。两份参考截到的都是**未聚焦档**（`#E9EAEE`），聚焦档尚无截图。

**实现待办**：Augit 现行实现与本判定相反，需改为卡片渲染；卡片高度、圆角半径、内外边距与描边色要先按参考图定标（该截图的 DPI 缩放尚未确定，不能直接把像素数当逻辑值），因此单独一轮实施。

| `Tree.rowHeight` | `24` | 文件树行高 |
| `Tree.border` | `4,12,4,12` | 树上下 4、左右 12 |
| `List.rowHeight` | `24` | 列表行高 |
| `List.border` | `4,0,4,0` | 列表上下 4 |
| `List.Button.separatorInset` | `4` | 列表按钮分隔内距 |
| `List.Button.leftRightInset` | `8` | 列表按钮左右内距 |
| `CheckBox.iconSize` | `24` | 复选框图形尺寸 |
| `CheckBox.textIconGap` | `4` | 复选框图文间距 |
| `CheckBox.borderInsets` | `4,4,4,4` | 复选框内距 |
| `RadioButton.iconSize` / `textIconGap` / `borderInsets` | `24` / `4` / `4,4,4,4` | 同上 |
| `Component.arc` | `8` | 组件圆角 |
| `Component.arrowAreaWidth` | `28` | 下拉箭头区宽 |
| `Component.borderColor` | `Gray9` | 组件边框 |
| `Button.arc` | `8` | 按钮圆角 |
| `Button.minimumSize` | `72,28` | 按钮最小尺寸 |
| `TextField.minimumSize` | `49,28` | 输入框最小尺寸 |
| `ComboBox.minimumSize` | `49,28` | 选择框最小尺寸 |
| `ComboBox.padding` | `1,9,1,6` | 选择框内距 |
| `Spinner.minimumSize` | `72,28` | 微调框最小尺寸 |
| `HelpTooltip.verticalGap` | `6` | 帮助提示纵向间距 |
| `OnePixelDivider.background` | `Gray12` | 1px 分隔线 |
| `Borders.color` | `Gray12` | 通用边框 |

### 5.3 标题栏、工具栏与窗口标签

`TitlePane`（未被浅色头部变体覆盖，两套相同）：

| 键 | 值 |
| --- | --- |
| `TitlePane.background` / `inactiveBackground` | `Gray13` |
| `TitlePane.Button.preferredSize` | `40,40` |
| `TitlePane.Button.preferredSize.compact` | `40,30` |
| `TitlePane.Button.hoverBackground` | `#FFFFFF1A`（`expUI_light`）/ `#0000000D`（浅色头部变体） |

`MainToolbar` 与 `MainMenu`：见 §3 的两套变体对照。

`MainWindow.Tab`（浅色头部变体）：

| 键 | 值 |
| --- | --- |
| `selectedBackground` | `Gray13` |
| `selectedInactiveBackground` | `Gray12` |
| `background` | `Gray12` |
| `foreground` | `Gray5` |
| `hoverBackground` | `Gray11` |
| `separatorColor` | `Gray10` |
| `borderColor` | `Gray13` |

### 5.4 状态栏与工具窗口

| 键 | 浅色 | 深色 |
| --- | --- | --- |
| `StatusBar.background` | `Gray13` | — |
| `StatusBar.Widget.foreground` | `Gray6` | — |
| `StatusBar.Widget.hoverForeground` | `Gray1` | — |
| `StatusBar.Widget.hoverBackground` | `Gray12` | — |
| `StatusBar.Widget.pressedBackground` | `Gray11` | — |
| `ToolWindow.background` | `Gray13` | — |
| `ToolWindow.Header.inactiveBackground` | `Gray13` | — |
| `ToolWindow.HeaderTab.hoverBackground` | `Gray12` | — |
| `ToolWindow.Button.foreground` | `Gray5` | `Gray9` |
| `ToolWindow.Button.selectedForeground` | `Gray14` | — |
| `ToolWindow.Button.selectedBackground` | `Blue4` | `Blue6` |
| `ToolWindow.Stripe.separatorColor` | `Gray9` | — |

`ToolWindow` 的显式高度、`StatusBar` 的显式高度、侧栏默认宽度均**不在主题文件中**，属于布局档案或运行时计算，本次未提取（见 §8）。

### 5.5 与版本控制直接相关的主题键

| 键 | 浅色 | 深色 |
| --- | --- | --- |
| `VersionControl.Log.Commit.currentBranchBackground` | `#EDF3FF` | `#283044` |
| `VersionControl.Log.Commit.hoveredBackground` | `#E9EAEC` | `#FFFFFFED` |
| `VersionControl.Log.Commit.unmatchedForeground` | `Gray7` | — |
| `VersionControl.Log.Commit.Reference.foreground` | `Gray6` | — |
| `VersionControl.FileHistory.Commit.selectedBranchBackground` | `#EDF3FF` | — |
| `VersionControl.Log.Graph.saturation` | `0.6` | `0.6` |
| `VersionControl.Log.Graph.brightness` | `0.7` | `0.6` |
| `VersionControl.GitLog.headIconColor` | `Yellow4` = `#FFAF0F` | — |
| `VersionControl.GitLog.localBranchIconColor` | `Green5` = `#369650` | — |
| `VersionControl.GitLog.remoteBranchIconColor` | `Purple4` | — |
| `VersionControl.GitLog.tagIconColor` / `otherIconColor` | `Gray6` | — |
| `VersionControl.Merge.Status.NoConflicts.foreground` | `Green2` = `#1F7536` | — |
| `VersionControl.MarkerPopup.borderColor` | `Gray11` | — |

提交图轨道颜色**不是固定色值**，而是由基础色经 `saturation` / `brightness` 派生。这一点可解释为什么按截图采样主轨道色难以在多分支下保持一致。

### 5.6 Windows 专用覆盖

主题文件里有按操作系统分支的键，Augit 只面向 Windows，这些分支是适用的：

| 键 | `os.default` | `os.windows` |
| --- | --- | --- |
| `Menu.borderColor` | `Gray12` | `windowsPopupBorder` = `#B9BDC9` |
| `ComboPopup.border` | — | `1,1,1,1,#B9BDC9` |
| `ContextHelp.fontSizeOffset` | — | `0` |
| `Popup.borderColor` | — | `windowsPopupBorder` |

另有 `Window.undecorated.border` = `1,1,1,1,#5A5D6B`，以及独立主题 `plugins/laf/win10/resources/win10intellijlaf.theme.json`（`Windows 10 Light`，`parentTheme: IntelliJ`，582 行）——后者是 Windows 10 系统观感主题，**不属于 expUI**，不要在 New UI 复原中混用。

### 5.7 Diff 与文件状态配色（来源是配色方案文件，不是主题文件）

Diff 行底色与文件状态色**不在主题文件里**，而在 `platform/platform-resources/src/themes/expUI/expUI_lightScheme.xml` 与 `expUI_darkScheme.xml` 中。这是 Augit 此前只能用语义色近似的地方。

| 键 | 浅色 | 深色 | 含义 |
| --- | --- | --- | --- |
| `ADDED_LINES_COLOR` | `#7FC784` | `#549159` | 行号槽**实心标记**（VCS 行状态）：新增行 |
| `DELETED_LINES_COLOR` | `#767A8A` | `#868A91` | 行号槽实心标记：删除行（**灰**，不是红） |
| `MODIFIED_LINES_COLOR` | `#88ADF7` | `#375FAD` | 行号槽实心标记：修改行（**蓝**，不是黄） |
| `WHITESPACES_MODIFIED_LINES_COLOR` | `#F7E2CB` | `#52433D` | 仅空白差异的修改行 |
| `DIFF_SEPARATORS_BACKGROUND` | `#E4E6EB` | `#2B2D30` | 分隔条颜色的**废弃回退键**（仅向后兼容）。真正的键是 `DIFF_SEPARATOR_WAVE`，expUI 未定义它，因此实际回退到这里 |
| `WHITESPACES` | — | `#6F737A` | 空白符显示颜色（仅深色注册） |
| `BORDER_LINES_COLOR` | — | — | 变更块边框色。expUI 深浅两套**均未定义**，`ColorKey` 也无代码默认值 ⇒ 取到 null，即 expUI 下不画彩色边框 |
| `DIFF_INSERTED` / `DIFF_DELETED` / `DIFF_MODIFIED`（属性） | `BACKGROUND` = `#BEE6BE` / `#D6D6D6` / `#C2D8F2` | `#294436` / `#484A4A` / `#385570` | **差异正文**的整行底与行内（词级）高亮（第 223 轮订正：三者是**同一批键**，整行底按"该块有无行内差异"取全强度或 `mix(…, 编辑器底, 0.6)`；见 `nui-behavior/08-diff-merge.md` §2.1）。expUI light 只覆写 `DIFF_MODIFIED.BACKGROUND`（`#C2D8F2`，原默认 `#CAD9FA`），深色三值取自 Darcula 段 `DefaultColorSchemesManager.xml:2261-2276`，浅色 INSERTED/DELETED 回落 Default（`:498-513`） |
| `IGNORED_ADDED_LINES_BORDER_COLOR` | `#7FC784` | `#549159` | 忽略空白时的新增行边框 |
| `IGNORED_DELETED_LINES_BORDER_COLOR` | `#767A8A` | `#868A91` | 忽略空白时的删除行边框 |
| `IGNORED_MODIFIED_LINES_BORDER_COLOR` | `#88ADF7` | `#375FAD` | 忽略空白时的修改行边框 |
| `FILESTATUS_ADDED` | `#067D17` | `#73BD79` | 新增文件 |
| `FILESTATUS_MODIFIED` | `#0033B3` | `#70AEFF` | 修改文件 |
| `FILESTATUS_DELETED` | `#6C707E` | `#6F737A` | 删除文件 |
| `FILESTATUS_IDEA_FILESTATUS_MERGED_WITH_CONFLICTS` | `#DE1B2E` | `#DE6A66` | 冲突文件 |

**与现有规范的冲突：** design-system.md §6.2 规定"Diff 使用 `success` 的低对比度背景表示新增，`danger` 的低对比度背景表示删除，`warning` 的低对比度背景表示修改"。权威方案是**新增绿、删除灰、修改蓝**——删除与修改的色相与现有规定不同。既然已确认完全按 New UI 对齐，此处改为按上表取值。**第 223 轮补充**：上表前三个 `*_LINES_COLOR` 是**行号槽实心标记**色族，差异正文的行底/行内层用 `DIFF_*.BACKGROUND`（见该行说明）。

**待补充：** 上表是"线条颜色"，IntelliJ 会以某个 alpha 与编辑区底色混合后作为行背景，另有独立的变更块边框与折叠分隔绘制规则。alpha 值与块边框规则尚未提取（见 §8）。

**并排/三栏视图的分隔条宽度：** 注册表 `diff.divider.width` 默认 **`24`**（`platform/util/resources/misc/registry.properties:1073`），`DiffSplitter` 与 `ThreeDiffSplitter` 都取 `JBUIScale.scale(该值)`；未变更区分隔条的 wave 图案 step = `max(scale(24) / 6, 2)` = **4**（100% 缩放）。三栏合并的规则见 [分册 08](nui-behavior/08-diff-merge.md) §7bis。

## 6. 提交图几何（权威原件）

`platform/vcs-log/impl/src/com/intellij/vcs/log/paint/PaintParameters.java`：

| 常量 | 值 | 含义 |
| --- | --- | --- |
| `WIDTH_NODE` | `16` | 单轨宽度 |
| `CIRCLE_RADIUS` | `4` | 节点半径（直径 8） |
| `THICK_LINE` | `1.5` | 普通连接线宽 |
| `SELECT_THICK_LINE` | `2.5` | 选中行连接线宽 |
| `GRAPH_TEXT_GAP` | `2` | 图形区到文字的间距 |
| `ROW_HEIGHT` | `22` | **基准行高** |

**缩放公式**（这是本节最关键的一条）：

```
scaleWithRowHeight(value, actualRowHeight) = value * actualRowHeight / 22
```

节点半径、轨宽、线宽、选中线宽、图形文字间距**全部**通过该函数按实际行高等比缩放。

`SimpleGraphCellPainter.kt` 的定位与描边规则：

| 项 | 规则 |
| --- | --- |
| 行中心 | `rowCenter = alignToInt(rowHeight / 2, FLOOR, ODD)` |
| 轨宽 | `elementWidth = alignToInt(scaleWithRowHeight(16, rowHeight), FLOOR, ODD)` |
| 第 `position` 轨中心 x | `elementWidth * position + elementCenter`，其中 `elementCenter = alignToInt(elementWidth / 2, FLOOR, null)` |
| 节点直径 | `circleDiameter = alignToInt(2 * scaleWithRowHeight(4, rowHeight), FLOOR, ODD)` |
| 选中节点直径 | `circleDiameter + selectedLineThickness - lineThickness` |
| 描边端点与连接 | `BasicStroke(lineThickness, CAP_ROUND, JOIN_ROUND)` —— **圆端 + 圆角** |
| 虚线相位 | `dash[0] / 2`（使虚线段关于中点对称） |
| 抗锯齿 | `VALUE_ANTIALIAS_ON` |
| 节点命中判定 | `hypot(dx, dy) <= circleRadius` |

`HeadNodePainter.kt` 的 HEAD 节点是**三个同心实心圆**（不是"环 + 圆点"两个图元）：

| 项 | 规则 |
| --- | --- |
| `RADIUS_DELTA` | `2` |
| 外圆直径 | `2 * scaleWithRowHeight(CIRCLE_RADIUS + RADIUS_DELTA, rowHeight)`，填充节点颜色 |
| 中圆直径 | 外圆 − `2 × delta`，用 `commitStyle.background` 填充（形成"环"） |
| 内圆直径 | 中圆 − `2 × delta`，用节点颜色填充（中心点） |
| 选中时 | **只画一个实心圆**（`selectedOuterCircleDiameter`），不画环；该值 = `outerCircleDiameter + selectedLineThickness - lineThickness` |

即：HEAD 外环的径向厚度由 `RADIUS_DELTA` = 2 决定，中心点直径不等于外圆减两倍环厚以外的任何值——按上面的递推就是外圆 − `4 × delta`。

### 6.1 对齐规则：常量值不等于渲染值

`SimpleGraphCellPainter` 对每个尺寸都套了 `PaintUtil.alignToInt(值, scaleContext, FLOOR, ODD)`（端点与直径）或 `alignToInt(值, scaleContext, FLOOR, null)`（中心与半径）。`PaintUtil.alignToInt` 的语义（见 `platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java`）是：**在设备空间**取整，并在指定 `FLOOR` 方向下把值对齐到最近的目标奇偶值，然后把结果换算回用户空间。`FLOOR + ODD` 的含义是"向下取到最近的奇数"。

因此在 100% 缩放下，实际参与绘制的值比设计常量**各少 1**：

| 项 | 设计常量（行高 22 时） | 100% 缩放下对齐后的实际值 |
| --- | --- | --- |
| 轨宽 `elementWidth` | 16 | 15 |
| 节点直径 `circleDiameter` | 8 | 7 |
| 普通线宽 `lineThickness` | 1.5 | 1 |
| 选中线宽 `selectedLineThickness` | 2.5 | 3（先对齐为 1，再由 `coerceAtLeast(lineThickness + 2 × pixel)` 抬到 3） |
| HEAD 外圆直径 | 12 | 11 |
| HEAD 中圆直径 | 12 − 4 = 8 | 7 |
| HEAD 内圆直径 | 8 − 4 = 4 | 3 |
| HEAD 选中单圆直径 | 12 + 2.5 − 1.5 = 13 | 13 |
| 第 0 轨中心 x `elementCenter` | 8 | 8（`null` 奇偶，不对齐） |
| 行中心 y `rowCenter` | 11 | 11（`11` 本身为奇数） |

两条推论，直接影响复原方式：

1. **按截图采样得到的尺寸会系统性偏小 1px，且随 DPI 变化。** 在 125% 或 175% 缩放下对齐发生在设备空间，换算回用户空间后是非整数（例如 15 / 1.25 = 12）。这解释了为什么按截图量出的轨道宽、节点直径常与设计常量差 1 个像素：采样到的是对齐后的设备像素。
2. **几何常量（`PaintParameters`）才是设计意图的权威**，渲染对齐是栅格化细节。比较时应以常量与缩放公式为准，不要用截图反推的值去覆盖常量。

`GraphColorManagerImpl.kt`：`DEFAULT_COLOR = 0`。

## 7. 与现有规范的差异清单

### 7.1 已一致（可直接作为已对齐证据）

| 项 | design-system.md | 权威值 | 结论 |
| --- | --- | --- | --- |
| 浅色 `panel` | `#FFFFFF` | `Gray14` = `#FFFFFF` | 一致 |
| 深色 `panel` | `#1E1F22` | `Gray1` = `#1E1F22` | 一致 |
| 深色 `chrome` | `#2B2D30` | `Gray2` = `#2B2D30` | 一致 |
| 深色 `border` | `#393B40` | `Gray3` = `#393B40` | 一致 |
| 深色 `text` | `#DFE1E5` | `Gray12` = `#DFE1E5` | 一致 |
| 深色 `selection-inactive` | `#43454A` | `Gray4` = `#43454A`（`*.selectionInactiveBackground`） | 一致（权威键直接命中） |
| `surface-radius` | 8–9 | `Component.arc` / `Button.arc` = `8` | 一致 |
| `separator` | 1 | `OnePixelDivider` 为 1px | 一致 |
| 提交图节点直径 | 8 | `2 × CIRCLE_RADIUS` = 8（设计常量；渲染对齐后 7，见 §6.1） | 与常量一致 |
| 提交图连接线宽 | 1.5 | `THICK_LINE` = 1.5（渲染对齐后 1） | 与常量一致 |
| 提交图每轨增量 | +16 | `WIDTH_NODE` = 16（渲染对齐后 15） | 与常量一致 |
| HEAD 外环直径 | 约 12 | `2 × (4 + 2)` = 12（渲染对齐后 11） | 与常量一致 |
| HEAD 环厚 | 线宽 2 | `RADIUS_DELTA` = 2（与奇偶对齐无关） | 一致 |
| HEAD 中心点直径 | 4 | 外圆 − `4 × delta` = 4（渲染对齐后 3） | 与常量一致 |
| 浅色分支标签 | `#FFAF0F` | `Yellow4` = `#FFAF0F`（也是 `GitLog.headIconColor`） | 一致，可确认在色板内 |

### 7.2 数值差异（当前值不在 New UI 色板内或与之不符）

以下按"当前值 → 权威值"列出。**这些是待确认项，不是待执行修改项**；改动前需先按 §3 确定主题变体。

| 令牌 / 项 | 当前（浅色） | 权威参考 |
| --- | --- | --- |
| `chrome` | `#E9EAEE` | `Gray13` = `#F7F8FA`（浅色头部变体）或 `Gray2` = `#27282E`（默认 Light）；见 §3 |
| `panel-muted` | `#F5F8FE` | 该值等于 `Blue13`（在色板内，但注册用途非面板次级表面）；次级表面为 `Gray13` / `Gray12` |
| `border` | `#E3E3E3` | `Gray12` = `#EBECF0`（`*.borderColor`、`Borders.color`） |
| `border-strong` | `#D1D3D9` | `Gray11` = `#DFE1E5`（`Tree.hash`）或 `Gray9` = `#C9CCD6`（`Component.borderColor`） |
| `text` | `#202124` | `Gray1` = `#000000`（浅色头部变体的工具栏与菜单前景） |
| `muted` | `#646870` | `Gray6` = `#6C707E` 或 `Gray7` = `#818594` |
| `faint` | `#A0A4AA` | `Gray8` = `#A8ADBD`（`*.disabledForeground`） |
| `accent` | `#3871E1` | `Blue4` = `#3574F0`（`*.focusColor`、`*.underlineColor`、`Component.focusedBorderColor`） |
| `accent-soft` | `#D0DFFE` | `Blue11` = `#D4E2FF`（浅色 `*.selectionBackground`） |
| `selection-inactive` | `#E9EAEE` | `Gray11` = `#DFE1E5`（浅色 `*.selectionInactiveBackground`） |
| `history-selection-inactive` | `#E9EAEC` | `#E9EAEC` 是 `VersionControl.Log.Commit.`**`hoveredBackground`**；失焦选中参考 `Gray11` |
| `hover` | `#F1F2F4` | 控件悬停为 `Blue12` = `#EDF3FF`；标签栏悬停为 `Gray12` = `#EBECF0` |
| 深色 `muted` | `#9DA1AA` | `Gray9` = `#9DA0A8` |
| 深色 `faint` | `#6F737B` | `Gray7` = `#6F737A` |
| 深色 `accent` | `#548AF7` | 该值等于 `Blue8`，但深色 `*.focusColor` / `*.underlineColor` 引用 `Blue6` = `#3574F0` |
| 深色 `accent-soft` | `#2F466F` | `Blue2` = `#2E436E` |
| 深色 `hover` | `#2D2F33` | `Gray2` = `#2B2D30` |
| 深色 `success` / `danger` / `warning` | `#6AAB73` / `#E37A7A` / `#EBA11B` | 深色 Green / Red / Yellow 家族均无这些值 |
| `row-height`（树/列表） | 27–30 | `Tree.rowHeight` / `List.rowHeight` = 24 |
| `control-height` | 30–31 | `Button` / `TextField` / `ComboBox` `minimumSize` 高 28 |
| `window-title-height` | 44 | `TitlePane.Button.preferredSize` = 40,40 |
| 树/列表行悬停 | 单一 `hover` 令牌，树与 Changes 都有悬停 | 悬停的**有无**按参考实现的**安装差异**决定，不是统一的：`JBTable` 构造时即 `TableHoverListener.DEFAULT.addTo(this)`，所以**表格一定有行悬停**；`TreeHoverListener` 与 `ListHoverListener` 都**不默认安装**（全仓只在协作工具的代码评审树、活动列表与 Switcher 等少数处显式 `addTo`），所以**树与列表默认没有行悬停**。`DefaultTreeUI.getBackground` 的悬停分支要求 `row == TreeHoverListener.getHoveredRow(tree)`，而该属性无人写入时恒为 −1，因此树的悬停分支永不触发。悬停底色三处共用同一个默认值：`JBColor(0xEDF5FC, 0x464A4D)`，失焦变体 `JBColor(0xF5F5F5, 0x464A4D)`（出处：`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2379-2380,2442,2490,2553`、`platform/platform-api/src/com/intellij/ui/table/JBTable.java:194`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:132`） |
| 提交图单轨宽 | 29 | `WIDTH_NODE` = 16（按行高缩放） |
| 提交图节点中心 | 左侧 15px | `elementWidth / 2` = 8（基准行高 22） |
| 提交图选中线宽 | 未登记 | `SELECT_THICK_LINE` = 2.5 |
| 提交图图形文字间距 | 未登记 | `GRAPH_TEXT_GAP` = 2 |
| 提交图轨道色 | 主轨 `#47A1B3` 等固定值 | 由 `saturation` / `brightness` 派生，非固定值 |
| 深色分支标签 | `#F2B846` | 深色 Yellow 家族中最近为 `Yellow7` = `#F2C55C` |

### 7.3 规则冲突

**已确认（2026-09-21）：** 提交图几何按权威规则**等比缩放**。design-system.md §8.3.1 与 §8.3.2 已同步改为"以基准行高 22px 为准，按 `实际行高 ÷ 22` 等比缩放节点半径、轨距、普通线宽、选中线宽与图形文字间距"，并登记基准常量与对齐规则。

实现侧尚未跟随，属于待办（见下）。其余 3 项仍待确认。

---

1. **提交图几何是否随行高缩放 —— 已确认采用权威规则。**
   design-system.md §8.3.1 原规定"图形节点直径、轨距和笔画**不随字号扩大**，只延长行内纵向连线"，与 IntelliJ 的 `PaintParameters.scaleWithRowHeight`（按 `rowHeight / 22` 等比缩放）结论相反。已按权威规则改为等比缩放。

   **实现落差（未完成）：** 当前实现仍是不缩放的固定值，需要改动的是：

   | 位置 | 现状 | 需要的改动 |
   | --- | --- | --- |
   | `web/src/mockup.css` `.commit-graph-svg .graph-primary/secondary/line` | `stroke-width: 1.5` 固定 | 改为按 `实际行高 ÷ 22` 计算 |
   | `web/src/mockup.css` `.graph-head-ring` | `stroke-width: 2` 固定 | 同上 |
   | `web/src/mockup.css` `--augit-graph-width` | 固定 `45px` | 随行高缩放 |
   | `web/src/mockup.js` | SVG 节点半径与轨道偏移为固定值 | 由基准常量与缩放公式推导 |
   | `tools/audit/live-shell.spec.cjs` | 30 处 graph 相关断言 | 补"字号变化后几何等比缩放"的断言与负向验证 |

   改动必须同步 `docs/ux-mockups/mockup.css`（与 `web/src/` 字节一致，由 `verify-ui-assets.ps1` 校验），并按 design-system.md §8.3.2 的待重算值一并处理 `单轨图形区宽 29px` / `节点中心 15px`。

   **另发现一处独立疑点（未确认，未改动）：** `.commit-graph-svg` 上的 `vector-effect: non-scaling-stroke` 会让 `stroke-width` 在**设备空间**固定，即不随 DPI 缩放。这与 design-system.md §4.2"所有值通过统一缩放函数转换，禁止直接按物理像素绘制"和 §10"96/120/144 DPI 必须保持相同的对齐关系"存在冲突：在 175% 缩放下线宽会显得比设计值细。IntelliJ 的做法相反——`PaintUtil.alignToInt` 先在设备空间对齐再换算回用户空间，是 DPI 感知的。此项需单独确认后再改。

2. **基础间距刻度。**
   design-system.md §4.1 规定基础间距优先使用 4px 刻度。
   expUI 权威值中存在非 4 倍数的登记值：`MainToolbar.Icon.insets` = `5,5,5,5`、`HelpTooltip.verticalGap` = 6、`ComboBox.padding` = `1,9,1,6`、`Component.arrowAreaWidth` = 28、`TabbedPane.tabHeight` = 40（相对 4 刻度尚可）、`List.Button.leftRightInset` = 8（符合）。沿用 4px 刻度会系统性偏离参考值。

3. **`chrome` 令牌的粒度。**
   design-system.md 用一个 `chrome` 覆盖标题栏、全局工具栏和状态栏。
   expUI 中 `TitlePane.background` = `Gray13`、`MainToolbar.background` = `Gray13`（浅色头部变体）或 `Gray2`（默认 Light）、`StatusBar.background` = `Gray13`、`MainWindow.Tab.background` = `Gray12`、`EditorTabs.background` = `Gray14`——是**多个不同表面**。单一 `chrome` 令牌在浅色头部变体下可能恰好都等于 `Gray13`，但在默认 Light 变体下会立刻失效。

4. **深色选中态色阶。**
   design-system.md §7 要求列表获得键盘焦点时选中行使用参考蓝色；浅色 `accent-soft` = `#D0DFFE` 与 `Blue11` = `#D4E2FF` 接近，但深色 `accent-soft` = `#2F466F` 与权威 `Blue2` = `#2E436E` 有 `#01, #00, #01` 的逐通道差异。需确认深色选中背景是否统一为 `Blue2`。

### 7.4 由本文新增的可用信息

以下项目此前在 design-system.md 中标记为"待校准"或缺失，现在有权威规则可依：

- 提交图轨道配色规则（`saturation` / `brightness`），替代按截图逐个采样主轨与次轨色。
- 提交图选中线宽 2.5、图形文字间距 2、虚线相位 `dash[0] / 2`、圆端圆角描边。
- HEAD 节点的三同心圆构造（外 12 / 中 8 / 内 4）与"选中时只画实心圆"。
- 失焦选中背景的权威键 `*.selectionInactiveBackground`（浅 `Gray11`、深 `Gray4`）。
- 文件颜色标签的完整映射（`FileColor.Yellow/Green/Orange/Rose/Violet/Blue/Gray`）。
- Windows 专用的弹层边框 `#B9BDC9` 与 `ComboPopup.border` 四元组。
- New UI 图标路径方案（`/expui/...`）与 `platform/icons/src/expui/` 下 1,740 个 SVG 的测量源。

## 8. 尚未覆盖

| 缺口 | 原因 |
| --- | --- |
| 左侧工具窗口默认宽度、最小宽度、底部工具区最小/最大高度 | 不在主题文件内，属布局档案（`ToolWindowDefaultLayoutManager`、`ToolWindowLayoutProfileMigrationHelper`）或运行时计算 |
| `StatusBar` 显式高度、`ToolWindow.Header` 显式高度 | 主题中无高度键，由组件按字体度量计算 |
| 全局左侧工具栏宽度 | 同上 |
| 界面字体族 | expUI 不定义字体族；仓库内字体为 `platform/jewel/.../fonts/inter/Inter-*.ttf`（Inter，OFL）与 JetBrains Mono。Augit 面向中文使用 `Microsoft YaHei UI`，不冲突 |
| `icons` 主题段的完整图标状态映射 | 已取得 24 项（Windows 窗口按钮 8 项、复选框/单选钮状态 SVG、`ColorPalette`），未逐一测量 1,740 个 SVG 的几何 |
| 默认深色头部变体的实际启用条件 | 需对照 PyCharm 2026.2.1 的运行界面确认 |

`icons` 段中与该产品范围相关的部分（Windows 窗口按钮与复选框状态）：

| 键 | 值 |
| --- | --- |
| `/windows/minimize.svg` 等 8 个 | `/expui/windows/{minimize,maximize,restore,close}{,Inactive}.svg` |
| 复选框 / 单选钮状态 | `/themes/expUI/icons/dark/{checkBox,radio}*.svg`（共 16 个状态） |
| `ColorPalette.Checkbox.Background.Default` | `Gray14` |
| `ColorPalette.Checkbox.Border.Default` | `Gray8` |
| `ColorPalette.Checkbox.Background.Selected` | `Blue4` |
| `ColorPalette.Checkbox.Border.Selected` | `Blue4` |
| `ColorPalette.Checkbox.Focus.Wide` | `Blue4` |
| `ColorPalette.Checkbox.Foreground.Selected` | `Gray14` |
| `ColorPalette.Checkbox.Foreground.Disabled` | `Gray9` |
| `ColorPalette.Checkbox.Background.Disabled` | `Gray13` |
| `ColorPalette.Checkbox.Border.Disabled` | `Gray11` |

## 9. 合规约束

- **代码许可**：仓库中单个源文件头部声明 Apache 2.0（例如 `newUiUtil.kt`、`HeadNodePainter.kt`）。
- **仓库根许可**：根目录 [`LICENSE.txt`](https://raw.githubusercontent.com/JetBrains/intellij-community/master/LICENSE.txt) 的是 **JETBRAINS OPEN-SOURCE BUILD TERMS v1.3**（2026-06-15 生效），而非 Apache 2.0 文本，附加了个人数据处理、运行时数据发送、更新检查与捷克法仲裁条款；GitHub 的许可证识别结果为 `Other / NOASSERTION`。`NOTICE.txt` 声明 "This software includes code from IntelliJ IDEA, Copyright (C) JetBrains s.r.o."。
- **本项目策略**：本文只记录数值与规则，按 design-system.md §7.0 使用项目内自绘等效实现，**不分发** JetBrains 的图标 SVG、字体文件、商标或产品标志。许可讨论不构成对分发的授权判断；若未来需要移植具体算法代码，必须单独说明归属与许可并更新 `THIRD-PARTY-NOTICES.md`。
- **本文不覆盖 AI 集成、代码生成或产品能力范围变更。**

### Git 引用标签的分组配色（`VcsLogStandardColors.Refs`）

来源：`platform/vcs-log/impl/src/com/intellij/vcs/log/VcsLogStandardColors.java`；日志侧映射在 `plugins/git4idea/backend/src/log/GitRefManager.kt:264-269`（HEAD→TIP、本地分支→BRANCH、远程→BRANCH_REF、标签→TAG，各键都可用 `VersionControl.GitLog.*IconColor` 覆盖）。

| 分组 | 浅色 | 深色 |
| --- | --- | --- |
| HEAD（`TIP`） | `#FFD100` | `#E1C731` |
| 本地分支（`BRANCH`） | `#3CB45C` | `#3CB45C` |
| 远程分支（`BRANCH_REF`） | `#9F79B5` | `#9F79B5` |
| 标签（`TAG`） | `#7A7A7A` | `#999999` |
| 其他（`LEAF`） | `#8A2D6B` | `#C31E8C` |

2026 参考图日志首行的 `main` 标签正是这份配色：**描边绿（本地分支）+ 内点黄（HEAD）**。
