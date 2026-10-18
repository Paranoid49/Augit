# 19 · 内置终端（第 5 区）

2026-10-17终端提示符间距定位：参考Reworked安装包非首块顶部7、已完成块底部6逻辑像素，经JBUI.scale；旧原图输出偏移40px、提示符17px，23px残差与13×1.75相近，仍仅来源推断。JAR/class哈希及原始字节码保留。未改产品、未新增实例，完整逐行未通过；简洁终端命令块有意差异提案待用户答复，未算批准。见[来源记录](../terminal-block-layout-source-2026-10-17.md)。

2026-10-17全可见区域接续：正常入口14行含提示符和空白、同尺寸参考2880×1716已取证。两内容区并集1370040像素仍差97830（RGB2），面板与提示符块/焦点不同，不计完整配对。9项JS、7项Python通过，终端产品未改；参考进程退出但临时目录删除被拒绝。详见[全行与同尺寸记录](../pixel-terminal-visible-rows-2026-10-17.md)。

2026-10-17正常入口接续：终端采集由直接exe及合成配置改为日常启动器、真实配置副本、默认profile。正常DPI1.75实际字符CDP确认随包JetBrains Mono；八行步长32/31交替，与既有参考一致，但墨迹框局部对齐后每行仍有1255–1923像素超过RGB2容差，未取得整窗同态，不能宣称完整逐行通过。8项身份/配置回归通过，证据与启停清理见[本批记录](../pixel-terminal-daily-entry-2026-10-17.md)。

2026-10-16字体来源诊断：随包WOFF2与独立安装TTF在同浏览器、五档缩放和深浅两色的十组Canvas样本逐像素相同，未修改产品。此证据不是实际DOM／PyCharm配对，八行实机残差仍未解决；两项定向测试通过。详见[本批记录](../pixel-terminal-font-source-2026-10-16.md)。

2026-10-16终端字形反证：实机13px与23/1.75px的完整PNG完全相同，向下取整反而增大差异；八行残差一致，独立安装TTF与参考TTF逐字节相同。未改产品字号，完整终端配对未通过，证据及清理见[本批记录](../pixel-terminal-raster-size-2026-10-16.md)。

2026-10-16深色终端实机配对：修正误继承的工具面板底色/文字色为控制台#1E1F22/#BCBEC4；修后八行178×21墨迹外框、32/31步长与参考一致，仍有抗锯齿残差，未计整窗通过。6项定向与真实主题回归通过，两套产物同步；参考临时目录清理被策略拒绝，进程已停止。详见[深色终端配对](../pixel-terminal-dark-base-2026-10-16.md)。

2026-10-16普通入口深色取证：消除历史场景与可变字号审计偏差；八行真实Shell步长32/31交替，累计偏差最多0.5物理px，仅实际端，不算深色双端配对。见[普通入口记录](../pixel-terminal-normal-entry-2026-10-16.md)。

2026-10-16选区接续：终端选区现按浅深编辑器方案区分活动/失焦颜色，保留选择；真实xterm专项与2项单测通过，Debug/Release随包资源同步。完整同态配对仍未完成。证据、哈希、失败与清理见[选区记录](../pixel-terminal-selection-2026-10-16.md)。

2026-10-14接续：xterm的1.7实际产生约29px行格，并非规范22px；参考Reworked 2025为JetBrains Mono13、行距1.0。规范取舍待用户裁决，未改产品参数。证据、验证及清理见[终端行格记录](../pixel-terminal-metrics-2026-10-14.md)。

本册是 `11-surface-audit.md` 第 5 区的采集结果，场景：`terminal`、`terminal-close`。
口径同前：每条都带 checkout（`/mnt/d/github/intellij-community`，commit `576e328`）里的文件:行号。

第 157 轮落地了 **Esc 的语义**（权威：终端里的 Esc 是"退出到编辑器"，不是送给 Shell 的字节）；本区其余差距**没有本地权威可比**（会话/标签层在未包含的 Terminal 插件里，见 §2）。

