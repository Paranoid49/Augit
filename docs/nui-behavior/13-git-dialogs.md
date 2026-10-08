# 13 · Git 写操作对话框的交互（第 2 区）

本册是 `11-surface-audit.md` 第 2 区的采集结果，按对话框分批补齐（Push → Stash → Clone → …）。
口径同 `12-commit-changes.md`：每条都带 checkout 里的文件:行号。

## 1. Push（`VcsPushDialog`）

### 1.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 对话框标题 | 单仓库 = `push.dialog.push.commits.to.title` = **"Push Commits to {0}"**，`{0}` 是**仓库短名**（`DvcsUtil.getShortRepositoryName(...)`）；多仓库 = `push.dialog.push.commits.title` = **"Push Commits"** | `VcsPushDialog.java:134-137`；`DvcsBundle.properties:54-55` |
| 主按钮文案 | `setOKButtonText(DvcsBundle.message("action.push"))` = **"&Push"** | `VcsPushDialog.java:133`；`DvcsBundle.properties:46` |
| 主按钮是**拆分动作** | OK action = `ComplexPushAction`（`OptionAction`）：`myDefaultAction = actions.getFirst()`、`myOptions = actions.subList(1, …)`，渲染为 `JBOptionButton`；动作组 `Vcs.Push.Actions` = **`Vcs.Push.Simple` + `Vcs.Push.Force`** | `:283-285`、`:422-433`、`platform/dvcs-impl/resources/intellij.platform.vcs.dvcs.impl.xml:87-92` |
| 启用判据 | `enableOkActions(boolean)` → `myMainAction.setEnabled(value)`，**唯一的调用点**监听 `PushLogTreeUtil.EDIT_MODE_PROP`：**用户开始内联编辑时禁用**，编辑结束由对话框校验自动恢复 | `:380-394`；`PushController.java:125-130` |
| 中心面板尺寸 | **800 × 450**（`CENTER_PANEL_WIDTH/HEIGHT`，`myListPanel.setPreferredSize(...)`） | `:83-84,198` |
| 内容结构 | 上：`createTopPanel()`；中：`PushLog`（提交列表 + 可编辑的目标分支/远端列）；下：`createOptionsPanel()`；南侧另有 `createSouthAdditionalPanel()` | `:190-200` |
| 南侧内距 | `super.createSouthPanel()` 后套 `JBUI.Borders.empty(8, 12)` | `:186-189` |

### 1.2 与 Augit 的差异与落地

| 项 | Augit 原状 | 权威 | 处置 |
| --- | --- | --- | --- |
| **标题** | 硬编码 **"推送提交到 Augit"** —— 把**产品名**写进了"推送到哪里"，而推送目标是**远端**，语义错误 | §1.1：`Push Commits to <仓库短名>` | **已改**：新增 `pushDialogTitle()`，取**工作区名**（与标题栏 chip 同一来源 `workspaceName`，无 live 时回退 `Augit`）⇒ 真机显示「推送提交到 <真实仓库名>」，mockup 保持原样 |
| 主按钮文案 | 「推送」 | `&Push` | 语义一致 ✓ 不改 |
| 拆分动作（Force Push） | 无 | `Vcs.Push.Simple` + `Vcs.Push.Force` | **不新增**（无 force push 能力 ⇒ 按边界不做），登记 |
| 无远端时禁用 | 「推送」禁用 + 「定义远端」入口 | 权威的禁用只由内联编辑触发；"没有目标"由列表/校验体现 | Augit 的现有做法**更保守且不冲突** ⇒ 保留 |
| 中心面板 800×450 | 用 JS 公式按字号算（`--push-*`），宽度走 `.dialog.wide` | 800×450 是**列表的首选尺寸** | **待比**：需要在同一字号下量 Augit push 列表的实际尺寸再定，见 §1.3 |

### 1.3 剩余项与最终归类（均已归类，不是待办）

1. **中心面板尺寸 800×450** ⇒ **有意产品差异**：权威给的是列表的 `JBDimension` 首选尺寸（100% 缩放、默认字号）；Augit 的 push 列表由 `measurePushDialog()` 按字号算，不硬编码像素（`design-system.md` §9 的对话框字号适配口径），故不改公式、也不再"待量"。
2. **内联编辑时禁用主按钮**：Augit 的 push 列表**没有内联编辑**（目标分支/远端是只读展示）⇒ 无对应交互，登记。
3. **失败/取消的呈现**（push 进行中可以取消）已在 `design-system` 与既有断言里覆盖，本册不重复。

### 1.4 测试

`live-shell.spec.cjs` 的 Push 场景新增断言：对话框标题 = 「推送提交到 <`window.__augitLive.workspaceName`>」，且断言里**同时要求桩环境的工作区名 ≠ "Augit"** —— 否则旧实现（硬编码产品名）也能通过，属**空洞通过**。

## 2. Stash 创建（`GitStashDialog`）

