# 正常入口接续验收（2026-10-18）

## 基线与身份

- 分支 `dsh`，HEAD `d43e761e250075df3b7222dbbb0db9fdee3d32ac`；接续时工作区只有未跟踪 `.tmp/`，`git diff --check` 通过。
- 全部实机实例由 `tools/start-augit.ps1` 强制非增量构建启动；未使用 `-SkipBuild`、场景参数、DPI 覆盖或产品测试分支。
- 本批实际映射或进程路径核对的产物：
  - `Augit.dll` SHA256 `159123F29DEF5DAF6EFE66A9D30F430D6CD22CF22912CB0527BD0BF23EA8559D`
  - `Augit.Infrastructure.dll` SHA256 `A92BBE4CEFC2544220E5FC5D53510810C5A53A9947F5D6EF5613FD75D42894CE`
  - 普通冒烟、刷新、嵌套模态和设置映射批均得到同一对哈希；设置批 `mapped-images.json` 直接来自本实例。
- 环境仍加载 `AudioDevProps2.dll`（Nahimic），仅作诊断，不据此推断因果，也不作为性能验收通过理由。
- 当前构建追加默认工作区/默认设置、无 CDP 启动冒烟：PID11292，`start-augit.ps1` 强制构建，存活 10s、窗口标题 `Augit`，`CloseMainWindow=true` 且进程退出；这是启动可用性证据，不是整页视觉验收。

## 正常入口冒烟

命令与证据：`artifacts/pixel-followup-2026-10-18/normal-entry-smoke/`。

- PID 24608、端口 49328、强制构建 0 警告 0 错误。
- 纯文本只读；正文实际 `JetBrains Mono` 13px；内置终端真实输入输出、隐藏后恢复；Changes 单击不替换正文，Enter 打开只读 Diff。
- `window.__augitErrors`、页面异常和失败资源均为空。
- `CloseMainWindow` 成功，端口关闭，临时仓库已删除。

## 设置正常入口

三批均通过 `--daily-launcher` 走正常启动器、复制真实设置、默认 WebView2 profile，附 CDP 驱动；每批 10 个设置窗口，共 30 窗。

| 批次 | 原生 presented | 捕获调用返回 | 捕获异步等待 | 原生 close→destroyed | 动作关闭观察上界 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 深色 `settings-dark-cycle` | 341–399ms | ≤2.7ms | 74.6–101.1ms | 25.0–47.2ms | 57/104/55/79ms |
| 浅色 `settings-light-cycle` | 339–407ms | ≤2.7ms | 73.6–99.3ms | 24.6–34.6ms | 65/104/39/49ms |
| 浅色映射 `settings-light-cycle-mapped` | 375–497ms | ≤2.9ms | 80.3–100.3ms | 38.4–56.8ms | 86/145/70/74ms |

- Cancel、Save、Esc、标题关闭均完成；主题预览取消、持久化失败重试、保存应答迟到后取消重开、Apply 后重开均通过；无页面错误。
- `settings-light-cycle-mapped` 的 `mapped-images.json` 记录本实例实际加载的 Shell/Infrastructure 哈希，与其余批一致；进程路径与隔离设置副本见 `process-identity.json`。
- 30 次开窗全部未触发 500ms 捕获超时；本批未复现 2904ms 慢样本，也未制造真实慢样本，因此超时后的提前揭示路径仍未实机验证。另尝试 OS 级挂起 WebView2 renderer/GPU 七次，仍未触发 `capture-timeout`；按 CDP 进程差异精确挂起设置页 renderer（含 GPU）后捕获仍 81–93ms 完成，说明该路径不能触发超时，见[捕获超时挂起尝试](settings-capture-stall-2026-10-18.md)。
- 动作关闭耗时为 CDP 点击到设置状态 `active=false` 的上界，包含驱动派发与轮询；原生 `close-request→destroyed` 才是产品关闭阶段。
- 证据：`artifacts/pixel-followup-2026-10-18/settings-dark-cycle/`、`settings-light-cycle/`、`settings-light-cycle-mapped/`。

