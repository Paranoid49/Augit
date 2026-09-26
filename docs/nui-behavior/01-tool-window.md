# 01 工具窗口与主窗口框架

> 证据来源：IntelliJ 社区版源码克隆 `/mnt/d/github/intellij-community`（commit `576e328`）。
> 路径均为相对该仓库根目录。全文为收集到的**规则**，不复制源码实现。
> 每条规则后以 `（来源：路径:行号）` 标注出处。

## 本节要点

1. **New UI 只有 LEFT / RIGHT / BOTTOM 三条 stripe**：TOP 锚点被禁用（注册到 TOP 会被强制改为 LEFT）；未安装 `ToolWindowStripeExtension` 时 BOTTOM 的按钮并入左/右 stripe 底部的“分屏组”（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:1085、platform/platform-impl/src/com/intellij/toolWindow/ToolWindowPaneNewButtonManager.kt:131）。
2. **默认权重 `weight=0.33`、`sideWeight=0.5`、`isSplit=false`、`isVisible=false`、`isShowStripeButton=true`、`contentUiType=TABBED`、`type=DOCKED`、`anchor=LEFT`**（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:96-118）。
3. **左/右两条侧栏内部的排列由设置决定**：`leftHorizontalSplit`/`rightHorizontalSplit` 为 false（默认）时两个工具窗口上下堆叠；为 true 时为左右并排，此时 `isUltrawideLayout()` 为真，两个窗口的 weight 相加后作为“整条分隔条”的宽度权重（来源：platform/ide-core/src/com/intellij/openapi/wm/ToolWindowAnchor.java:56、platform/platform-impl/src/com/intellij/toolWindow/ToolWindowPane.kt:719、platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:1402）。
4. **stripe 按钮几何**：按钮 40×40、图标 20、图标内距 5、圆角 12（compact 模式 8）；stripe 宽度可拖拽，范围 40–100（compact 最小 33），显示工具窗口名称时默认 59 宽（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1317、platform/platform-impl/src/com/intellij/toolWindow/ResizeStripeManager.kt:138）。
5. **header 高 41**（`ToolWindow.Header.height`），标题左右内距 `(0,12,0,16)`、工具栏左右内距 `(0,12,0,8)`；New UI 下 header 图标默认只在鼠标悬停或窗口激活时出现（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1173、platform/platform-impl/src/com/intellij/toolWindow/InternalDecoratorImpl.kt:920）。

标注约定：

- **【可直接实现】**：HTML/CSS/C# 能表达的静态取值或纯逻辑。
- **【Swing 特有】**：依赖 Swing 组件模型（JLayeredPane、Splitter、GroupLayout、ActionToolbar 等），不能照搬，只能借鉴行为语义。
- **【需推断】**：由我推导但源码无直接证据，实现前需复核。

---

## 1. 布局模型

### 1.1 `ToolWindowDescriptor` 全部默认值

`ToolWindowDescriptor` 是布局的序列化单元（New API 的默认布局与 `window.layouts.xml` 都记录它）。

| 字段 | 默认值 | 可变 | 说明 |
| --- | --- | --- | --- |
| `id` | 无 | 否 | 工具窗口 ID（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:97） |
| `order` | `-1` | 是 | stripe 内序号；`-1` 表示尚未分配（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:98） |
| `paneId` | `WINDOW_INFO_DEFAULT_TOOL_WINDOW_PANE_ID` | 否 | 所属 pane（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:100） |
| `anchor` | `ToolWindowAnchor.LEFT` | 是 | 默认锚在左侧（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:101） |
| `isAutoHide` | `false` | 否 | 见 §4.5（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:102） |
| `floatingBounds` | `null` | 否 | 浮动窗口界限（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:103） |
| `isMaximized` | `false` | 否 | 仅 WINDOWED 模式有意义（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:104） |
| `type` | `ToolWindowType.DOCKED` | 否 | 当前视图模式（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:106） |
| `internalType` | `ToolWindowType.DOCKED` | 否 | 内部（docked/sliding）模式快照（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:107、platform/platform-impl/src/com/intellij/openapi/wm/impl/WindowInfoImpl.kt:66） |
| `contentUiType` | `ToolWindowContentUiType.TABBED` | 是 | 见 §6.3（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:108） |
| `isActiveOnStart` | `false` | 否 | 启动时激活（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:110） |
| `isVisible` | `false` | 是 | **默认全部不可见**（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:111） |
| `isShowStripeButton` | `true` | 否 | 默认显示 stripe 按钮（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:112） |
| `weight` | `0.33f` | 是 | 占框架比例（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:114） |
| `sideWeight` | `0.5f` | 否 | 同锚点分屏时占分隔条比例（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:115） |
| `isSplit` | `false` | 否 | 是否为“分屏组”窗口（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:117） |

- **【可直接实现】** 与 `WindowInfoImpl` 的运行期默认值一致：`DEFAULT_WEIGHT = 0.33f`、`sideWeight = 0.5f`、`anchor = LEFT`、`type = DOCKED`、`isVisible = false`、`isShowStripeButton = true`、`isSplit = false`、`order = -1`、`contentUiType = TABBED`，且 `weight`/`sideWeight` 读写时都被夹在 `0f..1f`（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/WindowInfoImpl.kt:35、:38-100）。
- **【可直接实现】** `PREVIEW` 这个 ID 的窗口永远不能是 DOCKED，会被强制为 SLIDING（来源：WindowInfoImpl.kt:132-134）。
- **【可直接实现】** 反序列化后：可见但“不允许启动激活”（EP 上 `doNotActivateOnStart`）的窗口会被置为不可见（来源：WindowInfoImpl.kt:116-122、:165-174）。
- **【需推断】** 由构建器产出的默认布局走的是 `DefaultToolWindowDescriptorBuilderImpl`，其 `weight` 默认取 `WindowInfoImpl.DEFAULT_WEIGHT`（=0.33），与 `ToolWindowDescriptor` 一致（来源：platform/platform-impl/src/com/intellij/toolWindow/defaultToolWindowlayoutProvider.kt:305-312）。

### 1.2 `ToolWindowAnchor` 与两个判定式

- 只有 4 个值：`TOP / LEFT / BOTTOM / RIGHT`，序列化文本为 `"top"/"left"/"bottom"/"right"`；`VALUES` 顺序固定为 TOP、LEFT、BOTTOM、RIGHT（来源：platform/ide-core/src/com/intellij/openapi/wm/ToolWindowAnchor.java:28-31、:43-47、:80-89）。
- `isHorizontal()` = `TOP || BOTTOM`（来源：ToolWindowAnchor.java:52-54）。**【可直接实现】**
- `isSplitVertically()`：
  - `LEFT && !UISettings.leftHorizontalSplit`，或
  - `RIGHT && !UISettings.rightHorizontalSplit`（来源：ToolWindowAnchor.java:56-59）。
  - 即 **默认（两个开关都是 false）= true**：左/右两条侧栏内部是“上下堆叠”。
- `isUltrawideLayout()` = `!isHorizontal && !isSplitVertically`（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:93）。**只有把左/右“水平分屏”开关打开（`leftHorizontalSplit`/`rightHorizontalSplit = true`）时，左/右锚点才是 ultrawide 布局**。**【可直接实现】**
- 两个开关是普通用户设置，跟随主题 roamed state，默认 false（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:99-101，platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:147-149）。

> ⚠️ **容易搞错**：`isSplitVertically == true` 时 `Splitter.orientation` 也为 true，表示“上下堆叠”；`isUltrawideLayout()` 为真才是“左右并排”。不要按字面把 `isSplitVertically` 理解成“垂直方向排布两个窗口”。

### 1.3 默认布局 V1 与 V2 的完整差异

V1 = Classic UI 默认布局，V2 = New UI 默认布局；两者由不同的扩展方法构建（来源：platform/platform-impl/src/com/intellij/toolWindow/defaultToolWindowlayoutProvider.kt:29-30、:173-185）。

| 锚点 | V1（`addPlatformDefaultsV1`） | V2（`addPlatformDefaultsV2`） |
| --- | --- | --- |
| TOP | 抛 `UnsupportedOperationException("The top stripe is not supported")` | 同左 |
| LEFT | `Project`（weight 0.25，COMBO） | `Project`（weight 0.25，COMBO）、`Commit`（0.25）、`Structure`（0.25，**isSplit=true**） |
| RIGHT | `Notifications`（0.25） | `Notifications`（0.25，**COMBO**）、`AIAssistant`（0.25）、`Database`（0.25）、`Gradle`（0.25）、`Maven`（0.25） |
| BOTTOM | `Version Control`、`Find`、`Run`、`Debug`（0.4）、`Inspection`（0.4） | `Version Control`、`Problems`、`Problems View`、`Terminal`、`Services` |

（来源：defaultToolWindowlayoutProvider.kt:232-288）

- **【可直接实现】** V2 相对 V1 的实质差异：左栏新增 `Commit` 与“分屏组”`Structure`；右栏新增 `Notifications` 的 COMBO 形态以及插件窗；底部把 `Find/Run/Debug/Inspection` 换成 `Problems / Problems View / Terminal / Services`。
- **【可直接实现】** V1/V2 都**没有显式设置 `isVisible`**（默认 false）、`isShowStripeButton`（默认 true）、`isSplit`（除 V2 的 Structure 外都是 false）、`sideWeight`（默认 0.5）。因此默认状态是：所有工具窗口都有 stripe 按钮、都不可见。
- **【可直接实现】** 构建默认布局时 `order` 按 LEFT → RIGHT → BOTTOM 的遍历顺序、在每个 stripe 内部从 0 递增重排（来源：defaultToolWindowlayoutProvider.kt:202-218）。
- **【可直接实现·与 Augit 相关】** V2 的 LEFT 顺序即 `Project(0) → Commit(1) → Structure(2, split)`；BOTTOM 顺序即 `Version Control(0) → Problems(1) → Problems View(2) → Terminal(3) → Services(4)`。Augit 的“项目/提交/搜索”与“终端/Git 历史”可据此对齐编号与分屏分组。
- **【需推断】** `Problems`（Auto-Build）与 `Problems View` 是注释里点名的两个不同 ID（来源：defaultToolWindowlayoutProvider.kt:280-283）；Augit 不含构建工具，可忽略这两个条目。