### 2.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 标题 / 主按钮 | `stash.title` = **"Stash"** / `stash.button` = **"Create Stash"** | `GitBundle.properties:437,446` |
| 字段顺序 | **Git 根**（下拉）→ **当前分支**（标签）→ **提交信息**（`stash.message` = "&Message:"）→ **两个复选同一行**（`stash.keep.index` + `stash.include.untracked`） | `GitStashDialog.kt:41-53` |
| 每个字段都有 tooltip | `common.git.root.tooltip`（"Select Git VCS root"）、`common.current.branch.tooltip`（"The currently checked-out branch."）、`stash.message.tooltip`（"Enter stash message here"）、`stash.keep.index.tooltip`（"…index are left intact"）、`stash.include.untracked.tooltip`（"…untracked files are also stashed"） | `GitBundle.properties:52-55,439,441,443` |
| 信息编辑框首选尺寸 | **400 × 60** | `GitStashDialog.kt:25` |
| 初始焦点 | **提交信息编辑框**（`getPreferredFocusedComponent()`） | `:70` |
| 关闭时存草稿 | `dispose()` 里：信息非空 ⇒ `VcsConfiguration.saveCommitMessage(message)`（下次预填） | `:64-68` |
| 复选默认值 | 都不勾（`JBCheckBox` 默认未选中）；**没有持久化**上次选择 | `:29-31`、`GitStashUtils.kt:430`（API 默认 `false`） |

### 2.2 本轮落地：字段 tooltip

Augit 的四个字段**一个提示都没有**。按权威补上（本地化）：根目录「选择 Git 仓库根目录」、当前分支「当前已检出的分支」、消息「在此输入 Stash 消息」、保留索引「勾选后，已加入索引的改动会原样保留」。

实测（同步后）：四个 `title` 均在（`stash-root`／`stash-branch`／`stash-message`／`check-line`），且**对话框几何不变**（620×337，`#stash-message` 474×78）—— 提示属性不影响布局。

### 2.3 冲突：`Include untracked` 复选（**已升级为人工裁决**）

- **权威有**：`stash.include.untracked` 复选，默认不勾，不持久化。
- **宿主机有**：`ShellBridge.cs:1969-1970` 读 `keepIndex ?? false` 与 **`includeUntracked ?? true`**，交给 `StashWithOptionsAsync(...)`。
- **规范不要**：`ux-spec:524-525` 明确规定 stash 创建只有四个字段（Git 根目录／当前分支／消息／保留索引），并规定 Tab 循环为「根目录、消息、保留索引、取消、创建、关闭」。

⇒ 结果是 **Augit 现在总是把未跟踪文件也 stash，而界面无法改变这一点**。加上该复选会**改变"什么会被 stash"**，属产品行为而不是呈现；且与 `ux-spec` 的字段清单直接冲突。**我未自行选择**，三种选项与取舍已写入 `10-backlog.md` §三（人工裁决表第 1 项）。

**顺带发现的既有偏差**：`includeUntracked ?? true` 的宿主默认值与权威的"默认不含"相反 —— 无论选哪个选项都要一并决定。

### 2.4 剩余项与最终归类（均已归类，不是待办）

| 项 | 说明 |
| --- | --- |
| 初始焦点 | 权威聚焦**信息框**；`ux-spec:525` 的 Tab 循环从**根目录**起。两者不完全冲突（Tab 顺序 ≠ 初始焦点），Augit 不设显式初始焦点 ⇒ 归**有意产品差异**（按规范口径保留现状），不是"待确认" |
| 关闭时存草稿 | 权威在关闭时把非空信息存进 IDE 的 `saveCommitMessage`，下次预填；Augit 只在**会话内**保留（`ux-spec:525` 的"失败或取消保留消息"）⇒ 跨次持久化属新增行为，归**有意产品差异**（不新增） |
| 信息编辑框 400×60 | Augit 实测 474×78（默认字号）；权威只给**首选尺寸**、未给对话框尺寸与拉伸规则 ⇒ **不做结论**（与 Push 的 800×450 同类） |

## 3. Stash 应用 / 弹出（`GitUnstashAsDialog`）

### 3.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 标题 | `stash.unstash.changes.in.root.dialog.title` = **"Unstash Changes in {0}"**（`{0}` = 仓库根的可读名） | `GitUnstashAsDialog.kt:34`；`GitBundle.properties:484` |
| 字段 | 当前分支（**只读标签**，`stash.unstash.changes.current.branch.label` = "Current branch:"）→ **`As new &branch:`**（文本框，**初始聚焦**，`validateName` 校验；**可留空**，非空则"把 stash 检出为新分支"，见 `unstash.branch.tooltip`）→ `Pop stash`（复选，带 tooltip）→ `Reinstate index`（复选，带 tooltip） | `:40-77`；`:485,499-500,518-521` |
| **启用条件** | **分支文本框非空 ⇒ `Pop stash` 与 `Reinstate index` 两个复选都被禁用**（`popStashCheckbox.isEnabled = !hasBranch`） | `:88-92` |
| **按钮文案三态** | 分支非空 → `unstash.button.branch`（**"Branch"**）；否则 `Pop stash` 勾选 → `unstash.button.pop`（**"Pop Stash"**）；否则 → `unstash.button.apply`（**"Apply Stash"**） | `:94-103`；`:501-503` |
| 重算时机 | 分支文本变化 → `updateEnabled()` + `updateOkButtonText()`；`Pop stash` 变化 → `updateOkButtonText()` | `:55-59,79-85` |
| pop 的语义说明 | `unstash.pop.stash.tooltip` = **"If selected the stash is dropped after it is applied"** | `:518` |

