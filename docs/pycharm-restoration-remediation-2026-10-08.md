# PyCharm 复原整改结果（2026-10-08）

本次已按建议顺序完成主要可执行整改，并在真实 Augit 外壳中应用参考外观。不能称为整体 100% 复原：PyCharm 22 页正文对照、同字体正文栅格化和 Windows 10 实机证据仍未完成。源码规则和内部一致性测试只证明对应范围。

## 逐项结果

| 原编号 | 结果 | 实施与证据 |
| --- | --- | --- |
| 1 · 语言与术语 | 已整改 | 主界面、菜单、设置、搜索、弹窗和提示使用英文；宿主说明集中兼容转换，用户正文与提交信息保留原文。 |
| 2 · 实际 14px | 已校准 | 真实设置页执行恢复；磁盘保存独立界面字号 12、等宽字号 13，旧配置读取保持兼容。 |
| 3 · 界面字体 | 已校准 | 生效 Microsoft YaHei UI，已核对本机安装字体；实际 DPI 为 175%。 |
| 4 · 密度公式 | 已整改并验证默认基线 | Tree／Log 使用各自的 24／26 基准和字号档位，状态栏保留 28px 参考基准；大小字号矩阵保持可用。尚不能将全部未采页面视为像素验收。 |
| 5 · 工具轨 | 已整改 | 37×40 容器、30×30 背景、40 步长、20 图标、6px 圆角；测试区分命中区和绘制区。 |
| 6 · 设置溢出 | 已整改 | 四个分类在 12、14、40px 下无横向溢出或字段裁切，说明自然换行。 |
| 7 · 设置底栏 | 已整改 | OK／Cancel／Apply；应用不关闭、取消不保存、无改动时 Apply 禁用。 |
| 8 · 弹窗按钮 | 已整改 | 区分 78×34 外层与 72×28 内部绘制区，保持进行态和失败态布局；Clone 已通过完整相关验证。 |
| 9 · 正文／Diff | 规则已整改，字体对照待补证 | 字体与界面独立，行距按字体度量×1.2；仍使用已安装的 Cascadia Mono，没有复制 PyCharm 的 JetBrains Mono 字体。 |
| 10 · 状态加粗 | 已整改 | Git 文件状态和一般状态标记使用普通字重，标题与必要分组保留强调。 |
| 11 · YAML | 已复核并收敛令牌 | 当前开始提交已有三层；旧清单过时。增加专用字母色和 Islands Dark SVG 补丁色。 |
| 12 · 日志搜索 | 图形与下拉已整改，持久化范围仍不同 | Text or hash 使用搜索历史图形并提供最近五项；普通引用／作者搜索不加下拉。当前历史仅保留本窗口会话。 |
| 13 · 深色混色 | 已整改 | 按 Islands Dark 别名及继承链取色，启动表面同步；代码色与界面色分开。 |
| 14 · 提交图 | 已整改，复杂图对照待补证 | 引用名哈希／片段号＋HSB 取色，修正完整父哈希关联，保留已知提交的筛选与分页颜色。不是固定四色循环。 |
| 15 · 滚动与悬停 | 已整改，真机细节待对照 | 普通／悬停滚动条档、标签溢出滚动条、树与表格的悬停归属分别处理。 |

## 生效配置与附件

| 项目 | 校准前 | 校准后 |
| --- | --- | --- |
| 界面字体 | Segoe UI | Microsoft YaHei UI |
| 界面字号 | 14px | 12px |
| 等宽字体 | Cascadia Mono | Cascadia Mono |
| 等宽字号 | 共用 14px | 独立 13px |
| 主题设置 | System | Light |

真实校准由 [外壳脚本](../tools/audit/live-restoration-shell.cjs) 通过本次 WebView2 诊断端口执行；不是只修改代码默认值。最近目录、窗口、标签、活动文件及其他设置保持原值。

附件目录：`artifacts/pycharm-restoration-2026-10-08/`。`settings-before-font-calibration.png` 与 `settings-after-font-calibration.png` 记录字体校准阶段，`shell-font-calibration.json` 记录实测值。CDP 截图用于真实外壳结构和生效配置，不代替未遮挡屏幕字体证据。

## 验证记录

触发阶段验证的原因是界面语言、全局 CSS 令牌、共享字体度量和外壳启动表面同时变化。小批次先运行受影响测试，阶段完成后再扩大范围。

