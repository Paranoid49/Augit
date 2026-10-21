# 保留范围验收状态（2026-10-18）

本文只汇总当前可复验证据和未闭环项，不把局部通过当作整体完成。详细原始记录见各链接文档。

- **2026-10-22 行为套件**：HEAD `63f5bdb` 上 `tools/audit/live-shell.spec.cjs` 完整通过（`live-shell 通过 2339 项断言`，退出码 0，未执行 0 项）。该套件是无头 Chromium + 宿主桩的**行为/语义**验收，**不替代**整页像素验收（页面级同态配对 50、像素合格 0），也不替代正常入口实机证据；Settings 慢开/白闪、刷新首内容帧、搜索 timeout、参考 Diff 加载态仍按下文专项记录。

## 1. 正常入口

| 项目 | 状态 | 证据 |
| --- | --- | --- |
| 强制非增量构建 | 通过 | `tools/start-augit.ps1` 每次输出编译日志；`normal-entry-acceptance-2026-10-18.md` |
| 构建身份 | 已记录，需按 HEAD 区分 | 历史 `Augit.dll` SHA256 `159123F29DEF5DAF6EFE66A9D30F430D6CD22CF22912CB0527BD0BF23EA8559D` 对应 HEAD `d43e761`；当前 HEAD `690eb19` 重建为 `F53764A81EF29A81BC78E6921E8C0B9D529DE059B5037A94EB5E8A6F6AEB4E00`，`src/Augit.Shell` 在 `d43e761..HEAD` 无 diff，差异来自 `ProductVersion` 内嵌提交号。当前 `live-data.js` SHA256 `12D57E343FF58F2D9198882F0E7395048BC0E2C076772B1FAD7B55FCBDA41B3D`（第 131 批推送成功 toast 修复后）；当前（第 20 批后）`mockup.css` `4388373D881C4A7EAB1524249CD05FE1429F16AAF72D067493E2CEA472232954`；第 69 批 New Worktree 布局修复后 `mockup.js` `DDF905A9…9377`，第 136 批误改后 `mockup.js` `3745BF98…1002`，第 137 批按用户裁决回退到 `F3D0CD530D2B017538A21B3ECC43F8D7CF8E13BC4AC598AA4BF4A187A7E7671D`、`mockup.css` 第 140 批大文件 Warning 横幅修复后 `53BC7DF736BA3801C3529E0BE48D30F7D29FA4906F19A0F8F9A49E1C810C1A6C`（历史 `5BAA1232…8967`）；2026-10-18 正常入口 PID 9056 已按当前身份复验；PID 13864（第16批）和 15736（第17批）为前一身份，详见[当前构建身份](normal-entry-identity-2026-10-18.md)、[首个同态配对](homologous-main-text-pair-2026-10-18.md) §4、§10。 |
| 2026-10-18 当前构建冒烟 | 通过 | PID 13864；2880×1716/DPI168；0 警告 0 错误；设置 `B8868855…DC3D`；`normal-entry-identity-2026-10-18.md` |
| 普通冒烟 | 通过 | 3/3；PID 13060/22396/28268 |
| 设置正常入口 30 窗 | 通过 | 深/浅/映射各 10 窗；原生 339–497ms、捕获等待 73.6–101.1ms、原生关闭 24.6–56.8ms；`settings-capture-timeout-2026-10-17.md` |
| 刷新 | 部分通过 | 轻量严格 460.9/339.6/352.8/359.5ms；全窗首轮 651.6ms、首内容 731.7ms；`refresh-500ms-acceptance-2026-10-17.md` |

- **2026-10-19 终端状态栏修复后当前共享 UI 身份**：`mockup.js` `DA93667AA7F6555D4D1DAE2F9C534A6EEA56053EEB7133193E024ADCA8643C59`、`mockup.css` `F53F53D88DFED8CE844FC80FB67AEE3945ACBEC62AC5EFA7F4317256D3911EDF`、`live-data.js` `12D57E343FF58F2D9198882F0E7395048BC0E2C076772B1FAD7B55FCBDA41B3D`；`Augit.exe` `1738568C…23CC`、`Augit.dll` `FA350BFF…AAB7`、`ProductVersion=0.1.0+da572899…ce5aa`。正常入口 PID 452（浅色 1661/1662）、4672（深色 1660/1661/1662）已加载该身份并逐像素复验；详见[终端状态栏高度与顶边修复](terminal-statusbar-height-fix-2026-10-19.md)与[当前构建身份](normal-entry-identity-2026-10-18.md) §6。

## 2. 设置

### 已通过

- CDP 30 窗：Cancel/Save/OK/Esc/标题关闭、主题取消、持久化失败重试、Apply/重开、嵌套关闭。
- 无 CDP 11 次有效开窗：原生 599.2–747.5ms，捕获等待 92.1–139.9ms，未触发 500ms 超时。
- 无 CDP 7 次正常关闭：
  - Cancel 4 次：浅色 59.1ms、深色 72.7ms、同一进程重复 67.9/55.8ms；
  - Save/OK 54.8ms；
  - 标题 WM_CLOSE 86.7ms；
  - Esc（`keybd_event`）48.3ms。
- 无 CDP 重复两次录像 `foreground=true`；其余关闭录像前景 false，只作输入/时序证据。
- 失败结论已纠正：所谓“940×560 窄窗”是审计侧 DPI 二次换算错误，实际窗口 2880×1716 最大化。
- 2026-10-18 窗口级冒烟（设置 CSS 第 8–10 批后）：正常入口 3 次打开 Settings 窗口出现 330/343/349ms，`WM_CLOSE` 关闭 24/30/38ms，均 1 个可见窗口、0 重复。**仅窗口出现/消失**，不是首内容/无闪，不替代上面的 599.2–747.5ms 首内容与 48.3–86.7ms 原生关闭证据。

### 未闭环

- **不同 DPI/跨屏**未验证。
- **2904ms 慢样本**未复现，`CapturePreviewAsync` 500ms 超时分支未被真实慢样本触发；超时后提前揭示风险未验证。
- 深色白闪只在若干样本未检出，不能外推全场景。

证据：`settings-no-cdp-2026-10-18.md`、`settings-capture-stall-2026-10-18.md`、[设置开/关窗口级冒烟](settings-open-close-smoke-2026-10-18.md)。

## 3. 刷新

- `WorkspaceFileWatcher.MergeDelay=20ms`。
- 全主窗 GDI 录像四轮：严格撤盖 651.6/421.7/402.5/390.4ms；首个新内容帧 731.7/494.7/462.5/480.2ms。
- 录像约 20.4fps，60–90ms 是采样上界，真实滞后约 11–90ms；第 1 轮即使扣一帧仍 >500ms。
- 轻量无录像严格 460.9/339.6/352.8/359.5ms，但无全窗视频。
- 外部分支变化和冲突同步已有低于 500ms 样本；全场景透明帧/白帧/光标/工具提示未闭环。

证据：`refresh-content-onset-2026-10-18.md`、`refresh-external-visible-2026-10-17.md`。

## 4. 界面英文

- 正常入口浅色/深色各 20 状态，共 40 个状态扫描，CJK 发现数 0；两轮负向对照均检出 1 项。
- 覆盖 main/text/changes/diff/terminal、settings 四页、settings 保存失败、go-to-line、stash-create、stash-manager、worktree-manager、New Worktree 空字段、无远程 fetch 失败、push、remote、Define Remote 空名称/空 URL。
- 补充独立异常态：新建分支空/空白名称显示 `Name cannot be empty.` / `Name cannot contain whitespace.`，新建标签空名称显示 `Name cannot be empty.`（PID20524/端口64786），见[名称校验](branch-create-validation-2026-10-18.md)。
- 冲突列表/只读 Diff：真实 merge 冲突夹具，PID17344/端口62807，可见正文/title/aria CJK 0；只读 Diff 无三栏/保存入口。
- Rebase 冲突 Skip/Abort：PID5140/端口50564，动作与正文英文 CJK 0；Skip 后 rebase 完成、工作区干净、正常关窗。
- Merge 冲突 Continue：PID3172/端口52119，外部解决后 Continue 可用、CJK 0；点击后正常关窗。
- Merge 冲突 Abort：PID19348/端口59750，Abort 可用、CJK 0；点击后正常关窗。
- 未覆盖 Reset/Clone/操作结果等错误与危险确认状态。

证据：`ui-english-audit-2026-10-18.md`。

## 5. 保留页面双端对照

