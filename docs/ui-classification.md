# Augit 功能与界面的 New UI 归类清单

本文件是目标文本要求的**归类总表**：「Augit 当前每个功能和界面都必须明确归类为：已按 New UI 对齐、有意产品差异、不适用、无法取证或待处理，并具备对应的权威出处、实现位置和按场景可复验的依据。」

本文件只做**归类与索引**，不新增产品能力、不改写任何规格条文。行为规则仍以 `docs/product-spec.md`、`docs/architecture.md`、`docs/ux-spec.md`、`docs/design-system.md` 为准；逐条取证的详细过程在 `docs/nui-behavior/*.md`。

## 0. 口径

### 0.1 五个归类

| 归类 | 含义 | 目标完成时的要求 |
| --- | --- | --- |
| **已按 New UI 对齐** | 行为/视觉按本地权威 `intellij-community@576e328` 落地，且有可复跑依据 | 允许存在 |
| **有意产品差异** | 与权威不同，但是**产品已决定**的偏离（用户裁决或 `product-spec` 明确不做），已写明理由 | 允许存在 |
| **不适用** | 权威里的那个概念要求 Augit 没有的能力（多仓库、后台运行等），或该界面是 Augit 自有表面 | 允许存在 |
| **无法取证** | 权威材料不在本地 checkout、或权威自己写明已搬迁，无法可靠确认 | 允许存在，但必须写明**具体原因**与处理结论（用户认可） |
| **待处理** | 尚未完成的对齐工作 | **必须清零**；无法完成的要转成"有意产品差异／不适用／无法取证"并记录原因 |

### 0.2 证据等级（可复验依据）

| 等级 | 形式 |
| --- | --- |
| E1 断言 | `tools/audit/live-shell.spec.cjs` 的具名断言；`tools/verify-ux-*.cjs`（39 个，全绿） |
| E2 单元测试 | `tests/Augit.{Core,Infrastructure,Shell}.Tests` |
| E3 真机脚本 | `tools/audit/verify-window-chrome.ps1`、`verify-acceptance.ps1`、`capture-surface.ps1`、`measure-performance.ps1` |
| E4 像素/截图 | `docs/ui-compliance.md` §1.1 的 55 场景 `layoutPercent`、`artifacts/*` |
| E5 取证记录 | `docs/nui-behavior/*.md` 的权威 `文件:行号` |
| E6 内部一致性 | 视觉稿＝运行时字节一致（`verify-ui-assets.ps1`），仅用于"无法取证"类 |

### 0.3 通用实现位置

界面本体在 `web/src/`（`mockup.js` 结构、`live-data.js` 实时逻辑、`mockup.css` 令牌与规则；四个共享文件与 `docs/ux-mockups/` 字节一致）；宿主与桥接在 `src/Augit.Shell/ShellBridge.cs`；业务在 `src/Augit.Infrastructure/`、`src/Augit.Core/`。下表的"实现位置"只写**该行特有**的落点，其余默认指上述文件。

---

## 1. 全局框架与视觉系统

| # | 功能/界面 | 归类 | 权威出处 | 实现位置 | 可复验依据 |
| --- | --- | --- | --- | --- | --- |
| 1.1 | 窗口骨架（自绘标题栏、无原生 caption、窗口三键） | 已按 New UI 对齐 | `01-tool-window.md`；`MainToolbar.*` | `mockup.js` 标题栏 | E3 `verify-window-chrome.ps1` 11/11；E1 live-shell |
| 1.2 | 左轨工具窗口按钮（32×32、图标 16、圆角 3、选中两档） | 已按 New UI 对齐 | `01`；`ToolWindowStripeExtension`、`SquareStripeButtonLook` | `mockup.css` `.rail-button` | E1 `verify-ux-frame-buttons.cjs`、live-shell |
| 1.3 | 工具窗口切换/折叠/互斥、分隔条 | 已按 New UI 对齐 | `01`；`ToolWindowDescriptor` | `live-data.js` 布局状态 | E1 live-shell（§5.1/§9.4） |
| 1.4 | 状态栏（字段、只读标识、编码/换行） | 已按 New UI 对齐 | `07`；`StatusBar` | `mockup.js` 状态栏 | E1 `verify-ux-statusbar.cjs`、live-shell |
| 1.5 | 编辑器标签几何与状态（42px、两套渲染定为 expUI 经典） | 已按 New UI 对齐 | `03`；`EditorTabs.*`、`IslandsTabPainter` | `mockup.css` 标签 | E1、E4 |
| 1.6 | 树/列表选择、键盘导航、speed search、复选框 | 已按 New UI 对齐 | `02`；`Tree.Selection.arc`、`WideSelectionListUI` | `live-data.js` 焦点区域 | E1 live-shell（§5.4） |
| 1.7 | 动作可用性：禁用 vs 隐藏（`Presentation.setEnabledAndVisible(false)` ⇒ 不显示） | 已按 New UI 对齐 | `04`；`Presentation.java:576`、`GitSingleRefAction.kt:40` | `mockup.js` 各行菜单 | E1 live-shell（第 171 轮） |
| 1.8 | 菜单项行高 28、选中圆角 4、分隔线 3、弹层圆角 8 | 已按 New UI 对齐 | `04`；`PopupMenu.Selection.*`、`IdeaPopupMenuUI` | `mockup.css` `.menu-item` | E1 `verify-ux-typography.cjs` |
| 1.9 | 主工具栏项目配色渐变 | 已按 New UI 对齐 | `intellij-platform-ui-reference.md` §3.1；`MainToolbar` | `mockup.css` `.titlebar[data-project-color]` | E1 `verify-ux-titlebar.cjs`、E5 |
| 1.10 | 图标线宽 1 / 关闭叉 9×9 / 填充化 / 颜色 | 已按 New UI 对齐 | `09-icons.md`；`expui/*` | `mockup.js` `icon()`、`mockup.css` | E1 `verify-ux-offline-icons.cjs` |
| 1.11 | 主题切换链路（WebView2 表面色、系统主题即时跟随） | 已按 New UI 对齐 | `07` §1.3；`ShellTheme.cs` | `src/Augit.Shell/ShellTheme.cs` | E2 `ShellThemeTests`、E1 live-shell |
| 1.12 | DPI 缩放契约（逻辑像素不变、细线整设备像素） | 已按 New UI 对齐 | `07` §2；`JBUIScale.scale` | `mockup.css` `--augit-hairline`、`applyDeviceScale()` | E1 live-shell（第 199/200 轮） |
| 1.13 | 模态遮罩：透明点击承接层、不做背景变暗 | 已按 New UI 对齐 | `IdeGlassPaneImpl.kt`（无变暗绘制） | `mockup.css` `.scrim` | E1 `verify-ux-reset-layout.cjs`（浅/深 × 三缩放：铺满可视区、`rgba(0,0,0,0)`、`pointer-events` 非 none）、E5 `intellij-platform-ui-behavior.md` §11、E4 |
| 1.14 | 滚动条滑块（`ScrollBar.Transparent.thumbColor`，悬停档不实现） | 有意产品差异 | `ScrollBarPainter.java:113` | `mockup.css` `scrollbar-color` | E5 `10-backlog.md` §五（悬停档无 CSS 表达） |
| 1.15 | 树/Changes 行悬停底 | 有意产品差异 | 权威未给树装 `TreeHoverListener`；平台元数据支持 `if hover is allowed` | `mockup.css` `.tree-row:hover`、`.changes-list .check-row:hover` | 用户裁决（`10-backlog.md` 第 100 轮）；E1 live-shell |
| 1.16 | 树行高 28 基准（权威 `Tree.rowHeight` = 24、参考图 23.5） | 有意产品差异 | `02`；`Tree.rowHeight` | `mockup.js` `tree-height = ceil(max(27,h+8)/2)*2` | 用户裁决"规格名义值"（`10-backlog.md` §五，第 116 轮）；E4 |
| 1.17 | 只读正文行高 1.7×（权威运行时为 `字体度量高×1.2`） | 有意产品差异 | `03` §8.3；`Editor` 行高 | `mockup.js` `--code-line-height` | E5 `design-system.md` §4.3、E4 |
| 1.18 | 字号范围 9–40px（平台不校验上限） | 有意产品差异 | 平台建议 8–72、不校验 | `mockup.js` 字号设置 | E5 `intellij-platform-ui-behavior.md` §3 |
| 1.19 | 空右键菜单不显示占位项 | 有意产品差异 | 权威补占位项 | 各右键菜单渲染 | E5 `intellij-platform-ui-behavior.md` §3；`ux-spec` 为准 |
| 1.20 | WebView2 多进程内存基数（约 543–631 MB，100 MB 目标不可达） | 不适用 | 平台自绘/原生控件才有 100 MB 量级 | 架构决定 | E3 `measure-performance.ps1`、`performance-report.md` §10 |
| 1.21 | Compose/Jewel 专属尺寸（如 Jewel `IconButton` 24×24、`dialogUnscaledGaps` 10/12） | 不适用 | 那属 Compose 栈，Augit 是 Swing 等价物的 HTML 复刻 | —— | E5 `intellij-platform-ui-behavior.md` §2.3、`10-backlog.md` 一/二·补四 |
| 1.22 | 项目树缩进步长 18px（首级 22px） | 已按 New UI 对齐 | `02`；`Tree.leftChildIndent`(7)+`Tree.rightChildIndent`(11)、`ClassicPainter.getRendererOffset()` | `mockup.css` 的 `--tree-depth` 计算、`mockup.js` 两处树构建 | E3 `verify-ux-project-tree.cjs`（dpi×字号矩阵断言步长与 depth-7=130px） |
| 1.23 | `Tree.border = 4,12,4,12`（Swing 树外内距） | 有意产品差异（实现方式） | `expUI_light.theme.json:802` 等 | `mockup.css` `.side-content.tree` 的行内距 | Augit 的 DOM 无 Swing border 层，用行内距表达同一边距，不逐值套用 |

