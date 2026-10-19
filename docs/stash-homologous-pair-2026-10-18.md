# Stash 对话框同态配对（2026-10-18）

## 1. 结论

在**正常启动入口**取得修复后的 Stash 对话框，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：`Stash` 标题、Git root、当前分支 `main`、Message、Keep index、Create Stash/Cancel；空消息；均未执行 Stash；浅色、DPI168。
- 实际（PID 5464）：`Git root` 只读文本框；`Keep index` 与 `Include untracked files` **同一行、完整可见**；无标题图标/消息历史/帮助；窗口约 1085×579。
- 参考（PID 2632，前批）：`Git Root:` 下拉；`Current Branch: main`；`Message:` + 历史图标；`Keep index`；无 Include untracked；底部帮助；窗口约 815×499。
- 页面级同态配对 **12 → 13**；像素合格仍 **0**。

## 2. 修复（第 57 批）

- 根因：live/static Stash 的 `.form-grid` 把 Keep index 与 Include untracked 当成两个独立行；默认 620×337 高度下 Include 行落到 body 折叠线以下，只露出半行。
- 修复：两复选包进 `.stash-check-row`，`grid-column: 1 / -1`、flex 同行、`gap: 24px`、标签不再单独 `margin-top`；仍保留权威/用户第 211 轮裁决的 Include 控件。
- 回归：`tools/test-stash-check-row.mjs` 5/5 PASS；`verify-ux-offline-icons.cjs` PASS 110 场景/主题；`verify-ui-assets.ps1` PASS。
- 正常入口 PID 5464：Git 菜单 → Create Stash 打开；两复选同一行，无截断/滚动。

## 3. 已知差异

- 实际 Git root 为只读文本，参考为下拉；实际有 Include untracked（用户裁决对齐权威，参考 2026.2.1 该对话框未出现）、参考有消息历史/帮助/标题图标。
- 标题大小写、窗口尺寸、输入/按钮几何、行距。
- 未测：Stash 成功/失败、保留索引/未跟踪行为、空消息校验、深色、多仓库。

## 4. 证据

- 实际修复后（正常入口）：`artifacts/pixel-followup-2026-10-18/stash-fix/actual-stash-normal.png`（`3C8E24E5…F9AD`）。
- 参考：`artifacts/pixel-followup-2026-10-18/stash-dialog/reference/reference-stash-dialog.png`（`3C672911…9621`）。
- 修复前 CDP 取证：`stash-dialog/actual/actual-stash-dialog.png`（仅诊断，不计配对）。
- 当前 `mockup.js` `DDF905A9…9377`；`mockup.css` `EBEAE830…4D6B`。

## 5. 清理

- 实际 PID 5464 正常关闭；参考 PID 2632 前批结束；`settings.json` 恢复 `B8868855…DC3D`；未执行 Stash、无 Git 写；无残留；`cleanup-check-84.json`；未提交、未推送。
