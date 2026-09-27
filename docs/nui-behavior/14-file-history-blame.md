# 14 · 文件历史与 Blame 的交互（第 3 区）

本册是 `11-surface-audit.md` 第 3 区（`file-history`、`blame` 两个场景）的采集结果。
口径同 `12-commit-changes.md`／`13-git-dialogs.md`：每条都带 checkout（`/mnt/d/github/intellij-community`，commit `576e328`）里的文件:行号。

第 150 轮落地了 **Blame 归属行的悬停提示**；文件历史列表的列集合差异本轮只采集、未落地（见 §2.2）。

## 1. Blame 归属列

### 1.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 归属列（aspect）集合 | `GitFileAnnotation.getAspects()` 返回 **`{REVISION, DATE, AUTHOR}`** —— 数组顺序即渲染顺序 | `plugins/git4idea/backend/src/annotate/GitFileAnnotation.java:125-128` |
| 各列**是否默认显示** | `DATE` = **true**、`AUTHOR` = **true**、`REVISION` = **false** ⇒ **默认只显示"日期 + 作者"两列**，修订列默认关闭 | 同文件 `:84-85`（DATE）、`:93-94`（REVISION）、`:101-102`（AUTHOR）；默认值由各 aspect 自己的 `isShowByDefault()` 提供，经 `VcsUtil.isAspectAvailableByDefault(id, defaultValue)` 读取 | 
| 日期列的**值** | `FileAnnotation.formatDate(date)` = `DateFormatUtil.formatPrettyDate(date)`（"今天/昨天"这类相对日期，取不到时退回 `formatDate`） | `GitFileAnnotation.java:88-91`；`platform/vcs-api/src/com/intellij/openapi/vcs/annotate/FileAnnotation.java:284-286`；`platform/platform-api/src/com/intellij/util/text/DateFormatUtil.java:111-118` |
| 作者列的**值** | `VcsUserUtil.toExactString(lineInfo.getAuthorUser())` | `GitFileAnnotation.java:105-107` |
| 修订列的**值** | `lineInfo.getRevisionNumber().getShortRev()`（**短**哈希） | `GitFileAnnotation.java:96-98` |
| 归属行的**悬停提示** | `FileAnnotation.getToolTip(lineNumber)` 取**纯文本**变体（`getToolTip(line, false)`），逐行拼：`commit {修订}` → `Author: {作者}` → `Date: {日期时间}` → [`Path: {路径}`] → **空行** → 提交信息 | `GitFileAnnotation.java:175-176`、`:184-206` |
| 提示里修订的形态 | `revisionNumber.asString()` = **完整哈希**（与列里显示的短哈希不同） | `GitFileAnnotation.java:191`；`plugins/git4idea/backend/src/GitRevisionNumber.java:48-50` |
| 提示里其他两行 | `commit.description.tooltip.author` = **"Author: {0}"**、`commit.description.tooltip.date` = **"Date: {0}"**（日期用 `DateFormatUtil.formatDateTime`，即**日期 + 时间**）、`commit.description.tooltip.commit` = **"commit {0}"** | `GitFileAnnotation.java:191-193`；`platform/vcs-api/vcs-api-core/resources/messages/VcsBundle.properties:975-977`；`DateFormatUtil.java:120-122` |
| `Path:` 行的出现条件 | 仅当 `!myFilePath.equals(lineInfo.getFilePath())`（归属跨文件，例如全仓归属） | `GitFileAnnotation.java:195-197`；文案 `VcsBundle.properties:978` |
| 提交信息段落 | `appendCommitMessageBlock(commitMessage)`：前面**空一行**再写消息；取不到完整消息时退回 `lineInfo.getSubject() + "\n..."` | `GitFileAnnotation.java:200-204` |
| 提示的换行规则 | 首行不加前导换行；其后每行前加 `\n`；消息块前加 `\n\n` | `platform/vcs-impl/src/com/intellij/openapi/vcs/annotate/AnnotationTooltipBuilder.java:35-60` |
| 归属视图的**动作家族** | `AnnotateCurrentRevisionAction`、`AnnotatePreviousRevisionAction`、`AnnotateRevisionAction`、`AnnotateDiffOnHoverToggleAction`、`AnnotateToggleAction`、`AnnotateLocalFileAction`，打包入口 `AnnotateActionGroup` | `platform/vcs-impl/src/com/intellij/openapi/vcs/annotate/actions/` |

### 1.2 与 Augit 的差异与落地

