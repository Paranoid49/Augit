# Augit 正式 1.0 发布建议（2026-10-25）

> **2026-10-26 状态更新**：Windows 10 22H2 与双显示器/混合 DPI 已在实机完成验证（含 100/125/150/175/200%、负坐标、模拟热插拔、深色窄窗和 rc.2 重建包安装），见[22H2 多显示器 DPI 修复与扩展验证](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)。用户已决定把 `1.0.0-rc.2` 作为**公开预发布**交付，真实用户可以下载安装包或便携包试用。本文 §4 的 Windows 10/跨屏条目据此关闭；§5 建议 A 中“不创建公开 Release、不上传产物”已被该决定取代。正式 1.0 的其余外部条件仍然有效。
>
> **2026-10-26 rc.4 补充**：rc.2 便携版在没有 .NET 10 的新电脑上先弹出 .NET 宿主错误；rc.3 把便携版改为 self-contained，安装版保持 framework-dependent 联网安装器。rc.4 在 rc.3 基础上增加 Windows 10 22H2 首次启动/选目录退出的加固（CETCompat=false、欢迎页、async void 边界、`git init` 超时和本地日志）；启动路径仍待在用户实机复验。当前公开资产见[rc.4 发布清单](release-manifest-1.0.0-rc.4.md)。
>
> **结论：`1.0.0-rc.2` 可以作为最终 RC 候选交付；当前不建议直接宣布正式 1.0。**
> 推荐进入 RC 真实日常使用观察，并把 Windows 10、跨屏 DPI、干净虚拟机、安装器真实缺运行时、
> 代码签名作为正式 1.0 前的外部/发布条件。当前环境能独立完成的收口项已闭环；外部环境缺口
> 不再阻塞这些已完成工作，但也不能被写成通过。

## 1. 候选身份

| 项 | 值 |
| --- | --- |
| 版本 | `1.0.0-rc.2` |
| RC 构建源提交 | `8643b9e176f0b739806c44c480a915df9796d9e9` |
| 程序集 ProductVersion | `1.0.0-rc.2+8643b9e…` |
| 便携包 | `Augit-1.0.0-rc.2-win-x64-portable.zip`，3,522,601 B，SHA256 `e339da490611d7c0ea2e4b87bddd33f9cb840eb2c76333be9fc5ab4a207bb5fc` |
| 安装器 | `Augit-1.0.0-rc.2-win-x64-setup.exe`，5,010,098 B，SHA256 `ecb0013425ef096763f08331f29f67b653673512dd5304152fd098f31272e65c` |
| 产物目录 | `artifacts/release-1.0.0-rc.2/final/` |
| web 资源清单 | 27 个文件，清单 SHA256 `76d8f3d6c8f2016fe4a026ef5b59ad6b4b882709c2c91f167468fba3b5e5532f` |

rc.2 相对 rc.1 的运行时变化只有大文本活动行的可访问名称修复（`web/src/large-text-view.js`）
和相应测试；安装器/升级逻辑未变，但仍用最终 rc.2 字节重新执行了安装升级验收。

## 2. 当前环境已解决并验证

