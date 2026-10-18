# 2026-10-13 字体与独立模态整改接续（更新至2026-10-17）

2026-10-17 Git 元数据刷新：正常入口外部 `git checkout -b external-branch` 命令 82ms、界面 branch 更新 125ms（PID 20572/端口 54491，CloseMainWindow 成功、临时目录删除）；只覆盖单次本地分支变化，见[记录](git-metadata-refresh-2026-10-17.md)。

2026-10-17 捕获等待上限：历史2904ms样本定位为窗口显示后捕获/渲染等待（present请求2478.8ms、rendered2889ms、presented2904ms），给CapturePreviewAsync加500ms上限并记录capture-timeout；Shell246、设置281/304/286ms、正常冒烟3/3通过。当前Shell SHA256=15A1AE14B74D049B3F3B695F4BC052BEB606CF22E9F87B606CE58DFE594A74CD；慢样本不可重复，超时路径未在真实慢样本触发。

2026-10-17第二轮性能接续：设置正常入口附CDP连续开窗48次（3+10+5+30）并导出原生阶段，呈现271–449ms（平均380ms、无一次达到500ms），未复现2904ms慢样本；首窗原生303ms、外部轮询887ms；10次开窗无重复窗口；另录制一次设置开关（2880×1716、25fps）未检出整窗白帧，最大包络外像素集中在标题栏与CDP指针native tooltip；无CDP四次真实系统鼠标开窗呈现492.0/959.6/748.6/909.3ms，方差主因是git/detect→preview的63–407ms（含document.fonts.ready/主题应用），关闭52–59ms；样本4近白扫描0整帧白闪候选，未复现2904ms；深色主题高帧率复核确认真实一帧白闪：CopyFromScreen 400×300中心两次(347/349帧、约116fps)均100/100全白(约第55/57帧)，带trace白帧在presented后1641.7ms；DDA另记1–2帧整窗白块，GDI整窗同帧未复现；已在设置子窗WM_ERASEBKGND用主题表面色填充（主窗不变），两次独立深色高帧率349/350帧、同进程三次开关291/296/299帧、浅色348帧max0，Shell244项、正常冒烟、设置257/229/244ms、关闭37/103/52/68ms、刷新459/360/344/384ms通过；仍需浅色/全DPI/多次开关验证。Shell SHA256=AF550779EECDFB4F3BF6B1BF32EEE0D1E283DA47C55F9397F830157B1E0024AB。提前`document.fonts.load`候选三样本620.8/480.0/542.3ms，但撤回后对照469.0ms/83.6ms，候选被否定并恢复live-data.js MD5=4A266F4927CDCE21B8FAD52168618787。另5次Cancel关闭原生24.8–46.1ms、驱动上界69–151ms，未复现关闭卡顿。`WorkspaceFileWatcher.MergeDelay`50→20ms后正常入口刷新严格撤盖GDI两批495.7/361.3/366.3/338.4ms、496.5/364.9/381.4/372.9ms，DDA批481.0/368.0/352.1/355.5ms；完整帧排除光标后GDI1363帧、DDA273帧均0变化帧，表单客户区未检出透明/白帧；但仅585×252与25fps，不宣称全场景无闪或500ms验收完成。正常入口冒烟另验证文件只读JetBrains Mono13、终端echo输出与隐藏保留、Changes单击不改编辑区且Enter打开只读Diff、无页面错误/失败请求，CloseMainWindow成功；证据normal-entry-smoke-2026-10-17.md。实际映射Infrastructure=D95B2F3C…75D9F6AC3→2CCA339F…AF8190F1F，Shell仍27D29A65…2C26944F；Infrastructure203、Shell243通过，资源已清理。详见[原生阶段](settings-native-stage-2026-10-17.md)与[合并窗口](refresh-watcher-merge-2026-10-17.md)；慢开、关闭卡顿及整体目标未完成。

2026-10-17三栏冲突编辑器移除：用户批准后删除内置三栏编辑、草稿、逐块合并、整侧接受、保存及专属端点/模型/测试；保留冲突提示、文件列表、普通只读Diff、Continue/Skip/Abort和外部处理后的状态同步。规范与验收矩阵已同步，55页目录按保留范围调整为54页（A线54、干净仓库巡检53、PyCharm未对照16），三栏逐行像素验收登记为经批准移出范围。正常入口PID25552、端口57018（附CDP），实际映射Shell SHA256=27D29A65…2C26944F、Infrastructure=D95B2F3C…75D9F6AC3；DPR1.75、Diff正文JetBrains Mono13、界面Microsoft YaHei UI；冲突列表→只读Diff→外部`git add`后267ms列表清空且Continue可用。Core72、Shell243、Infrastructure203、JS43+20+5通过，共享资产9项、22脚本编码、DOC_CLAIMS_OK及`git diff --check`通过；`CloseMainWindow`未成功、按本批PID执行`taskkill`、端口无监听、临时目录已删、正常关窗未取证。四网页MD5更新为mockup.js=2D6A11B95157BB61796FCB831626D2B7、mockup.css=4F4D6394178D8D96D4CCCE6DC0421904、live-data.js=4A266F4927CDCE21B8FAD52168618787、bridge.js=6FDFEB10AF93CBCA2191202A1126BC3D；Release未更新。三栏专属CSS与`verify-ux-conflict.cjs`已清理，正常入口冒烟PID9712通过、共享CSS仍与视觉稿一致；其余三栏专属历史工具已一并删除，只保留只读回归工具。整体目标未完成。详见[移除报告](conflict-editor-removal-2026-10-17.md)。终端命令块分隔/块间留白经批准登记为有意差异，其余对照继续。

2026-10-17远端长URL：按权威粗体度量修正URL列宽，正文仍400；修前共享页面636对期望667失败，修后及正常入口均667逻辑像素。3项布局、41项行为通过，最终JS MD5=0EEBB52037A8EEABDB81DA777E8C1FFE；两实例/端口/临时目录独立清理。未采本数据态双端PNG，不计整页通过。详见[长URL核查](pixel-remote-url-width-2026-10-17.md)。设置/刷新及历史清理阻塞见下一条，整体goal仍active。

2026-10-17最终背景恢复：补失败同图重试不再跳过加载等待；正常入口最后撤盖629.0/567.9/542.8/504.9ms，305完整帧仍有187非光标边缘变化，未通过。最终Shell SHA256=94EF57F5…78EF46；27项Shell覆盖及6项脚本/接线验证，本批两实例已清。用户已批准临时停Nahimic，但服务权限拒绝，前后仍Running/Auto/PID5848，对照未执行；参考PID21240及指定目录虽获清理授权，仍被自动审批拒绝。详见[最终复验与阻塞](refresh-background-loaded-2026-10-17.md)，整体未完成。

2026-10-17背景换图确认：宿主刷新现在等待对应图片onload，补齐脚本提交与加载之间的协议缺口；正常入口四次最后撤盖648.6/527.2/508.4/531.2ms仍超500，307完整帧仍有187非光标边缘变化像素。26项Shell及6项换图/接线测试通过，实际Shell SHA256=954D7E…572FE，本批PID/端口/临时目录独立清理。用户已授权清理历史参考PID21240和指定目录，但自动审批再次拒绝，仍未清理。详见[背景加载确认](refresh-background-loaded-2026-10-17.md)，整体未完成。

2026-10-17无CDP设置GDI时钟：正常启动器472帧与原生阶段已核对；控制器创建2448.24ms、presented3280.86ms，桌面首次出现被302/303相邻帧夹定，慢开仍未解决。实际映射身份已核对，含Nahimic；字体执行证据和正常关闭周期未补齐，不能宣称验收通过。本批PID/记录器/临时目录已清理，参考历史清理阻塞仍在。详见[无CDP时钟证据](settings-gdi-clock-2026-10-17.md)，整体未完成。

2026-10-17远端列分配接续：参考空表330宽实际75/255，表头内距8、正文2；已修复固定列与旧错误对齐断言。正常入口完整图RGB2差17120→17028，但平均色差上升，仍未通过。JS MD5=F24517B92AC7A46D6343F3E214C333B2，CSS=00FA3357EC646E91030A9DFDD925DAA9；41项行为及定向布局通过。本批Augit清理完成，参考PID21240/目录仍待清理授权。详见[列分配与复验](pixel-remote-table-columns-2026-10-17.md)，整体未完成。

2026-10-17远端组件接续：安装版实际布局确认无外层布局边框、工具条24加1分隔、表头25及26×24动作按钮；正常入口修后完整图RGB2差20902→17120，仍未通过。CSS MD5=0D75936BA3E8F4077344545FA1604A32，Push URL保留。41项行为、12组布局与3项身份/门禁通过；最终进程实际映射已核对。两批Augit清理完成，参考PID21240和临时目录清理被自动审批拒绝、等待用户指示，不能宣称零残留。详见[组件边距与复验](pixel-remote-component-insets-2026-10-17.md)。整体未完成。

2026-10-17最新刷新纠正：查明详情缓存以短hash写入、完整hash查询而永不命中，已修为fullHash；正常入口首轮遮盖2次降为1次，最后撤盖919.6→631.1ms，后续518.7/569.1/502.9ms仍超500ms。旧观测仅首轮，不能当完整刷新耗时。最终live-data.js MD5=DE85823861C020608F28DE997197B555；319完整帧仍有边缘变化，不计无闪。23项JS覆盖、25项Infrastructure/2项桥接/6项Python分批通过，九批资源已清。详见[最新根因与计时纠正](refresh-status-batch-2026-10-17.md)。整体未完成。

2026-10-17刷新状态读取接续：实际正文仍JetBrains Mono13、UI12、DPR1.75；同批去重及只读Git并行后最终624/524/553/597ms，未达500ms，完整帧边缘变化仍存在。产物映射身份、测试和清理见[刷新整改记录](refresh-status-batch-2026-10-17.md)，不增加整页配对合格数。

2026-10-17终端全区域：实际14行、五空白行及同尺寸参考已取证；完整内容区1370040像素仍差97830；行级补测8行步长31/32一致、横向起点差1–2px、墨迹差约3px，面板原点40px与提示符17px仍未同态，不能以八行字体诊断代替同态。9项JS/7项Python通过，未改产品终端，临时参考目录删除受阻，见[本批记录](pixel-terminal-visible-rows-2026-10-17.md)。

2026-10-17生命周期接续：环境与脚本注册异步续接补销毁保护，旧设置失败不再影响所有者。最终正常入口未就绪关闭75ms退出，命中脚本注册pending但未证明迟到续接执行；八类回归和20项JS通过。当前Debug身份、Shell分批测试、三实例清理及限制见[生命周期记录](settings-initialization-lifetime-2026-10-17.md)。四网页未变，整体整改未完成。

2026-10-17远端最新：禁用图标误用文字灰色已修正，正常入口原图全图差异20982→20969，保留Push URL；只涉及225个图标像素，不计整页通过，详见[远端记录](pixel-remote-approved-layout-2026-10-17.md)。

2026-10-17最新：正常关闭阶段导出补齐，13窗原生销毁证据完整、八类设置回归通过；附CDP且没有新桌面录像，慢开/闪烁仍未验收，见[关闭阶段记录](settings-close-stages-2026-10-17.md)。正常入口冲突1890×1215三态完整编辑区比较及左来源点线留白修复见[pixel-conflict-daily-inset-2026-10-17.md](pixel-conflict-daily-inset-2026-10-17.md)，仍差24564/24519/25488像素。未增加55页整页合格数量。

2026-10-17无CDP与正常入口接续：新增有界原生阶段诊断，秒级等待仍在且可见性去重候选已撤销；正常入口真实文件刷新首次614ms，完整283帧仅检出光标变化；独立设置阴影完整四角因背景不同不可比较。四网页MD5未变，最新DLL、测试、失败路径及零残留记录见[本批报告](passive-settings-refresh-shadow-2026-10-17.md)。55页、逐行和全场景目标仍未完成。

