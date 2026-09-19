# PyCharm 交互基线（由 JSON 生成，请勿手改）

> 机器源：`docs/baselines/pycharm-interactions.json`。本文件由
> `node tools/audit/gen-interaction-baseline.cjs` 生成；改动请改 JSON 再重新生成。
> 目标 ⑯ 要求「补基线与核对共用同一份数据」——核对入口是
> `node tools/audit/check-interactions.cjs`（校验每条 `augit.ref` 指向的断言真实存在）。

## 采集环境

| 项 | 值 |
| --- | --- |
| product | PyCharm |
| edition | Professional |
| version | 2026.2.1 |
| installPath | D:\JetBrains\PyCharm 2026.2.1 |
| configDir | %APPDATA%\JetBrains\PyCharm2026.2 |
| theme | Islands Light (laf.xml: laf themeId="Islands Light") |
| editorColorScheme | Light Theme |
| zoom | 100% |
| uiFont | Microsoft YaHei (Use custom font, checked) |
| dpi | 168 |
| scalePercent | 175 |
| window | {"rect":"-12,-12 2904x1740","state":"maximized","logicalRect":"1659x994（= 2904/1.75）","note":"窗口矩形含最大化时超出屏幕的不可见 resize 边框（2880+24=2904；1800-60=1740），所以宽度大于屏幕物理宽度是正常的"} |
| project | D:\github\Augit |
| capturedAt | 2026-09-19 |
| uiFontOverride | {"enabled":true,"face":"Microsoft YaHei UI","size":12,"source":"options/other.xml: overrideLafFonts/fontFace/fontSize","note":"12.0 是 PyCharm 自己的单位（UI 里显示为 Size 12），不能直接等同于 Augit 的 13px；对照时必须按几何/视觉等价测，不得当成同值"} |
| editorFont | {"customized":false,"source":"options/editor-font.xml 只有 VERSION=1（未自定义，用主题内置默认）"} |
| consoleFont | {"customized":false,"source":"options/console-font.xml 只有 VERSION=1"} |
| terminalFont | {"customized":false,"source":"options/terminal-font.xml 只有 VERSION=1（SECONDARY_FONT_FAMILY 空）"} |
| ideScale | {"presentationModeIdeScale":1.75,"source":"options/other.xml"} |
| screen | {"device":"\\\\.\\DISPLAY1","boundsPhysical":"2880x1800","workingAreaPhysical":"2880x1676","dpi":168,"scalePercent":175,"logicalArea":"1646x1029","evidence":"GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN) + GetDpiForSystem=168 + GetDpiForMonitor=168 (scale 175%)，均在本进程 SetProcessDPIAware 之后读取；此前记录的「1646x1029 物理 = 941x588 逻辑」是**重复缩放**的错值（已被 2904 宽的窗口证伪）","correction":"2026-09-19 复核：1646x1029 是**逻辑**尺寸，物理为 2880x1800；工作区 2880x1676 物理（1800-1676=124 物理 ≈ 71 逻辑，任务栏）"} |

## 已确认的裁决

- **spec_7_17_conflict**：按规格 §7.17 + PyCharm 结构落地（现有视觉稿把等宽字体/字号画在『外观』页是基线自身的错）
  - 确认人/时间：用户 2026-09-19 确认
  - 落地方案：`{"外观":["主题","界面字体","界面字号"],"文件查看":["正文字体","等宽字体","等宽字号","默认换行"],"Git":["git.exe 路径","检测结果","最低版本说明"],"终端":["Shell 类型","自定义启动命令"],"导航":"点击分类真正切页，右页只显示当前分类；切换保留未保存编辑；搜索框按分类过滤"}`
- **compare_theme**：A —— 直接在 PyCharm 当前 Light 主题下对照，不切换其 IDE 主题
  - 确认人/时间：用户 2026-09-19 选择 A；备注：Augit 侧用 --theme light；dark 主题的 PyCharm 对照本轮不做（用户不改 IDE 偏好）
- **settings_dirty_marker**：只补『未保存修改』标记，不做分组折叠；标记复用现有令牌 --augit-blue
  - 确认人/时间：用户 2026-09-19 选择『只补标记，不做分组折叠』
  - 落地方案：`{"标记位置":"设置导航里该分类行 + 对话框标题行","触发":"该分类存在未保存草稿（live.settingsDraft 含该页字段）","清除":"保存成功后消失；取消/关闭对话框不改磁盘也不留标记","分组折叠":"不做（Augit 分组表头保持现状）"}`
