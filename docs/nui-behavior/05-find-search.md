# 05 查找与搜索：交互与页面逻辑（PyCharm 2026.2.1 New UI）

## 本节要点

1. 编辑器内查找条是编辑器面板的 NORTH 组件（整行、正文整体下移），不是浮层；输入框在左、状态计数与按钮在右。
2. 输入即搜，不等防抖节流之外的显式确认：每次文档变更触发一次查询，查询本体在 30 ms 活动延时后被合并执行；大文件按 50 ms / 最多 10000 条分块产出，结果边搜边显示。
3. 当前匹配与其余匹配是两套独立绘制：其余匹配用 `TEXT_SEARCH_RESULT_ATTRIBUTES` 背景，当前匹配只加"圆角描边"（caret 色），不改文档文本。
4. 导航是两段式的：越过末尾只弹出提示（"…not found, press F3 to search from the top"），再按一次才回绕到首/末项。
5. 计数文字是 `当前项/总数`（如 `3/17`）；搜索进行中先显示 `N results`，搜索结束后才出现 `当前项`。

> 范围：本文只覆盖 Augit 会用到的三类搜索——只读正文的"当前文件查找"、文件名/全局快速搜索、全仓文本搜索。替换（Replace / Replace All / Preserve Case）相关规则一律跳过。
>
> 出处路径均相对 `/mnt/d/github/intellij-community`。该克隆为 Bazel 目录布局（如 `platform/lang-impl/src/...`），等价于上游 `platform/lang-impl/src/com/intellij/find/...`。

标注约定：**[可直接实现]** = HTML/CSS/C# 能直接表达；**[Swing 特有]** = 依赖 Swing 组件模型，不可照搬，只能取语义；**[需推断]** = 源码中没有直接对应，需按 WebView2/DOM 能力推导。

---

## 1. 查找条的几何与布局

### 1.1 容器与整体位置

- 查找条挂在编辑器面板的 `BorderLayout.NORTH`，因此它**占据编辑器整行宽度**，并且**把正文区域整体下推**（不是覆盖在文本上）。**[可直接实现]**（来源：platform/platform-impl/src/com/intellij/openapi/editor/impl/EditorImpl.java:1616）
- 打开/关闭查找条走 `editor.setHeaderComponent(header)`：关闭时传 `null`，会清空 header 面板并 revalidate/repaint；若编辑器本身有"常驻 header"，则常驻部分在上、查找条在下（垂直堆叠）。**[需推断]**（来源：EditorImpl.java:2693-2713）
- 编辑器内查找条的组件是 `SearchReplaceComponent`（`EditorHeaderComponent` 子类）。**[Swing 特有]**（来源：platform/lang-impl/src/com/intellij/find/SearchReplaceComponent.java:116）

### 1.2 左右两栏与间距

- 组件是一根水平 Splitter：左侧是输入区（`leftPanel`），右侧是工具条区（`rightPanel`）。左侧初始占比默认 `0.33F`；在"最大化左栏"模式下为 `0.9F`，且被限制为 `MAX_LEFT_PANEL_PROP = 0.9F`。**[可直接实现]**（来源：SearchReplaceComponent.java:118-119, 331, 353-359）
- 右栏与左栏之间的左侧补偿间距常量 `RIGHT_PANEL_WEST_OFFSET = 13`（px，随 UI 缩放）。非最大化模式下右栏左边距为 6 px。**[可直接实现]**（来源：SearchReplaceComponent.java:117, 366, 387）
- 分隔线本身宽 1 px（`OnePixelSplitter`）。**[可直接实现]**（来源：SearchReplaceComponent.java:354）

### 1.3 输入框尺寸

- 输入框是"文本域 + 外层壳"结构：内层 `JBTextArea`，行数 `rows = multiline ? 2 : 1`（单行查找恒为 1），列数 `columns = 12`，最小宽度 `150 px`。**[可直接实现]**（来源：SearchReplaceComponent.java:736-738）
- 打字时输入框会随内容**横向扩张**（注册表开关 `ide.find.expand.search.field.on.typing`，默认 `true`）：可扩张到的上限 = 左栏可用宽度 − 右侧图标区预留 `180 px`，且不小于输入框最小宽度。**[可直接实现]**（来源：SearchReplaceComponent.java:721-732）
- 多行输入时文本域行数上限：`min(ide.find.max.rows 注册表值, 实际行数)`，并被夹在 `1..25`。单行模式下粘贴的换行会被替换成空格。**[需推断]**（单行查找下不适用；来源：SearchTextArea.java:129-137, 147-148）

### 1.4 输入框内边距与描边

- 非 New UI 时宽度按主题键 `Editor.SearchField.borderInsets` 取；**New UI 默认 `insets(7, 10, 7, 8)`**（上/左/下/右）。**[可直接实现]**（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:774-784）
- 工具条区上边距：New UI 为 `insetsTop(3)`（键 `Editor.SearchToolbar.borderInsets`）。**[可直接实现]**（来源：JBUI.java:786-796）
- 输入框外壳四周还会内缩 `SEARCH_FIELD_EMPTY_INSETS = insets(3)` 用于画焦点描边，即描边环画在距组件边界 3 px 处。**[可直接实现]**（来源：SearchReplaceComponent.java:120, 819-843）
- 输入框外壳圆角 = `Component.arc`（默认 5），焦点描边宽度 = `Component.focusWidth`（默认 2）。**[可直接实现]**（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/darcula/DarculaUIUtil.java:299, 302）
- 输入区背景色取命名色 `Editor.SearchField.background`（回退到主题背景色）；New UI 下 `SearchReplaceComponent`、外壳、内层文本域三者背景都设为该色。**[可直接实现]**（来源：SearchTextArea.java:81, SearchReplaceComponent.java:408-410, 781-788）
- 文本域自身边框上下内距：Mac 下 3/2；其他平台下 `top = (字体行高 ≤ 16 ? 2 : 1)`、`bottom = (含换行 ? 2 : 深色主题 ? 1 : 0)`；HiDPI 用户缩放时 `top=2, bottom=0`。左右内距恒为 0。**[需推断]**（这是 Swing 字体度量推导；Web 端应按实际字体行高取整；来源：SearchTextArea.java:170-194）

### 1.5 状态计数文字