### 3.2 与 Augit 的差异

Augit 的 stash 管理窗口用**三个并列动作**（应用／弹出／删除，`ux-spec:526` 明确要求"应用、弹出与删除清晰区分"），而权威的**同一组语义**分散在 `GitUnstashAsDialog`（apply/pop + 可选新分支）与其他入口里。两者**结构不同但语义一致**：

| 语义 | 权威 | Augit | 处置 |
| --- | --- | --- | --- |
| apply（保留 stash） | 按钮文案 `Apply Stash`（未勾 pop 时） | 「应用」（`data-stash-action="apply"` → 宿主 `keepStash: true`） | ✓ 一致 |
| pop（应用后丢弃） | 勾 `Pop stash` + 按钮文案变 `Pop Stash`，tooltip 说明"应用后丢弃" | 「弹出」（→ 宿主 `keepStash: false`） | **已补 tooltip**（见下） |
| drop | 另有删除入口 | 「删除」+ `stash-drop-confirm` 确认 | ✓ 一致 |

**本轮落地**：给两处 stash-manager 渲染点的「应用」「弹出」按钮补上权威 tooltip —— 应用「应用后保留该 Stash」、弹出「应用后丢弃该 Stash」（后者逐字对应 `unstash.pop.stash.tooltip`；前者的措辞是该 tooltip 的补集，并与宿主 `keepStash: true` 的语义一致）。两处**都要改**：静态样本（`managementPage('stash')`）与实时面板各有一份，只改一处会让视觉稿与真机不一致。

### 3.3 不支持 ⇒ 不新增（登记）

| 权威有的 | 宿主机状态 | 处置 |
| --- | --- | --- |
| **`As new branch:`**（把 stash 检出为新分支） | 宿主 `git/stash-write` 只有 `apply` / `pop` / 删除三种动作，无"应用为新分支" | **不新增**（新能力），登记 |
| **`Reinstate index`**（应用时同时恢复索引状态） | 宿主 `UnstashAsync(repository, reference, keepStash, ct)` 无该参数 | **不新增**（新能力），登记 |
| **`Clear`**（`unstash.clear` = 删除全部 stash，`GitBundle:504-505`） | 宿主只有按 reference 的单个删除 | **不新增**（新能力），登记 |

### 3.4 剩余项与最终归类（均已归类，不是待办）

- **按钮三态改名** ⇒ **有意产品差异**：权威是"一个按钮 + 随状态改名"（`Apply Stash`／`Pop Stash`／`Branch`）；Augit 是三个并列按钮，且 `ux-spec:526` 明确要求并列 ⇒ **不改**（结构差异，非顺序/启用问题）；
- **`Pop stash` 勾选后复选的联动禁用** ⇒ **不适用**：Augit 没有这两个复选，无对应交互。

## 4. Reset（`GitResetDialog`）

### 4.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 标题 / 按钮 | `reset.title` = **"Reset Head"** / `git.reset.button` = **"Reset"** | `GitBundle.properties:403,1217` |
| **模式与顺序** | 下拉按 **`MIXED` → `SOFT` → `HARD`** 依次 `addItem` ⇒ **默认落在 MIXED** | `GitResetDialog.java:157-159` |
| 模式文案 | `Soft`／`Mixed`／`Hard`（另有 `Keep` 模式，但**不在该对话框里**） | `GitBundle.properties:1203-1210` |
| **模式说明（每档一句）** | Soft:"Files won't change, differences will be staged for commit."；Mixed:"Files won't change, differences won't be staged."；Hard:"Files will be reverted to the state of the selected commit. **Warning: any local changes will be lost.**" | `GitBundle.properties:1204,1206,1208` |
| 目标引用校验 | `validateFields()`：引用非法 ⇒ `setErrorText(reset.commit.invalid)` + `setOKActionEnabled(false)` | `GitResetDialog.java:246-254`；文案 `:400` |
| 初始焦点 | 目标提交文本框（`getPreferredFocusedComponent()`） | `:240-242` |

> ⚠️ **权威此处疑似自身 bug**：`validateFields()` 在 `if (invalid) { setErrorText(...); setOKActionEnabled(false); }` **之后无条件**执行 `setErrorText(null); setOKActionEnabled(true);` —— 后半段会立刻覆盖前半段，等于"永远可用、永远无错误"。写法上缺 `else`。**按意图对齐**（非法 ⇒ 报错 + 禁用），**不照搬这处覆盖**。

### 4.2 抓到的偏差：**默认模式是破坏性的那一档**