2026-10-17后续：远端最新全图仍差20982像素（RGB容差2），以下21520属于前一阶段。设置新增无CDP正常入口、独立域名及续接线程反证；域名候选撤销、正式构建已恢复，慢开仍未解决，见[最新诊断](settings-no-cdp-origin-2026-10-17.md)。冲突已有三状态扩展为完整编辑区逐行诊断，保留行尾/编号槽/连接带差异，不改变正常入口验收状态，见[全宽记录](pixel-conflict-full-width-2026-10-17.md)。

2026-10-17最新接续：远端实机调整已获用户明确批准且保留Push URL，正常入口主窗624×385，整图仍差21520像素（容差2），见[批准后整改](pixel-remote-approved-layout-2026-10-17.md)。设置准备改由窗口消息执行，664帧复验仍检出整块白窗，见[准备排队记录](settings-ready-queue-2026-10-17.md)。终端采集改走正常启动器，补齐实际字符JetBrains Mono字体证据，八行步长一致但完整栅格未通过，见[终端正常入口记录](pixel-terminal-daily-entry-2026-10-17.md)。未增加55页合格数量，整体目标未完成。

2026-10-16跳转行无遮挡接续：取得同为空输入聚焦、584×252、指针清除96px的双端原图；输入框仍偏3物理px，未判通过。只读参考组件变换新增0.75物理px横向相位证据，未盲改产品偏移。11项真实行为通过；临时代理目录删除被拒绝、进程已停止，见[本批记录](pixel-goto-empty-pair-2026-10-16.md)。

2026-10-16字体来源诊断：随包WOFF2与独立安装TTF在同浏览器、五档缩放和深浅两色的十组Canvas样本逐像素相同，未修改产品。此证据不是实际DOM／PyCharm配对，八行实机残差仍未解决；两项定向测试通过。详见[本批记录](pixel-terminal-font-source-2026-10-16.md)。

2026-10-16激活后渲染接续：日常启动器录到整窗白屏，分段静置确认首次呈现阶段仍有白帧。设置现先在cloak期间显示并激活，再确认自身渲染，最后单独揭示；修后504原始帧全画面检查未再检出整窗白屏，最大差异为Terminal分类，关闭后58帧稳定；不宣称全部场景零闪。Shell226项、Release八组通过，秒级慢开仍未解决。实现、反例和启停见[本批记录](settings-activation-render-2026-10-16.md)。

2026-10-16日常启动器接续：独立设置回归实际执行start-augit.ps1及默认WebView profile，八组通过、重复请求保持唯一HWND，四条关闭38–66ms。真实脏草稿OK录像55ms关闭、版本仅增一次；首次呈现1228ms；420帧原始录像检出两帧整窗白屏，闪烁仍未解决。关闭后50帧稳定不抵销开窗／切页阶段失败。入口差异、证据与启停见[本批记录](settings-actual-launcher-2026-10-16.md)。

2026-10-16入口精简接续：独立Settings改用共享实现的精简HTML入口，去掉五项未用资源，导航记录17→12项。Release前三次呈现320／291／446ms，修后314／289／338ms；仅样本改善，偶发慢开根因仍未定位。Release八组、Debug源入口及17项Shell定向通过，两套产物25文件同步；证据与边界见[本批记录](settings-minimal-entry-2026-10-16.md)。

2026-10-16真实OK录像接续：Release随包配置隔离实例修改终端草稿后OK，66ms关闭、版本只增一次、持久值正确。323帧全检查，关闭后的54帧与最终状态一致；首次原生呈现1741ms仍属慢开未解决项。录像最大间隔38ms，不宣称全场景无闪。审计9项、证据及资源清理见[本批记录](settings-ok-cycle-2026-10-16.md)。

2026-10-16独立设置阴影接续：旧采集入口已改为独立页面及唯一HWND校验；正常源网页入口取得严格前景／指针门禁下的原始PNG，关闭背景两次一致。下方64px外角通过，上方背景不一致，默认尺寸也与旧参考不同，完整阴影仍未通过。本批不改产品视觉，8项审计测试通过；失败采样、哈希和六次实例清理见[本批记录](settings-independent-shadow-audit-2026-10-16.md)。

2026-10-16启动接续：独立设置去掉二次配置读取与空容器字体度量，后续通知仍读最新值。Debug／Release各10次开窗298–488／317–494ms，首次326／317ms；不能据此消除历史慢开和完整帧缺口。分段耗时、测试与清理见[本批记录](settings-startup-single-read-2026-10-16.md)。

2026-10-16关闭阴影接续：整帧比较揭示旧包络漏掉92710像素中间态；设置现先撤下合成画面再激活主窗。修后252帧未再出现该关闭阴影变化，Debug/Release回归及Shell224项通过；首次仍1540ms，不能宣布全场景无闪。原图、时序、限制和清理见[本批记录](settings-full-frame-cycle-2026-10-16.md)。

2026-10-16逐行接续：既有三状态系统原图27个固定行区域均仍有像素差异，初始文字横向残差约2物理像素；后两状态中央首行包含光标影响。三栏整数布局符合参考，未加入平移补偿或改称通过。审计4项反例、身份哈希和资源记录见[本批诊断](pixel-conflict-fixed-lines-2026-10-16.md)。

2026-10-16阴影接续：清洁复采的浅色活动跳转行完整外环468480像素及四个内角1024像素与参考零差异，最外沿回到背景。旧图内角反例已检出，采集加入128物理像素指针门禁，外环退出码也要求阴影未截断。此结果仅覆盖本次状态，深色／非活动及独立Settings新路径仍未完成。证据、工具验证及清理见[本批记录](pixel-shadow-inner-corners-2026-10-16.md)。

2026-10-16原生主题与单例接续：独立设置保存、取消和重开已同步WebView底色；异步保存通过UI消息更新COM。Debug创建期间并发八次打开保持同一HWND，Release随包回归通过；最终批次开窗336–625ms，仍不宣称全场景无闪。失败试验、原生输入限制、验证和清理见[本批记录](settings-independent-webview-2026-10-16.md)。

2026-10-16晚间逐帧纠正：旧抽帧遗漏关闭淡出与白窗。独立设置已继续修补过渡和呈现顺序，302原始帧复核仍不等于全场景零闪；重复入口与关闭功能通过，偶发秒级开窗仍未解决。当前实现、失败原图、计时、哈希及限制见[独立设置整改](settings-independent-webview-2026-10-16.md)。

2026-10-16最新接续：用户已授权独立设置WebView，正式路径已实施并同步架构，下面“待确认／未实施”均为历史状态。设置不再搬移主控制器或捕获背景；真实源网页及Release随包回归通过，首次呈现白窗与关闭抢焦点已继续修补。开关录像已有一次有效全过程，但全部帧定量及整体像素验收未完成。证据、失败记录、哈希和启停见[独立设置整改](settings-independent-webview-2026-10-16.md)。

2026-10-16冲突接续：修复保存成功仍保留编辑器和自身保存触发未保存误报；20项定向及随包真实保存通过。整数客户区消除底栏分数坐标偏差，但三状态系统补采超时，整窗配对未通过。证据、哈希、清理及启停见[本批记录](pixel-conflict-save-lifecycle-2026-10-16.md)。

2026-10-16用户回归接续：真实设置单例／关闭回归通过，但两项合成试验均仍有边缘闪帧，已撤回并强制重建正式Debug；独立设置WebView方案待用户确认，不能宣称问题修复。证据、资源及启停见[本批记录](settings-window-regression-next-2026-10-16.md)。

2026-10-16连续状态采样与合成反证：正式GDI仍有撤盖后边缘闪动；will-change和原始PNG分层子窗候选均失败，已恢复正式Debug。重复窗口排队修复保留，整体闪烁未完成。证据、恢复、启停与清理见[本批记录](settings-native-frame-trace-2026-10-16.md)。

2026-10-16显隐隔离：只创建隐藏截图层345帧、只显隐遮盖325帧均无变化；增加表单隐藏即复现一帧约229万像素露底。visibility候选仍失败并已恢复，审计12项通过，整体未完成。完整证据及资源记录见[隔离记录](pixel-refresh-create-hide-isolation-2026-10-16.md)。


2026-10-16排队重开整改：新表单在旧关闭或背景捕获等待期间提前绘制的竞态已修复，20项定向及Debug/Release真实宿主回归通过，日常真实配置只观察到一个设置窗口；DDA仍2帧边缘变化，整体闪烁未完成。证据、哈希、启停与清理限制见[本批记录](settings-queued-reopen-2026-10-16.md)。


2026-10-16无变化通知整改：真实仓库复现构建目录事件导致无条件背景重画，现以数据差异限制刷新；Debug/Release重复入口、五条关闭及嵌套通过。正式DDA仍4帧边缘变化、首次602ms，未宣称整体闪烁修复。证据、正常入口验证、哈希与清理见[本批记录](settings-nochange-refresh-2026-10-16.md)。


2026-10-16终端字形反证：实机13px与23/1.75px的完整PNG完全相同，向下取整反而增大差异；八行残差一致，独立安装TTF与参考TTF逐字节相同。未改产品字号，完整终端配对未通过，证据及清理见[本批记录](pixel-terminal-raster-size-2026-10-16.md)。

2026-10-16设置交接续查：客户区仅验证与固定内容层两个试验仍分别有1/2帧边缘变化，均已撤回；正式Debug真实仓库重复入口/五条关闭/嵌套回归通过，整体闪烁未修复。证据、恢复哈希与清理见[本批记录](settings-refresh-handoff-probes-2026-10-16.md)。

2026-10-16屏幕原图反证：遮盖源图与录像首帧2440620像素完全一致，交接仍两帧4054边缘/角部像素变化；仅补齐源图外框不足以消除闪亮。候选及驱动已恢复，未改变产品，证据与清理见[本批记录](pixel-refresh-screen-source-2026-10-16.md)。整体目标未完成。

2026-10-16设置外框接续：重复入口及真实仓库9标签回归通过，单层网页外框保留且两套产物更新；正式浅色仍3帧内部露底、深色仍2帧边缘变化。PNG组合候选失败已撤回，整体无闪未通过；证据、哈希、启停与清理见[本批记录](settings-native-frame-2026-10-16.md)。

2026-10-16深色终端实机配对：修正误继承的工具面板底色/文字色为控制台#1E1F22/#BCBEC4；修后八行178×21墨迹外框、32/31步长与参考一致，仍有抗锯齿残差，未计整窗通过。6项定向与真实主题回归通过，两套产物同步；参考临时目录清理被策略拒绝，进程已停止。详见[深色终端配对](pixel-terminal-dark-base-2026-10-16.md)。

2026-10-16首图交接修复：打开独立表单前等待背景PNG就绪，初始底色透明；等待中的资源在窗口线程随关闭释放。207项Shell及9项JS通过，真实Debug五次打开单次呈现且首图已加载，关闭241–333毫秒。背景刷新边缘闪动仍未解决；详见[关闭与首图交接记录](settings-close-runtime-2026-10-16.md)。

2026-10-16重开修复后系统复验：342帧中62帧边缘/角部变化，内部变化0，首次601.59ms，严格门禁仍失败。裁切捕获虽减少约25ms，但表单半像素边界导致精确等价未证实，不进入产品。新增诊断及5项定向测试，资源已清理；证据、限制和启停命令见[复验与裁切诊断](pixel-refresh-after-reopen-2026-10-16.md)。整体目标保持未完成。

