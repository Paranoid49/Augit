# Augit 1.0.0 Beta 验收报告（2026-10-24）

> 结论：**Beta 收口的 A 类发布阻塞已清零**；关键 B 类项已取得结论（B5/B8 安装链路通过，B1/B2/B3/B4 通过或带披露边界）；B6 的安装器真实缺失运行时交互、B7 的真实屏幕阅读器会话仍是**环境缺口**，不写成通过，也不阻止 1.0.0-rc.1 候选交付，但阻止宣布正式 1.0 完成。
> 本报告只覆盖本机可验证范围；Windows 10 22H2、跨屏 DPI、干净虚拟机安装仍未验证。

## 1. 候选身份

| 项 | Beta | RC |
| --- | --- | --- |
| 版本 | `1.0.0-beta.1` | `1.0.0-rc.1` |
| 源码提交 | `51dcadd356cd2fc5b8910dc60690d50416245491` | `06e41072771542958c244aec70a4333f7d6dbaee` |
| 产品代码差异 | — | 仅版本号（csproj、release 默认值） |
| 便携包 | `Augit-1.0.0-beta.1-win-x64-portable.zip`，3,522,193 B，SHA256 `0d11e998136b9b2fd241276fe31073424faface5bd30db4b4821892e47b96c05` | `Augit-1.0.0-rc.1-win-x64-portable.zip`，3,522,176 B，SHA256 `a65016ba9987c165bf0c4dd258b1362cae9ad781d68253f6cb481aff78607a25` |
| 安装器 | `Augit-1.0.0-beta.1-win-x64-setup.exe`，5,010,079 B，SHA256 `f937bf99cfcd112220c5ba42736b36a80b414b2cd5cafe7520a66f13cf2294a6` | `Augit-1.0.0-rc.1-win-x64-setup.exe`，5,010,433 B，SHA256 `47310fa3e8c6edacb30024db70be2243b126b7c8bcc043199154c5fa46456076` |
| 程序集 ProductVersion | `1.0.0-beta.1+51dcadd…` | `1.0.0-rc.1+06e4107…` |

产物绝对路径：

