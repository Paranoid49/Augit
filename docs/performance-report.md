# Augit 阶段五性能验收报告

## 0. WebView2 时代的早期实测值（历史快照，不是当前基线）

以下数值是 WebView2 界面**早期**的一次抽样，**不是当前基线**；当前基线见 **§10**（第 212 轮建立，空仓库／已有仓库／大仓库三类场景各 3 次运行）。第 2–9 节保留为「原生 Win32 + Scintilla」外壳的历史记录，其中的启动与内存数字同样**不代表当前版本**。

| 指标 | 实测值 | 说明 |
| --- | ---: | --- |
| 冷启动到窗口出现 | 94–158 ms | Release、非 self-contained、真实外壳 |
| 10 万文件工作区首次可用 | 141–158 ms | 启动路径不递归扫描整棵目录 |
| Augit 主进程 Working Set | 约 62 MB | 承载 Git、文件、终端与桥接逻辑 |
| WebView2 进程树 Working Set | 约 464 MB | 6 个进程：浏览器、渲染、GPU、网络等 |
| **合计 Working Set** | **约 540–560 MB** | 见下节口径说明 |
| 空闲 CPU | 0.08% | 事件驱动，无后台轮询 |
| 外部文件变化反映 | < 450 ms | 目标 500 ms |
| 单实例激活已有窗口 | 102–140 ms | 按工作区路径的内存映射登记 |
| `git/history`（10 万提交） | 约 271 ms | 含 `--topo-order`；排序正确性优先 |

**内存口径**：只统计 Augit 主进程会严重低估真实占用；只统计 `msedgewebview2` 进程会把其他应用的浏览器进程算进来。必须按父进程关系把 WebView2 进程树归属到本实例。实测中曾因未做归属，把 540 MB 误报为 822 MB（多算了 6 个属于其他程序的 WebView2 进程）。

**100 MB Working Set 目标在当前技术基线下不可达。** WebView2 采用 Chromium 多进程架构，其固有多进程开销是这个量级差距的全部来源，不是实现缺陷。达到该目标只能改用自绘或原生控件界面，代价是放弃视觉稿与 HTML/CSS 的复用关系。

## 1. 结论

本报告第 2–9 节记录历史版本（原生 Win32 + Scintilla 外壳）的验收与诊断；**它们不适用于当前 WebView2 界面版本**，不作为当前性能结论。当前版本的三类场景基线见 **§10**；第 0 节是更早的一次 WebView2 抽样，仅作历史对照。

现行启动与流畅性目标为持续优化目标；交互流畅性与完整视觉还原优先，任何超出都必须记录实测值、原因分析和可复现的测量方法。


2026-08-28 在 Windows 11 x64 实机上完成 Release、`win-x64`、非 self-contained 发布版的阶段五性能验收。已测项目满足 1 秒启动、100 MB 核心 Working Set、按需资源回收、500 毫秒本地变化同步、大规模仓库响应和空闲 CPU 指标。

本报告记录 Windows 11 管理员安装、覆盖升级和卸载验收结果。Windows 10 22H2 实机验收按用户要求不纳入本轮。

## 2. 测量环境

| 项目 | 实际环境 |
| --- | --- |
| 操作系统 | Windows 11 家庭版中文版 25H2，`10.0.26200.9168`，x64 |
| 处理器 | AMD Ryzen 7 8845HS，8 核 16 线程 |
| 内存 | 31.29 GiB |
| 磁盘 | CT1000P3PSSD8，NVMe SSD |
| .NET Runtime | Microsoft.NETCore.App `10.0.5` |
| WebView2 Runtime | 已安装 `151.0.4129.107` |
| Git | Git for Windows `2.45.1.windows.1` |
| 发布方式 | Release、`win-x64`、非 self-contained 便携版 |

基准要求是 8 核 CPU、16 GB 内存和 SSD。本机核心数与存储类型符合要求，物理内存高于基准，因此内存结论只按进程实际占用判定，不把机器剩余内存作为性能依据。

## 3. 测量口径

- 启动时间从创建进程开始，到主窗口完成首次渲染且能响应窗口消息为止。
- 核心内存在界面可操作后空闲 5 秒测量，关闭 Markdown、终端和冲突解决器，并统计 Augit 核心进程树。
- 大仓库使用 100 个目录、每目录 1000 个文件的十万文件工作区；历史验收使用实际包含十万条提交对象的 Git 仓库。
- Markdown 和终端分别连续打开、关闭 10 次；首次和第十次关闭后均空闲 10 秒再读取主进程 Working Set。
- 界面响应使用 250 毫秒窗口消息超时探针，记录加载期间的最大响应时间和超时次数。
- 正式测量前临时停止 `NahimicSvc64`、`NahimicSvc32` 和 `nahimicNotifSys`，并逐模块确认目标进程没有加载 Nahimic、`AudioDevProps2.dll` 或 E-SafeNet。测试结束后按原命令恢复三个组件，`NahimicService` 保持原有的自动启动和运行状态。

## 4. 核心启动、内存与空闲 CPU

| 轮次 | 首次可操作 | 空闲 5 秒 Working Set | 私有内存 | 空闲两秒 CPU 增量 |
| --- | ---: | ---: | ---: | ---: |
| 1 | 336.56 ms | 41.18 MiB | 10.18 MiB | 0 ms |
| 2 | 151.03 ms | 41.30 MiB | 9.52 MiB | 0 ms |
| 3 | 145.97 ms | 41.43 MiB | 9.63 MiB | 0 ms |

