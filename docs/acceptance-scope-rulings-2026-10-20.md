# 验收范围、用户裁决与证据台账（2026-10-20）

本文同步 2026-10-20 用户对上一 session 最后五张解释图及提交问题的答复，作为当前验收范围的权威文件。旧文档中与本文直接冲突的“待用户裁决”“未提交”“未触发即正常”等表述按本文更正；具体缺陷的原始证据仍以各专项文档和本机 `artifacts/` 为准。

## 1. 用户裁决（已批准，不再重复询问）

1. **不实现 42 CSS 右轨**：不新增右侧 42 CSS 工具轨，也不用删除或压窄 Commit Details 来凑图。右轨缺失登记为明确批准的范围差异。
2. **保留简化自定义 Settings**：不重建完整 PyCharm 设置树；保留控件的字体、配色、间距、状态和交互反馈继续对齐参考。简化功能不豁免慢开、闪烁、重复窗口和关闭卡顿。
3. **Diff 局部加载、Git 操作结果、二进制相关呈现，100% 参考 PyCharm 2026.2.1 与 `D:/github/intellij-community`**。自行查实机与本地源码确定真实呈现；参考若使用 loading indicator、balloon 或不可预览提示，就对齐对应形态；不虚构独立“结果页”；不再要求用户提供截图。第 3 条不是把 Diff/Git/二进制全部归为有意差异。
4. **搜索 timeout 按 bug 方向继续排查**：不以“代码存在、未触发”结束，也不以“没有触发”直接断言逻辑损坏。必须核对真实请求、扫描范围、忽略规则、rg 启动/退出、取消传播、部分结果保留、bridge 应答和前端状态覆盖。
5. **共用功能页必须像素合格**：两边都有的功能，其页面必须像素合格；Augit 不具备的功能不新增，相关界面保持一致的 UI 风格。不得用任意 1px 容差、模糊、平移、掩码、只比较正文或小区域配准冒充整页通过。
6. **后续自动提交、推送**：限本任务已验证改动，按大模块形成可复验结果后提交，使用 Conventional Commits；只暂存自己的文件，不强推，不混入用户、其他 agent 或历史临时文件；完成后核实远端提交号。

## 2. 验收类别

四个计数口径不同，禁止互相换算成完成率：

- **54 页目录**：`ux-spec.md` §12 的历史页面目录口径（移除三栏冲突编辑器后）。
- **41 项覆盖矩阵**：`remaining-pages-matrix-2026-10-18.md` 的条目口径，一项可含多个页面/状态。
- **50 组页面级语义/状态配对**：两端在同一上下文下取得语义相同的页面或状态，不等于像素合格。
- **0 张整页像素合格**：保守口径。任何已知结构、文字、几何、主题或状态差异都可能使整页不通过。

| 类别 | 定义 | 验收方式 | 计数 |
| --- | --- | --- | --- |
| A. 共用功能页 | 两端都有该功能，且可在相同上下文、主题、DPI、字体字号下到达同一状态 | 整窗原图对照；文字、图标、几何、配色、状态、交互反馈一致且在既定测量误差内 | 像素合格才计通过；当前 0 |
| B. 简化/专有界面 | Augit 不提供参考的完整功能树，或该界面为 Augit 专有（如简化 Settings、Stash Manager 对话框、二进制摘要、无 AI 入口） | 不逐像素配对；按已批准范围保持一致的字体、配色、间距、状态和交互反馈 | 单独登记；不冒充 A 类通过 |
| C. 已批准例外 | 用户已明确批准的差异 | 按例外记录，不整改、不计数 | 不增加 A 类像素通过 |

### 2.1 当前分类清单

**C 类已批准例外（不整改）**

| 例外 | 依据 |
| --- | --- |
| 无右侧 42 CSS 工具轨 | 本文件 §1 第 1 条（2026-10-20） |
| 终端 Reworked 命令块装饰与块间留白 | 2026-10-17 裁决；`ux-spec.md` §7.16 |
| 等宽正文固定 JetBrains Mono 13，设置页不提供等宽字体/字号选择 | 2026-10-15 裁决；`design-system.md` 第 5 行 |
| 无内置文本编辑/保存、无三栏冲突编辑器 | 2026-10-17 裁决 |
| 不接入 AI（参考的 AI Agents 下拉等入口不复制） | 产品范围 |
| 系统窗口阴影差异 | 既有登记；不作为像素通过 |
| Windows 10、跨屏 DPI | 未验证；如实登记，不宣称通过 |

**B 类简化/专有界面（保持风格，不做整页像素配对）**

