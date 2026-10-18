# 深色终端普通输出颜色与八行配对

日期2026-10-16。本批从普通入口实机取证发现终端错误继承工具面板底色和界面文字色，修正共享CSS与UX规范。未修改行格、字宽、字重、字体或渲染引擎。整体四项目标仍未完成，Windows10排除本次实机验收，跨屏DPI未验证。

## 来源与实测

本地IntelliJ提交576e328的JBTerminalSchemeColorPalette.kt规定默认底色优先CONSOLE_BACKGROUND_KEY、前景优先NORMAL_OUTPUT_KEY；expUI_darkScheme.xml分别为1E1F22与BCBEC4。PyCharm2026.2.1隔离配置实机原图印证两色；Augit修前是191A1C与D1D3D9。CSS现在仅在深色.terminal-view使用正确控制台颜色，静态prompt随正文继承；实时xterm仍通过terminalTheme读取计算样式，ANSI16色及浅色不变。

证据目录artifacts/pixel-followup-2026-10-16/terminal-dark-pair-hook。两端DPI168、JetBrains Mono13、深色、终端失焦且鼠标在编辑区，终端文字采样区域无遮挡。参考通过安装包powershell-integration.ps1支持的JEDITERM_SOURCE执行固定八行，Augit通过Custom启动命令执行相同表达式；均为真实PowerShell输出，没有写渲染buffer。启动横幅不同，整窗尺寸2430×1637与2450×1628、面板原点亦不同，因此不计55页同态整窗配对通过。

八行均为Ag 0123456789。参考行顶1236/1268/1299/1331/1362/1394/1425/1457；实际1167/1199/1230/1262/1293/1325/1356/1388。两端步长32/31交替、累计偏差最大0.5物理px。阈值160的亮墨迹外框参考178×21；修前179×21、修后178×21。宽度变化来自颜色阈值，不是本批调整了字宽。

修后逐行按墨迹框平移比较，每行仍1305像素RGB不相同、1225超过RGB2，平均最大通道差7.866、最大72。未缩放或重采样，未用放宽阈值把差异算作通过；字形抗锯齿及整窗状态仍需整改。完整逐行参数、原图元数据和哈希见eight-row-comparison.json，qualifiedPair及pixelRestorationVerified均false。

原始PNG SHA256：参考fae4eb03b41c9381b89bd856bcdad9b785e2ff60e9c371aaab1fbfad4a815647；修前c35e86bde914ba9e98d84e5b8825a6742bd7ef9ba80d4594c4ffada4b4cb8cc6；修后44f3dcc5001498575d7ee045d3677ff8c2f8985bbcbc71446de47c79e003977a。

## 失败记录、验证与交付

首个参考配置的terminal-local.xml没有改变实际启动命令；安装包也存在该类，不能据此断言版本不支持配置键。后改用已核对的集成脚本钩子取得八行。首个Augit审计写了小写custom，被设置规范化而没有生效；现使用产品已登记的Custom，并由实际八行输出及xterm计算样式验证。失败实例保留日志，不把失败尝试计入通过。临时参考启动器已删除。

新增--startup-rows审计入口和定向测试；6项终端配置/调色测试通过146毫秒，真实live-terminal-palette深浅切换与取消恢复通过。普通入口修后系统截图及颜色断言通过；共享资产、JS语法、git diff --check通过。只执行本模块必要测试，未跑Shell或全仓全量。Debug/Release构建0警告0错误（2.93/1.88秒），两套24文件逐字节与web一致。

mockup.css MD5由D5DADE8F4DB84506031A9AB3B1F5DB80变为52721655D0B1969C38441708C3033208；mockup.js=44D6C53684B74DBC6116FF25F7C22932、live-data.js=3DCFCEAED3358BE87D4FA3A69080E7FA、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9不变。混合修改未暂存或提交。

## 资源与命令

参考PID8712/26176及日志记录的子进程已停止；实际12732/27564/25676、主题验证26180已停止，52842/62181/59984/59312无监听，实际临时profile全部删除。两份临时参考配置目录C:/Users/wl024/AppData/Local/Temp/Augit-terminal-reference-fcNeKj与Augit-terminal-reference-ly4OSl的删除命令被自动审批策略拒绝，仍存在；没有换方式绕过，不宣称全部临时文件清理完成。其启动器已删除，目录内没有运行进程。fixture的notes.txt仍为原三行。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止为正常关闭窗口；异常只对本次启动打印的PID执行taskkill /PID <PID> /T /F。

复验：node tools/audit/capture-terminal-pair.cjs <Playwright路径> <新目录> --theme=Dark --startup-rows --wait-foreground。实际选择窗口、点击编辑区并观察后创建输出目录terminal-ready；脚本采集后finally停止实例及删除profile。此入口隔离配置，不代表用户原有运行实例。
