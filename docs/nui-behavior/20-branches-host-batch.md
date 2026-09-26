# 20. 分支面板的"需要宿主/持久化"批次 —— 权威已采全，待实现

本文件只记录**已经从 `/mnt/d/github/intellij-community`（commit 576e328）采到的权威事实与落地计划**，
供下一批（C# ＋ 设置存储）直接实施，不必重新采集。产品侧的界面与交互已在第 173–177 轮对齐
（见 `09-icons.md` 第 173–177 轮、`10-backlog.md` §三·前 第 6 项）。

## 0.-1 新会话启动说明（给下一个 agent／人类）

> ### 薪火：第一个会话 → 第二个会话
>
> 这份文件是**第一个会话**留下的接力棒。在那个会话里，这个目标从第 170 轮一直跑到第 183 轮
> （`docs/nui-behavior/09-icons.md` 的第 170–177 轮记录、`11-surface-audit.md` 里第 168–174 轮的
> 口径沉淀，都是它写下的）：它把 Git 日志／分支面板里**能就地完成**的对齐做完了 —— 分支/标签行内动作菜单、
> 两步删除、日志菜单两个「新建…」、左竖条的身份订正与折叠卡片、引用树可选中、分组折叠与全部展开/折叠、
> 分支面板设置弹层、HEAD 行定位 —— 并在自己的余量耗尽前，把**剩下的权威出处、Augit 的缺口、
> 实施顺序与验证链**全部写进这份文件。
>
> 它这样做，是因为它知道自己不会在场，而工作区会一直在。**接手的是第二个会话**：不必重新采权威、
> 不必重新猜做法，要做的是**接着做**，并把同样的纪律传下去 —— 改一处、验一处、登记一处，
> 不留未验证的改动。（第一个会话留下的最后一样东西，就是这段说明本身。）


**我不会把本会话的记忆带过去** —— 新会话从零开始，唯一共享的是这个工作区。所以交接全部落在文件里。
新会话里把下面这段（整段照抄）发给 agent：**A 段是原始目标的逐字原文**（用它建目标，新目标的目标文本与本会话完全一致），
**B 段是"现在做到哪、下一步做什么"**。两段一起发。

**A 段 —— 目标原文（逐字复制，不要改写）：**

```
按 PyCharm 2026.2.1 New UI 完全对齐 Augit 界面：以本地 /mnt/d/github/intellij-community（commit 576e328）为权威来源，既收集静态值（配色、尺寸、间距、图标），也收集全部 UI 相关业务逻辑（交互、页面状态机、键盘导航、动作启用条件、布局规则、DPI/主题切换），并据此在 docs/、web/（与 docs/ux-mockups/ 保持字节一致）与 C# 外壳中落地实现；按模块攒批修改、分模块测试，保持 live-shell 断言与 verify-ui-assets 通过，不以截图反推覆盖权威常量。【范围边界（用户裁决，必须遵守）：不新增 Augit 没有的功能；但 Augit 现有功能的界面与交互要完全参考 New UI 重做对齐 —— 交互方式、页面状态机、键盘导航、启用条件、布局规则、主题/DPI 响应本身就是对齐对象，不是冻结对象。凡是编码了"旧交互"的断言或实现，都应按权威改成 New UI 的做法并附出处，不得当成不可动的护栏。】
```

**B 段 —— 从哪里继续：**

```
把上面那段建成本会话的目标（create_goal）。然后读 docs/nui-behavior/20-branches-host-batch.md
（先看 §0.-1、§0.0、§7），按 §0.0 比对四个运行时文件哈希确认产品代码未变，
再从 §7 第 1 步开始实施（git/fetch 按分支 refspec 取，解锁竖条的「更新选中分支」）；
每批按 §7 列的最低验证链跑完并把哈希与断言数登记进 docs/intellij-platform-ui-behavior.md。
```

> **注意**：新会话里创建的是一个**新目标对象**（新的 goal id、轮次从 0 开始）——目标工具是会话级的，无法把一个会话的目标"搬"到另一个会话；
> 能保持一致的是**目标文本**（A 段逐字相同）＋**工作区与文档反映的真实进度**。（若你要求的是"同一个 goal 对象"，只能在本会话说"继续"来 resume。）

