# 跳转行标题栏接续（2026-10-16）

按已授权的实机布局调整补齐标题品牌槽。参考PyCharm2026.2.1运行时header左槽40逻辑px、图标16px且左距12px、标题起点40px、右侧关闭区32px。来源为goto-current-pair/reference-runtime.json及本地权威提交576e328的DialogHeader.kt/CustomHeader.kt；保留Augit蓝色A品牌例外及只支持行号的功能边界。

共享mockup.js/CSS与实时openGoToLineDialog同步，design-system.md同步。新增图标、标题、关闭区几何断言，修前缺少图标/标题定位失败，修后Light/Dark、界面12/40、DPR1/1.75/2共12组合通过。Debug/Release构建均0警告0错误，随包资源镜像一致。

正常场景入口的真实Shell捕获在已观察主窗前景后释放main-active-ready，11项行为通过，网页错误0。证据artifacts/pixel-followup-2026-10-16/goto-header-normal。实际图标16×16、左距12、标题起点40、关闭32，PNG584×252。原始截图包含鼠标指针及光晕，不能计为完整无遮挡双端同态配对；已有输入焦点与字体栅格差异仍未解决。

四文件MD5从mockup.js=44D6C53684B74DBC6116FF25F7C22932、mockup.css=285BA9ED6A1C6BCDF78C53A8362110D9、live-data.js=CB897499424737392D7EF315E41F91E4变为396E779B261AC31FBE4B625181134680、15DE14CA11BB3E8682E403FB7EF5E054、F03F2393B7E03DF3B6BFFEEEBBF25987；bridge.js仍为F297BF8769E249CA0D804F4F4EEEF5A9。未暂存、提交或覆盖其他改动。

Augit27448已退出，端口54437释放，临时目录Augit-goto-pair-fP1JJA及检查点已清理。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。正常关闭窗口；异常仅对本次启动输出PID执行taskkill /PID <本次PID> /T /F。

本批实机命令：node tools/audit/capture-goto-line-shell.cjs <Playwright模块路径> <新证据目录> --activate-main。实际观察并激活主窗后释放main-active-ready，驱动finally清理实例。全55页、全模态、系统阴影及背景无闪仍未完整验收。