- 计数是一个 `JLabel`，**水平居中**显示。**[可直接实现]**（来源：platform/lang-impl/src/com/intellij/find/editorHeaderActions/StatusTextAction.java:45-55）
- 为了避免计数变化导致按钮左右抖动，标签的**首选宽度按样本文本 `"9888 results"` 一次性算定**，高度取 `max(文本高度, DEFAULT_MINIMUM_BUTTON_SIZE.height)`。**[可直接实现]**（来源：StatusTextAction.java:48-52, 57-59）
- 常规色：New UI 用 `Label.infoForeground`；出错（无匹配 / 非法正则）时用错误前景色。**[可直接实现]**（来源：StatusTextAction.java:28-31, SearchReplaceComponent.java:505-513）

### 1.6 按钮尺寸与间距

- 工具条按钮统一最小尺寸 `DEFAULT_MINIMUM_BUTTON_SIZE = 22 × 22`（随 UI 缩放）。**[可直接实现]**（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/ActionToolbar.java:79）
- 输入框内嵌的额外动作（三个选项开关等）用 `GridLayout(1, n, scale(4), 0)` 排列：**按钮之间水平间距 4 px**，整组右边距 2 px。**[可直接实现]**（来源：SearchTextArea.java:311, 320）
- 图标组与输入框之间的间距：外层 `BorderLayout` 水平 gap 为 `scale(3)`（3 px）。**[可直接实现]**（来源：SearchTextArea.java:247）
- 图标组右侧预留：New UI 为 8 px，旧 UI 为 5 px。**[可直接实现]**（来源：SearchTextArea.java:241）
- 图标组的上下边框：非岛屿模式为 `emptyTop(2)`。**[可直接实现]**（来源：SearchTextArea.java:256-258）
- 历史按钮容器边框：New UI 为 `empty(2, 3, 0, 8)`，旧 UI 为 `empty(2, 3, 0, 0)`。**[可直接实现]**（来源：SearchTextArea.java:260-268）
- 文本域滚动视口边框固定为 `empty(2, 0, 2, 2)`（滚动视口四角不作额外留白）。**[可直接实现]**（来源：SearchTextArea.java:86, 153-169）

### 1.7 按钮顺序

- New UI 下编辑器查找条的动作顺序（从左到右）是：**状态计数 → 上一项 → 下一项 → 过滤/范围组 → "更多"弹出组**。**[可直接实现]**（来源：EditorSearchSession.java:238-265）
- 三个选项开关与"Click to highlight"链接被声明为 `Embeddable`，因此从工具条里**移出**并塞进输入框内部右侧，视觉顺序为：**[输入框][区分大小写][全字匹配][正则][点击高亮链接]**。**[Swing 特有/可直接实现]**（来源：SearchReplaceComponent.java:232-251, EditorSearchSession.java:140-143）
- 关闭按钮在 New UI 下是工具条末端的右对齐按钮（`RightAlignedToolbarAction`），不是独立标签。**[可直接实现]**（来源：SearchReplaceComponent.java:297-315, 850-856）
- 结论映射到 Augit：`[输入框][大小写][全字][正则][状态 x/y][上一项][下一项][关闭]` 的顺序与 IntelliJ 一致。**[可直接实现]**

### 1.8 聚焦顺序

- 显式设置了遍历顺序：**查找输入框 → 替换输入框 → 输入框内多余按钮 → 工具条按钮 → 模式按钮**；`Tab`/`Shift+Tab` 在查找组件内部循环（先在组件上注册了抑制编辑器 Tab 动作的转移动作）。**[需推断]**（Augit 只需保留"Tab 在查找条内循环、不逃到正文"；来源：SearchReplaceComponent.java:396-399, 651-666）

---

## 2. 输入与自动定位

### 2.1 触发时机

- 输入框的 `DocumentListener` 在文本变化后通过 `invokeLater` 派发 `searchFieldDocumentChanged()`——**每次按键（每次文档变更）都会触发一次查询请求**，不做"按键级"合并。**[可直接实现]**（来源：SearchReplaceComponent.java:563-568）
- 处理体中先重置匹配上限为 `MATCHES_LIMIT`，再把输入文本写入 `FindModel`；`FindModel` 观察者随即清空旧结果并调用 `updateResults(...)`。**[可直接实现]**（来源：EditorSearchSession.java:437-444, 175-210）

### 2.2 防抖延时

- 真正的去抖在 `LivePreviewController`：每次请求先 `cancelAllRequests()`，再以 **`USER_ACTIVITY_TRIGGERING_DELAY = 30` ms** 延迟把查询投到 pooled 线程执行。即"最后一次输入后 30 ms 才真正搜索"的合并式防抖。**[可直接实现]**（来源：platform/lang-impl/src/com/intellij/find/impl/livePreview/LivePreviewController.java:41, 145-172）
- 查找条刚打开时先以 `setUserActivityDelay(0)` + `updateResults(false)` 立即搜一次，然后恢复为 30 ms。**[可直接实现]**（来源：EditorSearchSession.java:615-625）
- 全仓搜索（Find in Files）的防抖是 **100 ms**（见 §9.2）；Search Everywhere 是 **100 ms**，且查询为空时延时为 0。**[可直接实现]**（来源：FindPopupPanel.java:1290-1300；SearchEverywhereUI.java:910-929）

### 2.3 首个匹配的定位规则

- 搜索结束后由 `updateCursor(oldCursorRange, next)` 决定当前项，优先级为：
  1. 若本次是"连续输入中的一次重搜"且**上一轮的当前匹配仍然存在**（与新结果相交）→ 保留它（黏住当前项，不跳动）；
  2. 否则若此前没有当前项（首次搜索）→ 取**光标处或光标之后的第一个匹配**（先看光标所在匹配，再看光标所在选区内首个匹配，再看光标之后首个匹配）；
  3. 否则 → 取**光标之后的第一个匹配**；
  4. 若仍为空且有匹配 → 回绕取第一个匹配。
  **[可直接实现]**（来源：SearchResults.java:806-830, 856-872, 924-940）
- 也就是说：**自动定位不是无条件"从头开始"，而是"从当前光标位置向后"**，并且输入过程中尽量保持当前项稳定。[可直接实现]（来源：SearchResults.java:856-872）

### 2.4 打字时是否滚动正文

- 由设置项 `SCROLL_TO_RESULTS_DURING_TYPING`（`FindSettings`）决定，**默认 `true`**：搜索找到当前项时就把它滚入可见区（通常居中）。**[可直接实现]**（来源：platform/analysis-impl/src/com/intellij/find/impl/FindSettingsBase.java:67, 327-334；EditorSearchSession.java:202）

