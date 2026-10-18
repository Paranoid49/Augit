# Augit 与 PyCharm 像素差异报告（2026-10-13）

2026-10-17远端长URL：按权威粗体度量修正URL列宽，正文仍400；修前共享页面636对期望667失败，修后及正常入口均667逻辑像素。3项布局、41项行为通过，最终JS MD5=0EEBB52037A8EEABDB81DA777E8C1FFE；两实例/端口/临时目录独立清理。未采本数据态双端PNG，不计整页通过。详见[长URL核查](pixel-remote-url-width-2026-10-17.md)。设置/刷新及历史清理阻塞见下一条，整体goal仍active。

2026-10-17最终背景恢复：补失败同图重试不再跳过加载等待；正常入口最后撤盖629.0/567.9/542.8/504.9ms，305完整帧仍有187非光标边缘变化，未通过。最终Shell SHA256=94EF57F5…78EF46；27项Shell覆盖及6项脚本/接线验证，本批两实例已清。用户已批准临时停Nahimic，但服务权限拒绝，前后仍Running/Auto/PID5848，对照未执行；参考PID21240及指定目录虽获清理授权，仍被自动审批拒绝。详见[最终复验与阻塞](refresh-background-loaded-2026-10-17.md)，整体未完成。

2026-10-17背景换图确认：宿主刷新现在等待对应图片onload，补齐脚本提交与加载之间的协议缺口；正常入口四次最后撤盖648.6/527.2/508.4/531.2ms仍超500，307完整帧仍有187非光标边缘变化像素。26项Shell及6项换图/接线测试通过，实际Shell SHA256=954D7E…572FE，本批PID/端口/临时目录独立清理。用户已授权清理历史参考PID21240和指定目录，但自动审批再次拒绝，仍未清理。详见[背景加载确认](refresh-background-loaded-2026-10-17.md)，整体未完成。

2026-10-17无CDP设置GDI时钟：正常启动器472帧与原生阶段已核对；控制器创建2448.24ms、presented3280.86ms，桌面首次出现被302/303相邻帧夹定，慢开仍未解决。实际映射身份已核对，含Nahimic；字体执行证据和正常关闭周期未补齐，不能宣称验收通过。本批PID/记录器/临时目录已清理，参考历史清理阻塞仍在。详见[无CDP时钟证据](settings-gdi-clock-2026-10-17.md)，整体未完成。

2026-10-17远端列分配接续：参考空表330宽实际75/255，表头内距8、正文2；已修复固定列与旧错误对齐断言。正常入口完整图RGB2差17120→17028，但平均色差上升，仍未通过。JS MD5=F24517B92AC7A46D6343F3E214C333B2，CSS=00FA3357EC646E91030A9DFDD925DAA9；41项行为及定向布局通过。本批Augit清理完成，参考PID21240/目录仍待清理授权。详见[列分配与复验](pixel-remote-table-columns-2026-10-17.md)，整体未完成。

2026-10-17远端组件接续：安装版实际布局确认无外层布局边框、工具条24加1分隔、表头25及26×24动作按钮；正常入口修后完整图RGB2差20902→17120，仍未通过。CSS MD5=0D75936BA3E8F4077344545FA1604A32，Push URL保留。41项行为、12组布局与3项身份/门禁通过；最终进程实际映射已核对。两批Augit清理完成，参考PID21240和临时目录清理被自动审批拒绝、等待用户指示，不能宣称零残留。详见[组件边距与复验](pixel-remote-component-insets-2026-10-17.md)。整体未完成。

2026-10-17最新刷新纠正：查明详情缓存以短hash写入、完整hash查询而永不命中，已修为fullHash；正常入口首轮遮盖2次降为1次，最后撤盖919.6→631.1ms，后续518.7/569.1/502.9ms仍超500ms。旧观测仅首轮，不能当完整刷新耗时。最终live-data.js MD5=DE85823861C020608F28DE997197B555；319完整帧仍有边缘变化，不计无闪。23项JS覆盖、25项Infrastructure/2项桥接/6项Python分批通过，九批资源已清。详见[最新根因与计时纠正](refresh-status-batch-2026-10-17.md)。整体未完成。

2026-10-17刷新接续：正常入口实际映射新Infrastructure并确认字体/缩放；状态读取降到约132–144ms，但最终四次完整刷新仍超500ms，303完整帧仍有187非光标边缘变化像素。未新增55页配对通过，完整证据、当前哈希及清理见[刷新整改记录](refresh-status-batch-2026-10-17.md)。

> 本文保留整改前的29项差异与原始数值。当前实施、字体集成、独立窗口、Settings大字号补修及未验收边界见[更新至2026-10-14的接续记录](pixel-font-followup-2026-10-13.md)。55页同态双端配对仍未完成，不能把下文历史值或自身回归通过理解为当前全量像素结案。

## 结论

