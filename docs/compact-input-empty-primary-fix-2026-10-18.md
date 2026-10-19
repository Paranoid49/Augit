# 紧凑输入窗口空名称主按钮修复（2026-10-18）

## 1. 结论

修复 New Tag 空输入时 `Create` 仍可点击、与参考 `OK` 禁用不一致的问题：

- 根因：`showCompactInput()` 只聚焦/全选输入框，未把确认按钮的禁用态与名称绑定；`submitCompactDialog()` 只在提交后报 `Name cannot be empty.`。
- 修复：`showCompactInput()` 取得 `[data-compact-action="confirm"]`，`confirm.disabled = field.value.trim().length === 0`；监听 `input` 同步；打开时立即同步。
- 影响同一入口的 **New Branch / Rename / New Tag**：空名称确认禁用，有名称启用；New Tag 与参考 `OK` 的空态一致。
- Go to Line / Checkout Tag or Revision 走 `compactInputDialog` 直建路径，本次不改，避免扩大范围。

## 2. 回归与正常入口

- `tools/test-compact-input-empty.mjs` **7/7 PASS**；`node --check live-data.js` 通过；`verify-ui-assets.ps1` PASS。
- **正常入口 PID 3308**：
  - Git Log 提交右键 → New Tag，空输入 `Create` 灰色禁用；
  - 输入 `auc-empty-check` 后 `Create` 变蓝启用；
  - Esc 关闭，未创建标签。
- 修复后 `live-data.js` `FC04772FBFC1F61C7AFD57A302651A0993B11B526C31CBF2A7FA1FBE231017B1`。

## 3. 仍存差异

- New Tag：标题完整长哈希换行、无标题图标/问号、窗口尺寸；New Branch/Rename 的空态主按钮行为同步改变，未逐页重采。
- 未测：非法/空白标签名、同名标签、force、成功/失败、深色。

## 4. 证据与清理

- 空态：`artifacts/pixel-followup-2026-10-18/new-tag-fix/actual-new-tag-empty.png`（`108575AA…185A`）。
- 填名：`actual-new-tag-filled.png`（`AF5A6636…B293`）。
- PID 3308 正常关闭；设置恢复 `B8868855…DC3D`；无标签/Git 写；`cleanup-check-97.json`；未提交、未推送。
