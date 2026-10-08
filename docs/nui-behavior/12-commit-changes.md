# 12 · Commit / Changes 的交互与启用条件

本册是 `11-surface-audit.md` 第 1 区的采集结果，按区域分批补齐。**采集口径**：每条都带 checkout 里的文件:行号；实现在 Next 轮按此改。

## 1. 提交按钮的启用判据（权威）

```java
// platform/vcs-impl/src/com/intellij/openapi/vcs/changes/ui/CommitChangeListDialog.java:602-604
public boolean hasDiffs() {
  return !getIncludedChanges().isEmpty() || !getIncludedUnversionedFiles().isEmpty();
}

// 同文件 :616-624
private void updateButtons() {
  if (myDisposed || myUpdateDisabled) return;
  boolean enabled = hasDiffs() && !myWorkflow.isExecuting();
  if (myCommitAction != null) myCommitAction.setEnabled(enabled);
  myExecutorActions.forEach(action -> action.updateEnabled(enabled));
  okButtonUpdateAlarm.cancelAllRequests();
  okButtonUpdateAlarm.addRequest(myUpdateButtonsRunnable, 300, ModalityState.stateForComponent(getBrowser()));
}
```

**判据 = 「有已勾选的改动（含未版本化文件）**且**不在提交中」**。三条要点：

1. **提交信息是否为空，不参与这个判据**（见 §2）。
2. `hasDiffs()` 数的是**已勾选**的项（`getIncluded*`），不是"工作区有改动"；未勾选任何文件 ⇒ 按钮禁用。
3. 未版本化文件单独计入（`getIncludedUnversionedFiles()`）。

## 2. 空提交信息：**确认**，不是阻断（权威）

```kotlin
// platform/vcs-impl/src/com/intellij/vcs/commit/SingleChangeListCommitWorkflowHandler.kt:117-122
override fun checkCommit(sessionInfo: CommitSessionInfo): Boolean =
  super.checkCommit(sessionInfo) &&
  (
    getCommitMessage().isNotEmpty() ||
    ui.confirmCommitWithEmptyMessage()
  )

// 接口声明：platform/vcs-impl/src/com/intellij/vcs/commit/SingleChangeListCommitWorkflowUi.kt:14
fun confirmCommitWithEmptyMessage(): Boolean
```

即：**信息为空时先弹确认（`confirmCommitWithEmptyMessage()`），用户确认就**继续提交**；取消才回到编辑。** 空信息**不是**阻断条件，也不改变按钮的启用态。

## 3. 重新计算启用态的时机（权威）

| 触发 | 出处 |
| --- | --- |
| **勾选/取消勾选**（inclusion 变化） | `addInclusionListener(() -> updateButtons(), this)` — `CommitChangeListDialog.java:351` |
| 提交开始 | `executionStarted() { updateButtons(); }` — `:292` |
| 提交结束 | `executionEnded() { updateButtons(); }` — `:295` |
| **300 ms 去抖/轮询** | `okButtonUpdateAlarm.addRequest(myUpdateButtonsRunnable, 300, …)` — `:622-623`；`myUpdateButtonsRunnable = { updateButtons(); updateLegend(); }`（`:161-164`）⇒ 实质是**周期性重算**，因为包含状态可能被后台刷新改变 |

## 4. 附带执行器（"提交并推送"一类）的启用

```java
// CommitChangeListDialog.java:115 与 :799-803
isExecutorEnabled(executor) = super.isExecutorEnabled(executor) && (!executor.areChangesRequired() || !isCommitEmpty())
public void updateEnabled(boolean hasDiffs) {
  if (myCommitExecutor != null) setEnabled(hasDiffs || !myCommitExecutor.areChangesRequired());
}
```

即**要求改动**的执行器在没有已勾选改动时禁用；**不要求改动**的执行器（如某些自定义 executor）始终可用。

## 5. 与 Augit 现状的差异

