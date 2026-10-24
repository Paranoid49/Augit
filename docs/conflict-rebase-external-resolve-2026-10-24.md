# 外部删除冲突文件后 rebase continue 同步（2026-10-24）

> 结论：rebase 冲突窗口打开时，外部 `git rm conflict.txt` + `git rebase --continue`
> 能完成 rebase；应用约 0.5s 内关闭冲突窗，工作区干净，分支回到可用的 feature，
> 项目树/历史刷新。未使用应用内 Continue，也未恢复三栏冲突编辑器。

## 1. 夹具

- 临时仓库 `main` 与 `feature` 修改同一行，`git rebase main` 产生 `UU conflict.txt`；
- 真实外壳 `start-augit.ps1 -SkipBuild`，PID 20584；
- 冲突窗口显示 `Abort Rebase`、`Skip`，未解决时**没有 Continue**。

## 2. 外部解决

1. `git rm conflict.txt`（删除冲突文件，标记已解决）；
2. `git -c core.editor=true rebase --continue`；
3. Git 输出：
   ```
   [detached HEAD 39d3c9b] test: feature-conflict
    1 file changed, 3 deletions(-)
    delete mode 100644 conflict.txt
   Successfully rebased and updated refs/heads/feature.
   ```
4. 应用侧：
   - `.dialog.conflict-session-dialog` 消失；
   - `git status --porcelain` 为空；
   - `git branch --show-current` = `feature`；
   - `git log --oneline --all` 含 `feature-second` / `feature-conflict` / `main` / `base`；
   - HEAD = `ac795c4 test: feature-second`（`other.txt` 1 file changed）；
   - 项目树只剩 `other.txt`，Git Log 显示 feature 历史。

## 3. 证据

- 截图：`artifacts/conflict-rebase-external-resolve-2026-10-24/after-external-resolve.png`
- 报告：`.../report.json`（`external-remove`、`external-continue`、`external-resolve-state`）
- 脚本：`tools/audit/conflict-rebase-external-resolve.cjs`
- 实例 PID 20584 正常关闭，`pidAlive=false`。

## 4. 边界

- 只测单文件删除解决；未测外部编辑后 `git add`、多文件冲突、`git checkout --ours/--theirs`。
- 未在应用内点击 Continue；该按钮路径仍由现有冲突证据覆盖。
- 行为/状态证据，不是冲突窗口像素配对；页面级仍 50、像素仍 0。
- `git log --all` 含 feature 原始冲突提交是 rebase 后历史的一部分，不能据此断言被跳过。

## 5. 复验

```powershell
node tools/audit/conflict-rebase-external-resolve.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/conflict-rebase-external-resolve-2026-10-24
```
