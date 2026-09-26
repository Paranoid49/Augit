# 15 · 快速打开与跳转行（第 7 区）

本册是 `11-surface-audit.md` 第 7 区的采集结果，场景：`quick-open`、`quick-open-empty`、`go-to-line`、`search-limited`。
口径同前：每条都带 checkout（`/mnt/d/github/intellij-community`，commit `576e328`）里的文件:行号。

第 152 轮落地了**跳转行**侧的采集与一处交互改正；**快速打开**侧已定位到权威目录，采集放下一轮（见 §2）。

## 1. 跳转行

权威是 `GotoLineAction`（动作、启用条件）＋抽象对话框 `GotoLineNumberDialog`（标题/标签/预填/解析）＋
编辑器实现 `EditorGotoLineNumberDialog`（相对行号、执行与滚动）。

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 对话框标题 | `IdeBundle.message("dialog.title.go.to.line.column")` = **"Go to Line:Column"** | `platform/platform-impl/src/com/intellij/ide/util/GotoLineNumberDialog.java:37`；`platform/platform-api/resources/messages/IdeBundle.properties:2089` |
| 输入框标签 | `label.line.column` = **"[Line] [:column]:"** | `GotoLineNumberDialog.java:100`；`IdeBundle.properties:2088` |
| 初始值 | `String.format("%d:%d", getLine() + 1, getColumn() + 1)` ⇒ **用当前插入符位置预填 1 基的"行:列"** | `GotoLineNumberDialog.java:108` |
| 打开时的焦点 | `getPreferredFocusedComponent()` 返回输入框 | `:40-42` |
| 预填文字**全选** | `MyTextField` 的 `focusGained`：`if (!e.isTemporary()) selectAll();` ⇒ 直接输入即整段替换 | `:76-84` |
| 输入框首选宽度 | `getPreferredSize()` = **200 × 高度** | `:87-91` |
| 输入解析 | 模式 `\s*(\d+)?\s*(?:[,:]?\s*(\d+)?)?\s*`：接受 `12`、`12:3`、`12,3`；行取 `parseInt(组1, 当前行+1)`（**空输入 = 当前行，仍然有效**），列取 `parseInt(组2, -1)`；仅当 **`行 > 0`** 才有效 ⇒ `Coordinates(行-1, max(0, 列-1))` | `:29`、`:47-55` |
| 相对行号 | `[+-]\d+` ⇒ 相对插入符行移动，结果**钳制到 `[0, 总行数-1]`**，列为 0 | `platform/platform-impl/src/com/intellij/ide/util/EditorGotoLineNumberDialog.java:31-45` |
| 确认时的非法输入 | `doOKAction()`：`if (coordinates == null) return;` ⇒ **窗口不关、不报错、什么都不做**（基类没有 `doValidate`，OK 按钮始终可用） | `EditorGotoLineNumberDialog.java:47-49` |
| 确认后的动作 | `scrollToCaret(ScrollType.CENTER)`（**目标行滚到可视区中部**）→ `removeSelection()` → 焦点交给编辑器正文组件 | `:50-57` |
| 动作启用条件 | `GotoLineAction.update()`：有 project 且 `EDITOR_EVEN_IF_INACTIVE != null` 才 enabled+visible；否则**两者都关** | `platform/platform-impl/src/com/intellij/ide/actions/GotoLineAction.java:52-60` |
| 动作名 | `command.go.to.line` = "Go to Line" | `IdeBundle.properties:383` |
| 仅内部可用的 Offset 行 | 第二行 "Offset:" 输入框只在 `ApplicationManager.getApplication().isInternal()` 时创建 | `GotoLineNumberDialog.java:110-155`；`IdeBundle.properties:2087` |

### 1.1 与 Augit 的差异与处置

Augit 的跳转行入口是 `Ctrl+G` 与正文工具栏的「跳转行」按钮，落到共享的紧凑单行窗口（`live-data.js` 的
`openGoToLineDialog()` → `compactInputDialog("跳转行", "行号", "", "跳转")`，确认走 `goToLine()`）。

| 差异 | 处置 |
| --- | --- |
| 目标行滚动位置：权威 **CENTER**，Augit 原为 `block:"nearest"`（"够到就停"） | **第 152 轮按权威改为 `block:"center"`**（`live-data.js` 的 `goToLine()`）。同文件里差异块导航早已用 `center`，改动后口径一致 |
| 确认后焦点交给正文 | **本来就对齐**：权威 `requestFocus(myEditor.getContentComponent())`，Augit 关闭窗口后把焦点交给 `.code-view` ✓ |
| 非法输入不关窗 | **本来就对齐**（Augit 同样不关窗）；但**反馈不同**，见下表 |
| 动作启用条件 | **本来就对齐**：Augit 的四个正文工具栏按钮只在文本视图里渲染（Blame 等视图不渲染这组按钮，`live-shell` 已有断言），等价于权威"没有编辑器就不启用" ✓ |

