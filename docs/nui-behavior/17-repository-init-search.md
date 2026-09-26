# 17 · 仓库初始化与全仓搜索（第 9 区）

本册是 `11-surface-audit.md` 第 9 区的采集结果，场景：`repository-init`、`repository-search`、`git-unavailable`、`search-limited`。
口径同前：每条都带 checkout（`/mnt/d/github/intellij-community`，commit `576e328`）里的文件:行号。

**第 155 轮是采集轮，没有改产品代码**：搜索侧的交互核对后**本来就对齐**（见 §2），初始化侧的界面契约已采全但**整条路都没接线**（宿主缺 `git/init`），故并入 `10-backlog.md` §三·补 第 1 项一起做。

## 1. 仓库初始化（`GitInit`）

权威是 `plugins/git4idea/backend/src/actions/GitInit.java`（一个 `DumbAwareAction`）：

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 动作启用条件 | `update()`：`project == null \|\| project.isDefault() \|\| TrustedProjects.isProjectTrusted(project)` ⇒ 只有**受信任的项目**才 enabled+visible | `GitInit.java:38-43` |
| 目标目录 | 弹**单目录选择器**（`createSingleFolderDescriptor`），标题/描述取 `init.destination.directory.title`／`.description`，`setShowFileSystemRoots(true)`、`setHideIgnored(false)`、`setEnvironmentRestricted(true)`；起点是当前选中目录，取不到则项目根 | `:45-64` |
| **何时确认** | **只有目标已在 Git 下才确认**：`GitUtil.isUnderGit(root) && Messages.showYesNoDialog(… "init.warning.already.under.git"（转义后的目录 URL）…, "init.warning.title", 警告图标) != YES` ⇒ 返回。**不是仓库时没有任何确认，直接初始化** | `:66-74` |
| 初始化过程 | `new Task.Backgroundable(project, GitBundle.message("common.refreshing"))` ⇒ **后台任务**（标题 "Refreshing"），不占模态 | `:76-78` |
| 失败 | `VcsNotifier.notifyError(INIT_FAILED, GitBundle.message("action.Git.Init.error"), result.getErrorOutputAsHtmlString(), true)` ⇒ **错误通知**（带 Git 的错误输出） | `:80-83` |
| 成功 | 项目非默认时 `refreshAndConfigureVcsMappings(...)`：刷新该根目录的 VFS → `ProjectLevelVcsManager.setDirectoryMappings(VcsUtil.addMapping(...))` → `VcsDirtyScopeManager.rootDirty(root)` | `:88-92`、`:94-99` |

### 1.1 与 Augit 的差异（第 205 轮已消除）

Augit 原来的 `repository-init` 场景是一个**模态确认对话框**（`初始化 Git 仓库` / `D:\projects\notes 不是 Git 仓库` /
`初始化会创建 .git 元数据…` / 继续仅浏览、初始化仓库）。第 205 轮已按权威重做为**入口 ＋ 警告**两态，
下表两处差异随之消除：非仓库时点「创建」**直接初始化**（没有二次确认），确认只在“目标已在 Git 下”时出现
（Yes/No，文案带目标目录）。实时外壳另接上 Git 主菜单入口与系统目录选择器。

**第 204／205 轮补记（本项已结案）**：宿主侧 `git/init` 按上表接线（`path`／`confirm` 两参数；只有“目标已在 Git 下”回
`requiresConfirmation`，非仓库直接初始化；成功后作废 Git 解析与状态缓存；+3 条宿主单测）；界面侧把
`repository-init` 实时化 —— Git 主菜单「创建 Git 仓库…」（权威入口在 VCS 菜单）、非 Git 工作区自动给出入口一次、
「选择目录…」走系统目录选择器、进行中冻结全部动作、失败在窗口内写明 Git 的错误输出、成功后关窗并重读状态／历史／引用。
样例页也改成权威的两个状态（入口 ＋ “已在 Git 下”的 Yes/No 警告），由新增的 `verify-ux-repository-init` 覆盖。

**为什么当时不落地**：宿主缺 `git/init`（`IGitServices.InitializeAsync` 已实现，`ShellBridge` 没有该方法，`10-backlog.md` §三·补 第 1 项），所以 `repository-init` 在实时外壳里根本走不到 —— 先改确认层会得到"改了也没人能点"的界面。已把上面这张表作为**接线时的界面契约**并入该 backlog 项。

