# 工具轨 Tooltip 与窗口边缘观察（2026-10-24）

> 结论：真实鼠标悬停 Terminal 轨道按钮后，出现原生 tooltip `Terminal`；按钮只有
> `aria-label="Terminal"`，没有 `title`。整窗截图 2511×1592，2px 边框内无纯白像素，
> 未观察到白色/透明边框帧。这是行为与边缘像素观察，不是参考 tooltip 像素通过。

## 1. 方法

- 临时 Git 仓库 + 隔离设置；真实 `start-augit.ps1 -SkipBuild`，PID 22408。
- 等待 `window.__augitReady`，CDP 读取：
  - `.rail-button[data-tool="terminal"]` 的几何/aria-label/title；
  - 所有 `.rail-button`、`.toolbar-button`、`.icon-button` 的 label/title。
- 用 DPI-aware `SetCursorPos` 把鼠标移到 Terminal 轨道中心（屏幕 `153,1514`），
  不点击；等待 1.6s。
- PowerShell `Graphics.CopyFromScreen` 按 `GetWindowRect` 截取整个窗口（含标题栏/边框）：
  - 悬停前：`window-before.png`
  - 悬停后：`window-tooltip.png`
- CDP 客户区截图 `client.png`；比较悬停前后截图差异。

## 2. Tooltip

| 项 | 值 |
| --- | --- |
| 控件 | Terminal 轨道按钮，37×40 CSS，y=795.3 |
| 可访问名 | `Terminal` |
| `title` | **null** |
| 悬停后 | 左侧出现原生 tooltip，文字 `Terminal` |
| 差异 bbox | x 0–225、y 1377–1480（含轨道 hover 高亮与 tooltip） |
| 实例 | PID 22408 正常关闭，`pidAlive=false` |

- 其余轨道按钮（Project/Search/Terminal/Git Log）也都只有 `aria-label`、无 `title`；
  带 `title` 的按钮（如 Collapse Project Tree）仍有 title。
- tooltip 没有被窗口左/下边缘裁切；文字完整。
- 这是 Chromium/WebView2 对可访问名的原生提示，不是产品自绘 tooltip；与 PyCharm
  参考 tooltip 的圆角、字体、延迟、位置未做像素配对。

## 3. 窗口边缘

- `GetWindowRect`：`121,87,2511,1592`（物理，168 DPI）。
- 整窗 PNG 外圈 2px 共 16,412 个像素，**纯白 0 个**；上/右/下边缘主色为
  `#E9EAEE`，左边缘含 `#D9DADD`/`#CCCDD0` 的过渡像素。
- 悬停前后截图尺寸/窗口位置不变；没有观察到透明/白色边框帧。
- 这是稳态单帧，不是关闭/启动瞬间的逐帧录像；系统阴影仍是已知差异，不能写成
  “窗口边缘像素通过”。

## 4. 边界

- 只测浅色、当前 DPI、Terminal 一个 tooltip；未测深色、窄窗、参考 tooltip 像素、
  tooltip 延迟/消失/键盘触发。
- 只观察稳态窗口边缘；未覆盖启动首帧、关闭末帧、最大化/还原、跨屏 DPI。
- 工具轨没有 title 但仍有原生 tooltip，说明 `aria-label` 被 WebView2 用作提示；
  这不等于参考的自绘 tooltip 样式一致。
- 页面级配对仍 50、像素仍 0。

## 5. 复验

```powershell
node tools/audit/tooltip-window-edge.cjs <playwright> artifacts/tooltip-window-edge-2026-10-24
```
