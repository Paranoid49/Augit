# 模态客户区定位整改（2026-10-17）

后续验证口径订正：本报告顶部后半留出区域几乎纯色，不能当成纹理混合的独立验证。新的交错有纹理样本及末态背景对照见[瞬变记录](refresh-edge-background-transition-2026-10-17.md)，原始数值保留。

本批修复一个由正常入口实机证据确认的坐标错误：最大化主窗的外框包含屏幕外边距，首层模态却把网页客户区裁切加到了外框原点，导致窗口向左上各偏12物理像素。修后使用ClientToScreen读取客户区原点，嵌套窗口相对已移动父窗的规则保持。读取失败释放已准备快照并明确失败。没有改变字号、缩放、系统阴影或DWM边距，也没有为验收平移图片。

## 正常入口修前与修后

证据目录分别为artifacts/pixel-followup-2026-10-17/refresh-edge-owner-coordinates和refresh-client-origin-fixed。两批均通过tools/start-augit.ps1强制非增量Debug构建、真实配置副本、默认Session与正常DPI；附CDP只读诊断和页面交互，不当作无CDP验收。

- 采集器新增所有者身份与客户区读取，并在截图前后核对一致：主窗外框[-12,-12,2892,1728]、客户区[0,0,2880,1716]、客户区屏幕原点[0,0]、DPI168。
- 业务源裁切均为[1148,733,1733,985]。修前系统矩形[1136,721,1721,973]，新增定位断言用旧实机证据明确失败；修后系统矩形[1148,733,1733,985]，client-placement.json通过。
- 定向自动化覆盖最大化负外框偏移、普通窗口非零原点及错误身份。实机仅覆盖当前最大化首层跳转行；没有把普通窗口、嵌套、跨屏全部登记通过。
- 修前1278完整帧最大间隔41ms、最差265变化像素；修后1298完整帧最大间隔40ms、最差113变化像素，全部位于[17,0,153,2]。容差RGB2，无配准、无排除像素，fullPresentationVerified仍为false。
- 修后最差帧在撤盖应答后49.54ms。baseline-worst-pair.png已目视：顶部文字状边缘变成灰边，正文表单仍在。不是无闪修复，也不是阴影验收。
- 最后撤盖修前589.5/546.7/454.1/486.8ms，修后593.8/466.9/527.8/478.4ms；两批都有超500ms，不能把定位修复称为性能改善。四轮最终正文及关闭释放检查通过。

## 边缘来源订正

上一批把背景PNG按业务裁切原点采样，未考虑模态实际屏幕位置与背景客户区原点不同。因此旧报告的“同坐标不吻合”只适用于那组错误坐标，不能据此否定屏幕下方背景参与边缘合成。此次背景区域严格由模态屏幕矩形减所有者客户区屏幕原点计算，未搜索配准偏移。

用顶部第二行前半段拟合screen=a×background+b，修前a=0.5360、b=47.0777、训练MSE1.238；未参与拟合的顶部后半段MSE1.763、最大通道残差3.239。修后相应a=0.5358、b=47.1338、训练MSE1.299，顶部留出MSE1.757。两组支持该行与物理下方背景混合相关。左侧独立验证MSE106.7/111.4，并不符合完全相同混合系数，不能把单行拟合扩展成整个DWM边缘模型，更不能据此认定系统实现因果。

下一步应核对遮盖显隐前后顶部边缘与物理下方背景的合成关系；不重复已失败的DWM边距、擦除、裁切、固定等待和遮盖候选。

## 身份、测试与清理

实际映射Shell SHA256由4CAC07952E541B48BCF069F0F7DDA4B03783A8BB4F12BF86329129D9148C9F75变为B8F2BDAAD19F9B5740DF1BEC45CE1F84C38E26233CB77FB1558180BD54CE0B62。Infrastructure仍A08EB839CC37A2E71BFB5CF8C03C2BCD4FA74BE596CCECDC9D2222E12C27964B；执行live-data仍31964BB95E93477B66EC159588AC14B805B3339DA3BAC69EA52B8B2EFC8A1BFC。真实UI Microsoft YaHei UI12、正文JetBrains Mono13、DPR1.75；Nahimic仍映射。Release未更新。

四网页MD5不变：mockup.js=0EEBB52037A8EEABDB81DA777E8C1FFE；mockup.css=00FA3357EC646E91030A9DFDD925DAA9；live-data.js=D50C01AF5311343AC7F8E03FA2E2F0FC；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

31项Shell定向、4项采集/定位JS、7项刷新驱动、5项源图Python、4项原生采集门禁Python通过；Shell定向耗时87ms，Python源图455ms、门禁6905ms，最终4项采集JS155ms。dotnet format限定ShellModalHost.cs通过，9项共享资产通过，未跑全量。工具传输曾因换行转义失败三次，失败调用未执行命令，纠正后成功。本批文件UTF-8/LF、JS语法和git diff --check通过。

两批cleanup-independent.json独立确认PID26432/5404/10728和18596/4984/22460均退出，63250/63065无监听，Augit-daily-refresh-WVL5Ii/JyyWtp已删除。单测临时夹具自动回收，分析进程全部退出，PNG/JSON/录像保留为正式证据。历史参考清理审批阻塞、Nahimic服务权限阻塞仍保留。没有提交、暂存或回滚混合修改。

## 复验与未完成范围

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止：优先正常关窗；异常仅对本次核实PID执行taskkill /PID <本次PID> /T /F。

复验：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --trace-edge-source --verify-client-placement --gdi-clock

分析：依次运行compare-modal-edge-source.py、analyze-static-modal-video.py、correlate-daily-refresh.py，参数均为新证据目录。

复杂Git和三栏冲突精修继续暂停，未删除功能；正常启动与设置慢开/关闭、背景透明帧/500ms、55页全模态、终端完整配对、完整系统阴影均未整体完成。Windows10排除本轮，跨屏DPI未验证，goal保持active。