三轮均低于 1 秒启动和 100 MB 核心 Working Set 优化目标。测量时没有 Augit 子进程，目标进程没有加载被禁止的第三方注入模块，空闲期间也没有持续 CPU 活动；这两个数值现为持续优化目标，不是牺牲交互流畅性或视觉还原的硬门槛。

## 5. 按需资源与回收

| 能力 | 首次关闭后 Working Set | 第十次关闭后 Working Set | 增量 | 关闭后资源 |
| --- | ---: | ---: | ---: | --- |
| Markdown | 79.27 MiB | 75.99 MiB | -3.29 MiB | 每次对应 WebView2 子进程均退出 |
| 内置终端 | 77.23 MiB | 82.68 MiB | 5.45 MiB | 每次对应 WebView2、Shell、子进程树和会话目录均回收 |

两项增量均低于 20 MB，且没有随开关次数线性增长。终端测试同时覆盖空闲关闭、前台命令识别、确认后结束完整进程树和 Worktree 会话锁释放。

## 6. 大规模数据与界面响应

| 场景 | 结果 | 判定 |
| --- | --- | --- |
| 十万文件工作区 | 三轮首次可操作为 `336.56/151.03/145.97 ms`，启动路径未递归扫描整棵目录 | 通过 |
| 十万提交历史 | 最终便携版首屏耗时 `676.98 ms`，列表只创建 100 条；消息探针最大 `22.82 ms`，零次 250 毫秒超时 | 通过 |
| 一万行真实 diff | 加载耗时 `179.49 ms`；消息探针最大 `0.43 ms`，零次 250 毫秒超时 | 通过 |
| 外部文件变化 | 自动化测试在 500 毫秒内更新只读视图和冲突结果区 | 通过 |
| 本地 Git 状态变化 | 事件驱动合并延迟为 50 毫秒，自动化测试在 500 毫秒内观察到最新状态 | 通过 |
| 远端状态 | 没有后台 fetch 或轮询，只在用户主动执行 fetch 或 pull 后刷新 | 通过 |

## 7. 发布产物与复现性

发布脚本从锁定依赖还原开始，在独立暂存目录中完成发布、许可证收集、安装器构建和校验。只有全部检查通过后才替换最终产物；失败会清理暂存区，不留下新的半成品。

阶段五性能测量时的回归共 252 项全部通过，其中核心 50 项、基础设施 108 项、应用 94 项；Release 构建为零警告、零错误。后续 UX/UI 重构新增了自动化覆盖，后续回归数量以各次测试记录自身的结果为准。发布修正后又在临时停止三个 Nahimic 用户组件的环境中，对 Markdown 与终端各连续开关 10 次完成针对性资源回收复验，两项均通过；复验结束后三个组件恢复，`NahimicService` 测试前后均保持自动启动和运行状态。原生程序嵌入 `asInvoker`、Windows 10/11 兼容、长路径感知和 ComCtl32 v6 清单；发布脚本使用 UTF-8 BOM 保存中文信息，并通过自动化测试和真实构建确认 Windows 自带 PowerShell 5.1 能够正确解析执行，不要求额外安装 PowerShell 7。

