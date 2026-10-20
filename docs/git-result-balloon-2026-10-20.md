# Git 操作结果通知按参考 balloon 形态整改（2026-10-20）

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
- `web/src/live-data.js`：Push 失败新增 `Push failed` 错误通知（正文取原始错误首行），完整原因仍保留在 Push 对话框内；成功通知沿用 `Push` / `Pushed to <upstream>.`，按 `notifySuccess→INFORMATION` 显示信息图标。
- 未新增 Console、独立“结果页”或参考没有的 Git 能力。

## 3. 验证

- 新增 `tools/verify-ux-operation-result.cjs`：浅/深两套共 **24 项**通过（卡片宽度/圆角、错误图标 `rgb(219,88,96)`、标题、正文、动作链接 `rgb(53,116,240)` 且默认无下划线）。
- `mockup-scenes` 54/54（浅）、54/54（深）；frame-buttons 24/24、titlebar 12/12、document-button-states 60/60；`verify-ui-assets` PASS；`check-doc-claims` OK。
- 静态截图：`artifacts/git-balloon-2026-10-20/actual-operation-result.png`（与参考裁剪并排比较，卡片形态一致）。

## 4. 边界

- 只测了**浅色**卡片的实测值；深色 balloon 没有参考截图，沿用主题面板色，未做像素验收。
- 参考截图是裁剪图，无法可靠测量卡片到窗口右/下的距离；实现保留右 18 / 下 33，不宣称位置像素通过。
- 成功 balloon 未从参考实机重新采集；成功图标依据 `VcsNotifier.notifySuccess → INFORMATION` 的源码事实，不是参考截图。
- 失败时同时保留 Push 对话框内的原始错误与通知摘要，这是 Augit 不新增 Console 的适配；参考会关闭对话框并给 `Show details in console`。
- 页面级配对仍 **50**、像素合格仍 **0**；本项不增加页面数。

## 5. 清理

- 本批只启动无头 Chromium（`verify-ux-operation-result`、状态矩阵、场景渲染），未启动 Augit 或 PyCharm 实例；浏览器由脚本 `finally` 关闭。
- 原始参考裁剪与实测值在 `artifacts/git-balloon-2026-10-20/`（Git 忽略）。
