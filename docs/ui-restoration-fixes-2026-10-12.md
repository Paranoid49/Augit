# PyCharm复原整改实施记录（2026-10-12）

R01–R08按“设置状态机 → 冲突范围 → 字体基线 → Markdown与图标 → 当前归类”的顺序实施。本文记录具体修复和证据范围，不判定100%复原。V01–V10是验收矩阵，未覆盖的状态、真实跨屏、同字体栅格及Windows 10继续保留未验证。

## 1. 工作区与参考

- Augit分支`dsh`，开始HEAD为`3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`；开始时已有120个跟踪文件改动及未跟踪文件。原有改动全部保留；混合工作区未自动提交，不能把已有改动纳入本轮提交。
- 权威源码为`D:/github/intellij-community@576e32820af82f97529d70f9269726803a27c016`。设置Enter对照`DialogWrapper.java`的Windows焦点按钮hook；模式位置对照`TextEditorWithPreview.kt`的`isShowActionsInTabs`；图形缩放对照`LafManagerImpl.kt`、`JBUIScale.discreteScale`与组件独立基准。
- 已打开PyCharm的只读参考：Microsoft YaHei UI／12，JetBrains Mono／13.0，行距1.2，连字关闭；Markdown模式位于标签区右侧。设置观察后取消，未修改PyCharm配置。
- Markdown排版只读核对当前安装包`D:/JetBrains/PyCharm 2026.2.1/plugins/markdown/lib/intellij.markdown.jar`内的`org/intellij/plugins/markdown/extensions/common/baseStyles/default.css`，未复制资源或字体。
- 本机Windows 11 x64，真实WebView2的DPR为1.75。系统字体枚举没有JetBrains Mono，有Cascadia Mono与Consolas；PyCharm自带字体不能冒充系统可用字体。没有Windows 10实机或真实跨显示器验收条件。

## 2. R01–R08实施与验收

| 项目 | 本轮行为 | 验收证据与边界 |
| --- | --- | --- |
| R01 | Enter先执行焦点按钮；Cancel零写入，Apply留窗，普通字段落到OK；选择框、多行输入、组词、229键码、修饰键和长按保留各自语义。 | 浅深设置专项12项；真实WebView2验证Cancel与Apply。实际Windows IME组词仍待V02／V03实机链验证。 |
| R02 | 草稿／保存中／失败／已保存按窗口会话归属；全局保存单飞，保存中冻结字段和提交；取消可关窗。旧成功和失败不关闭新窗、不清新草稿、不写新错误。 | 延迟成功、失败、重复提交、取消重开、Esc、失败重试均有回归；等待宿主持久化完成，不用客户端超时提前解锁。 |
| R03 | 宿主返回块身份和零基半开三侧行范围；完整侧投影经本机Git差异脚本映射，前端移除唯一文本／局部上下文猜测。结果DOM保留身份用于接受、撤销、导航和联动滚动。空侧始终零宽；映射按顺序单次扫描。外部重载重读索引三侧与操作状态。 | Core冲突23项、真实Git冲突服务16项、Shell桥接4项、浏览器冲突51项；覆盖重复正文、首行空侧、自动合并变化、混合换行、非顺序接受及Undo／Redo。接受首块后重载，剩余块来源范围及身份保持。未把任意外部手改标记、真实IME和大量短行压力写成全部验收。 |
| R04 | 恢复快照改为JetBrains Mono13，先查本机字体；缺失依次回退Cascadia Mono／Consolas／Courier New并说明。只改草稿，Cancel不持久化。产品默认与用户已保存偏好不被覆盖。 | 字体存在／缺失浏览器回归；真实WebView2显示“JetBrains Mono is not installed. Using Cascadia Mono.”。缺字体情况下不能宣称同字体行高和墨迹已校准。 |
| R05 | Markdown正文、列表、表格、引用跟随UI字号；h1–h6倍率2.2／1.8／1.3／1／1／1，正文行距1.6、左右2em，代码独立等宽字号和1.45行距；标题、列表、引用及代码留白按参考专属CSS校准。 | 浅深UI12／20／40相对度量回归；真实WebView2正文12、h1为26.4。不增加独立预览字号或编辑功能。 |
| R06 | 三模式动作移到编辑标签区右侧、Tab Options之前；移除多余正文工具栏，保留Tooltip、当前状态和键盘焦点。 | 三模式、阅读位置、独立标签区刷新及Ctrl+F回归；综合发现预览Ctrl+F还查询旧正文按钮，current-find.js现改走标签Raw入口，浅深12／20／40及真实WebView2再验证；符号链接提示和正文查找仍占所属正文行。 |
| R07 | 普通16、紧凑13、工具轨／标题20图形与各自槽位按UI用户缩放；系统窗口控制和框架关闭保持16，复选框独立，提交图仍由实际行高决定。搜索、提交、查找和Diff同步容纳图形，限宽工具条局部横向滚动且聚焦完整露出命中区，不推动整个正文；标题省略、工具轨纵向滚动保证入口可达。三栏英文来源标题按字宽分行，大字号导航按组换行，接受宽度取三种动作最大值；只读JSON错误与链接提示在空间不足时各自滚动，保留正文。 | 复原专项32项：包含浅深UI13／32／40，搜索1180／320宽下的完整命中区、无重叠和无溢出。原891项布局专项及20场景字号检查提供几何证据；未替代PyCharm同字号像素实测或真实跨屏。 |
| R08 | 归类总表、历史待办和设计系统开头增加当前日期／范围；保留旧“清零／完成”历史，但当前结论指向本清单和V矩阵。 | 历史记录不删除；当前规范更新模式位置、字体恢复、图标缩放与桥接契约，未把待验证改成已修缺陷或通过。 |

