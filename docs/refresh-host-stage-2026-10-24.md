# 外部文件与本地 Git 状态同步：同轮宿主阶段证据（2026-10-24）

> 结论：本批在同一轮里同时记录宿主阶段（不依赖 CDP 的时间戳）、网页应用/绘制回调与屏幕首内容帧。
> 外部文件修改 10/10 轮屏幕首内容 **236–293ms**，宿主侧应用完成 **161–207ms**；外部 checkout 3/3 轮
> 分支可见 **117–152ms**、完整应用 **201–232ms**。全部低于 500ms。
> 历史 gdigrab 的 611/552/624ms 慢样本**本批未复现**，10 轮里也没有任何超过 300ms 的宿主阶段；
> 因此仍不能把慢样本定性为产品缺陷，也不能宣布所有条件已连续达标。原始历史结果继续保留。

## 1. 构建与入口

- 源码：HEAD `3574ce1c13cfa7d035e560f0f893733e25b7ccf0` + 本批未提交改动（阶段跟踪与探测）。
- `Augit.exe` ProductVersion：`1.0.0-beta.1+3574ce1c13cfa7d035e560f0f893733e25b7ccf0`（Debug、`--no-incremental` 重建）。
- 正常入口：`tools/start-augit.ps1 -Workspace <隔离仓库> -SettingsFile <隔离设置> -SkipBuild`。
- CDP 只用于读取项目树/编辑区矩形、确认真实双击已打开文件；阶段时间戳与屏幕采集不依赖 CDP。
- `AUGIT_REFRESH_TRACE_DIRECTORY=<绝对目录>` 时宿主启用阶段跟踪；未设置时所有跟踪入口为空操作。
- 每轮窗口置 topmost、真实 Win32 鼠标双击打开 `refresh.txt`，窗口前台后录屏；结束用 `WM_CLOSE`
  关闭本批实例，脚本确认端口无监听、临时目录删除、无残留。

## 2. 阶段定义与时钟校准

宿主导出 `refresh-<pid>-<id>.json`：每个样本含 `stage`、`detail`、QPC `timestamp`、`utcTicks`、线程号，
以及 `originTimestamp`、`frequency`、UTC 锚点。

- `watch-received`：`FileSystemWatcher` 收到每批第一个原始事件；
- `watch-merged`：20ms 合并窗口触发；
- `bridge-notify`：宿主把 `workspace-changed` 推给网页；
- `bridge-request/response` + detail：`workspace/changes`、`git/status`、`document/read`；
- `page-applied` / `page-painted`：网页应用完成、随后双 `requestAnimationFrame`；
- `harness-mark`：采集器在写文件前经真实桥接打点，宿主记录同一时刻的 QPC。

跨进程只用 `harness-mark` 对齐：采集器记录页面发送时刻与往返时间，按单程一半估算宿主接收时刻；
本批 mark 往返 1–3ms，写入发生在 mark 后 2–4.5ms。屏幕首内容帧由 60fps gdigrab/ddagrab 单独定位，
帧量化误差 ≤16.7ms。阶段之间的时间差直接用同一进程 QPC，跨进程误差约 1–2ms。

## 3. 外部文件修改 → 编辑区首内容

夹具：40 行 `refresh.txt`，采集开始约 800ms 后整文件重写第 2 行（generation 0→1）。
每轮记录 PID、窗口句柄、前台、裁切区域、写入偏移、首内容帧；保留基线/首内容 PNG 与 ffmpeg 日志。

### 3.1 gdigrab（60fps，5 轮）

| 轮次 | 屏幕首内容滞后 | 写入→监听 | 监听→合并 | 桥接通知 | `git/status` | `document/read` | 网页应用 | 绘制回调 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| light-1 | **280ms** | 3.9ms | 42ms | 43.1ms | 46.4→164ms | 167.3→173.8ms | 207.5ms | 224.1ms |
| light-2 | **286ms** | 4.9ms | 41.8ms | 43ms | 46.9→167.3ms | 171→177.5ms | 206.9ms | 223.7ms |
| light-3 | **293ms** | 6.2ms | 43.3ms | 44ms | 47.5→164.1ms | 168.3→170.3ms | 203.9ms | 220.6ms |
| light-4 | **275ms** | 7ms | 43ms | 44.2ms | 48→154ms | 157.6→159.6ms | 198.4ms | 213.7ms |
| dark-1 | **291ms** | 7ms | 43.4ms | 44.1ms | 49.4→162.5ms | 166.2→168.2ms | 200.2ms | 215.9ms |

### 3.2 ddagrab（60fps，5 轮）

