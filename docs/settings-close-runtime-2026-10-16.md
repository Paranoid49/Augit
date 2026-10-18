# 普通启动设置关闭卡顿整改（2026-10-16）

## 最新：用户报告双设置表面与整窗闪动后的回归修复

上一批嵌套返回优化引入重入缺陷：关闭没有清空presentedDialog；返回判断把分配过身份的节点当成已呈现父表单；返回开始后也未立即消费旧子表单身份。结果是重开或返回等待modal-present应答时，连续DOM变化再次抢占正在进行的呈现，旧任务finally提前解除nativeCapture隐藏。背景截图可能因此包含设置表单，与独立设置窗同时显示。此前只延迟cover应答并在窗口稳定后断言，未覆盖呈现应答前的变化，不能以旧通过记录否认用户报告。

本批新增两项直接执行产品状态机的失败用例：重开时呈现次数实际4、预期2；父窗恢复实际5、预期3。修复关闭后清空身份，只对已呈现父节点允许抢占，并在开始呈现时立即更新当前节点，防止重复消费返回状态。首次部分修复仍有父窗重复请求4对3，补齐开始呈现更新后相关13项通过149.7毫秒。未修改C#，未重复Shell全模块测试。

真实Debug随包回归增加--delay-present：扣住原生呈现回包，在每次首次/重开期间连续8帧改变背景，必须恰有1次呈现请求、1个Settings DOM、原生depth=1，且应答前nativeCapture保持true。同时延迟cover应答、保留9个真实仓库标签，覆盖关闭、重开及嵌套返回。证据settings-reopen-final/result.json：五次打开上述断言均通过；五条关闭139.79/167.46/139.13/156.66/247.69毫秒，嵌套返回112.35毫秒，同一父HWND19726470，网页错误0。此处证明重复呈现和过早解除隐藏路径已修复；未取得本批整窗逐帧录像，不将原有背景刷新边缘闪动问题写成全部解决。

尝试将旧JS放入临时资源目录补做实机反证时，整条命令被自动审批策略拒绝，没有执行，也没有创建对应实例或目录；未绕过拒绝。修前证据采用上述真实产品函数的失败测试，修后采用真实宿主结果，不虚构修前实机录像。为该反证临时增加的显式资源参数已移除，不保留未执行的辅助入口。

Debug/Release均重新构建0警告0错误，各1.37秒，24份随包资源逐文件哈希均与web一致；共享资产、两个JS语法和git diff --check通过。live-data.js MD5由BF0559C03870BEA790695BB347433721变为3DCFCEAED3358BE87D4FA3A69080E7FA，其余三份基线哈希不变。测试PID26916及直属子进程不存在，63947无监听，Augit-settings-close-b3tCxk目录已删除。正常启动/停止命令仍见下文。未暂存或提交，不宣称整体目标完成。

## 接续：首图就绪交接（2026-10-16）

重开问题修复后检查发现，CreateSnapshotAsync只提交背景导航，PresentCoreAsync便重挂业务控制器，未等待背景PNG就绪；新背景控制器还可能以默认底色遮住所有者。这是打开阶段的露底风险，不等同于已证明用户全部闪烁均来自此处。当前改为透明初始底色、等待首图就绪后重挂；等待中的背景由宿主持有，关闭时在窗口线程释放并淘汰旧代次，避免把COM清理放到计时器线程。未改变单一业务WebView、原生子窗遮盖或系统阴影方案。

新增4项准备/失败/过期/关闭测试；最终Shell模块207项通过（17秒），JS关闭状态机9项通过（161毫秒），定向C#空白格式、共享资产、JS语法及git diff --check通过。Debug已通过测试构建更新，Release构建0警告0错误（4.14秒）。未再次运行无关模块。

真实Debug随包验证settings-background-ready-final：真实仓库9标签、用户配置副本及隔离profile，故意扣住呈现和遮盖应答。五次打开均仅一次呈现、depth=1、应答前保留截图隐藏，且backgroundLoadedRevision>=0；关闭327/261/241/271/333毫秒，网页错误0，嵌套及原生竞争断言通过。此处证明首图交接和生命周期，不提供打开/关闭完整动画无闪保证。