## 2. Git 与文件功能

| # | 功能/界面 | 归类 | 权威出处 | 实现位置 | 可复验依据 |
| --- | --- | --- | --- | --- | --- |
| 2.1 | 项目树（展开/刷新/外部变化增量、右键菜单） | 已按 New UI 对齐 | `02`；`AbstractProjectViewPane` | `live-data.js` 树渲染 | E1 `verify-ux-project-tree.cjs`、live-shell |
| 2.2 | 只读文本查看（行号、换行、显示空白、查找） | 已按 New UI 对齐 | `03`、`05`；`EditorTabs`、查找条 | `mockup.js` `code-view` | E1 `verify-ux-find.cjs`、`verify-ux-document-toolbar.cjs` |
| 2.3 | Markdown 预览/原文（模式记忆、链接处理） | 已按 New UI 对齐 | `03`；`ux-spec` §7.3 | `markdown.js`、`live-data.js` | E1 `verify-ux-markdown.cjs` |
| 2.4 | JSON 原文/格式化与错误行列 | 已按 New UI 对齐 | `ux-spec` §7.4；`JsonDisplayFormatter` | `live-data.js`、`mockup.js` | E1 `verify-ux-json.cjs` 24/24、E2 Core 单测 |
| 2.5 | 图片查看器（缩放/平移/适应） | **无法取证** | 图像查看器**不在本 checkout**（三次独立检索 0 命中） | `image-preview.js`（与视觉稿字节一致） | E6 `verify-ui-assets.ps1`；用户裁决（第 211 轮）逐字见 `10-backlog.md` §三 第 3 项 |
| 2.6 | 不可预览文件（三档上限、只读预览、警告横幅） | 已按 New UI 对齐 | `FileSizeLimit.kt:14-24`、`LargeFileNotificationProvider.java:37-58` | `DocumentLimits`、`ReadOnlyDocumentService`、`mockup.js` `largeFileBanner()` | E1 `verify-ux-large-file-preview.cjs`、E2 |
| 2.7 | Changes 列表与提交（勾选、Amend、空信息确认、可用性） | 已按 New UI 对齐 | `12-commit-changes.md`；`CommitChangeListDialog.java:602-604`、`SingleChangeListCommitWorkflowHandler.kt:117-122` | `live-data.js` `commitSelectedChanges` | E1 `verify-ux-commit-workflow.cjs`、live-shell |
| 2.8 | Changes 右键菜单顺序 | 已按 New UI 对齐 | `12` §10；`ChangesViewPopupMenu`、`Git.FileActions` | `mockup.js` `changesContextMenu()` | E1 `check-changes-context-menu.test.cjs` |
| 2.9 | 工作区 Diff（状态机、边界两段式、文件导航） | 已按 New UI 对齐 | `08`、`ux-spec` §7.7 | `live-data.js` `loadDiff`/`moveDiffFile` | E1 live-shell（§7.9 块）、`verify-ux-diff-typography.cjs` |
| 2.10 | Diff 行色两档 + 行内（词级）层 | 已按 New UI 对齐（整行底两档 + 行内层）／**中间行号槽着色待处理**（T10） | `08` §2.1／§2.2；`DiffViewerHighlighters.kt:113-129`、`TextDiffTypeFactory.java:50-74`、`DiffDrawUtil.java:754-783` | `mockup.css` `--augit-diff-*`、`--augit-diff-inline-*`；`mockup.js` `marked()` | E1 `check-diff-inline.test.cjs`、E5、live-shell（第 223 轮 `src/Modified.cs` 场景） |
| 2.11 | Git 历史（列表、详情、分页、快捷键） | 已按 New UI 对齐 | `06`、`07`；`VcsLogUI` | `live-data.js` `loadHistory` | E1 `verify-ux-history.cjs`、`verify-ux-history-details.cjs` |
| 2.12 | 提交图（轨道分配、边、配色） | 已按 New UI 对齐 | `06`；`GitLogGraphColorManager` | `mockup.js` 图形绘制 | E1 `verify-ux-commit-graph.cjs` |
| 2.13 | Git 历史筛选（文本/哈希、分支、用户、日期、路径） | 已按 New UI 对齐（**登记差异**见右） | `VcsLogFilterCollection`、`VcsLogFiltererImpl.kt:88-101` | `git/history` 参数、`live-data.js` 筛选弹层 | E1 live-shell（第 179/185/188–191 轮） |
| 2.14 | 文件历史（权威四列 + 作者 `*`/tooltip） | 已按 New UI 对齐 | `14`；`FileHistoryPanelImpl.java:292-306,764-788` | `mockup.js` `historyAuthorCell()` | E1 `verify-ux-file-history.cjs` |
| 2.15 | Blame（归属列、悬停日期、上一修订） | 已按 New UI 对齐 | `14`；`GitFileAnnotation.java:193`、`AnnotatePreviousRevisionAction` | `live-data.js` `loadBlame(path, revision)` | E1 `verify-ux-blame.cjs`、E2 |
| 2.16 | 引用比较（与当前分支、两任意分支） | 已按 New UI 对齐（**登记差异**：开在编辑器标签） | `GitCompareBranchesUi`、`ShowArbitraryBranchesDiffAction` | `live-data.js` `openBranchComparison()` | E1 live-shell（第 180/193 轮） |
| 2.17 | 分支与标签（创建/重命名/删除、两步确认、引用树、多选） | 已按 New UI 对齐（**登记差异**见 20 册） | `13` §8、`20-branches-host-batch.md` | `git/branch`、`git/tag`、`live-data.js` 引用树 | E1 live-shell（第 170/171/182–184 轮）、E2 |
| 2.18 | 日志左竖条 = 分支面板动作组 + 引用树 + 我的分支 | 已按 New UI 对齐 | `BranchesInGitLogUiFactoryProvider`、`BranchesTreeModel.kt:214-222` | `live-data.js` `refTree`、`mockup.js` | E1 live-shell（第 173–178 轮） |
| 2.19 | Stash 创建/应用/弹出/删除（`Include untracked` 默认不勾） | 已按 New UI 对齐 | `GitStashDialog.kt:41-53`、`GitUnstashAsDialog` | `ShellBridge.CreateStashAsync`、`mockup.js` | E1 `verify-ux-stash.cjs`、E2（第 211 轮） |
| 2.20 | Reset（默认 Mixed、模式顺序、影响说明） | 已按 New UI 对齐 | `GitResetDialog.java:157-159` | `git/reset`、`mockup.js` | E1 `verify-ux-reset-rollback.cjs`、live-shell |
| 2.21 | Rollback（入口文案、回收站说明） | 已按 New UI 对齐（**登记差异**：确认框形态按 `ux-spec`） | `RollbackAction`／`RollbackChangesDialog` | `live-data.js` | E1 `verify-ux-rollback-layout.cjs` |
| 2.22 | Worktree 管理（创建/新分支/打开/安全移除/主工作树） | 已按 New UI 对齐 | `GitWorkingTreeDialog.kt:185-198`、`RemoveWorkingTreeAction.kt:31-40` | `GitWorktreeService`、`ShellBridge`、`mockup.js` | E1 `verify-ux-worktree.cjs`、E2（第 212 轮） |
| 2.23 | Clone（浅克隆、URL 空即禁用） | 已按 New UI 对齐 | `DvcsCloneDialogComponent.kt:127`、`GitShallowCloneViewModel` | `live-data.js` | E1 `verify-ux-clone.cjs` |
| 2.24 | Push（标题用仓库名、拆分动作、无远端） | 已按 New UI 对齐（**差异**：Force Push 不提供） | `VcsPushDialog.java` | `git/unpushed`、`mockup.js` | E1 `verify-ux-push.cjs` |
| 2.25 | 远端管理（名称/fetchUrl/pushUrl） | 已按 New UI 对齐（**差异**：三重名校验未前置；远端分支删除未接线） | `GitDefineRemoteDialog` | `git/remote-write`、`mockup.js` | E1 `verify-ux-remote.cjs`；E5 `13` §9.1 |
| 2.26 | 仓库初始化（创建 Git 仓库、确认形态、失败原因） | 已按 New UI 对齐 | `GitInit.java:45-83` | `git/init`、`mockup.js` `repositoryInitBody()` | E1 `verify-ux-repository-init.cjs`、E2 |
| 2.27 | Smart Checkout（overwriteRisk 对话框、恢复冲突说明） | 已按 New UI 对齐（**差异**：Force Checkout 不提供） | `GitSmartOperationDialog.java:36-125`、`GitBrancher.java:91-92` | `git/checkout-smart`、`live-data.js` | E1 `verify-ux-smart-checkout.cjs`、E2 |
| 2.28 | 冲突操作会话列表（Continue/Skip/Abort 可用性） | 已按 New UI 对齐 | `08` §7bis；`MergeActionCaptions` | `git/operation`、`live-data.js` | E1 live-shell（第 149 轮） |
| 2.29 | 三栏冲突解决器（唯一可编辑区、每侧高亮、整侧接受） | 已按 New UI 对齐 | `08` §7bis；`ThreesideMergeHighlighters` | `mockup.js` `conflict-*`、`live-data.js` | E1 `verify-ux-conflict.cjs` 72/72 |
| 2.30 | 快速打开 / 跳转行（结果上限 30、进行中状态） | 已按 New UI 对齐 | `15`；`GotoFileAction`、`SearchEverywhereUI.java:217` | `search/files`、`mockup.js` | E1 live-shell（第 152/153/201 轮） |
| 2.31 | 全仓搜索（浮层扁平行、1000 条上限、Continue/Abort） | 已按 New UI 对齐 | `17`；`UsageLimitUtil.java:26-34`、`FindPopupResultsAutoloadHandler.kt:69-80` | `search/text`、`live-data.js` | E1 `verify-ux-search-limited.cjs`、E2 |
| 2.32 | Git 不可用（提示一次、配置入口、提交/历史入口禁用） | 已按 New UI 对齐 | `17` §3；`GitExecutable` 检测（权威在缺 Git 时隐藏/禁用 VCS 入口） | `live-data.js` `showGitUnavailable`、`mockup.js` `rail()` 的禁用分支 | E1 live-shell（第 215 轮：禁用标签集、悬停原因、点击不切换） |
| 2.33 | 内置终端（键位 Esc/Tab、标题栏三动作、会话回收） | 已按 New UI 对齐（**键位侧**）／**无法取证**（标签生命周期） | `19`；`TerminalEscapeKeyListener.java` | `live-data.js` xterm 接线 | E1 live-shell（第 157 轮）、E2 `ShellBridgeTerminalBufferTests` |
| 2.34 | 终端标签生命周期／关闭确认／重命名 | **无法取证** | 权威写明已搬到 `org.jetbrains.plugins.terminal`（本 checkout 不含） | 按 `ux-spec` §7.16 与实现维护 | E5 `19-terminal.md`、`11-surface-audit.md` 第 5 区 |
| 2.35 | 设置（分类/搜索/spotlight/底栏/快捷键/草稿） | 已按 New UI 对齐（**登记差异**见右） | `SettingsDialog.java:86-96`、`SettingsSearch.java`、`SpotlightPainter.kt` | `live-data.js` 设置 | E1 live-shell（第 195–197 轮） |
| 2.36 | 操作进行/结果（取消一次性、提示不自动消失） | 已按 New UI 对齐 | `16`；`ProgressDialogUI.kt:136-152`、`ProcessBalloon.kt:114-127` | `reflectWriteOperation()` | E1 live-shell（第 154 轮） |
| 2.37 | 操作进度条（`.progress-track`） | **待处理**（见 §7 T3） | `ProgressWindow`／`ProgressDialogUI` | 实时侧尚未渲染 | E4 仅视觉稿 `operation-progress` |
| 2.38 | 提交信息校验/Amend 覆盖条件 | 已按 New UI 对齐 | `12` §9；`AmendCommitHandlerImpl.kt:78-115`、`SingleChangeListCommitWorkflowHandler.kt:75` | `live-data.js` `amendInitialMessages`／`amendDrafts` | E1 live-shell（第 216 轮：改过不覆盖/不查宿主、没改过载入并聚焦、取消按权威恢复） |
| 2.39 | Git「标记为收藏」 | 有意产品差异（维持禁用并写明） | `ToggleFavoriteAction`＋`DvcsBranchSettings.favorites` | 竖条禁用项 | 用户裁决（第 211 轮，`10-backlog.md` §三 第 4 项） |
| 2.40 | 分支面板「按仓库分组」 | 不适用 | 权威要求多仓库 | 设置弹层禁用项 | E5 `20-branches-host-batch.md` §4 |
| 2.41 | 远端分支删除、Prune、CheckinFiles 等 Changes 动作家族 | 不适用（产品无该能力，按边界不新增） | `GitDeleteRemoteBranchOperation` 等 | —— | E5 `09-icons.md` 第 171 轮、`13` §8 |

