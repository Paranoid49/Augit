# 连续设置回读共享字体等待（2026-10-17）

## 根因及修复

接续前批同值刷新整改后，核对既有捕获裁切反例，未重做已失败候选。设置关闭与提交通知可能相邻到达，均触发loadSettings：前一次读取先写live.settings再等待字体，第二次读到相同字体会误认为无需等待，直接恢复面板和标记设置就绪。此前只保护旧续接，未覆盖新回读跳过在途字体任务。

新增真实loadSettings函数回归，修前证明第二次回读在字体未结束时已经完成，断言失败。修复按工作区live对象保留settingsTypographyTask及四项字体参数键：相同配置共享当前Promise，不重复度量；不同字号创建新的任务。字体开始时撤销settingsReady，成功且仍为当前配置才恢复面板和标记就绪。finally仅清除自己的任务，旧任务不能清除更新任务；失败后保持未就绪，后续同配置重新尝试。原有代次、工作区、设置身份和主题预览保护保持。

没有合并配置读取，没有缓存后续配置，也没有增加固定等待或测试专用分支；只等待真实在途字体工作。权威约束沿产品字体设置、架构字体度量后恢复面板、现有设置保存/取消语义，不新增功能或页面。

## 分批验证

- 新回归修前失败；新增共享等待、失败重试和旧任务清理三个行为覆盖。
- settings-typography-reload、settings-preview-reload、settings-save-close、settings-open-singleflight共22项通过，223.23ms。
- JS语法、9项共享资源、UTF-8/LF与git diff --check通过；未跑全量。

## 正常入口证据

目录artifacts/pixel-followup-2026-10-17/settings-shared-typography。通过tools/start-augit.ps1强制非增量构建、复制真实配置、默认Session、系统DPI，附CDP且普通单击，不加延迟或重排应答。字号12→14→12的Apply、再修改14点确定、主窗真实字号更新、重开显示14、Apply恢复12及取消均通过；无网页错误。实际未强制触发字体竞态，raceForced=false；竞态由函数测试证明，不能据本次普通回归宣称竞态实机已复现。

两次原生创建至呈现297/428ms。修改后确定只有settings/write与window/settings-close，相隔6.8ms；驱动观察关闭622ms含派发和轮询，不能当作原生关闭耗时或无CDP桌面验收。没有新录像；秒级慢开、闪烁及整体验收仍未解决。

实际执行live-data.js SHA256从ECCE4CA51207DACEC72D0DF92E811EFFA6CE160C1ED01B243A21CE96A5602C5A变为EED7AB21899461B5A968EC47EAC41CAFACC903505445F3D73FECA70EE22D6D75；MD5从D5CEB8B2097BD275AA6098A06B850F57变为ACCEC428FDA5173B60F2D83BA02A2BA5。mockup.js/mockup.css/bridge.js的MD5仍为0EEBB52037A8EEABDB81DA777E8C1FFE、00FA3357EC646E91030A9DFDD925DAA9、F297BF8769E249CA0D804F4F4EEEF5A9。

实际映射Shell仍B8F2BDAAD19F9B5740DF1BEC45CE1F84C38E26233CB77FB1558180BD54CE0B62，Infrastructure仍A08EB839CC37A2E71BFB5CF8C03C2BCD4FA74BE596CCECDC9D2222E12C27964B。映射路径为Debug产物，哈希读取对应磁盘文件；Release未更新。两窗DPR1.75、初始UI12，设置搜索框实际Microsoft YaHei UI，主窗正文实际随包JetBrains Mono、配置正文13。Nahimic仍加载，未重试权限受阻操作。

## 资源与接续

PID27900、端口63241、临时Augit-independent-settings-pVxJIE由finally清理，并独立核实均不存在或无监听，cleanup-independent.json保留。无新增服务器或录制进程；原始证据保留。当前分支dsh、HEAD仍3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6，既有混合修改保留，未暂存、提交或回滚。

整体goal保持active。设置正常无CDP完整操作和秒级慢开、背景透明帧与500ms、55页全模态、终端完整逐行、系统阴影仍未全验收；复杂Git/冲突精修暂停，未删除或算完成。Windows10排除本轮，跨屏DPI未验证。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。

停止优先正常关窗；异常仅停止核实属于本次的实例：taskkill /PID <本次PID> /T /F。

定向测试：node --test tools/audit/settings-typography-reload.test.cjs tools/audit/settings-preview-reload.test.cjs tools/audit/settings-save-close.test.cjs tools/audit/settings-open-singleflight.test.cjs。

实机复验：node tools/audit/independent-settings.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --daily-launcher --single-open --typography-cycle --expect-no-close-reload。
