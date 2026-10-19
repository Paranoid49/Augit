# 深色 accent-brand 令牌订正（2026-10-18）

## 1. 问题

深色主题块把 `--augit-accent-brand` 写成了浅色 `blue-80` `#3871E1`，而权威 `accent-brand-bg` 的深色值是 `Blue6` `#3574F0`：

- `docs/design-system.md`（主框架入口与终端标题节）明确“选中底与前景取 `ToolWindow.Button.selectedBackground` = `accent-brand-bg`（浅 `blue-80` `#3871E1`／深 `#3574F0`）……Augit 的 `--augit-accent-brand` 本来就与这两个值一致”。
- `verify-ux-titlebar.cjs`、`verify-ux-frame-buttons.cjs`、`verify-ux-document-button-states.cjs`、`verify-ux-reset-rollback.cjs` 均以深色 `rgb(53, 116, 240)` 为期望；本次运行 `verify-ux-frame-buttons` 在深色 accent 断言处实际得到 `rgb(56, 113, 225)`，是首个明确的实现偏差。

## 2. 修改

`docs/ux-mockups/mockup.css` 深色块：

```css
--augit-accent-brand: #3574f0;
```

浅色 `#3871E1` 不变；深色 `--augit-blue` 原本就是 `#3574F0`，现在两个蓝值一致。关闭终端、工具轨激活底、焦点环和主按钮共享该令牌，因此这是全局深色修正，不是终端局部特例。

### 2.1 同一轮相邻令牌表对齐

`design-system` 的详细段把深色 hover/pressed 写成旧值 `#FFFFFF16`/`#FFFFFF26`，而深色令牌表和实现是 `#FFFFFF17`/`#FFFFFF29`。本轮把详细段对齐到令牌表/实现；`verify-ux-frame-buttons` 的期望随之改为 hover `0.09`、pushed `0.16`、faint `#4C4F56`（深色令牌表 `faint` 即 `#4C4F56`）。这是规范/测试一致性修正，不改变浅色值，也不声称深色整页像素通过。

## 3. 验证

直接计算样式（Chromium，terminal.html，浅/深各一次）：

| 主题 | `--augit-accent-brand`（body 计算值） | 激活工具轨 `::before` |
| --- | --- | --- |
| light | `#3871e1` | `rgb(56, 113, 225)` |
| dark | `#3574f0` | `rgb(53, 116, 240)` |

- 新增 `tools/verify-ux-dark-accent.cjs`：**PASS**。浅色 `#3871E1`/`rgb(56,113,225)`，深色 `#3574F0`/`rgb(53,116,240)`，直接读取 `terminal.html` 的 body 令牌与激活工具轨 `::before`。
- `verify-ui-assets.ps1`：PASS（视觉稿与运行时字节一致）。
- `verify-ux-frame-buttons.cjs`（仅 size 13 子集）：accent/pushed/hover/faint 断言已通过，随后在激活工具轨边框既有期望 `rgb(30,31,34)` vs 实际 `rgb(25,26,28)` 处失败；该套件在 size 40 还有终端头部缩放/重叠等既有问题，不记通过。
- `verify-ux-titlebar.cjs`：在既有数组期望处失败；`verify-ux-document-button-states.cjs` 在旧中文按钮名 `当前文件搜索` 处超时。两者均为英文审计/旧期望遗留，不记通过。
- 复验命令：

```powershell
node tools/verify-ux-frame-buttons.cjs <playwright> <chromium> <evidence>
# 或直接读取 terminal.html?theme=dark 的 body/激活轨道计算样式
```

## 4. 边界与清理

- 只订正深色 accent 令牌；未改浅色，未改任何布局、字号或终端格宽。
- 没有深色同态参考图；本记录只证明令牌与权威值/既有断言一致，不宣称深色整页像素通过。
- 本批未启动 Augit 或 PyCharm；只运行本地 headless Chromium 测试，测试浏览器已关闭。证据在 `artifacts/pixel-followup-2026-10-18/dark-accent-2026-10-18/`；未提交、未推送。
