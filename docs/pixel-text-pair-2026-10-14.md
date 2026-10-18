# 普通文本原始屏幕取证（2026-10-14）

整体整改未完成。本批取得同夹具、同屏幕尺寸的两端普通文本原始 PNG，并修补配对门禁的同图冒充漏洞；没有修改产品正文几何，也不计入55页合格同态配对。Windows10排除本次验收，跨屏DPI保留未验证。

## 原图与条件

目录为 `artifacts/pixel-followup-2026-10-14/text-pair/`。参考为 PyCharm2026.2.1／262.9437.214 隔离安装，实际为 Augit Debug真实Win32/WebView2外壳。两端使用参考fixture中的notes.txt（三行文字及末尾换行），深色、UI12、JetBrains Mono13、DPR1.75，最大化屏幕原图均为2880×1716，系统DPI168。参考配置文件登记Microsoft YaHei UI12与编辑器行距1.2；配置声明不能代替双端实际字体回退证据。

- `reference-single-tab.png`：SHA256 `9840a24c45ef719da6a9be6285dd9a719e911bad5f462a3ea30a20434dc4abf2`。通过参考UI关闭README后台标签，只保留notes.txt；侧栏和底部工具窗已隐藏。
- `maximized/actual.png`：SHA256 `2b4d61d598e3cb48d93dd266c24036705476f09a78b006730b8dce210101dbed`。单个notes.txt标签，正文聚焦，页面错误为空；几何记录在同目录actual-geometry.json。
- 两份PNG均由CopyFromScreen取得，无重采样；前后进程、前景、窗口和DPI稳定。人工目视确认正文及第二三行没有通知遮挡。鼠标光晕仍在标题／标签附近，参考首行有光标及当前行背景，故不声称全页无遮挡同态。

Augit保留规格要求的只读文档工具栏，参考编辑器没有该行。参考右侧工具轨、当前行与光标、行号槽结构不同。`design-system.md`§5明确规定Augit至少三位行号加28px留白、右内距3px；参考源码EditorGutterLayout.createNewUILayout则组合行号、图标、折叠等槽，EditorGutterComponentImpl.getInitialLineNumberWidth使用断点图标占位。不能从一个短文件截图直接改成固定偏移，或删除产品工具栏来使图片相似。

## 局部量测

新增measure-text-ink.py按原图三通道均≥140识别亮色墨迹，边界右下不包含。仅第二三行，排除首行光标／高亮。结果为text-ink-metrics.json；该方法衡量可见墨迹，不等于字形advance或完整字体度量。

| 行 | 参考墨迹边界 | Augit墨迹边界 | 宽度差（Augit−参考） |
| --- | --- | --- | --- |
| First line | [190,196,324,214] | [164,259,296,277] | −2物理px |
| Second line | [189,234,338,252] | [164,297,310,315] | −3物理px |

两端上述连续行的墨迹顶部间距均38物理px，墨迹高度均18物理px；亮色像素数分别365/429与456/496，栅格并不相同。必须继续核对实际字体、字距与抗锯齿，不能把局部行距一致写成字体像素复原。整页配对条件尚未通过，fullPageComparable与pixelRestorationVerified均为false。

## 审计整改与验证

继续核查字宽：参考源码UISettings.kt的editorFractionalMetricsHint并非固定开启，而是由抗锯齿方式、FontSubpixelResolution和注册项决定。旧PixelFontMetrics.java强制开启fractional metrics，因此不能单独证明实际编辑器字宽。新增显式on/off（默认仍on）对照：同一参考JBR、随安装字体、1.75倍变换、13px下，on的spaceWidth为7.799970逻辑px，off为8.000000；物理advance差约0.35px/字符，与截图短字符串累积差方向一致。32档字号行盒／基线在两模式一致。此结果是诊断候选，尚未读取实际编辑器模式，不据此修改产品letter-spacing。两项自动化测试覆盖默认兼容、32档行盒、13px字宽及非法模式；首次错误模式的stderr解码异常已改按字节捕获，再复验。

