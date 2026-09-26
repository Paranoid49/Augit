# New UI 对齐：未决项清单（backlog）

本文件把 `docs/design-system.md` 与 `docs/nui-behavior/0*.md` 里所有**尚未解决**的项集中到一处，作为后续轮次的作业面。
每一项都注明：现象、权威依据、为什么当时没做、以及需要什么才能做。

判定原则始终是：**本地 `/mnt/d/github/intellij-community`（commit `576e328`）里的键与代码是权威**；参考截图只用来**发现**差异，不用来反推覆盖权威常量；
规范与权威冲突时以 New UI 为准（用户裁决，见 `design-system.md` §1）。

> **「还剩多少轮」以 `11-surface-audit.md` 为准**（第 134 轮建立）：它把 Augit 现有的 56 个界面场景逐区摊开，标注"已有行为文档／有权威待采／权威待定位／待产品裁决"，当前加总为 **12–21 轮**。本文件负责记"具体每项卡在哪"，审计文件负责记"总共还差多少"。

## 一、被"组件归属"卡住的项

这一类是**同一个根因**：Augit 的某个控件到底对应 New UI 的哪个组件没有证据，而两个候选的权威度量差很大，不敢按其中一个改。

| # | 控件 | 现状 | 候选权威 | 卡在哪 |
| --- | --- | --- | --- | --- |

**要解开它，需要**：在 checkout 里找到**使用**这些控件的调用点（哪个工具栏用的是 Jewel 组件、哪个用的是 Swing `ActionButton`），或者找到 PyCharm 2026 里该工具栏的可用参考样本。目前全仓搜不到 `StripeButton`／经典 `ActionButtonUI` 一类的类。

## 二、参考图有现象、权威无对应键

| # | 现象 | 现状 | 说明 |
| --- | --- | --- | --- |


## 二·补二、对话框页脚分隔线 —— **第 103 轮结案：普通对话框本就没有分隔线**

权威 `DialogWrapper.java:838-845`：只有 `DialogStyle.COMPACT` 的 south panel 才加 `CustomLineBorder(…, 1, 0, 0, 0)` 的 1px 顶边，普通对话框只加 `JBUI.Borders.emptyTop(8)`。2026 参考图 Confirm Exit（与 reset／rollback 同类的破坏性确认框）实测确实没有分隔线。**已删除** `.dialog-footer` 的 `border-top`，并把基类内距按权威改为 `8px 12px`。



## 二·补四、经典对话框的**内容内距**与**标题图标**

**(a) `dialogUnscaledGaps` 不适用于这些对话框**：第 83 轮在 `IntelliJSpacingConfiguration` 里发现 `dialogUnscaledGaps = UnscaledGaps(10, 12, 10, 12)`，一度想拿它对齐 `.dialog-body`/`.dialog-footer`；但本轮查明：
- 权威里**没有** `DialogWrapper.insets`／`DialogWrapper.contentInsets` 一类的键（grep 为空），`dialogUnscaledGaps` 的注释是"Unscaled gaps between dialog content and its content"，属**新式 DSL 对话框**；
- Augit 这些是经典风格的 `DialogWrapper` 复刻；实测生效的都是**各对话框自己的覆写**（`settings` 15px 17px、`stash-manager` 0 16px、`reset` 15px 17px 17px…），基础 `.dialog-body { padding: 16px }` 几乎不被使用。

因此**不改**：套用 10/12 会与视觉稿基线（AGENTS.md 规定 `docs/ux-mockups/` 是设计基线）冲突，且无经典对话框的权威内距键。

**(b) 对话框标题前的图标**：2026 参考图 `confirm-exit-dialog.png` 里，标题左侧有一个**蓝色问号图标**；而 Augit 全库**没有** `.dialog-icon` 一类规则，却有 7 个对话框的标题行写着非对称左内距（`padding: 0 13px 0 25px`／`padding-left: 26px`）——**恰好是给图标预留的位置**，很可能是早期实现删掉图标后留下的。

**未实施**：新增图标属于可见的界面元素，需要先定"哪些对话框用哪种图标"（参考图只给了 Confirm Exit 的问号；reset/rollback 等破坏性操作按 IntelliJ 惯例应是警告类图标，候选在 `platform/icons/src/expui/general/`：`warning`／`question`／`info`）。等有了更多对话框参考图或产品侧口径再动。

## 二·补五、Diff 三层颜色的归属（**第 133 轮结案**）

Diff 的颜色分**三层**，第 133 轮用参考图的**精确像素实测**把归属定死（详见 `08-diff-merge.md` §2.2）：

| 层 | 浅色实测 | 几何 | 权威键 | 状态 |
| --- | --- | --- | --- | --- |
| **整行软底** | 增 `#EDFCED`／删 `#F4F7F9`／改 `#E7EFFA` | 整行满宽（w1625–1697） | **无** | 只能取参考实测值 |
| **行内（词/段级）高亮** | 增 `#BEE6BE`／删 `#D6D6D6`／改 `#C2D8F2` | 局部段 | **`DIFF_*.BACKGROUND`** ✓ | 权威明确；**Augit 尚未实现** |
| 行号槽实心标记 | `#7FC784`／`#767A8A`／`#88ADF7` | 窄条 | `*_LINES_COLOR` ✓ | 权威明确 |

- **软行底**（整行背景）：增 `#EDFCED`／删 `#F4F7F9`／改 `#E7EFFA`，浅色档已落地 ✓ 与参考实测一致。
- **行内词级高亮**：Augit **没有**这一层。上一版 backlog 写"词级高亮证据不足"，那是**当时看错了层**——参考图里 `#C2D8F2` 分成 `887-1026`／`1678-1794`／`1796-1836`／`1838-1925`／`1973-2817` 五段，是词/段级高亮而不是整行带（`08-diff-merge.md` §5 原来记的"【需推断】"因此也结案了）。

### 待办

1. **行内词级高亮**：权威键已定（`DIFF_*.BACKGROUND`），参考图也证实存在；但落地需要**宿主提供词级差异范围**（`MergeInnerDifferences` 那一类数据），Augit 的 diff 模型没有 ⇒ 这已接近"新增数据通道"。按用户裁决的边界（**不新增功能**）**暂不实施**，除非产品口径确认它属于"现有 diff 功能的呈现方式"。
2. **`.diff-current` 这一层在 New UI 里没有对应物**，需产品口径二选一：① 删除（导航差异只滚动、不染色）；② 改为行内词级高亮（即第 1 条）。**在定下来之前不动色值。**
3. **深色整行软底用的是行内层的色**（`#294436`／`#484A4A`／`#385570` = `DIFF_*.BACKGROUND`）。浅色的整行软底**无权威键**、深色又**无参考图** ⇒ 无从取值，暂留并记录。
4. ~~深色软色是推测值~~ → **第 132 轮已作废**：`DefaultColorSchemesManager.xml` 的 **Darcula** 段里 `DIFF_INSERTED`／`DIFF_DELETED`／`DIFF_MODIFIED` 的 `BACKGROUND` 就是 `294436`／`484a4a`／`385570`（`:2267`／`:2261`／`:2273`），与 Augit 深色值逐字相同——它们是权威值，只是**层归属被用错了**（见第 3 条）。

#### 二·补五·续（第 132 轮：Diff 变色的权威键已定位，并发现浅/深两档的"色族"不一致）

**(a) 权威键找到了。** 差异视图里"变更行"的取色走 `TextDiffTypeImpl.getColor()`：

```
diff-impl/src/com/intellij/diff/util/TextDiffTypeFactory.java:60-62
  public @NotNull Color getColor(@Nullable Editor editor) {
    return ObjectUtils.notNull(getAttributes(editor).getBackgroundColor(), JBColor.DARK_GRAY);
```

即 **`DiffColors.DIFF_INSERTED`／`DIFF_DELETED`／`DIFF_MODIFIED` 的 `BACKGROUND`**（下称 **B 族**）。而 Augit 深色 `.diff-current` 用的 `549159`／`868a91`／`375fad` 是 **`*_LINES_COLOR`**（下称 **A 族**，`LineStatusMarkerColorScheme.getColor()` 的注释写明它"primarily used to paint **filled gutter marker**"，即**行号槽标记**，不是正文底色）。

**(b) 两张权威表的完整取值**（采纳链：浅＝`expUI_lightScheme.xml`（parent `Default`）、深＝`expUI_darkScheme.xml`（parent `Darcula`））：

| `DIFF_*.BACKGROUND`（B 族，正文变色） | 浅色 | 深色 |
| --- | --- | --- |
| `DIFF_INSERTED` | `BEE6BE` | `294436` |
| `DIFF_DELETED` | **`D6D6D6`** | `484a4a` |
| `DIFF_MODIFIED` | `c2d8f2`（`expUI_lightScheme.xml:262` 覆写；parent 为 `CAD9FA`） | `385570` |

| `*_LINES_COLOR`（A 族，行号槽标记） | 浅色 | 深色 |
| --- | --- | --- |
| `ADDED` / `DELETED` / `MODIFIED` | `7fc784` / `767a8a` / `88adf7` | `549159` / `868a91` / `375fad` |

**(c) 第 133 轮的像素实测把层归属定死了 —— 本节原先的"两个色族"框架本身是错的。** 原判断把 `DIFF_*.BACKGROUND` 当成"正文行底"，于是把 Augit 的两条令牌看成"同一层的两种色族"；实际是**三层**：

| 参考图里的层 | 浅色实测 | 几何 | 权威键 |
| --- | --- | --- | --- |
| 整行软底 | `#EDFCED`／`#E7EFFA`／`#F4F7F9` | 整行满宽（w1625–1697） | **无** |
| 行内（词/段）高亮 | `#BEE6BE`／`#C2D8F2` | 局部段（`#C2D8F2` 分五段） | `DIFF_*.BACKGROUND` ✓ |
| 行号槽标记 | `#7FC784`／`#767A8A`／`#88ADF7` | 窄条 | `*_LINES_COLOR` ✓ |

