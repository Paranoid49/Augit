# 02 树与列表的交互与选择状态机

本节要点：

1. IntelliJ 的「失焦保留选中项」不是靠状态快照实现的，而是**每次绘制时用 `hasFocus()` 现算颜色**：`RenderingUtil.isFocused()` → `CurrentTheme.{Tree,List,Table}.Selection.background(focused)`。焦点变化只触发重绘，不触发任何选择事件，因此「不取消选择、不打开文件、不请求 Diff、不改勾选」是结构上成立的，不需要额外的守卫代码。
2. 键盘导航由 `TreeAction` 全量接管（Home/End/↑/↓/←/→/PageUp/PageDown，Shift 扩展选择，Ctrl 只移动 lead），每次选中后固定调用 `TreeUtil.scrollToVisible(..., false)`；滚动时把可视矩形扩为 5 倍行高并上移 2 倍行高，即目标行上方、下方各留 2 行上下文。
3. 类型选择（speed search）用 `MinusculeMatcher`：**大小写不敏感 + 驼峰词首匹配**；树默认 `(shouldMatchFromTheBeginning=false, shouldMatchCamelCase=true)`，没有「输入超时重置」机制，Esc、Enter、方向键或焦点丢失即关闭。
4. 行高 24px（`Tree.rowHeight` / `List.rowHeight`，expUI）；树每级缩进 = `leftChildIndent(7) + rightChildIndent(11)`，首级渲染偏移 = `max(箭头宽/2, 7)`；New UI 的选中高亮左右各缩进 12px、圆角 8px，多行连选时相邻边不圆角。
5. Changes / Unversioned 列表用 Tree 实现；复选框命中区只有左侧 `JCheckBox 首选宽度 × 行高`，点击复选框会**先把该行设为选中**，再对所有已选中项统一切换勾选状态。

---

## 0. 权威取值速查

### 0.1 主题键与颜色

| 键 | Light（expUI） | Dark（expUI） | 说明 |
| --- | --- | --- | --- |
| `Tree.background` | `Gray13` = `#F7F8FA` | `Gray2` = `#2B2D30` | 默认取 `*` 通配 |
| `Tree.foreground` | `Label.foreground(false)` | 同左 | |
| `Tree.selectionBackground` | `Blue11` = `#D4E2FF` | `Blue2` = `#315FBD` | 有焦点时选中背景 |
| `Tree.selectionInactiveBackground` | `Gray11` = `#DFE1E5` | `Gray4` = `#43454A` | 失焦保留态背景 |
| `Tree.selectionForeground` | `Gray1` = `#000000` | `Gray12` = `#DFE1E5` | |
| `Tree.selectionInactiveForeground` | `Gray1` | `Gray12` | 失焦态前景 |
| `Tree.hoverBackground` | `Blue12` = `#EDF3FF` | `Gray3` = `#393B40` | |
| `Tree.hoverInactiveBackground` | `Gray11` | `Gray11` | |
| `Tree.rowHeight` | `24` | `24` | |
| `Tree.border` | `4,12,4,12`（上,左,下,右） | `4,12,4,12` | Tree 组件内边距 |
| `Tree.hash` | `Gray11` = `#DFE1E5` | `Gray5` = `#5A5D6B` | 缩进参考线颜色 |
| `Tree.Selection.arc` | `8`（默认值） | `8` | 选中高亮圆角 |
| `Tree.forceFocusedSelectionForeground` | `false`（expUI） | `false` | |
| `List.rowHeight` | `24` | `24` | |
| `List.border` | `4,0,4,0` | `4,0,4,0` | |
| `List.selectionBackground` / `selectionInactiveBackground` | 同 `*` 通配：`#D4E2FF` / `#DFE1E5` | `#315FBD` / `#43454A` | |

- expUI 主题里 `Tree` 段的显式内容为：折叠图标 `chevronRight`、展开图标 `chevronDown`（展开/收起两个状态共用同一图标文件，只有 `collapsedSelectedIcon` 之类的键名区分）、`forceFocusedSelectionForeground: false`、`rowHeight: 24`、`hash`、`border`（来源：`platform/platform-resources/src/themes/expUI/expUI_light.theme.json:794`、`platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:763`）。
- `"selectionBackground": "Blue11"`、`"selectionInactiveBackground": "Gray11"`、`"selectionForeground": "Gray1"`、`"selectionInactiveForeground": "Gray1"`、`"hoverBackground": "Blue12"` 定义在 `"*"` 通配段，Tree/List/Table 都继承（来源：`platform/platform-resources/src/themes/expUI/expUI_light.theme.json:107`）。
- 调色板：`Gray1 #000000`、`Gray2 #27282E`、`Gray3 #383A42`、`Gray4 #494B57`、`Gray10 #D3D5DB`、`Gray11 #DFE1E5`、`Gray12 #EBECF0`、`Gray13 #F7F8FA`、`Gray14 #FFFFFF`、`Blue4 #3574F0`、`Blue11 #D4E2FF`、`Blue12 #EDF3FF`（来源：`platform/platform-resources/src/themes/expUI/expUI_light.theme.json:8`）。
- 暗色调色板：`Gray1 #1E1F22`、`Gray2 #2B2D30`、`Gray3 #393B40`、`Gray4 #43454A`、`Blue2` / `Gray4` 见上表（来源：`platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:9`）。
- **注意**：旧主题 `intellijlaf` 的取值与 New UI 不同（`Tree.rowHeight: 20`、`Tree.border: 0,4,0,0`、`forceFocusedSelectionForeground: true`、`List.rowHeight: 20`），Augit 目标为 2026.2 New UI，**必须用 expUI 一组值**（来源：`platform/platform-resources/src/themes/intellijlaf.theme.json:404`、`platform/platform-resources/src/themes/intellijlaf.theme.json:995`）。
- 代码内置兜底值：`DEFAULT_RENDERER_SELECTION_BACKGROUND = (0x3875D6, 0x2F65CA)`、`DEFAULT_RENDERER_SELECTION_INACTIVE_BACKGROUND = (0xD4D4D4, 0x0D293E)`、`DEFAULT_RENDERER_HOVER_BACKGROUND = (0xEDF5FC, 0x464A4D)`、`DEFAULT_RENDERER_HOVER_INACTIVE_BACKGROUND = (0xF5F5F5, 0x464A4D)`——这些只在主题缺键时生效，New UI 下都会被主题覆盖（来源：`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2376`）。

### 0.2 尺寸与几何

| 项 | 值 | 来源 |
| --- | --- | --- |
| 行高（Tree/List/Table） | 24px（`JBUI.scale(24)`，可被 `Tree.rowHeight`/`List.rowHeight` 覆盖） | `platform/util/ui/src/com/intellij/util/ui/JBUI.java:2515`、`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2395` |
| 左缩进 `Tree.leftChildIndent` | 7 | `platform/platform-resources/src/themes/intellijlaf.theme.json:1001` |
| 右缩进 `Tree.rightChildIndent` | 11 | `platform/platform-resources/src/themes/intellijlaf.theme.json:1003` |
| 箭头与渲染器最小间隙 `GAP` | 2 | `platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:22` |
| 选中高亮水平内缩 `HORIZONTAL_SELECTION_OFFSET` | 12 | `platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:97` |
| 选中高亮圆角 | `Tree.Selection.arc`，默认 8 | `platform/util/ui/src/com/intellij/util/ui/JBUI.java:2505` |
| 收缩长选中高亮时右侧额外余量 | `JBUI.scale(4)` | `platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:373` |
| 展开箭头图标画布 | 16×16（`chevronRight` 折线 (6,11.5)→(9.5,8)→(6,4.5)；`chevronDown` 折线 (11.5,6.25)→(8,9.75)→(4.5,6.25)；描边圆头，浅色主题描边色 `#818594`） | `platform/icons/src/expui/general/chevronRight.svg`、`platform/icons/src/expui/general/chevronDown.svg` |
| 复选框图标画布 | 24×24 viewBox，方框 `x=4.5 y=4.5 w=15 h=15 rx=2.5`，选中填充 `Blue4`，勾形折线 `(8,12.5)→(11,15.5)→(16.5,9)` 宽 2 圆头；不确定态为 `x=6.5 y=11 w=11 h=2 rx=1` 的白色横杠 | `platform/platform-resources/src/themes/expUI/icons/dark/checkBox.svg`、`checkBoxSelected.svg`、`checkBoxIndeterminateSelected.svg` |
| 控件画布宽度/高度 | 四个状态图标宽高的最大值（`DefaultControl.getWidth()/getHeight()`） | `platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultControl.java:40`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultControl.java:47` |
| 展开控件命中区高度 | `2 + 控件高`，若小于行高则垂直居中 | `platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:549` |
| 滚动余量 | 目标行上方保留 2 行，可视矩形扩到 5 行 | `platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1932` |
| 扩展提示（截断文本浮层）延迟 | 10ms | `platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:292` |
| 滚动后二次校正延迟 | 5ms | `platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1904` |

---

## 1. 选中与焦点状态机

### 1.1 核心机制：颜色在绘制时按焦点现算

- 选中背景永远由「是否选中 + 当前是否有焦点」两个布尔量在绘制时求值，`selected ? Selection.background(focused) : BACKGROUND`。焦点不参与选择模型（来源：`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2507`、`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2387`）。
- 「是否有焦点」的判定顺序是：`component.hasFocus()` → 否则查 `FOCUSABLE_SIBLING` 客户端属性指向的兄弟组件是否有焦点 → 否则若设置了 `ALWAYS_PAINT_SELECTION_AS_FOCUSED` 且窗口 active，则仍算有焦点（来源：`platform/util/ui/src/com/intellij/ui/render/RenderingUtil.java:188`、`platform/util/ui/src/com/intellij/ui/render/RenderingUtil.java:194`）。**【可直接实现】**（Web 端等价物：`element.contains(document.activeElement)` 或记录一次 `focusout` 后的 `relatedTarget`；不需要保存选中快照）。
- 树绘制的实际分支：`focused = RenderingUtil.isFocused(tree)`；`lead = focused && row == getLeadSelectionRow()`；`selectedControl = selected && focused`——**展开/收起箭头只有在「行被选中且树有焦点」时才使用 selected 版图标**（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:353`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:355`）。**【可直接实现】**
- 暗色主题下有一个额外抑制：如果选中背景本身已经很暗（`isDark(background)`），则不再用 selected 箭头图标（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:405`）。**【需推断】**（规则明确，但 Augit 的暗色选中背景 `#43454A` 是否触发 `isDark` 需要按同一亮度算法验证；标「需复核实」）。

