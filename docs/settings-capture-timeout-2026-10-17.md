# 设置捕获等待上限与 2904ms 慢样本定位（2026-10-17）

## 历史慢样本的原生阶段

用户指出的 2904ms 样本已定位到既有证据 `artifacts/pixel-followup-2026-10-17/settings-controller-issued-fonts/opening-times.json`：

| 阶段 | 时间 |
| --- | ---: |
| controller 创建完成 | 85ms |
| 导航开始 | 150ms |
| prepare 开始 | 402ms |
| 窗口显示（cloak 中） | 410ms |
| rendered | 2889ms |
| presented | **2904ms** |
| `window/settings-present` 请求 | **2478.8ms** |

控制器/导航/字体均在 410ms 内完成，慢点发生在 **窗口显示后的捕获/渲染等待**，不是控制器创建或字体枚举。该批早于 `capture-issued` 细分阶段，因此只能把范围收窄到 rendered 前的捕获等待，不能逐段证明是 `CapturePreviewAsync` 还是其前的 Show/激活。

## 修复

在 `PresentSettingsWindowAsync` 中给 `CapturePreviewAsync` 加 **500ms 等待上限**：

- 正常捕获实测约 68–150ms，不会触发上限；
- 超过 500ms 时记录 `capture-timeout`，继续揭示设置窗，不再把开窗拖到秒级；
- 晚到的捕获任务用 `OnlyOnFaulted` 续接观察异常，避免未观察异常；
- 该上限只影响超过正常捕获 3–7 倍的异常样本。

新增 `ShellSettingsCaptureTimeoutTests`：超时返回 false、正常完成返回 true、异常按原任务抛出。

## 回归

- Shell **246** 项通过（含 2 项捕获超时测试）。
- 正常入口冒烟修正 `.diff-layout`/`.diff-code-line` 等待竞态后连续 **3/3** 通过（PID 13060/22396/28268，端口 64655/53303/55175），`CloseMainWindow`、端口关闭、临时目录删除均通过。
- 设置正常入口 3 次：presented **281 / 304 / 286ms**，rendered 279/295/279ms，未触发超时。\n- 追加 4 次冷启动首窗、同时 4 个 PowerShell CPU 占用进程：presented **352 / 332 / 337 / 343ms**，rendered 345/323/332/333ms，host 973/992/1178/985ms，仍未复现 2904ms，也未触发 500ms 捕获上限。\n- 捕获超时构建上深色白闪复验：288 帧/约2.5s，`maxWhiteSamples` **0**，擦除底色修复仍有效。
- 当前 Shell SHA256 `15A1AE14B74D049B3F3B695F4BC052BEB606CF22E9F87B606CE58DFE594A74CD`；Infrastructure 仍 `2CCA339F…AF8190F1F`。

## 边界

- 2904ms 样本不可重复，500ms 超时路径尚未在真实慢样本中触发；不能宣称慢开已完全修复。
- 超时后跳过捕获确认，理论上可能在极端情况下提前揭示未完成表面；现有设置白闪修复（WM_ERASEBKGND 主题底色）降低风险，但需要后续慢样本验证。
- 该修复不改变正常捕获顺序；正常开启仍在 capture 完成后揭示。

证据：`settings-capture-timeout-startup/`、`normal-smoke-wait-1/2/3/`、历史 `settings-controller-issued-fonts/opening-times.json`。
