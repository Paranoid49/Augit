# 移除内置三栏冲突编辑器（2026-10-17）

## 结论与批准范围

用户于 2026-10-17 明确批准移除内置三栏冲突编辑器：保留冲突提示、冲突文件列表、普通只读 Diff，以及外部工具处理后的状态同步；既有 Continue、Skip、Abort 语义不变。不新增外部工具配置页，不接入 AI，不自动接受任意一侧，不自动标记已解决。

本批完成规范同步、代码移除和正常入口复验。三栏专属的编辑、草稿、逐块合并、整侧接受、保存和逐行像素验收登记为**经批准移出范围**，原未通过的历史测量保留，不改写为通过。

## 规范同步

- `product-spec.md`：删除“三栏结果区是唯一可编辑区”、三栏编辑与整侧接受条款；冲突改为状态、列表、普通只读 Diff、外部处理后的状态同步；等宽字体作用范围去掉冲突三栏；交付阶段改为“冲突处理（只读查看、外部工具解决和状态同步）”。
- `ux-spec.md`：§7.13 点击冲突文件改为普通只读 Diff；§7.14 重写为“冲突文件的只读查看与外部处理”；页面目录从 55 页调整为 54 页并登记 `conflict-resolver` 移出范围；原三栏运行时核对段落加范围变更说明。
- `architecture.md`：删除冲突结果写回、stage 2/3 三侧读取和结果保存校验；桥接只返回冲突状态、列表、操作类型和动作可用性。
- `design-system.md`：三栏冲突解决器小节及后续三栏记录登记为历史、移出范围；冲突文件沿用普通只读 Diff 的视觉规则。
- `docs/ui-compliance.md`：`conflict-resolver` 从 §1.1、§1.4、§1.6 和 `verify-acceptance.ps1` 场景分母移出；A 线 54、干净仓库真机巡检 53、PyCharm 未对照 16（9+2+4+1）。
- `docs/pixel-difference-report-2026-10-13.md`、`docs/pixel-pair-inventory-2026-10-14.md`：目录按保留范围调整为 54 页，`conflict-resolver` 登记为经批准移出范围。
- `docs/nui-behavior/08-diff-merge.md` §7bis：三栏渲染规则降为历史参考，不再约束当前实现或验收。
- `docs/ui-classification.md`：2.29 与逐页归类登记为移出范围，历史 E1 证据保留但不作为当前验收目标。
- `AGENTS.md`：同步“Augit 不提供任何内置文本编辑或保存”的通用规则。

## 代码移除

- `web/src/live-data.js`：删除 `conflict-draft.js` 导入、三栏视图、结果编辑、逐块接受、撤销重做、草稿与未保存询问、外部重载询问、整侧接受、`git/conflict-load/save/accept/words` 调用和解决器专属绑定；`openConflictFile` 改为打开普通只读 Diff；操作会话只渲染冲突列表与真实动作。新增 `operationSessionSuppressed`，查看只读 Diff 时不会自动弹回会话窗口。
- `web/src/mockup.js`（与 `docs/ux-mockups/mockup.js` 字节一致）：删除三栏文档渲染、冲突块/来源高亮、滚动同步、行号、工具栏、静态三栏视觉稿和解决器图标；新增只读 Diff 的冲突横幅 `liveConflictNotice()`，提示外部处理并提供“Show Conflicts”返回入口。
- `web/src/mockup.css`（与视觉稿一致）：新增 `.conflict-readonly-notice` 条带样式；原三栏未引用样式已在后续清理中删除。
- `web/src/conflict-draft.js`：删除。
- `docs/ux-mockups/conflict-resolver.html`：删除；目录页移除对应第 20 项并重新编号；`conflict-list` 场景行改为不可编辑的只读 Diff 入口提示。`web/index.html` 与 `web/settings-surface.html` 增加 `data:` 空图标声明，消除正常启动的 `favicon.ico` 缺失请求（正常入口复验无失败资源）。
- `src/Augit.Shell/ShellBridge.cs`：移除 `git/conflict-accept`、`git/conflict-load`、`git/conflict-words`、`git/conflict-save` 端点及四个实现；保留 `git/conflicts` 状态读取和 `git/operation`/`git/operation-action`。
- `src/Augit.Core/Git/GitConflictModels.cs`、`GitConflictRangeMap.cs`、`GitMergeWordDiff.cs`、`src/Augit.Infrastructure/Git/GitConflictService.cs`：删除；`GitConflictIndex.cs` 因操作状态读取仍被 `GitOperationService` 使用而保留。
- `IGitServices.cs`：删除 `IGitConflictService`。
- `web/src/bridge.js`：从“等待宿主结束”的写操作集合移除两个已删除的冲突写方法。
- 测试与审计：删除 `conflict-draft.test.cjs`、`conflict-save-lifecycle.test.cjs`、冲突 Core/Infrastructure/Shell 测试；`GitOperationServiceTests` 改为“外部写文件 + `git add`”解决冲突；`live-shell.spec.cjs` 删除三栏专属用例与桩；`coverage-generator.test.cjs`、`verify-acceptance.ps1` 同步 54/53/16。
- 三栏专属 `tools/verify-ux-conflict.cjs` 与 `mockup.css` 中 `.conflict-page/.conflict-line/.conflict-block/.conflict-columns` 等三栏样式已删除；只读冲突横幅 `.conflict-readonly-notice`、文件冲突状态和普通 Diff 样式保留。其余只针对三栏编辑器的 `verify-conflict-*.cjs`、`capture-conflict-reference-shell.cjs`、`conflict-daily-*.cjs`、`compare-conflict-*.py`、`probe-*conflict*` 等历史工具已一并删除；仅保留只读回归 `conflict-readonly.cjs` 与 `live-goal-conflict.cjs`。历史文档中的旧命令不再可执行，也不得把其结果当作当前证据。