冲突范围新增本机Git查询，只有侧投影与索引内容不同时运行；关闭外部diff／textconv，使用零上下文Myers编辑脚本。差异输出上限为两侧10MB正文及逐行前缀预算（40MB加64KiB），系统临时目录在`finally`删除，失败和截断明确返回失败，不回退前端猜测。此路径未新增第三方依赖。

## 3. 修改范围

- 设置：`web/src/live-data.js`，新增`tools/audit/live-settings-state.cjs`。
- 冲突：`GitConflictModels.cs`、新增`GitConflictRangeMap.cs`、`GitConflictService.cs`、`ShellBridge.cs`，Core／Infrastructure／Shell对应测试和`live-goal-conflict.cjs`。
- 字体、Markdown与图标：共享`mockup.js`／`mockup.css`及current-find.js两侧镜像；新增`live-restoration-fixes.cjs`，更新相关布局、字号、Markdown和综合测试中的旧固定尺寸／旧按钮位置断言。
- 真实外壳：新增`run-restoration-fixes-shell.cjs`和`live-restoration-fixes-shell.cjs`，独立设置及profile，不写用户配置。等待首个导航就绪再验收；停止命令失败时仍清理本实例临时目录。
- 综合C#验证发现旧`SearchStatusTextTests`还要求宿主中文文案直接出现在英文视觉稿中，现改为验证中文→英文映射与共享视觉稿一致，未改变搜索产品逻辑。
- 文档：设计系统、UX、架构、07／08／21／22／24模块、权威行为索引、归类总表、历史待办及原整改清单。

## 4. 分批验证

公共冲突范围契约和全局CSS令牌变化触发本次模块完整验证；小批次先跑设置、冲突与复原专项，不在每次微调后重跑全量。

| 命令或范围 | 结果 | 输出耗时 |
| --- | --- | --- |
| `dotnet test tests/Augit.Core.Tests --no-restore` | 95通过 | 测试1秒，含构建命令5.68秒 |
| `dotnet test tests/Augit.Infrastructure.Tests --no-build --no-restore` | 209通过 | 测试35秒 |
| `dotnet test tests/Augit.Shell.Tests --no-build --no-restore` | 165通过 | 测试16秒 |
| `dotnet test … --filter FullyQualifiedName~GitConflict` | Core23、Infrastructure16、Shell桥接4通过 | Core972ms，Infrastructure7秒；桥接先前批次未单记耗时 |
| `dotnet build Augit.slnx --no-restore` | 0警告0错误 | MSBuild3.25秒 |
| `dotnet format Augit.slnx --no-restore --verify-no-changes --include …` | 本轮C#文件格式通过 | 后续C#最终定向格式检查通过，未单记总耗时 |
| `live-shell.spec.cjs … --settings-state-only` | 12通过 | 先前批次未单记耗时 |
| `live-shell.spec.cjs … --goal-conflict-only` | 51通过；10485078字节混合换行正文编辑／保存 | 单次尾部编辑167ms，编辑及保存2109ms |
| `live-shell.spec.cjs … --restoration-fixes-only` | 32通过 | 未单记耗时 |
| `live-shell.spec.cjs … --layout-only` | 891通过，DPR1／1.25／1.5 | 先前批次未单记耗时 |
| `run-restoration-fixes-shell.cjs …` | 真实WebView2通过，截图及JSON保留 | 本轮命令4.63秒 |

