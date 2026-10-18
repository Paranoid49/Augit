# 2026-10-14 三栏冲突真实对照

## 中央结果与空侧边界

结果高亮现根据草稿实时字符范围计算，不沿用加载时的旧行号；两侧都处理后结果区绘制上下点线，撤销恢复未处理底色。空侧借后继真实行的伪元素绘制双边界，已处理改点线，不创建可编辑占位文本。此规则沿前述DiffDrawUtil空范围双线与RESOLVED模式；文件末尾无换行而没有后继行的边界仍需补验，不能宣称所有空侧已完成。

真实单块完整回归通过中央处理/撤销状态、外部刷新、组合输入及保存，证据merge-result-border。新增真实Git“左侧删除、右侧修改”夹具，接受空侧并忽略右侧后结果范围为空，已处理边界存在，保存精确等于两行原文、无占位空行，索引已解决，见[空侧结果](../artifacts/pixel-followup-2026-10-14/merge-empty-border/empty.json)。9共享资产、JS语法与空白检查通过。边界精确像素、字号矩阵仍未验收。

修改live-data、两份共享CSS及真实驱动。PID10916/25004及临时仓库/profile由finally清理，独立查询确认PID不存在、最终临时根不存在；未暂存提交。启动沿真实驱动，空侧场景在输出目录和Playwright参数后加--empty，异常仅taskkill本次AUDIT_PID。整体整改未完成。

## 已处理来源范围边界

按权威DiffDrawUtil.java:584–630、PaintMode.RESOLVED的NONE背景/DOTTED边界规则，为左右非空已处理块补上下点线；按块范围仅标记首末行，不在块内每行重复画。绝对定位伪元素不改变行高或正文，撤销同步移除已处理边界。浅/深边界使用DIFF_CONFLICT背景色#ffd5cc/#45302b。空侧边界、中央结果范围和精确点阵与参考配对仍待完成，不宣称整个高亮族完成。

真实回归确认接受侧点线、行高22、文本不变，以及外部刷新、编辑撤销、组合输入和精确保存。首次断言假定CSS边框计算值为1px而失败；DPR1.75下实际为0.571429px，修正为允许浏览器在不超过1逻辑像素内量化，不改产品样式迁就测试。最终merge-resolved-border-final完整通过，9共享资产、JS语法和空白检查通过。

live-data MD5由A699DC934796211F71D749FAAE54C00B变为2FB4AA712F62529B1F569041E7BA14AE，CSS由7CDC655B0AFCA85E50C217F234A4C361变为79F641D9390EF3E2775D8EE3F122B168；mockup.js/bridge未变。修改两份共享CSS、live-data及真实驱动。PID21964/11752均退出，各临时仓库/profile由finally清理，最终临时根不存在。启动沿真实驱动输出merge-resolved-border-final，异常仅taskkill本次AUDIT_PID；未暂存提交。

## 来源高亮跟随处理状态

此前中央计数归零后来源两侧仍显示未处理底色。新增真实断言在修前失败（接受左侧后仍有1行conflict-side），修后按每侧leftHandled/rightHandled及宿主行范围同步高亮；重叠范围中只要仍有未处理块就保留底色，撤销按恢复的状态重新绘制，不改来源文本。

真实单块完整回归通过：接受左侧清除左底色、右侧保留，忽略右侧后两侧清除，撤销恢复右侧；外部刷新、编辑、组合输入和保存回归通过。真实两块不等长对齐及精确保存、6项模型、JS语法与空白检查通过。证据在merge-highlight-before/after/multi目录，修前失败保留。此项只证明处理状态正确，处理后虚线边界、颜色及空侧标记仍需精确参考配对。

live-data MD5从9E546367C4390790BB9E3D6DFFB1042E变为A699DC934796211F71D749FAAE54C00B，其他网页未变；另改真实驱动。PID20908/18876/5464及各自profile/仓库由finally释放。启动沿真实驱动，输出merge-highlight-after；多块追加Playwright路径及--multi。异常只停止本次AUDIT_PID，正常自动退出。未暂存提交，整体未完成。

## 不等长多块显示对齐

新增alignConflictDraftRows，在每块三侧起止边界处用行前margin补齐高度；不增加DOM文本或可编辑占位行，复制/保存继续按真实正文读取。几何使用相对滚动容器的内容坐标，计数同步、接受/忽略和撤销时重算。当前实现逐边界量测，极大文件的布局成本尚待优化；全文任意编辑导致来源对应关系变化的场景未全覆盖。

真实Git夹具增加两块：第一块左侧多一行，第二块右侧多一行，中间12行公共正文。初始、接受首块、接受第二块后，context 0及last line三栏顶部差小于0.1逻辑像素；保存精确等于草稿且索引无未合并项。[最终几何](../artifacts/pixel-followup-2026-10-14/merge-multiblock-final/multi.json)及同目录PNG已生成，前一轮PNG已目视公共段对齐。单块全链路回归、6项模型、JS语法和空白检查通过。行号、连接动作、处理后来源高亮与配色尚未复原，不把两块夹具当全量验收。

live-data MD5由E6A27B2A53D287CBB7D79AD0ABEE7846变为9E546367C4390790BB9E3D6DFFB1042E，其他三网页未变。修改另含真实驱动--multi分支。PID6812/15796/1912与对应临时仓库/profile由finally清理，未暂存提交。

```powershell
node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-multiblock-final C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright --multi
# 正常自动退出，异常只停止本次驱动输出的PID。
taskkill /PID <AUDIT_PID> /T /F
```

## 保留冲突模态窗口身份

已修复后台会话刷新重复创建干净解决器的问题：当前冲突文档对象未变时保留正文与窗口；外部Reload或干净自动刷新只替换dialog-body，保留原有dialog节点和宿主模态身份，重新绑定新正文的编辑/保存行为。避免每次重载都重新触发整窗隐藏捕获及输入焦点丢失。此项与此前异步文档身份检查、监视器启动修复共同验证。

两轮真实外部修改回归均完整通过：merge-retained-window及[merge-retained-verified](../artifacts/pixel-followup-2026-10-14/merge-retained-verified/interaction.json)。新增节点身份断言确认Reload和干净自动刷新没有替换模态窗口；Keep保留编辑器节点、文本、选区、事务游标。随后接受/忽略、撤销/重做、跨行替换及选区恢复、组合输入协议、精确保存和索引解决均通过。6项模型、JS语法和空白检查通过。本结果不外推所有模态或前景逐帧无闪隐，原生IME候选窗仍未验证。

live-data MD5由3741CB7A4B17033AE12CD10E29D22073变为E6A27B2A53D287CBB7D79AD0ABEE7846，其他三网页未变，修改另含真实驱动。PID5564/11052、端口52983/55282、临时根Augit-merge-pair-uXxXdy/PvqMeM均由finally释放，独立查询确认进程、监听和目录均不存在。未暂存提交。

