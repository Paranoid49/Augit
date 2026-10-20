# 搜索超时与取消：受控停顿实测、取消缺陷修复（2026-10-20）

本文接续[搜索超时代码路径](search-timeout-code-path-2026-10-19.md)与[100 万文件搜索](search-timeout-1m-2026-10-19.md)，回答 2026-10-20 用户裁决第 4 条：超时分支不能以“代码存在、未触发”结束，取消也必须真实结束 ripgrep。

## 1. 结论

1. **15 秒超时分支在正常入口可真实触发且行为正确**：受控停顿期间保留已完成结果，结果区显示 `Search timed out; completed results are preserved.`（宿主文案「搜索超时，已保留完成的结果。」的英文映射），ripgrep 在约 15.0 秒被结束，退出后界面仍可继续操作。
2. **发现并修复一个真实取消缺陷**：修复前，搜索进行中按 `Esc` 关闭浮层（或改发新查询）只会让网页丢弃结果，**宿主 ripgrep 继续跑到 15 秒超时才结束**。实测按 `Esc` 后 rg 仍存活 13.85 秒；这与 `product-spec.md` 第 67 行「用户可以随时取消；超时和取消后必须结束 ripgrep 进程」及 `ux-spec.md` §7.15「超时和取消后结束 ripgrep 进程」冲突。
3. 修复后按 `Esc` 或改发新查询会发送 `search/cancel`，宿主按 requestId 取消对应请求；实测 rg 在 `Esc` 后 0.7 秒内消失，15 秒超时分支回归仍通过。
4. 先前 250k/1M 文件实验的 `No results found` 与本次结论不矛盾：那些搜索在 15 秒内自然完成，本来就不该进入超时分支；它们不能证明分支失效，也不能证明正常。**“没有触发”不再作为结案依据**，本文以受控停顿和完整时间线结案。

## 2. 方法