## 无 CDP 设置开窗

另跑无远程调试实例：`remoteDebugging=false`、`cdpConnections=0`，真实系统鼠标点击标题栏设置按钮。五次有效开窗原生呈现 **599.2 / 730.9 / 656.6 / 747.5 / 678.3ms**，捕获异步等待 92.1–103.3ms，未触发超时。

- 运行 8–14 用真实输入/键盘/标题关闭消息取得 7 次无 CDP 正常关闭，Settings 关闭、主窗 `CloseMainWindow` 成功：
  - 运行 8 浅色 Cancel：presented 678.3ms，close **59.1ms**，录像 `foreground=false`。
  - 运行 9 深色 Cancel：presented 638.0ms，close **72.7ms**，录像 `foreground=false`。
  - 运行 10 浅色同一进程重复两次 Cancel：presented **732.6/714.5ms**，close **67.9/55.8ms**，录像 `foreground=true`。
  - 运行 11 Save/OK：presented 663.8ms，close **54.8ms**，录像 `foreground=false`。
  - 运行 12 标题 WM_CLOSE：presented 679.6ms，close **86.7ms**，录像 `foreground=false`。
  - 运行 14 Esc（`keybd_event`）：presented 661.3ms，close **48.3ms**，录像 `foreground=false`；运行 13 的 `SendInput` 路径返回 0 失败，不计为 Esc。
- 已覆盖浅色/深色 Cancel、同一进程重复开关、Save/OK、标题关闭同路径和 Esc；仍需不同 DPI。
- 运行 5–7 在输入/截图前退出且无 Application Error 事件；直接启动诊断 3/3 存活 10s，原因未定位。简化夹具周期的“940×560 CSS 窄窗”已核实为审计侧 DPI 二次换算错误，实际窗口为 2880×1716 最大化（`no-cdp-main.png`），该结论撤回。
- 详见[无 CDP 设置开窗复核](settings-no-cdp-2026-10-18.md)。

## 真实嵌套模态关闭

新增 `tools/audit/nested-modal-close.cjs`，正常入口真实操作：标题栏分支弹层 → Push（无远端）→ Push 内真实的 Define remote → 关闭远端 → 返回原 Push → 关闭 Push。

证据：`artifacts/pixel-followup-2026-10-18/nested-modal-close-3/`（最终脚本；生命周期断言已拆到独立模块）。

| 状态 | 深度 | window | ownerWindow | parentWindow | 所有者启用 | 快照数 |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| Push 父窗 | 1 | 3211370 | 3999742 | 3211370 | 否 | 1 |
| Define remote 子窗 | 2 | 2361242 | **3211370** | 2361242 | 否 | 2 |
| 关闭子窗返回 Push | 1 | **3211370** | 3999742 | 3211370 | 否 | 1 |
| 关闭 Push | 0 | 0 | 0 | 3999742 | 是 | 0 |

- 子窗原生 owner 指向 Push HWND；返回时复用同一 Push HWND 和同一 DOM 节点，未重建父表单。
- 最终 Push 关闭后 `snapshotCount=0`、`ownerEnabled=true`、`captureCoverWindow=0`；`CloseMainWindow` 成功，端口关闭，临时目录删除，页面错误为空。
- 生命周期断言拆到 `tools/audit/nested-modal-close-state.cjs`，5 项正/负向测试通过。
- 该场景验证真实产品模态栈，不是注入表单；仍不代表全部嵌套模态状态已逐一验收。

## 背景刷新

### 轻量严格计时

正常入口、无全主窗录像，PID 4888、端口 60701。四轮严格撤盖（写入→最后 `window/modal-uncover` 应答）为 **460.9 / 339.6 / 352.8 / 359.5ms**，全部低于 500ms；外部文件内容均为最后写入版本。证据：`artifacts/pixel-followup-2026-10-18/refresh-current/`。

