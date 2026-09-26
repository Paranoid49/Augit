# 07 主题切换、DPI 缩放、弹层与对话框状态机

本节要点：

1. **主题切换 = 换色板与绘制委托，不换几何**：`UIManager.setLookAndFeel(theme)` 之后对全部窗口做 `updateComponentTreeUI`，随后 `updateUI()` 依次跑 `patchLafFonts → applyDensityOnUpdateUi → patchHiDPI` 把尺寸重新归一化。窗口边界、组件树结构、展开/选中状态、每个主题记住的编辑器配色都被保留；被替换的只有颜色、图标映射、边框/UI 委托类与字体默认值。
2. **「跟随系统深色」是「注册表/桌面属性拉取 + 事件推送」两段式**：Windows 下读 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`，`== 0` 判为深色；变化由 AWT 桌面属性 `win.lightTheme.on` 触发，且始终 `invokeLater` 到 UI 线程。检测不出结果（异常/不支持）时**不切换**，并按「非深色」处理。
3. **缩放有三个独立来源**：`USR_SCALE`（IDE 缩放，默认由「界面字号 ÷ 12」导出）、`SYS_SCALE`（显示器 DPI）、`OBJ_SCALE`（单个组件）。派生量 `PIX_SCALE = USR × OBJ × DEV`，其中 `DEV` 只在 JRE 托管 HiDPI 下等于 `SYS`。用户缩放被量化到 **0.25 的整数倍**。
4. **非整数缩放下 `PaintUtil.alignToInt(value, ctx, RoundingMode, ParityMode)` 是唯一正确的对齐手段**：它先排除用户缩放、把用户空间值换算到设备空间取整、再换算回用户空间。起点用 `FLOOR`、终点用 `CEIL`；**`FLOOR + ODD` 对 < 1 的值会产出负数（0.5 → -1）**，这是最容易踩的边界。
5. **弹层的关闭通路是 4 组可独立开关的判据**（Esc、点击外部、窗口失焦/停用、与模态窗口及打开动作的联动），默认全开；`cancel()` 是取消，`closeOk()` 是「ok=true 的取消」，关闭原因通过 `onClosed(LightweightWindowEvent.isOk())` 回传。模态对话框在 Windows 上 **Enter 触发当前聚焦的按钮**，不是默认按钮。

分类标记约定：

- **【可直接实现】** 静态取值、公式、状态机，可直接用 HTML/CSS/JS 或 C# 表达。
- **【Swing 特有】** 依赖 Swing / IntelliJ 平台组件模型或绘制管线，Web 层只能等价重写语义，不能照抄 API。
- **【需推断】** 源码未直接给出、需要按截图或试验补齐的部分。

取证锚点：`/mnt/d/github/intellij-community`，commit `576e328`，`build.txt` = `263.SNAPSHOT`（即 PyCharm 2026.2 平台代次）。文中「来源」路径均相对该仓库根目录。几何与色值总表另见 `docs/intellij-platform-ui-reference.md`，本节只记录规则、状态与时序。

---

## 1. 主题切换机制

### 1.1 三态与「跟随系统深色」

- **【可直接实现】** 「跟随系统深色」由两层状态组成：一个布尔开关 `autodetect` + 两个「首选主题 id」（`preferredDarkThemeId` / `preferredLightThemeId`）。三态映射为：浅色 = `autodetect=false` 且主题为浅色；深色 = `autodetect=false` 且主题为深色；跟随系统 = `autodetect=true`，当前主题由系统状态在深/浅两个首选主题之间切换。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:184-198,386-391,936-955）
- **【可直接实现】** 打开 `autodetect` 时立刻记住当前编辑器配色（`rememberSchemeForLaf`），随后触发一次同步；关闭时不回滚主题，只停止跟随。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:938-955）
- **【可直接实现】** 跟随状态只在「平台支持检测」时可用：`getAutodetectSupported() = detector.detectionSupported`；不支持时 UI 层隐藏「同步系统主题」选项。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:957；platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:227-249）
- **【可直接实现】** Windows 检测规则：注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` 的 `AppsUseLightTheme` 值为 `0` → 深色，其他 → 浅色；**读取抛异常时返回「非深色」**（即未知按浅色处理）。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/SystemDarkThemeDetector.kt:146-168）
- **【可直接实现】** 平台分派：macOS → `MacOSDetector`、Windows 10+ → `WindowsDetector`、Linux → `LinuxThemeDetector`、其他 → 恒为浅色且不支持检测的 `EmptyDetector`。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/SystemDarkThemeDetector.kt:36-43,192-197）
- **【Swing 特有】** 检测器必须在 `LafManagerImpl` 初始化阶段就创建（不能按需创建），因为构造时就要挂系统监听器；`autodetect=true` 会强制提前创建。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:194-195,408-411,959-967）
- **【可直接实现】** 未保存过任何状态时（首次启动），默认主题在 Windows 高对比度模式下取 `JetBrainsHighContrastTheme`，否则取**深色默认主题**，且 `autodetect=false`。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:458-466,618-636）

### 1.2 系统主题变化的传播时序

- **【可直接实现】** Windows 的触发点是 AWT 桌面属性 `win.lightTheme.on` 的 `PropertyChangeEvent`：`newValue != true` 判为深色，且**必须 `invokeLater`（`ModalityState.any()`）**后才回调同步函数，即「先入队、后应用」，不会在属性回调里同步改 UI。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/SystemDarkThemeDetector.kt:155-159）
- **【可直接实现】** 主动检测是异步的：`check()` 在协程里跑 `isDark()`，取不到结果（`null`）就直接返回、不做任何切换；拿到结果后再切到 UI 线程 + `ModalityState.any()` 执行同步。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/SystemDarkThemeDetector.kt:60-72）
- **【可直接实现】** 同步判定条件（决定「这一次是否需要换主题」）：设 `currentIsDark = 当前主题为空 或 当前主题 isDark`，`expectedTheme = 深色 ? (首选深色 ?: 默认深色) : (首选浅色 ?: 默认浅色)`；**只有 `currentIsDark != systemIsDark` 或 `currentTheme !== expectedTheme` 时才切换**。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:315-331）
- **【可直接实现】** 跟随模式下切换主题是**异步**的（`async=true` → `SwingUtilities.invokeLater`），编辑器配色同步也是 `invokeLater`，且只在 `baseName` 不同才换。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:329,348-353）
- **【Swing 特有】** 主题切换动画默认关闭，开启条件是注册表 `ide.intellij.laf.enable.animation`：开启时先对每个可见窗口截图并贴到 `JLayeredPane.DRAG_LAYER`，切换完成后再淡出；动画是 `Animator("ChangeLAF", 总帧 60, 时长 800ms, 不重复)`，alpha 曲线 `1 - (1 - cos(π·frame/totalFrames))/2`。（来源：platform/platform-impl/src/com/intellij/ide/actions/ChangeLAFAnimator.java:26-46,66-101）
- **【Swing 特有】** 「悬停即预览主题」只在注册表 `ide.instant.theme.switch` 开启时生效：主题列表选中项变化后延迟 **500ms** 才换主题（拖动选择不会狂换），弹层关闭时取消该定时器；若弹层是「非 ok 关闭」（Esc），则**回滚到打开弹层时的主题**。（来源：platform/platform-impl/src/com/intellij/ide/actions/QuickChangeLookAndFeel.java:227-267）

### 1.3 切换时「重建什么、保留什么」

切换的完整调用链与顺序：

1. **【Swing 特有】** `setCurrentLookAndFeel()` 本身**不改组件层级**（源码注释明确写出）；它只：记住当前编辑器配色 → dispose 旧主题对象 → `doSetLaF()` → 更新 `currentTheme` → 更新主题下拉模型 → 必要时换编辑器配色 → `UISettings.fireUISettingsChanged()` → `ActionToolbarImpl.updateAllToolbarsImmediately()` → 计算是否需要重启。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:638-672）
2. **【Swing 特有】** `doSetLaF()` 里：清空 SVG `colorPatcherProvider` 与 `selectionColorPatcherProvider`（图标配色补丁必须重建），用「预热的基础 LaF + 主题」构造 `LookAndFeelThemeAdapter`，然后 `UIManager.setLookAndFeel(adapter)`；若需要安装编辑器配色则同时 `theme.installEditorScheme(...)`。失败则弹错误对话框并返回。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:711-746）
3. **【Swing 特有】** `LookAndFeelThemeAdapter.getDefaults()` = 基础 LaF 的 defaults + `initBaseLaF()` + `theme.installTheme(defaults)` + 若干修正；`isNativeLookAndFeel()` 恒为 `true`。也就是说**所有主题共用同一个 Swing 基础 LaF，主题只往 defaults 里灌值**。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LookAndFeelThemeAdapter.kt:54-74,90-92）
4. **【可直接实现】** 主题 JSON 的 `ui` 段是「任意 UIDefaults 键 → 值」的字典，值可以是颜色、数字、字符串、图片路径（`.png`/`.svg`）、边框类名（`*Border`）、UI 类名（`*UI`）、`AllIcons.*` 反射图标；末尾带 `Width`/`Height` 的键会被解析成数字。（来源：platform/platform-impl/src/com/intellij/ide/ui/uiThemeParser.kt:22-65）
5. **【Swing 特有】** 颜色值会被包装成「带名字的 JBColor（`IJColorUIResource`）」，因此**换主题时按名字查色可以拿到新值**，而早期创建的 `JBColor` 实例也能跟随；`*.<后缀>` 形式的键是通配补丁，会更新所有以该后缀结尾的既有键。（来源：platform/platform-impl/src/com/intellij/ide/ui/UITheme.kt:74-123；platform/platform-impl/src/com/intellij/ide/ui/laf/LookAndFeelThemeAdapter.kt:54-74）
6. **【Swing 特有】** 真正的「重建组件」发生在 `updateUI()`：`notifyLookAndFeelChanged()` 广播事件，然后对 **`Frame.getFrames()` 里的每个窗口**递归 `IJSwingUtilities.updateComponentTreeUI`，并继续处理 `window.getOwnedWindows()`（因此被主窗口拥有的模态设置对话框也会一起重建）。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:841-848,1443-1448）
7. **【Swing 特有】** 避免重复 `updateUI`：切主题的 updater 会临时订阅 `LafManagerListener.TOPIC`，若 `DarculaInstaller.install()/uninstall()` 已经触发过一次 `lookAndFeelChanged`，就不再手动 `updateUI()`。旧版 Darcula ↔ 其他主题之间需要额外的安装/卸载步骤。（来源：platform/platform-impl/src/com/intellij/ide/actions/QuickChangeLookAndFeel.java:285-309）
8. **【Swing 特有】** 切主题后会做一次「重启检查」：欢迎页不检查；两个主题都不要求重启则不提示；两个主题都要求重启但属于同一主题组也不提示；否则在切换动作结束后弹重启对话框。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:674-703）

**保留项（切主题不能丢）**：

- 窗口/对话框尺寸与位置（尺寸服务按 key 存的是内容尺寸，切主题不写入也不清空）。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:2259-2263；platform/platform-impl/src/com/intellij/openapi/ui/impl/DialogWrapperPeerImpl.java:946-966）
- 组件树结构与展开/选中状态（`updateComponentTreeUI` 只换 UI 委托与字体，不重建模型对象）。
- 「每个主题上次用的编辑器配色」映射 `lafToPreviousScheme`（含 2023.2 起导入的名字键兼容）。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:200-202,987-1013）
- 用户自定义字体设置：`storedDefaults[themeId]` 缓存每个主题原始字体 defaults，切换时先存后改，不会把已改过的值当成原始值再缩放一次。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:899-924）

**重建项**：UI 委托（Button/Checkbox/Tab/ScrollBar…）、图标路径映射（New UI 走 `expui/` 前缀补丁）、边框绘制类、`JBColor` 颜色值、字体默认值、工具栏动作按钮。（来源：platform/platform-impl/src/com/intellij/ui/ExperimentalUIImpl.kt:98-126；platform/platform-impl/src/com/intellij/ide/ui/laf/LookAndFeelThemeAdapter.kt:54-74）

