# 跨工作区标签会话与已不存在文件的幽灵标签整改（2026-11-25）

## 1. 结论

用户报告：工作区 `ops-server-report-resend` 中的 `global.json` 标签显示「This file cannot be previewed in Augit / global.json · Unknown File / The file was deleted, renamed, or replaced.」，而旁边 `package.json` 正常；项目树中**并没有** `global.json`。按用户 2026-11-25 指令「举一反三、参考本机 PyCharm 实机及 D:/github/intellij-community 风格」逐层复现后，确认这是三类同源缺陷：

1. **跨工作区标签会话泄漏（主因）**：标签会话、活动文件与目录展开层级是全局设置，切换工作区时 `SettingsStore.RememberWorkspaceAsync` 只更新 `LastWorkspace`、没有清空旧会话。真实启动顺序又是 `Program` 先记住新工作区、窗口后读取设置，于是 `ShellBridge.ReadSettingsAsync` 的同工作区门控被绕过：新窗口把**上一个工作区**的 `openFiles` 按相对路径恢复成当前仓库的文件。已不存在的 `global.json` 被建为 `Missing` 信息页标签，同名文件则错误显示旧仓库内容；随后 `persistSession` 又把它写回磁盘，重启后永久保留。
2. **会话恢复/显式打开把 `Missing` 当普通文档**：`ReadOnlyDocumentService` 对已删除文件返回的是正常结果载荷 `DocumentReadStatus.Missing`（不是异常）；网页层 `openDocument` 对任何载荷都走 `openDocumentTab`，于是新建 `file-limit` 幽灵标签。启动恢复路径应当跳过它，显式打开路径应当只说明原因、不建标签。
3. **变化批次只重读当前文档**：外部变化批次只对 `currentPath` 做删除判断和重读；后台已打开文档即使出现在同一批变化里也不会刷新，可能继续显示已被替换/删除的旧内容。

本批修复后：打开另一个工作区不再恢复旧仓库标签（无幽灵标签、无错误同名内容）；同一工作区恢复时跳过已不存在文件并激活首个仍存在的标签；显式打开不存在文件只在右下提示区说明原因；变化批次中命中的后台文档会在后台重读并更新为删除状态页（按 `docs/ux-spec.md` §7.2）。

## 2. 环境与证据身份

| 项 | 值 |
| --- | --- |
| 系统 | Windows 11 家庭中文版 `10.0.26200` x64 |
| 显示 | 单屏 2880×1800，200% 缩放 |
| 运行时 | .NET SDK `10.0.201`，WebView2 Runtime `158.0.4370.33` |
| 参考 IDE | 本机 PyCharm `2026.2.1`（build `262.9437.214`，独立 reference 配置） |
| 参考源码 | `D:/github/intellij-community`（`FileEditorManagerImpl.kt` 的 `beforeFileDeletion`） |
| 构建 | `dotnet build Augit.slnx --no-restore`，0 警告 0 错误 |
| 证据目录 | `artifacts/session-workspace-isolation-2026-11-25/` |

整改后构建身份（见各 `result.json` / 本目录后续补充的哈希）：

- `Augit.dll` 与 `Augit.exe` 程序集身份、`web/src/live-data.js` 源码哈希在最终验证后记录于 §6.3；
- 证据运行使用真实宿主与真实网页，不依赖场景桩或视觉稿参数。

## 3. 复现

### 3.1 真实入口：从工作区 A 打开工作区 B

为复现真实启动顺序（非隔离实例才执行 `RememberOpenedWorkspace`），使用真实 `%LOCALAPPDATA%\Augit\settings.json`：先备份并记录 SHA-256，写入工作区 A 的会话（`global.json`、`package.json`、`src/a.txt`，活动文件 `global.json`），再以 `--workspace B` 启动窗口，结束后恢复原设置并核对哈希完全一致。

工作区构造：

- A：`global.json`、`package.json`、`src/a.txt`；
- B：仅 `package.json`（Git 仓库），另外在真实宿主运行期间被用户/分支切换删除的同名文件场景由后续用例覆盖。

整改前状态（`before-cross-workspace/result.json`）：

```json
{
  "workspaceName": "workspace-b",
  "tabs": [
    {"path":"global.json","editor":"file-limit","status":"Missing","typeName":"Unknown file","message":"The file was deleted, renamed, or replaced."},
    {"path":"package.json","editor":"json","status":"TextReady","typeName":"JSON text"},
    {"path":"src/a.txt","editor":"file-limit","status":"Missing","typeName":"Unknown file"}
  ],
  "activePath": "global.json",
  "documentPath": "global.json",
  "tree": ["", "package.json"]
}
```

