# IntelliJ Platform New UI 行为与页面逻辑（索引）

本文是 [New UI 数值参考](intellij-platform-ui-reference.md) 的配套索引。数值参考管"静态取值"，本文与 `nui-behavior/` 下的分册管"**交互与页面逻辑**"。

- 分册是**取证记录**，不是规格。界面规则以 [design-system.md](design-system.md) 与 [ux-spec.md](ux-spec.md) 为准，产品行为以 [product-spec.md](product-spec.md) 为准。
- 运行时令牌的唯一权威仍是 `web/src/mockup.css`（design-system.md §11.1）。
- 冲突处理原则（用户已确认）：**完全按 New UI 对齐**；只有当产品规格明确"不做"某能力时，才以产品规格为准，并在本文登记为**有意的产品偏离**。

## 1. 分册索引

| 分册 | 覆盖 | 行数 |
| --- | --- | --- |
| [01-tool-window.md](nui-behavior/01-tool-window.md) | 布局模型、ultrawide、stripe 与按钮、显示/隐藏/激活状态机、分隔条权重、标题与图标 | 594 |
| [02-tree-list.md](nui-behavior/02-tree-list.md) | 选中与焦点状态机、键盘导航、speed search、展开折叠、行高与缩进、复选框、滚动 | 410 |
| [03-editor-tabs.md](nui-behavior/03-editor-tabs.md) | 标签几何与两套渲染、选中/悬停、关闭与拖动、溢出、预览标签、只读正文、行号、空白符 | 609 |
| [04-actions-icons.md](nui-behavior/04-actions-icons.md) | 动作可用性模型、禁用 vs 隐藏、Toggle、图标状态解析与滤镜、按钮状态机、菜单分组 | 611 |
| [05-find-search.md](nui-behavior/05-find-search.md) | 查找条几何、防抖与首个匹配规则、高亮属性、导航与越界、选项开关、性能 | 397 |
| [06-commit-graph.md](nui-behavior/06-commit-graph.md) | 轨道分配算法（含伪代码）、边类型与绘制、配色公式、引用标签、行布局、筛选分页 | 393 |
| [07-theme-dpi-dialogs.md](nui-behavior/07-theme-dpi-dialogs.md) | 主题切换与重建、DPI 缩放与对齐、字体字号、弹层状态机、对话框、通知、设置 | 442 |
| [08-diff-merge.md](nui-behavior/08-diff-merge.md) | 变更类型到颜色键的映射、行底有效值、gutter 标记绘制、未变更区分隔条 wave 图案、行内词级差异；**三栏合并与冲突解决**（gutter 操作的存在性判据、每侧高亮器安装规则、动作组与启用判据） | 260 |
| [09-icons.md](nui-behavior/09-icons.md) | expUI 图标的可测量参数：填充 vs 描边、默认线宽（**1px**，非 1.5）、端点/转角、逐图标几何；两轮已落地项（线宽修正 + 填充化）与填充化剩余项的最终归类 | 187 |
| [21-markdown-preview-fonts.md](nui-behavior/21-markdown-preview-fonts.md) | Markdown 预览正文与代码块字号继承、界面字号联动、死 CSS 令牌清理 | 88 |
| [22-icon-alignment.md](nui-behavior/22-icon-alignment.md) | JSON、YAML、Markdown、文件夹、搜索、复制、终端等图标几何对齐审计与差异归类 | 36 |
| [23-project-tree-gestures.md](nui-behavior/23-project-tree-gestures.md) | 项目树箭头、目录名称、文件行的单击/双击/Enter 手势分流与焦点恢复 | 18 |

每份分册的规则都带 `（来源：相对路径:行号）`，并标注 **【可直接实现】/【Swing 特有】/【需推断】**。

**路径注意**：本仓库是 Bazel 布局，若干包不在常见位置——
查找功能在 `platform/lang-impl/src/com/intellij/find/`（不在 `platform-impl`）；
动作系统包名是 `com.intellij.openapi.actionSystem`（不是 `action`），`AnAction`/`Presentation` 在 `platform/editor-ui-api`。

## 2. 本轮已落地的改动

### 2.1 浅色/深色结构令牌对齐（已生效）

`web/src/mockup.css` 的 `:root` 与 `body[data-theme="dark"]` 已按 expUI 取值改写，权威键与出处见数值参考 §5.2 与 §7。

| 令牌 | 浅色（原 → 新） | 深色（原 → 新） | expUI 键 |
| --- | --- | --- | --- |
| `chrome` | `#E9EAEE` → `#F7F8FA` | `#2B2D30`（不变） | `ToolWindow.background` / 浅头部 `MainToolbar.background` |
| `panel-muted` | `#F5F8FE` → `#F7F8FA` | `#25262A` → `#2B2D30` | `*.background` |
| `border` | `#E3E3E3` → `#EBECF0` | `#393B40` → `#1E1F22` | `*.borderColor` |
| `border-strong` | `#D1D3D9` → `#C9CCD6` | `#4B4D53` → `#4E5157` | `Component.borderColor` |
| `text` | `#202124` → `#000000` | `#DFE1E5`（不变） | 浅头部 `MainToolbar.foreground`（Gray1） |
| `muted` | `#646870` → `#6C707E` | `#9DA1AA` → `#9DA0A8` | `StatusBar.Widget.foreground` |
| `faint` | `#A0A4AA` → `#A8ADBD` | `#6F737B` → `#5A5D63` | `*.disabledForeground` |
| `blue` | `#3871E1` → `#3574F0` | `#548AF7` → `#3574F0` | `*.focusColor` |
| `blue-soft` | `#D0DFFE` → `#D4E2FF` | `#2F466F` → `#2E436E` | `*.selectionBackground` |
| `selection-inactive` | `#E9EAEE` → `#DFE1E5` | `#43454A`（不变） | `*.selectionInactiveBackground` |
| `history-selection-inactive` | `#E9EAEC` → `#DFE1E5` | `#43454A`（不变） | 同上（原值实为 `hoveredBackground`） |

> **本表是早期一次 expUI 对齐的历史记录，不是当前值。** 后续轮次把其中多项又改回 ManyIslands 家族语义别名：`chrome` `#F7F8FA` → `#E9EAEE`（`gray-150`）、`blue-soft` 浅 `#D4E2FF` → `#D0DFFE`（`blue-140`）、`panel-muted` 浅 `#F7F8FA` → `#F7F8F9`（`gray-160`，第 131 轮）、`border`／`border-strong` 同理。**当前值一律以 `docs/design-system.md` §6.1（浅色）／§6.2（深色）为准**。（`--augit-blue` 令牌本身现值与本表末行一致；但焦点环与主按钮已改走 `accent-brand-bg` = 浅 `blue-80` `#3871E1`／深 `Blue6` `#3574F0`，见第 90／117 轮。）

### 2.2 修复了一个静默失效的重复令牌块

`mockup.css` 原有 **两个 `:root` 块**（文件顶部一个，"第二轮视觉基线"处还有一个）。第二个位置更靠后、同优先级，**静默覆盖**第一个——所以对第一个块的颜色改动不会生效。这正是历史上"改了颜色看不出变化"的成因。

