# 04 · 动作系统、启用条件与图标/按钮状态

本节要点：
- IntelliJ 的动作可用性模型是**拉取式**：`AnAction.update(AnActionEvent)` 把 `enabled`/`visible`/图标/文本写进一次性的 `Presentation`；平台在调用 `update()` **之前**先把这两个标志重置为 `true`，所以「不设即为可用可见」是默认语义。
- 动作的**不可见（visible=false）等于从菜单/工具栏中消失**，动作的**不可用（enabled=false）等于留在原位灰显**；「不显示无效的占位项」在 IntelliJ 里对应的是可见性过滤 + `HIDE_DISABLED_CHILDREN` 组属性，而不是把禁用项藏起来。
- 更新时机分两套：弹出菜单/右键菜单是**打开时一次性会话内联更新**（每个子菜单单独一次会话），工具栏是**每 500 ms 轮询 + 首次显示 + 焦点/目标组件变化**才更新，且用户输入会取消在途更新。
- 图标状态解析顺序在三个绘制点上**互不相同**：工具栏按钮是「禁用 → 悬停 → 普通」（**不读 selectedIcon**），弹出菜单项是「禁用 → 选中 → 普通」，Swing 菜单勾选槽是「禁用 → 选中 → 普通」且禁用优先。
- 禁用图标默认由滤镜生成（工具栏与菜单用 `GrayFilter(brightness=33, contrast=-35, alpha=100)` 去饱和；新 UI 头部/主工具栏另用 `GrayFilter(0, 0, 30)` 半透明），而非另存一套资源。

**分类标签**：`【可直接实现】`= HTML/CSS/C# 能表达；`【Swing 特有】`= 依赖 Swing/Swing Action 模型无法照搬；`【需推断】`= 平台未直接说明、由代码或产品目标推导。

**取证说明**：任务给出的目录 `platform/platform-api/src/com/intellij/openapi/action/`、`platform/platform-impl/src/com/intellij/openapi/action/impl/`、`platform/ide-core/src/com/intellij/openapi/action/` 在当前克隆（commit 576e328）中**不存在**；真实目录是 `platform/*/src/com/intellij/openapi/actionSystem/`（包名 `com.intellij.openapi.actionSystem`）。`AnAction` 与 `Presentation` 位于 `platform/editor-ui-api`，不是 `platform-api`。以下所有引用按真实路径给出。本文只记录数值、状态映射与规则，不复制任何图标 SVG、字体或商标资源。

---

## 1. 动作的可用性模型

### 1.1 `AnAction.update()` 与 `Presentation` 的关系

- 一个动作可以被同时放在多个工具栏、菜单、弹出框中；**每个位置各有一份独立的 `Presentation`**，默认是 `getTemplatePresentation()` 的克隆；`update()` 通过 `AnActionEvent.getPresentation()` 拿到该位置那一份并就地改写。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnAction.java:36-48）
- `update()` 的契约是：**可能被高频调用、必须快、不得改 UI 或任何状态（只允许填缓存）**；`getActionUpdateThread()` 决定它在 EDT 还是 BGT 上跑；若 `Presentation.isRWLockRequired()` 为真则在读锁下调用。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnAction.java:328-363）
- 模板 `Presentation` 是一个**哨兵**：在模板上调用 `setEnabled`/`setVisible` 会触发 `LOG.warnInProduction`，且模板上的 enabled/visible 值会被忽略（菜单项与快捷键处理依赖各自的默认值）。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:550-558, 604-608）
- `AnActionEvent` 构造时断言 presentation 不是模板（`presentation.assertNotTemplatePresentation()`）。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnActionEvent.java:53-60）【Swing 特有】

### 1.2 `setEnabled` 与 `setVisible` 的区别

| 方法 | 语义 | 表现 |
| --- | --- | --- |
| `Presentation.setEnabled(boolean)` | 动作逻辑可否执行；为 false 时 `actionPerformed` 不会被调用 | 按钮/菜单项**灰显**，仍在原位 |
| `Presentation.setVisible(boolean)` | 是否在菜单/工具栏/弹出框中出现 | 不出现（被过滤掉） |

- 两者的确切措辞：`setEnabled` 的 javadoc 明确写「In case when action represents a button or a menu item, the representing button or item will be greyed out」。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:560-574）
- `setVisible` 的 javadoc 明确写「Sets whether the action is visible in menus, toolbars and popups or not」。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:375-388）
- 两者各自触发 `PROP_ENABLED` / `PROP_VISIBLE` 属性变更事件，UI 组件靠这些事件决定重绘/重算图标。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:50-61, 581-585）
- 标志位存储在同一个 `myFlags` 位域里（`IS_ENABLED=0x1`、`IS_VISIBLE=0x2`），因此两者互相独立、互不蕴含。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:64-78）
- `isEnabledAndVisible()` 是二者的合取；`setEnabledAndVisible(boolean)` **同时**设置两者（同一个值）。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:576-579, 714-716）【可直接实现：一个 `disabled` 类 + 一个「不渲染」判定，等价于 `disabled = !enabled`、`hidden = !visible`】

### 1.3 更新前的默认状态：先重置为「可用且可见」

这是最容易踩错、也最直接对应 Augit 需求的一条：

- `ActionUpdater.updateAction()` 在调用动作的 `update()` 之前显式执行 `presentation.setEnabledAndVisible(true)`，注释写「reset enabled/visible flags (actions are encouraged to always set them in `update`)」。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:595-613）
- `Presentation` 的初始位域也是 `IS_ENABLED | IS_VISIBLE | IS_DISABLE_GROUP_IF_EMPTY | IS_RW_LOCK_REQUIRED`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:78）
- 推论：**忘记在 `update()` 里设条件 = 永久可用且可见**。所以「不显示无效动作」必须在 `update()` 里主动写 `visible=false`（而不是依赖没有设置）。【可直接实现】

### 1.4 `isDumbAware`

- `isDumbAware()` 默认实现 = `this instanceof DumbAware`；`AnAction` 覆写为「若接口返回 true 则 true，否则看 `ActionClassMetaData.isDefaultUpdate(this)`」，即**没有覆写 `update()` 的动作自动视为 dumb-aware**（因为没覆写就没有索引依赖）。（来源：platform/core-api/src/com/intellij/openapi/project/PossiblyDumbAware.java:11-17；platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnAction.java:186-195）
- 推荐做法是继承 `DumbAwareAction` 而不是覆写 `isDumbAware()`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnAction.java:186-189；platform/ide-core/src/com/intellij/openapi/project/DumbAwareAction.java:19-24）
- 在索引未就绪（dumb mode）时：非 dumb-aware 的动作在 `update` 阶段被强制 `enabled=false` + `visible=false`，但它原本的 enabled/visible 会被记进 `WOULD_BE_ENABLED_IF_NOT_DUMB_MODE` / `WOULD_BE_VISIBLE_IF_NOT_DUMB_MODE` 客户端属性，dumb mode 结束后恢复。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:330-336, 357-368；由 platform/platform-impl/.../impl/ActionUpdater.kt:616-619 与 Utils.kt 的 dumb 检查共同覆盖）
- `Separator` 自身实现 `DumbAware`（分隔线永远可用）。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/Separator.java:18）
- **对 Augit 的映射**：Augit 没有「索引」概念，`isDumbAware` 可等价为「动作的启用条件是否依赖尚未就绪的异步数据（例如 Git 仓库状态尚未加载完）」。【需推断】

### 1.5 `getActionUpdateThread()`（BGT/EDT）对时序的影响

- 枚举语义：`BGT` = 后台线程 + 完整 DataContext 数据，**首选**；`EDT` = UI 线程 + 仅 UI 数据，不得访问 PSI/VFS/工程模型。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/ActionUpdateThread.java:21-38）
- `ActionUpdateThreadAware` 默认值是 **EDT**；`ActionUpdateThreadAware.Recursive` 返回 BGT。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/ActionUpdateThreadAware.java:23-35）
- `AnAction` 的默认值：若实现已废弃的 `UpdateInBackground` 且返回 true → BGT；若 `ActionClassMetaData.isDefaultUpdate(this)`（即没覆写 `update`）→ BGT；否则 **EDT**。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnAction.java:207-216）
- `ActionClassMetaData.isDefaultUpdate` 是通过反射找出 `update(AnActionEvent)` 的**声明类**是否为 `AnAction` 本身来判断的，结果缓存在 `action.myMetaFlags`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/ActionClassMetaData.java:26-28, 55-63）
- 线程决策代码：`shallAsync = (updateThread == BGT)`，`shallEDT = !(canAsync && shallAsync)`；`canAsync = Utils.isAsyncDataContext(dataContext)`。**关键推论：BGT 动作只有在 DataContext 是 async 时才真正跑在后台线程，否则仍被当作 EDT 处理。**（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:184-191）
- 在 EDT 上调用一个声明为 BGT 的操作会 `LOG.error("Calling on EDT … that requires BGT")`，除非处于 `SlowOperations.ACTION_PERFORM` 段内或单元测试模式。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:188-190）
- `ActionUpdateThreadAware.Recursive` 的强制值会**覆盖**动作自身的声明。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:182-183）【Swing 特有：Augit 是单线程 JS + C# 桥，没有 EDT/BGT 之分】

### 1.6 动作更新是「每次弹出前统一刷新」还是「持续刷新」

**两套机制并存，取决于承载容器。**

**（A）弹出菜单 / 右键菜单 / 子菜单：打开时一次性会话更新**

- 一次组展开 = 恰好一个 `ActionUpdater` = 恰好一个更新会话：`Utils.expandActionGroupSuspend` 构造 `ActionUpdater(...)` 后 `updater.runUpdateSession { updater.expandActionGroup(group) }`；`asUpdateSession()` 返回 `UpdateSessionImpl`，会话被挂到该会话内创建的每个 `AnActionEvent` 上。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:381-386；ActionUpdater.kt:546-548, 517-524, 707）
- 组展开是「先更新组本身 → 并行更新所有直接子项 → 后处理 → 更新 inline 动作」：`children.map { async { expandGroupChild(it, hideDisabled) } }.awaitAll().flatten()`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:351-392）
- **子菜单是惰性的**：`popupGroup` 的子组在打开父菜单时不会被展开，只生成一个子菜单持有者；它的填充发生在该子菜单真正被展开时（Mac 主菜单路径单独处理）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:707-708；ActionMenu.kt:357-392）
- 每次都新建会话，因此**同一个动作在同一菜单里只会被 update 一次**；不同菜单/工具栏各自持有独立 presentation，各自会 update。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:134-136, 581-584, 624-626）

**（B）工具栏：轮询 + 事件驱动**

- `ActionToolbar` 的契约原文：「Async toolbars are not updated immediately despite the name of the method. **Toolbars are updated automatically on showing and every 500 ms if activity count is changed.**」（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/ActionToolbar.java:153-163）
- 计时器周期：`TIMER_DELAY = 500`（ms），应用失活时 `DEACTIVATED_TIMER_DELAY = 5000`，打字后 `UPDATE_DELAY_AFTER_TYPING = 500`（ms）内跳过 tick。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionManagerXmlSupport.kt:79-81；ActionManagerImpl.kt:762-772, 791-794）
- tick 只在 `ActivityTracker.count` 发生变化时才真正通知监听者：`lastTimePerformed = ActivityTracker.getInstance().count; if (lastTimePerformed == lastEventCount) return`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionManagerImpl.kt:791-807）
- `AnAction` 的 javadoc 给出应用侧触发手段：「If the action is added to a toolbar, its update method can be called twice a second, but only if there was any user activity or a focus transfer. If your action's availability is independent of these events, call `ActivityTracker.getInstance().inc()`」。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnAction.java:347-353）
- 监听器的注册/注销跟随组件显示状态：`showNotify()` 里 `addTimerListener`，`hideNotify()` 里 `removeTimerListener`；keymap 变化会刷新全部工具栏按钮的 tooltip。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ToolbarUpdater.kt:44-68, 96-104）
- 工具栏 tick 会被跳过：组件未显示、有菜单路径被选中（弹出菜单打开时不改工具栏状态，避免同一动作在菜单里被错误启用/禁用）、有模态对话框获得焦点。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ToolbarUpdater.kt:118-142）
- 目标组件变化会触发立即更新；首次显示走 fast-track。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:1373-1383, 1052-1057）

