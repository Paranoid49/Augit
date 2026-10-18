# Augit 统一设计系统

2026-10-17远端工具栏禁用图标沿用安装版GrayFilter：浅#BDBDBD、深#5A5A5A，不复用Label.disabledForeground。仅作用于Remove/Edit禁用图标，恢复启用时仍用icon色；浅色正常入口原图已确认，深色完整实机配对未完成，见pixel-remote-approved-layout-2026-10-17.md。

2026-10-15用户裁决：正常启动的全部界面以真实外壳测试基准对齐。旧外观配置首次读取迁移到 Light、Microsoft YaHei UI 12；以 appearanceVersion=1 标记，后续保留用户的主题及界面字号调整。等宽正文固定为随包 JetBrains Mono 13，覆盖旧等宽偏好并取消设置页的等宽字体/字号选择。保留工作区、标签、面板及 Git 设置；系统 DPI 正常跟随，不复制审计强制100%参数。本条取代下文旧字体保留、等宽字体及字号可任选的条款。

## 1. 文档目的

> **当前状态（2026-10-12）**：第 307／309 轮的“清零／完成”只说明当时登记范围，不代表当前 PyCharm 全场景复原。当前整改与验证缺口见 [整改清单](ui-restoration-checklist-2026-10-12.md) 和 [实施记录](ui-restoration-fixes-2026-10-12.md)；未验证项保持未验证，不转成通过。


本文把 `product-spec.md`、`architecture.md` 和 `ux-spec.md` 中已经确认的界面规则收敛为一套可执行的视觉系统。它用于统一 Augit 当前已有页面和状态的实现细节，使界面具有一致的字体、间距、图标、颜色、层级和交互反馈。

2026-10-13像素整改及用户裁决：复选框统一使用24px透明画布、16px绘制外框；未选聚焦外框18px，已选/半选聚焦保留16px内框并绘制22px外环，随界面字号档位缩放。取消按钮始终显示Cancel，进行态原因放在局部状态区；Amend与蓝色last commit链接分开，链接打开现有Git Log，不提供指定提交Amend下拉；Windows Stash/Reset确认在Cancel左侧。以下旧条款中独立15px复选框、取消进行态预留和Amend中性历史图标由此订正。字体安装、随包加载和独立模态接续见[字体与窗口整改记录](pixel-font-followup-2026-10-13.md)，首批逐项证据见[整改记录](pixel-restoration-2026-10-13.md)。

本文不新增产品能力，不新增页面、按钮、设置项、文件格式或 Git 操作。任何组件只能承载产品规格已经列出的动作；没有产品动作的视觉稿不得转化为可点击控件。

PyCharm 2026.2.1 New UI 是空间组织、信息密度、反馈节奏和功能图标的最高还原基线。Augit 的理想目标是在自身已有产品范围内尽可能 100% 复原 PyCharm；允许像素级复制，也允许采用其他能达到同等视觉与交互效果的实现方式，不以所有平台渲染像素绝对相等作为整体完成条件。像素对照用于发现和修正差异，仍未验证的部分必须明确记录。功能图标必须与对应 PyCharm 图标保持相同的轮廓、比例、线宽、填充、位置、颜色和状态，不能用语义相近的替代图标；不使用 JetBrains 商标、产品标志或官方图标资源文件。

## 2. 规范优先级

发生冲突时按以下顺序判断；发现当前规范之间存在冲突时必须停止对应实施并向用户确认，不得自行选择解释。

**与 PyCharm New UI 冲突时的裁决（用户已明确）**：凡是「本规范登记的数值/写法」与「本地 `intellij-community` checkout 里的 New UI 权威」不一致，**一律以 New UI 为准**，并把本规范改成 New UI 的值；不再逐条等待确认。参考截图与 New UI 权威不一致时同样以 New UI 为准，截图只用于发现差异。此前在本仓库各处记为「与规范冲突、待确认」的条目，按本条逐条改为 New UI 取值。

1. `docs/product-spec.md`：产品范围和用户可见行为。
2. `docs/architecture.md`：C#、.NET 10、原生 Win32 外壳 + WebView2，界面本体为 `web/` 下的 HTML/CSS/JS。
3. `docs/ux-spec.md`：页面结构、状态机、焦点和视觉验收。
4. 本文：跨页面的视觉令牌和组件实现规则。
5. `docs/ux-mockups/`：比例、密度和状态的视觉参照，同时是运行时界面代码的来源。

`docs/roadmap.md` 只保留历史实施记录，不参与当前设计值、实现约束或冲突判断。

本文不能反向改变上面任何文件的产品行为或技术约束。

### 2.1 实现落点（阅读本文前必读）

本文早期的实现描述以「原生 Win32 自绘 + Scintilla」为落点。当前主界面**已改为 WebView2 承载 HTML/CSS/JS**，因此文中下列术语按此对应关系理解，**视觉规则本身不变**：

| 文中旧表述 | 当前实际落点 |
| --- | --- |
| 原生控件、owner-draw、`DrawItem`、GDI/GDI+ 绘制 | HTML 元素与 CSS 声明 |
| Scintilla、直接调用接口、`SendMessage`、正文标记 | `.code-view` / `.diff-layout` 等 DOM 结构与类名 |
| `NativeTheme`、颜色与度量缓存 | `web/src/mockup.css` 中的 CSS 自定义属性（`--augit-*`） |
| HWND、subclass、`TrackMouseEvent`、窗口消息 | DOM 事件与 `:hover` / `:focus-visible` / `[aria-*]` 状态 |
| `CreateRoundRectRgn`、圆角直径 | `border-radius` |
| 原生弹层、模态窗口 | 弹层仍为DOM结构；Settings按2026-10-16授权使用独立WebView2及唯一owned窗口，主窗持续呈现；其他真实模态重挂同一WebView2会话，表单继续由共享DOM绘制 |

**令牌的唯一权威落点是 `web/src/mockup.css`。** 本文登记令牌的语义与取值；CSS 中出现的视觉常数必须能在本文找到登记项，反之本文新增令牌必须同步落到 CSS，并由 `tools/audit/verify-ui-assets.ps1` 保证 `docs/ux-mockups/` 与 `web/src/` 的字节一致性。

下文凡出现具体 Win32 API、GDI+ 参数或 Scintilla 接口名，均属历史实现描述；仅当它与当前 CSS 实现冲突时才以当前实现为准，并应更新本文。


## 3. 设计目标

- 轻量：界面只显示当前能力需要的信息，不用装饰填充空白。
- 稳定：局部加载、刷新和错误只改变所属区域，主框架的位置不跳动。
- 一致：同一语义在所有页面使用同一颜色、图标、字号、间距和状态表达。
- 可读：浅色、深色、100%/125%/150% DPI 下文字和状态均清晰。
- 克制：使用 PyCharm 的空间组织、信息密度和功能图标视觉；Augit 只保留自己的品牌标记，不为已有功能设计另一套“近似图标”。

本次复原的当前数值与旧记录发生差异时，以 [24 · 参考外观整改](nui-behavior/24-reference-restoration.md) 的逐组件取证为准；历史待办结案不代表整体视觉验收。

## 4. 基础令牌

所有界面绘制（当前为 `web/src/mockup.css` 中的 CSS 声明与 DOM 结构）都必须从同一组逻辑像素令牌派生。禁止在页面代码中写入未登记的视觉常数；例外必须先补充本文。

### 4.1 间距刻度

基础间距优先使用 4px 刻度：`0、4、8、12、16、20、24、28、32`。这是通用布局的默认刻度；对应 PyCharm 场景存在不同间距时，应测量并登记为组件令牌，不得为凑整强行改变参考布局。布局中优先使用以下关系：

| 令牌 | 值 | 用途 |
| --- | ---: | --- |
| `space-1` | 4 | 图标与文字的最小间隔、分隔线附近留白 |
| `space-2` | 8 | 同组控件间距、标题栏内边距 |
| `space-3` | 12 | 行内主要内容间距、表单标签与输入框间距 |
| `space-4` | 16 | 区块内边距、工具窗口左右内边距 |
| `space-5` | 20 | 区块之间的舒适间距 |
| `space-6` | 24 | 对话框和详情区的主要分组间距 |
| `space-8` | 32 | 大区块外边距和弹层边缘留白 |

图标按钮的命中区可以大于可见图形，但命中区不能改变相邻可见图形的对齐基线。

### 4.2 结构尺寸

以下是 96 DPI 逻辑像素值；所有值通过 `NativeTheme.Scale` 缩放，禁止直接按物理像素绘制。

这些值是当前实现的统一基线，不代表每项均已通过 PyCharm 实测验收。发现差异时应同步修正组件令牌、视觉稿和原生实现，并在视觉复核记录中保留参考场景与测量条件。字体增大后的内容尺寸按实际度量适配，不能靠裁切或缩小用户字体维持默认值。

| 令牌 | 值 | 规则 |
| --- | ---: | --- |
| `window-title-height` | 44 | 默认标题栏高度；随实际字高扩展 |
| `global-rail-width` | 42 | 左侧全局工具栏宽度 |
| `tool-button-hit` | 工具轨 37×40；其他按钮按组件 | 工具轨的绘制内距与命中区分离 |
| `icon-visible` | 16×16 | 工具图标可见绘图区 |
| `side-panel-default` | 360 | 推荐窗口左侧工具窗口宽度 |
| `side-panel-min` | 300 | 小窗口左侧工具窗口最小宽度 |
| `bottom-panel-min` | `max(180, 4h + 80)` | 底部工具窗口最小高度；`h` 为当前界面实际行高 |
| `bottom-panel-max` | `max(305, bottom-panel-min)` | 底部工具窗口参考最大高度；高字体时不小于最小高度 |
| `bottom-panel-default` | `clamp(bottom-panel-min, 客户区高度×31%, bottom-panel-max)` | 默认 Git 历史或终端高度 |
| `tool-header-height` | 38–42 | 工具窗口标题和底部工具窗口标题 |
| `row-height` | `max(round(24s), h)` | 项目树行，`s` 为界面字号相对 12px 的 0.25 离散缩放档；默认 24px |
| `control-height` | 30–31 | 输入框、选择框和普通按钮 |
| `tab-height` | `max(round(40s), h + round(12s))` | IntelliJ New UI 编辑标签栏；默认 40px |
| `tab-visible-height` | 28 | 默认界面字号下正式和临时标签的可见背景高度，栏内上下各留 6px |
| `tab-content-inset` | 8 | 标签背景内图标左侧留白（`EditorTabs.tabInsets` 的左右分量）；图标区 16px，图文间距 4px（`EditorTabbedContainer` 的 `iconTextGap`） |
| `tab-close-visible` | 8 | 标签关闭叉号的可见尺寸，独立命中区宽度 28px |
| `status-height` | 28（默认字号） | 底部状态栏。New UI 的公式是 `max(scale(20), 文本高 + StatusBar.Widget.border 垂直 insets)`，而 New UI 下该边框是 `insets(6, 8)`（高 12），默认字号文本高 16 ⇒ 28；2026 参考图实测状态栏底 `#E9EAEE` 区间 51 物理px（定标 ≈1.79）⇒ 28.5，吻合 |
| `separator` | 1 | 面板和区域分隔线 |
| `surface-gap` | 4 | 工具窗口、编辑区和底部工具区之间 |
| `surface-radius` | 8–9 | 主面板外边缘、弹层和对话框 |

内容行、文件树行和表格行不使用卡片圆角。圆角只表达一个独立表面，不用于每一项列表。

主框架在界面字号或字体改变后，以正文、半粗体和临时标签斜体中的最大实际行高 `h` 计算容器，字号档位为 `s = max(0.75, round(字号 / 12 × 4) / 4)`。标题栏至少 `h + 18px`，文字按钮至少 `h + 4px`；标签栏、树行按上表的离散缩放基准与字形下限计算；项目头至少 `h + 10px`，文件路径工具栏至少 `h + 8px`，状态栏为 `max(round(28s), h + round(12s))`。标签卡片默认上下各留 6px。所有留白再按当前 DPI 换算。主菜单项和状态栏右侧文字按实际字宽分配，长路径使用剩余空间省略。工具轨列宽为 37px，主网格不另加左内距，项目表面从 4px 间隙后的 x=41 开始。

界面字号变化同时缩放所属图标族、命中区和框架尺寸。用户缩放 `s = max(0.75, round(UI字号 / 12 × 4) / 4)`，逻辑像素尺寸按 `round(基准 × s)` 计算，系统 DPI 再由 WebView2 栅格化。基准：普通图形16、紧凑图形13、工具轨及标题动作20、普通命中区28、模式步长26、工具轨37×40、内距5/2/5/5、圆角6；工具轨限高时纵向滚动。系统窗口控制和对话框框架关闭图形保持16，复选框按24px画布／16px外框独立度量并随用户缩放，提交图继续按实际提交行高计算。标题栏44、项目头39、标签40、文档工具栏36、Diff工具栏39为各自缩放基准，并以实际字形高度保底；搜索和提交槽位须容纳缩放后的图形。树选择、首个可见节点、正文选择和滚动保持，等宽字号不随界面字号改变。设置回默认12px后恢复基准。当前参考的12px来自 Microsoft YaHei UI 配置，不泛化为所有用户默认；大字号的自动化几何验证不等于 PyCharm 同字号墨迹已验收。

底部 Git 工具窗口在字号变化后使用 `max(180px, 4h + 80px)` 作为最小高度，其中 `h` 取当前界面字体的实际行高；默认高度仍按客户区 31% 比例计算并受参考上限约束。扩展只作用于底部工具窗口，不改变左侧工具窗口、编辑标签和正文的选择与滚动状态。

工具窗口分隔条沿用 4px 表面间隙，命中容差不改变拖动起点。HTML 主框架同样用动态底部高度分配网格，不再额外固定 225px 底部下限或 255px 编辑区下限，避免大字号在最小窗口内覆盖状态栏。高度来自各自真实字体度量，不把浏览器与 GDI 的字高差异当作已校准。

`surface-radius` 表示半径，在 CSS 中直接作为 `border-radius` 使用。背景、标题栏与内容裁剪使用同一轮廓。提交面板先填充 `Chrome` 外底，不能先填满 `Panel` 再覆盖同色圆角，导致边缘仍然是方形。

### 4.3 层级

从底到顶固定为：`Chrome`（标题栏和工具栏）→ `Panel`（工具窗口和编辑区）→ `PanelMuted`（次级区域，例如工具窗口底）→ `Popup`（菜单和弹层）→ `Modal`（对话框；点击承接层透明、不降低背景权重，见 §8.5）→ `Focus`（焦点环）。

同一层级只使用背景色和 1px 分隔线区分，不使用阴影堆叠。弹层和模态框可以使用轻微阴影，但不能靠阴影替代边界或状态文字。

## 5. 字体系统

### 5.1 字体族

- 界面字体：默认 `Microsoft YaHei UI`，与参考环境中 PyCharm 的实际界面字体一致；缺失时依次回退 `Segoe UI`、`Microsoft Sans Serif`。用户已经保存的其他字体继续优先使用。
- 只读文本、Diff 和终端：使用用户设置的等宽字体，默认随包 `JetBrains Mono` 2.304，资源不可用时依次回退 `Cascadia Mono`、`Consolas`、`Courier New`。必须验证随包字体加载或系统字体存在，不能只把不存在的名称交给系统后接受无声替换。参考默认`FontPreferencesImpl.myUseLigatures=false`，只读正文、Diff与Markdown代码关闭连字；终端沿用xterm逐格字形。
- 界面、Markdown 与终端共用字体解析结果，界面按 `en` 区域渲染，用户文档保留原文。应用不读取 PyCharm 目录内的字体资源；随包字体仅来自独立开源仓库，附 OFL 许可证。用户安装的字体可以通过已有设置选择。
- 为了比较字体和布局，PyCharm 参考窗口与 Augit 都必须先设置为相同的界面字体、等宽字体和字号。2026-10-11 前台实测 PyCharm 为 Microsoft YaHei UI／12、JetBrains Mono／13.0、编辑器行距 1.2；Augit 当前用户配置为 Microsoft YaHei UI／12、Cascadia Mono／13，两端等宽字体尚未统一。此前 Cascadia Mono 是 Augit 配置，不能写成 PyCharm 实测值。用户已保存字体不被静默覆盖，应用不读取 PyCharm 字体；本批次已安装并随包集成独立开源 JetBrains Mono，但未统一实际字体前不得宣称正文栅格与行高验收通过。
- Markdown 预览必须接收同一套用户字体设置，并对字体名称进行 HTML/CSS 转义。正文跟随**界面**字号、代码块跟随**等宽**字号——落地细节、死令牌 `--augit-ui-font-size` 的订正与本轮验证见 [21 · Markdown 预览字号归属与死令牌](nui-behavior/21-markdown-preview-fonts.md)。
- “恢复参考外观”的快照为 Light、Microsoft YaHei UI／12、JetBrains Mono／13，来源为2026-10-12前台复核。按用户2026-10-13裁决，新配置默认使用随包 JetBrains Mono 2.304；恢复前验证实际字体资源和系统字体。资源不可用时依次选择Cascadia Mono、Consolas、Courier New并显示原因。恢复只改草稿，Cancel不持久化，不覆盖已有用户字体。字体资源实际加载后重新度量正文；字体可用与逐像素配对分别登记。
- Markdown正文行距1.6、左右内距2em；h1–h6倍率为2.2／1.8／1.3／1／1／1，标题顶部1em、底部16px，h1–h3顶部内距0.6em；h2行距1.2、h3为1，列表缩进2em。代码独立等宽字号，代码块内距16px、行距1.45、圆角3px。参考为当前PyCharm安装包Markdown `baseStyles/default.css`，字号归属仍按产品设置模型，不新增独立预览字号。
- 查找框高为`max(30,字高+4,round(28s)+2)`，条高为`max(42,字高+16,round(28s)+14)`；发生局部横向滚动时额外容纳10px滚动条。输入、三开关、导航与关闭仍占一行，Tab聚焦须完整显示命中区；局部滚动不得推动整个编辑器。只读JSON错误与符号链接提示共存时可各自滚动，说明区至少一行加16px内距，正文至少保留一行。
- Markdown 预览的页面背景、正文、辅助说明和边框共用 `Panel`、`Text`、`Muted`、`Border` 色板；不另设深色背景。加载提示为预览区域顶部的 36px 紧凑行，失败提示按文字换行度量高度，原文或旧预览保持可见。
- 不使用 Emoji 作为图标，也不使用图标字体中的任意字符替代正式图标绘制。
- 不使用 Unicode 箭头、菱形、三点或文字字形充当功能图标；例如下拉箭头、分支、提交和更多动作都必须由统一绘制入口生成。按钮标题中的省略号仍属于文字内容，可以保留在本地化字符串中。

### 5.2 字体角色

本文字号均为 96 DPI 逻辑像素，不是物理截图像素或印刷点数。12px 在 175% Windows 缩放下对应 21 个物理像素，实际字形高度仍由字体度量决定；不能直接拿截图中的字符高度反推设置字号。

| 角色 | 大小 | 字重 | 用途 |
| --- | ---: | --- | --- |
| `ui-body` | 12px（默认） | 普通 | 树、列表、按钮、状态文字；由用户字号设置覆盖 |
| `ui-medium` | 12px（默认） | 半粗 | 当前项目、当前分支、选中项、主要标签；由用户字号设置覆盖 |
| `ui-heading` | 12px（默认） | 半粗 | 工具窗口标题、对话框标题；由用户字号设置覆盖 |
| `ui-small` | 12px（默认） | 普通 | 路径、作者、时间和辅助说明；只降低颜色，不缩小字号；由用户字号设置覆盖 |
| `code-body` | 13px（默认） | 普通 | 普通文本、Diff 和终端正文；由用户字号设置覆盖 |
| `code-line-number` | 13px（默认） | 普通 | 行号；使用较弱文字色并跟随等宽字号设置 |

只允许普通和半粗两个主要字重。正文、路径和状态不能通过连续减小字号制造层级；层级优先使用颜色、位置和间距表达。

### 5.3 文字排版

- 界面可见文案固定为英文，术语与当前参考环境统一；宿主兼容说明由 `web/src/ui-text.js` 管理。
- 用户文件内容、提交信息、引用名和 Git／终端原始输出保持原文；文档与代码注释使用中文。
- 标签、树节点和按钮使用单行省略号；省略号前保留最小 8px，关闭按钮保持独立命中区。
- 标题栏工作区入口显示当前工作区目录名；未打开工作区时显示 `Augit`。工作区与分支入口按实际字宽分配，最大宽度各为 180px，长名称独立省略；图形到文字间距 8px，文字到 8px 下拉箭头间距 4px，右侧留白 8px。当前文件入口宽度为实际字宽加 28px（左右各 8px、文字到箭头 4px、箭头 8px），不保留固定的 112px 最小宽度。下拉箭头统一复用 `NativeTheme.DrawChevronIcon`。界面字体改变时重新度量，分支名称未变的状态刷新不触发布局。
- 文本与图标垂直居中以字体实际度量为准，不能按字符框顶部硬编码。
- 代码区行号、正文和 Diff 两侧使用相同的行高和等宽字宽；行号列宽按最大行号调整。
- Windows参考Editor为Subpixel抗锯齿。随包JetBrains Mono加载成功时，编辑器字符步长按独立JBR整数度量取 `round(round(字号 × DPR) × 0.6) / DPR`，以相对浏览器实际字宽的letter-spacing实现；行号和词级／查找高亮使用同一步长，字体或DPI变化重新度量。其他字体、资源未加载时保持原始字距，终端和Markdown渲染预览不套用编辑器规则。32字号×8比例的参考度量和原始屏幕边界见[普通文本取证](pixel-text-pair-2026-10-14.md)。字距对齐不代表抗锯齿墨迹完全相同。
- 文本行号栏用当前等宽字体实际度量最长行号（至少预留三位），再加 28px 逻辑留白；只读正文、等宽字号或 DPI 变化时重新度量，界面字号不得改变正文起点。
- 只读正文和 Diff 的行距乘数为 1.2，作用于实际字体度量，不直接以字号乘 1.7。随包JetBrains Mono加载成功后使用参考JBR的AWT度量：hhea为1020/−300/0、unitsPerEm为1000，ascent/descent分别向上取整，再按Java单精度乘1.2向上取整；基线为行高减去descent与一半附加行距。13px对应22px行盒、16px基线；18px/37px保留Java单精度造成的31px/61px行盒。通过真实DOM基线探针只平移文字墨迹，不改行盒、背景或滚动步长。其他字体或资源失败继续使用浏览器实际度量；终端沿已登记的xterm行距，Markdown预览代码仍为1.45。普通文本、JSON、Markdown源文、Diff和Blame共用编辑器规则。32档独立JBR与浏览器实测、真实WebView2基线及边界见[接续记录](pixel-font-followup-2026-10-13.md)。HTML同样按实际等宽字宽度量行号列，行号文字取浅`#AEB3C2`／深`#4B5059`；行号栏与代码区同底、右侧留3px，横向固定，正文单独滚动。
- 设置中的界面字体与等宽字体各自占一行，每行包含字体名称和独立的字号输入；默认界面字号为 12px，默认等宽字号为 13px。修改等宽字号不得改变树、标签和按钮的字号，修改界面字号不得改变只读源文、Diff和终端的字号。Markdown 预览正文跟随界面字号，代码块跟随等宽字号。
- 旧版本只保存一个字号时，两类文字先沿用旧值；用户分别调整后独立保存。设置窗口执行“应用”后自身的文字和输入控件也必须立即使用新字体。

