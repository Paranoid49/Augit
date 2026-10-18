# 终端行格相位与多行漂移分析

2026-10-15接续。目标仍未完成，本批没有修改产品行格或文字偏移；避免根据两行样本将18逻辑px错误改为固定31物理px。Windows 10排除本次验证，跨屏DPI未验证；冲突窗口布局规范确认仍待用户回复。

## 新证据

证据目录：artifacts/pixel-followup-2026-10-15/terminal-row-phase。

TerminalRowRaster.java使用参考安装内JBR和JetBrains Mono，字号13、基础高度18、175%比例，分别渲染四种纵向相位的20行相同字串。terminal-row-raster.cjs用当前随包字体和当前xterm生成对应夹具。两者都是独立夹具，不冒充PyCharm实机窗口。multi-row-analysis.json记录八张原图哈希、每行位置、19个步长和累计漂移：步长为31/32交替，20行最大累计漂移0.5物理px。参考JBR相位0/0.25以31起步，0.5/0.75以32起步；说明31与32单次间隔不能单独证明行格正确或错误。

当前真实WebView2自然横幅仍为32对PyCharm参考31。实际行容器顶部663.6875逻辑px，行高18、设备行格31.5。浏览器字体度量ascent13/descent4，参考JBR为14/4；两种渲染的基线起点不同，但不能直接假定加1逻辑px即可解决。

live、live-baseline、live-origin均为严格前景/PID/窗口尺寸检查的原始系统PNG。探针只在隔离实例中调整文字层相位，不改变ConPTY输出，也不发送Shell命令。偏移0.25/0.5物理px时相同字形两行步长为31；0/0.75/1/1.75/2物理px时为32。按容器原点向上取整得到0.546875物理px，仍为32。参考基线差对应的1.75物理px也未修复。因此没有把固定偏移或原点取整作为产品整改；这些试验只证明栅格相位敏感，不能代替整窗同态配对。

baseline.json早期版本读取空font简写，得到默认字体度量，已修正为fontSize+fontFamily；仅live-baseline/live-origin的ascent13/descent4有效。浏览器标记初次放到行容器得到0、随后等待文字span渲染并移入该span，测得基线13。失败及无效度量不作为结论依据。Java以源码模式运行，没有生成class临时文件。

## 工具与验证

- 新增TerminalRowRaster.java、terminal-row-raster.cjs，保留可复验夹具；PNG和JSON是正式证据。
- capture-terminal-pair.cjs新增可选--baseline-probe，记录实际尺寸、不同文字相位系统图和原点修正；默认自然横幅路径不改变。
- measure-terminal-row-step.py新增重复字形多行检查，拒绝行数不符、越界及无效参数，输出累计漂移且始终不宣称像素复原。
- 4项分析器定向测试通过（0.099秒），8份20行夹具分析通过，两个JS语法及git diff --check通过。没有产品逻辑修改、构建或全量回归。
- 当前产品MD5保持：mockup.js 6F4A29DE66FF139F95CBDEF6074B5CBD；mockup.css 411B555CAC9293854DD200E3C1C08625；live-data.js B48E37B755997A2C28D98AF8CB87E7ED；bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。

## 启停和清理

真实Augit PID4320/1536/9568，端口51486/53036/57870，隔离目录Augit-terminal-pair-cItnJ6/4748oc/cJHBEK由finally停止删除。JBR源码执行已退出，浏览器夹具finally关闭，无常驻服务器。没有重新启动PyCharm参考，没有操作用户终端或修改隔离参考配置，历史被拒绝清理目录未重试。

~~~powershell
& D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/jbr/bin/java.exe tools/audit/TerminalRowRaster.java D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/jbr/lib/fonts/JetBrainsMono-Regular.ttf artifacts/terminal-phase-next
node tools/audit/terminal-row-raster.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe artifacts/terminal-phase-next
node tools/audit/capture-terminal-pair.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/terminal-phase-live-next --baseline-probe
python -B tools/audit/test_terminal_row_step.py
# 正常自动清理；异常时只停止本次输出的PID及其子树。
taskkill /PID <本次Augit_PID> /T /F
~~~

下一步仍需取得参考终端同态多行输出与绝对基线证据；不得使用偏移0.25样本宣称修复。55页/全部模态、刷新透明帧与延迟、终端完整配对、系统阴影仍未完成。
