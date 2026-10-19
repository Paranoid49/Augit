# Git Push 网络失败状态配对（2026-10-19）

## 1. 结论

用同一隔离夹具和同一提交，把远端设为 `http://127.0.0.1:1/repo.git`，让两端都收到同一条 HTTP 502 失败：

- 实际：Push 对话框保持打开，正文显示
  `fatal: unable to access 'http://127.0.0.1:1/repo.git/': The requested URL returned error: 502`
  Push 恢复可用，`Cancel` 可关闭；分支仍 `ahead 1`。
- 参考：对话框关闭，右下 balloon 显示 `Push failed`，正文同一 502 错误，并提供 `Show details in console`；分支仍 `ahead 1`。
- 登记为**状态级同态配对（已知差异，非页面级、非像素合格）**；页面级仍 **49**，像素合格仍 **0**。

这是**网络/HTTP 失败**，不是此前的 non-fast-forward 拒绝；两者不能互相替代。失败结果仍不是独立页面，故不增加页面计数。

## 2. 夹具与路径

- 隔离夹具：`.tmp/git-failure-2026-10-19/fixture`。
- 基线 `985a3e7 test: failure base`，待推提交 `97e1c76 test: failure push`，`main...origin/main [ahead 1]`。
- 远端 `http://127.0.0.1:1/repo.git`；`git ls-remote origin` 在 3s 内返回 128，错误为同一 502。
- 实际：正常入口 PID 7444；branch chip → `Push...` → `Push`。
- 参考：PyCharm 2026.2.1 / PY-262.9437.214，PID 22960；`Ctrl+Shift+K` → `Push`。

## 3. 对照

| 项目 | 实际 | 参考 |
| --- | --- | --- |
| 待推提交 | `1 commits`，`test: failure push` | 同名提交，1 file |
| 失败反馈 | 对话框内原始 `fatal:` 文本 | 右下 `Push failed` balloon |
| 详细入口 | 原始文本直接可见 | `Show details in console` |
| 重试 | Push 恢复可用 | 对话框已关闭，可重新打开 |
| 分支 | `main...origin/main [ahead 1]` | 同 |
| 远端状态 | 未推送、无新远端引用 | 同 |

已知差异：实际是内联原始错误，参考是摘要 balloon + Console 入口；两者都没有独立的“Git 操作结果页”。不据此判产品缺陷。

## 4. 边界

- 只覆盖浅色、DPI 168、单提交 Push、HTTP 502；未覆盖认证失败、超时、被拒、Force Push、Fetch 失败或真实网络。
- 失败前/后没有精确计时；只记录稳定失败帧。
- 状态级配对不改变页面级 49，也不改变像素 0；`Git 操作结果` 页面级仍未闭环。

## 5. 证据

- 实际失败：`artifacts/pixel-followup-2026-10-19/git-failure-closure/actual/actual-push-failed.png`（`E58D3FF6…3E68`）。
- 参考失败：`reference/reference-push-failed.png`（`61725234…DC9F`）。
- 实际/参考待推状态：`actual/actual-push-ready.png`、`reference/reference-push-ready.png`。

## 6. 清理

- 实际 PID 7444、参考 PID 22960 正常关闭；参考配置从启动前快照恢复并哈希一致。
- 临时夹具删除；全局设置仍 `B8868855…DC3D`；未修改代码、未提交、未推送。
- `cleanup-check-169.json`。
