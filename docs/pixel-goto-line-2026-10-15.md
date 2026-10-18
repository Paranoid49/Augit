# 跳转行实机接续（2026-10-15）

2026-10-15最新接续：正常入口焦点颜色修正、同尺寸系统PNG及200×34参考输入组件诊断见[焦点与几何记录](pixel-goto-focus-2026-10-15.md)。尺寸与完整配对未结案。

## 初次接续与当时待裁决

上一轮完整阴影外沿量化为有效进展。本轮转向55页中的go-to-line，取得PyCharm2026.2.1初始预填及清空后的两份原始系统PNG，并修复真实入口与视觉稿按钮文案漂移：live-data.js的Go to改为OK。视觉稿原为OK，不修改视觉稿迁就实现。

实机底部顺序为OK、Cancel；design-system.md紧凑输入窗口明确规定Cancel、确认，且默认宽400逻辑px。因此已经向用户请求只调整跳转行布局并同步规范，当时尚未收到该项裁决，未实施尺寸、按钮顺序等冲突改动；后续授权与实施见本文最后的整改段。现有仅行号、无列跳转、错误反馈的产品边界保留。

## 当前证据

目录artifacts/pixel-followup-2026-10-15/goto-line-pair：

- reference-initial.png：默认1:1全选，活动窗口原始屏幕PNG，DPI168，DWM外框584×252物理px。
- reference-empty.png：通过已观察输入框BackSpace清空，仍聚焦输入框，尺寸相同；原图目视确认无遮挡，无鼠标光晕覆盖窗口内容。标题Go to Line:Column，标签[Line] [:column]:，按钮OK/Cancel。
- behavior/actual-state.json：实际真实外壳外框700×288物理px，正文输入为空且聚焦，按钮Cancel/OK。与参考宽度差116、高度差36物理px；该尺寸是宿主状态及DOM几何，不冒充已取得的实际原始PNG。
- actual与actual-observed两次系统采集均被前景门禁拒绝，未生成合格实际截图。第二次Computer Use观察到实际弹窗，但后续系统前景身份仍不匹配，因此不以工具截图代替原始PNG、不放宽门禁。

参考与实际当前输入值虽已一致，但标题/标签/功能边界、尺寸、按钮顺序等仍不同。参考字形回退证据亦未补齐；本批不增加55页合格配对数，不宣称同态或像素复原通过。原始PNG未缩放或平移。

## 验证与实现

新增capture-goto-line-shell.cjs，使用真实Ctrl+G入口；支持默认严格系统截图、显式等待前景观察，以及独立--behavior-only功能回归。行为模式写明screenCaptureRequested=false，不能作为截图验收。采集前后使用现有window/modal-state，finally关闭本次实例、CDP并删除临时profile。

七项真实流程通过：初始输入聚焦、按钮文案、输入0后错误且不关闭、输入2后导航到第二行、关闭后焦点回正文、Cancel关闭、正文内容不变。同时核验所有者禁用/恢复、快照归零、无前端错误。JS语法与git diff --check通过；未运行全量套件或构建。

## 基线与清理

仅产品live-data.js一处文案变化，MD5由00E7ECB134CBD0939313286BE7C92E38变为019F54DFDE0629B0DDB67B91B46E50C7。mockup.js BE21F21B0D6F6036844ADF51BD24DB46、mockup.css 0A62EE8E629016699206B3B66D7D38B1、bridge.js F297BF8769E249CA0D804F4F4EEEF5A9未变。保留混合修改，未暂存、提交或回滚。

参考PID9472及子树15964/25564/22020/11956/9156/20880/11144已停止。Augit PID22204/25348/2288、端口55576/58374/64913均清理，临时Augit-goto-pair-pUAlgU/3zQESC/SdRa44删除。历史被拒绝清理的目录未重试。正式原始图及JSON保留。

    $pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
    node tools/audit/capture-goto-line-shell.cjs $pw artifacts/goto-line-new
    node tools/audit/capture-goto-line-shell.cjs $pw artifacts/goto-line-behavior-new --behavior-only
    # 正常由finally结束；异常仅停止本次返回的PID。
    taskkill /PID <本次Augit或参考PID> /T /F

参考沿整改接续记录的隔离PYCHARM_PROPERTIES启动；本批立即恢复调用进程环境变量。总体目标仍未完成，Windows10排除，跨屏DPI未验证。

## 前景诊断接续

新增证据目录 artifacts/pixel-followup-2026-10-15/goto-foreground-diagnostic。actual-empty.png 为系统采集原始 PNG（700×288，DPI168），SHA256 为 69efb84d4bc865399a02134433ed88e12eca6c6a83fefcfcea47482718ce3bcf。采集前后目标 HWND18942160、PID11956 均保持前景，边界与 DPI 稳定。目视核对完整窗口、空输入焦点、无可见遮挡或鼠标光晕，登记 content-review.json；该记录不修改采集器原始元数据。相对参考584×252仍宽116、高36物理px，故不增加合格配对数。

