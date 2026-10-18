# 冲突窗口实机布局接续

日期：2026-10-15。分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。用户明确授权按PyCharm实机重排并同步规范；Windows 10排除本次验证，跨屏DPI保留未验证。混合工作区未回滚、暂存或提交。整体整改仍未完成。

## 布局修改

参考为PyCharm 2026.2.1的 `artifacts/pixel-followup-2026-10-15/conflict-line-pair/reference-initial.png`。本地 `MergeRequestProcessor.java` 的 updateBottomActions 将左右接受放WEST，Windows下将应用、取消依次放EAST。已将文件名固定在标题、导航与当前块接受两侧/忽略侧改为紧凑图标工具栏、来源标题左对齐，移除重复返回底栏；底部左侧接受左/右，右侧应用/取消。

布局授权不改变产品行为：接受仍处理当前冲突块并可撤销；Apply Changes仍校验并标记已解决；Cancel复用原取消/返回会话状态机。未添加参考中的智能合并或无对应功能入口。中央仍是唯一可编辑区。同步修改design-system.md和ux-spec.md。

图标有title和aria-label。目视首轮截图发现全局文本按钮78px下限及内阴影仍覆盖图标，已局部覆盖并增加计算样式断言。移除实时不可见Back按钮，冻结状态覆盖可见Cancel；保留旧data-conflict-back事件兼容分支。

## 验证

- 72组主题、DPI、独立字号、按钮/焦点、首行空侧和编辑范围专项通过。首轮后因上述目视缺陷补修，再跑同一受影响专项，最终图标宽度和边框断言通过。没有运行产品全量测试或构建。
- 冲突模块live-goal 51项断言通过，包含取消、外部变化、失败恢复、撤销及大文件保存；约10MB混合换行样本单次尾部编辑354ms、编辑及保存1650ms，仅为本次样本。
- 真实Git单块完整流程通过：编辑、追加、撤销、外部刷新、精确保存。三状态原始系统PNG及进程身份保存于 `artifacts/pixel-followup-2026-10-15/conflict-layout-screen/`。
- 最终72组图在conflict-layout-final；真实完整流程在conflict-layout-live。共享9资产、JS语法、文档声明检查与git diff --check通过。历史全量2373项快照仍过期。

最终系统图已目视：工具栏与单层底栏符合本批结构；参考1730px宽，实际1750px宽，来源槽、正文横坐标、文字墨迹、标题与工具项仍不同。三状态不能作为整窗同态通过，也不增加55页合格配对数量。

## 哈希与清理

mockup.js MD5从6F4A29DE66FF139F95CBDEF6074B5CBD变为7ADAD5CBF206BC11D9AF3FC026CFCE76；mockup.css从411B555CAC9293854DD200E3C1C08625变为E9CF7BDFEE3AABE2300027CC4672840C；live-data.js从B48E37B755997A2C28D98AF8CB87E7ED变为00E7ECB134CBD0939313286BE7C92E38；bridge.js保持F297BF8769E249CA0D804F4F4EEEF5A9。共享视觉稿与运行时字节一致。

实际PID14044、22548已由驱动finally停止；端口65438、63453无监听；临时Augit-merge-pair-r69lWc/wOITNg不存在。浏览器专项和live-goal均由finally关闭浏览器/服务器。正式截图与JSON保留；本批文档修改临时脚本删除，历史被拒绝的清理未重试。

```powershell
$pw = "C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright"
$env:AUGIT_BROWSER_EXECUTABLE = "C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe"
node tools/verify-ux-conflict.cjs $pw $env:AUGIT_BROWSER_EXECUTABLE artifacts/conflict-layout-manual
node tools/audit/live-shell.spec.cjs $pw --goal-conflict-only
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-live-manual $pw --light
# 正常由finally关闭；异常时只使用本次驱动输出的PID。
taskkill /PID <本次AUDIT_PID> /T /F
```