综合网页与相关静态检查器结果见本节末的最终批次记录。早期综合运行遇到旧模式选择器、旧固定16px断言和换行后仍要求标题高度等于单行高度；先核对权威与新几何专项，再订正旧期望，失败输出不计入通过。综合测试提供`--collect-failures`收集同批残差，失败项不增加通过数，最终仍返回非零，以避免频繁从头跑全量。


最终相关静态检查：字号20场景（5.33秒），Markdown6组三模式及2组拖动（6.99秒），文档工具栏60组（11.32秒），Diff72组（47.88秒），历史42组（8.95秒），Changes→Diff连续交互与悬停6组（7.34秒），查找48个布局状态及连续输入／键盘／5组组词（19.51秒），三栏72组（未单记总耗时）。查找逐个聚焦验证局部滚动后的完整命中区，Diff逐个聚焦验证比较外框不横移。大字号三栏和查找截图已人工查看。

旧静态检查器曾遇到参数缺证据目录、英文动作名称／新主题值、旧1.7行距／固定图形和移除后的Markdown工具栏假设；补齐参数后，按当前权威规则更新断言，裁切等真实问题修正产品代码。综合脚本旧text-viewer场景依赖样例正文，而当前实时启动保留空编辑器；现明确打开真实夹具文件并用快捷键建立查找状态。失败诊断不计通过。


设置状态机专项12项（13.97秒）、设置／Diff菜单／日历10项（19.28秒）、真实路径Remotes41项（11.60秒）、恢复路径23项（4.36秒）均通过；最新复原32项再次通过，其中补验预览Ctrl+F。C#Infrastructure最终209项通过（35秒），冲突服务16项（6秒），包含外部索引阶段重读和全same重复行。

**综合live-shell尚未完整通过**：最后一轮运行到Markdown预览查找时发现旧正文按钮查询，已修正并通过新专项及真实WebView2；同轮Tab断言把禁用导航也当成必经点，现按启用项循环。前几次全量被旧模式位置／固定尺寸／缺少真实文档夹具提前停止；没有把局部成功算成全量通过。为遵循分批验证纪律，后续仅重跑直接失败路径和相关模块；完整综合剩余用例继续登记未验收。

## 5. 当前验证缺口矩阵

| 项目 | 本轮已有证据 | 仍待验收 |
| --- | --- | --- |
| V01 Git动作 | Infrastructure／Shell完整自动化、浏览器综合中的已有Git动作桩 | 全部真实hook／认证／锁／取消组合，以及merge／rebase／cherry-pick／revert逐项状态矩阵 |
| V02 冲突编辑 | 真实Git加载／保存／外部变化测试，51项浏览器接受／编辑／Undo／Redo和大正文专项 | Windows真实IME、选区粘贴、很多短行、完整失败及外部任意改写链 |
| V03 焦点和弹层 | 设置12项、综合测试中既有嵌套弹层回归 | 所有嵌套链逐层Tab／Esc／焦点返回与父草稿保留的真实外壳矩阵 |
| V04 二级页面 | 已有891项布局及本轮相关检查器 | 所有页面×加载／空／禁用／校验／失败／成功状态的逐页前台对照 |
| V05 Diff／历史 | 综合回归和相关静态字号检查，完整C#测试 | 相同真实补丁及复杂分叉／引用叠放的同场景PyCharm对照 |
| V06 配色和控件状态 | 浅深专项、现有主题／图标／焦点回归 | 每页完整状态色、线宽和中心的参考像素对照；不能从少量页面推到全部 |
| V07 主题草稿 | 设置浅深／取消／保存竞态，真实WebView2浅深切换 | System外部变更，Git忙／冲突草稿／终端输出同时切换时的实机矩阵 |
| V08 DPI／窗口 | 浏览器DPR1／1.25／1.5布局、真实DPR1.75外壳 | WM_DPICHANGED跨屏、最大化／恢复／贴靠／弹层resize完整前台矩阵 |
| V09 终端 | Infrastructure／Shell自动化中的既有终端测试 | PowerShell／CMD／Git Bash／WSL／自定义命令全链、真实IME、canvas主题与缩放及前台子进程回收 |
| V10 Windows10／字体 | Windows11真实字体缺失回退 | Windows10 22H2 x64实机、同字体墨迹及跨环境取整 |

