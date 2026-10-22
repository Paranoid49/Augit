# 第四组整窗配对：面板几何状态对齐（2026-10-22）

前三组整窗配对只对齐了视图、文件、标签与树/提交选择，**没有对齐用户可保存的面板尺寸**：参考的 Project 面板与底部 Git 高度来自参考工程保存的布局，Augit 使用默认 360px / 31%。本组先把两侧面板几何对齐，再重新采集；这是首次面板几何同态的整窗配对。

## 1. 状态与身份

| 项目 | 值 |
| --- | --- |
| 参考图 | `artifacts/pixel-pair-3-2026-10-22/pycharm-root-commit-selected.png`（2880×1716，sha256 `FEBAA04B…DC04BE`） |
| Augit 图 | `artifacts/pixel-pair-4-2026-10-22/augit-side362.png`（2880×1716，sha256 `30389E4C…D8A0`） |
| Augit | PID 24516，正常关闭；ProductVersion `0.1.0+4b193af…` |
| `Augit.exe` / `Augit.dll` | `E4E7068C…DD0B16` / `B7B57DBA…249E1B` |
| `mockup.js` / `mockup.css` | `F478F7BC…16490E` / `755CEFD2…A78513` |
| 隔离设置 | `D:/github/Augit/.tmp/settings-pixel-pair-3-2026-10-22.json` |
| 布局状态 | `toolWindows.workspaceRoot` 指向参考夹具；`projectPanelWidth=362`、`bottomPanelHeight=321` |
| 共同状态 | Project Files、README preview-only、单标签、`.idea` 折叠、根行选中、唯一提交选中并显示 3 files + 详情 |

## 2. 面板几何对齐

| 边界 | 参考物理坐标 | 前三组 Augit | 本组 Augit |
| --- | ---: | ---: | ---: |
| 项目树/编辑区分隔 | 703 | 699 | **703** |
| 底部 Git 顶边 | 1097 | 1127 | **1097** |
| Git 引用面板右缘 | 940 | 635→939 | **939** |
| Git 详情面板左缘 | 2249 | 2184→2249 | **2249** |

参考的布局在 workspace 状态中保存为 Project weight `0.2225258`、Version Control weight `0.33044806`。Augit 的默认值仍是 `--augit-side-width` 360 与 `--augit-bottom-height` 31%，本组**没有改产品默认 token**，只在隔离设置里显式给出与参考同态的 `projectPanelWidth=362`、`bottomPanelHeight=321`。这样比较的是共享布局，而不是用默认值差异充当产品差异。

设置的 `toolWindows.workspaceRoot` 必须与启动工作区一致，否则 Augit 会按设计忽略不属于当前工作区的面板尺寸（前三组的设置文件仍指向 `D:\github\Augit`，因此 288/空宽度未生效）。本组是首次让该字段指向参考夹具。

## 3. 整窗差异

零容差、不裁剪、不平移、不配准、不扩掩码：

| 项目 | 差异像素 | 占比 |
| --- | ---: | ---: |
| 整窗 | **434119 / 4942080** | **8.7841%** |
| 标题栏 0–76 | 107593 / 221760 | 48.52% |
| 标签条 77–146 | 19947 / 201600 | 9.89% |
| 主区 147–1096 | 121498 / 2736000 | 4.44% |
| — 项目树 | 47992 / 667850 | 7.19% |
| — 编辑区 | 73506 / 2068150 | 3.55% |
| 底部 Git 1097–1687 | 180192 / 1702080 | 10.59% |
| 状态栏 1688–1715 | 4889 / 80640 | 6.06% |

对比：首组 15.4095%，第二组 11.0175%，第三组 10.3752%，本组 **8.7841%**。第三组与本组之间只改变了面板几何状态，差异减少 8.2 万像素，说明前三组的大块底部/编辑区差异主要不是产品绘制差异，而是**面板尺寸未同态**。

## 4. 剩余差异

1. **标题栏**：参考的 PyCharm 项目徽标、`notes.txt` 项目名（`.idea/.name`）、绿色项目渐变、Run 配置选择器、trial 与右侧工具轨；Augit 的蓝色渐变、`fixture` 工作区名与窗口按钮。大部分属参考独有或项目元数据差异，不能算共享绘制通过。
2. **标签条**：参考 Project Files 标题与六枚头部图标，Augit 只有简化 Project 标题与四枚图标；编辑器标签本身几何已接近，仍有文字栅格差。
3. **底部 Git**：列宽与面板高度已同态；剩余是参考的 Console 标签、过滤/查看图标、提交行与详情区的字体/内距，以及 Augit 没有的搜索选项。
4. **项目树/编辑区**：树文字行高取整与字形栅格；编辑区 h1/正文/代码的字形栅格与代码字族回退宽度。
5. **状态栏**：参考根行聚焦时只显示 `fixture`，Augit 仍显示 `fixture › README.md`；字段集合也不同。
6. 整窗仍未通过，像素合格仍 **0**，页面级同态配对仍 **50**。

## 5. 结论与清理

- 本组首次把用户可保存的项目面板宽度与底部高度纳入同态状态，整窗原始差异 **8.7841%**；仍未通过。
- 采集前已运行 `node tools/audit/check-pixel-pair-settings.cjs <settings> <workspace>`：本组通过（362/321、workspaceRoot 指向参考夹具）；旧设置文件被正确拒绝。
- 自动化：`live-shell` 全量 **2340/2340**、`mockup-scenes` **54/54 light + 54/54 dark**、`verify-ui-assets`、`check-doc-claims`、`git diff --check` 通过（本轮无产品代码改动，上述验证沿用当前 HEAD 已通过的构建）。
- 参考配置恢复 README 单标签、`.idea` 折叠、根行选中；未修改参考项目文件；参考 PyCharm 已正常退出。
- Augit PID 24516 正常关闭；无残留本实例 WebView2；全局 `settings.json` SHA256 仍为 `8840E775…462CF`。
- 隔离设置与原始截图留在 `.tmp/`、`artifacts/`（Git 忽略）。
