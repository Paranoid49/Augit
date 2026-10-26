# Augit 1.0.0-rc.2 证据索引（2026-10-25）

本索引把 RC 交付所需的产物、命令、报告和证据位置集中列出。原始大文件在 `D:\github\Augit\artifacts\`（Git 忽略），本文不宣称它们已上传或公开。

## 1. 候选身份

| 项 | RC | Beta |
| --- | --- | --- |
| 版本 | `1.0.0-rc.2` | `1.0.0-beta.1` |
| 源码提交 | `8643b9e176f0b739806c44c480a915df9796d9e9` | `51dcadd356cd2fc5b8910dc60690d50416245491` |
| 文档收口提交 | `48b9c9bc79447dc6177e413e07c346f950ca08f0`（RC 构建源仍为 `8643b9e176f0b739806c44c480a915df9796d9e9`；后续发布文档提交不改变产物） | 同上 |
| 程序集 ProductVersion | `1.0.0-rc.2+8643b9e…` | `1.0.0-beta.1+51dcadd…` |
| 产物目录 | `artifacts/release-1.0.0-rc.2/final/` | `artifacts/release-1.0.0-beta.1/final2/` |
| 便携包 SHA256 | `e339da490611d7c0ea2e4b87bddd33f9cb840eb2c76333be9fc5ab4a207bb5fc` | `0d11e998136b9b2fd241276fe31073424faface5bd30db4b4821892e47b96c05` |
| 安装器 SHA256 | `ecb0013425ef096763f08331f29f67b653673512dd5304152fd098f31272e65c` | `f937bf99cfcd112220c5ba42736b36a80b414b2cd5cafe7520a66f13cf2294a6` |
| 便携包 SHA256SUMS | `artifacts/release-1.0.0-rc.2/final/SHA256SUMS.txt` | `artifacts/release-1.0.0-beta.1/final2/SHA256SUMS.txt` |
| web 资源清单 SHA256 | `76d8f3d6c8f2016fe4a026ef5b59ad6b4b882709c2c91f167468fba3b5e5532f`（27 个文件；清单 `artifacts/release-1.0.0-rc.2/final/web-resource-manifest.txt`） | 不同：Beta 使用旧 `large-text-view.js`（无行名称修复），web 清单与 RC 有 1 个文件差异 |

## 2. 构建与全量检查

| 项 | 结果 | 位置 |
| --- | --- | --- |
| Release 构建 | 0 警告 / 0 错误 | `artifacts/release-1.0.0-rc.2/full-validation-2026-10-25b.log` |
| Core 测试 | 72/72 | 同上 |
| Infrastructure 测试 | 206/206 | 同上 |
| Shell 测试 | 253/253 | 同上 |
| 全量 live-shell | **2379/2379，退出码 0，未执行 0**（空闲复跑；此前并发负载下 §7.8 失败一次，记录保留） | `artifacts/release-1.0.0-rc.2/live-shell-full2.log`；失败日志 `live-shell-full.log` |
| UI 资产字节一致 | PASS | 同上（全量检查日志） |
| PowerShell 编码规则 | 32 个脚本 PASS | 同上 |
| 文档声明机械核对 | `DOC_CLAIMS_OK` | 同上 |
| coverage 生成器自测 | PASS | 同上 |
| JS 语法（4 个运行时文件） | PASS | 同上 |
| `git diff --check` | PASS | 同上 |
| 最终 Beta 便携包烟测 | PASS（关闭 130ms、退出 0、无残留） | `artifacts/release-1.0.0-beta.1/final2-verify/portable-report.json` |
| 最终 RC.2 便携包烟测 | PASS（关闭 94ms、退出 0、无残留） | `artifacts/release-1.0.0-rc.2/final-verify/portable-report.json` |

## 3. 安装、升级、卸载

| 项 | 结果 | 位置 |
| --- | --- | --- |
| `0.1.0` 安装 | 退出 0；版本 `0.1.0+6ae4ced…`；端口/菜单/PATH 检查通过；README/第三方说明存在、无 LICENSE | `artifacts/legacy-upgrade-acceptance-2026-10-25-rc2d/legacy-upgrade-report.json` |
| RC.2 覆盖升级 | 退出 0；版本 `1.0.0-rc.2+8643b9e…`；卸载记录 1 个；PATH 值与安装前完全相同；documents 齐全 | 同上 |
| RC.2 启动 | 窗口出现 631ms、退出 0 | 同上 `launch` |
| 卸载 | 退出 0；安装目录/卸载记录/右键菜单/开始菜单移除；PATH 逐字恢复；共享运行时未变；设置保留 | 同上 `after` |
| 安装/升级日志 | Inno Setup | 同目录 `legacy-install.log`、`rc-upgrade.log` |
| 签名状态 | 0.1.0/RC setup 均 `NotSigned` | 同上报告 `before.*Signature` |
| Beta → rc.1 历史 | 通过（旧候选，保留作沿革） | `artifacts/install-acceptance-2026-10-24-final2/installed-report.json` |

## 4. 500ms 同轮宿主阶段

| 项 | 结果 | 位置 |
| --- | --- | --- |
| 外部文件 gdigrab 5 轮 | 屏幕首内容 275–293ms；网页应用 198–208ms | `artifacts/refresh-host-trace-2026-10-24b/report.json` |
| 外部文件 ddagrab 5 轮 | 屏幕首内容 236–289ms；网页应用 161–205ms | `artifacts/refresh-host-trace-2026-10-24c/report.json` |
| 追加 gdigrab 5 轮（2026-10-25） | 屏幕首内容 254–271ms；网页应用 164–179ms | `artifacts/refresh-host-trace-2026-10-25a/report.json` |
| 追加 ddagrab 5 轮（2026-10-25） | 屏幕首内容 249–275ms；网页应用 167–177ms | `artifacts/refresh-host-trace-2026-10-25b/report.json` |
| 合计 | **20/20 同轮屏幕首内容 <300ms**；历史 611/552/624ms 未复现 | 上述四批 |
| 本地 checkout 3 轮 | 分支可见 117–152ms；完整应用 201–232ms | `artifacts/git-status-sync-2026-10-24b|c|d/cleanup.json` |
| 记录文档 | 阶段定义、校准、历史慢样本边界 | `docs/refresh-host-stage-2026-10-24.md` |

## 5. 真实鼠标与冲突

| 项 | 结果 | 位置 |
| --- | --- | --- |
| 两文件 rebase 冲突 | 外部删除 a.txt、编辑并暂存 b.txt；Continue 启用 | `artifacts/conflict-rebase-multifile-continue-os-2026-10-24j/report.json` |
| 真实前台 OS 鼠标 | 绝对坐标命中 `BUTTON.primary-button`；pointerdown=1/click=1 | 同上 `os-mouse-continue` |
| Git 结果 | status 空、分支 `feature`、HEAD `test: feature-second` | 同上 `external-resolve-state` |
| 关闭与清理 | CloseMainWindow、端口关闭、临时目录删除 | 同上 `cleanup.json` |
| 记录文档 | 坐标校准与失败样本区分 | `docs/conflict-rebase-real-mouse-continue-2026-10-24.md` |

## 6. 大文本、软换行与可访问性

| 项 | 结果 | 位置 |
| --- | --- | --- |
| 80,000 行软换行（页面级） | 构建 3.62s、80,000 节点；Find/Go to Line/全选/关闭/恢复通过 | `artifacts/large-text-wrap-2026-10-24/run.log` |
| 80,000 行软换行（2026-10-25 重跑） | 构建 **4,371ms**、80,000 节点；Find/GoTo/全选/关闭/恢复 30/30 | `artifacts/accessibility-oskeyboard-2026-10-25/large-text-wrap-review.log` |
| 80,000 行软换行（真实外壳） | 构建 5.23s、末行 80000、恢复窗口层；0 失败 | `artifacts/large-text-wrap-normal-entry-2026-10-24/large-text-normal-entry.json` |
| 80,000 行窗口化/选择/Find/Go to Line | 通过 | `docs/large-text-virtualization-2026-10-23.md`、`docs/large-text-2000-2026-10-24.md` |
| 可访问树/UIA | 138 后代/168ms；离屏全文不可读 | `docs/large-text-accessibility-2026-10-24.md`、`docs/large-text-a11y-list-2026-10-24.md` |
| 真实 NVDA 2026.2 会话 | 小文件全文读出；2,001 行初始读出“total 2,001/26 项”；活动行只报位置→修复后播报行号+正文；末行 2000 正文；可访问树仅 25 行 | `artifacts/nvda-session-2026-10-25/nvda-debug.log`、`nvda-evidence-final.txt`、`nvda-big2k-line2000-final.png` |
| 纯 OS 键盘 NVDA（rc.2） | 小文件 5 行全文；2,001 行到 1504/1523/2000；80,001 行到 61/80000；真实 OS 键盘，无 CDP 输入 | `docs/nvda-oskeyboard-session-2026-10-25.md`、`artifacts/accessibility-oskeyboard-2026-10-25/nvda-osk-debug.log` |
| 可访问性记录 | 缺陷、修复、边界与正式 1.0 建议 | `docs/nvda-screen-reader-session-2026-10-25.md` |
| 限制逐项判定 | §3/§4 分类、证据与外部条件 | `docs/limits-triage-2026-10-25.md` |
| 记录文档 | 软换行边界与影响 | `docs/large-text-wrap-boundary-2026-10-24.md` |

## 7. Settings 阶段

| 项 | 结果 | 位置 |
| --- | --- | --- |
| 冷开 3 次（2026-10-24c） | 491.4 / 451.1 / 567.2ms；原生 created→presented 411/371/469ms；CapturePreview 159/89.9/88.5ms | `artifacts/settings-cold-open-2026-10-24c/settings-cold-open.json` |
| 2904ms 历史样本 | prepare 410ms、capture 完成 2889ms、present 请求 2478.8ms；未复现 | `docs/settings-controller-issued-2026-10-17.md` |
| 结论文档 | 567ms 不自动升级为发布阻塞；capture 超时仅单测 | `docs/settings-cold-open-stage-2026-10-25.md` |
| 受控超时测试 | 500ms 上限 + 正常/异常路径 **3/3 通过**（558ms） | 、`tests/Augit.Shell.Tests/ShellSettingsCaptureTimeoutTests.cs` |
| 2026-10-25 复跑 | 固定坐标错过设置按钮，opened=false，记为采集输入问题；脚本已修正 | `artifacts/settings-cold-open-2026-10-25c/settings-cold-open.json` |

## 8. 运行时缺失与校验

| 项 | 结果 | 位置 |
| --- | --- | --- |
| 缺少 .NET | 宿主对话框 + `Download it now`；退出 0x80008083 | `artifacts/runtime-missing-2026-10-24/dotnet-missing-dialog.png` |
| 缺少 WebView2 | Augit 消息框含缺失项、官方地址、便携说明；退出 1 | `artifacts/runtime-missing-2026-10-24/webview2-missing-fallback-dialog.png` |
| VerifyRuntime.ps1 | 成功=0；错误哈希=10；错误大小=9；未签名=11；第三方签名=12 | `artifacts/runtime-missing-2026-10-24/verify-runtime-results.txt` |
| 首次打包启动回归 | 探测早于加载器加载；已修复并从 `51dcadd` 重建 | `docs/runtime-missing-messaging-2026-10-24.md` §2.1 |

## 9. 既有专项证据（本轮未重跑）

以下证据仍是有效历史记录，但不能替代本轮 RC 的整窗像素结论：

- 搜索超时重试：`docs/search-timeout-retry-2026-10-24.md`
- Diff 局部加载：`docs/diff-loading-normal-entry-2026-10-24.md`
- Push 非快进/认证失败/认证成功：`docs/push-non-fast-forward-2026-10-24.md`、`push-auth-failure-2026-10-24.md`、`push-auth-success-2026-10-24.md`
- Define Remote 自动 Fetch 失败/回滚：`docs/define-remote-autofetch-2026-10-24.md`、`define-remote-reference-2026-10-24.md`
- 大文本选择/拖动/键盘：`docs/large-text-select-all-2026-10-24.md`、`large-text-drag-scroll-2026-10-24.md`、`large-text-a11y-list-2026-10-24.md`
- 终端关闭/选择/ANSI/背景：`docs/terminal-close-running-2026-10-24.md`、`terminal-selection-copy-2026-10-24.md`、`terminal-ansi-colors-2026-10-24.md`、`terminal-background-2026-10-24.md`
- 主窗启动/关闭帧：`docs/main-window-startup-close-frames-2026-10-24.md`
- 冲突外部 abort/混合解决/逐步刷新：`docs/conflict-rebase-external-abort-2026-10-24.md`、`conflict-rebase-multifile-resolve-2026-10-24.md`、`conflict-rebase-multifile-continue-2026-10-24.md`

## 10. 范围裁决与限制

- 发布阶段裁决：`docs/acceptance-scope-rulings-2026-10-20.md` §9
- 发布收口台账：`docs/release-closure-2026-10-24.md`
- 正式 1.0 发布建议：`docs/release-recommendation-1.0-2026-10-25.md`
- 公开发布清单：`docs/release-manifest-1.0.0-rc.2.md`
- 发布流程：`docs/releasing.md`
- §3/§4 限制逐项判定：`docs/limits-triage-2026-10-25.md`
- Beta 验收：`docs/beta-acceptance-2026-10-24.md`
- 已知限制：`docs/known-limitations-1.0.0-rc.2.md`
- 原像素协议：`docs/pixel-pair-protocol-2026-10-22.md`（历史结果 0 张，不因 RC 改写）

## 11. 未验证环境

> 2026-10-26 更新：Windows 10 22H2 x64、双显示器/混合 DPI 已完成实机验证，见[22H2 验证](win10-22h2-multimonitor-validation-2026-10-26.md)与[DPI 修复复验](win10-22h2-multimonitor-dpi-fixes-2026-10-26.md)。以下保留当前仍未验证的项。

- 干净虚拟机/全新系统的安装。
- 真实缺少 WebView2 的安装流程（缺少 .NET 的流程已在 22H2 实机通过）。
- 三块及以上显示器、物理拔插显示器、其他 DPI 组合与显示器排列。
- Narrator 完整会话、JAWS/Braille、其他 DPI/深色/窄窗屏幕阅读器；纯 OS 键盘 NVDA 与 80,001 行抽样已完成。
- 足够时长的真实日常使用观察。

这些项目在发布说明和已知限制中如实登记，不写成通过。
