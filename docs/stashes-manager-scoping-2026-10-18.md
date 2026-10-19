# Stash Manager 取证（2026-10-18）

## 1. 结论

完成实际端 Stash Manager 取证；参考端未找到对应入口，**未建立同态配对**，页面级仍 14。

- 实际正常入口 PID 11820：Git 菜单 → `Stashes…`，打开独立 `Stash` 窗口；工具栏 `+`／删除（禁用）／刷新；左侧 `Empty`、右侧 `No Stashes`；右下 `Close`；窗口约 1627×673。
- 参考 PID 16528：Find Action `Stashes` 未打开管理界面；Find Action `Stash Manager` 无结果，界面停留在 `app.py` 编辑器/Find Action 空列表。未取得参考 Stash Manager。
- 实际是独立模态窗口，参考按 PyCharm 习惯可能是 Git 工具窗口的 Stashes 页；入口/形态未核对，不作产品结论。
- 未创建/删除 Stash，fixture 无 Git 写操作。

## 2. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/stashes-pair/actual/actual-stashes-window.png`（`47101012…58CA`）。
- 参考无结果：`reference/reference-stashes2.png`（`A77E269C…630E`）；首次 `Stashes` 也无管理界面（`reference-stashes.png`）。

## 3. 边界

- 未测：非空 Stash 列表、创建/应用/弹出/删除、搜索、刷新、空态文案对比、深色。
- 需用参考 Git 菜单/工具窗口或 UIA 重新定位 Stash Manager，再判断形态差异。

## 4. 清理

- 实际 PID 11820、参考 PID 16528 已结束；`settings.json` 恢复 `B8868855…DC3D`；无 Augit/pycharm64 残留；无 CDP 端口。
- 本批未修改代码；`cleanup-check-87.json`；未提交、未推送。
