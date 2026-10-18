# 日常启动与测试窗口对齐（2026-10-15）

2026-10-16用户双窗口回归：上一批嵌套优化遗留表单身份，重开/返回等待应答时会重复呈现并提前解除隐藏，已用两个失败测试复现并修复。13项定向通过；真实Debug故意延迟呈现及截图回包，五次打开均一次呈现、depth1，关闭139–248ms；两套产物已更新，测试资源已清理。没有本批逐帧录像，不宣称原有全部闪帧消失。详见[最新回归记录](settings-close-runtime-2026-10-16.md)。

2026-10-16普通启动关闭整改及嵌套接续：修复关闭排队、重试吞通知、标题栏关闭遗漏，以及子窗返回父窗等待捕获和重新隐藏父表单。用户关闭旧实例后Debug/Release均已重建；最终Debug随包、真实仓库9标签、延迟应答验证五条关闭路径149–241ms，嵌套返回109ms，零网页错误。11项JS及203项Shell通过，两套24文件镜像一致，测试资源已清理。完整失败、证据、哈希及启停命令见[关闭整改](settings-close-runtime-2026-10-16.md)，不等于全量像素目标完成。

## 2026-10-16 终端选区修正后的交付复核

正式日常启动器与随包显式main-project入口各完成32状态，退出码均0；两路资源哈希、实际执行JS哈希一致，32/32状态度量相同，23/32原始PNG完全相同。系统DPR1.75、页面缩放1、界面12、正文固定JetBrains Mono13。日常路线使用真实配置逐字节副本及默认profile，用户原配置未改写。这是入口比较，不是55页PyCharm验收。

余下原图差异：Quick Open/Results/Reset各78像素，File Info/Settings Appearance/Settings File View/Workspace Open各27像素，Stash Create41926、Worktrees37622。后两组局部图确认焦点位于不同输入框/列表，以及后台Changes滚动位置不同；状态未统一，不宣称全部视觉一致。小差异保留量化，不用容差或排除区域抹去。

证据：artifacts/startup-path-investigation-2026-10-16/selection-final-daily-dialogs及selection-final-test；前者paired-result.json为逐页比较，normal-commit.png为日常入口原图，remaining-differences.png展示主要未统一状态。

首轮采到22状态后在Workspace Open失败：审计以display:contents包装层为可见目标，其自身无矩形。现Workspace Open、Remote Add均检查内部真实dialog；未改产品CSS或放宽采样门禁。新增包装层负例/内部表单正例，截图5个子场景（Node总计6项）通过，耗时2774.76ms。首轮失败保留在selection-final-daily，不计通过。

终端选区产品改动及浅深专项见[选区记录](pixel-terminal-selection-2026-10-16.md)。当前MD5：mockup.js=44D6C53684B74DBC6116FF25F7C22932、mockup.css=D5DADE8F4DB84506031A9AB3B1F5DB80、live-data.js=6200A1F1FFF74A92DC9A55AEFD0D4DAA、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。Debug/Release各24文件逐字节同步，共享9项资源、相关JS语法及git diff --check通过。

本次PID18832/11068/12972，端口56906/53012/64069，临时目录Augit-startup-compare-hl8adQ/kkhQfk/EGF8ZJ，驱动finally完成退出清理。启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止正常关闭窗口，异常时taskkill /PID <本次PID> /T /F。未暂存、提交或回滚既有混合改动；整体目标仍未完成。

2026-10-16截图异常接续：下节Settings Terminal缺失图已定位到采集工具隐式隐藏光标引发背景刷新，增加跨采样区间门禁并取消该样式写入后，普通入口补拍成功。此项不等于解决产品桌面露底，详见[截图干扰记录](pixel-capture-interference-2026-10-16.md)。

## 2026-10-16 用户重申后的当前版本核查

本批以正常启动窗口作为交付对象，未继续调整冲突窗口小数坐标。直接调用日常启动器（当前 Debug、仓库 web、真实配置逐字节副本、默认 profile），再与同版本随包资源、显式 main-project、隔离 profile 比较；没有注入 CSS、字体或 DPI。用户配置原文保持不变。

