# 图标几何对齐审计

审计日期：2026-10-02。权威仓库为 `D:\github\intellij-community` 的 `platform/icons/src/expui/**` SVG 资源；本次引用的仓库基线 commit `576e32820af82f97529d70f9269726803a27c016` 的提交主题是 `RSRP-504795 rider fus: add more extensions and filetypes to be reported`，并非图标提交，因此这里不把它表述为“New UI 图标提交”。DSH 会话 `augit-main` 不存在；按任务要求新建独立 headless 会话 `session-50333eb1-aaa1-42fa-b891-4c565bf73e03` 完成只读核对，未修改仓库。

## 本批次已修复

`web/src/mockup.js` 与 `docs/ux-mockups/mockup.js` 同步修改，资源字节一致：

- JSON：由 8×8 描边折线改为 `fileTypes/json.svg` 的 12×12 填充花括号。
- YAML：由 JSON 折线改为 `fileTypes/yaml.svg` 的折角文档与 Y 字形几何。
- Markdown：按 `fileTypes/markdown.svg` 的填充 M 与下载箭头路径校准。
- 项目与文件夹：按 `toolwindows/project.svg`、`nodes/folder.svg` 的折角、圆角和外框范围校准。
- 搜索与搜索历史：普通搜索改为 `general/search.svg` 的 4.5 半径圆和精确手柄；搜索历史补齐填充圆盘、手柄与右上三角。
- 复制：按 `general/copy.svg` 修正前页圆角、三条横线和后页完整轮廓。
- 终端：按 `fileTypes/shell.svg` 修正圆角外框、提示符和下划线几何。

## 已核对无需修改

- 图片缩小、放大：`image/zoomOut.svg`、`image/zoomIn.svg` 的圆心、半径与加减号范围已一致。
- 图片适应区域：`image/fitContent.svg` 的外框和四条内角线已一致。
- 工作区根目录角标：权威图标没有对应元素，保留 Augit 自有角标，归有意产品差异。

## 限制与归类

本轮补充归类：

- 工具窗口 rail 的圆角：当前实现为 3px；`SquareStripeButtonLook` 的 `Button.ToolWindow.arc` 权威值对应 6px 半径，而设计系统另有 3px/7px 历史表述，归类为“规范未定义或冲突”，暂不自行改动。
- 工具窗口普通按钮与工具栏按钮统一使用 28px 命中区，工具轨按钮保持 32×32px；工具轨同组按钮中心按 32px 步长排列，不额外叠加外边距。标题栏/工具栏图标仍按 16px 绘图网格实现，归类为组件路径差异。
- Tree/List 的图标文字间距在权威通用组件中有 2px 与项目树专用 8px 两种来源；项目树按 `ClassicPainter` 与设计系统的 8px 文件图标间距实现，其他列表保留各自组件规则，归类为“有意组件差异”。
- 暂存后修改（例如 `MM`、`AM`）的双状态字段已由宿主保留并进入快照签名，但产品没有规定第二个可见标记，归类为“规范未定义或冲突”，当前不增加猜测性 UI。
- 没有 Windows 10 22H2 实机和同环境 PyCharm 前台取证；对应项目标记为“无法取证”，仅以 IntelliJ 源码、无头行为和 Windows 11 环境结果作为证据。

YAML 权威 SVG 使用纸张、折角和 Y 三种填充色；本次授权只允许修改 `mockup.js` 图标块，运行时 SVG 通过 `currentColor` 继承既有 `file-type-yaml` 主题令牌，因此保留了几何但未引入新的颜色令牌。该颜色层差异记录为无法在本批次仅改图标块完整复现，依据是 `fileTypes/yaml.svg` 与 `yaml_dark.svg` 的三色填充；后续若要完全对齐需由主 agent 统一 CSS 令牌后再处理。

`history-search` 的所有当前调用点仍渲染普通 `icon("search")`，本批次只校准登记的 `history-search` 图标块，未改变调用点或交互行为。

## 验证

- `node --check web/src/mockup.js`：通过。
- `node --check docs/ux-mockups/mockup.js`：通过。
- `powershell -NoProfile -File tools/audit/verify-ui-assets.ps1`：通过，运行时与视觉稿资源一致。
- `git diff --check -- web/src/mockup.js docs/ux-mockups/mockup.js`：通过。

未启动服务器、浏览器或临时文件；独立 DSH 会话已结束，未提交改动。
