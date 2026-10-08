# PyCharm 还原差异清单（2026-10-08）

本次是只读审计，不修改产品实现或用户字体设置。结论：当前不能称为 100% 还原。最明显的问题是界面语言、实际生效字体，以及组件密度和尺寸推导。历史“待办清零”包含有意差异、无法取证等结案，不能理解为视觉一致。

## 取证范围

- 参考：已打开的 PyCharm 2026.2.1，项目 Augit；实际查看主窗口、Commit、Git Log 和 Settings，随后关闭本次打开的 Settings。
- 本地源码：`D:/github/intellij-community`，提交 `576e32820af82f97529d70f9269726803a27c016`，与登记的参考版本一致。
- Augit：分支 `dsh`，提交 `8b3a61f7d91d74df2152f66badd5ccc58d81263d`，检查的是当前工作区，包含已有未提交改动。未覆盖这些改动。
- 启动已有 Debug 外壳并明确加载当前 `web/`，查看项目树、提交、日志和设置。外壳文件时间为 2026-10-07；本次没有重新构建，因此不以截图证明最新 C# 改动已完成验证。
- PyCharm 外观配置：`%APPDATA%/JetBrains/PyCharm2026.2/options/other.xml` 的 `NotRoamableUiSettings` 为 Microsoft YaHei UI、12.0、`overrideLafFonts=true`；`laf.xml` 为 Islands Light。
- Augit 当前 `%LOCALAPPDATA%/Augit/settings.json` 为 Segoe UI、`fontSize=14`、无独立界面字号；真实设置页也显示 Segoe UI / 14。
- 本次截图用于定性观察，没有进行统一物理截图尺度的逐像素测量；不把工具显示图片的宽高当成 DPI 校准证据，也不把 PyCharm 的 `presentationModeIdeScale` 当成当前 Windows 缩放。

## 已确认差异与优先级

P1 表示直接影响整体相似度或造成可见布局问题；P2 表示局部视觉、状态或交互差异。部分条目是当前规格保留的差异，列出不表示已授权改变产品边界。

