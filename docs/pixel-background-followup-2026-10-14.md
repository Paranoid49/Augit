# 2026-10-14 背景刷新试验（未验收）

2026-10-16遮盖接续：有效前景下owned顺序候选仍有内部露底；原始PNG位图候选六次303–414ms、323帧内部未变化，但52帧边缘变化，均已撤回并恢复Debug，架构未变。详见[窗口顺序试验](pixel-refresh-owned-order-2026-10-16.md)与[位图试验](pixel-refresh-owned-bitmap-2026-10-16.md)，整体未完成。

2026-10-16用户授权owned遮盖试验：系统录像混入外部窗口，候选未定论并恢复正式实现；新增全录像前景身份采样，已知受干扰录像拒绝作为产品闪帧验收。见[独立遮盖记录](pixel-refresh-owned-cover-2026-10-16.md)。

2026-10-15遮盖提速：首图及复用改用快速PNG，深色六次281–414ms，浅色复用260–396ms；整表面边缘仍变化，透明帧未验收。Debug/Release已更新，当前哈希、限制、验证及清理见[本批记录](pixel-refresh-fast-cover-2026-10-15.md)。

## 2026-10-15 普通入口补采仍失败

本批分析 `artifacts/pixel-followup-2026-10-15/refresh-restored-normal/light` 已采录像，清单确认普通入口、Light、界面 12、JetBrains Mono 13、DPR 1.75。DDA 原始录像 378 帧，每帧 2440620 像素，RGB 容差 2，排除 0 像素；76 帧有变化。第 62 帧（1.067 秒）内部区域变化 2209047 像素，存在内容泄漏；第 74–75 帧（1.283/1.285 秒）另有边缘变化。最大采样间隔 57ms，无重复时间戳；`sampledWholeSurfacePassed=false`、`fullPresentationVerified=false`。不能宣布当前 RAF 后接 timer 的恢复候选已解决透明帧。

视频 SHA256 为 `492f83ed7a3d21a1f1b534f6e0ced4e46f098189c2ba482f9608737f33d14f42`。分析命令：`D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py artifacts/pixel-followup-2026-10-15/refresh-restored-normal/light`。结果为目录内 `refresh-surface-analysis.json`，ffmpeg DTS 量化提示保留于原始日志。采集 PID26196、录制 PID5372、端口54388、临时目录 `Augit-shadow-pair-ei2k74` 已 finally 清理并独立核实无残留。此次失败不能被入口一致性的结果抵销。

## 后续验证

### 同实例五次开关与进程归属

新增`--memory-cycles`：同一实例连续五次打开Settings、改变背景标记、确认快照版本更新、关闭并自然空闲5秒，不强制GC。每轮关闭后确认快照数量和遮盖PNG归零；内存采样保存每个归属进程的原始数值。产品代码不变，无新增依赖。

[五轮原始记录](../artifacts/pixel-followup-2026-10-14/memory-cycles/memory-cycles.json)及[当时的进程身份](../artifacts/pixel-followup-2026-10-14/memory-cycles/process-roles.json)：

| 轮次 | 打开刷新后工作集 | 关闭空闲后工作集 | 关闭空闲后私有字节 | 关闭后进程数 |
| --- | ---: | ---: | ---: | ---: |
| 1 | 1243222016 | 902557696 | 631246848 | 7 |
| 2 | 1216688128 | 927129600 | 653434880 | 7 |
| 3 | 1224015872 | 930701312 | 656457728 | 7 |
| 4 | 1221840896 | 919199744 | 644399104 | 7 |
| 5 | 1205821440 | 931524608 | 657936384 | 7 |

首末轮GPU进程16480工作集480264192→509493248字节，私有464244736→490446848；宿主24748工作集76210176→78737408，主渲染8536为112066560→114364416。GPU占关闭后总量的一半以上。五轮不呈单向无界增长，不能外推长期无泄漏；下一步应量测截图/合成资源成本，不能继续凭总工作集推断JS泄漏。此组没有额外遮盖像素和五次刷新样本步骤，不与前组总数直接相减为优化收益。

