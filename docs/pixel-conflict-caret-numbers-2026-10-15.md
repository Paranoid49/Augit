# 冲突窗口光标行号整改（2026-10-15）

## 范围与依据

接续LCD正文批次，检查三栏编号。参考初始原图中左右第2行、结果第1行编号更深；这不是冲突行底色的组成部分。源码EditorGutterComponentImpl.java的doPaintLineNumbers读取各编辑器的主光标逻辑行，并使用LINE_NUMBER_ON_CARET_ROW_COLOR；不要求编辑器持有焦点。ThreesideTextDiffViewerEx.java的doScrollToChange把三侧光标分别移到目标差异起点。Light配色来自expUI_lightScheme.xml，普通#AEB3C2、光标行#767A8A；IslandSchemeDark.xml为#4B5059/#A1A3AB。

本地权威源码提交576e32820af82f97529d70f9269726803a27c016属于263分支，不能代替实机262.9437.214版本一致性证明。参考图沿用conflict-reference-clean/reference-initial-clear.png等原始图，本批未启动PyCharm。

## 修改

共享mockup.js为每个正文用WeakMap记录光标行；首次定位实际差异首行，之后以Selection的活动端跟随点击、键盘与反向选择，离开正文保留各自状态。重排行号时按存活行节点保持位置，删除该行后夹取有效下标。空白对齐占位不参与编号。共享mockup.css仅改变当前行编号颜色，未写入正文、改变高亮底色、可编辑边界或保存契约。

selectionchange仅注册一份页面级监听，每次查询当前正文，不捕获模态节点；WeakMap不持有已移除窗口。没有新增依赖。工具栏导航仍沿既有选择逻辑，尚未证明所有三侧导航位置与参考一致。

## 验证与边界

- 新增verify-conflict-caret-numbers.cjs：浅深两主题共20项，覆盖独立光标、正反选区活动端、失焦、编号重建、正文不变、行插入/删除、容器端点及节点关闭重开；通过。
- 实机浅色三状态位于artifacts/pixel-followup-2026-10-15/conflict-caret-final。完整不同像素83020/89584/99224；正文27162/27840/31094；标题分隔线均0。均未缩放或平移原图。相较LCD批次，初始及接受左侧各少5，全部处理后多50；不能据此认定整窗视觉更接近或完整通过。
- 深色真实多块保存位于conflict-caret-save，脚本正常退出；具体保存、滚动与焦点断言见multi.json。
- 共享资产、JavaScript语法、空白及文档声明按本批范围检查；未运行全量套件或C#构建。
- 55页与全部模态、背景刷新角部/直边、终端逐行、完整系统阴影仍未完成；Windows 10按用户要求排除，跨屏DPI未验证。总体目标保持未完成。

## 基线、哈希和清理

分支dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。保留既有混合改动，未回滚、暂存或提交。

mockup.js MD5 56AF172CFF9BBD45FDFDFE5F9003143B → BE21F21B0D6F6036844ADF51BD24DB46；mockup.css C2A61BEC007B0CFE47D1698E591ED3AD → 0A62EE8E629016699206B3B66D7D38B1。live-data.js 00E7ECB134CBD0939313286BE7C92E38、bridge.js F297BF8769E249CA0D804F4F4EEEF5A9未变。

本批PID23536/19380均退出；52421/65107端口无监听；临时Augit-merge-pair-ygHfjA/QJYvRi目录均已删除并核验。测试浏览器由finally关闭；未处理历史被拒绝清理的目录。正式截图及JSON保留为审计证据。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/verify-conflict-caret-numbers.cjs $pw $chrome
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-caret-manual-new $pw --light --screen-pair --reference-frame --pair-focus
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-caret-save-manual-new $pw --multi --reference-frame
# 正常由finally关闭；异常时只停止本次脚本打印的PID。
taskkill /PID <本次Augit_PID> /T /F
```
