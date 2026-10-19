# Git 操作进行中取证尝试（2026-10-18）

## 1. 结论

用慢 remote 让 Push 停留在进行中，实际端捕获到 `Pushing...` 状态；参考端 Push 后直接出现结果 balloon，未捕获进行中状态，**未建立配对**，页面级仍 47。

- 隔离夹具：`.tmp/progress-remote.git`（bare，`pre-receive` hook `sleep 6`）+ `.tmp/progress-fixture` clone；临时提交 `b4cbf85 test progress push`。
- 实际 PID 23984：Push 对话框 `main → origin/main`、`1 commits`、`test progress push`；点 Push 后 1s 内捕获 **`Pushing...`**（详情区一行 + 底部 `Pushing...`，无百分比/进度条）。
- 参考 PID 3268：Push Commits 对话框 `main → origin : main`、右侧 `No commits selected`；点 Push 后对话框关闭，右下角 balloon **`Everything is up to date`**；没有可截取的进行中页/进度条。
- 两端的 Push 模型不同（实际推送 1 个提交并停留进行中；参考没有选中提交、直接给出结果），不强行配对。

## 1.1 参考结果复核（第 143 批）

- 参考 PID 22152 用同一 bare + pre-receive `sleep 8` 克隆并提交 `reference progress push`；Push Commits 对话框列出 1 个提交，点 Push 后 1s 内右下角出现 **`Pushed 1 commit to origin/main`** ballot，Push 没有停留。
- 说明该 hook 在参考的 Git 调用里没有形成慢操作；参考侧的进行中状态仍未取得。本轮只补充了 Push 成功结果的状态配对（见[Push 成功结果状态配对](git-result-state-pair-2026-10-18.md)）。

## 2. 后续

- 参考端在 Push 对话框勾选/确认提交列表后再推，并在慢 hook 下 **0.5–1s** 截“进行中”状态；实际 `Pushing...` 只是一行文本，没有进度百分比，若规范要求进度条需单独登记差异。
- 失败/超时/取消进行中操作仍未覆盖。

## 3. 清理

- 实际 PID 23984、参考 PID 3268 已结束；临时 clone/bare 删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；`settings.json` 恢复 `B8868855…DC3D`；fixture 未变。
- 本批未修改仓库代码；`cleanup-check-150.json`；未提交、未推送。
