# 整窗同态像素配对：协议与首次采集（2026-10-22）

本文记录用户裁决第 5 条（两边都有的功能页面必须像素合格）的**整窗配对协议**与首次同尺寸采集结果。
本轮只建立采集与状态对齐流程，**没有任何页面被判定为像素合格**。

## 1. 环境身份

| 项目 | 值 |
| --- | --- |
| 参考 | PyCharm 2026.2.1，`build.txt` = `PY-262.9437.214`，`product-info.json` version 2026.2.1 / 262.9437.214 |
| 参考配置 | `PYCHARM_PROPERTIES=D:/JetBrains/PyCharm-2026.2.1-reference/reference-2026.2.1.properties`（独立 config/system/log） |
| 参考进程 | PID 6368，主窗 SunAwtFrame 8193738 |
| Augit | `ProductVersion=0.1.0+283bac011160b0f20fcf9ff9d9900a471196460f`（当前 HEAD），PID 21680 |
| 启动方式 | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/start-augit.ps1 -Workspace D:/JetBrains/PyCharm-2026.2.1-reference/fixture` |
| 桌面 | 2880×1800，DPI 168（scale 1.75）；两端均最大化 |
| 窗口几何 | 外框 2904×1740 @ (-12,-12)；客户区帧 2880×1716 @ (0,0) |

## 2. 采集方法

- 使用 `tools/audit/capture-screen-region.ps1 -ExpectedProcessId <pid> -Padding 0`：`CopyFromScreen` 逐像素抓取，校验前台进程、窗口句柄、DPI、激活稳定性，输出同名 `.json` 记录 `frameRectPhysical`、`imageSize`、`foregroundBeforeAndAfter`、`sha256`。
- 每次切换前台用 Alt+Tab（`SetForegroundWindow` 在无输入所有权时被系统拒绝；`AppActivate` 亦不可靠），切换后先用 `GetForegroundWindow` 核对 PID 再抓图。
- **PrintWindow 只能作诊断**：JetBrains 编辑器正文由加速表面绘制，`PrintWindow` 抓到的编辑区是空白；首次 `pycharm-before.png` 即为此限制的证据，不能用于像素结论。
- 原始图保存在 `artifacts/pixel-pair-main-project-2026-10-22/`（Git 忽略）。

## 3. 首次采集暴露的状态差异（均为像素合格的前置阻塞）

1. **参考恢复了一个已删除文件的标签**，启动即弹 `Warning / File not found.`；工程树还残留 `huge.txt`（fixture 中已不存在）。必须先关闭残留标签、刷新工程视图，否则两侧文件集不同。
2. **参考处于未授权试用态**：标题栏右侧 `Start Free Trial`。这是许可证环境产物，不属于 Augit 功能；像素报告须单列为排除项或先授权，不能混入共享区域比较。
3. **参考显示 `No Python interpreter configured for fixture` 横幅**：Python 解释器是参考专有能力，Augit 不提供；按用户裁决第 5 条属“不具备的功能”，界面保持风格即可，不计入像素对照区域。
4. **工程树内容不同**：PyCharm 默认 Project 视图显示 `External Libraries`、`Scratches and Consoles` 并隐藏 `.idea`；Augit 显示 `.idea`（且可展开到 `inspectionProfiles`、`.name` 等），不显示前两者。这是共享项目树功能的真实差异，必须二选一收敛：参考切到 `Project Files` 视图（显示 `.idea`）以对齐，或 Augit 按参考默认隐藏以点开头的配置目录。未收敛前不能做整窗像素结论。
5. **Markdown 显示模式不同**：PyCharm 打开 README.md 时是“源码编辑器 + 预览”分栏并带编辑器横幅/工具栏；Augit 是只读预览单栏。需把参考切到 preview-only（编辑器右上角第三个图标），再与 Augit 比较。
6. **标签集合不同**：参考有 `notes.txt`、`app.py`、`README.md`；Augit 有 `.name`、`README.md`。必须两侧各只保留 `README.md` 一个标签，且激活态一致。
7. **右轨**：参考右侧有工具窗竖条与通知/工具图标；Augit 按用户裁决第 1 条不实现 42 CSS 右轨，属**明确批准的范围差异**，像素报告单列排除，不计作通过。

## 4. 首组同态采集（2026-10-22 第二轮）

按第 3 节的阻塞逐项对齐后，取得首组**状态同态**的整窗图：

| 项 | Augit | PyCharm |
| --- | --- | --- |
| 图像 | `augit-readme-final2.png` | `pycharm-pf3.png` |
| sha256 | `d1c74aa36882ef182d40cfbd189c39916827c98ba27974a374b4c851113f54a8` | `0e0d6f48e3a2f26e44e99557b623ceaa918338d0faee7b01b33f28e05c36e0b7` |
| 尺寸/DPI | 2880×1716 / 168 | 2880×1716 / 168 |
| 视图 | Project（Augit 文件树，显示 `.idea`） | **Project Files**（显示 `.idea`，已关掉 External Libraries/Scratches） |
| 文档 | README.md 预览，单标签激活 | README.md preview-only，单标签激活 |
| 参考状态 | 无试用/解释器横幅（README 预览态） | `Start Free Trial` 仍在（许可证环境） |

对齐手段：PyCharm 关闭残留标签、切 `Project Files` 视图、README preview-only、关闭 `app.py`/`notes.txt` 标签；Augit 关闭 `.name` 标签、折叠 `.idea`。参考的 `Start Free Trial`、右侧工具窗竖条属环境/已批准例外。

### 4.1 原始逐像素差异（无容差）

- 差异像素 **761,552 / 4,942,080 = 15.4095%**；差异包围盒 `(0,0)–(2870,1705)`。
- 分带：标题栏 **44.94%**、工具栏 **13.25%**、树+编辑区 **18.12%**、底部工具窗 **11.52%**、状态栏 **5.55%**。
- 差异掩码：`artifacts/pixel-pair-main-project-2026-10-22/diff-mask.png`。

### 4.2 差异分类（从掩码可辨认）

| 类别 | 具体差异 | 处理 |
| --- | --- | --- |
| 已批准例外 | 参考 `Start Free Trial`、右侧工具窗竖条；Augit 无 | 单列排除，不计通过 |
| 标题栏/标签 | 标签圆角、内边距、激活底色与参考不同；参考有 trial 按钮 | 共享区域，需整改 |
| 项目树根行 | Augit `fixture` + 灰色路径后缀；参考直接显示完整路径 | 共享区域，需按产品裁决统一 |
| 文件状态 | Augit 行尾 `M` 徽标；参考 Project Files 视图无徽标（状态以颜色表达） | 共享区域，需对齐 |
| 编辑区预览 | `Pixel reference` 等文字出现重影（字体/字号/左缘不一致），代码块底色与内距不同 | 共享区域，需按参考字体度量 |
| 底部 Git | 工具栏分支/日期/路径控件宽度与位置、提交行高、右侧详情列宽均不同 | 共享区域，需逐项测量 |
| 状态栏 | 两侧文本与分段位置不同 | 共享区域 |

### 4.3 关键边界实测（物理 px，2880×1716 同图坐标）

| 边界 | Augit | PyCharm | 差 |
| --- | --- | --- | --- |
| 标题栏/工具条底（chrome→白） | 77 | 77 | 0 |
| 标签条底（白色正文起点） | 145–147 | 145–147 | 0 |
| 树/编辑区竖分隔 | 699 | 703 | 4（2.3 CSS） |
| 底部工具窗顶 | 1126–1135 | 1096 / 1175–1182（多段） | 30–40 |
| 标题栏中部内容 | 显示**当前文件名**（README.md） | `Current File` 下拉 | 语义差异 |

### 4.4 组件探测（同图测量）

| 组件 | Augit | PyCharm | 差 |
| --- | --- | --- | --- |
| 编辑标签外框 | x 700–946，y 80–142（247×63） | x 700–943，y 80–142（244×63） | 宽 3px，高一致 |
| 标签条带差异 | — | — | 该带原始差异仅 **10.8%**，tab 文本/圆角/关闭叉接近 |
| 项目树文字行距 | 中心 178/218/261/304/344，pitch 40/43/43/40 | 174/216/260/302/344，pitch 42/44/42/42 | 行距≈42px 一致；首行中心差 4px |
| README 一级标题墨迹框 | 453×103 | 471×109 | 参考约大 4–6% |

结论：**外框、chrome/标签条分界、标签条高度、树行距已经基本对齐**；剩余差异集中在**标题栏中部语义、树根行/状态表达、Markdown 预览字体比例、底部 Git 内部布局**。下一轮优先用组件探测把 Markdown 预览字号/缩放调到参考比例，再处理树根行与底部 Git。

## 5. 当前结论与下一步

- 已建立并跑通「同 DPI、同最大化窗口、同视图、同文档、同显示模式」的整窗采集；
- 首组**未通过**：原始差异 15.41%，且差异集中在共享区域，不能归为已批准例外；
- 页面级同态配对仍 **50**、整窗像素合格仍 **0**。

下一步（按差异占比从高到低）：

1. 标题栏/标签条：先确认「标题栏中部是否显示当前文件名」与「标签条是否独立成 37px 行」的产品裁决；Augit 目前把文件名放进标题栏中部、标签条与正文同底色，参考是 `Current File` 下拉 + 独立标签条。
2. 项目树：统一根行呈现（名称 + 路径的样式/省略）与文件状态表达（徽标 vs 颜色），以参考 Project Files 视图为准；
3. 编辑区预览：按参考 Markdown 预览的字体、字号、段落间距、代码块内距逐项测量，消除文字重影；
4. 底部 Git：工具栏控件宽度、提交行高、详情列宽按参考测量后整改；
5. 每改一类重抓两端整窗图，重新计算差异与分带占比，直到共享区域可判定；通过前不登记像素合格。

## 6. 清理

- 本轮参考 PID 26952、Augit PID 20140 均已正常关闭（参考经 `Confirm Exit`），无残留 `Augit`/`pycharm64` 进程。
- 未修改参考配置与用户项目文件；参考使用隔离 config/system/log。
- 首轮另一次采集的参考 PID 6368、Augit PID 21680 也已正常关闭。
