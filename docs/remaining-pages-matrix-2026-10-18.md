# 保留页面覆盖矩阵（2026-10-18）

> 本表按 `docs/ux-spec.md` §12 的页面清单对照当前证据；只做静态盘点，不新增配对，不替代正常入口截图。当前页面级同态配对 **50**、像素合格 **0**。2026-10-20 用户裁决、验收类别和已批准例外见[验收范围与裁决](acceptance-scope-rulings-2026-10-20.md)；本表状态不等于整页像素通过。2026-10-22 首组同态整窗（Project Files 视图 + README 预览，2880×1716/DPI168）原始差异 **15.41%**；第二组修复 Markdown 代码块底色与项目树根行后为 **11.0175%**；第三组在参考提交/根行同选中并重整 Git 三列后为 **10.3752%**，仍未通过；协议见[整窗同态像素配对](pixel-pair-protocol-2026-10-22.md)，第二/三组见[第二组](pixel-pair-2-2026-10-22.md)、[第三组](pixel-pair-3-commit-selected-2026-10-22.md)。

| # | 页面/状态 | 状态 | 证据 | 下一步 |
| --- | --- | --- | --- | --- |
| 1 | 打开工作区 | 部分（参考 Welcome 已采，语义状态） | [Open Workspace 参考采集](open-workspace-reference-2026-10-19.md) | 实际 `Open Workspace` 对话框 vs 参考无项目 `WelcomeScreen`；共享最近项目/打开/克隆语义，但上下文/结构不同，暂不计页面级；补同上下文状态或实际 Choose Directory 动作 |
| 2 | 初始化仓库 | 配对 | [初始化仓库配对](init-repo-homologous-pair-2026-10-18.md) | 执行初始化后状态、Choose Directory、已存在 .git、深色 |
| 3 | 项目 | 配对 | [首个配对](homologous-main-text-pair-2026-10-18.md)（含 Project 面板）、[文件状态表达](markdown-typography-2026-10-22.md)、[第二组整窗](pixel-pair-2-2026-10-22.md) | 文件状态已改为参考纯颜色、无行尾徽标；根行已改为完整路径与常规字重；树文字仍低 1–2 px、树宽差 3 px，面板几何继续收窄 |
| 4 | 项目右键菜单 | 配对（文件行+根节点） | [项目文件右键菜单配对](project-file-context-homologous-pair-2026-10-18.md)、[根节点右键配对+修复](project-root-context-homologous-pair-2026-10-18.md) | 目录节点/菜单动作未测 |
| 5 | 普通文本 | 配对 | [首个配对](homologous-main-text-pair-2026-10-18.md) | 标签/工具栏/几何已知差异 |
| 6 | Markdown | 配对 | [Markdown 配对](markdown-homologous-pair-2026-10-18.md)、[参考排版实现与实测](markdown-typography-2026-10-22.md)、[第二组整窗](pixel-pair-2-2026-10-22.md) | 已按参考实机 13px 实现：h1/正文/代码垂直带与参考对齐；代码块底色已对齐 #F4F7F9；代码字族回退宽度差 16 物理 px；整窗第二组 11.0175%，未通过 |
| 7 | JSON | 配对 | [JSON 配对](json-homologous-pair-2026-10-18.md) | 语法高亮/工具栏 |
| 8 | 图片 | 配对 | [有效图片配对](image-valid-homologous-pair-2026-10-18.md) | 默认缩放按 §7.5 不放大，不改 |
| 9 | 不可预览文件 | 配对（大文件预览）/部分 | [大文件预览配对](large-file-limit-homologous-pair-2026-10-18.md)、[Warning 真实输入闭环](large-warning-real-input-2026-10-19.md)、[二进制 Diff 复核](binary-diff-reference-2026-10-19.md) | 实际 Warning 显示与两个真实鼠标动作已闭环；参考同文件无 Warning，故无参考动作可配；二进制摘要页产品专有差异（参考 `.bin` 未知类型/`.zip` 归档页），图片大文件仍未覆盖 |
| 10 | Commit | 配对 | [Commit 配对](commit-homologous-pair-2026-10-18.md) | 标签/占位/图标/底部 Log |
| 11 | Commit 空态 | 配对 | [Commit 空态配对](commit-empty-homologous-pair-2026-10-18.md) | 空态呈现/Unversioned/Log/通知差异 |
| 12 | Changes 右键菜单 | 配对 | [Changes 菜单配对](changes-context-menu-homologous-pair-2026-10-18.md) | 动作子集 vs 完整菜单 |
| 13 | 工作区 diff | 配对 | [Diff 配对](diff-homologous-pair-2026-10-18.md) | 行尾/工具栏/编辑铅笔 |
| 14 | Diff 局部加载 | 部分（上限状态级配对；截断提示已修；加载形态已按参考源码整改） | [Diff 上限参考态](diff-limit-reference-2026-10-19.md)、[截断提示修复](diff-truncation-notice-fix-2026-10-19.md)、[加载形态整改](diff-loading-form-2026-10-20.md) | 25.38MB 两端拒绝正文；8.74MB/92001 行实际只回传前 2000 行，现已显示 `Truncated` + 边界说明，参考完整显示 1 difference。加载态已改为头部不定进度条 + 正文 `Loading...`（源码形态，非像素）；仍需参考实机加载态像素、实时瞬态截图与重试状态 |
| 15 | Git 历史 | 配对 | [Git Log 配对](git-log-pair-scoping-2026-10-18.md)、[第三组整窗](pixel-pair-3-commit-selected-2026-10-22.md) | 引用/详情列宽已按参考对齐（940/2249）；Console、额外工具图标、详情排版与状态栏文件上下文仍差 |
| 16 | Git 历史右键菜单 | 配对 | [提交菜单配对](git-log-context-menu-homologous-pair-2026-10-18.md) | 实际动作子集 |
| 17 | 文件历史 | 配对 | [File History 配对](file-history-homologous-pair-2026-10-18.md) | Revision/头/Console/预览宽度 |
| 18 | Blame | 配对 | [Blame 配对](blame-homologous-pair-2026-10-18.md) | inlay/Console/几何；未提交行已修 |
| 19 | 引用比较 | 配对 | [Compare 配对](compare-workspace-homologous-pair-2026-10-18.md) | 标签/头/行尾措辞/工具栏 |
| 20 | 分支与标签 | 配对 | [分支弹层配对](branches-homologous-pair-2026-10-18.md) | 行菜单/快捷键/搜索措辞/几何 |
| 21 | Smart Checkout | 配对 | [Smart Checkout 配对与修复](smart-checkout-homologous-pair-2026-10-19.md) | 实际/参考覆盖风险对话框同态；实际动作成功、两端取消 no-op；说明截断已修；Force Checkout/文案/几何差异保留 |
| 22 | 创建 Stash | 配对 | [Stash 配对](stash-homologous-pair-2026-10-18.md) | Git root/Include/历史/尺寸；复选同行已修 |
| 23 | Stash 管理 | 配对 | [Stash Manager 同态配对](stash-manager-homologous-pair-2026-10-19.md) | 同一 stash/1 文件；实际对话框 vs 参考 Git 工具窗 Stash 标签；Apply/Pop 入口一致；View Content/Delete/尺寸差异 |
| 24 | Reset | 配对 | [Reset 配对](reset-dialog-homologous-pair-2026-10-18.md) | 四档单选/标题/头/尺寸 |
| 25 | Rollback | 配对 | [Rollback 配对](rollback-changes-homologous-pair-2026-10-18.md) | 多文件树/Delete local copies/尺寸 |
| 26 | Worktree | 配对 | [New Worktree](new-worktree-dialog-homologous-pair-2026-10-18.md)、[Worktrees 管理](worktrees-manager-homologous-pair-2026-10-18.md) | 字段已修；Branch/Project name/形态差异 |
| 27 | 远端 | 配对 | [Git Remotes 配对](git-remotes-homologous-pair-2026-10-18.md)、[Define Remote 配对](define-remote-homologous-pair-2026-10-18.md) | 编辑/删除远端状态 |
| 28 | Clone | 配对 | [Clone 配对](clone-dialog-homologous-pair-2026-10-18.md) | provider 列表/标签/默认目录 |
| 29 | Push | 配对 | [Push 无 upstream](push-dialog-homologous-pair-2026-10-18.md)、[Push 有 upstream](push-upstream-homologous-pair-2026-10-18.md)、[Push 待推提交](push-commit-homologous-pair-2026-10-18.md)、[Push 被拒](push-rejected-homologous-pair-2026-10-18.md) | 成功结果无独立页面 |
| 30 | Push 无远端 | 配对 | 同 Push 配对（无 upstream） | 已有远端/上游状态未采 |
| 31 | Git 操作进行中 | 部分（状态级配对） | [真实慢远端状态配对](git-progress-hanging-remote-2026-10-19.md)、[参考取消复核](git-cancel-reference-2026-10-19.md) | 同一 1 提交推送，实际对话框 `Pushing...` / 参考状态栏 `Pushing...`；两端取消均成功（参考首次为坐标误点，已按正确坐标复核）；网络失败状态已采；结果 balloon/结果页仍未闭环 |
| 32 | 冲突列表 | 配对 | [冲突列表配对](conflict-list-homologous-pair-2026-10-18.md) | 解决后 Continue/Abort、多冲突文件、深色 |
| 33 | 快速打开 | 配对（结果） | [Quick Open 结果配对](quick-open-results-homologous-pair-2026-10-18.md)、[快速打开 scoping](quick-open-pair-scoping-2026-10-18.md) | 选择结果打开、类/符号/动作搜索 |
| 34 | 快速打开空态 | 配对 | [Quick Open 空态配对](quick-open-empty-homologous-pair-2026-10-18.md) | 实际 Open File vs 参考 Search Everywhere；Ctrl+P 需 WebView 焦点 |
| 35 | 全仓搜索 | 配对（空态+结果+零结果） | [Find in Files 空态](search-empty-homologous-pair-2026-10-18.md)、[结果](search-results-homologous-pair-2026-10-18.md)、[零结果](search-no-results-homologous-pair-2026-10-18.md)、[全仓搜索 scoping](full-search-pair-scoping-2026-10-18.md) | 正则/忽略文件；实际结果含 .idea 差异待按规则核对；截断与超时另项 |
| 36 | 搜索截断与超时 | 配对（截断）；超时/取消状态级已验证 | [搜索截断配对](search-truncation-homologous-pair-2026-10-18.md)、[超时与取消实测](search-timeout-cancel-2026-10-20.md) | 正常入口受控停顿已真实触发 15 秒超时：已完成结果保留 + 超时说明 + rg 结束；关闭浮层/改发新查询的取消缺陷已修复并复验。页面级仍 50、像素仍 0；超时整页像素、重试、深色、窄窗未覆盖 |
| 37 | 终端 | 配对（深色纵向/浅色 ANSI 已对齐） | [终端配对](terminal-homologous-pair-2026-10-18.md)（含深色/选区）、[深色面板/首行对齐](terminal-dark-panel-offset-2026-10-19.md)、[ANSI 蓝修复](terminal-ansi-blue-palette-fix-2026-10-19.md) | 深色 header 1176/首行 1187 与参考一致（二维配准 dx -1/dy -1）；浅色 ANSI Blue #0060AF；右侧 42 CSS 工具轨是结构差异；其他 ANSI 索引/关闭运行中仍待测 |
| 38 | 关闭运行中终端 | 配对 | [关闭终端配对](close-terminal-homologous-pair-2026-10-18.md) | Stop/Terminate 后的实际终止、Don't ask again 持久化 |
| 39 | 设置 | 配对（Appearance/Git/Terminal/File View） | [Settings/Appearance](settings-appearance-homologous-pair-2026-10-18.md)、[Settings/Git](settings-git-homologous-pair-2026-10-18.md)、[Settings/Terminal](settings-terminal-homologous-pair-2026-10-18.md)、[Settings/File View](settings-fileview-homologous-pair-2026-10-18.md)、[设置 scoping](settings-pair-scoping-2026-10-18.md)、[原生长时间线](settings-native-timeline-2026-10-20.md)、[深色首内容与关闭路径](settings-dark-first-content-2026-10-22.md) | 简化功能集合已批准；2026-10-20 正常入口 presented 495–585ms、Esc 关闭 32–60ms、presented 首帧完整、3 次快速点击仍 1 窗；2026-10-22 深色 3 周期点击→presented 492–538ms、Cancel/OK/标题关闭均 0 残留、60fps 首帧与稳定帧逐像素相同；2904ms、capture 超时、跨屏与长时间重复未覆盖；像素未合格 |
| 40 | Git 不可用 | 配对 | [Git 不可用配对](git-unavailable-homologous-pair-2026-10-18.md) | 修复路径后恢复、不同错误、深色 |
| 41 | Git 操作结果 | 部分（状态级；成功/失败通知均已按参考 balloon 形态整改） | [Git 操作结果配对与修复](git-result-homologous-pair-2026-10-18.md)、[Push 网络失败](git-push-failure-2026-10-19.md)、[balloon 形态整改](git-result-balloon-2026-10-20.md)、[Push 失败通知整改](push-failure-balloon-2026-10-22.md) | 成功通知为图标/粗体标题/正文/蓝色动作，正常入口实测 `Push` + `Pushed to origin/master.`；失败已按参考关闭对话框并显示 `Push failed` balloon（502 正常入口实测），`Show details` 重开保留错误、列表与重试；fetch/取消结果仍未闭环；页面级仍 50、像素 0 |