同时 `before-cross-workspace/settings-after-start.json` 记录了两个决定性事实：`lastWorkspace` 已变为 B，但 `openFiles` 仍是 A 的三项——`settings/read` 的 `sameWorkspace` 因此为真，门控失效。界面见 `before-cross-workspace/window.png`。

整改后同一操作（`after-cross-workspace/result.json`）为 `tabs: []`，项目树只有 B 的 `package.json`；`after-cross-workspace/settings-after-start.json` 中 `openFiles: []`，且 `toolWindows.workspaceRoot` 固定为旧工作区 A。界面见 `after-cross-workspace/window.png`。

### 3.2 同一工作区：会话里的文件已不存在

隔离实例（显式 `--settings-file` / `--user-data-folder`，不读写用户设置）：

- 设置 `lastWorkspace = workspace-b`，`openFiles = ["global.json", "package.json"]`，`activeFile = "global.json"`；
- B 中只有 `package.json`（另有恢复后用于后台删除测试的 `README.md`、`worker.js`）。

`same-workspace-missing/result.json` 六项检查全部为真：

- `noGhostOnRestore`：恢复完成后没有 `Missing` 标签；
- `activeFallback`：上次活动文件不存在时激活首个仍存在的 `package.json`；
- `sameTabsAfterExplicitMissing`：显式打开 `global.json` 后标签集合不变；
- `explicitMissingToast`：右下提示 `Open File / The file was deleted, renamed, or replaced.`；
- `backgroundDeletedPage`：后台标签 `README.md` 被外部删除后，标签仍在但正文更新为删除状态页（`Missing` + `file-limit`）；
- `activeKept`：后台删除不抢占当前 `worker.js`。

对应截图：`same-workspace-missing/1-restored.png`、`2-explicit-missing.png`、`3-background-deleted.png`。

### 3.3 PyCharm 2026.2.1 参考实机

使用 `D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe`、`reference-2026.2.1.properties` 隔离配置和 fixture：在 fixture 临时加入 `global.json` / `package.json`，以 `pycharm64.exe <fixture> <fixture>/global.json` 启动真实 IDE，打开 `global.json` 后从文件系统删除它并用 `Ctrl+Alt+Y`（Synchronize）触发 VFS 刷新：

- 删除前（`pycharm-reference/1-before-delete.png`）：标签栏有 `README.md`、`global.json`，`global.json` 正文为 JSON；
- 删除后（`pycharm-reference/2-after-delete.png`）：`global.json` 标签与项目树条目都消失，活动标签落到 `README.md`。

源码依据：`D:/github/intellij-community/platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/FileEditorManagerImpl.kt:2116-2123` 的 `beforeFileDeletion` 对删除路径下所有已打开文件执行 `closeFile(..., moveFocus=true, closeAllCopies=true)`。PyCharm fixture 仅临时新增两个未跟踪文件，截图后已删除；`git status` 与批前一致（`M app.py`、`?? .idea/` 为既有状态）。

## 4. 根因

### 4.1 设置层：切换工作区未清空旧会话

`SettingsStore.RememberWorkspaceAsync` 只合并 `LastWorkspace` 与 `RecentWorkspaces`。`Program.RememberOpenedWorkspace` 在窗口创建前调用它，因此窗口读取设置时 `LastWorkspace` 已等于新工作区，`ShellBridge.ReadSettingsAsync` 中 `sameWorkspace` 判断为真，旧 `OpenFiles` 被原样下发。此前同类问题（工具窗口面板尺寸）已用 `ToolWindows.WorkspaceRoot` 单独归属解决，标签会话却没有对应归属。

### 4.2 网页层：`Missing` 载荷仍建立标签

真实宿主对已删除路径返回 `status: "Missing"` 的完整载荷；`openDocument` 成功分支只做字段校验后调用 `openDocumentTab`，标签因此以 `editor: "file-limit"` 出现，标题后缀为分类器的「未知文件」。`restoreSession` 逐个读取恢复集合，于是幽灵标签被建立并被 `persistSession` 写回 `openFiles`。

### 4.3 变化批次：只处理当前文档

`applyWorkspaceChanges` 对变化文件只判断是否命中 `currentPath`；未命中的已打开后台文档既不判删除也不重读。当前文档的删除判断依赖 Git 状态 `Deleted`，但分支切换后文件已不在工作区时，状态列表未必给出该项，于是回落到“重新读取”并同样触发 4.2 的幽灵标签。

## 5. 实现

