# 08 · Diff 与合并的渲染规则

本节要点：

1. **变更类型的颜色键映射是唯一的**：`LineStatusMarkerColorScheme.getColor()` 把 `Range.INSERTED/DELETED/MODIFIED/EQUAL` 分别映射到 `ADDED_LINES_COLOR` / `DELETED_LINES_COLOR` / `MODIFIED_LINES_COLOR` / `WHITESPACES_MODIFIED_LINES_COLOR`（来源：`platform/diff-impl/src/com/intellij/openapi/diff/LineStatusMarkerColorScheme.kt:23-31`）。
2. **gutter 变更标记是不透明填充**，不做 alpha 混合；alpha 只出现在未变更区域的分隔条图案上（来源：`platform/diff-impl/src/com/intellij/openapi/diff/LineStatusMarkerDrawUtil.java:195`、`platform/diff-impl/src/com/intellij/diff/util/DiffLineSeparatorRenderer.java:263`）。
3. **`DIFF_SEPARATORS_BACKGROUND` 是废弃键**，只做向后兼容；分隔条图案真正的颜色键是 `DIFF_SEPARATOR_WAVE`，回落链为 `DIFF_SEPARATOR_WAVE` → `DIFF_SEPARATORS_BACKGROUND` → `Gray._128`（来源：`DiffLineSeparatorRenderer.java:317-322`、`:351-357`）。
4. 分隔条图案是**正弦样折线**：每 4 个 step 一个周期，step = `max(scale(diff.divider.width) / 6, 2)`，半高 `scale(3)`，上下各留 1px 抗锯齿间隙（来源：`DiffLineSeparatorRenderer.java:326-349`）。
5. `DIFF_INSERTED` / `DIFF_DELETED` 在 expUI 配色方案中**没有 BACKGROUND**，只有 `DIFF_MODIFIED` 有；词级高亮的底色因此不是由方案文件给出的（见 §5、§8）。

## 1. 数据模型与颜色键映射

变更块以 `Range` 表示，类型是四个常量之一：`INSERTED`、`DELETED`、`MODIFIED`、`EQUAL`。

| 变更类型 | 颜色键 | 用途 |
| --- | --- | --- |
| `INSERTED` | `EditorColors.ADDED_LINES_COLOR` | 新增行 |
| `DELETED` | `EditorColors.DELETED_LINES_COLOR` | 删除行 |
| `MODIFIED` | `EditorColors.MODIFIED_LINES_COLOR` | 修改行 |
| `EQUAL` | `EditorColors.WHITESPACES_MODIFIED_LINES_COLOR` | 仅空白差异的修改行 |

（来源：`LineStatusMarkerColorScheme.kt:23-31`）

另有三个独立键：

| 键 | 作用 | 取值来源 |
| --- | --- | --- |
| `EditorColors.BORDER_LINES_COLOR` | 变更块边框颜色 | expUI 深浅两套方案**均未定义**，`ColorKey` 也无代码默认值 ⇒ `getColor` 返回 null（来源：`platform/editor-ui-api/src/com/intellij/openapi/editor/colors/EditorColors.java:82`，`createColorKey` 不带默认值） |
| `EditorColors.IGNORED_ADDED_LINES_BORDER_COLOR` 等三个 | 忽略空白差异时的行边框 | 方案文件已定义，见 §2 |
| `DiffColors.DIFF_INSERTED` / `DIFF_DELETED` / `DIFF_MODIFIED` | 词级与错误条纹的属性键 | `TextAttributesKey`，见 §5 |

## 2. 颜色取值（expUI，两主题）

| 键 | 浅色 | 深色 |
| --- | --- | --- |
| `ADDED_LINES_COLOR` | `#7FC784` | `#549159` |
| `DELETED_LINES_COLOR` | `#767A8A` | `#868A91` |
| `MODIFIED_LINES_COLOR` | `#88ADF7` | `#375FAD` |
| `WHITESPACES_MODIFIED_LINES_COLOR` | `#F7E2CB` | `#52433D` |
| `DIFF_SEPARATORS_BACKGROUND`（废弃） | `#E4E6EB` | `#2B2D30` |
| `WHITESPACES`（空白字符色） | 未定义 | `#6F737A` |
| `IGNORED_ADDED_LINES_BORDER_COLOR` | `#7FC784` | `#549159` |
| `IGNORED_DELETED_LINES_BORDER_COLOR` | `#767A8A` | `#868A91` |
| `IGNORED_MODIFIED_LINES_BORDER_COLOR` | `#88ADF7` | `#375FAD` |
| `DIFF_MODIFIED` 属性 | `BACKGROUND=#C2D8F2`、`ERROR_STRIPE_COLOR=#B6D2F2` | 未定义 |

（来源：`platform/platform-resources/src/themes/expUI/expUI_lightScheme.xml:12,16,17,21-23,31,44,260`；`expUI_darkScheme.xml:12,18,19,25-27,37,52`）

### 2.1 正文行底色的有效值（方案覆盖 + 默认回落）

上表是"线条颜色"，**不是正文行的底色**。`DIFF_INSERTED` / `DIFF_DELETED` / `DIFF_MODIFIED` 这三个 `TextAttributesKey` 的 `BACKGROUND` 同时供**整行底**与**行内（词级）高亮**使用（两者同色，见 §2.2 的两档规则；第 223 轮定案）。取值如下（expUI 只覆盖了其中的 `DIFF_MODIFIED`（浅色），其余走默认值）。默认值在 `platform/platform-resources/src/DefaultColorSchemesManager.xml`：

| 属性 | 浅色 BACKGROUND | 深色 BACKGROUND | 有效来源 |
| --- | --- | --- | --- |
| `DIFF_INSERTED` | `#BEE6BE` | `#294436` | 默认值（expUI 未覆盖） |
| `DIFF_DELETED` | `#D6D6D6` | `#484A4A` | 默认值（expUI 未覆盖） |
| `DIFF_MODIFIED` | `#C2D8F2` | `#385570` | 浅色由 expUI 覆盖（原默认 `#CAD9FA`）；深色走默认值 |
| `DIFF_CONFLICT` | `#FFD5CC` | `#45302B` | 默认值 |

（来源：`platform/platform-resources/src/DefaultColorSchemesManager.xml:489-524,2255-2278`；`expUI/expUI_lightScheme.xml:260-263`）

**这些就是最终色值，不需要再叠 alpha。** `ADDED_LINES_COLOR`（浅 `#7FC784`）等 `*_LINES_COLOR` 仍是**另一个用途**的键：它服务 `LineStatusMarkerColorScheme` 的**行号槽实心标记**（VCS 行状态），不是差异正文的行底；`DIFF_*.BACKGROUND` 服务差异正文的行底与行内高亮。实现时不要混用。

**整行底色分两档（第 223 轮由权威代码定案）**：`DiffViewerHighlighters.createHighlighter` 里