## 1. 权威（本 checkout 里能采到的部分）

| 项 | 权威 | 出处 |
| --- | --- | --- |
| **Esc 的语义** | `TerminalEscapeKeyListener`：按键被按下且未被消费时，`ToolWindowManager.activateEditorComponent()` **把焦点交给编辑器组件**，随后 `e.consume()` ⇒ **ESC 不送给 Shell**；类注释写明"similarly to `InternalDecoratorImpl.processKeyBinding`" | `platform/execution-impl/src/com/intellij/terminal/TerminalEscapeKeyListener.java:34-42` |
| 什么键算"退出键" | 在**终端工具窗口内**：以 `Terminal.SwitchFocusToEditor` 动作的**快捷键**为准（`KeymapUtil.getKeyStrokes`，不是硬编码 Esc）；在工具窗口外（如执行控制台）：有该动作则同样按键匹配，**没有快捷键时用 Esc**；Terminal 插件未加载时也用 Esc | 同上 `:44-72`（`isEscape` = `VK_ESCAPE && modifiersEx == 0`） |
| 不处理的守卫 | `toolWindow == null` ⇒ 直接返回（不在工具窗口里就不抢这个键） | 同上 `:47-51` |
| 光标形状 | `TerminalUiSettingsManager.CursorShape` = **BLOCK（默认）／UNDERLINE／VERTICAL**，文本取 `terminal.cursor.shape.*.name` | `platform/execution-impl/src/com/intellij/terminal/TerminalUiSettingsManager.kt:150-154`、`:137` |
| 补全弹窗默认值 | `maxVisibleCompletionItemsCount = 6`、`autoShowDocumentationPopup = true` | 同上 `:141-146` |
| 终端字号的来源 | `detectFontSize()`：演示模式取 `presentationModeFontSize`，否则取 **`UISettingsUtils.scaledConsoleFontSize`**（跟随控制台字体，不是硬编码数值）；主题/字号变化时 `resetFontSize()` | 同上 `:113-119`、`:44-52` |
| 标题状态机 | `TerminalTitle`：标题可变、`change{}` 原子更新、**监听者可在任意线程收到通知**（进程改标题与用户重命名都走这一处） | `platform/execution-impl/src/com/intellij/terminal/TerminalTitle.kt:18-45` |

## 2. 这一区**缺什么**：权威自己写明了搬迁

会话/标签/关闭确认/重命名这些属于**终端插件**，而它不在本 checkout：

- `platform/execution-impl/src/com/intellij/terminal/session/TerminalSession.kt` 全文 7 行，内容是：
  `@Deprecated("Was moved to the Terminal plugin: org.jetbrains.plugins.terminal.session.TerminalSession")` ⇒ **权威自己写明会话层已搬到 `org.jetbrains.plugins.terminal`**，而该插件目录不在本 checkout（`plugins/` 下没有 `terminal`）。
- 全仓检索 `SwitchFocusToEditor` **只命中 `TerminalEscapeKeyListener.java` 一处**（`getAction("Terminal.SwitchFocusToEditor")`），说明**动作定义与默认快捷键也在那个插件里**。

⇒ 本区能对标的是"终端面板/键位/设置"这一层；**标签生命周期、关闭确认、重命名的键盘交互**没有本地权威可比。与第 6 区（图像查看器）同类，但证据更强：**这次是权威自己在代码里写明搬迁**，不是"没找到"。

## 3. 与 Augit 的对照与落地

Augit 的终端是 xterm.js ＋ 宿主轮询（`live-data.js` 的 `startTerminal()`），规格见 `ux-spec.md` §7.16。