**归属列本身：本来就对齐，本轮确认不是缺口。** Augit 的行是 `<span>日期</span><span>作者</span><span>行号</span>`（`web/src/mockup.js` 的 `liveBlameView()`／`blameView()`），
即**日期 → 作者 → 行号**。权威的 aspect 数组虽以 REVISION 打头，但 `REVISION.isShowByDefault() == false`（`GitFileAnnotation.java:93-94`），
**默认可见顺序正是"日期 → 作者"**，行号是编辑器的行号槽、不属于 aspect。⇒ 列集合、列顺序都一致，**不需要加修订列**。
（此前审计设想"Augit 缺修订列"，实测默认关闭后不成立——这类"授权默认值是 false"的项必须查 `isShowByDefault()`，不能只看 aspect 存在。）

**归属行的悬停提示：第 150 轮补齐。** 权威逐行给提示（`getToolTip`），Augit 此前**没有任何 tooltip**。落地点：

- `web/src/mockup.js` 新增 `blameRowTooltip(revision, author, date, message)`，按下表拼装；
- 两个渲染点都接上：实时侧 `liveBlameView()` 用 `line.fullHash || line.hash`（权威提示用完整哈希，**不是**列旁的短哈希）、`line.author`、`line.date`、`line.summary`（`git/blame` 载荷本就带这四个字段，故**无需改外壳**）；样例侧 `blameView()` 取该提交在日志样例里的同一份数据（`commit-4` / `I49` / `2026/8/28 8:25` / `feat: 实现 Augit 阶段零至五功能`）。
- `Path:` 行不出现——本视图是**按文件**归属（`GitFileAnnotation.java:195-197` 的条件不成立）。
- 样例侧提示里的日期**带时间**、而归属列只有日期，恰是权威"列值用 `formatPrettyDate`、提示用 `formatDateTime`"的差别。**第 201 轮已补齐实时侧**：外壳 `git/blame` 每行同时回传短日期 `date` 与日期时间 `dateTime`（`yyyy/M/d H:mm`，本地时区，`ShellBridge.cs`），界面用 `blameTooltipDate()` 取后者做提示、槽位仍显示前者（见 §1.3 的结案行）。

最终文本形态（实时侧，示例取 `live-shell.spec.cjs` 的注入载荷）：

```
commit full-aaa
Author: l49
Date: 2026/9/15

feat: 一
```

### 1.3 待补（都不新增功能，转 `10-backlog.md` §三·补 C# 接线）

| 项 | 现状 | 权威/判断 |
| --- | --- | --- |
| ~~提示 `Date:` 缺时间~~ **第 201 轮已落地** | 权威 `Date:` 用 `DateFormatUtil.formatDateTime`（日期 + 时间）；现桥接每行多回 `dateTime = AuthorDate.ToLocalTime().ToString("yyyy/M/d H:mm")`，`loadBlame` 透传，`blameTooltipDate()` 取它做提示（槽位仍用短日期 `date`），并新增桥接单测 `归属行同时回传短日期与日期时间` | — |
| ~~**Annotate Previous Revision**~~ **第 202 轮已落地**（见 §1.4） | `IGitServices.ReadBlameAsync` 已有 `revision` 形参（`src/Augit.Core/Git/IGitServices.cs:142-146`），但 `ShellBridge.ReadBlameAsync` 把第三个实参**硬编码为 `null`**（`ShellBridge.cs:2530`），界面只发 `{ path }`（`web/src/live-data.js:290`）⇒ 能力在 C#、桥接与入口缺失 | 权威有 `AnnotatePreviousRevisionAction`；落地属"接线既有能力"，非新增功能 |
| 其余归属动作（`AnnotateRevisionAction` 选修订、`AnnotateDiffOnHoverToggleAction` 悬停看差异、`AnnotateLocalFileAction` 本地文件） | Augit 只有"打开 Blame"一条入口 | **不新增**（用户裁决：不新增 Augit 没有的功能）；登记备查 |

### 1.4 「标注上一修订」—— **第 202 轮已落地**

权威 `AnnotatePreviousRevisionAction`（`VcsBundle.properties:1058-1059` 的
`action.annotate.previous.revision.text` = "Annotate Previous Revision"）挂在**注释槽的动作组**里
（`AnnotateToggleAction.java:272-276`），只在 `PreviousFileRevisionProvider` 能给出上一修订时出现
（`update()`：`myProvider == null` ⇒ `setEnabledAndVisible(false)`）；修订取自
`GitPreviousFileRevisionProvider.getPreviousRevision(lineNumber)` 的第一分支
`lineInfo.getPreviousFileRevision()`（`GitFileAnnotation.java:482-501`），也就是
`git blame --line-porcelain` 的 `previous <sha> <file>` 头。

落地：`GitBlameLine.PreviousRevision` ＋ porcelain 解析该头；`git/blame` 收 `revision`／回 `previousRevision`；
归属行右键菜单一项（只对有上一修订的行）；按该修订就地重新标注，工具栏显示 `上一修订 <短哈希>`。
**登记差异**：权威把结果开在**新标签**里（动作描述 "…in a new tab"），Augit 就地重标注同一视图。