- 夹具：`D:/AugitAcceptance/search-timeout-live-2026-10-20/workspace`，Git 仓库只跟踪 `README.md`，另含嵌套仓库 `tree/`（150 目录 × 1000 文件 = 150,000 个 64B 文本文件，约每 500 个文件一处 `needle_token_live_timeout`）。`git status` 只显示 `?? tree/`，避免 15 万文件进入 Commit 详情。
- 真实 ripgrep 直接调用耗时 **2.96s**、301 处命中，命中到达时间 0.095–2.572s ⇒ 有稳定的“进行中”窗口可停顿。
- 正常入口：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/start-augit.ps1 -Workspace <夹具> -SettingsFile <隔离 settings>`；
  - 取消对照实例 PID 12652（修复前），最终实例 PID 25464（修复后）。
- 真实输入：`SendInput` 键鼠，`SetForegroundWindow` 后点击搜索轨 `(21,147)`，点击输入框 `(590,198)`，`SendKeys` 输入 `needle`。
- 受控停顿：守护脚本轮询父进程为 Augit 的 `rg.exe`，找到后 700ms 调用 `NtSuspendProcess`，持续观察退出；这模拟不可响应的慢文件系统，走的是**同一套**生产启动/读行/取消/收尾路径，不替换可执行文件、不改生产超时。
- 时间戳全部使用 UTC；窗口截图用 `PrintWindow(PW_RENDERFULLCONTENT)`，同时记录窗口矩形、DPI 与前景状态。

## 3. 超时（修复前，PID 12652）

| 事件 | UTC | 说明 |
| --- | --- | --- |
| 输入完成 | 11:31:50.827 | 真实键盘 |
| rg 启动 | 11:31:51.447 | 父进程 = Augit |
| 挂起 rg | 11:31:52.155 | `NtSuspendProcess` 返回 0 |
| rg 退出 | 11:32:06.059 | 挂起后 13.90s，请求开始后约 15.0s，退出码 -1（被结束） |

截图 `14-timeout-state.png`：9 条已完成结果保留，覆盖层底部 `Search timed out; completed results are preserved.`，输入框仍为 `needle`。没有出现 `No results found` 冒充结果。

## 4. 取消缺陷（修复前，PID 12652）

| 事件 | UTC | 说明 |
| --- | --- | --- |
| 输入完成 | 11:32:32.620 | `needle` |
| rg 启动 | 11:32:33.291 | |
| 挂起 rg | 11:32:34.005 | 返回 0 |
| **按 Esc** | **11:32:33.628** | 浮层关闭；此后 rg 未结束 |
| rg 退出 | 11:32:47.854 | 挂起后 13.85s，仍是 15 秒超时，不是取消 |

`15-after-esc.png` 显示浮层已关闭，但进程表仍有 `rg` PID 22352 持续到超时。根因：

- `bridge.js` 的 `invoke()` 只有请求/应答，没有取消通道；`ShellWindow` 以 `CancellationToken.None` 调用 `ShellBridge.HandleAsync`，宿主没有任何按请求取消的来源。
- `live-data.js` 的 `invalidateSearchRequests()` 只递增令牌、清去抖，丢弃晚到结果，不通知宿主。
- 因此 `RipgrepSearchService` 只能等 15 秒 `SearchOptions.Timeout`。

## 5. 修复

| 文件 | 改动 |
| --- | --- |
| `web/src/bridge.js` | 新增 `cancelRequest(requestId)`：发送 `{ method: "search/cancel", params: { requestId } }`，不占用也不结算原请求 Promise。 |
| `web/src/live-data.js` | 搜索请求自增 `requestId` 并随 `search/text`、`search/files` 发送；`runSearch`/`continueLimitedSearch` 在发起新查询前结束旧请求；`invalidateSearchRequests()`（关闭浮层、会话失效）取消活动请求并清空 `activeSearchRequest`。 |
| `src/Augit.Shell/ShellBridge.cs` | 新增 `search/cancel` 分发与 `_searchRequests` 登记表；`SearchTextAsync`/`SearchFilesAsync` 用 requestId 对应的 `CancellationTokenSource` 链接超时与服务；只移除自己登记的源，避免旧请求晚到收尾删掉同 id 的新请求。 |

取消是幂等的：请求已收尾、源已释放或 id 不存在时返回 `{cancelled:false}`，不报错；同 id 重试先取消旧源。

## 6. 修复后验证（最终构建 PID 25464）

最终构建：`Augit.exe` SHA256 `48348A5B…3934`、`Augit.dll` `AA0509C7…B1E5`、`ProductVersion=0.1.0+f32d70c…`；`live-data.js` `7D481F50…185E`、`bridge.js` `6EFF9F71…BF52`。

| 场景 | 事件 | UTC | 结果 |
| --- | --- | --- | --- |
| 取消 | rg 启动 | 11:38:17.907 | |
| 取消 | 按 Esc | 11:38:18.351 | 浮层关闭 |
| 取消 | 观察 rg | 11:38:18.643 | 已退出（挂起调用返回 -1），`Esc` 后 **< 0.3s** 消失，不再跑满 15s |
| 超时 | rg 启动 | 11:38:32.506 | |
| 超时 | 挂起 | 11:38:33.215 | 返回 0 |
| 超时 | rg 退出 | 11:38:47.225 | 挂起后 14.01s（约 15s 总时长），退出码 -1 |

截图：`22-after-esc-fix.png`（Esc 后浮层关闭、无 rg）、`32-final-timeout.png`（9 条结果 + 超时说明）。

自动化：

- `node tools/audit/bridge-timeout.test.cjs` PASS（新增取消消息不结算原请求、读取超时不丢失、空 id 不发消息）。
- `node tools/audit/search-cancel-wiring.test.cjs` 5/5 PASS（网页导入、失效路径、每次搜索 requestId、分页、宿主登记/只取消对应 id）。
- `dotnet build Augit.slnx --no-restore` 0 警告 0 错误；`dotnet test tests/Augit.Shell.Tests` **246/246** 通过。
- `verify-ui-assets.ps1` PASS；`verify-script-encoding.ps1` PASS；`git diff --check` 通过。

## 7. 边界

- 本次证明的是**超时/取消状态与进程生命周期**，不是“搜索截断与超时”整页像素合格；页面级配对仍 **50**、像素仍 **0**。
- 受控停顿是诊断条件，不是日常条件；日常条件下的搜索延迟仍按既有 500ms/性能规则另行验收。
- 未覆盖：搜索超时后的重试、深色主题、窄窗口、正则/忽略开关组合、参考实机同状态截图。
- 250k/1M 树不再重复；若未来要验证“自然慢盘”，应使用可核实的慢文件系统而不是继续加文件数。

## 8. 清理

- 最终实例 PID 25464 正常关闭；修复前实例 PID 12652、首个 15 万行 Commit 详情实例 PID 8724 因渲染卡顿，核实 PID 后 `taskkill /PID … /T /F`；中间构建实例 PID 17232 正常关闭。
- 受控停顿守护进程随 rg 退出结束；无残留 `rg.exe`、无监听端口。
- 150,000 文件夹具与 512MB 校准文件已删除；隔离 settings 与临时脚本位于 `D:/AugitAcceptance/search-timeout-live-2026-10-20`，取证后删除。
- 原始证据仅在本机 `artifacts/search-timeout-2026-10-20/`（Git 忽略），推送代码不等于上传截图。