用户指出的问题成立，之前的检查漏掉了基础组件的明显差异。最严重的不是一个颜色差一点，而是 **Changes 的父子层级缩进方向相反、复选框绘制模型不对、字号放大后容器与图形脱节、部分页面正文和操作区被挤出可见范围**。

本报告提供原始像素坐标、当前实际 CSS 几何、上游数值、代码入口、影响面与整改条件；附保留范围内的 54 个登记页面的实际尺寸清单（原 55 页；三栏冲突页已于 2026-10-17 经批准移出范围，历史测量不得改写为通过）。**没有取得同态参考的字段不填假数字；完整场景清单不等于所有页面已完成 PyCharm 双端像素配对。** 本次实际执行 220 个静态页面/主题/字号组合、12 个定向补测组合、8 份真实 WebView2 几何快照；不是将历史测试数量累加为还原率。

本轮只做审计及交付报告，没有修改产品代码、用户设置或 Git 工作区内容，没有把前一天已修的 R01–R08 重新当成未修问题。

## 1. 证据、单位与基线

| 证据 | 本次实际取得内容 | 可用于什么 |
| --- | --- | --- |
| 用户两张 PNG | Augit 142×299；PyCharm 219×425，保存未缩放副本 | 原图像素框、行步长、相对缩进、纯色值；不直接相减不同裁剪的绝对坐标 |
| 真实 WebView2 | 隔离设置/profile，当前 Debug 产物的 15 个网页资源与源码镜像门禁通过；DPR=1.75，UI12/14，以及1024×640/UI40 | 真实 DOM 矩形与截图；CSS px ×1.75 得栅格化前的物理长度，最终边缘仍可能有抗锯齿取整 |
| 全场景扫描 | 55页面×浅/深×两组：1180×760/UI12/DPR1、1024×640/UI40/DPR1 | 当前计算样式、几何、边界和截图；不是 PyCharm 对照通过证据 |
| 当前 PyCharm 前台 | 主界面、Commit、Git Log、Markdown、分支弹层、Git Remotes、Define Remote | 页面结构、动作顺序和可见状态；Sky 输出是 JPEG 且可能缩放，不把其中采样色当原始精确色值 |
| 本地权威源码 | `D:/github/intellij-community@576e32820af82f97529d70f9269726803a27c016` | 组件尺寸、绘制形状、调色板及 Windows 按钮顺序 |

坐标矩形统一写为 `[x,y,w,h]`；PNG 像素区域采用左闭右开端点。差值为 **Augit − 参考**。CSS 数值保留到 0.01px 只是浏览器测量精度，不表示物理屏幕能绘制百分之一像素。

工作区仍为 `dsh`、HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`，有大量既有未提交修改。当前用户设置文件读到 UI12/Cascadia Mono13，但用户截图和运行中窗口不能据此倒推为已应用此文件；本次隔离实例另做 UI12/14 对照，没有静默替用户恢复外观。

附件目录：[像素审计附件](../artifacts/pixel-audit-2026-10-13/)。其中 `scene-metrics*.json` 保存各元素前3个实例的坐标、字号、行高、背景、边框、内距、圆角和图标矩形；超过3个同类实例未被这份 JSON 穷举。

## 2. 用户截图：直接像素测量

![用户截图像素标注](../artifacts/pixel-audit-2026-10-13/annotated-user-comparison.png)

| 项目 | Augit 原图 | PyCharm 原图 | 差值／结论 |
| --- | --- | --- | --- |
| 分组复选框外接矩形 | `[58,5,26,26]`，已勾选 | `[64,23,28,28]`，未勾选 | 外接尺寸各少2px；状态不同，蓝/白不作为错误比较 |
| 第一文件复选框 | `[16,57,26,27]` | `[97,65,28,28]` | 宽−2px，高−1px；分数栅格落点产生26/27px交替 |
| 后续复选框上边 | 110、162、215 | 107、149、191、233、275 | Augit步长53/52/53；参考恒42 |
| 文件行中心步长 | 52.5px | 42px | **+10.5px，+25%**；第一到第四行中心间距多31.5px |
| 子项相对分组复选框左边 | 16−58 = **−42px** | 97−64 = **+33px** | **−75px，缩进方向错误**；两图裁剪偏移已被消除 |
| Amend 未勾选框 | `[6,268,26,26]` | `[24,368,28,28]` | 同为未勾选，宽高均−2px |
| Amend 框边稳定纯色 | `#D1D3D9` | `#A8ADBD` | RGB差 **(+41,+38,+28)**；Augit明显偏淡 |
| 文件及已选框蓝色 | `#3574F0` | 图标蓝`#3574F0` | 此色一致，不应一并改掉 |
| 分组选中背景 | 白底 | `#E9EAEE`灰底 | 两图行选择/焦点状态不同，**不直接判为缺失背景**；应补同态对照 |
| Amend 文案 | 整段“Amend last…”同色 | “Amend”＋蓝色“last commit”链接 | 结构不同；完整当前前台还可见参考下拉箭头和帮助提示 |