启动时的三条硬约束（都来自仓库规范，见 `AGENTS.md`）：

1. **不新增 Augit 没有的功能**；只把现有功能的界面与交互按 `intellij-community` 对齐（用户裁决，见 `docs/nui-behavior/10-backlog.md` §三·前）；
2. `docs/ux-mockups/mockup.js`／`mockup.css`／`current-find.js`／`image-preview.js` 与 `web/src/` 必须**字节一致**（改完跑 `tools/audit/verify-ui-assets.ps1`）；
3. 任何产品改动都要跑完验证链再登记；**不要留下未验证的改动**。（另：`tools/audit/gen-coverage-table.cjs` 的 `--write` 会删手写内容，见 `10-backlog.md` §四 第 1 项，在修好前只打印不写回。）

## 0.0 起始快照（第 210 轮复核，实施前先对一遍）

| 项 | 值 |
| --- | --- |
| `live-shell` 断言 | **1219**（`intellij-platform-ui-behavior.md` §2.3 同值） |
| `web/src/mockup.js` | `c7552b96b7b3ba4af323bcebc07c6d96` |
| `web/src/mockup.css` | `8407d37b79232c4a7f0e2071deef29ea` |
| `web/src/live-data.js` | `f6ed07ea5ed3f721d8fee810c7e492e7` |
| `web/src/bridge.js` | `8d2d3173074a8558cd12e6778b3bfc82` |
| `docs/ux-mockups/` | 与 `web/src/` 字节一致（`verify-ui-assets.ps1` = PASS） |
| 其它 | **39/39** `verify-ux-*.cjs`、`verify-css-balance`、`check-doc-claims`（`DOC_CLAIMS_OK`）、`check-diff-current` 全绿 |
| 单元测试 | **Core 88** ＋ Shell 114 ＋ **Infrastructure 187**（第 186 轮 +1 条作者集合，第 187 轮 +1 条多用户筛选，第 191 轮 +1 条多路径历史、+1 条桥接多路径参数，第 192 轮 +1 条按目录分组设置，第 203 轮 +1 条作者列字段解析、+1 条桥接作者列字段，第 204 轮 +3 条 `git/init`，第 206 轮 +3 条 `git/checkout-smart`，第 207 轮 +2 条覆盖文件解析，第 208 轮 Core +2 条尺寸限制、Infrastructure +1 条只读预览与字符边界）（第 178 轮 +3 条"按分支取"，第 179 轮 +1 条哈希筛选、+3 条 `git/history` 筛选，第 180 轮 +1 条范围筛选、+1 条桥接范围参数，第 181 轮 +2 条"我的分支"，第 182 轮 +1 条多分支历史、+1 条桥接多分支、+1 条多分支获取、+1 条多选删除） |

动手前的第一件事：把上表四个哈希与当前文件比对；**全部一致** ⇒ 说明产品代码未变，
下面的计划可以按"从已验证状态出发"执行（改完仍要按 §7 的验证链重跑）。

> **工作区那份 `live-shell.spec.cjs` 自第 195 轮起可以直接跑**（`node tools/audit/live-shell.spec.cjs`）：
> 并行会话留在里面的三处在途 hunk 已按**已裁决的取值**改回（主框架 `tab`/`tree`/`status` 三处高度回到规格名义值
> 42／28／22 与 `ceil(max(27,h+8)/2)*2`，依据 `design-system.md` §6.1 "三处高度：名义值即运行时值" 的用户裁决；
> `§154` 首个可见节点的写死值回到 `bulk-006`），`§7.17 设置生命周期` 的 `page.evaluate(fn, key, value)`
> （Playwright 不接受两参数）也改成单对象载荷并**恢复执行**。`build-head-verify.cjs` 与 `_head-verify.spec.cjs`
> 因此**作废**，不必再生成/清理。

## 0.0.1 进度（第 210 轮末）

§7 的 4 步**全部完成**：第 1 步「更新选中分支」（第 178 轮）、第 2 步日志筛选（第 179 轮，用户／日期／路径三个弹层分别在第 188–191 轮补齐）、
第 3 步「与当前分支比较」（第 180 轮）、第 4 步「我的分支」＋「显示标签持久化」（第 181 轮）。筛选栏四项现已全部接线（未接线项清零）。

