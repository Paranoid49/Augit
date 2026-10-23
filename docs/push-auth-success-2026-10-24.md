# Push 认证成功路径：URL 内置凭据 / credential helper + smart HTTP（2026-10-24）

> 结论：真实外壳 Push 到本地 smart-HTTP 远端成功；远端 URL 使用 `user:pass@` 内置凭据，
> Git 先收到 401 challenge，再带 `Authorization` 重试并完成 `git-receive-pack`。
> 主窗右下显示 `Push / Pushed to origin/main.`，弹层关闭，远端 main 前进到待推提交。

## 1. 方法

- 临时目录内用 `git init --bare server/remote.git` 建裸远端。
- Node 服务端实现 `git http-backend` CGI：
  - 无 `Authorization` 返回 401 + `WWW-Authenticate: Basic`；
  - 凭据不等于 `user:pass` 返回 403；
  - 通过后设置 `GIT_PROJECT_ROOT`/`PATH_INFO` 等 CGI 环境，代理 GET/POST 到
    `git http-backend`，返回 smart-HTTP 响应。
- 种子仓库先 `git push -u` 到 `http://user:pass@127.0.0.1:<port>/remote.git`，建立一个
  在远端 `main` 上的基础提交；随后新增 `extra.txt` 提交，形成一条待推提交。
- 真实外壳 `start-augit.ps1 -SkipBuild` 打开种子工作区：
  1. 真实鼠标点分支 chip；
  2. `Push…`；
  3. Push 弹层确认按钮（真实 Win32 点击）；
  4. 等右下结果通知，再截图并读取远端 `git log`。
- CDP 只读 DOM 矩形、通知文本与截图；凭据不写入设置或产品存储。

## 2. 结果

| 项 | 值 |
| --- | --- |
| 通知 | **`Push` / `Pushed to origin/main.`** |
| Push 弹层 | 已关闭 |
| 远端 `main` | `150f1f0 test: auth push`、`85a9464 test: auth seed` |
| HTTP 请求 | 7 次：2 次无凭据 401 challenge、4 次带 Authorization、1 次 HEAD `/` |
| 实例 | PID 31304 正常关闭，`pidAlive=false` |
| 截图 | `artifacts/push-auth-success-2026-10-24/push-auth-success.png` |
| 报告 | `push-auth-success.json` |

请求序列（同一端口）：

```text
GET  /remote.git/info/refs?service=git-receive-pack  无 Authorization → 401
GET  /remote.git/info/refs?service=git-receive-pack  Authorization
GET  /remote.git/info/refs?service=git-receive-pack  无 Authorization → 401
GET  /remote.git/info/refs?service=git-receive-pack  Authorization
POST /remote.git/git-receive-pack                     Authorization
HEAD /                                                无 Authorization（连通性探测）
POST /remote.git/git-receive-pack                     Authorization
```

两次 401 是 Git 对带凭据 URL 的 challenge 流程，最终由 URL 凭据完成认证；
没有凭据提示、没有认证失败通知。

### 2.1 凭据助手路径

同一 smart-HTTP 端点、URL **不带**用户名/密码（`http://127.0.0.1:<port>/remote.git`），
仓库本地配置凭据助手：

```text
credential.helper = !f() { echo username=user; echo password=pass; }; f
```

为隔离本机已安装的 credential manager，测试进程把 `GIT_CONFIG_GLOBAL` 和
`GIT_CONFIG_SYSTEM` 指向空文件；仓库本地助手仍由真实 Git 子进程调用。

| 项 | 值 |
| --- | --- |
| 通知 | **`Push` / `Pushed to origin/main.`** |
| 远端 `main` | `03c1e99 test: auth push`、`94cfd81 test: auth seed` |
| HTTP 请求 | 6 次：2 次 401 challenge、4 次带 Authorization；无 HEAD 探测 |
| 实例 | PID 23048 正常关闭，`pidAlive=false` |
| 截图 | `artifacts/push-auth-helper-2026-10-24j/push-auth-success.png` |
| 报告 | `artifacts/push-auth-helper-2026-10-24j/push-auth-success.json`（`mode=credential-helper`） |

这说明认证成功不只依赖 URL 内置凭据；Git 的 credential helper 也能在本机供给凭据，
Augit 不读取、不存储这些凭据。

## 2.2 深色 + 窄窗

同一 URL 凭据路径在 Dark、1000×740 窗口重跑：

- 右下通知仍为 `Push / Pushed to origin/main.`；
- Push 弹层关闭；
- 远端 `main` 两条提交：`6c6b5e3 test: auth push`、`340a89b test: auth seed`；
- 7 次 HTTP 请求，4 次带 Authorization；
- 实例 PID 22496 正常关闭；
- 截图：`artifacts/push-auth-dark-narrow-2026-10-24/push-auth-success.png`。

通知在窄窗右下完整显示两行文本，没有被裁切；仍未采参考 PyCharm 通知像素。

## 3. 与认证失败路径的关系

- 失败路径（[push-auth-failure-2026-10-24.md](push-auth-failure-2026-10-24.md)）使用同一
  401 端点，认证不可用时点击 Push 得到 `Push failed` 与明确认证文案。
- 本批成功路径使用同一端点加 Basic 校验；说明失败文案不是“所有 HTTP 401 都统一失败”，
  而是凭据可用时正常重试并完成。
- Augit 仍不存储凭据；凭据来自远端 URL（由本机 Git 处理）。这不是产品推荐的用户配置方式，
  只是验证认证成功链路。

## 4. 边界

- 覆盖 **URL 内置用户名/密码** 与 **仓库本地 credential helper** 两种 Basic 供给方式；
  不覆盖系统 credential manager、SSH key、token UI、OAuth、代理或证书错误。helper 路径
  隔离了全局/系统 Git 配置，不能写成自然 GCM 环境已通过。
- 服务端是本地受控 CGI，不代表自然网络延迟、慢盘或远端 hooks。
- 通知是浅色、当前 DPI 的截图/文本证据，不是参考整页像素；页面级配对仍 50、像素仍 0。
- 深色/窄窗只验证了 URL 凭据成功通知的位置与文本，未采参考像素；helper 路径未重复深色/窄窗。

## 5. 清理与复验

- 服务端、Augit、临时裸远端/种子仓库全部关闭并删除；无残留端口或进程。
- 复验：
  ```powershell
  node tools/audit/push-auth-success.cjs <playwright> artifacts/push-auth-success-2026-10-24
  ```
