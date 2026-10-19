# Commit 空态取证与参考阻塞（2026-10-18）

> **第 86 批更新**：通过在参考 `trusted-paths.xml` 临时预信任临时 clone，信任对话框不再出现，参考主窗口正常前台并取得 Commit 空态；已登记为 [Commit 空态同态配对](commit-empty-homologous-pair-2026-10-18.md)（页面级 27）。配置已恢复。本页保留两次阻塞过程。

## 1. 结论

在临时干净 clone 上取得实际 Commit 空态；参考端被 Trust 对话框后的输入桌面/前景不可用阻塞，**未建立同态配对**，页面级仍 25。

- 临时 clone：`D:/github/Augit/.tmp/commit-empty-fixture`（从 fixture 克隆，工作树干净；取证后已删除）。
- 实际 PID 2100：Commit 工具窗显示 `No changes to commit` / `The working tree matches HEAD.`；Commit、Commit and Push 禁用；Amend/Commit message/底部 Log 正常；顶部 workspace chip `commit-empty-fixture`。
- 参考 PID 22852：打开临时 clone 时先出现 **Trust and Open Project 'commit-empty-fixture'?**；`Enter` 信任后主窗口加载（标题 `commit-empty-fixture – README.md`），但随后 `GetForegroundWindow()=0`、`OpenInputDesktop()` 返回 0，无法取得前景/输入桌面，无法执行 Ctrl+K 与截图。未取得参考 Commit 空态。
- 实际空态截图：`commit-empty-pair/actual/actual-commit-empty.png`（`B1207639…1895`）。
- 参考 Trust 对话框：`reference-commit-empty.png`（诊断，非目标页）。

## 2. 边界

- 实际 Commit 空态页面本身可用；“No changes to commit” 与参考的文案/布局尚未同态对照。
- 参考 Trust 对话框是打开未信任临时目录的安全提示；实际无对应 Trust 页，该差异未登记为配对。
- 输入桌面/前景不可用是**本轮环境/会话状态限制**，不是产品失败；后续在桌面可交互时重试参考 Commit 空态，不重复整轮 Git 写测试。

## 3. 清理

- 实际 PID 2100、参考 PID 22852 已关闭；临时 clone 已删除（`.tmp` 中历史资源保留）；`settings.json` 恢复 `B8868855…DC3D`；无 Augit/pycharm64 残留；无 CDP 端口。
- 本批未修改代码；`cleanup-check-105.json`；未提交、未推送。

## 4. 第 83 批重试（2026-10-19 08:26）

- 输入桌面曾恢复（`OpenInputDesktop()` 返回有效句柄 2308），重新创建临时 clone、启动参考 PID 22668；再次出现 Trust，`Enter` 信任后主窗口加载为 `commit-empty-fixture – README.md`，但 `GetForegroundWindow()` 仍为 0。
- `AllowSetForegroundWindow(ASFW_ANY)`、`ShowWindow(SW_RESTORE)`、`SetWindowPos(SWP_SHOWWINDOW)`、`SwitchToThisWindow`、`SetForegroundWindow` 均无法把该窗口设为前台；无法 Ctrl+K/稳定截图。
- 判定为**环境/会话前台限制**（参考主窗口无法激活），不是产品失败；未建立配对，页面级仍 25。临时 clone 再次删除；`cleanup-check-106.json`。