当前产物如下，最终交付前以 `artifacts/SHA256SUMS.txt` 的复核结果为准：

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-0.1.0-win-x64-portable.zip` | 3,201,655 字节 | `3debb5d0ea268b6c8a1ebd7494fc4599b7ea80c3a5fafca5d439bb8cfce31cee` |
| `Augit-0.1.0-win-x64-setup.exe` | 4,703,977 字节 | `727ab518804b004e89c56b9de93e370f8d8d7d5029fce7bdf55559e4e4a26dcb` |

发布校验覆盖必需文件、第三方许可证、非 self-contained、无 PDB、固定运行时下载地址、文件大小、SHA-256 和有效微软数字签名。

补齐原生应用清单并重建上述产物后，又对正式便携版执行三轮轻量启动回归。首次响应为 `230.81/174.57/173.02 ms`，空闲 5 秒 Working Set 为 `38.64/38.96/38.89 MiB`，私有内存为 `8.57/8.49/8.44 MiB`，空闲两秒 CPU 时间增量均为 `0 ms`；目标进程没有加载 Nahimic、`AudioDevProps2.dll` 或 E-SafeNet。测量期间临时停止的三个 Nahimic 用户组件均按原命令恢复，`NahimicService` 始终保持自动启动和运行状态，用户设置、测试进程和临时目录均已恢复或清理。

2026-09-13 按当前 Release 构建再次进行轻量启动抽样（Windows 11 x64，未停用系统组件）：首次出现主窗口分别为 `307.89/297.71/367.08 ms`，对应 Working Set 为 `47.39/47.31/46.27 MiB`。三轮均正常关闭，未留下 Augit 进程；该抽样只更新当前构建的基线，不替代 Windows 10 22H2 实机或完整连续输入性能验收。

Windows 11 管理员验收先安装 `0.0.9`，再覆盖升级到 `0.1.0`，两次安装退出码均为 `0`，启动到窗口耗时分别为 `689.53 ms` 和 `615.35 ms`，应用均能正常退出；卸载退出码为 `0`，人工兜底清理项为零。验收前后均不存在安装目录、卸载记录、开始菜单快捷方式、系统 PATH 中的 Augit 项、资源管理器右键菜单和安装器所有权注册表项，用户设置 SHA-256 指纹保持 `A7C73216F65DE1E8D606487851F28483BA5711DD70E1AB70F77956DA308E99A5`。系统 PATH 逐字恢复为安装前的注册表值，.NET Runtime 保持 `10.0.5` 与 `8.0.25`，WebView2 Runtime 保持 `151.0.4129.107`，没有发生系统组件更新。

## 8. 尚未完成

- Windows 10 22H2 x64 实机验收按用户要求不纳入本轮。
- 当前机器实际使用 .NET Runtime `10.0.5`，满足正式产物声明的 `10.0.0` 兼容下限；Windows 11 管理员验收确认安装器不会为兼容运行时强制下载锁定版 `10.0.11`。Windows 10 22H2 的该行为不在本轮实机验收范围内。
- Windows 10 22H2 实机验收不作为本轮阶段五和首版发布条件。

## 9. 2026-09-13 项目树增量刷新诊断

本节使用当前 Release 应用程序集和真实 Win32 消息循环，但宿主是 MSTest，不是正式便携发布版。因此只记录项目树操作诊断，不重写上方历史冷启动、核心 Working Set 或资源开关验收结论。

- 环境：Windows 11 家庭版 25H2，`10.0.26200.9445`，x64；AMD Ryzen 7 8845HS，8 核 16 线程，31.29 GiB 内存，CT1000P3PSSD8 NVMe SSD，.NET `10.0.5`。
- 样本为隔离的本地非 Git 工作区，100 个目录各有 1000 个空文本文件，另有少量上下文文件；准备过程不计入界面操作时间。没有停用 Nahimic 或其他系统组件，没有启动 WSL、终端或运行时安装。
- 初始已登记树节点少于 120，展开一个千项目录后少于 1120；新增文件后的刷新保持已选文件的原生节点及主窗口布局。该用例不验证十万提交 Git 历史。
- 操作计时包含目录查询、界面更新及探针收尾；消息探针在独立测试任务中每约 5ms 发送一次 `WM_NULL`，单次超时 250ms。它不能证明所有鼠标、键盘和绘制延迟都有相同上限。

| 操作 | 完成时间 | 探针次数 | 最大消息响应 | 250ms 超时 |
| --- | ---: | ---: | ---: | ---: |
| 千项目录首次展开 | 574.16ms | 40 | 19.82ms | 0 |
| 千项目录增量刷新 | 49.87ms | 2 | 2.01ms | 0 |

实现现已让 F5 和外部变化共用增量入口，保留未变化节点及其展开子树；大批量增删按时间片交回消息循环，完成时不覆盖用户改选、收起或工作区切换。首轮回归发现加载中收起被旧展开标记覆盖，已经修复并保留失败记录；排序断言改为核对目录服务实际提供的 Windows 自然顺序。

最终项目树、文档标签、工作区恢复及状态队列组合回归为 107/107，0 失败、0 跳过；完整格式检查通过。原始输出保存在 `artifacts/project-tree-incremental-2026-09-13/tree-incremental-final.trx`，被测 `Augit.dll` 的 SHA-256 为 `22A90D3E17FFD966572FA77E939ED155AC53262FEB5D4588F8662C8C7D0EA80E`。隔离文件由测试清理，测试与构建进程在收尾后退出。

当前正式发布版冷启动、核心内存、十万提交历史、按需资源反复开关以及全应用连续输入性能仍未重新验收；本节也不替代 Windows 10 22H2 实机兼容性或 PyCharm 同内容视觉对照。

## 10. 三类场景的当前基线（第 212 轮建立，2026-09-26）

本节是**当前基线**，取代第 0 节那次早期 WebView2 抽样。目的按目标文本执行："先在当前 Windows 11 x64 环境建立冷启动空仓库、已有仓库和大仓库三类场景的启动速度、操作响应、内存占用、关闭速度和资源清理基线，再持续优化。"

### 10.1 测量环境

| 项 | 实测环境 |
| --- | --- |
| 操作系统 | Windows 11 家庭版中文版 25H2，`10.0.26200`，x64 |
| 处理器 | AMD Ryzen 7 8845HS（8 核 16 线程） |
| 内存 | 31.29 GiB |
| 磁盘 | CT1000P3PSSD8（NVMe SSD） |
| .NET Runtime | `Microsoft.NETCore.App 10.0.5` |
| WebView2 Runtime | `153.0.4234.48`（比第 0 节记录的 `151.0.4129.107` 新） |
| Git | Git for Windows `2.45.1.windows.1` |
| 被测程序 | `src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe`（Release、非 self-contained） |
| 主题 | `--theme dark` |

> **Windows 10 22H2 未验证**：本节全部读数来自 Windows 11 x64 实机，不构成 Windows 10 结论。

### 10.2 方法与命令

测量脚本：`tools/audit/measure-performance.ps1`（第 212 轮新增，BOM-less 纯 ASCII，符合 `verify-script-encoding.ps1`）。

```powershell
powershell -NoProfile -File tools/audit/measure-performance.ps1 `
  -Exe <Augit.exe> -Workspace <工作区> -Scenario <名字> `
  [-LargeDirectory <相对目录>] [-OpenFile <相对文件>] `
  -Out artifacts/perf-20260926/<场景>-<n>.json
```

