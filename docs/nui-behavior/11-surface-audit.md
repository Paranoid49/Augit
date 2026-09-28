# 11 · 界面表面审计：Augit 现有功能 ↔ New UI 权威

> **状态（第 307 轮，2026-09-28）：本审计的作业面已全部收口。** 下表的"估轮"是第 133 轮的历史估计，**不再代表当前进度**：§3 的 11 个区已逐区采集并落地（修订记录里有每一轮的收口条目），`10-backlog.md` 已清零、`ui-classification.md` §7 待处理表已清零、§2.10 只剩一行**已获用户认可的「不适用」**（§7.9 #23 比较对话框）。本文件此后只作历史审计记录。

本文件回答一个此前一直模糊的问题：**「还要多少轮」**。做法是把 Augit**现有的界面表面**逐个摊开，对每个表面问三件事，然后把"没有权威依据"和"有权威但未采集"的格子数出来。

## 0. 口径

| 判定 | 含义 |
| --- | --- |
| **已采** | 该表面的**交互/状态机/键盘/启用条件/布局/主题响应**已在 `0*.md` 里有带文件行号的权威出处 |
| **有权威·未采** | 能在 checkout 里定位到对应组件/类，但**交互规则还没采集进 `0*.md`** |
| **权威待定位** | 连"对应哪个组件"都还没找到（backlog 一 那类） |
| **已对齐 / 待改** | 采集之后，Augit 实现是否需要按权威改 |
| **⚠️ 边界** | 采集到的东西若等于"新增功能"，则**不实施**（用户裁决），只登记 |

**本审计只覆盖"界面与交互对齐"**，不把"New UI 有什么功能"当作待办清单——Augit 的功能集合不增不减。

## 1. 现有资产（审计的底表）

| 资产 | 数量 | 位置 |
| --- | --- | --- |
| 界面场景（视觉稿＝运行时来源） | **56** | `docs/ux-mockups/*.html` |
| 分模块检查器 | **35**（全绿） | `tools/verify-ux-*.cjs` |
| 行为文档 | **8** + 图标 + backlog | `docs/nui-behavior/0[1-8]*.md` |
| 状态覆盖矩阵（默认/空/错/加载/禁用/危险） | 5 组，§5 补图清单基本结案 | `docs/baselines/state-coverage-matrix.md` |
| 三方覆盖清单（PyCharm × 视觉稿 × 规格） | 设置 + 主窗口 | `docs/baselines/mockup-gap-inventory.md` |
| 交互序列 | `sequences` | `docs/baselines/pycharm-interactions.json` |

**结论**：**状态**覆盖（空/错/加载/禁用/危险）这一维度此前已基本做完；本轮审计暴露的真正缺口是**交互与行为权威的采集面**。

## 2. 已覆盖区（8 份行为文档）

| 区 | 代表场景 | 文档 | 权威锚点示例 |
| --- | --- | --- | --- |
| 工具窗口与主窗口框架 | `main-project`、`project-context-menu` | `01` | `ToolWindowDescriptor`、`MainToolbar.*`、strip 几何 |
| 树与列表交互/选择状态机 | 项目树、历史列表 | `02` | `Tree.Selection.arc`、`WideSelectionListUI`、type-ahead |
| 编辑器标签与只读正文 | `text-viewer`、`markdown-preview`、`json-preview` | `03` | `EditorTabs.*`、`IslandsTabPainter` |
| 动作系统/启用条件/图标状态 | 全局 | `04` | `AnAction.update()`、`Presentation`、`HIDE_DISABLED_CHILDREN` |
| 查找与搜索 | `text-viewer` 的查找条 | `05` | 查找条几何与输入规则 |
| 提交图 | `git-history-graph` | `06` | 轨道分配算法、边类型、配色 id |
| 主题切换/DPI/弹层与对话框状态机 | 全局 | `07` | `JBUIScale`、`UIUtil`、Popup 生命周期 |
| Diff 与合并 | `commit-diff`、`git-compare*`、`conflict-*`、`diff-*` | `08` | `TextDiffTypeImpl`、`DiffDrawUtil`、`ThreesideMergeHighlighters` |

## 3. 未覆盖区（本次审计的核心产出）

判定方式：在 `0[1-8]*.md` 里搜该区的**类名与场景名**，零命中即记为未覆盖（可复跑）。

