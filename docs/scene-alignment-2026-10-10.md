# 实际场景选图、密度与编辑器状态整改（2026-10-10）

本轮按最新审查先修 Changes 密度、文件类型、分支图标与标签状态，再处理目录可见性、搜索定位、Commit 组织、日志引用和术语。项目树已经是24px，不按此前约30px的错误估测继续缩小。进入任务前的混合未提交改动保留，不整体暂存或提交。

## 基线与依据

- 工作区：`D:/github/Augit`，分支 `dsh`，提交 `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`。
- 权威仓库：`D:/github/intellij-community`，提交 `576e32820af82f97529d70f9269726803a27c016`。
- 本机前台参考：PyCharm 2026.2.1、Islands Light；Windows 11 x64，175% DPI。Augit 隔离外壳实际生效12px、DPR 1.75。
- 修改前已依 `development-validation.md` §1 读取产品、架构、UX、设计系统、03/12/22等受影响模块、backlog、索引、工作区差异和权威调用链。沿用上一批次用户裁决，不重复请求确认。
- 本轮没有新增依赖、字体文件或外部图标包。图形使用项目内联绘制和既有CSS令牌，核对实际 FileType→图标映射→主题资源调用链，避免仅验证某个SVG却选错使用场景。

| 权威路径 | 本轮用于确认 |
| --- | --- |
| `platform/icons/src/PlatformIconMappings.json`、`platform/icons/src/expui/fileTypes/` | JS、TS、CSS、EditorConfig与忽略文件实际映射及主题图形 |
| `plugins/editorconfig/common/src/plugin/EditorConfigFileType.kt` | `.editorconfig` 使用 `AllIcons.Nodes.Editorconfig` |
| `platform/vcs-impl/src/com/intellij/openapi/vcs/changes/ignore/lang/IgnoreLanguage.java` | `.gitignore` 使用 `AllIcons.Vcs.Ignore_file`，映射为 `fileTypes/ignored.svg` |
| `platform/icons/src/language/python.svg` | Python双色图形 |
| `plugins/git4idea/shared/src/com/intellij/vcs/git/ui/GitBranchesTreeIconProvider.kt` | 普通分支、当前、收藏与组合状态的图形选择 |
| `platform/platform-impl/src/com/intellij/openapi/application/impl/islands/IslandsTabPainter.kt` | 编辑器活动范围及圆角悬停卡片 |
| `platform/vcs-log/impl/src/com/intellij/vcs/log/ui/render/TagPainter.kt`、`plugins/git4idea/shared/resources/messages/GitBundle.properties` 的 `git.new.tag.dialog.*` | 引用色标叠层及新建标签的实际动作文案 |

## 逐项处理

