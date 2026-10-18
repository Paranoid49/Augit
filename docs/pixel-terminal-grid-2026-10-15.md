# 终端字符网格整改（2026-10-15）

整体整改未完成。本批修复实时终端文字步长和网格不一致：同一Windows PowerShell自然横幅两行宽差从−6/−21物理px降至0/0。行顶部间距仍为参考30、Augit29物理px，抗锯齿和完整终端逐行配对未通过。Windows10排除本次实机验收，跨屏DPI保留未验证。

## 根因与实现

本地权威CharacterGrid.kt:136–137采用view.maxCharWidth乘列间距；EditorView.java:861–863取粗斜体W宽度。新增TerminalGridMetrics.java使用独立开源JetBrains Mono2.304粗斜体和参考JBR，在32字号×8比例测得256组W宽度，均符合round(round(size×DPR)×0.6)/DPR。证据为cell-width-inherit/jbr-max-widths.csv；独立度量不代替实机截图。

xterm6.0.0 DOM把letterSpacing取整为设备像素，无法表达175%下13px字体从约13.65到14物理px的补偿。因此保留上游原包，增加确定性生成器patch-xterm-cell-width.cjs，校验上游SHA256及六个唯一锚点，新增augitCellWidth选项。默认0沿用上游；正数指定逻辑格宽，文字、光标、选区和鼠标命中沿同一渲染尺寸。补丁不缩放字形、不修改终端协议；不适用于未集成的Canvas/WebGL addon。

terminal-geometry.js只在随包JetBrains Mono实际加载时启用该规则。切换字体、字号、窗口DPI或字体加载完成后重新计算；Consolas等字体恢复上游度量。现有fit-addon继续计算行列，无新增用户设置、依赖、后台进程或定时器。

第一次真实复测发现网格已经变宽，但文字仍窄：全局星号选择器的letter-spacing:0覆盖了xterm行容器的内部补偿。共享CSS仅让xterm行和文字span继承内部字距；不套用普通编辑器CSS，不改变工具栏。

## 原始证据

根目录：artifacts/pixel-followup-2026-10-15/terminal-line-pair。保留reference-light.png和fixed/actual-light.png作为修前原图；中间结果为cell-width-inherit；本批正式结果grid-final/actual-light.png及banner-ink.json。均为175%DPI、浅色、JetBrains Mono13，同自然Shell横幅；整窗布局不同，只计局部文字配对，不计55页同态验收。测量三通道均≤80，不缩放原图。

| 文字 | 参考宽×高 | 修前宽×高 | 修后宽×高 | 修后宽差 |
| --- | ---: | ---: | ---: | ---: |
| Windows PowerShell | 249×18 | 243×18 | 249×18 | 0 |
| Copyright横幅 | 789×23 | 768×23 | 789×23 | 0 |

正式修后SHA256：a58a1452b4d2dd0fa67780984727baa55c0df4acf1fa4a51333305c04ec07b95。修后墨迹边界为94,1166,343,1184及96,1195,885,1218；参考边界见前批banner-ink.json。高度相同不代表基线、轮廓或抗锯齿一致。

## 验证与失败记录

- 浏览器48组合：6个模拟DPI×4字号×深浅主题，覆盖实际文字步长、光标、选区和鼠标拖选；误差上界为1物理像素，不能解释为逐像素相等。6比例的Consolas零选项回退与原始上游包比较一致；负数、字符串及非有限选项拒绝。
- 单测4项通过，包含256组独立度量、未加载和其他字体回退、确定性生成及上游漂移拒绝、ANSI配色。
- 真实WebView2专项通过：深浅主题、取消预览、13→17→13、JetBrains Mono→Consolas→JetBrains Mono，节点身份保持，字格分别8/10.285714/8逻辑px。ANSI中文英文16行输出及缓冲检查通过，但不是PyCharm双端混排配对。
- Debug Shell构建0警告0错误，2.43秒；本批未跑全量测试。
- 首次补丁调用锚点错误及一次工具字符串截断未写入；已改正。第一张只改网格的真实截图仍差−6/−19，保留cell-width目录作失败证据。Java初版输出字符串编译失败，改为CSV后256组通过。浏览器字体URL初次失败，改为内存路由，无临时服务器。原0.12逻辑px断言暴露DOM1/64px逐段取整，改为明确的1物理px上界并登记，未将其称为像素全等。

