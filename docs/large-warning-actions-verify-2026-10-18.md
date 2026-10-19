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

### 2.1 2026-10-19 真实输入闭环（更正）

- 正常入口、隔离夹具、28,500,000 字节 `huge.txt`，用 `SendInput`/`SetCursorPos` 真实鼠标点击两个按钮；**没有启用 CDP**。
- Hide：横幅在 1.2s 截图里仍在，但随后消失；设置仍为 `false`；关闭标签再打开同一文件仍隐藏；正常重启后横幅重新出现。
- Don't Show Again：点击后设置写为 `true`，横幅消失；重启后同一文件直接打开且横幅不出现。
- 因此“物理合成点击未命中/工具限制”也不准确：点击可达，只是本次 2.5MB 预览的**屏幕可见重绘**落在约 3.7–4.2s（屏幕像素采样），1.2s 观察点太早。该耗时是本样本的性能观察，不据此判缺陷。
- 参考 2026.2.1 用同一文件打开为普通编辑器，无 Warning 横幅、无对应动作；参考默认配置未被修改，故本项没有可配对的参考警告 UI。
- 页面级仍 48、像素合格仍 0。详见[真实输入与持久性闭环](large-warning-real-input-2026-10-19.md)。

## 3. 更正

- 第 141 批 `large-warning-actions-defect` 的“动作无响应/缺陷”结论**撤回**；第 142 批把原因写成“物理合成点击未命中/工具限制”也不准确，2026-10-19 已用真实鼠标证明点击可达，1.2s 只是观察时点，屏幕可见反馈更晚。
- 页面级配对仍 **48**；像素合格仍 **0**；参考端同文件未出现大文件警告，未形成参考警告动作配对。

## 4. 清理

- 实际 PID 11360/8628 已结束；临时 `huge.txt` 删除；`settings.json` 恢复 `B8868855…DC3D`（`hideLargeFileWarning=false`）；fixture 未变。
- 本批未修改代码；`cleanup-check-163.json`；未提交、未推送。