## 3. 对话框与状态机（权威均出自 `13-git-dialogs.md` 与 `07-theme-dpi-dialogs.md`）

| # | 项 | 归类 | 说明 |
| --- | --- | --- | --- |
| 3.1 | 经典对话框内容/标题/底栏内距、无底栏分隔线 | 已按 New UI 对齐 | `DialogWrapper.java:838-845,1522`、`UIUtil.java:370-371` |
| 3.2 | 破坏性确认框标题图标（reset/rollback 问号） | 已按 New UI 对齐 | 官方 `questionDialog.svg`；其余 5 个对话框图标无依据 ⇒ 不适用（见 §6） |
| 3.3 | 对话框状态机（进行中冻结、取消、焦点恢复） | 已按 New UI 对齐 | `07`；`DialogWrapper` |
| 3.4 | `Diff 当前块`（`.diff-current`） | 已按 New UI 对齐（**第 223 轮删除该层**） | 权威 `DiffDrawUtil.PaintMode` 只有 `DEFAULT`／`IGNORED`／`RESOLVED`／`EXCLUDED_*`，没有"当前差异"模式；导航只定位（`ux-spec.md:438/439/502`） |
| 3.5 | 行内词级 Diff 高亮 | 已按 New UI 对齐（第 223 轮实现） | `DIFF_*.BACKGROUND`；宿主 `oldChanges`／`newChanges` 通道此前已存在（`GitWordDiff`、`ShellBridge`） |
| 3.6 | 冲突解决器 Continue/Skip/Abort 文案 | 有意产品差异 | 权威走 `MERGE_ACTION_CAPTIONS` 钩子，由 `ux-spec` 定义 |
| 3.7 | 对话框"在后台运行"按钮、任务自定义取消文案 | 不适用 | Augit 写操作不阻塞、只有一种取消文案 |
| 3.8 | 图像查看器缩放/平移规则 | 无法取证 | 同 2.5 |
| 3.9 | 终端标签生命周期 | 无法取证 | 同 2.34 |
| 3.10 | Force Push、`As new branch`、`Reinstate index`、`Clear`、`Delete local copies of added files` | 不适用 | 产品无能力，按边界不新增（`13` §2/§9） |

## 4. 场景级归类（55 个产品场景；`index.html` 是视觉稿索引页，不属产品界面）

实现位置统一为 `web/src/mockup.js`（结构）＋ `web/src/live-data.js`（实时）＋ `web/src/mockup.css`；下表的"依据"指该场景特有的证据（通用证据见 §0.2）。