### 5.4 PyCharm 对齐的字体设置契约

字体设置必须提供四个相互独立的值，并在设置窗口中明确显示其作用范围：

| 设置值 | 默认值 | 作用范围 |
| --- | --- | --- |
| 界面字体 | `Microsoft YaHei UI`（按回退规则解析） | 文件树、标签、工具窗口、菜单、按钮、状态栏和 Markdown 普通正文 |
| 界面字号 | `12px` | 上述所有界面文字 |
| 等宽字体 | 随包 `JetBrains Mono` 2.304（按回退规则解析） | 只读文件正文、Diff、Git 文本详情和终端 |
| 等宽字号 | `13px` | 上述所有等宽文字 |

修改任一值不得隐式修改另外三个值。字号允许用户在 9–40px 范围内调整，立即刷新已打开的视图并持久化；字体缺失时必须显示实际回退字体，不能保留一个未安装的名称造成“看似一致”的假象。截图验收时必须记录四个实际生效值，并在相同 Windows DPI 下对照 PyCharm。

## 6. 颜色系统

颜色名称是语义令牌，页面不得直接使用颜色字面量。以下值与 `NativeTheme.Palette` 和视觉稿保持一致。

浅色与深色均使用 Islands 家族的语义别名。`ManyIslandsLight.theme.json` 与 `ManyIslandsDark.theme.json` 分别解析各自颜色链，父主题只补充没有覆写的键；不得以“最接近的 expUI 色值”替代已存在的 Islands 语义色。深色 SVG 的 `#CED0D6` 还需按主题图标调色补丁映射为 `icon-default-stroke = #C3C5CB`。

### 6.1 浅色主题

| 令牌 | 色值 | 用途 |
| --- | --- | --- |
| `chrome` | `#E9EAEE` | 标题栏、全局工具栏、状态栏、左侧栏的基础背景（参考主题族 `main-window-bg` = `layer-0-bg` = `gray-150`；三张 PyCharm 参考截图实测同值，占画面 12–15%）；主工具栏在其上叠加项目配色渐变，见 §6.5 |
| `panel` | `#FFFFFF` | 工具窗口、编辑区、对话框内容（`EditorTabs.background` / `Menu.background` / `Popup.background` = `Gray14`） |
| `panel-muted` | `#F7F8F9` | 次级表面与禁用控件底。权威 `*.background` 与 `*.disabledBackground` = `dialog-bg` = `layer-1-bg` = `gray-160`（ManyIslandsLight:381,387,201,168,11），深色侧由 expUI_dark 的 `*.disabledBackground` = `Gray2` `#2B2D30` 印证。**第 131 轮把浅色由 `#F7F8FA` 订正为 `#F7F8F9`** —— 前者是**未被采用**的 expUI_light 的 `Gray13`，与 `chrome`（→ `gray-150` `#E9EAEE`）、`border`、`border-strong` 当初属同一类偏差；**不再与 `chrome` 同值** |
| `border` | `#E9EAEE` | 普通分隔线（参考主题 `ui.*.borderColor` = `dialog-border` = `layer-1-border-inline` = `gray-150`；标签栏下沿实测同值） |
| `toolbar-border` | `#E9EAEE` | 文档工具条底边。权威 `Editor.Toolbar.borderColor` = `editor-border-inline`（浅与 `border` 同值）。深色见 §6.2——那里是 `Gray3` `#393B40`，**比表面更亮**，与普通分隔线用的 `Gray1` 不同 |
| `border-strong` | `#D1D3D9` | 拖动分隔线、组件边框（参考主题 `ui.Component.borderColor` = `control-border` = `gray-130`） |
| `text` | `#000000` | 主文字（浅色头部变体的 `MainToolbar.foreground` / `MainMenu.foreground` = `Gray1`） |
| `muted` | `#5F6269` | 路径、时间、辅助文字（`StatusBar.Widget.foreground` → `text-muted` = `gray-70`；PyCharm 参考截图状态栏文字实测同值） |
| `faint` | `#9FA2A8` | 禁用文字和弱提示。权威 `Label.disabledForeground`（ManyIslandsLight 指向 `text-disabled` = `gray-100`）；原 `#A8ADBD` 是 `ParameterInfo.disabledForeground` 这个**特定组件**的值（也等于未被采用的 expUI_light 的 Gray8） |
| `icon` | `#6C707E` | 未激活图标与文件类型图标（权威图标文件描边色本身即 `#6C707E`）。**与 `muted` 不同值** |
| `line-number` | `#AEB3C2` | 编辑器行号文字（配色方案 `LINE_NUMBERS_COLOR`；参考截图行号区实测同值）。行号栏底色与代码区同底，不单独设令牌 |
| `tabs-border` | `#E9EAEE` | 标签栏下沿分隔线（`ui.EditorTabs.underTabsBorderColor` = `editor-border` = `layer-2-border` = `gray-150`；参考截图 y=157 实测同值） |
| `tab-selected-bg` | `#E9EAEE` | 选中标签卡片底（未聚焦档，`inactiveUnderlinedTabBackground` = `tab-selected-bg-inactive` = `gray-150`；参考截图卡片填充实测同值） |
| `tab-selected-border` | `#D1D3D9` | 选中标签卡片描边（未聚焦档，`inactiveUnderlinedTabBorderColor` = `tab-selected-border-inactive` = `gray-130`；参考截图卡片边线实测同值） |
| `tab-selected-bg-active` | `#E3EBFE` | 选中标签卡片底（聚焦档，`underlinedTabBackground` = `tab-selected-bg-active` = `blue-150`） |
| `tab-selected-border-active` | `#A7C5FF` | 选中标签卡片描边（聚焦档，`underlinedBorderColor` = `tab-selected-border-active` = `blue-120`） |
| `scrollbar-thumb` | `#73737333` | 普通区域滑块，轨道透明；使用 WebView2 区域滚动条 |
| `scrollbar-thumb-hover` | `#73737347` | 普通区域滑块悬停和拖动，取 `ScrollBarPainter.THUMB_HOVERED_BACKGROUND` |
| `tab-scrollbar-thumb` / `tab-scrollbar-thumb-hover` | `#ABABAB` / `#7F7F7F` | 标签栏专用滑块，取 `TABS_THUMB_BACKGROUND` / `TABS_THUMB_HOVERED_BACKGROUND` |
| `accent` | `#3574F0` | 激活工具、主要动作、焦点（`*.focusColor` / `*.underlineColor` = `Blue4`） |
| `accent-brand` | `#3871E1` | **侧栏选中工具按钮的底色**（`ui.ToolWindow.Button.selectedBackground` = `toolbar-selected-bg-active` = `accent-brand-bg` = `blue-80`），前景固定白（`icon-over-accent`）。与 `accent` **不是同一个蓝**：复选框选中底在参考主题里是 `#3574F0` |
| `rail-selected-fg` | `#FFFFFF` | 激活工具窗口入口的图标前景（`ui.ToolWindow.Button.selectedForeground`；与 `accent-brand` 底配对） |
| `accent-soft` | `#D0DFFE` | 选中行和辅助选中表面（参考家族 ManyIslandsLight 的 `selection-bg-active` = `blue-140`；`#D4E2FF` 是未被采用的 expUI_light 的 Blue11） |
| `selection-inactive` | `#E9EAEE` | 列表、树失去焦点后保留选择的中性背景（`ui.*.selectionInactiveBackground` = `selection-bg-inactive` = `gray-150`） |
| `history-selection-inactive` | `#DFE1E5` | Git 历史列表失去焦点后保留选择的中性背景（同上；旧值 `#E9EAEC` 实为 `Log.Commit.hoveredBackground`，不是失焦选中色） |
| `hover` | `#00000012` | 普通控件悬停的兼容令牌；项目树、Changes 和引用树不绘制悬停行底色，表格行使用 `row-hover`。 |
| `action-button-hover` | `#00000012` | **动作按钮**（图标按钮、分段按钮、对话框次级按钮）悬停底。权威 `ActionButton.hoverBackground`（`EditorToolbarButtonLook`、`SegmentedButtonLook` 的 `POPPED` 态都读它）；深色见 §6.2——那里是 `#FFFFFF16`（`MainToolbar.Icon.hoverBackground` 未定义时的回落） |
| `title-menu-hover` | `#00000012` | **主菜单条目**悬停底。权威 `MainMenu.transparentSelectionBackground`；深色 §6.2 为 `#FFFFFF1A`。注意「菜单项」与「动作按钮」在权威里是**不同的键**，Augit 原先共用一个 `hover` 令牌是错的 |
| `pressed` | `#00000020` | 按下表面**以及工具栏 ToggleAction 的选中态**。权威 `ui.ActionButton.pressedBackground`（ManyIslandsLight 指向 `core-bg-transparent-pressed` = `transparent-black-30`）；与 `hover` 同为正黑叠加、比它更重一档。选中的 toggle 之所以也用它，是因为 `ActionButton.getPopState()` 把 `isSelected()` 喂给 `isPushed` 参数、直接返回 `PUSHED`（第 194 轮）。深色见 §6.2 |
| `search-error-bg` | `#FFCCCC` | 设置搜索**无命中**时的输入框底色。权威 `SettingsEditor` 把编辑器背景设成 `LightColors.RED` = `JBColor.namedColor("SearchField.errorBackground", 0xffcccc, 0x743A3A)`（只有 `intellijlaf` 定该键 ⇒ New UI 走兜底）。深色见 §6.2 |
| `spotlight-border` | `#E08855` | 设置搜索命中项的 spotlight 边框（线宽 2、外扩 1）。权威 `GlassPanel` 的 `Settings.Spotlight.borderColor` 兜底 = `ColorPalette.Orange6`；**登记差异**：权威同时用 glass panel 把命中之外的区域压暗，那层底色是从目标背景现算的（`darker()` + alpha 100）而非常量，Augit 不做压暗层。深色见 §6.2 |
| `search-option-selected` | `#DAE4ED` | **芯片式可选按钮**（查找条与全仓搜索里的区分大小写／全字匹配／正则表达式）**未悬停的选中态**底。权威 `ui.SearchOption.selectedBackground` = `JBColor.namedColor(..., 0xDAE4ED, 0x5C6164)`（只有 `intellijlaf` 主题定义该键 ⇒ New UI 走兜底值）；悬停/按下另取 `selectedHoveredBackground`／`selectedPressedBackground`，默认回落到 `pressed`。深色见 §6.2 |
| `row-hover` | `#00000008` | 表格类列表（Git 历史提交、文件历史）在**持有焦点**时的行悬停底。权威 `ui.Table.hoverBackground`／`ui.List.hoverBackground`，ManyIslandsLight 把两者都指向 `selection-bg-hovered` = `transparent-black-10` = `#00000008`（**半透明覆盖**，与 `hover` 同一原则）；`#EDF5FC` 只是 `JBUI.java:2379` 的代码默认值，参考家族已覆盖 |
| `row-hover-inactive` | `#00000008` | 表格失焦悬停底；项目树、Changes 和引用树不安装行悬停监听。 |
| `success` | `#338555` | 成功、新增状态（非 Diff 场景；Diff 行底用独立令牌，见 §6.2）。权威 `Label.successForeground` = `text-success` = `green-80` |
| `danger` | `#C54E58` | 删除、危险动作、错误。权威 `Label.errorForeground` = `text-error` = `red-80` |
| `warning` | `#A56906` | 警告和超限。权威 `Label.warningForeground` = `text-warning` = `yellow-80` |
| `green-soft` | `#F5FAF3` | 成功提示条底色（`Banner.successBackground` = `feedback-success-bg` = `green-160`） |
| `red-soft` | `#FFF6F5` | 危险提示条底色（`Banner.errorBackground` = `red-160`） |
| `yellow-soft` | `#FFF6E9` | 警告提示条底色（`Banner.warningBackground` = `yellow-160`） |

#> **同名规则的两层结构**：`mockup.css` 里同一选择器的同一属性常有两次声明（先给基础值、后面精修层再覆盖），全文件共 95 处这样「取值不同」的重复。绝大多数是**有意的两层结构**，且**后写的正是已核对的权威值**；由于存在 4 个 `@media` 条件块，基础值在条件块外可能仍然生效，因此**不做大规模清理**。已把三处与规范明显矛盾的基础值改为与生效规则一致的令牌形式：`.augit-window` 的 `44px/23px` → `var(--augit-title-height)/var(--augit-status-height)`（实测 44/28，与 §3 一致）、`.side-tool` 的 `42px` → `var(--augit-project-header-height)`（实测 39）、`.editor-area` 的 `42px` → `var(--augit-tab-height)`（实测 40）。

## 6.2 深色主题

| 令牌 | 色值 | 用途 |
| --- | --- | --- |
| `chrome` | `#26282C` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `panel` | `#191A1C` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `panel-muted` | `#212326` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `border` | `#26282C` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `toolbar-border` | `#393B40` | 文档工具条底边（`Editor.Toolbar.borderColor` = `Gray3`）。深色下这条线**比表面亮**，不是暗线 |
| `border-strong` | `#40434A` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `text` | `#D1D3D9` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `muted` | `#9FA2A8` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `faint` | `#4C4F56` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `icon` | `#C3C5CB` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `line-number` | `#4B5059` | 编辑器行号文字（配色方案 `LINE_NUMBERS_COLOR`） |
| `tabs-border` | `#26282C` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `tab-selected-bg` | `#26282C` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `tab-selected-border` | `#40434A` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `tab-selected-bg-active` | `#233558` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `tab-selected-border-active` | `#2E4D89` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `scrollbar-thumb` | `#80808059` | 取 ManyIslandsDark 的 `ScrollBar.thumbColor`／`Transparent.thumbColor` 覆盖值 |
| `scrollbar-thumb-hover` | `#8080808C` | 取同主题的 `hoverThumbColor` 覆盖值，包含悬停和拖动 |
| `tab-scrollbar-thumb` / `tab-scrollbar-thumb-hover` | `#434344` / `#535455` | 标签栏专用滑块的深色默认值 |
| `accent` | `#3574F0` | 激活工具、主要动作、焦点（`*.focusColor` / `*.underlineColor` = `Blue6`） |
| `accent-brand` | `#3871E1` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `rail-selected-fg` | `#FFFFFF` | 激活工具窗口入口的图标前景（`ui.ToolWindow.Button.selectedForeground`；深色主题仍为白色） |
| `accent-soft` | `#2A4371` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `selection-inactive` | `#33353B` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `history-selection-inactive` | `#43454A` | Git 历史列表失去焦点后保留选择的中性背景（同上） |
| `hover` | `#FFFFFF17` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `row-hover` | `#FFFFFF10` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `row-hover-inactive` | `#FFFFFF10` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `success` | `#57965C` | 成功、新增状态（非 Diff 场景）。expUI_dark 无 successForeground 键，取调色板 `Green6` |
| `danger` | `#F57E84` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `pressed` | `#FFFFFF29` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `action-button-hover` | `#FFFFFF17` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `search-error-bg` | `#743A3A` | 设置搜索无命中时的输入框底色（`SearchField.errorBackground` 的深色兜底值） |
| `spotlight-border` | `#A36B4E` | 设置搜索命中项的 spotlight 边框（`Settings.Spotlight.borderColor` 兜底 = `ColorPalette.Orange4`） |
| `search-option-selected` | `#5C6164` | 芯片式可选按钮**未悬停的选中态**底（`ui.SearchOption.selectedBackground` 的深色兜底值；只有 `intellijlaf` 主题定义该键 ⇒ New UI 走 `0xDAE4ED`／`0x5C6164`） |
| `title-menu-hover` | `#FFFFFF17` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `warning` | `#D59637` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `green-soft` | `#273828` | 成功提示条底色（深色无 `Banner` 键，取调色板 `Green2`） |
| `red-soft` | `#56272B` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |
| `yellow-soft` | `#44321D` | Islands Dark 对应语义别名；具体解析链见本轮整改分册 |

Diff 行底色使用参考实现的配色方案取值，**不**用 `success`／`danger`／`warning` 近似：纯新增行为 `#BEE6BE`（深色 `#294436`）、纯删除行为 `#D6D6D6`（深色 `#484A4A`）、修改行为 `#E7EFFA`（深色 `#25323E`）—— 整行底分两档：没有行内差异的块取 `DIFF_*.BACKGROUND` 全强度，有行内差异的块取 `TextDiffTypeImpl.getIgnoredColor()` = `mix(DIFF_*.BACKGROUND, 编辑器底, 0.6)`（第 223 轮订正，出处见 `nui-behavior/08-diff-merge.md` §2.1）；**行内（词级）高亮**再用全强度的 `DIFF_*.BACKGROUND`（浅 `#BEE6BE`／`#D6D6D6`／`#C2D8F2`，深 `#294436`／`#484A4A`／`#385570`）叠加。注意 `*_LINES_COLOR` 的 `#7FC784`／`#767A8A`／`#88ADF7`（深 `#549159`／`#868A91`／`#375FAD`）是**行号槽实心标记**色族，不是正文行底。仅空白差异的修改行为 `#F7E2CB`（深色 `#52433D`）、未变更区域的折叠分隔底为 `#E4E6EB`（深色 `#2B2D30`）；忽略空白时的行边框取 `IGNORED_*_BORDER_COLOR`（`#7FC784`／`#767A8A`／`#88ADF7`）。文件状态色同样取自配色方案：新增 `#067D17`（深色 `#73BD79`）、修改 `#0033B3`（深色 `#70AEFF`）、删除 `#6C707E`（深色 `#6F737A`）、冲突 `#DE1B2E`（深色 `#DE6A66`）、未跟踪/未知 `#B23247`（深色 `#E88F89`）。状态不能只靠颜色，必须同时显示图标、文字或结构位置。取值出处见 [IntelliJ Platform New UI 数值参考](intellij-platform-ui-reference.md) §5.7。

### 6.3 状态优先级

`danger` 高于 `warning`，`warning` 高于 `accent`，`accent` 高于普通状态。禁用状态始终使用 `faint`，不得使用低透明度导致文字不可读。焦点环在两种主题下均使用 `accent`，宽度为 1px；若背景相近，增加 1px 中性外环。

### 6.4 标题栏按钮状态

以下混色在 sRGB 中以 `chrome` 为底，用于对齐现有视觉稿，不作为新的 PyCharm 实测值：

| 状态令牌 | 规则 |
| --- | --- |
| `title-workspace` | 工作区入口常驻背景：8% `accent` + 92% `chrome` |
| `title-chip-hover` | 工作区、分支入口**悬停**。权威 `HeaderToolbarButtonLook.getHeaderBackgroundColor()` 的透明下拉分支 = `MainToolbar.Dropdown.transparentHoverBackground`：浅 `#00000012`／深 **`#FFFFFF1A`**（按下另用 `title-button-pressed`） |
> **已落地**：整条链是 `MainToolbar.background` = `main-window-bg` = `layer-0-bg` = `gray-150` = **`#E9EAEE`**（浅）／`Gray2` `#2B2D30`（深），而 `MainToolbar.Icon.background` 与 `MainToolbar.Dropdown.background` **与它同值**——即工作区 chip 在权威里**没有独立底色**。Augit 原来的 `--title-workspace: 8% accent + 92% chrome` 属凭空添加，已改为 **`transparent`**（与同色等价，且不依赖 chrome 取值）。

| `title-context-hover` | 当前文件入口**悬停**，同 `title-chip-hover`（浅 `#00000012`／深 `#FFFFFF1A`）；按下与 chip 一致走 `title-button-pressed` |
| `title-button-hover` | 汉堡、搜索、设置、窗口按钮、内嵌文字菜单**悬停**。权威同上函数的图标分支 = `MainToolbar.Icon.hoverBackground`，浅 `#00000012`；深色该键未定义，回落 `ActionButton.hoverBackground` = **`#FFFFFF16`** |
| `title-button-pressed` | 标题栏按钮按下底：浅色 `#00000020`（= `MainToolbar.Icon.pressedBackground`），深色 `#393B40`（该键在 expUI_dark 里是 Gray3，与 `ActionButton` 的半透明白 `#FFFFFF26` **不同**） |
| `title-close-pressed` | 窗口关闭按钮按下：`#C42B1C`，白色图形 |