**（C）用户输入取消在途更新**

- `IdeEventQueue` 预处理器：任何 `KeyEvent(keyCode != 0)` 或 `MouseEvent.MOUSE_PRESSED` 都会 `cancelAllUpdates(ourToolbarJobs, ...)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:696-704）
- 打开右键菜单会取消全部更新。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:486-488）

**（D）会话内结果批量应用**

- 更新结果先写进会话私有的 `updatedPresentations`，UI 只在一个批次里被触碰：`applyPresentationChanges()` 在每次组展开结束时于 EDT 调用一次。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:134-136, 159-173, 336-338）
- 应用方式：保留已创建的自定义组件 → `postProcessPresentation` → `orig.copyFrom(copy, customComponent, allFlags=true)` → 若可见则更新自定义组件。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:158-173）

### 1.7 更新抛异常的后果

- `ActionUtil.updateAction` 的 `finally { if (!isPerformed) presentation.isEnabled = false }` **保证抛异常的动作不可能保持 enabled**。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:382-386）
- `IndexNotReadyException` 在 `ActionUpdater` 层被捕获后设 `isEnabledAndVisible = false` 并把该 presentation 当成功结果缓存。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:616-619）
- 其他 `Throwable` → `handleException` 记 `LOG.error`（带操作名 + actionId + 文本），结果标记 `failed`，**该 presentation 不被缓存**，因此会退回「上一次已知状态 / 模板状态」。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:620-627, 857-875）
- 没有「更新超时后中止」的硬超时；只有共享数据重试上限 `actionSystem.update.actions.max.await.retries`（默认 500）和协作式取消。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:143, 942-956, 822-839）
- 诊断阈值：单次 update > 1 s 记 warn；抢 EDT > 300 ms 记 info；组展开累计抢 EDT > 500 次或 > 3 s 记 warn 并建议改用 BGT。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:202-206, 289-295, 343-347）【需推断：Augit 无 EDT，这些阈值只用于「单次可用性求值耗时预算」】

### 1.8 弹出框打开路径与时间预算

- 打开路径是**同步阻塞**的：`ActionPopupMenuImpl.show()` 先清空再 `updateChildren(...)` → `Utils.fillPopupMenu(...)`；只有在 `getComponentCount() != 0` 时才真正 `super.show(...)`；如果求值后一个菜单项都没有，则弹出框不显示并记 warn（`"'<place>' popup menu fails to show: no menu items"`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionPopupMenuImpl.java:118-152, 170-173）
- ⚠️ **但这条守卫在实践中通常不会触发**：`fillMenuInner` 在展开结果为**空列表**时会补一个 `EMPTY_MENU_FILLER` 项（`EmptyAction`，文本取自 `CommonBundle "empty.menu.filler"`），所以"这个组里一个动作都没有"在 IntelliJ 里表现为**弹出一个只有一条占位文案的菜单**，而不是不弹菜单。注意判断用的是**未过滤的原始列表** `list.isEmpty()`，因此"列表非空但全部被可见性过滤掉"仍会走到上面那条不弹出分支。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:718-724, 218-221）
- `popupMenuWillBecomeVisible` 只在菜单为空时补一次填充，因此不会重复求值。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionPopupMenuImpl.java:203-210）
- 弹出框的 DataContext 在 show 时创建，并包成 async 上下文（`Utils.createAsyncDataContext(...)`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionPopupMenuImpl.java:128-130）
- 时间预算（fast track）：EDT 最多阻塞 `actionSystem.update.actions.async.fast-track.timeout.ms`（默认 **50 ms**）；主菜单或新 UI 主工具栏的**首次**求值放宽到 **1000 ms**；连续失败会把预算按失败次数整除收缩（注释举例：20 个工具栏 × 50 ms = 1 s 冻结）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:1317-1338）
- 弹出框在求值超过 `actionSystem.popup.progress.icon.delay`（默认 **300 ms**）后才在 glass pane 上插入进度图标（菜单项场景用 `AnimatedIcon.Default`，非菜单用 `AnimatedIcon.Big`，且位置按菜单项 / 弹出点分别偏移）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:651-682）
- 禁止递归更新会话：`LOG.error("Recursive update sessions are forbidden. Reuse existing AnActionEvent#getUpdateSession instead.")`，触发条件是「在 EDT 上且已处于 `SlowOperations.ACTION_UPDATE` 段内」。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:154-156, 936-940）
- **Augit 映射**：右键菜单应在**同步的一帧内**完成全部项的可用性求值（本地数据 + 已缓存的 Git 状态），不要为菜单项打异步请求。（来源：同上）【可直接实现】
- **Augit 与 IntelliJ 的差异点（须按产品规格裁决）**：IntelliJ 对"空组"会补一条占位文案（`EMPTY_MENU_FILLER`），而 Augit 的产品规格要求「不显示不可用的占位项」。因此在 Augit 里，全空的右键菜单应**不弹出**（等价于 `getComponentCount()==0` 分支），而不是照搬这条 filler 行为；把它记为**有意的产品偏离**，不要声称与 IntelliJ 一致。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:718-724；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionPopupMenuImpl.java:132-135）【需推断】

### 1.9 `Presentation.copyFrom` 的语义

- 默认 `copyFrom(other)` 只复制 **enabled/visible** 两个标志（外加 text/description/icon/selectedIcon/disabledIcon/hoveredIcon 与客户端属性），**不复制其他 flag**；`allFlags=true` 时整字复制（保留 template 位）。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:623-677）
- 客户端属性是**单层扁平命名空间、按 `key.toString()` 存**：`copyFrom` 会做并集但还是会丢掉源里没有的键——因此把重要客户端属性放在**模板** presentation 上更稳（平台自己的 `ActionUtil.COMPONENT_NAME` 文档就是这么建议的）。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:663-707；platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:93-130）【可直接实现：等价于「以一份权威状态对象为唯一源，避免增量合并」】

---

## 2. 禁用与隐藏的判定差别

### 2.1 核心结论

- **禁用（enabled=false）：仍然显示，只是灰显。**
- **隐藏（visible=false）：从容器里消失。**
- 「不显示无效动作」只能用 **visible=false**，或使用组属性 `HIDE_DISABLED_CHILDREN` 把「禁用子项」整体剔除。仅设 `enabled=false` 会留下一个灰显的占位项。

### 2.2 组展开时的可见性/禁用过滤（菜单与工具栏共用的第一道闸）

`ActionUpdater.expandGroupChild()` 是权威判定点：

1. 子项 `presentation == null || !presentation.isVisible` → 直接丢弃（返回空列表）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:452-456）
2. 组本身 `!presentation.isVisible` → 不处理该组的子项，返回空列表。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:360-364）
3. `hideDisabledBase && !presentation.isEnabled && !alwaysVisible` → 丢弃该子项。其中 `alwaysVisible = child is ActionGroup && presentation.getClientProperty(ALWAYS_VISIBLE_GROUP) == true`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:457-460）
4. `hideDisabled` 的传播来源：父组 presentation 的 `HIDE_DISABLED_CHILDREN` 属性（`hideDisabled = hideDisabled || presentation.getClientProperty(HIDE_DISABLED_CHILDREN) == true`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:365）
5. 弹窗组在 `SUPPRESS_SUBMENU`/`ALWAYS_VISIBLE_GROUP` 时跳过这些检查（`skipChecks`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:464-471）
6. 判断「有没有可见子项 / 有没有可用子项」时**只扫描前 100 个非分隔线子项**并提前退出（`childrenFlow.take(100).filter { it !is Separator }.takeWhile { ... }`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:474-490）【需推断：这是性能保护；Augit 菜单很小，可对全部子项求值】

### 2.3 菜单项的可见性/禁用落地（第二道闸）

- `Utils.filterInvisible()` 逐项过滤并做分隔线整理：
  - `!presentation.isVisible` → 跳过，并记 `LOG.error("Invisible menu item for … Most probably caused by async presentation updates that must be avoided")`；
  - 非分隔线且文本为空 → 跳过并记 error；
  - 开头的无文本分隔线 → 跳过；相邻分隔线 → 用后一个替换前一个（即合并）；结尾的无文本分隔线 → 删除。
  （来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:786-819）
- **`ActionMenuItem.updateFromPresentation()` 明确不调用 `setVisible`**，注释写「all items must be visible at this point」，只做 `setEnabled(presentation.isEnabled)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:122-146）
- `ActionMenu`（子菜单持有者）则同时应用两者：`isVisible = presentation.isVisible; setEnabled(presentation.isEnabled)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenu.kt:216-224）
- 结论：**菜单项一旦建出来就必定可见，禁用项留在原位灰显**；隐藏只能靠上层过滤（2.2 与 2.3 第一段）。【可直接实现】

### 2.4 `HIDE_DISABLED_CHILDREN`：把「不可用项」真正隐藏掉

- 属性定义：`ActionUtil.HIDE_DISABLED_CHILDREN = Key.create("HIDE_DISABLED_CHILDREN")`，注释「Hide disabled child actions」。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:167-169）
- 命中时，组展开会把子项包装成强制隐藏禁用子项的透明包装：`ActionGroupUtil.forceHideDisabledChildren(child)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:497-507）
- `forceHideDisabledChildren` 返回一个 `ActionGroupWrapper implements TransparentWrapper`，其模板 presentation 上打 `HIDE_DISABLED_CHILDREN=true`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ActionGroupUtil.java:48-66）
- 现成的便捷类：`DefaultCompactActionGroup` 在模板 presentation 上设该属性。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/DefaultCompactActionGroup.java:9-28）
- `ActionUtil.ALWAYS_VISIBLE_GROUP` 可让一个组豁免隐藏（例如 VCS 的分支组、Recent Files 组）。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:171-173；使用例：platform/dvcs-impl/src/com/intellij/dvcs/ui/RootAction.java:34）
- 参考实现：`SuppressActionWrapper` 用它来隐藏不可用的 suppress 项。（来源：platform/lang-impl/src/com/intellij/codeInspection/ui/actions/suppress/SuppressActionWrapper.java:51）【可直接实现：Augit 的文件树右键菜单应把「禁用子项」过滤成不渲染，而不是渲染灰项】

### 2.5 空组的两种处理：`hideGroupIfEmpty` 与 `disableGroupIfEmpty`

- `Presentation.setHideGroupIfEmpty(boolean)`：**组在没有任何可见子项时被隐藏**，默认 `false`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:419-430）
- `Presentation.setDisableGroupIfEmpty(boolean)`：**组在没有任何可见子项时显示为禁用**，**默认 `true`**。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:432-443；默认位在 Presentation.java:78）
- 落地逻辑：仅对 `isPopup` 且未跳过检查的组生效；`if (!performOnly && !hasVisible && disableEmpty) presentation.setEnabled(false)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:491-496）
- 返回规则：`!hasEnabled && hideDisabled || !hasVisible && hideEmpty` 时——若组可执行（`performGroup`）则退化为**单个动作项**，否则整个组消失（空列表）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:499-503）
- 默认值组合的含义：**子菜单默认「空则灰显」而不是「空则消失」**；要让它消失必须显式 `setHideGroupIfEmpty(true)`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:424-443）【可直接实现】

### 2.6 工具栏按钮禁用时的图标表现

- `ActionButton.updateIcon()`：优先使用 `presentation.getDisabledIcon()`；否则若普通图标非空且 `IconLoader.isGoodSize(icon)`（宽>0 且高>0）则 `myDisabledIcon = myLook.getDisabledIcon(myIcon)`；否则置空并 `LOG.error("invalid icon …")`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:470-486；platform/util/ui/src/com/intellij/openapi/util/IconLoader.kt:271）
- 默认 look 的 `getDisabledIcon(icon) = IconLoader.getDisabledIcon(icon)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:135-137）
- `paintButtonLook()`：`if (isEnabled() || !StartupUiUtil.isUnderDarcula() || ExperimentalUI.isNewUI()) look.paintBackground(...)`——**在新 UI 下禁用按钮也会走背景绘制入口**，但背景色取的是状态色（见 7.1），普通态通常为 null 因而无背景；随后总是绘制图标与边框。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:543-554）
- `ActionButtonLook.paintBorder`：`if (state == NORMAL && !component.isBackgroundSet()) return;`——普通态无背景时不画边框。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:108-115）
- 结论：**禁用工具栏按钮 = 图标换成禁用版（去饱和或半透明）+ 不响应 hover 背景/边框**。禁用时 `getPopState()` 不会返回 POPPED/PUSHED，因为 `getPopState(isPushed)` 的 hover 分支要求 `isEnabled()`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:626-639）【可直接实现：`button:disabled .icon { opacity / filter: grayscale }`，并去掉 `:hover` 背景】

