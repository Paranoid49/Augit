# 新建分支/标签名称校验（2026-10-18）

## 问题

`submitCompactDialog()` 在新建分支或标签名称为空时只设置 `window.__augitCheckoutError` 并返回，紧凑输入窗口本身没有显示错误。用户点击 Create 后窗口保持打开但无任何反馈，属于静默失败。

## 修复

- 在 `submitCompactDialog()` 内增加 `showError(message)`，写入当前 `[data-compact-dialog] .footer-help` 的文本和 `title`。
- 空名称显示 `Name cannot be empty.`；含空白名称显示 `Name cannot contain whitespace.`。
- 保留原有 `window.__augitCheckoutError`，不改变 Git 调用、校验条件或窗口关闭行为。

## 正常入口回归

- 启动：`tools/start-augit.ps1` 强制非增量构建，临时设置副本，CDP 诊断；工作区为参考 fixture。
- 实际 PID 20524、端口 64786。
- 打开 Git Log，右键提交，选择 **New Branch…**，在空名称输入框点击 Create。
- 窗口保持打开，底栏显示 `Name cannot be empty.`；再输入 `bad name` 点击 Create，显示 `Name cannot contain whitespace.`；再右键提交选择 **New Tag…**，空名称同样显示 `Name cannot be empty.`，标题 `Create New Tag On <hash>`。
- 三次提示均写入 `title`，无中文、无页面错误。
- `CloseMainWindow` 成功，端口关闭，临时目录删除。
- 证据：`artifacts/pixel-followup-2026-10-18/branch-create-validation-2/`（`result.json`、`cleanup.json`）。
- 工具：`tools/audit/branch-create-validation.cjs`。

## 边界

- 这是独立正常入口回归，不把英文审计的 17 状态计数增加为 18；英文审计已覆盖的 17 个状态不变。
- 该修复不改变分支/标签创建成功路径，也不涉及内置编辑或保存。
