# 19 · 内置终端（第 5 区）

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