### 1.4 布局持久化与归一化

- **【可直接实现】** 整个布局存为 `DesktopLayout`：`id → WindowInfoImpl` 映射 + 四个锚点的 `unified_weights`（top/left/bottom/right，默认均为 0.33）（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/DesktopLayout.kt:20-23、platform/platform-impl/src/com/intellij/openapi/wm/impl/UnifiedToolWindowWeights.kt:20-23）。
- **【可直接实现】** New UI 写 `layoutV2` 标签、Classic 写 `layout` 标签；读盘时按当前 UI 决定哪个是“当前布局”、哪个进 `oldLayout`（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerState.kt:69-73、:100-122）。
- **【可直接实现】** 只有当**没有任何布局被调度**时才调用 `noStateLoaded()`（空元素不计）（来源：ToolWindowManagerState.kt:92-145）。
- **【可直接实现】** `recentWindows` 与 `moreButton`（More 按钮所在侧，默认 LEFT）也持久化在其中（来源：ToolWindowManagerState.kt:60、:79-88、:126-134）。
- **【可直接实现】** 全局默认布局单独存于应用级 `ToolWindowLayout`（`window.layouts.xml`），有“具名布局档案”概念：`activeLayoutName`、`layouts: name → {v1, v2, unifiedWeights}`；默认档案名 `Custom`，工厂默认名为空串 `""`；对空名档案写入会被改名为 `Custom`（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowDefaultLayoutManager.kt:38-39、:66-78、:138-141）。
- **【可直接实现】** 从 2022.3 的旧结构（只有一个 v1/v2 列表）迁移：若 `layouts` 为空而 `v1`/`v2` 非空，则把两者装进名为 `Custom` 的档案（来源：ToolWindowDefaultLayoutManager.kt:111-123）。
- **【可直接实现】** 档案缺项时回退到“按当前 UI 现场构建的默认布局”；`unifiedWeights` 缺项回退为四锚点 0.33（来源：ToolWindowDefaultLayoutManager.kt:145-161、:214、:306-312）。
- **【可直接实现】** stripe 内序号归一化规则：先按锚点权重 `TOP=1 < LEFT=2 < BOTTOM=3 < RIGHT=4` 排序，再按 `order` 排序；同一锚点内从 0 重新编号，且 `order == -1` 的窗口保持 `-1`（来源：DesktopLayout.kt:180-213）。
- **【可直接实现】** `setAnchor` 且给了显式 order 时，目标 stripe 中 `order >= 新order` 的窗口会整体右移 1；用 `-1` 表示“排到最后”（来源：DesktopLayout.kt:75-101）。
- **【Swings 特有】** `DesktopLayout.create()` 允许内容工厂自带锚点覆盖注册时的锚点（来源：DesktopLayout.kt:42-55）。

### 1.5 布局档案（layout profile）

“档案”有两层含义，注意区分：

**(a) 具名布局档案**（上节）：应用级、用户可切换/重命名/删除的布局集合。**【可直接实现】**

**(b) 项目框架档案（profile）**：按“框架类型 ID”（`projectFrameTypeId`）决定该框架首次打开时用什么布局。**【可直接实现】**

- **【可直接实现】** 项目框架档案来源有两个，按固定顺序求值：
  1. `ProjectFrameToolWindowLayoutService` 读取 `com.intellij.projectFrameToolWindowLayout` 扩展点的 bean（按 `id` 匹配 profileId）；
  2. 再依次询问 `com.intellij.toolWindowLayoutProfileProvider` 扩展点。
  第一个非空结果胜出；之后若还有 provider 返回非空，只记录错误日志并忽略（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowLayoutProfileProvider.kt:79-108、platform/platform-impl/src/com/intellij/toolWindow/ProjectFrameToolWindowLayout.kt:107-163）。
- **【可直接实现】** 档案 bean 的字段：`id`、`applyMode`（`seedOnly`/`forceOnce`，缺省 `seedOnly`）、`migrationVersion`（缺省 0，读取时 `coerceAtLeast(0)`）、`toolWindows[]`；每个工具窗口条目可声明 `id`、`register`、`anchor`、`visible`、`showStripeButton`、`weight`、`contentUiType`、`split`、`sideWeight`，全部可选（来源：ProjectFrameToolWindowLayout.kt:24-100）。
- **【可直接实现】** 两种应用模式：
  - `SEED_ONLY`（默认）：只在**没有任何已保存的 per-project 布局**时应用（来源：ToolWindowLayoutProfileProvider.kt:38-43）。
  - `FORCE_ONCE`：对给定 `migrationVersion` **强制应用一次**（来源：ToolWindowLayoutProfileProvider.kt:44-48）。
- **【可直接实现】** FORCE_ONCE 的迁移去重：用 `PropertiesComponent` 的整数键 `toolwindow.layout.profile.migration.<profileId>` 记录已应用版本；`appliedVersion >= migrationVersion` 就跳过；`migrationVersion <= 0` 直接不应用；应用后写回当前版本（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowLayoutProfileMigrationHelper.kt:45-64）。
  - 这是仓库里 `ToolWindowLayoutProfileMigrationHelper` 的**全部**迁移逻辑，不存在按“档案种类”分支的多套迁移。
- **【可直接实现】** 档案布局是以**全局默认布局的拷贝**为基底，再按工具窗口条目逐项覆盖：无布局属性的条目跳过；`register=false` 的条目直接从布局里删除（该窗口**不再注册**）；需要重排时按同锚点最大 order+1 追加（来源：ProjectFrameToolWindowLayout.kt:165-206、:209-223）。
- **【可直接实现】** `register=false` 的 ID 集合另有独立用途：它会让该工具窗口**根本不注册**（`isToolWindowRegistrationSuppressed`），EP 增删监听也会跳过（来源：ProjectFrameToolWindowLayout.kt:124-144、platform/platform-impl/src/com/intellij/toolWindow/ToolWindowSetInitializer.kt:226-244、:291-306）。
- **【可直接实现】** profileId 由 `ProjectFrameTypeService.getToolWindowLayoutProfileId(frameTypeId)` 给出；未声明或 ID 空白时为 `null`，此时所有档案逻辑短路（来源：platform/project-frame/src/com/intellij/openapi/wm/ex/ProjectFrameType.kt:112-114、:83-89）。
- **【可直接实现】** 应用时机（严格顺序）：
  1. `doInit` 里先解析档案（此时才知道 `projectFrameTypeId`）；
  2. **若 `noStateLoaded`（没有已保存布局）** → 用档案布局（无档案则用全局默认布局）调度 `setLayout`；
  3. **再**执行 `applyProjectFrameLayoutPolicy`，`FORCE_ONCE` 若命中则覆盖式地把档案布局再调度一次；
  4. 之后才建立默认 pane 并 `initUi` 注册并布局工具窗口，`ToolWindowSetInitializer.scheduleSetLayout` 在初始化完成前只把布局暂存到 `pendingLayout`（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:423-436、:565-568、platform/platform-impl/src/com/intellij/toolWindow/ToolWindowSetInitializer.kt:78-96、:125-171）。
- **【需推断】** 第 3 步的覆盖能生效，是因为 `scheduleSetLayout` 在未初始化时用 `pendingLayout.set(...)` 覆盖同一个原子引用；即 `FORCE_ONCE` 优先于默认布局。

### 1.6 布局批量应用的 3 遍式时序

`setLayout` 分三遍执行，顺序对实现很关键（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:1360-1438）：

1. **第 1 遍（可见性与模式）**：`isSplit` 或“内部/外部模式”变化的可见窗口先临时隐藏再重新显示；`type` 变化的窗口先保存状态并移除装饰器，若新模式可见则稍后重显；`old.visible=false && new.visible=true` 的窗口加入待显示集合，统一 `doShowWindow`。
2. **第 2 遍（尺寸）**：只处理 `new.isVisible && new.isDocked` 的窗口。
   - `weight` 变化 → 调 `setWeight(anchor, weight)`；**若同锚点另有可见可停靠窗口且 `anchor.isUltrawideLayout()`，则把对方 weight 加进来**（因为并排时两人共用一条分隔条）；
   - `sideWeight` 变化 → 调 `setSideWeight(toolWindow, sideWeight)`。
3. **第 3 遍（重绘）**：对每个受影响的 pane 依次 `buttonManager.revalidateNotEmptyStripes()`、`validate()`、`repaint()`，然后 `activateEditorComponent()`，最后统一 revalidate/repaint 根 pane，并发出 `SetLayout` 事件。

**【Swing 特有】** 三遍式是因为 Swing 的 validate/repaint 与组件树增删耦合；Web 实现只需“先算最终可见集合与尺寸，再一次性渲染”即可获得同样结果。

---

## 2. wideScreen 与 ultrawide

### 2.1 `wideScreenSupport`（宽屏布局，默认 false）

`ToolWindowPane` 内部只有两个 `ThreeComponentsSplitter`：`verticalSplitter`（承载 TOP/BOTTOM）与 `horizontalSplitter`（承载 LEFT/RIGHT）。**【Swing 特有】**

- `wideScreenSupport = false`（默认）：`verticalSplitter` 是外层，`horizontalSplitter` 作为其 inner 组件 → 左右侧栏被夹在顶/底栏之间（经典布局）。
- `wideScreenSupport = true`：`horizontalSplitter` 是外层，`verticalSplitter` 作为 inner 组件 → 左右侧栏贯通整个窗口高度。

（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowPane.kt:152-193）

- **【可直接实现】** 切换时保留文档组件（编辑器区），把旧外层从 layered pane 移除、装入新外层（来源：ToolWindowPane.kt:502-517）。
- **【可直接实现】** 文档区永远放在 **inner** 组件上：`isWideScreen ? verticalSplitter : horizontalSplitter` 的 innerComponent（来源：ToolWindowPane.kt:412-420、:184-189）。
- **【可直接实现·几何映射】** 锚点到槽位的映射（同一映射同时用于取放组件）：
  - TOP → 垂直分隔条的“首组件”（上）
  - LEFT → 水平分隔条的“首组件”（左）
  - BOTTOM → 垂直分隔条的“尾组件”（下）
  - RIGHT → 水平分隔条的“尾组件”（右）
  （来源：ToolWindowPane.kt:312-321、:399-410）