## 2. 文件历史列表

### 2.1 权威

列集合与顺序由 `FileHistoryPanelImpl.createColumnList` 依次追加（`platform/vcs-impl/src/com/intellij/openapi/vcs/history/FileHistoryPanelImpl.java:292-306`）：

| 序 | 列 | 表头 | 值 | 出处 |
| --- | --- | --- | --- | --- |
| 1 | `RevisionColumnInfo` | `column.name.revision.version` = **"Version"** | `VcsUtil.getShortRevisionString(...)`（`ShortVcsRevisionNumber` 取短形态，否则 `asString()`） | `:296-297`、`:652-688`；`VcsBundle.properties:160`；`platform/vcs-api/src/com/intellij/vcsUtil/VcsUtil.java:402-406` |
| 2 | `DateColumnInfo` —— **仅当 `!provider.isDateOmittable()`** | `column.name.revision.date` = **"Date"** | `DateFormatUtil.formatPrettyDateTime(date)` | `:298`、`:690-728`；`VcsBundle.properties:161` |
| 3 | `AuthorColumnInfo` | `column.name.revision.list.author` = **"Author"** | `revision.getAuthor()`，**作者与提交者不同名时值追加 `*`**；单元格 tooltip = `{作者} <{邮箱}>`，提交者不同再追加 `, via {提交者} <{邮箱}>` | `:299`、`:751-799`；`VcsBundle.properties:111`、`:935` |
| 4 | provider 附加列（`components.getColumns()`） | Git 未提供 ⇒ 无 | — | `:300-304` |
| 5 | `MessageColumnInfo` | `label.selected.revision.commit.message` = **"Commit Message"** | 提交信息**首行主题**（`MessageColumnInfo.getSubject`） | `:305`、`:801-831`；`VcsBundle.properties:159` |

- Git 的 `isDateOmittable()` 返回 **false**（`plugins/git4idea/backend/src/history/GitHistoryProvider.java:75-78`）⇒ **日期列出现**，Git 文件历史的完整列顺序是 **Version → Date → Author → Commit Message**。
- 表体由 `DualView(…, columns, …)` 建立（`:184-200`），表头文本来自各 `ColumnInfo.getName()`。

### 2.2 与 Augit 的差异与落地（第 151 轮落地）

Augit 原先的文件历史行是 `<span>作者</span><span>日期</span><span>提交信息</span>`（**作者 / 日期 / 提交信息** 三列，无表头）。相对权威有四处差异，第 151 轮处理如下：

| 差异 | 处置 |
| --- | --- |
| **缺 "Version" 列** | **已落地**：首列改为短修订（实时侧取载荷里的 `commit.hash`，即 `git/file-history` 返回的 `entry.ShortHash`；样例侧取该提交在样例日志里的显示值 `commit-4`）。数据侧不需要改外壳——`hash`／`fullHash` 本来就在载荷里 |
| **列顺序不同** | **已落地**：改为权威顺序 **版本 → 日期 → 作者 → 提交信息**（CSS 网格四列 62px／108px／90px／`1fr`；权威只给 Swing 的 preferred 字符数 10／—／14／80 个 m，故列宽仍按 Augit 自身的 px 比例给） |
| **表头行** | **已落地**：新增 `.history-columns`（版本 / 日期 / 作者 / 提交信息）。依据：表体由 `DualView(ColumnInfo[])` 建表、表头文本取各 `ColumnInfo.getName()`（`FileHistoryPanelImpl.java:184-200`），其高度参与布局计算（`DualView.java:462` 用 `getTableHeader().getHeight()`），默认高度 25（`DarculaTableHeaderUI.java:115`）；`myDualView.setShowGrid(true)`（`FileHistoryPanelImpl.java:370`）。**表头与数据行共用同一套 CSS 列宽**，并由 `verify-ux-file-history` 逐列比对宽度，防止错位 |
| 作者列的 `*` 标记与单元格 tooltip | ~~未落地，转 §2.3~~ **第 203 轮已落地**（见 §2.4）：载荷补 `%cn`／`%ce` 与两个历史载荷的三字段，界面按权威规则渲染值与 tooltip |

改动的产品文件：`web/src/mockup.js`（新增 `fileHistoryColumns()`；`liveFileHistoryTool()`／`fileHistoryTool()` 改四列并插入表头；新增字号令牌 `--augit-history-columns-height`）、`web/src/mockup.css`（`.history-row`／`.history-columns` 共用四列网格；`.history-columns` 高度 25 与次级前景、底边分隔；`.history-list-pane` 网格改为三行）。`docs/ux-mockups/` 同名文件字节同步。

