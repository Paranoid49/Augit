# 设置准备消息排队与正常入口白帧复验（2026-10-17）

本批接续交接，不重跑已撤销的遮盖、GPU及合成试验。整体目标保持未完成。

## 实现和计时纠正

`window/settings-ready`仅投递SettingsPrepareMessage，原生窗口循环执行cloak、SyncBounds、控制器可见、ShowWindow及位置通知；最终呈现仍走既有消息。避免在WebView消息回调里同步重入窗口消息，不增加固定等待或额外窗口。architecture.md已同步。增加准备各阶段单调计时点，并区分自动化click调用、实际trusted click、宿主创建及呈现。

`settings-queued-prepare-regression`正常启动器八组通过，十次原生呈现598、566、569、543、540、552、610、2903、550、556ms；第八次控制器创建2412ms，是实际慢开。另六次外部约3秒主要是点击事件延后2497–2617ms送达，不能混为宿主慢开。准备6–20ms。关闭Cancel/Save/Escape/标题97/217/73/107ms。该回归与离线分析并行，不能用于归因性能改善。

前批已执行17项IndependentSettings定向及一次226项Shell模块测试，全部通过；C#两文件定向格式通过。本批不重复全量测试。

## 正常入口录像反例

修后`artifacts/pixel-followup-2026-10-17/settings-queued-prepare-cycle`经tools/start-augit.ps1强制构建、默认profile、真实配置副本、普通DPI录制。实例2440、端口57071；准备986→1018ms，原生呈现1343ms，外部hostReady1668ms，OK实际关闭270ms且版本只增一次。录制器上限改25秒，流程完成时必须仍存活，不能把原12秒截断录像计作全过程。

完整664原始帧、2880×1716、排除0像素、RGB容差2，最大间隔39ms。整帧最近状态最大差995644像素，逐像素包络最大差534510像素。第193帧3.645秒已目视确认整块白色设置窗，证明排队修改尚未消除白闪。不得用局部绿测试或历史259帧未检出白窗替代此反例。fullPresentationVerified=false。原始视频SHA256为4a26c10a41996455eec32652de00edbff45c403683f17862823dfd4494da6a3e。

之前`settings-ready-stages-cycle`分析会话19547及Python23152/ffmpeg27120已终止，未生成分析JSON；不重复当作完成。其录像仍保留。修后完整分析会话86530已正常结束，Python28136及ffmpeg22244退出。

## 捕获后合成等待候选

候选在RevealSettingsWindow中、解除cloak之前增加DwmFlush，仍不显示截图或引入固定延时。现有6项隐藏启动断言及定向C#格式通过，候选尚不能视为整体修复。

正常入口settings-post-capture-flush-cycle，PID25532、端口61032。原生呈现4248ms、控制器111ms、导航317ms、准备3799→3809ms、捕获完成4238ms；外部hostReady4555ms，真实OK关闭259ms。WOFF2资源记录2863.8ms，settings/read70.7ms、字体枚举183.7ms、git/detect200.6ms；开窗期间WM_NULL共68采样、最大9.4048ms、最大采样间隔84ms，无超时。资源等待与桥接迟到相关但不能据此确定根因，更不能归因于10ms揭示阶段。

原分析误选5.004秒第270帧作为打开参考，目视实际仍为主窗，原JSON与图保留但不得作为有效三状态分析。分析器新增独立输出目录，防覆盖测试及已有反例共6项通过。纠正采用6.509秒第355帧，目视确认Appearance页已打开；输出corrected-open-reference，769帧、2880×1716、排除0、RGB容差2，最大间隔51ms。整帧最近状态最大95387像素、包络最大65645像素，最严重帧为Terminal分类，未检出此前整块白窗；仅覆盖本次记录，fullPresentationVerified=false。视频SHA256为0599d13c71334dca7078b545b0804ed66ea2872c78cbdce091b90392ed6978c0。

原分析会话24360及纠正会话7638已正常结束；纠正Python15836已完成。分析未与下一轮启动性能采样并行。标题局部769帧白色扫描无候选，但只作辅助，不能替代全画面结果。新增只读资源分段和longtask观察，1项观察器测试通过，不修改产品等待条件。

另核对Microsoft Learn的ICoreWebView2_3.SetVirtualHostNameToFolderMapping文档，其提醒.local顶级域可能造成导航延迟；当前产品使用augit.local。该项仅为后续线索：现有项目颜色和历史筛选使用localStorage，不能随意换origin丢失数据，本批未改域名或迁移用户存储。官方页面通过PowerShell读取，内置网页查询未返回内容，不冒充搜索引用；来源为https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2_3。

## 身份与边界续记

提前cloak显示候选已撤销：InitializeWebViewAsync创建控制器前先遮蔽再不激活显示，正常入口settings-early-cloak-startup六次原生呈现590/532/2710/568/2728/2900ms；第3次脚本注册88→2274ms，第5次准备330ms至捕获2721ms，第6次导航158ms至准备2691ms。字体仅2–3.3ms，仍发生多阶段慢开，无明确收益。精确删除本轮新增初始化块及对应测试，保留既有排队、捕获后flush和分段计时；不重做该候选。实例27292、端口62755、目录Wagbv0由finally清理。

不并行离线分析的settings-flush-startup-observed六次普通单击，原生呈现582/571/2772/520/2993/536ms；第3次控制器67ms而导航2321ms，第5次WOFF2耗时2274.3ms。全部longtask列表为空，资源条目的DNS/连接字段全0，不能以0值认定不存在解析延迟。扩展计时后settings-flush-staged-regression八组通过，十次原生呈现591/2788/3062/548/2950/544/613/579/563/545ms；其中一次控制器2508ms。Cancel/Save/Escape/标题关闭119/188/76/90ms；唯一窗口、主题取消、真实写入失败重试、迟到保存与Apply重开通过。秒级慢开仍存在，不归因于单一阶段。

以上实例24828/24832、端口62875/63380、目录a4nGpp/MD7mQF均由finally清理。最终计时字段版本17项IndependentSettings测试79ms通过；Release强制非增量构建0警告0错误、Debug/Release各25资源镜像及9项共享资源一致。后续若进行生命周期候选，该构建结论不自动覆盖候选。

`settings-normal-runtime-identity-corrected`已用Debugger.getScriptSource核对主窗11及设置9脚本、CSS/WOFF2哈希，DPR1.75，正文实际JetBrains Mono，设置输入实际Microsoft YaHei UI。该证据早于本批C#修改，其DLL身份不外推最终构建。新的远端实机证据单独登记最终DLL和资源身份。

仍需解决首次白帧、控制器偶发秒级等待及全模态背景刷新；55页、完整终端/冲突逐行、系统阴影不能结案。Windows10排除本轮，跨屏DPI未验证。

## 启停

后续无CDP正常入口与独立设置域名反证见[最新接续](settings-no-cdp-origin-2026-10-17.md)。提前cloak和独立域名候选都已撤销且强制重建；正常启动慢开和完整呈现仍未结案。

正常启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`。优先正常关闭；异常仅停止本次实例：`taskkill /PID <本次PID> /T /F`。本批实例由finally清理，证据保留；收尾资源复核另记。
