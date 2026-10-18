# 独立截图遮盖试验与前景证据门禁

日期2026-10-16。用户明确授权：试验无激活owned截图窗，通过无闪帧及阴影验收后再同步架构。该授权保留；本轮没有取得有效完整系统录像，既不采纳候选，也不判定此方向失败。正式实现已逐字节恢复，架构规范未修改。

## 候选与结果

仅将ShellModalHost截图子窗改为无激活、工具窗口样式的owned popup，显示时取所有者屏幕矩形。仍只显示原PNG，单一业务WebView、DOM、快照队列、等待和原模态阴影不变。没有加入产品参数、复制业务DOM或新依赖。候选差异保存在artifacts/pixel-followup-2026-10-16/refresh-owned-cover/candidate.diff。

首次light录像370帧，完整1820×1341、容差2、排除0像素，369帧发生变化；最坏第46帧1786460像素变化。但目视worst-frame-46.png实际为ChatGPT会话，不能归因Augit内容泄漏。6次流程观测首轮767.40ms、第二次506.40ms；这些受干扰数据不作性能结论。录像SHA256为7ed520d3dab81f9c1e6e786ba2cae39b6f42683476152adf08a2dc79a234d2a9，原始录像及失败分析全部保留。

第二次light-retry在首次Settings系统截图即被前景门禁拒绝：目标PID24308、窗口9177144，实际前景PID7524、窗口3539996；只读进程检查为WindowsTerminal.exe。它尚未开始有效录像。不能把这两次登记为候选视觉通过或失败，也没有阴影不退化结论。后续重试应使用新增连续前景采样，不重复依赖起止截图身份。

## 保留的取证改进

新增watch-recording-foreground.ps1，以50ms轮询GetForegroundWindow/GetWindowThreadProcessId，只读记录UTC、窗口、PID，不激活或移动任何窗口。recording-foreground.cjs负责启动、等待首样本、结束进程和保存JSON。capture-refresh-screen.cjs在录像前启动采样、录完后结束，保存原始身份样本；出现不同PID、同PID其他窗口、损坏数据或采样器提前退出，均不能计为有效验收。verify-refresh-surface.py拒绝明确未通过的前景证据。

50ms轮询不能证明每次Present都无遮挡，也不能检测所有同进程置顶遮挡；继续保留人工原图核验。历史没有此记录的录像仍能用于诊断，不能因此升级证据强度。本次首份录像另加manual-validity.json明确无效，不重写原始录像和原始分析。

## 验证及恢复

前景分类3项通过128.33ms，整表面分析7项通过0.010秒；真实只读采样4次、预期负例qualified=false、采样器正常停止。20个PowerShell编码/解析检查、JS语法、git diff --check通过。本次没有运行无关全量或扩大产品测试；候选未保留，不以测试证明其产品正确性。

候选Debug构建0警告0错误9.56秒；恢复构建0警告0错误3.49秒。恢复前核验当前候选哈希与备份哈希后才写回，未覆盖其他修改。ShellModalHost.cs恢复SHA256为5C7D5BDBC0BCFD2219BB9BBC3E2319E5EB3E9978653954EB2BC0402432DDA209；候选哈希保存在candidate.sha256，恢复临时备份已删除。Release未装入候选。网页四MD5本批未改，见[本轮终端取证](pixel-terminal-normal-entry-2026-10-16.md)。

## 资源及复验

Augit17388/24308、录像器16452、前景采样器8444已结束；端口57158/58418无监听；Augit-shadow-pair-0YKdKC/RQHkRY已删除。终端4480、端口60566、临时目录pUZKOT也已清理。原图、JSON、候选差异属于保留证据，历史拒绝清理路径未触碰。

当前正式版本采集命令：node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新目录> Light --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames --activate-main。该命令不包含已恢复的owned候选；不得用当前执行结果冒充候选复验。在computer-use实际激活并观察主窗后创建驱动输出main-active-ready信号。驱动finally停止实例和录制器并清理profile；异常只按输出PID执行taskkill /PID <本次PID> /T /F。

日常启动powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1，关闭窗口停止。55页/全部模态、背景刷新、终端/冲突逐行及系统阴影四项目标仍未完成；Windows10排除本次验收，跨屏DPI未验证。
