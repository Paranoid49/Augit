# 无CDP慢开期间的窗口响应（2026-10-17）

## 新证据与结论

本批产品逻辑未改，接续设置主题回读修复后，针对未覆盖的无CDP慢开阶段同步采集既有WM_NULL响应探针。旧探针只覆盖附CDP的快速开窗，不能代替本批结果。正常入口再次复现慢开：原生起点至presented为3369.0071毫秒，自身CapturePreview等待2652.7855毫秒。捕获期间41次WM_NULL均成功，最大3.023毫秒，最大采样间隙95毫秒。

这不支持“整个原生线程在捕获期间持续停止处理发送消息”的解释；但发送消息可在COM重入时得到处理，不能据此宣称全部消息泵正常、排除投递消息延迟或采样间隙阻塞，更不能证明GPU与网页流畅。没有新增等待、预创建或显隐候选，没有重做已否证的呈现实验。下一步应定位CapturePreview请求到回调的浏览器/合成阶段，不再仅凭慢开时间盲改窗口顺序。

## 正常入口和身份

证据目录：artifacts/pixel-followup-2026-10-17/settings-no-cdp-response。tools/start-augit.ps1强制非增量Debug构建，真实配置副本、默认Session、无CDP端口及连接，正常DPI。源配置与副本SHA256均为ed4b3bd3dadec93cf966fa413832763361dc1ffdb67541b92666e3dcf807db09。

PID4588的实际映射Shell SHA256为94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46，Infrastructure为1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80。仍映射Nahimic AudioDevProps2.dll，无法证明其因果；用户允许临停服务，但既有权限拒绝未解除，本批没有重复停服务。按产品规范，本组只作诊断，不作无注入环境性能验收。

源live-data.js SHA256为62E1C15EC0056B5C261EEF0EEF7B0E91E7023E3C3C446F7B13DA1F2F71B12A3F、MD5为E1EE6539727817718DBBFC752747BB46；其余网页及Shell与上一轮一致，Release未更新。此次无CDP未读取执行脚本或实际字形；设置显示Microsoft YaHei UI12，主窗屏幕DPI168、录像2880×1716，不把显示名称、磁盘哈希或上一轮CDP身份冒充本次实际字形与执行证据。

## 时序与桌面

Computer Use实际点击调用区间1792224055206–1792224055330；创建锚点UTC1792224056381.6557。工具返回至创建的约1051.66毫秒单独保留，不混入控制器或捕获耗时，也未归因。

| 阶段 | 耗时毫秒 | 区间内响应样本 | 响应超时 | 最大响应毫秒 | 最大采样间隙毫秒 |
| --- | ---: | ---: | ---: | ---: | ---: |
| created→presented | 3368.7724 | 52 | 0 | 3.023 | 95 |
| 控制器创建 | 90.8074 | 2 | 0 | 0.2876 | 63 |
| 脚本注册 | 126.1206 | 2 | 0 | 0.2807 | 63 |
| 自身捕获 | 2652.7855 | 41 | 0 | 3.023 | 95 |

全部527样本超时0、最大13.8925毫秒；原生阶段均在线程2。响应分析器用JavaScript解析UTC至毫秒，存在不足1毫秒截断；原生耗时以单调时钟为准。探针目标始终是本实例主窗11667046，不伪称直接检测设置子窗。

settings-clock-analysis.json通过既有严格GDI帧时钟关联：467完整帧、最大采样间隙103.253毫秒，前景采样全部属于本实例。录像SHA256为4e2bda103fd67adfde41dd1ba96e1424c94491842769d66d6ea8d5fb4400492d。

原生presented UTC1792224059750.662位于第344/345帧墙钟1792224059689.995/1792224059758.610之间；两张完整原始PNG已目视，分别为主窗和完整Settings。桌面出现被夹定在原生锚点后3308.339–3376.954毫秒，不等同精确Present时刻。不以两张图或467帧时钟映射宣称所有画面无闪，fullPresentationVerified仍false。

当前computer-use仍只枚举主窗，截图虽包含owned设置子窗，但没有独立可选目标。未重试此前被拒的子窗输入，未伪造HWND或换通道绕过。最终走采集器finally结束本次进程，未取得正常Cancel或关闭周期，completeCycleVerified仍false。

## 工具验证与清理

新增correlate-settings-response.cjs，严格检查实例、原生时钟、阶段、响应覆盖、解析错误，输出每阶段样本数、超时、最大响应和采样间隙及源文件哈希，拒绝覆盖原报告。四项新增测试覆盖长捕获、稀疏采样、超时、错实例和缺失时钟；初版反例只移除最后一点却仍包含终点，已把反例改为确实缺失终点，未放宽分析规则。与无CDP采集三项合计7项通过，168.56毫秒。

cleanup-independent.json独立确认Augit4588、响应探针21508、前景记录器17040、ffmpeg22600均退出，临时目录Augit-no-cdp-sdDKpH已删除；未开CDP端口，握手文件由finally删除。分析与提帧进程正常结束，没有新增服务器。历史PyCharm清理自动审批阻塞仍保留，不宣称全局零残留。工作区混合修改未回滚、覆盖、暂存或提交。

收尾补验既有响应分类器2项通过，115.19毫秒；本批共9项不同定向用例通过。JS语法、UTF-8/LF、git diff --check、源证据关联与握手文件清理检查通过，未跑全量测试。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关窗；异常只针对本次核实实例：taskkill /PID <本次PID> /T /F。

复验关联：node tools/audit/correlate-settings-response.cjs <含原生阶段及response-probe.json的新证据目录>。采集使用既有settings-no-cdp.cjs --gdi-clock与shell-response-probe.cjs；响应探针须在开窗前启动并在记录后stop，不新增常驻进程。

设置慢开、无CDP完整关闭、背景透明帧与延迟、55页/全模态、终端完整配对及系统阴影未完成。复杂Git/冲突精修暂停，未删除功能、不算通过；Windows10排除本轮，跨屏DPI未验证，整体goal仍active。
