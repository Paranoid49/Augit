# IntelliJ Platform New UI 行为与页面逻辑（索引）

本文是 [New UI 数值参考](intellij-platform-ui-reference.md) 的配套索引。数值参考管"静态取值"，本文与 `nui-behavior/` 下的 7 份分册管"**交互与页面逻辑**"。

- 分册是**取证记录**，不是规格。界面规则以 [design-system.md](design-system.md) 与 [ux-spec.md](ux-spec.md) 为准，产品行为以 [product-spec.md](product-spec.md) 为准。
- 运行时令牌的唯一权威仍是 `web/src/mockup.css`（design-system.md §11.1）。
- 冲突处理原则（用户已确认）：**完全按 New UI 对齐**；只有当产品规格明确"不做"某能力时，才以产品规格为准，并在本文登记为**有意的产品偏离**。

## 1. 分册索引

| 分册 | 覆盖 | 行数 |
| --- | --- | --- |
| [01-tool-window.md](nui-behavior/01-tool-window.md) | 布局模型、ultrawide、stripe 与按钮、显示/隐藏/激活状态机、分隔条权重、标题与图标 | 594 |
| [02-tree-list.md](nui-behavior/02-tree-list.md) | 选中与焦点状态机、键盘导航、speed search、展开折叠、行高与缩进、复选框、滚动 | 410 |
| [03-editor-tabs.md](nui-behavior/03-editor-tabs.md) | 标签几何与两套渲染、选中/悬停、关闭与拖动、溢出、预览标签、只读正文、行号、空白符 | 609 |
| [04-actions-icons.md](nui-behavior/04-actions-icons.md) | 动作可用性模型、禁用 vs 隐藏、Toggle、图标状态解析与滤镜、按钮状态机、菜单分组 | 611 |
| [05-find-search.md](nui-behavior/05-find-search.md) | 查找条几何、防抖与首个匹配规则、高亮属性、导航与越界、选项开关、性能 | 397 |
| [06-commit-graph.md](nui-behavior/06-commit-graph.md) | 轨道分配算法（含伪代码）、边类型与绘制、配色公式、引用标签、行布局、筛选分页 | 393 |
| [07-theme-dpi-dialogs.md](nui-behavior/07-theme-dpi-dialogs.md) | 主题切换与重建、DPI 缩放与对齐、字体字号、弹层状态机、对话框、通知、设置 | 442 |
| [08-diff-merge.md](nui-behavior/08-diff-merge.md) | 变更类型到颜色键的映射、行底有效值、gutter 标记绘制、未变更区分隔条 wave 图案、行内词级差异；**三栏合并与冲突解决**（gutter 操作的存在性判据、每侧高亮器安装规则、动作组与启用判据） | 260 |
| [09-icons.md](nui-behavior/09-icons.md) | expUI 图标的可测量参数：填充 vs 描边、默认线宽（**1px**，非 1.5）、端点/转角、逐图标几何；两轮已落地项（线宽修正 + 填充化）与仍未填充化的图标 | 187 |

每份分册的规则都带 `（来源：相对路径:行号）`，并标注 **【可直接实现】/【Swing 特有】/【需推断】**。

**路径注意**：本仓库是 Bazel 布局，若干包不在常见位置——
查找功能在 `platform/lang-impl/src/com/intellij/find/`（不在 `platform-impl`）；
动作系统包名是 `com.intellij.openapi.actionSystem`（不是 `action`），`AnAction`/`Presentation` 在 `platform/editor-ui-api`。

## 2. 本轮已落地的改动

### 2.1 浅色/深色结构令牌对齐（已生效）

`web/src/mockup.css` 的 `:root` 与 `body[data-theme="dark"]` 已按 expUI 取值改写，权威键与出处见数值参考 §5.2 与 §7。

| 令牌 | 浅色（原 → 新） | 深色（原 → 新） | expUI 键 |
| --- | --- | --- | --- |
| `chrome` | `#E9EAEE` → `#F7F8FA` | `#2B2D30`（不变） | `ToolWindow.background` / 浅头部 `MainToolbar.background` |
| `panel-muted` | `#F5F8FE` → `#F7F8FA` | `#25262A` → `#2B2D30` | `*.background` |
| `border` | `#E3E3E3` → `#EBECF0` | `#393B40` → `#1E1F22` | `*.borderColor` |
| `border-strong` | `#D1D3D9` → `#C9CCD6` | `#4B4D53` → `#4E5157` | `Component.borderColor` |
| `text` | `#202124` → `#000000` | `#DFE1E5`（不变） | 浅头部 `MainToolbar.foreground`（Gray1） |
| `muted` | `#646870` → `#6C707E` | `#9DA1AA` → `#9DA0A8` | `StatusBar.Widget.foreground` |
| `faint` | `#A0A4AA` → `#A8ADBD` | `#6F737B` → `#5A5D63` | `*.disabledForeground` |
| `blue` | `#3871E1` → `#3574F0` | `#548AF7` → `#3574F0` | `*.focusColor` |
| `blue-soft` | `#D0DFFE` → `#D4E2FF` | `#2F466F` → `#2E436E` | `*.selectionBackground` |
| `selection-inactive` | `#E9EAEE` → `#DFE1E5` | `#43454A`（不变） | `*.selectionInactiveBackground` |
| `history-selection-inactive` | `#E9EAEC` → `#DFE1E5` | `#43454A`（不变） | 同上（原值实为 `hoveredBackground`） |

> **本表是早期一次 expUI 对齐的历史记录，不是当前值。** 后续轮次把其中多项又改回 ManyIslands 家族语义别名：`chrome` `#F7F8FA` → `#E9EAEE`（`gray-150`）、`blue-soft` 浅 `#D4E2FF` → `#D0DFFE`（`blue-140`）、`panel-muted` 浅 `#F7F8FA` → `#F7F8F9`（`gray-160`，第 131 轮）、`border`／`border-strong` 同理。**当前值一律以 `docs/design-system.md` §6.1（浅色）／§6.2（深色）为准**。（`--augit-blue` 令牌本身现值与本表末行一致；但焦点环与主按钮已改走 `accent-brand-bg` = 浅 `blue-80` `#3871E1`／深 `Blue6` `#3574F0`，见第 90／117 轮。）

### 2.2 修复了一个静默失效的重复令牌块

`mockup.css` 原有 **两个 `:root` 块**（文件顶部一个，"第二轮视觉基线"处还有一个）。第二个位置更靠后、同优先级，**静默覆盖**第一个——所以对第一个块的颜色改动不会生效。这正是历史上"改了颜色看不出变化"的成因。

已把第二个块收敛为只保留它真正需要偏离的尺寸项（`--augit-side-width`），颜色令牌回到单一来源。颜色令牌现在每个恰好 2 处定义（浅色 + 深色）。**后续新增令牌必须在文件顶部的两个块中声明，禁止再引入第三个同名块。**

### 2.3 验证证据