### 2.7 子菜单/组项的可见性对按钮的影响

- 动作组在工具栏上是一个按钮；它是否画下拉箭头由 `shallPaintDownArrow()` 决定：必须是 `ActionGroup`、`presentation.isPopupGroup()` 为真、且模板与当前 presentation 都没有 `HIDE_DROPDOWN_ICON` 属性。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:531-537）
- 空组在工具栏上的行为由 1.3/2.5 的规则决定：要么消失，要么禁用，要么退化为可执行单项。【可直接实现】

### 2.8 `ActionUtil.performAction` 会重新检查什么

- 检查项（按序）：dumb mode 且非 dumb-aware → 忽略；**只重新检查 `presentation.isEnabled`**，false 时返回 `Ignored("action is disabled")`；**不检查 visible**；**不重新跑 update**；不校验 DataContext。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:463-480）
- `ActionManagerImpl.performWithActionCallbacks` 追加检查：`SKIP_ACTION_EXECUTION` 客户端属性 → 忽略；目标 `CONTEXT_COMPONENT` 不可见（且 place 不是 touchbar、未设 `ALLOW_ACTION_PERFORM_WHEN_HIDDEN`）→ warn + `Ignored("target component is not showing")`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionManagerImpl.kt:591-605）
- 契约要求动作自己在 `actionPerformed` 里**重新校验上下文**并「不适用就什么都不做」，且不得假设 `update()` 已跑过；废弃的 `beforeActionPerformedUpdate` 标注为「NEVER CALLED」。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnAction.java:365-375, 411-434）【可直接实现：Augit 的每个动作在 IPC 入口再做一次前置条件校验】

---

## 3. Toggle 动作

### 3.1 模型

- `ToggleAction extends AnAction implements Toggleable`，用于「菜单里的复选框」或「工具栏上保持按下态的按钮」；默认 `KeepPopupOnPerform.IfPreferred`。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ToggleAction.java:14-22, 50-57）
- 选中态存储在 presentation 的客户端属性 `SELECTED_KEY = Key.create("selected")`（同时保留字符串键 `SELECTED_PROPERTY = "selected"`）；`isSelected(presentation)` = `Boolean.TRUE.equals(getClientProperty(SELECTED_KEY))`。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/Toggleable.java:32-62）
- 组件也可单独承载选中态：`Toggleable.setSelected(JComponent, Boolean)` 会写入组件客户端属性并 `repaint()`；传 `null` 表示「回到默认行为」。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/Toggleable.java:64-83）
- `ToggleAction.actionPerformed` 取反：`boolean state = !isSelected(e); setSelected(e, state); Toggleable.setSelected(e.getPresentation(), state)`。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ToggleAction.java:59-65）
- `ToggleAction.update` 除了同步选中态，还在**弹出菜单（非搜索弹出框）中强制把 icon 置空**，注释写「force showing check marks instead of toggle icons」。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ToggleAction.java:83-92）

### 3.2 三种呈现形态

| 位置 | 选中态呈现 | 依据 |
| --- | --- | --- |
| 弹出/右键菜单（无图标） | 勾选标记（checkmark），未选中时用 16×16 空图标占位以保持对齐 | ActionMenuItem.kt:196-233 |
| 弹出/右键菜单（有图标） | 图标外套一层 `PoppedIcon(icon, 16, 16)` 高亮底 | ActionMenuItem.kt:216-220 |
| 工具栏按钮 | 进入 `PUSHED` 状态 → 画按下背景 + 边框（不是勾选标记） | ActionButton.java:221-223, 626-639 |

- 菜单侧细节：`updateIcon` 中若是 toggleable 且（`icon == null` 或位于 checked group 内 或 全局关闭菜单图标），则：有图标就用图标；否则选中时用 checkmark 三态图标，未选中时把 normal/selected/disabled 三槽全部设为 `EmptyIcon.ICON_16`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:196-215）
- `ActionMenuItem` 继承 `JBCheckBoxMenuItem`，其 `isSelected()` 返回 toggle 标记——这是 Swing 复选框语义的落点。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:57-69, 260）
- 无图标 toggleable 的 checkmark 由 LAF 名称解析取得三态（normal/selected/disabled），`ActionStepBuilder.calcRawIcons` 也会做同样的替换。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:279-291；platform/platform-impl/src/com/intellij/ui/popup/ActionStepBuilder.java:151-161）
- 工具栏按下时的图标：`getIcon()` 在 `PUSHED`/`POPPED` 时会用 `presentation.getHoveredIcon()`（若已设），选中态走的是这条「悬停图标」通道而**不是** `selectedIcon`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:450-456）
- **重要边界**：工具栏路径完全不消费 `Presentation.getSelectedIcon()`（见 4.2）；要让工具栏的选中态换图标，动作必须在 `update()` 里自行 `setIcon(...)`/`setHoveredIcon(...)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:450-456）【Swing 特有（结论层面的差异）；实现层面【可直接实现】】

### 3.3 菜单勾选标记与工具栏按下态、`aria-pressed` 的对应

`ActionButton.AccessibleActionButton` 给出无障碍状态映射，可直接翻译成 Web 语义：

- `state == PUSHED` → 加 `AccessibleState.PRESSED`；`isSelected()` → 加 `AccessibleState.CHECKED`；`isFocusOwner()` → 加 `FOCUSED`；`shallPaintDownArrow()` → 加 `EXPANDABLE`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:730-759）
- 属性变更监听：当 presentation 的 `"selected"` 属性由 false→true 时广播 `ACCESSIBLE_STATE_PROPERTY, null → CHECKED`，反向则 `CHECKED → null`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:142-159）
- `AccessibleValue` 语义：选中 → 1，未选中 → 0；`setCurrentAccessibleValue` 0/非 0 分别调用 `Toggleable.setSelected(this, false/true)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:787-827）
- 弹出框侧：toggleable 项的无障碍图标描述在选中/未选中之间切换。（来源：platform/platform-impl/src/com/intellij/ui/popup/PopupFactoryImpl.java:940-944）
- **Augit 映射**：`aria-pressed="true|false"` 对应 CHECKED；若渲染为 menu 角色则用 `role="menuitemcheckbox"` + `aria-checked`；键盘焦点环对应 FOCUSED；有子菜单对应 `aria-haspopup`/`aria-expanded`。【需推断（平台无 HTML 语义，只有 Swing Accessible 语义）】

### 3.4 Toggle 与弹出框是否保持打开

- `Presentation.setKeepPopupOnPerform(KeepPopupOnPerform)` 用两个标志位编码四态 `Never / IfRequested / IfPreferred / Always`；toggle 默认 `IfPreferred`，非 toggle 默认 `Never`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:67-68, 492-517；platform/platform-api/src/com/intellij/openapi/actionSystem/KeepPopupOnPerform.java:9-45）
- 实际行为还受用户设置 `UISettings#getKeepPopupsForToggles` 影响。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:492-501）【需推断：Augit 的右键菜单是否在点击后关闭，需要一个等价设置位】

---

## 4. 图标状态解析顺序

### 4.1 四个图标槽位

`Presentation` 持有四个图标槽：`icon`（supplier）、`disabledIcon`、`hoveredIcon`、`selectedIcon`，各有独立属性键 `PROP_ICON`/`PROP_DISABLED_ICON`/`PROP_HOVERED_ICON`/`PROP_SELECTED_ICON`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:56-61, 83-87, 282-357）
`Presentation.copyFrom` 会复制全部四个槽。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:654-661）

### 4.2 三个绘制点的解析顺序（互不相同）

**（A）工具栏按钮 `ActionButton.getIcon()`：禁用 > 悬停 > 普通**

```
enabled = isEnabled()
popState = getPopState()
hoveredIcon = (popState == POPPED || popState == PUSHED) ? presentation.getHoveredIcon() : null
icon = enabled ? (hoveredIcon ?: myIcon) : myDisabledIcon
```
（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:443-456）

- 空图标回退：模板 presentation 的图标，否则 `AllIcons.Toolbar.Unknown`；禁用时优先模板的 disabledIcon，否则 `IconLoader.getDisabledIcon(icon)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:462-468）
- **`selectedIcon` 在工具栏路径完全未被引用**。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:450-456）

**（B）弹出菜单项 `ActionStepBuilder.calcRawIcons`：禁用 > 选中 > 普通，且禁用会污染两个槽**

- 取 `icon` / `selectedIcon` / `disabledIcon` 三槽（并尊重 `HIDE_ICON` 客户端属性）；
- 若 normal 与 selected 都为空且动作是 `Toggleable` 且（选中或 `forceChecked`），三槽全部替换为 LAF checkmark 的三态；
- 若 `!presentation.isEnabled()`：`icon = disabledIcon != null || icon == null ? disabledIcon : getDisabledIcon(icon)`，`selectedIcon = disabledIcon != null || selectedIcon == null ? disabledIcon : getDisabledIcon(selectedIcon)`。
（来源：platform/platform-impl/src/com/intellij/ui/popup/ActionStepBuilder.java:145-167）
- 绘制时二次选择：`getIcon(selected) = selected && mySelectedIcon != null ? mySelectedIcon : myIcon`。（来源：platform/platform-impl/src/com/intellij/ui/popup/PopupFactoryImpl.java:985-987）

**（C）Swing 菜单勾选槽：禁用 > 选中 > 普通**

- `BegMenuItemUI.getCheckIcon()`：`!menuItem.isEnabled() ? disabledIcon : isSelected(menuItem) ? selectedIcon : icon`；宽度超过 `maxGutterIconWidth` 的图标直接丢弃（返回 null）。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:477-483）
- `IdeaMenuUI.getAllowedIcon()`：先 `enabled ? getIcon() : getDisabledIcon()`，然后**仅在 enabled 时**才可能覆盖为 selectedIcon——即禁用项永不显示选中图标。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/IdeaMenuUI.java:536-545）
- 菜单项自身（非勾选槽）的 Swing 图标：`!model.enabled → getDisabledIcon()`，`pressed && armed → getPressedIcon() ?: getIcon()`。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:175-188）【Swing 特有】

**汇总表**

| 绘制点 | 顺序 | 是否用 selectedIcon |
| --- | --- | --- |
| 工具栏按钮 | disabled → hovered(POPPED/PUSHED) → normal | 否 |
| 弹出菜单项 | disabled → selected → normal（禁用同时覆盖 selected 槽） | 是 |
| Swing 菜单勾选槽 | disabled → selected → normal | 是 |
| 树/列表单元格渲染器 | selected → normal（选中优先） | 由渲染器各自实现 |

（来源：ActionButton.java:450-456；ActionStepBuilder.java:145-167；BegMenuItemUI.java:477-483；IdeaMenuUI.java:536-545）

### 4.3 禁用图标的自动生成

- 工具栏与菜单的默认禁用图标都是**运行时滤镜生成**，不是预置资源：`ActionButtonLook.getDisabledIcon(icon) = IconLoader.getDisabledIcon(icon)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:135-137）
- `IconLoader.getDisabledIcon` 会走 `filterIcon`（对 `EmptyIcon` 原样返回，对 `CachedImageIcon` 复用缓存）。（来源：platform/util/ui/src/com/intellij/openapi/util/IconLoader.kt:278-299）
- 滤镜数值：`UIUtil.getGrayFilter()` = `new GrayFilter(33, -35, 100)`（brightness=33、contrast=-35、alpha=100 即不透明，属去饱和/灰度化）。（来源：platform/util/ui/src/com/intellij/util/ui/UIUtil.java:300-302）
- 另一套并列机制：文本类灰色滤镜 `new GrayFilter(20, 0, 100)`。（来源：platform/util/ui/src/com/intellij/util/ui/UIUtil.java:304-305）
- 新 UI 的头部/主工具栏按钮 look **换成半透明而非去饱和**：`GrayFilter(0, 0, 30)`（alpha=30，约 70% 透明），并按 `MainToolbar.Button.iconSize`（默认 20 逻辑 px）重缩放图标。（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/customFrameDecorations/header/toolbar/HeaderToolbarButtonLook.kt:33-37, 55-56, 104-122）
- 另有通用半透明工具 `IconLoader.getTransparentIcon(icon)` 默认 `alpha = 0.5f`（与 disabled 规则是两回事）。（来源：platform/util/ui/src/com/intellij/openapi/util/IconLoader.kt:301-321）
- **Augit 映射建议**：普通工具栏禁用图标用 `filter: grayscale(...)` + 亮度/对比度调整近似 `GrayFilter(33,-35,100)`；主工具栏/头部按钮禁用图标用 `opacity: 0.3` 近似 `GrayFilter(0,0,30)`。不要为禁用态准备独立 SVG 资源。【可直接实现】