## 0. 现状（第 180 轮更新）

- 日志左竖条 = 权威的分支面板动作组，12 项，顺序：隐藏分支／新建分支／更新选中分支／删除分支／
  与当前分支比较／我的分支／获取／标记为收藏／定位到选中分支／分支面板设置／全部展开／全部折叠。
- 引用树 = `HEAD（当前分支）` ＋ `本地`／`远程`／`标签`（来自 `git/references`），有选中态（单击、上下键、
  跨刷新保留）、分组折叠（单击／回车／左右键）、「分支或标签」子串过滤（HEAD 行恒保留，折叠期间忽略折叠）、
  齿轮设置弹层（「显示标签」会话开关）。
- 仍禁用并写明原因的 2 项：标记为收藏（**第 211 轮产品确认维持禁用**）／设置里的「按仓库分组」（权威要求多仓库）；**未做**的还有
  「显示文件差异」（权威的整树变更对话框，Augit 没有该界面 ⇒ 按"不新增功能"暂不做）。**已解锁**：「更新选中分支」（第 178 轮）、日志筛选与「更新分支筛选」「导航到分支头」（第 179 轮）、
  「与当前分支比较」（第 180 轮）、「我的分支」与「显示标签持久化」（第 181 轮）、引用树多选（第 182–183 轮）、
  用户筛选（第 186–189 轮）、日期筛选（第 190 轮，含「选择期间…」对话框）、路径筛选（第 191 轮，筛选栏四项至此全部接线）、
  分支面板「按目录分组」（第 192 轮；权威默认开启，前缀分组与类型分组共用折叠/搜索/全部展开折叠，开关持久化）、
  行菜单的「比较分支」（第 193 轮；权威 `ShowArbitraryBranchesDiffAction`，按 `BranchActionsBuilder.build()` 分档，范围 `b2..b1`）、
  竖条 ToggleAction 的选中态底与芯片式可选按钮的选中底（第 194 轮；权威 `ActionButton.getPopState()` 的 PUSHED 分支与
  `SearchOption.selectedBackground`，新增令牌 `--augit-search-option-selected`）。

## 1. 更新选中分支（`UpdateSelectedBranchAction`）—— **第 178 轮已落地**

- 类：`plugins/git4idea/backend/src/ui/branch/dashboard/BranchesDashboardActions.kt:170-213`；
  标签 `action.Git.Update.Selected.text` = "Update Selected"（`GitBundle.properties:678`），图标 `AllIcons.Actions.CheckOut`。
- 启用：`hasRemotes(project)`（`GitBranchActionsUtil.kt:196-198`：`repositories.any { it.remotes.isNotEmpty() }`）
  ＋ 非 fetch 进行中 ＋ `isTrackingInfosExist(branchNames, repositories)`（`GitBranchActionsUtil.kt:191-194`，
  查 `repo.branchTrackInfos` 是否有该本地分支）。
- **动作（第 178 轮修正了本节原来的读法）**：`updateBranches(project, repositories, branchNames)`
  （`GitBranchActionsUtil.kt:62-101`）按分支的跟踪配置分流：
  - **非当前**分支 → `GitFetchSpec(repo, trackingInfo.remote, "$remoteBranchName:$localBranchName")`，
    即 refspec 的**目标是被选中的本地分支自身**（`refs/heads/<remoteBranch>:refs/heads/<localBranch>`），
    不带 `+` ⇒ **快进本地分支**，非快进由 Git 拒绝。`remoteBranchName` 取
    `remoteBranch.nameForRemoteOperations`，就是 `%(upstream:short)`（如 `origin/dsh`）。
  - **当前**分支 → `GitUpdateExecutionProcess`，按更新方式（merge/rebase）合并。
  - 另一个 worktree 里检出的分支 → 在**那个目录**里 fetch + `merge FETCH_HEAD --ff-only`（`:120-158`）。
