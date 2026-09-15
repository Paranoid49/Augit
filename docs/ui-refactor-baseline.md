# UI 重构基线审计

更新日期：2026-09-14。本文件记录重构开始前的可复现基线，以及本轮建立的取证工具。数值均为实测，方法可复现。

## 1. 目标与实测对象

- 视觉目标：`docs/ux-mockups/`（`mockup.css` 93 KB、`mockup.js` 213 KB，共 40+ 场景）与 `docs/ux-spec.md`。
- 视觉稿本身是**可运行的 HTML/CSS/JS 实现**，不是静态图片；其场景通过 URL 参数（`theme`、`ui-size`）切换浅深色与字号。
- 现行实现：`src/Augit.App`（119 个文件、66573 行，原生 Win32 自绘）+ `src/Augit.Core`（2573 行）+ `src/Augit.Infrastructure`（9356 行）。

## 2. 像素基线（main-project / 深色）

方法：视觉稿用 Playwright + 本机 Chromium 以视口 1180×760、`deviceScaleFactor=1` 渲染截图；原生用
`Augit.App.VisualAuditHost main-project --dpi=96` 采集，按窗口逻辑宽度 1180 缩放到同一尺度后逐像素比较。

| 区域 | 平均通道差 | RMS |
|---|---|---|
| 整体 | 12.6 | 38.1 |
| 标题栏 | 3.8 | 18.9 |
| 状态栏 | 7.0 | 23.2 |
| 左侧项目树 | 14.6 | 40.7 |
| 正文区 | 13.8 | 40.8 |
| 底部面板 | 16.0 | 40.1 |
| 右侧 Git 面板 | 30.2 | 62.7 |

亮度分布：视觉稿暗部（<90）95.6%、亮部（>150）2.5%、均亮 40.5；原生 93.4% / 3.7% / 42.9。

结论：**窗口骨架（标题栏、状态栏）最接近，数据密集区（项目树、正文、底部面板、右侧 Git 面板）偏差最大**，
其中右侧 Git 面板平均差 30.2，是最需要重建的区域。

注意：该基线仍受两处未消除的干扰——原生采集为物理像素需重采样；两侧内容不同源（视觉稿用固定样例内容，
原生显示真实仓库内容）。因此上述数值用于**区域定位**，不作为逐像素验收结论。

## 3. 本轮建立的取证工具

`tools/audit/capture-surface.ps1`：用 `CopyFromScreen` 采集**真实屏幕像素**（design-system 要求用于字体与
抗锯齿验收的证据类别），输出窗口矩形、前台状态与白色占比，并在判定被遮挡时重试。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\audit\capture-surface.ps1 `
  -Exe <审计宿主 exe> -Settings <设置 json> -Workspace <工作区> `
  -Surface main-project -Out <输出 png> -Dpi 96
