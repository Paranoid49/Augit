# UI 重构基线审计

更新日期：2026-09-14。本文件记录重构开始前的可复现基线，以及本轮建立的取证工具。数值均为实测，方法可复现。

## 1. 目标与实测对象

- 视觉目标：`docs/ux-mockups/`（`mockup.css` 93 KB、`mockup.js` 213 KB，共 40+ 场景）与 `docs/ux-spec.md`。
- 视觉稿本身是**可运行的 HTML/CSS/JS 实现**，不是静态图片；其场景通过 URL 参数（`theme`、`ui-size`）切换浅深色与字号。
- 现行实现：`src/Augit.App`（119 个文件、66573 行，原生 Win32 自绘）+ `src/Augit.Core`（2573 行）+ `src/Augit.Infrastructure`（9356 行）。

## 2. 像素基线（main-project / 深色）

方法：视觉稿用 Playwright + 本机 Chromium 以视口 1180×760、`deviceScaleFactor=1` 渲染截图；原生用
`Augit.App.VisualAuditHost main-project --dpi=96` 采集，按窗口逻辑宽度 1180 缩放到同一尺度后逐像素比较。

| 区域 | 平均通道差 | RMS |
|---|---|---|
| 整体 | 12.6 | 38.1 |
| 标题栏 | 3.8 | 18.9 |
| 状态栏 | 7.0 | 23.2 |
| 左侧项目树 | 14.6 | 40.7 |
| 正文区 | 13.8 | 40.8 |
| 底部面板 | 16.0 | 40.1 |
| 右侧 Git 面板 | 30.2 | 62.7 |

亮度分布：视觉稿暗部（<90）95.6%、亮部（>150）2.5%、均亮 40.5；原生 93.4% / 3.7% / 42.9。

结论：**窗口骨架（标题栏、状态栏）最接近，数据密集区（项目树、正文、底部面板、右侧 Git 面板）偏差最大**，
其中右侧 Git 面板平均差 30.2，是最需要重建的区域。

注意：该基线仍受两处未消除的干扰——原生采集为物理像素需重采样；两侧内容不同源（视觉稿用固定样例内容，
原生显示真实仓库内容）。因此上述数值用于**区域定位**，不作为逐像素验收结论。

## 3. 本轮建立的取证工具

`tools/audit/capture-surface.ps1`：用 `CopyFromScreen` 采集**真实屏幕像素**（design-system 要求用于字体与
抗锯齿验收的证据类别），输出窗口矩形、前台状态与白色占比，并在判定被遮挡时重试。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\audit\capture-surface.ps1 `
  -Exe <审计宿主 exe> -Settings <设置 json> -Workspace <工作区> `
  -Surface main-project -Out <输出 png> -Dpi 96
```

同时确认的宿主事实：

- `Augit.App.VisualAuditHost <设置> <工作区> [surface] [截图路径] [--dpi=96|120|144] [--unified] [--json-source] [--wide-graph] [--empty-left|--empty-right]`。
- 主窗口类名 `Augit.MainWindow.Native`；窗口构造期间会短暂上报 `-34x0` 一类退化矩形，枚举窗口必须轮询到尺寸稳定。
- 审计宿主可用 Release 或 Debug 输出直接运行；`PrintWindow` 采集不受遮挡影响，但按 `design-system.md` 只能证明结构。

## 4. 环境约束（影响验收方式）

- 本环境前台窗口长期被 DSH Web GUI 浏览器占用，`SetForegroundWindow` 无法稳定把被测窗口提到前台，
  因此真实屏幕像素采集会被遮挡。这是 `docs/visual-refinement-status.md` 中反复出现
  「实屏逐像素校准仍未完成」的直接原因。
- PowerShell 5.1 按 ANSI 读取 `.ps1`，脚本内**不得包含非 ASCII 字符**，否则解析失败。
- 构建用 Windows 侧 SDK：`/mnt/c/Program Files/dotnet/dotnet.exe`（10.0.201）。

## 5. 已知且被日志反复确认的差距类别

引自 `docs/visual-refinement-status.md`（1547 行）的自我声明，按出现规模排序：

1. 与参考同内容、同 DPI 的实屏字体／逐像素校准未完成（覆盖 69 个小节）。
2. Windows 10 22H2 实机验收未完成（58 处）。
3. 全应用／正式性能验收未完成（58 处）。
4. 具体页面整页未完成：文件历史、Blame、三栏冲突解决器、复杂多轨提交图、图标与文件类型颜色、图片、JSON 错误条（约 2px）、
   Markdown 正文左缘（约 2px）、终端首开（约 2.5 秒）、设置、Clone、Reset/Rollback、Push、Worktree 等。
5. `PrintWindow` 只能证明结构，不能作为屏幕字形证据（与 `design-system.md` §12 一致）。

## 6. 复用边界

`src/Augit.Core` 与 `src/Augit.Infrastructure` 共 11929 行，是纯逻辑与 Win32 互操作层（Git 命令与解析、工作区文件、
rg 搜索、ConPTY 终端、设置存储、实例协调），与界面绘制方式无关，重建界面时可整体保留。
