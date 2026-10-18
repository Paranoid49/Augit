# 背景刷新分段计时与未采纳试验（2026-10-15）

## 当前结论

整体整改未完成，首次刷新仍超过500ms。此批最终保留审计分段计时，不保留未经实测证明收益的产品调整。未改变两帧呈现确认、DWM同步或快照捕获策略。本批取证时终端行高尚待裁决；用户后续已批准按PyCharm实机整改，落实情况见[终端行距整改](pixel-terminal-lineheight-2026-10-15.md)。

## 未采纳的隐藏加载试验

尝试让隐藏遮盖首图onload只确认加载，省去隐藏时两帧，显示后仍保留两帧及DwmFlush。试验版通过9项准备/加载测试、3项脚本测试及真实关闭竞态、重新打开、嵌套、快照尺寸、更新合并与焦点草稿回归。但系统录屏首次深色570.38ms、浅色507.51ms，没有证明优于此前519.49/506.24ms。跨运行波动无法证明回退，也无法证明优化成立，因此只撤销本批试验代码及对应新增测试，不撤销任何前序修改。已重新构建恢复后的产品，0警告/0错误，2.53秒。

试验证据目录位于 `artifacts/pixel-followup-2026-10-14/`，沿用旧批次目录名；时间以JSON UTC时间为准：

- `refresh-hidden-ready-lifecycle/`：真实专项通过，后续五次368.47–382.94ms。
- `refresh-hidden-ready-dark/`：431帧、0探针异常，最大间隙50ms。
- `refresh-hidden-ready-light/`：429帧、0探针异常，最大间隙50ms。

试验最初C#测试误用xUnit特性，编译失败；按项目MSTest规范修正后9项通过。试验撤销后不把新增2项C#测试计作当前产品测试覆盖。未运行全量套件。

## 当前产品基线分段

新增审计专用 `trace-modal-refresh.cjs`，只在隔离页面记录modal-cover、modal-refresh、modal-uncover的请求/响应时刻，不保存参数、结果或用户内容；原样转发、保留失败语义，结束恢复postMessage并移除监听。`capture-refresh-screen.cjs`将阶段记录并入每次采样，检查三阶段顺序、应答完成且无错误。时间为网页performance.now，不与外部时钟直接相减。宿主往返包含排队和桥接开销，不等同纯捕获时间或屏幕切换时间。

恢复产品后的深色基线 `refresh-phase-baseline-dark/`：

| 样本 | 状态轮询观察ms | 遮盖往返ms | 背景更新往返ms | 撤盖往返ms |
| --- | ---: | ---: | ---: | ---: |
| 首次 | 555.35 | 326.50 | 185.50 | 7.60 |
| 后续1 | 356.39 | 186.70 | 143.30 | 2.30 |
| 后续2 | 382.47 | 216.20 | 145.20 | 2.20 |
| 后续3 | 422.43 | 200.80 | 172.50 | 2.80 |
| 后续4 | 385.39 | 201.80 | 141.50 | 3.40 |
| 后续5 | 375.47 | 193.50 | 141.50 | 2.20 |

首次遮盖内部PNG捕获211.02ms、并行控制器创建85.55ms、图片加载及显示等待109.08ms；并行时间不可相加。遮盖响应至背景请求约7.30ms、背景响应至撤盖请求约6.50ms。证据表明主要成本在捕获及图片准备，而非这两处帧等待；不应继续削减呈现保护来凑500ms。后续优化应测量捕获面积/编码、可复用资源的成本和生命周期，不能以旧截图代替当前表单。

基线437采样帧、0探针异常、0重复时间戳，但282处间隙超过25ms、最大50ms；fullPresentationVerified仍false。三探针与GDI轮询不证明全表单、全部显示帧及全部模态无透明帧。

## 验证与清理

最终2项审计钩子测试及2项原呈现脚本测试通过（合计4项，0.134秒），覆盖透明转发、字段最小化、同步失败/宿主错误、监听和钩子恢复。脚本语法、9共享资产、C#限定格式检查及git diff --check通过。

Augit PID22984/2256/20948/9012，录屏PID4448/17136/23424均已退出，端口57765/55885/62870/50669无监听；隔离目录Augit-pixel-fix-vE0Ruh、Augit-shadow-pair-0ebHe5/wG0WY3/itrq5E均已删除。正式视频/JSON保留，未重试历史拒绝清理动作。未启动参考PyCharm。

四网页MD5前后不变：mockup.js `DB12568D024253582D1C09B5DA1CD5AE`，mockup.css `439C96E4A5696967DC702CAC9B9A0850`，live-data.js `DB52470B2BAB434DE4E463086A39FA6D`，bridge.js `F297BF8769E249CA0D804F4F4EEEF5A9`。未暂存、提交或覆盖混合修改。

```powershell
$phasePlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node --test tools/audit/trace-modal-refresh.test.cjs tools/audit/snapshot-presentation.test.cjs
# 使用新的输出目录；驱动finally清理本次录屏、实例和profile。
node tools/audit/capture-shadow-shell.cjs $phasePlaywright artifacts/refresh-phases-new Dark --refresh-video
python -B tools/audit/verify-refresh-video.py artifacts/refresh-phases-new
# 异常中断时只填写本次驱动打印的PID。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```