**细线（分隔线、描边、焦点环）按 DPI 取整到整设备像素**：权威把"1 单位的线"按 `JBUIScale.scale(1) = round(userScaleFactor)` 取整（`JBValue.get()` 同义，`JBValue.java:65-78`；`07-theme-dpi-dialogs.md` §2 明说"分隔线粗细"必须按 DPI 缩放、§2.4 的 `alignIntToInt()` 同义）。CSS 侧的等价写法是 `round(dpr) / dpr` px，落在令牌 `--augit-hairline`（`calc(var(--augit-hairline-device) * 1px / var(--augit-dpr))`，两个变量由 `mockup.js` 的 `applyDeviceScale()` 写；未运行 JS 时回落 1px，与改动前逐值相同）。**引擎限制（登记差异）**：Chromium 会把 `border-*`／`outline` 的宽度取整到整数 CSS 像素，因此令牌只在 `height`/`width` 画出来的分隔线与 `box-shadow` 描边这类能表达小数的属性上生效；边框仍等于 1px（不变差），参考实现的 `scale(1)` 对边框的那点影响在 CSS 里无法表达。另登记：参考实现的 `patchLafFonts` 还会把界面字号折进 `userScaleFactor`（`LafManagerImpl.kt:857-885`），Augit 的细线只按 DPI 取整 —— 长度模型把字号影响烘进各自公式（`07` §2 的映射里已接受），再叠一次会重复计算。
**工具条图标按钮（`.icon-button` / `.toolbar-button`）的背景圆角半径为 3px**（第 86 轮再由 `EditorToolbarButtonLook` 印证：它未覆写 `getButtonArc()` 且委托 `SYSTEM_LOOK` ⇒ 仍是 `BUTTON_ARC` = 6 ⇒ 半径 3）；**其悬停底取 `ActionButton.hoverBackground`**（浅 `#00000012`／深 `#FFFFFF16`，深色是 `MainToolbar.Icon.hoverBackground` 未定义时的回落），按下底取 `ActionButton.pressedBackground`（= `--augit-pressed`，浅 `#00000020`／深 `#FFFFFF26`）；（权威 `IntUiBridgeIconButton.kt:24,55`：`CornerSize(DarculaUIUtil.BUTTON_ARC.dp / 2)`，`Button.arc` = 6 ⇒ 3；同文件 `minSize` = 24×24、`padding` = 0，而 Augit 现为 27×27／28×24 —— 该尺寸来自本规范 §4 的字号适配（如 `max(27px, h + 6px)`），Jewel `IconButton` 的 24×24 属 Compose 栈、**不适用**于 Swing 等价物的 HTML 复刻（`ui-classification.md` §1.21），**不再列为待核**）；**标题栏按钮与文字入口的背景圆角半径为 6px**（权威 `MainToolbar.Button.arc` = `UIInteger("MainToolbar.Button.arc", 12)`，arc 是直径 ⇒ 半径 6、标题栏下拉（工作区 chip／分支 chip／当前文件入口）的内容内距取权威 `MainToolbar.Dropdown.borderInsets` = New UI 下的 `insets(5, 10, 5, 6)`（`JBUI.java:1383`、标题栏按钮尺寸取 `MainToolbar.Button.size` = **30×30**（`JBUI.java:1274-1278`）、图标取 `MainToolbar.Button.iconSize` = **20**（`:1286-1299` 默认 20，主题未覆盖；30 − 20 = 左右各 5，与 `MainToolbar.Icon.insets` = `6,5,4,5` 的左右值一致）、标题栏下拉最大宽取 `MainToolbar.Dropdown.maxWidth` = **350**（两套家族一致）；`MainToolbar.Dropdown.hoverArc()` 与 `Dropdown.arc` 同值 12 ⇒ 半径 6，见 `:1391`）；窗口标题栏工具条走同一个 `HeaderToolbarButtonLook.hoverArc()`）；左侧工具轨采用当前 Islands 路径：命中区 37×40px、背景 30×30px、20px 专用图标、6px 背景圆角；上下内距各 5px、左右为 5/2px，同组中心步长 40px。此规则替代旧扩展路径的 32×32／16px 推断。可见未激活入口保持 pressed 背景，激活入口保持蓝底白图形，hover 与键盘 focus 不改变活动工具窗身份；轨道按钮**未选中**的前景取权威 `ToolWindow.Button.foreground`、**分隔线**取 `ToolWindow.Stripe.separatorColor`（浅 = `core-border-transparent` = `transparent-black-30` = `#00000020` 的**淡叠加**，不是实色灰；深 = `#43454A`）、 = 浅 `gray-70` `#5F6269`／深 `#9DA0A8`，**选中**底与前景取 `ToolWindow.Button.selectedBackground` = `accent-brand-bg`（浅 `blue-80` `#3871E1`／深 `#3574F0`）与白字——Augit 的 `--augit-accent-brand` 本来就与这两个值一致；**菜单项选中圆角 4px**（权威 `PopupMenu.Selection.arc` = 8 ⇒ 4，`IntUiBridgeMenu.kt` / `IntUiBridgeSimpleListItem.kt` 同样除 2）；**CSS 去重以"选择器+属性+值三者全同"为界**（第 107 轮）：这类重复中后一处必胜（选择器相同 ⇒ 特异性相同），但**若后一处在 `@media`/`@supports` 内则不然** —— 媒体查询不成立时先前那条才生效。因此去重必须排除 at-rule 内的规则；本轮据此删除 40 处，并用"新旧 CSS 逐元素计算样式对照"验证为 0 差异。**对话框标题行左右内距均为 12**（第 106 轮收尾）：第二条 `.dialog-header` 基类的 `padding: 0 13px` 与 reset／rollback 的右侧 13px 都改为 **12**，与 `getRegularPanelInsets()` 的左右 12 一致；reset／rollback 左侧保留 16px（标题图标实测左边距）。**`.compact-input` 是活类**（`mockup.js` 的 `compactInputDialog()` 与"跳转行"页在用），属 COMPACT 风格：权威里 COMPACT 的内容边框是 `JBUI.Borders.empty()`，但其内容组件自带 16px 内距并不被禁止，故保留待核。**对话框内容（正文 .dialog-body）的内距取权威 `UIUtil.getRegularPanelInsets()` = `(8, 12, 8, 12)`**（`UIUtil.java:370-371` 的 `REGULAR_PANEL_TOP_BOTTOM_INSET = 8`、`REGULAR_PANEL_LEFT_RIGHT_INSET = 12`，经 `DialogWrapper.createDefaultBorder()` 作用于内容面板）：第 105 轮把 11 条 body 规则的横向 16/17px 与纵向 15/17px 统一为 **`8px 12px`**（4 个滚动容器保留其纵向 0、横向改为 12）。`.compact-input .dialog-body` 例外未动 —— 它的名字对应 `DialogStyle.COMPACT`，而权威里 COMPACT 的内容边框是 `Borders.empty()`。**对话框底栏与标题行的内距取权威 `DialogWrapper` 的两个 border**（第 104 轮）：New UI 布局路径（`DialogWrapper.java:1522`）里 south section 是 `JBUI.Borders.empty(0, 12, 8, 12)` ⇒ **左右 12**（COMPACT 路径的 `:843` 也是 `empty(8,12)`）；内容/标题侧走 `createDefaultBorder()` → `UIUtil.getRegularPanelInsets()`。因此 8 个对话框页脚的横向内距由 16/17/22px 统一改为 **12px**（`.push-dialog` 保留其顶部 12px），5 个操作类对话框标题行的 `padding-left: 25/26px` 改为 **12px**（reset／rollback 因有标题图标保留 **16px**，与参考图实测的图标左边距一致）。各对话框**body** 的内距（15px 17px 等）未动，属另一议题。**表单控件与主菜单条目的高度/内距必须随字号缩放**（第 119 轮）：① `.reset-dialog` 的文本框与下拉框此前被写死 `height: 30px`（第 95 轮为解开与按钮变量的耦合），大字号下 `clientHeight < fontSize` 被验收判为"裁剪"，现改为 `var(--reset-field, 30px)`，`--reset-field` 由 `mockup.js` 按 `Math.max(30, line + 10)` 计算（与 clone/stash 的 `field` 同式）；② 主菜单条目原与 `.top-button` 共用 `padding: 0 5px`（合计 10px），而它是**下拉**，权威 `MainToolbar.Dropdown.borderInsets` = `insets(5, 10, 5, 6)` ⇒ 左 10 + 右 6 = 16px，已拆开单独设置。**对话框底栏高度必须容得下按钮**（第 118 轮）：`push` 的底栏公式原写 `Math.max(53, line + 25)`，而底栏实际高度 = 按钮 `Math.max(37, line + 17)` + 顶部内距 12 = `line + 29`；大字号下按钮因此溢出对话框底边（`verify-ux-push` 在 `ui-size=40` 实测溢出 3px）。已改为 `line + 29`。其余对话框的底栏用的是 `button + 23/25`，本就会随按钮缩放，无需改动。**分段组（`.segmented`）容器无底色、边框取 `Component.borderColor`**（第 125 轮）：基类注释已记录权威「`SegmentedButtonComponent.paint()` 只调 `super.paint()` 未设背景、Jewel 的 `SegmentedControlColors` 也没有 background 字段」，但深色曾单独加过 `background: var(--augit-panel-muted)`，与之一致性矛盾，已删除（两主题都透出父容器底色）。容器边框浅 `#D1D3D9`／深 `Gray5 #4E5157`；几何自洽式：组宽 87 = 边框 1 + 内距 2 + 按钮 40 + 间隙 1 + 按钮 40 + 内距 2 + 边框 1，按钮距组边缘 3（= `getBorderInsets()` = BW(LW)）。**焦点环统一用 `accent`**（第 117 轮）：`design-system` 一贯写"焦点使用命中区内侧 1px **accent** 圆角边框"，但实现里有 14 条焦点态规则写成了 `--augit-blue`（浅 `#3574F0`，比 accent 的 `#3871E1` 偏亮）。现全部改用 `--augit-accent-brand`；覆盖 `.message-field:focus`、各处 `:focus-visible`、对话框与工具栏/终端的 `:focus::after` 等。检查器 `verify-ux-frame-buttons`／`document-button-states`／`conflict` 里深色 accent 的错误期望 `Blue8 #548AF7` 也订正为权威的 `Blue6 #3574F0`。**对话框底栏（south panel）没有分隔线**（第 103 轮）：权威 `DialogWrapper.java:838-845` —— 只有 `getStyle() == DialogStyle.COMPACT` 时才给 south panel 加 `CustomLineBorder(…, 1, 0, 0, 0)` 的 1px 顶边，普通对话框只加 `JBUI.Borders.emptyTop(8)`；2026 参考图 Confirm Exit 实测确实**没有**分隔线。Augit 原先在 `.dialog-footer` 上写了 `border-top`，已删除；底栏基类内距同时按权威取 **`8px 12px`**（`JBUI.Borders.empty(8, 12)`）。注意各对话框仍有自己的页脚内距覆写（16/17/22px），那是视觉稿对齐值，**未改动**，列为待核。**破坏性确认框（reset／rollback）的标题图标**（第 102 轮落地）：2026 参考图 Confirm Exit 里，标题前有一个 **27.3 逻辑px** 的蓝底白问号图标、**左边距 16 逻辑px**；配色取官方 `general/questionDialog.svg`（浅 `#4682FA`／深 `#548AF7` + 白色问号）。Augit 用 `.reset-dialog/.rollback-dialog .dialog-header::before` 实现 —— `.dialog-header` 本身已是 flex 容器，伪元素即第一个 flex 项，**无需改 DOM 或 JS**；同时把这两个对话框标题行的左内距 25px 改为 16px（原 25px 是空位）。**第 307 轮把「哪些对话框有图标、画哪一个」按权威定性并补齐**（用户裁决「完全参考 intellij-community」）：① `Messages` 系的确认框用 `getQuestionIcon()` —— 删除 Stash（`GitStashUtils.kt:86`）、删除引用（`GitBranchUiHandlerImpl.java:179` 的 `showOkCancelDialog`）、移除 Worktree（`GitCheckoutInOtherWorktreeDialogs.kt:54`），加上原有的 reset／rollback，统一为 28px 蓝底白问号（浅 `#4682FA`／深 `#548AF7`，即 `--augit-dialog-icon-bg`），左边距 16px；② `GitInit.java:72` 的「目标已在 Git 下」警告用 `getWarningIcon()` ⇒ 橙底白叹号（浅 `#FFAF0F`／深 `#F2C55C`，新增令牌 `--augit-dialog-warning-bg`；官方 `general/warningDialog.svg` 是圆角三角，这里用 `clip-path` 三角近似 + 文本叹号，与问号图标的做法一致）；③ 自定义 `DialogWrapper`（`GitStashDialog`／`GitCloneDialog`／`GitPushDialog`／`GitNewBranchDialog`）都没有 `setIcon` ⇒ 不画标题图标，Augit 的 stash／clone／push／远端等对话框因此保持无图标；④ 紧凑输入窗口（新建标签／重命名／跳转行）对应权威的**常规** `Messages.showInputDialog`，Augit 用紧凑适配、形状不同 ⇒ 同样不画图标。**订正旧记述**：那 5 个操作类对话框的 `padding-left: 25/26px` 空位早在第 104／105 轮就归一为 **12px**，此处不再保留空位。**滚动条滑块取权威 `ScrollBar.Transparent.thumbColor` = `key(0x33737373, 0x47A6A6A6)`**（`ScrollBarPainter.java:113`，Java 字面量是 ARGB、前两位为 alpha）⇒ 浅 `#73737333`／深 **`#a6a6a647`**；第 101 轮订正：早先记的"深浅同值"不成立（两个 New UI 家族虽都不覆写该键，但代码默认自带深色一半）。同文件 88-122 行其余默认：`hoverThumbColor` = 浅 `#73737347`／深 `#a6a6a659`、`hoverThumbBorderColor` = 浅 `#59595947`／深 `#38383859`、`Transparent.thumbBorderColor` = 浅 `#59595933`／深 `#38383847`；**悬停档未实现**——`scrollbar-color` 表达不了 hover，而 `::-webkit-scrollbar` 与标准属性混用时 Chromium 优先采用标准属性，需整体改用 webkit 方案。**失焦选中色统一取权威 `selection-bg-inactive` = `gray-150` = `#E9EAEE`**（浅色）：通用行与**历史（git log）列表**都用它（第 99 轮把历史列表误用的 `#dfe1e5` 改正 —— 那是 `--augit-text` 的深色值）；深色两者同为 `#43454a`（expUI_dark 没有 `selection-bg-inactive` 键，按层级反向取 Gray4）。**已删除的零引用令牌**（第 98 轮）：`--augit-blue-hover`、`--augit-green`、`--augit-green-soft` —— 全库 0 次 `var()` 引用、C# 外壳亦无；Diff 相关配色用的是 `--augit-diff-added/deleted/modified`（第 223 轮起浅色为 `#bee6be`／`#d6d6d6`／`#e7effa`，行内层另有 `--augit-diff-inline-*`），与它们无关。**工作区/项目徽标（`.brand-mark`）= 20×20、圆角 3**（第 97 轮统一）：此前有三层——已被覆盖的死值 23px/5px、17px、以及 `.workspace-chip > .brand-mark` 的 20px。按内部一致性（chip 内一直是 20px）与相关权威键 `recentProjectAvatarIconSize()` = 20 统一到 **20px**，并删除死值；参考图实测徽标约 18 逻辑px（第 63 轮，读图精度有限），17 与 20 都在误差内，故取与既有 chip 一致且有关联权威键的 20。**菜单项行高 = 28px**（第 96 轮）：2026 参考图 `vcs-operations-popup.png`（1.5x 已复核）实测选中色带 42 物理px ÷1.5 = **28 逻辑px**；权威里**没有**菜单项高度的键（`JBUI`／`Editor`／`PopupMenu` 都没有），而 Augit 的 29（基类）／30（`.context-menu`）也无依据，故统一为 28；菜单内的 `.menu-separator` 仍为"1px 线 + 上下各 1px 边距"（合计 3，第 66 轮按 `PopupMenuSeparator.height` 落地）。**对话框按钮高度 = 37px**（第 95 轮统一）：权威 `Button.minimumSize` = 72×28 只是**下限**，2026 参考图 Confirm Exit 实测 **37.3 逻辑px**（该图缩放已复核为 1.5x），故取 37；此前同一产品里并存 28／30／31 三种高度，现由 `mockup.js` 各对话框的 `button = Math.max(37, line + 17)` 与 CSS 侧 fallback 统一到 37。**注意**：这些高度**由 JS 在运行时计算**（`mockup.js` 用模板拼接 `--<name>-button` 等变量名），改 CSS fallback 不足以改变实际渲染，必须同时改 JS 公式；同时把字段/行高对 `--<name>-button` 的**借用**解开了（reset 文本框与 stash 复选行改为固定 30px），clone 的按钮也从 `--clone-field` 改为独立的 `--clone-button`。**复选框/单选框的图标与文字间距取权威 `ui.CheckBox.textIconGap` / `ui.RadioButton.iconTextGap` = `4`**（两套 New UI 家族一致；第 92 轮把 `.check-line`/`.radio-line` 及其对话框专用覆写里的 8px 全部改为 4px）。同组键还有 `iconSize` = **24**（画布）与 `borderInsets` = `4,4,4,4`：官方 SVG 是「24 画布中方框 15×15、rx 2.5」，所以 4.5 的边距正是那 4 个 inset、可见方框 15px 与 Augit 的 `15×15` 一致（圆角 3 与 `rx 2.5` 差 0.5px 未动）；`ui.CheckBox`/`ui.RadioButton` 只给了这些键、**没有**行高键，`.check-line` 的 `min-height: 30px` 暂留，**对话框底取 `dialog-bg` = `layer-1-bg` = `gray-160` = `#F7F8F9`**（亮色 layer 链：`layer-0-bg` = `gray-150` = `#E9EAEE` 撑起 chrome、`layer-1-bg` = `#F7F8F9` 是面板/对话框层、`layer-2-bg` = white 是内容层；2026 参考图 Confirm Exit 实测对话框底正是 `#F7F8F9` ✓。深色主题**没有** layer 定义，按"深色层级反向"layer-1 即 `Gray1` `#1E1F22`，与 `--augit-panel` 现值一致故不变。弹层与搜索浮层仍用 `popup-bg` = `#FFFFFF`，因此第 91 轮把 `.dialog` 的背景单独覆盖，不再与 `.popover`/`.search-overlay` 共用），**主按钮底色取 `Button.default.startBackground`/`endBackground`**（浅 `control-brand-bg` = `accent-brand-bg` = `blue-80` = `#3871E1`、深 `Blue6` = `#3574F0`，正好等于 `--augit-accent-brand` 的浅/深值；2026 参考图 Confirm Exit 的主按钮实测 `#3871E1` ✓；第 90 轮由 `--augit-blue` 改正），编辑器**查找框底色**取权威 `Editor.SearchField.background` = `editor-bg-inline`（浅 `#F7F8F9`／深 `Gray1` `#1E1F22`，第 89 轮落地），**主框架几何按当前 PyCharm 前台配置**：`tab-height = max(40, h + 12)`、`tree-height = max(24, h + 8)`、`status-height = max(28, h + 12)`；当前界面字号 12px 时默认值分别为 **40／24／28**。CSS 兜底令牌同步为 40／24／28，避免静态视觉稿首帧与实时外壳出现尺寸跳变。**同时该检查器只允许 400/600 两档字重**，第 102 轮给对话框标题图标写的 `font-weight: 700` 已改回 **600**。**当前差异块高亮（第 114 轮实现 → 第 223 轮删除）**：`.diff-current` 这一层在权威里没有对应物（`DiffDrawUtil.PaintMode` 只有 `DEFAULT`／`IGNORED`／`RESOLVED`（无底+点线边框）／`EXCLUDED_*`），第 223 轮按权威删除 —— `live-data.js` 的 `moveDiffChange` 只保留定位与触发按钮焦点，`mockup.css` 删掉 3 条规则与 6 处令牌定义。检查器由原来的 `check-diff-current.test.cjs` 改为 `tools/audit/check-diff-inline.test.cjs`。**Diff 的两层颜色（第 113 轮修正，第 223 轮订正取值）**：① **整行底** ＝ 按权威两档规则取 增 `#BEE6BE`／删 `#D6D6D6`／改 `#E7EFFA`（第 223 轮订正；原记的 `#EDFCED`／`#F4F7F9` 是错认，理由见后段"第 223 轮订正"）；② **行内（词/段级）高亮的强色** ＝ `DiffColors.DIFF_*.BACKGROUND` 全强度（浅 `#BEE6BE`／`#D6D6D6`／`#C2D8F2`）。第 113 轮把参考图里宽度 590.7 的强色带读成"选中差异的整栏背景"，**第 223 轮的逐行复测推翻**：那是**一行**的高度（`y623–661` ≈ 26 逻辑px），且权威的整行取色随"该变更块有没有行内差异"分两档（`DiffViewerHighlighters.createHighlighter` 的 `ignored = !resolved && innerFragments != null` ⇒ `getIgnoredColor()`），并非"当前差异"层。第 112 轮"标记条贴行号槽、宽 65px"的判断也一并作废（行号槽色族是 `*_LINES_COLOR`，不是正文行色）。**Diff 的三层颜色已分清**（第 111 轮，第 223 轮订正②③）：① **整行底**（`.diff-code-line.*` 的整行背景）＝ 增 `#BEE6BE`／删 `#D6D6D6`／改 `#E7EFFA`（深 `#294436`／`#484A4A`／`#283541`）；② 第 111 轮把"标记条/词级层"记成 `LineStatusMarkerColorScheme` 的原始方案色（`ADDED/DELETED/MODIFIED_LINES_COLOR`），并说参考图里细条处实测的 `#BEE6BE`／`#C2D8F2` "正是 Augit 原有的两个强色" —— **层归属记错**：那两个值是 `DIFF_*.BACKGROUND`（整行底与行内高亮层），`*_LINES_COLOR`（浅 `#7FC784`／`#767A8A`／`#88ADF7`）才是行号槽实心标记；③ 深色整套与行内层当时都未实现（`grep diff-word|word-diff|diff-inline` 为 0）—— **第 223 轮已实现行内层并订正两档整行底**。**Diff 行底色 = 权威 `DIFF_*.BACKGROUND` 的两档取色（第 110 轮起，第 223 轮订正）**：真实 Diff 视图（`diff-viewer-ctrlD-file.png` 等，1.5x）里纯新增行的整行底实测就是 `#BEE6BE`、有行内差异的修改行是 `#E7EFFA`（= `mix(#C2D8F2, #FFFFFF, 0.6)`），行内层是 `#C2D8F2` —— 三者同图并存、与 §2.1 的两档规则逐值吻合。第 110 轮曾把 `#EDFCED` 当成"增行软底"（它其实是语言注入片段底色，见下段），**第 223 轮已改回权威值**。**编辑器配色方案**（`expUI_lightScheme.xml`／`expUI_darkScheme.xml`）是第 110 轮启用的新权威来源：`--augit-line-number`（`AEB3C2`／`4B5059`）、`--augit-file-added`（`067D17`／`73BD79`）、`--augit-file-deleted`（`6C707E`／`6F737A`）、`--augit-file-modified`（`0033B3`／`70AEFF`）与之**逐值一致** ✓。****第 223 轮订正（原"遗留"）**：浅色"软行底" `#EDFCED`／`#F4F7F9` 是第 110 轮的错认 —— `#EDFCED` 在参考图里于 `y738..892` 段**左右两栏同时满宽覆盖**（同一张图里的纯新增行已被量到是 `#BEE6BE`，一个截图不可能有两个增行底色），且它逐字等于 `INJECTED_LANGUAGE_FRAGMENT.BACKGROUND`（语言注入片段底色，Augit 无语言服务 ⇒ 不适用）；`#F4F7F9` 在差异正文两栏内一个像素都没有，等于浅色 `layer-1-bg`（面板底）。现整行底按权威两档规则取 `#BEE6BE`／`#D6D6D6`／`#E7EFFA`（深 `#294436`／`#484A4A`／`#283541`），并新增**行内词级高亮层**（`DIFF_*.BACKGROUND` 全强度，见上）。**深色弹层/对话框的层级关系已定**（第 109 轮，此前记为"深色无解"）：`ManyIslandsDark.theme.json` 的 layer 链给出答案 —— `popup-bg` = `layer-1-bg` = `gray-30` = **#26282C**、`dialog-bg` = `layer-0-bg` = `gray-10` = **#191A1C**，即**深色下对话框比弹层更暗**，与浅色（dialog = `layer-1` #F7F8F9 < popup = `layer-2` #FFFFFF）**次序一致**。Augit 原先深色两者同为 `Gray1`，现把弹层/搜索浮层改用新增的 `--augit-popup-bg`（浅 #FFFFFF／深 **Gray2 #2B2D30**，取 expUI_dark 灰阶中最接近 #26282C 的一档），对话框仍是 `--augit-dialog-bg`（浅 #F7F8F9／深 Gray1 #1E1F22）。**弹层与菜单的底色取权威 `PopupMenu.background`**（第 108 轮补全链条）：New UI 的弹层/菜单由 Jewel 渲染，`IntUiBridgePopupContainer.kt:21` 与 `IntUiBridgeMenu.kt:39` **都读 `PopupMenu.background`**（不是 `Popup.background`）；浅色该键 = `popup-bg` → `layer-2-bg` → **#FFFFFF**（实测三个菜单页均为白 ✓），而**对话框**走 `layer-1-bg` = #F7F8F9（第 91 轮）—— 两层之分在权威里成立。深色下 `ui.PopupMenu.background`、`colors.popup-bg`、`ui.Popup.background` 与 `Panel.background` **全部未定义**，Jewel 会拿到 `Color.Unspecified`、Swing 对话框落到旧默认（`#CDCDCD`／`#3C3F41`），故深色取值**无法由主题判定**，保留 Augit 的 Gray1 `#1E1F22`（依据"深色层级反向"）。**浮层/对话框/搜索浮层的 1px 边框色取权威 `ui.Popup.borderColor`**（浅 `#D1D3D9`／深 `#43454A`，即 `popup-border` 令牌；深色原先有一条特异性更高的覆盖写 `--augit-border-strong` = `Gray5 #4E5157`，第 88 轮已改），**菜单/浮层容器圆角 8px**（权威 `IdeaPopupMenuUI.CORNER_RADIUS` = `UIInteger("PopupMenu.borderCornerRadius", 8)`，名字即半径、桥接 `IntUiBridgePopupContainer.kt` 不除 2、**浮层内列表选中项圆角 4px、左右内缩 8px**（权威 `Popup.Selection.arc` = 8 ⇒ 半径 4、`Popup.Selection.leftRightInset` = 8，`JBUI.java:1640-1641`；浮层搜索框自身取 `Popup.SearchField.borderInsets` = `insets(4, 12)`、`inputInsets` = `insets(4, 8, 8, 2)`，`JBUI.java:1594/1598`）；2026 参考图里工具栏溢出菜单面板的角实测约 13.5 物理px ÷ 1.79 ≈ 7.5，吻合）；**文件树选中行圆角 4px**（权威 `Tree.Selection.arc` = 8 ⇒ 4，`IntUiBridgeLazyTree.kt:49`）。 菜单几何另按权威补齐：浮层左右内距按选中框的 `outerInsets` 取 **7px**（2026 参考图 vcs-operations-popup 实测选中色带距弹层边缘约 7 逻辑px，两者吻合）、`PopupMenuSeparator.height` = 3（整条区域高，其中线本身 `stripeWidth` = 1、左缩进 `stripeIndent` = 1，见 `DarculaMenuSeparatorUI.java:15-17`，两套 New UI 主题都未覆盖），菜单项横向内距取 `PopupMenu.Selection.innerInsets` = `insets(0, 6)`（`JBUI.java:1672`，New UI 分支），选中框相对弹层的 `outerInsets` = `insets(1, 7)`（`:1676`）。改动列表与 Git 日志表的行圆角在 Swing 侧查不到对应键（`WideSelectionListUI` 不含 arc），暂留 5px 并记为待核。焦点使用命中区内侧 1px `accent` 圆角边框，不改变尺寸。普通图标使用 `icon`，悬停或按下时使用 `text`；文字入口使用 `text`。禁用时使用 `faint`，不显示悬停、按下或焦点环；工作区保留常驻底色，其他入口恢复 `chrome`。同一按钮内部移动不反复使其失效重绘。