### 2.2 `isUltrawideLayout()` 的判定

见 §1.2：`isUltrawideLayout() = !isHorizontal && !isSplitVertically`（来源：toolwindow.kt:93）。展开为：

| 锚点 | `*HorizontalSplit=false`（默认） | `*HorizontalSplit=true` |
| --- | --- | --- |
| TOP / BOTTOM | false | false（`isHorizontal` 恒为真） |
| LEFT | **false**（`isSplitVertically=true` → 上下堆叠） | **true**（左右并排） |
| RIGHT | **false** | **true** |

### 2.3 ultrawide 为真/假时的实际后果

**(A) 什么时候才启用**：只有同锚点、同 pane 上出现**第二个可见可停靠窗口**并被合到同一分隔条时才会走到“并排/堆叠”的权重计算（`addAndSplitDockedComponentCmd`）；单窗口时只走 `normalizeWeight(info.weight)`（来源：ToolWindowPane.kt:632-786、:770-777）。

**(B) 分隔条比例（proportion）的分配**（来源：ToolWindowPane.kt:715-769）：

- ultrawide（`isSideBySideSplit == true`）：
  - 先取“已存在窗口”的原尺寸并回写它的 weight：`oldWeight = 该组件实际宽 / 根 pane 宽`（经 §5.1 的半像素补偿）；
  - `proportion = oldWeight / (oldWeight + newWeight)`（新窗口 `isSplit=false` 时反过来：`newWeight / (newWeight + oldWeight)`）；
  - 合并后锚点权重 `newWeight = normalizeWeight(oldWeight + info.weight)`，并写回 `layoutState.setUnifiedAnchorWeight(anchor, newWeight)`。
  - → 并排时**两个窗口平分这条分隔条，谁的 weight 大谁宽**；分隔条总宽 = 两者之和。
- 非 ultrawide（上下堆叠）：
  - `proportion = 已保存的 sideWeight 比例`（`state.getPreferredSplitProportion(id, sideWeight_a/(sideWeight_a+sideWeight_b))`，若无保存值就用 sideWeight 归一）；
  - 锚点 weight 取“先来的那个窗口”的 weight（`normalizeWeight(oldWeight)` 或 `normalizeWeight(info.weight)`），**不求和**。
- 判断“谁先谁后”依赖 `info.isSplit`：新窗口 `isSplit=true` 时旧窗口为 first、新窗口为 second；否则新窗口为 first（来源：ToolWindowPane.kt:729-768）。

**(C) 统一权重（unified weight）**（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:606-670、:1400-1411）：

- 激活任意工具窗口时，若“独立尺寸记忆”关闭（New UI 看 `rememberSizeForEachToolWindowNewUI`，默认 **false**；Classic 看 `rememberSizeForEachToolWindowOldUI`，默认 true），则该窗口的 `weight` 被覆盖为**所属锚点的统一权重**。
- 例外：ultrawide 锚点上，如果同锚点还有另一个可见可停靠且 `isSplit` 状态**不同**的窗口并排显示，则**不使用**统一权重（因为并排布局里每个窗口的 weight 代表自己那一列，不是整条分隔条）。
- 于是 New UI 默认行为是：**同一条侧栏上所有（非并排）工具窗口共享同一个宽度**，切换窗口不会改变侧栏宽度。
- **【需推断】** 拖动分隔条后写入的是 `unifiedAnchorWeight[anchor]`（见 (D)），所以“统一”是同侧同锚点级别的，不区分单窗口。

**(D) 显示/移除时的传递**：

- 移除一侧窗口时，另一个窗口的 weight 用实际尺寸重算并写回，同时写统一权重，再 `setComponent` 换成单组件（来源：ToolWindowPane.kt:261-302）。
- 拖动分隔条结束（`movedOrResized`）时：`info.weight = 实际尺寸比例`，`setUnifiedAnchorWeight(anchor, 停靠区整体尺寸比例)`（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerDecorators.kt:163-190）。

---

## 3. 工具窗口按钮与 stripe

### 3.1 结构

- **【可直接实现】** 一个 pane 由 `ToolWindowPaneNewButtonManager` 装配成 `BorderLayout`：中心 = pane，西 = 左工具栏，东 = 右工具栏，北/南 = 顶/底工具栏（**只有存在 `ToolWindowStripeExtension` 时才创建顶/底**）（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowPaneNewButtonManager.kt:25-73）。
- **【可直接实现】** 左侧工具栏含两个 stripe：`topStripe(anchor=LEFT)` 与 `bottomStripe(anchor=BOTTOM)`；右侧工具栏含 `topStripe(anchor=RIGHT)` 与 `bottomStripe(anchor=BOTTOM, split=true)`（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowLeftToolbar.kt:11-12、platform/platform-impl/src/com/intellij/toolWindow/ToolWindowRightToolbar.kt:11-12）。
- **【可直接实现】** BOTTOM 锚点工具窗口的按钮落位：`isSplit=false` → 左工具栏底部分组；`isSplit=true` → 右工具栏底部分组（来源：ToolWindowPaneNewButtonManager.kt:137-144、:227-239）。
- **【可直接实现】** 顶/底锚点（需扩展）→ 顶/底水平工具栏；无扩展时 `getStripeFor(TOP)`、`getStripeFor(BOTTOM, isSplit=null)` 会抛异常（来源：ToolWindowPaneNewButtonManager.kt:131-157、:241-250）。
- **【可直接实现】** New UI 无扩展时**没有 TOP stripe**：注册到 TOP 的窗口会被改成 LEFT（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:1085-1088）。
- **【可直接实现】** 工具栏描边：左栏右侧 1px 线、右栏左侧 1px 线，颜色 `ToolWindow.Stripe.borderColor`（默认 `JBColor.border()`）；内部顶部还有一条 `MainToolbar.borderColor` 顶线；背景统一 `ToolWindow.stripeBackground()`（来源：ToolWindowLeftToolbar.kt:28、ToolWindowRightToolbar.kt:27、platform/platform-impl/src/com/intellij/toolWindow/ToolWindowToolbar.kt:58-66、platform/util/ui/src/com/intellij/util/ui/JBUI.java:1089-1091）。
- **【可直接实现】** 上下两个分组之间的分隔线（New UI 左/右竖栏、且无扩展时使用）：首选尺寸 32×11（逻辑像素），实际绘制为水平居中、宽 24、高 1 的线段；颜色 `ToolWindow.Stripe.separatorColor`，拖拽中换成 `ToolWindow.Stripe.DragAndDrop.separatorColor`（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/StripeButtonSeparator.kt:22-38、JBUI.java:1151-1154、platform/platform-resources/src/themes/expUI/expUI_light.theme.json:747-752、:735-746）。

### 3.2 按钮几何（`StripeToolbar.Button`）

默认值（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1317-1363）：

| 项 | 键 | 默认 |
| --- | --- | --- |
| 按钮尺寸 | `StripeToolbar.Button.size` | 40×40 |
| 图标尺寸 | `StripeToolbar.Button.iconSize` | 20 |
| 图标内距 | `StripeToolbar.Button.{left,right}StripeIcon[WithName].padding` | 5（四边） |
| 文本横向偏移 | `StripeToolbar.Button.{left,right}StripeTextOffset` | 0 |
| 圆角 | `Button.ToolWindow.arc` | 非 compact 12，compact 8 |

- **【可直接实现】** 默认 Light/Dark（expUI）主题**不覆盖**上述任何键，因此 40/20/5/12 就是默认外观；`Islands` 主题覆盖为尺寸 `37,40`、左右图标内距 `5,5,5,2` / `5,2,5,5`、带名称时 `4,6,4,2` / `4,2,4,6`、文本偏移 `+2` / `-2`（来源：platform/platform-resources/src/themes/islands/ManyIslandsLight.theme.json:1160-1175、platform/platform-resources/src/themes/islands/ManyIslandsDark.theme.json:1192-1207）；`Darcula` 只覆盖 compact：尺寸 32×32、图标 16、内距 4/3（来源：platform/platform-resources/src/themes/darcula.theme.json:836-849）。
- **【需复核实】** PyCharm 2026.2.1 默认主题是 “Light/Dark(expUI)” 还是 “Islands”；若是 Islands，按钮几何要按上表切换到 37×40 与不对称内距。
- **【可直接实现】** 背景“药丸”的绘制矩形 = 组件矩形 **减去组件 insets 再减去图标内距**（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/SquareStripeButtonLook.kt:62-71）。
- **【可直接实现】** 图标居中于**同一内距矩形**内；带角标（`HoledIcon`）时先按原图标居中，再减去角标额外 insets，避免角标出现/消失时图标跳动（来源：SquareStripeButtonLook.kt:111-130、:161-165）。
- **【可直接实现】** 图标按 `StripeToolbar.Button.iconSize` 缩放；缺失图标用 `AllIcons.Toolbar.Unknown`（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/SquareStripeButton.kt:108-116、:341-363）。
- **【可直接实现】** More 按钮使用同一尺寸函数（`getStripeToolbarButtonSize(moreButton=true)`）（来源：platform/platform-impl/src/com/intellij/toolWindow/MoreSquareStripeButton.kt:39-42）。
- **【可直接实现】** 显示名称时按钮高度增加“图标与文字间隙 3 + 字号 TINY 的单行高度 × 行数”；名称按**第一个空格拆成最多 2 行**，取 `stripeShortTitleProvider`（若有）否则 `stripeTitleProvider`（来源：SquareStripeButton.kt:217-222、:315-329）。
- **【可直接实现】** 名称超出可用宽度时右侧叠一条宽 3 的渐隐（来源：SquareStripeButton.kt:274-305）。
- **【可直接实现·Augit 对照】** Augit 当前 `.rail-button` 为 32×32、圆角 7、下间距 4、rail 列宽 42（来源：web/src/mockup.css:2278-2293）。这与 IntelliJ 的 **compact** 档（32×32、圆角 8）接近，而不是默认 40×40/圆角 12；若要 100% 复原，按钮应为 40×40、图标 20、圆角 12。**【需推断】**

### 3.3 按钮状态与配色

状态判定（`SquareStripeButtonLook.getState`，来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/SquareStripeButtonLook.kt:73-85）：

