# 状态覆盖矩阵（⑭ 的差集表）

**口径**（先把"什么算一个状态"定清楚，避免用感觉填表）：

- 规格来源：`ux-spec` §6.5（加载反馈）、§9.1–§9.4（状态机）、§10.1 空态、§10.2 错误、§10.3 禁用、§10.4 危险；
- 判定方式（全部可复跑）：
  1. **专用场景页**：`docs/ux-mockups/<name>.html` + `web/src/mockup.js` 的 `scenePages`；
  2. **页面内状态**：`mockup.js` 里由 URL 参数或 `dataset` 表达的状态 —— 实测清单为
     `find-state`、`search-state`、`json-state`、`commit-state`、`clone-result`、`stash-result`、
     `terminal-state`、`history-state`，以及 `loading / ready / success / target / unified / graph /
     details / empty-side / long / long-error` 等布尔或取值参数
     （复跑：`grep -oE '(get|has)\("[a-z-]*state[a-z-]*"\)' web/src/mockup.js`）；
  3. **缺**：上述两处都找不到对应状态 —— 记"未发现专用视觉稿"，并给出候选补法；
  4. **待人工确认**：实现里有、但我还没有逐一截图核对的（不写成"已有"）。
- ⚠️ 本矩阵只覆盖**静态视觉稿的状态覆盖**；"操作 → 状态转换"在
  `docs/baselines/pycharm-interactions.json` 的 `sequences` 里另算。

## 1. 文件查看类

| 页面 | 默认 | 空 | 错 | 加载 | 禁用 | 危险 |
| --- | --- | --- | --- | --- | --- | --- |
| 项目与文件查看 `main-project` | ✅ 专用页 | ✅ `editor:"empty"`（"选择文件以查看内容"，由 `workspace-open`/`repository-init`/`git-unavailable` 场景携带，无独立页） | 待人工确认（目录不可用提示有实现） | ✅ 目录懒加载 | ✅ 未选中文件时的动作禁用 | — |
| 普通文本 `text-viewer` | ✅ 专用页 | ✅ `find-state=empty` | ✅ `find-state=invalid`（正则非法）/`no-match` | ✅ `find-state=loading` | — | — |
| Markdown `markdown-preview` | ✅ 专用页 | — | 待人工确认（预览渲染失败） | — | ✅ 非源码模式的定位按钮 | — |
| JSON `json-preview` | ✅ 专用页 | — | ✅ `json-state=invalid`（错误定位条） | — | ✅ 格式化按钮在非法 JSON 时禁用 | — |
| 图片 `image-preview` | ✅ 专用页 | — | ✅ `image-error` 专用页（⑭ 第 5 项已完成：宿主 `ImageDecodeFailed`/`ImageTooLarge` 与"浏览器解不开"三种失败都走同一信息态并显示原因） | 待人工确认 | ✅ 非 PNG/JPEG/BMP 不放行 | — |
| 不可预览文件 `file-limit` | ✅ 专用页（该页本身即错误/超限态：超限 / 非法 UTF-8 / GIF / WebP / 二进制摘要） | — | ✅ 同左 | — | — | — |

## 2. Commit / Diff / 历史

