# 同值纯文本通知停止重复捕获（2026-10-17）

## 根因与范围

接续设置确定关闭修复后，重新检查真实刷新耗时。最近首次刷新中遮盖准备262ms、背景更新137ms，既有预创建、裁切与固定等待候选均有失败记录，不重复采用。本批发现外部写回相同字节时，openDocument仍改写状态栏并请求批次区域刷新，导致整轮遮盖和背景捕获。此前侧栏同值修复没有覆盖当前正文路径。

正常入口修前已复现：document/read实际执行，遮盖168.8ms、背景更新106.2ms，背景图版本0→1。正文DOM本身已被现有区域比较保留，不能把这次问题误写成正文节点一定被替换；真实浪费是打开提示和区域更新引起的背景捕获。

修复限定已显示、读取成功的普通纯文本后台批次。仍每次读取磁盘；不重复展示用户打开提示，将新载荷经正式toLiveDocument映射后与原模型全部字段逐项比较，包括正文、编码、换行、大小、状态及说明。相同则返回false，批次不要求正文区域刷新。复杂字段jsonError按行列比较，其余直接比较，避免为大文本额外JSON序列化。异步后核验代次、工作区、原标签及模型身份，普通打开、真实内容变化、Markdown/JSON/图片、失败及删除沿原路径处理，不新增缓存或忽略通知。

## 实机证据

均位于artifacts/pixel-followup-2026-10-17，使用tools/start-augit.ps1强制非增量构建、真实配置副本、默认Session、正常DPI，附CDP。审计在临时Git仓库写真实文件，不修改产品条件或响应时序。

|目录|结果|
|---|---|
|refresh-same-document-before|同字节写入产生cover/refresh/uncover，背景图版本0→1|
|refresh-same-document-after|初版同字节写入仍读磁盘、无捕获，背景图版本0→0|
|refresh-same-document-final|避免大文本序列化后的最终版：同字节写入仍读状态40.1ms、正文1.6ms、目录0.4ms，无捕获且版本0→0|
|refresh-text-noop-content|最终版真实正文四轮更新与Esc释放通过；最后撤盖604.4/466.5/467.1/489.5ms，首次仍超500ms|

前三批均随后写入不同正文generation1，确认正文和背景更新；同值阶段正文、输入框身份、焦点及输入17保持。普通刷新1200完整帧、最大间隔40ms，最差第63帧191变化像素，已目视仍为已有顶部及光标范围；不排除像素，不计无闪。录像SHA256=8E5696214FE64823AFFAF8E453C22399AB066188F322968F330C14DB0591D317。没有用同值通过代替真实变化的500ms或全部模态验收。

## 测试与身份

新增同值读取回归修前失败。修后文档、批次、迟到失败、后台更新与现有驱动合计49项通过1219ms；最后直接比较优化后重跑受影响30项通过178ms。覆盖五种正文/元数据变化、三类非纯文本、失败、切换、关闭及后台更新。JS语法、9项共享资源、UTF-8/LF与git diff --check通过；未跑全量。

实际执行live-data.js修前SHA256=F12EB39E70F82D86DF1E215468EA3C53197999764E8CF44E759EA37789A59BFC，初版CC9B91BF66753D7B88F6D80A8B1ECE700397255AD7A3E46E765017AF1087AF59，最终ECCE4CA51207DACEC72D0DF92E811EFFA6CE160C1ED01B243A21CE96A5602C5A。MD5由59998C04CB91C8EB470F1A2CC5C29206变为D5CEB8B2097BD275AA6098A06B850F57。其余三个网页MD5保持0EEBB52037A8EEABDB81DA777E8C1FFE、00FA3357EC646E91030A9DFDD925DAA9、F297BF8769E249CA0D804F4F4EEEF5A9（分别mockup.js/mockup.css/bridge.js）。

实际映射Shell仍B8F2BDAAD19F9B5740DF1BEC45CE1F84C38E26233CB77FB1558180BD54CE0B62、Infrastructure仍A08EB839CC37A2E71BFB5CF8C03C2BCD4FA74BE596CCECDC9D2222E12C27964B，均Debug路径；哈希来自映射路径对应磁盘文件。DPR1.75、配置UI Microsoft YaHei UI12、正文JetBrains Mono13，正文实际字形为随包JetBrains Mono。Nahimic仍映射，不计最终性能验收；Release未更新。

## 清理及剩余项

四批PID22308/28216/26188/25388、端口63451/52090/63083/62593、临时目录后缀xRoFM7/8Ylr4n/mhGphk/707ZAi，以及录像15388、监视11556均已独立确认退出或删除，各目录保存cleanup-independent.json。分析进程已结束。审计证据保留，未启动服务器，未碰历史审批拒绝项，未提交或回滚既有混合修改。

整体未完成，goal保持active。真实变化首次仍超过500ms、边缘闪变保持；设置秒级慢开、无CDP操作、55页全模态、终端完整逐行、系统阴影仍未全验收。复杂Git/冲突精修暂停，未删除或算完成；Windows10本轮排除、跨屏DPI未验证。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。

停止优先正常关窗；异常仅对本次核实PID执行taskkill /PID <本次PID> /T /F。

同值复验：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --same-document --expect-same-document-noop。

普通刷新录像将最后两个开关替换为--gdi-clock；完整帧分析：D:/Python/Python3119/python.exe -B tools/audit/analyze-static-modal-video.py <证据目录>。