## 锁定与工作区

运行时xterm从488663变为489086字节，SHA256从14903579ff54664cd72f8e8699e6961a6272c21863ec1c3b118cdc8af5d4a972变为172ef5039c371479d3b93579cde8157914268bfc5e5fb14971254a3562c3d149；锁文件和第三方说明同步，原包和MIT许可证保留。补丁的移除条件为上游提供等效稳定格宽接口；升级必须重新审查。

四网页MD5：mockup.js保持DB12568D024253582D1C09B5DA1CD5AE；mockup.css E9D173258AFD662A66ABC90EDB0B9734→6AFA43AB3714BA447FEA02CBA3A33274；live-data.js 50B86BC40F3F003A8FE292888DC930BC→101590DDBA634002223B3F9CD4EB89A9；bridge.js保持F297BF8769E249CA0D804F4F4EEEF5A9。分支dsh、HEAD3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；保留工作区已有混合修改，未暂存、提交或回滚。

## 启动、停止与清理

驱动finally已停止本批Augit2784/2516/23200及子树，并删除Augit-terminal-pair-XkdkyB/3BXSkB、Augit-terminal-colors-Cd8Ium。57512/53479/63626无监听；浏览器矩阵关闭所有context和browser，未启动服务器。正式PNG/JSON/CSV保留，失败Java生成的空jsonl删除；未重试任何历史被拒绝的清理动作。

复验命令（在仓库根目录；Playwright模块和Chromium路径按本机安装填写）：

~~~powershell
node tools/audit/patch-xterm-cell-width.cjs --check
node --test tools/audit/terminal-geometry.test.cjs tools/audit/terminal-palette.test.cjs
node tools/audit/verify-terminal-grid.cjs <Playwright模块路径> <Chromium.exe路径> artifacts/terminal-grid-new.json
node tools/audit/capture-terminal-pair.cjs <Playwright模块路径> artifacts/terminal-pair-new
node tools/audit/live-terminal-palette.cjs <Playwright模块路径> artifacts/terminal-grid-live-new
dotnet build src/Augit.Shell/Augit.Shell.csproj --no-restore
# 驱动自动清理；异常中断时仅停止本次输出的PID及子树。
taskkill /PID <本次Augit_PID> /T /F
~~~

收尾检查：9共享资产、24个Debug网页镜像文件、9个运行时锁定资产的大小与SHA256、相关JS语法、文档声明和git diff --check通过。完整原图已目视复核；工具展示可能缩小，量测始终使用2450×1628原图。历史全量断言快照继续标记过期，未用本批专项覆盖代替全量。

长行补验发现上游WidthCache使用32字符offsetWidth整数宽，100列累计光标偏移为−1.25至−3.125物理px。仅在augitCellWidth启用时改用getBoundingClientRect小数宽，选项变化清除缓存；关闭扩展保留原始offsetWidth路径。最终48组及6组100列检查通过，累计偏移降至−0.015625至−0.0390625物理px，见browser-matrix-final.json。真实设置切换专项再次通过，PID22460及profile已清理。两次严格前景采图被拒收，未生成PNG；交互等待首次与清理竞态，改为45秒等待且仍保留前景校验。最终构建1.61秒、0警告0错误。

最终前景采图经computer-use激活本次窗口后通过：两行墨迹宽249/789物理px，与参考相同；行顶间距仍29对30，不宣称基线或整页相同。grid-final原图局部已目视。后续PID11596/22460/18744/12284/11736均已退出，50663/61353/52361/52020/55836无监听；对应OB0VnW/1eeeI0/fxb82p/WVlh0a/grpBY1隔离目录均不存在。最终发布锁与24文件镜像再次通过。人工激活采图可在capture-terminal-pair命令末尾加--wait-foreground，45秒内激活本次窗口；前景检查不通过时仍拒收。
