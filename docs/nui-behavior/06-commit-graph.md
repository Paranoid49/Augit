# 06 提交图：轨道分配、配色与状态

本节要点：

1. **轨道（lane）不是全局固定的**：`x = elementWidth × lane + elementWidth/2`，而 `lane` 是「该行可见元素列表排序后的下标」，逐行重新紧压编号；全局只有 `layoutIndex`（片段号）是稳定的，它决定颜色与左右顺序。
2. **`layoutIndex` 由一次「从各 head 出发、始终优先走第一个未分配父提交」的 DFS 分配**，每到一个首次访问的叶子就推进计数器；这是「分叉展开 / 合流收敛」和同分支颜色稳定的根源。
3. 图形区宽度是**逐行**算出来的（`max(maxLane+1, min(6, recommendedWidth)) × elementWidth + graphTextGap`），提交标题用 `appendTextPadding(graphWidth)` 顶到图形区右侧，因此标题起点跟随该行图形区宽度。
4. 选中不等于行选中：`isSelected` 的打印元素用「先黑粗线/黑大圆、再本色细线」画成黑描边；行选中是表格底色，两者是两套机制。
5. 筛选/折叠不改 `layoutIndex`，只隐藏提交并把最近可见节点用 `DOTTED` 边直连；分页不是增量拼接，而是用更大的 `CommitCountStage` 在同一永久图上重算可见图。

分类标记约定：

- **【可实现】** 静态取值、公式、布局算法，可直接照做。
- **【Swing】** 依赖 Swing / IntelliJ 平台机制的交互与绘制管线，Web 层需要等价重写，不能照抄 API。
- **【需推断】** 源码未直接给出、需要按截图或试验补齐的部分。

取证锚点：`/mnt/d/github/intellij-community`，commit `576e328`，`build.txt` = `263.SNAPSHOT`。几何常量与主题色已另见 `docs/intellij-platform-ui-reference.md` §5、§6，本节不重复罗列色值表，只记录算法与状态差异。

---

## 1. 数据模型与三层索引

- **【可实现】** 提交历史只有一张表，**每一行恰好是一个可见提交**，`rowCount = visibleGraph.visibleCommitCount`，表格行与可见图行当前是恒等映射（`fromGraphToTableRow/fromTableToGraphRow` 返回原值，只是为将来插入非提交行留的钩子）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphTableModel.kt:46-48,143-145）
- **【可实现】** 三层索引不能混淆：① 表格行（`VcsLogTableIndex`）② 可见图行（`VcsLogVisibleGraphIndex`）③ 永久图 nodeId（≥0 为已加载、≤-2 为「未加载父提交」的合成 id、-1 为未知）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphTableModel.kt:31-34；platform/vcs-log/impl/src/com/intellij/vcs/log/graph/impl/permanent/PermanentCommitsInfoImpl.kt:30-48）
- **【可实现】** 行 → 提交：可见图行取 `compiledGraph.getNodeId(row)` 得到永久图 nodeId，再 `permanentCommitsInfo.getCommitId(nodeId)`；提交 → 行：`compiledGraph.getNodeIndex(nodeId)`，不可见返回 null。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:44-53）
- **【可实现】** 打印元素（PrintElement）是最小绘制单元，每个元素携带 `rowIndex`、`positionInCurrentRow`（本行 lane）、`colorId`、`isSelected`；边元素另有 `positionInOtherRow`、`type`（UP/DOWN）、`lineStyle`、`hasArrow()`。（来源：platform/vcs-log/graph-api/src/com/intellij/vcs/log/graph/PrintElement.kt:7-12；platform/vcs-log/graph-api/src/com/intellij/vcs/log/graph/EdgePrintElement.kt:4-20）
- **【可实现】** 节点打印元素的 `nodeType` 有三种：`FILL`（实心圆，默认）、`OUTLINE`（只描边）、`OUTLINE_AND_FILL`（HEAD，三同心圆）。（来源：platform/vcs-log/graph-api/src/com/intellij/vcs/log/graph/NodePrintElement.java:7-16）

---

## 2. 轨道分配算法

### 2.1 输入与输出

- **【可实现】** **输入**：`commits[0..n-1]`，行号即下标，已满足「任何提交都排在其所有父提交之前」的拓扑序，并在同层内按时间倒序（子提交在前）。插入规则：对每个提交找一个插入下标，遇到「列表中有它的父提交」或「列表中的提交时间更早」即停，从而保证子先于父。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/data/VcsLogJoiner.java:283-324）
- **【可实现】** **输入**：每个提交的有序父列表 `parents(i)`（第一父在前，顺序有意义）。**输出**：① 每个节点的 `layoutIndex`（≥1，全局稳定）；② 每一行的元素列表与各自的 `lane`（本行 x 槽位）；③ 由节点/边派生的打印元素（含每个端点在相邻行的 lane）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/GraphLayoutBuilder.kt:58-83；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:126-173）

### 2.2 步骤 0：父提交去重

- **【可实现】** 建图前先删掉 `parents(i)` 中的重复项，保持首次出现顺序（两个父相同时也合并为一个）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/DuplicateParentFixer.java:60-85）

### 2.3 步骤 1：建永久线性图（隐式相邻边 + 长边表）

- **【可实现】** 若某提交只有 1 个父，且该父正好是**下一行**的提交，则不存边，改用「相邻行隐式直连」（画图时视为一条连到下一行同槽的竖线）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/PermanentLinearGraphBuilder.java:55-67；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/PermanentLinearGraphImpl.java:44-45,59）
- **【可实现】** 其余父提交都作为长边存下（`up = 当前行`、`down = 父所在行`）；父提交不在已加载集合中时，边记成 `NOT_LOAD_COMMIT` 特殊边（`targetId` 为负的合成 id）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/PermanentLinearGraphImpl.java:47-57；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/PermanentLinearGraphBuilder.java:123-129,134-153）
- **【可实现】** 邻边按方向分为：`NORMAL_UP(i)` = 以 i 为下端点（i 的父边，向上看）、`NORMAL_DOWN(i)` = 以 i 为上端点（i 的子边，向下看）；`SPECIAL` 单独一类。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/api/EdgeFilter.java:19-25；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/PermanentLinearGraphImpl.java:41-62）

### 2.4 步骤 2：分配 layoutIndex（片段号 / 分支号）

可复现伪代码（这是「分叉展开、合流收敛、颜色归属」的唯一来源）：

```
# 来源：GraphLayoutBuilder.kt:20-83；Dfs 的显式栈语义见 utils/DfsUtil.kt:14-34
heads = [i | children(i) 为空]             # 没有子提交的行 = 分支尖端
sortedHeads = sort(heads, headComparator)
cur = 1
layoutIndex[0..n-1] = 0                     # 0 = 未分配
headNodes = []
for head in sortedHeads:
    if layoutIndex[head] != 0: continue      # 已被前面的 head 的遍历覆盖
    headNodes.append(head)
    # 显式栈遍历：同一个节点会被反复求值，直到它不再有未分配的父提交
    stack = [head]
    while stack 非空:
        u = stack.top()
        firstVisit = (layoutIndex[u] == 0)
        if firstVisit: layoutIndex[u] = cur
        child = children(u) 中第一个 layoutIndex == 0 的元素   # 只走第一个未分配的父
        if child != null:
            stack.push(child)                 # 深入
        else:
            if firstVisit: cur += 1           # 只有“首次到达即无未分配父”才推进计数器
            stack.pop()                       # 回溯，回到父节点继续找下一个未分配父
```

