# 外部修改 → 编辑区首内容重新测量（2026-10-24，当前构建）

> 状态：**已在本批构建上重新采集；无 CDP 的 gdigrab 共 13 轮，其中 3 轮超过 500 ms
> （611 / 552 / 624 ms）；另外 10 轮 241–294 ms。500 ms 目标仍未连续达成，不能写成通过。**
> 后续同轮阶段诊断显示慢样本仍未归因（见 §6），不能写成产品缺陷定论。
> 本文只登记“外部文件修改 → 编辑区出现新内容”这一条路径，不是全窗刷新、白帧或像素验收。

## 1. 构建与入口

- 构建：`tools/start-augit.ps1 -PrepareOnly`（0 警告 0 错误），ProductVersion
  `0.1.0+faeb0a1572b27f0488db067d14b4925c847e2361`。
- 启动：`tools/start-augit.ps1 -Workspace <隔离仓库> -SettingsFile <隔离设置> -SkipBuild`。
- 隔离设置从当前全局配置复制；浅色 4 轮、深色 1 轮。全局设置 SHA256 批前/批后均为
  `8840E7754E244F67B266E300AAB0142F7907E2C30FE6D0DB772C77F9962462CF`。
- 正常入口：项目树真实鼠标双击 `refresh.txt` 打开单标签；窗口最大化、前台。
  前台窗口原本是最大化的 Edge，脚本临时把本实例置为 topmost，再用 `click-window.ps1`
  的 Win32 双击激活/打开；录制结束随实例关闭恢复。CDP 只用于读取项目树/编辑区矩形、
  确认文档已打开，不注入场景或替换宿主。
- 关闭：向本批 PID 发送 `WM_CLOSE`；5 轮均正常退出，`pidAlive=false`，无残留
  Augit/ffmpeg。

## 2. 方法

- `ffmpeg -f gdigrab -framerate 60` 采集编辑区左上 `800×500` 物理像素为 raw gray，4 s。
- 采集首帧落盘后轮询 raw 文件长度；`t0 = now - framesWritten/60`。
- `t0 + ~805 ms` 时把 `refresh.txt` 第 2 行从 `generation 0` 改成 `generation 1`
  （整文件重写），记录 `writeAt`。
- 以首帧为基线，逐帧统计灰度差 > 8 的采样点（每 2 像素一次）；**连续 3 帧变化 > 200**
  的第一帧记为首内容帧。首内容滞后 = `onsetFrame/60*1000 - (writeAt - t0)`。
- 保留基线与首内容帧 PNG，删除百 MB 级 raw。

## 3. 结果

| 批次 | 轮次 | 主题 | 写入偏移 | 首内容帧 | 首内容滞后 |
| --- | --- | --- | --- | --- | --- |
| i | light-1 | Light | 805 ms | 85 | **611 ms** |
| i | light-2 | Light | 808 ms | 65 | 276 ms |
| i | dark-1 | Dark | 808 ms | 65 | 275 ms |
| j | light-1 | Light | 807 ms | 64 | 259 ms |
| j | light-2 | Light | 813 ms | 64 | 254 ms |
| j | light-3 | Light | 815 ms | 82 | **552 ms** |
| j | light-4 | Light | 809 ms | 63 | 241 ms |
| j | dark-1 | Dark | 807 ms | 66 | 293 ms |

- 慢样本不是“窗口未激活”或空帧：慢样本的稳定变化像素都是 **754**（深色 10797），与正常样本
  相同，即第 2 行文字本身；首内容帧 PNG 可见对应 generation。
- 表内两次慢样本分别出现在批次 i 的第 1 轮与批次 j 的第 3 轮，后续 gdigrab 批次又出现一次
  **624 ms**；**不能只归因于首次冷启动**。