```
val ignored = !resolved && innerFragments != null      // platform/diff-impl/.../simple/DiffViewerHighlighters.kt:113-129
... LineHighlighterBuilder(editor, startLine, endLine, type).withIgnored(ignored) ...
```

`ignored` ⇒ `PaintMode.IGNORED` ⇒ `DiffTextAttributes.getBackgroundColor()` 取 `TextDiffTypeImpl.getIgnoredColor()` = `ColorUtil.mix(DIFF_*.BACKGROUND, 编辑器底, 0.6)`（`DiffDrawUtil.java:568-590,847-863`、`TextDiffTypeFactory.java:64-74`、`MixedColorProducer.java:48-53`）；否则取 `PaintMode.DEFAULT` 的全强度 `DIFF_*.BACKGROUND`。`getIgnoredColor()` 先看 `FOREGROUND`，而两套方案的 `DIFF_*` 都只定义了 `BACKGROUND`（`FOREGROUND` 为空）⇒ 总是走 mix 分支。

因此**只有存在行内差异的变更块才有柔和行底**。纯新增／纯删除块的一侧为空 ⇒ 没有行内差异 ⇒ 整行是全强度色；`Modified` 行才有行内差异 ⇒ 整行是 mix 出来的柔和色。深色同理（编辑器底取 `expUI_darkScheme.xml:756-760` 的 `TEXT.BACKGROUND = #1E1F22`）：

| 层 | 浅色 | 深色 | 出处 |
| --- | --- | --- | --- |
| 整行底 · 纯新增（无行内差异） | `#BEE6BE` | `#294436` | `DIFF_INSERTED.BACKGROUND` 全强度 |
| 整行底 · 纯删除（无行内差异） | `#D6D6D6` | `#484A4A` | `DIFF_DELETED.BACKGROUND` 全强度 |
| 整行底 · 修改（有行内差异） | `#E7EFFA` | `#283541` | `mix(DIFF_MODIFIED.BACKGROUND, 编辑器底, 0.6)` |
| 行内（词/段级）高亮 | `#C2D8F2` | `#385570` | `DIFF_MODIFIED.BACKGROUND` 全强度（`change.diffType`） |

### 2.2 层归属的像素复测（第 133 轮首测 → 第 223 轮订正）

§2.1 把 `DIFF_*.BACKGROUND` 记成"正文行底色"**不完整**。第 133 轮直接测参考图 `artifacts/pycharm-16-final/diff-viewer-ctrlD-file.png`（2898×1734）：

**方法**（可复用）：用无头 Chromium 把 PNG 读成 `data:` URL 画进 canvas，再 `getImageData` 统计**精确 RGB 直方图**，并对目标颜色输出**包围盒**与**逐行 x 分段**；必要时用 `drawImage` 裁出局部放大目视。`data:` URL 不会污染 canvas，因此不需要起服务器。

| 参考图里的层 | 浅色实测值 | 像素数 | 几何 | 对应权威键 |
| --- | --- | --- | --- | --- |
| **整行底 · 纯新增** | `#BEE6BE` | 36323（同一值也用于行内层） | 单行满宽（`y623–661` ≈ 26 逻辑px，`1932-2817`） | `DIFF_INSERTED.BACKGROUND` 全强度 ✓ |
| **整行底 · 修改** | `#E7EFFA` | 122390 | 整行满宽（`y623–776`，两栏） | `mix(DIFF_MODIFIED.BACKGROUND #C2D8F2, #FFFFFF, 0.6)` ✓ 逐值吻合 |
| **行内（词/段级）高亮** | `#C2D8F2`（改）／`#BEE6BE`（增） | 63788／36323 | **局部段**：`#C2D8F2` 在 y700 为 `887-1026(140)`（左栏 `powershell` 一词）等；`#BEE6BE` 的局部段同样出现 | `DIFF_*.BACKGROUND` 全强度（`change.diffType`）✓ |
| 行号槽实心标记 | `#7FC784`／`#767A8A`／`#88ADF7` | —— | 窄条 | `*_LINES_COLOR` ✓（差异视图里的中间槽着色**未逐值核对**，见 §2.3） |

裁剪目视（`x840-1840,y600-800` 与 `x1840-2898,y600-800`）：蓝带里**只有 `powershell` 这个词**被 `#C2D8F2` 覆盖，该行行底是更浅的 `#E7EFFA` ✓。

**第 223 轮订正（§2.2 原表把 `#EDFCED`／`#F4F7F9` 记成"整行软底"）**：这两个值**不是**差异配色。

- `#EDFCED`（原记"增行软底"，213160 px）：第 223 轮逐列复测发现它在 `y738..892` 段**左右两栏同时满宽覆盖**（左 850-1670 与右 1975-2815 各自 820px 全命中）。同一张图里"新增行"的颜色已被量到是 `#BEE6BE`（`y623–661`），一个截图里不可能有两个"新增行底色" ⇒ `#EDFCED` 不是差异行色。它逐字等于配色方案的 `INJECTED_LANGUAGE_FRAGMENT.BACKGROUND`（`DefaultColorSchemesManager.xml:759-765`），且在同一目录的**非差异**截图 `diff-viewer-open.png` 里也覆盖同样这些行 ⇒ 它是**语言注入片段**的底色（README 的 ``` PowerShell ``` 代码围栏），Augit 不做语言服务，**不适用**。
- `#F4F7F9`（原记"删行软底"）：在差异正文两栏内**一个像素都没有**（`y150..1230` 两栏计数全 0），它只等于浅色主题的 `layer-1-bg`（面板/对话框底）⇒ 也是错认。
- 因此第 133 轮"整行软底无权威键、只能取参考实测值"的结论**作废**：整行底就是 `DIFF_*.BACKGROUND`（两档规则见 §2.1），`#E7EFFA` 的逐值吻合正是"修改行取 `getIgnoredColor()`"的验证。

**第 223 轮的落地与遗留**：

| Augit 令牌 | 第 223 轮起（浅/深） | 用在 | 依据 |
| --- | --- | --- | --- |
| `--augit-diff-added` | `#BEE6BE`／`#294436` | `.diff-code-line.added` 整行底 | `DIFF_INSERTED.BACKGROUND`（纯新增块无行内差异 ⇒ 全强度） |
| `--augit-diff-deleted` | `#D6D6D6`／`#484A4A` | `.diff-code-line.removed` 整行底 | `DIFF_DELETED.BACKGROUND` |
| `--augit-diff-modified` | `#E7EFFA`／`#283541` | `.diff-code-line.changed` 整行底 | `getIgnoredColor()` = `mix(DIFF_MODIFIED.BACKGROUND, 编辑器底, 0.6)` |
| `--augit-diff-inline-*` | `#BEE6BE`／`#D6D6D6`／`#C2D8F2` ／ `#294436`／`#484A4A`／`#385570` | `.diff-code-line mark` 行内层 | `DIFF_*.BACKGROUND` 全强度 |
| ~~`--augit-diff-current-*`~~ | **已删除** | ~~`.diff-current` 整行"当前差异"~~ | 权威 `DiffDrawUtil.PaintMode` 无此模式（见 §2.2bis） |

