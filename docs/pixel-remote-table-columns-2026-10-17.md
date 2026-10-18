# 远端列分配与renderer内距（2026-10-17）

本批承接已批准的实机调整，保留Push URL，不新增功能或页面。工作区dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6，开始586项混合修改；没有回滚、暂存或提交。整体目标仍未完成。

## 证据与整改

既有参考实例PID21240的Git Remotes由ReferenceTableGeometry.java单次只读attach读取。原始证据在artifacts/pixel-followup-2026-10-17/remote-table-columns/reference-table.json：空表实际宽330、高70，表头25，数据行高20；两列preferredWidth为70/250，实际75/255，minWidth均15、maxWidth均2147483647，autoResizeMode=2。诊断没有改变模型、选择或布局。JBR真实JTable自检另确认330→75/255、331→76/255、300→66/234、100→28/72、30→15/15；源码保留，可重新编译复验，不再次向既有进程加载同名agent。

本地权威GitConfigureRemotesDialog.updateTableWidth设定preferredWidth而非固定列宽；JTable无拖动布局按各列可扩展/可收缩余量分配差额。JBTable表头renderer左右内距8；远端MyCellRenderer取消border，SimpleColoredComponent仍保留左右2的内部边距。因此旧“表头和数据文字起点必须一致”测试与权威不符，应检查列边界一致及不同文字内距。

共享mockup.js新增局部列分配函数，在远端测量结束后依据实际table.clientWidth分配名称列，剩余宽度留给URL；窗口resize重新测量，包含受限宽度和垂直滚动条占位。共享CSS去掉整行6px内距，表头单元格取8px、正文2px。两侧共享资产一致。未改变数据行高、Push URL、Git调用和模态行为；参考20px数据行及正文URL字宽的完整数据态仍需后续同态实机核对，不把空态证据外推全部。

## 测试与正常入口

- 修前新renderer内距断言失败：实际0而期望8；修后通过。真实JTable五组列宽对应纯函数回归；空态浅深×12/40、400/1100视口缩放、30行独立滚动与列边界，Define浅深×12/13/20/40及限高，三个测试合计4.42秒通过。
- 远端完整行为41项通过；映射身份/错误PID/缺程序集及截图门禁三个测试通过。一次误调用旧verify-ux-remote未传executablePath，因缺Playwright自带Chromium失败；随后执行既有live-shell --goal-remote-only，明确AUGIT_BROWSER_EXECUTABLE为已有Edge，未下载依赖。
- 正常入口capture-remote-pair.cjs输出remote-columns-daily，tools/start-augit.ps1强制非增量Debug构建，实际配置副本、默认Session、正常DPI，无场景/固定尺寸/字号参数。实际PID18680、端口58548。
- 空态624×385、Define709×449两张原始PNG；前后前景/PID/HWND/DPI168/96px指针净空门禁通过，逐张目视无遮挡。三层取消、Add焦点归还、唯一Push、owner恢复和快照0、Git配置字节不变通过。附CDP用于身份与驱动，不作为无CDP验收。
- 实际映射Shell SHA256=62BE2A9637AC367090EC62CE0DEB322F83F70935A4C36EFB145A31A2FFD6958F，Infrastructure=1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80；仍见Nahimic模块，因果未证明。实际执行JS SHA256=FAEC2493D3BC905C4C3D0BAC383BC706F7738E65A784D005C247E7EA76ACA4A7，CSS=EA54ADFCD799B139C392F402A0D2D973D62FC59E08F8A69769C4E04D8E64BA0B。标题和空态采样字符Microsoft YaHei UI，UI12，正文设置13，DPR1.75。

同一参考remote-component-insets/reference-empty.png，完整240240像素、排除0：RGB容差2差异17120→17028，精确差17236→17144；平均绝对通道差2.88808275→2.93214563，反而增加。因此只能说列几何更符合实测，不能宣称整体像素误差全面改善，更不能登记整页通过。原图、compare-full.json、运行身份和实际映射均保留。品牌、边缘、字体栅格及其它几何差异仍存在。

## 当前身份与清理

四网页MD5：mockup.js从661C7570EB5805BB8759F2B2C6DF169F变为F24517B92AC7A46D6343F3E214C333B2；mockup.css从0D75936BA3E8F4077344545FA1604A32变为00FA3357EC646E91030A9DFDD925DAA9；live-data.js仍DE85823861C020608F28DE997197B555；bridge.js仍F297BF8769E249CA0D804F4F4EEEF5A9。Debug由正常启动器更新，Release未更新。

本批Augit PID18680、端口58548、临时配置目录Augit-remote-pair-FSkf4y及三个signal由finally清理并独立复核。测试浏览器和服务器自行finally关闭。参考诊断临时class/jar在保留源码和JSON后删除。参考PID21240及Augit-remote-insets-a8012f834fd543eba6a3651abc8be86c目录仍存在；此前自动审批拒绝清理，未获得新答复，不换通道重试。不能宣称零残留。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止优先正常关闭；异常仅对核实的本次PID执行taskkill /PID <本次PID> /T /F。本批Augit已退出。参考手动停止前必须确认21240仍为本批PyCharm路径。

设置慢开、全场景无闪/关闭、55页全部模态、背景刷新500ms、终端/冲突完整逐行和完整系统阴影均未完成。Windows10排除本轮，跨屏DPI未验证；goal保持active。

补充收尾：9项共享资产、Debug25项镜像、22脚本编码、JS语法、文档声明及git diff --check通过。历史全量快照已过期，未以旧断言数充作本次验证，也未重跑无关全量。