复验启动：`node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-retained-verified`；正常自动退出，异常只执行 `taskkill /PID <AUDIT_PID> /T /F`。此前失败记录保留用于追溯，不改写为通过。

## 外部刷新失败诊断与模态焦点

新增失败现场JSON保存DOM正文、innerText、草稿、选区、活动元素、祖先计算样式和宿主modal-state。[诊断](../artifacts/pixel-followup-2026-10-14/merge-external-diagnostic/failure.json)显示正文及草稿未丢失，活动元素却落到body，输入没有进入结果区；新模态捕获的隐藏阶段使即时innerText断言可能为空。捕获结束新增恢复仍连接且属于当前顶层窗口的焦点/冲突选区，并避免覆盖用户已经移到其他控件的焦点。真实驱动在Reload后等捕获完成再读可见正文。

本批次仍未稳定通过：merge-focus-final又在第一次Ctrl+Z后计数仍为0而失败，不能宣称焦点问题彻底解决。新现场在[最终失败记录](../artifacts/pixel-followup-2026-10-14/merge-focus-final/failure.json)，继续定位宿主焦点与截图遮盖交接；等待DOM捕获标记消失不等于输入路由已恢复。只通过JS语法/空白检查，未将失败回归登记为通过。

live-data MD5由58A75AB9DFF567B4CC51F64958A98454变为3741CB7A4B17033AE12CD10E29D22073，其他网页未变。PID9288/20596/16124均已退出，驱动finally删除本次临时仓库/profile。复验使用真实驱动并指定独立输出目录；异常仅taskkill本次AUDIT_PID。整体目标保持未完成。

## 外部刷新接续：尚未稳定

真实驱动新增磁盘修改场景：未保存草稿选择Keep后正文节点、草稿文本、事务游标和选区保持；第二次磁盘修改选择Reload后接纳新正文并清空草稿历史；干净草稿再次磁盘修改自动接纳。首次在启动后立即写盘漏掉通知，监视器原来直到首次2秒兜底查询才创建，现订阅后立即drainWorkspaceChanges建立监视。另给冲突异步读取加文档对象身份检查，避免旧请求覆盖重载后的新文档。

一次完整运行通过，但复跑在后续撤销/重做断言出现innerText为空，尚未确认是背景捕获暂时隐藏还是编辑状态错误。不能将本批次登记为稳定通过，也不能以等待一次通过掩盖故障。证据分别在merge-external-final、merge-external-verified和merge-external-settled目录；verified保存[Keep前后状态](../artifacts/pixel-followup-2026-10-14/merge-external-verified/external-refresh.json)，没有最终interaction结果即表示未跑完。下一步应在失败时同时捕获DOM文本、计算可见性、选区、草稿和模态快照状态以定位。

修改live-data和真实驱动，live-data MD5为58A75AB9DFF567B4CC51F64958A98454，其他网页未变；JS语法和空白检查通过，不代替实时验收。PID25416/3792/24504/26184/3960均已退出，各驱动finally报告临时仓库/profile删除。没有常驻服务器或提交。启动沿真实驱动、输出使用独立目录；异常只taskkill本次AUDIT_PID。整体目标保持未完成。

## 跨行选区与组合输入撤销

真实WebView2键盘跨行选择、替换、撤销后，原正文和跨行选区恢复通过。CDP的Input.imeSetComposition先输入zhong、再改为中文、Input.insertText提交：修前按一次Ctrl+Z会退回zhong，明确失败。现以compositionstart/end合并组合期间的文本/块状态补丁为单个历史事务；组词期间不拦截撤销快捷键，提交后一次撤销回到组合前，一次重做恢复中文。仅在组合期间暂存原正文，结束释放，历史仍保存差异片段。

修后真实回归通过，见[交互结果](../artifacts/pixel-followup-2026-10-14/merge-composition-final/interaction.json)。此证据属于WebView2组合输入协议，**不等同Windows原生输入法候选窗实机验收**，nativeWindowsImeVerified保持false。跨行测试覆盖一个已处理块后的正文，不外推任意未处理多块。6项模型、JS语法、空白检查通过；精确保存和Git索引解决回归通过。

live-data MD5由1BDEEFB45D0C2E300315DD426DD9DB48变为2DE537A0776A94E196937BAD6F0D3B59，其余三网页未变；修改另含真实审计驱动。修前PID13272、修后6516/25112及临时仓库/profile均由finally清理，独立检查PID不存在、三端口无监听、最终临时根不存在。启动命令为 `node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-composition-final`；异常仅 `taskkill /PID <AUDIT_PID> /T /F`。没有暂存提交，整体目标未完成。

## 草稿撤销选区恢复

草稿事务现记录编辑前后逻辑字符选区，接受动作把光标放在接受文本末端；撤销/重做重建结果行后按字符偏移恢复选区方向，不继续引用失效DOM节点。键盘与beforeinput的历史入口统一使用同一回放逻辑，保存中不回放，历史动作正确登记未保存状态。

真实键盘回归验证：重做后继续输入落在原插入点；选择首行两个字符后替换，再撤销，原文和两字符选区同时恢复。原有接受/忽略、计数、两次撤销/重做、实际保存精确字节与索引解决仍通过。见[真实取证目录](../artifacts/pixel-followup-2026-10-14/merge-selection)。6项模型、JS语法及空白检查通过。多行跨块选区、反向选区、IME组合输入仍未覆盖，不宣称完整编辑器验收。

修改live-data及真实驱动，live-data MD5由5AC76F03F06C09098B6C232374107F38变为1BDEEFB45D0C2E300315DD426DD9DB48，其余三网页哈希未变。PID14116/15504、端口51582/52145和对应临时仓库/profile均释放；独立查询确认两个PID不存在、端口无监听、目录不存在。启动沿真实驱动命令、输出改为merge-selection，异常仅taskkill本次AUDIT_PID。未暂存提交。

## 真实编辑器接入草稿

live-data与共享渲染器已接入独立草稿：祖先可可靠恢复时，中央初始显示祖先片段，未处理计数独立于Git标记；未知祖先暂沿原有带标记路径，不隐藏不确定内容。接受左侧后仍需处理右侧，新增忽略两侧动作；处理过的一侧按钮禁用。未处理块尚存时Apply显示局部说明并拒绝保存。接受/忽略及正文编辑进入草稿事务历史，Ctrl+Z/Ctrl+Y恢复文本与处理状态；保存仍走既有磁盘版本校验和Git标记解决。

真实单块夹具中三栏last line顶部已一致，原88逻辑像素错位消除。[初始PNG](../artifacts/pixel-followup-2026-10-14/merge-draft-ui-edit/initial.png)已目视，祖先base value可见；[交互结果](../artifacts/pixel-followup-2026-10-14/merge-draft-ui-edit/interaction.json)验证实际按钮接受左侧/忽略右侧、两次撤销/重做、手动插入文本及撤销/重做、最终磁盘精确字节和Git索引无未合并项。不是全三栏验收：不等长多块、原生IME组词、跨块任意编辑、撤销后光标恢复、外部刷新完整场景仍须补验；当前接受后重建结果行，光标细节尚未对齐。行号、连接动作与配色也未完成。