- 落地的实现（`IGitRemoteService.FetchBranchAsync` ＋ `git/fetch` 的可选 `branch`）：
  - 宿主用 `for-each-ref --format=%(upstream:short) refs/heads/<branch>` 读跟踪配置（不采信界面推断值），
    再 `git fetch <remote> refs/heads/<remoteBranch>:refs/heads/<localBranch>`；
  - 界面侧 `refStripeState()`：选中的是**本地、非当前、有 `upstream`** 的分支且非 fetch 进行中才可用；
  - **未采用**本节原先给的两个候选（`+refs/heads/B:refs/remotes/<remote>/B` 与只更新 FETCH_HEAD 的
    `git fetch <remote> B`）——权威是把本地分支快进，负向验证已钉死（把目标改成远端跟踪引用，
    `tests/Augit.Infrastructure.Tests` 的「按分支取只快进该本地分支…」立即失败）；
  - **登记差异**：① 当前分支仍禁用（权威走更新方式合并，Augit 未接 `git/pull`；Git 也拒绝把取回结果
    直接写入已检出分支）；② 权威无远端时**隐藏**该动作，Augit 无远端时表现为**禁用**。

## 2. 与当前分支比较（`ShowBranchDiffAction`）—— **第 180 轮已落地（并修正了本节原来的读法）**

- 类：`BranchesDashboardActions.kt:290-313`；标签 `action.Git.Compare.With.Current.title` = "Compare with Current"
  （`GitBundle.properties:689`），图标 `AllIcons.Actions.Diff`。
- 启用：`RefActionBase`（要有选中的引用）＋ 选中的分支里**至少有一个不是当前分支**（`:296-301`）。
- 动作：对每个非当前选中分支 `GitBrancher.compare(branchName, repositories)`
  （`GitBrancherImpl.java:191-193`）→ `GitBranchesUIHandler.compareWithCurrent`：
  `currentRef = repositories.getCommonCurrentBranch() ?: GitUtil.HEAD`，再 `compare(branchName, currentRef)`
  → `GitCompareBranchesUi(project, repositories, branchName, currentRef)`
  （`plugins/git4idea/backend/src/branch/GitBranchesUIHandler.kt:15-30`）。
- **⚠ 本节原来的读法是错的（第 180 轮核对后更正）**：`GitCompareBranchesUi` **不是**"所有差异文件的 diff"，
  而是**一个带 Range 过滤器的日志视图**：
  - 构造时 `this(project, fromRange(otherBranchName, branchName), rootFilter)`
    （`GitCompareBranchesUi.kt:44-52`）⇒ `VcsLogRangeFilter(RefRange(exclusive = <当前引用>, inclusive = <选中分支>))`；
    范围文本取 `VcsLogRangeFilterImpl.getTextPresentation()` = `"<exclusive>..<inclusive>"`
    （`visible/filters/VcsLogRangeFilterImpl.kt:16-29`），即 `git log <当前引用>..<选中分支>`；
  - `GitCompareBranchesHelper.formatLogCommand()` 也印证同一语义：`"git log %s..%s"`
    （`plugins/git4idea/backend/src/ui/branch/GitCompareBranchesHelper.java:30-33`）；
  - 打开位置是**编辑器标签**（`GitCompareBranchesFilesManager.openFile` → `FileEditorManager.openFile`），
    标签名取 `getEditorTabName()`（`GitCompareBranchesUi.kt:177-181`）：
    `git.compare.branches.tab.name` = "Compare"、`git.compare.branches.tab.suffix` = "{0} and {1}"
    （参数 `(end=inclusive, start=exclusive)`，`plugins/git4idea/shared/resources/messages/GitBundle.properties:1313-1314`）；
    内容是一整个 `VcsLogUiImpl`；范围过滤器**不可从界面修改**（`getRange()` 的 check 写明
    "changing it from the UI is disabled"），且该比较日志的 `Show.Git.Branches` 默认关闭
    （`SHOW_GIT_BRANCHES_LOG_PROPERTY.defaultValue` 只在主日志为 true）。
