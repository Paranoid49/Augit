# 远端组件边距与正常入口复验（2026-10-17）

本批承接用户已批准的“按实机调整，保留 Push URL”，不新增功能或页面。工作区分支dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6，开始584项混合修改；不回滚、暂存或提交。设置慢开和刷新问题不因本批远端改善而结案。

## 根因与修改

安装版PyCharm 2026.2.1隔离配置实例PID21240，以既有ReferenceMergeGeometry只读诊断获取实际Swing布局。仅将诊断采样时长延长，未改参考布局、字体或文档。证据为`artifacts/pixel-followup-2026-10-17/remote-component-insets/remote-components.json`和同批`reference-empty.png`，系统DPI168、缩放1.75，完整窗口624×385。组件逻辑宽356与物理宽624之间存在原生舍入，不能拿整数screen坐标反推每条像素线。

实测DialogRootPane边框内距0，标题40；内容根内距8/12/8/12；ToolbarDecorator外框1，内部动作区24加独立底分隔1；动作按钮26×24、左右内距2；JBTableHeader高25；空态所在JBTable高70；SouthPanel上距8，OK 78×34。权威本地源码：DialogWrapper.java的createDefaultBorder/createSouthPanel、ToolbarDecorator.java的CustomLineBorder、DarculaTableHeaderUI.java默认TableHeader.height=25。

Augit原先继承了通用dialog的布局边框，工具条与表头均把分隔占位计入24px内部，因此外框、表头、空态和底栏不是同一几何结构。现仅远端主窗取消布局边框、用不占空间的内侧outline保留网页预览边界；工具条动作区24另加1分隔、按钮26且gap0，表头及空态起点取行高加1。底栏明确8/12内距。Define和产品Push URL不改。共享CSS两侧字节一致。

未把剩余差异补成任意固定像素偏移。表头当前仍为6px内距，权威JBTableHeader有8px renderer内距，且JTable实际列宽会分配剩余空间；后续须整体核对列几何和数据renderer，不能只移动表头破坏现有列对齐。原生/网页描边和分数DPI栅格差异也仍在。

## 正常入口证据

两批均执行tools/start-augit.ps1强制非增量Debug构建，真实配置副本、默认Session、正常DPI，没有固定场景、窗口尺寸或字号参数。`remote-component-layout`为PID6352/端口62391；最终`remote-component-mapped`为PID27600/端口50213。主表624×385，Define709×449，三行字段含Push URL、初始URL焦点和Fetch勾选，正文无溢出。全部原始PNG经过PID/HWND、前后前景、DPI、96px指针净空门禁；首批两图及参考原图目视无遮挡；最终Define再次目视，URL光标可见，空态与首批在RGB容差2下差异0；Define仅URL光标区域54像素变化，范围[158,176,160,203]。

三层取消、Add焦点归还、唯一Push、最终所有者启用与快照释放、Git配置字节不变通过。附CDP用于读取身份和驱动，不能冒充无CDP验收。最终实际执行CSS SHA256为F8E63AC7BA61D9370431C2F1F65150EAD59E376035C092B512D05A8C8E8D4CF8，实际标题/空态字体为Microsoft YaHei UI，UI12、正文设置13、DPR1.75。字体证据不外推未采样字符。

最终新增实际进程映射读取：Shell DLL SHA256为62BE2A9637AC367090EC62CE0DEB322F83F70935A4C36EFB145A31A2FFD6958F，Infrastructure为1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80，路径均为Debug/win-x64；哈希来自实际映射路径对应的磁盘文件，不是内存字节哈希。仍映射Nahimic AudioDevProps2.dll，未证明因果。首批只有构建目标磁盘身份，最终映射证据不倒填给首批。

对新参考完整240240像素、排除0、RGB容差2：修前20902，修后及最终均17120；精确差17236，平均绝对通道差2.88808275。旧参考对应修前20969，两份参考不同，不能混用分母或比较数。表格纯色区域框参考[23,84,604,297]，修前[23,87,601,295]，修后[21,85,603,297]；主按钮纯色框参考[473,317,598,365]，修后[471,317,598,366]。这些是纯色点包围盒而非全部抗锯齿边界，几何仍未完全一致。品牌不同未裁除，整页pixelRestorationVerified=false，不增加55页合格数。

## 分批验证与身份

- 新边框断言修前失败（1px而非0），修后空态浅深×12/40四组、Define浅深×12/13/20/40八组及限高通过，合计两个浏览器测试5.31秒。
- 远端行为41项通过。首次启动测试未指定已有Edge，因缺失Playwright自带浏览器失败；改用既有AUGIT_BROWSER_EXECUTABLE配置后通过，未下载依赖。
- 身份探针增加正确PID、错误PID及缺程序集三种分支；连同屏幕/行为模式门禁共3项通过，JS语法通过。
- 9项共享资产、Debug25资源镜像、22脚本编码和git diff --check通过。未跑无关全量，Release未更新。
- CSS MD5从D671A6BA32C439BD4CBD774862FB084C变为0D75936BA3E8F4077344545FA1604A32。mockup.js=661C7570EB5805BB8759F2B2C6DF169F，live-data.js=DE85823861C020608F28DE997197B555，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9均未改。

## 资源与未决项

两批Augit及临时配置目录3aQw54/DHEHtT由finally清理；独立复核PID6352/27600、端口62391/50213、两个目录及signal均无残留。行为测试浏览器和服务器由finally退出。

参考首次打包因JBR没有jar.exe失败，改用Python标准库ZIP完成同一临时诊断jar；没有新增依赖。参考实例PID21240及临时目录`C:/Users/wl024/AppData/Local/Temp/Augit-remote-insets-a8012f834fd543eba6a3651abc8be86c`的清理命令被自动审批以blocked by policy拒绝。已明确询问用户是否授权再次尝试，未换通道重试；13:58独立核查两者仍在。该局部阻塞不阻止其他已授权整改，但不能宣称全部清理。历史拒删目录也未触碰。

启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`。优先正常关闭，异常仅对本次核实PID执行`taskkill /PID <本次PID> /T /F`。本批实际实例已退出；参考需要用户处理或明确答复后再清理，手动停止命令为`taskkill /PID 21240 /T /F`，先确认PID仍属于本批参考路径。

复验：设置AUGIT_PLAYWRIGHT_MODULE为本机Playwright路径后运行`node --test tools/audit/remote-empty-layout.test.cjs tools/audit/remote-define-layout.test.cjs tools/audit/capture-remote-pair.test.cjs`；行为执行`node tools/audit/live-shell.spec.cjs <Playwright路径> --goal-remote-only`，AUGIT_BROWSER_EXECUTABLE指向已有Edge。屏幕复验必须使用capture-remote-pair.cjs新目录及正常入口，不能覆盖旧证据。

设置慢开、全场景无闪/关闭、55页全部模态、背景刷新500ms、终端/冲突完整逐行和完整系统阴影均未完成。Windows10排除本轮，跨屏DPI未验证。goal继续active。
