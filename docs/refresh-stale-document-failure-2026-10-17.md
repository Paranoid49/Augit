# 正文迟到失败保护（2026-10-17）

本批修复旧文档请求失败覆盖后续文档状态的问题；不是背景闪帧或刷新性能结案。复杂 Git 与冲突精修保持暂停，移除三栏编辑器的提议尚未确认，没有删除功能或增加页面。

## 根因和修改

openDocument 的成功路径检查 documentToken，失败路径却直接清空正文、加载提示或按当前状态关闭标签。真实函数回归中，先读取 old.txt，再成功读取 new.txt，随后旧读取失败会令新正文变为 null；另一个回归证明旧失败会关闭已标记删除的旧标签。修前两项失败，当前请求失败反馈用例通过。

web/src/live-data.js 仅在失败分支写状态前补与成功路径相同的代次检查。失效请求不再动新状态；当前请求的错误、删除处理和提示清理保持。依据为 ux-spec.md §6 的异步状态所有权及迟到结果规则，不涉及参考组件外观或新产品能力。

新增 tools/audit/document-stale-failure.test.cjs 执行正式函数并控制请求完成顺序。与 document-batch-render、workspace-change-render 合计17项定向通过，约193毫秒；9项共享资源一致，git diff --check 通过。未运行全量测试。首次模块语法命令误将源码作为命令参数导致长度超限，随后改为标准输入检查。

## 正常入口证据

目录：artifacts/pixel-followup-2026-10-17/refresh-stale-document-failure。通过 tools/start-augit.ps1 强制非增量 Debug 构建，真实配置副本、默认 Session、正常 DPI，附 CDP。四次真实文件修改、后台刷新、Esc 关闭和快照回收通过；没有强制制造竞态，不能将此结果当成迟到失败的实机复现。

- 最后撤盖：657.7 / 537.7 / 545.5 / 515.2 毫秒，每轮一次捕获，仍超过500毫秒。
- 完整录像1339帧、585×252，最大间隙39毫秒，未重采样。RGB容差2的最差帧76有265个变化像素，原图已目视；时钟关联为撤盖应答后43.376毫秒。未分解本批光标和边缘贡献，不计无闪通过。
- runtime.json 确认 DPR1.75、界面 Microsoft YaHei UI12、正文实际使用随包 JetBrains Mono13。
- 实际执行 live-data.js SHA256：31964BB95E93477B66EC159588AC14B805B3339DA3BAC69EA52B8B2EFC8A1BFC。MD5由 E1EE6539727817718DBBFC752747BB46 改为 D50C01AF5311343AC7F8E03FA2E2F0FC。其他三网页 MD5 未变。
- 进程映射 Shell SHA256 仍为4CAC07952E541B48BCF069F0F7DDA4B03783A8BB4F12BF86329129D9148C9F75；Infrastructure仍为1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80。Release未更新。
- 本实例仍映射 Nahimic 模块；性能为诊断证据，不外推无注入验收。

## 清理和接续

cleanup-independent.json 独立确认 Augit27768、前景监视22388、录像7004已退出，50536无监听，临时目录 Augit-daily-refresh-pWpb8n已删除。分析进程已完成；历史参考清理审批阻塞及 Nahimic 权限阻塞未解除，不宣称全局零残留。保留混合修改，未暂存或提交。

设置慢开、无CDP完整开关、刷新透明帧和500毫秒目标、55页全模态、终端与系统阴影仍未完成。Windows10排除本轮，跨屏DPI未验证，整体goal保持active。下轮继续原生呈现与刷新延迟，不重复本批迟到失败实验。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关窗；异常时仅对本次核实PID执行：taskkill /PID <本次PID> /T /F。

定向测试：node --test tools/audit/document-stale-failure.test.cjs tools/audit/document-batch-render.test.cjs tools/audit/workspace-change-render.test.cjs

正常入口复验：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --trace-mutations --gdi-clock