| 项 | Augit 现状（文件:行） | 权威 | 处置 |
| --- | --- | --- | --- |
| **空提交信息** | **硬拦且永不继续**：`mockup.js:4315-4322` 在点击时 `preventDefault()` + 报"提交信息不能为空。"；实时路径同样 `live-data.js:4464-4471` 设 `__augitCommitError` 后 `return` | §2：**弹确认，确认后继续提交** | **改**：换成确认流程（复用现有确认对话框机制），确认后照常提交 |
| **未勾选任何文件** | 点击后报错"请至少选择一个要提交的文件。"（`live-data.js:4455-4462`）；静态稿里按钮写死 `disabled`（`mockup.js:1697`、`:1714`） | §1：按钮**直接禁用**（`hasDiffs() === false`） | **核**：确认实时侧是否也按勾选数置 `disabled`（点击后才报错与"按钮禁用"是两种交互，权威是后者） |
| **重算时机** | 未见 300 ms 去抖/轮询；勾选变化后即时重算（需核实） | §3 | **核**：至少保证"勾选变化立即重算"；300ms 轮询是权威的实现细节，Augit 无后台刷新可不必照搬（**登记为差异**，见 §7） |

## 6. 未采集（该区剩余）

| 缺口 | 说明 |
| --- | --- |
| **Amend 的交互** | 勾选/取消 Amend 时提交信息如何被载入与恢复；`live-data.js:8286` 已有入口，但权威侧未采 |
| **提交信息校验（inspection）** | `vcs-impl/.../commit/message/*.kt`（`BodyLimitInspection`、`SubjectBodySeparationInspection` 等）的呈现与时机；它们只产生**警告**，不阻断 |
| **改动列表右键菜单项的启用/隐藏规则** | 对应场景 `changes-context-menu` |
| **提交设置（齿轮）入口** | 对应 `settings.html` 的 Commit 相关项 |
| **"提交并推送"的后续切换** | 提交成功后是否自动打开推送对话框（`commitSelectedChanges(true)` 的语义） |

## 7. 落地记录（第 135 轮：空信息改为确认）

**已按权威改掉"硬拦"**，实现为两处共用同一个确认助手：

| 位置 | 改动 |
| --- | --- |
| `web/src/mockup.js` | 新增 `confirmCommitWithEmptyMessage(onConfirm, onCancel)`（`dialog()` 生成覆盖层，`data-commit-empty-message` 标记）；共享绑定 `bindInteractions()` 不再 `preventDefault()` 后报"提交信息不能为空。"，改为**弹确认**，确认后置 `__augitCommitAnyway` 并重新触发一次点击放行；取消则 `input.focus()` |
| `web/src/live-data.js` | `commitSelectedChanges(andPush, event, confirmed)` —— 空信息分支不再"设错误后 return"，而是 `event.preventDefault(); event.stopPropagation()`（避免共享绑定再弹一次）后弹确认，确认回调走 `commitSelectedChanges(andPush, null, true)` 继续提交；取消把焦点退回信息栏 |

**文案**（按 Augit 中文界面本地化，语义对权威逐条）：

| Augit | 权威键 |
| --- | --- |
| 标题「无提交信息」 | `VcsBundle.properties:37` `error.title.check.in.with.empty.comment` = **No Commit Message** |
| 正文「请在提交信息栏填写改动摘要。」 | `:36` `error.text.check.in.with.empty.comment` = **Add a summary of your changes in the commit message field** |
| 主按钮「仍然提交」 | `:35` `action.commit.anyway.text` = **{0} Anyway** |
| 次按钮「取消」 | `CommitCheck.kt:104` 的 `[Yes/No]` 语义 |

**自动化测试**：`tools/audit/check-commit-empty-message.test.cjs`（16 条）——覆盖①接线（两处都不再硬拦、都调用共享助手）②视觉稿行为（点提交→出现确认层、标题/正文/两个按钮齐备、**不再出现旧报错**）③取消→关闭且焦点回到信息栏④仍然提交→链接照常放行（进入 `operation-result.html`）。

