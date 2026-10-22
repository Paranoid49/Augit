# Settings 冷启动首开实测（2026-10-22）

本文补 Settings“2904ms 慢样本”和“真实 capture 500ms 超时”两项输入中的第一项：在一个**全新进程、首次打开 Settings** 的冷路径上，用宿主原生阶段 trace 测量 created→presented 与 capture 耗时。结论只覆盖本次冷开，不覆盖 60fps 全窗录像与跨屏 DPI。

## 1. 条件

| 项目 | 值 |
| --- | --- |
| Augit | PID 21968，正常关闭 |
| 构建 | `0.1.0+4b193af…`，`-SkipBuild` 正常入口 |
| 工作区 | `D:/JetBrains/PyCharm-2026.2.1-reference/fixture` |
| 隔离设置 | `D:/github/Augit/.tmp/settings-pixel-pair-3-2026-10-22.json`（Light、Microsoft YaHei UI 12、362/321 面板） |
| 触发 | 标题栏齿轮，进程内**第一次**打开 Settings；首次 Settings 没有先前窗口或控制器 |
| 采集 | `AUGIT_SETTINGS_TRACE_DIRECTORY=D:/github/Augit/artifacts/settings-cold-2026-10-22/trace2`，宿主 `SettingsStageTrace` 原始 JSON |
| 桌面 | 单屏 2880×1800，DPI 168（1.75），最大化 |

## 2. 原生阶段

trace `frequency=10000000`（100ns tick），下列毫秒为相邻/相对阶段换算。

| 阶段 | 相对 created | 说明 |
| --- | ---: | --- |
| created | 0.0ms | 设置窗口对象创建 |
| controller-create-start → end | 26.9 → 59.6ms | WebView2 控制器创建 |
| script-register-start → end | 59.8 → 156.6ms | 设置脚本注册 |
| navigation-start | 157.0ms | 开始导航 |
| settings/read | 173.1ms | 读取设置 |
| settings/fonts | 233.3ms | 字体解析 |
| git/detect | 240.5ms | Git 检测 |
| window/settings-preview | 298.4ms | 首内容预览 |
| window/settings-ready | 300.6ms | 设置页 ready |
| prepare-start → prepared | 301.1 → 312.1ms | 显示准备/合成 |
| window/settings-present | 350.3ms | WebView 呈现请求 |
| activation-start | 353.1ms | 激活 |
| capture-issued → capture-end | 382.3 → 467.0ms | `CapturePreviewAsync` **84.7ms**，未触发 500ms 超时 |
| uncloak-start → presented | 479.6 → 480.0ms | 撤 DWM cloak |

按 `created` 到 `presented`：**480.0ms**；`prepare-start` 到 `presented`：**178.9ms**。

## 3. 结论

- 本次**全新进程首次**打开 Settings 的 created→presented 为 **480.0ms**，低于 500ms；没有复现历史 2904ms 慢样本。
- `CapturePreviewAsync` 本次 **84.7ms**，没有进入 `capture-timeout` 分支；因此本次只证明冷路径没有慢 capture，不证明 500ms 超时分支已被正常入口触发。
- 控制器创建约 **59.6ms**、脚本注册约 **96.8ms**、设置读取约 **60.2ms**、首预览约 **57.9ms**、显示准备约 **9.9ms**，是冷路径的主要分段；后续若继续优化，优先看脚本注册、设置读取与首预览，而不是重复测量已稳定的撤 cloak。
- 本次没有全窗 60fps 录像；不能替代深色/浅色可见帧证据，也不能证明所有机器无白闪。冷开只测 1 次，不是统计分布。

## 4. 清理

- 通过 UIA 的 `Cancel` 正常关闭 Settings，随后正常关闭 Augit；PID 21968 无残留。
- 全局 `%LOCALAPPDATA%\Augit\settings.json` SHA256 仍为 `8840E775…462CF`。
- trace 原始文件：`artifacts/settings-cold-2026-10-22/trace2/settings-21968-…-2.json`（Git 忽略）。