### 1.2 失焦时到底发生了什么

- `WideSelectionListUI.createFocusListener()` 覆写：单选模式下交给 Swing 默认实现；**多选模式下只调用 `list.repaint()`**，不做任何选择模型操作（来源：`platform/platform-api/src/com/intellij/ui/components/WideSelectionListUI.java:283`）。这是「失焦保留选中」最直接的代码依据。
- `com.intellij.ui.treeStructure.Tree` 自己加了 `MyFocusListener`，`focusGained` / `focusLost` 都只遍历当前选中路径并 `repaint(bounds)`，注释明确说是为了修补 `BasicTreeUI.FocusHandler` 只重绘 lead 行的问题（来源：`platform/platform-api/src/com/intellij/ui/treeStructure/Tree.java:1196`、`platform/platform-api/src/com/intellij/ui/treeStructure/Tree.java:287`）。
- 因此「点击同一工具窗口的工具条输入框」后的完整链路是：输入框 `requestFocus` → 列表/树收到 `focusLost` → 只重绘选中行 → 下一次绘制时 `isFocused()` 返回 false → 背景换成 `selectionInactiveBackground`、前景换成 `selectionInactiveForeground` → **没有 `TreeSelectionListener` / `ListSelectionListener` 被触发**（来源：`platform/util/ui/src/com/intellij/ui/render/RenderingUtil.java:105`、`platform/platform-api/src/com/intellij/ui/components/WideSelectionListUI.java:283`）。**【可直接实现】**
- 与产品规格的对应关系：因为焦点变化不经过选择模型，「不取消选择、不打开文件、不请求 Diff」是结构性保证；「不改变 Git 文件勾选状态」也成立，因为勾选状态存放在独立的 inclusion model 中，与焦点/选择无关（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:198`）。**【可直接实现】**
- 例外：Project View 的树额外挂了一个 FocusListener，在 `focusGained`/`focusLost` 时**仅当选中项多于 1 个**才重新发一次选择变更通知，目的是刷新状态栏的「N 个元素已选中」文案（来源：`platform/lang-impl/src/com/intellij/ide/projectView/impl/AbstractProjectViewPaneWithAsyncSupport.java:151`、`platform/lang-impl/src/com/intellij/ide/projectView/impl/AbstractProjectViewPaneWithAsyncSupport.java:183`）。这是选择**通知**的重发，不是选择内容的改变。**【可直接实现】**（Augit 复刻时若无「状态栏多选计数」需求，可省略）。

### 1.3 前景色与 `forceFocusedSelectionForeground`

- 渲染器写文本时：只有当 `selected && isFocused() && Tree.forceFocusedSelectionForeground()` 三个条件同时成立，才强制用有焦点版的选中前景色；否则用元素自身声明的颜色（来源：`platform/platform-api/src/com/intellij/ui/ColoredTreeCellRenderer.java:174`、`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2542`）。
- New UI（expUI）里该键为 `false`，所以**失焦和获焦都沿用元素自身颜色**，只有背景在变；旧主题 `intellijlaf` 里是 `true`，行为不同（来源：`platform/platform-resources/src/themes/expUI/expUI_light.theme.json:799`、`platform/platform-resources/src/themes/intellijlaf.theme.json:1014`）。**【可直接实现】**
- `calcFocusedState()` 的默认实现就是 `myTree.hasFocus()`，且每个渲染周期只计算一次并缓存（来源：`platform/platform-api/src/com/intellij/ui/ColoredTreeCellRenderer.java:140`、`platform/platform-api/src/com/intellij/ui/ColoredTreeCellRenderer.java:147`）。**【Swing 特有】**（缓存是渲染器复用的产物，Web 端无需模拟）。

### 1.4 lead selection（焦点框）与 Windows 差异

- 非 macOS 上，若 lead 行**未被选中**，会用「有焦点版选中背景色」画一圈 1px 矩形边框；若 lead 行被选中且它上下相邻行也都被选中（多选连选），则在行内缩进 1px 再画一圈树背景色的边框（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:444`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:449`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:184`）。**【可直接实现】**
- 列表侧等价逻辑在 `WideSelectionListUI.paintCell`：`row == leadSelectionIndex && list.hasFocus()` 时才画；未选中画实线框，多选连选时内缩 1px 画背景色内框（来源：`platform/platform-api/src/com/intellij/ui/components/WideSelectionListUI.java:95`、`platform/platform-api/src/com/intellij/ui/components/WideSelectionListUI.java:120`）。**【可直接实现】**
- 渲染器拿到的 `focused` 参数含义是「这一行是 lead 行且列表有焦点」，**不是**「列表整体有焦点」（来源：`platform/platform-api/src/com/intellij/ui/components/WideSelectionListUI.java:74`）。**【可直接实现】**
- `Tree.forceFocusedSelectionForeground` 在本仓库中被读取的位置只有一处（`ColoredTreeCellRenderer`）；任务描述里提到的 `UIUtil.isToUseDumbIcon` 在本版本全仓库无任何引用（已用 ripgrep 全仓检索确认为 0 命中）。**【需复核实】**

### 1.5 Git 提交列表（Log）

- Git log 的提交列表是 Table 而非 Tree/List，但焦点语义一致：`getSelectionBackground(forceFocus, row)` → `hasFocus() ? SELECTION_BACKGROUND : SELECTION_BACKGROUND_INACTIVE`，两者默认分别回落到 `UIUtil.getListSelectionBackground(true/false)`，即 `List.selectionBackground` / `List.selectionInactiveBackground`（来源：`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:144`、`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:1105`、`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:1209`）。**【可直接实现】**
- 它有可覆盖键：`VersionControl.Log.Commit.selectionBackground` / `selectionInactiveBackground` / `selectionForeground` / `selectionInactiveForeground` / `hoveredBackground`（来源：`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:141`）。Augit 若想让历史列表用不同于文件列表的失焦色，应使用独立的语义变量（Augit 现有 `--augit-history-selection-inactive` 已符合该思路）。

---

## 2. 键盘导航

### 2.1 绑定表（树）

`TreeAction` 用 `InputMap` + `ActionMap` 全量安装（若 ActionMap 已有任意键则整体跳过，避免重复安装）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:95`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:492`）。

| 按键 | 行为 | 备注 |
| --- | --- | --- |
| `Home` | 选中第 0 行 | 会跳过分隔符行 |
| `End` | 选中最后一行 | |
| `↑` / `↓`（含小键盘） | 上/下行，方向键**先移动行号再判断循环** | |
| `←` | 若当前行已展开 → 折叠；否则跳到父行 | 父行不可见时回退到「可见的上一个兄弟/上一行」 |
| `→` | 若当前行已展开或是叶子 → 跳到下一可见行；否则展开当前行 | 展开分支会先记 `startMeasuringExpandDuration` 再 `onPathExpanded` |
| `PageUp` / `PageDown` | 见 2.3 | |
| `Shift+↑/↓/Home/End/PageUp/PageDown` | 扩展选择，锚点不变 | |
| `Ctrl+↑/↓`/`Ctrl+Home/End` 等 | 仅移动 lead，不改变选择 | **只在 `DISCONTIGUOUS_TREE_SELECTION` 模式下生效** |
| `Ctrl+PageUp/PageDown`（注册名为 `scrollUpChangeLead` / `scrollDownChangeLead`） | 仅移动 lead | 同上前提 |
| `Ctrl+Shift+Backspace`（下一兄弟） / `Ctrl+Shift+Tab`（上一兄弟） | 注册名 `selectNextSibling` / `selectPreviousSibling` | 找不到兄弟则不动 |

