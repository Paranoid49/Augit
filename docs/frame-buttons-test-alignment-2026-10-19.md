# `verify-ux-frame-buttons` 断言对齐（2026-10-19）

## 1. 结论

本批只修正测试本身的过期/易碎断言，**未改产品代码**；修正后套件 **24/24 PASS**。

此前失败分三层，全部是测试问题，不是产品回归：

1. `hover()` 把控件滚入视口后，旧测试拿滚动态 bounds 对比初始态，产生整页 x/y 平移的假失败。
2. 旧断言要求 40 号 UI 的 `Local` 标签和关闭段都等于 `--augit-icon-hit`（91px）；2026-10-19 大字号修复已把两者改为文字高度 **49px**，动作按钮仍 91px。
3. 旧测试把深色 `panel` 期望写成 `#1E1F22`、`mutedPanel` 写成 `#2B2D30`；`design-system` §6.2 的权威值是 `panel` **#191A1C**、`panel-muted` **#212326**，`#1E1F22` 是 editor-bg、`#2B2D30` 是 popup-bg。

## 2. 修正

- **滚动位置**：比较 bounds 前复位 `window` 和所有可滚动元素的 `scrollLeft/scrollTop`。
- **Local/关闭段高度**：读取 `--terminal-session-height`（普通字号回落到命中区），关闭段用 49px、More/Hide 用 91px；重叠检查不变。
- **深色面板期望**：`panel = rgb(25,26,28)`（#191A1C）、`mutedPanel = rgb(33,35,38)`（#212326），与设计系统一致。
- `terminal-large-actions` 继续由独立 `tools/test-terminal-large-actions.mjs` 5/5 守护。

## 3. 结果与证据

```text
主框架按钮与终端标题就绪/加载矩阵 24/24 通过。
```

- 修正前：`run.log`（`E6010CBD…5AC2`）bounds 假失败；`run3.log`（`5DAF830B…20BD`）`49 !== 91`。
- 面板期望修正中间态：`run5.log`、`run6.log`。
- 最终：`run6.log`（`24/24 PASS`）。
- 命令：
  `node tools/verify-ux-frame-buttons.cjs <playwright> <chromium> artifacts/.../screens6`
- `node --check tools/verify-ux-frame-buttons.cjs` 通过。

## 4. 边界与清理

- 这是测试与既有布局/设计系统对齐，不替代整页像素验收；页面级仍 **50**、像素仍 **0**。
- 未修改 `web/src/` 或视觉稿；UI 资产仍 PASS。
- 未提交、未推送；测试浏览器已关闭，无残留 Augit/PyCharm。
- `cleanup-check-178.json` 保留中间失败记录；`cleanup-check-179.json` 为最终 24/24 通过。
