# 背景换图加载确认（2026-10-17）

本批修复了背景刷新应答早于图片加载的协议缺口；正常入口四次完整刷新仍超过500ms，307帧中仍有边缘变化，整体目标未完成。没有新增功能或页面，没有重复已否证的裁切、内缩、重叠换图和预创建实验。

## 根因与实现

`ShellModalHost.Snapshot.ReplaceAsync()`原先只等待`ExecuteScriptAsync`设置新图片src，未等待该图片onload；网页收到`window/modal-refresh`应答后就可以恢复表单并撤盖。现在先按版本登记加载等待，脚本提交后等待对应加载消息。旧版本不会误报当前版本成功，脚本失败、导航失败及关闭均结束等待。

等待在原窗口线程同步续接；清理字典后才唤醒，允许续体重入关闭。`ShellModalOperations`串行保护刷新，初始图先通过Ready才交接。后续检查发现Offer在失败后同图重试时可能返回null，故最终背景刷新直接Add并按LoadedRevision判断是否重发；相同已加载图不重发，失败同图重新等待。原有图片队列仍保留给其它使用方。产品保留原有版本队列与覆盖顺序，不加固定延时或线程池续体。onload只证明图片已加载替换，不等于桌面合成已呈现。

修改：`src/Augit.Shell/ShellSnapshotImages.cs`、`src/Augit.Shell/ShellModalHost.cs`；测试：`tests/Augit.Shell.Tests/ShellSnapshotImagesTests.cs`、`tools/audit/snapshot-presentation.test.cjs`。新增测试与产品同批添加，未执行修前红测试，不宣称修前测试失败。宿主接线测试是源码契约检查，实际换图JS在VM执行；两者不能代替真实COM故障注入。

## 正常入口证据

目录：`artifacts/pixel-followup-2026-10-17/refresh-background-load-awaited/`。入口为`tools/start-augit.ps1`强制非增量构建，真实配置副本、默认Session，附CDP，非强制100%缩放。实际PID27816、端口58613，DPI168、DPR1.75、UI12、正文13；实际正文节点确认随包JetBrains Mono，网页执行哈希与源文件一致。终端未打开，不外推其字体。

| 次数 | 首轮观察ms | 最后撤盖ms | 背景刷新请求ms | 最终加载/图片版本 | 遮盖次数 |
| --- | --- | --- | --- | --- | --- |
| 1 | 653 | 648.6 | 138.9 | 1/1 | 1 |
| 2 | 543 | 527.2 | 123.7 | 2/2 | 1 |
| 3 | 523 | 508.4 | 121.7 | 3/3 | 1 |
| 4 | 537 | 531.2 | 121.4 | 4/4 | 1 |

所有数值来自events.json和complete-requests.json。此前正式实现最后撤盖631.1/518.7/569.1/502.9ms，当前没有稳定提速证据。当前遮盖捕获188.30/140.95/123.72/133.19ms；复用后ready仍35.06/32.27/50.91ms。不得把等待加载修正称作500ms达标。

完整585×252原始录像307帧，最大帧间隔64ms，SHA256=`02AC4307A3E021FD7D385595A30CF1C482080E90883E0D454FBC590B1F609805`。RGB容差2，最差第74帧、1.237秒，265变化像素；目视并重新计算得到输入光标矩形[218,107,221,133]内78像素，其余187像素范围[1,1,165,252]。没有裁掉边缘或排除像素，fullPresentationVerified=false。原始帧、完整分析及分类JSON均保留。

实际映射Shell SHA256=`954D7E396653735469C9D57C5C2E414E175A4E9D2820714B46037448F92572FE`；Infrastructure=`1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80`。实例仍加载Nahimic，因果未证，依产品规格只能作诊断性能数据。Debug已更新，Release未更新。

四网页MD5前后相同：mockup.js=`F24517B92AC7A46D6343F3E214C333B2`；mockup.css=`00FA3357EC646E91030A9DFDD925DAA9`；live-data.js=`DE85823861C020608F28DE997197B555`；bridge.js=`F297BF8769E249CA0D804F4F4EEEF5A9`。

## 定向验证与清理

