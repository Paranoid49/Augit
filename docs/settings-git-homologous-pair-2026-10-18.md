# Settings/Git 同态配对（2026-10-18）

## 1. 结论

完成设置 Git 页两端取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：Settings 的 Git 页；均检测到 `C:\Program Files\Git\cmd\git.exe`；均未修改、未保存；Apply 不可用。
- 实际：标题 `Settings — Augit`；1820×1330；左栏 Git；`git.exe` 留空则搜索系统 PATH；说明“Git is unavailable without a valid configuration; file browsing remains available.”；Detection result `Detected:C:\Program Files\Git\cmd\git.exe (2.45.1)`；Minimum version Git for Windows 2.40 or newer；OK/Cancel/Apply。
- 参考：标题 `Settings – notes.txt`；1772×1215；`Version Control > Git`；`Path to Git executable` Auto-detected 同一路径，Test 按钮；`Set this path only for the current project`、ignored directories、Commit（staging/CRLF/50MB/GPG）、Push（protected branches `master;main`）；OK/Cancel/Apply。
- 页面级同态配对 **35 → 36**；像素合格仍 **0**。

## 2. 取证路径

- 实际 PID 23424：正常入口 → 齿轮 → Settings → Git（左侧）。
- 参考 PID 11612：Ctrl+Alt+S → Settings → 搜索 `Git` → `Version Control > Git`。
- 两端均未修改；取证后关闭。

## 3. 已知差异与边界

- 实际是轻量 Git 检测页；参考是完整 Git 配置（Test、Commit/Push、protected branches 等），不在产品范围，不照搬。
- 实际显示版本 2.45.1；参考只显示自动检测路径，未在同页显示版本。
- 未测：无效 git.exe、Test、修改保存/取消、File View/Terminal 页面、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/settings-git-pair/actual-settings-git.png`（`3B28901A…36CB`）。
- 参考：`reference/reference-settings-search.png`（`7638B2F7…0299`）。

## 5. 清理

- 实际 PID 23424、参考 PID 11612 已结束；`settings.json` 恢复 `B8868855…DC3D`；未保存；`cleanup-check-122.json`；未提交、未推送。
