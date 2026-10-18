# 背景捕获业务表面隔离试验（2026-10-15）

## 结论

本轮检验两个此前没有直接实测的假设，均未通过，已逐项撤销并恢复 Debug 构建。Release 未装入试验。没有新增产品逻辑或依赖，没有重复全量测试。前一轮日常启动的 32 状态证据仍有效，但不能代替背景刷新验收。

1. 仅在 RefreshSnapshotAsync 的 CapturePreviewAsync 调用期间设置业务控制器 IsVisible=false，finally 恢复原值：首次 modal-refresh 请求后未观察到响应，最终恢复等待超时。不能据此断言所有隐藏 WebView 都不能截图，但该实际流程不可用。未尝试靠增加超时掩盖。
2. 保持 IsVisible=true 和视口尺寸，仅在同一截图调用期间将 Bounds 左边移到完整视口宽度之外，finally 恢复原位置：六次刷新及关闭成功，GDI 采样显著减小差异，但独立 Desktop Duplication 复核仍出现大面积内部画面丢失，因此不保留此方案。第59帧目视为整个客户区白屏、外边框保留，不能称为已经看到了下层业务正文；与此前原实现显示底层正文的失败形态须区分。

## 原始证据

目录前缀为 artifacts/pixel-followup-2026-10-15/，均为普通入口、Light、界面12、正文JetBrains Mono13、DPR1.75。完整1820×1341表面、RGB容差2、排除0像素，不剔除四角。

| 条件 | 目录 | 结果 | 首次/后续流程观测 |
| --- | --- | --- | --- |
| 隐藏业务控制器 | refresh-live-hidden/light | 首次refresh无响应，采集失败 | 无有效完成延迟 |
| 移出父窗裁切区，GDI | refresh-live-clipped/light | 422帧，47变化帧，最多12像素，均在四角 | 538.31ms / 383.45–433.71ms |
| 同一裁切代码，DDA | refresh-live-clipped/dda | 372帧，79变化帧，第59帧2295402像素变化，其中内部2208002 | 564.58ms / 374.40–430.22ms |

GDI录像SHA256为36f1b29d1ab933f28f1a0731c9d506587266ea7d654f1ae9ddf50e12128154f2，DDA为ff443aafea98625ca8f4097457bd02c09c544c9cc27bb96827324fe24af30c0e。DDA第59帧原尺寸解码保留为interior-leak-frame59.png。两组完整分析的sampledWholeSurfacePassed和fullPresentationVerified均false。GDI最大采样间隔50ms，DDA44ms；DDA解码器输出时间基转换的非递增DTS警告，原始ffprobe时间戳无重复、完整解码帧数匹配，但不据此宣称每个显示Present被捕获。DDA相对时间没有冒充UTC或与GDI桥接墙钟相减。

隐藏试验保留modal-surface-trace.json和未完成录像；没有refresh-screen.json，不能用完整采样分析器把失败录像计为通过。该轨迹只证明cover响应成功后DOM隐藏，随后refresh请求无已记录响应，不证明具体COM内部死锁原因。

## 恢复、清理与下一步

两次试验构建和两次恢复构建均0警告0错误，最终恢复构建2.71秒。ShellModalHost.cs SHA256恢复为3CEA5A1ED2EE7BD55EF6F9AE93CDDE607567167080E795BB459732266756B070。四运行时文件均未改，git diff --check通过。未回滚既有混合修改、暂存或提交。

PID13420/25836/25776及录制器780/25904/20664已退出；端口49203/51104/52989无监听；临时目录Augit-shadow-pair-n8nsbt/CuNjU9/OKaN7R已删除。原始证据属于保留交付物。第一次ffmpeg单帧提取的shell转义失败，改用Python参数列表后解码；不修改原录像。

下一步需要改变遮盖表面的合成隔离条件，而非继续增加RAF等待或仅隐藏/移动业务表面。若涉及多个业务WebView或复制可执行DOM，则触及现有架构边界，不能未经确认实施。整体55页/全部模态配对、背景稳定性与500ms、终端/冲突配对、阴影验收仍未完成。Windows10排除本次验证，跨屏DPI未验证。

日常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。正常关闭窗口；异常时只对本轮输出PID执行taskkill /PID <PID> /T /F。试验已撤销，不能用当前源码复跑命令宣称复现上述试验。