**未一并应用的权威值**：`myDualView.setShowGrid(true)`（`FileHistoryPanelImpl.java:370`）会在单元格之间画表格线。本轮只登记不应用——它是 Swing 的绘制开关，New UI 主题下的实际呈现（线色、是否被 LaF 覆盖）无法在离线环境核对，贸然应用就变成"用猜测覆盖已验收的视觉基线"。留待能与真机对照时再定。

### 2.3 作者列字段的来源（第 151 轮登记的缺口，第 203 轮已补）

权威的 `AuthorColumnInfo`：

- 值：`revision.getAuthor()`，**当 `author != committerName` 时追加 `*`**（`FileHistoryPanelImpl.java:780-788`）；
- 单元格 tooltip：`{作者} <{邮箱}>`，提交者不同再追加 `, via {提交者} <{邮箱}>`（`:764-778`；`file.history.details.committer.tooltip.info` = **"via {0}"**，`VcsBundle.properties:935`）。

现状：`GitHistoryEntry` **有** `AuthorEmail`（`src/Augit.Core/Git/GitHistoryModels.cs:33`），但 `ShellBridge.ReadFileHistoryAsync` 的投影只发 `hash`／`fullHash`／`subject`／`author`／`date`，把邮箱丢掉了；**提交者姓名/邮箱在模型里根本没有**（`GitHistoryEntry` 只有 author 一组）⇒ 需要"桥接补作者邮箱 + 模型与解析补提交者"两步，当时归入 `10-backlog.md` §三·补（C# 接线）第 5 项；**两步已在第 203 轮补齐，见 §2.4**。

### 2.4 作者列的 `*` 与单元格 tooltip —— **第 203 轮已落地**

权威 `FileHistoryPanelImpl.AuthorColumnInfo`：值 = `revision.getAuthor()`，作者 ≠ 提交者时加 `*`
（`valueOf`，`FileHistoryPanelImpl.java:780-788`）；单元格 tooltip = `{作者} <{邮箱}>`，提交者不同名再追加
`, via {提交者} <{提交者邮箱}>`（`getCustomizedRenderer`，`:764-778`；`file.history.details.committer.tooltip.info` = "via {0}"，
`VcsBundle.properties:935`）。字段来自 `GitFileRevision.getAuthorEmail()`／`getCommitterName()`／`getCommitterEmail()`
（`plugins/git4idea/backend/src/GitFileRevision.java:89-105`）。

落地：`GitHistoryEntry` 补 `CommitterName`／`CommitterEmail`；`%cn`／`%ce` 进 `HistoryFormat`（三处格式与解析同步，
非推送列表复用同一格式串）；`git/file-history` 与 `git/history` 都回传三字段；界面 `historyAuthorCell()` 按上述规则
渲染（文件历史、与当前分支比较、样例三处共用）。无登记差异。

## 3. 验证（第 150–151 轮）

- 新增断言：`tools/verify-ux-blame.cjs` 校验每行归属的 `title` 恰为权威四段文本、且所有行一致；
  `tools/audit/live-shell.spec.cjs` 的 Blame 段新增两条 `check`：实时侧用 **`fullHash`**（`commit full-aaa …`）、且**不以短哈希开头**。
- 四个登记哈希中只有 `web/src/mockup.js` 变化（`e023113591982e75dcc42c49ccf0dfaf` → `4071c7fa509a7f3b1c4bdeb0587e5086`），`mockup.css`／`live-data.js`／`bridge.js` 未变；`docs/ux-mockups/mockup.js` 与 `web/src/mockup.js` 字节一致。
- 未改 C#（提示所需字段已在 `git/blame` 载荷里）⇒ 本轮不需要构建。

### 第 151 轮（文件历史列表四列）

- 新增/改写断言：`tools/verify-ux-file-history.cjs` 断言四列文本（`commit-4`／日期／`I49`／标题）、前三列宽度 62/108/90、**表头列名与顺序**、以及**表头与数据行逐列同宽**；`tools/verify-ux-history-typography.cjs` 把 `.history-columns` 纳入"文字不被固定高度裁切"的检查对象；`live-shell` 新增 `文件历史表头为 版本→日期→作者→提交信息`、`文件历史行按权威列序填真实数据` 两条。
- 哈希：`web/src/mockup.js` `4071c7fa509a7f3b1c4bdeb0587e5086` → **`76bef20ed5d82a5ba76112f9289321b5`**；`web/src/mockup.css` `9ebdb35f0182369a6bab826657038640` → **`6da20a85fc685d0560d05db902c86692`**；`live-data.js`／`bridge.js` 未变。`docs/ux-mockups/` 两个同名文件字节一致。
- 未改 C#（`hash`／`shortHash` 已在 `git/file-history` 载荷里）⇒ 同样不需要构建。

