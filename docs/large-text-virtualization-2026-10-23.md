# 大文件只读预览的全文层 / 视觉窗口层原型（2026-10-23）

> 状态：**原型已建立并通过 2,000 行语义验证与 80,000 行结构性验证；尚未完成真实外壳 22.75 MB 正常入口验收。**
> 本文只登记本批实现、证据和边界，不把无头 Chromium 结果写成真实外壳或整页像素验收。

## 1. 背景与目标

此前的大文件只读预览把全部 `.code-line` 建进 DOM，再用 `content-visibility: auto`、
`contain-intrinsic-size` 和每帧 500 行分块构建降低开销。这些只是**布局/构建优化，不是虚拟化**：

- 80,000 行时 DOM 行节点仍随全文线性增长，UIA 全量枚举曾持续 20–30 秒未结束；
- 选择、复制、Find in File 和 Go to Line 都依赖已建的行节点，无法在窗口化后保留全文语义；
- 「窗口外不可选」不能作为产品行为。

2026-10-23 用户明确要求保留全文只读语义（拖动选择与复制、Find 全文计数与定位、
Go to Line 跳到未显示区域、原文换行与行号对应、可访问性和加载状态），先验证
「全文逻辑层 + 视觉窗口层」分离，再评估 80,000 行和 22.75 MB 样本。

## 2. 实现

新增 `web/src/large-text-view.js`（运行时专用，不进入视觉稿共享的四个字节一致文件）：

- **全文逻辑层**：`text` 与 `lineStarts`（每行起始 UTF-16 偏移）。查找计数、选择切片、
  复制和行号定位都只依赖该层，与窗口里当前画了多少行无关。
- **视觉窗口层**：只创建可视区 + 8 行 overscan 的 `.code-line` 节点；全文滚动高度由
  `.virtual-code-lines` 的 `height = lineCount × rowHeight` 占位。行节点绝对定位，
  行号仍 `position: sticky; left: 0`，横向滚动时固定。
- **选择**：禁用原生选区；指针位置用 `caretRangeFromPoint()` 落到可见行内的字符，
  再换算为全文偏移。拖到视口外时按指针到边缘的距离自动滚动并继续扩展；
  `Ctrl+A` 直接选择 `0..text.length`。
- **复制**：在 `copy` 事件里按全文偏移 `slice` 生成 `text/plain`，不依赖当前窗口的 DOM。
- **Find in File**：`current-find.js` 的 `readSource()` 读取全文；`select()` 把匹配集合与
  当前项交给窗口层，由窗口层只渲染可视行交集，并用全文偏移滚动到未显示的命中。
  关闭查找时把当前命中写成全文选区。
- **Go to Line**：`live-data.js` 优先使用 `view.__augitVirtual.lineCount` 校验行号，
  再用 `revealLine()` 按全文行号居中滚动；没有窗口层时保持原 `data-line` 节点路径。
- **可访问性**：窗口行 `aria-hidden`，另建一个 `pre[role=document]` 承载全文文本；
  写入推迟到可视行绘制后的空闲任务，并在写入后强制一次布局，避免把「已赋值」
  误当成「辅助技术已可读」。
- **软换行**：窗口层按统一行高定位，无法表达换行后的可变行高。启用软换行时退回
  原有完整行构建（语义不变），关闭后重新挂回窗口层；该回退登记为当前边界。

`measureCodeViews()` 改为按 `data-virtual-lines` 的**全文行数**计算行号列宽，
不能再用窗口里的几十个节点当总行数。`reuseTextViewInPlace()` 遇到窗口层时重建控制器，
保留滚动位置并向查找条派发 `document-content-changed`。

## 3. 2,000 行语义验证

夹具：2,000 行、340,313 字符；第 1、1000、1999、2000 行含 `needle`。
通过正常入口 `__augitOpenDocument()` 打开（宿主桩返回 `TextPreview` / `readOnlyPreview`）。

