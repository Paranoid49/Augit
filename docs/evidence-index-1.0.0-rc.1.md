# Augit 1.0.0-rc.1 证据索引（2026-10-24）

本索引把 RC 交付所需的产物、命令、报告和证据位置集中列出。原始大文件在 `D:\github\Augit\artifacts\`（Git 忽略），本文不宣称它们已上传或公开。

## 1. 候选身份

| 项 | RC | Beta |
| --- | --- | --- |
| 版本 | `1.0.0-rc.1` | `1.0.0-beta.1` |
| 源码提交 | `909ecb62c3006ab066940590a5f19bb07aa88f8b` | `51dcadd356cd2fc5b8910dc60690d50416245491` |
| 程序集 ProductVersion | `1.0.0-rc.1+909ecb6…` | `1.0.0-beta.1+51dcadd…` |
| 产物目录 | `artifacts/release-1.0.0-rc.1/final2/` | `artifacts/release-1.0.0-beta.1/final2/` |
| 便携包 SHA256 | `d5bed9ad686a9f1b9232f8b4a1a2512e9912d62a7751b72d18d0a19d7871803a` | `0d11e998136b9b2fd241276fe31073424faface5bd30db4b4821892e47b96c05` |
| 安装器 SHA256 | `bc9913c06fae8393e6b7b51eb0678ed15c6b584b04481773e4e7e2a26826cf3b` | `f937bf99cfcd112220c5ba42736b36a80b414b2cd5cafe7520a66f13cf2294a6` |
| 便携包 SHA256SUMS | `artifacts/release-1.0.0-rc.1/final2/SHA256SUMS.txt` | `artifacts/release-1.0.0-beta.1/final2/SHA256SUMS.txt` |
| web 资源清单 SHA256 | `141ba8d7d467becbe6c25053679ec98a8e7f6b3ded945787f16373d9e8500446`（27 个文件；清单 `artifacts/release-1.0.0-rc.1/final2/web-resource-manifest.txt`） | 同一 web 资源内容（便携烟测双向哈希 0 差异） |

## 2. 构建与全量检查

| 项 | 结果 | 位置 |
| --- | --- | --- |
| Release 构建 | 0 警告 / 0 错误 | `artifacts/release-1.0.0-beta.1/full-validation.log` |
| Core 测试 | 72/72 | 同上 |
| Infrastructure 测试 | 206/206 | 同上 |
| Shell 测试 | 252/252 | 同上 |
| 全量 live-shell | **2379/2379，退出码 0，未执行 0** | `artifacts/release-1.0.0-beta.1/live-shell-full.log` |
| UI 资产字节一致 | PASS | 同上 |
| PowerShell 编码规则 | 31 个脚本 PASS | 同上 |
| 文档声明机械核对 | `DOC_CLAIMS_OK` | `artifacts/release-1.0.0-beta.1/doc-claims.log` |
| coverage 生成器自测 | PASS | 同上 |
| JS 语法（4 个运行时文件） | PASS | 同上 |
| `git diff --check` | PASS | 同上 |
| 最终 Beta 便携包烟测 | PASS | `artifacts/release-1.0.0-beta.1/final2-verify/portable-report.json` |
| 最终 RC 便携包烟测 | PASS（关闭 109ms、退出 0、无残留） | `artifacts/release-1.0.0-rc.1/final2-verify/portable-report.json` |

## 3. 安装、升级、卸载

| 项 | 结果 | 位置 |
| --- | --- | --- |
| Beta 安装 | 退出 0；文档/快捷方式/右键菜单/PATH 齐全；启动窗口 906.6ms、退出 0 | `artifacts/install-acceptance-2026-10-24-final2/installed-report.json` |
| RC 覆盖升级 | 退出 0；版本 `1.0.0-rc.1`；PATH 值不变且仅 1 项；设置哈希不变 | 同上 |
| 卸载 | 退出 0；安装目录/卸载记录/右键菜单/开始菜单移除；PATH 逐字恢复；共享运行时未变 | 同上 |
| 安装/升级日志 | Inno Setup | 同目录 `beta-install.log`、`rc-upgrade.log` |
| 签名状态 | Beta/RC setup 均 `NotSigned` | 同上报告 `before.betaSetupSignature/rcSetupSignature` |

## 4. 500ms 同轮宿主阶段

| 项 | 结果 | 位置 |
| --- | --- | --- |
| 外部文件 gdigrab 5 轮 | 屏幕首内容 275–293ms；网页应用 198–208ms | `artifacts/refresh-host-trace-2026-10-24b/report.json` |
| 外部文件 ddagrab 5 轮 | 屏幕首内容 236–289ms；网页应用 161–205ms | `artifacts/refresh-host-trace-2026-10-24c/report.json` |
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
| 80,000 行软换行（真实外壳） | 构建 5.23s、末行 80000、恢复窗口层；0 失败 | `artifacts/large-text-wrap-normal-entry-2026-10-24/large-text-normal-entry.json` |
| 80,000 行窗口化/选择/Find/Go to Line | 通过 | `docs/large-text-virtualization-2026-10-23.md`、`docs/large-text-2000-2026-10-24.md` |
| 可访问树/UIA | 138 后代/168ms；离屏全文不可读 | `docs/large-text-accessibility-2026-10-24.md`、`docs/large-text-a11y-list-2026-10-24.md` |
| 记录文档 | 边界与影响 | `docs/large-text-wrap-boundary-2026-10-24.md` |

## 7. 运行时缺失与校验

| 项 | 结果 | 位置 |
| --- | --- | --- |
| 缺少 .NET | 宿主对话框 + `Download it now`；退出 0x80008083 | `artifacts/runtime-missing-2026-10-24/dotnet-missing-dialog.png` |
| 缺少 WebView2 | Augit 消息框含缺失项、官方地址、便携说明；退出 1 | `artifacts/runtime-missing-2026-10-24/webview2-missing-fallback-dialog.png` |
| VerifyRuntime.ps1 | 成功=0；错误哈希=10；错误大小=9；未签名=11；第三方签名=12 | `artifacts/runtime-missing-2026-10-24/verify-runtime-results.txt` |
| 首次打包启动回归 | 探测早于加载器加载；已修复并从 `51dcadd` 重建 | `docs/runtime-missing-messaging-2026-10-24.md` §2.1 |

## 8. 既有专项证据（本轮未重跑）

以下证据仍是有效历史记录，但不能替代本轮 RC 的整窗像素结论：

- 搜索超时重试：`docs/search-timeout-retry-2026-10-24.md`
- Diff 局部加载：`docs/diff-loading-normal-entry-2026-10-24.md`
- Push 非快进/认证失败/认证成功：`docs/push-non-fast-forward-2026-10-24.md`、`push-auth-failure-2026-10-24.md`、`push-auth-success-2026-10-24.md`
- Define Remote 自动 Fetch 失败/回滚：`docs/define-remote-autofetch-2026-10-24.md`、`define-remote-reference-2026-10-24.md`
- 大文本选择/拖动/键盘：`docs/large-text-select-all-2026-10-24.md`、`large-text-drag-scroll-2026-10-24.md`、`large-text-a11y-list-2026-10-24.md`
- 终端关闭/选择/ANSI/背景：`docs/terminal-close-running-2026-10-24.md`、`terminal-selection-copy-2026-10-24.md`、`terminal-ansi-colors-2026-10-24.md`、`terminal-background-2026-10-24.md`
- 主窗启动/关闭帧：`docs/main-window-startup-close-frames-2026-10-24.md`
- 冲突外部 abort/混合解决/逐步刷新：`docs/conflict-rebase-external-abort-2026-10-24.md`、`conflict-rebase-multifile-resolve-2026-10-24.md`、`conflict-rebase-multifile-continue-2026-10-24.md`

## 9. 范围裁决与限制

- 发布阶段裁决：`docs/acceptance-scope-rulings-2026-10-20.md` §9
- 发布收口台账：`docs/release-closure-2026-10-24.md`
- Beta 验收：`docs/beta-acceptance-2026-10-24.md`
- 已知限制：`docs/known-limitations-1.0.0-rc.1.md`
- 原像素协议：`docs/pixel-pair-protocol-2026-10-22.md`（历史结果 0 张，不因 RC 改写）

## 10. 未验证环境

- Windows 10 22H2 x64 实机。
- 跨屏/多显示器混合 DPI。
- 干净虚拟机/第二台机器的安装。
- 真实安装器缺少运行时下的确认、联网下载、取消、失败与重试。
- 真实 Narrator/NVDA 连续朗读会话。

这些项目在发布说明和已知限制中如实登记，不写成通过。