驱动真实单组和五轮均通过，进程归属2项、JS语法和`git diff --check`通过。PID24748及子树已退出，端口54123和`Augit-pixel-fix-MBcwt9`临时profile已释放。网页四文件哈希未变。启动沿本文末尾命令换新输出目录并追加`--memory --memory-cycles`，正常自动清理；异常只对打印的AUDIT_PID运行`taskkill /PID <AUDIT_PID> /T /F`。本批次仍不登记系统前景、首次刷新或整体像素验收完成。

### 自然空闲与强制回收诊断

驱动新增可选`--gc-diagnostic`，仅在内存采样启用时执行：自然空闲5秒后采样，逐页面记录DOM计数、JS堆和实际图片，执行CDP强制回收，再采样并空闲5秒复测。诊断标记`acceptanceEvidence:false`，不在产品加入强制回收，也不把这些数字作为产品性能验收。

首组[直接回收诊断](../artifacts/pixel-followup-2026-10-14/gc-diagnostic/gc-diagnostic.json)没有回收前空闲对照：工作集1507495936→1510924288→空闲后1290248192字节，不能把延迟下降归功于GC。补充[先空闲后回收对照](../artifacts/pixel-followup-2026-10-14/idle-gc-diagnostic/geometry.json)如下：

| 阶段 | 工作集字节 | 私有字节 |
| --- | ---: | ---: |
| 连续刷新后 | 1532669952 | 1160019968 |
| 自然空闲5秒 | 1379979264 | 1006309376 |
| 强制回收后 | 1289428992 | 908451840 |
| 再空闲5秒 | 1298132992 | 908795904 |
| 关闭Settings后 | 1038663680 | 770924544 |

两组均覆盖3个页面，快照页面各剩一张2872×1577图片且onload为空。自然空闲可释放部分占用；总进程内存仍显著高于JS堆，不能据此判定具体缓存或排除原生资源保留。下一步应按进程类型与重复开关周期定位稳定保留量，再决定方案，避免仅凭DOM计数继续盲改产品代码。背景首次延迟、系统前景无闪隐及总体内存仍未验收。

本批次只修改审计驱动和记录，无产品代码变化，四网页MD5与上一批相同。真实两组通过，进程归属2项测试通过，JS语法、共享资产和空白检查通过。PID24076／25364、端口51776／63580及两份临时profile均已释放；正式JSON与PNG证据保留。启动使用本文末尾命令，将输出目录改为新目录并追加`--cover-pixels --refresh-samples --memory --gc-diagnostic`；正常自动清理，异常仍仅对打印的AUDIT_PID执行`taskkill /PID <AUDIT_PID> /T /F`。

### 退役图片资源释放

真实回归保留被替换的旧图片节点，确认其已脱离DOM、回调为空，但src仍存在。不能仅凭此宣称泄漏；现新图片成功replace后主动移除旧图片src，提前解除图片资源引用，不影响显示中的新图。旧实现负向失败，修后旧图connected/hasSource/hasLoadHandler均false，移动三色标记、五次刷新及关闭归零通过，见[退役图片结果](../artifacts/pixel-followup-2026-10-14/retired-image-after/geometry.json)。

同含像素取证的流程下，工作集Settings1001373696、首次遮盖后1221464064、连续刷新1497440256、关闭1095438336字节；连续后仍约1.39GiB，与前一组接近，**没有证据表明该变化解决了整体内存增长**。这是资源释放加固，不登记性能通过。

修改ShellModalHost.cs及驱动，网页四文件哈希未变；Debug0警告/错误，真实单组、JS语法及空白检查通过。修前PID26372、修后15840由finally清理，端口65156／59587和profile均释放。启动同下方命令加`--cover-pixels --refresh-samples --memory`，异常按输出AUDIT_PID停止。

### 图片替换回调引用链整改

检查发现每张新图片的onload闭包捕获previous，替换后仍留在当前DOM图片上，可能沿旧图回调连续保留历史图片。新增真实回归要求加载后的onload为空，在旧实现明确失败。现成功、迟到和失败路径清除新图回调，替换前同时清除旧图回调；错误通知仍回宿主。修改ShellModalHost.cs及驱动，无新增依赖、网页哈希不变。

