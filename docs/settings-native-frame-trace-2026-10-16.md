# 设置闪帧：连续状态采样与两项合成反证

日期：2026-10-16。重复窗口排队修复保持；整体闪烁尚未解决。本批没有保留新的产品修复，两个候选均已撤销，正式Debug已强制非增量重建。不得以原生状态正确或功能回归通过代替屏幕稳定。Windows10排除，跨屏DPI未验证；原有55页/全模态、终端/冲突逐行及系统阴影缺口保持。

## 连续状态与系统录像

新增审计工具trace-native-modal-frames.cjs，仅在--trace-native-frames启用。每个RAF最多一个在途只读请求，记录查询起止UTC、DOM隐藏/busy及五个原生白名单布尔值；有界记录、显式丢弃计数、释放取消后续帧并忽略迟到结果。四个新增测试与既有表面追踪测试共7项通过，154.67ms，采集驱动语法通过。无新增产品桥接字段。

证据目录artifacts/pixel-followup-2026-10-16/refresh-native-frame-trace-retry/light。正式实现、普通场景、参考fixture、隔离profile、Light、DPR1.75。GDI/FFV1共399帧，1820×1341，首帧固定、RGB容差2、排除0像素，1帧变化；第39帧4064像素（角131、直边3933、内部0），已查看原始PNG。最大采样间隔50ms、重复时间戳0。严格门禁失败。

同机GDI墙钟链核验通过：失败帧在首次uncover应答后39.993ms；相邻原生查询显示表单隐藏数0、busy=false、遮盖隐藏、业务控制器可见。保留1024条记录、后续丢弃423条，读取失败0；其中涉及隐藏的91条全部报告遮盖ready、visible、topmost。记录上限后的状态不推测，不把HWND层级等同于DWM Present。本次没有复现内部露底，不能撤销既有DDA失败证据。前景189采样、最大间隔104ms、qualified=true；采样间干扰仍不能排除。

六轮观察486.17/319.61/306.93/304.73/338.71/325.38ms；包含诊断请求负载，只描述样本，不外推稳定延迟。录像SHA256为092d0fdc74edbafe141328ca4c9bab36803771b61855bf22cb162cea1077555c。首个目录refresh-native-frame-trace等待前景确认超时，实例已清理，不计有效录像。关联器首次早于分析报告生成而失败，待报告生成后顺序重跑通过，没有将首次失败记成通过。

## 未采纳候选

候选均使用DDA/FFV1、完整1820×1341、RGB容差2、零排除、有效前景；原始源时间戳无重复，解码DTS量化警告保留，不冒充UTC关联或完整Present追踪。

1. refresh-dialog-layer-probe/light：仅两份共享CSS增加原生表单will-change:opacity。343帧、5变化帧，65–67帧各2295790像素变化（内部2208415），77–78帧各4062边缘/角部变化，最大间隙39ms。六轮591.53/300.94/309.22/365.65/347.15/308.40ms。已撤回，未进入随包资源。录像SHA256 f1f81039ee4de93d0f029ecfffb2efb6faa1bc41ef580b2b1c58cda532566e87。
2. refresh-layered-child-probe：以原始PNG、GDI+解码、UpdateLayeredWindow分层子窗替代遮盖WebView，业务WebView仍单一，不复制DOM或控件绘制。首轮light误用屏幕坐标，发生偏移，326帧/54变化帧，明确无效候选；修正为子窗坐标0,0后的corrected-light为309帧/2变化帧，63–64帧各9717像素（角236、直边9481、内部0），最大间隙38ms，仍失败。六轮374.75/338.40/308.27/341.28/282.84/310.51ms。单次未采到内部变化不证明消除露底，也没有阴影通过结论。修正录像SHA256 54dd97b84b6b88ed5169f8b006bd13e18d24c115af50dbd36efdca3eefc4b978。

位图损坏/越界两项既有候选测试实际运行通过112ms；不把它们当资源生命周期完整验证。候选源码和测试保存在证据目录的txt文件中，不是编译输入。没有同步架构，没有将未通过方案放入日常版本。

## 恢复与清理

逐文件确认候选字节未被其他修改覆盖后恢复。ShellModalHost.cs SHA256恢复124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348；删除本批两个候选编译输入。恢复Debug --no-incremental /nodeReuse:false构建6.08秒、0警告0错误。Release从未装入候选。

四网页MD5保持：mockup.js=396E779B261AC31FBE4B625181134680、mockup.css=15DE14CA11BB3E8682E403FB7EF5E054、live-data.js=38A6F63C55532F4D67A210E5ED933F88、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。混合工作区未回滚、暂存或提交。

最终共享9资产一致，Debug/Release各24项随包网页镜像一致，正式DLL不含ShellPngCover试验类型；git diff --check、文档声明检查通过。文档检查仍提示历史全量快照已过期，不将历史断言数当本轮通过项。新增审计文件和本文均为UTF-8/LF。

Augit28064/26452/28396/25760/26848及对应录屏/采样进程均由驱动finally结束；隔离目录see9Fe/ZsRvdO/okRsAn/nS6bn8/jFntGn独立确认不存在，端口50149/61483/56880/53849/52189无监听。未启动PyCharm；历史被拒绝删除目录未触碰。原始录像、候选文本和报告是保留证据。

## 后续与复验

现有证据将边缘闪动定位到撤盖后的桌面合成阶段，但没有确定唯一根因。后续应核对原生非客户区与业务表面的呈现交接；不再重复单纯延长RAF、改opacity为visibility、will-change或本次位图子窗候选，也不删除边缘或降低阴影要求。

```powershell
# 日常启动；停止请正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 异常时只停止本次启动打印的PID。
taskkill /PID <本次PID> /T /F
# 审计工具定向测试。
node --test tools/audit/trace-native-modal-frames.test.cjs tools/audit/trace-modal-surfaces.test.cjs
# 正式产品采样，不加载已撤回候选；确认真实主窗前景后释放打印的检查点。
node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新目录> Light --normal-entry --refresh-video --trace-surfaces --trace-native-frames --activate-main
D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py <新目录> --require-stable
# 分析报告生成后再执行，只接受GDI墙钟来源。
D:/Python/Python3119/python.exe -B tools/audit/correlate-refresh-clock.py <新目录>
```