### 4.4 尺寸与 DPI 缩放

- 工具栏按钮首选尺寸 = `max(minimumButtonSize, icon + iconInsets)`，再加上组件 insets；`getMinimumSize()` 直接返回 `getPreferredSize()`。图标在 `(尺寸 − insets − iconInsets)` 内**居中**。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:417-441；platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:188-198）
- 工具栏最小按钮尺寸（逻辑值，再由 `JBUI.size` 缩放）：默认 `22×22`；navbar `20×20`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/ActionToolbar.java:77-86）
- 新 UI 主工具栏按钮 `30×30`，图标尺寸 `20`；burger 菜单图标 `20`；`MainToolbar.Button.size` / `MainToolbar.Button.iconSize` 是可覆盖的命名键，默认值来自 `defaultExperimentalToolbarButtonSize() = size(30,30)` 与 `defaultExperimentalToolbarButtonIconSize() = 20`。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1269-1299, 1313-1315）
- 工具栏内距：**按钮内距来自工具栏的 `ActionButtonBorder`，默认 `({2}, {1})`（directional=2、orthogonal=1，未缩放，随后由 `JBInsets` 做 DPI 缩放）**；映射到 AWT insets 时按方向不同——水平工具栏为 `Insets(top=1, left=2, bottom=1, right=2)`，垂直工具栏为 `Insets(top=2, left=1, bottom=2, right=1)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:826, 832-841, 1740-1764）
- ⚠️ **需要更正的常见误解**：`JBUI.CurrentTheme.Toolbar` 里确实存在 `Toolbar.Button.buttonInsets`（默认 `JBInsets.create(1, 2)`）与 `MainToolbar.Icon.insets`（新 UI 默认空、否则 `(1,2)`）两个主题键，但**它们并未被 `ActionButton` / `ActionToolbarImpl` 的按钮绘制路径消费**；实际生效的是上一条的 `ActionButtonBorder`。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1235-1267；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:826, 1740-1764）
- 工具栏容器内距：非 mini 模式取 `JBUI.CurrentTheme.Toolbar.verticalToolbarInsets()` / `horizontalToolbarInsets()`，新 UI 下为 `insets(5, 7)`；否则 `JBUI.Borders.empty(2)`；mini 模式为空边框且不透明。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1243-1259；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:1679-1699）
- mini 模式：`minimumButtonSize = JBUI.emptySize()`，并使用 `ActionButtonLook.INPLACE_LOOK`（无背景无边框）；带装饰按钮的非 mini 模式最小尺寸为 `JBUI.size(30, 20)`，否则 `DEFAULT_MINIMUM_BUTTON_SIZE`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:1679-1699, 684-692）
- 工具栏分隔线几何：`gap = scale(2)`、`center = scale(3)`，因此宽 = `gap*2 + center`（= 8 逻辑 px）、高 = `scale(24)`；颜色 = `JBUI.CurrentTheme.Toolbar.SEPARATOR_COLOR`（键 `ToolBar.separatorColor`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:881-914；platform/util/ui/src/com/intellij/util/ui/JBUI.java:1232-1233）
- 弹出菜单图标会**按该弹出框内最大图标盒统一缩放**（`calcRawIcons(..., forceChecked=true)` 求出 maxW/maxH 后逐项 `scaleIconToSize`；缺失图标用同尺寸 `EmptyIcon` 补齐）。（来源：platform/platform-impl/src/com/intellij/ui/popup/ActionStepBuilder.java:79-108；platform/platform-impl/src/com/intellij/ui/popup/PopupFactoryImpl.java:922-933）
- 缩放公式：`neededScale = min(maxW,maxH)/min(w,h) * currentScale`，差值 < 0.01 时复用原图标；`EmptyIcon` 特判为精确盒尺寸。（来源：platform/platform-impl/src/com/intellij/ui/popup/PopupFactoryImpl.java:1040-1054）
- 菜单勾选槽宽度上限由 UIManager 键 `Menu.maxGutterIconWidth` / `MenuItem.maxGutterIconWidth` 控制，默认 **18**；超过上限的图标被丢弃（不裁剪）。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/IdeaLaf.kt:49-50；platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:89-100, 477-483）
- 16 逻辑 px 的具体落点：`EmptyIcon.ICON_16`（菜单 toggle 占位）与 `PoppedIcon(icon, 16, 16)`（这里 16 是**设备像素**，不走 `JBUI.scale`）。（来源：platform/util/ui/src/com/intellij/util/ui/EmptyIcon.java:29；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:211-219）
- 缩放函数：`JBUIScale.scale(int) = round(userScaleFactor * i)`。（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:391-401）
- 新图标 API 单位：`px` 只乘上下文缩放（`contextScale * value`），`dp` 乘 `displayDensity * contextScale * value`，取整。（来源：platform/icons-impl/src/com/intellij/platform/icons/impl/modifiers/IconModifier.kt:21-29；platform/icons-api/src/com/intellij/platform/icons/design/IconUnits.kt:16-47）
- 树的图标尺寸不由平台强制，渲染器画什么就是什么；最小行高来自 `Tree.rowHeight` 主题键。（来源：NodeRenderer 相关，见 4.7）【需推断（NodeRenderer 未逐行核读，标「需复核实」）】
- **Augit 映射**：`--toolbar-button-size: 22px`（普通）/ `--main-toolbar-button-size: 30px`，主工具栏图标 `20px`，菜单/树/普通工具栏图标 `16px`，菜单勾选槽宽 `18px`。这些逻辑 px 在 WebView2 中由浏览器 DPI 处理，不需要自己乘缩放系数。【可直接实现】

### 4.5 修饰符（modifier）与角标几何

- 两种单位：`IconUnit.DisplayPoint`（`dp`）与 `IconUnit.Pixel`（`px`）；`IconUnit.Zero = 0.dp`。（来源：platform/icons-api/src/com/intellij/platform/icons/design/IconUnits.kt:16-47）
- 边距修饰符 `margin(left, top, right, bottom)`，四参默认全 `Zero`；单参 = 四边相同；双参 = (vertical, horizontal)。`IconMargin` 的声明顺序是 top/left/bottom/right。（来源：platform/icons-api/src/com/intellij/platform/icons/modifiers/MarginIconModifier.kt:7-18；platform/icons-api/src/com/intellij/platform/icons/design/IconMargin.kt:6-18）
- 对齐枚举 9 值：`TopLeft … BottomRight` = 垂直 {Top,Center,Bottom} × 水平 {Left,Center,Right}；`align(...)` 写 `layout.align`，布局默认 `null` 表示不偏移。（来源：platform/icons-api/src/com/intellij/platform/icons/design/IconAlign.kt:11-37；platform/icons-api/src/com/intellij/platform/icons/modifiers/AlignIconModifier.kt:23）
- 放置数学（设备 px）：`x = leftMargin + offsetX`、`y = topMargin + offsetY`、`w/h = round(px * factor)`；偏移量 `Center: slot/2 − size/2 − pos`、`Bottom: slot − pos − bottomMargin − size`、`Right: slot − pos − rightMargin − size`；最终 x/y 钳制到 ≥ 0。（来源：platform/icons-impl/.../rendering/layers/LayerLayout.kt:87-132；路径前缀 platform/icons-impl/src/com/intellij/platform/icons/impl/）
- 修饰符按**声明顺序从左到右**复合：`a then b` 生成组合修饰符，`CombinedIconModifier.applyTo(layout) = other.applyTo(root.applyTo(layout))`；根修饰符是恒等。（来源：platform/icons-impl/src/com/intellij/platform/icons/impl/modifiers/CombinedIconModifier.kt:8-9；DefaultModifiersFactory.kt:14-19；RootIconModifier.kt:8-9）
- `alpha`/`scale`/`stroke`/`cutoutMargin` 分别写 `layout.alpha`（默认 `1f`）/`layout.scale`/`layout.stroke`/`layout.cutoutMargin`。（来源：platform/icons-impl/src/com/intellij/platform/icons/impl/modifiers/AlphaIconModifier.kt:22；ScaleModifier.kt:9-10；StrokeModifier.kt:9-23；CutoutMarginModifier.kt:27）
- **角标默认在右上角**，不是右下角：`IconDesigner.badge(color, shape = circle(2.8.dp), align = IconAlign.TopRight, cutout = 1.2.dp)`。（来源：platform/icons-api/src/com/intellij/platform/icons/design/IconDesigner.kt:31-39）
- 经典圆点角标按图标尺寸归一化：圆心 `x = IconBadge.dotX/20`（默认 `16.5/20`）、`y = IconBadge.dotY/20`（默认 `3.5/20`），半径 `IconBadge.dotRadius/20`（默认 `3.5/20`）；挖孔时半径额外加 `IconBadge.borderWidth/20`（默认 `1.5/20`），非挖孔时边框为 0；尺寸取 `min(width, height)`，越界则返回 null。（来源：platform/core-ui/src/ui/BadgeDotProvider.kt:6-31）
- 传统 `LayeredIcon` 的叠加约束是 9 个方位常量（CENTER/NORTH/NORTH_EAST/EAST/SOUTH_EAST/…），按 `(父 − 子)` 计算 x/y；但仓库里的实际用法是**按需选择**、没有一个全局约定（文本角标用 SOUTH_EAST、插件 logo 用 SOUTH_WEST）。（来源：platform/core-ui/src/ui/LayeredIcon.kt:268-326；platform/core-ui/src/util/IconUtil.kt:589-599；platform/platform-impl/src/com/intellij/ide/plugins/newui/PluginLogoIcon.java:72-77）【需复核实：仅核读了 LayeredIcon 的常量与两处用法】
- **结论**：平台不存在「角标一律在右下角」的规则。Augit 若需要角标，建议统一采用**右上角**（与新图标 API 的 `badge()` 默认一致），并把它作为项目内约定记录下来。【需推断】

### 4.6 分层与合成

- `IconDesigner` 的每个方法 = 一层，**声明顺序即渲染顺序**；层类型：image / icon（嵌套）/ box / row / column / spacer / animation / shape。（来源：platform/icons-api/src/com/intellij/platform/icons/design/IconDesigner.kt:10-29）
- 渲染按层序进行，**后声明的画在上层**；整体尺寸取各层消耗空间的最大值。（来源：platform/icons-impl/src/com/intellij/platform/icons/impl/rendering/DefaultIconRenderer.kt:26-53）
- 布局层尺寸：Row = 宽度求和 / 高度取最大；Column = 高度求和 / 宽度取最大；Box = 两者都取最大，且 Box 的所有子层画在同一 bounds 上。（来源：platform/icons-impl/src/com/intellij/platform/icons/impl/rendering/layers/LayoutIconLayerRenderer.kt:18-80）
- 普通 Swing 图标可作为一层嵌入（`SwingIconLayer(legacyIcon, modifier)`）。（来源：platform/icons-impl/src/com/intellij/platform/icons/impl/layers/SwingIconLayer.kt:9）
- 传统 `RowIcon` 从左到右排布、按 TOP/CENTER/BOTTOM 对齐；总宽 = 宽度和，高 = 最大高度。传统 `LayeredIcon` 按数组顺序绘制（后者在上），跳过 disabled 层，并按各层偏移计算整体包围盒。（来源：platform/core-ui/src/ui/RowIcon.kt:94-160；platform/core-ui/src/ui/LayeredIcon.kt:374-391, 419-460）【需复核实：RowIcon/LayeredIcon 仅核读关键行】
- **Augit 映射**：用 CSS 绝对定位的兄弟节点实现分层（角标层在后、`pointer-events: none`），尺寸与偏移按 4.5 的公式换算成 `em`（相对 16px 基准）即可。【可直接实现】

### 4.7 菜单 / 工具栏 / 树 的差别

- **菜单**：全局设置 `UISettings.showIconsInMenus` 关闭时，图标三槽全部置空并打 `HIDE_ICON` 客户端属性。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/MenuItemPresentationFactory.java:26-45；platform/platform-impl/src/com/intellij/ui/popup/ActionStepBuilder.java:145-149）
- **菜单**：所有可见项按同尺寸盒缩放，因此一个弹出框不会混用 16px 与 24px 字形。（来源：platform/platform-impl/src/com/intellij/ui/popup/ActionStepBuilder.java:79-108）
- **工具栏**：不做图标尺寸同质化，默认 look 也不重缩放图标（只有头部/主工具栏 look 会按 `MainToolbar.Button.iconSize` 重缩放）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:417-428；HeaderToolbarButtonLook.kt:104-122）
- **树**：图标由渲染器决定，平台**不强制**树的图标尺寸——渲染器直接画节点/`Presentation` 提供的图标（`setIcon(fixIconIfNeeded(...))`）；最小行高来自 `JBUI.CurrentTheme.Tree.rowHeight()`（减去 `getIpad()` 与边框内距），所以节点可以更高但不能更矮；浅色主题下「选中且获得焦点」时会把图标换成深色变体，条件是 `!isDarkTheme && Registry.is("ide.project.view.change.icon.on.selection", true) && selected && hasFocus`。（来源：platform/platform-api/src/com/intellij/ide/util/treeView/NodeRenderer.java:32-101）
- **工具栏弹出框**：若按钮设置了 `setNoIconsInPopup(true)`，其弹出框里的动作 presentation 的 icon 与 hoveredIcon 会被置空。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:323-333）【需复核实：仅由代码引用定位，未逐行核读该分支】

---

## 5. DataContext 与启用条件

### 5.1 上下文即「动作可见性」的输入

- 动作从 `AnActionEvent.getData(DataKey<T>)` 取上下文；`getData` 委托给 `getDataContext()`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnActionEvent.java:227-239）
- 常用键（Augit 范围内的等价物）：`PROJECT`、`EDITOR`（当前聚焦编辑器）、`CARET`、`VIRTUAL_FILE`、`VIRTUAL_FILE_ARRAY`（多选）、`PSI_FILE`、`NAVIGATABLE`/`NAVIGATABLE_ARRAY`、`LANGUAGE`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/CommonDataKeys.java:24-132）
- 平台键：`COPY_PROVIDER`/`CUT_PROVIDER`/`PASTE_PROVIDER`/`DELETE_ELEMENT_PROVIDER`（能力提供者模式）、`TREE_EXPANDER` + `TREE_EXPANDER_HIDE_ACTIONS_IF_NO_EXPANDER`（树展开动作按是否有 expander 隐藏）、`CONTEXT_MENU_POINT`/`CONTEXT_MENU_LOCATOR`（右键菜单定位）、`MODALITY_STATE`。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/PlatformDataKeys.java:30-98）
- **`PlatformDataKeys.TREE_EXPANDER_HIDE_ACTIONS_IF_NO_EXPANDER` 是「不显示占位项」的一个现成范式**：无 expander 时直接隐藏相关动作。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/PlatformDataKeys.java:68-69）【可直接实现】

### 5.2 DataContext 的包装与派生

- `CustomizedDataContext` 可在父上下文之上叠加 provider，并把「作者明确声明无值」与「未提供」区分开：`EXPLICIT_NULL` 哨兵让查找**立即终止且返回 null**，不再往上问。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/CustomizedDataContext.java:15-18, 64-68）
- `AnActionEvent.getDataContext()` 在 `presentation.isPreferInjectedPsi()` 时返回注入（injected）上下文；`AnAction.setInjectedContext(boolean)` 控制该偏好。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnActionEvent.java:219-235, 327-333；platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnAction.java:477-489）
- **Augit 映射**：把「当前上下文」建模为一个显式的数据对象（等价于 Augit 的选中项/焦点/仓库状态快照），并支持「显式无值」以便动作区分「未选中任何东西」与「上下文不支持该查询」。【可直接实现】

### 5.3 焦点在别处时启用状态如何变化

- 工具栏的数据上下文来自**目标组件**：`setTargetComponent(JComponent)` 决定 `getToolbarDataContext()` 的取数起点；`ActionToolbar.getDataContextFor(component)` 也走同一个上下文。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/ActionToolbar.java:167-173, 211-222）
- `ActionToolbarImpl` 需要目标组件时优先用「窗口内任一获得焦点的组件」，否则回落到自身。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:1393-1410）
- 目标组件变化会触发立即更新（`setTargetComponent` 在显示中时调 `updateActionsImmediately()`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:1373-1383）
- 焦点转移本身就是一次「活动」，会推动 500 ms 轮询里的 `ActivityTracker.count` 变化，从而触发工具栏重算。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/AnAction.java:347-353；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionManagerImpl.kt:791-807）
- 对工具栏按钮，如果动作声明关注的目标不在上下文里，正确做法是 `visible=false` 而不是 `enabled=false`——因为禁用项仍会留下占位。（来源：见 2.2/2.3）
- **Augit 映射**：文件树的右键菜单在弹出瞬间读取「当前选中节点集合 + 仓库状态」求所有项的可启用性；焦点切到别的面板（例如提交视图）时，工具栏按钮应在焦点变化事件里重算，而不是等下一次轮询。【可直接实现】

### 5.4 一个真实动作的启用条件范式（VCS）

- `GitOperationActionBase` 把「仓库必须处于某状态」编码为构造参数，`update()` 只做两件事：
  ```
  e.presentation.isEnabledAndVisible = !getAffectedRepositories(e.project).isEmpty()
  if (visible && place == 合并/变基工具栏 place) presentation.icon = getMainToolbarIcon()
  ```
  并且声明 `getActionUpdateThread() = BGT`。（来源：plugins/git4idea/backend/src/actions/GitOperationActionBase.kt:20-38）
- 派生类按状态区分：Abort 有 Merge/CherryPick/Revert 三个变体，分别绑定 `MERGING` / `GRAFTING` / `REVERTING` 状态；Skip 与 Continue 属于 rebase 组并各自提供工具栏图标。（来源：plugins/git4idea/backend/src/actions/GitAbortOperationAction.kt:36-63；GitRebaseSkip.java:15-24）
- **这是 Augit「不显示无效动作」的直接样板**：Continue/Skip/Abort 的可见性来自「仓库当前处于什么 Git 操作状态」这一条单一判定；判定为空则 `isEnabledAndVisible = false`（**同时**禁用与隐藏）。【可直接实现】

---

## 6. 按钮的悬停/按下/焦点呈现

### 6.1 按钮状态枚举

```
NORMAL   = 0
POPPED   = 1    // 悬停（鼠标未按下）
PUSHED   = -1   // 被鼠标按下且指针在按钮上
SELECTED = 2    // 获得焦点，或（对可切换按钮）处于选中态
```
（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ActionButtonComponent.java:22-52）

- `ActionButton.getPopState()` 的实际优先级：**PUSHED（选中态 或 悬停+按下）→ POPPED（悬停）→ SELECTED（焦点所有者）→ NORMAL**。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:626-639）
- 通用静态映射 `ActionButtonLook.getButtonState(isEnabled, isHovered, isFocused, isPressedByMouse, isPressedByKeyboard)`：禁用→NORMAL；按下（鼠标或键盘）→PUSHED；悬停→POPPED；焦点→SELECTED；否则 NORMAL。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:164-186）
- 注意 `SELECTED` 的语义是**「焦点或选中」二者之一**，所以焦点环与 toggle 选中态在部分组件里共用同一状态值；`ActionButton` 自身则只把焦点归入 SELECTED、把 toggle 选中归入 PUSHED。（来源：ActionButtonComponent.java:36-39；ActionButton.java:626-639）
- **没有独立的 `DISABLED` 绘制状态**：禁用是通过「状态自然回落到 NORMAL（因为 hover/press 分支都要求 `isEnabled()`）+ 使用禁用图标」来表达的。边界情况：**禁用的已选中 toggle 仍会返回 PUSHED**，因此仍会画按下背景与边框。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:626-639, 543-554）
- 按钮绘制顺序固定为：背景（条件性）→ 图标 → 边框 → 下拉箭头（若满足条件）；`paintChildren` 是空实现。禁用且处于旧 Darcula（非新 UI）时会**完全跳过背景绘制**。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:217-218, 520-554）

### 6.2 背景与边框的取色

- 背景：`NORMAL` 态只在「组件显式设过背景」时才画 `getBackground()`，否则 null（不画）；`PUSHED` 用 `ActionButton.pressedBackground()`；其他（POPPED/SELECTED）用 `ActionButton.hoverBackground()`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:96-100）
- 边框：`NORMAL` 且未设背景 → 不画；`PUSHED` 用 `pressedBorder()`；其余用 `hoverBorder()`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:108-115）
- 命名色与默认值（新 UI 主题键 / 回退色）：`ActionButton.pressedBackground`→`Gray.xCF`；`ActionButton.pressedBorderColor`→`Gray.xCF`；`ActionButton.focusedBorderColor`→`JBColor(0x62b8de, 0x5eacd0)`；`ActionButton.hoverBackground`→`Gray.xDF`；`ActionButton.hoverBorderColor`→`Gray.xDF`；`ActionButton.hoverSeparatorColor`；`ActionButton.separatorColor`。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:363-390）
- 圆角与线宽：`DarculaUIUtil.BUTTON_ARC = new JBValue.UIInteger("Button.arc", 6)`；`DarculaUIUtil.LW = new JBValue.Float(1)`。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/darcula/DarculaUIUtil.java:298-301）
- 默认 look 的背景是圆角矩形（`RoundRectangle2D`，arc 取 `BUTTON_ARC`，开启抗锯齿 + `STROKE_NORMALIZE`），边框是用同一 arc 与 `LW` 画的圆角描边。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/IdeaActionButtonLook.java:23-51）
- 存在另一套方角 look：`Win10ActionButtonLook` 背景是 `fillRect`（直角），边框是 1px 内缩的「画框」（`JBInsets.removeFrom(innerRect, JBUI.insets(1))` + 奇偶填充）。**注意：该 look 在整个仓库中除自身声明外没有任何引用点，默认不生效**；把它当作"Windows 平台的默认外观"是误读。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Win10ActionButtonLook.java:32-59）
- 还有 `INPLACE_LOOK`（全部空实现，用于就地编辑类按钮）与 `SYSTEM_LOOK`（委托给 `IdeaActionButtonLook`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:23-78）
- **Augit 映射**：普通工具栏按钮 → 圆角 6px、无边框、hover 背景 `Gray.xDF`、按下背景 `Gray.xCF`；若视觉稿显示的是方角加 1px 内缩画框（Windows 经典风格），改用 `Win10ActionButtonLook` 的几何。不要两者混用。【可直接实现】

### 6.3 内距与尺寸数值

- 图标位置：`rect = 组件尺寸 − insets − iconInsets`，`x = rect.x + (rect.width − iconW)/2`，`y` 同理——**居中对齐**。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:188-198）
- 工具栏按钮内距（见 4.4）：由工具栏的 `ActionButtonBorder` 提供，默认 `({2}, {1})`；对**水平**工具栏即 `Insets(top=1, left=2, bottom=1, right=2)`，对**垂直**工具栏即 `Insets(top=2, left=1, bottom=2, right=1)`。⚠️ `Toolbar.Button.buttonInsets` / `MainToolbar.Icon.insets` 这两个主题键虽存在但**未被消费**；工具栏容器内距另取 `(5, 7)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:826, 1740-1764；platform/util/ui/src/com/intellij/util/ui/JBUI.java:1235-1267）
- 文本按钮：`ICON_TEXT_SPACE = 2`、`TEXT_ARROW_SPACE = 2`、`BUTTONS_GAP = 4`（`getMargins()` = `JBUI.insets(0, 4)`）；下拉箭头图标为平台内建的下三角，绘制位置 `x = max(iconRect.right, textRect.right) + scale(2)`、`y = textRect.y + (textRect.height − arrowH)/2 + 1`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButtonWithText.java:57-63, 149-151, 174-176, 254-258）
- 文本按钮的文字基线用 `fm.getAscent()` 绘制，且会按下划线标注助记符字符。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButtonWithText.java:251-253）
- 下拉箭头叠加（图标按钮版）：`arrowX = iconPos.x + 1 + (origIconW − arrowW)`，`arrowY = iconPos.y + 1 + (origIconH − arrowH)`——即箭头贴图标区域的**右下角**、带 1px 偏移。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:153-162）
- 预置最小按钮尺寸常量（供 `setMinimumButtonSize`）：`DEFAULT_MINIMUM_BUTTON_SIZE = JBUI.size(22, 22)`；`NAVBAR_MINIMUM_BUTTON_SIZE = JBUI.size(20, 20)`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/ActionToolbar.java:77-86）