- **default_wrap_conflict**：从设置页去掉『默认换行』一行，只保留正文查看时的自动换行动作
  - 确认人/时间：用户 2026-09-19 选择『从设置页去掉这一行』
- **image_zoom_scope**：按 ux-spec §7.5 实现完整的图片缩放/平移（选项①），并保留视觉稿里的四个按钮
  - 确认人/时间：用户 2026-09-19 选择①（按 ux-spec 实现/保留完整缩放平移）；第 333 轮更正：实现早已存在，本轮补断言

## 界面（surfaces）

### pycharm.main：主窗口

- **layout**：`顶部 = 主工具栏(菜单/工作区下拉/运行控件/搜索/设置/窗口按钮)；左 = 工具窗口按钮条(竖排)；左栏 = 工具窗口内容；中 = 编辑器标签与正文；右 = 工具窗口或编辑器；底 = 状态栏`
- **evidence**：`["artifacts/pycharm-baseline-20260919/pycharm-main.png"]`
- **observations**：`["当前项目树展开到 D:\\github\\Augit；编辑器标签 3 个（audit-clone-seq3.bmp / product-spec.md / performance-rep...）","编辑器当前行(46)有整行浅绿底高亮，行号槽独立列宽","Light 主题：内容面板近白、分隔线浅灰、选中/激活用浅蓝"]`

### pycharm.settings：Settings

- **host**：`modal dialog (SunAwtDialog 'Settings - Augit'), opens on Ctrl+Alt+S`
- **layout**：`左侧 = 搜索框 + 分类树；右侧 = 当前分类页面（顶部面包屑 'Appearance & Behavior > Appearance'）`
- **treeTopLevel**：`["Python","Jupyter","Project Structure","Appearance & Behavior","System Settings","File Colors","Scopes","Notifications","Data Editor and Viewer","Quick Lists","Required Plugins"]`
- **treeAppearanceChildren**：`["Appearance","Menus and Toolbars"]`
- **rightPageAppearance**：`{"theme":"Islands Light","editorColorScheme":"Light Theme","checkbox":"Different tool window background in the dark theme","accessibility":{"zoom":"100%","useCustomFont":true,"fontName":"Microsoft YaHei","supportScreenReaders":false}}`
- **evidence**：`["artifacts/pycharm-baseline-20260919/pycharm-settings-appearance.png"]`
- **observations**：`["界面字体(UI font)属于 Appearance & Behavior > Appearance（Accessibility 区的 Use custom font），不是编辑器字体页","编辑器/等宽字体在 PyCharm 里是另一个顶层分类 Editor > Font（本轮尚未抓取，待补）","选中项用浅蓝底整行高亮（Appearance 行），未选中项无底色；分类树带展开箭头，可折叠"]`
- **treeFull**：`{"Appearance & Behavior":["Appearance","Menus and Toolbars","System Settings","Notifications","Quick Lists","Required Plugins","Trusted Locations","Path Variables","Presentation Assistant","Data Editor and Viewer"],"Keymap":[],"Editor":{"General":["Auto Import","Appearance (modified marker: dot)","Breadcrumbs","Code Completion","Code Folding","Console","Editor Tabs","Gutter Icons","Postfix Completion"],"Smart Keys":[],"Sticky Lines":[],"Code Editing":[],"Font":[],"Color Scheme":["General","Language Defaults","Color Scheme Font","Console Font","Code Review","Console Colors","Debugger","Diff & Merge","User-Defined File Types","VCS"],"Inspections":[],"Live Templates":[],"Reader Mode":[]},"Plugins":[],"Build, Execution, Deployment":[],"Tools":["Terminal","Advanced Settings"],"Languages & Frameworks":["Markdown"],"Jupyter":["Jupyter General"],"Python":[],"Project Structure":[],"Scopes":[],"File Colors":[]}`
- **searchBehaviour**：`搜索框输入关键字后左侧分类树被过滤成只含命中页面（实测 font 命中 Editor>Font、Color Scheme Font、Console Font；terminal 命中 Tools>Terminal）`
- **breadcrumb**：`右页顶部面包屑『分组 › 页面』+ 前进/后退箭头 + 右侧固定图标；被修改但未应用的页在树里带实心圆点（实测 Appearance 行有蓝点）`

### pycharm.settings.appearance：Appearance & Behavior › Appearance