- **【可实现】** 用仓库自带测试图核对（`0` 的父为 `1,6,3`，`1` 的父为 `7`，`6` 的父为 `7`，`7` 的父为 `8`，`2` 的父为 `3`，`4` 的父为 `5`）：heads 为 `0,2,4`；head `0` 的遍历沿「第一父链」`0→1→7→8` 全部拿到 `layoutIndex = 1`，回溯到 `0` 后下一个未分配父 `6` 拿 `2`、再下一个 `3` 拿 `3`；随后 head `2` 拿到 `4`，head `4`（及其父 `5`）拿到 `5`。即「主链同号、侧链另开号」。（来源：platform/vcs-log/graph/testData/layoutBuilder/manyNodes_in.txt；platform/vcs-log/graph/testData/layoutBuilder/manyNodes_out.txt）

- **【可实现】** `children(u)` 的顺序由建图时的边顺序决定，因此「第一父」优先继承当前 `layoutIndex`，其他父（合并进来的分支）在回溯时才被分配新的 `layoutIndex`。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/GraphLayoutBuilder.kt:69-79；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/PermanentLinearGraphBuilder.java:109-132）
- **【可实现】** 每个节点还能查到「它归属哪个 head」：把各 head 的 `layoutIndex` 排序后用二分查找取「≤ 本节点 layoutIndex 的最大者」（相等时取该 head 本身）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/GraphLayoutImpl.kt:12-26）
- **【需推断】** `headComparator` 依赖各 VCS 的 `VcsLogRefManager.getBranchLayoutComparator()`；Git 的顺序是 `origin/master > 远端分支 > master > 本地分支 > tag > 当前分支 > HEAD > other`，Augit 若只支持 Git，可直接照抄该优先级表。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/graph/HeadCommitsComparator.java:44-71；plugins/git4idea/backend/src/log/GitRefManager.kt:219-230）

### 2.5 步骤 3：逐行求 lane（真正的 x 槽位）

```
# 来源：PrintElementGeneratorImpl.kt:246-268（元素集合）、:175-190（相邻行位置反查）
for r in 0 .. n-1:
    elems = [ GraphNode(r) ]                       # 每行有且只有一个节点
    for e in 所有长边:
        if not (e.up < r < e.down): continue       # 只取“严格穿过本行”的边
        if not visibleInRow(e, r): continue
        elems.append(e)
    # 与相邻行相连的特殊边（未加载父 / 半截虚线）
    elems += { e ∈ special(r-1) | e.up   == r-1 }
    elems += { e ∈ special(r+1) | e.down == r+1 }
    elems.sort(byLayoutIndexComparator)            # 见 2.6
    for i, elem in elems: lane[r][elem] = i        # lane 就是排序后的下标

visibleInRow(e, r):
    span = e.down - e.up
    if span < longEdgeSize: return true
    return min(r - e.up, e.down - r) <= visiblePartSize
```

- **【可实现】** 默认参数（界面属性「显示长边」关闭时）：`longEdgeSize = 30`、`visiblePartSize = 1`、`edgeWithArrowSize = 极大值`；开启后为 `1000 / 250 / 1000`。也就是说跨度 < 30 行的边**每一行都画**；跨度 ≥ 30 行的边只在两端各 1 行内出现。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:44-51,277-281；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:35,147-152；platform/vcs-log/impl/src/com/intellij/vcs/log/impl/VcsLogUiPropertiesImpl.kt:122）
- **【可实现】** 「穿过本行」的判定是**严格**的 `up < r < down`，端点行不含该边；端点行靠 2.7 的「节点的相邻边」分支补画。这一条已用测试数据核对（边 (0,6) 出现在第 1..5 行，(1,6) 出现在第 2..5 行）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/EdgesInRowGenerator.java:35-49,105-122；platform/vcs-log/graph/testData/edgesInRow/manyNodes_out.txt）
- **【可实现】** `EdgesInRowGenerator` 只是 `{e | up < r < down}` 的增量算法（按 40 行分块缓存，向上走一步 = 加上 `NORMAL_UP(r)`、去掉 `NORMAL_DOWN(r-1)`；向下反之），语义与上面的定义式完全一致，Augit 直接用定义式或前缀扫描即可，不必复刻缓存。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/EdgesInRowGenerator.java:56-122）
- **【可实现】** lane 逐行紧压：同一分支在不同行可能占不同 lane，但排序键保证相对左右顺序稳定，这就是 PyCharm 里轨道「贴合」而不留空槽的原因。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:246-268）

### 2.6 排序键（决定左右顺序）

- **【可实现】** 求 lane 用的比较器是「layoutIndex + 行号」绑定的专用比较器，可直接照抄下面这段（`LI(x)` = `layoutIndex[x]`；未加载节点（nodeId < 0）直接用它的负 nodeId 当 `LI`）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/GraphElementComparatorByLayoutIndex.java:26-74；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:59-63）

  ```
  cmp(x, y):                                  # 负值 = x 排在 y 左侧
    两者都是普通边 e1, e2:
        if e1.up == e2.up:
            if e1.down < e2.down: return -cmp2(e2, Node(e1.down))
            else:                 return  cmp2(e1, Node(e2.down))
        if e1.up < e2.up:         return  cmp2(e1, Node(e2.up))
        else:                     return -cmp2(e2, Node(e1.up))
    e1 是特殊边（只有一端）:      return -cmp2(e2, Node(端点(e1)))
    e2 是特殊边（只有一端）:      return  cmp2(e1, Node(端点(e2)))
    x 是边、y 是节点:             return  cmp2(x, y)
    x 是节点、y 是边:             return -cmp2(y, x)
    两者都是节点:                 0（每行只有一个节点，不会发生）

  cmp2(e, node):                              # 边与节点的核心比较
    if e 是特殊边:  return LI(端点(e)) - LI(node)
    m = max(LI(e.up), LI(e.down))
    if m != LI(node): return m - LI(node)      # 按“主导 layoutIndex”排序
    else:             return e.up - node.index # 同片段内：来自上方的边排在节点左侧
  ```

- **【可实现】** 实际效果：同一行里元素按「其主导节点的 layoutIndex」从左到右排列，而**节点的相邻边**（起点/终点就在本行节点的那些边）不参与排序，直接复用节点的 lane。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:139-149）
- **【可实现】** 排序必须是**稳定排序**：节点与「从它出发的第一条边」会返回 0（相等），此时靠插入顺序决定先后（实现里先放节点、后放边，所以节点在同号元素中最靠左）；边的集合本身来自 `Set`，同一行的边之间若有 0 比较结果，其相对顺序不保证。Augit 用数组按「节点、再按边表顺序」构造并稳定排序即可复现。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:252-266；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/GraphElementComparatorByLayoutIndex.java:54-55）

### 2.7 打印元素生成（端点行如何补画）

- **【可实现】** 对一个 **GraphEdge 元素**（穿过本行的边）：向下一行发一条 `DOWN` 元素（`from = 本行 lane`，`to = 下一行该边的 lane`），向上一行发一条 `UP` 元素（`to = 上一行该边的 lane`）。若目标行里没有该边，则退化为用「它在目标行的端点节点」的 lane。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:151-168,175-190）
- **【可实现】** 对一个 **GraphNode 元素**：遍历该节点的全部邻边（上下都算），每条边若能在相邻行定位到 lane 就发一条 DOWN 和/或 UP 元素，**position 一律等于该节点的 lane**；定位不到则不发（这正是端点行不重复画穿行边的原因）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:136-150）
- **【可实现】** 节点元素在集合里被排到最后统一追加，因此渲染顺序是「先边后节点」，节点覆盖在连线之上。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:128,171）
- **【可实现】** 一条斜线由上下两行各画一段半斜线（各自延伸到相邻行中心），合起来才是完整折线；因此实现时每行都要画到邻行 lane，不能只画到本行边界。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:163-169）

