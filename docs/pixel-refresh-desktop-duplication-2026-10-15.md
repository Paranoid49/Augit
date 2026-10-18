# 背景刷新独立桌面复制采集

日期2026-10-15。整体目标未完成；Windows10排除本次验收，跨屏DPI未验证。上一批内缩复采失败后，不再继续猜测窗口样式。本批在未修改产品的前提下增加Desktop Duplication采集路径，交叉核对GDI样本。

## 新证据

两次独立采集均捕获到Settings表面短暂显示底层文件内容，以及单独的边缘闪亮。首份frame-7.png已目视确认是底层notes.txt及Changes区域，frame-14.png显示Settings恢复但边缘有变化。因此不能将此前边缘变化简单解释为GDI独有假象；同时也不能把Desktop Duplication取得的每一帧直接等同于显示器完整Present。两种路径不是同步采集，不能用它们断言某一具体GDI帧漏采。

当前产品ShellModalHost.cs SHA256保持77B43F8E4C009DFFB3C15288B2D88CEC8BCB5537D137557893C138841E330B9A。原有首图、两帧保护和DwmFlush不变。新增调查重点是为什么实际表单在CapturePreview期间隐藏时，独立桌面复制仍会取得底层内容；不能再把“GDI录像内部变化为0”当作整面保护已经完成。

证据根artifacts/pixel-followup-2026-10-15/refresh-desktop-duplication。

| 目录 | 帧数/变化帧 | 整面底层内容帧 | 边缘帧 | 时序 |
| --- | --- | --- | --- | --- |
| light | 58/29 | 7/10/22/46/55；每帧2295955变化像素 | 14；3898像素 | 8个重复时间戳，最大间隙717ms |
| precise-light | 65/27 | 40/50；每帧2295955变化像素 | 15；3885像素 | 重复时间戳0，最大间隙3003ms |

每帧1820×1341、2440620像素，RGB容差2、排除0像素，严格门禁均退出1。两组首次均明确零字节/零子窗，首次观测528.94/489.92ms；后续五次分别358.58/337.66/390.62/351.65/377.17ms与350.40/363.43/373.43/366.10/359.32ms，不是独立性能基准。

录像SHA256：light为d0758902788e4b57061ae35e070d6e97b80b63507d56a2495f496b96eed4ea97；precise-light为b480a4bb0520171d2c4e1040ca7a75522e156eaac1afa30df5c071fe0808856d。保留全部原始帧、日志、清单、前后截图与分析，不删除异常或重复时间戳。

## 工具与限制

FFmpeg本机帮助确认支持ddagrab；只读Win32查询确认当前只有一块2880×1800显示器，物理原点(0,0)。新增显式--desktop-duplication模式，output_idx=0、BGRA8位、draw_mouse=0、dup_frames=0、hwdownload且不缩放，FFV1无损编码。默认仍使用既有GDI路径；此模式仅用于已核实零原点单显示器，本批不扩展为跨屏支持。

第一份默认1/60编码时间基量化了相邻更新，产生8个重复时间戳；第二份保留1/1000000编码基准，保存录像重复为0。rawvideo解码输出仍报告自身默认时间基的DTS量化警告；已核对实际解码帧数等于ffprobe帧数，不隐去或丢弃帧。事件源不复制静态帧，因此长间隔不能直接等同于漏显示帧，也不能反过来证明没有漏帧。

采集器增加30秒墙钟上限，避免无桌面更新时一直等待录像媒体时间截止；超时只停止本次仍存活的录屏进程，finally回收并保留日志。独立参数模块测试原始坐标、无缩放、无重复帧生成、拒绝非法区域、默认路径保持及超时清理。分析器显式校验DDA源路径、区域及原始尺寸；GDI墙钟关联器明确拒绝DDA相对时钟，不对两种时间基直接相减。

最终8项Node、8项时钟、6项整表面测试通过，共22项；相关JS语法和git diff --check通过。没有修改产品代码、构建产品或运行全量套件。图片提取初次PowerShell过滤器转义失败，修正为Python参数数组后成功，失败未产生证据图。

## 清理与复验

Augit PID25432/3088、录屏PID21392/25328均停止，端口59982/60246无监听；专用Augit-shadow-pair-Zac95e/LJpCtN均不存在，已独立复查。FFmpeg解码/提取均正常结束；原图、录像、日志为正式证据保留。未启动PyCharm，不重试历史审批拒绝清理。

    node tools/audit/capture-shadow-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/refresh-dda-new Light --refresh-video --desktop-duplication
    python -B tools/audit/verify-refresh-surface.py artifacts/refresh-dda-new --require-stable
    node --test tools/audit/refresh-recorder-arguments.test.cjs tools/audit/refresh-cover-identity.test.cjs
    python -B -m unittest discover -s tools/audit -p test_refresh_clock.py
    python -B tools/audit/test_refresh_surface.py
    # 正常由finally清理；异常仅停止本次返回的PID。
    taskkill /PID <本次Augit_PID> /T /F
    taskkill /PID <本次recorderPid> /T /F