| 验证 | 结果 |
| --- | --- |
| 资源字节一致（`tools/audit/verify-ui-assets.ps1`） | `PASS: runtime UI assets match the visual mockups.` |
| 验收套件（`node tools/audit/live-shell.spec.cjs`） | **`live-shell 通过 1254 项断言`**（退出码 0；第 136–146 轮按权威改了交互——"空信息走确认"、"无勾选即禁用"、`Amend` 改名与聚焦、Changes 菜单重排、Push 标题用仓库名、Reset 默认改 Mixed、Clone 按钮 URL 空即禁用、Worktree 新建表单加「新分支」——断言由 1070 增至 1083；第 150 轮加"归属行悬停提示用完整修订"两条 → 1085；第 151 轮加"文件历史表头列名与顺序""按权威列序填真实数据"两条 → 1087；第 152 轮加"目标行滚到可视区中部"一条 → 1088；第 153 轮加"查询进行中先清空旧结果并显示正在搜索""结果到达不覆盖用户继续输入""查询落地后回到结果列表"三条 → 1091；第 154 轮加"取消是一次性动作"一条 → 1092；第 157 轮加"终端里 Esc 把焦点交回正文且不送给 Shell"一条 → 1093；第 160 轮加"日志筛选草稿在重绘后保留"一条 → 1094；第 161 轮加"点底部「日志」标签回到日志且不整页跳转"一条 → 1095；第 162 轮加"已加载的空日志往返后不重复查询"一条 → 1096（第 163 轮把它扩成"不重复查询**且回到日志**"两半，条数不变）；第 164 轮加"往返后详情正文位置与提交选择保持"一条 → 1097；第 166 轮加"日志工具栏 搜索／刷新／定位 HEAD 已接线"三条与"返回／删除引用 禁用并写明原因"两条 → 1102；第 167 轮加"日志右键菜单"四条 → 1106；第 169 轮加"跳转行按钮／定位当前文件／折叠项目树／侧栏最小化"四条 → 1110；第 171 轮加"非当前分支行菜单给出检出、重命名与删除""当前分支行不出现检出/删除""标签行菜单可删除""按该行自己的名字重命名""删除引用先确认影响""未完全合并时再问一次并强制删除"六条 → 1116；第 172 轮加"日志菜单「新建分支…」以选中提交为起点""「新建标签…」是单字段窗口且标题带该提交""标签名含空白字符时不调用宿主并保留窗口""确认后把标签打在选中的提交上"四条（并把日志菜单的"禁用项/可用项"计数从 5/2 改成 4/3）→ 1120；第 173 轮按权威把日志左竖条改成分支面板动作组，删掉 5 条编码旧竖条的断言、加 8 条 → 1123；第 174 轮把引用树按 `live.references` 重做成 HEAD＋本地/远程/标签 并加选中态与搜索，竖条补回权威的完整动作组（12 项）并按选择启停 → 1129；第 175 轮给引用树分组加折叠/展开（单击与左右键）并接线竖条的「全部展开／全部折叠」→ 1133；第 176 轮补分支面板设置弹层（「显示标签」会话开关）→ 1135；第 177 轮让 HEAD 行也能被定位（权威里 HEAD 节点可选中、可导航）→ 1136；第 178 轮给竖条「更新选中分支」接线——宿主 `git/fetch` 增加可选 `branch`，按该分支跟踪的远端分支快进本地分支，并补 6 条断言 → 1142；第 179 轮落地日志筛选（`20-branches-host-batch.md` §7 第 2 步）：`git/history` 收 `message`／`hash`／`author`／`since`／`until`／`branch`／`path`，服务层按权威实现"哈希命中即短路其它筛选、一条没命中回落文本筛选"（`VcsLogFiltererImpl.kt:88-101,336-341`），界面接线「文本或哈希」的回车/失焦/清空、引用行双击与回车的分支筛选、「分支」控件弹层（全部＋HEAD＋本地/远程）与设置里两个**互斥**的「单击时」开关，未接线的用户／日期／路径三项按 ux-spec §7.8 禁用并写明原因，并补 8 条断言 → 1150；第 180 轮落地「与当前分支比较」——核对后更正了交接文档的原读法：权威打开的是**带 Range 过滤器的日志视图**（`GitCompareBranchesUi` ＝ `fromRange(current, branch)`，文本 `<current>..<branch>`），不是文件差异，因此宿主给 `git/history` 加 `rangeExclusive`/`rangeInclusive`（`git log A..B`），界面把底部工具窗口切成兄弟标签 `比较: <分支> 与 <当前>`，并补 3 条断言 → 1153；第 181 轮落地「我的分支」与「显示标签持久化」——宿主 `git/branches-mine` 按权威 `BranchesDashboardUtil`（独占提交非空且全由当前用户提交）现算判据，竖条「我的分支」做成 `aria-pressed` 的会话内开关、引用树按 `showOnlyMy` 只留命中分支＋HEAD 行、标签一并过滤，并把「显示标签」写回设置文件（`ApplicationSettings.ShowGitBranchesTags`），补 4 条断言；「标记为收藏」按用户裁决（新增项目级持久化状态，超出「不新增功能」的边界）**暂不实施**、继续禁用并写明待裁决 → 1157；第 182 轮补齐引用树多选的**宿主侧**（`git/history` 的 `branches` 并集、`git/fetch` 的 `branches` 逐个快进、`git/branch` `delete` 的 `names` 逐个删并回 `deleted`/`refused`；只动 C# ⇒ 断言数不变）；第 183 轮落地多选的**界面侧**——选中集从单值改成数组（权威 `Tree.java:141,291` 的 `DISCONTIGUOUS_TREE_SELECTION`）、Ctrl/⌘+单击切换、Shift+单击与 Shift+方向键按锚点扩展区间、空格切换、动作判据与删除文案随选中集变化（`refs.none { it.isCurrent }`／`allRefsAreBranches`）、ENTER/双击与「更新选中分支」「与当前分支比较」「删除」都按整个选中集执行，并补 7 条断言 → 1164；第 184 轮补上**引用树行的右键菜单**（权威 `BranchesTree.kt:272` 的 `BranchesTreeActionGroup` → `BranchActionsBuilder.build` 按选中集构成：单引用走 `GitSingleRefActions`、多引用走 `MultipleLocalBranchActions`）与右键选择规则（`Tree.java:1112-1130`：右键落到未选中行先替换选中集），补 3 条断言 → 1167；第 185 轮把**日期筛选**接线（权威 `DateFilterPopupComponent`：选择期间…／最近 24 小时／最近 7 天，值文本 `Since/Until/<a>-<b>`；宿主 `since`/`until` 第 179 轮已就绪，零 C#），补 3 条断言 → 1170；第 186／187 轮备好「按用户筛选」的宿主两半（`git/authors` 列表 ＋ 多值 `authors`），第 188 轮落地**用户弹层**（复选列表 ＋ 全选/全不选），补 3 条断言 → 1173；第 189 轮补上用户弹层的**搜索框**（权威 `MultipleValueFilterPopupComponent` 列表顶部）→ 1174；第 190 轮把日期弹层的第三项「选择期间…」落地（权威 `DateFilterComponent`：起始/结束 ＋ 确定 → `fromDates(after, before)`，两端都空不设筛选），补 2 条断言 → **1201**；第 202 轮落地**「标注上一修订」**（权威 `AnnotatePreviousRevisionAction` → `AnnotateRevisionAction`，文本 `action.annotate.previous.revision.text` = "Annotate Previous Revision"）：宿主 `GitBlameLine` 新增 `PreviousRevision`（解析 `git blame --line-porcelain` 的 `previous <sha> <file>` 头，正是权威 `GitPreviousFileRevisionProvider` 的第一分支），`git/blame` 新增可选 `revision`（服务层本就支持、桥接此前硬编码 null）并回传 `previousRevision`；界面在归属行右键给出该动作、**只对带上一修订的行**给出（权威 `update()` 在 provider 拿不出上一修订时 `setEnabledAndVisible(false)`），点它按该修订就地重新标注并在工具栏写明依据的修订。**登记差异**：权威在新标签里打开标注，Augit 就地重标注同一视图。第 203 轮落地**文件历史作者列的 `*` 与单元格 tooltip**（权威 `FileHistoryPanelImpl.AuthorColumnInfo`，`FileHistoryPanelImpl.java:764-788`：作者 ≠ 提交者时值后加 `*`，tooltip 为 `{作者} <{邮箱}>`，不同名再追加 `, via {提交者} <{提交者邮箱}>`，文案 `file.history.details.committer.tooltip.info` = "via {0}"，`VcsBundle.properties:935`）：宿主 `GitHistoryEntry` 新增 `CommitterName`／`CommitterEmail` 并解析 `%cn`／`%ce`（分页历史、非推送列表与提交详情三处格式；非推送列表改为复用同一个格式串），`git/file-history` 与 `git/history` 都回传 `authorEmail`／`committerName`／`committerEmail`；界面新增 `historyAuthorCell()`（文件历史、与当前分支比较、样例三处共用）并按该规则渲染值 `*` 与 tooltip。并补 1 条断言 → **1202**；第 205 轮把**仓库初始化的界面**接线（权威 `GitInit.java:45-83`：入口文案 `action.Git.Init.text` = "Create Git Repository…"、目标来自单目录选择器、**只有"目标已在 Git 下"**才弹 Yes/No 警告、非仓库直接初始化、失败带 Git 错误输出）：Git 主菜单加「创建 Git 仓库…」，非 Git 工作区自动给出入口一次，进行中冻结全部动作，成功关窗后重读状态／历史／引用并刷新；新增 `tools/verify-ux-repository-init.cjs` ⇒ 检查器 **35 → 36**；并补 6 条断言 → **1212**；第 207 轮把 **Smart Checkout 的界面**接线（权威 `GitSmartOperationDialog` `GitSmartOperationDialog.java:36-125`：标题 "Git {0} Problem"、受影响改动列表、「Smart Checkout」与「Don't Checkout」两个选择、**默认焦点在取消**）：宿主在 `git/checkout` 失败时回传 `overwriteRisk`／`overwritePaths`（解析 git 的"会被覆盖"错误，口径同 `GitLocalChangesWouldBeOverwrittenDetector` 检测器），界面据此弹权威形态对话框，取消绝不执行 Git，恢复冲突时说明临时 stash 已保留；新增 `tools/verify-ux-smart-checkout.cjs` ⇒ 检查器 **36 → 37**；并补 4 条断言 → **1212**；第 209 轮把**大文件的只读预览接进界面**（权威 `LargeFileNotificationProvider.java:37-58`：Warning 面板文案 "The file is too large ({0}). Showing a read-only preview of the first {1}." ＋「隐藏通知」/「不再显示」两个动作）：`TextPreview` 映射成纯文本只读视图并带 `previewBytes`，编辑器顶部给横幅，隐藏只记会话、不再显示写 `HideLargeFileWarning` 设置；新增 `tools/verify-ux-large-file-preview.cjs` ⇒ 检查器 **37 → 38**，并把三处编码"整页拒绝"旧行为的断言按权威改写；并补 4 条断言 → **1216**；第 210 轮落地**全仓搜索到限后的「结果过多」**（权威 `UsageLimitUtil.java:26-34` 的 "Too Many Results" ＋ Continue／Abort，`UsageViewManagerImpl.java:334-357` 的"继续后不再提示／中止即取消"）：宿主 `search/text` 支持 `offset`／`limit` 分页，界面到限弹对话框、继续则分页取完并追加、中止保留已有结果；新增 `tools/verify-ux-search-limited.cjs` ⇒ 检查器 **38 → 39**；并补 3 条断言 → **1219**；第 212 轮把 **Worktree 移除**补上权威的 `!isMain` 判据（`RemoveWorkingTreeAction.isEnabledFor()` 的 `!it.isCurrent && !it.isMain`）：`GitWorktreeInfo` 新增 `IsMain`（按 `GitWorktreeListParser` 取 `git worktree list` 的**首项**）、`git/worktrees` 下发该字段、`InspectRemovalReadinessAsync` 与 `RemoveAsync` 两侧都拦、界面把状态显示成「主工作树，不能移除」并禁用「移除…」＋悬停原因，并补 1 条断言 → **1220**；第 215 轮落地 **`ux-spec` §7.18 第 2 条**（Git 不可用时「提交」「Git 历史」入口禁用并给出悬停原因）：`mockup.js` 的 `rail(active, gitUnavailableReason)` 写 `aria-disabled`／`title`、`shell()` 在 live 下**只**取 `live.gitUnavailableReason`（Git 可用时不退回场景参数）、`live-data.js` 的 `bindToolRail()` 阻止禁用入口切换工具窗口、`showGitUnavailable()` 在原因晚到时重绘 rail 区域，视觉稿场景 `git-unavailable` 传同一条原因；并补 2 条断言 → **1222**；第 216 轮按权威 `AmendCommitHandlerImpl.kt:78-115` 补齐 **Amend 的"仅在用户没改过信息时才覆盖/恢复"**（`initialMessage` 由 `amendInitialMessages` 在提交框首次渲染时记录、提交成功后清空；只有"当前值 == 基线"才读上一次提交信息并载入；恢复条件改成"字段仍等于载入的 amend 信息"；忽略空白相等则不动字段；`mockup.js` 的 Amend 示例文本加 `window.__augitLive` 守卫以免冲掉真实输入），把原先"改过也覆盖"的 2 条断言替换为"改过不覆盖/不查宿主"与"没改过载入并聚焦、取消恢复"（**断言数仍为 1222**）；第 217 轮订正**项目树缩进步长**：权威 `Tree.leftChildIndent`(7)+`Tree.rightChildIndent`(11)=18、`ClassicPainter.getRendererOffset()` 的 `(depth-1)*(left+right)`、参考图实测 18.4；实现此前被 `.side-content.tree .depth-1..4` 四条固定规则写成 **16px 步长**且超过 `depth-4` 会退回 6px，现改为按行上的 `--tree-depth` 计算 `calc(4px + 18px * var(--tree-depth))`（首级 22px、任意深度递增），并由 `tools/verify-ux-project-tree.cjs` 在 dpi 96/120/144 × 字号 13/40 的每一步断言步长 18 与 depth-7=130px（**断言数不变**；`design-system.md` §8.3 与 `02-tree-list.md` 的 16px 旧表述同步订正）；第 218 轮落地 **Git 历史上的翻页**（`ux-spec` §7.8 第 475-476 行）：宿主 `git/history` 新增 `page`（页大小 100，`ShellBridgeHistoryTests` +1），界面 `loadHistoryPage()` 在 `.commit-list` 滚动触底时取下一页并追加、`historyRequestToken` 使上下文切换后的旧页作废、失败不推进页码、`live.historyScrollTop` + `restoreHistoryScroll()`（并补一帧兜底）保持"现有 100 条可见"；并补 5 条断言 → **1227**；第 219 轮为 **§7.13 外部解决冲突后的会话列表更新**补 1 条断言（推送 `workspace-changed {gitMetadata:true}`，链路 `loadStatus（hasConflicts）→ loadOperationSession → openConflictSession` 就地重绘，列表 2 → 1、说明同步、耗时 < 500ms）→ **1228**（只改 spec，未动产品代码）；第 220 轮实现并断言 **§7.9 点击 Blame 提交定位 Git 历史**（权威 `GitFileAnnotation.showAffectedPaths()` → `VcsLogNavigationUtil.jumpToRevisionAsync`，registry `vcs.blame.show.affected.files.in.log` 默认 true）：归属行加 `data-blame-full`、新增 `locateBlameCommit()`（`live.fileHistory` 存在时先 `clearHistoryPathFilter()` 恢复日志布局与解除路径限定）、日志已可见时不重绘以免重置选中，并把日志选中写进 `live.historySelectedHash`；并补 3 条断言 → **1231**；第 221 轮实现并断言 **§6.7 关闭比较后返回尚未就绪的普通标签显示读取占位**：`shell()` 在 `live.pendingDocument && !live.document && editor !== "diff"` 时切到新增的 `documentLoadingView()`（`.empty-tool-state` + `role="status"`），并补 1 条断言（慢读产品规格文档 → 在途时打开比较 → 真实点击关闭比较标签 → 正文为"正在读取 <文件>…"且无 `.empty-state` → 读完后正文替换）→ **1232**；第 222 轮为 **§7.16 终端启动时序**补 3 条断言并实现启动代际失效：`live-data.js` 新增 `terminalGeneration`（`closeTerminalNow()` 递增），`startTerminal()` 在 `terminal/start` 返回后若代际已变则 `terminal/stop` 释放刚启动的 Shell；断言 `§7.16 启动中关闭：晚到的启动作废并释放刚启动的 Shell`（700ms 启动延迟、`starts=1`/`stops=2`/`ready=false`）、`§7.16 关闭后立即重开建立新请求`、`§7.16 ready 前的提示符/输出在终端显示后保留`（桩旋钮 `__terminalBootOutput`）→ **1235**；第 223 轮关闭**归类总表 §7 的 T1／T2**并订正 Diff 行色模型：删除无权威对应的 `.diff-current` 层（`live-data.js` 的 `moveDiffChange` 只保留定位与触发按钮焦点，`mockup.css` 删除 3 条规则与 6 处 `--augit-diff-current-*` 定义），实现**行内（词级）高亮层**（`.diff-code-line.<kind> mark` 取权威 `DiffColors.DIFF_*.BACKGROUND`；宿主 `oldChanges`／`newChanges` 通道与 `mockup.js` 的 `marked()` 本就存在，只有配色缺失），并按 `DiffViewerHighlighters.createHighlighter` 的 `ignored = !resolved && innerFragments != null` 两档规则把整行底订正为 增 `#BEE6BE`／删 `#D6D6D6`／改 `#E7EFFA`（深 `#294436`／`#484A4A`／`#283541`）；同时修正 `diffChangeBlocks()` 不认 `changed`（`Modified`）行、导致纯修改型 Diff 一处差异都定位不到的真实缺陷；检查器 `tools/audit/check-diff-current.test.cjs` 由 `check-diff-inline.test.cjs` 取代；桩新增 `src/Modified.cs` 场景并补 2 条断言 → **1237**；第 224 轮关闭**归类总表 §7 的 T11**（`ux-spec.md:441`「差异数量按连续变更块计算」此前不成立）：`diffChangeBlocks()` 改为一栏计块（双栏下每一栏都是同一份变更块的完整行列表，原来扫整份 DOM 使每处差异算两次），"是否已在边界"改用**点击前**的索引判断（否则定位到首/尾块的那一次点击就直接变成边界提示），导航状态从跨重绘存活的**滚动容器**移到**布局根**（此前切单双栏或换文件后第一次点箭头会直接切相邻文件）；并修掉 `bindDiffModes()` 不按 `live.diffMode` 回填导致**切单栏后一次刷新退回双栏**（`mockup.js`，回填只改 DOM、不回调宿主）；`§7.9 差异导航…` 断言回到规格口径（一次替换 = **1** 处，原来记的"2 处"是两栏各算一次的产物），`src/Modified.cs` 场景加严为"两处被上下文隔开的 Modified：0 → 1 → 第三次才出提示"＋"切单栏后总数仍是 2 而 changed 行只有 2"（**断言条数仍为 1237**）；第 225 轮关闭**归类总表 §7 的 T6 中两条 §7.3 项**：Markdown 预览的加载/失败状态（读取在途超过 `LoadingFeedbackDelay` = 150ms 才在预览区顶部显示「正在生成 Markdown 预览…」，短读取不闪提示；失败时保留原文与旧预览、在预览区给出宿主原因、点预览或 Enter/Space 重试读取），状态记在 `live.markdownPreview`，`mockup.js` 的 `liveMarkdownDocument()` 只按状态渲染、`syncMarkdownPreviewState()` 做**不重绘正文**的局部同步（原文滚动位置与对照比例保持），并补 2 条断言 → **1239**；第 228 轮修掉一处**默认场景显示样例文档**的产品缺陷并加 1 条断言 → **1240**：实时侧没有文档时 `editor` 会停在场景默认值 `"markdown"`，正文于是渲染视觉稿的样例文档（标题"Augit 产品规格"、文件栏 `docs/product-spec.md`），而 `live.document` 是 `null` —— 把磁盘上不存在的文件当成真实只读文件显示；现按规格 §6.7 的无文档提示落成 `editor === "empty"`（只对产品默认场景 `main-project` 生效，`--scene` 审计场景仍用样例正文）。同一轮为 `text-viewer` 的"刷新后查找条不重复打开"与 `main-project` 的"确认后焦点回到触发区域"两处用例补上"先打开真实文档"的前置（它们此前依赖样例正文）；第 231 轮为 §7.2 第 11 条补断言 → **1244**：桩新增计数可精确预期的样本 `docs/search-sample.txt`，`git` 在"忽略大小写子串／加全字／再加区分大小写"三档下计数与高亮标记逐值吻合（8／4／2），并断言不出现视觉稿的固定 `/12`；第 233 轮为 §7.2 第 13 条补 **7 条断言 → 1251**：包一层 `window.Worker` 计数证明只有正则走后台线程（普通文本 0 次作对照）；每 20ms 采样证明 145ms 前无"正在搜索…"、之后出现且结果照常落地；对照组"在途连按两次下一项 ⇒ 落地后 `3/8`"证明方向队列真的被兜住并排空；A/B 两次在途任务被"换查询/换开关"取消（结果与队列一步都不许出现，最终 `1/4`）；Esc 关闭、`visibilitychange(hidden)`、切 Markdown 预览三条分别证明旧结果失效；同轮把 `__augitFindResultDelay` 的注入点从"Worker 结果到达之后"移到"报告就绪之前"，修掉第 127 轮 Worker 路径注入恒被丢弃的桩缺陷；第 234 轮为 §7.8 第 3 条补 **3 条断言 → 1254**：提交详情独立滚动（`max=1186`、末行在场）＋真实滚轮与 Home/End/PageUp·Down/方向键只移动详情不切换提交；重复选择与收折详情保持阅读位置、切换提交回到新详情顶部（往返一次 ⇒ `top===0` 非平凡）；提交信息超 20 MB 时**两栏**都显示宿主原因、重绘后仍在，且不退回占位模板里的第一个提交主题 —— 最后一条是**先挂断言挂出来的产品缺陷**（原因原先只写在变化文件栏，详情栏只剩行头，且 `detailHtml` 留空会让重绘退回 `history.commits[0]` 的占位模板）；第 230 轮为 §7.7 的**双栏几何、只读身份与同步滚动**补 3 条断言 → **1243**：13px 行留白／7px 槽内距／中栏宽度＝`max(84, 最长行号文本宽 + 14)`（测试用同一公式复算）＋文件栏中栏同宽；两个锁图标（只读身份）＋悬停说明含完整路径＋切单双栏不动工具栏；新增长差异桩 `src/LongDiff.cs`（120 行上下文）后断言滚 60px 时左右正文与中栏行号位移逐值相同；同轮把 §7.9 #23「比较对话框只列出分支、标签和提交」按产品边界改标 **不适用**（Augit 用引用树 + 日志筛选选比较目标，不实现该对话框），生成器 `gen-clause-conclusions.cjs` 新增 **E 不适用** 类别，§2.10 的 C 类 3 → **2**，随后 §7.3 #6/#12 两行由 `未覆盖` 转 **是** ⇒ C 类 **0**（分母 100 → 98）；同时删掉 §2.0 摘要里手写的旧分类计数（99/4/10/25）以免与脚本口径再次漂移；第 208 轮把**大文件的三档限制与"前 N 的只读预览"接进宿主**（权威 `FileSizeLimit.kt:14-24,60-110` 与 `FileUtilRt.java:1089-1101`：内容加载 20 MB／智能感知 2500 KB／预览 2500 KB，扩展名只能放大；超过内容加载上限的文本由 `LargeFileEditorProvider` 只读预览前 `getPreviewLimit` 字节，`LargeFileNotificationProvider.java:37-58`）：`DocumentLimits` 改成三档并按扩展名取值，`ReadOnlyDocumentService` 按完整 UTF-8 字符边界返回 `TextPreview` ＋ `previewBytes`，`document/read` 带上该字段；Core +2 与 Infrastructure +1 条单测；**只动 C# 与测试 ⇒ 断言数与四个运行时哈希不变**；第 206 轮把 **Smart Checkout 的宿主侧**接线（`git/checkout-smart`，权威 `GitBrancher.java:91-92` 的 "stash-checkout-unstash" 与 `GitCheckoutOperation.java:367-395,505-524`；回包与 `git/operation` 共用会话投影，恢复冲突即 `SmartCheckout` 会话且保留临时 stash；权威的 Force Checkout 属破坏性新增能力 ⇒ 登记为差异），+3 条宿主单测；**只动 C# 与测试 ⇒ 断言数与四个运行时哈希不变**；第 204 轮把**仓库初始化的宿主侧**接线（`git/init`，权威 `GitInit.java:66-83`：只有"目标已在 Git 下"才要 Yes/No 确认、非仓库直接初始化、过程走后台任务 ⇒ 并入写操作通道；成功后作废 Git 解析与状态缓存），+3 条宿主单测；**只动 C# 与测试 ⇒ 断言数与四个运行时哈希不变**；第 202 轮落地**「标注上一修订」**（权威 `AnnotatePreviousRevisionAction` → `AnnotateRevisionAction`，文本 `action.annotate.previous.revision.text` = "Annotate Previous Revision"）：宿主 `GitBlameLine` 新增 `PreviousRevision`（解析 `git blame --line-porcelain` 的 `previous <sha> <file>` 头，正是权威 `GitPreviousFileRevisionProvider` 的第一分支），`git/blame` 新增可选 `revision`（服务层本就支持、桥接此前硬编码 null）并回传 `previousRevision`；界面在归属行右键给出该动作、**只对带上一修订的行**给出（权威 `update()` 在 provider 拿不出上一修订时 `setEnabledAndVisible(false)`），点它按该修订就地重新标注并在工具栏写明依据的修订。**登记差异**：权威在新标签里打开标注，Augit 就地重标注同一视图。并补 1 条断言 → **1201**；第 201 轮做**两处宿主侧对齐**：① 快速打开的结果上限 **100 → 30**（权威 `SearchEverywhereUI.SINGLE_CONTRIBUTOR_ELEMENTS_LIMIT = 30`，`SearchEverywhereUI.java:217-218`；每贡献者取 `contributors.size() > 1 ? 15 : 30`，`:951-956` —— Augit 的快速打开就是 `GotoFileAction` 的**单贡献者** Files 路径；全仓搜索的 1000 条阈值是另一回事），同步 C# 常量、宿主两个单测与 `ux-spec.md`；② 归属行悬停提示的 `Date:` 补上**时间**（权威 `GitFileAnnotation.java:193` 用 `DateFormatUtil.formatDateTime`，`:120-124`）—— 外壳 `git/blame` 新增 `dateTime`（`yyyy/M/d H:mm`，本地时区），槽位仍显示短日期，并补 1 条桥接单测。第 200 轮落地**细线（1 单位分隔线）的整设备像素对齐**：权威把「1 单位的线」按 `JBUIScale.scale(1) = round(userScaleFactor)` 取整（`JBValue.get()` 同义，`07-theme-dpi-dialogs.md` §2 的「分隔线粗细必须按 DPI 缩放」），CSS 侧等价写法 `round(dpr) / dpr` 落在新令牌 `--augit-hairline`（`mockup.js` 的 `applyDeviceScale()` 在加载与 resize 时写 `--augit-dpr`／`--augit-hairline-device`，未运行 JS 回落 1px ⇒ 1× 与改动前逐值相同），全库 92 处 `border*`／`outline`／`box-shadow` 与 3 个显式分隔线元素改用它；**引擎限制已登记**：Chromium 把 `border-*`／`outline` 宽度取整到整数 CSS 像素，令牌只在 `height`/`width` 与 `box-shadow` 上生效。并补 1 条断言 → **1200**；第 199 轮钉住 **DPI 传递链的网页侧契约**（`07-theme-dpi-dialogs.md` §2）：外壳在 `--dpi` 路径上把 WebView2 的 `RasterizationScale` 固定成 dpi/96（`ApplyRasterizationScale()`），网页层因此拿到 `devicePixelRatio = dpi/96` 而 **CSS 像素仍是逻辑像素** —— 用 `deviceScaleFactor: 2` 的重量级仿真验证：`devicePixelRatio === 2`、十个关键元素矩形与 1× **逐一相同**，且自绘行高在两种缩放下都落在**整设备像素**上（`--code-line-height × dpr` 为整数，对应 `07` §2.4 `alignIntToInt` 在 CSS 侧的等价约束）。并补 1 条断言 → **1199**；第 198 轮处理外壳与网页层的**主题边界**：① `ShellTheme.SurfaceColor()`（浅 `#E9EAEE`／深 `#2B2D30`，即网页层 `body` 的 `--augit-chrome`）在**导航前**写给 WebView2 的 `DefaultBackgroundColor`，并在 `theme/changed` 时同步 —— 此前深色下会先用默认白底画一帧（白闪），参考实现切主题时会重绘全部窗口背景；② 按 `07-theme-dpi-dialogs.md` §1.3 的明确建议补一条**主题几何不变性**断言：同一场景在浅/深两套主题下，标题栏／轨道／三栏日志／提交行／筛选控件等十个关键元素的 `getBoundingClientRect()` **逐一相同**，而 `body` 底色不同。并补 1 条断言 → **1198**；第 197 轮把设置对话框的**底栏与快捷键**按权威补齐：底栏照 `SettingsDialog.createActions()`（主设置对话框 `isApplyButtonNeeded = true`、`isResetButtonNeeded = false`，`SettingsDialog.java:86-96,200-215`）从实时外壳此前的「取消／保存」改成**「取消／应用／确定」**（与视觉稿同一组）——「应用」只 `editor.apply()` 不关窗、且照 `SettingsEditor.updateStatus()` 的 `setEnabled(isModified())` **只在有未保存修改时可用**；`Ctrl+F` 照 `SearchTextField.FindAction`（经 `SettingsDialog.init()` 注册到 `ACTION_FIND`）聚焦并**全选**搜索框、且不再顺带打开文档查找；搜索框里的 `↑/↓` 照 `SettingsSearch.preprocessEventForTextField()` 委托给分类树 ⇒ 移动分类选择而焦点仍在搜索框；`Enter` 照 `DialogWrapper` 的 default button ⇒ 触发「确定」（写回并关窗）。并补 5 条断言 → **1197**；第 196 轮按权威落地**设置搜索**（`SettingsFilter` ＋ `SearchableOptionsRegistrar`）：命中判据从「只有分类名」扩展为「选项标签／下拉项文案／取值 ＋ 分类名」（`getConfigurables()` 的 nameHits／contentHits，`shouldBeShowing()` 让含命中项的分类保持可见），文字变化后 **100 ms 去抖**（`SettingsFilter.update()` 的 `delay(100.milliseconds)`），当前分类无命中而别处有 ⇒ **切到第一个命中分类**（`shouldMoveSelection`），命中项按 spotlight 边框色 `Settings.Spotlight.borderColor`（兜底 `ColorPalette.Orange6`／`Orange4`）标出并滚到视野中央（`SpotlightPainter` 的 `addSpotlight` ＋ `center(component)`），无命中时输入框底色取 `SearchField.errorBackground`（`LightColors.RED`，浅 `#ffcccc`／深 `#743a3a`），ESC 在文本框有内容时清空过滤且**不关对话框**（`SettingsSearch.preprocessEventForTextField` 消费该事件）。新增令牌 `--augit-search-error-bg`／`--augit-spotlight-border`，并补 4 条断言 → **1192**；第 195 轮**让工作区那份 `live-shell.spec.cjs` 重新可跑**（不再需要临时生成器）：把并行会话留在里面的三处在途 hunk 按已裁决的取值改回（主框架 tab/tree/status 三处高度回到规格名义值 42／28／22 与 `ceil(max(27,h+8)/2)*2`、`design-system.md` §6.1 的「三处高度：名义值即运行时值」用户裁决；§154 首个可见节点的写死值回到 `bulk-006`），并把 `§7.17 设置生命周期` 的 `page.evaluate(fn, key, value)`（Playwright **不接受**两参数）改成单对象载荷 —— 于是那条一直被剔除的生命周期断言**本身也跑起来了**（取消 ⇒ 不生效且关窗、保存 ⇒ 生效且关窗），断言数 1187 → 1188；另修两处时序：取消后用**合成单击**重开设置并显式等待按钮出现，原「保存写入设置」步骤前也补一次重开（生命周期块结束时对话框是关着的）。产品代码本轮**未改**。第 194 轮落地**ToggleAction 的选中态底**：权威 `ActionButton.getPopState()` 把 `isSelected()` 当 `isPushed` ⇒ 选中的 toggle 走 `PUSHED` 分支、底取 `ActionButton.pressedBackground()`（`ActionButton.java:221-222,626-639`、`ActionButtonLook.java:85-100`），因此竖条的「我的分支」选中时用 `--augit-pressed`（此前**没有任何规则**、看不出按下态）；芯片式可选按钮（查找条与全仓搜索的区分大小写／全字匹配／正则表达式）另有独立键 —— `SearchTextArea.MyActionButton.getPopState()` 把选中映射成 `SELECTED`（`:464-467`），`FieldInplaceActionButtonLook` 在未悬停时画 `SearchOption.selectedBackground`（`JBColor.namedColor("SearchOption.selectedBackground", 0xDAE4ED, 0x5C6164)`，`JBUI.java:522-528`；只有 `intellijlaf` 主题定义该键 ⇒ New UI 走兜底值）、悬停/按下画 `selectedHoveredBackground`／`selectedPressedBackground`（默认回落到 `pressedBackground`）⇒ 新增令牌 `--augit-search-option-selected` 并订正原来复用的 `--augit-blue-soft`；New UI 主题把 `hoverBorderColor`／`pressedBorderColor` 都设成 `transparent`，所以按钮只画底不画边与权威等价。并补 1 条断言 → **1187**；第 193 轮落地**「比较分支」**（权威 `ShowArbitraryBranchesDiffAction`，`action.Git.Compare.Selected.title` = 「Compare Branches」）：引用树行菜单的构成按 `BranchActionsBuilder.build()`（`BranchesDashboardActions.kt:100-121`）分档 —— 单选走 `GitSingleRefActions`、2 节点＝1 分支＋HEAD 走 `HeadAndBranchActions`（只有两个比较动作）、全是引用且 >1 走 `MultipleLocalBranchActions`（比较分支／显示文件差异／更新选中分支／删除分支，**没有**「与当前分支比较」）、其余（如引用＋标签）没有菜单；配对照 `BranchesPairActionBase.getBranchPair()`（`:316-362`）—— 恰好两个分支取选中顺序的前两个、1 分支＋HEAD 取（该分支, 当前分支）、3 个以上分支时该动作**隐藏**、两个名字相同（只可能是 HEAD＋当前分支）时禁用并写明原因；范围照 `compareAny(b1, b2)` → `GitCompareBranchesUi` 的 `fromRange(b2, b1)`（即 `b2..b1`）。「显示文件差异」（`ShowArbitraryBranchesFileDiffAction` → `CompareWithLocalDialog` 的整树变更列表）Augit 没有该界面 ⇒ 按「不新增功能」不落地并登记差异。并补 3 条断言 → **1186**；第 192 轮落地**「按目录分组」**（权威 `git.branches.group.by.directory`，`GitGroupBranchByDirectoryAction`；默认**开启** —— `DvcsBranchSettings.groupingKeyIds` 的默认值就是 `DIRECTORY`）：引用树按引用名的 `/` 逐段构树（`LazyRefsSubtreeHolder.buildSubTree()`，组节点落在第一个成员的位置；组内子节点按 `getSubTreeComparator()` 稳定排序；`getRefComparator()` 增加「名字含 `/` 的在前」这一键），前缀分组与类型分组共用同一套折叠/搜索/全部展开折叠交互，开关持久化到设置文件（`ApplicationSettings.GroupBranchesByDirectory`），并补 3 条断言 → **1183**；第 191 轮把**路径筛选**接线（权威 `StructureFilterPopupComponent`：`Select…`（`EditPathsAction` 的多行文本框）／`Select in Tree…`（`SelectPathsInTreeAction` → `VcsStructureChooser`）／`Recent`（`SelectFromHistoryAction`）三项，值文本按 `getTextFromFilePaths` 的「排序后第一条 ＋ ` + N`」并缩到 `FILTER_LABEL_LENGTH` = 30），宿主 `git/history` 新增**多值** `paths`（`git log … -- p1 p2`，与 `branches`／`authors` 同一套多值口径；单值 `path` 仍给文件历史用），并补 4 条断言 → **1180**） |
| 令牌唯一性 | 全部颜色令牌各 2 处定义（浅/深），无第三处覆盖 |
| 断言是否硬编码色值 | 零个 hex 字面量——套件只断言令牌名、几何与状态，因此配色变更不会打挂断言 |

