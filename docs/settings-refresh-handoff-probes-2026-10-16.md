# 设置闪烁：客户区绘制与固定内容层试验（2026-10-16）

## 结论

设置重复打开修复仍保留，恢复后的正式Debug在真实仓库9标签、延迟呈现、重复入口、延迟遮盖、四分类及嵌套回归通过。整表面闪烁仍未修复；本轮两个候选均失败，已按候选字节一致性核对后精确撤回，未进入Release。不得以状态机回归通过或本次内部零变化宣称全部闪烁消失。Windows10按用户要求排除，跨屏DPI未验证。

## 两项反证

第一项仅在已登记模态父窗处理WM_PAINT，以ValidateRect确认客户区失效，不调用该消息的默认过程；不拦截WM_NCPAINT，不改变DWM边框及阴影。第二项增加固定原生内容子窗，业务WebView与遮盖同属内容子窗，外框的直接子层保持不变；不复制业务DOM。两项分开试验，没有叠加。

| 候选 | 采样帧 / 变化帧 | 最大变化像素（角 / 直边 / 内部） | 首轮耗时 | 后续五轮耗时 |
| --- | --- | --- | --- | --- |
| 客户区仅验证 | 336 / 1 | 4040（131 / 3909 / 0） | 621.47ms | 338.89 / 333.04 / 340.67 / 328.70 / 317.57ms |
| 固定内容子窗 | 322 / 2 | 3992（131 / 3861 / 0） | 566.09ms | 313.17 / 320.05 / 314.06 / 333.36 / 331.99ms |

证据目录分别为artifacts/pixel-followup-2026-10-16/refresh-validate-client-probe/light与refresh-content-container-probe/light。两组均普通场景入口、隔离配置及profile、Light、DPR1.75，使用computer-use确认主窗前景后开始；固定参考fixture不等于用户真实仓库，本轮另跑真实仓库回归。DDA/FFV1每帧1820×1341，全部2440620像素与首帧比较，容差2、排除0像素；严格门禁均退出1。最大采样间隙55/41ms、原始重复时间戳0；FFmpeg解码DTS换算警告保留。前景采样102/99次、最大间隔103/119ms、无观测干扰，不能排除采样间干扰。fullPresentationVerified仍false。

首组第71帧、第二组第68/69帧发生边缘变化，最大RGB差115；完整原帧与分区报告保留。不能将比此前少的变化帧数当作修复收益，也没有新增完整阴影通过结论。录像SHA256分别为a2f049f22c9a2726d5ecdf21b9a3c4e8c61d24eae0725fbbe97042d225147407和f8ce8cb22fb746d5e0c6c5ff9c7825f6591887e7d0c62389e180843918fa72f3。

## 正式版本复验

撤回后Debug构建0警告0错误，4.13秒。真实仓库使用随包网页、默认配置副本、隔离profile与9标签；延迟呈现时额外重复设置入口，并覆盖延迟遮盖、分类切换和嵌套返回。五条关闭路径save/cancel/escape/header/apply-save分别429.07/228.87/369.07/243.33/396.65ms，网页错误0。证据settings-restored-real-workspace。此项不包含逐帧录像，不能替代闪烁验收。

重复入口、相同字体不重复度量及字体变化仍度量共3项测试通过，169.20ms；9项共享资产一致。Debug/Release各24项网页文件与web完全一致；Release未装入两个试验。未重复全量Shell套件，因为最终没有新增保留的可执行逻辑。git diff --check通过。

ShellModalHost恢复SHA256为124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348。四份网页MD5未变：mockup.js=44D6C53684B74DBC6116FF25F7C22932；mockup.css=285BA9ED6A1C6BCDF78C53A8362110D9；live-data.js=CB897499424737392D7EF315E41F91E4；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。保留所有既有混合修改，未暂存或提交。

## 资源与复验

本轮Augit4552/21236/20720、前景采样8352/28640、录像11268/26396已退出；59621/53988/65100无监听。临时目录Augit-shadow-pair-XSVpmF、Augit-shadow-pair-01e66V、Augit-settings-close-GPUGcu由finally删除，另行确认。候选源码、原始录像、PNG及分析作为审计证据保留；不触碰历史被拒绝的清理目录。

```powershell
# 日常入口：构建后启动，停止请正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 只有异常退出时才填入本次启动打印的PID，避免误杀其他实例。
taskkill /PID <本次PID> /T /F
# 本轮正式回归；驱动finally自动清理。
node tools/audit/check-settings-close.cjs <Playwright模块路径> <新证据目录> --many-tabs --delay-cover --delay-present --nested --bundled-web --navigation
```

后续不能重复上述两个方案或仅增加等待；需要取得业务表面、遮盖和DWM实际提交之间的对应证据。本轮没有解决整个Augit闪烁，55页/全模态、逐行配对及完整阴影的证据缺口仍保留。
