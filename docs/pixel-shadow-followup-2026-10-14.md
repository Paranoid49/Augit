# 系统阴影取证接续（2026-10-14）

2026-10-15新增[四角取证](pixel-shadow-corners-2026-10-15.md)：当前版本消除旧左下快照栅格干扰；深色右下64物理px外侧12288像素局部通过，128px及其余角部因背景不同仍未验收，非活动状态未验收。未修改产品阴影。

已取得浅深主题双端系统PNG及背景，发现并修复右下重复投影，近边16像素专项通过。四角、非活动状态及背景干扰尚未完成验收，不宣称全系统阴影或55页通过。下方历史限制按批次保留，最新进度见末节。

## 机制与来源

Augit `ShellModalHost.cs` 创建带 `0x00C80000` 样式的独立所有者窗口，调用 `DwmExtendFrameIntoClientArea`，四边扩展1物理像素。这个1不是阴影宽度，不能用作阴影半径结论。

本地 IntelliJ 权威 `IdeGlassPaneImpl.kt:206` 仅在 Windows 且 `ide.window.shadow.painter=true` 时安装 `WindowShadowPainter`。后者只为符合条件的无装饰 owned window 绘制，使用八片素材；不等同于所有参考窗口都使用这套阴影。参考 properties 和 bin 下 vmoptions/properties 未找到显式开启项，但这不足以证明运行时属性值或当前 Settings 实际机制。不得直接照搬素材尺寸替代系统测量，也不复制官方资源。

## 当前取证限制

本轮启动独立 PyCharm 2026.2.1 参考 PID6444，窗口已出现，但 computer-use 返回 Windows 锁屏画面。已停止窗口输入并询问用户解锁。锁屏画面未写入正式参考证据。PID6444及子树已用 `taskkill /PID 6444 /T /F` 停止，保留原有用户 PyCharm。

现有 `native-inspect/system-source.json` 的JPEG缺原始origin；只能证明形态。当前 computer-use API 没有无损编码参数，不能把JPEG转PNG冒充原始无损数据。真实测量需取得可信的原始无损系统合成图；CDP、PrintWindow客户区不含系统外沿，不能替代。

## 定量工具与采集要求

新增 `tools/audit/measure-window-shadow.py`，使用已有 Pillow，不新增依赖。输入为同位置、同尺寸的窗口图与无窗口背景图，以及窗口边界的物理坐标（右下排他）、实际缩放。需人工核对两图背景、主题、活动状态和曝光区域一致；不重采样、不含遮挡。PNG格式检查不能识别先前JPEG转码，必须另有来源记录。

工具按四边中央半段排除圆角，逐物理像素距离记录RGB平均暗化量中位数及最大通道变化。报告超过默认2级RGB阈值的最远范围，末四行仍有变化则 `tail_settled=false`，需扩大采样或排除背景变化。报告不推算高斯模糊半径，不自动下验收结论。正式对比应同时检查阈值敏感性、四角形状、活动/非活动状态以及浅深主题；当前没有实机数值。

```powershell
python -B tools/audit/measure-window-shadow.py <窗口原始PNG> <同位置背景原始PNG> --rect <左> <上> <右> <下> --scale 1.75 --radius 64 --output <剖面JSON>
python -B tools/audit/test_window_shadow.py
```

四项定向测试通过：非对称四边/缩放、变亮排除/尾部变化、JPEG/裁切拒绝、尺寸/无效缩放拒绝。合成图只存在内存；`-B`禁止字节码缓存，没有后台进程或测试临时文件。产品代码和主要网页哈希未改，不运行全量测试。

独立参考启动沿接续记录：临时设置进程环境 `PYCHARM_PROPERTIES=D:/JetBrains/PyCharm-2026.2.1-reference/reference.properties`，运行 `D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe D:/JetBrains/PyCharm-2026.2.1-reference/fixture` 后恢复环境。每次记录返回PID；停止命令为 `taskkill /PID <本次参考PID> /T /F`。

用户报告已解锁后，重新启动PID15404并实际观察到参考主窗及Settings；已保存原始Settings/关闭动画结束后的背景JPEG与origin、哈希，见artifacts/pixel-followup-2026-10-14/shadow-reference-observation/source.json。关闭后的首帧仍处于淡出动画，已排除，重观测后才保存背景。接口仍返回JPEG，不能用于上述无损剖面工具，当前没有新增阴影定量结果。PID15404及子树已停止。