- **fields**：`{"Theme":"Islands Light（下拉；勾选 Sync with OS 时为只读灰态）","Sync with OS":"已勾选（对应 Augit 的『跟随 Windows』）","Editor color scheme":"Light Theme default（灰态）","Different tool window background":"未勾选","Accessibility.Zoom":"100%（提示 Alt+Shift+= / - / 0 调整）","Accessibility.Use custom font":"已勾选 Microsoft YaHei UI，Size 12（与 options/other.xml 的 fontSize=12.0 一致）","Accessibility.Support screen readers":"未勾选（Requires restart）","Use contrast scrollbars":"未勾选","Adjust colors for red-green vision deficiency":"未勾选","UI Options":"Compact mode 未勾选；Drag-and-drop with Alt pressed only 未勾选；Smooth scrolling 已勾选"}`
- **evidence**：`["pycharm-settings-appearance.png","pycharm-settings-search-font.png"]`

### pycharm.settings.editor-font：Editor › Font（路径已确认，字段待二次采集）

- **pathEvidence**：`搜索 font 后分类树中出现 Editor > Font（见 pycharm-settings-search-font.png）`
- **note**：`编辑器字体属于顶层分类 Editor（与 ux-spec §7.17 把等宽字体放在『文件查看』一致）；**页面正文（Font/Size/Line height/Fallback 的取值）仍未抓到**，但已确认树路径、Console Font 的第二条路径，以及三次搜索落空的具体去向与证据图 —— 下一步只需在前台解锁后按 navigationRecipe 点一次并截图。`
- **treeConfirmed**：`{"evidence":"artifacts/pycharm-interactions-16/p16c-editor-font.png（搜索 font 后落到 Color Scheme，但左侧树完整展开）","editorSubtree":["General（Appearance、Editor Tabs）","Font","Color Scheme（Color Scheme Font、Console Font、Code Review、VCS）","Inspections","Live Templates","Reader Mode","Code Style（Python、EditorConfig、HTML、…）","Natural Languages"],"consoleFontPath":"Editor › Color Scheme › Console Font（与 Editor › Font 是两处不同的字体设置）"}`
- **navigationRecipe**：`清空搜索框 → 展开 Editor → **直接点 Font**。不要用搜索跳转：三次实测都被带到别的页面（见 failedSearchPaths）——固定坐标/搜索驱动是这台机器上反复抓不到该页正文的原因。`
- **failedSearchPaths**：`[{"query":"font","landed":"Editor › Color Scheme","evidence":"artifacts/pycharm-interactions-16/p16c-editor-font.png"},{"query":"editor font","landed":"Keymap","evidence":"artifacts/pycharm-interactions-16/p16b-page-editor-font.png"},{"query":"line spacing","landed":"Editor › Code Style › HTML","evidence":"artifacts/pycharm-interactions-16/p16d-linespacing-y450.png"}]`

### pycharm.settings.tools-terminal：Tools › Terminal

- **fields**：`{"Terminal engine":"Reworked 2025","Command Completion":"已勾选 Show a completion popup as you type（Only for parameters）；popup Ctrl+Space；insert with Enter","Start directory":"D:\\github\\Augit（默认取项目目录）","Environment variables":"（空）","Font Settings":"Font: JetBrains Mono；Fallback: JetBrains Mono；Size: 13.0；Line height: 1.0；Column width: 1.0","Configure colors…":"链接"}`
- **evidence**：`["pycharm-settings-search-terminal.png"]`
- **note**：`PyCharm 的终端字体是独立设置；Augit 按产品规格第 163 行让终端跟随『等宽字体/字号』——属既定产品选择，不是缺陷，但必须在 ⑦ 对照表里列为差异`

## 跳转关系（jumps）

| id | 触发 | 到达 | 类型 | 规格出处 | Augit |
| --- | --- | --- | --- | --- | --- |
| settings.dirty-marker | 在某分类页修改字段但未 Apply | 该分类行出现实心圆点（未应用标记） | indicator | PyCharm 行为 | ❌ 缺口（Augit 没有『哪个分类有未保存修改』的指示：切页草稿保留，但用户看不到改动位置） |
| settings.nav.switch | 点击分类树中的页面行 | 右侧页面替换为该分类（面包屑同步） | in-place | ux-spec §7.17 | ✅ 一致（live-shell:§7.17 点击"文件查看"真正切页且右页只显示该分类） |
| settings.open | Ctrl+Alt+S | 模态 SunAwtDialog（标题 Settings - <项目名>） | modal | ux-spec §7.17 | ✅ 一致（live-shell:§5.1 齿轮图标打开设置窗口 \\| live-shell:§7.17 设置窗口有四个分类入口与分组表头） |
| settings.search-filter | 在搜索框输入关键字 | 左侧分类树被过滤为命中页面 | filter | ux-spec §7.17 | ✅ 一致（live-shell:§7.17 搜索框按分类名过滤） |

