# 保留页面覆盖矩阵（2026-10-18）

> 本表按 `docs/ux-spec.md` §12 的页面清单对照当前证据；只做静态盘点，不新增配对，不替代正常入口截图。当前页面级同态配对 **48**、像素合格 **0**。

| # | 页面/状态 | 状态 | 证据 | 下一步 |
| --- | --- | --- | --- | --- |
| 1 | 打开工作区 | scoping | 实际 Open Workspace 已截（`clone-pair/actual/actual-workspace-window.png`）；参考 Welcome/Trust 未配对 | 参考 Open Project/Welcome 同态采集 |
| 2 | 初始化仓库 | 配对 | [初始化仓库配对](init-repo-homologous-pair-2026-10-18.md) | 执行初始化后状态、Choose Directory、已存在 .git、深色 |
| 3 | 项目 | 配对 | [首个配对](homologous-main-text-pair-2026-10-18.md)（含 Project 面板） | 面板几何/宽度差异继续收窄 |
| 4 | 项目右键菜单 | 配对（文件行+根节点） | [项目文件右键菜单配对](project-file-context-homologous-pair-2026-10-18.md)、[根节点右键配对+修复](project-root-context-homologous-pair-2026-10-18.md) | 目录节点/菜单动作未测 |
| 5 | 普通文本 | 配对 | [首个配对](homologous-main-text-pair-2026-10-18.md) | 标签/工具栏/几何已知差异 |
| 6 | Markdown | 配对 | [Markdown 配对](markdown-homologous-pair-2026-10-18.md) | 源码高亮/垂直节奏 |
| 7 | JSON | 配对 | [JSON 配对](json-homologous-pair-2026-10-18.md) | 语法高亮/工具栏 |
| 8 | 图片 | 配对 | [有效图片配对](image-valid-homologous-pair-2026-10-18.md) | 默认缩放按 §7.5 不放大，不改 |
| 9 | 不可预览文件 | 配对（大文件预览）/部分 | [大文件预览配对](large-file-limit-homologous-pair-2026-10-18.md) | Warning 横幅显示态（两端均已关闭）、二进制摘要页产品专有差异、图片大文件 |
| 10 | Commit | 配对 | [Commit 配对](commit-homologous-pair-2026-10-18.md) | 标签/占位/图标/底部 Log |
| 11 | Commit 空态 | 配对 | [Commit 空态配对](commit-empty-homologous-pair-2026-10-18.md) | 空态呈现/Unversioned/Log/通知差异 |
| 12 | Changes 右键菜单 | 配对 | [Changes 菜单配对](changes-context-menu-homologous-pair-2026-10-18.md) | 动作子集 vs 完整菜单 |
| 13 | 工作区 diff | 配对 | [Diff 配对](diff-homologous-pair-2026-10-18.md) | 行尾/工具栏/编辑铅笔 |
| 14 | Diff 局部加载 | 未完成 | 无 | 大文件或慢读，实际/参考加载态 |
| 15 | Git 历史 | 配对 | [Git Log 配对](git-log-pair-scoping-2026-10-18.md) | 标签模型/Console/图/列/高度 |
| 16 | Git 历史右键菜单 | 配对 | [提交菜单配对](git-log-context-menu-homologous-pair-2026-10-18.md) | 实际动作子集 |
| 17 | 文件历史 | 配对 | [File History 配对](file-history-homologous-pair-2026-10-18.md) | Revision/头/Console/预览宽度 |
| 18 | Blame | 配对 | [Blame 配对](blame-homologous-pair-2026-10-18.md) | inlay/Console/几何；未提交行已修 |
| 19 | 引用比较 | 配对 | [Compare 配对](compare-workspace-homologous-pair-2026-10-18.md) | 标签/头/行尾措辞/工具栏 |
| 20 | 分支与标签 | 配对 | [分支弹层配对](branches-homologous-pair-2026-10-18.md) | 行菜单/快捷键/搜索措辞/几何 |
| 21 | Smart Checkout | 未完成 | 无 | 实际/参考 Smart Checkout 页 |
| 22 | 创建 Stash | 配对 | [Stash 配对](stash-homologous-pair-2026-10-18.md) | Git root/Include/历史/尺寸；复选同行已修 |
| 23 | Stash 管理 | scoping | [Stash Manager 取证](stashes-manager-scoping-2026-10-18.md) | 参考 Git 菜单/工具窗定位 Stashes |
| 24 | Reset | 配对 | [Reset 配对](reset-dialog-homologous-pair-2026-10-18.md) | 四档单选/标题/头/尺寸 |
| 25 | Rollback | 配对 | [Rollback 配对](rollback-changes-homologous-pair-2026-10-18.md) | 多文件树/Delete local copies/尺寸 |
| 26 | Worktree | 配对 | [New Worktree](new-worktree-dialog-homologous-pair-2026-10-18.md)、[Worktrees 管理](worktrees-manager-homologous-pair-2026-10-18.md) | 字段已修；Branch/Project name/形态差异 |
| 27 | 远端 | 配对 | [Git Remotes 配对](git-remotes-homologous-pair-2026-10-18.md)、[Define Remote 配对](define-remote-homologous-pair-2026-10-18.md) | 编辑/删除远端状态 |
| 28 | Clone | 配对 | [Clone 配对](clone-dialog-homologous-pair-2026-10-18.md) | provider 列表/标签/默认目录 |
| 29 | Push | 配对 | [Push 无 upstream](push-dialog-homologous-pair-2026-10-18.md)、[Push 有 upstream](push-upstream-homologous-pair-2026-10-18.md)、[Push 待推提交](push-commit-homologous-pair-2026-10-18.md)、[Push 被拒](push-rejected-homologous-pair-2026-10-18.md) | 成功结果无独立页面 |
| 30 | Push 无远端 | 配对 | 同 Push 配对（无 upstream） | 已有远端/上游状态未采 |
| 31 | Git 操作进行中 | 未完成 | 无 | 慢 fetch/push 进度，禁止危险写 |
| 32 | 冲突列表 | 配对 | [冲突列表配对](conflict-list-homologous-pair-2026-10-18.md) | 解决后 Continue/Abort、多冲突文件、深色 |
| 33 | 快速打开 | 配对（结果） | [Quick Open 结果配对](quick-open-results-homologous-pair-2026-10-18.md)、[快速打开 scoping](quick-open-pair-scoping-2026-10-18.md) | 选择结果打开、类/符号/动作搜索 |
| 34 | 快速打开空态 | 配对 | [Quick Open 空态配对](quick-open-empty-homologous-pair-2026-10-18.md) | 实际 Open File vs 参考 Search Everywhere；Ctrl+P 需 WebView 焦点 |
| 35 | 全仓搜索 | 配对（空态+结果+零结果） | [Find in Files 空态](search-empty-homologous-pair-2026-10-18.md)、[结果](search-results-homologous-pair-2026-10-18.md)、[零结果](search-no-results-homologous-pair-2026-10-18.md)、[全仓搜索 scoping](full-search-pair-scoping-2026-10-18.md) | 正则/忽略文件；实际结果含 .idea 差异待按规则核对；截断与超时另项 |
| 36 | 搜索截断与超时 | 配对（截断）/部分 | [搜索截断配对](search-truncation-homologous-pair-2026-10-18.md) | 超时未覆盖；掩码/排除文件/深色 |
| 37 | 终端 | 配对 | [终端配对](terminal-homologous-pair-2026-10-18.md)（含深色/选区） | 状态栏/右栏/ANSI/关闭运行中 |
| 38 | 关闭运行中终端 | 配对 | [关闭终端配对](close-terminal-homologous-pair-2026-10-18.md) | Stop/Terminate 后的实际终止、Don't ask again 持久化 |
| 39 | 设置 | 配对（Appearance/Git/Terminal/File View） | [Settings/Appearance](settings-appearance-homologous-pair-2026-10-18.md)、[Settings/Git](settings-git-homologous-pair-2026-10-18.md)、[Settings/Terminal](settings-terminal-homologous-pair-2026-10-18.md)、[Settings/File View](settings-fileview-homologous-pair-2026-10-18.md)、[设置 scoping](settings-pair-scoping-2026-10-18.md) | 修改保存、慢开/白闪/关闭性能 |
| 40 | Git 不可用 | 配对 | [Git 不可用配对](git-unavailable-homologous-pair-2026-10-18.md) | 修复路径后恢复、不同错误、深色 |
| 41 | Git 操作结果 | 部分（成功 toast 状态级） | [Git 操作结果配对与修复](git-result-homologous-pair-2026-10-18.md) | 成功 toast vs 参考 balloon；结果页、fetch 结果、失败结果未闭环 |

