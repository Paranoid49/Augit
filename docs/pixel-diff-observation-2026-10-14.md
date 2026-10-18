# 2026-10-14 真实工作区差异对照

## 单个差异文案与批次收尾

共享渲染器的差异摘要在只有一个变更块时显示 `1 difference`，零个和多个保持 `N differences`。新增回归覆盖零个、一个、两个变更块，以及删除与新增构成一个替换块的计数；与首次加载用例合计9项通过。真实外壳同时断言单数文案，见[实际结果](../artifacts/pixel-followup-2026-10-14/diff-summary/geometry.json)及同目录 `reference-diff-Light.png`，截图已目视确认。该截图仍非PyCharm同态配对；其中LF/Mixed换行标签的语义差异保持待整改。

修改涉及两份共享 `mockup.js`、`diff-first-hunk.test.cjs` 和真实审计驱动。JS语法、9共享资产及空白检查通过。mockup.js MD5由 `8CDBB8764ED05E510C9FF0CABA0DDB8F` 变为 `9FB7D02E3D4442700E4F10910161CCDE`；CSS为 `7CDC655B0AFCA85E50C217F234A4C361`，live-data为 `73B757F728FC893773DCB3F24F622311`，bridge为 `F297BF8769E249CA0D804F4F4EEEF5A9`，后三者未变。分支仍为dsh、HEAD仍为3e084a2d，未暂存或提交混合修改。

驱动结束后独立查询确认PID21276不存在、端口50954无监听、临时目录 `C:/Users/wl024/AppData/Local/Temp/Augit-pixel-fix-Ocyvih` 不存在。本批次没有残留服务器；历史审批拒绝删除的目录仍按接续记录保留，不据此声称全部历史资源清零。

复验启动命令如下；正常退出由驱动finally关闭本次进程树并删除本次profile，异常中断仅停止日志打印的本次PID：

```powershell
node --test tools/audit/diff-first-hunk.test.cjs tools/audit/diff-initial-loading.test.cjs
node tools/audit/run-pixel-remediation-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/pixel-followup-2026-10-14/diff-summary --single --reference-fixture --reference-diff
taskkill /PID <AUDIT_PID> /T /F
```

用户已排除Windows10实机验收，并明确保留跨屏DPI为未验证；这两项不作为本次完成前置条件，也不转为通过。55页及全模态配对、背景刷新前景帧和延迟、终端/冲突逐行配对、系统阴影仍未验收，整体目标未完成。

## 正文落点的纵向滚轮补验

此前纵向滚轮使用整个容器中心，落点可能在行号区域，不能证明开启独立横向滚动后左右正文仍能传递纵向滚轮。本轮将鼠标分别放在左右正文100/20的位置，每次从顶部滚动400；父容器scrollTop均变为400，两侧自身scrollTop均为0，左右变更行及连接图形顶部一致。见[落点记录](../artifacts/pixel-followup-2026-10-14/diff-side-wheel/geometry.json)的sideWheel。该结果排除了当前100行用例中正文截住纵向滚轮的问题，不外推触控板惯性、全字号或全部比较模式。

本轮只增加审计驱动断言，没有修改产品；四网页哈希与前批相同。真实单组、JS语法和空白检查通过，PID15040及子树、端口49822和临时profile全部清理，内存DOM移除。复验沿本文命令，输出改为diff-side-wheel；异常只停止打印的本次AUDIT_PID。完整参考配对、终端/冲突、背景前景及阴影仍未验收。

## 双栏长行横向访问

真实WebView2回归将首行扩为750字符：旧实现栏宽353px、内容5863px、overflow-x:hidden，用户无法横向访问，新增断言先负向失败。共享CSS现将diff-side设overflow-x:auto、overflow-y:hidden，纵向仍由三栏父容器统一滚动。修后实际横向鼠标滚轮使左正文移动，右正文、行号槽和父容器横向位置均保持0；之前100行纵向滚轮与连接对齐回归也通过。见[真实结果](../artifacts/pixel-followup-2026-10-14/diff-horizontal-after/geometry.json)。这证明横向滚轮访问，不代表滚动条始终贴在可见底部或全部键盘交互已验收。