### 1.4 切换主题时尺寸不变性的依据（Augit 硬要求）

- **【可直接实现】** 官方 New UI 的主题 JSON 里**确实带有几何数值键**（`expUI_light` / `expUI_dark` / `expUI_light_with_light_header` 与 islands 组的多个变体都覆盖了同一批键）：`Button.arc=8`、`Component.arc=8`、`CheckBox.iconSize=24`、`Component.arrowAreaWidth=28`、`List.rowHeight=24`、`Tree.rowHeight=24`、`TabbedPane.tabHeight=40`、`EditorTabs.underlineHeight=4`、`EditorTabs.underlineArc=4`、`TabbedPane.tabSelectionArc=4`、`Shortcut.backgroundOpacity=0`、`Popup.Advertiser.fontSizeOffset=-1` 等。（来源：platform/platform-resources/src/themes/expUI/expUI_dark.theme.json `ui` 段；platform/platform-resources/src/themes/expUI/expUI_light.theme.json `ui` 段）
- **【可直接实现】** 逐键比对 `expUI_light.theme.json` 与 `expUI_dark.theme.json` 的数值型 UI 键：两边的键集合与几何取值完全一致，**仅有的差异全是颜色/透明度系数**（`Code.Inline.backgroundOpacity`、`Code.Block.backgroundOpacity`、`EditorTabs.unselectedBlend`、`Breakpoint.iconHoverAlpha`、`VersionControl.Log.Graph.brightness` 等），另有一个布尔键 `Tree.forceFocusedSelectionForeground` 只在浅色出现。（来源：platform/platform-resources/src/themes/expUI/expUI_light.theme.json 与 expUI_dark.theme.json 的 `ui` 段逐键比对）
- **【可直接实现】** 结论规则：**Augit 要把「尺寸/间距/圆角/行高」放在与主题无关的另一层（等价于 UI defaults 之外的常量表），主题文件只允许出现颜色与透明度**。若主题文件里放了尺寸，虽然平台允许（`ui` 段任意值），但会直接违反「切主题不改尺寸」的要求。（来源：platform/platform-impl/src/com/intellij/ide/ui/uiThemeParser.kt:22-65 说明值类型不受限；expUI 主题比对给出正向样例）
- **【可直接实现】** 平台自己也没把尺寸完全抽离主题，所以它用 `patchHiDPI` 在每次 `updateUI` 后**统一重算**会被字号影响的尺寸键（`List.rowHeight`、`Table.rowHeight`、`Tree.rowHeight`、VCS Log 行高、`Tree.leftChildIndent`、`Tree.rightChildIndent`、`SettingsTree.rowHeight`、`Slider.*Size`、以及所有 `*.maxGutterIconWidth`、所有 `UIResource` 的 `Dimension`/`Insets`），并用哨兵键 `hidpi.scaleFactor` 记录上次用过的缩放，避免重复叠加。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1334-1392）
- **【Swing 特有】** 行高补丁的具体算法：`new = scale(round(old / prevScale))`，即「先反算回无缩放值，再按当前缩放重算」；若某键在主题里本来就 ≤ 0，则记 warning 并置 0（`patchRowHeight`）。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1384-1392）
- **【Swing 特有】** 密度（compact / default）是**独立于主题**的开关，并且函数在「密度名未变」时**直接 return**，注释明确写着「重复应用同一密度会破坏像 Tree.rowHeight 这类可缩放值」。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1460-1489）
- **【Swing 特有】** 手动应用密度时的顺序很关键：先把 IDE 缩放**临时置 1** → 触发一次 `UISettingsChanged` → 重新设置同一主题 → `updateUI()` → 恢复真实缩放 → 再触发一次。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:748-763）
- **【可直接实现 / 需推断】** 主题切换**不改变**窗口尺寸、控件尺寸、间距与顺序——这是平台通过「两套主题几何一致 + patchHiDPI 归一化」间接保证的，而不是由某段代码显式断言。Augit 应在实现里加一条自动化校验：对同一界面在浅/深主题下截图并逐元素比对 `getBoundingClientRect()`，作为回归测试。（来源：推断自 §1.3 的重建清单与 §1.4 的主题比对）

### 1.5 主题切换时的抗锯齿与输入映射

- **【Swing 特有】** `updateUI()` 每次都会重设 `RenderingHints.KEY_TEXT_ANTIALIASING`、`KEY_TEXT_LCD_CONTRAST`、`KEY_FRACTIONALMETRICS` 三个桌面提示，并写入 `laf.scaleFactor = scale(1f)`（MigLayout 的逻辑像素依赖它）。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:817-848）
- **【Swing 特有】** `initInputMapDefaults()` 会重建 Tree/TextArea/TextField/PasswordField/Table 的快捷键表（含剪切/复制/粘贴的简化键与标准键），因此主题切换不会丢掉编辑快捷键。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LookAndFeelThemeAdapter.kt:104-105,214-256）
- **【Swing 特有】** Windows 下弹层强制使用**重量级（heavyweight）**窗口（`idea.popup.weight` 未指定时；macOS 也是 heavyweight），目的是消除绘制残影并提速。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1398-1433）

---

## 2. DPI 缩放

### 2.1 三个缩放源与派生缩放

- **【可直接实现】** `ScaleType` 只有三个枚举值，语义如下：（来源：platform/util/ui/src/com/intellij/ui/scale/Scale.kt:44-90）
  - `USR_SCALE`：由 IDE 管理，**当前由「界面字号设置」导出**；IDE 托管 HiDPI 模式下它包含默认系统缩放，JRE 托管 HiDPI 模式下它与系统缩放无关、默认字号时为 1.0。默认字号变化时两种模式下都按比例变化。
  - `SYS_SCALE`：由设备 DPI 或系统设置决定；多显示器下每个 GraphicsConfiguration 各有一个，另有「默认设备」的一份。
  - `OBJ_SCALE`：单个 UI 对象附加的缩放，只影响该对象，默认 1.0。
- **【可直接实现】** 派生类型只有三个：（来源：platform/util/ui/src/com/intellij/ui/scale/DerivedScaleType.java:14-40；platform/util/ui/src/com/intellij/ui/scale/ScaleContext.kt:120-136）
  - `EFF_USR_SCALE = USR × OBJ`；
  - `DEV_SCALE = JRE 托管 HiDPI ? SYS : 1.0`；
  - `PIX_SCALE = DEV × USR × OBJ`（`ScaleContext.derivePixScale = getScale(DEV_SCALE) * super.derivePixScale()`，而 `UserScaleContext.derivePixScale = USR × OBJ`）。
- **【可直接实现】** 缩放源是按「作用范围」选的，不要混用：**矢量绘制**用 `UserScaleContext`（不含设备缩放）；**位图/栅格绘制**用 `ScaleContext`（含设备缩放）。源码注释直接写明这一分工。（来源：platform/util/ui/src/com/intellij/ui/scale/UserScaleContext.java:15-23；platform/util/ui/src/com/intellij/ui/scale/ScaleContext.kt:12-19）
- **【可直接实现】** `ScaleContext` 的构造来源有五种：空（默认设备 + 当前用户缩放）、`Component`、`GraphicsConfiguration`、`Graphics2D`、单个 `Scale`。由 `Component` 创建的上下文会持弱引用，并在 `update()` 时**同时**刷新 `USR_SCALE`（取全局 `JBUIScale.userScale`）与 `SYS_SCALE`（取该组件当前 GraphicsConfiguration）。（来源：platform/util/ui/src/com/intellij/ui/scale/ScaleContext.kt:20-79,81-151）
- **【可直接实现】** `overrideScale(scale)` 会**永久钉住**某个缩放（之后 `update()` 不再改它），用于让某控件与用户缩放解耦（例如按设备像素绘制的小图标）。（来源：platform/util/ui/src/com/intellij/ui/scale/UserScaleContext.java:74-103,129-149）
- **【可直接实现】** 缓存失效的判据是 **`PIX_SCALE` 是否变化**（不是 `USR_SCALE`），因此换显示器（SYS 变）会正确让按 DPI 生成的位图缓存失效。（来源：platform/util/ui/src/com/intellij/ui/scale/ScaleContext.kt:216-231）
- **【Swing 特有】** 模式判定：JRE 托管 HiDPI 在 macOS、Wayland 上恒开启；Windows/Linux 上要求 `hidpi` 系统属性为真**且**运行在 JetBrains JVM **且** `sun.java2d.SunGraphicsEnvironment.isUIScaleEnabled()` 为真。（来源：platform/util/ui/src/com/intellij/ui/JreHiDpiUtil.kt:44-82）

### 2.2 用户缩放的计算规则

- **【可直接实现】** 缩放量化分辨率固定为 **0.25**：`discreteScale(s) = round(s / 0.25) × 0.25`（注册表 `ide.scale.discrete.take.floor` 为真时改成向下取整）。因此**不存在 1.1 或 1.3 这样的用户缩放**；`padStart`-style 的非量化值只可能来自调试属性 `ide.ui.scale`。（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:37,284-301,490-495）
- **【可直接实现】** 用户缩放默认等于系统缩放（IDE 托管 HiDPI），JRE 托管 HiDPI 下默认 1.0；若用户字号改变，`patchLafFonts` 会用 `fontSize / DEF_SYSTEM_FONT_SIZE` 直接设置用户缩放。（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:303-347；platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:857-885）
- **【可直接实现】** 系统缩放来源：IDE 托管 HiDPI 下 = `系统字体大小 / DEF_SYSTEM_FONT_SIZE`；JRE 托管 HiDPI 下 = `gc.defaultTransform.scaleX`。基准 `DEF_SYSTEM_FONT_SIZE` 默认 **12**。（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:105-108,113,220-241,520-530）
- **【可直接实现】** Windows 的系统字体取自桌面属性 `win.messagebox.font`，源码注释说明它**自带缩放**；macOS 硬编码 13pt / `.SF NS Text`；Linux 若桌面属性 `gnome.Xft/DPI` 可用则反推出真实基准字号。（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:138-198）
- **【可直接实现】** 兜底规则：`hidpi=false` 时系统缩放与用户缩放都强制为 1；注册表 `ide.scale.below.100.only.fonts` 为真且系统缩放 ≥ 1 时，小于 1 的用户缩放被抬回 1.0；Linux 且基准字号为 12 时，1.25 被特判为 1.0。（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:222-225,325-347）
- **【需推断】** 96 / 120 / 144 DPI 在 JRE 托管 HiDPI 下分别对应 SYS = 1.0 / 1.25 / 1.5（120/96=1.25、144/96=1.5），此时 1.25 是**真正的分数缩放**（不是被量化到 1.25 的 0.25 倍数那种巧合）。（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:497-505,520-530）

### 2.3 `alignToInt(value, ctx, RoundingMode, ParityMode)` 的完整语义

参数与取值：（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:47-107）

- `RoundingMode`：`FLOOR`（`floor`）、`CEIL`（`ceil`）、`ROUND`（`Math.round`）、`ROUND_FLOOR_BIAS`（`ceil(v - 0.5)`，即 .5 向下）。
- `ParityMode`：`EVEN` / `ODD`，另有 `of(int)`（按值奇偶）与 `invert(pm)`。

算法（逐步）：（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:163-173）

1. `rm == null` → 用 `ROUND`；`scale == 0` → 直接返回 0。
2. **scale 不是「用户缩放」**：`getScale(ctx) = PIX_SCALE / USR_SCALE`，即等于 `OBJ_SCALE × DEV_SCALE`。用户缩放被有意排除，因为传入的 `usrValue` 按约定**已经乘过用户缩放**。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:34-42,251-258）
3. 设备空间整数取值：`devValue = rm.round(usrValue × scale)`；**但如果要求对齐奇偶（`pm != null`）且 `rm == ROUND`，这里改用 `FLOOR`**。
4. 若取整结果的奇偶与 `pm` 不符：`rm == FLOOR` 时 **-1**，否则 **+1**。
5. 返回 `devValue / scale`（回到用户空间的双精度值）。

