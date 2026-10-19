# 主窗 / 普通文本首个同态配对（2026-10-18）

## 1. 结论

首个 `main-project` / `text-viewer` **同态参考配对已完成**，口径和测量可复验。第 2 批已修复标签文本图标、标签图标框和关闭叉并重采复验；工具轨起点与行号槽经核对为参考端主工具栏/折叠区结构差异（§10）。因此**同态配对完成 1**，但仍**不记作“像素合格通过”**，像素合格计数仍为 0。

- 参考：PyCharm **2026.2.1 / 262.9437.214**，隔离 reference 配置，fixture 工作区。
- 实际：`tools/start-augit.ps1` 强制非增量构建的 Augit，正常入口打开同一 fixture。
- 两端：2880×1716、DPI 168、浅色、界面 Microsoft YaHei UI 12、正文 JetBrains Mono 13、Project 面板约 360 CSS、单标签 `notes.txt`、Project 树选中 `notes.txt`、无底部工具窗、无宽右侧面板。
- 原始图：`artifacts/pixel-followup-2026-10-18/homologous-main-text/reference-main-text.png`、`actual-main-text.png`。
- 缩放总览：`pair-overview.png`（仅用于总览，逐像素测量始终用原始 PNG）。
- 标签修复前后对照：`tab-compare-after.png`（上参考、下实际，4× 放大）。
- 环境、哈希、状态与复验命令：`pair-environment.json`。

该配对只覆盖 `main-project` / `text-viewer` 的这一种状态，不等于 54 页或全部状态完成。

## 2. 先纠正参考版本

第一次采集误用了用户安装的 PyCharm 2026.2.3（`D:/JetBrains/PyCharm 2026.2.1` 目录名与实际版本不符，product-info 为 2026.2.3 / 262.10968.92），不能作为 2026.2.1 参考。

- 该实例 PID 13688 已关闭；两张图保留为 `*-2026.2.3-invalid.png`，不进入配对。
- 2026.2.3 曾把共享 reference 配置的迁移标记写成 `PY-262.10968.92`，已恢复为 `PY-262.9437.214`。
- 正确的 2026.2.1 实例使用 `unpacked/bin/pycharm64.exe`、`reference-2026.2.1.properties`、独立 `system-2026.2.1` / `log-2026.2.1`，PID 6852。
- 日志身份：`IDE: PyCharm (build #PY-262.9437.214, Tue, 11 Aug 2026 15:48:00 GMT)`。

用户原有 PyCharm PID 20336 在本会话首次操作前已于 18:56:24–18:56:27 正常退出（日志 `IDE SHUTDOWN`），本会话没有向它发送输入，也没有关闭或重启它。

## 3. 参考端操作（真实键盘/鼠标）

1. `Win+Up` 最大化；窗口帧与客户区均为 `[0,0,2880,1716]`，DPI 168。
2. `Ctrl+Shift+F12` 收起所有工具窗；`Alt+1` 只打开 Project。
3. 关闭 `README.md` 标签，只留 `notes.txt`。
4. 拖动 Project/编辑器分隔条，使 Project 面板约 360 CSS；面板右缘物理 x=704–716。
5. 单击 `notes.txt` 行使 Project 树获得焦点（与 Augit 端一致）。
6. `Refresh VCS Changes`；fixture 的 `app.py` 修改在参考端以文件名颜色表达，没有 `M` 徽标。
7. 关闭 2026.2.1 首次启动的 Defender 与中文语言通知。

右侧只有 42 CSS 的工具窗口条，没有宽面板。Find Action 搜索 `sidebar` 无结果；`View | Appearance | Tool Window Bars` 会同时隐藏左侧工具轨，不能只隐藏右侧条。因此保留右条并登记为参考端结构差异，没有为凑图修改产品。

## 4. 实际端状态与构建身份

- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1 -Workspace D:/JetBrains/PyCharm-2026.2.1-reference/fixture`
- PID 24612，`Augit.exe` SHA256 `DA0C4AFD…DF96C`，`Augit.dll` SHA256 `F53764A8…B4E00`。
- `ProductVersion=0.1.0+690eb1937b4b65600dbbd9d3e4bc91b8f93a61dd`。
- `live-data.js` SHA256 `B4719F2F…7B45B`；第 2 批标签修复后 `mockup.css` `20EFA054…12E7`、`mockup.js` `78E9EAA1…1758`（修复前分别为 `C45AA643…09C2E`、`6D2068EB…F3940`）。
- 随包 JetBrains Mono Regular SHA256 `A9CB1CD8…F45F2`。

构建哈希订正：状态表原记录的 `159123F2…8559D` 是 HEAD `d43e761` 的构建；当前 HEAD `690eb19` 只增加文档提交，`src/Augit.Shell` 在 `d43e761..HEAD` 无 diff，但 .NET 的 `ProductVersion` 内嵌当前提交号，因此同一 Shell 源码重建得到 `F53764A8…B4E00`。旧哈希只能定位历史证据，不能用于核对当前实例。

## 5. 测量

### 5.1 共同结构（物理像素 / CSS）

| 项目 | 参考 | 实际 | 说明 |
| --- | --- | --- | --- |
| 标题/工具栏行下沿 | 76–79 | 77–79 | 一致；顶栏内容属产品差异 |
| 标签条下沿 | 145–149 | 145–149 | 一致 |
| 文档工具栏 | 无 | 149–212（63 / 36 CSS） | Augit 产品要求，正文整体 +63 |
| Project 面板右缘 | 704–716 | 700–711 | 面板约 360 CSS；物理差约 3 |
| 正文起点 | x=829 | x=802 | 实际相对参考 x−27、y+63 |
| 行号右缘 | x=739–747 | x=782–792 | 行号列宽差约 14 CSS |
| 行号/正文竖线 | x=822–823 有 | 无 | 参考行号槽右侧细线 |
| 右侧工具窗条 | 2812–2880（42 CSS） | 无 | 参考端独有 |
| 状态栏上沿 | 1664–1670 | 1668–1670 | 基本一致 |

### 5.2 正文墨迹

| 行 | 参考外接框（x0..x1, y0..y1） | 实际外接框 | 宽度 |
| --- | --- | --- | --- |
| 1 | 829..1006, 158..178 | 802..979, 221..241 | 177 / 177 |
| 2 | 830..963, 196..213 | 802..936, 259..276 | 133 / 134 |
| 3 | 829..977, 234..251 | 802..950, 297..314 | 148 / 148 |

- 行距：两端均为 38 / 38 物理像素（≈21.7 CSS），与 13px JetBrains Mono + 1.2 行距的既有测量一致。
- 实际相对参考平移 `(+27, −63)` 后，三行墨迹外接框逐行重合（第 2 行宽差 1 物理像素）。
- 紧框比较：≥16 差异约占 17.8%–21.0%，但 **100%** 落在参考端深色墨迹的 1 像素邻域内；1px 高斯模糊后降到 7.3%–8.6%。两端墨迹都带子像素彩边（63%–75% 的墨迹像素有 ≥10 色度），因此残差属于不同栅格器的边缘抗锯齿，不是行高、字宽或起点偏移。
- 不把该局部结果外推为整页“还原度”。

### 5.3 全图差异（含结构差异，不作还原度）

- 全图 4,942,080 像素：完全相等 91.18%，最大通道差 ≤16 占 94.73%，≥16 占 5.32%，平均绝对差 3.03。
- 该比例包含标题栏、右侧工具窗条、文档工具栏、`.idea` 行、行号槽细线、当前行底色等结构/状态差异，不能写成“产品还原度 94.73%”。

## 6. 已识别差异

| 差异 | 参考 | 实际 | 性质 |
| --- | --- | --- | --- |
| 文本文件图标 | 四横线（长/短交替） | 四横线（text.svg） | 已修复（§10）；栅格 128×128 与权威 SVG 零差异 |
| 标签关闭叉 | 约 8 CSS，浅灰（最暗 #A8ADBD） | 14×14 物理、#A8ADBD | 已修复（§10）；位置 +2/+1 物理像素 |
| 标签图标框 | 16 CSS | 16 CSS | 已修复（§10）；图标 x=741..760 两端一致 |
| 工具轨首个按钮 | 蓝色底 y=80–132，中心 106.5 | y=91–143，中心 117 | 实测下移 10.5 物理像素（约 6 CSS），需复核设计系统内距后再改 |
| 行号槽竖线 | 有 1px 细线 | 无 | 设计系统目前写“同底、右侧留 3px”，无此线；先登记 |
| `.idea` 行 | PyCharm 隐藏点目录 | 显示 | 产品行为差异 |
| VCS 修改标记 | 文件名着色，无 M 徽标 | `M` 徽标 | 产品差异；Augit 不以颜色单独表达状态 |
| 当前行/插入符 | 第 4 行浅蓝当前行 | 无 | 参考编辑器状态；只读正文不适用 |
| 标题栏/右条/文档工具栏 | 参考端结构 | Augit 产品结构 | 结构性差异，已登记 |

## 7. 下一步

1. 先修标签组件：`.txt` 四横线图标、标签图标 13→16 CSS、关闭叉大小/颜色，并补自动化图标对照（`tools/audit/live-icons.cjs` 当前没有 Text 条目）和标签几何检查。
2. 复核工具轨首个按钮的 top 内距，决定是否调整 `mockup.css` 与 `design-system.md`；不要只凭一张图改公共框架。
3. 行号槽细线需规范裁决；若加，只改共享 CSS 并更新设计系统，不引入额外结构。
4. 修后重采同一配对，再按模块扩展到设置、终端等保留页面。

## 8. 复验

```powershell
python artifacts/pixel-followup-2026-10-18/homologous-main-text/compare-main-text.py `
  --ref artifacts/pixel-followup-2026-10-18/homologous-main-text/reference-main-text.png `
  --act artifacts/pixel-followup-2026-10-18/homologous-main-text/actual-main-text.png `
  --out artifacts/pixel-followup-2026-10-18/homologous-main-text/comparison-main-text.json
python artifacts/pixel-followup-2026-10-18/homologous-main-text/align-text.py
python artifacts/pixel-followup-2026-10-18/homologous-main-text/aa-analysis.py
```

