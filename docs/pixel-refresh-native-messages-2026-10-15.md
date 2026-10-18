# 背景刷新原生消息与闪亮采样关联

日期：2026-10-15。本批针对撤盖响应后约40ms边缘闪亮，新增一次隔离诊断取证；产品改动全部恢复，整体整改保持未完成。Windows 10排除本次验收，跨屏DPI未验证。

## 方法与结果

临时在ShellModalHost的窗口过程中记录WM_PAINT(000F)、WM_ERASEBKGND(0014)、WM_NCPAINT(0085)、WM_NCACTIVATE(0086)、WM_SHOWWINDOW(0018)、WM_WINDOWPOSCHANGING/CHANGED(0046/0047)、DWM组合/颜色消息(031E/031F)，同时记录cover-show和uncover-start/end。每条仅包含窗口句柄、消息标识、UTC毫秒和Stopwatch计数，不记录用户内容，不改变消息返回值。固定上限4096条，实际108条，未截断。临时通过既有modal-state读取，录像结束后保存native-messages.json。

证据根：artifacts/pixel-followup-2026-10-15/refresh-native-messages/light。原始FFV1录像SHA256：440577a05a046b83e439d9c0fecff97fde5fec4b67ef859797c634fb1f84822c。438采样帧，逐帧检查全部2440620像素，容差2，排除像素0；46帧变化，最大间隙50ms，无重复时间戳，严格门禁退出1。

- 首轮明确为零字节、零子窗的首次遮盖。六轮观测延迟545.08/380.97/391.53/408.19/386.04/404.55ms，首轮仍超500ms；诊断与录屏存在开销，不作为独立性能基准。
- 第38帧首先出现12个角部像素变化，最大RGB差6，发生在遮盖响应前20.152ms。
- 第48帧最严重：3900个变化像素，其中角部132、直边3768、内部0，最大RGB差134。原始采集时间1792052237522.565ms，位于首轮撤盖响应后40.565ms。
- 父窗25168166、遮盖子窗9308134。首轮撤盖期间父窗在1792052237480ms收到WM_ERASEBKGND，在7481ms收到WM_PAINT；子窗隐藏与uncover-end也在7481ms。最严重采样距这些最后消息41.565ms。
- 首轮撤盖后150ms内没有新的已记录消息；WM_NCPAINT只在遮盖子窗显示阶段出现，父窗没有该阶段的新WM_NCPAINT。故本样本不支持“响应后又收到一条较晚WM_NCPAINT才触发闪亮”的解释。已有绘制何时进入DWM合成仍未知，不能把没有新消息解释为原生绘制无关。

native-frame-correlation.json按已验证的原始录像采集时钟，为全部46个失败帧保留最近三条原生消息和时间差。时间相关不证明因果；未拦截WebView内部子窗，也未观测DWM内部Present。fullPresentationVerified仍false，不扩大为无闪帧结论。下一步应针对父窗既有绘制和子窗可见性切换的合成提交取证，而不是重复隐藏顺序、DwmFlush或禁用边框的历史失败试验。

## 验证、恢复与清理

临时诊断构建0警告0错误，8.44秒；采集成功后按修改后SHA256确认无人覆盖，再逐字节恢复当前批次之前的ShellModalHost.cs与capture-refresh-screen.cjs。恢复构建0警告0错误，2.70秒。ShellModalHost恢复SHA256为77B43F8E4C009DFFB3C15288B2D88CEC8BCB5537D137557893C138841E330B9A。没有回滚其他已有修改，未暂存或提交。

整表面分析仅解码一次，后续时钟关联复用分析；原始输入、滤镜、编码、封装及帧时钟核对通过。临时消息记录未进入最终产品，不新增持久日志、依赖或公共契约，因此不运行无关全量测试。临时安装脚本与恢复备份在核对恢复哈希后删除；正式录像、PNG、JSON和本记录保留。

Augit PID11604、录屏PID16820已由finally停止，64523无监听，临时目录Augit-shadow-pair-uJkffo不存在，独立复查通过。未启动PyCharm；历史被自动审批拒绝的清理目录未重试，不宣称历史资源全清。

复验正式实现（不含临时原生消息诊断）：

    node tools/audit/capture-shadow-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/refresh-native-followup Light --refresh-video
    python -B tools/audit/verify-refresh-surface.py artifacts/refresh-native-followup --require-stable
    python -B tools/audit/correlate-refresh-clock.py artifacts/refresh-native-followup
    # 脚本finally自动清理；异常只停止本次返回的进程，不按名称批量终止。
    taskkill /PID <本次Augit_PID> /T /F
    taskkill /PID <本次recorderPid> /T /F

本批未修改四个运行时网页文件，MD5：mockup.js CA9A7349D1CDBA88B33616DE9AE67740；mockup.css E2FC5FAEE0578F3BFADF0A6723F9C8C3；live-data.js 019F54DFDE0629B0DDB67B91B46E50C7；bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。
