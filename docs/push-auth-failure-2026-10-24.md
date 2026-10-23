# Push 认证失败（HTTP 401）正常入口探测（2026-10-24）

> 结论：真实外壳对一个只返回 HTTP 401 的远端执行 Push，Git 请求走到认证挑战；Augit
> 不打开凭据输入，宿主终止操作并显示 `Push failed`。**修复后**文案已从笼统的
> “交互输入”改为“认证凭据”，见 §2.2；凭据输入、重试成功和参考像素仍未完成。

## 1. 夹具与方法

- 临时仓库 `repo`，本地 bare remote 建立 upstream 和一条待推提交
  `test: local ahead`；随后把 `origin` 改为
  `http://127.0.0.1:<port>/repo.git`。
- 本地 Node HTTP 服务对任何请求返回 **401** + `WWW-Authenticate: Basic`；记录请求。
- 真实外壳、隔离设置、`-SkipBuild`；以 `GIT_TERMINAL_PROMPT=0`、`GCM_INTERACTIVE=never`
  启动，避免凭据弹窗无限等待。
- 真实 Win32 点击：分支 chip → `Push…` → `Push`；CDP 只读 DOM/坐标与截图。

服务记录：

```text
GET /repo.git/info/refs?service=git-receive-pack   → 401
HEAD /                                            → 401
HEAD /                                            → 401
```

## 2. 结果（修复前）

| 项 | 值 |
| --- | --- |
| 待推提交 | `test: local ahead` |
| 摘要 | `main → origin/main` |
| 第一次远端请求 | `GET /repo.git/info/refs?service=git-receive-pack` 401 |
| Push 对话框 | 失败后关闭 |
| balloon 标题 | `Push failed` |
| balloon 正文 | `Git requires interactive input; the graphical operation was stopped.` |
| balloon 动作 | `Show details`（`push-details`） |
| 认证专用文案 | **无**；没有 401、用户名/密码、凭据帮助或认证失败字样 |
| 重试 | 未执行；没有可用凭据，重试仍会走同一路径 |

- 截图：`artifacts/push-auth-failure-2026-10-24b/push-auth-failure.png`
- 报告：`push-auth-failure.json`（PID、点击 UTC、服务请求、失败文案）。
- 实例 PID 20440 正常关闭，`pidAlive=false`；HTTP 服务关闭；临时仓库删除；
  无残留 Augit/服务。

## 2.1 深色窄窗补充（修复后）

同一 401 路径在深色、1024×640 CSS 视口（1100×800 物理请求）下重跑：

- balloon 标题/正文为修复后的认证文案：
  `Git requires authentication credentials; the graphical operation was stopped.`；
- `toast` 几何 **375×100 @ (631,507)**，右缘 1006、下缘 607，均在 1024×640 视口内；
- 截图：`artifacts/push-auth-failure-auth-message-dark-2026-10-24/push-auth-failure-dark-narrow.png`；
- 实例 PID 正常关闭，服务关闭，临时仓库删除。

这补充了通知的深色/窄窗可见性，但仍**不是**参考像素。

## 2.2 认证文案修复

- `GitCommandRunner` 现在区分认证提示（`could not read Username/Password`、
  `authentication prompts disabled`）与其他交互输入；认证时返回
  `Git 需要认证凭据，图形操作已停止。`
- `ui-text.js` 增加对应英文
  `Git requires authentication credentials; the graphical operation was stopped.`
- 其他 `terminal prompts disabled` 等情况仍返回通用
  `Git requires interactive input; the graphical operation was stopped.`
- C# 测试：`GitRemoteServiceTests` **9/9 通过**（新增认证/通用两条）。
- 正常入口复验（HTTP 401）：
  - 标题 `Push failed`；
  - 正文 `Git requires authentication credentials; the graphical operation was stopped.`；
  - `Show details` 仍保留；
  - 截图：`artifacts/push-auth-failure-auth-message-2026-10-24/push-auth-failure.png`。
- 仍然没有凭据输入、凭据管理器集成或认证成功路径；参考实机未采。

## 3. 技术结论

- HTTP 401 确实到达 Git；Git 需要交互式凭据；宿主按既定策略停止图形操作。
- 文案修复后已明确显示“认证凭据”，不再把认证失败笼统写成“交互输入”；
  但产品仍不提供凭据输入/凭据管理器，也没有认证成功或重试路径。
- 因此“认证失败通知文案”已有进展，**认证流程与参考像素仍未完成**，不能写成认证已对齐。
- 参考 PyCharm 的认证失败反馈仍未采；不能把当前文案写成参考一致。

## 4. 边界

- 401 Basic 挑战是受控本地服务，不是自然 HTTPS 远端、代理或 SSO。
- 未配置凭据助手；未测试已有有效凭据、错误凭据、凭据过期或重试成功。
- 只测浅色、当前 DPI、单远端、单分支；没有通知像素配对。
- `GIT_TERMINAL_PROMPT=0`/`GCM_INTERACTIVE=never` 是诊断环境，说明宿主在无可交互终端时的
  行为；真实桌面若有 Git Credential Manager，路径可能不同。

## 5. 复验

```powershell
node tools/audit/push-auth-failure.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/push-auth-failure-2026-10-24b
```

- 脚本启动 401 服务与应用、真实点击、截图并关闭；临时文件全部清理。
