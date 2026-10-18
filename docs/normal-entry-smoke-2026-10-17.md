# 正常入口冒烟：文件、终端、Changes→Diff（2026-10-17）

## 目的

验证正常启动的 Augit 在真实仓库、真实宿主和默认配置下，文件阅读、内置终端、Changes 选择与只读 Diff 可用，且页面无运行期错误。该检查用于发现“测试通过但正常入口失败”的偏差，不替代像素或性能验收。

全程通过 `tools/start-augit.ps1` 强制非增量构建，复制真实设置、默认 WebView2 profile 和系统缩放，不带 `scene` 或测试专用条件。采集为**附 CDP**；无 CDP 同流程未执行。

## 结果

| 检查 | 实测 |
| --- | --- |
| 启动 | 强制构建 0 警告 / 0 错误；PID 16184，端口 60900 |
| 普通文件 | 双击 `plain.txt` 打开 `.code-view`；只读，无 `contenteditable`；JetBrains Mono 13px |
| 内置终端 | 打开 xterm，输入 `echo AUGIT_TERM_OK` 并回车；输出出现在终端；切到 Git 历史再切回，输出保留 |
| Changes 单击 | `changed.txt` 单击后当前 `.code-view` 仍在，未打开 `.diff-layout` |
| Changes Enter | 打开只读 Diff，`.diff-code-line` 2 行，无可编辑元素 |
| 运行期错误 | `window.__augitError` 为空、`__augitErrors` 为空；无 `pageerror`、`console.error`、失败资源请求 |
| 关闭 | `CloseMainWindow` 成功；端口无监听；临时仓库与设置副本已删除；无残留进程 |

实际加载产物由 `read-process-mapped-images.py` 从本实例读取，`Augit.dll` 与 `Augit.Infrastructure.dll` 均存在；映射明细见证据目录 `mapped-images.json`。


## 等待条件修正（2026-10-17）

后续复跑发现：`.diff-layout` 容器先出现，`.diff-code-line` 正文随后异步渲染；旧断言只等容器就立即读行数，曾在 4 次冒烟中出现 2 次 `lines=0` 的误报。产品 Diff 实际已打开，属于测试竞态。

- 冒烟改为 `waitForSelector('.diff-layout')` 后继续等待 `document.querySelectorAll('.diff-code-line').length > 0`，再做只读/行数断言。
- 修正后连续 3 次正常入口冒烟全部 `NORMAL_SMOKE_OK`（`normal-smoke-wait-1/2/3`，PID 13060/22396/28268，端口 64655/53303/55175）。
- 该修正只改审计脚本，不改产品；之前的 `lines=0` 失败不再作为产品失败，也不能反过来把产品 Diff 判为通过。
## 证据

`artifacts/pixel-followup-2026-10-17/normal-entry-smoke/`：

- `launch.json`：正常启动器命令、强制构建输出、端口与工作区。
- `mapped-images.json`：本实例实际映射模块与哈希。
- `report.json`：四项检查结果与运行期错误。
- `cleanup.json`：`CloseMainWindow`、端口关闭、临时目录删除与无残留进程。

## 限制

- 附 CDP；不能用该结果代替无 CDP 的正常启动或像素验收。
- 只覆盖一个临时仓库、一次文件打开、一次终端命令和一次 Changes→Diff；未覆盖设置、模态、刷新、长会话或关闭运行中命令。
- 终端只验证一条命令能输入/输出并在隐藏后保留，未做逐行像素或字体栅格配对。
- 未新增页面或功能；未回滚、暂存或提交既有修改。

## 复验

```powershell
node tools/audit/normal-entry-smoke.cjs <playwright 模块路径> artifacts/pixel-followup-2026-10-17/normal-entry-smoke
```

脚本自行创建临时 Git 仓库、强制构建，优先正常关窗，异常时只结束本批 PID；启动/停止命令与产品正常入口一致。
