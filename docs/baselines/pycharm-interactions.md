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
| window | {"rect":"-7,-7 1659x994","state":"maximized"} |
| project | D:\github\Augit |
| capturedAt | 2026-09-19 |
| uiFontOverride | {"enabled":true,"face":"Microsoft YaHei UI","size":12,"source":"options/other.xml: overrideLafFonts/fontFace/fontSize","note":"12.0 是 PyCharm 自己的单位（UI 里显示为 Size 12），不能直接等同于 Augit 的 13px；对照时必须按几何/视觉等价测，不得当成同值"} |
| editorFont | {"customized":false,"source":"options/editor-font.xml 只有 VERSION=1（未自定义，用主题内置默认）"} |
| consoleFont | {"customized":false,"source":"options/console-font.xml 只有 VERSION=1"} |
| terminalFont | {"customized":false,"source":"options/terminal-font.xml 只有 VERSION=1（SECONDARY_FONT_FAMILY 空）"} |
| ideScale | {"presentationModeIdeScale":1.75,"source":"options/other.xml"} |
| screen | {"device":"\\\\.\\DISPLAY1","boundsPhysical":"1646x1029","workingAreaPhysical":"1646x981","dpi":168,"scalePercent":175,"logicalArea":"941x588","evidence":"System.Windows.Forms.Screen (bounds) + GetDpiForWindow=168 + PyCharm 键名后缀 @168dpi"} |

## 已确认的裁决

- **spec_7_17_conflict**：按规格 §7.17 + PyCharm 结构落地（现有视觉稿把等宽字体/字号画在『外观』页是基线自身的错）
  - 确认人/时间：用户 2026-09-19 确认
  - 落地方案：`{"外观":["主题","界面字体","界面字号"],"文件查看":["正文字体","等宽字体","等宽字号","默认换行"],"Git":["git.exe 路径","检测结果","最低版本说明"],"终端":["Shell 类型","自定义启动命令"],"导航":"点击分类真正切页，右页只显示当前分类；切换保留未保存编辑；搜索框按分类过滤"}`
- **compare_theme**：A —— 直接在 PyCharm 当前 Light 主题下对照，不切换其 IDE 主题
  - 确认人/时间：用户 2026-09-19 选择 A；备注：Augit 侧用 --theme light；dark 主题的 PyCharm 对照本轮不做（用户不改 IDE 偏好）

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
- **note**：`编辑器字体在 PyCharm 属于另一个顶层分类 Editor，而不是外观页；这与 ux-spec §7.17 把等宽字体放在『文件查看』一致`

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
| settings.field.disabled | 不可用字段（Theme 在 Sync with OS 勾选时） | "文字转灰、下拉不可展开" | {"status":"diff","note":"Shell 非『自定义命令』时禁用自定义启动命令输入框（同一手法）"} | live-shell:§7.17 终端自定义命令仅在选择『自定义命令』时可编辑 |
| settings.primary-button | 主按钮（OK） | {"bg":"#3871e1","note":"实测取色 (1180,1150)"} | {"token":"--augit-blue: #3871e1","note":"逐值相同"} |  |
| settings.surfaces | 对话框/树/底栏 底色 | {"bg":"#f7f8f9","searchFieldBg":"#ffffff","lowerAreaBg":"#d9dbdb"} | {"chrome":"#e9eaee","panel":"#ffffff","panelMuted":"#f5f8fe"} | 不同：PyCharm 面板灰 #f7f8f9，Augit 按 design-system 用冷灰标题栏 #e9eaee + 白色内容面板 #ffffff（ux-spec §4.4 明文规定）。属规范内既定选择，不是缺陷；⑦ 对照表如实列出。 |
| settings.tree.selected | 分类树选中行 | {"bg":"#d0dffe","note":"整行浅蓝底（实测取色，原图坐标 (250,690)）"} | {"token":"--augit-blue-soft: #d0dffe","note":"浅色令牌与 PyCharm 实测逐值相同"} | live-shell:§7.17 默认停在『外观』…（选中行 selected 类） |

## 已知缺口（gaps）

- **settings-dirty-indicator**：未保存修改的分类标记（PyCharm 的实心圆点）｜证据：pycharm-settings-search-font.png（Appearance 行带蓝点）｜Augit：无此指示；草稿会保留但用户看不到哪里改了｜计划：候选：在导航行加未保存标记（需先定设计令牌，交用户确认）
- **settings-terminal-font**：PyCharm 终端字体是独立设置（JetBrains Mono 13.0）｜证据：pycharm-settings-search-terminal.png｜Augit：按产品规格第 163 行跟随『等宽字体/字号』｜计划：既定选择，仅记录差异
- **settings-group-fold**：PyCharm 分类分组可折叠（▼/▶）｜证据：pycharm-settings-tree-2/3.png｜Augit：分组表头不可折叠｜计划：已在三方覆盖清单登记

## 阻塞（blockers）

- **pycharm-focus**：需要 PyCharm 处于前台才能做的交互抓取（打开设置、点击工具窗口、逐态截图）｜原因：Windows 前台锁拒绝了 SetForegroundWindow / AppActivate / 合成点击，脚本的硬校验（只有前台是 PyCharm 才发按键）因此主动中止 —— 这是有意的保护，避免按键落到用户其他窗口｜证据：FOREGROUND=Edge(pid 3340) → ABORT_NOT_FOREGROUND want=7688；连续 3 次标题栏合成点击后仍未激活｜解除：用户在方便时点一下 PyCharm 使其成为前台（或用任务栏激活），随后即可继续抓取；不需要改任何 PyCharm 设置

## 对照配方（compareRecipe）

- **compare-recipe**：在同一 DPI 与同一逻辑尺寸下比较 PyCharm（Swing）与 Augit（WebView2）的关键区域几何与配色｜主题：Light（用户选 A：不改 PyCharm 主题）｜不覆盖：["dark 主题的 PyCharm 对照（需要改用户 IDE 主题，用户选 A 不做）","Windows 10 22H2"]

