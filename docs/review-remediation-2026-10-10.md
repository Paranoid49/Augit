# 审查整改与同尺寸复核（2026-10-10）

本轮保护进入任务前已有的未提交改动，只处理本次审查指出的状态、异步、快捷键、宿主文案和规范冲突。分支 `dsh`，提交 `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`；权威源码为 `D:/github/intellij-community` 的 `576e32820af82f97529d70f9269726803a27c016`。

## 已修复

| 问题 | 处理与证据 |
| --- | --- |
| 工具轨偏宽 | 同窗口截图揭示主网格仍为旧 42px 轨道列并叠加 6px 左内距；改为 37px 列、无额外左内距。项目表面从 x=52 改为 x=41。按钮维持 37×40、背景 30×30、20px 图标和 6px 圆角。 |
| 项目树疑似 30px 行高 | 在两窗口均为 1646×981、175% DPI 的前台附件中复核；Augit 实际 Microsoft YaHei UI 12px、树行 24px。没有按不同尺寸截图的估测继续缩小行高。 |
| 根目录选择丢失 | 模板与增量更新的路径回落都使用空值判断，保留根路径空字符串；根选择跨区域重建和 F5 保持。 |
| 根目录折叠 ARIA 错误 | 根节点 `expanded` 从当前展开集合读取，与箭头、子行可见性和 F5 后的状态一致。 |
| 恢复任务覆盖手动折叠 | 目录恢复复用用户操作代次，逐步落地并在每次 await 后检查；用户折叠立即停止旧恢复，不再展开后续子目录。无恢复标签时也恢复目录。 |
| 慢目录响应覆盖 F5 | `loadChildren` 与重列目录共用刷新代次；旧展开响应不写新缓存，折叠再展开仍显示新清单。 |
| 分帧更新丢失焦点或新选择 | 每一帧读取当前选择，替换或移动焦点行时把焦点交还新节点，宿主已销毁/视图切换时停止旧帧。150 行受控分帧场景覆盖第二帧之后的用户选择。 |
| Ctrl+F 越过输入区/模态 | 文档和 Diff 共用模态、输入区、已消费事件、输入法会话和重复按键门；宿主全局入口显式打开同一查找条，避免两份监听器共同执行。设置、快速打开、其它 textarea 和 IME 连续场景已验证。 |
| 深色滚动条 | 采用 `ManyIslandsDark` 的 `#80808059`／`#8080808C`，不再使用 `ScrollBarPainter` 未覆盖的默认色。 |
| 静态设置失败缺 Apply | `settings-save-failure` 保留 OK／Cancel／Apply，与其它设置场景一致。 |
| 宿主中文直出 | 目录选择器的标题和应用校验说明改为英文；启动、导航和渲染失败使用固定英文说明及错误类型/代码，不直接显示本地化异常堆栈。Windows 自带按钮继续使用系统语言。 |

## 用户裁决

用户本轮确认：工具轨采用上述 Islands 规则；三栏冲突正文使用等宽字体实际度量×1.2，并在设备空间取整；项目树、Changes 和引用树不另画 hover 背景；快速打开单击只选择，双击或 Enter 正式打开，上限 30 项。产品、UX、设计系统和相应模块记录已同步；终端继续使用独立的 xterm 行距契约。

## 验证

| 范围 | 命令简称 | 结果 |
| --- | --- | --- |
| 本轮回归 | `live-shell.spec.cjs … --review-only` | 16 项通过，包含所有交错缺口、快捷键、Enter、深色令牌和三栏行距 |
| 主布局 | `live-shell.spec.cjs … --layout-only` | 891 项通过，约 20 秒；主网格尺寸变化触发对应完整矩阵 |
| 参考表面 | `live-shell.spec.cjs … --reference-only` | 10 项通过 |
| 恢复外观与设置 | `live-shell.spec.cjs … --restoration-only` | 23 项通过 |
| 项目树 | `verify-ux-project-tree.cjs …` | 12 组通过，涵盖主题、DPI、字号、无 hover 和深层缩进 |
| 查找 | `verify-ux-find.cjs …`、`verify-ux-find-documents.cjs …` | 48 个布局状态、5 组输入法场景及 JSON/Markdown 流程通过 |
| 宿主局部 | `dotnet test … --filter WorkspacePickerTests/ShellFailureTextTests` | 5 项通过，49ms |
| 宿主模块 | `dotnet test tests/Augit.Shell.Tests --no-build --no-restore -c Release` | 123 项通过，16 秒 |
| 搜索上限 | `dotnet test … --filter RipgrepSearchServiceTests` | 7 项通过，3 秒；最佳 30 项顺序与 Gitignore 行为保持 |
| 格式 | `dotnet format … --verify-no-changes --include <本轮 C# 文件>` | 通过 |
| 最终构建 | `dotnet build Augit.slnx --no-restore --disable-build-servers -c Release` | 0 警告、0 错误，3.07 秒 |