已把第二个块收敛为只保留它真正需要偏离的尺寸项（`--augit-side-width`），颜色令牌回到单一来源。颜色令牌现在每个恰好 2 处定义（浅色 + 深色）。**后续新增令牌必须在文件顶部的两个块中声明，禁止再引入第三个同名块。**

### 2.3 验证证据

| 验证 | 结果 |
| --- | --- |
| 资源字节一致（`tools/audit/verify-ui-assets.ps1`） | `PASS: runtime UI assets match the visual mockups.` |
| 验收套件（`node tools/audit/live-shell.spec.cjs`） | **`live-shell 通过 2373 项断言`**（退出码 0；本轮完整实时套件覆盖终端、搜索、Diff、Blame、文件历史、设置、DPI/字号、读写竞态及底部工具窗口生命周期；历史轮次见下方快照沿革） |
| 令牌唯一性 | 全部颜色令牌各 2 处定义（浅/深），无第三处覆盖 |
| 断言是否硬编码色值 | 零个 hex 字面量——套件只断言令牌名、几何与状态，因此配色变更不会打挂断言 |

验收套件用的是 **HEAD 版本**的 `live-shell.spec.cjs`（另建临时副本运行、跑完即删）。原因：并行会话当时正在改该文件，其工作区版本存在一个两参数 `page.evaluate` 的 Playwright 报错（`Too many arguments...`），会在设置场景提前中止；用 HEAD 版本才能隔离本轮改动。

> **第 136 轮的例外**：本轮**必须**改这个文件里的断言（它们编码的是"空提交信息点击即拒绝"的旧交互，按权威应改成"确认后继续"）。做法是 **HEAD + 仅这两处断言改动** 的临时副本（工作区版本仍因并行会话的 `Too many arguments` 跑不起来，实测确认）。因此上表的 `live-shell.spec.cjs`（HEAD 版）哈希仍指 HEAD 原样；工作区版本的改动本身要等并行会话收尾后才能整套跑通。断言数由 1070 → **1073**（本段两处改动合计：删 5 条旧断言、加 8 条新断言）。

#### 已验证快照（后续轮次用它低成本确认"代码未变、上次结论仍有效"）

**当前复核（2026-10-05）**：完整实时套件运行通过，退出码 0，共通过 **2373 项断言**。本轮工作区受登记文件 md5 已重新计算，以下快照值按当前工作区更新；资源字节一致、语法检查、文档声明与交互基线已复核。

`live-shell 通过 2373 项断言` 对应的文件哈希（**当前复核，2026-10-05**：本轮完整套件通过 2373 项断言；以下为当前工作区实测值）

| 文件 | md5（2026-10-05 当前复核值） |
| --- | --- |
| `web/src/mockup.css` | `5f6d41b10241085d04d0ef3f8a20c8b6`（当前复核：快捷键配套布局、工具轨步长、工具栏尺寸与 Diff 顺序） |
| `web/src/mockup.js` | `e4694d910af5788bf7ce251288f4b710`（当前复核：树状态映射、状态标记与历史文件通用选择器） |
| `web/src/live-data.js` | `6812d388cb695ce1902389a718f8c60a`（当前复核：搜索/快捷键状态机、终端代际、树状态变化与 HEAD 快照） |
| `web/src/bridge.js` | `8d2d3173074a8558cd12e6778b3bfc82` |
| `live-shell.spec.cjs`（工作区版） | `41d4b57e1b77a7f242697acc1222e5a4`（当前复核：项目树 Git 状态、快捷键/搜索/查找状态机与布局断言，断言数 2332 → 2373） |
| `web/src/markdown.js`（第 283 轮加入本表） | `192c3443bbe1a28bba7459160c3d35b4`（图片阻止与相对候选占位） |
| `web/src/image-preview.js` | `97f7e23b8b2b4bcb0c27e1c2e6dcd78f`（当前复核未变） |
| `web/src/current-find.js` | `c8d284f5462bfce475f28a9dd1bede9e`（当前复核：失焦 Esc、重复 Ctrl+F 与 IME 状态边界） |

第 182 轮（引用树多选的**宿主侧**）只动了 C# 与测试，第 183 轮把界面侧接上后四个运行时哈希才变化；两轮的单测合计 Core 86＋Shell **100**＋Infrastructure **181**；第 186 轮（用户弹层的**宿主地基** `git/authors`）同样只动 C# 与测试 ⇒ 上表四个哈希与断言数**不变**（沿用第 185 轮的 `live-shell 1170`、35/35 `verify-ux-*.cjs`），Infrastructure 增至 **183**（第 187 轮加多用户筛选的宿主侧，同样只动 C# ⇒ 哈希与断言数不变）。

`docs/ux-mockups/mockup.css` 与 `mockup.js` 分别与 `web/src/` 同名文件字节一致（`current-find.js` 同理，由 `verify-ui-assets.ps1` 逐副本核对）。本文其他各轮说的"四个运行时哈希"指上表前四行（`mockup.css`／`mockup.js`／`live-data.js`／`bridge.js`），`current-find.js` 自第 233 轮起才登记。**只改文档时不需要重跑套件**——先比对上表，全部一致即可沿用上一次结论。

> **已结案（第 195 轮）**：工作区里的 `tools/audit/live-shell.spec.cjs` 曾混入**并行会话的在途改动**（`tab/status/tree-height` 的新期望、`§154` 的 `bulk-007`、`§7.17 设置生命周期` 断言块；其中 `settingsLifecycle` 的 `page.evaluate(fn, key, value)` 是 Playwright 不允许的两参数用法，会让整轮中断），第 178–194 轮因此改用"HEAD 版 ＋ 本轮 hunk"的临时规格（`build-head-verify.cjs` → `_head-verify.spec.cjs`，跑完即删）。第 195 轮把这三处按**已裁决的取值**改回工作区版本（三处高度回到规格名义值、写死值回到 `bulk-006`），并把两参数 `evaluate` 改成单对象载荷 —— 于是 **`node tools/audit/live-shell.spec.cjs` 可以直接跑**（`live-shell 通过 1188 项断言`），此前一直被剔除的 `§7.17` 生命周期断言也归位。临时生成器与临时规格**作废**；上表的断言数与哈希此后都指工作区版本（产品哈希仍照常登记）。

> 历史记录（第 116 轮）：当时套件测得 `tree-height=28`，并按当时的 27–30px 规格将其登记为有意差异。该裁决描述的是旧实现，不再代表当前状态；本轮已将项目树行高按 `Tree.rowHeight=24` 收敛，当前动态规则为 `max(24px, h + 8px)`，默认界面字号下为 24px。

### 2.4 修掉文档表与运行时权威的漂移

`design-system.md` 的 §6.1/§6.2 颜色表**长期落后于 `mockup.css`**：表中仍是改前的手采样值（浅色 `panel-muted #F5F8FE`、`border #E3E3E3`、`text #202124`、`accent #3871E1` 等），而运行时早已换成 expUI 权威值。深色的 `success`/`danger`/`warning` 也与 `mockup.css` 不一致（表里是 `#6AAB73`/`#E37A7A`/`#EBA11B`，实现是 `#57965C`/`#F05F5F`/`#C77D20`）——这处漂移在我的改动之前就存在。