### 全主窗录像与首个真实内容帧

正常入口、全主窗 GDI 录像，PID 10204、端口 56646，2880×1716、约 20.4fps（约 49ms/帧）。严格撤盖为 **651.6 / 421.7 / 402.5 / 390.4ms**；首个可见新内容帧为 **731.7 / 494.7 / 462.5 / 480.2ms**，比撤盖应答再滞后 **60.0–89.8ms**；这是采样上界，真实滞后约 11–90ms。

- 第 1 轮在录像负载下首个新内容帧超过 500ms；第 2–4 轮低于 500ms。
- 新工具 `tools/audit/measure-refresh-content-onset.py` 以 `x=760,y=200,w=380,h=500` 的正文裁切区区分新旧 generation；旧/新差异 1248–1898 像素，首帧与静置新内容差异 0 像素。5 项测试通过。
- 因此“最后一次 `window/modal-uncover` 应答”只能作为宿主代理；不能单独据此宣称屏幕可见 500ms 通过。详见[首个可见新内容帧](refresh-content-onset-2026-10-18.md)。
- 仍未覆盖裁切区外、未采样帧、全部 Present、全部 Git 元数据、不同 DPI/负载和整窗无闪。

## 保留模态：跳转行空输入

复用参考系统 PNG 与当前正常入口 Go to Line 空输入截图，同浅色、UI12、DPI168、聚焦状态：

- 字段均为 200×34 CSS，相对 dialog x=113、y=52 CSS；按钮 78×34、间距 6 CSS。
- 字段蓝色框参考 `[203,95,546,147]`、实际 `[202,94,545,147]`，宽高相同，仅 1 物理像素左/上偏移；旧记录的“整体 3px 左移”在当前构建不再成立。
- ROI `[190,85,562,160]`、容差 3：1598/27900 像素不同，差异集中在边框和字形抗锯齿；标题文案、标签文案与品牌图标属产品范围差异，不能按逐像素相同验收。
- 该状态仍未计为 54 页合格配对；错误、有效行、取消、深色、其他 DPI 等跳转行状态仍未逐一复核。详见[跳转行当前配对](goto-line-current-pair-2026-10-18.md)。

## 界面英文审计

- 正常入口、英文夹具、临时设置，扫描 20 个状态：main、text-viewer、changes、diff、terminal、settings 四页、settings-save-failure、go-to-line、stash-create、stash-manager、worktree-manager、worktree-create-empty、branch-fetch-no-remote、push、remote、remote-name-empty、remote-url-empty。
- 终端标签裁决后当前构建：浅色 PID27264/端口55723、深色 PID12796/端口62816，各 **20** 状态；共 **40** 个状态扫描。新增无远程 `fetch` 失败、Define Remote 空名称/空 URL、New Worktree 空字段校验。可见直接文本、`title`、`aria-label` 的 CJK 发现数均为 **0**；各类校验提示均为英文；两轮负向对照均检出 1 项。排除正文和终端原始输出，用户数据不翻译。
- 两次 `CloseMainWindow` 成功，端口关闭，临时目录删除。
- 工具：`tools/audit/ui-english-audit.cjs`；证据 `artifacts/pixel-followup-2026-10-18/ui-english-audit-16/` 与 `ui-english-audit-dark-7/`。详见[界面英文审计](ui-english-audit-2026-10-18.md)。

## 保留终端：Custom shell 标题