验收套件用的是 **HEAD 版本**的 `live-shell.spec.cjs`（另建临时副本运行、跑完即删）。原因：并行会话当时正在改该文件，其工作区版本存在一个两参数 `page.evaluate` 的 Playwright 报错（`Too many arguments...`），会在设置场景提前中止；用 HEAD 版本才能隔离本轮改动。

> **第 136 轮的例外**：本轮**必须**改这个文件里的断言（它们编码的是"空提交信息点击即拒绝"的旧交互，按权威应改成"确认后继续"）。做法是 **HEAD + 仅这两处断言改动** 的临时副本（工作区版本仍因并行会话的 `Too many arguments` 跑不起来，实测确认）。因此上表的 `live-shell.spec.cjs`（HEAD 版）哈希仍指 HEAD 原样；工作区版本的改动本身要等并行会话收尾后才能整套跑通。断言数由 1070 → **1073**（本段两处改动合计：删 5 条旧断言、加 8 条新断言）。

#### 已验证快照（后续轮次用它低成本确认"代码未变、上次结论仍有效"）

`live-shell 通过 1254 项断言` 对应的文件哈希（第 234 轮复跑；本轮 `live-data.js`（`loadCommitDetails` 失败分支把 20 MB 原因同时写进变化文件与提交详情两栏，并写进 `live.commitDetails.detailHtml`以免重绘退回占位模板）与 `live-shell.spec.cjs`（§7.8 第 3 条的 3 条新断言 + 桩旋钮 `__commitDetailFailures`）换哈希，`mockup.css`／`mockup.js`／`bridge.js`／`current-find.js` 未变、断言数 1251 → **1254**；第 233 轮复跑；本轮 `current-find.js`（`__augitFindResultDelay` 的注入点从"结果到达之后"移到"Worker 报告就绪之前"，修掉第 127 轮 Worker 路径注入恒被丢弃的桩缺陷）与 `live-shell.spec.cjs`（§7.2 第 13 条的 7 条新断言）换哈希，`mockup.css`／`mockup.js`／`live-data.js`／`bridge.js` 未变、断言数 1244 → **1251**；第 222 轮复跑；本轮 `live-data.js`（终端启动代际 `terminalGeneration`：`closeTerminalNow()` 递增、`startTerminal()` 在晚到时 `terminal/stop` 释放刚启动的 Shell）与 `live-shell.spec.cjs`（`__terminalStartDelayMs`／`__terminalBootOutput` 旋钮 + 3 条终端启动断言）换哈希，`mockup.js`／`mockup.css`／`bridge.js` 未变、断言数 1232 → **1235**；第 221 轮复跑；本轮 `mockup.js`（新增 `documentLoadingView()`，`shell()` 在"读取在途且无文档、当前不是比较"时把正文切到读取占位）与 `live-shell.spec.cjs`（慢读 + 关闭比较的 §6.7 用例）换哈希，`live-data.js`／`mockup.css`／`bridge.js` 未变、断言数 1231 → **1232**；第 220 轮复跑；本轮 `mockup.js`（归属行新增 `data-blame-full`）与 `live-data.js`（`locateBlameCommit()`、归属行点击处理、日志提交选中写入 `live.historySelectedHash`）换哈希，`mockup.css`／`bridge.js` 未变、断言数 1228 → **1231**；第 219 轮复跑；本轮**只改 `live-shell.spec.cjs`**（§7.13 外部解决冲突后会话列表 500ms 内更新的断言），四个运行时文件与第 218 轮逐个相同、断言数 1227 → **1228**；第 218 轮复跑；本轮 `live-data.js`（`git/history` 分页：`loadHistoryPage()`、`bindHistoryScroll()`、`restoreHistoryScroll()`、`mapHistoryCommit()`、`historyRequestToken`，`loadHistory` 不再早捕获 `live`、`applyHistory` 附着分页状态）与 `live-shell.spec.cjs`（`__historyPages`／`__historyPageFails` 旋钮 + 5 条分页断言）换哈希，宿主 `ShellBridge.ReadHistoryAsync` 新增 `page` 参数、`ShellBridgeHistoryTests` +1 条；`mockup.css`／`mockup.js`／`bridge.js` 未变；第 217 轮复跑；本轮 `mockup.css`（`.side-content.tree` 的缩进改为按 `--tree-depth` 的 18px 步长，删除四条固定 `depth-N` 16px 规则）与 `mockup.js`（两处项目树构建写 `--tree-depth`）换哈希，`live-data.js`／`bridge.js`／`live-shell.spec.cjs` 未变，断言数 1222 不变（缩进步长由 `verify-ux-project-tree.cjs` 的 dpi×字号矩阵断言，不在 live-shell）；第 216 轮复跑；本轮 `mockup.js`（`bindChangesWorkflow()` 的 Amend 示例行为加 `window.__augitLive` 守卫）与 `live-data.js`（Amend 的"面板激活初始信息"基线 `amendInitialMessages`、成对草稿 `{before, amend}`、忽略空白比较、载入/恢复同时写 `live.commitDraft`、提交成功后清空基线）换哈希，宿主与 `bridge.js`／`mockup.css` 未变，断言数 1222 不变（替换两条 Amend 断言）；第 215 轮复跑；本轮 `mockup.js`（`rail()` 的 Git 不可用禁用分支、`shell()` 的 `gitUnavailable` 参数）与 `live-data.js`（`bindToolRail()` 的禁用入口守卫、`showGitUnavailable()` 的 rail 区域重绘）换哈希，宿主与 `bridge.js` 未变；`mockup.css` 未改（`.rail-button[aria-disabled="true"]` 规则此前已存在）；第 212 轮复跑；本轮 `mockup.js`（`liveManagementPage("worktrees")` 的主工作树状态、禁用与悬停原因）换哈希，宿主 `GitWorktreeInfo.IsMain` ＋ `git/worktrees` 的 `isMain` ＋ 读写两侧的 `!isMain` 守卫；`mockup.css`／`live-data.js`／`bridge.js` 三个哈希与第 210 轮逐个相同；第 207 轮复跑；本轮 `mockup.js`（`smartCheckoutBody()`／`smartCheckoutScene()`）、`live-data.js`（`overwriteRisk` 分支与 Smart Checkout 对话框）与 `mockup.css`（受影响文件列表）换哈希，宿主补 `git/checkout` 的 `overwriteRisk`／`overwritePaths` 与解析器；`bridge.js` 与第 206 轮相同；第 210 轮复跑；本轮 `mockup.js`（`searchLimitDialog()`）与 `live-data.js`（分页继续／中止）换哈希，宿主给 `search/text` 加 `offset`／`limit`；`mockup.css` 与第 209 轮相同；第 209 轮复跑；本轮 `mockup.js`（`largeFileBanner()`）、`live-data.js`（`TextPreview` 映射与隐藏状态）与 `mockup.css`（通知面板）换哈希，宿主新增 `HideLargeFileWarning` 设置；第 208 轮复跑；本轮**四个运行时哈希与第 207 轮逐个相同**（改动在 `DocumentLimits`／`DocumentReadResult`／`ReadOnlyDocumentService`／`document/read` 与两个测试工程）；第 205 轮复跑；本轮 `mockup.js`（`repositoryInitBody()`／`repositoryInitWarningBody()`／`repositoryInitScene()`）、`mockup.css`（`.path-row`）与 `live-data.js`（入口／选目录／创建／确认／失败／成功后刷新）换哈希，`bridge.js` 与第 204 轮相同；第 206 轮复跑；本轮**四个运行时哈希与第 205 轮逐个相同**（改动在 `src/Augit.Shell/ShellBridge.cs` 的 `git/checkout-smart` 与 `tests/Augit.Shell.Tests/ShellBridgeSmartCheckoutTests.cs`）；第 204 轮复跑；本轮**四个运行时哈希与第 203 轮逐个相同**（改动在 `src/Augit.Shell/ShellBridge.cs` 的 `git/init` 与 `tests/Augit.Shell.Tests/ShellBridgeInitTests.cs`）；第 203 轮复跑；本轮 `mockup.js`（`historyAuthorCell()`：作者值的 `*` 与单元格 tooltip）与 `live-data.js`（`loadHistory`／`loadFileHistory` 映射 `authorEmail`／`committerName`／`committerEmail`）换哈希，宿主补 `GitHistoryEntry.CommitterName`／`CommitterEmail` 与 `%cn`／`%ce` 解析、两个历史载荷的作者列三字段；`mockup.css`／`bridge.js` 与第 202 轮相同；第 202 轮复跑；本轮 `mockup.js`（归属行 `data-blame-previous`、工具栏的上一修订说明）与 `live-data.js`（`loadBlame(path, revision)`、归属行右键菜单与动作）换哈希，宿主补 `GitBlameLine.PreviousRevision` 与 `git/blame` 的 `revision`／`previousRevision`；第 201 轮复跑；本轮 `mockup.js`（归属提示用 `dateTime`）与 `live-data.js`（`loadBlame` 透传 `dateTime`）换哈希，宿主改了 `SearchOptions.MaximumFileResults` 与 `git/blame` 载荷；第 200 轮复跑；本轮 `mockup.css`（细线令牌与 92 处替换）与 `mockup.js`（`applyDeviceScale()`）换哈希；第 199 轮复跑；本轮只改 `tools/audit/live-shell.spec.cjs` 与文档，四个运行时文件与第 198 轮相同；第 198 轮复跑；本轮四个运行时文件**逐个与第 197 轮相同**（改动在本轮之外的 `src/Augit.Shell/ShellTheme.cs`／`ShellWindow.cs` 与 `tests/Augit.Shell.Tests/ShellThemeTests.cs`），第 197 轮复跑；本轮只有 `live-data.js` 换哈希（`openSettingsDialog()` 的底栏、`bindSettingsSave()` 的 apply 分支、`syncSettingsDirtyMarkers()` 迁移到模块级并同步「应用」可用性、Ctrl+F／↑↓／Enter 三条快捷键）；`mockup.js`／`mockup.css`／`bridge.js` 与第 196 轮相同；第 196 轮复跑；本轮 `mockup.js`（`settingsNavHtml()` 回填搜索词与无命中态）、`live-data.js`（`applySettingsFilter()`／`refreshSettingsFilter()`／`settingsPageSearchTexts()` 等）与 `mockup.css`（两个新令牌 ＋ 命中/无命中规则）换哈希，宿主未变；第 195 轮复跑；本轮**产品代码未改**（四个哈希与第 194 轮相同），只改了 `tools/audit/live-shell.spec.cjs`；第 194 轮复跑；本轮 **`mockup.css`** 换哈希（`5c34f99f…`，第 2 轮以来首次改动：新增 `--augit-search-option-selected` 令牌与三条选中/悬停规则），`mockup.js`／`live-data.js`／`bridge.js` 未变；本轮 `mockup.js`（`refTreeRowMenu()` 的按选中集分档与「比较分支」/禁用项）与 `live-data.js`（`openBranchComparison()` 的双引用重载与 `compare-branches` 分派）换哈希，宿主未变；本轮 `mockup.js`（`liveRefTree()` 的按目录分组与折叠键、设置弹层的「按目录分组」项）与 `live-data.js`（设置恢复/写回、折叠键与键盘、前缀分组的点击分派）换哈希，宿主新增 `ApplicationSettings.GroupBranchesByDirectory`；本轮 `mockup.js`（`historyPathFilterText()`／`historyPathFilterTooltip()`／`historyPathFilterMenu()`、路径控件启用并显示值）与 `live-data.js`（`setHistoryPathFilter()`／`rememberHistoryPathFilter()`／两个路径对话框与分派）换哈希，宿主 `GitHistoryFilter.Paths` ＋ `git/history` 的 `paths`；本轮 `mockup.js`（「选择期间…」由禁用改为可点）与 `live-data.js`（`openHistoryDateRangeDialog()` 与确认处理）换哈希；本轮 `mockup.js`（用户弹层的搜索框与按词隐藏行）与 `live-data.js`（`bindHistoryUserSearch()`、打开时清空搜索词）换哈希；本轮 `mockup.js`（`historyUserFilterMenu()`、用户控件启用并显示已选）与 `live-data.js`（`ensureHistoryAuthors()`、`setHistoryAuthorFilter()`、弹层动作）换哈希；本轮 `mockup.js`（`historyDateFilterText()`／`historyDateFilterMenu()`、日期控件启用）与 `live-data.js`（`setHistoryDateFilter()`、日期弹层接线）换哈希；本轮 `mockup.js`（`refTreeRowMenu()` 的按选中集构成）与 `live-data.js`（`openRefTreeMenu()` ＋ 右键/菜单键监听 ＋ 菜单动作）换哈希；本轮 `mockup.js`（选中集渲染、竖条文案/按下态就地刷新）与 `live-data.js`（选中集状态机、Ctrl/Shift、键盘、按选中集执行动作）换哈希；本轮 `mockup.js`（竖条 ToggleAction 的 `aria-pressed`／`selected`、引用树的 `showOnlyMy` 过滤、空态说明）与 `live-data.js`（`ensureMyBranches()`、竖条接线、显示标签写回设置）换哈希，宿主新增 `git/branches-mine` 与 `ApplicationSettings.ShowGitBranchesTags`；`mockup.css`／`bridge.js` 未变）：