### 1.2 有意**不照抄**权威的三处（附理由）

| 权威行为 | 为什么 Augit 不照抄 |
| --- | --- |
| 非法输入**完全静默**（`doOKAction` 直接 `return`，没有 `doValidate`，也没有提示） | 这是 Swing 对话框缺少反馈的**缺陷**，不是设计。Augit 的 §10.2 规则要求错误说明"发生了什么／哪些状态未改变／可以做什么"，故保留 `"请输入行号（正整数）。"`／`"行号从 1 开始。"`／`"当前文件只有 N 行。"`。**对齐意图而不是照抄缺陷**（同第 143 轮 Reset 的 `validateFields()` 处理方式） |
| 越界行号被**钳制**到最后一行 | 绝对行号的钳制并不是 `getCoordinates()` 的行为，而是 `LogicalPosition`／插入符模型在越界时的**副作用**：`getCoordinates()` 只保证 `行 > 0`，真正钳制的是 `moveToLogicalPosition`。权威里**有意的**钳制只出现在相对行号那一支（`Math.min(logicalLine, linesTotal - 1)`）。Augit 保留"超出末尾就说明文件实际行数"的显式反馈，不为一个副作用丢掉错误说明 |
| 预填当前"行:列"并全选 | Augit 的只读正文**没有插入符、也没有列**。要复现预填就得先发明一个"当前行"状态（插入符/光标），那是**新增能力**，触碰用户裁决的边界；且规范第 335 行明确"不增加列跳转"。⇒ 保持空输入 + 聚焦，只**登记**这条差异 |
| 接受 `line:column`、`line,column`、`+N`／`-N` | 同上：列与相对行号都以插入符为参照，只读查看器没有该参照。⇒ 仍只接受正整数行号（`ux-spec.md:335`），**不新增**列跳转或相对跳转 |
| 输入框首选宽度 200px | 该输入框属于**四个动作共用**的紧凑窗口（跳转行、检出引用、创建跟踪分支、重命名），改宽度会一起影响另外三个（属第 2 区范围）。而权威的 200px 是 Swing 首选宽度、窗口本身没有固定宽；Augit 的 400px 窗口与 100% 输入框是既有适配。⇒ 登记不动 |
| 仅内部构建可见的 `Offset` 行 | `isInternal()` 才创建 ⇒ 正式版用户看不到，且 Augit 没有偏移量概念 ⇒ 不适用 |

## 2. 快速打开（第 153 轮采集并落地两处）

**入口即"单贡献者"搜索**：`GotoFileAction.actionPerformed()` 调 `showInSearchEverywherePopup(FileSearchEverywhereContributor.class.getSimpleName(), e, true, true)`
（`platform/lang-impl/src/com/intellij/ide/actions/GotoFileAction.java:21-24`）⇒ 它是 Search Everywhere 弹层里**只有 File 一个贡献者**的那一档，
不是 `All` 混合档。这决定了下面两条数值与状态规则。

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 结果上限 | 每贡献者 **`SINGLE_CONTRIBUTOR_ELEMENTS_LIMIT = 30`**（混合档 `MULTIPLE_CONTRIBUTORS_ELEMENTS_LIMIT = 15`）；`limit = contributors.size() > 1 ? 15 : 30` | `SearchEverywhereUI.java:217-218`、`:951-958` |
| **搜索进行中的空态** | `rebuildList()` 每次搜索**先 `stopSearching()`、把列表空态设为 `label.choosebyname.searching`**，随后 `myListModel.expireResults()` ⇒ **上一个查询的结果被清掉**，列表显示 **"Searching…"** 直到新结果到达 | `SearchEverywhereUI.java:932-936`、`:978`；`IdeBundle.properties:675` |
| 首项选中 | 结果到达后选中恒为 0 号（除非有"同一页签上次选中项"可恢复）：`elementsSelected(new int[]{0})`（`selectFirstItem()`）与"size>0 且 selectedIndex<=0 时尝试恢复上次选择" | `:1408-1410`、`:1846-1860` |
| 上下键 | **到端点不回绕**：`scrollList(true/false)` 不是方向键，而是绑定在 `NAVIGATE_TO_NEXT_GROUP`／`NAVIGATE_TO_PREV_GROUP`（跳首/末项）上（`MixedSearchListModel.getIndexToScroll` = `getSize()-1`／`0`）；方向键走列表默认导航 | `:1096-1101`、`:1326-1333`；`MixedSearchListModel.java:56-58` |
| `Enter` | 打开当前选中项（`elementsSelected`） | `:1363-1387` |
| 单击 vs 双击 | 该页签**有 preview provider** ⇒ `isPreviewDoubleClick` 为假：单击只改变当前项（预览），**双击**才正式打开 | `:1363-1370` |
| 空查询提示 | 弹层空查询时提示 `Type / to see commands`；顶部 6 个分类页签与 `Include non-project items` 复选 | 见 `ui-compliance.md:291` 的实拍对照 |