1. **宿主设置层**（`src/Augit.Infrastructure/Settings/SettingsStore.cs`）：`RememberWorkspaceAsync` 检测到 `LastWorkspace` 真正变化时，清空 `OpenFiles`、`ActiveFile`、`ExpandedDirectories`，并把旧面板尺寸的 `ToolWindows.WorkspaceRoot` 固定到旧工作区；同一工作区重复记录只更新最近列表，不清会话。这样即使真实启动顺序先改 `LastWorkspace`，`settings/read` 也不会把旧仓库文件下发。
2. **网页恢复与打开**（`web/src/live-data.js`）：
   - `openDocument` 收到 `status === "Missing"` 且当前没有该路径的标签时，不创建标签：启动恢复静默跳过，显式打开显示右下 `Open File` 错误提示并主动刷新文件树；已有标签则继续走 `openDocumentTab` 更新为删除状态页（`docs/ux-spec.md` §7.2）。
   - 已处于 `Missing` 状态的活动标签再次打开时允许重新读取（删除页不是终态，文件恢复后可回到正文）。
   - `restoreSession` 在指定活动文件不存在时激活恢复集合里第一个仍存在的标签，避免“有标签条但没有正文”。
   - `applyWorkspaceChanges` 在变化批次里额外重读命中的后台文档标签（不切换当前标签），使外部修改/删除不再只对当前文档生效。
3. **测试**：
   - `SettingsStoreTests` 新增「切换工作区时清空旧工作区标签会话并固定旧布局归属」；
   - `ShellBridgeSettingsTests` 新增真实启动顺序回归「启动记住新工作区后旧标签会话不会通过设置读取泄漏」；
   - `tools/audit/live-shell.spec.cjs` 新增 7 条断言：恢复跳过 `Missing`、活动文件回退、恢复项不写回会话、显式打开不建标签并报原因、已打开文档删除后保留删除状态页、文件恢复后可重读。

## 6. 验证

### 6.1 自动化

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build Augit.slnx --no-restore` | 0 警告 0 错误 |
| 基础设施测试 | `dotnet test tests/Augit.Infrastructure.Tests --no-build --no-restore` | 223/223 通过 |
| 外壳测试 | `dotnet test tests/Augit.Shell.Tests --no-build --no-restore` | 286/286 通过 |
| 新增网页断言 | 以临时定向入口运行 `live-shell.spec.cjs` 的新增段落（临时副本不提交） | 898 项通过（含 `live-layout` 与 7 条新断言） |

当前开发环境的本机 Playwright 浏览器修订与仓库记录的运行方式不一致（捆绑模块要求 chromium-1234，本机只有 1208/1228/1243），且完整 `live-shell.spec.cjs` 在字体族与分支弹层两处存在与本批无关的既有失败（字体固定裁决后的旧断言、环境点击超时）。本批用定向入口和真实宿主分别覆盖新增行为；完整套件仍需在正常验收环境复跑。

### 6.2 真实宿主

| 场景 | 结果 |
| --- | --- |
| A → B 打开工作区 | 无 `global.json`/`src/a.txt` 幽灵标签；`package.json` 不再被误恢复成旧仓库同名文件 |
| B 同工作区恢复（含已缺失活动文件） | 只恢复 `package.json`，并激活它 |
| 显式打开不存在文件 | 不建标签；右下提示 `Open File / The file was deleted, renamed, or replaced.` |
| 后台标签被外部删除 | 标签保留，正文更新为删除状态页；当前标签不变 |

### 6.3 身份与清理

- 用户设置 SHA-256 在真实入口复现前后均为 `0D5315801AC8402DE0DC335E9805A751317979DF861BB5F229A3632B748A5F9F`（备份并恢复，见 `.tmp` 过程记录，不提交）；
- 临时隔离设置、WebView2 数据目录、PyCharm fixture 临时文件均已在取证后清理；
- 最终构建的 `Augit.dll`、`Augit.exe`、`web/src/live-data.js` 哈希见 `artifacts/session-workspace-isolation-2026-11-25/README.md`。

## 7. 与参考实现的差异及残留

- **已打开文件被删除**：PyCharm 在 VFS 删除事件后关闭该文件标签；Augit 依据 `docs/ux-spec.md` §7.2 保留标签并把正文替换为删除状态页。本次只把「恢复/显式打开/后台变化」三条路径补齐到不出现幽灵标签和旧内容，未改变 §7.2 的删除页语义。
- **分支切换后的后台标签**：变化批次只重读批次中出现路径的后台文档；若 Git 元数据变化没有伴随文件系统事件，后台标签仍可能保留旧内容直到其路径再次变化或被选中后重新读取。这个边界与现有「按变化批次局部刷新」设计一致，未在本批扩大为全量轮询。
- **删除页文案**：`Missing` 载荷仍由通用不可预览信息页承载，标题后缀为「未知文件」（英文界面 `Unknown file`）。分类器文案与删除原因分属两层，本批没有新增 UI 文案，避免与现有视觉稿不一致。
