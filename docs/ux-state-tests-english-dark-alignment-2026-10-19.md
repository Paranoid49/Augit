# 标题栏与文档按钮状态测试对齐（2026-10-19）

## 1. 结论

此前登记为失败的 `verify-ux-titlebar` 和 `verify-ux-document-button-states` 都是**过期测试期望**，本批修正后：

```text
标题栏状态矩阵 12/12 通过。
文档按钮状态矩阵 60/60 通过。
```

加上前一阶段的 `verify-ux-frame-buttons` **24/24**，三个已知失败套件均已通过。本批只改测试，未改产品代码或视觉稿。

## 2. 标题栏（12/12）

- 主菜单已英文化：`['File','View','Git','Terminal','Settings']`，旧中文数组过期。
- 深色 hover 从旧 `#FFFFFF16`（0.086）订正为 `#FFFFFF17`（0.09）；chips/菜单同样为 `#FFFFFF17`。
- 深色 pressed 从旧 `#393B40` 订正为 `#FFFFFF29`（0.16）。
- 深色 chrome 从旧 `#2B2D30`（popup-bg）订正为 `#26282C`。
- 深色 faint 从旧 `#5A5D63` 订正为 `#4C4F56`。

## 3. 文档按钮（60/60）

- 英文界面：`Find in File`、`Match case`、`Words`、`Regex`，旧中文 aria 全部过期。
- 深色 hover/pressed/faint 与标题栏同源订正为 `#FFFFFF17`/`#FFFFFF29`/`#4C4F56`。
- 可选截图步骤：部分场景（如图片预览）没有文档工具栏，`controls.first().hover()` 会超时；现仅在 `controls.count()` 非空时截图，不影响状态矩阵。

## 4. 证据

- `artifacts/pixel-followup-2026-10-19/ux-state-tests-current/titlebar-final.log`（`EFBF20D1…4669`）。
- `.../document-final.log`（`82ACC746…81E5`）。
- 中间失败日志 `titlebar.log`…`titlebar6.log`、`document-buttons.log`…`document-buttons5.log` 保留，未删除。
- `node --check` 两份测试通过。
- 命令：
  `node tools/verify-ux-titlebar.cjs <playwright> <chromium> <evidence>`
  `node tools/verify-ux-document-button-states.cjs <playwright> <chromium> <evidence>`

## 5. 边界与清理

- 测试通过不等于整页像素验收；页面级仍 **50**、像素仍 **0**。
- 未修改 `web/src/` 或视觉稿；UI 资产 PASS。
- 未提交、未推送；测试浏览器已关闭，无残留 Augit/PyCharm。
- `cleanup-check-180.json`。