（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:42`、`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:206`）

- **选择后一定滚动到可见**：所有选中动作最后统一调用 `TreeUtil.scrollToVisible(tree, path, false)`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:215`）。**【可直接实现】**
- 方向键**跳过分隔符行**（`SeparatorWithText`）：`findRowExceptSeparator` 循环递增/递减直到遇到非分隔符（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:141`）。**【可直接实现】**
- 循环滚动（从末行 ↓ 回到首行）默认**关闭**：需要 `ide.tree.ui.cyclic.scrolling.allowed=true` **且** 用户设置 `UISettings.getCycleScrolling()` 为真，且屏幕阅读器未激活；扩展选择模式下永远不循环（来源：`platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:2265`、`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:107`、`platform/util/resources/misc/registry.properties:256`）。**【可直接实现】**
- `→` 跳到子节点时会跳过 `LoadingNode`（异步树加载占位）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:226`）。**【需推断】**（Augit 无异步加载占位概念，等价规则可省略）。

### 2.2 `Enter` 与 `Space`

- `Enter` 由 `EditSourceOnEnterKeyHandler` 注册为 `WHEN_FOCUSED` 键盘动作：先尝试「打开/导航到源」；**若当前无法导航（或该快捷键已被 `$EditSource`/`$ViewSource` 动作覆盖），则回退执行该 `Enter` 上原有的动作——对树而言就是展开/折叠**（来源：`platform/platform-api/src/com/intellij/util/EditSourceOnEnterKeyHandler.java:106`、`platform/platform-api/src/com/intellij/util/EditSourceOnEnterKeyHandler.java:44`）。**【可直接实现】**（Augit 的语义是「打开文件」；对目录应按此规则回退为展开/折叠）。
  导航是否请求编辑器焦点由高级设置 `edit.source.on.enter.key.request.focus.in.editor` 决定；是否走异步导航请求由注册表 `ide.navigation.requests`（默认 `true`）决定（来源：`platform/util/resources/misc/registry.properties:2225`、`platform/platform-api/src/com/intellij/util/EditSourceOnEnterKeyHandler.java:58`）。**【Swing 特有】**
- Project View 与 Changes 树都显式安装了 Enter 处理器（来源：`platform/lang-impl/src/com/intellij/ide/projectView/impl/AbstractProjectViewPaneWithAsyncSupport.java:172`）。
- **`Space` 在普通项目树/文件树上没有任何绑定**：全仓检索 `VK_SPACE` 在 `platform/platform-impl/src/com/intellij/ui/tree/ui/`、`platform/platform-api/src/com/intellij/ui/treeStructure/`、Project View 三个目录下均无命中。Space 只在这两类树上有语义：
  - **Changes 列表**：`MyToggleSelectionAction` 绑定 `VK_SPACE`。若当前有选中项 → 切换所有选中项的 include 状态；**若选择为空 → 切换全部可 include 的变更**（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:826`、`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:843`）。**【可直接实现】**
  - **CheckboxTree 系**：Space 切换选中节点的勾选，且**仅在未安装 speed search 时才生效**（`SpeedSearchSupply.getSupply(mainComponent) == null`）；多选时以 lead 行的勾选结果为准，其余选中项被设为同一状态（来源：`platform/platform-api/src/com/intellij/ui/CheckboxTreeHelper.kt:196`、`platform/platform-api/src/com/intellij/ui/CheckboxTreeHelper.kt:111`）。**【可直接实现】**

### 2.3 `PageUp` / `PageDown` 步长

- 步长不是「一屏」，而是：`height = max(可视区高 − 4 × 行高, 1)`，然后取 `getClosestPathForLocation(bounds.x, bounds.y ± height)` 命中的行。
  - `PageDown` 目标 y = 当前行底边 + height；`PageUp` 目标 y = 当前行 y − height（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:158`、`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:171`）。
  - 也就是说：**可视区能放 N 行时，翻页后目标行与当前行相差 N−3 行，目标行之后仍留 3~4 行上下文**。**【可直接实现】**
- lead 为空或路径不可见时：PageDown 选最后一行，PageUp 选第一行（来源：同上）。

### 2.4 「焦点进入列表时选中首个可见行」

- 树上**没有**「获得焦点即选中首行」的规则。唯一的「选首个」发生在 **Project View 初始化恢复展开状态时**：若持久化的树状态为空且当前选择为空，调用 `TreeUtil.promiseSelectFirst(myTree)`，它遍历可见路径、跳过隐藏根、选第一个可见节点（来源：`platform/lang-impl/src/com/intellij/ide/projectView/impl/AbstractProjectViewPane.java:754`、`platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1960`）。
- 导航动作在 lead 为空时的兜底是「选第一行」（`lineDown`/`lineUp`/`selectChild`/`selectParent` 都有该分支）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:118`、`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:220`）。**【可直接实现】**
- 结论：**Augit 不应实现「焦点进入列表就自动选中第一行」**；只有当列表本身为空且处于首次填充时，才按 Project View 的规则选中首行。**【可直接实现】**

---

## 3. 类型选择（speed search / type-ahead）

### 3.1 匹配算法

- 默认比较器为 `new SpeedSearchComparator(false)`（`shouldMatchFromTheBeginning=false`）；树使用 `new SpeedSearchComparator(false, true)`（额外开驼峰匹配）（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:126`、`platform/platform-impl/src/com/intellij/ui/TreeSpeedSearch.java:73`）。
- 归一化规则：若 `shouldMatchCamelCase`，先把模式按驼峰切成词并用 `*` 连接；若不要求从头匹配且模式不以 `*` 开头，则在前面补 `*`（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchComparator.java:53`）。
- 实际匹配器是 `NameUtil.buildMatcher(pattern).withSeparators(hardSeparators).build()`，即 `MinusculeMatcher`。它「允许小写化的驼峰匹配」，用于导航、补全、speed search（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchComparator.java:67`、`platform/util/text-matching/src/com/intellij/psi/codeStyle/MinusculeMatcher.kt:18`）。
- **大小写**：`MinusculeMatcher` 在小写化空间上比较，因此**大小写不敏感**；但大小写完全一致的匹配分值更高（`matchingDegree` 用于排序，speed search 只做「匹配/不匹配」判定，不用分值）（来源：`platform/util/text-matching/src/com/intellij/psi/codeStyle/MinusculeMatcher.kt:56`、`platform/platform-impl/src/com/intellij/ui/SpeedSearchComparator.java:44`）。**【可直接实现】**
- 树默认 `shouldMatchCamelCase=true` + `fromTheBeginning=false`，综合效果：**输入 `abc` 可以命中 `aXbYc`、`fooAbc`、`FooBarConnection` 等多种形态**。**【可直接实现】**

### 3.2 触发、时序与超时

- **没有超时**：不存在「N 毫秒后重置搜索串」的机制。搜索串 = 弹出搜索框中的文本，一直保留到弹出框关闭。
- 触发：在 `KEY_TYPED` 事件上，若字符是字母/数字，或是非空白且在 `PUNCTUATION_MARKS = "*_-+\"'/.#$>: ,;?!@%^&"` 中，则弹出搜索框并把该字符作为初始串，并 `consume()` 事件（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:575`、`platform/platform-api/src/com/intellij/ui/speedSearch/SpeedSearch.java:25`）。
- 搜索框弹出后，每次输入都会插入字符并立即 `updateSelection(findElement(text), text, true)`（自动选中语义）（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:743`）。
- 找不到匹配时，搜索框文本变为错误前景色（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:779`）。
- 关闭条件（`SearchField.processKeyEvent`）：`Enter`、`PageUp`、`PageDown`、`←`、`→` 会关闭弹出框（非 sticky 模式）；`Esc` 关闭并消费事件；空串时 `Backspace` 被消费不做任何事（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:958`）。
- 组件失焦即关闭弹出框（除非 `keepEvenWhenFocusLost()` 返回 true）（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:184`）。
- 搜索框内 `↑/↓/Home/End` 被消费，用于在匹配项之间跳转（`findPreviousElement` / `findNextElement` / `findFirstElement` / `findLastElement`），不会改变搜索文本（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:683`、`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:982`）。
- 循环查找是否绕回由 `UISettings.getCycleScrolling()` 控制（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:479`）。**【可直接实现】**
- **焦点进入时保留搜索文本**只在 `isStickySearch()` 为真时发生（`focusGained` 里读 `SEARCH_TEXT_KEY` 重建弹出框）（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:192`）。**【Swing 特有】**
- 按 `Ctrl+Backspace` 会删除搜索串的最后一个「词」（按驼峰拆词）（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:238`）。**【可直接实现】**
- 搜索框定位：默认贴在组件可视区左上角**上方**（`y = r.y + componentY − popupHeight`），若超出窗口顶边则夹到窗口内；`ide.speed.search.allow.custom.location` 默认 `false`（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:1094`、`platform/util/resources/misc/registry.properties:2211`）。**【可直接实现】**
- Project View 会把 speed search 定位器指向工具窗口头部（`SPEED_SEARCH_LOCATOR` → `getSizeAndLocation`），使搜索框显示在工具窗口标题栏位置（来源：`platform/lang-impl/src/com/intellij/ide/projectView/impl/ProjectViewTree.java:80`、`platform/lang-impl/src/com/intellij/ide/projectView/impl/ProjectViewTree.java:138`）。**【需推断】**（Augit 若不做工具窗口内嵌搜索框，可退回「树上方浮层」）。

