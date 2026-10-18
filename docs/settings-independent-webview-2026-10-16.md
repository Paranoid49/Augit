# 设置独立 WebView 整改与验收边界（2026-10-16）

2026-10-16关闭阴影接续：整帧比较揭示旧包络漏掉92710像素中间态；设置现先撤下合成画面再激活主窗。修后252帧未再出现该关闭阴影变化，Debug/Release回归及Shell224项通过；首次仍1540ms，不能宣布全场景无闪。原图、时序、限制和清理见[本批记录](settings-full-frame-cycle-2026-10-16.md)。

## 最新接续：原生主题同步与创建期单例

2026-10-16接续修复已进入Debug和Release：主题预览此前只改变网页，WebView默认底色以及新开设置的URL仍可能继承启动主题。本批在预览、成功提交和关闭恢复时同步原生表面；新子窗使用当前生效主题。异步保存可能在线程池续接，COM底色更新必须投递原生消息交由UI线程执行。设计系统的模态表格也补齐已授权的Settings独立控制器例外。

修改文件：ShellWindow.Settings.cs、ShellWindow.cs、ShellTheme.cs、IndependentSettingsTests.cs、independent-settings.cjs、settings-hidden-startup.test.cjs以及本次规范和记录。四个网页文件未改，MD5仍为mockup.js=5148D9CDA4D61A8F11D0F125407918B8、mockup.css=D5342D22C245695A57F93BC90A1DC7ED、live-data.js=3216B5CDC0FE86166B87389B7D6B3954、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

证据均在artifacts/pixel-followup-2026-10-16：

| 目录 | 结论 |
| --- | --- |
| settings-independent-early-compose、settings-early-compose-dark | 提前显示隐藏合成的试验未保留。浅色较快，但深色239帧中最大3996688像素越界，仍有秒级慢开；源码已精确恢复并强制非增量重建，不能使用该试验DLL。 |
| settings-surface-theme-dark | 首次主题补丁功能回归失败：直接在异步保存续接访问COM导致保存错误；已改为UI消息。此目录录像对应修正前构建，不能充当最终构建录像。278帧全帧诊断最大6838像素越界、135帧非零、最大时间间隔56ms；fullPresentationVerified=false。 |
| settings-surface-theme-final | 修正后Debug深色八组通过，网页错误0；10次原生创建至呈现334–590ms；取消／OK／Esc／标题关闭84／54／57／71ms。新增原生底色、取消恢复和重开初始主题断言通过。 |
| settings-surface-theme-release | Release功能与原生Esc观察批次；工具第一次状态捕获显示其他前景应用，重新激活后看到唯一设置子窗。Esc后的即时读数仍active=true，随后驱动记录关闭。没有可靠输入到关闭时序，native-close条目仅为最终状态断言，不计独立原生交互验收。 |
| settings-surface-theme-release-final | Release随包八组通过，网页错误0；10次创建至呈现332–619ms；四条关闭91／176／51／76ms。 |
| settings-surface-singleflight | 最后Debug创建期单例回归八组通过；每次首次点击后立即并发八次开窗请求，所有应答持有同一非零HWND，显示后再复验八次。10次创建至呈现336–625ms；四条关闭92／173／70／256ms。 |

本批没有用最快样本覆盖历史秒级慢开，也没有把三状态颜色包络或功能断言当作零闪证明。最终主题补丁尚缺重新录制的全部开关帧及可信原生拖动／点击专项；整体像素目标继续未完成。

定向验证：Shell IndependentSettingsTests 15项（54ms）；JS隐藏启动、重复入口、字体回读共7项；JS语法、C#定向格式、文档声明与git diff --check通过。Debug强制构建6.13秒、Release5.95秒，均0警告0错误。Debug／Release各24文件镜像、9共享资产校验通过。未重跑全量测试，旧全量快照仍过期。

跳转行补采shadow-goto-current-complete为584×252物理像素、168DPI，背景稳定；顶部鼠标光晕污染使外环无法完整比较，七个区域零差异不能替代完整通过，ring.json保留失败。未筛掉污染像素，也未宣称内侧圆角或完整阴影验收通过。

本批启停仍使用下文正常入口和驱动finally清理。PID22728、20076、26192、23676、26224、22764、1032、28288、16092、26040均已退出；相关八个端口无监听，八个隔离配置/profile目录均不存在。本次删除三个握手标记的命令被自动审批以blocked by policy拒绝，未重试：settings-early-compose-dark/cycle-continue、settings-surface-theme-dark/cycle-continue、settings-surface-theme-release/continue，均位于本日证据目录下。录像、PNG和JSON按证据保留；此前被拒绝的目录及标记未触碰。未暂存或提交混合修改。

用户已明确授权“允许独立设置 WebView，并同步规范”。本批按该授权修改正式设置入口；不改变其它模态的技术路线，不覆盖既有混合修改，不暂存或提交。上一批属于有实际代码与取证的进展；整体像素目标仍保持未完成。

## 实施范围