| 批次 | 命令／范围 | 结果 |
| --- | --- | --- |
| 设置兼容 | `dotnet test tests/Augit.Infrastructure.Tests --no-restore --filter FullyQualifiedName~SettingsStoreTests` | 15 项通过，测试执行 177ms |
| 构建 | `dotnet build Augit.slnx --no-restore --disable-build-servers -c Release` | 通过，0 警告／0 错误，27.26s |
| 恢复参考外观 | `live-shell.spec.cjs … --restoration-only` | 21 项通过；覆盖取消、应用、四字体值、正文保留、布局和颜色关联 |
| 主框架字号 | `verify-ux-typography.cjs …` | 20 场景通过 |
| Clone | `verify-ux-clone.cjs … <临时截图目录>` | 26 项通过 |
| 主布局 | `live-shell.spec.cjs … --layout-only` | 891 项通过 |
| 场景渲染 | `mockup-scenes.spec.cjs … light`／`dark` | 分别 55／55 通过 |
| 外壳主题 | `dotnet test tests/Augit.Shell.Tests --no-restore -c Release --disable-build-servers --filter FullyQualifiedName~ShellThemeTests` | 14 项通过，52ms；初次阶段失败已定位为启动底色未同步并修复 |
| 说明转换 | `node tools/audit/ui-text.test.cjs` | 通过；原始文件、提交信息、路径和输出保持原文 |
| 格式与资源 | 语法检查、CSS 配平、脚本编码、共享资源、`git diff --check` | 已执行；最终结果随收口登记 |
| 全部实时场景 | `live-shell.spec.cjs …` | 正在收口；旧中文匹配断言已逐项按实际英文说明更新，不能将中途失败计为通过 |

当前工作分支及初始哈希见 [批次分册](nui-behavior/24-reference-restoration.md)。最终四文件哈希和完整阶段结果在收口后补记。

## 未完成的参考验收

- 历史 5 页只有入口证据：push、push-no-remote、rollback、stash、worktrees。
- 历史 17 页没有正文对照：blame、changes-context-menu、clone、conflict-list、conflict-resolver、file-history、git-unavailable、image-error、operation-progress、operation-result、remote、repository-init、repository-search、reset、stash-drop-confirm、stash-manager、workspace-open。
- 本会话已检查 [computer-use 技能](C:/Users/wl024/.codex/plugins/cache/openai-bundled/computer-use/26.1002.51308/skills/computer-use/SKILL.md) 及其指导。指导要求“Use `node_repl` JavaScript for all Computer Use actions.”，但本会话没有该入口，且运行依赖中没有 `@oai/sky`；因此不能重新采集这些 PyCharm 页面，也不沿用“没有 PyCharm 进程”的旧理由。
- PyCharm 进程仍为用户原有实例，未关闭、未改配置。没有 Windows 10 实机；同字体、同文本的正文／Diff 屏幕对照和深色全场景真机对照仍待补证。
- 当前搜索历史会话范围与参考的应用级持久化不同；复杂永久提交图对照尚未完成。这些具体差异不合并进“待办清零”。

## 资源与手动命令

校准实例 PID 23564 及所属六个 WebView2 已正常退出并逐 PID 复查。调试端口为 9345；没有留下服务器或终端会话。保留的截图／JSON 是验收附件，临时解析器及测试产物由本批次结束时清理。

手动启动当前 Release 界面（同一 PowerShell 会话）：

```powershell
$augitPreview = Start-Process -FilePath 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit --web-root D:/github/Augit/web --width 1646 --height 981 --no-session-restore' -PassThru

# 观察结束后，仅关闭本次启动的实例和子进程。
$augitPreview.Refresh()
if (-not $augitPreview.HasExited) {
    [void]$augitPreview.CloseMainWindow()
    if (-not $augitPreview.WaitForExit(5000)) {
        taskkill /PID $augitPreview.Id /T /F
    }
}
```

自动化浏览器验证使用已安装的 Playwright 与 Chromium，不下载浏览器。测试脚本的 `finally` 会关闭浏览器与服务；如需人工中止本次测试进程，先使用 `Get-CimInstance Win32_Process` 确认其命令行和 PID，再执行 `taskkill /PID <本次 Node PID> /T /F`，不得按进程名称结束用户已有应用。
