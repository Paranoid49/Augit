# 冲突工具栏图标整改

## 禁用状态接续

2026-10-16本批修正冲突工具栏禁用图标误用Label.disabledForeground的问题。仅新增作用于conflict-tool的浅深禁用颜色，正文、文本按钮和焦点布局不变。浅色#BDBDBD，深色#5A5A5A；普通启用仍使用icon语义色。视觉稿、运行时及Debug/Release随包CSS已同步，正常启动使用同一规则。

来源为已安装PyCharm2026.2.1的intellij.platform.ide.impl.jar：ManyIslandsLight继承ExperimentalLightWithLightHeader→ExperimentalLight→IntelliJ，grayFilter为33,-35,100；ManyIslandsDark继承ExperimentalDark→Darcula，grayFilter为-70,-70,100。中间ID与路径由META-INF/PlatformExtensions.xml核对。直接用随包JBR运行intellij.platform.util.ui.jar中的GrayFilter，输出light=FFBDBDBD、dark=FF5A5A5A；取证源码保留在artifacts/pixel-followup-2026-10-16/conflict-disabled-layout/GrayFilterProbe.java，避免将本地263分支源码误认作262安装版。浅色原始reference-initial-clear.png禁用上箭头区域存在#BDBDBD纯色。深色是安装版计算证据，仍缺相同状态的系统截图，不宣称整窗或栅格完全复原。

tools/verify-ux-conflict.cjs合并新增禁用色、SVG描边、禁用悬停/焦点消失、恢复启用及命中区不变断言，72组一次通过。一次真实外壳夹具conflict-disabled-live通过，toolbar.json记录Previous禁用RGB189、Next启用RGB108/112/126，interaction.json记录接受/忽略、撤销重做、编辑及逐字节保存、索引解决成功；nativeWindowsImeVerified仍为false。initial.png已目视，但采于首行焦点规范化前且带CDP外围黑区，不作为合格系统配对。未重复全量测试、没有C#改动或重建。

9项共享资源、Debug/Release各24个资源镜像、相关JS语法、文档声明检查和git diff --check通过。CSS MD5由16A3071D8EB381CEEE8E5B7BDF339026改为06EA2451420B1F14DB2A16DB3BAD7AA9；JS三项沿下文前批最终值。PID22424已退出，端口62966无监听，Augit-merge-pair-fNwu0g临时目录及专用WebView2均无残留；浏览器布局测试finally关闭。Java临时验证与删除组合命令被自动审批拒绝、未执行，未重试删除；改用保留的证据源码直接运行成功。没有触碰历史被拒绝清理目录。

启停仍使用本文末尾命令；布局与真实取证命令沿原参数，把输出目录改为本批conflict-disabled-layout/conflict-disabled-live。整体四项目标继续保留，本批不增加55页合格计数。

日期：2026-10-16。沿用dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。既有混合修改保留，未暂存或提交。上一轮取得三状态系统图并验证导航生效，属于取证进展；其补采仍有鼠标光晕，本轮实际查看后确认不能登记为无遮挡整窗配对。

## 证据与修复

参考conflict-reference-clean/reference-initial-clear.png在下一处箭头区域[82,95,106,122]存在纯色#6C707E；上一轮conflict-navigation-clean/actual-initial.png相同区域为黑色。产品冲突按钮沿用secondary-button，继承正文色；其箭头还独立使用缩短的路径，没有复用普通Diff已采用的共享箭头。

本轮mockup.js将上下导航改为已有icon函数的arrow-up/arrow-down；其它动作保持现有功能及形状。mockup.css将启用冲突工具按钮绑定既定icon语义色：浅色#6C707E、深色#C3C5CB。对应设计系统§6的既有规则，不新增字体、调色板或依赖。禁止复制JetBrains品牌资源的边界不变，复用项目内既有图形。两个共享文件同步视觉稿，并同步Debug/Release随包资源。

禁用图标目前仍沿既有faint颜色，参考截图呈灰阶且与该值不同，尚未完成禁用滤镜和栅格的定量复原。本轮不宣称所有工具图标、所有状态或整体窗口一致。

## 验证

- tools/verify-ux-conflict.cjs新增共享图形身份、浅深颜色、描边继承及图形不超命中区断言；本模块72组通过，覆盖主题、DPI、独立字号及布局。未运行产品全量或重复51项功能模块。
- 一次真实Win32/WebView2浅色夹具验证通过，记录于artifacts/pixel-followup-2026-10-16/conflict-toolbar-live。toolbar.json确认首行下一处可用、共享arrow-down、颜色和描边均rgb(108,112,126)。interaction.json确认接受、忽略、撤销重做、编辑、逐字节保存及索引解决通过。
- initial.png已目视；它是CDP客户区截图，包含外围黑区且采于后续首行焦点规范化之前，不能和1730×1215参考系统图直接比较或证明系统阴影。
- 两套各24文件资源镜像、9项共享视觉资产及相关JavaScript语法检查通过。没有修改C#，没有重新构建。

## 前批补采与清理

conflict-navigation-screen与conflict-navigation-clean都已落盘三状态系统图；本轮查看clean初始图仍有鼠标光晕，不能按目录名判断取证合格。前批Computer Use因浏览器URL识别失败停止，本轮没有重试桌面操作或绕过限制。主窗前景检查点已接入冲突采集，两个已结束驱动成功走完，但没有因此提高整窗配对计数。

本轮PID20148、端口57629、临时Augit-merge-pair-rvGnWY已退出/无监听/删除；同时复查前批PID15680/21460、53830/60280及Gi4sff/oN04O8目录均无残留。浏览器专项由finally关闭，无新增参考进程或常驻服务器。历史被拒绝删除目录未触碰。

MD5：mockup.js从6E61081C842CA96770B3F8BB56E13B0A变为A8167A59D3CBFD452683C82FA7A44D61；mockup.css从99A3D1E61C7D12BA82DD25CE2E685A76变为16A3071D8EB381CEEE8E5B7BDF339026。live-data.js=C8DA64065C77E60234F2D614334E8DC4、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9不变。

复验：

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/verify-ux-conflict.cjs $pw $chrome artifacts/conflict-toolbar-layout-manual-new
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-toolbar-live-manual-new $pw --light --reference-frame
```

日常启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`。停止请关闭窗口，异常仅对本实例执行`taskkill /PID <本次PID> /T /F`。整体55页及全部模态、背景刷新、终端/冲突逐行和系统阴影目标均继续保留，未标记完成。