- Beta：`D:\github\Augit\artifacts\release-1.0.0-beta.1\final2\`
- RC：`D:\github\Augit\artifacts\release-1.0.0-rc.1\final\`

## 2. 实测环境

- Windows 11 家庭版中文版 25H2，`10.0.26200`，x64；屏幕 2880×1800，175% DPI，单显示器。
- .NET SDK 10.0.201；运行时 `Microsoft.NETCore.App 10.0.5`、`Microsoft.WindowsDesktop.App 10.0.5`（另有 8.0.25）。
- WebView2 Runtime `155.0.4283.62`；Git for Windows 2.45.1；Node 24.14.0；ffmpeg 8.0.1。
- 管理员验收在同机同一用户下通过 UAC 提权执行；测试使用隔离临时仓库、隔离设置和临时端口。

## 3. 逐项结论

| 项 | 结论 | 证据 |
| --- | --- | --- |
| A1 README 截断 | **已修复并回归** | README 重写；包内 README 8.7KB；发布脚本校验 |
| A2 LICENSE 缺失 | **已修复并回归** | 包内 `LICENSE` 存在；安装后同目录存在 |
| A3/A4 版本身份与安装器 FileVersion | **已修复并回归** | Beta/RC 程序集 ProductVersion 带提交号；setup ProductVersion 为预发布串、FileVersion `1.0.0.0` |
| A5 缺运行时提示 | **已修复并验证（受控条件）** | 缺少 WebView2 显示官方地址并退出 1；缺少 .NET 由宿主显示 `Download it now`；首次包启动回归已修复 |
| B1 外部文件 500ms | **本批同轮 10/10 通过（236–293ms）**，历史 611/552/624ms 未复现、未归因，保留风险 | `refresh-host-stage-2026-10-24.md` |
| B2 本地 Git 500ms | **通过**：checkout 分支可见 117–152ms、完整应用 201–232ms | 同上 |
| B3 真实鼠标 Continue | **通过**：真实前台 OS 鼠标命中按钮，Git 状态/HEAD/索引符合预期 | `conflict-rebase-real-mouse-continue-2026-10-24.md` |
| B4 大文本软换行 | **通过但披露边界**：80,000 行全量构建 5.23s；操作与恢复通过 | `large-text-wrap-boundary-2026-10-24.md` |
| B5 安装/升级/卸载 | **通过**：beta→rc 覆盖升级，PATH 精确恢复、右键菜单清理、设置保留、共享运行时不变 | `artifacts/install-acceptance-2026-10-24/installed-report.json` |
| B6 安装器缺运行时交互 | **部分通过**：`VerifyRuntime.ps1` 0/9/10/11/12 全通过；安装器真实缺失运行时交互未测 | `runtime-missing-messaging-2026-10-24.md` |
| B7 真实屏幕阅读器 | **未验证**：只有强制 accessibility + UIA；离屏全文不在可访问树 | `large-text-accessibility-2026-10-24.md` |
| B8 候选包启动/退出 | **通过**：便携包与安装目录均启动出窗口、正常关闭、无残留 | 便携报告、安装报告 |

## 4. 安装链路实测（B5/B8）

- Beta 安装：退出码 0；默认 `C:\Program Files\Augit`；README/LICENSE/第三方说明齐全；开始菜单快捷方式存在；
  PATH 项 1 个（ExpandString）；资源管理器右键菜单存在；安装后启动窗口 918.3ms、标题 `Augit`、退出码 0。
- RC 覆盖升级：退出码 0；程序集 `1.0.0-rc.1+06e4107…`；PATH 仍为 1 项且值与 Beta 完全相同；
  右键菜单、开始菜单、文档保留；用户设置 SHA256 未变（`8840E775…2CF`）。
- 卸载：退出码 0；安装目录删除；卸载记录 0；右键菜单移除；开始菜单移除；系统 PATH **逐字恢复**到安装前；
  .NET 运行时与 WebView2 版本前后完全一致；用户设置保留。
- 报告：`artifacts/install-acceptance-2026-10-24/installed-report.json`；
  安装/升级日志 `beta-install.log`、`rc-upgrade.log` 同目录。
- 签名：Beta/RC setup 均为 `NotSigned`（Augit 自身未签名）；下载的微软运行时才校验 Microsoft 签名。

## 5. 本轮验证摘要

- Release 构建：0 警告、0 错误。
- 单元测试：Core 72/72、Infrastructure 206/206、Shell 252/252。
- 全量 live-shell：**2379/2379**，退出码 0，未执行 0。
- `verify-ui-assets` PASS；31 个 PowerShell 脚本编码规则 PASS；`check-doc-claims` OK；coverage、JS 语法、`git diff --check` 通过。
- 便携包烟测：Beta/RC 均 PASS（必需文件、web 双向哈希、版本、真实启动截图、关闭 109–130ms、退出 0、无残留）。
- 安装验收：beta 安装、rc 升级、卸载全部 PASS（见 §4）。

## 6. P0/P1 与残留风险

- **P0（数据丢失/损坏）**：本批未发现。
- **P1（核心流程不可完成/持续崩溃）**：本批未发现；首次最终包的启动误报已修复，未进入交付。
- 保留风险：
  1. 500ms 历史三次慢样本未归因（本批 10/10 未复现），不能写成所有条件稳定达标。
  2. 80,000 行软换行 5.23s 同步构建与 80,000 节点；已列入已知限制。
  3. 真实安装器缺运行时交互、真实屏幕阅读器会话缺环境。
  4. Windows 10、跨屏 DPI、干净虚拟机安装未验证。
  5. 整窗像素历史 0 张，未达成原整改目标。
- 这些风险不包含已确认的 A 类阻塞；均已在发布材料中披露，交给 RC 真实使用观察与用户裁决。

## 7. Beta 放行结论

**允许构建并交付 1.0.0-rc.1 候选。** 不得据此宣布正式 1.0：仍需 RC 的真实日常使用观察，以及 B6/B7、Windows 10/跨屏等缺口的后续处理。