## 原始系统PNG采集与参考初测

新增只读采集器`tools/audit/capture-screen-region.ps1`，直接使用`Graphics.CopyFromScreen`读取系统合成像素，不使用PrintWindow、CDP截图或JPEG转码。每份PNG附物理坐标、DWM边界、DPI、PID、前景前后状态和SHA256；不自动把前景窗口等同于无遮挡。采集器拒绝覆盖已有证据，复采要求整数区域与相同DPI，并核对采样前后窗口边界、进程与DPI。Graphics、Bitmap、哈希流均释放，线程DPI上下文恢复。

证据目录：`artifacts/pixel-followup-2026-10-14/shadow-lossless-reference/`。参考为PyCharm2026.2.1、深色、UI12、DPI168（1.75倍）；`settings-dark-wide.png`与`background-dark-wide.png`均1811×1471，原始区域`[534,117,1811,1471]`，窗口图内边界`[128,128,1683,1343]`。关闭动画首帧排除后才采背景，两次稳定背景SHA256完全一致：`0d51120cd1b8a6b1ee8f98da84c280e8fea54521211fcdc774a1dab46b32349f`。

阈值2、半径128物理像素的`profile-dark-wide.json`：左18、右18、下68物理像素（分别约10.286、10.286、38.857逻辑像素），这三侧末四行回到阈值以内。顶部中位数超过阈值的范围5物理像素，但末尾仍有通道变化，且两图标签焦点颜色不同，因此顶部不计有效阴影结论。初版64像素半径的底部未收敛，保留原数据供追溯，不用它代替扩大后的结果。阈值范围不是高斯模糊半径，当前未证明两端一致。

`augit-settings-dark.png`为2076×1597原始PNG，已目视确认Settings内容；关闭取背景时遇到用户输入与系统搜索遮挡，停止窗口操作。此图缺背景配对，不计双端验收或55页合格配对。

采集器4项定向测试通过（6.846秒）：原始PNG尺寸/哈希/同区域、错误PID拒绝、禁止覆盖、错误来源/缩放/非整数或越界区域拒绝。初测发现旧PowerShell环境无法解析Get-FileHash，改为.NET SHA256并重测通过。中文PowerShell脚本采用UTF8 BOM+LF以兼容5.1，17脚本编码检查通过；阴影算法原有4项测试通过。仅审计工具变化，未跑产品全量回归。

本批参考PID16012、Augit PID26328及端口62169在下一轮查询时均已不在运行；原隔离profile清理另核对，不把旧PID误用于新进程。四个主要网页MD5未变：`6298632B16BA82E5FCFB9B0AB177593D`、`06751CCD396E8ECACD19779CBBDE38AB`、`DB52470B2BAB434DE4E463086A39FA6D`、`F297BF8769E249CA0D804F4F4EEEF5A9`。

```powershell
# 先通过界面观察确认目标与无遮挡状态；每次使用本次实际PID和句柄。
pwsh -NoProfile -File tools/audit/capture-screen-region.ps1 -ExpectedProcessId <本次PID> -Padding 128 -Output <窗口PNG>
pwsh -NoProfile -File tools/audit/capture-screen-region.ps1 -WindowHandle <关闭后主窗句柄> -ExpectedProcessId <本次PID> -RegionFrom <窗口PNG.json> -Output <背景PNG>
python -B tools/audit/test_screen_region.py --window <主窗句柄> --process <本次PID> --region <窗口PNG.json>
# 启动命令沿本文件与整改接续记录的隔离实例配置；异常停止只针对本次实例。
taskkill /PID <本次PID> /T /F
```

## 双主题系统剖面与重复投影整改

本批新增`capture-shadow-shell.cjs`，使用现有Playwright/CDP驱动隔离实例的真实Settings开关，截图仍由只读系统采集器读取；不以CDP图代替系统外沿。每组关闭后采两份背景，SHA256必须一致，且模态深度、关闭后快照归零、所有者恢复、未处理错误均检查。新流程在finally关闭实例和调试端口并删除本次profile。桌面工具点击WebView2子窗口被归属检查拒绝，因此未继续重试鼠标输入，改用项目既有测试接口。