- **同态配对完成：50**（`main-project` / `text-viewer` 一，`terminal.html` 单一状态一，`README.md` Markdown 分栏一，`sample.json` JSON 一，Git Log 一，Commit 一，工作区 Diff 一，File History 一，Blame 一，分支弹层一，Checkout 对话框一，New Branch 对话框一，Stash 对话框一，Reset 对话框一，Push 对话框一，Git Log 提交右键菜单一，New Tag 对话框一，Clone Repository 一，New Worktree 一，Compare with Workspace 一，Worktrees 管理一，损坏图片错误页一，有效图片预览一，Rollback 对话框一，Changes 右键菜单一，Git Remotes 一，Commit 空态一，Define Remote 一，Push 有 upstream 一，Push 待推提交一，Push 被拒一，冲突列表一，项目文件右键菜单一，项目根节点右键菜单一，Settings/Appearance 一，Settings/Git 一，Settings/Terminal 一，Find in Files 空态一，Find in Files 结果一，Find in Files 零结果一，Find in Files 截断一，Quick Open 空态一，Quick Open 结果一，Git 不可用一，初始化仓库一，Settings/File View 一，关闭运行中终端一，大文件只读预览一，**Smart Checkout 一**、**Stash Manager 一**）；**像素合格整页配对：0**（保守口径，不把结构差异当像素一致）。
- `main-project` / `text-viewer`：2880×1716、DPI168、浅色、两端约 360 CSS Project 面板、单标签 `notes.txt`、树选中一致、无底部工具窗/宽右侧面板；标签图标/关闭叉已修复，工具轨起点与行号槽设为参考端工具栏/折叠区结构差异。详见[首个同态配对](homologous-main-text-pair-2026-10-18.md) §10。
- 跳转行：字段 200×34、按钮 78×34、蓝色边界 1px；标题/标签/品牌图标仍为产品差异。
- 编辑页：6.082% 是共同区域最大通道差≥16 的比例，不是“还原度 93.918%”；参考有约 39 CSS 右栏，实际无右栏；+63px 与 36 CSS 文档工具栏×1.75 对应。
- 终端：首个同态配对完成（浅色、`Local`、Windows PowerShell、8 行 `Ag 0123456789`、失焦；面板顶差 1px、八行顶 0/1、步长 31/32、左缘 1px、字宽 177 一致；提示符块间距 23px 为已批准 Reworked 差异）。第 5 批已把 Local 标签/关闭区修为参考的约 65×28 CSS 紧凑卡片（填充 #ECECF0、边框 #D6D8DD、卡内 `closeSmall` #A8ADBD；位置差 -4/-2px）。右侧栏 42 CSS 已按 2026-10-20 用户裁决登记为批准范围差异；浅色 ANSI Blue 已修为 `#0060AF`；完整内容、逐色索引与选区仍有未覆盖项；**状态栏 7/8 物理高度差已于 2026-10-19 修复**：终端打开时状态栏 28→32 CSS，上缘 1668→1661，终端正文底 1667→1660，隐藏终端后回到 1668；1661 的 1px `#F1F2F4` 顶边也已修复，PID 452 逐像素采样为 1661 `#F1F2F4`、1662 `#E9EAEE`，与参考一致。**ui-size 40 头部动作区已于 2026-10-19 按 CDP 几何修复**：header 63→99、More/Hide 间距 8px、会话标签 49px，正常入口 PID 18692 复验；面板顶边色已修复为两端 `#F1F2F4`（§9）。**终端鼠标选区/复制已修复并验证**：CDP 定位确认实际 `.xterm-screen` 从物理约 1221.5 开始，先前 y1135/1200 点击落在终端 header/编辑区；正确双击 (150,1240) 产生 `#A6D2FF` 选区，`Ctrl+Shift+C` 与 `Ctrl+Insert` 均复制 `0123456789`，`Ctrl+C` 仍为 SIGINT。深色多行也已验证：选区 `#214283`、文字 `#BCBEC4`，`Ctrl+Shift+C` 复制 `123456789`/`Ag 0123456789`。快捷键判定已抽为纯函数并由 `tools/test-terminal-copy-shortcut.mjs` 覆盖 10/10，正常入口 PID 23864 E2E PASS。见[终端选区/复制修复](terminal-selection-copy-fix-2026-10-18.md)、[深色多行验证](terminal-selection-dark-2026-10-18.md)。详见[终端同态配对 §8–§9](terminal-homologous-pair-2026-10-18.md)。
- 终端 ui-size 40 复验（2026-10-19，已由后续修复取代）：先前 PID 14072 只见 `...` 点状动作、Hide 不可辨识；仅改 More 偏移无效，已回退。随后 CDP 量得 More/Hide 重叠 59px、按钮超出 header 39px，按命中区放大 header/会话/动作区；正常入口 PID 18692 复验 header 99px、More/Hide 间距 8px、Local/关闭叉完整。详见[终端大字号动作区修复](terminal-large-actions-fix-2026-10-19.md)；旧回退证据保留在[ui-size 40 复验](terminal-large-font-40-2026-10-19.md)。
- 终端深色状态配对完成（状态级，不计入页面级 48）：深色 8 行失焦；面板顶 +1、首行 +9、步长 31/32、提示符 -14、状态栏 +8；编辑器/行号栏底已从工具面板 `#191A1C` 修为参考 `#1E1F22`，终端正文/文字核心 `#1E1F22`/`#BCBEC4` 一致；面板顶边色已修复为两端 `#212326`；子像素墨迹数 1148 vs 1235 仍差。详见[终端深色状态配对](terminal-dark-pair-2026-10-18.md)。
- 深色 `--augit-accent-brand` 由误用的浅色 `#3871E1` 订正为权威 `Blue6 #3574F0`（浅色不变）；直接计算样式：深色 body `#3574F0`、激活工具轨 `rgb(53,116,240)`，浅色仍 `#3871E1`/`rgb(56,113,225)`。新增 `verify-ux-dark-accent.cjs` **PASS**；`frame-buttons` size13 的 accent/pushed/hover/faint 已过，仍在激活工具轨边框既有期望 `#1E1F22` vs 实际 `#191A1C` 处失败，`titlebar`/`document-button-states` 仍有旧中文/数组期望失败，均不记通过。详见[深色 accent 订正](dark-accent-token-fix-2026-10-18.md)。
- 终端聚焦：点击终端内容后两端提示符均为实心方块光标，`Local` 标签/工具窗图标激活；参考多 `AI Agents` 下拉（Augit 不接 AI，参考独有），提示符位置约 1465 vs 1502 物理仍属既有命令块/面板差异。终端状态级配对完成，不增页面级 21/像素 0。详见[终端聚焦配对](terminal-focused-pair-2026-10-18.md)。
- 文本选择：`notes.txt` 双击 `line` 选词；参考 `#A6D2FF`/黑字，实际原 Chromium 默认 `#3367D1`/白字，已修为 `#A6D2FF`/继承正文色（深色计算 `#214283`/`#BCBEC4`）；`ux-spec §7.2` 的只读选择/复制可用；正常入口 PID 7796 已实测双击 `line` + Ctrl+C 得 `line`、Ctrl+A/Ctrl+C 得完整 41 字符。选择状态级配对完成，不增加页面级 21，像素合格仍 0。详见[文本选择配对](text-selection-pair-2026-10-18.md)。
- 图片：临时把 `web/assets/image-sample.png` 复制到 fixture，打开同一图片。实际 **67% fit**、棋盘格底+图片外框、工具栏缩小/百分比/放大/适应、`app.py`+图片双标签；参考 **Fit Zoom to Window**、白底无外框、工具栏网格/分屏/缩放/1:1/适应/旋转、notes/image/README 三标签。未建立配对；棋盘格位于 PNG 透明实际矩形内、外围纯色，符合 `ux-spec §7.5`，参考白底/额外工具栏属 PyCharm 差异，不修；临时 fixture 图片已删除、notes 恢复原哈希。详见[图片 scoping](image-pair-scoping-2026-10-18.md)。
- JSON：临时 11 行展开 `sample.json`，实际只读/JSON 格式化/无语法高亮/单标签，参考可编辑/JSON 语法高亮/多标签/`No JSON schema`；行号与 11 行内容一致，登记为**页面级同态配对**（已知差异，非像素合格）。临时 fixture 文件已删除。详见[JSON 同态配对](json-homologous-pair-2026-10-18.md)、[快速打开 scoping](quick-open-pair-scoping-2026-10-18.md)。
- Markdown：`README.md` **页面级同态配对**（已知差异，非像素合格）。实际分栏分隔线拖到 1759–1760，参考 1760–1761（差 1 物理）；预览标题实际 x1814/233 vs 参考 1810/243（+4/−10）；同一文件/分栏/内容/浅色。已知差异：实际单标签 vs 参考 notes+README 双标签、实际源码纯文本只读 vs 参考 Markdown/Python 语法高亮/光标/折叠槽、代码块垂直节奏更松（已核对为 design-system 规格值，非实现偏差，见[排版规格核对](markdown-typography-spec-2026-10-18.md)）。详见[Markdown 同态配对](markdown-homologous-pair-2026-10-18.md)、[scoping](markdown-pair-scoping-2026-10-18.md)。
- Git Log：**页面级同态配对**（已知差异，非像素合格）。同一提交 `test: create pixel fixture`/`main`/PixelAudit 2026/10/13 20:06/e9188bf、3 文件与详情、过滤；实际底部 `Git` 分组 + 单一 `Log` 工具标签（CDP：`tool-tab active`），参考 Git 工具窗口 `Git`/`Log`/`Console` 的 Log 激活。已知差异：Console、提交图/列、详情标题、工具窗高度与 Project 宽度。先前“Log 点击无变化”已更正为**本来已激活**，不是交互故障。详见[Git Log 同态配对](git-log-pair-scoping-2026-10-18.md)。
- 主菜单：实际点击 hamburger 展开内联 `File / View / Git / Terminal / Settings`；参考同位置本次未打开菜单，未形成配对，不作产品结论；实际内联菜单条与参考汉堡菜单结构不同。第 55 批实际 Git 菜单可见 `Create Stash…` 等，但点击 Create Stash 未打开对话框、焦点落回 Git Log 筛选，入口是否接线未定位，未写 Git。第 56 批在附 CDP 实例中取得 Stash 参考对照；第 57 批修复两复选被拆成两行的问题（`.stash-check-row`），正常入口 PID 5464 打开后 `Keep index` 与 `Include untracked files` 同行完整可见，登记为**第 13 个页面级配对**；详见[Stash 同态配对](stash-homologous-pair-2026-10-18.md)。
- Git Log 提交右键菜单：参考端条目已取证（Copy Revision、Create Patch、Cherry-Pick、Checkout Revision、Reset Current Branch to Here、Revert/Undo、Edit/Fixup/Squash/Drop、Interactively Rebase、Push All、New Branch/Tag 等）；实际在提交行 `(1000,1255)` 右键未打开菜单（一次命中筛选），Shift+F10 也未打开，Changes 右键亦未打开；入口/提交行可聚焦未定位，**未建立配对**，不作缺失/缺陷结论。详见[Git Log 提交右键菜单取证](git-log-commit-menu-scoping-2026-10-18.md)。
- Stash Manager（页面级配对，2026-10-19）：同一隔离夹具、同一 `stash@{0} test stash manager`（1 文件 `notes.txt`），实际 PID 14568 的 `Stash` 对话框（Stash Manager、Apply/Pop/View Content/Delete、Contains 1 files、Close）与参考 PID 15680 的 Git 工具窗 `Stash` 标签（同名 stash/main、1 file、Apply/Pop）同语义。参考入口已更正为 Find Action **`Unstash Changes...`**（`Git.Unstash`），不是 `Stashes`/`Stash Manager` 文字。未执行 Apply/Pop/Delete；页面级 **49→50**、像素仍 0。详见[Stash Manager 同态配对](stash-manager-homologous-pair-2026-10-19.md)。
- 全仓搜索：实际 `Ctrl+Shift+F` 为紧凑 Find in Files 浮层，`Ag` 结果含 notes.txt 与 .idea；参考端快捷键/Find Action 对话框焦点不可靠，`Ag` 曾落到 notes.txt 编辑区（已撤销、磁盘未变），未捕获参考结果，**未建立配对**。详见[全仓搜索 scoping](full-search-pair-scoping-2026-10-18.md)。
- Commit：左侧 Commit 工具窗口；Changes 1 个 `app.py` 勾选、Unversioned 6 个 `.idea` 文件、Amend、空提交信息、Commit/Commit and Push、notes.txt、底部 Git Log 均与参考同态。已知差异：实际无 “1 modified”、占位小写且无聚焦蓝框、工具栏图标较少、底部 Log 选中态不同。实际通过轨道 `aria-label=Commit`（物理 32,187）打开，此前 Ctrl+K/坐标错误已更正。页面级配对 5→6。详见[Commit 同态配对](commit-homologous-pair-2026-10-18.md)。
- 工作区 Diff：实际/参考均为 Commit 左栏 + `Commit: app.py` 并排 Diff，相同 5 行、第 5 行 `# changed for pixel audit` 绿色、1 difference；差异为 `HEAD/Workspace` vs 哈希/`Current version`、`LF/Mixed` vs `CRLF/LF`、实际无编辑铅笔（只读）、工具栏与底部 Log。页面级配对 6→7。Unified 上下文行号缺失已修复：live unified 模板输出 old/new 两列行号，正常入口实际为 `1/1`…`5/5`；`tools/test-diff-unified-line-numbers.mjs` 4/4 PASS；修复后 `verify-ux-offline-icons.cjs` 再 PASS 110 场景/主题。剩余标题/行尾/编辑铅笔/工具栏差异，不改变页面级 7、仍非像素合格。详见[Diff 同态配对](diff-homologous-pair-2026-10-18.md) §6。
- File History：实际 Project 右键 `app.py` → File History；参考 Project 选中 `app.py` → Find Action Show History；两端同一提交 `test: create pixel fixture`/`e9188bf`/PixelAudit 2026/10/13 20:06、`Branch: HEAD`、`History: app.py`、右侧 app.py 全绿 Diff。差异：实际有 Revision/Date/Author/Commit message 列头与 `e9188bf^`/`e9188bf` 头，参考无 Revision 列、头为 `e9188bf3 app.py`，底部缺 Console。页面级配对 7→8。详见[File History 同态配对](file-history-homologous-pair-2026-10-18.md)。
- Blame：实际 Project 右键 `app.py` → Blame（`.idea` 目录右键无反应属目标为目录，不是缺陷）；参考 `app.py` 编辑器 + Find Action Annotate with Git Blame。两端 5 行、1–4 行 `2026/10/13 PixelAudit`、第 5 行未提交。未提交行归属已修复：全零 hash/`Not Committed Yet` 渲染空白槽、仅保留行号，测试 5/5 PASS；`offline-icons` 再 PASS 110。剩余只读文档 vs 可编辑注释、usage inlay、Console、几何差异。页面级配对 8→9。详见[Blame 同态配对](blame-homologous-pair-2026-10-18.md)。
- 分支弹层：实际点击标题栏 `main`（物理 380,35）、参考点击 branch widget `main`（360,35）；两端同一搜索框、Update Project/Commit/Push/New Branch/Checkout Tag or Revision、Local/main。实际 `main Current` 且展开当前分支行菜单（From main/Compare/New Worktree/Push/Rename），参考无该展开、有快捷键和右上关闭/设置图标。页面级配对 9→10。详见[分支弹层同态配对](branches-homologous-pair-2026-10-18.md)。
- Checkout 对话框：实际分支弹层 `Checkout Tag or Revision…`、参考 Find Action 同动作；两端单引用输入框空态、关闭/取消/主操作。实际标题 `Checkout Tag or Revision`、标签 `Tag or revision`、`Cancel/Checkout`、无标题图标；参考标题 `Checkout`、`Enter reference…`、`OK/Cancel`、带图标；未执行 Checkout。页面级配对 10→11。详见[Checkout 对话框同态配对](checkout-dialog-homologous-pair-2026-10-18.md)。
- New Branch 对话框：实际分支弹层 `New Branch…`、参考 Find Action `New Branch`；两端模态、分支名输入、Create/Cancel。实际标题 `New Branch`、空输入、无 Checkout branch/Overwrite existing branch、`Cancel/Create`；参考 `Create New Branch`、预填 `main` 全选、Checkout 勾选/Overwrite 未勾选、`Create/Cancel`、带图标。缺分支选项属功能/结构差异。未创建分支。页面级配对 11→12。详见[New Branch 对话框同态配对](new-branch-dialog-homologous-pair-2026-10-18.md)。
- Stash 对话框：实际正常入口（Git 菜单 Create Stash，PID 5464）与参考同一 `Stash`、Git root、当前分支 `main`、Message、Keep index、Create Stash/Cancel；修复后 `Keep index` 与 `Include untracked files` 同一行完整可见。实际 Git root 为只读文本、有用户裁决的 Include untracked、无历史/帮助/图标；参考 Git Root 下拉、无 Include、有历史/帮助/图标。页面级配对 12→13。详见[Stash 同态配对](stash-homologous-pair-2026-10-18.md)。
- Reset 对话框：实际 Git 菜单 `Reset Current Branch…`（PID 24956）、参考 Git Log 提交右键 `Reset Current Branch to Here…`；同一目标 `e9188bf`、默认 Mixed、Reset/Cancel、说明，均未执行 Reset。实际标题 `Reset Current Branch`、Target commit 文本框、Mode 下拉、仅显示当前模式说明；参考 `Git Reset`、头部提交摘要、Soft/Mixed/Hard/Keep 四档单选及全部说明、帮助图标；尺寸不同。页面级配对 13→14。详见[Reset 对话框同态配对](reset-dialog-homologous-pair-2026-10-18.md)。
- Push 对话框（无 upstream）：实际 Git 菜单 `Push…`、参考 Find Action `Push`；两端同一 `main`、`Define remote`、无远端/上游、Push tags `All`、Push 禁用、Cancel，均未 Push。实际标题 `Push`、右侧 “Unable to load push preview/无 upstream”、无右侧工具栏/帮助；参考 `Push Commits to fixture`、`main →`、`No commits selected` + 工具栏、帮助、Push 下拉；尺寸不同。页面级配对 14→15。详见[Push 对话框同态配对](push-dialog-homologous-pair-2026-10-18.md)。
- Git Log 提交右键菜单：实际正常入口（PID 4428）在提交行物理 `(1411,1288)` 右键打开；实际 `Copy Commit Hash / Cherry-pick / Compare with Workspace / Reset Current Branch Here... / Revert Commit / New Branch... / New Tag...`，参考另有 Create Patch/Checkout Revision/Show Repository/Undo/Edit/Fixup/Squash/Drop/Rebase/Push All/Go to Child 等复杂 Git 动作；已知范围差异。第 66 批点击 Compare with Workspace 两次菜单仍在、比较视图未打开，接线/命中未定位，不判缺陷。页面级配对 15→16。详见[Git Log 提交右键菜单同态配对](git-log-context-menu-homologous-pair-2026-10-18.md)。
- New Tag 对话框：实际 Git Log 提交右键 `New Tag...`（PID 23196）、参考同菜单 `New Tag…`（PID 4488）；两端同一 `Create New Tag On <hash>`、`Enter the name of new tag`、空输入、取消/主操作，均未创建标签。差异：实际完整长哈希换行、无标题图标/问号；参考哈希截断、有图标/问号。空输入主按钮已修复（第 70 批：实际空名 `Create` 禁用、填名启用，对齐参考 `OK` 禁用）；窗口约 700×287 vs 706×270。页面级配对 16→17。详见[New Tag 对话框同态配对](new-tag-dialog-homologous-pair-2026-10-18.md)。
- Clone Repository：实际工作区 chip → Open Workspace → `Clone Repository…`（PID 9064）；参考 Find Action `Clone Repository` → 左侧 `Repository URL`。两端同一 Git/URL 空/Directory/Shallow 1/Clone 禁用/Cancel，均未 Clone。差异：实际无 provider 列表/图标/More via plugins、`Repository URL` vs `URL`、默认目录 `D:\github\Augit` vs `C:\Users\wl024\PycharmProjects`、窗口尺寸不同。页面级配对 17→18。详见[Clone Repository 同态配对](clone-dialog-homologous-pair-2026-10-18.md)。
- New Worktree：实际分支 chip → 分支弹层 → `New Worktree…`（PID 16224）；参考 Find Action `New Worktree`（PID 25280）。两端同一 New Worktree/分支/New Branch/目录/Cancel/主操作，均未创建。差异：实际 `Branch` 文本框 vs 参考 `From branch` 下拉、无 `Project name`、`Directory` vs `Location`、主按钮 `Create` vs `Create & Open Worktree`；**实际字段布局重叠/截断已修复**（第 69 批：`.worktree-new-branch-row` 跨两列、复选与名称输入同行，正常入口 PID 14008 复验）；窗口约 1627×639 vs 855×541。页面级配对 18→19。详见[New Worktree 对话框同态配对](new-worktree-dialog-homologous-pair-2026-10-18.md)。
- New Worktree 字段布局修复：根因是 `New Branch` 复选行只占 `form-grid` 第 1 列，Directory 标签被挤到同行第 2 列、目录输入落到下一行；改为跨两列 flex 同行后，Branch/New Branch/Directory 三行对齐、占位完整。`tools/test-worktree-check-row.mjs` 6/6、offline 110 PASS、正常入口 PID 14008 复验。详见[New Worktree 布局修复](new-worktree-layout-fix-2026-10-18.md)。
- 紧凑输入空名称主按钮修复：`showCompactInput()` 现在按 `field.value.trim()` 同步确认按钮禁用态并监听 `input`；New Branch / Rename / New Tag 空名称不可提交。`test-compact-input-empty.mjs` 7/7、正常入口 PID 3308 空态禁用/填名启用；未创建标签。详见[紧凑输入空名称修复](compact-input-empty-primary-fix-2026-10-18.md)。
- Compare with Workspace：实际分支弹层 `Compare with Workspace`（PID 15028）打开 `Compare: app.py:main ↔ Workspace`；参考提交右键 `Compare with Local` → Changes 列表双击 `app.py` 打开 `Changes: app.py`。两端同一 app.py、1 difference、拆分视图、绿色第 5 行、右侧工作区内容，均未写 Git。实际左右两侧均有代码内容（原始 PNG 像素采样纠正预览误读：左 464 / 右 530 暗像素，步长 3）；标签/头 `main` vs `e9188bf3`、`Workspace` vs `Current version`、行尾措辞/工具栏不同。页面级配对 19→20。详见[Compare with Workspace 同态配对](compare-workspace-homologous-pair-2026-10-18.md)。
- Commit 空态：第 83 批两次失败后，第 86 批在参考 `trusted-paths.xml` **临时预信任**同一临时 clone（取证后恢复原文件），Trust 不再出现、参考主窗口正常前台并 Ctrl+K。实际（PID 2100）`No changes to commit` / Commit 禁用、底部 Log；参考（PID 22704）`Changes` 空、`Unversioned Files 5 files`、Commit 仍可用、README 编辑器、无底部 Log、IDE settings 通知。同一干净 clone、无已跟踪修改、均未写 Git；页面级配对 **26→27**。详见[Commit 空态同态配对](commit-empty-homologous-pair-2026-10-18.md)。
- Push 失败结果更正（第 95 批）：原“non-fast-forward”fixture 实为本地在 other commit **之后**的新提交（`ahead 1`，快进），Push **实际成功**；CDP 复现 `__augitPushError=null`、对话框正常关闭、bare 更新为本地提交。因此“失败无错误反馈/代码意图不一致”作废。
- Find in Files 零结果：实际 PID 24336 查询 `zzzznotfound` 显示 `No results found` + `Try another query or adjust the filters.`；参考 PID 17156 用 Find Action 打开工具窗（标题为空，HWND 3475234）并**直接对该 HWND 输入**，显示 `Nothing found` + File mask `*.py`。同一查询/无结果/In Project/未改文件；页面级配对 **39→40**。详见[Find in Files 零结果同态配对](search-no-results-homologous-pair-2026-10-18.md)。
- Push 成功结果（状态级配对）：实际 PID 16616 toast `Push`/`Pushed to origin/main.`（`07AA42B2…BF48`）与参考 PID 22152 balloon `Pushed 1 commit to origin/main`（`4BB37938…2B7A`）对应同一“1 个提交成功推到 origin/main”；已知差异为 toast vs balloon/文案。状态级补充，**不改变页面级 48**；参考进行中仍未采。详见[Push 成功结果状态配对](git-result-state-pair-2026-10-18.md)。
- 大文件 Warning 动作（真实输入已闭环）：第 141 批“点击无响应”和第 142 批“物理点击未命中/工具限制”的解释均已更正。2026-10-19 用正常入口、28,500,000 字节隔离夹具、`SendInput`/`SetCursorPos` 真实鼠标（未启用 CDP）完成：Hide 后设置仍 `false`、关闭重开仍隐藏、正常重启后横幅重新出现；Don't Show Again 写入 `true`、横幅消失、重启后仍隐藏。1.2s 截图里横幅仍在，屏幕可见变化落在约 3.7–4.2s，属本样本重绘耗时观察，不据此判缺陷。参考 2026.2.1 同文件为普通编辑器、无 Warning 横幅/动作，故没有参考警告 UI 可配对。本项不增加页面级（当时总数 48；当前 49），像素仍 0。详见[真实输入与持久性闭环](large-warning-real-input-2026-10-19.md)、[动作复核](large-warning-actions-verify-2026-10-18.md)。
- 大文件 Warning 横幅（已修复）：CDP 确认 DOM 与状态正确（`status=TextPreview`、`readOnlyPreview=true`、横幅存在），但横幅作为 `.document-view` 额外 grid item 没有独立行，`getBoundingClientRect().height` 仅 **13.14px**、被正文挤出视口。第 140 批给 `.editor-notification` 加 `min-height:26px/box-sizing:border-box` 并新增三条 `:has(> .editor-notification)` 网格行；`tools/test-large-file-banner-css.mjs` **8/8**；正常入口 PID 14720 显示 **`Large file (29.9 MB). Showing the first 2.4 MB as a read-only preview.` + Hide Notification / Don't Show Again**（`E7B78435…A184`）。参考端同文件未显示大文件警告（见[真实输入与持久性闭环](large-warning-real-input-2026-10-19.md)）；实际侧状态，本项不增加页面级（当时总数 48；当前 49）。详见[大文件 Warning 横幅修复](large-warning-fix-2026-10-18.md)。
- 大文件 Warning 横幅缺陷（历史）：`hideLargeFileWarning=false` 的全新会话打开 31.4MB `huge.txt`，修复前只显示只读预览、无警告横幅（`0B7B760C…CBC7B`）；根因与修复见上条。详见[大文件 Warning 横幅缺失](large-warning-defect-2026-10-18.md)。
- Appearance UI 字号保存（实际侧状态）：实际 PID 24232 用 UIA 聚焦 `ui-font-size` + 真实按键 12→13，Apply 后 `settings.json` `textFontSize=13`（`FF06C052…0F11`），Appearance 页 Size 13、Apply 禁用（`09EC73C0…9822`）；已恢复 `B8868855…DC3D`。参考未采，不构成配对；本项不增加页面级（当时总数 48）。详见[Appearance UI 字号保存态](settings-ui-font-save-2026-10-18.md)。
- File View 等宽字体/字号（按用户裁决，无缺口）：`docs/design-system.md` 第 5 行（2026-10-15 用户裁决）规定等宽正文固定 `JetBrains Mono 13` 并**取消设置页的等宽字体/字号选择**；实际 File View 的只读 `Size 13` 是正确的产品行为，参考 `Editor > Font` 可编辑是已知产品差异，不配对。第 136 批曾误加输入框，第 137 批已回退，`mockup.js` 回到 `F3D0CD53…671D`，`tools/test-file-view-code-size.mjs` 6/6 守护固定展示，正常入口 PID 4852 复验只读页（`E23F1CB2…F6B1`）。本项不增加页面级（当时总数 48）。详见[File View 等宽字体/字号取证与纠正](settings-save-scoping-2026-10-18.md)。
- Diff 上限（状态级配对，2026-10-19）：同一 25,380,029 字节 / 270,000 行 `big.txt`、同一提交 `70a24f0`，实际 PID 17436 在 `Commit: big.txt` 显示 `Text diff is unavailable for this file (SideTooLarge).`，参考 2026.2.1 PID 12384 显示 `Cannot show diff … The file is bigger than 20.97 MB.` + `Reload`；两端都无 diff 正文。参考用“选中行 + Ctrl+D”成功，先前双击失败是操作路径问题。阈值文案/动作不同，登记为**状态级**，本项不增加页面级（当时总数 48；当前 49），像素仍 0；`Diff 局部加载` 页仍未完成。详见[Diff 上限参考态](diff-limit-reference-2026-10-19.md)、历史[取证](diff-limit-scoping-2026-10-18.md)。
- Diff 局部加载（未配对）：fixture 临时 4.19MB 未跟踪 `large.txt`；实际 PID 20176 Commit 视图双击打开 `Commit: large.txt`，`1 difference`、HEAD 空/Workspace 全绿新增，可见第 0–21 行，**无 Loading/partial/limit 提示**，可继续滚动；未产生暂存；参考未启动。无法证明“局部加载”页，页面级仍 46。详见[Diff 局部加载取证](diff-partial-scoping-2026-10-18.md)。
  - **2026-10-19 补**：8,740,031 字节 / 92,001 行文件末尾有一行改动，实际宿主只回传前 2000 行且旧 UI 显示 `0 differences`；参考同一文件完整显示 **1 difference** 与第 92001 行。已让 UI 使用 `truncated`：工具栏显示 `Truncated`，正文顶部提示“只显示前 2000 行，其余改动未显示”，并新增网格行避免与正文重叠。这是实现上限+提示修复，不是参考同态；页面级仍 50、像素 0。详见[Diff 截断提示修复](diff-truncation-notice-fix-2026-10-19.md)。