本次七项真实行为检查再次通过，首次严格采集成功；此前两次失败作为历史保留。未修改原生窗口焦点逻辑，不能把一次通过解释为根因修复。capture-screen-region.ps1 仅增强拒绝信息，报告目标和前景各自的窗口句柄、进程ID，门禁不放宽。新增 test_screen_region_diagnostics.py 以替身 Win32 返回值执行真实脚本前置段，失焦/错误实例/正确前景三项自动化检查通过（2.531秒），不截取或操作桌面，不创建测试文件。

独立复查 PID11956 已退出，55540 无监听，临时 Augit-goto-pair-U964FY 已删除。四个运行时 MD5 与上节一致，未修改产品布局，未运行构建或全量测试。Windows10仍排除，跨屏DPI仍未验证。

    python -B -m unittest discover -s tools/audit -p test_screen_region_diagnostics.py
    node tools/audit/capture-goto-line-shell.cjs $pw artifacts/goto-line-new
    # 采集器 finally 自动停止本次实例；异常只终止日志中的本次 PID。
    taskkill /PID <本次AugitPID> /T /F

收尾：共享9项资产一致、17个PowerShell脚本编码与语法、7个本批文件UTF-8/LF、文档声明和git diff --check通过。文档检查提示历史全量快照过期，未将其计作本批通过。再次核验PID、监听端口、专用临时目录均不存在。

## 用户批准后的布局整改

2026-10-15 用户批准按实机调整并同步规范。本批增加跳转行独立样式，其他紧凑输入入口保留原布局；底部 OK、Cancel，Tab 按输入、OK、Cancel、关闭循环。默认宽334逻辑px、最小高144px，标题常规字重、OK强调字重，移除标题分隔线；标签最小92px以保持参考输入起点，字号增大时按文字扩展，错误说明独占一行。同步 ux-spec.md、design-system.md 与视觉稿/运行时共享资产。仅接受正整数行号，保留空输入、错误反馈及只读正文边界。

最终证据在 artifacts/pixel-followup-2026-10-15/goto-layout-approved/verified：原始 actual-empty.png 为584×252、DPI168，和参考外框尺寸一致（此前700×288）。采集前后前景、进程、边界及DPI稳定，目视完整无遮挡。标题/标签因产品边界不同；品牌图标、关闭按钮、输入框细节和字形仍有差异，不能计为完整像素配对。

验证：capture-goto-line-shell.cjs 原七项行为加按钮顺序、键盘正反循环、默认实机尺寸、错误状态布局共11项通过。verify-goto-layout.cjs 在DPR1/1.75/2、字号12/40、浅深主题共12组通过；这是静态缩放检查，不是跨屏DPI验收。共享9项资产、5个相关JS语法与git diff --check通过。更新旧live-shell跳转行Tab期望，未重跑全量；无C#改动，无需构建。

运行时MD5前→后：
- mockup.js：CA9A7349D1CDBA88B33616DE9AE67740 → FCEA8C42FDA8154FA07728BAB2B753E6。
- mockup.css：E2FC5FAEE0578F3BFADF0A6723F9C8C3 → A642041A37905E7BAD4ACCB79565CD60。
- live-data.js：019F54DFDE0629B0DDB67B91B46E50C7 → 7D53EE9C49D2136AB43A6ED582C98D09。
- bridge.js：F297BF8769E249CA0D804F4F4EEEF5A9不变。

首轮同步命令失败，行为测试仅验证按钮与键盘，没有计作布局验收；随后修正并重新同步/采集。styled、final保留用于尺寸迭代，最终以verified为准。启动命令沿上节采集器；正常finally清理，异常仅对日志中本批PID运行 taskkill /PID <PID> /T /F。Windows10排除，跨屏DPI未验证，整体像素复原目标未完成。

收尾独立核验：Augit PID24268/24400/8956/7696均退出，端口62342/57782/54018/50188无监听，专用profile dNJ5sh/FCtyv5/mFfscZ/1YRjTo均已删除。未暂存或提交混合工作区。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/verify-goto-layout.cjs $pw C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe
node tools/audit/capture-goto-line-shell.cjs $pw artifacts/goto-approved-new
# 采集器自动关闭本次实例；异常时仅用日志中的PID。
taskkill /PID <本次AugitPID> /T /F
```

最终收尾：16个本批文件UTF-8/LF通过，DOC_CLAIMS_OK、git diff --check通过；文档检查提示历史全量快照过期，未将2373断言旧快照计作当前通过。
