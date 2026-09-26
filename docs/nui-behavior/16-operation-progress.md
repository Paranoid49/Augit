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

## 3. 登记未做

| 项 | 理由 |
| --- | --- |
| **进度条**（`.progress-track`／`.progress-value`）只出现在 `operation-progress` 视觉稿里，实时侧不渲染 | 该场景写的是"正在执行 **Smart Checkout**…"，而 Smart Checkout 是**宿主能力已实现、桥接与界面缺失**的项（`10-backlog.md` §三·补 第 2 项）。给一个尚未接线的操作补进度条没有意义；等接线时连同进度呈现一起做 |
| "在后台运行"按钮（`shouldShowBackground`，`ProgressWindow.java:99-110`） | Augit 的写操作本就不阻塞查看（提交后仍可浏览、可取消），没有"模态卡住需要转后台"的场景 ⇒ 不新增 |
| 取消按钮文案可被任务覆盖（`TaskCancellation.buttonText`） | Augit 的取消入口只有"取消"一种文案，没有需要改写文案的任务 ⇒ 不需要该机制 |

## 4. 验证（第 154 轮）

- 产品改动只有 `web/src/live-data.js`（`reflectWriteOperation()` 同步取消按钮禁用态 ＋ `cancelWriteOperation()` 入口守卫）；未改 `mockup.js`／`mockup.css`。
- `tools/audit/live-shell.spec.cjs` 的 §9.3 段新增一条 `check`：`取消是一次性动作（按过即禁用、不再发第二次请求）` —— 在 `write/cancel` 注入 900 毫秒延迟期间按第二次，断言按钮已禁用且 `__writeCancels` 仍为 1。
- 未改 C#（取消链路的宿主侧早已存在）⇒ 不需要构建。