2026-10-16用户双窗口回归：上一批嵌套优化遗留表单身份，重开/返回等待应答时会重复呈现并提前解除隐藏，已用两个失败测试复现并修复。13项定向通过；真实Debug故意延迟呈现及截图回包，五次打开均一次呈现、depth1，关闭139–248ms；两套产物已更新，测试资源已清理。没有本批逐帧录像，不宣称原有全部闪帧消失。详见[最新回归记录](settings-close-runtime-2026-10-16.md)。

2026-10-16普通启动关闭整改及嵌套接续：修复关闭排队、重试吞通知、标题栏关闭遗漏，以及子窗返回父窗等待捕获和重新隐藏父表单。用户关闭旧实例后Debug/Release均已重建；最终Debug随包、真实仓库9标签、延迟应答验证五条关闭路径149–241ms，嵌套返回109ms，零网页错误。11项JS及203项Shell通过，两套24文件镜像一致，测试资源已清理。完整失败、证据、哈希及启停命令见[关闭整改](settings-close-runtime-2026-10-16.md)，不等于全量像素目标完成。

2026-10-16遮盖接续：有效前景下owned顺序候选仍有内部露底；原始PNG位图候选六次303–414ms、323帧内部未变化，但52帧边缘变化，均已撤回并恢复Debug，架构未变。详见[窗口顺序试验](pixel-refresh-owned-order-2026-10-16.md)与[位图试验](pixel-refresh-owned-bitmap-2026-10-16.md)，整体未完成。

2026-10-16独立遮盖试验：用户已授权，实际录像被外部前景干扰，候选未定论且正式实现已恢复。新增持续前景取证门禁；普通入口深色终端八行步长完成实际端测量。详见[遮盖取证](pixel-refresh-owned-cover-2026-10-16.md)与[终端普通入口](pixel-terminal-normal-entry-2026-10-16.md)，整体仍未完成。

2026-10-16选区接续：终端选区来源整改已接入正常入口和随包资源，浅深焦点状态与固定字体专项通过。完整终端配对及整体目标仍未完成。证据、哈希、失败与清理见[选区记录](pixel-terminal-selection-2026-10-16.md)。

2026-10-16遮盖局部捕获：三组原图差异1640/0/1640，局部耗时未稳定下降且伴随背景刷新，因此未接入产品。2项专项、原始失败证据及资源清理见[局部捕获对照](pixel-cover-clip-2026-10-16.md)。历史桌面露底证据保持，整体未完成。

2026-10-16截图干扰：确认Playwright默认隐藏光标会改写后台输入框style并触发背景刷新；审计改为保留真实光标，增加跨采样时段可见性门禁。普通入口Settings补拍成功，4个专项子场景通过；既有桌面露底证据仍保留，整体未完成。失败、原图与清理见[截图干扰记录](pixel-capture-interference-2026-10-16.md)。

2026-10-16冲突无遮挡补采：新增96px只读指针门禁及坐标证据，三状态系统图成功，初始完整原图无此前光晕；完整差异80674/87155/96834，仍未通过。Apply局部宽度修正后差异2305，少于前批2651；不外推整体。失败、4项定向及清理见[指针取证记录](pixel-conflict-pointer-2026-10-16.md)。

2026-10-16底栏宽度接续：修正文本ceil造成Apply Changes宽121而参考120的问题，改为参考整数度量round；96组定向及一次真实取证确认四按钮宽99/108/120/78。右组位置仍多0.571逻辑px、纵向多0.286，与客户区小数尺寸一致，未以任意偏移补偿；证据、哈希与清理见[底栏接续](pixel-conflict-footer-2026-10-16.md)。

2026-10-16冲突底栏：修正Apply Changes默认动作字重400→600，72组定向通过，正常入口与随包资源同步。三状态系统补拍仍有鼠标标记；应用按钮原始像素差2419→2651，未宣称像素收益或整窗通过。原图、反证、哈希和清理见[底栏接续](pixel-conflict-footer-2026-10-16.md)。

2026-10-16禁用工具图标接续：按安装版PyCharm GrayFilter实算修正冲突工具栏浅深禁用灰阶，72组定向及一次真实编辑保存通过，正常入口与两套随包CSS同步。深色系统配对及整窗仍未完成；当前哈希、证据与清理见[工具栏接续](pixel-conflict-toolbar-2026-10-16.md)。

2026-10-16冲突工具栏：导航复用共享上下箭头，启用图标改用既定浅深语义色，72组布局及一次真实编辑保存通过，Debug/Release资源已同步。前批补采仍有鼠标光晕，禁用图标与整窗像素差异未结案；证据、哈希和启停清理见[工具栏整改](pixel-conflict-toolbar-2026-10-16.md)。

2026-10-16冲突公共行导航：修复首行在冲突前仍禁用下一处的问题；12项定向、51项模块及真实浅色单块/深色多块保存通过。修正采集主题版本遗漏，并同步Debug/Release资源。原始期望、失败原因和清理见[导航整改](pixel-conflict-navigation-2026-10-16.md)；未增加55页合格数，整体目标仍未完成。

2026-10-16 背景刷新置底试验仍失败，候选已逐字节撤回并重建。保留主窗前景检查点及3项定向测试；316帧、49变化帧、最坏12726边缘像素，首次515.37ms。完整证据、哈希、启动停止与清理见[置底试验记录](pixel-refresh-zorder-2026-10-16.md)。总体目标未完成。

2026-10-15 跳转行完整外沿：普通入口与 PyCharm 浅色活动态 584×252 窗口外侧 192 像素带共 468480 像素，RGB 差异为 0，最外圈已回到背景色；其余主题、状态及圆角内部未结案。7+4 项定向测试、两套 24 文件镜像与清理通过；完整来源、哈希、启动停止命令见 [本批记录](pixel-shadow-goto-extent-2026-10-15.md)。日常启动对齐成果保持，整体目标未完成。

2026-10-15DWM过渡单变量试验：禁用过渡仍出现内部露底，时钟定位在背景捕获请求期间；试验已撤销。恢复对照438帧仍48变化帧、首次528.88ms，不能结案。完整前后哈希、两组原图/时钟、恢复构建及清理见[过渡试验记录](pixel-refresh-transitions-2026-10-15.md)。

2026-10-15覆盖关系：新增有界状态诊断，六轮隐藏区间153–183ms、12个覆盖应答均可见置顶；428帧仍48变化帧，最坏4035像素在撤盖响应后43.76ms，整表面未通过。7项定向、Debug/Release构建和清理通过，显示逻辑未改。完整哈希、原始证据与启停见[覆盖关系记录](pixel-refresh-surface-order-2026-10-15.md)。

2026-10-15遮盖确认试验：普通入口GDI 424帧将边缘最坏变化定位到撤盖响应后30.645ms；RAF后任务候选DDA 373帧仍68变化帧、首次539.04ms，未证明收益，已撤销本批试验并重建。此前内部泄漏原帧已目视确认。6项定向及资源清理通过，整体未完成；见[本批证据](pixel-refresh-cover-task-2026-10-15.md)。

2026-10-15普通入口交付：修复 Reset/Rollback 状态样式仅测试场景生效的问题，80 组通过；更新旧 Release，两套各 24 个随包文件一致。普通/显式 main-project/Release 三路 32 状态度量一致，原图 23 对无差异、9 对仍有差异，不宣称 55 页完成。背景刷新 378 帧仍有内部露底。哈希、命令、失败及清理见[启动对齐记录](startup-alignment-2026-10-15.md)与[背景接续](pixel-background-followup-2026-10-14.md)。

2026-10-15刷新分段：隐藏加载减少两帧的试验没有证明收益，已仅撤销本批试验并重建。新增审计计时，当前首次遮盖326.50ms、背景更新185.50ms，首次总体555.35ms仍未达标；437采样帧探针无异常但有50ms间隙。命令、失败、哈希及清理见[分段记录](pixel-refresh-phases-2026-10-15.md)，整体未完成。

2026-10-15阴影接续：补严格四角比较及深浅当前版本原图，深色右下64px外侧局部通过；128px、同背景全角、圆角内侧及非活动状态仍缺证据。5项专项通过，本批实例/端口/临时profile已清理。详见[四角取证](pixel-shadow-corners-2026-10-15.md)，整体未完成。

2026-10-14终端实测：发现xterm实际约29px、现行规范22px、参考候选约18px三者不一致。已补审计度量并请求规范取舍，尚未修改产品行高。截图、专项、当前哈希和进程清理见[终端行格记录](pixel-terminal-metrics-2026-10-14.md)。整体未完成。

## 当前验收范围裁决（用户最新目标）

用户最新目标保留：55页及全部模态状态双端同态截图与差异修复、背景刷新透明帧与延迟、终端及冲突解决器逐行配对、系统阴影定量。跨显示器DPI已从本次目标移除，不作为完成前置条件；历史未验证记录保留，不宣称已通过。Windows10继续按此前用户裁决排除本次实机验收，产品支持范围不变。

用户强调只做必要测试、严禁频繁全量测试。后续按实际改动和已识别风险选择定向验证；没有新改动或新证据缺口时不重复完整回归。上述范围调整不代表其余目标已完成。

本记录接续[29项原始报告](pixel-difference-report-2026-10-13.md)和[首批整改](pixel-restoration-2026-10-13.md)。用户裁决保持PyCharm **2026.2.1**，补齐对应参考环境，并安装、集成JetBrains Mono。字体与参考版本缺口已补齐，Settings真实交互获用户确认；**55页全状态双端配对、系统阴影定量、跨屏DPI仍未完成，不能结案或宣称全部复原。Windows10已由用户排除本次验收，未验证。**

## 1. 环境与授权

2026-10-14系统阴影接续：已核查DWM/可选绘制器边界，并增加四边剖面测量工具，4项定向测试通过。实机取证遇Windows锁屏，已停止本轮参考进程并请求解锁；没有新增合格截图或阴影数值。来源、测量口径和清理见[系统阴影记录](pixel-shadow-followup-2026-10-14.md)。

2026-10-14已补真实PyCharm三栏参考与Augit真实Git冲突观察：中央初始内容、块状态和行位置存在实质差异，单块使后续正文错位88逻辑像素。证据与下一步模型整改见[冲突逐行对照](pixel-conflict-observation-2026-10-14.md)。尚未配准为合格双端截图，不计全目标完成。

用户后续明确选择“保留跨屏DPI为未验证”。本次不再以取得第二块显示器作为完成前置条件，保持未验证标注，不宣称通过；Windows10同样按既有裁决排除本次实机验收。其他目标不缩减。

真实app.py对照已发现首次加载闪现XML示例、完整文件多出补丁头正文行、缺少左右CRLF/LF标识，详见[逐行观察](pixel-diff-observation-2026-10-14.md)。目前只有内容核对，主题/面板/截图来源未同态，不能计为配对通过。

2026-10-14参考差异观察：重新启动独立PyCharm参考，打开同fixture的app.py左右差异，目视确认CRLF基线／LF工作区及第5行新增`# changed for pixel audit`。截图接口仍返回JPEG，已按真实格式保存[来源登记](../artifacts/pixel-followup-2026-10-14/reference-diff-observation/source.json)，不是无损双端配对。第一次切页截图残留旧正文，第二次观察才确认当前内容，后续不能用标签选中代替画面核验。没有据此调整产品像素。

本机WMI仅报告一块活动显示器，[当前连接记录](../artifacts/pixel-followup-2026-10-14/reference-diff-observation/monitors.json)已保存；已询问用户是否连接第二块不同缩放显示器，答复前不把跨屏标为通过或移出范围。本次隐藏启动Augit没有可见窗口，故实际端取证失败；下次使用既有可见审计驱动。PyCharm PID16940和Augit PID12904及各自子树均已通过`taskkill /PID <本次PID> /T /F`停止，54389端口无监听。