### 6.5 主工具栏的项目配色渐变

颜色索引保存在 `%LOCALAPPDATA%/Augit/WebView2/Shell/Session` 的本地存储 `augit.project.colors`，按工作区规范化路径恢复，首次分配使用本次蓝色参考索引 4（对应 IntelliJ 零基索引 3），后续工作区按九色顺序分配。路径大小写和分隔符变化、重启、主题变化不重新分配；不读取或改写 `.idea`，不新增配色设置入口。

New UI 的主工具栏背景**不是固定颜色**，而是按当前项目的配色绘制的水平渐变。Augit 采用同一机制，使标题栏左侧呈现参考界面那种轻微冷蓝渐变。

| 参数 | 规则 |
| --- | --- |
| 项目配色数 | 9 套，与参考一致 |
| 渐变半径 | 300px（逻辑像素），中心向左右各延伸，左端在窗口左边缘截断 |
| 渐变中心 | 跟随标题栏项目部件（工作区入口）图标的水平中心 |
| 饱和度 | 0.85 |
| 起始色 | 起始色 = 项目配色按 0.85 与 `chrome` 基础底混合（sRGB 逐通道）；渐变的另一端为纯 `chrome` |
| 未分配配色 | 使用 `mix(panel, 项目自定义色, 0.18)` |

9 套配色的起始色取自参考实现 `RecentProject.Color{1..9}.MainToolbarGradientStart`：

- 浅色（ManyIslandsLight）：`#F7D7CB`、`#F5E6B8`、`#E2F2C9`、`#CBE4F7`、`#D4D2FC`、`#ECD3F5`、`#E4D3F5`、`#CEF0E9`、`#CEEDD4`
- 深色（ManyIslandsDarcula）：`#664832`、`#5E4724`、`#45522F`、`#335661`、`#394773`、`#613861`、`#503D70`、`#325959`、`#365439`

这两组是"配色按**头部明暗**取值"，不是按主题明暗取值：浅色但深色头部的变体用的是接近深色主题的那一组（9 套中仅第 3 套不同）。Augit 浅色用浅色头部，因此取第一组。具体出处与实测验证见 [IntelliJ Platform New UI 数值参考](intellij-platform-ui-reference.md) §3.1。

渐变只作用于主工具栏；状态栏、工具窗口、编辑区不使用渐变。主题切换不改变渐变的几何与半径，只替换起始色与基础底。

## 7. 图标系统

### 7.0 复原边界

- 对产品规格已经存在的每个功能入口，视觉结果必须以 PyCharm 2026.2.1 New UI 中对应场景的图标为目标，按逐像素对照修正。对照范围包括轮廓、比例、内外留白、线宽、端点、填充、颜色、状态、中心点和命中区；“语义相同但外形不同”的替代图标不算完成。平台抗锯齿造成的边缘像素差异作为渲染差异记录，不能掩盖几何差异。参考场景本来没有图标时保留空列，不从其他场景移入同名图标。
- 复原的是用户可见结果，不复制 JetBrains 的官方图标资源文件、商标、产品标志或私有实现。应用内使用 owner-draw 或其他项目内自绘等效实现，绘制结果应与参考图标保持一致。
- 只复原产品规格已经列出的动作。PyCharm 中 Augit 没有对应能力的入口不得为了视觉相似而新增；删除或裁剪后的空间按 PyCharm 的剩余布局规则重新对齐。
- Augit 的品牌 `A` 标记是唯一不要求与 PyCharm 相同的图形。它只能出现在品牌位置，不能替代任何功能图标，也不能改变功能图标的几何和对齐规则。

### 7.1 绘制规则

2026-10-10 场景选图补充：JS 使用黄色 JS 徽标，TS 使用蓝色 TS 徽标，CSS 使用蓝盾，Python 使用蓝/黄双层形，`.editorconfig` 使用专用齿轮，`.gitignore` 使用灰色斜线圆。分支树依 `GitBranchesTreeIconProvider` 选择普通分支、当前、收藏与组合图形；`main/master` 和 `origin/main/master` 使用源码默认收藏，明确的 `isFavorite` 优先。日志引用依 `TagPainter` 绘制独立或叠加色标，不能复用分支树图标。对应场景、颜色令牌和测试范围见 [场景整改](scene-alignment-2026-10-10.md)。

- 通用功能图标使用 16×16px 绘图网格，默认放置在 32×32px 命中区中心；网格不等于可见笔画必须撑满 16px。标签关闭、下拉箭头、菜单和紧凑工具栏使用对应组件登记的图形尺寸和命中区。
- **描边图标默认使用 1px 逻辑线宽**（expUI 集合的描边图标普遍不声明 `stroke-width`，取 SVG 默认值 1）；少数图标显式更粗，例如勾形为 1.5px 圆头圆角。**但参考实现有两套图标集，线宽不同**：`platform/icons/src/expui/**` 为 1，而 `expui/` 以外的经典集为 **1.75**；New UI 未提供 expUI 版时会回落到经典集（例如分支图标用的是经典 `vcs/branch.svg`，线宽 1.75）。取图标时必须先确认它来自哪一套，不能一律按 1px 处理。**多数功能图标其实是填充路径而非描边线条**（加号、减号、窗口最小化/还原/关闭、齿轮、提交圆环、文件夹角标等），绘制时应按对应参考图标的实际形式表达，不能一律画成描边。实际线宽、填充、端点和转角按对应图标登记；复制图标使用 1px 细轮廓等已校准差异不能被通用值覆盖。逐图标的测量见 [New UI 图标参数](nui-behavior/09-icons.md)。
- 默认圆端、圆角只用于符合该特征的参考图形；参考图标的方端、折角或实心部分必须保留，不为形式上的统一重画另一套图形。
- 图形在 16px 网格内保留至少 1px 安全边距；不能因路径超出网格而视觉偏移。
- 图标颜色由状态令牌决定：普通为 `icon`，悬停和焦点为 `text`，激活为白色置于 `accent-brand`，禁用为 `faint`，危险动作为 `danger`。
- 图标按钮必须注册中文悬停说明和辅助技术名称。说明使用动作名称，例如“刷新文件树”“显示提交详情”。

### 7.2 图标布局

- 默认工具栏按钮中心点按 32px 步长排列；紧凑工具栏按对应组件令牌排列。同一区域不得混用临时偏移，也不能把菜单行高当作图标缩放系数。
- 工具栏中可见图标之间不再额外添加不规则 margin；组间默认使用 8px 或 12px 间隔，参考场景的不同间距须先登记再共用。
- 图标旁有文字时，图标与文字间为 8px；文字与下拉箭头间为 4px。
- 工具窗口标题和标签栏的更多菜单使用纵向三个等距实心点，**点中心间距 5px、圆点半径 1px（可见直径 2px）**；不能用横向三点或 Unicode 字符替代。（第 314 轮按权威与实现订正：权威 `platform/icons/src/expui/general/moreVertical.svg` 是 `cx=8, cy=3/8/13, r=1`，即间距 5、直径 2；实现 `mockup.js:1146` 的 `ellipsis-vertical` 与之逐值相同。此前登记的「间距 4px／可见直径约 1.6px」与两处都不符，疑似把 `menu` 图形三条线的间距 4 误套到三点上。）
- 下拉箭头、展开箭头和折叠箭头使用同一套几何比例，只改变方向。
- 禁止用“看起来接近”的系统图标临时补位；功能图标必须补齐为 PyCharm 可见几何的自绘等效图标并登记语义。补齐前可以使用明确的文字动作保持已有能力可达，不得隐藏已有动作。

| 已有入口 | 原图中的几何与实现要求 |
| --- | --- |
| 提交 | 单一空心圆点与左右水平线，线条终止于圆周，不使用滑杆图标 |
| 项目 | 文件夹轮廓，页签和主体保持同一连续边线 |
| Git 历史 | 分支连线与两个空心节点，连接线不得穿过圆心 |
| 终端 | 矩形轮廓内的提示箭头与短横线 |
| 收起工具窗口 | 居中横线；关闭标签或结束终端才使用叉号 |
| 设置 | 连续六齿外轮廓与空心内孔，不使用分离的辐条 |
| 刷新 | 两段平滑弧线与箭头；弧线使用现有 GDI+ 绘制，不以多段折线近似圆弧 |
| 显示 Diff / 比较 | 上半箭头向左、下半箭头向右，两个短箭头水平错开，中间不连接 |
| 回滚 | 向左箭头位于上半部，右侧半圆弯回下半部短横线，不能用普通左箭头替代 |
| 预览 / 显示提交详情 | 杏仁形上下连续曲线和独立空心瞳孔；不能画成菱形或两个椭圆 |
| 定位当前文件 / HEAD | 圆环与四个朝内短刻度，中心留空，不画穿过圆心的十字 |
| 折叠项目树 | 上下两枚向内相对的折线，中心留空，不使用同向双下箭头 |
| 提交工具栏托盘箭头 | 下箭头与带凹口的闭合托盘保持分离；保留现有展开分组命令，不增加 Git 动作 |
| Stash 管理列表 | 与"提交工具栏托盘箭头"**同一份几何**（权威 `vcs-impl/resources/icons/new/stash.svg` 与 `expui/vcs/shelve.svg` 同形）；不得用带盖箱形替代 |
| 更新项目 / 推送 | 两条互为 **180° 旋转**的斜向箭头，头部是直角折角而不是三角头：更新项目朝左下、推送朝右上 |

以上入口统一由 `web/src/mockup.js` 的 `toolbarIconShapes` 登记几何、经 `icon()` 输出，并与 `docs/ux-mockups/mockup.js` 字节一致（第 29–36 行登记的"当前实际落点"同样适用于本节）。标签关闭叉号使用 1px 线宽，**更多菜单圆点为 `r=1`（可见直径 2px）、点中心间距 5px**（同 §7.2，按权威 `moreVertical.svg` 订正；原记的"约 1.6px"与实现不符）；复制图标依据可见菜单样本使用 1px 细轮廓，前页保留三条短文字线，是小型辅助图形的明确例外；其余描边图标按 §7.1 使用 1px，未提供 expUI 版而回落到经典集时为 1.75px。

工具栏、历史工具区和右键菜单共用相同语义的图标绘制。菜单行高与按钮命中区只决定中心位置，不缩放或拉伸图形。曲线使用 SVG 的圆弧（`A`）或三次贝塞尔（`C`）在 16×16 网格内表达；不引入新的运行时依赖。

图标映射必须按实际动作核对：项目树与 Changes 菜单的文件历史、Blame、复制路径和资源管理器定位分别共用同一种图形；文件历史不能接成回滚箭头，Blame 不能接成比较箭头，资源管理器定位不能接成新建分支图标，设置不能接成定位准星。图形复用验收须同时检查真实菜单的动作映射和绘制结果。

配套 HTML 视觉稿的图标由 `mockup.js` 本地生成，不加载远端图标库，也不等待页面加载后的占位替换。未知图标名称必须显式报错；标题栏重建、比较标签及查找条动态插入共用同一入口。通用图形保持 16px 网格，锁形沿用文件信息区的 12px 网格，不可预览文件沿用原生的 18px 网格；标签关闭叉在 16px 网格内使用 9×9 对角线、1px 线宽，不把网格再次缩成 8px。窗口控制按钮使用独立自绘图形，不使用文字字形。

离线完整性验证不代表图形已与 PyCharm 实测一致。分支重命名、工作区克隆入口和冲突列表状态提示现在均使用已登记的 16px 原生几何，并与视觉稿共用动作语义；无对应产品动作的参考菜单空列仍保持为空。三种图形的同内容屏幕校准仍属于整体视觉验收的一部分。

### 7.3 品牌标记

Augit 使用自己的 `A` 标记。品牌标记不参与工具栏图标对齐，不把 PyCharm 或 JetBrains 标志放入界面。品牌标记的圆角、尺寸和背景必须在标题栏、启动页和通知中复用同一令牌。

标题栏工作区品牌字形使用固定 13px 的默认界面字体；左侧小标记使用 9px，在 17px 外框内居中，背景内框为 13px。品牌仅跟随 DPI，不随用户界面字号放大或被裁切。

## 8. 布局系统

### 8.1 主窗口

主窗口固定由标题栏、主区域和状态栏组成。主区域从左到右为全局工具栏、左侧工具窗口和编辑工作区；底部 Git 历史或终端在编辑工作区下方按需展开。

```
标题栏 44px
全局工具轨 37px | 左侧工具窗口 | 编辑标签与正文
                     └── 底部 Git 历史或终端（互斥，横跨左侧工具窗口与编辑区）
状态栏 28px（默认字号）
```

标题栏、工具栏、标签栏和状态栏即使处于空态、加载态或错误态也保留。窗口变窄时先压缩左侧工具窗口和 Git 详情区，不缩小字体，不改成纵向移动布局。

主区域沿视觉稿使用 6px 左侧页边距、42px 全局工具栏、4px 间隙和 7px 右侧页边距；项目区与编辑区位于上方区域，底部 Git/终端工具窗口从项目区左缘开始，横跨项目区与编辑区并共用主内容区右边界。

### 8.2 工具窗口

- 工具窗口标题行高度 38–42px，标题使用 `ui-heading`。
- 标题左侧内边距 12px；标题与下拉箭头间 4px；标题与右侧按钮组之间由弹性空间隔开。
- 右侧按钮均使用 32px 命中区，按钮组内中心点按 32px 对齐。
- 工具窗口内容不使用厚边框；与相邻区域之间只使用 1px 分隔线和 4px 间隙。
- 左侧项目、提交、搜索固定在顶部；底部终端、Git 历史固定在底部。切换只替换对应内容，不重建编辑区。

### 8.3 文件树和列表