以上均为部分验证或未验证；没有把V项计为“已全部通过”，也没有把它们全部算成确证缺陷。

## 6. 文件哈希与资源

| 文件 | 修改前MD5 | 修改后MD5 |
| --- | --- | --- |
| `web/src/mockup.js` | `5D0C7DE0D3A935C03F8DFBC414AAFAB4` | `52FC1BF4D914A7D42E549C200E01A4DF` |
| `web/src/mockup.css` | `F5956EE130E2F3E6BC1DE24E5502B08A` | `56F07FE57D6210B86B0E48AE4824BCC8` |
| `web/src/live-data.js` | `9A18302CD99D3BBF06900F0270381BF4` | `D68C52FA4D68C3B458CC16993677DF6A` |
| `web/src/bridge.js` | `F297BF8769E249CA0D804F4F4EEEF5A9` | `F297BF8769E249CA0D804F4F4EEEF5A9` |

四个共享文件两側字节一致：`mockup.js`、`mockup.css`、`current-find.js`、`image-preview.js`。mockup.js、mockup.css及current-find.js为本轮修改，image-preview.js保留原有工作区内容。current-find.js迁移前MD5为E47643F5CCE7761312CDE438E6837D55，修改后为45C1B8795D435CA5256941F3B0525545，两侧一致。所有本轮编辑文件为UTF-8／LF。

真实外壳最终批次PID8108，诊断端口9338，临时目录`C:/Users/wl024/AppData/Local/Temp/Augit-restoration-oeIlav`，脚本已停止所属进程树并删除目录。首个启动时导航抢跑实例PID23988也已退出，其`Augit-restoration-JenV3s`目录已核对种子与系统temp归属后删除；没有结束用户PyCharm或其它应用。早期成功批次PID22488及`Augit-restoration-WR9Ccq`同样已清理。

保留证据位于`artifacts/restoration-fixes-2026-10-12/`：`settings-font-baseline.png`、`markdown-tabs-light.png`、`markdown-tabs-dark.png`、`webview2-results.json`，以及人工复核的`conflict-large-font.png`／`find-large-font.png`。其它批量测试截图清理，只保留这些验收附件。这些是交付附件，不是临时文件；截图证明本机外壳结构与配置生效，不能证明同字体PyCharm像素完全复原。

## 7. 手动启动与停止

```powershell
dotnet build Augit.slnx --no-restore
$env:AUGIT_BROWSER_EXECUTABLE = 'C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
$playwrightPath = 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/live-shell.spec.cjs $playwrightPath --settings-state-only
node tools/audit/live-shell.spec.cjs $playwrightPath --goal-conflict-only
node tools/audit/live-shell.spec.cjs $playwrightPath --restoration-fixes-only
node tools/audit/run-restoration-fixes-shell.cjs $playwrightPath
```

浏览器专项正常完成由`finally`关闭服务器与浏览器；真实外壳runner输出本次PID／端口／目录并自行清理。手动中断时，只停止本次记录的进程树：

```powershell
# 浏览器专项：替换为本次启动的Node PID，停止其服务器及浏览器子进程。
taskkill /PID <本次Node进程ID> /T /F
# 真实外壳：替换为runner输出的AUDIT_PID，不按进程名批量结束。
taskkill /PID <本次AUDIT_PID> /T /F
```

若只手动打开Augit，启动和停止命令为：

```powershell
$augitProcess = Start-Process -FilePath '.\src\Augit.Shell\bin\Debug\net10.0-windows\win-x64\Augit.exe' -ArgumentList '--workspace','D:\github\Augit' -PassThru -WindowStyle Hidden
# 使用记录的本实例PID，停止本实例与所属WebView2。
taskkill /PID $augitProcess.Id /T /F
```


最终收尾：浅深各55／55视觉稿场景通过；最新891布局再次通过，复原32项含预览Ctrl+F再次通过，真实WebView2的previewFind为source／opened=true且无脚本错误。最终C#格式、JS语法、四文件镜像、PowerShell编码、文档声明一致性、覆盖表生成器与git diff --check均通过。启动的HTTP服务、浏览器、Augit及所属WebView2已结束；端口9338无监听，系统temp无本轮冲突范围目录。最后一次18:17:52构建启动的复用MSBuild节点PID26048（父26392）已核对进程身份后停止，避免编译服务残留。保留六个截图／JSON交付附件，其它批量测试截图已删除；用户原有.tmp和原有应用未处理。