### 6.4 键盘与鼠标交互

- 空格键等同于点击；对含下拉箭头的按钮，**下箭头键**也等同于点击（`keyReleased` 时 `getModifiersEx() == 0` 且键码为 SPACE 或 DOWN）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:164-174）
- 鼠标：`MOUSE_PRESSED` 时若跳过条件不成立且 `isEnabled()`，置 `myMouseDown = myRollover = true` 并重绘；`MOUSE_RELEASED` 时**仅当仍在按钮内（`myRollover`）**才 `performAction`（即按下后移出再松开不触发）；`skipPress = e.isMetaDown() || e.getButton() != BUTTON1`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:565-603, 621-624）
- 全局鼠标按下标记：`ourGlobalMouseDown` 用于处理「在别处按下、拖入本按钮」的情形。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:590-601, 605-609）
- 按钮默认 **不可获得焦点**，只有屏幕阅读器激活时才可聚焦（`setFocusable(ScreenReader.isActive())`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:160-162）
- 按下/悬停状态在 presentation 变化时**不会**被重置为 NORMAL；presentation 的 `PROP_ENABLED`/`PROP_ICON` 只触发 `updateIcon()` + repaint，`"selected"` 只触发 repaint。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:646-671）

### 6.5 焦点环绘制

- 默认 look 下**没有独立的焦点环颜色**：焦点只让 `getPopState()` 返回 `SELECTED`，从而背景取 `hoverBackground()`、边框取 `hoverBorder()`（默认 `Gray.xDF = RGB(223,223,223)`），即**焦点描边在视觉上等于 hover 圆角边框**（arc = `Button.arc` 默认 6，线宽 = `DarculaUIUtil.LW` 默认 1.0）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:633-635, 626-639；platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:96-115；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/IdeaActionButtonLook.java:48-51）
- **唯一会画专用焦点色的是 `FieldInplaceActionButtonLook`**（搜索框/过滤器类工具栏，需 `toolbar.setCustomButtonLook(...)` 显式启用）：`paintBorder` 里 `if (component.isFocusOwner() && component.isEnabled())` 则用 `SYSTEM_LOOK.paintLookBorder(rect, JBUI.CurrentTheme.ActionButton.focusedBorder())`，否则退回普通边框。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/FieldInplaceActionButtonLook.java:16-28）
- 焦点色值：`ActionButton.focusedBorderColor`（键同名），默认 `JBColor(0x62b8de, 0x5eacd0)`（浅/深）；另有组件级 `Component.focusedBorderColor`（`0x87AFDA / 0x466D94`）。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:358-374）
- 按钮默认**不可获得焦点**（见 6.4），所以焦点环在图标工具栏里通常是「屏幕阅读器激活时」才会出现的东西。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:160-162）
- `ActionButtonLook.getButtonState(isEnabled, isHovered, isFocused, isPressedByMouse, isPressedByKeyboard)`（禁用→NORMAL；按下→PUSHED；悬停→POPPED；焦点→SELECTED；否则 NORMAL）是给外部消费者用的公开映射，**`ActionButton` 自身不用它**。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:164-186）
- 焦点获得/失去只触发 `repaint()`，不改变 presentation，也不触发重新求值。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:175-185）
- **Augit 映射**：若要可见的焦点环，用 `:focus-visible { outline: 2px solid var(--focus) }`，色值取 `#62B8DE`（浅）/`#5EACD0`（深）；但不要声称「IntelliJ 默认就有可见焦点环」——默认态下它和 hover 边框同色。【需推断】

