# 工作区变化串行消费（2026-10-17）

本批按用户最新方向优先处理刷新正确性，暂停复杂Git页面及冲突像素精修。未新增产品功能、页面或外部工具入口，未提交或回滚既有混合修改。整体目标仍未完成。

## 缺陷与修复

`drainWorkspaceChanges()`此前每收到一次推送或兜底通知就独立读取并应用宿主批次。宿主原子取走队列不能保证网页应用顺序：上一批仍在读取状态或文件时，下一批可以同时执行，旧结果可能晚到覆盖较新状态。

新增可控异步回归扣住第一批应用；三次后续通知使读取计数从2增到5，修前断言失败。现在从取批次到完成界面应用串行执行；在途通知只登记下一轮读取，不重放事件载荷，也不按文件路径去重。失败仍报告错误，保留等待中的唤醒并解除运行标记。修后顺序、同路径连续修改、丢失推送兜底和失败恢复均通过。此修复保证本消费入口的顺序，不宣称所有其他状态读取入口已统一排序。

修改文件：`web/src/live-data.js`、`tools/audit/workspace-change-delivery.test.cjs`。同时纠正旧注释中“无定时唤醒”及用50毫秒监视合并窗口推断500毫秒总延迟的不实描述。

最终live-data.js源文件MD5为`3C4854B89FBB9442824847D44976A380`；实机采集时为`30D403365C0146546D312BB303981E3F`，两者仅上述中文注释不同。最终语法与文档声明检查通过；文档检查仍提示历史全量快照过期，不把它计为本批全量验证。

## 正常入口复验

证据目录：`artifacts/pixel-followup-2026-10-17/refresh-serial-delivery/`。正常启动器强制非增量Debug构建，真实配置副本、默认Session，附CDP及系统录屏；未强制100%缩放。PID20996、端口61592，实测DPI168、DPR1.75、界面12、正文13，实际正文节点加载随包JetBrains Mono。终端未打开，不外推终端字体。

执行网页SHA256为`5d8b4bd1d309d457070a0a9d25ed7b1bd0be6f3cbf318f75cd1652e9184d3df8`（后续仅修改中文注释，因此下一次执行身份须重新核对）。Shell实际映射保持`94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46`。完整产物、映射、配置和字体记录分别见runtime.json、mapped-images.json、process-identity.json和launch.json。Release未更新。

四次首轮观察为641/532/551/567毫秒，最后撤盖为636.4/523.7/540.3/555.8毫秒，每次一条遮盖链，仍超过500毫秒。前批629.0/567.9/542.8/504.9毫秒，不能宣称稳定提速。本次正常场景证明修后流程可运行；批次竞态由定向回归证明，不将四次顺序文件写入冒充实机并发压力验证。第三方注入环境限制仍在，不计干净环境性能验收。

完整585×252录像312帧，最大间隔45毫秒，RGB容差2，未裁边、未排除像素。最差第76帧1.295秒有187变化像素，范围[1,1,165,252]；已目视最差完整帧，不能把边缘变化忽略为通过。录像SHA256为`841b2ef8c0ce8c1b30fb56c68218092a4a38cda7cfd5f2be8ea603cf9a8fabde`，fullPresentationVerified=false。设置慢开及完整开关、背景闪帧、全场景与阴影仍未完成。

## 验证与资源

- 变化消费、区域渲染及正常刷新审计共22项通过，耗时1.178秒；新增竞态测试已取得修前失败及修后通过。
- JavaScript语法、9项共享资源一致性与git diff --check通过，没有运行全量测试。
- 正常跳转行关闭后snapshotCount=0、captureCoverWindow=0、ownerEnabled=true，网页错误为空。
- 独立检查确认Augit20996、前景监视20548、录屏10580已退出，61592无监听，Augit-daily-refresh-Lx8P0T已删除。分析工具已退出。历史参考清理与Nahimic服务权限阻塞不受本条覆盖。

## 复验命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关闭；异常时只结束本次已核实实例。
taskkill /PID <本次PID> /T /F
node --test tools/audit/workspace-change-delivery.test.cjs tools/audit/workspace-change-render.test.cjs tools/audit/refresh-daily-file.test.cjs
node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新证据目录> --git-fixture
D:/Python/Python3119/python.exe -B tools/audit/analyze-static-modal-video.py artifacts/pixel-followup-2026-10-17/<新证据目录>
```