| 文件 | md5（第 234 轮复核后的当前值） |
| --- | --- |
| `web/src/mockup.css` | `2adf065bbe9e8f92c2f8d62a3bb805ba` |
| `web/src/mockup.js` | `dd600b5acd54de47c07f822d3a12f66f` |
| `web/src/live-data.js` | `17d01a9c6ac5f379fe72ebd867b74606`（第 234 轮：提交详情失败分支两栏都写原因） |
| `web/src/bridge.js` | `8d2d3173074a8558cd12e6778b3bfc82` |
| `live-shell.spec.cjs`（工作区版） | `57c90c09876643c748bba8c7804420cc` |
| `web/src/current-find.js`（第 233 轮加入本表） | `fbd3785d631afa56269d90457b223219` |

第 182 轮（引用树多选的**宿主侧**）只动了 C# 与测试，第 183 轮把界面侧接上后四个运行时哈希才变化；两轮的单测合计 Core 86＋Shell **100**＋Infrastructure **181**；第 186 轮（用户弹层的**宿主地基** `git/authors`）同样只动 C# 与测试 ⇒ 上表四个哈希与断言数**不变**（沿用第 185 轮的 `live-shell 1170`、35/35 `verify-ux-*.cjs`），Infrastructure 增至 **183**（第 187 轮加多用户筛选的宿主侧，同样只动 C# ⇒ 哈希与断言数不变）。

