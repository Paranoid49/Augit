# Augit 接手说明

本文供下一个接手本项目的执行者（AI 或人）快速建立准确上下文。先读 `AGENTS.md`，再按 `docs/development-validation.md` 的顺序读取规格、模块文档和当前基线；本文其余内容是当前实现与历史交接信息。

更新日期与状态：见文末「当前状态」。

---

## 1. 当前 Goal

目标：构建一个轻量、快速、熟悉 JetBrains New UI 的通用代码与 Git 工具 Augit。

Augit 主要服务 Git 操作、文件查看、代码搜索和差异处理，不区分编程语言，也不试图成为完整 IDE。只完善和优化 `docs/product-spec.md` 规定且当前已经实现的功能，不新增语言服务、项目索引、插件、构建调试、AI 等 Augit 没有的能力。普通文件保持只读，三栏冲突解决器的结果区是唯一允许编辑和保存文本的区域。

以 PyCharm 2026.2.1 New UI 作为视觉和交互参考，以本地 `/mnt/d/github/intellij-community` 的 commit `576e328` 作为行为权威。在 Augit 已有功能范围内，对齐布局、配色、尺寸、间距、字体、图标、控件状态、页面状态机、焦点、键盘导航、动作启用条件、弹层、对话框、Diff、冲突解决、设置、终端和 Git 操作，以及主题切换、DPI 缩放和窗口变化行为。

允许使用鼠标、键盘、截图和 UI Automation 操作用户已经打开的 PyCharm，用于观察和验证 New UI 的真实布局与交互。只能进行参考所需的只读操作，不关闭用户原有的 PyCharm，不修改用户项目、Git 状态或持久化设置。PyCharm 中 Augit 没有的功能不实现；无法从源码或实际界面可靠确认的行为登记为无法取证，不得凭截图猜测。

继续使用 C#/.NET 10 原生 Win32 外壳、WebView2 和 `web/` 下的 HTML/CSS/JavaScript，不切换到 WPF、WinForms 或 Swing。要求结构、布局逻辑、CSS 令牌、状态和交互严格对齐；允许 Chromium 与 Swing 在字体栅格化、抗锯齿和图标渲染上的可解释差异。

轻量和 UI 熟悉度同等重要。先在当前 Windows 11 x64 环境建立冷启动空仓库、已有仓库和大仓库三类场景的启动速度、操作响应、内存占用、关闭速度和资源清理基线，再持续优化。当前没有 Windows 10 实机测试条件，不宣称已经完成 Windows 10 验证。

实现按大模块分批推进。小批次只验证受影响范围，大模块完成后验证该模块；严禁高频运行全量测试。具体文件读取顺序、测试命令、哈希登记、性能记录和资源清理流程以 `docs/development-validation.md` 为准。

最终，Augit 当前每个功能和界面都必须明确归类为：已按 New UI 对齐、有意产品差异、不适用、无法取证或待处理，并具备对应的权威出处、实现位置和可复验依据。
## 2. 当前技术方案（已落地运行）

| 层 | 技术 | 位置 |
|---|---|---|
| 外壳 | C# / .NET 10 原生 Win32 窗口 + WebView2 | `src/Augit.Shell/`（4 个 .cs，约 2.4k 行） |
| 业务 | 文件、Git、搜索、终端、设置 | `src/Augit.Infrastructure/`（约 9.4k 行）、`src/Augit.Core/`（约 2.6k 行） |
| 界面 | HTML / CSS / JavaScript | `web/`（约 9.3k 行） |
| 设计基线 | 视觉稿 = 运行时代码来源 | `docs/ux-mockups/`（49 个场景页） |

**关键约束：`docs/ux-mockups/` 与 `web/src/` 必须字节一致。**

`mockup.js`、`mockup.css`、`current-find.js`、`image-preview.js` 四个文件在两处各存一份且内容完全相同，由 `tools/audit/verify-ui-assets.ps1` 强制校验。**改界面时同步复制，否则校验失败。修改视觉稿以迁就实现属于违规。**

**New UI 取值来源：** 复原 PyCharm 2026.2.1 New UI 所需的信息分两层，出处与取证方式见下列两份文档（均从 IntelliJ 开源仓库提取）：

- `docs/intellij-platform-ui-reference.md` —— **静态取值**：命名色板、结构尺寸、提交图几何与缩放公式、Diff 与文件状态配色、主工具栏项目配色渐变机制。
- `docs/intellij-platform-ui-behavior.md` —— **交互与页面逻辑**：工具窗口状态机、树/列表选择与键盘导航、编辑器标签与只读正文、动作可用性、查找、提交图算法、主题/DPI/弹层，并汇总已裁决的规范冲突与待实施模块；分册在 `docs/nui-behavior/`。

两者只是推导令牌与行为时的**上游输入**，令牌权威仍是 `web/src/mockup.css`。

### 2.1 消息桥接