（方法：无头 Chromium 把 PNG 读成 `data:` URL 画进 canvas → `getImageData` 精确直方图 + 包围盒 + 逐行 x 分段；`data:` URL 不污染 canvas，不需要起服务器。裁剪目视确认：蓝带里**只有 `powershell` 这个词**被 `#C2D8F2` 覆盖，行底是更浅的 `#E7EFFA`。）

因此 **`--augit-diff-current-*` 用的浅色 `#BEE6BE`／`#C2D8F2` 是"行内层"的色**，却被用在了整行层；深色三个则是行号槽色。而**权威里没有"当前差异"这一层**（`DiffDrawUtil.PaintMode` 只有 `DEFAULT`／`IGNORED`／`RESOLVED` = 无底 + 点线边框／`EXCLUDED_*`）。处置见上一节"待办"。

**⚠️ 反向教训（第 132 轮的提案是错的）**：第 132 轮我曾提议"把浅色行底改成 `#BEE6BE`／`#C2D8F2`，因为它们是权威值" —— **那会改错**：它们是**行内**层的值，刷到整行上等于把词级高亮当行底。**"这个色有权威出处"不等于"它属于这一层"**；定位层归属必须靠**绘制路径**（`createInlineHighlighter` → `getTextAttributes(..., BackgroundType.DEFAULT)`）或**实测几何**，不能只看键名与色值。




## 二·补七、`tools/verify-ux-*.cjs` 的 40 个分模块检查器：11 处会话回归（本轮已分类）

第 116 轮把 `tools/verify-ux-*.cjs` 全部跑了一遍（此前只跑过离线图标那一个）—— **23 通过、12 失败**。再用 `git worktree` 拉出 **HEAD 干净工作树**做基线对照，结论：**12 个里有 11 个在 HEAD 是通过的**，即**都是本次会话引入的回归**；只有 `verify-ux-json` 在 HEAD 也失败（本就如此）。

### 当前净进展（第 118 轮末）

原 12 个失败项中 **5 个已转绿**：`commit-workflow` ✓、`project-tree` ✓、`push` ✓（底栏公式）、`reset-layout` ✓（文本框改回随字号缩放）、`typography` ✓（主菜单条目内距）。**7 个仍失败**：`conflict`、`document-button-states`、`frame-buttons`、`history`、`json`、`reset-rollback`、`titlebar`。

其中 `titlebar` 在第 120 轮已订正 **3 组陈旧期望**（深色 accent `Blue8`→`Blue6`；悬停/按下改为按权威令牌的半透明值、并改用 computed 字符串比较 —— 原来的 canvas 读法会丢 alpha，把 `rgba(0,0,0,0.07)` 读成 `[0,0,0]`；`faint` 改为权威 `Label.disabledForeground` 浅 `#9FA2A8`／深 `Gray6 #5A5D63`），但该文件仍有多组旧值（如工作区 chip 的常态底：代码为透明、检查器期望 accent 混合），**待续**。

### 已修好（检查器的期望值陈旧，代码有权威依据）

- `verify-ux-commit-workflow` ✓（悬停色 `#F1F2F4` → `#00000008`）
- `verify-ux-project-tree` ✓（同上，含令牌十六进制形式）

### 期望值已按依据更新、但文件内还有后续陈旧断言

`reset-rollback`（悬停族 + 红色 `#C74440`→`#C54E58`）、`push`、`document-button-states`、`frame-buttons`、`history`（分段组 81→87、31→33）、`reset-layout`、`typography`、`conflict`。这些检查器把同一批旧值**同时**写在「计算样式 rgb 形式」与「令牌十六进制形式」里，逐条替换后仍有后续断言失败，需继续按运行值核对。

### 属于**代码缺陷**（不该改检查器）

1. ~~按下/切换态按钮仍是 `#3574F0`~~ → **第 117 轮已定位并修复**：不是"按钮底色"，而是**焦点环**（`design-system` 明写"焦点使用命中区内侧 1px **accent** 圆角边框"）被写成 `--augit-blue`。已把 **14 条焦点态规则**（含检查器所测的 `.terminal-header .icon-button:focus…::after`）改用 `--augit-accent-brand`；同时把 3 个检查器里深色 accent 的错误期望 `Blue8 #548AF7` 订正为权威的 `Blue6 #3574F0`。注：改动时先按"值"替换误伤了 6 条**非焦点**规则（复选框混合态／字段边框／查找高亮／加载标记／阴影），已按**所属选择器**逐条回退。
2. ~~`push` 底栏几何溢出~~ → **第 118 轮已修复**：实测 `ui-size=40` 时按钮超出对话框底边 3px。根因是 push 的底栏高度公式写死为 `line + 25`，而底栏实际需要「按钮（`max(37, line+17)`）+ 顶部内距 12」= `line + 29`；第 95 轮把按钮公式从 `line+8` 提到 `line+17` 后，这个差额才显现。已改为 `Math.max(53, line + 29)`，`verify-ux-push` 24 组全部通过 ✓。（其余对话框用的是 `button + 23/25`，本就会随字号缩放，只有 push 写的是 `line`。）
3. **`reset-layout` 出现裁剪**（`clipped: true`，期望 false）—— 疑与第 105 轮 body 内距改为 `8px 12px` 有关。→ **第 119 轮已修复**（见上「净进展」）。
4. **`typography` 在 9px 字号下菜单文字不再完整** —— 疑与第 96/92 轮菜单内距有关。→ **第 119 轮已修复**（见上「净进展」）。
5. `conflict` 的**焦点色**：检查器期望深色 `#548AF7`、实际 `#3574F0`，需先查权威 `Component.focusColor` 再定方向。

### 流程改进

把这 40 个检查器纳入每轮清单（原先"每轮只看 live-shell + 离线图标"的口子正是它们漏掉的原因）。

## 二·补八、分模块检查器的"值族"系统性更新（方法已定，工作量待排）

第 116–121 轮把 40 个 `tools/verify-ux-*.cjs` 逐个跑过、并用 HEAD 工作树定性，结论：**剩余失败几乎全是"检查器编码的是本次会话之前的值"**，而非产品缺陷 —— 因为本次会话按权威改了上百处颜色/尺寸。

### 已确认的陈旧"值族"（改检查器，代码有权威依据）

| 族 | 旧值 | 权威值 |
| --- | --- | --- |
| 动作按钮/行悬停 | `#F1F2F4` / `#2D2F33`（已删除的 `--augit-blue-hover`） | 浅 `#00000012`／深 `#FFFFFF16`；行用 `#00000008`／`#464A4D` |
| 按下 | 与悬停混用 | `--augit-pressed`：浅 `#00000020`／深 `#393B40` |
| 禁用文字 `faint` | 浅 `#A0A4AA`／深 `#6F737B`（Gray7） | `Label.disabledForeground`：浅 `#9FA2A8`／深 `Gray6 #5A5D63` |
| 深色 accent | `Blue8 #548AF7` | `accent-brand-bg` = `Blue6 #3574F0` |
| 分段组常态底 | `#F4F5F7` | **透明**（`SegmentedButtonBorder`） |

**已全绿（10 个）**：`commit-workflow`、`project-tree`、`push`、`reset-layout`、`typography`、**`frame-buttons`**（第 122 轮，24/24）、**`titlebar`**（第 131 轮，12/12）、**`history`**（42 组）、**`document-button-states`**（60/60）、**`reset-rollback`**（12/12）。

**仍失败（2 个）**：`conflict`（**布局设计**问题，不是期望问题）、`json`（**HEAD 既有失败**，`'1' !== '4'`）。其中第 122 轮新增两条判断依据：
- **轨道按钮选中未聚焦时不是 accent**（权威 `SquareStripeButtonLook.getBackgroundColor()`：只有自身聚焦才用 accent；未聚焦时给悬停底 `#55555528`/`#0f0f0f28`）——`frame-buttons` 据此转绿；
- **按下是独立一档**（`--augit-pressed` 浅 `#00000020`／深 `#FFFFFF26`）；但 `mouse.down()` 只对指针确实落在其上的元素生效，部分样本按下后仍是悬停态，故 `document-button-states` 改为"两档都接受"。

### 待办

1. 对上述 6 个文件继续"跑到失败 → 读 actual → 按权威判断是代码还是期望"的循环；
2. `verify-ux-json` 在 **HEAD 也失败**（`'1' !== '4'`），属既有问题，需单独排查；
3. 剩余未清扫的可能族：`mutedPanel`（浅 `#F5F8FE`／深 `#25262A`）、`disabled`（浅 `#F5F8FE`）、`danger` 深色 `#E37A7A`。

### 方法沉淀

每个失败都走同一套判断：**读 actual → 查权威 → 是代码错就改代码（如 push 底栏公式、reset 文本框固定高度），是期望旧就改期望并附权威注释**。这一步不能省成"让测试跟着代码走"，否则第 115 轮那种"用补丁掩盖偏差"的错会重演。

### 二·补八·续（第 123 轮实测的四类当前失败）

