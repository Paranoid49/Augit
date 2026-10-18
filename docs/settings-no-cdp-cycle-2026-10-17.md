# 无 CDP 设置开关：真实系统输入与原生阶段（2026-10-17）

## 目的与方法

补齐设置开窗/关闭的**无 CDP**证据：正常入口启动，不附加远程调试参数；用真实系统鼠标点击标题栏设置按钮，用 `EnumWindows` 观察同进程新窗口，再按 Esc 关闭设置、正常关闭主窗。

- 启动：`tools/start-augit.ps1 -SettingsFile <副本>`，强制非增量构建，默认 WebView2 profile。
- 输入：`SetCursorPos` + `mouse_event`（逻辑坐标 `1522,22`；本机 PowerShell 为 DPI 非感知，对应物理齿轮 `2664,38`）。没有使用 CDP、UI Automation 或应用内测试钩子。
- 观察：`EnumWindows` 按 PID 找新可见顶层窗口，标题 `Settings — Augit`；关闭按 `Esc`。
- 计时：同时导出 `SettingsStageTrace`；`presented` 从设置子窗对象创建起算，不含点击前的主窗处理。`click→window visible` 是 OS 可见窗口时间，不等于最终绘制。

记录器 `settings-no-cdp.cjs` 在 25 秒录像结束后才等待 `exit.signal`；首个样本的设置开窗发生在录像结束之后，只保留原生阶段，不作为录像证据。

## 样本

四次独立正常入口、真实系统输入，均无 CDP：

| 样本 | PID | 原生 presented | 点击→可见窗口 | `git/detect→preview` | 控制器创建 | 捕获段 | 关闭 request→destroyed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1（未录到开窗） | 12512 | **492.0ms** | 未计时 | 63.3ms | 75.1ms | 85.0ms | 52.2ms |
| 2 | 27808 | **959.6ms** | 855ms | **407.3ms** | 95.9ms | 119.1ms | 54.0ms |
| 3 | 436 | **748.6ms** | 654ms | **139.1ms** | 109.9ms | 131.4ms | 57.3ms |
| 4 | 25524 | **909.3ms** | 854ms | **252.2ms** | 98.1ms | 141.8ms | 59.4ms |

- 四次原生呈现 **492–959.6ms**，没有复现 2904ms；但无 CDP 样本整体比附 CDP 的 271–449ms 慢。
- 方差主要落在 `git/detect` 到 `window/settings-preview` 之间（63–407ms）。该区间包含 `await document.fonts.ready` 和首次主题应用，是当前最可疑的慢开来源；尚未定位到具体字体请求或证明因果。
- 控制器创建 75–110ms、捕获 85–142ms、关闭 52–59ms，均无秒级尖峰。
- 点击到窗口可见 654–855ms，与原生呈现相差约 55ms；窗口可见不等于完整内容已呈现。

## 录像与白帧扫描

样本 4 的 DDA 录像（2880×1716，`dup_frames=1`，约 1400 帧/25s）：`check-settings-white-frames.py` 统计近白像素（三通道 ≥250）：

- 最大近白像素 **3,886,615**，整帧像素 4,942,080；
- 阈值 4,500,000 以上 **0 帧**；未检出整帧白闪候选。
- 限制：阈值只覆盖近全白帧；合法浅色页面和局部白块不会被判为候选，尚未做逐帧包络/同态参考。

证据目录：

- `artifacts/pixel-followup-2026-10-17/settings-no-cdp-cycle/`（样本 1，开窗未被录像覆盖）
- `artifacts/pixel-followup-2026-10-17/settings-no-cdp-cycle-2/`（样本 2）
- `artifacts/pixel-followup-2026-10-17/settings-no-cdp-cycle-3/`（样本 3；cleanup.json 被已终止旧任务覆盖，不作为清理凭据）
- `artifacts/pixel-followup-2026-10-17/settings-no-cdp-cycle-4/`（样本 4；cleanup、录像、白帧扫描完整）

样本 4 的 `cleanup.json`：PID 25524、临时目录 `Augit-no-cdp-AhXCXY` 已删除；主窗由 `CloseMainWindow` 正常关闭，端口未开，无残留进程。实际映射 `Augit.dll` `27D29A65…2C26944F`、`Augit.Infrastructure.dll` `2CCA339F…AF8190F1F`；`launch.json` 记录 `remoteDebugging=false`、`cdpConnections=0`。


## 字体预载候选（已撤回）

