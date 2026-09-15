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
| Git 历史工具窗、提交图、文件历史、Blame | 未开始 | — |
| 对话框族（Push/Remote/Clone/Stash/Reset/Worktree/冲突解决器） | 未开始 | — |
| 终端 | 未开始 | — |

### 已知待办

- `git/status` 经宿主调用实测约 15 秒，而同机直接执行
  `git status --porcelain=v1 --untracked-files=all` 仅 **0.73 秒**、
  `git log --max-count=100` 仅 **0.065 秒**。因此瓶颈不在 git 本身，
  而在 Augit 的调用路径（进程启动方式、命令条数或输出解析），需要进一步定位。
  界面已按「不阻塞首屏」处理，但这是下一轮值得优先处理的体验问题。
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

### 本轮踩坑记录（供后续复用）

- `ExecuteScriptAsync` 对返回 Promise 的脚本只回 `{}`；异步脚本内部的有效结果必须写入全局变量后再同步读取。
- Win32 消息循环没有 `SynchronizationContext`，`await` 之后不能再访问 WebView2；跨步骤操作要合并进单次脚本调用。
- `--width/--height` 在提交版外壳中不存在，窗口尺寸由设置文件决定；`MainWindowHandle` 在构造期间可能是 0，
  必须轮询或用 `EnumWindows` 按类名取最大窗口。
- 无头验收套件里，ES 模块必须经 HTTP 提供（`file://` 会被 CORS 拒绝）。
