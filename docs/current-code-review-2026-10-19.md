# 当前未提交代码复核（2026-10-19）

## 1. 范围

本轮只复核当前工作区未提交改动，没有新增产品代码。

`git diff --stat`：14 个文件，325 insertions / 72 deletions。代码侧集中在：

- `web/src/mockup.js` + 视觉稿：
  - 状态栏基准 `--augit-status-height-base`；
  - 终端大字号动作区 `terminal-large-actions`；
  - Diff 2000 行截断说明；
  - Smart Checkout 影响说明 class。
- `web/src/mockup.css` + 视觉稿：
  - 终端状态栏 4 CSS 增高与 1 物理px 顶边；
  - 深色终端 1 物理px 底边；
  - 终端大字号动作/会话定位；
  - Diff 截断独立网格行；
  - Smart Checkout 自动换行。
- 测试：frame-buttons / titlebar / document-button-states 的过期期望修正。

## 2. 复核结论

- `live-data.js` 已回到 `12D57E34…1B3D`，无 ANSI 临时映射残留；
- 无 `terminal-ansi.js` 或对应临时测试残留；
- 新增 CSS 规则均有明确使用点，未发现重复选择器互相覆盖；
- `mockup.js` / `mockup.css` 与 `docs/ux-mockups/` 字节一致；
- 三个状态矩阵套件通过：frame-buttons 24/24、titlebar 12/12、document-button-states 60/60；
- 定向守护：终端状态栏 7/7、Diff 截断 5/5、Smart Checkout 5/5、终端大字号 5/5。

## 3. 边界

- 这是代码/测试复核，不是整页像素或整体验收；
- 页面级同态配对仍 **50**、像素合格整页仍 **0**；
- Settings 性能、终端 ANSI/深色首行/右侧栏、Diff 局部加载、搜索超时、Git 结果页仍未闭环；
- 未提交、未推送。

## 4. 清理

- 无残留 Augit/PyCharm；
- 未新建临时夹具；`cleanup-check-196.json`。
