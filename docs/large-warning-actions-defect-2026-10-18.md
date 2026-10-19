# 大文件 Warning 横幅动作不响应（2026-10-18）

> **2026-10-19 更正**：本文“动作无响应/缺陷”的结论已撤回。第 142 批用 CDP 对按钮派发 DOM click，`Hide Notification` 会写 `hiddenLargeFileWarnings` 并隐藏横幅，`Don't Show Again` 会写 `hideLargeFileWarning=true` 并隐藏横幅；第 141 批的物理合成鼠标点击未命中属于工具限制，不是产品缺陷。见[大文件 Warning 动作复核](large-warning-actions-verify-2026-10-18.md)。

## 1. 结论（已撤回）

第 140 批修复横幅可见后，继续验证 **Hide Notification** / **Don't Show Again** 两个动作：实际端两次点击都没有效果，横幅保持可见、设置未改变。登记为缺陷，未配对，页面级仍 48。

- 实际 PID 16180：31.4MB `huge.txt` 打开，横幅可见（`1D8100E0…F6F6`）。
- 点 **Hide Notification**（物理约 `2543,230`）后 1.2s 截图，横幅仍在（`9730B91A…2325`）；`settings.json` 的 `hideLargeFileWarning` 仍 false（该动作应只影响会话，但横幅也应消失）。
- 点 **Don't Show Again**（物理约 `2732,230`）后，横幅仍在（`EA2DAB57…1826`）；`hideLargeFileWarning` 仍 false（该动作应写设置并隐藏）。
- 两次点击时指针都在按钮上（按钮出现悬停下划线），`windowFromPoint` 为主窗口；因此不是“点空”，而是动作没有生效。
- 代码侧处理器存在（`live-data.js:7754-7773`，`[data-large-file-action]`，`hide` 写 `live.hiddenLargeFileWarnings`，`disable` 调 `settings/write`）；需进一步定位事件是否到达、`live.document.path`、`refreshAfterEvent("editorContent")` 或按钮的覆盖/命中问题。

## 2. 后续

- 用 CDP 在按钮上派发真实 click，观察 `window.__augitLive.hiddenLargeFileWarnings`、`hideLargeFileWarning` 与横幅 DOM，区分事件未到达还是状态/重绘问题。
- 修复后补自动化测试（click → 会话隐藏 / 设置持久化），正常入口复验“隐藏后重开同文件仍隐藏”“Don't Show Again 重启后仍隐藏”；再与参考的大文件提示动作配对。

## 3. 清理

- 实际 PID 16180 已结束；临时 `huge.txt` 删除；`settings.json` 恢复 `B8868855…DC3D`、`hideLargeFileWarning=false`；fixture 未变。
- 本批未修改代码；`cleanup-check-162.json`；未提交、未推送。
