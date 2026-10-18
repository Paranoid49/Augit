# 当前查找、Blame与文件历史日期整改（2026-10-10）

本批次处理用户最新四项差异，并按用户裁决把搜索进行中导航与Blame提示两项规范分歧改为PyCharm行为。进入任务时工作区已有大量混合改动；分支为`dsh`、HEAD为`3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`，权威源码为IntelliJ提交`576e32820af82f97529d70f9269726803a27c016`。没有整体暂存、回滚或提交这些混合改动。

## 本轮实现

| 项目 | 结果 |
| --- | --- |
| 当前文件查找布局 | 正文、静态文本、Markdown/JSON原文和Diff共用currentFindBar。搜索图标、文本与大小写/全字/正则在同一输入外壳内，计数和导航在壳后、关闭右对齐。计数使用9888 results样本，普通计数变化不移动导航。 |
| 无匹配导航状态 | 无匹配、搜索中、IME组词、非法正则和超时禁用上一项/下一项；Enter/Shift+Enter不排队。Tab跳过禁用按钮，全局区域捕获不重复移动查找条焦点。 |
| Blame背景色 | 默认按提交者时间倒序分组，同时间的不同提交同色；五个浅色/深色锚点每段四个中间色，共21色，通道按参考浮点截断，分组索引为floor(21×索引/组数)。Git porcelain解析committer-time并经模型、桥接、实时状态传到渲染层。 |
| 文件历史日期 | ShellPrettyDate使用0到61分钟的Math.rint等价边界、Moments/A minute/N minutes/1 hour ago、Today、Yesterday和普通本地日期时间；dateTime保留完整时间，日期列直接使用date，提示使用dateTime。 |
| Blame富文本提示裁决 | 取消原生title；应用内提示提供完整提交哈希链接、作者、日期时间、可选跨文件路径及完整提交消息。哈希链接复用既有日志定位，不改变只读正文。完整消息按需读取并合并同提交请求，视图关闭/重建后丢弃旧提示响应。作者、路径、消息均按文本转义；没有新增问题跟踪器集成或Git写操作。 |

完整消息缓存最多64项；超过16384字符的消息完整显示但不保留在缓存，避免每项20MB输出边界累积成大内存。提示的定时器、document/window监听和缓存在区域释放时清理；没有新增依赖。

参考调用链：`SearchTextArea`的Embeddable动作、`StatusTextAction`宽度样本、`PrevNextOccurrenceAction`的hasMatches/isSearchInProgress；`AnnotateToggleAction.computeBgColors`、`AnnotationsSettings`、`ColorGenerator`及`GitFileAnnotation.GitRevisionsOrderProvider/getHtmlToolTip`；`FileHistoryPanelImpl.DateColumnInfo`、`DateFormatUtil.doFormatPretty`和`UtilBundle.properties`的分钟消息。

本轮通过computer-use技能只读打开当前PyCharm查找条，确认三个开关位于输入外壳内部，计数和箭头随后、关闭靠右；取证后按Esc关闭，保留用户PyCharm与Project页面。没有执行Git写操作。

## 文件与当前哈希

实现涉及`web/src/current-find.js`、`mockup.js`、`mockup.css`、`live-data.js`、Core的GitBlameLine、Infrastructure的GitHistoryService、ShellBridge和ShellPrettyDate；共享JS/CSS已同步`docs/ux-mockups/`。新增`tools/audit/live-find-blame.cjs`，扩展相关视觉稿、Shell与Infrastructure测试。UX、设计系统、05/14分册及行为索引同步现行裁决。

| 文件 | 接续时MD5 | 完成MD5 |
| --- | --- | --- |
| mockup.js | E29B5BBDE9944607021F67DFA96D625D | 6BC1E49C8908A193A9D4E5A1D033A4B6 |
| mockup.css | E7CA51583E45C789312FE55CECA47689 | B46C6E073405329890FF4CA409135324 |
| live-data.js | EEE6AC59AF80DB43D041E3A2363CE7C7 | BBE36CBF9F0E796B5E36B8F88A584895 |
| bridge.js | 16E41439BB7F4C2A36C2E95A4375A823 | 16E41439BB7F4C2A36C2E95A4375A823 |
| current-find.js | 未另取接续前哈希 | A3F8CDE2978649571613D39D4A2A03A1 |