由此得到四条必须记住的推论：

- **【可直接实现】** `FLOOR + ODD` 的效果是「取不超过该值的最大奇数」，**对任意 `0 ≤ v < 1` 的值会得到 -1**（例：0.5 → floor 0 → 奇数不符 → -1；1.2 → floor 1 → 1）。把它用在非负坐标上会产生负坐标，这是该 API 最主要的陷阱。
- **【可直接实现】** `ROUND + ODD` / `ROUND + EVEN` **不是「就近取奇/偶」**：因为起点被强制 `FLOOR`，结果只会向上补偿，例如 `ROUND + EVEN` 等价于「不小于 v 的最小偶数」（2.1 → 2；1.9 → 2；1.1 → 2），而 d 形如 javadoc 所说的「2.1 floor'd to odd 1、1.9 round'ed to odd 1」只在 `pm = ODD` 且 floor 已是奇数时成立。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:150-173）
- **【可直接实现】** `CEIL + ODD` = 不小于 v 的最小奇数；`FLOOR + EVEN` = 不大于 v 的最大偶数。
- **【可直接实现】** `getParityMode(usrValue, ctx, rm)` 单独可用：按同样的「排除用户缩放」规则把值转到设备空间取整，再返回奇偶。典型用法是「先求笔画中心该落在奇数还是偶数设备像素，再让两端各自对齐」。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:109-127）

配套 API：

- **【可直接实现】** `devValue(usrValue, ctx) = usrValue × scale`（不取整）；`devPixel(g) = 1 / devValue(1, g)` 求「一设备像素等于多少用户空间单位」，但它是用 `ROUND` 后的整数缩放做近似（1.25 → 1.0、2.0 → 0.5），**非整数缩放下不精确**。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:225-249）
- **【Swing 特有】** `alignTxToInt(g, offset, alignX, alignY, rm)` 只在「有分数缩放的平移、且无旋转」时把 `Graphics2D` 的平移量对齐到整数设备坐标，并把原 transform 返回以便还原；`alignClipToInt(...)` 同理对齐 clip 的 x/y/width/height（x/y 一个 rounding、w/h 另一个）。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:270-298,362-393）
- **【可直接实现】** `isFractionalScale(tx)` 判据是 `scaleX != (int)scaleX || scaleY != (int)scaleY`；`getUserSpacePixelOffset(g)` 返回「当前 transform 原点最近的物理像素距离」除以缩放（用户空间），**仅在存在分数缩放且无旋转时非 null**（Windows 分数缩放是典型场景），其 javadoc 给出 150% 下返回 (0.333, 0.333) 的例子。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:300-350,452-456）

### 2.4 `alignIntToInt` 与非 0.25 倍数缩放

- **【可直接实现】** `alignIntToInt(int usrValue, ctx, rm, pm)` 只接受 `FLOOR` / `CEIL`（其他传入抛 `IllegalArgumentException`），返回「在用户空间与设备空间同时都是整数」的整数：从原值出发按 `rm` 方向逐 1 试探，最多试探 4 次（要求奇偶时 8 次）；超过就**原样返回**，注释明确说明「缩放不是 0.25 的倍数时无法对齐」。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:175-209）
- **【可直接实现】** 负值直接原样返回（循环条件含 `result >= 0`）；「已对齐」判定用 `|round(scaled) - scaled| < 0.0001`。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:194,203-209）

### 2.5 `JBUIScale.scale` 与 `JBUI.scale` 的差别

| 方面 | `JBUIScale.scale` | `JBUI.scale` |
| --- | --- | --- |
| 定义位置 | `object JBUIScale`（真实现） | `class JBUI`（外观层适配） |
| `scale(float)` | `value × 用户缩放`，返回 Float，**不取整** | 已 `@Deprecated`，直接转发给 `JBUIScale.scale(float)` |
| `scale(int)` | `(用户缩放 × i).roundToInt()` | 转发给 `JBUIScale.scale(int)`，语义相同 |
| 其他 | `scaleIcon`、`scaleFontSize`、`isHiDPI`、`isUsrHiDPI`、`sysScale(…)`、`pixScale(…)`、`getFontScale` | 额外提供 `unscale(i)`、`JBValue` 工厂（`value/uiIntValue/size/insets`）、`CurrentTheme` 取值入口 |

（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:387-427；platform/util/ui/src/com/intellij/util/ui/JBUI.java:79-103）

- **【可直接实现】** 关键差别是**取整时机**：`scale(int)` 四舍五入到整数；`scale(float)` 保留小数。要对齐到物理像素时必须用 `PaintUtil.alignToInt`/`alignIntToInt`，**不能**依赖 `scale(int)`——1px 边框在 1.25 缩放下 `round` 仍是 1，在 1.5 下变 2，在 1.75 下也变 2，直接破坏对齐关系。
- **【可直接实现】** 字号缩放有特例：`scaleFontSize(size, userScale)` 在用户缩放为 1.25 时乘 **1.34**、为 1.75 时乘 **1.67**、其他情况乘用户缩放，最后 `.toInt()`（**截断，不是四舍五入**）。这是为了让字号落在整数点上。（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:414-427）
- **【可直接实现】** 屏幕 DPI 转缩放：`getScreenScale() = discreteScale(screenResolution / 96)`（headless 时按 96）。（来源：platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:497-505）

### 2.6 哪些尺寸必须缩放、哪些必须取整到物理像素

- **【可直接实现】** 必须按 DPI 缩放（`JBUIScale.scale` / `JBUI`）：所有 Insets、Dimension、行高、圆角半径（arc）、图标尺寸、固定列宽、组件最小/首选尺寸、分隔线粗细。平台的做法是统一在 defaults 里放 `UIResource` 的 `Dimension`/`Insets` 并在 `patchHiDPI` 里批量缩放。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1349-1381）
- **【可直接实现】** 必须取整对齐物理像素：1px 描边/边框矩形的两端、分隔线、树形缩进引导线、聚焦轮廓、表格网格线。规则是「**起点 `FLOOR`、终点 `CEIL`**（保证线宽不少于 1 个设备像素）或「先求 `getParityMode` 再让两端对称」。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:150-173,300-346）
- **【可直接实现】** 行高有兜底：`Tree` 默认行高 = `JBUIScale.scale(24)`；`UIManager` 里该键取值 `<= 0` 时**回退到默认值**（注释：Linux 平台不支持 rowHeight）。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:2516-2528）
- **【可直接实现】** 内置主题给的 New UI 行高就是无缩放的 24：`List.rowHeight = 24`、`Tree.rowHeight = 24`。（来源：platform/platform-resources/src/themes/expUI/expUI_dark.theme.json `ui` 段）
- **【Swing 特有】** 位图资源必须按「像素缩放」而不是「用户缩放」加载，`JBUIScale.scaleIcon` 与 `UIUtil.drawImage` 是配套入口。（来源：platform/util/ui/src/com/intellij/ui/scale/Scale.kt:30-40,123；platform/util/ui/src/com/intellij/ui/scale/JBUIScale.kt:403-412）
- **【可直接实现】** Augit 映射建议：CSS 里所有间距用 `px`（= 用户空间逻辑像素），让 WebView2 自己处理 devicePixelRatio；只有「1 物理像素线」需要显式处理（`border-width: 1px` 在 125% 下会落在半个设备像素上）。等价做法是：线宽 = `1 / devicePixelRatio`，坐标用 `Math.floor` 而不是 `Math.round`。**不要**在 JS 里对布局尺寸做 `Math.round`，那才会真正破坏不同 DPI 下的一致性。（来源：推断自 PaintUtil 的 FLOOR/CEIL 组合与 `scale(int)` 的 `roundToInt`）

---

## 3. 界面字体与字号

### 3.1 一次 `updateUI` 里字体是怎么被改写的

- **【Swing 特有】** `patchLafFonts` 是唯一入口，三条分支：（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:857-885）
  1. 用户勾选了「覆盖默认字体」**或** IDE 缩放 ≠ 1：先 `storeOriginalFontDefaults` 存原始值，取字体名（用户设置或默认字体族）与字号（`用户字号或默认字号 × IDE 缩放`），调 `initFontDefaults`，再用 `getFontScale(fontSize)` 设置用户缩放；
  2. 否则若使用 Inter 字体（New UI + JetBrains JVM，且语言不是中日韩）：同样 `initFontDefaults`，用户缩放用默认字体的比例；
  3. 否则 `restoreOriginalFontDefaults`：把每个 patch 过的字体键还原成该主题的原始值，并把用户缩放设回 `getFontScale(JBFont.label().size)`。
- **【可直接实现】** `initFontDefaults` 把**一份 UI 字体实例**写进 `patchableFontResources` 里的**全部 34 个字体键**（Button、ToggleButton、RadioButton、CheckBox、ColorChooser、ComboBox、Label、List、MenuBar、MenuItem、MenuItem.acceleratorFont、RadioButtonMenuItem、CheckBoxMenuItem、Menu、PopupMenu、OptionPane、Panel、ProgressBar、ScrollPane、Viewport、TabbedPane、Table、TableHeader、TextField、FormattedTextField、Spinner、TitledBorder、ToolBar、ToolTip、Tree 等）；随后 4 个键被专门值覆盖：`PasswordField.font`、`TextPane.font`、`EditorPane.font` 用 UI 字体，`TextArea.font` 用等宽字体。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1503-1530）
- **【可直接实现】** 同一函数里**等宽字体只有一处**：`TextArea.font = Monospaced(字号与 UI 字号相同)`；`PasswordField.font` 与 `TextPane.font` / `EditorPane.font` 用 UI 字体本身。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1519-1530）
- **【可直接实现】** 因此 Augit 的四项独立设置与平台的对应关系是：**界面字体/界面字号** = `UISettings.fontFace` / `fontSize2D`（作用于整套 UI defaults）；**编辑器等宽字体/字号**是编辑器配色方案的一部分（`EditorColorsScheme`），不在 LaF 字体补丁里；LaF 里的 `TextArea.font` 只是等宽回退。**「界面字号」通过用户缩放间接影响所有尺寸**，「等宽字号」不影响布局尺寸。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1519-1530；platform/editor-ui-api/src/com/intellij/ide/ui/UISettings.kt:418-456；推断部分已标注）
- **【Swing 特有】** 中/日/韩语言环境下强制回退到系统字体，不使用 Inter。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1435-1441）
- **【Swing 特有】** New UI + JetBrains JVM 且注册表 `ide.experimental.ui.inter.font` 为真时，会**强行取消**用户的「覆盖默认字体」勾选。（来源：platform/platform-impl/src/com/intellij/ui/ExperimentalUIImpl.kt:191-198）

### 3.2 字号变化影响哪些尺寸、何时重新度量

- **【可直接实现】** 字号改变 → `patchLafFonts` 重算用户缩放 → `setUserScaleFactor` → 之后 `patchHiDPI` 按新缩放重算所有 `UIResource` 的 `Dimension`/`Insets`、行高、缩进与滑块尺寸。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:857-885,1334-1382）
- **【Swing 特有】** 字号与设置变更都通过 `UISettingsListener` 汇总：`addListeners` 里比较 `computeValuesOfUsedUiOptions()`（`overrideLafFonts`、`fontFace`、`fontSize2D`、IDE/编辑器抗锯齿类型、…）与上次快照，**只有值真的变了才 `updateUI()`**。这是「字号改了但没触发全局重算」这类 bug 的防线。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:277-299,1532-1539）
- **【Swing 特有】** `UISettings.fireUISettingsChanged()` **只允许在 EDT 调用**；非 EDT 会记 error 并改走 `invokeLater`。（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettings.kt:769-797）
- **【可直接实现】** 字号允许列表是固定的一组整数串：**8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72**；输入框可编辑，非法或 ≤ 0 的输入回退到原值（`getIntValue`：非数字或 ≤0 → 返回 `defaultValue`）。（来源：platform/util/ui/src/com/intellij/util/ui/UIUtil.java:266-270,1276-1280；platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:766-791）
- **【需推断】** Augit 要求字号范围 9–40px，与平台的 8–72 列表不重合；平台没有「硬上限」，只有 ≥ 1 的有效性判断与上面的下拉建议值。若 Augit 要在 40 封顶，需要在输入校验里自己实现（平台无对应规则）。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:785-791）