**遗留（见 §2.3）**：差异视图**中间行号槽**的着色（参考图里 `#C2D8F2` 填充与变更连接区梯形）在 Augit 未实现；层归属与几何未逐值核对。

### 2.2bis 当前差异块（`.diff-current`）的处置（第 223 轮：删除）

规格只要求"上一处／下一处差异"**定位**到连续变更块并保留触发按钮焦点（`ux-spec.md:438/439/502`）。权威里**没有**"当前差异"这一层：`DiffDrawUtil.PaintMode` 只有 `DEFAULT`／`IGNORED`／`RESOLVED`（无底 + 点线边框）／`EXCLUDED_*`（`DiffDrawUtil.java:754-783`）。

第 113 轮曾把参考图里 `#BEE6BE` 的一段"整栏宽 590.7"读成"按 Ctrl+D 后当前差异被整块选中"，**第 223 轮复测推翻**：那段是**一行**（`y623–661` ≈ 26 逻辑px，其余同宽行同属这一行），且颜色恰是"无行内差异的纯新增块"的全强度 `DIFF_INSERTED.BACKGROUND`。据此删除该层：`live-data.js` 的 `moveDiffChange` 只做定位，`mockup.css` 删除 3 条规则与 6 处令牌定义。

### 2.3 中间栏（行号槽 ＋ 变更连接区）的权威（第 223 轮登记 → 第 226 轮补齐）

参考图里修改块的左右行号槽（`x1678-1794`／`x1838-1925`）被 `#C2D8F2` 填充，两槽之间还有连接两侧行范围的
**梯形**（`»` 图标在其中）。第 226 轮把两半都读到了，各自出处如下：

| 半 | 权威 | 取值／几何 |
| --- | --- | --- |
| **行号槽填充** | `DiffLineMarkerRenderer.paint()` ＋ `drawMarker()`：`x1 = 0`、`x2 = gutter.width`，用 `getGutterMarkerPaintRange(editor, startLine, endLine)` 求 y 范围，`BackgroundType.DEFAULT` ⇒ `backgroundColor = diffType.getColor(editor)`（`DiffLineMarkerRenderer.kt:34-104`） | **全强度 `DIFF_*.BACKGROUND`**，与"该行是否 ignored（柔和底）"**无关**——柔和/全强度只影响正文行与"槽与正文之间那条窄带" |
| 槽与正文之间的窄带 | 同一函数：`editorMode != gutterMode` 时先画 `whitespaceSeparatorOffset..gutter.width`（用 `editorMode`，即 IGNORED ⇒ `getIgnoredColor()` 的柔和值），再画 `x1..whitespaceSeparatorOffset`（用 `gutterMode` 的全强度） | 这就解释了参考图里 `1927-1972` 是柔和 `#E7EFFA`、而 `1838-1925` 与正文各是 `#C2D8F2` |
| **变更连接区（梯形）** | `DiffDividerDrawUtil.DividerPolygon.paint()` → `drawTrapezium(g, 0, dividerWidth, …)`，`DefaultPainter.getFillColor()` = `correctType(...).getColor(editor)`、`getBorderColor()` = `null`（`DiffDividerDrawUtil.java:305-320,439-535`） | 全强度 `DIFF_*.BACKGROUND`、**无边框**；几何由两侧行范围 `startY1..endY1`／`startY2..endY2` 构成，不等长时按 `withAlignedHeight()` 对齐 |
| 单行增删（空范围） | 同一 `drawMarker()`：`y2 - y1 <= 2` 时不填充，改画 2px 的 `drawChunkBorderLine`（类型色）；`alignedSides` 时不画 | 与"整块填充"是两档 |

**Augit 现状（第 306 轮已实现）**：`.diff-gutter` 在**实时差异**里改为三列 —— 旧行号 ｜ 变更连接区 ｜ 新行号
（`diff-gutter-live`）：逐行两格按**块类型色的全强度** `DIFF_*.BACKGROUND` 填充（块内两侧是否都有行决定
INSERTED／DELETED／MODIFIED），连接区列 14px、用 `clip-path: polygon(...)` 画同色无边框的梯形/尖角；
行高锁定为正文行高并与正文的 8px 内距对齐。**第 306 轮同时修掉一个真实缺陷**：此前实时差异的行号槽
是逐行两个 `<div>` ＋ 每格 8px 内距的自动行，实测行高 85px、与正文 22px 完全错位（静态样例用
`<div>` ＋ `<br>` 的两列结构，靠行内文本流对齐，因此没暴露）。静态样例（`liveDiffView()`）保持原来的
简化两列结构，只有实时差异渲染槽底与连接区。

**用户裁决 2026-09-28（T10）**：取方案 ①「按权威补逐行槽底 + 每块梯形」，第 306 轮实现并断言
（`live-shell` 通过 1451 项断言，其中两条新断言核对槽底色/对齐与连接区几何；负向验证见 `09-icons.md`
的 `nonaginta-sextum`）。


## 3. 变更标记的绘制规则

**【可直接实现】**

- **块外框**：`framingBorder > 0` 且块的起止行不同时，先用 **gutter 背景色**填充 `(x - framingBorder, y - framingBorder, 宽 + framingBorder, 高 + framingBorder*2)` 的矩形，即用背景色在标记外扩出一圈"留白框"，而不是画一条有颜色的边。（来源：`LineStatusMarkerDrawUtil.java:180-190`）
- **填充**：对每个 `y1 != y2` 且未标记为 ignored 的变更，用该类型的颜色填充 `(x1, start, endX, end)` 矩形，**不透明、无 alpha**；x1 在悬停时额外向左加宽 `getHoveredMarkerExtraWidth()`。（来源：`LineStatusMarkerDrawUtil.java:192-203`）
- **边框色**：`LineStatusMarkerColorScheme.getBorderColor()` 取 `BORDER_LINES_COLOR`；expUI 下该键未定义 ⇒ 取到 null。因此**在 expUI 主题下变更块不画有颜色的边框**，"边框"效果来自上一条的背景色外扩。（来源：`LineStatusMarkerColorScheme.kt:36-38`）
- **错误条纹**（编辑器右侧色条）取 `DiffColors.DIFF_*` 属性的 `errorStripeColor`。（来源：`LineStatusMarkerColorScheme.kt:41-48`）

## 4. 未变更区域的分隔条（wave 图案）

**【可直接实现】**

