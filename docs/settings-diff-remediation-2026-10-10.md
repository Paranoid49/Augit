# 设置、Diff、日历与普通菜单整改（2026-10-10）

本轮处理最新八项审查，保留进入任务时所有混合未提交改动。分支 `dsh`、提交 `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`；权威IntelliJ源码提交 `576e32820af82f97529d70f9269726803a27c016`。按开发验证流程读取产品、架构、UX、设计系统、07/08模块、backlog、参考索引、工作区状态与权威实现。

## 逐项处理与产品边界

| 审查项 | 当前实现 |
| --- | --- |
| 设置标题过大 | 取消大号h2，正文抬头使用1rem半粗路径面包屑和前后导航，参考 `SettingsEditor.myBreadcrumbs`、Back/Forward动作；只保留Appearance、File View、Git、Terminal四个产品分类。 |
| 字体手输 | UI字体与等宽字体使用选择下拉框；首次打开设置按需调用 `settings/fonts`，通过Win32 `EnumFontFamiliesExW`读取本机字体族，过滤竖排别名、去重排序。名称列表缓存，不反复枚举；设备上下文在finally释放。旧配置中的名称继续保留，避免切页静默改变配置。 |
| 导航选区 | 移除分类行左右外边距与圆角，色带横跨导航栏客户区。搜索框保留自身10px左右内距，字号与草稿规则保持。 |
| Diff齿轮排列 | 将失效中文aria选择器改为当前英文Settings，齿轮位于单双栏切换之后；DOM与视觉/Tab顺序一致。 |
| Diff局部设置 | 齿轮打开局部菜单，只保留产品已有的Show Whitespaces和Show Line Numbers；不跳转全局设置，不出现代码高亮、编辑、缩进设置或外部Diff等额外能力。显示行号切换保留行号槽/连接区几何；空白符只加显示层，复制文本不变。 |
| 日历弹层 | 用应用内Microba经典结构等价面板代替showPicker：年份/月导航、周一至周日、42格、选中日期、today/none；箭头键移动、PageUp/PageDown换月、Enter选中、Esc只关闭日历并回到按钮。日期字段使用可校验的本地年月日文本，非法日期不静默进位。 |
| 菜单边界 | 所有指针菜单插入DOM后测真实宽高，以宿主客户区夹边，最大高度受可用空间限制；删除固定240px预算。大字号在右下角打开也完整留在客户区内。 |
| 普通菜单键盘 | Project、Changes、日志和引用菜单共用Down/Up/Home/End/Enter导航；跳过隐藏、禁用和分隔项，选中动作消费一次Enter，Esc恢复原焦点。历史分支speed-search继续保留自身处理。 |

字体枚举新增一个只读桥接方法，不新增运行时依赖、不引入WPF/WinForms，也不访问或保存用户文档。仅缓存约300个名称；真实本机结果为300个字体族，成本为一次打开设置时的本地枚举，不影响首屏启动。Diff的显示行号属于已有行号能力的显示开关，数据不写磁盘、不重查Git。

参考路径：`AppearanceConfigurable.kt:350`使用 `FontComboBox`；`platform/platform-impl/src/com/intellij/openapi/options/newEditor/SettingsEditor.java`使用 `Breadcrumbs`及Back/Forward；日历来自 `libraries/microba/src/com/michaelbaranov/microba/calendar/ui/basic/` 中ClassicCalendarPanel、CalendarGridPanel和AuxPanel。日期面板结构从当前PyCharm前台取证，Augit界面继续使用英文月份和星期。

## 文件与哈希

实现：`web/src/mockup.js`、`mockup.css`、`live-data.js`、`src/Augit.Shell/ShellBridge.cs`、新增 `ShellFontCatalog.cs`；`mockup.js/css`同步视觉稿。自动化：新增 `ShellFontCatalogTests.cs`、`tools/audit/live-settings-diff.cjs`、`live-settings-shell.cjs`，扩展 `live-shell.spec.cjs`。现行UX、设计系统及架构同步结构和只读桥接边界。

