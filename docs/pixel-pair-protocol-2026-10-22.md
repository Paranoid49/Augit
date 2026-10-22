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
- **面板几何也是状态**：配对设置必须显式包含 `projectPanelWidth`、`bottomPanelHeight`，且 `toolWindows.workspaceRoot` 指向本批工作区；否则 Augit 按设计忽略不属于当前工作区保存的尺寸。采集前运行：

  ```powershell
  node tools/audit/check-pixel-pair-settings.cjs <本批设置.json> <本批工作区>
  ```

  第四组首次用 `projectPanelWidth=362`、`bottomPanelHeight=321` 对齐参考保存的 Project/Version Control 权重；未对齐面板尺寸的整窗数值不能当作共享绘制差异。

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
| 项目树根行 | 2026-10-22 已改为与参考相同的完整工作区路径、常规字重，并按面板宽度省略 | 已对齐（第二组配对）；树文字栅格/基线仍差 |
| 文件状态 | 2026-10-22 已改为与参考相同的文件名颜色表达，移除行尾 `M` 徽标 | 已对齐；正常入口局部截图见[Markdown 记录](markdown-typography-2026-10-22.md) |
| 编辑区预览 | `Pixel reference` 等文字出现重影（字体/字号/左缘不一致），代码块底色与内距不同 | 2026-10-22 已按参考实机 13px 排版实现；垂直带对齐，代码字族回退宽 16 px |
| 底部 Git | 工具栏分支/日期/路径控件宽度与位置、提交行高、右侧详情列宽均不同 | 共享区域，需逐项测量 |
| 状态栏 | 两侧文本与分段位置不同 | 共享区域 |

### 4.3 关键边界实测（物理 px，2880×1716 同图坐标）

| 边界 | Augit | PyCharm | 差 |
| --- | --- | --- | --- |
| 标题栏/工具条底（chrome→白） | 77 | 77 | 0 |
| 标签条底（白色正文起点） | 145–147 | 145–147 | 0 |
| 树/编辑区竖分隔 | 699 | 703 | 4（2.3 CSS） |
| 底部工具窗顶 | 1126–1135 | 1096 / 1175–1182（多段） | 30–40 |
| 标题栏中部内容 | 显示**当前文件名**（README.md） | Run 配置选择器，标签为 `Current File`（点击菜单为 `Current File` / `Edit Configurations…`） | 参考独有能力；不是文件上下文入口，按已批准范围不新增 |

### 4.4 组件探测（同图测量）

| 组件 | Augit | PyCharm | 差 |
| --- | --- | --- | --- |
| 编辑标签外框 | x 700–946，y 80–142（247×63） | x 700–943，y 80–142（244×63） | 宽 3px，高一致 |
| 标签条带差异 | — | — | 该带原始差异仅 **10.8%**，tab 文本/圆角/关闭叉接近 |
| 项目树文字行距 | 中心 178/218/261/304/344，pitch 40/43/43/40 | 174/216/260/302/344，pitch 42/44/42/42 | 行距≈42px 一致；首行中心差 4px |
| 预览一级标题行 | y 233–270（h38），x 755–1092（w338） | y 243–278（h36），x 760–1110（w351） | 参考**低 10px**、宽 13px、高略小；左缘 +5px |
| 预览正文行 | y 323–339（h17），x 753–886（w134） | y 334–350（h17），x 758–880（w123） | 行高一致；参考低 11px、左缘 +5px、宽窄 11px |
| 预览代码行 | y 411–432（h22），x 781–1019（w239） | y 425–441（h17），x 786–972（w187） | 参考低 14px；**代码字高 17 vs 22、宽 187 vs 239**，预览代码字号明显更小 |

结论：**外框、chrome/标签条分界、标签条高度、树行距已经基本对齐**；剩余差异集中在**标题栏中部语义、树根行/状态表达、Markdown 预览的垂直起点/左缘/代码字号、底部 Git 内部布局**。上一版“标题约大 4–6%”是混合墨迹框造成的粗测，已用逐行测量替换：参考标题实际略矮但更宽，代码行明显更小。

### 4.5 参考 Markdown 预览样式的权威来源

从参考安装的 `plugins/markdown/lib/intellij.markdown.jar` 提取
`org/intellij/plugins/markdown/extensions/common/baseStyles/default.css`（存于
`artifacts/pixel-pair-main-project-2026-10-22/intellij-markdown-default.css`），与 Augit 现行规则对照：