修后真实单组含移动标记、回调清除、五次刷新及关闭归零通过，见[当前结果](../artifacts/pixel-followup-2026-10-14/image-chain-after/geometry.json)。五次延迟352.8–386.3ms。图片队列4项通过，构建0警告/错误。内存主窗810803200、Settings1001807872、首次遮盖后1248329728、连续后1494843392、关闭后1043800064字节工作集；仍有显著增长，不能宣称内存问题全解。该组包含额外像素取证，与上轮阶段组输入序列不同，不直接相减登记节省量。

修前失败PID22872、修后PID25040均由finally停止并删除profile，端口61526／64575释放。首次延迟、系统前景逐帧、55页配对和终端/冲突、阴影/DPI仍未完成。本条只记录真实引用链修复，不作为整项通过。

### 复用遮盖的真实内存成本

新增按父进程树归属的阶段采样，不能以156201字节PNG冒充整体占用。第一组`reuse-memory/geometry.json`进程7→8→9→7；首次刷新观测663.7ms，连续刷新后工作集1505898496、私有1139412992字节，关闭后1053827072／797061120。该数据含连续刷新缓存，不把全部增量归为单个控制器。

第二组进一步分离首次遮盖与连续刷新，见[阶段数据](../artifacts/pixel-followup-2026-10-14/reuse-memory-phases/geometry.json)：

| 阶段 | 本实例进程数 | 工作集字节 | 私有字节 |
| --- | ---: | ---: | ---: |
| 主窗 | 7 | 808472576 | 563613696 |
| Settings | 8 | 1004630016 | 699625472 |
| 首次遮盖后 | 11 | 1332064256 | 893440000 |
| 连续刷新后 | 11 | 1609732096 | 1168302080 |
| 关闭后 | 7 | 1033367552 | 771842048 |

两次均为Debug、DPR1.75、UI12、同fixture，单次树采样而非长期稳定统计；进程数差异原样保留，不能挑低值。首次遮盖后相对Settings增约312MiB工作集，后续再增约265MiB，关闭时控制器/PNG归零但浏览器缓存未立即回到主窗基线。当前遮盖方案的内存与首次延迟不满足收口要求，仍需降低捕获/解码成本和验证前景呈现，不能仅以五个后续延迟样本宣布解决。

本轮只改审计驱动，增加memoryFirstCover、memoryRetainedCover及保留态断言；产品代码、四文件哈希不变。两组真实回归通过，归属测试2项、JS语法和空白检查通过。PID24680／25156，端口58206／52082均退出，profile由finally删除。启动采样沿下方命令增加`--refresh-samples --memory`，异常仍只停止输出AUDIT_PID。

### 遮盖复用实施

当前改为同一模态存续期间复用隐藏遮盖控制器；每次先捕获最新原图、等待对应图片版本加载，再显示遮盖。结束刷新只隐藏，切换表单/尺寸/缩放、退栈和关闭则释放。当前PNG与短期控制器保留成本明确存在，不再要求每次刷新后字节归零；最终关闭仍强制归零。图片等待保存局部Task引用，避免同步关闭续体清空字段后的空引用。

同fixture五次更新稳定观测336.4／385.9／404.4／387.2／429.1ms，控制器创建均0，图片等待8.6–14.8ms，见[复用样本](../artifacts/pixel-followup-2026-10-14/reuse-cover/refresh-samples.json)。这是Debug/DPR1.75单环境后续刷新样本，不包含首次创建，不能外推全部场景或宣布整项完成。

新增同一控制器更新标记位置后再核验原始PNG，证明不是重复旧图。矩阵前五组通过，第六组Dark40在遮盖测试之前因树缩进断言失败；单独两组UI40复验通过，失败原因未确定，不写为首轮六组全通过。记录分别为`reuse-matrix/geometry.json`、`reuse-large-retry/geometry.json`。Shell完整182项通过（12秒，模态生命周期模块阶段验证触发），Debug0警告/错误，定向C#格式、JS语法、共享9资产及空白检查通过。

