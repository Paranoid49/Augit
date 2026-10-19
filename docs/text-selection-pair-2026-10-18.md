# 文本选择状态配对与选中色修复（2026-10-18）

## 1. 结论

完成 `notes.txt` 中**双击选词（`line`）**的参考/实际取证，并修复选中背景色：

- `ux-spec §7.2`：文本始终只读，鼠标选择和复制可用。实际选择/复制可用。
- 参考选中背景：浅色 **#A6D2FF**，选中文字黑色。
- 实际修复前：Chromium 默认选中蓝 **#3367D1**、白字；修复后 **#A6D2FF**、继承正文黑色。
- 深色计算值：**#214283** / 文字 `#BCBEC4`；未做深色屏幕截图。
- 这是**选择状态级配对**，不增加页面级同态配对 2，也不改变像素合格 0。

## 2. 方法与证据

- 实际：正常入口 PID 9056，`notes.txt`，双击第 2 行 `line`；2880×1716、DPI168、浅色。
- 参考：隔离 2026.2.1，PID 17452，`notes.txt`，双击同一 `line`；同尺寸/DPI/浅色。
- 修复前实际（选 `0123456789`，用来看默认色）：`actual/actual-selection-dblclick.png`（`a502ff59…6452`）。
- 修复后实际：`actual/actual-selection-line.png`（`054e4117…0a4a`）。
- 参考：`reference/reference-selection-word.png`（`32f60e35…bc98`）。
- 对照：`selection-compare.png`（`03edffbd…c057`）。

## 3. 修复

- 新增令牌 `--augit-selection-bg`：浅 `#A6D2FF`、深 `#214283`。
- 新增全局：

```css
::selection {
  background: var(--augit-selection-bg, #a6d2ff);
  color: inherit;
}
```

- 选中文字不再强制白色，浅色黑字、深色继承 `#BCBEC4`。
- 行号仍 `user-select:none`；正文可用鼠标选择/复制，与 `ux-spec §7.2` 一致。

## 4. 验证与边界

- 样本：参考选中区实测 (166,210,255) = `#A6D2FF`；实际修复后同值；修复前 (51,103,209) = `#3367D1`。
- 计算样式：浅 `rgb(166,210,255)` / `rgb(0,0,0)`；深 `rgb(33,66,131)` / `rgb(188,190,196)`。
- `verify-ux-offline-icons.cjs` 增加 `::selection` 背景/文字色断言，**PASS**：110 场景/主题、12 尺寸组合、2 动态流程。
- 复制验证（正常入口 PID 7796）：双击 `line` + Ctrl+C，剪贴板为 `line`；点击正文后 Ctrl+A、Ctrl+C，剪贴板为完整 41 字符 `Ag 0123456789`/`First line`/`Second line`/空行。WebView2 `AreBrowserAcceleratorKeysEnabled=false` 未阻止正文 Ctrl+C，选择/复制可用。
- 未覆盖：深色屏幕选中截图、多行鼠标拖动选择、右键菜单复制、富文本/外部剪贴板格式、终端/Diff 选择；不能外推这些状态。
- 页面级同态配对仍 **2**，像素合格整页配对仍 **0**。

## 5. 清理

- 实际 PID 9056、参考 PID 17452 正常关闭；实际关闭后设置文件曾变为 `93A22D3B…`，已从批前备份恢复 `B8868855…DC3D`；参考未改主题/配置；未触碰用户原有 PyCharm；无端口/应用残留。
- 复制验证 PID 7796 正常关闭，设置恢复 `B8868855…DC3D`；`cleanup-check-50.json`、`text-copy-test/copy-test.json`。
- `cleanup-check-49.json`；未提交、未推送。
