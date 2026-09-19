# 三方覆盖清单（PyCharm × 视觉稿 × 规格）

本文件是目标 ⑬ 的产出：把 **PyCharm 实际界面**、**48 个视觉稿页面**、**`ux-spec`/`design-system` 条文**
三方逐项对齐，列出差集与冲突。**只写已核对的事实**，每条带证据；未采集的写进"待采集"，不猜。

- 机器可读的操作逻辑基线：`docs/baselines/pycharm-interactions.json`（schema `augit.interaction-baseline/1`）
- PyCharm 环境（实测）：2026.2.1 Professional，`D:\JetBrains\PyCharm 2026.2.1`，
  配置目录 `%APPDATA%\JetBrains\PyCharm2026.2`，主题 **Islands Light**，
  UI 字体覆盖 **Microsoft YaHei UI / 12.0**（`options/other.xml`），
  编辑器·控制台·终端字体**均未自定义**（三个 font xml 只有 `VERSION=1`），
  屏幕 **1646×1029 物理 @ DPI 168（175%）= 逻辑 941×588**，项目即本仓库。
- 证据图：`artifacts/pycharm-baseline-20260919/`（主窗口 + Settings 对话框）。

## 1. 设置窗口（规格 §7.17）

| 项 | 规格 | 现有视觉稿 | PyCharm 实测 | 差集 / 结论 |
| --- | --- | --- | --- | --- |
| 窗口形态 | 模态对话框，左搜索+分类，右当前分类 | 模态对话框 ✓ | 模态 `SunAwtDialog`（标题 `Settings – <项目>`），左搜索框+分类树、右当前分类页（面包屑） | **一致** |
| 导航行为 | （规格只说"右侧为当前分类"） | **零绑定**：所有分类堆在一页，点击无反应 | 点树节点切换右页；选中行整行浅蓝底；树可折叠 | **视觉稿缺"跳转"这一操作逻辑** → 需要补（③） |
| 分类名 | 外观与行为、文件查看、Git、终端 | 导航写了这 5 条，但只画了"外观"的字段 | 顶层含 Python / Jupyter / Project Structure / Appearance & Behavior / System Settings / File Colors / Scopes / Notifications / Data Editor and Viewer / Quick Lists / Required Plugins… | 视觉稿的分类**只有名字没有页面**；Augit 只需 §7.17 的四类，但四类都要有内容 |
| 主题 | 外观：跟随 Windows/浅色/深色，立即预览、取消恢复 | 有下拉 ✓ | `Appearance & Behavior › Appearance` 的 Theme 下拉（当前 Islands Light） | 一致（Augit 多一个"跟随 Windows"） |
| 界面字体 | 规格把"正文字体/等宽字体/字号"归到**文件查看** | 把 界面字体+等宽字体+两个字号**都画在"外观"** | **UI 字体在 `Appearance`（Accessibility 的 Use custom font）**；编辑器字体在另一个顶层分类 | **视觉稿与规格+PyCharm 不一致（已裁决：按规格+PyCharm 改）** |
| 文件查看 | 正文字体、等宽字体、字号、**默认换行** | **该分类无任何内容** | 对应 PyCharm 的 `Editor › Font`（本轮**待采集**） | 视觉稿缺页 → 需要补 |
| Git | `git.exe` 路径、**检测结果**、**最低版本说明** | 只有路径输入框 | PyCharm 无对应页（Augit 专有，按产品规格） | 视觉稿缺两项 |
| 终端 | Shell 类型、**自定义启动命令** | 只有 Shell 下拉（有"自定义命令"选项但**没有命令输入框**） | PyCharm 终端设置另有 shell path 等（待采集） | 视觉稿缺字段 |
| 不该出现 | 不显示插件/市场/键位/解释器/构建/调试/AI | 未出现 ✓ | PyCharm 有这些（Augit 不做） | 一致（Augit 范围更窄） |

**落地方案（用户 2026-09-19 确认）**：
`外观` = 主题 + 界面字体 + 界面字号；`文件查看` = 正文字体 + 等宽字体 + 等宽字号 + 默认换行；
`Git` = git.exe + 检测结果 + 最低版本说明；`终端` = Shell 类型 + 自定义启动命令；
导航点击真正切页（右页只显示当前分类）、切页保留未保存编辑、搜索框按分类过滤。

