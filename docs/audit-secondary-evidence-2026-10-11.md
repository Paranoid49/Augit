# 二级页面当前证据审计（2026-10-11）

本记录只审计现行实现和本地 IntelliJ 源码，不操作真实 Git、不启动桌面应用。进入审计时工作区已有大量未提交改动；不将历史截图或同引擎像素检查写成真实 PyCharm 面级取证。

范围是 `clone`、`remote`、`repository-init`、`repository-search`、`workspace-open`、`operation-progress`、`operation-result`。权威源码锚点为 `D:/github/intellij-community` 的 `576e32820af82f97529d70f9269726803a27c016`。

## 当前确证差异

本轮在上述范围内没有发现可由当前代码与权威源码直接确认、且尚未登记或修复的**功能接线**差异。本结论不覆盖图标、几何和二级窗口拓扑；其中 Remotes 已有独立专项取证确认差异，见下表。

下列项目不能被误报为新缺陷：

| 场景 | 当前实现与依据 | 结论 |
| --- | --- | --- |
| `clone` | 实时入口使用 `openCloneDialog()` 与 `liveCloneBody()`，并复用 `bindCloneDialog()` 的校验、冻结和取消链路。[live-data.js](../web/src/live-data.js) 的 `openCloneDialog`、[mockup.js](../web/src/mockup.js) 的 `liveCloneBody`；权威为 `plugins/git4idea/backend/src/ui/GitCloneDialogComponent.kt:44-60,155`。 | 已有规格 §7.12 的输入、浅克隆、键盘、取消与失败重试测试；缺的是 PyCharm 同场景前台正文取证。 |
| `remote` | 现行场景以 `liveManagementPage("remote")` 建立管理对话框；现有规格记录已覆盖按字号、右侧详情独立滚动、失焦和失败留窗。[ui-compliance.md](ui-compliance.md) 第 995 行；权威入口为 `plugins/git4idea/backend/src/remote/GitConfigureRemotesDialog.kt:49`。 | 功能接线未见新缺口；但专项证据已确认多余 `cloud` 图标，且现行双栏内联编辑与权威表格／独立编辑窗口存在结构冲突，见 [图标、几何和状态专项取证](audit-geometry-evidence-2026-10-11.md:11)。 |
| `repository-init` | 非仓库直接初始化、仅“已在 Git 下”进入确认、成功后刷新状态的路径由 `openRepositoryInitDialog`、`submitRepositoryInit` 和 `finishRepositoryInit` 接线。[live-data.js](../web/src/live-data.js:4038)；权威 `plugins/git4idea/backend/src/actions/GitInit.java:45-99`。 | 当前结构符合权威确认时机；未完成前台 PyCharm 面级取证。 |
| `repository-search` | 搜索浮层有 pending、空结果、结果上限继续/中止和文本开关；实现入口为 `liveSearchOverlay()`、`searchLimitDialog()`。[mockup.js](../web/src/mockup.js:5359)；权威 `platform/usageView/src/com/intellij/usages/UsageLimitUtil.java:16-34`、`platform/lang-impl/src/com/intellij/ide/actions/searcheverywhere/SearchEverywhereUI.java:906-907`。 | 历史同引擎差异 2.08 已归为真实命中文本差异，不能判作布局缺陷；当前仍缺真实 PyCharm 弹层取证。 |
| `workspace-open` | 最近目录与系统选目录共用 `workspace/open` 结果模型；同目录、既有窗口、新窗口和失败分别说明事实。[live-data.js](../web/src/live-data.js:10323-10417)。 | 当前产品单实例策略与 IntelliJ 多项目打开策略并非一对一表面，缺前台对照，不能断言视觉等价。 |
| `operation-result` | 运行时提示由状态 `live.toast` 渲染、使用 `role="alert"`，没有自动消失计时器。[mockup.js](../web/src/mockup.js:5150)；权威 `platform/platform-impl/src/com/intellij/openapi/wm/impl/status/ProcessBalloon.kt:114-127`。 | 状态提示行为已有依据；缺前台气泡正文和定位取证。 |

## 有意差异

`operation-progress` 不复刻 IntelliJ 模态 `ProgressWindow` 的进度条。Augit 的写操作保持非模态，只要求禁用重复动作、显示当前动作和一次性取消；这是用户于 2026-09-28 已裁决的产品差异。[16-operation-progress.md](nui-behavior/16-operation-progress.md:57)；权威模态进度组成见 `platform/platform-impl/src/com/intellij/openapi/progress/util/ProgressDialogUI.kt:56-108,136-152`。当前运行时取消防重入实现在 [live-data.js](../web/src/live-data.js:12050)。

`workspace-open` 通过 Augit 的同目录单实例约束决定“当前窗口／激活既有窗口／新窗口”，这属于产品规格中的窗口生命周期，不应按 IntelliJ 的 `OpenProjectTask` 直接照搬。

## 本批未取得的前台证据

本批未取得上述七页的真实 PyCharm 同场景正文截图或交互记录。`ui-compliance.md` §1.6 的旧 `MEDIUM_BLOCKED` 标记描述的是当时自动化输入通道的历史限制，**不是当前工具阻塞**；主任务已能通过 Sky 在真实前台点击 Settings 菜单和字体页，并以 Cancel 恢复。后续采集应按当前前台能力逐页执行，不应再以“无法激活 Swing 菜单或弹层条目”概括为阻塞原因。

主任务后续补到 Remotes 主窗与双击编辑窗的真实前台证据：Name／URL 表格加 Add／Remove／Edit 工具栏、底部 OK；双击行出现 Define Remote（Name／URL、Fetch remote、OK／Cancel）。未输入或提交，Esc 逐层恢复。远端页面不再属于当前“没有前台正文证据”的集合，但 Augit 双栏内联编辑结构仍待现行规格冲突裁决；其他六页不据此升级覆盖状态。

同引擎像素表的 `clone`、`remote`、`repository-init`、`workspace-open` 等结果只说明视觉稿与实时 WebView2 的一致性；不替代真实 PyCharm 的字体、系统控件、窗口所有者和动态行为验证。