### 3.3 搜索范围与自动选中

- **搜索范围**：`TreeSpeedSearch.canExpand` 决定是否遍历折叠节点。Project View 与 Changes 树都用默认 `canExpand=false`（`TreeUIHelperImpl.installTreeSpeedSearch(tree)` → `installOn(tree)` → `canExpand=false`；ChangesTree 显式传 `false`）。因此**只搜索当前可见行**（来源：`platform/platform-impl/src/com/intellij/ui/TreeUIHelperImpl.java:29`、`platform/platform-impl/src/com/intellij/ui/TreeSpeedSearch.java:92`、`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:208`）。**【可直接实现】**
- `canExpand=true` 时用 `TreeUtil.treePathTraverser` 深度遍历全部节点（来源：`platform/platform-impl/src/com/intellij/ui/TreeSpeedSearch.java:248`）。**【可直接实现】**
- 遍历时始终过滤掉 `LoadingNode`（来源：`platform/platform-impl/src/com/intellij/ui/TreeSpeedSearch.java:256`）。**【需推断】**
- 自动选中由注册表 `ide.speed.search.tree.auto.select`（代码默认 `true`）与「本次选中是否由文本变化自动触发」共同决定：`shouldAutoSelect() = !isAutoSelectionBySpeedSearch() || ide.speed.search.tree.auto.select`（来源：`platform/platform-impl/src/com/intellij/ui/TreeSpeedSearch.java:221`）。**【可直接实现】**
- 行的可搜索文本优先取 `NodeDescriptor.toString()`，否则取 `path.getLastPathComponent().toString()`（来源：`platform/platform-impl/src/com/intellij/ui/TreeSpeedSearch.java:59`）。
- 列表侧 `selectElement` 会调用 `ScrollingUtil.selectItem` 并滚动到该项；无匹配时清空选择（来源：`platform/platform-impl/src/com/intellij/ui/ListSpeedSearch.java:96`）。**【可直接实现】**
- `Ctrl+A`（Select All）在 speed search 弹出框激活且选择模式为多选时，会选中**所有匹配当前模式的项**；再次按下则退回单选 lead 项（来源：`platform/platform-impl/src/com/intellij/ui/TreeSpeedSearch.java:274`、`platform/platform-impl/src/com/intellij/ui/ListSpeedSearch.java:129`）。**【可直接实现】**

---

## 4. 展开与折叠

### 4.1 单击箭头 vs 双击名称

- 树的默认 `toggleClickCount` 由设置决定：`expandNodesWithSingleClick` 为真 → 1，否则 2；该设置**默认 false**（`var expandNodesWithSingleClick: Boolean by property(false)`），所以**默认是双击展开**（来源：`platform/platform-api/src/com/intellij/ui/treeStructure/Tree.java:2183`、`platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:141`）。
- 单击展开箭头是独立路径：`BasicTreeUI` 判断点击点是否落在展开控件命中区内，命中则只切换展开状态、**不改变选择**。
  - 命中区计算：`x = painter.getControlOffset(...) + insets.left`，`width = control.getWidth()`，`height = 2 + control.getHeight()` 并在行内垂直居中（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:538`）。
  - `getControlOffset` 对叶子节点或 depth ≤ 0 返回 −1，表示「不绘制控件」，此时 `isLocationInExpandControl` 直接返回 false（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:544`、`platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:49`）。**【可直接实现】**
- `DefaultTreeUI` 覆写 `isToggleEvent`：在 Swing 默认判断之外，**额外要求 `tree.toggleClickCount == event.getClickCount()`**，并调用 `EditSourceOnDoubleClickHandler.isExpandPreferable(tree, tree.getSelectionPath())`，以修正 `clickCount % toggleClickCount == 0` 在单击模式下会误判的问题（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:499`）。**【需推断】**（Swing 事件计数细节，Web 端用 `dblclick` 即可）。
- `isExpandPreferable` 的判定顺序（来源：`platform/platform-api/src/com/intellij/util/EditSourceOnDoubleClickHandler.kt:137`）：
  1. 路径为 null → false；行为为 `NEVER` → false；节点是叶子 → false。
  2. 若**未**安装双击处理器 → true（默认展开）。
  3. 行为为 `NAVIGATABLE` 且该节点有可导航对象 → false（优先打开）。
  4. 行为为 `ALWAYS` → true。
  5. 否则回退到节点自身的 `ExpandOnDoubleClickSupport.expandOnDoubleClick()`，无该接口则 true。
  行为由注册表 `ide.tree.expand.on.double.click` 控制，默认 `DEFAULT`（可选 `ALWAYS` / `NEVER` / `NAVIGATABLE`）（来源：`platform/util/resources/misc/registry.properties:239`、`platform/platform-api/src/com/intellij/util/ui/tree/ExpandOnDoubleClick.java:31`）。
- 双击处理器的 `onDoubleClick` 还有两条前置条件：**点击的路径必须与当前选择路径相同**；且若 `isToggleEvent` 成立则直接返回 false，把展开/折叠留给点击处理（即「非叶子的展开优先级高于打开」）（来源：`platform/platform-api/src/com/intellij/util/EditSourceOnDoubleClickHandler.kt:190`、`platform/platform-api/src/com/intellij/util/EditSourceOnDoubleClickHandler.kt:206`）。**【可直接实现】**
- Changes 列表把切换点击数**硬编码为 2**，与全局设置无关（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesListView.java:91`）。
- CheckboxTree 系把 `getToggleClickCount()` 覆写为 **−1**，以阻止「点击复选框顺带展开/折叠节点」（来源：`platform/platform-api/src/com/intellij/ui/CheckboxTreeBase.kt:87`）。**【可直接实现】**

### 4.2 递归折叠

- `SmartExpander` 安装到 Changes 树上（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:218`、`platform/util/ui/src/com/intellij/ui/SmartExpander.java:24`）。
- 递归折叠由高级设置 `ide.tree.collapse.recursively` 控制，变化时通过 `RecursiveExpandSettingListener` 同步到 `SmartExpander.setRecursiveCollapseEnabled`（来源：`platform/platform-impl/src/com/intellij/ui/tree/RecursiveExpandSettingListener.kt:9`、`platform/util/ui/src/com/intellij/ui/SmartExpander.java:20`）。**【可直接实现】**

### 4.3 自动展开

- 自动展开（只有一个子节点时自动展开该子节点）**默认只对 `BgtAwareTreeModel` 生效**，且要求树 `isShowing()`；可用客户端属性 `AUTO_EXPAND_ALLOWED` 覆盖，用 `AUTO_EXPAND_FILTER` 过滤（返回 true 表示「不要展开」）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:192`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:198`）。
- 触发点是布局缓存检测到「某节点从 0 个可见子节点变为 1 个」时调用 `autoExpandHandler`，后者 `EdtInvocationManager.invokeLaterIfNeeded(() -> tree.expandPath(row))`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeLayoutCache.kt:114`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:838`）。
- Project View 的自定义过滤器：目录且名字不以 `.` 开头时**允许自动展开**。（来源：`platform/lang-impl/src/com/intellij/ide/projectView/impl/ProjectViewTree.java:62`）。**【需推断】**（Augit 是否要自动展开单子节点目录属于产品决策；若要，按此规则实现）。

### 4.4 展开状态持久化

- Project View 用 `TreeState` 保存：按子面板 ID 存取 `TreeState`；恢复时若有持久化状态就 `treeState.applyTo(myTree)`，否则选首行。`TreeState` 是 `JDOMExternalizable`，以 `expand` / `select` / `presentation` / `path` 标签序列化，路径元素按 `id`+`type` 或用户对象匹配（来源：`platform/lang-impl/src/com/intellij/ide/projectView/impl/AbstractProjectViewPane.java:754`、`platform/platform-api/src/com/intellij/ide/util/treeView/TreeState.java:56`、`platform/platform-api/src/com/intellij/ide/util/treeView/TreeState.java:82`）。**【需推断】**（Augit 若要持久化展开状态，键可用「节点相对路径 + 节点类型」，与这里的 `id`+`type` 思路一致）。
- Changes 树用 `TreeStateStrategy` 枚举控制保留策略（`DO_NOTHING` / `ALWAYS_RESET` / `ALWAYS_KEEP` 等），并提供 `setKeepTreeState` / `setScrollToSelection`（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:867`、`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:880`）。**【可直接实现】**
- 批量展开/折叠时（`beginBulkOperation` / `endBulkOperation`），展开事件被抑制、结束后一次性 `updateExpandedPaths`、`updateLeadSelectionRow`、`updateSize`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:781`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:808`）。**【Swing 特有】**