## 文件历史列表的窄栏横向滚动（第 252 轮）

`ux-spec` §7.9 要求"窄栏在自身区域横向滚动"，而 `.history-list-pane` 原先是 `overflow: hidden`，
四列 `min-width` 又是 360px ⇒ 窗口收窄时内容**既不滚也不缩、直接被裁掉**
（实测 900px 窗口下列表区只剩 146px，行容器仍宽 360px、`scrollWidth === clientWidth`，右侧列完全看不到）。

修法：`.history-list-pane { overflow-x: auto; overflow-y: hidden }`。横向滚动放在**列表区**上
（而不是只把 `.history-rows` 变成滚动容器）——否则表头留在原地、与滚动的数据行错位。
实测：900px 窗口下列表区 `scrollWidth 360 > clientWidth 146`，真实横向滚轮滚动后表头与数据行
**一起**左移同样的像素数且逐列仍对齐；放宽后回到无滚动。

配套断言：表头与数据行共用同一套列宽（实测 `grid-template-columns` 逐值相同、四列左边缘与宽度相等）、
行内无提交图与右置元信息、宽→窄→宽三次布局变化中 `git/file-history`／`git/diff` 调用数、
选中提交与标签数都不变（沿用同一提交身份、不重建主窗口）。

> 断言教训：用"把容器 `scrollLeft` 写成 100"验证"能滚"是无效判据 —— `overflow: hidden` 的元素
> 仍可被**程序**滚动，断言分不出"能滚"与"被裁掉"。必须用真实横向滚轮（`mouse.wheel(deltaX, 0)`）。

## 文件历史往返的日志上下文与补查规则（第 253 轮）

进入文件历史前记下的"返回上下文"（`live.fileHistoryReturn`）与返回时的恢复：

- **已实现并断言**：底部工具窗状态、提交选择（`historySelectedHash`）、详情正文位置（`commitDetailScrollRestore`）、
  详情显隐（本轮新增 `live.historyDetailsHidden`）。已生效的组合筛选、页码、提交列表纵横滚动都因为存在 `live` 里
  而自然跨过往返（断言逐项核对）。
- **本轮修掉的缺陷**：
  1. 详情显隐此前只写在 DOM（`panel.style.display`）⇒ 底部工具窗一重绘就丢；现状态化，`applyHistoryDetailsState()`
     在每次渲染后落地，开关入口只改状态。
  2. 提交详情为空时，占位 `.empty-state`（`position: absolute; inset: 0`）的宿主 `.changed-files` 没有定位上下文
     ⇒ 空态铺满整窗（实测 `0,0,1180,760`）并吞掉**所有**点击（`elementFromPoint` 在工具入口上命中的是空态）。
     修法：`.changed-files`／`.commit-detail` 补 `position: relative`。
- **尚未实现（登记）**：变化文件树的折叠/选择/顶部位置没有状态键（树由 `live.commitDetails.filesHtml` 预渲染、
  交互只改 DOM）⇒ 任何重绘（包括切筛选）都会丢。
- **返回日志的补查规则**：日志**从未查询**过时返回必须补一次查询（实测调用数 1 → 2、数据落地）；
  已加载的日志（含**空**日志）直接恢复、不重复查询（调用数不变、空态文案逐字相同）。

## 提交详情里变化文件树的状态化（第 254 轮）

规格 §7.9 条目三把"折叠/选择/顶部位置"与筛选、页码、详情显隐并列为**进入文件历史前要保存的日志上下文**。
第 253 轮登记时这三项**都只写在 DOM 上**，本轮收口：

- **折叠此前根本没有实现**：`historyFilesHtml()` 给组行画了 `chevron-down`，却没有任何点击绑定 —— 点了不动、
  也没有可折叠的语义。现在组行带 `data-history-group="<相对路径前缀>"`（根行的键是空串），点击把
  `live.commitDetailsUi.collapsed[key]` 取反，整棵树由 `historyFilesHtml(files, ui)` **按状态重建**：
  折叠组换 `chevron-right`、写 `aria-expanded="false"`、子树逐行加 `hidden`。
  `hidden` 必须配一条样式规则（`.changed-files .tree-row[hidden] { display: none }`）—— 行是 `display: flex`，
  会盖掉 `[hidden]` 的 UA 规则，只写属性的话"折叠"只换箭头、行还在。
- **选择**：叶行点击/Enter 把 `selectedPath` 写进同一份状态，构建器按它回填 `.selected`（此前只改 DOM 类名，
  区域重绘就丢）。