- 项目树行高使用 `max(24px, h + 8px)`；Changes、历史和普通列表按各自组件令牌取值，不能继续使用旧的 28px 统一基准。
- 展开箭头、文件图标、状态标记和文本依次排列，中心线一致。
- 树一级步长按`max(round(7s), floor(round(16s)/2)) + round(11s)`计算，`s`为界面字号档位。`ClassicPainter.getLeftIndent(controlWidth/2)`把左缩进限制到展开图标画布的整数半宽；New UI画布16px，故默认是8+11=19px，UI14为24px、UI40为62px。行内距沿`--tree-depth`逐级累加，默认项目树首级为4+19=23px；第7级为137px。旧18px解释遗漏了半宽下限。Changes使用`Tree.border`左右12px加首级renderer偏移19px；展开画布为16px，独立置于renderer之前，文件为第二级38px。2026-10-14原始PNG双方子框向右33物理px、行步长42px、蓝框28×28px，先前34px差已消除；两张整窗尺寸仍不同，绝对横坐标差−2px不计整页配准通过，见[精确量测](../artifacts/pycharm-reference-2026-10-13/commit-components-metrics.json)。
- 焦点位于列表时，选中行使用 `accent-soft`；焦点移出列表后仍保留选择，但改用对应的 `selection-inactive`（Git 历史使用 `history-selection-inactive`）。这不是取消选择，也不触发文件打开、Diff 请求或 Git 勾选变化。悬停使用 `hover`；背景覆盖内容行，不绘制卡片边框。
- 项目树、Changes 和引用树不额外绘制悬停底；选中项保留聚焦或失焦背景。根文件夹角标透出所在行实际底色，不引入另一层行背景。
- 单击行只改变选择；双击或 `Enter` 执行打开。复选框的勾选状态与列表选择状态完全独立。
- 虚拟化列表仍必须保持相同的键盘顺序、行高和滚动锚点。
- 项目树、标签、Changes、历史变化文件与搜索结果共用文件类型图标。类型色写在图标自身，Git 状态只改变文件名颜色。映射必须同时核对实际 FileType 调用和 `PlatformIconMappings`：JS/TS/CSS/Python、EditorConfig、忽略文件和各文档类型使用自己的图形；`.gitignore` 实际走 `Vcs.Ignore_file` 并映射为 `expui/fileTypes/ignored.svg` 的斜线圆，不采用同名 `gitignore.svg` 的 Git 菱形。各类型浅深色与几何见本轮场景记录。项目树和 Changes 的文件图标与文字间距为2px，复选与选择独立。
- 项目树与历史变化文件的目录使用同一个圆角文件夹：16px 网格内约 14×12px 主体、1px 细边和浅填充，左右侧边竖直；展开与折叠共用图形，不加前盖横线。工作区根目录右下叠加 7px 蓝色圆角标记，周围保留实际行背景。普通文件夹与全局项目工具按钮是不同组件，不共用同一个工具栏轮廓。
- 浅色目录边线 `#6C707E`、填充 `#EBECF0`，根角标边线 `#3574F0`、填充 `#E7EFFD`，来自 PyCharm 168 DPI 屏幕样本。深色暂用 `#868A91/#43454A` 和 `#548AF7/#253F61`：深浅结构同构，但**未取得同场景的 PyCharm 深色参考截图** ⇒ 这 4 个深色值归**无法取证**（`ui-classification.md` §5），按内部一致性维护，不写成"已与 PyCharm 深色对齐"。由 `NativeTheme.TreeIcons.cs` 集中绘制。
- 项目树展开标复用统一曲线绘制；8px 箭头网格到 16px 目录/文件网格间距 4px，图标到名称 8px。根目录名按当前半粗字体实际字宽分配，最多 180px；辅助路径左对齐并紧跟名称 8px，不按字符数估宽，也不靠右对齐制造中间空洞。
- Markdown 使用无外框的实心 M 与独立下箭头，在 16px 图标区内居中，高度约 8px；不得用 12px 高的细折线或 `M↧` 字符拼接替代。C# 使用圆弧 C 与倾斜的井号，中心保持开放，不使用界面字体。
- 当前文件类型色由共享 CSS 的 `.file-type-*` 与独立多层令牌解析。Markdown、JSON、YAML、JS、TS、CSS、Python 和 EditorConfig 的双主题栅格对照已覆盖；不把该范围外的图形或真实屏幕抗锯齿写成已全部验收。
- 通用文件图标使用折角纸页和水平文字线，图片使用矩形外框与框内山形、圆点；不得为普通文件绘制图片内容，也不得省略图片外框。
- `.slnx`、`.props` 和 `.gitattributes` 复用已有三条水平线图形，`.gitignore` 复用已有圆圈斜杠图形；这些类型也由统一入口绘制，不能在项目树与 Changes 中使用不同图形。
- Changes 的文件名与目录按当前字体实际字宽分配，间距为 8px；优先显示完整文件名，两者各自的最小辨认宽度取实际字宽与 56px 的较小值。空间不足以同时容纳这两个最小宽度及间距时优先保留文件名，不再按固定百分比分列。
- 项目树、Changes 文件/分组行和引用树不额外绘制悬停底色（2026-10-10 用户裁决，替代第 100 轮的有意差异）。Git 历史和文件历史属于表格，保留 `row-hover`／`row-hover-inactive`。选中项使用聚焦或失焦选中色，悬停不覆盖选中。

提交工具窗使用同一实际字高 `h`：标题为 `max(39px, h + 10px)`，工具行为 `max(36px, h + 8px)`，Changes 行为 `max(round(24s), h)`（`s` 是界面字号的离散缩放档，12px 基线为 24px），Amend 行为 `max(22px, h + 4px)`，底部文字动作为 `max(30px, h + 8px)`。图标按钮维持 27px 宽，设置图标命中区维持 27×30px；默认提交/提交并推送外宽下限为78/102px，实际字宽另加28px内距和6px透明边框。两动作加设置图标与两个 7px 间距超过可用宽度时换成两行，行间距 7px，底部留 5px。提示行与输入正文至少容纳一行实际文字；空间不足先缩短列表，不挤压按钮。Amend行为“Amend”与蓝色“last commit”链接，放不下时链接换到下一行，文件数量仅在列表分组显示，不在输入栏重复显示；大字号时标签省略并保留完整悬停说明。这些字号适配值与视觉稿、原生共用，不作为新的 PyCharm 实测结论。

### 8.3.1 Git 历史密度

- Git 引用标签按分组着色（权威 `VcsLogStandardColors.Refs`，日志侧映射见 `GitRefManager.kt:264-269`）：本地分支绿 `#3CB45C`、远程紫 `#9F79B5`、标签灰 `#7A7A7A`（深 `#999999`）、HEAD 黄 `#FFD100`（深 `#E1C731`）；当前分支的引用取两色——描边用本地分支绿、内点用 HEAD 黄，与参考图日志里 `main` 标签一致。
- 当前分支底色的**范围**按权威 `platform/vcs-log/impl/.../highlighters/CurrentBranchHighlighter.java` 实现：染色条件是 `getContainedInCurrentBranchCondition(root)`，即**沿父链从当前分支 HEAD 可达的全部提交**（分叉合入的提交同样计入），而不是只标 HEAD 那一行；`getStyle` 里 `if (isSelected) return DEFAULT` ⇒ **选中的行不染色**（实现上靠 `.commit-row.current-branch` 规则排在 `.selected` 之前）。该高亮器还有一条抑制规则：日志**按 HEAD 或按当前分支筛选**时不染色——Augit 目前没有按分支筛选日志的入口，这条不适用。参考图里整个可见日志区（连续 y973..1053）都是这一色，与本实现一致。
- Git 日志里"当前分支所指提交"那一行用浅蓝底标出：权威 ColorKey `VersionControl.Log.Commit.currentBranchBackground` 的默认值是 `#EDF3FF`（深色 `#283044`），参考图日志首行实测同值（该行右端带 `main` 标签，正与引用面板里的 `HEAD（当前分支）` 对应）。行被选中时仍显示选中底色，当前分支底只作区分。
- Git 标题文字从面板左侧 12px 开始，控件绘制不得再次叠加左内距；标题按半粗字体实际字宽占位，到“日志”标签外框留 11px（原视觉稿的 7px 标题余量加 4px 组间距）。日志与文件历史标签之间为 4px；两类标签的文字左右各留 10px，另计 1px 边框，外框圆角半径 5px。日志按实际文字占宽，文件历史标签最多 280px，窄栏省略路径并保留右侧动作空间。这是已确认的 12px 统一及既有视觉稿标签几何落实，不新增 PyCharm 实测结论。
- 活动日志或文件历史标签使用 `border-strong` 边界与 `panel-muted` 背景；不混入蓝色焦点或悬停底色。焦点框、悬停和活动标签分别表达，失焦不改变活动标签身份。
- 历史界面按实际界面字高 `h` 适配：标题栏 `max(38px, h + 14px)`，文字标签 `max(24px, h + 4px)`，筛选栏 `max(36px, h + 14px)`，筛选框 `max(25px, h + 2px)`；文字居中，图标与命中区保持原尺寸。Git 标题、日志标签与取消按钮按实际字宽加内距分配，不能互相覆盖。
- 引用树和变化文件行高 `max(24px, h + 4px)`，提交行高 `max(26px, h + 6px)`；图形节点半径、轨距、普通与选中线宽、图形文字间距按实际行高等比缩放，规则见 §8.3.2。列表字号更新保持同一控件、选择和有效顶部行，滚动范围与分页临界值使用当前实际行高。
- 文件历史工具栏高 `max(39px, h + 12px)`，分支文字按实际字宽分配，受限时省略并保留后续图标空间。详情标题为 `max(40px, 2h)` 的两行区域，作者日期与引用各至少容纳一行实际文字；可用空间仍受用户工具窗口高度限制。这些数值属于 Augit 字号适配，不标为 PyCharm 大字号实测。
- 文件历史提交行采用权威的“版本、日期、作者、标题”四列（`FileHistoryPanelImpl.createColumnList` 的 Version → Date → Author → Message，Git 的 `isDateOmittable()` 为 false 故日期列在），不显示 Git 日志提交图或引用标签；左右留白各 10px，表头高 `max(25px, h + 6px)`（权威 `TableHeader.height` 默认 25），版本／日期／作者三列不窄于 62px/108px/90px，标题占余量并至少保留 120px，四列均在自身范围内省略。最小内容宽度由四列和留白共同决定，窄栏使用局部横向滚动而不是挤压列宽。共用原提交列表的行高、焦点选中色与分页规则；列集合与顺序属权威对齐，列宽仍为 Augit 自身的 px 比例（权威只给 Swing 的 preferred 字符数：版本 10、作者 14、提交信息 80 个 m）。
- 提交详情使用局部纵向滚动，标题、元信息与正文一起移动；正文沿用等宽字体与字号，内距为左右 10px、上下 8px，元信息后留 8px。不添加大卡片边框或窗口级滚动。上方变化文件约占可用高度的 56%，其后为可选动作行与详情正文；动作行只在能完整容纳当前字体的文字及间距时显示，紧凑时已有文件动作仍可从变化文件菜单进入。
- 历史标题栏的局部取消按钮使用 88×24px 命中区，默认从右侧 168px 处开始，与右侧更多动作和折叠图标保留独立空间；慢文件查询超过 150 毫秒后才显示，出现与消失均不移动列表、筛选或编辑区。取消入口隐藏时，焦点回到可操作的历史列表。
- 历史筛选栏左右留白 8px，控件之间 4px；搜索框默认最小 110px、最大 230px，搜索图标占 16px，文字区从框左侧 28px 开始。
- 筛选按钮按实际标签字宽预留文字、下拉箭头和内边距，至少 48px，并向上对齐 4px 刻度。默认文字下通常为 52px。
- 右侧详情与搜索按钮始终保留，命中区各 28px、中心相距 32px。放不下的筛选项按原顺序收入 28px 命中区的右箭头菜单；不增加筛选条件或 Git 操作。
- 历史行的标题、引用、作者、日期独立绘制，列间距 8px。字体或提交快照变化时重新度量一次，引用列最多 128px、作者列最多 96px，其余空间优先留给标题；可用时标题至少 120px。
- 常规日期格式为本地 `yyyy/M/d HH:mm`；窄栏收起引用并将日期收紧到当天 `HH:mm` 或其他日期 `MM-dd`。标题、作者和日期各自省略，不拼接成一个会被长引用截断的字符串。
- 历史刷新图标复用统一弧线绘制；变化文件复用类型图标，图标区 16px、到文字 8px，不用字母状态码替代文件类型图形。
- 分支树引用使用右上带圆孔的斜向实心标签；提交行引用使用同方向细轮廓。浅色标签为参考采样色 `#FFAF0F`，深色暂用 `#F2B846`；圆孔保留实际行背景。当前只校准单个引用的几何；多引用叠放与不同引用类型的颜色**未取得对应参考场景 ⇒ 归无法取证**（`ui-classification.md` §5），按已校准的单引用几何维护，不写成已按多引用场景验收。
- 筛选下拉使用无竖杆的 V 形折线，与历史树的展开标记复用 `NativeTheme.DrawChevronIcon`；菜单搜索复用统一的空心镜圈和手柄，不能用导航向下箭头或单独斜线替代。
- 竖向工具栏短于完整按钮组时，按原顺序保留完整按钮，并为最后的右箭头预留 28px 命中区。右箭头打开横向图标弹层：高度 36px，按钮 28×28px、中心步长 32px，外侧水平留白 6px，垂直留白 4px。返回入口留在原工具栏，弹层包含其余整组已有动作，复用原绘制方法和启用条件。
- 历史图标、筛选按钮与溢出弹层的焦点框沿用文档工具栏的 1px `accent` 内框，与悬停和筛选选中底色分别表达。大字号下侧栏首个动作随筛选行高度下移，图标仍为 28px 命中区；极短区域允许收紧箭头前的空白，距前一个按钮至少 2px、距底边至少 4px，不能显示裁切的箭头。

### 8.3.2 提交图

- 使用现有 Git 查询返回的拓扑顺序和父提交哈希布局；轨道只代表真实连接。快照变化时重建布局，逐行重绘不能重新扫描提交关系。
- 图形几何以**基准行高 22px** 为准，按 `实际行高 ÷ 22` 等比缩放节点半径、轨距、普通线宽、选中线宽和图形文字间距。基准常量：节点半径 4px、轨距 16px、普通线宽 1.5px、选中线宽 2.5px、图形文字间距 2px。缩放只改变图形区内部的绘制尺寸，不改变字符排版和列分配。几何常量、缩放公式与出处见 [IntelliJ Platform New UI 数值参考](intellij-platform-ui-reference.md) §6。
- 缩放后的值在设备空间向下对齐到最近的奇数。因此 100% 缩放下实际渲染比基准常量各少 1px（例如节点直径 8 渲染为 7、轨距 16 渲染为 15），非 100% 缩放下用户空间的值可能不是整数。比较截图像素时不得用对齐后的值反推基准常量。
- 图形区宽度与各轨横向位置按权威公式推导（**第 310 轮已实现**）：宽度 = `floor(列数 × 16 × 实际行高 ÷ 22) + floor(2 × 实际行高 ÷ 22)`（`GraphCommitCellUtil.getGraphWidth`，用的是**未对齐**的原始轨宽与图形文字间距，各自向下取整）；第 `position` 轨中心 x = `elementWidth × position + elementCenter`，其中 `elementCenter = alignToInt(16 × h ÷ 22 ÷ 2, FLOOR, null)`、`elementWidth` 是 `FLOOR + ODD` 对齐后的轨宽。**两处登记的实现收严**：① 权威把 `min(6, recommendedWidth)`（该图的推荐泳道数下界）也算进列数，Augit 不计算 `recommendedWidth`，直接用整份列表的最大轨数 ⇒ 单轨历史下图形区比权威更窄，但与"标题统一从整个图形区右侧开始、不在不同行按节点位置跳动"（权威是逐行算宽度）同属已登记的收严；② 原先"单轨图形区宽 29px、节点中心位于左侧 15px"是待重算的固定值，已按公式替换并由 `tools/verify-ux-commit-graph.cjs` 逐值复算。
- 普通节点为**实心圆**，直径 8px（对齐后 7）；HEAD 节点是**三个同心实心圆**（不是"描边环 + 圆点"）：外圆直径 12px（对齐后 11）填节点色、中圆比外圆小 2×`RADIUS_DELTA`（基准 `RADIUS_DELTA` = 2，缩放后单独取整）填**所在行的实际背景**、内圆再小 2×`RADIUS_DELTA` 填节点色（权威 `HeadNodePainter.kt:31-60`）；连接线宽 1.5px。这些是基准行高 22px 下的常量值，随行高等比缩放。HEAD 环内使用所在行的实际背景，选中背景不能被白色圆片覆盖（`.commit-row.current-branch` 因此同时写 `--augit-row-background`）。图元素被选中时（Augit 没有该动作）画单个实心圆、不画外环；**表格行选中不改变图形**（`06-commit-graph.md`：选中集合只由图动作写入）。
- 主轨道浅色为 `#47A1B3`、深色为 `#5BB5CA`。次轨道循环使用浅色 `#9767B1 / #609349 / #C2665A` 和深色 `#B88AD1 / #87B772 / #DE8F81`，由 `NativeTheme.GitGraphColor` 集中管理；浅色主轨道来自 PyCharm 历史截图采样，次轨道按同一色阶展开。**本组取值是 Augit 自有的图表配色，属登记差异**（`ui-classification.md` §2.12a）：权威由「head／片段 → ref 名 hashCode → colorId → `HSB(saturation, brightness)`」派生（`GraphColorManagerImpl.kt`、`DefaultColorGenerator.kt:43-58`），而 Augit 的日志没有 head／片段模型，无法照抄该派生链。
- 未加载或被筛选掉的父提交使用本行短虚线延续，不连到无关的下一条结果；根节点下方不绘制连线。这一缺失父节点表达属于同构推导，不作为 PyCharm 像素一致的验收结论。
- 同时存在可见和缺失父提交时，短虚线使用独立的分叉端点，不能与可见父关系的实线完全重叠。多个缺失父关系各自保留短分叉；第一父缺失时，其他父关系仍使用次轨颜色。
- 多轨窄栏保留完整图形区、120px 标题、至多 48px 的最小作者预算及短日期，各列间距和右侧留白仍为 8px。总宽度超出提交列表时，仅列表启用横向滚动；标题、图形和元数据一起移动，筛选栏、引用树和详情不动。恢复足够宽度或较少轨道后收起不再需要的滚动条。这是 Augit 对已有可读性规则的适配，归**有意产品差异**（权威没有"多轨窄栏"这一 Augit 适配规则的直接实测样本，`ui-classification.md` §1.27），不登记为 PyCharm 实测。

### 8.4 编辑标签和正文

文件历史右侧采用本节的完整历史比较工具栏、文件信息行和单/双栏正文，详情区自身不再叠加第二组工具栏。保留原左右分栏和详情显隐，删除旧视觉稿中的编辑铅笔及无对应产品动作的展开占位。默认双栏，浅深色与比较页共用高亮、行号中栏、计数和模式图形；详情宽度仍为底部内容宽度的 42%，限制在 360–520px。此项为现有只读 Diff 能力的组合，不新增文件编辑能力。

工作区 Diff 左侧工具栏按“上一处、下一处、搜索、上一文件、文件计数、下一文件”排列；文件计数位于横向箭头之间，避免把文件导航和差异导航混为一组。右侧按“差异摘要、忽略空白、双栏、单栏、设置”排列。原生按钮与 HTML 视觉稿保持同序，Tab 顺序遵循可见顺序。

历史/引用比较已有工具栏采用相同的右侧顺序：“差异计数、忽略空白、双栏、单栏、设置”，左侧保留上一处、下一处和搜索。差异计数显示“n 处差异”，右对齐在忽略空白之前，使用 `ui-body` 与 `muted`；加载期间清除旧计数，超过 150 毫秒仍使用局部加载文字，摘要、失败和取消时不显示文本差异计数。Tab 按按钮可见顺序进入可见正文，双栏按左、右顺序，随后返回标签栏；计数、文件栏和行号中栏不进入 Tab 顺序。键盘焦点使用与普通文档工具栏一致的 1px `accent` 内边框，不以选项选中底色代替焦点。提交与文件选择跟随按已确认的 UX 第 7.8 节执行；历史多文件导航的参考证据仍单独验收，不能由工具栏外观证明完成。

- 标签栏高度 **40px**，相邻标签**重叠 1px**（`tabHGap = -tabBorder.thickness`），不是可见间隙；默认字号下标签高 28px、上下各留 6px（权威 `ManyIslandsLight.theme.json` 的 `ui.EditorTabs.tabInsets = -6,8,-6,8`；2026 参考图实测标签条白底加下沿边线共 70 物理px，按卡片 28 逻辑px 定标得 39.2，吻合）。选中标签**用圆角卡片表达**：卡片底色取 `tab-selected-bg-active`（`blue-150` = `#E3EBFE`，聚焦档）或 `tab-selected-bg-inactive`（`gray-150` = `#E9EAEE`，未聚焦档），并带 1px 描边；**不使用下划线**。两份 PyCharm 参考截图（`pycharm-baseline-20260919/pycharm-main.png` 与 `visual-refinement-2026-09-08/pycharm-history-normal.png`）都显示选中标签是圆角卡片 + 描边 + 关闭叉，且 Islands 渲染的 `paintUnderline()` 是空实现；原先"经典下划线、不用底色方块"的结论来自一次**取样窗口漏掉选中标签**的采样，已在 `intellij-platform-ui-reference.md` §5.2.1 推翻。标签栏底色与编辑区一致（`EditorTabs.background` = 白）。**已实现**（第十六轮）：卡片用 `::before` 画在标签内容之后（`isolation: isolate` + `z-index: -1`），`inset: 0 4px` 对应 `IslandsTabPainter.paintTab` 的 `hOffset = 4`；圆角半径 6（`arc = 12` 视为直径）；纵向不额外内缩——标签盒本身就是 28px 内容盒，与 `vOffset` 后的卡片高一致。底色/描边走 §6.1／§6.2 的 `tab-selected-*` 令牌，聚焦档由包含标签与正文的 `.editor-area:focus-within` 切换；未选中项悬停时用相同卡片几何绘制 `tab-hover-bg`（浅 `#00000012`／深 `#FFFFFF17`），选中但失焦的悬停背景也用该档，活动选中卡片保持活动底色。同时删掉了原 `::after` 4px 下划线与一条旧的 `body[data-theme="dark"] .editor-tab.active` 覆盖（它用 `panel-muted` + `border-strong`，特异性更高会把卡片盖掉）。
- 标签宽度按当前界面字体的实际字宽计算，不按字符数估算；图标区 16px，图文间距 8px，始终预留关闭按钮空间，悬停和激活不改变文件名的省略位置。普通标签自然宽度限制为 110–215px，空间不足时保留现有压缩和活动标签可见规则。
- 引用比较标签自然宽度为 210–420px，显示顺序为“比较: 文件名 · 来源 → 目标”；保留临时标签斜体、16px 图标和独立关闭热区。按实际字宽绘制，空间不足时文件名获得约 40% 内容宽度，两侧引用平分余量，较短内容让出空余空间；三部分各自省略。分隔符同样按字体度量，不得覆盖关闭按钮。文件栏显示短哈希，其完整身份由悬停说明提供。此收缩分配是 Augit 适配规则，不作为 PyCharm 同场景已实测结论。
- 文件类型图标、修改标记和关闭按钮必须有固定顺序：图标 → 状态标记 → 文件名 → 关闭按钮。
- 临时标签与正式标签只通过状态标记或文字重量区分，不改变标签栏高度。
- 面包屑、工具栏和正文使用同一左边界；正文行号列固定，正文滚动不带走行号。
- 普通文本和 Diff 始终只读；2026-10-17 用户批准移除内置冲突编辑器后，冲突文件同样只读，Augit 不提供任何内置文本编辑或保存。

历史、引用和文件历史比较的正文沿用视觉稿的左右 13px、顶部 8px 内边距；中间行号区左右各留 7px。行号区至少 84px，按当前等宽字体度量两列最长行号（至少三位）及四个空格后加左右留白，不再让大字号或长行号被固定宽度裁切。右版本标题与右正文同步对齐。修改等宽字体只重新度量和排列当前控件，不重新解析补丁或调用 Git；正文与行号共用相同的顶部留白和行高。以上为 Augit 现有比较结构的适配，不作为新增 PyCharm 实测。

