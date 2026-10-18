# 设置控制器创建调用分界（2026-10-17）

本批只补充正常路径的 controller-create-issued 阶段：调用 CreateCoreWebView2ControllerAsync 取得同一个 Task 后记录，再等待该 Task。未修改创建参数、等待条件、显隐顺序或关闭保护，没有预创建、固定延时或测试专用行为。该改动是诊断，不是慢开修复。

## 正常入口证据

全部四批通过 tools/start-augit.ps1 强制非增量构建，复制真实设置，沿用默认 Session 和正常系统 DPI。证据位于 artifacts/pixel-followup-2026-10-17/ 下。

| 目录后缀 | 进程 | 证据和限制 |
| --- | ---: | --- |
| settings-controller-issued | 24276 | 无 CDP；原生呈现663.01ms，控制器同步调用约1.48ms、返回后等待约67.70ms。录像在点击前结束，严格关联器明确拒绝，不计开窗录像通过。 |
| settings-controller-issued-covered | 3452 | 无 CDP；原生呈现844.8468ms，控制器同步调用1.5215ms、返回后等待75.8955ms。479帧完整时间关联，最大采样间隔89.187ms；前景采样属于本实例。 |
| settings-controller-issued-identity | 14708 | 附 CDP，八组设置功能回归通过，含延迟保存、取消重开及失败重试；并非仅单次开窗。没有执行脚本字体身份文件，目录名不作为身份依据。 |
| settings-controller-issued-fonts | 13900 | 附 CDP，单次开窗、字体身份和Cancel通过；原生呈现2904ms，控制器16→85ms，准备完成410ms、捕获完成2889ms；window/settings-present请求耗时2478.8ms。没有本批细分原生导出，不能再区分激活、捕获调用和捕获等待。 |

第二批呈现夹在原始第42/43帧，present-01.png 与 present-02.png 已目视确认主窗→完整设置，原图2880×1716；工具显示缩小不参与计算。视频SHA256为 A1432C7A0D2117CE7950C72CA44572874450F9A028CE7BA786125CA86445E099。只核对阶段邻帧，没有全帧像素验收，不能宣称无闪或正常关闭已验证。computer-use仍只列主窗，没有重新尝试历史已拒的子窗输入；无CDP两批通过实例清理结束，不计正常关闭。

两次无CDP样本未复现慢创建，不证明历史2487ms创建发生在同步或异步哪一段。最后附CDP实例反而再次证实秒级呈现等待仍存在。下一次确有必要采集时，必须同时开启已有原生细分导出，避免再丢失慢样本分界；不重复采样直到出现快速数字，也不取消已有白帧保护。

## 身份与条件

无CDP两批的 mapped-images.json 核对真实进程映射，Shell SHA256由 B8F2BDAAD19F9B5740DF1BEC45CE1F84C38E26233CB77FB1558180BD54CE0B62 变为 37A8CA23E30988784966BF320AEC88C1A17944E9EB29C2BBA786AFEF7DF8C67E；Infrastructure仍为 A08EB839CC37A2E71BFB5CF8C03C2BCD4FA74BE596CCECDC9D2222E12C27964B。路径来自映射，哈希来自对应磁盘文件；Release未更新。Nahimic仍映射，未重试服务权限阻塞，不推断因果。

最后附CDP批次两窗实际执行live-data.js SHA256仍为 EED7AB21899461B5A968EC47EAC41CAFACC903505445F3D73FECA70EE22D6D75。主窗正文实际随包JetBrains Mono，设置搜索框实际Microsoft YaHei UI；配置正文13、界面12、两窗DPR1.75。该字体证据仅属于该实例，不外推无CDP实例。无CDP设置源/副本哈希一致，详见各自launch.json。

四网页MD5未变：mockup.js=0EEBB52037A8EEABDB81DA777E8C1FFE，mockup.css=00FA3357EC646E91030A9DFDD925DAA9，live-data.js=ACCEC428FDA5173B60F2D83BA02A2BA5，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

## 定向验证与清理

新增源码顺序断言修前失败、修后通过；隐藏启动11项142ms、生命周期和初始化关闭11项153ms、SettingsStageTraceTests 17项182ms通过。源码断言不等同可控COM行为测试。限定文件dotnet format whitespace、共享资产9项和git diff --check通过，未运行全量。最初命令误列不存在的settings-initialization-lifetime.test.cjs；后来使用正确shell-initialization-lifetime.test.cjs补跑，不将前次11项计为覆盖该文件。提帧两次转义失败未生成图，最终成功提取上述两帧，未改变视频。

settings-controller-issued-covered/cleanup-independent.json 独立核实进程24276/14548/26240/3452/10180/25584/14708/13900全部退出、60340和55232无监听、四个本批临时目录均已删除。未新增服务器，原始证据保留，历史PyCharm清理与Nahimic权限阻塞不在本批清理结论内。未回滚、覆盖、暂存或提交既有修改。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止优先正常关窗；异常仅对核实属于本批的实例执行 taskkill /PID <本次PID> /T /F。

定向测试：node --test tools/audit/settings-hidden-startup.test.cjs tools/audit/shell-initialization-lifetime.test.cjs tools/audit/settings-startup-close.test.cjs。原生诊断：node tools/audit/settings-no-cdp.cjs <新证据目录> --gdi-clock；时钟关联：D:/Python/Python3119/python.exe -B tools/audit/correlate-settings-clock.py <新证据目录>。

整体仍未完成。慢开、闪烁、背景刷新500ms、55页全模态、终端配对和完整阴影仍缺证据；冲突精简和终端命令块提案待答复。Windows10排除本轮，跨屏DPI未验证。