| 项 | Augit 原状 | 权威 | 处置 |
| --- | --- | --- | --- |
| **默认模式** | **Hard**（`<option selected>`）—— 即打开对话框、直接点确认就会**丢弃本地改动** | **MIXED**（非破坏性："Files won't change, differences won't be staged."） | **已改**（见下） |
| 选项顺序 | Soft → Mixed → Hard | **Mixed → Soft → Hard** | **已改** |
| 目标提交 | 预填真实 `HEAD` 短哈希 ✓ | 文本框 + 校验 | ✓ 一致（校验的报错文案见 §4.1 的 bug 注记） |
| 动作按钮文案 | **随模式变化**（「执行 Reset」／「确认 Reset Hard」） | 固定 "Reset" | **不改**：`ux-spec:736` 明确要求"确认按钮使用动作名称，例如'确认 Reset Hard'" —— 这是 Augit 已登记的安全增强，且与权威在 `unstash.button.*` 上"按动作命名"的做法同源 |
| Hard 的危险样式 | 红色按钮 + `danger` 影响块 ✓ | 无（Swing 侧无此样式约定） | ✓ 保留（`ux-spec:529` 要求"Hard 使用红色确认"） |

**落地**：`#reset-mode` 的 option 改为权威顺序、默认选 **Mixed**，并给每个 option 加稳定 `value`（`mixed`／`soft`／`hard`）；同时**同步重排** `mockup.js` 里按 `selectedIndex` 索引的 `presentations` 数组 —— 否则说明文字会与选项错位（危险档仍是索引 2，危险样式逻辑不变）。

### 4.3 连带改正的断言

spec 的 Reset 场景原本断言"**打开即** Hard ＋ 红色确认 ＋ 确认 Reset Hard"（`rsOpen.modeValue === 2`），并把"影响说明用真实改动数（5 个已跟踪文件）"也挂在打开态 —— **这两条编码的都是旧默认**。已按权威改成：

| 断言 | 旧 | 新 |
| --- | --- | --- |
| 打开态 | Hard（索引 2）＋ danger ＋「确认 Reset Hard」 | **Mixed（索引 0）＋ 非 danger ＋「执行 Reset」**，且影响说明出现"重置索引" |
| Soft 切换 | 按 `{ index: 0 }` 选 | 按 **`value='soft'`** 选（顺序本身就是对齐对象，索引会随权威改动而变） |
| Hard 的危险样式与"5 个已跟踪文件" | 挂在打开态 | 移到**显式选中 Hard 之后**（`value='hard'`） |

### 4.4 剩余项与最终归类（均已归类，不是待办）

- **模式说明的呈现** ⇒ **有意产品差异**：权威是"选项文本只有 Soft/Mixed/Hard + **单独一句说明**"；Augit 把说明**折进选项文本**（`Soft · 仅移动 HEAD`）并在影响块里另给一句细节 ⇒ 属结构差异，且 `ux-spec:529` 要求"在同一页面解释 HEAD、索引和工作区的影响"（Augit 的影响块正是干这个）⇒ **不改**，登记；
- **`Keep` 模式** ⇒ **不适用**：权威有 `git.reset.mode.keep`，但**不在该对话框**（不 `addItem`）；
- **`validateFields()` 的覆盖 bug** ⇒ **不适用**：见 §4.1 —— 不照搬。

## 5. Rollback（`RollbackAction` / `RollbackChangesDialog`）

### 5.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 动作文案 | `getRollbackOperationName(project) + ELLIPSIS`；Git 侧 `GitRollbackEnvironment.getRollbackOperationName()` = `git.rollback` = **"&Rollback"** ⇒ 菜单项 = **"Rollback…"** | `RollbackAction.java:81`；`GitRollbackEnvironment.java:44-46`；`GitBundle.properties:1181` |
| **启用判据** | `hasReversibleFiles(e)`：① 有任意改动（`getAllChanges()` 非空）② 有**磁盘上已丢失**的文件（`MISSING_FILES_DATA_KEY`）③ 有**在 IDE 外被修改**的文件（`getModifiedWithoutEditing`）—— 三者之一即可用 | `:84-105` |
| **可见性** | 默认 `setEnabledAndVisible(false)`，再按上条置真；**另有一条隐藏分支**：`isPreferCheckboxesOverSelection() && 非模态提交模式 && CHANGES_VIEW_POPUP` | `:70-80` |
| 隐藏分支是否生效 | Registry **`vcs.prefer.checkboxes.over.selection` 默认 `false`** ⇒ **默认不生效** | `RollbackFilesAction.kt:65`；`registry.properties:806` |
| 确认对话框标题 | `changes.action.rollback.custom.title` = **"{0} Changes"** ⇒ Git 下 = **"Rollback Changes"** | `RollbackChangesDialog.kt:54`；`VcsBundle.properties:335` |
| 确认按钮 / 取消按钮 | OK = 动作名 **"Rollback"**；Cancel = `CommonBundle.getCloseButtonText()` = **"Close"**（**不是 Cancel**） | `:48-49` |
| 正文 | **带勾选的文件浏览器**（`LocalChangesBrowser`）+ 提交图例 + 一个复选 `changes.checkbox.delete.locally.added.files` = **"&Delete local copies of added files"** | `:58-78`；`VcsBundle.properties:350` |
| **两个联动启用规则** | ① **OK 可用 ⟺ 已勾选改动非空**（`okAction.isEnabled = includedChanges.isNotEmpty()`）② **「删除本地新增文件副本」复选可用 ⟺ 已勾选里有 `Change.Type.NEW`**（`hasNewFiles`） | `:82-92` |
| 复选值持久化 | 该复选的值写进 `PropertiesComponent`（下次打开沿用） | `:74-77` |