`design-system.md` §11.1 声明 `mockup.css` 是唯一权威，因此表按实现同步，并给每一行补上它的权威键（如 `accent` ← `*.focusColor` = `Blue4`）。同步用脚本比对完成，不靠肉眼：两个主题各 16 行，当前 **0 处不一致**。

**教训**：改运行时令牌时必须同时改登记表。只改 `mockup.css` 会让文档变成一份"看起来权威、实际过期"的说明，下一个接手的人会照它改回去。后续每次改令牌都应跑一遍这个比对。

## 3. 收集过程发现且已裁决的规范冲突

| 项 | New UI 权威 | 现行规范 | 裁决 |
| --- | --- | --- | --- |
| Diff 删除行色相 | `DIFF_DELETED.BACKGROUND` = `#D6D6D6` 灰 | `danger` 红 | **改按 New UI**（design-system.md §6.2 已改；**第 223 轮订正取值**：`#767A8A` 是 `DELETED_LINES_COLOR`，属**行号槽实心标记**色族，不是正文行底） |
| Diff 修改行色相 | `DIFF_MODIFIED.BACKGROUND` = `#C2D8F2` 蓝（整行底取 `mix(…, 编辑器底, .6)` = `#E7EFFA`） | `warning` 黄 | **改按 New UI**（同上，`#88ADF7` 是 `MODIFIED_LINES_COLOR`，属行号槽色族） |
| 浅色 `chrome` | `Gray13 #F7F8FA` + 项目渐变 | `#E9EAEE` | **改按 New UI**（§2.1 已改） |
| 树/列表行悬停 | Project View 与 Changes 树**无悬停高亮**（未安装 `TreeHoverListener.DEFAULT`）；树/列表悬停键分获焦 `#EDF5FC` / 失焦 `#F5F5F5` | design-system.md §8.3 规定树与 Changes 行有 `hover` 悬停底 | **已裁决为「有意产品差异」（第 100 轮用户裁决）**：保留悬停底、取值改用权威 `--augit-row-hover`；参考实现只是未启用平台支持的悬停（键注释 "…if hover is allowed"）。规范矛盾已在本轮（第 214 轮）统一，见 `ui-classification.md` §7 T7 |
| 项目树行高 | `Tree.rowHeight = 24`（参考图实测约 23.5） | 旧实现默认 28；当前实现 `max(24px, h + 8px)` | **已按 New UI 收敛**：默认界面字号下项目树行高为 24px。第 116 轮的“保留 28 基准”是旧实现当时的有意差异，现已被本轮整改取代。Changes、历史和普通列表使用各自组件行高，不由该树令牌统一决定 |
| 只读正文行高 | `字体度量高 × 1.2` | 等宽字号的 1.7 倍 | **已裁决为「有意产品差异」**：`design-system.md` §4.3 规定 1.7×（默认 13px→22px）为 Augit 正文字密度；权威 1.2 登记在案，不改 |
| 空右键菜单 | 补一条占位项 | "不显示不可用的占位项" | **以产品规格为准**，登记为有意偏离 |
| 冲突解决器 Continue/Skip/Abort 文案 | 平台无硬编码来源，走 `MERGE_ACTION_CAPTIONS` 钩子 | 由 Augit 规格定义 | **以产品规格为准**；可照搬的只有启用条件机制 |
| 字号范围 9–40px | 平台无此约束（建议 8–72、不校验上限） | 9–40 | **以产品规格为准**，登记为有意偏离 |
| 模态遮罩 | 平台**没有**模态变暗层 | 视觉稿含遮罩 | **已实施（第 11 模块）**：`.scrim` 改为透明点击承接层、去掉背景变暗，代码与像素两条证据见 §11。**第 229 轮补**：深色主题当时还留着一条特异性更高的 44% 混色覆盖（实际把背景压暗），已删除并加断言（浅/深 × 三缩放） |
| 编辑器标签渲染 | 两套互斥：expUI 经典（平铺 + 下划线）／Islands（28px 圆角卡片） | 现有稿混用两套特征（`border-radius: 6px` + 底色方案） | **定为 expUI 经典**，依据见数值参考 §5.2.1（截图实测标签行几乎纯白、无卡片底色带） |
| `TabbedPane.tabHeight = 40` | 编辑器标签栏在 40px 栏内绘制 28px 卡片，上下各保留约 6px | 现状 40px 栏 / 28px 卡片 | 现状已按 IslandsTabPainter 收敛；42px 是旧实现记录 |

## 4. 实现模块与逐项状态（每一项都已归类，不是待办队列）

按依赖顺序列出，每块独立改动 + 独立验证。**本节不是待办队列**：每项都写明最终归类（✅＝已实施；其余＝已裁决的「有意产品差异／不适用」并给出依据）。