| 条件 | 状态 |
| --- | --- |
| 工具窗口**激活**（`toolWindow.isActive`） | `SELECTED` |
| 否则窗口**可见** | `PUSHED` |
| 否则**未悬停** | `NORMAL` |
| 否则（悬停/按下） | 回落 `button.popState`（悬停 = `POPPED`，按下 = `PUSHED`；来源：platform/platform-impl/src/com/intellij/openapi/actionSystem/ex/ActionButtonLook.java:130-133、:166-186） |

背景（来源：SquareStripeButtonLook.kt:104-109、:89-99、ActionButtonLook.java:96-99）：

| 状态 | 背景 |
| --- | --- |
| 激活/聚焦 | `ToolWindow.Button.selectedBackground` |
| 可见（PUSHED） | `ActionButton.pressedBackground` |
| 悬停（POPPED） | `ActionButton.hoverBackground` |
| NORMAL | 组件背景色（未显式设置则**不绘制背景**） |

描边（来源：SquareStripeButtonLook.kt:87-102）：

- 激活/聚焦态**不画**描边；
- NORMAL 且未设置背景色**不画**描边；
- PUSHED → `ActionButton.pressedBorder`；其他 → `ActionButton.hoverBorder`。

图标着色（来源：SquareStripeButtonLook.kt:132-140、SquareStripeButton.kt:239-241）：

- 激活时图标转为描边图标（`toStrokeIcon`），颜色 `ToolWindow.Button.selectedForeground`；
- 非激活时用原图标（含主题色）。

禁用：`ActionButtonLook.getDisabledIcon(IconLoader.getDisabledIcon)`（来源：ActionButtonLook.java:135-137）；但 New UI 的方形按钮在 `updateUI`/`updateState` 里直接把 presentation 设为 `isEnabledAndVisible = true`，可见性由 `toolWindow.isShowStripeButton && toolWindow.isAvailable` 与“是否需要按钮”决定（来源：SquareStripeButton.kt:105-110、:386-396；platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:1189-1193）。

默认 Light（expUI）与 Dark（expUI）具体取值（来源：platform/platform-resources/src/themes/expUI/expUI_light.theme.json:725-757、platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:715-746 与两文件的 `colors` 段）：

| 键 | Light | Dark |
| --- | --- | --- |
| `ToolWindow.background` | `Gray13` = `#F7F8FA` | 未覆盖 → 回落 `JBColor.PanelBackground` |
| `ToolWindow.Button.foreground` | `Gray5` = `#5A5D6B` | `Gray9` = `#9DA0A8` |
| `ToolWindow.Button.selectedForeground` | `Gray14` = `#FFFFFF` | `Gray14` = `#FFFFFF` |
| `ToolWindow.Button.selectedBackground` | `Blue4` = `#3574F0` | `Blue6` = `#3574F0` |
| `ToolWindow.Button.hoverBackground` | `Gray12` = `#EBECF0` | 未覆盖（用 `ActionButton.hoverBackground`） |
| `ToolWindow.Stripe.separatorColor` | `Gray9` = `#C9CCD6` | `Gray4` = `#43454A` |
| `ToolWindow.Stripe.DragAndDrop.separatorColor` | `Blue7` = `#709CF5` | `Blue8` = `#548AF7` |
| `ToolWindow.HeaderTab.hoverBackground` | `Gray12` = `#EBECF0` | （只覆盖 `hoverInactiveBackground` = `Gray3`） |
| `ToolWindow.DragAndDrop.areaBackground` | `#A0BDF84D` | `#366ACF4D` |
| `ToolWindow.DragAndDrop.hintBackground` | `#A0BDF81A` | `#366ACF1A` |
| `ToolWindow.Button.DragAndDrop.stripeBackground` | `Gray13` | `Gray2` = `#2B2D30` |
| `ToolWindow.Button.DragAndDrop.buttonDropBackground` | `Blue11` = `#D4E2FF` | `#35538F99` |
| `ToolWindow.Button.DragAndDrop.buttonDropBorderColor` | `Blue7` | `Blue8` |
| `ToolWindow.Button.DragAndDrop.buttonFloatingBackground` | `Gray11` = `#DFE1E5` | `Gray5` = `#4E5157` |

- **【可直接实现】** 非聚焦主窗口下的图标另有“选中前景 = 主题色描边”；`StripeButtonUi.SELECTED_BACKGROUND_COLOR`/`FOREGROUND_COLOR` 等常量（`0x5A5D6B` / `0x9DA0A8`、`Gray.x55@40`、`Gray.x0F@40`、`Gray.x55@85`、`Gray.x0F@85`）属于 **Classic** stripe（`StripeButton`），New UI 走上面的主题键（来源：platform/platform-impl/src/com/intellij/toolWindow/StripeButtonUi.kt:27-33）。
- **【可直接实现】** 拖拽中的按钮用 `ToolWindow.Button.DragAndDrop.buttonFloatingBackground` 绘制“浮动按钮”，并居中画图标（来源：SquareStripeButtonLook.kt:144-158）。

### 3.4 排列、分隔与溢出

**排列比较器**（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/AbstractDroppableStripe.kt:56-77）：

1. 先按 `isSplit`：非分屏组在前，分屏组在后；
2. 若 New UI 且锚点 BOTTOM 且在**竖栏**上：按 order **倒序**（用户从下往上看按钮）；
3. 其他情况按 order 升序；`order == -1` 视为 `Int.MAX_VALUE`（排最后）。

- **【可直接实现】** 不可见的按钮不参与布局（拖拽中的按钮例外）（来源：AbstractDroppableStripe.kt:571-625）。
- **【可直接实现】** 分组方式两种：
  - New UI 左/右竖栏且**无扩展**：在第一个 `isSplit=true` 的按钮前插入分隔线组件；若分隔线会落到第一位，则普通状态下隐藏它、拖拽中转为“上方放置”标记；
  - 其他（Classic 或带扩展的 New UI）：不插入分隔线，改用 Classic 式“留白把分屏组推到远端”。
  （来源：AbstractDroppableStripe.kt:98-111、:578-622）
- **【可直接实现】** 拖拽中的放置矩形圆角 = `Button.ToolWindow.arc`，填充 `ToolWindow.Button.DragAndDrop.buttonDropBackground`；竖直栏上的跨组分屏还需画虚线框（虚线段长 4、框宽 30）（来源：AbstractDroppableStripe.kt:627-658）。
- **【可直接实现】** 拖拽敏感距离 `DROP_DISTANCE_SENSITIVITY = 200`（来源：AbstractDroppableStripe.kt:54）。
- **【可直接实现】** **溢出**：New UI 不用滚动，而是用 More 按钮。More 按钮只在**主** pane 上初始化，放入 topStripe 的父容器中央（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowToolbar.kt:104-114）。
- **【可直接实现】** More 按钮的可见性 = 该工具栏上“有可见按钮 **或** More 可见”；More 自身的可用性 = 弹出的动作列表非空（来源：ToolWindowToolbar.kt:92-98、MoreSquareStripeButton.kt:134-142）。
- **【可直接实现】** More 弹出内容 = 所有“有 stripe 按钮但当前没显示在 stripe 上”的可用工具窗口，外加 `ActivateToolWindowActions` 组中的额外项；排序：先按助记符（无助记符排最后）再按 ID 字母序（来源：platform/platform-impl/src/com/intellij/ide/actions/ToolWindowsGroup.java:46-88）。
- **【可直接实现】** More 弹出面板最小宽度 300，弹出位置 x = 工具栏宽（左侧）或 `-300`（右侧），y = More 按钮 y（来源：MoreSquareStripeButton.kt:100-126）。
- **【可直接实现】** More 按钮所在侧可切换（持久化 `moreButton`，默认 LEFT）；同一时刻只有一个侧栏上的 More 可见（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:1443-1458、platform/platform-impl/src/com/intellij/toolWindow/MoreSquareStripeButton.kt:78-83）。
- **【可直接实现】** 按钮右键菜单：`隐藏该窗口` → 分隔线 → `移动到…` 组 → 分隔线 → `显示工具窗口名称`（来源：SquareStripeButton.kt:365-373）。
- **【可直接实现】** 菜单里的“隐藏”语义是 `hideToolWindow(hideSide=false, moveFocus=true, removeFromStripe=true)`——即**连同 stripe 按钮一起移除**（来源：SquareStripeButton.kt:375-384）；而左键点击已激活按钮则是 `removeFromStripe=false`（来源：SquareStripeButton.kt:398-415）。
- **【可直接实现】** 按钮支持鼠标中键或 Shift+左键 = “关闭”语义：Classic 按钮直接 `hideToolWindow(id, hideSide=true)`（来源：platform/platform-impl/src/com/intellij/toolWindow/StripeButton.kt:244-247）；New UI 方形按钮只响应左键，其他键不触发按压逻辑（来源：SquareStripeButton.kt:139）。
- **【可直接实现】** 按钮 tooltip：纯文本标题 + 对齐方向（LEFT 栏→RIGHT、RIGHT 栏→LEFT、TOP→BOTTOM、BOTTOM 无扩展时按分屏取左/右）+ Activator 快捷键，`initialDelay=0`、`hideDelay=0`（来源：SquareStripeButton.kt:126-154）。
- **【可直接实现】** tooltip 在主弹出层打开时会自行抑制（来源：SquareStripeButton.kt:134）。
- **【可直接实现】** 拖拽前的“可拖拽”标记：New UI 方形按钮 `MouseDragHelper.setComponentDraggable(this, true)`；整条拖拽是否启用受高级设置 `ide.tool.window.header.dnd`（默认 **true**）控制（来源：SquareStripeButton.kt:91、platform/platform-impl/src/com/intellij/toolWindow/ToolWindowDragHelper.kt:676）。

### 3.5 stripe 宽度、名称与"显示名称"模式