- 网页 → 外壳：`chrome.webview.postMessage` 发 `{ id, method, params }`
- 外壳 → 网页：`PostWebMessageAsJson` 回 `{ id, result }` 或 `{ id, error }`
- **事件**：`{ event, payload }`，**不带 `id`**，与回复共用队列靠 `id` 有无区分。
- 27 个方法，按 `workspace/*`、`document/read`、`git/*`、`search/*`、`settings/*`、`terminal/*` 分组。

### 2.2 区域渲染

界面按命名区域定点替换，不整页重绘：

```
titlebar  rail  side  editorTabs  editorContent  statusbar  bottomTool  overlay  toast
```

调用 `window.__augitRenderRegions(...names)`。整页重绘**只用于结构性变化**（例如工具窗口的出现与消失）。

---

## 3. 必须知道的六条硬约束（踩过坑的结论）

这些是本项目反复出错后才确认的规则，**违反会重现已修过的缺陷**：

1. **捕获阶段不能同步替换被点击的元素。**
   事件处理挂在 `document` 捕获阶段，视觉稿自身的监听挂在冒泡阶段。同步替换会让元素在事件继续传播前离开文档，冒泡监听永远收不到事件。用 `refreshAfterEvent` 延后到事件派发结束。

2. **写入状态前必须校验宿主契约。**
   缺 `path` 等身份字段的载荷一律按失败处理。否则无效对象进入状态，之后每一次渲染都要靠调用方容错（曾导致整页白屏）。

3. **异步加载必须带递增令牌。**
   晚到的旧响应要丢弃，否则慢的旧结果会覆盖用户最后一次选择。

4. **区域替换前要释放节点持有的全局监听。**
   所有者是被替换节点、监听却挂在 `document`/`window` 上的绑定，必须登记并由 `disposeRegionBindings()` 释放，否则随刷新累积。

5. **用户状态不能只放在 DOM 上。**
   勾选、草稿、选中行、滚动位置、工具窗口布局、标签集合都要进状态。区域刷新会重建节点，只放 DOM 的状态会被静默丢弃。

6. **缓存要在前提条件变化时失效。**
   例如 `git.exe` 路径设置变化后必须清空 Git 解析缓存，否则用户改好设置也得重启。

---

## 4. 验证方式（严格按用户要求：分模块、不做全量矩阵）

| 手段 | 命令 | 用途 | 耗时 |
|---|---|---|---|
| 单元测试 | `dotnet test Augit.slnx -c Release` | Core + Infrastructure | 约 55 s |
| 验收套件 | `node tools/audit/live-shell.spec.cjs` | 界面逻辑与交互（桩宿主） | 约 60 s |
| 资源一致性 | `powershell -File tools\audit\verify-ui-assets.ps1` | 视觉稿 ↔ 运行时字节一致 | 约 3 s |
| 场景渲染 | 逐场景打开 `docs/ux-mockups/*.html` | 41 场景可渲染、无页面错误 | 约 60 s |
| 真实外壳截图 | `powershell -File tools\audit\shell-capture.ps1 -Scene X -Theme dark` | 保真度、启动、内存 | 视场景 |
| 视觉稿像素比对 | 截图与视觉稿逐像素差 | 目标 0.000 | 约 20 s |

**用户明确禁止每轮跑全应用矩阵。** 日常改动只跑相关的分模块验证；全量只在模块完成或交付前跑。

### 4.1 写断言的两条硬要求

- **断言必须能区分「实现对」与「实现错」。** 新增断言或修复后做**负向验证**：临时移除修复，确认断言确实失败。否则无法排除「断言恰好通过」。
- **桩必须异步**，否则表达不了乱序返回；**桩的取值不能等于默认值**，否则区分不出「读到桩」和「用了默认值」。

---

## 5. 已完成 / 未完成

### 已完成并有断言覆盖

- 技术栈迁移 WebView2 完成，原生自绘已从解决方案移除。
- 规格 §5.1 工具窗口切换与折叠、§5.2 标签集合（六阶段）、§5.3 弹层 Esc、§5.4 树键盘导航与焦点。
- §7.x 各功能模块、§12.1/§12.2/§12.4/§12.5 验收项。
- 视觉稿 41 场景渲染正常，像素差 0.000，资源字节一致。
- 文档已按实际技术方案全面更新（`architecture.md` 整体重写）。

### 尚未完成 / 需要继续

- **§5.2 收尾**：Git 历史结果的预览语义、比较标签关闭叉的完整取消语义、引用比较的独立跟随。
- **§5.4 剩余**：`Tab` 在当前区域内按视觉顺序移动焦点（尚未逐条验证）；固定快捷键与产品规格的一致性核查。
- **§5.3 剩余**：对话框取消后恢复打开前焦点、确认后焦点回触发区域；紧凑输入窗口的 `Tab`/`Shift+Tab` 循环与输入法组词规则。
- **规格其余章节未逐条核查**：§6.x 状态机、§9.x 状态机、§10.x 空/错误/禁用/危险状态。**注意：不要用「节标题关键词是否出现在测试里」这种启发式判断覆盖情况**——标题多为概括性描述，该启发式会给出 36/52 的假阴性。可靠的发现方式是**实际检查某项行为是否存在**（§5.2 与 §5.3 的缺口就是这样找到的）。
- **Windows 10 22H2 实机验收**（按用户要求不纳入此前轮次）。
- **内存**：实测约 525 MB，与 100 MB 目标的差距源于 WebView2 架构，用户已放开该限制。