| 编号 | 优先级 | 未还原项 | 当前证据与影响 | 建议 |
| --- | --- | --- | --- | --- |
| 1 | P1 | 界面语言不一致，且中英混排 | PyCharm 为 Project / Commit / Log / Settings；Augit 为项目 / 提交 / 日志 / 设置，同时保留 Changes、Unversioned Files、Amend、modified。设置说明、弹窗、占位符及提示大量为中文。`web/src/mockup.js:2133`、`:4031`、`:4394`、`:5326` 等可直接核对。 | 如果以当前英文 PyCharm 为目标，统一英文界面及术语；文档和代码注释继续中文。不能只翻译几个标题。 |
| 2 | P1 | 本机实际界面字号仍为 14，不是代码默认的 12 | 名义字号相对 PyCharm 12 大约大 16.7%。`ApplicationSettings.cs:42` 的 `UiFontSize => TextFontSize ?? FontSize`、`SettingsStore.cs:43` 的旧配置回退，以及 `ShellBridge.cs:1242` 共同解释了原因。 | 校准当前生效配置，并为旧配置提供明确的“恢复参考外观”方式；不能反复只改默认值，也不能静默覆盖用户偏好。 |
| 3 | P1 | 实际字体不一致 | Augit 是 Segoe UI，中文部分需要字体回退；PyCharm 显式指定 Microsoft YaHei UI。字宽、基线、字重与字体高度都可能不同。真实设置页与配置文件互相印证。 | 先使用同字体、同字号、同主题、同显示缩放，再验收几何；不能只比较字号数字。 |
| 4 | P1 | 密度计算仍是 Augit 自定公式 | `mockup.js:64` 起按 `国Ag` 的字体包围盒推导：树行 `max(24,h+8)`、历史行 `max(26,h+6)`、状态栏 `max(28,h+12)` 等。PyCharm 的 `LafManagerImpl.kt:855` 起先计算字体与用户缩放，再由组件路径取尺寸；二者不等价。 | 字体校准后逐组件测量行距、标题、工具栏和留白。不能把所有尺寸统一乘一个缩小比例。 |
| 5 | P1 | 左侧工具轨尺寸、步长、图标及圆角不匹配 | Augit `.rail-button` 为 32×32、无额外步长、半径 3，通用图标 16；参考 `ManyIslandsLight.theme.json:1160` 为按钮容器 37×40，并有绘制内距；`JBUI.java:1337` 默认轨道图标 20，`:1362` 为 arc 12（普通模式半径 6）；`SquareStripeButtonLook.kt:142` 明确覆盖通用按钮圆角。当前界面也能看到按钮排列更挤。 | 区分容器、命中区、背景绘制区和图标尺寸，按轨道专用组件还原。现有代码引用通用 `IdeaActionButtonLook` 推导半径的注释不适用于此处。 |
| 6 | P1 | 设置页当前就出现横向溢出 | 真实 Segoe UI / 14 下，右侧说明被裁切，底部出现横向滚动条。`.settings-layout` 固定左列 245，`.settings-page` 内距 22/28，见 `mockup.css:4014` 起。 | 表单和说明按剩余宽度换行；在 12px 和已有 14px 配置下都应可用，不能靠强行缩字掩盖溢出。 |
| 7 | P1 | 设置页按钮顺序与层级不同 | 当前 PyCharm 为 OK / Cancel / Apply；Augit 为取消 / 应用 / 确定。两边真实窗口均已观察；`mockup.js:5326`、`live-data.js:9455` 附近明确写死 Augit 顺序。 | 按 Windows 上参考 Settings 的动作顺序、宽度和焦点规则对齐，不套用统一“主要动作最右”模板。 |
| 8 | P1 | 多类对话框按钮存在偏大的尺寸下限 | Push、Clone、Stash、Reset 等多处使用 `max(37,h+17)`，例如 `mockup.js:331`、`:450`、`:618`、`:921`。源码注释混合引用“截图实测 37”与缩放说明；参考主题 `Button.minimumSize=72,28`，实际高度还应经 `DarculaButtonUI.java:359` 的组件公式计算。 | 重新核查物理像素到逻辑单位的换算及实际按钮尺寸。37 与 28 并非可直接互换的同类值；该项尚未逐弹窗实测差额。 |
| 9 | P2 | 正文及 Diff 的字体和行距模型不同 | Augit 默认 Cascadia Mono，正文行高为字号×1.7（`mockup.js:60`、`:1455`）；本机旧配置还会沿用共用字号。参考 `FontPreferences.java:23` 的默认行距系数为 1.2，且乘数基数是字体度量，不能直接与 1.7 相除。 | 用同一份文本核对真实编辑器字体、基线间距和可见行数，单独校准正文；不把界面 12px 套到代码区。 |
| 10 | P2 | 部分状态文字过度加粗 | `.file-status-modified/deleted/added/conflict` 及其他状态类统一设 600（`mockup.css:710` 起）。参考 Changes 渲染器 `ChangesBrowserNodeRenderer.java:66` 起通过装饰器处理，兜底为 `STYLE_PLAIN`，没有“所有 Git 状态都加粗”的统一规则。 | 核查每个实际使用点，按参考角色保留必要粗体；这与字号一起影响视觉重量，不能一刀切取消所有粗体。 |
| 11 | P2 | YAML 图标颜色层未复原 | `mockup.css:929` 为统一红色；`nui-behavior/22-icon-alignment.md` 已明确当前几何使用 `currentColor`。参考 `platform/icons/src/expui/fileTypes/yaml.svg` 包含纸张浅色、红色轮廓和灰色 Y 三层。 | 补齐语义颜色层和深浅变体；已有几何修复不等于完整图标复原。 |
| 12 | P2 | 已登记的搜索历史图形没有用于对应入口 | `mockup.js:4394` 的日志“文本或哈希”入口仍调用普通 `icon("search")`；同文件多个 `history-search` 调用点相同。当前 PyCharm 日志搜索框图形带下拉提示。历史图标审计也登记了“图标块修了、调用点没改”。 | 按有搜索历史/选项的具体入口选择图形，并核对其下拉行为；不能给所有普通搜索框统一加下拉。 |
| 13 | P2 | 深色不是完整的 Islands Dark 色族 | Augit 弹层 `#2B2D30`、对话框 `#1E1F22`（`mockup.css:312`）；参考 `ManyIslandsDark.theme.json` 的 popup-bg 为 layer-1-bg / `#26282C`，dialog-bg 为 layer-0-bg / `#191A1C`。实现注释也承认取“最接近的 expUI”色。 | 逐语义令牌解析继承链，不混用 expUI 和 Islands。此项为源码确认，本次未切换用户 PyCharm 主题做深色真机对照。 |
| 14 | P2 | Git 提交图轨道颜色算法不同 | `mockup.js:3630` 固定四色循环；参考按引用名、head/片段等计算颜色。`ui-classification.md` 的 2.12a 也明确登记为有意差异。 | 若继续追求完整相似度，应补齐数据映射和颜色规则；本次不把已完成的节点/连线几何重新记为缺失。 |
| 15 | P2 | 滚动条及列表悬停状态没有完整复原 | `mockup.css:258` 明确未实现滚动条悬停档；`:614`、`:632` 隐藏标签条滚动条。树/Changes 保留自定义悬停底（`:799`、`:1309`），历史文档把这些归为有意差异。 | 将它们作为可见交互差异保留在清单中；按参考逐项恢复，而不是用“已结案”代替一致性。 |