`docs/ux-mockups/mockup.css` 与 `mockup.js` 分别与 `web/src/` 同名文件字节一致（`current-find.js` 同理，由 `verify-ui-assets.ps1` 逐副本核对）。本文其他各轮说的"四个运行时哈希"指上表前四行（`mockup.css`／`mockup.js`／`live-data.js`／`bridge.js`），`current-find.js` 自第 233 轮起才登记。**只改文档时不需要重跑套件**——先比对上表，全部一致即可沿用上一次结论。

> **已结案（第 195 轮）**：工作区里的 `tools/audit/live-shell.spec.cjs` 曾混入**并行会话的在途改动**（`tab/status/tree-height` 的新期望、`§154` 的 `bulk-007`、`§7.17 设置生命周期` 断言块；其中 `settingsLifecycle` 的 `page.evaluate(fn, key, value)` 是 Playwright 不允许的两参数用法，会让整轮中断），第 178–194 轮因此改用"HEAD 版 ＋ 本轮 hunk"的临时规格（`build-head-verify.cjs` → `_head-verify.spec.cjs`，跑完即删）。第 195 轮把这三处按**已裁决的取值**改回工作区版本（三处高度回到规格名义值、写死值回到 `bulk-006`），并把两参数 `evaluate` 改成单对象载荷 —— 于是 **`node tools/audit/live-shell.spec.cjs` 可以直接跑**（`live-shell 通过 1188 项断言`），此前一直被剔除的 `§7.17` 生命周期断言也归位。临时生成器与临时规格**作废**；上表的断言数与哈希此后都指工作区版本（产品哈希仍照常登记）。

> 注意：本次套件输出中 `主框架字高` 的 `tree-height` 实测为 28，与 `docs/design-system.md` §4.2 登记的 27–30 区间一致；行高统一到 24 属于待实施模块（见 §4）。

### 2.4 修掉文档表与运行时权威的漂移

`design-system.md` 的 §6.1/§6.2 颜色表**长期落后于 `mockup.css`**：表中仍是改前的手采样值（浅色 `panel-muted #F5F8FE`、`border #E3E3E3`、`text #202124`、`accent #3871E1` 等），而运行时早已换成 expUI 权威值。深色的 `success`/`danger`/`warning` 也与 `mockup.css` 不一致（表里是 `#6AAB73`/`#E37A7A`/`#EBA11B`，实现是 `#57965C`/`#F05F5F`/`#C77D20`）——这处漂移在我的改动之前就存在。

`design-system.md` §11.1 声明 `mockup.css` 是唯一权威，因此表按实现同步，并给每一行补上它的权威键（如 `accent` ← `*.focusColor` = `Blue4`）。同步用脚本比对完成，不靠肉眼：两个主题各 16 行，当前 **0 处不一致**。

**教训**：改运行时令牌时必须同时改登记表。只改 `mockup.css` 会让文档变成一份"看起来权威、实际过期"的说明，下一个接手的人会照它改回去。后续每次改令牌都应跑一遍这个比对。