### 3.3 `JBUIScale.Fonts` 的角色划分

平台没有 `JBUIScale.Fonts` 这个类（**需核实**该命名是否来自 PyCharm 文档）；实际的角色划分在 `JBFont` 与 `LafManagerImpl`：

- **【可直接实现】** `JBFont.label()` = 从 `UIManager.getFont("Label.font")` 取 UI 基准字体；`create(font, tryToScale=true)` 会把字号乘上用户缩放。（来源：platform/util/ui/src/com/intellij/util/ui/JBFont.java:68-100）
- **【可直接实现】** 角色派生全部基于基准字体、并且增减量**也乘用户缩放**：`h0 = label +12 加粗`、`h1 = +9 加粗`、`h2 = +5`、`h3 = +3`、`h4 = +1 加粗`、`medium = label -1`、`small = label -2`。（来源：platform/util/ui/src/com/intellij/util/ui/JBFont.java:150-176）
- **【可直接实现】** Windows 且**非 New UI** 时，`medium` 与 `small` 都退化成 `regular`（即不做减号）；New UI 下 `small` 仍建议尽量用 `medium` 代替。（来源：platform/util/ui/src/com/intellij/util/ui/JBFont.java:174-189）
- **【Swing 特有】** `JBFont` 是 `Font` 子类但内部持有「缩放后字体」，`equals` 比较缩放后实例；派生的 `deriveFont` 会先刷新缩放再改字号。Web 层只需保留「角色 → 相对字号的 offset」表，不需要这个类。（来源：platform/util/ui/src/com/intellij/util/ui/JBFont.java:60-67,114-148）

---

## 4. 弹层（Popup）状态机

### 4.1 状态枚举与生命周期

- **【可直接实现】** 状态机只有 6 个状态：`NEW → INIT → SHOWING → SHOWN → CANCEL → DISPOSE`。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:376-385）
- **【可直接实现】** 合法迁移与守卫：（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:404-563,1074-1132,2164-2221）
  - `init()` 把所有参数落定，末态设为 `INIT`；`requestFocus=true` 与 `focusable=false` 同时传是**非法组合**，会被强制改成 `requestFocus=false` 并记 error。
  - `cancel()`：若已在 `CANCEL`/`DISPOSE` 直接返回；进入 `CANCEL` 后先问 `canClose()`，**被拒绝则回退到 `SHOWN`**；否则存尺寸/位置 →（若是鼠标事件）吞掉下一个事件 → `popup.hide(false)` → 通知栈派发器 → `disposePopup()` → 触发 `onClosed` → dispose。
  - `dispose()`：若当前是 `SHOWN` 会先 `cancel()`（注释：已显示的弹层必须先取消）；`DISPOSE` 再进入直接返回；清空内容、监听、鼠标外取消器，最后在焦点稳定后执行 `finalRunnable`。
- **【Swing 特有】** `cancel()` 无参版本会自动从事件队列里取「真实当前事件」，**只有该事件的组件确实位于弹层窗口内**时才把它作为取消事件传递（用于精确吞掉那一次点击）。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1052-1063,1103-1105）

### 4.2 `cancel` / `closeOk` / `hide` 的区别

- **【可直接实现】** `closeOk(e)` = `setOk(true)` + 把 `okHandler` 拼到 `finalRunnable` 前 + `cancel(e)`。即**「确认关闭」与「取消关闭」走完全相同的清理路径，唯一区别是 `myOk` 标志**。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1044-1050,2917-2929）
- **【可直接实现】** 关闭原因通过 `JBPopupListener.onClosed(new LightweightWindowEvent(this, myOk))` 广播，监听者用 `isOk()` 判定；`finalRunnable`（含 `okHandler`）在弹层 dispose 后、焦点稳定时才执行。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1117,2208-2216）
- **【可直接实现】** Balloon 侧同构：`hide()` = `hide(false)`、`hide(true)` = 「ok」、`dispose()` = `hide(false)`、`hideImmediately()` 关掉动画再关；同样以 `onClosed(..., ok)` 回传。（来源：platform/platform-impl/src/com/intellij/ui/BalloonImpl.java:1139-1210）
- **【可直接实现】** 一个易忽略的守卫：**智能淡出被暂停时 `hide()` 会被直接忽略**（`isSmartFadeoutPaused` 分支立即返回），这是通知悬停暂停的实现基础。（来源：platform/platform-impl/src/com/intellij/ui/BalloonImpl.java:1160-1171）

### 4.3 四条关闭通路

**（A）Esc 关闭**

- **【可直接实现】** `Esc` 的识别不是硬编码按键：`isCloseRequest(e)` 依次查当前键盘映射里 `IdeActions.ACTION_EDITOR_ESCAPE` 的快捷键，要求是「单段键盘快捷键」且修饰键完全一致；查不到映射表时回退为「`VK_ESCAPE` 且无修饰键」。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:2967-2986）
- **【可直接实现】** 命中 Esc 时的优先级：先给自定义 `keyEventHandler`；然后要求 `cancelKeyEnabled`（默认 **true**）且当前没有正在输入 speed-search；如果**根组件里还有一个正在过滤的 speed search，先清空它而不是关弹层**，否则 `cancel(e)`。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:2942-2958）
- **【Swing 特有】** Esc 的分发由栈派发器统一处理：无弹层直接返回；若当前聚焦窗口是模态 Dialog 且弹层不在该窗口内，则不处理。（来源：platform/platform-impl/src/com/intellij/ui/popup/StackingPopupDispatcherImpl.java:168-180）

**（B）点击外部关闭**

- **【可直接实现】** 组件级弹层的默认值是 `cancelOnClickOutside = true`。（来源：platform/platform-impl/src/com/intellij/ui/popup/ComponentPopupBuilderImpl.java:55）
- **【可直接实现】** 判定优先级（每次 `MOUSE_PRESSED`）：点击落在弹层内容区内 → 不关；`isCancelOnClickOutside()` 为 false → 不关；`canClose()` 为 false → 不关；下拉菜单（`MenuSelectionManager` 有选中路径）→ 不关；否则 `cancel(mouseEvent)`。（来源：platform/platform-impl/src/com/intellij/ui/popup/StackingPopupDispatcherImpl.java:106-166）
- **【可直接实现】** **点击外部会从栈顶往下依次关闭整条弹层链**，而不是只关最上面一层（循环体在 cancel 后继续取 `myStack.peek()`）；只有 `needStopFurtherEventProcessing`（事件需要被转发给下层组件）为真才提前结束。（来源：platform/platform-impl/src/com/intellij/ui/popup/StackingPopupDispatcherImpl.java:88-166）
- **【可直接实现】** 弹层自身还装了一个鼠标监听器做「内容区 + 2px 容差」的二次判定：`bounds` 各向外扩 2px，点在容差外则 `cancel()`。也就是说**内容区外 2px 内仍算弹层内部**，避免贴边点击误关。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1893-1910）
- **【可直接实现】** Wayland 下无法取得屏幕坐标，改为「事件窗口不是弹层窗口就关闭」的粗略判定。（来源：platform/platform-impl/src/com/intellij/ui/popup/StackingPopupDispatcherImpl.java:127-136；platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:474）

**（C）窗口失焦 / 停用关闭**

- **【可直接实现】** `isCancelNeeded(event, popupWindow)` 规则：事件窗口为空或弹层窗口为空 → 关；事件窗口是弹层的后代 → 不关；**弹层不可聚焦且弹层是事件窗口的后代 → 不关**；显式调用过 `setForceCancelOnFocusLoss(true)` → 关；Wayland 下当事件窗口等于弹层 owner 时 → 不关（拖动窗口时的临时焦点转移）；其余 → 关。对应触发事件是 `WINDOW_ACTIVATED` 与 `WINDOW_GAINED_FOCUS`。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:2440-2471,3061-3083）
- **【可直接实现】** 开关：`cancelOnWindowDeactivation` **默认 true**（组件弹层），触发 `WINDOW_ACTIVATED/GAINED_FOCUS` 后会 `invokeLater(cancel())`，即**延迟到下一个事件循环**再关，避免在事件处理中间销毁窗口。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:255,2391-2398,2444-2450；platform/platform-impl/src/com/intellij/ui/popup/ComponentPopupBuilderImpl.java:56）
- **【可直接实现】** 「鼠标移出后关闭」是另一条可选通路：`cancelOnMouseOutCallback(checker)` 借助全局 AWT 事件监听，只有**曾经进入过**弹层（`myEverEntered`）之后，`MOUSE_MOVED`/`MOUSE_PRESSED` 落在弹层外且 `checker` 返回 true 才关闭。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:2400-2471）

**（D）模态窗口与打开动作的联动**

- **【可直接实现】** `canClose()` 的完整判据：无「保持弹层打开的模态窗口」**且** `callback` 为 null 或返回 true **且** 不处于「刚打开就关闭」窗口期；或者已经 disposed。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1134-1142）
- **【可直接实现】** 「保持弹层打开」的模态窗口 = 带 `DialogWrapper.KEEP_POPUPS_OPEN` 客户端属性的窗口；`DialogWrapper` 会把它设到自己的窗口上。典型场景：「从历史记录粘贴」由弹层发起、弹层需要活下来。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1144-1146；platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:1685-1704,1919-1922）
- **【可直接实现】** 「刚打开就关闭」保护只针对 X.Org 的已知 bug：仅当 `UIUtil.isXServerOnWindows()`、弹层是普通窗口层级、当前事件是 `WINDOW_DEACTIVATED`、且距离开放时间 **< 1000ms** 时才阻止关闭。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1170-1177）

### 4.4 弹层栈与父子关系

- **【可直接实现】** 只有「入栈」的弹层（`inStack=true`）进栈；持久弹层单独记一份；所有弹层都进一个弱引用全集，用于 `getChildPopups(component)`（沿 owner 链判断归属）。（来源：platform/platform-impl/src/com/intellij/ui/popup/StackingPopupDispatcherImpl.java:34-80；platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:3025-3040）
- **【可直接实现】** 子弹层点击穿透的判定：如果鼠标事件的窗口不是弹层窗口、但**是弹层窗口的后代**，则事件不处理（视为在弹层内部）。（来源：platform/platform-impl/src/com/intellij/ui/popup/StackingPopupDispatcherImpl.java:113-117）
- **【Swing 特有】** 打开新弹层时会把「弹出时刻的模态实体数组」记下来（`modalEntitiesWhenShown`），之后判断「是否有新模态窗口盖住弹层」只需比较该数组之后新增的部分，因此**弹层显示前就存在的模态窗口不会导致它关闭**。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1148-1168,1108）

### 4.5 边框、圆角、阴影参数