### 2.8 合流与分叉的视觉表现

- **【可实现】** **合流（merge）**：合并提交节点的 lane 由它在元素列表中的位置决定，它的多条父边在**下一行**分别对准各父提交的 lane，于是连线从节点 lane 呈扇形展开。合并提交的父边因为「父不是下一行」而都是长边。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/PermanentLinearGraphBuilder.java:55-67；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:139-149）
- **【可实现】** **分叉**：一个提交有多个子提交时，子提交各自占据自己的 lane，父提交所在行的元素列表会把所有相关边排好序，连线在父节点行**收敛**到该节点的 lane。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:139-149,246-268）
- **【可实现】** `layoutIndex` 相同的节点在同一「片段」内，其节点与连线用同一颜色；`layoutIndex` 变化的位置就是配色与左右分组变化的边界。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/PrintElementPresentationManagerImpl.kt:39-58）

### 2.9 轨道何时释放、何时复用

- **【可实现】** 不存在显式的「释放槽位」；每行重新编号即天然回收空槽，两条不同分支的连线只要不在同一行出现就可以共用同一个 lane 编号。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:246-268）
- **【可实现】** `layoutIndex` 一旦分配就永久保留（同一永久图内），所以分支被合并/分叉都不会改变既有节点的颜色归属；只有重建永久图（刷新、加载更多、切仓库）才会重新分配。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/permanent/GraphLayoutBuilder.kt:58-83；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:57-65）

---

## 3. 边类型与绘制几何

### 3.1 类型

- **【可实现】** 图层的边类型共 5 种：`USUAL`（普通）、`DOTTED`（折叠/筛选跨越）、`NOT_LOAD_COMMIT`（指向未加载提交）、`DOTTED_ARROW_UP`（向上半截虚线）、`DOTTED_ARROW_DOWN`（向下半截虚线）；前两种是「普通边」（有完整 up/down），后三种只有一端。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/api/elements/GraphEdgeType.java:18-33；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/api/elements/GraphEdge.java:10-22）
- **【可实现】** 线型映射：`USUAL`、`NOT_LOAD_COMMIT` → 实线；`DOTTED`、`DOTTED_ARROW_UP/DOWN` → 虚线。枚举里虽然还有 `DOTTED` 线型值，但当前实现从不产出它（**需复核**：可能为历史遗留）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/elements/EdgePrintElementImpl.kt:43-48；platform/vcs-log/graph-api/src/com/intellij/vcs/log/graph/EdgePrintElement.kt:16-20）
- **【可实现】** 绘制方向只有两个：`DOWN`（本行 → 下一行）与 `UP`（本行 → 上一行），由「另一端点在哪一行」决定，与语义上的父子方向无关。（来源：platform/vcs-log/graph-api/src/com/intellij/vcs/log/graph/EdgePrintElement.kt:11-14；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:142-167）

### 3.2 虚线的构造与相位

- **【可实现】** 虚线是 `BasicStroke(线宽, CAP_ROUND, JOIN_ROUND, dash = [dashLength, spaceLength], dashPhase = dashLength / 2)`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:101-109）
- **【可实现】** `dashCount = max(1, floor(edgeLength / rowHeight))`、`spaceLength = rowHeight / 2 - 2`、`dashLength = edgeLength / dashCount - spaceLength`；垂直边（`x1 == x2`）的 `edgeLength` 取 `rowHeight`，因此正好一格「一虚一空」。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:131-133,219-229）
- **【可实现】** 相位取 `dash[0] / 2`（即从半段虚线开始），目的是让相邻两行的虚线段在接头处对齐；Augit 用 `stroke-dashoffset = dashLength / 2` 等价。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:101-109）

### 3.3 几何、圆角与箭头

- **【可实现】** 本行节点/连线中心 `y = rowCenter = alignToInt(rowHeight / 2, FLOOR, ODD)`；lane 的 x `= elementWidth × lane + elementCenter`，其中 `elementWidth = 16 × rowHeight/22`、`elementCenter = elementWidth / 2`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:81-84；platform/vcs-log/impl/src/com/intellij/vcs/log/paint/PaintParameters.java:17-19,33-39）
- **【可实现】** 普通线宽 `1.5 × rowHeight/22`，选中线宽 `2.5 × rowHeight/22`（实际取「对齐后至少比普通线宽多 2 像素」）；两条线都用圆头圆角（`CAP_ROUND`、`JOIN_ROUND`）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:86-97）
- **【可实现】** 竖直段（`from == to`）：向下画到 `rowHeight - arrowGap`，向上画到 `arrowGap`；`arrowGap` 仅在箭头端点元素上非零（`circleRadius/2 + 1`），使箭头尖端不扎进节点圆。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:156-162）
- **【可实现】** 斜段：画到 `(对面 lane 的 x, y ± rowHeight)`，箭头中心取线段中点；箭头由两条短线构成，夹角余弦平方 `0.7`、长度 `0.3 × rowHeight`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:137-144,163-169,201-217）
- **【可实现】** 有箭头的边一律使用**实线**描边（即使边类型是虚线）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:127-134）
- **【可实现】** 节点圆直径 `8 × rowHeight/22`；选中态直径 = 普通直径 + (选中线宽 − 普通线宽)，即留出描边厚度差。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:91-94）
- **【可实现】** 节点类型 `OUTLINE`（灰色 `JBColor.GRAY` 描边、不填充）在当前代码里**没有任何生产者**：vcs-log 内只有 HEAD 会改节点类型（→ `OUTLINE_AND_FILL`），其余一律 `FILL`。Augit 可以照做（只实现 FILL 与 HEAD），把 OUTLINE 视为预留。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:172-187,200；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/HeadPrintElements.kt:6-12）

### 3.4 HEAD 节点（三同心圆）

- **【可实现】** HEAD 节点 = 三层同心实心圆：外圆用节点色，中间一圈用**行背景色**（`commitStyle.background`，因此会跟随选中/悬停底色变化），内圆再用节点色；每层直径递减 `2 × RADIUS_DELTA`，`RADIUS_DELTA = 2 × rowHeight/22`（基准行高下外径 12 → 中 8 → 内 4）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/HeadNodePainter.kt:19-29,47-59）
- **【可实现】** HEAD 选中时**只画一个外径为「外径 + 选中线宽 − 普通线宽」的实心圆**（配合 3.5 的双遍绘制形成黑描边环）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/HeadNodePainter.kt:31-35）
- **【可实现】** HEAD 不是图层的概念，而是**行级后处理**：某行的 refs 里出现名为 `HEAD` 的 ref 时，把该行唯一的节点元素换成 `OUTLINE_AND_FILL` 版本。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphTableModel.kt:97-117；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/HeadPrintElements.kt:6-12）
- **【需推断】** 「外环与中心点」在实际截图中看起来像空心圆环，是因为中间层用了行背景色；Augit 用 CSS 时等价做法是「三个同心圆 + 中间那层用当前行背景色」，或在选中/悬停时同步改这一层颜色。

### 3.5 选中态的绘制（黑描边）

