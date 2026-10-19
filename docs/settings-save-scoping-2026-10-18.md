# File View 等宽字体/字号取证与纠正（2026-10-18）

## 1. 用户裁决

`docs/design-system.md` 第 5 行（2026-10-15 用户裁决）：**等宽正文固定为随包 JetBrains Mono 13，覆盖旧等宽偏好并取消设置页的等宽字体/字号选择**。

因此实际端 File View 的 `Monospaced font JetBrains Mono` / `Size 13` **只读展示是正确的产品行为**；参考 `Editor > Font` 可编辑是参考行为，不复制。这里没有“设置保存缺口”。

## 2. 第 135 批观察

- 实际 PID 5460：File View 的 Size 无输入框；Apply 初始禁用；`settings.json` 未变。
- 以上本身符合用户裁决；当时把它记成“缺口”是错误判断。

## 3. 第 136 批误改与第 137 批回退

- 第 136 批给 File View 加了等宽字体下拉与 `codeFontSize` 输入（违反上述裁决）；正常入口 PID 24324 已显示输入框（`8F93856F…6810`）。
- 第 137 批**已回退**：`web/src/mockup.js` 与 `docs/ux-mockups/mockup.js` 恢复固定展示；两份哈希均回到 `F3D0CD53…671D`。
- `tools/test-file-view-code-size.mjs` 改为守护裁决：固定 `JetBrains Mono`/`13`、不提供等宽字体或字号输入、两份一致 → **6/6 PASS**。
- 正常入口 PID 4852 复验：File View 回到只读展示（证据 `E23F1CB2…F6B1`，与第 45 对原始证据一致）；`settings.json` 恢复 `B8868855…DC3D`。

## 4. 结论

- File View 等宽字体/字号选择**按用户裁决不提供**；与参考 `Editor > Font` 的差异登记为**已知产品差异**，不配对、不算缺口。
- 设置页合法的可保存项仍是 Appearance 的 UI 字号/主题（独立于等宽）；本轮没有对 UI 字号保存态取证，不能外推为“设置保存全部完成”。
- 页面级配对仍 48。

## 5. 清理

- 第 136/137 批的实际 PID 24324/4852 均已结束；临时无；`settings.json` 恢复；fixture 未变；`cleanup-check-157.json`（误改尝试）与 `cleanup-check-158.json`（回退复验）；未提交、未推送。