verify-reference-pair原先仅凭应用名称拒绝自比，测试正向甚至共用同一张sample.png。现增加规范路径相同和SHA256相同拒绝，正向改为两个独立测试图，增加同文件／复制图负向。JPEG负向单独断言格式错误，避免被其他错误掩盖。

capture-text-pair-shell.cjs先激活、查询window/query、必要时最大化，再读最终几何及取屏幕PNG；后续字体记录分列fontFamily/fontSize/fontWeight/letterSpacing，避免CSS font简写为空。最后这一诊断字段修改通过语法检查，尚未再次启动真实外壳，不冒称现有JSON已有新字段。

本批2项墨迹测试与5项配对门禁测试通过；JS语法和git diff --check通过。未变更产品可执行逻辑，未重复运行Shell／仓库全量。

早期失败保留：PID8652前景门禁拒绝；PID22388图未最大化且几何采样过早，排除；PID8804等待前景超时；PID11316用了错误的window/state查询，已改window/query；最终PID18688取得合格原始PNG但尚非整页同态。第一次量测包含参考首行光标，行距45不当作正文结果，最终只量第二三行。

## 启动、停止与清理

```powershell
$pixelPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-text-pair-shell.cjs $pixelPlaywright artifacts/text-pair-new --wait-foreground
# 激活驱动打印的本次窗口后，在另一终端释放采集门；180秒超时后自动清理。
New-Item artifacts/text-pair-new/foreground-ready.signal -ItemType File
# 正常路径finally关闭实例并删除隔离profile；异常中断只结束本次PID。
taskkill /PID <本次Augit_PID> /T /F
$pixelOldProperties=$env:PYCHARM_PROPERTIES
try {
  $env:PYCHARM_PROPERTIES='D:/JetBrains/PyCharm-2026.2.1-reference/reference.properties'
  $pixelReferenceProcess=Start-Process 'D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe' -ArgumentList 'D:/JetBrains/PyCharm-2026.2.1-reference/fixture' -PassThru
} finally { $env:PYCHARM_PROPERTIES=$pixelOldProperties }
taskkill /PID $pixelReferenceProcess.Id /T /F
python -B -m unittest discover -s tools/audit -p test_text_ink.py
python -B -m unittest discover -s tools/audit -p test_reference_pair.py
node --check tools/audit/capture-text-pair-shell.cjs
git diff --check
```

参考PID17616及其子树已停止。五个Augit实例均由驱动finally清理；隔离目录m5Kua7、f9S8GK、pPo0kO、nZgAcE、GzKAId均不存在，端口50949/64324/63928/56096/54257无监听，前景门文件已删除。正式截图／JSON保留。历史自动审批拒绝清理项未重试、未绕过。

四网页MD5前后不变：mockup.js为6298632B16BA82E5FCFB9B0AB177593D，mockup.css为06751CCD396E8ECACD19779CBBDE38AB，live-data.js为DB52470B2BAB434DE4E463086A39FA6D，bridge.js为F297BF8769E249CA0D804F4F4EEEF5A9。混合工作区未回滚、暂存或提交。

## 字符步长修正（同日后续批次）

本节替代前述“尚未修改产品字距”的当前状态，但保留前述原始失败证据。通过computer-use只读打开隔离PyCharm的Appearance页，确认Editor Antialiasing为Subpixel；未操作安全提示或修改参考设置。原始设置PNG为reference-antialiasing.png，SHA256为c1e061a587318a345c80ddf0fe10a85fae2deb77c9edb85145a497ecca552584。UISettings.editorFractionalMetricsHint在该模式默认关闭，隔离配置未发现fractional覆盖；源码是本地263.SNAPSHOT辅助依据，不冒充262二进制直接内省。实际短行截图及参考JBR的整数度量相互印证。

对参考JBR随包JetBrainsMono-Regular.ttf独立量测字号9–40、比例1/1.25/1.5/1.75/2/2.25/2.5/3共256组合，整数模式步长均符合round(round(size×DPR)×0.6)/DPR。原始机器数据为jbr-integer-widths.json，测试镜像为tools/audit/jetbrains-mono-integer-widths.json。仅随包正常400字形实际加载时，以该步长减Canvas原字宽得到letter-spacing；字体回退时置零。普通文本、JSON及Markdown源文、Diff、Blame和三栏冲突正文共用；终端和Markdown渲染预览未改。行号槽同步按修后步长度量。

