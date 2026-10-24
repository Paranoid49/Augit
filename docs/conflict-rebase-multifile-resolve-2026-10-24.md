# 多文件 rebase 冲突：外部混合解决后同步（2026-10-24）

> 结论：两个文件同时冲突时，冲突窗列出 `a.txt` / `b.txt`；外部对 `a.txt` 删除、
> 对 `b.txt` 编辑后 `git add`，再 `git rebase --continue`，应用关闭冲突窗、工作区
> 干净、feature 历史可用。本批验证多文件与“删除+编辑”混合解决，并观察外部先解决
> 一个文件后的中间列表刷新，不是 Skip。

## 1. 夹具

- base 提交含 `a.txt` / `b.txt`；
- feature 同时改两文件并追加 `feature-second`；
- main 同时改两文件；
- `git rebase main` 在 feature 上产生 `UU a.txt` 与 `UU b.txt`；
- 真实外壳 `start-augit.ps1 -SkipBuild`，PID 484；
- 冲突窗口文案同时含 `a.txt`、`b.txt`，按钮 `Abort Rebase` / `Skip`，未解决时无 Continue。

## 2. 外部解决

1. `git rm a.txt`（删除一个冲突文件）；
2. 等待应用刷新：冲突窗仍打开，显示 `1 conflicting file`、只剩 `b.txt`、
   `Current step 1/2`、`Remaining: 1 unresolved conflict`；应用内报告的操作按钮仍只有
   `Abort Rebase` / `Skip`（文本里出现 `Continue Rebase`，但没有对应的
   `data-operation-action`，不把它当成已启用）。
3. `b.txt` 写为 `b-resolved`，`git add b.txt`；
4. `git -c core.editor=true rebase --continue`；
4. Git 输出：
   ```
   [detached HEAD 52de056] test: feature-conflict
    2 files changed, 1 insertion(+), 2 deletions(-)
    delete mode 100644 a.txt
   Successfully rebased and updated refs/heads/feature.
   ```
5. 应用侧：
   - 冲突窗关闭；
   - `git status --porcelain` 为空；
   - 分支 `feature`；
   - HEAD `eaf3728 test: feature-second`；
   - 项目树只剩 `b.txt` / `other.txt`，`a.txt` 已删除；
   - Git Log 显示 feature-second / feature-conflict / main / base。

## 3. 证据

- 截图：`artifacts/conflict-rebase-gradual-resolve-2026-10-24/after-external-resolve.png`
- 报告：`.../report.json`（`multifile-conflict-ui`、`mid-external-resolve`、
  `external-remove`、`external-edit-add`、`external-continue`）
- 脚本：`tools/audit/conflict-rebase-multifile-resolve.cjs`
- 实例 PID 7112 正常关闭，`pidAlive=false`（本批逐步刷新）。

## 4. 边界

- 两文件、一个删除 + 一个编辑；未测两文件都编辑、都删除、二进制冲突。
- 未在应用内点击 Continue / Skip；应用内多文件 Skip 仍未单独覆盖。
- 已观察外部先解决一个文件后的中间列表刷新（1 个文件、b.txt、1/2）；
  未在中间状态点击应用内 Continue/Skip，也未验证“先外部解决 b、后 a”的反向顺序。
- 行为/状态证据，不是冲突窗口像素配对；页面级仍 50、像素仍 0。

## 5. 复验

```powershell
node tools/audit/conflict-rebase-multifile-resolve.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/conflict-rebase-gradual-resolve-2026-10-24
```
