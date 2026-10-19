# Git 操作进行中：真实慢远端状态配对（2026-10-19）

## 1. 结论

用一个**只接受连接、不返回 HTTP 响应**的本机远端制造真实慢 Push，而不是继续假设 `pre-receive sleep` 有效：

- 实际：Push 对话框保持打开，正文显示 `Pushing...`，主按钮变为禁用的 `Pushing...`，`Cancel` 可用。
- 参考：Push 对话框关闭，主窗口右下状态栏显示 `Pushing...` 与不确定进度条，`×` 取消入口。
- 两端推送的是同一分支/提交（`main` → `origin/main`，1 个提交 `46bf422 test: progress push`），并且服务端日志确认收到了 `git-receive-pack` 请求。
- 登记为**状态级同态配对（已知差异，非页面级、非像素合格）**；页面级仍 **49**，像素合格仍 **0**。

实际取消：点击 `Cancel` 后对话框显示 `Operation cancelled.`，Push 按钮恢复可用，分支仍 `ahead 1`，本轮推送的 `git.exe`/`remote-http` 进程已消失。
参考取消：点击状态栏 `×` 后 2s 的截图仍显示 `Pushing...`，5s 后仍在；随后关闭窗口出现“Background tasks are running”确认，选择 Exit 后进程退出。**参考取消动作未验证为成功**，不能据此判参考缺陷或通过。

## 2. 方法

- 本机 Python 服务器监听 `127.0.0.1:18765`，接受连接后读取请求并保持不响应；脚本 `artifacts/pixel-followup-2026-10-19/git-progress-closure/scripts/hang_server.py`。
- 隔离夹具：`.tmp/git-progress-2026-10-19/fixture`，`main` 基线 `919d16b`，追加提交 `46bf422 test: progress push`；`origin` 为 `http://127.0.0.1:18765/repo.git`，`branch.main.remote=origin`、`branch.main.merge=refs/heads/main`。
- 先用 `git ls-remote origin` 验证：3s 未返回、服务端收到 `GET /repo.git/info/refs?service=git-upload-pack`，随后终止该探测进程；Push 时服务端收到 `GET /repo.git/info/refs?service=git-receive-pack`。
- 实际：正常入口 PID 18352，2880×1716、DPI 168、浅色；branch chip → `Push...` → `Push`。
- 参考：PyCharm 2026.2.1 / PY-262.9437.214，PID 23276，同一夹具；`Ctrl+Shift+K` → `Push`。

## 3. 状态对照

| 项目 | 实际 | 参考 |
| --- | --- | --- |
| 待推提交 | `1 commits`，`test: progress push` | `test: progress push`，1 file |
| 进行中 | Push 对话框内 `Pushing...` | 状态栏 `Pushing...` + 进度条 |
| 取消入口 | `Cancel` 按钮 | 状态栏 `×` |
| 结果 | 点击 Cancel 后 `Operation cancelled.` | 点击 `×` 后 2s/5s 仍 `Pushing...`；关闭时提示后台任务 |
| 分支状态 | `main...origin/main [ahead 1]` | 同 |

已知差异：进行中呈现一个是模态对话框、一个是状态栏；参考有不确定进度条，实际只有文本；取消入口位置不同。以上均不构成像素合格结论。

## 4. 边界

- 只覆盖浅色、DPI 168、单提交 Push、本机 HTTP 远端；未覆盖 Fetch、失败、超时、被拒、Force Push 或真实网络。
- 没有精确测量“点击到 Pushing 出现”的延迟；截图只证明稳定进行中状态。
- 参考取消未验证；不能把“关闭时提示后台任务”当作取消成功，也不能把 5s 后仍显示 `Pushing...` 单独判为缺陷（点击可能未命中，且任务可能仍在收尾）。
- 状态级配对不增加页面级 49，也不改变像素 0。

## 5. 证据

- 实际进行中：`artifacts/pixel-followup-2026-10-19/git-progress-closure/actual/actual-pushing.png`（`A3D5716A…512A`）。
- 实际取消：`actual/actual-push-canceled.png`（`286EADBA…EA10`）。
- 参考进行中：`reference/reference-pushing.png`（`1BFF92AE…C732`）。
- 参考取消后 2s/5s：`reference/reference-push-canceled.png`（`8FAC21E8…FEF9`）、`reference/reference-push-canceled-5s.png`（`327B4C6C…384C`）。
- 参考关闭提示：`reference/reference-close-prompt.png`（`2B4801F9…BA00`）。
- 服务端日志：`server.log`（`7A3102E7…7492`），包含 `git-receive-pack` 请求。

## 6. 清理

- 实际 PID 18352、参考 PID 23276 正常关闭；参考 Exit 确认后进程退出。
- 本机服务器 PID 10656 已停止，`127.0.0.1:18765` 无监听；本批 `git.exe`/`remote-http` 进程已清理，未保留悬挂连接。
- 临时夹具删除；参考 `trusted-paths.xml`、`recentProjects.xml`、`window.state.xml` 恢复并哈希一致。
- 全局设置仍 `B8868855…DC3D`；未修改代码、未提交、未推送。
- `cleanup-check-168.json`。
