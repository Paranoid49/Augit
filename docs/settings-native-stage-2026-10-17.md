# 设置原生阶段导出与多次正常入口开窗（2026-10-17）

## 目的与口径

上一批附 CDP 设置仍出现约 2904ms 呈现，present 请求约 2479ms，但缺少细分原生阶段，不能定位慢点。本批按要求在正常入口采集中同时导出已有 `SettingsStageTrace` 原生阶段，用于区分控制器创建、脚本注册、导航、页面初始化、原生显示、捕获和揭示。

全程使用 `tools/start-augit.ps1` 强制非增量构建、复制真实设置、默认 WebView2 profile 和系统缩放，不注入场景或测试专用条件。采集为**附 CDP**（`independent-settings.cjs --daily-launcher`）；无 CDP 真实系统输入开窗/关闭见[独立记录](settings-no-cdp-cycle-2026-10-17.md)。

## 采集

| 批次 | 命令要点 | 开窗数 |
| --- | --- | ---: |
| A | `--daily-launcher --startup-only --startup-count=3` | 3 |
| B | `--daily-launcher --startup-only --startup-count=10` | 10 |
| C | `--daily-launcher --startup-only --startup-count=5`（同时记录关闭） | 5 |
| D | `--daily-launcher --startup-only --startup-count=30` | 30 |

每次采集前设置 `AUGIT_SETTINGS_TRACE_DIRECTORY` 为已存在的证据目录，启动器继承该环境变量；窗口展示和关闭时各导出一份原生阶段文件。证据目录：

- `artifacts/pixel-followup-2026-10-17/settings-native-stage/`
- `artifacts/pixel-followup-2026-10-17/settings-native-stage-10/`
- `artifacts/pixel-followup-2026-10-17/settings-close-timing/`
- `artifacts/pixel-followup-2026-10-17/settings-native-stage-30/`

四次采集均通过单次启动器完成，脚本结束输出 `{"cleaned":PID,...}`，临时设置副本目录已删除；未留下 Augit 或 WebView2 进程。

## 结果

原生 `presentedMilliseconds`（宿主揭示完成）与驱动外部就绪：

| 批次 | 开窗序号 | 原生呈现 ms | 控制器创建 ms | 脚本注册 ms | 导航 ms | 准备 ms | 捕获 ms | 外部就绪 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | 1 | 303 | 10→47 | 47→84 | 84 | 192 | 约 100 | 887 |
| A | 2 | 296 | 3→40 | 40→76 | 76 | 179 | 约 96 | 320 |
| A | 3 | 278 | 2→39 | 40→76 | 77 | 171 | 约 89 | 306 |
| B | 1–10 | 279–450 | 2–82 | 39–136 | 76–137 | 171–319 | 68–99 | 299–1031 |

- 48 次开窗（A 3 + B 10 + C 5 + D 30）的原生呈现全部在 **271–449ms**，没有复现此前的 2904ms 慢样本；平均 380ms，无一次达到 500ms。D 批 30 次单独统计最大值 443ms、平均 400.7ms。
- 外部就绪计时受 50–100ms 轮询与首次驱动等待影响；首窗原生 303ms 而外部 887ms，不能把外部轮询当作产品呈现时间。
- 后续开窗（B 批次第 5–10 次）整体变慢，主要增加在控制器创建（约 73–82ms）、脚本注册/导航（132–137ms）和准备阶段（277–319ms），`capture-start → capture-end` 仍稳定在 68–99ms；未发现单次捕获尖峰。
- 每次 `open()` 都并发发起 8 次 `window/settings-open` 并断言返回同一窗口；A/B/C/D 共 48 次未出现重复设置窗口，`window.__augitErrors` 为空。

### 一次 297ms 开窗的原生阶段

| 阶段 | 相对起点 ms | 与上一阶段间隔 |
| --- | ---: | ---: |
| created | 0 | — |
| controller-create-start | 9.1 | 9.1 |
| controller-create-issued | 10.1 | 1.0 |
| controller-create-end | 45.9 | 35.8 |
| script-register-start | 45.9 | 0.05 |
| script-register-end | 80.4 | 34.5 |
| navigation-start | 80.9 | 0.5 |
| settings/read | 109.5 | 28.6 |
| settings/fonts | 124.0 | 14.4 |
| git/detect | 130.4 | 6.4 |
| window/settings-preview | 177.0 | 46.6 |
| window/settings-ready | 178.9 | 1.9 |
| prepare-start | 179.3 | 0.4 |
| controller-visible | 180.2 | 0.7 |
| prepared | 184.5 | 4.2 |
| window/settings-present | 193.9 | 9.4 |
| activation-start | 195.8 | 1.9 |
| capture-start | 214.2 | 18.4 |
| capture-issued | 216.2 | 2.0 |
| capture-end | 294.1 | 77.9 |
| presented | 297.3 | 0.2 |