### 6.6 工具提示内容（动作名 + 快捷键）

- 帮助提示开启时（`UISettings.isIdeHelpTooltipEnabled()`）：标题 = presentation 文本，副标题 = 描述，快捷键 = `KeymapUtil.getFirstKeyboardShortcutText(action)`；描述只在「与标题不同」且（actionId 在平台白名单内 或 动作实现 `TooltipDescriptionProvider`）时才展示；动作还可提供 `TooltipLinkProvider` 的链接。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:488-509, 516-518）
- 帮助提示关闭时退回原生 tooltip：`setToolTipText(text != null ? text : description)`。此时有一个**确定的拼接格式**：先**删掉尾部的所有 `.`**，再在非空时追加 `" (" + 快捷键文本 + ")"`；结果为空则 tooltip 为 null。即形如「Rename (Shift+F6)」。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:389-402, 509-513）
- 帮助提示（新 UI）自身的时序/宽度：初次与重显走注册表 `ide.tooltip.initialReshowDelay`（默认 **500 ms**）、`ide.tooltip.reshowDelay`；隐藏延迟 `ide.tooltip.initialDelay.highlighter` 默认 150 ms；多行 dismiss 默认 **30 s**、普通 **10 s**；最大标题宽度 = 拥有者屏幕宽度的 **90%**（高度同样 90%），超过即切换到多行长文本路径。（来源：platform/platform-api/src/com/intellij/ide/HelpTooltip.kt:107-108, 160-165, 310-331, 468-496, 571-584, 649-657）【需复核实：HelpTooltip 未逐行核读】
- 文本按钮的 tooltip 只放描述；快捷键仅在 `SHORTCUT_SHOULD_SHOWN` 客户端属性为 true 时才附上。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButtonWithText.java:201-221; 68）
- 按钮强制 tooltip 居中：`putClientProperty(UIUtil.CENTER_TOOLTIP_DEFAULT, Boolean.TRUE)`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:187）
- 键盘 keymap 变化时，工具栏会遍历所有 `ActionButton` 调 `updateToolTipText()`，保证快捷键提示不陈旧。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ToolbarUpdater.kt:89-104）
- **Augit 映射**：`title` 属性用于名称，快捷键用 `data-shortcut` 由 JS 拼进 tooltip（`名称 (Ctrl+Shift+K)` 或两行式）；keymap/快捷键变更后必须主动刷新 tooltip。【可直接实现】

### 6.7 工具栏溢出与次级动作（New UI 的「chevron」与「齿轮」）

- **溢出提示 chevron**：当布局策略把放不下的组件标记为隐藏时（bounds 里出现 `Int.MAX_VALUE` 坐标），平台记录 `myFirstOutsideIndex` 并计算 `myAutoPopupRec`（最后一个可见边缘之后的空余区域再减去工具栏内距）；`paintComponent` 在该区域内画平台内建的 chevron 图标——水平工具栏 `x = maxX − iconW − 1`、`y` 垂直居中；垂直工具栏 `y = maxY − iconW − 1`、`x` 水平居中；鼠标悬停该区域即弹出溢出菜单。是否预留该区域由 `myReservePlaceAutoPopupIcon && !isInsideNavBar()` 决定。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:427-436, 777-802, 1413-1421, 1623-1629）
- **次级动作按钮（齿轮/更多动作）**：当工具栏的次级动作组非空时，追加一个普通 `ActionButton`（尺寸取 `NAVBAR_MINIMUM_BUTTON_SIZE` 或 `DEFAULT_MINIMUM_BUTTON_SIZE`），设置 `setNoIconsInPopup(true)`（弹出框内不显示图标）并打上 `ActionToolbar.SECONDARY_ACTION_PROPERTY`；其 tooltip 快捷键优先取 `SECONDARY_SHORTCUT` 客户端属性；可通过 `setSecondaryActionsIcon(icon, hideDropdownIcon=true)` 在模板 presentation 上打 `HIDE_DROPDOWN_ICON` 来去掉下拉箭头。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:478-492, 482-485, 1623-1647）
- 动作组按钮弹出的菜单是「显示禁用项」的弹出框（`ActionPopupOptions.showDisabled()`），并启用悬停展开子菜单、不做父边界对齐。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:300-321）
- **chip 式可选按钮**（搜索框/过滤器里的选中样式，与普通工具栏 toggle 不同）：`FieldInplaceActionButtonLook` 在非 rollover、状态为 SELECTED、按钮 enabled 时画 `SearchOption.BUTTON_SELECTED_BACKGROUND`（键 `SearchOption.selectedBackground`，默认浅色 `0xDAE4ED` / 深色 `0x5C6164`）；新 UI 下 SELECTED 的 hover/按下色分别取 `SearchOption.selectedHoveredBackground` / `selectedPressedBackground`（默认都回退到 `ActionButton.pressedBackground()`）。该 look 必须由工具栏显式启用（`toolbar.setCustomButtonLook(FieldInplaceActionButtonLook())`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/FieldInplaceActionButtonLook.java:18-64；platform/util/ui/src/com/intellij/util/ui/JBUI.java:522-528）
- 工具栏布局策略：构造时默认 `NOWRAP_STRATEGY`，非 mini 模式切换到 `AUTOLAYOUT_STRATEGY`；另有 `WRAP` / `EQUAL_SIZE_WRAP` / `COMPRESSING`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionToolbarImpl.kt:240, 1697；platform/editor-ui-api/src/com/intellij/openapi/actionSystem/toolbarLayout/ToolbarLayoutStrategy.java:13-23）【需复核实：ToolbarLayoutStrategy 未逐行核读】

### 6.8 无障碍角色

- `ActionButton` 的无障碍角色是 `PUSH_BUTTON`；无障碍名称优先取显式的 `ACCESSIBLE_NAME_PROPERTY`，否则 tooltip 文本，再否则 presentation 文本。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:684-710）
- 无障碍动作只有 1 个（点击），描述取自 `UIManager "AbstractButton.clickText"`；无障碍图标直接转发内层图标的 `AccessibleIcon`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:717-728, 761-785）【可直接实现：等价于 `role="button"` + `aria-label`】

---

## 7. 动作分组与菜单

### 7.1 分隔线规则

两处实现，规则一致：