| 审查项 | 当前处理与验证范围 |
| --- | --- |
| Changes偏松 | 12px基线分组/文件行统一24px；随字号按 `max(round(24s), h)` 扩展，`s`为以12px计算的四分之一档比例，`h`为实际字形高度。真实外壳抽样12行均24px，浏览器两主题和40px下限通过。 |
| 常见文件类型通用图 | 补JS/mjs/cjs/jsx、TS/mts/cts/tsx、CSS及Python/pyw/pyi映射；JS黄色徽标、TS蓝色徽标、CSS蓝盾、Python蓝黄双色。Project、Changes、搜索、真实打开后的标签均检查实际图形类；Git状态只染文件名。 |
| `.editorconfig`选图错误 | 改为专用齿轮；实际文件名触发正确图形，浅深颜色与栅格对照通过。 |
| `.gitignore`选图错误 | 改为灰色斜线圆，不再使用Git菱形。根据实际忽略文件类型调用链取证，四类使用场景同时覆盖。 |
| 分支树画成标签 | 普通分支使用分支节点，当前分支使用黄色空心标签，收藏使用星形，当前且收藏使用黄色实心标签，Git标签使用灰色空心标签；普通远端不再使用紫色标签。浅深主题均覆盖。 |
| 活动标签范围错误 | 活动域使用 `.editor-area:focus-within`，正文聚焦继续显示活动卡片；项目树或设置输入聚焦后使用失焦色。 |
| 未选中标签缺悬停卡片 | 未选中项和失焦选中项按相同几何绘制圆角卡片；浅 `#00000012`、深 `#FFFFFF17`。活动选中卡片hover保持活动底色；测试实际指针悬停、正文/树/模态交错。 |
| 项目树额外过滤目录 | 普通项目树只隐藏 `.git`；`.tmp`、`.workbuddy`、`artifacts`、`obj`、`TestResults`、`node_modules`均正常可见。仍按需读取单层，尊重不可展开及重解析点边界。 |
| 每目录200项截断 | 删除初次读取、恢复与刷新路径中的截断。257项受控目录展开及F5都完整显示，沿用现有每帧60行的补丁机制。 |
| 全仓搜索不定位 | 双击/Enter打开正式标签后按命中行定位、突出当前行并居中滚动；同文件不同命中重新定位且复用标签。在途打开受代次保护；Markdown/JSON先切磁盘原文，保留搜索浮层与焦点。 |
| Commit输入区组织 | 使用 `Amend last commit`及相邻中性历史图标，保留 `Last Commit`悬停说明与打开日志命令。数量只在Changes/Unversioned分组显示；底部兼容DOM槽隐藏。12/20/26/40px验证不重叠，重复历史入口点击保留选中提交。 |
| 日志引用合成一枚图标 | 每个引用独立保留名称与色标，HEAD与首个分支用多层色标表达；实际外壳从 `history.head`推导HEAD，覆盖宿主不返回字面HEAD的情况。多个引用的宽度测量同时更新。 |
| 新建标签自行翻译 | 实际日志动作使用 `Create New Tag On …`、`Enter the name of new tag`，验证真实右键动作打开的标题与字段。 |

分支收藏遵循 `GitBranchManager` 的默认名称：本地 `main/master`、远端 `origin/main/master`；显式 `isFavorite` 优先，排序为当前→收藏→原分组规则。本轮没有新增收藏切换能力或读取PyCharm私人收藏设置，不能把上述默认状态支持写成完整私人偏好同步。

## 修改文件与哈希

实现涉及 `web/src/mockup.js`、`web/src/mockup.css`、`web/src/live-data.js`；`mockup.js/css`同步到视觉稿。新增 `tools/audit/live-scene-alignment.cjs` 与 `live-scene-shell.cjs`；扩展 `live-icons.cjs` 和 `live-shell.spec.cjs`。设计系统与03/12/22模块同步当前结论。

| 运行时文件 | 开始MD5 | 最终MD5 |
| --- | --- | --- |
| `mockup.js` | `7B1F93FF60351E6F28ED3346447B26D7` | `6B88E2019C516472F88CD6F81828A95C` |
| `mockup.css` | `10B348C173B942272C4CEE10D80AAF02` | `B34402B37FA8B16A968F21AE38075DF9` |
| `live-data.js` | `96CF8937546C57324A97B18337D45F77` | `BBD796D4EAD6DDB861B95D1EC68C87C5` |
| `bridge.js` | `16E41439BB7F4C2A36C2E95A4375A823` | `16E41439BB7F4C2A36C2E95A4375A823` |

共享的 `current-find.js`、`image-preview.js`本批次未改，最终资源校验确认四个共享文件全部与视觉稿字节一致。UTF-8/LF检查覆盖本批次15个新增/触及文件，不覆盖已有逻辑。

## 自动化验证

本轮增加场景归属回归，检查“实际文件名/分支模型选用了什么图形”和焦点、悬停后的计算样式，补足原测试只核对图形、数值的盲区。旧实时Commit布局断言也按当前数量位置改写，保留动作命令、图标可见、字号扩展与不重叠检查。

以下命令的Playwright路径使用本机可解析路径；浏览器统一设置 `$env:AUGIT_BROWSER_EXECUTABLE='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'`。

