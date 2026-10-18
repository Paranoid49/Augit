# 持续复原审计与整改（2026-10-12）

本批接续 [2026-10-11 差异清单](goal-audit-2026-10-11.md)，Goal 保持 active。字体、Markdown、图标字号缩放、冲突侧范围及完整状态矩阵仍有缺口，不能称为整体 100% 复原。

## 基线与取证边界

分支 `dsh`，HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`；IntelliJ checkout 为 `576e32820af82f97529d70f9269726803a27c016`。接续时已有 117 个跟踪文件改动及多个未跟踪审计文件，本批只增量修改，未回滚、覆盖或整体暂存。空白检查与四个共享 UI 文件字节一致检查通过。

使用 computer-use 技能观察用户原有 PyCharm：Augit／AGENTS.md，Commit 与 Git Log 展开，左原文右预览。Find Action 搜索 Manage Remotes，Git Remotes 为 Name／URL 两列、Add／Remove／Edit、唯一 OK；初始无选中，Down 选中 origin。双击进入 Define Remote，URL 初始聚焦并选中，Fetch remote 勾选，底栏 OK／Cancel；两次 Esc 分别回主表和原主窗口。没有保存远端、执行 Fetch、修改用户文件或 Git 状态、改变主题／字号／窗口布局／系统 DPI，未关闭用户 PyCharm。IDE 可能自行保留动作搜索历史，不称其配置文件逐字节未变。

## 差异与整改

| 编号 | 当前证据 | 本批处理 |
| --- | --- | --- |
| F-02／Remotes | 旧清单写双栏，当前工作区已是 Name／URL 表格与独立 Define Remote；本批复核真实结构、初始焦点、方向键和 Esc 层级 | 实时专项 41 项通过。Push URL、省略联网可达性校验按产品范围登记；前台深色／大字号与逐像素全矩阵未完成 |
| F-08a／Git 等待 | 多个入口设置 60 秒至 10 分钟网页超时，而宿主写入和网络无自动超时；网页提前失败可能解除忙态并丢失真实应答 | `bridge.js` 统一固定 Git 写入、网络和 `write/cancel` 等待宿主，遗留有限参数不覆盖契约；读取继续按自身超时。可控时钟及真实网页验证长等待、重复提交、取消和最终应答 |
| F-08b／启动回退 | 工作区失败或超过 8 秒的 Promise.race 把 live 赋 null，真实外壳显示样例树／标签／正文 | 启动即建立真实空对象，失败／等待截止保留空态及说明；F5 重试、代次丢弃旧应答。晚到结果保留同一对象与工具窗／标签／草稿，局部更新保留输入焦点 |
| F-09／首次布局恢复 | 尺寸虽落盘，却与标签会话共用 LastWorkspace 门控；只拖分隔条后无法恢复 | `ToolWindows.WorkspaceRoot` 单独保存尺寸归属，旧配置回退旧 LastWorkspace。新目录只拖一个面板不接管旧目录另一尺寸；普通偏好不转移归属；session/write 固定旧布局归属。未转移旧标签会话 |
| F-11／显式工作区 Diff 标签 | 完整套件发现 `--diff` 有正文却没有真实标签，无法关闭；空态规则不再允许旧样例标签兜底 | 显式 Diff 建立统一比较标签，保持工作区文件导航；关闭释放补丁、标签与 workspaceDiff 标记。空会话回归增至 17 项通过 |
| F-12／Markdown Ctrl+F | 全局快捷键提前消费按键，尝试点击并不存在的 Markdown Find 按钮，共享冒泡监听因此不执行 | 通过当前视图事件进入原共享查找。预览、对照、原文均验证重复 Ctrl+F 仅聚焦、保留查询及 Esc 关闭；查找／Blame 专项 26 项通过 |
| F-01、F-03～F-05、F-06、F-10 | 字族差异、图标墨迹固定、Markdown 字号／入口、冲突侧范围不完整、完整主题／DPI／窗口与二级状态矩阵欠采 | 保留待整改／未验证；本批数量不折算还原率 |

宿主契约：`GitCommandRunner.cs` 只为 LocalQuery 建 30 秒取消源；`ShellBridge.CancelWriteAsync()` 等 `_writes.CancelAsync()` 后应答。参考取消／等待来源见 `nui-behavior/16-operation-progress.md` 的 ProgressDialogUI／ProgressDialog／ProgressWindow。Remotes 对应本地 `plugins/git4idea/backend/src/remote/GitConfigureRemotesDialog.kt`、`GitDefineRemoteDialog.kt`。

## 验证与失败

先跑受影响范围，再因启动公共状态、桥接等待与配置归属变化扩大至模块验证。历史全量快照不当作当前结果。

| 命令或范围 | 当前结果 |
| --- | --- |
| `node tools/audit/bridge-timeout.test.cjs` | 通过；覆盖 20 个方法的默认、60 秒、10 分钟调用，以及读取超时、正常应答、失败和发送异常释放 |
| `live-shell.spec.cjs <playwright> --goal-write-only` | 5 项通过，约 4 秒；模拟超过十分钟仍忙，取消保持冻结，只发一次取消，最终应答继续有效 |
| `--goal-startup-only` | 13 项通过，约 6 秒；信息失败／无效、根目录失败／无效、慢信息、F5 重试、乱序与焦点保留 |
| `--empty-session-only` | 17 项通过，含显式工作区 Diff。此前首次关闭唯一比较标签断言非稳定失败，未改实现／期望重跑原目标用例通过，仍需隔离调查 |
| `--goal-remote-only` | 41 项通过 |
| `--goal-conflict-only` | 51 项通过；10MiB 长行尾部编辑约 161ms、编辑及保存 1932ms，不推广为短行／IME性能结论 |
| `--find-blame-only` | 26 项通过，包含三种 Markdown 模式的 Ctrl+F／重复／Esc |
| Shell 设置专项／Shell 模块 | 21／164 项通过；模块约 21 秒 |
| Infrastructure SettingsStore | 16 项通过，约 0.2 秒 |
| 定向 dotnet format、JS 语法、共享四文件字节一致、CSS 括号、git diff --check | 通过 |
| 当前完整 UI | 首次“冲突块被标出”失败：旧夹具 start:1,length:5 是行单位，真实契约是字符范围。已按 Core parser 的结束标记行含换行契约修正字符偏移／长度／侧文本，保留原高亮断言；后续结果继续登记 |

捆绑 Playwright 所期望的 chromium-1234 不存在，首次浏览器启动失败。实际通过 `AUGIT_BROWSER_EXECUTABLE` 指定本机 chromium-1243；未安装新依赖。没有 Windows 10 22H2 x64 实机，不声明该平台验收。

## 哈希与清理

| 文件 | 接续 MD5 | 当前已验证 MD5 |
| --- | --- | --- |
| mockup.js | 5D0C7DE0D3A935C03F8DFBC414AAFAB4 | 5D0C7DE0D3A935C03F8DFBC414AAFAB4 |
| mockup.css | F5956EE130E2F3E6BC1DE24E5502B08A | F5956EE130E2F3E6BC1DE24E5502B08A |
| live-data.js | 3DEB2F0A3D84A32ED7D752C4349C023A | 9A18302CD99D3BBF06900F0270381BF4 |
| bridge.js | AAA5EC59E52AD601420CA553C1C38B2D | F297BF8769E249CA0D804F4F4EEEF5A9 |

共享 current-find.js 当前 MD5 为 `E47643F5CCE7761312CDE438E6837D55`，两侧必须继续保持字节一致。Release Build 零警告零错误，15 文件镜像门禁通过；其后新增查找修复须重建产物。取消公共等待修复的负向对照只在 Node 内存读取副本中移除规则，新增桥接测试在 git/init 立即失败（1 个自动 timer）；仓库代码没有被回滚。

浏览器和 HTTP 服务由脚本 finally 关闭；C# 临时目录由 TemporaryDirectory 释放。本批没有启动真实 Augit／终端，未调用 DSH 子代理（只查看 ACP 用法）。既有 `.tmp` 和历史清理拒绝目录不属于本批创建，不擅自删除。完整测试日志保留 `artifacts/goal-audit-2026-10-12/live-shell-current.log` 为验收附件。结束前补本批 PID 退出与最终哈希。

手动运行测试及停止（脚本正常结束由 finally 关闭所属服务与浏览器）：

```powershell
$env:AUGIT_BROWSER_EXECUTABLE = 'C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/live-shell.spec.cjs 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright' --goal-startup-only
```

用户另行启动和正常停止真实应用：

```powershell
dotnet build src/Augit.Shell/Augit.Shell.csproj -c Release --no-restore --disable-build-servers
$augitRun = Start-Process 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit' -WindowStyle Normal -PassThru
[void]$augitRun.CloseMainWindow()
[void]$augitRun.WaitForExit(5000)
```
