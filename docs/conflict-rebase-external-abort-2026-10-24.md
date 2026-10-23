# Rebase 冲突的外部 abort 同步（2026-10-24）

> 结论：rebase 冲突窗口中外部执行 `git rebase --abort`，应用约 0.5 秒内关闭
> `Rebase Conflicts` 窗口，工作区恢复干净，分支回到 `feature`，主窗显示正常历史。
> 未在应用内点击 `Abort Rebase`；本批验证的是外部状态同步。

## 1. 夹具与入口

- 临时仓库：`main` 与 `feature` 修改同一行，`git rebase main` 在 `feature` 上产生
  `UU conflict.txt`；
- 真实外壳 `start-augit.ps1 -SkipBuild`，PID 24072；
- 冲突窗口显示：
  - `Rebase Conflicts`、`1 conflicting file`；
  - `Abort Rebase`、`Skip`；
  - **没有 Continue**（冲突未解决）。

## 2. 外部 abort

- 在外部执行 `git rebase --abort`（无输出、返回 0）；
- 等待 `.dialog.conflict-session-dialog` 消失（≤20s 超时内）；
- 结果：
  - `git status --porcelain` 为空；
  - `git branch --show-current` = **`feature`**；
  - `Rebase Conflicts` 窗口关闭；
  - 主窗历史仍可见 `main` / `feature-second` / `feature-conflict` / `base`；
- 实例 PID 24072 正常关闭。

## 3. 证据

- 截图：
  - `artifacts/conflict-rebase-external-abort-2026-10-24b/before-external-abort.png`
  - `.../after-external-abort.png`
- 报告：`.../report.json`（`external-abort-state`：status 空、branch feature）。
- 可复验脚本：`tools/audit/conflict-rebase-external-abort.cjs`（从 rebase-skip 脚本
  派生，外部 abort 代替应用内 Skip）。

## 4. 边界

- 只测单文件 rebase 冲突的外部 abort；未测多文件、删除冲突文件、外部 abort 后立即
  进行下一次 Git 操作。
- 未在应用内点击 `Abort Rebase`；该按钮路径仍由现有冲突脚本/既有证据覆盖。
- 这是行为同步证据，不是冲突窗口像素配对；页面级仍 50、像素仍 0。
- `git log --all` 仍包含 feature 原始冲突提交是 rebase abort 的正常结果，不能据此
  断言“abort 后历史不含冲突提交”。

## 5. 复验

```powershell
node tools/audit/conflict-rebase-external-abort.cjs <playwright> artifacts/conflict-rebase-external-abort-2026-10-24b
```
