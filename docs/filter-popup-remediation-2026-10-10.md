# 筛选弹层与提示系统整改（2026-10-10）

本轮针对真实前台 PyCharm／Augit 的弹层、筛选表单、键盘流程、工具轨提示和大字号树密度整改。上一轮文件图标、Changes密度和提交图改动作为既有基线保留。

## 规范范围与参考

- `docs/design-system.md` §9.3 的 200–420px 适用于带动作/图标列的通用上下文菜单；日期筛选是 `DateFilterPopupComponent` 的短列表弹层，按实际前台尺寸作为明确例外。
- 日期菜单采用 `Select…`、`Last 24 hours`、`Last 7 days`，宽108px、每行24px，图标列为空。
- 日期范围采用 IntelliJ `DateFilterComponent` 结构：横向 After／Before、各自独立启用复选框及日期/日历入口，默认窗口504×132，仅 OK；未启用端点不送筛选，Enter 等同 OK。字号放大后按字形度量扩展，未启用日期与日历入口灰显。
- 路径输入采用 `MultilinePopupBuilder` 结构：无标题、无底栏按钮，贴近 Path 控件，多行输入保留普通 Enter，Ctrl+Enter 提交，Esc/外部点击取消草稿；路径值按行清理并去空。
- 路径树采用独立目录缓存和懒加载，不依赖 Project 当前展开目录，保留复选状态和键盘展开/折叠；不展开目录联接。
- `FilterComponent` 语义：Down/Enter 打开筛选，Delete 清除，刷新后焦点回到等价筛选控件；在收纳菜单中的控件也按同一规则恢复焦点。
- 工具轨使用应用内 HelpTooltip 等价层：标题、已有快捷键、禁用原因和按工具轨方向定位；不再依赖浏览器原生 `title`。
- 大字号树按 `LafManagerImpl.patchHiDPI` 的 `Tree.leftChildIndent=7`、`rightChildIndent=11` 分别缩放；箭头槽、槽间距和图标槽同一字号档位缩放，文件图标绘制网格仍为16px。

依据：`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/DateFilterPopupComponent.java`、`platform/vcs-impl/src/com/intellij/openapi/vcs/versionBrowser/DateFilterComponent.java`、`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/MultilinePopupBuilder.java`、`platform/vcs-log/impl/src/com/intellij/util/ui/FilterComponent.java`、`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/VcsStructureChooser.java`、`platform/platform-impl/src/com/intellij/openapi/wm/impl/SquareStripeButton.kt`、`platform/platform-api/src/com/intellij/ide/HelpTooltip.kt`。

## 实现

- `web/src/mockup.js`：工具轨帮助提示、字号树几何令牌。
- `web/src/mockup.css`：日期短菜单、日期范围横向表单、路径浮层、独立路径树和 HelpTooltip 样式。
- `web/src/live-data.js`：日期端点启用状态、路径浮层 Ctrl+Enter、独立路径树懒加载、筛选 Down/Delete、分支 speed-search、焦点恢复与异步代次保护。
- `tools/audit/live-filter-popups.cjs`：实际筛选场景回归。
- `tools/audit/live-filter-shell.cjs`：隔离WebView2实际控件取证，普通目录仅按需读取，不保存设置或执行Git写操作。

`mockup.js` 与 `mockup.css` 已同步 `docs/ux-mockups/`；资源校验通过。

进入本轮与最终MD5：

| 文件 | 开始 | 最终 |
| --- | --- | --- |
| `mockup.js` | `6B88E2019C516472F88CD6F81828A95C` | `E9F14D7ABB43C64D5EE3C32BA092C3D1` |
| `mockup.css` | `B34402B37FA8B16A968F21AE38075DF9` | `2CA8E4966B16B48FC0C1CBFA08247CDA` |
| `live-data.js` | `BBD796D4EAD6DDB861B95D1EC68C87C5` | `9E0343E52D28CA4A482D6A1D8062B8DA` |
| `bridge.js` | `16E41439BB7F4C2A36C2E95A4375A823` | `16E41439BB7F4C2A36C2E95A4375A823` |

## 验证

