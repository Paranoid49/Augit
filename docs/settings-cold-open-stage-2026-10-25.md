# Settings 冷开与历史 2904ms 慢样本：阶段结论（2026-10-25）

> 结论：2026-10-24c 的 3 次全新进程冷开为 **491.4 / 451.1 / 567.2ms**；
> 原生 `created→presented` 为 **411.2 / 371.0 / 469.4ms**，`CapturePreview` 为 **159.0 / 89.9 / 88.5ms**。
> 567ms 样本的捕获只有 88.5ms，慢点仍在 WebView2 设置页初始化/导航与原生显示，
> 不是点击、捕获或关闭。历史 2904ms 样本可定位到捕获/呈现等待（prepare 410ms、
> capture 完成 2889ms、present 请求 2478.8ms），但无法重复；48 次正常入口开窗与后续批次均未复现。
> 当前没有重复数秒卡顿、无法关闭或错误输入处理，**不升级为核心发布阻塞**；保留为性能风险。

## 1. 当前阶段数据（2026-10-24c，RC 基线前）

夹具与入口：真实 Debug 外壳、隔离设置、真实鼠标点击设置按钮、原生
`SettingsStageTrace` 导出；3 个全新进程。

| 轮次 | 点击→presented | 原生 created→presented | CapturePreview | prepare | Esc 关闭 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | **491.4ms** | 411.2ms | 159.0ms | 4.1ms | 53.7ms |
| 2 | **451.1ms** | 371.0ms | 89.9ms | — | 29.1ms（Cancel） |
| 3 | **567.2ms** | 469.4ms | 88.5ms | — | 89.7ms（标题关闭） |

- 567ms 轮次的 capture 只有 88.5ms，且 presented 截图是完整 Appearance 页；
  慢在 `created→presented` 的 469ms（设置页初始化、字体、控制器/导航与捕获前后等待）。
- 三轮都是全新进程；点击轮询只占约 12–20ms，不能解释 567ms。
- 三轮 presented 截图完整，没有空白页；`CapturePreview` 均未接近 500ms 超时。

## 2. 历史 2904ms 样本

`settings-controller-issued-2026-10-17.md` 记录的 2904ms 样本：

- 控制器创建 16→85ms（69ms），准备完成 410ms；
- 捕获完成 2889ms；
- `window/settings-present` 请求耗时 2478.8ms。

可定位到 **CapturePreview 等待/呈现**，不是控制器创建或准备阶段。此后加入
`CapturePreviewAsync` 500ms 上限与 `capture-timeout` 阶段；正常设置开窗未再触发超时。
该样本早于细分原生导出，无法进一步区分捕获调用与捕获等待；保留为未重复风险，不伪称已修复根因。

## 3. 未复现证据

- `settings-native-stage-2026-10-17.md`：48 次正常入口开窗（3+10+5+30），原生呈现 271–449ms，
  平均 380ms，无一次达到 500ms。
- `settings-no-cdp-*`：无 CDP 正常入口多轮，原生呈现 339–497ms；无 CDP 全窗 599.2–747.5ms
  （含驱动轮询上界），未触发 capture 超时。
- 2026-10-24c 3 轮：491/451/567ms，仍有一次超过 500ms，但 capture 不是慢点。

## 4. CapturePreview 受控故障边界

- 真实 500ms 超时无法在正常设置页自然出现；不能靠无限等待取证。
- 现有 `ShellSettingsCaptureTimeoutTests` 已扩为 3 项：30ms 机制测试、**500ms 生产上限直接测试**、
  正常完成/异常传播测试；本批 **3/3 通过（548–558ms）**。这是**单元级受控故障证据**，
  不冒充真实慢样本。
- 关闭路径沿用 2026-10-24c 的真实开窗证据：Esc 53.7ms、Cancel 29.1ms、标题关闭 89.7ms；
  presented 截图完整，没有无法关闭或错误输入。
- 未对 `CapturePreviewAsync` 注入进程挂起；此前尝试挂起 renderer/GPU 未使捕获等待，
  已作为否证记录，不再重复。

## 5. 2026-10-25 复跑尝试

- 为按当前环境复核，使用 `settings-cold-open.ps1` 在 2026-10-25 复跑 3 轮；三次均 `opened=false`，
  没有进入产品阶段。原因：脚本把设置按钮坐标硬编码为最大化窗口的 `(1522,22)`；本次窗口以
  1434 CSS 宽恢复，点击落在窗口外。这是**采集脚本输入未命中**，不是 Settings 产品失败。
- 已修正脚本按客户端宽度计算按钮 x（右缘左 124 CSS），并恢复该脚本的 UTF-8 BOM；
  修正后的复跑仍受当前窗口/前台状态影响，未取得比 2026-10-24c 更强的新样本。
  因此本结论以 2026-10-24c 的原生阶段数据为准，并明确标注复跑输入问题。
- 复跑未修改产品代码；全局设置 SHA256 保持 `8840E775…2CF`，实例均正常关闭。

## 6. 结论与影响

- Settings 冷开目标 500ms 未连续达成（1/3 超过，最高 567ms）；没有重复数秒卡顿、无响应或无法关闭。
- 按任务口径，单次 567ms 不自动升级为核心发布阻塞；保留为已披露性能余项。
- 2904ms 历史慢样本未复现，慢点指向捕获/呈现等待；500ms 超时保护已有单测，真实超时仍缺自然样本。
- 不修改规格、不把 567ms 写成通过，也不把 2904ms 写成已修复。

## 7. 证据

- `artifacts/settings-cold-open-2026-10-24c/settings-cold-open.json` 与 `native-trace/*.json`。
- `docs/settings-native-stage-2026-10-17.md`、`docs/settings-controller-issued-2026-10-17.md`。
- `tests/Augit.Shell.Tests/ShellSettingsCaptureTimeoutTests.cs`。
- 2026-10-25 采集脚本修正：`tools/audit/settings-cold-open.ps1`（按客户端宽度定位，BOM 保留）；
  失败复跑记录在 `artifacts/settings-cold-open-2026-10-25c/settings-cold-open.json`。