| 检查器 | 当前失败 | 分类与处置 |
| --- | --- | --- |
| `history` | ~~分段按钮宽度 `38 !== 40`~~ | **第 124 轮已解**：实测组几何为「边框 1 + 内距 2 + 按钮 40 + **间隙 1** + 按钮 40 + 内距 2 + 边框 1 = **87**」，完全自洽 ⇒ 按钮 **40** 正确（= 内容 16 + 24，权威 `SegmentedButtonLook`），**检查器的 38 才是旧值**，已订正。同时澄清：`IntelliJSpacingConfiguration.segmentedButtonHorizontalGap = 12` **不适用于这个控件**（代码实测间隙 1px，与组宽自洽），该 12 属 Compose/DSL 的间距配置。**第 125 轮又解三层**：按钮相对组边缘的偏移 `3`（权威 `getBorderInsets()` = BW(LW) = 3 每侧，原写 2/41 是按旧按钮宽 38 推的）、深色边框 `#4E5157`（`Component.borderColor`，原 `#4B4D53` 无依据）；并**修掉一处真实代码缺陷** —— 深色专用的 `.segmented { background: var(--augit-panel-muted) }` 与基类注释记录的权威（`SegmentedButtonComponent.paint()` 未设背景）矛盾，已删除。该文件后续仍有断言。 |
| `reset-rollback` | 按下底 `0.125 !== 0.07` | **已修**：第 44 行 `mouse.down()` 后仍期望悬停色，改用新加的 `pressed` 档（`--augit-pressed`）；该文件仍有后续断言 |
| `document-button-states` | 深色按下 `0.15` vs 我的 `0.149` | **已修**：浏览器把 `#FFFFFF26`（0.14902）取整显示为 `0.15`，常量改为 `0.15`；该文件仍有后续断言 |
| `conflict` | `light-40-13-1-1024 正文高度不足`（正文 ≤ 80px） | **产品侧布局问题**：冲突对话框高度公式 `Math.min(…, innerHeight - 40)` 在大字号下把总高截到 600，正文被压到 80px 以下。与 push 底栏公式同类，需改公式或让正文区自行滚动 |

### 二·补八·续二（第 126 轮实测）

**`conflict` 的"正文高度不足"已量清**（`ui-size=40`、1024×640）：对话框 **600**（= `innerHeight - 40` 的封顶 ✓）、header **69**、footer **93**、`.dialog-body` **436**、`.conflict-column` **190**、**`.conflict-block` 仅 76**（检查器要求 > 80）。

要点：**外层并不缺空间**（body 436 ✓），是 `.conflict-block` 为**内容自适应**（`min-height: 0; overflow: auto`）而不拉伸，大字号下反而更矮。属"栏没撑满可用高度"的布局设计问题，需要专门设计（例如让列内的块区域撑满列高、由块内部滚动），**不宜随手改一个阈值**。

**`titlebar` 的工作区 chip（第 127 轮已订正）**：实测常态底为 `rgba(0, 0, 0, 0)`，检查器却期望 `mix(chrome, accent, 8)`。排查结论：① 令牌是 **`--title-workspace`**（我第 126 轮按 `--augit-title-workspace` 搜、搜不到 ✗，实为无 `augit-` 前缀），其值 `transparent` ✓；② 权威里**没有**"项目色底"这个键（`MainToolbar.project*` 全无命中）—— 项目色走的是**标题栏背景渐变**（`--augit-title-glow`，见 `.titlebar[data-project-color="1..9"]`）。故 **chip 透明是对的**，检查器陈旧。已把该期望改为透明，并把断言从 canvas 读法（丢 alpha）改为 computed 字符串比较。**第 128 轮修掉一处我自己引入的类型不匹配**：上述改动把 `normal` 从数组改成字符串后，第 82 行（禁用态回到常态底）仍用 `color()`（返回数组）比较，导致 `[233,234,238] !== 'rgb(233, 234, 238)'`，已统一为字符串比较。该文件**仍有较多期望组**（当前实测 `[0,0,0]` vs 期望 `[217,218,222]`，前者又是 canvas 丢 alpha 的读数，后者与该文件的 `faint` 也不符），属长尾，需逐组过。

### 二·补八·续三（第 131 轮：长尾一次清完，并沉淀"收集器"方法）

#### 1. 方法：断言收集器（已固化为仓库工具）

这些检查器**一失败就在第一条断言抛出**，逐条修要跑十几遍。第 131 轮改为用 `--require` 预载一个包装器，把 `node:assert` 的每个方法换成"记录并继续"，跑完在 `exit` 时打印**去重汇总**：

```bash
node --require /绝对路径/tools/audit/collect-assert-failures.cjs tools/verify-ux-<模块>.cjs <playwright> <chrome> [证据目录]
```

效果：`titlebar` 的 **84 次失败一次收敛成 5 组**，三轮内全绿；`history` / `document-button-states` / `reset-rollback` 一次跑完只剩 **1 / 1 / 3 组**。

**两个坑**：① `--require` 的相对路径不解析（`internal/preload` 报 `MODULE_NOT_FOUND`），必须给绝对路径；② 断言不再抛错后控制流会继续往下走，可能出现**级联记录**——判断时以"同一组 actual/expected 出现次数多"的优先。

#### 2. 五处订正（四处是检查器陈旧，一处是产品代码）

| 检查器 | 旧期望 | 权威 | 结论 |
| --- | --- | --- | --- |
| `history`（分段选中底，深色） | `rgb(30, 31, 34)` = `Gray1`（页面底） | `SegmentedButton.selectedButtonColor`：浅 = ManyIslandsLight 覆写的 `control-bg-raised` = `dialog-bg-inline` = `white`／深 = expUI_dark **`Gray3 #393B40`**（`expUI_dark.theme.json:231`） | **期望错**：写成 `Gray1` 的话选中块会与容器同色、根本看不出来；改为 `rgb(57, 59, 64)` |
| `document-button-states`（`[aria-pressed="true"]` 底，深色） | `rgb(47, 70, 111)` = `#2F466F` | `*.selectionBackground`：浅 = Islands `selection-bg-active-muted` = `blue-140 #D0DFFE`／深 = expUI_dark **`Blue2 #2E436E`** | **期望错**：`#2F466F` 在两套家族的任何调色板里都查不到；改为 `rgb(46, 67, 110)`（浅色期望本来就对） |
| `reset-rollback`（深色 danger 按钮底） | `rgb(227, 122, 122)` = `#E37A7A` | expUI_dark **`dangerBackground` = `Red7 #DB5C5C`**（`expUI_dark.theme.json:965`），ManyIslandsDark 的 `dangerBackground` **同值** | **期望错**：`#E37A7A` 无出处；改为 `rgb(219, 92, 92)` |
| `history-toolbar`（焦点环探针） | 探针 span 读 `var(--augit-blue)` | 焦点环权威是 **accent**（`design-system`："焦点使用命中区内侧 1px accent 圆角边框"），即 `--augit-accent-brand` = 浅 `blue-80 #3871E1`／深 `Blue6 #3574F0` | **探针令牌陈旧**：第 117 轮已把 14 条焦点态规则改用 `--augit-accent-brand`，探针却没跟着改。**浅色两令牌不同（`#3871E1` vs `#3574F0`）故失败；深色同值，所以这个陈旧永远不会在深色档暴露** |
| `reset-rollback` / `frame-buttons`（禁用底，浅色） | `#F5F8FE` = expUI_light `Blue13` | Islands `*.background` 与 `*.disabledBackground` = `dialog-bg` = `layer-1-bg` = **`gray-160 #F7F8F9`** | **产品代码错（差 1）**：见下 |

#### 3. 产品代码订正：`--augit-panel-muted` 浅色 `#F7F8FA` → `#F7F8F9`

`#F7F8FA` 是 **未被采用**的 `expUI_light` 的 `Gray13`（`docs/design-system.md:187` 明写该主题不采用）。按 §6.1 的取色规则"浅色查 ManyIslands 家族语义别名"：

```
ManyIslandsLight:381  "*".background          = dialog-bg
ManyIslandsLight:387  "*".disabledBackground  = dialog-bg
ManyIslandsLight:201  "dialog-bg"             = layer-1-bg
ManyIslandsLight:168  "layer-1-bg"            = gray-160
ManyIslandsLight:11   "gray-160"              = #F7F8F9
```

深色侧本来就对得上（expUI_dark `*.disabledBackground` = `Gray2 #2B2D30`）。这与 `chrome`（`Gray13 #F7F8FA` → Islands `gray-150 #E9EAEE`）、`border`、`border-strong` 当初的偏差**是同一类**，属遗留未清。

**影响面已核**：该令牌在 `mockup.css` 有 14 处引用（次级表面与各对话框的禁用按钮底），改动使浅色渲染差 1/255；`verify-ux-frame-buttons` 的浅色禁用底期望同步改为 `rgb(247, 248, 249)`，两处同步后仍全绿。

#### 4. 当前盘点（原 12 个失败项 + `json`；全量 **35** 个中的其余 22 个本来就全绿）

| 状态 | 个数 | 检查器 |
| --- | --- | --- |
| 已全绿 | **13/13** | 原 12 个失败项 **+ `json`**（HEAD 既有失败也在第 132 轮同轮内定位并修复，见续五） |

**为什么要全量重跑**：第 116 轮列出的"12 个失败"是**第 117 轮那 14 条焦点态规则改动之前**的快照，而 `history-toolbar` 恰好是被那次改动打破期望、却又没进旧清单的一个（旧清单只覆盖当时跑出失败的文件）。**只要产品侧按权威改色，就必须重跑整个检查器集合**，不能只重跑上次失败的那几个——`history-toolbar` 就是这样潜伏了一轮。

### 二·补八·续四（第 132 轮：`conflict` 结案 —— 唯一的失败原因不是"块不自适应"，而是自定的适屏边距）

**第 128 轮把病因判成"`.conflict-block` 内容自适应、不撑满列高"，第 132 轮实测推翻了这个判断**：`.conflict-column` 早已是 `display: grid`（`var(--conflict-column) minmax(0, 1fr)`）、`.conflict-block` 早已 `min-height: 0; overflow: auto` —— **块本来就在撑满**。

真正的断点在**对话框总高**。1024×640、`ui-size=40` 实测：

