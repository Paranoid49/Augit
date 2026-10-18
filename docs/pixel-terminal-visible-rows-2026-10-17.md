# 终端全部可见行与同尺寸参考（2026-10-17）

## 本批结果

补齐实际端全部14行的buffer索引、滚动位置、整行DOM边界、提示符及五个空白行。正常入口原图与新PyCharm参考均为2880×1716、DPI168。完整内容区并集1370040像素，RGB容差2仍差97830像素，精确差98086；未平移、缩放或排除行尾。这里只是同尺寸全区域诊断，面板、焦点和整窗内容未同态，完整逐行配对仍未完成。

## 实际入口与产物

目录artifacts/pixel-followup-2026-10-17/terminal-visible-rows。实际PID7576、端口51493，经tools/start-augit.ps1强制非增量构建，真实配置隔离副本、默认Session、UI12及DPR1.75；仅沿用已批准Custom启动表达式输出八行Ag 0123456789，不向buffer注入文字。实际字体CDP确认随包JetBrains Mono13、lineHeight1；DLL SHA256为62BE2A9637AC367090EC62CE0DEB322F83F70935A4C36EFB145A31A2FFD6958F。实际执行脚本/CSS/字体身份保存在runtime-identity.json，四网页MD5与生命周期批次一致，Release未更新。

actual-light.png SHA256：026612450de7c15fc8b4e5fb0b5df8235e5d673b16290f3bdf99d8dcfd432b36。屏幕前景、进程、DPI和边界稳定。指针在上方编辑区，终端文字及行尾目视无覆盖；全窗仍含指针光晕，未登记全窗无遮挡。

visible-rows.json记录viewportY=baseY=0、14行198列，第1–8行为固定输出，第9行为真实提示符，第10–14为空白；光标buffer行8。全宽网格向外取整覆盖1225666像素，每像素仅计一次。每个输出行阈值80暗像素562、提示符1721、五个空白行0，不能据此把与参考的差异算零。网格外的面板内边距另纳入完整内容区。

## 参考与差异

复制既有参考配置到本批隔离目录，通过安装包powershell-integration.ps1的JEDITERM_SOURCE钩子执行Clear-Host及相同八行表达式；没有通过Windows UI输入终端命令。参考PID21320，真实加载隔离安装的jvm.dll和fontmanager.dll，亦有Nahimic注入。终端字体沿用既有已验证参考配置，本批未新增JVM实际字形来源证明。

reference-light.png SHA256：187f10d799d0f98a5f63f47e6ccfe9fcfff73fee5ac27e6388d1b8e7d04eb609。通过正常最大化操作取得2880×1716；点击编辑区让终端失焦，指针移出终端后采集。原始PNG完整保留，参考面板正文边界[72,1178,2810,1661]，实际[72,1204,2868,1668]，右下不包含；依据原图标题分隔、正文外缘和底边。并集为[72,1178,2868,1668]，包括参考顶部、滚动条、两侧及实际网格之外内边距。

参考八行墨迹顶部1187/1219/1250/1282/1313/1345/1376/1408；实际1227/1259/1290/1322/1353/1385/1416/1448，仍差40物理px；参考提示符1460、实际1477，差17px。参考在输出与提示符间有额外块间距/分隔，因此不能整体平移40px便认为配准。两端终端焦点/光标状态、面板高度宽度、主区内容和侧栏也不同。该结果用于定位布局残差，不直接据此改变产品字体或引入固定平移。


## 行级补测（2026-10-17）

按暗像素投影对两端 8 行 `Ag 0123456789` 再做一次只读度量，补充此前只有纵向顶点的定量：

- 行顶仍为参考 1187/1219/1250/1282/1313/1345/1376/1408，实际 1227/1259/1290/1322/1353/1385/1416/1448，**整体差 40 物理像素**；相邻步长在 31/32 间交替，两端一致。
- 每行墨迹横向起点：实际约 94/98，参考约 96/99，**差约 1–2 物理像素**；墨迹像素数实际约 70/73、参考约 73/76，差约 3 像素，属抗锯齿残差量级。
- 提示符顶部实际 1477、参考约 1460，**差 17 像素**；参考在输出与提示符之间有额外块间距/分隔，属于已批准的命令块装饰差异，不能用整体平移 40px 配准。
- 结论更新（2026-10-17 面板高度对齐复测）：真实拖动底部分隔条 +23 CSS 后，八行墨迹顶与参考 **0–1px**、步长 31/32 一致、左缘差 1px；提示符差 22px 属已批准命令块间距。字形/行距不再是 40px 面板原点问题；面板宽度/状态栏/编辑区内容/焦点仍未同态，完整终端配对仍未通过。证据见[终端面板对齐](terminal-matched-panel-2026-10-17.md)。

## 工具与测试

- terminal-viewport-snapshot.cjs只读可见buffer与DOM；4项行为测试覆盖滚动映射、空白/末行保留、缺行拒绝，与5项配置测试合计9项通过155.46ms。
- measure-terminal-viewport.py逐像素覆盖整行，双端比较使用内容区并集。7项反例覆盖分数边界、空白与最右下残差、行尾裁切、缺行/索引错位、越界、不同尺寸和网格外内边距，通过2ms。
- 实际采集验证新增工具在真实xterm可用；JS语法及git diff --check通过。只运行相关审计测试，没有重跑Shell全量或字体来源实验，没有新增依赖或修改终端产品逻辑。

复验覆盖：D:/Python/Python3119/python.exe -B tools/audit/measure-terminal-viewport.py artifacts/pixel-followup-2026-10-17/terminal-visible-rows。

复验双端：D:/Python/Python3119/python.exe -B tools/audit/measure-terminal-viewport.py artifacts/pixel-followup-2026-10-17/terminal-visible-rows --reference artifacts/pixel-followup-2026-10-17/terminal-visible-rows/reference-light.png --reference-region 72 1178 2810 1661 --actual-region 72 1204 2868 1668。

## 失败、清理与后续

参考首启PID17556的PowerShell属性数组拼接成一行，已停止并保留reference-launch-failed.json；改为显式字符串列表后成功。分析首次命令转义换行失败，未产出通过结果，改为chr(10)后复验。

实际PID7576及端口51493、Augit-terminal-pair-exr13M临时目录已清理；参考通过Alt+F4及确认Exit正常关闭。cleanup-check.json确认PID17556/21320及已登记子进程均退出，无本批监听。fixture/notes.txt仍为原三行，未输入编辑内容。

自动审批拒绝了包含递归删除本批参考目录的命令，未改用其他通道绕过。目录C:/Users/wl024/AppData/Local/Temp/Augit-terminal-visible-reference-e450923cf7644720a3f054db023da2e7仍存在；历史拒删目录也未动，不能宣称全部临时资源已清理。用户可确认进程退出后手动删除该精确目录。

下一步需统一可见内容、面板几何、焦点/光标并采集新的双端证据；提示符块间距应先核对参考终端引擎与当前产品边界。不能通过裁交集、逐行墨迹配准或改视觉稿迁就实现。55页全状态、设置慢开/闪帧、刷新全场景、冲突长文及完整阴影仍未完成。Windows10排除本轮，跨屏DPI未验证。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止优先正常关闭，异常仅taskkill /PID <本次PID> /T /F。实际复采沿用capture-terminal-pair.cjs的--startup-rows --wait-foreground，取证后finally清理。