- 实际正常入口选择 Custom shell 后，标签曾显示中文 `自定义终端`；现按用户裁决显示 `Local`，当前 Shell 放悬停 `title`/`aria-label`，未改 Shell 启动和终端输出。
- PID19284、端口60293，实测 `Local` + `title="Custom terminal"`，八行输出，面板 304.2→327 CSS，`CloseMainWindow` 成功，临时目录删除。
- 历史 17 状态轮（加入 fetch 失败后、加入 remote 空名称前）：浅色 PID9796/端口57954、深色 PID6304/端口51266，CJK 0；证据 `ui-english-audit-9/`、`ui-english-audit-dark-4/`。
- 新建分支/标签名称校验：正常入口 PID20524/端口64786，Git Log 提交右键 New Branch… → 空名称 Create 显示 `Name cannot be empty.`，输入 `bad name` 显示 `Name cannot contain whitespace.`；New Tag… 空名称同样显示 `Name cannot be empty.`；均无中文；`CloseMainWindow` 成功，临时目录删除。见[名称校验](branch-create-validation-2026-10-18.md)。
- 冲突列表/只读 Diff：真实 merge 冲突夹具，PID17344/端口62807；列表标题 `Merge Conflicts`、`Abort Merge`、禁用 `Continue Merge`，只读 Diff 提示英文；可见正文/title/aria CJK 0。该实例由脚本按 PID 清理，不作正常关窗证据。
- Rebase 冲突 Skip/Abort：PID5140/端口50564；`Abort Rebase`、`Skip` 英文，CJK 0；Skip 后 rebase 完成、工作区干净、日志包含 `feature-second`，`CloseMainWindow` 成功，临时目录删除。
- Merge 冲突 Continue：PID3172/端口52119；外部解决后 `Continue Merge` 可用，CJK 0；点击 Continue 后对话框关闭，`CloseMainWindow` 成功，临时目录删除。
- Merge 冲突 Abort：PID19348/端口59750；`Abort Merge` 可用，CJK 0；点击 Abort 后对话框关闭，`CloseMainWindow` 成功，临时目录删除。
- `terminal-matched-panel`、`ui-text.test.cjs` 和共享资源检查通过；`live-data.js` MD5=`D8D4998D4FC7C82433873C38E3E8A2FE`。完整终端面板/状态栏/内容仍未同态。详见[终端标签记录](terminal-custom-label-2026-10-18.md)。

## 清理与残留