`ShellWindow.Settings.cs` 持有唯一设置子窗：创建控制器前登记身份，重复入口复用该窗。主窗始终保留自己的业务 WebView，不再为设置请求 modal-cover、modal-refresh 或重挂主控制器。子窗复用环境及设置服务，只允许设置读写、字体枚举和 Git 检测；不复制标签、终端、工作区和监听。主窗未加载设置 DOM，子窗只挂载共享设置表单。

页面读取设置、完成字体和布局后，先由宿主启用控制器并用 DWM cloak 暂不展示这个真实窗口，随后通过一次CapturePreviewAsync将设置自身渲染结果写入Stream.Null并丢弃，原生消息队列执行显示和激活、DwmFlush后解除cloak。没有展示截图、复制业务DOM或额外遮盖窗口。初始化失败或20秒超时关闭子窗，恢复所有者；仅在展示时禁用主窗，关闭时先恢复所有者再销毁子窗，避免中间激活其它应用。

保存沿现有服务；宿主的设置写入忙状态跨旧页关闭／新页重开保留。Apply留窗；取消不提交新草稿，主题预览在关闭时回读持久值；迟到应答不再访问旧控制器。保存失败保留表单，恢复隔离文件后可重试。不是全系统任意设置写入的事务化改造，也未宣称消除既有跨进程设置文件竞态。

日常启动的源网页、随包网页和 `--scene settings` 走同一套独立窗口代码。审计仅复制当前设置并隔离 WebView profile，增加 CDP 观察端口；没有强制 DPI、字体或空白会话。它仍不是未经隔离的用户配置写入验收。

## 本批证据与失败保留

证据根为 `artifacts/pixel-followup-2026-10-16/`：

| 目录 | 实际结果与边界 |
| --- | --- |
| settings-independent-desktop | 初版稳定开窗：2511×1592完整主窗口区域，DDA／FFV1共634帧，RGB容差2，排除0像素，变化0；源时间最大间隔55ms，开启静止重复帧。只说明该稳定阶段，不能代表首次开关。 |
| settings-independent-cycle | 第一份完整录像尝试等待页面就绪超时，失败保留，不计通过。 |
| settings-independent-cycle-diagnostic | 两次开关实际完成，但关闭时前景短暂成为Edge；严格门禁拒绝。 |
| settings-independent-cycle-fixed | 修复恢复所有者顺序后无前景干扰；抽帧仍见白色客户区，证明状态断言不能代表真实绘制。12秒录制未覆盖第二次关闭，不计完整开关验收。 |
| settings-independent-present | 最新首次呈现修复：2880×1716整主窗区域DDA／FFV1，5.03秒，包含一次打开、四次主窗文字更新和关闭；前景仅为本实例登记窗口。2fps总览与打开段10fps抽帧均已查看，未见前版白窗。尚未完成全部原始帧的开关阶段定量门禁，不宣称全场景零闪帧。 |
| settings-independent-final-release | 最新Release随包与settings场景共用入口；9组功能通过，零网页错误。11次记录的宿主就绪通知216–457ms（包含一次复用现有窗），CDP页发现481–714ms；Cancel／OK／Esc／标题关闭66／160／54／91ms。 |
| settings-independent-delivery | 最新Debug源网页路径，实际仓库和复制的用户配置；另记实例父链内存与关闭后资源，具体数值见该目录原始JSON。 |

功能回归覆盖八次重复入口保持同一HWND、主窗零设置DOM／子窗一份表单、旧模态宿主未激活、四分类、四关闭路径、主题预览取消、真实持久化失败后重试、延迟保存应答后取消重开、Apply保存后重开。所有已通过功能批次没有设置路径的截图／重挂请求，也没有子窗工作区／终端请求。

本批初版Shell模块220项通过；首次呈现修补后定向白名单及隐藏启动顺序测试另验，不重复跑全量。JS入口／关闭／归属／录屏资格24项通过，最后补充绘制握手顺序1项。Debug和Release构建0警告0错误；两套24文件网页镜像与共享9资产一致。格式、语法及最终清理由收尾记录补充，不将历史全量快照算作本批通过。

## 哈希、剩余项与启停

进入本批前四网页MD5：mockup.js=`FF0DA3AD5B3300D01716EFBE618AA590`，mockup.css=`FA339DDEBB91AEE32BFCB2600701B046`，live-data.js=`38A6F63C55532F4D67A210E5ED933F88`，bridge.js=`F297BF8769E249CA0D804F4F4EEEF5A9`。当前实测：mockup.js=`5148D9CDA4D61A8F11D0F125407918B8`，mockup.css=`D5342D22C245695A57F93BC90A1DC7ED`，live-data.js=`41F328DC352FB96DFCD35C4A8CD91ED4`，bridge.js不变。mockup.js同时保留此前冲突gutter修正，不把该差异归为设置。

冲突gutter最终三状态原图已继续比较：完整差异74171／79787／87290，标题分隔线差异均0，整窗仍未通过。证据 `conflict-gutter-inside-final/clean-comparison.json`；未以局部通过替代逐行与整窗目标。

