# 背景刷新双层显隐隔离试验

日期2026-10-15。承接原生消息记录，本批分别隔离WebView控制器和承载子窗的撤盖动作。两种方案都仍出现直边闪亮，均已撤销，正式产品保持原实现。整体整改未完成；Windows10排除本次验收，跨屏DPI未验证。

## 试验依据与范围

上一批闪亮采样发生在撤盖应答后40.565ms，最后父窗WM_PAINT后41.565ms，期间未再收到被记录的消息。因此本批不再交换两个隐藏动作的顺序，而是分别移除一个隐藏动作，检查某一层可见性转换是否必要。图片就绪、首图定尺、显示后两帧、DwmFlush、四边1px框架扩展、透明默认底色均保持。

1. 仅隐藏控制器：保留 `_captureCover.SetVisible(false)`，暂不对承载子窗执行ShowWindow(SW_HIDE)。子窗空壳保持可见，控制器图片隐藏。
2. 仅隐藏承载子窗：保留ShowWindow(SW_HIDE)，不将控制器IsVisible设false；试验状态的captureCoverReady同时检查真实子窗可见性，以免把隐藏父窗里的控制器误报为正在遮盖。没有修改录屏首次身份门禁或完整表面容差。

两组均是明确零字节、零子窗首次开始，每组只采集一次浅色六轮刷新。逐采样帧比较1820×1341完整表面，共2440620像素；RGB容差2，排除0像素。

| 方案 | 帧数/变化帧 | 最严重变化 | 首次ms | 后续五次ms |
| --- | --- | --- | --- | --- |
| 仅隐藏控制器 | 438/49 | 3902像素、最大RGB134 | 517.18 | 380.87/416.71/388.10/374.02/363.79 |
| 仅隐藏承载子窗 | 447/48 | 3902像素、最大RGB134 | 576.18 | 385.39/406.25/373.37/417.18/386.53 |

两组最严重帧分别47与48，均为角部132、直边3770、内部0；每组仅一帧有直边变化。最大采样间隙均50ms，无重复时间戳，严格检查均退出1。变化计数相同不证明两张像素逐值完全相同。两组均失败，故不支持通过仅省略一个隐藏动作消除闪亮，也不支持将单次延迟波动解释为优化。未进行无收益的深色重复试验。

## 原始证据

- `artifacts/pixel-followup-2026-10-15/refresh-controller-only/light`，录像SHA256：cb08c5af51da261a94d93745de2a58f64abdbc6a0757fbbe5621948bf4094e20。
- `artifacts/pixel-followup-2026-10-15/refresh-window-only/light`，录像SHA256：f2ebff8fe2bf7e9506e0fbf177c8875fe4c894ddaf5c304755be4bfc43f36f90。
- 每组保留原始FFV1、屏幕PNG及元数据、六轮事件、整表面分析、实验前后源码哈希。没有删除失败帧、重采样图像或改变通过条件。

本批进一步表明不能把两个显隐动作的其中一个简单当作唯一原因。子窗边缘参与父窗DWM扩展区域的合成、实时与快照控制器的表面切换仍需要直接证据；不据此移除边框、系统阴影或放宽角部容差。

## 恢复、验证与清理

每组结束后先核对当前源码仍为本次试验哈希，再逐字节恢复之前文件，并删除本批恢复备份。ShellModalHost.cs最终SHA256为77B43F8E4C009DFFB3C15288B2D88CEC8BCB5537D137557893C138841E330B9A。两组试验构建耗时2.55/2.52秒，恢复构建2.64/2.62秒，均0警告0错误。无产品逻辑保留，无新增依赖，未执行无关全量测试。未覆盖、回滚、暂存或提交已有混合修改。

实际PID19876/19220、录屏PID3108/7224均退出；端口62451/57351无监听；临时Augit-shadow-pair-ARXfkv/qLPWH1均不存在，已独立复查。未启动参考PyCharm，未重试历史审批拒绝的清理。

正式实现复验命令（不包含已撤销试验）：

    node tools/audit/capture-shadow-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/refresh-visibility-new Light --refresh-video
    python -B tools/audit/verify-refresh-surface.py artifacts/refresh-visibility-new --require-stable
    # 驱动finally自动清理；异常仅停止本次日志中的PID。
    taskkill /PID <本次Augit_PID> /T /F
    taskkill /PID <本次recorderPid> /T /F

四个运行时文件未变，MD5：mockup.js CA9A7349D1CDBA88B33616DE9AE67740；mockup.css E2FC5FAEE0578F3BFADF0A6723F9C8C3；live-data.js 019F54DFDE0629B0DDB67B91B46E50C7；bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。
