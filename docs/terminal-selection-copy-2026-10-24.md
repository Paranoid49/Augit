# 终端鼠标选区与 Ctrl+Shift+C 复制（2026-10-24）

> 结论：真实外壳中执行 `echo COPY_TOKEN_12345`，用真实鼠标从输出行左侧拖选，
> 按 **Ctrl+Shift+C** 后 Windows 剪贴板精确等于 `COPY_TOKEN_12345`。
> 这是浅色、当前 DPI 的单一文本选区证据；不是参考像素，也未覆盖深色/多行。

## 1. 方法

- 临时 Git 仓库 + 隔离设置；`start-augit.ps1 -SkipBuild` 正常启动。
- 真实点击 Terminal 轨道，等待 `.terminal-tool`；点击终端正文；
  剪贴板粘贴 `echo COPY_TOKEN_12345` + Enter。
- 从 `.xterm-rows` 读取行：输出行文本精确为 `COPY_TOKEN_12345`（第 5 行，y=708.6）。
- 真实鼠标从输出行左侧拖到右侧（CSS x=57→197，y=717.6），屏幕物理
  `221,1343 → 466,1343`。
- 按 **Ctrl+Shift+C**（不是 Ctrl+C；`terminal-copy.js` 的复制快捷键是
  Ctrl+Shift+C 或 Ctrl+Insert），等待后读取 Windows 剪贴板。
- CDP 只读行几何、`.xterm-selection` 节点和截图；点击使用脚本内嵌的
  DPI-aware 坐标（客户区原点 + `GetDpiForWindow`）。

## 2. 结果

| 项 | 值 |
| --- | --- |
| 输出行 | `COPY_TOKEN_12345`，x=53.0、y=708.6、高 18 |
| 拖选 | CSS 57,717.6 → 197,717.6；物理 221,1343 → 466,1343 |
| `.xterm-selection div` | 2 个 |
| 剪贴板 | **`COPY_TOKEN_12345`**（无额外提示符/换行） |
| 截图 | `artifacts/terminal-selection-copy-2026-10-24b/terminal-selection.png` |
| 报告 | `terminal-selection-copy.json` |
| 实例 | PID 18852 正常关闭，`pidAlive=false` |

截图中选区蓝色覆盖输出 token；下一行提示符未包含在剪贴板内。

### 2.1 深色补充

同一命令、同一拖选坐标在 Dark 主题重跑：

- 输出行 y=708.6、选区 CSS 57→197；
- `.xterm-selection div` 2 个；
- 剪贴板仍为 **`COPY_TOKEN_12345`**；
- 截图：`artifacts/terminal-selection-copy-dark-2026-10-24/terminal-selection-dark.png`；
- 实例 PID 22352 正常关闭，`pidAlive=false`。
- 深色选区色与浅色不同，但仍是行为/截图证据，不是参考像素。

### 2.2 多行补充

执行 `echo COPY_LINE_1; echo COPY_LINE_2`，从第一行输出拖到第二行输出：

- 输出行 y=708.6（`COPY_LINE_1`）、y=726.6（`COPY_LINE_2`）；
- 拖选 CSS 57,717.6 → 193,735.6（物理 221,1343 → 459,1374）；
- `.xterm-selection div` 3 个；
- 剪贴板精确为：
  ```text
  COPY_LINE_1\r\nCOPY_LINE_2
  ```
  （Windows 换行，没有提示符/额外行）；
- 截图：`artifacts/terminal-selection-copy-multi-2026-10-24/terminal-selection-multi.png`；
- 实例 PID 2168 正常关闭，`pidAlive=false`。

这验证了跨两行的连续选区复制；仍不是参考像素，也未覆盖块选择。

### 2.3 反向拖选 + Ctrl+Insert

同一 token 从右向左拖选，并使用 **Ctrl+Insert** 复制：

- 拖选 CSS 193,717.6 → 57,717.6（物理 459,1343 → 221,1343）；
- `.xterm-selection div` 2 个；
- 剪贴板仍精确为 `COPY_TOKEN_12345`；
- 截图：`artifacts/terminal-selection-reverse-insert-2026-10-24/terminal-selection-insert.png`；
- 实例 PID 16368 正常关闭，`pidAlive=false`。

这补齐了反向拖选和 Ctrl+Insert 两个快捷键路径；仍不是参考像素。

### 2.4 选区背景色

对已有选区截图按 token 行裁切，取选区面积内最常见的精确色：

| 主题 | 选区背景 | 像素数 | 文字色 |
| --- | --- | ---: | --- |
| 浅色 | `#A6D2FF` | 6,019 | `#000000` |
| 深色 | `#214283` | 5,980 | `#BCBEC4` |

- 与 `terminalSelectionPalette()` 的 `selectionBackground` 完全一致；
- 仍是截图/行为证据，不是参考 PyCharm 选区像素；未测失焦选区 `#B5B7BD`/`#4C4F56`。




## 3. 失败路径（不是产品缺陷）

- 第一次拖选选中了**命令行**（`PS ...> echo COPY_TOKEN_12345`），并误用 Ctrl+C；
  剪贴板仍是旧值 `Usage`。
- 改正为输出行 + Ctrl+Shift+C 后通过。
- 这也说明 `terminal-copy.js` 的快捷键口径是 Ctrl+Shift+C/Ctrl+Insert；普通 Ctrl+C
  不触发复制，符合当前实现，不应写成复制失败。

## 4. 边界

- 只测浅色/深色、反向与两行连续拖选、当前 DPI；未测块选择、
  双击/三击、鼠标释放到窗口外。
- 未采参考 PyCharm 选区像素；选区颜色/抗锯齿没有逐像素配对。
- 剪贴板只验证文本精确值，不验证富文本/MIME 或复制后的终端状态。

## 5. 复验

```powershell
node tools/audit/terminal-selection-copy.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/terminal-selection-copy-2026-10-24b
```

- 脚本正常关闭实例、删除临时仓库；无 Augit/终端子进程残留。
