# 同态参考截图交接（2026-10-18）

> 2026-10-18 19:17 补记：首个 `main-project`/`text-viewer` 同态配对已采集并测量。参考必须用 `D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe`（product-info 2026.2.1 / 262.9437.214）；`D:/JetBrains/PyCharm 2026.2.1` 目录实际是 2026.2.3，不能使用。Find Action 没有 “Hide Right Sidebar”；`View | Appearance | Tool Window Bars` 会同时隐藏左侧工具轨，右条只能登记为结构差异。详见[首个同态配对](homologous-main-text-pair-2026-10-18.md)。

## 当前已推送状态

- 分支 `dsh`，HEAD `b3d8a8b5911001b9d1e7807d31492983979ca658`，已推送至 `origin/dsh`。
- 已跟踪文件干净；仅剩未跟踪 `.tmp/`，不得删除。
- 本批产品改动：终端标签 `Local` + 悬停当前 Shell；紧凑输入窗口空名称/空白校验。
- 本批验收：20 状态浅深英文审计、设置无 CDP 全关闭动作、冲突 Continue/Abort/Skip、默认工作区启动冒烟。详见[状态总表](retained-acceptance-status-2026-10-18.md)。

## 当前 PyCharm 参考截图（不是同态参考）

- 用户当前 PyCharm PID20336，窗口 2880×1716。
- 已激活并截图：`artifacts/pixel-followup-2026-10-18/reference-current-pycharm.png`。
- 该图**不能用作合格参考**：
  - 打开的是 Augit 仓库，不是参考 fixture；
  - 左侧是 Commit 工具窗，不是 Project；
  - 当前是深色主题，实际端是浅色；
  - 右侧通知/AI 栏仍可见；
  - 编辑器是 AGENTS.md + Markdown 预览，不是 notes.txt 只读页；
  - 底部 Git/Log 工具窗可见。
- 仅保留为“当前用户窗口状态”证据，不进入 54 页配对。

## 下一会话第一步

用户已授权“自己操作鼠标键盘截图”。下一会话按以下顺序做**一个**同态参考，不要一次改太多：

1. 核实 PyCharm 实例 PID/启动时间/标题，不能按旧 PID 盲操作；不要关闭用户其他窗口。
2. 用真实键盘/鼠标：
   - `Ctrl+Shift+F12` 收起所有工具窗；
   - `Alt+1` 只开 Project；
   - `Ctrl+O` 打开 `D:/JetBrains/PyCharm-2026.2.1-reference/fixture/notes.txt`（或先把 fixture 作为项目打开，保持与 Augit 实际端同一内容）；
   - `Ctrl+Shift+A` 搜索 `Hide Right Sidebar` 并执行，隐藏右侧通知/AI 栏；
   - 确认底部 Git/Log、Commit、Markdown 预览都关闭；
   - 如主题不是浅色，临时切浅色并记录原主题，截完恢复；
   - 拖动 Project/编辑器分隔条到实际端同宽（当前实际约 360 CSS；1440 物理列宽在 DPI168 下约 2520 物理像素窗口，按实际截图再核）。
3. 激活窗口后用 `tools/audit/capture-screen-region.ps1 -ExpectedProcessId <当前PID>` 截整窗，保存 PNG + 元数据；不要用 CDP 页面截图冒充桌面参考。
4. 核对：2880×1716、DPI168、浅色、Project 宽度、notes.txt 内容、无右栏、无底栏、无 Commit、无预览。
5. 截完恢复用户原主题/工具窗布局；不修改用户文件。
6. 只有同态参考成立后，才把 `main-project`/`text-viewer` 的整页配对写入状态表；否则继续登记为阻塞。

## 不要做

- 不把当前 `reference-current-pycharm.png` 当同态参考。
- 不为了对齐参考改实际端产品结构；参考操作只影响 PyCharm UI 布局，截完恢复。
- 不重启/关闭用户 PyCharm，除非用户明确要求；优先在当前窗口操作。
- 不重复已否证的 DWM/自绘阴影、终端字体/相位、捕获挂起实验。