工作区、历史、引用和文件历史 Diff 的增删底色覆盖整行，包括文字右侧空白和实际新增/删除的空行；双栏为对齐补出的空白行保持正文背景。浅色新增/删除分别使用 `#C9EECF/#F7D7D7`，深色使用 `#294330/#4B2D2D`，与视觉稿 `green-soft/red-soft` 一致；字词变化继续叠加局部高亮。主题切换只更新颜色，不重新查询、解析补丁或重置阅读位置；新正文、失败、取消和关闭不继承旧正文的行底色。

#### Diff 模式图形

- Diff 文件信息区双栏高度 31px，来源与路径位于左正文上方，目标位于右正文上方；单栏高度 55px，内含两行 24px 的来源/目标信息。图标 12px、左内边距 8px、图文间距 8px，文字使用 `ui-body`；引用用 `text`，路径用 `muted`。来源按实际字宽分配，最多使用所在区域可用文字空间的一半，余量留给路径；长引用和路径各自省略，完整身份保留在悬停说明。
- 工作区 Diff 的双栏正文沿用左右 13px、行号中栏左右 7px 的正文留白；中栏宽度至少 84px，并按当前等宽字体和两侧最长行号实际度量后扩展或收回。界面字号变化不改变中栏和正文，等宽字号变化只重新度量当前已显示内容，不重新查询 Git。
- 双栏 Diff 的行号中栏内部为**三列**：旧行号 ｜ 变更连接区 ｜ 新行号；连接区列宽 **14px**、位于中栏正中（`left: calc(50% - 7px)`），两侧行号列各占剩余一半。**逐行槽底**只在变更块中该侧确有行号时绘制，取该块类型色的**全强度** `DIFF_*.BACKGROUND`（新增 `#BEE6BE`／删除 `#D6D6D6`／修改 `#C2D8F2`；深色 `#294436`／`#484A4A`／`#385570` —— 与行内高亮共用 `--augit-diff-inline-*` 令牌）；块类型由块内两侧是否都有行决定（两侧都有 ⇒ 修改色）。**变更连接区**是同色、**无边框**的梯形：左边缘 = 旧侧行范围、右边缘 = 新侧行范围，某一侧没有行时那一侧在两范围中点收成尖角；几何用 `clip-path: polygon(...)` 按"块高占比"表达，因此不随界面字号或 DPI 改变。行号槽的行高锁定为正文行高（`--code-line-height`）、顶部与正文的 8px 内距对齐；连接区图层不遮挡两侧行号，也不进入 Tab 顺序。权威 `DiffLineMarkerRenderer.drawMarker()`（槽底）与 `DiffDividerDrawUtil.DividerPolygon`（梯形、`getBorderColor()` 为 `null`），取证见 `nui-behavior/08-diff-merge.md` §2.3。
- Diff 工具栏的「查找」可用（第 308 轮按权威实现，权威是标准编辑器查找 `IdeActions.ACTION_FIND`）：点击后在**文件栏与正文之间**插入与文档正文同一条 `.current-find`（`.diff-layout:has(> .current-find)` 多出一行、正文仍是弹性行），正文按**逻辑行取并集**（双栏下同一逻辑行左右各一份，新侧有内容取新侧、纯删除行取旧侧）⇒ 单栏与双栏命中数一致；命中用 `<span class="find-hit">`／`.find-hit.find-current` 包裹，色值沿用文档查找的 `--augit-blue` 20% 与 `--augit-blue-soft` + 1px `--augit-blue` 内框，且**不重写行内容**（行内词级差异 `<mark>` 保留）。加载态下该按钮与差异箭头一样禁用并写明原因。
- 上下排布来源于历史比较实测 `PY-DIFF-MODES-01`，工作区和独立引用比较复用同一只读结构，属于 Augit 适配；31/55px 是与现有工具栏相协调的逻辑尺寸，不作为 PyCharm 原始像素测量。显示模式切换只调整信息区和正文高度，正文加载期间保留已显示模式的文件信息布局。

- 工作区 Diff、历史与引用比较共用 `NativeTheme.DiffIcons.cs` 的 16px 图形，不再分别绘制。双栏为 11×11px 圆角空心框与中心竖线；单栏为相同空心框，不加文字横线。边框 1px、圆角半径 2px，居中于既有按钮命中区。
- 圆角框结构来自 `PY-DIFF-MODES-01` 的浅色实测；上述逻辑尺寸是截图适配值，**同环境像素校准项归无法取证**（本机没有与参考截图一致的 PyCharm 环境；`ui-classification.md` §5），实现按该适配值维持，不写成 PyCharm 原始像素测量。绘制显式指定笔画，不使用设备上下文中的当前画刷填充，避免深色出现白色矩形；改变用户字体不改变图形。
- 独立“忽略空白”按钮保留现行二态操作，使用段落空白符号轮廓；当前开关状态由既有选中底色表达。它属于 Augit 已有简化方案，不是 PyCharm 参考工具栏的原样入口。Changes 的提交勾选与 Amend 仍使用正式复选框，不替换为该图形。
- HTML 视觉稿以 `diff-unified`、`diff-side-by-side`、`diff-ignore-whitespace` 自绘 SVG 使用相同几何，不复用 Markdown 模式或通用面板图标。
- 单双栏组成 **87×33px** 分段控件（内距由 1 改为 2 后增加 2px；见下），外框为 1px `border-strong`、**圆角半径 3px**，内边距 **2px**（合计边框内距 3 = 权威 `SegmentedButtonBorder.getBorderInsets()` = `DarculaUIUtil.BW + LW` = `Component.focusWidth`(2) + 1）；双栏在左、单栏在右，按钮为 **40×27px**（图标 16 + 权威横向内距 12×2）、**圆角半径 3px**，中间间隔 1px。圆角取权威 `IntUiBridgeSegmentedControl.kt` / `IntUiBridgeSegmentedControlButton.kt` 的 `CornerSize(DarculaUIUtil.BUTTON_ARC.dp / 2)` = **3**（`Button.arc` = 6，arc 是直径）；组边框色来自经典路径 `getSegmentedButtonBorderPaint()` 的容器分支：`Button.buttonOutlineColorStart/End(false)` = `control-border`／深 `Gray5`，即 `border-strong` 令牌；选中按钮的描边来自同一函数的 subButton 分支：`SegmentedButton.selectedStart/EndBorderColor`，两套家族里**同值**（浅 `#B5B7BD`／深 `#6F737A` = `control-border-raised`），且 `paintBorder()` 用内外两条圆角矩形（even-odd）画成**四边各 1px 的整圈描边**——不是"两端"。**已统一**：`.segmented` 有两条同名规则，原先一条用 `--augit-panel-muted`（`#F7F8FA`）、另一条写死 `#F4F5F7`，**后者生效**造成分歧；现统一为 `--augit-panel-muted`（当时实测 `rgb(247, 248, 250)`；该背景规则第 84 轮又按权威 `SegmentedButtonComponent.paint()` 改为 `transparent`，见下条，令牌值第 131 轮改为 `#F7F8F9`）。另：两条同名规则里的内距，第 81 轮已按权威改为 **2px**（见上），因此早先那条规则里的 `padding: 2px` 现在正是生效值——第 75 轮曾记它"被 1px 覆盖、是失效值"，那个结论已被第 81 轮取代。

**按钮内距已定案**：曾经登记的"权威里按钮横向内距是 `PaddingValues(horizontal = 14.dp)`"来自 **Jewel** 的 `IntUiBridgeSegmentedControlButton.kt`，而第 81 轮已确认 Augit 的分段控件对应的是**经典 Swing** 的 `SegmentedButtonToolbar`，那条 Jewel 度量**不适用**。经典路径的按钮尺寸在第 83 轮定位到：`SegmentedButtonComponent.getPreferredSize()` = 内容 + `spacing.segmentedButtonHorizontalGap`(12)×2 宽、`max(内容 + segmentedButtonVerticalGap(3)×2, Button.minimumSize().height − 2)` 高（后者 13px 字号下为 27，与现值一致），常量在 `IntelliJSpacingConfiguration` 里。横向内距已由 9/10 改为 **12**；容器内距按 `SegmentedButtonBorder.getBorderInsets()` 取 2（+1 边框 = 3）。
- Diff 工具栏默认高 39px；界面字号变化后至少为实际行高加 12px，文字摘要垂直居中，图标命中区保持 27px。文件信息双栏默认高 31px、单栏默认高 55px；界面字号变化后分别至少为实际行高加 12px、两行（实际行高加 4px）加 7px。正文起点随文件信息实际高度移动，不能裁切来源、目标或差异计数。
- **分段容器没有专门底色**（第 84 轮定案）：权威 `SegmentedButtonComponent.paint()` 只调 `super.paint()`（未设背景）再画子按钮描边，即透出父容器底色；Jewel 侧的 `SegmentedControlColors` 同样没有 background 字段。故 `.segmented` 的 `background` 由 `panel-muted` 改为 **`transparent`**（实测差异工具条里为 `rgba(0, 0, 0, 0)`），选中按钮靠白底 + `#B5B7BD` 整圈描边区分。文档模式组（`.document-modes`）保留自己按参考图定的 `var(--augit-panel)` 底色，不受影响；选中按钮按权威 `ui.SegmentedButton` 取 `control-bg-raised`（浅色白／深色 `Gray3` = `#393B40`）与 `text` 图形，并用 **`inset 0 0 0 1px` 画整圈 1px 描边** `control-border-raised`（浅色 `gray-110` = `#B5B7BD`／深色 `Gray7` = `#6F737A`，即 `selectedStart/EndBorderColor`——这两个色在两套家族里同值，`getSegmentedButtonBorderPaint()` 里的 `GradientPaint` 因此退化为实色；`paintBorder()` 画的是内外两条圆角矩形，四边都有）；聚焦选中改用 `focusedSelectedButtonColor` = `toolbar-selected-bg`（浅色 `blue-140` = `#D0DFFE`／深色 `Blue3` = `#35538F`）**换底色**，不再使用旧的 1px `accent` 焦点内边框。未选中使用 `icon` 图形，禁用使用 `faint`。忽略空白保持独立 27×27px 按钮及原有蓝色开关状态；它与分段组、分段组与设置之间各留 7px。
- 分段组只统一视觉，两项仍是独立的原生焦点目标。工作区和历史/引用比较均支持 Tab、Shift+Tab、Enter 和 Space；模式切换复用已加载补丁，不重查 Git，不改变提交选择、草稿或其他标签。原生复用 `NativeTheme.DiffToolbar.cs`，不额外创建窗口、线程或常驻资源。

#### 文档模式工具栏

- 文档工具栏左内距 9px、右内距 8px，普通动作使用 28×28px 命中区及 4px 间距，模式按钮仍为 26px。路径从左内距开始绘制，不再叠加文字内缩；过长路径单行省略并提供完整路径悬停说明。Markdown、JSON 和普通文本采用同一行路径规则。Blame 使用相同工具栏高度，数量文字采用 `ui-body` 与 `muted`，右对齐在关闭入口前，间距 4px；关闭命中区保持 28×28px，距右边 8px，复用关闭图形与焦点样式。字体增大时文字按实际行高适配，关闭图形不变大。
- Blame 归属边栏复用视觉稿的三列：日期至少 72px，作者摘要至少 24px，归属行号至少 24px，左右各 6px、两处列间距各 5px，总宽起点为 142px。日期与行号按当前等宽字体的最大位数扩展，作者最小宽度按 `I49` 的实际字宽扩展，长作者单行省略。边栏使用 `panel` 底色、`muted` 文字和右侧 1px `border`，点击行使用 `blue-soft` 背景；纵向位置取正文实际文档行坐标，不单独累计行高。正文自身行号栏保持既有规则。
- 图片工具栏左侧依次为缩小、百分比、放大、适应区域，右侧显示像素尺寸、类型和文件大小。文字宽度按实际界面字体度量并加 8px，百分比至少 48px，预留 `800%` 的宽度避免缩放时跳动；文字高度至少为实际字高，三个图标不随字号扩大。空间不足先压缩右侧文件信息，完整信息可悬停查看，动作仍可到达。
- 图片工具图形统一为 16px 网格、1px 线宽，**几何以 New UI 图标文件为准**：缩小／放大都是**无手柄的圆**（`expui/image/zoomOut.svg`／`zoomIn.svg`，中心 8,8、r6.5），放大在圆心加贯穿的加号（竖条 x=8 跨 y4..12、横条 y=8 跨 x4..12），缩小只有减号；适应区域取 `expui/image/fitContent.svg`，为圆角外框 `1.5,2.5 13×11 rx1.5` 加两组内角线（左上竖 x4.5 跨 y5..9、横 y5.5 跨 x4..8；右下竖 x11.5 跨 y7..11、横 y10.5 跨 x8..12）。原「直径 9px 的圆 + 右下手柄 + 四个直角括号」是旧写法，已按 New UI 替换。焦点沿用文档工具栏规则。图片实际矩形范围内使用 8px 方格，交替 `panel` 与 `panel-muted`，透明像素直接透出棋盘格；棋盘原点固定在图片矩形左上角，随图片移动，裁切后不能改用可见部分作为原点。外围画布保持 `panel` 纯色。1px 图片边界完整内缩在图片矩形中，不使用越过图片边缘的居中描边，也不铺盖住透明区域的整张阴影底板。
- 图片稿与原生审计共用 `ux-mockups/assets/image-sample.png`，其自有 SVG 源文件和生成脚本一并保留，样图不作为运行时依赖或随应用发布。移除与原生样图无关的 HTML 文本卡片，图片按真实尺寸和适应比例显示。该样本用于受控对照，不代表 PyCharm 图片场景已实测。
- 图片刷新超过 150ms 时在画布中心叠加单行“正在读取文件…”，背景为 `panel`、文字为 `text`、四边内距 12px、圆角半径 3px；不添加遮罩或占用布局行，不接受焦点或鼠标输入。完成后移除，大小随真实文字度量；视觉稿通过 `?image-state=loading` 查看。这是 Augit 的异步状态适配，非 PyCharm 图片场景实测。
- 符号链接目标在文档工具栏下单独显示完整路径，使用 `panel-muted` 底色、`text` 文字及底部 1px `border`，四边内距 8px、最小高 32px，长路径和大字号按实际字高换行；JSON 错误条位于其后，正文和查找条顺次下移。目标信息不进入 Tab 顺序。
- Markdown原文/对照/预览动作位于编辑标签区右侧、Tab Options之前，不另占正文工具栏；26px模式步长随UI用户缩放。原文图标为四条等距横线，对照为左侧短横线加右侧窄圆角框，预览为带圆点和山形的圆角图片框。只有当前项绘制2px内缩、约5px圆角的选中底，整组无外框，Tooltip与键盘焦点可用。JSON保留正文工具栏内的双段控件，36px为其工具栏基准。
- Markdown 对照默认把扣除 6px 分隔带后的正文宽度等分，两侧各保留 240px 最小宽度；不足 480px 时按剩余宽度等分夹紧，原生按物理像素舍入。这是 Augit 的窄栏保护规则，不作为 PyCharm 同场景测量值。HTML 与原生使用相同的可用宽度口径，不把分隔带额外计入左栏百分比。
- JSON 顶部错误条位于路径工具栏和可选符号链接目标下方、正文上方，宽度随文档区，最小高 32px，四边文字内距 8px；高度按实际字高和换行行数扩展。使用 `panel` 底色、`danger` 文字、底部 1px `border`，获得焦点后使用 1px `accent` 内框，按下使用 `hover` 底色。行列、错误文字和“点击定位”共同表达状态，不依赖颜色；无需新增错误图标。属于 Augit 既有 JSON 规则的设计适配，当前没有对应 PyCharm 同场景实测。
- 三种模式图形由 `NativeTheme.DocumentIcons.cs` 提供 16px 网格内的 1px 细线几何；线条中心距、圆点和预览框圆角不随界面字体或等宽字体改变。文档“更多”复用通用纵向三点入口，文件历史设置复用六齿齿轮，不能使用双同心圆。
- 模式图标浅色 `#6C707E`、选中底 `#DFDFDF` 来自 168 DPI 的 PyCharm 屏幕样本；深色暂用 `#CED0D6`、`#43454A`，尚待对应深色样本校准。每个按钮宽度先按 DPI 换算，再累加组宽，避免 125% DPI 下按钮总宽与背景不一致。
- JSON 格式化按钮使用独立的结构化文本花括号图形，不再复用图片预览图标；该几何依据产品语义和同类工具栏模式推导，当前没有可作为直接证据的 PyCharm JSON 同场景屏幕样本，因此只能记为推导实现，不能宣称已完成 PyCharm 像素匹配。工具栏与标签栏的位置关系、悬停细节及其余文档/图片工具图形继续列入视觉复核，不以本次局部调整宣称整体完成。

文档、图片、Blame 关闭及查找条图标动作共用第 9.1 节状态：未激活时与 `panel` 共底，悬停或按下为 `hover`，换行、空白符、大小写、全字和正则等开关激活时为 `accent-soft`，不能误用主要提交动作的蓝色实心底与白色图标。普通图标为 `icon`，悬停或按下为 `text`；禁用为 `faint` 且不显示悬停及焦点框。背景圆角半径为 5px。模式组继续保留既有灰色选中底和图标颜色；未选中的可用模式悬停/按下时在相同 2px 内缩区域绘制 `hover`，不覆盖当前模式选中背景。

上述按钮获得焦点时显示内缩的 1px `accent` 方形边框，左上内距 2px、右下内距 3px，与既有模式按钮及 Diff 工具栏焦点入口共用；该状态不扩大按钮或改变正文位置。隐藏、禁用或销毁后清除过期悬停，同一按钮内移动不得重复触发布局或重绘。此项收敛现行组件状态，不新增 PyCharm 实测结论。

#### 主框架入口与终端标题

主框架工具轨采用 37×40px 命中区、30×30px 背景、20px 专用图标和 6px 圆角；项目标题与终端标题动作保持各自组件尺寸。普通、悬停、按下、可见、激活、焦点和禁用规则以工具轨专节及 CSS 令牌为准；激活入口的蓝底白图形不被悬停覆盖，禁用不显示背景或焦点环。

终端标题行高取 `max(38px, h + 12px)`，文字控件高取 `max(24px, h + 4px)`。左标题从 8px 开始，宽度至少 54px 或实际字宽加 8px；会话名称从标题宽度加 12px 开始，宽度至少 124px 或实际字宽加 16px，但为其后的三个动作保留 96px。会话关闭与名称相距 2px，命中区 24×24px；更多与隐藏为 28×24px，分别从右侧 68px、36px 开始，全部垂直居中。当前 Shell 显示为非交互单会话标签，长名称省略且悬停可读全名；不叠加第二个选择 Shell 按钮，切换配置沿现有更多菜单。该组尺寸是既有单会话能力的内容适配，不作为新的 PyCharm 实测值。

加载期间会话名称追加“ · 正在启动…”，仍使用同一套按字宽测量与省略规则；正文保留终端面板背景，不伪造 Shell 输出。就绪后只移除加载后缀，不重建标题控件。视觉稿通过 `terminal.html?terminal-state=loading` 查看该状态。

深色终端正文使用控制台方案背景#1E1F22、普通输出#BCBEC4，加载空白、静态样本与实时xterm一致，不继承工具面板#191A1C。来源、原始屏幕与逐行残差见[深色终端配对](pixel-terminal-dark-base-2026-10-16.md)，颜色一致不等于字形或整窗像素通过。

### 8.5 底部状态栏

2026-10-09 当前 PyCharm Islands Light 屏幕附件中，状态栏空白位置 `(700,970)`、`(900,970)`、`(1050,970)` 均为 `#E9EAEE`，与 `ManyIslandsLight.StatusBar.background = main-window-bg` 一致。历史 `#E2E3E8` 不再作为当前主题的取值依据。附件与尺寸见 [本轮整改记录](reference-restoration-2026-10-09.md)。

状态栏左侧显示工作区或活动文件路径，右侧格式组保持视觉稿中编码、换行、只读的顺序并靠右排列。各组之间使用 12px 间隔，左右各留 6px；不把状态文字作为可点击按钮，除非产品规格明确提供对应设置入口。

编码和换行只用于已成功读取的普通文本，取磁盘原文事实，显示模式切换不改变格式。图片、信息页、加载中和比较视图省略这两项；无文档时右侧为空。换行标签支持 LF、CRLF、CR、混合换行和无换行，不显示猜测值。

所有字段使用当前界面字体的实际字宽；取消动作在格式组左侧单独预留空间。路径使用剩余区域省略并保留完整悬停说明。存在操作提示时，为提示预留至多剩余区域一半的文字空间，路径短时提示可以继续使用空余宽度；路径与提示之间仍留 12px。状态更新只重绘所属控件，不重排主框架。这些是 Augit 适配值；**同环境参考图的字形复核项归无法取证**（本机没有与参考图一致的 PyCharm 环境，`ui-classification.md` §5），实现按现有字体度量维持，不写成已按参考图复核。

## 9. 组件状态规范

每个交互组件至少定义 `normal、hover、pressed、focused、selected、disabled` 六种状态；异步组件额外定义 `loading、error`。状态变化只更新组件所属区域。

### 9.1 按钮

- 主要动作：`accent` 实心底、白色文字；固定高度 30–31px。
- 普通动作：透明或 `panel` 背景、1px 边界或无边界；文字使用 `text`。
- 图标动作：透明背景，悬停使用 `hover`，激活使用 `accent-soft`。
- 危险确认：`danger` 实心底，按钮文字包含具体动作名称，例如“确认 Reset Hard”。
- 禁用：保留位置和尺寸，使用 `faint`，悬停说明解释禁用原因。