| 规则 | 参考 `default.css` | Augit `mockup.css`（2026-10-22 修改后） | 差 |
| --- | --- | --- | --- |
| body 字体 | `Helvetica, Arial, freesans, sans-serif` | 同左 | 一致 |
| body 字号/行高 | **13px / 1.6**（参考预览字号设置 13px，覆盖插件默认 14px），`margin: 0 2em` | 13px / 1.6，`padding: 0 2em`（26px） | 一致 |
| 标题 | h1 `2.2em`、h2 `1.8em`、h3 `1.3em`，行高 1.4/1.2/1，粗体 600（Arial 600 实际匹配 Bold） | 同左，600 | 一致 |
| `pre` | **`0.85em`** JetBrains Mono（≈11.05px），行高 1.45，`padding: 16px` | `.85em`（≈11.05px）、行高 1.45、padding 16px | 一致 |
| `code`（行内） | `0.9em` | `.9em`（≈11.7px） | 一致 |

**裁决与实施（2026-10-22）**：用户对 Markdown 字体/字号、Push 失败呈现、文件状态表达统一答复“参考 PyCharm 实机与 `D:/github/intellij-community`”。本节按参考实机实现并同步修订 `product-spec.md` §7、`design-system.md` §5.3、`ux-spec.md` §7.3；计算样式验证见 `tools/audit/live-restoration-fixes.cjs` 与 `tools/verify-ux-markdown.cjs`。

**实现后正常入口复测**（PID 22728，Light、DPI168、2880×1716、参考夹具 README preview-only）：h1 墨迹 y243–278 x758–1107，参考 y243–278 x760–1110；正文 y334–355，与参考完全同带；代码行 y423–442，参考 y424–441；内容左缘相对编辑区 45 vs 参考 44。垂直排版与标题/正文位置已对齐，水平 2–3 px 来自左侧栏分隔。代码行宽 986−784=202，参考 972−786=186：参考 JCEF 实际回退较窄等宽字体，Augit 固定随包 JetBrains Mono 11.05px；登记为字体回退差异，不擅自改字族。详见[Markdown 预览参考排版](markdown-typography-2026-10-22.md)。

**第二组整窗复测（2026-10-22 晚）**：修复 Markdown 代码块底色（`DOC_CODE_BLOCK` 屏幕实测 #F4F7F9）与项目树根行（完整路径、常规字重）后，原始零容差差异从 **15.4095%** 降到 **11.0175%**；主区 18.12% → **5.41%**，编辑区 12.59% → **4.66%**。底部 Git 选择态仍不同（参考未选中提交、Augit 默认选中首行），标题栏/标签条与树文字仍有差异，整窗仍未通过。详见[第二组整窗配对](pixel-pair-2-2026-10-22.md)。

**第三组同态复测（2026-10-22 晚）**：把参考提交行也选中，统一根行选中/README/单标签，并按参考实测把 `.git-log` 三列改为 `clamp(230px,29.4%,460px) minmax(0,1fr) clamp(280px,29%,353px)`；引用面板右缘参考 940 / Augit 939，详情面板左缘两端均 2249。整窗差异 **512749/4942080 = 10.3752%**（底部 14.06%、树 7.73%、编辑区 4.66%）。状态栏仍有一处不同（参考根行焦点只显示 `fixture`，Augit 仍显示 `fixture › README.md`），仍未通过。详见[第三组整窗配对](pixel-pair-3-commit-selected-2026-10-22.md)。

**第四组面板几何同态（2026-10-22 晚）**：前三组没有对齐用户可保存的 Project 面板宽度与底部 Git 高度；参考保存布局为 Project weight `0.2225258`、Version Control weight `0.33044806`。本组在隔离设置中显式使用 `projectPanelWidth=362`、`bottomPanelHeight=321`，并把 `toolWindows.workspaceRoot` 指向参考夹具（否则 Augit 按设计忽略不属于当前工作区的尺寸）。项目树/编辑区分隔对齐到物理 703，底部 Git 顶边对齐到 1097，Git 引用/详情边界 939/2249。整窗差异 **434119/4942080 = 8.7841%**：主区 4.44%（树 7.19%、编辑区 3.55%）、底部 10.59%、标签条 9.89%、状态栏 6.06%。**没有改产品默认 token**；这是首次面板几何同态配对，仍未通过。详见[第四组面板几何配对](pixel-pair-4-panel-geometry-2026-10-22.md)。

**状态记录要求（本组补）**：整窗配对必须同时记录并核对 `projectPanelWidth`、`bottomPanelHeight`、`toolWindows.workspaceRoot`；只对齐视图/文件/标签/选择不足以判定共享布局差异。前三组的差异数仍可作为历史记录，但不是面板同态数值。