## 结论

- 本表对 §12 的 41 项逐一标注：**未完成 5 项**（不可预览文件—**大文件预览已配对、实际 Warning 显示/两个真实动作已闭环、参考同文件无 Warning、二进制摘要产品差异**、Diff 局部加载—**上限状态级已配对、加载形态已按参考源码整改（非像素）、参考实机加载态/重试未完成**、Git 操作进行中—**进行中状态级已配对、两端取消均已复核通过、失败状态级已配对、结果 balloon/结果页未完成**、搜索截断/超时—**截断已配对、超时与取消状态级已验证（受控停顿、rg 结束、取消缺陷已修）**、Git 操作结果—**成功/网络失败状态级已配对、页面未闭环**）、**scoping 0 项**（Open Workspace 参考已采但只登记语义状态；Stash Manager 已配对）。Smart Checkout 已配对；项目文件行与根节点右键均已配对；目录节点/菜单动作未测；全仓搜索空态/结果/零结果/截断、Quick Open 空态/结果、Git 不可用、初始化仓库、关闭运行中终端已配对。
- 页面级配对总数 **50**，覆盖本表项目/项目文件右键/项目根节点右键/初始化仓库/文本/Markdown/JSON/图片/大文件只读预览/Commit/Commit 空态/Changes 菜单/Diff/Git Log/菜单/文件历史/Blame/Compare/分支/Stash/Reset/Rollback/Worktree/远端 Git Remotes+Define Remote/Clone/Push（无 upstream+有 upstream+待推提交+被拒）/冲突列表/Smart Checkout/Stash Manager/Settings Appearance+Git+Terminal+File View/Find in Files 空态+结果+零结果+截断/Quick Open 空态+结果/Git 不可用/关闭运行中终端/终端，另含表中未单列的 Checkout/New Branch/New Tag/Worktrees 管理/损坏图片错误页等模态子页。41 项不是 54 页全量分母。
- **不应把本表的状态当作 41/41 或 54/54 完成**；未完成项需要正常入口证据。
- 输入桌面在 2026-10-19 08:31 恢复后已补 Commit 空态、Git Remotes、Push 状态、冲突列表和项目文件右键；2026-10-19 晚补大文件 Warning 真实输入/重开/重启闭环（参考同文件无 Warning）、Diff 上限参考态（状态级，不增页面数）、Smart Checkout 页面/动作/说明截断修复（页面级 48→49）、Git 进行中真实慢远端状态、Push 网络 502 失败状态（状态级，均不增页面数）、Open Workspace 参考 Welcome（语义状态，不增页面数）与 Stash Manager 页面级配对（49→50）。并补 Diff 2000 行截断提示（工具栏 `Truncated`、正文边界说明，参考同文件完整 1 difference）。后续优先 Diff 真正局部加载/重试、Git 结果页、搜索超时。