修改ShellModalHost.cs、ShellWindow.cs及驱动；四个网页文件MD5未变。PID21824、6784／24944／9196／9164／11184／2068、22452／25932均由finally退出并删除profile。仍缺首次刷新优化、额外控制器实际内存、前景逐帧无闪隐与完整双端配对。不能用本次性能收益替代这些要求。

### 分阶段连续样本

追加失败实验：尝试同时启动CapturePreview与CreateController以缩短串行等待。三个纯任务单测通过，但真实协议并发场景在首次模态呈现即返回“无法打开独立模态窗口”；把WhenAll改为逐个await仍同样失败，不能将原因武断归为WhenAll。已撤回本轮并行实现及仅为该实现新增的两个文件，恢复串行捕获；未触碰前序用户改动。恢复后构建0警告/错误，真实单组通过（`serial-restored/geometry.json`）。实验PID18064／25124及恢复验证23988均由finally清理，profile删除。结论是当前COM路径不能直接采用此并行方案，后续应从复用控制器或更合适的捕获机制推进；本轮不报告性能改善。

为避免只比较两次偶然数据，宿主状态新增captureCoverTiming，将捕获PNG、创建控制器、等待初始图片呈现分别计时；测试驱动增加`--refresh-samples`，五次独立变化以20ms轮询确认新版本加载且遮盖已释放。最新[五样本](../artifacts/pixel-followup-2026-10-14/cover-five-samples/refresh-samples.json)总稳定观测为557.5／537.7／563.5／527.7／570.9ms；捕获157.3–169.9ms，控制器55.2–61.5ms，图片等待80.5–102.2ms。该总值包含遮盖移除与轮询，不是精确的屏幕呈现时间；全部超过500ms，明确保持未完成。新证据支持优先复用模态存续期间的遮盖控制器，不能只删掉等待帧来凑阈值。

本批次仅新增诊断与自动化样本，未宣称性能优化。构建0警告/错误，两次单组真实回归通过，第二次含五个样本的版本、释放、有限非负计时断言。live-data及共享三文件MD5与前批相同。实例16712／18092、端口55158／54506均已退出，独立profile由finally删除。执行命令同下方，在末尾增加`--refresh-samples`即可采样；异常停止仍按本次AUDIT_PID。当前仍需实际前景逐帧、55页双端、终端/冲突及阴影/DPI证据。

遮盖关闭竞态和关闭后PNG归零断言已完成浅深×UI12/14/40六组，见[六组记录](../artifacts/pixel-followup-2026-10-14/cover-race-matrix/geometry.json)。进一步在原Settings DOM明确opacity=0时对新增遮盖控制器截图，预期坐标三色内部原始像素通过并已目视完整Settings，见[像素记录](../artifacts/pixel-followup-2026-10-14/cover-pixels3/geometry.json)。首轮误按8px偏移遗漏边框，第二轮CDP发现旧背景目标滞后，均保留失败，修正测量后通过。这仅证明遮盖内容，不证明系统合成前景没有闪帧。

像素检查组的加载观测627.9ms、DOM透明194.7ms（100ms轮询）；延迟仍不满足稳定500ms目标，不能结案。下一步须优化额外控制器创建成本并取系统前景证据，不能靠放宽阈值。

代码另修：Pop把遮盖释放纳入逐步清理，单步故障不跳过剩余资源；Ready使用布尔失败值，背景副本不等待时也不产生无人观察的异常Task；关闭先释放控制器/流再同步完成Ready。构建0警告/错误、ShellModal13项通过，最终单组含关闭竞态、像素及归零通过，见[最终单组](../artifacts/pixel-followup-2026-10-14/cover-cleanup-final/geometry.json)。live-data哈希未变。

本批次PID22960／20092／25704／22356／5324／21164（六组）、25984／2452（失败）、11516（像素通过）、20268（最终单组）均由finally退出并删除profile。新增截图和JSON是正式证据，保留。仍未完成55页双端配对、终端/冲突逐行配对、系统阴影和真实跨屏DPI。