- **顶部位置**：宿主 `[data-live-changed-files]` 的 `scroll` 事件写 `scrollTop`，`rebindAfterRender()` 与
  `loadCommitDetails()` 收尾按状态回填。两处守卫缺一不可：
  1. 被区域重绘替换下来的**旧宿主** `scrollTop` 会归零并派发 `scroll` 事件 ⇒ 必须 `host.isConnected` 才记录；
  2. 渲染期间存在"宿主还没有可滚高度"的窗口，那时读到的 0 同样会冲掉真实位置 ⇒ 只在 `scrollHeight > clientHeight`
     时记录，并照 `.commit-list` 的 `pendingHistoryScroll` 加 `pendingCommitFilesScroll` 恢复窗口
     （窗口内的滚动一律不当用户动作），窗口结束后按实际位置回写状态（内容变短时浏览器夹回的值才是真实值）。
- **状态按 `revision` 归属**：`commitDetailsUi(revision)` 在修订变化时整体重置 —— 折叠、选择与位置都属于那一条提交，
  改选提交后不得残留。
- **`filesHtml` 必须跟着状态走**：区域重绘与文件历史往返读的都是 `live.commitDetails.filesHtml`。
  第 254 轮实测的缺陷是**只在折叠时重建**：选择后没有重建 ⇒ 往返后渲染出"折叠了但没选中"的旧快照
  （`stateSelected === 'src/App.cs'` 而 DOM 里没有 `.selected`）。现在折叠走 `syncCommitFilesTree()`（重建 + 重写 DOM +
  恢复滚动），选择走 `rebuildCommitFilesHtml()`（只重建状态里的 HTML，不重写 DOM，避免把刚点的行拆下来）。
- **断言与负向验证**：`§7.9 提交详情「变化文件树」的折叠/选择/顶部位置在重绘与文件历史往返后保持`
  用**两面判据**（DOM 与状态同时相等，折叠另有计算样式 `display: none`），前置断言宿主真的可滚动
  （`scrollHeight - clientHeight = 167 > 0`，滚动位置 60 非平凡）；负向对照在运行时把 `window.__augitHistoryFiles`
  包一层丢掉 `ui`，此时状态照记而 DOM 不跟随 ⇒ 判据失败。第二条断言
  `§7.9 变化文件树状态按提交归属：改选提交后不残留上一条的折叠/选择/位置` 覆盖状态重置。

## 归属边栏的布局规则与"实时模式不得跑样例交互"（第 255 轮）

§7.9 的归属边栏一条（三列、与正文同行高、纵向同步、横向不动、字号按字宽扩展、作者列内省略、点击用完整哈希）
此前只断言了三列与"行数与真实归属一致"，本轮把余下判据补齐，并挂出并修掉一处真实缺陷。

### 缺陷：视觉稿的**样例**交互在实时外壳里也跑，把样例日志写进实时界面

`mockup.js` 的 `bindBlame()` 为静态视觉稿（`blame.html`）实现了一条"点击归属行 → 在底部日志里定位该提交"的
**样例**链路：它按 `row.dataset.blameCommit`（**短**哈希）在 `gitLog(false)` 生成的**样例**日志里找行、
`replaceChildren(commit)` 只留命中那一行、再把整个底部工具窗换成这份样例日志。实时外壳同样加载 `mockup.js`，
于是这条链路也会跑（实时层自己的 `locateBlameCommit()` 挂在 document 捕获阶段、先跑，但它不阻止后续监听）。

实测（日志已可见 + Blame 文档在前台，点一次归属行）：

| | 提交行数 | 提交图 | 之后点另一行 |
| --- | --- | --- | --- |
| 修前 | **1**（只剩命中那一行） | **0** | 误报「无法定位到该提交」（列表里已没有目标行） |
| 修后 | 3（`live.history.commits` 全部） | 3 | 正常定位，无提示 |

修法：`window.__augitLive.blame` 存在时（实时模式）该样例分支在写完选中反馈后直接返回，
点击语义由实时层的 `locateBlameCommit()` 独占；**保留**纵向同步 `sync()`（真实行为，不属于样例）。

### 归属边栏的布局判据（实测）

夹具用桩旋钮 `__blameLines` 注入 60 行、第 4 行 400 字符、第 6 行超长作者名（默认夹具 3 行短内容会让
"纵向同步""横向不动"**平凡为真**）：

| 判据 | `code-font-size=13` | `code-font-size=20` |
| --- | --- | --- |
| 归属列字号 / 正文实测字号 | 13px / 13px | 20px / 20px |
| 行高（归属列 / 正文） | 22 / 22 | 34 / 34 |
| `--blame-date-width`（实测复算） | 72（72） | 109（109） |
| `--blame-number-width`（实测复算） | 24（24） | 37（37） |
| `--blame-width` = 日期 + 作者样本 + 行号 + 22 | 142 | 205 |
| 正文可滚范围（纵向 / 横向） | 948 / 2538 | 1668 / 4299 |