- 历史参考临时目录 `C:/Users/wl024/AppData/Local/Temp/Augit-remote-insets-a8012f834fd543eba6a3651abc8be86c`：核实 `system/.pid=21240`、PID 21240 已不存在、无其他进程引用后删除；删除前后记录在 `artifacts/pixel-followup-2026-10-18/cleanup-reference-21240/`。
- 本批普通冒烟 PID 24608（端口 49328，临时目录 `Augit-normal-smoke-mWsjQj`）、设置批 PID 24412/20840/27344（端口 50125/57819/59507，临时目录 `Augit-independent-settings-zVDH4t`/`hEuf85`/`P69kGi`）、嵌套模态 PID 4028/26444（端口 55316/63784，临时目录 `Augit-nested-modal-19F9tO`/`mdewaU`）、刷新 PID 4888/10204（端口 60701/56646，临时目录 `Augit-daily-refresh-5lJJmO`/`lJALVA`）均已退出；无 CDP 批 PID 22304/15100/15060/24736/22564/4276/25260/27104/3152/25920/26196/5668/27076/26232 亦已退出（临时目录 `Augit-no-cdp-21Ewdb`/`VHKRsL`/`pL2jTl`/`37q2Ck`/`t0Rty3`/`Mo6ZSk`/`FERptL`/`hErYKh`/`n6DnNw`/`5FLk2d`/`m9Ti90`/`pNbxjA`/`bDizPl`/`KQre8v` 已删除）。对应端口无监听。
- 汇总复核：`cleanup-check-11.json` 记录原挂起实验 PID5312 已被系统复用为 `svchost`（创建于本批结束之后），**未按旧 PID 误杀**；`cleanup-check-12` 追加 16 状态浅/深英文审计 PID6296/3960；`cleanup-check-13` 追加简化无 CDP 夹具周期 PID4784/9668/27020/2700；`cleanup-check-14` 追加成功关闭样本 PID27104；`cleanup-check-15` 追加深色/重复 no-CDP PID3152/25920；`cleanup-check-16` 追加 Save/OK 与标题关闭 PID26196/5668；`cleanup-check-17` 追加 Esc 失败/回退 PID27076；`cleanup-check-18` 追加 Esc 成功 PID26232；`cleanup-check-19` 追加终端标签裁决复验 PID26960/22252/19284 与英文审计 PID26148/21572；`cleanup-check-20` 追加 17 状态英文审计 PID9796/6304；`cleanup-check-21` 追加新建分支空名校验 PID2252；`cleanup-check-22` 追加空/空白名称校验 PID10656；`cleanup-check-23` 追加默认工作区启动冒烟 PID11292；`cleanup-check-24` 追加新建标签空名称校验 PID20524；`cleanup-check-25` 追加 18 状态英文审计 PID20920/18076；`cleanup-check-26` 追加冲突英文扫描 PID17344；`cleanup-check-27` 追加 Rebase Skip/Abort PID5140；`cleanup-check-28` 追加 Merge Continue PID3172；`cleanup-check-29` 追加 Merge Abort PID19348；`cleanup-check-30` 追加 19 状态英文审计 PID4976/11220；`cleanup-check-31` 追加 20 状态英文审计 PID27264/12796，共 68 个本批 PID 全部不存在（原复用为 `svchost` 的 PID25584 也已退出），无 `Augit-capture-stall-*`/`Augit-settings-process-*`/`Augit-terminal-panel-*`/`Augit-ui-english-*`/`Augit-no-cdp-*`/`Augit-start-diag-*`/`Augit-no-cdp-cycle-*` 临时目录、无残留 `Augit` 进程。`cleanup-check-2` 至 `cleanup-check-10` 为分批快照。
- 设置 native trace 与刷新全窗录像 205,096,982 字节保留作为原始证据。
- 未清理用户原有 PyCharm（PID 22768）；未触碰其他历史 `C:\Users\wl024\AppData\Local\Temp\Augit-*` 目录。它们不属于本批启动资源，也不在本次授权范围内。

## 仍未完成

- 2904ms 设置慢样本仍不可重复；500ms 捕获超时后的提前揭示和真实慢样本仍无实机证据。
- 深色白闪修复只在已有两轮高帧率样本中未复现；未覆盖全部 DPI、更长周期和所有开关路径。
- 54 页保留页面与全部模态子状态的双端同态合格配对仍为 0；编辑页只完成正文相对关系与偏移对齐，未整页通过。
- 终端完整内容、面板宽度、标签、状态栏和焦点/光标仍未同态；命令块间距按已批准差异保留。
- 系统阴影顶部/底部弱于参考；用户 2026-10-18 已裁决登记为已知差异、不宣称验收（不是豁免）。深色、非活动态、不同 DPI 和完整四角仍未验收。
- 本次正常入口证据支持“启动、设置基本路径、真实嵌套关闭和刷新首内容帧已有可复验结果”，不构成整体目标完成。

## 复验命令

```powershell
node tools/audit/normal-entry-smoke.cjs <playwright> artifacts/pixel-followup-2026-10-18/normal-entry-smoke
node tools/audit/nested-modal-close.cjs <playwright> artifacts/pixel-followup-2026-10-18/nested-modal-close-3
node --test tools/audit/nested-modal-close-state.test.cjs
node tools/audit/refresh-daily-file.cjs <playwright> artifacts/pixel-followup-2026-10-18/refresh-current --gdi-clock
node tools/audit/refresh-daily-file.cjs <playwright> artifacts/pixel-followup-2026-10-18/refresh-owner-current --gdi-clock --record-owner
python -B -m unittest tools/audit/test_refresh_content_onset.py
python -B tools/audit/measure-refresh-content-onset.py artifacts/pixel-followup-2026-10-18/refresh-owner-current
```

设置批复验使用 `independent-settings.cjs --daily-launcher [--dark]`；脚本自行清理实例和临时目录。
