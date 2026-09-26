# 18 · 图片与不可预览（第 6 区）

本册是 `11-surface-audit.md` 第 6 区的采集结果，场景：`image-preview`、`image-error`、`file-limit`。
口径同前：每条都带 checkout（`/mnt/d/github/intellij-community`，commit `576e328`）里的文件:行号。

**第 156 轮是采集轮，没有改产品代码**，这一区还出了一个**阻塞**：图像查看器的权威**不在这份 checkout 里**（见 §2），需要用户裁决。

## 1. 「不可预览」一半：权威是"三档限制 ＋ 只读预览"，不是"打不开"

### 1.1 权威

| 项 | 权威 | 出处 |
| --- | --- | --- |
| 限制有**三档** | 扩展点 `com.intellij.fileEditor.fileSizeChecker` 的 `ExtensionSizeLimitInfo` 定义三种：**内容加载**（content）、**智能感知**（intellisense）、**预览**（preview）上限 | `platform/core-api/src/com/intellij/openapi/vfs/limits/FileSizeLimit.kt:14-19`、`:33-36` |
| 三档的默认值 | 内容加载 = `FileUtilRt.getUserContentLoadLimit()` = 属性 `idea.max.content.load.filesize`，**默认 20 MB**；智能感知 = `PersistentFSConstants.getMaxIntellisenseFileSize()`（`idea.max.intellisense.filesize`，默认 2500 KB）；预览 = `FileUtilRt.LARGE_FILE_PREVIEW_SIZE`（`idea.max.content.load.large.preview.size`，默认 2500 KB） | `platform/util-rt/src/com/intellij/openapi/util/io/FileUtilRt.java:1091-1101`；`FileSizeLimit.kt:70-110` |
| 按扩展名可**放大**、不可缩小 | 扩展点可按扩展名给每档一个更大的值；**若给的值小于默认值则被忽略**（默认值同时是**最小值**） | `FileSizeLimit.kt:20-24`、`:63-67`、`:86-91`、`:104-109` |
| 判定入口 | `isTooLargeForContentLoading(fileSize, extension)` 等按扩展名取限值比较 | `:60-63` |
| **超限后的行为** | 仍然**显示只读预览**：`large.file.preview.notification` = **"The file is too large ({0}). Showing a read-only preview of the first {1}."**，其中 `{0}` 是文件大小、**`{1}` 是该扩展名的预览上限**（`FileSizeLimit.getPreviewLimit(extension)`） | `platform/platform-impl/src/com/intellij/openapi/fileEditor/impl/text/LargeFileNotificationProvider.java:49-56`；`platform/platform-api/resources/messages/IdeBundle.properties:2120` |
| 提示的形态与动作 | `EditorNotificationPanel`（**Warning** 状态）＋ 两个动作标签 `action.label.hide.notification`、`label.dont.show`；隐藏记在编辑器 `HIDDEN_KEY`、永久关闭记在 `PropertiesComponent` 的 `DISABLE_KEY` | 同上 `:37-58` |
| 另一个入口（大文件编辑器） | `com.intellij.largeFilesEditor` 走自己的编辑器，横幅文案 `large.file.editor.notification.text.the.file.is.too.large.so.showing.in.read.only.mode` = **"The file is too large: {0}. Read-only mode."**，同样带"隐藏／不再显示"两个动作 | `platform/lang-impl/src/com/intellij/largeFilesEditor/editor/LargeFileNotificationProvider.java:41-48`；`platform/platform-api/resources/messages/EditorBundle.properties:125` |

### 1.2 与 Augit 的对照

Augit 侧：`DocumentLimits` 是**单一全局阈值**（`MaximumTextBytes = 10 MB`、`MaximumImageBytes = 25 MB`、`MaximumImagePixels = 25 000 000`，
`src/Augit.Core/Documents/DocumentLimits.cs:5-9`），超限时宿主返回 `DocumentReadStatus.TextTooLarge`／`ImageTooLarge`，界面**整页拒绝预览**并给"使用系统默认程序打开"。

