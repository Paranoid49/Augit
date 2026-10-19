# Stash 管理取证尝试（2026-10-18）

## 1. 结论（第 117 批已更正：实际已到达 Stash Manager）

在临时 clone 构造 1 条 stash，**实际端已打开 Stash Manager 对话框**，但参考端未见对应 stash 管理页，未建立配对，页面级仍 44。

- 实际 PID 13592：主菜单 Git 弹出菜单底部 `Stashes...`（物理约 `150,1546`）→ `Stash` 对话框：工具栏 +/删除/刷新、`Stash Manager`、左列 `stash@{0} test stash manager 2`、右侧 `stash@{0} · test stash manager 2`、`main · 2026/10/19 09:29`、`Apply`/`Pop`/`View Content`/`Delete`、`Contains 1 files` + `notes.txt`、`Close`。证据 `5094EEE9…D5C`。
- 参考 PID 11132：同一 stash clone，Git 工具窗 Log 仅显示 main 提交；Git/Log/Console 标签中没有 Stash 列表/页；未找到参考 stash 管理入口。证据 `855469A3…2D7`。
- 仍无同态页可配对；实际 Stash Manager 是产品专有对话框。

- 临时 clone `.tmp/stash-manager-fixture`：修改 `notes.txt` 后 `git stash push -m 'test stash manager'`；`stash@{0}: On main: test stash manager`。
- 实际 PID 9012：主菜单 → Git → 尝试 `Stashes...`；点击落点未命中菜单项，打开了分支弹层（From main New Branch/Compare with Workspace/New Worktree/Push/Rename）。Git Log 中可看到 `On main: test stash manager` 与 `refs/stash`、`index on main…`。
- 独立 Stash Manager 页面未打开；参考端未启动。

## 1.2 参考 `Stash` 动作（第 118 批）

- 参考 PID 24356：Find Action `Stash` → **`Stash` 创建对话框**：`Git Root`、`Current Branch: main`、`Message`、`Keep index`、`Create Stash`/`Cancel`。证据 `FBE3A324…BB26`。
- 这是**创建 stash**，不是 Stash Manager 列表；它对应已配对的 `Stash` 页面（第 14 对），不是实际 `Stash Manager`（列表 + Apply/Pop/View Content/Delete）的同态页。
- 参考端未找到与 Stash Manager 对应的管理页；仍无配对。

## 2. 后续

- 改用键盘在主菜单中定位 `Stashes...`（或先量取该弹出菜单的实际矩形），避免按固定坐标误点。
- 参考端用 `Git > Stash` 工具窗/Stashes 节点采集；实际页面确认后再配对。
- 本页“Stash 管理”仍未完成；Git Log 里的 stash 行只是间接证据。

## 3. 清理

- 实际 PID 9012 已结束；临时 clone 删除；`settings.json` 恢复 `B8868855…DC3D`；无 Augit/pycharm64 残留；无 CDP 端口。
- 本批未修改代码；`cleanup-check-137.json`；未提交、未推送。