**实施状态（本阶段）**：

| 项 | 状态 | 证据 |
| --- | --- | --- |
| 四分类分页 + 导航真正切页 | **已实施** | `mockup.js` 的 `settingsLayoutHtml/settingsNavHtml/settingsPageHtml`（静态稿与实时外壳共用同一份结构）；`live-data.js` 的 `switchSettingsPage/renderSettingsDialog/bindSettingsPages` |
| 切页保留未保存编辑 | **已实施** | `collectSettingsDraft()` 在切页前收草稿，`saveSettings()` 先合并草稿再收当前页 DOM；数字字段按 number 存（宿主 `GetDouble` 只接受 JSON number，字符串会被静默忽略） |
| 搜索框按分类名过滤 | **已实施** | `bindSettingsPages()` 的 input 委托：按分类名隐藏不匹配行 |
| Git 检测结果 + 最低版本说明 | **已实施** | 桥接新增 `git/detect`（读缓存的 `GitRuntimeInfo`：路径/版本/最低 2.40/原因）；Git 页显示"已找到：<路径>（<版本>）"或失败原因 |
| 终端自定义启动命令 | **已实施** | 宿主 `TerminalCustomCommand` 早已存在、界面此前没有输入框；现按 Shell 选择启用/禁用并随保存写入 |
| 文件查看：等宽字体/字号 | **已实施** | 从"外观"页迁到"文件查看"页 |
| 文件查看：**默认换行** | **待裁决（规范冲突）** | `ux-spec` §7.17 要求该设置；`product-spec` 只把"自动换行"列为正文查看能力（第 45 行），**没有定义持久化的默认换行设置**，宿主 `ApplicationSettings` 里也没有对应字段。按纪律须由用户裁决：加字段（改产品规格+宿主+界面）／或从设置页去掉该条 |
| 真机证据 | **已留档** | `artifacts/settings-pages-20260919/`（外观/文件查看/Git/终端 四页，dark，1180×760）；Git 页实测显示 `C:\Program Files\Git\cmd\git.exe（2.45.1）` |
| 分组折叠（PyCharm 的 ▼ 箭头） | **未做（已知差异）** | 我们的导航把"外观与行为"作为不可点的分组表头，不折叠；PyCharm 是可折叠树节点 |
| 最近目录条目 | **已从设置页移除** | 规格四分类里没有该分类；最近目录在「打开工作区」页（`recentWorkspaces`） |

## 2. 主窗口与工具窗口

| 项 | 规格 | 现有视觉稿 | PyCharm 实测 | 差集 / 结论 |
| --- | --- | --- | --- | --- |
| 工具窗口入口 | §5.1 左侧固定顺序 项目/提交/搜索 + 终端/Git 历史 | 有 rail ✓ | 左竖排工具窗口按钮条（Project 等），带选中高亮 | 需要采集"点击已激活入口是否折叠""各按钮悬停/选中色"（**待采集**） |
| 标题栏右侧入口 | §5.1 有搜索与设置入口 | 有（放大镜/齿轮） | 新版 UI 的标题栏含主工具栏、搜索、设置、运行控件 | 本阶段已修好 Augit 侧"点了没反应"（见 `ui-refactor-baseline.md` 第 305 轮） |

## 3. 待采集（必须先让 PyCharm 成为前台）

> 阻塞原因：Windows 前台锁拒绝了脚本抢焦点，脚本的硬校验主动中止（避免按键落到其他窗口）。
> 用户在方便时点一下 PyCharm 即可继续；**不改任何 PyCharm 设置**（对照主题固定 Light）。

1. `Editor › Font` 页（编辑器/等宽字体、字号、行距）+ `Editor › Color Scheme › Console Fonts`；
2. 设置分类树的下半部分（滚动到底，抄全清单）；
3. 跳转关系：点工具窗口按钮 → 哪个面板开/关/替换、点击已激活入口是否折叠、关闭叉/隐藏的行为；
4. 点击反馈：工具窗口按钮、标签、树行在 悬停/按下/选中/焦点/禁用 各态的取值（前后对照截图）；
5. 加载/失败/进行中/空态 在 PyCharm 里的样子（例如 VCS 操作进行中的进度条与取消入口）。