## 尚不能宣称完成的验证

1. **真实 PyCharm 页面覆盖不完整。** 现有 `ui-compliance.md:66` 记录 33/55 面级对照、5 页仅入口级证据、17 页未对照。这是历史覆盖记录，不是本轮重新跑出的结果，更不是“还原率 60%”。
2. **5 页仅入口级证据：** push、push-no-remote、rollback、stash、worktrees；看到菜单入口不等于验证对话框正文。
3. **17 页历史未对照：** blame、changes-context-menu、clone、conflict-list、conflict-resolver、file-history、git-unavailable、image-error、operation-progress、operation-result、remote、repository-init、repository-search、reset、stash-drop-confirm、stash-manager、workspace-open。其中部分为产品自有表面或已认可的差异，不能全部计为实现缺陷。
4. **旧“无法采集”理由需要重新判断。** 本次已能打开 PyCharm Settings，当前仓库也确有改动。因此“本机没有可用 PyCharm”或“当前工作区干净”不能无条件沿用。没有重试的页面不宣称已解除阻塞。
5. **内部像素一致不是 PyCharm 一致。** 视觉稿与运行时共享代码，字节一致和同引擎像素测试只能证明内部一致性；无法证明字体、文案、组件选型正确。
6. **归类表有过时数值。** 例如 `ui-classification.md` 1.16 仍登记树行高 28，而当前代码基础值为 24；标签 42 的旧条目也落后于当前 40。旧条目不能直接变成新的修复任务。
7. **字体栅格化、深色全场景、不同 DPI、最小窗口及 Windows 10 实机**未在本轮完成验收。

## 推荐实施顺序

1. 明确以当前英文 Islands Light 界面为基线，统一术语和生效字体配置。文档和注释仍用中文。
2. 修复设置页溢出，重新校准树、历史、正文行距与工具栏；把工具轨专用尺寸从普通按钮中拆开。
3. 校准各对话框按钮、动作顺序与留白，再补齐图标颜色、深色令牌和交互细节。
4. 对未采页面补充真实对照，分别记录已一致、明确差异、证据不足；不再把三者合成“100%”。

本清单不要求补齐 PyCharm 的运行/调试、AI、插件、Git Console、Force Push、多仓库或普通文件编辑能力；这些由 Augit 产品边界决定。规范与参考存在冲突时，应先明确规范再实施，不能自行改写产品行为。

## 验证与资源

- 已执行共享资源一致性检查，四个文件通过。开始审计时 `git diff --check` 无输出。
- 本轮不修改可执行逻辑，不运行全量测试；截图不替代功能测试或平台验收。
- 当前 `web/src/mockup.js` MD5：`8E2F10978649FB789359FAA9F9337C9F`；`web/src/mockup.css` MD5：`3CB729D394DDEEDACC4E3D54CF70DAFD`。
- 本次启动 Augit PID 21620；所属 WebView2 PID 为 11828、18640、17884、15224、19816、9116。只清理本实例，不关闭用户已有 PyCharm。不启动服务器或终端会话，不生成临时截图文件。
- 结束后按上述七个 PID 复查，无残留进程；字体配置仍为 Segoe UI / 14。新增报告为 UTF-8 / LF，结束时 `git diff --check` 无输出。

手动复现启动与停止命令（同一个 PowerShell 会话；前台窗口用于观察）：

```powershell
$auditProcess = Start-Process -FilePath 'D:/github/Augit/src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit --web-root D:/github/Augit/web --width 1646 --height 981 --no-session-restore' -PassThru

# 观察结束后，先关闭设置等弹层，再正常退出本次实例。
$auditProcess.Refresh()
if (-not $auditProcess.HasExited) {
    [void]$auditProcess.CloseMainWindow()
    if (-not $auditProcess.WaitForExit(5000)) {
        taskkill /PID $auditProcess.Id /T /F
    }
}
```
