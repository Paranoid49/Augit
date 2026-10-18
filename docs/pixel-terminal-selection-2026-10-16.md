# 终端选区与正常入口同步（2026-10-16）

## 产品修正

原终端浅深主题均硬编码活动选区 `#2F466F`。现由 `web/src/terminal-palette.js` 提供编辑器方案选区：浅色活动 `#A6D2FF`、失焦 `#B5B7BD`；深色活动 `#214283`、失焦 `#4C4F56`。`live-data.js` 的既有终端初始化和主题刷新共同使用，不增加测试专用外观分支。焦点移出后保留文本选择。正文继续固定随包 JetBrains Mono 13。

依据为本地 IntelliJ 提交 `576e32820af82f97529d70f9269726803a27c016`：`JBTerminalSystemSettingsProviderBase.java` 的 `getSelectionColor()` 使用编辑器选区键；`TerminalColorScheme.kt` 委托全局编辑器方案；`platform/platform-resources/src/themes/Light.xml` 覆盖浅色活动选区，`DefaultColorSchemesManager.xml` 提供 Default/Darcula 父方案颜色。安装版 PyCharm 2026.2.1 的 `intellij.platform.ide.impl.jar` 内 Light/Islands Dark 方案已核对；失焦值来源为本地源码父方案，不冒充本轮安装版运行时取色。

不采用此前终端截图原点相差22物理px作为固定偏移：两端窗口和终端原点不同，该数字不能作为产品补偿。完整终端同态配对仍未完成。

## 验证

- `node --test tools/audit/terminal-palette.test.cjs`：2项通过，150.74ms。
- `node --check tools/audit/live-terminal-palette.cjs`、`node --check web/src/live-data.js`：通过。
- 真实外壳专项：浅深ANSI索引、主题预览/取消、xterm节点保留、活动/失焦选区和固定字体全部通过，退出码0。字体13、逻辑行格18px、系统DPR1.75。
- 证据：`artifacts/pixel-followup-2026-10-16/terminal-selection-visible/palette.json` 及浅深PNG。样本直接写入真实xterm，不是Shell输出，也不是PyCharm双端配对；截图仅覆盖可见行。
- 首次 `terminal-selection` 运行因选中第0行位于视口外而超时；驱动增加滚动到顶部后通过，保留失败记录，不计入通过。
- 删除驱动中已失效的字体/字号选择操作，改为断言固定标签与旧控件不存在。截图保留真实光标，避免截图工具临时写样式。
- 产品两文件已同步Debug/Release随包目录；每套24个资源逐字节一致，共享9项资产检查通过。

## 基线与清理

分支 `dsh`，HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`。保留混合工作区，未暂存、提交或回滚。MD5：`live-data.js` 从 `C8DA64065C77E60234F2D614334E8DC4` 变为 `6200A1F1FFF74A92DC9A55AEFD0D4DAA`；`terminal-palette.js` 当前 `7C9C2268467C309AA035ECD3EF0A6BCC`。本批未记录后者修前哈希，不用HEAD代替混合工作区修前内容。其余未改：`mockup.js=44D6C53684B74DBC6116FF25F7C22932`、`mockup.css=D5DADE8F4DB84506031A9AB3B1F5DB80`、`bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9`。

两次终端实例PID24144/5224已退出，端口59824/64391无监听，临时目录 `Augit-terminal-colors-mYUb0w`、`Augit-terminal-colors-2F1o9I` 已删除并独立核对。

日常启动：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
```

专项复验：

```powershell
node tools/audit/live-terminal-palette.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-16/terminal-selection-visible
```

专项finally自动停止本实例并清理临时profile；日常窗口正常关闭即可，异常时执行 `taskkill /PID <本次输出的PID> /T /F`。本批没有C#变更，不重复无关完整构建或全量测试。55页/全部模态配对、背景刷新、完整终端/冲突配对及系统阴影目标仍未完成。Windows10按用户要求排除本次验收，跨屏DPI保留未验证。