| 参数 | 规则 | 来源 |
| --- | --- | --- |
| 颜色键 | `DIFF_SEPARATOR_WAVE`（常量名 `FOREGROUND`），回落 `DIFF_SEPARATORS_BACKGROUND`，再回落 `Gray._128` | `DiffLineSeparatorRenderer.java:317-322, 351-357` |
| step（四分之一周期） | `max(JBUIScale.scale(Registry.intValue("diff.divider.width")) / 6, 2)`，**每个波 4 个 step** | `:326-329` |
| 半高 | `JBUI.scale(3)` | `:334-336` |
| 垂直偏移 | `(lineHeight - 2 × 半高 - 2 × 1) / 2` | `:338-341` |
| 抗锯齿间隙 | `getAAGap() = 1`（常量） | `:347-349` |
| 起始相位 | `getStepSize()`，注释说明是为了以 45° 向下跨过分隔条、留出更小的转折肩部 | `:343-346` |
| 绘制周期 | 沿 x 循环 `index`，仅当 `index % 4 == 0` 时绘制图案 ⇒ **每 4 个 step 出现一次** | `:260-266` |
| 合成方式 | 图案先画进缓存位图，再用 `AlphaComposite.SrcOver` 贴到目标上 | `:258-266` |
| 描边 | `getStroke(isHovered)` + `GraphicsUtil.setupAAPainting`，开抗锯齿 | `:300-303` |

分隔条宽度本身（两栏/三栏视图的分隔器）取同一个注册表项：`DiffSplitter` 与 `ThreeDiffSplitter` 都用 `JBUIScale.scale(Registry.intValue("diff.divider.width"))`。（来源：`platform/diff-impl/src/com/intellij/diff/tools/util/DiffSplitter.java:47`、`ThreeDiffSplitter.java:145`）

> `diff.divider.width` 的**注册表默认值**未在本次范围内取到（见 §8）。因此上式中除该值以外的所有量都已确定；只要拿到它的默认值即可算出 step。

## 5. 词级差异的机制

**【需推断】**

`TextDiffTypeFactory` 把 `DiffColors.DIFF_INSERTED` / `DIFF_DELETED` / `DIFF_MODIFIED` 注册为三种 `TextDiffType`，供差异查看器标注字词级变化。（来源：`platform/diff-impl/src/com/intellij/diff/util/TextDiffTypeFactory.java:24-26`）

行级底色~~已在 §2.1 取到~~ → **第 133 轮已由 §2.2 的像素实测区分开**：`DIFF_*.BACKGROUND` 是**行内**层；整行软底是另一层、权威无键。因此**行内高亮与整行底色确实不是同一个值**（原判断正确），且行内层不需要再叠 alpha（§2.1 末句）。

## 6. 与 Augit 现状的差异

| 项 | Augit 现状 | New UI 权威 | 处理 |
| --- | --- | --- | --- |
| 新增行 | `success` 绿 | `ADDED_LINES_COLOR` 绿 | 值接近但需换成权威色 |
| 删除行 | `danger` 红 | `DELETED_LINES_COLOR` **灰** | 色相不同，须改 |
| 修改行 | `warning` 黄 | `MODIFIED_LINES_COLOR` **蓝** | 色相不同，须改 |
| 仅空白差异 | 无独立令牌 | `WHITESPACES_MODIFIED_LINES_COLOR` | 需新增 |
| 未变更折叠分隔底 | 无独立令牌 | `DIFF_SEPARATORS_BACKGROUND`（浅 `#E4E6EB` / 深 `#2B2D30`） | 需新增 |
| 忽略空白时的行边框 | 未区分 | `IGNORED_*_BORDER_COLOR`（三色，与行色同值） | 需新增 |
| 文件状态色 | 无独立令牌 | `FILESTATUS_*`（见数值参考 §5.7） | 需新增 |
| 变更块边框 | 无 | expUI 下 `BORDER_LINES_COLOR` 未定义 ⇒ **不画彩色边框**，用背景色外扩 framing | 实现为背景色外扩，不要画边框线 |

**实现建议**：在 `mockup.css` 顶部两个令牌块中新增 `--augit-diff-added`、`--augit-diff-deleted`、`--augit-diff-modified`、`--augit-diff-whitespace`、`--augit-diff-separator`、`--augit-file-added`、`--augit-file-modified`、`--augit-file-deleted`、`--augit-file-conflict`，浅深各一套，再把现有用 `success`／`danger`／`warning` 近似的 Diff 与 Changes 样式改到这些令牌上。**注意不要改动 `--augit-green`／`--augit-red`／`--augit-orange` 的语义**——它们服务于非 Diff 场景（例如冲突解决器、危险动作），与 Diff 行色不是同一个色板。

## 7. 未收集

| 缺口 | 说明 |
| --- | --- |
| 并排视图的行对齐与空白填充区颜色 | 未收集 |
| 差异查看器工具栏与文件信息行的几何 | 未收集；Augit 现有实现见 design-system.md §8.4 |
| `MergeConflictType.Type` 的完整枚举值 | 本次未定位到定义文件；`MergeThreesideViewer.java:384` 可见其中至少有两种（`MODIFIED_DELETED`、`DELETED_MODIFIED`） |
| 行内差异的最终着色 | **第 223 轮定案**：行内层取 `DIFF_*.BACKGROUND` 全强度（不叠 alpha）；整行底按"该块有无行内差异"分两档（见 §2.1／§2.2） |

## 7bis. 三栏合并与冲突解决的渲染规则

本节把原先列在"未收集"里的三栏合并规则补齐，取值与出处如下。

### 7bis.1 分隔条宽度与 wave 的 step（补齐 §4 的缺口）

| 项 | 值 | 出处 |
| --- | --- | --- |
| `diff.divider.width` 注册表默认值 | **`24`** | `platform/util/resources/misc/registry.properties:1073` |
| 由它推出的 step | `max(scale(24) / 6, 2)` = **`4`**（100% 缩放） | `platform/diff-impl/src/com/intellij/diff/util/DiffLineSeparatorRenderer.java:326-329` |

即 §4 里"分隔条图案每 4 个 step 一个周期"在默认缩放下周期为 16px。

### 7bis.2 三栏的行标记：**操作不存在就等于不显示**

这是"不显示无效动作"的权威实现方式——不是把动作禁用，而是**根本不创建它**。

`ThreesideMergeHighlighters.installOperations()` 按固定顺序安装 6 个 gutter 操作（来源：`platform/diff-impl/src/com/intellij/diff/merge/ThreesideMergeHighlighters.kt:44-56`）：

1. `createResolveOperation()`（挂在 `ThreeSide.BASE`）
2. `createAcceptOperation(LEFT, APPLY)`
3. `createAcceptOperation(LEFT, IGNORE)`
4. `createAcceptOperation(RIGHT, APPLY)`
5. `createAcceptOperation(RIGHT, IGNORE)`
6. `createResetOperation()`

隐藏条件（同一文件）：