不能用两图中 Amend 的绝对 y=268/368 相减得出“位置错100px”：截图起点、列表视口高度和可见行数不同。必须用同窗口尺寸、同分隔位置的配对截图测量该项。

### 对行高问题的订正

隔离真实外壳中 UI12 的文件行实际是24 CSS px，DPR1.75下为42物理px；UI14 触发1.25档用户缩放，行为30 CSS px，即52.5物理px。**原图25%的行高差在当前实现中可以由不同的生效字号档位复现；不能因此把全部30px行高都改成24px。** 但缩进、复选框尺寸与边框色在 UI12 下仍然错误，和字号差异无关。

## 3. 确证差异与整改清单

### A. Changes、复选框及其复用页面

| 编号／优先级 | 实测与参考差值 | 根因、影响范围及整改条件 |
| --- | --- | --- |
| A01／P1 | UI12真实外壳分组框x=79、子项x=55，**−24 CSS px**；参考子项应在分组右侧，原图+33物理px | `mockup.js:3306` 文件行与分组是平级 flex；仅分组有16px箭头＋8px gap，子项没有层级占位。修复 Changes/Unversioned 的层级布局；统一字号后再测精确目标缩进。不能只改文字 padding 而不移动框、图标、命中区。 |
| A02／P1 | `.fake-check`边框盒15×15；参考SVG的rect为15×15但描边居中扩至**16×16**，少1 CSS px（当前原图少2物理px） | `mockup.css:1355` 把SVG中心路径尺寸当外接尺寸。Changes、Amend、用户/路径筛选和Diff显示菜单复用它。应分别建24px图标画布与16px绘制外框，避免再次混淆路径尺寸和控件尺寸。 |
| A03／P2 | `.fake-check`布局仅15px；参考 CheckBox 图标画布24px，**−9 CSS px**，四周透明留白丢失 | 权威 `ManyIslandsLight.theme.json:531` 的iconSize24、textIconGap4。应校准透明槽、绘制框、文本间隔和点击区，不能将24px全部画成实心框。当前相邻元素的偏移也需随之复测。 |
| A04／P1 | 浅色未选框 `#D1D3D9`，参考`#A8ADBD`，RGB差(+41,+38,+28) | 使用了通用 Component 边框而非 Checkbox.Border.Default。为复选框独立取色；影响所有 `.fake-check`，并单独检查深色。 |
| A05／P2 | 聚焦框：参考路径16px＋2px居中描边，外接**18px**；Augit仍是15px盒加2px内收outline，不能得到同一外轮廓 | `mockup.css:1415`；参考 `checkBoxFocused.svg`。校准普通→聚焦的外轮廓、圆角和外扩位置，不能用通用按钮焦点环代替。 |
| A06／P2 | 禁用选中框仍用白色勾，参考浅色勾`#C9CCD6`；深色勾`#6F737A`，半选横线`#868A91` | `.fake-check.checked::after`将stroke固定white，缺复选框专用禁用图形规则。属于已确认CSS状态模型差异；各业务路径是否实际禁用要单独列状态，不声称所有页面都已复现。 |
| A07／P2 | Clone/Stash框15px，边框浅`#9FA2A8`而非`#A8ADBD`，RGB差(−9,−11,−21)；深色边框`#4C4F56`而参考图形`#5A5D63` | `mockup.css:4112/4142`另写了一套复选框，既不同于参考，也不同于Changes。两页同时校准，包含普通/勾选/禁用/焦点。 |
| A08／P2 | Clone/Stash勾用1.5px边线；参考2px，**−0.5 CSS px**；日期勾用1px，**−1 CSS px** | `mockup.css:4114/4144/3725`使用旋转CSS边框，端点与路径也不同。复用等效图形几何，不能只将线宽数字改对。 |
| A09／P2 | 日期范围框15px、radius3，未选框色仍取`#D1D3D9` | 日期弹层也属于上述框/色/焦点错误族；必须纳入同一整改批次，不能只修用户截图里的Changes。 |
| A10／P2 | UI40时复选框仍15×15，而普通图标已52×52；参考图标随userScaleFactor变化，16px外框在3.25档应52px | 上轮全局图标整改明确把复选框留为独立固定族，导致勾选框与文字/邻图比例失衡。按同一用户缩放契约测量，但不得连系统窗口按钮一起盲目放大。 |
| A11／P2 | Changes选中行radius5；树选中基线radius4，**+1 CSS px** | `mockup.css:3199`仍以“Changes是列表/表格”的旧说明保留5；实际上角色和权威均为ChangesTree。按树组件而非日志表格校准；Settings树的整栏选中是例外，不能套用。 |
| A12／P2 | Amend当前是一段文本＋独立history图标；参考是复选动作＋DropDownLink＋提示，链接后留白4px | `ToggleAmendCommitModeAction.kt`的ToggleAmendPanel、`AmendCommitModeDropDownLink.kt`与当前 `.commit-amend/.commit-last`。这涉及动作组织，不仅是换成蓝字。参考含“指定提交Amend”能力，Augit产品只明确Amend；超出能力部分需裁剪，不能加一个无功能下拉来装饰。 |