---

## 5. 行高、内距、缩进、箭头与图标间距

### 5.1 行高

- `Tree.rowHeight()`：默认 `JBUIScale.scale(24)`，键为 `Tree.rowHeight`；值 ≤ 0 时回落到默认值（来源：`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2515`、`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2526`）。
- `List.rowHeight()`：默认 `JBUIScale.scale(24)`，键为 `List.rowHeight`（来源：`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2395`）。
- 列表项最小高度由 `UIUtil.updateListRowHeight` 强制：`size.height = max(size.height, UIManager.getInt("List.rowHeight"))`（来源：`platform/util/ui/src/com/intellij/util/ui/UIUtil.java:3079`）。
- `Tree` 组件的首选高度：`rows.size * defaultRowHeight + 变高总偏差`（`rowHeight > 0` 时直接用 `rows.size * rowHeight`）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeLayoutCache.kt:169`）。
- `Tree` 还有一个「额外空行高度」概念用于把树撑满视口（受视口高度限制，最多不超过 `视口高 − 行高`）（来源：`platform/platform-api/src/com/intellij/ui/treeStructure/Tree.java:1346`）。**【需推断】**
- Changes 树在特定注册表开关下把行高固定为渲染器首选高度（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:238`）。**【需推断】**
- **Augit 现状核对**：`web/src/mockup.css` 中 commit 行高为 `27px`、history 行高为 `26px`，均**不是** 24px。若以「100% 复原 PyCharm New UI」为准，应统一到 24px；这是与实现现状的差异，需产品确认（标「需复核实」）。

### 5.2 树的缩进模型（`ClassicPainter`，New UI 实测路径）

设 `controlWidth = 箭头控件宽`（= 四个状态图标宽的最大值），`left = max(controlWidth/2, Tree.leftChildIndent)`，`right = Tree.rightChildIndent`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultControl.java:40`、`platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:94`、`platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:98`）。

- depth 0 → 渲染偏移 0；depth < 0 → 返回 −1，表示该行不绘制（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:37`）。
- depth 1 → 渲染偏移 = `max(controlWidth + left − controlWidth/2 + scale(GAP), left + right)`；depth ≥ 2 → `(depth−1) * (left + right) + 上述首级偏移`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:44`）。
- **每级之间的增量固定为 `left + right`**；首级额外多出 `controlWidth/2 + GAP` 的空间，用于容纳展开箭头（来源：同上）。
- 展开控件（箭头）的 x 偏移：depth ≤ 0 或叶子 → −1（不绘制）；否则 `left − controlWidth/2`，depth ≥ 2 时再加 `(depth−1) * (left + right)`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:49`）。
- 箭头图标在控件框内**水平垂直居中**（`x + (width − iconWidth)/2`, `y + (height − iconHeight)/2`）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultControl.java:54`）。**【可直接实现】**
- **紧凑模式**：`CompactPainter.DEFAULT = (paintLines=null, left=2, right=2, leafIndent=null)`，每级增量 = `left + 2`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/CompactPainter.java:15`、`platform/platform-impl/src/com/intellij/ui/tree/ui/CompactPainter.java:36`）。
- 三种预置画笔：`DEFAULT = ClassicPainter(null,null,null,null)`、`COMPACT = ClassicPainter(null,null,0,null)`（右缩进 0）、`LEAF_WITHOUT_INDENT = ClassicPainter(null,null,null,0)`（叶子不缩进）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/Control.java:40`）。
- 选择顺序：树的客户端属性 `Control.Painter.KEY` → 应用级 `Control.Painter.KEY` → `UISettings.getCompactTreeIndents()` → `ide.tree.painter.classic.compact` → `ide.tree.painter.compact.default` → `DEFAULT`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:102`）。
- 全局缩进微调：注册表 `ide.ui.tree.indent` 默认 `-1`（表示不覆盖）；≥ 0 时**只覆盖右缩进**（来源：`platform/util/resources/misc/registry.properties:1149`、`platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:98`）。
- 缩进参考线（guidelines）默认**不绘制**：`Tree.paintLines` 在 expUI/intellijlaf 主题中未定义或为 false，`getPaintLines` 还要求 `UISettings.getShowTreeIndentGuides()`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:83`、`platform/platform-resources/src/themes/intellijlaf.theme.json:1002`）。线色为 `Tree.hash`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/Control.java:44`）。**【可直接实现】**
- 根节点默认不可见、根句柄默认显示：`installDefaults` 在 `Tree.showsRootHandles` 未定义时设为 `TRUE`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:485`）；Project View 与 Changes 都显式 `setRootVisible(false)` + `setShowsRootHandles(true)`（来源：`platform/lang-impl/src/com/intellij/ide/projectView/impl/AbstractProjectViewPaneWithAsyncSupport.java:168`、`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:203`）。**【可直接实现】**
- 树边框通过 `LookAndFeel.installBorder(tree, "Tree.border")` 安装，即 expUI 的 `4,12,4,12`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:483`、`platform/platform-resources/src/themes/expUI/expUI_light.theme.json:802`）。**【可直接实现】**

### 5.3 图标与文字间距

- 渲染器组件（含图标 + 文本）从「该行的渲染器偏移」处开始摆放，宽度 = `treeX + treeWidth − insets.left − offset` 再减去右侧滚动条占位（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:312`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:416`）。
- 箭头控件与渲染器之间保证的最小间隙是 `GAP = 2`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/ClassicPainter.java:22`）。
- 图标与文字之间的间距由渲染器组件（`ColoredTreeCellRenderer`）自身决定，树 UI 不参与（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:418`）。**【需推断】**（Augit 需按 mockup 现状确定，IntelliJ 侧无单点可引用）。

### 5.4 选中高亮的几何（New UI 特性）