JS语法、9共享资产及空白检查通过。CSS MD5由7FA7858A0756567A586DA0500953009D变为7CDC655B0AFCA85E50C217F234A4C361，三个JS未变。修前PID10960/端口52034，修后20092/55854及各自profile均清理，测试DOM移除。启动沿本文命令，输出diff-horizontal-before/after；异常停止只使用本次AUDIT_PID。无新增依赖、未暂存提交；整体目标保持未完成。

## 长差异用户滚动

真实WebView2中调用产品diffBodyParts渲染100行/240px高度，原实现overflow:hidden：脚本设置scrollTop=900可以移动且连接对齐，但用户不能正常滚动。新增断言在旧实现明确失败，说明以往程序化定位不是用户滚动证明。

共享CSS的diff-columns改为overflow:auto，正文、行号及连接区共同滚动。修后保留程序化对齐检测，并用Playwright鼠标滚轮实际滚动，确认scrollTop增加且变更正文/连接区顶部差小于1px。结果见[真实外壳记录](../artifacts/pixel-followup-2026-10-14/diff-scroll-after/geometry.json)。该100行是内存测试数据，不是新的PyCharm双端截图，也未证明所有横向长行和单栏交互已通过。

真实单组、JS语法、9共享资产、空白检查通过。CSS MD5由D4C73495006C81274D86093208862E36变为7FA7858A0756567A586DA0500953009D，三个JS不变。修前PID15300/端口57873，修后26312/55853及两份profile均由finally清理；内存测试DOM已移除。启动沿本文命令，输出改为diff-scroll-before/after；异常只taskkill本次AUDIT_PID。整体目标继续保持未完成。

## 空侧边界与连接起点

权威DiffDrawUtil.java:595–626给空范围无正文背景，在首行顶部或前一行底部绘制doubleLine；drawChunkBorderLine:112–127的实线双线为两条相邻像素线。现空侧连续新增/删除占位只在块起点绘制2px类型色边界，不重复画满各占位；连接多边形空侧尖端由块中点改为实际起点。保留实际空行与替换高亮。

8项定向测试通过，新增连续块只有一条边界及尖端在顶部的断言。真实新增夹具及两种视口高度回归通过，PNG已目视，见[真实证据](../artifacts/pixel-followup-2026-10-14/reference-diff-boundary/geometry.json)。滚动、多个复杂替换块与全字号的边界精确像素仍未验证；本次不宣称全部块型对齐。

JS语法、9共享资产和空白检查通过。mockup.js MD5从4167161E487BEC6C953E6A7AE61D62DD变为8CDBB8764ED05E510C9FF0CABA0DDB8F；CSS从F7F72768E68D502908F7E8D5B8F7D5A2变为D4C73495006C81274D86093208862E36；live-data/bridge不变。PID5908、端口61988、profile由finally清理。启动沿本文命令，输出改为reference-diff-boundary，异常仅taskkill本次AUDIT_PID。整体目标仍未完成。

## 缺失侧占位高亮修复

新增行的旧侧、删除行的新侧此前继承整行类型色，导致没有文本的一侧也染满绿色/灰色。现在按该侧是否具有真实行号决定正文高亮；有行号且文本为空的实际空行仍高亮，两侧都有内容的替换保留修改底色。连接图形、行号槽和占位高度保持既有规则。空侧边界线的精确绘制尚未配对，本次不声称全部块形状已完成。

新增负向回归在修前明确失败（added不等于空类型）；修后新增、删除、实际空行、替换两侧与原有首片段/加载测试共7项通过。真实新增夹具旧侧占位背景透明，新增文本仍高亮；两种区域高度下连接对齐仍通过，PNG已目视。见[真实结果](../artifacts/pixel-followup-2026-10-14/reference-diff-empty-side/geometry.json)。JS语法、9共享资产和空白检查通过。