## 各态反馈（feedback）

| id | 目标 | PyCharm | Augit | 判读 |
| --- | --- | --- | --- | --- |
| rail.button.hover | 左侧工具窗口按钮条（rail button） | {"method":"同尺度（2904x1740 物理）两图对比：逐 8px 网格找差异 > 12 的单元，再对**变化单元本身**取均值（不是整框均值，也不是单点采样）","changedCells":47,"bbox":"x 24..392, y 232..1464","idleMean":"208,211,216","hoverMean":"217,219,224","caveat":"这是**变化单元的聚合均值**（含图标/边框像素），不能当成纯底色采样值","evidence":["artifacts/pycharm-interactions-16/p16-main-idle.png","artifacts/pycharm-interactions-16/p16-main-hover-toolbutton.png"]} | {"status":"diff","token":"--augit-hover: #f1f2f4 (=241,242,244)","rule":"mockup.css .rail-button:hover","ref":"live-shell:真实悬停改变行背景","note":"两侧都有悬停反馈，但取值差异明显：PyCharm 变化单元聚合 ≈217,219,224（更暗），Augit 悬停底色 241,242,244（更亮）。按 design-system 的 --augit-hover 实现，不改。"} |  |
| settings.field.disabled | 不可用字段（Theme 在 Sync with OS 勾选时） | "文字转灰、下拉不可展开" | {"status":"diff","note":"Shell 非『自定义命令』时禁用自定义启动命令输入框（同一手法）"} | live-shell:§7.17 终端自定义命令仅在选择『自定义命令』时可编辑 |
| settings.primary-button | 主按钮（OK） | {"bg":"#3871e1","note":"实测取色 (1180,1150)"} | {"token":"--augit-blue: #3871e1","note":"逐值相同"} |  |
| settings.surfaces | 对话框/树/底栏 底色 | {"bg":"#f7f8f9","searchFieldBg":"#ffffff","lowerAreaBg":"#d9dbdb"} | {"chrome":"#e9eaee","panel":"#ffffff","panelMuted":"#f5f8fe"} | 不同：PyCharm 面板灰 #f7f8f9，Augit 按 design-system 用冷灰标题栏 #e9eaee + 白色内容面板 #ffffff（ux-spec §4.4 明文规定）。属规范内既定选择，不是缺陷；⑦ 对照表如实列出。 |
| settings.tree.selected | 分类树选中行 | {"bg":"#d0dffe","note":"整行浅蓝底（实测取色，原图坐标 (250,690)）"} | {"token":"--augit-blue-soft: #d0dffe","note":"浅色令牌与 PyCharm 实测逐值相同"} | live-shell:§7.17 默认停在『外观』…（选中行 selected 类） |
| statusbar.hover | 状态栏 | {"method":"同上（同一套网格与阈值）","changedCells":0,"note":"两图 md5 不同（3f37a60d… vs c116df40…），但在 8px 网格 + 阈值 12 下**零变化单元**：要么悬停未生效，要么差异小于该采样分辨率（例如只有 1px 顶边线）。**因此这张图不能作为状态栏悬停取值的证据**。","evidence":["artifacts/pycharm-interactions-16/p16-main-idle.png","artifacts/pycharm-interactions-16/p16-main-hover-statusbar.png"]} | {"status":"inconclusive","note":"Augit 状态栏本身没有 :hover 规则（grep 无命中），因此这一格既不是 diff 也不是 pass：两侧都缺少可比对的悬停取值证据"} |  |

## 操作序列与状态转换（sequences）