- **列宽来自实测字宽**：断言把 `--blame-date-width`／`--blame-number-width` 与用 canvas 按**正文实测字体**
  独立复算的数字宽度逐值比较（与 `applyTypography()` 同一口径），因此验的是"公式"而不是"某个恰好成立的常数"。
- **作者列内省略**：真实作者串 `very-long-author-name-that-must-elide` 的 `scrollWidth > clientWidth`
  （`nowrap` + `ellipsis`），且**不**参与列宽 —— 归属栏总宽仍按样本测量，长作者不会把栏撑开。
- **点击用完整哈希**：夹具里短哈希（`bbb2222`）≠ 完整哈希（`full-bbb2222`），日志行的 `data-full-hash`
  只有完整值 ⇒ 点第 6 行必须定位到**该**提交（日志默认选中首行，因此判据能区分"真的按完整哈希定位"与
  "恰好默认选中首行"）。负向验证把 `data-blame-full` 抹空（退回短哈希）：立刻弹「无法定位到该提交」，
  选中行停在默认首行。
- 另两条负向验证：把归属列的 `scrollTop` 钉成 0（等价"没有纵向同步"）⇒ 正文 120 而归属性 0；
  20px 下把两个列宽钉回 13px 的取值 ⇒ 与独立复算不符（72 ≠ 109、24 ≠ 37）。

## 文件历史列表的选择状态（第 256 轮）

规格 §7.9 第二条要求"选择、上下键、分页和返回继续使用同一提交身份"。第 252 轮只断言了四列布局与窄栏滚动，
**这条列表其实没有任何点击处理**：行上的 `.selected` 恒是第一条（渲染里写死 `index === 0`），右侧详情恒取
`commits[0]` —— "换选提交"在界面上做不到，也就没有"提交身份"可沿用。本轮把选择做成状态：

- **状态**：`live.fileHistory.selectedFull`，在 `loadFileHistory()` 里初始化成最新一条（而不是留给渲染兜底）——
  返回上下文、预览取消与区域重绘三条路径因此读到同一个键。渲染 `liveFileHistoryTool()` 按它回填
  `.selected`／`aria-selected`，右侧 `.commit-detail`（新增 `data-live-file-history-detail`）按选中的提交渲染。
- **行标注**：与日志列表同一套 —— `role="option"` + `aria-selected` + 短哈希 `data-history-hash` +
  完整哈希 `data-history-full`（后者同时是选择与将来"打开比较"的身份键）。
- **交互**：单击选中（定点更新，不整页重绘，避免丢焦点）；焦点在行上时 `ArrowUp`/`ArrowDown` 在行间移动
  并把焦点与滚动一起带过去。
- **同一提交重复选择不重建详情**：判据必须是**子节点身份**（`innerHTML` 重写会换掉子节点，宿主节点永远不变），
  同时 `git/file-history`／`git/diff` 调用数不变。
- **负向验证**：让行不再匹配选择器（等价"没有点击处理"）⇒ 点击不换选；抹掉"同一提交不重建"的守卫 ⇒
  子节点被换掉；抹空 `selectedFull` 后重绘 ⇒ 退回第一条（证明渲染读的确实是状态）。
- **仍未实现（登记）**：右侧仍是提交信息详情；规格第五/六条的"预览（按提交与路径复用查询、改选立即取消旧预览、
  隐藏/清除时取消未完成查询）"与"右侧复用完整只读比较视图"待接线 —— 本轮的选择键正是这两条的键位前提。

## 文件历史右侧的只读比较视图与预览生命周期（第 257 轮）

规格 §7.9 第五条要求"预览按提交与路径复用查询、相同快照不重写正文、改选立即取消旧预览、隐藏/清除时取消未完成查询"，
第六条要求"右侧复用**完整只读比较视图**（工具栏九项、文件栏父版本·提交版本·路径、正文与引用比较同构）"。
实时侧此前右侧只有提交信息详情，本轮按"提交 + 路径"装载预览正文：

- **排版共用一份实现**：`mockup.js` 把 `liveDiffView()` 里的正文构造抽成 `diffBodyParts(diff)`（行号、词级高亮、
  单栏模板、差异块摘要），`liveDiffView()` 与新增的 `liveFileHistoryPreviewView()` 都调它 ——
  实测同一份补丁在两处的 `.diff-columns` outerHTML **逐字相同**（827 字节），这就是"与引用比较同构"的可复验形式。
