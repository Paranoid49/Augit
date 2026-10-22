# Markdown 预览参考排版与项目树状态表达（2026-10-22）

本文记录 2026-10-22 用户裁决后的首批实现：Markdown 预览按 PyCharm 实机与 `intellij-community` 的 Markdown 插件排版，项目树文件状态按参考只改文件名颜色。两项都只完成代码、规范与计算样式/正常入口局部证据，不是整窗像素合格。

## 1. 裁决与实现

用户对 Markdown 字体/字号、Push 失败呈现、文件状态表达三项统一答复：“参考 PyCharm 实机与 `D:/github/intellij-community`”。本批实施前两项中的 Markdown 与文件状态；Push 失败仍待下一批。裁决原文见[验收范围与裁决](acceptance-scope-rulings-2026-10-20.md) §8。

### 1.1 Markdown

参考插件 `plugins/markdown/lib/intellij.markdown.jar` 的 `baseStyles/default.css` 写正文 14px，但参考环境实际把 Markdown“Preview font size”设为 **13**（与编辑器字号一致），覆盖了插件默认值。实测证据：

- 参考 h1“Pixel reference”墨迹宽 **351 物理 px**；Augit 14px 候选为 **377 px**，13px 为 **351 px**。
- 参考 h1 墨迹高 36 px；Augit 14px 为 39 px，13px 为 36 px。
- 参考正文/代码墨迹带在 y=334–355 / 424–441；Augit 14px 分别低 13 px，13px 后回到同一带。

因此实现取实际参考值，而不是插件 CSS 的默认 14px：

| 规则 | 实现值 |
| --- | --- |
| 预览正文字族/字号/行高 | `Helvetica, Arial, freesans, sans-serif` / 13px / 1.6 |
| 左右内距 | 2em（26px） |
| 标题倍率/行高/字重 | 2.2／1.8／1.3／1／1／1；1.4（h2 1.2、h3 1）；600（Arial 600 实际匹配 Bold 字面） |
| 标题留白 | 顶部 1em、底部 16px，h1–h3 顶部内距 .6em |
| 段落/列表/表格/代码块外边距 | 16px |
| 代码字族 | 随包 `JetBrains Mono`，回退 `Consolas` 等 |
| 代码块 | 0.85em（11.05px）、行高 1.45、内距 16px、圆角 3px |
| 行内代码 | 0.9em（11.7px）、内距 .2em .4em、外边距 2px、圆角 3px |
| 表格单元 | 内距 6px 13px |

同步修订：

- `product-spec.md` §7：Markdown 预览改为参考排版，不再跟随界面/等宽字号。
- `design-system.md` §5.3 第 139/141/173 行：字号 13px、代码 .85/.9em、标题 600。
- `ux-spec.md` §7.3 字号归属行。
- `docs/nui-behavior/21-markdown-preview-fonts.md`：旧“正文跟随 UI、代码跟随等宽”结论标为已被本次裁决取代。
- `docs/pixel-pair-protocol-2026-10-22.md` §4.5：候选实现改为已实施，记录 13px 实机口径。

共享 `mockup.css` 已同步到 `docs/ux-mockups/mockup.css`，`verify-ui-assets.ps1` PASS。

### 1.2 项目树文件状态

参考 Project Files 视图只用文件名颜色表达 Git 状态，没有行尾 A/M/? 徽标。实现改动：

| 位置 | 改动 |
| --- | --- |
| `mockup.js` `liveTreeRowHtml()` | 删除 `statusMarker` 的可见 `<span class="tree-status">`；保留 `file-status-*` 文件名颜色、`data-git-status`/`data-index-status`/`data-worktree-status` 与 `aria-label` 中的 “Git status:…” |
| `mockup.js` `treeGitStatus()` | 删除已无消费者的 `marker` 字段 |
| `mockup.css` | 删除 `.tree-status` 规则；补注释说明状态只走颜色与无障碍属性 |
| `ux-spec.md` 第 171 行 | 改为“只用文件名颜色，不额外显示行尾徽标” |

注意：行首蓝色的 `M↓` 是 **Markdown 文件类型图标**，不是状态徽标；`README.md` 文件名字色才是修改状态。正常入口截图 `normal-initial.png` / `normal-readme-13px-client.png` 可核对。

## 2. 正常入口证据