| 场景 | 归类 | 依据 |
| --- | --- | --- |
| `main-project` | 已按 New UI 对齐 | E3 真机巡检、E4 像素 |
| `workspace-open` | 已按 New UI 对齐 | E4；E1 live-shell |
| `project-context-menu` | 已按 New UI 对齐 | E1 `verify-ux-project-tree.cjs` |
| `text-viewer` | 已按 New UI 对齐 | E1 `verify-ux-find.cjs`、E4 |
| `markdown-preview` | 已按 New UI 对齐 | E1 `verify-ux-markdown.cjs`、`verify-ux-json.cjs` |
| `json-preview` | 已按 New UI 对齐 | E1 `verify-ux-json.cjs` 24/24 |
| `image-preview` | 无法取证 | E6；用户裁决第 211 轮 |
| `image-error` | 无法取证 | E6；用户裁决第 211 轮 |
| `file-limit` | 已按 New UI 对齐 | E1 `verify-ux-large-file-preview.cjs` |
| `commit-changes` | 已按 New UI 对齐 | E1 `verify-ux-commit-workflow.cjs`、E3 |
| `commit-empty` | 已按 New UI 对齐 | E1 `check-commit-empty-message.test.cjs` |
| `changes-context-menu` | 已按 New UI 对齐 | E1 `check-changes-context-menu.test.cjs` |
| `commit-diff` | 已按 New UI 对齐 | E1、E4 |
| `diff-status` | 已按 New UI 对齐 | E1 `verify-ux-diff-typography.cjs` |
| `diff-loading` | 已按 New UI 对齐 | E1 live-shell |
| `diff-boundary` | 已按 New UI 对齐 | E3（第 371 轮专测）、E4 |
| `git-history` | 已按 New UI 对齐 | E1 `verify-ux-history.cjs`、E3 |
| `git-history-empty` | 已按 New UI 对齐 | E1 live-shell |
| `git-history-graph` | 已按 New UI 对齐 | E1 `verify-ux-commit-graph.cjs` |
| `git-history-menu` | 已按 New UI 对齐 | E1 live-shell（第 167/172 轮） |
| `history-diff-loading` | 已按 New UI 对齐 | E1 live-shell |
| `history-diff-cancelled` | 已按 New UI 对齐 | E1 live-shell（取消入口） |
| `history-diff-failure` | 已按 New UI 对齐 | E1 live-shell（失败保留标签可重试） |
| `git-compare` | 已按 New UI 对齐 | E1 `verify-ux-history-follow.cjs` |
| `git-compare-empty` | 已按 New UI 对齐 | E4 |
| `file-history` | 已按 New UI 对齐 | E1 `verify-ux-file-history.cjs` |
| `blame` | 已按 New UI 对齐 | E1 `verify-ux-blame.cjs` |
| `branches` | 已按 New UI 对齐 | E1、E2（第 170/171 轮） |
| `stash` | 已按 New UI 对齐 | E1 `verify-ux-stash.cjs` |
| `stash-manager` | 已按 New UI 对齐 | E1 `verify-ux-stash-manager.cjs` |
| `stash-drop-confirm` | 已按 New UI 对齐 | E1、E4 |
| `reset` | 已按 New UI 对齐 | E1 `verify-ux-reset-layout.cjs`、`verify-ux-reset-rollback.cjs` |
| `rollback` | 已按 New UI 对齐 | E1 `verify-ux-rollback-layout.cjs` |
| `worktrees` | 已按 New UI 对齐 | E1 `verify-ux-worktree.cjs`、E2（第 212 轮） |
| `remote` | 已按 New UI 对齐 | E1 `verify-ux-remote.cjs` |
| `clone` | 已按 New UI 对齐 | E1 `verify-ux-clone.cjs` |
| `push` | 已按 New UI 对齐 | E1 `verify-ux-push.cjs` |
| `push-no-remote` | 已按 New UI 对齐 | E1 `verify-ux-push.cjs` |
| `operation-progress` | 待处理（进度条） | E4 仅视觉稿（见 §7 T3） |
| `operation-result` | 已按 New UI 对齐 | E1 live-shell（第 154 轮） |
| `smart-checkout` | 已按 New UI 对齐 | E1 `verify-ux-smart-checkout.cjs` |
| `conflict-list` | 已按 New UI 对齐 | E1 live-shell（第 149 轮） |
| `conflict-resolver` | 已按 New UI 对齐 | E1 `verify-ux-conflict.cjs` 72/72 |
| `repository-init` | 已按 New UI 对齐 | E1 `verify-ux-repository-init.cjs` |
| `repository-search` | 已按 New UI 对齐 | E1 live-shell |
| `search-limited` | 已按 New UI 对齐 | E1 `verify-ux-search-limited.cjs` |
| `quick-open` | 已按 New UI 对齐 | E1 live-shell（第 153 轮） |
| `quick-open-empty` | 已按 New UI 对齐 | E1、E4 |
| `go-to-line` | 已按 New UI 对齐 | E1 live-shell（第 152 轮） |
| `settings` | 已按 New UI 对齐 | E1 live-shell（第 195–197 轮） |
| `settings-dirty` | 已按 New UI 对齐 | E1、E4 |
| `settings-save-failure` | 已按 New UI 对齐 | E1、E4 |
| `terminal` | 已按 New UI 对齐（键位）／无法取证（标签） | E1 live-shell（第 157 轮）；见 2.34 |
| `terminal-close` | 无法取证（标签生命周期） | 见 2.34 |
| `git-unavailable` | 已按 New UI 对齐 | E1 live-shell（第 215 轮：入口禁用 + 悬停原因 + 点击不切换） |
| `index`（索引页） | 不适用 | 视觉稿索引，不是产品界面 |

## 5. 无法取证清单（含具体原因与处理结论）

| 项 | 为什么无法取证 | 处理结论 |
| --- | --- | --- |
| 图像查看器（`image-preview`／`image-error`） | New UI 图像查看器不在 `/mnt/d/github/intellij-community`：`rg -l "class ImageViewer\|class UberImageViewer\|class ImageEditor"`、`find -name "*ImageViewer*"`、全仓 `rg -l "UberImageViewer"` 三次检索 0 命中 | **用户裁决（第 211 轮）：认定为"无本地权威"**；此后只按内部一致性（视觉稿＝运行时基线）维护，不声称与 PyCharm 对齐 |
| 终端标签生命周期／关闭确认／重命名 | 权威自己在 `session/TerminalSession.kt` 的 `@Deprecated` 文本里写明已搬到 `org.jetbrains.plugins.terminal`，该插件不在本 checkout | 按 `ux-spec` §7.16 与实现维护，**不写成"已对齐"**（`19-terminal.md`） |
| Windows 10 22H2 实机行为 | 当前无 Windows 10 实机 | 只登记 Windows 11 x64 结果并明确标注；**不宣称完成 Windows 10 验证** |

## 6. 不适用清单（权威概念要求 Augit 没有的能力）

| 项 | 原因 |
| --- | --- |
| 多仓库相关（按仓库分组、多仓库分歧时的检出、跨仓库日志） | 产品规格不含多仓库 |
| 后台运行按钮、任务自定义取消文案 | Augit 写操作不阻塞、只有一种取消文案 |
| Compose/Jewel 专属尺寸 | 属另一个 UI 栈，不能套用到 Swing 等价物的 HTML 复刻 |
| 图像查看器之外的 New UI 专有编辑器能力（结构视图、意图动作等） | 属"完整 IDE"能力，Augit 不提供 |
| 十万提交历史 fixture | 本机无快速生成条件（性能项，见 §8） |

## 7. 待处理清单（目标完成前必须清零）

> 这是目标的硬性要求：「待处理」只用于执行期间的追踪，**不进入目标完成状态**。下列每项都必须解决、或转成"有意产品差异／不适用／无法取证"并取得用户认可的处理结论。

| # | 项 | 现状与依据 | 出路 |
| --- | --- | --- | --- |
| T1 | ~~`.diff-current`（当前差异块）在 New UI 无对应物~~ | **已关闭（第 223 轮）**：删层（只定位不染色）。像素复测证明参考图里那段强色是**一行**的高度（`y623–661` ≈ 26 逻辑px），是"无行内差异的新增块"的全强度 `DIFF_INSERTED.BACKGROUND`，不是"当前差异被整块选中" | — |
| T2 | ~~行内词级 Diff 高亮~~ | **已关闭（第 223 轮）**：实现为 `.diff-code-line mark`，取权威 `DIFF_*.BACKGROUND`；宿主通道（`GitWordDiff` → `ShellBridge` 的 `oldChanges`／`newChanges`）第 8 模块起就存在，`mockup.js` 的 `marked()` 也早已渲染 `<mark>`，缺的只是配色 | — |
| T3 | 操作进度条（`.progress-track`）实时侧不渲染 | **第 226 轮已取证**（`16-operation-progress.md` §3）：进度条是权威**模态 `ProgressWindow`** 的构成（`ProgressDialogUI.kt:56-108`），无分数时按 `isIndeterminate`（`:163-181`）；Augit 不实现该窗口，写操作也没有任何进度分数，`ux-spec.md:699-701` 对"进行中"只要求"禁用重复触发＋显示取消与当前动作"（已实现并断言）。**建议登记为「有意产品差异」**，并已写明若用户要保留该元素时的最小权威做法 | 待用户认可该结论（认可后从本表移除并把 §2 的对应行改标） |
| T4 | ~~Amend"仅在用户没改过信息时才覆盖"~~ | `12-commit-changes.md` §9 登记待做 | **已关闭（第 216 轮）**：按权威 `AmendCommitHandlerImpl.kt:78-115` 实现"面板激活时的初始信息"基线 |
| T6 | ~~§2.10 的 C 类"未覆盖（无断言也无观察）"~~ | **已关闭（第 225 轮）**：C 类 **3 → 0** —— §7.9 #23「比较对话框只列出分支、标签和提交」按产品边界改标 **不适用**（新增 E 类，见 §9），§7.3 #6/#12 两条 Markdown 预览加载/失败状态**实现并断言**（见 §9）。§2.10 重算：分母 100 → **98**、A 78／B 3／C **0**／D 16／E 1 | — |
| T7 | 规范内部矛盾：树/Changes 行悬停 | `design-system.md` §8.3 第 415 行（用户裁决保留）与第 427 行（"待移除"）互相冲突 | **本轮已按用户裁决（第 100 轮）统一为"保留 + 有意差异"**，见 §9 修订 |
| T8 | 行为索引 §3 冲突表的三行旧状态 | `intellij-platform-ui-behavior.md` §3 仍把"行悬停/行高/行高 1.2/模态遮罩"写成"待实施/待核实" | **本轮已改标为已裁决的"有意产品差异"或"已实施"**，见 §9 修订 |
| T9 | ~~树/列表缩进与 `Tree.border` 的逐值核对~~ | `02-tree-list.md` 记 18px 步长，`design-system.md` §8.3 却写 16px，实现是四条固定规则（16px 步长） | **已关闭（第 217 轮）**：按权威 7+11=18 与参考图 18.4 订正为 `--tree-depth` 的 18px 步长（支持任意深度），并加 dpi×字号矩阵断言；`Tree.border` 是 Swing 外内距、Augit 用行内距表达（登记为实现方式差异） |
| T10 | 差异视图**中间栏**的"变更连接区"（梯形）与随变更着色的槽底未实现 | **第 226 轮权威已补齐**（`08-diff-merge.md` §2.3）：槽底 = `DiffLineMarkerRenderer.drawMarker()` 的 `gutterMode` ⇒ **全强度 `DIFF_*.BACKGROUND`**（与行的 ignored 柔和底无关，`:34-104`）；"槽与正文之间的窄带"用 `editorMode`（柔和）；梯形 = `DiffDividerDrawUtil.DividerPolygon`（全强度色、无边框、`withAlignedHeight()` 对齐，`DiffDividerDrawUtil.java:305-320,439-535`）；单行增删改画 2px 线。Augit 的行号槽是**逐行两个 `<div>`** ⇒ 逐行填充可直接落在单元格背景上，但**梯形**需要新的绘制面（中栏没有独立分隔器元素） | ① 按权威补"逐行槽底（全强度色）＋ 每块梯形（分隔器或 `clip-path` 近似）"；② 或登记为**有意产品差异**。**需产品口径**（未获口径前不动实现） |
| T11 | ~~差异块计数与边界提示**按两栏各算一次**~~ | **已关闭（第 224 轮）**：`ux-spec.md:441` 要求"差异数量按连续变更块计算"，而 `diffChangeBlocks()` 扫整份 DOM ⇒ 双栏下每处差异被算两次（`data-diff-total` 翻倍、边界提示早一次）。同时修掉"到边界那一次点击就直接给提示"（应先定位）与"导航状态挂在跨重绘存活的滚动容器上 ⇒ 切单双栏/换文件后第一次点箭头会直接切文件" | — |

