# 2026-10-16 遮盖局部捕获对照

上一轮修正审计截图隐式光标样式写入，属于有效进展。本轮核对刷新驱动：正式录像阶段直接使用桌面FFV1和桥接，未调用Playwright页面截图，所以历史桌面露底证据不能由光标干扰解释或撤销。

## 局部捕获假设与结果

当前遮盖只展示模态区域，却捕获完整业务视口。本批新增仅审计的probe-cover-clip.cjs，在普通入口Settings已打开后，用原生状态liveBounds与rect换算CDP clip，scale=1，不缩放PNG；三轮交替完整/局部采集。compare-startup-paths仅在显式--probe-cover-clip时调用。产品代码没有改动。

首轮目录artifacts/pixel-followup-2026-10-16/cover-clip-probe：局部PNG1820×1341，文件103921字节；完整PNG约404170–628481字节。两份完整图混入表单隐藏状态，不能当作性能或像素通过证据。保留全部原图与pixel-pairs.json。

后续cover-clip-traced增加逐次前后忙状态、捕获标记时间和resize观察。完整采集耗时121.04/178.80/117.02ms，局部459.13/174.01/427.89ms；没有稳定收益。三组裁取原尺寸对照差异1640/0/1640像素，差异框[36,78,1763,258]；未移动或重采样原图。记录到背景隐藏周期，没有resize事件。局部捕获后出现刷新与字体区域差异，但现有观测不能证明局部捕获是唯一触发原因。decision.json明确adopted=false。

结论：不接入产品、不改正常启动、不宣称降低刷新延迟。后续如继续此方向，须先隔离原生Page.captureScreenshot clip对渲染状态的影响；禁止只用PNG变小作为采用依据。

## 其他核查与边界

已有浅色最坏帧边缘含大量RGB47灰色，最外两行/两列变化集中。父窗WS_CLIPCHILDREN、WS_EX_COMPOSITED、DWM扩展、边框禁用等均有历史失败/阴影回归记录，本轮未重复实施。源PNG为RGB不透明，不能归因透明PNG叠加。所有诊断均不排除边缘验收。

## 验证与清理

clip换算专项2项通过（127.67ms），覆盖五档比例、非法比例和错误坐标方向；驱动语法、git diff --check通过。本轮两次真实运行分别是初次实验与新增时序诊断，没有产品全量测试、构建或UI样式改动。

PID16464/10072退出；端口56368/53284无监听；临时目录Augit-startup-compare-JeuUfi/26e5tG已删除。未启动参考PyCharm、录像器或服务器，无新增常驻资源。ShellModalHost.cs SHA256保持5C7D5BDBC0BCFD2219BB9BBC3E2319E5EB3E9978653954EB2BC0402432DDA209。四网页未改。没有暂存、提交、回滚用户改动；历史拒绝清理路径未触碰。

复验专项：node --test tools/audit/probe-cover-clip.test.cjs。只在调查该假设时运行：node tools/audit/compare-startup-paths.cjs <Playwright模块路径> <新目录> --daily-launcher --only-mode=normal --surfaces --probe-cover-clip。驱动finally清理；异常只按本次输出PID执行taskkill /PID <PID> /T /F。

日常启动仍为powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1，关闭窗口停止。55页/全模态、背景透明帧与≤500ms、终端/冲突逐行、系统阴影四项整体目标均保持未完成；Windows10排除本轮，跨屏DPI未验证。