| 轮次 | 屏幕首内容滞后 | 写入→监听 | 网页应用 | 绘制回调 |
| --- | ---: | ---: | ---: | ---: |
| light-1 | **289ms** | 5.3ms | 205.3ms | 223.3ms |
| light-2 | **255ms** | 4.8ms | 170.1ms | 183ms |
| light-3 | **247ms** | 5.2ms | 161.2ms | 176.4ms |
| light-4 | **236ms** | 6.4ms | 170.8ms | 184.2ms |
| dark-1 | **271ms** | 5.7ms | 178.8ms | 193.5ms |

两种采集方式结果一致：产品侧在约 160–210ms 完成应用，绘制回调在约 176–224ms，屏幕首内容在
约 236–293ms。绘制回调到屏幕帧的 30–76ms 属于合成/采集链路，单独记录，不并入产品处理时间。

### 3.3 与历史慢样本的关系

历史无 CDP gdigrab 13 轮中出现 611/552/624ms 三次慢样本；本批 10/10 轮未复现，且同轮宿主阶段
没有任何 >300ms 的段。此前 20 轮 gdigrab+stage 也为 237–308ms。当前能支持的结论是：

- 本批与最近两批的快速样本中，慢点不在文件监听、合并、`git/status`、`document/read` 或网页应用；
- 历史三次慢样本缺少同轮宿主阶段，**不能**据此认定产品处理慢，也**不能**认定是采集误差；
- 继续保留为未闭环性能风险，不把「本批 10/10」写成所有条件下已稳定达标。

## 4. 外部本地 Git 状态（checkout）

夹具：本地仓库 main 上外部执行 `git checkout -b external-branch`；正常入口打开同一仓库。
采集器在命令前经真实桥接打点，命中 `git-watch-*`、`bridge-notify-git`、`workspace/changes`、
`git/status`、网页应用/绘制阶段。

| 轮次 | 分支在界面可见（CDP 10ms 轮询） | 网页应用完成 | 绘制回调 | `git/status` 读取 |
| --- | ---: | ---: | ---: | ---: |
| b | **146ms** | 228.4ms | 235.2ms | 50.1→120.4ms |
| c | **152ms** | 231.7ms | 238.8ms | 未逐项列出 |
| d | **138ms** | 201ms | 207.6ms | 未逐项列出 |

- `git-watch-received` 出现在 `git checkout` 进程退出**之前**（-28.7ms），这是文件监听的真实行为：
  分支引用在命令执行中已写入 `.git`，界面因此会先看到部分更新，命令结束后继续完成整批应用。
- 用户可感知的「命令结束后分支可见」为 117–152ms；完整应用（状态/历史/引用/正文区域）为 201–232ms，
  均低于 500ms。
- 冲突状态同步另有历史专项证据 230/241/411ms；本批未重测冲突刷新。

## 5. 证据位置与复验

```powershell
$env:AUGIT_REFRESH_TRACE_DIRECTORY='D:/github/Augit/artifacts/<输出目录>/trace'
node tools/audit/refresh-content-onset.cjs <playwright> artifacts/<输出目录>
node tools/audit/refresh-content-onset.cjs <playwright> artifacts/<输出目录> --ddagrab
$env:AUGIT_REFRESH_TRACE_DIRECTORY='D:/github/Augit/artifacts/<git输出目录>/trace'
node tools/audit/git-metadata-refresh.cjs <playwright> artifacts/<git输出目录>
```

- 文件路径：`artifacts/refresh-host-trace-2026-10-24b/`（gdigrab，5 轮）、`...-24c/`（ddagrab，5 轮）；
  每轮 `report.json` 的 `hostTrace.timeline` 与 `analysis`，以及基线/首内容 PNG、ffmpeg 日志、`trace/` 原始导出。
- Git 路径：`artifacts/git-status-sync-2026-10-24b|c|d/`，`cleanup.json` 的 `hostTrace` 与 `checks`。
- 本批 PID、端口和临时目录均记录在各 `report.json`/`cleanup.json`；实例正常关闭，无残留。

## 6. 边界

- 只测一个 40 行文件第 2 行变化；只测一个本地 Git 仓库的 checkout。不是全窗、滚动区、大文件、
  外部分支/冲突或网络远端。
- 采集器使用 gdigrab/ddagrab 与 CDP 读矩形；CDP 不参与时间戳，但无法完全排除其对调度的轻微影响。
- 宿主阶段导出只在显式环境变量下启用；普通运行没有此 I/O。阶段跟踪本身只写内存缓冲，关闭时导出一次。
- 仍缺 500ms 的自然慢盘、跨屏高 DPI、Windows 10 与更长连续运行证据。