**已关闭**：T5（`git-unavailable` 下提交/Git 历史入口禁用态）由**第 215 轮**实现并断言 —— `rail()` 按 `gitUnavailableReason` 写 `aria-disabled="true"` ＋ `title` 原因，`bindToolRail()` 阻止禁用入口切换工具窗口；`ui-compliance.md` §2.6 §7.18 第 2 条由"未覆盖"转"是"，§2.10 的 C 类随之由 11 条降到 10 条（本表 T6 已同步）。T4（Amend 覆盖条件）由**第 216 轮**按权威 `AmendCommitHandlerImpl.kt:78-115` 实现并断言（见 §2.38）。T9（树缩进步长）由**第 217 轮**订正为权威的 18px 并加断言（见 §1.22／§1.23／`02-tree-list.md`）。**第 218 轮**实现并断言 **Git 历史上翻页**（`ux-spec` §7.8 第 475-476 行）：宿主 `git/history` 收 `page`、界面滚动触底追加并由 `restoreHistoryScroll()` 保持可见位置。**第 219 轮**为 **§7.13 外部解决冲突后的会话列表更新**补断言（推送 `workspace-changed {gitMetadata:true}` 后 500ms 内列表 2 → 1）。**第 220 轮**实现并断言 **§7.9 点击 Blame 提交定位 Git 历史并选中该提交**（含从文件历史打开的 Blame 恢复日志布局、解除路径限定；权威 `GitFileAnnotation.showAffectedPaths()` → `VcsLogNavigationUtil.jumpToRevisionAsync`，registry 默认 true），并把日志提交选中写进 `live.historySelectedHash` 使刷新不再重置选中。**第 221 轮**为 **§6.7 关闭比较后返回尚未就绪的普通标签**补读取占位（`documentLoadingView()`）并加断言。**第 222 轮**为 **§7.16 终端启动时序**补代际失效（关闭期间晚到的 `terminal/start` 释放刚启动的 Shell、不复活终端）与"ready 前输出保留"断言。T6 的 C 类随之由 10 条降到 **3** 条。**第 223 轮**关闭 T1／T2 并订正 Diff 行色模型：删除无权威对应的 `.diff-current` 层（`moveDiffChange` 只保留定位与按钮焦点），实现行内（词级）高亮层（`.diff-code-line mark`，权威 `DIFF_*.BACKGROUND`），按 `DiffViewerHighlighters.createHighlighter` 的 `ignored = !resolved && innerFragments != null` 两档规则把整行底订正为 增 `#BEE6BE`／删 `#D6D6D6`／改 `#E7EFFA`（深 `#294436`／`#484A4A`／`#283541`）；同时修正 `diffChangeBlocks()` 不认 `changed`（`Modified`）行导致纯修改型 Diff 一处差异都定位不到的真实缺陷。像素复测推翻第 110／133 轮的"软行底"结论（`#EDFCED` = `INJECTED_LANGUAGE_FRAGMENT.BACKGROUND`，在参考图里左右两栏同时满宽覆盖；`#F4F7F9` 在差异正文两栏内 0 像素），新增 T10（中间栏"变更连接区"）与 T11（差异块两栏重复计数）。**第 224 轮**关闭 T11：`diffChangeBlocks()` 只按**一栏**计块（规格 `ux-spec.md:441`「差异数量按连续变更块计算」，此前扫整份 DOM 使双栏下每处差异算两次），"是否已在边界"改用**点击前**的索引判断（否则定位到首/尾块的那一次点击就直接给提示），并把导航状态从**滚动容器**移到**布局根**（滚动容器跨正文重绘存活，导致切单双栏或换文件后第一次点箭头直接切文件）；`live-shell` 的 §7.9 差异导航断言随之回到规格口径（一次替换 = **1** 处，原来记的"2 处"是两栏各算一次的产物），并新增"单栏与双栏数量一致 + 每处差异只占一行"的断言。T10 的权威依据也读到了（`DiffDividerDrawUtil.DefaultPainter.getFillColor()` = 全强度 `DIFF_*.BACKGROUND`、`withAlignedHeight()` 对齐几何），但"行号列自身被填充"那一半**未定位到绘制者** ⇒ 待产品口径（实现 or 有意差异）。**第 231 轮**为 §7.2 第 11 条补断言（查找计数必须由真实正文算出）：桩新增 `docs/search-sample.txt`，
`git` 的三档计数与高亮标记逐值吻合（1/8、1/4、1/2），并断言不出现视觉稿的固定 `/12`。
**第 230 轮**为 §7.7 的比较视图补 3 条断言（双栏 13px／7px／`max(84, 最长行号文本宽+14)` 几何与文件栏对齐、
只读身份＋完整路径悬停＋切单双栏不动工具栏、长差异场景下左右正文与中栏行号同步滚动）；新增桩
`src/LongDiff.cs` 以让"同步滚动"可测。**第 229 轮**给 1.13 补上 E1 并修掉一处与结论相反的残留：深色主题有一条特异性更高的
`body[data-theme="dark"] .scrim { background: color-mix(… 44% …) }`，把模态背景**压暗**了 ——
与"参考实现不做背景变暗"（代码证据 `IdeGlassPaneImpl.kt` 无变暗绘制 + 参考截图对话框外仍是精确
`#FFFFFF`／`#E9EAEE`）相反；浅色那条 22% 混色则早已被文件末尾的 `background: transparent` 覆盖成死值。
两条都已删除，并在 `verify-ux-reset-layout.cjs` 里按 浅/深 × 三缩放 断言"铺满可视区 + 完全透明 + 承接点击"。
**第 228 轮**修掉一处产品缺陷并锁定性能结论：实时侧没有文档时 `editor` 停在场景默认值 `"markdown"`，
正文于是渲染视觉稿的**样例文档**（标题"Augit 产品规格"、文件栏 `docs/product-spec.md`），而 `live.document`
是 `null` ⇒ 把磁盘上不存在的文件当成真实只读文件显示；现按规格 §6.7 的无文档提示落成 `editor === "empty"`
（只对产品默认场景 `main-project` 生效）。`bindInteractions` 的 63 ms 经逐项计时定位到
`bindMarkdownModes` 38 ms，而那 38 ms 实为**整页首次布局**（不可省）⇒ 首屏链路已到地板
（`performance-report.md` §12）。**第 226 轮**把 T10 的最后一块权威补齐：行号槽填充来自 `DiffLineMarkerRenderer.drawMarker()` 的 `gutterMode`（**全强度 `DIFF_*.BACKGROUND`**，与行的 ignored 柔和底无关；"槽与正文之间的窄带"才用 `editorMode` 的柔和值），单行增删改画 2px 线（`DiffLineMarkerRenderer.kt:34-104`）；Augit 的行号槽是逐行两个 `<div>`，逐行填充可直接落单元格背景，但**梯形**需要新的绘制面。同轮为 T3 补读 `ProgressDialogUI.kt` 全文：进度条是模态 `ProgressWindow` 的构成、无分数时不确定式，建议登记为**有意产品差异**（不补），两处结论均**待用户认可**。**第 225 轮**关闭 T6 的两条 §7.3 项：Markdown 预览读取超过 150ms 时在预览区顶部显示「正在生成 Markdown 预览…」（短读取不闪提示）、失败时保留原文与旧预览并在预览区给出原因、点预览（或 Enter/Space）重试读取；状态进 `live.markdownPreview`，DOM 只按状态渲染，局部同步不重绘正文（原文滚动位置与对照比例保持）。§7.9 #23 的"比较对话框"改标 **不适用**（Augit 用引用树 + 日志筛选选比较目标，不实现该对话框，与 §3.10 同口径），生成器随之新增 **E 不适用** 类别，§2.10 重算为**分母 98**、A 78／B 3／C **0**／D 16／E 1；随后 §7.3 #6/#12 两行也由 `未覆盖` 转 **是**（各有新断言，见 `ui-compliance.md` §2.6 该两行），**T6 因此清零**。**第 233 轮**为 §7.2 第 13 条（大文档与正则的后台执行、150ms 加载阈值、换查询/开关/正文/模式取消旧任务与方向队列、隐藏/关闭失效）补 **7 条断言**，`ui-compliance.md` §2.6 该行由"部分"转 **是**：包一层 `window.Worker` 计数证明只有正则走后台线程（普通文本 0 次，作对照）；逐 20ms 采样证明 145ms 前不出现"正在搜索…"、之后出现且结果照常落地；对照组证明在途期间按下的"下一项"真进方向队列（落地后 `2/8`）；随后 A、B 两次在途任务被"换查询/换开关"取消（A/B 的结果与队列一步都不许出现，最终 `1/4`）；Esc 关闭、`visibilitychange(hidden)`、切 Markdown 预览三条分别证明旧结果失效（隐藏期间不出现计数、恢复可见后重新查询）。同轮订正第 127 轮留下的一处**桩可见性缺陷**：`__augitFindResultDelay` 在 Worker 路径上原本加在结果到达**之后**（那一刻 `stop()` 已抬 `generation`），延迟结束却拿取消前的代次比对 ⇒ 注入延迟下结果恒被丢弃，该路径其实从未可观测；现把注入点移到 **Worker 报告就绪之前**（值为空时生成的代码与原来逐字相同，生产零开销）。**第 234 轮**为 §7.8 第 3 条（提交详情正文独立滚动、键位只动详情、重复选择与收放保持阅读位置、切换提交回顶部、文件历史返回恢复位置、只读、超 20 MB 说明原因）补 **3 条断言**，`ui-compliance.md` §2.6 该行由"部分"转 **是**：30 行正文让详情真的可滚动（`max=1186`、末行在场）后，真实滚轮与 Home/End/PageUp·Down/方向键只移动详情、选中行全程不变；重复选择同一行与收折详情都保持 150、往返切回后 `top===0` 且目标仍可滚动（非平凡）；超 20 MB 那条**一挂就挂出了产品缺陷** —— 宿主原因原先只写在变化文件栏，详情栏只剩行头、正文消失却无解释，且 `detailHtml` 留空会让任何重绘退回占位模板（固定取 `history.commits[0]`）把第一个提交的主题画进详情栏；现两栏都写这条原因并同时进 `live.commitDetails`（桥接把"提交信息超限"与"变化文件列表超限"折叠成一条 `reason`）。**第 235 轮**为 §7.8 第 5 条（详情操作行按字宽/高度分配并在放不下时紧凑隐藏、被收起时焦点交给变化文件列表；变化文件右键菜单的 `Shift+F10`／菜单键入口、`Esc` 关闭并恢复列表焦点、只对文件行提供）补 **3 条断言**，`ui-compliance.md` §2.6 该行由"部分"转 **是**：宽窗口 310px ≥ 280 时操作行可见，底部工具窗压到 140px（面板高 102）与收窄到 1180 窄栏档（详情栏 210）两条路径都触发隐藏并把预留高度降到 0；焦点先在操作行上时收起后落到 `.changed-files`；两种键盘菜单键都开同一菜单、`Esc` 关菜单并交回列表焦点、右键目录行不开菜单也不动选中。本轮踩到两处"判据会测反/空过"：默认 1180 宽度本身就在窄栏档（操作行本就收起），基线必须用宽窗口；"打开菜单不打开比较"原先读 `window.__augitLive.editor`，日志场景里它是 `undefined` —— 两边都 `undefined` 会空过，改成活动底部标签与提交行数不变。**第 236 轮**把 §7.8 第 7、8、12 条（竖条用右箭头替换放不下的按钮、弹层复用整组动作与禁用状态并支持左右键/Tab/Enter/Space/Esc、极短区域不画越界按钮、筛选栏收纳箭头消失时的焦点交接）一轮做完，`ui-compliance.md` §2.6 两行都转 **是**：三档竖条高度（202/82/42px）下可见集合与"可见按钮底边 ≤ 高度−4"逐档断言，42px 档连箭头也放不下 ⇒ 只留「隐藏分支」；弹层是 `buttons.slice(1)` 的克隆（禁用状态逐项相同）、方向键与 Tab 各走一步、Esc 关层并把焦点交回箭头；筛选栏 1280 宽聚焦箭头后放宽到 1500，焦点落到最后一个可见可用动作。同轮**又挂出一处真实缺陷**：区域 Tab 顺序处理器（document 捕获）只跳过 `[data-augit-overlay]`，漏了顶层 `popover`，Tab 被处理两次、在弹层里每按一次跳过一项（实测「新建分支…」→「获取」，跳过「我的分支」）——守卫补上 `[popover]:popover-open` 后与方向键一致。**第 237 轮**把 §7.8 第 12 条剩的两半（窄栏收纳菜单支持键盘选择、选完把输入焦点交给相应已有控件）补成 **2 条断言**，`ui-compliance.md` §2.6 该行由"部分"转 **是**，并又修掉两处真实缺陷：① `bindRegionTabOrder()` 的守卫漏了 `details[open]` ⇒ 焦点从收纳箭头按 Tab 会被带出菜单（键盘够不到收纳项），守卫再补 `details[open]` 后 Tab 依次落到「日期」「路径」；② 四个筛选弹层的 `showPointerContextMenu` 没给 `focusFirst: true` ⇒ 收纳项按回车后 `details` 一关、被点按钮消失，`document.activeElement` 掉到 `body`，键盘用户彻底失去位置；补上后可见控件与收纳项两条路径的焦点都落在弹层首项。**第 238 轮**把 §7.8 第 13 条剩的两半（列宽来自同一套文字度量、长引用不吞掉后续列）补成 **4 条断言**，`ui-compliance.md` §2.6 该行由"部分"转 **是**：宽栏 5 条轨道且引用/作者/日期列宽逐值等于用同一 canvas 度量复算的 51/27/110px，窄栏换短日期（`9/15`）并把引用列整列让位而作者/日期仍可见，注入长引用后列宽停在 128、注入超长作者名后停在 96。同轮**又挂出一处真实缺陷**：右角「刷新」只 `loadHistory()`（写状态）不 `refresh("bottomTool")` ⇒ 宿主已下发新数据而 DOM 一动不动（实测 `__historyCalls` 2 → 3、标签仍是旧值），与所有筛选路径走的 `reloadHistoryKeepingFocus()` 自相矛盾；现改走同一路径并在区域替换后按无障碍名把焦点放回新按钮。**第 239 轮**把 §7.8 第 22 条（多轨窄栏的局部横向滚动、横向位置保持、往返恢复纵横并归位）补成 **3 条断言**，`ui-compliance.md` §2.6 该行由"部分"转 **是**：120 条 + 超长作者名下行为 254 > 列表 239（标题列保底 120px），把 `scrollLeft` 推到上界后改选/重复点击/相同快照刷新都保持，往返文件历史后纵横都回到 300/15，放宽窗口后横向归位 0。同轮修掉两处真实缺陷：① 横向位置**根本没有状态**（只记纵向）；② 往返回来纵向被写成 0 —— 用 setter 追踪 + 列表 `scrollTop` 赋值栈追踪定位到 `applyTypography()` 的 `restoreScrollAnchors()` 把"改字号前捕获（列表刚重建、位置为 0）"的锚点原样回填，冲掉了刚恢复的位置；实时层改为把待恢复位置留在 `pendingHistoryScroll` 里、渲染后/下一帧/200ms 窗口末各对齐一次，窗口内的滚动事件不当作用户动作（共享视觉稿的锚点逻辑未改）。**第 240 轮**把 §7.8 第 14 条（类型图标跨上下文共用、类型色不被状态色覆盖、辅助技术名称保留状态符号与文件名）补成 **3 条断言 + 1 处新实现**，`ui-compliance.md` §2.6 该行由"部分"转 **是**：五个上下文（项目树／标签／日志变化文件／快速打开结果／Changes 列表）都非空，4 个同名文件跨 ≥2 上下文且图标 class 与内部路径数据逐字节相同；深/浅两套主题下类型色逐值等于权威 expui 取值、而文件名用 `--augit-file-modified` 且与之不同值；行 `aria-label` = 状态符号（权威 `GitChangeType` 的 M/A/C/D/R/U/T）＋ 文件名，未跟踪文件不造符号。为了拿"快速打开结果"当第五个上下文，**先撞出一处更严重的缺陷**：`openSearchOverlay()` 只手动挂节点、而 `main-project` 一类场景模板里没有 overlay ⇒ `renderSearchOverlay()` 的定点刷新找不到替换源，Ctrl+P／Ctrl+Shift+F 的**结果从不落到 DOM**（既有断言只查浮层是否打开）。修法：打开时置 `live.searchOpen`、`shell()` 据此产出 overlay 替换节点、`liveSearchOverlay()` 补 `live-overlay` 类、`closeLiveOverlay()` 清状态（否则"Esc 关掉、一刷新又回来"）。**第 241 轮**把 §7.8 第 2 条（外观应用下的提交列表：按字高扩展、图标不变、不重叠，保持顶部锚点/横向偏移/选择，不触发 Git 查询、不重建提交图）补成 **2 条断言**，`ui-compliance.md` §2.6 该行由"部分"转 **是** —— §7.8 至此**全部 22 行无"部分"**。实测字号 13→20：行高 26→31、每行提交图 `viewBox` 高同步跟随（宽度 29 与图标 16px 不变）、行内四列无重叠；首个可见提交与相对偏移（`h-15`／0）、`scrollLeft` 15、选中项 `h-15` 全部保持，`git/history`／`git/status` 调用数不变、行节点身份保持。同轮记下两处口径：提交图几何**仍不随行高缩放**是 `intellij-platform-ui-reference.md` 已登记的落地缺口（§8.3.2 要求按 `行高 ÷ 22` 等比缩放，实现未跟随），因此故意不断言"节点半径不变"；本轮只改测试，运行时哈希不变。**第 242 轮**为 §7.5 第 2、4 条（棋盘格只覆盖图片矩形、外围画布纯色；按钮缩放保留焦点、缩放/拖动/方向键只影响图片）补 **2 条断言**，`ui-compliance.md` §2.6 两行都转 **是**：画布 `background-image: none` 且底色等于 `--augit-panel`，棋盘格画在 `<img>` 自己的 `conic-gradient` 背景上（两色停 = panel／panel-muted、`16px 16px`／`8px 0px`）；焦点在「放大」上按真实 Enter 后比例 1 → 1.25 而焦点不离开按钮，放大到 200% 后拖动/方向键只改图片位置并夹在画布内，画布矩形、树滚动、页面滚动与状态栏文本全程不变。记一处测试教训：适应区域时图片完全落在画布里，`pointerdown` 会直接返回、方向键平移也被夹回 0，因此"先放大到 200%"是断言图片会移动的前提，否则判据恒假。**第 243 轮**把 §7.5 第 7 条实时侧的四半（>150ms 才在画布中心提示且不改工具栏/标签/面板尺寸、不抢当前文档与焦点、旧请求失效、关闭后晚到位图释放）补成 **2 条断言 + 1 处新实现**，`ui-compliance.md` §2.6 该行转 **是**：新增 `live.imagePreview` 状态与 150ms 提示（与 Markdown 预览同一套"状态→DOM"做法，并在重绘后按状态补回）。同轮修掉两处真实缺陷：① 实时侧图片/JSON 的标签条是**视觉稿样例标签**（`href="image-preview.html"`、无 `data-tab-id`）⇒ 点击会 404、关闭/中键/激活全失效；② `closeTab()` 不推进读取令牌 ⇒ 关闭标签后晚到的位图会把标签**复活**。第一次全量跑还抓到本轮自己引入的回归（同步逻辑删掉了静态 `?image-state=loading` 的提示），已用 `data-live-hint` 标记区分并复跑通过。**第 244 轮**把 §7.5 第 6 条（外部更新复用预览、保留手动缩放与位置、适应模式按新尺寸重算、即使大小不变也重新解码）补成 **2 条断言**（第 8 条的"缩小平滑采样 + 100% 原像素"也一并断言），`ui-compliance.md` §2.6 第 6 行转 **是**。同轮修掉两处真实缺陷：① `openDocumentTab()` 命中已有标签时直接 return，把重读结果丢掉 ⇒ **所有**文档的外部更新都不会换内容（图片的"重新解码""损坏后显示信息页"因此做不到）；现复用同一标签但替换 `document`/`editor`/`title`。② `image-preview.js` 的 `load` 处理器无条件打回适应区域 ⇒ 手动缩放与位置在每次重读后丢失；现按文档路径把视图状态记在 `live.imageView` 上并在绑定后恢复（关闭标签时丢弃）。§7.5 第 8 条的"平滑图后台生成、先快速采样再原位更新"登记为**无法取证**（Augit 无两段式重采样、本仓库定向检索找不到对应实现）⇒ 该行保留"部分"并等用户口径。
**第 245 轮**为 §7.6 第 8 条（外部修改文件后保留复选状态、仅更新状态标记与 diff 版本）与第 14 条（校验/Hooks 失败复用提示行、危险色、长原因可悬停、保留草稿与勾选、等待与失败期间不抢焦点）补 **3 条断言**，`ui-compliance.md` §2.6 两行都转 **是**（§2.10：分母 83 → 81、A 62 → 60）。同轮又挂出两处真实缺陷：① 改动列表的增量补丁 `patchChangesList()` 把行状态类名写成 `live-file-status-<kind>`，而 `mockup.css` 只定义 `.file-status-*` ⇒ 外部更新走增量补丁后文件名**丢掉 Git 状态色**（实测补丁后该类名整个消失；勾选保留与查询次数本来就对）；② `refreshAfterEvent("side")` 整块替换侧栏会重建提交信息 `textarea` ⇒ 等待与失败期间焦点掉到 `document.body`，且 `restoreChangesState()` 恢复草稿的 `box.value = …` 赋值会把光标推到末尾，"失败后接着改信息"在界面上做不到。现把焦点意图与光标位置当用户状态记进 `live`（`commitSelection` 由输入/按键/选择/点击/聚焦事件记录，`commitFocusBeforeRender` 由 `refresh()` 在替换**之前**捕获，提交动作自身也显式置一次），`restoreChangesState()` 只在这次重绘确实弄丢焦点时恢复、用完即清 ⇒ 焦点在编辑器/改动行/工具入口时**不抢**。两条都做了负向验证（关掉光标恢复 ⇒ 光标从 3 变 7；去掉"重绘前焦点在提交区内"的信号 ⇒ 焦点被从「项目」抢走）。
**第 246 轮**为 §7.6 第 5 条（悬停独立维护、选中优先、只重绘命中行、滚动/折叠/增删/行高变化后重命中、空白区不算最后一行、隐藏/禁用/销毁清除、悬停不改状态且不查 Git）补 **4 条断言**，`ui-compliance.md` §2.6 该行转 **是**（§2.10：分母 81 → 80、A 60 → 59）。
**第 247 轮**为 §7.6 第 10 条（字号下各行按字高扩展、图标与复选框固定、动作放不下换两行、输入框至少保留提示行与一行正文、上一次提交在空间不足时省略）补 **4 条断言**，`ui-compliance.md` §2.6 该行转 **是**（§2.10：分母 80 → 79、A 59 → 58）⇒ **§7.6 全节 14 行无"部分"**。三档字号实测：行高 27 → 31 → 39、选项行 39 → 46 → 54、动作按钮 30 → 33 → 41 按字高增长，而工具图标 16×16、设置按钮 27×30、Amend 复选框 15×15 逐值不变；字号 40 时两个文字动作按原顺序换两行（左边缘对齐、不重叠）而设置图标仍留在第一行的行带内；面板高 420／480 时输入框仍保留提示行与一行正文（54／57 ≥ 行盒 16／51），让位的是列表（344 → 117 → 10px）。同轮**挂出并修掉一处真实缺陷**：实时外壳的 `.commit-last` 槽位渲染的是**分支名**（无图标、无 `title`），而设计基线／视觉稿（已另起静态服务复测 `commit-changes.html`）是 `<a href="git-history.html" title="上一次提交">上一次提交</a>` ＋ 历史图标 ⇒ 装不下文字时 `icon-only` 只剩一个空槽；现按视觉稿渲染并把点击接到 Git 历史工具窗口（日志已可见时不重绘，重复点击不折叠底部区域）。四条断言都做了负向验证（钉死行高、次按钮钉回首行、输入框压到 8px、拆掉图标与 `title`）。
**第 248 轮**为 §7.7 第 4 条（第一次请求加载时保留旧 Diff 正文约 200ms，随后在同一正文区域替换为加载状态或新结果）与第 5 条（工具栏左侧上一处/下一处/搜索/上一个文件/文件计数/下一个文件，右侧差异摘要/忽略空白/双栏单栏/设置，Tab 顺序与视觉顺序一致）补 **5 条断言**，`ui-compliance.md` §2.6 两行都转 **是**（§2.10：分母 79 → 77、A 58 → 57、D 16 → 15）。同时**挂出一处实现落差**并修掉：实时外壳的 Diff 工具条与规格是两套东西——缺「查找」「忽略空白」「设置」，右侧显示的是"N 行"而不是差异摘要；现由 `mockup.js` 新增的 `liveDiffToolbar()` 让就绪/加载/空差异三态共用规格顺序，摘要按"连续变更行算一块"从 `diff.rows` 现算（与导航的 `diffChangeBlocks()` 同口径）。「忽略空白」接成真实差异选项（`loadDiff` 继承 `live.diffOptions`，切换后强制重查，实测 `__diffWhitespace` 三态变化），「设置」按应用约定打开设置对话框；加载时序实测：阈值前 6 帧旧正文原样（79 字符）、标记 152ms 后同区换成加载状态、29 帧不退回旧正文、结果落地换成新正文，其他区域矩形逐值不变。5 条断言都做了负向验证（打乱顺序/只改外观不重查/摘要换成行数/拆掉设置处理/阈值改 0）。**登记两项待用户裁决**：① 本条规格写"约 200 毫秒"，Augit 用全局 150ms 阈值（§6.5），而权威 `ProgressUIUtil.DEFAULT_PROGRESS_DELAY_MILLIS` 本地实测 **300L** ⇒ 行为已断言、数值三方差异待口径；② 「查找」在 Diff 正文上无实现（共享查找条只服务 `.document-view > .code-view`），已按规格位置渲染但**禁用并写明原因**，是否投入 Diff 正文查找待口径。
**第 249 轮**为 §7.7 第 6 条（差异计数与单双栏一致、历史与引用比较沿用同一规则）与第 9 条（跨文件同步与查询期间禁用差异箭头）补 **3 条断言**，`ui-compliance.md` §2.6 两行都转 **是**（§2.10：分母 77 → 75、A 57 → 55）。两条"只缺断言"的条目一共挂出**三处真实缺陷**并修掉：① `liveDiffView` 的加载分支把**文件箭头也禁用**（与规格"文件箭头和 Changes 仍允许改选"相反）⇒ 只禁用差异箭头；② `moveDiffFile()` 不设加载标记 ⇒ `live.diffLoading` 永远为假、"查询期间禁用差异箭头"在工作区 Diff 的文件切换上从未发生 ⇒ 切换前后调度/清除标记；③ `switchDiffMode()` 只传 `mode`、丢掉当前正文的 revision／commit／ignoreWhitespace／version ⇒ **历史比较切单栏后 `live.diff` 变 null、编辑区退回视觉稿样例数据**（实测工具条出现 `1/42 个文件`、正文是样例 XML、再切回双栏也回不来）⇒ `loadDiff()` 记住 `live.diffParts`、`switchDiffMode()` 带上它，单双栏命中同一份补丁缓存（`diffPatchKey` 不含 mode），既不重查 Git 也不丢比较上下文。实测：查询在途时差异箭头禁用（并写明原因）而文件箭头可用、Changes 仍可勾选改选，落地后勾选/标签/几何不丢且不恢复旧定位；用两个新桩旋钮让历史比较与引用比较返回两处 Modified，测试独立复算 DOM 块数 ⇒ 分栏/单栏都是 2 块＝摘要 `2 处差异`、导航 `data-diff-total === "2"`。三条都做了负向验证。
**第 250 轮**为 §7.7 第 12 条（双栏留白 13px、中栏左右 7px、中栏按最长行号度量；改等宽字号只重排正文与行号）与第 14 条（Markdown 与 JSON 的默认 Git 页面仍显示磁盘真实文本 diff，可从工具栏打开修改后预览）补 **2 条断言**，`ui-compliance.md` §2.6 两行都转 **是**（§2.10：分母 75 → 73、A 55 → 54、D 15 → 14）。第 12 条纯补断言：字号 13 → 20 时 `--code-line-height` 22 → 34px、正文行盒 22 → 34、中栏按新字体重新度量 93 → 135（与测试同一公式复算值逐值相等），留白 13px／槽内距 7px 不变，`git/diff` 请求数与文件读取数都不增加，草稿/选中/勾选/当前正文与行数逐项保持，还原后回到基线。第 14 条**挂出并修掉一处真实缺陷**：改动工具窗工具栏的「预览」在实时外壳里**没有任何处理者**（实测点击后编辑区仍是差异、读取次数 0、连"未接线"都不记录）⇒ 现在按选中的改动文件打开只读文档（Markdown 显式切预览模式、JSON 用 `liveJsonDocument()` 自带的格式化视图）；同时断言默认页确实是**文本 diff**（正文里是 Markdown 源标记 `**加粗**`、没有渲染后的 `<strong>`、没有 `.markdown-preview`）。两条都做了负向验证（中栏宽度钉死、拆掉预览接线）。
**第 251 轮**为 §7.7 第 7 条（双栏文件栏标注基准/当前与只读身份；**单栏上下排列**、路径跟随来源；仅重排文件信息与正文）、第 8 条（**首次边界提示不查询、不移动正文**）与第 10 条（`Esc`／改方向／选文件／切模式／隐藏关闭／重载／改布局都撤销待跨文件状态；首尾不循环、只给局部说明）补 **4 条断言**，`ui-compliance.md` §2.6 三行都转 **是**（§2.10：分母 73 → 70、A 54 → 51）。本轮**挂出并修掉三处真实缺陷**：① 撤销矩阵此前只有"再按同方向"一条路径 —— `Esc`、选文件、切模式、重载、改布局、关闭都不清 `live.diffBoundaryHint`（DOM 被重绘抹掉而状态留着 ⇒ 再次同方向点击**直接跨文件**）⇒ 新增 `clearDiffBoundaryHint()` 并接到 Esc（只在真有提示时消费）、`loadDiff`、`selectChangeRow`、`applyRailAction`、窗口 resize、`activateTab`、`closeDiff`；② 列表首/尾没有相邻文件时只是静默清掉提示，没有规格要求的"已到首/尾"局部说明 ⇒ 复用同一提示位显示；③ `diff-boundary` 场景的**样例**提示节点被注入**实时**正文（状态为 null 时 DOM 里仍有一条「再次点击可进入下一个文件」）⇒ 样例标记只在静态视觉稿注入（`&& !live`）。实测：单栏文件栏来源/目标上下排列且宽度同为 787、文件栏两行；长差异里首次提示不增加任何宿主调用且滚动/首行/正文区矩形/索引不变；首尾分别给出「已到改动列表的首个/最后一个文件」且路径不回卷。四条断言都做了负向验证。另**登记一处观察待用户口径**：提示按视觉稿的绝对定位贴在正文内容顶部，长差异滚到边界块后可能落在可视区之外。**§7.7 至此只剩第 11 条的"变更连接区"**（实现里尚不存在，属 T10 待产品口径）。本轮**只改 harness**：先做行为实测再写断言——Chromium 在滚动、列表增删、行高变化与折叠后**会**按指针位置重新命中（指针全程不动时命中行始终等于几何上在指针下的那一行），因此现有纯 CSS `:hover` 实现是对的，不需要新增 JS 悬停层；唯一坑是 `display:none` 的隐藏行会留一个**看不见**的陈旧 `:hover` 匹配，所以判据改成"可见行的底色相对基线的变化"（顺带对将来改成 JS 维护悬停类的实现也成立）。四条断言覆盖：悬停期间零 DOM 变更＋行节点身份不变＋同一行内移动不重复命中；四类布局变化后按指针重命中；列表内空区（最后一行之下）两侧都无命中；切走工具窗口后列表被销毁、切回来不恢复旧悬停。四条都做了负向验证（四种扰动分别让对应断言失败）。另登记一处口径：该条的"禁用"在 Changes 列表里没有可触发路径（行始终可用，折叠走 `[hidden]`），最接近的"Git 运行时不可用"由 §7.18 管。

