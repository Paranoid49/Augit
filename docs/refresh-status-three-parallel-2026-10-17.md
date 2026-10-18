# 状态查询三路并行（2026-10-17）

本批把 GitStatusService 内的状态、分支和 HEAD 三项独立只读查询同时启动；正常入口两批验证有局部提速，首次刷新及闪帧仍未解决。没有新增功能、页面或依赖，没有继续复杂 Git 界面精修。

## 修改和边界

原实现等待 status 完成后才并行 branch 与 rev-parse；三者只依赖已核实的仓库根目录。本批同时启动三者，Task.WhenAll 等待全部结束，再依次处理状态失败、输出超限、解析失败、分支失败和 HEAD 失败，保留未出生分支与 detached HEAD 语义。命令、输出限制、取消、超时和状态解析不变；无跨批缓存，不改 Git 写操作。短时并发上限由两个查询提高到三个，不声称这些独立查询构成 Git 原子快照。

产品修改仅 src/Augit.Infrastructure/Git/GitStatusService.cs。GitStatusServiceTests 新增状态输出超限优先及已取消不返回部分状态；GitCommandRunnerTests 将原两个真实进程取消测试参数化为2/3个，全部实际启动后取消，返回时逐PID确认退出。测试目录由既有 TemporaryDirectory 释放。

19项状态与命令读取器定向通过；补三进程用例后仅重跑6项读取器测试通过870ms，合计20项不同Infrastructure用例。直接依赖ShellBridgeGitStatusTests两项通过。三文件格式检查、9项共享资源、git diff --check通过。首次测试因新增断言缺空值检查触发CS8602，补Assert.IsNotNull后通过；未跑全量测试，未宣称新增测试修前失败。

## 正常入口结果

证据目录位于 artifacts/pixel-followup-2026-10-17。两批均经 tools/start-augit.ps1 强制非增量Debug构建、真实配置副本、默认Session、正常DPI启动，附CDP。

| 批次 | 状态查询毫秒 | 最后撤盖毫秒 |
| --- | --- | --- |
| 原实现：refresh-stale-document-failure | 154.4 / 143.2 / 149.5 / 153.6 | 657.7 / 537.7 / 545.5 / 515.2 |
| refresh-status-three-parallel | 87.4 / 91.1 / 88.2 / 94.0 | 577.9 / 479.9 / 460.1 / 486.5 |
| refresh-status-three-parallel-burst | 首批96.7 / 87.1 / 80.7 / 92.9 | 652.2 / 484.6 / 474.0 / 486.0 |

连续写入四轮均在首个git/status在途时再写，批次串行，最终正文为2/4/6/8；第二批状态耗时29.8/0.7/0.8/0.7ms，不能将宿主缓存命中的小值冒充三路并行收益。每轮一次遮盖，最后正常Esc关闭，snapshotCount=0、captureCoverWindow=0、ownerEnabled=true。

两组完整录像分别1212/1262帧，585×252，最大间隙均44ms；未重采样。RGB容差2最差分别265/187变化像素，原始最差PNG已目视，仍有边缘变化。光标状态不同，不能把265降到187登记为闪帧改善。两组后三轮低于500ms仅为有限诊断样本，首次仍超目标，整体无闪及性能未通过。

runtime.json 确认 DPR1.75、UI Microsoft YaHei UI12、正文实际随包 JetBrains Mono13。process-identity.json 与 mapped-images.json 确认进程实际路径及映射：

- Infrastructure SHA256由1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80变为A08EB839CC37A2E71BFB5CF8C03C2BCD4FA74BE596CCECDC9D2222E12C27964B。
- Shell仍4CAC07952E541B48BCF069F0F7DDA4B03783A8BB4F12BF86329129D9148C9F75；实际执行live-data.js仍31964BB95E93477B66EC159588AC14B805B3339DA3BAC69EA52B8B2EFC8A1BFC。
- 四网页MD5前后相同：mockup.js=0EEBB52037A8EEABDB81DA777E8C1FFE；mockup.css=00FA3357EC646E91030A9DFDD925DAA9；live-data.js=D50C01AF5311343AC7F8E03FA2E2F0FC；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。Release未更新。
- 仍映射Nahimic，未取得无注入对照，不能作为正式无注入性能验收。

## 清理与接续

各批cleanup-independent.json独立确认27384/27596/21308及3680/10328/8412均退出；59875/62038无监听，Augit-daily-refresh-5ne2Yk及Augit-daily-refresh-pee5hR已删除，采集、分析和测试会话均终止。历史参考清理审批阻塞、Nahimic服务权限阻塞保持，不宣称全局零残留。未回滚、覆盖、暂存或提交混合修改。

下轮继续遮盖捕获及原生呈现成本、首次刷新和设置慢开。55页/全模态、终端/冲突逐行及完整阴影仍未完成；复杂Git与冲突精修暂停，功能移除尚未确认。Windows10排除本轮、跨屏DPI未验证，整体goal保持active。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关窗；异常仅对本次核实PID执行 taskkill /PID <本次PID> /T /F。

复验：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --gdi-clock；连续写入加 --burst。