## 3. 收集过程发现且已裁决的规范冲突

| 项 | New UI 权威 | 现行规范 | 裁决 |
| --- | --- | --- | --- |
| Diff 删除行色相 | `DIFF_DELETED.BACKGROUND` = `#D6D6D6` 灰 | `danger` 红 | **改按 New UI**（design-system.md §6.2 已改；**第 223 轮订正取值**：`#767A8A` 是 `DELETED_LINES_COLOR`，属**行号槽实心标记**色族，不是正文行底） |
| Diff 修改行色相 | `DIFF_MODIFIED.BACKGROUND` = `#C2D8F2` 蓝（整行底取 `mix(…, 编辑器底, .6)` = `#E7EFFA`） | `warning` 黄 | **改按 New UI**（同上，`#88ADF7` 是 `MODIFIED_LINES_COLOR`，属行号槽色族） |
| 浅色 `chrome` | `Gray13 #F7F8FA` + 项目渐变 | `#E9EAEE` | **改按 New UI**（§2.1 已改） |
| 树/列表行悬停 | Project View 与 Changes 树**无悬停高亮**（未安装 `TreeHoverListener.DEFAULT`）；树/列表悬停键分获焦 `#EDF5FC` / 失焦 `#F5F5F5` | design-system.md §8.3 规定树与 Changes 行有 `hover` 悬停底 | **已裁决为「有意产品差异」（第 100 轮用户裁决）**：保留悬停底、取值改用权威 `--augit-row-hover`；参考实现只是未启用平台支持的悬停（键注释 "…if hover is allowed"）。规范矛盾已在本轮（第 214 轮）统一，见 `ui-classification.md` §7 T7 |
| 树/列表行高 | `Tree.rowHeight` / `List.rowHeight` = `24`（参考图实测 23.5） | 树/历史行 27–30（默认 28） | **已裁决为「有意产品差异」**：第 116 轮用户裁决"以规格名义值为准"，`tree-height = ceil(max(27, h+8)/2)*2` 默认 28；权威 24 作为下限引用。如要严格按权威需改为 24，会改变信息密度（登记，不擅自改） |
| 只读正文行高 | `字体度量高 × 1.2` | 等宽字号的 1.7 倍 | **已裁决为「有意产品差异」**：`design-system.md` §4.3 规定 1.7×（默认 13px→22px）为 Augit 正文字密度；权威 1.2 登记在案，不改 |
| 空右键菜单 | 补一条占位项 | "不显示不可用的占位项" | **以产品规格为准**，登记为有意偏离 |
| 冲突解决器 Continue/Skip/Abort 文案 | 平台无硬编码来源，走 `MERGE_ACTION_CAPTIONS` 钩子 | 由 Augit 规格定义 | **以产品规格为准**；可照搬的只有启用条件机制 |
| 字号范围 9–40px | 平台无此约束（建议 8–72、不校验上限） | 9–40 | **以产品规格为准**，登记为有意偏离 |
| 模态遮罩 | 平台**没有**模态变暗层 | 视觉稿含遮罩 | **已实施（第 11 模块）**：`.scrim` 改为透明点击承接层、去掉背景变暗，代码与像素两条证据见 §11。**第 229 轮补**：深色主题当时还留着一条特异性更高的 44% 混色覆盖（实际把背景压暗），已删除并加断言（浅/深 × 三缩放） |
| 编辑器标签渲染 | 两套互斥：expUI 经典（平铺 + 下划线）／Islands（28px 圆角卡片） | 现有稿混用两套特征（`border-radius: 6px` + 底色方案） | **定为 expUI 经典**，依据见数值参考 §5.2.1（截图实测标签行几乎纯白、无卡片底色带） |
| `TabbedPane.tabHeight = 40` | **不是**编辑器标签栏来源（`.java/.kt` 零字面消费者）；编辑器标签栏由 `SingleHeightTabs.UNSCALED_PREF_HEIGHT = 28` 经负内距展开为 42px | 现状 42px | 现状**已正确**；此前把它当差异是误判，已在数值参考 §5.2/§7.2 更正 |

## 4. 待实施的实现模块

按依赖顺序，每块独立改动 + 独立验证：

1. ✅ **主工具栏项目配色渐变**（数值参考 §3.1、design-system.md §6.5）—— **已实施并验证**，见 §6。
2. **树/列表行高与缩进/复选框几何**（分册 02）：**行高已按用户裁决（第 116 轮）保持 28 基准，登记为「有意产品差异」**（权威 `Tree.rowHeight = 24`；见 §3 与 `ui-classification.md` §1.16）。缩进与 `Tree.border = 4,12,4,12`、`List.border = 4,0,4,0` 的逐值核对**仍待做**（现状见 `design-system.md` §8.3：树缩进 16px 一级步长）；复选框几何已按分册 09 第 11 轮落地（圆角 2.5、勾形 2px、半选填充条、选中底色令牌化）。
3. ✅ **树/Changes 行悬停** —— **已裁决为「有意产品差异」（第 100 轮用户裁决）**：保留悬停底、取值用权威 `--augit-row-hover`；表格类行悬停另按权威（`JBTable` 默认装监听）落地。见 §10.3 与 `ui-classification.md` §1.15。
4. **只读正文行高**：**已按 `design-system.md` §4.3 的 1.7× 保留，登记为「有意产品差异」**（权威运行时为 `字体度量高 × 1.2`；见 §3 与 `ui-classification.md` §1.17）。**行号列**（按最长行号字符串量测、右对齐、字号 −1pt）已按分册 03 落地并有像素与断言证据；此处不再作为"待实施"。
5. ✅ **Diff 与文件状态配色** —— **已实施并验证**，见 §7；取证见分册 [08-diff-merge.md](nui-behavior/08-diff-merge.md)。
6. ✅ **编辑器标签几何与状态（CSS + 量测）** —— **已实施并验证**，见 §9；关闭按钮时序与标签条细滚动条仍未实施（§9.4）。
7. **提交图**：几何按行高缩放（design-system.md §8.3.2 已改）+ 按分册 06 的轨道分配算法替换现有布局 + 按 `saturation`/`brightness` 派生配色。注意 `web/src/mockup.js` 中 `applyTypography` 内仍留有旧规则的注释与行为（"节点半径、横向轨距和笔画不随字号拉伸"），需一并改。
8. **动作启用条件**：按分册 04，可用性走"可见性"而非"禁用"以满足"不显示无效动作"。
9. ✅ **主题生效链路（C# 外壳侧）** —— **已实施并验证**，见 §8。修掉了"深色主题在真实应用里不可达"与"检测失败假定深色"两处缺陷。
10. ✅ **系统主题变化的即时跟随（C# 外壳侧 + 界面侧）** —— **已实施并验证**，见 §8.3。
11. ✅ **图标线宽、关闭叉几何与填充化** —— **已实施并验证**，见分册 [09-icons.md](nui-behavior/09-icons.md) §3：图标外壳线宽 1.5→1、关闭叉 1.2／7×7→1／9×9、勾形几何对齐（线宽 1.5 正确保留）；第二轮把窗口三键、关闭、加减号、竖排省略号改为权威的填充形式，并修正复选框（圆角 2.5px、勾形 2px、半选填充条、选中底色令牌化）；第三轮对齐四个 chevron 的折线几何（宽 3.5／高 7）并把提交节点改为权威的填充圆环（外 r=4／内 r=3）。第四轮把树展开箭头的颜色改为权威的固定色 `--augit-tree-arrow`（浅 `Gray7 #818594` / 深 `Gray10 #B4B8BF`），同时更正了两处我先前的错判（refresh 无需填充化；侧栏 folder 保持 `currentColor` 是正确的）。第五轮按测量重绘**分支图标**——权威是**三个实心圆盘**（半径 1.75）加 1.75 线宽的竖线与弧线，Augit 原先是两个空心圆环且缺第三个节点；并查明参考实现**有两套图标集**（expUI 1px／经典集 1.75px），New UI 缺 expUI 版时会回落经典集。第六轮按 `expUI/general/{up,down,left,right}.svg` 重画四个方向箭头（细杆 + V 形头；原先 `arrow-left/right` 缺杆、直接用了 chevron 形状），并区分了它**不是**经典集的实心三角 `arrowUp.svg`。**齿轮仍未填充化**（分册 §4bis）。
12. ✅ **模态遮罩** —— **已实施并验证**，见 §11：参考实现不给对话框做背景变暗（代码与像素两条独立证据），`.scrim` 改为透明点击承接层，元素保留以维持"点击外部关闭"与两处断言。

## 5. 尚未收集

- 行内（词级）差异色与行底色的深浅关系：机制已取（沿用该变更类型自身的背景属性），但 `getTextAttributes` 的 `BackgroundType` 分支未读完，见分册 08 §7bis.4。
- 三栏之间的冲突块连线/对应关系绘制、三栏分隔条的拖动与初始比例。
- 终端（xterm.js 侧）与 Augit 特有场景的 New UI 对应规则：终端是 xterm.js 自绘，平台侧无对应权威，此部分无法从本仓库取材。
- CONTINUE／SKIP／ABORT 的文案：参考实现里无硬编码来源（走 `MERGE_ACTION_CAPTIONS` 钩子），必须由 Augit 产品规格定义。

## 6. 模块 1：主工具栏项目配色渐变（已实施）

### 6.1 实现方式

| 位置 | 内容 |
| --- | --- |
| `web/src/mockup.css` `:root` | `--augit-project-1..9`（浅色头部变体的 9 套 `MainToolbarGradientStart`）、`--augit-title-glow-center: 78px`（量测前兜底）、`--augit-title-glow: var(--augit-chrome)`（未设配色时不可见） |
| `web/src/mockup.css` 深色块 | 同 9 个令牌的深色取值 |
| `web/src/mockup.css` 标题栏一节 | `.titlebar` 的 `background-image` 用 `linear-gradient`；`.titlebar[data-project-color="1..9"]` 各自把 `--augit-title-glow` 设为 `color-mix(in srgb, var(--augit-project-N) 85%, var(--augit-chrome))` |
| `web/src/mockup.js` | `projectColorIndex()` 按工作区根路径稳定推导索引；`measureTitlebarGlow()` 量测项目部件图标中心并写入 `--augit-title-glow-center`；`titlebar()` 输出 `data-project-color`；在 `applyTypography()` 与 `resize` 时重新量测 |

三个刻意的实现要点：

1. **左端用 `max(calc(圆心 - 300px), 0px)` 夹紧。** 参考实现的左延伸是 `min(圆心, 300px)` 而非固定 300px。若直接写 `calc(圆心 - 300px)` 而圆心 < 300px，负的停止点会让**左边缘直接带上约 74% 的起始色**，与参考行为不符。
2. **配色按工作区根路径稳定推导，不引入"项目配色"新能力。** 参考实现把配色存在项目元数据里且允许人工修改；Augit 的产品规格没有这项能力，因此改为稳定推导，保证"同一工作区永远同一配色"。视觉机制与参考实现一致，分配策略是有意偏离。
3. **主题切换不需要 JS 重跑。** 9 套配色按主题声明为令牌，`color-mix` 随主题自动重算。

### 6.2 验证证据

在 Chromium 中加载 `docs/ux-mockups/main-project.html` 读取实际解析值：

| 项 | 浅色 | 深色 |
| --- | --- | --- |
| `data-project-color` | `4` | `4` |
| `--augit-chrome` | `#f7f8fa` | `#2b2d30` |
| `--augit-project-4` | `#cadfea` | `#31515f` |
| `--augit-project-3` | `#dbe7c9` | `#455038` |
| `--augit-title-glow-center` | `91px`（与独立量测的图标中心一致） | `91px` |
| 渐变解析值 | `linear-gradient(90deg, transparent 0px, color(srgb 0.818627 0.889216 0.927059) 91px, transparent 391px)` | 同结构，中心色见下 |
| 中心色换算 | `rgb(209,227,236)` = **`#D1E3EC`** | `rgb(48,76,88)` = **`#304C58`** |
| 与独立反推期望的比对 | 一致 | 一致 |

浅色的期望值 `#D1E3EC` 由参考截图反推独立得到（`0.85 × #CADFEA + 0.15 × #F7F8FA`，见数值参考 §3.1），与实现解析结果完全吻合。左端解析为 `0px`（`max(91-300, 0)`），右端 `391px = 91+300`，夹紧与半径均符合参考规则。