mockup.js MD5从A2E18435D77D232EC4F13D69427668B3变为4167161E487BEC6C953E6A7AE61D62DD，CSS及其他网页文件未变，两份共享JS一致。PID7356、端口63105及临时profile均由finally清理；启动沿本文命令输出改为reference-diff-empty-side，异常仍只taskkill本次AUDIT_PID。整体55页/全模态、背景前景、终端/冲突和阴影验收仍未完成。

## 换行标签颜色

按DiffUtil.createSeparatorPanel、JBColor.java:440/443/473及DarculaColors.java修正单一换行标签颜色：浅色LF/CRLF/CR为0000ff/ff0000/ff00ff；深色为589df6/ff6464/9776a9。混合/无换行继续中性色，未把原始检测结果伪装成参考的主要换行语义。通用链接和错误提示颜色不变。

真实外壳调用实际diffFileHeader生成三个标签，在light/dark两种主题下断言六个计算颜色，全部通过；最终主题恢复、临时DOM移除。[原始结果](../artifacts/pixel-followup-2026-10-14/reference-diff-ending-colors/geometry.json)。另5项定向、JS语法、9共享资产及空白检查通过。PID26220、端口49799及profile均清理；启动沿本文命令，输出改为reference-diff-ending-colors；异常仍`taskkill /PID <AUDIT_PID> /T /F`。

mockup.js MD5由62F8FF241B2EB98D88E28A646366045E变为A2E18435D77D232EC4F13D69427668B3，CSS由8281785732B21D8695BDA4E821E859C0变为F7F72768E68D502908F7E8D5B8F7D5A2；live-data/bridge未变。当前规范未发现明确要求启用自定义Git过滤器的条款；原始读取与参考过滤后内容仍须统一设计和验证，此颜色批次不代表该语义已解决，也不代表55页/终端/冲突及阴影完成。

## 权威换行显示语义核实

后续已定位为何参考显示CRLF/LF，不能再将其视为未知或错误标签。权威源码路径均相对`D:/github/intellij-community/`：

- `plugins/git4idea/backend/resources/intellij.vcs.git.backend.xml:661`中`git.read.content.with`默认FILTERS；`backend/src/GitContentRevision.java:84`调用GitFileUtils；`backend/src/util/GitFileUtils.kt:241–270`默认使用`cat-file --filters`读取版本内容。
- `platform/diff-impl/src/com/intellij/diff/DiffContentFactoryImpl.java:830`先检测输入的换行属性，再规范化文本；`platform/util/src/com/intellij/openapi/util/text/StringUtil.java:2887`的detectSeparators取首次换行，并不报告Mixed。
- 工作区`FileDocumentContentImpl.java:46`使用LoadTextUtil；`platform/core-impl/src/com/intellij/openapi/fileEditor/impl/LoadTextUtil.java:801`选择占多数的换行符（CRLF须严格多于另外两种，否则CR多于LF选CR，否则存在LF选LF）。
- `platform/diff-impl/src/com/intellij/diff/util/DiffUtil.java:950`中CRLF为JBColor.RED、LF为JBColor.BLUE、CR为JBColor.MAGENTA；上一轮灰色标识也尚未对齐。

已对同一夹具执行只读命令并保存[字节计数与哈希](../artifacts/pixel-followup-2026-10-14/reference-line-separator-semantics.json)：原始blob有4个LF；`core.autocrlf=true`下`cat-file --filters HEAD:app.py`得到4个CRLF；磁盘有4个LF和1个CRLF。故参考CRLF/LF与上述两条内容路径一致，原始LF/Mixed也描述了另一套事实。

**上一轮实现提供原始格式，不构成参考差异页换行语义复原。** 后续必须处理比较内容与元数据的统一口径，不能只将标签伪装为CRLF/LF。尤其自定义Git过滤器可能改变内容、执行外部程序并产生额外成本，而当前差异命令明确禁用textconv；需先核对架构与比较读取边界再实施。普通查看器的磁盘混合换行规则不应因差异页参考语义而改变。本轮没有修改产品，也没有宣称完成这项。

本轮只读git进程均同步退出，没有启动窗口或服务器，没有创建临时profile；正式JSON证据保留。文档及空白检查通过，产品四网页哈希与上批相同。

## 原始换行格式信息

