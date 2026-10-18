# 冲突正文基线序列化整改

日期2026-10-15。继续已批准的冲突实机整改；保留混合工作区，未暂存、提交或回滚。Windows10排除，跨屏DPI未验证。整体目标未完成。

## 因果与产品修改

前批发现WebView2实际行内基线15.991073608398438，而参考运行时ascent16。新试验conflict-baseline-quantized-shell证明按1/64设备单位舍入原补偿无效；conflict-baseline-bounded-shell显示逐行残差迭代仍受CSS数值表示影响，复用首行规范化参数则12行均为16。conflict-baseline-serialized-shell验证统一padding-top=1.14286px、line-height=20.8571px可使12行基线全16，恢复后回15.991。没有固定增加1物理px，没有改变JBR目标值。

共享mockup.js的measureConflictResolver现把已有基线补偿导出的顶部留白和leading先赋给独立CSSStyleDeclaration，读取其规范化表示后写入两个冲突专用变量。mockup.css所有冲突正文行继承这对变量，未初始化时保留原calc回落。仅调整参数传递，行高、行盒、编号、选区、正文和保存契约不变；字体测量复用，未新增每行探针或监听。两份共享资产同步，design-system.md和ux-spec.md同步说明。

无头Chromium的15组对照原本已是整数基线，不能复现真实WebView2问题；其用途仅为字号/DPR和新增行兼容检查，不用于证明真实误差修复。

## 验证与原始图

- probe-conflict-baseline-quantization.cjs覆盖DPR1/1.25/1.5/1.75/2、代码字号13/17/40共15组，所有现有行及新增行基线、行盒检查通过；记录conflict-baseline-quantization/final-matrix.json。
- verify-conflict-row-fill.cjs双主题、字号、横滚与末端留白24组通过。
- conflict-leading-final三状态系统PNG成功，1730×1215、DPI168，三栏首行基线16。初始图目视完整、中央首字符焦点，无遮挡鼠标光晕；没有缩放或平移。完整不同像素80804/87285/96963；正文25096/25691/28983，标题分隔线均0。相较conflict-caret-final正文27162/27840/31094分别减少2066/2149/2111像素；仍有横向、字形栅格、工具栏、标题与底栏差异，不宣称完整通过，不增加55页合格数。
- conflict-leading-save真实深色多块滚动、横滚编号固定、视口外焦点、精确保存通过。随后合并重复测量并新增真实所有非占位行基线断言，再运行conflict-leading-save-final：编辑前后基线均16（容差0.001逻辑px），精确保存通过。截图发生在测量去重前，渲染逻辑相同但不冒充最终代码重新截图。
- probe-exact-conflict-baseline.cjs恢复style断言已在本批三次试验中执行通过。临时试验仅修改隔离实例，finally恢复并退出。

未修改C#或公共协议，未运行构建/全量。共享资产、JS语法、UTF-8/LF、文档声明与git diff --check收尾检查另行登记。历史全量断言快照不作为本批证明。

## 哈希与资源

mockup.js MD5 BE21F21B0D6F6036844ADF51BD24DB46 → CA9A7349D1CDBA88B33616DE9AE67740；mockup.css 0A62EE8E629016699206B3B66D7D38B1 → E2FC5FAEE0578F3BFADF0A6723F9C8C3。live-data.js 019F54DFDE0629B0DDB67B91B46E50C7、bridge.js F297BF8769E249CA0D804F4F4EEEF5A9不变。

本批实际PID11744/1708/10304/20984/23504/24216，端口58446/52841/57246/57937/64269/51948，专用临时Augit-merge-pair-mpqWG4/nbk2dH/eabGvn/hDyGij/yoVdp2/jbke6L均由finally回收并独立复查。无头浏览器finally关闭，未启动参考PyCharm或常驻服务器；历史被拒清理目录不重试。

    $pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
    $chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
    node tools/audit/probe-conflict-baseline-quantization.cjs $pw $chrome
    node tools/audit/verify-conflict-row-fill.cjs $pw $chrome
    node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-leading-manual-new $pw --multi --reference-frame
    node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-leading-screen-manual-new $pw --light --screen-pair --reference-frame --pair-focus
    # 正常finally停止；异常只停止本次打印的PID。
    taskkill /PID <本次PID> /T /F

收尾实测：9项共享资产一致、4个JS文件语法、10个改动文件UTF-8/LF、文档声明及git diff --check通过。六个本批PID均不存在、六个端口无监听、六个专用临时目录均不存在。历史全量快照仍过期，没有据此声称全量通过。