边界复验：

```powershell
python artifacts/pixel-followup-2026-10-18/homologous-main-text/measure-edges.py `
  artifacts/pixel-followup-2026-10-18/homologous-main-text/reference-main-text.png `
  --y0 300 --y1 1600 --fraction 0.6
```

## 9. 清理

- Augit PID 24612：`CloseMainWindow()` 后立即退出；`settings.json` 从 `settings-before-pair.json` 恢复，恢复后 SHA256 `B8868855A535D49EFD0D89FC98E78359DB8BD0AAAD3D1A1FA887A6740BE8DC3D`。
- 参考 PyCharm PID 6852：`CloseMainWindow()` → Confirm Exit → 约 2s 退出；删除 `system-2026.2.1/.pid` 中已死的 6852 记录。
- 误用实例 PID 13688：已于 19:14 通过 Confirm Exit 关闭。
- 用户 PyCharm PID 20336：本会话未操作，18:56 已自行退出。
- 本批未启用 CDP 端口；残留检查无 `Augit`、`pycharm64`、Augit 专属 `msedgewebview2`，批次 PID 无监听端口。
- `cleanup-check-32.json` 记录进程、配置恢复、无效证据保留与未提交/未推送状态；`.tmp/` 未删除。
- 第 2 批：Augit PID 14020 正常关闭；`settings.json` 从 `settings-before-pair-2.json` 恢复，SHA256 仍为 `B8868855…DC3D`。离线图标、字号和 `.txt` 图标栅格测试的 Chromium 均已在 `finally` 中关闭；无 Playwright Chromium、WebView2、Augit/PyCharm 或端口残留。`cleanup-check-33.json`。

## 10. 第 2 批：标签图标与关闭叉修复（2026-10-18 19:30）

### 修改