1. ✅ **主工具栏项目配色渐变**（数值参考 §3.1、design-system.md §6.5）—— **已实施并验证**，见 §6。
2. ✅ **项目树行高、缩进与复选框几何**（分册 02）：项目树行高现按权威 `Tree.rowHeight=24` 实现；第 116 轮曾保留 28px 作为有意差异，属于旧实现历史。**缩进与 `Tree.border` 的逐值核对已由第 217 轮关闭**（原 T9）：按权威 `Tree.leftChildIndent`(7) + `Tree.rightChildIndent`(11) = **18px 一级步长**订正（原 `design-system.md` §8.3 写的 16px 已作废），`Tree.border = 4,12,4,12`／`List.border = 4,0,4,0` 是 Swing 的组件外内距，Augit 的 DOM 没有这一层、用行内距表达同一边距（登记为实现方式差异，见 `ui-classification.md` §1.22／§1.23），并加了 dpi×字号矩阵断言（`verify-ux-project-tree.cjs`）；复选框几何已按分册 09 第 11 轮落地（圆角 2.5、勾形 2px、半选填充条、选中底色令牌化）。
3. ✅ **树/Changes 行悬停** —— **已裁决为「有意产品差异」（第 100 轮用户裁决）**：保留悬停底、取值用权威 `--augit-row-hover`；表格类行悬停另按权威（`JBTable` 默认装监听）落地。见 §10.3 与 `ui-classification.md` §1.15。
4. **只读正文行高**：**已按 `design-system.md` §4.3 的 1.7× 保留，登记为「有意产品差异」**（权威运行时为 `字体度量高 × 1.2`；见 §3 与 `ui-classification.md` §1.17）。**行号列**（按最长行号字符串量测、右对齐、字号 −1pt）已按分册 03 落地并有像素与断言证据；此处不再作为"待实施"。
5. ✅ **Diff 与文件状态配色** —— **已实施并验证**，见 §7；取证见分册 [08-diff-merge.md](nui-behavior/08-diff-merge.md)。
6. ✅ **编辑器标签几何与状态（CSS + 量测）** —— **已实施并验证**，见 §9；关闭叉显示时机与标签条细滚动条按 §9.4 归类为**有意产品差异**（产品规格 `ux-spec.md` §7.1 要求后台普通文件标签可由关闭叉关闭；Augit 用原生滚动容器并隐藏细滚动条）。
7. ✅ **提交图几何按行高缩放（第 310 轮已实施并验证）** —— `web/src/mockup.js` 的 `graphGeometryFor()`／`commitGraphWidth()` 按 `PaintParameters.scaleWithRowHeight` 与 `PaintUtil.alignToInt(…, FLOOR, ODD)` 推导轨宽／轨中心／行中心／节点直径／HEAD 三同心圆／线宽，`commitGraphSvg()` 据此绘制；`applyTypography` 里那句"节点半径、横向轨距和笔画不随字号拉伸"的旧注释与旧行为**已删除**，固定 `--augit-graph-width: 45px` 与 `vector-effect: non-scaling-stroke` 一并移除。断言：`tools/verify-ux-commit-graph.cjs` 的几何段 + live-shell `§8.3.2 提交图几何随行高等比缩放`。**同项内的另外两半已分类、不属于待实施**：① 轨道**配色**由固定四色令牌给出（权威按 head／片段哈希派生，Augit 的日志没有 head／片段模型）⇒ **有意产品差异**，见 `ui-classification.md` §2.12a；② 图动作（长边截断、悬停箭头、片段折叠/展开）⇒ **不适用**（产品规格不含图动作），见 §2.12b。
8. **动作启用条件**：按分册 04，可用性走"可见性"而非"禁用"以满足"不显示无效动作"（**已落地并断言**，见 §2.3 与 `ui-classification.md` §1.7）。
9. ✅ **主题生效链路（C# 外壳侧）** —— **已实施并验证**，见 §8。修掉了"深色主题在真实应用里不可达"与"检测失败假定深色"两处缺陷。
10. ✅ **系统主题变化的即时跟随（C# 外壳侧 + 界面侧）** —— **已实施并验证**，见 §8.3。
11. ✅ **图标线宽、关闭叉几何与填充化** —— **已实施并验证**，见分册 [09-icons.md](nui-behavior/09-icons.md) §3：图标外壳线宽 1.5→1、关闭叉 1.2／7×7→1／9×9、勾形几何对齐（线宽 1.5 正确保留）；第二轮把窗口三键、关闭、加减号、竖排省略号改为权威的填充形式，并修正复选框（圆角 2.5px、勾形 2px、半选填充条、选中底色令牌化）；第三轮对齐四个 chevron 的折线几何（宽 3.5／高 7）并把提交节点改为权威的填充圆环（外 r=4／内 r=3）。第四轮把树展开箭头的颜色改为权威的固定色 `--augit-tree-arrow`（浅 `Gray7 #818594` / 深 `Gray10 #B4B8BF`），同时更正了两处我先前的错判（refresh 无需填充化；侧栏 folder 保持 `currentColor` 是正确的）。第五轮按测量重绘**分支图标**——权威是**三个实心圆盘**（半径 1.75）加 1.75 线宽的竖线与弧线，Augit 原先是两个空心圆环且缺第三个节点；并查明参考实现**有两套图标集**（expUI 1px／经典集 1.75px），New UI 缺 expUI 版时会回落经典集。第六轮按 `expUI/general/{up,down,left,right}.svg` 重画四个方向箭头（细杆 + V 形头；原先 `arrow-left/right` 缺杆、直接用了 chevron 形状），并区分了它**不是**经典集的实心三角 `arrowUp.svg`。**齿轮的填充形式按 `ui-classification.md` §1.10 归「已按 New UI 对齐」**（`design-system.md` §7.1 已写明多数功能图标按填充路径表达）；图标填充化清单里剩下的唯一一项是**工作区根文件夹角标** —— 它是 Augit 自有标记、权威 `nodes/folder.svg` 没有对应角标 ⇒ 归**不适用**（`ui-classification.md` §6），不是待办（分册 §4bis）。
12. ✅ **模态遮罩** —— **已实施并验证**，见 §11：参考实现不给对话框做背景变暗（代码与像素两条独立证据），`.scrim` 改为透明点击承接层，元素保留以维持"点击外部关闭"与两处断言。

## 5. 原「尚未收集」清单的处理结论（2026-10-01 第 310 轮订正）

本节此前只列现象、没有处理结论。4 项逐条给出结论与出处（分类口径见 `ui-classification.md` §0.1）：

| 原条目 | 结论 | 依据 |
| --- | --- | --- |
| 行内（词级）差异色与行底色的深浅关系 | **已收集并落地**（不再是未收集） | 分册 [08-diff-merge.md](nui-behavior/08-diff-merge.md) §7bis.4 第 223 轮定案：行内取 `DIFF_*.BACKGROUND` **全强度**（`getTextAttributes(type, editor, BackgroundType.DEFAULT)`），同一块的整行底因 `ignored = innerFragments != null` 取 `getIgnoredColor()` 的柔和值 ⇒ 深浅关系来自两条不同分支，不是 alpha 叠加；实现与断言见 `ui-classification.md` 2.10／3.5 |
| 三栏之间的冲突块连线/对应关系绘制、三栏分隔条的拖动与初始比例 | **已收集（宽度、step、初始比例）＋「拖动」不适用** | 分册 08 §7bis.1：`diff.divider.width = 24`、step = `max(scale(24) / 6, 2)` = 4；§7bis.2/§7bis.3 给出 gutter 标记与按侧高亮的安装规则；**初始比例**取权威 `ThreeDiffSplitter.resetProportions()` 的 `1/3 : 1/3 : 1/3`（`mockup.css` 的 `.conflict-columns` 注释引同一出处）。**拖动分隔条**属权威合并视图的交互，`ux-spec.md` §7.14 只要求"三栏比例…保持不变"、不提供拖动 ⇒ 归类**不适用**（Augit 不新增该交互） |
| 终端（xterm.js 侧）与 Augit 特有场景的 New UI 对应规则 | **无法取证** | 终端正文由 xterm.js 自绘，平台侧没有对应的绘制权威（`19-terminal.md`、`ui-classification.md` §5 第 2 项）；Augit 按 `ux-spec.md` §7.16 与实现维护，**不声称与 PyCharm 对齐** |
| CONTINUE／SKIP／ABORT 的文案 | **有意产品差异** | 参考实现走 `MERGE_ACTION_CAPTIONS` 钩子、没有硬编码来源，必须由 Augit 产品规格定义（`ux-spec`，见 `ui-classification.md` 3.6）；`MergeActionCaptions` 的启用条件机制照搬 |

## 6. 模块 1：主工具栏项目配色渐变（已实施）

### 6.1 实现方式

| 位置 | 内容 |
| --- | --- |
| `web/src/mockup.css` `:root` | `--augit-project-1..9`（浅色头部变体的 9 套 `MainToolbarGradientStart`）、`--augit-title-glow-center: 78px`（量测前兜底）、`--augit-title-glow: var(--augit-chrome)`（未设配色时不可见） |
| `web/src/mockup.css` 深色块 | 同 9 个令牌的深色取值 |
| `web/src/mockup.css` 标题栏一节 | `.titlebar` 的 `background-image` 用 `linear-gradient`；`.titlebar[data-project-color="1..9"]` 各自把 `--augit-title-glow` 设为 `color-mix(in srgb, var(--augit-project-N) 85%, var(--augit-chrome))` |
| `web/src/mockup.js` | `projectColorIndex()` 按工作区根路径稳定推导索引；`measureTitlebarGlow()` 量测项目部件图标中心并写入 `--augit-title-glow-center`；`titlebar()` 输出 `data-project-color`；在 `applyTypography()` 与 `resize` 时重新量测 |