- **【可直接实现】** 一般弹层边框：宽度 = `Popup.borderWidth`（默认 **1**），画在**内侧**（`StrokeType.INSIDE`）；颜色 = `Popup.borderColor`（激活）/ `Popup.inactiveBorderColor`（非激活）；非弹层用途时宽度固定为 1。边框不透明度为 opaque，insets = `ceil(borderWidth)`。（来源：platform/util/ui/src/com/intellij/ui/PopupBorder.java:86-107；platform/util/ui/src/com/intellij/util/ui/JBUI.java:1583-1591）
- **【可直接实现】** macOS + 有阴影窗口时**不画**弹层边框，除非 UI default `Popup.paintBorder` 为真；expUI 深色主题显式设了 `Popup.paintBorder = true`（浅色主题没有该键）。Windows 不受此例外影响。（来源：platform/util/ui/src/com/intellij/ui/PopupBorder.java:38-43；platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:510-525）
- **【可直接实现】** 弹层标题栏：高度 = `scale(28)`（有控件）/ `scale(24)`（无控件）；insets 默认 `(12,10,10,10)`；背景 `Popup.Header.activeBackground` / `inactiveBackground`，前景 `activeForeground` / `inactiveForeground`。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1557-1579）
- **【可直接实现】** 弹层内搜索框：外边框 insets 默认 `(4,12)`、输入 insets 默认 `(4,8,8,2)`，底部一条 1px `Popup.separatorColor` 分隔线；分隔线默认 insets `(4,8)`、分隔标签 insets `(3,16)`。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1593-1627；platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1948-1967）
- **【可直接实现】** 列表/菜单选中项圆角：`Popup.Selection.arc` 默认 8、`Menu.Selection.arc` 默认 8、`PopupMenu.Selection.arc` 默认 8、`Tree.Selection.arc` 默认 8。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:1640,1664,1679,2505）
- **【可直接实现】** `Ide.Shadow`（用于弹层/无边框窗口/玻璃面板对话框）与 `Notification.Shadow`（用于通知气泡）的取值（expUI 深色，浅色仅颜色不同）：（来源：platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:410-447；platform/platform-resources/src/themes/expUI/expUI_light.theme.json:438-472）

  | 组 | borderInsets | 各边/角 0 层色 | 各边/角 1 层色 |
  | --- | --- | --- | --- |
  | `Ide.Shadow` | `16,16,16,16` | `#00000000`（浅色 `#FFFFFF00`） | `#00000020`（浅色 `#00000010`） |
  | `Notification.Shadow` | `5,5,5,5` | `#00000000`（浅色 `#FFFFFF00`） | `#00000010`（浅色 `#80808010`） |

- **【可直接实现】** 阴影的绘制模型：四个边是「0 层色 → 1 层色」的线性渐变（上边从上到下、下边从下到上、左边从左到右、右边从右到左），四个角是 45° 对角渐变；`arc > 0` 时先在四个角各画一个 `arc × arc` 的纯色方块再叠渐变；`hideSide(topCorners, bottom)` 可去掉上角或整个底边（用于贴在屏幕上/下边缘的通知）；`borderColor != null` 时最后再描一个 1px 的矩形。（来源：platform/platform-impl/src/com/intellij/ui/ShadowJava2DPainter.kt:13-14,20-120）
- **【可直接实现】** 阴影默认值（主题未提供时）：insets = 5（`Notification.Shadow.borderInsets` 的默认）、颜色 = `Gray._200` 透明度 50。（来源：platform/platform-impl/src/com/intellij/ui/ShadowJava2DPainter.kt:13-14,26-31）
- **【可直接实现】** 通知气泡圆角：`Notification.arc` 默认 **12**；New UI 的通知阴影画笔 `arc = scale(6)`、insets 取 `Notification.Shadow.borderInsets`。`hideTopCorners` 时左侧渐变的起点会下移 `scale(7)`。（来源：platform/platform-impl/src/com/intellij/ui/NotificationBalloonRoundShadowBorderProvider.kt:31-58；platform/platform-impl/src/com/intellij/ui/ShadowJava2DPainter.kt:83-88）
- **【可直接实现】** 玻璃面板式对话框把 `Ide.Shadow` 的 insets 直接当成透明窗口的外边距（默认 16px 四边），在中间放内容面板；另有 30% 黑色描边变体用于无系统阴影的场合。（来源：platform/platform-impl/src/com/intellij/openapi/ui/impl/GlassPaneDialogWrapperPeer.java:378-390；platform/platform-impl/src/com/intellij/openapi/ui/impl/ShadowBorderPainter.java:26-31）
- **【可直接实现】** 弹出菜单的阴影是**窗口级**的（`WindowShadowMode.NORMAL / DISABLED`），由 `showShadow` 决定（组件弹层默认 **true**）。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1994-1996；platform/platform-impl/src/com/intellij/ui/popup/ComponentPopupBuilderImpl.java:81）

### 4.6 关闭动画与延时

- **【可直接实现】** 一般弹层（菜单/下拉/组件弹层）**没有关闭动画**：`cancel()` 直接 `hide` + dispose。唯一的动画是可选的主题切换淡出（§1.2）。
- **【可直接实现】** 通知气泡有淡入/淡出动画：`Animator(总帧 8, 时长 = animationCycle)`，不可重复；通知构造时把周期设为 **200ms**，气泡构建器默认是 **500ms**；远程桌面会话下动画整体禁用。（来源：platform/platform-impl/src/com/intellij/ui/BalloonImpl.java:1003-1043,2320-2327；platform/platform-impl/src/com/intellij/notification/impl/NotificationsManagerImpl.java:799-812；platform/platform-impl/src/com/intellij/ui/popup/BalloonPopupBuilderImpl.java:44）
- **【可直接实现】** 动画时长为 0 时直接跳到最后一帧只画一次（等价于无动画）。（来源：platform/platform-api/src/com/intellij/util/ui/Animator.kt:150-160）
- **【可直接实现】** 气泡自动消失由两个定时器组成：`startFadeoutTimer(delay)` 一次性触发 → `hide(true)`；`startSmartFadeoutTimer(delay)` 多了「应用失活时暂停」的能力——失活时取消定时器并把剩余时间记在 `mySmartFadeoutDelay`，下一次 AWT 活动事件到达时用剩余时间重新起表。（来源：platform/platform-impl/src/com/intellij/ui/BalloonImpl.java:182-188,1062-1091）
- **【可直接实现】** 气泡默认**不自动消失**（构建器的 `fadeoutTime = -1`，`startFadeoutTimer` 只在 delay > 0 时生效）。（来源：platform/platform-impl/src/com/intellij/ui/popup/BalloonPopupBuilderImpl.java:36,130；platform/platform-impl/src/com/intellij/ui/BalloonImpl.java:1079-1091）
- **【Swing 特有】** 通知位置的重新布局有 **200ms 去抖**（`relayoutRequests.debounce(200ms)`），窗口缩放时不会每帧重排。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutImpl.kt:86-97,245-247）
- **【Swing 特有】** 气泡的自动隐藏默认行为：构建器默认 `hideOnMouseOutside=true`、`hideOnKeyOutside=true`、`hideOnAction=true`、`hideOnFrameResize=true`、`hideOnCloseClick=true`、`closeOnClick=false`；通知场景显式把它们设成传入的 `hideOnClickOutside`（常规通知路径传的是 **false**）。（来源：platform/platform-impl/src/com/intellij/ui/popup/BalloonPopupBuilderImpl.java:34-64；platform/platform-impl/src/com/intellij/notification/impl/NotificationsManagerImpl.java:799-812,431）

---

## 5. 对话框与模态（`DialogWrapper` / `Messages`）

### 5.1 按钮排列（Windows）

- **【可直接实现】** 默认动作集合 = `[OK, Cancel]`，有 `helpId` 时追加 `Help`；左侧动作集合默认为空。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:1234-1243）
- **【可直接实现】** 布局：先放左侧按钮组，然后一个 `weightx=1` 的弹性空隙把右侧按钮组推到最右，**右侧按钮按 `createActions()` 的数组顺序从左到右排列**（即 Windows 上 OK 在左、Cancel 在右）；只有 `myButtonAlignment == CENTER` 且没有「不再询问」复选框时才在右边再加一个弹性空隙。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:787-812）
- **【可直接实现】** 「居中按钮」已被废弃，源码注释明确：**对话框动作按钮应当右对齐**。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:1653-1660）
- **【可直接实现】** 两组按钮之间固定留 **20px** 间隙；同组内相邻按钮间隙 = **12px − 该按钮自身的左右 insets**（`BASE_BUTTON_GAP = 12`），即视觉间距恒为 12px 而不是盒模型间距。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:796,856-878）
- **【可直接实现】** 动作面板边框：默认只有上边距 **8px**；`COMPACT` 样式则改为「1px 顶部分隔线 + `empty(8,12)`」（Windows + JetBrains JVM 下分隔线颜色取 `CustomFrameDecorations.separatorForeground`）。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:628-645,840-847）
- **【可直接实现】** 对话框内容区默认边框 = `(top/bottom 8, left/right 12)` 的空边框。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:233-235；platform/util/ui/src/com/intellij/util/ui/UIUtil.java:368-375,1250-1252）
- **【可直接实现】** 「不再询问」复选框放在左下角，右边距 20px；帮助按钮（若被移到左侧）放在最左。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:814-838）
- **【可直接实现】** 消息对话框（`Messages` / `MessageDialogBuilder`）**不做平台相关的按钮重排**，按调用方给的 `options` 顺序生成：`yesNo` = `[Yes, No]`、`yesNoCancel` = `[Yes, No, Cancel]`、`okCancel` = `[OK, Cancel]`、输入对话框 = `[OK, Cancel]` 且 `OK` 是 index 0 的默认按钮。因此 Windows 下 Yes 在左、No 在右、Cancel 最右。（来源：platform/ide-core/src/com/intellij/openapi/ui/MessageDialogBuilder.kt:120-130,152-167,216-223；platform/platform-api/src/com/intellij/openapi/ui/messages/MessageDialog.java:167-197；platform/platform-api/src/com/intellij/openapi/ui/Messages.java:1305-1336）
- **【可直接实现】** `MessageDialog` 的 `CreateActions` 只对 `defaultOptionIndex` 打 `DEFAULT_ACTION`、对 `focusedOptionIndex` 打 `FOCUSED_ACTION`，**默认按钮与初始聚焦按钮可以是两个不同的按钮**。（来源：platform/platform-api/src/com/intellij/openapi/ui/messages/MessageDialog.java:174-191）

### 5.2 默认按钮、Enter/Esc 与焦点

- **【可直接实现】** 默认按钮由动作上的 `DEFAULT_ACTION` 决定，`OkAction` 自带该标记 → `rootPane.setDefaultButton(button)`。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:965-969,2100-2106）
- **【可直接实现】** **Windows（以及开启相应注册表的 Linux）上 `Enter` 触发的不是默认按钮，而是当前拥有焦点的按钮**：平台装了一个 ENTER 快捷键动作，`actionPerformed` 里对 `KeyboardFocusManager.getFocusOwner()` 若为可用 `JButton` 就 `doClick()`；该动作在焦点不在按钮上时自身不可用。这是 PyCharm 在 Windows 上的实际行为，Augit 必须照做。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:1537-1539,1551-1572）
- **【可直接实现】** `Esc` → 取消动作（除非 `createCancelAction()` 返回 null）；`F1` 与「上下文帮助」快捷键 → 帮助动作（`ide.remove.help.button.from.dialogs` 或进度对话框时禁用）；`←`/`→` 在按钮之间移动焦点（循环、跳过禁用按钮）；`Y`/`N` 只在该按钮助记符是 Y/N、文本恰为 `Yes`/`No` 时才绑定。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:1961-1987,2004-2019,907-917）
- **【可直接实现】** Esc 的取消动作会先询问 `PopupUtil.handleEscKeyEvent()`：**如果当前有弹层，Esc 先关弹层，不触发对话框取消**。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:1992-1998）
- **【可直接实现】** 助记符解析：`&` 或 `_` 后一个字符作为助记符（大小写不敏感），支持 `&&`/`__` 转义；文本里会去掉这两个前缀字符。动作上的 `Action.MNEMONIC_KEY` 优先于文本解析结果。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:955-1000）
- **【Swing 特有】** 首次获得窗口焦点时决定初始焦点：优先 `getPreferredFocusedComponent()`，否则**默认按钮**；两者都会把鼠标指针移到默认按钮上；若组件不可用则不聚焦。（来源：platform/platform-impl/src/com/intellij/openapi/ui/impl/DialogWrapperPeerImpl.java:1195-1224）
- **【Swing 特有】** 对话框关闭后把焦点还给打开前的组件（`componentToRestoreFocus.requestFocus()`，在 `show()` 返回后执行）。（来源：platform/platform-impl/src/com/intellij/openapi/ui/impl/DialogWrapperPeerImpl.java:599-607）
- **【Swing 特有】** 按钮点击有一次重入保护：如果对话框已关闭或正在执行动作，`fireActionPerformed` 直接丢弃该次点击。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:934-948）

