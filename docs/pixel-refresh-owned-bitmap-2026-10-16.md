# 原始PNG位图遮盖试验（2026-10-16）

## 结论

本批在用户已授权的无激活owned原始PNG遮盖范围内，试验用Windows内置GDI+解码PNG、UpdateLayeredWindow提交原尺寸裁切位图，去掉遮盖专用WebView。业务仍为原有单一WebView，没有复制DOM、重绘控件或新增依赖。候选没有达到整表面无变化要求，已撤回，不更新架构，不进入日常版本。

普通入口浅色Settings，175%系统缩放，Desktop Duplication采样323帧，每帧1820×1341；首帧固定，RGB容差2，不排除边缘、角部或指针区域。52帧变化，最坏12796像素（角部408、边缘12388），内部变化帧0；最大采样间隔39ms，无重复源时间戳。已查看第52帧原图。单次未采到正文露底不等于消除所有透明帧；边缘门禁仍失败。

六次刷新413.64、331.55、373.03、302.72、371.26、324.14ms，本次均低于500ms，只是本次观测结果，不外推稳定延迟。前景99次采样、最大间隔91ms，qualified=true，采样器未提前结束；仍不能排除采样间短暂干扰。没有完成此候选系统阴影不退化验证，因此不满足用户允许保留候选的条件。

## 证据与测试

`artifacts/pixel-followup-2026-10-16/refresh-owned-bitmap/light/`保留FFV1原录像、前景记录、刷新时序、完整像素报告和最坏帧。录像SHA256为2cee4029c69a07002b9b99d9795475ed1a7cb6531edb95ffbc2f10d976d0267e。分析脚本为verify-refresh-surface.py及locate-refresh-frame-difference.py，没有修改原图像素。

候选两项定向测试覆盖损坏PNG多次拒绝及裁切越界，2项通过112ms；首次构建触发CA2201/CA1806，修正后构建与测试通过。未运行无关全量。候选源码作为证据保存在上级目录ShellPngCover.cs.txt、ShellPngCoverTests.cs.txt及candidate.diff，不属于编译输入。候选仅实验性代码，不作为可发布实现或资源生命周期完整证明。

核对候选与备份哈希后，仅撤回本批ShellModalHost候选和两个新增文件；恢复SHA256为5C7D5BDBC0BCFD2219BB9BBC3E2319E5EB3E9978653954EB2BC0402432DDA209。恢复Debug构建0警告0错误2.90秒；Release未装入候选。共享UI资产检查、git diff --check通过。没有覆盖其他修改、暂存或提交。

## 清理与复验

Augit15612、前景采样器13720、录像器3492均退出，临时目录Augit-shadow-pair-wJpKc1不存在。原有owned顺序试验的临时备份和本批恢复备份在校验后删除；证据保留，历史拒绝清理路径未触碰。

复验命令：`node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新目录> Light --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames --activate-main`。需要实际激活并观察Augit后释放main-active-ready检查点；脚本finally清理实例，异常只对本次输出PID执行`taskkill /PID <PID> /T /F`。命令使用当前构建，不包含已撤回候选。

日常启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`，关闭窗口停止。55页/全部模态、背景刷新、终端/冲突逐行及系统阴影四项目标仍未完成。Windows10排除本次验收；跨屏DPI未验证。
