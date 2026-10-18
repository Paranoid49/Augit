# 远端实机布局批准后整改（2026-10-17）

2026-10-17远端组件接续：安装版实际布局确认无外层布局边框、工具条24加1分隔、表头25及26×24动作按钮；正常入口修后完整图RGB2差20902→17120，仍未通过。CSS MD5=0D75936BA3E8F4077344545FA1604A32，Push URL保留。41项行为、12组布局与3项身份/门禁通过；最终进程实际映射已核对。两批Augit清理完成，参考PID21240和临时目录清理被自动审批拒绝、等待用户指示，不能宣称零残留。详见[组件边距与复验](pixel-remote-component-insets-2026-10-17.md)。整体未完成。

用户明确答复“批准按实机调整，保留 Push URL”，解除此前尺寸、标题栏和工具条的规范冲突。未改远端读写、Fetch默认勾选、嵌套取消及产品额外Push URL。

## 最新：禁用工具图标灰阶

原始参考reference-empty.png的Remove/Edit纯色为#BDBDBD，remote-statustext-position实际为#9FA2A8，后者来自Label.disabledForeground。沿用pixel-conflict-toolbar-2026-10-16.md中安装版262的GrayFilter实算证据，仅将.remote-toolbar button:disabled浅深分别设为#BDBDBD/#5A5A5A。两侧共享CSS同步，不改变位置、命中区或文本色，Push URL保留。

remote-empty-layout.test.cjs新增禁用SVG颜色与选中数据后恢复启用色断言，修前失败，修后浅深×UI12/40四组通过2.67秒，包含既有滚动及列对齐检查。9项共享资产及diff格式通过，未跑无关全量。

正常启动器强制构建的remote-disabled-gray-retry：PID28348、端口64995，真实配置副本、默认Session、DPR1.75、Light/UI12。actual-empty.png为624×385、actual-define.png为709×449，前后PID/HWND/前景和96px指针净空门禁通过，两原图目视无遮挡。三层取消、Add焦点归还、Push唯一、快照释放及Git配置字节不变通过。process-identity.json保留实际exe命令行及Augit.dll模块路径，仍加载Nahimic AudioDevProps2.dll，不作干净环境性能验收。两份runtime.json核验执行脚本、CSS和字体字节；标题/空态实际Microsoft YaHei UI，不外推未采样字形。

整图240240像素、排除0，容差2差异20982→20969，精确差21651→21640，平均绝对通道差3.800397→3.785866。修前后仅225像素变化，界限[82,97,152,119]，全部在两个禁用图标内；几何和字形残差仍在，不计整页通过。新PNG SHA256=02BB4E479F8F490C520D8662C75243C0C7446C47F8E05B1628FC4BAE6EA18C3F。深色只有安装版计算及同引擎测试，本批没有深色系统配对。

首次remote-disabled-gray被指针净空门禁拒绝，failure.json保留原因；未降低门禁。重试通过Computer Use在已观察主窗空白区清除指针后取证。实例25864/28348、端口59868/64995、两临时目录jTchUN/i7rCXF及本批signal均已清理，cleanup-check.json独立复核；历史拒删参考目录未触碰。

CSS MD5由6E79CBFE5FA5629AC900EAF05D109A43改为D671A6BA32C439BD4CBD774862FB084C；JS=661C7570EB5805BB8759F2B2C6DF169F、live-data.js=D64AB5C4E97AA7D3ED60F456555A34AE、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9不变。Debug随包更新，Release未更新；DLL为53D7FB2553EA41C4C405425D48995A9312ECBC3DBEA8CC980AB64A5868DC94D5。未提交。

## 前批布局修改

主窗默认宽度为624/1.75逻辑像素，高220；保留名称和URL内容驱动扩宽。标题40px品牌槽、16px Augit标记、400字重、32px关闭区且无底线；工具条24px并覆盖通用图标28px最小命中区，防止按钮溢出及遮断外框。底栏50px，主按钮600字重。Define沿用同一标题结构并继续按完整三行表单计算高度。共享mockup.js/CSS两侧同步；UX和设计规范同步。

新增几何回归先因缺品牌失败，随后检出工具条通用36px下限，以及通用图标28px最小命中区。最终四组空态、八组Define字号/主题及限高测试通过；6项布局/身份/门禁测试4.71秒。远端行为41项、12组DPR/主题/字号布局通过。旧测试把标题首个span当作文字，已改读明确remote-title，未削弱标题值断言。9项共享资产、JS语法及git diff --check通过。