- **【可实现】** 绘制分三遍：① 所有**未选中**元素用各自颜色画一遍；② 所有**选中**元素先用 `MARK_COLOR = 黑`、以加粗线宽画一遍；③ 选中的元素再用各自颜色、普通线宽画一遍。结果就是节点外一圈黑边、连线外一圈黑边（线本身仍是分支色）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:39-56,199）
- **【Swing】** 选中集合来自 `PrintElementPresentationManager`，只能由「图动作」写入（悬停箭头、悬停可折叠片段、折叠/展开、LinearBek 片段高亮），**表格行选中不会进入这个集合**。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/PrintElementPresentationManagerImpl.kt:19-37；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:118-127,136-142）

### 3.6 命中测试（鼠标在节点/边上）

- **【可实现】** 先测所有节点（`hypot(dx, dy) <= 节点半径`，命中即返回），再测所有边（`hypot(到起点) + hypot(到终点) < 线段长 + 线宽`，即到线段的距离小于线宽）；节点优先于边。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:59-77,111-123）
- **【Swing】** 命中测试以**单元格内坐标**进行（`getPointInCell(e.getPoint(), Commit, row)`），并且只有命中**边**时表格才不切换选中行（命中节点/空白仍会选中行）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphCommitCellController.java:58-64,84-91）

---

## 4. 配色分配

### 4.1 两种取色策略

- **【可实现】** `GraphColorGetterByNode`：颜色直接由 `(commitId, layoutIndex)` 决定，与 head 无关。**当前代码库里只被 Git 后端的 `GitLogProvider` 用于生成一个恒定色（永远返回 0）的图**，不是主日志的路径。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/GraphColorGetterByNode.kt:11-24；plugins/git4idea/backend/src/log/GitLogProvider.kt:194）
- **【可实现】** `GraphColorGetterByHead`：先由节点反查它所属的 head，把 `(headCommitId, headFragmentIndex, fragmentIndex)` 三者交给 `GraphColorManager`，因此**颜色按分支/片段而不是按单个提交**分配。主日志走这条路径。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/GraphColorGetterByHead.kt:11-18,27-33；platform/vcs-log/impl/src/com/intellij/vcs/log/data/VcsLogGraphDataFactory.kt:74-83）

### 4.2 颜色 id 的完整算法

- **【可实现】** `GraphColorManager.getColor(headCommit, headFragmentIndex, fragmentIndex)`：若 `headFragmentIndex == fragmentIndex`（节点就在 head 自己的片段内）→ 返回 `该 head 上「最重要」ref 的 name.hashCode()`（最重要 = 该提交所有 ref 中按 `referenceManager.branchLayoutComparator` 最小者）；若该 head 没有任何 ref → 返回 `DEFAULT_COLOR = 0`；否则（节点在分叉出来的子片段里）→ 直接返回 `fragmentIndex`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/graph/GraphColorManagerImpl.kt:7-17；platform/vcs-log/impl/src/com/intellij/vcs/log/data/RefsModel.kt:25-28）
- **【可实现】** `DEFAULT_COLOR = 0` 是**唯一**的特殊值（语义：该 head 上没有可用 ref），映射为黑色；不存在 `-1` 之类的「不染色」哨兵，也不存在图形染色总开关。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/graph/GraphColorManagerImpl.kt:15-17；platform/vcs-log/impl/src/com/intellij/vcs/log/graph/DefaultColorGenerator.kt:61-63）
- **【可实现】** 边的 colorId 取「两端中 `layoutIndex` 更大的一端」的节点色（相等时取上端）；特殊边（只有一端）取该端点的节点色。这保证连线跟随「更靠右/更年轻的那条分支」的颜色。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/PrintElementPresentationManagerImpl.kt:39-58）
- **【可实现】** id → 色值的换算：`colorId == 0` 直接是**黑色**；其余按
  ```
  r = |(colorId*200 + 30) % 100| + 70
  g = |(colorId*130 + 50) % 100| + 70
  b = |(colorId* 90 + 100) % 100| + 70
  hue = RGBtoHSB(r, g, b).hue
  color = HSBtoRGB(hue, saturation, brightness)
  ```
  即三个通道先被折叠到 70..169 再取色相，最终饱和度/亮度由主题给。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/graph/DefaultColorGenerator.kt:42-59）
- **【可实现】** 主题键 `VersionControl.Log.Graph.saturation` / `...brightness` 从 `UIManager` 读取，缺失时回退 `0.4 / 0.65`，并各自 `coerceIn(0, 1)`。**expUI 主题实际给了值**：浅色 `saturation 0.6 / brightness 0.7`，深色 `saturation 0.6 / brightness 0.6`，所以默认值只是第三方主题的兜底。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/graph/DefaultColorGenerator.kt:31-33,39-40,65-72；platform/platform-resources/src/themes/expUI/expUI_light.theme.json:860-865；platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:833-837）
- **【可实现】** 主题键在平台元数据里注册为 2024.2 引入，语义分别是「图形的饱和度」「图形的亮度」，取值范围 0..1。（来源：platform/platform-resources/src/themes/metadata/IntelliJPlatform.themeMetadata.json:3142-3151）
- **【Swing】** 颜色表按 `colorId` 惰性缓存（`Int2ObjectOpenHashMap.computeIfAbsent`），换 LAF 时清空重建；负 id（未加载提交）在取色前会被规整为 head=0 的色。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/graph/DefaultColorGenerator.kt:23-37；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/GraphColorGetterByHead.kt:13-15）

### 4.3 稳定性和筛选/排序的影响

- **【可实现】** 同一分支颜色稳定，因为颜色绑定 `layoutIndex`（或 head 的 ref 名哈希），而 `layoutIndex` 只在永久图重建时才会变化。**筛选、折叠、展开、切换排序都不改变 `layoutIndex`**，因此颜色不变。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:57-65；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/linearBek/LinearBekController.kt:179-191）
- **【可实现】** 分叉点下方：子片段用 `fragmentIndex` 取色，因此与父分支不同色；合流点上方：节点沿用「它所属 head」的片段色，于是两条支流在合流后统一成一条线的颜色。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/graph/GraphColorManagerImpl.kt:7-13）
- **【可实现】** 没有任何 ref 的 head 会拿到 `DEFAULT_COLOR = 0` → 黑色（典型场景：被 ref 过滤掉但仍在历史里的分支尖端）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/graph/GraphColorManagerImpl.kt:9-12；platform/vcs-log/impl/src/com/intellij/vcs/log/graph/DefaultColorGenerator.kt:61-63）
- **【可实现】** 颜色只由「分支名」参与哈希，**不带仓库根**，因此多个仓库里同名的分支（例如都叫 `master`）会得到相同颜色；`hashCode` 碰撞时也会同色。取色路径完全不涉及书签/标签，因此书签不影响图形颜色。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/graph/GraphColorManagerImpl.kt:8-13）
- **【可实现】** 加载更多 / 刷新 / 切换仓库会重建永久图并重新分配 `layoutIndex`，此时颜色可能整体改变；这是源码允许的行为，不是缺陷。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/PermanentGraphImpl.kt:200-219）
- **【可实现】** 不存在「按作者着色」或图形染色总开关：与图相关的 UI 属性只有 `Graph.ShowLongEdges`（长边显示）与 `Graph.Options`（排序），其余可切换项都是整行 highlighter（`Highlighter.<id>`）。`VcsLogColorManager` 的职责是「给路径/仓库根分配颜色」（Root 列色条、标签配色），与提交图轨道颜色无关。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/impl/MainVcsLogUiProperties.java:16-17,33-49；platform/vcs-log/impl/src/com/intellij/vcs/log/impl/CommonUiProperties.java:9-18；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/VcsLogColorManager.java:15-40）

