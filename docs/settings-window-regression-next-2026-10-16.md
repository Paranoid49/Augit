# 设置双窗口与闪烁：本次复核和待确认方案

2026-10-16最新接续：用户已授权独立设置WebView，正式路径已实施并同步架构，下面“待确认／未实施”均为历史状态。设置不再搬移主控制器或捕获背景；真实源网页及Release随包回归通过，首次呈现白窗与关闭抢焦点已继续修补。开关录像已有一次有效全过程，但全部帧定量及整体像素验收未完成。证据、失败记录、哈希和启停见[独立设置整改](settings-independent-webview-2026-10-16.md)。

日期：2026-10-16。用户报告打开设置出现两份窗口且整个应用闪烁。本次没有完成修复，不以功能回归通过抵消屏幕失败。Windows 10 已按用户要求排除，跨显示器 DPI 保持未验证。

## 当前证据

在真实 Augit 仓库、默认配置副本、隔离 profile、随包网页和正常场景下，重复入口、延迟呈现／遮盖／关闭应答、九个标签、四分类和嵌套关闭回归通过。首轮目录为 artifacts/pixel-followup-2026-10-16/settings-user-regression-current，五条关闭 182/157/154/154/203 毫秒，网页错误为零。

原有测试只检查一个表单和原生 depth=1，没有检查快照内容。本次给 tools/audit/check-settings-close.cjs 增加 --background-content：连续三次开关设置，通过 CDP Page.getResourceContent 保存已加载的原始背景 PNG，在实例退出后才写证据，避免取证本身触发仓库监听。第一次使用画布导出被跨域规则拒绝，未绕过安全设置；改为读取已加载资源后完成，证据 settings-background-content-resource。第一份原图已目视确认只有主界面；其余原图保留待进一步自动比较。这仅说明本次未复现，不否定用户曾看到双窗口。

## 两项未采纳试验

第一项让初始背景 WebView 先隐藏、解码完成后显示并确认呈现，再移走业务控制器。新增两项生命周期检查，连同已有准备测试共 14 项通过；真实九标签组合回归通过，关闭 121–203 毫秒。没有完成开窗过渡录像；后续背景刷新录像仍失败，不能声称已改善首次开窗。候选源码与测试保存在 settings-hidden-background-video/candidate-*.txt，已精确撤销。

第二项只在模态父窗口设置 WS_EX_NOREDIRECTIONBITMAP；区别于此前遮盖子窗的同名试验。未改变业务 DOM、遮盖层、系统阴影参数或等待。仍失败，已精确撤销。

| 证据目录 | 采样帧／变化帧 | 最坏变化像素 | 内部变化 | 源时间最大间隔 |
| --- | --- | --- | --- | --- |
| settings-hidden-background-video | 316／2 | 9717，角236／边9481 | 0 | 41ms |
| settings-parent-redirection-probe | 329／2 | 4064，角131／边3933 | 0 | 35ms |

两组为 Light、正常场景、参考 fixture、DPR1.75、默认 GPU 路径，Computer Use 确认前景后采集 Desktop Duplication／FFV1；完整1820×1341、RGB容差2、排除0像素。两组源时间戳重复均为0，解码DTS量化警告保留，严格门禁均退出1。没有完整开关动画或阴影不退化证据，不外推内部露底已解决。录像SHA256分别为 fef54072510ae1f3b63b829f17abec5f5e6dedd4fcea7f4dcb9186d7215b644f 和 fb4b4dde8b86c3765fa7bb64dbd6bb868033f73a832132b0c67363308b9d0082。

恢复宿主 SHA256 为124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348；强制非增量 Debug 构建0警告0错误。首次恢复脚本因传输转义失败未执行，随后修正、核对哈希并重新构建，未将第一次构建算作恢复成功。恢复后 settings-restored-after-user-regression 再次通过真实九标签组合回归，关闭270/229/374/260/217毫秒，网页错误0。

## 待用户确认的结构调整

### 本次接续的代码核对

当前设置回归仍未解决。`openSettingsDialog` 已复用现有设置表单，`ShellModalHost.PresentCoreAsync` 已按表单身份复用原生窗口；这些防重条件不能证明背景 PNG 中没有第二份表单。`bindNativeModalHost` 的背景更新仍按 `modal-cover → 隐藏业务表单 → modal-refresh → 恢复业务表单 → modal-uncover` 执行，系统录像已有这条链路露底及边缘闪动的失败证据。尚未确认用户所见的第二份设置到底是额外原生窗口还是截图残影，不得将推测写成唯一根因。

