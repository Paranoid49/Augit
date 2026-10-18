# 外部正文重读合并渲染（2026-10-17）

本批消除正常入口文件变化的一次重复区域刷新。修前每次真实文件变化导致editor-area两次子节点替换，修后一次；四轮均减少43项DOM变更。完整刷新仍超过500毫秒，仍有边缘闪帧，不能登记性能或无闪通过。复杂Git与冲突精修保持暂停，没有增加功能或页面。

## 根因和修改

applyWorkspaceChanges调用openDocument(path, {activate:false})成功后，openDocument先排队刷新侧栏、正文、标签、状态栏和标题栏；随后applyWorkspaceChanges读取目录并再次刷新正文等区域。普通文本正文已由reuseTextViewInPlace复用，所以不能把这两次调用说成正文控件被创建两次；实际重复替换发生在标签等区域，也导致额外绑定与DOM变更。

web/src/live-data.js新增内部deferRender选项，仅外部变化批次使用。成功读取仍更新文档状态和既有标签，然后由批次收尾统一按实际变更区域渲染。用户主动打开保留原事件后刷新及等待契约；错误路径仍即时反馈、清理pendingDocument。删除判断仍用本批最新Git状态，消费串行逻辑不变，不改变捕获等待或原生显隐。

新增document-batch-render.test.cjs执行真实函数，修前批次统一渲染用例失败，修后后台成功、普通打开及失败路径3项通过。workspace-change-render回归明确验证调用方传入deferRender。refresh-daily-file只读诊断新增节点类型计数并补覆盖，不采集正文。

## 正常入口对照

三批证据位于artifacts/pixel-followup-2026-10-17，各自由tools/start-augit.ps1强制非增量Debug构建、真实配置副本、默认Session和正常DPI启动，附CDP。实际DPR1.75，界面Microsoft YaHei UI12、正文随包JetBrains Mono13；runtime.json、process-identity.json、mapped-images.json核对实际加载。

| 目录 | 每轮editor-area子节点变化 | 每轮DOM变更 | 最后撤盖ms |
| --- | --- | --- | --- |
| refresh-document-render-before | 2/2/2/2 | 170/133/133/133 | 624.0/534.4/517.4/518.4 |
| refresh-document-render-after | 1/1/1/1 | 127/90/90/90 | 605.0/535.5/543.5/531.0 |
| refresh-document-render-burst | 2/2/2/2（每轮真实两批） | 173/136/136/136 | 621.6/529.2/517.3/545.1 |

连续写入四轮均证明第二次写入发生在首批git/status在途时，两次状态请求串行，最终正文为2/4/6/8。所有批次正常Esc关闭，快照与遮盖释放、所有者恢复可用，网页错误为空。render-comparison.json保存统计，不以总MutationObserver数量推断实际屏幕帧数。

修前及修后完整录像各312帧、585×252，RGB容差2，最大间隔38/46毫秒；最差265/187像素，修后原始PNG已目视。修前265包含78光标加187边缘，不能把265变187当成闪帧修复。连续写入录像311帧中第56/57帧时间戳同为0.976秒，被完整帧工具拒绝；analysis-rejected.json记录具体问题，未去重、重采样或降低门槛。该组功能/桥接证据有效，像素验收不通过。

最终live-data.js MD5由51F31F911876565442A0FA444FF0E84F变为57A689BAAFB9B22608F29526B12CEFE6，实际执行SHA256=554480C5C11DEA839DDA25413ADF6A51E431B160C6BABD5C0AB20AF023A387BE。Shell保持94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46，其他三网页未改，Release未更新。

## 测试和清理

26项读取/刷新/消费/审计定向通过（约1.18秒），审计补充节点计数后7项再通过（约1.17秒）；另5项同值/范围回归通过（约1.51秒），共31项不同用例。9项共享资源一致、JS语法及git diff --check通过，无全量测试。

各批cleanup-independent.json确认Augit9388/17468/24976、录屏9588/20900/11364退出，端口58248/62212/60155无监听，Temp下Augit-daily-refresh-nidvex/dwA3kA/EinWmm已删除。monitor-cleanup.json确认前景监视19684/26672/26072退出。分析命令已结束，无本批服务器残留。历史参考清理自动审批阻塞和Nahimic权限阻塞保留。既有混合修改未回滚、覆盖、暂存或提交。

## 接续与启停

本批收益仅为消除重复区域刷新，没有稳定总耗时改善，也未解决此前正文露出与撤盖后边缘闪帧。下一步仍需定位原生合成，而非重复已否证的显隐、裁切、内缩、额外CapturePreview或固定等待实验。设置慢开、无CDP完整开关、55页/全模态、终端与完整阴影未完成；冲突及复杂Git精修暂停。Windows10排除本轮，跨屏DPI未验证，整体goal保持active。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关窗；异常仅针对本次核实PID：taskkill /PID <本次PID> /T /F。

复验：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --trace-mutations；连续变化加--burst。证据目录必须新建，不覆盖已有输出。