## 8. 非界面目标项（不计入上面的功能归类）

| 项 | 状态 |
| --- | --- |
| 三类场景性能基线（空仓库/已有仓库/大仓库） | **已建立**（第 213 轮，`performance-report.md` §10，`measure-performance.ps1`）；后续为持续优化 |
| 首屏链路与 WebView2 内存基数优化 | **均已达当前技术基线的地板（第 228／229 轮，附实测）**：第 227 轮把"窗口 → 首屏"的 ~680 ms 拆成 窗口 ~300 ms ／ 环境＋控制器 ~360 ms ／ 页面载入 ~40 ms ／ 页面 boot ~250 ms（`performance-report.md` §11），并否掉一次"去掉导航前 await"的尝试（导航提前 60 ms 但页面侧后移 40 ms，首屏无净收益）。下一步已由第 228 轮执行：页面 boot 细分为 `loadDocument` 38 ms／mockup 求值 ~17 ms／**`renderScene` 4 ms**／**`bindInteractions` 63 ms**／`rebindAfterRender` 3 ms；两个候选（设置读写源生成序列化、把 `bindInteractions` 延后）分别**因默认值语义回归**与**"可交互但无绑定"先例**被否/缓做（见 §12）。`bindInteractions` 63 ms 经逐项计时定位到 `bindMarkdownModes` 38 ms，而那 38 ms 是**整页首次布局**（新解析完的 DOM 本来就要布局一次，`setRatio` 的 `clientWidth` 只是触发点）⇒ 不是可省的工作；除非缩小首帧 DOM，该链路已到地板。**WebView2 内存基数已量化（第 229 轮，`performance-report.md` §13）**：GPU 186–233 MB／浏览器 ~124 MB／渲染 ~95 MB（页面 JS 堆只 4 MB）／主进程 ~57 MB ⇒ 由 Chromium 多进程架构 × 窗口像素面积决定，与 §0 的判断一致；`--in-process-gpu` 与 `--disable-gpu-compositing` 都会让 WebView2 起不来，`--disable-gpu` 被忽略 ⇒ 没有安全的开关级优化，**无剩余可执行项** |
| 十万提交历史性能 | 缺 fixture（本机无法快速生成），沿用第 0 节早期抽样并标注非本轮实测 |
| Windows 10 22H2 实机兼容性 | 未验证，无实机条件 |