---

## 5. 引用标签（label / ref）

- **【可实现】** 标签几何：高度 = `字体高 + 上内边距 1 + 下内边距 2`（默认缩放后 22）；文字左内边距 4、右内边距 4（右内边距同时作为整体宽度余量）；标签之间间距 12，紧凑模式 6；`LABEL_ARC = 6` 只用于 classic UI 的灰色圆角背板，不是标签本体轮廓。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/LabelPainter.java:53-64,89-92,134-135,186-189,219）
- **【可实现】** 标签本体是**图标**，不是文本背景：宽 `= round((6.25 + 2×(colors−1)) × h / 6.25)`、高 `= h`，即单色时是 h×h 的正方形，每多一层颜色横向加 `2h/6.25`；图形区与文字之间另有 1px 间距（仅 New UI）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/LabelIcon.java:25,109-121；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/LabelPainter.java:59-60,384-386）
- **【可实现】** New UI 的标签轮廓直接按 16px 网格的 expui「currentBranch」图形绘制：`scale = size / 16`，先以行背景色填充轮廓、再用标签色描边（圆头圆角 1px 线宽），最右一层额外画一个半径 1（16 网格下中心 10.5,5.5）的圆点；classic UI 则用另一套「多边形 + 左侧圆孔」的画法，从最后一个颜色往前叠，用行背景色在偏移 1px 处充当层间缝隙。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/TagPainter.kt:11-45,49-60；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/LabelIcon.java:54-107）
- **【可实现】** 颜色来源：每个标签的颜色取 `RefGroup.getColors()`，而它由 `VcsRefType.getBackgroundColor()` 决定；`SimpleRefGroup` 按 ref 类型分组，同类型 ref 多于 1 个时颜色**写两次**（形成双层叠标签）；`SingletonRefGroup` 只给单色。命名色键为 `VersionControl.GitLog.headIconColor / localBranchIconColor / remoteBranchIconColor / tagIconColor / otherIconColor`（expUI：`Yellow4 / Green5 / Purple4 / Gray6 / Gray6`）。书签（bookmark）不使用这套颜色，固定用 `VersionControl.RefLabel.bookmarkBackground`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/impl/SimpleRefGroup.kt:18-23；platform/vcs-log/api/src/com/intellij/vcs/log/VcsRefType.java:29-34；platform/vcs-log/impl/src/com/intellij/vcs/log/VcsLogStandardColors.java:10-14；plugins/git4idea/backend/src/log/GitRefManager.kt:264-284；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/BookmarkIcon.java:21-22）
- **【可实现】** ref 名 → 类型判定按前缀顺序短路：`refs/heads/` 本地分支、`refs/remotes/` 远端分支、`refs/tags/` 标签、`HEAD` 特殊、其余 other；同一提交上多个 ref 的绘制顺序是 HEAD、当前分支、master、origin/master、本地分支、远端分支、标签、other。（来源：plugins/git4idea/backend/src/log/GitRefManager.kt:190-217,360-367）
- **【可实现】** 本地分支与其 tracked 远端分支合并成一组，因此渲染成双色叠标签；tag 合并成一组，`Table.ShowTagNames`（默认 false）关闭时名字为空、只剩图形。（来源：plugins/git4idea/backend/src/log/GitRefManager.kt:296-332；platform/vcs-log/impl/src/com/intellij/vcs/log/impl/SimpleRefGroup.kt:47-49；platform/vcs-log/impl/src/com/intellij/vcs/log/impl/VcsLogApplicationSettings.kt:109-110）
- **【可实现】** 标签图标有按「缩放 + 高度 + 背景色 + 颜色列表」为 key 的缓存（上限 40），Augit 可等价做对象缓存。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/LabelIconCache.kt:12-27）
- **【可实现】** 单元格内标签的可显示宽度预算：`textAndLabelsWidth = 列宽 − 图形区宽`，`freeSpace = textAndLabelsWidth − 提交文本首选宽`；紧凑模式取 `min(freeSpace, textAndLabelsWidth/3)`，非紧凑取 `max(freeSpace, max(textAndLabelsWidth/2, textAndLabelsWidth − 80))`，最后夹到 ≥0。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellRenderer.kt:275-287,320-322）
- **【可实现】** 放不下时**不显示「+N」**，而是把从当前位置起的所有 ref 颜色合并成**一个**多层标签（颜色按首次出现顺序收集，出现次数 >1 的写两次），其文字 = 「此前已有标签则置空，否则用当前这一组的文字」，然后停止继续放标签；预算里还会预留「剩余 ref 合并后那个标签」的宽度。「…N more」只出现在 tooltip 和详情面板（tooltip 上限 10 条）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/LabelPainter.java:222-247,278-299；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/TooltipReferencesPanel.java:36,42,101-107）
- **【可实现】** 标签文字自身的省略：仅当「文字宽度 > 可用宽度 且 字符数 > `MAX_LENGTH = 22`」时才处理，先把第一个 `/` 之前替换为 `..`，再按可用宽度 `shortenTextToFit`；可用宽度 ≤ 0 时退化为 `shortenTextWithEllipsis(..., 22, 0, "…")`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/LabelPainter.java:62-64,318-331）
- **【可实现】** 标签默认**右对齐**（贴在提交列右缘、覆盖在文本之上）；开启 `Table.LabelsLeftAligned` 后画在图形区右侧，并用 `appendTextPadding(graphWidth + 标签宽 + 4)` 把提交文本推到标签之后。左对齐时顺序为 [书签, detached HEAD, refs…]，右对齐时顺序为 [detached HEAD, refs…, 书签]。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellRenderer.kt:192-204,232-246；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/LabelPainter.java:153-179；platform/vcs-log/impl/src/com/intellij/vcs/log/impl/VcsLogApplicationSettings.kt:112-113）
- **【Swing】** 悬停 tooltip 的命中范围：右对齐时 `x ≥ 列宽 − 标签总宽`，左对齐时 `图形区宽 < x ≤ 图形区宽 + 标签总宽`；命中时禁用单元格展开并显示常驻 tooltip。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellRenderer.kt:84-105；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphCommitCellController.java:172-195）
- **【可实现】** 点击标签图形本身**没有**跳转行为（标签是渲染出来的图形，不进 `SimpleColoredComponent` 的链接片段）；可点击的是提交文本里的链接（hash / issue），由链接监听处理 `NavigateToCommit` 标签后跳转。点击图形区元素才触发折叠/展开/跳转。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphCommitCellController.java:58-64,197-206；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:1070-1089）

---

## 6. 行布局与列

- **【可实现】** 行高由图形单元格渲染器决定（不是固定常量，`Table.rowHeight` 对本表无效）：
  `rowContentHeight = max(标签高度, 字体行高 + scale(verticalPadding))`；
  New UI 再取 `max(rowContentHeight, 主题 rowHeight)`。
  主题默认 `VersionControl.Log.Commit.rowHeight = 26`、`verticalPadding = 7`，即 New UI 下默认 26px。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:888-895；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellRenderer.kt:289-299；platform/util/ui/src/com/intellij/util/ui/JBUI.java:1900-1923）