## 2. 全仓搜索与搜索浮层

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 结果数量阈值 | 高级设置 `ide.find.result.count.warning.limit`，**默认 1000**（只是**提示阈值**，不是硬上限） | `platform/platform-impl/resources/intellij.platform.ide.impl.xml:1491`；`platform/usageView/src/com/intellij/usages/UsageLimitUtil.java:16-21` |
| 到限后的行为 | **弹警告对话框询问是否继续**：标题 `find.excessive.usages.title` = **"Too Many Results"**、正文 `find.excessive.usage.count.prompt` = **"Too many results found. Are you sure you wish to continue?"**、按钮 `Continue` / `Abort`（警告图标）；返回 `CONTINUE`／`ABORT` | `UsageLimitUtil.java:26-34`；`platform/usageView/resources/messages/UsageViewBundle.properties:84,86,87` |
| 输入框→结果列表的键转发 | **只转发 Up／Down**：`ScrollingUtil.installMoveUpAction(myResultsList, getSearchField())` ＋ `installMoveDownAction(...)` | `platform/lang-impl/src/com/intellij/ide/actions/searcheverywhere/SearchEverywhereUI.java:906-907` |
| Home／End／PageUp／PageDown | 由 `ScrollingUtil.installActions` 装在**列表自身**的 `WHEN_FOCUSED` 映射上（`VK_HOME`→首行、`VK_END`→末行、`VK_PAGE_UP/DOWN`→翻页）；输入框聚焦时这些不生效 —— **Home／End 仍是文本框的光标行为** | `platform/platform-api/src/com/intellij/ui/ScrollingUtil.java:275-281`、`:345-357` |
| 是否回绕 | 转发动作的 `cycleScrolling` 取 `UISettings.getInstance().getCycleScrolling()` ⇒ **是否回绕取决于用户的"循环滚动"设置**，不是固定行为 | `ScrollingUtil.java:328-338` |
| 空查询的浮层高度 | 空模式切 `ViewType.SHORT`（`SearchEverywhereUI.java:938`），SHORT 时把弹层 **pack 到内容最小尺寸**、有查询时用 FULL 恢复首选尺寸 | `platform/lang-impl/src/com/intellij/ide/actions/searcheverywhere/SearchEverywhereManagerImpl.java:407-411` |
| 空查询的"搜索中"提示 | `setSearchInProgress(StringUtil.isNotEmpty(getSearchPattern()))` ⇒ 空模式不显示"搜索中" | `SearchEverywhereUI.java:1539` |
| 空查询的结果 | 空模式**不是"什么都不显示"**：各贡献者照常给出默认条目（Files 档即文件列表） | 同上（`:1539` 的取反） |

### 2.1 与 Augit 的对照

| 项 | 结论 |
| --- | --- |
| 阈值 1000 | **本来就对齐** ✓ Augit 的截断阈值也是 1000（`ux-spec.md:593` 与 `search-limited` 场景），权威默认值同样是 1000 |
| 到限后的行为 | **第 210 轮已对齐**：界面弹权威的「结果过多」（标题／正文／Continue／Abort，Continue 为默认按钮），宿主 `search/text` 增加 `offset`／`limit` 分页；继续 = 按页取回并追加且不再提示（权威 Continue 后也不再提示），中止 = 保留已有结果并取消搜索（权威 Abort 即 `cancelCurrentSearch()`）。**登记差异**：权威在**同一次**后台搜索里继续，Augit 用分页请求表达同一结果（保持单次响应有界） |
| 键盘导航 | **本来就对齐** ✓ Augit 的浮层只处理方向键与 `Enter`／`Esc`；权威也只把 Up／Down 从输入框转发给列表，Home／End 留在文本框、PageUp／PageDown 不生效 |
| 回绕 | **修正第 153 轮的说法**：当时写"到端点不回绕"过于绝对 —— 权威是否回绕取决于 `UISettings.cycleScrolling`。Augit 用钳制，且**没有"循环滚动"这个设置**（新增设置＝新增功能）⇒ 记为"已知且有意保留的差异" |
| 空查询的浮层高度 | **本来就对齐** ✓ 权威 SHORT 时 pack 到内容最小尺寸；Augit 的规格正是"空态只保留标题和输入框所需高度，结果出现后向下增长" |
| 空查询的结果与"搜索中" | **本来就对齐** ✓ 空模式不显示"搜索中"、但仍有默认条目；Augit 的宿主对空查询返回文件列表（`RipgrepSearchService` 的 `query ??= string.Empty` 与 `ScoreFile` 的空串分支），界面照常渲染（`liveSearchOverlay` 只在 `query.length > 0` 时才显示"未找到结果"） |
| 结果的呈现与排序 | **已对齐（第 159 轮结案）**：**弹层**表面（Find popup／Search Everywhere 文本页签，Augit 的对应物）用扁平行模型 `FindPopupItem`，**按文件路径排序**、当前结果所在文件置顶（`FindPopupResultsAutoloadHandler.kt:69-80,145-147,349`）；Augit 的扁平行＋ripgrep 按文件连续输出同构 ✓。而**按文件分组的树**属于 Find in Files **工具窗口**（`UsageViewSettings.kt:20-27` 的 `isGroupByFileStructure = true`），那是 Augit 没有的另一个表面 |