### B. 大字号、窗口缩小及全局图标

下列为 **1024×640、UI40**。普通图形52px来自现行用户缩放3.25档；本节不要求PyCharm也在该配置下可同时显示所有面板，只要求本产品支持的字号/窗口组合不把正文和主要动作变成不可见。B01/B02已在真实WebView2再次测到，其余是实际浏览器布局＋源码证据。

| 编号／优先级 | 精确测量 | 整改条件 |
| --- | --- | --- |
| B01／P1 | Commit＋Git Log组合，`.editor-content`高**0px**（静态）；`.changes-list`高**10px**（静态/真实），连一条78px文件行都容不下 | 重新约束工具窗最小高度、底部高度与正文剩余空间；不能只断言整页没有横向溢出就通过。至少保证可聚焦的正文/列表区域有实际可见空间。 |
| B02／P1 | 真实外壳Commit按钮 `[134,608,179,98]`，底至706，超窗口**66px**；Commit and Push `[134,713,280,98]`，顶部就超窗口**73px**，底超171px；状态栏从y549覆盖底部91px | `measureCommitPanels()`按控件最小值计算后未保证整块进入宿主可用区。动作须固定可达，正文独立滚动；不得通过增加空白或把控件藏到状态栏后解决。 |
| B03／P1 | Changes分组箭头容器16×20；内部SVG52×52，左右各外溢**18px**、上下各**16px** | 图形被全局SVG规则放大，`.change-chevron`槽仍固定16×20。同步容器、命中区、中心及缩进，验收相邻框不重叠。 |
| B04／P1 | Diff标签关闭容器16×20，SVG实际**52×16** | 通用宽度缩放与旧专用16px高度规则交叉，关闭图形横向失真。统一关闭图形的尺寸角色，验证x/y缩放一致及真实Tab/鼠标命中。 |
| B05／P1 | Git历史搜索历史触发区24×53，SVG52×52；SVG右边比按钮右边多**34px** | 固定24px触发区没有跟随图形宽度，挤进输入文字。同期普通图标槽、下拉箭头、搜索输入内距一起验收。 |
| B06／P1 | Settings搜索框**209×25**、字体40；按既有`max(25,1em+8)`规则应至少48px，当前**少23px** | `mockup.css:4203`后置height25覆盖`:1719`自适应规则。已截图确认上下裁字。修复级联，检查所有设置搜索状态而非仅font字段。 |
| B07／P2 | Settings导航列229px，“Appearance & Behavior”换成110px两行；Appearance条目字形/文本向右侵入正文边界 | 当前.nav固定列上限230、tree-row overflow visible。长标题可换行但不能覆盖相邻面板；记录真实文本包围盒，不只检查行盒留在列内。 |
| B08／P2 | Settings面包屑分隔SVG原规则12×12，在UI40被全局图形覆盖成52×52；当前header高度91px | `mockup.css:4930`附近专用尺寸与文件末尾SVG规则重叠。需为面包屑明确紧凑图形角色；不能用普通16px图标族批量覆盖所有SVG。 |

### C. 二级页面、按钮和真实入口漂移

| 编号／优先级 | 当前测量／参考 | 整改条件 |
| --- | --- | --- |
| C01／P1 | **Settings静态稿1040px宽，真实从主界面打开930px宽，差110 CSS px**。相同约1641×901逻辑视口、UI12；高度分别766与766.29，后者含DPR边框取整 | 静态`.dialog-xl`、运行时`.dialog.wide.settings-dialog`使用不同宽度规则。文件字节一致不代表调用路径结构一致。所有二级入口都要检查运行时DOM、实际样式与设计场景是否为同一变体。 |
| C02／P1 | Windows Stash、Reset默认动作顺序参考为确认→Cancel；当前为Cancel→确认，位置交换 | `DialogWrapper.createActions():1234`为OK/Cancel，Windows不走Mac排序；GitStashDialog/GitResetDialog只改OK文案，没有重排。Settings与Define Remote已对齐，不代表所有模态都对齐。Clone/Push需各自最终动作组复核，不能仅套同类结论。 |
| C03／P2 | UI12 Commit按钮外宽**72px**，全局普通按钮外宽下限78px，少6；扣两侧3px透明边框后绘制宽66，比72px绘制下限少6 | `measureCommitPanels()`内联width与`.commit-actions`min-width0覆盖全局按钮规则。保证文字内距、最小绘制尺寸和实际外盒，不要将背景/透明边框计数混为一谈。 |
| C04／P2 | UI12 Cancel在Settings/Rollback为78px，Clone/Push/Stash为124px，Reset为126px；同文案分别多46/48px | 现实现为进行态英文预留宽度，这是“状态不跳动”的本地适配，不能冒称参考同尺寸。逐页比较实际参考并决定是否用参考的整组布局/进行态组织；不要盲目统一成78导致取消中裁字。 |
| C05／P2 | 当前原生参考Settings/Remotes/Define都是独立窗口；Augit为主WebView中的绝对定位层 | 形态已经存在差异：外框、阴影、窗口移动/适屏/所有者行为不能靠`.dialog`尺寸相等证明。架构限制下应实现等效体验，不要求换技术栈。本次只确认拓扑差异，不编造阴影模糊半径误差。 |
| C06／P1 | UI40文件信息页：正文宿主`[428,273,589,276]`，信息块`[620.72,313,560,566.45]`，**右越界163.72px、下越界330.45px**；标题18px、正文40px，标题反而小22px | 已看图并定向测量，见`info-metrics-large.json`。`.info-state`网格与`.info-block`最小内容宽度/居中约束需一起修正，提供正文滚动且保证系统打开动作可达；标题按角色缩放，不能只增大容器。 |