| # | 区 | 场景 | 权威线索（已定位） | 权威待定位 | 缺口内容 | 估轮 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | **Changes / 提交流程** | `commit-changes`、`commit-empty`、`changes-context-menu` | `vcs-impl/.../changes/ui/CommitDialogChangesBrowser.java`、`.../configurable/CommitDialogConfigurable.kt` | — | 提交的启用条件、勾选与 Amend 的交互、右键菜单项启用/隐藏规则、提交信息校验时序 | 1–2 |
| 2 | **Git 写操作对话框**（第 149 轮**收口**） | `push`、`push-no-remote`、`remote`、`stash`、`stash-manager`、`stash-drop-confirm`、`clone`、`worktrees`、`branches`、`smart-checkout`、`reset`、`rollback` | `VcsPushDialog.java`、`GitStashDialog.kt`、`GitUnstashAsDialog.kt`、`GitResetDialog.java`、`RollbackAction`／`RollbackChangesDialog`、`DvcsCloneDialogComponent`＋`GitShallowCloneViewModel`、`GitWorkingTreeDialog`／`RemoveWorkingTreeAction` | 创建仓库、Smart Checkout（**两者均已结案**：创建仓库第 204–205 轮、Smart Checkout 第 206–207 轮，见 §三·补 第 1／2 项） | **已采完**：`13-git-dialogs.md` §1–§9 | **0**（剩两项 C# 接线，已在 backlog §三·补 单列） |
| 3 | **文件历史与 Blame**（第 150–151 轮**收口**，第 202 轮补动作家族） | `file-history`、`blame` | `GitFileAnnotation.java`（归属列默认值、`getToolTip`、`GitPreviousFileRevisionProvider` 的 `previous <sha> <file>`）、`AnnotateToggleAction.java:272-276`＋`AnnotatePreviousRevisionAction.java`（注释槽动作组）、`FileHistoryPanelImpl.createColumnList`（`:292-306` 列集合与顺序）、`GitHistoryProvider.isDateOmittable`（`:75-78`）、`AnnotationTooltipBuilder.java`（提示换行规则）、`DarculaTableHeaderUI.java:115`（表头高 25） | — | **已采完且已落地**：`14-file-history-blame.md` §1（Blame，含 §1.4「标注上一修订」）＋§2（文件历史列表）：归属行悬停提示、四列列集合与顺序、表头行、归属行的上一修订动作 | **0**（第 201 轮已补 tooltip 的 `Date:` 时间、第 202 轮已补「标注上一修订」、第 203 轮已补作者列的 `*` 与单元格 tooltip ⇒ 本区**清零**） |
| 4 | **设置** | `settings`、`settings-dirty`、`settings-save-failure` | `SettingsDialog.java`、`SettingsDialogFactory.kt`、`SettingsFilter.kt`、`SettingsSearch.java`、`ConfigurableController.java`、`ConfigurableMarkerProvider.java`、`GlassPanel.java`、`SpotlightPainter.kt` | — | 切页时的草稿语义、搜索过滤规则、脏标记时机、取消/应用的状态回滚 | **1**（第 196 轮已落地搜索侧：选项级命中＋100 ms 去抖＋无命中变红＋spotlight 边框＋ESC 清空，及分类名命中的保留；**剩**：`isResetActionEnabled()`／`myResetAllAction`（恢复默认，主对话框 `isResetButtonNeeded = false` ⇒ 预期登记为差异）与 `ConfigurableEditorBanner` 的适用性核对；第 197 轮已落地底栏「取消／应用／确定」与 `Ctrl+F`／`↑↓`／`Enter` 三条快捷键） |
| 5 | **内置终端**（第 157 轮：键位侧落地，标签侧**无本地权威**） | `terminal`、`terminal-close` | `platform/execution-impl/src/com/intellij/terminal/`：`TerminalEscapeKeyListener.java`（Esc→编辑器并消费）、`TerminalUiSettingsManager.kt`（光标/补全/字号来源）、`TerminalTitle.kt`（标题状态机） | 标签生命周期／关闭确认／重命名：**权威自己写明已搬到 `org.jetbrains.plugins.terminal`**（`session/TerminalSession.kt` 的 `@Deprecated` 文本），该插件不在本 checkout | **已采完**：`19-terminal.md`；Esc 语义已落地（终端里 Esc 回正文、不送 pty） | **0（可对标部分）**，标签侧无可比权威、以 Augit 规格与实现为准 |
| 6 | **图片与不可预览**（第 156 轮：**一半采到、一半阻塞**） | `image-preview`、`image-error`、`file-limit` | `FileSizeLimit.kt`（三档按扩展名的限制）、`FileUtilRt.java:1091-1101`（20 MB／2500 KB 默认值）、两个 `LargeFileNotificationProvider`（只读预览警告） | **图像查看器**：三次独立检索确认**不在本 checkout**；**第 211 轮裁决：无本地权威**，该场景只按内部一致性（视觉稿＝运行时基线）维护（详见 `18-file-limit-image.md` §2） | **不可预览侧已采完且已落地**（宿主第 208 轮三档限制＋只读预览、界面第 209 轮 Warning 横幅与隐藏／不再显示）→ `18-file-limit-image.md` §1；图片侧**无本地权威（第 211 轮裁决）**、按内部一致性维护 | **0（不可预览）／阻塞（图片）** |
| 7 | **快速打开 / 跳转行**（第 152–153 轮**收口**） | `quick-open`、`quick-open-empty`、`go-to-line`、`search-limited` | `GotoLineNumberDialog.java`／`GotoLineAction.java`／`EditorGotoLineNumberDialog.java`（跳转行）；`GotoFileAction.java` ＋ `SearchEverywhereUI.java`／`MixedSearchListModel.java`（快速打开） | — | **已采完且已落地**：`15-quickopen-gotoline.md` §1（跳转行）+ §2（快速打开：进行中状态、结果不回写输入框） | **0**（结果上限已于第 201 轮按权威改为 30） |
| 8 | **操作进度与结果**（第 154 轮**收口**） | `operation-progress`、`operation-result` | `ProgressWindow.java`／`ProgressDialog.kt`／`ProgressDialogUI.kt`（进度窗口与取消语义）、`ProcessBalloon.kt`（结果气泡生命周期）、`StatusBarProgress.java` | — | **已采完**：`16-operation-progress.md` —— 取消一次性语义已落地，重复触发抑制／进行中禁用／提示不自动消失**本来就对齐** | **0**（进度条属尚未接线的 Smart Checkout，见该册 §3） |
| 9 | **仓库初始化与全仓搜索**（第 155 轮：搜索侧**收口**，初始化侧采集完成） | `repository-init`、`repository-search`、`git-unavailable`、`search-limited` | `GitInit.java`（初始化）、`UsageLimitUtil.java`＋`intellij.platform.ide.impl.xml:1491`（1000 条阈值与"是否继续"）、`ScrollingUtil.java`＋`SearchEverywhereUI.java:906-907`（键转发）、`SearchEverywhereManagerImpl.java:407-411`（SHORT 视图 pack） | — | **已采完**：`17-repository-init-search.md`；搜索侧经核对**本来就对齐**（阈值 1000、键转发、空态高度），到限后"Continue/Abort"（第 210 轮落地）与初始化界面契约（第 204–205 轮落地）已登记 | **0**（第 159 轮结案：弹层表面是**扁平行 + 按文件路径排序**，Augit 已对齐 ⇒ §三·补三 撤销；另两项属宿主能力：仓库初始化 §三·补 第 1 项（**第 204–205 轮已接线并实时化**）；到限后继续搜索 §三·补 第 7 项 **第 210 轮已落地**） |
| 10 | **冲突列表**（第 158 轮**收口**） | `conflict-list` | `08-diff-merge.md` §7bis（渲染侧）＋§7bis.2／§7bis.7（可用性判据） | — | **已采完**：三动作的可用性由 Git 操作状态决定；`live-shell` 已断言"Continue 保留并禁用且说明原因"与"`supportsContinue=false && canAbort/canSkip=false` ⇒ 三者都不渲染"（等于权威的"操作不存在就不显示"） | **0** |
| 11 | **C# 外壳侧** | 无对应 html | `07` 覆盖平台级主题/DPI | 窗口 chrome、`ShellTheme.cs`/`ShellSystemTheme.cs` 的落地 | 外壳与网页层的职责边界、DPI 传递、主题事件时序 | **1**（第 198 轮已落地主题边界：WebView2 表面色随主题（导航前设定＋`theme/changed` 同步）＋按 `07` §1.3 建议补浅/深几何不变性断言；**已落地**：第 199 轮钉住 DPI 契约（2× 下逻辑像素不变 ＋ 行高吸附整设备像素）、第 200 轮落地细线令牌 `--augit-hairline`（`round(dpr)/dpr`，92 处；**登记差异**：Chromium 把 `border-*`/`outline` 宽度取整到整数 CSS 像素，令牌只对 `height`/`width` 与 `box-shadow` 生效）） |