### 2.5 输入法组词（composition）

- **IntelliJ 的查找包内没有任何 `composition` / `InputMethodRequests` 相关分支**（`SearchTextArea`、`EditorSearchSession`、`SearchResults`、`LivePreview` 均无），组词由 Swing 文本组件与 AWT 输入法层原生处理。**[Swing 特有]**（依据：对 `platform/lang-impl/src/com/intellij/find` 全目录检索 `composition|inputmethod|isComposing` 无结果）
- 因此 Augit 现有的"组词期间不触发查询、`compositionstart` 让在途查询失效、`compositionend` 后按需重搜"规则**在 IntelliJ 中找不到直接对应**，属于 WebView2/DOM 输入模型下的必要补充。**[需推断]**（对照：docs/ux-mockups/current-find.js:228-247；第 233 轮行号重取）
- 可对应的事实是"**旧查询结果必须整体失效、绝不能落到界面上**"这一条：IntelliJ 用 stamp 丢弃（见 §8.3），Augit 用 generation 丢弃，语义等价。**[可直接实现]**（来源：SearchResults.java:404-435）

### 2.6 输入上限

- 查找文本长度上限由注册表 `editor.max.search.selection.length` 控制，默认 **10000** 字符；超限则**不更新**输入框内容（保持旧值）。**[可直接实现]**（来源：SearchReplaceComponent.java:627-629）
- 输入框支持输入历史补全（Ctrl+Space 触发，候选来自正文中的单词）与历史下拉；历史下拉快捷键为 `Alt+Down` / `Alt+Up`。**[Swing 特有]**（来源：VariantsCompletionAction.java:39-70；platform/platform-api/src/com/intellij/ui/SearchTextField.java:63-66）

---

## 3. 结果高亮

### 3.1 高亮不改变文本

- 所有匹配高亮都是**在编辑器的 markup model 上叠加 RangeHighlighter**（`EXACT_RANGE`，且 `setVisibleIfFolded(true)`），**不修改文档字符内容**。这直接支持 Augit "高亮不改变 textContent" 的约束：DOM 侧应使用 `<mark>` 一类的包裹元素或 `Range` + CSS 自定义高亮，而不重写文本节点。**[可直接实现]**（来源：platform/lang-impl/src/com/intellij/find/impl/livePreview/LivePreview.java:563-570）

### 3.2 两套属性

- **其余匹配**：取配色方案属性 `EditorColors.TEXT_SEARCH_RESULT_ATTRIBUTES`（有背景色）。**[可直接实现]**（来源：platform/lang-impl/src/com/intellij/find/impl/livePreview/EditorLivePreviewPresentation.kt:15-16；platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:62）
- **当前匹配**：不设前景/背景，只设 **caret 色 + `EffectType.ROUNDED_BOX`** 的效果色，即画一个**圆角矩形描边**（"光标框"），字体为 plain。**[可直接实现]**（来源：EditorLivePreviewPresentation.kt:27-29）
- **选区内的匹配**：额外叠一层 `JBColor.WHITE` 的 `ROUNDED_BOX` 描边（与当前匹配互斥：当前匹配不叠加选区描边）。**[可直接实现]**（来源：EditorLivePreviewPresentation.kt:31-32；LivePreview.java:479-497）
- **零宽匹配**（如正则匹配空串）：在默认属性上把 `effectType` 改为 `BOXED`、effectColor 取该背景色，让空结果的"点"可见。**[需推断]**（Web 侧需自行实现零宽命中的可视标记；来源：EditorLivePreviewPresentation.kt:18-22）
- 匹配与当前匹配使用同一高亮层（`HighlightManagerImpl.OCCURRENCE_LAYER`）。**[Swing 特有]**（来源：EditorLivePreviewPresentation.kt:34-35）

### 3.3 具体色值（New UI / expUI 方案）

- 深色：`TEXT_SEARCH_RESULT_ATTRIBUTES` 背景 `#114957`、effect 色 `#165e70`、error stripe `#72d6d6`。**[可直接实现]**（来源：platform/platform-resources/src/themes/expUI/expUI_darkScheme.xml:762-767）
- 浅色：前景 `#000000`、背景 `#fcd47e`、error stripe `#c47233`。**[可直接实现]**（来源：platform/platform-resources/src/themes/expUI/expUI_lightScheme.xml:455-460）
- New UI 的输入框错误前景色取命名色 `SearchField.errorForeground`（回退红色）。**[可直接实现]**（来源：SearchReplaceComponent.java:510-513）

### 3.4 只读正文上的实现约束

- IntelliJ 的高亮与正文文本是两套结构（文档 vs markup model），因此"查找高亮"和"正文内容"天然解耦；Augit 的只读正文同样应保持 **正文 DOM 文本节点只读**，高亮只做装饰层。**[可直接实现]**
- 高亮数量有硬上限：当匹配数达到 `matchesLimit` 时**完全不画任何高亮**（`isBelowMatchesLimit()` 为假时 `dropHighlighters()`），并给出"Click to highlight"链接让用户主动放开。**[可直接实现]**（来源：LivePreview.java:146-151, 337-350；EditorSearchSession.java:114-117, 427）

---

## 4. 导航

### 4.1 方向与按键

- 编辑器内查找单行模式下：
  - **Enter = 下一项**（`ACTION_EDITOR_MOVE_CARET_DOWN` 与 `CommonShortcuts.ENTER` 一起绑到 NextOccurrenceAction）；**[可直接实现]**（来源：platform/lang-impl/src/com/intellij/find/editorHeaderActions/NextOccurrenceAction.java:37-44）
  - **Shift+Enter = 上一项**（另加 `ACTION_EDITOR_MOVE_CARET_UP`）；**[可直接实现]**（来源：platform/lang-impl/src/com/intellij/find/editorHeaderActions/PrevOccurrenceAction.java:40-47）
  - 多行模式下不再追加 Enter 绑定，只保留上/下方向键语义。**[Swing 特有]**（来源：PrevNextOccurrenceAction.java:39-44）