本批先行实例25996、最终实例8512由脚本finally结束，临时目录Augit-settings-close-u5Eeme/jFACjR删除，端口54312/59806释放。未修改四份网页基线文件，其MD5沿用上一节最终值。正常启动与停止命令仍见下文。背景刷新边缘闪动保留未解决，不宣称整个问题完成。

## 用户现象与根因

用户报告正常启动后Settings点击OK，表单消失但空框、阴影长时间保留。此前小fixture、等待稳定后Cancel的测试不能证明此路径正确。本批使用D:/github/Augit真实工作区、默认配置副本、恢复9个真实标签和展开目录，无scene、pixel-exact或会话重置参数，保留系统缩放。隔离配置与WebView profile用于避免写用户会话，不将其称为用户正在运行的原实例。

基线settings-close-before/result.json记录：保存返回后DOM在2086.20ms移除，关窗请求直到2549.60ms才发出，中间完整等待cover、refresh、uncover，空框排队约463ms。首轮延迟应答回归进一步复现约15秒等待：保存排版又触发变化，scheduled表示稍后重试，却被当成已经排了微任务，后来的关闭通知因此丢失。首次修复没有覆盖这一条件；保留失败记录settings-close-after，不把它当通过。

## 产品修复

- 公共bindNativeModalHost在无表单时优先发送原生关闭，越过在途背景捕获；宿主已有Invalidate负责淘汰旧捕获。
- 分离重试标记scheduled与实际微任务标记microtaskQueued，持续背景变化不能吞掉关闭通知。
- 以操作代次隔离旧捕获、呈现和撤盖返回，避免旧任务修改新窗口状态、解除其忙状态或撤掉其遮盖。
- Settings标题栏链接式Close接入取消会话入口；原生取消事件也能找到此链接式按钮。保留保存失败留窗、Apply不关窗及取消恢复主题语义。

产品修改仅web/src/live-data.js；不采用前批失败的owned遮盖试验，不更换技术栈或复制DOM。本批未改Git写入、用户配置、仓库文件内容和视觉稿。修复针对公共模态调度，但不据此声称所有嵌套模态、所有机器性能已验证。

## 回归与交付

新增modal-close-priority.test.cjs直接执行产品状态机：扣住遮盖/背景刷新、额外变化留下重试标记、关闭重开、旧呈现返回、重复关闭。与恢复帧、快照归属共8项通过。真实check-settings-close.cjs覆盖OK、Cancel、Esc、标题栏Close、Apply后OK，并检查原生active=false、ownerEnabled=true、业务controller回归主窗、snapshotCount=0。

settings-close-final：实际仓库9标签，故意扣住真实遮盖应答，五条关闭路径363.71/274.77/241.04/280.32/304.08ms；每次关闭重开后才释放旧应答，零网页错误。settings-close-delivered：直接加载随包资源，无延迟注入，五条路径298.32/169.84/165.35/275.58/309.50ms；DOM移除与关闭请求观察时间相差0.1–0.2ms，零网页错误。统计包含自动化输入及状态读取开销，不代表每台机器的保证值，也不是逐帧无闪验收。

证据位于artifacts/pixel-followup-2026-10-16/下对应目录。中间失败还包括错误的标题栏button选择器（实际为a）以及审计MutationObserver顺序误判，均保留并修正；最终门禁按相邻DOM移除事件配对，不把同一微任务内0.1ms先后误认为上一轮关闭。测试不再只断言DOM消失，而等待原生销毁及所有者恢复。

首批web/src/live-data.js已同步Debug和Release随包资源，两侧24文件完整镜像检查通过。当时用户运行中的PID9976仍加载旧脚本；后续嵌套批次一度因该进程占用Debug产物而无法构建。用户回复“已关闭”后已确认该进程退出，重新构建Debug和Release成功；当前正常启动产物包含下述全部修复。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。正常关闭窗口停止；异常只对该启动输出的PID执行taskkill /PID <PID> /T /F。

回归：node tools/audit/check-settings-close.cjs <Playwright路径> <证据目录> --many-tabs --delay-cover；随包路径改为--many-tabs --bundled-web。脚本finally停止自己实例、关闭调试连接并删除隔离profile。未全量重跑无关模块，未暂存或提交。Windows10排除本次验收，跨屏DPI未验证；55页、全模态、背景刷新逐帧和阴影等原目标仍未全部完成。