- 正常样本 241–294 ms；存在约 2 倍抖动。
- 该结果与 2026-10-23 的 380.0/406.7/287.0/259.3 ms（`d26f226`）不是同一构建，
  不能混成一组；本批说明当前 HEAD 仍会出现 >500 ms 慢样本，旧的 731.7 ms 也未复现。

## 4. 边界

- `t0` 来自 raw 文件长度轮询，最坏 ~1 帧；写入发生在一个 JS 宏任务内，文件监听合并
  窗口与 Git 状态查询都会进入这段滞后。
- 只测一个 40 行文件的**第 2 行变化**，只在编辑区左上 800×500 裁切；不是全窗、滚动区、
  大文件或外部分支/冲突同步。
- 只测一次 topmost 激活方式；没有证明普通非 topmost 前台点击、其他 DPI 或双屏。
- 没有逐帧全白/全黑统计；不能替代全窗刷新视频验收。
- 慢样本的具体阶段（文件监听、`git/status`、读取、渲染、合成）尚未归因，下一步应在
  host 侧加阶段时间戳后复测；不把“2 次慢”写成产品缺陷定论。

## 5. 证据与复验

```powershell
node tools/audit/refresh-content-onset.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/refresh-content-onset-2026-10-24j
```

- `artifacts/refresh-content-onset-2026-10-24j/report.json`：表内 j 批次 5 轮。
- `artifacts/refresh-content-onset-2026-10-24i/report.json`：表内 i 批次，包含 611 ms 慢样本。
- 后续 gdigrab 批次：`artifacts/refresh-content-onset-2026-10-24gg`（含 624 ms）、
  `...comb1` ~ `...comb4`（20 轮，全部 237–308 ms，同轮记录 stage）。
- ddagrab 对照：`artifacts/refresh-content-onset-2026-10-24dd`（5 轮 223–274 ms）。
- 同目录 `*-baseline.png` / `*-onset.png`：首帧与首内容帧；`*.ffmpeg.log`。
- 每轮记录 PID、窗口句柄、前台检查、裁切、写入偏移、首内容帧和滞后；实例正常关闭。

## 6. 同轮阶段诊断与采集方法对照（2026-10-24 后续）

**无采集的 12 轮阶段跟踪**（`tools/audit/refresh-stage-trace.cjs`，同一构建、同一夹具、
同一写入位置，只注入 request/MutationObserver 计时，不跑屏幕采集）：

| 阶段 | 结果 |
| --- | --- |
| `git/status` 开始→结束 | 50–115 ms |
| `document/read` | 1–4 ms |
| 正文 DOM 变化（相对写入） | **89–156 ms** |
| DOM 变化→下一帧 rAF | 3–4 ms |

**同轮 gdigrab + stage（`artifacts/refresh-content-onset-2026-10-24comb1`~`comb4`，20 轮）**：
屏幕首内容 **237–308 ms**，同轮 DOM 变化 **153–194 ms**，`document/read` 3–11 ms，
`git/status` 88–120 ms；20 轮没有出现 >500 ms。

**ddagrab 对照（5 轮）**：223–274 ms，没有 >500 ms。

**目前能说的**：
- 无采集的 DOM/桥接阶段没有出现 >300 ms 的慢段；12/12、20/20 都远低于 500 ms。
- 但无 CDP 的 gdigrab 原始样本出现了 **3/13** 次 >500 ms。慢样本的屏幕变化像素与正常样本相同，
  不是空帧；由于没有同轮阶段数据，不能判断慢在文件监听、合成、DWM 还是 gdigrab 自身。
- CDP 连接可能改变 WebView2 的调度/遮挡状态，gdigrab 与 ddagrab 也可能有不同采集开销；
  这两种相关因素都尚未排除。因此**不能把 3 次慢写成产品缺陷，也不能把 20 次快写成目标通过**。
- 下一步必须用 host 侧阶段时间戳（不依赖 CDP）与两种采集方式交错复测；在此之前保留
  611/552/624 ms 原始结果与 237–308 ms 对照结果。
