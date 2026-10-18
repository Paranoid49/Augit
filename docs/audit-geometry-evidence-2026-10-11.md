# 图标、几何和状态专项取证（2026-10-11）

本批只读核对当前源码，未构建、未启动 Augit、未操作用户 PyCharm，也没有修改共享 UI 文件。分支为 `dsh`，HEAD 为 `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`。进入本批时工作区已有跨模块改动；源码继续被主任务其他 agent 修改，因此下列行号和哈希描述本批读取时状态，不能代替下一批实测。

已读取当前产品、UX、设计系统、树和图标行为册及 2026-10-10 的场景、筛选、设置、查找与审查整改记录。以前报告中的中文主界面、项目树 30px 行高、旧文件类型映射、旧工具轨 32px 结论不重复作为当前缺陷。

## 当前证据

本节记录发现时状态。主任务随后通过 Sky 在真实 PyCharm 前台打开 Git → Manage Remotes，确认 Name／URL 两列表格、Add／Remove／Edit 工具栏与唯一 OK；双击现有行打开 Define Remote，字段为 Name／URL、Fetch remote 与 OK／Cancel。仅观察并按 Esc 返回，没有输入、保存或 fetch。G-01 的额外云图标已经从实时与静态模板移除，`--reference-only` 11 项、远端浅深主题／字号／DPI 12 场景通过；这些测试不表示 G-02 结构已经还原。

| 编号 | 优先级与分类 | 触发条件及当前实现 | 参考实际调用链 | 处理建议与取证边界 |
| --- | --- | --- | --- | --- |
| G-01 | P2，确认的图标调用差异 | 打开 Remotes，左侧每个远端名都先渲染 `cloud`。`web/src/mockup.js:2108` 为 remote 指定 cloud，`:2118` 将该图标插入每个对象行。 | `plugins/git4idea/backend/src/remote/GitConfigureRemotesAction.kt:34` 打开 `GitConfigureRemotesDialog`；后者 `:77` 注册 `MyCellRenderer`，`:231-245` 的 RepoNode、RemoteNode、String 三条分支只 append 文本，不设置任何远端云图标。 | 这是使用图标的场景错误，不能用 cloud 的 SVG 几何测试说明已经匹配。产品 `product-spec.md:162` 要求参考无图标处保持空图标列；去除远端行额外 cloud 图形可独立整改。只有源码证据，未测前台可见差几像素。 |
| G-02 | P1，现行规格与参考结构分歧 | Remotes 在左列表右详情内联编辑 Name、Fetch URL、Push URL，并直接放 Save/Delete。`web/src/mockup.js:2028-2039`、`:2120`，`web/src/mockup.css:4205` 为双栏，`:4248-4264` 给远端详情、字段和内距。 | `GitConfigureRemotesDialog.kt:57` 为 JBTable；`:69` 底部仅 OK；`:73-96` 为表格及 Add/Remove/Edit ToolbarDecorator，`:210` 两列 Name/URL；双击 `:79-87` 执行 editRemote，`:274-278` 另开 GitDefineRemoteDialog。 | 当前 `ux-spec.md:537` 明确要求双栏，`design-system.md:609` 也按双栏给尺寸。这是产品内布局规则与 100% 参考目标的分歧，不应把局部字段尺寸测试通过当作整个二级页面还原。需按主任务最新目标同步裁决布局，不能在本批只读审计中擅改规范。未取得当前 PyCharm Remotes 前台截图。 |
| G-03 | P2，确定的字号缩放机制分歧，涉及现行规格 | 修改界面字号时 `applyTypography` 仅写 `--augit-ui-scale` 并缩放行高、缩进、图标槽位；`web/src/mockup.js:70-92`。SVG 可见盒仍固定 16px（`mockup.css:917-927`）、紧凑族 13px（`:931-940`）、工具轨 20px（`:4842`）。未发现补偿的 CSS zoom/transform；Shell 的 RasterizationScale 只按 DPI（`ShellWindow.cs:909-926`）。 | `LafManagerImpl.kt:858-871` 在 overrideLafFonts 时由实际界面字号计算并设置 userScaleFactor；`JBUIScale.kt:286-299` 归一缩放；`CachedImageIcon.kt:158/167/192-203` 的尺寸取实际图标，`ScaledIconCache.kt:36-49` 缓存按系统缩放及 `JBUIScale.scale(1f)` 生成，`:53-62` 按 ScaleContext 加载。 | 大字号时 Augit 放大图标槽但固定墨迹，与参考字号驱动的用户缩放链不同。`design-system.md:514/645` 和 `ux-spec.md:618` 部分位置明确要求固定图标尺寸，因此要先按目标统一这些规格；不能只扩大命中区便称全局字号还原。此项不等于系统 DPI 错误，也未实测同字号 PyCharm 图标盒或抗锯齿。 |
| G-04 | P1，真实对照配置不一致 | 本批独立读取 `%LOCALAPPDATA%/Augit/settings.json`，等宽字体为 Cascadia Mono、13px，界面为 Microsoft YaHei UI、12px。`mockup.js:2199` 的 Restore Reference Appearance 仍把等宽字体写为 Cascadia Mono。 | 主 agent 在当前 PyCharm 前台 Editor > Font 页面实测为 JetBrains Mono、13.0、Line height 1.2、ligatures off；Appearance 为 Microsoft YaHei UI 12、Zoom 100%、Sync with OS、Islands Light、compact off。 | 字体家族不同已能改变字宽、轮廓和行高。旧文档将 Cascadia Mono 写成“PyCharm实测基线”已不成立，需同步参考快照和恢复参考外观草稿。不得为对照静默改变用户设置；JetBrains字体在PyCharm的jbr/lib/fonts存在不等于已安装为浏览器系统字体，也不得把其文件复制到Augit分发。 |

