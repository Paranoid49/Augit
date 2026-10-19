# Git 不可用同态配对（2026-10-18）

## 1. 结论

完成 Git 不可用状态两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Git 不可用；Git 引用/提交/状态无法读取；文件浏览仍可用；均未执行 Git 写操作、未改文件。
- 实际：右下 toast `Git unavailable` + “The configured Git executable does not exist. Check the path. File browsing remains available.” + `Configure git.exe`；底部 Git Log `Loading commit history...`/`Reading commits from Git.`、`No references`、`Loading changes...`。
- 参考：Git 工具窗为空——无 `HEAD (Current Branch)`/`Local`/提交列表；右侧 `Commit details` 为空；未显示可见通知（可能已过期）。
- 页面级同态配对 **42 → 43**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 2320：备份 `settings.json` → 将 `gitExecutablePath` 设为 `C:/nonexistent/git.exe` → 正常入口启动 fixture；右下 toast 与空 Git Log。
- 参考 PID 3000：进程环境 PATH 移除所有含 `Git` 的段后启动（不改参考配置文件）；Git 工具窗为空；未见通知。
- 取证后实际 `settings.json` 恢复 `B8868855…DC3D`；参考无 `git.xml` 生成、配置文件未改；参考进程结束。

## 3. 已知差异与边界

- 实际有明确 toast + 配置入口；参考该状态下未显示可见通知（可能已过期），仅表现为 Git 工具窗空。
- 实际 Git Log 仍在 `Loading...`；参考为空列表；两者都不是“Git 正常”。
- 未测：修复路径后的恢复、Toast 超时、不同错误（Git 版本过低/权限）、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/git-unavailable-pair/actual/actual-git-unavailable.png`（`1A4DC318…A9A7`）。
- 参考：`reference/reference-git-unavailable4.png`（`2EB63F49…B39F`）。

## 5. 清理

- 实际 PID 2320、参考 PID 3000 已结束；实际 `settings.json` 恢复 `B8868855…DC3D`；参考配置未改（无 git.xml）；`cleanup-check-133.json`；未提交、未推送。