GitDiffDocument新增两侧DocumentLineEndings，基础设施对Ready文本读取原始blob/工作区字节并复用既有检测器；不存在、超限或读取失败保持Unknown，取消仍传播。版本比较两侧都从Git读取。桥接传递标识，主差异文件栏显示LF/CRLF/CR及混合/无换行，Unknown不猜测。新增读取每个blob最多接受10MiB完整结果，磁盘读取固定初始长度并检查是否增长；无新增依赖，但每次文本差异额外读取内容和启动cat-file，响应成本尚需量测，不能宣称性能无影响。文件历史嵌入预览尚未接入标识。

真实取证纠正早先假设：HEAD:app.py原始68字节全LF；磁盘前四行LF，末行`# changed for pixel audit`以CRLF结束。因此当前正确原始标识是LF/Mixed line endings，不能照抄参考JPEG上的CRLF/LF。首次真实断言按参考标签失败，第二次按双LF仍失败，第三次以已核验字节为依据通过，见[实际结果](../artifacts/pixel-followup-2026-10-14/reference-diff-endings3/endings.json)。PyCharm显示规则为何不同仍待权威源码核查；本项不宣称与参考同态一致。

新增真实Git测试覆盖CRLF blob与LF磁盘，差异服务10项通过；5项网页定向测试、Shell182项（桥接字段变化触发）、真实单组、C#定向格式、JS语法、共享9资产、空白检查通过。首次构建的long/int模式错误已修，最终Debug0警告/错误。PID15408/21656/17412均由finally停止、profile删除，端口63033/51429/59132随实例关闭。启动与异常停止沿本文命令，输出分别reference-diff-endings/2/3。

mockup.js MD5从1A62A3B43C88326790C57274CF42CACC变为62F8FF241B2EB98D88E28A646366045E；CSS从6F37B278B689663C4B17788CFAB0564F变为8281785732B21D8695BDA4E821E859C0；live-data/bridge哈希不变。修改另含Core模型、GitDiffService、ShellBridge、服务测试和审计驱动；没有暂存或提交。

## 连接图形高度修复

连接图层改为实际行数乘`--code-line-height`，不再由top/bottom填满整个视口。连接块原有百分比范围与变更类型保持，随行高变化。真实回归将差异区域高度分别设为240/600，两次新增行和连接图形顶部均250.000015、高度均22.000002逻辑像素；恢复后PNG已目视，连接图形不再落在窗口底部。[原始几何](../artifacts/pixel-followup-2026-10-14/reference-diff-connector/geometry.json)。此处只证明当前新增块及两种高度，不代表所有多块/滚动/字号组合已验收。

5项定向测试、真实单组、JS语法、9共享资产及空白检查通过。mockup.js MD5从63471A31D1BA57CB3E573B6CC4AA28F2变为1A62A3B43C88326790C57274CF42CACC；CSS从5C1D95FC4479C3DC8BB22D5869324890变为6F37B278B689663C4B17788CFAB0564F；live-data/bridge不变。两侧共享文件字节一致。PID10744、端口49690和profile均释放，启动沿下文命令换输出为reference-diff-connector，异常仍taskkill本次PID。CRLF/LF及完整同态证据仍待完成。

## 文件起始片段头修复

shared mockup.js现仅移除旧/新范围均从0或1开始的首个HunkHeader；中段首片段及后续片段仍保留，避免把省略上下文拼成连续文本。三个新增用例覆盖普通首片段、空侧、多片段分隔、原数组不变及变更块计数，与加载两项合计5项通过。真实app.py两侧不再出现@@行，新增行及真实行号仍保留，[修后证据](../artifacts/pixel-followup-2026-10-14/reference-diff-first-hunk/geometry.json)通过并已目视PNG。

目视另发现：变更连接图形落在正文下方。`.diff-gutter-connectors`的top/bottom铺满视口，但连接块使用文本行数计算百分比，造成定位比例错误。此项仍待修，不能因本批次断言通过忽略截图缺陷。CRLF/LF和其余全目标仍未完成。