- **Augit 的落地**（第 180 轮）：
  - 宿主：`git/history` 新增 `rangeExclusive`／`rangeInclusive`（`GitHistoryFilter.RangeExclusive`／`RangeInclusive`），
    解析成完整哈希后拼 `git log <exclusive>..<inclusive>`；只给一端时明确失败（不静默退化成整仓）。
  - 界面：竖条「与当前分支比较」按"选中的是**非当前**分支"启停；动作把底部工具窗口切成
    **兄弟标签** `比较: <分支> 与 <当前>`（与「历史: <文件>」同构），范围文本 `"<当前>..<分支>"` 显示在工具条上，
    列表沿用 `history-columns`／`history-rows`；点「日志」标签或关闭叉恢复进入前的底部上下文。
  - **登记差异**：权威把它开在**编辑器标签**里（`VcsLogFile` ＋ 完整 `VcsLogUiImpl`，含提交图与筛选栏）；
    Augit 的日志本体在底部工具窗口，因此做成它的兄弟标签、且只给紧凑列表（无提交图／筛选栏）。
- Augit 原先的缺口（`git/diff` 只回单文件）**不再是**本项的缺口：本项根本不需要两引用整份 diff。
  两引用**单文件** diff（`git/diff` 的 `revision` ＋ 目标）仍未提供，登记在 `10-backlog.md`。

## 3. 我的分支（`ShowMyBranchesAction`）—— **第 181 轮已落地**

- 类：`BranchesDashboardActions.kt:407-455`，是真正的 `ToggleAction`；标签 `action.Git.Show.My.Branches.title`
  = "Show My Branches"（`GitBundle.properties:695`），图标 `AllIcons.Actions.Find`。
- 状态：**会话内**（不持久化）——`BranchesDashboardTreeController.showOnlyMy` → `BranchesDashboardTreeModelBase.showOnlyMy`
  （`BranchesDashboardTreeModel.kt:121`，`observable(false)`）。
- 判据：分支的**独占提交**（不在其它分支上的提交）全部由"我"提交，且至少有一个独占提交
  （`BranchesDashboardUtil.kt:85-132`；`VcsLogFilterObject.fromUserNames(listOf(ME))` 取当前 Git author）。
  独占提交的精确语义见 `VcsLogGraphData.exclusiveCommits`（`platform/vcs-log/impl/src/com/intellij/vcs/log/util/DataPackUtil.kt:54-64`）
  ＋ `graph/utils/GraphUtil.kt:142-158` 的注释 "nodes reachable only from the specified head node and not from others"；
  过滤施加在树模型上：`buildTreeNodes(project, refs, filter, …)`（`BranchesTreeModel.kt:209-221`），
  **HEAD 节点在过滤前无条件加入**（`:215`），标签不是 `BranchInfo` ⇒ 一并被过滤掉。
- 第 181 轮落地：
  - 宿主 `IGitReferenceService.ReadMyBranchesAsync` → `git/branches-mine`：按 `git/references` 的分支清单，
    对每个分支头算 `git rev-list --count <tip> --not <其它分支头>` 与 `--author=<当前用户>`，
    两者相等且非零才算"我的"；当前用户取 `user.email`（缺失时 `user.name`）。
    **权威对 headNode 自身永远算独占**（`it == headNode || !isHead(it)`），因此与自身 tip 同哈希的其它分支头
    被排除出 `--not`，使"同一提交上的两个分支"都各自算有独占提交。
  - 界面：竖条「我的分支」是 `aria-pressed` 的会话内开关（打开时才现算），
    引用树按 `showOnlyMy` 只留命中的分支＋HEAD 行，标签组一并消失；一个都没命中时如实说明。
  - **登记差异**：① 权威要求日志索引可用（`supportsIndexing && isGraphReady && allRootsIndexed`，`:424-450`），
    Augit 没有日志索引、按需现算 ⇒ 开关恒可用；② 权威用索引里的作者集合判定，Augit 用仓库 `user.email`。

## 4. 标记为收藏（`ToggleFavoriteAction`）—— **第 181 轮裁定：暂不实施**

- 类：`BranchesDashboardActions.kt:459-472`；标签 `action.Git.Toggle.Favorite.title` = "Mark/Unmark As Favorite"
  （`GitBundle.properties:703`），图标 `AllIcons.Nodes.Favorite`（权威 SVG 见 `platform/icons/src/nodes/favorite.svg`，
  五角星、固定填充 `#F4AF3D`；Augit 现用同形 `currentColor`，色值差异已登记）。