| 规则 | 条件 | 出处 |
| --- | --- | --- |
| 该侧已解决就不创建操作 | `createOperation` 在 `change.isResolved(side)` 时 `return null` | `:58-66` |
| 该侧不是变更就不显示应用/忽略箭头 | `createAcceptOperation` 的渲染器里 `if (!change.isChange(versionSide)) return null` | `:80-83` |
| 应用箭头再查一次是否已解决 | `createApplyRenderer` 里 `if (change.isResolved(side)) return null` | `:132` |
| 重置操作**只在 AI 已解决时**出现 | `if (!change.isResolved \|\| !change.isResolvedWithAI) return null` | `:95-97` |

最后一条对 Augit 有直接含义：**产品规格明确不接入 AI，所以"重置变化"这个动作在 Augit 里永不出现**——与规格"不显示无效动作"一致，不需要移植。

合并视图的 gutter 标记外框宽度是 `JBUIScale.scale(2)`（`MergeThreesideLineStatusMarkerRenderer.kt:57`），比两侧 diff 视图更宽。

### 7bis.3 每侧高亮器的安装规则与状态

`DiffViewerHighlighters` 按"基准栏始终、左右两栏仅在该侧确有变更时"安装（来源：`platform/diff-impl/src/com/intellij/diff/tools/simple/DiffViewerHighlighters.kt:34-46,113-129`）：

| 项 | 规则 |
| --- | --- |
| 安装范围 | `BASE` 恒装；`LEFT` 仅当 `change.isChange(Side.LEFT)`；`RIGHT` 同理 |
| 行内高亮器 | 同样的安装范围 |
| `resolved` | `change.isResolved(side)` |
| `ignored` | `!resolved && innerFragments != null`——**"忽略"的判据是"未解决且存在行内差异"，不是另一个独立状态** |
| 隐藏条纹标记 | `withHideStripeMarkers(side == ThreeSide.BASE)` |
| 无行号时的特殊隐藏 | `side == BASE && !isChange(LEFT) && isChange(RIGHT)` |

### 7bis.4 行内（词级）差异

- 数据：`MergeInnerDifferences` 按 side 保存三组 `TextRange`（left/base/right），`get(side)` 取对应一组（`platform/diff-impl/src/com/intellij/diff/tools/util/text/MergeInnerDifferences.java`）。
- 绘制：对每组范围调用 `DiffDrawUtil.createInlineHighlighter(editor, innerStart, innerEnd, change.diffType)`，偏移基于该侧变更块的起始行（`DiffViewerHighlighters.kt:100-108`）。
- 着色来源：`InlineHighlighterBuilder.done()` 取 `getTextAttributes(type, editor, BackgroundType.DEFAULT)`，即**沿用该变更类型自己的背景属性**，不是另设一套色（`platform/diff-impl/src/com/intellij/diff/util/DiffDrawUtil.java:668-669`）。
- **第 223 轮定案**：行内色取 `change.diffType` 的 `DIFF_*.BACKGROUND` **全强度、不叠 alpha**（`getTextAttributes(type, editor, BackgroundType.DEFAULT)` → `DiffTextAttributes.getBackgroundColor()`，`DiffDrawUtil.java:668-669,847-863`）；同一块的**整行**底则因为 `ignored = innerFragments != null` 而取 `getIgnoredColor()` 的柔和值 ⇒ "行内全强度 + 行底柔和"的深浅关系来自这两条不同分支，不是 alpha 叠加（见 §2.1）。

### 7bis.5 合并动作组与文案

`MergeThreesideViewer` 的动作组构成（来源：`platform/diff-impl/src/com/intellij/diff/merge/MergeThreesideViewer.java:229-238`）：

1. `ShowDiffWithBaseAction` × 3（分别对应 LEFT / BASE / RIGHT，即"与基准比较"）
2. 分隔线
3. `ApplyNonConflictsAction` × 3，文案 key 分别为 `action.merge.apply.non.conflicts.left.text`、`...all.text`、`...right.text`（"应用非冲突变更到左／全部／右"）

接受动作的按钮文案 key：`button.merge.resolve.accept.left` / `button.merge.resolve.accept.right`（同文件 `:335-336`）。

动作启用判据（来源：`platform/diff-impl/src/com/intellij/diff/merge/MergeThreesideViewerActions.kt`）：

| 动作 | 判据 | 出处 |
| --- | --- | --- |
| 接受某一侧 | `!change.isResolved(side)` | `:180` |
| 忽略某一侧 | `!change.isResolved(side)` | `:112` |
| 忽略整块 | `!change.isResolved` | `:124` |
| 重置（AI） | `change.isResolvedWithAI` | `:156` |

即：**每一侧的可用性只由「该侧是否已解决」决定**，不含其它条件。

### 7bis.6 对 Augit 的实现含义

- Augit 的三栏冲突解决器应把"接受本侧／接受对方"实现为**按侧独立解析**（`isResolved(side)`），而不是一个整体状态；这直接决定箭头/动作的显示与隐藏。
- "不显示无效动作"应实现为**不渲染**，与参考实现一致（而非渲染成禁用态）。
- 基准栏（BASE）不显示条纹标记；左右两栏只在实际有变更时才有相应高亮与动作。
- 不移植"重置变化"（仅 AI 场景）。
- 行内词级差异按侧独立提供范围，复用该侧变更类型的颜色。

### 7bis.8 补齐 `MergeConflictType`（本轮收集）

`MergeConflictType.Type` 的定义文件已定位：`platform/util/diff/src/com/intellij/diff/util/MergeConflictType.kt:33`，完整枚举是**四个值**（不是先前推测的 `MODIFIED_DELETED`／`DELETED_MODIFIED` 这类复合名）：

| 项 | 值 | 出处 |
| --- | --- | --- |
| `Type` | `INSERTED`、`DELETED`、`MODIFIED`、`CONFLICT` | `MergeConflictType.kt:33-35` |
| `resolutionStrategy` | `MergeConflictResolutionStrategy?`，默认 `DEFAULT`；构造重载 `canBeResolved: Boolean` 映射为 `DEFAULT` 或 `null` | `:12-15` |
| `canBeResolved()` | `resolutionStrategy != null` | `:17-19` |
| `isChange(side: Side)` | 返回该侧的变更标志（左/右各一个） | `:21-23` |
| `isChange(side: ThreeSide)` | `LEFT`／`RIGHT` 取各自的标志，**`BASE` 恒为 `true`** | `:25-31` |

`MergeConflictResolutionStrategy`（`platform/util/diff/src/com/intellij/diff/util/MergeConflictResolutionStrategy.kt`）是三个值，且**注释即语义**：

- `DEFAULT` —— *"Only available when there is no conflict"*，即只在无冲突时可用；
- `TEXT` —— 用词级不重叠来化解冲突（`ComparisonMergeUtil`）；
- `SEMANTIC` —— 用文件结构化解（`LangSpecificMergeConflictResolver`）。