- Smart Checkout（页面级配对，2026-10-19）：同一隔离夹具（main 本地改 `app.py`，feature 改同一文件）两端都弹出 `Git Checkout Problem`；实际 PID 14576/20156，参考 PID 3152。实际说明复用 `.commit-meta` 的单行 `nowrap` 被截成 `... and resto`，已加 `smart-checkout-impact` 换行规则并同步视觉稿；`test-smart-checkout-copy.mjs` 5/5、`verify-ui-assets` PASS。实际取消 no-op；实际 Smart Checkout 后分支 `feature`、本地 `# local note` 与 `# feature branch` 同时存在、stash 为空。参考只验证取消 no-op，未执行确认动作。已知差异：实际无 Force Checkout、文案/文件列表/尺寸不同。页面级 **48→49**、像素仍 0。详见[Smart Checkout 配对与修复](smart-checkout-homologous-pair-2026-10-19.md)。
- Git 操作结果（已修复，状态级）：此前实际 PID 996 Push 成功无显式结果（仅日志更新）；本轮新增 `pushSuccessText` 并在成功路径 `showToast({ title: "Push", text: "Pushed to origin/main.", kind: "success" })`，正常入口 PID 16616 捕获 `Push`/`Pushed to origin/main.` toast；参考 PID 3268 balloon `Everything is up to date`。状态级同态（toast vs balloon，已知差异），**不改变页面级 47**；`Git 操作结果` 页面级仍未闭环。测试 `tools/test-push-result-toast.mjs` 7/7；共享 UI 资产未变。详见[Git 操作结果配对与修复](git-result-homologous-pair-2026-10-18.md)。
- Git 操作进行中（状态级配对，2026-10-19）：改用只接受连接、不响应 HTTP 的本机远端制造真实慢 Push，不再依赖 `pre-receive sleep`。同一夹具、同一 `main → origin/main` 的 1 个提交，实际 PID 18352 的 Push 对话框显示 `Pushing...`，参考 PID 23276 状态栏显示 `Pushing...` + 进度条；服务端确认收到 `git-receive-pack`。实际点击 Cancel 显示 `Operation cancelled.`、分支仍 `ahead 1`、推送进程消失；参考点击 `×` 的首次坐标落在进度条上方，**2026-10-19 复核按正确坐标 `(2358,1634)` 点击后 `Pushing...` 消失、分支仍 `ahead 1`、推送进程消失**，参考取消已通过。登记为状态级，不增加页面级 50、像素仍 0；失败状态已采，结果页仍未闭环。详见[真实慢远端状态配对](git-progress-hanging-remote-2026-10-19.md)、[参考取消复核](git-cancel-reference-2026-10-19.md)。
- Git 操作结果（Fetch 尝试，未配对）：实际 PID 9720 Git 菜单 `150,1437` 实际打开 **Push**（无 upstream 页，已配对）；改点 `150,1397` 后主窗原样，**未捕获 Fetch 完成 toast**；参考未启动。页面级仍 47。详见[Fetch 结果取证](fetch-result-scoping-2026-10-18.md)。
- Push 网络失败（状态级配对，2026-10-19）：同一隔离夹具、同一 `97e1c76` 提交，远端 `http://127.0.0.1:1/repo.git` 返回 HTTP 502；实际 PID 7444 在 Push 对话框内显示原始 `fatal: ... 502`，参考 PID 22960 显示 `Push failed` balloon + `Show details in console`；两端都未推送、分支仍 `ahead 1`。这是网络失败，不是 non-fast-forward；登记为状态级，不增页面级 49、像素仍 0。详见[Push 网络失败状态配对](git-push-failure-2026-10-19.md)。
- 搜索截断：实际 PID 5344 Find in Files `012345` → `Too Many Results`（More than 1000，Continue/Abort）；参考 PID 15200 清空 mask 后点搜索放大镜 → `100+ matches in 2+ files`（notes.txt + large.txt，结果截断）。同一查询/文件/截断/未写；页面级配对 **46→47**。**超时未覆盖**，不能把本配对当成“搜索截断/超时”全部完成。详见[搜索截断同态配对](search-truncation-homologous-pair-2026-10-18.md)。
- 大文件只读预览：fixture 临时 31,388,893 字节 `huge.txt`（超过内容加载上限 20MB）；实际 PID 3516 打开 `huge.txt`，`Read-only` 只读预览第 0–24 行，无 Warning 横幅（`largeFileWarningDismissed` 已持久化）；参考 PID 6156 打开同一文件，普通编辑器展示同一内容，无“File is too large”对话框/警告。同一 31.4MB/预览内容/未写；页面级配对 **47→48**。**警告横幅本身的显示状态未覆盖**（两端警告均已被关闭）；二进制摘要页仍为产品专有差异。详见[大文件只读预览同态配对](large-file-limit-homologous-pair-2026-10-18.md)。
- 大文件限制（4.19MB 尝试，未配对）：fixture 临时 4.19MB `large.txt`；Project 树双击行坐标两次落错；Quick Open（Ctrl+P→`large`→Enter）实际成功打开 `large.txt`，`Read-only`，第 0–23 行正文，**4,188,893 字节无“文件过大”限制/提示**。参考 Ctrl+Shift+N 的 Search Everywhere 未列出该新建文件（索引未更新），未采到大文件警告。实际“无限制直接打开”与参考 2.5MB 阈值是产品差异/待确认规范，不是同一页面，页面级仍 46。详见[大文件限制取证](large-file-scoping-2026-10-18.md)。
- 关闭运行中终端：实际 PID 11736 正常入口 → Terminal（轨道物理约 `32,1546`）→ 点击终端正文 → `ping -t 127.0.0.1` 持续输出 → 点击 `Local` 标签关闭 → `Close Terminal` / “A command is still running…” / `Keep Terminal` / `Stop Command and Close`；参考 PID 4448 Alt+F12 → 同一 ping → 关闭 `Local` → `Process 'Terminal Local' Is Running` / `Don't ask again` / `Terminate`/`Cancel`。同一运行中终端确认/未终止/未写；页面级配对 **45→46**。详见[关闭运行中终端同态配对](close-terminal-homologous-pair-2026-10-18.md)。
- 关闭运行中终端（过程记录）：CDP 量得 Terminal 轨道按钮 CSS `[0,866,37,40]`（物理约 `32,1546`），纠正先前 `34,1678` 点空的坐标错误；实际 PID 20172 打开 Terminal（Local、Windows PowerShell），输入 `ping -t 127.0.0.1` 后点击 `Local` 标签关闭位置，终端面板关闭但**未捕获运行中确认**，命令/确认状态未区分；参考未启动。页面级仍 45。详见[关闭运行中终端取证](close-terminal-scoping-2026-10-18.md)。
- 打开工作区（参考已采，语义状态）：实际 PID 20100 从工作区 chip 的 `Open Workspace...` 打开 `Open Workspace` 对话框（Recent Workspaces、Choose Directory、Clone Repository、Cancel）；参考 PID 14564 无项目启动取得 `WelcomeScreen`（Open/New/Clone、搜索项目、最近 fixture、New Script/New Notebook/Open File/Learn/Plugins）。两者共享最近项目/打开/克隆语义，但实际是已有工作区上的模态、参考是无项目 Welcome，上下文与结构不同；登记为语义状态参考，**不增加页面级 49**、像素仍 0。未执行实际选择目录/克隆或参考 Project 打开动作。详见[Open Workspace 参考采集](open-workspace-reference-2026-10-19.md)。
- Settings/File View：实际 PID 1820 File View（Monospaced font JetBrains Mono Size 13、说明、OK/Cancel/Apply）；参考 PID 4268 Editor > Font（JetBrains Mono 13.0、Line height 1.2、ligatures、Typography、预览、Apply 禁用）。同一字体/字号/未保存；页面级配对 **44→45**。详见[Settings/File View 同态配对](settings-fileview-homologous-pair-2026-10-18.md)。
- Stash 管理（未配对，第 118 批补充）：参考端 Find Action `Stash` 打开的是 **Stash Changes 创建对话框**（Git Root/Current Branch/Message/Keep index/Create Stash），对应当已配对的 `Stash` 页面（第 14 对），**不是 Stash Manager**；参考未找到 Stash Manager 同态页。页面级仍 44。详见[Stash 管理取证 §1.2](stash-manager-scoping-2026-10-18.md)。
- Stash 管理（未配对）：临时 clone 构造 1 条 stash（`stash@{0}: On main: test stash manager 2`）。实际 PID 13592 从主菜单 Git 底部 `Stashes...`（物理约 `150,1546`）成功打开 `Stash` 对话框：Stash Manager、`stash@{0}` 列表、Apply/Pop/View Content/Delete、Contains 1 files notes.txt；参考 PID 11132 Git Log 仅 main 提交，Git/Log/Console 无 Stash 列表/页。实际 Stash Manager 是产品专有对话框，未找到参考同态页，页面级仍 44。详见[Stash 管理取证](stash-manager-scoping-2026-10-18.md)。
  - **2026-10-19 更正**：参考入口是 Find Action **`Unstash Changes...`**（`Git.Unstash`），打开 Git 工具窗口 `Stash` 标签；已与同一 stash 完成页面级配对，页面级 49→50。见[Stash Manager 同态配对](stash-manager-homologous-pair-2026-10-19.md)。