- 触发条件：`ExperimentalUI.isNewUI()` **且** 注册表 `ide.experimental.ui.tree.selection`（默认 `true`）**且** 该树不是 `PlainSelectionTree` **且**（行被选中 **或** 该行是 hover 行）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:360`、`platform/util/resources/misc/registry.properties:2013`）。
- 左边界 = `min(treeX + 12, insets.left + (controlOffset < 0 ? rendererOffset : controlOffset))`；右边界 = `treeX + treeWidth − 12`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:365`）。
- 若该行禁用了「收缩选区」，右边界还要扩到 `渲染器右边缘 + 4`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:372`）。
- 圆角处理：单行或选区不连续时用 `FILL.paint(..., arc)` 画圆角矩形；若该行上方相邻行也被选中，则把圆角矩形顶部用直角补满；下方同理（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:388`）。**【可直接实现】**
- 非 New UI 走 `g.fillRect(helper.getX(), bounds.y, helper.getWidth(), bounds.height)`——**整行通栏、无圆角、无水平内缩**（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:403`）。Augit 目标是 New UI，应采用圆角内缩版。
- 扩展提示浮层的圆角与选区圆角一致（`TreeExpandableItemsHandler` 在 New UI 下 `setBorderArc(Tree.ARC)`）（来源：`platform/platform-impl/src/com/intellij/ui/TreeExpandableItemsHandler.java:90`）。

---

## 6. 滚动与虚拟化

### 6.1 绘制只处理可见行

- `paint()` 从 `cache.getPathClosestTo(0, clipBounds.y − insets.top)` 找到首个可见行，然后逐行向下绘制，**当 `bounds.y + bounds.height >= clipBounds.bottom` 时立即 break**（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:335`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:463`）。**【可直接实现】**（Web 端等价做法：`IntersectionObserver` 或按 `scrollTop/rowHeight` 计算可见区间后只渲染这些行）。
- 布局缓存维护 `NodeList` + `VariableHeightSupport`（行高不固定时维护每行的 y 前缀和增量），并缓存节点的尺寸与行号（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeLayoutCache.kt:29`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeLayoutCache.kt:34`）。**【需推断】**
- 布局缓存按需创建子节点：只有展开的节点才 `ensureChildrenVisible()`，且 `rows` 只包含可见节点（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeLayoutCache.kt:91`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeLayoutCache.kt:502`）。**【可直接实现】**

### 6.2 大模型开关

- `largeModel` 默认被强制关闭：除非注册表 `ide.tree.large.model.allowed`（默认 `false`）为真，或树设置了客户端属性 `LARGE_MODEL_ALLOWED`，否则 `installDefaults`/`setLargeModel`/`setModel` 都会把 `largeModel` 重置为 false（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:188`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:480`、`platform/util/resources/misc/registry.properties:1829`）。
- Project View 与 Changes 树都显式开了大模型：`setLargeModel(true)`（来源：`platform/lang-impl/src/com/intellij/ide/projectView/impl/ProjectViewTree.java:57`、`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:228`）。**【Swing 特有】**（大模型模式是 Swing `JTree` 的性能开关，Web 端用虚拟列表实现同一目标）。

### 6.3 非整行的滚动范围与首选宽度

- `Tree.getScrollableUnitIncrement`：当 Swing 算出 0 且方向向上时，返回 `visibleRect.y`，以便鼠标滚轮能滚回 0（因为 `BasicTreeUI.getPathBounds` 含 insets 导致到不了 0）（来源：`platform/platform-api/src/com/intellij/ui/treeStructure/Tree.java:1394`）。
- 列表侧等价逻辑在 `JBList.adjustIncrement`（来源：`platform/platform-api/src/com/intellij/ui/components/JBList.java:402`）。**【需推断】**
- 首选宽度算法（`ide.tree.experimental.preferred.width` 默认 `true`）：从可见首行开始统计 `max(bounds.x + bounds.width)`，直到「超过可视底部且已用行数 ≥ `visibleRowCount`」才停；再补 insets；若结果小于可视宽度，则按 `ide.tree.prefer.to.shrink.width.on.scroll`（默认 true）和 `ide.tree.preferable.right.margin`（默认 25%）追加右侧余量（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:595`、`platform/util/resources/misc/registry.properties:1832`、`platform/util/resources/misc/registry.properties:1841`）。**【需推断】**

### 6.4 滚动到可见的余量规则（`TreeUtil.scrollToVisible`）

- 签名 `scrollToVisible(tree, path, centered)`：`centered` 由注册表 `ide.tree.autoscrollToVCenter`（默认 `false`）决定（来源：`platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1889`、`platform/util/resources/misc/registry.properties:223`）。
- **横向**：`centered=false` 且树未开启「水平自动滚动」（`ide.tree.horizontal.default.autoscrolling` 默认 `true`，即默认**开启**自动滚动）时，把目标矩形横向拉伸到整个视口宽；否则预留 `scale(20)` px 的控件宽度，宽度取 `viewportWidth / 2` 或全宽（来源：`platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1912`、`platform/util/resources/misc/registry.properties:225`）。
- **纵向**：若视口高于行高、且小于整树高度：`centered` 或「视口 < 5 倍行高」时把目标行**垂直居中**；否则把可视矩形扩为 5 倍行高并**上移 2 倍行高**——即**目标行上方保留 2 行、下方保留 2 行**（来源：`platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1926`、`platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1932`）。**【可直接实现】**
- **二次校正**：滚动后 5ms 再取一次 path bounds 并重滚一次（防止布局尚未完成）；期间树被标记为 `TREE_IS_BUSY`（来源：`platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1901`）。**【需推断】**（Web 端布局是同步的，一般不需要；若用虚拟列表+异步测量则需要）。
- 滚动是否触发重绘：位置变化由 Swing 视口管理，选中变化只重绘受影响的行（`repaintPath` / `repaintRow` / `repaint(bounds)`）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:276`、`platform/platform-api/src/com/intellij/ui/hover/TreeHoverListener.java:64`）。**【可直接实现】**（Web 端等价：只对变化的行做 DOM 更新，避免整表重渲染）。

---

## 7. 复选框（Changes / Unversioned 列表）

### 7.1 命中区

- Changes 树的复选框命中区：**左侧 `myCheckboxWidth × 整行高` 的矩形**。`myCheckboxWidth = new JCheckBox().getPreferredSize().width`（即一个默认复选框的首选宽度）（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:197`、`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:289`）。
- 命中还需要：树显示复选框、树已启用、该路径可 include（`isIncludable`）（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:289`）。**【可直接实现】**
- `CheckboxTree` 系的命中区：取渲染器中复选框组件的 bounds，平移到行坐标；若高度为 0 则退化为 `行高 × 行高` 的正方形；策略 `checkByRowClick` 为真时整行都可点击（来源：`platform/platform-api/src/com/intellij/ui/CheckboxTreeHelper.kt:155`、`platform/platform-api/src/com/intellij/ui/CheckboxTreeHelper.kt:166`）。**【可直接实现】**

### 7.2 点击复选框对选择态的影响

- **Changes 列表：点击复选框会先把该行设为选中**（`setSelectionPath(path)`），然后按「当前所有选中项」统一切换 include 状态（`toggleChanges(selected(...))`），并返回 true 消费事件（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:271`）。
  - 推论：多选状态下点击任一选中行的复选框，会**同时切换全部选中项的勾选**。
- **CheckboxTree 系：点击复选框后也会 `tree.setSelectionRow(row)`**（来源：`platform/platform-api/src/com/intellij/ui/CheckboxTreeHelper.kt:168`）。
- 因此「点击复选框不改变选中态」**不是** IntelliJ 的行为；Augit 若要「点复选框不改变选中」，属于对参考实现的**有意偏离**，需产品确认（标「需复核实」）。
- 复选框点击处理被实现为 mouse listener 而非 Swing 默认 cell 点击，原因见代码注释：`IdeGlassPaneImpl.dispatch` 对 `DnDAware` 组件在选区上的点击不会调用 `processMouseEvent`（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:264`）。**【Swing 特有】**

### 7.3 部分选中（indeterminate）

- 用 `ThreeStateCheckBox`（`State.SELECTED` / `NOT_SELECTED` / `DONT_CARE`），第三态默认关闭（`isThirdStateEnabled = false`），由渲染器通过 `setState(...)` 设置（来源：`platform/platform-api/src/com/intellij/ui/CheckboxTreeBase.kt:118`、`platform/platform-api/src/com/intellij/ui/CheckboxTreeBase.kt:127`、`platform/platform-api/src/com/intellij/ui/CheckboxTreeBase.kt:149`）。
- 父节点第三态的推导规则（`getNodeStatus`）：
  - 若节点无子节点、或关闭了父子联动（`myUsePartialStatusForParentNodes=false`），直接用自身状态。
  - 否则递归求子节点状态：任一子节点为 `DONT_CARE` → 父节点即 `DONT_CARE`；子节点状态不一致 → `DONT_CARE`。
  - 若所有子节点状态一致但与父节点自身状态不同：`DEFAULT_POLICY` 下返回子节点状态，其他策略返回 `DONT_CARE`（来源：`platform/platform-api/src/com/intellij/ui/CheckboxTreeBase.kt:173`）。
- `DEFAULT_POLICY = CheckPolicy(checkChildrenWithCheckedParent=true, uncheckChildrenWithUncheckedParent=true, checkParentWithCheckedChild=false, uncheckParentWithUncheckedChild=true)`，且注释明确标注该策略「保留了一个缺陷，新代码不应使用」，推荐 `PROPAGATE_EVERYTHING_POLICY = (true,true,true,true)`（来源：`platform/platform-api/src/com/intellij/ui/CheckboxTreeHelper.kt:194`、`platform/platform-api/src/com/intellij/ui/CheckboxTreeBase.kt:252`）。**【可直接实现】**（Augit 的 Changes 列表通常只需「文件级勾选 + 目录级聚合显示」，目录的全选/半选建议用 `PROPAGATE_EVERYTHING_POLICY` 语义：全勾→选中，全不勾→未选中，混合→半选）。
- Win10 LAF 下复选框还支持 rollover / pressed 视觉（通过 client property `JCheckBox.rollOver.rectangle` / `JCheckBox.pressed.rectangle` 传递用户对象）（来源：`platform/util/ui/src/com/intellij/util/ui/UIUtil.java:2979`、`platform/platform-api/src/com/intellij/ui/CheckboxTreeBase.kt:154`）。**【可直接实现】**
- 复选框图标几何见 0.2 节；不确定态为实心方框 + 白色圆角横杠（**不是**灰色勾）。**【可直接实现】**

### 7.4 勾选状态与选择状态相互独立

- Project View 的选中变化只影响 `Selection`；Changes 的勾选状态存于 inclusion model，通过 `InclusionListener` 通知（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:198`）。**【可直接实现】**
- 这正好支撑产品规格的「失焦不改变 Git 文件勾选状态」——两者本就不共用存储。

