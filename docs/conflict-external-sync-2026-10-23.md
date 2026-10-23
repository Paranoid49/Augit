# 冲突外部处理与 Continue 同步（2026-10-23）

## 1. 夹具

隔离仓库：`D:/github/Augit/.tmp/conflict-refresh-2026-10-23/repo`。

- 基线 `conflict.txt` 三行提交；
- `other` 分支改第二行并提交；
- `master` 改第二行并提交；
- `git merge other` 产生 `UU conflict.txt`。

正常入口：`start-augit.ps1`，隔离设置 `settings-conflict-2026-10-23.json`，PID 23956。

## 2. 冲突状态

打开工作区后显示 `Merge Conflicts`：

- `1 conflicting file`、`conflict.txt`；
- `Abort Merge`、`Continue Merge` 可见；
- `Remaining: 1 unresolved conflict`；
- 截图：`artifacts/conflict-refresh-2026-10-23/conflict-before.png`。

## 3. 外部解决

在外部把 `conflict.txt` 改成解决后的三行并执行 `git add conflict.txt`：

- 应用在约 1 秒内把说明改为 `No unresolved conflicts`；
- `Continue Merge` 仍可用，`Abort Merge` 仍可用；
- 冲突文件不再是未解决项；
- 截图：`artifacts/conflict-refresh-2026-10-23/conflict-resolved.png`。

## 4. Continue Merge

通过 UIA 点击 `Continue Merge`：

- 生成合并提交 `3eeaecf Merge branch 'other'`；
- 工作区 `git status` 干净；
- `Merge Conflicts` 窗口关闭；
- 主窗 Changes 为 0 文件，提交历史显示合并提交；
- 截图：`artifacts/conflict-refresh-2026-10-23/conflict-continued.png`。

## 5. Rebase Skip / Merge 无 Skip

- 新建单文件 rebase 冲突夹具：`master` 与 `other` 改同一行，`git rebase other` 产生 `UU a.txt`；
- 正常入口 PID 31628 显示 `Rebase Conflicts`、`1 conflicting file`、`Abort Rebase`、`Skip`、`Continue Rebase`；
  - 截图：`artifacts/conflict-refresh-2026-10-23/rebase-before.png`。
- UIA 点击 `Skip`：rebase 完成，仓库回到 `master`，`git status` 干净，当前提交为 `9b30f37 other`；
  - 截图：`artifacts/conflict-refresh-2026-10-23/rebase-skipped.png`。
- 另造两文件 `git merge` 冲突：对话框只有 `Abort Merge`/`Continue Merge`，没有 `Skip`。Git merge 本身没有 skip 语义，因此这不是缺陷；Skip 只在 rebase 等可跳过步骤的操作里出现。

## 6. 外部 Abort

- 新建单文件 merge 冲突夹具，正常入口 PID 15364 显示 `Merge Conflicts`/`Abort Merge`；
- 外部执行 `git merge --abort`；
- 约 1 秒内 `Merge Conflicts` 窗口消失，Changes 恢复为 0 文件，`git status` 干净，历史回到 `master`；
- 截图：`artifacts/conflict-refresh-2026-10-23/external-abort.png`。

## 7. 二进制冲突

- 创建 256 字节二进制 `blob.bin`，`other` 与 `master` 分别改同一字节，`git merge` 产生 `UU blob.bin`；
- 正常入口 PID 28320 显示 `Merge Conflicts`/`1 conflicting file`/`blob.bin`；
- 双击后打开 `Commit: blob.bin` 只读 Diff，显示：
  - `Unresolved conflict` 横幅；
  - `This file has no text differences.`；
  - 没有二进制摘要/不可比较说明；
- 截图：`artifacts/conflict-refresh-2026-10-23/binary-open.png`。

这暴露一个尚未对齐的呈现问题：二进制冲突被当成“无文本差异”，而不是明确的二进制冲突。当前横幅仍提示外部处理，但没有说明“二进制”这一原因；由于参考实机的二进制冲突文案未采，本轮不擅自改文案，登记为待补证据。

## 8. 边界

- 单文件 merge/rebase 已验证；多文件 merge 只验证了按钮集合，没有执行 Abort；外部 Abort 已验证单文件 merge；
- 未测外部 Abort rebase、删除冲突文件、二进制冲突、多文件 Skip；
- 未重建三栏冲突编辑器，符合 2026-10-17 裁决；
- 外部解决是文本编辑 + `git add`，不经过应用内编辑；
- 这是行为证据，不是冲突页像素配对。