接续时mockup.css和current-find.js两侧漂移；最终四个共享文件全部字节一致。历史批次哈希不代替本表当前状态。

## 分批验证

| 命令/范围 | 结果 |
| --- | --- |
| live-shell.spec.cjs … --find-blame-only | 最终23项通过：正文/Diff、空结果Tab、普通与Worker在途禁用且不排队、IME、非法正则、同时间配色、五锚点、安全消息、提交定位、关闭后晚到提示、日期透传 |
| verify-ux-find.cjs … | 48组布局状态及5组IME连续场景通过，覆盖浅深主题、13/40px、1024/1180宽度、查询/错误/超时与资源释放 |
| verify-ux-find-documents.cjs … | JSON/Markdown查找流程通过 |
| verify-ux-blame.cjs … | 24组主题/字号/DPI、三列、纵向滚动、提交定位与关闭恢复通过 |
| dotnet test Shell测试项目 … --filter相关类 | 4项通过、2秒 |
| dotnet test Infrastructure测试项目 … --filter FullyQualifiedName~GitHistoryServiceTests | 21项通过、5秒 |
| dotnet test Shell测试项目 … -c Release | 125项通过、16秒；桥接字段变化后触发模块完整验证 |
| dotnet build Augit.slnx --no-restore --disable-build-servers -c Release | 0警告、0错误、30.43秒 |
| dotnet format … --verify-no-changes --include本批次C#文件 | 通过 |
| verify-ui-assets、verify-script-encoding、JavaScript语法、git diff --check | 通过；PowerShell编码14项 |

测试发现并修复全局Tab捕获与查找条自身循环重叠、通用aria提示给Blame补回title两处串扰。Infrastructure第一次测试编译误用Shell测试帮助方法，已改用该项目的RunAsync并重新通过。125%DPI下底部滚动极值因取整相差1个逻辑像素，滚动同步判据容许此差值；首行边界与行高仍独立核对，此项不登记为完全像素一致。

浏览器截图与JSON保留于`artifacts/find-blame-2026-10-10/`，包括浅深查找正常/空结果、Blame提示及browser-surfaces.json。已经查看共享视觉稿的实际尺寸截图；截图不作128×128图标归一化，也不代替真实外壳或字体栅格验收。

真实外壳取证命令被自动审批拒绝，只返回`blocked by policy`，未给更具体原因；命令没有执行，未启动Augit、9351端口或隔离WebView2目录。`live-find-blame-shell.cjs`保留作可复验的取证脚本，本批次未执行成功，不能记为实机通过。同字体抗锯齿、复杂提交图、所有Git状态色、滚动条动态、17个历史页面、深色全页面以及Windows10 22H2实机仍未完成，不能宣称100%复原。

## 资源与复验

所有本批次浏览器和定向HTTP服务器都在finally关闭；最终检查本批次验证命令/临时Chromium进程0、9351监听0，`.tmp/find-blame-webview-20261010`不存在。截图/JSON属于正式验收附件，保留。没有关闭用户PyCharm或停止其它工具/应用的进程。

定向测试的启动命令如下，正常或失败都会自动关闭服务器与浏览器，无需手动停止：

```powershell
$env:AUGIT_BROWSER_EXECUTABLE = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
node tools/audit/live-shell.spec.cjs 'C:/Users/wl024/AppData/Local/OpenAI/Codex/runtimes/cua_node/2c9e75c4e9c71beb/bin/node_modules/playwright' --find-blame-only
```

供用户手动启动与停止Augit的命令（本批次未成功启动）：

```powershell
$augitFindProbe = Start-Process -FilePath 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit --web-root D:/github/Augit/web --scene blame --blame web/src/mockup.js --theme Light --no-session-restore' -WindowStyle Hidden -PassThru
# 只关闭上面这条命令启动的窗口；不按进程名关闭其它实例。
[void]$augitFindProbe.CloseMainWindow()
[void]$augitFindProbe.WaitForExit(5000)
```