**合计：约 12–21 轮**（第 133 轮的历史估计；每个区的"估轮"含：定位权威 → 采集进 `0*.md` → 按权威改实现/断言 → 分模块验证）。**第 307 轮核查时该估计已用尽并结清**，见文件头状态说明。

> **进展（第 135 轮）：第 1 区首片已采集 → `12-commit-changes.md`。**
> 已定死的权威：启用判据 `hasDiffs() && !isExecuting()`（`CommitChangeListDialog.java:602-604,616-624`）、**空提交信息走"确认"而非阻断**（`SingleChangeListCommitWorkflowHandler.kt:117-122`）、重算时机（inclusion 监听 `:351`、执行开始/结束 `:292/:295`、300ms 去抖 `:622`）、executor 判据（`:115`、`:799-803`）。
> **并已发现一处待改的真实交互分歧**：Augit 把"空提交信息"实现为**硬拦且永不继续**（`mockup.js:4315-4322`、`live-data.js:4464-4471`），权威是**弹确认、确认后继续**。
> 第 1 区剩余：Amend 交互、提交信息校验的呈现、右键菜单项启用/隐藏规则、提交设置入口、"提交并推送"的后续切换 ⇒ 本区估轮不变（1–2，已用 1）。
>
> **第 136 轮：上面那条分歧已按权威改掉** —— "空提交信息"从**硬拦且永不继续**改为**弹确认、确认后照常提交**（`mockup.js` 的共享助手 `confirmCommitWithEmptyMessage()` + `live-data.js` 的 `commitSelectedChanges(andPush, event, confirmed)`），文案取 `VcsBundle.properties:36-37`／`:35` 的语义并本地化，新增自动化测试 `tools/audit/check-commit-empty-message.test.cjs`（16 条）。本区剩余项见 `12-commit-changes.md` §8。
>
> **同轮第二半：提交动作的可用性按权威 `hasDiffs()`** —— 实时侧此前**没有任何地方**按勾选数同步按钮可用性（`reflectWriteOperation()` 把空闲禁用态寄存在 `dataset.idleDisabled`，而没人写它），因此"无勾选仍可点、点了才报错"；现改为 `shouldDisable = busy || !hasIncluded` 并给出禁用理由，勾选变化经 `refreshAfterEvent("side")` 自动重算。本区第 2 项完成，剩 Amend／信息校验／右键菜单项规则／提交设置入口／提交并推送后续（估轮不变）。
>
> **第 138 轮：Amend 已采集并补齐**（`12-commit-changes.md` §9）—— 权威：勾选框 `Amend commit` + 提示 + `Alt+M`、勾选载入上次信息**并聚焦**、取消恢复、**提交动作改名**（`Amend {0}` / `Amend Commit and Push…`）。Augit 的"载入/恢复"这一半**本来就对** ✓；本轮补了 **提示**、**改名**（`applyAmendActionLabel()`，同时挂在重渲染与点击上）、**载入后聚焦**。`Alt+M`、"仅在用户没改过时覆盖"、载入失败对话框三项**登记为待做**（前两项触及"不新增交互"的边界）。本区剩余：提交信息校验的呈现、右键菜单项启用/隐藏规则、提交设置入口、"提交并推送"的后续切换。
>
> **第 139 轮：Changes 右键菜单按权威重排**（`12-commit-changes.md` §10）—— 权威 `ChangesViewPopupMenu` + `Git.FileActions` 给出两条相对顺序：**回滚在显示 Diff 之前**、**Blame 在文件历史之前**；Augit 两处都反了，已改正（只改 `mockup.js` 一处，实时侧复用同一模板）。连带改正 spec 里三处**硬编码旧顺序**的索引断言（`[2]/[3]/[1]` → `[3]/[2]/[0]`），并新增 `check-changes-context-menu.test.cjs` 把顺序**显式**测出来（该场景此前无任何检查器覆盖）。未定位：`在资源管理器中定位` 在权威菜单里找不到对应动作 ⇒ 保留不删、记为待核。本区剩余：提交信息校验的呈现、提交设置入口、"提交并推送"的后续切换。
>
> **第 140 轮：进入第 2 区（Git 写操作对话框），首片 Push 完成** —— 新建 `13-git-dialogs.md`。权威：标题 `Push Commits to <仓库短名>`、主按钮 `&Push`、按钮是**拆分动作**（`Vcs.Push.Simple` + `Vcs.Push.Force`）、启用只由**内联编辑**触发、列表首选尺寸 **800×450**、南侧内距 `empty(8,12)`。**抓到真实缺陷**：Augit 标题硬编码「推送提交到 **Augit**」—— 把产品名当成了推送目标（真机上永远显示 Augit），已按权威改为取 `workspaceName`。登记：Force Push（不新增）、800×450（待量）、内联编辑禁用（无对应交互）。
>
> **第 141 轮：Stash 创建已采（`13-git-dialogs.md` §2）** —— 字段顺序/文案/5 条 tooltip/首选尺寸 400×60/初始焦点/草稿语义/复选默认值。落地：补了 4 个字段的 tooltip（几何不变：620×337）。**升级为人工裁决**：`Include untracked` 复选 —— 权威有、宿主机早已支持（`ShellBridge.cs:1970` 的 `includeUntracked ?? true`）而 `ux-spec:524-525` 明确规定只有四个字段；加它会**改变"什么会被 stash"**（产品行为）⇒ 已写入 `10-backlog.md` §三（该表此前为空）。> **第 142 轮：Stash 应用/弹出已采（`13-git-dialogs.md` §3）** —— 权威 `GitUnstashAsDialog` 两条范式：**分支非空 ⇒ 两个复选联动禁用**（`:88-92`）、**按钮文案三态**（Apply Stash／Pop Stash／Branch，`:94-103`）。落地：给「应用／弹出」补上语义 tooltip（「应用后保留该 Stash」／「应用后丢弃该 Stash」，后者逐字对应 `unstash.pop.stash.tooltip`），**两处渲染点都改**以免视觉稿与真机不一致。"
> **三项登记为"不新增"**（宿主不支持）：`As new branch`、`Reinstate index`、`Clear`。"
> **第 143 轮：Reset 已采（`13-git-dialogs.md` §4）** —— 权威 `GitResetDialog.java:157-159` 的模式顺序 **MIXED→SOFT→HARD** ⇒ **默认 MIXED**（非破坏性）。而 Augit 默认落在 **Hard**：打开对话框直接点确认就会丢弃本地改动。已改（顺序 + 默认 + 给 option 加稳定 `value`、同步重排 `presentations`）。连带改正 **4 处"编码旧默认"的断言**（`live-shell` 两处 + `verify-ux-reset-layout` 一处）。另记录权威 `validateFields()` 自身的覆盖 bug（按意图对齐，不照搬）。
> **第 144 轮：Rollback 已采（`13-git-dialogs.md` §5）—— 本轮无产品改动**，三条"不改的理由"逐条写明：入口文案与权威逐字一致 ✓；可见性那条隐藏分支因 Registry `vcs.prefer.checkboxes.over.selection` **默认 `false`** 而不生效 ⇒ 本就该显示 ✓（**细读避免了一次错改**）；确认对话框形态是 `ux-spec:531` 规定的差异、`Delete local copies of added files` 因宿主无能力不新增。
> **第 145 轮：Clone 已采（`13-git-dialogs.md` §6）** —— 浅克隆行（文案/顺序/默认/启用联动/校验）**本就对齐** ✓，只补了深度框 tooltip `--depth`；**真缺陷是克隆按钮**：权威 `DvcsCloneDialogComponent.kt:127` 的 `isOkActionEnabled() = getUrl().isNotBlank()` ⇒ **URL 空即禁用**，而 Augit 是"始终可用 + 点击后提示"。已改，并连带改正 **3 处断言 + 2 句规范**（`ux-spec:541/543`）。
> **第 146 轮：Worktree 已采（`13-git-dialogs.md` §7）** —— **新建表单缺「新分支」**（宿主 `ShellBridge.cs:507` 早已支持 `newBranch`，UI 从没传过）⇒ 已补上（**默认不勾 ⇒ 行为不变**，与第 141 轮 Stash 那次不同：那次会翻转默认，必须裁决）；字段顺序按权威改为 分支→新分支→目录。另记录**移除判据缺 `!isMain`**（C# 侧待做，**第 212 轮已落地**：`GitWorktreeInfo.IsMain` 取 `git worktree list` 首项，读写两侧都拦、界面禁用并给原因）。
> **第 147 轮：Branches 已采（`13-git-dialogs.md` §8）** —— **规范要求"删除分支/标签"，而产品根本没有该能力**（宿主 `git/branch` 只有 create/rename；UI 亦然）。权威侧它是正式操作（`GitDeleteBranchOperation`：收集未合并提交 → 删除 → 失败回滚）。按边界**不新增**，已升级为专项裁决（`10-backlog.md` §三 第 2 项），并改正状态矩阵里未经核对的 ✅。**本轮未改产品代码。**
> **第 148 轮：宿主方法表全量扫描** —— 发现**两处"能力已实现、只差接口"**：**仓库初始化**（`IGitServices.InitializeAsync` ✓ 但无 `git/init`）与 **Smart Checkout**（`SmartCheckoutAsync` + 临时 stash 标记与中断续做 ✓ 但无桥接方法），UI 都从不调用 ⇒ 属对齐、可直接做，已单列进 `10-backlog.md` §三·补（**本轮刻意不动 C#**：并行会话正在编辑 `src/Augit.Shell/`，构建会连带编译其在途改动、失败无法归因）。另核对 **Remote**（字段为超集、校验较弱 ⇒ 不改）。**本轮未改产品代码。**
> **第 149 轮：第 2 区收口** —— 12 个场景已全部采集（`13-git-dialogs.md` §1–§9）。顺带核对了两处**本以为要补、实际已对齐**的地方：
> ① **冲突列表**（属本审计第 10 区）：live-shell 已注入 rebase 会话，断言"Continue 前置未满足时**保留并禁用且说明原因**"，以及 `supportsContinue=false && canAbort/canSkip=false` 时三者**都不渲染**（`live-shell.spec.cjs:11654-11721`）—— 与 `08-diff-merge.md` §7bis.2 的权威结论（"操作不存在就等于不显示"，而不是仅禁用）一致 ✓；
> ② **`operation-progress`／`operation-result`** 属本审计第 8 区，未在第 2 区范围内。
> 第 2 区的净产出：**5 项真实改动**（Push 标题、Stash 创建 tooltip、Stash 应用/弹出 tooltip、Reset 默认改 Mixed、Clone 按钮 URL 空即禁用、Worktree「新分支」——共 6 项）、**3 项登记**（Force Push／800×450／Alt+M 等）、**2 项裁决**（Stash `Include untracked`、删除分支/标签）、**2 项 C# 接线**（仓库初始化、Smart Checkout）。
>
> **第 171 轮：第 2 区那条"删除分支/标签"结案** —— 第 147 轮记为"产品根本没有该能力"，第 168 轮用户裁决"完全参考 intellij-community"后改走实现路线：第 170 轮补宿主（`git/branch delete` 两步 + `git/tag`），第 171 轮补界面（**每个引用自己的动作菜单**，右键/菜单键打开；删除先说明影响、未合并时再问一次）。**本轮还纠正了自己第一版的口径**：我先把当前分支的「检出／删除」做成"禁用＋原因"，核对权威后确认应为 **`Presentation.setEnabledAndVisible(false)` ⇒ 整项不出现**（`GitSingleRefAction.kt:40` + `Presentation.java:576`；当前引用的检出只在多仓库分歧时可用 `GitCheckoutAction.kt:19-23`）——"不适用"与"未接线"是两种不同的呈现，前者不出现，后者才禁用并写明原因。剩余两件登记在同一轮记录里：日志工具栏「删除引用」（两个入口之一，需先采集 chooser 规则）与「新建标签…」四字段对话框（要 C# 加 `force` 参数）。
>
> **口径沉淀（第 171 轮）**：**"禁用并写明原因"不是通用的兜底**。权威里 `Presentation.isEnabledAndVisible=false` 的含义是"这个动作对这一行不适用 ⇒ 不显示"；只有当动作**适用**、而实现侧**还没有能力**时才应该退化成"禁用＋原因"。把两者混用会让菜单出现一堆永远灰着的项。
>
> **第 172 轮：日志右键菜单的两个「新建…」结案 + 一条方法论沉淀** —— ① 「新建标签…」原来禁用，现在按 `Git.CreateNewTag` → `GitCreateTagAction` 的 **`Messages.showInputDialog` 单字段**落地（标题带选中提交哈希、字段「新标签名称」、空白字符前置拒绝、标签打在该提交上）；② 「新建分支…」原来建的是 HEAD 分支，现在按 `Git.CreateNewBranch.FromCommit` 带 `startPoint`（C# 一行透传 + 2 个宿主用例）。**方法论（第六条）**：我上一轮把「新建标签…」的权威写成四字段的 `GitTagDialog`（并据此登记"要 C# 加 `force`"），这轮发现**同一个功能名在不同入口下是不同的对话框** —— `GitTagDialog` 属于**分支浮层**的 `Git.Tag`，日志菜单走的是 `GitCreateNewTag`。⇒ 查对话框之前必须先钉**入口**（action id + 从哪个菜单进来），否则会照着一份"看起来对、其实不是这条路径"的规范去实现。
>
> **第 173 轮：日志左竖条的身份钉住了 —— 它不是"日志工具条"，是"分支面板的动作组"** —— 权威 `BranchesInGitLogUiFactoryProvider.createMainComponent()`：日志主区 = `addToLeft(expandControlPanel)` ＋ `addToCenter(branchViewSplitter)`，`expandControlPanel` 的展开卡片是**竖向** `ActionToolbar`（`createActionToolbar("Git.Log.Branches", actions, false)`），组内容 = `Git.Log.Hide.Branches` ＋ `Separator()` ＋ `BranchesDashboardTreeComponent.createActionGroup()`（13 项），折叠卡片是 `ExpandStripeButton`（竖排文字 "Branches"）。⇒ Augit 旧竖条的「返回／新建引用／删除引用／刷新／搜索／比较／定位 HEAD」**没有一项**是权威这条竖条上的东西；本轮把竖条改成"隐藏分支 ＋ 新建分支/获取/定位到选中分支"（有能力且不需要引用选择），把「刷新」按 `Vcs.Log.Toolbar.RightCorner` 移到横向行右角，并复现了折叠/展开卡片。**方法论（第七条）**：**同名"工具栏"未必是同一个东西**。第 166 轮把 Augit 的竖条当成"日志工具条"去比对 `Vcs.Log.Toolbar`，得到的结论（"权威里没有这条竖条"）只说明"名字对不上"，而不是"结构不存在"——真正的对应物在 `git4idea` 的分支面板工厂里，且它是**竖的**。先找"这块 UI 由哪个类造出来"，再谈对齐。
>
> **第 174 轮：引用树可选中 —— 竖条里"需要选择"的四项同时解锁** —— 权威树模型 `BranchesTreeModel.kt:214-222`：顶层 = HEAD 节点 ＋ 按 `GitRefType` 的分组（顺序 HEAD→LOCAL→REMOTE→TAGS），组名 `Local`／`Remote`／`Tags`；行里只有引用名（上游只进 tooltip，`BranchesTree.kt:130,141-143`），当前分支/收藏是图标，排序是"当前优先 → 收藏 → 组 → 其它"＋自然序；搜索按引用名匹配且 **HEAD 节点（匹配文本为 null）恒保留**（`:210-217`）；单击默认不做别的，双击/回车才是"把日志筛选到该分支"。Augit 按这张表重建了引用树并加选中态，删掉了"从已加载提交的 references 推分支名"的旧做法（那会漏掉不在当前页的引用）。**方法论（第八条）**：**树的行内容与"树上能做什么"是两件事** —— 我先按印象把上游写进行里、把搜索套用了"过滤一切行"，两处都被权威的行渲染器与匹配文本判据纠正（上游只进 tooltip；HEAD 恒保留）。