6项模型测试、真实交互、JS语法、9共享资产和空白检查通过。mockup.js MD5从9FB7D02E3D4442700E4F10910161CCDE变为169599C899766FBF8CA693AB934E2487，live-data从73B757F728FC893773DCB3F24F622311变为5AC76F03F06C09098B6C232374107F38；CSS与bridge未变。修改两份共享JS、live-data和真实审计驱动。三次驱动PID18352/25748/12720与各自临时仓库/profile由finally清理，独立查询确认PID均退出，最终临时根不存在。无常驻服务器，未暂存提交。

复验：`node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-draft-ui-edit`；异常停止沿下方`taskkill /PID <AUDIT_PID> /T /F`，正常自动退出。本批次只验证单块真实场景，不将其换算为55页配对完成。

## 独立草稿模型与真实数据验证

新增 `web/src/conflict-draft.js`，以祖先片段替换工作区中的未处理块，其余已解决正文和原始换行符保留。块独立记录左右处理/接受状态，接受左侧后计数仍为1，忽略右侧后归零；原始字符偏移编辑、接受和忽略共用事务历史，撤销/重做同时恢复文本及块状态。历史保存被替换片段和块元数据，不复制完整文件。相邻空祖先块共享字符位置时按原块顺序移动锚点，逆序接受不会交换文件内容。

6项模型测试覆盖初始投影、未知祖先拒绝、双侧处理计数、状态撤销、左右拼接、重复动作、相邻空块正逆序、正文编辑与重做分支清除。真实审计外壳使用实际宿主返回的Git冲突数据加载模块，验证祖先初始正文、接受左侧、忽略右侧及两次撤销，见[草稿结果](../artifacts/pixel-followup-2026-10-14/merge-draft/draft.json)。**模型尚未替换可见编辑器**，同目录截图仍含Git标记，不能宣称界面已修。输入法、光标、保存和外部刷新接入仍需完成。

Debug构建0警告/错误，JS语法和空白检查通过。四个既有网页哈希未变，新模块没有新增依赖。真实审计PID13460、端口57542及临时profile/仓库 `Augit-merge-pair-Xa6d3i` 已由finally释放，独立查询确认进程不存在、无监听、目录不存在。

```powershell
node --test tools/audit/conflict-draft.test.cjs
node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-draft
# 异常中断仅停止驱动打印的本次PID；正常结束自动清理。
taskkill /PID <AUDIT_PID> /T /F
```

## 逐块祖先恢复

后续基础设施用三份索引正文执行只输出到内存的 `git merge-file --stdout --diff3`，恢复普通merge标记中缺失的祖先片段。临时输入保留原始结束符，输出限制约四份正文，30秒超时；没有改写仓库文件。生成结果复用完整Git编辑脚本定位两侧范围，以范围身份索引候选，仅两侧规范化正文也一致且唯一匹配时补入块AncestorText。已有diff3祖先保留；失败、输出超限、外部改写而无法匹配时保持未知，不猜测。该路径增加一次merge-file及必要的范围查询，性能尚待量测。

真实Git回归覆盖普通标记恢复base、双方新增的空祖先、空侧/重复上下文、LF/CRLF、接受首块后重载仍找到第二块祖先，以及外部改写冲突侧正文时不强行配对。服务18项通过（最终约8秒），桥接4项通过（约3秒），包含实际块ancestor字段断言；定向C#格式及空白检查通过。祖先临时目录在finally删除，测试进程与本批次测试仓库已退出/清理。四网页哈希与上一批一致；本批次只改冲突服务及两份测试，没有界面变化，三栏错位仍未修复。

此处是前端不含标记的草稿所需数据准备，尚未实现独立未处理状态、撤销同步和行对齐，不能计为冲突解决器验收完成。测试启动沿下方命令；未启动GUI或服务器，无额外停止命令。

## 祖先数据接入批次

GitConflictDocument增加可空AncestorText，加载真实索引stage1并经Shell桥接提供给网页。存在祖先时沿既有10MiB上限和UTF-8分类读取；无stage1为明确空串，不可安全读取为null，不从普通冲突标记猜测祖先。仅文本冲突增加读取，每次最多增加一次大小查询和一次blob读取；额外延迟尚未量测。工作区ResultText、磁盘版本、块偏移和保存行为保持原样，尚未启用新的前端草稿，不能宣称三栏错位已修。

新增真实Git测试覆盖普通merge标记不含祖先时仍读取stage1、外部工具改写并移除冲突标记后重载保留其结果，以及双方新增冲突的空祖先；桥接测试断言实际祖先字段。冲突服务18项通过（约7秒），桥接定向4项通过（约3秒），因桥接公共数据变化执行Shell完整182项通过（约13秒）。受影响C#格式和空白检查通过。测试进程已退出；测试临时目录由using释放，结束查询未发现本批次新建目录残留。未启动GUI或服务器。

修改文件为Core冲突模型、Infrastructure冲突服务、ShellBridge及两份相关测试。四网页MD5未变：mockup.js为9FB7D02E3D4442700E4F10910161CCDE，CSS为7CDC655B0AFCA85E50C217F234A4C361，live-data为73B757F728FC893773DCB3F24F622311，bridge为F297BF8769E249CA0D804F4F4EEEF5A9；混合改动未暂存提交。

```powershell
dotnet test tests/Augit.Infrastructure.Tests --no-restore --filter FullyQualifiedName~GitConflictServiceTests --verbosity quiet
dotnet test tests/Augit.Shell.Tests --no-restore --filter FullyQualifiedName~ShellBridgeConflictAcceptTests --verbosity quiet
dotnet test tests/Augit.Shell.Tests --no-build --no-restore --verbosity quiet
```

本批次新增PyCharm2026.2.1独立合并窗口和Augit真实Git冲突取证，补上此前只有合成用例、没有真实参考的问题。尚未完成双端同态配准，不计55页配对通过。

## 参考与实际内容

参考使用权威 `platform/diff-impl/src/com/intellij/diff/applications/MergeApplication.kt` 的 `merge ours theirs base result` 入口。四份输入保存在[参考目录](../artifacts/pixel-followup-2026-10-14/merge-reference)，内容均为三行加末尾LF：首行 `first line`、末行 `last line`，中间分别为 `base value`、`ours value`、`theirs value`。result初始与base一致；未执行Apply，结束后SHA256仍与base一致。独立入口验证三栏渲染行为，不替代Git冲突列表入口的完整验收。

