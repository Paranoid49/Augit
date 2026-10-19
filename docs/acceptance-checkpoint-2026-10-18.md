# 验收检查点（2026-10-18）

## 1. 当前基线与资源

- 分支 `dsh`，HEAD `690eb1937b4b65600dbbd9d3e4bc91b8f93a61dd`；未提交、未推送。
- 工作区保留本会话改动与 `.tmp/`；fixture 恢复 app.py/notes.txt/README.md 三个文件；`settings.json` `B8868855…DC3D`。
- 无 Augit/pycharm64 残留、无 CDP 端口；参考 PyCharm 均已关闭，用户原有 PyCharm 未触碰。

## 2. 当前构建身份

| 项目 | 值 |
| --- | --- |
| `Augit.exe` | `DA0C4AFD…DF96C` |
| `Augit.dll` | `F53764A8…B4E00` |
| `ProductVersion` | `0.1.0+690eb1937b4b65600dbbd9d3e4bc91b8f93a61dd` |
| `web/src/mockup.js` | `DDF905A9…9377` |
| `web/src/mockup.css` | `5BAA1232…8967` |
| `web/src/live-data.js` | `FC04772F…17B1` |
| `web/src/terminal-copy.js` | `A6F9FEA4…604F` |

## 3. 页面级同态配对：48

