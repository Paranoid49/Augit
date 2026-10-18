# 正常入口界面英文审计（2026-10-18）

## 目的

验证产品“界面可见文案固定英文”的当前正常入口状态，重点覆盖主界面、只读文档、Changes/Diff、终端、设置和保留模态。审计只读，不修改产品。

## 方法

- 正常入口 `tools/start-augit.ps1` 强制非增量构建，临时英文夹具仓库和临时设置副本，附 CDP 驱动。
- 夹具文件、目录、仓库名和提交信息全部使用英文，避免把用户数据误判为界面文案。
- 扫描每个状态中可见元素的直接文本、`title` 和 `aria-label`，使用 CJK 正则 `[\u3400-\u9fff]`。
- 排除正文 `.code-view` 和终端 `.xterm` 原始输出；用户数据按产品规则不翻译。
- 覆盖 20 个状态：main、text-viewer、changes、diff、terminal、settings-appearance、settings-file-view、settings-git、settings-terminal、settings-save-failure、go-to-line、stash-create、stash-manager、worktree-manager、**worktree-create-empty**、**branch-fetch-no-remote**、push、remote、**remote-name-empty**、**remote-url-empty**。
- 工具：`tools/audit/ui-english-audit.cjs`。

## 结果

终端 `Local` + 悬停当前 Shell 裁决后的当前构建复验：

- 浅色 PID 27264、端口 55723；深色 PID 12796、端口 62816；各 20 状态，CJK 发现数均为 **0**，两轮负向对照均检出 1 项。
- 新增 `branch-fetch-no-remote`：无远程夹具触发真实 `git/fetch` 失败，失败提示为英文。
- 新增 `remote-name-empty`：Define Remote 清空名称后点击 OK，显示 `Remote name cannot be empty`，取消不写入。
- 新增 `remote-url-empty`：名称填 `origin`、URL 留空后点击 OK，显示 `Remote URL cannot be empty`，取消不写入。
- 新增 `worktree-create-empty`：New Worktree 空字段点击 Create，显示英文校验，取消不创建。
- 两次 `CloseMainWindow` 成功，临时目录删除。
- 证据：`artifacts/pixel-followup-2026-10-18/ui-english-audit-16/`、`ui-english-audit-dark-7/`。
- 以下 6296/3960 为标签变更前的历史整轮记录，保留不删。

浅色运行（历史 16 状态）：

- 正常入口 PID 6296、端口 52976，强制构建；16 个状态全部扫描完成。
- **CJK 发现数 0**：没有可见中文界面文案；`title`/`aria-label` 也未发现中文。
- 设置持久化失败态用真实不可写路径触发，失败提示同样为英文。
- 负向对照：向状态栏注入一个可见中文节点后，扫描器检出 1 项；证明“0 发现”不是扫描器失效。
- 页面错误、控制台错误和失败请求均为空；`CloseMainWindow` 成功，端口关闭，临时目录删除。
- 证据：`artifacts/pixel-followup-2026-10-18/ui-english-audit-7/`（`report.json`、`cleanup.json`）。

深色运行（历史 16 状态）：

- 同一 16 状态、同一英文夹具，`theme=Dark`；PID 3960、端口 53709。
- **CJK 发现数 0**，负向对照检出 1 项；页面错误为空；`CloseMainWindow` 成功，临时目录删除。
- 证据：`artifacts/pixel-followup-2026-10-18/ui-english-audit-dark-2/`。
- 历史 16 状态两轮 32 个；17 状态两轮 34 个；18 状态两轮 36 个；19 状态两轮 38 个；当前 20 状态两轮合计 40 个。均未发现中文界面文案；仍不覆盖全部错误、危险确认、Reset/Clone 等状态。

## 边界

- 只覆盖上述 20 个正常入口状态，不覆盖所有错误、失败重试、危险确认、Reset/Clone 等全部模态子状态。
- 尝试补充 Reset 确认态时发现：Git Log 提交右键 `reset-here` 在实时菜单固定带 reason、渲染为禁用项，无法从该入口打开；主菜单入口未可靠到达。未取得 Reset 确认截图，未据此判产品缺陷。
- 冲突列表/只读 Diff 独立正常入口：PID17344/端口62807，真实 merge 冲突夹具；可见正文、`title`、`aria-label` 的 CJK 均为 0，`Continue Merge` 在冲突未解决时禁用。该两项不混入 18 状态计数。
- Rebase 冲突 Skip/Abort 独立正常入口：PID5140/端口50564；`Abort Rebase`、`Skip` 均为英文，正文/title/aria CJK 0；Skip 后 rebase 完成、工作区干净、日志包含后续提交，`CloseMainWindow` 成功。该两项也不混入 18 状态计数。
- Merge 冲突 Continue 独立正常入口：PID3172/端口52119；外部解决后列表清空、`Continue Merge` 可用，正文/title/aria CJK 0；点击 Continue 后对话框关闭，`CloseMainWindow` 成功，临时目录删除。该状态也不混入 18 状态计数。
- Merge 冲突 Abort 独立正常入口：PID19348/端口59750；`Abort Merge` 可用，CJK 0；点击 Abort 后对话框关闭，`CloseMainWindow` 成功，临时目录删除。
- 上述 20 状态浅深复验早于分支空名称校验修复；该新文案由独立正常入口回归覆盖，未回算进 20 状态，也不宣称旧构建覆盖新状态。
- 补充独立正常入口回归：Git Log → 提交右键 New Branch… → 空名称 Create 显示 `Name cannot be empty.`，`bad name` 显示 `Name cannot contain whitespace.`；New Tag… 空名称显示 `Name cannot be empty.`；均无中文；PID20524/端口64786。见[新建分支/标签名称校验](branch-create-validation-2026-10-18.md)。
- 终端原始输出、文件名、提交信息、分支名和路径属于用户数据，允许非英文；本审计用英文夹具排除了这类噪声。
- 该审计不替代像素配对、布局或交互验收，也不增加 54 页合格数。
- 发现的中文缺陷已在同一构建修复：Custom shell 标签现按用户裁决显示 `Local`，当前 Shell `Custom terminal` 放悬停 `title`/`aria-label`，见[终端标签记录](terminal-custom-label-2026-10-18.md)。

## 复验

```powershell
node tools/audit/ui-english-audit.cjs <playwright> artifacts/pixel-followup-2026-10-18/ui-english-audit-7
node tools/audit/ui-english-audit.cjs <playwright> artifacts/pixel-followup-2026-10-18/ui-english-audit-dark-2 --dark
```

脚本自行创建/清理临时夹具、实例和 profile；异常时只停止本批 PID。