| 范围 | 结果 |
| --- | --- |
| 筛选与提示场景 | `live-filter-popups` **19项通过**，覆盖浅/深主题、日期尺寸/键盘、路径浮层、路径树、分支 speed-search、HelpTooltip、Delete焦点、慢响应竞态及40px表单与任意深度引用树，9.62秒 |
| 项目树大字号 | `verify-ux-project-tree` **12/12通过**；96/120/144 DPI、13/40px字号，缩进按7+11缩放 |
| 主布局 | `live-shell --layout-only` **891项通过**，约43秒 |
| 既有场景选图与状态 | `live-shell --scene-alignment-only` **30项通过**，约13秒 |
| 静态页面渲染 | 浅/深主题分别55/55通过，31.27/31.65秒 |
| JavaScript/资源 | 运行时四共享文件字节一致，语法检查通过，`git diff --check`通过 |

最终静态校验：16个本批次触及文件UTF-8/LF通过、7个JavaScript文件语法通过、14个PowerShell脚本编码通过、文档声明 `DOC_CLAIMS_OK`；资源、编码、文档声明与空白检查合计3.14秒。最终清理核查为本轮相关进程0、9349监听0、临时目录不存在。

本批次没有C#或bridge协议改动；未再次跑完整业务套件。先跑局部失败场景，再在共享尺寸变更完成后跑完整布局矩阵；没有用历史全量2373项记录冒充当前全部业务通过。命令使用现有Playwright与Edge，不下载依赖：

```powershell
$env:AUGIT_BROWSER_EXECUTABLE = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
node tools/audit/live-shell.spec.cjs 'C:/Users/wl024/AppData/Local/OpenAI/Codex/runtimes/cua_node/2c9e75c4e9c71beb/bin/node_modules/playwright' --filter-popups-only
node tools/verify-ux-project-tree.cjs 'C:/Users/wl024/AppData/Local/OpenAI/Codex/runtimes/cua_node/2c9e75c4e9c71beb/bin/node_modules/playwright' 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
```

筛选场景证据位于 `artifacts/filter-popups-2026-10-10/`：PyCharm 的 `pycharm-date-range.png`、`pycharm-path-input.png`；Augit 的 `augit-date-menu.png`、`augit-date-range.png`、`augit-path-input.png`、`augit-path-tree.png`、`augit-rail-tooltip.png`及 `augit-filter-scenes.json`。真实外壳生效字体Microsoft YaHei UI 12px、DPR1.75，日期菜单108px/24px、日期范围504×132.286px（设备像素舍入），路径浮层415.08×86px，无标题/按钮；路径树读到 `docs/product-spec.md` 时Project中仍未加载该路径，脚本错误为空。窗口分别1640×985与1641×981，附件确认结构、密度和调用归属，不作为同尺寸同正文逐像素验收。

## 限制与清理

仍未完成同字体字形/抗锯齿、滚动条动态、全部 Git 状态色与复杂提交图、17个历史页面完整正文、深色全页面和 Windows 10 22H2 实机验证，不能宣称100%复原。

本轮未启动常驻服务；每次 Playwright 测试的浏览器与临时HTTP服务均在 `finally` 关闭。最后确认测试 Node、WebView2、9349端口和隔离目录无残留。PyCharm 与用户原有 Edge 未关闭。

本轮原生隔离实例PID28956及复核PID6964均经外壳 `window/close` 正常退出，诊断端口为9349；隔离目录 `.tmp/filter-webview-20261010`在核对绝对路径及进程归属后删除。正式截图和JSON作为验收附件保留。

日历入口采用本机浏览器的日期选择弹层；本轮验证了独立入口、启用状态和筛选提交，没有将浏览器日期面板记为Microba日历逐像素复原。工具轨只显示Augit已绑定的快捷键，未虚构Project/Commit/Terminal的PyCharm激活键。

手动启动与停止（同一PowerShell会话）：

```powershell
$augitFilter = Start-Process -FilePath 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit --web-root D:/github/Augit/web --no-session-restore' -PassThru
$augitFilter.Refresh()
if (-not $augitFilter.HasExited) {
    [void]$augitFilter.CloseMainWindow()
    if (-not $augitFilter.WaitForExit(5000)) { taskkill /PID $augitFilter.Id /T /F }
}
```
