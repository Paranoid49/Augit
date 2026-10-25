# Windows 10 多显示器实机验证（2026-10-25）

> 本批只做验证和取证：没有修改产品代码、没有改显示器分辨率或缩放、没有提交。
> **机器实际是 Windows 10 21H2（10.0.19044.2965）**，不是产品声明的 Windows 10 22H2（19045）。
> 因此本报告只能登记 21H2 的实机结果；**不能把“Windows 10 22H2”从“未验证”改成“已验证”**。
> **后续（同日）**：D1/D2 已修复并复验（代码提交 `9552c69`），见[多屏几何修复](win10-multimonitor-geometry-fix-2026-10-25.md)；本报告的修复前测量保留作回归基线。

## 1. 结论

- 本机为 Windows 10 Pro 21H2 x64、双显示器 2560×1440 @100% + 1920×1080 @100%、任务栏在两块屏都显示。Augit `1.0.0-rc.2` 从当前提交源码构建后可以启动、渲染、加载 Git Log 只读内容，并启动显示 PowerShell 提示符的 ConPTY 终端。**这里的启动使用 `-p:CETCompat=false` 全量重建；默认构建在本机启动即退出 `0x80131506`，见 §6.1。**
- 以下双屏行为通过：默认位置在主屏；移动到副屏后 CSS 视口与内容不变；主/副屏各自最大化到自己的工作区；最大化还原、最小化恢复；普通位置和最大化状态跨重启持久化；保存位置在虚拟屏外时回退到默认位置；同一工作区第二次启动在 77 ms 内激活已有窗口并退出。
- 模态对话框在副屏上的定位、Owner 禁用、背景快照、右/下工作区边界夹取、跨屏拖动后的夹取、Esc 关闭并恢复焦点均通过。
- Settings 在正常位置可以居中打开、重复打开复用同一窗口、Esc 返回焦点；但 **Settings 没有工作区夹取**。
- **发现 2 个与不同分辨率多屏直接相关的 P2 几何缺陷**：
  1. **D1**：主窗靠右/靠下时，Settings 仍按主窗中心摆放，1040×672 的窗口可被放到工作区外；实测右侧 830 px 在屏外，只剩 210 px 可见。
  2. **D2**：启动恢复尺寸只按主显示器工作区收敛，不按窗口所在显示器收敛；在大主屏保存 2500×1300 后移到 1920×1040 副屏重启，窗口右侧 620 px、下方 360 px 在工作区外。
- 另外用 `--dpi 168` 模拟 175% 时，默认 `(80,80)` 会让窗口底部压到任务栏下 10 px（D3）。这只是审计参数模拟，真实混合 DPI 未验证，因此记为风险，不记为已确认缺陷。
- 测试：Core **72/72**、Shell **253/253**、Infrastructure **202/206**。4 个 ConPTY 集成测试在本机被测试宿主问题挡住；真实 App 的终端已显示 PowerShell 提示符，因此不把测试失败写成产品终端失败。
- 仍未验证：Windows 10 22H2、真实混合 DPI/`WM_DPICHANGED`、安装器与缺运行时流程、干净 VM、rc.2 发布二进制、负坐标/三块以上显示器、热插拔/分辨率变化。

## 2. 环境

| 项目 | 实测值 |
| --- | --- |
| 操作系统 | Microsoft Windows 10 Pro **21H2**，`10.0.19044.2965`（UBR 2965），x64 |
| 机器 | ASUS 组装机，AMD Ryzen 7 5700X（8C/16T），64 GB 内存 |
| GPU | NVIDIA GeForce RTX 2070 SUPER，驱动 `31.0.15.3623` |
| 主显示器 | AOC Q27G2SG4B+，2560×1440 @155 Hz，100%（96 DPI），`\\.\DISPLAY1`，工作区 `0,0–2560,1400` |
| 副显示器 | AOC 24G2W1G4，1920×1080 @144 Hz，100%（96 DPI），`\\.\DISPLAY2`，位于主屏右侧，工作区 `2560,0–4480,1040` |
| 虚拟屏 | `0,0–4480,1440`；任务栏在两块屏都显示，因此工作区高度分别少 40 px |
| WebView2 | Evergreen `155.0.4283.62` |
| .NET | SDK `10.0.201`（用户目录本机安装）；运行时 `10.0.5`；系统原有运行时为 8.0.25 |
| Git | `2.45.1.windows.1` |
| Augit | `1.0.0-rc.2`，源码提交 `6955cc9c04f62f32aa3ad735b0cb978c5815070e`，Debug `net10.0-windows` / `win-x64`，验证构建 `CETCompat=false` |
| 外观 | Light（System→Light），界面 Microsoft YaHei UI 12，等宽 JetBrains Mono 13，DPR 1 |
| 隔离 | 独立 `--settings-file`；没有读写用户原有 `%LOCALAPPDATA%\Augit\settings.json`（单实例测试除外，见 §6） |

