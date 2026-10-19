# 主菜单入口取证（2026-10-18）

## 1. 结论

本轮尝试主菜单/Stash 入口，**未形成合格同态配对**，页面级计数仍 12。

- 实际：点击 UIA `Main Menu` 按钮（矩形 `63,12,53,53`，中心物理 `89,38`）后，标题栏展开**内联菜单条** `File / View / Git / Terminal / Settings`。
- 参考：点击汉堡按钮（物理约 `60,35`）后本次未打开菜单；截图停留在 `app.py` 编辑器，无参考菜单内容。
- 实际内联菜单条与参考的汉堡菜单结构不同；未取得可比参考，不作产品结论。
- 后续误点分支 chip（`374,36`）打开 `Branches and Tags` 工具提示，并打开底部 Terminal（`Local`/PowerShell）；终端页面已有既有配对，本批不重复计数。
- 第 55 批补充：实际点击 `Git`（物理 264,36）后出现**底部左侧 Git 菜单**，可见 `Create Git Repository… / Fetch / Push… / Branches and Tags… / Stashes… / Create Stash… / Worktrees… / Reset Current Branch…`；点击 `Create Stash…`（约 150,1066）后未打开对话框，焦点落回 Git Log 的 `Branch or tag` 筛选，菜单关闭。未执行任何 Git 写操作；Create Stash 入口是否接线**未定位**，不据此判产品缺陷。
- 参考汉堡菜单仍未打开；Stash 对话框未取得配对。
- 第 58 批补充：附 CDP 实例通过 `aria-label="Main Menu"` 打开菜单条、DOM 点击 `Git` 后，`[data-main-menu-action]` 查询为 **0**，未取得 Reset Current Branch 项；未打开 Reset 对话框，未执行 Reset，参考端未启动。该序列的菜单 DOM 与真实鼠标路径不同，入口未成立，不作产品结论；`cleanup-check-85.json`。

## 2. 证据

- 实际内联菜单：`artifacts/pixel-followup-2026-10-18/stash-pair/actual/actual-main-menu2.png`（`6961FE4C…CBE8`）。
- 参考点击后：`reference/reference-main-menu.png`（`F9C07FA0…3969`，无菜单）。
- 误开终端：`actual/actual-git-menu.png`（`929DBED2…3427`），仅诊断。
- Git 菜单：`artifacts/pixel-followup-2026-10-18/stash-dialog/actual/actual-git-menu.png`（`F595C3E5…8310`）；Create Stash 点击后无对话框。

## 3. 边界

- 未到达 Stash 对话框；未执行任何 Git 写操作。
- 参考汉堡菜单的打开坐标/方式未成立；后续需用参考 UIA/菜单快捷键重新取证，不把本次无菜单判为产品缺陷。
- 主菜单是否为产品 54 页之一未在本轮扩大计数；实际内联菜单条登记为观察差异。

## 4. 清理

- 实际 PID 13896 正常关闭；参考 PID 10248 关闭（无响应后按本轮 PID 结束）；`settings.json` 恢复 `B8868855…DC3D`；无 Augit/pycharm64 残留；无 CDP 端口。
- 第 55 批实际 PID 18788 正常关闭；设置恢复 `B8868855…DC3D`；无参考进程；`cleanup-check-81.json`、`cleanup-check-82.json`；未提交、未推送。