资源清理例外：自动审批拒绝了合并清理命令；拆开后进程停止成功，但对已核验绝对路径的`Remove-Item -LiteralPath 'C:/Users/wl024/AppData/Local/Temp/Augit-diff-pair-20261014' -Recurse -Force`仍被拒绝，仅给出`blocked by policy`。该目录的profile及settings.json尚未删除，不声称已清理；未改用其他工具绕过。正式截图证据保留。产品代码及四网页哈希未变化。

2026-10-14用户追加裁决：本次整体整改不要求Windows10实机验证；产品支持范围不变，仍不得宣称已验证Windows10。其余目标保持。背景刷新当前试验进展见[背景刷新试验](pixel-background-followup-2026-10-14.md)，该方案尚未完成视觉验收，不计结案。

2026-10-14新增接续：修复已打开Settings仅改变尺寸时误隐藏表单、导致输入焦点丢失；真实浅深×UI12/14/40六组通过。参考Settings本次无防火墙遮挡，但工具仍返回JPEG，不能计为无损双端配对。修前失败、修后证据、当前哈希与清理见[尺寸变化接续](pixel-modal-resize-followup-2026-10-14.md)。

- 分支`dsh`，HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`。工作区有大量先前修改与未跟踪文件，本批次只续改相关部分，未回滚、全量暂存或提交混合工作区。
- 本机Windows11 x64，build26200，系统缩放175%；未取得Windows10实机，也未执行真实跨显示器操作。
- 原`D:/JetBrains/PyCharm 2026.2.1`中的product-info实际为2026.2.3，不能用目录名冒充版本。新参考包为官方`pycharm-2026.2.1.win.zip`，product-info为2026.2.1／`262.9437.214`，SHA256为`83378EC32FD4C4090F4544038043DA157BD54A1688F071AA4791EC1A43FD72ED`。项目与配置独立；用户已手动处理安全和项目信任提示。
- 参考UI为Microsoft YaHei UI12、Zoom100%；Editor为JetBrains Mono13、行距1.2、连字关闭。已在Appearance实际核对Islands Dark及其Editor scheme，新增深色Settings和Commit/Log参考；早先未确认的切换图仍不冒充深色通过。
- Windows界面操作使用computer-use技能；没有直接PowerShell UIAutomation或自制输入助手。只读审查仅通过`dsh acp`，未使用Codex子agent；首个会话超时没有正式结果，不能登记审查通过。

## 2. D01：字体安装、分发和度量

JetBrains Mono固定为独立开源仓库的**v2.304／SIL OFL1.1**，未复制PyCharm安装包内字体。四个TTF永久安装于当前用户字体目录并注册HKCU Fonts；系统安装是用户要求的成果，不按临时文件删除。Augit和视觉稿分别分发四个WOFF2与OFL，发布目录额外保留`licenses/JetBrainsMono-OFL.txt`。

| 字体资源 | 字节 | SHA256 |
| --- | ---: | --- |
| Regular | 92164 | `a9cb1cd82332b23a47e3a1239d25d13c86d16c4220695e34b243effa999f45f2` |
| Bold | 94588 | `c503cc5ec5f8b2c7666b7ecda1adf44bd45f2e6579b2eba0fc292150416588a2` |
| Italic | 95864 | `cb6a1b246318ed3885d7dffa14a2609297fe80e9b8e500bea33b52fa312a36a4` |
| BoldItalic | 98152 | `3a013466c0eee979fb9d42c2d7a8887cd3645dc8b897cfc5b71781cf982efc5a` |

四字体合计**380768字节**，OFL另4399字节；浏览器按需加载，不新增进程或Node运行时。用途是摆脱系统缺字体造成的无声回退；替代方案为只依赖系统安装，但无法保证其他机器获得同字形。许可证、锁定版本、移除条件见`THIRD-PARTY-NOTICES.md`及`tools/runtime-assets.lock.json`。移除条件是产品不再需要随包参考字形，或替代字体在登记场景中通过同字体验收。

新配置默认JetBrains Mono，已有用户偏好保留。恢复参考外观只改草稿，Cancel不保存；资源失败仍走Cascadia Mono／Consolas／Courier New，并说明实际回退。只读正文、Diff、冲突和Markdown代码关闭连字。

CDP的`CSS.getPlatformFontsForNode`已取得`familyName=JetBrains Mono`、`isCustomFont=true`及非零glyphCount，证明使用随包资源；系统安装或`document.fonts.check`不能单独代替该证据。角色证据见[font-roles.json](../artifacts/pixel-font-followup-2026-10-13/font-roles.json)。终端仅确认xterm字体配置与加载资源，未取得canvas字形像素配对。

先前浏览器以包围盒计算的13px行高为20.5714px。参考JBR独立测量32个字号9–40：JetBrains Mono的unitsPerEm1000、hhea1020／−300／0，AWT分别取整ascent/descent，再按Java单精度乘1.2向上取整，13px得到**22px行盒、16px基线**。18px／37px必须保留Java浮点语义，得到31px／61px。文字基线用隐藏DOM探针实测后平移，不改行盒和滚动步长；Canvas在真实WebView2 DPR1.75下不能可靠代替DOM基线。

独立源为`tools/audit/PixelFontMetrics.java`与`jetbrains-mono-reference-metrics.json`，公式来自`EditorView.java#getLineHeight`；[JBR实测](../artifacts/pixel-font-followup-2026-10-13/jbr-font-metrics.json)、[32档浏览器基线](../artifacts/pixel-font-followup-2026-10-13/text-baselines.json)及真实外壳六组均通过。误命名的`browser-font-metrics-before.json`被先前脚本覆盖为修后数据，已剔除，不能引用为修前证据。

另补已选/半选焦点：内框保持16px，外环22px；未选焦点18px。两主题及UI12/14/40真实外壳验证了该几何。

## 3. C05：独立模态实施与验收边界

`ShellModalHost`创建有所有者的独立Win32 HWND，将**同一个业务WebView2Controller**重挂到最上层窗口，Bounds保留原网页视口并以负偏移裁切表单。表单DOM、草稿、监听、焦点和桥接不复制；没有改技术栈，也没有用原生控件重画正文。所有者禁用，嵌套表单分别有窗口，关闭后重挂原controller并恢复所有者。

背景由短期snapshot WebView2显示PNG，图片走内存WebResourceResponse，避免NavigateToString的2MB上限。每层只保留一个背景控制器，关闭释放controller和MemoryStream。异步捕获受关闭代际约束；创建失败清理截图，主所有者关闭前先退栈。默认窗口过程改为`DefWindowProcW`，修复ANSI入口造成辅助工具只读到单字母标题的问题。

2026-10-14补充有界图片队列：每层最多保留显示图、在途图和最新待发送图，加载较慢时合并中间版本；相同PNG不生成版本。每次响应共享只读byte[]、使用独立MemoryStream游标；新图加载后再替换旧图，onload绑定自己的版本。刷新await后再次校验关闭代际与快照身份，关闭清空所有响应和待发送图。主界面变化刷新全部背景，父表单变化只刷新所属背景及更上层，顶层草稿不捕获；文本节点按parentElement归属。移除表单执行ResizeObserver.unobserve，pagehide清集合。

`dsh acp`只读审查会话`175c5061-00eb-4470-bab7-a55f884846c2`指出慢加载积压、关闭后递增诊断版本和观察器未释放三项，均已修复。原结论见[中文审查记录](../artifacts/pixel-font-followup-2026-10-13/acp-snapshot-review.md)。真实连续刷新8次、间隔15ms最终合并到imageRevision=loadedRevision=5，变更停止后只保留109812字节当前图，顶层搜索焦点和草稿保留；父表单变化不改变主背景哈希。最终六组关闭后snapshotCount与snapshotStreamBytes均为0，见[最终六组](../artifacts/pixel-font-followup-2026-10-13/tree-origin-matrix/geometry.json)。捕获仍暂时隐藏表单，透明帧和额外renderer成本尚未消除。

消息入口解析独立为`ShellModalRequest`：只截获合法模态信封，其余请求交回现有桥接；尺寸、标题、祖先与有限矩形明确校验，畸形JSON不会从async void逸出。WM_DPICHANGED已补逻辑裁切换算、建议矩形及返回所有者时恢复缩放；显式`--dpi`／`--pixel-exact`不被覆盖。自动几何测试覆盖负坐标工作区和缩放，**不代表真实跨屏验收**。

真实六组已验证独立HWND、GW_OWNER、ownerEnabled=false、同controller父窗口、嵌套深度2、返回同父表单、最终snapshotCount0、owner恢复和无未捕获错误。系统合成图已看到独立Settings、背景不变暗和外部阴影，见[系统截图来源](../artifacts/pixel-font-followup-2026-10-13/native-inspect/system-source.json)。这些JPEG仅证明形态；CDP整页PNG不证明系统窗口阴影。

computer-use鼠标输入被工具拒绝，原因是点位位于`msedgewebview2.exe / Chrome Legacy Window`而非工具选中的owner；键盘刷新报`foreground window did not report a process id`。没有伪造窗口句柄重试。用户2026-10-13回复“确认”：搜索能输入、标题可拖到主窗外、Cancel返回正常，登记为手工证据。系统阴影定量、跨屏和所有模态逐页配对仍未验收。

当前成本和风险：每层增加短期WebView2 controller、PNG及解码内存。背景DOM变化现触发逐层PNG刷新，旧流在新图片加载后释放，关闭仍兜底释放。真实回归确认背景文本变化导致PNG哈希变化，背景控制器确认新图片已加载，Settings搜索草稿及焦点保留；连续刷新延迟仍需测量，不写成C05结案。

新增按父进程归属的单次Release抽样（`dotnet Augit.dll`、1640×900、UI12、实际DPR1）：Settings出现494ms，进程数7→8→7，工作集664530944→768249856→800186368字节，私有字节414900224→464646144→531406848。模态增量工作集约98.9MiB、私有约47.4MiB；关闭后controller/流归零但浏览器内存没有立即回落，不能宣称内存成本已消除，也不能外推到DPR1.75或长期稳定态。Debug同场景出现560ms、工作集949624832→1154646016→1287487488字节，保留差异而不混成平均值。JSON含本实例PID集合，不包含其他应用的WebView2。

首轮`dsh acp`确认呈现重入、故障清理及快照DPI三项，已修复：窗口线程同步队列串行呈现，关闭代际淘汰捕获/排队请求；逐步清理保留首故障，Dispose先标失效，snapshot流在finally释放；快照按所属窗口同步比例及Bounds，另一显示器上的所有者不随最上层改比例。首轮真实并发回归暴露SemaphoreSlim续体进入线程池导致窗口退出，已改为窗口线程同步完成队列，复跑六组通过。

复审会话`5791d4d3-8a30-477a-9513-7fe7cb4297f2`的结论逐条人工核对：Invalidate/Dispose不调用SetResult，不会自行唤醒到调用线程；现有调用仅窗口线程，另补显式线程拒绝。清理器会执行全部步骤，审查所述首异常跳过剩余释放不成立；仍补关闭重入保护及空栈Pop幂等。旧请求在调用action前即被拒绝；Close后允许新表单是必要产品行为，Dispose才永久拒绝。原结论保留供追溯，不登记为审查无缺陷。

## 4. A01／B03：树起点与原始栅格

权威`ClassicPainter.java:40,95`的左缩进为`max(controlWidth/2, Tree.leftChildIndent)`，画布16px且整数除法，因此一级步长为`max(round(7s),floor(round(16s)/2))+round(11s)`，UI12/14/40为**19/24/62 CSS px**。首批7+11=18的解释遗漏画布半宽下限，不能沿用。项目树默认首级23px、第7级137px。

