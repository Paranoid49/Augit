# Diff 加载状态改为参考形态（2026-10-20）

按 2026-10-20 用户裁决第 3 条：Diff 局部加载 100% 参考 PyCharm 2026.2.1 与本地 `intellij-community`，参考使用 loading indicator 就对齐 loading indicator，不虚构独立结果页。

## 1. 参考源码事实

本地 `intellij-community`（HEAD `576e32820af82f97529d70f9269726803a27c016`）：

- `platform/diff-impl/src/com/intellij/diff/impl/DiffRequestProcessor.java`：
  - 初始活动请求是 `new LoadingDiffRequest()`；
  - 头部工具栏包含 `MyProgressBar extends JProgressBar`，`setIndeterminate(true)`、默认不可见，请求准备期间由 `DiffContext.showProgressBar(true)` 显示；
  - 加载期间正文是 `LoadingDiffRequest` 的消息面板，不是骨架屏。
- `platform/diff-impl/src/com/intellij/diff/requests/LoadingDiffRequest.java`：消息文本取 `CommonBundle.getLoadingTreeNodeText()`，即 `Loading...`。
- `platform/diff-impl/src/com/intellij/diff/requests/UnknownFileTypeDiffRequest.java` 与 `ErrorDiffRequest`：失败/未知类型也留在 Diff 区域内的消息面板，不新开结果页。

## 2. 实现

| 位置 | 改动 |
| --- | --- |
| `web/src/mockup.js` | 新增 `diffLoadingProgress()`：输出 `<span class="diff-progress" role="progressbar" aria-label="Generating diff">`；实时工作区 Diff、文件历史预览、静态 `diff-loading` 场景和比较页的加载态统一使用它。 |
| `web/src/mockup.js` | 加载正文改为 `<div class="diff-loading-message" role="status">Loading...</div>`；删除 12 行骨架条与行号槽骨架。 |
| `web/src/mockup.css` | 新增 `.diff-progress`（120×4，蓝色不定条，1.1s 循环）与 `.diff-loading-message`（正文居中、240px 最小高度）；删除 `.diff-loading-side`/`-gutter`/`-line`/`-gutter-line` 与 `.comparison-loading-label`。 |
| `docs/ux-mockups/mockup.js`、`mockup.css` | 与运行时字节一致（`verify-ui-assets` PASS）。 |
| `tools/audit/live-shell.spec.cjs` | 加载态断言从 `comparison-loading-label === 'Generating diff…'` 改为 `diff-progress` 存在；其余工具栏顺序、禁用矩阵、文件栏提示断言不变。 |

保留的 Augit 产品动作：文件栏的 `Generating diff…` 小提示、加载态的 `Cancel Comparison` 按钮（Augit 已有取消比较能力；参考的 Diff 加载头部没有同名按钮，登记为产品差异，不删除既有能力）。

## 3. 验证

- `verify-ui-assets.ps1` PASS。
- `node tools/audit/mockup-scenes.spec.cjs <playwright> light` **54/54**；`dark` **54/54**。
- `node tools/verify-ux-history.cjs <playwright> <chromium>` **42 组通过**（含 `history-diff-loading`）。
- `node tools/audit/diff-initial-loading.test.cjs` **2/2 PASS**。
- 静态页面实测：`diff-loading.html` 渲染出 `.diff-progress`、`.diff-loading-message=Loading...`，无 `.comparison-loading-label`、无骨架条；截图 `artifacts/diff-loading-2026-10-20/diff-loading-light.png`。
- 完整 `live-shell` 套件运行结果见 §5（本文件先记录，结果另补）。

## 4. 边界

- 这是**源码形态对齐**，不是整页像素合格：没有取得参考实机 Diff 加载态截图，未比较进度条位置/宽度/动画速度的像素值。
- 实时加载态只持续 150ms 延迟之后、Diff 返回之前；本次未在正常入口抓到该瞬态截图。页面级配对仍 **50**、像素合格仍 **0**。
- 未覆盖：深色、窄窗口、历史比较加载的取消按钮布局、参考实机加载态。

## 5. 完整 live-shell 与既有测试债务

- 命令：`node tools/audit/live-shell.spec.cjs D:/github/universe-genesis-sandbox/node_modules/playwright-core`。
- 首次运行在“提交详情未加载”处失败：`__augitCommitLoaded` 已按代码注释使用完整提交身份（`live-data.js` 写 `commit.fullHash`），测试仍等短哈希 `aaa1111`。在干净 HEAD `83650e1` 的临时 worktree 复跑得到同一失败 ⇒ 与本批 Diff 加载改动无关。
- 修正该过期期望后继续，又在 Settings/File View 的 `[data-setting="codeFontSize"]` 处失败：该控件已按 2026-10-15 裁决移除。改为断言 `[data-fixed-code-font]`=`JetBrains Mono`、`[data-fixed-code-size]`=`13` 且无编辑控件后，继续到“改等宽字号会作用到正文”等 4 项字体段落仍按旧的可编辑模型断言。
- 这些属于 2026-10-15 裁决后的测试债务，已登记在[live-shell 测试债务](live-shell-test-debt-2026-10-20.md)；本批**不重写整套字体段落**，因此**没有取得完整 live-shell 通过**，也不把本批 UI 改动记作套件验证通过。
- 本批实际覆盖：`mockup-scenes` 54/54（浅/深）、`verify-ux-history` 42 组、`diff-initial-loading` 2/2、状态矩阵 24/12/60、UI 资产字节一致、静态截图。