曾尝试在 `settings-surface.html` 预载 Regular/Bold WOFF2，验证是否能缩短 `document.fonts.ready` 等待；两次无 CDP 样本：

| 样本 | 原生 presented | `git/detect→preview` | 点击→可见 |
| --- | ---: | ---: | ---: |
| preload-1 | 442.6ms | 63.8ms | 420ms |
| preload-2 | 781.3ms | 154.4ms | 722ms |

与未预载的 492.0/959.6/748.6/909.3ms 相比没有稳定改善，样本 2 仍出现 154ms 字体/主题间隔。该候选已从产品撤回，`settings-surface.html` 不再包含字体 preload；两次证据保留在 `settings-no-cdp-preload-1/`、`settings-no-cdp-preload-2/` 仅作反证。撤回后设置入口/生命周期 24 项测试通过。

字体/主题间隔仍是慢开的下一个待定位点，但预载不是充分修复。


## 提前加载随包正文（对照后撤回）

在 `bootSettingsSurface()` 开始处、`settings/read` 之前，用 `document.fonts.load('13px "JetBrains Mono"', 'Ag 0123456789')` 提前启动字体请求，与宿主读取/检测并行；`document.fonts.ready` 仍在呈现前等待，不改变可见性或就绪条件。无 CDP 三次样本：

| 样本 | 原生 presented | `git/detect→preview` | 点击→可见 |
| --- | ---: | ---: | ---: |
| earlyfont-1 | 620.8ms | 117.9ms | 519ms |
| earlyfont-2 | 480.0ms | 50.8ms | 416ms |
| earlyfont-3 | 542.3ms | 87.2ms | 455ms |
| control（撤回后） | 469.0ms | 83.6ms | 454ms |

对比未提前加载的四次 492.0/959.6/748.6/909.3ms（字体间隔 63.3/407.3/139.1/252.2ms），提前加载三次呈现 480–621ms、字体间隔 50.8–117.9ms，看似收敛；但随后**撤回提前加载**又采到 control 469.0ms/83.6ms，说明系统/字体缓存波动足以产生同样低的样本，不能把改善归因于该候选。该候选因此**不保留**：`live-data.js` 已恢复，测试断言撤回，earlyfont 证据仅作探索记录。earlyfont-2 的 1410 帧近白扫描最大值 3,886,615、阈值 4,500,000 以上 0 帧，未检出整帧白闪候选；earlyfont-3 的同类扫描因耗时被终止，未计入。control 的录像未另做白帧扫描。当前 `live-data.js` MD5=`4A266F4927CDCE21B8FAD52168618787`、SHA256=`1041EC2BC5B349F254304CB31AC26899566D0B9D9A2FE2200ADC28704AD3FB01`。

## 深色主题白窗候选（DDA 专用，倾向伪影）

`settings-no-cdp --dark` 用深色设置副本开窗两次，DDA 录像均在呈现后约 1.2–1.3 秒出现 **1–2 帧纯白设置客户区**：

- 最大近白像素 **2,408,143**，恰好接近设置窗面积；帧 100/102 是完整深色表单，帧 101 为整块白色客户区（`dark-frame-100.png`、`dark-frame-101.png`、`dark-max-nearwhite.png`）。
- 两次 DDA 分别命中 1 帧（索引 101）和 2 帧（索引 91–92）；窗口可见后 375/441ms，原生 presented 445.3ms（另一次未逐项展开），关闭 64.5ms。
- 两次 GDI 全屏深色对照最大近白只有 **377**（约 20fps）；设置窗区域 116 帧/29fps、顶部条带 208 帧/52fps、全宽 1820×200 中心带 311 帧/103fps 均未命中。
- 但 `Graphics.CopyFromScreen` 中心 80×60 在 350 帧/116fps 中第 62 帧出现 25/25 白点；随后 400×300 中心采样两次分别 347 帧（第 55 帧）和 349 帧（第 57 帧）出现 **100/100 采样点全白**，保存为 `copyscreen-white-400.png`、`copyscreen-trace-white.png`。这是屏幕像素捕获，不是 DDA。
- `check-settings-white-frames.py` 的 4,500,000 阈值只覆盖整屏白，会漏掉约 2.4M 的设置窗白块；已用 `--threshold 2000000 --output dark-white-window-scan.json` 补记候选。浅色主题同样不能靠 4.5M 阈值证明设置窗区域无白闪。