- Settings 四个分类页（Appearance、File View、Git、Terminal）的简化功能集合。
- Stash Manager、Worktrees 管理、某些模态对话框中的专有动作集合。
- 二进制/未知类型文件的信息与差异不可预览提示（参考依赖文件类型关联与归档比较，Augit 不新增该功能）。
- 参考独有的 Console、AI Agents、完整 Git 动作、提交图列等。

**A 类共用功能页（必须像素合格）**

- 文本、Markdown、JSON、图片、大文件只读预览、Commit、Commit 空态、Changes、工作区 Diff、Diff 局部加载、Git 历史、文件历史、Blame、引用比较、分支与标签、Push（各状态）、Smart Checkout、冲突列表、快速打开（空态/结果）、全仓搜索（空态/结果/零结果/截断/超时）、终端（内容、颜色、选区、状态栏）、关闭运行中终端、设置各控件状态、Git 不可用、初始化仓库等。
- 已在 41 项矩阵登记但只取得状态级证据的（Diff 上限、Git 进行中、Git 结果、Push 失败）仍是 A 类缺口；状态级配对不能替代整页验收。

## 3. 三项参考呈现的已核对事实

### 3.1 Diff 局部加载

本地 `intellij-community`（HEAD `576e3282`）源码事实：

- `platform/diff-impl/src/com/intellij/diff/impl/DiffRequestProcessor.java`：初始活动请求是 `LoadingDiffRequest`；顶部工具栏含一个 `JProgressBar`（`setIndeterminate(true)`），请求准备期间通过 `DiffContext.showProgressBar(true)` 显示。即参考的加载形态是**头部不定进度条 + 正文 “Loading...” 消息面板**，不是正文骨架屏。
- `platform/diff-impl/src/com/intellij/diff/requests/LoadingDiffRequest.java`：正文消息取 `CommonBundle.getLoadingTreeNodeText()`（“Loading...”）。
- `platform/diff-impl/src/com/intellij/diff/requests/UnknownFileTypeDiffRequest.java`：未知文件类型在 Diff 区域内用 `DiffUtil.createMessagePanel` 显示 `error.cant.show.diff.for.unknown.file`（`Cannot show diff for an unknown file type`），并附 `button.associate.file.type` 动作。
- 超限：`VcsBundle.properties:1050` 的 `Cannot show the contents of ... The file is bigger than ...` 由 Diff 区域内的错误/消息面板展示，附 `Reload`。

实机证据（既有）：25.38 MB 文件两端都无 Diff 正文，参考显示 `Cannot show diff: ... bigger than 20.97 MB` + `Reload`；8.74 MB/92,001 行参考完整显示 `1 difference`，Augit 有 2000 行实现上限并已加截断说明。详见[Diff 上限参考态](diff-limit-reference-2026-10-19.md)、[截断提示修复](diff-truncation-notice-fix-2026-10-19.md)。

**结论**：Diff 局部加载按 A 类整改，形态改为头部不定进度条 + 正文 “Loading...” 消息；失败/超限/未知类型保持在 Diff 区域内的消息面板，不新造独立结果页。2026-10-20 已按源码形态实现并验证静态场景/历史比较；当前仍缺参考实机加载态像素证据，因此不记像素通过。截断说明是 Augit 实现上限的诚实提示，不因此算参考同态。

### 3.2 Git 操作结果

- 参考使用 `VcsNotifier` 的非打断式 balloon：成功为 `Pushed 1 commit to origin/main` 一类标题/正文；失败为 `Push failed` + `Show details in console`；实机已分别取得 PID 22152、22960 的屏幕证据。
- Augit 当前成功用右下 toast（`Push` / `Pushed to origin/main.`），失败在 Push 对话框内显示原始 Git 错误文本。
- **结论**：按 A 类对齐 balloon 形态（位置、层次、标题/正文/动作），不新增独立结果页；失败同时保留真实原因，不包装成成功。详见[Push 网络失败](git-push-failure-2026-10-19.md)、[Git 操作结果配对](git-result-homologous-pair-2026-10-18.md)。

### 3.3 二进制相关呈现

- 参考对未知类型在 Diff 区域内显示不可预览消息 + `Associate`；对 `.zip` 显示归档内容比较；Augit 不提供文件类型关联与归档内容比较，不新增。
- Augit 的二进制摘要/不可预览提示保持在 Diff 区域内，按 B 类统一字体、配色、间距和状态，不冒充参考同态，也不新增“结果页”。
- 大文件 Warning 的显示、真实鼠标 Hide、Don't Show Again 持久化与重启闭环已完成；可见反馈曾落在 3.7–4.2s，响应迟缓仍待分析，不能写成即时反馈。参考同一 28.5 MB 文件没有 Warning，不能捏造参考动作通过。

## 4. 搜索超时（按 bug 方向）

现状与已排除项：