| 页面 | 默认 | 空 | 错 | 加载 | 禁用 | 危险 |
| --- | --- | --- | --- | --- | --- | --- |
| Commit `commit-changes` | ✅ | ✅ `commit-empty` 专用页 | ✅ `commit-state=validation`/`hook-failure` | ✅ 写操作进行中（`operation-progress` + 按钮进行态） | ✅ 无勾选/无信息时禁用提交 | ✅ 回滚确认（`rollback`） |
| Changes 右键菜单 `changes-context-menu` | ✅ | — | — | — | ✅ 不适用项禁用 | ✅ 回滚入口 |
| 工作区 Diff `commit-diff` | ✅ | ✅ `diff-status` 专用页（⑭ 第 2 项完成；`diff-status=Ready` 即无文本差异） | ✅ `diff-status`（二进制/超限/失败共用同一说明块，§10.2 三要素齐备） | ✅ `diff-loading` 专用页（含"再次点击进入下一个文件"的边界页 `diff-boundary`） | — | — |
| Git 历史 `git-history` | ✅ | ✅ `git-history-empty` 专用页（⑭ 第 1 项已完成：实时侧用空仓库实测 content 0.64） | ✅ Git 不可用 `git-unavailable` | ✅ 历史常比首屏慢（`history-state` 参数） | — | ✅ 危险提交操作入口 |
| Git 历史右键菜单 `git-history-menu` | ✅ | — | — | — | ✅ | ✅ |
| 文件历史与 Blame `file-history` | ✅ | — | 待人工确认 | ✅ | — | — |
| Blame `blame` | ✅ | — | ✅ 二进制/超限时只读说明（同 `file-limit` 语义） | ✅ | — | — |
| 引用比较 `git-compare` | ✅ | **缺**（无差异/无共同祖先） | ✅ `history-diff-failure`（同构） | ✅ `history-diff-loading`（同构） | — | — |
| 历史 Diff 加载/失败/取消 | — | — | ✅ `history-diff-failure` | ✅ `history-diff-loading` | — | ✅ `history-diff-cancelled`（取消后保留身份） |

## 3. Git 写操作与危险确认

| 页面 | 默认 | 空 | 错 | 加载 | 禁用 | 危险 |
| --- | --- | --- | --- | --- | --- | --- |
| 分支与标签 `branches` | ✅ 专用页 | ✅（无匹配引用） | ✅（检出失败文案） | ✅（弹层内进行态） | ✅ 不一致时禁用 | ✅ 删除分支/标签 |
| 创建 Stash `stash` | ✅ | — | ✅ `stash-result` | ✅ `stash-result` | ✅ 无改动时 | — |
| Stash 管理 `stash-manager` | ✅ | ✅（无 Stash） | 待人工确认（应用/弹出失败） | ✅ | ✅ 选项不可用时 | ✅ 删除确认（待人工确认是否有专用态） |
| Reset `reset` | ✅ | — | ✅（目标不存在） | ✅（进行态 + 取消） | ✅ 进行中禁用 | ✅ Hard 红确认 |
| Rollback `rollback` | ✅ | ✅（无改动可回滚） | ✅（失败保留说明） | ✅ | ✅ | ✅ 将丢失改动 + 回收站说明 |
| Worktree 管理 `worktrees` | ✅ | ✅（无 Worktree） | 待人工确认 | ✅ | ✅ 内置终端占用时禁用移除并给原因 | ✅ 移除确认（待人工确认） |
| 远端管理 `remote` | ✅ | ✅（无远端） | ✅ 保存失败 | ✅ | ✅ | ✅ 删除确认（待人工确认） |
| Clone `clone` | ✅ | — | ✅ `clone-result` | ✅ `clone-result` + 进度 | ✅ 浅克隆关闭时深度禁用 | — |
| Push `push` | ✅ | ✅ `push-no-remote` | ✅（错误反馈保留上下文） | ✅ 进行态 + 取消 | ✅ 无远端时禁用 | — |
| Git 操作进行中 `operation-progress` | ✅ 专用页 | — | — | ✅ | ✅ 重复动作不可触发 | ✅ 取消入口 |
| Git 操作结果 `operation-result` | ✅ 专用页（成功/失败/冲突） | — | ✅ | — | — | — |

## 4. 冲突、搜索、终端、设置、其他