### 5.3 校验失败时的表现

- **【可直接实现】** 点击 OK 时：先 `doValidateAll()`；若返回非空，取**第一个** `ValidationInfo`，若它带可见组件就把焦点移到该组件，然后刷新错误信息并重新开始连续校验；**只要列表中还有 `okEnabled == false` 的项就直接 return，不关闭对话框**。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:2108-2127）
- **【可直接实现】** 连续校验：默认开启，进入时 `invokeLater` 启动，之后在 `myValidationDelay` 间隔上循环执行 `updateErrorInfo(doValidateAll())` 并再次排程；线程可选 Swing 线程或后台线程，Swing 线程时用对话框的 `ModalityState`。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:1584-1623）
- **【可直接实现】** 错误信息的两个落点：（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:1434-1435,2201-2250）
  - **整体错误**：对话框底部按钮行上方的一条文本（`myErrorText`，`BorderLayout.CENTER`）。默认隐藏；显示时最小高度参与实际尺寸计算。
  - **组件级错误**：给组件装 `ComponentValidator`，在其上打客户端属性 `"JComponent.outline" = "error" | "warning"`（由组件 UI 画红/黄描边），并在组件获得焦点时弹出一个提示气泡。
- **【可直接实现】** 校验信息列表是按值比较的：与上次完全相同就整体跳过；被移除的项会清掉对应组件的 outline。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:2224-2239）
- **【可直接实现】** 错误/警告文本颜色：错误 → `NamedColorUtil.getErrorForeground()`，警告 → `MessageType.WARNING.getTitleForeground()`。（来源：platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:2274-2282）
- **【可直接实现】** 校验提示气泡样式：HTML 内容、错误/警告各自的背景与边框色、**`setCancelOnClickOutside(false)`**（点外部不关）、有阴影、不可聚焦。（来源：platform/platform-api/src/com/intellij/openapi/ui/ComponentValidator.java:307-343）
- **【可直接实现】** 输入对话框（`Messages.InputDialog`）的校验时机更早：文档监听器在每次文本变化时重新判定 OK 是否可用，并可在输入框上直接显示错误文本；`doOKAction` 里再次用 `checkInput && canClose` 双闸。（来源：platform/platform-api/src/com/intellij/openapi/ui/Messages.java:1305-1344）

### 5.4 模态

- **【可直接实现】** `IdeModalityType` 映射到 AWT 模态类型后设置到 `JDialog`；`setModal/isModal` 直接转发给 `JDialog`。（来源：platform/platform-impl/src/com/intellij/openapi/ui/impl/DialogWrapperPeerImpl.java:245-270,406-413）
- **【可直接实现】** **平台没有实现「模态遮罩/变暗层」**：全仓库检索没有把 owner 窗口变暗的 painter 或 overlay（`IdeGlassPaneImpl` 的 painter 只有加载指示与窗口阴影；`ModalityHelper` 只是反射读 `Window.isModalBlocked`）。因此 PyCharm 的模态阻挡靠 AWT 模态本身，背景窗口**不变暗**。（来源：platform/platform-impl/src/com/intellij/openapi/wm/impl/IdeGlassPaneImpl.kt:199-208,539-549；platform/platform-impl/src/com/intellij/openapi/wm/impl/ModalityHelper.java:14-70）
- **【需推断】** 上一条与常见「模态遮罩」预期相反；Augit 若已按截图实现变暗遮罩，需要与实际 PyCharm 截图复核后决定是否保留（本节倾向“无遮罩”）。（来源：对上述代码的推断）
- **【可直接实现】** 模态期间弹出的弹层默认会被关闭，除非该模态对话框显式声明「保持弹层打开」。（来源：platform/platform-impl/src/com/intellij/ui/popup/AbstractPopup.java:1134-1146；platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java:1685-1704）
- **【Swing 特有】** 模态窗口显示/隐藏时会瞬时修改事件队列的模态栈（`leaveModal()` 等）并在 finally 中恢复；macOS 上还会临时隐藏持久弹层、对话框关闭后恢复。（来源：platform/platform-impl/src/com/intellij/openapi/ui/impl/DialogWrapperPeerImpl.java:518,575-582,613-620）

---

## 6. 通知

### 6.1 显示位置与堆叠

- **【可直接实现】** 位置枚举四个角（`TOP_RIGHT` / `TOP_LEFT` / `BOTTOM_RIGHT` / `BOTTOM_LEFT`），**默认 `BOTTOM_RIGHT`**；欢迎页的通知被强制为 `BOTTOM_RIGHT`（从右下角向上堆叠）。（来源：platform/ide-core/src/com/intellij/notification/NotificationLocation.kt:10-25；platform/platform-impl/src/com/intellij/ui/BalloonLayoutImpl.kt:270-276）
- **【可直接实现】** 同时可见数量上限 = 注册表 `ide.notification.visible.count`，**默认 2**；新增时若已达上限，**移除最早的一个**（FIFO，不是最久未查看）。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutImpl.kt:48-49,150-157）
- **【可直接实现】** 同一 `groupId` 的通知会**合并（merge）**：新气泡插入到被合并者原来的位置，而不是追加到末尾。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutImpl.kt:158-199）
- **【可直接实现】** 水平锚点：左对齐时 `x = 窗口左缘 + 4`，右对齐时 `x = 窗口右缘 - 4`，再按左右方向各偏移 ±4；New UI 下若存在工具窗口面板还要加上面板的 x 偏移。垂直起点：顶部时 `insets.top + scale(10)`，底部时从窗口底边向上扣掉工具窗口底栏与状态栏高度。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutImpl.kt:260-332）
- **【可直接实现】** 同列内气泡**首尾相接**排列（底部对齐时每个气泡的 y 依次减去自身高度），不额外加间隙——视觉上的间隔来自 `Notification.Shadow` 的 5px 阴影内边距。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutImpl.kt:334-355）
- **【可直接实现】** 高度不够时**按列**分栏：逐列累加高度，超过可用高度就开新的一列；若分出的列数 > 1，则从最旧的气泡开始移除直到只剩一列（即**不会真的显示两列**，而是丢弃最旧的）。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutImpl.kt:260-269,357-373）
- **【可直接实现】** 气泡宽度是**固定值**：`FixedWidth = scale(360 + Ide 阴影左右宽)`，另有 `MaxWidth = FixedWidth - scale(60)`、`MaxFullContentWidth = scale(350)`、`MinWidth = scale(100)`；正文宽度上限另受 `scale(600)` 与宿主窗口宽度 −20 的约束（取较小者），超出则加水平滚动条。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutConfiguration.java:39-73；platform/platform-impl/src/com/intellij/notification/impl/NotificationsManagerImpl.java:786-793）
- **【可直接实现】** 两行/三行布局切换：若「有标题 + 有正文 + 有动作」用 `treeLines()`（图标 10×7、上下留白 7/3/7、底 8）；否则若正文在「标题或动作占 1 行」时的高度放不下也用 `treeLines()`；其余用 `twoLines()`（图标 10×11、留白 11/5/5、底 14）。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutConfiguration.java:75-114）
- **【可直接实现】** New UI 的图标区宽 = `scale(32)`、动作间距 = `scale(16)`、右上动作偏移 `(9,9)`（旧 UI 是 `(8,6)`），这些值可被 UI default `Notification.iconOffsetSize` 覆盖（宽高会被交换使用）。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutConfiguration.java:105-157）

### 6.2 自动消失与悬停暂停

- **【可直接实现】** 展示类型决定生命周期：`NONE` 不弹；`BALLOON` 自动消失（文档写 10 秒）；`STICKY_BALLOON` 必须用户关闭；`TOOL_WINDOW` 进工具窗口且工具窗口不可用时降级为 `BALLOON`。（来源：platform/ide-core/src/com/intellij/notification/NotificationDisplayType.java:8-14；platform/platform-impl/src/com/intellij/notification/impl/NotificationsManagerImpl.java:268-308）
- **【可直接实现】** 实际延时是**硬编码常量**：`STICKY_BALLOON → 300000ms`，其他 → **10000ms**；并且**只有应用处于激活状态才开始计时**（应用未激活时挂一个激活监听，激活后再起表）。（来源：platform/platform-impl/src/com/intellij/notification/impl/NotificationsManagerImpl.java:446-476）
- **【可直接实现】** 悬停暂停：鼠标进入气泡（`MOUSE_MOVED` 且 inside 状态翻转）时**取消定时器并记下已消耗时间**（`myFadeoutRequestDelay -= now - requestMillis`）；鼠标离开且剩余时间 > 0 时**用剩余时间重新起表**。前提是气泡启用了按钮（通知场景成立，因为设置了关闭按钮）。（来源：platform/platform-impl/src/com/intellij/ui/BalloonImpl.java:228-241）
- **【可直接实现】** 应用失活时同样暂停并把剩余时间搬到 `mySmartFadeoutDelay`，下次任意 AWT 活动事件到达时续上。（来源：platform/platform-impl/src/com/intellij/ui/BalloonImpl.java:182-188,1062-1077）
- **【可直接实现】** 「更多」按钮展开内容期间通过 `runWithSmartFadeoutPause` 暂停；若期间未被重新暂停，则关闭气泡。（来源：platform/platform-impl/src/com/intellij/ui/BalloonImpl.java:1045-1060；platform/platform-impl/src/com/intellij/ui/NotificationBalloonActionProvider.java:79-90）
- **【可直接实现】** 通知的 `expire()` / 用户点关闭的区分：监听 `onClosed`，`!isOk()`（即用户主动关）时让通知过期，从而从事件日志里移除。（来源：platform/platform-impl/src/com/intellij/notification/impl/NotificationsManagerImpl.java:293-307）
- **【可直接实现】** 全局开关 `SHOW_BALLOONS`（默认 true）关掉时**不显示任何气泡**（但仍可能进事件日志）。（来源：platform/platform-impl/src/com/intellij/notification/impl/NotificationsConfigurationImpl.java:44,158,223-224）

### 6.3 动作按钮

- **【可直接实现】** 气泡右侧固定两个动作位：**「更多/齿轮」在左、关闭在右**；布局按 `closeOffset = beforeCloseSpace + 关闭图标宽 + rightActionsOffset.width`、`allActionsOffset = closeOffset + afterGearSpace + 齿轮图标宽` 计算，New UI 下 `beforeCloseSpace = scale(5)`、`afterGearSpace = scale(7)`、`beforeGearSpace = scale(15)`。（来源：platform/platform-impl/src/com/intellij/ui/BalloonLayoutConfiguration.java:105-157）
- **【可直接实现】** 关闭按钮的命中区比图标大：`CloseHoverBounds = (5, 5, 12, 10)`，按钮边界向左上各扩 5、宽高各扩 12/10，因此**贴边也能点到**。（来源：platform/platform-impl/src/com/intellij/ui/NotificationBalloonActionProvider.java:59,240-254）
- **【可直接实现】** 通知正文里的动作默认渲染成链接（HTML），点击后走 `Notification.fire(notification, action, context)`，并在数据上下文里带上 `Notification.KEY`。（来源：platform/platform-impl/src/com/intellij/notification/impl/NotificationsManagerImpl.java:322-376）
- **【可直接实现】** 编辑器有焦点时，气泡内动作快捷键由 `HintManagerImpl.isActionToIgnore` 过滤（某些动作不触发气泡关闭）。（来源：platform/platform-impl/src/com/intellij/ui/BalloonImpl.java:705-715）

### 6.4 信息 / 警告 / 错误的视觉差异

