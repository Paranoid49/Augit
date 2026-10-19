# 大文件 Warning 动作复核（2026-10-18）

## 1. 结论

第 141 批把“Hide Notification / Don't Show Again 点击无响应”记为缺陷；本轮用 CDP 在按钮上派发 DOM click 复核，两个动作**实际都生效**，先前是**物理合成鼠标点击未命中**的工具限制，不是产品缺陷。

- **Hide Notification**（CDP click）：
  - `live.hiddenLargeFileWarnings = ["huge.txt"]`，`hideLargeFileWarning=false`；
  - 立即查询时横幅还在（重绘未完成），1.2s 后 `banner=false`（证据 `442C0E61…B0D2`）；设置未变（会话内隐藏，符合语义）。
- **Don't Show Again**（CDP click）：
  - `live.hideLargeFileWarning=true`，`settings.json` 的 `hideLargeFileWarning=true`；
  - 1.2s 后 `banner=false`（证据 `138CCF52…7AFE`）；设置已持久化。
- 物理 `ui-drive` 合成点击在同一按钮坐标上不触发（第 141 批），但 DOM click 与状态/设置链路均正常；不能据此判产品缺陷。

## 2. 解释与边界

- `ui-drive` 通过系统鼠标事件点击，命中/焦点与浏览器 `element.click()` 不同；本轮没有再用物理鼠标重试，也没有验证真实用户鼠标（工具限制与产品行为分开记录）。
- `Don't Show Again` 的“重启后仍隐藏”未复验：本轮只确认设置写入 true；之后已恢复基线 false。
- `Hide Notification` 的“重开同文件仍隐藏”只验证了同一会话内 `hiddenLargeFileWarnings` 与横幅消失，未重新打开该文件。

## 3. 更正

- 第 141 批 `large-warning-actions-defect` 的“动作无响应/缺陷”结论**撤回**；保留“物理合成点击未命中”的工具限制记录。
- 页面级配对仍 **48**；像素合格仍 **0**；参考端大文件提示未采。

## 4. 清理

- 实际 PID 11360/8628 已结束；临时 `huge.txt` 删除；`settings.json` 恢复 `B8868855…DC3D`（`hideLargeFileWarning=false`）；fixture 未变。
- 本批未修改代码；`cleanup-check-163.json`；未提交、未推送。