Changes此前仍把展开图形缩到8px，并用7px列表内距、7px行内距和多个flex gap凑renderer起点。现按`Tree.border=4,12,4,12`保留左右12px；首级renderer偏移19px，第二级38px。16px展开画布独立放在renderer之前；不改变复选框24px画布与16px外框。

| 原始PNG局部组件 | PyCharm2026.2.1 | 当前Augit | Augit−参考 |
| --- | --- | --- | --- |
| 分组蓝框 | `[135,222,28,28]` | `[133,222,28,28]` | x−2px，尺寸一致 |
| 文件蓝框 | `[168,264,28,28]` | `[166,264,28,28]` | x−2px，尺寸一致 |
| 子框相对分组 | 33px | 33px | 0px |
| 行步长 | 42px | 42px | 0px |

前一版当前框为x131/165、相对34px；本批次消除1px相对差。比较未缩放、未重采样，量测脚本为`tools/audit/measure-reference-components.py`，两图SHA256和矩形见[组件量测](../artifacts/pycharm-reference-2026-10-13/commit-components-metrics.json)。参考整窗2430×1637、当前2872×1577，窗口与面板尚未完整配准；不能把局部0px写成整页通过。分组计数另按VcsBundle的No files／1 file／N files订正；根文件路径按VcsPresentablePath的forceRelativePath显示仓库名，嵌套目录使用Windows反斜杠。真实fixture断言1 file与fixture通过，静态和运行时共用分组函数；完整文字墨迹仍需配对。

## 5. D02／D03与参考缺口

2026-10-14逐页复核：[55页配对现状](pixel-pair-inventory-2026-10-14.md)明确区分参考候选、自身一致性与合格双端配对；现有来源不能证明任一整页无损同态配对完成。新增verify-reference-pair门禁及负向测试，不把门禁通过当像素通过。

截图按实际DOM可见特征、页面、主题、字号、DPR、输入与SHA256登记，`screenshot-provenance.cjs`拒绝缺场景特征或特征不可见的截图。历史错误命名的Clone／Rollback／Branches图保留并排除，不能按名字充当通过。

Controller重挂后，默认CDP surface截图可能读到旧主窗背景；DOM存在和PNG哈希不同仍不能单独证明Settings已进入画面。`indent-refresh-matrix`与`refresh-release`中的Settings图因此排除，不计截图通过，历史geometry保留错误。驱动现用`Page.captureScreenshot{fromSurface:false,captureBeyondViewport:false}`，返回view客户区，右侧/底部黑色余量不作系统阴影证据。`view-capture-matrix`、`whole-commit-matrix`、`bounded-final-matrix`、`tree-origin-matrix`和`tree-origin-final`取得实际Settings正文；最新单组已去除回归临时添加的“父层刷新”标题文字。系统截图被防火墙／OneDrive遮挡的区域同样排除，不能用作精确配对。

参考JPEG已覆盖Settings字体与外观、文本、Commit/Changes/Diff、Git Log与详情、Remotes空态/Define、Push无远端、Stash、Clone等状态，见[参考来源索引](../artifacts/pycharm-reference-2026-10-13/source-index.json)。仍缺全部55页的同数据、同焦点、同视口、双主题成对PNG；不把未知差值填0，不以Augit自身220图代替PyCharm图。

### 5.1 Settings大字号表单与独立滚动补验

UI40真实PNG发现固定110px标签列把Theme文字推入选择框，70px字号框不能完整容纳40。正文现在按自身宽度与字号在48em以内使用单列，24em以内让字体选择独占一行；字号框用`max(70px, 3ch + 26px)`。标签Range包围盒须在自己的列内；数字按实际字宽、左右内距、边框和16px原生步进槽验收，不用无依据的100px门槛。首版100px断言失败时实际96.359px，修正为字宽预算后通过。

滚动补拍进一步发现隐式auto网格行被左侧分类撑成526.286px，超过405px可用高度；`dialog-body.scrollTop`变为113.143px，连左侧搜索也被右侧字段带走。现明确`minmax(0,1fr)`行列，左侧搜索与右侧面包屑固定，分类树和设置正文分别滚动。权威`SettingsEditor.java:379–390`分别在左右BorderLayout.NORTH放置搜索和抬头，`SettingsTreeView.java:205`单独建立分类滚动窗格。初版独立滚动遗漏列的最小内容宽度，目视发现面包屑撑宽、选择框右缘裁切；已补`minmax(0,1fr)`列与抬头范围/横向scrollLeft断言，未登记该中间版本为最终通过。

最终UI40两主题主布局和左右面板均为405px，主体scrollTop保持0，面包屑顶边118px不变；字体正文自身滚动，40和底部按钮完整可见。图为[浅色字体区](../artifacts/pixel-font-followup-2026-10-13/settings-final-matrix/settings-fonts-Light-40.png)、[深色字体区](../artifacts/pixel-font-followup-2026-10-13/settings-final-matrix/settings-fonts-Dark-40.png)，度量见[最新六组](../artifacts/pixel-font-followup-2026-10-13/settings-final-matrix/geometry.json)。`settings-scroll-before`是发现联动的修前证据；`settings-fixed-regions-matrix`是未修列宽的中间证据，仅供失败追溯。

每份正式模态PNG先以内存三色标记验证实际客户区的预期位置，三块内部必须精确为`#E91159/#0DE3AD/#472BF1`；移除标记、等待两帧后重采正式图，不在全图搜色凑通过。Python子进程固定`PYTHONUTF8=1`并用`-B`，中文JSON不能乱码，也不新建字节码缓存。核验3项测试含旧主窗和错位置负向；设置12个组合另注入旧auto行高，必须检出内容撑高；第一次负向要求主体一定滚动，在保留新正文容器时过强，改为直接检查布局内容高超过可见高，正向仍要求主体scrollTop=0。

最终设置三状态×两主题×UI12/40共12组合（含负向）通过，受影响48组合也通过；实时设置/Diff10项和设置状态12项通过，最新真实浅深UI12/14/40六组通过。新结构未改变草稿、Apply/Cancel和窗口归属。全部数值属于Windows11、DPR1.75的Augit真机验证，仍非55页PyCharm同态像素结案。

## 6. 分批验证与哈希

| 验证 | 当前结果 |
| --- | --- |
| Shell构建 | 0警告／0错误 |
| ShellModal几何、请求、并发、故障释放与图片专项 | 16通过；含4项图片版本／有界队列测试 |
| Shell完整模块 | 最新181通过，16秒；图片生命周期模块完成触发；历史180、177不当作当前计数 |
| 真实WebView2浅深×UI12/14/40 | 最新6组通过，含直接并发/关闭竞态、有界背景刷新且草稿焦点保留、字体22/16、树19/24/62、16px展开画布和嵌套释放；移除测试标题后单组补验通过 |
| live-shell --restoration-fixes-only | 42通过 |
| live-shell --goal-conflict-only | 前序51通过；10,485,078字节尾部编辑208ms／总2282ms |
| verify-pixel-remediation --all | 全局树缩进变化后224组合通过；本次Changes起点与展开画布局部变更另跑48受影响组合通过 |
| live-shell --pixel-remediation-only／Changes连续工作流 | 最新9项通过／6组悬停与Changes→Diff通过 |
| 项目树／筛选弹层回归 | 本接续12／19项通过；本次未修改其结构而重复全量 |
| native-modal-state | 2项通过，覆盖文本节点归属与混合拓扑变更 |
| Settings表单、固定搜索/面包屑与独立滚动 | 受影响48组合、最终12组合含负向；设置/Diff10项、设置状态12项、最新真实六组通过；模态PNG内容核验3项通过 |
| 共享UI/字体镜像、JS语法、git diff --check | 通过；最终门禁另登记 |
| CSS角色字重检测专项 | 3通过；字体face的700不计为UI角色，真实按钮700/500仍检出 |
| 发布镜像与锁定资产 | 最新Release publish成功，web21文件镜像通过；artifacts发布目录exe单组真实回归通过，失败路径仍保留 |

真机路径新断言初次失败为`innerWidth×DPR`的逻辑像素取整差：controller2870物理px、innerWidth1641；修为至多一个逻辑像素的舍入容差，继续严格断言父窗口与原视口范围，不将它误记作controller尺寸变化。

| 文件 | 接续开始MD5 | 当前MD5 |
| --- | --- | --- |
| mockup.js | `F3F10BBE90C71AA706A042B157078EAC` | `3EEACB9FE4AA6B29CBE0F3495E426CC5` |
| mockup.css | `8DDC944FDB1E4FEEFB16E6A5AFAA244C` | `5C1D95FC4479C3DC8BB22D5869324890` |
| live-data.js | `8A11E9CF52924FE8C14D978BC8ABF24D` | `F9F6922B29C334F35C9884A536BA3A35` |
| bridge.js | `F297BF8769E249CA0D804F4F4EEEF5A9` | `F297BF8769E249CA0D804F4F4EEEF5A9` |

2026-10-14本轮修改前四文件MD5为`2B9E6993A80CEF403722B063C7A52BA0`、`10BF6448B240588B426BDF77420C1D08`、`F9F6922B29C334F35C9884A536BA3A35`、`F297BF8769E249CA0D804F4F4EEEF5A9`。本批次文件为共享mockup.js/css、live-data.js、native-modal-state.js、ShellModalHost.cs、ShellSnapshotImages.cs及对应测试／审计驱动／模块文档。未暂存混合改动。

Settings接续修改前js/css为`CF4F60822A2ED91E4B7C0B98B8C7FFCE`／`E85736ACD41F7CF74CF7DFD93583E5F5`；后为上表当前值。Changes和背景批次的中间CSS值`3A940B27D6FB77B3B7058F53DE94E48C`保留追溯，不能代替最新文件。新增修改范围为共享Settings HTML/CSS、两个像素审计驱动及模态PNG核验；测试耗时约8秒（12组合）、9–10秒（两组）、约30秒（六组），各实例PID、随机端口及临时profile由驱动逐条输出并清理。此前Release数据属于有界队列阶段快照，未包含后续Settings布局；不以它宣称当前最终发布性能通过。

最终Release单组为同fixture、1640×900、UI12、DPR1.75，Settings出现360ms，进程7→8→7；工作集835174400→991625216→1220009984，私有582062080→681046016→953847808字节。模态增量约149.2MiB工作集、94.4MiB私有；关闭值在连续刷新和嵌套之后采集，PNG／controller归零但浏览器缓存未立即回落。不能与DPR1、不同fixture的早期值作直接前后比较，不能宣称轻量目标达成。按PID22596父进程树归属，见[最终发布抽样](../artifacts/pixel-font-followup-2026-10-13/bounded-final-release/geometry.json)。前一版Release226ms和约200MiB增量是有界队列前数据，单独保留。

初次live-shell启动未指定浏览器而寻找不存在的chromium_headless_shell-1234，设置AUGIT_BROWSER_EXECUTABLE后9项通过。首轮真机UI40的树起点断言遗漏列表横向scrollLeft；修正量测坐标归属后六组通过，未修改产品几何以迁就断言。C#格式检查、JS语法、共享9资产及git diff --check通过。

## 7. 复验、启动与停止

