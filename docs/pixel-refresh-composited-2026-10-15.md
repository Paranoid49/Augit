# 背景刷新组合绘制试验与全帧分区

日期2026-10-15，分支dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。整体目标仍未完成。Windows 10排除本次验收，跨屏DPI未验证。保留混合修改，未回滚他人修改、暂存或提交。

## 试验与恢复

只对模态父窗CreateWindowEx增加WS_EX_COMPOSITED（0x02000000），检查子窗组合绘制能否消除原生边框闪亮。未改技术栈、四边1px DWM扩展、透明控制器底色、截图来源、首图及显示后两帧确认、DwmFlush或刷新队列。

浅色样本仅角部变化，深色仍有一次严重直边变化，不能证明该样式稳定改善。试验一行及注释均已撤销，产品恢复本批开始状态。试验构建0警告/0错误、8.46秒；恢复构建0警告/0错误、3.00秒。最终ShellModalHost.cs SHA256为77B43F8E4C009DFFB3C15288B2D88CEC8BCB5537D137557893C138841E330B9A。未保留未经证据支持的产品改动。

## 三组原始录像

证据均在artifacts/pixel-followup-2026-10-15，原始FFV1、PNG、身份清单及分析保留。每帧1820×1341，整表面2440620像素，RGB容差2，excludedPixels=0。三组首次均明确bytes=0/window=0/reused=false，不将复用耗时当首次。

| 目录 | 采样/变化帧 | 最严重变化 | 首次ms | 后续五次ms |
| --- | --- | --- | --- | --- |
| refresh-composited-light | 441/44 | 12像素、最大6级RGB | 524.05 | 379.65 / 416.14 / 393.26 / 366.69 / 379.73 |
| refresh-composited-dark | 432/48 | 12792像素、最大216级RGB | 584.03 | 430.85 / 367.79 / 438.04 / 407.21 / 411.82 |
| refresh-composited-control-light（恢复原实现） | 439/50 | 9523像素 | 565.98 | 407.17 / 386.26 / 461.09 / 394.22 / 438.07 |

全部严格检查退出1；最大采样间隙50ms，无重复时间戳。fullPresentationVerified仍false，不以GDI采样替代全部Present。录像不是统计性能基准，深色采集时存在解码负载，不能据单次耗时比较推断性能因果。

浅色试验与同环境恢复对照的四边中央半段64px阴影暗化中位数最大差均0，shadow-control-comparison.json保留结果。这只证明本次同应用采样剖面相同，不能当PyCharm完整阴影验收。

## 新的定位证据

此前仅提取最严重帧，不能知道其余变化来自何处。现verify-refresh-surface.py对每个失败帧增加四角16px、其余边缘16px、内部三个互斥区域计数，总和必须等于整表面changedPixels；不排除区域、不改变容差或通过条件。新增连续帧编号分段，保留缺帧间隔，不把录像时间直接关联为宿主呈现时刻。

- 浅色组合绘制：44变化帧全部只有四角，最大12像素；六个连续段分别为39–46、71–77、102–109、132–138、160–166、188–194。直边及内部均0。
- 深色组合绘制：四角48帧；第50帧额外12388个直边像素，角部404，合计12792；内部0。该直边帧与此前40–48的角部变化段之间有一帧恢复。其余角部变化形成后续五段。
- 恢复浅色：四角50帧；第49帧额外9287个直边像素、角部236，合计9523；内部0。直边帧与此前39–47角部变化段之间也有一帧恢复。

这提示圆角持续变化与偶发直边闪亮须分别定位：前者在六段中反复出现，后者在这两份录像中各只有一帧，且在首段角部变化结束后。不能因此断定唯一根因或归属某一次桥接应答，两个时钟尚未直接同步。后续应对撤盖后的原生重绘/首次资源状态做精确观测，避免再次用静态PNG或最严重一帧代替完整时间序列。

## 验证与资源

6项区域测试与5项整表面测试通过，覆盖32px边界、互斥计数、非二值拒绝、连续/不连续段、每区独立及严格门禁。三份录像重新解码取得分区，之后按同一录像SHA256核验已有结果再汇总连续段，没有再次启动产品测试。未执行产品全量、C#完整测试或无关模块回归。

深色最严重第50帧已提取为原始PNG并目视核对，边缘明亮细线与算法分区一致，未以裁切隐藏失败。收尾共享9资产、文档声明、git diff --check通过；历史全量快照仍过期，不用于本批结论。

本批Augit PID16436/25256/1292、录屏PID9116/25124/20868均由finally退出；端口57241/62204/50213无监听；临时目录Augit-shadow-pair-RWvEVf/u1htnl/6PGrbt不存在，已只读复查。解码进程均终止，PNG/JSON/录像是正式证据。历史被拒绝清理不重试。未启动参考PyCharm。

网页MD5未变：mockup.js 56AF172CFF9BBD45FDFDFE5F9003143B，mockup.css F9F29FAB9339BD2420A216408F2E00D7，live-data.js 00E7ECB134CBD0939313286BE7C92E38，bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
# 当前为恢复后的正式实现，试验样式没有保留。
node tools/audit/capture-shadow-shell.cjs $pw artifacts/refresh-regions-manual-new Light --refresh-video
python -B tools/audit/verify-refresh-surface.py artifacts/refresh-regions-manual-new --require-stable
python -B -m unittest discover -s tools/audit -p test_refresh_frame_regions.py
python -B -m unittest discover -s tools/audit -p test_refresh_surface.py
# 正常由finally停止；异常只使用本次输出的PID。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```
