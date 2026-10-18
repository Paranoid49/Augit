# 终端提示符块间距来源（2026-10-17）

## 最新裁决（覆盖下文“待答复”描述）

用户已于 2026-10-17 批准保留简洁 xterm 终端，并将 PyCharm Reworked 命令块分隔与块间留白登记为**有意差异**，其余文字、行格、颜色、面板和实际内容继续对照。规范已同步：`ux-spec.md` §7.16、`design-system.md` 终端记录、`product-spec.md` 未新增终端能力。下文的来源核对与未通过结论保留为历史证据，不再作为验收缺口；有意差异不得用于掩盖其他未通过的像素或状态差异。

## 本批结论

本批没有改产品、配置或参考引擎，也没有重新采集或重做字体实验。接续terminal-visible-rows中“八行输出统一差40物理px、提示符差17px”的残差，核对当前已安装PyCharm参考终端实现，确认它包含连续字符网格之外的命令块装饰。完整终端配对仍未通过。面板高度对齐复测见[终端面板对齐](terminal-matched-panel-2026-10-17.md)：真实拖动 +23 CSS 后八行顶 0–1px、步长一致，提示符 22px 属已批准块间距。

用户已要求不新增功能，并指出复杂Git界面投入过多。本批通过问题卡提出：保留简洁终端，将命令块装饰登记为有意差异，继续其余文字和面板对照；或先暂停终端视觉对照。尚未收到答复，不能将提案视为批准，未修改产品/UX规范，未切换参考引擎以取得容易通过的图片。此局部边界不阻塞启动、设置和背景刷新整改。

## 可复验的来源

证据目录artifacts/pixel-followup-2026-10-17/terminal-block-layout-source。

- 安装包D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/plugins/terminal/lib/terminal.jar中，TerminalOptionsProvider$State构造函数默认REWORKED；BlockTerminalOptions$State默认showSeparatorsBetweenBlocks=true。
- modules/intellij.terminal.frontend.jar中的TerminalBlocksDecorator：受该分隔开关控制，createFinishedBlockDecoration调用createTopInlay和createBottomInlay；底部插入值为6，顶部首块为0、后续块为7，另创建BlockSeparatorRenderer及TerminalPromptSeparatorRenderer。
- terminal.jar中的VerticalSpaceInlayRenderer.calcHeightInPixels对高度执行JBUI.scale。
- 保留decorator.txt、options.txt、spacing.txt、block-options.txt原始javap -p -c输出。source-manifest.json登记工具路径、JAR与四个class SHA256及类路径；随后重新读取JAR验证哈希一致。
- 历史参考使用的隔离配置目录为C:/Users/wl024/AppData/Local/Temp/Augit-terminal-visible-reference-e450923cf7644720a3f054db023da2e7。只读检索XML未见terminalEngine或showSeparatorsBetweenBlocks显式覆盖，原有记录已实测Reworked引擎。该历史目录删除曾被自动审批拒绝，本批没有删除、改写或重启它。

以上是安装包静态实现与历史配置，不是当前运行中的组件对象快照。不能据此把默认值等同某一帧运行时属性，也不能据此断言所有参考终端状态使用同一布局。

## 原图交叉核对

复用terminal-visible-rows中2880×1716原始系统PNG，重新核验各自sidecar哈希。参考SHA256=187F10D799D0F98A5F63F47E6CCFE9FCFFF73FEE5AC27E6388D1B8E7D04EB609，实际SHA256=026612450DE7C15FC8B4E5FB0B5DF8235E5D673B16290F3BDF99D8DCFD432B36。原图未修改、平移或缩放；查看工具的缩小显示仅供目视，数值分析读取原始文件。

复用measure-text-ink.py，RGB三通道均不超过80取墨迹；参考诊断框[85,1180,300,1500]、实际[92,1220,310,1510]。这些框只用于定位字顶，不能代表完整提示符/全行比较。结果见ink-diagnostic.json：

|项|参考物理y|实际物理y|
|---|---|---|
|八行输出顶部|1187/1219/1250/1282/1313/1345/1376/1408|1227/1259/1290/1322/1353/1385/1416/1448|
|提示符墨迹顶部|1460|1477|

八行输出偏移恒40px，提示符偏移17px，差23px；顶部7加底部6的13逻辑px在175%下名义22.75物理px，与残差相近，支持块装饰是重要来源的推断。不同文字墨迹顶部不是共同基线，且没有采集当帧inlay对象，故不宣称精确因果或已对齐。spacing-cross-check.json保存计算和限制。整窗图目视也可见参考输出与提示符间的分隔线；Augit仍为连续网格。

仍保留既有完整内容区并集差97830像素（RGB2），不通过裁交集、只比较八行、逐行墨迹平移或排除空白降低验收范围。面板大小/位置、焦点光标、主区内容和字形残差仍不一致。没有增加55页合格数，没有把来源定位记为终端完成。

## 当前身份、验证与资源

本批仅只读安装包、旧PNG和配置，并新增证据/文档。正式live-data.js仍EED7AB21899461B5A968EC47EAC41CAFACC903505445F3D73FECA70EE22D6D75，MD5=ACCEC428FDA5173B60F2D83BA02A2BA5；其他三个网页不变。本批没有正常入口新实例，旧图片不能替代当前产品验收。

四个javap进程及Python分析均正常结束，未启动Augit、PyCharm、Shell会话、服务器或录像，无新增临时目录；artifacts下保存的是复验凭据。未触碰已有混合修改，未暂存或提交。没有修改可执行逻辑，故不重复运行产品测试；文档UTF-8/LF及git diff --check通过。

整体目标仍active且未完成。终端边界答复待定，继续其他已授权工作；Windows10排除本轮、跨屏DPI未验证。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。

停止优先正常关窗；异常仅对本次核实PID执行taskkill /PID <本次PID> /T /F。