口径：

- **启动到窗口出现**（`windowMs`）：`Start-Process` 到主窗口句柄出现，10 ms 轮询。
- **首屏可用**（`pageReadyMs`）：到页面 `window.__augitReady === true`，经 CDP `Runtime.evaluate` 30 ms 轮询。
- **Git 与历史就绪**（`gitReadyMs`）：到 `__augitGitReady && __augitHistoryReady`。
- **操作响应**：在页面内用 `performance.now()` 包住真实宿主调用 —— 根目录列举（`workspace/list`）、指定大目录列举（1000 项目录）、打开文档（`document/read`）；另有应用自身埋点 `__augitMarks`（`info`／`root`／`status`／`open`）。
- **内存**：空闲 5 秒后按**父进程关系**遍历被测进程的全部后代，分开记 `Augit` 主进程、`msedgewebview2` 树、以及其它后代（例如浏览器拉起的第三方覆盖层进程）——避免把别的应用的浏览器算进来，也不把第三方助手混进 WebView2 数字。
- **空闲 CPU**：主进程 `TotalProcessorTime` 在 2 秒窗口内的增量。
- **关闭速度**（`shutdownMs`）：`CloseMainWindow()` 到进程退出。
- **资源清理**（`descendantsLeft`）：关闭后轮询最多 15 秒，后代进程必须为 0。
- 每次运行前备份 `%LOCALAPPDATA%\Augit\settings.json`，结束时**按字节还原**（本次运行不写回任何持久设置）；`--width/--height/--theme` 是审计覆盖，本身不写回。
- 每场景 3 次独立运行；下表给 min／median／max。

### 10.3 场景定义（可复建）

| 场景 | 工作区 | 定义 | 复建命令 |
| --- | --- | --- | --- |
| 空仓库 `empty-repo` | `D:\tmp-augit-perf\empty-repo` | `git init`，无提交、无文件 | `git init` |
| 已有仓库 `existing-repo` | `D:\github\Augit` | 本仓库自身（README／C#／web／docs，含真实历史与未提交状态） | 无 |
| 大仓库 `large-repo` | `D:\tmp-augit-perf\large-repo` | 100 目录 × 1000 个文件（共 100 100），单条基线提交，目录被 `.gitignore` 忽略 ⇒ 工作区大而状态为空 | 见下 |

大仓库复建（一次性，2026-09-26 实测建目录约 4 分 40 秒，NTFS／drvfs）：

```bash
python3 - <<'PY'
import os
root = "/mnt/d/tmp-augit-perf/large-repo"
for d in range(100):
    dd = os.path.join(root, f"dir-{d:03d}")
    os.makedirs(dd, exist_ok=True)
    for i in range(1000):
        open(os.path.join(dd, f"f-{i:04d}.txt"), "w").close()
PY
cd /mnt/d/tmp-augit-perf/large-repo && git init -q && printf 'dir-*/\n' > .gitignore \
  && git add .gitignore && git commit -q -m "test: large workspace baseline"
```

> 大仓库的**历史**是单条提交（本机不具备快速生成十万提交 fixture 的条件）。因此本节的大仓库结论覆盖"大工作区 + 大目录列举"，不覆盖"十万提交历史"——后者仍是历史缺口（第 0 节的 `git/history` 271 ms 是早期抽样，不是本轮实测）。
>
> 该 fixture 与 9 份结果 JSON 一起登记；fixture 属临时测试资源，性能模块收尾时按 `docs/development-validation.md` §5 删除。

### 10.4 实测结果（每场景 3 次：min／median／max）

| 场景 | 窗口 ms | 首屏 ms | Git+历史 ms | 列举根目录 ms | 列举大目录 ms | 打开文档 ms | 主进程 WS MB | WebView2 WS MB | 树合计 WS MB | 关闭 ms | 残留 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 空仓库 | 302／324／326 | 969／990／1000 | 1149／1167／1171 | 1／1／28 | 不适用 | 不适用 | 57.38／57.55／57.63 | 528.61／543.37／543.58 | 586.24／600.74／601.14 | 126／128／132 | 0 |
| 已有仓库 | 293／305／308 | 977／980／981 | 1273／1283／1289 | 6／15／21 | 2／2／99（61 项） | 35／38／38 | 64.52／64.62／69.85 | 614.05／631.03／634.47 | 678.57／695.65／704.32 | 116／136／137 | 0 |
| 大仓库 | 294／322／330 | 972／998／1013 | 1173／1182／1199 | 2／2／11 | 55／55／57（1001 项） | 26／27／28 | 64.23／64.26／64.49 | 558.69／563.29／580.04 | 622.95／627.52／644.52 | 116／118／147 | 0 |

应用自身埋点（3 次一致）：工作区信息／根目录 `info=root` ≈ 29–30 ms（空仓库）、120–124 ms（已有仓库）、121–125 ms（大仓库）；`git/status` ≈ 398–423 ms（三场景接近，说明状态耗时与文件数弱相关，索引干净时按索引读）。

空闲 CPU（主进程 2 秒 CPU 增量）：0／0／15.6 ms 交替出现（15.6 ms 是采样窗口边界处的取整），即**空闲期无可观测的持续 CPU 活动**。