- 不可预览文件（未配对）：临时 `sample.bin`（14B）双击，实际显示 `This file cannot be previewed in Augit` + Binary file + Open in Default Application；参考弹 `Register New File Type Association`（`.bin` 未知类型）。改用有效 `sample.zip`（137B）后，实际同样显示二进制摘要（`sample.zip · Binary file · 137 B`），参考双击只选中 Project 行、编辑器仍 app.py，没有二进制页/标签。属文件类型关联/二进制预览差异，不判缺陷；页面级仍 44。详见[不可预览文件取证](binary-file-scoping-2026-10-18.md)。
- 初始化仓库：隔离非 Git 目录 `D:/AugitAcceptance/init-fixture`；实际 PID 22996 自动弹出 `Create Git Repository`（Destination、Choose Directory、Cancel/Create）；参考 PID 17060 预信任后 Alt+9 打开 `Version Control` 空态（Create Git repository... 链接）。同一非 Git/创建入口/未初始化；页面级配对 **43→44**。临时目录删除、信任恢复。详见[初始化仓库同态配对](init-repo-homologous-pair-2026-10-18.md)。
- Git 不可用：实际 PID 2320 将 `gitExecutablePath` 设为 `C:/nonexistent/git.exe` 后启动，右下 `Git unavailable` toast + `Configure git.exe`，Git Log 显示 Loading/No references；参考 PID 3000 进程 PATH 移除 Git 后启动，Git 工具窗为空、无可见通知。同一 Git 不可用/文件可浏览/未写；页面级配对 **42→43**。实际设置已恢复，参考配置未改（无 git.xml）。详见[Git 不可用同态配对](git-unavailable-homologous-pair-2026-10-18.md)。
- Quick Open 结果：实际 PID 23744 在编辑区焦点后 Ctrl+P 输入 `notes`，Open File 单条 `notes.txt`；参考 PID 2424 Ctrl+Shift+N 输入 `notes`，Search Everywhere Files 单条 `notes.txt` + 路径/Open in Right Split。同一查询/结果/未打开文件；页面级配对 **41→42**。详见[Quick Open 结果同态配对](quick-open-results-homologous-pair-2026-10-18.md)。
- Quick Open 空态：实际 PID 24036 **先在编辑区点击**使 WebView 获得键盘焦点，再 Ctrl+P，打开 `Open File` 空浮层；参考 PID 23172 Ctrl+Shift+N 打开 `Search Everywhere` 空浮层（All/Classes/Files/Symbols/Actions/Text、Project Files）。同一空输入/无查询/未打开文件；页面级配对 **40→41**。先前 Ctrl+P 未打开是焦点问题，已更正。详见[Quick Open 空态同态配对](quick-open-empty-homologous-pair-2026-10-18.md)。
- Find in Files 结果：实际 PID 9908 浮层查询 `Ag` 返回 5 条（notes.txt + `.idea` modules.xml/fixture.iml×2/profiles_settings.xml）；参考工具窗 1 match in 1 file（notes.txt，File mask `*.py`）。首条结果一致、均未改文件；实际包含 `.idea` 未跟踪 XML 为真实结果集差异。页面级配对 **38→39**。详见[Find in Files 结果同态配对](search-results-homologous-pair-2026-10-18.md)。
- Find in Files 空态：实际 PID 20360 左侧搜索轨按钮打开浮层（空查询、刷新/W/`.*`/Include ignored files）；参考 PID 23800 改用 Ctrl+Shift+A → `Find in Files` 打开工具窗并清空查询（File mask `*.py`、In Project、`Type search query to find in files`、Open in Find Window 禁用）。同一空查询/无结果/In Project/未搜索；实际浮层 vs 参考工具窗；页面级配对 **37→38**。详见[Find in Files 空态同态配对](search-empty-homologous-pair-2026-10-18.md)。
- Settings/Terminal：实际 PID 17164 齿轮 → Settings → Terminal（Shell Windows PowerShell、Custom startup command）；参考 PID 24536 Ctrl+Alt+S → 搜索 Terminal → Tools > Terminal（Reworked 2025、Command Completion、Start directory、JetBrains Mono 13.0）。同一 Terminal/字体族/未保存；页面级配对 **36→37**。**发现并修复 Apply 初始可用+蓝点**：`settingsDirtyPages()` 把 `null` 与 `""` 判成不同，`terminalCustomCommand` 空值导致 Terminal 页初始脏；修复后两份 `mockup.js` `F3D0CD53…671D`，`tools/test-settings-dirty-null.mjs` 7/7，正常入口 PID 18236 验证 Apply 禁用、无蓝点。详见[Settings/Terminal 同态配对](settings-terminal-homologous-pair-2026-10-18.md)。
- Settings/Git：实际 PID 23424 齿轮 → Settings → Git（git.exe 留空搜索 PATH、Detection 2.45.1、Minimum 2.40、OK/Cancel/Apply）；参考 PID 11612 Ctrl+Alt+S → 搜索 Git → Version Control > Git（Auto-detected 同一路径、Test、Commit/Push/protected branches）。同一 Git 路径/未保存；实际轻量检测页 vs 参考完整 Git 配置；页面级配对 **35→36**。详见[Settings/Git 同态配对](settings-git-homologous-pair-2026-10-18.md)。
- Settings/Appearance：实际 PID 21584 齿轮打开 `Settings — Augit`（Appearance、Theme Light、Microsoft YaHei UI 12、Restore Reference Appearance、OK/Cancel/Apply）；参考 PID 5468 Ctrl+Alt+S 打开 `Settings – notes.txt` Appearance（Theme Islands Light、Use custom font 12、Accessibility/UI Options、Apply 禁用）。同一 Appearance/Light/UI 字体 12/未保存；页面级配对 **34→35**。本页不替代设置慢开/白闪/关闭性能验收。详见[Settings/Appearance 同态配对](settings-appearance-homologous-pair-2026-10-18.md)。
- 项目根节点右键（第 99 批，修复+配对）：根节点空 `data-tree-path` 导致右键无反应；新增 `treeContextPath()` 回落 `live.root`，`tools/test-project-root-context.mjs` 6/6；正常入口 PID 12592 验证根节点出现 6 项菜单。参考根节点完整 IDE 菜单；页面级配对 **33→34**。详见[项目根节点右键菜单同态配对](project-root-context-homologous-pair-2026-10-18.md)。
- 项目文件右键菜单：实际 PID 2592 在 `fixture/app.py` 行右键（物理 `150,271`）得到 6 项 Copy Path/Show in Explorer/Open in External Terminal/Refresh/File History/Blame；参考 PID 22288 同一 `app.py` 右键得到完整 PyCharm 菜单。同一文件行/菜单/未执行动作；页面级配对 **32→33**。**根节点右键实际仍无菜单**，为未覆盖缺口。详见[项目文件右键菜单同态配对](project-file-context-homologous-pair-2026-10-18.md)。
- 冲突列表：隔离 clone 构造 `notes.txt` 冲突；实际 PID 22028 弹出 `Merge Conflicts`（1 conflicting file、Abort Merge、Continue Merge 禁用、Remaining: 1 unresolved conflict）；参考 PID 10716 显示 `Merging main` 标题栏 + 绿色继续/红色中止、无独立模态。同一 1 个未解决冲突、未提交、提供继续/中止；页面级配对 **31→32**。详见[冲突列表同态配对](conflict-list-homologous-pair-2026-10-18.md)。
- Push 被拒（第 96 批，真正分叉）：`pf3-local` 与远端 main 各自新增提交（local 47507e5 / remote 3f2b316），实际 Push 后对话框保持并显示原始 Git 拒绝文本，参考弹出 `Push Rejected`（Merge/Rebase/Cancel）。同一被拒、未推、无 force；实际内联 raw error+重试 vs 参考摘要模态+Merge/Rebase。页面级配对 **30→31**。详见[Push 被拒同态配对](push-rejected-homologous-pair-2026-10-18.md)。
- Push 成功结果：临时 bare + 1 待推提交的 clone；实际 PID 19496 用 UIA 定位 Push 按钮并 Invoke，bare 收到 `20ee5ad test: pending success keyboard`，Git Log/`main`/`origin/main` 同步更新；**没有成功 toast/结果页/通知**，成功只体现在 Log/引用。参考未采集，未建立配对；失败/超时结果页仍待验证。详见[Push 成功结果取证](push-success-result-scoping-2026-10-18.md)。
- Push 成功/结果：临时 bare + 1 待推提交的 clone；实际点击物理 `(2167,1256)` 落在主窗、未命中 Push 按钮，未取得成功率/结果页，参考未启动，**未建立配对**；临时 clone/bare 已删除。详见[Push 成功取证尝试](push-success-scoping-2026-10-18.md)。
- Push 待推提交：临时 bare + clone，clone 内构造 `test: pending push`；实际 PID 12372 的 Push 显示 1 commit + Target/credentials/Push 可用；参考 PID 20152 显示同名提交 + 1 file notes.txt/Push 可用。同一 1 个待推提交、未推送、Push tags All/Cancel 相同；标题/箭头/右栏/尺寸不同。页面级配对 **29→30**。详见[Push 待推提交同态配对](push-commit-homologous-pair-2026-10-18.md)。
- Push 有 upstream：临时 bare remote + clone（`.tmp/ops-*`，取证后删除）；实际 PID 18076 打开 `Push`（main→origin/main、No commits to push、0 commits、Target origin/main、credentials 说明、Push 禁用）；参考 PID 22228 预信任后打开 `Push Commits to ops-fixture`（main→origin : main、No commits selected、右工具栏、Push 可用）。同一无待推提交/未推/取消，标题/箭头/右栏/可用性/尺寸不同。页面级配对 **28→29**。详见[Push 有 upstream 同态配对](push-upstream-homologous-pair-2026-10-18.md)。
- Fetch 操作结果：无远端 fixture 上 Git 菜单 `Fetch` 点击后 2s 内未见操作结果页/提示，未启动参考、**未建立配对**，不判缺陷；需隔离可控远端 fixture 重试。详见[Fetch 操作结果取证](fetch-result-scoping-2026-10-18.md)。
- Define Remote：实际 Git Remotes `+`（PID 16620）打开 `Define Remote`（Name origin、空 URL、Push URL、Fetch remote 勾选、OK/Cancel）；参考 Manage Remotes `+`（PID 5976）打开同名表单（无 Push URL）。同一 origin/空 URL/Fetch remote/OK/Cancel，均未创建；实际 `Push URL` 为批准保留字段，高度 449 vs 363、图标 A vs PyCharm。页面级配对 **27→28**。详见[Define Remote 同态配对](define-remote-homologous-pair-2026-10-18.md)。
- Git Remotes：实际 Push → `Define remote`（PID 21596）打开 `Git Remotes`；参考 Find Action `Manage Remotes`（PID 23292）打开同一 `Git Remotes`。同一空远端、`+`/`-`/铅笔、Name/URL、`Nothing to show`、OK，均约 624×385、未写 Git；仅标题图标（实际 A / 参考 PyCharm）与局部抗锯齿差异。页面级配对 **25→26**。详见[Git Remotes 同态配对](git-remotes-homologous-pair-2026-10-18.md)。
- Changes 右键菜单：实际 Changes 行右键（PID 22784）为 `Rollback… / Show Diff / Blame / File History / Copy Path / Show in Explorer`；参考同菜单（PID 10836）另有 Commit File/Move to Another Changelist/Show Diff in a New Tab/Jump to Source/Copy Path-Reference/Delete/Add to VCS/changelist/patch/Shelve/Refresh/Local History/Git 等。同一 app.py/核心 Rollback/Show Diff/复制路径/历史入口，均未执行动作；轻量子集 vs 完整动作，顺序/分组/快捷键/图标不同。页面级配对 **24→25**。详见[Changes 右键菜单同态配对](changes-context-menu-homologous-pair-2026-10-18.md)。
- Rollback 对话框：实际 Changes 右键 `Rollback…`（PID 22784）打开 `Rollback File · app.py`（红色警告、File/Changes Modified、Cancel/Rollback File）；参考 Commit 右键 `Rollback…`（PID 10836）打开 `Rollback Changes`（文件树勾选、1 modified、Delete local copies 禁用、Rollback/Close）。同一 app.py/丢弃警告/主操作，均未回滚；单文件警告 vs 多文件树、标题/按钮/尺寸不同。页面级配对 **23→24**。详见[Rollback 对话框同态配对](rollback-changes-homologous-pair-2026-10-18.md)。
- 有效图片预览：实际 Project 树双击 64×48 PNG（PID 14920）打开图片页，工具栏 `100%`/缩放/适应，右上 `64 x 48 · PNG image · 271 B`，图片按 **100%** 显示；参考（PID 23124）适应窗口放大显示，工具栏/元数据措辞/状态不同。同一图片内容/尺寸/271 B、均只读。页面级配对 **22→23**；实际小图 100% 不放大符合 `ux-spec §7.5`“不放大小图”，参考适应窗口放大为已知平台差异，不登记为缺陷/待修。详见[有效图片预览同态配对](image-valid-homologous-pair-2026-10-18.md)。
- 损坏图片错误页：ASCII 内容的 `broken.png` 被实际按**内容签名**分类为 Text，属既定分类口径，不是缺口；改用带 PNG 签名的 `corrupt.png` 后，实际打开 `This file cannot be previewed in Augit`（15 B、Cannot read image dimensions、Open in Default Application），参考显示 `Image not loaded`（图片工具栏）。同一损坏 PNG、均不能预览/提示外部打开，页面级配对 **21→22**。详见[损坏图片错误页同态配对](image-error-homologous-pair-2026-10-18.md)。
- Worktrees 管理：实际 Git 菜单 `Worktrees…`（PID 25464）打开独立 `Worktrees` 对话框，左侧 `main · 路径`，右侧 Path/Status `Main worktree; cannot be removed`/Terminal `No running embedded terminal`、`Open Window`/`New Worktree`/`Remove...` 禁用/`Close`；参考 Find Action `Worktrees`（PID 7596）打开底部 Git 工具窗口 `Worktrees` 标签，仅单行 `fixture main 路径`。同一主工作树/路径/不可移除，均未写 Git；形态（对话框 vs 工具窗）、详情/动作、工具栏/尺寸不同。页面级配对 20→21。详见[Worktrees 管理页同态配对](worktrees-manager-homologous-pair-2026-10-18.md)。
- 快速打开：`Ctrl+P` 实际为 **Open File** 文件-only 紧凑面板；参考 `Ctrl+Shift+N` 为 Search Everywhere（All/Classes/Files/Symbols/Actions/Text）。`ux-spec` 第 106 行明确只保留快速打开文件、删除类/符号/操作/非项目搜索，实际符合产品范围；参考全分类属范围外，未建立配对。详见[快速打开 scoping](quick-open-pair-scoping-2026-10-18.md)。
- 查找条：`notes.txt` + Ctrl+F + `Ag` 状态 scoping 完成；参考为编辑器顶部紧凑浮层（含漏斗/省略号），实际为文档工具条下整行查找条（下推正文、无漏斗/省略号），两端均 `1/1`。`ux-spec` 明确要求正文顶部占一行，实际符合；参考浮层为未采用结构，登记为**有意结构差异**，不重排为浮层。未建立配对，详见[查找条 scoping](find-bar-pair-scoping-2026-10-18.md)。
- 设置：Appearance 页 scoping 完成（参考 2026.2.1 / 实际同尺寸 1792×1225 物理；参考分类树 + Editor color scheme/Zoom/无障碍/UI Options 等，实际 4 类平铺 + 受支持控件）。第 8 批 `.settings-layout` 列宽 230→246 CSS，实采分隔线 442–443，参考 443–444；第 9 批 Appearance 标签列 110→42 CSS、主题下拉 180 CSS，实采 567–873，参考 561–870（起点 +6、宽 -3 物理）；第 10 批 `.settings-page` 页顶内距 12→0，主题下拉顶部 189→168，参考 163（+5 物理），面包屑 110 vs 参考 100。字体行参考独有 “Use custom font” 复选/结构、底部 footer 等仍在；搜索自动聚焦蓝环已确认是 `live-data.js` 在 `settings-present` 前主动聚焦、与揭示/合成表面绑定，未修改，失焦对照更接近参考。深色设置 footer 曾观察到 UIA `y≈1943`；干净复测（正常入口、未移动窗口，2880×1716）设置窗口 1040×760 CSS、深色预览后 OK 在 `y=1446`、footer 正常可见；受控 resize 请求 1792×884 时窗口被最小高度钳到 1792×1120（640 CSS），OK `y=1234` 仍可见，且窗口无 WS_THICKFRAME、用户不能缩放。判定 `y≈1943` 为审计强制尺寸/过渡态伪影，**不登记为产品缺陷并关闭该 residual**；其他屏幕/DPI 的整体设置仍未验证。按 `ux-spec §7.17` 参考额外分类/控件属产品范围外，未建立同态配对；像素合格仍 0。当前无 CDP 两个样本：PID 19096（gdi）presented **645.0ms**、close 44.0ms，60fps gdigrab 输入送达延迟 2.65s 不计产品；PID 6068（ddagrab）presented **808.0ms**、close 66.6ms，输入延迟 2.74s 不计产品（808ms 为采集负载样本，不替代 645ms/599–747ms）；设置窗局部 60fps 扫描中，内容 9.30s 出现、初始无整块白帧；10.20–10.23s 的 3 帧内容空白在既有 repeat 两次开窗（gdi）与新 ddagrab 151 帧（内容 10.08s，空白 0）均未复现，判为 gdi 采集/时间戳伪影，不登记为产品缺陷。ddagrab 原生 presented 808.0ms、close 66.6ms（采集负载样本），不替代 645ms/599–747ms，也不宣称全局无闪。见[设置当前样本](settings-current-sample-2026-10-18.md)。详见[设置 scoping §7–§11](settings-pair-scoping-2026-10-18.md)。
- 54 页及全部模态子状态仍未按模块完成；页面数不是全状态分母。用户 2026-10-18 已选择“提供同态参考”；本会话已用隔离 PyCharm 2026.2.1（262.9437.214）完成首个配对，不再等待用户提供截图。首个配对发现的标签文本图标、标签图标框和关闭叉已在第 2 批修复并复验（记录 §10）；工具轨首个按钮起点、行号槽细线/宽度仍见 §6。