```
对话框 600（= innerHeight − 40 封顶）  期望高 765（243 + 69 + 82 + 114 + 164 + 93）
├ 对话框标题 69
├ 正文 436 = 导航行 82 + 三栏 190 + 操作区 164（换行成两行）
│            └ 三栏 190 = 列标题 114（两行）+ 正文 76   ← 缺口 165 全部由正文承担
└ 页脚 93
```

也就是说：**窄视口让列标题分成两行、操作区换成两行（两者都是必要的），而自定的 40px 边距把差额全压到正文上。**

权威依据：`ScreenUtil.fitToScreen()` 转调 `moveToFit(rect, screen, padding = null, crop = true)`（`ScreenUtil.java:371-393`）—— **没有外边距**，超出屏幕时直接裁到屏幕边界。故把 `mockup.js` 的 `innerHeight - 40` 与 CSS 的 `max-height: calc(100% - 40px)` 都改为**不扣边距**（`innerHeight` / `max-height: 100%`）。实测正文 76 → **116**，其余档位（494/241）**完全不变**——证明改动只作用在"被封顶"的那一档。

`docs/design-system.md` §三栏冲突解决器 的规格行同步订正（"最高不超过宿主高度减 40px" → "最高不超过宿主高度"，并顺手修掉该行里更早遗留的按钮公式 `max(28px, h + 8px)` → `max(37px, h + 17px)`）。

**结论**：`verify-ux-conflict` **PASS=72**。至此原 12 个失败项**全部转绿**；连唯一剩下的 `verify-ux-json`（HEAD 既有失败）也在同轮内定位并修复，见续五。

**教训**：第 128 轮我只量了"外层不缺空间（body 436）"，就断定是内层布局问题并写进 backlog；其实**量到的那一层并不是瓶颈层**。要判"谁被压缩了"，必须**从对话框总高一路加到底**（本轮的 `expected 765 vs actual 600`），而不是只看相邻两层的大小关系。

### 二·补八·续五（第 132 轮：`verify-ux-json` 的 HEAD 既有失败 —— 三元表达式把"实时原文"与"视觉稿样例"混为一谈）

`verify-ux-json` 一直报 `'1' !== '4'`（8 个尺寸/主题/宽度组合），此前记为"HEAD 既有、与本会话无关"。第 132 轮定位到真实病因，**是产品缺陷，不是检查器问题**：

`mockup.js` 的 `bindJsonModes()` 里写着

```js
const source = liveSource?.length > 0 ? liveSource : sampleSource;                  // 样例 source 是**单行有效** JSON
const invalidSource = view.classList.contains("json-invalid") ? source : sampleInvalidSource;
```

视觉稿场景 `json-preview.html?json-state=invalid` 会给根节点加 `json-invalid`，但**它没有 `data-json-source`**，`source` 已回退成**有效的单行样例** `sampleSource`；于是"无效原文"取到的其实是有效单行文本。正文只有 2 行，`code.querySelector('[data-line="4"]')` 为 null，定位逻辑落到 `|| code.querySelector('.code-line')` 的兜底 = **第一行**。

而 `sampleInvalidSource`（`{\n  "sdk": {\n    "version": "10.0.201",\n    "rollForward":}\n}\n`）正是为这个场景准备的、错误在第 4 行 `"rollForward":}` 的文本 —— 设计意图与实现相反。`ux-spec.md:383` 本就写着"点击错误可以**定位对应行**"，所以是**实现违反了规格**。

修法：按"有没有实时原文"分流，实时路径行为**完全不变**（仍用宿主给的原文与 `data-json-error-line`）：

```js
const hasLiveSource = typeof liveSource === "string" && liveSource.length > 0;
const invalidSource = view.classList.contains("json-invalid") && hasLiveSource ? source : sampleInvalidSource;
```

`verify-ux-json` **24 个状态全部通过**。至此**全部 35 个分模块检查器全绿**（第 132 轮末全量复跑：`总检查器 35，非全绿 0`）。

**教训**：`?state=` 这类"视觉稿参数"和"宿主数据"是**两条不同的数据来源**，用一个布尔类名（`json-invalid`）同时表达两者，就会在只有其中一条存在时取错值。这里 `json-invalid` 既表示"宿主文档是无效 JSON"，也表示"本场景要演示无效 JSON"，而两者的原文来源不同。

## 三·前、用户裁决（第 168 轮起施行）：**不再逐项请示，界面以 intellij-community 为准**

第 168 轮用户明确：**"为什么需要我裁决啊，完全参考 intellij-community 啊"**。⇒ 本节下列各项不再是"等裁决"，而是**照权威实现**；唯一继续标注的是"权威材料本身不在本地 checkout"（那是缺材料，不是缺决定）。

| # | 项 | 按权威的处置 |
| --- | --- | --- |
| 1 | Stash `Include untracked` | **已实现（第 168 轮）**：加复选（与「保留索引状态」同处一行）、**默认不勾选**，`includeUntracked` 改由界面决定（原来写死 `true`） |
| 2 | 删除分支/标签 | **已实现**：宿主/桥接半第 170 轮（`git/branch` 加 `delete`，`force` 两步：先 `-d` 由 Git 拒绝未完全合并并给原因，用户确认后再 `-D`；新增 `git/tag` 的 `create`／`delete`，附 3 个宿主测试）；**界面半第 171 轮**（分支浮层每行右键／菜单键打开该引用自己的动作菜单，权威 = 分支浮层里每个引用的子菜单 `Git.Branch`；删除先说明影响，未合并时再问一次"未合并提交会一并丢弃"再强制删）。当前分支行按权威**不出现**检出/删除（`setEnabledAndVisible=false`），只有重命名 |
| 4 | Git 历史筛选 | **已落地（界面四项全部接线）**：宿主 `git/history` 收 `message`／`hash`／`author`／`since`／`until`／`branch`／`path`／`paths`（多值）／`authors`／`branches`，服务层按权威实现"哈希命中即**短路**其它筛选、零命中**回落**文本筛选"（`VcsLogFiltererImpl.kt:88-101,336-341`）与多值"任一命中即命中"；界面接线「文本或哈希」（回车/失焦/清空）、引用行双击与回车的分支筛选、「分支」弹层（全部＋HEAD＋本地/远程）、「用户」弹层（复选列表＋全选/全不选＋搜索框，第 188–189 轮）、「日期」弹层（选择期间…／最近 24 小时／最近 7 天，第 185／190 轮）、「路径」弹层（选择…／在树中选择…／最近，第 191 轮），并给"筛选筛空了"单独的空态与「重置筛选」。**登记差异**：路径候选来自**已加载**的项目树而非完整 `CheckboxTree`；「最近」路径筛选只在会话内保留（权威写项目级设置）。**仍缺**：与"进入文件历史要保存组合筛选"的上下文规则（详见 `09-icons.md` 第 179／185／188–191 轮、`20-branches-host-batch.md` §7 第 2 步） |
| 5 | "只有一个结果直接跳转" | **按权威默认实现**：`showPanelIfOnlyOneUsage = !FindSettings.isSkipResultsWithOneUsage()`（默认跳过）⇒ 只有一个结果时不开结果面板；Augit 是边打边搜，落地时要保留"用户可继续输入"的判据 |
| 6 | 日志左竖条＝分支面板动作组（第 171 轮遗留，**第 173／174 轮已接线大半**） | **已落地**：第 173 轮把竖条改成分支面板动作组并落地折叠/展开卡片；第 174 轮把引用树按 `live.references` 重做成 `HEAD（当前分支）` ＋ `本地/远程/标签`（行只有名字、上游进 tooltip、当前分支排组内第一、按引用名自然序），加选中态（单击选中、上下键移动、跨刷新保留）与「分支或标签」子串过滤（HEAD 行恒保留），并接上「删除分支…」（复用第 171 轮的两步影响确认）与「定位到选中分支」（选到该引用的提交，不在当前页时说明原因）。**仍缺**：① ~~**多选**（权威是 `DISCONTIGUOUS_TREE_SELECTION`，Augit 现为单选）~~ **第 182（宿主）／183（界面）轮已落地**：宿主 `git/history` 的 `branches` 并集、`git/fetch` 的 `branches` 逐个快进、`git/branch` `delete` 的 `names` 逐个删并回 `deleted`/`refused`；界面把 `live.logRefSelection` 改成选中集数组，普通单击替换／Ctrl+单击切换／Shift+单击与 Shift+方向键区间扩展／空格切换，动作判据与删除文案随选中集变化。**第 184 轮补**：引用树行的右键菜单（`BranchesTree.kt:272` 的弹出组，按选中集构成）与右键选择规则（`Tree.java:1112-1130`）；**仍缺**：~~两**任意**分支的比较（`ShowArbitraryBranchesDiffAction`／`ShowArbitraryBranchesFileDiffAction`）~~ **第 193 轮落地前半**：行菜单按 `BranchActionsBuilder.build()` 分档，「比较分支」＝ `ShowArbitraryBranchesDiffAction`（恰好两个分支取选中顺序、1 分支＋HEAD 取该分支与当前分支、3 个以上隐藏、同名禁用），范围照 `compareAny(b1,b2)` ⇒ `fromRange(b2,b1)`；「显示文件差异」（`ShowArbitraryBranchesFileDiffAction` → `CompareWithLocalDialog` 的整树变更列表）Augit 没有该界面 ⇒ 按"不新增功能"不做、已登记差异；中文复数形态塌缩与「多分支比较只开一个视图」两处差异仍在，见 `09-icons.md` 第 183／184／193 轮；② ~~**双击/回车 = 把日志筛选到该分支**（`Git.Log.Branches.Change.Branch.Filter`）—— 前置是历史筛选本身（§三·前 第 4 项）~~ **第 179 轮已落地**（双击/回车把日志筛选到该引用：分支行给分支名、HEAD 行给 `HEAD`、标签行不发起查询；见 `09-icons.md` 第 179 轮）；③ ~~「更新选中分支」（权威取该分支 refspec；宿主 `git/fetch` 只支持整仓）~~ **第 178 轮已落地**：宿主 `git/fetch` 加可选 `branch`（`IGitRemoteService.FetchBranchAsync` 按 `%(upstream:short)` 组装 `refs/heads/<remoteBranch>:refs/heads/<localBranch>` 快进本地分支，3 条宿主测试），竖条按"本地＋非当前＋有 upstream＋非 fetch 进行中"启停；**未采用**本文档原先给的两个候选（权威是快进本地分支，不是更新远端跟踪引用），**登记差异**：当前分支仍禁用（权威走更新方式合并，Augit 未接 `git/pull`）、无远端时权威隐藏而 Augit 禁用（详见 `09-icons.md` 第 178 轮、`20-branches-host-batch.md` §1）；④ ~~「与当前分支比较」（两引用整份比较）~~ **第 180 轮已落地**（核对后更正：权威打开的是**带 Range 过滤器的日志视图**——`GitCompareBranchesUi` ＝ `fromRange(current, branch)`、文本 `<current>..<branch>`——不是文件差异 ⇒ 宿主给 `git/history` 加 `rangeExclusive`/`rangeInclusive`，界面把底部工具窗口切成兄弟标签 `比较: <分支> 与 <当前>`；登记差异：权威把它开在编辑器标签里）；⑤ ~~「我的分支」（会话内过滤：分支独占提交全部由我提交，`BranchesDashboardUtil.kt:85-132`）~~ **第 181 轮已落地**（宿主 `git/branches-mine` 按 `rev-list --not` ＋ `--author` 现算判据；竖条为 `aria-pressed` 的会话内开关，引用树按 `showOnlyMy` 只留命中分支＋HEAD 行、标签一并过滤）；⑥ **「收藏」第 181 轮按用户裁决暂不实施**（新增项目级持久化状态，超出"不新增功能"的边界；竖条继续禁用并写明"待产品裁决"，落地要点见 `20-branches-host-batch.md` §4）；⑦ 「分支面板设置」**第 176 轮已落地弹层**：条目与顺序照权威，其中「显示标签」为可用复选行，**第 181 轮起持久化**到设置文件（权威持久化在项目级 `GitVcsSettings`（workspace 文件），Augit 的设置文件是应用级，登记为差异），「单击时」两开关**第 179 轮已落地**（互斥单选：FILTER＝单击即把日志筛选到该引用、NAVIGATE＝单击即定位到分支头；会话内状态，权威是应用级属性，登记为差异）、~~「按目录分组」仍禁用并写明原因（前置：前缀分组）~~ **第 192 轮已落地**（权威 `git.branches.group.by.directory`／`GitGroupBranchByDirectoryAction`，默认**开启**：引用名按 `/` 逐段构树（`LazyRefsSubtreeHolder.buildSubTree()`），前缀分组与类型分组共用折叠/搜索/全部展开折叠交互，开关写回设置文件 `ApplicationSettings.GroupBranchesByDirectory`；至此设置弹层只剩「按仓库分组」禁用——权威要求多仓库）；~~⑧ ExpandAll／CollapseAll~~ **第 175 轮已落地**（分组可折叠：单击/回车/空格、左右键；竖条两项作用在所有分组；搜索时忽略折叠）。**下一批的完整权威与实施计划已单独落盘 → `20-branches-host-batch.md`**（按分支取、历史筛选宿主侧、两引用整份比较、设置存储、多选、双击筛选的权威出处与建议顺序） | 2–3 |
| 7 | 日志右键菜单「新建标签…」| **已实现（第 172 轮）**：按 `GitCreateTagAction`（`GitCreateTagAction.java:31`，`Messages.showInputDialog` **单字段**）的契约落地 —— 标题 `在 <提交哈希> 上新建标签`（权威 `git.new.tag.dialog.title` 的 `{0}` 就是 `commit.asString()`）、字段「新标签名称」、空白字符前置拒绝，提交走 `git/tag` 的 `create` 且带 `target`（建在该提交上的**轻量**标签）。**不需要动 C#**。上一轮写的"四字段 `GitTagDialog` + C# 加 `force`"**已作废**：那是**分支浮层**那条路径。**教训**：同一个功能名在不同入口下可能是不同的对话框，先钉入口再查对话框 |
| 8 | 日志右键菜单「新建分支…」从选中提交建分支 | **已实现（第 172 轮）**：`ShellBridge.cs` 的 `create` 改为透传 `startPoint`（宿主 `CreateBranchAsync` 早已支持起点，解析不到时给"创建分支的起点不存在或不是唯一提交。"），界面把选中提交哈希带上；`ShellBridgeBranchTagTests` 新增 2 个用例（从旧提交建分支、标签打在指定提交上）。**出处**：`Git.CreateNewBranch.FromCommit`（"New Branch…"，`GitCreateNewBranchFromCommitAction.kt:19`，`backend.xml:421-423`）**只对单个选中提交可用**（多选即 `Disabled`，`:57`） |
| 3 | 图像查看器 | **仍缺材料**：权威（New UI Image Viewer）不在 `/mnt/d/github/intellij-community`（三种检索零命中）⇒ 在此之前按视觉稿基线维护；一旦拿到来源立即采集对齐 |

