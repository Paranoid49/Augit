# PyCharm 复原整改清单（2026-10-12 独立复核）

> **2026-10-12实施接续**：用户已授权按建议顺序修复，R01–R08行为与文档整改已实施，验证及边界见[整改实施记录](ui-restoration-fixes-2026-10-12.md)。原缺陷证据、哈希和未验证矩阵保留为审计时快照；本轮实测不能外推为V01–V10全矩阵或100%复原。

当前不能判定为 100% 复原。本次确认两个设置交互缺陷，并复核仍存在的字体、Markdown、图标缩放与冲突侧范围差异。历史待办清零、视觉稿与运行时代码一致，都不代表与 PyCharm 的所有页面、状态一致。

本清单初次生成仅审计并新增文档，未修改产品实现、未提交Git；用户后续已授权实施，当前结果见文首接续链接。整改建议不是授权新增产品能力；涉及现行规范冲突的实施，应先遵循 AGENTS.md 的冲突处理要求。

## 1. 基线与证据边界

- Augit：分支 `dsh`，HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`。进入审计时已有 120 个跟踪文件改动及多份未跟踪文件，全部保留。
- IntelliJ：`D:/github/intellij-community`，HEAD `576e32820af82f97529d70f9269726803a27c016`，本次实际读取对应源码，不以历史报告代替源码。
- PyCharm：实际激活用户打开的 `Augit – AGENTS.md`，观察 Commit、Git Log、Markdown 原文与预览，以及 Settings → Appearance、Editor → Font。设置通过 Esc 取消返回；没有保存配置、执行 Git 写操作或改变系统 DPI。
- 本次前台确认：Islands Light、Sync with OS、Zoom 100%、Microsoft YaHei UI 12、Compact mode 未开启；Editor 为 JetBrains Mono 13.0、Line height 1.2、连字关闭。截图经过显示缩放，不据其宽高反推系统 DPI 或精确逻辑尺寸。
- 设置缺陷使用当前 `web/` 和现有 `live-shell.spec.cjs` 的 WebView2 消息桩，在 Chromium 中实际按键、点击并延迟消息复现。不是仅凭字符串搜索推断；也不是 Windows 原生外壳实测。
- 本次未启动真实 Augit，未执行全量 UI、C#、真实 Git 写入、真实终端或跨显示器测试。后文明确区分“确认差异”“已登记差异”和“待验证”。

当前 MD5：

| 文件 | MD5 |
| --- | --- |
| `web/src/mockup.js` | `5D0C7DE0D3A935C03F8DFBC414AAFAB4` |
| `web/src/mockup.css` | `F5956EE130E2F3E6BC1DE24E5502B08A` |
| `web/src/live-data.js` | `9A18302CD99D3BBF06900F0270381BF4` |
| `web/src/bridge.js` | `F297BF8769E249CA0D804F4F4EEEF5A9` |

## 2. 确认需要整改的项目

P1：优先处理错误动作、状态竞态、核心内容定位及全局对照基线。P2：视觉、导航组织及证据管理收敛。优先级不表示已经发生数据丢失。

| 编号 | 优先级 | 未复原或错误行为 | 当前证据 | 整改与验收条件 |
| --- | --- | --- | --- | --- |
| R01 | P1 | **设置中焦点在 Cancel 上按 Enter，实际执行保存。** | `live-data.js:10450` 起的全局 Enter 捕获只排除 TEXTAREA 和修饰键，然后无条件 `save.click()`。本次实测 Cancel 持有焦点、Enter 后 `settings/write` 次数为 1，窗口仍等保存应答。IntelliJ `DialogWrapper.java:1537`、`:1551` 起在 Windows 安装 Enter hook，优先执行当前启用 JButton。 | Enter 优先激活焦点按钮；普通字段才落到默认 OK。覆盖 Cancel、Apply、OK、恢复外观、导航按钮、展开中的选择框、输入法组词。Cancel 必须零写入；Apply 留窗；不可用按钮不触发默认保存。 |
| R02 | P1 | **设置没有保存单飞与窗口代际保护：重复保存、旧应答关闭新窗口。** | `live-data.js:3188`、`:3284`、`:10696`。延迟第一次写入后再次点击 OK，记录 2 次写入，OK 仍启用；点击 Cancel 后重新打开设置，新窗数量为 1，释放旧请求后变为 0。旧 `.then(closeSettingsDialog)` 没有检查原窗口身份。 | 建立草稿／保存中／失败／已保存的明确状态和窗口代次。冻结重复提交；旧请求只能更新所属会话，不能关闭新窗口、清除新草稿或覆盖其错误。覆盖保存期间编辑、取消、关闭重开、慢写、失败、乱序；同一提交只发一次请求。 |
| R03 | P1 | **三栏冲突左右侧高亮仍是启发式映射，不能保证所有块准确定位。** | `mockup.js:2243` 的 `conflictSourceHighlights()` 将结果文本投影到来源，再依赖唯一公共上下文匹配；歧义分支返回 `complete:false` 或只画部分行。IntelliJ `TextMergeChange.kt` 直接从 `MergeLineFragment` 读取左右起止行，结果侧从 model 读取。 | 宿主提供稳定块身份和精确三侧范围，前端不要重新猜侧范围。以重复正文、相邻冲突、首行空侧、自动合并的非冲突变化、混合换行验证高亮、导航、滚动与接受动作共同指向同一块。维持现有精确保留换行与撤销能力。 |
| R04 | P1 | **“恢复参考外观”的等宽字族与当前 PyCharm 不同。** | 本次前台 Editor Font 为 JetBrains Mono 13.0；`mockup.js:2338` 的 `REFERENCE_APPEARANCE` 仍为 Cascadia Mono 13。字号相同不能证明字宽、基线、行高相同。 | 更新参考快照的实际来源；先检查系统字体可用性，缺字族明确登记回退。不能复制分发 PyCharm 字体文件，也不能静默覆盖用户偏好。用相同正文核对普通查看、Diff、三栏、行号、终端的实际字族与基线。 |
| R05 | P2 | **Markdown 排版尚未统一：正文跟随 UI，h1/h2 却固定，标题与段落缩放比例会漂移。** | `mockup.css:3356` 起正文 `line-height:1.62`、内距 `24px 36px 40px`，h1 为 29px、h2 为 21px，正文继承 UI 字号，代码独立取等宽设置。当前实现与 10-11 已登记的参考排版差异仍在；本次不重称旧 JCEF CSS 数值为新实测。 | 核对 Markdown 专属正文、标题 h1–h6、列表、表格、引用、代码块与行内代码的字族、字号和留白；建立完整相对度量。UI 12/20/40 下不能正文扩大而标题保持 29/21。Markdown 字号跟随关系涉及现行产品规范，实施前先统一规则。 |
| R06 | P2 | **Markdown 模式动作位置不同，Augit 多占一条正文工具栏。** | 本次前台 Raw/Split/Preview 图形位于编辑标签右侧；`mockup.js:3428` 及 `liveMarkdownDocument()` 仍在正文 `document-toolbar` 中绘制。IntelliJ `TextEditorWithPreview.kt` 的 `isShowActionsInTabs` 为 New UI 且有标签时成立，模式动作加入 tab actions。 | 在现有三模式范围内对齐入口位置、顺序、选中态、命中区、Tooltip 与键盘焦点；验证切模式不改变原文／预览的阅读状态。不增加编辑能力。 |
| R07 | P2 | **放大界面字号只放大部分槽位，SVG 墨迹仍固定。** | `mockup.js:70` 起写入用户缩放及树图标槽；`mockup.css:922`、`:937`、`:4868` 等 SVG 仍固定 16/13/20px。IntelliJ `LafManagerImpl.kt:858` 起从界面字体设置 userScaleFactor。与前次登记的字号缩放机制差异一致。 | 区分系统 DPI、UI 用户缩放、等宽字号；逐图标族核对实际缩放链，不能只扩大留白或一律放大全部 SVG。验证图形、槽位、描边、命中区和 Tooltip 的对应关系。当前没有同字号墨迹量测，不填造像素误差。 |
| R08 | P2 | **旧“待办清零／目标完成”分类不能代表当前实现与当前参考。** | `ui-classification.md` 保留第 309 轮“目标可判定为完成”，§5 还有旧环境无法取证和退役原生文件入口；当前已有真实可访问的 PyCharm，且 R01/R02 可复现。`goal-audit-2026-10-12.md` 自身也明确仍有缺口。 | 保留历史快照但增加明确日期和适用范围；当前清单以“场景×状态×环境”的可复验证据计数。已修、未修、未验证、明确产品裁剪分别登记，不把无法取证转成还原通过。 |

### R01/R02 实测记录

使用现有专项入口的内存副本执行一次诊断；没有修改仓库的测试或产品脚本。仅在消息桩处暂存 `settings/write`，其它请求走原桩：

1. 从主界面打开设置，将界面字体草稿改为 Tahoma。
2. 聚焦 Cancel，按 Enter；收到一条保存请求，焦点仍为 Cancel。
3. 点击 OK；第二条保存请求产生，按钮未禁用。
4. 鼠标点击 Cancel，再从标题栏打开设置。
5. 交付第一条旧请求给原桩；旧保存回调关闭了新设置窗口。

实际输出：

```text
CANCEL_ENTER { writes: 1, open: true, focus: 'cancel' }
SECOND_SAVE { writes: 2, okDisabled: false }
REOPEN_BEFORE_OLD_REPLY 1
REOPEN_AFTER_OLD_REPLY 0
```

这是缺陷复现结果，不是原 `--settings-diff-only` 全套通过；诊断替换了该专项调用，所以它打印的“通过 0 项断言”不作为验收指标。浏览器中设置写入只作用于桩，不写用户设置。

## 3. 已登记的行为差异，不作为新发现重复计算

| 项目 | 当前情况 | 后续处理 |
| --- | --- | --- |
| Blame → Annotate Previous Revision | `live-data.js:3323` 起明确登记当前为就地重新标注；IntelliJ `AnnotatePreviousRevisionAction.java` 的动作描述及修订打开链为新标签。 | 若要进一步接近参考，在已有只读标签能力内设计路径＋修订身份及返回语义。当前属于已登记差异，不宣称本轮新发现。 |
| 终端单会话、非模态 Git 进度、独有文件预览能力 | 部分是产品裁剪或已经裁决的差异。 | 只验证产品要求及共有交互，不追加多会话、IDE 功能、AI、Force Push、Git Console、多个仓库或普通文件保存。 |
| Remotes 的 Push URL 等 Augit 字段 | 当前已经由双栏内联编辑改成表格＋独立 Define Remote；产品额外字段仍使尺寸不同。 | 旧双栏、额外 cloud 图标不能继续列为当前未修。剩余是附加字段的空间适配和完整状态验收。 |

## 4. 尚不能宣称完全复原的验证清单

以下是具体待验证项，不等于已经确认代码有错。静态视觉稿、浏览器桥接桩、真实 WebView2、PyCharm 前台属于不同证据层，不能互相代替。

| 编号 | 优先级 | 范围 | 必须覆盖的验收场景 |
| --- | --- | --- | --- |
| V01 | P1 | Git 动作启用条件与页面状态机 | Commit/Amend/Commit and Push；空仓、无 HEAD、无勾选、外部暂存、校验失败、hook 失败、写锁、认证需要输入、取消。验证 disabled/hidden、原因、默认焦点、重复动作抑制及结束后从 Git 事实重读。对 merge/rebase/cherry-pick/revert 分别验证 Continue/Skip/Abort 的合法状态，不能只用 merge 代表全部。 |
| V02 | P1 | 冲突编辑和保存 | 在隔离仓库运行真实 IME、选区替换、多行粘贴、删除、Undo/Redo、保存失败、外部变化、取消确认和忙态。10MiB 长行的既有测试不能代替大量短行与中文组词。侧范围先修 R03 再验。 |
| V03 | P1 | 弹层栈、焦点、键盘导航 | Push→Remotes→Define/Remove；日期→日历；搜索→结果过多；冲突→放弃确认。逐层验证 Esc 仅退一层、焦点返回本层触发点、Tab/Shift+Tab 不穿背景、Enter 尊重当前按钮、方向键跳过禁用项、父层草稿／选择／滚动保留。通用焦点仍用 `focusBeforeDialog` 单槽，部分嵌套页另写恢复逻辑，需要逐链验证，不直接判定所有嵌套均坏。 |
| V04 | P2 | 所有二级页面的布局与控件状态 | Clone、Push（含无远端）、Remotes/Define/Remove、Stash/Unstash/Drop、Reset/Rollback、分支/标签输入、Smart Checkout、Worktree、初始化、打开工作区、Go to Line、搜索、设置、冲突列表和结果。对每页登记主次动作顺序、窗口内距、字段高、列宽、底栏、禁用/错误/加载/空态。已有局部测试不等于面级全矩阵完成。 |
| V05 | P2 | Diff、历史、提交图 | 相同真实补丁核对单双栏行号、空白行、词级色、连接区、差异块计数、边界提示、忽略空白、重命名、二进制及超限摘要。覆盖工作区/历史/引用三来源，切标签、过滤分页、外部变更与慢旧请求。复杂分叉/合并、多个引用叠放、HEAD 节点及选择失焦需同场景对照。 |
| V06 | P2 | 配色、图标与焦点例外 | 浅深两主题逐页覆盖普通、hover、pressed、selected focused、selected inactive、disabled、loading、error、danger。Git 文件状态色叠加选中和失焦；图标的路径、线宽、中心、选用场景、空图标列与 Tooltip。Settings 导航是 ALWAYS_PAINT_SELECTION_AS_FOCUSED 例外，不能套通用失焦变灰规则。 |
| V07 | P1 | 主题切换与设置草稿 | Light/Dark/System，外部系统主题改变，设置中预览后 Cancel/Apply/OK，保存失败，Git 正忙、Diff 开着、冲突有草稿、终端有输出时切换。验证只换色、不改变焦点、尺寸、选区、滚动与任务状态；真实 WebView2 和终端 canvas 都要覆盖。 |
| V08 | P1 | DPI、窗口变化与二级窗口几何 | 96/120/144 DPI 及当前参考实际缩放，真实跨显示器，最大化/恢复/最小化、贴靠、最小窗口、大字号、打开弹层后 resize。专项核对 `ShellWindow.cs:1012`：`OnDpiChanged` 更新 `_effectiveDpi` 后交给 DefWindowProc，未显式使用建议 RECT；要验证外框与 WebView2 是否一起保持逻辑尺寸。本次未跨屏复现，不写为确证缺陷。`--dpi` 启动覆盖不能代替真实 WM_DPICHANGED。 |
| V09 | P2 | 终端 | PowerShell/CMD/Git Bash/WSL/自定义命令的启动、失败、运行、隐藏恢复、关闭前台命令确认、子进程回收；Tab 进 shell、Esc 回正文、输入法、复制、2000 行截断、主题/DPI/面板缩放。终端插件会话层不在当前 IntelliJ checkout，源码缺失不等于前台无需验收；不能以普通命令行冒充 JetBrains 终端状态。 |
| V10 | P2 | Windows 10 与跨环境字体 | Windows 10 22H2 x64 尚无实机证据；Windows 11 的结果不外推。核对字体缺失回退、缩放取整、原生窗口边框/阴影和系统选择框。 |

V04 建议按操作链验证：打开 → 初始焦点 → 空/有数据 → 选择 → 修改草稿 → 校验失败 → 再提交 → 等待 → 取消/失败/成功 → 返回父层。每个状态再检查布局、图标、动作启停和焦点；只截正常页面无法覆盖这一链。

## 5. 不应重新列为当前缺陷的旧项目

当前代码及 10-12 接续记录已显示这些路径有修复：Remotes 表格与独立编辑窗、Git 写入/网络等待宿主、启动失败保留真实空态、布局独立归属、显式工作区 Diff 标签、Markdown Ctrl+F。冲突混合换行保存、接受块身份和取消确认也已有专项整改。

它们仍应进入回归矩阵，但不能抄旧清单标成“尚未实现”。本次只运行字节一致检查及新的设置诊断，没有重新背书这些模块的历史通过数量。

## 6. 建议执行顺序

1. 先修 R01/R02，并为焦点按钮语义、慢保存、关闭重开增加自动化回归；这比继续微调间距优先。
2. 修 R03 的侧范围契约，进行 Core/桥接/三栏联动验证；不要继续靠 CSS 掩盖范围不准确。
3. 固定真实参考字体和字号基线，再收敛 Markdown 与图标缩放；否则基线变化会使后续像素测量重复返工。
4. 按 V03/V04 的完整操作链验收二级页面，再做 Diff/Git/终端的真实状态矩阵。
5. 最后补主题、跨 DPI、窗口变化和平台矩阵，更新当前归类。只有明确的产品裁剪和有证据的通过项才能结案。

## 7. 本次检查、资源与复验

- `git diff --check`：通过。
- `powershell -NoProfile -File tools/audit/verify-ui-assets.ps1`：四个共享文件通过。
- 设置诊断：成功复现 R01/R02；没有修改测试期望来伪装通过。
- 浏览器和 HTTP 服务由现有 harness 的 `finally` 关闭，Node 诊断进程退出码 0；结束检查没有 `chrome.exe` 残留。未启动真实 Augit、Git 命令服务器或终端会话。
- 诊断代码从标准输入传入，在内存中替换专项调用，没有生成临时脚本；没有删除其他任务的 `.tmp` 或历史文件。仅新增本报告作为交付物。

用户手动运行已有设置专项的命令（此命令是既有专项，不含上文临时诊断）：

```powershell
$env:AUGIT_BROWSER_EXECUTABLE = 'C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/live-shell.spec.cjs 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright' --settings-diff-only
```

正常完成由脚本 `finally` 停止服务器与浏览器。若用户手动中断且进程残留，只针对本次自己记录的 Node PID 停止整个进程树，不按程序名批量结束：

```powershell
# 将占位值替换为本次专项启动时记录的 Node PID。
taskkill /PID <本次Node进程ID> /T /F
```