| 行/状态 | PyCharm真实窗口 | Augit真实外壳 |
| --- | --- | --- |
| 初始第1行 | 三栏均first line | 三栏均first line |
| 初始第2行 | ours value / base value / theirs value | ours value / Git起始标记 / theirs value |
| 初始末行 | 三栏第3行均last line | 左右第3行、中央第7行 |
| 初始冲突块 | 三栏均一行，带行号与两侧块动作 | 左右一行，中央五行，缺少行号和连接动作 |
| 接受左侧块 | 中央改ours value，计数仍为1 | 本批次尚未取证此状态 |
| 随后忽略右侧块 | All conflicts resolved，出现完成提示 | 本批次尚未取证此状态 |

参考截图为接口原始JPEG，989×694，不是无损屏幕PNG：[初始](../artifacts/pixel-followup-2026-10-14/merge-reference/initial.jpg)、[接受左侧](../artifacts/pixel-followup-2026-10-14/merge-reference/after-left.jpg)、[已处理](../artifacts/pixel-followup-2026-10-14/merge-reference/resolved.jpg)。每次点击后立即截图仍可能显示旧内容，第二次独立观察确认正文变化后才归档。

实际使用临时真实Git仓库：base提交后分出topic，两分支分别修改第二行，再merge产生真实冲突；没有改动用户仓库或参考输入。通过实际冲突列表进入解决器，宿主返回三侧内容、范围和磁盘版本，未注入模拟桥接应答。见[原始JSON](../artifacts/pixel-followup-2026-10-14/merge-actual/initial.json)及[真实WebView2 PNG](../artifacts/pixel-followup-2026-10-14/merge-actual/initial.png)。DPR1.75，行高22逻辑像素；左右last line顶部292.2143，中央380.2143，相差88逻辑像素，即4行。当前三栏没有满足UX §7.14的纵向对齐约束。

## 整改方向

不能只隐藏Git标记的DOM行：当前接受、撤销、保存、外部刷新和未处理计数都依赖带标记的结果文本与偏移。需要让编辑草稿和未处理块状态独立，读取索引stage1祖先并建立三侧映射；保留用户已在工作区解决的内容及版本校验，不能用祖先文本覆盖已解决区域。再完成不同长度块的显示对齐、真实行号、块动作和处理状态回归。

颜色也未对齐：当前左右使用深色#523234、中央#2b3f59；权威DIFF_CONFLICT深色#45302b、浅色#ffd5cc，参考还区分正文弱化与行内片段，不应直接把整块替换成同一实色。本次记录证据，未以局部颜色修复代替冲突模型整改。

## 验证与资源清理

新增 `tools/audit/capture-conflict-reference-shell.cjs`，自动断言真实merge退出1、磁盘存在冲突标记、实际三栏左右第二行正确，并保存三侧正文/几何。真实运行通过，但其通过表示取证成功，不表示还原通过。产品文件及四网页MD5未变；没有暂存或提交。

首次隐藏参考PID19092没有可见窗口，停止后改用可见交互窗口PID18504，完成取证后停止。实际外壳PID26480、端口50091、临时根 `C:/Users/wl024/AppData/Local/Temp/Augit-merge-pair-gHn9SO` 均由finally释放；独立查询确认三个PID均不存在、端口无监听、临时根不存在。正式文本夹具与截图是复验证据，保留。历史审批拒绝清理项仍见字体接续记录。

```powershell
# 实际端：自动创建并清理隔离仓库、profile和本次进程树。
node tools/audit/capture-conflict-reference-shell.cjs
# 异常中断只停止脚本输出的本次pid。
taskkill /PID <AUDIT_PID> /T /F
# 参考端：可见交互窗口；只使用已保存的审计输入。
$conflictOldProperties = $env:PYCHARM_PROPERTIES
try {
  $env:PYCHARM_PROPERTIES = 'D:/JetBrains/PyCharm-2026.2.1-reference/reference.properties'
  $conflictEvidence = 'D:/github/Augit/artifacts/pixel-followup-2026-10-14/merge-reference'
  $conflictReference = Start-Process 'D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe' -ArgumentList @('merge', "$conflictEvidence/ours.txt", "$conflictEvidence/theirs.txt", "$conflictEvidence/base.txt", "$conflictEvidence/result.txt") -PassThru
} finally { $env:PYCHARM_PROPERTIES = $conflictOldProperties }
taskkill /PID $conflictReference.Id /T /F
```

终端、完整同态截图、背景刷新、阴影继续待验收。Windows10排除本次验证、跨屏DPI保留未验证；整体目标未完成。

## 末尾无换行正文恢复（2026-10-14）

真实EOF夹具显示：Git把前一行也纳入冲突，因此不是空范围；驱动原来的空侧断言不成立。另发现Git为冲突分隔标记补上的换行进入草稿。GitConflictService现在仅在规范三方合并末块、范围身份及两侧正文匹配、索引原文后缀精确匹配时去掉补入的结束符；保留不匹配的外部编辑祖先。原始工作区ResultText不改。普通merge及diff3两种新增服务用例通过。桥接旧断言原先期待补入换行，现按真实索引无换行正文校正，并断言左右正文。

验证：冲突服务20项（8秒）、桥接4项（3秒）、草稿6项通过；Shell Debug构建0警告0错误（3.31秒），9共享资产、JS语法、git diff --check通过。真实EOF接受当前侧并忽略右侧后保存字节精确相等，索引已解决，见merge-empty-eof-fixed/empty.json；普通单块完整回归merge-eof-regression通过。本批不证明EOF接受双方、全部空范围边界或像素配对完成，这些仍需继续。

修改GitConflictService、服务及桥接测试、真实驱动。四网页MD5修前后未变：mockup.js=169599C899766FBF8CA693AB934E2487，mockup.css=5A6BDCD3146E3497A9711EB831E031B1，live-data.js=E51BC59B403B226CBE7FC7283B48189A，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。分支dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6，既有混合改动保留，未暂存提交。

启动：`node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-empty-eof-fixed C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright --empty --eof`。普通回归去掉两个末尾开关并更换输出目录。驱动finally自动停止本次实例并删除临时仓库/profile；异常手动停止：`taskkill /PID <驱动输出的本次PID> /T /F`。本批PID5200/11760、端口61965/53096及临时目录CnX0vw/csig2Y已清理，历史清理例外沿接续记录保留。
## EOF接受双方行边界（2026-10-14）

继续上一批：新增失败测试复现无末尾换行的双方被直接拼成同一行。权威MergeModelBase.kt:303的appendChange经DiffUtil.java:1348以行插入。草稿现用joinSides补两段间边界，优先沿左/右现有结束符，均无结束符时采用LF；不添加文件末尾换行，空侧不生成占位空行。直接Both、先左后右、先右后左及撤销重做均覆盖；CRLF、CR、已有LF、空侧另有回归。尚未取得该状态的PyCharm双端同态截图，不宣称像素一致。

修改web/src/conflict-draft.js、模型测试和真实驱动。8项模型测试通过（119毫秒）；真实--empty --eof --both保存精确等于两侧按行拼接，Git索引已解决，证据artifacts/pixel-followup-2026-10-14/merge-eof-both/empty.json及empty.png。JS语法、9共享资产、git diff --check通过。四主要网页哈希沿上一批保持不变，未改共享视觉资产。未暂存提交。