---

## 8. 悬停与工具提示

### 8.1 悬停高亮

- 悬停行以客户端属性记录（树 `TreeHoveredRow`、列表 `ListHoveredIndex`），值为 `-1` 时清除属性（来源：`platform/platform-api/src/com/intellij/ui/hover/TreeHoverListener.java:47`、`platform/platform-api/src/com/intellij/ui/hover/ListHoverListener.java:47`）。
- 更新时机：`mouseEntered` 等同于 `mouseMoved`；`mouseExited` 置为 −1；仅当行号发生变化才回调（来源：`platform/platform-api/src/com/intellij/ui/hover/TreeHoverListener.java:20`）。**没有悬停延迟**（无 timer）。**【可直接实现】**
- 行号计算：树用 `TreeUtil.getRowForLocation(tree, x, y)`，列表用 `list.locationToIndex(new Point(x, y))`（来源：`platform/platform-api/src/com/intellij/ui/hover/TreeHoverListener.java:27`、`platform/platform-api/src/com/intellij/ui/hover/ListHoverListener.java:27`）。**【可直接实现】**
- **只有安装了 hover listener 的组件才会绘制悬停背景**：`DefaultTreeUI.getBackground` 在非选中分支中检查 `row == TreeHoverListener.getHoveredRow(tree)`，然后取 `RenderingUtil.getHoverBackground(tree)`，该函数在 `PAINT_HOVERED_BACKGROUND` 显式为 false 时返回 null（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:132`、`platform/util/ui/src/com/intellij/ui/render/RenderingUtil.java:180`）。
- **重要发现**：`TreeHoverListener.DEFAULT` 在安装侧只在少数位置被显式添加（如 `CodeReviewProgressTreeModel.kt`、`Switcher.kt`、`JBTable`），**Project View 树与 Changes 树本身没有安装它**——它们走的是 `TreeExpandableItemsHandler` 的 `getCellKeyForPoint`，该处**优先读 `TreeHoverListener.getHoveredRow`，读不到才回退到 `getRowForLocation`**（来源：`platform/platform-impl/src/com/intellij/ui/TreeExpandableItemsHandler.java:100`）。也就是说：**Project View 的行本身没有悬停背景高亮**，悬停行为只用于「截断文本提示」。
  - 结论：**树行悬停高亮在 PyCharm New UI 里并不是全局规则**，Augit 若给所有树行加 hover 背景，属于额外增强（标「需复核实」）。
- 悬停颜色优先级：选中 > 悬停 > `ColoredItem` 自定义色 > `BackgroundSupplier` > `TreePathBackgroundSupplier`（树）/ `ListCellBackgroundSupplier`（列表）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:122`、`platform/platform-api/src/com/intellij/ui/components/WideSelectionListUI.java:125`）。**【可直接实现】**
- 悬停颜色在失焦时使用 `hoverInactiveBackground`（来源：`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2555`）。**【可直接实现】**

### 8.2 截断文本的扩展提示（expansion hint）

- 开关：注册表 `ide.expansion.hints.enabled` 默认 `true`；首次启用还需组件加载完成（来源：`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:95`、`platform/util/resources/misc/registry.properties:389`）。
- **延迟 10ms**：`handleSelectionChange` 取消上一次定时器并 `updateAlarm.request(10, ...)`；这 10ms 用于合并快速鼠标移动（来源：`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:281`、`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:292`）。**【可直接实现】**
- 出现条件（`calcToolTipDetails`）：
  - 行必须**纵向完全可见**：行顶 < 视口顶 → 放弃；行底 > 视口底 → 放弃（来源：`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:458`）。
  - 内容必须**横向被裁剪**：可用宽度 = `cellMaxX − visMaxX`，≤ 0 则不显示（来源：`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:476`、`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:494`）。
  - 浮层**贴在视口右边缘**，`y` 与行对齐；若 `location.x` 恰好落在屏幕左边界（跨屏）则放弃（来源：`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:467`、`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:473`）。
  - 与其它可见窗口相交时不显示（`noIntersections`）（来源：`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:328`、`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:371`）。
- 消失条件：鼠标离开组件（`ide.hide.expandable.tooltip.owner.mouse.exit` 默认 `true`）、双击、焦点丢失、层级变化、组件隐藏或改变大小、选中项改变（来源：`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:156`、`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:167`、`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:267`、`platform/util/resources/misc/registry.properties:386`）。**【可直接实现】**
- 提示内容是**把该行渲染器原样重绘**到一个浮层里（而不是取一段纯文本），因此图标与颜色都会保留；浮层高度 = 行高，宽度 = 被裁剪掉的宽度（来源：`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:504`、`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:550`）。**【需推断】**（Augit 用 CSS 复制行内容即可，但要注意宽度与被裁剪量一致）。
- **Swing `TooltipManager` 的标准工具提示**在这条路径上未被改写：全仓检索 `setInitialDelay` 只命中两处按钮类组件，树/列表没有覆盖初始延迟——即树/列表的 `JToolTip`（如节点自身注册了 `ToolTipText`）沿用 Swing 默认 750ms 初始延迟 / 500ms 重现延迟 / 4000ms 消失延迟（来源：`platform/platform-impl/src/com/intellij/openapi/wm/impl/SquareStripeButton.kt:131`、`platform/platform-impl/src/com/intellij/toolWindow/MoreSquareStripeButton.kt:154`）。**【需推断】**（默认值来自 JDK `TooltipManager`，本仓库未覆盖；标「需复核实」）。
- 浮层也参与选中高亮的圆角（New UI 下 `setBorderArc(Tree.ARC)`）（来源：`platform/platform-impl/src/com/intellij/ui/TreeExpandableItemsHandler.java:90`）。**【可直接实现】**

---

## 9. 对 Augit 的落地清单（按类别汇总）

### 9.1 可直接实现（HTML/CSS/C# 能表达）

