# 设置初始化期间关闭的生命周期修复（2026-10-17）

## 原因与修改

正常窗口可在WebView2环境创建或文档脚本注册等待期间销毁。原实现只在控制器创建后检查销毁状态；环境返回后可能继续使用旧HWND，脚本返回后可能继续访问已关闭控制器，迟到失败还可能通知所有者或弹错误框。

ShellWindow.cs在环境创建、脚本注册完成和异常处理入口补充销毁检查；关闭期间返回的控制器立即Close并清字段。ShellWindow.Settings.cs拒绝已关闭子窗或所有者的初始化失败，避免旧窗口影响重开的设置。未增加产品延时、测试专用COM条件或改变显示顺序，不把该修复归为慢开的根因修复。

## 正常入口证据

以下目录均位于artifacts/pixel-followup-2026-10-17/，经tools/start-augit.ps1强制非增量构建，真实配置隔离副本、默认Session和正常DPI。附CDP，不是无CDP桌面验收。launch、process-identity及main-runtime-identity保留实际exe、加载DLL、网页和字体身份。

| 目录 | PID / 端口 | 结果 |
| --- | --- | --- |
| settings-initialization-close | 28252 / 60054 | 未就绪关闭，102ms观察到退出；脚本注册开始后销毁 |
| settings-lifetime-regression | 27984 / 60369 | 八类回归通过，10窗正常关闭阶段完整，无网页错误 |
| settings-initialization-close-final | 20876 / 58371 | 最终产品代码，未就绪关闭，75ms观察到退出 |

最终样本控制器创建结束48.83ms、脚本注册开始48.88ms、Dispose开始51.93ms、控制器关闭75.10ms、Disposed76.00ms。没有script-register-end：实际覆盖了等待期间关闭，但进程已退出，不能证明迟到续接实际执行。scriptRegistrationRaceVerified和fullPresentationVerified均保持false。

回归十窗呈现304/272/517/430/432/462/430/427/444/423ms，Cancel/OK/Escape/标题关闭观察57/122/54/76ms；关闭分析器确认10窗全部完整，原生关闭38.49–50.74ms。这批先于最后的旧设置失败保护；最终保护后已再跑定向测试和最终未就绪关闭。实际进程仍加载Nahimic模块，只登记诊断性能，不推断注入因果或全场景达标。

## 自动化与身份

- 新增5项源码契约，修前失败、修后通过；它们不等同可控COM异步行为测试。
- 公共生命周期变更触发一次Shell模块243项测试，13秒通过；最后失败入口保护后仅IndependentSettingsTests与SettingsStageTraceTests 34项，156ms通过，没有重复全量。
- 审计退出判据抽为settings-startup-close.cjs，6项行为测试覆盖未就绪前提、退出等待、超时、权限错误、关闭失败和非法PID；与9项隐藏启动及5项生命周期契约合计20项，175.75ms通过。
- dotnet format的两文件空白检查、审计语法及git diff --check通过。
- 收尾9项共享资产、Debug25项网页镜像、22项PowerShell编码及DOC_CLAIMS_OK通过；旧全量快照过期提示仍保留，不将其算本批全量通过。
- Debug DLL SHA256：62BE2A9637AC367090EC62CE0DEB322F83F70935A4C36EFB145A31A2FFD6958F；Release未更新。
- 四网页MD5不变：mockup.js=661C7570EB5805BB8759F2B2C6DF169F，mockup.css=D671A6BA32C439BD4CBD774862FB084C，live-data.js=D64AB5C4E97AA7D3ED60F456555A34AE，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

## 清理与复验

final/cleanup-check.json独立确认上述三个PID、三个监听端口和三个隔离目录均无残留。历史参考目录因自动审批拒删仍保留，本批未重试，不宣称整体零残留。没有暂存或提交混合工作区。

定向测试：node --test tools/audit/settings-startup-close.test.cjs tools/audit/shell-initialization-lifetime.test.cjs tools/audit/settings-hidden-startup.test.cjs。实机初始化关闭：node tools/audit/independent-settings.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/settings-next --daily-launcher --close-owner-during-startup。需要阶段导出时先将AUGIT_SETTINGS_TRACE_DIRECTORY设为该已存在绝对目录。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止优先关闭窗口，异常时只对本次PID执行taskkill /PID <本次PID> /T /F。

慢开、闪帧、55页全模态、终端和冲突完整逐行及完整阴影仍未完成。Windows10排除本轮，跨屏DPI未验证；整体goal保持active。