## 正常入口证据

最终目录`artifacts/pixel-followup-2026-10-17/remote-approved-final-retry`。启动器强制非增量Debug构建、真实配置副本、默认profile、无固定DPI/尺寸/场景。原生PNG及JSON通过PID/HWND、前后前景、DPI、边界和96px指针净空门禁；已目视无遮挡。主窗624×385与参考原图相同，Define709×449，仍保留Push URL，正文溢出均0。三层取消、Add焦点归还、Push唯一、最终快照0、所有者恢复及Git配置字节不变均通过。

主窗实际执行源码、CSS和WOFF2与本次源码哈希一致；DPR1.75、界面12px、标题和空态实际字体Microsoft YaHei UI（CDP实际字体列表），URL为普通index入口。DLL磁盘SHA256为ecb4793953404e0a7041718ebaa7a2486d7a26dd4c33416c2d15c80f2bbecf8d；exe路径由电脑操作窗口列表核对。两状态分别保存runtime.json，后续设置候选构建不能沿用此DLL身份。

与既有PyCharm2026.2.1参考空态同尺寸全图比较：240240像素、排除0，精确差22189像素，RGB容差2后仍差21520像素，最大通道差255，平均绝对通道差4.072877。品牌、文字墨迹、工具条细节、正文空态纵向和按钮位置仍有差异，pixelRestorationVerified=false；不增加55页合格页数。Define参考无Push URL，不能把额外行裁掉冒充整页匹配。

## 空态纵向后续修正

本地权威StatusText.java的myShowAboveCenter默认true，getTextComponentBound采用(ownerHeight-textHeight)/3；JBTable.java为默认StatusText，GitConfigureRemotesDialog未覆写。此前flex垂直居中与此不符。共享DOM增加文字span，CSS用1fr/auto/2fr保持该规则；大字号/浅深四组几何回归先红（实际365.5、应356.33逻辑px）后绿，填充数据后独立滚动及列对齐仍通过。规范同步为水平居中、纵向剩余三分之一，不把3px残差写成固定补偿。

正常入口remote-statustext-position，PID5276、端口59807、默认profile与真实配置副本，主窗624×385，Define保留Push URL且正文无溢出，两份PNG均经过前后前景及96px指针净空门禁并目视检查。三层关闭、焦点归还、快照释放和Git配置字节不变通过；临时目录Augit-remote-pair-yZxmfW由finally删除。

全图240240像素、排除0，容差2差异由21520降至20982；精确差21651，平均通道差3.8003968，仍未通过整页。阈值150下空态文字墨迹框实际[229,208,396,230]、参考[231,211,396,233]，仍有左2/上3物理px及墨迹残差，不能只移文字隐藏结构差异。实际PNG SHA256为fd092dd248d2185418b04d6b51bc4de79eb15674199492bd66a4a1311d2ac0ca。

本次共享mockup.js MD5为1186D90A02D8BC87F0F2764C13D0440F，CSS为6E79CBFE5FA5629AC900EAF05D109A43；live-data.js及bridge.js未变。上文哈希和21520为前一阶段证据，不覆盖其原图。

## 失败保留和资源续记

`remote-approved-chrome`首次被指针门禁拒绝，保持门禁；`remote-approved-clean`取得两张原图，暴露工具条按钮仍受通用28px规则影响；修正后`remote-approved-final`前景被用户操作改变、等待超时，不能当作成功；最终重试完整通过。未覆盖任何已有原图。

实例20616/10336/21376/27892均由finally退出，临时目录A7lz4Y/A22cDC/oSgzxI/DJoYdH由驱动删除；收尾再独立复核端口及信号。混合工作区不暂存、不提交、不回滚。

mockup.js MD5从CCE614AA84809724C103D6E98505DDE0变为76C99F9F3CABDAF7705EFC7C5D3EF24E，CSS从F988C98DCB03385B301C87AD223FEF45变为A9EC9AAE957B8AEA7B2FF23A7E4CA0CD。live-data.js/bridge.js保持D64AB5C4E97AA7D3ED60F456555A34AE/F297BF8769E249CA0D804F4F4EEEF5A9。

启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`。停止优先关闭窗口；异常仅对本次实例执行`taskkill /PID <本次PID> /T /F`。Windows10排除，跨屏DPI未验证。全55页、全模态、刷新白帧、终端/冲突完整逐行及完整阴影仍未完成。
