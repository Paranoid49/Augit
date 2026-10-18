# 背景刷新整表面取证与首图尺寸（2026-10-15）

## 本批结论

整体目标未完成。将系统录像从三处探针扩展到每帧整张表面后，发现此前漏掉的圆角差异、首图放大错位及一帧边框闪亮。首图尺寸现已在导航前按PNG真实宽高设置，修后深浅样本未再出现放大错位；边框闪亮和圆角差异仍存在，首次刷新仍超过500ms目标，不能登记无闪隐通过。Windows 10排除本次验证，跨屏DPI保留未验证。

## 新检查与证据

新增 `verify-refresh-surface.py`：核验原始FFV1编码、尺寸和帧时间戳，流式解码RGB，逐采样帧对照首帧，每帧2440620像素，容差2级RGB，不排除边缘、光标或任何像素。报告变化计数、最大通道差、包围盒、录像SHA256和实际时间间隙。动画或遮挡也会报告，不自动解释成露底；GDI采样仍可能漏显示帧，所以 `fullPresentationVerified` 始终false。只使用已有Pillow及FFmpeg，无新增依赖。

| 样本 | 帧数 | 变化帧 | 主要差异 |
| --- | ---: | ---: | --- |
| 历史深色分段基线 | 437 | 48 | 47帧各10个圆角像素差至5；1帧12796边框像素差至216 |
| 当前修改前深色 | 438 | 47 | 另捕获1帧346101像素变化，原图显示遮盖图放大错位 |
| 首图预定尺深色 | 456 | 48 | 无放大错位；仍有10像素圆角差及1帧12796边框像素差 |
| 首图预定尺浅色 | 448 | 46 | 无放大错位；12像素圆角差至6及1帧9523边框像素差至241 |

历史源为 `artifacts/pixel-followup-2026-10-14/refresh-phase-baseline-dark/`。当前源及结果位于 `artifacts/pixel-followup-2026-10-15/refresh-surface-{current-dark,sized-dark,sized-light}/`。各目录含原始MKV、分段清单、整表面JSON、稳定背景；所有整表面结论均false。修前frame-42.png及历史 `refresh-surface-diagnosis/` 的帧0／38／48、差异蒙版和角部对照已查看；历史frame48边缘由主题色短暂变为约183灰色，未把它误称为整张正文透明。

浅色录像含1个重复时间戳，FFmpeg提示非递增DTS；解码数量仍与原始时间戳一致，重复帧没有删除。深浅修后最大间隙34ms，仍不能证明显示帧完整。

## 产品修复与未采纳试验

`ShellSnapshotPresentation.InitialImageSize`在导航前读取受控CapturePreview PNG的IHDR物理尺寸，以捕获比例换算CSS宽高；`ShellModalHost`将它写入首图内联样式。此前只有onload执行fitSnapshotImage，初始图片有机会以物理尺寸作为CSS尺寸进入渲染。保留onload的更新定尺、两帧确认及DwmFlush，不缩减呈现保护、不重采样PNG。该改动影响首图，后续克隆图片继承当前尺寸并在替换前按新PNG重新定尺。

另试验先隐藏遮盖子窗再隐藏控制器，深色424帧仍有一帧12792边框像素差，未证明收益，已仅撤销本批顺序试验并重新构建。试验目录 `refresh-surface-hide-window-dark/` 保留，不能当作最终产品验证。圆角差异可能与合成边缘有关，边框闪亮原因尚未确定，不以推测修改系统阴影或放宽容差。

修后深色六次刷新观测为512.17／367.39／388.96／428.23／382.71／429.77ms；浅色542.20／386.06／387.49／393.41／364.02／361.59ms。时间仍是状态轮询观测，不能视为屏幕实际切换时刻，也不从两次单样本推导稳定性能提升。

## 定向验证与清理

- 整表面算法4项：单像素负向、边缘／远离探针位置、容差与格式、流截断，0.003秒通过。
- 首图尺寸新增2项MSTest，46ms通过；ShellSnapshot相关13项，85ms通过。未重复全量Shell或仓库套件。
- 实际注入脚本及导航前定尺接线3项通过，0.136秒。
- 真实单组关闭竞态、重新打开、嵌套、遮盖三色／复用、更新合并、PNG尺寸、草稿焦点及关闭释放通过，目录 `refresh-initial-size-lifecycle/`。
- 最终恢复后的Debug构建0警告0错误，2.78秒；限定C#格式、9共享资产、git diff --check通过。

第一次补丁封装解析失败，未写入；重发正确补丁成功。一次Python默认GBK读取中文JSON失败，改显式UTF-8后完成诊断。没有将失败记成通过。

产品网页四MD5前后不变：mockup.js `DB12568D024253582D1C09B5DA1CD5AE`，mockup.css `E9D173258AFD662A66ABC90EDB0B9734`，live-data.js `50B86BC40F3F003A8FE292888DC930BC`，bridge.js `F297BF8769E249CA0D804F4F4EEEF5A9`。分支dsh、HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`；混合改动未回滚、暂存或提交。

本批Augit PID2832／12204／19604／18420／6232，录屏9668／4708／19948／19656均退出；端口54494／60816／60642／63312／50214无监听。隔离目录Augit-shadow-pair-2a3i8Y／TaEnuu／oGCaiQ／d2PaD8和Augit-pixel-fix-wWdJV1已不存在；解码进程已结束。未启动参考PyCharm，未重试历史被拒绝的清理。

```powershell
python -B tools/audit/test_refresh_surface.py
dotnet test tests/Augit.Shell.Tests --no-restore --filter FullyQualifiedName~ShellSnapshot -v minimal
node --test tools/audit/snapshot-presentation.test.cjs
$surfacePlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-shadow-shell.cjs $surfacePlaywright artifacts/refresh-surface-new Dark --refresh-video
python -B tools/audit/verify-refresh-surface.py artifacts/refresh-surface-new
# 驱动finally清理实例和录屏；异常中断只使用本次打印的PID。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```

