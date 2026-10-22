# Fetch 成功/失败结果通知（2026-10-22）

参考源码 `GitFetchSupportImpl.FetchResultImpl.doShowNotification()` 对 fetch 结果统一显示 `VcsNotifier` 通知：成功标题 `Fetch successful`、正文 `Fetched successfully`；失败标题 `Fetch failed`、正文为 Git 原始错误。此前 Augit 成功只关弹层、失败把错误留在分支弹层内，未对齐。

## 1. 实现

- `web/src/live-data.js` 的 `runPopoverAction('fetch')`：
  - 成功：关闭弹层，显示 `Fetch successful` / `Fetched successfully` 通知，再重读状态与引用。
  - 失败（宿主返回 `fetched:false` 或调用异常）：关闭弹层，显示 `Fetch failed` / Git 原始错误；不再设置 `__augitCheckoutError` 或重新打开分支弹层。
- 成功/失败文案取自 `GitBundle.properties`：
  - `notification.title.fetch.success=Fetch successful`
  - `notification.content.fetched.successfully=Fetched successfully`
  - `notification.title.fetch.failure=Fetch failed`
- 产品/UX 规范同步：`product-spec.md` §5.2、`ux-spec.md` §7.10。
- `tools/audit/live-shell.spec.cjs` 的 fetch 场景改为断言成功通知与失败关闭弹层 + 通知正文。

## 2. 正常入口证据

| 项目 | 值 |
| --- | --- |
| Augit | PID 7376，正常关闭（点击关闭按钮→进程消失 140ms） |
| ProductVersion | `0.1.0+4b193af…` |
| `live-data.js` SHA256 | `DCEC9DB0…6455332` |
| 隔离设置 | `D:/github/Augit/.tmp/settings-fetch-evidence-2026-10-22.json` |
| 工作区 | `D:/github/Augit/.tmp/fetch-evidence-2026-10-22/repo` |
| 环境 | Light、DPI168（1.75）、最大化、单屏 |

### 2.1 失败

- remote：`http://127.0.0.1:1/repo.git`
- 真实操作：UIA 调用标题栏分支 chip `main` → 分支弹层的 `Update Project…` 超链接（即 fetch）。
- 结果：弹层关闭；UIA 文本 `Fetch failed`（2268,1546,124,27）与正文 `fatal: unable to access 'http://127.0.0.1:1/repo.git/': The requested URL returned error: 502`（2268,1580,512,55）；页面无内联错误弹层。
- 截图：`artifacts/fetch-evidence-2026-10-22/fetch-failed.png`（sha256 `41A292F6…63402`）。

### 2.2 成功

- 同一仓库改为本地 bare remote `D:/github/Augit/.tmp/fetch-evidence-2026-10-22/remote.git`，已 push `main`。
- 同一正常入口再次执行 fetch。
- 结果：弹层关闭；UIA 文本 `Fetch successful`（2268,1574,171,27）与正文 `Fetched successfully`（2268,1608,204,27）；引用树出现 `Remote/origin`，分支行显示 `main ⇄ origin/main`。
- 截图：`artifacts/fetch-evidence-2026-10-22/fetch-success.png`（sha256 `55F0C5F4…20CD5`）。

### 2.3 Update Selected

- 同一临时仓库增加受跟踪的非当前分支 `feature`（upstream `origin/feature`），checkout 回 `main`。
- 在引用树选择 `feature`，点击左侧竖条 `Update Selected`：
  - 成功：UIA 显示 `Fetch successful`（2268,1574,171,27）与 `Fetched 1 target`（2268,1608,166,27）；截图 `artifacts/fetch-evidence-2026-10-22/update-selected-success.png`。
  - 失败（origin 改为 `http://127.0.0.1:1/repo.git` 后再次执行）：UIA 显示 `Update failed`（2268,1546,144,27）与 Git 原始 502 错误；截图 `artifacts/fetch-evidence-2026-10-22/update-selected-failed.png`。
- 依据：`GitBranchActionsUtil.updateBranches()` 对 fetch 目标调用 `showNotificationIfFailed("Update failed")`，成功后发送 `branches.fetch.finished`。

### 2.4 Define Remote 自动 Fetch

- 参考 `GitConfigureRemotesDialog.kt:286-292` 的 `doFetch()` 只调用 `showNotificationIfFailed()`：保存远端后的自动 Fetch 失败显示 `Fetch failed` 通知，成功不额外发通知。
- Augit 对应：保存成功并勾选 Fetch 时，成功仍只更新管理器内联 `Added/Saved … Fetched …`；失败保留 `Added/Saved …` 并显示 `Fetch failed` + Git 原始错误，不把错误写进内联 notice。
- `live-goal-remote` 新增场景：新增 `fetchfail` 远端、`__fetchFails=true`，断言只写一次 `add`、自动 Fetch 一次、行出现、notice 只有 `Added fetchfail`、toast 为 `Fetch failed` + `No remote configured`。本模块 **42/42** 通过。

## 3. 验证

- 全量 `live-shell` **2342/2342 通过**（退出码 0、未执行 0）；fetch 场景断言成功通知标题/正文、失败关闭弹层与原始错误；`live-goal-remote` 断言保存远端后的自动 Fetch 失败只显示 `Fetch failed` 通知、不写入内联错误。
- 未改 `mockup.js`/`mockup.css`，`verify-ui-assets` 仍为双侧一致。

## 4. 边界

- 只覆盖浅色、单屏、单仓库、一个失败 502 与一个本地成功 fetch；未覆盖多远端、认证失败、取消、通知位置像素与深色。
- 失败正文使用 Git 原始错误；未实现参考的 pruned refs / 多仓库汇总正文。
- 页面级同态配对仍 **50**、整窗像素合格仍 **0**。

## 5. 清理

- Augit PID 7376 正常关闭；临时 bare remote 与仓库仅位于 `.tmp/fetch-evidence-2026-10-22/`。
- 全局 `%LOCALAPPDATA%\Augit\settings.json` SHA256 仍为 `8840E775…462CF`。
