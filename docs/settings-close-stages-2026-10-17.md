# 设置正常关闭阶段诊断（2026-10-17）

## 结论与范围

补齐了正常 WM_CLOSE / WM_DESTROY 路径的原生阶段导出。此前只有显式 Dispose 路径导出关闭阶段，正常网页 Cancel、OK、Escape 和标题关闭不能由旧阶段文件证明控制器实际释放。本批不改变显示、关闭、等待或线程调度顺序，不引入依赖，不宣称慢开、闪烁或整体关闭卡顿已解决。

沿用563项混合修改、分支dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；未回滚、暂存或提交。远端实机调整及保留Push URL的用户批准继续有效。Windows10排除本轮，跨屏DPI未验证。

## 修改与反例

- ShellWindow.cs：正常关闭记录请求、关闭开始、所有者释放、销毁开始、控制器关闭和销毁完成，最后沿既有后台CreateNew机制导出。
- ShellWindow.Settings.cs：所有者恢复分解为开始、启用、cloak、DwmFlush完成和前景激活；不调整原有调用顺序。
- settings-hidden-startup.test.cjs：新增正常关闭分支契约；修前因缺失close-start失败，修后通过。
- analyze-settings-close.cjs及测试：每个设置窗口只读最后一份阶段文件，缺失销毁、时间倒序和无效时钟均拒绝；不能用旧窗成功掩盖新窗证据缺失。原生标题先请求取消的记录按网页确认之后的关闭序列分析。

诊断仍有每窗64条限制；长交互可能填满，分析器会明确报告关闭证据不完整，不能视作产品关闭失败。导出异步且不阻塞关闭，进程立刻退出可能丢失导出。

## 正常入口实际结果

证据根目录为artifacts/pixel-followup-2026-10-17/。两批均执行tools/start-augit.ps1强制非增量构建，真实配置副本、默认Session、系统DPI；附CDP驱动，不能代替无CDP桌面完整验收。

|批次|实际结果|
|---|---|
|settings-close-stages，PID13212，端口59242|三次呈现302/278/418ms；每次8个重复请求保持同一HWND。正常关闭原生耗时33.46/26.55/49.15ms；控制器释放16.55/16.80/30.64ms。每窗均有presented和destroyed两份独立导出。|
|settings-close-regression，PID3212，端口62433|八类回归通过，共10次开关；Cancel/OK/Escape/标题关闭驱动观察48/191/52/65ms。主题取消、持久化失败重试、延迟保存后重开、Apply重开通过；无网页错误。呈现303–485ms；原生关闭29.60–53.34ms。|

两份close-analysis.json分别核验3和10个窗口完整阶段。计时从宿主收到window/settings-close到destroyed，不含网页保存、驱动派发和最终桌面呈现；子阶段有包含关系，不可简单叠加。此次未复现秒级关闭或慢开，不抵销历史无CDP反例。完整回归含保存应答延迟、隔离文件不可写等显式故障测试，不能将这些条件混作普通性能样本。

首批运行身份：settings-runtime-identity.json与main-runtime-identity.json核验实际执行脚本对源码哈希、虚拟主机CSS及字体字节；设置搜索文字实际Microsoft YaHei UI、15glyph，DPR1.75、Light、UI12、配置正文JetBrains Mono13。正文字体仅在存在节点时取样，不外推所有页面。Debug DLL SHA256为53D7FB2553EA41C4C405425D48995A9312ECBC3DBEA8CC980AB64A5868DC94D5；这是启动器目标磁盘身份，本批未额外保留进程模块路径快照，不能写成模块加载身份完整通过。

四网页MD5不变：mockup.js=661C7570EB5805BB8759F2B2C6DF169F；mockup.css=6E79CBFE5FA5629AC900EAF05D109A43；live-data.js=D64AB5C4E97AA7D3ED60F456555A34AE；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。Debug更新，Release未更新。

## 分批验证与资源

9项设置启动/关闭契约通过141ms；17项SettingsStageTraceTests通过174ms；4项关闭分析器测试通过128ms。分析器首轮正则转义错误由测试检出，修正后通过。两次正常启动构建、C#定向格式检查、9项共享资产和git diff --check通过；未频繁全量测试。

两批脚本finally已停止各自实例树并删除Augit-independent-settings-xDkqFq和Augit-independent-settings-NzaUMj。独立检查PID13212/3212均不存在、59242/62433无监听、两目录不存在；cleanup-check.json保存核验。历史因自动审批拒绝保留的参考目录未触碰。原始JSON作为证据保留。

## 未完成与下一步

无CDP设置owned子窗仍缺工具可用输入目标，前次路径和失败见settings-no-cdp-origin-2026-10-17.md；本批未重复该已知受阻实验，也未绕过输入门禁。Nahimic注入因果未证实，依产品规范仍不能做最终性能验收。

背景刷新首次614ms仍未解决。核对到裁切捕获已在pixel-refresh-after-reopen-2026-10-16.md由真实系统截图否证：内部仍差2987像素，不能为了约25ms收益再次直接采用。未更改刷新逻辑。下一步应补实际进程模块路径记录、无CDP有效输入条件，并继续其它授权像素配对；55页全模态、终端/冲突完整逐行和全阴影仍未完成。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关闭；异常仅停止确认属于本次的PID。
taskkill /PID <本次PID> /T /F
# 离线复核现有证据，无需重新启动产品。
node tools/audit/analyze-settings-close.cjs artifacts/pixel-followup-2026-10-17/settings-close-stages
node tools/audit/analyze-settings-close.cjs artifacts/pixel-followup-2026-10-17/settings-close-regression
```