| 文件 | 开始MD5 | 最终MD5 |
| --- | --- | --- |
| `mockup.js` | `E9F14D7ABB43C64D5EE3C32BA092C3D1` | `E29B5BBDE9944607021F67DFA96D625D` |
| `mockup.css` | `2CA8E4966B16B48FC0C1CBFA08247CDA` | `142ED4F08297FB52941FEB8FB402AA77` |
| `live-data.js` | `9E0343E52D28CA4A482D6A1D8062B8DA` | `0AA4C31259978146866AF9E919024ED3` |
| `bridge.js` | `16E41439BB7F4C2A36C2E95A4375A823` | `16E41439BB7F4C2A36C2E95A4375A823` |

MD5取自本批次真实工作区，历史批次的哈希不冒充当前状态。

## 验证与取证

| 验证 | 结果 |
| --- | --- |
| 设置/Diff/日历/普通菜单 | `live-shell.spec.cjs … --settings-diff-only`，10项通过、最终6.60秒；浅深两主题的导航草稿、字体Apply、视觉/Tab顺序、闰日与日期校验、日历按钮Enter与菜单真实动作、右下角40px菜单及跳过禁用项 |
| 设置相邻回归 | `--restoration-only`，23项通过、4.51秒 |
| 日期/路径筛选 | `--filter-popups-only`，19项通过、11.49秒 |
| 主布局完整矩阵 | `--layout-only`，891项通过、44.33秒；共享设置和菜单布局完成后触发 |
| 字体本机单测 | `dotnet test … --filter FullyQualifiedName~ShellFontCatalogTests`，1项通过、46ms |
| Shell模块 | `dotnet test tests/Augit.Shell.Tests --no-build --no-restore -c Release`，124项通过、14秒；新增桥接方法触发 |
| 格式 | `dotnet format Augit.slnx --no-restore --verify-no-changes --include …`，本轮C#文件通过 |
| 最终构建 | `dotnet build Augit.slnx --no-restore --disable-build-servers -c Release`，0警告、0错误、4.68秒 |
| 静态页面 | `mockup-scenes.spec.cjs … light/dark`，浅深各55/55通过，30.64/30.74秒 |

字体枚举首次编译遇到结构体字段CharSet遮蔽枚举名，修正字段名后通过；新测试按仓库分析器改成中文无下划线方法名与 `Assert.IsNotEmpty`。这些失败尝试不记录为通过。未再次跑全量业务套件，已有2373项历史记录仍为历史证据。

附件目录 `artifacts/settings-diff-remediation-2026-10-10/`：PyCharm设置与Microba日历截图；Augit设置、Diff局部菜单、日历截图及 `augit-settings-scenes.json`。真实外壳Microsoft YaHei UI 12px、DPR1.75、300个字体族；标题h2数量0、字体控件SELECT、导航选区无圆角；Diff菜单两项；日历42格，脚本错误为空。截图复核发现日历定位误用了display:contents覆盖层边界，已改为主窗口客户区，并补充真实日历贴日期字段的偏移断言。

本轮确认八项审查对应的结构和流程已经整改；同字体栅格、复杂提交图、全部Git状态色、滚动条动态、17个历史页面、深色全页面及Windows10实机仍未完成，不宣称100%复原。

## 清理与复验命令

测试启动的HTTP服务和浏览器在finally关闭；隔离实机使用端口9350及 `.tmp/settings-webview-20261010`，取证后经 `window/close` 正常关闭，确认本实例所属WebView2退出后删除隔离目录。正式截图/JSON保留作验收附件；用户PyCharm设置、日历和日期草稿均取消，原窗口/主题保留。

本轮设置/Diff原生实例PID22608及复核PID22044均正常退出；最终核查相关进程0、9350监听0、临时目录不存在。没有暂存或提交进入任务前的混合改动。

```powershell
$env:AUGIT_BROWSER_EXECUTABLE = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
node tools/audit/live-shell.spec.cjs 'C:/Users/wl024/AppData/Local/OpenAI/Codex/runtimes/cua_node/2c9e75c4e9c71beb/bin/node_modules/playwright' --settings-diff-only

$augitSettings = Start-Process -FilePath 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit --web-root D:/github/Augit/web --no-session-restore' -PassThru
$augitSettings.Refresh()
if (-not $augitSettings.HasExited) {
    [void]$augitSettings.CloseMainWindow()
    if (-not $augitSettings.WaitForExit(5000)) { taskkill /PID $augitSettings.Id /T /F }
}
```
