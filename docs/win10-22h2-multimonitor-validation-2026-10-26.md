# Windows 10 22H2 多显示器实机验证（2026-10-26）

> **后续（2026-10-26）**：本页记录的 M1/M2 已修复，并新发现/修复了混合 DPI 启动的 M3 与 200% 窄窗的 M4；修复代码 `7892c34`/`98c443f`，复验矩阵、负坐标、模拟热插拔、深色窄窗和 rc.2 安装器见[22H2 多显示器 DPI 修复与扩展验证](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)。用户已明确将三屏/物理拔线移出范围。本页保留修复前的原始观察，不把已修复项当作现状。
>
> 本批只做验证和取证，没有修改产品代码。
> 机器为 **Windows 10 Pro 22H2 `10.0.19045.6456`**，双显示器 2560×1440 + 1920×1080，两块屏默认都是 100%。
> 上一批 21H2 的验证见[Windows 10 多显示器验证（2026-10-25）](win10-multimonitor-validation-2026-10-25.md)；D1/D2 修复见[多屏几何修复](win10-multimonitor-geometry-fix-2026-10-25.md)。
> 验收结论：**22H2 的默认构建、双屏主流程、D1/D2 修复和全部自动化测试通过；真实混合 DPI 发现一个新的 P2 尺寸保持缺陷（M1）。**

## 1. 结论

- **默认 CET-on 构建在 22H2 上可用**：`csc.exe` 不再报 CET，`dotnet build --no-incremental` 0 警告 0 错误，`Augit.exe` 正常启动。上一台 21H2 `19044.2965` 需要用 `CETCompat=false` 才能运行；本机不需要。
- 双屏不同分辨率的主流程通过：默认主屏 `(80,80)/1180×760`；移到副屏 `(2660,120)` 后视口不变；主/副屏各自最大化到工作区；最大化还原、最小化恢复、普通位置和最大化状态跨重启持久化。
- D1（Settings 越界）和 D2（恢复尺寸只按主屏收敛）的修复在 22H2 上复验通过。
- 模态对话框在副屏居中、Owner 禁用、背景快照、右边界夹取和 Esc 关闭均通过；Settings 正常居中；终端 ConPTY 显示 PowerShell 提示符；同一工作区第二实例 83ms 退出并激活原窗。
- 自动化测试全部通过：Core **72/72**、Shell **262/262**、Infrastructure **206/206**。上一轮 21H2 被测试宿主挡住的 4 个 ConPTY 集成测试，在 22H2 上全部通过。
- **混合 DPI（临时把 DISPLAY1 设为 125%）发现 M1**：真实鼠标跨屏拖动不保持窗口的逻辑尺寸。1180×760@100% 移到 125% 后变成 1280×800 物理、CSS 视口 **1024×640**（最小布局）；拖回 100% 后仍是 1280×800 物理、CSS 视口 **1280×800**。详见 §5。
- 同一混合 DPI 状态下，模态对话框正确缩放（500×204 物理 = 400×164 逻辑）；Settings 得到 CSS 820×512，截图内容完整，记为观察项 M2，不写成已确认裁剪缺陷。
- 未验证：22H2 安装器和 rc.2 发布二进制、更多 DPI 组合、负坐标/三屏/热插拔、深色窄窗矩阵、`live-shell`（本工作区没有 Playwright 浏览器环境）。

## 2. 环境

| 项目 | 实测值 |
| --- | --- |
| 操作系统 | Microsoft Windows 10 Pro **22H2**，`10.0.19045.6456`（UBR 6456），x64 |
| 机器 | ASUS 组装机，AMD Ryzen 7 5700X（8C/16T），64 GB |
| GPU | NVIDIA GeForce RTX 2070 SUPER，驱动 `32.0.15.6094`；另装有 GameViewer Virtual Display Adapter，但本次没有启用虚拟显示器 |
| 主显示器 | AOC Q27G2SG4B+，2560×1440 @155 Hz，100%，工作区 `0,0–2560,1400` |
| 副显示器 | AOC 24G2W1G4，1920×1080 @144 Hz，100%，位于主屏右侧，工作区 `2560,0–4480,1040` |
| 任务栏 | 两块屏都显示 |
| WebView2 | `155.0.4283.62` |
| .NET | SDK `10.0.201`；运行时 `10.0.5`（用户目录） |
| Git / 产物 | `dsh` @ `9f52de2`（代码修复 `9552c69`），`Augit 1.0.0-rc.2+9f52de2` Debug |
| 外观 | Light，Microsoft YaHei UI 12，JetBrains Mono 13，DPR 1 |
| 构建 | 默认属性（CET 开启），0 警告 0 错误 |

