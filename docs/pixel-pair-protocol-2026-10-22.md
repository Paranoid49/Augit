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

## 4. 当前结论

- 已建立**同分辨率、同 DPI、同最大化窗口**的整窗采集链路，并取得两端 2880×1716 的真实屏幕图；
- 但状态尚未同态（上述 1–6），本轮**没有产生可判定的整窗像素配对**；
- 页面级同态配对仍 **50**、整窗像素合格仍 **0**；不得把本轮图像当作像素通过证据。

## 5. 下一轮执行顺序

1. 参考：关闭 `huge.txt` 残留标签与警告，`File > Reload from Disk` 刷新工程树；处理试用/许可证显示（授权或登记为排除项）。
2. 参考：切到 `Project Files` 视图（或确定让 Augit 隐藏 `.idea` 的产品裁决），使两侧树内容一致。
3. 参考：README.md 切 preview-only；两侧各只保留 README.md 单标签并激活。
4. 两端按第 2 节方法各抓一张整窗图，先核对 PNG 尺寸、DPI、窗口矩形与前台记录完全一致，再做逐像素差异；
5. 差异按“共享区域 / 已批准例外（右轨、许可证、解释器横幅、Python 专有）”分类出报告，共享区域逐项整改后重抓，直到整窗共享区域可判定；
6. 通过后才把该页登记为像素合格；未通过的差异保留原图与坐标，不用容差或掩码抹平。

## 6. 清理

- 本轮参考 PID 6368、Augit PID 21680 均已正常关闭（参考经 `Confirm Exit`），无残留 `Augit`/`pycharm64` 进程。
- 未修改参考配置与用户项目文件；参考使用隔离 config/system/log。