- 全局快捷键 `FindNext` = `F3`（另有 `Ctrl+Shift+L` 等），`FindPrevious` = `Shift+F3`。**[可直接实现]**（来源：platform/platform-resources/src/keymaps/$default.xml:504-510, 707）
- 这些快捷键被注册在**查找输入框**上（`registerCustomShortcutSet(shortcut, shortcutHolder)`），所以按 Enter 导航时**焦点留在输入框**。**[可直接实现]**（来源：SearchReplaceComponent.java:886-907）
- 普通 Enter 会被输入框无条件消费（注册空动作），避免落到外层默认按钮上。**[Swing 特有]**（来源：SearchReplaceComponent.java:586-589）

### 4.2 导航前置条件

- 上一项/下一项按钮在 **`搜索会话存在 && 搜索未在进行 && 有匹配`** 时才可用。搜索进行中按钮禁用。**[可直接实现]**（来源：PrevNextOccurrenceAction.java:34-37；EditorSearchSession.java:478-481）
- 独立的 `OccurrenceAction.update` 还要求 `availableForSelection() || findModel.isGlobal()`。**[Swing 特有]**（来源：OccurrenceAction.java:26-37）

### 4.3 到达末尾：两段式

- 第一段（越界）：`nextOccurrence` 找不到下一项且此前未处于"未找到"状态时，调用 `setNotFoundState(true)`，最终走 `FindUtil.processNotFound(...)`：
  - 在**光标附近弹出轻量提示**（正向在下方 `HintManager.UNDER`，反向在上方 `HintManager.ABOVE`）；
  - 提示文案：`"{查询}" not found, press {快捷键} to search from the top`（正向）/ `... from the bottom`（反向）；若取不到快捷键文本则退化为 `... perform "Find Next" again to search from the top`；
  - 提示的隐藏策略为"任意按键 / 文本变化 / 滚动"时消失，**无自动超时**（timeout = 0）；
  - 同时在编辑器 user data 里记下方向（`Direction.DOWN` / `UP`），并注册 caret 监听，光标一动就清除该记录。
  **[可直接实现]**（来源：platform/lang-impl/src/com/intellij/find/FindUtil.java:806-875；SearchResults.java:1037-1046, 990-999）
- 第二段（回绕）：再次触发同方向导航时，`myNotFoundState` 已是 `true` → `processFromTheBeginning = true` → 正向回绕到**第一个匹配**、反向回绕到**最后一个匹配**。**不会**从第一段就直接回绕。**[可直接实现]**（来源：SearchResults.java:976-1006, 1026-1046）
- 仅在"确实还有匹配但当前方向已到端点"时才加这句提示；`processNotFound` 会先反向到文件头/尾探测一次，找不到匹配则保持原文案（仅 `"{查询}" not found`）。**[可直接实现]**（来源：FindUtil.java:810-851）
- 文案来源：`find.search.string.not.found.message`、`find.search.again.from.top.hotkey.message`、`find.search.again.from.top.action.message`、`find.search.again.from.bottom.*`。**[可直接实现]**（来源：platform/analysis-impl/resources/messages/FindBundle.properties:3-7）

### 4.4 导航对选择与光标的影响

- 导航会把当前匹配同时落实为**编辑器选区**：把 caret 移到匹配末尾，并 `selection.setSelection(start, end)`；随后按设置可选地 `scrollToCaret(CENTER)`。**[可直接实现]**（来源：platform/lang-impl/src/com/intellij/find/impl/livePreview/SelectionManager.java:46-76；SearchResults.java:1057-1062）
- 导航会把当前项压入一个"光标位置栈"，用于在重搜后尽量恢复同一处。**[Swing 特有]**（来源：SearchResults.java:1008-1010, 832-845）

### 4.5 计数何时更新

- 每次 `searchResultsUpdated` 与每次 `notifyCursorMoved` 都会刷新状态文字与工具条可用态。**[可直接实现]**（来源：SearchResults.java:1064-1069；EditorSearchSession.java:397-435）
- 分块搜索期间，第一块一到就更新一次（此时**没有当前项**），因此显示形如 `N results`；搜索全部结束后 `updateCursor` 确定当前项，再刷新为 `i/N`。**[可直接实现]**（来源：SearchResults.java:733-744, 748-762；EditorSearchSession.java:412-417）

---

## 5. 选项开关（区分大小写 / 全字匹配 / 正则）

### 5.1 呈现

- 三个开关都是 `CheckboxAction` 派生的工具栏动作，**在工具条里显示文字**，但实际使用时被 `Embeddable` 化塞进输入框内部（只显示图标）。**[Swing 特有]**（来源：platform/lang-impl/src/com/intellij/find/editorHeaderActions/EditorHeaderToggleAction.java:33-36；SearchReplaceComponent.java:232-251）
- 每个开关有 **三个图标状态：normal / hovered / selected**，分别由 `setIcon` / `setHoveredIcon` / `setSelectedIcon` 提供；选中态图标优先于普通图标绘制。**[可直接实现]**（来源：EditorHeaderToggleAction.java:26-31；SearchTextArea.java:467-474）
- 图标为 **16×16**；New UI（expUI）下三态复用同一源图，靠颜色/底衬托出状态。**[可直接实现]**（来源：platform/util/ui/src/com/intellij/icons/AllIcons.java:108-110, 179-181, 236-238）
- 图标映射：区分大小写 = `AllIcons.Actions.MatchCase*`；全字匹配 = `AllIcons.Actions.Words*`；正则 = `AllIcons.Actions.Regex*`。**[可直接实现]**（来源：ToggleMatchCase.java:12-17；ToggleWholeWordsOnlyAction.java:12-17；ToggleRegex.java:18-23）
- 悬停说明（tooltip）文本就是动作文字：`Match &Case` / `&Words` / `Rege&x`，即"Match Case" / "Words" / "Regex"（助记符下划线在显示时去掉）。**[可直接实现]**（来源：FindBundle.properties:121, 124-126；EditorSearchSession.java:532-534）
- 正则开关的 tooltip 额外带一条链接 `Show expressions help`，点击弹出正则帮助。**[需推断]**（Augit 可省或改为外链；来源：ToggleRegex.java:25-28；FindBundle.properties:128）
- 开关在辅助功能角色上是 CHECK_BOX（内嵌样式的按钮）。**[可直接实现]**（来源：SearchTextArea.java:477-492）

### 5.2 切换后的行为