## 8. 历史待办与最终归类（已全部收口）

1. ~~空信息改为确认~~ ✓ 第 135 轮已落地（见 §7）；
2. ~~"无勾选即按钮禁用"~~ ✓ **第 136 轮已落地**（见 §7.2）：实时侧此前**没有任何地方**按勾选数同步按钮可用性 —— `reflectWriteOperation()` 只处理"进行中"，把空闲禁用态寄存在 `dataset.idleDisabled` 里**等别处设置，而别处没人设置**。现按权威判据重算。
3. **登记为差异（不照搬）**：300 ms 去抖/轮询是 Swing 侧为吸收后台刷新而设，Augit 的改动列表由宿主事件驱动，无对应需求 ⇒ 写进 `design-system` 的差异表，不实现；
4. ~~**本区剩余采集**~~ **已收口（历史）**：Amend 交互、提交信息校验的呈现、右键菜单项启用/隐藏规则、提交设置入口与「提交并推送」的后续切换均已在后续轮次落地并断言（当前归类见 `ui-classification.md` §2.7／§2.8／§2.38，本册 §7／§10）；本项不再是当前待办。

### 7.2 提交动作的可用性按权威 `hasDiffs()`（第 136 轮）

`reflectWriteOperation()` 原先只算 `busy`，禁用态靠"记住渲染时的值"（`dataset.idleDisabled`）—— 而实时侧没有任何地方提供这个值，于是**无勾选时提交按钮仍可用、点了才报错**，与权威相反。

现按 §1 的判据重算：

```js
const busy = !!live.writeOperation;
const hasIncluded = ((live.status && live.status.files) || []).some((file) => file.checked);
const shouldDisable = busy || !hasIncluded;      // = !(hasDiffs() && !isExecuting())
// <a> 用 aria-disabled + .disabled 类（disabled 属性对链接无效，isControlDisabled() 认前者）
// 并给出禁用理由 title（规格 §10.3）：无勾选时"先在改动列表里勾选要提交的文件。"
```

**为什么挂在 `reflectWriteOperation()` 里**：它在 `rebindAfterRender()`（`live-data.js:3400,3426`）中被调用，而勾选变化走 `toggleChangeChecked()` → `refreshAfterEvent("side")` → 重渲染 + 重新绑定 ⇒ **勾选一改就会重算**，不需要额外监听。

**连带改掉 spec 里两处编码旧交互的断言**（同一裁决）：

| 位置 | 旧 → 新 |
| --- | --- |
| `live-shell.spec.cjs`「未勾选时」 | 断言"点击后报错且错误具备三要素" → 断言**动作用 `aria-disabled="true"` + `.disabled` 禁用**，且**强点（`force: true`）也不产生错误** |
| 同场景「§10.2 错误归属到发生区域」 | 原来跟在"未勾选点击"之后（现在没有错误了）→ 移到上面「确认后由宿主拒绝」那条**真实错误路径**上 |

**踩到的坑**：Playwright **默认拒绝点击"未启用"的元素**（`aria-disabled="true"` 也算），所以"验证禁用态下强点无副作用"必须用 `click({ force: true })`，否则它会一直重试到 30 s 超时 —— 我第一次就是这么挂的，日志里反而**证明了改动生效**（按钮渲染为 `<a aria-disabled="true" … class="primary-button disabled">`）。

**修订记录**

- 第 135 轮建立（`11-surface-audit.md` 第 1 区）：启用判据、空信息确认、重算时机、executor 判据已采；差异与落地计划如上。
- 第 138 轮补 Amend 的交互采集与落地（见 §9）。
- 第 138 轮补 Changes 右键菜单的项顺序（见 §10）。

## 10. Changes 右键菜单的结构与顺序（第 138 轮采集 + 落地）

