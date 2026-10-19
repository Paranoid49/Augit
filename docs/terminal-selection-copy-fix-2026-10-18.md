# 终端选区/复制修复与先前缺口更正（2026-10-18）

## 1. 更正：先前“选区缺口”是取证坐标错误

- 先前缺口记录把双击/拖动放在物理 `y=1135`/`1200`；CDP 实测（PID 16336，`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333`）：
  - `.terminal-view` CSS `y=686.99`（物理约 1202）；
  - `.xterm-screen` CSS `y=697.99`（物理约 1221.5），高 252 CSS；
  - 物理 `y=1135/1200` 落在终端 header/编辑区底边，不在 xterm 屏幕。
- 在正确坐标物理 `(150,1240)` 双击第 1 行 `0123456789`，`.xterm-selection` 产生 `div`，截图出现 **#A6D2FF** 选区；实际终端鼠标选区工作正常。
- 因此[先前缺口记录](terminal-selection-copy-gap-2026-10-18.md)与[根因收窄](terminal-selection-root-cause-2026-10-18.md)中“实际选区不工作”的结论**作废**，保留为取证过程记录；不是产品选区缺陷。
- 参考同状态选区为 `#A6D2FF`（`be55f6a7…1c14`），实际修复后同色。

## 2. 复制修复

`web/src/live-data.js` 的 xterm 自定义按键处理新增：

- `Ctrl+Shift+C` / `Ctrl+Insert`：有选区时读 `getSelection()`，经宿主 `clipboard/write` 写入；
- `Ctrl+C` 仍是 Shell 的 SIGINT，不拦截；
- 使用宿主桥而不是 `navigator.clipboard`（WebView2 默认不授予 ClipboardApiRequested）。

## 3. 正常入口验证

- 构建 0 警告 0 错误；PID **12644**，未启用 CDP；`notes.txt` + 8 行输出，浅色 2880×1716/DPI168。
- 双击 `(150,1240)`：选区出现；`Ctrl+Shift+C` 后剪贴板 **`0123456789`**；重置哨兵后 `Ctrl+Insert` 同样得到 **`0123456789`**。
- 选区截图：`artifacts/pixel-followup-2026-10-18/terminal-selection-debug/actual-terminal-selection-copyfix2.png`（`bd7ce9e4…4cef2`）。
- 初始选区（CDP 调试实例）截图：`actual-selection-corrected.png`（同哈希），`.xterm-selection` 有节点；调试实例 PID 16336 只用于定位，不作为验收。

## 4. 边界

- 仅验证浅色、第 1 行单词、Ctrl+Shift+C/Ctrl+Insert；深色、多行、Ctrl+Insert 在深色/大字号、右键菜单、粘贴、IME 未测。
- 快捷键判定已抽为纯函数 [`web/src/terminal-copy.js`](../web/src/terminal-copy.js)，并由 [`tools/test-terminal-copy-shortcut.mjs`](../tools/test-terminal-copy-shortcut.mjs) 覆盖 10 组组合，**PASS 10/10**；正常入口 PID 23864 再次 E2E 验证 `Ctrl+Shift+C` 得到 `0123456789`。
- 剪贴板/选区端到端仍为正常入口人工验证（需要真实 WebView2/xterm），尚未做成完全自动化 E2E。
- 不增加页面级同态配对 2，像素合格仍 0；终端选区/复制为状态级修复。

## 5. 清理

- 调试 PID 16336、验证 PID 12644、单元/E2E 验证 PID 23864 正常关闭；CDP 9333 端口关闭；设置恢复 `B8868855…DC3D`；参考 PID 17172 已在前批关闭；未触碰用户原有 PyCharm；无端口/应用残留。
- `cleanup-check-54.json`、`cleanup-check-56.json`；未提交、未推送。
