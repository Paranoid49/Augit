# 独立遮盖窗口顺序试验（2026-10-16）

用户授权独立无激活PNG遮盖试验后，本批移除原子窗HWND_BOTTOM操作，并为owned窗口加入SWP_NOOWNERZORDER。仍保留单一业务WebView，PNG由专用截图WebView显示。此次取证前景有效，候选仍失败，未同步架构。

证据目录：`artifacts/pixel-followup-2026-10-16/refresh-owned-order/light/`。Desktop Duplication录像350帧，每帧1820×1341、RGB容差2、不排除任何像素；54帧变化，最大采样间隔39ms，无重复源时间戳。前景采样105次、最大间隔96ms、qualified=true；轮询不能排除采样间干扰。最坏第75帧变化2296486像素，内部2209086；已目视原帧，内容为Augit底层项目/文件/Git区域，属于真实内部露底。不能用其余仅边缘变化的帧掩盖这次失败。

六次实测耗时592.90、340.74、335.61、355.79、323.04、345.41ms，首次超过500ms。录像SHA256：170c63e870bb354a46a214e666b429c1d1af100c3969a6af74986c437b0ddfae。DDA相对时间未与UTC精确对齐，不据此断言发生在某一桥接阶段。

候选差异保存为上级目录candidate.diff。源文件恢复前核对候选SHA256 A248896C675AA657F24DC87731835BEA9A4E49A49F55BF0BD32117233D9BE76A及备份；恢复ShellModalHost.cs为5C7D5BDBC0BCFD2219BB9BBC3E2319E5EB3E9978653954EB2BC0402432DDA209。恢复Debug构建0警告0错误、3.11秒；Release未装入此候选。

Augit10084、采样器6480、录像器23728均已退出，端口53196无监听，临时目录Augit-shadow-pair-pcOhZH不存在。未启动PyCharm，未处理历史拒绝清理路径。普通启动对齐成果保留；背景刷新及总体目标均未完成。

启动复验：`node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新目录> Light --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames --activate-main`。该命令使用当前构建，不自动重装历史候选。正常完成由finally清理；异常按本次输出PID执行`taskkill /PID <PID> /T /F`。日常启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`，关闭窗口停止。