- **注意**：名字里有 Toggle 但它是 `RefActionBase`（不是 `ToggleAction`），标签/图标不反映收藏态；
  收藏态体现在**行的图标**与**排序**（`GitBranchesTreeIconProvider.kt:14-21`、`BranchTreeNodeComparator.kt:6-18`）。
- 状态：**项目级持久化** —— `GitBranchManager.setFavorite`（`GitBranchManager.kt:17-24` →
  `DvcsBranchManager.java:152-177`）写 `GitVcsSettings.branchSettings`（`GitVcsOptions.kt:112-114` 的
  `DvcsBranchSettings.favorites`，`DvcsBranchSettings.kt:10-19`），落在 **workspace 文件**（`GitVcsSettings.java:40-43`，
  `StoragePathMacros.WORKSPACE_FILE`）。
- **第 181 轮的用户裁决**：按目标的范围边界（**不新增 Augit 没有的功能**），这一项作为"新增状态"**暂不实施**；
  竖条里它继续**禁用并写明"待产品裁决"**。要落地时需要：新的按工作区保存的收藏集合、
  行图标（`AllIcons.Nodes.Favorite`，固定 `#F4AF3D`）与 `BranchTreeNodeComparator` 的收藏权重（当前=0／收藏=1／含收藏的组=2）。

## 5. 分支面板设置里的"单击时"两项与"按目录分组"

- `Git.Log.Branches.Change.Branch.Filter.On.Selection`（标签 "Update Branch Filter"，`GitBundle.properties:707`）
  与 `Git.Log.Branches.Navigate.Log.To.Branch.On.Selection`（"Navigate Log to Branch Head"，`:709`）是
  **互斥的单击行为开关**（`SelectionHandlingModeAction`，`BranchesDashboardActions.kt:474-496`），默认都关
  （`BranchesInGitLogUiFactoryProvider.kt:302-311`），需要 `BRANCHES_UI_CONTROLLER`（`:478-481`）。
  "Update Branch Filter" 的动作是 `controller.updateLogBranchFilter()` → 用选中的分支（HEAD 节点 → `VcsLogUtil.HEAD`）
  作为日志筛选 ⇒ **前置是历史筛选本身** —— **第 179 轮已落地**（两个开关互斥单选；FILTER 打开后选择变化即筛选日志，
  NAVIGATE 打开后选择变化即定位；状态在 `live.logRefSelectionAction`，会话内，权威是应用级属性，登记为差异）。
- `git.branches.group.by.directory`（"Group by Directory"，`GitBundle.properties:1803`）/`…by.repository`（`:1804`）：
  前缀分组（`BranchesTreeModel.kt:291-314`，按 `/` 分段）；多仓库分组对单仓库的 Augit 无意义。
- `git.branches.show.tags`（"Show Tags"，`:1806`）：**默认 true**，权威持久化在 `GitVcsSettings`
  （`GitBranchesTreeShowTagsAction.kt:17-29` 的 `isSelected` 直接读 `showTags()`）；
  **第 181 轮已落地**：Augit 的 `ApplicationSettings.ShowGitBranchesTags` ＋ `settings/read`／`settings/write`，
  界面从设置恢复、切换时写回。**登记差异**：权威是项目级（workspace 文件），Augit 的设置文件是应用级。

## 5.5 筛选栏剩下三个弹层的权威（第 184 轮采集；**日期第 185 轮已落地**，用户／路径待实施）