快速样本的主要耗时是控制器创建约 36ms、脚本注册约 35ms、页面装配（导航到 ready）约 98ms、准备约 5ms、激活+捕获约 99ms。慢样本若再次出现，该导出可直接区分控制器创建、导航/装配、激活或 `CapturePreviewAsync` 哪一段发生秒级等待。


## 关闭阶段补充（同一批正常入口）

在 `--startup-only --startup-count=5` 中每次点击 Cancel 后等待 `window/settings-state.active=false`，并保留原生关闭阶段。证据：`artifacts/pixel-followup-2026-10-17/settings-close-timing/`。

| 项 | 实测 |
| --- | --- |
| 驱动观测关闭（上界） | 83 / 69 / 139 / 83 / 151 ms |
| 原生 `close-request → destroyed` | 24.8–46.1 ms（5 次） |
| 原生 `destroy-start` 前 | 8.9–19.5 ms（owner 释放、DwmFlush、重新激活） |

本样本未复现关闭卡顿；驱动值包含 CDP 点击派发与 25ms 轮询，不能当作无 CDP 关闭耗时；无 CDP 关闭在独立记录中为 52.2–59.4ms。

另一次正常入口动作批（`--daily-launcher`，证据 `settings-actions-round9/`）分别点击 Cancel、Save、Esc、标题关闭，驱动观测关闭为 **53 / 80 / 81 / 69ms**，均低于 100ms；主题取消、持久化失败重试、延迟保存重开、Apply 重开均通过，`__augitErrors` 为空。OK 保存、Esc 与标题关闭已计时，但仍是 CDP 驱动上界；嵌套关闭未计时。


## 开窗/关闭录像（附 CDP，补充证据）

为检查设置开窗与关闭是否出现整窗白帧，额外录制了一次完整开关（2880×1716、25fps、DDA）：`artifacts/pixel-followup-2026-10-17/settings-cycle-recording/`。录像 225 帧，三份参考状态（关闭前、已打开、关闭后）逐帧检查：

- `nearWhitePixels` 在关闭前/后约 3,592,650，已打开约 1,614,108；从未接近整帧 4,942,080，未检出整窗白帧。
- 最大包络外像素 3,541（另一次指针移开对照 5,403），范围集中在标题栏与鼠标位置的“Settings”原生提示，不是设置窗透明/白底。
- “Settings”提示来自 CDP 点击使渲染器悬停在设置按钮、而系统光标在另一位置造成的原生 tooltip；将系统光标移到 (20,20) 后提示随光标移动，确认不是产品窗口绘制。该提示不计产品闪烁，也不能作为通过证据。
- 限制：25fps 采样、仅一次开/关、包络分析会把合法动画/光标计入差异；不能证明每个 Present 无闪；无 CDP 开关录像见独立记录（约 1400 帧近白扫描、0 整帧白闪候选）。

证据：`settings-cycle-recording/`、`settings-cycle-pointer-cleared/` 及其 `cycle-frame-analysis.json`、`cycle-worst-envelope.png`。

## 真实双击补充

另一次无 CDP 真实系统双击设置按钮（`settings-no-cdp-double-1/`）：首次打开后 300ms 再点同一齿轮，800ms 后仍只有 1 个 `Settings — Augit` 顶层窗口（HWND 44239008），无第二个控制器或 trace；原生呈现 875.8ms、`git/detect→preview` 244.7ms、关闭 62.7ms。该样本支持真实输入下不重复开窗，但不代表所有快速多次点击。白闪修复后 Shell AF55 又复验一次：双击后仍只有 1 个 Settings（HWND 30738504），原生呈现 407.4ms、字体间隔 84.6ms、关闭 48.1ms。

## 结论与边界

- 本批只完成“补齐细分原生导出并观察 48 次正常入口开窗、5 次关闭”，**没有修复慢开，也没有复现慢样本**。不得据此宣称慢开已解决。
- 原生阶段不等于桌面最终呈现，也不证明实际字形或透明帧；外部就绪轮询间隔不能当作产品延迟。
- 后续若再出现秒级样本，必须同时保留本次同类的原生阶段文件、`opening-times.json` 和外部时间线；不能只登记 present 总耗时。
- 设置重复窗口在 48 次开窗中未复现；关闭卡顿在 5 次 Cancel 中未复现（原生 `close-request → destroyed` 24.8–46.1ms），无 CDP 关闭为 52.2–59.4ms；Cancel/Save/Esc/标题关闭为 53–81ms，嵌套关闭仍未计时。
- 本批未回滚、暂存或提交既有混合修改；无新增页面或功能。

## 复验

```powershell
$out = 'artifacts/pixel-followup-2026-10-17/settings-native-stage-10'
New-Item -ItemType Directory -Force -Path "$out/native-trace" | Out-Null
$env:AUGIT_SETTINGS_TRACE_DIRECTORY = (Resolve-Path "$out/native-trace").Path
node tools/audit/independent-settings.cjs <playwright 模块路径> $out --daily-launcher --startup-only --startup-count=10
```

脚本自身清理临时设置与实例；异常时只停止其输出的本批 PID。