- **【可直接实现】** stripe 宽度可拖拽（New UI 左/右竖栏）：拖拽结果写入按锚点分开的自定义宽度 `toolWindowLeftSideCustomWidth` / `toolWindowRightSideCustomWidth`，默认 0（来源：platform/platform-impl/src/com/intellij/toolWindow/ResizeStripeManager.kt:230-253、platform/editor-ui-api/src/com/intellij/ide/ui/UISettingsState.kt:85-87）。
- **【可直接实现】** 宽度换算：`width = 父容器宽 × proportion`；RIGHT 侧取补（`fullWidth - width`）；再叠加拖拽起始时的 delta（来源：ResizeStripeManager.kt:113-136）。
- **【可直接实现】** 上下限：最小 40（`compactMode` 为 33），最大 100（来源：ResizeStripeManager.kt:138-150）。
- **【可直接实现】** 只有“条带可调整大小”时才挂载 1px 拖拽分隔件：无扩展时 = `showToolWindowsNames`；有扩展时由扩展决定。分隔件宽度 1，左侧栏贴右边、右侧栏贴左边（来源：ResizeStripeManager.kt:79-102、:210-213）。
- **【可直接实现】** 点击/双击分隔件会设置 0.5 比例：实现上用 `myIgnoreProportion` 区分“拖拽”（`asComponent()` 先被调用）与“点击”（只连续 `setProportion`），点击被忽略以免跳变（来源：ResizeStripeManager.kt:43、:113-117、:184-191）。
- **【可直接实现】** 自定义宽度 > 0 ⇒ 按钮显示名称（`showName`），否则只显示图标；`width == 0 && scale == 0` 时首次会读取设置并做一次上下限修正（来源：ResizeStripeManager.kt:152-182）。
- **【可直接实现】** 开关“显示工具窗口名称”会把左右自定义宽度统一重置为 **59**（开启）或 **0**（关闭），并广播给所有已打开项目（来源：ResizeStripeManager.kt:215-228）。
- **【可直接实现】** 缩放变化时宽度按比例重算（`width × newScale / oldScale`）（来源：ResizeStripeManager.kt:162-165）。
- **【可直接实现】** 无扩展时，在 stripe 空白处右键弹出的菜单只有一项：`ToolWindowShowNamesAction`（来源：ResizeStripeManager.kt:49-61、:255-258）。
- **【可直接实现】** 名称态的按钮外观切换会重建 look 并 revalidate/repaint（来源：SquareStripeButton.kt:156-164）。
- **【可直接实现】** stripe 内换行的名称使用 TINY 相对字号（来源：SquareStripeButton.kt:324）。
- **【可直接实现】** stripe 整体可见性：`showButtons = !hideToolStripes && !presentationMode`，或处于“覆盖显示”状态；左右栏分别按“自身是否有可见按钮”决定（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowPane.kt:422-428、ToolWindowPaneNewButtonManager.kt:81-104）。
- **【可直接实现】** `hideToolStripes` 还可能被 `experimentalSingleStripe`（非漫游设置）强制为 true（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettings.kt:83-86）。

### 3.6 header（工具窗口标题栏）

- **【可直接实现】** 高度：New UI = `ToolWindow.Header.height`，默认 **41**；Classic = `SingleHeightTabs.UNSCALED_PREF_HEIGHT`。实际首选高 = `scale(该值) + (需要顶线 ? 1 : 0) - insets.top - insets.bottom`（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowHeader.kt:75-81、:337-343、platform/util/ui/src/com/intellij/util/ui/JBUI.java:1173-1183）。
- **【可直接实现】** 标题（左侧 label）左右内距 New UI = `ToolWindow.Header.labelLeftRightInsets`，默认 `(0,12,0,16)`；不显示 ID 时用 `ToolWindow.HeaderTab.leftRightInsets`，默认 New UI `(0,12)`、Classic `(0,8)`；Classic 退回 `(0,2,0,7)`（来源：JBUI.java:1185-1191、:1133-1135、platform/platform-impl/src/com/intellij/openapi/wm/impl/content/ContentLayout.java:59-65、platform/platform-impl/src/com/intellij/openapi/wm/impl/content/BaseLabel.java:56-58）。
- **【可直接实现】** 右侧工具栏内距：New UI = `ToolWindow.Header.toolbarLeftRightInsets`，默认 `(0,12,0,8)`；Classic = `(2,0)`（来源：ToolWindowHeader.kt:182-185、JBUI.java:1189-1191）。
- **【可直接实现】** 标题左区（west panel）为两列 GroupLayout：第一列是内容 tab 组件（宽度取 preferred、最大 preferred，高度可拉伸到无穷），可选第二列为 `sideComponent`，两侧各留 1px 间隙（来源：ToolWindowHeader.kt:399-425）。
- **【可直接实现】** 右侧工具栏使用 `NOWRAP` 策略、不保留自动弹出图标；当 New UI 且锚点不是 LEFT/RIGHT 时，其首选宽度直接取工具栏首选宽（来源：ToolWindowHeader.kt:179-197）。
- **【可直接实现】** 工具栏动作顺序（左→右）：`TabList` → [当前内容的关闭动作] → [当前内容自带动作] → 分隔线 → 附加标题动作 → 通用组（`停靠` / `齿轮选项` / `隐藏`）。当装饰器带 `HideCommonToolWindowButtons` 时**只保留 `TabList`**（来源：ToolWindowHeader.kt:119-159、:99-103）。
- **【可直接实现】** 顶/底线绘制（New UI）：需要顶线由装饰器判定；底线默认画，但以下情况按规则处理——
  - BOTTOM 锚点 → 画；
  - `contentUiType == TABBED` 且内容数 > 1 → 画；
  - 窗口自带顶部工具栏 → 画；
  - 内容处于“已滚动”状态 → 画；
  - 若选中的内容组件声明 `HIDE_HEADER_BOTTOM_LINE` → 不画（内容工具栏可与 header 无缝相接）。
  （来源：ToolWindowHeader.kt:293-318、ToolWindowHeader.kt:96-104）
- **【可直接实现】** **header 图标默认只在悬停/激活时出现**（New UI 且未开启 `ide.always.show.tool.window.header.icons`，该高级设置默认 **false**）：透明度动画的可见条件 = 悬停 **或** 弹出层显示中 **或** 窗口激活 **或** 装饰器宽度 < 120 **或** 内容声明 `DONT_HIDE_TOOLBAR_IN_HEADER`；Classic 关闭该行为（来源：platform/platform-impl/src/com/intellij/toolWindow/InternalDecoratorImpl.kt:920-937、platform/platform-impl/resources/intellij.platform.ide.impl.xml:1458）。
- **【可直接实现】** 非激活窗口在浅色主题下额外叠一层 `#FFFFFF` @ alpha 30 的白色蒙版（来源：ToolWindowHeader.kt:320-331）。
- **【可直接实现】** 标题区交互：
  - 单击 → `activateToolWindow`（来源 `ToolWindowHeader`）并把焦点交给内容；
  - **双击标题区 → 切换最大化**（来源：ToolWindowHeader.kt:242-248）；
  - 中键 / Shift+左键 → 隐藏**整条侧栏**；再按 Alt → 只隐藏**当前窗口**（来源：ToolWindowHeader.kt:211-235、platform/util/ui/src/com/intellij/util/ui/UIUtil.java:1843-1846）；
  - 右键 → 工具窗口上下文菜单（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/content/ToolWindowContentUi.java:622-635）。
- **【可直接实现】** 内容区（tab 区域）上的隐藏语义与 header 相反：中键/Shift+左键 = 隐藏整条侧栏，**Ctrl**+中键/Shift+左键 = 只隐藏当前窗口（来源：ToolWindowContentUi.java:765-772）。
- **【可直接实现】** 可拖动区域：header 顶部保留一段“拖拽/拖动”区，高度 = `ide.new.tool.window.resize.area.height`（注册表值为 **13**，代码内兜底默认 14，范围 1..26）乘以缩放；BOTTOM 锚点在该区域之上的拖动被判定为**移动窗口**而不是调整大小（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowPane.kt:81-84、ToolWindowContentUi.java:555-580、platform/util/resources/misc/registry.properties:130-131）。
- **【可直接实现】** 鼠标悬停在 header 的可调整区域时显示 `N_RESIZE_CURSOR`（上下调整），离开恢复默认指针（来源：ToolWindowContentUi.java:540-553）。
- **【Swing 特有】** header 与内容 tab 的工具栏是 `ActionToolbar`（动作系统），Web 实现只需还原“按钮集合 + 顺序 + 悬停显隐”的结果。

---

## 4. 显示 / 隐藏 / 激活状态机

### 4.1 `ToolWindowEventSource` 枚举（全部触发来源）

枚举值（顺序即声明顺序，新增/删除需同步提升事件日志版本）（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:38-46）：

`StripeButton`、`SquareStripeButton`、`ToolWindowHeader`、`ToolWindowHeaderAltClick`、`Content`、`Switcher`、`SwitcherSearch`、`ToolWindowsWidget`、`RemoveStripeButtonAction`、`HideOnShowOther`、`HideSide`、`CloseFromSwitcher`、`ActivateActionMenu`、`ActivateActionKeyboardShortcut`、`ActivateActionGotoAction`、`ActivateActionOther`、`CloseAction`、`HideButton`、`HideToolWindowAction`、`HideSideWindowsAction`、`HideBottomWindowsAction`、`HideAllWindowsAction`、`JumpToLastWindowAction`、`ToolWindowSwitcher`、`InspectionsWidget`。

- **【可直接实现】** 该枚举只用于埋点与日志（`recordActivation`/`recordHidden`/`recordShown`），**不参与行为分支**（来源：ToolWindowManagerImpl.kt:611-613、:940、:713）。
- **【可直接实现】** 关键来源与语义映射：
  - stripe 左键点击 → `SquareStripeButton`（来源：SquareStripeButton.kt:406、:413）；
  - header 单击/双击标题 → `ToolWindowHeader`；header 的 Alt+关闭 → `ToolWindowHeaderAltClick`（来源：ToolWindowHeader.kt:206、:221、:224、:228）；
  - 显示新窗口挤掉旧窗口 → `HideOnShowOther`（来源：ToolWindowManagerImpl.kt:980）；
  - 隐藏整侧时对同侧其他窗口 → `HideSide`（来源：ToolWindowManagerImpl.kt:893）。

### 4.2 激活流程（`activateToolWindow`）

顺序（来源：ToolWindowManagerImpl.kt:574-645）：

