# Augit 1.0.0-rc.2 发布说明

- 发布日期：2026-10-24
- 状态：**1.0 Release Candidate 候选**，不是正式 1.0
- 源码提交：`8643b9e176f0b739806c44c480a915df9796d9e9`（运行代码与 Beta 相同，另含发布文档/README 更新）
- 文档收口提交：`48b9c9bc79447dc6177e413e07c346f950ca08f0`（RC 构建源仍为 `8643b9e176f0b739806c44c480a915df9796d9e9`；后续仅发布文档/证据索引，不改变 RC 产物）
- 上一发布线：`0.1.0`（历史产物）；本次为 1.0 预发布候选

## 1. 这是什么

Augit 是面向「代码由外部 AI、脚本或人工修改」这一协作方式的轻量 Windows 只读项目查看器与 Git 图形工具。界面使用 C#/.NET 10 原生 Win32 外壳 + WebView2，不接入 AI，不提供内置文本编辑和保存。

1.0.0-rc.2 是 Beta 收口后的候选包，用于真实日常使用观察。它**不是正式 1.0**：正式发布仍需 RC 观察结果、已知限制裁决和用户确认。

## 2. 本候选包含

- 只读浏览：文本、Markdown、JSON、图片（PNG/JPEG/BMP）、二进制/超限信息态、文件树、快速打开、全仓搜索、大文本窗口化预览。
- Git：Changes/Unversioned、只读 Diff（并排/统一、忽略空白）、提交历史与详情、文件历史、Blame、分支/标签/引用、Commit、Fetch/Pull/Push、Define/Edit Remote、Stash、Worktree、Smart Checkout、Reset/Rollback 等确认链路。
- 冲突：冲突状态与文件列表、普通只读 Diff、外部编辑/暂存后的状态同步，以及 Continue/Skip/Abort；不提供三栏冲突编辑器。
- 内置终端：xterm 简洁形态，PowerShell 默认，可配置其他 Shell，关闭运行中会话前确认并结束进程树。
- Settings：简化自定义结构，主题/字体/字号/面板尺寸/终端等设置；不重建 PyCharm 完整设置树。

## 3. 发布收口改动（相对 Beta 前状态）

- 版本统一为 `1.0.0-rc.2`：程序集状态、安装器 `AppVersion`、产物命名一致；安装器数值 `FileVersion` 为 `1.0.0.0`，产品版本保留预发布标识。
- 修复被误截断的 `README.md`，重写安装、便携、运行时、Git/认证、只读边界、已知限制、升级/卸载和反馈说明。
- 分发包新增项目 `LICENSE` 原文，并与 README、第三方说明一起由发布脚本校验。
- 便携包缺少 WebView2 时先显示缺失项、微软 Evergreen 官方地址与“不自动安装运行时”说明；缺少 .NET 时由 .NET 宿主提供 `Download it now`。
- 修复首次打包冒烟发现的启动回归：WebView2 探测现在先加载随包 `native\WebView2Loader.dll`，正常启动不再误报运行时缺失。
- 新增宿主刷新阶段诊断（默认关闭），完成外部文件/Git 状态 500ms 同轮测量与真实前台 OS 鼠标 Continue Rebase 验收。
- 真实 NVDA 2026.2 屏幕阅读器会话发现大文本活动行只播报位置、不读正文；已修复为逐行播报行号与正文，并保留“离屏全文不在可访问树”的已披露边界。rc.2 上又完成纯 OS 键盘 NVDA：小文件 5 行全文、2,001 行到 1504/1523/2000、80,001 行到 61/80000，未发现新语义缺陷。
- 完成 `0.1.0 → 1.0.0-rc.2` 直接覆盖升级、启动与卸载验收；Beta → rc.1 升级此前已通过。
- 软换行 80,000 行仍退回全量 DOM（真实外壳首测 5.23s、页面级 3.62–9.78s、本轮重跑 4.371s、80,000 节点）；本阶段不实现可变行高虚拟化，列为正式 1.0 已知限制与 1.0 后工程目标。

## 4. 产物

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.2-win-x64-portable.zip` | 3,522,601 B | `e339da490611d7c0ea2e4b87bddd33f9cb840eb2c76333be9fc5ab4a207bb5fc` |
| `Augit-1.0.0-rc.2-win-x64-setup.exe` | 5,010,098 B | `ecb0013425ef096763f08331f29f67b653673512dd5304152fd098f31272e65c` |

Beta 对照产物（已通过安装升级验收）：

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-beta.1-win-x64-portable.zip` | 3,522,193 B | `0d11e998136b9b2fd241276fe31073424faface5bd30db4b4821892e47b96c05` |
| `Augit-1.0.0-beta.1-win-x64-setup.exe` | 5,010,079 B | `f937bf99cfcd112220c5ba42736b36a80b414b2cd5cafe7520a66f13cf2294a6` |

产物绝对路径：

