# 背景刷新系统录屏与遮盖层级整改（2026-10-14）

## 新证据与实现

旧DOM状态、遮盖PNG正确和两次requestAnimationFrame回调未能证明系统前景不露底。本批使用本机已有FFmpeg 8.0.1的gdigrab读取桌面物理区域，以FFV1/bgr0无损编码。隔离Settings加入三处12逻辑像素色块，每个解码帧检查中心5×5物理像素。探针不进入产品资产。

修前425帧中36帧探针消失，人工查看1.2秒原始帧确认Settings整个正文变成主窗内容。两个WebView控制器共享父窗口，图片加载不保证遮盖在实时控制器上方。

ShellModalHost改用独立原生子窗承载遮盖控制器，控制器仍只显示原PNG，不绘制原生正文或复制业务DOM。子窗初始隐藏、置底，使用无背景刷窗口类及透明WebView默认底色；图片就绪后不激活地置顶，两帧回调后等待DwmFlush完成再确认允许隐藏真实表单。刷新结束隐藏子窗，模态/尺寸/比例变化及关闭时销毁并释放控制器。新增诊断字段登记遮盖子窗句柄和可见性。

## 录像结果

目录均在artifacts/pixel-followup-2026-10-14下，保留MKV、参数/日志、六次刷新时序、分析JSON、前后原始PNG及关闭后两次一致背景。

| 目录 | 帧数 | 异常帧 | 判断 |
| --- | ---: | ---: | --- |
| refresh-screen-dark | 425 | 36 | 修前真实露底 |
| refresh-screen-dark-child | 427 | 1 | 独立子窗后首次白屏 |
| refresh-screen-dark-transparent | 437 | 1 | 透明WebView后首次系统灰底 |
| refresh-screen-light-transparent | 425 | 1 | 浅色同样失败 |
| refresh-screen-dark-noerase | 439 | 1 | 无背景刷后首次露底 |
| refresh-screen-dark-hidden | 420 | 1 | 初始隐藏后仍首次露底 |
| refresh-screen-dark-flush | 417 | 0 | 当前已采样探针通过 |
| refresh-screen-light-flush | 432 | 0 | 当前已采样探针通过，时间戳有缺陷 |

最终深色最大采样间隙34ms；浅色最大34ms且1个重复时间戳。请求60fps不等于每个显示帧都被采集，0探针异常不能写成完整无透明帧证明。只覆盖Settings首次及五次复用刷新，不覆盖所有模态、所有位置或嵌套前景。

分析器保留重复时间戳并输出strictTimingPassed=false，倒退仍拒绝。sampledProbeFramesPassed与fullPresentationVerified分开，后者始终false。浅色首次分析因重复时间戳拒绝，修正为保留缺陷后才取得完整分析，未删除任何帧。

## 延迟与验证

Debug、175%缩放、UI12、1400×930逻辑主窗、正在录屏。时间来自宿主状态轮询，不是屏幕内容最终切换时刻。

| 场景 | 首次ms | 后续五次ms |
| --- | ---: | --- |
| 深色修前 | 609.59 | 393.83 / 349.53 / 368.27 / 359.68 / 363.11 |
| 深色最终 | 572.35 | 376.39 / 368.78 / 403.89 / 411.64 / 376.56 |
| 浅色最终 | 573.59 | 396.94 / 374.92 / 393.49 / 424.31 / 381.76 |

首次仍超过500ms目标，不能以录屏负载为由自动豁免，不将不同运行单样本视为稳定性能A/B。屏幕实际延迟、稳定分布和内存仍需验证。

最终refresh-flush-lifecycle定向单组通过：遮盖三色、复用、更新合并边界、物理尺寸、嵌套父层、焦点/草稿、关闭清理；新增断言验证子窗显示、隐藏、关闭后句柄归零。中间refresh-child-lifecycle也通过并保留。因窗口生命周期变化，收口只运行一次Shell完整测试：184通过、0失败、12秒；没有运行仓库全量。两项实际注入脚本测试、两项录像分析单测、C#限定文件格式、三个JS语法及git diff --check通过。最终构建0警告/0错误。

## 复验与清理

```powershell
$pixelPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
# 新证据目录；正常finally停止FFmpeg和Augit并删除隔离profile。
node tools/audit/capture-shadow-shell.cjs $pixelPlaywright artifacts/refresh-dark-new Dark --refresh-video
node tools/audit/capture-shadow-shell.cjs $pixelPlaywright artifacts/refresh-light-new Light --refresh-video
python -B tools/audit/verify-refresh-video.py artifacts/refresh-dark-new
python -B tools/audit/verify-refresh-video.py artifacts/refresh-light-new
node tools/audit/run-pixel-remediation-shell.cjs $pixelPlaywright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/refresh-lifecycle-new --single --reference-fixture --cover-pixels --coalesce-refresh
# 异常中断只停止驱动打印的本次PID，不能按进程名批量结束。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```

Augit PID：3852（开窗驱动超时未录）、15100、2156、7556、18248、4960、12324、9532、13828、16600、21140。FFmpeg PID：3196、7112、7688、16832、18524、17128、7660、21612。独立查询确认本批进程无残留，11个隔离目录均不存在，11个调试端口无监听。历史两个审批拒绝删除的目录未重试，仍见阴影记录。审计证据文件保留。

首次开窗驱动在表单已经打开后因重挂重试超时；现只在确实存在活动Settings时接受已完成状态，不强制点击或吞掉其他失败。

四主要网页MD5前后未改：mockup.js 6298632B16BA82E5FCFB9B0AB177593D，mockup.css 06751CCD396E8ECACD19779CBBDE38AB，live-data.js DB52470B2BAB434DE4E463086A39FA6D，bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。未回滚、暂存或提交混合工作区。

整体仍未完成：55页及全模态配对、终端/冲突完整逐行配对、完整刷新时序与延迟、阴影四角/非活动状态仍未结案。Windows10排除本次验收，跨屏DPI保留未验证。