### 10.1 权威结构

`ChangesViewPopupMenu`（`platform/vcs-impl/resources/META-INF/VcsActions.xml:186-219`）的注册顺序：

```
1  CheckinFiles                      ← 提交（组）
2  ChangesView.Revert                ← 回滚（RollbackAction，变更列表级）
3  ChangesView.RevertFiles           ← 回滚（RollbackFilesAction，文件级）
4  ChangesView.Move
5  Diff.ShowDiff                     ← 显示 Diff
6  Diff.ShowStandaloneDiff
7  ChangesView.EditSource            ← 跳转到源
8  CopyReferencePopupGroup           ← 复制引用/路径（组）
--- 分隔
9  $Delete · ChangesView.AddUnversioned · RemoveDeleted · Edit
--- 分隔
10 NewChangeList · RemoveChangeList · SetDefault · Rename · CreatePatch · Shelve
--- 分隔
11 ChangesView.Refresh
--- 分隔
12 VersionControlsGroup              ← 各 VCS 的文件级动作（动态收编）
```

第 12 项 `VersionControlsGroup` 里是 `VcsFileGroupPopup`（`VcsActionGroup`/`VcsGroupsWrapper`，`VcsActions.xml:56-58`），git 侧注册的是 `Git.FileActions`（`plugins/git4idea/backend/resources/intellij.vcs.git.backend.xml:162-174`）：

```
CheckinFiles → Git.Add → --- → Annotate（Blame）→ Compare.SameVersion → Compare.Selected →
Git.CompareWithBranch → Vcs.ShowTabbedFileHistory（文件历史）→ Vcs.ShowHistoryForBlock
```

另有 `Git.ChangesView.Conflicts`（Accept Yours/Theirs）以 `anchor="first"` 插到最前（`:487-488`）。

### 10.2 与 Augit 的差异（本轮改正）

Augit 的菜单是 6 项简化版，**相对顺序有两处与权威相反**：

| 项 | 权威 | Augit 原状 | 处置 |
| --- | --- | --- | --- |
| 回滚… ↔ 显示 Diff | `ChangesView.Revert`(2) **在** `Diff.ShowDiff`(5) **之前** | 显示 Diff 在前 | **已改为回滚在前** |
| Blame ↔ 文件历史 | `Annotate` **在** `Vcs.ShowTabbedFileHistory` **之前**（同属 `Git.FileActions`） | 文件历史在前 | **已改为 Blame 在前** |

分组（三组、组间一个分隔符、组内不插）与权威的"这几项连续、之后才分隔"一致 ✓，未改。

**改一处即两侧生效**：实时侧并不另写菜单，而是复用 `mockup.js` 的 `changesContextMenu()`（`live-data.js` 的 `openChangesContextMenu()` → `showPointerContextMenu(changesContextMenu(), …)`）——第 138 轮实测确认。

### 10.3 未定位 / 未做

| 项 | 说明 |
| --- | --- |
| `在资源管理器中定位` | `ChangesViewPopupMenu` 里**未找到**对应动作（该组里最接近的是 `ChangesView.EditSource` = 跳转到源，语义不同）⇒ **保留现状不删**（不移除既有入口），最终归类**有意产品差异**（`ui-classification.md` §2.8） |
| `复制路径` | 权威是 **`CopyReferencePopupGroup` 组**（多项/子菜单）；Augit 是单项简化。未改（属"Augit 的简化"，不是顺序问题） |
| 其余项 | CheckinFiles / Move / Delete / Add / RemoveDeleted / Edit / 变更列表管理 / CreatePatch / Shelve / Refresh —— **Augit 没有这些入口**，按边界**不新增** |

### 10.4 测试

新增 `tools/audit/check-changes-context-menu.test.cjs`（14 条）—— 覆盖①渲染顺序逐项核对②三组分组③实时侧复用同一模板④权威 id 在注释里留痕。**该场景此前没有任何检查器覆盖**（`changes-context-menu.html` 不在 35 个检查器的场景列表里）。