| id | 步骤 | 期望 | 规格出处 | Augit |
| --- | --- | --- | --- | --- |
| seq.diff.loading-threshold | ["选中一个改动文件","差异在 150ms 内返回 → 不显示任何加载提示","差异较慢 → 150ms 后左侧/文件标题行出现加载提示","正文到达 → 提示被正文替换且不残留"] |  | ux-spec §9.1（加载超过阈值才提示）+ §6.5 | ✅ 一致（live-shell:§9.1 等待阈值：加载提示延迟约 150 毫秒出现 \\| live-shell:150 毫秒内不显示加载动画 \\| live-shell:§9.1 已显示：加载提示被正文替换 \\| live-shell:加载完成后不残留加载提示） |
| seq.diff.request-failure | ["选中一个改动文件","宿主读取失败","列表与当前选择保持原样","用户已输入的草稿不丢"] |  | ux-spec §9.1（失败保留列表与选择）+ §5.3（草稿保留） | ✅ 一致（live-shell:§9.1 请求失败：保留列表与选择 \\| live-shell:查询失败保留用户草稿） |
| seq.doc.mode-memory | ["打开 Markdown 文件（默认预览）","切到原文","切到别的标签再切回 → 仍是原文","三模式往返后原文滚动与预览阅读位置保持","切模式不创建新标签、不写回文件"] |  | ux-spec §7.3（会话内记忆模式、各自滚动位置保持） | ✅ 一致（live-shell:§7.3 会话内记住文档模式、三段式切换不创建标签 \\| live-shell:§7.3 模式切换不丢失各自滚动位置） |
| seq.json.invalid-defaults-to-source | ["打开格式错误的 .json","默认显示原文而不是格式化结果","顶部错误条给出宿主算出的行列","『格式化』按钮保持位置但被禁用","点错误条（或 Enter）定位到出错行并把焦点交给正文"] |  | ux-spec §7.4（格式错误默认原文 + 错误行列 + 禁用格式化） | ✅ 一致（live-shell:§7.4 格式错误默认原文并禁用格式化、错误条给出宿主行列 \\| live-shell:§7.4 错误条 Enter 与单击同效 \\| live-shell:§7.4 点击错误条定位到宿主给出的出错行并把焦点交给正文） |
| seq.settings.save-failure | ["在设置里改一个字段","点应用/保存","写入失败 → 对话框保持打开并显示原因","用户输入保留，可重试"] |  | ux-spec §9.3 / §10.2 | ✅ 一致（live-shell:§9.3 保存失败时对话框保持打开 \\| live-shell:§9.3 保存失败时保留用户输入） |
| seq.terminal.large-output | ["终端里执行产生 ~4.8MB 输出的命令","输出持续滚动渲染直到结束","随后输入的命令仍能回显并产生输出"] |  | ux-spec §7.16（大段输出后仍可继续交互） | ⏳ 未核对 |

## 已知缺口（gaps）

- **settings-dirty-indicator**：未保存修改的分类标记（PyCharm 的实心圆点）｜证据：pycharm-settings-search-font.png（Appearance 行带蓝点）｜Augit：已补未保存标记（第 316 轮）；分组折叠仍不做｜计划：已裁决并实现：导航行/标题行加未保存标记（复用 --augit-blue），不做分组折叠
- **settings-terminal-font**：PyCharm 终端字体是独立设置（JetBrains Mono 13.0）｜证据：pycharm-settings-search-terminal.png｜Augit：按产品规格第 163 行跟随『等宽字体/字号』｜计划：既定选择，仅记录差异
- **settings-group-fold**：PyCharm 分类分组可折叠（▼/▶）｜证据：pycharm-settings-tree-2/3.png｜Augit：分组表头不可折叠｜计划：用户 2026-09-19 裁决：只补未保存标记，分组折叠不做（登记为差异）
- **settings-font-page-content**：Editor › Font / Console Font 页面内容未抓到（只抓到分类路径与 UI 字体事实）｜证据：artifacts/pycharm-interactions-16/：p16-settings-search-font.png（分类路径）、p16b-page-editor-font.png、p16d-*.png｜Augit：Augit 的『文件查看』页字段按 product-spec 与 PyCharm 结构对齐，不照搬编辑器字体页｜计划：补抓需要 UI 自动化（设置树滚动位置随会话变化，固定坐标点击会落到别的分类）；已在 §3.1 如实登记

## 阻塞（blockers）

- **pycharm-focus**：需要 PyCharm 处于前台才能做的交互抓取（打开设置、点击工具窗口、逐态截图）｜原因：Windows 前台锁拒绝了 SetForegroundWindow / AppActivate / 合成点击，脚本的硬校验（只有前台是 PyCharm 才发按键）因此主动中止 —— 这是有意的保护，避免按键落到用户其他窗口｜证据：FOREGROUND=Edge(pid 3340) → ABORT_NOT_FOREGROUND want=7688；连续 3 次标题栏合成点击后仍未激活｜解除：用户 2026-09-19 把 PyCharm 切到前台后本轮的抓取已完成（FOREGROUND_PID 7688 want=7688，31 张产物见 artifacts/pycharm-interactions-16/）；后续同类抓取仍需前台

## 对照配方（compareRecipe）

- **compare-recipe**：在同一 DPI 与同一逻辑尺寸下比较 PyCharm（Swing）与 Augit（WebView2）的关键区域几何与配色｜主题：Light（用户选 A：不改 PyCharm 主题）｜不覆盖：["dark 主题的 PyCharm 对照（需要改用户 IDE 主题，用户选 A 不做）","Windows 10 22H2"]