| 项 | 结果 | 证据 |
| --- | --- | --- |
| `0.1.0 → rc.2` 直接覆盖升级 | **PASS**：安装/升级/启动 631ms/卸载退出码均 0；PATH 逐字恢复、设置保留、共享运行时不变 | `artifacts/legacy-upgrade-acceptance-2026-10-25-rc2d/legacy-upgrade-report.json` |
| 真实屏幕阅读器 | 真实 NVDA 2026.2 读出小文件全文、2,001 行窗口与末行正文；修复活动行只报位置不读正文的语义缺陷 | `docs/nvda-screen-reader-session-2026-10-25.md` |
| 纯 OS 键盘 NVDA（rc.2） | 小文件 5 行全文；2,001 行到第 1504/1523/2000 行，NVDA 报行号+正文+“2001 之 N 项”；80,001 行抽样到第 61/80000 行；未发现新语义缺陷 | `docs/nvda-oskeyboard-session-2026-10-25.md`、`artifacts/accessibility-oskeyboard-2026-10-25/` |
| Settings 受控测试 | 500ms 上限直接单测 + 正常/异常路径 **3/3 通过**；关闭路径沿用 3 次 29.1–89.7ms | `tests/Augit.Shell.Tests/ShellSettingsCaptureTimeoutTests.cs` |
| 活动行可访问名称回归 | `live-large-text` 30/30；活动行/全部渲染行均带行号+正文名称 | `artifacts/large-text-nvda-fix-2026-10-25/run.log` |
| 外部文件 500ms | 2026-10-24/25 合计 **20/20** 同轮屏幕首内容 236–293ms；历史 611/552/624ms 未复现 | `docs/refresh-host-stage-2026-10-24.md`、四批 `refresh-host-trace-*` |
| Settings 阶段分析 | 3 次冷开 491/451/567ms；capture 88–159ms；2904ms 历史样本慢在 capture/present、未复现 | `docs/settings-cold-open-stage-2026-10-25.md` |
| 最终包启动/退出 | 便携关闭 94ms、退出 0、无残留；安装版启动 631ms、退出 0 | `release-1.0.0-rc.2/final-verify/portable-report.json`、legacy 报告 |
| 构建与回归 | Release 0 警告 0 错误；Core 72/72、Infrastructure 206/206、Shell 253/253；全量 live-shell 2379/2379；32 个 PowerShell 编码、UI 资产、doc claims、coverage、JS、diff check 通过 | `artifacts/release-1.0.0-rc.2/full-validation-2026-10-25b.log`、`live-shell-full2.log` |

说明：全量 live-shell 首次在构建/安装并发负载下于 §7.8 触底分页失败一次；空闲复跑 2379/2379
通过，失败日志保留，不把负载下失败写成稳定通过。

## 3. 已披露限制（不阻塞候选交付，但不写成通过）

1. **软换行 80,000 行退回全量 DOM**：页面级 3.62–9.78s（本轮重跑 4.371s）、真实外壳首测 5.23s，
   80,000 个行节点；之后滚动/Find/Go to Line/全选/关闭可用。可变行高虚拟化列为 1.0 后工程目标；
   不声称所有大文本模式都已窗口化。
2. **屏幕阅读器离屏全文不可连续朗读**：可访问树只暴露当前窗口约 25–30 行（总数 2,001/80,001）；
   可逐页导航、读当前行、Find/Go to Line/复制全文，但不能像普通文档一样一次连续朗读全文。
   纯 OS 键盘 NVDA 已覆盖小文件、2,001 行和 80,001 行抽样，未发现新的焦点/行号/正文缺陷。
3. **Settings 冷开未连续 500ms**：3 次 491/451/567ms；567ms 是性能余项，不升级为发布阻塞。
   500ms 超时/异常路径已由受控单测覆盖（3/3），关闭路径沿用 29.1–89.7ms 证据。
4. **历史 2904ms Settings 慢样本未复现**：慢点指向 CapturePreview/呈现等待；500ms 超时/异常路径
   已由 3 个受控单测覆盖，仍无自然超时样本。
5. **500ms 历史慢样本未归因**：20/20 未复现，保留未归因风险。
6. **整窗像素**：历史结果仍为 0 张合格 / 50 组页面级配对，未达成原整改目标；只登记持续改进。
7. **代码签名**：Augit 自身 `NotSigned`，SmartScreen 可能提示未知发布者。
8. **未支持/已批准范围差异**：Force Push、子模块、平台接口、内置 Git Console、AI、内置编辑保存等，
   以 `docs/known-limitations-1.0.0-rc.4.md` 为准。

## 4. 外部环境要求（当前机器不可验证）