- **【可直接实现】** 三种类型各有一组（图标、背景色、标题前景色、边框色），默认取自主题键：（来源：platform/ide-core/src/com/intellij/openapi/ui/MessageType.java:14-27；platform/util/ui/src/com/intellij/util/ui/JBUI.java:2334-2360）

  | 类型 | 背景键 / 默认（浅 / 深） | 边框键 / 默认（浅 / 深） |
  | --- | --- | --- |
  | ERROR | `Notification.ToolWindow.errorBackground` / `0xffcccc` / `0x704745` | `Notification.ToolWindow.errorBorderColor` / `0xd69696` / `0x998a8a` |
  | INFO | `Notification.ToolWindow.informativeBackground` / `0xbaeeba` / `0x33412E` | `Notification.ToolWindow.informativeBorderColor` / `0xa0bf9d` / `0x85997a` |
  | WARNING | `Notification.ToolWindow.warningBackground` / `0xf9f78e` / `0x5a5221` | `Notification.ToolWindow.warningBorderColor` / `0xbab824` / `0xa69f63` |

  前景色三者默认都取 `Tooltip.FOREGROUND`（`Notification.ToolWindow.{error,informative,warning}Foreground` 的默认值）。（来源：platform/util/ui/src/com/intellij/util/ui/JBUI.java:2334-2374）
- **【可直接实现】** expUI 深色主题覆盖了这三个键，使三者前景一致、只有背景/边框区分：`errorForeground/warningForeground/informativeForeground = Gray12`，`errorBackground = Red3`、`warningBackground = Yellow1`、`informativeBackground = Gray3`，边框分别为 `Red4` / `Yellow2` / `Gray4`。（来源：platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:465-476）
- **【可直接实现】** 通知气泡本身的默认填充/边框来自另一组键：`Notification.background`（浅 `Gray._242` / 深 `0x4E5052`）、`Notification.borderColor`（浅 `0xCDB2B2B2` / 深 `0xCD565A5C`）、正文色 `Notification.foreground`（浅黑 / 深 `Gray._191`）；expUI 深色覆盖为 `background = Gray3`、`borderColor = Gray4`、`foreground = Gray13`、`linkForeground = Blue9`。（来源：platform/platform-impl/src/com/intellij/notification/impl/NotificationsManagerImpl.java:148-150,524-532；platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:449-464）
- **【可直接实现】** 多个通知的「主导类型」判定：出现 ERROR 立刻返回 ERROR；否则记住第一个 WARNING；否则 INFORMATION。（来源：platform/ide-core/src/com/intellij/notification/NotificationType.java:15-24）
- **【可直接实现】** 通知气泡体**没有阴影边框**（`setShadow(false)` + `setBorderInsets(empty)`），阴影完全由 `Notification.Shadow` 的透明内边距 + 圆角画笔绘制。（来源：platform/platform-impl/src/com/intellij/notification/impl/NotificationsManagerImpl.java:799-818）

---

## 7. 设置界面的状态

### 7.1 「立即生效」与「点应用后生效」的分界

| 设置项 | 生效时机 | 依据 |
| --- | --- | --- |
| 主题（外观下拉框） | **立即**（选中即切，异步） | `lafProperty.afterChange` → `invokeLater { QuickChangeLookAndFeel.switchLafAndUpdateUI(..., async=true) }`，随后做重启检查。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:214-220） |
| 「同步系统主题」勾选 | **立即** | `syncThemeProperty.afterChange { lafManager.autodetect = it }`。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:221-224） |
| IDE 缩放百分比 | **立即** | `onChanged` 里校验通过就写 `settings.ideScale` 并 `invokeLater { fireUISettingsChanged() }`。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:302-330） |
| 界面字体名 / 字号 / 「覆盖默认字体」 | **应用后生效** | 走 DSL 的 `bind`/`bindSelected`，没有 `afterChange`；由对话框的 OK/Apply 触发 `apply()`。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:335-373,719-736） |
| 紧凑模式 | **应用后生效**，且额外调 `applyDensity()` | `checkBox(...).bindSelected(...).onApply { LafManager.getInstance().applyDensity() }`。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:450-457） |
| 抗锯齿类型 | **应用后生效**，且应用时重建全部组件的抗锯齿设置 | `onApply` 遍历所有窗口的所有 `JComponent` 重设 AA 信息。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:689-699） |
| 颜色盲模式 | **应用后生效**，且延后到「所有改动都应用完」再重载配色 | 注释明确指出回调是逐组件执行的，所以要 `invokeLater` 后统一 reload。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:395-421） |
| 编辑器配色方案 | 可修改性受「同步系统主题」控制；应用时 `apply()` | `enabledIf(syncThemeAndEditorSchemePredicate.not())`。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:261-286） |

- **【可直接实现】** `apply()` 的顺序：先记录若干旧值 → `isModified` 快照 → `super.apply()`（逐组件 `onApply`）→ 若 `UISettings` 被改过则 `fireUISettingsChanged()` + 刷新全部编辑器 → 最后检查是否需要弹重启提示（屏幕阅读器开关、主菜单显示模式、标题栏合并）。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:719-736）
- **【Swing 特有】** `UISettings.fireUISettingsChanged()` 会先增加一次修改计数（供 `PersistentStateComponentWithModificationTracker` 使用），再广播给组件树与消息总线。（来源：platform/editor-ui-api/src/com/intellij/ide/ui/UISettings.kt:772-797）

### 7.2 校验与回退（非法输入恢复原值）

- **【可直接实现】** IDE 缩放输入框：`validationOnInput` 用统一的校验函数；`onChanged` 里**再校验一次**，不通过就 `return`（不改设置）。解析规则：先去 `%`，必须是可转为 Float 的值，且 `toInt() == value`（**只接受整数百分比**）且 > 0；否则视为非法。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:302-330；platform/platform-impl/src/com/intellij/ide/actions/IdeScaleTransformer.kt:96-135）
- **【可直接实现】** 演示模式缩放另有限制：**0.5×–4×**，越界给出错误消息；普通 IDE 缩放没有上下限。（来源：platform/platform-impl/src/com/intellij/ide/actions/IdeScaleTransformer.kt:75-124）
- **【可直接实现】** 下拉建议值与「重置」链接：普通缩放建议列表 `0.7, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0`，演示模式 `1.0 … 3.0`；当前值不等于默认值时显示「重置」链接，点击即把下拉设回默认值（走同一条 `onChanged` 路径）。（来源：platform/platform-impl/src/com/intellij/ide/actions/IdeScaleTransformer.kt:79-91；platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:299-330）
- **【可直接实现】** 字号输入：非数字或 ≤ 0 时回退到**传入的默认值**（即当前值），不报错、不改设置。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:785-791）
- **【可直接实现】** 取消「覆盖默认字体」勾选时，会把字体名与字号**主动写回当前默认字体**（而不是留空），并把两个控件置灰。（来源：platform/platform-impl/src/com/intellij/ide/ui/AppearanceConfigurable.kt:335-373）

### 7.3 设置窗口自身是否立即使用新字体

- **【可直接实现】** 是。字体设置走 Apply/OK → `UISettings.fireUISettingsChanged()` → `LafManagerImpl` 的 `UISettingsListener` 发现 `computeValuesOfUsedUiOptions()` 变了 → `updateUI()` → `patchLafFonts()` 重写 defaults → 对 `Frame.getFrames()` 的每个窗口（含其 `getOwnedWindows()`，设置对话框作为模态子窗口属于此列）执行 `updateComponentTreeUI`，因此设置窗口本身立即换成新字体。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:277-299,817-848,857-885,1443-1448）
- **【需推断】** 若设置对话框**不是**由主窗口 owner 拥有（例如独立顶层窗口），`Frame.getFrames()` + `getOwnedWindows()` 的遍历覆盖不到它，字体不会立即刷新。Augit 的 WPF/Win32 外壳没有这个限制，但要注意「所有窗口都要收到字号变更」这条等价规则。（来源：platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1443-1448）
- **【Swing 特有】** 设置对话框打开期间如果切了主题，对话框自身也会被重建（因为它在 owner 链上），但对话框里「未应用」的编辑值不会因此丢失（模型值不随 UI 委托重建而重置）。（来源：推断自 platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:1443-1448 与 DialogWrapper 的模型/视图分离）

---

## 8. 边界情况与需复核清单

- **【可直接实现】** `alignToInt` 在 `scale == 0` 时返回 0（不做除法）；`getScale` 计算出的缩放在异常情况下可能 ≤ 0，此时不做任何修正（源码里那段日志是注释掉的）。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:163-173,251-258）
- **【可直接实现】** `FLOOR + ODD` 会把 0.5 变成 -1（§2.3）；任何用它在非负坐标上求奇数位置的地方都可能产出负值，Augit 实现时应显式 `Math.max(0, ...)` 或改用 `CEIL + ODD`。
- **【可直接实现】** `isFractionalScale` 只看 scale 是否为整数，**不看 translate**；因此 2.0 缩放下平移 0.5px 的 transform 不被视为「分数缩放」，相关对齐 API 会直接不生效。（来源：platform/util/ui/src/com/intellij/ui/paint/PaintUtil.java:452-456）
- **【需复核】** `devPixel(g) = 1 / round(scale)` 在非整数缩放下不精确（§2.3）；若 Augit 需要「1 物理像素」的精确表达，应用 `1 / devicePixelRatio` 而不是取整后的值。
- **【需复核】** `expUI_light_with_light_header.theme.json` 只覆盖了 `Notification.Shadow` 而没有 `Ide.Shadow`，说明它继承自 `expUI_light`；Augit 若做「浅色 + 浅色标题栏」变体，要确认继承链。（来源：platform/platform-resources/src/themes/expUI/expUI_light_with_light_header.theme.json:110）
- **【需复核】** 「模态对话框不变暗背景」这一结论来自「平台没有对应的 painter」；如果实际 PyCharm 截图显示有变暗，则说明该效果由别处（例如截图工具或 Windows 系统）提供。建议用真实 PyCharm 截图复核。
- **【需复核】** 通知在三行布局下的具体换行位置、以及 350/360px 宽度在 125% 缩放下的实际渲染（所有宽度都过 `JBUIScale.scale`，非整数缩放会有 ±1px 抖动）。
- **【需推断】** 平台不存在 `JBUIScale.Fonts` 这一类型；若 PyCharm 文档提到它，实际对应的是 `JBFont` 的角色方法（`label/regular/medium/small/h0..h4`）。
- **【需推断】** 「字号 9–40px」这一区间不是平台约束（平台建议列表 8–72、无上限校验），Augit 需要自行在输入校验里封顶。
- **【需推断】** 主题切换「只替换颜色和资源、不改变尺寸、间距、顺序或交互状态」这条要求在 IntelliJ 里不是被某段代码强制的，而是靠「同名主题几何键取值一致」+ `patchHiDPI` 归一化共同保证；Augit 应把它落成自动化回归测试而不是代码约定。

---

## 附录 A：分类总表