## 4. ~~另有 2 项**需产品口径**~~（**第 223 轮两项都已落地**）

1. ~~**`.diff-current` 在 New UI 里没有对应物**（`DiffDrawUtil.PaintMode` 只有 `DEFAULT`／`IGNORED`／`RESOLVED`=无底+点线边框／`EXCLUDED_*`）⇒ 删除／改为行内词级高亮／换用行号槽色族，三选一。~~ → **已删除该层**（第 223 轮）：导航只定位不染色；参考图里那段强色经复测是"无行内差异的新增块"的整行全强度色，不是"当前差异块"。
2. ~~**行内词级高亮**需要宿主提供词级差异范围 ⇒ 已接近"新增数据通道"，触碰"不新增功能"边界。~~ → **前提不成立**：宿主通道（`GitWordDiff` → `ShellBridge` 的 `oldChanges`／`newChanges`）与 `mockup.js` 的 `<mark>` 渲染早就存在，缺的只是配色；第 223 轮补齐（见 `09-icons.md` 第 223 轮）。

## 5. 与本审计同时成立的三条限制

1. **"已采"不等于"已对齐"**：采集只解决"有没有权威依据"；实现是否吻合要逐个表面核（很多区在 `design-system.md` 里已有落地记录，但那是**依据 Augit 现状写的**，需要按本审计重新对照权威）。
2. **35 个检查器全绿只证明"实现 ↔ 我写的期望"一致**：覆盖面由我选了哪些断言决定，未写进断言的交互面全绿也不代表已对齐。
3. **不新增功能**：第 3 节里若某区采集后发现"New UI 有、Augit 没有"的交互，只有在它是**现有功能的呈现方式**时才改；否则只登记。

