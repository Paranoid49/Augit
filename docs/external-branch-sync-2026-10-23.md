# 外部 checkout 后的引用树同步（2026-10-23）

## 1. 问题

正常入口打开隔离仓库、当前分支 `main`，在外部执行 `git checkout feature`：

- 标题栏分支 chip 约 **318ms** 后从 `main` 变为 `feature`；
- 但引用树仍把 `main` 标为当前分支，`feature` 没有当前标记；
- 截图（修复前）：`artifacts/external-branch-2026-10-23/after-feature.png`；
- 等待 1 秒后截图哈希仍相同，不是刷新尚未完成。

这与“外部分支变化必须同步引用树”的刷新验收目标冲突。

## 2. 根因与修复

`applyWorkspaceChanges()` 的 `changes.gitMetadata` 分支只重读 `git/status` 与 `git/history`，没有重读 `git/references`；标题栏分支来自 status，引用树来自 references，因此两者分叉。

修复：`gitMetadata` 刷新时并行调用 `loadReferences()`，然后由既有 `refresh("side", …)` 更新引用树。

对应断言：`live-shell` 新增“Git 元数据变化后重读引用并更新当前分支标记”，把 `feature/ux` 设为当前分支后，检查 `feature/ux` 的 `data-ref-current=true` 且 `dsh` 为 `false`。

## 3. 修复后正常入口

- 重启正常入口 PID 16852，当前分支 `main`；
- 外部 `git checkout feature`，等待 1 秒；
- 截图：`artifacts/external-branch-2026-10-23/fixed-feature-tree.png`；
- 标题栏显示 `feature`；
- 引用树 Local 中 `feature` 带当前分支标记并排到前面，`main` 不再标当前；
- 仓库已恢复 `main`。

## 4. 边界

- 这是行为同步证据，不是引用树像素配对；
- 只测一次外部 checkout，未测 detached HEAD、删除当前分支、冲突检出等状态；
- 引用树更新未做逐帧计时；标题栏 318ms 是 UIA 轮询近似值。
