# 背景刷新原始采集时钟关联

日期2026-10-15，分支dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。整体整改未完成；Windows 10排除本次验收，跨屏DPI保持未验证。保留工作区混合修改，未暂存、提交或回滚。

## 本批范围

核对live-data.js恢复表单后已等待两次requestAnimationFrame。撤盖当前先隐藏控制器再隐藏子窗；历史已试过交换顺序及撤盖前DwmFlush，均未解决，因此没有重复这些产品试验。本批只扩展隔离审计，不修改产品逻辑、规范、DWM边框、透明背景、呈现保护或通过容差。

trace-modal-refresh.cjs保留单调时钟，增加请求/响应Date.now；capture-refresh-screen.cjs启用ffmpeg -debug_ts，保存原始输入微秒时间戳及编码链。新增correlate-refresh-clock.py核对输入归零偏移、滤镜帧、编码半刻度量化、封装与ffprobe输出、录像SHA256及桥接墙钟漂移。拒绝记录缺失或不匹配，不能直接把录像相对秒数与网页performance.now相减。录制-t边界只允许量化到截止点的未编码尾帧，不任意截短序列。

## 真实录像与结论

证据位于artifacts/pixel-followup-2026-10-15/refresh-clock-dark及refresh-clock-light。FFV1原图1820×1341，整表面2440620像素，RGB容差2，excludedPixels=0；均为零字节、零子窗的明确首次遮盖。

| 主题 | 采样帧/变化帧 | 最严重变化 | 首次ms | 后续五次ms |
| --- | --- | --- | --- | --- |
| 深色 | 479/46 | 10角部像素、最大RGB5 | 489.14 | 354.73 / 375.75 / 383.99 / 353.84 / 384.73 |
| 浅色 | 472/45 | 9523像素、最大RGB241 | 492.82 | 364.94 / 372.51 / 412.30 / 376.79 / 363.52 |

两组严格整表面检查均退出1，最大采样间隙34ms，无重复时间戳。内部变化均0。首次观察值本次低于500ms，但没有产品优化、样本量有限且开启诊断日志，不能由此宣称性能整改完成或否定历史超时。

- 深色首个角部变化帧38在遮盖响应前20.653ms；首段末帧46在撤盖请求后3.758ms、响应前2.242ms。本组没有直边闪亮。
- 浅色首个角部变化帧37在遮盖响应前22.312ms。第46帧出现236角部及9287直边变化，共9523；原始采集时间为1792045106037.942ms，位于首次撤盖请求后44.942ms、响应后40.942ms。该帧不在任何桥接请求/响应区间内。
- 因而本次圆角变化发生在遮盖已参与合成的阶段，直边闪亮采样发生于撤盖应答之后；不再仅凭未经对齐的两种相对时钟推断阶段。时间先后仍不足以证明根因：GDI采样不是Present、Date.now有毫秒精度、桌面异步合成以及跨事件时钟均有限制，fullPresentationVerified仍false。

下一步应检查撤盖后原生非客户区重绘与首次子窗资源状态，分别定位圆角覆盖和后发直边闪亮。不得增加任意等待、去掉系统阴影、放宽容差或重复已失败的隐藏顺序试验。55页及全部模态配对、终端/冲突逐行和完整阴影仍未完成。

## 验证与清理

7项时钟关联测试、2项桥接审计测试、4项首次遮盖身份测试通过；相关JavaScript语法、9项共享资产、文档声明、9个本批文件UTF-8/LF及git diff --check通过。浅色第46帧已提取原始PNG并目视核对边缘亮线。两份录像各只运行一次整表面解码，后续关联读取已有分析与原始日志，不重复启动产品。未跑全量测试或构建。

本批Augit PID24192/7900、录屏PID25456/20912已由finally停止；端口63127/53550无监听，临时目录Augit-shadow-pair-zobO35/c32u5M已删除并只读复查。未启动PyCharm。历史被拒绝清理目录保持原状，未重试。录像、JSON及日志属于正式证据保留。

产品SHA256未变：ShellModalHost.cs为77B43F8E4C009DFFB3C15288B2D88CEC8BCB5537D137557893C138841E330B9A；live-data.js为981FA5962B82BA969397C21E23228ACAA4AD6F5B1E9E9CEC484B805040B6C431。网页MD5未变：mockup.js 56AF172CFF9BBD45FDFDFE5F9003143B，mockup.css F9F29FAB9339BD2420A216408F2E00D7，live-data.js 00E7ECB134CBD0939313286BE7C92E38，bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-shadow-shell.cjs $pw artifacts/refresh-clock-manual-new Light --refresh-video
python -B tools/audit/verify-refresh-surface.py artifacts/refresh-clock-manual-new --require-stable
python -B tools/audit/correlate-refresh-clock.py artifacts/refresh-clock-manual-new
python -B -m unittest discover -s tools/audit -p test_refresh_clock.py
node --test tools/audit/trace-modal-refresh.test.cjs tools/audit/refresh-cover-identity.test.cjs
# 正常由finally停止；异常仅停止本次脚本输出的PID。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```