证据：`editor-page-partial-2026-10-17.md`、`pixel-pair-inventory-2026-10-14.md`、`terminal-matched-panel-2026-10-17.md`、`goto-line-current-pair-2026-10-18.md`、[首个同态配对](homologous-main-text-pair-2026-10-18.md)、[终端同态配对](terminal-homologous-pair-2026-10-18.md)、[终端深色状态配对](terminal-dark-pair-2026-10-18.md)、[深色 accent 订正](dark-accent-token-fix-2026-10-18.md)、[设置 scoping](settings-pair-scoping-2026-10-18.md)、[设置当前样本](settings-current-sample-2026-10-18.md)、[查找条 scoping](find-bar-pair-scoping-2026-10-18.md)、[文本选择配对](text-selection-pair-2026-10-18.md)、[终端聚焦配对](terminal-focused-pair-2026-10-18.md)、[终端选区缺口](terminal-selection-copy-gap-2026-10-18.md)、[终端选区根因收窄](terminal-selection-root-cause-2026-10-18.md)、[终端选区/复制修复](terminal-selection-copy-fix-2026-10-18.md)、[终端深色多行选区](terminal-selection-dark-2026-10-18.md)、[终端复制快捷键测试](../tools/test-terminal-copy-shortcut.mjs)、[Markdown scoping](markdown-pair-scoping-2026-10-18.md)、[Markdown 同态配对](markdown-homologous-pair-2026-10-18.md)、[Markdown 排版规格](markdown-typography-spec-2026-10-18.md)、[图片 scoping](image-pair-scoping-2026-10-18.md)、[JSON 同态配对](json-homologous-pair-2026-10-18.md)、[快速打开 scoping](quick-open-pair-scoping-2026-10-18.md)、[Git Log 同态配对](git-log-pair-scoping-2026-10-18.md)、[全仓搜索 scoping](full-search-pair-scoping-2026-10-18.md)、[主菜单入口取证](main-menu-scoping-2026-10-18.md)、[Stash 对话框取证](stash-dialog-scoping-2026-10-18.md)、[Commit 同态配对](commit-homologous-pair-2026-10-18.md)、[Diff 同态配对](diff-homologous-pair-2026-10-18.md)、[File History 同态配对](file-history-homologous-pair-2026-10-18.md)、[Blame 同态配对](blame-homologous-pair-2026-10-18.md)、[分支弹层同态配对](branches-homologous-pair-2026-10-18.md)、[Checkout 对话框同态配对](checkout-dialog-homologous-pair-2026-10-18.md)、[New Branch 对话框同态配对](new-branch-dialog-homologous-pair-2026-10-18.md)、[Stash 同态配对](stash-homologous-pair-2026-10-18.md)、[Reset 对话框同态配对](reset-dialog-homologous-pair-2026-10-18.md)、[Stash Manager 取证](stashes-manager-scoping-2026-10-18.md)、[Push 对话框同态配对](push-dialog-homologous-pair-2026-10-18.md)、[Git Log 提交右键菜单取证](git-log-commit-menu-scoping-2026-10-18.md)、[Git Log 提交右键菜单同态配对](git-log-context-menu-homologous-pair-2026-10-18.md)、[New Tag 对话框同态配对](new-tag-dialog-homologous-pair-2026-10-18.md)、[Clone Repository 同态配对](clone-dialog-homologous-pair-2026-10-18.md)、[New Worktree 对话框同态配对](new-worktree-dialog-homologous-pair-2026-10-18.md)、[Compare with Workspace 同态配对](compare-workspace-homologous-pair-2026-10-18.md)、[Worktrees 管理页同态配对](worktrees-manager-homologous-pair-2026-10-18.md)、[损坏图片预览取证](image-error-scoping-2026-10-18.md)、[损坏图片错误页同态配对](image-error-homologous-pair-2026-10-18.md)、[有效图片预览同态配对](image-valid-homologous-pair-2026-10-18.md)、[Rollback 对话框同态配对](rollback-changes-homologous-pair-2026-10-18.md)、[Changes 右键菜单同态配对](changes-context-menu-homologous-pair-2026-10-18.md)、[Git Remotes 同态配对](git-remotes-homologous-pair-2026-10-18.md)、[Define Remote 同态配对](define-remote-homologous-pair-2026-10-18.md)、[Push 有 upstream 同态配对](push-upstream-homologous-pair-2026-10-18.md)、[Push 待推提交同态配对](push-commit-homologous-pair-2026-10-18.md)、[Push 被拒同态配对](push-rejected-homologous-pair-2026-10-18.md)、[冲突列表同态配对](conflict-list-homologous-pair-2026-10-18.md)、[项目文件右键菜单同态配对](project-file-context-homologous-pair-2026-10-18.md)、[项目根节点右键菜单同态配对](project-root-context-homologous-pair-2026-10-18.md)、[Settings/Appearance 同态配对](settings-appearance-homologous-pair-2026-10-18.md)、[Settings/Git 同态配对](settings-git-homologous-pair-2026-10-18.md)、[Settings/Terminal 同态配对](settings-terminal-homologous-pair-2026-10-18.md)、[Find in Files 空态同态配对](search-empty-homologous-pair-2026-10-18.md)、[Find in Files 结果同态配对](search-results-homologous-pair-2026-10-18.md)、[Find in Files 零结果同态配对](search-no-results-homologous-pair-2026-10-18.md)、[Quick Open 空态同态配对](quick-open-empty-homologous-pair-2026-10-18.md)、[Quick Open 结果同态配对](quick-open-results-homologous-pair-2026-10-18.md)、[Git 不可用同态配对](git-unavailable-homologous-pair-2026-10-18.md)、[初始化仓库同态配对](init-repo-homologous-pair-2026-10-18.md)、[Settings/File View 同态配对](settings-fileview-homologous-pair-2026-10-18.md)、[关闭运行中终端同态配对](close-terminal-homologous-pair-2026-10-18.md)、[搜索截断同态配对](search-truncation-homologous-pair-2026-10-18.md)、[Git 操作结果配对与修复](git-result-homologous-pair-2026-10-18.md)、[大文件只读预览同态配对](large-file-limit-homologous-pair-2026-10-18.md)、[设置修改保存取证](settings-save-scoping-2026-10-18.md)、[Appearance UI 字号保存态](settings-ui-font-save-2026-10-18.md)、[大文件 Warning 横幅缺陷](large-warning-defect-2026-10-18.md)、[大文件 Warning 横幅修复](large-warning-fix-2026-10-18.md)、[大文件 Warning 动作不响应](large-warning-actions-defect-2026-10-18.md)、[大文件 Warning 动作复核](large-warning-actions-verify-2026-10-18.md)、[Push 成功结果状态配对](git-result-state-pair-2026-10-18.md)、[验收检查点](acceptance-checkpoint-2026-10-18.md)。

## 6. 阴影

- 系统阴影有效四边定量：实际有阴影，左右接近参考，顶部/底部偏弱。
- 旧“无阴影”结论撤回；无收益的 DWM/自绘实验不重复。
- 深色、非活动态、完整四角未闭环。
- “阴影差异已接受”缺少明确用户批准依据，只能登记为已知差异，不能据此验收或扩大豁免。

证据：`shadow-reference-settings-2026-10-17.md`、`shadow-difference-registration-2026-10-18.md`。

## 7. 需要用户裁决或输入

1. **终端标签**：已裁决 `Local` + 悬停当前 Shell，并已实现/验证；该冲突关闭。
2. **54 页双端参考**：用户已选择提供同态参考（无右栏、同项目面板宽度、同工具栏/标签/选中项）；首个 `main-project`/`text-viewer` 参考已由本会话按隔离 2026.2.1 实例采集，其余页面继续按模块采集，不再等待截图。
3. **阴影**：用户已选择登记为已知差异，不宣称验收。
4. **跨屏 DPI**：提供第二显示器/可切缩放环境，或接受本轮不验证。
5. **2904ms 慢样本**：提供原始复现操作/日志，或接受超时分支仍未验证。

## 8. 清理

