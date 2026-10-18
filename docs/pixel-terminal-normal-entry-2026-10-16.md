# 终端普通入口取证修正（2026-10-16）

旧capture-terminal-pair.cjs固定使用text-viewer场景、允许9–40号正文，且未写appearanceVersion，不能代表当前普通启动和固定13号正文。现默认普通入口恢复fixture内notes.txt，不传scene/open/no-session-restore；使用明确外观版本、固定JetBrains Mono13及显式Light/Dark。其他字号直接拒绝，防止设置被产品覆盖后仍把采集标成17号。

固定45秒等待改为有界确认文件，确认窗口身份后立即采集，等待结束清理信号。新增可选--require-pointer-clear传递系统截图96物理px指针门禁。它要求光标在整个窗口外，不因浏览器hover成功就自动认定满足。保留历史baseline-probe作为明确的隔离相位试验，不计普通产品验收。

## 本批实际证据

普通入口Dark，系统DPR1.75、JetBrains Mono13、基础行格18逻辑px，真实PowerShell输出八行Ag 0123456789；没有直接写渲染buffer。原始系统PNG2450×1628，截图前后进程/前景/边界/DPI校验通过。证据位于artifacts/pixel-followup-2026-10-16/terminal-normal-dark。实际SHA256为6394e3991af16df2f169b234140f0f64b9463641968f860dded108cde73c5dc2。

八行首字母墨迹顶1293/1325/1356/1388/1419/1451/1482/1514，步长32/31交替，相对31.5物理px累计偏差最多0.5px。分析器新增明确亮墨迹极性，直接分析原PNG，不反色/缩放图片；阈值160、范围[93,1290,106,1540]记录于eight-row-measurement.json。第一次下界1548包含下一条提示符，行数门禁拒绝；目视并打印连续墨迹带确认后改为1540，未截短返回行数或补零。

本批没有深色PyCharm同态参考，因此qualifiedPair=false、pixelRestorationVerified=false。系统指针实测[1315,877]位于编辑区；本轮未启用整窗96px门禁，只确认终端取样区域未受该指针影响。截图工具自身的窗口预览不是原始PNG证据。

## 测试与清理

terminal-capture-options.test.cjs三项通过，119.46ms；test_terminal_row_step.py五项通过，0.120秒；capture-terminal-pair.cjs语法及git diff --check通过。没有修改产品代码或运行无关全量。网页四MD5保持mockup.js=44D6C53684B74DBC6116FF25F7C22932、mockup.css=D5DADE8F4DB84506031A9AB3B1F5DB80、live-data.js=6200A1F1FFF74A92DC9A55AEFD0D4DAA、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

启动命令：node tools/audit/capture-terminal-pair.cjs <Playwright模块路径> <新输出目录> --theme=Dark --repeat-rows --wait-foreground。通过computer-use实际激活并观察驱动所报窗口后，创建输出目录内terminal-ready信号；脚本随后取证并finally停止。异常仅执行taskkill /PID <本次PID> /T /F。

本次PID4480、端口60566及Augit-terminal-pair-pUZKOT已退出/释放/删除并独立检查。未启动PyCharm。普通启动仍为powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1，关闭窗口停止。完整终端配对及整体四项目标仍未完成。