1. `.txt` 文件类型改用权威 `expui/fileTypes/text.svg` 的四条 1 单位实心横线（y=3/6/9/12，长 12、短 8，`rx=0.5`），不再使用文档轮廓。
2. 编辑器标签文件图标由 13px 改为 **16px** 绘图盒。参考端 `text.svg` 的 12 单位长线在 16px 下为 21 物理像素；13px 只有约 17 物理像素，与参考不符。CSS 注释同步订正，`--augit-compact-icon-size` 13px 只保留给标题栏/工具窗动作。
3. 标签关闭叉改用权威 `expui/general/closeSmall.svg` 填充路径；命中区 20×20、绘图盒 16px、颜色 `#A8ADBD`、悬停 `#6C707E`。令牌 `--augit-tab-close` 同时登记浅/深色。
4. 测试：`live-icons.cjs` 增加 `text.svg` + `notes.txt` 对照项；`verify-ux-offline-icons.cjs` 增加 16px 标签图标、20×20 关闭区、`close-small` 图标和 `#A8ADBD` 颜色断言，并订正 20×20 工具轨网格/图标、7 个查找条图标和历史比较关闭叉等过期期望。

### 复采与测量

- 实际端重新由正常入口构建启动（PID 14020，构建 0 警告 0 错误），同一 fixture / 2880×1716 / DPI168 / 浅色 / 树选中 `notes.txt`。
- 实际截图 SHA256 `d095f273…bea22c7`；修复前实际图保留为 `actual-main-text-round1.png`（`11c6909d…f33545fa9`）。
- 标签文件图标：参考 x=741..760，实际 x=741..760；四条线起点 y=101/106/111/117 对 102/107/112/118（+1 物理像素）。
- 标签正文：参考 x=776..860，实际 x=774..858；宽度同为 84 物理像素，起点差 2 物理像素（约 1.1 CSS）。
- 关闭叉：参考 14×14 物理、最暗 `#A8ADBD`；实际 14×14、最暗 `#A8ADBD`，位置 +2/+1 物理像素。
- 卡片右缘：参考 902，实际 907（差 5 物理像素，约 2.9 CSS）；修复前为 916。
- 全图：完全相等 91.1912%、最大通道差 ≥16 占 5.3128%、平均绝对差 3.0204；修复前为 91.1795% / 5.3240% / 3.0293。提升幅度小是因为标签区域只占全图约 0.1%，该数字仍不能当还原度。
- 修正后标签对照见 `tab-compare-after.png`。

### 验证

- `tools/audit/verify-ui-assets.ps1`：PASS，视觉稿与运行时 `mockup.js/css` 字节一致。
- `tools/verify-ux-offline-icons.cjs`：**PASS**，110 个场景/主题、12 个尺寸组合、2 个动态流程，远端请求 0。
- `tools/verify-ux-typography.cjs`：**PASS**，20 个场景。
- `verify-text-icon.cjs`：`.txt` 图标与权威 `text.svg` 在 128×128 栅格下 **0 像素差**、最大通道差 0。
- `node --check`：`mockup.js`、`live-icons.cjs`、`verify-ux-offline-icons.cjs` 均通过。
- `verify-script-encoding.ps1`：PASS，26 个脚本。
- `git diff --check`：通过。

### 仍未闭环

- 工具轨首个按钮相对参考低约 10.5 物理像素（约 6 CSS）。已核对 IntelliJ 源码：`StripeToolbar.Button.size` 40×40、图标 20、icon padding 5 ⇒ 30×30 背景；参考端按钮位于 40 CSS 主工具栏内，标签条/编辑区从 44 CSS 开始，Augit 的单一 44 CSS 标题栏把工具轨整体放到 44 CSS 之后。因此这是标题栏/工具栏结构差异，不是 rail padding 错误；不改公共框架。
- 参考行号槽约 67 CSS 含 IntelliJ 折叠/图标区；Augit 只读正文没有折叠，设计系统按最长行号 + 28px 得到约 53 CSS。该差异为编辑器能力/结构差异，登记。
- `.idea` 行、`M` 徽标、第 4 行当前行底色、文档工具栏 36 CSS、右侧工具窗条 42 CSS：产品/状态/参考端结构差异，均已登记。
- 结论：`main-project` / `text-viewer` 的**同态配对完成 1**（内容、主题、DPI、面板宽度、焦点/选中项一致，差异已登记）；**像素合格整页配对仍记 0**（保守口径，不把结构差异当像素一致）。下一组页面为设置/终端。