- ~~Windows 10 22H2 x64 实机。~~ **2026-10-26 已完成**（`10.0.19045.6456` 双屏实机，见 22H2 验证记录）。
- ~~跨屏/多显示器混合 DPI。~~ **2026-10-26 已完成**（真实鼠标跨屏、100–200%、负坐标、模拟热插拔）。
- 干净虚拟机或第二台机器安装。
- 安装器在真实缺少 .NET/WebView2 的机器上的确认、联网下载、取消、失败与重试。**其中缺少 .NET 的确认与下载安装已在 22H2 实机通过；缺少 WebView2 仍未验证。**
- 代码签名证书与发布授权。
- 真实日常使用观察（仓库浏览、外部修改同步、提交/分支、拉取推送、冲突、终端、重启恢复）。
- 可选：Narrator/JAWS/Braille。纯 OS 键盘 NVDA 与 80,001 行 NVDA 抽样已完成；
  Narrator 只确认可执行文件存在，未做完整会话；JAWS/Braille 依赖许可/硬件。

这些项目必须继续标为“未验证”，不能因 rc.2 通过安装或便携烟测而改写成已通过。

## 5. 发布建议

### 建议 A（推荐）：交付 rc.2 最终候选，进入 RC 观察

- 现在可以交付 `1.0.0-rc.2` 作为**最终 RC 候选**，供真实日常使用与外部环境验证。
- 2026-10-26 已按用户决定创建公开预发布 Release（Pre-release），提供安装器、便携包和校验和；这不等于宣布正式 1.0。
- 观察期重点：真实仓库高频操作、外部修改同步、冲突 Continue、长会话/重启、超大文本软换行、
  低配置机器上的 Settings/冷开、屏幕阅读器实际使用反馈。
- 观察期结束后，由用户根据外部验证与披露边界裁决是否发布正式 1.0。

### 建议 B：满足条件后发布正式 1.0

至少需要：
1. 完成一轮真实日常使用观察，无新的 P0/P1；
2. Windows 10/跨屏/干净安装中至少补齐发布目标所要求的平台；
3. 对安装器真实缺运行时流程给出结论（验证或明确接受未验证）；
4. 对代码签名作出决定（签名或明确接受 SmartScreen 提示）；
5. 用户明确接受软换行、屏幕阅读器离屏全文、Settings 余项、像素历史结果等披露边界。

### 建议 C：若坚持当前立即发布 1.0

不是技术上的“全部通过”，而是**用户明确接受 §3 全部披露项与 §4 未验证项**后的产品决策。
Agent 不能自行豁免现行 500ms、无障碍或平台验证要求；本建议不把 rc.2 写成正式 1.0 已验收。

## 6. 结论

- 当前环境可解决的收口项已完成，rc.2 是当前最强、可复验的候选。
- 2026-10-26 已发布公开预发布资产；正式 1.0 仍受干净虚拟机、真实缺 WebView2、代码签名、其他辅助技术与真实使用观察约束。
- 建议**继续 RC 观察**，而不是现在宣布正式 1.0。
- 若用户接受披露边界并完成/豁免外部条件，rc.4 可作为正式 1.0 的发布基础。

## 7. 证据入口

- 收口台账：`docs/release-closure-2026-10-24.md`
- rc.4 发布清单：`docs/release-manifest-1.0.0-rc.4.md`
- rc.4 发布说明：`docs/release-notes-1.0.0-rc.4.md`
- 已知限制：`docs/known-limitations-1.0.0-rc.4.md`
- rc.2 历史发布记录：`docs/release-manifest-1.0.0-rc.2.md`、`docs/release-notes-1.0.0-rc.2.md`
- 证据索引：`docs/evidence-index-1.0.0-rc.2.md`
- Windows 10 22H2 多显示器/DPI：`docs/win10-22h2-multimonitor-validation-2026-10-26.md`、`docs/win10-22h2-multimonitor-dpi-fixes-2026-10-26.md`
- NVDA 会话：`docs/nvda-screen-reader-session-2026-10-25.md`
- 纯 OS 键盘 NVDA：`docs/nvda-oskeyboard-session-2026-10-25.md`
- 限制逐项判定：`docs/limits-triage-2026-10-25.md`
- Settings 阶段：`docs/settings-cold-open-stage-2026-10-25.md`
- Beta 验收：`docs/beta-acceptance-2026-10-24.md`