- **【可实现】** 图形几何按 `scaleWithRowHeight(v, h) = v × h / 22` 等比缩放，并且提交列渲染器会把画笔的 `rowHeight` 覆写成表格实际行高，所以行高一变，节点半径/轨距/线宽/图形文字间距同步变。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/PaintParameters.java:33-43；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/column/VcsLogDefaultColumn.kt:107-110；platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:33）
- **【可实现】** **图形与提交标题在同一列**（Commit 列，id `Default.Subject`，标题文案取自 `vcs.log.column.subject`），不存在独立的「Commits」图形列。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/column/VcsLogDefaultColumn.kt:89；platform/vcs-log/impl/resources/messages/VcsLogBundle.properties:314-317）
- **【可实现】** 列注册顺序（model index 稳定且只增）为 `[Root, Commit, Author, Date, Hash]`；默认可见顺序 `[Root, Commit, Author, Date]`（**Hash 默认隐藏**）；Root 与 Commit 是强制列，Root 不可移动/不可缩放且标题为空串。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/column/VcsLogColumnManager.kt:31,50-52；platform/vcs-log/impl/src/com/intellij/vcs/log/impl/VcsLogApplicationSettings.kt:52-56；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/column/VcsLogColumnUtil.kt:18-32）
- **【可实现】** dynamic 列（可开关、可记忆宽度）= `[Author, Hash, Date]` + 扩展列；Commit 与 Root 的 `isDynamic = false`，即 Commit 列宽**不持久化**，而是「表格剩余宽度」：`Commit 宽 = 表格宽 − Σ(其他列 preferredWidth)`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/column/VcsLogDefaultColumn.kt:43,59,89；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:484-507）
- **【可实现】** 其他列宽没有硬编码默认值：最多采样 1000 行、上限 `scale(300)` 估算内容宽；Root 列宽为单仓库 0 / 隐藏名字时 New UI 10、classic 13 / 显示名字时按最长根名 + insets（上限 300）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:136-137,509-551；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/RootCellRenderer.java:47-50,201-217）
- **【可实现】** 表格表头不可见（`InvisibleResizableHeader` 只用于拖拽/缩放），列标题文案实际出现在列开关菜单与设置页。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:210；platform/platform-api/src/com/intellij/ui/table/JBTable.java:1271-1280）
- **【可实现】** author 与 date 是**两列**，不在标题列里拼接：Author = 短用户名（author ≠ committer 或 committer 是机器人邮箱时追加 `*`）；Date = `formatPrettyDateTime(authorTime，或 commitTime 取决于 Table.PreferCommitDate)`，pretty 允许时输出「N minutes ago / today / yesterday」这类相对文案。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/column/VcsLogDefaultColumn.kt:154-177；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/frame/CommitPresentationUtil.java:69-76；platform/platform-api/src/com/intellij/util/text/DateFormatUtil.java:120-164）
- **【可实现】** 这两列没有自己的省略号逻辑（只有标签、Root 名、提交详情标题有显式 `…`）；超宽由渲染组件裁剪，**需复核**其裁剪/省略的确切表现。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogStringCellRenderer.kt:21-46；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/LabelPainter.java:62,319-331；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/RootCellRenderer.java:114-118）
- **【可实现】** 详情（details）**不是表内新增行**，而是表格下方的独立面板（`Window.ShowDetails` 控制，最多同时展示 50 个提交详情）；因此表格行数与图形行数恒等，图形不会延伸到详情区域。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/impl/CommonUiProperties.java:9；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/frame/MainFrame.java:172-178,245-247；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/details/CommitDetailsListPanel.kt:40-93）
- **【可实现】** 表格无网格线（`showVerticalLines=false`、`showHorizontalLines=false`、`intercellSpacing=0`），相邻行的竖直连线因此视觉上连续。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:207-209）
- **【Swing】** New UI 下每个单元格被包进 `SelectablePanel`（圆角选中/悬停、左右 8px 间隔、内部 4px insets），classic UI 是纯色背景；New UI 的表格背景用 `ToolWindow.background`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogNewUiCellWrapper.kt:18-44,109-174；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:435-465,1204-1207）
- **【需推断】** 「表内展开详情行」在当前 commit 未发现实现；若 Augit 需求里有，属于自定行为，需单独定义行高与图形列表现。

---

## 7. 选中、悬停与标记状态

- **【可实现】** 行选中只是表格底色：选中背景 `VersionControl.Log.Commit.selectionBackground`、失焦 `selectionInactiveBackground`、前景同名色；图形节点不会被额外高亮。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:144-154,1213-1215）
- **【可实现】** 行悬停：非选中行把 `VersionControl.Log.Commit.hoveredBackground` 与该行背景按前者 alpha 混合后作为底色（浅色主题值 `#E9EAEC`；深色主题在同一键上另给一个带 alpha 的值，8 位十六进制按 `#RRGGBBAA` 解析，alpha 在最后两位），选中行不混合。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:139-142,814-822；platform/platform-resources/src/themes/expUI/expUI_light.theme.json:855-857；platform/platform-resources/src/themes/expUI/expUI_dark.theme.json:828-830；platform/util/src/com/intellij/ui/ColorHexUtil.java:28-37）
- **【可实现】** 悬停会影响 HEAD 圆环：HEAD 中间层使用 `commitStyle.background`，所以悬停/选中行的 HEAD 三环中间那圈颜色随之改变。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellRenderer.kt:213-216；platform/vcs-log/impl/src/com/intellij/vcs/log/paint/HeadNodePainter.kt:52-54）
- **【可实现】** 悬停图形元素（三选一，命中即改光标为手型并触发重绘）：
  1. 悬停带箭头的边（长边截断箭头、未加载提交箭头）→ 只高亮**那一个**箭头元素；点击则展开/跳转（未加载提交跳到 `targetId`）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:94-129）
  2. 悬停在线性可折叠长片段内部 → 高亮该片段的**中间节点集合**；点击则折叠这些节点并生成一条 `DOTTED` 边。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/collapsing/CollapsedActionManager.java:151-197）
  3. 悬停已折叠的 `DOTTED` 边 → 高亮该边的上下端点（即委托图的对应两个提交）；点击则展开中间节点并删除该 `DOTTED` 边。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/collapsing/CollapsedActionManager.java:244-278）
- **【需推断】** 悬停图形元素时返回的答案是 `isRepaintRequired = false`（不整表重绘），只依赖表格 hover 监听重绘旧/新悬停行；因此被高亮的片段若跨多行，黑描边是否只在光标所在行出现**需实测核实**。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:118-122,167-175）
- **【可实现】** 鼠标移开图形元素时会清空图内选中集合（`setSelectedElements(emptySet())`），因此黑描边消失。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:136-142；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphCommitCellController.java:67-81）
- **【可实现】** 多个计数的「选中区间」：图内选中集合是 `Set<nodeId>`，行内所有图元素逐个判断是否属于该集合；边只有在「两端（以及 targetId）都在集合内」时才算选中。不连续多选就是多个不相邻集合同时着色，绘制规则与单选一致（同样的黑描边）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/PrintElementPresentationManagerImpl.kt:74-100）
- **【可实现】** 图内选中不改变行的底色，行的多选底色完全由表格负责；两者互不驱动。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/PrintElementPresentationManagerImpl.kt:19-31；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:213）
- **【可实现】** 不存在「被 mark 的提交」这一图形状态：`MARK_COLOR` 在本版本就是选中元素的描边色（黑）。旧版本里用于标记提交的语义已由 highlighters（整行样式）取代。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:199；platform/vcs-log/impl/src/com/intellij/vcs/log/paint/SimpleGraphCellPainter.kt:50-56）
- **【可实现】** 内置整行高亮器（都作用于行样式，不动图形）：
  - 当前分支：底色 `VersionControl.Log.Commit.currentBranchBackground`（选中时不施加）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/highlighters/CurrentBranchHighlighter.java:28-46）
  - 我的提交：加粗（New UI 且作者列可见时只加粗作者列）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/highlighters/VcsLogCommitsHighlighter.java:39-56）
  - 合并提交：前景改灰 `VersionControl.Log.Commit.unmatchedForeground`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/highlighters/MergeCommitsHighlighter.java:27-31）
  - 未索引提交：前景蓝（受 `vcs.log.highlight.not.indexed` 注册表开关控制，默认高亮器 ID `INDEXED_COMMITS` 不进菜单）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/highlighters/IndexHighlighter.java:26-34）
  - 未匹配行（`RowType.UNMATCHED`）：前景 `JBColor.GRAY`；`rowType` 取不到（`getGraphRowInfo` 返回 null）的行整体使用 `CURRENT_BRANCH_BG` 背景，Commit 列前景灰、其他列加粗（该分支的意图源码未注释，**需复核**）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:773-812；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphTableModel.kt:83-90）
  - 本地更改 / WIP 行：渲染器里有 `NewCommit`（文案来自 `vcs.log.wip.label`）的专用分支，但在本 commit 找不到构造点，属于不可达代码（**需复核**）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellRenderer.kt:325-354）