- **状态**：`live.fileHistoryPreview = { path, commit, mode, ignoreWhitespace, key, loading, ready, diff }`，
  请求键 = `路径|提交|忽略空白`；**补丁缓存不含显示模式** ⇒ 单双栏切换只重新排版、不查询 Git。
- **装载 / 复用 / 取消**：同一把钥匙且已就绪时直接返回（不重查、不重写正文）；命中补丁缓存时只重排；
  每次装载推进令牌，晚到响应一律作废；`force: true`（忽略空白切换）按规格"真实差异选项"重查一次。
- **DOM 同步**：只替换右侧面板（定点），不整区重绘 —— 文件历史列表的方向键导航依赖行上的焦点。
  首次进入时右侧还是提交信息面板，这里走一次**区域重绘 + `rebindAfterRender()`**把面板换成预览
  （区域重绘会换掉整个底部工具窗，工具条上"清除路径筛选"等既有标签都挂在被替换的节点上）。
- **导航作用域**：`diffChangeBlocks(scope)`／`moveDiffChange(direction, scope)` 让同一套箭头服务两份正文；
  作用域非空时跳过只属于工作区 Diff 的两段式边界提示与"再按进入相邻文件"。
- **释放**：清除文件历史与折叠底部工具窗都会 `releaseFileHistoryPreview()`（推进令牌 + 清空请求/补丁 + 置空状态）；
  `ensureFileHistoryPreview()` 在每次渲染后按当前选择补查（首次进入、重新展开）。
- **仍未做**：预览工具条上的「上一处/下一处差异」还没有断言；"隐藏右侧详情"（工具条那个 `eye`）还没有标签与绑定；
  "失败后再次选择同一提交可以重试"、"相同快照不改变阅读位置"未断言。

## 预览工具条的差异块导航与右侧详情的显隐（第 258 轮）

第 257 轮接上右侧只读比较视图后，还剩两件事，本轮做完：

- **差异块导航**：`diffChangeBlocks(scope)`／`moveDiffChange(direction, scope)` 让同一套「上一处/下一处差异」
  服务两份正文。作用域非空（预览）时跳过只属于工作区 Diff 的两段式边界提示与"再按进入相邻文件" ——
  实测在尾块同方向再按不出提示、不切文件、不重查，`data-diff-index` 停在最后一块。
- **「显示/隐藏提交详情」**：文件历史工具条的 `eye` 此前没有标签、也没有绑定，现在与日志详情同一套语义
  （标签 `显示提交详情`/`隐藏提交详情` + `aria-pressed`；状态 `live.fileHistoryDetailsHidden`，
  `applyFileHistoryDetailsState()` 在每次渲染后落地；隐藏时容器收成单列 ⇒ 列表占满整宽）。
  - **隐藏**：`cancelFileHistoryPreviewRender()` 推进令牌（在途响应作废）并丢弃**未就绪**的那一份；
    已完成的比较原样保留 ⇒ 正文、显示模式、正文节点身份都不变。
  - **重显**：`ensureFileHistoryPreview()` 按当前选择补查；显示模式与忽略空白是**会话选项**
    （`fileHistoryPreviewOptions`），未完成的那份被丢弃后补查仍沿用它们（实测键仍是 `…|ws`）。
  - **仍未完成**：隐藏用 `display:none`，其子树重新显示后 `scrollTop` 归零 ⇒ **阅读位置**要保存/恢复；
    右侧比较视图的 Tab 顺序也没有断言。

## 预览正文的阅读位置与工具窗口内的 Tab 顺序（第 259 轮）

- **阅读位置**：隐藏用的是 `display:none`，而**隐藏期间的任何区域重绘都会换掉正文节点** ⇒ 重新显示时
  `scrollTop` 归零（实测 300 → 0）。因此显式保存/恢复：`fileHistoryPreviewScroller()` 按
  `diffScrollSync` 的同一口径找到真正滚动的祖先，隐藏前 `rememberFileHistoryPreviewScroll()` 记下
  `{内容键, scrollTop}`，重显、正文重建与每次渲染后 `restoreFileHistoryPreviewScroll()` 只在**同一内容键**上恢复
  （换提交/换选项后不沿用旧位置）；`releaseFileHistoryPreview()` 一并清掉记忆值。
- **Tab 顺序**：从列表工具条的清除入口起连按 Tab，前 12 个落点全在 `.history-tool-content` 内
  （列表工具条四个 → 详情显隐 → 比较工具条六项 → 两条提交行），全程不进入上方文档正文（`.editor-content`）。
- **仍未完成**：比较区正文**不是 Tab 停靠点**（`.diff-layout` 无 `tabindex`）—— 规格第七条说"经过工具按钮与正文"、
  第十九条要求"保留一个正文焦点位置"，两处都要改，且需与 §7.7 工作区 Diff 的既有 Tab 序列断言一起动。