```powershell
dotnet build src/Augit.Shell/Augit.Shell.csproj --no-restore
dotnet test tests/Augit.Shell.Tests --no-build --no-restore
$pixelPlaywright = 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$env:AUGIT_BROWSER_EXECUTABLE = 'C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/run-pixel-remediation-shell.cjs $pixelPlaywright
node tools/audit/verify-pixel-remediation.cjs $pixelPlaywright $env:AUGIT_BROWSER_EXECUTABLE artifacts/pixel-font-followup-2026-10-13/settings-final-scenes --settings-only
node tools/audit/live-shell.spec.cjs $pixelPlaywright --settings-diff-only
node tools/audit/live-shell.spec.cjs $pixelPlaywright --settings-state-only
python -B -m unittest discover -s tools/audit -p test_modal_raster.py
node --test tools/audit/css-role-weights.test.cjs tools/audit/modal-process-memory.test.cjs
node tools/audit/live-shell.spec.cjs $pixelPlaywright --restoration-fixes-only
powershell -NoProfile -File tools/audit/verify-ui-assets.ps1
# 两分钟观察窗口，测试实例和profile由finally关闭；不启动常驻服务器。
node tools/audit/run-pixel-remediation-shell.cjs $pixelPlaywright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/pixel-font-followup-2026-10-13/native-manual --inspect
# 异常中断只停止驱动输出的本次PID及其后代。
taskkill /PID <AUDIT_PID> /T /F
# 手动启动与停止；变量只归属本次实例。
$pixelApp = Start-Process -FilePath './src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit' -PassThru
taskkill /PID $pixelApp.Id /T /F
# 参考环境只影响本次启动，不改用户原PyCharm。
$pixelOldProperties = $env:PYCHARM_PROPERTIES
try {
  $env:PYCHARM_PROPERTIES = 'D:/JetBrains/PyCharm-2026.2.1-reference/reference.properties'
  $pixelReferenceProcess = Start-Process 'D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe' -ArgumentList 'D:/JetBrains/PyCharm-2026.2.1-reference/fixture' -PassThru
} finally { $env:PYCHARM_PROPERTIES = $pixelOldProperties }
taskkill /PID $pixelReferenceProcess.Id /T /F
```

本批次测试驱动均按finally退出并删除隔离profile，未启动常驻服务器。字体TTF与正式截图/JSON是成果，保留。参考安装包、隔离配置及fixture保存于`D:/JetBrains/PyCharm-2026.2.1-reference`，product-info再次核对2026.2.1／262.9437.214，路径同步至reference.properties及recentProjects。

正式路径启动PID20416再次出现Windows防火墙提示，本轮再观察仍未消失；前序临时路径的安全／信任确认不证明本次提示消失。[computer-use指引](C:/Users/wl024/.codex/plugins/cache/openai-bundled/computer-use/26.1007.21434/docs/guidance.md)明确要求“Do not act on security or privacy permission requests”，因此未操作，也未将遮挡画面计作像素证据。2026-10-14本批次结束按已核实路径的PID20416及其参考子树停止；taskkill返回成功后的首次CIM检查仍短暂显示主进程，重新查询确认已退出。用户原PyCharm和OneDrive未停止。完整配对仍需要下一次参考启动时由用户处理安全提示及遮挡。

Settings最终六组PID22884／23868／17656／23516／7836／19480均由finally退出并删除对应临时profile；本轮之前两组补验和中间六组也已逐项退出。Debug最终构建0警告/错误，21文件输出镜像、9共享资产、JS语法、16脚本编码、文档声明和git diff --check通过；历史2373项全量断言快照仍过期，不作为本次实测。没有常驻测试服务器或浏览器残留。

自动审批拒绝批量删除本轮临时发布目录，返回仅为`blocked by policy`；前序旧参考临时根目录删除也曾被拒绝。没有换工具或拆分同一动作绕过。待清理清单为`.tmp/pixel-{font,final,background,verified}-publish`、`.tmp/pixel-publish-probe`、`.tmp/pixel-{followup,native,native-followup,snapshot}-acp.cjs`、`.tmp/PixelFontMetrics.java`、`.tmp/pixel-code-metrics.cjs`、`artifacts/pixel-publish-probe-2026-10-13`、`artifacts/pixel-refresh-publish-2026-10-13`、`artifacts/pixel-bounded-final-publish-2026-10-14`以及系统临时目录下旧参考的fixture／log／system。未删除整个.tmp，其中存在用户与前序其他任务文件。测试实例和profile已由驱动清理，静态证据及正式字体保留。

2026-10-14终端接续：ANSI16色及主题切换后终端节点保留已整改，定向验证通过，本批进程与临时目录已清理。前后哈希、证据、启动/停止命令见[终端记录](nui-behavior/19-terminal.md)。仍未完成PyCharm逐行配对，不计整体完成。

2026-10-14复用遮盖显示等待批次：新增显示后两次渲染回调确认，定向8项Shell测试、脚本测试与真实单组流程通过；首次409.69ms、后续五次363.44–376.42ms。未证明系统前景无透明帧，详情见[背景刷新记录](pixel-background-followup-2026-10-14.md)。本批进程及临时资源已清理。

2026-10-14右来源行旁顺序批次：实机发现并修复为行号、接受、忽略，单块/多块定向验证通过。前后哈希、截图与清理见冲突逐行对照记录的右来源行旁操作顺序节。精确图标和槽宽、完整双端配对仍未完成。

2026-10-14行旁动作自绘批次：字体符号替换为14px独立图形，真实多块定向验证通过；完整来源、前后哈希、失败修正、清理及未验收状态见冲突逐行对照记录的行旁动作自绘图形节。

2026-10-14追加顺序与取证纠错：修复先右后左的操作顺序，补单侧接受后的下折箭头和图标色；9项模型及真实单块通过。旧surface截图已排除，新客户区PNG通过三色像素核验并目视确认。前后哈希、证据、命令、清理与未验收范围见冲突逐行对照记录的“按操作顺序追加与截图纠错”节。整体整改仍未完成。

2026-10-14浅色取证补修：结果编辑区的浏览器默认黑色外框已移除，保留文字光标与按钮焦点。真实聚焦/追加短流程及有效PNG目视通过；CSS当前MD5为9DB3E0F524AC6747B4FFD962421F5CA6。详见冲突逐行对照记录的“浅色结果区默认焦点框整改”，不能据此宣称完整双端像素复原。

2026-10-14行号槽处理状态已接入：未处理完整底色、已处理点线及空范围显示层；真实浅色单块和深色多块的焦点、滚动、精确保存通过。前后哈希、命令、清理及未验收范围见冲突逐行对照记录“行号与动作槽的处理状态绘制”。完整双端配对、阴影和前景透明帧仍未完成。

2026-10-14背景刷新合并批次：消除遮盖准备期间同一变化造成的重复捕获，修前2次、修后1次；捕获结束后的新变化仍产生下一批，真实边界验证通过。live-data当前MD5为DB52470B2BAB434DE4E463086A39FA6D，前后证据、采样限制、清理与启动/停止命令见背景刷新记录“遮盖准备期间合并背景变化”。未取得系统前景逐帧证据，不宣称整体完成。

2026-10-14系统阴影批次：原始系统PNG双主题对照发现CS_DROPSHADOW叠加DWM的右下4物理像素重复投影，已移除；浅色近边最大暗化差94/79降至1/0，深色0/0，构建及双主题真实专项通过。四角、非活动与背景栅格干扰仍未验收；证据、命令、哈希及两个被审批拒绝删除的隔离目录见阴影取证接续记录。整体整改仍未完成。

2026-10-14快照物理像素整改：确认CapturePreview实际2452像素被压到2450像素，改为按PNG真实宽高和捕获比例呈现；深浅主题同位置文字带各156个差异像素降为0，真实更新/嵌套尺寸及关闭清理回归通过。完整证据、命令、哈希和未验收边界见[pixel-snapshot-sizing-2026-10-14.md](pixel-snapshot-sizing-2026-10-14.md)。整体整改未完成。

2026-10-14系统录屏批次：修前425帧中36帧真实露底，独立遮盖子窗及DWM呈现等待后深/浅417/432采样帧探针均无异常；仍有34ms间隙及浅色重复时间戳，不能宣称完整逐帧通过。首次录屏负载延迟572/574ms仍超目标。184项Shell及定向真实生命周期回归通过，本批进程/端口/profile已清理。完整失败迭代、命令、证据、哈希及未验收项见[系统录屏整改](pixel-refresh-screen-2026-10-14.md)。整体目标保持未完成。

2026-10-14普通文本取证：取得同夹具、同尺寸双端原始PNG，第二三行墨迹间距均38物理px，但字串宽度与栅格仍不同；JBR分数度量开关已定位为待确认因素。配对门禁补拒绝同文件和复制图，9项定向测试通过。证据、失败尝试、未验收边界与清理见[普通文本记录](pixel-text-pair-2026-10-14.md)。55页合格同态配对仍未完成；产品四网页哈希不变，未暂存或提交。

2026-10-14正文字符步长整改：参考Subpixel模式与256组独立JBR度量确认整数设备字宽，修正共享正文、Diff、Blame、冲突及嵌套高亮字距。原图两行宽度差从−2/−3降至+1/0物理px，抗锯齿仍不同。48组合、26项查找归属、51项冲突回归通过；失败迭代、当前哈希、启动停止和清理见[普通文本记录](pixel-text-pair-2026-10-14.md)。完整双端同态配对仍未完成。
2026-10-14隐藏遮盖准备并行化：PNG捕获与隐藏子窗控制器创建重叠，补齐关闭销毁父窗导致的过期创建失败处理；七项定向及191项Shell通过。深/浅首次观测519.49/506.24ms仍超目标，434/438采样帧探针无异常但最大间隙50ms，不能宣称完整无闪隐。失败迭代、实现、命令、哈希和进程/端口/profile清理见[并行准备记录](pixel-refresh-parallel-2026-10-14.md)。整体仍未完成。

2026-10-14冲突中央行号槽与字体：中央默认缩至43px，不再预留来源动作空间；修复font简写为空导致三栏编号回退界面字体。24组定向及真实深色多块/浅色短流程通过。原始参考PNG、前后哈希、命令、清理和未完成范围见[行号槽记录](pixel-conflict-gutter-2026-10-14.md)。整窗结构与55页双端配对仍未完成。

2026-10-14三栏连接带：新增两条24px只读曲线，按稳定块ID跟随当前草稿、左右处理状态和滚动，已处理侧点线、空侧双边界。真实多块/浅色/末尾空侧及72组布局专项通过；旧字体期望和截图坐标失败已记录。来源、最终覆盖边界、哈希、启动停止及清理见[连接带整改](pixel-conflict-connectors-2026-10-14.md)。完整双端配对仍未完成。

2026-10-15非活动阴影批次：采集器增加活动状态、前景身份与遮挡边界检查；真实5项、角部6项定向测试通过。深色右下64物理像素角区12288像素背景完全一致，最大通道差1级RGB；其余角及128范围背景不同，整体仍未通过。深色双端活动／非活动四边统计相同，但不等于逐像素通过。证据、失败迭代、哈希、启动停止与清理见[非活动阴影记录](pixel-shadow-inactive-2026-10-15.md)。Windows 10排除本次验证，跨屏DPI保留未验证，整体目标未完成。

2026-10-15终端用户裁决已执行：xterm基础行距1.0替代1.7，规范和共享静态终端同步；175% DPI下默认行格29.1667→17.1818逻辑px，13→17→13及深浅主题真实专项通过。此前“待裁决”均为历史状态。证据、哈希、失败迭代、启动停止和清理见[终端行距整改](pixel-terminal-lineheight-2026-10-15.md)。双端逐行配对与整体整改仍未完成。

2026-10-15整表面刷新批次：逐采样帧检查全部2440620像素，发现三探针遗漏的首图放大错位、圆角差异与首轮边框闪亮。首图改为导航前按PNG真实尺寸定尺，修后深浅样本未再出现放大错位；边框闪亮、圆角差异及首次超500ms仍未解决。13项相关Shell、3项脚本、4项分析算法及真实生命周期专项通过；未重复全量测试。证据、哈希、启动停止与清理见[整表面取证与首图尺寸](pixel-refresh-surface-2026-10-15.md)。不得将历史三探针通过解释为整表面或全部显示帧通过，整体目标仍未完成。