1. 注册一个 `UiActivity.Focus("toolWindow:<id>")`（非模态），用于抑制“自动隐藏”等抢焦点逻辑；
2. 取 entry；不存在则记 error 并返回；
3. **若“统一尺寸”开启且该窗口可使用统一权重** → 用锚点统一权重覆盖 `info.weight`；
4. 记录埋点，并把该 ID 从 `recentToolWindows` 移除后插到队首（最近使用列表）；
5. 若窗口**不可用**（`!isAvailable`）：只把窗口带到前台/请求焦点，**不改变可见性**，然后返回；
6. 若当前**不可见** → 置 `isActiveOnStart = autoFocusContents` 后走 `showToolWindowImpl`；
7. 否则若**不要求焦点**且类型不是内部（docked/sliding）→ 把宿主窗口带到前台（不抢焦点）；
8. 若要求焦点且应用处于激活状态 → `requestFocusInToolWindow()`；否则把 entry 压入 `activeStack`；
9. 发出 `ActivateToolWindow` 事件。

- **【可直接实现】** `autoFocusContents` 会写入 `isActiveOnStart`，这是“窗口因激活而显示”与“仅因布局而显示”的区分点（来源：ToolWindowManagerImpl.kt:630-631、:715）。
- **【可直接实现】** 激活完成后（异步 EDT）才执行调用方传入的 runnable，然后移除 `UiActivity` 标记（来源：ToolWindowManagerImpl.kt:589-595）。
- **【可直接实现】** `activeToolWindowId` 不是直接记录的，而是**按焦点组件反查**：取当前活动 pane 的 `mostRecentFocusOwner` 逐级上溯到最近的可停靠装饰器；非活动框架时检查 floating/windowed 装饰器（来源：ToolWindowManagerImpl.kt:729-744、platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerSupport.kt:90-99）。
- **【可直接实现】** `lastActiveToolWindowId` 取 `activeStack` 中“持久化深度”内第一个可用窗口（来源：ToolWindowManagerImpl.kt:746-754）。

### 4.3 显示流程（`showToolWindowImpl` → `doShowWindow`）

`showToolWindowImpl`（来源：ToolWindowManagerImpl.kt:930-952）：

1. `!isAvailable` → 直接返回 false（不显示）；
2. 埋点；
3. `isVisible = true`，**同时把 `isShowStripeButton` 强制置回 true**；
4. `order == -1` 时分配“同锚点最大 order+1”；
5. 复制一份 info 快照应用到 entry，然后 `doShowWindow`。

`doShowWindow`（来源：ToolWindowManagerImpl.kt:954-1020）：

1. FLOATING → 浮动装饰器；WINDOWED → 独立窗口装饰器；其余（DOCKED/SLIDING）走内部停靠逻辑；
2. **同锚点同 pane 同 `isSplit` 同 `type` 的其他可见窗口会被隐藏**：走 `setHiddenState(..., HideOnShowOther)`、应用新 info、从 pane 移除装饰器；若栈功能开启且对方是停靠、非 auto-hide 窗口，则把它的 info 压入 `SideStack`；
3. 把本窗口装饰器加到 pane；
4. 从 `SideStack` 中移除本窗口 ID；
5. 若还没有 stripe 按钮 → 创建；
6. 调度内容初始化，发出 `toolWindowShown` 事件。

- **【可直接实现】** 结论：**同一侧栏（同锚点同 pane）同时最多只有一个非分屏窗口 + 一个分屏窗口可见**；“切换”表现为隐藏旧的、显示新的。
- **【可直接实现】** 只有同一 `isSplit` 状态的窗口会互相挤掉；分屏窗口与非分屏窗口可以同时存在（来源：ToolWindowManagerImpl.kt:976-977）。
- **【可直接实现】** auto-hide 窗口被挤掉时**不进入撤销栈**（来源：ToolWindowManagerImpl.kt:990-992）。

### 4.4 隐藏流程与 `hideSide` / `HideOnShowOther` / 撤销栈

`hideToolWindow`（来源：ToolWindowManagerImpl.kt:829-868）：

- 参数：`hideSide=false`、`moveFocus=true`、`removeFromStripe=false`、`source`；
- `moveFocusAfter = moveFocus && 当前窗口处于激活`，但**若窗口本来就不可见则强制为 false**；
- `removeFromStripe=true` 时附带 mutation：`isShowStripeButton = false` 并移除按钮；隐藏完成后把 `toolWindowPaneId` 重置为根 pane；
- 隐藏后发 `HideToolWindow` 事件；若需要移焦，则把焦点交还编辑器区；最后 `revalidateStripeButtons()`。

`executeHide`（来源：ToolWindowManagerImpl.kt:870-925）：

1. 先 `deactivateToolWindow`（§4.6）；
2. **`hideSide = true` 且类型不是 FLOATING/WINDOWED**：清空该锚点在 `activeStack`/`SideStack` 中的所有记录，然后把**同 pane 同锚点的所有其他可见窗口**逐个 `deactivateToolWindow(..., source = HideSide)`；
3. **否则（只隐藏自己）**：栈功能开启时，从 `SideStack` 里弹出**第一个 `isSplit` 与本窗口相同、且 pane/锚点/type/autoHide 都仍然匹配**的历史窗口；若它当前不可见则把它显示出来（这就是“隐藏当前窗口后自动恢复上一个”的机制）；随后把自己从 `activeStack` 移除。

- **【可直接实现】** 撤销栈默认关闭：`ide.enable.toolwindow.stack` 默认 **false**（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerSupport.kt:87-88、platform/util/resources/misc/registry.properties:1008-1009）。开启后不匹配的历史项会被丢弃（脏数据防御）。
- **【可直接实现】** `SideStack.push` 断言必须是停靠且非 auto-hide 窗口（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/SideStack.java:19-23）。
- **【可直接实现】** `clearSideStack()` 只在栈功能开启时生效（来源：ToolWindowManagerImpl.kt:1799-1803）。
- **【可直接实现】** 窗口变为**不可用**（`toolWindowUnavailable`）时：若它当时激活且可见，隐藏后把焦点交还编辑器区，并移除 stripe 按钮（来源：ToolWindowManagerImpl.kt:1881-1892）。
- **【可直接实现】** 窗口注册时会**替换**（`Temp`）等特殊注册路径同样受 `isButtonNeeded` 控制（来源：ToolWindowManagerImpl.kt:1039-1057、:1189-1193）。

### 4.5 autoHide 语义

- **【可直接实现】** `isAutoHide` 只是一个持久化布尔量（默认 false）；`setToolWindowAutoHide` 仅在值变化时更新并发出 `SetToolWindowAutoHide` 事件，**不改变可见性**（来源：ToolWindowManagerImpl.kt:1742-1757）。
- **【可直接实现】** 真正行为在焦点监听里（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerLifecycle.kt:55-138）：
  1. 只处理“永久性焦点丢失”：`oppositeComponent == null`、`isTemporary`、或对方已不显示 → 忽略；
  2. 取当前最后聚焦框架的项目的管理器；
  3. `auto.hide.all.tool.windows.on.focus.change`（默认 **true**）→ 遍历**所有**窗口；false → 只检查“丢失焦点的那个窗口”；
  4. 对每个候选：不可见 → 跳过；既非 auto-hide 且类型不是 SLIDING → 跳过；
  5. `isAboutToReceiveFocus` 为真 → 跳过（避免“从一个滑动窗口切到另一个”时刚显示就被隐藏）；
  6. 焦点确实离开该窗口 → `deactivateToolWindow(info, entry)`；
  7. 例外：焦点进入该窗口的子弹出气球（popup balloon）或对话框 → 不隐藏。
- **【可直接实现】** 任何动作执行前还可能触发一次隐藏（`auto.hide.all.tool.windows.on.any.action`，默认 **true**）：排除“从 Swing 弹出窗口触发”的动作和“动作来源工具窗口自身”，其余 auto-hide 窗口全部隐藏（来源：ToolWindowManagerLifecycle.kt:210-228、platform/ide-core-impl/resources/intellij.platform.ide.core.impl.xml:484-488）。
- **【可直接实现】** 属于**非激活**框架的窗口：当某框架失去焦点且获得焦点的不是同一管理器的任一 pane 框架时，重置“hold 状态”（即 Alt 覆盖态）（来源：ToolWindowManagerLifecycle.kt:151-160）。
- **【可直接实现】** 编辑器区获得焦点时会清空 `activeStack`（来源：ToolWindowManagerLifecycle.kt:84-93）。
- **【可直接实现】** `deactivateToolWindow` 的写状态顺序：先 `setHiddenState`（埋点 → `isActiveOnStart=false` → `isVisible=false` → 从 `activeStack` 移除）→ 再执行 mutation → 再移除装饰器 → 最后把 info 应用到 entry（来源：ToolWindowManagerImpl.kt:696-718）。
- **【需复核实】** Augit 不使用 SLIDING 模式；若实现“自动隐藏”，只需保留 `isAutoHide` 的语义与上面第 4 步的条件。

### 4.6 相关动作语义

| 动作 | 语义 | 来源 |
| --- | --- | --- |
| `HideToolWindowAction` | 隐藏**当前活动**（无活动则最近活动）窗口，`hideSide=false`；可用性 = 窗口可见且焦点在其中，或满足“可用快捷键隐藏”（可见 + 内部类型） | platform/platform-impl/src/com/intellij/ide/actions/HideToolWindowAction.kt:17-27、:29-48 |
| `HideSideWindowsAction` | 隐藏当前/最近窗口**所在整侧**（`hideSide=true`）；可用性：无活动窗口时看最近窗口是否可隐藏，有活动窗口时一律可用 | platform/platform-impl/src/com/intellij/ide/actions/HideSideWindowsAction.kt:14-21、:24-40 |
| `HideBottomToolWindowsAction` | 逐个隐藏**所有 BOTTOM 锚点**且“可用快捷键隐藏”的窗口（不改变其他侧） | platform/platform-impl/src/com/intellij/ide/actions/HideBottomToolWindowsAction.kt:15-20、:36-39 |
| `HideAllToolWindowsAction` | 若有可隐藏窗口：先 `clearSideStack()`，逐个隐藏（`hideSide=false`），把整份布局快照存进 `layoutToRestoreLater`，最后 `activateEditorComponent()`；若已无可隐藏窗口但存在快照 → **恢复快照布局**（同一动作即“隐藏全部/恢复窗口”切换） | platform/platform-impl/src/com/intellij/ide/actions/HideAllToolWindowsAction.kt:14-49 |
| `JumpToLastWindowAction` | 取 `lastActiveToolWindowId`，窗口必须可用，然后 `activateToolWindow(id, runnable=null, autoFocusContents=true)`；可用性 = 存在最近活动窗口且可用 | platform/platform-impl/src/com/intellij/ide/actions/JumpToLastWindowAction.java:19-49 |