## 正常入口证据

统一通过 `tools/start-augit.ps1` 强制非增量构建启动，复制真实设置、沿用默认 WebView2 profile 和系统缩放，不使用场景参数或测试专用条件。证据目录：`artifacts/pixel-followup-2026-10-17/conflict-readonly/`。

| 项 | 实测 |
| --- | --- |
| 启动 | `forcedBuild=true`，0 警告 / 0 错误；PID 25552，CDP 端口 57018（附 CDP） |
| 实际加载产物 | `Augit.dll` SHA256 `27D29A6558B2DF0F4FC5EB7DD9B7960EF8923B4EE1E45625DB5FA95B2C26944F`；`Augit.Infrastructure.dll` SHA256 `D95B2F3CE17FE45008212F2AA37E67A6269D16486F90EFD13DFAF9756D9F6AC3`；映射路径来自本实例（`read-process-mapped-images.py`） |
| 页面与缩放 | `https://augit.local/index.html?theme=light`，无 `scene` 参数；DPR 1.75 |
| 字体 | Diff 正文：`JetBrains Mono` 13px；界面：`Microsoft YaHei UI` |
| 冲突列表 | 标题 `Merge Conflicts`；1 行 `conflict.txt`；Abort 可用、Continue 禁用；三栏 0、保存 0、可编辑 0 |
| 只读 Diff | 会话窗口收起；`.diff-layout` 14 行正文；三栏 0、保存 0、逐块/整侧动作 0、可编辑 0；冲突横幅提示“does not provide a built-in merge editor” |
| 已移除端点 | `git/conflict-load/save/accept/words` 全部返回 `Unknown host method` |
| 外部解决同步 | 外部写入并 `git add` 后，冲突行清空、摘要 `No unresolved conflicts`、Continue 可用、三栏仍为 0 |
| 外部同步时延 | `git add` 完成到界面清空冲突并启用 Continue：267ms（附 CDP 单次样本；无 CDP 未计时） |
| 清理 | `CloseMainWindow` 未成功，按本次核实 PID 执行 `taskkill /PID 25552 /T /F`；端口 57018 无监听；临时仓库与设置副本已删除；本实例无残留进程。正常关窗本批未取得证据，仍属未完成项 |
| 运行期错误 | 无 `pageerror`、无 `console.error`、无失败资源请求；`window.__augitError` 为空，`window.__augitErrors` 为空（修复前发现的 `favicon.ico` 缺失已通过 `data:` 图标声明消除） |

补充：本实例仍映射本机 Nahimic 模块（`AudioDevProps2.dll`），仅作诊断记录，不推断因果，历史服务权限阻塞未解除。


### 三栏 CSS 与历史工具清理后复验

