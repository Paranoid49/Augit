# 设置确定关闭移除子窗回读（2026-10-17）

## 范围与原因

继续用户优先的设置关闭路径；复杂Git与三栏冲突像素精修暂停，尚未删除功能。当前分支dsh、HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；保留既有混合修改，不暂存或提交。

真实saveSettings函数在宿主确认写入成功后，不区分Apply和OK，均等待loadSettings读取配置并可能等待字体度量，然后才允许关闭。独立设置窗口即将销毁，主窗已经通过settings/committed、settings/closed回读，子窗此时的回读无必要。正常入口修前已记录settings/write→settings/read→window/settings-close。

与UX设置保存/取消约定及本地IntelliJ SettingsDialog.java第232–245行的applyAndClose顺序核对：仍必须先保存成功再关闭。修复只增加closeAfterSave参数，在独立设置OK成功后省去子窗回读；Apply和共用页面仍回读，失败仍保留草稿和窗口。未改宿主写入、主窗通知、关闭协议、原生呈现或初始化路径。

## 修前与修后证据

证据根目录：artifacts/pixel-followup-2026-10-17/。

|批次|确定时子窗请求|写入请求至关闭请求|驱动点击至关闭观察|
|---|---|---|---|
|settings-ok-no-reload-before|settings/write、settings/read、window/settings-close|24.2ms|714ms|
|settings-ok-no-reload-after|settings/write、window/settings-close|5.6ms|579ms|

两批均由tools/start-augit.ps1强制非增量构建启动，复制真实配置、默认Session、系统DPI，附CDP驱动及只读请求日志；无应答延迟注入，无测试专用产品分支。记录保存到ok-close-requests.json。单次驱动耗时包含派发和轮询，不能由714→579断言稳定性能收益，更不能计为正常无CDP关闭验收。

两批执行字号12→14→12的Apply，再修改为14点OK，主窗实际字号及持久化配置均为14，重开显示14；随后Apply恢复12并取消，最终配置与主窗恢复12，原面板值保持，无网页错误。typography-cycle.json保留检查结果。没有录像，本批不证明无闪或秒级慢开已解决。

实际执行脚本由CDP读取源体核验：修前live-data.js SHA256为55F32A4BB27BD073CE325B206543F8B6EC7E9EF5341FF8807E882B703E1D25CD；修后为F12EB39E70F82D86DF1E215468EA3C53197999764E8CF44E759EA37789A59BFC。修前MD5为E6D3B9263497E5B43E444348DAB3DC63，修后59998C04CB91C8EB470F1A2CC5C29206。其余三网页MD5保持mockup.js=0EEBB52037A8EEABDB81DA777E8C1FFE、mockup.css=00FA3357EC646E91030A9DFDD925DAA9、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

两批实际映射Shell路径指向Debug/net10.0-windows/win-x64/Augit.dll，对应磁盘SHA256=B8F2BDAAD19F9B5740DF1BEC45CE1F84C38E26233CB77FB1558180BD54CE0B62；Infrastructure为A08EB839CC37A2E71BFB5CF8C03C2BCD4FA74BE596CCECDC9D2222E12C27964B。映射路径不等同内存字节哈希。两窗DPR1.75、初始UI12，搜索框实际Microsoft YaHei UI、主窗正文实际随包JetBrains Mono，配置正文13。Nahimic AudioDevProps2.dll仍映射，未重试受阻的服务操作。Release未更新。

## 定向验证

新增settings-save-close.test.cjs执行真实saveSettings：修前6项中2项失败，修后与字体回读、主题预览、初始化关闭共23项通过，耗时192.75ms。覆盖独立OK不回读、Apply与共用页面回读、失败保留草稿、旧会话迟到保存；事件入口显式区分Apply/OK。正常入口另行覆盖真实字段、提交、主窗更新及重开，不用函数测试代替实机。

JavaScript语法、9项共享资产、UTF-8/LF及git diff --check通过。仅改live-data.js保存函数与调用、定向测试和现有审计脚本相关分支；未跑全量测试。

## 清理、复验与剩余项

修前PID5968、端口64497、临时目录Augit-independent-settings-bbKpUD；修后PID23704、端口61505、临时目录Augit-independent-settings-Tg80uO。两批finally结束实例树并删除目录，随后独立检查PID不存在、端口无监听、目录不存在，分别保存cleanup-independent.json。没有启动额外服务器；证据文件保留。


整体目标仍未完成：正常无CDP设置操作证据受输入目标限制，秒级慢开、背景透明帧与延迟仍有反例；55页全模态、终端完整逐行、系统阴影未全部完成，冲突与复杂Git精修暂停。Windows10排除本轮，跨屏DPI未验证；不把暂停当作完成。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。

正常关闭优先用窗口关闭按钮；异常只清理确认属于本次的实例：taskkill /PID <本次PID> /T /F。

复验命令（仓库根目录，实际Playwright路径按本机）：

node --test tools/audit/settings-save-close.test.cjs tools/audit/settings-typography-reload.test.cjs tools/audit/settings-preview-reload.test.cjs tools/audit/settings-startup-close.test.cjs

node tools/audit/independent-settings.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/settings-ok-no-reload-recheck --daily-launcher --single-open --typography-cycle --expect-no-close-reload
