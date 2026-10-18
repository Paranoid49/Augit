# 屏幕原图遮盖反证（2026-10-16）

## 结论与下一步

上一批保留设置单层网页外框，仍有真实露底。本批在已授权的独立owned原始PNG试验范围内，只检查“源图未包含系统外框是否足以解释闪亮”。使用同一个Settings窗口的原始屏幕PNG作为遮盖，原图裁取内容与录像首帧2440620个像素完全一致，RGB最大差为0；显示/隐藏交接仍出现两帧4054像素变化。该样本否定了“只补齐源图系统边框就能消除全部闪动”的充分性，不能据此宣称已找到唯一根因。

接下来应定位原生窗口显隐、DWM边框与背景更新交接，不再重复无证据收益的字体、PNG尺寸或同一外框内容试验。单次未出现内部露底不撤销正式实现的历史露底证据。55页与全部模态双端同态、背景无闪与延迟、终端/冲突逐行、完整系统阴影四项目标全部保持未完成。

## 诊断条件和结果

证据目录：artifacts/pixel-followup-2026-10-16/refresh-screen-source-probe/ui-thread。

普通场景入口、Light、系统DPR1.75、UI12、JetBrains Mono13。先在真实表单放置既有三色探针，再用只读系统采集器取得窗口原图；录像前固定该PNG，窗口区域按整数偏移128/128裁取，既不缩放也不修改颜色。原图SHA256校验通过。候选仅供此同态固定表单诊断，不能处理任意草稿变化或遮挡，也没有作为产品截图方案保留。

Desktop Duplication无损FFV1共264帧，每帧1820×1341，固定首帧、RGB容差2、排除0像素。第52/53帧变化4054像素，角部131、非角部边缘3923、内部0，最大RGB差115。最大采样间隙39ms，源时间戳无重复；解码DTS量化警告保留。前景84次采样、最大间隔99ms，qualified=true；仍不能排除采样间干扰，未证明完整Present。

source-comparison.json记录原始屏幕PNG裁片与录像首帧完全一致；同一源图与第52帧的4054个差异和整表面报告一致，未改像素或选择更有利首帧。最坏帧已目视，Settings正文仍完整，外沿有变化。sampledWholeSurfacePassed和fullPresentationVerified均false。

录像SHA256：b85c227fa28a1535cad145240cc5896d897545bd3fdf982fa4cbf993118c8139。
源PNG SHA256：901e28fd2fe829dc9de4224f67c187734c98517706a0788b9421878d34672f56。

六次刷新观测258/198/202/195/222/205ms，因源图在录像前已采集，这些数字不含真实捕获成本，不能登记为产品500ms通过或性能优化。未取得此候选阴影不退化及双主题验证；已有失败足以拒绝进入产品，不为失败候选扩大全量测试。

## 中间失败和恢复

light和preloaded目录保留两次host-timeout:window/modal-state失败。最初误将超时归因采集等待；随后前置采集仍失败，检查发现候选File.ReadAllBytesAsync后无托管SynchronizationContext，后续Win32操作可能离开窗口线程。将仅审计文件读取改为窗口线程同步完成后，ui-thread试验完整执行。此调整是诊断代码修正，不宣称修复正式产品的窗口线程故障。两次不完整录像没有补写成功清单，也不用于正式无闪验收。

PNG损坏与越界2项定向测试通过，107ms。未重复Shell完整套件或无关业务测试。所有候选源代码和驱动副本留在上述证据目录，正式ShellModalHost与两个审计驱动逐字节恢复；临时ShellPngCover及测试文件已删除。恢复Debug构建0警告0错误，4.37秒；Release从未装入本批候选。

恢复SHA256：

- ShellModalHost.cs：124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348。
- capture-shadow-shell.cjs：53A8BBD42730C52162BCDA61A44EA3238129AAF9C7A309F754DF9F6A7D2AE1C7。
- capture-refresh-screen.cjs：688048397310FF647066B8B9025EFD7065F3D2D810B84655F6E73123B54AD98E。

四网页MD5未改变：mockup.js=44D6C53684B74DBC6116FF25F7C22932，mockup.css=285BA9ED6A1C6BCDF78C53A8362110D9，live-data.js=CB897499424737392D7EF315E41F91E4，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。架构、产品功能及用户配置未改变，混合工作区未暂存、提交或回滚。

## 复验与资源

本批启动Augit3156/24008/19240，前景采样4356/6112/13328，录像20148/27896/26948；三个驱动finally均报告退出和隔离目录删除。最终独立核查进程、端口55093/62493/63342和目录vm8Y0Z/ZD5FgI/6d03cI。构建关闭节点复用，不留下构建服务器；历史拒绝清理目录未触碰。

原录像分析：python -B tools/audit/verify-refresh-surface.py artifacts/pixel-followup-2026-10-16/refresh-screen-source-probe/ui-thread --require-stable，预期退出1。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。正常关闭窗口停止；异常只按本次输出PID执行taskkill /PID <本次PID> /T /F。本批诊断候选已撤销，普通启动不会读取审计PNG。

Windows10仍排除本次验收；跨屏DPI保留未验证。
