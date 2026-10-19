# 参考端 Push 进行中取消复核（2026-10-19）

## 1. 结论

此前的“参考取消未验证”是**点击坐标错误**：`×` 在状态栏底部，上一批点击落在进度条上方。按截图量取到正确中心 `(2358,1634)` 后，参考端取消成功：

- 参考 2026.2.1 / PID 17512，远端为不响应的本机 HTTP 服务（端口 18767），Push 1 个提交。
- 状态栏出现 `Pushing...` + 进度条，服务端收到 `git-receive-pack`。
- 点击 `×` 后：`Pushing...`/进度条消失，分支仍 `main...origin/main [ahead 1]`，无新远端引用，推送相关 `git.exe`/`remote-http` 进程消失。
- 实际端取消此前已验证：`Operation cancelled.`、`ahead 1`、推送进程消失。

该结果更正 [Git 进行中真实慢远端状态](git-progress-hanging-remote-2026-10-19.md) 中“参考取消未验证”的边界；仍为**状态级**，不增加页面级 50。

## 2. 方法

- 夹具：`.tmp/git-cancel-2026-10-19/fixture`，1 个待推提交，remote `http://127.0.0.1:18767/repo.git`，服务端不响应。
- 参考：`PYCHARM_PROPERTIES=reference-2026.2.1.properties`，`pycharm64.exe`；`Ctrl+Shift+K` → Push。
- `×` 屏幕坐标 `(2358,1634)`；此前的 `(2366,1607)` 点在进度条上方，不能作为取消失败证据。

## 3. 证据

- 进行中：`artifacts/pixel-followup-2026-10-19/git-cancel-closure/reference-pushing.png`（`F09FED93…9596`）。
- 取消后：`reference-canceled.png`（`A4C8B5BC…EF8A`），进度条消失、分支 `ahead 1`。
- `×` 坐标放大：`cancel-crop.png`。
- 服务端日志：`server.log`，含 `git-receive-pack` 请求。

## 4. 清理

- 参考 PID 17512 关闭；服务器 PID 16432 停止，`18767` 无监听；本批 git 进程清理。
- 临时夹具删除；参考 `trusted-paths.xml`、`recentProjects.xml`、`window.state.xml` 从启动前快照恢复并哈希一致。
- 全局设置仍 `B8868855…DC3D`；未修改代码、未提交、未推送。
- `cleanup-check-173.json`。
