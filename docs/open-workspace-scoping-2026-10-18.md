# Open Workspace 空态取证尝试（2026-10-18）

## 1. 结论

清空 `lastWorkspace`/`recentWorkspaces` 后无参启动，实际端仍打开 `D:/github/Augit`（回落到启动目录/默认仓库），**未出现 Open Workspace/欢迎页**，未建立配对，页面级仍 45。

- 实际 PID 16176：`start-augit.ps1` 无 `-Workspace`；`settings.json` 的 `lastWorkspace=""`、`recentWorkspaces=[]`；窗口标题 `Augit`、Project 根 `D:/github/Augit`、Git Log 为 Augit 仓库。
- 参考端未启动。
- 本次只读取仓库，没有新增未跟踪文件；`.tmp/` 保留。

## 2. 后续

- 正常入口是否提供“关闭工作区”/Open Workspace 动作需先核对产品规范与真实菜单；不能仅凭清空设置判断页面存在。
- 参考端用无项目启动的 Welcome 页采集；实际找到真实入口后再配对。

## 3. 清理

- 实际 PID 16176 已结束；`settings.json` 恢复 `B8868855…DC3D`；无 Augit/pycharm64 残留；无 CDP 端口。
- 本批未修改代码；`cleanup-check-141.json`；未提交、未推送。