启动：`node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-eof-both C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright --empty --eof --both`。自动finally清理，异常手动命令`taskkill /PID <本次PID> /T /F`；本次PID13280、端口61514、临时目录Augit-merge-pair-8gtSIw，确认进程及目录不存在。55页配对、背景刷新、终端与系统阴影仍未全部验收，跨屏保留未验证、Win10排除本次验收。
## 冲突基础色统一（2026-10-14）

依据DefaultColorSchemesManager.xml:489/2255的DIFF_CONFLICT及DiffViewerHighlighters.kt:111–126，三侧未处理冲突共用浅色#FFD5CC、深色#45302B；去掉当前来源偏红/中央蓝色的自定义组合。同步两份CSS与设计系统旧声明。参考在有innerFragments时以TextDiffTypeFactory.getIgnoredColor（混入背景0.6）减淡行底，并保留片段原色；此层尚未实现，因此当前只能登记基础色修正，不能宣称参考截图相同。既有参考initial.jpg只作观察，不是无损配准证据。

真实驱动新增--light及三栏计算色断言。merge-color-dark和merge-color-light完整回归均通过，涵盖初始三栏色、处理后底色消失、撤销恢复、外部刷新、组合输入与精确保存；JS语法、9共享资产、git diff --check通过。本批仅CSS令牌变化，无新增产品可执行逻辑，未额外运行全量构建。CSS MD5从5A6BDCD3146E3497A9711EB831E031B1变为AE285BA8C213E03F952B1AA726DBC9A9，其他三主要网页哈希不变。未暂存提交。

启动：`node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-color-light C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright --light`；深色移除--light并换输出目录。finally自动关闭本次实例及清理仓库/profile，异常手动：`taskkill /PID <本次PID> /T /F`。PID23660/26144，端口61678/59751，临时目录Augit-merge-pair-1RHpSO/aDFauk已删除、进程已退出。行内分层、行号、逐块动作和同态截图仍未完成。
## 参考高亮模式实机确认（2026-10-14）

重新启动隔离PyCharm2026.2.1参考合并，设置菜单实机勾选Highlighting Differences→Words、Ignore Differences→None、Synchronize Scrolling。三栏行号与逐块接受/忽略位于正文旁；默认Words减淡整行、突出差异词，当前Augit仅基础色仍不匹配。尝试Lines后没有取得勾选或画面确认，不能登记切换成功；已Escape关闭菜单，未Apply，result.txt仍为三行祖先正文。

截图接口继续返回image/jpeg（989×694），API无格式参数；保存原格式到artifacts/pixel-followup-2026-10-14/merge-reference-mode/words.jpg及source.json。不能改名成PNG或计入55页合格配对。下一步产品整改须覆盖默认Words分层及行号/逐块操作，不能通过把参考改为Lines来替代默认状态复原。此次只新增证据/记录，产品文件未修改，无额外测试服务器。

启动沿前述隔离PYCHARM_PROPERTIES及pycharm64.exe merge四文件命令，本次PID13236，已执行`taskkill /PID 13236 /T /F`成功停止；没有更改用户原有IDE实例。git diff --check通过。
## 三栏独立行号（2026-10-14）

按实机参考及ThreesideMergeHighlighters/Editor行号结构，增加正文之外的aria-hidden行号显示层：左来源右置，中央及右来源左置。根据真实行盒坐标更新位置，编号不计虚拟留白；正文仍保持原有span及纯文本编辑，保存不读取行号。修改mockup.js/CSS两侧共享文件、live-data同步入口、真实驱动及设计文档旧隐藏行号声明。当前槽宽为字体ch度量适配，尚未取得精确参考几何；逐块动作、连接区、槽内冲突着色和Words分层仍缺。当前滚动同步重建行号节点，大文件性能未测，需继续优化，不宣称全量完成。

真实merge-line-numbers完整回归及merge-line-numbers-multi通过：每栏编号完整、行顶差小于0.1逻辑像素、行号在正文之外，编辑/撤销/外部刷新/组合输入/精确保存继续通过；多块不等长公共段对齐通过。初始PNG已目视行号可见且不遮挡短行正文。JS语法、9共享资产、git diff --check通过。未跑全量测试，未暂存提交。

MD5：mockup.js从169599C899766FBF8CA693AB934E2487到08247AD2A3EC6F8EB7945C904C2B91DC；CSS从AE285BA8C213E03F952B1AA726DBC9A9到D3AB0BD937472E218D014FCAE8B0A81C；live-data从E51BC59B403B226CBE7FC7283B48189A到4F2A9833BFA365CD529EF6017A1583BF；bridge保持F297BF8769E249CA0D804F4F4EEEF5A9。

启动沿capture-conflict-reference-shell.cjs，输出目录分别merge-line-numbers和merge-line-numbers-multi，后者在Playwright参数后追加--multi。异常停止`taskkill /PID <本次PID> /T /F`，正常finally自动关闭清理。PID23384/22620，端口55864/58793，临时目录Augit-merge-pair-xlYDkD/DhIOCQ均已回收。
## 行号滚动复用（2026-10-14）

上一批每次scroll重建所有行号，现在把行号坐标保存到独立track内，滚动只更新track的translateY；内容、字体或几何变化仍重新度量。横向滚动不移动行号层。不声称大文件整体性能达标：内容变化仍逐行量测，后续需虚拟化/批量读取评估。

真实多块驱动增加受控100px视口、2000px行宽的滚动压力状态，保持正文文本不变。merge-number-scroll-final/multi.json记录纵向66px、横向120px滚动，节点保持复用、行号横坐标固定、行顶偏差小于0.1px；恢复尺寸后两块接受、公共行对齐、精确保存及Git索引解决通过。JS语法、9共享资产、git diff --check通过。此为实际WebView2受控几何验证，不是PyCharm同态截图或长文件性能基准。

修改两份mockup.js/CSS及真实驱动。mockup.js MD5从08247AD2A3EC6F8EB7945C904C2B91DC变为0C63DAB903BB496AD4C7C6D0F4704756；CSS从D3AB0BD937472E218D014FCAE8B0A81C变为62FF35BC6F93860ABBDC4EAD06FBA820；live-data/bridge未变。未暂存提交。

启动：`node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-number-scroll-final C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright --multi`。finally清理，异常停止`taskkill /PID <本次PID> /T /F`。两次PID20656/25732，端口61993/64336，临时目录Augit-merge-pair-Mgx7kj/dRUXfV均已删除，进程不存在。整体整改仍未完成。
## 逐块接受与忽略入口（2026-10-14）

三栏来源行号层新增按稳定块ID定位的接受/忽略按钮，操作直接作用于该块，不依赖中央当前块选择；处理该侧后入口消失，撤销恢复，应用期间禁用。按钮置于正文外，未混入保存文本。参考ThreesideMergeHighlighters.createAcceptOperation与实机词级视图。当前使用文字等效箭头/叉号，精确图标、槽宽、EOF空侧锚点与键盘导航仍未验收，不宣称视觉完成。

