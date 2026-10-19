# Git Log 同态配对（2026-10-18）

## 1. 结论

完成 fixture 单提交的 Git Log 取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：提交 `test: create pixel fixture`、`main`、`PixelAudit 2026/10/13 20:06`、e9188bf、右侧 3 个文件与提交详情、branch/tag 与 text/hash 过滤，浅色。
- 实际：底部 `Git` 分组标题 + 单一 `Log` 工具标签，**Log 已激活**；内容为提交日志，右侧 `Commit Details` + File History/Blame。
- 参考：Git 工具窗口 `Git`/`Log`/`Console` 三个标签，`Log` 激活；右侧为提交信息、作者、文件列表。
- 已知差异：标签模型/Console、提交图/列、详情标题、工具窗高度与 Project 宽度。
- 页面级同态配对 **4 → 5**；像素合格仍 **0**。

## 2. 更正：Log 标签已激活，不是交互故障

- CDP 查询实际底部标签：`[{"t":"Log","cls":"tool-tab active"}]`，只有一个 Log 工具标签且已 active；`Git` 是左侧分组标题，不是第二个标签。
- 先前 UIA `Log` 按钮 `[142,1147,78,43]` 的 Invoke/鼠标点击未改变画面，是因为 **Log 本来已经激活**，点击无视觉变化属预期，不是渲染/交互缺陷。
- 实际进程 `Responding=True`、无 `window.__augitErrors`；该待排查项撤销。

## 3. 证据

- 实际：正常入口 PID 10324；`actual/actual-git-log-notes.png`（`ea3cefad…dfef`）。
- 参考：隔离 2026.2.1，PID 12632；`reference/reference-git-log.png`（未选提交，`61485fa2…58e6`）与 `reference/reference-git-log-selected.png`（选中提交，`cb5aeae7…f60d`）。
- 调试实例 PID 7664（CDP 9333）仅用于确认 Log 标签 active，不作为验收证据；实际验收图仍为 PID 10324、参考 PID 12632。
- 已知差异见 §1；底部工具窗高度、图/列和标签结构均不同，不称像素合格。

## 4. 边界

- 未测：提交右键菜单、筛选、分支选择、提交详情滚动、文件打开、Git 历史复杂图、空仓库、Reset/危险动作。
- Log 标签更正见 §2；不代表 Git 数据或提交详情错误。

## 5. 清理

- 实际 PID 10324、参考 PID 12632 正常关闭；调试 PID 7664 关闭；CDP 9333 关闭；实际设置恢复 `B8868855…DC3D`；参考未改主题/配置；未触碰用户原有 PyCharm；无端口/应用残留。
- `cleanup-check-62.json`、`cleanup-check-63.json`；未提交、未推送。
