# 正文、Markdown 与工具条取证（2026-10-11）

本批以当前 PyCharm 2026.2.1 前台、已安装 Markdown 插件与 IntelliJ 平台源码核对。DSH 承担高消耗检索，两轮结果经独立复核。没有安装、复制或读取 PyCharm 字体文件，也没有保存用户配置变化。

## 前台实际条件

- Appearance：Microsoft YaHei UI／12，Zoom100%，Islands Light，Sync with OS 开启，Compact mode 关闭。
- Editor → Font：JetBrains Mono／13.0，Line height1.2，Enable ligatures 关闭。
- Languages & Frameworks → Markdown：Preview rendering engine 为 Chromium browser，Default layout 为 Editor and Preview，Preview layout 为 Split vertically，Preview font size13，Sync scroll 开启；Custom CSS 的 Load from 和 CSS rules 均未勾选，输入区空且禁用。只展开观察，再 Cancel 返回，未改值。
- 当前 AGENTS.md 是左原文右预览，Raw／Split／Preview 在顶部标签区右侧。只读浏览和设置观察未编辑该文件。
- Augit 用户配置界面 Microsoft YaHei UI／12、等宽 Cascadia Mono／13；当前 Markdown 预览正文继承界面字号12。两端等宽字族不一致。

## 源码与资源证据

| 项 | Augit 当前路径 | 当前参考实际路径 | 结论 |
| --- | --- | --- | --- |
| 预览正文 | `web/src/mockup.css:3356` padding24／36／40px、line-height1.62，UI 字体与字号；`docs/design-system.md` §5 明确指定 UI 归属 | 安装插件 `plugins/markdown/lib/intellij.markdown.jar!/org/intellij/plugins/markdown/extensions/common/baseStyles/default.css:38-46`：Helvetica／Arial 等、line-height1.6、margin0 2em；动态 colors.css 使用 MarkdownPreviewSettings 的独立预览字号覆盖 body | 当前前台确有独立 Preview font size13，与 Augit UI12不同。预览字号归属与现行规格冲突；不能靠调整截图配方抹去此差异。实际 JCEF 字族仍需最终渲染字体证据，声明字体不等于使用字体。 |
| 标题 | 同文件 `:3361-3370` h1=29px／bottom28px，h2=21px／margin26 0 13px；h3–h6 无规则。`web/src/markdown.js:89-95` 实际动态支持 h1–h6 | 默认 CSS `:195-206,239-289`：标题 margin-top1em／bottom16px，h1–h6 为2.2／1.8／1.3／1／1／1em，独立行高及 padding | 字号联动和标题间距机制不同，已确认真实 provider 与自定义CSS条件后继续做逐元素测量。 |
| 行内代码／代码块 | `mockup.css:3375-3396` 独立等宽字号，pre padding8／12px、radius4、行高继承1.62 | 默认 CSS `:406-446` inline字号0.9em／radius3；pre字号0.85em／padding16px／line-height1.45／radius3 | 默认样式链不同；代码字号归属受现行规格约束，不能自行改变。语法高亮属于产品排除，不列为缺陷。 |
| 模式动作位置 | `mockup.js:3502` 在正文永久 document-toolbar 中；CSS至少36px、按钮28px | `platform/platform-impl/src/com/intellij/openapi/fileEditor/TextEditorWithPreview.kt:202-203`：New UI＋有tabs时 isShowActionsInTabs；`:376` 正文 right toolbar 过滤 view actions。当前安装 MarkdownEditorWithPreview 无 override | 前台与调用链均支持标签区动作；Augit 额外正文工具栏改变内容顶部与留白。其余正文动作保留产品范围，不整体删掉只读工具。 |
| 正文行高 | `mockup.js:1540-1545` Canvas包围盒×DPR取整×1.2 | `EditorView.java:842-850,991-1019` AWT metrics、scheme lineSpacing以及 registry 分支 | 参数1.2相同不能证明度量等价；需要同实际字体、字号、DPI的前台基线测量。 |

`BaseStylesExtension` 的实际安装字节码显示 default.css＋colors.css 按 BEFORE_ALL 加载；`InlineStylesExtension` 自定义CSS为 AFTER_ALL。Markdown 预览还支持临时缩放，因此设置值13不能代替最终 computed style 以及实际字形基线读数。

Markdown 的 action place 为 TEXT_EDITOR_WITH_PREVIEW，不能套用 EDITOR_TOOLBAR 的 fontSizeOffset−2，也不能把 error stripe 的 EditorToolbarButtonLook 当作 Markdown 按钮证据。Swing ActionButton 的最终外盒要加 minimum、iconInsets、component insets，通用源码22px不是此处最终盒尺寸。

## 需继续验收

同文档、同分栏宽、同主题、同DPI，测 body／h1–h6／p／pre／inline code 最终字号、字族、实际使用字体、基线和内外距；验证大字号、窄窗口、滚动同步与模式切换的焦点。现阶段未取得这些完整矩阵，不宣称100%复原。

DSH 完成会话 `session-d350a7ef-0f8d-4021-878f-0277471e4a7b` 两轮；其“UI13”和“JCEF声明JetBrains Mono即实际可用”两项误报已撤回。没有产生或遗留本专项进程／临时文件。完整批次见 [持续审计记录](goal-audit-2026-10-11.md)。
