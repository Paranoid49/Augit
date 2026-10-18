# 无CDP正常入口及独立域名反证（2026-10-17）

本批接续已撤销的提前cloak候选，不重复该实验。整体目标仍未完成，未增加55页合格数。工作区既有混合修改保留，未暂存或提交。

## 正式构建恢复

先执行tools/start-augit.ps1 -PrepareOnly，强制非增量Debug构建通过，0警告0错误。恢复后的Augit.dll SHA256为9D5840E0384A0E1003F68AF717209792687FDC6DFF7E7A1B607A1AE81113045F。捕获后DwmFlush、准备消息排队及分段计时保留，初始化前提前cloak已从源码和构建移除。

## 无CDP诊断

新增tools/audit/settings-no-cdp.cjs：执行正常启动器强制构建，复制真实设置，不改主题、字号、DPI或窗口尺寸；复用默认profile。启动子进程环境移除WebView调试参数、替代运行时、profile和脚本调试pipe覆盖。脚本不连接CDP、不注入网页脚本，桌面输入使用computer-use，录像及前景采样只读。

第一批settings-no-cdp-native，PID10052、目录Augit-no-cdp-LoDrzI。浏览器命令行证实默认Session且没有remote-debugging-port。真实点击调用区间1792205376022–1792205376178ms，15秒原始帧目视确认设置已打开。采集流程将24秒信号等待误作全过程期限，未完成关闭即清理，MKV含提前结束诊断；该批不得计完整开关或最终呈现验收。局部40×40初步扫描得到8.290秒变化，但旧临时解码未显式passthrough，该数字仅保留作诊断线索，不作为精确时序。

第二批settings-no-cdp-native-cycle，PID22320、目录Augit-no-cdp-G3BN8f。已改为25秒自动封装后保留应用供核对和正常关闭，不再把录像时限当应用退出期限。DWM窗口168DPI、1.75倍，2880×1716；前景采样全部属于本实例，主窗23855332、设置3868274。真实点击区间1792205519300–1792205519446ms，截图确认设置已呈现。

computer-use的list_windows只返回主窗，未返回Settings子窗。对主窗发Escape并未关闭设置且改变了主窗状态，不能登记关闭耗时；针对已观察子窗截图的Cancel坐标点击，被工具以Chrome Legacy Window不属于所选Augit窗口拒绝。本批没有绕过工具伪造子窗对象或使用PowerShell注入输入。最终按本次PID清理；completeCycleVerified与fullPresentationVerified均为false。

第二批未通过CDP读取实际字形字体，因此配置、源码哈希和截图外观不能替代实际字体证据；历史CDP字体结果不外推本次无CDP实例。此项仍需无调试的运行时诊断或可正确定位owned窗口的输入能力。两批均没有调试端口，不以未连接CDP直接证明无其它测量扰动。

补充离线诊断：第二批1336原始帧，最大时间间隔43ms，40×40探针首次变化在第382帧7.301秒。视频SHA256为ee74a10432397fcac5077d459d70480a5bc735ae45400459e6073e806375d626。首次解码遗漏passthrough导致帧数不符，分析器拒绝产出；纠正后帧数与ffprobe一致，但rawvideo输出有时间基准舍入警告，完整保留在onset-diagnostic.json。另用select直接提取第381/382帧，onset-01/02.png已目视：前者为主窗，后者为已打开Settings，不是整白窗。未逐帧审核全部画面，不据两帧宣称无闪。此局部结果不等于完整呈现时刻；录像启动墙钟与首帧也未严格校准，不将约1.8秒差值写成原生耗时，更不能认定CDP就是根因。

## 独立设置虚拟域候选已撤销

Microsoft的ICoreWebView2_3.SetVirtualHostNameToFolderMapping文档提示.local可能产生导航延迟。仅将不存项目颜色/Log历史的独立设置改为settings.augit.example进行验证，主窗保持augit.local，未迁移或清除用户存储。

正常入口settings-example-origin-startup十次单击原生呈现623/526/2480/531/2560/509/511/531/497/503ms。第3次启动脚本注册72→2065ms发生在导航前；第5次导航132ms、准备2258ms。更换域名仍有秒级慢开，不能将.local当作已证实的单一根因。首轮外部3516ms与原生623ms差异主要在实际click送达前，仍分别记录。

本批实际执行脚本、CSS、WOFF2和采样节点字体已经由runtime-identity核对；候选DLL SHA256为12b10ece37b4222139494f2d8cb3248b84f0dcd9347cb669df8cc0c83d49f9ff。该身份只属于已撤销候选。19项Shell定向与12项相关JS通过只证明回归范围，不代表慢开解决。

因无明确收益，精确撤销本轮EntryVirtualHost与对应两条测试；正常启动器再次强制构建，正式DLL恢复9D5840E0…113045F。保留审计器按实际页面origin核对脚本的改进，并补相似主机拒绝测试。产品主窗和设置最终都仍用augit.local。

## 验证、资源与继续路径

### 原生续接线程反证

新增只读四项托管线程ID，分别记录创建、控制器创建完成、启动脚本注册完成、自身捕获完成。settings-thread-observation正常启动器六次采样全部为线程2，原生呈现2575/2610/512/2614/2567/514ms。第1次准备至捕获2250ms，第2次字体1995.6ms，第4次设置controller.IsVisible从317ms至2439ms，第5次脚本注册2076ms。没有证据支持跨线程作为这些慢开的根因，不引入同步上下文改造。

8项隐藏启动/线程记录测试、定向C#格式通过；17项IndependentSettings定向及最终构建另行核对。最终仅新增诊断字段，正式Debug DLL SHA256变为A4721906A628D6C14F9A859AC7CAF84E0350CC5A9D5B5406AF405F8405F31F22；前述9D5840E0仅是域名候选撤销后的阶段身份。PID9404、端口49699、目录zzjTB5由finally清理。

审计身份/无CDP工具6项、局部帧时序3项通过；Debug随包25资源、共享9资源一致。没有重复全量测试。独立域候选实例21572、端口62184、目录JMBvE7由finally清理。无CDP实例10052/22320及各自录像、前景采集进程均由驱动清理。

最终17项IndependentSettings通过，76ms；Debug25项资源镜像、git diff --check、DOC_CLAIMS_OK通过。文档检查明确提示历史全量快照过期，没有为更新数字重复全量。独立资源核查settings-no-cdp-cleanup-check.json确认本批已登记PID残留0、相关监听0、四个隔离目录残留0。移除5个本目标已结束采集的握手文件，保留所有PNG、MKV及JSON证据，未处理历史被拒绝目录。

优先下一步是只读记录无CDP实例原生阶段并统一视频时钟，避免以外部click耗时混算宿主创建；没有该证据前不再盲改显示顺序、固定延时或预热控制器。设置子窗输入工具局部阻塞不阻止继续冲突、背景刷新和其他配对。

正常启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`。优先关闭窗口；异常仅对启动器返回的本次PID执行`taskkill /PID <本次PID> /T /F`。无CDP诊断：`node tools/audit/settings-no-cdp.cjs artifacts/pixel-followup-2026-10-17/settings-no-cdp-next`，使用尚不存在目录；取证前创建record.signal，完成观察后创建exit.signal。信号和隔离目录由finally清理，原始证据保留。