删除 `tools/verify-ux-conflict.cjs` 及 `mockup.css` 中 `.conflict-page/.conflict-line/.conflict-block/.conflict-columns` 等三栏未引用样式后，重新用正常入口冒烟（`normal-smoke-css-cleanup/`）：PID 9712、端口 53732、附 CDP、强制非增量构建；实际映射 `Augit.dll` `27D29A65…2C26944F`、`Augit.Infrastructure.dll` `2CCA339F…AF8190F1F`。纯文本只读、JetBrains Mono 13、终端输出、Changes→只读 Diff 均通过；无 `pageerror`/`console.error`/失败资源；`CloseMainWindow` 成功、端口关闭、临时目录删除。共享 `mockup.css` 与视觉稿仍字节一致（当前 MD5 `5857AFF257DFA4EADAF9984D636C2C2D`、SHA256 `C45AA643BA3A8D7E6048B7CD40D6B2EF16F4E27A3B274A8D55038BFAADA09C2E`）。该冒烟不重新证明冲突只读 Diff 的全部行为，冲突专项证据仍以本文件上表为准。

### 当前构建的只读冲突回归（清理后）

`conflict-readonly.cjs` 在当前 Shell `27D29A65…2C26944F`、Infrastructure `2CCA339F…AF8190F1F` 上重新跑正常入口（PID 28572、端口 60423，强制非增量构建、附 CDP）：

- 冲突列表 1 行 `conflict.txt`，Abort 可用、Continue 禁用；三栏/保存/可编辑均为 0。
- 打开为普通只读 Diff：14 行正文、无三栏/保存/逐块动作，横幅明确“不提供内置合并编辑器”。
- `git/conflict-load/save/accept/words` 均返回 `Unknown host method`。
- 外部写文件并 `git add` 后，列表清空、摘要 `No unresolved conflicts`、Continue 可用，同步 **411ms**（单次样本）。
- 默认只读路径不点击 Abort/Continue，合并操作仍在进行，对话框只提供 Abort/Continue；此时主窗被模态挡住，审计只能 `taskkill`，不能冒充正常关窗。
- 补充 `--abort-close` 模式（同一临时仓库、正常入口 PID 25200、端口 56124）：外部同步 **240ms** 后点击 `Abort Merge`，对话框关闭，`CloseMainWindow` **成功**、端口关闭、临时目录删除、无页面错误。操作结束后的正常关窗已取证。
- 补充 `--continue-close`（PID 10040、端口 58123）：外部同步 **250ms** 后点击 `Continue Merge`，对话框关闭，`CloseMainWindow` **成功**、端口关闭、临时目录删除、无页面错误；Continue 语义与结束后正常关窗通过。该简单 merge 状态未提供 Skip，Skip 未执行。
- Skip：正常入口 rebase 冲突（PID 22200、端口 50196）只提供 Abort Rebase/Skip，Continue 不出现；点击 Skip 后对话框关闭，`git status` 干净，历史为 `test: feature-second` / `test: main` / `test: base`，被跳过的 `test: feature-conflict` 未进入历史，后续 `other.txt` 已应用；`CloseMainWindow` 成功、端口关闭、临时目录删除、无页面错误。服务层另有 11 项高级操作动作测试通过（Rebase/CherryPick/Revert Continue/Skip/Abort，Merge Continue/Abort）及 Merge 冲突拒绝 Skip 测试。
- 白闪修复后 Shell `AF550779…B1E0024AB` 重跑：Continue-close 外部同步 241ms（PID 24712/端口 53419）、Abort-close 230ms（PID 27440/端口 49177）、rebase Skip（PID 25304/端口 53418）均通过，三者 `CloseMainWindow` 成功、端口关闭、临时目录删除、无页面错误。Abort 审计改用 DOM `click()`，避免按钮在对话框销毁时被 Playwright 误判为 detached。
- 证据：`artifacts/pixel-followup-2026-10-17/conflict-readonly-round15/`。

## 定向验证

