# 独立设置完整开关逐帧接续

日期2026-10-16；分支dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。承接独立设置WebView授权，未暂存、提交或回滚混合改动。本批先补齐逐帧反证，再修改设置关闭的合成与焦点顺序。

## 本轮实机证据

目录：artifacts/pixel-followup-2026-10-16/settings-final-cycle-audit。使用Debug真实外壳、源网页、实际仓库与复制的用户配置；隔离profile，主题为Dark，不强制DPI或窗口尺寸。不是未隔离的用户配置写入验收。

computer-use窗口观察未能取得稳定身份；没有依此判定应用退出。原进程25564仍在，由现有系统截图工具确认前景PID、HWND和DPI稳定，再目视原图后继续原测试。未重复启动实例。完整录像的66次前景采样只包含登记的主窗21432872和设置窗15600056，前景资格通过。

2880×1716桌面复制／FFV1录像共217原始帧，时长4.016秒，最大帧时间间隔44ms；不排除像素、不重采样。视频SHA256：6305b4e5e2e554be1d1b2106896d5f25397596e665697466fec8027d291d15cc。

8组功能回归通过、网页错误0；Cancel／OK／Esc／标题关闭分别72／139／63／73ms。11次宿主创建到呈现402–833ms。本批数据不能消除历史秒级慢开记录；CDP发现和主机计时不同，不混为一个延迟指标。

第一阶段全部217帧RGB包络诊断：71帧越界，最大6928像素。最大差异原图可见Settings提示框与按钮焦点，未见历史整块白窗。包络仍可能放过中间颜色混合，不能据此宣布无闪；继续对同一份录像计算整帧状态距离，不以重拍选择更好样本。

## 审计改动

analyze-settings-cycle.py增加--whole-state：每帧分别与开前、开中、关后三张完整原始帧比较，再选择差异最小的完整状态；不能逐像素选取不同状态拼接。淡化中间色与上下拼接反例即使落在颜色包络内，也会被整帧比较检出。保留既有包络证据，独立结果文件拒绝覆盖。合法悬停、光标和状态栏变化仍统计，不自动转成产品通过。

诊断阶段5项定向测试通过，9项共享资产一致，git diff --check通过；该阶段尚未构建，后续产品修复的构建与测试见下节。产品四网页MD5前后保持：mockup.js=5148D9CDA4D61A8F11D0F125407918B8；mockup.css=D5342D22C245695A57F93BC90A1DC7ED；live-data.js=3216B5CDC0FE86166B87389B7D6B3954；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

## 资源与命令

测试驱动已停止首轮Augit PID25564并删除临时目录C:/Users/wl024/AppData/Local/Temp/Augit-independent-settings-FjrevK。前景采样PID11884、离线分析PID25612及解码PID26936均已结束。证据PNG、JSON、MKV保留；历史被拒绝的清理目标未重试。

```powershell
# 日常启动；正常关闭窗口，异常时仅停止本次打印的PID。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
taskkill /PID <本次PID> /T /F
# 离线分析已保存录像，不启动应用或服务器。首次输出后拒绝重复覆盖。
D:/Python/Python3119/python.exe -B tools/audit/analyze-settings-cycle.py artifacts/pixel-followup-2026-10-16/settings-final-cycle-audit --open-seconds 2.7 --whole-state
D:/Python/Python3119/python.exe -B -m unittest discover -s tools/audit -p test_settings_cycle_frames.py
```

仍未完成：全部设置状态和原生拖动、其它模态背景刷新、55页全模态同态、终端／冲突逐行完整配对以及其它主题／活动状态的系统阴影。Windows10按用户决定排除，跨屏DPI保留未验证。

## 整帧反证与关闭修复

第一轮整帧比较发现第163、164帧（3.115／3.125秒）与最近完整状态相差92710像素，包络却只报1233。差异图主要位于设置下缘外侧阴影和背后的内容。说明颜色包络确实会漏掉此类异常，不再用包络数字作为完整闪帧门禁。没有把录像时间直接等同于宿主墙钟。

原关闭顺序先恢复并激活所有者，设置窗口尚可见，随后才销毁。现ReleaseSettingsOwner先启用所有者，再用DWM cloak撤下设置合成画面并DwmFlush，随后才激活主窗；cloak失败回退隐藏该设置窗口。Dispose异常清理也在关闭控制器之前执行此步骤。没有改系统动画、替换截图、移动业务WebView或建立第二个设置窗口。

复录目录settings-close-cloak-cycle：252帧、2880×1716，最大间隔46ms，视频SHA256为7338207795f425ebd795bea82f76f6bc40782b337413434fc3323641069b50c2。整帧最大差异14280，位于打开阶段提示框／搜索焦点；第197至200帧只剩状态栏更新的96像素差异，第201至251帧与最终关闭状态差异0（RGB容差2、完整区域、未屏蔽像素）。此次没有重现前版关闭时92710像素的阴影中间态。只证明本次深色Cancel流程改善，不扩张为所有主题、所有关闭路径或所有模态无闪。

修后Debug八组功能通过，Cancel／OK／Esc／标题关闭82／60／78／63ms；11次开窗中首次1540ms，其余401–445ms，慢开仍存在。Release随包入口八组通过，四条关闭139／34／56／83ms，网页错误均0。

新增关闭顺序防回归，6项JS入口／呈现测试与15项Shell定向通过。由于同时修改Dispose公共清理路径，完成修改后仅运行一次Shell模块完整224项测试，全部通过（13秒），没有运行整个解决方案全量测试。Debug／Release强制构建均0警告0错误；两套各24网页文件镜像一致，C#定向格式检查通过。产品四网页哈希不变，本次实际产品修改位于ShellWindow.cs和ShellWindow.Settings.cs。

后续实例PID23200／27124和临时目录Augit-independent-settings-F1VbkQ／Augit-independent-settings-UK3JHz由驱动finally回收；前景采样PID27388、分析PID20548及解码PID27732完成后退出。旧失败与本次成功证据均保留，不能用新结果覆盖旧失败。

收尾只读复核：上述9个进程无存活项，53951／63848／57860端口无监听，三个隔离目录均不存在。删除本轮settings-final-cycle-audit/cycle-continue与settings-close-cloak-cycle/cycle-continue的命令被自动审批以blocked by policy拒绝，没有执行或换方式重试；两个小标记保留，不能宣称临时标记全部清理。文档声明检查通过，但明确历史全量快照过期，未引用旧2373断言作为本批结果。