- 生产代码存在 15 秒 `CancellationTokenSource`、`IsTimedOut` 和超时文案；`RunConfiguredAsync` 有“取消/超时后结束 ripgrep 进程”的单元测试。
- 250k/1M 文件实验只观察到 `No results found`，没有查询开始/完成的精确时间戳，不能据此证明 timeout 分支正常或失效。
- 250k/1M 树成本高且未触发，不再盲目增加文件数；`PROCESS_MODE_BACKGROUND_BEGIN`（error 87）、`IDLE_PRIORITY_CLASS`、非管理员 SMB 共享均已有记录，不重复。

要求：

1. 用有边界、可清理的受控慢操作（例如对真实 ripgrep 子进程做可核实的停顿）在正常入口触发 15 秒超时，记录查询开始、rg 启动、停顿点、超时时刻、rg 退出、bridge 应答和前端呈现的完整时间线。
2. 验证超时后：结果区保留已完成结果并显示超时状态；rg 进程（含子进程树）确实结束；随后可正常发起新查询；关闭浮层不复活旧结果。
3. 不缩短生产超时、不替换正常入口、不 mock 页面、不隐藏测试参数来制造通过。
4. 若真实超时后显示零结果、丢失状态或被 `No results found` 覆盖，修根因并补回归；若确认是测试未达到超时条件，给出完整时间线而不是猜测。

## 5. 正常入口、Settings 与刷新

- Settings 简化功能集合已获批准；慢开、白闪、重复窗口、关闭卡顿不在豁免范围。
- 最近 13 个原生周期：窗口出现 267–327ms，销毁 60–65ms；636/877ms 是标题探针，不能代替完整首内容、presented 或全帧无闪。无 CDP 首内容历史 599.2–747.5ms；2904ms 慢样本、真实 `CapturePreviewAsync` 500ms 超时及提前揭示风险仍未闭环。
- 刷新 `MergeDelay=20ms`；历史全窗首个新内容帧 731.7/494.7/462.5/480.2ms；轻量撤盖代理指标不能替代首内容帧。继续覆盖透明帧/白帧、外部分支变化、冲突同步、边缘、光标和工具提示。
- 正常入口统一使用 `tools/start-augit.ps1`；每批核对实际 exe/dll、ProductVersion、加载的 web 资源、配置路径、字体、主题、窗口/客户区尺寸、DPI 和缩放。

## 6. 提交、证据与清理

- 每个可独立复验模块完成后自动提交、推送；只暂存本任务文件；Conventional Commits；不强推。
- `artifacts/` 被 Git 忽略，原始截图/录像只在本机；推送代码不等于上传证据。
- 每批记录 PID、子进程、端口、临时目录和配置快照；结束正常关闭本批实例并核实资源释放；不按进程名清杀用户应用，不照搬旧 PID。
- 已批准例外按例外报告，不计作像素通过；Windows 10、跨屏 DPI 和已知阴影边界保持如实登记。

## 7. 完成条件

只有保留范围内的行为、性能及共用功能页像素目标都由正常入口的实际证据支持，才能标记整体完成。测试通过、配对数量、提交推送成功、局部状态级配对或小区域配准都不构成整体验收。

## 8. 2026-10-22 追加裁决

用户对以下三项统一答复：**参考 PyCharm 实机与 `D:/github/intellij-community`**。

1. **Markdown 预览字体/字号**：采用参考 Markdown 插件排版——正文 13px `Helvetica, Arial, freesans, sans-serif`，`pre` 0.85em、`code` 0.9em；同步修订 `product-spec.md` §7、`design-system.md` §5.3 和 `ux-spec.md` §7.3，不再要求预览跟随界面/等宽字号。2026-10-22 已实现 CSS 与测试，正常入口像素复测另行登记。
2. **Push 失败呈现**：按参考关闭 Push 对话框并在右下显示 `Push failed` balloon（类型图标、粗体标题、原始脱敏错误正文、蓝色文字动作）。参考的 `Show details in console` 依赖 Augit 不提供的 Git Console；为保留既有重试能力，动作改为 `Show details`，点击重新打开含错误详情、可重试的 Push 对话框。2026-10-22 已实现并同步修订 `product-spec.md` §5.1 与 `ux-spec.md` §7.12，不新增独立结果页或 Git Console；正常入口实机证据另行登记。
3. **文件状态表达**：Project Files 视图按参考只用文件名颜色表达状态，移除行尾 `M` 徽标；同步修订 `ux-spec.md` 第 171 行，保留 `FILESTATUS_*` 颜色语义，不新增其他状态列。2026-10-22 已实现，正常入口截图见[Markdown 记录](markdown-typography-2026-10-22.md)。

三项均不得扩大为新的有意差异；实施后仍以正常入口实机与整窗像素证据验收。