## 结论

- 本表对 §12 的 41 项逐一标注：**未完成 6 项**（不可预览文件—**大文件预览已配对、二进制摘要产品差异/Warning 横幅未覆盖**、Diff 局部加载、Smart Checkout、Git 操作进行中、搜索截断/超时—**截断已配对、超时未覆盖**、Git 操作结果—**成功 toast 状态级已配对、页面未闭环**）、**scoping 2 项**（打开工作区、Stash 管理）。项目文件行与根节点右键均已配对；目录节点/菜单动作未测；全仓搜索空态/结果/零结果/截断、Quick Open 空态/结果、Git 不可用、初始化仓库、关闭运行中终端已配对。
- 页面级配对总数 **48**，覆盖本表项目/项目文件右键/项目根节点右键/初始化仓库/文本/Markdown/JSON/图片/大文件只读预览/Commit/Commit 空态/Changes 菜单/Diff/Git Log/菜单/文件历史/Blame/Compare/分支/Stash/Reset/Rollback/Worktree/远端 Git Remotes+Define Remote/Clone/Push（无 upstream+有 upstream+待推提交+被拒）/冲突列表/Settings Appearance+Git+Terminal+File View/Find in Files 空态+结果+零结果+截断/Quick Open 空态+结果/Git 不可用/关闭运行中终端/终端，另含表中未单列的 Checkout/New Branch/New Tag/Worktrees 管理/损坏图片错误页等模态子页。41 项不是 54 页全量分母。
- **不应把本表的状态当作 41/41 或 54/54 完成**；未完成项需要正常入口证据。
- 输入桌面在 2026-10-19 08:31 恢复后已补 Commit 空态、Git Remotes、Push 状态、冲突列表和项目文件右键；后续优先 Git 操作结果页、Smart Checkout、不可预览/大文件限制。