- 图片版本、准备及操作队列最终26项通过（91ms），含加载续体同步关闭和失败续体重入。
- 实际换图JS、两帧呈现及宿主接线6项通过（128ms）。关联刷新/消费/重绘20项在本批此前通过（1.155秒），未无故重复。
- C#修改文件格式检查通过；9项共享资产、Debug25资源镜像通过。仅运行受影响模块，没有全量测试。
- 独立检查cleanup-independent.json确认PID27816、前景监视器26448、录屏11884已退出，58613无监听，Augit-daily-refresh-UnnFAF已删除。
- 历史参考PID21240及Augit-remote-insets-a8012f834fd543eba6a3651abc8be86c目录：用户本次明确授权只清理这两者。核实进程路径及创建时间后，清理仍被自动审批以blocked by policy拒绝，命令未执行。不得据本批资源已清宣称全局零残留；不更换通道绕过。

## 复验与后续

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关闭；异常只终止本次核实的实例。
taskkill /PID <本次PID> /T /F
node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新证据目录> --git-fixture
dotnet test tests/Augit.Shell.Tests --no-restore --filter 'FullyQualifiedName~ShellSnapshotImagesTests|FullyQualifiedName~ShellSnapshotPreparationTests|FullyQualifiedName~ShellModalOperationsTests'
node --test tools/audit/snapshot-presentation.test.cjs
```

设置秒级慢开及无CDP完整开关、背景透明/边缘帧与500ms、55页全模态同态、终端/冲突完整逐行、完整阴影继续未完成。Windows10排除本轮，跨屏DPI保留未验证。没有暂存或提交。

## 失败同图重试及最终复验

后续生命周期核对发现：加载失败后图片Hash已经更新，但LoadedRevision未更新；再次刷新相同PNG时，旧Offer会返回null，错误地跳过等待。最终改用串行背景操作的Add返回版本，与LoadedRevision比较，未加载时重新提交并等待。已加载同图仍不重发。新增失败同图恢复测试，图片/操作队列15项通过64ms，接线及实际脚本6项通过125ms；准备模块沿用本批12项已通过结果，合计27项Shell覆盖，没有重跑全量。两项目修改文件格式检查通过。真实COM图片失败尚未注入，不声称该故障路径已实机覆盖。

最终正常入口目录`refresh-background-retry-final`，PID20872、端口52025。首轮观察663/579/548/515ms，最后撤盖629.0/567.9/542.8/504.9ms，每次一链，最终加载/图片版本1/1至4/4；仍超500ms。实际映射Shell SHA256更新为`94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46`，Infrastructure与四网页不变；默认Session、配置副本、DPR1.75及实际JetBrains Mono均再次核对，仍有Nahimic映射。

最终录像完整585×252、305帧、最大间隔67ms，SHA256=`3A36EF0F500C223CD79877BCEAC679C343CC54284A8B7B58E443D83C3604FB66`；第70帧1.208秒最差265像素。目视及独立分类仍为光标78、其余边缘187，范围[1,1,165,252]，排除像素0，不通过无闪。完整证据没有覆盖前一批。cleanup-independent.json确认20872、监视器20256、录屏23408均退出，52025无监听，Augit-daily-refresh-qQWesF目录已删除。

## Nahimic对照受限

用户明确批准临时停止Nahimic服务并在测试后恢复。只读核实唯一服务NahimicService，原State=Running、StartMode=Auto、PID5848、程序C:/WINDOWS/system32/NahimicService.exe。尝试使用Stop-Service按服务名停止时，系统拒绝打开该服务（Cannot open NahimicService service on computer '.'）；对照脚本没有启动。finally核对原状态仍为Running/Auto、同一PID5848，无需恢复，没有改变系统启动方式。

原始before.json、failure.txt、after.json在`artifacts/pixel-followup-2026-10-17/settings-nahimic-service-control/`。这属于服务访问权限阻塞，与参考清理被自动审批拒绝是不同问题。需要有服务管理权限的用户提供临时停止后的环境或授权的提权执行能力，才能继续干净环境对照；当前没有该证据，不能断言Nahimic是根因，也不能计性能验收通过。继续其他已授权工作，goal保持active。
