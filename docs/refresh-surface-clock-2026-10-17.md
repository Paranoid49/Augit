# 正常入口隐藏与恢复时序（2026-10-17）

本批接续同值刷新修复，继续定位真实内容刷新闪帧。没有修改产品等待、窗口显隐、裁切或边框。新增证据把本组异常限定为首次撤盖应答之后的两帧边缘变化；四次表单隐藏期间采到的变化仅为光标。既有整块正文露出反例未复现也未解决，整体目标保持active。

## 采集与身份

tools/audit/refresh-daily-file.cjs新增--trace-surfaces，复用已有trace-modal-surfaces.cjs只读记录表单隐藏/恢复和原生遮盖应答，不改桥接参数、返回值或时序。采集仍走tools/start-augit.ps1强制非增量Debug构建、真实配置副本、默认Session、正常DPI，附CDP和GDI桌面录像。外部两次连续写入本批临时Git夹具，每轮两次git/status串行，最终正文版本为2/4/6/8。

证据目录：artifacts/pixel-followup-2026-10-17/refresh-daily-surface-clock。runtime.json、process-identity.json和mapped-images.json记录实际身份；产品沿用上一轮live-data.js MD5=51F31F911876565442A0FA444FF0E84F、执行SHA256=216793BB5DFDDE14470DDA0464C4F3CF1D9D170C78CEBF32118D0FBA25BD72DE，Shell SHA256=94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46。DPR1.75、界面Microsoft YaHei UI12、实际正文随包JetBrains Mono13；Release未更新，未打开页面的字体不外推。

## 完整帧与阶段结果

1252帧、585×252完整表面、RGB容差2、最大采样间隔39毫秒；未裁边、重采样或删除光标像素。录像SHA256=365cf0d1487da90b1a0b8aba5543d287fe5a908ddbd8d4d7be8a14d0f5ef3b6a。GDI原始输入、滤镜、编码、封装及视频时间戳全部通过既有严格关联器。

四次最后撤盖分别660.8/588.8/538.4/561.8毫秒，仍未达到500毫秒。四次隐藏区间135/131/129/153毫秒；遮盖应答均报告ready、visible、topmost为true。隐藏区间内部（距边界超过3毫秒）的已变化帧数5/8/4/6，最大均78像素，位于光标矩形218,107至221,133。三毫秒仅标记阶段归属的不确定边界，未从完整帧检查排除任何帧。

最差第70帧输入墙钟1792222635057.235，位于首次撤盖应答后50.235毫秒、DOM恢复后65.235毫秒，无在途桥接阶段重叠。第69/70/71/72帧像素变化为0/187/187/0；两帧录像相对时间1.396和1.405秒，不能据此声称完整闪烁持续时间只有9毫秒。edge-sequence.png保留四帧，edge-difference-mask.png完整尺寸标出左侧、左上和上边缘异常；worst-frame.png已目视，不是整块正文露出。完整报告仍fullPresentationVerified=false。

新增analyze-daily-surface-clock.py及4项测试，拒绝截断、缺失恢复、遮盖未就绪、层级不符、时钟漂移及不同批次记录。surface-clock-analysis.json为初版结果，surface-clock-validated.json保留加强后的双记录匹配及源文件哈希，不覆盖旧证据。工具只关联已变化帧，不将缺失记录推成零变化；原生应答仅证明当时状态，不能证明区间全程的桌面合成或因果。

## 验证与清理

10项JS审计回归通过，约1.14秒；4项新增Python及13项既有时钟回归通过。JS语法和git diff --check通过。未跑全量测试，产品源码与九项共享资源本批未改，不重复上一轮资产实验。

cleanup-independent.json独立确认本批Augit16456、前景监视26724和ffmpeg13076已退出，端口53937无监听，Temp/Augit-daily-refresh-RX2sxe已删除。分析命令同步退出，未启动服务器。历史参考清理自动审批阻塞和Nahimic服务权限阻塞保持，不宣称全局零残留。未回滚、覆盖、暂存或提交既有混合改动。

## 下一步与复验

新证据支持优先检查恢复后撤盖与边缘合成之间的关系，不支持重新增加遮盖自身CapturePreview或固定等待。此前整块正文露出仍需在同一时钟下定位；不能用本组未复现代替修复。设置慢开、无CDP完整开关、500毫秒、55页/全模态及完整阴影仍未完成；复杂Git和冲突精修继续暂停。Windows10排除本轮，跨屏DPI未验证。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关闭；异常仅对本次已核实实例：taskkill /PID <本次PID> /T /F。

正常入口采集：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --burst --gdi-clock --trace-surfaces

依次运行tools/audit/analyze-static-modal-video.py、correlate-daily-refresh.py、analyze-daily-surface-clock.py，参数均为新证据目录。各工具拒绝覆盖既有输出；不要为重复生成分析重新采集产品。