真实两块夹具先滚动正文到第二块，点击该块接受左侧，确认第一块未处理、第二块仅左侧处理；撤销恢复两个入口。随后纵横滚动行号复用/对齐、两块处理及精确保存通过，证据merge-gutter-final/multi.json。此前失败揭示overflow:hidden行号层会被按钮聚焦自动滚动，改overflow:clip避免独立滚动；测试改为真实滚动正文后点击，未强制穿透遮挡。JS语法、9共享资产、git diff --check通过。

修改共享mockup.js/CSS、live-data及真实驱动。正常finally回收全部本批启动的实例/临时仓库/profile；最终PID1104、端口65277、临时目录Augit-merge-pair-ECEjDR已清理。失败尝试PID4496/6536/22760/24268/26516也均由finally清理。启动沿capture-conflict-reference-shell.cjs输出merge-gutter-final，Playwright参数后加--multi；异常停止`taskkill /PID <本次PID> /T /F`。未暂存提交，整体整改未完成。
## 逐块键盘焦点可见性（2026-10-14）

逐块按钮聚焦时按对应正文行滚动正文容器，保持行号层不独立滚动；在上下边界留1逻辑像素容纳DPI量化。修复行号坐标在布局写入后沿用旧正文顶边导致的175%缩放偏移，使用同一时点正文坐标计算行位置。未改变正文或保存语义。

真实多块测试通过程序聚焦视口外第二块按钮、确认可见并发生正文滚动，再实际Enter接受，第一块保持未处理；Ctrl+Z恢复入口。后续66px纵滚/120px横滚、节点复用、行号对齐和两块精确保存通过，见merge-gutter-keyboard-fixed/multi.json。此测试覆盖focus/Enter路径，不等同完整Tab顺序及辅助技术验收。JS语法、9共享资产、git diff --check通过。

修改两份mockup.js及真实驱动。失败尝试均finally清理；最终PID16928、端口61917、临时目录Augit-merge-pair-buQgXi已回收。启动沿capture-conflict-reference-shell.cjs，输出merge-gutter-keyboard-fixed，Playwright参数后加--multi；异常停止`taskkill /PID <本次PID> /T /F`。未暂存提交，整体目标仍未完成。
## 文件尾删除与整侧为空实测（2026-10-14）

增加真实Git末尾删除(--empty --end-delete)与整侧空文件(--empty --whole-empty)两种夹具，两者均通过行旁接受当前侧、忽略右侧，处理状态和空范围边界存在，保存分别精确为first line加原有LF、零字节文件，Git索引均已解决。没有为了显示入口增加保存占位文本。证据merge-end-delete及merge-whole-empty，后者empty.json明确登记gutterActions与wholeEmpty。现有末尾空行可定位，无须新增产品回退锚点；没有证据的特殊外部编辑范围仍不宣称覆盖。

本批只扩展真实驱动，产品哈希保持上一批；JS语法、git diff --check通过。两次PID15652/15952，端口56700/53518，临时目录Augit-merge-pair-fcwm3e/SY5ImB均finally回收并查询不存在。启动沿capture-conflict-reference-shell.cjs，输出上述目录，Playwright参数后分别加--empty --end-delete或--empty --whole-empty；异常停止`taskkill /PID <本次PID> /T /F`。未暂存提交，55页同态配对及其他整体目标仍未完成。
## 三栏词级范围计算准备（2026-10-14）

核对DiffUtil.java:1129–1187：三侧存在时以中央当前结果分别ByWord比较左右，中央取两组范围；缺一侧时比较另外两侧，只有一侧则无行内层。新增Core GitMergeWordDiff复用既有GitWordDiff，中央范围合并重叠/相邻，null与空字符串区分。4项测试覆盖差异词、中央两处独立变化、空侧/缺失侧、中文表情及CRLF一致正文，47ms通过。

当前只是可测试的计算层，尚未接入bridge/界面，不宣称Words高亮已实现；既有GitWordDiff的256-token上限和中段回退仍存在，与IntelliJ ByWord复杂分词/对齐尚未全面对照。下一步接入实时当前结果时必须防止旧异步范围覆盖新编辑，且不破坏选区、IME、撤销。没有修改四主要网页或启动应用服务器，未暂存提交，整体目标未完成。
## 词级比较只读桥接（2026-10-14）

新增git/conflict-words，三个必填字符串yours/result/theirs均为当前片段，单侧上限65536个UTF-16代码单元，返回三侧start/length数组；不解析路径、不访问Git、不写工作区。缺失和超限走错误通道，计算前后检查取消。复用Core GitMergeWordDiff，不在网页复制另一套词级算法。超限片段后续页面必须保留整行高亮，不以截断文本伪造局部范围。

新增2项桥接测试通过（91ms），含非Git目录及无文件写入、缺失和超限拒绝。尚未接入前端，Words显示仍未完成；页面接入需比较请求前后草稿身份、块状态和文本，丢弃过期结果，不改contenteditable正文结构。此次仅ShellBridge及测试变化，四网页未改，无测试服务器；未暂存提交。
新增桥接契约触发Shell模块完整验证：184项通过（13秒），受影响C#定向格式检查及git diff --check通过。测试进程已自然退出，无启动服务器。

## 真实Words高亮接入（2026-10-14）

live-data按当前草稿逐块调用git/conflict-words，使用CSS Highlight引用现有文本节点，未向contenteditable插入标签。未处理行按参考40%冲突色+60%正文背景减淡，差异词保留原冲突色。代次、草稿对象、正文及节点存活检查丢弃过期响应；超限或接口失败保留整行模式。关闭或清除冲突时删除Highlight并使旧请求失效。多块当前串行比较，缓存/调度成本尚待评估。

真实单块merge-words：三个范围文本精确ours/base/theirs，正文无嵌套span；编辑、撤销、外部刷新、组合输入、精确保存通过。多块merge-words-multi：词级层、第二块键盘接受、撤销、滚动和保存通过。构建0警告错误（2.29秒）、JS语法、9共享资产、git diff --check通过。未完成浅色/全状态像素配对及延迟响应专门注入测试，不宣称完整Words一致；分词沿现有Core规则而非全量ByWord移植。

修改live-data、两份CSS及真实驱动。live-data MD5从CD8A9E5F24860A347DD2342DD4B6D051变为0C83F790761AF0F620B51C38ADA8AAA7；CSS从D07319B5D7B9701846DC309381B384FC变为F73E359E4B43C7557959A68935BA2C78；mockup.js/bridge未变。PID17180/24084、端口57605/49683、临时目录FEZYOI/kI1GJl由finally回收。启动沿capture-conflict-reference-shell.cjs输出merge-words或merge-words-multi，后者Playwright参数后加--multi；异常停止taskkill /PID <本次PID> /T /F。未暂存提交，整体目标未完成。
## 浅色与迟到词级请求验证（2026-10-14）