双主题原始图已逐图目视，位于`shadow-augit-dark`、`shadow-augit-light`、`shadow-reference-light`及前述参考深色目录。1.75倍、RGB阈值2时，两端四边中位暗化范围均为深色18/18/5/68、浅色55/55/37/119物理像素（左/右/上/下）。但只看最远范围会漏报：修前浅色右侧第1像素暗化136，参考42；底部155，参考76，额外差异连续占4物理像素。`shadow-profile-comparison.json`保留修前完整剖面和阈值1/2/3/5比较，不能拿它当修后结论。

根因为`ShellModalHost.RegisterClass`同时设置`CS_DROPSHADOW`（0x00020000）及DWM框架阴影。现窗口类Style改为0，保留已有DWM机制和客户区几何；没有凭截图调整模糊参数。新增`verify-shadow-edge.py`比较右/下近边1–16物理像素中位暗化曲线，容差3级RGB，且永远不输出全阴影已验收。修前浅色专项明确失败（最大差94/79），修后浅色1/0、深色0/0，均通过。修后原图、背景、重复背景、完整剖面及`edge-after.json`位于`shadow-augit-light-fixed`和`shadow-augit-dark-fixed`；浅色修后图已目视确认额外黑边消失。

取证发现的独立问题继续保留：Augit左侧背景文字的快照/实时栅格不同导致末端不收敛，参考顶部标签焦点改变也污染尾部；鼠标高亮位于窗口内部，不在本次近边四侧中央采样带。不能用中位数相同抹去背景文字差异。远端差异、四角形状及非活动状态仍需补证。整体55页与全部状态的配对仍未完成。

验证按风险定向执行：Debug构建9.68秒、0警告/0错误；C#修改文件空白格式检查通过；近边校验器3项测试通过；双主题真实修后采集/稳定背景/关闭释放通过；17脚本编码、9共享资产和git diff --check通过。仅窗口类投影位变化，无桥接或生命周期契约变更，未重复产品全量套件。新增和修改文件为`ShellModalHost.cs`、采集器及其4项测试、`capture-shadow-shell.cjs`、近边验证器及3项测试、相关文档；四个主要网页前后哈希均为本文件上一节记录值。

首次修后PID4884因目标不在前景被采集器拒绝，未产生有效PNG，finally清理完成。随后浅色PID19348/端口55433和深色PID7684/端口50307通过并清理；修前PID16824/59602、21992/56498同样清理。参考PID5032及子树已停止，手动实例PID4708及子树已停止；后续进程路径查询无本次Augit或参考残留。

两个手动隔离目录删除被自动审批拒绝，理由仅`blocked by policy`，未换工具或拆分同一删除动作绕过：`C:/Users/wl024/AppData/Local/Temp/Augit-shadow-a9c534aad257470d945b4d4446264ff9`、`C:/Users/wl024/AppData/Local/Temp/Augit-shadow-c277676d77e14d91bacb1fa264a66e73`。它们保留为待清理静态目录；自动驱动创建的5个profile已在finally删除。没有将待清理目录写成清理成功。

```powershell
# 每次使用新的证据目录，工具拒绝覆盖；脚本自动启动并停止本次实例。
$pixelPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-shadow-shell.cjs $pixelPlaywright artifacts/shadow-light-new Light
node tools/audit/capture-shadow-shell.cjs $pixelPlaywright artifacts/shadow-dark-new Dark
python -B tools/audit/test_shadow_edge.py
python -B tools/audit/verify-shadow-edge.py <本次剖面JSON> <同主题参考剖面JSON> --output <近边结果JSON>
# 驱动异常中断时，仅停止控制台输出的本次PID及子树。
taskkill /PID <本次PID> /T /F
```

2026-10-14快照物理像素整改：确认CapturePreview实际2452像素被压到2450像素，改为按PNG真实宽高和捕获比例呈现；深浅主题同位置文字带各156个差异像素降为0，真实更新/嵌套尺寸及关闭清理回归通过。完整证据、命令、哈希和未验收边界见[pixel-snapshot-sizing-2026-10-14.md](pixel-snapshot-sizing-2026-10-14.md)。整体整改未完成。