```

同时确认的宿主事实：

- `Augit.App.VisualAuditHost <设置> <工作区> [surface] [截图路径] [--dpi=96|120|144] [--unified] [--json-source] [--wide-graph] [--empty-left|--empty-right]`。
- 主窗口类名 `Augit.MainWindow.Native`；窗口构造期间会短暂上报 `-34x0` 一类退化矩形，枚举窗口必须轮询到尺寸稳定。
- 审计宿主可用 Release 或 Debug 输出直接运行；`PrintWindow` 采集不受遮挡影响，但按 `design-system.md` 只能证明结构。

## 4. 环境约束（影响验收方式）

- 本环境前台窗口长期被 DSH Web GUI 浏览器占用，`SetForegroundWindow` 无法稳定把被测窗口提到前台，
  因此真实屏幕像素采集会被遮挡。这是 `docs/visual-refinement-status.md` 中反复出现
  「实屏逐像素校准仍未完成」的直接原因。
- PowerShell 5.1 按 ANSI 读取 `.ps1`，脚本内**不得包含非 ASCII 字符**，否则解析失败。
- 构建用 Windows 侧 SDK：`/mnt/c/Program Files/dotnet/dotnet.exe`（10.0.201）。

## 5. 已知且被日志反复确认的差距类别

引自 `docs/visual-refinement-status.md`（1547 行）的自我声明，按出现规模排序：

1. 与参考同内容、同 DPI 的实屏字体／逐像素校准未完成（覆盖 69 个小节）。
2. Windows 10 22H2 实机验收未完成（58 处）。
3. 全应用／正式性能验收未完成（58 处）。
4. 具体页面整页未完成：文件历史、Blame、三栏冲突解决器、复杂多轨提交图、图标与文件类型颜色、图片、JSON 错误条（约 2px）、
   Markdown 正文左缘（约 2px）、终端首开（约 2.5 秒）、设置、Clone、Reset/Rollback、Push、Worktree 等。
5. `PrintWindow` 只能证明结构，不能作为屏幕字形证据（与 `design-system.md` §12 一致）。

## 6. 复用边界

`src/Augit.Core` 与 `src/Augit.Infrastructure` 共 11929 行，是纯逻辑与 Win32 互操作层（Git 命令与解析、工作区文件、
rg 搜索、ConPTY 终端、设置存储、实例协调），与界面绘制方式无关，重建界面时可整体保留。

## 7. 重构进度

| 模块 | 状态 | 证据 |
|---|---|---|
| WebView2 外壳（窗口、控件承载、场景/主题/像素对照参数） | 已完成 | 真实窗口截图；Release 构建 0 警告 0 错误 |
| 界面资源层（`web`，与视觉稿共用同一套 CSS/场景脚本） | 已完成 | `verify-ui-assets.ps1` 四个共享文件逐字节一致 |
| 数据桥（`workspace/info`、`workspace/list`、`document/read`、`git/status`） | 已完成 | 真实窗口截图显示 23 条真实工作区条目 |
| 项目树接真实数据 + 目录懒展开 | 已完成 | 首屏 `info` 55ms、`root` 131ms；展开点击已验证 |
| Git 状态异步补齐（不阻塞首屏） | 已完成 | `git/status` 实测约 15 秒，已移出首屏路径 |
| 正文 / Markdown / JSON / 图片接真实文档 | 未开始 | — |
| Changes 工具窗接真实状态 | 已完成 | 真实外壳截图显示 4 个真实改动文件；验收套件覆盖 |
| Git 日志（底部面板）接真实历史 | 已完成 | 提交行、完整哈希、分支标签与引用树均来自真实历史；验收套件覆盖 |
| Blame 逐行归属 | 已完成（浏览器验收通过） | 验收套件 27 项断言覆盖归属行数、日期作者与正文 |
| 文件历史（限定路径） | 已完成（浏览器验收通过） | 验收套件 28 项断言覆盖行数、首行内容、标签路径与样例隔离 |
| Git 历史工具窗（提交详情） | 已完成 | 真实外壳截图显示真实提交主题与 3 个变更文件 |
| 提交图（多轨复杂结构） | 已完成 | 真实外壳显示真实提交与多泳道配色，HEAD 带圆环 |
| 远端 / Stash / Worktree 管理窗口 | 已完成 | 真实外壳截图显示真实远端 origin 与真实 URL |
| Push 对话框 | 已完成 | 真实外壳显示真实分支与上游；"0 个提交"与 git 实测一致 |
| 三栏冲突解决器 | 已完成（含保存往返实测） | 真实冲突仓库实测读取与写回；验收套件 46 项断言 |
| Reset / Rollback 对话框 | 已完成 | Reset 目标取真实 HEAD；Rollback 标题与详情取真实改动文件 |
| Clone 对话框 | 已完成（含真实克隆实测） | 浅克隆 depth=1 实测提交数 1；非空目录被拒绝 |
| 内置终端 | 已完成（真实外壳实测） | 真实 ConPTY 会话输出 PowerShell 横幅，xterm.js 渲染 |

### Git 调用路径优化（第六轮）

用独立探针直接调用 `Augit.Infrastructure` 的公开 API，测得真实耗时：

| 阶段 | 耗时 |
|---|---|
| `GitExecutableLocator.ResolveAsync` | 44 ms |
| `GitRepositoryService.InspectAsync` | 241 ms |
| `GitStatusService.ReadAsync` | 181 ms |
| `GitHistoryService.ReadPageAsync` | 40 ms |

结论：**底层 Git 调用本来就很快（合计约 0.5 秒）**，先前的「15 秒」来自应用内重复执行：
`git/status` 与 `git/history` 各自独立做一次「定位可执行文件 + 检查仓库」，
在启动阶段并发争用。

修复：在 `ShellBridge` 内缓存 Git 运行时与仓库快照，并用信号量串行化首次解析，
两个方法共享同一份结果。同时让 `ShellBridge` 实现 `IDisposable`，随窗口一起释放。
- 项目树目前用界面侧忽略清单隐藏构建产物；应改为读取 Git 自身忽略规则（需要
  `Augit.Infrastructure` 暴露公开接口，当前 `GitCommandRunner` 是 internal）。
- `docs/ux-mockups/mockup.js` 是显式重复副本（与 `web/src` 一致），已由校验脚本防漂移；
  后续应抽出共享模块消除重复。

## 8. 第二轮：文档模块

### 已完成

- 项目树接真实数据、目录按需展开且展开状态在重绘后保留（缓存 + 展开集合）。
- 新增 `web/src/markdown.js`：受控 Markdown 渲染器，只生成安全标签，原始 HTML 与远程图片被阻止。
- 新增 `web/src/live-data.js` 的文档打开路径：按 `kind` 分派到文本 / Markdown / JSON / 图片 / 不可预览视图；
  状态栏与标签页显示真实路径与文件名。
- 新增 `--open <相对路径>` 启动参数，可在外壳启动时直接打开指定文件。
- 新增起点：真实数据路径的验收套件 `tools/audit/live-shell.spec.cjs`（14 项断言全部通过）：
  树来自宿主、展开取子项、Markdown 预览来自真实内容且不残留样例、纯文本按行渲染、展开状态保留、`?open=` 启动即打开。

### 已解决：文档通道（第三轮）

**根因不是消息体积，而是线程亲和性。** 上一轮判断为「大响应不送达」是错的：

- 实测消息桥可稳定送达 512 KB（1000/8000/32000/64000/96000/128000/512000 全部成功，最大用时 9 ms）。
- 真正原因：桥接方法 `document/read` 是异步的，`await` 之后的延续**不一定回到 UI 线程**，
  而 `PostWebMessageAsJson` 只能在 UI 线程调用，否则抛
  `CoreWebView2 members can only be accessed from the UI thread`。
  同步方法（`workspace/info`、`workspace/list`）因此一直正常，只有异步方法必然失败。
- 修复：异步响应放入托管队列，用自定义窗口消息（`ReplyMessage`）唤醒 UI 线程后再发送。
  跨线程只传消息、不封送字符串——先前用 `Marshal.StringToHGlobalUni` 传指针会被 WebView2
  判为非法参数（`Value does not fall within the expected range`）。
- 同时对齐了宿主与网页层的字段契约（原实现返回 `classification`，网页层读 `kind`）。

真实外壳已验证：`--open global.json` 与 `--open docs/product-spec.md`（106 KB）均正确渲染，
标签页、状态栏路径与正文内容都来自真实文件。

### 第四轮：Changes 工具窗

- `liveChangesSide` 按真实 Git 状态渲染：分组（Changes / Unversioned Files）、组计数、
  文件状态（modified/added/untracked 等映射到既有配色）、勾选态（改动默认全选、未跟踪默认不选）。
- 修复异步竞态：`git/status` 可能先于工作区数据返回，早期实现会把状态写到随后被丢弃的对象上。
  现改为模块级缓存 + `applyStatus()`，两者任意先后到达都能正确附着。
- 分支名改为参与渲染（`titlebar()` 直接读 live 数据），不再依赖一次性的 DOM 补丁，
  避免后续重绘把分支名覆盖回样例值。

### 第五轮：Git 日志接真实历史

- 新增宿主方法 `git/history`：复用 `GitRepositoryService` 做一次仓库检查，再读首页历史；
  HEAD 直接取自带 `IsHead` 标记的历史条目，避免为拿 HEAD 额外启动一次 git 进程。
- 网页层 `liveGitLog` 按真实历史渲染提交行、分支标签、引用树与提交图。
- 修复两处契约问题：宿主返回的父哈希是短哈希，而提交图按完整哈希建索引，需先映射；
  `liveGitLog` 的筛选栏必须带 `history-filters` 类并包含 `details` 溢出节点，
  否则既有绑定函数会在 `bar` 为 null 时抛错。
- 修复一处布局问题：`.empty-state` 是绝对定位，放进详情面板会在没有提交时覆盖整页；
  实时面板改用普通文本占位。

### 第六轮：Blame 与文件历史

- 新增宿主方法 `git/blame` 与 `git/file-history`，后者用 `GitHistoryFilter.FilePath` 限定路径。
- 独立探针实测：blame 约 40–44 ms（`global.json` 7 行、`product-spec.md` 186 行、`README.md` 103 行），
  文件历史约 38–50 ms，均返回真实数据。
- 网页层新增 `liveBlameView`，槽位结构与样例版一致，行高由既有 `measureCodeViews` 统一校准。
- 新增 `--blame <路径>` 启动参数用于验收。
- 验收套件扩充到 **27 项断言**（新增 Blame 四项：行数、日期作者、正文内容、不残留样例）。

### 重要更正

上一轮提交 `8f7adc6` 声称已完成「复用 Git 运行时与仓库检查」，但复核发现该提交
**并未包含实际改动**：当时的重构被随后重新应用的诊断补丁覆盖，只有 `ShellBridge`
的构造函数与 `IDisposable` 落地，共享解析器 `ResolveGitAsync` 写进了文件却没有任何调用方。
本轮已重新完成：四个 Git 方法现在都走 `ResolveGitAsync`，本进程内只解析一次。

### 第七轮：恢复外壳实时数据 + 可靠的取证方式

**重大更正：上一轮之后的改动把外壳的实时数据彻底打断了。** 表现是所有桥接请求都超时
（`workspace/info`、`git/status` 均 30 秒超时），页面退回样例数据。

诊断过程（本轮建立的可靠取证方式）：

1. **CDP 远程调试**：给外壳加 `--debug-port`，通过 `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`
   传入 `--remote-debugging-port`，再从 Windows 侧用 CDP 读取真实页面状态。
   这是本项目第一个**被验证可靠**的外壳内取证手段——写临时文件、写仓库目录、
   读 stderr 在 WinExe 下都不可靠。
   CDP 给出的关键事实：`__augitLive=false`、`err="load-document:host-timeout:workspace/info"`。
2. 加计数器确认 `WebMessageReceived` 收到 6 条、处理器完成 6 条——**收方向正常**。
3. 在处理器内直接 `PostWebMessageAsJson` 可以送达页面；经自定义消息泵转发则不送达。

**根因**：第六轮为解决线程亲和性引入的「托管队列 + 自定义窗口消息泵」投递链路
（`Pump` 同步上下文）无法把回复送达页面。回退到 `24a40ac` 的队列实现后实时数据立即恢复
（分支标签回到 `dsh`）。

**结论与处置**：外壳的桥接回复继续使用 `24a40ac` 已验证的队列实现；本轮在其上只补
`--blame` 参数透传，未再引入新的投递机制。

### 第八轮：修复 Blame 挂起（真实外壳内已可用）

**根因**：`ShellBridge` 用 `SemaphoreSlim`（`_gitGate`）串行化 Git 解析，并把结果缓存在字段里。
启动阶段 `git/status` 与 `git/history` 并发进入；`git/blame` 随后到达时排在它们之后等待信号量。
一旦先到者的解析仍在进行，`git/blame` 就会一直等待——在真实外壳中表现为该请求永不返回，
而诊断日志显示它停在 `ResolveGitAsync` 之前。

**修复**：改为缓存 **Task** 而不是结果（`_gitResolution ??= ResolveGitCoreAsync(...)`）。
并发的 Git 请求共享同一次解析，后到者 await 同一个 Task，不再有锁等待。
`_gitGate` 与 `IDisposable` 随之删除。

**验证**：真实外壳内 `--scene blame --blame global.json` 现在显示
`global.json 只读` 与真实 JSON 正文、7 行归属（与后端探针的 7 行一致）。

**同时确认**：`main-project`（实时项目树 20 条 + 分支 `dsh`）与
`commit-changes`（真实改动 `src/Augit.Shell/ShellBridge.cs`）均正常。

### 第九轮：文件历史界面

- 网页层新增 `liveFileHistoryTool`，沿用样例版的底部工具窗结构：
  标签显示真实路径，列表按提交逐行显示作者、时间与主题，首行选中并在详情区展示。
- 新增宿主到界面的完整链路：`git/file-history` → `loadFileHistory` → `live.fileHistory`。
- 新增 `--file-history <路径>` 启动参数用于验收。
- 验收套件扩充到 **28 项断言**（新增文件历史四项）。
- 视觉稿侧确认底部面板结构正常（在浏览器中 `file-history` 场景的 `.bottom-tool` 存在，
  `workspace` 带 `with-bottom`）。

### 第十轮：底部面板取证 + 修复重复初始化

**修复一个真实缺陷**：`ShellWindow` 构造函数把 `InitializeMessage` 投递了两次
（同一段注释与 `PostMessage` 重复出现），导致 WebView2 环境与控制器被创建两遍、
初始化竞态。这很可能是此前多轮「实时数据时有时无」的根因之一。已删除重复块。

**底部面板取证**：确认 `--width/--height` 其实存在，之前拿不到预期尺寸是因为
`MainWindowHandle` 在构造期指向非主窗口。改用按类名取最大窗口后取证成功。
`tools/audit/shell-capture.ps1` 新增 `-Height` 参数（底部工具窗在默认窗口高度下位于可视区之外）。

**真实外壳截图确认**：

- 文件历史：标签 `历史: docs/product-spec.md`，列表两行真实提交
  （`2026/09/14 … feat(ui): 完成…`、`2026/08/28 … feat: 实现 A…`），与后端探针的 2 条一致。
- Git 日志：提交列表为真实提交（`feat(ui):`、`fix(shell):`），
  引用树为真实分支（`dsh`、`main`、`origin/dsh`），提交图正常渲染。

### 第十一轮：提交详情接真实数据

- 新增宿主方法 `git/commit`：读取单个提交的正文与变更文件列表。
- 网页层在底部日志详情区加稳定挂载点（`data-live-changed-files` / `data-live-commit-detail`），
  复用 mockup 既有的 `history-commit-selected` 事件：选中提交即取详情，首次渲染后自动加载首条提交。
- 变更文件按 `GitChangeKind` 映射到既有状态配色，显示文件名、目录与变更文件数。
- 验收套件扩充到 **32 项断言**（新增提交详情四项：变更文件、提交信息、正文与样例隔离）。

**真实外壳截图确认**（1740×1150 窗口）：
提交详情显示真实主题（`fix(shell): 修复重复初始化，并为底部面板补齐取证`）、
`3 个文件` 与真实文件名（`ui-refactor-baseline.md` 等）、真实作者与日期（`l49 09/15`）。

### 第十二轮：管理窗口接真实数据

- 新增四个只读宿主方法：`git/remotes`、`git/references`、`git/stashes`、`git/worktrees`。
- 网页层新增 `liveManagementPage`，把远端 / Stash / Worktree 三个管理窗口接到真实数据，
  结构、图标与样式沿用样例版。
- 新增 `loadReferences`：并发取四类数据并在到达后补一次重绘（管理窗口依赖它们）。
- 修复两处渲染生命周期问题：整页重绘会清空提交详情区，新增 `refreshCommitDetails`
  在每次重绘后补齐；`__augitHistoryReady` 不再被详情加载阻塞。
- 验收套件扩充到 **37 项断言**（新增远端列表/URL、Stash 列表与样例隔离）。

**真实外壳截图确认**：远端管理显示真实远端 `origin` 与真实 URL（`git@github.com:…`）。

### 第十三轮：Push 对话框接真实数据

- 网页层新增 `computePush`：由分支、引用与历史推出当前分支、上游引用与领先提交。
  上游提交在已加载历史窗口内时可直接切片；不在窗口内时标记为「数量未知」，
  **不猜测、也不谎报为 0**。
- 新增 `livePushDialogBody`：显示 `分支 → 上游`、待推送提交列表与目标引用；
  没有配置上游时回退到样例版的「定义远端」状态。
- 修复一处此前潜伏的 mockup 崩溃：`bindChangesWorkflow` 在存在「提交:」标签但
  没有选中文件行时会用 `null` 调用 `createComparison`，导致 `push` 等场景整页重绘失败。
  现在该分支会保留标签外观并等待用户选择文件。
- 验收套件扩充到 **41 项断言**（新增 Push 四项：摘要、提交列表、详情与样例隔离）。

**真实外壳核对**：显示 `dsh → origin/dsh`、`0 个提交`。
用 git 直接验证：`git rev-list --count origin/dsh..dsh` 返回 `0`，`dsh` 与 `origin/dsh`
指向同一提交（`9595e45`），因此界面结论正确。

### 第十四轮：三栏冲突解决器接真实数据

- 新增三个宿主方法：`git/conflicts`（冲突会话与文件列表）、`git/conflict-load`（三栏内容与冲突块）、
  `git/conflict-save`（写回结果并标记已解决，版本不一致时拒绝覆盖）。
- 网页层新增 `liveConflictResolver`：左栏当前分支、中栏可编辑结果（唯一可编辑区域）、右栏合入内容；
  冲突块在结果正文里按行范围标出。结果栏保存走 `saveConflict`。
- 新增 `--conflict <路径>` 启动参数用于验收。
- 验收套件扩充到 **46 项断言**（新增冲突五项：栏标题、可编辑性、冲突标记、冲突块标出、标题信息）。

**保存往返实测**（独立探针，真实 git 冲突仓库）：
1. 造出真实 merge 冲突（`main` 与 `feature` 改同一行）；
2. `LoadAsync` 返回 1 个冲突块、结果正文含完整 `<<<<<<< / ======= / >>>>>>>` 标记；
3. 用「接受两侧」的结果 `SaveResolvedAsync` → `ok=True`，剩余冲突 0；
4. 文件内容被正确写为 `line1 / yours / theirs / line3`；
5. `git status --porcelain` 显示 `M  a.txt`（已暂存、冲突已解决）。

这验证了「读取冲突 → 编辑结果 → 写回并标记已解决」的完整链路。

### 第十五轮：内置终端

**方案**：xterm.js 直接在 WebView2 主页面内渲染，ConPTY 会话由宿主
`ConPtyTerminalSession` 管理。两者同为 Chromium，不需要第二个 WebView2 实例。

- 前端：`web/vendor/xterm/`（xterm 6.0.0 + addon-fit，从 `tools/xterm` 复制）；
  `live-data.js` 负责创建 xterm、绑定输入/尺寸事件并按偏移量轮询输出。
- 宿主：新增 `terminal/start`、`terminal/read`、`terminal/write`、`terminal/resize`、`terminal/stop`。
  输出按偏移增量读取，缓冲区上限 4 MB（约 2000 行量级），避免长时间运行后内存无界增长。
  启动前先结束旧会话，避免留下孤儿 Shell 进程。
- 终端只在 `?scene=terminal` 时启动，不做常驻进程。

**真实外壳实测**：宿主日志确认 `start shell=Windows PowerShell`、
`started ok pid=30848`，并且轮询持续读到输出（buffer 195 → 215 字节）；
截图显示 xterm 渲染出真实的 PowerShell 启动横幅（`Windows PowerShell` /
`Copyright (C) Microsoft Corporation.`），确认真实 ConPTY 输出已进入前端。

**修复的渲染生命周期问题**：整页重绘会替换终端宿主元素并把样例文本放回去，
新增 `reattachTerminal` 在每次重绘后把 xterm 自己的 DOM 节点搬回新宿主，
保留既有会话而不是重建。

### 第十六轮：设置窗口接真实数据

- 新增宿主方法 `settings/read` 与 `settings/write`。写入只接受已知字段，
  字号先做 9–40 范围校验，避免把非法值写进设置文件。
- 网页层新增 `liveSettingsBody`（主题、界面字体与字号、等宽字体与字号、终端 Shell、
  git.exe 路径、最近目录计数），值全部来自设置文件。
- 验收套件扩充到 **53 项断言**（新增设置七项：主题、界面字号、等宽字号、Shell、
  git 路径，以及保存后字号被提交、其它字段未丢失）。

**真实外壳核对**：界面显示 `跟随 Windows`、`Segoe UI`、`Cascadia Mono`；
与设置文件（`theme: System`、`textFontFamily: Segoe UI`、`monospaceFontFamily: Cascadia Mono`）一致。

**修复的保存绑定问题（第四次同类）**：直接给对话框按钮绑定监听会随整页重绘失效，
点击后不生效。改为文档级事件委托，按底栏顺序（取消 / 应用 / 确定）只对后两个写回。

**修复的布局问题**：`.form-grid` 的 `minmax(0, 1fr)` 会把字体名输入压到 0 宽、
字号标签折成竖排。新增 `.live-settings` 作用域样式为字体行单独定宽；
该样式只在实时设置页生效，视觉稿渲染差异仍为 **0.000**。

### 第十七轮：实测启动与内存，并修复会话目录堆积

**实测（Release，连续三轮，同机）**：

| 指标 | 数值 |
|---|---|
| 冷启动到主窗口可见 | **110–139 ms** |
| 外壳进程工作集 | 约 54 MB |
| WebView2 子进程合计工作集 | 约 435–470 MB（6 个进程） |
| 合计（工作集之和） | 约 490–523 MB |

WebView2 子进程分解（典型一次）：

| 进程 | 工作集 | 私有 |
|---|---|---|
| browser | 120.8 MB | 38.8 MB |
| gpu | 131.1 MB | 125.8 MB |
| renderer | 104.9 MB | 62.4 MB |
| utility ×2 | 58.3 MB | 21.6 MB |
| crashpad | 12.3 MB | 3.0 MB |

**结论（如实记录）**：启动速度远好于原始 1 秒要求，这是外壳保持精简的直接结果。
内存则是 Chromium 渲染层的固有成本：即使只加载一个页面，Chromium 也要起 browser /
gpu / renderer / utility / crashpad 六个进程，合计约 0.5 GB 工作集（私有字节约 0.25 GB）。
这与「100% 复原 HTML 视觉稿 + 丝滑」是同一枚硬币的两面——正是 Chromium 才换来零移植漂移
与合成器级流畅度。若要压到 100 MB 量级，就必须回到自绘方案，同时放弃前两项。

注意：多进程工作集之和会重复计算共享内存，私有字节（约 254 MB）更接近真实增量。

**修复的磁盘泄漏（真实缺陷）**：早期实现按 `Session-{进程号}` 建 WebView2 会话目录，
从未清理。实测累积 **115 个目录、1.4 GB**。现改为固定 `Session` 目录，
并在启动时清理历史 `Session-*` 目录（正被其它实例占用的会跳过，下次再试）。
实测清理后目录数 115 → 0，磁盘占用 1.4 GB → 39 MB。

### 第十八轮：Reset 与 Rollback 对话框

- Reset：目标提交取真实 HEAD（短哈希），模式沿用规格说明的 Soft / Mixed / Hard。
- Rollback：标题与详情取真实改动文件（优先「改动」组），显示路径与变更类型；
  没有可回滚改动时给出明确的空状态而不是样例内容。
- 验收套件扩充到 **57 项断言**（新增 Reset 目标哈希格式与样例隔离、
  Rollback 标题真实路径与样例隔离）。

**真实外壳核对**：
- Reset 显示目标提交 `6544412`，与 `git rev-parse --short HEAD` 的 `6544412` 一致；
- Rollback 标题显示 `tools/audit/live-shell.spec.cjs`、变更 `Modified`，
  与工作区真实改动一致（样例路径 `app.manifest` 不再出现）。

### 第十九轮：工作区差异视图

- 新增宿主方法 `git/diff`：按文件读取工作区差异，返回**结构化行**而不是原始补丁。
  解析与分栏规则留在核心层（`GitUnifiedDiffParser`），网页层只负责渲染
  「旧行 / 行号槽 / 新行」三列，两侧规则一致。
- 超大差异只回传前 2000 行并标记 `truncated`：整份补丁可能上万行，
  全量下发既拖慢渲染也没有阅读价值。
- 网页层新增 `liveDiffView`：删除行 / 新增行 / 修改行沿用既有配色，
  差异行内的字符级区间（`GitTextSpan`）渲染为 `<mark>` 高亮。
- 新增 `--diff <路径>` 启动参数。
- 验收套件扩充到 **63 项断言**（新增差异六项：路径标签、删除行、新增行、
  行内高亮、行号槽、样例隔离）。

### 第二十轮：查清并修正差异视图的两个问题

上一轮记录的「差异视图回退样例」经 CDP 查证，实际是**两个独立问题**：

**问题一：我的验收方法错了，功能本身是好的。**
用 CDP 读取真实页面后确认 `git/diff` 正常返回
（`ok ms=449 available=true rows=875`），DOM 里也确实存在
875 行对应的 1750 个差异行元素。
之前判为「未生效」，是因为我选的验证文件（`docs/ui-refactor-baseline.md`）
恰好**没有未提交改动**，宿主据此正确返回 `available=false`，
界面按设计回退到样例；我误以为整条链路失效。

**问题二（真实缺陷）：差异区在转储原始补丁。**
`GitUnifiedDiffParser` 会产出 `Metadata` 行（`diff --git` / `index` / `---` / `+++`）
与 `HunkHeader` 行（`@@ … @@`）。这些是补丁头部而非文件内容，
之前被原样渲染进差异列，看起来像补丁转储而不是一份差异。
现已剔除元数据行，区块头行单独着色为 `.diff-code-line.hunk`。

**真实外壳截图确认**：显示真实路径 `web/src/live-data.js`、
区块头 `@@ -1,867 +1,870 @@` 与真实源码内容，`diff --git` 等噪声不再出现。

**同时修正**：差异标签优先取真实差异路径，差异尚未到达时退回启动参数里的目标路径，
避免短暂显示样例文件名。

**关于窗口标题同步**：`DocumentTitleChanged` 的事件参数没有公开的强类型成员，
只能按属性名反射取值（已在代码注释中说明），保留该功能用于任务栏识别。

### 第二十一轮：点击改动文件打开差异

- 新增点击委托：点击改动列表中的文件行即加载该文件的差异并切到差异视图。
  与项目树用同一套思路（捕获阶段 + `closest`），既不受整页重绘影响，
  也不依赖内容安全策略允许内联处理器。
- 验收套件扩充到 **66 项断言**（新增三项：改动行可点击、点击后加载对应差异、
  编辑器切到 diff）。

**未取得真实鼠标点击的截图证据。** 我尝试了多种方式（`mouse_event` 合成点击、
`SetForegroundWindow` + `AttachThreadInput` 抢焦点、`ShowWindow` 最大化），
但在此环境下 `WindowFromPoint` 返回的句柄始终不是外壳窗口，
说明屏幕坐标对应的窗口层级与我假设的不一致。本轮为此消耗过多时间，已停止。

判断依据：该交互属于**事件处理逻辑**，无头浏览器才是验证它的正确工具
（已在其中用真实 `click()` 覆盖）；而真实外壳截图更适合验证**渲染结果**，
这已在第二十轮用 `--diff` 参数完成。后续不再用合成鼠标点击验证交互逻辑。

### 第二十二轮：Clone 对话框接真实 Git

**关键取舍：复用视觉稿已经写好的 Clone 对话框，而不是另写一套。**
`bindCloneDialog` 已实现必填校验、深度正整数校验、焦点转移、输入法组词保护、
进行中冻结表单、取消语义与 Tab 循环。我最初写了一套重复的校验逻辑，
随后删除并改为：外壳只注入 `window.__augitCloneRequest`，由视觉稿的对话框调用它。
这样校验规则只有一份，且视觉稿的渲染差异保持为 0.000。

- 新增宿主方法 `git/clone`：在调用 Git **之前**完成校验——
  地址/目录非空、深度为正整数、路径合法、目标目录为空或不存在。
  宁可提前明确失败，也不让 Git 在半途报出难懂的错误。
- 目标目录用最近目录预填；浅克隆默认不勾选、深度禁用，勾选后才启用。
- 失败原因显示在对话框内并聚焦对应字段。
- 验收套件扩充到 **74 项断言**（新增 Clone 八项）。

**真实仓库实测**（独立探针，公开仓库）：
- 非空目标目录被正确拒绝；
- 浅克隆 `depth=1` 成功，`git rev-list --count HEAD` 返回 **1**（与要求一致）；
- `.git` 存在，`InspectAsync` 识别为 `WorkingTree`。

### 第二十三轮：提交图接真实历史

**发现的真实缺口**：`gitLog()` 里写的是
`if (liveHistory && !complexGraph) return liveGitLog(...)` ——
`complexGraph` 场景（提交图）被显式排除在真实数据之外，
因此多轨图一直渲染样板的 8 条提交。这个排除条件在结构化行还没有父子关系时
是必要的，但宿主已经在历史载荷里返回了每个提交的 `parents`，
条件已经过时。现改为：只要有真实历史就用真实历史，泳道由
`buildCommitGraph` 从真实父子关系推导。

- 验收套件扩充到 **78 项断言**（新增提交图四项：按真实历史行数绘制、
  每行都有图形、主题与哈希来自宿主、合并提交产生多条泳道配色）。

**真实外壳截图确认**：提交图显示真实提交（`feat(ui):`、`fix(ui):` 系列），
每行有彩色泳道节点，HEAD 行带圆环标记；分支引用树同时显示真实分支 `dsh`。

**修正的验收数据**：历史桩原先只有 2 条线性提交且 `parents` 为空，
无法覆盖分叉与汇合。现改为一次合并提交 + 两条父提交，
使泳道推导的真实分支逻辑被实际覆盖。

### 关于 CDP 诊断通道的结论

`--debug-port`（`AdditionalBrowserArguments = --remote-debugging-port=N`）能开启 CDP，
但**会破坏 WebView2 的消息通道**：开启后所有桥接请求超时、页面退回样例数据。
因此该通道只能用于排查「页面完全无数据」这类问题，不能与正常数据路径并存。
本轮已将其移除，仅在需要时临时启用。

### 本轮踩坑记录（供后续复用）

- `ExecuteScriptAsync` 对返回 Promise 的脚本只回 `{}`；异步脚本内部的有效结果必须写入全局变量后再同步读取。
- Win32 消息循环没有 `SynchronizationContext`，`await` 之后不能再访问 WebView2；跨步骤操作要合并进单次脚本调用。
- `--width/--height` 在提交版外壳中不存在，窗口尺寸由设置文件决定；`MainWindowHandle` 在构造期间可能是 0，
  必须轮询或用 `EnumWindows` 按类名取最大窗口。
- 无头验收套件里，ES 模块必须经 HTTP 提供（`file://` 会被 CORS 拒绝）。