- **【Swing】** 行样式由「base style + 所有 highlighter 组合 + 悬停混合」合成，选中时不叠加悬停。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:764-822）

---

## 8. 筛选、折叠与分页

### 8.1 筛选后图形如何重建

- **【可实现】** 可见图由 `Options` 与筛选类型决定：无筛选 → 基线图；有匹配提交 → `FilteredController`（隐藏非匹配提交并补虚线）；只有分支头 → `CollapsedController`（只做可达性，**不补虚线**）；`FirstParent` / `LinearBek` 是另外两种选项。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/PermanentGraphImpl.kt:51-85）
- **【可实现】** 筛选不是「把被隐藏提交换成一个折叠节点」，而是**从可见集合里删掉**，最近的可见上下节点之间用一条 `DOTTED` 边直连；随后整图重建打印元素。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/FilteredController.kt:44-48；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/collapsing/CollapsedGraph.java:242-274）
- **【可实现】** 每次筛选/刷新/加载更多都会 `createVisibleGraph` 并**重建整个打印元素生成器**（`layoutIndex` 不重算，比较器沿用永久图布局）；只有交互式折叠/展开走 `graphChanges != null` 的增量刷新。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/visible/VcsLogFiltererImpl.kt:128-134；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:57-65,136-145）
- **【Swing】** 筛选模式下禁用折叠/展开按钮（`isActionSupported` 对 `FilteredController` 返回 false）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/VisibleGraphImpl.kt:177-182）

### 8.2 「父提交不在当前结果」时的短虚线

- **【可实现】** 生成两条方向不同的虚线边：`DOTTED`（两端都还在可见集合内，跨越被隐藏区间）与半截边 `DOTTED_ARROW_UP/DOWN`（一侧端点不可见，只剩箭头贴着一个可见节点）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/collapsing/DottedFilterEdgesGenerator.kt:56-65,182-188）
- **【可实现】** 算法（对可见性变化区间做两次线性扫描）：
  ```
  downWalk: 正序扫描，对每个可见节点取父节点集合
            visibleParentMax  = 可见父中最大的“编号”
            invisibleParentMax = 不可见父中最大的“编号”
            若二者相等或无不可见父 → 只记录编号
            否则 → 生成一条 DOTTED 边（当前节点 → 那个不可见父）
  cleanup:  区间编号重置为哨兵
  upWalk:   倒序扫描，对每个可见节点取子节点集合，处理对称（生成 DOTTED 边到最近可见子节点）
  ```
  （来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/collapsing/DottedFilterEdgesGenerator.kt:44-153）
- **【需复核】** 上述扫描里 `downWalk` 的结果会被随后的 `cleanup()` 覆盖，而且 `hasDottedEdges` 在向下方向命中时返回 `false`、向上返回 `true`，两处对不对称，需确认是否为有意设计。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/collapsing/DottedFilterEdgesGenerator.kt:44-54,193-201）
- **【可实现】** 半截边只有一端：`DOTTED_ARROW_UP` 只填 `downNodeIndex`，`DOTTED_ARROW_DOWN` 只填 `upNodeIndex`；它们被并入相邻行的元素集合，`DOWN` 箭头出现在「上端点正好在上一行」的行，`UP` 箭头出现在「下端点正好在下一行」的行。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/api/elements/GraphEdge.java:15-22；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:192-212,256-263）
- **【需推断】** 半截虚线在端点行之外**不画线**，所以视觉上是「贴着节点的短虚线 + 箭头」，长度就是一格行高；第 0 行向上的箭头源码留了 `todo`，可能不显示。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:203-206）
- **【可实现】** 终点找不到位置时用 `TerminalEdgePrintElement`（`positionInOtherRow == positionInCurrentRow`），只画一格内的线加箭头。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/elements/TerminalEdgePrintElement.java:10-17；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:156-167）

### 8.3 折叠 / 展开

- **【可实现】** 手动折叠：隐藏片段中间节点、删掉穿过它们的 `DOTTED` 边、新建一条连接上下边界的 `DOTTED` 边；展开是其逆操作（显示中间节点、删掉 `DOTTED` 边）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/collapsing/CollapsedActionManager.java:169-190,244-269）
- **【可实现】** 可折叠片段有长度限制：短片段上限 10 行、搜索范围 10、悬停探测上限 500；只有足够长的线性片段才允许折叠。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/collapsing/LinearFragmentGenerator.java:23-24,90-124）
- **【可实现】** `LinearBek` 模式在构造时就把所有可折叠合并块折叠起来（删除被折叠的边并记录到 `hiddenEdges`），展开时从 `hiddenEdges` 递归还原。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/linearBek/LinearBekController.kt:41-45,97-110；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/linearBek/LinearBekGraph.java:22-46）
- **【可实现】** 折叠后轨道不重排：`layoutIndex` 不变，只是被隐藏节点从每行元素列表消失，剩余元素重新紧压编号。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/collapsing/CollapsedGraph.java:232-274）

### 8.4 分页（加载更多）

- **【可实现】** 提交数分四档：`5 → 100 → 2000 → 全部（Int.MAX_VALUE）`，每请求一次前进一档；文件历史用 `30 → 全部`。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/visible/CommitCountStage.kt:4-25；platform/vcs-log/impl/src/com/intellij/vcs/log/history/FileHistoryFilterer.kt:80）
- **【可实现】** 触发时机：读取表格**最后一行**时，若 `canRequestMore`（可见包允许且本次未请求过）则发起加载更多。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphTableModel.kt:62-65；platform/vcs-log/impl/src/com/intellij/vcs/log/util/VcsLogUtil.java:462-469）
- **【可实现】** **分页不是把新页提交拼接到已有图形上**：请求后仍用同一个永久图、更大的提交数上限重新过滤并整体替换 `VisiblePack`，然后重建打印元素。因此「已绘制图形与新页拼接」在 IntelliJ 中并不存在，Augit 若采用增量拼接属于自定行为（**需推断**）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/visible/VisiblePackRefresherImpl.java:250-279；platform/vcs-log/impl/src/com/intellij/vcs/log/visible/VcsLogFiltererImpl.kt:253-268）
- **【可实现】** 提交数超过分页上限时，图形算法本身不受影响：`recommendedWidth` 用采样估计（最多 20000 行、加权均值 + 加权标准差、四舍五入），只参与图形区宽度的下界。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:53-124,282-284）
- **【可实现】** 选中项跨重建保持：记下选中提交集合、是否贴顶、锚点与偏移，重建后遍历新图所有行把提交映射回行号再恢复；交互式折叠会先 `fireTableDataChanged`（清空选区）再恢复。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/SelectionSnapshot.kt:18-100；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/GraphCommitCellController.java:101-119）