**对 Augit 的含义**：`TEXT`／`SEMANTIC` 是参考实现的智能化解能力，Augit 不接入 AI、只做按侧接受，因此**两者都不移植**；`DEFAULT` 这条"无冲突才可用"也只是解释了参考实现里动作可用性的来源。真正可照搬的是 `isChange(side)` 这个**门控**——它正是 §7bis.2 里"该侧不是变更就不创建应用/忽略箭头"的判据（`:80-83`），而 `BASE` 恒 `true` 也解释了基准栏为什么总有动作位。

### 7bis.9 本节新增的两处待定映射（不自行选择解释）

| 冲突 | 权威 | 现行规范/实现 | 待定原因 |
| --- | --- | --- | --- |
| `isChange(side)` 的门控怎么落到 Augit 的底部按钮 | 参考实现按侧**不创建**"应用/忽略"箭头（`ThreesideMergeHighlighters.kt:80-83`）；`isChange(ThreeSide.BASE)` 恒 `true` | 规范 `design-system.md:571` 写"提供接受左侧、两侧或右侧的动作"，未规定按侧隐藏；Augit 的 `live.conflict` 模型也**没有暴露每侧是否变更** | 落地需要模型新增每侧变更标志，且要先定"左侧未变更时是否隐藏『接受左侧』"，属产品行为 |
| 三栏初始比例 | `ThreeDiffSplitter.resetProportions()`：`myProportion1 = myProportion2 = 1f / 3`，即**严格等分**（`platform/diff-impl/src/com/intellij/diff/tools/util/ThreeDiffSplitter.java:66-67`） | 规范 `design-system.md:584` 明确登记"三栏可用宽度按 `1 : 1.08 : 1`"（`.conflict-page` 的 `grid-template-columns` 同值） | 规范条文与权威相冲突；且这是已登记的布局值，按项目规则不由本轮自决 |

### 7bis.7 本节仍未覆盖

| 缺口 | 说明 |
| --- | --- |
| 冲突块在三栏之间的**连线/对应关系绘制** | 未收集 |
| CONTINUE / SKIP / ABORT 三动作的文案与状态判据 | 参考实现里这三个词无硬编码来源（走 `MERGE_ACTION_CAPTIONS` 钩子，见分册 04），须由 Augit 产品规格定义；可照搬的只有"按 Git 操作状态决定可用性"的机制 |
| 三栏视图的分隔条**拖动**行为 | 初始比例已收（见 §7bis.9），拖动时的夹紧与持久化仍未收集 |

## 8. 已落地实现（模块 5；第 223 轮按 §2.1／§2.2 订正 Diff 行色与新增行内层）

| 令牌 | 浅色 | 深色 | 来源 |
| --- | --- | --- | --- |
| `--augit-diff-added` | `#BEE6BE` | `#294436` | `DIFF_INSERTED.BACKGROUND` 全强度（纯新增块无行内差异，§2.1 两档规则） |
| `--augit-diff-deleted` | `#D6D6D6` | `#484A4A` | `DIFF_DELETED.BACKGROUND` 全强度 |
| `--augit-diff-modified` | `#E7EFFA` | `#283541` | `getIgnoredColor()` = `mix(DIFF_MODIFIED.BACKGROUND, 编辑器底, 0.6)` |
| `--augit-diff-inline-added` | `#BEE6BE` | `#294436` | 行内层 `DIFF_*.BACKGROUND` 全强度 |
| `--augit-diff-inline-deleted` | `#D6D6D6` | `#484A4A` | 同上 |
| `--augit-diff-inline-modified` | `#C2D8F2` | `#385570` | 同上（浅色由 expUI 覆写） |
| `--augit-diff-whitespace` | `#F7E2CB` | `#52433D` | `WHITESPACES_MODIFIED_LINES_COLOR` |
| `--augit-diff-separator` | `#E4E6EB` | `#2B2D30` | `DIFF_SEPARATORS_BACKGROUND`（废弃回退键，见 §4） |
| `--augit-file-added` | `#067D17` | `#73BD79` | `FILESTATUS_ADDED` |
| `--augit-file-modified` | `#0033B3` | `#70AEFF` | `FILESTATUS_MODIFIED` |
| `--augit-file-deleted` | `#6C707E` | `#6F737A` | `FILESTATUS_DELETED` |
| `--augit-file-conflict` | `#DE1B2E` | `#DE6A66` | `FILESTATUS_IDEA_FILESTATUS_MERGED_WITH_CONFLICTS` |
| `--augit-status-clean` | `#1F7536` | `#57965C` | `VersionControl.Merge.Status.NoConflicts.foreground`（Green2 / Green6） |

接线：`.diff-code-line.added/.removed/.changed`（整行底）、`.diff-code-line.<kind> mark`（行内层，`mockup.js` 的 `marked()` 用宿主 `oldChanges`／`newChanges` 包 `<mark>`）与 `.file-status-added/.file-status-modified/.file-status-deleted`。**新增了此前缺失的 `.file-status-added` 与 `.file-status-deleted` 两条规则**——这两个类名由 JS 产出（各 6 处）但原先没有颜色规则，靠继承。

`--augit-green` / `--augit-red` / `--augit-orange` **保持不变**：它们服务冲突解决器、内联告警与危险动作等非 Diff 场景，与 Diff 色板不是同一套。

**注意** `.file-status-new` 不是文件状态，而是 Worktree 面板"干净，可安全移除"这类正向状态文字，因此映射到 `NoConflicts.foreground` 的绿，而不是 `FILESTATUS_UNKNOWN`。

浏览器实测（`tools/audit/check-diff-inline.test.cjs`，两主题 × 三种行 + 行内层，共 29 项断言）：

| 元素 | 浅色 | 深色 |
| --- | --- | --- |
| `.diff-code-line.added` / `… mark` | `rgb(190,230,190)` = `#BEE6BE` | `rgb(41,68,54)` = `#294436` |
| `.diff-code-line.removed` / `… mark` | `rgb(214,214,214)` = `#D6D6D6` | `rgb(72,74,74)` = `#484A4A` |
| `.diff-code-line.changed`（整行底 / `mark`） | `rgb(231,239,250)` = `#E7EFFA` ／ `rgb(194,216,242)` = `#C2D8F2` | `rgb(40,53,65)` = `#283541` ／ `rgb(56,85,112)` = `#385570` |
| `.file-status-modified` | `rgb(0,51,179)` = `#0033B3` | `rgb(112,174,255)` = `#70AEFF` |

整行底的 `.diff-code-line.changed` 在视觉稿 `commit-diff.html` 里没有对应数据行，检查器用注入元素验证；`mockup.js` 的 `cssKind()` 会把 `Modified` 映射为 `changed`，实时侧由 `live-shell.spec.cjs` 的 `src/Modified.cs` 场景覆盖。

## 8. 标注说明