`environment.json` 记录了同一组机器可读值。

## 3. 方法

- 真实外壳：直接启动 `src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe`，传 `--web-root`、`--workspace`、隔离 `--settings-file`，环境变量指向本机 .NET 10。
- CDP：通过 `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>` 打开只读调试口，查询 `innerWidth/innerHeight/devicePixelRatio/scrollWidth/scrollHeight`、字体与页面错误，并用 `Runtime.evaluate` 触发与真实点击相同的 DOM 事件。没有用 CDP 注入窗口几何。
- 原生探针：`EnumWindows`、`GetWindowRect`、`GetClientRect`、`GetDpiForWindow`、`GetMonitorInfo`、`ShowWindow`、`SetWindowPos`、`WM_EXITSIZEMOVE`、`WM_CLOSE`；进程按 PID 归属，只操作本批启动的实例。
- 截图：PIL 从屏幕取窗口或客户区；最大化窗口先截外层窗口再裁掉 8 px 不可见缩放边框。
- 未改显示器缩放、分辨率、任务栏位置或系统主题；没有把 `--dpi` 模拟结果当成真实 DPI。

## 4. 结果明细

### 4.1 主窗口与跨屏

| 状态 | 窗口矩形（物理 px） | 客户区 | 显示器/工作区 | CSS 视口 |
| --- | --- | --- | --- | --- |
| 首次启动 | `80,80–1260,840` | 1180×760 | DISPLAY1 `0,0–2560,1400` | 1180×760，DPR 1 |
| 移到副屏 | `2660,120–3840,880` | 1180×760 | DISPLAY2 `2560,0–4480,1040` | 1180×760 |
| 副屏最大化 | `2552,-8–4488,1048` | 1920×1040 | DISPLAY2 工作区 | 1920×1040 |
| 主屏最大化 | `-8,-8–2568,1408` | 2560×1400 | DISPLAY1 工作区 | —（客户区已测，未单独读 CDP） |
| 还原 | `2660,120–3840,880` | 1180×760 | DISPLAY2 | 1180×760 |

- 最大化后客户区严格等于该显示器工作区；副屏任务栏没有被盖住，标题栏按钮和状态栏都在视口内。
- 最大化/还原、最小化/恢复都回到同一矩形；跨屏移动不改 CSS 视口，也没有出现页面错误。
- `WM_GETMINMAXINFO` 返回最小跟踪尺寸 1024×640，与 96 DPI 的布局下限一致。

### 4.2 位置与最大化持久化

- 在副屏 `(2660,120)`、1180×760 关闭后，设置写入 `left=2660, top=120, width=1180, height=760, isMaximized=false`；重启恢复到同一矩形和同一 CSS 视口。
- 在副屏最大化后关闭，设置保留普通矩形 `2660,120,1180,760` 且 `isMaximized=true`；重启在副屏最大化，客户区 1920×1040，CDP 视口 1920×1040。
- 把保存位置改成 `(9000,5000)`（虚拟屏外）后重启，窗口回到主屏 `(80,80)`，没有出现在屏幕外。

### 4.3 模态对话框