驱动新增--stale-words，仅测试中暂存git/conflict-words发送，其余宿主请求正常；接受左侧产生旧比较后忽略右侧解决该块，再释放旧请求，确认无高亮恢复。两次撤销重新得到ours/base/theirs。浅色完整流程merge-words-light-stale通过，深色merge-words-cleanup-final通过，并在真实编辑器移除后确认CSS Highlight注册表无conflict-words引用。

首次关闭断言在__augitConflictSaved出现时立即检查而失败；该标志是保存完成，loadOperationSession仍异步运行。改为等待编辑器实际移除后核验，未改产品迁就测试。不宣称覆盖所有异步乱序、多块请求或完整像素复原。

本批仅审计驱动及记录变化，产品哈希未改。JS语法、git diff --check通过。PID21180/25396/15236、对应临时目录Augit-merge-pair-XJc2J7/1tY3j4/q77aao均finally清理。启动沿capture-conflict-reference-shell.cjs，Playwright参数后加--stale-words，浅色另加--light；异常停止taskkill /PID <本次PID> /T /F。整体目标未完成，未暂存提交。
## 右来源行旁操作顺序（2026-10-14）

重新打开PyCharm2026.2.1单块merge，初始四行（含尾部空行）与Augit现有模型一致，中央为base value。实际右来源从左到右是行号、接受、忽略；Augit此前是忽略、接受、行号。参考原始JPEG及来源元数据见merge-right-gutter-reference，不算无损配对。权威ThreesideMergeHighlighters.kt:57–60、79起确认两侧独立APPLY/IGNORE，顺序观察来自实机。

共享mockup.js按侧排列按钮，CSS仅移动右来源行号与按钮位置；保留原槽宽与稳定块ID，不改变接受语义。真实多块、单块均通过，新增DOM顺序与行号横坐标断言；后续接受/撤销/滚动/精确保存继续通过。单块gutter-initial.png已目视确认右侧顺序修正，gutter-order.json记录断言。不是精确槽宽、图标及连接区像素通过；左右同内容截图仍存在外壳、尺寸、编码差异。

mockup.js MD5：D7D0B12938C9CA14E83E391E91057563 → 12C7334C0A36F5963BBF43FE74834B50；CSS：F73E359E4B43C7557959A68935BA2C78 → 523872BF676F31CC3AAE6A687AA315F6。live-data/bridge未变。共享资产、JS语法及git diff --check通过，没有全量测试。

启动：node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-right-gutter-single C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright；多块输出merge-right-gutter并追加--multi。正常finally关闭；异常taskkill /PID <本次PID> /T /F。Augit PID22080/25488及临时Augit-merge-pair-RmBVPv/TDFXC9已清理；参考PID18808已停止且参考路径无残留。未暂存提交，整体整改未完成。

## 行旁动作自绘图形（2026-10-14）

权威DiffUtil.java:553按侧选择Diff.ArrowRight/Arrow；AllIcons映射New UI的14×14资源。参考端点测量为双折线、1px圆端，忽略为11×11对角叉线；在统一icon入口自绘polyline/path与镜像，替换字体字符×/»/«，未分发官方SVG资源文件。字号不再改变字形。仍沿既有文字色，尚未完成图标颜色、抗锯齿及单侧已接受后的下箭头状态对齐。

真实多块断言最初发现全局.augit-window图标规则覆盖14px为16px，已提高局部规则优先级。最终merge-gutter-svg-final通过实际SVG尺寸14px、语义名称、无文本字形、顺序、键盘聚焦/Enter/撤销、滚动和精确保存；初始图已目视。共享资产、语法及git diff --check通过，未跑全量。

mockup.js MD5从12C7334C0A36F5963BBF43FE74834B50到91261820FA332E3CE385081439B5E613；CSS从523872BF676F31CC3AAE6A687AA315F6到AF03C9F166369B529B3BAFF35813ED6C。live-data/bridge未改。两次失败PID17012/7612及最终PID26552均finally清理，临时JhOrDb/U8lKPY/eVqKQy均删除。最终端口53420。未暂存提交。

启动沿capture-conflict-reference-shell.cjs，输出artifacts/pixel-followup-2026-10-14/merge-gutter-svg-final，Playwright参数后加--multi；异常停止taskkill /PID <本次PID> /T /F。此批不是完整双端像素验收。

## 按操作顺序追加与截图纠错（2026-10-14）

实际 PyCharm 2026.2.1 先接受右侧、再接受左侧，中央顺序为 first line / theirs value / ours value / last line / 尾部空行。原始系统 JPEG 及来源在 merge-append-reference，未点击 Apply；仅作行为证据，不作无损像素配对。权威 MergeConflictModel.kt:280–320 的 isOnesideAppliedConflict 在现有结果末尾追加，ThreesideMergeHighlighters.kt:111 使用下折箭头；忽略一侧不等同接受。

修复 conflict-draft.js 原先先右后左仍强制左右排序的问题，保留两次操作之间的用户编辑；显式 Accept Both 仍按左右顺序。共享 mockup.js 增加左右下折双箭头并随另一侧 accepted 状态切换；CSS 使用图标色而非正文色，Islands Dark 为 #C3C5CB、Light 为 #6C707E。依据 ManyIslandsDark.theme.json 的 icon-default-stroke 映射及自绘图形，不分发官方 SVG。

9 项草稿模型测试通过，包括中间编辑及撤销重做；真实单块 merge-append-states 通过先右后左、撤销两次、仅忽略、先左后右箭头、外部刷新、编辑及精确保存。该目录 one-side-accepted.png 实际捕到旧主窗，明确排除其视觉证明，DOM 断言不证明截图正确。

capture-conflict-reference-shell.cjs 全部模态截图改用 CDP view/fromSurface=false；先以内存三色标记核对实际客户区像素，移除标记后另采正式 PNG。新增 --capture-only 仅执行初始/追加状态取证，不重复已通过的外部刷新、IME 协议及保存流程。merge-append-view 的初始和单侧已接受 PNG 均通过标记检查，后者已目视确认三栏、中央 ours value 及右侧下折箭头；外围黑区不是系统阴影。像素核验工具 3 项测试、驱动语法及 git diff --check 通过。没有执行全量测试。

本批 mockup.js MD5 从 91261820FA332E3CE385081439B5E613 到 831018085A501C9EDDAE2BE8759F5DA4，CSS 从 AF03C9F166369B529B3BAFF35813ED6C 到 BC30F27AD39ECA4B2A53F48B2C5A48DC。live-data/bridge 保持 851EFBF8C278A2D40DB6208CE2E60496 / F297BF8769E249CA0D804F4F4EEEF5A9；共享资产验证已通过。记录时分支 dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6，保留混合改动，未暂存提交。

