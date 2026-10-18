# 背景刷新置底试验与采集前景修正

日期：2026-10-16。总体整改继续，未缩减55页、全模态、刷新、终端/冲突逐行及系统阴影目标。Windows10按用户裁决排除，跨屏DPI未验证。

## 本批结论

先前两项口头判断经精确检索已纠正：`mockup.css` 已有 `data-native-background-capture` 的 opacity 规则；正文固定 JetBrains Mono 13 及设置页正文选择器移除也已实现。未针对这两项重复修改。界面字体与正文等宽字体不是同一设置，不把仍有界面字体列表当成正文整改未落地。

本批只试验保持截图控制器及子窗可见，撤盖时以 HWND_BOTTOM 将截图移到实时控制器之后，诊断就绪状态同时检查子窗顺序。原图片等待、DWM边框/阴影、业务DOM、系统缩放及恢复帧逻辑不变。该候选仍失败，已经逐字节撤回，未进入Release。

## 实际证据

证据根目录：`artifacts/pixel-followup-2026-10-16/refresh-cover-zorder/`。成功录像位于 `light-main-first/`，为普通入口、Light、175%系统缩放、UI12、JetBrains Mono13。首次遮盖明确为零字节/零子窗，随后六次背景更新。

独立DDA无损FFV1录像316帧，完整表面1820×1341，每帧2440620像素；容差2，排除0像素，49帧变化。最严重第65帧（1.190秒）有12726像素变化：四角408、其余16px边缘12318、内部0，最大RGB差237。`worst-frame-65.png` 已目视核对。最大采样间隔38ms、原始时间戳重复0；解码DTS量化警告保留，未删除失败帧。

六次观察延迟515.37、329.43、269.40、279.26、296.48、281.98ms；首次超过500ms。`sampledWholeSurfacePassed` 与 `fullPresentationVerified` 均false。本次未采到内部变化不能撤销历史内部露底证据。DDA时间不能直接套用GDI墙钟关联器，未据相对时间宣称闪变具体发生于某条桥接应答后。

录像SHA256：`8459f27e1b0bac24f201ff01953479fc8235bdd94d6dc44bf4293cb87bad10af`。`experiment-summary.json`、`refresh-surface-analysis.json`、`modal-surface-trace.json` 和原始录像/PNG保留。该结果否定以单纯置底替代显隐能解决问题，不证明所有子窗方案均不可行。下一步需要检查首个子窗参与合成时的父窗框架表面，不能重复本次方案或放宽边缘验收。

## 保留的采集修正

前三次在录像前被严格前景身份检查拒绝：PID4508前景是Edge；PID9340/9944前景是Augit所有者而不是Settings。原生输入工具将标题点击识别为WebView子窗并拒绝，未绕过该工具检查。三个失败实例均finally清理，不计有效录像。

新增 `--activate-main` 检查点：在主窗仍可交互时，通过computer-use枚举并激活真实窗口，观察后才继续打开Settings。此路径成功取得模态前景，未更改截图身份门禁。新增 `capture-window-checkpoint.cjs` 限时等待信号并在成功/超时/访问错误时清理；3项测试通过，108.82ms，采集脚本语法通过。它仅是审计工具，不改变日常启动。

本批只完成一组有效六轮录像及一次整表面分析，没有重复产品全量测试。候选Debug构建8.67秒、恢复Debug构建1.82秒，均0警告0错误。试验源码SHA256 `92F5F897FFB4E53DCF62D4F70D843C51BFAA7B75E3FE9532194BFEAE83108D7B`；恢复为 `5C7D5BDBC0BCFD2219BB9BBC3E2319E5EB3E9978653954EB2BC0402432DDA209`，恢复前同时验证候选与备份哈希，未覆盖其他修改。

## 清理及复验

独立检查本批Augit PID4508/9340/9944/2220、录制器11868均已退出；端口64575/50219/64013/62808无监听；临时目录3dag8Y/AKxWYW/fv1SGD/oFMOTN、信号文件和本批恢复备份均已清理，见 `cleanup.json`。历史拒绝删除的目录未触碰。无暂存、提交或回滚既有混合修改。

```powershell
node --test tools/audit/capture-window-checkpoint.test.cjs
node --check tools/audit/capture-shadow-shell.cjs
# 当前正式实现复验，不含已撤回候选；必须使用新证据目录。
node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新目录> Light --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames --activate-main
# 在真实主窗已激活并观察后，创建驱动输出的main-active-ready文件；等待超时不会继续。
D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py <新目录> --require-stable
# 正常驱动finally清理；异常时仅填本次输出的实例PID。
taskkill /PID <本次Augit或录制器PID> /T /F
```

日常启动仍为 `powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`，正常关闭窗口即可；不要添加上述审计参数。

四份网页MD5本批未改：mockup.js `6E61081C842CA96770B3F8BB56E13B0A`；mockup.css `99A3D1E61C7D12BA82DD25CE2E685A76`；live-data.js `641B29F3BC50EC22500924F64B9C63CE`；bridge.js `F297BF8769E249CA0D804F4F4EEEF5A9`。