| 场景 | 结果 |
| --- | --- |
| 副屏最大化时打开 New Branch | 窗口类 `Augit.Shell.Modal`，400×164，中心 `(3520,520)` 与 Owner 中心一致，完全在副屏工作区内；Owner `enabled=false` |
| 对话框背景 | 截图显示主界面快照完整，没有白底或空洞 |
| Owner 靠右 `(4200,200)` | 对话框 `4080,498–4480,662`，右边缘贴工作区右界 |
| Owner 靠下 `(2660,780)` | 对话框 `3050,876–3450,1040`，下边缘贴工作区下界 |
| Owner 跨屏 `(2300,250)` | 920 px 在副屏、260 px 在主屏；对话框落在副屏并保持与 Owner 同中心 |
| 对话框跨屏拖动后结束移动 | 从主屏拖到 `(4300,900)`，`WM_EXITSIZEMOVE` 后夹回 `4080,876–4480,1040` |
| Esc | 对话框销毁，Owner 重新启用，前台焦点回到主窗 |

### 4.4 Settings

- Owner 在副屏 `(2660,120)`、1180×760 时，Settings 为 `2730,164–3770,836`，1040×672，完整落在工作区；第二次点 Settings 只有 1 个 Settings 窗口，前台仍是它；Esc 关闭后主窗重新启用并取得焦点。
- **缺陷 D1**：Owner 在 `(4200,200)` 时，Settings 为 `4270,244–5310,916`。工作区右界 4480，窗口右缘 5310，**830 px 在屏外**；副屏上只剩左侧 210 px 可见（截图 08）。Settings 创建逻辑只按 Owner 中心居中，没有像模态那样调用工作区夹取。

### 4.5 其他平台行为

- 同工作区第二次启动：第二个进程 **77 ms** 退出，退出码 0；副屏上的第一个窗口保持前台。
- 终端：切到 Terminal 后 xterm 显示 Windows PowerShell 横幅与 `PS D:\github\Augit>`，10 行已渲染；没有页面错误。说明该机 ConPTY 能启动 PowerShell；本批没有在真实壳内输入命令。
- 启动到第一个可见原生窗口：3 轮 118.9 / 118.3 / 119.0 ms。这里只测到原生窗口出现，**不是**首内容或可交互时间，不能替代启动性能验收。
- 页面字体：`Microsoft YaHei UI` 与 `JetBrains Mono` 均 `document.fonts.check(...) = true`；UA 为 Windows NT 10.0 / Chrome 155 / Edge 155。

### 4.6 高 DPI 模拟（不是真实混合 DPI）

- `--dpi 120`：客户区 1475×950，CDP 视口 1180×760，DPR 1.25，无滚动溢出。
- `--dpi 168`：客户区 2065×1330，CDP 视口 1180×761，DPR 1.75；窗口在 `(80,80)`，底边 1410，比 1400 的工作区下界多 10 px，状态栏末 10 px 落在任务栏区域。由于任务栏仍是 100%，这只能说明**模拟条件下**默认位置没有按工作区回收；真实 175% 需单独验证。

## 5. 缺陷

### D1 — Settings 没有按显示器工作区夹取（P2，已修复）

- 复现：主窗 1180×760 放在 `(4200,200)`（DISPLAY2，工作区 `2560,0–4480,1040`），点击 Settings。
- 期望：像模态一样把 Settings 矩形夹到 Owner 所在显示器的工作区。
- 实际：`4270,244–5310,916`；右侧 830 px 在屏外，只有 210 px 可见，搜索框、正文和底部按钮都被裁掉。
- 影响：主窗靠近屏幕右/下边时，Settings 无法完整操作；单屏也会出现，不是多屏专属。
- 证据：[08-defect-settings-offscreen-visible-210x672.png](../artifacts/win10-multimonitor-2026-10-25/screenshots/08-defect-settings-offscreen-visible-210x672.png)。
- 修复：已在同日后续批次实现 `ShellModalGeometry.CenterAndFit` 并复验；本报告的测量保留为修复前基线。见[多屏几何修复](win10-multimonitor-geometry-fix-2026-10-25.md)。

### D2 — 恢复尺寸只按主屏工作区收敛（P2，已修复）