- 组展开后处理 `removeUnnecessarySeparators(visible)`：丢弃——（a）最后一个位置的分隔线；（b）后面紧跟另一个分隔线的分隔线；（c）作为整体第一个且**文本为空**的分隔线。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:841-851）
- 菜单填充时 `filterInvisible()`：开头的无文本分隔线跳过；相邻分隔线**用后一个替换前一个**（合并，保留后者的文本）；结尾的无文本分隔线删除。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:802-817）
- 带文本的分隔线是**分区标题**：`Separator.create(text)` 在文本为空/空白时返回共享的无参实例，非空时新建带文本实例；渲染时带文本的分隔线变成 `GroupHeaderSeparator` 标签（并用 `setHideLine(first)` 隐藏最上方一条的分隔线），无文本的才是普通分隔线。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/Separator.java:23-45；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:957-992）
- 工具栏可把分隔标题当标签显示：`ActionToolbar.setShowSeparatorTitles(boolean)`（默认关）。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/ActionToolbar.java:197-201）
- 分隔线声明为 `DumbAware` + `LightEditCompatible`，且是装饰元素而非动作。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/Separator.java:18）
- **组内的子项顺序**由 `DefaultActionGroup` 决定：`Anchor.FIRST` 插入到索引 0，`Anchor.LAST` 追加到末尾，`Anchor.BEFORE`/`Anchor.AFTER` 会先进入 `myPendingActions`，直到其参照动作已存在才落位；读取时**已排序的子项在前，未解析的 pending 子项一律追加到末尾**。同一个动作被重复添加会被拒绝（LOG.error 并去重，分隔线豁免）。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/DefaultActionGroup.java:124-143, 242-264, 270-301, 569-580）【可直接实现：Augit 的菜单构建器可照搬「Anchor 定位 + 未解析项兜底到末尾 + 重复项去重」三条】

### 7.2 子菜单箭头与「组退化为单项」

- `isPopupGroup()` 的组在菜单里显示为**子菜单**；非 popup 组的子动作**内联注入**父组。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:390-402）
- `isPerformGroup()` 的组既能被子菜单展开、也能被当作普通动作执行。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:404-417）
- 子菜单的最终决定（`expandGroupChild` 里）：
  - `performOnly = isPopup && canBePerformed && SUPPRESS_SUBMENU == true`；
  - 若检查子项后 `!hasVisible`，则 `performOnly = canBePerformed && !hasVisible`——**空的可执行组退化为单个动作项，不显示箭头**；
  - 结果写进内部属性 `SUPPRESS_SUBMENU_IMPL`。
  （来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:464-496）
- `ActionUtil.SUPPRESS_SUBMENU` 的文档：「By default, a 'performable' non-empty popup action group menu item still shows a submenu. Use this key to disable the submenu and avoid children expansion on update」，普通与模板 presentation 都支持。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:132-142）
- `ActionUtil.HIDE_DROPDOWN_ICON`：关掉**工具栏按钮**上的下拉箭头叠加（**与菜单子菜单箭头无关**）；同样支持两种 presentation。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:144-154）
- **菜单子菜单箭头的实际规则**：全局 LAF 把 `Menu.arrowIcon` 装成 `DefaultMenuArrowIcon`（平台内建的菜单箭头图标，含 selected/disabled 变体），右对齐到 `viewRect.right − arrowWidth`、垂直居中于标签区域；**但在弹出菜单（右键菜单及其子菜单）里箭头被刻意去掉**——`checkArrowIcon()` 会在 `IdeaPopupMenuUI.isPartOfPopupMenu(item)` 为真时把 arrowIcon 置空。也就是说：**只有主菜单栏的子菜单保留箭头**，右键菜单/上下文菜单不画箭头。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LookAndFeelThemeAdapter.kt:58, 95-102；platform/platform-impl/src/com/intellij/ide/ui/laf/MenuArrowIcon.kt:13-33；platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:112-116, 395-398；platform/platform-impl/src/com/intellij/ide/ui/laf/intellij/IdeaPopupMenuUI.java:41-70）
- `DefaultActionGroup.createPopupGroup(...)` 设 popup=true，`createFlatGroup(...)` 设 popup=false——这是「子菜单 vs 内联」的构造侧选择。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/DefaultActionGroup.java:104-110）
- `MoreActionGroup` 是「更多」弹出组的规范实现：popup=true + `HIDE_DROPDOWN_ICON` + `isHideGroupIfEmpty=true`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/MoreActionGroup.kt:12-19）【需复核实：MoreActionGroup.kt 未逐行核读】

### 7.3 快捷键右对齐显示

- 快捷键文本由 `menuItem` 的 accelerator 提供；绘制矩形按「从右往左」定位：
  `acceleratorRect.x = viewRect.x + viewRect.width − arrowIconRect.width − (arrowIconRect.width > 0 ? menuItemGap : 0) − acceleratorRect.width`
  `acceleratorRect.y = viewRect.y + viewRect.height/2 − acceleratorRect.height/2`
  （来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:391-393）
- 快捷键文本宽度用 `SwingUtilities.computeStringWidth(keyStrokeMetrics, keyStrokeText)`，字体用 accelerator 专用字体（`g.setFont(acceleratorFont)`）。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:141, 219, 354-356）
- 加速键来源：动作显示用快捷键集的**第一个 KeyboardShortcut 的第一个 KeyStroke**；**若该 KeyStroke 是裸 Enter 则不注册**（否则用户无法选择其它菜单项）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:157-174, 53-55）
- presentation 可追加后缀：`ActionUtil.KEYBOARD_SHORTCUT_SUFFIX`，与默认快捷键文本拼接后作为显示文本。（来源：platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:160-161；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:138-145）
- Mac 简化快捷键模式下，显示文本放入客户端属性 `"accelerator.text"`（原生屏幕菜单另走 `setAcceleratorText`）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:165-169）【可直接实现：`display:flex; justify-content:space-between;` + 右列快捷键文本；Windows/Linux 用 Ctrl/Alt/Shift，Mac 用 ⌘/⌥/⇧ 映射由 Augit 自行决定（产品只面向 Windows，可只用 Ctrl/Alt/Shift）】

### 7.4 菜单项的列宽与内距

- 勾选槽列宽 = `Menu.maxGutterIconWidth` / `MenuItem.maxGutterIconWidth`，默认 **18**；文本与图标都会整体右移这个宽度（`textRect.x += myMaxGutterIconWidth; iconRect.x += myMaxGutterIconWidth`），再额外加 `menuItemGap`。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/IdeaLaf.kt:49-50；platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:376-381）
- 若当前项没有勾选图标且弹出框整体不需要图标列，`checkEmptyIcon` 会把 gutter 宽度临时降为 0（`IdeaPopupMenuUI.hideEmptyIcon(comp)`）。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:118-120）
- 勾选图标在 gutter 内居中：`checkIconRect.x += (viewRect.x + myMaxGutterIconWidth/2) − checkIcon.getIconWidth()/2`，y 按标签区域垂直居中。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:399-405）
- 次级图标（`ActionUtil.SECONDARY_ICON`）放在**文本右侧**：`secondaryIconRect.x = labelRect.x + labelRect.width + menuItemGap`，y 垂直居中。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:383-389；platform/platform-api/src/com/intellij/openapi/actionSystem/ex/ActionUtil.kt:163-165）
- 选中背景色：`Menu.selectionBackground`，回退到列表选中背景。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:102）
- **行高（新 UI 有权威公式）**：`IdeaMenuUI.patchPreferredSize` 会把每个弹出菜单项/菜单栏项的高度强制为 `JBUI.CurrentTheme.List.rowHeight() + outerInsets.height()`，其中 outerInsets 对弹出菜单取 `PopupMenu.Selection.outerInsets()`、对菜单栏项取 `Menu.Selection.outerInsets()`；非新 UI 保留 Swing 计算出的高度。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/IdeaMenuUI.java:97-106）
- **首选宽度组成**（`BegMenuItemUI.getPreferredSize`）：文本/图标联合矩形 → 有快捷键时 `+ 快捷键宽度 + 7 × defaultTextIconGap` → 有次级图标时 `+ 次级图标宽 + 2 × gap` → 需要勾选/箭头时 `+ gutter + 2 × gap + 箭头宽` → `+ 2 × gap` → `+ 项 insets`；最后**宽与高都强制为奇数**（`if (rect.width % 2 == 0) rect.width++`，高度同理）。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:418-465）
- 带文本的分隔线标签内距：新 UI 用 `JBUI.CurrentTheme.Popup.separatorLabelInsets()`，否则 `JBUI.CurrentTheme.ActionsList.cellPadding()`。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:957-967）
- 助记符下划线：文本始终用 `BasicGraphicsUtils.drawStringUnderlineCharAt` 绘制；关闭助记符时 `setDisplayedMnemonicIndex` 存 `-1`、`setMnemonic` 存 `0`；助记符开关来自 `!UISettings.disableMnemonics`（弹出菜单）；平台 LAF 下**按住 Alt 才重绘显示下划线**（`LaFMnemonicDispatcher` 设置 `isAltPressed` 并重绘所有 `displayedMnemonicIndex != -1` 的 `JLabel`/`AbstractButton`）。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:189-213；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:148-155；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenu.kt:232-238；platform/platform-impl/src/com/intellij/ide/ui/laf/LaFMnemonicDispatcher.kt:34-46, 72-87）【需复核实：LaFMnemonicDispatcher 由子项报告转述，未逐行核读】
- 菜单项行高公式所需的 `List.rowHeight()` 与 `PopupMenu.Selection.outerInsets()` / `Menu.Selection.ARC` 具体数值属于主题键，已由 `docs/intellij-platform-ui-reference.md` 的取值清单覆盖；本节只登记公式与关系。（来源：platform/platform-impl/src/com/intellij/ui/plaf/beg/IdeaMenuUI.java:75-106）

### 7.5 菜单项悬停与状态栏描述

- 菜单项获得选中路径时会把动作描述推到状态栏：`showDescriptionInStatusBar(isIncluded, component, description)`；`description` 来自 presentation。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:180-186；ActionMenu.kt:269-272）
- 菜单项执行时会记录特性使用统计（`context.menu.click.stats.<actionId>`），并在 `performAction` 里克隆 presentation 后调 `ActionUtil.performAction`——**不重跑 update**。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:262-276）
- 菜单项的 `setBorderPainted(false)`，边框由 UI 自己画。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:82-107）【可直接实现：hover 时状态栏/底部提示栏显示动作描述（Augit 可映射到状态栏文案）】

### 7.6 文本装饰钩子

- 动作文本可在显示前被统一装饰：`ActionPresentationDecorator.decorateTextIfNeeded(action, text)`；实例由平台/插件设置。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionPresentationDecorator.java:13-39）
- 菜单项与按钮在渲染文本时都走这个钩子。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionMenuItem.kt:127；ActionButtonWithText.java 的 `getText()`/`layout` 路径；ActionMenu.kt:220）【需推断：Augit 可用等价钩子做「快捷键后缀/上下文提示」装饰，但平台未定义具体格式】

---

## 8. 与 Augit 产品范围直接相关的落点

### 8.1 三栏冲突解决器的 Continue / Skip / Abort

**实证部分（IntelliJ 侧）**

- 合并对话框的底部按钮不是 `AnAction` 工具栏，而是 **Swing `Action` + `JButton`**：`MergeRequestProcessor.createButtonsPanel` 用 `DialogWrapper.createJButtonForAction(action, rootPane)` 生成按钮并用 `DialogWrapper.layoutButtonsPanel(buttons)` 排布；左侧动作组与右侧动作组分别加到 `BorderLayout.WEST/EAST`，中间是反馈工具栏。（来源：platform/diff-impl/src/com/intellij/diff/merge/MergeRequestProcessor.java:250-280）
- 三个解决动作（接受左侧、接受右侧、应用变更）是 `AbstractAction`，按 `MergeResult` 分派：`LEFT`/`RIGHT`/`RESOLVED`/`CANCEL`。（来源：platform/diff-impl/src/com/intellij/diff/merge/MergeThreesideViewer.java:301-331）
- 按钮文案是可定制的：`MergeUtil.getResolveActionTitle` 优先读 `DiffUserDataKeysEx.MERGE_ACTION_CAPTIONS`（一个 `Function<MergeResult, String>`），否则回退到内置文案：`CANCEL` → 「Save（若为迭代式解决）/ Cancel」、`LEFT` → accept left、`RIGHT` → accept right、`RESOLVED` → apply changes。（来源：platform/diff-impl/src/com/intellij/diff/merge/MergeUtil.java:38-70；DiffUserDataKeysEx.java:127）
- 另外一处在补丁应用场景里覆写了 `CANCEL` 的文案。（来源：platform/vcs-impl/src/com/intellij/openapi/vcs/changes/patch/ApplyPatchUtil.java:195）
- **启用条件**：解决动作在不允许操作时被显式禁用——rediff 开始前 `myAcceptResolveAction.setEnabled(false)`，rediff 完成后恢复 `true`；外部操作期间统一 `enableResolveActions(false/true)`（一次性控制 left/right/accept 三个）。（来源：platform/diff-impl/src/com/intellij/diff/merge/MergeThreesideViewer.java:543-552, 635-640, 1124-1128）
- **禁用态呈现**：`MergeResult` 的按钮走 `JButton` 的 Swing 默认禁用态（文字变灰），不是隐藏。因此 IntelliJ 的合并对话框里「不可用的动作」是**灰显**的，不是消失的。（来源：platform/diff-impl/src/com/intellij/diff/merge/MergeRequestProcessor.java:277-280；与 2.1 的通用规则一致）
- Git 侧的 Continue / Skip / Abort 是**动作**且走「同时禁用与隐藏」范式：`isEnabledAndVisible = !getAffectedRepositories(project).isEmpty()`，即仅当存在处于目标状态的仓库时才出现；Abort 按 `MERGING`/`GRAFTING`/`REVERTING` 分三种，Skip/Continue 属 rebase 组。（来源：plugins/git4idea/backend/src/actions/GitOperationActionBase.kt:20-38；GitAbortOperationAction.kt:36-63；GitRebaseSkip.java:15-24）