- **【可直接实现】**：§3、§4 的规则是几何与常量，HTML/CSS 可表达。
- **【Swing 特有】**：`Graphics2D` 的 `AlphaComposite`、`getStroke`、`GraphicsUtil.setupAAPainting` 是 Swing 绘制细节；在 HTML 侧等价物是 `opacity`／`color-mix` 与 SVG/canvas 描边。
- **【需推断】**：§5 的词级高亮底色。

## 12. Diff 工具栏的顺序与「忽略空白」（第 248 轮）

`ux-spec` §7.7 第 5 条要求工具栏「左侧依次为上一处、下一处、搜索、上一个文件、文件计数和下一个文件；
右侧为差异摘要、忽略空白、双栏/单栏和设置。文件箭头围绕文件计数分组，键盘 Tab 顺序与视觉顺序一致」。
第 248 轮核对时发现**实时外壳与规格是两套工具条**：live 的模板只有
`上一处差异／下一处差异／{文件箭头与计数}／N 行／双栏·单栏` —— 缺「查找」「忽略空白」「设置」，右侧还是正文行数。

**落地**：

- `web/src/mockup.js` 新增 `liveDiffToolbar({ fileNav, arrowsDisabled, summary, busy })`，
  就绪／加载／空差异三态共用同一份规格顺序；差异摘要在渲染时按「连续变更行算一块」从 `diff.rows` 现算
  （与 `live-data.js` 的 `diffChangeBlocks()` 同一口径，双栏不重复计数）；
- `web/src/live-data.js`：`loadDiff()` 的 `ignoreWhitespace` 改成"调用方没显式传时沿用 `live.diffOptions`"
  （否则切显示模式／切相邻文件／外部刷新三条重载路径都会丢掉这个会话选项）；
  新增「忽略空白」点击处理（切换 `live.diffOptions.ignoreWhitespace`、写 `aria-pressed`/`active`、强制重查）
  与「设置」点击处理（按应用既有约定打开设置对话框，与提交框的「提交设置」同一入口）；
- **「查找」在 Diff 正文上尚未实现**：共享查找条 `current-find.js` 只服务 `.document-view > .code-view`，
  因此按项目既有做法把它渲染在规格位置但**禁用并写明原因**（`title="当前版本暂不支持在 Diff 正文中查找"`），
  不留死入口；视觉稿里该按钮同样没有行为。是否投入 Diff 正文查找待用户口径。

实测（dark，工作区 Diff `src/App.cs`）：按钮 DOM 顺序 = 规格九项；文件计数 `1/3 个文件` 夹在两个文件箭头之间；
真实 Tab 序列 `下一处差异 → 上一个文件 → 下一个文件 → 忽略空白 → 双栏 → 单栏 → 设置`；
差异摘要 `src/App.cs` 1 处、`src/Modified.cs` 2 处（与 `data-diff-total` 一致）；
加载态同序并追加「取消比较」，差异箭头与「查找」禁用。

## 13. 跨文件查询的加载态与显示模式切换（第 249 轮）

两条规格条款（`ux-spec` §7.7 第 9、6 条）暴露了三处实现落差：

1. **加载分支禁用错了箭头**。§7.7 第 9 条要求「跨文件查询/排版期间**禁用差异箭头**，
   **文件箭头和 Changes 仍允许改选**」，而 `liveDiffView` 的加载分支用的是 `fileNavBusy`（文件箭头 `disabled`）、
   差异箭头反而可用。现改为只把 `arrowsDisabled` 交给上一处/下一处，`fileNav` 原样保留。
2. **文件切换路径没有加载反馈**。`moveDiffFile()`（「上一个文件／下一个文件」）直接 `loadDiff(path)`，
   从不 `scheduleDiffLoadingMarker()` ⇒ `live.diffLoading` 永远为假，加载分支在工作区 Diff 的文件切换上不可达。
   现在切换前后调度/清除标记（超过 §6.5 的 150ms 阈值才显示，短查询不闪）。
3. **显示模式切换丢掉比较上下文**。`switchDiffMode()` 只传 `mode` 重新 `loadDiff()`，
   丢掉 revision／commit／ignoreWhitespace／version ⇒ **历史比较切单栏后 `live.diff` 变成 null、
   编辑区退回视觉稿样例数据**（实测工具条出现 `1/42 个文件` 与 `1 处差异，0 个已包含`、正文是样例 XML，
   再切回双栏也回不来）。现在 `loadDiff()` 记住 `live.diffParts`，`switchDiffMode()` 带上它 ——
   单双栏命中同一份补丁缓存（`diffPatchKey` 不含 mode），既不重查 Git 也不丢上下文。

实测（`diff-boundary` 场景把切相邻文件的查询拖到 2.5 秒）：在途时差异箭头禁用并写明原因、
文件箭头与忽略空白/双栏/单栏/设置仍可用、Changes 仍可勾选与改选；查询结束后落到相邻文件、
计数 `2/3 个文件`、勾选保留、标签数不变、正文区/轨道/侧栏/标签条矩形逐值不变、不恢复旧定位
（新正文 `data-diff-index` 为空）。

历史与引用比较的块规则用两个桩旋钮（`__historyCompareBlocks`、`__refCompareBlocks`）返回
"两处被上下文隔开的 `Modified`"来核对：测试独立复算 DOM 里的连续变更块数（分栏取最后一栏、
单栏取 `.diff-columns` 的直接子行），两条视图都是 2 块＝摘要 `2 处差异`、导航 `data-diff-total === "2"`。

## 14. 等宽字号重排与改动工具窗的「预览」入口（第 250 轮）

- **改等宽字号只重排正文与行号**（§7.7 第 12 条）：字号 13 → 20 时 `--code-line-height` 22 → 34px（= codeSize × 1.7）、
  正文行盒 22 → 34、行号中栏按新字体重新度量 93 → 135（`measureCodeViews()` 的 `max(84, 最长行号文本宽 + 14)`），
  行留白 13px 与槽内距 7px 是设计常量；`git/diff` 请求数与文件读取数都不增加，草稿/选中/勾选/当前正文与行数都不丢。
- **改动工具窗工具栏的「预览」接线**（§7.7 第 14 条）：Markdown 与 JSON 的默认 Git 页面仍是**磁盘真实文本 diff**
  （Live 实测：正文里是源标记 `**加粗**`，没有渲染后的 `<strong>`、也没有 `.markdown-preview`），
  修改后的预览从工具栏打开。此前该按钮在实时外壳里**没有任何处理者**（点击后编辑区仍是差异、读取次数 0、
  连"未接线"都不记录）——现在按选中的改动文件打开只读文档：Markdown 显式切到**预览**模式
  （与文档工具栏的原文／对照／预览同一套模式状态），JSON 不用额外设置（`liveJsonDocument()` 默认即格式化视图，
  无效 JSON 时才回落原文）。

