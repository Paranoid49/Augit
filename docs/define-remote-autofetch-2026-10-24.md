# Define Remote 自动 Fetch 失败：保留对话框并回滚（2026-10-24）

> 结论：按参考 PyCharm `GitDefineRemoteDialog` 修正生命周期。无远端仓库正常入口中，
> 无效 URL 的自动 Fetch 失败后：Define Remote **保持打开**、URL 红框、对话框底部显示
> 完整 Git 错误；刚写入的远端被**回滚删除**，管理器和 Push 预览都不出现新远端、也不弹
> toast。与参考仍有“URL 旁气球 vs 对话框底部内联 notice”的位置差异。

## 1. 正常入口

- 临时 Git 仓库无 remote；真实 `start-augit.ps1 -SkipBuild`。
- 分支 chip → `Push…` → `Define remote` → `Add`；
- Name 清空为 `badremote`，URL `http://127.0.0.1:1/repo.git`，OK。

## 2. 修复

`web/src/live-data.js` 的 `saveRemoteDefine()`：

1. 写入远端（add/update）后，**先**在 Define 对话框仍打开时执行 `git/fetch`；
2. Fetch 失败：
   - add 回滚为 `git/remote-write {action:"delete", name}`；
   - update 回滚为原 name/fetchUrl/pushUrl；
   - `reloadRemotes()` 后重新渲染 Define，设置 `error` 与 `invalidField="fetchUrl"`；
   - 不关闭 Define、不写管理器 notice、不设置 `__augitRemoteSaved`；
3. Fetch 成功才关闭 Define、刷新管理器并显示 `Added … Fetched …`。

`web/src/mockup.js`：

- `remoteDefineBody()` 支持 `invalidField`，URL 加 `aria-invalid="true"` 和 `.invalid`；
- 重绘后 Define 层级保持在管理器之上。

`web/src/mockup.css`：

```css
.remote-define-dialog .text-field.invalid { border-color: var(--augit-red); }
.remote-define-dialog .remote-notice { white-space: normal; overflow-wrap: anywhere; }
```

- 同步 `docs/ux-mockups/`，`verify-ui-assets` PASS。

## 3. 结果

正常入口报告：`artifacts/define-remote-reference-fix-2026-10-24e/define-remote-autofetch.json`：

| 项 | 值 |
| --- | --- |
| Define 对话框 | **保持打开** |
| URL `aria-invalid` | **true / 红框** |
| Define notice | 完整 502 文本，两行换行 |
| 管理器 notice | null |
| 管理器新行 | 无 |
| toast | 无 |
| 主窗 Push 对话框 | 仍在底层 |
| `git remote` | **空**（回滚成功） |
| 实例 | PID 8296 正常关闭，`pidAlive=false` |
| 截图 | `.../define-remote-autofetch.png` |

## 4. 与参考对比

| 项 | 参考 | Augit |
| --- | --- | --- |
| Define 保持打开 | 是 | 是 |
| URL 红框 | 是 | 是 |
| 不写远端 | 是 | 是 |
| 管理器通知 | 无 | 无 |
| 错误位置 | URL 旁气球 | 对话框底部 notice（**差异**） |

参考截图：[Define Remote 参考反馈](define-remote-reference-2026-10-24.md)。

## 5. 自动化

- `live-goal-remote` 自动 Fetch 失败断言改为：
  - Define 仍在、URL `aria-invalid=true`；
  - 写序列最后两步为 `add` + `delete`；
  - 无 fetchfail 行、无管理器 notice；
  - notice 不裁切、无 toast。
- 完整 `live-shell` 2371/2371 通过（退出码 0）。
- `define-remote-autofetch.cjs` 正常入口报告 `remoteConfig=""`。

## 6. 边界

- 位置仍是内联 notice，不是参考的 URL 旁气球；不写成参考像素一致。
- 只验证新增远端；编辑远端回滚走同一代码路径但未单独正常入口复验。
- 错误来自本机 `127.0.0.1:1` 502，不覆盖认证、代理、慢盘。
- 页面级配对仍 50、像素仍 0。