参考 PID14756 已停止且未应用文件；参考 result/base 的 SHA256 同为 14F6DB9E5F72A87AFEB79DFF24E93B5C9E4C457CFA060134CB7C58B977B51CF5。完整单块 PID26276/端口49175/临时 Augit-merge-pair-obUPBM 及取证 PID8404/端口53187/临时 Augit-merge-pair-tKxEWY 均由 finally 清理，取证后独立查询确认 PID 和目录不存在。

```powershell
node --test tools/audit/conflict-draft.test.cjs
node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-append-view C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright --capture-only
python -B -m unittest discover -s tools/audit -p test_modal_raster.py
# 正常 finally 自动关闭；异常只停止该次启动并记录的 PID。
taskkill /PID <AUDIT_PID> /T /F
```

完整三栏窗口结构、浅深主题抗锯齿、连接区及全部模态状态仍未完成双端配对；55 页合格配对数仍为 0。Windows 10 不在本次验收范围，跨屏 DPI 保留未验证。以上是局部代码与取证整改，不是整体完成。

## 浅色结果区默认焦点框整改（2026-10-14）

同批浅色短流程 merge-append-view-light 确认追加状态、图标色和像素标记均通过，目视发现中央可编辑区域带浏览器默认黑色整栏外框。现有真实参考 right-first/right-then-left 未见该外框；与项目只读编辑器去除浏览器默认外框的处理一致，局部增加 .conflict-column.result .conflict-block:focus 的 outline:none。保留光标、可编辑状态以及行旁按钮和底部动作的焦点样式，不改草稿或保存逻辑。

新增真实聚焦断言：activeElement 是结果编辑器、仍可编辑、outlineStyle 为 none、caretColor 非透明。merge-focused-view-light 短流程通过，正式 PNG 通过客户区标记核验，已目视确认黑框消失。只重跑受影响短流程，未再跑全量或重复保存回归；9 个共享资产、驱动语法和 git diff --check 通过。CSS MD5 从 BC30F27AD39ECA4B2A53F48B2C5A48DC 到 9DB3E0F524AC6747B4FFD962421F5CA6，其他三个主要网页哈希未变。

浅色前次 PID20000/端口50899/临时 Augit-merge-pair-G32eQg，修后 PID26136/端口57226/临时 Augit-merge-pair-dpGE3u 均 finally 退出并删除目录，独立查询无 PID/目录残留。启动沿上述 capture-conflict-reference-shell.cjs 命令，输出改为 merge-focused-view-light，末尾加 --capture-only --light；异常停止 taskkill /PID <AUDIT_PID> /T /F。

历史驱动的默认 surface PNG 不能仅凭 DOM 验证就计作模态截图通过。本批已把 empty/multi/initial 三处也切换至相同客户区核验函数，未为只改调用路径重复运行全部历史场景；旧图仅保留历史证据状态，不回填为新验证。当前合格双端配对仍为 0，系统阴影与前景透明帧仍待实测。

## 行号与动作槽的处理状态绘制（2026-10-14）

参考实机 right-first 显示未处理来源及中央行号槽保留完整冲突底色，而正文 Words 行底减淡；已处理来源槽无底色并保留边界。权威 DiffDrawUtil.java:570–588 将 ignored 仅用于 editorMode，gutterMode 仍 DEFAULT；resolved 同时将两者设为 RESOLVED。原 Augit 槽内始终只有面板背景，与此不同。

共享 mockup.js 在行号 track 追加只读背景层，复用正文的未处理、已处理首尾和空范围状态；不复制正文、不加入可编辑区域。CSS 对未处理槽使用完整冲突色，复用已处理点线与空范围双边界。背景层位于行号/动作下方，pointer-events:none；滚动仍平移既有 track。保持行号为原有首节点，避免改变滚动复用契约。

真实浅色单块 merge-gutter-paint-light 通过槽背景 #FFD5CC、宽度、纵向位置、接受后透明底与上下点线、追加/撤销状态；真实深色多块 merge-gutter-paint-multi 通过 #45302B、初始/处理后对齐、键盘聚焦/Enter/撤销、纵横滚动节点复用及精确保存。两次 PNG 均先核验客户区标记；深色初始图已目视确认行号和动作未被底色遮盖。JS语法、9共享资产和git diff --check通过，未运行全量。空范围绘制已接入但本批未补跑空侧专门场景，不能外推为其像素验收。

mockup.js MD5 从 831018085A501C9EDDAE2BE8759F5DA4 到 6298632B16BA82E5FCFB9B0AB177593D；CSS 从 9DB3E0F524AC6747B4FFD962421F5CA6 到 06751CCD396E8ECACD19779CBBDE38AB。live-data/bridge未变。修改两份共享JS/CSS及真实取证驱动。PID23912/端口57930/临时Augit-merge-pair-0doP5x，PID25508/端口49172/临时Augit-merge-pair-VZXU0a均finally清理，独立查询不存在。

```powershell
node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-14/merge-gutter-paint-multi C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright --multi
# 浅色短流程改输出为 merge-gutter-paint-light，并把 --multi 换为 --capture-only --light。
# 正常自动清理；异常停止仅针对驱动输出的本次PID。
taskkill /PID <AUDIT_PID> /T /F
```

当前槽宽、正文留白与连接区尚未一致，55页合格同态配对仍为0；没有把本次客户区PNG作为系统合成图或系统阴影证据。工作区未暂存或提交。

2026-10-14冲突中央行号槽与字体：中央默认缩至43px，不再预留来源动作空间；修复font简写为空导致三栏编号回退界面字体。24组定向及真实深色多块/浅色短流程通过。原始参考PNG、前后哈希、命令、清理和未完成范围见[行号槽记录](pixel-conflict-gutter-2026-10-14.md)。整窗结构与55页双端配对仍未完成。

2026-10-14三栏连接带：新增两条24px只读曲线，按稳定块ID跟随当前草稿、左右处理状态和滚动，已处理侧点线、空侧双边界。真实多块/浅色/末尾空侧及72组布局专项通过；旧字体期望和截图坐标失败已记录。来源、最终覆盖边界、哈希、启动停止及清理见[连接带整改](pixel-conflict-connectors-2026-10-14.md)。完整双端配对仍未完成。

2026-10-15冲突三状态逐行接续：双端取得初始/接受左侧/全部处理系统原图，参考完成提示遮挡中央第二行。正文和编号槽改为设备像素量化粗点线，175%主体周期修正为着色4/留白3；上下厚度、抗锯齿与整窗结构仍不同。六组点阵、实际浅色/空侧/深色多块专项通过；证据、哈希、失败及启停清理见[三状态记录](pixel-conflict-line-pair-2026-10-15.md)。整体目标未完成。

2026-10-15冲突边界矢量接续：渐变与图案贴图会损失边缘像素，改用只读SVG直接路径；实际175%上4排/下3排与参考一致，两段局部平移配准共3000像素零差异，不等于整窗同态通过。12组严格栅格及真实单块/多块/空侧保存通过。证据、失败、哈希与清理见[三状态记录](pixel-conflict-line-pair-2026-10-15.md)。整体目标仍未完成。