- 每个开关的 `setSelected` 改 `FindModel` 的对应位，并**同时写入 `FindSettings` 的本地偏好**（`localCaseSensitive` / `localWholeWordsOnly` / `localRegularExpressions`），使该偏好跨会话记忆。**[可直接实现]**（来源：ToggleMatchCase.java:24-28；ToggleWholeWordsOnlyAction.java:24-28；ToggleRegex.java:35-40）
- `FindModel` 变更会触发 §2.1 的观察者链路 → **立即重新查询**（经 30 ms 合并）。**[可直接实现]**（来源：EditorSearchSession.java:175-210）
- 全仓搜索里，切换开关会显式调用 `scheduleResultsUpdate(true)`（立即重排 100 ms 后的查询）。**[可直接实现]**（来源：FindPopupPanel.java:2053-2061）

### 5.3 全字匹配的判定语义

- IntelliJ 的"全字"不是简单的 `\b`：它用 `Character.isJavaIdentifierPart`（标识符字符：字母/数字/`_`/`$` 等）判定，并有一条特殊规则——**当相邻字符与被匹配的首/尾字符相同时，视为词边界**。**[需推断]**
- 具体规则（正向后视）：起始边界 = 前一字符不是标识符（若首字符本身是标识符），否则前一字符 ≠ 首字符；结束边界 = 末字符是标识符时要求后一字符不是标识符，否则要求后一字符 ≠ 末字符；文件首/尾天然成立。**[可直接实现]**
  （来源：platform/lang-impl/src/com/intellij/find/impl/FindManagerBase.java:247-276）
- 注意 Augit 现有实现用的是 `[\p{L}\p{N}_]`（见 docs/ux-mockups/current-find.js:103-108），**与 IntelliJ 的 `isJavaIdentifierPart` 不等价**（例如 `$`、以及"相邻同字符"规则）。这是刻意差异，需在实现说明里保留标注。**[需复核实]**

### 5.4 正则

- 正则合法性在**每次查询前**用 `Pattern.compile(text)` 预校验；抛 `PatternSyntaxException` 时：**输入框文字变红**、停用"点击高亮"链接、清空结果、状态文字置为 `Bad pattern`，并且**不发起搜索**。**[可直接实现]**（来源：EditorSearchSession.java:645-673；SearchSession.java:11；FindBundle.properties:79）
- 若正则为纯 `|` 串（匹配空串的退化模式），状态显示 `Vague pattern` 并等同"无可搜内容"处理。**[需推断]**（来源：EditorSearchSession.java:663-667；ApplicationBundle.properties:656）
- 全仓搜索里非法正则不是"红色文字"，而是 `ComponentValidator` 的错误态（红色描边 + 错误气泡），文案 `find.invalid.regular.expression.error`（带查询串与语法描述）；匹配空串的正则也有单独报错。**[可直接实现]**（来源：FindPopupPanel.java:1520-1532）
- 正则执行有**灾难性回溯保护**：匹配时使用 `BombedCharSequence` 包裹正文，每 ~1024 次 `charAt`/`length` 调用检查一次取消，从而保证正则不会永久卡死线程。**[可直接实现]**（来源：platform/core-api/src/com/intellij/patterns/StringPattern.java:129-137；platform/util/src/com/intellij/openapi/util/text/StringUtil.java:3107-3135）
- 正则大小写：全仓搜索里 `CASE_INSENSITIVE` 会作为 flag 传给 `Pattern.compile`（`MULTILINE` 恒开）。**[可直接实现]**（来源：FindPopupPanel.java:1524）

---

## 6. 无结果与错误

### 6.1 无匹配

- 无匹配时：**输入框文字变红**（`SearchField.errorForeground`）、**状态文字转为错误色**，状态文本按匹配数生成。**[可直接实现]**（来源：SearchReplaceComponent.java:505-513；EditorSearchSession.java:407-425）
- 状态文本的三条分支：
  - 搜索中且尚未有匹配 → **不进入错误态**（因为"还没有匹配"不等于"没有匹配"）；
  - 全局搜索下匹配数为 0 且编辑器原本没有选区 → `No selection`；
  - 匹配数 0 → `0 results`。
  **[可直接实现]**（来源：EditorSearchSession.java:404-425；ApplicationBundle.properties:652, 655）
- 匹配数超过上限时状态文本变为 `10000+`（`{0}+`），并显示"Click to highlight"链接。**[可直接实现]**（来源：ApplicationBundle.properties:654；EditorSearchSession.java:405-427；FindBundle.properties:147）

### 6.2 输入框为空时的提示

- 输入框文字为空时才显示空文本（`Search`）；且**只在输入框获得焦点时显示**。**[可直接实现]**（来源：SearchReplaceComponent.java:520-522, 763-765；ApplicationBundle.properties:661）
- 空输入框且已勾选某些选项时，空文本会变成"已启用的选项"提示：一个选项 → 选项名；两个 → "A and B"；全部为空 → `Search`。**[可直接实现]**（来源：EditorSearchSession.java:536-550；FindBundle.properties:68-69）
- 存在多行选区时，空文本改为 `Search in selection. {快捷键} to search for selected text`。**[可直接实现]**（来源：EditorSearchSession.java:552-562；ApplicationBundle.properties:658）

### 6.3 无匹配时是否禁用导航

- 会：上一项/下一项要求 `hasMatches()`；无匹配时禁用（灰色）。
- "打开全部匹配/多光标"等更多动作也要求 `hasMatches`。**[可直接实现]**（来源：PrevNextOccurrenceAction.java:34-37；OccurrenceAction.java:34-36）

### 6.4 全仓搜索的无结果提示

- 结果列表为空时按状态给出**多行空态文本**：
  - 查询与文件掩码都为空 → `Type search query to find in files`；
  - 否则 → `Nothing found` 起头；
  - 若因目录范围未递归 → 追加 "Search recursively in subdirectories" 链接；
  - 若启用了搜索选项 → 追加 `Search option used: {选项}`（多选项时列出全部并给 `Clear all options` 链接）；
  - 若查询里含正则元字符且语法合法 → 追加 `Search with Regex` 链接（点击即打开正则开关）。**[可直接实现]**（来源：FindPopupPanel.java:1358-1453；FindBundle.properties:62-75）
- 注意"正则开关"在 Augit 里点击切换后应清空"建议正则"提示，避免重复提示。**[可直接实现]**（来源：FindPopupPanel.java:2057-2059）

---

## 7. 关闭与恢复

### 7.1 Esc 关闭与焦点