> 下面那张表是**裁决前的记录**，保留证据与出处；其"两条出路／我未自行选择"的表述按上面的裁决表执行。

## 三、规范 / 测试 / 权威三方不一致，需人工裁决

> **第 211 轮用户裁决（本表逐项结案）**：
> ① **Stash 的 `Include untracked`**：**加复选、默认不勾选**（完全对齐权威）—— 界面复选与显式参数早在
>    第 168 轮已就位，本轮把宿主缺省值从 `?? true` 改成 **`?? false`**（不传参数＝不包含），并加一条单测
>    钉住两条路径（默认保留未跟踪文件／勾选后才一起 stash）。ux-spec §7.11 的字段清单本来就写了"默认都不勾选"。
> ② **删除分支/标签**：**现有能力已够，只做文档校正** —— 引用行菜单的删除（含未合并时的二次确认与强制删除）
>    已在第 171 轮落地并有断言；把本表与状态矩阵里"产品根本没有这个能力"的旧描述改成与实际一致；
>    **远端分支删除**仍未接线，按边界登记为差异（权威另有远端删除路径）。
> ③ **图像查看器**：**认定为"无本地权威"**，此后 `image-preview`／`image-error` 只按**内部一致性**
>    （视觉稿＝运行时基线）维护，不再声称与 PyCharm 对齐；审计表与 `18-file-limit-image.md` §2 同步改写。
> ④ **「标记为收藏」**：**产品已确认维持禁用并写明待裁决**，不实施（维持第 181 轮的裁决）。
>
> 下面那张表是**裁决前的记录**，保留证据与出处。

| # | 项 | 冲突 |
| --- | --- | --- |
| 1 | **Stash 创建对话框的 `Include untracked` 复选**（第 141 轮提出） | **权威**：`GitStashDialog.kt:41-53` 有 `Include &untracked` 复选（`GitBundle:441-442`），默认**不勾**（`JBCheckBox` 未选中，且**不持久化**上次选择）。**宿主机**：`ShellBridge.cs:1970` 早已支持 `includeUntracked`，且 `?? true` —— 即 **Augit 现在总是把未跟踪文件也 stash**，界面却没有任何控件能改。**规范**：`ux-spec:524-525` 明确规定 stash 创建只有四个字段（Git 根目录／当前分支／消息／保留索引），并给了 Tab 循环。⇒ **加该复选会改变"什么会被 stash"**（产品行为，不只是呈现），且与 `ux-spec` 的字段清单冲突。三种选项：① 加复选、默认**不勾**（完全对齐权威，但把 Augit 的默认行为从"含未跟踪"翻成"不含"）；② 加复选、默认**勾选**（暴露控件、保持 Augit 现有行为，但默认值与权威不同）；③ 不加，把"总是含未跟踪"登记为差异。**我未自行选择**：它动的是数据行为，不属"界面呈现"。 |
| 2 | ~~**删除分支/标签**（第 147 轮提出）~~ **第 211 轮结案：现有能力已够，仅需文档校正**（见本节开头的裁决说明；能力见本节之后「已实现」那条） | **规范要求**它：`ux-spec:735` 把"删除分支或标签"与 Reset Hard／Rollback／删除 Stash／移除 Worktree 并列为"必须显示具体影响"的危险操作。**但产品根本没有这个能力**：宿主 `git/branch` 只支持 `create`／`rename`（`ShellBridge.cs:2443-2455`），UI 也只发这两种（`data-branch-action` 全库只有 create/rename）。权威侧它是有分量的操作（`GitDeleteBranchOperation`：收集未合并提交 → 删除 → 失败回滚恢复分支，并提示"Unmerged commits were discarded"）。⇒ 按边界（**不新增功能**）**不实施**；两条出路：① 立项新增该能力（宿主 + UI + 确认层 + 未合并提交分析 + 本地/远端/标签三类文案）；② 承认超出产品范围，把 `ux-spec:735` 与状态矩阵里那条 ✅ 改成"未实现"。**我未自行选择。** |
| 3 | **图像查看器无本地权威**（第 156 轮提出） | 审计第 6 区的"权威待定位"是 New UI 图像查看器的缩放/平移组件，但**它不在这份 checkout 里**：`rg -l "class ImageViewer\|class UberImageViewer\|class ImageEditor"`、`find -name "*ImageViewer*"`、全仓 `rg -l "UberImageViewer"` 三次独立检索均 **0 命中**（唯一相关命中是调试器的 `xdebugger.imageEditorUIProvider`）。⇒ `image-preview`／`image-error` 没有可比对象，Augit 的 `image-preview.js` 与视觉稿是自定基线，既不能说对齐也不能说偏离。**两条出路**：① 提供含图像查看器的权威来源（另一份 checkout／插件源码），我据此采集并落地；② 明确"无本地权威"，此后该场景只按内部一致性维护，并把审计表的"权威待定位"改成"权威不在本 checkout"。**我未自行选择。** |
| 4 | **~~Git 历史筛选完全没实现~~**（第 165 轮提出；**第 179 轮按出路①结案**） | **规范要求它**：`product-spec.md:112`「支持提交信息、哈希、作者、日期、分支和文件路径筛选历史」；`ux-spec.md` §7.8 第 14 行「提交列表顶部有文本或哈希搜索，以及分支、用户、日期和路径筛选」、第 16 行「**不移除筛选能力**……收纳菜单只调用**已有**分支、用户、日期和路径筛选动作」。**权威也有**：`platform/vcs-log/api/src/com/intellij/vcs/log/` 下的 `VcsLogTextFilter`／`VcsLogBranchFilter`／`VcsLogDateFilter`／`VcsLogStructureFilter`（路径）／`VcsLogHashFilter`／`VcsLogDetailsFilter`，以及把它们合成一份的 `VcsLogFilterCollection`。**实现现状**：筛选栏那 4 个按钮（分支／用户／日期／路径）与「文本或哈希」输入框在实时外壳里**都没有处理者** —— `bindHistoryLayout()` 只做**窄栏收起**（量宽→隐藏→焦点转移），`bindLogFilterDraft()`（第 160 轮）只把输入存成草稿；`history.commits` **从不按任何条件过滤**（检查器也只钉了这些按钮的布局与焦点）。**宿主同样缺**：`GitHistoryRequest(Page, PageSize, Filter: new GitHistoryFilter(FilePath: …))` 只支持**文件路径**一项（那是文件历史在用的）⇒ 文本/作者/日期/分支都得新增宿主过滤。⇒ 属"**规范要求、权威有、产品完全没有**"的一类，且**必须动宿主**。两条出路：① 立项实现（多轮：宿主过滤 ＋ 四个筛选弹层与状态机 ＋ 与"进入文件历史要保存组合筛选"的上下文规则配套）；② 承认超出当前范围，把 `product-spec.md:112` 与 `ux-spec.md` §7.8 第 14 行改成"未实现"，并让筛选栏不再显示成可用入口。**我未自行选择。** **⇒ 第 179 轮按出路①落地**（宿主筛选通道 ＋ 界面接线；用户／日期／路径三个弹层与"进入文件历史保存组合筛选"的上下文规则仍未做），见 `09-icons.md` 第 179 轮。 |