| 主题 | 可直接实现 | Swing 特有 | 需推断 |
| --- | --- | --- | --- |
| 主题切换 | autodetect 状态模型、三态映射、Windows 注册表判据、切换条件（dark 状态或主题 id 变化）、勾选即生效 | `UIManager.setLookAndFeel` + `updateComponentTreeUI` 重建、`IJColorUIResource` 命名色跟随、DarculaInstaller、800ms 切换淡出、500ms 悬停预览与回滚 | 切换不动尺寸的强制手段（需自建回归测试） |
| DPI 缩放 | 三个缩放源、`PIX = USR×OBJ×DEV`、0.25 量化、`scaleFontSize` 特例（1.25→×1.34、1.75→×1.67）、`alignToInt` 全套语义 | `GraphicsConfiguration.defaultTransform`、JRE/IDE 托管 HiDPI 切换、`alignTxToInt`/`alignClipToInt` 改写 transform | 96/120/144 在具体机器上的 SYS 取值、CSS 下 1 物理像素的等价写法 |
| 字体字号 | 34 个字体键、`TextArea.font = Monospaced(UI 字号)`、行高兜底 `scale(24)`、字号建议列表、非法输入回退 | `JBFont` 角色方法、Windows 旧 UI 下 medium/small 退化、JetBrains JVM Inter 强制、CJK 回退系统字体 | 四项独立设置的完整映射、40px 上限 |
| 弹层状态机 | 6 状态枚举与迁移、`cancel` vs `closeOk` vs `hide`、4 条关闭通路与默认开关、2px 容差、同组/异组动作布局 | AWT 事件监听器、`StackingPopupDispatcher` 栈、`WindowShadowMode`、transform 对齐 | 关闭动画在 Web 层的等价实现 |
| 边框阴影 | `Popup.borderWidth=1`（内侧）、`Ide.Shadow 16/16/16/16`、`Notification.Shadow 5/5/5/5`、渐变与角部画法、`Notification.arc=12`、标题栏 28/24、搜索框 insets | `PopupBorder` 的激活/非激活切换、macOS 特例 | 浅色主题下 `#FFFFFF00` 起点色的实际观感 |
| 对话框 | 右对齐按钮、OK 左 Cancel 右、12px 视觉间距、20px 组间距、上边距 8、内容 insets 8/12、Windows 上 Enter 触发聚焦按钮、Esc/←/→/Y/N/F1 绑定、校验失败保焦点+报错 | `JRootPane.setDefaultButton`、`Window` 焦点恢复、模态实现 | 模态遮罩是否存在（倾向无） |
| 通知 | `BOTTOM_RIGHT` 默认、最多 2 个、FIFO 淘汰、同 groupId 合并、固定宽 360、10s/300s 延时、悬停与失活暂停、类型配色表 | `BalloonImpl` 动画（8 帧 / 200ms）、`BalloonLayoutImpl` 200ms 去抖、`NotificationBalloonActionProvider` 命中区 | 三行布局换行、非整数缩放下的宽度抖动 |
| 设置界面 | 立即生效 vs 应用生效清单、缩放校验（整数百分比、演示模式 0.5–4）、字号非法回退、取消覆盖字体写回默认值 | `BoundSearchableConfigurable` / DSL 的 afterChange / onApply 机制 | 非 owner 窗口的字体刷新 |

## 附录 B：对现实现的核对结论（第二十七轮）

本轮把本册里三条与"Augit 硬要求"直接相关的规则拿去核对实现，结论**都是已满足或已登记的有意分歧**，因此**没有产生代码改动**。记录证据，供后续轮次直接引用：

### B.1 「切换主题时尺寸不变」在结构上成立

这是本册 §1.4 的 Augit 硬要求。核对方式不是抽样比对，而是**扫描全部深色覆盖块**（`body[data-theme="dark"] …`）里是否出现任何影响布局的声明：

| 检查 | 结果 |
| --- | --- |
| 尺寸类属性（`height`/`width`/`padding`/`margin`/`font-size`/`border-radius`/`gap`/`top/left/right/bottom`/`line-height`/`letter-spacing` 等） | **0 条** |
| 布局类属性（扩展加入 `display`/`position`/`flex*`/`grid-template-*`/`overflow*`/`white-space`/`border-width`/`text-align`/`vertical-align` 等） | **0 条** |
| 含 `border` 的深色声明 | 仅 `border-color: var(--augit-border-strong)`（颜色）与令牌定义 |

也就是说深色主题**只改颜色**（令牌 + `border-color`），尺寸不变性是结构性的，而不是靠约定维持的。这条结论可以直接替代"切主题前后量尺寸"的抽样测试。

### B.2 字体角色：与权威的 h0–h4 体系是**有意分歧**

§3.3 给出的角色偏移是 `h0 = 基准 +12 加粗`、`h1 = +9 加粗`、`h2 = +5`、`h3 = +3`、`h4 = +1 加粗`、`medium = 基准 −1`、`small = 基准 −2`，且**增减量也乘用户缩放**。

Augit 的字号体系（`design-system.md` §5.2）**明确不采用**这套偏移：所有角色默认都是 13px，只允许**普通/半粗**两个字重，并写明"正文、路径和状态不能通过连续减小字号制造层级；层级优先使用颜色、位置和间距表达"。因此 `medium` 不减 1、`small` 不减 2 是**已登记的设计选择**，不是缺口；`ui-heading` 存在但同样取 13px。**无需改动**，也不应为了对齐偏移表而引入多档字号（那会违反同一条规范）。

§3.2 的字号允许列表（`8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72`）与 Augit 的 9–40 范围不重合，本册已注明"平台无硬上限，40 封顶需自己在输入校验里实现"，属已在册的差异。

### B.3 `document-formatted`（JSON 格式化按钮）找不到权威，且现状已是登记过的推导实现

为它重新做了一次全图标集检索（`platform/icons/src` 下 2223 个非深色文件，含经典集）：`format`/`json`/`brace`/`pretty`/`prettify`/`beautify` 关键词命中的只有：

- `expui/actions/reformatCode.svg` —— 图形是**四条横线 + 一个右向三角**（"重排动作"），不是花括号；
- `modifiers/braces.svg` —— 是**一对花括号**，与 Augit 现用图形同形，但位于 `modifiers/`（文件类型修饰符语义），不是工具栏模式按钮；
- `json/object.svg` / `json/array.svg` / `fileTypes/json.svg` 等 —— 文件类型/结构图标，与"格式化视图模式"无关。

`design-system.md` 已把该按钮登记为"独立的结构化文本花括号图形……依据产品语义和同类工具栏模式推导，当前没有可作为直接证据的 PyCharm JSON 同场景屏幕样本，因此只能记为推导实现"。本轮检索**印证了该记录**（平台确实没有同场景样本可依），因此**保持现状**，不改成 `reformatCode` 或 `modifiers/braces`。

## 附录 C：两处「chrome 高度」出现三方不一致（第二十八轮，待确认）

本轮把参考截图的下缘与标签条逐像素量了一遍，结果与规范和本地权威**三方不一致**。按 `AGENTS.md`「当前规范出现冲突时必须停止实施并向用户确认，不得自行选择一种解释」，此处**不改代码、不改数值**，先记录证据。

### C.1 定标

参考截图只有一张同时含编辑器标签卡片（`visual-refinement-2026-09-08/pycharm-history-normal.png`；另一张 `pycharm-baseline-20260919/pycharm-main.png` 底部是正文，**没有截到状态栏**，无法交叉验证）。

标签卡片的逻辑高度可由权威公式**唯一确定**：`IslandsTabPainter.paintTab` 里 `vOffset = max(rect.height − fullHeight, 8)`，而 `fullHeight = scale(28)`；只要 `rect.height ≥ 36`，`vOffset = rect.height − 28`，于是**卡片高度恒为 28 逻辑px**。参考图实测卡片（含 1px 描边）为 **50 物理px** ⇒ 缩放 ≈ **1.75–1.79**。

### C.2 标签栏高度：42 / 40 / ≈38 三个数

| 来源 | 数值 | 依据 |
| --- | --- | --- |
| 现行规范 | **42px**，且写明"上下各留 7px（`EditorTabs.tabInsets` 的 −7 上下分量）" | `design-system.md:441` |
| 本地 checkout 主题 | **40px**，上下各 6px | `ManyIslandsLight.theme.json`：`ui.EditorTabs.tabInsets = -6,8,-6,8`，配合 `SingleHeightTabs.UNSCALED_PREF_HEIGHT = 28` |
| 2026 参考图实测 | **≈38–39px** | 标签条白底区间 y89..156 = 68 物理px ÷ ≈1.75–1.79 |

注意：规范所引的「−7 分量」在主题里**不存在**（是 `-6`），这一条出处本身有误；而参考图给出的第三个数值（≈38）又低于两者。三者互不相等，无法由现有证据判定哪个是 2026.2.1 的真实值。

### C.3 状态栏高度：22–23 / 公式 / ≈29

| 来源 | 数值 | 依据 |
| --- | --- | --- |
| 现行规范与实现 | **22–23px** | `design-system.md` 与 `--augit-status-*` 令牌 |
| 本地 checkout | **无显式高度键**，高度是算出来的 | `IdeStatusBarImpl.kt:351`：`insets.top + insets.bottom + max(minIconHeight, preferredTextHeight)`，其中 `minIconHeight = scale(18+1+1) = scale(20)`、`preferredTextHeight = 文本高 + StatusBar.Widget.border 的垂直 insets`（`:169-170`、`:803`）。主题里 `ui.StatusBar` 只给颜色/`topBorderWidth`，**不给高度**，因此该式的结果取决于 widget 边框 insets，本轮未查到定值 |
| 2026 参考图实测 | **≈29px** | 状态栏底 `#E9EAEE` 区间 y1057..1107 = **51 物理px**，上下相邻为 `#F1F2F4` 分隔线与窗口边框；51 ÷ ≈1.75–1.79 ≈ 28.5–29 |

也就是说：规范值（22–23）与参考图实测（≈29）相差约 6px，而本地 checkout **查不到**可判定该值的常量。在定案前不动 `--augit-status-*`。

### C.4 决议与落地（用户已明确：所有冲突一律以 New UI 为准）

用户裁决：**全部以 New UI 为准，今后所有冲突一律对齐 New UI**。据此本轮把 §C.2／§C.3 两处按本地 checkout 的权威取值落地，并同步规范：

| 项 | 原值 | 现值 | 权威依据 |
| --- | --- | --- | --- |
| 标签栏高 | 42px（规范另注"上下各 7px"，但主题里没有 −7） | **40px，上下各 6px** | `ManyIslandsLight.theme.json` 的 `ui.EditorTabs.tabInsets = -6,8,-6,8`，配合 `SingleHeightTabs.UNSCALED_PREF_HEIGHT = 28` |
| 状态栏高 | 22px | **28px** | `IdeStatusBarImpl.kt:351` 的 `max(scale(20), 文本高 + StatusBar.Widget.border 垂直 insets)`；New UI 下该边框为 `insets(6, 8)`（`JBUI.java` 的 `StatusBar.Widget.border`，`isNewUI()` 分支），默认字号文本高 16 ⇒ 28 |

两处都与 2026 参考图实测吻合（标签条 70 物理px ÷ ≈1.79 = 39.2；状态栏 51 物理px ÷ ≈1.79 = 28.5），因此这次是**权威与截图互相印证**，不是拿截图反推常量。

### C.5 坑：布局高度令牌其实由 `mockup.js` 在运行时内联写入

改 CSS 的 `:root` 令牌后实测**完全没变**。原因是这些高度令牌有**第二处定义**：`web/src/mockup.js:73` 的 `const length = (name, pixels) => root.setProperty(\`--augit-${name}\`, \`${pixels}px\`)` 会在启动时把整张高度表**内联写到 `<html>` 上**，优先级高于样式表。按完整令牌名（`--augit-tab-height`）在 JS 里搜**搜不到**，因为名字是模板拼出来的——这与之前踩过的「模板拼字符串」是同一类陷阱。

正确做法是**两处一起改**：

- `mockup.js` 的推导式：`tab-height = max(40, h + 24)`（卡片 `h + 12` + 上下各 6）、`status-height = max(20, h + 12)`；
- `mockup.css` 的 `:root` 兜底值：`--augit-tab-height: 40px`、`--augit-status-height: 28px`（与默认字号下的推导结果一致）。

连带发现一处历史不一致（本轮未改，供后续核对）：同一张表里 `tree-height` 的兜底是 `27px`、推导式是 `ceil(max(27, h+8)/2)*2`（默认字号得 28），而 New UI 的值是 `JBUIScale.scale(24)`／主题 `Tree.rowHeight = 24`——**差 4px**。这属于同一类"规范 vs 权威"冲突，按新政策下一轮对齐，并需要同步验收套件里对应的期望式。

### C.6 验收套件里的期望值也要跟着改

本册核对出的高度公式同样写在验收套件里（`tools/audit/live-shell.spec.cjs` 的"规格:154 主框架六处随实际字高扩展"）：它的期望式原本是 `tab-height = max(42, h+14)`、`status-height = max(22, h+2)`，正是旧规范的值，因此改实现后套件必然失败。按新政策已把期望式同步为 `max(40, h+24)`／`max(20, h+12)`。**结论：凡"规范值"出现在验收套件里，改规范时必须同时改期望式**，否则会出现"实现对了、套件红了"的假失败。