- **【可直接实现】** “可用快捷键隐藏”的统一判定 = `isVisible && type.isInternal`（即 FLOATING/WINDOWED 不参与批量隐藏）（来源：HideToolWindowAction.kt:17-21）。
- **【可直接实现】** `HideAllToolWindowsAction` 会把状态写进 `presentation` 的 `CURRENT_STATE_IS_MAXIMIZED_KEY`，供主工具栏按钮显示“最大化编辑器/恢复窗口”的切换图标（来源：HideAllToolWindowsAction.kt:40-46）。

### 4.7 Alt 双击“覆盖/悬浮 stripe”状态机

- **【可直接实现】** 状态：`WAITING → PRESSED →(松开) RELEASED →(再按) HOLD`（来源：ToolWindowManagerImpl.kt:373-394、platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerSupport.kt:25-27）。
- **【可直接实现】** 触发键 = `ActivateProjectToolWindow` 快捷键的修饰键（取不到或修饰键多于一个时回落到 Alt / macOS 上 Meta）；`toolwindow.disable.overlay.by.double.key` 为真时整个机制关闭（来源：ToolWindowManagerSupport.kt:50-85）。
- **【可直接实现】** 只有“精确按下该修饰键（`modifiers xor mask == 0`）”或松开时才推进状态；按住期间若鼠标按键按下则重置（来源：ToolWindowManagerImpl.kt:355-365）。
- **【可直接实现】** 进入 `HOLD` 时把所有 pane 设为“条带覆盖显示”（`setStripesOverlaid(true)` → stripe 即使被隐藏也显示）；高级设置 `ide.suppress.double.click.handler`（默认 false）为真时不做覆盖显示（来源：ToolWindowManagerImpl.kt:378-383、platform/platform-impl/resources/intellij.platform.ide.impl.xml:1487）。
- **【可直接实现】** 第二次按下的等待窗口 = `actionSystem.keyGestureDblClickTime`（默认 **300 ms**）；超时后若仍不在 HOLD 则重置（来源：ToolWindowManagerImpl.kt:191-199）。
- **【可直接实现】** 任何“无修饰键的按键”会重置状态；跨项目的按键也会重置（来源：ToolWindowManagerImpl.kt:331-353）。
- **【可直接实现】** 覆盖态下点击 stripe 按钮意味着“显示该窗口”，随后应重置覆盖态（由鼠标/焦点事件驱动）。
- **【可直接实现】** 编辑器中“聚焦/悬停变更”会以 50 ms 防抖批量刷新工具窗口 header 的悬停/激活外观（来源：ToolWindowManagerLifecycle.kt:170-184）。

### 4.8 其他影响状态机的时序与防抖

- **【可直接实现】** 隐藏后按钮刷新：`revalidateStripeButtons()` 通过 `invokeLater` 异步执行（来源：ToolWindowManagerImpl.kt:295-298）。
- **【可直接实现】** 布局重算后的按钮/面板刷新统一放在第 3 遍（见 §1.6）。
- **【可直接实现】** New UI 下点击 header 上的动作（`ActionPlaces.TOOLWINDOW_TITLE`）会先激活该工具窗口；在工具窗口弹出菜单中执行动作（`ActionPlaces.TOOLWINDOW_POPUP`）会把最近活动窗口的 header 工具栏重新置为可见（来源：ToolWindowManagerLifecycle.kt:230-242）。

---

## 5. 分隔条、权重与尺寸约束

### 5.1 权重 ↔ 像素的换算

- **【可直接实现】** 设置尺寸：`anchorSize = 根 pane 尺寸 × weight`（TOP/BOTTOM 用高，LEFT/RIGHT 用宽），再写入对应分隔条的 first/last size（来源：platform/platform-impl/src/com/intellij/toolWindow/ToolWindowPane.kt:355-374）。
- **【可直接实现】** 回读比例（`getAdjustedRatio`）：`ratio = part/total`，再加半个像素的补偿：`ratio += ((part + direction)/total - ratio) / 2`（`direction` 为 +1 或 -1）。效果是取“part/total”与“part±1/total”的中点，避免像素取整把边界整块算给某一侧（来源：ToolWindowPane.kt:323-331、platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:228-232）。
- **【可直接实现】** 权重归一化 `normalizeWeight`：`weight <= 0` → 0.33；`weight >= 1` → 0.67；其余原样（来源：ToolWindowPane.kt:86-91）。
- **【可直接实现】** 分屏比例归一化：`normalizeWeight(sideWeight_a / (sideWeight_a + sideWeight_b))`（来源：ToolWindowPane.kt:741-744）。
- **【可直接实现】** 根 pane 尺寸不可信时（即将最大化、或尺寸为 0×0）**推迟**设置权重，等 resize 后再重试；同一锚点新的设置请求会取消上一个未完成的 future（来源：ToolWindowPane.kt:333-354）。**这就是“未完成的尺寸请求会被取消”的机制**。
- **【可直接实现】** 尺寸更新前的守卫：若停靠区位于 ThreeComponentsSplitter 内、且 inner（编辑器）组件为 null 或不可见，则**不更新权重**（避免 IDEA-319836 类问题）（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerDecorators.kt:270-287、:163-190）。
- **【可直接实现】** 分屏 `sideWeight` 写入：`splitter.proportion = window.isSplit ? normalizeWeight(1 - sideWeight) : normalizeWeight(sideWeight)`（first 组件占整条的比例）（来源：ToolWindowPane.kt:378-397）。
- **【可直接实现】** 最大/最小尺寸：`getMaxSize(first) = size - 另一侧尺寸 - minSize`（来源：platform/platform-api/src/com/intellij/openapi/ui/ThreeComponentsSplitter.kt:484-506）。
- **【可直接实现】** 尺寸变化时若某侧把空间挤没了，会从 first/last 中按 `minSize` 回补（来源：ThreeComponentsSplitter.kt:365-390）。

### 5.2 拖动分隔条

- **【Swing 特有】** 拖动由两层机制实现，可借鉴其规则但不能照搬：
  1. **分隔条本身**：`OnePixelSplitter`（锚点内部两个窗口之间，1px 视觉宽度）与 `ThreeComponentsSplitter`（锚点与编辑器之间，分隔件宽 0，鼠标感应区 = `ide.splitter.mouseZone`，默认 **6**）；`setHonorComponentsMinimumSize(false)`；左/右锚点的分隔条**允许鼠标点击切换方向**（即切换 `leftHorizontalSplit`/`rightHorizontalSplit` 并持久化，同时触发 UI 设置变更事件）（来源：ToolWindowPane.kt:203-208、:638-699、platform/util/resources/misc/registry.properties:290-291）。
  2. **header 内的直接拖拽**：在 header 空白区按下后拖动，直接 `setFirstSize/setLastSize`（ThreeComponentsSplitter）或 `setProportion(1 - delta/高度)`（Splitter）；SLIDING 窗口改为 `updateBounds`；非 SLIDING 的浮动窗口只改高度（来源：ToolWindowContentUi.java:555-620）。
- **【可直接实现】** 拖动结束后的持久化：`info.weight = 实际尺寸/总尺寸`，`unifiedAnchorWeight[anchor] = 停靠区尺寸/总尺寸`；同一分隔条内的另一个窗口同步重算 `sideWeight`（second 组件要额外加上分隔件宽度）（来源：ToolWindowManagerDecorators.kt:163-190）。
- **【可直接实现】** 拖动的最小/最大约束：
  - 主分隔条（侧栏 vs 编辑器）：`ide.mainSplitter.min.size`，注册表值 **30**，代码内再夹到 `0..100` 后按缩放取用；该值同时作用于垂直与水平分隔条，并在注册表值变化时实时更新（来源：ToolWindowPane.kt:172-179、:218-222、platform/util/resources/misc/registry.properties:160-161）。
  - stripe 宽度：40..100（compact 33..100）（来源：ResizeStripeManager.kt:138-150）。
  - `setMaximized` 用 `stretch(window, Int.MAX_VALUE)` 撑满，恢复时还原保存的尺寸（来源：ToolWindowPane.kt:542-560、:439-455）。
- **【可直接实现】** 拖动（`setDragging(true)`）会记录起始 delta 并在下一次 `setProportion` 中补偿，避免“按下瞬间跳一格”（来源：ResizeStripeManager.kt:200-203、:113-136）。
- **【需复核实】** 任务描述里的“拖动时清除未完成的滚轮输入”在本 commit 的 `toolWindow`/`openapi/wm/impl`/`ThreeComponentsSplitter`/`OnePixelDivider` 中**未找到任何鼠标滚轮（`MouseWheelEvent`）相关代码**。已确认的同类“未完成输入清理”只有 §5.1 的“取消未完成的 `runOnceWhenResized` 权重回调”和 `ResizeStripeManager.myIgnoreProportion`（忽略点击触发的比例设置）。若确有滚轮行为，需要在其他模块（如 `IdeGlassPaneImpl` 的滚轮拦截器）继续核实。

### 5.3 滑动窗口（SLIDING）的尺寸与动画

- **【可直接实现】** 滑动窗口放在 layered pane 的 `PALETTE_LAYER`；显示/隐藏动画仅在 `UISettings.animateWindows` 为真且**非远程桌面会话**时执行；动画时长常量 `ANIMATION_DURATION = 300` ms（来源：ToolWindowPane.kt:788-880、platform/editor-ui-api/src/com/intellij/ide/ui/UISettings.kt:601）。
- **【可直接实现】** 动画形式：把被显示/隐藏的组件先画到“上层图像”，把背景画到“下层图像”，再用一个临时 Surface 移动；`dirtyMode`（批量布局中）禁用动画（来源：ToolWindowPane.kt:794-837、:844-875）。
- **【Swing 特有】** `rootPane.isAboutToBeMaximized` / `extendedState` 参与权重推迟判定（来源：ToolWindowPane.kt:341-346）。

