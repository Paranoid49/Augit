# 撤盖后边缘背景纹理瞬变（2026-10-17）

本批保留上轮客户区定位修复，不新增产品逻辑。正常入口证据进一步表明：顶部闪变时与物理下方背景的颜色相关性消失，末帧恢复；不是永久换图结果。仍未证明DWM内部因果，也未修复闪烁或500ms延迟。复杂Git/冲突精修保持暂停。

## 正常入口证据

目录artifacts/pixel-followup-2026-10-17/refresh-edge-background-settled。tools/start-augit.ps1强制非增量Debug构建、真实设置副本、默认Session、正常DPI，附CDP及GDI录像。采集未改DOM、字体或缩放，四次真实文件写入走产品监听。末态资源在录像结束后读取，不影响已记录的闪变时序。

- 最大化所有者客户区原点(0,0)，模态及背景区域[1148,733,1733,985]；定位断言通过。
- 初始背景SHA256=C5AB0735340A0A4BCEEFA13926AEE0DC13ED047384D5DAD6BD352288B0CE868F；末态4B4CF8FC7C3794AEE7DA2C69ED9936A31269EC6EEC6434AD81AF896D75020829。两者均直接读取已加载资源并与对应宿主backgroundHash核对，末态按哈希识别，避免把复用遮盖PNG误当背景。
- 完整背景图发生真实变化，但模态屏幕下方585×252区域在RGB容差2下变化0像素，顶部两行也为0。末态并非瞬变时的原子背景采样，此限制保留。
- 完整录像1263帧，最大间隔39ms，无重采样；最差第67帧191变化像素，其中113顶部边缘、78输入光标。最差PNG已与原录像指定帧逐字节核对并目视，未排除任何像素。录像SHA256=6EF5C16A7002D86B5CC67339E1B5861511B2D62FF181BD807563DE352A0EF53C。
- 最差帧位于撤盖应答后37.145ms。四次表单隐藏区间内未采到变化帧，不推断未采样的Present；最后一帧与基线RGB零差异，包含全部边缘、光标位置和正文。
- 最后撤盖600.7/499.4/508.9/469.8ms，两次超500ms；四轮最终正文及正常Esc关闭模态通过。附CDP、录像及Nahimic注入，仍只能作诊断性能数据。

## 背景相关性与验证口径订正

上轮用顶部前半拟合、后半留出；本轮检查发现后半几乎纯色，因此原低残差不能作为包含纹理的独立验证。本批新增交错横坐标：顶部第二行x=20至564，偶数273像素训练，奇数272像素留出。两组背景标准差60.847/56.909，均有文字纹理；任一组纹理不足则拒绝。没有搜索平移或调整图像。

模型screen=a×background+b：

| 时刻 | a | b | 训练MSE | 留出MSE | 留出最大通道误差 |
| --- | --- | --- | --- | --- | --- |
| 系统基线 | 0.53656 | 47.06783 | 1.53602 | 1.50016 | 3.10934 |
| 最差帧 | -0.00478 | 185.11065 | 1.63276 | 1.59128 | 3.10782 |
| 最后一帧 | 0.53656 | 47.06783 | 1.53602 | 1.50016 | 3.10934 |

这支持“正常边缘含背景纹理，瞬变时接近纯灰，随后恢复”的诊断。不能凭相关性声称DWM具体使用该线性公式，也不把正常边缘当成业务源图漏画。后续针对撤盖后的原生边缘提交；不重复黑刷擦除、WM_PAINT验证、背景重叠换图、DWM边距或固定等待等已有反证。

## 工具、身份与验证

capture-modal-edge-source.cjs新增captureSettledBackground，严格校验末态版本、遮盖隐藏和资源唯一哈希；所有CDP会话finally释放。refresh-daily-file.cjs仅在显式--trace-edge-source时录像后读取末态资源。新增analyze-edge-background-transition.py与测试，检查背景身份、布局、原图尺寸、录像哈希、指定帧完整解码以及纹理留出诊断。初始edge-background-transition.json保留；增加末帧验证后另存edge-background-transition-recovery.json，不覆盖旧分析。

12项采集/刷新JS用例通过1157ms，4项Python诊断测试通过21ms；覆盖重复背景哈希拒绝、错误加载版本、CDP释放、训练/留出无纹理拒绝、独立验证失败和解码截断。9项共享资源、JS语法、UTF-8/LF及git diff --check通过。产品未变，未重跑Shell全量。

实际映射Shell仍B8F2BDAAD19F9B5740DF1BEC45CE1F84C38E26233CB77FB1558180BD54CE0B62，Infrastructure仍A08EB839CC37A2E71BFB5CF8C03C2BCD4FA74BE596CCECDC9D2222E12C27964B。执行live-data仍31964BB95E93477B66EC159588AC14B805B3339DA3BAC69EA52B8B2EFC8A1BFC，UI Microsoft YaHei UI12、正文实际JetBrains Mono13、DPR1.75，Nahimic仍映射。四网页MD5未变：0EEBB52037A8EEABDB81DA777E8C1FFE / 00FA3357EC646E91030A9DFDD925DAA9 / D50C01AF5311343AC7F8E03FA2E2F0FC / F297BF8769E249CA0D804F4F4EEEF5A9（依次mockup.js、mockup.css、live-data.js、bridge.js）。Release未更新。

cleanup-independent.json独立确认PID11020/21856/15600退出、60758无监听、Augit-daily-refresh-y6mQV8已删除；分析解码与单测进程已结束，临时夹具由finally清理。历史参考实例清理及Nahimic服务权限阻塞保持，不宣称全局零残留。没有提交或暂存混合修改。

## 启停及剩余范围

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关窗；异常仅对核实的本次PID执行taskkill /PID <本次PID> /T /F。

复验：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --trace-edge-source --verify-client-placement --gdi-clock --trace-surfaces

分析按顺序运行analyze-static-modal-video.py、analyze-edge-background-transition.py、correlate-daily-refresh.py、analyze-daily-surface-clock.py，均传入新目录。

正常启动/设置开关慢、完整透明帧与500ms、55页全部模态、终端完整配对、系统阴影仍未整体通过。复杂Git/冲突精修暂停但未删除，Windows10排除本轮、跨屏DPI未验证，整体goal保持active。
