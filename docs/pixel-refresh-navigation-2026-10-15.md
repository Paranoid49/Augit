# 背景刷新导航并行与呈现保护（2026-10-15）

## 当前结论

整体整改未完成。隐藏遮盖文档导航现在与实时PNG捕获并行；保留导航前图片定尺、首图两帧、显示后两帧及DwmFlush。最终深色首次514.08ms仍超500ms，浅色354.13ms；两种主题整表面严格检查均失败，不能称为无闪隐或稳定性能达标。Windows10排除本次验证，跨屏DPI仍未验证。

## 实现与试验边界

- ShellModalHost仅在隐藏遮盖子窗提前导航空白文档，等待导航和捕获均完成后安装内存图片响应并装入预定尺首图；可见背景仍沿原先捕获后导航路径。导航失败关闭已创建控制器，监听器在finally解除，过期准备由现有代际规则释放。
- ShellSnapshotPresentation提取实际首图加载脚本，仍先定尺再等待两次渲染回调；ShowCoverAsync的显示确认与DwmFlush保持。新增navigationMilliseconds，controllerMilliseconds仍只表示控制器创建，不将两者合并冒充创建成本。
- 去掉WS_EX_TRANSPARENT的试验有485采样帧、44变化帧，最严重2427818像素变化，已撤销。
- 临时原生消息取证显示遮盖显示和隐藏触发WM_ERASEBKGND/WM_PAINT，采样期间没有WM_NCPAINT/WM_NCACTIVATE。该次481帧只捕获圆角差，不足以锁定偶发闪亮根因。临时环境开关、状态字段及消息钩子全部删除；只保留证据JSON。
- 单独删除首图两帧等待未带来明确收益。与导航并行组合时深色首次458.67ms，但原始帧49明确露出底层正文，122899像素变化，不能采纳该组合。已恢复首图两帧保护，再采深浅最终版本。

## 最终实机样本

证据目录为artifacts/pixel-followup-2026-10-15/refresh-surface-parallel-guard-dark及-light，均含原始FFV1录像、阶段时序、全表面分析和阴影回归JSON。每帧2440620像素、RGB容差2，不排除边缘或任意区域。

| 项目 | 深色 | 浅色 |
| --- | --- | --- |
| 六次观测耗时ms | 514.08 / 364.05 / 388.04 / 390.73 / 382.95 / 364.87 | 354.13 / 369.15 / 364.56 / 378.63 / 383.88 / 364.05 |
| 首轮捕获 / 创建 / 导航 / 后续就绪ms | 193.07 / 61.99 / 153.87 / 69.15 | 127.43 / 43.19 / 98.50 / 53.73 |
| 采样帧 / 变化帧 | 489 / 46 | 493 / 44 |
| 最严重变化 | frame59，12792像素，最大216RGB | frame59，3769像素，最大134RGB |
| 最大采样间隙 | 34ms | 50ms |
| 严格检查退出码 | 1 | 1 |

深色最严重原始帧已目视，呈现边框闪亮，未在该最终样本发现前述整块底层正文。浅色最严重帧仍由算法判失败，不能以探针稳定取代整表面失败。DPI为本机175%，GDI录像不是完整Present追踪，fullPresentationVerified仍false。并行采集期间存在解码负载，样本不作为独立性能基准；不能从单次改善推导稳定提速。

与首图定尺基线refresh-surface-sized-dark/light相比，四边中央半段64物理像素的阴影暗化中位数最大差均为0。此项只证明本次同应用采样范围内无变化，不是PyCharm双端完整阴影验收。

## 定向验证

- ShellSnapshot相关14项通过，67ms；新增测试覆盖隐藏文档初始化与捕获同时进行、关闭淘汰后只释放一次资源。
- 首图实际脚本及显示确认4项通过，最终110.84ms；断言首图定尺后两帧前不应答，显示确认仍独立等待。
- 最终Debug构建0警告0错误，2.41秒。限定C#格式检查、9共享资产及git diff --check通过。
- 首次真实单组使用正在写证据的工作仓库，审计标记audit-background-update消失，后续locator超时；不能记为通过，也未证明是产品故障。改用固定参考fixture，同一单组9.53秒通过，包含并发关闭、捕获关闭竞态、重新打开、嵌套、背景加载版本、草稿焦点、PNG尺寸及关闭释放。证据refresh-parallel-guard-lifecycle/geometry.json。不修改产品以迁就审计标记，不运行全量套件。
- 封装转义、Python字符串和一处补丁匹配曾失败，未产生对应写入；修正后取得结果。未将失败计为通过。

本批仅修改ShellModalHost.cs、ShellSnapshotPresentation.cs、ShellSnapshotPreparationTests.cs、snapshot-presentation.test.cjs及文档。网页四MD5前后不变：mockup.js 3B3399F68FD796B48952022625C3B5F4；mockup.css 09D3687D25A62F2377D117F3AE78599F；live-data.js B48E37B755997A2C28D98AF8CB87E7ED；bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。分支dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；保留既有混合修改，未暂存或提交。

## 启停与清理

本批Augit PID2580/5648/15400/3136/16196/844/3260/23400/2416/22108，录屏PID19128/13228/5280/12088/23332/12320/11696/14008均已退出。端口54387/64088/63969/56419/55630/57899/65415/53245/54095/63929均无监听。十个本批隔离profile目录Po02zl/llNtEy/jFfNQV/qPIicu/Pwxnz7/vTMoE3/5ZQylo/xALAEN/mpfEeR/deeiqD均已删除；原始正式证据保留。未启动PyCharm，未重试历史被拒绝的删除。

运行命令：

```powershell
$refreshPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-shadow-shell.cjs $refreshPlaywright artifacts/refresh-navigation-new Dark --refresh-video
python -B tools/audit/verify-refresh-surface.py artifacts/refresh-navigation-new --require-stable
node tools/audit/run-pixel-remediation-shell.cjs $refreshPlaywright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/refresh-navigation-lifecycle --single --reference-fixture
dotnet test tests/Augit.Shell.Tests --no-restore --filter FullyQualifiedName~ShellSnapshot -v minimal
node --test tools/audit/snapshot-presentation.test.cjs
# 驱动在finally中停止本次实例并删除隔离profile；仅异常中断时使用本次打印的PID。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```

下一步仍需解决首轮边框闪亮、圆角合成差及超过500ms的样本；55页/全部模态、终端与冲突逐行、完整系统阴影配对继续保留，不以本批结束代替整体目标完成。