### 5.2 与 Augit 的差异（本轮**无产品改动**，逐条说明为什么）

| 项 | Augit | 权威 | 结论 |
| --- | --- | --- | --- |
| 菜单/入口文案 | 「回滚…」 | `&Rollback` + `…` | **一致 ✓** |
| 可见性 | 菜单里显示 | 默认可见（隐藏分支因 Registry 默认 `false` 不生效） | **无需改 ✓** —— 细读这条**避免了一次错改**：只看 `RollbackAction.update()` 的隐藏分支会以为该藏 |
| **确认对话框的形态** | **真实只读 Diff + 风险说明 + 回收站说明 + 底栏确认** | **多文件浏览器 + 勾选 + 图例** | **不改**：`ux-spec:531` 明确规定 Augit 用"先展示真实只读比较，再由底栏确认"；且宿主 `git/rollback` 只接受单个 `path` ⇒ 多文件回滚＝新能力，按边界不做 |
| **`Delete local copies of added files`** | 无此复选 | 有（含"仅在有新增文件时可用"＋值持久化） | **不新增**：宿主无该能力；Augit 对新增/未跟踪文件是**移到回收站**（`ux-spec:531` 要求保留回收站说明），语义比"删除本地副本"更保守 |
| `hasReversibleFiles` 的三类来源 | 需选中一行（`live.selectedChangePath`） | 有改动即可用（**不依赖选择**） | **不改**：权威"不依赖选择"是因为它的对话框本身让你挑文件；Augit 是逐文件确认 ⇒ 必须先选中目标。属同一能力边界的两种表达 |
| 「在 IDE 外被修改」这一类 | **不适用** | `rollbackModifiedWithoutCheckout` | Augit 是只读查看器，没有"文件在 IDE 内打开时被外部修改"这个状态 ⇒ 登记 |

### 5.3 测试

`verify-ux-rollback-layout` 覆盖字号/布局/失败保留；`live-shell` 覆盖"取消不写入、确认后只刷新相关区域"（`ux-spec:831`）。

## 6. Clone（`DvcsCloneDialogComponent` + Git 浅克隆）

### 6.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| **OK 启用判据** | `isOkActionEnabled() = getUrl().isNotBlank()` ⇒ **URL 非空才可用**；Git 侧再叠加"**版本检查成功**"（`super && versionCheckState == SUCCESS`） | `DvcsCloneDialogComponent.kt:127`；`GitCloneDialogComponent.kt:141` |
| 浅克隆复选 | `clone.dialog.shallow.clone` = **"Shallow clone with a history truncated to"**，默认 **不勾**（`AtomicBooleanProperty(false)`） | `GitShallowCloneViewModel.kt:20,32`；`GitBundle.properties:904` |
| 深度框 | 内容 **`1`**，范围 **`1..Int.MAX_VALUE`**，`.enabledIf(shallowCloneCheckbox.selected)` ⇒ **仅勾选时可用** | `GitShallowCloneViewModel.kt:21,36-38` |
| 深度框 tooltip | **字面量 `--depth`**（`GIT_CLONE_DEPTH_ARG`） | `:41,48` |
| 深度后标签 | `clone.dialog.shallow.clone.depth` = **"commits"** | `GitBundle.properties:905` |
| 行内顺序 | **复选 → 数字框 → 标签**（`RightGap.SMALL` 串起） | `:30-45` |
| 取值语义 | 勾选才产出 `GitShallowCloneOptions(depth.toIntOrNull() ?: 1)`，否则 `null` | `:23-25` |

### 6.2 与 Augit 的差异与落地

| 项 | Augit | 权威 | 处置 |
| --- | --- | --- | --- |
| 浅克隆文案/顺序/默认 | 「浅克隆，历史截断为」+ 深度框 +「个提交」；默认不勾、深度 `1` | 同上 | **一致 ✓**（中译逐字对应） |
| 深度框启用 | `depth.disabled = running \|\| !shallow.checked` | `.enabledIf(shallow.checked)` | **一致 ✓** |
| 深度校验 | 提交时正则 + `1..2147483647`（＝ `Int.MAX_VALUE`） | `intTextField(1..Int.MAX_VALUE, 1)` | **一致 ✓**（`ux-spec:541` 也要求"校验正整数"） |
| **深度框 tooltip** | **无** | **`--depth`** | **已补**（两处渲染点） |
| **OK 启用** | 按钮**始终可用**，点击后才提示"请输入仓库地址和目标目录。" | **URL 为空即禁用** | **已改**：`create.disabled = running \|\| !source.value.trim()`，并接上 URL 的 `input` 监听、禁用时给 `title` 说明；目录仍走点击时提示 |
| 目录初值 | **空**（`ux-spec:541` 规定），因此"目录为空"这一档必须靠点击时提示 | 随 URL **自动派生** | 记录：正因为这里不同，删掉"点击时提示"会让目录空缺无处提示 |
| OK 还需版本检查 | 无此流程（设置在设置页做 Git 检测） | `versionCheckState == SUCCESS` | **不新增**（新流程） |

