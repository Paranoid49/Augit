# 冲突绘制上下文与精确基线试验

日期2026-10-15。继续已批准的冲突实机整改，未改产品文件、暂存、提交或回滚。Windows10排除，跨屏DPI未验证，整体目标未完成。

## 运行时证据

ReferenceMergeGeometry.java新增只读缓存诊断：读取EditorView实际FontRenderContext、组件Graphics2D变换、逐行offsetToXY及已经形成的TextLayoutCache。只遍历布局类的chunk/run/fragment，不调用布局生成、绘制或编辑；Graphics读取后dispose。Java17编译、JSON转义、继承字段、缺失字段及循环/范围边界自检通过。

证据目录artifacts/pixel-followup-2026-10-15/conflict-paint-context。reference-runtime.json来自PID2240；reference-fragments.json来自PID22364。cache-verification.json核验三栏九个SimpleTextFragment完整覆盖非空文本，实际片段字体均JetBrains Mono Regular 13，字符位置8/16/24……逻辑px，行高22、ascent16，逐行y为0/22/44/66。三栏FRC均LCD HRGB、整数度量、1.75比例；component.getGraphics的横向平移为0/0.25/0.5物理px，纵向0。

这证明本次初始夹具实际布局缓存字体与步长，不是拦截所有paint/draw调用，也不能外推其它状态或字符。旧配对fontFallback未任意改成通过。本地263分支源码仅用于定位字段，实际值来自262.9437.214参考进程。

reference-initial.png为1730×1215原始系统图，DPI168，SHA256 bc63823c33ecf7cb7cd9ac46450985c74d5424a83dd12d48b9fea568bb6fd830。前后身份/边界/DPI稳定，但中央首行有鼠标光晕，不能登记整窗无遮挡。来源首行无遮挡，以RGB最小通道<140统计，左[34,206,171,224]、右[1322,206,1459,224]，各618墨迹像素，与历史清晰参考及诊断图一致。未建立合格首行焦点同态，不增加55页合格数。

## 隔离基线试验

新增probe-exact-conflict-baseline.cjs，由真实外壳驱动--exact-baseline-probe调用。只改变隔离实例行内leading，按实际基线与JBR基线残差回算，最后恢复原style；断言草稿、行盒高度与位置不变。前两次追加探针被行尾换行符带到第二行，错误校准被行盒断言拒绝；一次UTF-8读取错误使修补未生效，随后增加修改退出码检查，改为首字符前探针。

最终conflict-exact-baseline-corrected（PID24868）成功采集三模式。基线原15.991073608398438，校准后部分行达到16，部分仍停留原值，暂不采用产品补偿。两侧首行原始PNG量测如下：

| 条件 | 左墨迹边界 | 右墨迹边界 | 左/右区域差异像素 |
| --- | --- | --- | --- |
| 原状 | [33,205,170,223] | [1322,205,1459,223] | 1161 / 1040 |
| 校准 | [33,206,170,224] | [1322,206,1459,224] | 1068 / 912 |
| 恢复 | [33,205,170,223] | [1322,205,1459,223] | 1161 / 1040 |

区域左[20,200,200,226]、右[1318,200,1510,226]，相对历史清晰参考，不缩放/平移PNG。精确基线改善首行纵向，横向及栅格仍不同。全窗校准变化1955像素，恢复相对原状还有59像素差，未证明其原因，不宣称全窗逐像素恢复。baseline-probe.json与pixel-comparison.json保留每行值和哈希。下一步须解决不同位置量化并覆盖编辑/字号/DPI，未修改产品字体、行高、抗锯齿或正文。

## 验证与资源

真实试验、Java自检及JS语法为定向检查，未跑全量。最终恢复style断言在成功采集后加入，尚待下次试验执行，不能声称已通过该新增断言。

参考PID2240/22364均已taskkill /T /F。实际PID7160/11024/19692/24868、端口53508/49676/64601/52014，临时Augit-merge-pair-VHQeKn/aB8ApY/Pa9kUB/bp0K24由finally清理，收尾独立复查。删除参考临时代理目录被自动审批以blocked by policy拒绝，未执行、未重试，目录保留：C:/Users/wl024/AppData/Local/Temp/Augit-paint-context-0c964b1f13944af3a57933c8a5485dfa。不能宣称资源全部清理，历史被拒目录未触碰。

首次JBR缺jar.exe，改用Python标准zipfile打包；字体文件内部度量核验因默认Python无fontTools未执行，未新增依赖。产品四文件MD5不变：mockup.js BE21F21B0D6F6036844ADF51BD24DB46；mockup.css 0A62EE8E629016699206B3B66D7D38B1；live-data.js 019F54DFDE0629B0DDB67B91B46E50C7；bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。

    $pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
    node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-baseline-manual-new $pw --light --screen-pair --reference-frame --pair-focus --exact-baseline-probe
    # 正常finally停止；异常仅终止本次驱动打印的PID。
    taskkill /PID <本次PID> /T /F

参考启动使用运行时几何记录中的隔离PYCHARM_PROPERTIES及本次专用VMoptions，启动后立即恢复环境变量。