本轮没有高频重跑全量实时套件；先按审查范围建立独立回归批次，再扩展到完整布局、查找和 Shell 模块。历史的全量通过记录不代表当前全部页面通过。

最终静态检查包含共享资源字节一致、UTF-8/LF、脚本编码、JavaScript 语法、文案转换、文档声明及 `git diff --check`。最终构建首次因本轮实机复测进程占用 apphost 失败，关闭该实例后重新构建，不将失败尝试记为通过。

本轮文件哈希（MD5，开始值来自进入本轮时的真实工作区）：

| 文件 | 开始 | 最终 |
| --- | --- | --- |
| `mockup.js` | `85A7C60EB1E28E9BD53CDD4794589992` | `7B1F93FF60351E6F28ED3346447B26D7` |
| `mockup.css` | `BD520F4BCC6927A1806E8B4568149EAE` | `10B348C173B942272C4CEE10D80AAF02` |
| `live-data.js` | `03DF8AA702E6DBA95E70E4B0D1569FA1` | `96CF8937546C57324A97B18337D45F77` |
| `bridge.js` | `16E41439BB7F4C2A36C2E95A4375A823` | `16E41439BB7F4C2A36C2E95A4375A823` |
| `current-find.js` | `E73D7F5C30E97D7E1C6A74770BA4F44E` | `4FA1A857CA86EAB6C17E0C46964E064E` |

## 前台证据与边界

附件目录为 `artifacts/review-remediation-2026-10-10/`：`pycharm-same-size.png`、`augit-same-size-native.png` 为同尺寸前台截图；`augit-same-size.json` 为本轮 WebView2 的生效字体、DPI、树行和工具轨计算几何。`augit-before-rail-padding.png` 记录额外占位修正前的表面；该图由原普通窗口采集，不能冒充同尺寸终值。

本轮真实前台执行目录名称单击、双击、独立箭头折叠、文件单击和 Enter 打开只读 JSON；截图和 UIA 已确认单击文件保持原标签，Enter 后新增正式标签。PyCharm 临时最大化取证后恢复到原 1640×985 普通窗口，保留原浅色主题。

同尺寸首屏复核只证明本轮记录的树行、工具轨和手势范围；字体栅格、各 Git 文件状态的同场景颜色、复杂提交图及 17 个历史页面的完整 PyCharm 正文对照仍未完成。Windows 10 22H2 实机仍未验证，不宣称 100% 复原。

## 清理与手动命令

本轮首个实例 PID 28664 因既有 WebView2 配置正在使用而失败，已关闭；正式隔离实例 PID 29184 使用诊断端口 9347 和 `.tmp/review-webview-20261010`。任务结束时关闭本实例、确认其 WebView2 子进程退出，并删除该隔离目录和一次性文档脚本；正式截图和 JSON 保留作验收附件。浏览器测试服务与浏览器由 `finally` 清理。

手动启动与关闭（同一 PowerShell 会话）：

```powershell
$augitReview = Start-Process -FilePath 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit --web-root D:/github/Augit/web --no-session-restore' -PassThru

$augitReview.Refresh()
if (-not $augitReview.HasExited) {
    [void]$augitReview.CloseMainWindow()
    if (-not $augitReview.WaitForExit(5000)) { taskkill /PID $augitReview.Id /T /F }
}
```

浏览器测试可直接运行上表命令，不需常驻服务器；中断时先核对本次 Node PID，再执行 `taskkill /PID <本次测试 PID> /T /F`，不能按进程名称结束用户的其它应用。
