# 2026-10-16 页面截图干扰与有效性校验

## 结论与范围

上一轮普通入口Settings Terminal截图只有主窗，而测试入口显示完整表单。本批查明页面截图本身会干扰背景刷新：当前安装的Playwright默认隐藏光标，在所有input、textarea和contenteditable上写入caret-color内联style，截图后恢复。产品观察后台节点style变化并刷新所属快照，因此每次截图都可能触发原生背景捕获。不能据此把旧CDP截图里的消失直接判定为桌面透明帧，也不能把页面度量一致当成截图有效。

本批修改审计工具，不修改产品渲染，不减少四项整体验收范围。真实桌面录像已有露底证据仍然成立，不因本次发现撤销；≤500ms、55页完整配对、终端/冲突和系统阴影仍未完成。

## 实施与证据

- 新增tools/audit/capture-stable-page.cjs，在截图前后检查目标可见性、背景忙标记及捕获属性；MutationObserver记录整个采样区间的变化，即使截图结束前恢复也拒绝该帧。超时失败，不保存成有效图。有效PNG不重采样，并保存尝试次数和捕获代际旁证。
- 采集显式使用caret:initial，保留实际光标，不注入隐藏光标样式。compare-startup-paths的主窗、局部巡检及startup-surfaces的32状态截图接入；不改页面样式或业务状态来通过门禁。该检查只证明CDP业务表面采样条件，不证明桌面遮挡、系统阴影或整段刷新稳定。
- 权威实现定位：本机依赖playwright-core/lib/coreBundle.js的inPagePrepareForScreenshots，hideCaret分支对所有输入节点调用style.setProperty，再由清理回调恢复。产品对应web/src/native-modal-state.js的snapshotDepthForMutation和live-data.js的背景观察器。
- 修改前门禁试验：stable-capture-normal-retry在Settings Appearance连续14次捕获均跨隐藏周期，退出码1，未输出有效设置图。取消隐式样式修改后，stable-capture-final正常入口五状态成功：Commit、File View、Git、Terminal各一次；Appearance拒绝一次中间帧，第二次成功。Terminal原图2881×1717已目视确认完整设置窗。

证据目录为artifacts/startup-path-investigation-2026-10-16/stable-capture-final，各状态保存原图和*-capture.json。首次stable-capture-normal未连上调试端口，退出码1并清理；确认无存活实例后才重新启动，不计通过。用户配置逐字节未变，使用真实配置副本和默认profile，无场景/DPI/字体覆盖。

## 验证与清理

新增capture-stable-page.test.cjs：原始PNG、跨采样区间隐藏再恢复、无后台style写入、持续隐藏超时且不写文件，共4个子场景（Node报告含父测试共5项）通过，最终耗时2426.76ms。修改驱动语法及git diff --check通过，没有运行产品全量测试。最初工具调用因字符串语法错误未执行；修正后成功写入。先测门禁再测无干扰修正的两次定向运行对应不同修改，没有重复全量。

本批PID9888、5908、3664均退出；53060、62596、61377无监听；临时目录Augit-startup-compare-7qB6r1、diBR9o、UemcSy及测试临时目录均已删除。历史拒绝清理路径未触碰，没有暂存、提交或回滚混合修改。

产品MD5保持：mockup.js=44D6C53684B74DBC6116FF25F7C22932，mockup.css=D5DADE8F4DB84506031A9AB3B1F5DB80，live-data.js=C8DA64065C77E60234F2D614334E8DC4，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

复验：设置AUGIT_PLAYWRIGHT为本机Playwright模块路径，AUGIT_BROWSER_EXECUTABLE为已安装Chromium路径，再执行node --test tools/audit/capture-stable-page.test.cjs。真实局部巡检使用node tools/audit/compare-startup-paths.cjs <Playwright模块路径> <新目录> --daily-launcher --only-mode=normal --surfaces；驱动finally停止实例并清理。

日常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。关闭窗口停止；异常时按启动输出执行taskkill /PID <本次PID> /T /F。