同日边框接续：撤盖前DWM等待无收益；屏蔽WM_NCPAINT虽令深色单份录像零差异，却造成浅色露底及系统阴影削弱，因此全部撤销。整表面工具增加严格失败退出，5项算法/门禁测试通过；恢复构建0警告0错误。上述整表面记录已补失败证据、清理及手动启动/停止命令；边框和完整呈现仍未解决，不计新增产品验收通过。

2026-10-15自然终端配对：修复父进程重定向标准句柄导致Shell提前退出，ConPTY4项及真实自然横幅通过；两行相同文字宽差仍为−6/−21物理px，完整终端与55页同态配对仍未完成。原始PNG、失败追踪、哈希、启动停止及清理见[自然横幅配对与句柄修复](pixel-terminal-pair-2026-10-15.md)。

2026-10-15终端格宽批次：新增锁定xterm DOM格宽补丁并修复全局零字距覆盖，自然横幅两行宽差−6/−21最终降至0/0物理px。48组浏览器网格/光标/选区/鼠标命中、256组JBR最大字宽及真实主题/字号/字体切换专项通过；行基线、抗锯齿与完整终端配对仍未完成。来源、失败迭代、哈希、启动停止及清理见[终端格宽记录](pixel-terminal-grid-2026-10-15.md)。整体目标未完成。

2026-10-15终端基础行格接续：随包字体以参考AWT基础高度驱动静态与实时终端，13/17号字为18/24逻辑px。新增相同字形逐行分析；真实17号字九类字母步长双端均42物理px、两行宽高一致，13号字仍32对31，未通过完整配对。48组合与真实设置专项通过，进程/profile已清理；依据、失败边界、哈希及命令见[行格配对记录](pixel-terminal-row-grid-2026-10-15.md)。整体目标未完成。

2026-10-15导航并行接续：隐藏快照文档导航与PNG捕获并行，首图及显示后两帧保护均保留。最终首次深色514.08ms、浅色354.13ms，边框/圆角严格门禁仍失败；14项Shell、4项脚本及固定夹具真实单组通过。试验回退、证据、哈希和启动/停止清理见[导航并行记录](pixel-refresh-navigation-2026-10-15.md)。整体整改未完成。

2026-10-15遮盖合成定位：新增源PNG与三份同位置系统图对照，源图不透明、底层隐藏前后哈希相同；初始角点仍有两类状态，不能归因或判定通过。重绘隔离、像素采样与主题底色试验均撤销，6项定向测试及实际取证通过。证据、失败和启停清理见[遮盖合成记录](pixel-cover-composition-2026-10-15.md)。整体目标未完成。

2026-10-15冲突三状态逐行接续：双端取得初始/接受左侧/全部处理系统原图，参考完成提示遮挡中央第二行。正文和编号槽改为设备像素量化粗点线，175%主体周期修正为着色4/留白3；上下厚度、抗锯齿与整窗结构仍不同。六组点阵、实际浅色/空侧/深色多块专项通过；证据、哈希、失败及启停清理见[三状态记录](pixel-conflict-line-pair-2026-10-15.md)。整体目标未完成。

2026-10-15冲突边界矢量接续：渐变与图案贴图会损失边缘像素，改用只读SVG直接路径；实际175%上4排/下3排与参考一致，两段局部平移配准共3000像素零差异，不等于整窗同态通过。12组严格栅格及真实单块/多块/空侧保存通过。证据、失败、哈希与清理见[三状态记录](pixel-conflict-line-pair-2026-10-15.md)。整体目标仍未完成。

2026-10-15终端相位分析：新增JBR/浏览器20行重复字形夹具，8份样本最大累计漂移0.5物理px；实机0.25/0.5px文字偏移改变两行步长，但基线差补偿和原点取整均未稳定修复，因此未写入产品。4项分析器测试及真实取证通过，完整配对仍未完成。证据、失败与启停清理见[终端相位记录](pixel-terminal-phase-2026-10-15.md)。

2026-10-15用户批准冲突整窗重排：紧凑工具栏、固定文件标题、左对齐来源、单层底栏已落实并同步规范。72组布局、51项模块回归和真实保存通过；最终三状态仍有宽度/槽位/墨迹差异，不计55页同态通过。证据、哈希、启动停止与清理见[冲突布局接续](pixel-conflict-layout-2026-10-15.md)。浅色活动阴影下方两角64px通过，上方受分隔线背景影响仍未通过，详见[同背景阴影](pixel-shadow-uniform-2026-10-15.md)。整体目标仍未完成。

2026-10-15同尺寸来源槽接续：实际与参考统一为1730×1215，修正来源编号宽度、动作间隔、编辑器底色及槽边界。72组布局和24组多位编号专项通过，真实三状态、空侧保存及多块滚动通过。完整像素配对仍未通过，证据、前后哈希、失败与启停清理见[来源槽记录](pixel-conflict-source-gutter-2026-10-15.md)。

2026-10-15整行高亮接续：修复中央/右栏末端白缝和横滚后短行底色缺口，保留参考左18px边距与文字原点。16组PNG/滚动专项、51项冲突模块、72组布局及12组边界栅格通过；最终四个原图采样点与参考相同，仍有2物理px垂直差异。失败迭代、哈希、启停清理见[整行高亮记录](pixel-conflict-row-fill-2026-10-15.md)。整体目标仍未完成。

2026-10-15父窗裁剪试验：WS_CLIPCHILDREN未消除深浅边框/角点变化，已撤销并恢复构建。审计清单新增首次/复用遮盖身份，4项测试与真实记录通过；恢复后明确首次540.18ms，428帧中45帧变化，严格整表面仍失败。证据、失败和启停清理见[首次身份记录](pixel-refresh-cover-identity-2026-10-15.md)。整体目标未完成。

2026-10-15源图与原生框架隔离：真实WebView2源图与三种系统状态在内部区域逐像素一致，边缘仍不同。零框架扩展虽445采样帧稳定却丢失阴影；单底边扩展保留近边阴影但深浅仍闪亮，两个试验均已撤销。3项诊断测试及恢复构建通过，证据与启停清理见[框架隔离记录](pixel-refresh-frame-isolation-2026-10-15.md)。不放宽整表面验收，整体仍未完成。

2026-10-15直边/圆角分离：父窗背景擦除仍失败；禁用DWM边框的单份浅色最严重帧由3773级别差异收敛到134个角部像素，直边/内部为0，但完整门禁和正常边框外观未通过，试验全部撤销。3项分区测试、恢复构建和清理通过，证据见[直边与圆角记录](pixel-refresh-border-corners-2026-10-15.md)。整体目标未完成。

2026-10-15来源标题接续：补齐标题底边框占高和连续横线；三行原图墨迹边界与参考一致，原生横线仍高1物理px，未宣称整窗通过。20组专项、72组布局、16组行填充和真实多块精确保存通过。前后哈希、失败与启停清理见[来源标题记录](pixel-conflict-title-separator-2026-10-15.md)。整体目标仍未完成。

2026-10-15来源横线绘制视口接续：初始态固定区域3452像素与参考一致，正文三行边界保持一致；接受后仍因合成出现混色，三状态完整差异已量化，不能宣称整窗通过。20组全厚度专项、72组布局、51项模块及真实深色多块保存通过。失败、当前哈希、启停清理见[来源标题接续](pixel-conflict-title-separator-2026-10-15.md)。整体目标未完成。


2026-10-15编辑光标合成接续：已排除背景刷新，确认居中变换令光标出现后的重叠层带半物理像素偏移；冲突窗口改用布局居中。60组栅格、72组布局和真实多块精确保存通过；原生三状态横线各3452像素颜色稳定，整窗仍有差异。哈希、失败试验、证据与启停清理见[来源标题记录](pixel-conflict-title-separator-2026-10-15.md)。整体整改未完成。


2026-10-15无遮挡三状态接续：新采PyCharm与Augit同尺寸、中央首字符焦点、无鼠标光晕和完成提示的原始PNG。分隔线三状态均3452像素一致，中央/右栏横偏2/4物理px、第二行高1px及槽边界差异仍在。实际字形已证实使用JetBrains Mono，参考逐字形回退证据仍缺，严格门禁拒绝，未增加55页合格数。两项比较器测试与真实取证通过；命令、原图、限制、哈希和清理见[无遮挡配对记录](pixel-conflict-clean-pair-2026-10-15.md)。整体目标未完成。

2026-10-15冲突运行时几何批次：整数分栏、3px正文留白和INDENT_GUIDE槽线颜色已修正，24组槽位及72组布局通过；参考进程清理命令被自动审批拒绝，PID21368与专用临时目录仍在。全量整改未完成，详情见[本批证据及手动清理命令](pixel-conflict-runtime-geometry-2026-10-15.md)。

2026-10-15客户区/底栏批次：按实机Insets修正不占位外框、42px工具栏、30px来源标题及独立按钮尺寸，底栏背景起点与参考同为1127物理px；72组布局、60组横线栅格、真实多块精确保存及2项比较器测试通过。整窗与文字仍有差异，全量目标未完成。前批参考PID21368现已不存在，历史临时目录仍在且未重试拒绝清理。哈希、证据和启停命令见[本批记录](pixel-conflict-client-area-2026-10-15.md)。

2026-10-15背景刷新接续：WS_EX_COMPOSITED父窗试验深色仍闪亮，已撤销并重建恢复。三组原始录像全帧分区证明角部变化反复出现、两组直边变化各仅一帧，内部均0；整表面仍失败且首次仍超500ms。保留分区与连续段审计、11项定向测试通过。哈希、时序限制及清理见[本批记录](pixel-refresh-composited-2026-10-15.md)，整体目标保持未完成。

2026-10-15刷新时钟关联：新增原始采集/编码链与桥接墙钟核对，深浅两组951采样帧仍有91变化帧；浅色直边闪亮在首次撤盖响应后40.942ms，圆角更早发生，不能据此确定根因。13项定向测试通过，未修改产品或宣称延迟达标。哈希、证据、启停及清理见[时钟关联记录](pixel-refresh-clock-2026-10-15.md)，整体目标未完成。

2026-10-15冲突正文尾部：按实机Insets将中央/右来源末尾留白12改18px，并同步规范与共享资产。24组整行像素/横滚（含最右端）通过，真实多块精确保存通过；最终三状态完整差异与前批一致，文字原点偏移尚未修复。哈希、证据与启停清理见[尾部留白记录](pixel-conflict-end-inset-2026-10-15.md)，整体目标未完成。

2026-10-15冲突文字合成排查：运行时支持子像素字体，但去圆角、不透明根、隐藏scrim及独立文字层均未恢复参考彩色边缘；独立层还改变文字像素，未保留产品试验。两项六模式/失败恢复流程通过，环境、原始图、启停与清理见[文字合成记录](pixel-conflict-text-composition-2026-10-15.md)。整体目标未完成。

2026-10-15冲突正文LCD整改：运行时追踪锁定透明scrim合并层contents-not-opaque；正文独立栅格化且不加will-change后恢复彩色子像素边缘。24组行填充/横滚、60组横线、真实多块精确保存及4项审计流程通过，坐标和整窗仍未复原。证据、哈希、启停及清理见[本批记录](pixel-conflict-lcd-text-2026-10-15.md)，整体目标未完成。

2026-10-15冲突光标行号：三栏各自保留当前逻辑行，编号浅色#767A8A、深色#A1A3AB，失焦后保留；选择活动端、插入/删除及编号重排同步，空白对齐占位不编号，不改变正文与保存内容。20项定向检查及真实多块保存通过，整窗仍未配对，整体目标未完成。依据、哈希、证据和启停清理见[光标行号记录](pixel-conflict-caret-numbers-2026-10-15.md)。

