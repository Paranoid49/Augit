# 16 · 操作进度与结果（第 8 区）

本册是 `11-surface-audit.md` 第 8 区的采集结果，场景：`operation-progress`、`operation-result`。
口径同前：每条都带 checkout（`/mnt/d/github/intellij-community`，commit `576e328`）里的文件:行号。

第 154 轮落地了**取消的一次性语义**；其余各项经核对**本来就对齐**或属"该能力尚未接线"（见 §3）。

## 1. 权威

New UI 里"操作进行中"有两种呈现，Augit 的对应物是提交侧栏的进行态与全局提示，故两处都要看：

### 1.1 模态进度窗口（`ProgressWindow` → `ProgressDialog`／`ProgressDialogUI`）

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 窗口构成 | `ProgressWindow(shouldShowCancel, shouldShowBackground, project, parentComponent, cancelText)`，内部建 `ProgressDialog(this, shouldShowBackground, cancelText, parentWindow)` ⇒ **模态**；`shouldShowBackground` 决定是否提供"在后台运行" | `platform/platform-impl/src/com/intellij/openapi/progress/util/ProgressWindow.java:99-122` |
| 取消按钮**是否出现** | 取消能力由 `TaskCancellation` 表达：`NonCancellableTaskCancellation` ⇒ **`cancelButton.isVisible = false`**（不可取消就不给入口）；`CancellableTaskCancellation` ⇒ 显示 | `ProgressDialogUI.kt:131-137` |
| 取消按钮文案 | 默认 `CommonBundle.getCancelButtonText()`；任务可用 `TaskCancellation.buttonText`／`tooltipText` 覆盖 | `ProgressDialogUI.kt:69`、`:137-142` |
| **取消是一次性动作** | `ActionListener { cancelAction(); cancelButton.isEnabled = false }` —— **按过即禁用**；同一个监听器还注册到 **`Esc`** | `ProgressDialogUI.kt:136-152` |
| 进行中可动态启停取消按钮 | `cancelButtonEnabledRequests`（`MutableSharedFlow<Boolean>`，replay=1）→ `ui.cancelButton.isEnabled = isEnabled`；`cancel()` ⇒ `enableCancelButtonIfNeeded(false)` | `ProgressDialog.kt:62`、`:123-127`、`:163-169` |
| 对话框级取消 | `doCancelAction()` ⇒ `progressWindow.cancel()`（对话框的 Esc／取消路径） | `ProgressDialog.kt:284-286` |
| 状态栏进度组件 | `StatusBarProgress extends ProgressIndicatorBase`（文本＋工具提示的轻量进度指示器） | `platform/platform-impl/src/com/intellij/openapi/progress/util/StatusBarProgress.java:23-57` |

### 1.2 结果提示（后台进程气泡）

| 项 | 权威 | 出处 |
| --- | --- | --- |
| **不自动消失** | `createBalloonBuilder(content).setFadeoutTime(0)` ⇒ 淡出时间为 0（不自动隐藏）；同时 `.setHideOnAction(false)`、`.setDisposable(parentDisposable)` | `platform/platform-impl/src/com/intellij/openapi/wm/impl/status/ProcessBalloon.kt:114-127` |

## 2. 与 Augit 的对照

Augit 侧有两处对应物：**提交侧栏的进行态**（`reflectWriteOperation()`：禁用提交/推送、插一条"「提交」进行中…"、插一个 `取消` 按钮）与**全局提示**（`live.toast` → `liveToast()`）。

| 审计项 | 权威 | Augit 现状 | 处置 |
| --- | --- | --- | --- |
| **取消语义** | 按过即禁用（`ProgressDialogUI.kt:144-145`） | 原来取消按钮一直可点：宿主确认前再按一次会发出**第二次 `write/cancel`** | **第 154 轮落地**：`reflectWriteOperation()` 在 `live.writeCancelling` 时禁用取消按钮（`disabled` ＋ `aria-disabled` ＋ `.disabled` 三处同步），`cancelWriteOperation()` 再加一层入口守卫 —— 与既有的"重复触发被拦下"同一条规则 |
| 重复触发抑制 | 模态窗口期间无法再次触发（`ProgressWindow` 模态） | 已有：进行中禁用提交与推送，且提交入口有 `if (live.writeOperation) return` 守卫；`live-shell` 已断言"重复触发被拦下（未新增请求）" | **本来就对齐** ✓ |
| 进行中禁用 | 同上 | 已有：`reflectWriteOperation()` 按 `busy` 禁用提交/推送并给出状态文字 | **本来就对齐** ✓ |
| 取消期间不得提前解冻 | `cancelButtonEnabledRequests` 让指示器控制取消按钮；取消后仍等指示器结束 | 已有：取消中保持 `writeOperation`（按钮仍禁用），**不提前重读状态**，宿主确认后才清标记并重读真实状态；`live-shell` 已断言 | **本来就对齐** ✓ |
| 不可取消就没有取消入口 | `NonCancellableTaskCancellation` ⇒ 按钮不可见（`ProgressDialogUI.kt:133-134`） | Augit 只在可取消的写操作上挂进行态与取消入口（`live.writeOperation` 只在提交路径设置）⇒ 等价成立 | **本来就对齐** ✓ |
| 结果提示不自动消失 | `setFadeoutTime(0)`（`ProcessBalloon.kt:115`） | `live.toast` 常驻，只有显式 `clearToast()`（重试成功等流程）才清除，没有自动隐藏计时器 | **本来就对齐** ✓ |
| 提示的读屏语义 | 通知/气泡 | `liveToast()` 用 `role="alert"` | **本来就对齐** ✓ |