| 验证项 | 结果 |
| --- | --- |
| 全文逻辑层 | `lineCount = 2000`，占位高度 = 2000 × 实际行高 |
| 视觉窗口 | 28 个行节点（< 120），不是 2,000 |
| 首行/行号 | 第 1 行正文与行号对应 |
| 可访问文本 | 长度 340,313，`role=document`，窗口行 `aria-hidden=true` |
| 滚动到末尾 | 渲染第 2000 行 `needle last` |
| 横向滚动 | `scrollLeft=320` 时行号列偏移 < 2px |
| 全选 | `0..340313`，复制长度 = 全文长度 |
| 拖到视口外 | 选择跨 > 20 行、长度 > 5,000，自动滚动 `scrollTop > 0` |
| Find | 计数 `1/4`；Enter ×3 到 `4/4` 并定位第 2000 行；关闭后保留 `needle` 选区 |
| Go to Line | 跳到第 1000 行、命中 `alpha needle middle`、按实际行高居中、窗口关闭 |
| 普通文档 | `docs/notes.txt` 不进入窗口层，仍为完整行渲染 |
| 软换行 | 打开后退回 2,000 行完整构建，关闭后恢复窗口层 |

## 4. 80,000 行评估（无头结构样本）

样本：80,000 行、2,868,893 字符（约 2.74 MiB），接近 22.75 MB 文件 2.5 MB 预览上限的规模。
在真实 `index.html` 页面内创建一个 900×600 的 `.code-view` 后直接 `mount()`，只测模块本身。

| 指标 | 本次实测 |
| --- | --- |
| 挂载（含建 `lineStarts`、首窗口行） | **14.7 ms** |
| 强制首次布局 | **20.1 ms** |
| 滚动到底部并渲染最后一行 | **5.9 ms** |
| 全文可访问文本首次布局（空闲任务） | **558 ms**，一次性 |
| 窗口行节点 | 初始 < 120；末尾 42 |
| 全文可访问字符 | 2,868,893 |
| 滚到末尾 | 渲染第 80000 行 |

结论：视觉窗口的挂载与滚动已与全文行数解耦；**全文可访问文本仍是 0.56 s 的一次性
布局成本**，本批没有把它写成「零成本」。这也是下一步需要继续拆分或按需物化的部分。

## 5. 边界与未完成

1. 以上是**无头 Chromium + 宿主桩**结果，不是真实 Win32 外壳、WebView2、UIA 或像素证据。
   尚未在 22.75 MB 真机样本上验证首内容、选择/复制、Find、Go to Line 和 UIA 枚举耗时。
2. 80,000 行样本是合成文本；2.96 MB / 80,000 行真实文件与 22.75 MB 文件仍需分开记录。
3. 软换行启用时退回完整行构建。80,000 行 + 换行仍会建立全部行节点，是登记的回退，
   不是窗口化完成。
4. 全文可访问文本在空闲任务里一次性写入；辅助技术要等这次布局完成才能读到全文。
   若后续实测影响输入，需要改成按需/分片物化，但不能删掉全文可访问语义。
5. 本批未改产品规范、未新增功能、未删除任何已批准能力；普通文档路径保持原实现。

## 6. 复验命令与证据

```powershell
$env:AUGIT_BROWSER_EXECUTABLE='C:\Users\wl024\AppData\Local\ms-playwright\chromium-1243\chrome-win64\chrome.exe'
node tools/audit/live-shell.spec.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright --large-text-only
```

本批结果：`live-large-text 通过 20 项断言`，指标：

```json
{"mountedMs":182,"accessibleMs2000":77,"rows2000":28,"rowHeight":18,
 "mountMs80000":14.7,"layoutMs80000":20.1,"scrollMs80000":5.9,
 "accessibleMs80000":558,"rows80000":42,"a11yChars":2868893}
```

该模块已接入完整 `live-shell` 流程：2026-10-23 当前工作区版完整套件 `live-shell 通过 2368 项断言`
（退出码 0，未执行 0 项；相对 2348 的 +20 即本模块），组件哈希与登记见
`docs/intellij-platform-ui-behavior.md` §2.3。完整套件里本模块的指标为
`mountedMs=47`、`mountMs80000=22.9`、`layoutMs80000=28`、`scrollMs80000=7.2`、
`accessibleMs80000=685`；与上面的单跑值同量级，说明差异来自机器负载而不是实现分叉。

行高 18 来自宿主桩的字体回退（桩设置 `Cascadia Mono` 未随包加载），不是参考环境或
真实外壳的 22 px；真实外壳仍须按随包 JetBrains Mono 13 重新测量，不能把本表行高当结论。

`live-shell.spec.cjs` 中原「延迟行构建后完整补齐 5000 行」断言已替换为窗口层断言：
窗口行节点 < 200、全文行数 5000、可访问文本完整、行号仍 `aria-hidden`。