**连带改掉 spec 里三处硬编码旧顺序的断言**（`live-shell.spec.cjs` 的"文件历史/Blame/回滚确认"场景按**索引**点菜单项）：

| 位置 | 旧索引 | 新索引 | 对应项 |
| --- | --- | --- | --- |
| 文件历史场景 | `[2]` | `[3]` | 文件历史 |
| Blame 场景 | `[3]` | `[2]` | Blame |
| 回滚确认场景 | `[1]` | `[0]` | 回滚… |

三处注释里的"菜单项顺序固定：…"也换成了带权威出处的顺序说明。这是一次**典型的"断言编码旧交互"**：重排 UI 后 spec 立刻报 `文件历史读取目标路径: [0,null]`，正是它按旧索引点到了别的项。

## 9. Amend 的交互（第 138 轮采集 + 落地）

### 9.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 勾选框文案 | **`Amend commit`** | `VcsBundle.properties:1161` `commit.amend.commit` |
| 勾选框提示 | **`Merge this commit with the previous one`** | `:1162` `commit.tooltip.merge.this.commit.with.the.previous.one` |
| 勾选框助记符 | **`Alt+M`**（`mnemonic = KeyEvent.VK_M`） | `ToggleAmendCommitOption.kt:17` |
| 勾选 → 载入 | `setAmendMessage()`：读上次提交信息，仅在**用户没改过**信息时才覆盖（`initialMessage == null \|\| beforeAmendMessage == initialMessage`）；新旧信息"忽略空白不等"时才动 | `AmendCommitHandlerImpl.kt:52,77-92` |
| 载入后焦点 | `setCommitMessageAndFocus()` —— 设完信息**把焦点移到提交信息栏** | `:117-119` |
| 取消勾选 → 恢复 | `restoreBeforeAmendMessage()`：仅在信息**仍等于**载入的 amend 信息时，恢复"进入 amend 前"的信息 | `:52,110-115` |
| **提交动作改名** | 切换后调 `workflowHandler.updateDefaultCommitActionName()`：主按钮 = `amend.action.name` = **`Amend {0}`**、次按钮 = `action.amend.commit.and.push.text` = **`Amend Commit and Push…`** | `:52`、`VcsBundle.properties:38`、`DvcsBundle.properties:128` |
| 载入失败 | 后台任务标题 `amend.commit.load.message.task.title`；失败弹错误对话框 | `:123-132` |

### 9.2 Augit 现状与落地

Augit **已经**实现了"勾选→调宿主 `git/last-commit-message` 回填；取消→恢复进入 amend 前的草稿"（`live-data.js` 的 `amendDrafts` + `restoreAmendDraft()`）✓。本轮补上缺的三项：

| 项 | 落地 |
| --- | --- |
| 勾选框提示 | `mockup.js` 的 4 处 `.commit-amend` 渲染加 `title="把这次提交合并到上一次提交"`（本地化权威 `:1162`） |
| **提交动作改名** | 新增 `applyAmendActionLabel(box, checked)`：勾选 → 「修改提交」／「修改提交并推送…」；取消 → 「提交」／「提交并推送…」。**是改名不是换动作** —— 类名与点击路径不变（`live-data.js` 的 `.commit-actions` 分支仍按 `primary-button` / 含"提交并推送"分流；"修改提交并推送…"仍含该子串） |
| 载入后聚焦 | 回填成功后 `field.focus({ preventScroll: true })`（权威 `:117-119`） |

**入字点选得对**：`applyAmendActionLabel()` 同时被 `restoreAmendDraft()`（每次重渲染后跑）与 amend 点击处理调用 —— 否则区域刷新会把文案恢复成渲染默认值，而 Amend 还勾着。

### 9.3 剩余项与最终归类（不是待办）