- `D:\github\Augit\artifacts\release-1.0.0-rc.2\final\`
- `D:\github\Augit\artifacts\release-1.0.0-beta.1\final2\`

`SHA256SUMS.txt` 在每个目录内，可复核。产物是本机构建，未上传到任何公开 Release。

Web 资源清单（`web/` 下 27 个文件，按“文件 SHA256 + 相对路径”排序后计算的清单 SHA256）：
`76d8f3d6c8f2016fe4a026ef5b59ad6b4b882709c2c91f167468fba3b5e5532f`；
原始清单 `artifacts/release-1.0.0-rc.2/final/web-resource-manifest.txt`。

## 5. 系统要求

- Windows 10 22H2 x64 或 Windows 11 x64（本候选只在 Windows 11 x64 实机验证）。
- .NET 10 Runtime（`Microsoft.NETCore.App`）；缺少时安装器先说明，经确认后下载锁定的微软运行时并校验哈希与 Microsoft 签名。
- Microsoft Edge WebView2 Runtime（Evergreen）。
- Git for Windows 2.40 或更高版本（仅 Git 功能需要）；Augit 不捆绑 Git。
- 认证交给 Git Credential Manager、SSH agent 或终端；Augit 不保存凭据。

## 6. 安装、便携、升级与卸载

- 安装版需要管理员权限，默认安装到 `C:\Program Files\Augit`；可选加入系统 PATH 和资源管理器“使用 Augit 打开”。
- 便携版解压后直接运行 `Augit.exe`，不写 PATH/右键菜单，不自动安装运行时；缺少运行时时给出官方下载入口。
- 覆盖升级沿用原安装目录、PATH 与右键菜单选择；保留 `%LOCALAPPDATA%\Augit` 的设置、最近目录和会话状态。
- 卸载删除程序文件、安装器自己加入的 PATH 项和右键菜单，不删除用户设置、工作区或共享运行时。

本候选已实测：`0.1.0 → 1.0.0-rc.2` 覆盖升级 → 启动 631ms → 静默卸载；退出码均 0，PATH 逐字恢复、右键菜单/开始菜单清理、设置哈希不变、.NET/WebView2 运行时未变化。Beta 安装 → rc.1 覆盖升级也已通过。未在 Windows 10 或干净虚拟机安装。

## 7. 验证摘要

- Release 构建 0 警告 0 错误。
- Core 72/72、Infrastructure 206/206、Shell 253/253（新增 Settings 500ms 上限测试）。
- 全量 live-shell 2379/2379，退出码 0，未执行 0（空闲复跑；此前构建/安装并发负载下 §7.8 触底分页断言失败一次，失败日志保留）。
- 便携包 Beta/RC 烟测 PASS；安装/升级/卸载验收 PASS。
- 外部文件修改同轮 20/20 屏幕首内容 236–293ms；本地 checkout 分支可见 117–152ms。
- 真实前台 OS 鼠标 Continue Rebase PASS；80,000 行软换行操作通过，构建耗时 3.62–9.78s（已披露）。

## 8. 已知限制

完整清单见[已知限制](known-limitations-1.0.0-rc.2.md)，重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- **Windows 10 22H2 与跨屏 DPI 未实机验证**。
- 整窗像素历史结果仍为 **0 张合格 / 50 组页面级配对**，未达成原整改目标；只登记持续改进。
- 500ms 历史 611/552/624ms 慢样本本批未复现、未归因，保留风险。
- 80,000 行软换行退回 80,000 行 DOM，真实外壳首测 5.23s、页面级 3.62–9.78s；大文件建议关闭软换行。
- 离屏全文不在可访问树，真实屏幕阅读器连续朗读未验证。
- 安装器在真实缺少运行时的机器上的确认/下载/取消/重试未实测。
- Settings 冷开 1 次 567.2ms，未连续达成 500ms；2904ms 历史样本未复现；500ms/正常/异常路径受控单测 3/3。
- 搜索超时仅受控慢条件验证；Push 认证覆盖按专项记录限定；Diff 有 10MB/20MB 与 2000 行实现上限。
- 不提供 Force Push、子模块、平台接口、Git Console 或自动更新。

## 9. 证据与复验

- [Beta 验收报告](beta-acceptance-2026-10-24.md)
- [发布收口台账](release-closure-2026-10-24.md)
- [证据索引](evidence-index-1.0.0-rc.2.md)
- [宿主刷新阶段](refresh-host-stage-2026-10-24.md)
- [真实鼠标 Continue](conflict-rebase-real-mouse-continue-2026-10-24.md)
- [大文本软换行边界](large-text-wrap-boundary-2026-10-24.md)
- [缺少运行时提示](runtime-missing-messaging-2026-10-24.md)

原始截图、报告和日志位于本机 `D:\github\Augit\artifacts\`（Git 忽略），不在公开仓库或 Release 中。

## 10. 下一步

1. 在真实日常仓库中观察本 RC：浏览、外部修改同步、提交与分支、拉取推送、冲突、终端、重启恢复。
2. 记录实际运行环境、问题、结果与回归依据；不在用户真实仓库制造破坏性测试。
3. 处理或裁定已知限制中的 500ms 慢样本、软换行构建、可访问性连续阅读、安装器缺运行时交互、Windows 10/跨屏验证。
4. 完成观察后再讨论正式 1.0；本候选不自动创建公开 Release、不上传产物。

## 11. 反馈

通过项目交付渠道或项目仓库 Issue 反馈，附版本（安装器标题或 `Augit.exe` 文件属性）、复现步骤、期望与实际结果。Augit 不做遥测/崩溃上报，不会自动上传代码、日志或使用数据。
