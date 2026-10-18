# 遮盖子窗提前创建反证（2026-10-16）

## 结论

在Settings父窗显示之前创建隐藏遮盖子窗，首次刷新仍出现连续两帧外框变化；该候选失败，已逐字节撤回并恢复Debug。不能将首次闪变仅归因于刷新期间新增空原生子窗。尚未分离首次WebView控制器创建、首次显示以及DWM合成，不能据此指定最终根因。

上一轮提供了换图重叠无效的系统证据，属于进展；本轮换用尚未验证的创建时机变量，没有重跑相同候选。整体目标保持未完成，55页/全模态、背景无闪、终端及冲突逐行、完整阴影均没有新增完整验收。Windows10排除，跨屏DPI未验证。

## 唯一试验变量

将ShellModalHost.CoverCaptureAsync中的原生子窗创建提取到EnsureCaptureCoverWindow，在PresentCoreAsync的ShowWindow之前调用；刷新时复用此空子窗。图片捕获、截图WebView控制器创建、PNG加载、显示保护、DWM框架及释放逻辑保持原样，不提前捕获旧表单，也不预热图片控制器。候选只编入Debug，未进入Release。

录屏基线明确为captureCoverBytes=0、captureCoverWindow=6031800、visible=false、reused=true。这里reused表示部分原生资源已准备，不能把首次534.74ms冒充零资源首次成本，更不能将空子窗预创建写成完整控制器预热。

## 实际证据

目录artifacts/pixel-followup-2026-10-16/refresh-precreate-child/light。普通场景入口、参考fixture、隔离配置/profile、Light、DPR1.75、UI12、JetBrains Mono13。通过computer-use激活已枚举主窗并观察后才释放main-active-ready，前景门禁没有更改。

DDA无损FFV1共343帧，每帧1820×1341、2440620像素；固定首帧，RGB容差2，排除0像素。第74/75帧（1.267/1.283秒）各4064像素变化，其中角部131、16px边缘3933、内部0，最大通道差120。最大采样间隔38ms、原始重复时间戳0；严格门禁退出1，sampledWholeSurfacePassed=false，fullPresentationVerified=false。FFmpeg解码DTS量化警告保留，没有删除失败帧。

视频SHA256为e3ff4beb333cb84ecc8686b8dbee514594dd456c2fa7c9a42eacf23c92b8aae0。frame-0.png和frame-74.png直接从原视频提取；第74帧已目视核对。原图中存在鼠标及光晕，分析没有屏蔽它们，本样本内部无变化，但不计无遮挡双端同态证据。

六轮耗时534.74/347.59/322.14/309.65/334.84/336.77ms。首次仍超500ms，不能依据后续波动推断性能收益。前景101次采样、最大间隔87ms、未记录干扰；仍不能排除采样间短暂干扰。

## 恢复与验证

候选构建3.61秒，恢复构建4.06秒，均0警告0错误。撤回时核对当前文件为本次候选哈希，再恢复保存的原始字节；ShellModalHost恢复SHA256为124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348。候选与基线文本及hashes.json作为审计证据保留，不是运行时资源。没有回滚既有混合修改，未暂存或提交。

未保留新增可执行逻辑，故没有重复全量Shell套件或无收益深色录像。恢复后的Debug24项web镜像一致，git diff --check通过。四网页MD5未变：mockup.js=396E779B261AC31FBE4B625181134680；mockup.css=15DE14CA11BB3E8682E403FB7EF5E054；live-data.js=AD3BA8F7851F463ABF99FF932B4CA95A；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

## 资源与启停

Augit18860、前景采样13468、录像16068及分析解码进程已退出；55620无监听，Augit-shadow-pair-Ipia8z及main-active-ready已清理。原始录像、截图和分析作为证据保留。历史被拒绝目录未触碰。

```powershell
# 日常启动；结束使用时正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 异常只停止本次输出PID及其子进程。
taskkill /PID <本次PID> /T /F
# 正式实现复验，不包含已撤回的预创建候选。
node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新证据目录> Light --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames --activate-main
D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py <新证据目录> --require-stable
```

后续应区分控制器首次创建与首次显示对原生框架的影响，不能再次把空子窗预创建当作未经尝试的修复，也不能放宽外框验收。