## 9. 修订记录

- 第 214 轮建立本文件（目标 Round 3）。来源：`11-surface-audit.md` 的 11 区结论、`ui-compliance.md` §1.4／§2.10／§3.2／§3.4、`10-backlog.md` 的裁决表、`design-system.md` §2/§8.3、各分册的权威出处。
- 同轮修订两处规范矛盾（T7／T8）：`design-system.md` §8.3 的树悬停段落与 `intellij-platform-ui-behavior.md` §3 的四行旧状态，按已记录的用户裁决（第 100／116 轮）与已实施事实（第 11 模块）改标，去掉"待实施/待核实"字样；行为索引 §4 的第 2/3/4 项同步改标（第 2 项留下一处新登记的待办 T9）。
- 归类判定原则：**"权威有、Augit 也有且按权威落地"= 已对齐；"权威有、产品明确不做"= 有意差异；"权威概念不适用"= 不适用；"权威材料不在本地"= 无法取证；其余未决 = 待处理**。不把"实现已有、只缺断言"写成"未实现"，也不把"未验证"写成"已对齐"。
- 第 215 轮：关闭 T5。`ux-spec` §7.18 第 2 条（提交/Git 历史入口禁用 + 悬停原因）实现并断言：`web/src/mockup.js` 的 `rail(active, gitUnavailableReason)` 给这两个入口写 `aria-disabled`／`title`（项目/搜索/终端保持可用），`web/src/live-data.js` 的 `bindToolRail()` 对禁用入口直接返回；视觉稿场景 `git-unavailable` 传同一条原因，静态基线与实时降级页一致。`ui-compliance.md` §2.6 该行转"是"，§2.10 C 类 11→10。