本批次继续整体整改。用户明确取消本次Windows10实机验收要求；其余55页/模态同态配对、终端与冲突逐行配对、系统阴影和跨屏DPI仍保留。产品支持范围不变，不声明Windows10已验证。

真实Windows11、DPR1.75、UI12、同一reference fixture的旧实现测得：背景变化后Settings DOM透明108.7ms，观察到背景加载119.4ms（轮询100ms，非精确合成延迟）。[原始记录](../artifacts/pixel-followup-2026-10-14/refresh-before/background-refresh-timing.json)。这证明此前只验证最终PNG更新不足以证明无闪隐。

当前工作区试验：宿主增加临时表单原图遮盖，在图片加载并经过两帧后才允许网页隐藏表单捕获背景；恢复表单两帧后释放遮盖。遮盖不复制业务DOM，增加一个短期WebView2及PNG。关闭/退栈释放遮盖，加载失败拒绝进入捕获。新增window/modal-cover及window/modal-uncover内部生命周期请求。此实现仍须补故障/关闭竞态和真正前景像素验证，不能以DOM隐藏期间存在遮盖对象宣称无闪烁。

首次真实试验失败：父层刷新比较期间主背景哈希变化。主背景删除测试标记后尚未完成刷新，增加明确busy信号并等待后第二次单组通过。第二次观测加载约379.2ms，DOM透明146.8ms；透明期间预期由遮盖呈现，但目前没有屏幕逐帧证据。比旧实现增加约260ms仅为两次单样本差值，不是稳定性能结论。持续刷新、输入响应与额外内存仍需测量，若代价不合理应替换方案。

验证：构建0警告/0错误；ShellModal筛选13项通过（含新增两个遮盖协议名称校验）；修正时序后的单组通过，结果见[试验几何](../artifacts/pixel-followup-2026-10-14/refresh-cover-probe2/geometry.json)。新增关闭后遮盖归零断言尚待下次运行。未跑全模块、不标方案完成。

修改文件：ShellModalHost.cs、ShellModalRequest.cs、ShellWindow.cs、ShellModalRequestTests.cs、live-data.js、run-pixel-remediation-shell.cjs及本批次文档。live-data.js前MD5为404AF1E77653319120C6CA1654569AB6，后为73B757F728FC893773DCB3F24F622311；mockup.js/css与bridge.js仍为3EEACB9FE4AA6B29CBE0F3495E426CC5／5C1D95FC4479C3DC8BB22D5869324890／F297BF8769E249CA0D804F4F4EEEF5A9。未暂存或提交混合工作区。

试验实例5748／15524（失败）／21160均由finally退出并删除profile，端口61954／50790／60837随实例释放。正式证据保留。

```powershell
# 一次性试验，正常结束自动退出并清理profile。
node tools/audit/run-pixel-remediation-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/pixel-followup-2026-10-14/refresh-cover-probe2 --single --reference-fixture
# 异常时只停止驱动打印的本次PID及子树。
taskkill /PID <AUDIT_PID> /T /F
```

## 严格首次刷新测量入口（2026-10-14）

重新测当前工作区，普通流程五次连续刷新为330.6、359.3、375.1、389.1、414.5ms，证据refresh-current-baseline。发现普通流程计时前执行尺寸等模态操作，不能直接把后续loadedObservedMilliseconds称作首次成本。驱动新增--cold-refresh，在Settings首次就绪后、其他模态测试前断言captureCoverBytes=0，变化后确认新版本加载、遮盖隐藏且busy结束，并要求controllerMilliseconds>0，明确包含首次控制器创建。

严格首次本次为402.14ms（20ms轮询），capture133.71ms、controller48.68ms、ready68.98ms，见refresh-cold-current/cold-refresh.json。本次单样本低于500ms，不能替代此前663.7ms记录或宣称稳定通过；系统前景逐帧、总体内存仍缺。此次新增证据而非产品优化，产品代码/哈希未变。两次真实单组完整流程通过，JS语法、git diff --check通过。