其它后代进程：三场景均为空（`otherDescendants=""`）——即被测进程树里没有第三方注入进程；此前一次运行里曾出现浏览器拉起的 AMD 覆盖层进程（`AMDRSServ`／`amdown`／`AMDRSSrcExt`），已按名称单独统计，不再混入 WebView2 数字。

### 10.5 与历史数值的关系（不要混用）

| 指标 | 第 0 节（早期抽样） | 本节（当前基线，median） | 说明 |
| --- | ---: | ---: | --- |
| 冷启动到窗口出现 | 94–158 ms | 305–324 ms | 口径不同：本节是"到主窗口句柄出现"，且经 CDP 附加（`--browser-args --remote-debugging-port`）。不能与旧值直接比较 |
| 首屏可用 | 未单列（旧"首次可用"141–158 ms） | 980–998 ms | 本节口径含"页面就绪 + 进程冷启"，且是 WebView2 冷启链路；旧值口径不同 |
| 10 万文件工作区 | 141–158 ms | 大仓库窗口 322 ms／首屏 998 ms | 旧值测的是"启动路径不递归扫描"；本节同样不递归（`info/root` 121–125 ms），三场景差异很小 |
| 主进程 Working Set | 约 62 MB | 57.6／64.6／64.3 MB | 一致 |
| WebView2 树 Working Set | 约 464 MB | 543／631／563 MB | WebView2 Runtime 已从 151 升到 153，且本节把 6 个浏览器子进程全数计入 |
| 合计 Working Set | 约 540–560 MB | 601／696／628 MB | 同上；仍远超 100 MB 目标，原因见第 0 节 |
| 空闲 CPU | 0.08% | 无可观测持续活动 | 一致 |
| 关闭 | 未单列 | 128／136／118 ms，`forcedClose=false` | 本节新增 |
| 资源清理 | 未见成文断言 | 后代残留 **0**（三场景 × 3 次） | 本节新增 |

**结论**：三类场景的启动、操作响应与关闭都在同一量级，**大仓库不因文件数变慢**（根目录只列一层，`info/root` 121–125 ms 与已有仓库持平；唯一的规模相关项是列 1000 项目录 55 ms vs 61 项目录 2 ms）。当前可优化的最大项是**首屏链路**（窗口 300 ms → 页面就绪 ~980 ms，差值约 680 ms 花在 WebView2 初始化与页面首帧），以及 WebView2 树的内存基数；这两项都属"持续优化"范围，本轮只建立基线，不改实现。

### 10.6 复跑与清理记录

- 全部 9 次运行：`descendantsLeft=0`、`forcedClose=false`；脚本 `finally` 中已把 `%LOCALAPPDATA%\Augit\settings.json` 还原为运行前字节。
- 结果 JSON：`artifacts/perf-20260926/{empty-repo,existing-repo,large-repo}-{1,2,3}.json`（含每次的完整字段）。
- 启动的进程：仅本脚本自己 `Start-Process` 的 Augit 进程及其 WebView2 后代；未触碰任何其它 Augit 实例或用户进程。
- 大仓库 fixture（`D:\tmp-augit-perf\large-repo`）与空仓库 fixture 保留到性能模块收尾，届时删除；本节保留复建命令以便重建。

## 11. 首屏链路的实测拆分与一次被否掉的优化（第 227 轮）

§10 把"窗口出现 → 页面就绪"的约 **680 ms** 差值列为最大可优化项，但没有拆开。本轮给测量脚本加了
三个字段（`breakdown`／`launchEpochMs`／`webviewFirstStartMs`），把这条链路切成**可归因的五段**，
并做了一次 A/B；**结论是上一段的可优化空间比看上去小得多**，这一段记录过程与数据，避免下轮重复试错。

### 11.1 新增字段与分段口径

| 字段 | 来源 | 含义 |
| --- | --- | --- |
| `launchEpochMs` | 脚本在 `Start-Process` **之前**记的墙钟毫秒 | 页面时钟（`performance.timeOrigin` 是 epoch 毫秒）与脚本时钟的对齐锚点 |
| `breakdown.timeOrigin - launchEpochMs` | 两者相减 | **导航开始**距启动的毫秒数 |
| `breakdown.loadEnd` | 页面 `PerformanceNavigationTiming` | 文档、CSS、脚本全部就绪（页面时钟，从导航开始算） |
| `pageReadyMs - nav - loadEnd` | 脚本时钟 | **页面自身 boot**（`loadDocument` 的宿主往返 ＋ mockup 注入 ＋ 首帧渲染 ＋ 绑定） |
| `webviewFirstStartMs` | 被测进程树里 `msedgewebview2` 的 `StartTime` | **WebView2 浏览器进程**出现的时刻；到导航开始之间是**环境握手 ＋ 控制器创建** |

### 11.2 复跑基线（每场景 3 次，2026-09-26；脚本与 §10 同一条命令 ＋ 新字段）

