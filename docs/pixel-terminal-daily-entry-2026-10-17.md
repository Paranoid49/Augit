# 终端正常入口与八行接续（2026-10-17）

旧终端采集直接启动exe、固定窗口尺寸并使用合成配置，不能用于正常入口验收。本批将capture-terminal-pair.cjs接入tools/start-augit.ps1强制非增量构建，使用真实配置的隔离副本和默认profile；仅按已授权采样命令输出八行，不改变产品字体、DPI或窗口尺寸。新增配置保留及拒绝字号覆盖测试，5项通过。

## 已取得证据

目录artifacts/pixel-followup-2026-10-17/terminal-daily-entry-corrected。实例28540、端口52934，普通index入口，DPR1.75、UI12，终端选项JetBrains Mono13、lineHeight1，14行198列。真实PowerShell输出八行Ag 0123456789及提示符；未向渲染buffer注入文字。截图SHA256为87ebcae0a165d12079528b8eca9959dc9a063755bc36fc5917e5d64acd405cf3。

八行文字顶部为1227/1259/1290/1322/1353/1385/1416/1448；既有参考为1271/1303/1334/1366/1397/1429/1460/1492。双方步长均32/31交替，最大累计偏差0.5物理px；原点相差44px。本项只验证行距，不代表完整逐行像素配对。未重做已完成的字体来源实验。

实际执行脚本、CSS及WOFF2身份已核对，但CDP对.xterm-rows父节点返回空字体数组；终端选项不能代替实际绘制字体，本批暂不宣称实际字体通过。截图文字区目视无遮挡，左树有指针光晕及提示，未通过全窗指针净空门禁。pixelRestorationVerified仍为false。

## 实际字符字体补证

后续将CDP采样从无直接文字的.xterm-rows父节点改为.xterm-rows > div > span，最多读取32个字符节点，仅glyphCount大于0的结果计实际字体。空集合新增失败门禁；3项身份测试和5项终端配置测试通过。

terminal-daily-font-confirmed再次走正常启动器，PID23296、端口62608，默认profile及真实配置副本，实际字符报告JetBrains Mono / JetBrainsMono-Regular、isCustomFont=true。原图SHA256与前次87ebcae0…405cf3完全相同，八行完整墨迹框均178×21物理px，每行562个阈值80下的暗像素；不再将父节点空数组当字体证明。采集后finally停止本次进程并删除Augit-terminal-pair-VLPm3q。指针仍位于左树，局部文字证据不外推整窗无遮挡。

八行墨迹框局部对齐的诊断已保存full-row-diagnostic.json，每行3738像素全部计入，RGB容差2差异为1255/1255/1352/1795/1923/1891/1683/1310。未缩放原图，未把局部平移视为窗口配准；参考背景和光晕影响仍在，不能直接归因于字体粗细。

## 失败与资源

terminal-daily-entry首次错误等待真实会话不存在的.code-view，超时后finally清理；改为主窗标题区后成功。PID19132/28540及端口54622/52934由驱动清理，临时目录soTKFA/IVqRIh删除，收尾独立复核另记。全部原始图与JSON保留，未提交混合工作区。

复验：node tools/audit/capture-terminal-pair.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/terminal-next --startup-rows --wait-foreground。脚本等待真实前景核对后继续，不自动降低门禁。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止优先关闭窗口，异常仅taskkill /PID <本次PID> /T /F。Windows10排除本轮，跨屏DPI未验证；整体目标未完成。
