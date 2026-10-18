# 设置回读异步续接保护（2026-10-17）

本批修复loadSettings在字体度量等待结束后的过期续接。旧配置已被新回读取代时，旧任务仍会恢复旧面板尺寸、刷新终端字体并标记设置就绪；现在重新检查请求代次、工作区对象及设置对象身份后才收尾。没有改变原生开关顺序、字体度量实现或增加等待。不能把该修复称为设置慢开的根因修复。

## 反例与代码

tools/audit/settings-typography-reload.test.cjs执行真实loadSettings函数，明确等待第一轮进入字体异步阶段，再应用第二轮配置，最后释放第一轮。修前面板应用顺序为480→320，旧尺寸覆盖最新尺寸；修后只有480。该可控顺序仅用于定向测试，不放入产品或实机验收环境。

web/src/live-data.js在await applyTypographyPreview之后检查canApply()、window.__augitLive与live.settings身份。新增请求失效、工作区替换、设置对象替换三类测试，均验证旧续接不刷新终端、不恢复面板、不标记就绪。共享applyTypography自身已有generation保护，本批不重写其实现。既有初读复用、后续回读、字体变化、普通设置入口与精简页面测试保持通过。

## 正常入口证据

证据：artifacts/pixel-followup-2026-10-17/settings-reload-generation。新增independent-settings.cjs --typography-cycle定向分支，由tools/start-augit.ps1强制非增量Debug构建，真实配置副本、默认Session和正常DPI，附CDP。走真实设置入口及Apply/OK/Cancel按钮，不延迟桥接应答或模拟字体加载。只修改本批隔离配置，不修改用户真实设置。

真实字号12→14→12，两次Apply后主窗配置与computed fontSize均一致，typographyReady完成；OK关闭、重新打开显示12、Cancel关闭成功，网页错误为空。两次原生创建至呈现290/437毫秒；普通单击测量，不带八次并发打开压力。这个短样本不能推翻既有无CDP秒级慢开反例。

真实配置没有projectPanelWidth和bottomPanelHeight字段，两者仍未写入；主窗sideWidth为空、bottomHeight为clamp(180px, 31vh, 305px)。因此本批实机只证明字号保存与重开正常，不证明带已保存面板尺寸的竞态实机复现或尺寸恢复验收。raceForced=false，未新增桌面录像，不声明无闪或无CDP完整周期通过。

main-runtime-identity.json、settings-runtime-identity.json、process-identity.json和mapped-images.json保存真实加载身份。DPR1.75，Microsoft YaHei UI12，主窗正文随包JetBrains Mono13；设置没有正文控件，不外推正文采样。最终live-data.js MD5由57A689BAAFB9B22608F29526B12CEFE6变为C281B1EAC844573EE688419F893F6A3A，执行SHA256=64F76701A51FA399F02645290F7F38E2144231FC052385A03A14393054641903。Shell保持94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46，其他三网页未改，Release未更新。

## 验证、清理和接续

11项设置定向用例通过，约175毫秒；9项共享资源、JS语法与git diff --check通过。首版测试仅等待固定微任务轮数导致测试同步失败，改为明确的字体开始信号后才取得480→320的产品反例；不把测试自身失败当产品证据。未运行全量测试。

cleanup-independent.json独立确认PID20968退出、57943无监听、Temp/Augit-independent-settings-IU0ZCM已删除。未启动录屏或测试服务器。历史参考清理自动审批阻塞及Nahimic权限阻塞保留，不宣称全局零残留。既有混合修改未回滚、覆盖、暂存或提交。

设置慢开、无CDP完整开关、背景刷新闪帧与500毫秒、55页全模态、终端和完整阴影仍未完成。复杂Git和冲突精修暂停；Windows10排除本轮，跨屏DPI未验证，整体goal保持active。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关窗；异常只终止本次核实实例：taskkill /PID <本次PID> /T /F。

复验：node tools/audit/independent-settings.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --daily-launcher --typography-cycle --single-open