| 页面 | 证据 | 已知差异 |
| --- | --- | --- |
| main-project / text-viewer | [首个配对](homologous-main-text-pair-2026-10-18.md) | 工具轨起点、行号槽、文档工具栏 |
| terminal.html 浅色 | [终端配对](terminal-homologous-pair-2026-10-18.md) | 状态栏 7px、右侧栏 42 CSS、ANSI、ui-size 40 |
| README Markdown 分栏 | [Markdown 配对](markdown-homologous-pair-2026-10-18.md) | 单/双标签、源码无高亮、预览垂直节奏（规格值） |
| sample.json | [JSON 配对](json-homologous-pair-2026-10-18.md) | 无语法高亮、只读、工具栏、多标签 |
| Git Log | [Git Log 配对](git-log-pair-scoping-2026-10-18.md) | 标签模型/Console、提交图/列、详情标题、工具窗高度 |
| Commit | [Commit 配对](commit-homologous-pair-2026-10-18.md) | 1 modified 标签、占位大小写/聚焦、工具栏图标、底部 Log 选中 |
| 工作区 Diff | [Diff 配对](diff-homologous-pair-2026-10-18.md) | HEAD/Workspace vs 哈希/Current、行尾标记、无编辑铅笔、工具栏、底部 Log |
| File History | [File History 配对](file-history-homologous-pair-2026-10-18.md) | Revision 列、e9188bf^/e9188bf vs e9188bf3 头、Console、预览宽度 |
| Blame | [Blame 配对](blame-homologous-pair-2026-10-18.md) | 只读文档 vs 编辑器注释、usage inlay、Console、几何；未提交行归属已修复为空白 |
| 分支弹层 | [分支弹层配对](branches-homologous-pair-2026-10-18.md) | Current、分支行菜单、快捷键、关闭/设置图标、搜索措辞、几何 |
| Checkout 对话框 | [Checkout 配对](checkout-dialog-homologous-pair-2026-10-18.md) | 标题/标签、OK/Cancel 顺序、标题图标、窗口/输入/按钮尺寸 |
| New Branch 对话框 | [New Branch 配对](new-branch-dialog-homologous-pair-2026-10-18.md) | 标题、空/预填 main、缺 Checkout/Overwrite、按钮顺序/图标/尺寸 |
| Stash 对话框 | [Stash 配对](stash-homologous-pair-2026-10-18.md) | Git root 文本/下拉、Include untracked、历史/帮助/图标、尺寸；复选同行已修复 |
| Reset 对话框 | [Reset 配对](reset-dialog-homologous-pair-2026-10-18.md) | 目标文本框/模式下拉 vs 四档单选、标题/头部摘要、帮助/图标、尺寸 |
| Push 对话框 | [Push 配对](push-dialog-homologous-pair-2026-10-18.md) | 标题、main →、右侧空态/工具栏、帮助、Push 下拉、尺寸 |
| Git Log 提交菜单 | [Git Log 菜单配对](git-log-context-menu-homologous-pair-2026-10-18.md) | 实际动作子集 vs 参考完整复杂 Git 动作；Copy/Compare 文案 |
| New Tag 对话框 | [New Tag 配对](new-tag-dialog-homologous-pair-2026-10-18.md) | 完整换行哈希 vs 截断、图标/问号、Create 启用 vs OK 禁用、尺寸 |
| Clone Repository | [Clone 配对](clone-dialog-homologous-pair-2026-10-18.md) | provider 列表/图标/More via plugins、Repository URL vs URL、默认目录、尺寸 |
| New Worktree | [Worktree 配对](new-worktree-dialog-homologous-pair-2026-10-18.md) | Branch 文本框 vs 下拉、无 Project name、Directory vs Location、字段重叠已修复、尺寸 |
| Compare with Workspace | [Compare 配对](compare-workspace-homologous-pair-2026-10-18.md) | 标签/头命名、行尾措辞、工具栏；左侧内容存在（已纠正预览误读） |
| Worktrees 管理 | [Worktrees 配对](worktrees-manager-homologous-pair-2026-10-18.md) | 对话框 vs 底部工具窗、详情/Open Window/Close、工具栏、尺寸 |
| 损坏图片错误页 | [图片错误配对](image-error-homologous-pair-2026-10-18.md) | 文件元数据/Open in Default Application vs Image not loaded/图片工具栏 |
| 有效图片预览 | [图片预览配对](image-valid-homologous-pair-2026-10-18.md) | 实际小图 100% 符合 §7.5；参考适应放大为已知差异；工具栏/元数据/状态 |
| Rollback 对话框 | [Rollback 配对](rollback-changes-homologous-pair-2026-10-18.md) | 单文件警告 vs 多文件树、Delete local copies、标题/按钮/尺寸 |
| Changes 右键菜单 | [Changes 菜单配对](changes-context-menu-homologous-pair-2026-10-18.md) | 实际动作子集 vs 参考完整 changelist/patch/shelve/VCS/Git 动作 |
| Git Remotes | [Remotes 配对](git-remotes-homologous-pair-2026-10-18.md) | 标题图标 A vs PyCharm；空远端/工具栏/表头/624×385 一致 |
| Commit 空态 | [Commit 空态配对](commit-empty-homologous-pair-2026-10-18.md) | No changes+禁用 vs 空 Changes+Unversioned 5+Commit 可用；Log/通知/编辑器 |
| Define Remote | [Define Remote 配对](define-remote-homologous-pair-2026-10-18.md) | 实际 Push URL 批准保留；高度 449 vs 363、图标 A vs PyCharm |
| Push 有 upstream | [Push upstream 配对](push-upstream-homologous-pair-2026-10-18.md) | 标题、origin/main vs origin : main、右栏/工具栏、Push 可用性、尺寸 |
| Push 待推提交 | [Push commit 配对](push-commit-homologous-pair-2026-10-18.md) | 标题/箭头、右栏计数+Target vs 文件树、工具栏、尺寸 |
| Push 被拒 | [Push rejected 配对](push-rejected-homologous-pair-2026-10-18.md) | 真正分叉；实际内联 raw Git error+重试 vs 参考 Push Rejected+Merge/Rebase |
| 冲突列表 | [冲突列表配对](conflict-list-homologous-pair-2026-10-18.md) | 实际 Merge Conflicts 模态 vs 参考 Merging main 标题栏/红色文件 |
| 项目文件右键菜单 | [文件菜单配对](project-file-context-homologous-pair-2026-10-18.md) | 实际 6 项轻量动作 vs 参考完整 IDE 菜单 |
| 项目根节点右键菜单 | [根菜单配对+修复](project-root-context-homologous-pair-2026-10-18.md) | 根节点空路径回落修复；测试 6/6；实际 6 项 vs 参考完整菜单 |
| Settings/Appearance | [Settings 配对](settings-appearance-homologous-pair-2026-10-18.md) | Light/字体 12/未保存一致；实际轻量分类 vs 参考完整 IDE 树；性能另证 |
| Settings/Git | [Settings Git 配对](settings-git-homologous-pair-2026-10-18.md) | 同一 git.exe 路径/未保存；实际轻量检测 vs 参考 Test/Commit/Push |
| Settings/Terminal | [Settings Terminal 配对](settings-terminal-homologous-pair-2026-10-18.md) | 同一 Terminal/字体族；实际 Shell 页 vs 参考引擎/补全/字体；Apply 初始可用已修复（7/7） |
| Find in Files 空态 | [Find in Files 配对](search-empty-homologous-pair-2026-10-18.md) | 空查询/无结果/In Project 一致；实际浮层 vs 参考工具窗、File mask/空态提示 |
| Find in Files 结果 | [搜索结果显示配对](search-results-homologous-pair-2026-10-18.md) | 查询 Ag 首条一致；实际 5 条（含 .idea）vs 参考 1 条、File mask/预览 |
| Find in Files 零结果 | [零结果配对](search-no-results-homologous-pair-2026-10-18.md) | 同一 zzzznotfound/无结果；实际 No results found+hint vs 参考 Nothing found/File mask |
| Quick Open 空态 | [Quick Open 配对](quick-open-empty-homologous-pair-2026-10-18.md) | 空输入/未打开文件；实际 Open File vs 参考 Search Everywhere；Ctrl+P 需 WebView 焦点 |
| Quick Open 结果 | [Quick Open 结果配对](quick-open-results-homologous-pair-2026-10-18.md) | 查询 notes/结果 notes.txt 一致；实际单列表 vs 参考 Files 标签+路径 |
| Git 不可用 | [Git 不可用配对](git-unavailable-homologous-pair-2026-10-18.md) | 均无引用/提交/状态、文件可浏览；实际 toast+Configure vs 参考空 Git 工具窗 |
| 初始化仓库 | [初始化仓库配对](init-repo-homologous-pair-2026-10-18.md) | 非 Git 目录/创建入口/未初始化；实际 Create Git Repository 模态 vs 参考 Version Control 空态 |
| Settings/File View | [File View 配对](settings-fileview-homologous-pair-2026-10-18.md) | JetBrains Mono 13/未保存；实际 File View vs 参考 Editor > Font（行高/连字/预览） |
| 关闭运行中终端 | [关闭终端配对](close-terminal-homologous-pair-2026-10-18.md) | 同一 Local/ping/关闭确认；实际 Keep/Stop Command vs 参考 Terminate/Cancel+Don't ask again |
| 搜索截断 | [搜索截断配对](search-truncation-homologous-pair-2026-10-18.md) | 同一 012345/notes+large/截断；实际 Too Many Results 模态 vs 参考 100+ matches 页；超时未覆盖 |
| 大文件只读预览 | [大文件预览配对](large-file-limit-homologous-pair-2026-10-18.md) | 同一 31.4MB/预览内容；实际 Read-only vs 参考可编辑；警告均已关闭 |