三个刻意的实现要点：

1. **左端用 `max(calc(圆心 - 300px), 0px)` 夹紧。** 参考实现的左延伸是 `min(圆心, 300px)` 而非固定 300px。若直接写 `calc(圆心 - 300px)` 而圆心 < 300px，负的停止点会让**左边缘直接带上约 74% 的起始色**，与参考行为不符。
2. **配色按工作区根路径稳定推导，不引入"项目配色"新能力。** 参考实现把配色存在项目元数据里且允许人工修改；Augit 的产品规格没有这项能力，因此改为稳定推导，保证"同一工作区永远同一配色"。视觉机制与参考实现一致，分配策略是有意偏离。
3. **主题切换不需要 JS 重跑。** 9 套配色按主题声明为令牌，`color-mix` 随主题自动重算。

### 6.2 验证证据

在 Chromium 中加载 `docs/ux-mockups/main-project.html` 读取实际解析值：

| 项 | 浅色 | 深色 |
| --- | --- | --- |
| `data-project-color` | `4` | `4` |
| `--augit-chrome` | `#f7f8fa` | `#2b2d30` |
| `--augit-project-4` | `#cbe4f7` | `#335661` |
| `--augit-project-3` | `#e2f2c9` | `#45522f` |
| `--augit-title-glow-center` | `91px`（与独立量测的图标中心一致） | `91px` |
| 渐变解析值 | `linear-gradient(90deg, transparent 0px, color(srgb 0.818627 0.889216 0.927059) 91px, transparent 391px)` | 同结构，中心色见下 |
| 中心色换算 | `rgb(209,227,236)` = **`#D1E3EC`** | `rgb(48,76,88)` = **`#304C58`** |
| 与独立反推期望的比对 | 一致 | 一致 |

浅色的期望值 `#D1E3EC` 由参考截图反推独立得到（`0.85 × #CADFEA + 0.15 × #F7F8FA`，见数值参考 §3.1），与实现解析结果完全吻合。左端解析为 `0px`（`max(91-300, 0)`），右端 `391px = 91+300`，夹紧与半径均符合参考规则。

`--augit-project-3` 在深色下取 `#45522f`，这正是 `ManyIslandsDarcula.theme.json` 的对应主题值；浅色使用 `ManyIslandsLight.theme.json` 的 `RecentProject.Color{1..9}.MainToolbarGradientStart`，不再混用旧的人工混色值。

## 7. 模块 5：Diff 与文件状态配色（已实施）

取证见分册 [08-diff-merge.md](nui-behavior/08-diff-merge.md)，令牌逐项对照见该册 §8。要点：

- 新增 10 个令牌：`--augit-diff-added/-deleted/-modified/-whitespace/-separator`、`--augit-file-added/-modified/-deleted/-conflict`、`--augit-status-clean`，浅深各一套。
- 接线 `.diff-code-line.added/.removed/.changed` 与 `.file-status-added/.file-status-modified/.file-status-deleted`；其中 **`.file-status-added` 与 `.file-status-deleted` 此前没有颜色规则**（JS 各产出 6 处，靠继承），本次补齐。
- `--augit-green` / `--augit-red` / `--augit-orange` **未改动**：它们服务冲突解决器、内联告警与危险动作，与 Diff 色板不是同一套。
- `.file-status-new` 不是文件状态而是 Worktree 面板的正向状态文字，映射到 `NoConflicts.foreground` 的绿。

**一个关键的区分**（容易做错）：`ADDED_LINES_COLOR`（`#7FC784`）是 gutter 等实心部件用的"线条色"，与正文行底 `DIFF_INSERTED.BACKGROUND`（`#BEE6BE`）是**两个不同用途的值**。正文行底用的是配色方案里已经合成好的柔和底色，**不需要再叠 alpha**——我此前把"行底 alpha"当成缺口，实际是找错了层。

**第 223 轮订正**：整行底**分两档** —— 无行内差异的块取全强度 `DIFF_*.BACKGROUND`（浅 `.added` `#BEE6BE`／`.removed` `#D6D6D6`），有行内差异的 `Modified` 行取 `mix(DIFF_*.BACKGROUND, 编辑器底, .6)`（浅 `#E7EFFA`）；行内（词级）高亮另用全强度 `DIFF_*.BACKGROUND`。第 110–133 轮一度把浅色整行底改成 `#EDFCED`／`#F4F7F9`，复测证明那是**语言注入片段底色**与面板底（见 `nui-behavior/08-diff-merge.md` §2.2），已改回。

验证：浏览器实测浅色 `.added` = `#BEE6BE`、`.removed` = `#D6D6D6`、`.file-status-modified` = `#0033B3`；深色对应 `#294436`、`#484A4A`、`#70AEFF`，与权威值一致（第 223 轮由 `tools/audit/check-diff-inline.test.cjs` 与 `live-shell` 的 `src/Modified.cs` 场景覆盖）。资源字节一致 PASS，验收套件 `live-shell 通过 1136 项断言`（该数为第 177 轮的当前值）。

## 8. 模块 9：主题生效链路（已实施）

这一模块来自分册 [07-theme-dpi-dialogs.md](nui-behavior/07-theme-dpi-dialogs.md) 的取证：系统深色判据与失败处理。同时修掉了一个**真实用户可见缺陷**。

### 8.1 修掉的缺陷：深色主题在真实应用里不可达

三处取值的大小写契约不统一：

| 环节 | 取值 |
| --- | --- |
| 设置里的模式 | `System` / `Light` / `Dark`（`NormalizeTheme` 归一化为首字母大写） |
| 解析后的生效主题 | `Dark` / `Light` |
| 网页层判断的 URL 契约 | `theme=dark`（**小写**，`mockup.js` 用 `requestedTheme === "dark"` 判等） |

外壳把生效主题原样拼进 URL，于是设置为"深色"时下发 `theme=Dark`，**判等失败、界面仍是浅色**。全仓只有一处 `data-theme` 赋值，外壳也没有设置 WebView2 配色或注入其它深色触发，因此深色主题在真实应用里完全没有入口。

**为什么长期没被发现**：审计与视觉稿一律用命令行小写参数（`--theme dark`，以及套件里 223 处 `theme=dark`），走的是 `ResolveTheme` 中"显式主题原样返回"的分支，从未经过设置里的大写取值。这正是"测试只覆盖一种大小写"造成的盲区。

**修复**：外壳侧新增 `ShellTheme.QueryValue` 归一化为小写（`ShellWindow.BuildQuery` 使用）；网页层同时改为大小写不敏感（`.trim().toLowerCase()`），防止后续调用方重新引入。

### 8.2 修掉的第二处：检测失败不应假定深色

