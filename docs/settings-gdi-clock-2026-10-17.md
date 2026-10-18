# 无CDP设置原生阶段与桌面时钟（2026-10-17）

本批解决旧ddagrab相对时间不能直接与原生UTC锚点关联的证据缺口，不修改产品开窗或呈现逻辑。仍有慢开，不是完成验收。入口tools/start-augit.ps1强制非增量Debug构建、真实配置副本、默认Session、正常DPI；未设置CDP端口、连接CDP或注入测试页面。Windows10排除本轮，跨屏DPI未验证。

## 采集与真实身份

settings-no-cdp.cjs新增显式--gdi-clock，仅改变外部记录器及采集方法标识；默认路径仍ddagrab。另补实际进程映射核对，缺少本实例Augit.dll/Infrastructure.dll即失败。新增correlate-settings-clock.py复用已有GDI帧时钟关联器，核对输入、滤镜、编码、封装与录像帧数及量化误差，再把原生Stopwatch阶段通过UTC锚点映射到前后帧；错PID、无效频率、缺时区、时钟倒退、缺失阶段或录像未覆盖一律拒绝。

证据目录artifacts/pixel-followup-2026-10-17/settings-gdi-clock，实际PID17192。实际映射Shell SHA256=62BE2A9637AC367090EC62CE0DEB322F83F70935A4C36EFB145A31A2FFD6958F，Infrastructure=1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80；仍映射Nahimic AudioDevProps2.dll，未确认因果。按产品规格§2只能作为诊断，不能作为干净环境性能验收。

源网页/随包资源由此前正常入口远端批次及本次强制构建核对；本次无CDP没有读取实际执行脚本或渲染字形，不把源哈希当已执行哈希，也不把设置显示的字体名称当实际字形证明。屏幕DPI168、整窗2880×1716；Settings显示Microsoft YaHei UI、12。最新四网页身份见pixel-remote-table-columns-2026-10-17.md，产品未再修改。

## 新事实

Computer Use实际点击UTC毫秒区间1792218534289–1792218534411；原生创建锚点1792218534483.4015。输入工具耗时、工具返回到原生创建的间隔分别记录，不能混算控制器时间。

| 原生阶段 | 毫秒 |
| --- | ---: |
| 控制器创建开始→结束 | 2448.2384 |
| 启动脚本注册开始→结束 | 152.2209 |
| controller.IsVisible开始→结束 | 1.8686 |
| 捕获开始→结束 | 115.1747 |
| 创建锚点→presented | 3280.8621 |

所有阶段仍在线程2，本次慢等待主要在CreateCoreWebView2ControllerAsync；不是此前controller.IsVisible慢调用的重复证明，也没有证据指向字体加载或跨线程。未引入预创建、固定延时、切换域名或其它已否证候选。

25秒GDI录像SHA256=3E10BF63AC6C6E1387E40891B59BED2DF49138CAD5E15863DA3BD01FD5641743，共472帧；原始墙钟1792218521476.197–1792218546449.073，最大帧间隔82.477ms。整个采样期前景均属于本实例。第302帧UTC1792218537734.902目视仍为主窗，第303帧UTC1792218537785.759目视完整设置；原生presented为1792218537764.2632，位于两帧之间。按本对相邻样本，桌面出现延迟落在创建锚点后3251.5005–3302.3575ms区间；这不是精确Present时刻。

保留297–305九张完整原始帧，每帧4942080像素、排除0，邻帧比较见neighbor-frames.json。301/302相同，303/304/305相同；297–300仍有约2153–2174像素背景/光标差异，尚未逐项归因。仅目视302/303及正常桌面状态，不把九帧或最大82ms间隔扩张为完整无闪结论；fullPresentationVerified=false。

桌面工具仍只列主窗，设置子窗没有独立可输入目标。未重复此前已拒绝的子窗点击，没有伪造HWND或换通道输入。本批用既有finally停止本次进程，没有取得正常Cancel/关闭周期证据；completeCycleVerified=false。

## 验证、清理与后续

采集模式/环境/前景三项Node测试、设置关联器四项Python测试、共用时钟八项Python测试通过。JS语法与git diff --check通过；没有跑Shell全量或改产品生命周期。首次/第二次ffmpeg邻帧提取因PowerShell传参丢失逗号转义失败，改用Python参数数组显式构造反斜杠成功，未覆盖既有证据。

Augit17192、ffmpeg11080、前景记录器8508、临时目录Augit-no-cdp-kfmNdE和握手文件已结束/删除，cleanup-independent.json独立核对。分析器及ffprobe也已结束，未留下服务器。原图、录像和JSON作为证据保留；诊断临时文件已清理。既有参考PID21240及其临时目录仍受此前自动审批拒绝影响，未再次尝试清理，不能声称工作区全部零残留。

下一步应针对控制器创建阶段补干净环境或可归因诊断；未获用户答复不得关闭Nahimic服务或改系统设置。可以继续已授权的刷新/逐行/阴影工作。新采样消除了“是否只是CDP点击等待”的一部分不确定性，但不证明注入是根因，不能盲改产品等待顺序。55页全模态、刷新500ms、完整逐行和系统阴影仍未完成，goal保持active。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。采集：node tools/audit/settings-no-cdp.cjs <全新证据目录> --gdi-clock。停止优先正常关闭；异常仅对已核实本批PID执行taskkill /PID <本次PID> /T /F。分析：D:/Python/Python3119/python.exe -B tools/audit/correlate-settings-clock.py <证据目录>。
