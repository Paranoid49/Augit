# 设置启动重复工作整改

日期2026-10-16，分支dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。前轮关闭阴影修复作为当前基线，本轮继续首次开窗延迟；未暂存、提交或回滚混合修改，整体目标仍未完成。

## 实施

独立设置启动原先先settings/read建立状态，装配表单后loadSettings又执行一次settings/read。现在loadSettings允许复用本次刚读取的初值，独立设置只读取一次；主窗启动、关闭通知和提交通知仍默认回读宿主，不缓存后续配置。

共享mockup.js原先装配独立设置空容器后立即执行字体度量，表单装好后又由loadSettings执行。现在仅独立设置跳过空容器度量，仍在完整表单装配后等待字体加载和测量；普通页面保留原初始化。docs/ux-mockups与web/src的共享文件已同步，没有修改字体、字号或布局令牌。

宿主新增控制器创建开始、控制器创建完成、导航开始三个单调时钟诊断点，与已有表单准备／渲染／呈现时刻一起输出。只有设置子窗记录，不改变等待顺序，不提前揭示未绘制窗口。审计从新页面创建时记录请求，逐次断言settings/read一次、字体ready、settingsReady和时间单调性。

## 真实验证

证据根artifacts/pixel-followup-2026-10-16：

| 目录 | 开窗次数／宿主创建至呈现 | 首次 | 关闭Cancel／OK／Esc／标题 |
| --- | --- | --- | --- |
| settings-startup-single-read | 10次，298–488ms | 326ms | 51／157／73／71ms |
| settings-startup-single-read-release | 10次，317–494ms | 317ms | 77／30／49／58ms |

Debug源网页和Release随包均八组功能通过、网页错误0。每次配置读取均为一次；重复入口创建期间与显示后保持同一窗口、保存失败重试、旧保存应答取消重开、Apply重开等原有验证保留。以复制的用户配置和隔离profile运行，不修改真实用户设置；两批分别从Dark／Light开始，后续有主题切换。

Debug控制器创建38–65ms、导航开始至表单准备101–196ms、表面渲染同步93–117ms；Release分别38–68ms、131–200ms、89–119ms。不同阶段均由同一宿主时钟计算，未把CDP发现页面时间与宿主时钟混合。测试仅两个应用实例，不能据此认定历史首次1540ms或更早秒级慢开已彻底解决，也不能把全部差值归因于这两处优化。

Release系统截图因前景PID不符被严格拒绝，未绕过门禁。first-layout-cdp.png仅为渲染器布局观察，已目视确认表单、搜索框、字体、按钮完成；不是系统截图、首次实际Present或闪烁证明。本轮未重录全帧视频，前轮关闭阴影录像不能直接扩张为本次新版本全部呈现通过。

## 测试、哈希与资源

JS定向测试覆盖初读复用、后续仍读取最新值、字体变化再度量、普通页面保留初始度量、独立设置装配顺序；最终修改后8项通过，入口单飞2项于本批前段通过。首次新增测试有期望字符串漏写结束符的笔误，修正后重验，不计首次失败为通过。Shell定向15项通过（49ms）；Debug／Release强制构建均零警告零错误，两套各24文件镜像一致，共享9资产一致，C#格式、JS语法及git diff --check通过。未高频重复Shell全套或解决方案全量测试。

四网页MD5：mockup.js从5148D9CDA4D61A8F11D0F125407918B8变为6A091BAA783027FBE2D040A48F7A737B；live-data.js从3216B5CDC0FE86166B87389B7D6B3954变为0F266297202DF61DBEB9D011211FC5FA；mockup.css仍D5342D22C245695A57F93BC90A1DC7ED；bridge.js仍F297BF8769E249CA0D804F4F4EEEF5A9。

本轮实例2736／27956及端口61194／52313由驱动finally清理，临时目录Augit-independent-settings-35cYbk／Augit-independent-settings-82TY2u由同一驱动回收。删除settings-startup-single-read-release/early-continue的命令被自动审批以blocked by policy拒绝，未执行、未重试；该标记保留。历史被策略拒绝的临时标记未重试。证据原图、JSON和审计代码保留。

```powershell
# 正常入口；已同步Debug与Release，无需使用审计参数。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 正常关闭窗口；异常时只停止启动器打印的本次PID。
taskkill /PID <本次PID> /T /F
# 定向复验自动退出并清理隔离配置。
node tools/audit/independent-settings.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/settings-single-read-manual-new --release
```

55页／全部模态同态、其它模态背景刷新、终端及冲突逐行、剩余系统阴影继续未完成；Windows10按用户决定排除，跨屏DPI保留未验证。