- **像素合格整页配对：0**；以上均为已知差异、非像素合格。

## 4. 已修复/已验证（本会话）

- 终端 Local 标签/标题/关闭；深色编辑器/终端底 `#1E1F22`；面板顶边 `#F1F2F4`/`#212326`。
- 文本选择色 `#A6D2FF`/`#214283`；终端选区/复制（Ctrl+Shift+C/Ctrl+Insert），快捷键纯函数测试 10/10 + 正常入口 E2E。
- Unified Diff 上下文行号修复：live unified 模板输出 old/new 两列，正常入口实采 `1/1`…`5/5`；`test-diff-unified-line-numbers.mjs` 4/4 PASS。
- Blame 未提交行修复：全零 hash / `Not Committed Yet` 行渲染空白归属槽、保留行号，不显示提示/点击；`test-blame-uncommitted.mjs` 5/5 PASS，offline 110 PASS，正常入口 PID 3084 实采。
- Stash 两复选同行修复：`.stash-check-row` 跨两列、flex 同行，Include untracked 不再被折叠；`test-stash-check-row.mjs` 5/5 PASS，offline 110 PASS，正常入口 PID 5464 实采。
- New Worktree 字段布局修复：`.worktree-new-branch-row` 跨两列、复选与名称输入同行，Directory 不再错位/截断；`test-worktree-check-row.mjs` 6/6 PASS，offline 110 PASS，正常入口 PID 14008 实采。
- 紧凑输入空名称修复：`showCompactInput()` 按名称同步确认按钮禁用态；New Branch/Rename/New Tag 空名不可提交，对齐参考 `OK` 禁用；`test-compact-input-empty.mjs` 7/7 PASS，正常入口 PID 3308 空态/填名复验。
- 设置 Appearance 左栏/控件/页顶；深色 accent `#3574F0`；设置 footer 伪影更正；设置开/关窗口级冒烟。
- Markdown/JSON/Git Log 页面级配对；图片棋盘格符合 §7.5；快速打开/全仓搜索 scoping。

## 5. 验证状态

- `test-terminal-copy-shortcut.mjs`：PASS 10/10。
- `verify-ux-offline-icons.cjs`：PASS 110 场景/主题（最后一次含 `::selection`/间隙断言）；第 46 批 Unified 行号修复后再次 PASS 110（`offline-icons-after-unified-fix/results.json`）。
- `verify-ux-dark-accent.cjs`：PASS。
- `verify-ui-assets.ps1`、`check-doc-claims.cjs`、`git diff --check`：PASS。
- `verify-ux-frame-buttons.cjs`：仍有旧期望/激活工具轨边框失败，不记通过。

## 6. 未闭环（不得当作完成）

- 54 页其余页面/全部模态状态；像素合格仍 0。
- Commit 页面已到达并记第 6 个页面级配对（此前“未到达”仅指当时 Ctrl+K/坐标路径）。
- 设置首内容/白闪/慢开/500ms 超时分支、无 CDP 偶发退出；当前无 CDP 两个样本：gdi presented 645.0ms/close 44.0ms、ddagrab 808.0ms/66.6ms（采集负载）；gdi 录像的 3 帧内容空白在既有 repeat 两次开窗与 ddagrab 151 帧均未复现，判采集伪影；全局无闪仍未证明。
- 终端状态栏/右侧栏/ANSI/深色首行/命令块；Markdown 源码高亮/标签模型；JSON 语法高亮/工具栏；图片工具栏/标签；Git Log 图/列/详情；Commit 的 1 modified/占位/工具栏差异；参考 Find in Files 焦点未建立。
- 跨屏 DPI 未验证；Windows 10 排除；阴影仍为已知差异，不称通过。

## 7. 下一步优先级

1. 设置首内容/白闪测量（正常入口，明确采集负载与首帧）。
2. Commit/Changes 页面可达性；Git Log 图/列；终端状态栏/右侧栏/ANSI。
3. 继续下一保留页面，维持“页面级配对/像素合格”双计数，不把已知差异混成像素通过。
