# 背景刷新源图与原生框架隔离

日期2026-10-15。上一轮属于取得实际证据与审计改进的进展，本轮继续原目标，不将局部诊断当作完成。Windows 10排除本次验证，跨屏DPI仍未验证。分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；混合工作区未回滚、暂存或提交。

## 定位结果

新增probe-snapshot-resampling.cjs与compare-snapshot-resampling.py，用已有源PNG在隔离Chromium测试五种DPR及auto/pixelated/transform三种呈现方式。只有100%下三者均无差异；其它比例不同模式仍有差异，不能据此替换真实宿主实现。隔离浏览器的布局精度和合成路径与WebView2不同，图片自然尺寸变换也没有稳定改善，不修改产品。完整输出在artifacts/pixel-followup-2026-10-15/snapshot-resampling-modes。

随后使用真实WebView2已加载的源PNG与系统原始图直接比较。根据已记录偏移(316,145)设备像素裁取，不缩放、不平移配准。refresh-clipchildren-dark及refresh-cover-source-dark-settled两份既有证据中，settings、cover-visible-live、cover-hidden-live三种状态在去掉外围16px后均零像素差；完整客户区仍有12796个不同像素，最大118级RGB，差异集中于边缘。内部比较仅用于根因定位，不改变整表面验收范围，不排除任何边缘验收。

compare-cover-source-screen.py校验原始截图方法、无重采样、源图尺寸、SHA256、DPI、进程/窗口身份、激活稳定性、同一屏幕区域及裁取边界；非整数物理偏移或越界拒绝，禁止PIL自动补黑造成误判。3项测试覆盖边缘差异、身份/哈希/尺寸/来源错误及偏移越界，通过。源图与正文内部逐像素一致是当前定位证据，不能推导全部过渡帧已通过。

## 原生框架试验

现行DwmExtendFrameIntoClientArea四边各扩展1物理px。分别试验全部为0和只保留Bottom=1，均未采纳，最终已恢复原来的四边1px。以下路径均在artifacts/pixel-followup-2026-10-15：

|目录|整表面录像|阴影/性能|
|---|---|---|
|refresh-zero-frame-light|445帧零变化，最大间隙50ms|右/下近边阴影相对原实现最大差42/76级RGB，原阴影消失；首次511.77ms，不能采用|
|refresh-bottom-frame-light|429帧47帧变化，最严重9521像素、241级RGB，最大间隙34ms|右/下1–16px阴影剖面与原实现最大差0/0；首次511.56ms，仍有闪亮，不能采用|
|refresh-bottom-frame-dark|435帧45帧变化，最大间隙50ms|首次536.44ms；严格整表面失败|

零扩展虽使所采样画面稳定，却破坏系统阴影；单边扩展保留近边阴影，却恢复了边框/圆角变化。此结果将后续调查重点指向原生框架合成与子窗呈现的交互，仍不足以断定唯一根因。所有报告fullPresentationVerified均false，不将GDI采样当作完整Present证明。

## 验证与清理

恢复后Debug构建0警告/0错误、2.89秒；JS语法、3项源图分析测试和git diff --check通过。只跑受影响试验与分析，没有运行产品全量套件。产品修改全部撤销，本批保留诊断工具、测试和原始证据。网页MD5不变：mockup.js 69203C667E940D79782FA85B600D49B4、mockup.css 91CB995954680CA777EF336D27580103、live-data.js 00E7ECB134CBD0939313286BE7C92E38、bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。

Augit PID12260/9716/23944、录屏PID1688/21208/23932已退出，端口62408/58347/49941无监听。隔离目录Augit-shadow-pair-YpEVkW/JuPVbx/0q3pUM已删除；浏览器诊断finally关闭，解码进程均已结束，单测临时目录自动删除。未新建临时诊断文件，成果脚本与PNG/JSON/录像保留，不重试历史被拒绝的清理。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
python -B tools/audit/test_source_screen.py
node tools/audit/probe-snapshot-resampling.cjs $pw $chrome artifacts/sampling-manual
node tools/audit/capture-shadow-shell.cjs $pw artifacts/refresh-frame-manual Light --refresh-video
python -B tools/audit/verify-refresh-surface.py artifacts/refresh-frame-manual --require-stable
# 正常由finally关闭；异常仅针对本次输出的PID。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```

55页及全部模态同态配对、背景刷新严格呈现和延迟、终端/冲突逐行、完整阴影验收仍未完成，整体目标保持活动。