1. **助记符 `Alt+M`** ⇒ **有意产品差异**：Augit 的界面没有助记符体系（中文界面 + 非 Swing 菜单），实现它等于**新增一套键盘交互**，触及"不新增功能"的边界 ⇒ 只登记、不实现（`ui-classification.md` §2.38）。
2. ~~**"仅在用户没改过信息时才覆盖/恢复"**~~ **第 216 轮已落地**（下条同）：Augit 的草稿机制在用户编辑时会删除草稿（`input` 监听），因此"取消后恢复"**效果等价**于"改过就不恢复"；差别在"**勾选时是否覆盖用户已输入的内容**"——权威 `AmendCommitHandlerImpl.kt:82` 的条件依赖 `initialMessage`（**面板激活时**的信息，`SingleChangeListCommitWorkflowHandler.kt:75` 在 `activate()` 里 `initialMessage = getCommitMessage()`）。落地：新增 `amendInitialMessages` 基线（每个提交框第一次渲染时记录字段值，提交成功后清空），勾选时只有"当前值 == 基线"才调用 `git/last-commit-message` 并载入；载入与恢复同时写 `live.commitDraft`（字段值跨重渲染由它保持），恢复条件照权威 `:112` 改成"字段仍等于载入的 amend 信息"；忽略空白相等则不动字段（`:99`）。
3. **载入失败的呈现** ⇒ **有意产品差异（实现方式）**：Augit 把失败写进既有 `window.__augitError` 通道，不新增权威的错误对话框（产品规格未要求该对话框）⇒ 登记、不改。

### 9.4 验证

spec 的 Amend 场景（`live-shell.spec.cjs`）扩了断言：勾选后主/次按钮文案 = 「修改提交」／「修改提交并推送…」、信息栏**获得焦点**，取消后恢复「提交」／「提交并推送…」。因工作区 spec 仍被并行会话的 `Too many arguments` 拖着跑不到底，本轮继续用"HEAD + 本轮三段改动"的临时副本验证（断言数见 `09-icons.md` 第 138 轮记录）。

## 11. 提交选项行的「上一次提交」入口（第 247 轮）

`ux-spec` §7.6 第 10 条要求「上一次提交在空间不足时只显示原有历史图标并保留完整悬停说明，数量使用剩余宽度省略」。
本轮核对时发现实时外壳与设计基线不一致：

| | 标记 | 文本 | `title` | 图标 |
| --- | --- | --- | --- | --- |
| 视觉稿静态场景（`changesSide()`，另起静态服务实测 `commit-changes.html`） | `<a class="commit-last file-status-modified" href="git-history.html">` | `上一次提交` | `上一次提交` | 1 个历史图标 |
| 实时外壳（修前） | `<span class="commit-last">` | 分支名（`live.branch`） | 无 | 0 |

`measureCommitPanels()` 在装不下文字时会给该行加 `icon-only`（`.commit-last.icon-only > span { display: none }`），
修前那一档就只剩一个空槽（实测字号 20 时 85×29、26 时 30×37 的空白块）。

**落地（第 247 轮）**：

- `web/src/mockup.js` 的 `liveChangesSide()` 按视觉稿渲染同一份标记（`上一次提交` ＋ `icon("history")` ＋ `title`）；
- `web/src/live-data.js` 删掉 `patchChangesList()` 里"把 `.commit-last span` 写成 `live.branch`"的定点更新
  （分支名由状态栏承担）；
- 点该入口打开 Git 历史工具窗口（视觉稿的 `href` 就是 `git-history.html`）：复用「打开日志视图」的路径，
  日志已可见时不重绘（避免把用户当前选中行重置成首行），因此重复点击不会折叠底部区域；
- 实测：字号 13 显示文字＋图标；20／26／40 进入 `icon-only`，文字让位而**图标仍在**（16×16）、`title` 仍完整、
  与数量不重叠；字号 40 时数量按剩余宽度省略。