| 页面 | 默认 | 空 | 错 | 加载 | 禁用 | 危险 |
| --- | --- | --- | --- | --- | --- | --- |
| 冲突操作会话 `conflict-list` | ✅ | ✅（无冲突） | ✅（Continue 冲突未解决时） | ✅ | ✅ Continue 禁用并给原因 | ✅ Abort |
| 三栏冲突解决 `conflict-resolver` | ✅ | — | ✅（非法 UTF-8/超限只允许整侧接受） | ✅ 应用进行中冻结 | ✅ 进行中禁用 | ✅ 危险接受 |
| 快速打开 `quick-open` | ✅ | ✅ `quick-open-empty` 专用页 | ✅（查询失败） | ✅ | — | — |
| 全仓搜索 `repository-search` | ✅ | ✅ `search-state=empty` | ✅ `search-state=error` | ✅ `search-state=loading` | ✅ `search-state=limited`（截断，专用页 `search-limited`） | — |
| 内置终端 `terminal` | ✅ 专用页 | ✅（无会话） | ✅（Shell 解析失败明确报错，不静默回退） | ✅（会话启动中） | ✅（关闭确认进行中） | ✅ `terminal-close` 专用页 |
| 设置 `settings` | ✅ 四个分类页 | — | ✅ `settings-save-failure` 专用页（⑭ 第 3 项已完成：两侧像素 0.04；另有"把设置文件临时造成不可写再点保存"的真机交互证据） | 待人工确认（Git 检测中） | ✅ 非"自定义命令"时命令输入框禁用 | — |
| Git 不可用 `git-unavailable` | ✅ 专用页 | — | ✅ | — | ✅ 整个 Git 模块禁用 | — |
| 打开工作区 `workspace-open` | ✅ 专用页 | ✅（无最近目录） | ✅（启动降级） | ✅ | — | — |
| 初始化仓库 `repository-init` | ✅ 专用页 | — | ✅ | ✅ | — | ✅（初始化确认） |
| Smart Checkout `smart-checkout` | ✅ 专用页 | — | ✅（覆盖风险） | ✅（临时 Stash 与恢复） | — | ✅ |
| Diff 文件边界 `diff-boundary` | ✅ 专用页 | — | — | — | — | — |
| Git 历史复杂图 `git-history-graph` | ✅（HTML 存在，`scenePages` 未登记） | — | — | — | — | — |
| 跳转行 `go-to-line` | ✅（HTML 存在，`scenePages` 未登记） | — | ✅（行号非法） | — | ✅ | — |

## 5. ⑭ 补图工作清单（按缺口优先级）

| # | 缺什么 | 依据 | 补法（候选） |
| --- | --- | --- | --- |
| 1 | ~~Git 历史空态~~ **已完成** | §10.1 | `docs/ux-mockups/git-history-empty.html` + 场景 `git-history-empty`（复用同一渲染器）+ 空仓库真机像素对照 0.64 |
| 2 | ~~工作区 Diff 的最终说明~~ **已完成** | §6.5 + §10.2 | 场景 `diff-status`（URL 参数 `diff-status`）+ 共享 `diffStatusNotice()`；实时侧改为正文说明块；真机二进制仓库像素对照 1.29 |
| 3 | ~~设置保存失败/只读失败~~ **已完成** | §9.3/§10.2；harness 有 `settingsReadOnly` 断言但无视觉稿 | `docs/ux-mockups/settings-save-failure.html` + 场景 `settings-save-failure`（复用四分类设置页渲染器）+ 两侧底栏文案逐字一致；真机用"设置文件不可写"实测复现（像素 0.04，第 314 轮） |
| 4 | **引用比较无差异/无共同祖先** | §7.9 | 复用 `history-diff-*` 的同构状态或新增 `git-compare-empty` |
| 5 | ~~图片解码失败~~ **已完成** | §7.5 | `docs/ux-mockups/image-error.html` + 场景 `image-error`（复用"不可预览文件"信息态）；实时侧判据改为按 `status` 而非 `kind`，并补 `<img>` 解码兜底；真机三样本实测 + 两侧像素 0.01（第 315 轮） |
| 6 | 分组折叠 / 未保存修改标记（设置页） | PyCharm 实测（`pycharm-settings-search-font.png`） | 需先定设计令牌，交用户确认 |
| 7 | 删除确认态（Stash/Worktree/远端）是否已有专用视觉稿 | 实现与 harness 有确认流程 | 先逐一截图确认，再决定是否补页 |

> 说明：「待人工确认」的格子会在补图时先截图核对（避免把"实现里有"写成"视觉稿有"）；
> 每补一页按 ⑮ 立即接线 + 断言 + 负向验证，并在本表把该格改成 ✅ + 场景名。