| 项 | 权威 | Augit 原状 | 处置 |
| --- | --- | --- | --- |
| **终端里的 `Esc`** | 焦点交给编辑器组件并消费该键（`TerminalEscapeKeyListener.java:34-42`），只对**不带修饰键**的 Esc 生效（`isEscape`） | 所有按键都经 `onData` 原样转发（`terminal/write`）⇒ **Esc 被送给 pty** | **第 157 轮落地**：`attachCustomKeyEventHandler` 拦下不带修饰键的 Esc → 焦点交回正文并消费（返回 `false`，xterm 不再处理）；**找不到可聚焦正文时不拦**（对应权威 `toolWindow == null` 的守卫）。测试同时断言 `terminal/write` 里**没有** `\u001b` |
| 正文里的 `Tab` | 终端应把 `\t` 交给 Shell（xterm 的默认行为） | 已对齐，`live-shell` 早有断言 | **本来就对齐** ✓ 两条一起看：**Tab 进 Shell、Esc 出终端** |
| 关闭确认（前台命令） | UI 在缺失插件里 | 已实现：`terminal/status` 返回 `foreground` 时先弹确认，文案说明"结束前台命令、Shell 及其整个子进程树" | **登记**：与 `ux-spec.md` §7.16 一致、实现与断言都在，但**无本地权威可比**，故不写"已对齐" |
| 单会话、隐藏不结束、恢复保留输出、关闭回收 | 同上 | 规格已定并实现（`ensureTerminal`／`stopTerminal`／`closeTerminalNow`） | 同上 |
| 光标形状／补全弹窗设置 | BLOCK/UNDERLINE/VERTICAL、6、true | Augit 设置页只有 Shell 类型与自定义命令（`ui-compliance.md:799`），没有终端外观设置 | **不新增**：Augit 没有终端外观设置项，不因权威有默认值就加一页设置 |

## 4. 验证（第 157 轮）

- 产品改动：`web/src/live-data.js`（`startTerminal()` 增加 Esc 键处理 ＋ 新增 `focusEditorFromTerminal()`）。未改 `mockup.js`／`mockup.css`／C#。
- `tools/audit/live-shell.spec.cjs` 的 §7.16 段新增一条 `check`：`终端里 Esc 把焦点交回正文且不送给 Shell` —— 在 `.xterm-helper-textarea` 上按 Esc，断言焦点**离开**终端、进入 `.editor-content`，且 `terminal/write` 收到的字节里没有 `\u001b`。

## 2026-10-14 ANSI16色来源整改

核对当前checkout的JBTerminalSchemeColorPalette.kt：ANSI色由ColoredOutputTypeRegistryImpl的索引映射读取编辑器方案；Islands Dark继承Darcula，未覆盖ANSI16色。Augit原terminalTheme只传背景/前景/光标/选区，ANSI色沿xterm默认，存在明确来源差异。新增web/src/terminal-palette.js，按DefaultColorSchemesManager.xml的Default/Darcula控制台16色映射到xterm字段，终端初始化及主题刷新均经既有terminalTheme使用。普通white映射CONSOLE_GRAY_OUTPUT，brightWhite才映射CONSOLE_WHITE_OUTPUT。

只修ANSI16色，不根据未经实机确认的推测修改行距1.7、选区色或光标。新增定向node --test tools/audit/terminal-palette.test.cjs通过，包含浅色全16项、深色关键索引及返回对象独立性；live-data语法与git diff --check通过。没有启动终端/服务器，无新增依赖。真实输出栅格、256色/真彩色、主题变化及PyCharm双端逐行配对仍未完成，不能把令牌来源一致当作视觉验收。此前插件源码缺失结论仍有效，平台配色源码可用。
## 2026-10-14 终端主题切换与节点保留

ANSI16色已接入权威 Default/Darcula 控制台映射。真实 Settings 保存浅深主题、预览深色后取消均通过。测试最初直接写设置未触发页面预览，改为真实控件后，截图又发现区域重绘使 xterm 脱离页面、显示静态样本。已在 rebindAfterRender 中复用 reattachTerminal，保留原会话节点；测试新增 DOM 身份断言，避免仅检查脱离页面的对象而误报。