- `Esc` 绑定在输入框外壳上（快捷键集取 `ACTION_EDITOR_ESCAPE`），触发 `close()`。**[可直接实现]**（来源：SearchReplaceComponent.java:850-865）
- 关闭时：**焦点显式回到编辑器正文组件**（`requestFocus(editor.getContentComponent(), false)`），然后销毁 live preview 并 `setHeaderComponent(null)`。**[可直接实现]**（来源：EditorSearchSession.java:607-613）
- 关闭会随 live preview 一起销毁结果集（清除全部匹配高亮与"当前项描边"），但**编辑器里由导航产生的选区保持不变**。**[可直接实现]**（来源：EditorSearchSession.java:641-643；LivePreviewController.java:320-327；LivePreview.java:319-335）
- 滚动位置：关闭本身不做滚动；若此前因"空查询"触发了 `restoreInitialCaretPositionAndSelection()`，则会回到会话开始时的选区/光标并用 `ScrollType.RELATIVE` 滚动。**[可直接实现]**（来源：EditorSearchSession.java:675-699）

### 7.2 空输入与"清空"按钮

- 清空输入框（点 X）时会把 `JUST_CLEARED` 客户端标记置位；随后 `nothingToSearchFor(...)` **不会**恢复会话初始选区（避免"清空后光标乱跳"）。**[Swing 特有]**（来源：SearchTextArea.java:423-434；EditorSearchSession.java:675-682）
- 清空按钮只在输入非空时出现。**[可直接实现]**（来源：SearchTextArea.java:270-290, 275）

### 7.3 关闭时提交历史

- 组件从界面移除时（`removeNotify`）会把当前查找文本与替换文本写入历史；`Ctrl+Enter`（Mac 为 `Cmd+Enter`）在非空时也写入历史并**把焦点交还正文**，为空时直接关闭。**[可直接实现]**（来源：SearchReplaceComponent.java:463-471, 570-583, 680-698）

### 7.4 重新打开：是否预填上次查询

- **会预填**。新建会话时 `createDefaultFindModel` 先 `copyFrom(FindManager.getFindInFileModel())`——这是项目级持久模型，含上次的查询串与选项；只有当编辑器当前有选区时才用选区文本覆盖查询串。**[可直接实现]**（来源：EditorSearchSession.java:375-386；FindManagerBase.java:70-73, 53-58）
- 三个选项的默认值来自 `FindSettings` 的"本地"偏好（上一节 §5.2），同样跨会话记忆。**[可直接实现]**（来源：FindManagerBase.java:56-58；FindSettingsBase.java:146-163）
- 另有 `RestorePreviousSettingsAction`：仅当**输入框为空**且存在"上一次查找模型"时可用，快捷键 `Enter`，作用是把上一次的查询与选项整体恢复。**[Swing 特有]**（来源：editorHeaderActions/RestorePreviousSettingsAction.java:26-52）
- 从"查找"切到"替换"再切回时，若查询串恰好等于会话开始时的选区文本，会被清空；否则恢复会话初始光标位置与选区。**[需推断]**（来源：EditorSearchSession.java:185-199）

---

## 8. 性能相关

### 8.1 慢查询的加载态

- 编辑器查找条**没有独立的 spinner**；进行中的唯一可见信号是"上一项/下一项被禁用"（`isSearchInProgress()`）+ 状态文字保持上一轮的形态（分块结果会持续刷新计数）。**[可直接实现]**（来源：EditorSearchSession.java:478-481；PrevNextOccurrenceAction.java:34-37）
- 全仓搜索有明确的加载态：头部 16×16 加载图标 + `{n}+ matches in {m}+ files` 的"未完成"文案（计数带 `+`）。**[可直接实现]**（来源：FindPopupPanel.java:1345-1356；FindPopupHeader.kt:68-70）
- Search Everywhere 在查询期间把结果列表空文本设为 `Searching…`。**[可直接实现]**（来源：SearchEverywhereUI.java:936）
- Augit 现有视觉稿的"正在搜索…"（150 ms 后显示）在 IntelliJ 中没有精确对应值，属于 Web 端补充。**[需推断]**（对照：docs/ux-mockups/current-find.js:173；第 233 轮行号重取）
- **可观测性（Augit 侧，第 233 轮）**：实时查找的在途窗口靠 `window.__augitFindResultDelay` 注入（生产为空 ⇒ 生成的 Worker 源码与原实现**逐字相同**）。注入点必须放在 **"Worker 报告就绪之前"**（`setTimeout(()=>postMessage({ready:true}),N)`），不能放在"结果到达之后" —— 后者那一刻 `stop()` 已经抬过 `generation`，延迟后再比对取消前的代次会恒不相等，结果永远落不了地，页面侧的 150 ms 加载阈值与 `busy` 期间的方向队列也就都观测不到（第 127 轮的 Worker 路径正是这样失效的，第 233 轮订正）。**[可直接实现]**

### 8.2 分块与让出

- 搜索在后台（pooled 线程）跑，按块产出：
  - 单块**时间预算 50 ms**（`CHUNK_TIME_BUDGET_MS`）；
  - 单块**最多 `MATCHES_LIMIT = 10000` 条**（因为把每条匹配变成 RangeHighlighter 是 EDT 上的主要开销）；
  - 只在"非 EDT 且未持读锁"时才真正分块（`ide.find.incremental.results` 注册表开关）；否则退化为一次搜完。**[可直接实现]**（来源：SearchResults.java:87, 337-361, 363-372；LivePreviewController.java:42）
- 每块算出后立刻 post 到 EDT 应用并通知监听者；**第一块按"全量更新"通知**（让监听者丢弃上一轮），其后各块按"追加"通知；**最后一块不追加通知**，由收尾的全量刷新统一发布（省一次全量遍历）。**[可直接实现]**（来源：SearchResults.java:404-435, 717-744）
- 匹配上限 10000 之外不画任何高亮（见 §3.4）。**[可直接实现]**（来源：LivePreview.java:348-350）

### 8.3 取消与旧结果丢弃

