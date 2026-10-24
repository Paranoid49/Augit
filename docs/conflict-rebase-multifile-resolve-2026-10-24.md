# 多文件 rebase 冲突：外部混合解决后同步（2026-10-24）

> 结论：两个文件同时冲突时，冲突窗列出 `a.txt` / `b.txt`；外部对 `a.txt` 删除、
> 对 `b.txt` 编辑后 `git add`，再 `git rebase --continue`，应用关闭冲突窗、工作区
> 干净、feature 历史可用。本批验证多文件与“删除+编辑”混合解决，不是 Skip。

## 1. 夹具

- base 提交含 `a.txt` / `b.txt`；
- feature 同时改两文件并追加 `feature-second`；
- main 同时改两文件；
- `git rebase main` 在 feature 上产生 `UU a.txt` 与 `UU b.txt`；
- 真实外壳 `start-augit.ps1 -SkipBuild`，PID 484；
- 冲突窗口文案同时含 `a.txt`、`b.txt`，按钮 `Abort Rebase` / `Skip`，未解决时无 Continue。

## 2. 外部解决

1. `git rm a.txt`（删除一个冲突文件）；
2. `b.txt` 写为 `b-resolved`，`git add b.txt`；
3. `git -c core.editor=true rebase --continue`；
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

- 截图：`artifacts/conflict-rebase-multifile-resolve-2026-10-24/after-external-resolve.png`
- 报告：`.../report.json`（`multifile-conflict-ui`、`external-remove`、`external-edit-add`、`external-continue`）
- 脚本：`tools/audit/conflict-rebase-multifile-resolve.cjs`
- 实例 PID 484 正常关闭，`pidAlive=false`。

## 4. 边界

- 两文件、一个删除 + 一个编辑；未测两文件都编辑、都删除、二进制冲突。
- 未在应用内点击 Continue / Skip；应用内多文件 Skip 仍未单独覆盖。
- 未验证应用在多文件冲突过程中逐步外部解决一个文件时的中间列表刷新；本批在两次
  Git 操作后一次性继续。
- 行为/状态证据，不是冲突窗口像素配对；页面级仍 50、像素仍 0。

## 5. 复验

```powershell
node tools/audit/conflict-rebase-multifile-resolve.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/conflict-rebase-multifile-resolve-2026-10-24
```