`environment.json` 有机器可读版本。

## 3. 方法

- 默认构建：`dotnet build Augit.slnx --no-restore --no-incremental`，不设置 `CETCompat`。
- 真实外壳：`Augit.exe --web-root ... --workspace ... --settings-file ... --user-data-folder ... --browser-args --remote-debugging-port=...`；设置文件和 WebView2 profile 都是隔离的，不读写用户已有配置。
- 原生探针：`EnumWindows/GetWindowRect/GetClientRect/GetDpiForWindow/GetMonitorInfo`、`ShowWindow`、`SetWindowPos`、`WM_EXITSIZEMOVE`、`WM_CLOSE`。
- DPI 跨屏：用 `SendInput` 的真实鼠标拖动标题栏，而不是只用 `SetWindowPos`；同时记录程序化移动的差异。
- 混合 DPI 测试临时在 Windows 设置页把 **DISPLAY1** 改为 125%，验证 `GetDpiForMonitor` 为 `120/96`，测试后恢复 100%（`96/96`）。没有改分辨率、主题或任务栏。
- 截图用屏幕原始像素；最大化截图裁掉 8 px 不可见缩放边框。

## 4. 通过项

### 4.1 主窗口与双屏

| 状态 | 窗口矩形 | 客户区 | 工作区 | CSS 视口 |
| --- | --- | --- | --- | --- |
| 首次启动 | `80,80–1260,840` | 1180×760 | DISPLAY1 `0,0–2560,1400` | 1180×760 |
| 副屏普通 | `2660,120–3840,880` | 1180×760 | DISPLAY2 `2560,0–4480,1040` | 1180×760 |
| 副屏最大化 | `2552,-8–4488,1048` | 1920×1040 | 副屏工作区 | 1920×1040 |
| 主屏最大化 | `-8,-8–2568,1408` | 2560×1400 | 主屏工作区 | — |
| 主屏还原 | `700,300–1880,1060` | 1180×760 | 主屏 | 1180×760 |

- 副屏最大化不覆盖副屏任务栏；标题栏按钮和状态栏可见。
- 最小化/恢复、最大化/还原都回到原矩形；主屏最大化不跳到副屏。
- 关闭在副屏后设置保存 `2660,120/1180×760`；重启恢复同一位置。最大化关闭保存普通矩形和 `isMaximized=true`，重启在副屏最大化。

### 4.2 D1/D2 修复复验

- **D1**：Owner `4200,200` 时 Settings 为 `3440,244–4480,916`，右边缘正好贴工作区 4480；正常 Owner `2660,120` 时为 `2730,164–3770,836`；Owner 靠下时为 `2730,368–3770,1040`。
- **D2**：保存 `2600,100` / 2500×1300，在副屏启动得到 `2560,0–4480,1040`，客户区/CSS 视口 1920×1040。尺寸放得下的 `4000,100`/1180×760 仍保留原位置（`4000,100–5180,860`），没有被强行挪回单屏。

### 4.3 模态、Settings、终端与单实例

- 副屏最大化时 New Branch 为 `3320,438–3720,602`（400×164），中心与 Owner 一致，Owner 禁用，背景快照完整；Owner 靠右 `4200,200` 时模态夹到 `4080,498–4480,662`；Esc 关闭并恢复 Owner 焦点。
- Settings 在副屏正常居中 `2730,164–3770,836`，1040×672。
- 终端切换后 xterm 显示 Windows PowerShell 横幅与 `PS D:\github\Augit>`，10 行，无页面错误。
- 同工作区第二次启动 83 ms 退出（码 0），第一个窗口保持前台。

### 4.4 自动化测试

| 项目 | 结果 |
| --- | --- |
| 构建 | 0 警告 0 错误（默认 CET-on） |
| Core | 72/72 |
| Shell | 262/262 |
| Infrastructure | 206/206（含此前在 21H2 被阻塞的 4 个 ConPTY 测试） |
| UI 资产 / 脚本编码 / doc claims | 全部通过 |

## 5. 混合 DPI 结果

两块屏默认都是 100%；为验证之前未覆盖的跨屏 DPI，临时把 DISPLAY1 设为 125%，DISPLAY2 保持 100%。

### M1（P2）：跨屏 DPI 不保持逻辑窗口尺寸