## 三·补、**能力已在 C# 里、桥接与界面缺失**（第 148 轮：宿主方法表全量扫描的产物）

这一类与 §三 的人工裁决不同：**能力已经实现**，缺的只是接口层与界面 —— 按判据属"对齐"而非"新增"，可直接做。之所以单列，是因为它们**必须动 C#**，而 C# 改动要连带构建整个 shell。

| # | 能力 | C# 侧（已实现） | 缺什么 | 估轮 |
| --- | --- | --- | --- | --- |
| 1 | ~~**仓库初始化**~~ **第 204–205 轮已落地** | `IGitServices.InitializeAsync`（`IGitServices.cs:18-20`）／`GitRepositoryService.cs:116` | 宿主第 204 轮：`git/init` 并入写操作通道（`path`／`confirm`，只有“目标已在 Git 下”回 `requiresConfirmation`，成功后作废解析与状态缓存）＋ 3 条单测；界面第 205 轮：Git 菜单「创建 Git 仓库…」、非 Git 工作区自动入口、系统目录选择器、进行中冻结、失败原因在窗口内、成功关窗并刷新（`docs/nui-behavior/09-icons.md` 第 204／205 轮）。 | 0 |**界面契约已采全（第 155 轮，`17-repository-init-search.md` §1）**：非仓库**不弹确认**、只有"目标已在 Git 下"才用 Yes/No ＋警告图标问一次（`GitInit.java:66-74`）；初始化走后台任务（标题 `common.refreshing`）；失败给错误通知（含 Git 错误输出）；成功后刷新 VFS＋写 VCS 目录映射 | 1 |
| 2 | ~~**Smart Checkout**~~ **第 206–207 轮已落地** | `IGitServices.SmartCheckoutAsync`（`IGitServices.cs:323-326`）／`GitOperationService.cs:187`（含 `SmartCheckoutMarker`／`GitOperationKind.SmartCheckout`／`CompleteSmartCheckoutAsync`） | ~~桥接方法 ＋ 单测~~ **第 206 轮已完成**：`git/checkout-smart`（写操作通道，回包与 `git/operation` 共用会话投影；+3 条单测；权威的 Force Checkout 登记为差异）。界面第 207 轮：`git/checkout` 失败时回传 `overwriteRisk`／`overwritePaths`，据此弹权威形态对话框（受影响文件列表、默认焦点在不检出、取消绝不执行 Git），恢复冲突时说明临时 stash 已保留（`docs/nui-behavior/09-icons.md` 第 207 轮）。 | 0 |
| 3 | ~~**归属行的 `Date:` 时间**（第 150 轮）~~ **第 201 轮已落地**：桥接补 `dateTime`（`git/blame` 每行，`yyyy/M/d H:mm` 本地时区）、`loadBlame` 透传、`blameTooltipDate()` 用它，槽位仍显示短日期；新增桥接单测 | `GitBlameLine.AuthorDate` 是 `DateTimeOffset`（`GitHistoryModels.cs:108`） | 桥接只回传 `yyyy/M/d`（`ShellBridge.cs:2546`），而权威提示的 `Date:` 用 `DateFormatUtil.formatDateTime`（日期 + 时间，`DateFormatUtil.java:120-122`／`GitFileAnnotation.java:193`）⇒ 多回一个 ISO 时间字段并接到 `blameRowTooltip` | 1 |
| 4 | ~~**Annotate Previous Revision**（第 150 轮）~~ **第 202 轮已落地**：宿主解析 `git blame` 的 `previous` 头（`GitBlameLine.PreviousRevision`）、`git/blame` 收 `revision` 并回传 `previousRevision`；界面在归属行右键给出「标注上一修订」（只对有上一修订的行）并按该修订就地重新标注（**登记差异**：权威开在新标签）；+1 宿主单测 | `IGitServices.ReadBlameAsync(repository, path, revision, ct)` 已支持 `revision`（`IGitServices.cs:142-146`） | `ShellBridge.ReadBlameAsync` 把第三个实参硬编码为 `null`（`ShellBridge.cs:2530`），界面只发 `{ path }`（`live-data.js:290`）⇒ 传参 + 入口（权威 `AnnotatePreviousRevisionAction`） | 1 |
| 5 | ~~**文件历史作者列的邮箱与提交者**（第 151 轮）~~ **第 203 轮已落地**：`GitHistoryEntry` 新增 `CommitterName`／`CommitterEmail`，`%cn`／`%ce` 进 `HistoryFormat`（非推送列表改为复用同一格式串，提交详情同步），`git/file-history` 与 `git/history` 都回传 `authorEmail`／`committerName`／`committerEmail`；界面 `historyAuthorCell()` 按权威 `AuthorColumnInfo` 渲染值后的 `*` 与 `{作者} <{邮箱}>[, via {提交者} <{邮箱}>]`；+1 宿主单测（Infrastructure）、+1 桥接单测（Shell） | `GitHistoryEntry.AuthorEmail` 已在模型里（`GitHistoryModels.cs:33`） | — | 0 |
| 6 | ~~**快速打开的结果上限 100 → 30**（第 153 轮）~~ **第 201 轮已落地**：`SearchOptions.MaximumFileResults = 30`（权威 `SINGLE_CONTRIBUTOR_ELEMENTS_LIMIT`），两个宿主单测与 `ux-spec.md:587` 同步 | `RipgrepSearchService` 已实现 100 项上限（并有宿主单测 `文件搜索最多返回一百项`） | 权威的"单贡献者"搜索上限是 **30**（`SearchEverywhereUI.java:217` `SINGLE_CONTRIBUTOR_ELEMENTS_LIMIT`，`:951-958` 按贡献者数取 30／15；`GotoFileAction` 正是单贡献者的 Files 档）。改它要同步 **C# 服务常量 + 宿主单测 + `ux-spec.md:587`**，故与 C# 批次一起做 | 1 |
| 7 | ~~**全仓搜索到 1000 条后"是否继续"**（第 155 轮）~~ **第 210 轮已落地** | ripgrep 搜索已实现 1000 项截断 | 权威阈值同样是 **1000**（高级设置 `ide.find.result.count.warning.limit` 默认值，`intellij.platform.ide.impl.xml:1491`），但到限后**弹警告问是否继续**（`find.excessive.usages.title` = "Too Many Results"、`Continue`／`Abort`，`UsageLimitUtil.java:26-34`），Augit 直接停止并提示缩小范围。**第 210 轮已完成**：宿主 `search/text` 支持 `offset`／`limit` 分页；界面到限弹权威的「结果过多」（标题／正文／Continue／Abort、Continue 为默认按钮），继续即用 `offset` 分页取回并追加、不再提示，中止保留已有结果并写明原因（`09-icons.md` 第 210 轮）。 | 0 |
| 8 | ~~**大文件阈值与"只读预览"**~~ **第 208–209 轮已落地** | `ReadOnlyDocumentService` 已能按 `DocumentLimits` 判定超限并回状态 | 权威是**三档按扩展名的限制**（`FileSizeLimit.kt:14-19`：内容加载 20 MB／`idea.max.content.load.filesize`、智能感知 2500 KB、预览 2500 KB，且**按扩展名只能放大**）＋超限后**仍显示"前 N 的只读预览"**并给可隐藏/不再显示的警告（`LargeFileNotificationProvider.java:49-58`、"The file is too large ({0}). Showing a read-only preview of the first {1}."）。**第 208 轮宿主侧已完成**：`DocumentLimits` 改成权威三档（20 MB／2500 KB／2500 KB，扩展名只能放大），`ReadOnlyDocumentService` 超过内容加载上限时按完整 UTF-8 字符边界返回前 `PreviewLimit` 字节的 `TextPreview` ＋ `previewBytes`，`document/read` 带上该字段（+3 条单测）。界面第 209 轮：`TextPreview` 走纯文本只读视图 ＋ 编辑器顶部的 Warning 横幅（「隐藏通知」只记会话、「不再显示」写 `HideLargeFileWarning` 设置），并改写三处编码"整页拒绝"的旧断言（`09-icons.md` 第 209 轮）。 | 0 |
| 9 | **创建标签**（第 167 轮） | `IGitServices.CreateTagAsync`（`IGitServices.cs:205`；另有 `DeleteLocalTagAsync`／`PushTagAsync`／`DeleteRemoteTagAsync`） | **桥接已实现（第 170 轮）**：`ShellBridge` 新增 `git/tag`（`create` 轻量/附注、`delete`）＋ 宿主测试。**删除的界面入口已实现（第 171 轮）**（引用行自己的动作菜单）。剩：日志右键菜单的「新建标签…」按 `GitCreateTagAction` 的**单字段**对话框接线（见 §三·前 第 7 项，纯界面）；日志工具栏「删除引用」见 §三·前 第 6 项 | 1 |