## 3. `git-unavailable`（Git 缺失或版本过低）

| 项 | 权威 | 出处 |
| --- | --- | --- |
| **受支持的最低版本** | `GitVersion.MIN = SystemInfo.isWindows ? new GitVersion(2, 19, 2, 0) : …` ⇒ **Windows 上 2.19.2**；`isSupported()` = `compareTo(MIN) >= 0` | `plugins/git4idea/backend/src/config/GitVersion.java:50`、`:162` |
| 检测过程的标题 | `git.executable.detect.progress.title` = "Detecting Git Executable"、`git.executable.version.progress.title` = "Identifying Git Version"、`git.executable.version.is` = "Git version is {0}" | `plugins/git4idea/shared/resources/messages/GitBundle.properties:592-594` |
| 缺失/过旧 | `git.executable.error.file.not.found` = "No such file: {0}"、`git.executable.error.bash.not.found` = "Cannot find bash executable" | 同上 `:595-596` |
| **可提供的动作** | `git.executable.install.available` = **"Install Git {0}"**、`git.executable.new.version.update.available` = **"Update to Git {0}"** | 同上 `:597-598` |

**与 Augit 的对照**：

- Augit 的门槛是 **2.40**（`git-unavailable` 场景与设置页的最低版本说明，`ui-compliance.md:798` 有断言），比权威的 2.19.2 **更严**。这是 Augit 的产品决定 —— 它用到的 Git 能力（`worktree`、`--path-format` 一类）晚于 2.19，把门槛降到权威值会让运行期出现"命令不存在"。⇒ **登记不改**（属能力门槛，不是界面交互）。
- 权威在缺失/过旧时给的是 **"Install Git {0}"／"Update to Git {0}"** 这类**下载安装**动作；Augit 只给"配置 git.exe"并提供检测结果与最低版本说明。下载安装属**新增能力**（联网拉取安装包），按边界**不新增** ⇒ 登记。

### 3.1 入口禁用（第 215 轮落地，`ux-spec` §7.18 第 2 条）

第 402 轮实测的缺口是：`git-unavailable` 下 5 个 rail 按钮里 `aria-disabled="true"` 的数量为 **0**（`ui-compliance.md` §2.6 §7.18 第 2 条记为"未覆盖"）。第 215 轮按 `ux-spec` §7.18 落地：

| 位置 | 改动 |
| --- | --- |
| `web/src/mockup.js` | `rail(active, gitUnavailableReason)`：原因非空时给「提交」「Git 历史」写 `aria-disabled="true"` ＋ `title` 原因；项目（树）、搜索（ripgrep）、终端都不依赖 Git，保持可用。`shell()` 新增 `gitUnavailable` 参数并从 `live.gitUnavailableReason` 取值；视觉稿场景 `git-unavailable` 传同一条原因 |
| `web/src/live-data.js` | `bindToolRail()` 对 `aria-disabled="true"` 的入口 `preventDefault` 后直接返回：不切换工具窗口、不重复弹错（局部错误与「配置 git.exe」入口已在检测时给过一次） |
| `web/src/mockup.css` | **未改** —— `.rail-button[aria-disabled="true"]` 的 faint 前景与悬停抑制此前已存在 |

**与权威的关系**：PyCharm 没有"Git 不可用降级页"，它在缺 Git 时只是隐藏/禁用 VCS 入口（`ui-compliance.md` §1.6 记为 AUGIT_ONLY）；Augit 用显式降级页表达同一状态，入口的禁用表达与权威同向。属"现有功能的呈现方式"，不新增能力。

**验证**：`live-shell` 两条断言（禁用标签集恰为「提交／Git 历史」、每个禁用入口带原因、其余三个可用；点击禁用入口前后 `live.layout.side` 与错误提示数不变）。

## 4. 验证（第 155 轮）

- **第 159 轮补记**：§2 表里最后一行"结果的呈现与排序"已由"未采"改为"已对齐（弹层表面扁平行＋按文件路径排序）" —— 第 158 轮曾据"两个表面都用到 `UsageViewPresentation`"判定"都按文件分组"，第 159 轮钉住**表面**后撤销（见 `10-backlog.md` §三·补三）。
- **本轮未改产品代码**：四个登记哈希与登记值一致（`mockup.js` `6fce1b9b…`、`mockup.css` `6da20a85…`、`live-data.js` `acf3ed7d…`、`bridge.js` `8d2d3173…`），故按快照规则**不需要重跑套件**；只改了文档，`check-doc-claims` 仍为 `DOC_CLAIMS_OK`。
- 本册的采集让 §三·补 第 1 项（仓库初始化）从"缺桥接"变成"缺桥接 ＋ 已知界面契约"，接线时不必再回查权威。