### 2.1 与 Augit 的差异与落地

| 差异 | 处置 |
| --- | --- |
| **查询进行中**：权威先清掉上一个查询的结果并显示 "Searching…"，结果到达才替换 | **第 153 轮落地**：`runSearch()` 先落到 `pending: true` 的"进行中"状态（`matches` 清空）再发查询，`liveSearchOverlay()` 在无命中且 `pending` 时显示「正在搜索…」。**顺带修掉一个真实错误状态**：Augit 原来在查询在途时仍按**上一个查询**渲染，上一个查询无命中就会对着新查询显示"未找到结果" |
| 结果到达**回写输入框** | **第 153 轮落地**：新增 `renderSearchOverlay()` —— 重绘前后保留输入框里用户已经继续输入的值与选择区。权威的搜索框内容从不由结果回写；Augit 原来每次结果到达都整体重绘成"已发出的那个 query"，慢查询返回时会吞掉用户新敲的字（与历史面板"数据到达不得打断用户输入"同一类缺陷） |
| 首项选中 | **本来就对齐**：`liveSearchOverlay()` 给 0 号结果加 `.selected` ✓ |
| 上下键到端点**是否**回绕 | **第 155 轮修正**：当时写"权威不回绕"过于绝对。权威只把 Up／Down 从输入框转发给结果列表（`SearchEverywhereUI.java:906-907`），而转发动作的 `cycleScrolling` 取 **`UISettings.getInstance().getCycleScrolling()`**（`ScrollingUtil.java:328-338`）⇒ **是否回绕取决于用户的"循环滚动"设置**。Augit 用 `Math.max(0, Math.min(len - 1, index ± 1))` 钳制，且**没有这个设置**（新增设置＝新增功能）⇒ 记为"已知且有意保留的差异" |
| 单击选择、双击/`Enter` 打开预览标签 | **本来就对齐**（规格 §5.2 与权威"有 preview provider 时单击不打开"一致）✓ |
| 结果上限 **30** vs Augit 的 **100** | **未落地，转 C# 项**：权威单贡献者上限是 30，而 Augit 的 100 写在规范（`ux-spec.md:587`）并由宿主单测钉住（`RipgrepSearchServiceTests` 的"文件搜索最多返回一百项"）⇒ 要动 C# 服务与宿主单测，且受并行会话占用的 shell 阻塞。已记入 `10-backlog.md` §三·补 第 6 项（连同"落地时同步改规范与既有单测"） |
| "同一页签的上次选中项"记忆 | **登记不实施**：权威会记住并在结果到达时恢复上次选中项（`:1846-1860`）。对 Augit 的快速打开而言，这要求跨"打开/关闭浮层"保留一份选择状态，属于**状态机的新增分支**（且与预览标签的复用规则纠缠）⇒ 先登记，等与真机对照后再定 |
| 6 个分类页签、`Type /` 命令、`Include non-project items` | **不新增**：那是 Search Everywhere 的多贡献者能力，Augit 的快速打开按产品规格只搜文件名（`ui-compliance.md:291` 已如实记录该差异） |

## 3. 验证（第 152–153 轮）

### 第 152 轮（跳转行）

- 产品改动只有 `web/src/live-data.js` 的 `goToLine()`（`nearest` → `center`）＋注释；未改 `mockup.js`／`mockup.css`。
- `tools/audit/live-shell.spec.cjs` 的 `Ctrl+G` 段新增一条 `check`：记录 `scrollIntoView` 的调用参数，断言 `block === 'center'`。
  夹具里的两份文本都不足一屏、观察不到几何居中，故断言的是**调用契约**（失败信息里带出全部调用参数）。
- 未改 C#（跳转行完全在网页层）⇒ 不需要构建。

### 第 153 轮（快速打开）

- 产品改动：`web/src/mockup.js` 的 `liveSearchOverlay()`（"正在搜索…"分支）、`web/src/live-data.js` 的 `runSearch()` ＋新增 `renderSearchOverlay()`。
- `live-shell` 的快速打开段新增三条 `check`：`查询进行中先清空上一个查询的结果并显示正在搜索`、`结果到达不覆盖用户继续输入的内容`、`查询落地后回到结果列表`。
  为了看见中间态，宿主 mock 的 `search/files` 支持注入延迟（`window.__searchFilesDelay`）——本地文件名搜索太快，不注入就一闪而过。
- `mockup.js` 与 `docs/ux-mockups/mockup.js` 字节一致；未改 C#（上限一项属 C#，见 §2.1）⇒ 不需要构建。