启动：`node tools/audit/run-pixel-remediation-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/pixel-followup-2026-10-14/refresh-cold-current --single --reference-fixture --cold-refresh --refresh-samples`。finally自动清理，异常停止`taskkill /PID <AUDIT_PID> /T /F`。本次PID18276/23500、端口51568/56520及临时profile Augit-pixel-fix-3qiB5V/BnLlhB均已回收。未暂存提交，整体整改保持未完成。
## 首次刷新六组矩阵（2026-10-14）

在同一严格--cold-refresh口径下完成Light/Dark×UI12/14/40六个隔离实例，全部初始captureCoverBytes=0且计时包含控制器创建。完整记录以refresh-cold-matrix/geometry.json为准；cold-refresh.json为最后一组便捷输出，不能代替六组记录。

|主题/UI|首次观测ms|捕获ms|控制器ms|图片就绪ms|
|---|---:|---:|---:|---:|
|Light12|414.89|141.23|45.37|63.39|
|Light14|455.67|177.16|46.99|59.20|
|Light40|384.72|100.28|44.04|76.50|
|Dark12|428.88|153.52|52.55|59.55|
|Dark14|474.90|177.51|49.38|67.82|
|Dark40|364.97|98.72|44.08|68.14|

每组仅一次首次样本、20ms轮询、Debug/DPR1.75；UI40使用1024×640窗口，UI12/14为1640×900，因此不能把较低值归因于大字号。六组均低于500ms，但不推翻历史超时，也不证明稳定分位数、系统前景无闪帧或总体内存通过。--refresh-samples现有分支只在Light12执行五次连续测量，其他组不声称拥有五次连续样本。优化应优先评估捕获面积/PNG编码成本，而非删除图像呈现等待。

六组几何及生命周期回归均通过，产品与驱动本批未改，未暂存提交。启动沿前一节移除--single并换输出refresh-cold-matrix，保留--reference-fixture --cold-refresh --refresh-samples。正常finally关闭/清理；异常`taskkill /PID <AUDIT_PID> /T /F`。PID26588/26192/7436/8320/26112/7164，端口58119/52286/64830/56638/62030/65011及六个临时profile均已回收。整体整改未完成。
## 复用遮盖显示等待（2026-10-14）

源码确认复用路径原先在图片onload完成后SetVisible(true)便返回，真实表单随后隐藏，未等待新显示控制器获得渲染机会。新增ShellSnapshotPresentation两次requestAnimationFrame确认；Snapshot.ShowCoverAsync先显示再注入脚本，收到presented才继续。失败/关闭解除等待。初次创建既有两帧路径不变。不把两帧回调当成DWM已呈现证明，前景逐帧验收仍缺。

定向脚本测试实际执行产品注入脚本，验证第一帧不确认、第二帧恰确认一次；快照图像/模态操作队列8项通过，限定两产品文件的dotnet format通过。真实Light/UI12单组生命周期与刷新流程通过：首次409.69ms，后续五次376.42/363.44/372.99/368.40/367.13ms；采样20ms。证据refresh-presentation-wait。运行时桌面先前处于锁屏、用户随后报告解锁，这批不计前景视觉或稳定性能验收，不与历史样本作严格A/B比较。

启动：node tools/audit/run-pixel-remediation-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/pixel-followup-2026-10-14/refresh-presentation-wait --single --reference-fixture --cold-refresh --refresh-samples。脚本finally停止PID22908并删除Augit-pixel-fix-oyyeNL，进程及端口55857复查无残留。异常停止taskkill /PID <本次AUDIT_PID> /T /F。四主要网页哈希未改，未跑全量。

## 遮盖准备期间合并背景变化（2026-10-14）

本轮复核发现，网页在请求 window/modal-cover 前就复制并清空 dirtyDepths；遮盖准备期间新增的背景变化留在下一批，即使即将开始的 CapturePreview 已经包含最新 DOM，仍再执行一次遮盖和背景捕获。真实注入回归在发送首次 modal-cover 后立即更新同一个标题栏标记，旧实现稳定复现为 covers=2、refreshes=2，但 snapshotRevision 只增加1，说明第二次捕获没有提供新的图片版本。修前结果见 refresh-coalesce-before/coalesced-refresh.json，不能把失败写为通过。