参考实现的 `isSystemThemeDark()` 返回**可空** `Boolean`，`AsyncDetector.check()` 遇到 `null` 直接 `return@launch`——即**检测失败就不切换**、按非深色取值。`WindowsDetector.isDark()` 捕获任何异常后 `return false`。

Augit 原先在 `SecurityException` 时 `return true`（假定深色），方向相反。现改为返回可空值 `null`，并由 `ShellTheme.FromSystemDark(null)` 落到 `Light`。注册表路径与取值（`...\Themes\Personalize` / `AppsUseLightTheme == 0`）与参考实现**本来就一致**，无需改动。

### 8.3 系统主题变化的即时跟随（已实施）

参考实现监听 `win.lightTheme.on` 属性变化并即时切换主题；Win32 侧对应 `WM_SETTINGCHANGE` 携带区域名 `ImmersiveColorSet`。现已按同一机制实现：

| 环节 | 实现 |
| --- | --- |
| 过滤 | `ShellWindow` 处理 `WM_SETTINGCHANGE`（`0x001A`），只接受 `lParam` 指向的宽字符串等于 `ImmersiveColorSet` 的消息。同一条消息也用于语言、无障碍等变化，不过滤会误触发重算（`ShellTheme.IsImmersiveColorSet`） |
| 是否跟随 | 只有主题模式为 `System` 时才跟随。`ShellOptions.ThemeMode` 单独保存模式——`Theme` 存的是**解析后**的生效主题，分辨不出它来自"跟随系统"还是用户显式选择（`ShellTheme.NextOnSystemChange`） |
| 无变化不推送 | 新主题与当前一致时返回 `null`，不向界面发无意义通知 |
| 检测失败 | 按非深色取值（与 §8.2 同一条规则） |
| 下发 | `Notify("theme/changed", { theme: "dark" \| "light" })`，仍走小写查询值契约（§8.1） |
| 界面侧 | `live-data.js` 订阅 `theme/changed`：切换 `body[data-theme]`（浅色时**移除属性**而不是写成 `light`），并调用 `refreshTerminalTypography()`——终端画在 canvas 上，CSS 规则管不到它。主题只换颜色，不改尺寸与布局，因此不触发重新布局 |

### 8.4 验证证据

| 验证 | 结果 |
| --- | --- |
| `dotnet test`（Augit.Shell.Tests） | **86/86 通过**，其中 `ShellThemeTests` 12 项（含区域名过滤、跟随/不跟随、无变化不推送、检测失败） |
| 浏览器功能验证（`main-project.html`，查询参数路径） | 无参数 → 浅色 `#f7f8fa`；`?theme=Dark` → **深色 `#2b2d30`**；`?theme=dark` → 深色；`?theme=DARK` → 深色；`?theme=Light` → 浅色 |
| 浏览器功能验证（`index.html`，真实桥接事件通道） | 初始浅色；推送 `theme/changed{theme:'dark'}` → `body[data-theme]='dark'`、chrome `#2b2d30`；推送 `'Light'` → 属性移除、chrome `#f7f8fa`；推送非法值 `purple` → 不变且无异常；未捕获异常 0 |
| 资源字节一致 | PASS |
| 验收套件 | 见 §2.3 同轮结果 |

`?theme=Dark` 一项是 §8.1 缺陷的直接回归证据：修复前该组合会落在浅色。

事件通道的验证需要真实 HTTP 服务：`index.html` 以 ES module 加载 `live-data.js`，而 `file://` 下 Chromium 会拦截模块导入，直接打开页面会得到"零事件处理器"的假象（视觉稿页面是普通脚本，没有这个问题）。

**常驻断言（第 310 轮已补齐）**：主题链路此前只靠 `dotnet test` + 两次功能验证脚本取证，`live-shell.spec.cjs` 里没有 `theme/changed` 的断言、也没有"大写 `Dark` 必须落深色"的断言（AGENTS.md 要求新增或修改可执行逻辑必须补自动化测试）。第 310 轮补上两条常驻断言：`§8.1 查询参数大小写不敏感：theme=Dark 落深色、theme=LIGHT 落浅色` 与 `§8.3 theme/changed 切换主题：大写 Dark 同样生效、Light 去掉属性、非法值忽略且无脚本错误`（后者依次推送 `dark` → `DARK` → `Light` → `purple`，断言深色生效、大写等价、浅色移除 `body[data-theme]` 且令牌回色、非法值不改变且无未捕获异常）。外壳侧的小写归一化由 `ShellThemeTests`（`ShellTheme.QueryValue("Dark") === "dark"`）常驻覆盖。

## 9. 模块 6：编辑器标签几何（已实施）

取证见分册 [03-editor-tabs.md](nui-behavior/03-editor-tabs.md) §1.4。本轮落地的是 **CSS 几何与选中表达**，取值全部有出处。

### 9.1 为什么这一模块能先做

几何模块普遍被"断言镜像实现公式"卡住，但标签模块与断言几乎无耦合：套件里与标签相关的几何断言只有一处 `rect('.editor-tabs')`，即标签**栏整体矩形**；当前栏高 40px、卡片高 28px、上下约 6px 留白已由离线图标/标签断言覆盖。单个标签的宽度、圆角、最小宽度仍需按中文文案和英文参考分别复核。

### 9.2 改动与实测

| 项 | 改前 | 改后（实测计算值） | 权威来源 |
| --- | --- | --- | --- |
| 标签最大宽度 | `max-width: 220px`／`230px`（两处重复） | `none` | 未固定标签无上限，只有固定标签有 2000px 上限 |
| 标签最小宽度 | 未设 | `50px` | `SingleRowLayoutStrategy.MIN_TAB_WIDTH` |
| 标签圆角 | `border-radius: 6px` | `0px` | expUI 经典渲染是平铺矩形；圆角只属 Islands 卡片 |
| 图文间距 | `gap: 6px`／`8px` | `4px` | `EditorTabbedContainer` 的 `iconTextGap = scale(4)` |
| 标签左右内距 | `padding: 0 9px`／`8px` | `8px` | `EditorTabs.tabInsets` 的左右分量 |
| 标签间关系 | `gap: 2px`／`3px`（间隙） | `margin-right: -1px`、`gap: 0` | `tabHGap = -tabBorder.thickness`，是重叠不是间隙 |
| 文字对齐 | 贴左 | `justify-content: center` | 被拉伸到最小宽度时文字居中 |
| 标签栏底色 | 未设（露出 chrome） | `#FFFFFF` | `EditorTabs.background` = Gray14，与编辑区同底 |
| 选中表达 | 底色方块（且用**硬编码** `#d5d9e0`／`#f0f2f5`） | 底部 4px 圆角下划线 `#3574F0` | `underlineHeight`/`underlineArc`/`*.underlineColor` = Blue4 |
| 未选中标签文字 | `color: var(--augit-muted)`（**变灰**） | `90% 前景 + 10% 底色`（浅）／`70% + 30%`（深） | 不是变灰：`editLabelForeground` 用 `blendColorsInRgb(底色, 前景, unselectedBlend)`，第三参是第二个颜色的权重 |
| 未选中标签图标 | 无 | `opacity: 0.75` | `getIconAlpha()` = `EditorTabs.unselectedAlpha`（两主题同为 0.75） |
| 标签悬停 | **完全没有 hover 规则** | 取消减弱（前景转满色、图标 alpha 转 1），**不改底色** | `paintDimmed()` 在 `isHoveredOrWithPopup` 时为假；`EditorTabs.hoverBackground = #FFFFFF00`（透明） |
| 标签栏边缘渐隐 | 无 | 滚动到边缘时两端各 10px 渐隐 | `ide.editor.tabs.fadeout.width = 10` |