### D. 对照条件与验收方法本身

| 编号／优先级 | 已确认问题 | 整改要求 |
| --- | --- | --- |
| D01／P1 | 当前参考Editor字体JetBrains Mono13，Augit系统实际Cascadia Mono13；字形、字宽、baseline不能保证相等 | 已修“恢复参考外观”会说明缺字体，但缺失回退仍不是还原。正文、Diff、冲突、行号及终端逐角色登记字体；未取得同字体不要输出整体像素通过。不得复制分发PyCharm字体。 |
| D02／P1 | 历史`screen-clone.png`、`screen-rollback.png`实际是主界面，没有对应对话框；`screen-branches.png`实际上是Diff | 不能按文件名当页面证据。本轮已看图排除，保留排除拼图。旧文件不删除；建立截图内容验证及来源索引。 |
| D03／P1 | 原检查以内部字节一致、视觉稿与自身运行时、断言数量为主，未断言“子框在父框右侧”或“SVG在命中盒内”等关键几何 | 应将本报告数字转成独立于实现的基线，按父子相对位置、文字/图形包围盒、可见交集、画布/描边区分建验收。禁止把旧期望简单改成当前值后宣布还原。 |

## 4. 正常状态与错误候选的排除

- 原图的勾选／未勾选、分组选中背景不是同态，不能当配色错误。应先使两端同态，再采样。
- ManyIslandsDark 的主按钮语义链仍可解析到`#3871E1`；当前也是此色。旧注释写`#3574F0`不构成当前颜色错误，不能仅按旧注释修改。
- 55页同一字号/视口的浅深两套测量，在本次采样元素的x/y/w/h上没有超过0.05px的差异；这是**主题结构一致性通过**，不是主题颜色与PyCharm逐像素匹配通过。
- R01/R02设置Enter与保存代际、R03冲突范围、R05/R06 Markdown排版/模式位置等在10-12已有后续实施。本轮不将先前版本的代码行号继续当当前缺陷。
- 简单`scrollWidth > clientWidth`有误报：文本可合法省略、滚动容器允许内容超出。只有结合截图、可见交集及语义后才列B项；附件clipping数组是候选，不是自动判定的全部缺陷。

## 5. 尚缺配对像素证据的完整功能范围

| 范围 | 本次证据 | 尚不能填写精确差值的内容 |
| --- | --- | --- |
| 主框架/Project/Changes/Log | 原图＋当前前台＋真实外壳＋源码 | 同窗口、同面板位置、同选择/焦点的全窗配准，全部文件图标墨迹与长文本 |
| 分支/筛选/右键/查找/快速打开 | 当前分支弹层、页面截图、CSS/源码 | 所有弹层边缘/阴影、滚动到末尾、子菜单焦点、深色同态参考 |
| Settings | 真实运行时尺寸及静态漂移；上次及本次参考 | 四分类所有验证/禁用/保存/失败状态的成对截图，字号变化时窗口重排 |
| Clone/Push/Reset/Rollback/Stash/Worktree | 当前静态页面/源代码；Stash/Reset动作顺序链 | 每页真实参考正常/错误/等待/取消/嵌套状态的尺寸和文字包围盒 |
| Remotes/Define | 本次真实参考、已有当前运行时结构、全场景度量 | 同一URL/同一行数/同字体的双端精确窗口及表列宽差值；不能用样例2行对参考1行算差 |
| Diff | 当前真实DOM、单双栏静态、上游规则 | 同一补丁、语言、空白选项、滚动锚点的逐行配准，语法色、连接区、词级高亮 |
| 冲突 | 新精确范围实现及当前三栏视觉稿；不复用旧启发式缺陷 | 真实冲突仓库双方相同三侧内容、接受前后/撤销/IME/外部更新的成对像素与状态链 |
| 终端 | 静态终端表面、源码边界 | 真实shell输出及canvas/选区/光标/IME/主题变化；本轮没启动终端，不能声称已对照 |
| DPI/窗口 | 真实DPR1.75，浏览器DPR1；UI12/14/40几何 | 真实跨显示器WM_DPICHANGED、100/125/150%的两端同态画面、最大化/恢复/贴靠；不得用DPR启动参数代替跨屏 |
| Windows10 | 无实机 | 全部实机像素和交互验证 |