live-data.js 在遮盖确认就绪、背景捕获开始之前，将等待期间的 dirtyDepths 与本批 depths 去重合并并清空；随后捕获期间新增的变化仍留在集合，完成后启动下一批。不删除两帧呈现等待，不修改宿主协议、图片版本队列或窗口拓扑，也不增加定时器。

真实修后 refresh-coalesce-after 变为 covers=1、refreshes=1、版本增加1；含五次20ms静默观测的总时长从修前961.5ms到修后469.4ms。这是两次单样本及不同运行状态的观测，主要结论是重复操作消除，不能据此量化稳定收益或宣布500ms性能通过。随后五次普通刷新为389.13/401.24/386.84/401.21/403.61ms，均含20ms轮询，不是系统呈现计时。

补充边界验证 refresh-coalesce-boundary：遮盖准备期间变化为1次遮盖/1次捕获/版本+1；在首次 modal-uncover 发送后更新标记，得到2次遮盖/2次捕获/版本+2，证明捕获完成后的新变化没有被本次合并吞掉。两项均等待连续五次稳定观测；含静默期总时长分别508.5ms和818.7ms，后者明确包含两次必要刷新。记录中的 latest 字段是 DOM 内容，不能单独证明最终PNG文字；本批未新增系统前景逐帧或像素内容验收。

两次修后单组真实外壳流程完成既有尺寸/背景归属/嵌套窗口/关闭清理断言，末尾 __augitErrors 为空；JavaScript语法及git diff --check通过。只运行受影响真实单组与新增边界，未运行全量测试或重建未改动的C#。修改文件为 web/src/live-data.js、tools/audit/run-pixel-remediation-shell.cjs 及本批记录。live-data MD5从851EFBF8C278A2D40DB6208CE2E60496到DB52470B2BAB434DE4E463086A39FA6D；mockup.js/CSS/bridge保持6298632B16BA82E5FCFB9B0AB177593D / 06751CCD396E8ECACD19779CBBDE38AB / F297BF8769E249CA0D804F4F4EEEF5A9。

本批PID26408/端口50772/临时Augit-pixel-fix-dCA3Dz（修前失败）、PID15660/端口62595/临时Augit-pixel-fix-h3tBa2（修后）、PID17812/端口59435/临时Augit-pixel-fix-DCRafo（边界）均由finally关闭和删除，独立查询确认进程与目录不存在。

```powershell
node tools/audit/run-pixel-remediation-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/pixel-followup-2026-10-14/refresh-coalesce-boundary --single --reference-fixture --coalesce-refresh
# 追加 --refresh-samples 可在同一单组记录五次普通刷新。正常 finally 自动关闭。
# 异常中断只停止驱动输出的本次PID及其子树。
taskkill /PID <AUDIT_PID> /T /F
```

背景刷新仍未完成系统前景无透明帧、稳定延迟分布及内存成本验收；55页及全模态同态配对、终端完整逐行配对、系统阴影定量仍未完成。本批未回滚、暂存或提交混合工作区。

2026-10-14快照物理像素整改：确认CapturePreview实际2452像素被压到2450像素，改为按PNG真实宽高和捕获比例呈现；深浅主题同位置文字带各156个差异像素降为0，真实更新/嵌套尺寸及关闭清理回归通过。完整证据、命令、哈希和未验收边界见[pixel-snapshot-sizing-2026-10-14.md](pixel-snapshot-sizing-2026-10-14.md)。整体整改未完成。

2026-10-14系统录屏批次：修前425帧中36帧真实露底，独立遮盖子窗及DWM呈现等待后深/浅417/432采样帧探针均无异常；仍有34ms间隙及浅色重复时间戳，不能宣称完整逐帧通过。首次录屏负载延迟572/574ms仍超目标。184项Shell及定向真实生命周期回归通过，本批进程/端口/profile已清理。完整失败迭代、命令、证据、哈希及未验收项见[系统录屏整改](pixel-refresh-screen-2026-10-14.md)。整体目标保持未完成。
