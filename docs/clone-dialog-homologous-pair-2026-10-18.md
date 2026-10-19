# Clone Repository 对话框同态配对（2026-10-18）

## 1. 结论

完成 Clone Repository（Repository URL 表单）取证，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：`Clone Repository` 标题、Version control `Git`、URL 空输入、Directory、`Shallow clone … truncated to 1 commits`、`Clone` 禁用、`Cancel`；均**未执行 Clone**；浅色、DPI168。
- 实际：无左侧 provider 列表/标题图标/`More via plugins…`；标签 `Repository URL`；Directory 预填 `D:\github\Augit`；窗口约 1627×505。
- 参考：左侧 `Repository URL / GitHub / GitHub Enterprise / GitLab`，`Repository URL` 选中；PyCharm 标题图标；`More via plugins…`；标签 `URL`；Directory `C:\Users\wl024\PycharmProjects`；窗口约 1751×1260。
- 页面级同态配对 **17 → 18**；像素合格仍 **0**。

## 2. 取证路径

- 实际正常入口 PID 9064：点击工作区 chip → `Open Workspace` → `Clone Repository…`；Esc 关闭，未 Clone。
- 参考 PID 12112：Find Action `Clone Repository` → 先显示 GitLab 登录页；点击左侧 `Repository URL` 切到 URL 表单；Esc 关闭，未 Clone。

## 3. 已知差异

- 实际直接 URL 表单；参考 provider 引导 + 左侧账号列表、`More via plugins…`。
- 标签 `Repository URL` vs `URL`；实际无标题图标；Directory 默认来源不同；窗口尺寸/行距。
- 未测：URL 校验、目标目录存在/非空、Shallow depth、Clone 成功/失败、账号登录、深色。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/clone-pair/actual/actual-clone2.png`（`6C83591F…2074`）。
- 参考 URL 表单：`reference/reference-clone-url.png`（`EFBD0EAA…D7BB`）；参考 provider 首页：`reference/reference-clone.png`（`7DF176BB…F73C`，诊断）。

## 5. 清理

- 实际 PID 9064、参考 PID 12112 已结束；`settings.json` 恢复 `B8868855…DC3D`；未 Clone、fixture 无目录/远端写；无残留；`cleanup-check-94.json`；未提交、未推送。