## 5. 当前结论与下一步

- 已建立并跑通「同 DPI、同最大化窗口、同视图、同文档、同显示模式」的整窗采集；
- 首组 15.4095%；第二组 11.0175%；第三组 10.3752%；第四组在面板几何同态后为 **8.7841%**，仍集中在标题栏、底部 Git 内容与树/文字栅格，不能归为已批准例外；
- 页面级同态配对仍 **50**、整窗像素合格仍 **0**。

下一步（按差异占比从高到低）：

1. 标题栏/标签条：标题栏中部已核实为参考 Run 配置选择器，属参考独有能力，不新增；剩余是标签圆角/内边距与标题栏右侧共享图标的逐项测量。
2. 项目树：文件状态已改为参考的纯颜色表达、根行已改为完整路径；剩余是全部树文字低 1–2 px 的行基线/栅格与树宽 3 px 差。
3. 编辑区预览：已按参考实际 13px 排版、代码块底色与参考同值；剩余是代码字族回退宽度、文字栅格与深色/大字号复测。
4. 底部 Git：三列宽度已按参考实测对齐（引用面板右缘 940/939、详情面板左缘 2249/2249）；剩余是参考 Console 与额外工具图标、提交详情排版，以及根行焦点时状态栏文件上下文差异。Console 与额外工具图标属参考独有能力，单列排除。
5. 每改一类重抓两端整窗图，重新计算差异与分带占比，直到共享区域可判定；通过前不登记像素合格。

## 6. 清理

- 本轮参考 PID 26952、Augit PID 20140 均已正常关闭（参考经 `Confirm Exit`），无残留 `Augit`/`pycharm64` 进程。
- 未修改参考配置与用户项目文件；参考使用隔离 config/system/log。
- 首轮另一次采集的参考 PID 6368、Augit PID 21680 也已正常关闭。

## 7. 当前阻塞与已实施裁决（2026-10-22 收尾）

1. **Markdown 预览字号/字体（已裁决、已实施）**：正文 13px Helvetica/Arial 等、`pre` .85em、`code` .9em、代码块底色 `#F4F7F9` 已落地，`product-spec.md` §7、`design-system.md` §5.3、`ux-spec.md` §7.3 已同步；第二组整窗编辑区差异降到 4.66%，剩余字形栅格与代码字族回退宽度。
2. **侧栏宽度（已核对，不是 token 缺陷）**：`side-panel-default=360` 是左侧工具窗轨道宽度，不含 37 px rail 和两侧 4 px 间隙。两端项目树左缘同为物理 x=72；Augit 白色内容止于 699、参考止于 703（差 2.3 CSS），与参考保存宽度／边框／滚动条留白的量级相符，但尚需读取参考保存值确认。**不改 token 凑图**。
3. **标题栏中部、文件状态与树根行（已实施）**：参考中部是 Run 配置选择器，属参考独有能力，不新增；文件状态已改为文件名颜色并移除 `M` 徽标；树根行已改为完整路径、常规字重。剩余是标题栏共享图标/标签几何与树文字栅格。
4. **Push 失败呈现（已实施）**：参考形态的 `Push failed` balloon + `Show details` 重开保留重试已完成，`product-spec.md` §5.1、`ux-spec.md` §7.12 已同步；正常入口 502 证据见[Push 失败通知](push-failure-balloon-2026-10-22.md)。
5. **整窗像素（未通过）**：第四组在面板几何同态后 **8.7841%**；剩余集中在标题栏（参考项目名/渐变/Run/trial）、标签条头部图标、底部 Git 内容（Console/过滤/查看图标/行内距）与树/文字栅格。不能把参考独有区域计入通过。

Settings 的深色、首内容和三条关闭路径证据见[Settings 深色首内容实测](settings-dark-first-content-2026-10-22.md)；该文同时纠正了 DWM cloak 查询属性（应查 `DWMWA_CLOAKED=14`，不是只写的 13）。

## 8. 全局设置清理（2026-10-22）

正常入口配对采集直接使用了全局设置，lastWorkspace/openFiles/ctiveFile 被写成参考夹具与 README（外观字段未变）。已按 2026-10-18 的 settings-before.json 恢复这三项为 D:\\github\\Augit、用户原标签与 	ools/release.ps1；窗口几何与视觉设置未动。后续实机采集必须用 -SettingsFile 隔离副本，结束不得回写全局设置。