选中表达由底色方块改为下划线，同时消掉了那两处硬编码色值——它们既不在 New UI 色板内，也违反 design-system §11"页面样式必须引用变量"。

一并做了一件结构性清理：`.editor-tabs` 原有**三处**同名块、`.editor-tab` 与 `.editor-tab.active` 各有**两处**。同优先级的后一处在 CSS 里静默覆盖前一处的全部同属性，我第一遍改的是**不起作用**的那一份。现已收敛为每个选择器各一处，并在注释里写明"新增样式必须写在本块内"。这是同类隐患在本项目中的第二次出现（第一次是颜色令牌块，见 §2.2）。

### 9.3 一处对既有文档的更正

分册 03 指出并已由我复核：`TabbedPane.tabHeight = 40` / `tabSelectionArc = 4` / `tabInsets = 0,12,0,12` 与 Islands 编辑器标签的卡片绘制规则需要分开理解；普通横向标签由 `IslandsTabPainter` 在 40px 栏内绘制 28px 卡片，上下约 6px 留白。Augit 当前已采用 40px 栏和 28px 卡片，中文标题宽度仍按实际字宽单独测量。

同时更正分册 03 对 Augit 现状的两处陈旧描述：它说 `.editor-tab` "没有省略号规则"，实际代码里有 `text-overflow: ellipsis`（且因父元素是 flex 容器而并不生效）；它引用的 `max-width: 230px` 与 `border-radius: 6px` 只是两处重复块中**后出现**的那一份，前面还有一份 `220px`／同样的半径。

### 9.4 剩余项的最终归类

| 项 | 最终归类 | 说明 |
| --- | --- | --- |
| ~~10px 边缘渐隐~~ | **已实施** | 见 §9.2。语义是滚动到部分移出可见区时的边缘渐隐，不是压缩时替代省略号 |
| 关闭叉显示时机（权威：选中 ∨ 悬停 ∨ 已修改 ∨ 已固定 + 驻留 150ms 才绘制） | **有意产品差异** | 产品规格 `ux-spec.md` §7.1 第 1 条要求"中键或关闭叉关闭后台普通文件标签" ⇒ 非选中标签的关闭叉必须可见可点；Augit 因此不采用权威的"仅特定状态出现 + 150ms 驻留"。修改它会连带使套件里多处 `click()` 非选中标签 `.tab-close` 的断言失效，属行为变更而非待办；归类见 `ui-classification.md` §1.25 |
| 固定标签 2000px 上限 | **不适用** | Augit 无固定标签能力（`ui-classification.md` §2.41 同族） |
| 标签条 5px 细滚动条（权威：鼠标在标签区内显示、离开后保留 2000ms） | **有意产品差异（实现方式）** | Augit 的标签条是浏览器原生 `overflow-x: auto` 容器并整体隐藏滚动条（§9.2／§9.5 实测），不新增自绘滚动部件；归类见 `ui-classification.md` §1.26 |

### 9.5 验证证据

浏览器实测（`docs/ux-mockups/main-project.html`）：标签栏底色 `rgb(255,255,255)`、`gap: 0px`、`overflow-x: auto`、`min-height: 40px`；标签 `min-width: 50px`、`max-width: none`、`margin-right: -1px`、`gap: 4px`、`padding: 8px/8px`、`border-radius: 0px`、`justify-content: center`、`height: 28px`；选中卡片上下约各 6px 留白。

**减弱混色的数值验证**（对照权威公式 `blendColorsInRgb(底色, 前景, blend)`）：

| 主题 | 实测未选中标签前景 | 手算期望 | 结论 |
| --- | --- | --- | --- |
| 浅色 | `color(srgb 0.1 0.1 0.1)` | 90% × `#000000` + 10% × `#FFFFFF` = 25.5/255 = 0.1 | 一致 |
| 深色 | `color(srgb 0.647451 0.654118 0.668627)` | 70% × `#DFE1E5` + 30% × `#1E1F22` = 165.1/255、166.8/255、170.5/255 = 0.647、0.654、0.669 | 一致 |

选中标签前景为 `rgb(0,0,0)`（满色）；未选中图标 `opacity: 0.75`、选中图标 `1`。

**渐隐的行为验证**（窄视口使标签条可滚动，`max = 170px`）：

| 滚动位置 | `--tab-fade-start` | `--tab-fade-end` |
| --- | --- | --- |
| 0（最左） | `0px` | `10px` |
| 60（中间） | `10px` | `10px` |
| 170（最右） | `10px` | `0px` |
| 回到 0 | `0px` | `10px` |

即"左端只在已滚动时渐隐、右端只在还有未显示内容时渐隐"，与参考实现的滚动渐隐语义一致；静止在两端时不产生渐隐。

资源字节一致 PASS；验收套件见 §2.3 同轮结果。

## 10. 模块 3：行悬停的有无与配色（已实施）

### 10.1 结论：悬停的"有无"是按**安装差异**决定的，不是统一的

一个容易被"顺手统一"的地方。参考实现里三类列表的悬停行为**并不一致**，差异来自监听器的安装位置：

| 容器 | 悬停监听 | 结果 |
| --- | --- | --- |
| `JBTable` | 构造时即 `TableHoverListener.DEFAULT.addTo(this)`（`JBTable.java:194`） | **一定有行悬停** |
| `JTree` | `TreeHoverListener` **不默认安装**；全仓只在协作工具的代码评审树显式 `addTo` | **默认没有行悬停** |
| `JBList` | `ListHoverListener` **也不默认安装**；只在活动列表、Switcher 等少数处显式 `addTo` | **默认没有行悬停** |

树的绘制路径要求 `row == TreeHoverListener.getHoveredRow(tree)`（`DefaultTreeUI.java:132`），而该属性在无人安装监听时恒为 −1，所以悬停分支**永不触发**。悬停底色三处共用同一个默认常量：

| 键 | 浅色 | 深色 |
| --- | --- | --- |
| `Table`/`List`/`Tree.hoverBackground` | `#EDF5FC` | `#464A4D` |
| 对应的 `hoverInactiveBackground` | `#F5F5F5` | `#464A4D` |

（`JBUI.java:2379-2380,2442,2490,2553`）

### 10.2 一次差点成立的错误结论

分册 02 结论是"树没有悬停背景"，我一度认为它**错了**：因为我发现 `setHoverPaintingDisabled`（悬停绘制的开关）在全仓**没有任何调用方**，而 `DefaultTreeUI` 确实调用 `RenderingUtil.getHoverBackground(tree)`。看起来悬停应该生效。