清理复核：首批PID2876/26196/25700/26348/23340/17112/17340均已结束，七个Augit-settings-close临时目录不存在，调试端口无监听。首批live-data.js MD5为C7E46A50E506A643192979202093DA71。共享资源、两套镜像、JS语法及git diff --check通过；中文PowerShell脚本20份编码检查通过。

## 接续：嵌套关闭与正常产物交付

举一反三检查发现：子表单关闭后返回已有父表单，仍可能排在背景捕获之后；若把返回当成首次呈现，还会重新隐藏父表单。修复同时覆盖网页和原生队列：网页识别已呈现父表单，抢先结束过期刷新并保留父表单；ShellModalOperations增加使旧代次失效的同步恢复入口，ShellModalHost仅对已经存在的父窗口使用该入口。捕获等待结束后再次核对代次及遮盖对象，防止过期任务显示已释放遮盖。没有采用此前失败的owned窗口试验，也没有改变单一业务WebView边界。

本接续修改web/src/live-data.js、src/Augit.Shell/ShellModalHost.cs、src/Augit.Shell/ShellModalOperations.cs及对应定向测试。先补充失败用例，再修复；ShellModalOperations专项5项通过，Shell模块203项通过（约13秒），本批最终JavaScript相关11项通过（156.8毫秒）。C#定向空白格式检查、两份JS语法、共享资产、文档声明及git diff --check通过。Debug/Release最终构建分别2.32/1.46秒，均0警告0错误。构建产物位于net10.0-windows/win-x64，两个web目录均24文件且逐文件哈希一致；首次复核误漏win-x64目录得到24个缺失，修正检查路径后通过，未据此改写产品文件。

最终真实Debug验证为artifacts/pixel-followup-2026-10-16/nested-close-debug-final/result.json：加载随包web、真实仓库9标签、默认配置副本，不使用scene、pixel-exact或会话重置参数；为保护用户会话仍使用隔离profile，并故意延迟遮盖应答。OK、Cancel、Esc、标题栏关闭、Apply后OK分别149.07/183.46/157.51/155.43/241.07毫秒；DOM移除至关窗请求观察差0–0.2毫秒，网页错误0。嵌套返回108.71毫秒，返回同一父HWND1640488、保留草稿且只剩1份父快照；原生捕获与返回并发探针201.10毫秒，旧捕获落地后遮盖HWND为0，最终关闭后快照0、所有者启用、控制器回归主窗。

嵌套探针注入临时表单，仅验证生命周期，不计入PyCharm同态页面；原生协议探针没有DOM，其最后两次关闭不计入用户操作延迟。Release两轮证据nested-close-release、nested-close-native-race亦保留。以上耗时含自动化输入与查询开销，不代表逐帧无闪验收或所有机器性能保证。

复验命令：`node tools/audit/check-settings-close.cjs <Playwright路径> <证据目录> --many-tabs --delay-cover --nested --bundled-web`。日常启动仍使用上文start-augit.ps1，无需使用测试参数。停止仍为正常关闭窗口，异常仅对启动输出的PID执行taskkill。

本接续PID26928/28552/24852及其直属子进程均不存在；端口58471/58761/54600无监听；临时目录Augit-settings-close-ijRZlC、Augit-settings-close-Njmr2t、Augit-settings-close-e72gNo均已删除。分支dsh、HEAD3e084a2d、451项混合工作区变动保持，未暂存或提交。四文件MD5：mockup.js=44D6C53684B74DBC6116FF25F7C22932，mockup.css=D5DADE8F4DB84506031A9AB3B1F5DB80，live-data.js由C7E46A50E506A643192979202093DA71变为BF0559C03870BEA790695BB347433721，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

本批解决的是设置关闭及同类嵌套返回卡顿；全部页面同态配对、背景刷新逐帧与阴影目标仍未完成，跨屏DPI保留未验证，Windows10按用户要求排除本次验收。

2026-10-16设置回归接续：重复入口复用表单，未变字体不全页度量，背景相同属性不捕获，关闭取消隐藏遮盖导航。18项JS、12项设置语义及209项Shell通过，Debug/Release真实竞争回归通过；录屏343帧仍52帧边缘变化、首次588.78ms，未宣称全部修好。详见[本批记录](settings-open-regression-2026-10-16.md)。