当前四网页 MD5：mockup.js=44D6C53684B74DBC6116FF25F7C22932，mockup.css=D5DADE8F4DB84506031A9AB3B1F5DB80，live-data.js=C8DA64065C77E60234F2D614334E8DC4，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。共享9项通过，Debug/Release各24个随包文件逐字节相同。两路资源、主窗及五页采集度量一致；系统DPR=1.75、页面缩放=1，实测界面字号12、正文字号13，正文JetBrains Mono，旧正文选择控件不存在。

2881×1717原图：初始主窗、Settings Appearance/File View/Git四对差异为0；Commit有27个差异像素。Settings Terminal的日常截图只显示主窗，测试截图显示设置窗，差异2774611像素，不能计为通过。采集器只等待固定350ms，未检查背景捕获期间的模态隐藏状态；度量相同不能证明这张截图有效，也不能由该CDP截图判定实际桌面窗口消失。本批保留失败原图，不重新归零、不宣称全页截图一致。

证据位于 artifacts/startup-path-investigation-2026-10-16/current-normal-delivery 与 current-test-delivery，汇总为前者 current-pair-result.json。两路进程PID912/25596、端口55626/60704及临时目录Augit-startup-compare-RJ8HPz/CXQcse均已退出或删除。未新增产品逻辑，未回滚、暂存或提交。