| 范围 | 命令 | 结果与耗时 |
| --- | --- | --- |
| 场景选图与状态 | `node tools/audit/live-shell.spec.cjs <Playwright路径> --scene-alignment-only` | 30项通过，14.12秒；含Markdown/JSON原文定位和多字号Commit历史入口 |
| 图形与颜色 | `node tools/audit/live-shell.spec.cjs <Playwright路径> --icons-only` | 130项通过；差异阈值仍为32/16384，未放宽。JS30、TS29、EditorConfig14，其余新增图形更低；初验耗时未留存 |
| 主布局完整矩阵 | `node tools/audit/live-shell.spec.cjs <Playwright路径> --layout-only` | 891项通过，约20秒；卡片焦点范围和Changes几何改变触发本模块完整验证 |
| 相邻审查回归 | 同脚本 `--review-only` / `--reference-only` / `--restoration-only` | 分别16/10/23项通过；初验耗时未留存 |
| 静态场景 | `node tools/audit/mockup-scenes.spec.cjs <Playwright路径> light`及 `dark` | 浅深各55/55通过，分别31.00/31.35秒 |
| 历史字号布局 | `node tools/verify-ux-history-typography.cjs <Playwright路径> <输出目录>` | 48组通过；一次性截图目录已删除，初验耗时未留存 |
| 构建 | `dotnet build Augit.slnx --no-restore --disable-build-servers -c Release` | 0警告、0错误，3.10秒；本批次没有C#逻辑变化 |

最终静态校验通过：四共享资源字节一致；7个JavaScript文件语法通过；本批次15个新增/触及文件均为UTF-8/LF；14个PowerShell脚本编码检查通过；文档声明 `DOC_CLAIMS_OK`；`git diff --check`无空白错误。资源/编码/文档声明/空白检查合计3.18秒。本轮没有再次运行全部实时业务测试，不把历史全量2373项通过记录替换为当前结论。

## 实机证据与未完成验收

附件位于 `artifacts/scene-alignment-2026-10-10/`：

- `pycharm-commit.png`：当前参考Commit表面。
- `augit-project.png`、`augit-commit.png`、`augit-commit-native.png`：隔离外壳对应表面。
- `augit-scenes.json`：实际字体/DPR、图标归属、目录、Changes行高、正文聚焦活动色、分支状态、日志引用与脚本错误集合。

JSON确认JS标签、EditorConfig/忽略文件选图，旧过滤目录可见，Changes12行均24px，正文焦点活动卡片为 `rgb(227,235,254)`；本地dsh当前、main默认收藏、origin/main默认收藏，日志dsh色标为 `head local`，origin/dsh为remote，错误集合为空。截图中的临时WebView2目录和一次性脚本属于取证时存在的工作区项目，清理后不再存在。

PyCharm为1640×985，Augit附件为1626×981；这些附件用于实机场景与调用归属确认，不作为同尺寸逐像素终验。取证后PyCharm已恢复Project表面、原普通窗口尺寸与浅色主题。

尚未完成：同字体同文本的字形、基线、字宽与抗锯齿；各Git文件状态的真实同场景颜色；复杂提交图与引用组合/省略的完整像素验证；滚动条动画、自动隐藏和拖动手感；17个历史页面完整PyCharm正文对照及深色全页面对照；Windows 10 22H2实机。不能宣称100%复原。

## 资源清理与手动命令

本轮实机进程PID26236，诊断端口9348，隔离目录 `.tmp/scene-webview-20261010`。已通过主窗口正常关闭，删除隔离目录、`build-scene-icons.cjs`、`update-commit-scene.cjs`、`update-scene-docs.cjs`和 `scene-history-matrix`。正式截图/JSON作为验收附件保留。浏览器测试通过 `finally`关闭本次浏览器和临时HTTP服务；最终核查确认PID26236、其WebView2、9348监听、本次测试Node/Playwright浏览器均无残留，五个临时路径全部不存在。保留用户原PyCharm与Edge。

手动启动/停止（同一PowerShell会话）：

```powershell
$augitScene = Start-Process -FilePath 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit --web-root D:/github/Augit/web --no-session-restore' -PassThru

$augitScene.Refresh()
if (-not $augitScene.HasExited) {
    [void]$augitScene.CloseMainWindow()
    if (-not $augitScene.WaitForExit(5000)) { taskkill /PID $augitScene.Id /T /F }
}
```

浏览器回归可直接运行表中脚本，测试结束自动停止服务器。若手动中断，先核对本次测试PID，再执行 `taskkill /PID <本次测试PID> /T /F`，不得按名称结束用户的其它浏览器或Node进程。