仍需：设置全部原始开关帧定量、深色完整过程、真实原生鼠标键盘与拖动专项、独立renderer成本的前后配对；以及55页和全模态双端同态截图、其它模态刷新、终端／冲突完整配对及系统阴影。Windows10按用户要求不做本次实机验收；跨屏DPI保留未验证。历史被策略拒绝删除的Augit-conflict-origin目录没有重试删除。

```powershell
# 日常入口；构建失败不会运行旧程序。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 停止：正常关闭窗口；异常时只用启动器打印的本次PID。
taskkill /PID <本次PID> /T /F
# 复验（驱动finally负责本实例、端口和临时配置清理）：
node tools/audit/independent-settings.cjs <Playwright模块路径> <新证据目录> --source-web --memory
```

## 收尾实测

最新Debug源网页8组通过，零网页错误；11次宿主就绪通知219–397ms，四条关闭67／42／63／77ms。最新Shell定向11项通过（54ms），最后8项JS定向通过，C#格式检查通过。脚本与产品JS语法、文档声明及git diff --check通过；声明检查明确旧全量快照过期，不把其2373断言计入本批。

实例父链采样：打开前7进程，Working Set 977.0MiB／Private 747.1MiB；设置打开时8进程，1252.2／955.1MiB；关闭500ms后7进程，1164.5／903.8MiB。关闭后进程数回落，但缓存和总内存未回到前值；没有旧架构同态基线，不宣称内存减少或全部即时回收。

本次接续实例PID21820、28444、25380、27344、6084、7052、9504、23216、2604均已由对应驱动finally清理；端口59488、50269、59402、61228、58577、59301、61336、49689、50647不再监听，9个独立配置/profile目录不存在。录屏、前景采样和解码会话均已终止；证据文件作为验收资料保留，临时握手标记删除。历史受策略拒绝的目录保持原状。

## 逐帧接续纠正（2026-10-16晚间）

此前2fps／10fps抽帧不足以验证开关：settings-independent-present全部278帧检查发现关闭缩小淡出；settings-independent-no-transition全部381帧又发现第275帧白色设置空窗。失败原图与录像保留，不能用旧摘要代替本次结论。DWM属性3只表示设置禁用过渡的请求已被接受；不支持的读取尝试已删除，诊断字段不伪称OS读回。

本批禁用设置自身过渡，避免重复尺寸赋值；首次显示和激活在解除cloak之前完成。隐藏动画帧改为宿主一次渲染同步，结果直接丢弃；最终显示从WebView回调移到原生窗口消息队列。没有修改系统动画设置，也没有为审计另设产品界面。

中间候选settings-independent-render-sync仍录到一帧整区域白色，不能归为通过。最终settings-independent-queued-present为2880×1716、302原始帧、5.788秒，最大采样间隔41ms；逐帧RGB包络未再出现前述大面积白窗，最大越界6876像素，131帧非零，主要候选含提示框与焦点变化。三状态包络可能放过错误混合，因此fullPresentationVerified仍为false，不称全场景零闪。原始视频SHA256为a6aa9310f289fec34ee391e92045709289e1304dbd1e7b8376f1074c04c5fe2d。

宿主增加创建到准备／渲染／呈现的单调时钟诊断，区分CDP轮询和产品时延。最终Debug八组功能通过、网页错误0，关闭92／177／115／113ms；创建至呈现512–2777ms，秒级慢开仍存在，不能选择最快样本宣称全部流畅。重复入口八次保留同一HWND、保存失败重试、取消延迟应答重开、Apply均通过。

新增完整帧诊断及3个反例测试；JS定向7项、Shell相关16项通过。Debug与Release强制构建0警告0错误。未修改mockup共享文件；live-data.js从41F328DC352FB96DFCD35C4A8CD91ED4变为3216B5CDC0FE86166B87389B7D6B3954，其余三文件哈希不变。

本次仍不完成整体目标：55页全模态配对、原生拖动专项、独立设置的新路径阴影量化、终端／冲突完整配对、跨屏DPI均未验收；Win10按用户决定排除。本批未回滚、暂存或提交混合修改。

### 最终发布与清理复核

settings-independent-queued-release八组通过，网页错误0，四条关闭88／177／69／122ms；10次宿主创建至呈现502–2819ms，慢开未解决。原生辅助功能树确认一个Settings子窗、主窗禁用并保持可见；原生点击工具两次报cached app state控件不可用，未操作成功，不计原生点击验收。

Debug／Release各24文件网页镜像一致，9共享资产一致；C#定向格式、JS语法、文档声明检查及git diff --check通过。声明检查报告旧全量快照已过期，未引用其断言数作本批成绩。

本次PID27956、17808、4632、26264、7576、9732、28656、11388由驱动finally清理；复核无Augit、ffmpeg、python进程，端口50638、57328、55948、53905、56919、57425、58778、59103均无监听，临时Augit-independent-settings-*目录无残留。录像握手标记删除命令被自动审批以blocked by policy拒绝，未重试；这些小标记留在上述证据目录。历史被拒绝的冲突目录未触碰。所有录像、PNG及JSON按验收证据保留。