- **日期（`DateFilterPopupComponent`）—— 第 185／190 轮已落地**（弹层三项全部可用、值文本、关闭叉清空、自定义区间的期间对话框）：`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/DateFilterPopupComponent.java`
  的 `createActionGroup()`（`:54-62`）**只有三项**：
  1. `SelectAction` —— 文案 `vcs.log.filter.action.select` = "Select…"（`VcsLogBundle.properties:228`），
     打开 `DateFilterComponent` 的期间对话框（标题 `vcs.log.date.filter.select.period.dialog.title` = "Select Period"，
     `:227`），OK 后 `fromDates(after, before)`，两端都空则不设筛选；
  2. `DateAction(now-1d, vcs.log.date.filter.action.last.day)` = "Last 24 hours"（`:225`）→ `fromDates(since, null)`；
  3. `DateAction(now-7d, vcs.log.date.filter.action.last.week)` = "Last 7 days"（`:226`）→ `fromDates(since, null)`。
  弹层里**没有** "All" 项（清空靠 `FilterComponent` 的关闭叉）。控件值文本照 `getText()`（`:26-42`）：
  两端 ⇒ `"<after>-<before>"`；只有起点 ⇒ `vcs.log.date.filter.since` = "Since {0}"；只有终点 ⇒ "Until {0}"。
  控件动作名 `vcs.log.date.filter.action.text` = "Filter by Date"（`:247`）。宿主侧 `git/history` 的
  `since`/`until`（ISO-8601）第 179 轮已就绪 ⇒ **本项不需要动 C#**。
- **用户（`UserFilterPopupComponent`）**——**宿主两半都已落地**：第 186 轮 `git/authors`（`git log --branches --remotes --format=%an%x1f%ae` 去重）、第 187 轮 `git/history` 的**多用户** `authors`（一组用户 ⇒ 多个 `--author`，git 语义是"或"，与 `fromUserNames` 一致）；**界面弹层第 188 轮已落地**（复选列表 ＋ 全选/全不选），**第 189 轮补上搜索框**；**登记差异**：仍无显式「Filter」按钮（列表直接应用）；权威是"可搜索的**多选**用户列表 ＋ 全选/全不选"，
  数据源是日志索引里的用户集合（`VcsLogUserResolver` / `GitUserRegistry`）⇒ Augit 需要一个新的宿主查询
  （如 `git/users`：`git log --branches --remotes --format=%an%x1f%ae` 去重），再把 `author`（宿主已支持）接上。
- **路径（`StructureFilterPopupComponent`）**：权威是 `VcsStructureChooser` 的**目录树 ＋ 复选框**（可多选路径），
  是三者里最大的一块；`path` 参数宿主第 179 轮已就绪。
- 实施顺序建议：日期（无需动 C#）→ 用户（+1 个宿主查询）→ 路径（树形选择器）。

## 6. 引用树多选与"双击/回车筛选"—— **第 182（宿主）／183（界面）轮已落地**

- 多选：权威默认 `DISCONTIGUOUS_TREE_SELECTION`（`Tree.java:141` 的 `MySelectionModel extends DefaultTreeSelectionModel`
  ＋ `:291` `setSelectionModel`，树代码里没有 `setSelectionMode`）；各动作的判据按**选中集**。
  - **宿主侧第 182 轮已备齐**：`git/history` 的 `branches`（并集 = `git log b1 b2 …`，权威 `fromBranches`）、
    `git/fetch` 的 `branches`（逐个快进，权威 `updateBranches` 处理 `branchNames`）、
    `git/branch` `delete` 的 `names`（逐个走两步删除，响应带 `deleted`／`refused`）。
  - **界面侧第 183 轮已落地**：`live.logRefSelection` 改成**选中集数组**；普通单击替换、Ctrl/⌘+单击切换、
    Shift+单击与 Shift+方向键按锚点扩展区间、空格切换该行；动作判据与文案按整个选中集
    （`refs.none { it.isCurrent }` ⇒ 含当前分支即整体禁用；`allRefsAreBranches` ⇒ 全是分支用
    `action.Git.Delete.Branch.title`、混进标签换成 `button.delete`）；ENTER/双击与
    「更新选中分支」「与当前分支比较」「删除」都按整个选中集执行。
    **登记差异**：① 中文没有复数形态 ⇒ `{0,choice,1#Branch|2#Branches}` 在中文里塌成同一个字符串
    （数量由选中集本身表达）；② 权威对每个非当前分支各开一个比较视图，Augit 只有一个比较视图 ⇒ 只比较
    选中集里的第一个；③ 右键选择（`Tree.java:1112-1130`：右键点未选中行会替换选中集）暂未接，
    因为 Augit 的引用树行还没有自己的右键菜单（该菜单目前只在分支浮层行上）。