- 复现：在 100% 副屏启动 1180×760 窗口（CSS 视口 1180×760），用真实鼠标拖到 125% 主屏；回程用位置-only `SetWindowPos`（不改尺寸）移回 100%。
- 期望：保持 1180×760 逻辑尺寸，即 125% 下 1475×950 物理像素。
- 实际：窗口停在 **1280×800 物理**（DPI 120），CSS 视口 **1024×640**，正好是布局下限；再以位置-only `SetWindowPos` 移回 100% 后窗口仍为 **1280×800 物理**，CSS 视口变成 **1280×800**，不是原来的 1180×760。
- 影响：混合 DPI 多屏下窗口尺寸不跟随逻辑尺寸；移到高 DPI 屏时布局被压到最小，移回后窗口变大。当前状态仍可见（1024×640 是声明下限），但不符合“系统 DPI 正常跟随、逻辑尺寸不变”的预期。
- 初步定位：`ShellWindow.OnDpiChanged` 只更新 `_effectiveDpi` 并调用 `DefWindowProc`，没有显式应用 `WM_DPICHANGED` 的 suggested rect，也没有按新旧 DPI 重新计算窗口物理尺寸；系统的 min track 尺寸把窗口压到当前 DPI 的最小值。
- 建议：在 `OnDpiChanged` 中按旧/新 DPI 计算逻辑尺寸对应的物理尺寸（并用 suggested rect 的位置），SetWindowPos 后夹到目标工作区，再同步 WebView2 边界；补跨 DPI 拖动测试。
- 证据：`measurements.json` 的 `mixed-dpi-main`；本批没有为 M1 的真实拖动状态截取 125% 主窗原图，数值来自真实鼠标拖动后的窗口/客户区/CDP 查询；回程是位置-only `SetWindowPos`，仍触发 DPI 变化。

### M2（P3 观察）：125% 下 Settings 的 CSS 视口偏小

- Owner 为 1280×800 物理（CSS 1024×640）时，Settings 原生客户区 1024×640，DPI 120，CSS 视口 **820×512**。
- 截图显示所有分区和底部按钮都在，没有明显裁切；这可能只是 Settings 的响应式布局，因此登记为观察项，不写成已确认缺陷。如果 Settings 有 1024×640 的最小布局要求，则需要单独修正。
- 模态在 125% 下正常：500×204 物理、400×164 逻辑，居中于 Owner。

混合 DPI 测试后已恢复：DISPLAY1 96 DPI、DISPLAY2 96 DPI，`PerMonitorSettings DpiValue=0`。

## 6. 未验证

- 22H2 安装器、缺 .NET/WebView2 流程、rc.2 便携/安装产物；本批是当前提交源码的默认构建，不是发布二进制。
- 100%/150%/175% 及更多混合 DPI 组合；Settings/模态打开期间切换 DPI；跨 DPI 保存/重启。
- 负坐标显示器、三块及以上、上下排列、热插拔/分辨率变化。
- 深色主题、窄窗、其他界面字号下的双屏矩阵。
- `live-shell`/Playwright 全量；本工作区没有 Playwright 浏览器环境。
- GameViewer 虚拟显示器（适配器存在，但本次未启用）。

## 7. 证据与清理

证据目录：[artifacts/win10-22h2-multimonitor-2026-10-26/](../artifacts/win10-22h2-multimonitor-2026-10-26/)

- [environment.json](../artifacts/win10-22h2-multimonitor-2026-10-26/environment.json)
- [measurements.json](../artifacts/win10-22h2-multimonitor-2026-10-26/measurements.json)
- [screenshots/](../artifacts/win10-22h2-multimonitor-2026-10-26/screenshots/)：主窗、副屏、最大化、模态与背景、Settings、D1/D2 修复、终端、混合 DPI 的 Settings/模态。
- [logs/](../artifacts/win10-22h2-multimonitor-2026-10-26/logs/)：默认构建、Core/Shell/Infrastructure 测试。
- [harness/](../artifacts/win10-22h2-multimonitor-2026-10-26/harness/)：窗口探针、启动器、CDP 客户端。

清理：所有 Augit 实例已关闭；没有残留 WebView2 子进程；测试创建的 `%LOCALAPPDATA%\Augit` 已删除；显示器缩放已恢复 100%；未改产品代码、主题或任务栏。`artifacts/*` 被 `.gitignore` 忽略，证据是本机的；关键数值已内联在本文。

## 8. 建议

1. 先修 M1：在 `WM_DPICHANGED` 中显式应用 suggested rect，并按旧/新 DPI 保持逻辑尺寸；用真实跨屏拖动复验 100%↔125% 和 100%↔150%。
2. M2 需要确认 Settings 是否有 1024×640 的最小布局；若有，125% 下应按 Owner 的逻辑尺寸和 DPI 正确分配，而不是只得到 820×512。
3. 在 22H2 上补一次真实发布产物的安装/启动；本批只验证了源码默认构建。
4. 本批没有修改代码，也没有提交；如需要，我可以先修 M1/M2 再按上一批流程提交和推送。