### 6.3 连带改正的断言与规范

| 位置 | 旧 | 新 |
| --- | --- | --- |
| `verify-ux-clone.cjs` 空 URL 段 | 点击克隆 → 期望「请输入仓库地址和目标目录。」＋焦点回 URL | 断言**按钮禁用**且**无提示**；随后只填 URL → 断言按钮启用 → 点击 → 仍期望目录提示＋焦点到**目录**（保住规范要求的那条路径） |
| 同文件 Tab 环测试 | 直接从版本控制开始按 Tab 直到 `.primary-button` | **先填 URL** 再验环（禁用控件不进环，与本文件对"启用的深度"的既有处理同一条规则） |
| `ux-spec:541` | "缺少必填项或深度不合法时…提示原因并聚焦对应输入" | 补：**URL 为空时克隆按钮直接禁用**（附权威出处），其余必填项走点击时提示 |
| `ux-spec:543` | Tab 环含"取消、克隆、关闭" | 改为"取消、**启用的克隆**、关闭（禁用的控件不进入环）" |

## 7. Worktree（`GitWorkingTreeDialog` + `RemoveWorkingTreeAction`）

### 7.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 新建对话框 | 标题 `working.tree.dialog.title`；OK `working.tree.dialog.button.ok` | `GitWorkingTreeDialog.kt:106-107` |
| **字段与顺序** | ① **来源引用**（`createRefComboBox`，带动态 comment）② **「新分支」复选 + 新分支名**（`working.tree.dialog.checkbox.new.branch`）③ **名称**（`label.name`）④ **位置**（`label.location`，`textFieldWithBrowseButton` + `singleDir()` 选择器 + 动态 comment） | `:175-230` |
| 「新分支」 | `createNewBranch` 默认 **false**；分支名 on-input/on-apply 校验（非空才校验合法性） | `:96,185-198` |
| 名称 | **自动建议**（`suggestProjectName()`），且 ref／新分支变化时**重算**（`dependsOn(projectName, ...)`） | `:99,102-104` |
| **移除的启用判据** | `trees.all { !isCurrent && **!isMain** && !创建中 && !删除中 }` | `RemoveWorkingTreeAction.kt:31-38` |

### 7.2 与 Augit 的差异与落地

| 项 | Augit 原状 | 权威 | 处置 |
| --- | --- | --- | --- |
| **「新分支」复选 + 分支名** | **完全没有** | 有（默认不勾） | **已加**：宿主机**早已支持** `newBranch`（`ShellBridge.cs:507` → `GitWorktreeService.cs:103-122` 用 `check-ref-format --branch` 校验后加 `-b`），只是 UI 从未传过。**默认不勾 ⇒ 不改变任何现有行为**（不勾时不传该参数） |
| **字段顺序** | 目录 → 分支 | 引用 → 新分支 → 名称 → 位置 | **已改**为 **分支 → 新分支 → 目录**（Augit 没有独立"名称"字段，`destination` 即完整路径） |
| 名称自动建议 | 无 | 有 | 记录（Augit 的目录是完整路径，无"名称"这一栏） |
| 位置的**浏览按钮** | 纯文本框 | 带文件夹选择器 | **不新增**（文件选择器是 C# 侧能力，Augit 没有） |
| **移除缺"主工作树"这一档** | 宿主只查 `IsCurrent`／`IsLocked`／目录不存在／终端占用／是否干净 | 另有 **`!isMain`** | **已落地（第 212 轮，C# 侧 + 界面）**：`GitWorktreeInfo` 新增 `IsMain`，按 `GitWorktreeListParser` 的判据取 **`git worktree list` 的第一项**（`isFirst`）；`InspectRemovalReadinessAsync` 与 `RemoveAsync` 都在 `IsCurrent` 之后加 `IsMain` 分支，回"仓库的主工作树不能移除。"；`git/worktrees` 下发 `isMain`，界面把状态显示成"主工作树，不能移除"并禁用「移除…」+ 悬停原因。证据：`GitWorktreeServiceTests.主工作树不能从链接Worktree窗口移除`、`ShellBridgeWorktreeTests.主工作树在链接窗口里不可移除并标记IsMain`、`live-shell` 三条新断言 |

### 7.3 连带改正的断言

