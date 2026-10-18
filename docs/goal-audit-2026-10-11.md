# 持续复原审计与整改（2026-10-11）

本记录承接当前 Goal：持续发现并整改 Augit 现有能力与 PyCharm 2026.2.1 New UI 的差异，再验证并继续审计。Goal 保持 active，不能以本批通过的测试证明整体 100% 复原。

分支 `dsh`，HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`。进入本批已有大量跨模块改动，未回滚、覆盖或整体暂存。本批使用多 agent，源码、批量核对和实现复核均调用 DSH；DSH 候选结论由各 agent 再独立检查，未把其误报直接列为缺陷。

## 当前差异清单

| 优先级／编号 | 当前差异 | 证据与处理状态 |
| --- | --- | --- |
| P1／F-01 | 等宽字体不同，正文栅格和基线尚未统一 | 前台 Settings → Editor → Font：PyCharm 为 JetBrains Mono 13.0、行距 1.2、连字关闭；Augit 用户配置 Cascadia Mono 13。界面两端均 Microsoft YaHei UI 12。已订正设计系统中把 Cascadia 写成 PyCharm 实测值的错误，用户字体未被静默修改。Restore Reference Appearance 仍是旧快照，待核本机字体可用性；不读取或分发 PyCharm 字体文件。 |
| P1／F-02 | Remotes 页面结构不同 | 前台 Git → Manage Remotes：Name／URL 两列表格、Add／Remove／Edit 工具栏、唯一 OK；双击行进入 Define Remote（Name／URL、Fetch remote、OK／Cancel）。Augit 是左列表＋右详情内联编辑。`ux-spec.md:537` 原要求双栏；复核 `design-system.md:18` 已有用户授权 New UI 权威优先且不重复确认，按此裁决继续同步规格和整改。 |
| P2／F-03 | 大字号图标墨迹固定，槽位扩展 | 当前 SVG 16／13／20px 与参考字号驱动 JBUIScale 的用户缩放机制不同；部分旧规则要求图标固定，应沿现行设计系统既有 New UI 优先裁决同步。来源链、文件行号见专项报告，尚未做同字号前台墨迹量测，不能说差几像素。 |
| P1／F-04 | Markdown 正文、标题、代码块的字体与间距不同 | 前台 Languages & Frameworks → Markdown：Chromium browser，Editor and Preview、Split vertically、Preview font size 13，Sync scroll 开启；Custom CSS 的 Load from 与 CSS rules 均关闭。Augit 正文继承 UI 12，h1 29px／h2 21px 写死、h3–h6 使用 UA；代码块独立等宽 13、内距 8／12px。安装中真实 JCEF 默认 CSS 为正文 1.6、横 margin 2em、h1–h6 相对字号、代码块 padding16／line-height1.45。现规范明确要求 Markdown 跟随 UI 字号，属于待统一规格的分歧；未捏造实际渲染字体或测量值。 |
| P2／F-05 | Markdown 模式按钮多一条正文工具栏 | 前台 PyCharm 的 Raw／Split／Preview 在标签区右侧；Augit 固定放在正文上方路径工具条。当前安装 `TextEditorWithPreview` 的 New UI＋有标签路径也将模式动作移到 tabs。现有 Augit 文档工具条规范仍须同步审查，不把 UI 测试符合旧规则当作还原完成。 |
| 部分已修／F-06 | 冲突左右高亮不对称、混合换行字符偏移误落行 | 原右侧整栏染色已移除。按原始 CRLF／CR／LF 起点二分转换结果字符区间；侧栏用原块文本投影，完整等同时精准映射，额外自动合并变更存在时仅映射可靠公共上下文夹住的等同区间。51 项目标回归通过。无法唯一定位时保持中性，`complete:false` 明确保留覆盖不足；宿主提供准确侧范围仍是后续整改，不能称侧高亮全面等价。 |
| 已修／F-07 | 编辑混合换行曾统一 LF | 已按 beforeinput 真实范围维护原始字符补丁，未改范围保留原边界，Undo／Redo 回放补丁；46 项目标测试通过。真实 WebView2 修改首行并非顺序接受两块后，磁盘正文逐字符匹配预期（含公共上下文的 CR／LF／CRLF）。IME与大量短行性能仍需进一步验收。 |
| P2／F-08 | 部分审计入口仍缺完整隔离与有效 CDP 超时 | 本实例新增显式设置文件和 WebView2 目录已通过测试，但老审计脚本尚未全部迁移。CDP ReceiveAsync(None) 的 deadline 无法中断阻塞；部分 finally 只清主进程，未确认所有后代。桥接新增 timeout0 不建自动timer，远端写入／Fetch已接入；其他旧 Git 写入调用仍需逐项检查，不能把前端超时当作宿主操作取消。 |
| P2／F-09 | 新工作区首次仅拖布局，尺寸归属仍受 LastWorkspace 门控 | 正常 `settings/write` 的两尺寸现已真实落盘；`settings/read` 对当前工作区匹配有门控，尚未发生 session/write 的新工作区可能不恢复布局。需按工作区打开原子更新会话归属，不能保存布局时把旧标签搬到新工作区。 |
| 验收缺口／F-10 | 其他二级页面、完整状态矩阵仍未取证 | Clone、初始化、搜索、打开工作区、操作结果、进度等仍需当前真实前台场景；全部 Git 颜色＋焦点组合、复杂提交图、深色全页面、跨 DPI／字体栅格仍无完整矩阵。Windows 10 22H2 x64 没有实机，未宣称验证。 |

专项证据见 [几何与状态](audit-geometry-evidence-2026-10-11.md)、[二级页面](audit-secondary-evidence-2026-10-11.md)。旧 67 项或更早覆盖表不是当前差异数量；已修复的问题不重复计算。

## 本批已整改

1. Git Log 接入 PageUp／PageDown，沿已有选择事件更新哈希、详情与 ARIA；失焦和刷新仍保留选择。
2. 冲突 Cancel、标题关闭、Back、Esc 统一走取消流程，dirty 时 Continue 保留 DOM／正文／选区／滚动／原生撤销记录，Discard 只丢内存草稿；applying 时拒绝退出。精确 0／1／2 英文单复数。
3. 三栏纵向按共同正文锚点同步，空只读侧保留虚拟滚动空间，横向独立；字符 offset／length 正确转换为结果行高亮范围，不再把宿主字符偏移当行号。
4. 任意顺序接受冲突使用稳定原始块身份；重复正文、相邻块和空侧删除后 Undo 不串到另一个来源。Both 按 Core 侧文本原样拼接，消除额外空行。
5. 结果空行不再放 NBSP 字符；保存按逻辑行读文本，保留真实 NBSP、空行、EOF。beforeinput 记录真实编辑范围并维护原始文本紧凑补丁，保留未修改区域的混合换行；Undo／Redo 不改变原生撤销栈。10MiB 长行文本实测尾部编辑约199ms、编辑＋桥接保存1847ms，不能推广为大量短行性能验收。
6. 真实外壳发现 Apply 与 Back 截断：运行时 `<button>` 没有命中静态 `<a>` 规则。共享 CSS 扩展到两种控件，Apply 使用保存动作宽度，Back 固定单行。
7. Remotes 去除每行额外 cloud 图标，静态与实时模板一致；不因此声称表格拓扑已整改。
8. `settings/write` 接收合法面板尺寸并独立合并，不覆盖缺失／非法字段或其他设置；布局写入不再物化旧配置的 UI 字号。
9. Build／Publish 完整镜像 web 路径集合和内容，清理已删除源文件残留，刷新同长度同时间内容；四个审计入口启动前按 SHA256 门禁检查。旧 `data.js` 已不在产物中。
10. 新增审计用 `--settings-file`／`--user-data-folder`，同时指定才隔离本实例的配置与浏览器状态。默认行为保持；隔离实例不激活或覆盖用户单实例登记，显式浏览器目录不执行默认历史目录清理。`workspace/open` 新实例尚未继承这些参数，审计时避开实际新窗出口。
11. 真实空会话不再回退样例标签／正文，覆盖 Git Log、Commit、Diff 等主窗场景；真实文档、比较、读取占位与 Blame 不被空态规则遮蔽。16 项回归通过，后续真实空仓前台已不再显示产品规格样例。工作区读取失败／超时令 live=null 的另一条 fallback 路径仍需整改。
12. 宿主桥接 timeout0 等待真实操作结果，不自动超时；读取仍按正超时处理，发送同步失败及时移除等待项和timer。可控时钟测试覆盖无自动超时、读取超时、正常响应、宿主错误和发送失败。远端模块网络／写入采用此契约，其余已有写操作尚未全迁移。

## 实机证据与限制

使用 computer-use 技能 Sky 操作当前已打开 PyCharm。未修改系统 DPI、未关闭用户 PyCharm、未编辑用户文件或执行用户 Git 操作。Appearance／Editor Font／Markdown 设置只读，所有观察窗口 Cancel 或 Esc 返回；主题、字号、布局配置没有应用变化。

Augit 使用当前 Release＋SHA256 15 文件镜像验证，在相同 1640×985 截图尺寸观察 Project、Markdown 与 Git Log。窗口尺寸相同不等于已统一文件、分栏、字体或所有最终计算样式，不据此作逐像素结论。临时拖动用于对齐项目树和底部高度，未直接写用户仓库。

后续真实冲突测试使用本任务临时仓库（独立配置、WebView2 profile），包含两个冲突块和 40 行公共上下文。前台 Next Difference 后先 Accept Yours 解决第二块，再 Accept Theirs 解决剩余第一块，计数 2→1→0；Ctrl+Z／Ctrl+Shift+Z 为 0→1→0。重建按钮样式后重新进入同一 fixture，实际 Apply 保存：磁盘逐字符等于 `before\ntheirs A\ncommon-1…common-40\nours B\nafter\n`，Git 从 UU 变为暂存 M，无未合并路径，列表为 No unresolved conflicts，Continue Merge 可用。未继续完成 merge。此场景证明统一 LF 两块保存链路，不能推广混合换行或所有编辑动作。前台还确认修复后 Apply／Back 完整单行；右侧全栏着色仍是未修差异。样例标签在无活动文件时仍短暂显示产品文档，该空态候选需下一批专门核查，不混入已经确证的结论。

随后空态已定位并整改，16 项回归通过，真实空仓编辑区不再出现样例。最新混合换行补丁用同一隔离 fixture 再验：公共上下文交替 CR／LF／CRLF，真实鼠标聚焦结果、Ctrl+Home 后键入 `edited-`，先接受第二块 Yours 再接受第一块 Theirs，Apply 后逐字符等于记录的 expected-mixed.txt，原结束符完整保留。Git 已解决但未继续 merge，用户配置哈希仍相同。第二次混合 fixture 曾把 CR 放在冲突标记行末导致 Core 不识别完整块，不作为通过场景；已重建为标记 LF、公共上下文混合换行。该试验同时发现混合 CR 下结果高亮字符起点计算错误，已纳入 F-06 相关整改。

首次前台取证仅使用 `--no-session-restore`，发现它不抑制 session 写回，用户标签记录被改为 AGENTS.md；任务检查该字段仍等于本批值后，仅将 openFiles／activeFile 恢复原 web/src/mockup.js，保留其他字段。新隔离运行前后用户配置 SHA256 均为 `BC0B46DC65D6F35A171D7D87DA9D9DDFECBB93154AEC22A636453A002A7E283D`。

## 分批验证

| 范围 | 当前结果 |
| --- | --- |
| Git Log 目标回归 | `--goal-log-only` 7 项通过 |
| 冲突目标回归 | `--goal-conflict-only` 51 项通过，包括混合换行精确保存、任意顺序、Undo／Redo、三侧滚动、取消冻结、默认与大字号按钮文字完整、侧栏可靠投影与歧义不误高亮 |
| 空会话回归 | `--empty-session-only` 16 项通过，静态视觉稿保留样例、真实空态拒绝样例 |
| Remotes 图标接线＋相关状态 | `--reference-only` 11 项通过 |
| Remotes 视觉稿 | 12 场景通过：浅／深、13／40px、DPR1／1.25／1.5；只证明当前规范布局和控件可见性 |
| 尺寸持久化＋审计隔离 | 36 项隔离配置测试通过 |
| Shell 模块完整测试 | 161 项通过，18 秒；启动／桥接／配置共用契约改变后触发 |
| Web 镜像回归 | 前序本批 53 断言通过，实际 Build／Publish 15 文件镜像通过；当前 Release Build 零警告零错误、15 文件 SHA256 门禁通过 |
| 定向格式与静态 | dotnet format 指定六文件通过；JavaScript 语法、共享四文件 byte 一致、git diff --check 通过 |
| 文档检查 | 脚本编码 16 项通过，数字声明 DOC_CLAIMS_OK、覆盖生成器一致性通过；2373 项旧完整实时快照已过期，不作当前全量结果 |

本批没有无分析地重复全量 UI 测试。新的继续整改产生代码变化时，应先跑失败／受影响范围，再按模块边界扩大。

## 资源与启动／停止

本批 HTTP／无头浏览器由 finally 关闭；真实冲突 Augit PID10248 已停止；按钮复验与保存实例 PID5892、混合 fixture PID27000／888 均通过 CloseMainWindow 关闭，9359 监听消失，任务 profile 命令行的后代进程为零。更早取证 PID26596／19184／23856 也均退出。未停止用户 PyCharm、Edge 或 DSH GUI。

自动审批拒绝任务临时目录删除（仅返回 `blocked by policy`，无具体原因），不据此宣称清理完成，也不换工具绕过：

- `C:/Users/wl024/AppData/Local/Temp/augit-web-mirror-publish-0b5bf5f6a3f649fb94f340b29768bd29`，此前 agent publish 校验产物。
- `C:/Users/wl024/AppData/Local/Temp/augit-goal-shell-42c627f15c244f018726859a97efc66a`，独立冲突仓库／设置／WebView2 profile。组合命令被拒后单独关闭指定 PID 成功；校验临时路径边界与进程退出后，单独 Remove-Item 也被拒。
- 本批 DSH 错误扩大到历史调查后产生的 `.tmp` 15 文件（dump_mine.py、dump_named.py、dump_session.py、my_session_dump.txt、scan_all_out.txt、scan_all.py、scan_parent_out.txt、scan_parent.py、scan_sessions.py、session_dump.txt、session-50d4a207_dump.txt、session-b68e267e_dump.txt、session-d350a7ef_dump.txt、struct_out.txt、struct_session.py）。已中止错误 DSH 并改精确单行任务；精确删除其中五文件被拒，未再次尝试。

正式验收截图保留 `artifacts/goal-audit-2026-10-11/remote/`，不当作临时文件删除。

正常启动和停止（只停止这条命令启动的进程）：

```powershell
dotnet build src/Augit.Shell/Augit.Shell.csproj -c Release --no-restore --disable-build-servers
$augitRun = Start-Process 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit' -WindowStyle Normal -PassThru
[void]$augitRun.CloseMainWindow()
[void]$augitRun.WaitForExit(5000)
```

隔离审计必须给自己的绝对路径并同时传两参数：

```powershell
$augitAudit = Start-Process 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit --scene git-history --no-session-restore --settings-file C:/Temp/AugitAudit/settings.json --user-data-folder C:/Temp/AugitAudit/webview' -WindowStyle Normal -PassThru
[void]$augitAudit.CloseMainWindow()
[void]$augitAudit.WaitForExit(5000)
```

`--no-session-restore` 单独使用不代表隔离。审计结束应关闭自身实例、确认后代和端口退出，再清理自己创建的目录；不能按名字关闭用户的所有 Augit／WebView2。

## 本批哈希

| 文件 | 接续时 MD5 | 本批验证时 MD5 |
| --- | --- | --- |
| mockup.js | A482BCD5FCE46FD57EA667D840B23D1D | 61B26F72B0C9F3EDABB0A0671664E4F0 |
| mockup.css | B46C6E073405329890FF4CA409135324 | E120EEA8028753C37EEC1B01B1FB4818 |
| live-data.js | 7B87127A9CF57CC637276E999D4D1DF2 | BEA00D94F9499F0733B4E211A22A35AC |
| bridge.js | 16E41439BB7F4C2A36C2E95A4375A823 | 16E41439BB7F4C2A36C2E95A4375A823 |

后续 mixed newline 等整改正在继续，以上是本批验证快照，不替代后续实际哈希与测试。
