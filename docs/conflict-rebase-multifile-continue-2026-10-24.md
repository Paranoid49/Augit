# 多文件 rebase 冲突：全部解决后应用内 Continue（2026-10-24）

> 结论：两个文件冲突时，外部先解决 `a.txt`（删除）与 `b.txt`（编辑 + add），应用把
> 冲突窗刷新为 `No unresolved conflicts`，`Continue Rebase` 变为可用；点击后 rebase
> 完成、窗口关闭、工作区干净、分支恢复正常。未使用外部 `git rebase --continue`。

## 1. 夹具

- `a.txt` / `b.txt` 同时冲突；
- 真实外壳 `start-augit.ps1 -SkipBuild`，PID 27496；
- 初始冲突窗 `1 conflicting file`，只有 `Abort Rebase` / `Skip`。

## 2. 外部解决

```powershell
git rm a.txt
# b.txt 写为 b-resolved
git add b.txt
```

应用约 0.5s 后刷新：

```
Rebase Conflicts
No unresolved conflicts
Current step 1/2
Abort Rebase   Skip   Continue Rebase
```

- `continue` 按钮的 `disabled=false`；
- 项目树只剩 `b.txt`；Git Log 显示 feature 未完成状态。

## 3. 应用内 Continue

点击 `Continue Rebase`（本批用 Playwright 对按钮的点击驱动页面，不是 OS 鼠标；
但入口是真实外壳、真实 Git 状态，不是场景注入）：

- 冲突窗关闭；
- `git status --porcelain` 为空；
- 分支 `feature`；
- HEAD `075b46e test: feature-second`；
- Git 历史：feature-second / feature-conflict / main / base；
- 项目树 `b.txt` / `other.txt`，`a.txt` 已删除；
- 实例 PID 27496 正常关闭，`pidAlive=false`。

## 4. 证据

- 截图：`artifacts/conflict-rebase-multifile-continue-2026-10-24b/ready-continue.png`
- 报告：`.../report.json`（`mid-external-resolve`、`ready-to-continue`、
  `external-edit-add`、`external-resolve-state`）
- 脚本：`tools/audit/conflict-rebase-multifile-continue.cjs`

## 5. 边界

- Continue 输入用页面 locator 点击，不是真实 OS 鼠标；因此不能替代前台鼠标点击验收。
- 未在中间状态点击 Skip/Abort；未测反向解决顺序、两文件都编辑/都删除、二进制冲突。
- 未采集参考 PyCharm 同状态像素；页面级仍 50、像素仍 0。

## 6. 复验

```powershell
node tools/audit/conflict-rebase-multifile-continue.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/conflict-rebase-multifile-continue-2026-10-24b
```