---

## 6. 环境与工具注意事项

- 构建：`"/mnt/c/Program Files/dotnet/dotnet.exe" build Augit.slnx -c Release -p:NuGetAudit=false`
- 推送：`git push origin dsh`（**SSH 可用，HTTPS 被拒**）。远端 `git@github.com:Paranoid49/Augit.git`。
- PowerShell 5.1 按 ANSI 读取 `.ps1`：**无 BOM 的脚本必须纯 ASCII**，含非 ASCII 又无 BOM 会解析错乱
  （`tools/release.ps1` 因此必须保留 UTF-8 BOM）。改完脚本跑 `tools/audit/verify-script-encoding.ps1`，
  它除编码规则外还会真的 `Parser::ParseFile` 解析一遍。
- 窗口尺寸与位置由设置文件 `%LOCALAPPDATA%\Augit\settings.json` 的 `window` 字段恢复（§6.6），
  `--width/--height` 与 `--dpi` 是**审计覆盖**，优先于恢复值且不写回设置。
  窗口物理尺寸按显示器缩放换算（不换算会让 CSS 视口缩小 1/scale，底部区域被裁）。
- `MainWindowHandle` 在窗口构造期间可能为 0，需轮询或按类名 `EnumWindows` 取最大窗口。
- 外壳加载**可执行文件旁**的 `web` 副本，不是仓库源码：改了 `web/` 必须重新构建，
  否则截图是旧界面（`capture-surface.ps1` 会在源码比副本新时拒绝截图）。
- `PrintWindow` 对 WebView2 的**底部与状态栏区域不可靠**，这些区域优先用断言而非截图。
  实测在**交互会话锁定**时它对 Augit 窗口整体返回全白（对 Edge 正常），
  此时用 CDP 的 `Page.captureScreenshot` 取真实渲染像素；该通道需要 Windows 侧 node，
  WSL 直连不到 Windows 环回（未开启 mirrored 网络）。
- `--debug-port`（CDP）**会破坏 WebView2 消息通道**，开启后所有桥接请求超时。仅用于排查「页面完全无数据」，不可与正常数据路径并存。
  用 `--browser-args --remote-debugging-port=9333` 时实测桥接仍正常（提交列表是真实数据），
  但 CDP 只用于布局与渲染结论，不用作数据路径证据。
- 无头验收套件里 ES 模块必须经 HTTP 提供，`file://` 会被 CORS 拒绝。
- 内存测量**必须按父进程关系**把 WebView2 进程树归属到本实例。曾因未归属，把 525 MB 误报成 822 MB（混入 6 个其他程序的 WebView2 进程）。

---

## 7. 验证基线

> **本表是早期轮次的历史快照，不是当前实测值**（第 178 轮标注）。当前基线以
> `docs/intellij-platform-ui-behavior.md` §2.3 为准（那里同时登记了四个运行时文件的 md5 与 `live-shell` 断言数）；
> 下表里的 `236（Core 86 + Infrastructure 150）`／`验收套件断言 261` 等数字已被后续轮次取代
> （第 177 轮实测：单元测试 **Core 86 ＋ Shell 91 ＋ Infrastructure 175**、`live-shell` **1136 项断言**，
> 资源一致性 PASS、构建 0 警告 0 错误）。视觉稿场景渲染数、像素差异与冷启动耗时未在本轮复测，
> 因此**不把旧值写成当前值**。

| 项目 | 数值（历史快照） |
|---|---|
| 单元测试 | 236（Core 86 + Infrastructure 150） |
| 验收套件断言 | 261 |
| 视觉稿场景渲染 | 41/41 |
| 视觉稿像素差异 | 0.000 |
| 资源一致性 | PASS |
| 构建 | 0 警告 0 错误 |
| 冷启动到窗口出现 | 94–158 ms |
| 10 万文件工作区首次可用 | 141–158 ms |
| 空闲 CPU | 0.08% |
| 外部文件变化反映 | < 450 ms（目标 500） |
| 内存（主进程 + WebView2 树） | 约 525 MB（62 + 464） |
| `git/history` 10 万提交 | 约 271 ms（含 `--topo-order`） |

---

## 8. 一句话交接

**规格实现的大头已完成且逐条有断言；剩余工作是按「实际检查行为存在与否」的方式逐条核查尚未覆盖的规格章节（尤其 §5.3/§5.4 尾部与 §6/§9/§10 状态机），每轮只跑相关分模块验证，并遵守第 3 节的六条硬约束以免重现已修缺陷。**

---

## 9. 历史 Goal 说明

本节保留旧任务文本仅供追溯。当前执行目标以第 1 节“当前 Goal”为准，详细执行流程以 `docs/development-validation.md` 为准；旧轮次、旧断言数和旧基线不能代替当前实测。
