# 搜索超时：代码路径存在，1M 文件未触发（2026-10-19）

## 1. 代码确认

- `src/Augit.Core/Search/SearchOptions.cs`：
  - `Timeout = TimeSpan.FromSeconds(15)`
  - `SearchTimedOutMessage = "搜索超时，已保留完成的结果。"`
- `src/Augit.Infrastructure/Search/RipgrepSearchService.cs`：
  - `SearchTextAsync` 使用 `CancellationTokenSource(SearchOptions.Timeout)`
  - 结束时 `timedOut = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested`
  - 返回 `SearchTimedOutMessage`
- `src/Augit.Core/Search/SearchResults.cs`：`Notice` 在 `IsTimedOut` 时返回该文案。
- 既有 `tests/Augit.Core.Tests/SearchStatusTextTests.cs` 覆盖状态文案/翻译映射。

因此 timeout 分支不是缺失实现。

## 2. 实测

- 250k 文件：18s 内 `No results found`，无 timeout；
- 1M 文件：20s 内 `No results found`，无 timeout；
- 搜索入口已确认是真实点击 `(32,257)`。

说明在该机器/该目录结构下，ripgrep 在 15s 内完成了扫描；没有进入 timeout 分支。

## 3. 结论与边界

- 不能据此判 timeout 分支失效；
- 继续加文件数量成本高且无效，不再重复；
- 若要真正触发，需要受控 I/O 限速/慢文件系统，并记录查询开始与 timeout 时刻；
- 页面级仍 **50**、像素仍 **0**。

## 4. 限速尝试

- `PROCESS_MODE_BACKGROUND_BEGIN` 对该进程返回 false / error 87（参数无效）；`IDLE_PRIORITY_CLASS` 可设，但不保证降低 I/O 优先级；
- 250k 树在 Idle 尝试后仍 `No results found`，未触发 timeout；
- 不再重复该 P/Invoke 路径；需要 Job Object I/O rate control 或慢文件系统。
- SMB 慢共享尝试：`New-SmbShare` 返回“拒绝访问”（当前进程非管理员），未创建共享、未启动搜索；不再重复。

## 5. 证据

- 代码：`SearchOptions.cs`、`RipgrepSearchService.cs`、`SearchResults.cs`、`SearchStatusTextTests.cs`
- 实测：`artifacts/pixel-followup-2026-10-19/search-timeout2/after-18s.png`、`.../search-timeout1m/after-20s.png`

## 6. 清理

- 无残留 Augit/PyCharm；1M/250k 临时树均已删除；未提交、未推送；`cleanup-check-210.json`、`cleanup-check-211.json`。