---

## 6. 标题、图标与 `contentUiType`

### 6.1 `ToolWindowProperty`

- **【可直接实现】** 枚举只有 4 个值：`TITLE`、`ICON`、`AVAILABLE`、`STRIPE_TITLE`（来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:28-31）。
- **【可直接实现】** `toolWindowPropertyChanged` 的实际反应：`ICON` → `stripeButton.updateIcon(...)`；**其他所有值（含 TITLE / STRIPE_TITLE / AVAILABLE）→ `stripeButton.updatePresentation()`**；两种情况都会同步刷新 `ActivateToolWindowAction` 的 presentation（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowManagerImpl.kt:1897-1909）。
- **【需复核实】** `ToolWindowProperty.AVAILABLE` 在本仓库中没有任何触发点；可用性变化走的是 `toolWindowAvailable`/`toolWindowUnavailable` 两条独立路径（来源：ToolWindowManagerImpl.kt:1875-1892）。

### 6.2 标题与图标来源

- **【可直接实现】** `stripeTitle` 由 `stripeTitleProvider` 动态求值；`stripeShortTitleProvider` 可选（stripe 名称换行时优先用它）（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/ToolWindowImpl.kt:653-655、:700-704、SquareStripeButton.kt:326-329）。
- **【可直接实现】** `setStripeTitle` 若值未变则直接返回；变更后刷新 contentUi 并在非初始化期发 `STRIPE_TITLE` 属性事件（来源：ToolWindowImpl.kt:682-694）。
- **【可直接实现】** 标题解析规则（供注册默认标题）：
  - `Project` 特殊：用 `IdeUICustomization.getProjectViewTitle(project)`，并持有 project 弱引用、project 释放后返回空串；
  - 其他：资源键 `toolwindow.stripe.<id>`（空格替换为下划线），取不到则回退为 ID 本身；bundle 名对核心插件用 `IdeBundle`，其他插件用其 `resourceBundleBaseName`（取不到返回 null）；
  - 找不到 bundle 时记录警告并返回空串。
  （来源：platform/platform-impl/src/com/intellij/toolWindow/toolwindow.kt:48-76）
- **【可直接实现】** `getTitle()`（工具窗口标题，非 stripe 标题）= **当前选中内容的 displayName**；`setTitle` 改的是选中内容的 displayName（来源：ToolWindowImpl.kt:651、:676-680）。
- **【可直接实现】** 图标来源优先级：工厂的 `icon` → EP 的 `icon` 路径解析；解析异常记 error 并回退 `EmptyIcon.ICON_13`（来源：toolwindow.kt:78-91）。
- **【可直接实现】** Classic UI 会校验工具窗口图标是否为 13×13（偏差 ≥1 记录警告）；New UI 不做此校验（来源：ToolWindowImpl.kt:665-674）。
- **【可直接实现】** stripe 按钮上的图标一律按 `StripeToolbar.Button.iconSize`（默认 20）缩放，与工具窗口原始图标尺寸解耦。
- **【可直接实现】** `isShowStripeButton` 与 `isAvailable` 共同决定按钮的可见性；`isVisible` 决定按钮的选中态（来源：SquareStripeButton.kt:386-396）。
- **【可直接实现】** 显示新窗口时 `isShowStripeButton` 会被强制恢复为 true（见 §4.3）——即“从 stripe 移除过”的窗口再次被显示时会重新获得按钮。

### 6.3 `TABBED` 与 `COMBO` 的界面差异

- **【可直接实现】** `contentUiType` 决定 `ToolWindowContentUi` 使用哪个 `ContentLayout`：
  - `TABBED` → `tabsLayout`，其真实类型是 **`SingleContentLayout`**（New UI 的单内容布局）；
  - 其他（`COMBO`）→ 懒创建的 `ComboContentLayout`。
  （来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/content/ToolWindowContentUi.java:348-358、:128-133）
- **【可直接实现】** `SingleContentLayout` 语义：工具窗口 header **就是它的 tab 栏**。若唯一的 Content 通过 `SingleContentSupplier.KEY` 提供子内容与动作，则这些 tab 与动作会被“搬进”工具窗口 header；当有 2 个以上 Content 时，外观退化为普通的多 tab 布局（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/content/SingleContentLayout.kt:80-91、:102-109）。
- **【可直接实现】** `COMBO` 布局：header 内是“ID 标签 + 下拉选择器”；下拉只有在 `contentCount > 1` 时才绘制（`isToDrawCombo`），下拉宽度受限（不超过剩余空间），并可用 `showContentPopup` 弹出最小宽度等于下拉宽度的列表（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/content/ComboContentLayout.java:44-73、:100-102、:104-109）。
- **【可直接实现】** `COMBO` 布局下 header 动作名会换成“视图”措辞：关闭 = “关闭视图”、关闭其他 = “关闭其他视图”、上一个 = “选择上一个视图”、下一个 = “选择下一个视图”（来源：ComboContentLayout.java:111-129）。
- **【可直接实现】** 两种布局的首选宽高算法不同：`TABBED` 的 tab 起始偏移来自 `tabsLayout.getTabLayoutStart()`，`COMBO` 用 `TabContentLayout.defaultTabLayoutStart()`（来源：ToolWindowContentUi.java:976-987）。
- **【需推断·Augit 相关】** V2 默认布局里只有 `Project` 与 `Notifications` 是 `COMBO`；Augit 的“项目”面板若采用单内容 + 显示工具窗口名称，用 `TABBED`/单内容布局更贴近实际观感；`COMBO` 只在同一工具窗口内需要多视图切换（如“项目”内的多视图）时才体现差异。

### 6.4 内容与窗口生命周期的联动

- **【可直接实现】** 内容增删后 `rebuild()`；当内容清空且满足条件时隐藏工具窗口：
  - `isToHideOnEmptyContent()` 为真 → 隐藏并**从 stripe 移除**（`removeFromStripe=true`）；
  - 否则若 `canCloseContents()` 且空文本为默认 → 隐藏但**保留 stripe 按钮**；
  - 其他情况不隐藏。
  （来源：ToolWindowContentUi.java:210-232）
- **【可直接实现】** 内容为空时若 `isToHideOnEmptyContent()` 为真，会把窗口重新设为可用（来源：ToolWindowContentUi.java:195-197）。
- **【可直接实现】** 内容区数据上下文会导出 `TOOL_WINDOW`、`HELP_ID`、`PROJECT`、关闭目标，以及（仅 TABBED）`SELECTED_CONTENT_TAB_LABEL`（来源：ToolWindowContentUi.java:774-788）。

---

## 7. Augit 可执行要点速查

按 Augit 现有界面结构（左侧顶部「项目/提交/搜索」切换、左侧底部工具区「终端/Git 历史」、中间只读标签）整理可直接落地的规则：

1. **【可直接实现】** 左侧栏是"工具窗口容器"：同时最多显示一个上部窗口（项目/提交）与一个下部分组窗口（终端/Git 历史），两者关系等价于 IntelliJ 的 `isSplit=false/true` 分组。
2. **【可直接实现】** 上/下两组之间放 24×1 的分隔线，整段占位 32×11，空出时隐藏（对应 `StripeButtonSeparator`）。
3. **【可直接实现】** 按钮选中态 = 蓝底白图标；可见但未选中 = pressed 背景 + pressed 描边；悬停 = hover 背景 + hover 描边；普通 = 无底色（对应 §3.3）。
4. **【可直接实现】** header 高度 41；标题左内距 12、右内距 16；动作按钮区左内距 12、右内距 8；header 动作按钮默认只在悬停/窗口激活时出现。
5. **【可直接实现】** 侧栏宽度：Augit 现为 300–360（来源：web/src/mockup.css:44）→ 若要贴合 IntelliJ，宽度应由 `weight × 框架宽` 得出，并允许拖动（范围 40–100 逻辑像素、最小 33）；注意 V2 默认布局给 `Project`/`Commit`/`Structure` 的 weight 是 **0.25**（而通用默认值是 0.33），开启"显示工具窗口名称"时宽度固定为 59。
6. **【可直接实现】** 侧栏显示/隐藏必须成组：隐藏整侧就是"隐藏该侧全部窗口"（对应 `hideSide=true`），`HideAll` 需要能一键隐藏并一键恢复快照。
7. **【需推断】** 底部区域高度：Augit 用 `clamp(180px, 31vh, 305px)`（来源：web/src/mockup.css:46），IntelliJ 用 weight（默认 0.33×窗口高）。两者语义不同，需按 PyCharm 观感二选一后固化。

---

## 8. 未找到证据 / 需复核实清单

| 项 | 结论 |
| --- | --- |
| 「拖动时清除未完成的滚轮输入」 | 在本 commit 的相关目录中**未找到**任何 `MouseWheelEvent` 处理；仅有「取消未完成的 resize 回调」（ToolWindowPane.kt:333-354）与「忽略点击触发的比例设置」（ResizeStripeManager.kt:184-191）。需在其他模块或更新版本核实 |
| `ToolWindowProperty.AVAILABLE` 的触发点 | 仓库内无调用；可用性变化走 `toolWindowAvailable`/`toolWindowUnavailable` |
| PyCharm 2026.2.1 的默认主题 | 无法从源码判定默认是 Light/Dark(expUI) 还是 Islands；两者 stripe 按钮几何不同（40×40 vs 37×40） |
| `StripeToolbar.Button.size` 等键的 expUI 覆盖 | expUI Light/Dark 未覆盖，取 JBUI 默认值；不排除产品级主题文件另设 |
| `ToolWindowLayoutProfileMigrationHelper` 的「多种档案」 | 迁移助手只有一套版本化逻辑；「档案种类」体现在 `ProjectFrameToolWindowLayoutService` bean 与 `ToolWindowLayoutProfileProvider` 扩展两个来源，以及 `SEED_ONLY`/`FORCE_ONCE` 两种应用模式 |
| `ide.new.tool.window.resize.area.height` | 注册表值 13，代码内兜底默认 14，范围 1..26；实际生效值应为 13 |