结论：结合原生 trace，400×300 白帧发生在 `presented` 后 **1641.7ms**（presented 22:38:54.310、白帧 22:38:55.952；采样启动后 560ms），而 DDA 整窗白块也出现在呈现后约 1.2–1.3s。高帧率 GDI 因此确认：深色设置开窗后存在**真实的一帧白色闪烁，至少覆盖 400×300 中心区**；整窗范围由 DDA 记录，未在 GDI 全窗同步帧中重复。它不是初始绘制白帧，也不是单纯 DDA 伪影。已加入设置子窗擦除底色修复候选（见下节）；两次深色高帧率样本未再命中，仍需更多主题/DPI 样本。

## 白闪修复候选（设置子窗擦除底色）

根因进一步收窄（均为正常入口、手动深色副本，400×300 中心高帧率采样）：

- `--disable-gpu`：354 帧 max0；`--disable-gpu-compositing`：354 帧 max0；`--disable-direct-composition`：351 帧 max0；三者都消除白帧。
- `--disable-features=CalculateNativeWinOcclusion`：349 帧仍在第 51 帧命中 25/25，排除原生遮挡计算。
- 开窗后延迟 3.5s 再采样：354 帧 max0，确认白帧只发生在开窗后的短过渡期，不是采样启动触发。

据此在设置子窗处理 `WM_ERASEBKGND`，用 `_controller.DefaultBackgroundColor` 填充客户区（主窗不变），避免 DirectComposition 过渡露出默认白底。修复后两次独立深色高帧率采样 349/350 帧、同一进程连续三次开关 291/296/299 帧、浅色一次 348 帧，`maxWhiteSamples` 均为 **0**；深色设置截图仍为完整表单（`erasefix-dark-settings.png`）。

回归：正常入口冒烟通过（PID 19176、端口 54518，文件/终端/Changes→Diff、无页面错误、正常关窗、临时目录删除）；Shell **244** 项通过；设置开窗原生 257/229/244ms；关闭动作批 Cancel/Save/Esc/标题为 37/103/52/68ms（Save 103ms 略高于 100ms 但无卡顿），主题取消、持久化失败重试、延迟保存重开、Apply 重开通过；刷新严格 459.2/360.0/343.7/383.9ms，均 <500ms。Shell SHA256=`AF550779EECDFB4F3BF6B1BF32EEE0D1E283DA47C55F9397F830157B1E0024AB`。

限制：修复已覆盖两次独立开窗和同一进程三次连续开关，但浅色已补一次 348 帧 max0，但尚未覆盖全部 DPI 和更长周期；全局 `--disable-direct-composition` 对照虽消除白帧，但刷新首轮实测 509/519ms，已撤回，不能作为全局修复。

## 未采用的尝试

尝试用 `WEBVIEW2_USER_DATA_FOLDER` 构造首次运行 profile 未生效：`ShellOptions.ResolveWebViewDataFolder` 显式使用默认 `%LOCALAPPDATA%\Augit\WebView2\Shell\Session`，`start-augit.ps1` 也未暴露 `--user-data-folder`。因此该次“fresh profile”实际仍是默认会话，样本已删除，不计入证据；首次运行/冷缓存场景仍未验证。

## 结论与边界

- 无 CDP 路径能正常打开/关闭设置；十次原生呈现 445.3–959.6ms，未复现秒级卡顿；提前加载候选被对照样本否定并撤回，**慢开仍未修复**，只是把无 CDP 方差定位到字体/主题等待区间。
- 浅色样本 4 未检出整屏白闪候选；但深色 DDA 样本出现设置窗大小的白块候选（2.4M 像素，1–2 帧），GDI 顶带约 52fps 208 帧最大近白 0，倾向 DDA 伪影，但仍不能完全排除真实短闪。只覆盖少量开关与近白阈值，不能宣称全场景无闪。
- 真实系统输入由审计脚本合成，不是人工操作；窗口检测只按 PID 和可见性，不证明最终绘制。
- 本批未新增产品页面或功能；未回滚、暂存或提交既有修改。

## 复验

```powershell
node tools/audit/settings-no-cdp.cjs artifacts/pixel-followup-2026-10-17/settings-no-cdp-cycle-4
# 等待 launch/mapped 后创建 record.signal；录像就绪后按逻辑坐标 1522,22 点击设置，
# 观察 Settings — Augit 窗口，Esc 关闭后等待 cycle-screen.json，再正常关闭主窗并创建 exit.signal。
D:/Python/Python3119/python.exe -B tools/audit/check-settings-white-frames.py <证据目录>
```

启动/停止仍使用 `tools/start-augit.ps1` 与正常关窗；异常时只停止核实属于本批的 PID。
