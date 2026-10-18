# 设置捕获调用与异步完成分界（2026-10-17）

本批接续无CDP慢开期间窗口仍响应的证据，只增加capture-issued原生阶段：CapturePreviewAsync返回Task后立即记录，再等待同一Task。捕获参数、激活、遮蔽、异常处理、销毁保护和揭示顺序均不改变；没有新增功能、页面、固定等待或预热。该诊断用于区分COM调用本身与调用后的异步等待，不能作为性能修复。

## 正常入口实测

两批均通过tools/start-augit.ps1强制非增量Debug构建、真实配置副本、默认Session、正常DPI，无CDP端口及连接。实际点击用computer-use，桌面记录用既有GDI/FFV1；未重试被拒的设置子窗输入。证据分别为artifacts/pixel-followup-2026-10-17/settings-capture-issued和settings-capture-issued-second。

| 批次 | PID | 起点至presented毫秒 | 控制器创建毫秒 | 捕获调用返回毫秒 | 返回后等待毫秒 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 首批 | 22376 | 813.9555 | 94.3329 | 2.9105 | 109.7215 |
| 第二批 | 7972 | 3216.9114 | 2487.0535 | 3.0379 | 99.3058 |

第二批仍慢开，但慢点移至控制器创建；两次捕获均快，故尚未捕到新增分界下的慢捕获样本，不能反推上一轮2652.7855毫秒属于哪一段。不能据快速捕获删去既有白帧保护。后续需针对偶发COM阶段取得可归因证据，而不是重复无改动采样直到出现漂亮数字。

首批点击调用UTC1792224534577–1792224534735，第二批1792224650736–1792224650880，原始input.json保留。两批原生阶段均线程2。严格时钟关联分别490/467帧，最大间隔78.206/71.274毫秒，前景采样均属于本实例。原生presented分别夹在337/338、377/378帧之间，四张2880×1716原始PNG已目视，均为主窗→完整Settings。未全帧像素验收，不宣称无闪，也未取得无CDP正常关闭周期。

## 构建、配置与字体

Shell实际映射SHA256由94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46变为4CAC07952E541B48BCF069F0F7DDA4B03783A8BB4F12BF86329129D9148C9F75。Infrastructure保持1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80，仍映射Nahimic，干净环境性能验收未完成。Release未更新。

四网页MD5本批重验不变：mockup.js=0EEBB52037A8EEABDB81DA777E8C1FFE，mockup.css=00FA3357EC646E91030A9DFDD925DAA9，live-data.js=E1EE6539727817718DBBFC752747BB46，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

另一次正常启动器附CDP身份/单次打开Cancel回归位于settings-capture-issued-identity，PID484，通过。实际Shell为新哈希，主窗执行live-data.js SHA256=62E1C15EC0056B5C261EEF0EEF7B0E91E7023E3C3C446F7B13DA1F2F71B12A3F；两窗DPR1.75、Microsoft YaHei UI12，主窗实际正文为随包JetBrains Mono13。无CDP两批没有读取执行脚本或实际字形，这份附CDP证据不外推无CDP实例，也不代替重复打开或完整关闭压力验收。

## 验证与清理

修改src/Augit.Shell/ShellWindow.Settings.cs，更新tools/audit/settings-hidden-startup.test.cjs，增加调用返回→await→销毁保护→完成→揭示顺序断言。10项JS定向通过，133.99毫秒；SettingsStageTraceTests 17项通过，149毫秒；限定文件dotnet format whitespace、九项共享资产及git diff --check通过。未跑全量测试。

三份cleanup-independent.json独立确认：22376/18284/21252、7972/27900/27516、484均退出；临时目录Augit-no-cdp-Qmrl2f、Augit-no-cdp-p1avuE、Augit-independent-settings-AgsGL8均删除，57178无监听。录像关联、提帧进程正常退出，未留服务器。本批原始证据保留，历史PyCharm清理自动审批和Nahimic服务权限阻塞保持，未绕过。未回滚、覆盖、暂存或提交既有混合修改。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关闭；异常仅对本次核实实例执行：taskkill /PID <本次PID> /T /F。

诊断采集：node tools/audit/settings-no-cdp.cjs <新证据目录> --gdi-clock；原生阶段包含capture-issued后，可用单调时间直接区分调用与等待。时钟关联：D:/Python/Python3119/python.exe -B tools/audit/correlate-settings-clock.py <新证据目录>。

整体未完成。慢开、闪帧、刷新500毫秒、55页/全模态、终端完整配对和阴影仍有缺口；复杂Git/冲突精修继续暂停，不作通过或删除结论。Windows10排除本轮，跨屏DPI未验证。
