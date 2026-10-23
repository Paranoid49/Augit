# Push non-fast-forward 失败与重试成功（2026-10-24）

> 结论：本地 bare remote、两个 clone 制造真实分叉；Augit 正常入口推送被远端拒绝后，对话框关闭、
> 右下 `Push failed` balloon 显示 Git 原始原因；外部 `pull --rebase` 解决分叉后，用 balloon 的
> `Show details` 重开并重试成功，远端出现两条提交。未覆盖认证失败和通知像素。

## 1. 夹具

- 临时 bare remote + `seed` / `workspace` / `other` 三个仓库，均为本地路径，无网络。
- `other` 先提交 `test: other advances main` 并推送；`workspace` 基于旧 `main` 提交
  `test: local diverges main`，形成分叉。
- Augit 隔离设置只替换 `lastWorkspace`；工作区、remote、settings 均在临时目录，结束删除。
- 当前构建 `-SkipBuild`；Light、DPI168、最大化；CDP 仅观察 DOM/坐标，UI 点击全部为 Win32。

## 2. 正常入口序列

| 步骤 | 操作 | 结果 |
| --- | --- | --- |
| 1 | 标题栏分支 chip `main` | 分支弹层打开 |
| 2 | 弹层 `Push…` | Push 对话框；1 条待推提交 `test: local diverges main`；`main → origin/main` |
| 3 | 对话框 `Push` | 被远端拒绝；对话框关闭 |
| 4 | 失败 balloon | 标题 `Push failed`，动作 `Show details`；正文为 Git 原始错误 |
| 5 | 外部 `git pull --rebase origin main` | 本地变基到 `other` 的新提交上；本地日志三条 |
| 6 | balloon `Show details` | 重开 Push 对话框；`push-notice` 保留原失败；提交列表仍在；`Push` 可用 |
| 7 | 再次 `Push` | 成功；balloon `Push` / `Pushed to origin/main.`；无 action |

失败正文关键行：

```text
! [rejected]        main -> main (fetch first)
error: failed to push some refs to '...\remote.git'
hint: Updates were rejected because the remote contains work that you do not
hint: have locally. ...
```

这是 Git 对非快进/远端领先的拒绝；本例远端已有另一条提交，所以状态是 `(fetch first)`，
不能写成字面 `(non-fast-forward)`。

变基后：

- 本地：`test: local diverges main` / `test: other advances main` / `test: initial`；
- 重试后远端：`test: local diverges main` / `test: other advances main` / `test: initial`；
- 工作区最终无未推送提交，分支 chip 仍为 `main`。

## 3. 证据

- 失败：`artifacts/push-non-fast-forward-2026-10-24c/push-failure.png`
  （对话框已关闭，右下 `Push failed` + 原始错误 + `Show details`）。
- 重试：`artifacts/push-non-fast-forward-2026-10-24c/push-retry.png`
  （右下 `Push` / `Pushed to origin/main.`，日志三行含 rebase 结果）。
- 报告：`push-non-fast-forward.json`，含点击 UTC、失败正文、重开 notice、远端最终日志。
- 实例 PID 27528 正常关闭，`pidAlive=false`；临时仓库和 bare remote 已删除；无残留 Augit。

## 4. 边界

- 本地 bare remote 是受控夹具，不是自然网络/认证服务；未覆盖认证失败、远端钩子拒绝、
  受控慢网络。
- 只测浅色、当前 DPI、单远端、单分支；没有参考 PyCharm 实机的失败/重试像素配对，
  不把本组截图写成通知像素通过。
- 失败后需要用户先在外部解决分叉；产品不自动 pull/rebase，这是本次验证的既定操作顺序。
- `Show details` 重开保留原失败 notice；重试成功后旧 notice 随对话框关闭，不残留在页面。
- 未测 balloon 被其他窗口遮挡、深色、窄窗及通知位置像素。