## 15. 文件栏的单栏排列与边界提示的撤销矩阵（第 251 轮）

- **单栏文件栏上下排列**（§7.7 第 7 条）：`mockup.css` 的
  `.diff-layout[data-diff-mode="unified"] .reference-filebar` 把文件栏改成单列两行（来源在上、目标在下），
  `.reference-after` 落到第二行；实测单栏下两者左边缘相同、目标顶边 ≥ 来源底边、宽度同为整宽，
  双栏下并排同高且文件栏中栏与正文行号中栏逐值相同。两种模式下来源都是 `HEAD`（基准）、目标是 `工作区`（当前），
  路径节点在来源一侧，两侧各一个只读锁。
- **边界提示的撤销矩阵**（§7.7 第 10 条）：`clearDiffBoundaryHint()` 统一撤销 `live.diffBoundaryHint`（只清状态不算撤销，
  DOM 会被重绘抹掉但状态会留到下一次点击），接到 `Esc`（只在真有提示时消费）、`loadDiff`（覆盖切模式／切文件／外部重载）、
  `selectChangeRow`（选文件）、`applyRailAction` 与窗口 `resize`（改布局）、`activateTab`（隐藏 Diff）、`closeDiff`（关闭）。
  撤销后再按同方向必须**重新走两段式**。
- **首/尾局部说明**：列表没有相邻文件时，不再静默清掉提示，而是在同一提示位显示
  「已到改动列表的首个文件」／「已到改动列表的最后一个文件」，并且不回卷整个文件列表。
- **样例提示不再进实时正文**：`diff-boundary` 场景的样例提示节点此前会被注入实时外壳
  （`live.diffBoundaryHint` 为 null 时 DOM 里仍有一条提示，直到第一次交互才清）—— 现在只在静态视觉稿注入。
- **一处待口径观察**：提示按视觉稿的 `position: absolute` 贴在**内容**顶部（偏移父级是滚动容器），
  长差异滚到边界块后提示可能落在可视区之外；视觉稿场景是短差异，未覆盖该情形。

## 比较区的键盘回路与"差异总数只在比较工具栏"（第 261 轮）

- **正文 → 标签栏**：比较区正文是可聚焦的正文位置（第 260 轮）。**正向** Tab 从正文先进下方的底部工具窗
  （参考实现的引用树有上百行，正向要把它走完才回到标签栏）；**反向** `Shift+Tab` 是相邻的：
  沿可见顺序经过比较工具栏（`设置→单栏→双栏→忽略空白→…`，都还在编辑器内容区内），第 9 步到达标签栏的
  `标签选项`。断言按反向取证并写成"沿可见顺序回到标签栏"。
- **差异总数**：连续两次「下一处差异」前后全局状态栏文本逐字不变（`live-ws只读`），摘要仍显示 `1 处差异`
  —— 导航只动比较区，不往全局状态栏写东西。

## 比较工具栏与文件信息区的行高扩展（第 264 轮）

工具栏与文件栏的高度来自界面字体的实测文本高（`mockup.js` 的
`length("diff-toolbar-height", Math.max(39, height + 12))`、`length("diff-filebar-height", Math.max(31, height + 12))`）。
实测三档界面字号（13／26／40）：工具栏／文件栏高 39/31 → 45/45 → 63/63，且**正文顶边 − 布局顶边 = 工具栏高 + 文件栏高**
三档都成立 ⇒ 字号变大只增高这两行、正文整体下移；图标 16×16、按钮命中区 27×27、按钮顺序与摘要提示在窄到
900px（编辑区 507px）时逐项不变。

## 差异请求键与显示模式（第 265 轮）

- **显示模式不进请求键/补丁键**：请求键与补丁键只取内容维度（路径/基准/提交/忽略空白/重命名/版本）。
  此前 `mode` 也在键里 ⇒ 加载期间切模式会并发发出第二次请求、补丁按模式各存一份（规格 §6.3 要求
  "查询期间切换显示模式只改变最终呈现方式"）。
- **响应不回写 `live.diffMode`**：显示模式是用户状态，由 `switchDiffMode()` 设置；响应只提供内容。
  此前 `loadDiff()` 写回 `parts.mode`，加载期间切模式会被响应吞掉（实测切完仍是双栏）。
- **重复点击当前模式不排版不查询**：视觉稿的模式按钮处理器此前在"模式没变"时仍回调外壳刷新 ⇒ 白排一次版。

## 引用比较的「取消比较」入口（第 266 轮）

规格 §7.9 第二十条：历史面板发起引用查询时"在自身区域显示'取消比较'、不写全局'正在生成 diff'提示、完成后隐藏入口"。
实时侧此前没有这个入口（视觉稿样例里那个 `<a href="history-diff-cancelled.html">` 点了会离开应用）。
现在 `liveBranchCompareTool()` 在 `compare.loading` 时于底部面板头部渲染 `取消比较`
（`data-branch-compare-cancel` + `aria-label`），列表区三态各有文案（正在读取／比较已取消／无独有提交）；
`live-data.js` 新增代际令牌 `branchComparisonToken`：取消时前进令牌并把状态置为
`{ loading: false, cancelled: true, commits: [] }`，晚到的响应据此作废（不关面板、可重试、不写全局提示）。

## 比较标签的三部分与文件栏的完整引用（第 267 轮）

- **标签三部分**（规格 §7.9 第十七条）：文件名 / 来源引用 / 目标引用各自一个 span，各自
  `overflow:hidden` + `text-overflow:ellipsis`。实时标签条此前把整串标题当纯文本渲染 ⇒ 长文件名会把两侧引用一起挤掉；
  现在 `live-data.js` 建结构化部件（`comparisonParts()`、`shortReference()`），`mockup.js` 按部件渲染。
- **前 8 位与祖先后缀**：40/64 位哈希显示前 8 位并保留 `[~^]` 后缀；命名引用保持原名（`dsh` 不截断）。
- **文件栏的双方引用**：`live.referenceComparison` 记下"与工作区比较"实际查询用的修订 —— 此前文件栏写死
  `HEAD → 工作区`，与查询不符。悬停说明保留**完整**引用 + 相对路径（显示值仍可只取前 8 位），
  实际 `git/diff` 查询始终用完整原值。

## 文件信息区的双栏左右 / 单栏上下（第 268 轮）

`diffFileHeader()` 同一份标记在两种模式下由 CSS 排布：
双栏时来源（含相对路径）在左、目标在右；单栏时文件栏变高（31 → 55），来源与目标**上下两行**
（顶部一行来源与路径、第二行目标）。失败/摘要态（0 行差异 + `.comparison-notice`）按**当前所选模式**
显示双方身份；查询与排版期间"模式 ↔ 文件栏布局"始终一致（不会半切）；文件栏没有可聚焦元素、
也不进入 Tab 顺序（从工具栏连按 12 次 Tab 不会落进文件栏）。