正常启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`。正常关闭窗口即可停止；异常时按脚本打印的本实例PID执行 `taskkill /PID <PID> /T /F`。此前32状态入口验证继续以原始记录为准；全量像素、背景刷新、完整模态等未完成项仍保留。

## 2026-10-16 当前入口配对交付复验

针对用户“正常启动的所有界面向测试对齐”，本批直接执行日常启动脚本，采用真实设置逐字节副本及默认 WebView2 profile；与当前 Debug 随包资源、显式 main-project、隔离 profile 的测试入口比较。两路系统 DPR=1.75、页面缩放=1，没有注入 CSS、字体或强制 DPI，用户原配置逐字节未变。

实际资源哈希一致，32 个状态的完整采集度量全部相同；2881×1717 CDP 原图中25张逐像素相同。其余7张仍存在差异：Settings Appearance 27像素、Workspace Open 17348、Stash Manager 45783、Worktrees 18295、Reset 45783、Push 27、Terminal 1796。差异包围框及双端局部原图保存在 paired-result.json 和 difference-review.png：目视发现工作区/工作树选中项为蓝色与失焦灰色差别，Stash/Reset为背景标题激活色差别，Terminal为提示符是否已输出；两处27像素位于背景提交输入框底边。未重新建立统一前台焦点与终端就绪的截图条件，因此不把度量一致写成全部原图一致。Commit、正文、Diff、File View等原图一致，正文固定 JetBrains Mono 13，无旧正文选择控件。

证据：artifacts/startup-path-investigation-2026-10-16/daily-alignment-handoff 与 test-alignment-handoff，配对汇总为前者 paired-result.json。最终日常参数为 --daily-launcher --only-mode=normal --all-surfaces；测试参数为 --only-mode=explicit-main --bundled-web --all-surfaces，两路均退出码0。首次误用 --normal-only 与 --all-surfaces，采完正常32状态后因尝试场景入口报参数冲突，已更正重跑，未将首次退出码1登记通过。

共享9项资源、Debug与Release各24个资源镜像、git diff --check通过。本批未新增产品代码改动。MD5：mockup.js=A8167A59D3CBFD452683C82FA7A44D61，mockup.css=16A3071D8EB381CEEE8E5B7BDF339026，live-data.js=C8DA64065C77E60234F2D614334E8DC4，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

PID18684、15860、23208已退出；端口58644、64470、61622无监听；临时目录 Augit-startup-compare-doSpV2、FMvToQ、AQpQLT、Jd0BcM均不存在。启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止：关闭窗口，必要时 taskkill /PID <启动脚本输出的PID> /T /F。55页及完整模态、背景刷新、终端/冲突逐行、系统阴影目标仍未完成。

## 2026-10-16 日常入口再次复核

继续以用户日常入口作为交付检查，不新增产品样式调整。直接调用 `tools/start-augit.ps1 -SkipBuild`，使用真实设置的逐字节副本和默认WebView2 profile；没有场景、强制DPI、字体覆盖或CSS注入。当前真实设置为Light、界面Microsoft YaHei UI 12、正文JetBrains Mono 13、appearanceVersion=1。复核主窗、Commit及Settings四分类，实际DPR=1.75、页面缩放=1，页面异常为空；File View固定显示JetBrains Mono和13，旧正文选择控件不存在。已目视查看Commit和File View原始截图。用户设置逐字节未改。

证据：`artifacts/startup-path-investigation-2026-10-16/user-normal-check/comparison.json`及同目录原图。这是当前日常入口的定向复核，不是重新执行32状态矩阵，也不是55页PyCharm配对。此前三路32状态结果仍以原批次记录为准。同步修正architecture中残留的“已有等宽偏好保留”描述，落实已授权的产品裁决；本轮没有新增可执行逻辑。

共享视觉资产、Debug与Release各24个随包资源的逐字节镜像检查、`git diff --check`通过。当前MD5：mockup.js=`6E61081C842CA96770B3F8BB56E13B0A`，mockup.css=`99A3D1E61C7D12BA82DD25CE2E685A76`，live-data.js=`641B29F3BC50EC22500924F64B9C63CE`，bridge.js=`F297BF8769E249CA0D804F4F4EEEF5A9`。本轮未改变这些文件。

本次PID9728、调试端口58062及临时目录`Augit-startup-compare-uFqCvy`由驱动finally清理，并独立检查无进程、监听及目录残留。日常启动使用`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`；停止时关闭窗口，必要时执行`taskkill /PID <启动脚本输出的PID> /T /F`。没有回滚、暂存或提交既有混合修改。

整体像素整改尚未完成，入口对齐不代替背景刷新、完整模态、终端/冲突逐行和阴影验收；Windows 10排除本次验收，跨屏DPI保持未验证。

2026-10-15跳转行组件批次：正常入口与模板共用200×34输入布局及内侧焦点边框；12组布局和随包资源真实11项行为通过，Debug/Release已更新。局部差异3908→1368，仍有横向3物理像素偏差，完整配对未结案。当前哈希、原图、命令及清理见[组件整改记录](pixel-goto-component-layout-2026-10-15.md)。

## 用户交付前复核

本次再次直接执行日常启动脚本，使用当前实际配置的逐字节副本与默认 WebView2 profile，沿用已构建 Debug；未传场景、强制 DPI、字体覆盖或外观重置。正常入口主窗、Commit、Settings 四分类已采集并目视检查 Commit 与 File View 原图。当前实际配置为 Light、Microsoft YaHei UI 12、JetBrains Mono 13、appearanceVersion=1；运行时 DPR 为 1.75，页面缩放为 1。File View 的正文字体与字号是固定文字，旧选择控件不存在。采集脚本完成无未捕获页面异常断言，并确认用户原配置逐字节未变。

证据为 artifacts/startup-path-investigation-2026-10-15/daily-user-handoff/comparison.json 及同目录原始截图。正常启动截图不作为 PyCharm 全页一致性证据；本次没有重新运行此前已完成的 32 状态矩阵，也没有新增产品渲染改动。Debug、Release 随包 web 各 24 个文件再次与源目录逐字节一致，共享视觉资产检查通过。四网页 MD5 延续下文最新记录。

本次 PID6932 已退出，端口63533无监听，临时目录 Augit-startup-compare-hYbOMq 已删除，专用 WebView2 无残留。接续录像的 PID22076、ffmpeg PID2664、端口53648和临时目录 Augit-shadow-pair-ZeQjEc 同时核实无残留。启动、停止命令仍使用本文日常命令；没有暂存或提交工作区混合改动。

## 补验：直接执行日常启动脚本

本次直接执行 tools/start-augit.ps1，使用 -SkipBuild、当前仓库和实际配置的逐字节副本；使用默认 WebView2 profile，仅通过子进程环境开启读取用调试端口。不传场景、强制 DPI、字体覆盖或外观重置，不注入 CSS。为避免巡检改变用户恢复标签，设置保存写入副本；因此不能称为默认配置文件的全量巡检。

32 个状态均成功打开并留图，页面异常数组为空，正文 JetBrains Mono 13、界面 Microsoft YaHei UI 12、系统 DPR 1.75、页面缩放 1。与此前显式 main-project 测试基准比较，8 个状态的完整度量对象相同，另 24 个状态仅 Changes 分组或文件行的 y 坐标不同，差值为一行 24 逻辑像素。此次 Changes 为 154 条，旧基准为 153 条，随后定位文件的滚动位置也相差一行。字体、颜色、尺寸及其余记录字段相同；保留完整差异，不把数据变化删去后宣称整图相同。

证据：artifacts/startup-path-investigation-2026-10-15/daily-launcher-final/ 下的 normal-surfaces.json、comparison.json、baseline-comparison.json 和 32 张页面截图。该巡检使用产品点击及公开打开入口；截图为 CDP 页面原图，不证明系统阴影和桌面合成过程。

新增 daily-launch-command.cjs 及其两项定向测试，compare-startup-paths.cjs 增加 --daily-launcher 模式。两项测试、驱动语法检查、共享资源一致性和 git diff --check 通过。本次未再改产品渲染代码或四份网页文件；MD5 延续下节记录。

本次 PID 17576 已退出，调试端口 51606 无监听，临时目录 Augit-startup-compare-efh5OR 已删除，用户配置逐字节未变。TCP TIME_WAIT 不计为监听或后台服务器。检查进程命令行命中了检查器自身，已区分。启动和停止仍使用本文日常命令。

本次只补齐日常脚本的直接验收证据。55 页 PyCharm 双端同态对照、背景刷新露底与延迟、终端及冲突完整像素配对、系统阴影等整体目标尚未完成；Windows 10 不在本次验证范围，跨屏 DPI 保留未验证。

## 本批交付：正常入口、测试入口和 Release 对齐

用户重申正常启动的所有界面向测试对齐。本批继续落实产品及随包资源，没有重置用户工作区、标签或工具窗。

### 查明并修复的入口差异

1. Reset/Rollback 按钮状态 CSS 原来限定 body 的测试场景名，正常主窗打开同样对话框时不匹配。七处选择器已改按实际 `.reset-dialog` / `.rollback-dialog` 类型匹配，深色仍由主题控制，视觉稿同步。当前 CSS 无 `data-scene` 选择器。新增 `dialog-entry-styles.test.cjs` 直接加载产品 CSS，浅深主题、两个对话框、四类按钮、五种状态共 80 组；修前焦点轮廓及内阴影不同，修后 80 组一致。
2. 本批开始时 Release DLL 时间为 2026-10-14 00:10:40，Debug 为 2026-10-15 18:22:28，确实存在旧产物。已重建 Debug/Release，均 0 警告、0 错误；两套随包 web 各 24 文件与源目录逐字节一致。没有证据断言用户此前一定启动了旧 Release；历史证据目录里的发布包不冒充当前程序。
3. 延续前批程序级迁移及正文固定 JetBrains Mono 13，本批让显式 `-AlignAppearance` 同步写入 `appearanceVersion=1`。审计临时设置也写版本，并检查实际主题/字号/DPR，避免请求 Dark/UI40 却迁移成 Light/UI12。该大型矩阵本批未重跑，仅完成驱动语法检查。

末次文档核对发现 ui-compliance 的脚本数仍为 17，与当前实测 19 不符；已更新数量及实测日期。该过期声明不作为通过证据。

### 三路真实外壳对照

新增 `startup-surfaces.cjs`，使用正式点击、快捷键及产品公开打开函数；不注入 CSS、字体、DPI 或业务桩。只打开和取消 Git 管理窗口，未执行 Git 写操作；终端打开后关闭，未输入命令。三路均使用当前配置的逐字节副本、独立 profile、exe 随包 web、系统 DPR 1.75、Light、界面 Microsoft YaHei UI 12、正文 JetBrains Mono 13、页面缩放 1。

| 路径 | 状态数 | 与 Debug 普通入口的度量比较 |
| --- | ---: | --- |
| Debug，无 `--scene` | 32 | 基准 |
| Debug，显式 `--scene main-project` | 32 | 32/32 相同 |
| Release，无 `--scene` | 32 | 32/32 相同 |

32 状态包括 Project 文本、当前查找、跳转空/非法、快速打开空/结果、仓库搜索、分支、Commit、Rollback、工作区 Diff、Git Log、Markdown 三模式、JSON、图片、文件信息、Settings 四页、打开工作区、Clone、创建/管理 Stash、Worktrees、Reset、Push、Remotes、新增 Remote、终端。每页记录字体、颜色、几何及异常，异常数组均为空；读取时排除背景捕获的临时隐藏态。

Debug 两入口 32 对原尺寸 PNG，在 RGB 容差 2、排除 0 像素下，**23 对差异为 0，9 对仍有差异**。最大是 Push 的 9359 像素，约占整图 0.1892%；搜索内容带、标题区及少量像素仍未全部归因。度量一致不等于全部原图一致。本批是入口一致性验证，不是 55 页 PyCharm 配对；完整 32 状态使用配置副本，不冒充实际默认 profile 的全量巡检。前批真实默认配置/profile 已测范围见后文。

证据为 `artifacts/startup-path-investigation-2026-10-15/all-surfaces/normal-surfaces.json`、同目录 `explicit-main-surfaces.json` 及 `release-surfaces/normal-surfaces.json`。汇总为 `startup-delivery-summary.json`，像素差异为 `all-surfaces/image-pair-differences.json`。最终单侧补跑覆盖的 `all-surfaces/comparison.json` 只有 explicit-main，不能代替两份逐页证据。当前普通入口原图包括 `normal-project-text.png`、`normal-commit.png`、`normal-settings-file-view.png`。

脚本迭代曾因图片选择器不符、Cancel 选择冲突、异步标题刷新移除菜单、背景捕获中间态而失败；已修正选择器、只读菜单重试和读取条件后复跑。首次启动器测试与真实实例并行，被已有实例保护拒绝；实例退出后单独复跑通过。这些失败不计通过，未通过注入样式绕过问题。

### 验证和清理

本批通过 80 组状态、Windows PowerShell 5.1 启动器回归、共享资产、19 个 PowerShell 编码检查、两套各 24 个资源镜像、四个审计 JS 语法，以及刷新采集启动 3 项/模态恢复 2 项定向回归。Infrastructure 214 项/Shell 194 项是前批公共设置契约回归，本批局部 CSS 没重复无关全量。小命令未全部单独计时，不补造耗时。

复验命令（从仓库根目录执行）：

```powershell
$pw = 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/dialog-entry-styles.test.cjs $pw 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/start-augit.test.ps1
node tools/audit/compare-startup-paths.cjs $pw artifacts/startup-path-investigation-2026-10-15/all-surfaces --all-surfaces --bundled-web
node tools/audit/compare-startup-paths.cjs $pw artifacts/startup-path-investigation-2026-10-15/release-surfaces --all-surfaces --bundled-web --only-mode=normal --binary=D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe
```

正常启动和停止命令见下文；也可直接运行已更新的 Debug/Release exe。审计脚本 finally 自动关闭本轮实例、端口并删除临时 profile；异常时仅按驱动输出 PID 执行 `taskkill /PID <本实例PID> /T /F`。本批 PID 21788/26104/4272/18140/13404/16696/24684/20088，端口 55524/57390/58103/61267/63533/56594/59913/51269，八个 `Augit-startup-compare-*` 临时目录及启动器测试目录均已清理。独立检查无本批 Augit、专用 WebView2、端口或目录残留；命令行扫描匹配到的检查器自身已区分。保留备份和 artifacts 证据，没有回滚、暂存或提交混合改动。

当前 MD5：`mockup.js=612E9976704343ABE1BB8778B077508F`、`mockup.css=089ED8741BD87C8A4F95501E3485526E`（前值 `A642041A37905E7BAD4ACCB79565CD60`）、`live-data.js=8A39A72F1D0F8FC7D53D3F158205B2C7`、`bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9`。四份中本批仅 CSS 哈希变化。

背景刷新普通入口 378 帧仍有内部露底和边缘变化，严格整表面验收失败，见[背景接续](pixel-background-followup-2026-10-14.md)。入口一致性不能代替背景、55 页/全模态、终端/冲突逐行及系统阴影验收。Windows 10 按用户要求排除本次验证；跨屏 DPI 未验证，整体整改目标仍未完成。

## 后续用户裁决与程序级落实

用户再次明确要求全部正常界面向测试对齐，不能只修改启动脚本或本机配置。此前将截图中的 Commit 归因为布局恢复没有证据，已撤回；不据此强制改成 Project。

SettingsStore 现将未带 appearanceVersion 的旧配置迁移到 Light、Microsoft YaHei UI 12，并以 appearanceVersion=1 随下一次正常保存落盘；读取本身不改原文件。已迁移配置保留后续主题及界面字号调整。所有读取/保存以及宿主 settings/write 均固定等宽正文 JetBrains Mono 13，设置 File View 页取消等宽字体/字号选择。工作区、标签、Git 设置、面板尺寸保留。此项按用户此前“只用 JetBrains Mono 13”与本次统一入口要求实施，三份产品/界面规范已同步裁决。

实际执行脚本的 CDP Debugger 哈希检查确认，默认 profile 与临时 profile 均执行同一份 mockup.js、live-data.js 和 bridge.js，不支持当前存在另一套缓存界面的推断。product-alignment 目录记录隔离/默认 profile 的 Commit 与 Settings 四分类截图及相同几何；real-default 记录没有 --settings-file、--user-data-folder、--scene 的真实默认入口；bundled-default 进一步去掉 --web-root，验证默认随包资源。真实默认入口与随包入口的主窗及五个页面度量一致，24 个输出资源逐字节一致。页面截图来自 CDP，不证明系统阴影。

设置迁移14项、宿主设置21项、格式检查、共享资产、语法检查及 git diff --check 通过；Debug构建0警告0错误。第一次真实入口检查因脚本重复点击已打开的 Commit 将其收起而超时，修正成只在不可见时打开后通过；这个失败不算产品通过证据。只检查上述页面，不能宣称55页与所有模态均完成验收。

本批PID14988/10940/10228/25772/17636/4308/18228、端口57455/57338/60846/65135/54381/56361/63345及各自临时目录均已清理并独立检查。真实默认配置已正常保存 appearanceVersion=1。启动仍可使用下文脚本，停止正常关闭窗口或按启动输出的PID执行taskkill；产品行为已不依赖脚本预处理。

当前运行时MD5：mockup.js=612E9976704343ABE1BB8778B077508F（前值FCEA8C42FDA8154FA07728BAB2B753E6）；mockup.css=A642041A37905E7BAD4ACCB79565CD60；live-data.js=8A39A72F1D0F8FC7D53D3F158205B2C7；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。后三者本批未改。

以下为此前仅对齐脚本的历史记录，由本节补充；不能用其“未修改产品”或“字体列表未实施”描述本批结果。

用户确认此前看到的较好窗口是蓝色 A 的 Augit，并在本次三组对照中选择第一组（普通 DPI、界面字号 12）作为接近的外观。不能据此认定所有历史测试窗口都使用同一组参数。

## 查证结果

同一 Debug 程序、仓库 web、工作区和配置副本下，普通入口与显式 main-project 入口截图逐像素一致。text-viewer 打开 Git Log 后，12 个区域的几何、字体和颜色度量一致；焦点、选择等状态没有完全统一，不宣称整图相同。三个入口加载的 mockup.css、mockup.js、live-data.js 哈希一致。

部分历史审计脚本（dump-live-dom.ps1、compare-pixels.ps1、verify-window-chrome.ps1）固定添加 --pixel-exact，将栅格比例设为 1；普通入口实测 DPR 为 1.75。这是实际存在的测试条件差异，但尚不能证明用户记忆中的窗口就是这一模式。

| 条件 | 标题栏物理高度 | 树行物理高度 |
| --- | ---: | ---: |
| 普通 DPI，界面 12 | 77 | 42 |
| 强制 100%，界面 12 | 44 | 24 |
| 普通 DPI，界面 14 | 96.25 | 52.5 |

用户上传原图标题背景为 y=0–95，与第三组标题高度接近。字号联动控件缩放能够复现这一比例，但没有截图当时的运行配置，不能断言当时字号一定是 14。当前磁盘配置原为界面 12、Cascadia Mono 13、仅打开 mockup.js，与上传截图并非相同会话。此前仅以旧构建或字体解释全部差异并不充分。

原始对照及度量保存在 artifacts/startup-path-investigation-2026-10-15，screenshot-session 为三模式、ui14-session 为界面 14 的额外反证。采集使用 CDP 截图，不用于证明系统阴影或完整桌面像素验收。

## 本次落实

- 新增 tools/start-augit.ps1，日常启动先构建当前 Debug 程序，显式加载仓库 web 和工作区；不带审计场景、强制 DPI 或临时 profile。
- 按用户选择，一次性将真实配置对齐为 Light、Microsoft YaHei UI 12、JetBrains Mono 13。其他字段保留，原始文件逐字节备份为 `%LOCALAPPDATA%/Augit/settings.json.before-startup-alignment-306c58a6cd2f4f70a5effcb1827be154.bak`。
- 普通启动不覆盖偏好。只有显式 -AlignAppearance 才再次对齐；已有 Augit 运行时拒绝写配置，防止旧窗口覆盖新设置。
- 此次没有修改产品渲染逻辑。设置页字体列表收敛仍未实施；原有像素整改目标仍未完成。

从任何工作目录启动：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\github\Augit\tools\start-augit.ps1
```

