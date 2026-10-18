# 外部 Git 元数据变化刷新（2026-10-17）

## 目的

补齐“外部文件与本地 Git 状态变化在 500ms 内反映”中的 **Git 元数据**一侧：只操作临时仓库，不改用户配置，不点击应用内 Git 操作。

## 方法

- 正常入口：`tools/start-augit.ps1` 强制非增量构建，附 CDP；临时 Git 仓库初始分支 `main`，有一次提交。
- 应用就绪后，外部执行 `git checkout -b external-branch`；`GitMetadataWatcher` 监听 `.git`，界面读取 `window.__augitLive.branch`。
- 计时从 checkout 命令完成到界面 branch 变为 `external-branch`；轮询 10ms。
- 工具：`tools/audit/git-metadata-refresh.cjs`。

## 结果

PID 20572、端口 54491，实际映射 Shell `AF550779…B1E0024AB`、Infrastructure `2CCA339F…AF8190F1F`：

| 项 | 实测 |
| --- | ---: |
| `git checkout -b external-branch` 命令 | 82ms |
| checkout 完成 → 界面 branch 更新 | **125ms** |
| 页面错误/失败请求 | 无 |
| 关闭 | `CloseMainWindow` 成功、端口关闭、临时目录删除 |

125ms 低于 500ms，说明本次外部 Git 分支元数据变化能及时反映；`.git` 合并窗口仍为 50ms。

## 边界

- 只覆盖一次本地分支 checkout；未覆盖 commit、远程 fetch/push、rebase/merge 等所有元数据变化。
- CDP 计时与轮询有少量误差；界面 branch 是文本状态，不等于整页/全场景无闪烁。
- 临时仓库/进程已清理；未改产品，未暂存或提交既有修改。

## 复验

```powershell
node tools/audit/git-metadata-refresh.cjs <playwright> <证据目录>
# 正常关闭由脚本清理；异常仅停止本次输出 PID。
```