- 复现：设置 `window = { left:2600, top:100, width:2500, height:1300, isMaximized:false }`，在 DISPLAY2（1920×1040 工作区）启动。
- 期望：按保存位置所在显示器的工作区收敛，或至少保证整窗可达。
- 实际：`2600,100–5100,1400`，客户区 2500×1300，CSS 视口 2500×1300；右侧 620 px 在屏外，下方 360 px 在工作区外（其中 320 px 已在物理屏外）。
- 影响：主屏比副屏大时，用户在主屏保存的大窗口一旦移到副屏再重启，就会大部分落在屏外；内容已渲染但无法直接点到。
- 证据：[09-defect-oversized-on-secondary-visible-1880x980.png](../artifacts/win10-multimonitor-2026-10-25/screenshots/09-defect-oversized-on-secondary-visible-1880x980.png)。
- 修复：已在同日后续批次实现目标显示器解析、尺寸收敛和超尺寸整窗夹取，并复验；本报告的测量保留为修复前基线。见[多屏几何修复](win10-multimonitor-geometry-fix-2026-10-25.md)。

### D3 — 模拟 175% 时默认位置压到任务栏下（P3，风险）

- `--dpi 168`、默认 `(80,80)`：2065×1330，底边 1410 > 工作区 1400。
- 真实 175% 未验证；模拟中任务栏仍是 100%，所以不把它写成已确认产品缺陷。若在 1440p/175% 实机复现，需要把初始位置也夹到工作区。

## 6. 构建与测试

### 6.1 构建环境的 CET 问题

- 本机全局 NuGet 源为空，首次 restore 报 NU1100；用 `--source https://api.nuget.org/v3/index.json` 恢复后成功。
- 直接用 SDK 10.0.201 构建时，`csc.exe` 以 `0x80131506` 崩溃，输出：
  `Your Windows doesn't fully support CET. Please install all available Windows updates.`
  `testhost.exe`、`VBCSCompiler.exe` 同样失败。事件日志见 `logs/event-log-cet.txt`。
- 本机是 21H2 Pro（`19044.2965`），不在 .NET 10 当前 [supported-os 列表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)的常规 Windows 10 项内（列表只保留了带 ESU 的 21H2/1809/1607 等项；22H2 也不在其中）。这是平台补丁级别与 CET 支持问题；是否属于产品支持范围需要发布裁决，不是 D1/D2 那种几何缺陷。
- 为完成验证，使用了一个只在本批测试目录中的 `csc.exe` shim：它用 `dotnet exec csc.dll` 编译，构建结果 **0 警告 0 错误**。
- **更正**：默认构建（不关闭 CETCompat）的 `Augit.exe` 在本机启动即退出 `0x80131506`；本报告所有 UI 证据来自 `-p:CETCompat=false` 且 `--no-incremental` 的构建。之前写的“`Augit.exe` 本身正常启动”只在 CET 关闭构建下成立，不是默认产物行为。事件日志见 `fix/default-build-cet-crash.txt`。
- 本机 `--browser-arguments` 不是有效开关（正确的是 `--browser-args`）；最初误传导致启动错误框，已纠正，不计产品缺陷。

### 6.2 测试结果

| 项目 | 结果 |
| --- | --- |
| `Augit.Core.Tests` | 72/72 通过 |
| `Augit.Shell.Tests` | 253/253 通过 |
| `Augit.Infrastructure.Tests` | 202/206；4 个 ConPTY 集成测试未通过 |
| 构建 | 0 警告 0 错误（csc shim） |

4 个失败都停在 `Augit.Terminal.TestHost.exe`：

- 作为 .NET 10 apphost 启动时退出码 `-2146233082`（0x80131506），没有结果文件；
- 改为 `dotnet exec Augit.Terminal.TestHost.dll` 后能跑，但在等待初始提示符时 8 s 超时（cmd 和 powershell 两种 shell 都是）。

真实 App 的终端在同一台机器上能显示 PowerShell 提示符（截图 10），因此本报告把 4 项登记为**测试宿主/环境阻塞，不是产品终端失败**，也不把 Infrastructure 写成 206/206。

## 7. 未验证与边界