| 场景 | 窗口 ms | 浏览器进程 ms | 环境＋控制器 ms | 页面载入 ms | 页面 boot ms | 首屏 ms | Git+历史 ms | 主进程 MB | WebView2 MB | 树 MB | 关闭 ms | 残留 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 空仓库 | 289／323／270 | 302／340／311 | 374／350／362 | 41／39／41 | 245／266／224 | 962／995／938 | 1181／1191／1159 | 58／57／57 | 523／534／541 | 580／591／598 | 117／191／114 | 0 |
| 已有仓库 | 297／300／301 | 329／347／345 | 348／368／373 | 39／41／46 | 266／242／236 | 982／998／1000 | 1274／1297／1337 | 67／67／67 | 613／615／615 | 680／682／682 | 143／133／122 | 0 |
| 大仓库 | 313／314／319 | 341／344／335 | 364／346／336 | 41／38／46 | 393／269／274 | 1139／997／991 | 1391／1192／1174 | 64／63／64 | 575／575／558 | 640／639／622 | 147／133／144 | 0 |

三场景 `descendantsLeft=0`、`forcedClose=false`；与 §10 的读数逐个同量级（首屏 962／998／997 vs 969／977／972），
说明基线可复现。

宿主侧探针（临时 `Stopwatch` 埋点，取证后已删除）给出 `Main` 内部的毫秒分布，空仓库典型一次：

```
main=5  parse=21  settings=87  workspace=88  window-ctor=120  window-show=133
wv-init-enter=138  wv-loader=140  wv-clean=142  wv-env=159  wv-controller=439
wv-pre-navigate=455  wv-navigate=457
```

即：`ShellOptions.Parse` ≈ 16 ms、`LoadSettings` ≈ **66 ms**（小文件 1.2 KB，成本是 JSON 序列化器的首次预热与
JIT，不是磁盘）、窗口创建 ≈ 32 ms、`CoreWebView2Environment.CreateAsync` 仅 ≈ 17 ms、
**`CreateCoreWebView2ControllerAsync` ≈ 280 ms**（最重的一段，WebView2 内部）。

### 11.3 一次被否掉的优化（页面错误收集器内联）

**想法**：导航前 `await AddScriptToExecuteOnDocumentCreatedAsync(...)` 是一次 IPC 往返（宿主探针显示
`wv-controller → wv-pre-navigate` 有 **72 ms**）。这段脚本是静态的，挪进 `web/index.html` 的同步脚本里
即可省掉这次等待。

**A/B**（空仓库；"优化臂" n=5 与"对照臂" n=3，同一脚本、同一构建方式）：

| 臂 | 导航开始 ms（中位） | 页面首个资源 ms | 页面载入 ms（中位） | 首屏 ms（中位） |
| --- | ---: | ---: | ---: | ---: |
| 基线（§11.2） | 676 | 15 | 41 | 962 |
| 优化臂（去掉导航前 await） | **642** | 53 | **82** | 1003 |
| 对照臂（改回原样重测） | 704 | 16 | 40 | 992 |

**读法**：导航确实**提前了约 60 ms**（宿主探针同向：`wv-pre-navigate` 511 → 438～455），
但页面自己的资源与 `load` 时序**整体后移约 40 ms** —— 也就是那段等待原本在**掩盖渲染器进程的启动**，
把它去掉只是把等待搬到了页面侧。首屏中位数在双方噪声内（992 vs 1003），**没有净收益**。
因此**不采用该改动**，产品代码保持原样（该轮只保留测量能力）。

**这一段给出的结论**：`窗口 300 ms ＋ 控制器 280 ms` 是放不掉的地板（WebView2 内部）；
剩下真正属于我们的只有**页面 boot ≈ 240–270 ms**（宿主往返 ＋ mockup.js 注入求值 ＋ 首帧整页渲染 ＋ 绑定）。
下一轮应先用 `performance.mark` 把这三者分开再决定改什么，而不是继续在宿主侧挪等待。

### 11.4 复跑与清理记录

- 结果 JSON：`artifacts/perf-20260927/`（`empty-repo|existing-repo|large-repo-{1,2,3}.json` 为 §11.2 基线；
  `opt-5x-{1..5}.json` 与 `ctrl-3x-{1,2,3}.json` 为 §11.3 的 A/B 两臂；同目录已在 `.gitignore` 里放行）。
- 全部运行：`descendantsLeft=0`、`forcedClose=false`；脚本 `finally` 已按字节还原
  `%LOCALAPPDATA%\Augit\settings.json`。
- 临时宿主埋点（`StartupTrace`）与内联脚本试验都已**从产品代码移除**；本轮产品代码零改动，
  唯一保留的脚本改动是 `tools/audit/measure-performance.ps1` 的新增字段（BOM-less ASCII，`verify-script-encoding` PASS）。
- 大仓库/空仓库 fixture 仍需保留到性能模块收尾。

## 12. 页面 boot 的细分与两次被否的优化（第 228 轮）

§11 把可优化面收窄到"**页面 boot ≈ 250 ms**"。本轮把它再拆一层，并按两个候选各做一次实验；
**两次都因实测不成立而回退，产品代码零改动**（本节只留数据与方法，便于下轮直接从这里继续）。

### 12.1 方法（可复用）

在 `live-data.js` 的 `boot()`（`b1_loadDocument`／`b2_mockupLoaded`／`b3_requestedDone`／`b4_rebind`）
与 `mockup.js` 的末尾（`p0_mockupEval`／`p1_renderScene`／`p2_bindInteractions`／`p3_typography`）
临时写 `window.__augitMarks[名字] = Math.round(performance.now())` —— 该对象**本来就是审计埋点**
（`info`／`root`／`status`／`open`），测量脚本的 `marks` 字段直接带出来，不需要改脚本。
取证后这些埋点已移除。

### 12.2 一次典型的页面 boot（空仓库；页面时钟 ms）