| 项目 | 值 |
| --- | --- |
| 启动 | `powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1 -Workspace D:/JetBrains/PyCharm-2026.2.1-reference/fixture -SettingsFile D:/github/Augit/.tmp/settings-markdown-pair-2026-10-22.json` |
| 实例 | PID 22728，正常关闭 |
| ProductVersion | `0.1.0+8c6920e94e5c19c59f4e8c24f9b9972b2b276f49` |
| `Augit.exe` / `Augit.dll` SHA256 | `30F0275F…927A94` / `FDFEB4FB…43E6B0` |
| `mockup.js` / `mockup.css` | `8DA5E119…B2487C` / `C35CF5D1…C9E868` |
| 隔离配置 | 从本轮全局设置复制，清空会话恢复项；Light、Microsoft YaHei UI 12、JetBrains Mono 13 |
| 桌面/窗口 | 单屏 2880×1800、DPI 168；最大化，客户区 2880×1716（截图裁掉 12 px 外框后测得） |
| 参考图 | `artifacts/pixel-pair-main-project-2026-10-22/pycharm-pf3.png`（同一 PY-262.9437.214、DPI 168、Project Files + README preview-only） |

### 2.1 墨迹带对照（物理 px，客户区坐标）

| 元素 | 参考 | Augit（13px） | 差 |
| --- | --- | --- | --- |
| h1 | y 243–278（h36），x 760–1110 | y 243–278（h36），x 758–1107 | 垂直完全一致；左 −2、宽 −3 |
| 正文行 | y 334–355（h22），x 758–880 | y 334–355（h22），x 756–882 | 垂直完全一致；左 −2、宽 +2 |
| 代码行 | y 424–441（h18），x 786–972 | y 423–442（h20），x 784–986 | 上 −1、高 +2、宽 +16 |
| 编辑区左缘 | 714 | 711 | −3（左侧栏宽差） |

正文内容左缘相对编辑区：参考 758−714=44，Augit 756−711=45，差 1 px。因此垂直排版、标题/正文位置和左内距已对齐；剩余 2–3 px 水平差来自左侧栏分隔（见[侧栏核对](pixel-pair-protocol-2026-10-22.md) §7 第 2 条）。

代码行比参考宽 16 px（≈8.5%）：参考 JCEF 实际回退到较窄的等宽字体，Augit 使用随包 JetBrains Mono 11.05px。产品规格固定 JetBrains Mono；本批不把代码字族改为 Consolas 凑这 16 px，登记为字体回退差异，后续若要求整页通过需另行裁决。

### 2.2 未覆盖

- 只有 1 个浅色样本；深色、界面字号 20/40、窄窗口、多标签未重测。
- 只做了 Markdown 正文区与项目树状态的局部测量，不是整窗像素合格；页面级仍 **50**、整窗像素仍 **0**。
- 参考截图是既有同环境文件，未在本批重新采集参考；参考的 Run 配置选择器、右侧工具轨、trial 等仍在共享区域外。

## 3. 验证

- `verify-ui-assets.ps1` PASS（9 项共享资源）。
- `node tools/verify-ux-markdown.cjs …` **6 组三模式连续交互 + 2 组拖动边界**通过；计算样式断言正文 13px、代码 11.05px、行内 11.7px，即使 root/`--augit-code-size` 改为 20/17 也不跟随。
- `mockup-scenes.spec.cjs` **54/54（light）**、**54/54（dark）**。
- 计算样式与实时场景断言同步更新：`tools/audit/live-restoration-fixes.cjs`、`live-restoration-fixes-shell.cjs`、`live-bundled-fonts.cjs`、`tools/audit/live-shell.spec.cjs`（项目树只颜色、无 `.tree-status`）。
- 全量 `live-shell.spec.cjs` **通过 2339 项断言，退出码 0，未执行 0 项**（运行于本批代码与更新后的断言）；该套件是无头 Chromium + 宿主桩，不替代正常入口证据。
- `git diff --check`、脚本编码与文档 claim 检查按批次执行。

## 4. 边界与清理

- PID 22728 经 Alt+F4 正常关闭；按父进程核对无本实例 `msedgewebview2` 残留。
- 全局 `%LOCALAPPDATA%\Augit\settings.json` SHA256 始终为 `8840E775…462CF`，最后写入时间未变；隔离副本与截图留在本机 `.tmp/` 和 `artifacts/markdown-typography-2026-10-22/`。
- 当前只剩 1 个浅色正常入口样本；深色、界面字号 20/40、窄窗和多标签的 Markdown 排版未重测。代码字族宽度差、参考 Run 配置选择器等仍按 §2.1/§2.2 登记，不能据此记整窗像素合格。
