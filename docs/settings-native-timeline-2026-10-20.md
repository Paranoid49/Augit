# Settings 正常入口原生时间线（2026-10-20）

本文补齐 `remaining-inputs` 中“完整 presented 原生时间线”缺口：不用 CDP，用正常入口、真实鼠标键盘、DWM cloak 探针和宿主自带的 `AUGIT_SETTINGS_TRACE_DIRECTORY` 原生阶段记录，测量点击到 presented、首内容和关闭。

## 1. 方法

- 构建身份：`Augit.exe` `8BB9E6F3…0D50`、`Augit.dll` `43CA3C81…97CC`、`ProductVersion=0.1.0+bb90984…`；`mockup.js` `C063D300…5E12`、`mockup.css` `449AEC4D…55D5`、`live-data.js` `7D481F50…185E`。
- 正常入口：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/start-augit.ps1 -Workspace D:/github/Augit -SettingsFile <隔离副本>`；隔离副本取自当前全局设置（`B8868855…DC3D`），窗口最大化、Light、DPI 168、2880×1800。
- 环境变量 `AUGIT_SETTINGS_TRACE_DIRECTORY` 指向 `artifacts/settings-native-2026-10-20/trace`，由宿主导出原生阶段（`created`、`controller-create-*`、`script-register-*`、`settings/read`、`settings/fonts`、`window/settings-preview/ready`、`prepare-*`、`window/settings-present`、`capture-*`、`presented`、`close-*`、`destroyed`）。
- 输入：真实 `SendInput` 鼠标点击 Settings 齿轮（物理 `2664,38`，UIA 实测矩形中心），每周期前 `SetForegroundWindow` 并确认前景。
- presented 判定：`EnumWindows` 找到 `Settings — Augit` 后轮询 `DwmGetWindowAttribute(DWMWA_CLOAKED)` 首次为 0；随后立即 `PrintWindow(PW_RENDERFULLCONTENT)` 截图，再隔 60ms 截第二张。
- 关闭：聚焦设置窗、真实 `Esc`，轮询 `IsWindow` 变 false。
- 3 个周期连续执行；结束后另做“3 次快速点击齿轮”的重复窗口检查。

## 2. 结果

| 周期 | 点击→窗口创建 | 点击→presented | 创建→presented（trace） | Esc→窗口消失 |
| --- | --- | --- | --- | --- |
| 1 | 400.5ms | **585.2ms** | 488.7ms | 60.5ms |
| 2 | 345.8ms | **519.4ms** | 452.4ms | 36.3ms |
| 3 | 339.0ms | **495.2ms** | 423.7ms | 32.3ms |

原生 stage 分段（3 周期范围）：

| 分段 | 耗时 |
| --- | --- |
| created → controller 创建返回 | 50–58ms |
| 脚本注册（`script-register`） | 62–90ms |
| `navigation-start` → `settings/read` | 57–59ms |
| `settings/read` → `settings/fonts` | 23ms |
| `settings/fonts` → `window/settings-preview` | 65–66ms |
| `window/settings-ready` → `prepared` | 4–8ms |
| `prepared` → `window/settings-present` | 17–28ms |
| `activation-start` → `capture-issued` | 44–52ms |
| `capture-issued` → `capture-end`（CapturePreviewAsync） | **85–94ms** |
| `capture-end` → `presented` | 3–12ms |
| `close-start` → `destroyed` | 53–61ms |

## 3. 首内容与重复窗口

- 6 张 presented 截图（3 周期 × presented/+60ms）SHA256 全部为 `0BD5FC02…`：揭开时已经是完整的 Appearance 页（主题、字号、控件、底部按钮齐全），60ms 后像素完全一致。这组样本没有出现“先白/空再填内容”的可见中间帧。
- 3 次快速点击齿轮（间隔约 80–120ms）后，EnumWindows 只找到 **1** 个可见 `Settings — Augit` 窗口；单飞仍然成立，没有重复设置窗。
- `capture-issued → capture-end` 均 85–94ms，未触发 500ms 超时分支；本批没有复现 2904ms 慢样本，也没有取得真实超时后提前揭示的样本。

## 4. 边界

- 只有 3 个周期、Light 主题、单屏 2880×1800/DPI 168；不是 13 周期统计的替代，也不是全窗录像。
- “presented 首帧已完整”是 6 张同哈希截图的结论，证明力限于 60ms 采样；不能据此宣布“无白闪”全场景通过。
- 未覆盖 Cancel/OK/标题关闭三条路径的关闭时长（本批只用 Esc），未覆盖深色、跨屏 DPI、2904ms 慢样本与 capture 超时分支。
- 这是原生窗口生命周期/首内容证据，不是 Settings 控件像素或交互验收；页面级仍 **50**、像素仍 **0**。

## 5. 清理

- 正常入口 PID 5328；3 个周期结束后设置窗均通过 Esc 关闭，主窗正常关闭；无残留 Augit/rg。
- 隔离设置副本未写回全局设置；全局设置仍 `B8868855…DC3D`。
- 临时目录 `artifacts/settings-native-2026-10-20/` 保留原始 trace 与截图（Git 忽略），不随代码推送。