本次没有再次安装已失败候选，也没有实施未经确认的第二个业务 WebView。当前运行时四文件 MD5 为 mockup.js=FF0DA3AD5B3300D01716EFBE618AA590、mockup.css=FA339DDEBB91AEE32BFCB2600701B046、live-data.js=AC578DFFFB854EC44B2FB8D6E7266B91、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9；其中 live-data.js 包含已完成的冲突保存生命周期修复，不是本次设置修复。检查时无 Augit 进程，上一冲突取证端口54580无监听，临时目录 Augit-merge-pair-wWEhFH 不存在。本次未启动应用、服务器或后台测试进程。

### 用户再次报告后的复核

2026-10-16接续复核：当前宿主SHA256仍与本文恢复值一致，四个网页MD5也未变化，未发现撤销的合成候选仍留在源码。本次重新执行设置重复入口、模态关闭优先级、背景变化归属三份定向测试，共18项通过；这只验证状态机，不证明用户所见的两个窗口或整窗闪烁消失。当前没有运行中的Augit进程，上一冲突诊断PID26520、端口55237和临时目录Augit-merge-pair-9dkfsy均已释放。本批没有启动应用或服务器。

检查确认现行设置入口复用已有表单节点；原生宿主也按表单身份复用窗口。但背景刷新仍依赖隐藏实际表单、捕获业务表面、以另一个截图控制器遮盖，以及恢复后的撤盖。现有记录中该交接已有桌面失败证据，因此不能通过增加单例断言、改字体或重新构建宣称修好。尚无本批证据确认用户看到的第二份设置是额外原生窗口还是背景图中的残影，保留此区别，不写成已确定唯一根因。

下面的独立设置方案已再次提交确认；确认前没有修改产品架构。取消截图交接后的验收必须检查正常入口的实际窗口与背景内容，并采集完整开关过程，不能只复用旧的DOM数量与原生depth断言。

建议先限定设置窗口引入独立业务 WebView，由一个原生 owned 窗口承载。它只挂载一份设置表单，不复制主界面、工作区监听、终端或标签状态。主窗口保留原业务控制器且持续呈现；打开、背景变化及关闭设置不再隐藏表单、搬移主控制器或刷新主窗截图。其它模态暂沿现有机制，避免一次改动全部模态。

复用现有 HTML/CSS 和设置服务；宿主维护创建中／打开／关闭中单例状态，重复入口只激活同一窗口。保存统一使用现有设置存储与单飞规则，完成后向主界面通知设置版本；取消不提交草稿。脏表单确认留在设置自己的页面；窗口关闭和创建失败均释放控制器、响应监听及 owned 窗口。日常启动与测试使用同一个入口及相同代码，测试只隔离存储。

成本是打开设置期间增加一个业务 renderer；预期能够移除设置路径上的主窗背景 renderer 和刷新遮盖 renderer，但必须按本实例进程树实际测量，不预先宣称内存减少。当前 docs/architecture.md 第63行要求重挂单一业务 WebView，AGENTS.md 明确规范冲突须确认，因此方案尚未实施，已向用户提出一次确认。

验收需包括正常入口冷开／重开／连击，真实仓库后台变化，OK／Cancel／Esc／标题关闭／Apply，保存失败及关闭中迟到应答，深浅主题、字体应用、完整开关及刷新桌面录像，以及实例资源释放。新增设置路径还必须证明没有 modal-cover／modal-refresh 截图请求，不只检查 DOM 数量。

## 启停与清理

本轮 Augit PID10824、27576、27660、15688、18076、26436、21344，录像27888／10612及前景18988／13120均由各驱动停止。对应隔离目录 Qw0X2Z、qvWzMr、fW25iO、KDhh34、CWl3cr、seoy2C、lIJauK 由 finally 删除；收尾另行核查。不触碰历史被拒绝清理目录。未回滚既有混合修改、暂存或提交。先前未完成的冲突布局工作保留，未在本批重新验收。

日常启动：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 正常关闭窗口；异常时只停止这次启动打印的PID。
taskkill /PID <本次PID> /T /F
```

审计驱动自动清理：node tools/audit/check-settings-close.cjs <Playwright模块路径> <新证据目录> --background-content --many-tabs --delay-cover --delay-present --delay-close --nested --bundled-web --navigation。此命令验证当前恢复版本，不安装上述已撤销候选。

最终独立核查上述11个PID不存在，7个端口无监听，7个隔离目录均不存在；共享9资产、14项现有入口／关闭JS测试、驱动语法、文档声明及git diff --check通过。历史全量快照仍过期，不计本轮结果。网页MD5与本批开始一致：mockup.js=FF0DA3AD5B3300D01716EFBE618AA590、mockup.css=FA339DDEBB91AEE32BFCB2600701B046、live-data.js=38A6F63C55532F4D67A210E5ED933F88、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。