## 3. 进度条的结论（第 226 轮重新评估；用户 2026-09-28 已认可「有意产品差异」）

`10-backlog.md` §三·补 第 2 项曾把进度条挂在"Smart Checkout 桥接缺失"上；第 206–207 轮已接线 ⇒
前提消失，本节按权威重新评估。

**权威（第 226 轮补读 `ProgressDialogUI.kt` 全文）**：

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 进度条是**模态进度窗口**的构成 | `progressPanel` = 文本行 ＋ 细节行 ＋ `progressBar`（跨两列）＋ 右侧 `cancelButton`／`backgroundButton` 的按钮列 | `ProgressDialogUI.kt:56-108` |
| 无进度数据时是**不定式** | `updateProgress(fraction, text, details)`：`fraction == null` ⇒ `progressBar.isIndeterminate = true`，否则 `value = (fraction * 100).toInt()`（`maximum = 100`） | `ProgressDialogUI.kt:66-68,163-181` |
| 进度条的取色键 | `ProgressBar.trackColor` / `progressColor` / `indeterminateStartColor` / `indeterminateEndColor`（另有 passed／failed 两族） | `ManyIslandsLight.theme.json:945-953` |

**Augit 的对应面**：写操作的进行态只有**提交侧栏**的一行状态文字 ＋ 取消入口
（`reflectWriteOperation()`；第 154 轮已断言"取消一次性"），**没有模态进度窗口**——
那是本册 §2 与 `10-backlog` 已登记的**有意差异**（"Augit 的写操作本就不阻塞查看，没有模态卡住需要转后台的场景"）。

**结论（用户 2026-09-28 已认可登记为「有意产品差异」，T3 随之关闭）**：不给写操作补进度条。理由三条，都可复验：

1. 进度条在权威里是**模态 `ProgressWindow`** 的构成；Augit 不实现该窗口（写操作非阻塞、可继续浏览），
   把它的子元素搬到侧栏状态行上属于**新增界面元素**，而 `ux-spec.md:699-701` 对"进行中"的要求只有
   "**进行中禁用重复触发，显示取消和当前动作**"——这三条**都已实现并断言**（§2 表 ＋ 第 154 轮）。
2. 写操作没有任何**进度分数**（宿主 `git/commit-create`／`git/push`／`git/checkout-smart` 等都是单请求，桥接无
   `progress` 事件），权威在这种情况下正是**不定式**进度条 ⇒ 即使画也只能画一条永远在动的条，
   信息量为零；要画确定式进度就必须新增进度通道（触碰"不新增能力"边界）。
3. 视觉稿里的 68% 是**样例值**（`.progress-value { width: 68% }`），不是实测进度；`ux-spec.md:842`
   对该场景的要求是"重复动作不可触发；取消进入等待停止而不是立即报告成功"，同样不含进度百分比。

**（未采用的备选）若用户改判要保留该元素**，最小权威对齐做法是：在侧栏状态行插入 `.progress-track` ＋
`data-progress="indeterminate"` 的 `.progress-value`，取色用 `trackColor` = `control-bg-small`
（浅 `#DDDFE4`／深 `#40434A`）与 `progressColor` = `control-brand-bg`（= 现有 `--augit-accent-brand`），
并按 `indeterminateStartColor`→`indeterminateEndColor`（`#A7C5FF`→品牌色）做渐变扫动；几何沿用视觉稿的 4px 高。该备选**不在当前范围内**（用户已认可"有意产品差异"），仅在改判时启用。

## 3bis. 另两项登记（第 154/225 轮，维持原结论）

| 项 | 理由 |
| --- | --- |
| "在后台运行"按钮（`shouldShowBackground`，`ProgressWindow.java:99-110`） | Augit 的写操作本就不阻塞查看（提交后仍可浏览、可取消），没有"模态卡住需要转后台"的场景 ⇒ **不新增**（不适用） |
| 取消按钮文案可被任务覆盖（`TaskCancellation.buttonText`） | Augit 的取消入口只有"取消"一种文案，没有需要改写文案的任务 ⇒ **不需要该机制**（不适用） |
| "在后台运行"按钮（`shouldShowBackground`，`ProgressWindow.java:99-110`） | Augit 的写操作本就不阻塞查看（提交后仍可浏览、可取消），没有"模态卡住需要转后台"的场景 ⇒ 不新增 |
| 取消按钮文案可被任务覆盖（`TaskCancellation.buttonText`） | Augit 的取消入口只有"取消"一种文案，没有需要改写文案的任务 ⇒ 不需要该机制 |

## 4. 验证（第 154 轮）

- 产品改动只有 `web/src/live-data.js`（`reflectWriteOperation()` 同步取消按钮禁用态 ＋ `cancelWriteOperation()` 入口守卫）；未改 `mockup.js`／`mockup.css`。
- `tools/audit/live-shell.spec.cjs` 的 §9.3 段新增一条 `check`：`取消是一次性动作（按过即禁用、不再发第二次请求）` —— 在 `write/cancel` 注入 900 毫秒延迟期间按第二次，断言按钮已禁用且 `__writeCancels` 仍为 1。
- 未改 C#（取消链路的宿主侧早已存在）⇒ 不需要构建。