- **stamp 机制**：每次发起搜索先 `getStamp()`（单调自增）；应用分块时若 `chunkStamp < lastUpdatedStamp` 直接丢弃；应用成功后把 `lastUpdatedStamp` 提升到本次 stamp。**晚到的旧查询结果因此被静默丢弃**。**[可直接实现]**（来源：SearchResults.java:59-61, 121, 404-435）
- **新查询打断旧查询**：`updateInBackground` 先 `cancelAllRequests()` 再排新请求。**[可直接实现]**（来源：LivePreviewController.java:155-172）
- **文档变化 / 写动作抢占**：分块之间若文档 modificationStamp 变化，或某块的读动作被写动作取消，则整次搜索**拒绝（rejected）**，由调用方在 EDT 上重新发起一次完整搜索；已被监听者看到的部分结果会先通知一次再重来。**[Swing 特有/可直接实现]**（来源：SearchResults.java:289-308, 312-323, 423-434；LivePreviewController.java:163-164）
- 正则内部的取消检查点见 §5.4。**[可直接实现]**
- 全仓搜索在取消后有一个"取消风暴重排"闹钟（`mySearchRescheduleOnCancellationsAlarm`），避免取消抖动期间反复重启。**[Swing 特有]**（来源：FindPopupPanel.java:253, 893, 1290-1300）
- 全仓搜索的分页加载：滚动接近底部（距底 < max(视口高/2, 50 px)）时自动加载下一页。**[可直接实现]**（来源：FindPopupPanel.java:1326-1343）

### 8.4 状态文本格式化

- 计数用复数选择格式：`0 results` / `1 result` / `N results`；当前项格式为 `{当前}/{总数}`；超限为 `{上限}+`。**[可直接实现]**（来源：platform/ide-core/resources/messages/ApplicationBundle.properties:652-654）

---

## 9. 全仓文本搜索（Find in Files）补充规则

> 对应 Augit 的"全仓搜索"覆盖层。只记录与 UI/交互有关、且与"当前文件查找"不同的部分。

### 9.1 面板结构

- 头部一行：**标题 `Find in Files`（加粗）→ 计数信息 → 加载图标（resizable 列，占满余量）→ 文件掩码复选框 → 掩码下拉/输入框 → 范围过滤按钮 → 固定按钮**；New UI 下垂直间距为 0，标题按 `ComplexPopup.headerInsets` 缩进，计数信息用 `ContextHelp.FOREGROUND` 颜色。**[可直接实现]**（来源：FindPopupHeader.kt:43-87）
- New UI 不画头部分隔竖线；旧 UI 会插一条 1×24 的分隔块。**[可直接实现]**（来源：FindPopupHeader.kt:79-82, 136-141）
- 文件掩码输入框是**可编辑下拉**，宽度被夹在 80–500 px 之间，最多显示 8 行候选。**[可直接实现]**（来源：FindPopupHeader.kt:97-134）
- 输入框 `columns = 25`、`rows = 1`；输入框与结果表共享滚动动作（结果表可被键盘滚动）。**[可直接实现]**（来源：FindPopupPanel.java:753-761, 828-831）
- 结果表：不显示列头、不显示网格、单元间距 0、不可编辑、允许多选、行始终按"聚焦态"绘制。**[可直接实现]**（来源：FindPopupPanel.java:745-751）
- 弹层位置/尺寸跨会话记忆（`DimensionService` 键 `find.popup`），分割条比例键 `find.popup.splitter`，左右初始比例 0.33。**[可直接实现]**（来源：FindPopupPanel.java:238-239, 500-508, 895）

### 9.2 交互时序

- 输入框文本变化 → `scheduleResultsUpdate()`：先 `updateControls()`，`cancelAllRequests()`，再 **100 ms** 后启动结果加载。**[可直接实现]**（来源：FindPopupPanel.java:1290-1300, 874-889）
- 文件掩码变化同样走 `scheduleResultsUpdate`（掩码输入框回车也触发）。**[可直接实现]**（来源：FindPopupPanel.java:627, 630-649）
- 结果表选中行变化 → **50 ms** 后刷新预览。**[可直接实现]**（来源：FindPopupPanel.java:869-873）
- 打开时：先应用上次尺寸/位置，关闭其它 popup，按需定位到屏幕点，然后 `invokeLater(scheduleResultsUpdate)`（即打开即搜）。**[可直接实现]**（来源：FindPopupPanel.java:500-529）
- 关闭策略（失焦即关）：`canBeClosed()` 要求"未被固定 && 应用处于激活 && 有聚焦窗口 && 无子 popup 打开"；每次失焦只关闭一层 popup，不直接关面板。**[可直接实现]**（来源：FindPopupPanel.java:534-558）
- 快捷键：`Enter` 打开到"Find"窗口；`Ctrl+Enter`（Mac `Cmd+Enter`）走 `openInFindWindow`；`Shift+Alt+Backspace` 重置全部筛选；`F1`/`Help` 打开帮助；`FindNext`/`FindPrevious` 在结果表里上下移动选中行。**[可直接实现]**（来源：FindPopupPanel.java:232-236, 521-527, 651-652, 833-859）
- 三个选项开关在全仓搜索里也带助记符快捷键（由动作文字里的 `&` 推导，如 `Alt+C`/`Alt+W`/`Alt+X`），注册在整个面板上。**[可直接实现]**（来源：FindPopupPanel.java:2026-2030）

### 9.3 结果文本

- 计数信息两种形态：完成时 `{n} matches in {m} files`（按单复数变形）；加载更多中为 `{n}+ matches in {m}+ files`。**[可直接实现]**（来源：FindBundle.properties:177-178）
- 单条结果内含文件名与路径；路径过长时按"保留尾部/中间截断"处理（`trimMiddle`，上限 120 字符）。**[可直接实现]**（来源：FindPopupPanel.java:370-380, 1091-1114）
- 结果数量有硬上限，超出后走分页（"load more"行：左侧缩进 = popup selection 内距 + 左右内距，行高按 15 行文本估算预览面板高度）。**[可直接实现]**（来源：FindPopupPanel.java:1311-1324, 862-868）

---

## 10. 文件名搜索 / 全局搜索（Search Everywhere · 快速打开文件）补充规则

> 对应 Augit 的 `Ctrl+P` 快速打开文件与"全仓搜索"覆盖层的标签页形态。