停止时正常关闭窗口；必要时使用启动脚本输出的本实例 PID：

```powershell
taskkill /PID <启动时输出的PID> /T /F
```

## 验证与清理

`tools/audit/start-augit.test.ps1` 验证普通入口保留偏好、显式对齐、会话及额外字段保留、原文备份、重复执行。三组采集的资源一致性和同 DPI 入口几何断言通过，compare-startup-paths.cjs 语法检查通过。Debug 构建 0 警告、0 错误。

Windows PowerShell 5.1 首次验证暴露中文脚本无 BOM 的解析问题，两个新增 PowerShell 文件已按仓库编码检查要求改为 UTF-8 BOM、LF；5.1 复验及全部 19 个 PowerShell 文件编码检查通过，git diff --check 通过。

新入口真实启动 PID1548，命令行为当前 Debug exe 加 --web-root/--workspace，无审计覆盖；正常关闭成功。七个对照进程 PID23628、20688、15512、21880、1396、3168、9572，七个 CDP 端口及全部 Augit-startup-compare-* 临时目录均已检查无残留。配置测试临时目录已删除。原配置备份及 artifacts 证据是保留交付物。

后续视觉验收必须包含这一日常入口及实际保存配置；100% 审计通过不能代替 175% 日常入口验收。

公共设置契约变更后的模块回归：Infrastructure全214项、Shell全194项通过；运行耗时分别45秒、24秒。未运行无关Core全量。再次确认本批Augit与专用WebView2进程无残留，git diff --check通过。