下一步针对首轮撤盖边框闪亮继续取证，再测捕获与准备成本；55页／全部模态、终端／冲突逐行配对、剩余阴影范围仍保留原目标，不以本批局部修复替代。

## 后续边框试验与回退

本批继续核对默认非客户区重绘。撤盖前增加DwmFlush的深色样本442帧仍有48变化帧，其中一帧12796边框像素变化、最大差216，首次观测581.44ms，无收益，已撤销。证据目录为refresh-surface-flush-uncover-dark。

拦截WM_NCPAINT的试验深色432帧零变化，但浅色432帧有2帧变化：一帧2308661像素露底（failure-01.png已目视），另一帧顶部14像素变化。进一步仅拦截已登记独立模态窗口，浅色428帧仍有1变化帧，不能判定通过。对应目录为refresh-surface-ncpaint-dark、refresh-surface-ncpaint-light、refresh-surface-ncpaint-parent-light。首份深色零差异只能说明该试验的采样结果，不能证明根因完全查明。

更关键的是，拦截WM_NCPAINT改变了系统阴影：与本批首图定尺版本比较，中央四边64物理px剖面的最大暗化中位数差，深色左/上/右/下为4.33/3/4.33/7.67，浅色42/27/42/76级RGB。浅色原始图目视显示系统外侧阴影削弱。shadow-regression.json与各shadow-profile.json保留；这些是同应用回归统计，不是PyCharm双端验收。不能用削弱阴影换取边框稳定，所有WM_NCPAINT试验代码已撤销，最终仅保留前述首图定尺修复。

试验版本真实单组关闭竞态、重开、嵌套、遮盖复用、合并及尺寸专项通过，记录于refresh-ncpaint-lifecycle；该结果不能覆盖试验中的录像失败，也不作为最终产品新增通过项。恢复后Debug重新构建0警告0错误，2.76秒。

整表面工具新增--require-stable严格模式：任一采样帧变化返回非零，调查模式仍生成失败报告；新增门禁行为测试后共5项通过（0.006秒）。浅色两个失败录像均使严格模式返回1，未放宽2级RGB容差或排除边缘。限定格式和9共享资产检查通过。

后续启动的Augit PID11068/15832/732/9180/11200、录屏18340/19756/22072/21752均已退出；端口58770/49688/64593/53250/56960无监听。隔离目录Augit-shadow-pair-PQRg1h/rlME1v/Qd7CtL/4Ux3Ow及Augit-pixel-fix-9Ctu0G均不存在。新增正式录像、PNG和JSON保留，未启动PyCharm或常驻服务器。原始四网页MD5不变。

一条rg正则及一次Windows glob参数无效，一次Python诊断遗漏UTF-8、一次FFmpeg筛选表达式转义失败；均修正后取得结果，未将失败写作通过。未回滚、暂存或提交用户已有修改。后续应在保留默认DWM阴影语义的前提下隔离重绘来源；不得直接屏蔽WM_NCPAINT作为最终修复。

```powershell
# 新证据目录完整采集后，严格检查所有采样像素。
python -B tools/audit/verify-refresh-surface.py artifacts/refresh-surface-new --require-stable
python -B tools/audit/test_refresh_surface.py
# 手动启动仅本次实例；结束时停止该实例及其子树。
$refreshAuditApp = Start-Process ./src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe -ArgumentList '--workspace D:/github/Augit' -PassThru
taskkill /PID $refreshAuditApp.Id /T /F
```

最后尝试仅给独立模态增加WS_CLIPCHILDREN，保留默认非客户区处理。浅色438帧仍有44变化帧，严格门禁失败；四边中央阴影剖面与首图定尺基线差均为0，说明该试验未改变此范围阴影但也未解决刷新问题，已撤销。证据refresh-surface-clipchildren-light保留。Augit PID21544与录屏18756已退出，端口64711无监听，Augit-shadow-pair-Ltcjau已删除。最终产品恢复本批开始时的首图定尺实现。

2026-10-15导航并行接续：隐藏快照文档导航与PNG捕获并行，首图及显示后两帧保护均保留。最终首次深色514.08ms、浅色354.13ms，边框/圆角严格门禁仍失败；14项Shell、4项脚本及固定夹具真实单组通过。试验回退、证据、哈希和启动/停止清理见[导航并行记录](pixel-refresh-navigation-2026-10-15.md)。整体整改未完成。

2026-10-15遮盖合成定位：新增源PNG与三份同位置系统图对照，源图不透明、底层隐藏前后哈希相同；初始角点仍有两类状态，不能归因或判定通过。重绘隔离、像素采样与主题底色试验均撤销，6项定向测试及实际取证通过。证据、失败和启停清理见[遮盖合成记录](pixel-cover-composition-2026-10-15.md)。整体目标未完成。