这里的“缺证据”不能作为结案理由，也不能将未知写成0px差异。两张截图无法提供其他页面的参考数值；本轮新增的220份Augit图同样不能替代缺失的PyCharm图。

## 6. 整改与验收顺序

1. A01–A05：先修层级/绘制框/边框/焦点，按用户原图和同字号真实外壳验收。用红绿叠图检查，保留整数像素边界，不通过缩放图片掩盖误差。
2. B01–B06：修容器和图形不同步、正文0px、按钮出屏、搜索裁字；测试必须断言实际可见交集和点击区，不只查样式属性。
3. 统一三个复选框实现族，再做浅深×未选/选中/半选/禁用/焦点；覆盖Changes、Amend、Clone、Stash、日期和筛选。
4. C01/C02：沿真实入口逐页验收对话框尺寸变体和Windows按钮顺序。将场景DOM和运行时DOM的差异显式列出，不依赖字节一致兜底。
5. 配齐第5节中每页的真实参考，再逐页补所有数值。没有同态参考或可追溯上游数值的页面不能盖“像素通过”。

像素验收表至少有：场景/状态、输入数据、主题、字体与实际回退、UI字号、DPR、窗口/面板尺寸、参考与实际图、相对矩形、纯色、文字/图标包围盒、差值、证据等级。形状的抗锯齿边缘应单列，不以纯色差判文字笔画失配。

## 7. 资源、失败与复验命令

浏览器扫描服务端口64613/58823/56216/54707/61790均已关闭。真实外壳PID14896/22496/9564均已停止；对应`Augit-pixel-*`隔离目录已删除，9347无监听。用户原有Augit与PyCharm没有关闭，用户终端没有启动。参考弹窗均Esc退出，没有Save/Fetch/Push等操作。

真实外壳Diff与UI40的CDP截图发生超时；已保留DOM测量，不将不存在的截图计为截图通过。首轮截图超时后改为逐份先落盘JSON，并把截图限制5秒，第二轮完成清理。UI40静态截图可观察布局问题，真实外壳同组DOM确认了Changes10px和按钮出屏；证据类型没有混写。

本轮无构建、无新增依赖、无全量业务测试。`verify-web-assets-mirror`验证15文件一致；报告与附件的文件格式和路径另做检查。保留截图、JSON、CSV、复验脚本和报告作为交付附件，不属于需要删除的临时文件。

```powershell
# 55页×浅深，正常字号；脚本 finally 关闭服务器和浏览器。
node artifacts/pixel-audit-2026-10-13/measure-scenes.cjs
# 55页×浅深，1024×640 / UI40。
node artifacts/pixel-audit-2026-10-13/measure-scenes.cjs --large
# 当前Debug产物应先通过网页镜像门禁；启动隔离真实外壳并自动清理。
powershell -NoProfile -File tools/audit/verify-web-assets-mirror.ps1 -SourceDirectory web -OutputDirectory src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/web
node artifacts/pixel-audit-2026-10-13/measure-shell.cjs
node artifacts/pixel-audit-2026-10-13/measure-shell.cjs --large
# 异常中断时，只停止上述脚本输出的本次PID及其后代，不按名称批量杀进程。
taskkill /PID <本次扫描Node或AUDIT_PID> /T /F
```

## 8. 54页实际尺寸逐页目录（原55页；三栏冲突页经批准移出范围）

下表是当前Augit的**实际测量值**，单位CSS px、UI12、1180×760、DPR1。主表面优先取dialog/popover，其余列为主框架。前景模态的尺寸不含阴影。所有页面均有浅深和UI40附件；没有成对PyCharm参考的行不填写差值。完整控件数值见JSON和CSV。