1. 选中态用两个独立 CSS 类/变量表达：`--augit-selection`（有焦点）与 `--augit-selection-inactive`（失焦），由容器上的 `.has-focus` / `.inactive` 类切换；**切换只重写样式，不动选中数据**。Augit 现有 `.tree-row.selected.inactive` / `.check-row.selected.inactive` 结构已经符合（来源：`platform/util/ui/src/com/intellij/util/ui/JBUI.java:2530`、`platform/platform-api/src/com/intellij/ui/components/WideSelectionListUI.java:283`）。
2. 失焦判定用「列表容器是否包含 `document.activeElement`」，在 `focusin`/`focusout`（`focusout` 用 `queueMicrotask` 等待新焦点落定）时更新。这是 Augit 现有的 `update()` 模式（来源：`platform/util/ui/src/com/intellij/ui/render/RenderingUtil.java:194`）。
3. 键盘导航按 2.1 的表绑定；每次导航后 `scrollIntoView({block:'nearest'})`，并额外保留 2 行上下文（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:215`、`platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1932`）。
4. PageUp/PageDown 用 `max(可视行数 − 4, 1)` 行的步长（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/TreeAction.java:165`）。
5. Speed search：无超时；字母/数字/标点直接触发；`Esc`/`Enter`/方向键关闭；匹配用小写化的驼峰子串匹配（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:575`、`platform/platform-api/src/com/intellij/ui/speedSearch/SpeedSearch.java:25`）。
6. `Enter`：能打开就打开，不能打开（目录等）则回退为展开/折叠（来源：`platform/platform-api/src/com/intellij/util/EditSourceOnEnterKeyHandler.java:106`）。
7. `Space`：只在 Changes 列表绑定，语义是「toggle include 所有选中项；无选中则 toggle 全部」（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:843`）。
8. 选中高亮：左右各 12px 内缩、8px 圆角，多行连选时相邻边直角化（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:97`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:388`）。
9. 复选框：命中区只覆盖左侧复选框宽度；不放宽到整行（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:289`）。
10. 悬停：即时生效、无延迟、单行高亮；切换时只重绘旧行与新行（来源：`platform/platform-api/src/com/intellij/ui/hover/TreeHoverListener.java:38`）。
11. 截断提示：10ms 合并延迟，仅在「行纵向完全可见 + 内容横向被裁剪」时显示（来源：`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:292`、`platform/platform-impl/src/com/intellij/ui/AbstractExpandableItemsHandler.java:458`）。

### 9.2 Swing 特有（依赖 Swing 组件模型，不可照搬）

1. 渲染器组件复用与 `CellRendererPane`、`removeCachedRenderers`、`painting` 原子标志（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:281`）。
2. `largeModel` / `AbstractLayoutCache` / `VariableHeightSupport` 的组合缓存与 `getPreferredHeight` 公式（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeLayoutCache.kt:169`）。
3. 批量展开时抑制展开事件（`beginBulkOperation` / `endBulkOperation` / `MyTreeExpansionListener`）（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:808`）。
4. `RenderingUtil.FOCUSABLE_SIBLING` / `ALWAYS_PAINT_SELECTION_AS_FOCUSED` 两个焦点旁路钩子（来源：`platform/util/ui/src/com/intellij/ui/render/RenderingUtil.java:29`、`platform/util/ui/src/com/intellij/ui/render/RenderingUtil.java:42`）。
5. Swing `TooltipManager` 的初始/重现/消失延迟（750/500/4000ms）与 `JToolTip` 生命周期。
6. `IdeGlassPaneImpl.dispatch` 对选区上的点击不分发 `processMouseEvent`，导致复选框必须用 mouse listener 处理（来源：`platform/vcs-impl/shared/src/com/intellij/openapi/vcs/changes/ui/ChangesTree.java:264`）。
7. `SpeedSearchBase.setupListeners` 中的 AWT 输入法（IME）事件处理、`WriteIntentReadAction`、EDT 断言（来源：`platform/platform-impl/src/com/intellij/ui/SpeedSearchBase.java:594`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:266`）。
8. `TreeState` 的 JDOM 序列化格式（可参考思路，不必复刻格式）（来源：`platform/platform-api/src/com/intellij/ide/util/treeView/TreeState.java:56`）。

### 9.3 需推断 / 需复核实

1. **行高**：IntelliJ New UI 为 24px，Augit 现为 27px（commit）/26px（history）——是否有意偏离，需产品确认。
2. **树行悬停高亮**：Project View 与 Changes 树并未安装 `TreeHoverListener.DEFAULT`，因此严格复原时树行**没有** hover 背景。Augit 若已有 hover 背景，属于增强。
3. **点击复选框是否改变选中态**：IntelliJ 会改变（先选中该行）；产品规格只约束「失焦」场景，未约束点击行为。
4. **`isDark(background)` 抑制选中箭头图标**在 Augit 暗色选中背景 `#43454A` 下的实际结果，需按同一亮度算法验证。
5. **图标与文字的间距**：IntelliJ 由渲染器决定，无单点权威值；需按 mockup 现状确定。
6. **Project View 自动展开单子目录**：规则明确（目录名不以 `.` 开头即允许自动展开），是否在 Augit 复刻属产品决策。
7. **Swing `TooltipManager` 默认延迟值**来自 JDK 而非本仓库，未在本仓库中核实；如需精确复原应实测 PyCharm。
8. **复选框图标的最终显示尺寸**：源 SVG 为 24×24 viewBox，实际屏幕像素取决于 LAF 缩放，本仓库未见显式缩放常量，需实测。

---

## 附二、对现实现的核对结论（第二十六轮，结论均为「无需改动」）

本轮把本册几条【可直接实现】的规则拿去对现有实现，结论是**都成立**，因此**没有产生代码改动**。记录证据与推断链，供后续轮次直接引用、不必重复核对：

| 规则 | 核对结果 | 依据 |
| --- | --- | --- |
| §2.4「导航动作在 lead 为空时选**第一行**」 | **在 Augit 的 DOM 模型里不可达**，无需实现 | 树容器 `.side-content.tree` **没有 `tabindex`**（有 `role="tree"` 但不可聚焦），能聚焦的是行本身（`<a href>`）；`live-data.js` 的方向键处理以 `event.target.closest(".side-content.tree .tree-row")` 为前提，因此不存在「树有焦点但没有任何行是 lead」的状态 |
| 参考实现的树是否画**缩进导线** | **不画**，Augit 一致，无需加 | `ui.Tree.hash` 在参考家族里走 `tree-indent-guide-border` → `tool-window-border`，但把参考截图（`pycharm-history-normal.png`）的项目树放大后，逐级缩进的行之间**没有任何竖线** |
| 树行前缀的 `M↓` 是不是状态徽标 | **不是**，是 **Markdown 文件类型图标**（Markdown 标志本身就是 M + 下箭头） | 参考图里所有 `.md` 行都带 `M↓`、`.bmp` 行是另一套橙棕色图标；`design-system.md` 的「图标 → 状态标记 → 文件名」顺序未被违反 |
| §1.3 `forceFocusedSelectionForeground` | New UI 为 `false` → **选中行沿用元素自身颜色**，只有背景随焦点变；Augit 的「文件名按状态着色、不随焦点改写」**正是该行为** | 本册原文：expUI 里该键为 `false`（`expUI_light.theme.json:799`），旧主题 `intellijlaf` 才是 `true` |
| 参考图里选中行文件名呈**红色** | 不是规则差异，是该文件自身的状态色（状态色含**冲突红**） | 同图其余修改文件均为蓝色（`修改`），与 Augit 的 `.file-status-*` 令牌一致 |
| §1.1「展开/收起箭头只在选中且有焦点时用 selected 版图标」 | 该条**不适用于** `expui/general/tree*.svg`：那三枚是**「树形视图」图画**（两个小方框 + 一段 L 形连线），不是行内折线 | `general/tree.svg` 与 `general/treeSelected.svg` 唯一差别是描边色 `#6C707E` → **`#3574F0`**，即「选中态用强调蓝」；它是工具栏切换按钮的图形语义，与行内折线（`--augit-tree-arrow`）无关 |

## 附：本次未涉及但可能相关的点

- 文件颜色（File Colors）导致的树行背景：`ProjectViewTree.isFileColorsEnabledFor` 与 `ColoredItem.getColor()`（来源：`platform/lang-impl/src/com/intellij/ide/projectView/impl/ProjectViewTree.java:160`、`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:136`）。
- 拖放落点高亮的颜色键：`DragAndDrop.ROW_BACKGROUND` / `DragAndDrop.BORDER_COLOR`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:454`）。
- 分隔符行（`SeparatorWithText`）的渲染与坐标计算在 New UI 下改用 `Popup.separatorInsets`（来源：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java:420`）。
- 可访问性：`promiseSelectFirst` 等滚动动作会向无障碍上下文发送可见数据变化通知（来源：`platform/platform-api/src/com/intellij/util/ui/tree/TreeUtil.java:1897`）。

## 附三、第二十九轮：树行高按权威改到 24px（本轮唯一实现改动）

本册 §0.2 一开始就登记了 **行高 24px**（`JBUI.java:2515` 的 `scale(24)`／New UI 主题 `Tree.rowHeight = 24`），但实现里是 28px——**收集没错、落地错了**。按用户新裁决（所有冲突一律以 New UI 为准）本轮修正：

| 项 | 原值 | 现值 | 依据 |
| --- | --- | --- | --- |
| 文件树行高 | `ceil(max(27, h+8)/2)*2`（默认 28） | **`max(24, h + 8)`（默认 24）** | 权威 `Tree.rowHeight = 24`；2026 参考图选中行色带与行距实测均为 **42 物理px**，按标签卡片 28 逻辑px 定标（≈1.79）⇒ **23.5** |

同时去掉了原先"向上取偶数物理像素"的做法：官方按整数缩放（`JBUIScale.scale(24)`），不取偶；该做法只影响大字号（32px 字号下 50 → 49）。

**顺带核对（结论：不用改）——树的缩进步长**：`Tree.leftChildIndent` / `rightChildIndent` 这两个键只在旧 LAF `intellijlaf.theme.json`（7 / 11）里出现，New UI 主题**不定义**它们；New UI 的实际默认在 Jewel 桥接里是 `retrieveUnscaledIntAsNonNegativeDpOrUnspecified("Tree.leftChildIndent").takeOrElse { 7.dp }`。**7 + 11 = 18** 正好是每级缩进步长，而 2026 参考图里三个层级的最左墨迹 x 为 145 / 178 / 211 物理px（**步长 33 物理px ≈ 18.4 逻辑px**），与 Augit 的 `.tree-row.depth-N { padding-left: 18·N + 2 }` 一致。因此缩进**不动**。

**验收套件里有两处与行高耦合的期望，必须同步**（否则出现"实现对了、套件红了"的假失败）：

1. §154 的公式校验里 `tree-height` 期望式改为 `max(24, h + 8)`；
2. §154「保持树的第一个可见节点」**写死了行名**（`f154Base.firstVisible === 'docs/bulk-006.txt'`）。行高变小后，同一 `treeScrollTop` 下首个可见行变成 `docs/bulk-007.txt`（前后仍相同，"保持"这条语义本身成立），写死值随之同步。这类"把实现细节写进断言"的判据在行高再变时会再次失效，已在此记明。
