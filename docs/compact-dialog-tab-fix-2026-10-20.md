# 紧凑输入窗口 Tab 卡在 Cancel 修复（2026-10-20）

## 1. 现象与根因

正常入口实测（New Branch，名称为空）：

- 焦点顺序：`Branch name` → `Cancel` → **再次 `Cancel`**（连续 4 次 Tab 都停在 Cancel），无法用键盘到达 Close/Create。
- 根因在 `web/src/live-data.js` 的 `bindCompactDialogKeys()`：Tab 顺序数组把 `Create` 也包含进来，而名称为空时 Create 是 `disabled`；`disabled.focus()` 是空操作，于是每次 Tab 都“跳向”Create 却停留在 Cancel。
- 同一段逻辑服务 New Branch / Rename / New Tag / Go to Line 四个紧凑输入窗口。

## 2. 修复

- 新增纯函数模块 `web/src/compact-dialog-focus.js`：
  - `compactDialogFocusOrder(candidates)`：过滤 `disabled` 与 `aria-disabled="true"`；
  - `nextCompactDialogFocus(order, active, backwards)`：按当前位置循环，当前焦点不在顺序里时向前取首项、向后取末项。
- `bindCompactDialogKeys()` 改用该模块；其他 Enter/Esc 规则不变。
- 行为：名称为空时 Tab 顺序为 `field → Cancel → Close → field`；名称非空时包含 Create，回到 `field → Cancel → Create → Close → field`。

## 3. 验证

- 单元测试 `node tools/test-compact-dialog-focus.mjs` PASS（禁用/aria-disabled 跳过、正反向循环、未知焦点、空顺序）。
- 真实正常入口（PID 14972，隔离设置副本、UIA 读取焦点）：
  - 空名称：`Branch name → Cancel → Close`（修复前为 `Branch name → Cancel → Cancel → Cancel`）。
  - 名称非空：UIA 在该会话把焦点报告成窗口级（`New Branch [Window]`），未能取得可靠逐项证据，改由 live-shell 断言覆盖。
- live-shell 紧凑窗口段落：名称非空走 `field,cancel,confirm,close`，清空后走 `cancel,close`（跳过禁用主按钮）；该段落已通过，套件继续运行到后续 Stash 段落。
- 真实 Stash 创建框（同一轮）的按钮顺序按 Windows 约定断言为 `Create Stash` 在 `Cancel` 左侧，Tab 顺序 `keep → includeUntracked → create → cancel`。

## 4. 边界

- 未复查深色主题、Shift+Tab 的真实键盘（单元测试覆盖反向）、其他三个紧凑窗口的逐一实机；它们共用同一绑定。
- 这是键盘可达性修复，不是像素或整页验收；页面级仍 50、像素 0。
- live-shell 套件仍有后续过期断言（见[live-shell 测试债务](live-shell-test-debt-2026-10-20.md)），不能把本修复当作套件整体通过。