| 范围 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build Augit.slnx --no-restore /nodeReuse:false` | 0 警告 / 0 错误 |
| Core | `dotnet test tests/Augit.Core.Tests --no-build --no-restore` | 72 通过 |
| Shell | `dotnet test tests/Augit.Shell.Tests --no-build --no-restore` | 243 通过 |
| Infrastructure | `dotnet test tests/Augit.Infrastructure.Tests --no-build --no-restore` | 203 通过（含改写后的操作会话 20 项） |
| 工作区渲染/批次/刷新 JS | `node --test tools/audit/workspace-change-delivery.test.cjs …` | 43 通过 |
| 桥接超时/变化渲染/覆盖表 | `node --test tools/audit/bridge-timeout.test.cjs tools/audit/workspace-change-render.test.cjs tools/audit/coverage-generator.test.cjs` | 20 通过 |
| 日常启动命令 | `node tools/audit/daily-launch-command.test.cjs` | 5 通过 |
| 设置入口与生命周期 | `node --test tools/audit/settings-surface-entry.test.cjs tools/audit/settings-hidden-startup.test.cjs tools/audit/shell-initialization-lifetime.test.cjs tools/audit/settings-startup-close.test.cjs` | 24 通过 |
| 冲突场景断言 | `node tools/audit/live-shell.spec.cjs <playwright> --goal-conflict-only` | 6 项通过 |
| 共享资产 | `powershell -NoProfile -File tools/audit/verify-ui-assets.ps1` | 9 项一致 |
| 视觉稿场景 | `node tools/audit/mockup-scenes.spec.cjs <playwright> light`（`AUGIT_BROWSER_EXECUTABLE`=Edge） | 54/54 场景渲染通过 |
| 脚本编码 | `verify-script-encoding.ps1` | 23 个脚本通过（新增 move-window.ps1） |
| 文档数字 | `node tools/audit/check-doc-claims.cjs` | `DOC_CLAIMS_OK` |
| 空白 | `git diff --check` | 通过 |

## 当前网页哈希

| 文件 | MD5 | SHA256 |
| --- | --- | --- |
| `web/src/mockup.js` | `2D6A11B95157BB61796FCB831626D2B7` | `52995353EDBF8B09001A7F0FF08D891E0E09E851D995CFD3B6F103F8222653E8` |
| `web/src/mockup.css` | `4F4D6394178D8D96D4CCCE6DC0421904` | `DF83D9B00D3CB842D1CEC113611D069794F683297F60D5D01C9BEB00468295C0` |
| `web/src/mockup.css`（清理三栏样式后） | `5857AFF257DFA4EADAF9984D636C2C2D` | `C45AA643BA3A8D7E6048B7CD40D6B2EF16F4E27A3B274A8D55038BFAADA09C2E` |
| `web/src/live-data.js` | `4A266F4927CDCE21B8FAD52168618787` | `1041EC2BC5B349F254304CB31AC26899566D0B9D9A2FE2200ADC28704AD3FB01` |
| `web/src/bridge.js` | `6FDFEB10AF93CBCA2191202A1126BC3D` | `653C8F5F8749AC083E883168770CC0AEEED7BF6C3860A5AAA4F1C45B17F58FE7` |

Release 未更新；本批正常入口只验证 Debug 实际映射产物，不外推到 Release。

## 未完成与下一步

- 总体目标仍未完成：正常启动与测试一致性、设置重复窗口/闪烁/慢开/关闭、背景刷新透明帧与 500ms 目标仍是优先项。
- 三栏逐行像素验收已移出范围，原 55 页目录调整为 54 页；其他保留页面的双端同态截图、模态全状态、终端逐行对照（在批准的有意差异边界内）、系统阴影定量仍未完成。
- 三栏未引用样式和 `tools/verify-ux-conflict.cjs` 已清理；三栏专属历史工具已全部删除，只保留只读回归 `conflict-readonly.cjs`/`live-goal-conflict.cjs`；不得把历史文档中的旧结果当作当前证据。
- Windows 10 实机验收仍排除本轮；跨屏 DPI 仍未验证。
- 本批未回滚、暂存或提交工作区既有混合修改。

正常启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`

停止优先正常关窗；异常时仅停止核实属于本批的实例：`taskkill /PID <本次PID> /T /F`

复验：`node tools/audit/conflict-readonly.cjs <playwright 模块路径> artifacts/pixel-followup-2026-10-17/conflict-readonly`。该脚本自行创建临时冲突仓库、强制构建；优先正常关窗，失败时只结束本实例并清理临时资源。