**Augit 映射（需推断）**

- `【可直接实现】` 三栏解决器的 Continue/Skip/Abort 应采用「**根据 Git 操作状态决定 `visible`，而非只决定 `enabled`**」的模型：
  - 只有 rebase 进行中才显示 Skip；只有 rebase/merge/cherry-pick/revert 进行中才显示对应 Continue/Abort；
  - 中间态（正在写入/正在刷新）→ 暂时 `disabled` 并保持可见（对应 IntelliJ 的 rediff 期间禁用）；
  - 状态未知（尚未从 git 读到状态）→ 保守地全部隐藏，或全部禁用并加 loading 指示（**需复核实**：平台对「状态尚未加载」没有统一样板，Augit 需自行定策略）。
- `【需推断】` 文案：「Continue / Skip / Abort」这一组措辞在本次核读的平台代码里**未找到硬编码来源**，它是通过 `MERGE_ACTION_CAPTIONS` 之类的定制钩子注入的；Augit 应按产品规格自行定义文案与状态映射，不应声称是从 IntelliJ 直接抄来的字符串。
- `【Swing 特有】` 合并对话框按钮的层叠/间距由 `DialogWrapper.layoutButtonsPanel` 决定，其数值未在本次范围内核读；Augit 若需精确复刻按钮条内距，需要另立一项取证（标「需复核实」）。

### 8.2 文件树右键菜单「不显示不可用的占位项」

- `【可直接实现】` 判定顺序照搬 IntelliJ 两道闸：
  1. 先按 `visible` 过滤（**不要**渲染成灰项）；
  2. 再按分隔线规则整理（去头/去尾/合并相邻）；
  3. 剩下的项里，`enabled=false` 的按产品规格决定是否保留灰显；若要「无占位项」，则应改用「等价于 `HIDE_DISABLED_CHILDREN` 的组属性」，把禁用项在过滤阶段一并剔除。
  （来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:786-819；ActionUpdater.kt:365, 452-460, 497-507）
- `【可直接实现】` 空子菜单：默认行为是「空则灰显」（`disableGroupIfEmpty` 默认 true）；要「空则消失」须显式设 `hideGroupIfEmpty=true`。（来源：platform/editor-ui-api/src/com/intellij/openapi/actionSystem/Presentation.java:424-443）
- `【可直接实现】` 菜单结构应使用「组 + 分隔线」模型，每项只携带 `enabled`/`visible` 两个独立标志，并在一次求值会话里批量算出（对应 `applyPresentationChanges` 的单批应用，避免逐项抖动）。（来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionUpdater.kt:159-173, 336-338）
- `【需推断】` Augit 的右键菜单项（如「在资源管理器中显示」「复制路径」「Git 操作子菜单」）的**具体可见条件**属于产品规格，不在 IntelliJ 源码里；平台提供的只是机制（前述规则）。

---

## 9. 待复核实清单

以下条目在本次核读中未取得完整一手证据，写入实现前需再核对：

1. ~~`ActionButton` 默认路径是否消费 `focusedBorder()`~~ **已解决**：默认 `IdeaActionButtonLook` 不消费；只有 `FieldInplaceActionButtonLook` 会画专用焦点边框（见 6.5）。（platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/FieldInplaceActionButtonLook.java:16-28）
2. ~~弹出菜单行高~~ **已解决**：新 UI 行高 = `List.rowHeight() + outerInsets.height()`（见 7.4）；但 `List.rowHeight()` / `PopupMenu.Selection.outerInsets()` / `Menu.Selection.ARC` 的**具体数值**仍需从主题键取值（由 `docs/intellij-platform-ui-reference.md` 覆盖）。（platform/platform-impl/src/com/intellij/ui/plaf/beg/IdeaMenuUI.java:75-106）
3. ~~弹出菜单子菜单箭头来源~~ **已解决**：`Menu.arrowIcon` = `DefaultMenuArrowIcon`，但在弹出菜单中 `checkArrowIcon()` 会置空（见 7.2）。（platform/platform-impl/src/com/intellij/ide/ui/laf/MenuArrowIcon.kt:13-33；platform/platform-impl/src/com/intellij/ui/plaf/beg/BegMenuItemUI.java:112-116）
4. `LayeredIcon` 的方位几何仅核读了关键行，未逐行确认。（platform/core-ui/src/ui/LayeredIcon.kt:268-326）
5. `ActionButton.setNoIconsInPopup(true)` 在弹出框内把 icon/hoveredIcon 置空的分支未逐行核读。（platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/ActionButton.java:323-333）
6. `DialogWrapper.layoutButtonsPanel` / `createJButtonForAction` 的按钮条内距与最小宽度（影响三栏解决器底部按钮条）。（platform/diff-impl/src/com/intellij/diff/merge/MergeRequestProcessor.java:277-280）
7. 「状态尚未加载完成」时 IntelliJ 的统一样板——本次未找到；Augit 的保守策略属自行决定。
8. `LaFMnemonicDispatcher` 的「按住 Alt 才显示助记符下划线」行为、`MoreActionGroup` 的实现、`ToolbarLayoutStrategy` 的策略清单、`HelpTooltip` 的时序/宽度常量均由子项报告转述，未逐行核读。（platform/platform-impl/src/com/intellij/ide/ui/laf/LaFMnemonicDispatcher.kt:34-87；platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/MoreActionGroup.kt:12-19；platform/editor-ui-api/src/com/intellij/openapi/actionSystem/toolbarLayout/ToolbarLayoutStrategy.java:13-23；platform/platform-api/src/com/intellij/ide/HelpTooltip.kt:107-657）
9. `Utils.kt` 中「弹出菜单内容构建与重建」的完整流程（`fillMenu` / `fillMenuInner` / `updateMenuItems` / `expandActionGroupImpl`）本次只核读了关键片段（786-819、695-724、1317-1338、651-682、957-992），中间部分未逐行通读。（platform/platform-impl/src/com/intellij/openapi/actionSystem/impl/Utils.kt:460-720, 900-960）

---

## 10. 分类速查表

| # | 规则 | 分类 |
| --- | --- | --- |
| 1 | `enabled`/`visible` 是两个独立布尔，默认都为 true | 可直接实现 |
| 2 | `visible=false` 才会让项消失；`enabled=false` 只灰显 | 可直接实现 |
| 3 | 「不显示无效项」= 可见性过滤 + 组级 `HIDE_DISABLED_CHILDREN` 等价属性 | 可直接实现 |
| 4 | 空组默认「灰显」（disableGroupIfEmpty=true），要消失需 hideGroupIfEmpty=true | 可直接实现 |
| 5 | 分隔线：去头（无文本）、去尾、合并相邻 | 可直接实现 |
| 6 | 弹出菜单打开时一次性批量求值；子菜单惰性求值 | 可直接实现（模型层） |
| 7 | 工具栏每 500 ms 轮询（失活 5000 ms、打字后 500 ms 内跳过），且仅在 activity 变化时 | 可直接实现 |
| 8 | 用户输入取消在途更新 | 可直接实现 |
| 9 | 按钮状态机 NORMAL/POPPED/PUSHED/SELECTED，选中态→PUSHED | 可直接实现 |
| 10 | 圆角 6、线宽 1、hover `Gray.xDF`、按下 `Gray.xCF` | 可直接实现 |
| 11 | 普通工具栏按钮 22×22、内距由 `ActionButtonBorder({2},{1})` 决定（水平工具栏 = 上下 1 / 左右 2） | 可直接实现 |
| 12 | 文本按钮：图标-文字间距 2、箭头间距 2、外边距 4 | 可直接实现 |
| 13 | 下拉箭头贴图标右下角（+1px 偏移） | 可直接实现 |
| 14 | 菜单勾选槽宽 18，次级图标在文本右侧，快捷键右对齐，行高 = List.rowHeight + outerInsets | 可直接实现 |
| 15 | 禁用图标用滤镜生成（灰化 33/-35/100 或半透明 0/0/30） | 可直接实现 |
| 16 | 图标状态顺序：工具栏「禁用→悬停→普通」，菜单「禁用→选中→普通」 | 可直接实现 |
| 17 | Toggle 在菜单=勾选标记（或 PoppedIcon 高亮底），在工具栏=按下背景 | 可直接实现 |
| 18 | `aria-pressed`/`aria-checked` 对应 CHECKED，键盘焦点对应 FOCUSED，下拉对应 EXPANDABLE | 可直接实现 |
| 19 | tooltip = 名称 + 快捷键（经典模式：去掉尾部 '.' 后追加 ` (快捷键)`）；keymap 变化要刷新 | 可直接实现 |
| 20 | `AnAction.update()` 与 `Presentation` 的模板/克隆关系、模板态断言 | Swing 特有 |
| 21 | `getActionUpdateThread()` BGT/EDT 与读锁包装（`isRWLockRequired`） | Swing 特有 |
| 22 | `ActionUpdateThreadAware.Recursive` 强制覆盖线程声明 | Swing 特有 |
| 23 | `JMenuItem` 的 `getPressedIcon()`、`JBCheckBoxMenuItem` 模型、`MenuSelectionManager` | Swing 特有 |
| 24 | `IconLoader`/`CachedImageIcon` 的滤镜缓存（1024 条 / 10 分钟） | Swing 特有 |
| 25 | `ActionButtonLook`/`IdeaActionButtonLook` 的 look 继承体系（`Win10ActionButtonLook` 是死代码） | Swing 特有 |
| 26 | `IconDesigner` 分层 DSL（层序即绘制序）与 `IconUnits` dp/px 区分 | 需推断（Augit 用 CSS 分层等价） |
| 27 | 角标默认右上角（平台无「右下角」全局约定） | 需推断 |
| 28 | 焦点环是否可见、色值取哪个（默认 = hover 边框；仅 FieldInplace look 用专用焦点色） | 需推断 |
| 29 | 菜单行高公式已确认；`List.rowHeight()` / `PopupMenu.Selection.outerInsets()` 的具体数值需从主题取值 | 需复核实 |
| 30 | 三栏解决器 Continue/Skip/Abort 的具体状态映射与文案 | 需推断 |
| 31 | 「Git 状态尚未加载」时的保守策略 | 需推断 |
| 32 | 右键菜单全空时：IntelliJ 补 `EMPTY_MENU_FILLER` 占位项；Augit 按规格应不弹出 | 需推断（有意的产品偏离） |
| 33 | 弹出菜单子菜单不画箭头（仅主菜单栏保留）；`HIDE_DROPDOWN_ICON` 只影响工具栏按钮 | 可直接实现 |
| 34 | 组内顺序：Anchor.FIRST/LAST/BEFORE/AFTER，未解析项追加末尾，重复项去重 | 可直接实现 |
