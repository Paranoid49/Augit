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