| 段 | 值 | 归属 |
| --- | ---: | --- |
| `loadDocument` 之前 | ~56 | 模块求值 + `boot()` 进入（与文档 `load` 事件几乎同时） |
| `loadDocument()` | **38** | 宿主往返：`workspace/info` + `workspace/list`（两者并行） |
| → mockup 脚本求值开始 | 17 | 动态插入 `<script>` + 取回（缓存命中 2 ms） |
| **`renderScene()` + `innerHTML`** | **4** | 整页标记构建与解析 —— 比预期小得多 |
| **`bindInteractions()`** | **63** | 约 25 个 `bind*` 逐个 `querySelectorAll` + 挂监听 |
| mockup 求值收尾 | ~11 | `load` 事件与 Promise 收尾 |
| `rebindAfterRender()` | 3 | 重绘后的绑定补齐 |
| 页面就绪 | ~195 | `__augitReady = true` |

即：**页面 boot 里最大的一项是 `bindInteractions`（~63 ms）**，其次是宿主往返（38 ms）；
"整页渲染"本身只有 4 ms —— 之前猜的"渲染太重"不成立。

### 12.3 候选一：设置读写的源生成序列化（**否掉**）

**动机**：宿主探针实测 `LoadSettings` **首次 73 ms、同进程第二次 1 ms**（文件只有 1.2 KB）⇒
成本是**反射式 `JsonSerializer` 的首次预热**（构造类型元数据与属性访问器），而它必须发生在建窗之前。

**做法**：为 `ApplicationSettings` 加 `[JsonSerializable]` 源生成上下文
（`JsonSourceGenerationOptions(PropertyNamingPolicy = CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)`，
与原先的 `JsonSerializerOptions` 逐项一致），`SettingsStore` 的两处调用改走上下文。

**结果**：`Augit.Infrastructure.Tests` **188 项里 3 项失败** —— 都是"文件里缺少某个键"的用例：

| 失败用例 | 期望 | 实测 |
| --- | --- | --- |
| `新配置使用中文界面字体而旧字体选择继续保留` | 读 `{"textFontFamily":"…"}` 得到该值 | 属性停在旧默认值 |
| `字体字号分别保存且旧配置保持原有字号` | 读 `{"fontSize":17}` 得到 17 | 0 |
| `分支面板按目录分组默认开启且可持久化` | 读 `{"theme":"Dark"}` 后分组仍为默认开启 | **false** |

**根因**（读生成代码确认）：生成器把 `record` 的 **init-only 属性当成"构造参数"**
（`ObjectWithParameterizedConstructorCreator` + `IsMemberInitializer`），创建对象时**逐个赋默认值**，
于是 JSON 里缺键的属性会**覆盖属性初始化器**（`= true`／`"Microsoft YaHei UI"`／`= 13` 全部丢失）。
反射路径是"先 `new()` 跑初始化器、再按存在的键覆盖"，语义不同 —— 这是**静默的默认值回归**，
被测试逮住。⇒ **回退**。要做成必须先改 `ApplicationSettings` 的成员形状（去掉 init-only / 改构造方式），
那是设置契约变更，须单独一批并逐条验证。

### 12.4 候选二：把 `bindInteractions` 的一部分延后到首帧之后（**本轮不做**）

63 ms 是当前最大的单项。可行的方向是把"需要先有交互才能碰到"的绑定（各对话框、设置页、草稿、
日志筛选草稿、详情滚动意图等）挪到 `__augitReady` 之后执行，只留"首屏就可能被点"的那些同步跑。
**不做的原因**是风险面已经有过先例：`bindInteractions` 里包含 `guardUnwiredNavigation`
（守卫未接线链接）与窗口/轨道/标签等入口，历史上正是"可交互但无绑定"的那段窗口里
点击未接线链接会把界面导航离开应用（`live-data.js` 的 `boot()` 注释记录了那一次实测）。
因此这一项需要：先给"哪些绑定属于首屏必须"定一份可复验清单，再让延后集合在
`__augitReady` 之后立刻补齐，并补一条断言（首帧后 N ms 内延后集合已绑定）。留给下一轮。

### 12.5 顺带发现并修掉的产品缺陷：默认场景把**样例文档**当成真实文件显示

追 `bindMarkdownModes` 那 37 ms 时发现：`shell()` 里 `let editorBody = markdownView();` 是**默认**正文，
而实时侧没有文档时 `editor` 会停在场景默认值 `"markdown"`（`live.editor` 此时还没被赋值），
于是 `liveMarkdownDocument()`／样例 `markdownView()` 把视觉稿的**样例文档**画了出来 —— 标题
"Augit 产品规格"、文件栏写着 `Augit › docs › product-spec.md　只读`，而 `live.document` 是 `null`。

**实测（stub 宿主 + 真机，`--no-session-restore`、空仓库）**：

```
修复前 PROBE {"editor":null,"doc":null,"path":"Augit › docs › product-spec.md　只读",
              "h1":"Augit 产品规格","hasEmpty":false,"errors":[]}
修复后 PROBE {"editor":null,"doc":null,"path":null,"h1":null,"hasEmpty":true,"errors":[]}
```