| 差异 | 权威 | Augit | 处置 |
| --- | --- | --- | --- |
| 阈值 | 内容加载 **20 MB**（可配） | 文本 **10 MB** | **登记**（属宿主常量 ＋ 规范值；且与下面这条行为差异一起改才有意义） |
| 超限后的行为 | **显示只读预览**（前 `{1}`，按扩展名的预览上限），并给可隐藏／可永久关闭的警告 | **整页拒绝**，只给"用系统默认程序打开" | **登记为宿主能力项**：要做"前 N 的只读预览"，宿主得先能返回截断内容（现在 `TextTooLarge` 只回状态） |
| 按扩展名的限制 | 支持（只能放大、不能缩小） | 无（全局一档） | **登记**：Augit 没有"按扩展名"的概念，属新增结构 |
| 图片上限 | **无权威可比**（见 §2） | 25 MB／2500 万像素 | 暂不动 |

⇒ 三项都**不是纯界面能解决的**：阈值与"截断预览"都在宿主侧，已记入 `10-backlog.md` §三·补 第 8 项（连同"落地时同步 `ux-spec.md` 的超限规则"）。

**第 208 轮补记（宿主侧已落地）**：`DocumentLimits` 改为权威三档（内容加载 **20 MB**／智能感知 **2500 KB**／预览 **2500 KB**，
扩展名登记只能放大、本 checkout 无任何登记），`ReadOnlyDocumentService` 对超过内容加载上限的文本不再整页拒绝，
而是按**完整 UTF-8 字符**边界返回前 `PreviewLimit(extension)` 字节的 `TextPreview` ＋ `previewBytes`
（消息用权威文案的中文对应："文件过大（{0}）。这里显示前 {1} 的只读预览。"），`document/read` 一并回传。
界面侧已在**第 209 轮**接上：`TextPreview` 映射成纯文本只读视图（权威大文件编辑器没有语言能力），
编辑器顶部画 Warning 横幅（`文件过大（{0}）。这里显示前 {1} 的只读预览。`）＋「隐藏通知」/「不再显示」
两个动作（前者只记会话，后者写 `HideLargeFileWarning` 设置并立即生效），由 `verify-ux-large-file-preview` 与
`live-shell` 覆盖。

**已经对齐的部分**：超限时**不静默**（权威给警告面板、Augit 给说明页）、都提供"离开本应用去打开"的出口（权威靠"隐藏提示后仍只读预览"，Augit 给"使用系统默认程序打开"）——这部分 Augit 的既有呈现不违背权威意图。

## 2. 「图片」一半：**权威不在这份 checkout 里**（阻塞）

审计表第 6 区的"权威待定位"是**图像查看器的缩放/平移组件**。本轮用三种互相独立的检索确认它不在 `/mnt/d/github/intellij-community`：

1. `rg -l "class ImageViewer|class UberImageViewer|ImageViewerUI|class ImageEditor"` 在 `platform`＋`plugins` 下 **0 命中**；
2. `find platform plugins -name "*ImageViewer*" -o -name "*ImageEditor*" -o -name "*ImageFileEditor*"` **0 命中**；
3. 对 `platform/utils` 之外的全仓 `rg -l "UberImageViewer"` **0 命中**（这是该组件在 IntelliJ 里的历史类名）。

（唯一相关命中是 `xdebugger.imageEditorUIProvider`，那是调试器的内存视图，不是图片查看器。）
⇒ 图像查看器（New UI 的 Image Viewer，含 缩放/适应/实际大小/旋转 等动作）**属于未包含在此 checkout 的插件**，本轮**无法采集**。

因此 `image-preview`／`image-error` 两个场景的"权威对齐"目前**没有可比对象**：Augit 的 `image-preview.js`（缩放档位 `[.1 .25 .5 .75 1 1.25 1.5 2 3 4 6 8]`、滚轮缩放、拖动平移、适应区域）与视觉稿是**自定基线**，既不能说它对齐、也不能说它偏离。

**裁决结果（第 211 轮）**：**认定为"无本地权威"** —— 此后 `image-preview`／`image-error` 只按**内部一致性**
（视觉稿＝运行时基线）维护，不再声称与 PyCharm 对齐；审计表相应改成"权威不在本 checkout"。

## 3. 验证（第 156 轮）

- **本轮未改产品代码**：四个登记哈希与登记值一致（`mockup.js` `6fce1b9b…`、`mockup.css` `6da20a85…`、`live-data.js` `acf3ed7d…`、`bridge.js` `8d2d3173…`），按快照规则不需要重跑套件；只改文档，`check-doc-claims` 仍 `DOC_CLAIMS_OK`。
- 未采到的部分全部写明理由与出路（§1.2 的宿主项、§2 的裁决项）。
