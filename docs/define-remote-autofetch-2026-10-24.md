# Define Remote 自动 Fetch 失败的内联反馈与换行修复（2026-10-24）

> 结论：正常入口（无远端仓库 → 分支 chip → Push… → Define remote → Add → 无效 URL → OK）
> 保存远端后自动 Fetch 失败，管理器保持打开，内联 notice 显示 `Added badremote.` 和完整
> Git 错误；主窗 toast 不出现。修复前该 notice 因 `.commit-meta { white-space: nowrap }`
> 被对话框 `overflow:hidden` 截断，只能看到 `http://127.0.0.1:1/rep…`。

## 1. 正常入口步骤

- 临时仓库无 remote；真实启动 `start-augit.ps1 -SkipBuild`。
- 真实鼠标：
  1. 分支 chip → `Push…`；
  2. Push 对话框中的 `Define remote`；
  3. Git Remotes 管理器 `Add`；
  4. Name 清空后输入 `badremote`，URL 输入 `http://127.0.0.1:1/repo.git`；
  5. OK。
- 等待 `.remote-window .remote-notice` 含 `Added badremote` 和 Git 错误；截图。

## 2. 修复前

`artifacts/define-remote-autofetch-2026-10-24b/define-remote-autofetch.png`：

- notice 文本实际是完整的：
  ```
  Added badremote. fatal: unable to access 'http://127.0.0.1:1/repo.git/':
  The requested URL returned error: 502 Run Fetch again later.
  ```
- 但视觉上只显示到 `http://127.0.0.1:1/rep…`，后面被裁掉；原因是 notice 同时带
  `.commit-meta` 的 `white-space: nowrap`，而 `.remote-dialog .dialog-body` 是
  `overflow: hidden`。
- 管理器仍打开、toast 不存在；行为正确，呈现不完整。

## 3. 修复

`web/src/mockup.css`：

```css
.remote-content .remote-notice {
  margin: 6px;
  white-space: normal;
  overflow-wrap: anywhere;
}
```

- 同步到 `docs/ux-mockups/mockup.css`，`verify-ui-assets` 通过。
- `live-goal-remote.cjs` 的自动 Fetch 失败断言增加：
  `whiteSpace=normal`、`overflowWrap=anywhere`、`scrollWidth <= clientWidth+1`（不裁切）。

## 4. 修复后

`artifacts/define-remote-autofetch-2026-10-24e/define-remote-autofetch.png`：

- 完整错误分三行显示在管理器内、OK 按钮上方；
- 实例 PID 24832 正常关闭（先关管理器、再关 Push、再关主窗），`pidAlive=false`；
- 管理器仍打开，Push 对话框仍在底层；
- `toast=false`，没有用被模态遮挡的主窗 toast 冒充反馈。

## 5. 证据

- 正常入口报告：`artifacts/define-remote-autofetch-2026-10-24e/define-remote-autofetch.json`（含 `manager/notice/toast/pushDialog`）。
- 修复前截图：`...-2026-10-24b/`；修复后：`...-2026-10-24d/`。
- `live-shell` 相关断言扩展后，完整套件 2371/2371 通过（退出码 0）。

## 6. 边界

- 只验证 Augit 侧内联反馈；PyCharm 参考侧已通过键盘+截图单独取得（见
  [Define Remote 参考反馈](define-remote-reference-2026-10-24.md)），参考会保留 Define
  Remote、在 URL 旁显示气球且不写入远端；Augit 当前的管理器内联 notice 是**差距**，
  不能写成参考一致。
- 错误使用本机 `127.0.0.1:1` 的 502，不覆盖真实网络、认证、代理和慢盘。
- notice 是文本换行修复，不是参考像素；页面级配对仍 50、像素仍 0。
- Define 对话框的 Name/URL/Push URL 布局、校验、删除和重命名不属于本批新证据。
