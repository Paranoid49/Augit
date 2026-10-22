# Git 操作结果通知按参考 balloon 形态整改（2026-10-20）

> 2026-10-22 接续：失败路径已按参考改为“关闭 Push 对话框 + `Push failed` balloon + `Show details` 重开保留重试”，正常入口 HTTP 502 实测与规范修订见[Push 失败通知整改](push-failure-balloon-2026-10-22.md)。本文下文的“失败仍保留内联错误”是 2026-10-20 的当时状态，已被本次整改取代。

## 1. 参考事实

- 实机：PyCharm 2026.2.1 `Push failed` balloon（`artifacts/pixel-followup-2026-10-19/git-failure-closure/reference/reference-push-failed.png`）。
  - 白底圆角卡片，约 **375–391 × 100 CSS**（按 DPI 168 折算），圆角 8px，边框约 `#EAEBED`；
  - 左侧 16px 类型图标、粗体标题、换行正文、下方蓝色文字动作（`Show details in console`，测得 `rgb(46,92,208)`）。
- 源码：通知图标来自 `platform/icons/src/general/notificationError.svg`（`#DB5860` 实心圆+感叹号）、`notificationInfo.svg`（`#389FD6` 圆+i）、`notificationWarning.svg`（`#EDA200` 三角）。
  `VcsNotifier.notifySuccess` 使用 `NotificationType.INFORMATION`（`VcsNotifier.java`），因此**成功结果用信息图标**，不是自造的绿色对勾。

## 2. 实现

- `web/src/mockup.js`：新增 `toastMarkup()` / `toastNotificationIcon()`，统一生成
  「类型图标 + 粗体标题 + 正文 + 蓝色文字动作」；`liveToast()`、`operation-result`、`git-unavailable`、`operation-progress` 共用。
- `web/src/mockup.css`：卡片改白底 375px、8px 圆角、`--augit-border` 边框、轻阴影、图标 16px、正文 1.35 行高、动作 `.toast-action` 为蓝色链接（不再用 secondary 按钮）。
- `web/src/live-data.js`：成功通知沿用 `Push` / `Pushed to <upstream>.`，按 `notifySuccess→INFORMATION` 显示信息图标。
- **失败结果仍保留 Push 对话框内的内联原始错误**：本批尝试过在失败时额外弹 `Push failed` 通知，但实测模态期间 toast 区域不刷新（旧成功通知仍留在屏幕上，UIA 也读不到新通知）；参考是“关闭对话框 + balloon”，而关闭会移除 Augit 规格要求保留的重试入口。该差异登记为**未对齐的产品差异**，不在本批擅自关闭对话框或把通知压到模态之上。
- 未新增 Console、独立“结果页”或参考没有的 Git 能力。

## 3. 验证

- 新增 `tools/verify-ux-operation-result.cjs`：浅/深两套共 **24 项**通过（卡片宽度/圆角、错误图标 `rgb(219,88,96)`、标题、正文、动作链接 `rgb(53,116,240)` 且默认无下划线）。
- `mockup-scenes` 54/54（浅）、54/54（深）；frame-buttons 24/24、titlebar 12/12、document-button-states 60/60；`verify-ui-assets` PASS；`check-doc-claims` OK。
- 正常入口成功推送（PID 1560，隔离 clone + 本机 bare remote，真实菜单/鼠标点击 Push）：1 个提交推送到 `origin/master`，右下通知显示蓝色信息图标 + 粗体 `Push` + `Pushed to origin/master.`；截图 `artifacts/git-balloon-2026-10-20/push-success-toast2.png`。
- 正常入口失败推送（同一 clone，origin 改为 `http://127.0.0.1:1/repo.git`，真实点击 Push）：对话框内显示 `fatal: unable to access ... 502`，旧成功通知仍未被替换；确认失败 balloon 在模态期间不可见后回退了该通知改动（见 §2）。屏幕证据 `push-failed-screen.png`。

## 4. 边界

- 只测了**浅色**卡片的实测值；深色 balloon 没有参考截图，沿用主题面板色，未做像素验收。
- 参考截图是裁剪图，无法可靠测量卡片到窗口右/下的距离；实现保留右 18 / 下 33，不宣称位置像素通过。
- 成功 balloon 未从参考实机重新采集；成功图标依据 `VcsNotifier.notifySuccess → INFORMATION` 的源码事实，不是参考截图。
- **失败结果未对齐**：参考关闭对话框并显示 `Push failed` balloon + `Show details in console`；Augit 保留对话框内联错误以维持规格要求的重试入口，且模态期间 toast 不刷新，因此不做“关闭对话框换 balloon”的未授权行为变化。该差异保持未闭环，不用成功形态掩盖。
- 页面级配对仍 **50**、像素合格仍 **0**；本项不增加页面数。

## 5. 清理

- 正常入口实例 PID 1560（隔离 clone + 本机 bare remote + 隔离设置副本）：成功推送与失败推送各一次；模态未响应正常关窗，核实 PID 后 `taskkill /PID 1560 /T /F`；无残留 Augit/rg。
- 隔离设置副本未写回全局设置；失败/成功操作只在临时 clone 与本地 bare remote 内，未触碰用户仓库。
- 无头 Chromium 由脚本 `finally` 关闭；原始参考裁剪、正常入口截图与实测值在 `artifacts/git-balloon-2026-10-20/`（Git 忽略）。