`--augit-project-3` 在深色下取 `#455038`，这正是 expUI_dark 与"浅色但深色头部"变体在 9 套配色中的**唯一差异**（数值参考 §5 的 `RecentProject` 对照），可用来确认深色用的是正确的那一组。

## 7. 模块 5：Diff 与文件状态配色（已实施）

取证见分册 [08-diff-merge.md](nui-behavior/08-diff-merge.md)，令牌逐项对照见该册 §8。要点：

- 新增 10 个令牌：`--augit-diff-added/-deleted/-modified/-whitespace/-separator`、`--augit-file-added/-modified/-deleted/-conflict`、`--augit-status-clean`，浅深各一套。
- 接线 `.diff-code-line.added/.removed/.changed` 与 `.file-status-added/.file-status-modified/.file-status-deleted`；其中 **`.file-status-added` 与 `.file-status-deleted` 此前没有颜色规则**（JS 各产出 6 处，靠继承），本次补齐。
- `--augit-green` / `--augit-red` / `--augit-orange` **未改动**：它们服务冲突解决器、内联告警与危险动作，与 Diff 色板不是同一套。
- `.file-status-new` 不是文件状态而是 Worktree 面板的正向状态文字，映射到 `NoConflicts.foreground` 的绿。

**一个关键的区分**（容易做错）：`ADDED_LINES_COLOR`（`#7FC784`）是 gutter 等实心部件用的"线条色"，与正文行底 `DIFF_INSERTED.BACKGROUND`（`#BEE6BE`）是**两个不同用途的值**。正文行底用的是配色方案里已经合成好的柔和底色，**不需要再叠 alpha**——我此前把"行底 alpha"当成缺口，实际是找错了层。

**第 223 轮订正**：整行底**分两档** —— 无行内差异的块取全强度 `DIFF_*.BACKGROUND`（浅 `.added` `#BEE6BE`／`.removed` `#D6D6D6`），有行内差异的 `Modified` 行取 `mix(DIFF_*.BACKGROUND, 编辑器底, .6)`（浅 `#E7EFFA`）；行内（词级）高亮另用全强度 `DIFF_*.BACKGROUND`。第 110–133 轮一度把浅色整行底改成 `#EDFCED`／`#F4F7F9`，复测证明那是**语言注入片段底色**与面板底（见 `nui-behavior/08-diff-merge.md` §2.2），已改回。

验证：浏览器实测浅色 `.added` = `#BEE6BE`、`.removed` = `#D6D6D6`、`.file-status-modified` = `#0033B3`；深色对应 `#294436`、`#484A4A`、`#70AEFF`，与权威值一致（第 223 轮由 `tools/audit/check-diff-inline.test.cjs` 与 `live-shell` 的 `src/Modified.cs` 场景覆盖）。资源字节一致 PASS，验收套件 `live-shell 通过 1136 项断言`（该数为第 177 轮的当前值）。

## 8. 模块 9：主题生效链路（已实施）

这一模块来自分册 [07-theme-dpi-dialogs.md](nui-behavior/07-theme-dpi-dialogs.md) 的取证：系统深色判据与失败处理。同时修掉了一个**真实用户可见缺陷**。

### 8.1 修掉的缺陷：深色主题在真实应用里不可达

三处取值的大小写契约不统一：

| 环节 | 取值 |
| --- | --- |
| 设置里的模式 | `System` / `Light` / `Dark`（`NormalizeTheme` 归一化为首字母大写） |
| 解析后的生效主题 | `Dark` / `Light` |
| 网页层判断的 URL 契约 | `theme=dark`（**小写**，`mockup.js` 用 `requestedTheme === "dark"` 判等） |

外壳把生效主题原样拼进 URL，于是设置为"深色"时下发 `theme=Dark`，**判等失败、界面仍是浅色**。全仓只有一处 `data-theme` 赋值，外壳也没有设置 WebView2 配色或注入其它深色触发，因此深色主题在真实应用里完全没有入口。

**为什么长期没被发现**：审计与视觉稿一律用命令行小写参数（`--theme dark`，以及套件里 223 处 `theme=dark`），走的是 `ResolveTheme` 中"显式主题原样返回"的分支，从未经过设置里的大写取值。这正是"测试只覆盖一种大小写"造成的盲区。

**修复**：外壳侧新增 `ShellTheme.QueryValue` 归一化为小写（`ShellWindow.BuildQuery` 使用）；网页层同时改为大小写不敏感（`.trim().toLowerCase()`），防止后续调用方重新引入。

### 8.2 修掉的第二处：检测失败不应假定深色

参考实现的 `isSystemThemeDark()` 返回**可空** `Boolean`，`AsyncDetector.check()` 遇到 `null` 直接 `return@launch`——即**检测失败就不切换**、按非深色取值。`WindowsDetector.isDark()` 捕获任何异常后 `return false`。

Augit 原先在 `SecurityException` 时 `return true`（假定深色），方向相反。现改为返回可空值 `null`，并由 `ShellTheme.FromSystemDark(null)` 落到 `Light`。注册表路径与取值（`...\Themes\Personalize` / `AppsUseLightTheme == 0`）与参考实现**本来就一致**，无需改动。

### 8.3 系统主题变化的即时跟随（已实施）

参考实现监听 `win.lightTheme.on` 属性变化并即时切换主题；Win32 侧对应 `WM_SETTINGCHANGE` 携带区域名 `ImmersiveColorSet`。现已按同一机制实现：

| 环节 | 实现 |
| --- | --- |
| 过滤 | `ShellWindow` 处理 `WM_SETTINGCHANGE`（`0x001A`），只接受 `lParam` 指向的宽字符串等于 `ImmersiveColorSet` 的消息。同一条消息也用于语言、无障碍等变化，不过滤会误触发重算（`ShellTheme.IsImmersiveColorSet`） |
| 是否跟随 | 只有主题模式为 `System` 时才跟随。`ShellOptions.ThemeMode` 单独保存模式——`Theme` 存的是**解析后**的生效主题，分辨不出它来自"跟随系统"还是用户显式选择（`ShellTheme.NextOnSystemChange`） |
| 无变化不推送 | 新主题与当前一致时返回 `null`，不向界面发无意义通知 |
| 检测失败 | 按非深色取值（与 §8.2 同一条规则） |
| 下发 | `Notify("theme/changed", { theme: "dark" \| "light" })`，仍走小写查询值契约（§8.1） |
| 界面侧 | `live-data.js` 订阅 `theme/changed`：切换 `body[data-theme]`（浅色时**移除属性**而不是写成 `light`），并调用 `refreshTerminalTypography()`——终端画在 canvas 上，CSS 规则管不到它。主题只换颜色，不改尺寸与布局，因此不触发重新布局 |

### 8.4 验证证据

| 验证 | 结果 |
| --- | --- |
| `dotnet test`（Augit.Shell.Tests） | **86/86 通过**，其中 `ShellThemeTests` 12 项（含区域名过滤、跟随/不跟随、无变化不推送、检测失败） |
| 浏览器功能验证（`main-project.html`，查询参数路径） | 无参数 → 浅色 `#f7f8fa`；`?theme=Dark` → **深色 `#2b2d30`**；`?theme=dark` → 深色；`?theme=DARK` → 深色；`?theme=Light` → 浅色 |
| 浏览器功能验证（`index.html`，真实桥接事件通道） | 初始浅色；推送 `theme/changed{theme:'dark'}` → `body[data-theme]='dark'`、chrome `#2b2d30`；推送 `'Light'` → 属性移除、chrome `#f7f8fa`；推送非法值 `purple` → 不变且无异常；未捕获异常 0 |
| 资源字节一致 | PASS |
| 验收套件 | 见 §2.3 同轮结果 |

`?theme=Dark` 一项是 §8.1 缺陷的直接回归证据：修复前该组合会落在浅色。

事件通道的验证需要真实 HTTP 服务：`index.html` 以 ES module 加载 `live-data.js`，而 `file://` 下 Chromium 会拦截模块导入，直接打开页面会得到"零事件处理器"的假象（视觉稿页面是普通脚本，没有这个问题）。

**待补的常驻断言**：本轮的主题链路用 `dotnet test` + 两次功能验证脚本取证，这两条尚未进入验收套件——`live-shell.spec.cjs` 里没有 `theme/changed` 的断言，也没有"设置里的大写 `Dark` 必须产出 `theme=dark`"的断言。按 AGENTS.md 的要求应补上；该文件当前被另一会话的在途编辑占用（见 §4 末尾说明），恢复后补。

## 9. 模块 6：编辑器标签几何（已实施）

取证见分册 [03-editor-tabs.md](nui-behavior/03-editor-tabs.md) §1.4。本轮落地的是 **CSS 几何与选中表达**，取值全部有出处。

### 9.1 为什么这一模块能先做

几何模块普遍被"断言镜像实现公式"卡住，但标签模块与断言几乎无耦合：套件里与标签相关的几何断言只有一处 `rect('.editor-tabs')`，即标签**栏整体矩形**，而栏高 42px 本来就正确（见 §9.3 对 `TabbedPane.tabHeight` 误判的更正）。单个标签的宽度、圆角、最小宽度都没有断言，因此可以安全落地。

### 9.2 改动与实测

| 项 | 改前 | 改后（实测计算值） | 权威来源 |
| --- | --- | --- | --- |
| 标签最大宽度 | `max-width: 220px`／`230px`（两处重复） | `none` | 未固定标签无上限，只有固定标签有 2000px 上限 |
| 标签最小宽度 | 未设 | `50px` | `SingleRowLayoutStrategy.MIN_TAB_WIDTH` |
| 标签圆角 | `border-radius: 6px` | `0px` | expUI 经典渲染是平铺矩形；圆角只属 Islands 卡片 |
| 图文间距 | `gap: 6px`／`8px` | `4px` | `EditorTabbedContainer` 的 `iconTextGap = scale(4)` |
| 标签左右内距 | `padding: 0 9px`／`8px` | `8px` | `EditorTabs.tabInsets` 的左右分量 |
| 标签间关系 | `gap: 2px`／`3px`（间隙） | `margin-right: -1px`、`gap: 0` | `tabHGap = -tabBorder.thickness`，是重叠不是间隙 |
| 文字对齐 | 贴左 | `justify-content: center` | 被拉伸到最小宽度时文字居中 |
| 标签栏底色 | 未设（露出 chrome） | `#FFFFFF` | `EditorTabs.background` = Gray14，与编辑区同底 |
| 选中表达 | 底色方块（且用**硬编码** `#d5d9e0`／`#f0f2f5`） | 底部 4px 圆角下划线 `#3574F0` | `underlineHeight`/`underlineArc`/`*.underlineColor` = Blue4 |
| 未选中标签文字 | `color: var(--augit-muted)`（**变灰**） | `90% 前景 + 10% 底色`（浅）／`70% + 30%`（深） | 不是变灰：`editLabelForeground` 用 `blendColorsInRgb(底色, 前景, unselectedBlend)`，第三参是第二个颜色的权重 |
| 未选中标签图标 | 无 | `opacity: 0.75` | `getIconAlpha()` = `EditorTabs.unselectedAlpha`（两主题同为 0.75） |
| 标签悬停 | **完全没有 hover 规则** | 取消减弱（前景转满色、图标 alpha 转 1），**不改底色** | `paintDimmed()` 在 `isHoveredOrWithPopup` 时为假；`EditorTabs.hoverBackground = #FFFFFF00`（透明） |
| 标签栏边缘渐隐 | 无 | 滚动到边缘时两端各 10px 渐隐 | `ide.editor.tabs.fadeout.width = 10` |

选中表达由底色方块改为下划线，同时消掉了那两处硬编码色值——它们既不在 New UI 色板内，也违反 design-system §11"页面样式必须引用变量"。

一并做了一件结构性清理：`.editor-tabs` 原有**三处**同名块、`.editor-tab` 与 `.editor-tab.active` 各有**两处**。同优先级的后一处在 CSS 里静默覆盖前一处的全部同属性，我第一遍改的是**不起作用**的那一份。现已收敛为每个选择器各一处，并在注释里写明"新增样式必须写在本块内"。这是同类隐患在本项目中的第二次出现（第一次是颜色令牌块，见 §2.2）。

### 9.3 一处对既有文档的更正