- 头部是**标签页 + 右侧工具条**：标签页置左、工具条右对齐并与标签页基线对齐，工具条左侧间距 18 px；头部底边有 1 px 分隔线；标签页字体为常规字重、背景 `ComplexPopup.HEADER_BACKGROUND`、不可聚焦。**[可直接实现]**（来源：platform/lang-impl/src/com/intellij/ide/actions/searcheverywhere/SENewUIHeaderView.kt:26-52）
- 查询节流同样是 **100 ms**（Swing 线程闹钟），并且 **查询为空时延时为 0**（立即重建，用于显示最近文件等）。重建请求在途时不会重复排队（`rebuildListScheduled` 标志位）。**[可直接实现]**（来源：SearchEverywhereUI.java:910-929）
- 查询变化时：先关闭过滤弹出层 → 重排列表 → `stopSearching()` → 把结果列表空文本设为 `Searching…` → 按"查询是否为空"切换列表视图密度（空查询用 SHORT 视图，非空用 FULL 视图）。**[可直接实现]**（来源：SearchEverywhereUI.java:931-943, 1110-1126）
- 每个 contributor 使用独立 `ProgressIndicator` 并支持取消：取消后不再接受该 contributor 的元素。**[可直接实现]**（来源：SearchEverywhereUI.java:1655-1715）
- 无结果空文本（按优先级）：
  1. contributor 自带 `SearchEverywhereEmptyTextProvider` 时由它接管；
  2. 追加 "Try to reset scope" + 作用域链接；
  3. 再追加"在 Find 结果中打开""清除过滤"等可点击动作。
  **[可直接实现]**（来源：SearchEverywhereUI.java:1926-1969）
- 索引未就绪时给出"索引模式/轻量模式下不完整"的专用提示文案，而不是"没有结果"。**[可直接实现]**（来源：SearchEverywhereUI.java:962-977）
- `Esc` 关闭弹层（取 `EditorEscape` 的快捷键集，缺失时退回 `CommonShortcuts.ESCAPE`）。**[可直接实现]**（来源：SearchEverywhereUI.java:1106-1108）
- 结果条数上限按"单 contributor / 多 contributor"区分；"Files"标签页会向主文件 contributor 请求更多元素。**[可直接实现]**（来源：SearchEverywhereUI.java:947-960）
- 上下移动列表时焦点保留在搜索框（滚动动作把列表与搜索框成对安装）。**[Swing 特有]**（来源：SearchEverywhereUI.java:905-908, 1071）

---

## 11. 与 Augit 现状的对照（供实现排期）

| 规则 | IntelliJ 取值/行为 | Augit `current-find.js` 现状 | 结论 |
| --- | --- | --- | --- |
| 查找条位置 | 编辑器整行 NORTH，正文下移 | 插到 `.code-view` 之前的整行条 | 一致 |
| 输入即搜 | 每次变更触发，30 ms 合并 | 每次 `input` 立即搜，无合并 | 建议补 30 ms 合并（或说明刻意不做） |
| 首个匹配 | 光标处/之后，输入中黏住当前项 | 首个匹配（`select(false)` 从 0） | 建议改为"从当前光标向后 + 黏住" |
| 计数格式 | `i/N`；搜索中 `N results` | 恒为 `i/N` | 建议补"搜索中显示 N" |
| 越界两段式 | 先提示"…press F3 to search from the top"，再按才回绕 | 直接取模回绕 | 需补两段式提示 |
| 当前项样式 | caret 色圆角描边，无背景 | `<mark class="find-current">` | 需核对 CSS 是否为描边而非填充 |
| 其余匹配样式 | `TEXT_SEARCH_RESULT_ATTRIBUTES` 背景 | `<mark class="find-match">` | 需核对色值 |
| 高亮实现 | markup model 叠加，不改文本 | 重建行内节点 + `<mark>` | 一致（未改 textContent 语义） |
| 非法正则 | 输入框变红 + `Bad pattern`，不搜索 | 状态显示 `正则表达式无效`，输入框不变色 | 建议补输入框红色态 |
| 无匹配 | 输入框变红 + `0 results` | 状态 `0/0`，不变色 | 建议补输入框红色态 |
| 选项记忆 | 按项目记忆查询串与三开关 | 会话内保留，重开预填 `savedQuery` | 建议持久化（C#/配置层） |
| 匹配上限 | 10000，超限不画高亮 + 链接 | 无上限 | 建议补上限与"点击高亮"入口 |
| 全字匹配 | `isJavaIdentifierPart` + 相邻同字符规则 | `[\p{L}\p{N}_]` | 明确差异，勿声称等价 |

（Augit 现状出处：docs/ux-mockups/current-find.js:34-35, 61-95, 96-175, 202-254；第 233 轮行号重取）

---

## 12. 需复核实清单

1. **需复核实**：New UI 下查找条输入框的实际视觉内距是否就是 `insets(7, 10, 7, 8)`（该值是 `JBUI.CurrentTheme` 的**默认回退**，实际可被主题 json 覆盖；本仓库未找到 expUI 主题里对 `Editor.SearchField.borderInsets` 的显式覆盖，需在真机 PyCharm 2026.2.1 用取色/量距确认）。
2. **需复核实**：expUI 主题中 `TEXT_SEARCH_RESULT_ATTRIBUTES` 的 `EFFECT_TYPE` 未显式给出，暗色下 effect 色 `#165e70` 具体画成下划线还是描边需真机确认（浅色方案同样未给出 effect 类型）。
3. **需复核实**：New UI 关闭按钮用的是 `AllIcons.General.Close`，其 16×16 参数未在本仓库的内联图标清单中直接列出（`AllIcons.java` 未收录该映射行），需真机确认尺寸与悬停态。
4. **需复核实**：编辑器查找条在全仓/全局弹层里的"加载中"是否真的完全没有视觉指示（本文结论基于代码中不存在 spinner 与 `isSearchInProgress` 只驱动按钮禁用）。
5. **需复核实**：`editorsearch.toomuch` 的 `10000+` 是否在 New UI 下原样显示为 `10000+`（未在真机确认是否有本地化差异）。
6. **需复核实**：`Character.isJavaIdentifierPart` 在实际 JDK/Unicode 版本下的具体字符集合（会影响全字匹配边界），本仓库未锁定 JDK 版本。
7. **需复核实**：`SearchTextArea` 文本域的上下内距公式是 Swing 字体度量推导，Web 端等价值需按实际字体重新量取。
8. **需复核实**：`REBUILD_LIST_DELAY = 100` 与 `USER_ACTIVITY_TRIGGERING_DELAY = 30` 是否与本仓库 commit `576e328` 的 PyCharm 2026.2.1 发行版本完全一致（分支差异未逐文件比对）。