**为什么本轮没做**：`src/Augit.Shell/` 下**并行会话正在编辑** `Program.cs`／`ShellOptions.cs`／`ShellWindow.cs` 并新增主题相关文件；`dotnet build`／`dotnet test` 会把他们的在途改动一起编译，失败时无法归因。等其收尾，或明确"允许在共享 shell 上并行构建"后再做。

**判据回顾（三类缺口的处理各不相同）**：

| 类型 | 例子 | 处理 |
| --- | --- | --- |
| 宿主有能力、界面没入口，且**不翻转默认** | Worktree 的 `newBranch` | 直接做（已完成） |
| 宿主有能力、界面没入口，但**会翻转默认** | Stash 的 `includeUntracked` | 交裁决（见 §三 第 1 项） |
| 能力在 C#、**桥接/界面都缺** | 仓库初始化、Smart Checkout | 可直接做，但需独立一轮 + 构建（本节） |
| **规范要求、产品完全没有该能力** | 删除分支/标签 | 不新增，交裁决（见 §三 第 2 项） |

### 三·补二、第 3 区剩余：文件历史列表的列集合 —— **第 151 轮结案**

权威的列顺序是 **Version → Date → Author → [provider 附加列] → Commit Message**（`FileHistoryPanelImpl.java:292-306`；Git 的 `isDateOmittable() = false` 故日期列出现，`GitHistoryProvider.java:75-78`）。
Augit 原为 **作者 → 日期 → 提交信息** 三列、无表头 ⇒ 已于第 151 轮改为权威四列 **版本 → 日期 → 作者 → 提交信息**，并补上表头行（表头与数据行共用同一套 CSS 列宽，由 `verify-ux-file-history` 逐列比对）。详见 `14-file-history-blame.md` §2.2。

**同区已清零**：作者列的 `*`（作者 ≠ 提交者）与单元格 tooltip 曾并入本节 §三·补 第 5 项，**第 203 轮已落地**（见 `14-file-history-blame.md` §2.4）。

**登记未应用**：`myDualView.setShowGrid(true)`（`FileHistoryPanelImpl.java:370`）的单元格表格线，理由见 `14-file-history-blame.md` §2.2 末（New UI 下的实际绘制无法离线核对，不拿猜测覆盖已验收的视觉基线）。

数据侧不需要改外壳：`git/file-history` 载荷已带 `hash`／`fullHash`／`author`／`date`／`subject`。
### 三·补三、第 9 区最后一项：全仓搜索的结果呈现 —— **第 159 轮结案：不是差异**

**上一轮（158）我把这一项写成了"权威按文件分组、Augit 是扁平行 ⇒ 待落地"。第 159 轮把"表面"钉住后**推翻了**它**：New UI 的同一个功能在**两个表面**里呈现不同，而 Augit 的浮层对应的是**弹层**那个。

| 表面 | 结果呈现 | 证据 |
| --- | --- | --- |
| **Find in Files 工具窗口** | `UsageView` **树**，默认按文件结构分组 | `FindInProjectUtil.java:75-79,395-459`；`platform/usageView/src/com/intellij/usages/UsageViewSettings.kt:20-27`（`isGroupByFileStructure = true` 等，用户可改） |
| **弹层（Find popup / Search Everywhere 文本页签）** | **扁平行**，每行一个命中；**按文件路径排序**（同一文件的命中相邻），并且**当前第一个结果所在的文件排到最前** | `platform/lang-impl/src/com/intellij/find/impl/FindPopupResultsAutoloadHandler.kt:69-80`（`rowComparator`：`state.firstResultPath` 优先 → `u1Path.compareTo(u2Path)`）、`:145-147`、`:349`（`FindPopupItem(usage, usagePresentation)` 的**行**模型） |

**Augit 的浮层是弹层**（非模态、键盘优先、边打边搜），对应的是**第二行**那个表面：`liveSearchOverlay('repository')` 的扁平行 ＋ 宿主 ripgrep 按文件连续输出的顺序 ⇒ **结构与排序都对齐** ✓ ⇒ **不需要改**。

**沉淀（第五条）**：**同一个功能在 New UI 里可能有两个表面（弹层 vs 工具窗口），呈现规则可以不同**。对齐前必须先钉"Augit 的这个东西对应哪个表面"——第 158 轮只查到"两边都用到 UsageView 的 presentation"就下了"都分组"的结论，漏了弹层自己那套**行**模型（`FindPopupItem`）。**"用了同一套后端"不等于"用了同一种呈现"。**

## 四、低优先级的清理项

| # | 项 | 说明 |
| --- | --- | --- |
| 1 | **`gen-coverage-table.cjs --write` 会删掉手写内容（第 180 轮实测，必须先修再跑）** | 它重写的区块里包含轮次手写的像素复核记录：在 `docs/ui-compliance.md` 上跑一次 `--write` 实测 **删掉 128 行**（第 86–93 轮那串 `> **第 N 轮补测（④，判据 = layoutPercent）**` 全部消失），且**第二次跑结果相同**（不是"跑两次才稳定"，而是每次都删）。恢复方式：`git show HEAD:docs/ui-compliance.md > docs/ui-compliance.md` 后重放本轮的行内改动 ＋ 重跑 `gen-clause-conclusions.cjs --write`（后者只动 §2.10，是安全的）。**结论：在把该生成器的重写区间收缩到手写块之外之前，不要再对它用 `--write`**；只需打印时直接 `node tools/audit/gen-coverage-table.cjs` |
| 1 | ~~`docs/handoff.md` §7「验证基线（当前实测）」整表过期~~ **第 178 轮已处理**：该节改名为「验证基线」并整表标注为**历史快照**，同时写明当前基线以 `intellij-platform-ui-behavior.md` §2.3 为准（含第 177 轮实测的 352 个单元测试与 1136 项断言）；未复测的像素/冷启动数字保持原值但不再声称"当前"。 | 表里仍写"单元测试 236（Core 86 + Infrastructure 150）／验收套件断言 261"，而第 172 轮实测是 **Core 86 ＋ Shell 91 ＋ Infrastructure 175**、`live-shell` **1120 项断言**；`视觉稿场景渲染 41/41`、`冷启动 94–158 ms` 等行也无法用当前工具复算。该表是早期轮次的快照，所属文档不在 `AGENTS.md` 的当前规范清单里 ⇒ **本轮只登记不改**（改它等于替一份历史快照背书，且 `handoff.md` 同时有并行会话在改）。建议：要么整表标"历史快照（第 N 轮）"，要么按当前实测重算后只在 `intellij-platform-ui-behavior.md` 保留一份 |


## 五、已解决、仅保留交叉引用的项
| 三处高度的规格名义值 vs 运行时派生值 | **第 116 轮按用户裁决解决（以规格名义值为准）**：`mockup.js` 三处公式恢复为规格形式 —— `tab-height` `Math.max(42, height + 14)`、`tree-height` `Math.ceil(Math.max(27, height + 8) / 2) * 2`、`status-height` `Math.max(22, height + 2)`，默认字号下实测 **42／28／22** ✓。**随之四处固定套件补丁全部作废** —— 未打任何补丁直接跑 HEAD 版 `live-shell.spec.cjs` 得 **1070 项断言通过**（§154 的 `firstVisible` 正是 HEAD 期望的 `docs/bulk-006.txt`）| 第 116 轮 |

| 弹层/对话框深色底色 | **第 109 轮解决**：`ManyIslandsDark` 的 layer 链显示 `popup-bg` = `layer-1-bg` = `gray-30` = #26282C、`dialog-bg` = `layer-0-bg` = `gray-10` = #191A1C ⇒ 深色下**对话框比弹层暗**（与浅色同序）。落地：新增 `--augit-popup-bg`（浅 #FFFFFF／深 Gray2 #2B2D30），弹层与搜索浮层改用它；对话框维持 `--augit-dialog-bg`（深 Gray1 #1E1F22）| 第 109 轮 |

| 批量 CSS 改动缺少等价性检查 | **第 107 轮解决**：新增 `tools/audit/verify-css-equiv.cjs`（新旧 CSS 逐元素计算样式对照，默认 6 页 × 全元素 × 19 项属性） | 第 107 轮 |

