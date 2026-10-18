# 背景刷新父窗裁剪试验与首次身份

日期2026-10-15。承接遮盖合成定位，整体目标未完成。Windows 10排除本次验证，跨屏DPI保留未验证。混合工作区不回滚、不暂存、不提交。

## 试验结论

本批试验为模态父窗增加WS_CLIPCHILDREN（0x02000000），检查父窗重绘是否覆盖遮盖子窗。深浅原始录像均失败，故已准确撤销本批一行样式修改及注释，并重建恢复后的Debug外壳，0警告/0错误，耗时2.90秒。未保留无证据收益的产品改动。没有删除既有呈现等待或放宽整表面2级RGB容差。

证据均位于artifacts/pixel-followup-2026-10-15：

|目录|条件|结果|
|---|---|---|
|refresh-clipchildren-dark|先inspect-cover再录像，已经预热|441帧、44变化帧；最严重1396像素、208级RGB，最大间隙50ms；首个观测384.07ms是复用，不能当首次|
|refresh-clipchildren-light|直接录像，首轮创建控制器|422帧、46变化帧；最严重9523像素、241级RGB；首轮577.91ms，最大间隙50ms|
|refresh-identity-final-dark|撤销裁剪后，明确bytes=0/window=0/reused=false|428帧、45变化帧，严格失败；首次540.18ms，随后423.78/485.41/379.96/379.70/362.22ms；最大间隙50ms|

深色试验静态settings、cover-visible-live和cover-hidden-live三份完整PNG哈希相同，源PNG为不透明RGB；同一实例的录像仍失败。这直接说明静态合成零差异不能用作过渡帧验收。浅色第47帧提取为worst-frame-47.png，已目视，原始尺寸未重采样；首轮滤镜转义失败未生成证据，修正后成功。

## 保留的审计改进

capture-refresh-screen.cjs现在在录屏前读取并校验初始遮盖字节数、窗口身份、就绪与可见状态，并记录initialCover.reused。缺失、负数或可见遮盖均拒绝作为基线；每次刷新保存priorCoverBytes及priorCoverWindow。旧清单没有字段时不能默认首次。4项自动化覆盖明确首次、隐藏复用、部分准备、缺失/非法字段及可见遮盖。

最终真实清单初始bytes=0、window=0、visible=false、reused=false；第二轮开始保留142008字节及同一遮盖窗口22743168。这只是条件与生命周期证据，不证明无闪帧或性能达标。GDI录屏不是完整Present追踪，fullPresentationVerified仍false。

## 验证与清理

4项审计测试通过，113.78ms；最终真实流程及关闭资源断言通过，但整表面严格检查退出1。初始试验构建和恢复构建均0警告/错误；受影响JS语法、git diff --check通过。未运行全产品测试。

本批Augit PID22212/21752/18148，录屏PID9644/1808/7924，已退出；端口56390/49680/56034无监听，隔离目录Augit-shadow-pair-mLHRVl/vTKmDI/8VR2tZ不存在。解码进程正常结束，录像、分析JSON及原始提取帧保留为成果，没有新临时脚本；未重试历史被拒绝的清理。

网页MD5本批不变：mockup.js 69203C667E940D79782FA85B600D49B4、mockup.css 91CB995954680CA777EF336D27580103、live-data.js 00E7ECB134CBD0939313286BE7C92E38、bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node --test tools/audit/refresh-cover-identity.test.cjs
node tools/audit/capture-shadow-shell.cjs $pw artifacts/refresh-first-manual Dark --refresh-video
python -B tools/audit/verify-refresh-surface.py artifacts/refresh-first-manual --require-stable
# 正常由finally停止；异常中断只使用本次驱动报告的PID。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```

不能把本批称为背景刷新修复完成。55页/全模态、终端/冲突精确逐行、完整系统阴影同样未完成，原目标不缩减。