- `cleanup-check-31.json`：68 个本批 PID 全部不存在（PID25584 的复用 `svchost` 也已退出）。无 `Augit-no-cdp-*`/`Augit-ui-english-*`/`Augit-branch-validation-*`/`Augit-conflict-readonly-*`/`Augit-conflict-skip-*`/`Augit-conflict-continue-*`/`Augit-conflict-abort-*` 等本批临时目录，无活动 `Augit`；历史 `Augit-terminal-reference-*` 未删除。
- 未触碰用户 PyCharm PID 22768；未删除 `.tmp/` 或其他历史目录。
- `cleanup-check-32.json`（首个同态配对批）：Augit PID 24612 正常关闭且默认设置恢复为 `B8868855…DC3D`；参考 PyCharm PID 6852 通过 Confirm Exit 关闭并清理死 `.pid`；误用 2026.2.3 实例 PID 13688 也已关闭；无残留 `Augit`/`pycharm64`/Augit 专属 WebView2，批次 PID 无监听端口。用户 PyCharm PID 20336 在操作前已自行退出，本会话未关闭或重启。未提交、未推送。
- `cleanup-check-33.json`（标签修复批）：Augit PID 14020 正常关闭，默认设置恢复为 `B8868855…DC3D`；三个离线 Playwright 测试浏览器均关闭，无 Chromium/WebView2/端口残留；本批未启动 PyCharm。未提交、未推送。
- `cleanup-check-34.json`（终端配对批）：Augit PID 1804 正常关闭，设置恢复 `B8868855…DC3D`；参考 PyCharm PID 19256 经 Confirm Exit 关闭；无残留进程/端口。未提交、未推送。
- `cleanup-check-35.json`（终端 Local 标签修复批）：Augit PID 24260 正常关闭，设置恢复 `B8868855…DC3D`；测试浏览器关闭；无残留进程/端口。未提交、未推送。`verify-ux-frame-buttons.cjs` 仍有既有 dark alpha 与 ui-size 40 重叠失败，不记通过。
- `cleanup-check-36.json`（深色 accent 订正批）：本批未启动 Augit/PyCharm，只运行 headless Chromium，浏览器已关闭；无端口/应用残留。未提交、未推送。三个深色测试套件仍因既有旧期望失败，不记通过。
- `cleanup-check-37.json`（设置 scoping 批）：Augit PID 7820 正常关闭，设置恢复 `B8868855…DC3D`；参考 PyCharm PID 12652 经 Confirm Exit 关闭；未触碰用户原有 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-38.json`（设置左栏宽度批）：Augit PID 17416 正常关闭，设置恢复 `B8868855…DC3D`；本批未启动参考 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-39.json`（设置 Appearance 控件批）：Augit PID 23380 正常关闭，设置恢复 `B8868855…DC3D`；本批未启动参考 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-40.json`（设置 Appearance 垂直内距批）：Augit PID 9744 正常关闭，设置恢复 `B8868855…DC3D`；本批未启动参考 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-41.json`（设置开/关窗口级冒烟）：Augit PID 17496 正常关闭，设置恢复 `B8868855…DC3D`；未启动参考 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-42.json`（设置搜索焦点态）：Augit PID 7392 正常关闭，设置恢复 `B8868855…DC3D`；未启动参考 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-43.json`（终端深色状态配对）：实际 PID 9612、参考 PID 4964 正常关闭；实际设置恢复 `B8868855…DC3D`；参考 `laf.xml`/`colors.scheme.xml` 恢复 Islands Light/Light；无端口/应用残留。离线图标套件 PASS（含编辑器底色断言）。未提交、未推送。
- `cleanup-check-44.json`（深色设置 footer 干净复测）：Augit PID 24732 正常关闭，设置恢复 `B8868855…DC3D`；未启动参考 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-45.json`（深色设置小高度受控复测）：Augit PID 10384 正常关闭，设置恢复 `B8868855…DC3D`；未启动参考 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-46.json`（当前构建正常入口身份）：Augit PID 13864 正常关闭，设置恢复 `B8868855…DC3D`；未启动参考 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-47.json`（终端面板边色）：Augit PID 15736 正常关闭，设置恢复 `B8868855…DC3D`；未启动参考 PyCharm；无端口/应用残留。离线图标套件 PASS（含间隙色断言）。未提交、未推送。
- `cleanup-check-48.json`（查找条 scoping）：Augit PID 2740、参考 PID 14136 正常关闭；实际设置恢复 `B8868855…DC3D`；未触碰用户原有 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-49.json`（文本选择）：Augit PID 9056、参考 PID 17452 正常关闭；设置关闭后曾变为 `93A22D3B…`，已恢复 `B8868855…DC3D`；离线图标套件 PASS（含选择色断言）。未提交、未推送。
- `cleanup-check-50.json`（文本复制）：Augit PID 7796 正常关闭，设置恢复 `B8868855…DC3D`；未启动参考 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-51.json`（终端聚焦）：Augit PID 18740、参考 PID 10040 正常关闭；设置恢复 `B8868855…DC3D`；未触碰用户原有 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-52.json`（终端选区/复制缺口）：Augit PID 5560、21828、参考 PID 17172 正常关闭；设置恢复 `B8868855…DC3D`；未触碰用户原有 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-53.json`（终端选区根因收窄）：Augit PID 19212 正常关闭；设置恢复 `B8868855…DC3D`；复制处理器试验已回退，无净代码改动；未提交、未推送。
- `cleanup-check-54.json`（终端选区/复制修复）：调试 PID 16336、验证 PID 12644 正常关闭；CDP 9333 关闭；设置恢复 `B8868855…DC3D`；Ctrl+Shift+C/Ctrl+Insert 复制 `0123456789`；未提交、未推送。
- `cleanup-check-55.json`（终端深色多行选区）：Augit PID 8640 正常关闭；设置恢复 `B8868855…DC3D`；深色选区 `#214283`、复制两行；未提交、未推送。
- `cleanup-check-56.json`（终端复制快捷键测试）：Augit PID 23864 正常关闭；单元测试 PASS 10/10；正常入口 E2E PASS；设置恢复 `B8868855…DC3D`；未提交、未推送。
- `cleanup-check-57.json`（Markdown 分栏 scoping）：Augit PID 5996、参考 PID 10112 正常关闭；设置恢复 `B8868855…DC3D`；未触碰用户原有 PyCharm；无端口/应用残留。未提交、未推送。
- `cleanup-check-58.json`（Markdown 同态配对）：Augit PID 6016 正常关闭；参考 PID 10112 已在前次关闭；设置恢复 `B8868855…DC3D`；分栏 1px、页面级配对计 3；未提交、未推送。
- `cleanup-check-59.json`（图片 scoping）：Augit PID 25484、参考 PID 11192 正常关闭；临时 fixture 图片已删除、notes.txt 哈希未变；设置恢复 `B8868855…DC3D`；未提交、未推送。
- `cleanup-check-60.json`（JSON 同态配对）：Augit PID 11452、参考 PID 2252 正常关闭；临时 sample.json 已删除、fixture 恢复 3 文件；设置恢复 `B8868855…DC3D`；页面级配对计 4；未提交、未推送。
- `cleanup-check-61.json`（快速打开 scoping）：Augit PID 1032、参考 PID 23652 正常关闭；设置恢复 `B8868855…DC3D`；未触碰用户原有 PyCharm；未提交、未推送。
- `cleanup-check-62.json`（Git Log 配对）：Augit PID 10324、参考 PID 12632 正常关闭；设置恢复 `B8868855…DC3D`；Log 标签已激活（前次误判）；未提交、未推送。
- `cleanup-check-63.json`（Git Log 标签更正）：调试 PID 7664 关闭；CDP 9333 关闭；设置恢复 `B8868855…DC3D`；页面级配对计 5；未提交、未推送。
- `cleanup-check-64.json`（全仓搜索 scoping）：Augit PID 23904、参考 PID 17516 正常关闭；设置恢复 `B8868855…DC3D`；notes.txt 磁盘未变；未提交、未推送。
- `cleanup-check-65.json`（验收检查点/Commit 可达性尝试）：Augit PID 25272 正常关闭；设置恢复 `B8868855…DC3D`；当时 Commit 页面未到达；无净代码改动；未提交、未推送。
- `cleanup-check-66.json`（Commit 同态配对）：Augit PID 18592、参考 PID 6392 正常关闭；设置恢复 `B8868855…DC3D`；页面级配对计 6；未提交、未推送。
- `cleanup-check-67.json`（设置当前无 CDP 样本）：PID 19096 由测试脚本 finally 结束（主窗非正常关闭）；Settings Esc 正常关闭 44.0ms；设置文件 `B8868855…DC3D`；无端口/应用残留；未提交、未推送。
- `cleanup-check-68.json`（设置录像局部帧扫描）：无新启动；内容 9.30s 出现、初始无整白；10.20–10.23s 3 帧内容空白候选（chrome `#E9EAEE`），全帧重取不一致，待第二录像；未提交、未推送。
- `cleanup-check-69.json`（设置空白候选交叉核对）：复用既有 repeat 录像两次开窗 60fps 前 2.2s，空白帧 0；候选未复现，判采集/一次性伪影，不登记产品缺陷；仍不宣称无闪；未提交、未推送。
- `cleanup-check-70.json`（设置 ddagrab 交叉核对）：PID 6068；原生 presented 808.0ms、close 66.6ms；ddagrab 151 帧内容 10.08s、空白 0；gdi 3 帧空白判采集伪影；设置文件 `B8868855…DC3D`；无端口/应用残留；未提交、未推送。
- `cleanup-check-71.json`（工作区 Diff 同态配对）：Augit PID 13396、参考 PID 18664 正常关闭；设置恢复 `B8868855…DC3D`；页面级配对计 7；未提交、未推送。
- `cleanup-check-72.json`（Diff Unified 状态）：Augit PID 6648、参考 PID 18992 正常关闭；设置恢复 `B8868855…DC3D`；实际上下文行号缺失，登记已知差异/待修复；未提交、未推送。
- `cleanup-check-73.json`（Unified 行号修复）：Augit PID 10520 正常关闭；设置恢复 `B8868855…DC3D`；`mockup.js` 新哈希 `AF60163C…CA15`；测试 4/4 PASS；未提交、未推送。
- `cleanup-check-74.json`（Unified 修复后回归）：无新应用进程；`verify-ux-offline-icons.cjs` PASS 110 场景/主题，证据 `offline-icons-after-unified-fix/`；未提交、未推送。
- `cleanup-check-75.json`（File History 同态配对）：Augit PID 11932、参考 PID 13240 正常关闭；设置恢复 `B8868855…DC3D`；页面级配对计 8；未提交、未推送。
- `cleanup-check-76.json`（Blame 同态配对）：Augit PID 2312、参考 PID 17560 正常关闭；设置恢复 `B8868855…DC3D`；页面级配对计 9；未提交、未推送。
- `cleanup-check-77.json`（Blame 未提交行修复）：Augit PID 3084 正常关闭；设置恢复 `B8868855…DC3D`；`mockup.js` 新哈希 `4E7E3202…BCE7`；测试 5/5、offline 110 PASS；未提交、未推送。
- `cleanup-check-78.json`（分支弹层同态配对）：Augit PID 8292、参考 PID 3460 正常关闭；设置恢复 `B8868855…DC3D`；页面级配对计 10；未提交、未推送。
- `cleanup-check-79.json`（Checkout 对话框同态配对）：Augit PID 11412、参考 PID 23896 正常关闭；设置恢复 `B8868855…DC3D`；未创建分支/未执行 Checkout；页面级配对计 11；未提交、未推送。
- `cleanup-check-80.json`（New Branch 对话框同态配对）：Augit PID 13316、参考 PID 15976 正常关闭；设置恢复 `B8868855…DC3D`；未创建分支；页面级配对计 12；未提交、未推送。
- `cleanup-check-81.json`（主菜单入口取证）：Augit PID 13896 正常关闭；参考 PID 10248 按本轮 PID 结束；未到达 Stash、无 Git 写；页面级仍 12；未提交、未推送。
- `cleanup-check-82.json`（Git 菜单/Create Stash 尝试）：Augit PID 18788 正常关闭；未打开 Stash 对话框、未写 Git；入口接线未定位；页面级仍 12；未提交、未推送。
- `cleanup-check-83.json`（Stash 对话框取证）：CDP PID 8560、参考 PID 2632 结束；Stash 图仅调试；Include untracked 截断候选；未写 Git；页面级仍 12；未提交、未推送。
- `cleanup-check-84.json`（Stash 两复选同行修复）：Augit PID 5464 正常关闭；设置恢复 `B8868855…DC3D`；`mockup.js` `DDF905A9…9377`、`mockup.css` `EBEAE830…4D6B`；测试 5/5、offline 110 PASS；页面级配对计 13；未提交、未推送。
- `cleanup-check-85.json`（Reset/Git 菜单入口尝试）：CDP PID 25468 结束；菜单项查询为 0、未打开 Reset、未启动参考、无 Git 写；页面级仍 13；未提交、未推送。
- `cleanup-check-86.json`（Reset 对话框同态配对）：Augit PID 24956、参考 PID 7600 结束；设置恢复 `B8868855…DC3D`；未执行 Reset；页面级配对计 14；未提交、未推送。
- `cleanup-check-87.json`（Stash Manager 取证）：Augit PID 11820、参考 PID 16528 结束；设置恢复 `B8868855…DC3D`；参考未取得管理界面；无 Stash 写；页面级仍 14；未提交、未推送。
- `cleanup-check-88.json`（Push 对话框同态配对）：Augit PID 24284、参考 PID 4528 结束；设置恢复 `B8868855…DC3D`；未执行 Push；页面级配对计 15；未提交、未推送。
- `cleanup-check-89.json`（Git Log 提交右键菜单取证）：Augit PID 22832 正常关闭；参考菜单复用；实际未打开菜单、未写 Git；页面级仍 15；未提交、未推送。
- `cleanup-check-90.json`（Git Log 菜单 Shift+F10 尝试）：Augit PID 11076 正常关闭；无菜单、未写 Git；页面级仍 15；未提交、未推送。
- `cleanup-check-91.json`（Git Log 提交右键菜单同态配对）：Augit PID 4428 正常关闭；设置恢复 `B8868855…DC3D`；未执行动作；页面级配对计 16；未提交、未推送。
- `cleanup-check-92.json`（New Tag 对话框同态配对）：Augit PID 23196、参考 PID 4488 结束；设置恢复 `B8868855…DC3D`；未创建标签；页面级配对计 17；未提交、未推送。
- `cleanup-check-93.json`（Compare with Workspace 尝试）：Augit PID 4672 正常关闭；菜单未关闭、比较视图未打开、未写 Git；页面级仍 17；未提交、未推送。
- `cleanup-check-94.json`（Clone Repository 同态配对）：Augit PID 9064、参考 PID 12112 结束；设置恢复 `B8868855…DC3D`；未 Clone；页面级配对计 18；未提交、未推送。
- `cleanup-check-95.json`（New Worktree 对话框同态配对）：Augit PID 16224、参考 PID 25280 结束；设置恢复 `B8868855…DC3D`；未创建 Worktree；页面级配对计 19；未提交、未推送。
- `cleanup-check-96.json`（New Worktree 布局修复）：Augit PID 14008 正常关闭；设置恢复 `B8868855…DC3D`；测试 6/6、offline 110 PASS；未创建 Worktree；页面级仍 19；未提交、未推送。
- `cleanup-check-97.json`（紧凑输入空名称主按钮修复）：Augit PID 3308 正常关闭；设置恢复 `B8868855…DC3D`；测试 7/7；未创建标签；页面级仍 19；未提交、未推送。
- `cleanup-check-98.json`（Compare with Workspace 同态配对）：Augit PID 15028、参考 PID 25352 结束；设置恢复 `B8868855…DC3D`；未写 Git；页面级配对计 20；未提交、未推送。
- `cleanup-check-99.json`（Worktrees 管理页同态配对）：Augit PID 25464、参考 PID 7596 结束；设置恢复 `B8868855…DC3D`；未写 Git；页面级配对计 21；未提交、未推送。
- `cleanup-check-100.json`（损坏图片预览取证）：Augit PID 13684、参考 PID 10096 结束；设置恢复 `B8868855…DC3D`；临时 `broken.png` 已删除；无配对；页面级仍 21；未提交、未推送。
- `cleanup-check-101.json`（损坏图片错误页同态配对）：Augit PID 12588、参考 PID 24048 结束；设置恢复 `B8868855…DC3D`；临时 `corrupt.png` 已删除；页面级配对计 22；未提交、未推送。
- `cleanup-check-102.json`（有效图片预览同态配对）：Augit PID 14920、参考 PID 23124 结束；设置恢复 `B8868855…DC3D`；临时 `sample-image.png` 已删除；页面级配对计 23；未提交、未推送。
- `cleanup-check-103.json`（Rollback 对话框同态配对）：Augit PID 22784、参考 PID 10836 结束；设置恢复 `B8868855…DC3D`；未回滚、fixture 未变；页面级配对计 24；未提交、未推送。
- `cleanup-check-104.json`（Changes 右键菜单同态配对）：复用 Round 79 两端菜单截图，无新进程；未写 Git；页面级配对计 25；未提交、未推送。
- `cleanup-check-105.json`（Commit 空态取证）：实际 PID 2100、参考 PID 22852 关闭；临时 clone 删除；设置恢复 `B8868855…DC3D`；参考受输入桌面不可用阻塞；无配对；页面级仍 25；未提交、未推送。
- `cleanup-check-106.json`（Commit 空态第二次尝试）：参考 PID 22668 关闭；临时 clone 删除；设置恢复 `B8868855…DC3D`；Trust 后仍无法前台；无配对；页面级仍 25；未提交、未推送。
- `cleanup-check-107.json`（Git Remotes 同态配对）：Augit PID 21596、参考 PID 23292 结束；设置恢复 `B8868855…DC3D`；未创建远端；页面级配对计 26；未提交、未推送。
- `cleanup-check-108.json`（Commit 空态同态配对）：参考 PID 22704 关闭；临时 clone 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；设置恢复 `B8868855…DC3D`；页面级配对计 27；未提交、未推送。
- `cleanup-check-109.json`（Define Remote 同态配对）：Augit PID 16620、参考 PID 5976 结束；设置恢复 `B8868855…DC3D`；未创建远端；页面级配对计 28；未提交、未推送。
- `cleanup-check-110.json`（Fetch 操作结果尝试）：Augit PID 23512 正常关闭；未见结果页；参考未启动；页面级仍 28；未提交、未推送。
- `cleanup-check-111.json`（Push 有 upstream 同态配对）：Augit PID 18076、参考 PID 22228 结束；临时 clone/bare 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；设置恢复；未推；页面级配对计 29；未提交、未推送。
- `cleanup-check-112.json`（Push 待推提交同态配对）：Augit PID 12372、参考 PID 20152 结束；临时 clone/bare 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；设置恢复；未推；页面级配对计 30；未提交、未推送。
- `cleanup-check-113.json`（Push 成功取证尝试）：Augit PID 8908 关闭；临时 clone/bare 删除；设置恢复；Push 按钮未命中；无配对；页面级仍 30；未提交、未推送。
- `cleanup-check-114.json`（Push 成功结果取证）：Augit PID 19496 关闭；临时 clone/bare 删除；设置恢复；实际推送成功但无结果页；无配对；页面级仍 30；未提交、未推送。
- `cleanup-check-115.json`（Push 失败取证尝试）：Augit PID 23140 关闭；临时 clone/bare 删除；设置恢复；失败无错误页捕获；无配对；页面级仍 30；未提交、未推送。
- `cleanup-check-116.json`（Push 失败根因复现）：CDP PID 20760 关闭；临时 clone/bare 删除；确认为快进推送成功、非失败；页面级仍 30；未提交、未推送。
- `cleanup-check-117.json`（Push 被拒同态配对）：Augit PID 17336、参考 PID 24224 结束；临时 clone/bare 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；设置恢复；页面级配对计 31；未提交、未推送。
- `cleanup-check-118.json`（冲突列表同态配对）：Augit PID 22028、参考 PID 10716 结束；临时 clone 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；设置恢复；页面级配对计 32；未提交、未推送。
- `cleanup-check-119.json`（项目文件右键菜单同态配对）：Augit PID 2592、参考 PID 22288 结束；设置恢复；未执行菜单动作；页面级配对计 33；未提交、未推送。
- `cleanup-check-120.json`（项目根节点右键修复+配对）：Augit PID 12592、参考 PID 14704 结束；设置恢复；测试 6/6；页面级配对计 34；未提交、未推送。
- `cleanup-check-121.json`（Settings/Appearance 同态配对）：Augit PID 21584、参考 PID 5468 结束；设置恢复；未保存；页面级配对计 35；未提交、未推送。
- `cleanup-check-122.json`（Settings/Git 同态配对）：Augit PID 23424、参考 PID 11612 结束；设置恢复；未保存；页面级配对计 36；未提交、未推送。
- `cleanup-check-123.json`（Settings/Terminal 同态配对）：Augit PID 17164、参考 PID 24536 结束；设置恢复；未保存；页面级配对计 37；未提交、未推送。
- `cleanup-check-124.json`（Terminal 初始脏修复）：Augit PID 18236 结束；`mockup.js` `F3D0CD53…`；测试 7/7；正常入口 Apply 禁用/无蓝点；设置恢复；页面级仍 37；未提交、未推送。
- `cleanup-check-125.json`（Find in Files 空态尝试）：Augit PID 20360、参考 PID 19804 结束；参考未显示搜索；无配对；页面级仍 37；未提交、未推送。
- `cleanup-check-126.json`（Find in Files 空态同态配对）：参考 PID 23800 结束；设置恢复；页面级配对计 38；未提交、未推送。
- `cleanup-check-127.json`（Find in Files 结果同态配对）：Augit PID 9908 结束；设置恢复；未改文件；页面级配对计 39；未提交、未推送。
- `cleanup-check-128.json`（Find in Files 零结果尝试）：Augit PID 24336、参考 PID 23768 经 taskkill 结束；参考未取得；无配对；页面级仍 39；未提交、未推送。
- `cleanup-check-129.json`（Find in Files 零结果同态配对）：参考 PID 17156 结束；设置恢复；页面级配对计 40；未提交、未推送。
- `cleanup-check-130.json`（Quick Open 空态尝试）：Augit PID 23268 结束；Ctrl+P 未打开；无配对；页面级仍 40；未提交、未推送。
- `cleanup-check-131.json`（Quick Open 空态同态配对）：Augit PID 24036、参考 PID 23172 结束；设置恢复；未打开文件；页面级配对计 41；未提交、未推送。
- `cleanup-check-132.json`（Quick Open 结果同态配对）：Augit PID 23744、参考 PID 2424 结束；设置恢复；未打开文件；页面级配对计 42；未提交、未推送。
- `cleanup-check-133.json`（Git 不可用同态配对）：Augit PID 2320、参考 PID 3000 结束；实际设置恢复；参考配置未改；页面级配对计 43；未提交、未推送。
- `cleanup-check-134.json`（初始化仓库同态配对）：Augit PID 22996、参考 PID 17060 结束；临时目录删除；`trusted-paths.xml` 恢复；页面级配对计 44；未提交、未推送。
- `cleanup-check-135.json`（不可预览文件尝试）：Augit PID 21956、参考 PID 11176 结束；临时 sample.bin 删除；参考未进入二进制页；无配对；页面级仍 44；未提交、未推送。
- `cleanup-check-136.json`（`.zip` 不可预览重试）：Augit PID 23148、参考 PID 24084 结束；临时 sample.zip 删除；参考仅选中；无配对；页面级仍 44；未提交、未推送。
- `cleanup-check-137.json`（Stash 管理尝试）：Augit PID 9012 结束；临时 clone 删除；未到达 Stash 管理页；无配对；页面级仍 44；未提交、未推送。
- `cleanup-check-138.json`（Stash Manager 实际页）：Augit PID 13592、参考 PID 11132 结束；实际 Stash Manager 已采、参考无 stash 页；无配对；页面级仍 44；未提交、未推送。
- `cleanup-check-139.json`（参考 Stash 动作）：参考 PID 24356 打开 Stash Changes（创建），非 Manager；无配对；页面级仍 44；未提交、未推送。
- `cleanup-check-140.json`（Settings/File View 同态配对）：Augit PID 1820、参考 PID 4268 结束；设置恢复；未保存；页面级配对计 45；未提交、未推送。
- `cleanup-check-141.json`（Open Workspace 尝试）：Augit PID 16176 结束；无参启动仍打开 Augit 仓库；设置恢复；无配对；页面级仍 45；未提交、未推送。
- `cleanup-check-142.json`（关闭运行中终端尝试）：Augit PID 20172 结束；无 ping 残留；轨道坐标已纠正；确认未捕获；无配对；页面级仍 45；未提交、未推送。
- `cleanup-check-143.json`（关闭运行中终端同态配对）：Augit PID 11736、参考 PID 4448 结束；无 ping 残留；设置恢复；页面级配对计 46；未提交、未推送。
- `cleanup-check-144.json`（大文件限制尝试）：Augit PID 22768 结束；临时 large.txt 删除；未捕获大文件页；无配对；页面级仍 46；未提交、未推送。
- `cleanup-check-145.json`（大文件 Quick Open 重试）：Augit PID 22420、参考 PID 12328 结束；临时 large.txt 删除；实际无限制打开、参考未见警告页；无配对；页面级仍 46；未提交、未推送。
- `cleanup-check-146.json`（搜索截断尝试）：Augit PID 5344、参考 PID 22548 结束；临时 large.txt 删除；fixture app.py 恢复 `B1986914…B954`；实际 Too Many Results、参考未捕获；无配对；页面级仍 46；未提交、未推送。
- `cleanup-check-147.json`（Diff 局部加载尝试）：Augit PID 20176 结束；临时 large.txt 删除；无暂存；实际完整加载、无 partial；参考未启动；无配对；页面级仍 46；未提交、未推送。
- `cleanup-check-148.json`（搜索截断同态配对）：Augit PID 5344、参考 PID 15200 结束；临时 large.txt 删除；fixture app.py 基线；页面级配对计 47；未提交、未推送。
- `cleanup-check-149.json`（Fetch 结果尝试）：Augit PID 9720 结束；误开 Push、Fetch 无 toast；无配对；页面级仍 47；未提交、未推送。
- `cleanup-check-150.json`（Git 操作进行中尝试）：Augit PID 23984、参考 PID 3268 结束；临时 clone/bare 删除；信任恢复；实际 Pushing...、参考 Up to date；无配对；页面级仍 47；未提交、未推送。
- `cleanup-check-151.json`（Git 操作结果尝试）：Augit PID 996 结束；临时 clone/bare 删除；实际无显式结果、参考 balloon；无配对；页面级仍 47；未提交、未推送。
- `cleanup-check-152.json`（推送成功 toast 修复）：实际 PID 16616 结束；临时 clone/bare 删除；测试 7/7；状态级配对；页面级仍 47；未提交、未推送。
- `cleanup-check-153.json`（大文件只读预览同态配对）：Augit PID 3516、参考 PID 6156 结束；临时 huge.txt 删除；设置恢复；页面级配对计 48；未提交、未推送。
- `cleanup-check-154.json`（Diff 上限尝试）：Augit PID 6984、参考 PID 5820 结束；临时 clone 删除；信任恢复；实际 SideTooLarge、参考未采；无配对；页面级仍 48；未提交、未推送。
- `cleanup-check-155.json`（Diff 上限参考重试）：参考 PID 4120 结束；临时 clone 删除；信任恢复；Commit 树双击仍折叠节点；无配对；页面级仍 48；未提交、未推送。
- `cleanup-check-156.json`（设置修改保存尝试）：实际 PID 5460 结束；File View 字号只读；设置恢复；无配对；页面级仍 48；未提交、未推送。
- `cleanup-check-157.json`（File View 字号输入误改）：实际 PID 24324 结束；输入框违反用户裁决；设置恢复；页面级仍 48；未提交、未推送。
- `cleanup-check-158.json`（File View 回退复验）：实际 PID 4852 结束；`mockup.js` 回退 `F3D0CD53…671D`；测试 6/6；设置恢复；页面级仍 48；未提交、未推送。
- `cleanup-check-159.json`（Appearance UI 字号保存）：实际 PID 24232 结束；`textFontSize` 13 已保存并恢复 12；参考未采；页面级仍 48；未提交、未推送。
- `cleanup-check-160.json`（大文件 Warning 横幅缺陷）：实际 PID 17412 结束；`hideLargeFileWarning=false` 仍无横幅；缺陷登记；无配对；页面级仍 48；未提交、未推送。
- `cleanup-check-161.json`（大文件 Warning 横幅修复）：实际 PID 14720 结束；CDP 确认 13.14px 根因；CSS 网格修复；测试 8/8；横幅可见；设置恢复；页面级仍 48；未提交、未推送。
- `cleanup-check-162.json`（大文件 Warning 动作缺陷）：实际 PID 16180 结束；Hide/Don't Show 均无响应；设置恢复；缺陷登记；无配对；页面级仍 48；未提交、未推送。
- `cleanup-check-163.json`（大文件 Warning 动作复核）：实际 PID 11360/8628 结束；CDP click 证明 Hide/Don't Show 均生效；物理合成点击未命中为工具限制；缺陷撤回；设置恢复；页面级仍 48；未提交、未推送。
- `cleanup-check-164.json`（Push 成功结果状态配对）：参考 PID 22152 结束；临时 clone/bare 删除；信任恢复；状态级配对；页面级仍 48；未提交、未推送。
- `cleanup-check-165.json`（大文件 Warning 真实输入闭环）：实际 PID 5288/14836/14456/18724、参考 PID 23072 均关闭；临时夹具/设置删除；参考 `huge.txt` 删除、`config/options` 恢复并逐文件哈希一致；全局设置仍 `B8868855…DC3D`；Hide 重开/重启与 Don't Show 重启均通过；参考同文件无 Warning；页面级仍 48；未提交、未推送。
- `cleanup-check-166.json`（Diff 上限参考态）：实际 PID 17436、参考 PID 20360/12384 均关闭；临时夹具删除；参考 `trusted-paths.xml` 恢复 `6E40F668…D2C9`；全局设置仍 `B8868855…DC3D`；同一 25.38MB 文件两端均拒绝 diff 正文，登记为状态级配对；页面级仍 48；未提交、未推送。
- `cleanup-check-167.json`（Smart Checkout 配对与修复）：实际 PID 23024/14576/20156/5980、参考 PID 3152 均关闭；临时夹具删除；参考 `trusted-paths.xml`/`recentProjects.xml`/`window.state.xml` 恢复并哈希一致；全局设置仍 `B8868855…DC3D`；实际取消 no-op、实际 Smart Checkout 成功且本地改动保留、stash 为空；说明截断修复测试 5/5、UI 资产一致；最终 CSS 身份由 PID 5980 复验；页面级 48→49；未提交、未推送。
- `cleanup-check-168.json`（Git 进行中真实慢远端）：实际 PID 18352、参考 PID 23276 关闭；本机挂起服务器 PID 10656 停止，18765 无监听；本批 git/remote-http 进程清理；临时夹具删除；参考配置恢复；全局设置仍 `B8868855…DC3D`；实际 Cancel 显示 `Operation cancelled.`，参考取消未验证；状态级配对，不增页面级 49；未修改代码、未提交、未推送。
- `cleanup-check-169.json`（Push 网络失败）：实际 PID 7444、参考 PID 22960 关闭；临时夹具删除；参考配置恢复；全局设置仍 `B8868855…DC3D`；同一 HTTP 502 失败，实际内联 `fatal`、参考 `Push failed` balloon；状态级配对，不增页面级 49；未修改代码、未提交、未推送。
- `cleanup-check-170.json`（Open Workspace 参考）：实际 PID 20100、参考 PID 14564 关闭；临时夹具删除；参考 `trusted-paths.xml`/`recentProjects.xml`/`window.state.xml` 从启动前快照恢复并哈希一致（Welcome 启动曾新增临时 `WelcomeScreen` 项目，已移除）；全局设置仍 `B8868855…DC3D`；实际 Open Workspace 对话框、参考 WelcomeScreen，登记语义状态，不增页面级 49；未修改代码、未提交、未推送。
- `cleanup-check-171.json`（Stash Manager 配对）：实际 PID 14568、参考 PID 15680 关闭；临时夹具删除；参考配置恢复；stash 保持 `stash@{0}`，未 Apply/Pop/Delete；全局设置仍 `B8868855…DC3D`；参考入口更正为 Find Action `Unstash Changes...`；页面级 49→50、像素仍 0；未修改代码、未提交、未推送。
- `cleanup-check-172.json`（终端 ui-size 40 复验）：实际 PID 14072 关闭；临时夹具删除；More/Hide 偏移尝试已回退，`mockup.css` 恢复 `A654FB57…444C`；终端头部整体布局仍异常，未留下代码改动；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-173.json`（参考 Push 取消复核）：参考 PID 17512 关闭；服务器 PID 16432 停止，`18767` 无监听；本批 git 进程清理；临时夹具删除；参考配置恢复；全局设置仍 `B8868855…DC3D`；按正确 `×` 坐标点击后 `Pushing...` 消失、分支仍 `ahead 1`，参考取消通过；未改代码、未提交、未推送。
- `cleanup-check-174.json`（终端大字号动作区修复）：CDP 诊断 PID 23472、正常入口 PID 18692 关闭；临时夹具删除；`mockup.js` `355FC6E3…CAEF`、`mockup.css` `923B2400…5097`；测试 5/5、UI 资产 PASS；ui-size 40 header 63→99、More/Hide 间距 8px、Local/关闭叉完整；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-175.json`（Diff 2000 行截断提示）：实际 PID 13488、参考 PID 9844 关闭；临时夹具删除；参考配置恢复；全局设置仍 `B8868855…DC3D`；实际 8.74MB/92001 行文件从 `0 differences` 改为 `Truncated` + 前 2000 行说明，参考完整 1 difference；新 `mockup.js` `265CD54C…D788`、`mockup.css` `037517C2…4EF9`；测试 5/5、UI 资产 PASS；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-176.json`（最新构建正常入口冒烟）：PID 3056 使用全局设置副本、正常入口打开 `D:/github/Augit`，加载当前 265CD54C/037517C2 资源并显示 `tools/release.ps1`，2880×1716/DPI168/浅色；构建 0 警告 0 错误；临时设置副本删除；全局设置仍 `B8868855…DC3D`；无代码改动、未提交、未推送。
- `cleanup-check-177.json`（二进制 Diff 参考复核）：实际 PID 6084、参考 PID 21560 关闭；临时夹具删除；参考配置恢复；实际 `sample.bin` 显示 `Text diff is unavailable (Binary).`，参考 `.bin` 为 `unknown file type` + `Associate`、`.zip` 为归档比较页；无同态二进制摘要页，不增加页面级 50；未改代码、未提交、未推送。
- `cleanup-check-178.json`（`verify-ux-frame-buttons` 断言对齐中间态）：只改测试，复位交互后的滚动位置，并按 2026-10-19 大字号布局把 Local/关闭段期望从 91px 改为 `--terminal-session-height`（40 号 UI 为 49px）；套件不再提前假失败，此时仍停在深色 `panel` 期望 `#1E1F22`；保留为中间证据。
- `cleanup-check-179.json`（`verify-ux-frame-buttons` 最终通过）：继续把深色 `panel`/`mutedPanel` 期望订正为 design-system §6.2 的 `#191A1C`/`#212326`，运行 **24/24 PASS**；未改产品代码/视觉稿，页面级仍 50、像素仍 0；测试浏览器关闭，未提交、未推送。
- `cleanup-check-180.json`（标题栏/文档按钮测试对齐）：订正英文菜单/aria 与深色 hover `#FFFFFF17`、pressed `#FFFFFF29`、chrome `#26282C`、faint `#4C4F56`，可选截图在无文档工具栏场景跳过；`verify-ux-titlebar` **12/12**、`verify-ux-document-button-states` **60/60**；连同 frame-buttons 24/24，三个既有失败套件均通过；未改产品代码/视觉稿，页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-181.json`（终端状态栏高度修复）：正常入口 PID 3620，真实点击 Terminal；终端打开时 `--augit-status-height` 28→32 CSS，状态栏上缘 1668→1661、正文底 1667→1660，与参考上缘 1661 一致；隐藏终端后回到 1668；新增 5/5 守护，新 `mockup.js` `DA93667A…3C59`、`mockup.css` `C7E21979…B132`；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-182.json`（终端状态栏 1px 顶边）：正常入口 PID 452；`border-top` 改为 `calc(1px / var(--augit-dpr))`，1661 `#F1F2F4`、1662 `#E9EAEE`，与参考逐像素一致；新增 6/6 守护，`mockup.css` `55EB7EB8…224A`；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-183.json`（深色终端 1px 底边）：正常入口 PID 4672；`body[data-theme="dark"] .terminal-tool` 加 1 物理px `#191A1C` 底边，1660 `#191A1C`、1661 `#212326`、1662 `#26282C`，与参考一致（1659 单像素抗锯齿级差）；守护 7/7，`mockup.css` `F53F53D8…1EDF`；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-184.json`（深色终端 320 面板复核）：正常入口 PID 13516、`bottomPanelHeight=320`；面板过渡约 1080、首行约 1193，参考 1100/1187；首行差 +6 但面板顶差 20，**不能判首行已对齐**，不据此改 CSS；默认 288 面板不能直接与参考比首行；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-185.json`（终端 ANSI 蓝复核）：正常入口 PID 3540，受控执行 `Write-Host ... -ForegroundColor Blue/DarkBlue`；主导蓝 `#298ADE`，调色板 `#063FDB`/`#2D61F0` 仅少量像素；无法证明是 ANSI 槽位还是 24-bit 输出，**未改配色**；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-186.json`（ANSI 映射尝试与回退）：CDP 原始字节见 `38;5;12`，尝试映射为 `#0060AF`；正常入口 PID 13420 重测仍 `#298ADE`，映射未生效；已删除 `terminal-ansi.js` 与单测、恢复 `live-data.js` 12D57E34，未留下产品代码；未区分缓存/24-bit/集成路径，不能算修复；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-187.json`（无 CDP Settings 采样失败）：cycle1 PID 11864 录像结束前未点 Settings；cycle2 PID 9360 的 `(2605,50)` 命中 Search，4s 帧为 Open File 浮层，设置窗未打开；两轮均不能用于 2904ms/500ms 或性能结论；原始 artifacts 保留并标为 invalid；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-188.json`（无 CDP Settings cycle3 失败）：PID 8204，点击 `(2700,50)` 后 4s 帧为 **DSH Web GUI**，Augit 已不在前台，设置窗从未打开；仍不能用于性能结论；原始 artifacts 标 invalid；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-189.json`（当前构建正常入口冒烟）：ANSI 映射回退并重建后，PID 18800 正常入口打开 fixture，Terminal 可打开、提示符正常；加载 `live-data.js` 12D57E34、`mockup.js` DA93667A、`mockup.css` F53F53D8；构建 0 警告 0 错误；临时夹具删除，全局设置仍 `B8868855…DC3D`；无代码改动、未提交、未推送。
- `cleanup-check-190.json`（Settings UIA 入口与有效无 CDP 周期）：UIA `Settings` 节点 rect `{2637,12,54,53}`，点击 `(2664,38)` 成功打开；cycle4 PID 20068 正常入口无 CDP，4s 帧确认设置窗；`analyze-settings-onset` 探针首变化 1.198s，但受主窗变化影响，不记为输入到呈现耗时；页面级仍 50、像素仍 0；未改代码、未提交、未推送。
- `cleanup-check-191.json`（Settings cycle6 慢开样本）：PID 10924 正常入口无 CDP，点击前时间戳距录像开始 37ms，标题探针首变化 5.447s（5.35s 未开、5.55s 已开）；包含输入 helper/录像负载，**不能直接归因产品**；7.482s 关闭点击未生效，无关闭耗时；页面级仍 50、像素仍 0；未改代码、未提交、未推送。
- `cleanup-check-192.json`（Settings cycle7 精确点击失败）：PID 9272，自写 precise-click 记录 +2.292s，但 3.2s/4.5s 帧仍主窗，Settings 从未打开；onset 8.538s 不是设置标题；cycle7 无效，不能用于延迟；页面级仍 50、像素仍 0；未改代码、未提交、未推送。
- `cleanup-check-193.json`（Settings cycle8/9 UIA Invoke 失败）：独立手动 UIA Invoke 可打开 Settings，但在 `settings-no-cdp` 录像实例中 `FromHandle` 找不到 Settings；cycle8 PID 21032、cycle9 PID 16860 均未打开设置窗；cycle9 mkv 0 字节；均无效，不能用于延迟；页面级仍 50、像素仍 0；未改代码、未提交、未推送。
- `cleanup-check-194.json`（三个 UI 状态套件汇总）：当前源码下 `verify-ux-frame-buttons` 24/24、`verify-ux-titlebar` 12/12、`verify-ux-document-button-states` 60/60 全部通过；测试浏览器关闭；不改产品代码/视觉稿；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-195.json`（Settings cycle10 UIA 失败）：PID 11036，录像开始后等待 3s 再 Invoke，仍 `Settings element not found`；设置窗未打开，cycle10 无效；说明不只是“树未就绪”，需换 harness/宿主时间线；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-196.json`（当前未提交代码复核）：14 文件/325 insertions；确认无 ANSI 临时映射残留、无重复选择器冲突、共享资产字节一致；三个状态矩阵套件及四个定向守护通过；未改代码/未提交；页面级仍 50、像素仍 0。
- `cleanup-check-197.json`（Settings 关闭 UIA 尝试失败）：PID 7756，FromHandle 下仍 `Settings element not found`，设置窗未打开；未取得关闭路径/耗时；未改代码；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-198.json`（剩余缺口与所需输入汇总）：整理 Settings 性能/关闭、终端 ANSI/深色首行/右侧栏、Diff 局部加载、搜索超时、Git 结果页与整页像素的已有证据、阻塞和下次所需输入；明确不应重复的坐标/UIA/调色板/288 面板路径；未改代码；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-199.json`（搜索超时尝试失败）：250k 文件临时树，PID 14792；`Ctrl+Shift+F` 与左轨搜索点击均未打开搜索浮层，18s 仍主窗；未取得 timeout；不能判产品；250k 树已删除；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-200.json`（Settings 打开探针 636ms）：正常入口 PID 20968 无 CDP，真实 SendInput 点击 `(2664,38)`；点击时间戳在 mouse down 前，6.575s 标题探针首变化，减去 5.939s = **636ms**；6.5s 未开/6.7s 已开；只是探针，不是完整首内容，未复现 2904ms；关闭仍未测；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-201.json`（Settings cycle12 打开/关闭）：PID 8356，打开点击 +5.965s、标题探针 6.842s（**877ms**）；用 EnumWindows 找到 Settings HWND 后点击 Cancel，exact 10.398s 仍开、10.595s 已关，Cancel **生效**；帧/输入时间戳有 ≤0.23s 偏差，不能给精确关闭耗时；两个打开探针 636/877ms，均非完整首内容；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-202.json`（Settings 原生窗口生命周期）：正常入口 PID 13412、无 CDP、真实 SendInput、5ms 轮询；3 周期窗口出现 **268/286/297ms**，窗口销毁 **60/62/63ms**；均为原生窗口生命周期，不是首内容/presented；未复现 2904ms；页面级仍 50、像素仍 0；未修改代码、未提交、未推送。
- `cleanup-check-203.json`（Settings 原生生命周期 10 周期）：PID 4176；打开窗口出现 267–327ms（median 276、mean 283.4），关闭 61–65ms（median 63、mean 63.1）；合并 13 周期打开 267–327ms、关闭 60–65ms；仍是窗口生命周期，不是首内容；未复现 2904ms；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-204.json`（终端 ANSI 蓝调色板修复）：xterm ansi[12] 直接取 `theme.brightBlue`，浅色 `#2D61F0`→`#0060AF`；同一 `Write-Host "BLUE"` 输出核心 46px 由 `(45,97,240)` 变 `(0,96,175)`；测试 2/2；正常入口确认；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-205.json`（搜索入口再次尝试）：PID 10600，真实点击 `(32,362)` 后搜索浮层仍未打开；未取得 timeout，不判产品；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-206.json`（头部搜索入口）：PID 18000，点击 `(2606,38)` 打开的是 **Open File** 对话框，不是仓库文本搜索；未取得 timeout；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-207.json`（25 万文件搜索）：CDP 诊断仅用于读取 DOM，真实点击正确搜索轨 `(32,257)` 打开 `Find in Files`；250k 文件、查询无结果，18s 内显示 `No results found`，**未触发 timeout**；未精确记录完成时刻；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-208.json`（100 万文件搜索）：PID 20628，正确搜索轨 `(32,257)`，1M 文件、20s 内 `No results found`，**仍未触发 timeout**；创建约 111s、删除约 215s；不再盲目加文件数；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-209.json`（当前构建身份）：构建 0 警告 0 错误；exe `1738568C…`、dll `FA350BFF…`、ProductVersion `0.1.0+da572899…`；JS/CSS/palette 哈希记录；frame 24/24、titlebar 12/12、document 60/60、palette 2/2、状态栏 7/7、Diff 5/5、Smart Checkout 5/5、大字号 5/5、UI 资产 PASS；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-210.json`（搜索超时代码路径）：`SearchOptions.Timeout` 15s、`SearchTimedOutMessage`、`RipgrepSearchService` timedOut 返回路径均有代码/测试；250k/1M 实测均在 15s 内完成无 timeout；结论是未触发而非缺失；不再加文件数；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-211.json`（搜索限速尝试）：`PROCESS_MODE_BACKGROUND_BEGIN` error 87；`IDLE_PRIORITY_CLASS` 可设但不降 I/O；250k Idle 后仍无 timeout；不再重复该 P/Invoke；需 Job Object I/O 限速/慢文件系统；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-212.json`（搜索 SMB 慢共享尝试）：`New-SmbShare` 因非管理员“拒绝访问”，未创建共享、未启动搜索；100k 临时树已删除；不再重复；页面级仍 50、像素仍 0；未提交、未推送。
- `cleanup-check-215.json`（终端首行顶部内距）：`.terminal-view` `padding-top` 11→2.5 CSS；正常入口 PID 14596、深色 315.4、同内容：header 顶 1176、首内容 1187，与参考一致（配准 dy -1）；新增 `tools/test-terminal-view-padding.mjs` 2/2，UI 资产 PASS；页面级仍 50、像素仍 0。
- `cleanup-check-216.json`（浅色终端回归）：正常入口 PID 21408、浅色、同内容：header 分隔线 1176、首内容 1187，与深色一致；顶部内距修复主题无关；页面级仍 50、像素仍 0。
- `cleanup-check-217.json`（终端区域对齐汇总）：矩阵第 37 行更新为“深色纵向/浅色 ANSI 已对齐”；深色 header 1176、首行 1187、配准 dy -1；浅色首行 1187；右侧 42 CSS 工具轨登记结构差异；页面级仍 50、像素仍 0。
- `cleanup-check-218.json`（终端顶距后三套件回归）：frame 24/24、titlebar 12/12、document 60/60 全部通过；共享 CSS 修改未使三个状态矩阵回归；页面级仍 50、像素仍 0。
- `cleanup-check-219.json`（推送后正常入口冒烟）：PID 6344，ProductVersion `0.1.0+15600a2…`；Terminal 正常打开、真实输入 `Write-Host "SMOKE"` 输出；资源哈希 `mockup.css 33974150…`；页面级仍 50、像素仍 0。
- `cleanup-check-220.json`（终端内容二维配准）：深色 315.4 + 2.5px 顶距，header 1176、首行 1187；内容区域二维配准最佳 dx -1 / dy -1；这是终端内容区域，不是整页像素合格；页面级仍 50、像素仍 0。
- `cleanup-check-221.json`（终端修复后完整 .NET 测试）：`dotnet test Augit.slnx --no-restore` 共 **521 通过 / 0 失败 / 0 跳过**（Core 72、Shell 246、Infrastructure 203）；共享 CSS/调色板修改未造成测试回归；页面级仍 50、像素仍 0。
- `cleanup-check-222.json`（搜索超时与取消）：正常入口受控停顿真实触发 15 秒超时（已完成结果保留 + 超时说明 + rg 结束）；发现并修复“关闭浮层/改发新查询不结束 ripgrep”的取消缺陷，修复后 Esc 后 <0.3s 结束；Shell 246/246、桥接/接线测试通过；推送 `83650e1`；页面级仍 50、像素仍 0。
- `cleanup-check-223.json`（Diff 加载形态）：按参考源码把骨架屏改为头部不定进度条 + 正文 `Loading...` 消息；mockup-scenes 54/54（浅/深）、verify-ux-history 42 组、diff-initial-loading 2/2、UI 资产 PASS；源码形态对齐，非像素合格；页面级仍 50、像素仍 0。
- `cleanup-check-224.json`（Settings 原生 presented 时间线）：正常入口 PID 5328、隔离设置副本（全局仍 `B8868855…DC3D`）、真实鼠标、DWM cloak 探针 3 周期：点击→presented **495–585ms**，创建→presented 423–489ms，`CapturePreviewAsync` 85–94ms，Esc→消失 32–60ms；presented 与 +60ms 共 6 张截图同哈希、首帧即完整 Appearance 页；3 次快速点击齿轮仍只有 1 个设置窗；2904ms/捕获超时/深色/跨屏未覆盖；页面级仍 50、像素仍 0。
- `cleanup-check-225.json`（Git 操作结果 balloon 形态）：按参考 `Push failed` 实测把通知改为 375px 白底 8px 圆角卡片 + `notification*.svg` 类型图标（#DB5860/#389FD6/#EDA200）+ 粗体标题 + 正文 + 蓝色文字动作；正常入口 PID 1560 成功推送实测 `Push` / `Pushed to origin/master.`（蓝色信息图标）；失败尝试确认模态期间 toast 不刷新、旧通知残留，回退失败通知，保留对话框内联错误（参考为关闭对话框 + balloon，与本产品保留重试入口冲突）；`verify-ux-operation-result` 24/24、mockup-scenes 54/54（浅/深）、三个状态矩阵通过；浅色成功形态对齐，失败未对齐、非像素合格；页面级仍 50、像素仍 0。