追下去才确认分册是对的，但**理由不同**：真正的闸门不是那个开关，而是"悬停行"属性。`RenderingUtil.getHoverBackground` 只负责给出颜色，决定"哪一行是悬停行"的是 `getHoveredRow`——没人安装监听，它就恒为 −1。我记住的教训是：**看到"开关未被关闭"不等于"功能会生效"，要找到真正的判定点。**

### 10.3 落地内容

| 位置 | 改动 | 状态 |
| --- | --- | --- |
| `web/src/mockup.css` `:root` / 深色块 | 新增 `--augit-row-hover`（`#EDF5FC` / `#464A4D`）与 `--augit-row-hover-inactive`（`#F5F5F5` / `#464A4D`） | 已落地 |
| `.commit-row:hover` / `.history-row:hover` | **新增**，取失焦档 | 已落地 |
| `.commit-list:focus .commit-row:hover` / `.history-rows:focus-within .history-row:hover` | 取获焦档 | 已落地 |
| `.tree-row:hover` | 按权威实现应**删除**（树无悬停） | **已裁决保留**：用户第 100 轮裁决为**有意产品差异**（`ui-classification.md` §1.15），不再按"待移除"处理 |
| `.changes-list .check-row:not(.selected):hover` | 按权威实现应**删除** | **已裁决保留**：同上（Changes 行悬停属该差异的登记范围） |

新增规则**排在选中态之前**：`.commit-row:hover` 与 `.commit-row.selected` 特异性相同，靠顺序让选中态覆盖悬停态——与参考实现"先判选中、再判悬停"的次序一致。

#### 阻塞原因（一次我自己造成的失败；该阻塞已由用户裁决解除）

我先前用几个 grep 模式判断"断言里零处涉及树行/改动行悬停"，据此移除了树与 Changes 的悬停。**套件随即失败**：

```
live-shell 失败：断言失败：真实悬停改变行背景: ["rgba(0, 0, 0, 0)","rgba(0, 0, 0, 0)"]
```

套件里确实有一条断言（`tools/audit/live-shell.spec.cjs:3644`）悬停 `.changes-list .change-file-row` 并要求底色发生变化。我的 grep 模式没覆盖它的措辞，于是得出了"零耦合"的错误结论。

已把两处移除**回退**，恢复套件通过；第 100 轮用户裁决把"保留树/Changes 行悬停"定为**有意产品差异**（`ui-classification.md` §1.15），因此不再计划移除。另一条相关断言（`.side-content.tree .tree-row` 的悬停，`:14419`）只把悬停当作**前置动作**，断言的是标题栏与状态栏逐像素不变，因此树行悬停的移除本身不影响它——受影响的只有 Changes 行。

**教训**：判断"断言是否覆盖某项行为"不能只靠几个关键词 grep。这里的正确做法是先把与主题相关的断言**读一遍**（或直接跑一次套件看哪条失败），而不是先改再验。这次代价是两轮往返。

### 10.4 验证证据

用**真实鼠标悬停**验证（早前一版探针靠遍历 `styleSheets` 判定，在 `file://` 下 Chromium 拒绝访问 `cssRules`，我的 try/catch 把全部样式表跳过、得到"零规则"的假象——那是探针缺陷，不是实现缺陷）：

| 场景 | 悬停前 | 悬停后 | 结论 |
| --- | --- | --- | --- |
| Git 历史提交行（表格，未聚焦） | `rgba(0,0,0,0)` | `rgb(245,245,245)` = `#F5F5F5` | 取失焦档 ✓ |
| Git 历史提交行（表格，已聚焦） | — | `rgb(237,245,252)` = `#EDF5FC` | 取获焦档 ✓ |
| 项目树行（树） | `rgba(0,0,0,0)` | 有变化 | 与权威实现不一致，**已裁决保留（有意产品差异**，§3 与 `ui-classification.md` §1.15**）** |
| Changes 行（树） | `rgba(0,0,0,0)` | 有变化 | 同上；Changes 行另被验收断言的悬停前置要求覆盖 |

资源字节一致 PASS；文档 §6.1/§6.2 与 `mockup.css` 比对为 18 行 0 处不一致；验收套件见 §2.3 同轮结果。

## 11. 模块 12：模态遮罩（已实施）

### 11.1 结论：参考实现**不给对话框做背景变暗**

Augit 原有一个 `.scrim`：`color-mix(in srgb, #EEF1F6 32%, transparent)` 底 + `backdrop-filter: saturate(75%)`（且那个 `#eef1f6` 是硬编码色，违反 §11"必须引用变量"）。design-system §8.5 还按"视觉稿最终级联结果"把它登记为规则，并注明 75% 饱和度"仍须单独验证"。

复核参考实现后确认**没有任何变暗**，两条独立证据：

| 证据 | 内容 |
| --- | --- |
| 代码 | `platform/openapi/wm/impl/IdeGlassPaneImpl.kt` 只注册 `namedPainters`、`windowShadowPainter` 与 `loadingIndicator`（`IdePaneLoadingLayer`），没有变暗绘制；全仓检索 `ModalityDimming` / `dimBackground` / `modalMask` 均无结果 |
| 像素 | 参考截图 `artifacts/pycharm-16-final/pycharm-branches-dialog.png`（2880×1800）在对话框之外的**六个区域**都是精确的 `#FFFFFF`（55–90%）与 `#E9EAEE`（8–22%）。若真有 32% 的 `#EEF1F6` 洗白，纯白会变成 `#FAFBFC` 一类值，不可能保持精确纯白 |

### 11.2 落地

`.scrim` 改为 `background: transparent`，去掉洗白与背景滤镜；**元素保留**——它承担「点击外部关闭」（`live-data.js` 在它上面挂 `click → closeLiveOverlay()`），且验收套件有两处断言要求它出现（`:6063` 的 `hasScrim`、`:6635-6639` 的 `scrim === true`）。因此这次改动**只动视觉、不动行为，断言不受影响**。

同时更新 design-system：§8.5 的遮罩取值条款改为"透明点击承接层、不做背景变暗"并写明证据；§4.3 的层级说明由 `Modal`（对话框和遮罩）改为注明承接层透明。

**顺带消掉一处硬编码色**：`#eef1f6` 随洗白一并移除。

### 11.3 这次判断里的一处自我纠正

我第一次用 `grep -c 'scrim'` 得到 **4**，却在自己的输出里把标签写成"（空/0=无耦合）"——**标签与数字矛盾**。幸好数字是对的，我按数字去读了那 4 处断言，才发现它们要求元素存在（而不是视觉），从而确认"只删视觉"是安全的。如果当时按标签误判为"零耦合"，就会把一个被断言要求的元素一起删掉。**读工具输出要看数字本身，不要看自己顺手写的标签。**

### 11.4 验证证据

| 验证 | 结果 |
| --- | --- |
| 验收套件 | 见 §2.3 同轮结果（`.scrim` 元素仍在，仅视觉改为透明） |
| 资源字节一致 | PASS |
| 参考实现交叉验证 | 代码与像素两条独立证据一致 |

### 11.5 结论与边界

参考实现里 `Modal` 的其它视觉处理（例如窗口级柔和阴影）**未逐一核对，归无法取证**：Win11 原生紧凑模态的外部阴影由系统合成，与遮罩无关；本项不改变任何当前实现，也不构成待办。
