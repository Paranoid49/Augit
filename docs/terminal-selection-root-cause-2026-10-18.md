# 终端选区缺口根因收窄：复制快捷键不是唯一原因（2026-10-18）

## 0. 2026-10-18 21:10 更正

复制快捷键试验无效的真实原因是**点击坐标在终端 header/编辑区**，不是选区层故障；CDP 定位后正确双击 `(150,1240)` 可产生选区，复制处理器随后验证通过。本记录保留为排查过程，结论由[终端选区/复制修复](terminal-selection-copy-fix-2026-10-18.md)取代。

## 1. 试验（历史）

在 `live-data.js` 的 xterm 自定义按键处理里临时加入：

- `Ctrl+Shift+C` / `Ctrl+Insert` 读取 `terminalInstance.getSelection()`，经宿主 `clipboard/write` 写入；
- 保留 `Ctrl+C` 作为 Shell SIGINT，不拦截。

构建 0 警告 0 错误，正常入口 PID 19212，打开终端执行同一 8 行命令，双击第 1 行后按 `Ctrl+Shift+C`：

- 剪贴板仍为哨兵 `SENTINEL-COPYFIX`；
- 截图仍无选区高亮（`actual-terminal-selection-copyfix.png`，`f3b0bbbe…e0cf`）；
- 说明 `hasSelection()` 为 false，复制处理器没有被有效选区触发，**不是单纯缺复制快捷键**。

试验代码已回退，`web/src/live-data.js` 无净差异；本轮不保留未验证逻辑。

## 2. 结论

- 根因收窄为：**xterm 选区层/鼠标指针映射/焦点**中的一项或多项未建立，导致鼠标拖动/双击不产生选区；参考同坐标可选。
- 仅补 `Ctrl+Shift+C` 复制入口不足以修复；必须先让实际终端产生选区。
- 仍未定位到具体 DOM/事件层原因；未修改产品行为，未宣称修复。
- 未测：CDP/DevTools 下的 xterm 选区状态、深色、右键菜单、Ctrl+Insert（无选区时同样无效果）。

## 3. 清理

- 实际 PID 19212 正常关闭；设置恢复 `B8868855…DC3D`；未启动参考 PyCharm；未触碰用户原有 PyCharm；无端口/应用残留。
- 本批无净代码改动；`cleanup-check-53.json`；未提交、未推送。