`live-shell` 里"表单提供目录与分支两个字段"（断言 `destination,branch`）编码的是旧表单，已改为断言 **`branch,newBranchEnabled,newBranch,destination``；并新增 5 条：字段顺序、默认不勾且分支名禁用、勾选后启用、空名点击不调用宿主且提示"请填写新分支名。"、填名后创建时宿主收到 `newBranch`（stub 已记录 `git/worktree-write` 全参，可验证"不勾时不传"）。

### 7.4 剩余项与最终归类（均已归类，不是待办）

1. ~~**主工作树判据**（§7.2 末行，C# 侧）~~ → **第 212 轮已落地**（见 §7.2 末行）；
2. **名称自动建议与位置浏览按钮** ⇒ **不适用**（见 §7.2 的两行：Augit 的目录是完整路径、没有"名称"栏；不新增文件选择器）；
3. 移除失败的呈现已在既有断言里覆盖（`worktree-remove` 的 `canRemove`/`reason` 经真机实测 ✓）。

## 8. Branches —— 规范要求"删除分支/标签"，而产品没有这个操作（**升级裁决**）

### 8.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 删除本地分支 | `GitDeleteBranchOperation`：**先收集未合并提交**（`delete.branch.operation.collecting.unmerged.commits.process` = "Collecting unmerged commits…"）→ 删除；失败时**回滚并恢复分支**（`…restoring.branch.process` = "Restoring Branch {0}…"、`…could.not.restore.branch.error`、`…branch.was.not.deleted.error`） | `plugins/git4idea/backend/src/branch/GitDeleteBranchOperation.java`；`GitBundle.properties:1365-1372` |
| 结果提示 | `delete.branch.operation.deleted.branch` = "Deleted branch {0}"；**`…unmerged.commits.were.discarded` = "Unmerged commits were discarded"**（加粗提示） | `GitBundle.properties:1370-1372` |
| 删除远端分支 | 另有独立实现 `GitDeleteRemoteBranchOperation` | 同目录 |

即：分支删除在权威里是**有未合并提交分析 + 失败恢复**的正式操作，不是一条命令。

### 8.2 Augit 现状：没有这个能力

| 事实 | 证据 |
| --- | --- |
| 宿主 `git/branch` **只支持 `create` 与 `rename`** | `ShellBridge.cs:2443-2455`（`"create"` / `"rename"` / 其它 ⇒ `未知的分支动作`） |
| UI 只发这两种动作 | `live-data.js:4752/4754`；全库 `data-branch-action` 只有 `create`／`rename` |
| 但**规范要求**它显示影响 | `ux-spec:735`："Reset Hard、Rollback、**删除分支或标签**、删除 Stash、移除 Worktree 必须显示具体影响" |
| 状态矩阵把它记成 ✅ | `baselines/state-coverage-matrix.md` 的"分支与标签 … 危险 ✅ 删除分支/标签" —— 该 ✅ **从未被真机核对**（矩阵 §5 第 7 项只核对了 Stash 与 Worktree） |

### 8.3 为什么本轮不动它（升级为专项裁决）

按用户裁决的边界——**不新增 Augit 没有的功能**——"删除分支/标签"是**一个完整的操作能力**（不是某个现有操作的界面呈现）：需要宿主侧新增 `git branch -d/-D`、未合并提交的分析、失败回滚恢复，以及本地/远端两种分支与标签的确认文案。**它不属于"现有功能的界面与交互对齐"**，所以不能自行实施；同时也不能假装 `ux-spec:735` 的那一条满足了。已写入 `10-backlog.md` §三（人工裁决表第 2 项），并把状态矩阵里那个未经核对的 ✅ 改成明确标注。

### 8.4 与"宿主有能力、界面没入口"的区别（判据补充）

到目前为止第 2 区出现的缺口有两类，**处理方式相反**：

| 类型 | 例子 | 处理 |
| --- | --- | --- |
| **宿主有能力、界面没入口** | Stash 的 `includeUntracked`（默认会翻转 ⇒ 需裁决）、Worktree 的 `newBranch`（默认不勾 ⇒ 可直接做） | 加界面＝对齐（附默认值判据） |
| **规范要求、产品根本没有该能力** | 删除分支/标签（本条） | **不新增**，升级裁决 |

**判据**：先看**宿主参数表**（能力是否已存在）→ 再看**规范条文**（是否要求一个不存在的能力）。前一类的"能力"已经存在，后一类没有。

## 9. 两处"能力已实现、桥接与界面缺失"（第 148 轮核实，**待做**）

第 146／147 轮沉淀的扫描法（**先看宿主参数表，再看规范条文**）本轮扩到宿主**全部方法表**，发现两处**基础设施已经做好、但 `ShellBridge` 没有方法、UI 也从不调用**的能力：

| 能力 | C# 侧（已实现） | 桥接 | UI | 场景 |
| --- | --- | --- | --- | --- |
| **仓库初始化** | `IGitServices.InitializeAsync`（`IGitServices.cs:18-20`）→ `GitRepositoryService.InitializeAsync`（`GitRepositoryService.cs:116`） | **无 `git/init`** | 从不调用 | `repository-init.html`（静态确认框） |
| **Smart Checkout**（宿主第 206 轮接线） | `IGitServices.SmartCheckoutAsync`（`IGitServices.cs:323-326`）→ `GitOperationService.SmartCheckoutAsync`（`GitOperationService.cs:187`），并含**临时 stash 标记** `SmartCheckoutMarker`（`:8`）、`GitOperationKind.SmartCheckout` 与中断后的续做 `CompleteSmartCheckoutAsync`（`:157-159,344`） | **第 206 轮已加 `git/checkout-smart`**（写操作通道；回包与 `git/operation` 共用会话投影，恢复冲突即 `SmartCheckout` 会话且保留临时 stash；+3 条单测） | **第 207 轮已接线** | `tools/verify-ux-smart-checkout.cjs`＋`live-shell` 四条断言 |

**结论**：这两处不是"设计没定"，而是**接口层没接** —— 与第 146 轮 Worktree 的 `newBranch` 同类（**宿主/基础设施有能力、界面没入口**），因此按判据**属"对齐"**、可以直接做。**但本轮没有动 C#**，原因是：

1. `src/Augit.Shell/` 下另有并行会话正在编辑 `Program.cs`／`ShellOptions.cs`／`ShellWindow.cs` 并新增 `ShellTheme.cs`／`ShellSystemTheme.cs`（`git status` 可见）。**`dotnet build`／`dotnet test` 会连带编译他们的在途改动**，一旦失败我无法区分"是我引入的"还是"他们没写完"；
2. 这两处各自要动 **C#（桥接方法 + 处理器 + 单测）→ JS（接线）→ `live-shell` 断言** 四层，适合作为**独立一轮**做完整验证，而不是塞进本轮尾巴。

**后续进展（两项均已结案）**：仓库初始化在第 204（宿主）／205（界面）轮完成；Smart Checkout 在第 206（宿主
`git/checkout-smart`）／207（界面：`overwriteRisk` → 权威形态对话框，取消绝不执行 Git，恢复冲突时说明临时 stash
已保留）轮完成，权威依据 `GitBrancher.java:91-92` 与 `GitSmartOperationDialog.java:36-125`。

### 9.1 Remote 的顺带核对（结论：字段是超集、校验较弱，暂不改）

| 项 | 权威 `GitDefineRemoteDialog` | Augit |
| --- | --- | --- |
| 字段 | **名称 + URL**（URL 另在 Configure Remotes 里分 fetch/push） | **名称 + 获取 URL + 推送 URL**（超集 ✓） |
| 校验 | `nameNotBlank() ?: nameWellFormed() ?: nameUnique()`（非空 → **合法** → **唯一**）+ URL 非空与可达性（`…validation.message` 四条文案）；**校验不通过就不调用 Git** | 依赖 `git/remote-write` 返回的 reason（宿主/Git 报错后再提示） |
| 结论 | —— | 字段不必改；**三重名校验**是否前置到界面（"不调用 Git 就提示"）留作候选，属交互增强而非缺陷，登记 |

## 10. 未覆盖（本册待续）

第 2 区其余：**Operation Progress·Result**、**Conflict List**（两者桥接已接线：`git/operation`／`git/operation-action`／`git/conflicts`／`git/conflict-*`），以及 §9 的两处桥接缺口。

## 修订记录

- 第 140 轮建立（`11-surface-audit.md` 第 2 区首片）：Push 的标题/文案/拆分动作/启用判据/几何已采；标题已按权威改正。
- 第 141 轮：Stash 创建已采（§2）——字段顺序/文案/tooltip/首选尺寸/初始焦点/草稿/默认值；补了 4 个字段 tooltip；`Include untracked` 升级为人工裁决。
- 第 142 轮：Stash 应用/弹出已采（§3）——启用条件、按钮三态、pop 语义 tooltip；补了「应用／弹出」两个 tooltip；`As new branch`／`Reinstate index`／`Clear` 三项登记为"不新增"。
- 第 143 轮：Reset 已采（§4）——**默认模式从 Hard 改为权威的 MIXED**、选项顺序改为 MIXED→SOFT→HARD、option 加稳定 `value`；连带改正 spec 里编码旧默认的两条断言；记录权威 `validateFields()` 自身的覆盖 bug（按意图对齐，不照搬）。
- 第 144 轮：Rollback 已采（§5）——**本轮无产品改动**（逐条说明为什么）：入口文案与权威一致 ✓、可见性因 Registry `vcs.prefer.checkboxes.over.selection` 默认 `false` 而**本就该显示** ✓（细读避免了一次错改）、确认对话框形态是 `ux-spec:531` 明确规定的差异、`Delete local copies of added files` 因宿主无能力而**不新增**。
- 第 145 轮：Clone 已采（§6）——浅克隆文案/顺序/默认/启用/校验**本就对齐** ✓；补**深度框 tooltip `--depth`**；按权威把**克隆按钮改为"URL 空即禁用"**并接上 URL 监听，连带改正检查器两处断言与 `ux-spec` 两句话。
- 第 146 轮：Worktree 已采（§7）——**新建表单缺「新分支」**（宿主早已支持 `newBranch`），已补上（默认不勾、行为不变）并把字段顺序改为权威的 分支→新分支→目录；另发现**移除判据缺"主工作树"这一档**（C# 侧待做）。
- 第 147 轮：Branches 已采（§8）——发现**规范要求"删除分支/标签"而产品没有该能力**（宿主 `git/branch` 只有 create/rename）⇒ 按边界**不新增**，升级为专项裁决并改正状态矩阵里未经核对的 ✅。
- 第 148 轮：扫描宿主**全部方法表**，发现**两处能力已实现但桥接与界面缺失**（仓库初始化、Smart Checkout）——均为"有能力、没入口"⇒ 属对齐、可直接做；本轮**未动 C#**（并行会话正在编辑 `src/Augit.Shell/`，构建会连带编译其在途改动），留作独立一轮。另核对 Remote（字段为超集、校验较弱，暂不改）。