mockup.js MD5由F3E98B56AFD51BBF8890868733058BEE变为63471A31D1BA57CB3E573B6CC4AA28F2，其余三网页哈希未变。9项共享资产、JS语法及空白检查通过。PID25352/端口60216/profile均由finally清理。启动与异常停止命令沿下文，输出改为reference-diff-first-hunk。本批次没有宿主逻辑变化。

## 首次加载示例闪现修复

后续已修正shared mockup.js：真实外壳始终进入liveDiffView；首次diff尚无数据时使用活动比较标签路径、禁用差异导航及空的左右正文，不再回落XML示例。静态视觉稿继续使用自己的示例。原有延迟加载提示时序不变，不添加虚假正文或伪造行号。

新增`diff-initial-loading.test.cjs`两项覆盖提示触发前后、活动目标路径及禁用导航。真实驱动在双击前观察正文变更，断言全部加载样本不含XML示例，且最终仍包含真实新增行；[修后记录](../artifacts/pixel-followup-2026-10-14/reference-diff-no-sample/geometry.json)通过。原先失败取证保留。两项自动化测试、JS语法、9项共享资产和空白检查通过；未修改宿主，无需重复构建。PID16040、端口56023、临时profile均释放，启动/异常停止沿本文命令。

本条仅解决示例闪现。补丁头行、换行标识和全状态同态对照尚未完成。

本批次mockup.js MD5由3EEACB9FE4AA6B29CBE0F3495E426CC5变为F3E98B56AFD51BBF8890868733058BEE；mockup.css为5C1D95FC4479C3DC8BB22D5869324890、live-data.js为73B757F728FC893773DCB3F24F622311、bridge.js为F297BF8769E249CA0D804F4F4EEEF5A9，后三者未变。共享两侧字节一致。

参考为PyCharm2026.2.1，同一reference fixture/app.py。参考JPEG及来源见[参考登记](../artifacts/pixel-followup-2026-10-14/reference-diff-observation/source.json)。实际端使用既有可见外壳驱动，新增`--reference-diff`，双击真实Changes行并等候有data-line的真实差异正文；[实际内容与行号](../artifacts/pixel-followup-2026-10-14/reference-diff-actual3/reference-diff-Light.json)及同目录PNG已保存。

## 已确认内容及差异

- 两端原文1–4行一致，实际新侧第5行为`# changed for pixel audit`，旧侧对应空位。实际行盒22逻辑像素。
- PyCharm显示左CRLF、右LF；Augit当前文件栏仅有HEAD、app.py、Workspace，没有换行标识。
- PyCharm直接从`def greet(name):`开始；Augit左右各在正文前显示`@@ -1,4 +1,5 @@`。该完整小文件不应额外占用补丁头行；不能据此删除多片段差异中必要的折叠/分隔语义，后续须按权威实现区分。
- 第一次等待任意`.diff-code-line`便采样时，实际取得静态XML示例而非app.py。第二次保留失败JSON确认此事；第三次等待真实行号数据后通过。`mockup.js`的shell在`live.diff`尚无值时回落`diffView`，这是产品加载过程缺陷，等待只是正确取证，不是修复。

本次仅实际端Light，参考为Islands Dark，窗口尺寸、面板及焦点尚未配准；参考JPEG和实际CDP PNG也不是同源无损屏幕对照。因此只能登记内容差异及加载缺陷，不能登记同态配对通过，不能把此文件替代终端/三栏冲突逐行验收。

## 验证与清理

修改审计驱动，无产品变化。前两次新增内容断言失败，第三次真实行号内容断言通过；失败证据不删。PID25380／14332／9920及子树均由驱动finally停止，临时profile删除，端口57674／51238／53284关闭。上一轮被自动审批阻止删除的`Augit-diff-pair-20261014`不属于本轮清理结果，仍保持残留登记。

```powershell
# 正常结束自动停止并清理本次profile；输出使用新的证据目录。
node tools/audit/run-pixel-remediation-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/pixel-followup-2026-10-14/reference-diff-actual3 --single --reference-fixture --reference-diff
# 异常仅停止驱动打印的本次PID。
taskkill /PID <AUDIT_PID> /T /F
```
