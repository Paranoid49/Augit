# Push 失败通知按参考形态整改（2026-10-22）

本文落实 2026-10-22 用户裁决：Push 失败呈现以 PyCharm 2026.2.1 实机与 `D:/github/intellij-community` 为准；同时保留 Augit 既有的重试能力。只覆盖失败反馈形态，不是 Push 页面整窗像素验收。

## 1. 裁决与兼容方案

参考行为：失败时 Read Push 对话框关闭，右下显示 `Push failed` balloon——错误图标、粗体标题、原始脱敏错误正文、蓝色 `Show details in console` 文字动作。Augit 不提供 Git Console（产品明确排除），因此：

- 失败后关闭对话框并显示同形态 balloon，动作为 `Show details`；
- 点击 `Show details` 清除 balloon，重新打开 Push 对话框；对话框继续显示同一错误、待推送提交列表与可用的 `Push`，即保留重试与详情入口；
- 不新增独立结果页、Git Console 或 Force Push。

同步修订：

- `product-spec.md` §5.1：失败时显示 `Push failed` 通知，正文为脱敏输出，`Show details` 可重新打开操作界面查看并重试；通知关闭后不保留。
- `ux-spec.md` §7.12：失败关闭对话框并显示参考形态通知；`Show details` 重开保留提交列表与焦点、允许重试；取消仍保留对话框。
- `acceptance-scope-rulings-2026-10-20.md` §8.2 记录为已实施。

## 2. 实现

| 位置 | 改动 |
| --- | --- |
| `web/src/live-data.js` `confirmPushDialog()` | 失败分支先保存 `window.__augitPushError` 与 `{pushed:false, reason}`，调用 `closePushDialog()`，再 `showToast({ title:"Push failed", text:failure, kind:"error", action:{ id:"push-details", label:"Show details" } })`。 |
| `web/src/live-data.js` 通知动作 | 新增 `[data-toast-action]` 委托：核对当前 `live.toast.action.id`，避免旧卡片执行过期动作；`push-details` 先 `clearToast()`，再 `openPushDialog({ failure })`。 |
| `web/src/live-data.js` `openPushDialog()` | 接受 `{ failure }`；预览读取并渲染完成后，把同一错误写回对话框 `.push-notice`，保留列表、焦点和可用 `Push`。 |
| `web/src/mockup.js` `toastMarkup()` | 有 `href` 的动作仍渲染 `<a class="toast-action">`；无 `href` 的应用内动作渲染 `<button type="button" class="toast-action" data-toast-action="…">`，不再默认指向 `settings.html`。 |
| `web/src/mockup.css` | 给 `button.toast-action` 去掉原生边框/底色、继承字体并保留蓝色链接外观。 |

失败期间旧成功通知会被新状态覆盖；模态期间不把通知压到对话框之上，而是先关闭对话框再显示，避免之前观察到的“模态期间通知不刷新、旧通知残留”。

## 3. 正常入口证据

| 项目 | 值 |
| --- | --- |
| 夹具 | `.tmp/push-failure-2026-10-22/fixture`，`master...origin/master [ahead 1]`，本地提交 `6d45ab8 test: pending push` |
| 远端 | `http://127.0.0.1:1/repo.git`，`git push` 返回同一 502 |
| 启动 | `powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1 -Workspace D:/github/Augit/.tmp/push-failure-2026-10-22/fixture -SettingsFile D:/github/Augit/.tmp/settings-push-failure-2026-10-22.json` |
| 实例 | PID 8360，正常关闭 |
| ProductVersion | `0.1.0+290539374eada321dacf80a022c1996f660be323` |
| `Augit.exe` / `Augit.dll` SHA256 | `B86F4FAE…0A82D4` / `1B2F5D18…A63C974` |
| `mockup.js` / `mockup.css` | `F877B503…7B0B37` / `1F4C2240…3F072C` |
| `live-data.js` / `bridge.js` | `5DD9124A…20F12F` / `6EFF9F71…3BF52` |
| 主题/字体 | Light / Microsoft YaHei UI 12 / JetBrains Mono 13 |
| 窗口 | 最大化，外框 2904×1740 @ (-12,-12)，客户区 2880×1716，DPI 168 |

操作与观察（真实鼠标/键盘，无 CDP）：

1. 点击标题栏分支 chip → `Push…`，打开 Push 对话框，显示 `master → origin/master`、`1 commits`、`test: pending push`，`Push` 可用。
2. 点击 `Push`；约 2.5 s 后对话框已关闭，右下出现 `Push failed` 卡片：红色错误图标、粗体标题、正文 `fatal: unable to access 'http://127.0.0.1:1/repo.git/': The requested URL returned error: 502`、蓝色 `Show details`。截图 `artifacts/push-failure-2026-10-22/failure-balloon.png`。
3. 点击 `Show details`（UIA 实测按钮矩形 `2268,1610,128,27`，中心 `2332,1623` 物理）；Push 对话框重新打开，显示同一错误、`1 commits` 列表和可用的 `Push`，无 balloon 残留。截图 `retry-dialog2.png`。
4. 点击 `Cancel` 关闭；`git status -sb` 仍为 `## master...origin/master [ahead 1]`，远端引用未变；随后正常关闭主窗。

本批验证的是“失败关闭 + balloon + 重开保留重试”的完整链路；没有把失败包装成成功，也没有新增 Console 或结果页。

## 4. 自动化与边界

- `node tools/test-push-result-toast.mjs` **12/12 PASS**（成功文案、失败关闭、`Show details` 动作、按钮渲染、action.id 核对）。
- 全量 `live-shell.spec.cjs` **通过 2340 项断言**（退出码 0，未执行 0 项）；套件中的失败场景已改为断言“对话框关闭 + balloon + Show details 重开 + 列表/重试保留”。
- `verify-ui-assets.ps1` PASS；`web/src/live-data.js`、`mockup.js` 语法检查通过。
- 仅覆盖浅色、DPI 168、单提交、HTTP 502；未覆盖认证失败、超时、non-fast-forward、真实网络、深色、窄窗和重试成功。
- 参考 balloon 的截图是既有裁剪图，未重新测量卡片到窗口右/下的距离；本批不宣称位置/尺寸像素通过。
- 页面级同态配对仍 **50**、整窗像素合格仍 **0**；本项不增加页面数。

## 5. 清理

- PID 8360 正常关闭；关闭后无本实例 `msedgewebview2` 残留。
- 全局 `%LOCALAPPDATA%\Augit\settings.json` SHA256 仍为 `8840E775…462CF`；隔离设置只写 `.tmp/` 副本。
- 夹具与 bare remote 位于 `.tmp/push-failure-2026-10-22/`，取证后已删除；原始截图在 `artifacts/push-failure-2026-10-22/`（Git 忽略）。