## 排除的错误候选

Settings 导航失焦后仍保持蓝色选中，是参考特意设置的例外。`SettingsTreeView.java:158` 显式设置 `RenderingUtil.ALWAYS_PAINT_SELECTION_AS_FOCUSED = true`。Augit 的 `mockup.css:4895` 保持其蓝色背景，不能仅凭它覆盖通用 inactive 规则就判缺陷。需要逐组件确认焦点代理和强制选中策略，不能把“所有列表失焦必须变灰”推广到 Settings 树。

文件名 Git 状态色与文件类型图标已由不同元素规则负责，当前颜色映射存在 added/modified/deleted/conflict/untracked/renamed/copied/typechanged/unmerged 规则。本批没有取得全部真实 Git 状态和选中/失焦组合的前台色样，不据此宣称完整颜色矩阵通过，也不把已修复映射再列为缺陷。

## 正文行高的证据边界

`web/src/mockup.js:1540-1545` 当前并非直接 `1.2 × 字号`，而是 `ceil(ceil((Canvas fontBoundingBoxAscent + fontBoundingBoxDescent) × DPR) × 1.2) / DPR`。正文、Diff 与冲突结果复用这个入口。对照 `EditorView.java:842-850`，参考先通过 `FontInfo.getFontMetrics` 与 `FontLayoutService.getHeight(fm)` 得到 AWT 字高，再由 `:991-1019` 的 `getLineHeight` 乘 scheme lineSpacing；两个 registry 分支还可能使用字体缩放后重新度量或不同 leading 处理。

现有注释明确说明浏览器没有 AWT leading，但本入口没有已证明的 Canvas→AWT 度量归一化。它与参考使用相同 1.2 参数，不能单凭公式证明相同行高；也不能按 `13×1.2=15.6` 宣称当前多了几像素。需要先将两端真实字族统一，再实测同字体、同字号、同DPI的字符基线间距，并记录当前参考 registry 分支。此项目前是验收证据不足，不写成已经证实的渲染数值缺陷。

## 尚需实际取证

1. 同窗口、同 DPI、同主题下 Remotes 主表、双击编辑二级窗口及按钮启用状态。
2. PyCharm 与 Augit 同界面字号 12/18/24，分别量测图标墨迹、图标盒、按钮命中盒、树缩进和文字基线；系统 DPI 不变，并记录、恢复 PyCharm 字号原值。
3. Push、Worktree、Stash、搜索、历史、Settings 各自 focused/selected/inactive/disabled 状态，避免用 Settings 的强制聚焦选中色代替普通列表规则。
4. 全部 Git 状态在 Project、Changes、历史变化文件中的浅/深色颜色、选中前后图标类型色及组合状态；复杂提交图仍需真实场景。
5. 前台字体栅格、悬停/按下动画、滚动条和 Windows 10 22H2 实机。本批没有这些证据，不能据此标记 100% 完成。

## DSH 与资源记录

已强制调用 DSH 做高消耗源码核对。最初专项 workflow 未返回可用结论，收口时停止它；随后改为针对已发现机制的三文件复核，独立会话 `session-0792f43b-1a87-4bce-8dce-561aa33003f5` 已完成，确认图标固定 px 与字号驱动 USR_SCALE 的声明机制分歧，以及 Settings 树的强制聚焦例外。其有限读取未证明所有活跃组件，报告进一步人工核对 CachedImageIcon、ScaledIconCache 与实际 Remotes Action 路径后才纳入上表。

本批没有 Build、Publish、应用、浏览器或服务器进程。没有再次读取、删除或绕过自动审批拒绝的 Publish 临时目录。仅生成本批任务提示和 DSH 输出临时文件；任务结束清理这些文件，并检查对应 DSH 进程退出。

读取时运行时 MD5：mockup.js=`9EF62459D5BCFA367727853DD984C6F8`，mockup.css=`B46C6E073405329890FF4CA409135324`，live-data.js=`7B87127A9CF57CC637276E999D4D1DF2`，bridge.js=`16E41439BB7F4C2A36C2E95A4375A823`。本批只新增本报告，无 UI 实现改动，因此未运行与本批无关的完整测试。