### 8.5 排序模式

- **【可实现】** 排序只有两种：`Off`（拓扑 + 时间）与 `Standard`（Bek：合并时让 incoming 排在正下方）；后者用 `SortIndexMap` 对行索引双向重映射，`layoutIndex` 需要通过该映射换算（`BekGraphLayout`）。（来源：platform/vcs-log/graph-api/src/com/intellij/vcs/log/graph/PermanentGraph.kt:51-54；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/sort/SortedBaseController.java:24-95；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/linearBek/LinearBekController.kt:179-191）
- **【可实现】** Bek 重排的目标：以 head 为单位切「分支段」，段内按「合并提交可跳过」的 DFS 收集节点，再把各段按时间戳逐块合并；块长度上限 20 行、相邻提交时间差超过 3 天即断开、块长超过阈值后（4 小时）也断开。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/sort/bek/BekBranchCreator.java:36-78；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/sort/bek/BekBranch.java:14-16,41-65；platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/facade/sort/bek/BekBranchMerger.java:33-66）
- **【需推断】** Augit 若只做只读查看，可先实现 `Off`（直接按输入拓扑序），把 Bek 作为后续增强；两者颜色都沿用同一 `layoutIndex`，因此切换排序不会改变配色。

---

## 9. 图形区宽度与 GRAPH_TEXT_GAP

- **【可实现】** 每行的图形区宽度（即提交文本的起始 x）：
  ```
  maxIndex = max over 本行打印元素 of positionInCurrentRow
             边还要额外取 (positionInCurrentRow + positionInOtherRow) / 2
  maxIndex = max(maxIndex + 1, min(6, visibleGraph.recommendedWidth))   # 6 = MAX_GRAPH_WIDTH，作为“最少泳道数”
  graphWidth = maxIndex × PaintParameters.getElementWidth(rowHeight)
             + PaintParameters.getGraphTextGap(rowHeight)
  ```
  （来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellUtil.kt:14-33）
- **【可实现】** `GRAPH_TEXT_GAP = 2`（按行高等比缩放），它是**唯一**把图形区宽度换算到文字起点的项；它在这里被计入 `graphWidth`，而不是在画线时使用。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/paint/PaintParameters.java:13,41-43；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellUtil.kt:31-32）
- **【可实现】** 提交文本通过 `appendTextPadding(graphWidth)` 被推到该行的图形区右侧；这个 padding 是「把文本游标至少推进到 graphWidth」，**不会把文本往左拉**，所以文本起点 = max(自然起点, graphWidth)。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellRenderer.kt:230-246；platform/platform-api/src/com/intellij/ui/SimpleColoredComponent.java:285-292,541-544）
- **【需推断】** 因为 `graphWidth` 逐行计算，「标题是否始终从同一 x 开始」取决于每行 `maxIndex` 是否相同：当全图 `recommendedWidth ≥ 6` 时每行至少 6 条泳道，多数行会得到同一个宽度；泳道很少的孤行会得到更小的 `graphWidth`，标题因此略微左移。Augit 若要严格对齐，需要额外取全局最大宽度（属于对 IntelliJ 行为的收严，需用户确认）。
- **【可实现】** `recommendedWidth` 只作为「最少泳道数」的下界参与上式，不是列宽；Commit 列宽由表格剩余宽度决定（见 §6）。（来源：platform/vcs-log/graph/src/com/intellij/vcs/log/graph/impl/print/PrintElementGeneratorImpl.kt:42,53-124；platform/vcs-log/impl/src/com/intellij/vcs/log/ui/table/VcsLogGraphTable.java:484-507）
- **【可实现】** 绘制顺序：先画单元格文本内容，再画引用标签，最后画图形（图形在最上层）。（来源：platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/GraphCommitCellRenderer.kt:192-204）

---

## 10. 边界情况与需复核清单

- **【可实现】** `EdgesInRowGenerator` 的分块缓存不参与语义，只影响性能；Augit 用定义式（`up < r < down`）即可。
- **【需复核】** `LineStyle.DOTTED` 枚举值当前无生产者。
- **【需复核】** `DottedFilterEdgesGenerator` 的 `cleanup()` 与 `hasDottedEdges` 不对称（见 §8.2），可能影响某些筛选组合下的虚线条数；Augit 实现时应以「最近可见祖先/后代直连」为目标语义，并用真实仓库截图核对。
- **【需复核】** `DOTTED_ARROW_UP` 在第 0 行的箭头有 `todo`，可能不显示。
- **【需复核】** SimpleColoredComponent 对超宽 author/date 是裁剪还是加省略号。
- **【需推断】** 表内「展开详情行」在当前 commit 不存在。
- **【需推断】** 跨多行的长边（跨度 ≥ 30）默认只画两端各 1 行并带箭头；若 Augit 希望始终画完整连线，需要显式改变该行为（并同步 `recommendedWidth` 的估算）。

---

## 附录 A：分类总表

| 主题 | 可直接实现 | Swing 特有 | 需推断 |
| --- | --- | --- | --- |
| 轨道分配 | `layoutIndex` DFS、逐行 lane 排序与紧压、`up<r<down` 元素集合、长边阈值 30/1、端点行补画规则 | 缓存与 `SLRUMap` 实现 | head 排序比较器在 Git 下的完整优先级 |
| 边与绘制 | 类型→线型映射、虚线构造与相位、箭头角度/长度、圆角描边、HEAD 三同心圆、选中双遍黑描边 | 命中测试坐标取自 Swing 单元格、`BasicStroke`/`Ellipse2D` 等 API | 第 0 行向上箭头、`LineStyle.DOTTED` |
| 配色 | id 计算公式、`DEFAULT_COLOR=0`→黑、head 片段 vs 子片段规则、expUI 饱和度/亮度 | 颜色表缓存的 LAF 失效重建 | 无 |
| 标签 | 高度/内边距/间距、16 网格轮廓、颜色分组与双层叠色、溢出合并为一个标签、宽度预算公式、右/左对齐 | tooltip 命中与常驻行为、图标缓存 | 轮廓的像素级还原 |
| 行与列 | 行高公式与主题默认 26/7、等比缩放、列集合与默认可见性、Commit 列=剩余宽度、详情为独立面板 | New UI 单元格 SelectablePanel 包装 | author/date 裁剪表现、表内详情行 |
| 选中/悬停 | 三类悬停高亮语义、黑描边画法、多选集合判定、行底色/悬停混色、整行高亮器 | 表格选中与图内选中互不驱动 | 无 |
| 筛选/分页 | 隐藏即删+`DOTTED` 直连、四档分页、整图重建、选中恢复 | `fireTableDataChanged` 流程 | 虚线扫描的两处不对称、增量拼接（IntelliJ 无此行为） |
| 间距 | `graphWidth` 公式与 `GRAPH_TEXT_GAP` 用法 | `appendTextPadding` 的游标语义 | 标题是否全局对齐 |