Reset／Rollback 底栏按钮在默认字号下为 30px 高、**圆角半径 3px**，相邻动作间隔 8px（权威 `IntUiBridgeButton.kt:60-68`：`cornerSize = buttonCornerSize()` = `DarculaUIUtil.BUTTON_ARC.dp / 2` = 3、`padding = PaddingValues(horizontal = 14.dp)`、`minSize = Button.minimumSize()` = 72×28——30px 高满足这个下限；各对话框另按自己的排布覆盖横向内距，属既有设计）。危险确认使用 `danger`，普通取消使用 1px `border-strong` 与 `panel`，悬停和按下为 `hover`；主要动作和危险动作保留实心底。焦点使用内缩 2px 的 1px `accent` 框，蓝色主要动作另加 `panel` 中性外环。禁用动作使用 `panel-muted` 与 `faint`，标题关闭禁用仍为 `panel`；关闭叉使用固定图形，不使用字体字符。状态变化不改位置、焦点顺序或 Git 数据。

### 9.2 输入框和选择框

当前文件查找条横向占满正文顶部一行，正文从其下方开始。界面最大实际行高为 `h` 时，查找条高度取 `max(42px, h + 16px)`，输入和结果区高度取 `max(30px, h + 4px)`；六个图标按钮维持 28×28px 并居中。结果区显示当前项/总数，长结果或错误按实际字宽分配，受限时省略并提供完整悬停说明。输入框优先使用剩余宽度，空间不足时收缩；开关图形使用统一 16px 矢量，不能使用随用户字体变化的 `Aa`、`ab` 或 `.*` 字形绘制。

查找条使用平直边界和底部 1px `border`，不使用悬浮阴影；输入框圆角半径为 5px。输入外框使用 1px `border`，聚焦时原位切换为 `accent`；文字距外框左右各 8px，按实际字高垂直居中。输入框留白仍属于输入命中区域，点击聚焦时保留查询与选择。查找条左右留白为 7px，控件间距为 3px。

- 高度 30–31px，左右内边距 8px，边界 1px `border`。
- 聚焦时边界变为 `accent`，不改变布局尺寸。
- 占位文字使用 `muted`，用户输入使用 `text`。
- 深色主题禁止使用系统白底选择框；选择框、下拉项和弹层都使用主题令牌自绘或主题化控件。所有原生选择框的收起表面使用主题化客户区绘制：浅深色面板、1px 边界、5px 圆角、固定 V 形箭头及真实焦点内框均不能露出系统白边；展开列表保留 Windows 的选择、键盘和辅助功能语义。父表单已经绘制输入框外框时，选择框只绘制内表面，不能叠加第二个边界。选择项、禁用和销毁后必须立即更新或解除绘制登记，不引入轮询或常驻窗口。

提交信息框的局部反馈复用上方提示行：默认 `20px`，按实际界面字高至少加 `4px`；左右内距 `6px`，与正文共享输入框底色。正常显示 `muted` 的“提交信息”，失败改为 `danger` 的原因文字；单行省略且提供完整悬停说明。提示切换不改变外框、列表或底部按钮的位置，不新增弹层或整栏告警卡片。

#### 9.2.1 复选框

- 24×24px透明画布内居中16×16px绘制外框（15px中心路径加1px描边）；圆角路径半径2.5px。未选边框浅`#A8ADBD`／深`#5A5D63`，使用复选框专用令牌，不借用一般分隔色。
- 全选与部分选中使用`#3574F0`蓝底和白色标记；勾号与横线使用2px基准圆端笔画，不使用字体字符。未选聚焦绘制18px外框，已选/半选聚焦保留16px内框并绘制22px外环。
- 禁用勾号浅`#C9CCD6`／深`#6F737A`，深色禁用半选横线`#868A91`；边框、内底、蓝底与标记各自按状态取值，不能一律降低整体透明度。
- Changes 与 Unversioned Files 的复选状态由完整 Git 快照和提交选择计算，折叠或展开不改变显示结果；空组显示未选中。
- 行选中背景只作用于实际选中的一行。分组身份、文件是否勾选与行是否选中是独立状态，不能把所有分组标题永久涂成选中蓝色。
- Changes、Amend、日期/筛选及Clone、Stash等原生HTML复选框共用相同画布、遮罩、颜色和焦点几何；原生input保留键盘与勾选语义。全部尺寸随UI字号档位缩放，UI40画布78px、外框52px。状态回归通过不代表全部业务状态已取得PyCharm配对截图。

### 9.3 弹层、菜单和模态框

- 非模态弹层贴近触发控件出现，自动聚焦第一个输入；`Esc` 只关闭最上层弹层。
- 菜单项高度 30px，左右内边距 12px，图标列宽 20px，文字列与图标间 8px。
- 菜单以最长文字的真实字宽加 52px 计算宽度，限制在 200–420px；200px 是短动作菜单的最小命中范围，420px 是当前产品裁剪后菜单的上限，属于 Augit 布局规则，不是 PyCharm 所有菜单的固定尺寸。文字、图标与分隔线各自保持原列，不用固定 320px 拉出空白。高度累加已缩放的各行及分隔项，再加上下各 8px，避免 125% DPI 下累计舍入侵占底部留白。
- 筛选弹层例外：日期筛选是三项无图标短列表，宽108px、行高24px；日期范围和路径输入分别遵循 IntelliJ `DateFilterComponent` 与 `MultilinePopupBuilder` 的独立结构。详细场景与键盘回归见 [筛选弹层整改](filter-popup-remediation-2026-10-10.md)。
- 设置正文抬头使用1rem半粗面包屑与前后导航，导航选区跨栏无外缩圆角；字体选择按本机列表提供下拉。Diff齿轮位于单双栏切换后，打开局部显示空白/行号菜单。日历使用应用内经典月份网格，所有普通菜单按真实宽高夹边并提供方向/Home/End/Enter导航。当前证据与限制见 [设置与Diff整改](settings-diff-remediation-2026-10-10.md)。
- Settings左侧搜索、右侧面包屑固定，分类列表和正文分别滚动；网格行列均允许收缩到可用区域，不能用隐式auto行高让大字号分类撑高整个主体。权威`SettingsEditor.java:379–390`将搜索和右侧抬头分别放在左右面板NORTH，分类树有独立滚动窗格。正文用自身宽度与字号判断表单排布：不超过48em时标签与字段单列，不超过24em时字体选择独占一行；字号框至少容纳两位数字、内距、边框与16px步进槽，宽度为`max(70px, 3ch + 26px)`。这是现有字号与局部滚动规则的落实，48em/24em不登记为PyCharm实测常量。标签按实际字形包围盒检查，不能侵入相邻字段。验证与截图见[当前接续记录](pixel-font-followup-2026-10-13.md)§5.1。
- 上下文菜单圆角半径 8px，1px `border-strong` 边框沿圆角连续绘制；选中背景半径 4px。图标列只决定 16px 图形的中心，不按行高拉伸；选中勾号由 `NativeTheme.DrawMenuCheckmark` 绘制，字体变化不得改变形状。危险文字使用 `danger`。分隔线、禁用行和四周留白不触发动作。
- 菜单动作图形集中于 `NativeTheme.MenuIcons.cs`，工具栏已有同名图形直接复用原入口。复制图标按可见 PyCharm 菜单的双页及短文字线校准；其他原先按矩形拉伸的菜单图形统一网格与比例；未取得对应 PyCharm 菜单样本的形状归**无法取证**（`ui-classification.md` §5），按图标体系内部一致性维护，不记为逐像素验收通过。
- 图标是否出现也遵循对应场景的参考：实测 Git 提交右键菜单中，复制哈希、Cherry-pick 和 Reset 显示图形；与工作区比较、Revert、新建分支、新建标签保留空图标列。空列仍占相同宽度，不能用其他位置的同名功能图标补位。菜单裁剪后保留 Augit 已有动作及原顺序。
- 模态框保留背后主窗口结构，**不降低背景权重**：参考实现不给对话框做背景变暗（见下方遮罩条款）。取消恢复打开前焦点。
- Settings取得原生窗口呈现确认后，由DWM负责窗口外框及圆角，网页保留边框占位但不重复绘制边框、圆角与阴影；非原生视觉稿保持网页外框。此规则不改变窗口、字段或按钮几何，也不代表背景刷新无闪验收通过，详见[设置外框接续](settings-native-frame-2026-10-16.md)。
- Clone 宽版模态保持 930px 默认宽度，标题至少 45px、字段至少 30px、底栏至少 53px；版本控制、仓库 URL、目录和浅克隆按垂直表单排列。界面字号增大时按实际字高扩展字段、按钮和错误说明，底栏固定，错误内容在正文区域内滚动，不出现平台账号、帮助占位或 Force Push 等产品范围外入口。
- **遮罩是透明的点击承接层，不做背景变暗。** 参考实现没有模态变暗：`platform/openapi/wm/impl/IdeGlassPaneImpl.kt` 只注册 `namedPainters`、`windowShadowPainter` 与 `loadingIndicator`（`IdePaneLoadingLayer`），没有任何变暗绘制；参考截图 `artifacts/pycharm-16-final/pycharm-branches-dialog.png`（2880×1800）在对话框之外的六个区域仍是精确的 `#FFFFFF`（55–90%）与 `#E9EAEE`——若真有 32% 的 `#EEF1F6` 洗白，纯白应变成 `#FAFBFC` 一类值。
- 遮罩元素仍必须存在并覆盖完整宿主客户区、随其大小变化，因为它承担「点击外部关闭」；它不接收键盘焦点。原先按视觉稿级联结果登记的浅色 `#EEF1F6`／32% 与深色 `chrome`／44%、以及 75% 背景饱和度，**已按参考实现取消**——这些值来自 Augit 自身视觉稿，不是 PyCharm 实测。Win11 原生紧凑模态的系统合成外部阴影仍独立成立，与遮罩无关。
- 模态框按钮组右下对齐，危险动作使用 `danger`。Windows的Stash/Reset按权威默认动作组排列为确认、Cancel；Clone/Push沿各自已验证动作组，不将同类推导冒称该页实测。
- Push 默认宽 930px，常规高 494px、无远端高 524px；宽高受宿主减 80px/88px 限制。实际字高为 `h` 时，标题为 `max(45px, h + 18px)`、底栏为 `max(53px, h + 25px)`、提交行为 `max(27px, h + 8px)`、按钮为 `max(28px, h + 8px)`。正文左右 17px、上下 15px，左栏 260px，右侧说明内距 18px；无远端额外预留 `max(30px, 行高 + 3px)` 的标签区。引用摘要及提交行圆角半径 5px，提交勾形在行左侧 20px 后的 16px 图标区，图文间距 6px。主要按钮/取消分别至少84px/78px；主要按钮预留进行态文字宽度加24px，取消始终为Cancel，间距 8px、距右边 17px；此项统一现有视觉稿与原生的字号和限高规则，不构成新增参考实测。

#### Clone 窗口

默认 930×289px，宽度最多为宿主减 90px，高度最多为宿主减 40px。标题高为 `max(45px, h + 16px)`，字段与按钮高为 `max(30px, h + 10px)`，底栏高为字段高加 23px；标签列至少 110px，与字段相隔 10px。表单左右 17px、顶部 15px、行距 13px，浅克隆行额外下移 1px；勾选文字至少 180px，深度至少 58px，单位至少 80px，各相隔 10px。放不下时深度与单位整体换到下一行，行距 8px。默认整窗按共同边界缩放，分数 DPI 的舍入余量留在正文底部，初始表单不因舍入出现滚动条。

取消按Cancel字宽和78px外宽下限分配；克隆预留“Cloning…”的实际字宽加24px，按钮间距 8px、右距 14px；状态切换不改变按钮位置。输入框使用 1px 主题边框和 5px 圆角；禁用的深度及单位使用主题弱化文字，不使用系统浮雕。错误在浅克隆行下方以实际字体换行，只有正文滚动；关闭保持固定 16px 图形，不显示帮助占位。这些规则落实已有模态和字号适配，不登记为新增 PyCharm 实测。

#### 远端管理窗口

2026-10-17安装版组件复核：主窗外层不占布局边框（网页预览用内侧轮廓），内容根内距8/12/8/12；工具条24px动作区外另有1px底分隔，按钮26×24、相邻gap0；表头为行高加1px（默认25），空态从完整表头后开始。底栏上下8px、左右12px。此规则覆盖下文对应旧工具条占位，Define保留现有三行和Push URL。证据与剩余差异见[pixel-remote-component-insets-2026-10-17.md](pixel-remote-component-insets-2026-10-17.md)。

远端管理为单列表格窗口。2026-10-17用户明确批准“按实机调整，保留 Push URL”：默认逻辑宽度为624/1.75px、高220px，对应175%下624×385物理像素参考；宽度最多为宿主减90px，高度最多为宿主减88px。Name/URL两列表格按权威`GitConfigureRemotesDialog.updateTableWidth()`公式扩宽：名称列=`max(30, 名称字宽)+30+10`，URL列至少250px，名称与URL均按当前界面粗体（本产品600）字宽预留，URL正文显示仍为400；整窗按名称列、URL列、10px间隔及24px正文内距扩展。顶端Add/Remove/Edit工具条与底栏唯一OK固定，字号放大或窗口限高时只滚动表格正文。实际字高为h时，行高取`max(24px, h+6px)`，标题栏`max(40px, h+18px)`，工具条`max(24px, h+4px)`，底栏`max(50px, h+25px)`，字段`max(30px, h+12px)`，按钮`max(34px, h+12px)`。标题使用40px品牌槽、16px Augit标记、普通字重、右侧32px关闭区，无标题底部分隔线；正文上距8px、左右12px，主要按钮600字重。单击只改选中态，双击或Edit打开独立Define Remote次模态；字段标签列按实际字宽扩展。输入框使用1px主题边框和5px圆角。原始参考见`artifacts/pixel-followup-2026-10-16/remote-normal-pair/`，此规范调整不等同整页像素验收通过。

Define Remote 默认 405×207px（2026-10-11 前台取证，系统 175%）；产品额外的 Push URL 在参考的两行之外再加一行，高度按 `max(24px, 行高 + 6px)` 追加，因此不声称与参考 207px 精确相同。Remove Remote 确认使用同一尺寸骨架并显示名称、URL 与影响。

#### 创建 Stash 窗口

默认保持 620×323px，宽度至少容纳实际标签与底部动作，最多为宿主减 90px，高度最多为宿主减 40px。标题高为 `max(45px, h + 16px)`，字段高为 `max(30px, h + 10px)`，分支高为 `max(24px, h)`，消息高为 `max(78px, 3h + 12px)`，按钮高为 `max(30px, h + 10px)`，底栏高为 `max(53px, 按钮高 + 23px)`。默认整窗高度以共同边界换算，分数 DPI 的舍入余量留在正文底部。表单左右 17px、顶部 15px；标签列至少 110px，与字段间隔 10px。正文独立滚动，焦点进入字段时使其完整可见；错误换行位于勾选项之后。取消保持Cancel且外宽至少78px，创建预留进行态文字宽度，状态变化不移动按钮；保留索引使用统一24px画布／16px外框复选框绘制并保留原生勾选语义，关闭使用固定图形，不显示帮助占位。本项为已有字号、模态及反馈规则的落实，不作为新增 PyCharm 实测。

#### Reset 与回滚确认窗口

Reset 默认 620×288px，回滚默认 930×430px；实际宽高按内容扩展，最多为宿主减 90px／40px。标题高为 `max(45px, h + 16px)`，字段和按钮高为 `max(30px, h + 10px)`，底栏为按钮高加 23px；标题文字从左侧 26px 开始，关闭图形保持固定尺寸。正文左右 17px、顶部 15px，底部动作右距 17px、间距 8px，按实际字宽加 26px 分配。Reset取消保持Cancel，确认预留进行态宽度；表单标签列至少 110px，字段间隔 13px，标签与字段相隔 10px，模式文本预算包含系统箭头、边框和滚动条实际占宽。

Reset 风险说明距表单 16px，回滚风险说明距比较 14px；警告内距左右 12px、上下 6px，标题与正文相隔 4px，按实际字体换行，浅深警告底分别为 `#F7D7D7`／`#4B2D2D`。Reset 风险区至少 62px，回滚至少 81px；回滚比较至少 214px，并保证能容纳当前工具栏、两行来源信息和四行默认等宽正文。错误也在正文内换行，限高时正文滚动且标题、底栏固定；无功能帮助占位不显示。这些规则落实现有字号与模态契约，不登记为新增 PyCharm 实测。

#### 紧凑单行输入窗口

- 视觉稿为 `ux-mockups/go-to-line.html`。复用现有模态标题栏、遮罩、普通按钮与主要按钮；不显示无对应能力的帮助入口。默认逻辑宽度 400px，标题栏至少 45px，圆角半径 9px，边框与分隔线 1px。此处尺寸是 Augit 对现有设计系统的适配，尚非 PyCharm 同场景测量值。
- 表单左右留白 17px，上下各 16px；标签按实际字宽排布，标签与输入框间距 12px，输入左右内边距 8px。输入行与按钮至少 31px 高，并至少容纳当前字体实际行高加 12px；标题栏至少为行高加 14px。底部在按钮上下各留 12px，取消在左、确定在右，间距 8px。
- 标题、标签和按钮按实际字体度量；长标题或大字号扩大窗口，不能把字体缩小或裁切按钮。所有换算经 `NativeTheme` 完成；界面字体和主题覆盖输入及标题，不使用固定系统字体。输入聚焦使用 `accent` 边框，按钮焦点有独立可见轮廓；状态变化不移动控件。
- 标题栏关闭按钮复用已有关闭图形，32px 宽命中区，图形只随 DPI 缩放，不随用户字号放大；提供“关闭”悬停说明及辅助技术名称。

- **跳转行例外（2026-10-15 用户批准）**：按 PyCharm 2026.2.1、DPI168 原始参考图584×252物理px调整，默认逻辑宽334px、最小高144px，宽度至少27em且受宿主两侧12px约束。标题区至少40px或实际行高加18px，不画标题下分隔线；按实机DialogHeader保留左侧40px品牌槽：Augit蓝色A为16×16px、距左12px；标题从40px开始，右侧32px为关闭命中区，标题省略不能覆盖关闭按钮。按实机组件几何，表单上12px、下8px、左12px、右20px，标签最小宽93px，与输入相隔8px；默认输入命中区200×34px，高度至少为行高加14px，随大字号扩宽。输入正常绘制区域在命中区内缩3px，1px边框；聚焦内缩2px、2px品牌蓝边框，圆角半径3px，焦点切换不移动控件。外窗边框叠绘，不参与布局。标题常规字重、OK强调字重。底部上下8px、左右12px，OK在左、Cancel在右、间距6px；按钮命中区至少78×34px且随字号增长。错误说明独占底栏一行并扩大窗口，不挤压按钮。仅跳转行应用此布局，不改变其他紧凑窗口。标题和标签保留仅行号语义，不新增列跳转。

#### 三栏冲突解决器（2026-10-17 经批准移出范围）

**范围变更**：用户已批准移除内置三栏冲突编辑器，保留冲突提示、冲突文件列表、普通只读 Diff 和外部处理后的状态同步。本小节以下全部数值、视觉稿和像素记录仅作历史追溯，不再是当前设计约束、实现要求或验收目标；原有未完成状态登记为“经批准移出范围”，不得改写为通过。冲突文件改用普通只读 Diff 的既有视觉规则。

- 默认 Apply Changes 使用半粗字重600，其他底栏按钮保持400。对应参考默认按钮绘制时加粗、首选尺寸按组件普通字体计算的规则；保留现有普通字体宽度计算，并检查实际半粗文字不会裁切。
- 冲突底栏文本首选宽度按参考FontMetrics.stringWidth取最近逻辑整数，再加34px；不统一向上取整。UI12、Microsoft YaHei UI基线为Accept Left 99px、Accept Right 108px、Apply Changes 120px、Cancel 78px；其它字体及字号继续按实际文本度量，不能硬编码这四个宽度。

- 工具栏禁用图标单独使用参考灰阶，浅色 `#BDBDBD`、深色 `#5A5A5A`，不复用禁用文字的 `faint`。数值由安装版 PyCharm 2026.2.1 的 GrayFilter 对现有 icon 颜色计算得到，浅色已与原始参考箭头纯色核对；深色尚缺同态屏幕配对。禁用不显示悬停或焦点，不改变命中区和三栏布局；重新启用恢复 icon 颜色。