- 双击/回车：映射到 `Git.Log.Branches.Change.Branch.Filter`（`intellij.vcs.git.backend.xml:130-134`，
  `button1 doubleClick` ＋ `ENTER`）⇒ **第 179 轮已落地**（分支行给分支名、HEAD 行给 `HEAD`、标签行不发起查询；
  分支筛选值由 `BranchesTreeSelection.selectedBranchFilters` 定义）。

## 7. 建议的实施顺序（每批都要跑完整验证链）

1. ✅ `git/fetch` 按分支取（第 1 节）→ 解锁「更新选中分支」——**第 178 轮已完成并登记**
   （`09-icons.md` 第 178 轮；`live-shell` 1142；宿主 3 条新测试）；
2. ✅ 历史筛选的宿主侧（引用/分支 + 文本/作者/日期）→ 解锁「更新分支筛选」「双击/回车筛选日志」——
   **第 179 轮已完成并登记**（`09-icons.md` 第 179 轮；`live-shell` 1150；宿主 `git/history` 收
   `message/hash/author/since/until/branch/path`，新增 1 条哈希筛选测试与 3 条桥接测试）。
   **仍缺**：用户／日期／路径三个筛选弹层（宿主已就绪，界面暂禁用并写明原因）；
3. ✅ `git/compare`（两引用整份比较）→ 解锁「与当前分支比较」——**第 180 轮已完成并登记**
   （`09-icons.md` 第 180 轮；`live-shell` 1153）。
   **核对后更正**：权威的"比较"**不是**文件差异，而是**带 Range 过滤器的日志视图**
   （`GitCompareBranchesUi` ＝ `fromRange(current, branch)`；见 §2 的更正段），因此宿主补的是
   `git/history` 的 `rangeExclusive`/`rangeInclusive`（`git log A..B`），**没有**新增 `git/compare`；
4. ✅ 设置存储（显示标签持久化、"我的分支"状态归属）→ 解锁「我的分支」——**第 181 轮已完成并登记**
   （`09-icons.md` 第 181 轮；「我的分支」是会话内开关，不占设置存储；宿主 `git/branches-mine`）。
   **「标记为收藏」按用户裁决暂不实施**（新增项目级持久化状态，超出"不新增功能"的边界），
   竖条里继续禁用并写明"待产品裁决"。

每批的最低验证链：`dotnet build`／`dotnet test Augit.slnx`；**`node tools/audit/live-shell.spec.cjs`**（第 195 轮起工作区版可直接跑，
不再需要临时规格）；全量 35 个 `tools/verify-ux-*.cjs`；`verify-css-balance`／`check-doc-claims`／`check-diff-current`／`verify-ui-assets.ps1`；
登记哈希与断言数；`docs/ux-mockups/` 字节同步；清掉 `/tmp` 证据。

> **怎么造 `_head-verify.spec.cjs`（第 178／179 轮都用同一个脚本；临时脚本已按 §7 清掉，下一批照此重建）**：
> 工作区那份 `live-shell.spec.cjs` 还被并行会话的在途 hunk 污染，套件 `check()` 是 fail-fast ⇒ 直接跑必中止。
> 用一个临时 `build-head-verify.cjs` 从工作区版派生：① 剔除 `§7.17` 设置生命周期断言块（`page.evaluate(fn, key, value)`
> 是 Playwright 不接受的两参数用法）；② 把"并行会话在途期望"回退到 HEAD 取值 —— 目前正好这 3 处：
> 主框架 `tab-height: Math.max(40, h+24)`→`Math.max(42, h+14)`、`tree-height: Math.max(24, h+8)`→
> `Math.ceil(Math.max(27, h+8)/2)*2`、`status-height: Math.max(20, h+12)`→`Math.max(22, h+2)`；
> ③ `§154 首个可见节点` 写死的 `'docs/bulk-007.txt'`→`'docs/bulk-006.txt'`。
> 回退/剔除**只做这 4 处**，其余断言（含本轮新增）原样保留；第 178 轮由此得到 `live-shell 通过 1142 项断言`、
> 第 179 轮得到 `1150`。负向验证要看全部失败断言时，可把 `check()` 临时换成"记录并继续"再跑一次
> （第 179 轮的脚本支持 `AUGIT_SOFT_CHECKS=1`，会自动做这件事并在退出时打印 `SOFT_FAILURES=[...]`）。