即：刚启动、还没打开任何文件时，界面显示的是一份**磁盘上并不存在**的文件，且没有任何错误提示。
**修复**：`main-project`（产品默认场景）下，没有任何视图可显示时把 `editor` 落成 `"empty"`
（"选择文件以查看内容"的无文档提示，规格 §6.7 提到的那个状态）；`--scene <名字>` 的审计/视觉稿场景
不在其列 —— 那些场景本来就靠样例正文演示排版与绑定。

**性能上没有可测收益**：修复前后各 3 次的空仓库中位数为 boot 245 → 238 ms、首屏 962 → 1000 ms
（同量级噪声；此前"优化臂 n=5／对照臂 n=3"的经验同样表明这一档差异不可辨）。原因是空态自身也要布局，
而整页首帧本来就要布局一次 ⇒ **这次改动只按产品缺陷记，不记性能收益**。

### 12.6 清理记录

- 两次实验的临时改动（`boot()`／`mockup.js` 埋点、`SettingsJsonContext` 与 `SettingsStore` 改动）
  都已从产品代码移除；`git status` 干净。
- 回退后复跑 `dotnet test tests/Augit.Infrastructure.Tests -c Release`：**188/188 通过**
  （也证明 12.3 的 3 项失败确由源生成改动引起）。
- `obj/generated`（为读生成代码而产出的中间目录）已删除。
- 第 228 轮的临时探针脚本（`probe-startup.ps1`，放在 `C:\Users\Public` 与 fixture 目录各一份）
  已删除；`emptyfix-{1,2,3}.json` 是 12.6 修复前后的对照读数，随本节入库。

## 13. WebView2 内存基数的构成（第 229 轮）

§8 的"内存基数优化"一直是空白项。本轮按进程类型把内存拆开、做了两组对照，并试了两个可能的开关。
结论与 §0 早就写下的一句一致（**100 MB Working Set 目标在当前技术基线下不可达**），本轮把"为什么"
量化到每一个进程。

### 13.1 构成（空仓库、深色、空闲 6 秒后；Working Set MB）

| 进程 | 默认（CDP 附加） | 无 CDP（2 次） | `--dpi 96` | 说明 |
| --- | ---: | ---: | ---: | --- |
| `msedgewebview2` `--type=gpu-process` | **232.9** | 213.3／229.3 | **186.3** | 合成器/GPU 共享表面，随**窗口像素面积与栅格化比例**变化 |
| `msedgewebview2`（浏览器进程） | 124.3 | 126.6／123.9 | 123.9 | Chromium 主进程 |
| `msedgewebview2` `--type=renderer` | 95.7 | 95.5／98.6 | 95.4 | 其中**页面 JS 堆只有 4.1 MiB used／6.5 MiB total**（`performance.memory`），其余是 Blink/V8 基线 |
| `Augit`（主进程） | 56.4 | 56.4／56.6 | 57.4 | 外壳自身 |
| `msedgewebview2` `--type=utility` ×2 | 35.9 ＋ 18.6 | 35.9／36.0 ＋ 18.6 | 35.9 ＋ 18.7 | 网络/存储等工具进程，无 CDP 时同样存在 |
| `msedgewebview2` `--type=crashpad-handler` | 12.3 | 12.3／12.3 | 12.3 | 崩溃上报 |
| **合计** | **576.1** | **558.6／575.3** | **529.9** | 与 §10 记录的 523–634 MB（WebView2 树）同量级 |

两条可复验的读法：

1. **审计的 CDP 附加只多占 20–35 MB**（576 vs 559–575）⇒ §10 记录的"WebView2 树"数字基本代表真实用户场景，
   不需要修正；但引用时要知道它含这点测量开销。
2. **GPU 进程与窗口像素面积/栅格化比例同向**：`--dpi 96` 让 `devicePixelRatio` 从 1.75 降到 1
   （逻辑视口 1646×981 → 2880×1716），GPU 进程 233 → 186 MB。**页面自身的 JS 堆只有 4 MB** ——
   也就是说 Augit 的内容与脚本在这 500 多 MB 里几乎不占份额。

### 13.2 两个开关试过都不行（不予采用）

| 开关 | 结果 |
| --- | --- |
| `--in-process-gpu` | **页面起不来**：25 秒内 CDP 没有 `index.html` 目标（WebView2 不支持把 GPU 进程并入浏览器进程） |
| `--disable-gpu-compositing` | 同上：页面起不来 |
| `--disable-gpu` | 早前已实测（`ShellWindow.cs` 的注释）：**WebView2 会忽略它**，GPU 进程依然存在 |

### 13.3 结论

WebView2 树的内存基数由 **Chromium 多进程架构 × 窗口像素面积**决定：GPU 进程（186–233 MB）、浏览器进程
（~124 MB）、渲染进程（~95 MB，其中页面只占 4 MB）三块就是全部量级。Augit 侧可动的只有主进程
（~57 MB）与页面（~4 MB），把它们压到 0 也进不了 100 MB 档；要进那一档只能放弃 Chromium/HTML/CSS 界面
（§0 已写明代价，且与"不切换技术栈"的目标约束冲突）。开关层面：能把 GPU 进程并掉的开关会让 WebView2
起不来，`--disable-gpu` 被忽略 ⇒ **没有安全的开关级优化**。

因此 §8 的"WebView2 内存基数优化"**没有剩余可执行项**，建议按"当前技术基线的地板（已取证）"结案；
本节的对照数据与 §10／§12 一起构成启动、响应、内存三项的当前结论。