首版只给容器字距，被全局星号零字距覆盖：spacing-fixed/actual.png中正文仍normal，文字宽度未变，仅槽宽增加。已补实际文本、span、查找mark、Blame子节点及独立冲突行号层；测试从人工继承probe改为实际正文节点，避免再次放过继承缺陷。保留首版失败图，不将它计作通过。

最终原图spacing-glyph-fixed/actual.png为2880×1716，SHA256为db6262e226c27cd5ade8c9a05f5c8c99e1840a9b96ad233bb898d9b575aa12fc，真实WebView2正文letterSpacing为0.200012px。第二三行阈值墨迹边界分别[165,259,300,277]和[165,297,314,315]；相对参考宽度差由−2/−3变为+1/0物理px，墨迹高度18、顶部间距38不变。第一行旁Project tooltip及屏幕光晕仍在，图片只用于无遮挡正文局部，不作为全页同态证据。JBR和WebView2抗锯齿墨迹仍不同，不能宣称字体栅格或整页完全一致。

验证分批结果：

- 2项函数测试覆盖256个独立JBR结果、资源未加载及其他字体回退；2项Java驱动测试覆盖默认兼容、on/off、非法模式和DPI参数。
- 48个真实浏览器组合覆盖4比例×3字号×文本/Diff/冲突/Blame，直接Range量测实际文字步长；另验查找mark、旧零字距负向、切换Consolas和字号往返。
- 正文排版126组合通过（54横向、42纵向滚动）；此轮在第一次继承修正前执行，后续文字继承由48组合、实时流程覆盖，不重复宣称最终全矩阵。
- live-find-blame 26项通过；live-goal-conflict首轮因旧选择器把新行号背景计作正文，30≠15，定位后限定.conflict-line，最终51项通过，包含10,485,078字节混合换行编辑保存（该单样本编辑360ms、总1713ms）。不通过删除行号背景来迁就测试。
- 9共享资产字节一致、JS语法、git diff --check通过。未修改C#，没有重复构建或运行仓库全量。

修改后MD5：mockup.js为8BF2C396751DF1FA9A164E6EBC772886，mockup.css为CB5E64C420FEA13B86EEEE3098BB455C；live-data/bridge仍为上节值。新改动为共享js/css、设计系统说明、Java诊断器、字距数据与回归、冲突测试选择器和本记录；未暂存、提交或覆盖其他混合修改。

```powershell
$pixelPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$env:AUGIT_BROWSER_EXECUTABLE='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node --test tools/audit/code-letter-spacing.test.cjs
node tools/audit/verify-code-spacing.cjs $pixelPlaywright $env:AUGIT_BROWSER_EXECUTABLE
node tools/audit/live-shell.spec.cjs $pixelPlaywright --find-blame-only
node tools/audit/live-shell.spec.cjs $pixelPlaywright --goal-conflict-only
$env:AUGIT_REFERENCE_JAVA='D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/jbr/bin/java.exe'
$env:AUGIT_REFERENCE_FONT='D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/jbr/lib/fonts/JetBrainsMono-Regular.ttf'
python -B -m unittest discover -s tools/audit -p test_jbr_fractional.py
# 真实截图启动/前景门/停止沿用上一节；本批只停止下列已核实的自有实例。
taskkill /PID 21188 /T /F
# Augit 17088/16012由finally结束，异常时才使用对应PID。
taskkill /PID <本次Augit_PID> /T /F
```

参考PID21188及子树已停止；Augit17088/16012均正常清理，LIuQoV/1OyND6隔离目录不存在，63947/54834端口无监听。Java度量进程已退出，浏览器和live-shell临时HTTP服务由finally关闭且驱动正常结束。正式截图、度量数据保留；未重试历史被审批拒绝的清理项。整体四项目标仍未结案。