- Windows 10 **22H2**（19045）及其安装器、缺运行时下载/取消/失败/重试；本机没有 22H2。
- **真实混合 DPI**：两块屏都是 100%，本批没有改显示器缩放，因此没有真实 `WM_DPICHANGED`、跨屏重栅格化、每屏不同任务栏高度的证据。
- rc.2 的便携/安装产物：本 checkout 没有发布二进制，本批验证的是当前提交源码构建。
- 负坐标显示器、三块及以上显示器、上下排列、显示器热插拔/分辨率变化、远程桌面。
- 深色主题、窄窗、其他界面字号下的多屏矩阵。
- 真实鼠标拖动跨屏（本批用 `SetWindowPos`/`WM_EXITSIZEMOVE` 覆盖几何路径，没有用 SendInput 模拟完整拖动）。
- 长时间运行、真实日常仓库操作与整窗像素对照。

## 8. 证据与清理

证据目录：[artifacts/win10-multimonitor-2026-10-25/](../artifacts/win10-multimonitor-2026-10-25/)

- [environment.json](../artifacts/win10-multimonitor-2026-10-25/environment.json)：机器、显示器、DPI、WebView2、.NET、产物身份。
- [measurements.json](../artifacts/win10-multimonitor-2026-10-25/measurements.json)：逐项期望/实际/判定、缺陷和测试汇总。
- [screenshots/](../artifacts/win10-multimonitor-2026-10-25/screenshots/)：12 张窗口/客户区/缺陷截图（编号 01–12；09 只截可见区域）。
- [logs/](../artifacts/win10-multimonitor-2026-10-25/logs/)：构建成功/失败、CET 事件日志、Core/Shell/Infrastructure 测试日志。
- [harness/](../artifacts/win10-multimonitor-2026-10-25/harness/)：窗口探针、启动器、CDP 客户端和启动计时脚本；只在本批使用，不是产品工具。
- [fix/](../artifacts/win10-multimonitor-2026-10-25/fix/)：D1/D2 修复后的截图、构建/测试日志、默认构建 CET 崩溃日志。

清理：

- 所有本批 Augit 实例已正常关闭；没有残留本实例 WebView2 子进程（按父进程和命令行归属核对）。
- 没有改显示器缩放/分辨率、系统主题或任务栏设置；`environment.json` 的 `cleanup.monitorScalingChanged=false`。
- 为单实例测试临时生成的 `%LOCALAPPDATA%\Augit\settings.json` 已删除，整个 `%LOCALAPPDATA%\Augit`（本次验证时创建）随临时数据一并删除。
- 本批临时改写的 .NET 10 SDK `csc.dll` 与 `ConPTY` 测试宿主 exe 已恢复原文件；`C:\Users\WL\.dotnet-sdk-10` 本身是为本次验证安装的用户目录工具链，保留以便复验，不属于产品代码或系统设置。
- 隔离设置、脚本和截图源文件位于已删除的 `.tmp`；证据副本在 `artifacts/`。
- `artifacts/*` 默认被 `.gitignore` 忽略，因此这些证据是**本机证据**，不随提交分发；关键数值已内联在本文 §4/§5，clone 后仍可读结论，但不能复核原始 PNG。

## 9. 建议

1. 已修 D1（Settings 夹取工作区）：复用 `ShellModalGeometry.CenterAndFit`，补 Owner 靠右/靠下测试，见[修复记录](win10-multimonitor-geometry-fix-2026-10-25.md)。
2. 已按“整窗可达优先”修 D2（目标显示器工作区收敛 + 整窗夹取）；高 DPI 下低于 1024×640 的例外仍需真实混合 DPI 验证，见[修复记录](win10-multimonitor-geometry-fix-2026-10-25.md)。
3. 真正的 Windows 10 验收需要一台 **22H2** 机器；本机 21H2 的结果只能作为“更老版本”补充证据。
4. 混合 DPI 必须在允许临时改一屏缩放并能恢复的条件下单独做；本批没有改系统缩放，不能把本机结果写成“跨屏 DPI 已验证”。
5. 产品声明的 Windows 10 支持范围与 .NET 10 当前 supported-os 列表需要产品侧重新裁决；至少要在发布说明里写清补丁级别/ESU 条件和 CET 相关限制。