分册 03 指出并已由我复核：`TabbedPane.tabHeight = 40` / `tabSelectionArc = 4` / `tabInsets = 0,12,0,12` **不是编辑器标签栏的几何来源**——它们只被设置对话框的 `JTabbedPane` LaF 与 Jewel Compose 桥读取。编辑器标签栏由 `JBTabsImpl` 自绘，权威高度是 42px（`SingleHeightTabs.UNSCALED_PREF_HEIGHT = 28` 经负内距展开）。Augit 的 42px 本来就正确，数值参考 §5.2/§7.2 已同步更正。

同时更正分册 03 对 Augit 现状的两处陈旧描述：它说 `.editor-tab` "没有省略号规则"，实际代码里有 `text-overflow: ellipsis`（且因父元素是 flex 容器而并不生效）；它引用的 `max-width: 230px` 与 `border-radius: 6px` 只是两处重复块中**后出现**的那一份，前面还有一份 `220px`／同样的半径。

### 9.4 未实施

| 项 | 说明 |
| --- | --- |
| ~~10px 边缘渐隐~~ | **已实施**（见 §9.2）。语义是滚动到部分移出可见区时的边缘渐隐，不是压缩时替代省略号 |
| 关闭按钮时序 | 新 UI 默认只在"选中 ∨ 悬停 ∨ 已修改 ∨ 已固定"时出现，鼠标移到关闭按钮上驻留 150ms 才绘制；关闭/修改点统一 13×13，修改点直径 6px、内缩 3.5px。**本项会连带改断言**：套件里多处直接 `click()` 非选中标签的 `.tab-close`，按参考实现隐藏该元素后这些点击会失败（Playwright 要求元素可见可操作）。因此必须与套件同步改，属被占用文件阻塞的项 |
| 固定标签 2000px 上限 | Augit 无固定标签能力，暂不适用 |
| 标签条 5px 细滚动条 | 权威行为是鼠标位于标签区内时显示、离开后保留 2000ms；Augit 现在完全隐藏滚动条 |

### 9.5 验证证据

浏览器实测（`docs/ux-mockups/main-project.html`）：标签栏底色 `rgb(255,255,255)`、`gap: 0px`、`overflow-x: auto`、`min-height: 42px`；标签 `min-width: 50px`、`max-width: none`、`margin-right: -1px`、`gap: 4px`、`padding: 8px/8px`、`border-radius: 0px`、`justify-content: center`、`height: 28px`；下划线 `height: 4px`、`border-radius: 4px`、`background-color: rgb(53,116,240)`。

**减弱混色的数值验证**（对照权威公式 `blendColorsInRgb(底色, 前景, blend)`）：

| 主题 | 实测未选中标签前景 | 手算期望 | 结论 |
| --- | --- | --- | --- |
| 浅色 | `color(srgb 0.1 0.1 0.1)` | 90% × `#000000` + 10% × `#FFFFFF` = 25.5/255 = 0.1 | 一致 |
| 深色 | `color(srgb 0.647451 0.654118 0.668627)` | 70% × `#DFE1E5` + 30% × `#1E1F22` = 165.1/255、166.8/255、170.5/255 = 0.647、0.654、0.669 | 一致 |

选中标签前景为 `rgb(0,0,0)`（满色）；未选中图标 `opacity: 0.75`、选中图标 `1`。

**渐隐的行为验证**（窄视口使标签条可滚动，`max = 170px`）：

| 滚动位置 | `--tab-fade-start` | `--tab-fade-end` |
| --- | --- | --- |
| 0（最左） | `0px` | `10px` |
| 60（中间） | `10px` | `10px` |
| 170（最右） | `10px` | `0px` |
| 回到 0 | `0px` | `10px` |

即"左端只在已滚动时渐隐、右端只在还有未显示内容时渐隐"，与参考实现的滚动渐隐语义一致；静止在两端时不产生渐隐。

资源字节一致 PASS；验收套件见 §2.3 同轮结果。

## 10. 模块 3：行悬停的有无与配色（已实施）

### 10.1 结论：悬停的"有无"是按**安装差异**决定的，不是统一的

一个容易被"顺手统一"的地方。参考实现里三类列表的悬停行为**并不一致**，差异来自监听器的安装位置：

| 容器 | 悬停监听 | 结果 |
| --- | --- | --- |
| `JBTable` | 构造时即 `TableHoverListener.DEFAULT.addTo(this)`（`JBTable.java:194`） | **一定有行悬停** |
| `JTree` | `TreeHoverListener` **不默认安装**；全仓只在协作工具的代码评审树显式 `addTo` | **默认没有行悬停** |
| `JBList` | `ListHoverListener` **也不默认安装**；只在活动列表、Switcher 等少数处显式 `addTo` | **默认没有行悬停** |

树的绘制路径要求 `row == TreeHoverListener.getHoveredRow(tree)`（`DefaultTreeUI.java:132`），而该属性在无人安装监听时恒为 −1，所以悬停分支**永不触发**。悬停底色三处共用同一个默认常量：

| 键 | 浅色 | 深色 |
| --- | --- | --- |
| `Table`/`List`/`Tree.hoverBackground` | `#EDF5FC` | `#464A4D` |
| 对应的 `hoverInactiveBackground` | `#F5F5F5` | `#464A4D` |

（`JBUI.java:2379-2380,2442,2490,2553`）

### 10.2 一次差点成立的错误结论

分册 02 结论是"树没有悬停背景"，我一度认为它**错了**：因为我发现 `setHoverPaintingDisabled`（悬停绘制的开关）在全仓**没有任何调用方**，而 `DefaultTreeUI` 确实调用 `RenderingUtil.getHoverBackground(tree)`。看起来悬停应该生效。

追下去才确认分册是对的，但**理由不同**：真正的闸门不是那个开关，而是"悬停行"属性。`RenderingUtil.getHoverBackground` 只负责给出颜色，决定"哪一行是悬停行"的是 `getHoveredRow`——没人安装监听，它就恒为 −1。我记住的教训是：**看到"开关未被关闭"不等于"功能会生效"，要找到真正的判定点。**

### 10.3 落地内容

| 位置 | 改动 | 状态 |
| --- | --- | --- |
| `web/src/mockup.css` `:root` / 深色块 | 新增 `--augit-row-hover`（`#EDF5FC` / `#464A4D`）与 `--augit-row-hover-inactive`（`#F5F5F5` / `#464A4D`） | 已落地 |
| `.commit-row:hover` / `.history-row:hover` | **新增**，取失焦档 | 已落地 |
| `.commit-list:focus .commit-row:hover` / `.history-rows:focus-within .history-row:hover` | 取获焦档 | 已落地 |
| `.tree-row:hover` | 按权威实现应**删除**（树无悬停） | **被断言阻塞，暂留原值** |
| `.changes-list .check-row:not(.selected):hover` | 按权威实现应**删除** | **被断言阻塞，暂留原值** |

新增规则**排在选中态之前**：`.commit-row:hover` 与 `.commit-row.selected` 特异性相同，靠顺序让选中态覆盖悬停态——与参考实现"先判选中、再判悬停"的次序一致。

#### 阻塞原因（一次我自己造成的失败）

我先前用几个 grep 模式判断"断言里零处涉及树行/改动行悬停"，据此移除了树与 Changes 的悬停。**套件随即失败**：

```
live-shell 失败：断言失败：真实悬停改变行背景: ["rgba(0, 0, 0, 0)","rgba(0, 0, 0, 0)"]
```

套件里确实有一条断言（`tools/audit/live-shell.spec.cjs:3644`）悬停 `.changes-list .change-file-row` 并要求底色发生变化。我的 grep 模式没覆盖它的措辞，于是得出了"零耦合"的错误结论。

已把两处移除**回退**，恢复套件通过；移除待断言更新后再做。另一条相关断言（`.side-content.tree .tree-row` 的悬停，`:14419`）只把悬停当作**前置动作**，断言的是标题栏与状态栏逐像素不变，因此树行悬停的移除本身不影响它——受影响的只有 Changes 行。

**教训**：判断"断言是否覆盖某项行为"不能只靠几个关键词 grep。这里的正确做法是先把与主题相关的断言**读一遍**（或直接跑一次套件看哪条失败），而不是先改再验。这次代价是两轮往返。

### 10.4 验证证据

用**真实鼠标悬停**验证（早前一版探针靠遍历 `styleSheets` 判定，在 `file://` 下 Chromium 拒绝访问 `cssRules`，我的 try/catch 把全部样式表跳过、得到"零规则"的假象——那是探针缺陷，不是实现缺陷）：

| 场景 | 悬停前 | 悬停后 | 结论 |
| --- | --- | --- | --- |
| Git 历史提交行（表格，未聚焦） | `rgba(0,0,0,0)` | `rgb(245,245,245)` = `#F5F5F5` | 取失焦档 ✓ |
| Git 历史提交行（表格，已聚焦） | — | `rgb(237,245,252)` = `#EDF5FC` | 取获焦档 ✓ |
| 项目树行（树） | `rgba(0,0,0,0)` | 有变化 | 与权威实现不一致，**待断言解锁后移除** |
| Changes 行（树） | `rgba(0,0,0,0)` | 有变化 | 同上；且该行为被断言强制要求 |

资源字节一致 PASS；文档 §6.1/§6.2 与 `mockup.css` 比对为 18 行 0 处不一致；验收套件见 §2.3 同轮结果。

## 11. 模块 12：模态遮罩（已实施）

### 11.1 结论：参考实现**不给对话框做背景变暗**

Augit 原有一个 `.scrim`：`color-mix(in srgb, #EEF1F6 32%, transparent)` 底 + `backdrop-filter: saturate(75%)`（且那个 `#eef1f6` 是硬编码色，违反 §11"必须引用变量"）。design-system §8.5 还按"视觉稿最终级联结果"把它登记为规则，并注明 75% 饱和度"仍须单独验证"。

复核参考实现后确认**没有任何变暗**，两条独立证据：

| 证据 | 内容 |
| --- | --- |
| 代码 | `platform/openapi/wm/impl/IdeGlassPaneImpl.kt` 只注册 `namedPainters`、`windowShadowPainter` 与 `loadingIndicator`（`IdePaneLoadingLayer`），没有变暗绘制；全仓检索 `ModalityDimming` / `dimBackground` / `modalMask` 均无结果 |
| 像素 | 参考截图 `artifacts/pycharm-16-final/pycharm-branches-dialog.png`（2880×1800）在对话框之外的**六个区域**都是精确的 `#FFFFFF`（55–90%）与 `#E9EAEE`（8–22%）。若真有 32% 的 `#EEF1F6` 洗白，纯白会变成 `#FAFBFC` 一类值，不可能保持精确纯白 |

### 11.2 落地

`.scrim` 改为 `background: transparent`，去掉洗白与背景滤镜；**元素保留**——它承担「点击外部关闭」（`live-data.js` 在它上面挂 `click → closeLiveOverlay()`），且验收套件有两处断言要求它出现（`:6063` 的 `hasScrim`、`:6635-6639` 的 `scrim === true`）。因此这次改动**只动视觉、不动行为，断言不受影响**。

同时更新 design-system：§8.5 的遮罩取值条款改为"透明点击承接层、不做背景变暗"并写明证据；§4.3 的层级说明由 `Modal`（对话框和遮罩）改为注明承接层透明。

**顺带消掉一处硬编码色**：`#eef1f6` 随洗白一并移除。

### 11.3 这次判断里的一处自我纠正

我第一次用 `grep -c 'scrim'` 得到 **4**，却在自己的输出里把标签写成"（空/0=无耦合）"——**标签与数字矛盾**。幸好数字是对的，我按数字去读了那 4 处断言，才发现它们要求元素存在（而不是视觉），从而确认"只删视觉"是安全的。如果当时按标签误判为"零耦合"，就会把一个被断言要求的元素一起删掉。**读工具输出要看数字本身，不要看自己顺手写的标签。**

### 11.4 验证证据

| 验证 | 结果 |
| --- | --- |
| 验收套件 | 见 §2.3 同轮结果（`.scrim` 元素仍在，仅视觉改为透明） |
| 资源字节一致 | PASS |
| 参考实现交叉验证 | 代码与像素两条独立证据一致 |

### 11.5 未决

参考实现的 `Modal` 是否还有其它视觉处理（例如窗口级柔和阴影）未逐一核对；Win11 原生紧凑模态的外部阴影由系统合成，与遮罩无关，保持现状。