证据：artifacts/pixel-followup-2026-10-14/terminal-palette-final/。截图已查看，显示受控 ANSI 中文/英文输出；截图只含视口末尾行，全部16行及颜色索引由 buffer 断言覆盖。样本直接写入 xterm，未发送 Shell 命令，不算 PyCharm 双端配对。定向真实验证、配色单测1项、JS语法、共享资产、git diff --check 通过；未跑全量。

live-data.js MD5：BECE8DF677BDE6EFC0C567B1E67C70FE → 851EFBF8C278A2D40DB6208CE2E60496。共享JS/CSS及bridge未变。最终PID26604、端口62039、临时目录Augit-terminal-colors-MmN2Q6已清理；前次PID9604及其临时目录也已清理。

启动命令：

```powershell
node tools/audit/live-terminal-palette.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-14/terminal-palette-final
```

脚本finally自动停止；手动停止只填写本次脚本输出的PID：`taskkill /PID <本次PID> /T /F`。

2026-10-15终端用户裁决已执行：xterm基础行距1.0替代1.7，规范和共享静态终端同步；175% DPI下默认行格29.1667→17.1818逻辑px，13→17→13及深浅主题真实专项通过。此前“待裁决”均为历史状态。证据、哈希、失败迭代、启动停止和清理见[终端行距整改](../pixel-terminal-lineheight-2026-10-15.md)。双端逐行配对与整体整改仍未完成。

2026-10-15自然终端配对：修复父进程重定向标准句柄导致Shell提前退出，ConPTY4项及真实自然横幅通过；两行相同文字宽差仍为−6/−21物理px，完整终端与55页同态配对仍未完成。原始PNG、失败追踪、哈希、启动停止及清理见[自然横幅配对与句柄修复](../pixel-terminal-pair-2026-10-15.md)。

2026-10-15终端格宽批次：新增锁定xterm DOM格宽补丁并修复全局零字距覆盖，自然横幅两行宽差−6/−21最终降至0/0物理px。48组浏览器网格/光标/选区/鼠标命中、256组JBR最大字宽及真实主题/字号/字体切换专项通过；行基线、抗锯齿与完整终端配对仍未完成。来源、失败迭代、哈希、启动停止及清理见[终端格宽记录](../pixel-terminal-grid-2026-10-15.md)。整体目标未完成。

2026-10-15终端基础行格接续：随包字体以参考AWT基础高度驱动静态与实时终端，13/17号字为18/24逻辑px。新增相同字形逐行分析；真实17号字九类字母步长双端均42物理px、两行宽高一致，13号字仍32对31，未通过完整配对。48组合与真实设置专项通过，进程/profile已清理；依据、失败边界、哈希及命令见[行格配对记录](../pixel-terminal-row-grid-2026-10-15.md)。整体目标未完成。

2026-10-15终端相位分析：新增JBR/浏览器20行重复字形夹具，8份样本最大累计漂移0.5物理px；实机0.25/0.5px文字偏移改变两行步长，但基线差补偿和原点取整均未稳定修复，因此未写入产品。4项分析器测试及真实取证通过，完整配对仍未完成。证据、失败与启停清理见[终端相位记录](../pixel-terminal-phase-2026-10-15.md)。

2026-10-15终端八行实机接续：两端七个相同字形步长均为32/31交替，最大累计偏差0.5物理px，本样本无累积行距漂移。原点恒差22px、字形像素差及实际中部鼠标光晕仍在，不计55页合格配对；产品行高不变。四项分析器测试通过，本批进程、端口、临时目录独立检查已清理。原图、量化、哈希和命令见[八行实机记录](../pixel-terminal-live-rows-2026-10-15.md)，整体目标未完成。

同批补采：系统鼠标移出终端后，完整八行文字区域无遮挡，双端各行墨迹框均178×21物理px、步长一致；焦点、窗口、背景及RGB差异仍未消除，不计完整配对。详见上述八行实机记录的补采段。
