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

## 5. 边界

- 只测一个冲突文件；单文件时没有出现 `Skip`，未验证多文件 Skip；
- 未重建三栏冲突编辑器，符合 2026-10-17 裁决；
- 外部解决是文本编辑 + `git add`，不经过应用内编辑；
- 未测外部 abort、删除文件、二进制冲突、多冲突文件。