2026-10-15完整阴影外沿：新增同尺寸八区逐像素比较，浅色活动态128px外沿覆盖774656像素；下方两角各16384像素暗化量差0，其余区域仍有背景不一致，完整阴影未通过。五项定向测试通过，最终采集复跑被前景门禁拒绝并清理。证据、哈希与启停见[本批记录](pixel-shadow-ring-2026-10-15.md)，整体目标未完成。

2026-10-15跳转行实机接续：取得参考预填/空输入原始PNG，修复真实入口Go to与视觉稿OK的文案漂移，七项真实行为回归通过。参考584×252、实际宿主700×288物理px，按钮顺序与现行规范冲突已请求裁决；两次实际截图被前景门禁拒绝，未增加55页合格配对数。证据、哈希与启停清理见[本批记录](pixel-goto-line-2026-10-15.md)。

2026-10-15跳转行前景诊断接续：已取得实际700×288原始系统PNG，前后前景身份/DPI/边界稳定且目视无遮挡，七项行为通过；拒绝诊断新增双方句柄与PID并由三项定向测试覆盖。尺寸、按钮顺序仍不同，布局仍待裁决，未增加55页合格配对数；一次成功不代表失焦根因已修复。证据与清理见[跳转行记录](pixel-goto-line-2026-10-15.md)。

2026-10-15冲突绘制上下文：取得三栏九个布局片段实际字体、8px步长与LCD整数度量证据。隔离精确基线试验使两侧首行纵向对齐，部分后续行仍有量化差异，未改产品。新参考中央有鼠标光晕，未增加合格配对；临时代理目录清理被自动审批拒绝并保留。详见[绘制上下文记录](pixel-conflict-paint-context-2026-10-15.md)。

2026-10-15冲突正文基线整改：leading经CSSStyleDeclaration规范化后传给所有行，真实三栏基线由15.991变16，保留行盒和正文。15组基线/新增行、24组行填充与横滚、真实多块编辑前后基线及精确保存通过；三状态正文差异分别降至25096/25691/28983，仍未完整复原。哈希、原图、限制和启停清理见[基线整改记录](pixel-conflict-leading-2026-10-15.md)。

2026-10-15终端八行实机接续：两端七个相同字形步长均为32/31交替，最大累计偏差0.5物理px，本样本无累积行距漂移。原点恒差22px、字形像素差及实际中部鼠标光晕仍在，不计55页合格配对；产品行高不变。四项分析器测试通过，本批进程、端口、临时目录独立检查已清理。原图、量化、哈希和命令见[八行实机记录](pixel-terminal-live-rows-2026-10-15.md)，整体目标未完成。

同批补采：系统鼠标移出终端后，完整八行文字区域无遮挡，双端各行墨迹框均178×21物理px、步长一致；焦点、窗口、背景及RGB差异仍未消除，不计完整配对。详见上述八行实机记录的补采段。

2026-10-15刷新原生消息接续：隔离诊断记录108条消息，浅色438帧仍46变化帧，最严重3900像素；闪亮采样距最后父窗WM_PAINT约41.565ms，其后150ms无新的已记录消息，不支持较晚WM_NCPAINT解释，但不能排除已有绘制的异步合成。首次545.08ms仍超目标。临时改动已逐字节恢复并重建，进程/端口/profile清理；见[原生消息记录](pixel-refresh-native-messages-2026-10-15.md)。整体目标未完成。

2026-10-15刷新双层显隐隔离：分别仅隐藏控制器、仅隐藏承载子窗，两组仍各有一次3902像素边缘闪亮，首次517.18/576.18ms，严格整表面均失败；两种试验全部撤销并恢复构建。未重复无收益深色试验或全量测试，本批进程/端口/profile已清理。证据、恢复哈希及启停见[显隐隔离记录](pixel-refresh-visibility-2026-10-15.md)，整体目标未完成。

2026-10-15遮盖内缩反证：避开四边1物理px后前两组仅角点变化，最终浅色复采仍出现3874像素闪亮，方案及专用测试已撤销。20项定向和真实生命周期虽通过，不能代替像素验收；恢复构建0警告0错误，本批资源已清理。见[内缩与复验记录](pixel-refresh-inset-2026-10-15.md)，整体目标未完成。

2026-10-15独立桌面复制采集：两份DDA录像均记录到Settings短暂显示底层文件内容及边缘闪亮，不支持GDI独有假象解释。第二份65帧/27变化帧，2帧整面内容变化，原图已核对；相对时钟不冒充UTC，事件采集仍不等于完整Present。新增采集路径、来源校验和超时清理，22项定向通过，产品未改，本批资源已清理。见[独立采集记录](pixel-refresh-desktop-duplication-2026-10-15.md)，整体目标未完成。

2026-10-15用户批准跳转行布局：独立紧凑样式、OK/Cancel顺序及键盘循环已落实，原始PNG由700×288缩到参考584×252。11项真实流程、12组字号/主题/DPR几何通过；完整像素仍有差异，不增加55页合格配对。基线、哈希和启停见[跳转行接续](pixel-goto-line-2026-10-15.md)。

2026-10-15隐藏/捕获隔离：六轮仅隐藏及六轮隐藏后捕获均只见12个角点变化，背景版本均不变；未覆盖新图替换，不能解释或消除正常刷新正文泄漏。修复DDA静止桌面结束方式，8项定向通过，严格整表面仍失败。证据和资源清理见[隔离记录](pixel-refresh-hide-isolation-2026-10-15.md)。

2026-10-15日常入口对齐：用户确认第一组普通DPI/界面12外观接近预期，新增明确构建与web来源的启动脚本，一次性备份并对齐实际配置为界面12、JetBrains Mono13。相同条件的普通/main-project入口逐像素一致；历史强制100%与字号联动是已确认的条件差异，尚不能归因所有历史窗口。新入口真实启动和正常关闭、配置保留测试、PowerShell5.1编码检查通过。详见[启动对齐记录](startup-alignment-2026-10-15.md)；整体像素整改未完成。

2026-10-15程序级启动对齐：按用户裁决新增旧外观一次迁移，所有入口正文固定JetBrains Mono13并移除正文选择器。默认profile、实际默认配置、exe随包web均已实测；Commit和Settings四分类度量对齐，24资源镜像一致。设置14项、宿主21项、格式及共享资产检查通过；全55页未验收。详见[程序级对齐与证据](startup-alignment-2026-10-15.md)。

2026-10-15接续：遮盖子窗无重定向试验仍有196364内部变化像素，已撤销并恢复原哈希；新参考关闭硬换行辅助线，完整底部及下角外沿同背景差0，其余仍欠条件一致性。Debug/Release各24资源再次一致。本批证据、未完成边界和启停清理见[反证与参考复采记录](pixel-refresh-redirection-shadow-2026-10-15.md)。

2026-10-15正文背景外沿配对：修正审计恢复配置路径并等待真实正文，浅色活动态64px完整外沿370944像素同背景、最大通道差1；远端仍有26级暗化，不能声称完整阴影。旧参考顶部光晕与旧普通入口空会话声明已更正。范围、失败、测试及清理见[正文背景外沿记录](pixel-shadow-body-ring-2026-10-15.md)。

2026-10-16设置回归接续：重复入口复用表单，未变字体不全页度量，背景相同属性不捕获，关闭取消隐藏遮盖导航。18项JS、12项设置语义及209项Shell通过，Debug/Release真实竞争回归通过；录屏343帧仍52帧边缘变化、首次588.78ms，未宣称全部修好。详见[本批记录](settings-open-regression-2026-10-16.md)。

2026-10-16设置脱离节点接续：修复已移除节点误触发背景遮盖，修前真实流程448.5ms、修后同项零请求。17项定向、Debug/Release真实重复入口及嵌套关闭通过；组合分类两次失败仍保留，原生闪烁未解决。详见[本批证据与清理](settings-detached-mutations-2026-10-16.md)。

2026-10-16跳转行标题接续：补齐16px品牌、40px标题起点及32px关闭区，12组几何与11项真实行为通过；原图含鼠标光晕，不计完整双端配对。详见[本批记录](pixel-goto-header-2026-10-16.md)。

2026-10-16背景队列接续：推送与兜底统一原子消费，消除同一文件/Git变化重复应用；上一批组合失败定位为取证JSON写入触发监听，证据改为实例结束后落盘。20项定向、5项队列集成、51项冲突、1项宿主及真实组合回归通过，关闭139–180ms。原生闪帧及全量像素仍未完成，详见[根因与证据](settings-change-queue-2026-10-16.md)。

2026-10-16背景换图重叠反证：新图插入后两帧才释放旧图，331帧仍1帧外框9717像素变化，内部0；候选已逐字节撤回并恢复Debug。原图、颜色分布、时序与清理见[本批记录](pixel-refresh-image-overlap-2026-10-16.md)。整体目标未完成。

2026-10-16遮盖子窗预创建反证：父窗显示前已有隐藏空子窗，343帧仍连续2帧外框4064像素变化、内部0；候选已精确撤回并恢复Debug。首次534.74ms，未计无闪或延迟达标，证据及清理见[本批记录](pixel-refresh-precreate-child-2026-10-16.md)。整体目标未完成。

2026-10-16产物身份接续：确认源码恢复保留旧时间后增量构建仍运行诊断DLL；普通入口强制非增量编译，日常入口审计取消跳过构建。真实重复入口/关闭/嵌套通过，正式DDA仍4帧内部露底及边缘变化，未宣称全部修复。证据及清理见[产物身份记录](settings-build-identity-2026-10-16.md)。

原始PNG透明撤盖候选仍失败并已恢复，详见[试验记录](pixel-refresh-bitmap-opacity-2026-10-16.md)。

2026-10-16合成路径接续：软件合成两组采样零变化但刷新变慢，保留GPU的遮挡诊断仍2帧失败；owned PNG候选外沿52帧变化，已撤回并强制恢复Debug。真实仓库9标签及重复/排队/嵌套回归通过，五条关闭181–276ms；整体闪烁仍未修复。详见[合成路径诊断](settings-composition-path-2026-10-16.md)。

2026-10-16日常入口与远端接续：设置八组正常启动器回归通过，但十次仍有2.65–2.97秒慢开；远端取证取消固定尺寸/临时profile，改走正常启动器。修复新增远端高度漏算边框及大字号复选框导致的正文滚动；8组布局、41项远端行为和真实三层关闭通过。两次修后屏幕门禁失败保留，不计像素通过。基线、哈希、证据及启停清理见[本批记录](pixel-remote-daily-entry-2026-10-16.md)，整体目标未完成。

2026-10-17设置等待纠正：本机Playwright对async假值提前返回，六处审计改为显式等待最终布尔值，旧宿主就绪及关闭耗时不再作为性能验收。正常入口八组复验通过，十次真实就绪426–593ms，关闭59–124ms；录屏样本就绪1409ms、OK关闭268ms，仍不宣称所有慢开与闪烁解决。根因、测试、哈希及启停见[本批记录](settings-awaited-state-2026-10-17.md)。

2026-10-17同批原始录像检出一帧整块白窗；随后把初始搜索焦点移到宿主最终渲染确认前，正常入口八组及初始焦点专项通过。修后259帧未再检出已知白窗，最大候选为保存中状态；不宣称全场景无闪。Debug／Release各25资源镜像一致，live-data.js MD5=D64AB5C4E97AA7D3ED60F456555A34AE，详情见上述设置等待纠正记录。

2026-10-17远端空态接续：正常启动器取得修前／修后空态及Define原始PNG，前景、DPI、指针门禁通过；补齐ToolbarDecorator外框、表头分隔与居中Nothing to show。4组空态、8组Define布局及41项远端行为通过。仍有窗口宽9／高24物理px、标题和工具条差异，未计整页通过。证据、哈希、失败与清理见[本批记录](pixel-remote-table-2026-10-17.md)。
