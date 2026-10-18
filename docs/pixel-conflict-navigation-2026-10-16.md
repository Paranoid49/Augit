# 冲突公共行导航整改

日期：2026-10-16。分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。保留既有混合修改，没有暂存或提交。上一轮日常入口复核属于证据进展，本轮继续原目标中的冲突逐行配对。

## 证据与修改

对照conflict-leading-final/actual-initial.png及conflict-reference-clean/reference-initial-clear.png，首行是公共文本、第二行是冲突时，实际两枚导航箭头均禁用，而参考下一处可用。旧currentConflictGroupIndex将块外光标也当作第一块。

本地IntelliJ的PrevNextDifferenceIterableBase按光标行选择后方首个或前方最后一个完整经过的变更。该checkout不是运行中262的同版本源码，仅用于解释；262.9437.214原始截图提供可见状态依据。产品未增加动作，修复已有导航。

live-data.js按结果区选区起点求导航目标，并在选择公共行时刷新箭头。块内跳到相邻未处理块，保存期间冻结。尚无正文选区时沿用当前块编号；导航整块选区以起点稳定当前块。接受及保存契约不改，UX规范同步。

采集脚本遗漏appearanceVersion，深色请求可能被配置迁移为浅色。已补版本1及实际主题断言，不能仅凭旧脚本的Dark请求证明旧证据为深色。

## 验证及失败

- verify-conflict-navigation.cjs在真实浏览器DOM加载产品函数，公共行、边界、多行块、选区、无选区回落、保存冻结及无冲突共12项通过。
- live-shell --goal-conflict-only最终51项通过，包含10485078字节混合换行编辑保存；未执行全量。首次因默认浏览器路径不存在退出，改用已安装Chromium。随后旧流程失败：接受第三块后，光标导航先回第二块，旧测试却依赖编号夹取跳到第一块。现明确初始第一块和两次上一处，原始逐字节保存期望保持不变，临时诊断输出已移除。
- 真实深色多块conflict-navigation-dark验证实际主题、滚动、视口外焦点及精确保存。浅色conflict-navigation-light验证首行下一处、末行上一处、正确冲突选区，以及接受、忽略、撤销重做、编辑与精确保存。证据位于artifacts/pixel-followup-2026-10-16下对应目录的multi.json、navigation.json及interaction.json。
- 原图为CDP客户区证据，不代替系统截图。文字横向偏移、字形栅格、完整工具栏和整窗配对尚未结案；55页合格数不增加。

## 交付与资源

唯一产品改动为web/src/live-data.js，MD5从641B29F3BC50EC22500924F64B9C63CE变为C8DA64065C77E60234F2D614334E8DC4，已同步Debug和Release随包资源；无需C#构建。两套各24文件镜像、共享视觉资产及相关JS语法通过。其余运行时MD5未变：mockup.js=6E61081C842CA96770B3F8BB56E13B0A，mockup.css=99A3D1E61C7D12BA82DD25CE2E685A76，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

PID22000/24784、端口52828/59061及Augit-merge-pair-cs8Ox6/ewW7tf临时目录已由finally回收，独立检查无残留。模块浏览器和服务器finally关闭，进程检查无对应Node、Playwright浏览器或专用WebView2残留。没有启动PyCharm，历史被拒清理目录未触碰。

复验命令：

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$env:AUGIT_BROWSER_EXECUTABLE='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/verify-conflict-navigation.cjs $pw $env:AUGIT_BROWSER_EXECUTABLE
node tools/audit/live-shell.spec.cjs $pw --goal-conflict-only
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-navigation-manual-new $pw --light --reference-frame
```

日常启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`。关闭窗口即可停止，异常只针对本次打印的PID执行`taskkill /PID <本次PID> /T /F`。所有原始目标继续保留；Windows10排除本次验收，跨屏DPI保持未验证。