## 6. 方法（可复跑）

```bash
# 1) 场景与检查器清单
ls docs/ux-mockups/*.html | wc -l          # 56
ls tools/verify-ux-*.cjs | wc -l           # 35

# 2) 某区是否已被行为文档覆盖（零命中 = 未覆盖）
grep -rl "VcsPushDialog\|commit-changes\|SettingsDialog" docs/nui-behavior/0[1-8]*.md

# 3) 权威是否可得
cd /mnt/d/github/intellij-community
rg -l "class VcsPushDialog" --glob '*.java' platform plugins
```

**修订记录**

- 第 133 轮建立本文件；把"剩余轮次"从感觉变成上表的加总（12–21 轮），并明确 2 项待裁决、3 条限制。
- 第 149 轮：第 2 区（Git 写操作对话框）收口 → `13-git-dialogs.md` §1–§9，只余两项 C# 接线。
- 第 150 轮：第 3 区（文件历史与 Blame）采集完成 → `14-file-history-blame.md`；**Blame 归属行悬停提示落地**（归属列本身经权威 `isShowByDefault()` 核实本来就对齐，不需加修订列），文件历史列表的列集合差异记入 `10-backlog.md` §三·补二，剩 1 轮。
- 第 151 轮：第 3 区**收口** —— 文件历史列表改为权威四列（**版本 → 日期 → 作者 → 提交信息**）并补表头行（`14-file-history-blame.md` §2.2）；同区只剩"作者列 `*`／单元格 tooltip"一项，需 C# 补字段（`10-backlog.md` §三·补 第 5 项）。
- 第 152 轮：第 7 区开工 —— **跳转行**采集完成并落地一处交互改正（目标行滚动改权威的 `ScrollType.CENTER`）→ `15-quickopen-gotoline.md` §1；同区剩**快速打开**的匹配与选中规则（权威目录已定位，下一轮）。
- 第 153 轮：第 7 区**收口** —— **快速打开**采集完成并落地两处（查询进行中先清空旧结果并显示「正在搜索…」；结果到达不再回写覆盖用户继续输入的内容），另登记结果上限 100→30 的 C# 项。下一区：第 4 区「设置」（并行会话正在同一区工作，择机）或第 8 区「操作进度与结果」。
- 第 154 轮：第 8 区**收口** —— 采集进度窗口／结果气泡权威 → `16-operation-progress.md`；落地**取消的一次性语义**（按过即禁用 ＋ 入口守卫，防第二次 `write/cancel`），其余四项核对后本来就对齐。下一区：第 5 区「内置终端」或第 9 区「仓库初始化与全仓搜索」。
- 第 155 轮：第 9 区**采集完成**（未改产品代码）→ `17-repository-init-search.md`：阈值 1000、键转发（只有 Up/Down 从输入框转发，Home/End 留在文本框）、空查询 pack 到内容高度三项核对后本来对齐；到限后"是否继续"与仓库初始化的界面契约登记为宿主项。**并修正第 153 轮"方向键不回绕"的说法**（是否回绕取决于 `UISettings.cycleScrolling`）。下一区：第 5 区「内置终端」或第 6 区「图片与不可预览」。
- 第 156 轮：第 6 区采集 —— **不可预览侧采到权威**（`FileSizeLimit` 三档限制 ＋ 超限后仍给"前 N 的只读预览"警告 ；Augit 为全局 10 MB 且整页拒绝）→ `18-file-limit-image.md` §1，宿主项记入 §三·补 第 8 项；**图片侧阻塞**：三次独立检索确认图像查看器**不在本 checkout**，已把"权威来源"作为裁决项写进 §三 第 3 项。
- 第 157 轮：第 5 区（内置终端）—— 权威侧采全并落地 **Esc 语义**（终端里不带修饰键的 Esc 把焦点交回编辑器正文并消费、**不送给 Shell**；Tab 仍进 Shell，两条相反且都由断言钉住）→ `19-terminal.md`；标签生命周期/关闭确认/重命名**无本地权威**（权威自己写明已搬到未包含的 Terminal 插件），以 Augit 规格与实现为准。
- 第 158 轮：**第 9 区的最后一项分辨清楚了**（`Ctrl+Shift+F` 走 UsageView ⇒ 结果**按文件分组**，`UsageViewSettings.kt:21` 默认 `isGroupByFileStructure = true`；Augit 是扁平行）→ 记入 `10-backlog.md` §三·补三，下一轮落地；**第 10 区收口**（三动作可用性判据见 `08-diff-merge.md` §7bis，`live-shell` 早有断言）。
- 第 159 轮：**撤销上一轮的一条结论**。第 158 轮据"Find in Files 与 Search Everywhere 文本页签都用到 UsageView presentation"判定"结果都按文件分组"，把"Augit 扁平行"记成待落地差异；第 159 轮钉住**表面**后推翻：**弹层**用的是自己的**行**模型（`FindPopupItem`，`FindPopupResultsAutoloadHandler.kt:69-80,145-147,349`），**扁平行 + 按文件路径排序**（且当前结果所在文件置顶），Augit 的浮层正是弹层 ⇒ **结构与排序都已对齐**，§三·补三 结案为"不是差异"。
- 第 307 轮：**全量核查并结清本审计** —— `10-backlog.md` 清零（对话框标题图标按权威定性并实现、创建标签条目订正为已落地、`gen-coverage-table.cjs` 加手写内容守卫）；`ui-classification.md` §7 待处理表清零、§2.10 只剩已认可的一行「不适用」；三类场景性能基线与资源回收记录（`performance-report.md` §10–§13）复核仍在。