<!-- 自动生成逐页目录 -->
| 页面 | 前景尺寸/主框架 | 文件/树/提交行高 | 已确认关联项 | 精确参考差值 |
| --- | --- | --- | --- | --- |
| [blame](../artifacts/pixel-audit-2026-10-13/blame-light.png) | 主框架1180×760 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [branches](../artifacts/pixel-audit-2026-10-13/branches-light.png) | 338.00×256.00 | 文件24.00 / 树24.00 / 提交26.00 | A族；B03/B05 | 非整页配对，不填0 |
| [changes-context-menu](../artifacts/pixel-audit-2026-10-13/changes-context-menu-light.png) | 200.00×190.00 | 文件24.00 / 树24.00 / 提交26.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [clone](../artifacts/pixel-audit-2026-10-13/clone-light.png) | 930.00×289.00 | 树24.00 | A07/A08、C04 | 非整页配对，不填0 |
| [commit-changes](../artifacts/pixel-audit-2026-10-13/commit-changes-light.png) | 主框架1180×760 | 文件24.00 | A01–A12、B01–B04、C03 | 非整页配对，不填0 |
| [commit-diff](../artifacts/pixel-audit-2026-10-13/commit-diff-light.png) | 主框架1180×760 | 文件24.00 / 树24.00 / 提交26.00 | A01–A12、B01–B04、C03 | 非整页配对，不填0 |
| [commit-empty](../artifacts/pixel-audit-2026-10-13/commit-empty-light.png) | 主框架1180×760 | — | A01–A12、B01–B04、C03 | 非整页配对，不填0 |
| [conflict-list](../artifacts/pixel-audit-2026-10-13/conflict-list-light.png) | 930.00×290.00 | 文件24.00 | 背景Changes继承A族；三栏本体需配对 | 非整页配对，不填0 |
| [diff-boundary](../artifacts/pixel-audit-2026-10-13/diff-boundary-light.png) | 主框架1180×760 | 文件24.00 / 树24.00 / 提交26.00 | 背景Changes继承A族；B01/B04 | 非整页配对，不填0 |
| [diff-loading](../artifacts/pixel-audit-2026-10-13/diff-loading-light.png) | 主框架1180×760 | 文件24.00 / 树24.00 / 提交26.00 | 背景Changes继承A族；B01/B04 | 非整页配对，不填0 |
| [diff-status](../artifacts/pixel-audit-2026-10-13/diff-status-light.png) | 主框架1180×760 | 文件24.00 / 树24.00 / 提交26.00 | 背景Changes继承A族；B01/B04 | 非整页配对，不填0 |
| [file-history](../artifacts/pixel-audit-2026-10-13/file-history-light.png) | 主框架1180×760 | 树24.00 | B05；筛选框继承A族 | 非整页配对，不填0 |
| [file-limit](../artifacts/pixel-audit-2026-10-13/file-limit-light.png) | 主框架1180×760 | 树24.00 | C06 | 非整页配对，不填0 |
| [git-compare-empty](../artifacts/pixel-audit-2026-10-13/git-compare-empty-light.png) | 主框架1180×760 | 树24.00 / 提交26.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [git-compare](../artifacts/pixel-audit-2026-10-13/git-compare-light.png) | 主框架1180×760 | 树24.00 / 提交26.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [git-history-empty](../artifacts/pixel-audit-2026-10-13/git-history-empty-light.png) | 主框架1180×760 | 树24.00 | B05；筛选框继承A族 | 非整页配对，不填0 |
| [git-history-graph](../artifacts/pixel-audit-2026-10-13/git-history-graph-light.png) | 主框架1180×760 | 树24.00 / 提交26.00 | B05；筛选框继承A族 | 非整页配对，不填0 |
| [git-history-menu](../artifacts/pixel-audit-2026-10-13/git-history-menu-light.png) | 217.48×218.00 | 树24.00 / 提交26.00 | B05；筛选框继承A族 | 非整页配对，不填0 |
| [git-history](../artifacts/pixel-audit-2026-10-13/git-history-light.png) | 主框架1180×760 | 树24.00 / 提交26.00 | B05；筛选框继承A族 | 非整页配对，不填0 |
| [git-unavailable](../artifacts/pixel-audit-2026-10-13/git-unavailable-light.png) | 主框架1180×760 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [go-to-line](../artifacts/pixel-audit-2026-10-13/go-to-line-light.png) | 400.00×164.00 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [history-diff-cancelled](../artifacts/pixel-audit-2026-10-13/history-diff-cancelled-light.png) | 主框架1180×760 | 树24.00 / 提交26.00 | 背景Changes继承A族；B01/B04 | 非整页配对，不填0 |
| [history-diff-failure](../artifacts/pixel-audit-2026-10-13/history-diff-failure-light.png) | 主框架1180×760 | 树24.00 / 提交26.00 | 背景Changes继承A族；B01/B04 | 非整页配对，不填0 |
| [history-diff-loading](../artifacts/pixel-audit-2026-10-13/history-diff-loading-light.png) | 主框架1180×760 | 树24.00 / 提交26.00 | 背景Changes继承A族；B01/B04 | 非整页配对，不填0 |
| [image-error](../artifacts/pixel-audit-2026-10-13/image-error-light.png) | 主框架1180×760 | 树24.00 | D01；信息态C06 | 非整页配对，不填0 |
| [image-preview](../artifacts/pixel-audit-2026-10-13/image-preview-light.png) | 主框架1180×760 | 树24.00 | D01；信息态C06 | 非整页配对，不填0 |
| [json-preview](../artifacts/pixel-audit-2026-10-13/json-preview-light.png) | 主框架1180×760 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [main-project](../artifacts/pixel-audit-2026-10-13/main-project-light.png) | 主框架1180×760 | 树24.00 / 提交26.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [markdown-preview](../artifacts/pixel-audit-2026-10-13/markdown-preview-light.png) | 主框架1180×760 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [operation-progress](../artifacts/pixel-audit-2026-10-13/operation-progress-light.png) | 主框架1180×760 | 文件24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [operation-result](../artifacts/pixel-audit-2026-10-13/operation-result-light.png) | 主框架1180×760 | 文件24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [project-context-menu](../artifacts/pixel-audit-2026-10-13/project-context-menu-light.png) | 203.09×187.00 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [push-no-remote](../artifacts/pixel-audit-2026-10-13/push-no-remote-light.png) | 930.00×524.00 | 文件24.00 / 树27.00 | C04；背景Changes继承A族 | 非整页配对，不填0 |
| [push](../artifacts/pixel-audit-2026-10-13/push-light.png) | 930.00×494.00 | 文件24.00 / 树27.00 | C04；背景Changes继承A族 | 非整页配对，不填0 |
| [quick-open-empty](../artifacts/pixel-audit-2026-10-13/quick-open-empty-light.png) | 主框架1180×760 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [quick-open](../artifacts/pixel-audit-2026-10-13/quick-open-light.png) | 主框架1180×760 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [remote](../artifacts/pixel-audit-2026-10-13/remote-light.png) | 372.00×272.00 | 树24.00 / 提交26.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [repository-init](../artifacts/pixel-audit-2026-10-13/repository-init-light.png) | 620.00×290.00 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [repository-search](../artifacts/pixel-audit-2026-10-13/repository-search-light.png) | 主框架1180×760 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [reset](../artifacts/pixel-audit-2026-10-13/reset-light.png) | 620.00×288.00 | 树24.00 / 提交26.00 | C02/C04 | 非整页配对，不填0 |
| [rollback](../artifacts/pixel-audit-2026-10-13/rollback-light.png) | 930.00×435.00 | 文件24.00 | C04 | 非整页配对，不填0 |
| [search-limited](../artifacts/pixel-audit-2026-10-13/search-limited-light.png) | 620.00×290.00 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [settings-dirty](../artifacts/pixel-audit-2026-10-13/settings-dirty-light.png) | 1040.00×641.00 | 树24.00 | B06–B08、C01 | 非整页配对，不填0 |
| [settings-save-failure](../artifacts/pixel-audit-2026-10-13/settings-save-failure-light.png) | 1040.00×641.00 | 树24.00 | B06–B08、C01 | 非整页配对，不填0 |
| [settings](../artifacts/pixel-audit-2026-10-13/settings-light.png) | 1040.00×641.00 | 树24.00 | B06–B08、C01 | 非整页配对，不填0 |
| [smart-checkout](../artifacts/pixel-audit-2026-10-13/smart-checkout-light.png) | 620.00×290.00 | 文件24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [stash-drop-confirm](../artifacts/pixel-audit-2026-10-13/stash-drop-confirm-light.png) | 620.00×274.00 | 文件24.00 / 树24.00 / 提交26.00 | A07/A08、C02/C04 | 非整页配对，不填0 |
| [stash-manager](../artifacts/pixel-audit-2026-10-13/stash-manager-light.png) | 930.00×385.00 | 树24.00 / 提交26.00 | A07/A08、C02/C04 | 非整页配对，不填0 |
| [stash](../artifacts/pixel-audit-2026-10-13/stash-light.png) | 620.00×331.00 | 文件24.00 / 树24.00 / 提交26.00 | A07/A08、C02/C04 | 非整页配对，不填0 |
| [terminal-close](../artifacts/pixel-audit-2026-10-13/terminal-close-light.png) | 620.00×290.00 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [terminal](../artifacts/pixel-audit-2026-10-13/terminal-light.png) | 主框架1180×760 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [text-viewer](../artifacts/pixel-audit-2026-10-13/text-viewer-light.png) | 主框架1180×760 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [workspace-open](../artifacts/pixel-audit-2026-10-13/workspace-open-light.png) | 930.00×466.00 | 树24.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |
| [worktrees](../artifacts/pixel-audit-2026-10-13/worktrees-light.png) | 930.00×365.00 | 树24.00 / 提交26.00 | 见第5节，未完成双端配对 | 非整页配对，不填0 |

CSV：[所有采样控件度量](../artifacts/pixel-audit-2026-10-13/all-metrics.csv)。离线查看器：[按页面、主题、字号查看截图及数值](../artifacts/pixel-audit-2026-10-13/index.html)。

## 9. 本轮源码快照 SHA256

- `mockup.js`：`EF5A0707B2624DA86CAAFE79779E18700EC72CBDE4891655823B9C715FB17DBE`
- `mockup.css`：`7AB693EADE9EC44C0891B8460C472B1E0CF70811EEAE46EFB37AB9CF2058AEDF`
- `live-data.js`：`CE0214117ACFF9310793B323053CA0BFA1BE7A4CF40C5EB7EC0D9C943C4826C0`
- `bridge.js`：`46FA7AB65DAD37CB5BB19E5B41598EDF0E20D07FA51CC7B1C433C572BA51220E`