- 视觉稿为 `ux-mockups/conflict-resolver.html`。2026-10-15 用户批准按 PyCharm 2026.2.1 实机重排：文件名固定在标题，紧凑工具栏、左对齐来源标题、三栏正文及单层底栏依次排列。移除独立文件名导航行和重复返回底栏。默认宽度最多1080px、高度694px，受宿主限制；三栏先扣除连接带，左右取三分之一的整数宽度，余量归中央；正文无外层横向留白，左右始终只读。
- 设界面实际行高为 `h`：标题 `max(40px,h+18px)`，按钮 `b=max(34px,h+14px)`（含两侧各3px透明命中边框），工具栏 `max(42px,h+16px)`，来源行 `max(30px,行数×h+8px)`（已包含底边框）。横线连续穿过两条连接带，采用编辑器TEARLINE_COLOR（浅色#D4D4D4、深色#555555），按设备像素绘制。窗口总高至少覆盖243px正文和固定区域，并裁到宿主高度；正文独立滚动。以上是实机默认几何加现有字号适配，大字号不是参考实测；冲突窗口采用布局居中，避免编辑光标出现后的半像素合成偏移；175%原生初始横线3452像素与参考一致，接受左侧及全部处理后同一区域颜色稳定，整窗仍未完全配对，见[来源标题记录](pixel-conflict-title-separator-2026-10-15.md)。
- 底栏左右12px、上下8px；左侧 Accept Left、Accept Right，右侧 Apply Changes、Cancel，同组间距6px。各按钮宽为各自实际文字加34px、下限78px；两组放不下时分两行。取消复用原返回冲突会话语义，状态提示在顶部，应用仍校验并标记已解决。接受按钮继续处理当前块，可撤销；不得将布局重排解释为新增整文件接受行为。
- 工具栏左右外距8px、内距4px/6px、圆角6px；导航及当前块接受两侧/忽略侧为图标，具有完整 title 和 aria-label。仅展示已实现动作，不照搬参考智能合并等无对应能力入口；状态计数靠右。来源统一左对齐，必要时按真实字宽同时扩展行数。文件名省略但完整路径保留悬停说明。
- 保存进行中冻结所有动作并将结果区设为只读；失败、取消和外部读取冲突使用局部文字提示，不重建三栏、不清除结果正文或阅读状态。
- 接受、导航、取消、返回和标题关闭的悬停／按下使用 `hover`；主要应用动作保留 `accent`。动作背景圆角半径 5px，次要动作边框 1px；禁用文字用 `faint`、底色用 `panel-muted`（标题关闭仍与 `panel` 共底）。焦点沿用文档按钮的 1px 内框，主要动作追加 1px `panel` 外环，禁用不显示悬停或焦点。状态只重绘当前按钮，不改变三栏、选择和解析版本；隐藏、禁用与销毁清除悬停跟踪。
- 三栏正文采用来源槽接续所列留白、无额外顶部留白，行高为等宽字体实际度量的 1.2 倍并在设备空间取整；保留缩进，长行横向滚动。普通上下文使用 `panel`，仅实际冲突行铺满所在栏的正文宽度；覆盖中央/右来源正文两侧留白和左来源右侧3px间隔，左来源18px固定编辑器边距保持panel。长短行共享横向内容宽度，横滚后短行底色不得露出缺口。按 `DefaultColorSchemesManager.xml` 的 `DIFF_CONFLICT`，三侧基础冲突色均为浅色 `#FFD5CC`、深色 `#45302B`，中央不使用蓝底。不把整块编辑器染色或额外添加红蓝外框。真实草稿中央从祖先构造，接受动作逐侧处理。参考 `DiffViewerHighlighters` 在存在行内差异时使用减淡行底色、保留差异片段原色；已通过 CSS Highlight 实现行内分层，并验证过期异步结果不会恢复已处理高亮；分词算法及同态像素配对仍未完成验收，不能代表完整高亮复原。静态 `RefreshAsync` 样本仍需随真实参考更新，不能用样本证明中央初始状态一致。
- 冲突块较短的一侧在其末尾使用 `panel` 色展示留白，使后续冲突与公共正文落在同一纵向位置；留白每行为同一等宽正文行高，不显示行号、字符、边框或冲突底色，不进入复制、编辑、撤销及保存文本。既有示例左侧补两行、右侧补三行，三栏结尾大括号对齐。
- 文件第一行就是冲突且某侧内容为空时，留白从该栏正文顶部开始，同样随正文滚动；首行留白超过一屏时仍可用滚轮和滚动条进入后续正文。对应视觉稿为[首行左侧为空](ux-mockups/conflict-resolver.html?empty-side=left)和[首行右侧为空](ux-mockups/conflict-resolver.html?empty-side=right)。两侧及中央使用相同内容样本，公共方法声明与结束大括号保持同一纵坐标；HTML 行高也按当前 DPI 的物理像素取整，不能累积小数误差。

### 9.4 通知、空态和错误

#### 快速打开与全仓搜索浮层

两者共用默认 730px 宽的紧凑骨架。设界面实际行高为 `h`，标题至少 `max(41px, h + 20px)`，输入框为 `max(31px, h + 8px)`，结果行为 `max(32px, h + 8px)`；图标保持 16px，文字字号不改变图形。空态在输入框下留 8px，不预留结果空白；有结果后从输入框下 13px 开始向下增长，最多分配十行，超过宿主可用高度时结果独立滚动。

全仓搜索的三个图形开关复用当前文件查找图形，命中宽 28px、相隔 4px；启用用 `accent-soft` 底与 `accent` 图形，悬停、按下、禁用和焦点分别沿用通用按钮规则，并登记中文名称和 Tooltip。“包含忽略文件”按真实字宽加 22px 分配，不压缩文字。默认与标题同排，放不下时按原顺序移到标题下一行；仍放不下则将包含忽略文件整体换行。快速打开标题与快捷键放不下时，快捷键移到下一行。

文本搜索状态位于结果下方，按实际字体换行；最多占三行加上下各 6px，长原因在只读区域内滚动，不因错误扩成大面积浮层或重启查询。测量接收本次目标宽度，避免先用旧宽度排布再跳动。上述为已有字号、紧凑空态和局部反馈规则的落实，未登记为新增 PyCharm 同场景实测。

Diff 边界导航使用正文附近的非模态提示，见 [文件边界视觉稿](ux-mockups/diff-boundary.html)。移除参考软件的额外设置说明后，只保留一行提示：默认高度 32px、左右内边距 12px、圆角直径 6px、1px 边框；字体使用 `ui-body`，背景/边框/文字使用 `Panel`、`BorderStrong`、`Text`。宽度按当前字体实际度量，高度至少容纳当前字号并留 12px 垂直内边距。双栏靠近右正文起始位置，单栏靠近正文左侧，纵向贴近当前差异；距正文边缘至少 8px，空间不足时收回正文边界内。提示不进入 Tab 顺序、不接管焦点，不占布局行；这里是删除设置说明后的 Augit 布局适配，深色配色尚不计为 PyCharm 同环境实测。

- 空态只显示一句主说明和必要的一个动作，不使用大插画。
- 错误归属于发生区域，说明事实、未改变的状态和可执行动作。
- 加载只显示在目标正文或列表区域；不清空其他区域，不使用会造成布局跳动的全屏动画。
- 历史和引用 Diff 首次加载超过 150 毫秒后复用工作区 Diff 的局部占位和加载标记，不显示空代码控件或无用滚动条；已有正文时继续保留正文，只显示工具行加载提示。查询失败、主动取消和摘要使用界面字体显示局部说明，内容区左侧留 24px、上方留 28px，16px 状态图标与文字间隔 8px；错误图标使用 `danger`，取消和摘要图标使用 `muted`，文字使用 `text`。提示关闭后重新启用真实正文，不能改变列表、标签和分隔位置。该中间状态布局是依据现有组件规则的 Augit 适配，未取得 PyCharm 对应慢查询的直接观测。
- 成功、错误、警告和冲突同时使用文字与图标，不能只依赖颜色。

## 10. 主题与 DPI

- 默认跟随 Windows；用户可选择浅色或深色。主题切换覆盖整个主界面、Diff、弹层、对话框、Markdown 和终端。
- 主题切换只替换颜色和资源，不改变组件尺寸、间距、顺序或交互状态。
- 所有尺寸以 96 DPI 逻辑像素登记，通过统一缩放函数转换；禁止组件自行计算缩放比例。
- 96、120、144 DPI 必须保持相同的对齐关系。四舍五入后出现 1px 误差时，优先保持中心线和分隔线连续。
- 字体与栅格化比例变化后必须重新应用全部令牌；WebView2 按 DPI 调整栅格化比例，逻辑尺寸不变。

## 11. 实现约束

- 主界面为 C#、.NET 10 原生 Win32 外壳 + WebView2 渲染（用户已授权由原生自绘切换）；不得引入 WPF、WinForms、Avalonia 或其他新的桌面 UI 框架。
- `web/src/mockup.css` 的 CSS 变量是颜色、字体、DPI 与通用尺寸的唯一来源；页面样式必须引用变量，不得自行复制颜色或字体值。
- 所有 owner-draw 图标必须经过统一绘制入口，使用已登记的各图标线宽、端点、填充和状态颜色，不由各页面另行解释。
- 滚动容器两条滚动条交界的空角只按主题 `panel` 令牌补画；不得接管滚动条绘制或覆盖正文，滚动容器移除时必须一并移除该绘制。
- 所有图标按钮必须在创建时登记命令、启用条件、可见标签和 Tooltip；没有命令映射的图标不得绘制。
- 视觉稿与运行时界面共用同一份 HTML/CSS；真实验收必须来自真实外壳截图（`tools/audit/shell-capture.ps1`），不得只以浏览器渲染视觉稿作为验收依据。
- 修改可执行逻辑时补充自动化测试；仅修改本设计文档不新增测试要求。

### 11.1 令牌权威来源

运行时令牌以 `web/src/mockup.css` 的 `:root` 与 `body[data-theme="dark"]` 为唯一权威，包含颜色、字体与主题切换。`docs/ux-mockups/mockup.css` 与该文件必须逐字节一致（由 `tools/audit/verify-ui-assets.ps1` 保证）。视觉稿后续调整应直接修改该文件，不得为单个页面保留另一套颜色。

复原 PyCharm 2026.2.1 New UI 时的**取值来源**见 [IntelliJ Platform New UI 数值参考](intellij-platform-ui-reference.md)。该文档记录从 IntelliJ 开源仓库提取的精确色值、尺寸、间距与提交图几何及其文件出处，是推导上述令牌时的上游输入，**不取代本节的权威地位**：令牌取值仍以 `mockup.css` 为准，两者冲突时以本节和 `mockup.css` 为准。本文只记录可测量数值与规则，不分发 JetBrains 官方资源文件；图标继续按 §7.0 用项目内自绘等效实现。

## 12. 逐页审计清单

每个产品规格场景都按以下顺序检查：

1. 主框架：标题栏、全局工具栏、工具窗口、编辑区和状态栏位置是否未变。
2. 尺寸：44/42/32/16/28/30/13px 令牌是否按 DPI 缩放，分隔线是否为 1px。
3. 字体：界面字体、等宽字体、字号、字重、基线和省略号是否一致。
4. 图标：图形是否在 16px 网格，线宽、中心点、命中区和 Tooltip 是否一致。
5. 间距：标题、图标、文字、列表行、标签和按钮是否使用统一刻度或已登记的 PyCharm 场景测量值。
6. 状态：普通、悬停、选中、禁用、加载、错误和危险状态是否同时具备颜色与文字/图标表达。
7. 局部更新：加载、刷新、异步结果是否只改变所属区域，是否保持焦点、滚动和分隔位置。
8. 主题与 DPI：浅色、深色、96/120/144 DPI、最小和推荐窗口是否保持相同结构。
9. 资源：测试关闭后无残留进程、WebView2、终端会话或临时文件。

截图对照时优先检查文字基线、图标中心点、按钮热区、标签高度、树缩进和分隔线连续性；这些误差比单个颜色差一个色阶更容易造成粗糙感。

`PrintWindow` 截图用于检查结构、控件范围和主题状态，不能单独证明屏幕上的字体与参考一致。字体、抗锯齿和字宽验收必须补充相同 Windows 缩放下的屏幕截图，确认目标窗口可见且没有被其他窗口覆盖后再采集；被遮挡的区域不得作为验收证据。

## 13. 现有界面收敛顺序

按以下顺序整理当前实现，每一步只统一已有组件，不添加能力：

1. 收敛 `--augit-*` 令牌与界面字体的度量落点（`web/src/mockup.css` 的 CSS 自定义属性）。
2. 收敛 `toolbarIconShapes` 图标的 16px 网格、线宽、命中区和 Tooltip 登记。
3. 收敛主窗口、工具窗口、标签栏和状态栏的结构尺寸与 4px 间距。
4. 收敛文件树、Changes、Git 历史和搜索列表的行高、缩进、选中及禁用状态。
5. 收敛普通按钮、图标按钮、输入框、选择框、菜单和模态框的状态。
6. 逐页按浅色、深色、最小/推荐窗口和 96/120/144 DPI 生成真实截图，对照 `ux-mockups` 修复偏差。
7. 运行现有自动化、格式检查、截图审计和资源清理检查后，记录当前视觉验收证据；历史路线图不作为当前设计依据。

### 2026-10-10查找与Blame组件令牌

查找栏在输入外壳内放搜索图标和三个28px按钮，开关间距4px、组右内距2px；计数样本文本9888 results，正常数量变化不移动导航。输入外壳边框1px、圆角3px；关闭按钮贴右侧。无匹配/搜索中/组词时导航保持位置并禁用。

Blame浅色五锚点为#A1BEF7/#C9DAFB/#DEE8FC/#EDF2FA/#F7F8FA，深色为#2F5194/#2D416B/#283754/#242D42/#22252E；每相邻锚点四个插值色，通道按ColorGenerator浮点截断生成21色，CSS令牌为--augit-blame-color-0到20。归属行通过提交者时间分组选择索引。富文本提示最大宽600px、客户区四边至少4px、内边距8px/10px、圆角4px，沿用面板与主题边框/文字令牌；提交链接用主题蓝色。此处记录WebView2等价实现，不声明提示全部像素已经同场景验收。

### 三栏中央编号槽（2026-10-14）

中央结果不保留来源接受/忽略空间：槽宽为max(16px, 实际最大编号字宽)+27px，正文再右移3px，编号右侧21px；默认43px。编号逐项使用正文等宽字体、字号和字距，禁止依赖可能为空的font简写。固定留白来自本机175%参考图，详见[测量与边界](pixel-conflict-gutter-2026-10-14.md)，其他字号仅完成自适应验证，未宣称参考像素全量通过。

### 三栏块连接带（2026-10-14）

2026-10-15来源槽接续：分隔线仅沿行号槽与正文的交界绘制，左来源镜像至槽左边，中央和右来源在槽右边；连接带外缘不绘制贯穿来源标题的竖线。左右编号也使用max(16px,实际最大编号字宽)，槽再加60px；左正文18px，右正文在槽后3px。左动作距槽左18px，右动作距槽右12px；左编号固定左边54px以容纳多位，右编号距槽右51px。来源标题及连接带使用编辑器panel底色。这些留白来自本机175%参考图，跨字号只验证无重叠，不宣称像素全量通过；证据见[pixel-conflict-source-gutter-2026-10-15.md](pixel-conflict-source-gutter-2026-10-15.md)。

三栏先扣除两条24px连接带（diff.divider.width），左右逻辑宽度向下取整，中央承接余量，与ThreeDiffSplitter一致。连接形状采用0.3/0.7三次贝塞尔控制点；未处理侧填DIFF_CONFLICT原色，已处理侧无填充、上下2.3px的2/2点线；空侧双边界。显示层不接收指针，不进入可编辑正文，滚动复用路径。实现与精确栅格未验收边界见[连接带记录](pixel-conflict-connectors-2026-10-14.md)。

2026-10-15终端行高裁决：按PyCharm实机使用JetBrains Mono13和基础行距1.0，默认参考约18逻辑px。静态终端和实时xterm均使用实际字体基础行格；随包字体加载后为ceil(字号×1.02)+ceil(字号×0.3)，其他字体沿原始度量，须记录实际度量和DPI取整，不再复用只读正文22px行高。此项只改变终端，不改变正文、Diff、冲突或工具栏。

2026-10-15实时终端格宽：终端仍不套用编辑器CSS字距。随包JetBrains Mono实际加载后，经xterm DOM专用格宽选项将单列设为 round(round(字号×DPR)×0.6)/DPR；来源为CharacterGrid/EditorView的粗斜体W网格及256组独立JBR度量。文字、光标、选区和鼠标命中共用网格；xterm行后代继承内部补偿字距，禁止全局零字距覆盖。其他字体或资源未加载时回到上游度量。用户未增加外观选项，字形不缩放。真实两行宽差最终降至0/0物理px，基线、抗锯齿与完整配对仍未通过，见[终端格宽记录](pixel-terminal-grid-2026-10-15.md)。

2026-10-15终端行格接续：xterm增加与格宽同源的augitCellHeight，避免浏览器把AWT默认18px、17号字24px压成约17.18/22.25px。真实17号字双端相同字母步长均42物理px，两行宽度一致；13号字当前32对参考31，仍有舍入/基线差异，不计完整通过。来源、逐字分析与失败边界见[终端行格配对](pixel-terminal-row-grid-2026-10-15.md)。

2026-10-17终端命令块有意差异（用户已批准）：Augit 保留简洁 xterm 连续字符网格，不实现 PyCharm Reworked 引擎的命令块装饰。参考实现 `TerminalBlocksDecorator` 在 `showSeparatorsBetweenBlocks` 下为非首块顶部生成 7 逻辑px、已完成块底部生成 6 逻辑px 的留白（经 `JBUI.scale`），并绘制块分隔线；由此产生的命令块分隔与块间行距差异登记为**有意差异**，不再作为终端验收缺口。除此之外，文字字形与行格、颜色、面板尺寸和位置、实际内容、光标与选区仍须对照；有意差异不得用于解释或掩盖其他未通过项。

2026-10-15已处理正文及编号槽边界：按UIUtil.drawBoringDottedLine每4逻辑像素绘制2px矩形，宽高按设备像素取整，边界从y-1开始；空范围重复两行后合并为3px高。绝对定位不改变行盒或保存文本。175%实机主体着色4/留白3，边缘厚度及抗锯齿仍未通过像素配对，详见[pixel-conflict-line-pair-2026-10-15.md](pixel-conflict-line-pair-2026-10-15.md)。

2026-10-15冲突边界矢量接续：渐变与图案贴图会损失边缘像素，改用只读SVG直接路径；实际175%上4排/下3排与参考一致，两段局部平移配准共3000像素零差异，不等于整窗同态通过。12组严格栅格及真实单块/多块/空侧保存通过。证据、失败、哈希与清理见[三状态记录](pixel-conflict-line-pair-2026-10-15.md)。整体目标仍未完成。

2026-10-15运行时诊断补记：PyCharm 262.9437.214在988逻辑像素宽度下，三栏313/314/313px、连接带24px。来源槽总宽79px、中央46px；分隔线位置分别距起点76/43px，余下3px属于正文前留白。Augit继续以分隔线为槽边界，正文留白由2改3px；槽线使用INDENT_GUIDE浅色#EBECF0、深色#313438。截图仍有中央2物理px及右侧1物理px残差，不能宣称完全对齐。详见[pixel-conflict-runtime-geometry-2026-10-15.md](pixel-conflict-runtime-geometry-2026-10-15.md)。

2026-10-15客户区与底栏接续：参考根面板Insets为0，冲突窗口外框改为不占布局的覆盖描边；默认工具栏42px、来源标题30px。底栏上下8px、左右12px，同组按钮间距6px；左右接受分别按各自字宽加34px、下限78px定宽，取消/应用同样度量；单层底栏为按钮高加16px，换行为两倍按钮高加24px。客户区高度与宽度不再被外框压缩。上述修改基于参考运行时及DarculaButtonUI、MergeRequestProcessor源码；大字号只证明自适应，未证明参考像素一致。详见[pixel-conflict-client-area-2026-10-15.md](pixel-conflict-client-area-2026-10-15.md)。

2026-10-15冲突正文尾部留白：按PyCharm 2026.2.1运行时EditorComponent Insets，中央结果与右来源正文右侧保留18逻辑px；左来源仍为左18px、右侧行号槽及3px正文间隙。行高、基线、文字起点及保存内容不变，整行高亮覆盖该尾部留白。依据和验证见[pixel-conflict-end-inset-2026-10-15.md](pixel-conflict-end-inset-2026-10-15.md)。

2026-10-15冲突正文LCD：不透明正文使用无位移translateZ(0)独立栅格化，不设置会禁用LCD的will-change，避免正文并入透明scrim层。保留背景快照、行盒、文字坐标和保存语义。运行时追踪、反证及未验限制见[pixel-conflict-lcd-text-2026-10-15.md](pixel-conflict-lcd-text-2026-10-15.md)。

2026-10-15冲突光标行号：三栏各自保留当前逻辑行，编号浅色#767A8A、深色#A1A3AB，失焦后保留；选择活动端、插入/删除及编号重排同步，空白对齐占位不编号，不改变正文与保存内容。20项定向检查及真实多块保存通过，整窗仍未配对，整体目标未完成。依据、哈希、证据和启停清理见[光标行号记录](pixel-conflict-caret-numbers-2026-10-15.md)。

2026-10-15冲突正文基线：既有JBR目标基线与行高不变，正文leading及顶部留白先经CSSStyleDeclaration规范化再作为共享变量传给所有行，避免嵌套calc残差使WebView2基线向下截断。不得逐行增加固定设备像素平移；插入行继承相同参数，保持行盒、编号、背景和保存内容。真实13px三栏基线16逻辑px；其它字号/DPR为定向自动化验证，不代替参考实机。见[基线序列化整改](pixel-conflict-leading-2026-10-15.md)。

2026-10-17已处理左来源边界：起点遵循EditorPainter正文Insets.left与UIUtil的4px周期取整，默认18px留白对应16px点阵起点；不得从整栏零点覆盖固定留白。只修正显示层起点，不改变行盒、草稿或字体；实机相位仍有残差，见[正常入口配对](pixel-conflict-daily-inset-2026-10-17.md)。

2026-10-16冲突客户区布局：保持原生外窗物理尺寸，内部按向下取整的逻辑宽高排布，设备像素余量不推移右侧按钮或底栏；外窗尺寸变化重新度量。该规则来自参考整数根面板，实机按钮几何已对照，完整屏幕像素仍未通过，见[本批证据](pixel-conflict-save-lifecycle-2026-10-16.md)。

2026-10-16远端新增表单高度补记：应用字段和字号令牌后，按完整表单实际高度、正文上下内距及border-box上下边框确定内容所需高度，避免默认尺寸出现2px无意义滚动及大字号复选框溢出；受视口限制时仍只滚动正文。不是参考两行表单的像素匹配声明，见[本批记录](pixel-remote-daily-entry-2026-10-16.md)。

2026-10-17远端空态历史阶段：正常启动器取得修前／修后空态及Define原始PNG，前景、DPI、指针门禁通过；补齐ToolbarDecorator外框、表头分隔。4组空态、8组Define布局及41项远端行为通过，尺寸与标题工具条当时未完成，见[前批记录](pixel-remote-table-2026-10-17.md)。后续用户批准按实机调整且保留Push URL，主窗已624×385；空态按StatusText默认规则水平居中、纵向位于剩余空间三分之一，禁止再沿用居中布局。最新仍差20982像素，不计整页通过，见[批准后整改](pixel-remote-approved-layout-2026-10-17.md)。

2026-10-17远端列布局：依据实际JTable，preferredWidth70/250在330宽空表分配为75/255；窗口限宽时按距minWidth15的余量收缩。表头和正文共享列边界，表头renderer左右内距8、正文SimpleColoredComponent左右内距2，不要求文字同起点。原始证据与未通过边界见[pixel-remote-table-columns-2026-10-17.md](pixel-remote-table-columns-2026-10-17.md)。