| 同名规则的基础层死值 | **第 107 轮解决**：审计出 **46** 组"选择器字符串+属性+值三者完全相同"的重复声明（选择器相同 ⇒ 特异性相同 ⇒ 后者胜）。但首轮直接删除后，**新旧 CSS 逐元素计算样式对照**发现 6 处实为 `@media` 内的"后者"（媒体查询不成立时先前那条才生效），遂改为**媒体查询感知**版本，只删 **40** 处，再对照得 **6 个页面 0 差异**（653–877 个元素 × 19 项属性）✓ | 第 107 轮 |

| 对话框标题行右内距 | **第 106 轮解决**：`.dialog-header` 第二条基类（3406 行）的 `padding: 0 13px` 与 reset／rollback 的右侧 13px 均改为 **12**，与 `getRegularPanelInsets()` 一致。**遗留**：`.compact-input`（COMPACT 风格，活类）的内距保留待核 | 第 106 轮 |

| 对话框页脚/标题内距 | **第 104 轮解决**：权威 `DialogWrapper.java:1522` south section = `empty(0,12,8,12)`、`:843` COMPACT = `empty(8,12)` ⇒ 横向 **12**。8 个页脚 16/17/22 → 12（push 保留 top 12）；5 个标题 `padding-left: 25/26` → 12（reset/rollback 保留 16，图标左边距实测值） | 第 104 轮 |

| 对话框 body 内距 | **第 105 轮解决**：权威 `UIUtil.getRegularPanelInsets()` = `(8,12,8,12)`（`UIUtil.java:370-371`）⇒ 11 条 body 规则统一为 `8px 12px`（4 个滚动容器保留纵向 0、横向 12）。**例外**：`.compact-input .dialog-body` 未动（对应 COMPACT，权威内容边框为空）。（标题行右侧 13px 已在第 106 轮改为 12）| 第 105 轮 |


| 对话框标题图标 | **第 102 轮（部分）**：reset／rollback 落地 28px 蓝底白问号图标（官方 `questionDialog.svg` 配色 浅 `#4682FA`／深 `#548AF7`，参考图实测 27.3 逻辑px、左边距 16 逻辑px），用 `.dialog-header::before` 实现；其余 5 个操作对话框的图标仍无依据 | 第 102 轮 |

| 滚动条悬停档 | **第 101 轮结案（悬停档不可实现，并修掉一处深色错误）**：权威默认值在 `ScrollBarPainter.java:88-122`（非 Mac 分支）——`hoverThumbColor` = 浅 `#73737347`／深 `#a6a6a659`。但 `scrollbar-color` 无法表达 hover，`::-webkit-scrollbar` 又与标准属性冲突（Chromium 优先标准属性），故悬停档**不实现**。顺带订正：`ScrollBar.Transparent.thumbColor` 的深色一半是 `#A6A6A647`，Augit 原先深浅同值 `#73737333`，已修正 | 第 101 轮 |

| 树行与 Changes 行的悬停底 | **第 100 轮解决（用户裁决：行有悬停底）**：两处改用权威 `--augit-row-hover`（浅 `selection-bg-hovered` = `transparent-black-10` = `#00000008`、深 `#464A4D`）。证据：平台元数据把 `List/Tree/Table.hoverBackground` 都记为 "…**if hover is allowed**"（since 2020.3）；参考实现没给树装 `TreeHoverListener`，故这是**有意偏离**，已在 design-system §6.1/§8.3 注明 | 第 100 轮 |

| `--augit-orange` 零引用 | **第 100 轮清理**：删除浅（`#a56906`）深（`#ba9752`）共 2 处定义，全库 0 次 `var()` 引用 | 第 100 轮 |

| 日志列表失焦选中色 | **第 99 轮解决**：`--augit-history-selection-inactive` 浅色 `#dfe1e5` 改为 **`#e9eaee`**。根因是浅色档误用了 `--augit-text` 的**深色**值（复制串行）；权威 `selection-bg-inactive` = `gray-150` = `#E9EAEE`，与既有的 `--augit-selection-inactive` 同值。深色 `#43454a`（Gray4）在 expUI_dark 无对应键，保留 | 第 99 轮 |

| 图标按钮尺寸 27 vs Jewel `IconButton` 24 | **第 98 轮判定不是冲突**：`ui.Editor.Toolbar` 只有 `borderColor`（无尺寸键）；Jewel 侧有 `IconButton.kt`，但 `platform/jewel/ide-laf-bridge` 里**没有** Swing 的 `IntUiBridgeIconButton`，说明 24×24 属 **Compose** 组件，而编辑器工具条是 Swing（`EditorToolbarButtonLook`），两者不是同一栈、不能互相套用。Swing 侧工具条按钮尺寸在权威里查无此键，故 Augit 的 27 保留 | 第 98 轮 |
| 三个零引用令牌 | **第 98 轮解决**：`--augit-blue-hover`／`--augit-green`／`--augit-green-soft` 各删 2 处定义（浅+深），全库 0 引用、`shell/` 亦无；顺带更正了把它们说成"服务冲突解决器与危险动作"的过时注释。同时发现 `--augit-orange` 也 0 引用（见下表） | 第 98 轮 |

| 选项卡选中边框的 painter 默认值 | **第 97 轮核实无需改动**：`IslandsTabPainter.kt:237` 的 `EditorTabs.underlinedBorderColor` 默认就是 `JBColor(0x7F99C3, 0x7F99C3)`，与 `--augit-tab-selected-border-active` 的 `#7f99c3` 一致；`:229` 的 `EditorTabs.inactiveUnderlinedTabBorderColor` 是 `JBColor(Color(0x7F,0x99,0xC3,0x80), …)`，正是 `--augit-tab-selected-border` 的 `#7f99c380` ✓ | 第 97 轮 |
| `.brand-mark` 死声明 | **第 97 轮解决**：删除被覆盖的 23px/5px 死值，徽标统一为 **20×20**（与 `.workspace-chip > .brand-mark` 及 `recentProjectAvatarIconSize()` 一致） | 第 97 轮 |

| 菜单项行高 | **第 96 轮解决**：统一为 **28px**（参考图 42 物理px ÷1.5）；权威无该键，且 JS 只负责定位菜单、不设高度，故直接改 CSS 即可生效 | 第 96 轮 |

| 对话框按钮高度 | **第 95 轮解决**：统一为 **37px**（参考图实测 37.3，权威下限 28）。关键认知：高度**由 `mockup.js` 运行时算**（模板拼接变量名），须同时改 JS 公式与 CSS fallback；并解开字段/行高对按钮变量的借用 | 第 95 轮 |


以下条目在早期文档里写着"待核／未落地"，**后续轮次已经落地**，此处只列索引，避免误读：

| 项 | 结论 | 落地轮次 |
| --- | --- | --- |
| `--augit-row-hover` / `-inactive` | 浅色两档统一为 `#00000008`（`selection-bg-hovered`／`transparent-black-10`），深色保持 `#464A4D` | 第 52 轮 |
| `--augit-reference` | 令牌已删除，改为 `--augit-ref-head/branch/remote/tag` 四分组 | 第 51 轮 |
| 工作区 chip 底色 | 权威里工具栏底、图标底、下拉底**同值**（浅 `#E9EAEE`／深 `Gray2`），故 chip **没有独立底色**，已改为 `transparent` | 第 70 轮 |
| `.gitignore` 图标 | 与 `ignored` 分开，取 `fileTypes/gitignore.svg` 的 `#F34E29` | 第 62 轮 |
| 分段控件 `.segmented` | **第 81 轮解决**：它不是 Jewel 组件，而是经典 Swing 的 `SegmentedButtonToolbar`（`platform-impl/.../ui/dsl/builder/components/SegmentedButtonToolbar.kt`）——依据是它用同一组 `SegmentedButton.*` 键、`SegmentedButtonLook.getStateBackground()` 的焦点逻辑（`parent.hasFocus()` 选 FOCUSED/SELECTED）与 Augit 的 `:focus-within` 实现完全同型、圆角同样走 `DarculaUIUtil.BUTTON_ARC`(=6⇒半径 3)。**Jewel 的 14.dp 内距与 72×28 minSize 属于另一个组件，不适用**。容器边框内距按 `SegmentedButtonBorder.getBorderInsets()` = `BW + LW` = `Component.focusWidth`(2)+1 = 3，已把 `padding` 由 1 改为 **2**（合计 3）| 第 81 轮 |
| 左轨**按钮几何** | **第 80 轮核实**：`ToolWindowStripeExtension.ICON_UNSCALED_SIZE = 16`，按钮尺寸取自该扩展的 `getButtonMinSize()`；`StripeToolbar.Button.size/iconSize` 的代码默认 40×40／20 只在未装扩展时生效。参考图实测 ≈32，与 Augit 的 32×32 + 16px 图标一致，**无需改动**；圆角已于第 84 轮定案 = **3**（`IdeaActionButtonLook.getButtonArc()` = `BUTTON_ARC` = 6 ⇒ 半径 3，原 7px 已改） | 第 80 轮 |
| 左轨"灰色选中态" | **第 78 轮解决**：绘制类 `SquareStripeButtonLook.getBackgroundColor()/paintIcon()` 里，只有 `button.isFocused()` 时才用 `ToolWindow.Button.selectedBackground`（浅 `#3871E1`／深 `#3574F0`）与白色前景，否则用 `ToolWindow.Button.foreground`。已按"未聚焦=普通前景+透明底、聚焦(:focus)=白字+accent-brand 底"落两档 | 第 78 轮 |
| 当前分支行底色**范围** | 按 `CurrentBranchHighlighter` 实现为"从 HEAD 沿父链可达的全部提交"，选中行不染色 | 第 56 轮 |
| 按下态 | 新增 `--augit-pressed`（浅 `#00000020`／深 `#FFFFFF26`），标题栏另有 `--title-button-pressed`（深 `Gray3`） | 第 65／69 轮 |
