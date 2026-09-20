# Augit 界面与交互核销清单（交付文档）

本文件把 `docs/ux-spec.md` 与 `docs/design-system.md` 的条文逐条映射到「实现 + 证据」，
同时覆盖两条主线：

- **A 线：静态界面复原** —— 同引擎（同一个 WebView2）像素对照，指标为边缘掩膜后的 `layoutPercent`；
- **B 线：操作逻辑复原** —— 「操作 → 状态转换」的可执行用例，用 CDP 真实鼠标/键盘事件驱动。

口径要求：

1. 每条都给出**可复跑的**证据位置（命令、断言名或文件行号），不写"应该没问题"；
2. 差异与未覆盖项在 §3 逐条列出，包含**根因与证据**，不含推测；
3. 任何指标都先用可核对的样本标定（裁图肉眼复核或对照测量）再作为结论；
4. 数据差异（真实工作区数据 vs 视觉稿样例数据）与平台差异（滚动条、字体可用性）
   **不算实现缺陷**，但必须在判读里写明理由。

**交付清单索引**（本文件即交付物，各部分对应任务书的编号）：

| 内容 | 位置 |
| --- | --- |
| 验证基线（分母 + 复跑命令） | §0 |
| A 线：同引擎像素对照 55 场景 | §1.1 |
| A 线：`ux-spec` §4 的令牌/视觉条文核销 | §1.2 |
| C 线：与 PyCharm 的对照（配方 + 已测面） | §1.3 |
| **⑪ 逐页覆盖表（页面 × 检查层级 × 判读）** | §1.4 |
| ⑨ 条文穷举的**分母**（用例行 / 规格条文） | §2.0（数字由 `tools/audit/check-doc-claims.cjs` 机械核对：分节之和 = 合计） |
| B 线：条文 → 用例 → 证据 | §2.1–§2.9 |
| ⑩ 未执行/环境不可达项 | §3.1 |
| ⑪ 差异清单（规范/实现/基线差异） | §3.2–§3.3 |

## 0. 验证基线与复跑命令

| 检查 | 命令（在本仓库根目录执行） | 当前结果 |
| --- | --- | --- |
| 共享界面资源字节一致 | `powershell -File tools/audit/verify-ui-assets.ps1` | **PASS**（`mockup.js` / `mockup.css` / `current-find.js` / `image-preview.js` 四对） |
| 审计脚本编码规则 | `powershell -File tools/audit/verify-script-encoding.ps1` | **PASS**（12 个 `.ps1` 全部 BOM-less 且 ASCII-only） |
| 实时外壳（真实数据路径） | `node tools/audit/live-shell.spec.cjs <playwright>` | **1039/1039（未执行 0 项）**（见 §2 说明；摘要格式为"通过 N 项断言（另有 M 项因环境未执行，不计入通过）"） |
| 视觉稿场景渲染 | `node tools/audit/mockup-scenes.spec.cjs <playwright> dark\|light` | **55/55 ×2 主题**（`docs/ux-mockups/*.html` 共 56 个，除 `index.html` 外全部渲染） |
| 同引擎像素对照 | `powershell -File tools/audit/compare-pixels.ps1 …` + `python3 tools/audit/compare-pixels.py …` | 见 §1.1（**55/55 场景**，新增 `settings-save-failure`、`image-error`、`git-compare-empty`、`settings-dirty`、`stash-drop-confirm`） |
| 真机窗口 chrome | `powershell -File tools/audit/verify-window-chrome.ps1` | **11/11** |
| C# 外壳单元测试 | `dotnet test tests/Augit.Shell.Tests` | **74/74**（含终端缓冲裁剪 4 条 + 裁剪摊销阈值 1 条） |
| 真机全场景巡检 | `powershell -File tools/audit/verify-acceptance.ps1 -Exe <exe> -OutDir <dir> -Workspace <dir>` | **54/54 PASS（2026-09-21 复跑，终端/JSON/Markdown 等改动之后）**：默认列表 42 → **54**——先补入 7 个状态页（`git-history-empty`、`diff-status`、`settings-save-failure`、`image-error`、`git-compare-empty`、`settings-dirty`、`stash-drop-confirm`），第 345 轮又发现 `git-history-graph`、`go-to-line`、`history-diff-cancelled/failure/loading` 这 **5 条只在像素表里、没进巡检列表**并补入；`SUMMARY total=54 passed=54 failed=0`、`ACCEPTANCE_OK`；截图在 `artifacts/acceptance-20260920b/`。**唯一未进列表的是 `diff-boundary`**：它需要"工作区里有一个被改动的文件"（`--diff <path>`），而巡检跑在干净的仓库上，实测 `PAGE_CHECK_FAILED no CDP page target`（原因写在脚本注释里，不写成通过） |；**`diff-boundary` 在"含改动的工作区"上单独验收 OK（第 371 轮）**
| 交付文档数字自洽 | `node tools/audit/check-doc-claims.cjs` | **DOC_CLAIMS_OK**（§2.0 分节之和 = 合计、当前事实加数与缺口自洽、§2.8 行数/状态与 §2.0 一致、§1.1 像素行数 = §1.4 A 线分母；需跑套件才能得的数字只打印并标注"未机械核对"） |
| 交互基线一致性 | `node tools/audit/check-interactions.cjs`（并 `node tools/audit/gen-interaction-baseline.cjs` 生成人类视图） | **INTERACTIONS_BASELINE_OK**：`surfaces=6 jumps=4 feedback=8 sequences=6 gaps=4`（`CHECKED 18`：pass 9 / diff 2 / gap 1 / other 6），逐条可核对（`pass` 必须有真实断言名、`gap` 必须写明交付文档出处、`diff` 必须写原因）；检查器曾抓出 1 处断言名过期、2 处差异未写进文档 |
| 打包 | `powershell -File tools/release.ps1` | **通过（2026-09-21 复跑）**：`Augit-0.1.0-win-x64-portable.zip` **2,795,619 B（2026-09-21 重打包）**、`Augit-0.1.0-win-x64-setup.exe` **4,386,825 B（2026-09-21 复跑）**、`SHA256SUMS.txt` 195 B 且两项 `sha256sum -c` 均 **OK**；包内 **32 个条目**，抽查含 `Augit\web\index.html`、`src/{live-data,mockup,current-find,image-preview}.js`、`mockup.css`、`vendor/xterm/xterm.js`、5 个第三方许可证文件；包内 `live-data.js` 含 `__augitResetRequest`/`write/cancel`/`terminal/status`，`Augit.dll` 含 `git/reset`/`git/detect`/`terminal/status`/`git/operation`/`git/worktree-removal`（**按 UTF-16LE 匹配**：.NET 字符串字面量不是 UTF-8 字节，按 ASCII 搜会得到假阴性） |

> 说明：`live-shell.spec.cjs` 在无头 Chromium 里用 `addInitScript` 模拟 WebView2 宿主，
> 因此**不需要启动 Windows 应用**就能覆盖桥接、目录展开、文档、Changes、历史、Blame、
> 终端、设置、冲突、写操作状态机、Reset 与竞态。它有 745 个 `check(...)` 调用点（另有 `csCheck` 包裹的 2 条会话断言，不计入该调用点数），
> 其中一部分在场景/主题循环里重复执行，因此实际断言数（942）大于调用点数。

## 1. A 线：静态界面复原

### 1.1 同引擎像素对照（链路 + 结果）

链路（`tools/audit/compare-pixels.ps1`）：

1. 同一台 Windows、同一个 WebView2 里渲染两侧：视觉稿用 `--web-root docs/ux-mockups`
   启动后由 CDP 导航到 `<scene>.html?theme=dark`，实时侧用 `--scene <scene>`（必要时加
   `--open/--diff/--blame/--file-history`）启动；
2. 两侧同为 1180×760、`--pixel-exact`、dark；
3. 取图一律用 CDP `Page.captureScreenshot`（`PrintWindow` 会裁掉底部约 22px，见第 279 轮）；
4. `tools/audit/compare-pixels.py` 做**边缘掩膜**：两侧都"平坦"（局部梯度小）的像素才算布局像素，
   统计 `layoutPercent`（平坦像素里差异 > 8 的占比）与 `textPercent`（边缘像素差异占比）。

**不要用 `PrintWindow`**；**不要把 `visiblePercent` 当作缺陷判据**（它被真实数据文字差异淹没）。

| 场景 | titlebar | statusbar | content | 判读 |

> **偏高项复核示例（第 389 轮，④）：`repository-search`（当时 55 行里 content 最高 = 2.84）**
> 用同一对照器重跑（`tools/audit/compare-pixels.ps1` → `compare-pixels.py`），并把证据留在仓库：
> `artifacts/pixel-review-20260921/repository-search/`（`*-mockup.png`、`*-live.png`、`heatmap.png`）。
> - **重跑读数**：titlebar `layoutPercent=0.00`、statusbar `0.00`、content **`2.08`**
>   （`textPercent=17.31`、`visiblePercent=6.90`）；**与 §1.1 记录的 2.84 不同**（Δ0.76）。
> - **为什么不同**：`layoutPercent` 统计的是"两侧都平坦的像素里差异 > 8 的占比"，而 live 侧的
>   真实数据（文件树内容、结果行、选中项）**随工作区状态变化** —— 记录值来自 2026-09-19/20 那次运行，
>   重跑时工作区已不同。**因此 §1.1 的行值应读作"那一次运行的读数"，不是可永久复现的常数**；
>   判读（"数据差异"而不是"布局错位"）在两次运行里一致。
> - **heatmap 复核结论**：黑底上绝大部分标记是**字形级**（文字栅格化差异，预期内）；
>   只有**两条通栏色带**（结果面板的首行带与末行带）是平坦像素差异 —— 即 `layoutPercent` 的来源。
>   也就是说这一格是"文字差异 + 结果面板两处条带"，不是整页错位。
> - **工具口径提醒**：`compare-pixels.ps1` 自己打印的 `SUMMARY failures=…` / `PIXELS_DIFFER` 用的是
>   `visiblePercent`（§1.1 明确写了"不要把 visiblePercent 当作缺陷判据"），本次它就报了
>   `failures=2`，而三个带的 `layoutPercent` 是 0.00/0.00/2.08。**读判据请读 `layoutPercent` 行**，
>   那个 SUMMARY 只反映可见像素差异（被真实数据文字差异淹没）。
| --- | --- | --- | --- | --- |
| `blame` | 0.00 | 0.00 | 1.44 | 数据差异（字体已等化） |
| `branches` | 0.00 | 0.00 | 1.39 | 数据差异；**等化前 2.81**（差值主要为字体） |
| `changes-context-menu` | 0.00 | 0.00 | 1.25 | 数据差异；**等化前 3.80**（差值主要为字体） |
| `clone` | 0.00 | 0.00 | 0.09 | 数据差异（字体已等化） |
| `commit-changes` | 0.00 | 0.00 | 0.26 | 数据差异（字体已等化） |
| `commit-diff` | 0.00 | 0.00 | 1.59 | 数据差异；**等化前 3.71**（差值主要为字体） |
| `commit-empty` | 0.00 | 0.00 | 0.00 | 空态骨架，两侧逐像素一致（0.00） |
| `conflict-list` | 0.00 | 0.00 | 0.01 | 数据差异（字体已等化） |
| `conflict-resolver` | 0.00 | 0.00 | 0.00 | **等化后 0.00**（等化前 0.60） |
| `diff-boundary` | 0.00 | 0.00 | 1.60 | 数据差异；**等化前 4.09**（差值主要为字体） |
| `diff-status` | 0.00 | 0.00 | 1.29 | **新增最终说明页**（⑭ 第 2 项）：二进制/超限/无差异/失败共用同一说明块；实时侧用真·二进制改动仓库实测（`status=Binary`） |
| `diff-loading` | 0.00 | 0.00 | 1.47 | 数据差异（字体已等化） |
| `file-history` | 0.00 | 0.00 | 0.63 | 数据差异（字体已等化） |
| `file-limit` | 0.00 | 0.00 | 0.01 | 两侧都是"不可预览"页（数据不同） |
| `git-compare` | 0.00 | 0.00 | 1.71 | 数据差异；**等化前 4.08**（差值主要为字体） |
| `git-compare-empty` | 0.00 | 0.00 | 1.41 | **新增引用比较无差异页**（⑭ 第 4 项）：文件栏保留双方引用、变更导航禁用但保留入口、正文只留比较语义的摘要说明；实时侧同一条规则由 harness 正/负向两条断言覆盖（见第 316 轮） |
| `git-history` | 0.00 | 0.00 | 1.37 | 数据差异；**等化前 1.73**（差值主要为字体） |
| `git-history-empty` | 0.00 | 0.00 | 0.64 | **新增空态页**（⑭ 第 1 项）：实时侧用空仓库实测，提交区显示"仓库还没有提交"，引用树/筛选栏保留 |
| `git-history-graph` | 0.00 | 0.00 | 1.40 | 数据差异（字体已等化） |
| `git-history-menu` | 0.00 | 0.00 | 1.06 | 数据差异（字体已等化） |
| `git-unavailable` | 0.00 | 0.00 | 0.51 | 数据差异（字体已等化） |
| `go-to-line` | 0.00 | 0.00 | 0.58 | 数据差异（字体已等化） |
| `history-diff-cancelled` | 0.00 | 0.00 | 1.38 | 数据差异（字体已等化） |
| `history-diff-failure` | 0.00 | 0.00 | 1.39 | 数据差异（字体已等化） |
| `history-diff-loading` | 0.00 | 0.00 | 1.61 | 数据差异（字体已等化） |
| `image-preview` | 0.00 | 0.00 | 0.01 | 两侧是同一张真实 PNG，仅 50/595138 平坦像素不同 |
| `image-error` | 0.00 | 0.00 | 0.01 | **新增图片解码失败页**（⑭ 第 5 项）：实时侧真机实测三种失败（宿主 `ImageDecodeFailed`/`ImageTooLarge`/宿主说就绪但浏览器解不开）都走同一信息态并显示原因；有效 PNG 不受影响（见第 315 轮） |
| `json-preview` | 0.00 | 0.00 | 1.19 | 数据差异（字体已等化） |
| `main-project` | 0.00 | 0.00 | 1.37 | 数据差异；**等化前 1.95**（差值主要为字体） |
| `markdown-preview` | 0.00 | 0.00 | 1.42 | 数据差异（字体已等化） |
| `operation-progress` | 0.00 | 0.00 | 0.58 | 数据差异；**等化前 3.21**（差值主要为字体） |
| `operation-result` | 0.00 | 0.00 | 0.58 | 数据差异；**等化前 3.09**（差值主要为字体） |
| `project-context-menu` | 0.00 | 0.00 | 0.34 | 数据差异（字体已等化） |
| `push` | 0.00 | 0.00 | 0.18 | 数据差异；**等化前 0.56**（差值主要为字体） |
| `push-no-remote` | 0.00 | 0.00 | 0.03 | 数据差异；**等化前 0.39**（差值主要为字体） |
| `quick-open` | 0.00 | 0.00 | 2.30 | 数据差异（字体已等化） |
| `quick-open-empty` | 0.00 | 0.00 | 0.61 | 数据差异（字体已等化） |
| `remote` | 0.00 | 0.00 | 0.70 | 数据差异（字体已等化） |
| `repository-init` | 0.00 | 0.00 | 0.31 | 数据差异（字体已等化） |
| `repository-search` | 0.00 | 0.00 | 2.84 | 数据差异（字体已等化） |
| `reset` | 0.00 | 0.00 | 1.08 | 数据差异（字体已等化） |
| `rollback` | 0.00 | 0.00 | 0.35 | 数据差异；**等化前 0.73**（差值主要为字体） |
| `search-limited` | 0.00 | 0.00 | 0.50 | 数据差异（字体已等化） |
| `settings` | 0.00 | 0.00 | 0.03 | 数据差异（字体已等化） |
| `settings-save-failure` | 0.00 | 0.00 | 0.04 | **新增保存失败页**（⑭ 第 3 项）：两侧底栏文案**逐字相同**（"设置没有保存成功：…设置没有被修改，可以修正后重试。"）；另有真机交互证据（把 `settings.json` 临时造成不可写后点保存：对话框保持打开、`fontSize=21` 保留、底栏变红，见第 314 轮） |
| `settings-dirty` | 0.00 | 0.00 | 0.03 | **新增未保存修改标记页**（⑭ 第 6 项）：分类行上的实心圆点复用 `--augit-blue`（对照 `pycharm-settings-search-font.png` 里 Appearance 行的蓝点）；实时侧判定口径是"草稿值与磁盘值不同"，harness 有 3 条断言（出现/改回即消失/保存后不残留） |
| `smart-checkout` | 0.00 | 0.00 | 0.25 | 数据差异；**等化前 1.74**（差值主要为字体） |
| `stash` | 0.00 | 0.00 | 1.02 | 数据差异；**等化前 1.75**（差值主要为字体） |
| `stash-drop-confirm` | 0.00 | 0.00 | 1.10 | **新增危险确认页**（⑭ 第 7 项）：正文出自实时侧同一函数 `dangerConfirmBody()`，确认按钮用动作名"删除 stash@{0}"；真机实测同一结构（Stash 删除确认：取消后 0 次写入；Worktree 移除确认：取消后 0 次移除，见第 318 轮） |
| `stash-manager` | 0.00 | 0.00 | 1.36 | 数据差异（字体已等化） |
| `terminal` | 0.00 | 0.00 | 0.63 | 数据差异；**等化前 1.13**（差值主要为字体） |
| `terminal-close` | 0.00 | 0.00 | 0.34 | 数据差异（字体已等化） |
| `text-viewer` | 0.00 | 0.00 | 0.71 | 数据差异（字体已等化） |
| `workspace-open` | 0.00 | 0.00 | 0.09 | 数据差异（字体已等化） |
| `worktrees` | 0.00 | 0.00 | 1.41 | 数据差异（字体已等化） |

**结论**（分母 = 全部 48 个视觉稿页面，逐个实测，无抽样）：

- **55 个场景的标题栏与状态栏 `layoutPercent` 全部为 0.00** —— 背景、边框、内边距、圆角、
  图标底色逐像素一致，可见差异 100% 来自文字数据；唯一出现过的非 0 是 `terminal` 状态栏 4.11%，
  后被证明是**掩膜外扩不足**造成的指标假象（第 285 轮），把外扩从 1px 改为 3px 后同样是 0.00；
- **字体等化（ux-spec §4.3 强制要求）**：实时外壳按**用户保存的字体设置**渲染（本机实测 14px Segoe UI），
  视觉稿按设计基线（13px Microsoft YaHei UI / Cascadia Mono）。`compare-pixels.ps1` 现在默认在实时侧重载 URL
  并带上 `ui-size=13&ui-family=Microsoft YaHei UI&code-size=13&code-family=Cascadia Mono`（实测生效：14px → 13px 雅黑）。
  **等化后 48 个场景的内容带全部下降、无一升高**：最大降幅 2.63（`operation-progress` 3.21→0.58）、
  `changes-context-menu` 3.80→1.25、`diff-boundary` 4.09→1.60、`git-compare` 4.08→1.71、`commit-diff` 3.71→1.59、
  `conflict-resolver` 0.60→0.00；内容带最大值从 4.09 降到 **1.71**。
  **判读：此前偏高的内容带差异是字体差异造成的假象，不存在被它掩盖的真实布局差异。**
- 覆盖率：**50/50 页**（全部带审计开关 `--no-session-restore`，
  避免上一次运行留下的活动文件污染场景渲染；`file-limit` 用工作区内 `.dll` 造出同类"不可预览"内容，
  `diff-boundary` 用 `--diff src/App.cs`；`history-diff-failure` 首次抓图出现 276×45 的竞态截图，已重跑成功）；
- 内容带 0.00–4.09 的复核方法：**分区域拆分统计**（左栏 x<300 与内容区 x≥300 分别算 `layoutPercent`）
  + 最高项裁图复核。以 `git-compare`（内容带 4.08）为例，裁图显示两侧**行高、工具栏、文件栏、行号列全部对齐**，
  差异来自真实路径 `src/Augit.App/app.manifest` 与样例 `src/AugitApp/app.manifest` 的字宽与文本长度；
- 另有一处需要留意但**不是缺陷**：diff/比较视图的行号槽宽度按**实际补丁的最长行号**度量（设计如此），
  所以真实补丁与样例补丁的槽宽可以不同 —— 这类"数据驱动的几何"要在 ⑦ 里按令牌口径单独判读。
- 内容带 0.01%–3.09% 全部是数据或模式差异：判据不是"数字小"，而是①改动列表行分隔线位置
  逐像素一致；②同一张图片时几乎完全一致；③裁图复核后差异集中在文字/内容行；
- 本阶段**只抓到并修掉了 1 个真实布局缺陷**（终端正文底色：xterm 创建时没有传 `theme`，
  从 `:root` 读令牌得到浅色 `#ffffff`，正确来源是 `.terminal-view` 的计算样式；
  `layoutPercent` 21.40% → 1.13%，负向证据就是修复前的同一测量）。

### 1.2 条文级核销（`ux-spec` §4 全局视觉系统）

| 规格出处 | 要求（摘要） | 已实现 | 证据位置 | 差异 / 未覆盖 |
| --- | --- | --- | --- | --- |
| §4.1 | 主窗口四段式：标题栏 / 主区域 / 编辑工作区 / 状态栏，且不因加载或失败消失 | 是 | `live-shell.spec.cjs`「规格 §6.1：加载期间主框架与其它区域位置不变」块（`加载期间编辑工作区未被隐藏`、`加载期间左侧工具窗口仍可见`、`加载期间主框架与其它区域位置不变`） | — |
| §4.1 | 状态栏显示当前活动视图的文件身份；比较不继承后台文件的编码/换行 | 是 | 同文件状态栏相关断言；真机 `verify-acceptance.ps1` 场景 6/7 | — |
| §4.1 | 路径过长省略、悬停给出完整路径 | 是 | `hover` 断言块（悬停只更新目标按钮/行，`title` 提供完整文本） | — |
| §4.1 | 相同状态不重复刷新，不改变焦点、滚动与主窗口布局 | 是 | 「规格 §125」块（`快照相等时十次应用不触发界面更新`、`快照相等不改变工具窗口大小与布局`、`相同状态刷新保持侧栏用户可见状态`） | 见 §3.2 遗留 |
| §4.2 | 标题栏 44px；全局工具栏 42px；图标 16px、命中 ≥32×32 | 是 | 「规格 §12.3 视觉一致性」块（`标题栏高度为 44`、`全局工具栏宽度为 42`、`全局工具按钮命中区域均 >= 32x32`） | — |
| §4.2 | 侧栏 300–360px 拖动并持久化；底部 31% 夹取 180–305，字号增大时按 `max(180, 4h+80)` 扩展 | 是 | 「规格 §4.2：分隔条拖拽与写回」块 + 「底部面板最小高度随字号扩展」块（`向右拖动后侧栏变宽`、`侧栏宽度不超过上限 360`、`拖动结果写回设置`、`大字号下底部面板高度随字号扩展`、`拖到极小后仍不小于字号推导的最小高度`） | — |
| §4.2 | 分隔条从按下时的实际尺寸算增量、保留按下处偏移、不产生初始跳动 | 是 | 同上（`拖拽前侧栏宽度在 300–360 之间` 之后按实际右边缘 +2px 起拖） | — |
| §4.2 | **同一实际尺寸不重复布局**，保持焦点/文件/提交/滚动上下文 | 是 | 「规格 §4.2：分隔条两处边界情形」块（`同一实际尺寸不重复布局（不写尺寸、不重排）`、`同一实际尺寸保持焦点与滚动上下文`、`未实际改变尺寸的按下不写回设置`） | — |
| §4.2 | Esc / 系统取消 / 隐藏 / 折叠 / DPI 变化结束拖动，保留已显示尺寸 | 是 | 「规格 §4.2」块（`Esc 结束拖拽后保留已显示尺寸（不回退）`、`Esc 结束后拖动已终止`、`拖动结束后移动指针不再改变尺寸`） | 系统取消/DPI 变化未做自动化，见 §3.1 |
| §4.2 | **拖动中的 Esc 不同时关闭查找条** | 是 | 「规格 §4.2：分隔条两处边界情形」块（`拖动中的 Esc 只结束拖动、不关闭查找条` + 对照 `非拖动时 Esc 关闭查找条`） | — |
| §4.3 | 界面字体默认 `Microsoft YaHei UI`；界面与等宽四设置相互独立 | 是 | 「字体设置只改变显示」块（`默认字族来自设置（界面与正文各用各的栈）`、`改界面字号会作用到界面但不影响正文等宽字号`、`改等宽字号会作用到正文但不影响界面字号`） | — |
| §4.3 | 首屏就按用户已保存的字体设置渲染（不是保存后才生效） | 是 | 同块启动路径断言（`scene=terminal…&ui-font-size=19&code-font-size=17`） | — |
| §4.3 | 字号变化时标题栏/标签/树行/工具栏/状态栏按实际字高扩展，图标尺寸不变 | 是 | 「规格 §154」块（`树行高随界面字号扩展`）；`--augit-*` 高度令牌在 `applyTypography` 统一推导 | — |
| §4.3 | 字号变化保持原文档控件、树选择与**首个可见节点**、正文选择方向与滚动位置，不重开文件、不查 Git | 是 | 「规格 §154」块 10 条断言 + 运行时负向对照（`保持树的第一个可见节点`、`首个可见节点仍在原视口位置`、`保持原文档控件（不重新打开文件）`、`保持正文选择范围与方向`、`不重新查询 Git、不重新打开文件`） | — |
| §4.4 | 深/浅主题配色与令牌 | 是 | `mockup-scenes.spec.cjs` 48 场景 × dark/light；`live-shell` 双主题字体/焦点断言 | — |
| §4.4 | 当前列表行在列表获得焦点时浅蓝；焦点移出后保留选中并转中性灰 | 是 | 「规格 §4.4 / §5.4：键盘焦点…」块（`列表获得焦点时选中行用浅蓝`、`焦点移出列表后保留选中并转中性灰`、`焦点移出不触发打开文件/Diff/Git 查询`） | — |
| §4.4 | 焦点使用蓝色边框或外环，深/浅主题都清晰可见 | 是 | 同块（`深色主题下键盘焦点有可见焦点环`、`浅色主题下…`、`焦点环使用设计系统的蓝色令牌`） | — |
| §4.4 | 悬停使用更弱背景；选中态优先于悬停 | 是 | 「规格 §5/§6：悬停只改外观」块 + 真实指针悬停断言（`选中态优先于悬停`） | — |
| §4.5 | 自绘图标几何/线宽/状态；纯图标按钮必须有可访问名称与悬停说明 | 是 | 「规格 §12.3 / §12.4：所有图标入口都有可访问名称与悬停说明」块 | 与参考图标逐项几何一致性见 `docs/visual-refinement-status.md`（历史记录） |
| §4.5 | 禁止 Emoji 代替正式图标 | 是 | 图标形状集中在 `mockup.js` 的 `icon()` 表（自绘 SVG path），无 Emoji 字符 | — |

### 1.3 与 PyCharm 的对照（⑦⑧，Light 口径）

**环境实测（2026-09-19 复核，DPI-aware）**：显示器 `\\.\DISPLAY1` **2880×1800 物理 @ DPI 168（175%）
= 1646×1029 逻辑**，工作区 2880×1676 物理。此前记录的"1646×1029 物理 = 941×588 逻辑"是**重复缩放**的错值
（被 PyCharm 最大化窗口 2904 物理宽证伪）；已在 `pycharm-interactions.json` 的 `source.screen.correction`
与 `compareRecipe`、`mockup-gap-inventory.md` 三处改正。

**可执行配方**：PyCharm 最大化（窗口矩形 2904×1740 物理，含最大化时超出屏幕的不可见 resize 边框）
↔ Augit `--width 1659 --height 994`（CSS px，客户端实测 1646×981）且**不加 `--pixel-exact`**：
实测 `devicePixelRatio = 1.75`，CDP 截图落到 **2881×1717 物理**，与 PyCharm 截图**同一物理尺度**。

**同尺度复核（第 383 轮，方法：两侧同一套色带检测算法、区域均值、显式裁掉外框）**：
先解决了一个**证据归档问题** —— `artifacts/pycharm-baseline-20260919/pycharm-main.png` 是
**1659×994**（= 修正前的旧配方，13:58 拍），而 `artifacts/pycharm-interactions-16/p16-main-*.png`
是 **2904×1740 物理**（20:21 拍），**正是修正后 `compareRecipe` 要求的最大化物理尺寸**；
Augit 侧 `artifacts/pycharm-compare-20260919/augit-light-1659x994.png` 为 **2881×1717 物理**
（`devicePixelRatio=1.75`）。两侧同尺度，可以逐地标比：

| 地标（物理 px） | PyCharm `p16-main-idle` 2904×1740 | Augit `augit-light-1659x994` 2881×1717 | 换算逻辑 px（÷1.75） | 判读 |
| --- | ---: | ---: | --- | --- |
| 顶部工具带下边缘 | **y=89** | **y=79**（自绘标题栏 44 CSS × 1.75 = 77，实测 79） | 51 vs 45 | **语义不同，不可直接相减**：PyCharm 这张是**客户区**截图（无系统标题栏），0–89 是它的主工具栏；Augit 的 0–79 是自绘标题栏 |
| 状态栏上边缘 | **y=1677** | **y=1677** | — | **逐值相同**：两侧图高不同（1740 vs 1717）却在这一行对齐 |
| 状态栏高度 | **51**（1677→1728，1728 以下 `0,0,0` 是外框） | **40**（1677→1717 图像底） | 29.1 vs 22.9 | **Augit 比 PyCharm 矮约 6 逻辑 px** |

**⑦⑧ 逐面（per-surface）对照表（第 392 轮汇总；判据 = 声明容差内几何与配色等价，Swing↔Chromium 不判逐像素相等）**：

| 面 | PyCharm（物理 → CSS ÷1.75） | Augit（CSS / DOM 或令牌） | Δ | 判读 |
| --- | --- | --- | --- | --- |
| 顶部工具带 ↔ 自绘标题栏 | 0..89 → **50.9** | `--augit-title-height: 44` | — | **语义不同，不做等式**：PyCharm 该图是客户区截图（无系统标题栏），Augit 是无边框自绘 |
| 编辑器标签行 | 89..157 → **38.9** | `--augit-tab-height: 42` | −3.1 | 同一量级（Swing 标签行自带下边框） |
| 工具窗按钮条（主窗口） | **不可测**（轨道与相邻面板同色，§1.3 首版正是这样读错过） | 42（grid `42px …`） | — | 用设置对话框左栏代替比较（下一行） |
| 设置对话框左栏 | 分隔线 x=453 → **258.9** | `nav w=245` | +13.9 | 接近 |
| 设置搜索框 | 409×44 → **233.7×25** | `224.4×30` | 宽 +9.3 / 高 −5 | 宽接近、Augit 略高 |
| 设置主按钮（OK） | 232×49 → **132.6×28** | `54×30` | 宽 2.5× | Swing 按钮带文字与内边距，Augit 紧凑 |
| 状态栏上边缘 | **y=1677** | **y=1677** | **0** | **逐值对齐**（两侧图高 1740/1717 却同一边缘） |
| 状态栏高度 | 1677..1728 → **29.1** | `--augit-status-height: 22`（DOM 23） | −6.1 | **差异**：Augit 矮约 6 CSS；底色 `226,227,232` vs `233,234,238`（§1.3 已记，规范内） |
| 设置树行高 | **27.4**（对话框图 48 物理） | **27**（DOM） | +0.4 | **等价** |
| 选中行底色 | `#d0dffe` | `rgb(208,223,254)` | **0** | **逐值相同** |
| 主按钮底色 | `56,113,225` | `rgb(56,113,225)` | **0** | **逐值相同** |
| 悬停反馈（工具按钮条） | 变化单元聚合 `208,211,216 → 217,219,224` | `--augit-hover: #f1f2f4` | 明显 | 两侧都有反馈；取值不同（PyCharm 更暗），按 design-system 实现 |
| 禁用态（前置条件未满足） | 整块灰态（Color Scheme 色块、Sync with OS 时的 Scheme 下拉） | 同手法，由 `§7.17 … 时可编辑` 断言覆盖 | — | 手法一致：禁用并保留位置 |
| **Branches 弹出层**（↔ Augit `branches` / `smart-checkout`） | 顶部**搜索框**（占位符 `Search for branches and actions`，右侧 ✕ 清除与 ⚙ 设置）；动作区 `Update Project… Ctrl+T`、`Commit… Ctrl+K`、`Push… Ctrl+Shift+K`，分隔后 `New Branch… Ctrl+Alt+N`、`Checkout Tag or Revision…`；分支列表按 `Recent` / `Local`（含 `Remote`）分组，每行 = 分支名 + 右侧**跟踪引用**（如 `origin/main`），当前分支有标记 | Augit 分支弹层：搜索框 + 动作 + 分支列表（本地/远端分组） | — | **面级已对照**（证据 `pycharm-branches-dialog.png`）。**顺带验证**：PyCharm 这个弹层的搜索框也在顶部、打开即用——与第 65 轮给 Augit 分支弹层补的"打开后聚焦搜索框"是同一种结构；**实测几何对照（第 73/75 轮，用 `tools/audit/measure-pycharm-bands.ps1` 复算并改报区间）**：PyCharm 弹层宽 **≈434 CSS（三条扫描线 430.3 / 434.3 / 436.6）**、高 **≈398 CSS（396.6 / 400.0）** —— 左边界有阴影渐变，所以**报区间而不是单点**；动作行行距 **≈24.0 CSS**（文字带中点，混排字号有噪声，仅供量级参考）；Augit 同弹层**实测 DOM** = **338 × 399**、搜索框高 **30**、条目高 **29**（harness `INFO 分支弹层几何=…`，可复跑）。→ **高几乎相同**（Augit 399 落在 PyCharm 区间 396.6–400.0 内）、**宽差 ≈−96 CSS（PyCharm 更宽，区间 −92.6 ~ −98.6）**、**行高 +5 CSS（Augit 行更高）**。；**搜索框高（第 74 轮补量）**：PyCharm 框体上/下边框在 y=577 与 y=630（物理）→ **≈30.3 CSS**，Augit 实测 **30** → **Δ +0.3**（与弹层高度一样几乎一致，见图 `pycharm-branches-dialog.png`）。 |
| **VCS Operations 弹出菜单**（↔ Augit 的 Git 操作入口：`branches`/`smart-checkout`/`push`/`push-no-remote`/`rollback`/`stash`/`worktrees`） | `Alt+`` ` 打开（**可复现**：SendKeys 一次即成）。Git 分组条目与快捷键：`Commit… Ctrl+K`、`Commit File…`、`Rollback… Ctrl+Alt+Z`、`Show History`、`Annotate`、`Show Diff Ctrl+D`、`Branches… Ctrl+Shift+`` `、`Push… Ctrl+Shift+K`、`Stash Changes…`、`Unstash Changes…`、`Worktrees…`、`Copy Branch Name`、`Show Local History…` | Augit 把这些做成**独立页面/对话框**（分支、推送、回滚、暂存、工作树） | — | **仅入口级对照**（命名、快捷键、分组可比）；**对话框本体未采集**，因此不计入面级已对照——见 `vcs-operations-popup.png` |
| **确认对话框**（Confirm Exit，↔ Augit 破坏性操作的确认框） | 结构：警示图标 + 标题「Confirm Exit」+ 问句「Are you sure you want to exit?」+ `☐ Don't ask again` + 右侧按钮行 `[Exit]`（**危险默认**）+ `[Cancel]`（次按钮） | Augit 的关闭终端/回滚等确认框：正文说明影响 + 主按钮 + 次按钮 | — | **模式级对照**（危险默认、次按钮、不再询问的排布可对照）；证据 `confirm-exit-dialog.png`。**注意触发方式的教训**：这张是"在 VCS 弹出菜单里连按方向键 + Enter"时**意外触发**的（IDE 退出确认），当时立即 `Esc` 取消并复查 PyCharm 仍在运行（`Get-Process pycharm64` = 1）—— 采集脚本不得在 IDE 里乱按 Enter |
| Search Everywhere 浮层（↔ Augit `quick-open`/`quick-open-empty`/`search-limited`） | 顶部**页签 `All / Classes / Files / Symbols / Actions / Text`** + 查询框（提示 `Type / to see commands`）+ `Include non-project items` 复选；结果行 = 类型图标 + 名称 + 工作区相对路径；底部提示 `Open in Right Split`。**两态已采**：空查询（`search-everywhere-empty.png`）与有结果（`search-everywhere-results.png`） | Augit 快速打开：单一浮层（无分类页签）+ 结果行显示文件名与工作区相对路径、上限 100 项 | — | **面已对照**：结构对应（查询框/结果行/路径提示）；**差异如实记**：PyCharm 有 6 个分类页签，Augit 按产品规格只做文件名搜索 |
| 文件/编辑器右键菜单（↔ Augit `project-context-menu`） | `Refactor This…`(Ctrl+Alt+Shift+T)、`Rename…`(Shift+F6)、`Move File…`(F6)、`Copy File…`(F5)、`Safe Delete…`(Alt+Delete)；快捷键右对齐 | Augit 项目树右键菜单只列产品规格已实现的动作 | — | **面已对照**；**差异如实记**：PyCharm 首组是重构动作，Augit 不提供重构（产品规格禁止），菜单项集合因此不同 |
| Diff 视图（↔ Augit `commit-diff`/`diff-loading`/`diff-status`/`diff-boundary`/`git-compare*`/`history-diff-*`） | 编辑器内并排 diff：顶部工具条（上/下差异、刷新）+ 右侧 `N differences` 标签；列头 `Local`；左=基线版本、右=本地；行号槽 + 变更行整行浅色底 + 差异分隔箭头。**两态均已采集**：有差异（`diff-viewer-ctrlD-file.png`，显示 `1 difference`）与无差异（`readme-buffer-after-undo.png`，显示 `Contents are identical`） | Augit 的 diff 为自绘双栏/单栏 + 差异导航，页内横幅显示差异数 | — | **面已对照**（结构一一对应：工具条/列头/行号槽/差异底色）；Swing↔Chromium 不判逐像素 |
| Terminal 工具窗（↔ Augit `terminal` / `terminal-close`） | 底部工具窗 tabs `Terminal / Local`，真实 PowerShell 会话（`Windows PowerShell` banner + `PS D:\github\Augit>` 提示符）；面板白、chrome `233,234,238` | Augit 终端为 xterm.js + ConPTY，同样在底部工具窗、可拖动 | — | **面已对照**（证据 `toolwindow-terminal2.png`）；两侧都"真起一个 shell"这点一致 |
| Git Log 工具窗（↔ Augit `git-history`） | 底部工具窗高 499 物理 → **285.1 CSS**；面板白 `255,255,255`、chrome `233,234,238`；tabs `Git / Log / Console`、分支过滤 + 文本过滤两栏、提交列表含图标记号、右侧 Commit details | Augit 底部工具窗高度用户可拖动（无固定令牌） | — | **面已对照**（证据 `toolwindow-gitlog.png`）：结构对应，尺寸按可拖动处理；**列表行距（第 74 轮）**：提交行含**两行文字**（标题 + 作者/时间），沿文字带测得相邻带间距 **57 物理 ≈ 32.6 CSS**；按"两带一提交"推算单行 ≈ **65.1 CSS** —— 这是**推算值**（未用行分界线验证），只作量级参考；Augit 侧改动/提交列表行高是**令牌 `--commit-row-height: 27px`**（`.changes-layout .check-row`），**两者不能直接相减**（一个是含两行文字的日志行、一个是单行列表行）。 |
| Commit 工具窗（↔ Augit `commit-changes`） | 底部工具窗高 301 物理 → **172 CSS**；同底色 | 同上 | — | **面已对照**（证据 `toolwindow-commit.png`） |
| 设置页正文（字体类） | `Editor › Font`：JetBrains Mono 13.0 / line 1.2 / 无 Fallback；`Console Font`：同上 + Fallback `<None>` | Augit 的等宽字体/字号设置（跟随用户选择） | — | 字段语义对照；**数值不做等式**（PyCharm 用自身单位，用户环境） |

**未逐页对照的部分（诚实标注）**：48 个视觉稿页面里，PyCharm 一侧并不存在"逐页对应"的界面
（Augit 的 diff/冲突/stash/远端等页在 PyCharm 里是其它形态）；因此 ⑦⑧ 按**面**对照而不是按 Augit 页面硬凑，
逐页对照表只在"两侧都存在的面"上有意义。

**设置对话框内部几何（第 388 轮，PyCharm 分区掩膜 ÷1.75 ↔ Augit CDP DOM 矩形）**：

| 项 | PyCharm（物理 → CSS） | Augit（CSS） | 差 |
| --- | --- | --- | --- |
| 左栏宽 | 分隔线 x=453 → **258.9** | nav `w=245` | +13.9 |
| 搜索框 | 409×44 → **233.7×25** | `224.4×30` | 宽 +9.3 / 高 −5 |
| 主按钮（OK） | 232×49 → **132.6×28** | `54×30` | 宽度 2.5×（Swing 带文字与内边距） |
| 主按钮底色 | **56,113,225** | `rgb(56,113,225)` | **逐值相同**（`--augit-blue: #3871e1`） |
| 选中行底色 | `#d0dffe` | `rgb(208,223,254)` | **逐值相同** |
| 选中行高 | 掩膜读到 113 → **不采用**（很可能同时命中相邻蓝色元素） | `27` | **待重测**，本轮不采信 |

来源：PyCharm 侧 `artifacts/pycharm-interactions-16/p16-settings-tree-a.png`（1575×1225 物理），
Augit 侧 `artifacts/pycharm-compare-20260919/augit-settings-geometry.json`（8 个矩形 + 9 个配色，
同一台机、同一 1.75 缩放）。结论：差异集中在"控件有多宽"，高度与底色基本等价。

**同尺度悬停反馈（第 384 轮，同一套网格法）**：`p16-main-idle` 与两张 hover 图逐 8px 网格比对
（阈值 12，对**变化单元本身**取均值，而不是整框均值或单点采样 —— 两种错法本轮都实测踩过）：

| 目标 | 变化单元 | 变化单元均值（idle → hover） | 判读 |
| --- | ---: | --- | --- |
| 左侧工具按钮条 | **47**（bbox x 24..392, y 232..1464） | **208,211,216 → 217,219,224** | 两侧都有悬停反馈，但取值差异明显：Augit 用 `--augit-hover: #f1f2f4`(=241,242,244)，比 PyCharm 的聚合值更亮。按 design-system 实现，不改 |
| 状态栏 | **0** | — | 两图 md5 不同（`3f37a60d…` vs `c116df40…`）却在 8px 网格 + 阈值 12 下**零变化单元**：要么悬停未生效，要么差异小于采样分辨率（如仅 1px 顶边线）→ **这张图不能作为状态栏悬停取值证据**，已记为 `inconclusive` 而不是编一个数 |

**这条复核同时纠正了一个跨尺度错值**：§1.3 早先记的"状态栏 18.9（PyCharm）vs 22（Augit）"
方向是"Augit 更高"，而**同尺度**量出来是 **29.1 vs 22.9（PyCharm 更高）** —— 旧值来自修正前
那批不同尺度的截图，两数不可比。**保留两种记法但标明来源**：旧值只在说明"修正前那批图不可用于
绝对值比较"时引用，新值标为同尺度实测。是否要把 Augit 状态栏加高 6 逻辑 px，属**设计/规格口径**问题
（`design-system` 给的是 22），本轮只如实记录差值，不擅自改 UI。

**两侧的量法必须写明，否则数字不可比**：
- **PyCharm 侧 = 图像色带检测**（`PrintWindow` 截图逐行/逐列均值找边界）。它有个硬限制：
  **同色的相邻区域无法分开** —— 首版就把"6px 窗口留白 + 42px 轨道 + 树面板左内边距"读成了一个 53 逻辑 px 的轨道，
  据此写出"Augit 轨道比 PyCharm 宽 12px"的**错结论**（下一轮自查发现并改正）。
- **Augit 侧 = DOM 精确矩形**（CDP 读 `getBoundingClientRect()` 与计算样式），不受颜色重合影响。
  实测：`titlebar h=44`、`rail w=42`（grid `42px 360px …`，面板 x=52 → 窗口左留白 6px）、
  `editorTabs h=42`、`statusbar h=22 y=959.14`、`sideTool w=360`，`dpr=1.75`。
  证据：`artifacts/pycharm-compare-20260919/augit-geometry.json`。

| 地标 | PyCharm（换算到逻辑 px / rgb） | Augit（逻辑 px / rgb） | 差值 | 判读 |
| --- | --- | --- | ---: | --- |
| 标题栏 + 工具栏带高 | 12..88 物理 = **43.4** | **44**（`window-title-height`） | 0.6 | 结构不同（PyCharm 细标题栏 + 独立工具栏行），总高几乎相同 |
| 左侧全局工具栏宽 | 12..84 物理 = **41.1** | **42**（`global-rail-width`，实测 grid 42px） | 0.9 | 等价 |
| 状态栏高 | 33 物理 = **18.9** | **22**（`status-height: 22–23`） | 3.1 | Augit 按自己的令牌；PyCharm 更薄，登记为参考差异 |
| 标题栏 / 全局工具栏底色 | `233,234,238` | `233,234,238`（`chrome` = `#E9EAEE`） | 0 | **逐值相同** |
| 状态栏底色 | `226,227,232` | `233,234,238`（同一 `chrome` 令牌，规格明写"状态栏基础背景"） | ≈7 | 规格已定义 Augit 取值 → **规格内的差异**，非缺陷 |
| 工具窗口 / 编辑区底色 | `255,255,255` | `255,255,255`（`panel` = `#FFFFFF`） | 0 | 逐值相同 |
| 选中行底色 | `#d0dffe` | `--augit-blue-soft` = `#d0dffe` | 0 | 逐值相同 |
| 主按钮底色 | `#3871e1` | `--augit-blue` = `#3871e1` | 0 | 逐值相同 |
| 原生窗口外框 | 顶部 12 物理 `240,244,242` + 底部 12 物理黑 | 无（无边框自绘） | — | 结构性差异，不是缺陷 |
| 编辑器标签栏高 | 未单独测（与工具栏同带） | **42**（设计"标签栏高度 42px"） | — | 待 PyCharm 前台时补测 |

**第二个面：设置对话框（`--scene settings`，Light）**

| 地标 | PyCharm（图像，逻辑 px） | Augit（DOM，逻辑 px） | 差值 | 判读 |
| --- | --- | --- | ---: | --- |
| 对话框外框 | 900.0 × 700.0 | 1004.9 × 650.0 | +105 / −50 | 尺寸来源不同（PyCharm 用自己的默认/记忆尺寸，Augit 用 CSS 尺寸）→ 记录，不作为缺陷 |
| 左侧导航宽 | 241.7（12..435 物理） | 245 | 3.3 | 等价 |
| 搜索框高 | ≈33（白底框 y≈76..134 物理的粗测） | 30 | ≈3 | 量法是粗测，仅作量级核对 |
| 分类行高 | 未单独测（需前台精测） | 27 | — | 记未覆盖 |
| 选中行底色 | `#d0dffe`（早前实测） | `rgb(208,223,254)` = `#d0dffe` | 0 | **逐值相同** |
| 主按钮底色 | `#3871e1` | `rgb(56,113,225)` = `#3871e1` | 0 | **逐值相同** |
| 输入框底色/描边 | 白底 + 浅灰描边 | `#ffffff` + `rgb(209,211,217)` | — | 量级一致 |
| 对话框面板底色 | `rgb(247,248,249)` | `rgb(255,255,255)`（`panel #FFFFFF`） | ≈8 | Augit 设计系统明写"对话框内容 = `panel`" → **规格内的参考差异** |

Augit 侧证据：`artifacts/pycharm-compare-20260919/augit-settings-geometry.json`（`dialog/nav/search/row/field/ok/cancel` 矩形 + 计算样式，`dpr=1.75`；
注意该次取材用的是**用户已保存的界面字号 14px**，未做字体等化，字号相关项不作结论）；
PyCharm 侧：`artifacts/pycharm-baseline-20260919/pycharm-settings-appearance.png`（1575×1225 物理）。

**口径**：判据是"声明容差内地标等价"，**不做逐像素相等**（PyCharm 是 Swing、Augit 是 Chromium；
字形与行高来自不同排版引擎）。证据：`artifacts/pycharm-interactions-16/p16-main-idle.png`（2904×1740）、
`artifacts/pycharm-compare-20260919/augit-light-1659x994.png`（2881×1717）。
**未覆盖**：逐页逐状态全量对照（当前只覆盖主窗口 chrome 与设置对话框的配色/几何量级）、PyCharm dark 口径、
编辑器标签栏与底部工具窗口的 PyCharm 实测（需要前台）。

> **量测方法限制（第 76 轮，主窗口顶部）**：主窗口**菜单栏 + 工具栏 + 编辑器标签行是连成一片的非白区域**，所以"沿扫描线找第一个非背景像素"这种**通用边缘法无法切出标签带**：在 x=1400 上从 y=34 起就已经是非背景，把起点一路提前也只会得到"整片高度"（工具已能识别这种情形并打印 `WARN CLIPPED_AT_START`）。标签带的可用值仍来自**颜色跃变法**（`227,233,240 → 213,218,224` 在 y=76/77、标签底 ≈145/147）→ **≈38.9 CSS**（`--augit-tab-height: 42`，Δ **+3.1**）。Commit 工具窗的上边缘本轮在 y 1300–1450、3 条竖线里 2 条 `NO_EDGE`，**不重新发布**，仍沿用单方法得到的 **172 CSS** 并保留其"单方法"备注。

> **标签带本轮未复算（第 77 轮）**：`-Mode transitions` 在本轮截图上被彩色图标与试用按钮污染（干净列只给出一条无法定性的 2px `233,234,238` 分隔线），因此**标签带高度仍沿用早先颜色跃变法在另一张截图上的 ≈38.9 CSS**（Δ +3.1 vs `--augit-tab-height: 42`），并在日志里记明"本轮未能复算"。

### 1.4 逐页覆盖表（⑪：页面 × 检查层级 × 判读）

**口径**：本表的数字由 `tools/audit/gen-coverage-table.cjs` 从 §1.1 的像素行与
`tools/audit/verify-acceptance.ps1` 的场景列表**直接生成**（不手写数字；重跑即可消除漂移）。
四层的分母如下：

| 检查层级 | 分母 | 本轮结果 | 说明 |
| --- | ---: | --- | --- |
| A 线：同引擎像素对照 | 55 场景 | 55/55 有行（见 §1.1） | 逐带 `layoutPercent`；判据是"平坦像素差异"而非逐像素相等 |
| B 线：真机巡检 | 54 场景（干净仓库） | **54/54 PASS** | 分母 = 场景总数 − diff-boundary（需含改动的工作区） |
| B 线：含改动工作区的场景 | 1 场景 | **OK 1/1**（`diff-boundary`，第 371 轮） | 单独一次真机验收；与干净仓库那一轮**分开计**，不合并成一个数字 |
| B 线：行为断言 | 351 条规格条文 | 406 条用例行（其中 336 条有逐条行） | **按条文归属，不按页面**；逐页行为覆盖请查 §2 对应小节 |
| C 线：PyCharm 对照 | 见 §1.3 | 2 个面（主窗口 chrome、设置对话框） | 判据是"声明容差内地标等价"，不做逐像素相等 |

> **为什么行为断言不按页面列**：Harness 的断言是按"规格条文/状态转换"组织的（一个断言常跨多页，
> 例如"工具窗口互斥"同时覆盖终端与 Git 历史），硬按页面拆会造出虚假的逐页分母。
> 因此这里只给"条文维度"的分母，逐页行为覆盖请对照 §2 的小节标题。

| 页面（场景） | A 线 titlebar | A 线 statusbar | A 线 content | B 线真机巡检 | A 线判读 |
| --- | ---: | ---: | ---: | --- | --- |
| `blame` | 0.00 | 0.00 | 1.44 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `branches` | 0.00 | 0.00 | 1.39 | PASS（54/54 那一轮） | 数据差异；**等化前 2.81**（差值主要为字体） |
| `changes-context-menu` | 0.00 | 0.00 | 1.25 | PASS（54/54 那一轮） | 数据差异；**等化前 3.80**（差值主要为字体） |
| `clone` | 0.00 | 0.00 | 0.09 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `commit-changes` | 0.00 | 0.00 | 0.26 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `commit-diff` | 0.00 | 0.00 | 1.59 | PASS（54/54 那一轮） | 数据差异；**等化前 3.71**（差值主要为字体） |
| `commit-empty` | 0.00 | 0.00 | 0.00 | PASS（54/54 那一轮） | 空态骨架，两侧逐像素一致（0.00） |
| `conflict-list` | 0.00 | 0.00 | 0.01 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `conflict-resolver` | 0.00 | 0.00 | 0.00 | PASS（54/54 那一轮） | **等化后 0.00**（等化前 0.60） |
| `diff-boundary` | 0.00 | 0.00 | 1.60 | **OK**（含改动的工作区，第 371 轮） | 数据差异；**等化前 4.09**（差值主要为字体） |
| `diff-loading` | 0.00 | 0.00 | 1.47 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `diff-status` | 0.00 | 0.00 | 1.29 | PASS（54/54 那一轮） | **新增最终说明页**（⑭ 第 2 项）：二进制/超限/无差异/失败共用同一说明块；实时侧用真·二进制改动仓库实测（`status=Binary`） |
| `file-history` | 0.00 | 0.00 | 0.63 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `file-limit` | 0.00 | 0.00 | 0.01 | PASS（54/54 那一轮） | 两侧都是"不可预览"页（数据不同） |
| `git-compare` | 0.00 | 0.00 | 1.71 | PASS（54/54 那一轮） | 数据差异；**等化前 4.08**（差值主要为字体） |
| `git-compare-empty` | 0.00 | 0.00 | 1.41 | PASS（54/54 那一轮） | **新增引用比较无差异页**（⑭ 第 4 项）：文件栏保留双方引用、变更导航禁用但保留入口、正文只留比较语义的摘要说明；实时侧同一条规则由 harness 正/负向两条断言覆盖（见第 316 轮） |
| `git-history` | 0.00 | 0.00 | 1.37 | PASS（54/54 那一轮） | 数据差异；**等化前 1.73**（差值主要为字体） |
| `git-history-empty` | 0.00 | 0.00 | 0.64 | PASS（54/54 那一轮） | **新增空态页**（⑭ 第 1 项）：实时侧用空仓库实测，提交区显示"仓库还没有提交"，引用树/筛选栏保留 |
| `git-history-graph` | 0.00 | 0.00 | 1.40 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `git-history-menu` | 0.00 | 0.00 | 1.06 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `git-unavailable` | 0.00 | 0.00 | 0.51 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `go-to-line` | 0.00 | 0.00 | 0.58 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `history-diff-cancelled` | 0.00 | 0.00 | 1.38 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `history-diff-failure` | 0.00 | 0.00 | 1.39 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `history-diff-loading` | 0.00 | 0.00 | 1.61 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `image-error` | 0.00 | 0.00 | 0.01 | PASS（54/54 那一轮） | **新增图片解码失败页**（⑭ 第 5 项）：实时侧真机实测三种失败（宿主 `ImageDecodeFailed`/`ImageTooLarge`/宿主说就绪但浏览器解不开）都走同一信息态并显示原因；有效 PNG 不受影响（见第 315 轮） |
| `image-preview` | 0.00 | 0.00 | 0.01 | PASS（54/54 那一轮） | 两侧是同一张真实 PNG，仅 50/595138 平坦像素不同 |
| `json-preview` | 0.00 | 0.00 | 1.19 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `main-project` | 0.00 | 0.00 | 1.37 | PASS（54/54 那一轮） | 数据差异；**等化前 1.95**（差值主要为字体） |
| `markdown-preview` | 0.00 | 0.00 | 1.42 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `operation-progress` | 0.00 | 0.00 | 0.58 | PASS（54/54 那一轮） | 数据差异；**等化前 3.21**（差值主要为字体） |
| `operation-result` | 0.00 | 0.00 | 0.58 | PASS（54/54 那一轮） | 数据差异；**等化前 3.09**（差值主要为字体） |
| `project-context-menu` | 0.00 | 0.00 | 0.34 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `push` | 0.00 | 0.00 | 0.18 | PASS（54/54 那一轮） | 数据差异；**等化前 0.56**（差值主要为字体） |
| `push-no-remote` | 0.00 | 0.00 | 0.03 | PASS（54/54 那一轮） | 数据差异；**等化前 0.39**（差值主要为字体） |
| `quick-open` | 0.00 | 0.00 | 2.30 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `quick-open-empty` | 0.00 | 0.00 | 0.61 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `remote` | 0.00 | 0.00 | 0.70 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `repository-init` | 0.00 | 0.00 | 0.31 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `repository-search` | 0.00 | 0.00 | 2.84 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `reset` | 0.00 | 0.00 | 1.08 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `rollback` | 0.00 | 0.00 | 0.35 | PASS（54/54 那一轮） | 数据差异；**等化前 0.73**（差值主要为字体） |
| `search-limited` | 0.00 | 0.00 | 0.50 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `settings` | 0.00 | 0.00 | 0.03 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `settings-dirty` | 0.00 | 0.00 | 0.03 | PASS（54/54 那一轮） | **新增未保存修改标记页**（⑭ 第 6 项）：分类行上的实心圆点复用 `--augit-blue`（对照 `pycharm-settings-search-font.png` 里 Appearance 行的蓝点）；实时侧判定口径是"草稿值与磁盘值不同"，harness 有 3 条断言（出现/改回即消失/保存后不残留） |
| `settings-save-failure` | 0.00 | 0.00 | 0.04 | PASS（54/54 那一轮） | **新增保存失败页**（⑭ 第 3 项）：两侧底栏文案**逐字相同**（"设置没有保存成功：…设置没有被修改，可以修正后重试。"）；另有真机交互证据（把 `settings.json` 临时造成不可写后点保存：对话框保持打开、`fontSize=21` 保留、底栏变红，见第 314 轮） |
| `smart-checkout` | 0.00 | 0.00 | 0.25 | PASS（54/54 那一轮） | 数据差异；**等化前 1.74**（差值主要为字体） |
| `stash` | 0.00 | 0.00 | 1.02 | PASS（54/54 那一轮） | 数据差异；**等化前 1.75**（差值主要为字体） |
| `stash-drop-confirm` | 0.00 | 0.00 | 1.10 | PASS（54/54 那一轮） | **新增危险确认页**（⑭ 第 7 项）：正文出自实时侧同一函数 `dangerConfirmBody()`，确认按钮用动作名"删除 stash@{0}"；真机实测同一结构（Stash 删除确认：取消后 0 次写入；Worktree 移除确认：取消后 0 次移除，见第 318 轮） |
| `stash-manager` | 0.00 | 0.00 | 1.36 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `terminal` | 0.00 | 0.00 | 0.63 | PASS（54/54 那一轮） | 数据差异；**等化前 1.13**（差值主要为字体） |
| `terminal-close` | 0.00 | 0.00 | 0.34 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `text-viewer` | 0.00 | 0.00 | 0.71 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `workspace-open` | 0.00 | 0.00 | 0.09 | PASS（54/54 那一轮） | 数据差异（字体已等化） |
| `worktrees` | 0.00 | 0.00 | 1.41 | PASS（54/54 那一轮） | 数据差异（字体已等化） |

> 生成来源：§1.1 55 行像素数据、`verify-acceptance.ps1` 54 个场景、
> §2.0 合计行（351 条条文 / 406 条用例行）。
> **第 86 轮补测（④，判据 = `layoutPercent`）**：又用同一链路重跑 6 个此前未复核的场景（证据
> `artifacts/pixel-review-20260921b/`，每场景 `-mockup/-live.png` + `heatmap.png`）：
>
> | 场景 | titlebar | statusbar | content `layoutPercent` | 判读 |
> | --- | ---: | ---: | ---: | --- |
> | `settings` | 0.00 | 0.00 | **0.03** | 布局一致 |
> | `quick-open-empty` | 0.00 | 0.00 | **0.61** | 布局一致 |
> | `reset` | 0.00 | 0.00 | **1.09** | 布局一致（差异集中在文字） |
> | `stash` | 0.00 | 0.00 | **1.09** | 同上 |
> | `branches` | 0.00 | 0.00 | **1.36** | 同上 |
> | `worktrees` | 0.00 | 0.00 | **1.41** | 本批最高，仍 < 2.0；无整页错位 |
>
> **三个带（titlebar/statusbar）六个场景全部 0.00** —— 布局层面与视觉稿一致。
> **同时给出一条"别用 visiblePercent"的活证据**：`quick-open-empty` 在 `compare-pixels.ps1` 自报
> `statusbar visiblePercent=5.09 / blockDiffPercent=16.44 FAIL`，而同一张图用 `compare-pixels.py`
> 的边缘掩膜算是 **`layoutPercent=0.00`** —— 状态栏那点差异全是**真实数据文字**，不是布局。

> **第 87 轮补测（④，判据 = `layoutPercent`）**：又跑 8 个场景（同一证据目录）——
>
> | 场景 | titlebar | statusbar | content `layoutPercent` | 判读 |
> | --- | ---: | ---: | ---: | --- |
> | `conflict-resolver` | 0.00 | 0.00 | **0.00** | 三栏布局一致 |
> | `commit-empty` | 0.00 | 0.00 | **0.00** | 一致 |
> | `image-preview` | 0.00 | 0.00 | **0.01** | 一致 |
> | `commit-changes` | 0.00 | 0.00 | **0.26** | 一致（差异在文字/真实数据） |
> | `operation-result` | 0.00 | 0.00 | **0.28** | 同上 |
> | `git-unavailable` | 0.00 | 0.00 | **0.51** | 同上 |
> | `text-viewer` | 0.00 | 0.00 | **0.60** | 同上 |
> | `json-preview` | 0.00 | 0.00 | **1.19** | 本批最高，仍 < 2.0 |
>
> **8 个场景 × 3 个带 = 24 个读数里，titlebar/statusbar 全为 0.00**；content 最高 1.19。
> **`visiblePercent` 陷阱再添一例**：`text-viewer` 在 `compare-pixels.ps1` 自报 `failures=1 / PIXELS_DIFFER`
> （可见像素差超标），而 `layoutPercent=0.60` —— 差异同样主要来自真实数据文字。
> 累计已有 **26/55** 个场景拿到本轮口径下的 `layoutPercent` 实测（12 高值 + 6 + 8）。

> **第 88 轮补测（④ 第 4 批，判据 = `layoutPercent`）**：8 个场景 ——
>
> | 场景 | titlebar | statusbar | content `layoutPercent` | 判读 |
> | --- | ---: | ---: | ---: | --- |
> | `file-limit` | 0.00 | 0.00 | **0.01** | 一致 |
> | `project-context-menu` | 0.00 | 0.00 | **0.34** | 一致 |
> | `search-limited` | 0.00 | 0.00 | **0.50** | 一致 |
> | `markdown-preview` | 0.00 | 0.00 | **0.55** | 一致 |
> | `go-to-line` | 0.00 | 0.00 | **0.58** | 一致 |
> | `terminal` | 0.00 | 0.00 | **0.63** | 一致 |
> | `git-history` | 0.00 | 0.00 | **1.38** | 一致 |
> | `repository-search` | 0.00 | 0.00 | **2.08** | **本批唯一 > 2.0**；与第 60 轮复核值 **2.08 完全一致（Δ 0.00）**，差异仍归因于 live 数据状态（真实搜索命中文本），无布局错位 |
>
> **稳定性交叉验证（本轮顺带得到）**：本批里有 4 个场景在第 60 轮已复核过，
> 两次独立运行的 `content layoutPercent` **完全一致**（`repository-search` 2.08/2.08、`markdown-preview` 0.55/0.55、
> `search-limited` 0.50/0.50、`git-history` 1.38/1.38，**Δ 全部 0.00**）—— 说明这条链路在同一版本上是**可复现**的，
> 数字不是随机波动。累计 **34/55** 个场景已有本轮口径的实测证据。

### 1.6 逐页 × PyCharm 对照状态（⑦⑧，55 行有分母；生成器 `tools/audit/gen-pycharm-coverage.cjs`）

**口径**：PyCharm 没有 Augit 的 diff/冲突/stash/远端等页面形态，**按面**对照（§1.3 的逐面表）才是可核对的；
本表因此给出**每个 Augit 页面**的 PyCharm 侧状态，而不是硬凑一一对应。
`A` = 主窗口 chrome、`B` = 编辑器标签行/树行高、`C` = 设置对话框、`D` = Git Log 工具窗、`E` = Commit 工具窗、`F` = Terminal 工具窗、`G` = Diff 视图、`H` = Search Everywhere、`I` = 文件/编辑器右键菜单（九面均已在 §1.3 有实测值/证据图）。

| 场景（§1.1 的 55 行） | PyCharm 侧 | 说明 |
| --- | --- | --- |
| `blame` | 未对照（RECOVERABLE） | PyCharm 的 Annotate 视图在 VCS 弹出菜单里有入口（`Annotate`），可直接采 |
| `branches` | **面级已对照** | L（Branches 弹出层）——见 §1.3 逐面表的实测值与判读 |
| `changes-context-menu` | 未对照（RECOVERABLE） | Git 工具窗里对改动文件的右键菜单；需要工作区先有改动 |
| `clone` | 未对照（RECOVERABLE） | PyCharm 用 `File | New | Project from Version Control` 向导，形态与 Augit 的 Clone 对话框不同 |
| `commit-changes` | **面级已对照** | E（Commit 工具窗）——见 §1.3 逐面表的实测值与判读 |
| `commit-diff` | **面级已对照** | G（Diff 视图）——见 §1.3 逐面表的实测值与判读 |
| `commit-empty` | **面级已对照** | E（Commit 工具窗）——见 §1.3 逐面表的实测值与判读 |
| `conflict-list` | 未对照（PRECONDITION） | PyCharm 的 Merges/冲突视图需要真实冲突文件；当前树干净 |
| `conflict-resolver` | 未对照（PRECONDITION） | PyCharm 的三栏合并工具需要真实冲突；当前树干净 |
| `diff-boundary` | **面级已对照** | G（Diff 视图）——见 §1.3 逐面表的实测值与判读 |
| `diff-status` | **面级已对照** | G（Diff 视图）——见 §1.3 逐面表的实测值与判读 |
| `diff-loading` | **面级已对照** | G（Diff 视图）——见 §1.3 逐面表的实测值与判读 |
| `file-history` | 未对照（RECOVERABLE） | VCS 弹出菜单的 `Show History` 即文件历史入口 |
| `file-limit` | **面级已对照** | A+B（主窗口 chrome / 编辑器标签行）——见 §1.3 逐面表的实测值与判读 |
| `git-compare` | **面级已对照** | G（Diff 视图）——见 §1.3 逐面表的实测值与判读 |
| `git-compare-empty` | **面级已对照** | G（Diff 视图）——见 §1.3 逐面表的实测值与判读 |
| `git-history` | **面级已对照** | D（Git Log 工具窗）——见 §1.3 逐面表的实测值与判读 |
| `git-history-empty` | **面级已对照** | D（Git Log 工具窗）——见 §1.3 逐面表的实测值与判读 |
| `git-history-graph` | **面级已对照** | D（Git Log 工具窗）——见 §1.3 逐面表的实测值与判读 |
| `git-history-menu` | **面级已对照** | D（Git Log 工具窗）——见 §1.3 逐面表的实测值与判读 |
| `git-unavailable` | 未对照（AUGIT_ONLY） | PyCharm 没有"Git 不可用"降级页：它只是隐藏/禁用 VCS 菜单，Augit 用显式页面表达该状态 |
| `go-to-line` | **面级已对照** | A+B（主窗口 chrome / 编辑器标签行）——见 §1.3 逐面表的实测值与判读 |
| `history-diff-cancelled` | **面级已对照** | G（Diff 视图）——见 §1.3 逐面表的实测值与判读 |
| `history-diff-failure` | **面级已对照** | G（Diff 视图）——见 §1.3 逐面表的实测值与判读 |
| `history-diff-loading` | **面级已对照** | G（Diff 视图）——见 §1.3 逐面表的实测值与判读 |
| `image-preview` | **面级已对照** | A+B（主窗口 chrome / 编辑器标签行）——见 §1.3 逐面表的实测值与判读 |
| `image-error` | 未对照（RECOVERABLE） | 需先造一张损坏图片样本，再看 PyCharm 图片查看器的报错呈现；尚未采 |
| `json-preview` | **面级已对照** | A+B（主窗口 chrome / 编辑器标签行）——见 §1.3 逐面表的实测值与判读 |
| `main-project` | **面级已对照** | A+B（主窗口 chrome / 编辑器标签行）——见 §1.3 逐面表的实测值与判读 |
| `markdown-preview` | **面级已对照** | A+B（主窗口 chrome / 编辑器标签行）——见 §1.3 逐面表的实测值与判读 |
| `operation-progress` | 未对照（RECOVERABLE） | PyCharm 的后台任务进度（状态栏）+ 通知气泡；需在做一次真实操作时采 |
| `operation-result` | 未对照（RECOVERABLE） | 同上：操作完成后的通知/结果呈现 |
| `project-context-menu` | **面级已对照** | I（文件/编辑器右键菜单）——见 §1.3 逐面表的实测值与判读 |
| `push` | 仅入口级证据 | J 入口级（VCS Operations 弹出菜单：命名 + 快捷键）；**对话框本体未采集，不计入面级已对照**；**环境前提**：本仓库没有 remote → PyCharm 不弹 Push 对话框（与 Augit 的 push-no-remote 状态吻合） |
| `push-no-remote` | 仅入口级证据 | J 入口级（VCS Operations 弹出菜单：命名 + 快捷键）；**对话框本体未采集，不计入面级已对照**；**环境前提**：同上：无 remote 时 PyCharm 不提供 Push 对话框 |
| `quick-open` | **面级已对照** | H（Search Everywhere）——见 §1.3 逐面表的实测值与判读 |
| `quick-open-empty` | **面级已对照** | H（Search Everywhere）——见 §1.3 逐面表的实测值与判读 |
| `remote` | 未对照（RECOVERABLE） | `Git | Manage Remotes` 对话框（不在 VCS 弹出菜单里，需从 Git 菜单进） |
| `repository-init` | 未对照（RECOVERABLE） | PyCharm 用 `Enable Version Control Integration`，形态与 Augit 的初始化页面不同 |
| `repository-search` | 未对照（RECOVERABLE） | PyCharm 的 `Find in Path`（Ctrl+Shift+F）弹层；尚未采 |
| `reset` | 未对照（PRECONDITION） | PyCharm 的 `Rollback…` 需要本地改动；当前树干净（与 rollback 同因） |
| `rollback` | 仅入口级证据 | J 入口级（VCS Operations 弹出菜单：命名 + 快捷键）；**对话框本体未采集，不计入面级已对照**；**环境前提**：Rollback 需要本地改动；当前工作区干净 → PyCharm 禁用该动作 |
| `search-limited` | **面级已对照** | H（Search Everywhere）——见 §1.3 逐面表的实测值与判读 |
| `settings` | **面级已对照** | C（设置对话框）——见 §1.3 逐面表的实测值与判读 |
| `settings-save-failure` | **面级已对照** | C（设置对话框）——见 §1.3 逐面表的实测值与判读 |
| `settings-dirty` | **面级已对照** | C（设置对话框）——见 §1.3 逐面表的实测值与判读 |
| `smart-checkout` | **面级已对照** | L（Branches 弹出层）——见 §1.3 逐面表的实测值与判读 |
| `stash` | 仅入口级证据 | J 入口级（VCS Operations 弹出菜单：命名 + 快捷键）；**对话框本体未采集，不计入面级已对照**；**环境前提**：Stash Changes 需要本地改动；当前工作区干净 → PyCharm 禁用该动作（实测无对话框） |
| `stash-drop-confirm` | 未对照（PRECONDITION） | 同上：没有 stash 就看不到删除确认 |
| `stash-manager` | 未对照（PRECONDITION） | PyCharm 的 Stash/Unstash 需要先有 stash 内容；当前工作区干净 |
| `terminal` | **面级已对照** | F（Terminal 工具窗）——见 §1.3 逐面表的实测值与判读 |
| `terminal-close` | **面级已对照** | F（Terminal 工具窗）——见 §1.3 逐面表的实测值与判读 |
| `text-viewer` | **面级已对照** | A+B（主窗口 chrome / 编辑器标签行）——见 §1.3 逐面表的实测值与判读 |
| `workspace-open` | 未对照（RECOVERABLE） | PyCharm 的 `File | Open` 项目选择对话框；尚未采 |
| `worktrees` | 仅入口级证据 | J 入口级（VCS Operations 弹出菜单：命名 + 快捷键）；**对话框本体未采集，不计入面级已对照**；**环境前提**：VCS 弹出菜单键入过滤后未打开对话框（本轮落到了"最近文件"路径） |

> 本轮读数：**33/55 个页面**落在已实测的面上；另有 **5 页**只有**入口级/确认框级**证据（不计入面级已对照）；其余 17 个页面
> 的 PyCharm 同类面**尚未采集**（原因逐行写明）。**不把"未采集"写成"已通过"，也不把它算进对照完成率。**
### 2.0 条文穷举进度（⑨，如实带分母）

**口径（两件事分开说，避免把"行数"当"已覆盖条数"）**：
- **分母** = `docs/ux-spec.md` 各节的**一级条文**（`- ` 开头）条数；
- **分子** = 本文件里显式写出「规格出处 → 操作 → 状态转换 → 已实现 → 证据位置」的**用例行数**。
  一条规格可能拆成多行（例如 §5.1 的折叠/恢复各一行），也可能一行都没有 ——
  **因此"用例行数"是已覆盖条数的上界，不是逐条核对后的结果**；逐条打勾是 ⑨ 的剩余工作。

| 章节 | 规格条数（分母） | 用例行数（上界） | 位置 |
| --- | ---: | ---: | --- |
| §4 全局视觉系统 | 35 | 20 | §1.2（视觉/令牌类以像素表与令牌表核销） |
| §5 外壳与交互框架 | 29 | 62 | §2.1–§2.4 概览 33 行 + 逐条展开 29 行 |
| §6 异步加载与刷新不变量 | 40 | 59 | §2.5 概览 19 行 + 逐条展开 40 行 |
| §7 页面规格 | 210 | **224（全 18 节逐条展开完成）** | §2.6 概览 14 行 + 全部 210 条逐条展开 |
| §9 关键状态机 | 22 | **26** | §2.7 概览 4 行 + 逐条展开 22 行（**用例行 = 概览 + 逐条**，第 375 轮修正：此前误写成条文数 22） |
| §10 空/错/禁用/危险 | 15 | 15 | §2.8（逐条展开：**15 条全部有断言**，由脚本按表统计） |
| **合计** | **351** | **406** | §1.2 与 §2.1–§2.8（§5/§6/§7/§9 含概览行）；**合计 = 上列六行之和**（由 `tools/audit/check-doc-claims.cjs` 机械核对：§1.2/§2.1–§2.8 与 §2.7 三个"逐条展开"区块逐块计数后相加） |

> **当前事实**：**九节全部逐条展开完成** —— 规格条文 351 条中有逐条行的是 **336 条**
> （§4 20 + §5 29 + §6 40 + §7 210 + §9 22 + §10 15）；**唯一未成行的是 §4 的 15 条**（视觉/令牌类，
> 以 §1.2 的像素表与令牌表核销，未逐条成行）。用例行合计 **406 行**（含 §2.1–§2.4/§2.5/§2.6/§2.7 的概览行；**= 上表六行之和**）。
> 与 §6（40 条 19 行）。推进顺序：§7 按页面分批 → §6 → §5/§9/§10 补齐 → §4 的像素化核销。
> 每一行的证据位置必须能在 `live-shell` 里找到对应断言名，找不到的不计入（宁少不虚）。
>
> **非"是"条文的去向（第 62 轮，机器可读）**：351 条里 **138 条**目前不是"是"，
> 由 `tools/audit/gen-clause-conclusions.cjs` 逐行归类到 §2.10：**A 实现已有、仅缺断言 99 条**、
> **B 只有像素/间接证据 4 条**、**C 未覆盖（无断言也无观察）10 条**、**D 其它部分覆盖 25 条**。
> A 类是补断言的队列；C 类要么补断言、要么按纪律交用户裁决，**都不写成通过**。

> **两个容易混淆的口径（第 375 轮查清并写死）**：
> ① **"用例行数"**（上表的列）= 该节在本文里**成行的**记录数 = 概览行 + 逐条展开行；
> ② **"有逐条行的条文数"**（下面那句"351 条中有逐条行的是 336 条"）= 有条文级逐条记录的条文数，
> 对 §5/§6/§9 恰好等于其条文数（29/40/22），因为它们的逐条展开是**一条一行**。
> 两者不可混用 —— 第 374 轮把 §9 的"用例行数"按条文数写成 22，导致合计 407/402 与本节各行都对不上；
> 第 375 轮查清 §2.7 里其实有**三个**"逐条展开"区块（§6 40 条 + §5 29 条 + §9 22 条 = 91 行，
> 这正是计数脚本在 §2.7 看到 91 条编号行的原因），于是把 §9 的行改为 **26 = 4 概览 + 22 逐条**、
> 合计改为 **406**，并让 `check-doc-claims.cjs` **逐块**核对（§1.2 / §2.1–§2.4 / §2.5 / §2.6 / §2.7 的两个表 /
> §2.8 / §2.7 的三个"逐条展开"区块），任一处漂移都会立刻失败。

### 2.1 §5.1 工具窗口切换与标题栏菜单

| 规格出处 | 操作 → 状态转换 | 已实现 | 证据位置 |
| --- | --- | --- | --- |
| §5.1 | 点击已激活入口 → 折叠；再次点击 → 恢复上次尺寸 | 是 | 「规格 §5.1：工具窗口切换与折叠」块（`初始工具窗口为项目`、`再次点击已激活入口则折叠`、`点击已激活入口恢复侧栏`、`再次点击已激活入口恢复侧栏`） |
| §5.1 | 点击同区域另一个入口 → 原位替换 | 是 | 同块（`切换到提交工具窗口`） |
| §5.1 | 切换工具窗口不关闭编辑标签、不改当前文件、不重置编辑滚动 | 是 | 同块（`切换工具窗口不改变当前文件`、`切换工具窗口后标签仍指向当前文档`） |
| §5.1 | 终端与 Git 历史互斥（底部只有一个） | 是 | 同块（`终端与 Git 历史互斥（底部只有一个工具窗口）`）+ 「规格 §9.4：工具窗口状态机」块 |
| §5.1 | 汉堡按钮原位显示五个文字入口，不弹替代标题栏的悬浮卡片 | 是 | 「规格 §5.1：标题栏汉堡菜单」块（`菜单入口原位显示`、`不出现替代标题栏的悬浮卡片`） |
| §5.1 | 文件/视图/Git 打开贴近入口的动作菜单，菜单关闭后恢复入口/分支/文件上下文 | 是 | 同块（`菜单关闭后入口恢复为普通标题栏`）+「菜单五个入口各自的行为」块（`主菜单「Git → 获取」执行取回`、`主菜单「Git → 推送」打开确认对话框且未直接推送`） |
| §5.1 | 终端/设置直接执行且先恢复普通标题栏 | 是 | 同块（`菜单「终端」切换底部终端窗口`、`菜单「设置」打开设置模态窗口`） |
| §5.1 | Esc 关闭内嵌菜单并恢复原标题栏，窗口按钮/树/标签/工具窗口状态不变 | 是 | 同块（`Esc 关闭内嵌菜单` + 关闭前后结构快照比对） |
| §5.1 | 已聚焦、可见、启用的标题栏按钮按 Enter 执行点击动作；**长按不重复切换** | 是 | 「规格 §5.1：长按（键盘自动重复）不重复触发」块（控制：`探针确实送出了 5 次 Enter（含 4 次重复）`；`长按不重复切换工具窗口（最多切换一次）`；`长按后状态与单击一致（已激活入口 → 折叠）`；`长按 Enter 时对话框只执行一次动作`） |
| §5.1 | 悬停/焦点只重绘相关按钮，不打开文件、不查 Git、不重排主框架 | 是 | 「悬停只改外观」块 + 焦点环块 |
| §5.1 | 标题栏右侧**搜索**（放大镜）与**设置**（齿轮）入口必须执行动作 | 是 | 「§5.1：标题栏右侧搜索/设置入口必须真的执行动作」块（`搜索图标打开快速打开浮层并聚焦输入框`、`齿轮图标打开设置窗口`，并断言不再落进 `__augitUnwired*` 未接线兜底） |

### 2.2 §5.2 标签

| 规格出处 | 操作 → 状态转换 | 已实现 | 证据位置 |
| --- | --- | --- | --- |
| §5.2 | 双击或 Enter 打开正式标签 | 是 | 「规格 §5.4 键盘路径」块（`单击树行不加载文档（需 Enter 或双击）`）+「点击改动文件打开差异」块 |
| §5.2 | Changes/历史/搜索单击只改选中，不抢编辑区；已有临时预览标签时后台更新 | 是 | 「规格 §5.2：标签集合」块 + `单击改动文件只更新选中态`、`单击改动文件不创建 Diff` |
| §5.2 | Changes 只保留一个工作区比较标签；双击/Enter/显示 Diff 打开并激活；后续选择持续更新同一标签 | 是 | 「规格 §5.2：工作区比较标签的跟随与解除跟随」块（`双击改动文件建立比较标签`、`比较标签始终只有一个`、`单击改动行跟随更新比较标签`、`跟随复用同一个比较标签（不新建）`、`复用比较标签时同步目标路径`） |
| §5.2 | 普通文档前台时只后台更新比较，不抢焦点 | 是 | 「规格 §5.2：普通文档在前台时只后台更新比较，不抢占焦点」块（`后台跟随不改变前台视图类型`、`后台跟随不把差异画到前台正文`） |
| §5.2 | 关闭比较后解除跟随；再次明确打开才重建 | 是 | 「跟随与解除跟随」块（`关闭比较标签后解除跟随`、`解除跟随后单击不重建比较标签`、`显式打开可重新创建比较标签`） |
| §5.2 | 关闭叉按下即捕获、移出后松开不误关；关闭后台标签不动前台 | 是 | 「规格 §5.2：关闭叉的按下即捕获，以及关闭后台标签的状态保持」块 + 「关闭后台比较只移除目标标签，不抢前台焦点」块 |
| §5.2 | 关闭比较的完整取消语义（在途请求失效、晚到不写回） | 是 | 「规格 §5.2：关闭比较标签的完整取消语义」块（`关闭比较后晚到响应不写回差异`、`关闭后的晚到收尾从抢走前台`） |
| §5.2 | 关闭比较保留 Changes 选中行、勾选、草稿与滚动 | 是 | 「规格 §5.2：关闭比较保留 Selected 行、勾选、草稿与滚动」块 |
| §5.2 | 外部文件变化保持标签顺序与当前标签；文件不存在则移除标签 | 是 | 「规格 §5.2：外部变化保持标签顺序与当前标签」块 |
| §5.2 | `Ctrl+W` 关闭当前标签并激活相邻标签 | 是 | 「规格 §5.4：固定快捷键」块（`关闭后激活相邻标签`）+ 组词期间不关闭标签 |

### 2.3 §5.3 弹层与对话框

| 规格出处 | 操作 → 状态转换 | 已实现 | 证据位置 |
| --- | --- | --- | --- |
| §5.3 | Esc 只关闭最上层弹层，不关闭其下方工具窗口 | 是 | 「规格 §5.3：Esc 关闭最上层弹层，不关闭其下方工具窗口」块（`Esc 关闭弹层`、`Esc 不关闭下方工具窗口`、`Esc 关闭弹层后主界面结构保持`） |
| §5.3 | 模态对话框打开时背景结构保持可见但不重建 | 是 | 「规格 §5.3：模态窗口期间背景禁用，关闭后恢复」块（`模态窗口打开期间背景被禁用`、`大型模态窗口同样禁用背景`、`关闭模态后背景恢复可用`、`大型模态关闭后背景恢复`） |
| §5.3 | 取消后焦点回到打开前元素；确认后回到触发区域或结果区域 | 是 | 「规格 §5.3：对话框取消后恢复打开前焦点」块（`取消后焦点回到打开前的树行`、`取消后焦点回到触发按钮`、`Worktree 取消后焦点回到打开前元素`、`保存后焦点回到触发区域`） |
| §5.3 | 紧凑输入窗口：打开即聚焦；Tab/Shift+Tab 在输入框→取消→确定→关闭间循环 | 是 | 「规格 §5.3：新建 / 重命名使用紧凑单行输入窗口」块（`紧凑输入窗口打开即聚焦输入框`、`Tab 按输入框→取消→确定→关闭→输入框循环`、`Shift+Tab 反向循环`） |
| §5.3 | 输入框/确定上 Enter 确认；取消/关闭上 Enter、Esc、标题栏关闭都取消 | 是 | 同块（`输入框 Enter 确认并调用分支接口`、`Esc 取消不调用接口`、`Esc 关闭窗口`） |
| §5.3 | 组词期间 Enter/Esc 交给输入法 | 是 | 「规格 §5.3：组词期间不抢占按键」块（`组词中的 Enter/Esc 不提交也不关闭紧凑窗口`、`对照：非组词时 Esc 会关闭紧凑窗口`、`组词中的 Ctrl+P 不打开快速打开`）+「产品规格 §3.3：固定快捷键」块（`组词期间 Ctrl+Shift+F 不打开全仓搜索`、`组词期间 Ctrl+W 不关闭标签`） |
| §5.3 | 模态打开期间主窗口禁用，关闭后恢复先前状态 | 是 | 「模态窗口期间背景禁用」块（`titlebarInert`/`mainInert`/`inert.length` 前后断言） |

### 2.4 §5.4 焦点与键盘

| 规格出处 | 操作 → 状态转换 | 已实现 | 证据位置 |
| --- | --- | --- | --- |
| §5.4 | 工具栏/树/列表/标签/输入框/正文都可用键盘访问 | 是 | 「规格 §5.4：Tab 在当前区域内按视觉顺序移动焦点」块（`Tab 可移动焦点`、`区域内 Tab 顺序符合视觉位置`、`左侧工具入口先于侧栏内容`） |
| §5.4 | 树方向键移动选择与焦点；右键/菜单键打开上下文菜单；Enter 执行默认动作 | 是 | 「规格 §5.4：树的键盘导航」块（`树支持方向键移动选择`、`方向键可连续移动且焦点跟随`）+「规格 §5.4：树用右键或菜单键打开上下文菜单」块 |
| §5.4 | 列表选择只更新选中；已有预览或 Enter/双击/显示 Diff 才请求预览；同一项重复选择不重复加载 | 是 | 「规格 §12.2：diff 请求去重与过期结果丢弃」块（`首次双击只产生一个 diff 请求`、`同一文件重复双击不重复请求`） |
| §5.4 | 不把 PyCharm 的快捷键原样带入 | 是 | 「规格 §5.4：不得把 PyCharm 的快捷键原样带入（反向断言）」块 |
| §5.4 + §4.4 | 键盘焦点有可见焦点环；失焦后选中态转中性灰 | 是 | 「规格 §4.4 / §5.4：键盘焦点必须有可见焦点环」块（6 条） |

### 2.5 §6 异步加载与刷新不变量

| 规格出处 | 操作 → 状态转换 | 已实现 | 证据位置 |
| --- | --- | --- | --- |
| §6.1 | 异步只更新最小区域；不清空主区域重建；加载期间主框架/工具窗口/标签/分隔位置不变 | 是 | 「规格 §12.2：加载前后工具窗口位置不变」块 + 「规格 §6.1：加载期间主框架与其它区域位置不变」块（`加载期间编辑工作区未被隐藏`、`加载期间左侧工具窗口仍可见`） |
| §6.1 | 异步数据到达不得打断用户输入 | 是 | 「规格 §6.1：异步数据到达不得打断用户输入」块 |
| §6.2 | 快照相等：不重建列表、不重设选中、不重算 diff、不改变工具窗口大小、不触发布局 | 是 | 「规格 §12.2 / §12.4：快照相等不更新」块（`快照相等时十次应用不触发界面更新`、`快照相等不改变工具窗口大小与布局`） |
| §6.2 | 快照必须含操作会话类型与选中标识 | 是 | 「规格 §6.2：快照必须包含"操作会话类型"与"选中标识"」块 |
| §6.3 | 请求键相同直接复用；单双栏只重排版不重查 Git；重复点击当前模式不触发加载 | 是 | 「规格 §6.3：显示模式切换复用补丁；关闭释放」块（`双栏模式产生一次请求`、`补丁缓存只有一份（单双栏共用）`、`切换模式后差异仍在`、`关闭 Diff 后释放补丁缓存`） |
| §6.3 | 新请求保留旧正文或轻量占位，不清空其他区域 | 是 | 「规格 §6.5：首次打开差异时也要在文件标题行提示加载」块 |
| §6.3 | 递增版本号；旧响应晚到必须丢弃；快速连选只显示最后一次 | 是 | 「规格 §12.2：快速连续选择三个文件，最终结果属于最后一个」块 + 「异步竞态：晚到的旧响应必须被丢弃」块 |
| §6.3 | 改选后的在途请求被无变化通知复用时不得失效 | 是 | 「规格 §6.3：在途 Diff 期间的无变化状态通知必须复用该请求」块 |
| §6.3 | 失败在当前 diff 区说明并保留列表与选择 | 是 | 「规格 §9.1：Diff 状态机」块（失败分支）+ §10.2 错误三要素块 |
| §6.3 | 从普通文件返回已加载 Diff 时恢复正文或摘要 | 是 | 「规格 §6.3：从普通文件返回已加载的 Diff 时恢复正文」块 |
| §6.3 | Git 文件列表刷新不等待慢 Diff | 是 | 「规格 §6.3：Git 文件列表刷新不得等待慢 Diff」块 |
| §6.4 | 未变化项保留原对象；部分变化增量更新并保持展开与滚动；选中不存在时选同组最近邻 | 是 | 「规格 §6.4：列表更新」块 + 「§6.4 条款一/二/三：保留原列表项、增量更新、最近邻选择」块 |
| §6.4 | 刷新期间不自动勾选/取消用户提交复选 | 是 | 「规格 §5.2：关闭比较保留 Selected 行、勾选、草稿与滚动」+ 列表更新块 |
| §6.5 | <150ms 不显示加载动画；超过后只在该内容区提示 | 是 | 「规格 §6.5：低于 150 毫秒不显示加载动画」块（`150 毫秒内不显示加载动画`、`加载完成后不残留加载提示`） |
| §6.5 | 有旧正文时在文件标题行提示；最终说明（二进制/超限/无差异/错误）不被收尾隐藏 | 是 | 「规格 §6.5：最终说明必须持续可见，不能被加载指示的收尾隐藏」块 |
| §6.5 | 加载指示不得循环触发布局 | 是 | 「规格 §6.5：加载指示不得循环触发布局」块 |
| §6.6 | 状态所有权表：每类状态只更新允许区域，明确禁止变化项 | 是 | 「规格 §6.6：状态所有权——「明确禁止变化」列」块 |
| §6.7 | 读取未完成时不建立标签；被取代的读取不留下视图；乱序只显示最后选择 | 是 | 「规格 §6.7：读取尚未完成时不建立标签，被取代的读取不得留下视图」块（`读取途中不建立标签、不替换正文`、`被取代的读取晚到后不创建视图`） |
| §6.7 | 启动恢复只激活一次；恢复收尾不得重选树或覆盖输入 | 是 | 「规格 §6.7：启动恢复上次打开的标签，且只激活一次」块 + 「会话数据写回；内容未变时不重复写盘」块 |

### 2.6 §7 页面规格（交互部分）

| 规格出处 | 操作 → 状态转换 | 已实现 | 证据位置 |
| --- | --- | --- | --- |
| §7.6 | 提交只提交勾选文件；进行中禁用重复触发并显示取消 | 是 | 「规格 §7.6：提交只提交勾选文件」块 + 「§9.3：Git 写操作状态机」块 |
| §7.6 | 提交并推送两步都真实执行，失败要区分"提交成功、推送失败" | 是 | 「提交并推送：两步都要真实执行」块 |
| §7.8 | 变化文件右键菜单与文件历史入口 | 是 | 「规格 §7.8：变化文件右键菜单，以及文件历史入口」块 |
| §7.8 | 历史比较标签标注双方引用，打开后成为前台 | 是 | 「规格 §7.8：历史比较标签」块（`双击变化文件建立历史比较标签`、`历史比较标签标注双方引用`、`历史比较打开后成为前台并显示差异`） |
| §7.9 | 引用比较按引用基准生成差异，且解除对 Changes 的跟随 | 是 | 「规格 §7.9：与工作区比较」块 + 「引用比较解除对 Changes 的跟随」块 |
| §7.11 | Stash 创建/管理、Worktree 管理、远端管理的动作行与守卫 | 是 | 「规格 §7.11 / §10.4：Stash 管理页」块、「Worktree 管理页」块、「远端管理页的删除/保存动作行」块、「新建 Worktree 表单」块 |
| §7.17 | 设置为模态对话框：左侧搜索+分类，右侧当前分类；四个分类各有内容 | 是 | 「§7.17：设置窗口按分类分页」块（10 条：`四个分类入口与分组表头`、`默认停在"外观"且右页只显示外观字段`、`点击"文件查看"真正切页且右页只显示该分类`、`切页保留未保存编辑`、`跨分类的改动一起保存`、`搜索框按分类名过滤`、`清空搜索后分类恢复`、`Git 分类显示检测结果与最低版本说明`、`终端自定义命令仅在选择"自定义命令"时可编辑`、`终端自定义命令写入宿主`）；真机四页截图 `artifacts/settings-pages-20260919/` | **默认换行**为规范冲突（`product-spec` 未定义该设置、宿主无字段）→ 见 §3.2 |
| §7.11 | Reset 紧凑对话框：模式影响说明、Hard 红色确认、字号/限高适配 | 是 | 「§7.11 / §10.4 / §9.3：Reset 在实时外壳中可达」块：入口 `Git 菜单 → Reset 当前分支…`；影响说明用**当前真实已跟踪改动数**；Hard 用危险确认（`确认 Reset Hard`）；确认发出一次 `git/reset`（带目标与模式）→ 成功关闭并重读状态；失败保留对话框与目标并给出原因；取消零请求；进行中禁用重复触发 + 取消入口走 `write/cancel` | 见 §3.2 说明（本项由本节新接线） |
| §7.12 | Push 确认对话框、错误反馈、生命周期 | 是 | 「规格 §7.12：Push 内嵌的远端窗口」块 + 「Push 对话框」块 |
| §7.13 | 冲突操作会话（Continue/Skip/Abort）与打开三栏解决器 | 是 | 「规格 §7.13/§10.3：冲突操作会话」块 + 「点击冲突文件打开三栏冲突解决器」块 |
| §7.14 | 接受左/两/右侧是结果区一次可撤销编辑；进行中冻结；失败保留正文并显示原因 | 是 | 「规格 §7.14：接受左侧/两侧/右侧是结果区的一次可撤销编辑」块 + 「应用进行中冻结、只读与忙碌提示」块 |
| §7.14 | 上一处/下一处与当前块；大字号与窄窗口排布；二进制/非法 UTF-8/超限只能整侧接受 | 是 | 「§7.14：上一处/下一处与当前块」块、「§7.14：大字号 / 窄窗口下的标题行排布」块、「§7.14：二进制 / 非法 UTF-8 / 超限文件只能整侧接受」块 |
| §7.16 | 终端按需单会话：运行时点入口即建会话、能输入、有输出、关闭需前台命令确认、隐藏保留会话 | 是 | 「§7.16：终端按需单会话」块（8 条：`运行时点终端入口真的建立会话（挂载 xterm 且只 start 一次）`、`终端轮询不堆积在途请求（并发上限 1）`、`终端输入经宿主写入`、`终端正文显示宿主输出`、`无前台命令时关闭终端：直接结束会话并收起（不弹确认）`、`有前台命令时先确认且未结束会话`、`取消后保留终端`、`确认后结束会话并收起`、`隐藏只收起面板、同一会话与正文保留`）；真机复探：xterm 挂载、7 行真实 banner、`hung=false` |
| §7.18 | Git 不可用时保留文件浏览并只提示一次 | 是 | 「规格 §7.18：Git 不可用时保留文件浏览，只提示一次」块 |

**§7 逐条展开（按小节推进）**：上表是"每项一条"的概览；下面是逐条展开，
本轮先做**证据最全**的 §7.13 与 §7.17。各小节的分母：7.1:9、7.2:16、7.3:13、7.4:9、7.5:10、
7.6:14、7.7:14、7.8:22、7.9:23、7.10:7、7.11:11、7.12:14、7.13:5、7.14:11、7.15:10、7.16:11、
7.17:7、7.18:4（合计 210，脚本按 `## 7.`–`## 8.` 区间统计）。

**§7.13 冲突操作会话（5 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.13 | 操作会话窗口显示操作类型、当前步骤、冲突文件和实际可用动作 | 是 | `冲突后自动进入操作会话，显示类型/步骤/冲突文件`（含标题 `Rebase 冲突`、`当前步骤 2/4`、冲突路径列表、动作集合） |
| 2 | §7.13 | Continue、Skip、Abort 只按本机 Git 当前状态显示；无效动作**直接不显示**而不是仅禁用 | 是 | `§10.3 宿主不适用的会话动作被隐藏而不是禁用占位` + 负向对照 `§10.3 负向对照：宿主提供的动作确实会渲染出来` |
| 3 | §7.13 | Continue 前置未满足时可保留按钮但禁用，并在附近说明仍有多少冲突未解决 | 是 | `Continue 前置未满足时保留并禁用且说明原因（§7.13）`（断言 `还有 2 个冲突未解决` 文案与 `continueDisabled === true`） |
| 4 | §7.13 | 点击冲突文件打开三栏冲突解决器 | 是 | `点击冲突文件打开三栏解决器`（断言标题、`__conflictLoads` 含路径、三栏、结果区 `plaintext-only`、返回与保存入口） |
| 5 | §7.13 | 外部工具解决文件后列表在 500 毫秒内增量更新，已消失的冲突项不保留 | 未覆盖 | 只找到"外部改变**当前差异文件**会重新请求"这类 diff 侧断言；**没有**针对"外部解决冲突后会话列表在 500ms 内增量更新"的断言 —— 需要构造"冲突数从 2 变 1"的宿主推送并断言列表行数与耗时；**宿主侧已有单测**：`GitConflictServiceTests.外部工具标记已解决后操作会话立即读取最新状态`（"立即读取最新状态"）；界面侧 500ms 增量更新仍无断言 |

**§7.17 设置（7 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.17 | 设置为模态对话框，左侧为搜索和分类，右侧为当前分类 | 是 | `§7.17 设置窗口有四个分类入口与分组表头`、`§7.17 点击"文件查看"真正切页且右页只显示该分类`、`§7.17 默认停在"外观"且右页只显示外观字段` |
| 2 | §7.17 | 首版分类只包含外观与行为、文件查看、Git 和终端 | 是 | 同块：断言分类行**严格等于** `['外观','文件查看','Git','终端']` |
| 3 | §7.17 | 外观支持跟随 Windows／浅色／深色；主题**立即预览**，取消后恢复原设置 | 部分 | 三种取值与"窗口显示真实主题"有断言（`设置窗口显示真实主题`），但"立即预览"与"取消后恢复原设置"**没有断言** |
| 4 | §7.17 | 文件查看提供正文字体、等宽字体、字号和默认换行等产品规格内设置 | 部分 | 等宽字体/字号有断言（`切页保留未保存编辑`、`跨分类的改动一起保存`）；**默认换行按用户 2026-09-19 裁决不实现该持久化行**（只保留正文查看时的自动换行），裁决见 §3.2 第 2 条；正文字体字段随分类存在（`hasUiFontSize`/`hasCodeFontSize` 断言） |
| 5 | §7.17 | Git 提供 `git.exe` 路径、检测结果和最低版本说明 | 是 | `§7.17 Git 分类显示检测结果与最低版本说明`（断言路径、版本、`2.40` 说明与检测调用次数） |
| 6 | §7.17 | 终端提供 Shell 类型和自定义启动命令 | 是 | `§7.17 终端自定义命令仅在选择"自定义命令"时可编辑`、`§7.17 终端自定义命令写入宿主`、`设置窗口显示真实 Shell` |
| 7 | §7.17 | 不显示插件、市场、键位、解释器、构建、调试和 AI 设置 | 部分 | 第 2 条"分类严格等于四项"在结构上排除了这些分类，但**没有**像 §10.3 那样对这些词做清单式断言（可复用同款扫描，成本低） |

**§7.1 项目与文件查看（9 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.1 | 初始进入工作区时立即显示标题栏、全局工具栏、项目窗口、标签条和状态栏 | 是 | `标题栏高度为 44`、`全局工具栏宽度为 42`、`§4.2 窄窗口下状态栏完整可见`、`Git 不可用时项目树仍可用`（五件套都存在） |
| 2 | §7.1 | 恢复标签时只创建当前活动标签的文件视图，其他标签先显示名称和状态 | 部分 | 有恢复块（`显式文档参数优先于会话恢复`、`恢复标签集合`）；**"其他标签只显示名称与状态、不建文件视图"没有断言** |
| 3 | §7.1 | 项目树单击仅选择，双击或 `Enter` 打开正式标签 | 是 | `单击树行不加载文档（需 Enter 或双击）` + `打开文件后树仍保持展开` |
| 4 | §7.1 | 树悬停独立维护、只重绘命中行、选中优先、移出/隐藏清除、滚动与目录更新后重新命中 | 部分 | 与 §7.6 同一族的悬停断言（`真实悬停改变行背景`、`悬停不触发宿主查询`、`选中态优先于悬停`）；**树侧"移出/隐藏清除""滚动后重新命中"没有断言** |
| 5 | §7.1 | 展开目录只读取一级子项，显示局部加载状态，不锁住其他区域 | 是 | `展开后出现子项`、`展开目录后树变长`、`展开状态在重绘后保留`；"局部加载状态"由 `目录懒加载` 断言族覆盖 |
| 6 | §7.1 | 文件树刷新保持展开、选择和滚动位置 | 是 | `展开状态在重绘后保留`、`替换编辑区不影响树展开状态`、`打开文件后树仍保持展开`（+ §6 的滚动保持块） |
| 7 | §7.1 | F5 与外部目录变化都增量更新已加载目录；未变化项及其子树保持身份；大目录分批；旧任务不恢复旧展开/选择，也不向新工作区插节点 | 部分 | 有外部变化与竞态块（`外部变化保持当前标签` 等）；**"节点身份保持""分批应用""旧任务不向新工作区插节点"没有断言** |
| 8 | §7.1 | 文件树右键菜单只显示产品规格已经实现的动作 | 部分 | `项目树菜单条目非空且不是未接线兜底` 覆盖"已接线"；**没有"只显示已实现动作"的清单式断言**（可复用 §10.3 的写法） |
| 9 | §7.1 | 打开底部 Git 历史时项目树与文件正文保持可见 | 部分 | `终端与 Git 历史互斥`、`§9.4 终端与 Git 历史互斥` 覆盖底部唯一性；**"树与正文仍可见"没有断言** |

**§7.2 普通文本查看（16 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.2 | 文本始终只读，鼠标选择和复制可用 | 是 | `文档状态栏含只读标识`、`正文与输入仍可选中复制` |
| 2 | §7.2 | 行号列固定，不因长行横向滚动消失 | 是 | `行号槽含真实行号`（+ 正文滚动快照断言） |
| 3 | §7.2 | 短文件不保留空白横向范围；长行扩展范围；内容或字体变化重新度量并收回旧偏移 | 部分 | 滚动快照覆盖"内容变化后位置保持"；**"短文件不保留空白横向范围""收回旧偏移"没有断言** |
| 4 | §7.2 | 工具栏提供自动换行、空白符显示、当前文件搜索和跳转行 | 是 | `Ctrl+F 打开当前文件查找`、`Ctrl+G 打开跳转行`、工具栏结构断言（`自动换行`/`显示空白` 按钮在文档工具栏里） |
| 5 | §7.2 | 文档工具栏按可见顺序参与 Tab/Shift+Tab；Enter/Space 激活；切换显示选项保留按钮焦点 | 部分 | 本轮新增 `§7.2 文档工具栏 Tab 按从左到右只经过可用控件`（路径文本 `tabindex=false`、x 单调递增、经过的标签全在"可用控件"集合内、禁用项不入列）与 `§7.2 切换显示选项保留按钮焦点`（点"自动换行"后焦点仍在它上面）；**"Enter/Space 激活"仍未断言**，且比较/终端工具栏（§7.7 第 5、§7.16 第 9 条）还没做同样核对；**本轮已断言**：`§7.2 文档工具栏按可见顺序参与 Tab 且切换显示选项保留按钮` —— 工具条按钮按可见顺序排列（x 非递减、≥3 个）、切换"自动换行"后按钮仍在工具条且焦点留在工具条；**仍未断言**：真实 Tab/Shift+Tab 焦点循环（本轮只用合成 keydown 采集可见顺序，合成事件不移动焦点）与 Enter/Space 激活 |
| 6 | §7.2 | `Ctrl+F` 在正文顶部打开占一行的查找条，不改变标签身份；关闭后恢复原正文区域 | 是 | `Ctrl+F 打开当前文件查找`、`刷新后查找条不重复打开`、`§4.2 前置条件：查找条已打开且焦点在查找输入框` |
| 7 | §7.2 | 搜索条包含普通文本、区分大小写、全字、正则开关，以及结果数量、上一项、下一项和关闭 | 是 | 查找块（`find-state` 系列 + 开关与导航按钮断言） |
| 8 | §7.2 | 有效查询自动定位首个匹配、显示"当前项/总数"、`Enter`/`Shift+Enter` 前后定位、焦点仍在查找框、`Tab` 循环、`Esc` 收起并交回正文、组词期间不抢 Enter/Esc/Tab | 是 | 自动定位/数量/前后定位/Esc 有断言；本轮新增 `§7.2 查找条 Tab 在输入框/开关/导航/关闭之间循环`（Tab 始终停在条内控件、经过 ≥4 个不同控件、若干次后回到输入框）与 `§7.2 组词期间不扫描、Escape/Enter 被抢占`（组词中 Esc/Enter 不关闭条、不导航）。**保留一处精度缺口**：逐项顺序与 DOM 顺序未一一对应，因此没有写"逐项相等"的判据 |
| 9 | §7.2 | 组词开始使未完成查询失效；组词中不扫描不移动、清旧数量；结束后按最终文字查询一次；取消组词复用已完成结果 | 部分 | 本轮新增两条：`组词期间不扫描`（compositionstart 后输入不扫描、状态清空）与 `组词结束后按最终文字重新统计`（compositionend 后状态重新出现）；**"取消组词回到原查询时复用已完成数量"仍未断言**；**已有断言（3/4）**：`§7.2 组词期间不扫描、Escape/Enter 被抢占`、`§7.2 组词结束后按最终文字重新统计`、`组词期间 Ctrl+W/Ctrl+P/Ctrl+Shift+F/Ctrl+G 不触发`、`组词中的 Enter/Esc 不提交也不关闭紧凑窗口`（含非组词对照）；**未断言**：取消组词后复用已完成结果；**第 81 轮新增断言**：`§7.2 取消组词后复用已完成结果（不重复查询），换新词仍会查` —— 实测 `base=1 → 取消组词后仍为 1 → 换新词为 2`（**含非空对照**，避免"没增加"是假通过）；仍未单独断言的是"组词开始使未完成查询失效"（现由"组词期间不扫描"间接覆盖） |
| 10 | §7.2 | 前后切换从当前匹配边界继续；零宽正则在首尾循环；无结果显示 `0/0`；无效正则或超时在查找条内说明原因 | 是 | `find-state=invalid`/`no-match` 场景 + 查找块断言（含 `0/0` 与原因文案） |
| 11 | §7.2 | 计数与定位采用同一大小写与全字边界规则；视觉稿的 12 项/3 项必须由样本算出，不能写死 | 部分 | 开关行为有断言；**"12 项（忽略大小写）/3 项（全字）"的数值核对没有断言**（视觉稿侧是固定样本文本） |
| 12 | §7.2 | 查找条采用正文顶部占行布局；输入后自动定位首项并显示当前项/总数；`Enter`/`Shift+Enter` 前后定位；`Esc` 收起并保留位置 | 是 | 视觉基线 `text-viewer`（含 `find-state` 变体）+ 上述查找断言 |
| 13 | §7.2 | 大文档与正则的计数、定位在后台执行；低于 150ms 不显示加载；超过显示"正在搜索…"；换查询/开关/正文/模式取消旧任务与方向队列；隐藏/关闭使旧结果失效 | 部分 | `find-state=loading` 页存在并有像素基线；**150ms 阈值、旧任务与方向队列取消、隐藏/关闭失效都没有断言** |
| 14 | §7.2 | 查找条按实际字高扩展；修改字体后保留查询、输入选择和焦点 | 未覆盖 | **实测缺口（第 67 轮）**：`--augit-find-height: 42px`（`mockup.css:48`，视觉稿与实时外壳**共用同一令牌**），`code-font-size` 13 → 查找条 42 / 输入框 30，19 → **仍是 42 / 30**，即"按实际字高扩展"没有实现；harness 保留一条**钉住断言**（`§7.2 缺口钉住：查找条高度不随字号变化…`），并按纪律标为**待用户裁决**（规格文本 vs 共用基线的固定令牌），见 §3.2 第 19 条 |
| 15 | §7.2 | 文件外部变化后尽量恢复滚动位置和选择；被删除时替换为删除状态页 | 是 | 外部变化块（`外部改变当前差异文件会重新请求` 等）+ 删除文件的断言（`文件已删除则移除其标签`） |
| 16 | §7.2 | 同类型外部更新复用正文控件，保留选择方向、横纵滚动、焦点与开关；变短时限制范围；选择端点不得停在 UTF-8 字符内部；查找保持并继续 | 部分 | 滚动/焦点保持有断言；**"选择端点不在 UTF-8 字符内部""查找结果数更新与继续定位"没有断言** |

**§7.9 文件历史、Blame 与引用比较（23 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.9 | 文件历史复用 Git 历史工具窗口，路径筛选固定为当前文件并显示清除入口 | 部分 | `文件历史标签显示真实路径`、`文件历史显示真实提交`、`文件历史不残留样例` 覆盖"复用 + 固定路径"；**清除入口本身没有断言** |
| 2 | §7.9 | 提交列表左到右显示作者、日期、标题，不用提交图与右置元信息；窄栏自身横向滚动；选择/上下键/分页/返回沿用同一提交身份 | 部分 | `文件历史首行为最新提交` 覆盖列表内容；**列布局、窄栏横向滚动、"不因列布局改变而重查"没有断言** |
| 3 | §7.9 | 进入文件历史时保存日志上下文（组合筛选、未执行输入、页码、选择、纵横滚动、折叠/选择/顶部、详情显隐、正文位置） | 部分 | 有"上下文/草稿保持"类断言族；**未逐项断言（尤其"未执行的筛选输入"与"正文位置"）** |
| 4 | §7.9 | 从文件树首次进入时日志可能尚未查询；返回后按原筛选补查，不把初始化空列表当最终空结果；已加载的空日志直接恢复不重复查询 | 部分 | 空历史有断言（`§10.1 无历史…`）；**"返回后按原筛选补查""空日志不重复查询"没有断言** |
| 5 | §7.9 | 文件历史预览按提交与路径复用查询；相同快照不重写正文/不改位置；改选立即取消旧预览；隐藏/清除/销毁取消未完成查询且晚到不覆盖 | 部分 | 有"晚到不覆盖"断言族可类比（§6）；**"改选立即取消旧预览""隐藏时取消"没有直接断言** |
| 6 | §7.9 | 右侧复用完整只读比较视图（工具栏上一处/下一处/查找/计数/忽略空白/双栏/单栏/设置），文件栏显示父版本·提交版本·路径，正文与引用比较同构 | 部分 | 比较视图与文件栏有像素基线（`file-history` 0.63）；**工具栏顺序与"与引用比较同构"没有断言** |
| 7 | §7.9 | 点"日志"或清除入口恢复进入前上下文；隐藏详情取消未完成查询、重显补查；已完成比较保留正文与位置；Tab 顺序限定在工具窗口内 | 部分 | 有隐藏/恢复类断言（`§7.16` 同族）；**Tab 顺序与"重显补查"没有断言** |
| 8 | §7.9 | Blame 在普通文本左侧增加作者与提交信息列，不改变只读属性 | 是 | `Blame 正文为真实文件内容`、`Blame 不残留样例归属` + 只读标识断言 |
| 9 | §7.9 | 归属边栏分日期/作者摘要/行号三列；与正文同行高、纵向同步、横向不动；字号变化按字宽扩展；作者列内省略；点击用完整哈希 | 部分 | `Blame 槽位含真实日期与作者`、`Blame 行数与真实归属一致` 覆盖三列与同步；**字号扩展、横向滚动不动、完整哈希点击没有断言** |
| 10 | §7.9 | Blame 未完成时切换文件/比较/关闭/隐藏/切工作区/销毁使旧请求失效；只接纳最后一次；旧结果不覆盖新状态 | 部分 | `缺 path 的 blame 载荷不进入状态` 是其中一条；**"只接纳最后一次"的乱序用例没有断言** |
| 11 | §7.9 | Blame 顶部右侧"n 行归属"与关闭入口；隐藏普通文档动作；Tab 可达关闭，Enter/Space 关闭后恢复工具栏、保留原文与位置并交回焦点 | 部分 | 结构在视觉稿与实现里；**Tab/Enter/Space 与"恢复工具栏/交回焦点"没有断言**；**已断言**：`§7.9 Blame 头部含"n 行归属"与关闭入口，Enter 可关闭`（头部文本含"行归属"、`aria-label="关闭 Blame"` 在、聚焦后 Enter 使该入口消失）；**未断言**：Tab 可达关闭、Space、关闭后恢复工具栏/保留原文与位置/交回焦点、隐藏普通文档动作 |
| 12 | §7.9 | 点击 Blame 提交定位 Git 历史并选择对应提交 | 未覆盖 | 没有断言（需要"从 Blame 点提交 → 历史选中该提交"的用例） |
| 13 | §7.9 | 从文件历史中的 Blame 定位提交时恢复日志布局、解除旧路径限定与预览请求；异步完成不抢焦点、不恢复旧页 | 未覆盖 | 同上，没有断言 |
| 14 | §7.9 | 引用比较复用 diff 编辑标签，并在标签和文件栏明确显示双方引用 | 是 | `历史比较标签标注双方引用`、`复用比较标签时同步标签文字`、`与工作区比较建立比较标签` |
| 15 | §7.9 | 文件信息沿用双栏左右/单栏上下结构；仍显示旧正文时文件栏保留旧布局，最终模式就绪一起切换；失败/取消/摘要按当前模式显示双方身份 | 部分 | 双栏/单栏结构有像素基线（`commit-diff`/`git-compare`）；**"旧正文期间保留布局""失败/摘要按模式显示身份"没有断言** |
| 16 | §7.9 | 比较工具栏与文件信息区按实际行高扩展；字号变大只增高这两行并下移正文，图标与命中区不变；窄宽度保留按钮顺序 | 部分 | 有字号适配块（覆盖工具栏）；**"只增高两行并下移正文""窄宽度保留顺序"没有断言** |
| 17 | §7.9 | 比较标签先文件名再来源与目标引用，三部分独立省略；40/64 位哈希显示前 8 位并保留 `^`/`~` 后缀；命名引用保持原名；文件栏悬停保留完整引用与路径 | 部分 | 标签文字有断言（`复用比较标签时同步标签文字`）；**三部分独立省略、前 8 位与后缀、悬停完整引用没有断言** |
| 18 | §7.9 | 历史与引用比较遵循 §6 加载规则：重复点当前模式不排版不查询；查询期间切模式只改最终呈现；忽略空白以最后选项为准，旧结果不覆盖新比较 | 部分 | `§7.9 历史比较无差异用比较措辞` + §6 晚到不覆盖族；**"重复点击不查询""忽略空白以最后为准"没有断言** |
| 19 | §7.9 | 差异总数在比较工具栏显示（不覆盖全局状态栏）；箭头定位后保留按钮焦点、可继续 Enter/Space；Tab 按可见顺序经工具栏与正文再回标签栏 | 部分 | 计数与导航在视觉稿与实现里；**焦点保留与 Tab 顺序没有断言**（与 §7.2#5/§7.16#9 同族缺口） |
| 20 | §7.9 | 历史面板发起引用查询时在自身区域显示"取消比较"，不写全局"正在生成 diff"；完成后隐藏取消入口 | 部分 | 工具窗有像素基线；**"取消比较"入口的出现/隐藏与"不写全局提示"没有断言** |
| 21 | §7.9 | 新引用比较取代未完成的旧查询；取消后晚到成功/失败不得显示；关闭比较标签同时取消该位置的文件或引用查询；取消比较不取消无关写操作 | 是 | §6 晚到不覆盖断言族 + `关闭比较标签后解除跟随`、`解除跟随后单击不重建比较标签` |
| 22 | §7.9 | 摘要与失败页清空上一份正文的变更导航，前后改动与正文搜索禁用但保留入口；失败原因留在比较区域可重试；关闭标签取消查询与排版 | 是 | `§7.9 引用比较无差异时给出比较语义的摘要`、`§10.3 摘要禁用变更导航但保留入口且不渲染差异行`、`历史比较无差异用比较措辞` |
| 23 | §7.9 | 比较对话框只列出分支、标签和提交，不显示平台 API 对象 | 未覆盖 | 该对话框本身未实现（§3.2 已记为"当前产品面不可达"），因此无断言 |

**§7.10 分支与标签（7 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.10 | 点击标题栏当前分支打开非模态弹层，搜索框自动获得焦点 | 是 | `分支芯片打开分支弹层`（含 scrim 与非模态）覆盖"打开"；**搜索框自动聚焦没有断言**；harness `§7.10 点分支芯片开非模态弹层且搜索框自动获得焦点`；**该断言首跑失败并暴露真缺陷**（弹层开了但什么都不聚焦，焦点留在分支芯片 `<a>` 上）→ 已修 `openBranchesPopover()` 聚焦 `input.search-field`，见 §3.2 第 18 条 |
| 2 | §7.10 | 顶部快捷动作按 Update、Commit、Push、新建分支、检出标签或版本排序 | 是 | `弹层含快捷动作`、`弹层已标注的快捷动作数量`、`弹层显示上游引用` |
| 3 | §7.10 | 引用按本地分支、远程分支和标签分组；当前分支醒目标记 | 是 | `分支弹层标出引用类型`、`引用树列出真实分支`、`分支标签来自真实引用` |
| 4 | §7.10 | 选择引用后打开二级动作，不立即执行切换 | 是 | `二级动作引用当前分支`（+ 选择引用后打开二级动作的同块断言） |
| 5 | §7.10 | 创建和重命名使用小型输入对话框；删除、推送和覆盖风险操作显示影响确认 | 部分 | 小型输入对话框有 `组词中的 Enter/Esc 不提交也不关闭紧凑窗口` 等断言族；**分支/标签删除的影响确认断言未落到本行**（危险确认在 §10.4 块里，未按此条复核） |
| 6 | §7.10 | 工作区可能被覆盖时停止普通切换，并提供 Smart Checkout 的影响说明和确认 | 部分 | `smart-checkout` 有独立场景与像素行；**"停止普通切换"+影响说明+确认的断言没有落到本行**；**宿主侧已有两个单测**：`GitOperationServiceTests.SmartCheckout恢复冲突进入解决流程并在完成后删除临时Stash`、`…SmartCheckout可在切换后恢复已跟踪和未跟踪改动并删除临时Stash`；界面侧的影响说明与确认弹层仍无断言 |
| 7 | §7.10 | 不显示 Force Push、GitHub、GitLab、子模块或其他仓库根节点 | 是 | `§10.3 分支弹层不以禁用占位出现不支持的能力` + `不出现被排除的产品入口` |

**§7.15 快速打开与全仓搜索（10 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.15 | `Ctrl+P` 在窗口上方中央打开非模态搜索浮层，输入框自动获得焦点 | 是 | `快速打开浮层默认聚焦输入框`、`组词中的 Ctrl+P 不打开快速打开` |
| 2 | §7.15 | 快速打开只搜索文件名，显示文件名和工作区相对路径，最多 100 项 | 是 | **harness**：`快速打开浮层默认聚焦输入框`、`快速打开显示文件名与路径`、`快速打开显示真实命中`、`方向键移动选择`；**C# 单测**（`tests/Augit.Infrastructure.Tests/RipgrepSearchServiceTests.cs`）：`文件搜索最多返回一百项`、`文件名搜索只匹配文件名且保留最佳一百项顺序`（本轮复跑该类 6/6 通过）——"最多 100 项"与"只搜文件名"由宿主侧单测钉住，界面侧由 harness 钉住 |
| 3 | §7.15 | 空态只保留标题和输入框所需高度；结果出现后浮层向下增长，不预留固定空白 | 是 | `快速打开标题正确`、`快速打开初始无结果` + `quick-open-empty` 像素行 |
| 4 | §7.15 | 上下方向键移动选择，`Enter` 打开，`Esc` 取消并恢复原焦点 | 部分 | `Enter 打开临时预览标签` 覆盖 Enter；**上下键移动与 Esc 恢复原焦点没有断言** |
| 5 | §7.15 | 全仓搜索使用相同浮层骨架，但显示区分大小写、全字、正则和包含忽略文件开关 | 是 | `全仓搜索显示三个开关`、`全仓搜索显示包含忽略文件` |
| 6 | §7.15 | 结果按文件与命中行显示，单击预览，双击或 `Enter` 正式打开并定位 | 是 | `全仓搜索显示命中行号与内容` + §5.4 的预览/打开断言 |
| 7 | §7.15 | 第 1001 项出现时停止搜索并在结果底部显示统一截断提示 | 是 | `search-limited` 场景 + `search-state=limited`（"结果超过 1000 条，已停止搜索…"）断言 |
| 8 | §7.15 | 超时和取消后结束 ripgrep 进程，结果区域保留已完成结果并标记状态 | 部分 | `search-state=timeout` 有像素页与文案断言；**"结束 ripgrep 进程"没有断言**（宿主侧进程回收） |
| 9 | §7.15 | 浮层按实际字高布局：标题 ≥ max(41px, h+20)、输入框 ≥ max(31px, h+8)、结果行 ≥ max(32px, h+8) | 部分 | 浮层有像素基线；**三个下限公式没有断言** |
| 10 | §7.15 | 三个开关用固定 16px 图形、包含忽略文件按字宽；窄宽度按原顺序换行；状态与错误在结果区下方换行、长说明留在只读区 | 部分 | 结构与像素在；**16px 固定、窄宽度换行顺序、状态换行没有断言** |

**§7.18 Git 不可用（4 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.18 | 未找到 Git 或版本过低时保留项目与文件浏览 | 是 | `Git 不可用时项目树仍可用`、`非 Git 目录保留项目树` |
| 2 | §7.18 | 提交和 Git 历史入口显示禁用状态及悬停原因 | 未覆盖 | **实现缺口（第 402 轮实测）**：`git-unavailable` 场景下 5 个 rail 按钮里 `aria-disabled="true"` 的数量为 **0**；代码侧 `aria-disabled` 只用在 commit-actions（`live-data.js:6357-6361`），rail 入口既不禁用也没有悬停原因。harness 保留一条断言把"当前实现"钉住，防止它被误写成通过 |
| 3 | §7.18 | 首次触发 Git 入口时显示局部错误，提供配置 `git.exe` 的设置入口 | 是 | harness `§7.18 Git 不可用时显示局部错误并提供配置 git.exe 入口`（`.toast.error` + 标题「Git 不可用」+ 指向设置的入口） |
| 4 | §7.18 | 不反复弹出错误，不阻塞普通文件查看 | 是 | `非 Git 目录不显示 Git 不可用提示`、`非 Git 目录仍可打开文件`、`非 Git 目录不注入 Git 状态` |

**§7.3 Markdown（13 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.3 | 从文件树打开时默认预览模式；用户切换模式后在当前会话记忆该选择 | 是 | 默认预览由 `Markdown 预览来自真实内容` 覆盖；会话内记忆由本轮断言 `§7.3 会话内记住文档模式、三段式切换不创建标签` 覆盖（切到 `source` → 打开另一个文档 → 切回，模式仍是 `source`，且另一个文档保持自己的 `preview`，互不污染） |
| 2 | §7.3 | 原文、预览和左右对照使用编辑区右上角三段式切换，不创建三个标签 | 是 | 三段式控件在视觉稿与实现里（`document-modes`）；"不创建三个标签"由 `§7.3 会话内记住文档模式、三段式切换不创建标签` 覆盖（三种模式各切一遍后文档标签数不变）；**图标尺寸/图形复原仍无断言（见第 3 条）** |
| 3 | §7.3 | 三段式沿用 PyCharm 紧凑图标控件：26px 按钮步长、36px 工具栏高度；三个图形各自复原；只有当前按钮显示内缩选中底色 | 部分 | 已断言：`§7.3 模式控件 26px 步长/36px 工具栏，§7.4 JSON 复用同一控件`（DOM 实测 5 个段按钮全是 26×26、间距 0、工具栏 36px，两种文档控件垂直对齐一致）+ 像素基线（`markdown-preview` 1.42）+ 选中态断言（`aria-pressed`）。**未断言**：三个图形的形状复原、"只有当前按钮有底色"的样式取值（内缩底色本身只有像素基线） |
| 4 | §7.3 | 左右对照分隔位置允许拖动，模式切换不丢失各自滚动位置 | 是 | `§7.3 拖动分隔条保留按下偏移并按实际宽度重排`（+120px 偏移）、`§7.3 模式切换不丢失各自滚动位置`（原文 180 / 预览 90 往返后逐位相等） |
| 5 | §7.3 | 拖动保留按下偏移、越界停在最小栏宽、重复位置不重排、结束条件与保留比例 | 是 | **四半全部有断言**：`§7.3 拖动分隔条保留按下偏移并按实际宽度重排`（按下偏移 + 重排）、`§7.3 越过边界停在最小栏宽`、`§7.3 重复移动到同一实际位置不重排正文`（含**非空对照**：再移 1px 必须新增写入，否则"没增加"可能是假通过）、`§7.3 拖动中的 Esc 只结束拖动并保留比例`（结束条件 + 保留比例）；另有 `§7.3 分隔条方向键按 2% 步进并可还原` 佐证 —— 第 419 轮曾写"2/4"，属**跨断言漏检**，本轮改正 |
| 6 | §7.3 | 预览加载期间保留原文或上一次预览，只在预览侧显示局部加载状态 | 未覆盖 | `markdown-preview` 有加载态像素页？**无断言** |
| 7 | §7.3 | 隐藏的 Markdown 标签不预热或并发创建预览；切回后才恢复上次模式 | 部分 | 有"后台标签不抢前台"的同类断言族（§6）；**"不预热预览"没有断言** |
| 8 | §7.3 | 相对文件链接在 Augit 标签中打开，外部链接交给系统前保持当前页面 | 是 | 本轮修缺陷后补断言：`§7.3 相对链接按当前文档目录解析、越界一律返回 null`、`§7.3 相对文件链接在 Augit 标签中打开`（点 `api/schema.md` 后活动标签变成 `docs/api/schema.md`）、`§7.3 外部链接交给系统、当前页面不导航`（`external/launch` 收到 `open:https://example.com/docs`，`location.hash` 与锚点 `href` 全程保持 `#`） |
| 9 | §7.3 | 阻止的图片或链接在原位置显示紧凑错误，不使整个预览失败 | 部分 | 链接侧已断言三类阻止（越界/协议不支持/锚点缺失）各自在原 `<p>` 位置替换成 `.markdown-blocked` 并带原因，预览其余 40 个段落与标题完好；**远程图片占位（`renderMarkdown` 的 `markdown-blocked` 分支）没有单独断言** |
| 10 | §7.3 | 三模式切换保留原文选择/预览阅读位置/对照比例；重复点当前模式不重新加载；关闭标签才结束预览会话 | 部分 | 原文滚动与预览阅读位置已断言（`§7.3 模式切换不丢失各自滚动位置`）；**对照比例跨模式保持、重复点当前模式不重新加载、关闭标签才结束预览会话没有断言** |
| 11 | §7.3 | 外部更新复用原文控件、保留选择/滚动/查找/模式；预览只应用最后一次有效渲染；后台标签不因更新创建预览 | 部分 | §6 的外部更新断言族覆盖部分（`外部改变当前差异文件会重新请求`）；**"只应用最后一次渲染"和"后台不建预览"没有断言** |
| 12 | §7.3 | 加载超 150ms 在预览区顶部显示紧凑提示、原文或旧预览继续可见；失败给原因可重试；被阻止链接保留原文与原标签文字 | 未覆盖 | 无断言 |
| 13 | §7.3 | 三模式切换参考 `PY-MARKDOWN-MODES-01`；精确位置保持与异步时序是 Augit 验收约束，不能标为 PyCharm 实测细节 | 部分 | 本条是**口径声明**（不是行为）：文档已按此口径写（§1.3 说明跨框架不判逐像素相等）；但"位置保持/异步时序"的断言缺失（同第 10/11 条） |

**§7.4 JSON（9 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.4 | 原文和格式化使用双段式切换，均只读 | 是 | 两段式控件与 `json-preview` 像素行 + 本轮断言（切换后正文来自真实 `data-json-source`，控件本身不产生可编辑区）；**"均只读"的显式断言仍缺**，但目前没有把该行降级为部分 —— 切换路径已覆盖 |
| 2 | §7.4 | 双段式复用 Markdown 模式控件的尺寸、对齐和状态规则，图形按 PyCharm 图标逐项复原 | 部分 | 已断言：与 Markdown 控件**同一套尺寸与对齐**（同一条几何断言，5 个段按钮逐值 26×26、间距 0、工具栏 36px、垂直中心相同）与禁用态（格式错误时 `disabled` + `title`）。**未断言**：图形（图标本身）按 PyCharm 图标逐项复原 |
| 3 | §7.4 | 格式化结果使用两空格缩进并保留属性顺序 | 是 | 格式化正文由宿主 `JsonDisplayFormatter`（Core 单测 `使用两空格并保持属性顺序`）产生，界面显示宿主的 `formatted` 字段；harness `§7.4 原文显示真实来源、格式化用宿主结果且两空格缩进保序、不写回文件` 用**压缩原文 + 宿主格式化正文**验证界面用的确实是宿主结果（而不是原样显示原文） |
| 4 | §7.4 | 格式错误时默认显示原文，并在顶部显示准确行列与错误文字；点击错误定位对应行 | 是 | harness `§7.4 格式错误默认原文并禁用格式化、错误条给出宿主行列`（`json-invalid` + 模式回落 `source` + 正文为原文 + 错误条文字含宿主给出的"第 4 行…第 19 列"）与 `§7.4 点击错误条定位到宿主给出的出错行并把焦点交给正文` |
| 5 | §7.4 | 错误行列从 1 开始、列按 Unicode 标量计数、不显示字节偏移；错误条可 Tab 到达，Enter/Space 与单击同效；定位后焦点进正文 | 是 | 行列口径由 Core 单测 `错误行列从一开始并按Unicode字符计列` 覆盖（含中文、代理对、CRLF 共 8 组数据）；错误条是原生 `<button>`（harness 断言 `tagName=BUTTON` 且 `tabIndex=0`），`§7.4 错误条 Enter 与单击同效` 覆盖 Enter 与焦点进正文。**Space 未单独断言**（原生 button 的默认动作，与 Enter 同路径） |
| 6 | §7.4 | 格式错误时保留"格式化"按钮位置并禁用；修复后恢复；错误条被外部修复隐藏时焦点回原文，其他焦点不变 | 部分 | 已断言：同一工具栏内保留按钮位置、`disabled` 为真且有 `title` 说明（harness `§7.4 格式错误默认原文并禁用格式化…`）。**未断言**："外部修复后恢复可用"与"错误条被外部修复隐藏时焦点回原文、其他焦点不变" |
| 7 | §7.4 | 切换格式化模式不写回文件，不显示保存按钮 | 是 | harness 在 JSON 文档内断言保存类按钮数为 0，且切换原文/格式化后 `live.document.text` 与切换前逐字相同 |
| 8 | §7.4 | 查找基于当前可见文本；切换模式保留查找词与开关、立即更新结果数、从新文本起点继续；重复点当前模式不重置查找位置；同内容复用结果 | 部分 | `重复点击当前模式不重置查找位置` 有断言（查找块）；**"切换模式后结果数立即更新并从新起点继续"没有断言** |
| 9 | §7.4 | JSON 同类型外部更新复用原文与格式化正文，保留模式/阅读位置/查找；变无效时显示最新原文并更新原因；仅焦点原在格式化正文时才转交 | 部分 | §6 外部更新族部分覆盖；**"变无效时转交原文的焦点规则"没有断言** |

**§7.11 Stash、Reset、Worktree 与远端（11 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.11 | Stash 创建用紧凑模态框：根目录、当前分支、消息、保留索引；创建按钮固定右下 | 部分 | `Stash 管理列出真实贮藏` 覆盖管理侧；**创建框的字段顺序与按钮位置没有单独断言**；**宿主侧已有单测**：`ShellBridgeStashWriteTests.创建Stash带消息并保留索引状态`（"带消息 + 保留索引"）；模态框布局与按钮位置仍无断言 |
| 2 | §7.11 | 创建框按字高容纳输入与动作，受限时只滚表单；消息多行、Enter 换行、Tab 顺序固定 | 部分 | 结构在视觉稿里；**字高容纳、只滚表单、Enter/Tab 行为没有断言** |
| 3 | §7.11 | 查看/恢复 Stash 用双栏管理窗：左侧列表，右侧消息/时间/分支/变化文件；应用·弹出·删除区分 | 是 | `Stash 管理列出真实贮藏`、`Stash 管理不残留样例` + §7.11 管理页块 |
| 4 | §7.11 | Reset 用紧凑动作对话框，不与对象管理窗共用大面积双栏 | 是 | `§7.11 Reset 可从 Git 菜单打开`、`Reset 模板不残留样例哈希`、`Reset 目标提交来自真实 HEAD` |
| 5 | §7.11 | Worktree 与远端用双栏管理窗：左列表、右详情与动作 | 是 | `远端管理列出真实远端`、`远端管理不残留样例`、`新建 Worktree 打开表单` |
| 6 | §7.11 | Reset 模式在同一页解释 HEAD/索引/工作区影响；Hard 用红色确认 | 是 | §10.4 块（`§10.4 Hard 使用危险确认样式且按钮写动作名`、`§10.4 影响说明使用真实已跟踪改动数`） |
| 7 | §7.11 | Reset 按字高量字段/说明/按钮；标题底栏固定，限高只滚表单；聚焦自动滚入；错误不移动整窗 | 部分 | 有字号适配块；**"限高只滚表单""聚焦滚入""错误不移动整窗"没有断言** |
| 8 | §7.11 | Rollback 风险说明按字换行并为未跟踪文件保留回收站说明；先展示真实只读比较再确认 | 是 | `回滚必须显示具体影响并确认`、`未跟踪文件的回滚说明进入回收站`、`已跟踪文件的回滚不显示回收站说明` |
| 9 | §7.11 | Worktree 详情显示分支/路径/干净状态/终端占用；条件不满足时禁用移除并给具体原因 | 是 | Worktree 管理页块 + §10.3 禁用原因断言族 |
| 10 | §7.11 | 远端只管理名称、Fetch URL、Push URL，不提供平台账号或项目选择 | 是 | `远端窗口提供名称与两个 URL 字段` + `不出现被排除的产品入口` |
| 11 | §7.11 | 远端按字高排布；限高只滚右侧详情，工具栏/列表/关闭固定；Tab 滚入被裁切字段；失败说明在详情 | 部分 | `远端保存失败保留窗口`、`远端保存失败显示 Git 原因`、`保存成功后远端窗口留在原地` 覆盖失败与保持；**"限高只滚右侧""Tab 滚入"没有断言** |

**§7.12 Clone、Push 与本地 Git 操作反馈（14 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.12 | Clone 用大型模态窗；表单按版本控制/URL/目录/浅克隆深度垂直排列；不含平台入口 | 是 | `Clone 目标目录用最近目录预填` + `不出现被排除的产品入口` |
| 2 | §7.12 | Clone 初始 URL/目录为空并给占位；浅克隆默认不勾选、深度 1 但禁用；校验不通过不调 Git | 是 | `Clone 深度默认禁用` + `clone` 块（勾选后才启用与正整数校验） |
| 3 | §7.12 | Clone 标题与底栏固定；字号放大按字宽字高排布；浅克隆行放不下时深度与单位换行；错误在正文换行 | 部分 | 有字号适配块；**"浅克隆行换行""错误在正文换行"没有断言** |
| 4 | §7.12 | Clone 初始焦点在 URL；Tab 循环含启用的深度；输入中 Enter 执行、Esc 取消 | 部分 | `clone` 块覆盖部分字段；**Tab 循环与 Enter/Esc 没有断言** |
| 5 | §7.12 | Clone 进行中冻结表单与确认；取消先请求 Git 结束；失败或取消保留输入允许重试；成功返回真实路径并关闭 | 部分 | `clone-result` 有状态断言族；**"取消先请求结束""结束前不能重新提交"没有断言** |
| 6 | §7.12 | Push 对话框显示本地引用、目标远端引用与待推送提交列表 | 是 | `Push 摘要显示真实分支与上游`、`Push 列出真实待推送提交`、`Push 详情显示目标与提交数` |
| 7 | §7.12 | Push 提交行用自绘勾形（非文字字形）；行高按字高；单击只选中不改整体范围；选中优先悬停、失焦保留中性 | 部分 | `Push 列出真实待推送提交` 覆盖列表；**勾形/行高/选中优先悬停没有断言**（`选中态优先于悬停` 已在 §7.6 修复为真跑） |
| 8 | §7.12 | Push 标题与底栏固定；右侧说明与错误在详情内滚动；左侧列表独立滚动；无远端时保留"定义远端"且推送禁用；限高为标签选项预留空间 | 部分 | `没有上游时保留「定义远端」入口`（含出现与保留两条）覆盖无远端分支；**"限高预留标签空间""独立滚动"没有断言** |
| 9 | §7.12 | 预览未完成或无待推送提交时不许推送；进行中阻止重复、取消等待；晚到成功不覆盖取消；失败与取消保留引用/列表/焦点 | 部分 | §9.3 写操作状态机断言族覆盖"进行中阻止重复"；**"预览未完成不许推送""晚到不覆盖取消"没有断言** |
| 10 | §7.12 | "定义远端"打开嵌套远端管理窗；关闭后只重读预览，不建第二个 Push、不改列表/草稿/布局；预览完成后焦点回提交列表 | 是 | `定义远端打开嵌套窗口`、`远端窗口期间 Push 窗口保持唯一`、`保存成功后 Push 窗口仍唯一`、`点击定义远端不跳转页面` |
| 11 | §7.12 | 不显示 Force Push 开关或平台账号入口 | 是 | `不出现被排除的产品入口` + `§10.3 分支弹层不以禁用占位出现不支持的能力` |
| 12 | §7.12 | 操作开始后确认按钮变进行状态并显示取消；不能重复提交同一操作 | 是 | §9.3 写操作状态机断言族（进行态禁用重复触发 + 取消入口） |
| 13 | §7.12 | 失败原因显示在当前对话框或触发区域；关闭后不保留 Git Console 或操作历史 | 是 | §10.2 错误归属断言 + `§10.2 界面上没有 Git Console / 控制台入口` |
| 14 | §7.12 | 图形 Git 命令需要交互输入时直接失败并说明原因，不提供原命令复制或终端重试 | 部分 | 实现有 `InteractiveInputRequired` 失败类型；**"界面给出原因且不提供复制/重试入口"没有断言**；**宿主侧已有单测**：`GitRemoteServiceTests.需要交互输入时返回稳定原因且不暴露原命令`（断言"给稳定原因"与"不暴露原命令"）；界面侧展示原因仍无断言 |

**§7.14 三栏冲突解决器（11 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.14 | 左侧为当前分支，中央为最终结果，右侧为合入内容 | 是 | `三栏标题来自真实分支`、`结果栏含真实冲突标记` |
| 2 | §7.14 | 三栏顶部固定显示来源；中央显示"可编辑"，左右始终只读 | 是 | `结果栏可编辑`（`contenteditable=plaintext-only`）已断言；**左右只读与来源固定没有断言**；harness `§7.14 三栏结构、仅中央可编辑、左右只读`（`.conflict-columns` 恰好 3 栏；全页 `[contenteditable]` 只有 1 个，且落在中间的 `.conflict-column.result`；中文标题含"可编辑"） |
| 3 | §7.14 | 冲突块三栏纵向对齐，提供接受左侧/两侧/右侧 | 是 | `冲突块被标出`、`前置条件：解决器显示 1 个未处理冲突与三种接受动作` |
| 4 | §7.14 | 下一处、上一处与未处理数量固定在顶部 | 部分 | `冲突标题显示真实文件名与冲突数` 覆盖计数；**上一处/下一处按钮的固定与行为没有断言** |
| 5 | §7.14 | 大字号或窄窗口下标题行重排：文件名移到顶部右侧，计数与导航独立一行，来源拆两行 | 部分 | 有 `§7.14：大字号 / 窄窗口下的标题行排布` 块；**该块的具体判据未逐项核对到本行** |
| 6 | §7.14 | 中央有未保存内容且外部文件变化时显示模态选择：重新载入或保留当前内容 | 部分 | 实现里有 `conflictPending` 询问层；**该模态的选择与结果没有断言** |
| 7 | §7.14 | 应用前重新校验文件与 Git 状态；失败时保留中央内容并显示最新原因 | 是 | §7.14 块（失败保留正文并显示原因） |
| 8 | §7.14 | 应用进行中冻结接受/导航/外部刷新/普通关闭；中央只读并显示"正在应用结果并标记已解决…"；失败或取消后按真实冲突数恢复动作 | 是 | `应用进行中冻结、只读与忙碌提示` 块 |
| 9 | §7.14 | 接受左/右/两侧是一次可撤销编辑；Ctrl+Z/Ctrl+Y 恢复或再应用；不清空原有撤销记录 | 是 | `接受左侧/两侧/右侧是结果区的一次可撤销编辑` 块 |
| 10 | §7.14 | 外部读取完成后再次核对窗口/请求/文件版本；读取期间用户编辑过则重新询问；保留时不重写正文/选区/滚动/撤销 | 部分 | 有"晚到不覆盖"断言族可类比；**"编辑过则重新询问""保留时不重写"没有断言** |
| 11 | §7.14 | 二进制、非法 UTF-8 与超限文件不进入三栏正文，改为整侧接受与外部工具页面 | 是 | `§7.14：二进制 / 非法 UTF-8 / 超限文件只能整侧接受` 块 |

**§7.8 Git 历史（22 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.8 | 底部 Git 工具窗口包含左侧竖向工具栏、引用树、提交列表和右侧详情 | 是 | `Git 历史竖向工具栏存在且非空`、`引用树列出真实分支`、`Git 日志显示真实提交`、`提交详情显示真实提交信息` |
| 2 | §7.8 | 字号增大时各部位按字高扩展、图标不变、不重叠；外观应用与窗口收放复用控件并保持选择/锚点/偏移，不触发 Git 查询或重建提交图 | 部分 | `刷新后提交选择不变`、`提交列表节点身份保持（未重建）`、`保存提交了修改后的字号` 覆盖"选择/身份/字号"；**列表顶部锚点、横向偏移、"不触发 Git 查询"没有断言** |
| 3 | §7.8 | 提交详情正文在右下区域独立滚动，不按字符数静默截断；滚轮/上下键/PageUp·Down/Home·End 只移动详情不切换提交；重复选择与收放保持阅读位置；切换提交回顶部；文件历史返回恢复位置；只读；超 20MB 说明原因 | 部分 | 有 `提交详情无页面错误`/`提交详情不残留样例` 与只读结构；**滚动键位只动详情、阅读位置保持、切换提交回顶部、20MB 边界文案都没有断言** |
| 4 | §7.8 | 长说明排版不阻塞选择/切换/关闭；先呈现首段再更新滚动范围；End 记录末尾意图；快速切换只接纳最新详情；字号与宽度改变只重排详情 | 部分 | 有"晚到不覆盖"的同类断言族（§6）；**"首段先呈现""End 意图""字号宽度只重排"没有断言** |
| 5 | §7.8 | "提交详情/文件历史/Blame"操作行按字宽分配，放不下时紧凑布局隐藏该行；变化文件右键菜单保留显示 Diff、文件历史与 Blame，只对文件行提供，Esc 后恢复列表焦点 | 部分 | `变化文件右键菜单与文件历史入口` 块覆盖菜单项与入口；**紧凑布局隐藏、Esc 恢复列表焦点、键盘菜单键没有断言** |
| 6 | §7.8 | 左侧竖向工具栏固定提供返回/新建引用/删除引用/刷新/搜索/比较/定位 HEAD；禁用时保留位置并显示原因 | 是 | `Git 历史竖向工具栏存在且非空`（七项）+ §10.3 的"禁用带原因"断言族 |
| 7 | §7.8 | 短工具窗用右箭头替换放不下的按钮，点击横向弹出整组；弹层复用原动作/图标/禁用状态，支持左右键/Tab/Enter/Space/Esc；隐藏/布局/主题变化关闭；缩小时焦点转到溢出箭头 | 部分 | 结构在视觉稿与实现里；**除 Esc 外的键位、弹出行为、焦点转移都没有断言** |
| 8 | §7.8 | 工具栏按钮用 Enter/Space 执行、焦点显示统一蓝色内框；收纳箭头消失时焦点交给同组最后一个可见可用动作；极短区域不绘制越界按钮 | 部分 | 有焦点环断言；**Enter/Space 执行与焦点交接、极短区域不越界没有断言** |
| 9 | §7.8 | 引用树顶部有"分支或标签"搜索 | 是 | 引用树筛选断言族（§1.1 `git-history` + `筛选栏保留` 类断言） |
| 10 | §7.8 | 提交列表顶部有文本或哈希搜索，以及分支、用户、日期和路径筛选 | 是 | 筛选栏断言族（`log-filterbar` 相关断言与像素行） |
| 11 | §7.8 | 筛选栏右侧"显示/隐藏提交详情"与"搜索提交历史"两个图标入口，各自只做一件事 | 部分 | 入口存在（§12.3 断言有可访问名称）；**"只折叠详情不刷新列表""搜索入口只交焦点给输入框"没有断言** |
| 12 | §7.8 | 窄栏按原顺序把放不下的筛选项收入右箭头菜单，不压细输入框、不移除能力；菜单调用已有筛选动作并交接焦点 | 部分 | 同上：结构在，**收纳行为与焦点交接没有断言** |
| 13 | §7.8 | 引用、作者和日期分别成列、共享文字度量；常规宽度显示本地完整日期与时间，引用含标签图形；窄栏短日期与省略为同构推导 | 部分 | 列布局在视觉稿与像素基线里；**"共享文字度量""不吞掉后续列"没有断言** |
| 14 | §7.8 | 变化文件与项目树/标签/Changes/搜索结果共用文件类型图标；类型色不被 Git 状态色覆盖；辅助技术名称保留状态符号与文件名 | 部分 | 图标复用有断言族；**"状态色不覆盖图标色""辅助技术名称含状态与文件名"没有断言** |
| 15 | §7.8 | 提交图用真实排序与父哈希绘制；普通节点实心圆、HEAD 外环加中心点；分叉合流按父关系展开收敛；缺父提交用短虚线；复杂多轨须单独场景验收 | 是 | `提交图按真实历史行数绘制`、`提交图主题来自宿主`、`提交图哈希来自宿主`、`合并提交产生多条泳道配色` + 独立场景 `git-history-graph`（像素行） |
| 16 | §7.8 | 单击提交立即高亮并异步更新右侧；已有历史比较时提交与文件选择同步更新该比较标签 | 是 | `提交详情显示真实提交信息`、`历史比较标签标注双方引用`、§5.2 的跟随断言族 |
| 17 | §7.8 | 双击变化文件或 Enter 打开并激活提交 diff；已有比较标签沿用；普通文档前台不抢占 | 是 | §5.2/§7.8 块（`双击变化文件建立历史比较标签`、`普通文档前台时只后台更新`、`显式打开可重新创建比较标签`） |
| 18 | §7.8 | 历史比较激活时立即打开标签并显示双方引用与路径；查询完成只填正文不再次激活/抢焦点；超 150ms 显示局部加载与"取消比较"；短查询不闪现；失败与取消保留标签可重试 | 部分 | `历史比较打开后成为前台并显示差异`、`历史比较标签标注双方引用` 已覆盖"立即打开"；**150ms 阈值、短查询不闪现、"取消比较"入口与重试没有断言**；**已有断言（1/4）**：`历史比较文件栏显示双方引用与路径`；**未断言**：立即打开标签、查询完成不再次激活/抢焦点、超时/失败路径 |
| 19 | §7.8 | 历史比较复用进行中的查询；改选文件或提交时原位更新；只接纳最后一次结果；后台更新不抢正文；关闭解除跟随；失败与取消受请求版本保护 | 是 | `跟随复用同一个比较标签（不新建）`、`复用比较标签时同步目标路径`、`关闭比较标签后解除跟随`、`解除跟随后单击不重建比较标签`、§6 的"晚到不覆盖"断言族 |
| 20 | §7.8 | 分页加载在列表底部触发；加载下一页时现有 100 条提交保持可见 | 未覆盖 | 没有分页/滚动触底断言（`docs/bulk-*` 是文件树批量夹具，不是历史分页） |
| 21 | §7.8 | 下一页成功接纳后才更新页码；失败或取消后从同一页重试；查询期间可选可滚可看详情；横向滚动与无关按键不触发；旧页晚到不覆盖 | 部分 | 有"晚到不覆盖"断言族可类比；**页码更新、失败重试、触发条件（横向滚动/无关按键）没有断言** |
| 22 | §7.8 | 多轨导致图形/标题/作者/短日期无法同时容纳时保留可读宽度并局部横向滚动；改变选择/重复点击/相同快照保留横向位置；进出文件历史恢复纵横滚动并归位 | 部分 | `git-history-graph` 有像素基线；**横向位置保持与归位没有断言** |

**§7.5 图片与不可预览文件（10 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.5 | 图片居中显示；工具栏左侧缩小/比例/放大/适应区域，右侧像素尺寸·类型·大小 | 是 | 结构在视觉稿与 `liveImageDocument()` 里都有；`§7.5 初次打开按适应区域显示…` 断言居中与四边边距，比例标签与实测一致（`图片文档把 dataUrl 带到渲染层` 覆盖 dataUrl 搬运） |
| 2 | §7.5 | 透明棋盘格只覆盖图片矩形，外围主题画布纯色；不添加编辑工具 | 部分 | 棋盘格是 `<img>` 自身的 CSS 背景（随图片矩形，天然只覆盖图片范围）；**没有断言**核对范围，"不添加编辑工具"也没有显式断言；**「不添加编辑工具」已有断言**（`§7.5 图片页只读：工具条只有缩放/适应，不含编辑类动作`）；棋盘格覆盖范围仍无断言 |
| 3 | §7.5 | 初次打开与"适应区域"完整显示（四边 ≥32px、不放大小图、极大图可 <10%） | 是 | `image-preview.js` 的 fit：`scale = min(1, (stage-64·dpi)/naturalW, (stage-64·dpi)/naturalH)`，即四边 32·dpi 且上限 1（不放大小图、极大图可低于 10%）；断言 `§7.5 初次打开按适应区域显示：四边各留 ≥32px、不放大…` |
| 4 | §7.5 | 放大后拖动、方向键、滚轮/Shift+滚轮/Ctrl+滚轮、限制边缘、适应居中、仅影响图片、保留按钮焦点、解除拖动 | 部分 | `image-preview.js` 已实现 pointer 拖动 + `setPointerCapture`、方向键 32px、滚轮 / Shift+滚轮 / Ctrl+滚轮、边缘夹取与适应居中、`Escape` 解除拖动；已断言**适应区域重居中**、**向右拖动到边缘被夹取**、**ArrowRight 左移 32px**、**Escape 解除拖动**、**滚轮可见/隐藏**；仍缺**按钮缩放保留焦点**与"操作仅影响图片"的断言 |
| 5 | §7.5 | 高精度滚轮累计到整档才缩放；单次多档合并；切换/按钮/拖动/隐藏时清除未完成输入 | 部分 | 实现里有 `wheelZoom` 余量累计 + `wheelMode` 切换与按钮动作都 `resetWheel()`；已断言"隐藏时不接受滚轮输入、可见时按档位缩放"、**不足一档不缩放**、**累计到整档缩放一档**、**单次多档合并**（`wheelZoom` 余量）；仍缺"切换滚轮模式时清除未完成输入"的断言；**已有断言（2/3）**：`§7.5 Ctrl+滚轮不足一档不缩放、累计到整档缩放、单次多档合并`；**未断言**：切换/按钮/拖动/隐藏时清除未完成输入；**另已断言（第 80/81 轮）**：`§7.5 按钮操作后清除未完成的滚轮输入`（-100 不足一档 → 点"适应区域" → 再 -100，缩放不再变化）；**仍未断言**：`切换/拖动/隐藏` 三种触发下的清除；**第 82 轮再补**：`§7.5 切换动作后清除未完成的滚轮输入`（同样可判别：-100 → 点"适应区域" → -100，缩放不变）；**仍未断言**：`拖动 / 隐藏` 两种触发下的清除 |
| 6 | §7.5 | 同一有效图片的外部更新复用预览、保留手动缩放与位置、适应模式重算；损坏时显示信息页 | 部分 | "损坏/不再支持 → 信息页"有断言（`§10.2 图片解码失败时显示宿主原因而不是破图`、`§10.2 浏览器解不开的图片也给出原因`）；"复用预览窗口 + 保留缩放与位置 + 重新解码"没有断言 |
| 7 | §7.5 | 解码在后台执行；可切标签/输入/关闭；旧请求失效、晚到位图释放；>150ms 只在画布中心显示"正在读取文件…" | 部分 | `image-preview.js` 支持 `?image-state=loading` 并在画布中心渲染 `.image-loading`；已断言加载态 `.image-loading` 文案/`role=status`/**画布中心**/**不改工具栏**；**150ms 阈值与"不抢文档/焦点"没有断言** |
| 8 | §7.5 | 缩小时对相邻像素平滑采样；平滑图后台生成，先快速采样再原位更新 | 部分 | 缩小走浏览器默认的平滑采样（未显式设置 `image-rendering`），100% 时 `scale=1` 保留原像素；**没有断言**核对采样方式与 100% 原像素 |
| 9 | §7.5 | 超限、解码失败、GIF、WebP 和其他二进制使用信息页，显示类型、大小和完整路径 | 是 | `§10.2 图片解码失败时显示宿主原因而不是破图`、`不可预览页显示名称、类型与大小`、`界面显示超限原因` |
| 10 | §7.5 | 信息页唯一主要动作是用系统默认程序打开 | 是 | `不可预览页提供"使用系统默认程序打开"`、`按钮用系统默认程序打开当前文件` |

**§7.6 Commit 与 Changes（14 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.6 | 提交工具窗自上而下为标题、工具栏、Changes 列表、提交信息和动作栏 | 是 | `§10.1 无 Changes 保留提交工具窗口骨架`（工具栏 + 提交框 + 禁用提交按钮）+ `Changes 显示真实改动文件`、`提交区显示改动数` |
| 2 | §7.6 | 只有 Changes 与 Unversioned Files 两组，不显示 Changelist 名称或创建入口 | 是 | `Changes 分组来自 Git`、`未跟踪文件默认不勾选` 覆盖分组事实；**没有**"不存在 Changelist 名称/创建入口"的反向断言；harness `§7.6 只有 Changes 与 Unversioned Files 两组、不显示 Changelist`（组头取 `.check-group-row > strong`，逐值 `Changes`/`Unversioned Files`） |
| 3 | §7.6 | 每组文件按显示文件名自然顺序，同名再按完整相对路径稳定排序 | 是 | 分组树按目录生成（视觉稿与实现同源）；**没有排序断言**；harness `§7.6 每组按显示文件名自然顺序、同名按完整路径`（**按 data-group 分组后**逐组比较，第一版把整表当一条序列而误报） |
| 4 | §7.6 | 文件复选决定是否提交完整文件；选中行与提交复选互相独立 | 是 | `未跟踪文件默认不勾选`、`单击改动文件只更新选中态`、`选择变化不改动提交草稿` |
| 5 | §7.6 | 悬停独立维护、选中优先、只重绘命中行、滚动折叠后重命中、空白区不算最后一行、隐藏清除、悬停不改状态且不查 Git | 部分 | `真实悬停改变行背景`、`悬停不触发宿主查询`、`悬停不改变选择与复选`、`⑥ 悬停操作后标题栏与状态栏逐像素不变`；**"选中态优先于悬停"此前一直 SKIP**（场景没有选中行）——第 336 轮已在采集快照前用真实点击选中一行，该断言现在真正执行并通过；重命中 / 空白区 / 隐藏清除仍没有断言 |
| 6 | §7.6 | 单击行只选中文件；已有 Diff 时后台更新、Diff 激活时原位更新；复选框只改提交选择 | 是 | `单击改动文件只更新选中态`、`单击改动文件不创建 Diff`（后台/原位更新见 §5.2 块） |
| 7 | §7.6 | Enter／双击／"显示 Diff" 打开并激活唯一工作区 Diff 标签，后续选择持续更新，关闭后解除跟随 | 是 | §5.2 块（`双击改动文件建立比较标签`、`比较标签始终只有一个`、`关闭比较标签后解除跟随`） |
| 8 | §7.6 | 用户修改文件后保留复选状态，仅更新状态标记与 diff 版本 | 部分 | `前置条件：草稿/勾选/滚动已就位`、`结构性重绘后草稿恢复到输入框` 覆盖重绘保状态；**"外部修改文件后复选保留"没有断言** |
| 9 | §7.6 | Commit 与 Commit and Push 固定在底部，滚动长列表仍可见 | 是 | 结构上是 `commit-actions` 固定行；**没有滚动后可见性断言**；harness `§7.6 Commit 动作固定在底部、滚动长列表仍可见`（滚到底后 `.commit-actions` 的 bottom 变化 ≤2px） |
| 10 | §7.6 | 字号改变时各行按字高扩展、图标与复选框固定、动作放不下换两行、输入框至少保留提示行 + 一行正文、上次提交空间不足时省略 | 部分 | 有字号/大字号适配块覆盖工具栏与 Diff；**提交工具窗的动作换行、输入框最小行数与"上次提交省略"没有断言** |
| 11 | §7.6 | 空态复用同一提交布局；空间不足时保留"没有待提交的更改"、收起次要说明 | 是 | `§10.1 无 Changes 显示指定文案`、`§10.1 无 Changes 保留提交工具窗口骨架`、`§10.1 无 Changes 不再列出文件行` |
| 12 | §7.6 | Amend 勾选后读取上一次提交信息；取消后恢复用户尚未提交的原文本 | 部分 | 视觉稿有 Amend 行；**读取/恢复文本没有断言**；**宿主侧已有两个单测**：`GitCommitServiceTests.读取上一次提交信息保留标题和正文并去除传输末尾换行`、`…无HEAD时读取上一次提交信息返回稳定失败而不伪造空提交`；界面侧的勾选/取消恢复原文本仍无断言 |
| 13 | §7.6 | 校验错误显示在提交信息附近，焦点留在提交信息，不弹全局错误框 | 是 | `§10.2 提交错误说明发生了什么／哪些状态未改变／可以做什么`、`§10.2 错误归属到发生区域`（`globalDialog === 0`） |
| 14 | §7.6 | 校验/Hooks 失败复用输入框上方提示行、危险色、长原因可悬停、保留草稿与勾选、修改后清除旧提示、等待期间不抢焦点 | 部分 | `校验失败保留表单`、`查询失败保留用户草稿`、`提交成功后清空草稿与错误` 覆盖"保留/清除"；**危险色样式、悬停读全文、失败不抢焦点没有断言** |

**§7.7 工作区 Diff（14 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.7 | 首次单击 Changes 文件只改列表选中，不创建/激活 Diff 标签，普通文件与滚动不变 | 是 | `单击改动文件只更新选中态`、`单击改动文件不创建 Diff` |
| 2 | §7.7 | Enter／双击／"显示 Diff" 创建或复用并激活唯一 Diff 标签；双击不固定原文件，后续选择持续更新 | 是 | §5.2 块（`双击改动文件建立比较标签`、`跟随复用同一个比较标签（不新建）`、`复用比较标签时同步目标路径`） |
| 3 | §7.7 | 已有 Diff 但前台是普通文件时后台更新；Diff 已激活时原位更新 | 是 | `后台跟随不改变前台视图类型`、`后台跟随不把差异画到前台正文` |
| 4 | §7.7 | 第一次请求加载时保留旧正文约 200ms，随后在同一正文区域替换为加载状态或新结果 | 部分 | `首次打开差异时在文件标题行显示加载提示` 覆盖加载提示；**"保留旧正文约 200ms"的时序没有断言** |
| 5 | §7.7 | 工具栏左侧依次上一处/下一处/搜索/上一文件/文件计数/下一文件，右侧差异摘要/忽略空白/双栏单栏/设置；Tab 顺序与视觉顺序一致 | 部分 | 结构在视觉稿与实现里都有；**Tab 顺序断言没有**（§12.3 只覆盖"图标有可访问名称"） |
| 6 | §7.7 | 差异数量按连续变更块计算；单双栏一致；前后定位到块首行；历史与引用比较沿用同一规则 | 部分 | 工具栏有差异计数与导航按钮；**"按变更块计数"与单双栏一致性没有断言** |
| 7 | §7.7 | 双栏文件栏标注基准与当前版本并对齐正文起点；单栏上下排列；只读身份与完整路径悬停；仅重排文件信息与正文 | 部分 | `diff-status` 与比较页的文件栏已有像素基线；**"只读身份/路径悬停/不移动工具栏"没有断言** |
| 8 | §7.7 | 到达首/尾变更块后再按同方向只显示"再次点击可进入上一个/下一个文件"，再按才切换；首次提示不查询、不移动正文、不循环 | 部分 | 有 `diff-boundary` 页与像素对照（1.60），实现里有该提示；**边界行为的断言缺失**（`diff-boundary` 只做了视觉基线） |
| 9 | §7.7 | 跨文件同步 Changes 选中路径、标签身份与文件计数，保留复选/草稿/其他标签/几何；查询期间禁用差异箭头而文件箭头仍可用；旧结果不覆盖新选择 | 部分 | `④ 只提交勾选…` 不适用；有 `跨文件` 相关块但只覆盖标签身份与计数；**"查询期间禁用差异箭头、文件箭头仍可用"没有断言** |
| 10 | §7.7 | `Esc` 关闭边界提示并撤销待跨文件状态；改方向/选文件/切模式/隐藏/关闭/重载/改布局同样撤销；首尾不循环 | 部分 | 结构在实现里；**撤销矩阵没有断言** |
| 11 | §7.7 | 双栏行号与变更连接区固定，左右正文同步垂直滚动 | 部分 | 视觉结构存在；**同步滚动没有断言** |
| 12 | §7.7 | 双栏左右留白 13px、行号中栏左右各 7px、中栏至少 84px 且按最长行号度量；改等宽字号只重排正文与行号 | 部分 | `--comparison-gutter-width` 由实现按行号度量；**13/7/84 这三个数值没有断言** |
| 13 | §7.7 | 二进制或超限 diff 使用摘要页替代正文，不改变 Commit 工具窗口 | 是 | §6.5 的最终说明四条（`最终说明持续可见（Binary/SideTooLarge/OutputTooLarge）` + 三要素）+ `diff-status` 页像素 1.29 |
| 14 | §7.7 | Markdown 与 JSON 的默认 Git 页面仍显示磁盘真实文本 diff，可从工具栏打开修改后预览 | 部分 | `Markdown 预览来自真实内容`、`预览不残留样例标题` 覆盖预览；**"默认显示磁盘真实文本 diff 且可从工具栏打开预览"没有端到端断言** |

**§7.16 内置终端（11 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §7.16 | 终端在底部按需创建；打开后**替换 Git 历史**但不影响项目树与编辑区 | 是 | `§7.16 运行时点终端入口真的建立会话（挂载 xterm 且只 start 一次）` + `终端与 Git 历史互斥（底部只有一个工具窗口）` + `§9.4 终端与 Git 历史互斥` |
| 2 | §7.16 | 打开后立即显示标题与"Shell 名称 · 正在启动…"；加载期间隐藏/关闭可用；重复打开复用同一请求；启动完成不覆盖用户选择 | 部分 | "复用同一请求"由 `只 start 一次` 覆盖；**启动中标题文案、加载期间隐藏/关闭可用、启动完成不覆盖用户已做的选择**都没有断言 |
| 3 | §7.16 | 加载期间关闭或切工作区 → 旧请求失效（不启动旧 Shell、不重新显示、不覆盖新工作区提示）；关闭后立即重开建立新请求 | 未覆盖 | 需要构造"启动中关闭/切工作区 + 晚到完成"的时序用例；当前只有轮询并发上限的断言 |
| 4 | §7.16 | 首版只显示一个会话标签，不显示无效的多会话管理 | 是 | 实现里确实只有单会话（无多会话 UI），但没有反向断言"不存在多会话入口/标签"；harness `§7.16 只显示一个会话标签、标题栏显示当前 Shell、无多会话管理 UI` |
| 5 | §7.16 | Shell 就绪后自动显示初始提示符（不要求先按键）；首次布局不截断启动输出；隐藏不结束会话、恢复保留输出；调整面板大小不重建 Shell | 部分 | 有 `§7.16 终端正文显示宿主输出`（真机复探 7 行真实 banner）与"隐藏只收起面板、同一会话与正文保留"；**"调整面板大小不重建 Shell"没有断言** |
| 6 | §7.16 | WebView2 首次导航期间 Shell 可并行启动；ready 前的提示符/输出在终端显示后保留；ready 后按实际列行补偿尺寸 | 未覆盖 | 需要"页面 ready 前已有输出"的时序构造 |
| 7 | §7.16 | 正文等宽字体、行高 = 等宽字号 ×1.7（13px→22px）、留白上下 11px/左右 12px；字号变化只重新度量终端 | 部分 | **已断言**：留白 `11px 12px` 与等宽字体来自设置（`§7.16 终端正文留白 11px/12px 且字体来自等宽设置`）、行高随字号单调增长（`§7.16 终端行高随字号增长…`，实测 13→25 / 17→34）；**仍未达成**：配置侧 `lineHeight: 1.7` 与规格一致，但渲染值是 25px 而非规格写的 22px（见 §3.2 第 20 条，待裁决） |
| 8 | §7.16 | 标题栏显示当前 Shell；"更多"菜单提供切换配置与外部终端入口 | 未覆盖 | `项目树「在外部终端打开」调用宿主` 覆盖了外部终端入口的一处；**终端标题栏的"更多"菜单没有断言**；**第一半已有断言**（同一条 harness 断言比对 `.terminal-session` 文本 === `__augitTerminalShell`）；**「更多」菜单仍没有断言**；**第一半已断言**（`§7.16 只显示一个会话标签、标题栏显示当前 Shell、无多会话管理 UI`）；**第二半是实现缺口**：点"更多操作"前后 DOM 签名逐字不变（`overlay=false`、body 长度/覆盖层数/弹层数一致）→ 该按钮**无绑定**；它是 `<button>` 而"未接线兜底"只记 `.html` 链接，故旧写法用 `__augitUnwiredLabel` 判断会假失败（已改为 DOM 签名对比）。harness 保留钉住断言 |
| 9 | §7.16 | 会话名称按字宽显示；标题行随字高扩展；三个动作按可见顺序 Tab/Shift+Tab 循环；正文 Tab 交给 Shell；当前 Shell 标签不入 Tab 顺序；长名称悬停说明 | 部分 | 已断言：`§7.16 终端标题栏动作按可见顺序、当前 Shell 标签不入 Tab 顺序`（三个按钮 x 递增、`.terminal-session` 无 `tabindex`）、`§7.16 标题栏三个动作 Tab 循环（第三个之后回到第一个）`、`§7.16 标题栏 Shift+Tab 反向循环（第一个回退到最后一个）`、`§7.16 终端正文的 Tab 交给 Shell（宿主收到 \t）`。**未断言**：会话名称按字宽显示、标题行随字高扩展、长名称悬停说明；**已断言的 2 半**：三个动作按可见顺序且当前 Shell 标签不入 Tab 顺序、Tab 循环（第三个回到第一个）、Shift+Tab 反向循环、正文 Tab 交给 Shell；**未断言**：「会话名称按字宽显示」与「标题行随字高扩展」；**另已断言（第 80/81 轮）**：`§7.16 会话名称按字宽显示（定宽 + nowrap + 溢出裁剪）` —— 实测 `Windows PowerShell`、宽 **146**（client 与 scroll 都是 146，13px 刚好放下）、`white-space: nowrap`、`overflow: hidden`、`text-overflow: ellipsis`；**仍未断言**：标题行随字高扩展 |
| 10 | §7.16 | 关闭空闲终端直接释放；存在前台命令时确认并说明将结束整个子进程树 | 是 | `§7.16 无前台命令时关闭终端：直接结束会话并收起（不弹确认）`、`有前台命令时先确认且未结束会话`、`§7.16 取消后保留终端`、`确认后结束会话并收起` |
| 11 | §7.16 | 关闭后恢复先前底部工具窗口状态或折叠底部区域，并回收 WebView2、Shell、WSL 与会话目录 | 部分 | "收起/恢复"有断言；**"回收会话目录/进程"没有断言**（宿主侧 `TerminalSessionRegistry` 有实现，但没有断言或单测引用它） |

### 2.7 §9 关键状态机

| 规格出处 | 操作 → 状态转换 | 已实现 | 证据位置 |
| --- | --- | --- | --- |
| §9.1 | Diff 状态机：临时标签→加载→就绪/失败/无差异；等待阈值内复用标签 | 是 | 「规格 §9.1：Diff 状态机」块（含 `§9.1 等待阈值：创建或复用 Diff 临时标签`） |
| §9.2 | Git 刷新：失败显示非阻塞错误、保留上次界面、标记非最新 | 是 | 「规格 §9.2：Git 刷新状态机」块 + 「状态查询失败的局部失败状态」块（`查询失败显示非阻塞局部错误（role=status、无模态浮层、带原因）`） |
| §9.3 | 写操作进行态、取消入口、取消后重读真实状态 | 是 | 「规格 §9.3：Git 写操作状态机」块（`§9.3 进行中记录当前动作`、`§9.3 进行中禁用重复触发`、`§9.3 进行中显示取消入口`、`§9.3 取消先通知宿主`、`§9.3 宿主确认前保持取消中`、`§9.3 取消后重新读取真实状态`） |
| §9.4 | 工具窗口状态机：切换/折叠/互斥 | 是 | 「规格 §9.4：工具窗口状态机」块（10 条 `§9.4` 断言） |

**§6 逐条展开（异步加载与刷新不变量 40 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §6 | 异步操作只能更新负责该数据的最小内容区 | 是 | `加载 diff 后左侧工具窗口位置尺寸不变`、`加载期间左侧工具窗口仍可见` |
| 2 | §6 | 不因 diff/历史/文件/Markdown/Git 状态加载而隐藏整个编辑工作区 | 是 | `加载期间编辑工作区未被隐藏` |
| 3 | §6 | 不通过先清空主区域、后重建控件来表现加载 | 是 | `加载期间编辑工作区未被隐藏` + `加载完成后不残留加载提示`（正文原位替换） |
| 4 | §6 | 主框架、工具窗口、分隔位置、标签条和非目标区域在加载期间保持像素位置不变 | 是 | `加载 diff 后左侧工具窗口位置尺寸不变` + `⑥ 悬停操作后标题栏与状态栏逐像素不变` |
| 5 | §6 | 当前工作区路径与仓库可用状态 | 是 | `非 Git 目录保留项目树`、`非 Git 目录不注入 Git 状态` |
| 6 | §6 | 当前分支和 HEAD | 是 | `分支标签来自真实引用`、`Reset 目标提交来自真实 HEAD` |
| 7 | §6 | Changes 与 Unversioned Files 的路径、状态和排序 | 是 | `Changes 显示真实改动文件`、`Changes 分组来自 Git` |
| 8 | §6 | 当前 Git 操作会话类型与可用动作 | 是 | `冲突后自动进入操作会话，显示类型/步骤/冲突文件`（含可用动作集合） |
| 9 | §6 | 当前已选文件或提交的稳定标识 | 是 | `提交列表节点身份保持（未重建）`、`刷新后提交选择不变` |
| 10 | §6 | 不重建列表 | 是 | `提交列表节点身份保持（未重建）` |
| 11 | §6 | 不重设选中项 | 是 | `刷新后提交选择不变` |
| 12 | §6 | 不重新计算或渲染 diff | 是 | `Git 刷新不重新加载 diff` |
| 13 | §6 | 不改变工具窗口大小 | 是 | `加载 diff 后左侧工具窗口位置尺寸不变` |
| 14 | §6 | 不触发布局 | 是 | 同第 13 条断言 |
| 15 | §6 | 可以更新"最后检查时间"等非布局状态，但首版没有必要显示该信息 | 部分 | 口径类条文：界面确实不显示该信息；**"允许但不显示"没有断言** |
| 16 | §6 | 请求键与当前已显示的 diff 相同时直接复用结果 | 是 | `§9.1 相同选择：不重新请求也不进入加载` |
| 17 | §6 | 单栏与双栏属显示模式：复用补丁只重排、不再次查询；重复点击当前模式不触发加载 | 是 | `双栏模式产生一次请求`、`切换模式后差异仍在` |
| 18 | §6 | 新请求开始后保留旧 diff 或在同一内容区显示轻量占位，不能清空其他区域 | 是 | `首次打开差异时在文件标题行显示加载提示`、`加载 diff 后左侧工具窗口位置尺寸不变` |
| 19 | §6 | 每个请求带递增版本号；旧请求晚于新请求返回时丢弃旧结果 | 是 | §6 晚到不覆盖断言族 + `关闭比较后晚到响应不写回差异` |
| 20 | §6 | 用户快速连续选择多个文件时只显示最后一次选择 | 是 | `快速连续打开文件时保留最后一次选择` |
| 21 | §6 | 改选后的新 Diff 仍在查询或排版时，无变化的 Git 状态通知必须复用该进行中请求 | 部分 | 有"不拿暂留正文判断失效"的实现注释与同类断言；**"状态通知复用进行中请求"没有直接断言** |
| 22 | §6 | 真实外部变化时仅当前文件受影响且请求键内容版本变化，才重新计算当前 diff | 是 | `外部改变无关文件不重新请求当前 diff`、`外部改变当前差异文件会重新请求` |
| 23 | §6 | 失败时在当前 diff 区显示原因，并保留文件列表和当前选择 | 是 | `查询失败保留上一次已知界面`、`§9.1 请求失败：保留列表与选择` |
| 24 | §6 | 从普通文件返回已加载 Diff 恢复正文或摘要；关闭后释放补丁与正文并取消请求；旧任务不得恢复已关闭标签 | 是 | `关闭比较已移除比较标签`、`关闭比较后晚到响应不写回差异` |
| 25 | §6 | 路径和状态均未变化时保留原列表项对象 | 是 | `提交列表节点身份保持（未重建）` |
| 26 | §6 | 部分变化时增量更新，保持未变化项、展开状态和滚动位置 | 是 | `展开状态在重绘后保留`、`结构性重绘后草稿恢复到输入框` |
| 27 | §6 | 当前选中项仍存在时保持选中；不存在时选同组最近邻；组为空时显示稳定空状态 | 是 | `刷新后提交选择不变` + §10.1 空态断言族 |
| 28 | §6 | 刷新期间不得自动勾选或取消用户的提交复选状态 | 是 | `未跟踪文件默认不勾选`、`选择变化不改动提交草稿` |
| 29 | §6 | 预计低于 150 毫秒的操作不显示加载动画，避免闪烁 | 是 | `150 毫秒内不显示加载动画` |
| 30 | §6 | 超过 150 毫秒后在目标内容区显示小型进度指示或"正在加载…" | 是 | `§9.1 等待阈值：加载提示延迟约 150 毫秒出现` |
| 31 | §6 | Diff 有旧正文时保留正文并在文件标题行提示加载；加载/完成/外部重算不覆盖全局状态栏提示 | 是 | `首次打开差异时在文件标题行显示加载提示`、`外部重算后全局提示保持不变` |
| 32 | §6 | 渲染完成前仍属当前请求；二进制/超限/无差异/错误的最终说明必须持续可见，不能被收尾隐藏；切回文本后隐藏 | 是 | §6.5 四条 `最终说明持续可见（Binary/SideTooLarge/OutputTooLarge/Ready）` + 三要素断言 |
| 33 | §6 | 加载指示不得循环触发布局，也不得使用大面积白屏 | 部分 | 真机巡检查"帧非空白"可间接覆盖；**"不循环触发布局"没有断言** |
| 34 | §6 | 可取消操作在原操作区域显示"取消"，取消后读取并显示最新磁盘事实 | 部分 | 有取消入口与"取消不得显示为完成"的断言族；**"取消后读取最新磁盘事实"没有直接断言** |
| 35 | §6 | 文件读取结果属于原文件标签；完成时只创建该标签控件、不重排主窗口；显示前重核标签存在/活动/比较状态 | 部分 | `加载完成后不残留加载提示` 与"不重排"族覆盖部分；**"重核三个状态"没有断言** |
| 36 | §6 | 读取期间切到比较，旧文件就绪后保持隐藏；切回复用已读内容；普通文件乱序完成时只显示最后选择的文件 | 是 | `后台跟随不改变前台视图类型`、`快速连续打开文件时保留最后一次选择` |
| 37 | §6 | 读取期间用户操作树/滚动/移焦点：已打开文件可完成显示，但不得重选树行、滚回原视口或抢回焦点 | 部分 | `§6.1 异步数据到达不得打断用户输入` 覆盖输入与焦点；**"不重选树行/不滚回视口"没有断言** |
| 38 | §6 | 关闭尚在读取的标签后结果不得创建视图或恢复标签；关闭其前面的后台标签不使当前读取失效 | 部分 | `文件已删除则移除其标签`、`关闭比较后晚到响应不写回差异` 覆盖部分；**"关闭前面的后台标签只改索引"没有断言** |
| 39 | §6 | 关闭比较后若返回的普通标签尚未就绪，正文显示该文件的读取占位，不能显示"从左侧文件树打开文件"的无文档提示 | 未覆盖 | 没有断言（需要"关闭比较 → 普通标签未就绪 → 占位而非无文档提示"的用例） |
| 40 | §6 | 启动恢复只激活原恢复文件一次；恢复期间用户操作后，收尾不得重新激活正文、重选项目树或抢焦点 | 部分 | `显式文档参数存在时不恢复会话`、`§6.7 对照：不带开关时恢复确实发生` 覆盖"只恢复一次"；**"收尾不重激活/不重选树"没有断言** |

**§5 逐条展开（外壳与交互框架 29 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §5 | 点击已激活的左侧入口：折叠对应工具窗口，再次点击恢复上次宽度或高度 | 是 | 「规格 §5.1：工具窗口切换与折叠」块（`再次点击已激活入口则折叠`、`点击已激活入口恢复侧栏`、`再次点击已激活入口恢复侧栏`） |
| 2 | §5 | 点击同区域的另一个入口：原位替换（项目↔提交、Git 历史↔终端） | 是 | 同块（`切换到提交工具窗口`）+ §9.4 断言族 |
| 3 | §5 | 切换工具窗口不关闭编辑标签、不改当前文件、不重置编辑滚动 | 是 | 同块（`切换工具窗口不改变当前文件`、`切换工具窗口后标签仍指向当前文档`） |
| 4 | §5 | 左侧和底部工具窗口可以同时打开 | 是 | 有"终端与 Git 历史互斥"与侧栏切换断言；**"左侧与底部同时打开"没有单独断言**；harness `§5 左侧与底部工具窗口可以同时打开`（诊断 `side=true, bottom=true`） |
| 5 | §5 | 终端和 Git 历史互斥，不允许同时占用底部区域 | 是 | `终端与 Git 历史互斥（底部只有一个工具窗口）`、`§9.4 终端与 Git 历史互斥` |
| 6 | §5 | 点击汉堡按钮时标题栏左侧原位显示五个文字入口，不弹替代标题栏的悬浮卡片 | 是 | 「规格 §5.1：标题栏汉堡菜单」块（`菜单入口原位显示`、`不出现替代标题栏的悬浮卡片`） |
| 7 | §5 | "文件、视图、Git"打开贴近入口的动作菜单；关闭后恢复入口、当前分支与当前文件上下文 | 是 | 同块（`菜单关闭后入口恢复为普通标题栏`）+「菜单五个入口各自的行为」块 |
| 8 | §5 | "终端"直接切换底部终端；"设置"直接打开设置模态；执行前先恢复普通标题栏 | 是 | 同块（`菜单「终端」切换底部终端窗口`、`菜单「设置」打开设置模态窗口`） |
| 9 | §5 | `Esc` 关闭内嵌菜单并恢复原标题栏；窗口按钮、树、标签和工具窗口状态不变 | 是 | 同块（`Esc 关闭内嵌菜单` + 关闭前后结构快照比对） |
| 10 | §5 | 双击文件或按 `Enter` 打开正式标签 | 是 | 「规格 §5.4 键盘路径」块（`单击树行不加载文档（需 Enter 或双击）`） |
| 11 | §5 | Changes／历史／搜索结果的单击只改选中；已有临时预览标签时后台更新，不抢占编辑区 | 是 | §5.2 块（`单击改动文件只更新选中态`、`单击改动文件不创建 Diff`、`后台跟随不改变前台视图类型`） |
| 12 | §5 | 上述列表按 `Enter` 打开并激活临时预览标签；下一个结果复用该标签 | 是 | `Enter 打开临时预览标签`、`下一个结果复用同一个预览标签`、`新建标签不是预览标签` |
| 13 | §5 | Changes 只保留一个工作区比较标签；双击/Enter/显示 Diff 打开并激活；后续选择持续更新同一标签；普通文档前台时只后台更新 | 是 | §5.2 块（`双击改动文件建立比较标签`、`比较标签始终只有一个`、`跟随复用同一个比较标签（不新建）`） |
| 14 | §5 | `Ctrl+W` 关闭当前标签，激活同一标签组中的相邻标签 | 是 | 有标签关闭断言族；**"Ctrl+W 关闭并把焦点交给相邻标签"没有单独断言**；harness `Ctrl+W 关闭当前标签`（只剩 1 个标签）+ `关闭后激活相邻标签`（激活的正是相邻的 `docs/notes.txt`）—— 两半都有断言 |
| 15 | §5 | 中键或关闭叉关闭后台普通文件标签只移除目标标签并释放视图；不重激活当前文件、不改正文/查找/树选择/焦点/滚动；按下不抢焦点 | 是 | 「规格 §5.2：关闭叉的按下即捕获，以及关闭后台标签的状态保持」块 |
| 16 | §5 | 工作区 Diff／历史／引用比较的关闭叉遵循相同规则；关闭后台比较只移除标签、取消请求并释放正文，不重排主窗口、不抢焦点 | 是 | 「关闭后台比较只移除目标标签，不抢前台焦点」块 |
| 17 | §5 | 外部文件变化后保持标签顺序、当前标签、选择范围和滚动位置，除非文件已不存在 | 是 | §6 外部变化断言族（`外部变化保持标签顺序`、`外部变化保持当前标签`、`文件已删除则移除其标签`） |
| 18 | §5 | 快速打开、分支选择和右键菜单是非模态弹层；当前文件搜索使用正文顶部占一行的查找条 | 是 | 快速打开/分支弹层的非模态断言 + 查找条占行断言（§7.2） |
| 19 | §5 | `Esc` 只关闭最上层弹层或取消当前搜索，不关闭其下方工具窗口 | 是 | 有 `Esc` 关闭菜单与查找条的断言；**"只关最上层、不关下层工具窗口"的层级用例没有单独断言**；harness `§5 Esc 只关闭最上层弹层、不关闭其下方工具窗口`（浮层 1→0，终端与左侧仍在） |
| 20 | §5 | 设置、克隆、Push、Reset、Stash、Worktree、远端管理与危险确认是模态对话框 | 是 | 各对话框块（`settings-window`/`clone-dialog`/`push-dialog`/`reset-dialog` 的模态断言） |
| 21 | §5 | 模态打开时背景结构保持可见，不重新创建主界面 | 是 | 设置窗口断言（`背景 inert` 且结构保留）+ 各管理窗口的"不重建"断言 |
| 22 | §5 | 对话框取消后恢复打开前焦点；确认后焦点回到触发区域或直接进入结果区域 | 是 | `Worktree 取消后焦点回到打开前元素` + 各对话框的焦点恢复断言 |
| 23 | §5 | 紧凑单行输入窗口：输入框获得焦点；`Tab` 在输入框/取消/确定/关闭间循环，`Shift+Tab` 反向 | 是 | `组词中的 Enter/Esc 不提交也不关闭紧凑窗口` 所在的紧凑对话框块（含键盘循环断言） |
| 24 | §5 | 紧凑窗口与遮罩归属顶层主窗口，主窗口禁用，关闭后恢复启用态与焦点；不重建背景页；业务校验由原入口完成 | 是 | 紧凑对话框块的模态与焦点恢复断言 + `跳转行确认后…` 系列 |
| 25 | §5 | 工具栏按钮、树、列表、标签、输入框和正文均可使用键盘访问 | 是 | §12.3 可访问名称断言族 + §5.4 键盘路径块 |
| 26 | §5 | `Tab` 在当前区域内按视觉顺序移动焦点，不先穿越所有全局工具入口 | 是 | `bindRegionTabOrder` 的断言族（`§5.4`/`§9.4`）——**但"终端标题栏三个动作循环"是它的已知例外，见 §3.2 第 8 条** |
| 27 | §5 | 树使用方向键移动，右键或菜单键打开上下文菜单，`Enter` 执行默认动作 | 部分 | `Enter` 打开有断言；**方向键移动与菜单键打开上下文菜单没有断言**；**已有断言**：`树支持方向键移动选择`、`项目树右键打开上下文菜单`、`菜单键打开项目树上下文菜单`（3/4 半）；**仍未断言**：`Enter` 执行默认动作；**第 81 轮实测**：`Enter` **没有**执行默认动作（行仍持有焦点、`__augitLive.document` 仍为 null，也没落进"未接线兜底"）→ 已加**钉住断言** `§5 缺口钉住：树 Enter 当前不执行默认动作`，缺口记 §3.2 第 23 条 |
| 28 | §5 | 列表选择变化先只更新选中；只有已有预览标签或用户 Enter/双击/显示 Diff 时才请求或更新预览；同一项重复选择不重复加载 | 是 | `同一项重复选择不重复加载`（§5.2/§6 断言族）+ `单击改动文件只更新选中态` |
| 29 | §5 | 固定快捷键严格遵循产品规格，不把 PyCharm 快捷键原样带入 Augit | 是 | `PyCharm 快捷键不生效（无浮层/无查找条/不改标签与正文）` + `Ctrl+F`/`Ctrl+G`/`Ctrl+P` 的规格断言 |

**§9 逐条展开（状态机 22 条）**

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §9 | `未选择`：编辑区保持当前正式文件或稳定空状态 | 部分 | `单击改动文件只更新选中态` 覆盖"不抢编辑区"；**"未选择时保持当前正式文件或稳定空态"没有单独断言** |
| 2 | §9 | `已选择`：只保留列表选中和路径意图，不创建 Diff、不请求正文；已有 Diff 标签时允许后台更新请求 | 是 | `§9.1 已选择：单击不请求差异` + §5.2 的后台跟随断言族 |
| 3 | §9 | `等待阈值`：明确打开 Diff 后创建或复用临时标签，150 毫秒内不显示闪烁动画 | 是 | `§9.1 等待阈值：创建或复用 Diff 临时标签`、`§9.1 等待阈值：加载提示延迟约 150 毫秒出现` |
| 4 | §9 | `加载中`：仅 Diff 标签正文显示加载；列表、工具栏和其他标签不变化 | 是 | `§9.1 加载中：列表与标签不再变化` + `慢 Diff 在途时文件列表仍及时刷新（未等待 Diff）` |
| 5 | §9 | `已显示`：原位替换加载内容 | 是 | `§9.1 已显示：加载提示被正文替换` |
| 6 | §9 | 新选择到达：递增请求版本并进入新的等待状态；旧结果不得覆盖 | 是 | §6/§7.7 的"晚到不覆盖"断言族 + `外部改变当前差异文件会重新请求` |
| 7 | §9 | 相同选择到达：保持已显示，不进入加载 | 是 | `§9.1 相同选择：不重新请求也不进入加载` |
| 8 | §9 | 请求失败：进入局部失败状态，保留选择和周边结构 | 是 | `§9.1 请求失败：保留列表与选择` |
| 9 | §9 | 多个文件系统与 `.git` 事件必须合并 | 部分 | 有"合并窗口"的实现注释与并发上限断言（终端轮询在途 ≤1 是不同对象）；**"文件系统与 .git 事件合并成一次查询"没有直接断言** |
| 10 | §9 | 查询期间不清空现有列表 | 是 | `§9.2 查询期间保留现有列表`、`§9.2 查询期间侧栏位置不变` |
| 11 | §9 | 无变化直接回到空闲 | 是 | `§9.2 无变化时界面不变` |
| 12 | §9 | 有变化仅更新差异项，并决定当前 diff 是否失效 | 是 | `§9.2 无关变化不使当前 diff 进入加载`、`外部改变无关文件不重新请求当前 diff` |
| 13 | §9 | 查询失败显示非阻塞错误，保留上一个已知界面但明确标记不是最新状态 | 是 | `查询失败显示非阻塞局部错误（role=status、无模态浮层、带原因）` |
| 14 | §9 | 进行中禁用重复触发，显示取消和当前动作 | 是 | §9.3 写操作状态机断言族（含 Reset/提交/推送的进行态与取消入口） |
| 15 | §9 | 成功后刷新相关文件、Changes、分支和历史 | 是 | §9.3 与 §6 的"成功后重读真实状态"断言族（`确认后列表与详情都按真实结果更新`） |
| 16 | §9 | 失败后保留用户输入并显示脱敏原因 | 是 | `§9.3 保存失败时保留用户输入`、`§9.3 保存失败时对话框保持打开`、`失败原因显示在对话框内` |
| 17 | §9 | 冲突后进入操作会话，不把冲突包装成普通失败 | 是 | `冲突后自动进入操作会话，显示类型/步骤/冲突文件` |
| 18 | §9 | 取消后等待本机 Git 停止，再读取真实仓库状态 | 部分 | 有 `write/cancel` 的取消入口与"取消不得显示为完成"断言（Clone 侧）；**"等待 Git 停止后再读真实状态"没有直接断言** |
| 19 | §9 | 左侧：`折叠 ↔ 项目 ↔ 提交 ↔ 搜索` | 是 | `§9.4 左侧初始为项目`、`§9.4 左侧切换到提交`、`§9.4 左侧切换到搜索`、`§9.4 折叠（最小序列）…` |
| 20 | §9 | 底部：`折叠 ↔ Git 历史 ↔ 终端` | 是 | `终端与 Git 历史互斥（底部只有一个工具窗口）`、`§9.4 终端与 Git 历史互斥` |
| 21 | §9 | 左侧和底部状态独立 | 是 | `§9.4 切换不重建编辑器` 覆盖"不互相重建"；**"左侧与底部状态互不影响"（例如底部折叠不改左侧激活项）没有单独断言**；harness `§9 左侧与底部状态独立（隐藏终端不改左侧）` |
| 22 | §9 | 切换只替换对应区域，不重建编辑器和另一个工具区域 | 是 | `§9.4 切换不重建编辑器` |

### 2.8 §10 空、错、禁用与危险状态（15 条逐条）

**状态口径**：`是` = 有**可核对的自动化断言**（harness 断言名或 C# 单测名，两者都必须能在仓库里检索到）；`部分` = 实现有、但只有间接证据或没有断言；
`未覆盖` = 找不到断言也找不到可复跑的观察。**没有断言的行不算通过**。

| # | 规格出处 | 条文 | 状态 | 证据 / 缺什么 |
| ---: | --- | --- | --- | --- |
| 1 | §10.1 | 空状态使用一句主说明和必要的一个动作，不使用大插画 | 是 | `§10.1 空态不使用插画且只给一句说明`：空态容器内 `img/svg/canvas` 计数为 0，文字长度 > 0 且 ≤ 80；视觉基线 `commit-empty`（0.00）与 `git-history-empty`（0.64）同口径 |
| 2 | §10.1 | 无 Changes：显示"没有待提交的更改"，保留提交工具窗口骨架 | 是 | `§10.1 无 Changes 显示指定文案`、`…保留提交工具窗口骨架`、`…不再列出文件行` |
| 3 | §10.1 | 无历史：显示"仓库还没有提交"，保留引用树和筛选栏 | 是 | `§10.1 无历史显示指定文案`、`…保留引用树与筛选栏`、`…详情区不停在加载态` |
| 4 | §10.1 | 搜索无结果：显示"未找到结果"，输入框和查询保持 | 是 | `§10.1 搜索无结果显示指定文案`、`…保留输入框与查询`、`…不列出结果行` |
| 5 | §10.2 | 错误归属到发生区域，不优先使用全局消息框 | 是 | `§10.2 错误归属到发生区域`（+ `查询失败显示非阻塞局部错误（role=status、无模态浮层、带原因）`） |
| 6 | §10.2 | 错误必须说明发生了什么、哪些状态未改变以及用户可以做什么 | 是 | `§10.2 提交错误说明发生了什么`／`…哪些状态未改变`／`…可以做什么`、`§10.2 未勾选错误具备三要素`、`§9.3 / §10.2 Reset 失败保留对话框并说明原因` |
| 7 | §10.2 | Git 命令输出脱敏并限制长度，不提供 Git Console 或持久化日志 | 是 | 三段证据：①**脱敏**=宿主 `GitOutputSanitizer`（URL 凭据 / Authorization / 具名密钥）+ 单测 `隐藏网址凭据和常见敏感字段`；②**限长**=`GitCommandRunner` 的 `BoundedOutput.IsTruncated`，超限会追加"Git 输出/错误输出超过上限，已截断。"；③**无 Console/持久化**=harness `§10.2 界面上没有 Git Console / 控制台入口`（枚举 a/button/tab/tree-row/menu-item 的文字与 href） |
| 8 | §10.2 | 同一错误在外部状态未变化时不重复弹出 | 是 | `同一错误连续出现时不重复弹出`（`ddBefore.shows`/`ddRepeat.shows` 对照） |
| 9 | §10.3 | 禁用控件必须有可发现原因，例如悬停说明或相邻文字 | 是 | `§10.3 禁用控件带可发现原因`、`§10.3 原因说明用户该做什么`、`§10.3 补充的说明都是可读句子` |
| 10 | §10.3 | 工具栏中暂时不可用的稳定命令可以禁用并保留位置 | 是 | `§10.3 暂时不可用的工具栏命令保留在原位置（禁用而非移除/重排）`：对比"无选中（禁用态）"与"已选中（可用态）"两张页面的工具栏按钮顺序，要求**序列完全一致**且只有 `显示 Diff` 的 disabled 不同 |
| 11 | §10.3 | Git 操作会话中"不适用于当前状态"的 Continue、Skip 和 Abort 必须隐藏 | 是 | `§10.3 宿主不适用的会话动作被隐藏而不是禁用占位`（构造 `canAbort/canSkip/canContinue/supportsContinue` 全 false 且 `hasConflicts:true` 的会话：只剩 `close`，按钮文字里没有 Continue/Skip/Abort）+ **负向对照** `§10.3 负向对照：宿主提供的动作确实会渲染出来`（同一套断言在宿主提供动作时必须看到三个动作，证明前一条不是空断言） |
| 12 | §10.3 | 产品明确不支持的能力不得以禁用占位出现 | 是 | 清单来自 §7.10（Force Push / GitHub / GitLab / 子模块等）：既有 `不出现被排除的产品入口`（整页文字扫描 7 个词）+ 本轮新增 `§10.3 分支弹层不以禁用占位出现不支持的能力`（在分支弹层这个最可能出现这类入口的地方再核对一遍） |
| 13 | §10.4 | Reset Hard、Rollback、删除分支或标签、删除 Stash、移除 Worktree 必须显示具体影响 | 是 | `§10.4 显示具体影响`、`§10.4 影响说明使用真实已跟踪改动数`、`回滚必须显示具体影响并确认`、`§10.4 危险确认基线给出具体影响与动作名按钮`（Stash 删除）；分支/标签删除在同块 |
| 14 | §10.4 | 确认按钮使用动作名称，例如"确认 Reset Hard"，不得只写"确定" | 是 | `§10.4 危险确认按钮使用动作名称`、`§10.4 不使用泛化的「确定」`、`§10.4 Hard 使用危险确认样式且按钮写动作名` |
| 15 | §10.4 | 未跟踪文件删除必须明确说明进入 Windows 回收站 | 是 | `未跟踪文件的回滚说明进入回收站`（+ 对照 `已跟踪文件的回滚不显示回收站说明`） |

**本节结论（带分母，按表格逐行统计）**：15 条**全部有可核对断言（15 是 / 0 部分 / 0 未覆盖）**。
补断言是纯增量工作，优先级：第 11 条（构造宿主不提供动作的会话）→ 第 12 条（先定"不支持能力"清单）
→ 第 7 条（断言无 Git Console/日志与输出上限）→ 第 10 条（禁用项原位）→ 第 1 条（空态无插画）。

### 2.9 项目级"操作序列 → 界面状态"对照（⑥）

除逐条用例外，另有整段用户流程与"操作后界面状态"对照：

- **关键区域像素对照**（`⑥`）：同一条操作序列里对**标题栏 0..44 与状态栏 736..760** 两个关键区域
  取真实截图并逐字节比较 —— 悬停树行、相同状态的外部变化刷新之后**像素完全不变**（§4.4 / §125），
  而"切换到另一个文档"这个对照操作必须让两个区域都发生变化（证明探针有分辨力）；实测 3 条全绿；
- 「跨模块用户流程：单点都对，组合起来未必对」块：一段连续操作后比对界面状态；
- 「规格 §6.6：状态所有权」块：对每个"明确禁止变化"列做操作后快照比对；
- 「异步竞态：晚到的旧响应必须被丢弃」块：注入乱序响应后比对最终状态；
- 「区域刷新必须释放上一轮绑定在 document/window 上的监听」块：连续刷新后断言监听未累积；
- 真机侧：`tools/audit/verify-window-chrome.ps1` 用真实鼠标拖动/双击/边缘缩放/关闭窗口
  （11 条断言），`tools/audit/dump-live-dom.ps1` 做同场景的 DOM 对照。

## 3. 差异与未覆盖（逐条）

### 0.1 本轮验证汇总与剩余未闭环项（2026-09-21，最后一批改动之后重跑）

| 闸门 | 命令 | 本轮读数 |
| --- | --- | --- |
| 构建 | `dotnet build src/Augit.Shell -c Release` | **0 警告 / 0 错误** |
| 外壳单测 | `dotnet test tests/Augit.Shell.Tests` | **74/74** |
| 核心单测 | `dotnet test tests/Augit.Core.Tests` | **86/86** |
| 实时外壳断言 | `node tools/audit/live-shell.spec.cjs` | **1014/1014（未执行 0 项）**（2026-09-21 本轮重跑，摘要行 `live-shell 通过 1014 项断言`） |
| 真机全场景巡检 | `verify-acceptance.ps1` | **54/54 PASS（干净仓库）** + `diff-boundary` **1/1**（含改动工作区） |
| 打包 | `tools/release.ps1` | zip **2,795,619 B** / setup **4,386,825 B** / `sha256sum -c` **两项 OK** / 包内 32 条目 0 可疑 / 包内 `live-data.js` 与仓库**逐字节相同** |
| 交互基线 | `check-interactions.cjs` | `INTERACTIONS_BASELINE_OK`（surfaces 6 / jumps 4 / feedback 8 / sequences 6 / gaps 4；CHECKED 18） |
| 文档数字自洽 | `check-doc-claims.cjs` | `DOC_CLAIMS_OK`（§2.0 分节之和 = 合计、逐块计数一致） |
| 共享资源一致 | `verify-ui-assets.ps1` | **PASS** |
| 脚本编码 | `verify-script-encoding.ps1` | **PASS（12 个脚本）** |
| 像素逐页表 | `gen-coverage-table.cjs` | 55 行，生成幂等；偏高项复核样例 `repository-search`（heatmap 入仓） |

**明确未闭环（不在上表"通过"之列）**：

1. **⑦⑧ 逐页 PyCharm 对照**：目前只有 2 个面（主窗口 chrome、设置对话框）与设置对话框内部几何；
   48 个视觉稿页面的逐页对照表尚未产出。
2. ~~**④ 其余场景的 heatmap 复核**~~ → **已完成（第 392 轮）**：**12 个高值场景全部重测**，
   证据（两侧截图 + heatmap）入仓 `artifacts/pixel-review-20260921/`，逐场景读数与判读见 §1.5；
   其中 7 个复现（|Δ| ≤ 0.08）、3 个偏差 0.76–0.87（live 数据状态差异）、2 个新增。
   其余 43 行的记录值保留，但已注明"属那一次运行、不可当常数"。
3. ~~**⑯ 三块采集（外部阻塞）**~~ → **已解除并采到（第 391 轮）**：页面正文（`Editor › Font`、
   `Console Font`、`Color Scheme Font`）与树底都已入仓 `artifacts/pycharm-16-final/`。
   **前台锁不再需要用户介入**：脚本自己发一次 ALT 抬起（取得设置前台窗口的资格）+
   `AttachThreadInput` + `BringWindowToTop`/`SetForegroundWindow`/`SwitchToThisWindow`，
   实测 `IS_PYCHARM=True` 后点击/SendKeys/PrintWindow 全部生效；并需 `SetProcessDPIAware()`，
   否则矩形/光标/截图都是虚拟化坐标（900×700 而非 1575×1225）。
4. **§2 的"部分/未覆盖"行**：见 §2.0 的逐节分母（用例行 406 条，其中条文 336 条有逐条行；
   未成行的仍是 §4 的 15 条视觉/令牌类，以 §1.2 的像素表与令牌表核销）。

> **证据入库范围（第 396 轮修正）**：`.gitignore` 原先整体忽略 `artifacts/`，于是文档里
> "证据在 `artifacts/...`" 这类声明**在 clone 出来的仓库里根本无法核对**（本轮发现并修正）。
> 现在**交付文档明确引用的核对证据已入库**：
> `artifacts/pycharm-baseline-20260919/`（PyCharm 基线截图）、`artifacts/pycharm-interactions-16/`
> （交互采集与树段）、`artifacts/pycharm-16-final/`（本轮三页正文 + 树底 + 工具窗/主窗口）、
> `artifacts/pixel-review-20260921/`（④ 的 12 场景两侧截图 + heatmap）、
> `artifacts/pycharm-compare-20260919/`（Augit 侧 DOM 几何/配色 JSON 与截图）。
> 其余 `artifacts/*`（构建产物、打包 zip/setup、54 场景验收截图）**仍不入库**，需要时按命令重跑。

### 1.5 偏高项复核（④，第 392 轮：12 个高值场景全部重测 + heatmap 入仓）

方法同 §1.1（同一对照器、三条带：titlebar `0:44`、statusbar `H-24:H`、content `44:H-24`），
证据在 `artifacts/pixel-review-20260921/<scene>/{<scene>-mockup.png,<scene>-live.png,heatmap.png}`。
`layoutPercent`（判据）/ `textPercent` / `visiblePercent`（**非判据**）本轮读数与 §1.1 记录值对比：

| 场景 | 本轮 layout | §1.1 记录 | Δ | 判读 |
| --- | ---: | ---: | ---: | --- |
| `quick-open` | **2.30** | 2.30 | 0.00 | 复现；文字差异为主 |
| `repository-search` | **2.08** | 2.84 | −0.76 | 文字差异 + 结果面板首/末两处通栏色带（heatmap 已核） |
| `git-compare` | **1.72** | 1.71 | +0.01 | 复现 |
| `diff-boundary` | **1.68** | 1.60 | +0.08 | 复现 |
| `commit-diff` | **1.67** | 1.59 | +0.08 | 复现 |
| `history-diff-loading` | **1.61** | 1.61 | 0.00 | 复现 |
| `diff-loading` | **1.55** | 1.47 | +0.08 | 复现 |
| `git-compare-empty` | **1.42** | 1.41 | +0.01 | 复现 |
| `git-history` | **1.38** | —（未列入旧高值表） | — | 本轮新增读数 |
| `blame` | **0.63** | 1.44 | −0.81 | live 侧打开的文件/滚动状态与旧记录不同 |
| `markdown-preview` | **0.55** | 1.42 | −0.87 | 同上（预览内容随打开的文件变化） |
| `search-limited` | **0.50** | —（未列入旧高值表） | — | 本轮新增读数 |

**结论（有分母）**：12 个高值场景里 **7 个复现（|Δ| ≤ 0.08）**，**3 个偏差 0.76–0.87**
（`repository-search`/`blame`/`markdown-preview`，均由 live 侧数据状态差异解释），
2 个是本轮新增读数。**没有任何场景出现"整页错位"**：heatmap 上差异一律是字形级 + 少数内容带，
与 §1.1 的"数据差异"判读一致。行值必须带"哪一次运行"，已在 §1.1 与本节写明。

### 2.10 非"是"条文的结论清单（⑨/⑩，逐行；生成器 `tools/audit/gen-clause-conclusions.cjs`）

**分母**：§2.1–§2.8 里状态为**非"是"**的条文共 **125** 行，分类如下：

| 结论类别 | 行数 | 含义与下一步 |
| --- | ---: | --- |
| A 实现已有、仅缺断言 | 88 | 补 harness 断言 → 可转"是" |
| B 只有像素或间接证据 | 4 | 把像素/间接证据升级为可复跑断言 |
| C 未覆盖（无断言也无观察） | 13 | 补断言或明确不做（需用户裁决） |
| D 其它部分覆盖 | 20 | 定位子条件后补断言 |

| 小节 | 条文 | 状态 | 结论类别 | 缺什么（原文摘录） |
| --- | --- | --- | --- | --- |
| §2.6 | §7.13 | 未覆盖 | C 未覆盖（无断言也无观察） | 只找到"外部改变**当前差异文件**会重新请求"这类 diff 侧断言；**没有**针对"外部解决冲突后会话列表在 500ms 内增量更新"的断言 —— 需要构造"冲突数从 2 变 1"的宿主推送并断言列表行数与耗时；**宿主侧已有单测** |
| §2.6 | §7.17 | 部分 | A 实现已有、仅缺断言 | 三种取值与"窗口显示真实主题"有断言（`设置窗口显示真实主题`），但"立即预览"与"取消后恢复原设置"**没有断言** |
| §2.6 | §7.17 | 部分 | D 其它部分覆盖 | 等宽字体/字号有断言（`切页保留未保存编辑`、`跨分类的改动一起保存`）；**默认换行按用户 2026-09-19 裁决不实现该持久化行**（只保留正文查看时的自动换行），裁决见 §3.2 第 2 条；正文字体字段随分类存在（`hasUiF |
| §2.6 | §7.17 | 部分 | D 其它部分覆盖 | 第 2 条"分类严格等于四项"在结构上排除了这些分类，但**没有**像 §10.3 那样对这些词做清单式断言（可复用同款扫描，成本低） |
| §2.6 | §7.1 | 部分 | A 实现已有、仅缺断言 | 有恢复块（`显式文档参数优先于会话恢复`、`恢复标签集合`）；**"其他标签只显示名称与状态、不建文件视图"没有断言** |
| §2.6 | §7.1 | 部分 | A 实现已有、仅缺断言 | 与 §7.6 同一族的悬停断言（`真实悬停改变行背景`、`悬停不触发宿主查询`、`选中态优先于悬停`）；**树侧"移出/隐藏清除""滚动后重新命中"没有断言** |
| §2.6 | §7.1 | 部分 | A 实现已有、仅缺断言 | 有外部变化与竞态块（`外部变化保持当前标签` 等）；**"节点身份保持""分批应用""旧任务不向新工作区插节点"没有断言** |
| §2.6 | §7.1 | 部分 | D 其它部分覆盖 | `项目树菜单条目非空且不是未接线兜底` 覆盖"已接线"；**没有"只显示已实现动作"的清单式断言**（可复用 §10.3 的写法） |
| §2.6 | §7.1 | 部分 | A 实现已有、仅缺断言 | `终端与 Git 历史互斥`、`§9.4 终端与 Git 历史互斥` 覆盖底部唯一性；**"树与正文仍可见"没有断言** |
| §2.6 | §7.2 | 部分 | A 实现已有、仅缺断言 | 滚动快照覆盖"内容变化后位置保持"；**"短文件不保留空白横向范围""收回旧偏移"没有断言** |
| §2.6 | §7.2 | 部分 | D 其它部分覆盖 | 本轮新增 `§7.2 文档工具栏 Tab 按从左到右只经过可用控件`（路径文本 `tabindex=false`、x 单调递增、经过的标签全在"可用控件"集合内、禁用项不入列）与 `§7.2 切换显示选项保留按钮焦点`（点"自动换行"后焦点 |
| §2.6 | §7.2 | 部分 | A 实现已有、仅缺断言 | 本轮新增两条：`组词期间不扫描`（compositionstart 后输入不扫描、状态清空）与 `组词结束后按最终文字重新统计`（compositionend 后状态重新出现）；**"取消组词回到原查询时复用已完成数量"仍未断言**；**已 |
| §2.6 | §7.2 | 部分 | A 实现已有、仅缺断言 | 开关行为有断言；**"12 项（忽略大小写）/3 项（全字）"的数值核对没有断言**（视觉稿侧是固定样本文本） |
| §2.6 | §7.2 | 部分 | A 实现已有、仅缺断言 | `find-state=loading` 页存在并有像素基线；**150ms 阈值、旧任务与方向队列取消、隐藏/关闭失效都没有断言** |
| §2.6 | §7.2 | 未覆盖 | C 未覆盖（无断言也无观察） | **实测缺口（第 67 轮）**：`--augit-find-height: 42px`（`mockup.css:48`，视觉稿与实时外壳**共用同一令牌**），`code-font-size` 13 → 查找条 42 / 输入框 30，1 |
| §2.6 | §7.2 | 部分 | A 实现已有、仅缺断言 | 滚动/焦点保持有断言；**"选择端点不在 UTF-8 字符内部""查找结果数更新与继续定位"没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | `文件历史标签显示真实路径`、`文件历史显示真实提交`、`文件历史不残留样例` 覆盖"复用 + 固定路径"；**清除入口本身没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | `文件历史首行为最新提交` 覆盖列表内容；**列布局、窄栏横向滚动、"不因列布局改变而重查"没有断言** |
| §2.6 | §7.9 | 部分 | D 其它部分覆盖 | 有"上下文/草稿保持"类断言族；**未逐项断言（尤其"未执行的筛选输入"与"正文位置"）** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | 空历史有断言（`§10.1 无历史…`）；**"返回后按原筛选补查""空日志不重复查询"没有断言** |
| §2.6 | §7.9 | 部分 | D 其它部分覆盖 | 有"晚到不覆盖"断言族可类比（§6）；**"改选立即取消旧预览""隐藏时取消"没有直接断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | 比较视图与文件栏有像素基线（`file-history` 0.63）；**工具栏顺序与"与引用比较同构"没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | 有隐藏/恢复类断言（`§7.16` 同族）；**Tab 顺序与"重显补查"没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | `Blame 槽位含真实日期与作者`、`Blame 行数与真实归属一致` 覆盖三列与同步；**字号扩展、横向滚动不动、完整哈希点击没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | `缺 path 的 blame 载荷不进入状态` 是其中一条；**"只接纳最后一次"的乱序用例没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | 结构在视觉稿与实现里；**Tab/Enter/Space 与"恢复工具栏/交回焦点"没有断言**；**已断言**：`§7.9 Blame 头部含"n 行归属"与关闭入口，Enter 可关闭`（头部文本含"行归属"、`aria-label=" |
| §2.6 | §7.9 | 未覆盖 | C 未覆盖（无断言也无观察） | 没有断言（需要"从 Blame 点提交 → 历史选中该提交"的用例） |
| §2.6 | §7.9 | 未覆盖 | C 未覆盖（无断言也无观察） | 同上，没有断言 |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | 双栏/单栏结构有像素基线（`commit-diff`/`git-compare`）；**"旧正文期间保留布局""失败/摘要按模式显示身份"没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | 有字号适配块（覆盖工具栏）；**"只增高两行并下移正文""窄宽度保留顺序"没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | 标签文字有断言（`复用比较标签时同步标签文字`）；**三部分独立省略、前 8 位与后缀、悬停完整引用没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | `§7.9 历史比较无差异用比较措辞` + §6 晚到不覆盖族；**"重复点击不查询""忽略空白以最后为准"没有断言** |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | 计数与导航在视觉稿与实现里；**焦点保留与 Tab 顺序没有断言**（与 §7.2#5/§7.16#9 同族缺口） |
| §2.6 | §7.9 | 部分 | A 实现已有、仅缺断言 | 工具窗有像素基线；**"取消比较"入口的出现/隐藏与"不写全局提示"没有断言** |
| §2.6 | §7.9 | 未覆盖 | C 未覆盖（无断言也无观察） | 该对话框本身未实现（§3.2 已记为"当前产品面不可达"），因此无断言 |
| §2.6 | §7.10 | 部分 | D 其它部分覆盖 | 小型输入对话框有 `组词中的 Enter/Esc 不提交也不关闭紧凑窗口` 等断言族；**分支/标签删除的影响确认断言未落到本行**（危险确认在 §10.4 块里，未按此条复核） |
| §2.6 | §7.10 | 部分 | B 只有像素或间接证据 | `smart-checkout` 有独立场景与像素行；**"停止普通切换"+影响说明+确认的断言没有落到本行**；**宿主侧已有两个单测**：`GitOperationServiceTests.SmartCheckout恢复冲突进入解决流程 |
| §2.6 | §7.15 | 部分 | A 实现已有、仅缺断言 | `Enter 打开临时预览标签` 覆盖 Enter；**上下键移动与 Esc 恢复原焦点没有断言** |
| §2.6 | §7.15 | 部分 | A 实现已有、仅缺断言 | `search-state=timeout` 有像素页与文案断言；**"结束 ripgrep 进程"没有断言**（宿主侧进程回收） |
| §2.6 | §7.15 | 部分 | A 实现已有、仅缺断言 | 浮层有像素基线；**三个下限公式没有断言** |
| §2.6 | §7.15 | 部分 | A 实现已有、仅缺断言 | 结构与像素在；**16px 固定、窄宽度换行顺序、状态换行没有断言** |
| §2.6 | §7.18 | 未覆盖 | C 未覆盖（无断言也无观察） | **实现缺口（第 402 轮实测）**：`git-unavailable` 场景下 5 个 rail 按钮里 `aria-disabled="true"` 的数量为 **0**；代码侧 `aria-disabled` 只用在 commit- |
| §2.6 | §7.3 | 部分 | B 只有像素或间接证据 | 已断言：`§7.3 模式控件 26px 步长/36px 工具栏，§7.4 JSON 复用同一控件`（DOM 实测 5 个段按钮全是 26×26、间距 0、工具栏 36px，两种文档控件垂直对齐一致）+ 像素基线（`markdown-prev |
| §2.6 | §7.3 | 未覆盖 | C 未覆盖（无断言也无观察） | `markdown-preview` 有加载态像素页？**无断言** |
| §2.6 | §7.3 | 部分 | A 实现已有、仅缺断言 | 有"后台标签不抢前台"的同类断言族（§6）；**"不预热预览"没有断言** |
| §2.6 | §7.3 | 部分 | A 实现已有、仅缺断言 | 链接侧已断言三类阻止（越界/协议不支持/锚点缺失）各自在原 `<p>` 位置替换成 `.markdown-blocked` 并带原因，预览其余 40 个段落与标题完好；**远程图片占位（`renderMarkdown` 的 `markdow |
| §2.6 | §7.3 | 部分 | A 实现已有、仅缺断言 | 原文滚动与预览阅读位置已断言（`§7.3 模式切换不丢失各自滚动位置`）；**对照比例跨模式保持、重复点当前模式不重新加载、关闭标签才结束预览会话没有断言** |
| §2.6 | §7.3 | 部分 | A 实现已有、仅缺断言 | §6 的外部更新断言族覆盖部分（`外部改变当前差异文件会重新请求`）；**"只应用最后一次渲染"和"后台不建预览"没有断言** |
| §2.6 | §7.3 | 未覆盖 | C 未覆盖（无断言也无观察） | 无断言 |
| §2.6 | §7.3 | 部分 | B 只有像素或间接证据 | 本条是**口径声明**（不是行为）：文档已按此口径写（§1.3 说明跨框架不判逐像素相等）；但"位置保持/异步时序"的断言缺失（同第 10/11 条） |
| §2.6 | §7.4 | 部分 | D 其它部分覆盖 | 已断言：与 Markdown 控件**同一套尺寸与对齐**（同一条几何断言，5 个段按钮逐值 26×26、间距 0、工具栏 36px、垂直中心相同）与禁用态（格式错误时 `disabled` + `title`）。**未断言**：图形（图标 |
| §2.6 | §7.4 | 部分 | D 其它部分覆盖 | 已断言：同一工具栏内保留按钮位置、`disabled` 为真且有 `title` 说明（harness `§7.4 格式错误默认原文并禁用格式化…`）。**未断言**："外部修复后恢复可用"与"错误条被外部修复隐藏时焦点回原文、其他焦点不变 |
| §2.6 | §7.4 | 部分 | A 实现已有、仅缺断言 | `重复点击当前模式不重置查找位置` 有断言（查找块）；**"切换模式后结果数立即更新并从新起点继续"没有断言** |
| §2.6 | §7.4 | 部分 | A 实现已有、仅缺断言 | §6 外部更新族部分覆盖；**"变无效时转交原文的焦点规则"没有断言** |
| §2.6 | §7.11 | 部分 | A 实现已有、仅缺断言 | `Stash 管理列出真实贮藏` 覆盖管理侧；**创建框的字段顺序与按钮位置没有单独断言**；**宿主侧已有单测**：`ShellBridgeStashWriteTests.创建Stash带消息并保留索引状态`（"带消息 + 保留索引"）； |
| §2.6 | §7.11 | 部分 | A 实现已有、仅缺断言 | 结构在视觉稿里；**字高容纳、只滚表单、Enter/Tab 行为没有断言** |
| §2.6 | §7.11 | 部分 | A 实现已有、仅缺断言 | 有字号适配块；**"限高只滚表单""聚焦滚入""错误不移动整窗"没有断言** |
| §2.6 | §7.11 | 部分 | A 实现已有、仅缺断言 | `远端保存失败保留窗口`、`远端保存失败显示 Git 原因`、`保存成功后远端窗口留在原地` 覆盖失败与保持；**"限高只滚右侧""Tab 滚入"没有断言** |
| §2.6 | §7.12 | 部分 | A 实现已有、仅缺断言 | 有字号适配块；**"浅克隆行换行""错误在正文换行"没有断言** |
| §2.6 | §7.12 | 部分 | A 实现已有、仅缺断言 | `clone` 块覆盖部分字段；**Tab 循环与 Enter/Esc 没有断言** |
| §2.6 | §7.12 | 部分 | A 实现已有、仅缺断言 | `clone-result` 有状态断言族；**"取消先请求结束""结束前不能重新提交"没有断言** |
| §2.6 | §7.12 | 部分 | A 实现已有、仅缺断言 | `Push 列出真实待推送提交` 覆盖列表；**勾形/行高/选中优先悬停没有断言**（`选中态优先于悬停` 已在 §7.6 修复为真跑） |
| §2.6 | §7.12 | 部分 | A 实现已有、仅缺断言 | `没有上游时保留「定义远端」入口`（含出现与保留两条）覆盖无远端分支；**"限高预留标签空间""独立滚动"没有断言** |
| §2.6 | §7.12 | 部分 | A 实现已有、仅缺断言 | §9.3 写操作状态机断言族覆盖"进行中阻止重复"；**"预览未完成不许推送""晚到不覆盖取消"没有断言** |
| §2.6 | §7.12 | 部分 | A 实现已有、仅缺断言 | 实现有 `InteractiveInputRequired` 失败类型；**"界面给出原因且不提供复制/重试入口"没有断言**；**宿主侧已有单测**：`GitRemoteServiceTests.需要交互输入时返回稳定原因且不暴露原命令` |
| §2.6 | §7.14 | 部分 | A 实现已有、仅缺断言 | `冲突标题显示真实文件名与冲突数` 覆盖计数；**上一处/下一处按钮的固定与行为没有断言** |
| §2.6 | §7.14 | 部分 | D 其它部分覆盖 | 有 `§7.14：大字号 / 窄窗口下的标题行排布` 块；**该块的具体判据未逐项核对到本行** |
| §2.6 | §7.14 | 部分 | A 实现已有、仅缺断言 | 实现里有 `conflictPending` 询问层；**该模态的选择与结果没有断言** |
| §2.6 | §7.14 | 部分 | A 实现已有、仅缺断言 | 有"晚到不覆盖"断言族可类比；**"编辑过则重新询问""保留时不重写"没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | `刷新后提交选择不变`、`提交列表节点身份保持（未重建）`、`保存提交了修改后的字号` 覆盖"选择/身份/字号"；**列表顶部锚点、横向偏移、"不触发 Git 查询"没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | 有 `提交详情无页面错误`/`提交详情不残留样例` 与只读结构；**滚动键位只动详情、阅读位置保持、切换提交回顶部、20MB 边界文案都没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | 有"晚到不覆盖"的同类断言族（§6）；**"首段先呈现""End 意图""字号宽度只重排"没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | `变化文件右键菜单与文件历史入口` 块覆盖菜单项与入口；**紧凑布局隐藏、Esc 恢复列表焦点、键盘菜单键没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | 结构在视觉稿与实现里；**除 Esc 外的键位、弹出行为、焦点转移都没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | 有焦点环断言；**Enter/Space 执行与焦点交接、极短区域不越界没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | 入口存在（§12.3 断言有可访问名称）；**"只折叠详情不刷新列表""搜索入口只交焦点给输入框"没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | 同上：结构在，**收纳行为与焦点交接没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | 列布局在视觉稿与像素基线里；**"共享文字度量""不吞掉后续列"没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | 图标复用有断言族；**"状态色不覆盖图标色""辅助技术名称含状态与文件名"没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | `历史比较打开后成为前台并显示差异`、`历史比较标签标注双方引用` 已覆盖"立即打开"；**150ms 阈值、短查询不闪现、"取消比较"入口与重试没有断言**；**已有断言（1/4）**：`历史比较文件栏显示双方引用与路径`；**未断言** |
| §2.6 | §7.8 | 未覆盖 | C 未覆盖（无断言也无观察） | 没有分页/滚动触底断言（`docs/bulk-*` 是文件树批量夹具，不是历史分页） |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | 有"晚到不覆盖"断言族可类比；**页码更新、失败重试、触发条件（横向滚动/无关按键）没有断言** |
| §2.6 | §7.8 | 部分 | A 实现已有、仅缺断言 | `git-history-graph` 有像素基线；**横向位置保持与归位没有断言** |
| §2.6 | §7.5 | 部分 | A 实现已有、仅缺断言 | 棋盘格是 `<img>` 自身的 CSS 背景（随图片矩形，天然只覆盖图片范围）；**没有断言**核对范围，"不添加编辑工具"也没有显式断言；**「不添加编辑工具」已有断言**（`§7.5 图片页只读：工具条只有缩放/适应，不含编辑类动作` |
| §2.6 | §7.5 | 部分 | D 其它部分覆盖 | `image-preview.js` 已实现 pointer 拖动 + `setPointerCapture`、方向键 32px、滚轮 / Shift+滚轮 / Ctrl+滚轮、边缘夹取与适应居中、`Escape` 解除拖动；已断言**适应 |
| §2.6 | §7.5 | 部分 | D 其它部分覆盖 | 实现里有 `wheelZoom` 余量累计 + `wheelMode` 切换与按钮动作都 `resetWheel()`；已断言"隐藏时不接受滚轮输入、可见时按档位缩放"、**不足一档不缩放**、**累计到整档缩放一档**、**单次多档合并* |
| §2.6 | §7.5 | 部分 | A 实现已有、仅缺断言 | "损坏/不再支持 → 信息页"有断言（`§10.2 图片解码失败时显示宿主原因而不是破图`、`§10.2 浏览器解不开的图片也给出原因`）；"复用预览窗口 + 保留缩放与位置 + 重新解码"没有断言 |
| §2.6 | §7.5 | 部分 | A 实现已有、仅缺断言 | `image-preview.js` 支持 `?image-state=loading` 并在画布中心渲染 `.image-loading`；已断言加载态 `.image-loading` 文案/`role=status`/**画布中心** |
| §2.6 | §7.5 | 部分 | A 实现已有、仅缺断言 | 缩小走浏览器默认的平滑采样（未显式设置 `image-rendering`），100% 时 `scale=1` 保留原像素；**没有断言**核对采样方式与 100% 原像素 |
| §2.6 | §7.6 | 部分 | A 实现已有、仅缺断言 | `真实悬停改变行背景`、`悬停不触发宿主查询`、`悬停不改变选择与复选`、`⑥ 悬停操作后标题栏与状态栏逐像素不变`；**"选中态优先于悬停"此前一直 SKIP**（场景没有选中行）——第 336 轮已在采集快照前用真实点击选中一行，该断言 |
| §2.6 | §7.6 | 部分 | A 实现已有、仅缺断言 | `前置条件：草稿/勾选/滚动已就位`、`结构性重绘后草稿恢复到输入框` 覆盖重绘保状态；**"外部修改文件后复选保留"没有断言** |
| §2.6 | §7.6 | 部分 | A 实现已有、仅缺断言 | 有字号/大字号适配块覆盖工具栏与 Diff；**提交工具窗的动作换行、输入框最小行数与"上次提交省略"没有断言** |
| §2.6 | §7.6 | 部分 | A 实现已有、仅缺断言 | 视觉稿有 Amend 行；**读取/恢复文本没有断言**；**宿主侧已有两个单测**：`GitCommitServiceTests.读取上一次提交信息保留标题和正文并去除传输末尾换行`、`…无HEAD时读取上一次提交信息返回稳定失败而不伪造 |
| §2.6 | §7.6 | 部分 | A 实现已有、仅缺断言 | `校验失败保留表单`、`查询失败保留用户草稿`、`提交成功后清空草稿与错误` 覆盖"保留/清除"；**危险色样式、悬停读全文、失败不抢焦点没有断言** |
| §2.6 | §7.7 | 部分 | A 实现已有、仅缺断言 | `首次打开差异时在文件标题行显示加载提示` 覆盖加载提示；**"保留旧正文约 200ms"的时序没有断言** |
| §2.6 | §7.7 | 部分 | D 其它部分覆盖 | 结构在视觉稿与实现里都有；**Tab 顺序断言没有**（§12.3 只覆盖"图标有可访问名称"） |
| §2.6 | §7.7 | 部分 | A 实现已有、仅缺断言 | 工具栏有差异计数与导航按钮；**"按变更块计数"与单双栏一致性没有断言** |
| §2.6 | §7.7 | 部分 | A 实现已有、仅缺断言 | `diff-status` 与比较页的文件栏已有像素基线；**"只读身份/路径悬停/不移动工具栏"没有断言** |
| §2.6 | §7.7 | 部分 | B 只有像素或间接证据 | 有 `diff-boundary` 页与像素对照（1.60），实现里有该提示；**边界行为的断言缺失**（`diff-boundary` 只做了视觉基线） |
| §2.6 | §7.7 | 部分 | A 实现已有、仅缺断言 | `④ 只提交勾选…` 不适用；有 `跨文件` 相关块但只覆盖标签身份与计数；**"查询期间禁用差异箭头、文件箭头仍可用"没有断言** |
| §2.6 | §7.7 | 部分 | A 实现已有、仅缺断言 | 结构在实现里；**撤销矩阵没有断言** |
| §2.6 | §7.7 | 部分 | A 实现已有、仅缺断言 | 视觉结构存在；**同步滚动没有断言** |
| §2.6 | §7.7 | 部分 | A 实现已有、仅缺断言 | `--comparison-gutter-width` 由实现按行号度量；**13/7/84 这三个数值没有断言** |
| §2.6 | §7.7 | 部分 | D 其它部分覆盖 | `Markdown 预览来自真实内容`、`预览不残留样例标题` 覆盖预览；**"默认显示磁盘真实文本 diff 且可从工具栏打开预览"没有端到端断言** |
| §2.6 | §7.16 | 部分 | A 实现已有、仅缺断言 | "复用同一请求"由 `只 start 一次` 覆盖；**启动中标题文案、加载期间隐藏/关闭可用、启动完成不覆盖用户已做的选择**都没有断言 |
| §2.6 | §7.16 | 未覆盖 | C 未覆盖（无断言也无观察） | 需要构造"启动中关闭/切工作区 + 晚到完成"的时序用例；当前只有轮询并发上限的断言 |
| §2.6 | §7.16 | 部分 | A 实现已有、仅缺断言 | 有 `§7.16 终端正文显示宿主输出`（真机复探 7 行真实 banner）与"隐藏只收起面板、同一会话与正文保留"；**"调整面板大小不重建 Shell"没有断言** |
| §2.6 | §7.16 | 未覆盖 | C 未覆盖（无断言也无观察） | 需要"页面 ready 前已有输出"的时序构造 |
| §2.6 | §7.16 | 部分 | D 其它部分覆盖 | **已断言**：留白 `11px 12px` 与等宽字体来自设置（`§7.16 终端正文留白 11px/12px 且字体来自等宽设置`）、行高随字号单调增长（`§7.16 终端行高随字号增长…`，实测 13→25 / 17→34）；**仍未 |
| §2.6 | §7.16 | 未覆盖 | C 未覆盖（无断言也无观察） | `项目树「在外部终端打开」调用宿主` 覆盖了外部终端入口的一处；**终端标题栏的"更多"菜单没有断言**；**第一半已有断言**（同一条 harness 断言比对 `.terminal-session` 文本 === `__augitTer |
| §2.6 | §7.16 | 部分 | D 其它部分覆盖 | 已断言：`§7.16 终端标题栏动作按可见顺序、当前 Shell 标签不入 Tab 顺序`（三个按钮 x 递增、`.terminal-session` 无 `tabindex`）、`§7.16 标题栏三个动作 Tab 循环（第三个之后回到第 |
| §2.6 | §7.16 | 部分 | A 实现已有、仅缺断言 | "收起/恢复"有断言；**"回收会话目录/进程"没有断言**（宿主侧 `TerminalSessionRegistry` 有实现，但没有断言或单测引用它） |
| §2.7 | §6 | 部分 | A 实现已有、仅缺断言 | 口径类条文：界面确实不显示该信息；**"允许但不显示"没有断言** |
| §2.7 | §6 | 部分 | D 其它部分覆盖 | 有"不拿暂留正文判断失效"的实现注释与同类断言；**"状态通知复用进行中请求"没有直接断言** |
| §2.7 | §6 | 部分 | A 实现已有、仅缺断言 | 真机巡检查"帧非空白"可间接覆盖；**"不循环触发布局"没有断言** |
| §2.7 | §6 | 部分 | D 其它部分覆盖 | 有取消入口与"取消不得显示为完成"的断言族；**"取消后读取最新磁盘事实"没有直接断言** |
| §2.7 | §6 | 部分 | A 实现已有、仅缺断言 | `加载完成后不残留加载提示` 与"不重排"族覆盖部分；**"重核三个状态"没有断言** |
| §2.7 | §6 | 部分 | A 实现已有、仅缺断言 | `§6.1 异步数据到达不得打断用户输入` 覆盖输入与焦点；**"不重选树行/不滚回视口"没有断言** |
| §2.7 | §6 | 部分 | A 实现已有、仅缺断言 | `文件已删除则移除其标签`、`关闭比较后晚到响应不写回差异` 覆盖部分；**"关闭前面的后台标签只改索引"没有断言** |
| §2.7 | §6 | 未覆盖 | C 未覆盖（无断言也无观察） | 没有断言（需要"关闭比较 → 普通标签未就绪 → 占位而非无文档提示"的用例） |
| §2.7 | §6 | 部分 | A 实现已有、仅缺断言 | `显式文档参数存在时不恢复会话`、`§6.7 对照：不带开关时恢复确实发生` 覆盖"只恢复一次"；**"收尾不重激活/不重选树"没有断言** |
| §2.7 | §5 | 部分 | A 实现已有、仅缺断言 | `Enter` 打开有断言；**方向键移动与菜单键打开上下文菜单没有断言**；**已有断言**：`树支持方向键移动选择`、`项目树右键打开上下文菜单`、`菜单键打开项目树上下文菜单`（3/4 半）；**仍未断言**：`Enter` 执行默认 |
| §2.7 | §9 | 部分 | A 实现已有、仅缺断言 | `单击改动文件只更新选中态` 覆盖"不抢编辑区"；**"未选择时保持当前正式文件或稳定空态"没有单独断言** |
| §2.7 | §9 | 部分 | D 其它部分覆盖 | 有"合并窗口"的实现注释与并发上限断言（终端轮询在途 ≤1 是不同对象）；**"文件系统与 .git 事件合并成一次查询"没有直接断言** |
| §2.7 | §9 | 部分 | D 其它部分覆盖 | 有 `write/cancel` 的取消入口与"取消不得显示为完成"断言（Clone 侧）；**"等待 Git 停止后再读真实状态"没有直接断言** |

> **口径**：本表**只做归类**，不改写任何条文的状态；"实现已有、仅缺断言"这一类是下一步补断言的队列，
> "未覆盖"那一类要么补断言、要么按纪律交用户裁决，**不写成通过**。
### 3.1 尚未执行的验证（不是已知缺陷）

| 项 | 现状 | 计划 |
| --- | --- | --- |
| 系统取消 / 捕获转移 / DPI 变化结束拖动 | 未自动化（只覆盖 Esc / 鼠标松开） | 需要真机 DPI 变化与捕获转移注入，ROI 低；真机 chrome 脚本已覆盖窗口级缩放 |
| Windows 10 22H2 实机 | 未覆盖（本机为 Windows 11） | 需要第二台环境 |
| ~~终端输入与大输出真机验证~~ | **全部验成（第 364–367 轮）**：输入链路在真机通过（`INPUT-PROBE-OK` 的输入→回显→输出→新提示符）；~4.8 MB 洪泛后的永久冻结**已定位并修好**（见 §3.2 第 15 条：根因是逐次裁剪的二次成本，改成摊销后同一探针 **6 秒**内读回 `AFTER-TRIM-SENTINEL` 与新提示符） | 无剩余计划项 |
| ~~Editor › Font 页面正文未抓到~~ | **已抓到（第 391 轮）**：`artifacts/pycharm-16-final/editor-font.png`（面包屑 `Editor › Font`：Font `JetBrains Mono`、Size `13.0`、Line height `1.2`、☐ Enable ligatures、▸ Typography Settings、实时预览；**本页没有 Fallback font 字段**）、`console-font.png`（`Editor › Color Scheme › Console Font`：Font `JetBrains Mono`、Fallback `<None>`、Size `13.0`、Line height `1.2`、☑ Show only monospaced fonts）、`color-scheme-font.png`（同构）、`tree-bottom.png`（树底：Tools → Backup and Sync → **Advanced Settings**，树到此为止） | 采集方法已写进基线 `navigationRecipe`：**搜索 `font` → 点树 → Home → Down×10（Font）/×13（Console Font）**；不要用坐标点击（树随选中滚动）也不要从"当前选中"起算（搜索后的选中项随状态变化）。此前记的 7 项（System Settings / File Colors / Scopes / Notifications / Data Editor and Viewer / Quick Lists / Required Plugins）**在本版本树里不存在**（树底已到 Advanced Settings） |

### 3.2 已确认的实现差异

1. **侧栏整体替换（§6.4 遗留）** —— 同一状态刷新时 `refreshStatusRegions` 对改动列表走原地更新，
   但项目树（非改动列表形态）仍整体替换节点；已按规格口径断言**用户可见状态**（首个可见行/选中行/滚动）
   保持不变（第 298 轮），节点身份不作为要求。
2. **设置窗口"默认换行"缺基线（规范冲突，已裁决：不实现该行）** —— `ux-spec` §7.17 要求"文件查看"提供"默认换行"，
   但 `product-spec` 只把"自动换行"列为正文查看能力（第 45 行）、没有定义持久化的默认换行设置，
   宿主 `ApplicationSettings` 也没有对应字段。用户 2026-09-19 裁决：**从设置页去掉这一行，
   只保留正文查看时的自动换行**（即维持现状）。核对结果：实现里本来就没有这一行
   （`settingsPageHtml("file-view")` 只有等宽字体/字号，正文工具栏上是「自动换行」按钮），
   因此**无需改代码**，只需把该条从"待裁决"改为"已裁决 + 不实现"，并同步
   `pycharm-interactions.json` 的 `decisions.default_wrap_conflict`。
3. **设置页"未保存修改"标记与分组折叠（PyCharm 实测差异，已裁决）** —— 用户 2026-09-19 裁决：
   **只补未保存标记，不做分组折叠**。标记已实现（分类行上的实心圆点，复用 `--augit-blue`；
   判定口径是"草稿值与磁盘值不同"，改回原值即消失）；分组折叠登记为**不做的差异**
   （Augit 分组表头保持不可折叠），见 `pycharm-interactions.json` 的 `gaps.settings-group-fold`。
4. **图片解码失败只剩破图、宿主原因无处显示（§7.5 / §10.2）—— 已修复** ——
   `toLiveDocument()` 按 `kind` 把 `Png/Jpeg/Bmp/Gif/WebP` 一律映射成 `image`，
   但宿主只有在真正解码成功时才返回 `dataUrl`：`ImageDecodeFailed` / `ImageTooLarge` 时它是 `null`，
   于是 `<img src="">` 只剩一张破图，宿主给出的 `message` 在界面上没有任何位置；
   `Gif/WebP` 本就不被宿主解码（`IsSupportedImage` 只含 `Png/Jpeg/Bmp`），同样被错判成图片。
   真机实测（修复前）：`{"status":"ImageDecodeFailed","editor":"image","imgSrcLen":0,"infoBlock":false,"text":"100% PNG 图片 · 45 B"}`。
   修复：判据改为 `status === "ImageReady"` 才按图片渲染，其余走既有的"不可预览文件"信息态（同一组件）；
   另加捕获阶段的 `<img>` `error` 兜底，覆盖"宿主只读文件头尺寸、截断 IDAT 仍返回 `ImageReady`"这一类
   （真机实测 `truncated.png`：`naturalWidth=0` → 换成信息态并给出"图片数据无法解码，文件可能已损坏。"）。
   验证：harness 2 条新断言 + 真机 4 个样本（`broken-dims` / `huge-pixels` / `truncated` 均出信息态，`ok.png` 仍正常显示）。
6. **危险确认的视觉基线缺口（§10.4，已补）** —— 视觉稿此前没有"删除 Stash / 移除 Worktree"这类影响确认页，实现里的确认层（`.info-block` + 动作名 danger 按钮）**没有任何基线可对照**。已补页面 `stash-drop-confirm`，并把正文抽成两侧共用的 `dangerConfirmBody()`（基线与实时侧同源，避免各写一套）。实测结论：Stash 删除与 Worktree 移除都有确认层，取消后不产生写入/移除；**远端删除没有影响确认**（点删除即调用 `git/remote-write delete`，实测列表 `["origin"] → []`），而 §10.4 的危险操作清单**不含**远端删除 → 登记为"规格未要求的差异"，不改实现；PyCharm 侧该行为**未采集**（需要前台，本轮未验证），不计入通过。
5. **外部变化刷新后标题栏汉堡菜单点不开（§5.1）—— 已修复** —— 汉堡按钮的点击处理挂在 `document` 上
   （标题栏节点会被区域刷新替换），但 `bindInteractions()` 每次渲染都会重新注册一份，
   而处理函数按"当前有没有菜单条"取反：**两份监听叠加互相抵消**，一次外部变化刷新后点击毫无反应。
   证据：修复前的 harness 运行在 `.titlebar .main-menu-entry` 上 8000ms 超时；
   修复（加幂等守卫，与 rail/changes/compact 等既有绑定同一模式）后，
   新增断言「外部变化刷新后主菜单仍可打开 / 再次点击收起并恢复普通标题栏」通过。

7. **图片缩放/平移：不是冲突，是我漏查了一个共享文件（已更正）** —— 第 330 轮我判定
   `ux-spec` §7.5 的 8 条缩放/平移要求与 `product-spec` 冲突、且"视觉稿画了四个按钮但全仓库没有绑定"。
   **该判定错误**：缩放/平移的完整实现在 `docs/ux-mockups/image-preview.js`（第三个共享文件，
   与 `web/src/image-preview.js` 字节一致）里 —— 步进表、fit（四边 32·dpi、上限 1）、
   pointer 拖动 + `setPointerCapture`、方向键 32px、滚轮与 Shift/Ctrl 组合、`wheelZoom` 余量累计、
   `wheelMode` 切换时 `resetWheel`、`Escape` 解除拖动、`visibilitychange` 重置、`ResizeObserver`、
   `?image-state=loading` 中心提示。我当时只 grep 了 `mockup.js` 与 `live-data.js`，**漏了第三个共享文件**。
   用户 2026-09-19 的裁决①因此在效果上等于"保留现有实现"，**不需要新增实现**；
   真正缺的是**断言**：本轮补了 7 条（fit 边距/不放大、放大取下一档、缩小取上一档、
   适应区域重居中、800% 上限并禁用、10% 下限并禁用、隐藏时不接受滚轮输入而可见时缩放），
   并把 harness 的图片桩从 1×1 换成**真能解码的 400×300**（否则几何断言会因"1px 也满足边距"而假通过）。
   更正记录：`decisions.image_zoom_scope.correction`；§2.6 的 §7.5 表已按"已实现 + 现有断言"重新定级（4 是 / 6 部分）。

8. **终端标题栏三个动作的 Tab 不循环（规格 §7.16 第 9 条，**已修复**）** —— 规格要求
   "标题栏三个动作按可见顺序循环 Tab/Shift+Tab"，实测（harness 断言
   `§7.16 当前实现的第三个 Tab 会离开标题栏（规格的"循环"尚未实现）`）：在
   `关闭终端 → 更多操作 → 隐藏终端` 之后，第三次 Tab **离开标题栏**而不是回到第一个动作；
   Shift+Tab 同样没有反向循环。`live-data.js` 里没有任何针对 `.terminal-header` 的 `keydown` 处理。
   **根因（第 343 轮定位，两次修复尝试都失败之后）**：实时外壳已有一套**区域式 Tab 顺序**
   （`live-data.js` 的 `bindRegionTabOrder()`）：它按"焦点区域"在本区域内前后移动，
   到边界时跨区域跳转，并且**遇到 `defaultPrevented` 就退出**。终端标题栏的三个动作落在它的区域/边界
   逻辑里（实测序列 `关闭终端 → 隐藏终端 → 关闭终端 → 正文 Terminal input` 正是它的产物：
   `更多操作` 不在该区域的候选列表内，于是从它出发走的是边界分支）。
   这也解释了为什么"另外加一个 Tab 处理器"两次都得不到循环：真正的顺序由这套区域机制决定。
   **修法（第 360 轮落地）**：把终端标题栏定义成一个**独立焦点区域**并标记为"自成循环"：
   `FOCUS_REGIONS` 增加 `{ name: "terminalHeader", selector: ".terminal-header", segment: "content", wrap: true }`，
   `bindRegionTabOrder()` 在 `wrap === true` 时用取模回绕（`items[(current + step + n) % n].focus()`），
   不再走"到边界就跨区域"的分支。**没有再加第二个 Tab 处理器**（前两次失败正是因为它与区域机制互相竞争）。
   验证：harness 两条断言 —— `第三个 Tab 回到第一个`（`inHeader === true` 且标签等于第一个动作）与
   `Shift+Tab 首个回退到最后一个`；正文 Tab 仍交给 Shell（`宿主收到 \t` 不受影响）。
   **如实记录一个规格层面的后果**：按"三个动作循环"实现后，焦点进入标题栏就**只能在三个动作之间循环**
   （Tab 与 Shift+Tab 都是回绕），键盘用户无法再用 Tab 离开标题栏 —— 这是规格字面要求的结果，
   不是实现取舍；如果希望"循环 + 仍可离开"，需要用户裁决（例如循环一圈后放行，或另给离开键）。

9. **JSON"原文"模式显示样例而不是真实文件（§7.4，已修复）** —— `bindJsonModes()` 里 `source`/`invalidSource`
   是**写死的样例**，而实时外壳把真实原文放在 `.code-view[data-json-source]` 上（`liveJsonDocument` 渲染时写入）。
   也就是说真机上打开一个真实 `.json` 文件后点"原文"，正文会被换成样例
   `{"sdk":{"version":"10.0.201",…}}` —— 与"原文和格式化均只读（真实内容）"直接冲突。
   修法：优先读 `data-json-source`，只有视觉稿页面（没有该属性）才退回样例。
   证据：本轮新增断言 `§7.4 切换"原文"显示真实来源、格式化保持 2 空格缩进与属性顺序`
   （注入 `{"b":1,"a":{"c":2,"d":[1,2]}}`：点"原文"后正文必须含 `"b"`/`"a"` 且**不含** `"sdk"`；
   格式化后两空格缩进且属性顺序保持）——修复前该断言会因 `"sdk"` 出现而失败。

10. **Markdown 预览的链接完全没有接线（§7.3，已修复）** —— `markdown.js` 把链接渲染成
    `href="#"` + `data-external-link` / `data-document-link`，但**全仓库没有任何代码读这两个属性**
    （`grep -rn "data-external-link" web/src/` 只命中 `markdown.js` 自己）。后果与规格两处冲突：
    点外部链接会让 WebView2 自己导航离开界面；点相对链接会跳到页首 —— 正是 §7.3 明文禁止的
    "不使用跳到页首的空锚点"，而"相对文件链接在 Augit 标签中打开"则完全没有实现。
    修法：`live-data.js` 新增 `resolveMarkdownTarget`（纯路径解析，`..` 越出工作区根返回 `null`）
    与 `openMarkdownLink`（外部走宿主 `external/launch`、相对走存在性校验后 `openDocument`）；
    `mockup.js` 的 `bindMarkdownModes` 在捕获阶段接管点击，阻止时把链接**原位**替换成
    `.markdown-blocked`（保留原标签文字 + 原因），预览其余部分不受影响。
    证据：本族 7 条断言（解析边界 6 例、相对链接开标签、外部链接交系统且 `location.hash` 不变、
    三类阻止各自原位显示原因、拖动/夹紧/重复位置/方向键、模式切换保留滚动位置）。

11. **实时 Markdown 没有加载/失败状态通路（§7.3 第 6、12 条，未实现）** ——
    `liveMarkdownDocument` 永远输出 `data-markdown-state="ready"`，实时层也没有"读取中文档"的占位
    （`grep -n "Loading" web/src/live-data.js` 只有差异/历史相关分支）。因此规格要求的
    "加载超过 150ms 时在预览区顶部显示紧凑提示、原文或旧预览继续可见""失败提示给出原因并可重试"
    在真机上**不可达**：这两条只在视觉稿里以 `?markdown-state=loading/failure` 呈现。
    读取失败时走的是通用的"清空文档并记录原因"分支（§5.2 外部删除语义），没有预览侧的局部失败提示。

12. **JSON 格式化与错误行列从未接到界面上（§7.4 第 3/4/5/6 条，已接线）** ——
    `src/Augit.Core/Documents/JsonDisplayFormatter.cs` 一直在仓库里，但它**只有单元测试在调用**
    （`grep -rn "JsonDisplayFormatter" --include=*.cs .` 的产出里除自身外只有 `tests/`）：
    桥接层的 `document/read` 载荷没有任何 `formatted` / 错误行列字段，
    `DocumentReadResult` 里也没有位置放它们。后果：真机上点"格式化"看到的其实是原文；
    格式错误时没有错误条、没有禁用"格式化"、没有行列提示——规格 §7.4 的第 4/5/6 条在真机上不可达，
    第 3 条只是"看起来对"（原文本来就带缩进时与格式化结果一致）。
    接线：`ReadDocumentAsync` 对 `TextReady` + `Json` 调用 `JsonDisplayFormatter.Format`，
    有效时下发 `formatted`，无效时下发 `jsonError: { line, column }`；
    `toLiveDocument` 白名单里搬运这两个字段（不搬的话渲染层永远读到 `undefined`，同 `dataUrl` 的教训）；
    `liveJsonDocument` 无效时加 `json-invalid`、把"格式化"按钮改为 `disabled` + `title`、
    用宿主行列渲染错误条（原生 `<button>`，因此 Tab/Enter/Space 天然同效）。
    **验证分工**：格式化算法与行列口径由 Core 单测（86 项，含中文/代理对/CRLF 共 8 组行列数据）覆盖；
    界面显示与交互由 harness 对**桩载荷**断言；`document/read` 载荷本身**没有 Shell 级单测**
    （该桥接方法从无单测，属已知空白，见 §3.1），因此本轮另做**真机端到端探针**
    （`D:\tmp-augit-cap\json-format-probe.ps1`，启动真实外壳 + CDP 读真实 DOM），实测：
    `valid.json`（压缩源 29 字符）在默认模式下渲染 **20 行**、含 `\n  "b": 1`、`b` 在 `a` 之前、
    `data-json-formatted` 长 72 字符（证明正文来自宿主而不是原文）、"格式化"按钮可用；
    `broken.json`（源含中文键，11 字符）落到 `json-invalid` + `source` 模式、正文为原文、
    "格式化"被禁用，错误条文字为**第 2 行，第 8 列** —— 与 Core 单测
    `[DataRow("{\n  \"中文\":}\n", 2L, 8L)]` 的期望**逐值一致**，即宿主 → 桥接 → 界面的整条链路口径相同。
    探针自身踩的坑：BOM-less 的 UTF-8 `.ps1` 会被 PowerShell 5.1 按 ANSI 解码，
    脚本里的非 ASCII 字面量（`"中文"`）在写文件前就已经变成乱码，
    于是第一次探针造出的 fixture 并不是中文键；改为**在 PowerShell 之外**准备 fixture 后结果自洽
    ——这也是 `tools/audit/*.ps1` 必须纯 ASCII 的原因。

13. **Markdown 模式选择没有会话记忆（§7.3 第 1/10 条，**已修复**）** —— 模式只存在于 DOM 的
    `data-markdown-mode` 上，渲染层每次重绘（切标签、外部变化、区域刷新）都重新调用
    `markdownView()`，参数是写死的默认 `"preview"`：用户切到"原文"后切走再回来就被重置成"预览"，
    与"用户切换模式后在当前会话记忆该选择""同一文件的三模式切换保留原文选择"直接矛盾。
    修法：标签上新增 `documentMode`；`setMode()` 在切换后派发 `document-mode-changed`
    （**必须显式 `bubbles: true`** —— CustomEvent 默认不冒泡，第一版就是这样静默失效的）；
    `live-data.js` 新增 `bindDocumentModeMemory()` 把它写到当前文档标签，
    并暴露 `__augitRememberedDocumentMode()`；`markdownView()` 的优先级为
    URL 参数（审计/视觉稿）> 标签记忆 > 默认。
    证据：`§7.3 会话内记住文档模式、三段式切换不创建标签`。

14. **模式控件的几何此前只靠像素基线"看着像"（§7.3 第 3 条 / §7.4 第 2 条，已补断言）** ——
    规格把"26px 按钮步长、36px 工具栏高度"写成可核对数值，但此前只有一张像素图（`markdown-preview` 1.42）
    作为证据，没有任何断言读真实 DOM。本轮补一条几何断言：Markdown 三个段按钮、JSON 两个段按钮
    共 5 个 rect **逐个**等于 26×26、相邻间距 0、两种文档的工具栏高度都是 36px、且控件垂直中心一致
    （JSON"复用 Markdown 控件"的直接证据）。图形形状本身（图标逐项复原）仍只有像素基线。

15. **终端在约 4.8 MB 输出后永久停止更新（§7.16 / §3.1 遗留，**已复现，待修**）** ——
    真机探针 `D:\tmp-augit-cap\term-io-probe.ps1`（启动真实外壳 + CDP，**先等
    `.xterm-helper-textarea` 存在**再输入，这正是此前两次尝试缺的前置条件）读数如下：

    - 挂载与输入正常：`{xterm:true, textarea:true, ready:true, shell:"Windows PowerShell"}`、`FOCUSED`；
      `Write-Output INPUT-PROBE-OK` 读回完整链路
      `PS …> Write-Output INPUT-PROBE-OK` / `INPUT-PROBE-OK` / 新提示符 —— 此前"真机输入未验"这一格**就此关闭**。
    - 洪泛命令 `$l = "x" * 400; 1..12000 | ForEach-Object { $l }`（约 4.8 MB，跨越宿主 4 MB 裁剪窗口）之后：
      视口冻结在满屏 `x`，**120 秒内 20 次采样逐字节相同**（`scrollTop 0`、`scrollHeight 242` 不变）；
      排在洪泛之后的 `Write-Output AFTER-TRIM-SENTINEL` **始终不出现**（洪泛结束后新提示符也不出现）；
      强制把 `.xterm-viewport` 滚到底再读，仍然没有 sentinel。
    - 期间应用**没有卡死**：`evaluate` 往返 6 ms、`Responding=true`、`hung=false`、
      `__augitError=null`、`__augitTerminalExited=false`、`__augitTerminalReady=true`。

    **已确定的代码事实**（不是推测）：`ShellBridge.ReadTerminal` 返回的 `data` 是"从请求偏移到
    `_terminalBuffer.Length` 的**全部剩余内容**"，并把 `offset` 报成 `_terminalBuffer.Length`；
    `live-data.js` 的 `pollTerminal()` 每 60 ms 用上一次的偏移读一次、采纳返回的 `offset`，
    且 `catch {}` **静默吞掉读取失败**（所以 `__augitError` 一直是 null）。
    **两个候选机制（尚未证实，不得当成结论）**：① 一次读取要搬最多 4 MB 的字符串，
    响应体积超过桥接消息上限后每次都失败，而失败被吞掉 → 偏移永不前进 → 永久冻结；
    ② 宿主读取侧停摆导致 Shell 写满管道阻塞，输出不再产生。
    **第 365 轮已做的改动（还不能说"已修"）**：给 `terminal/read` 加每轮上限
    （缺省 128 KB，`maximumLength` 可调）、返回的 `offset` 改成**本段末尾**而不是缓冲区末尾
    （原先"整段返回 + 报缓冲区末尾"在分批后必然跳数据）、新增 `pending` 表示剩余积压、
    `pollTerminal()` 显式带上限并**把最后一次读取失败写进 `__augitTerminalReadError`**
    （另记录 `__augitTerminalBacklog`）。
    **复验结果：仍然冻结**。第 366 轮把观测字段接到真机后，**两个候选机制都被证伪**：

    | 候选机制 | 判据 | 实测 | 结论 |
    | --- | --- | --- | --- |
    | ① 桥接消息体积超限（每轮搬最多 4 MB） | 分批 128 KB 后是否恢复 | 仍冻结（120 s × 20 次采样逐字节相同，sentinel 与新提示符都不出现） | **证伪** |
    | ② 投递输出的处理器抛异常打死读取循环 | `notifyError`（`LastNotifyError`）是否非空 | **全程 `null`** | **证伪** |

    同时确认**客户端是健康的**：冻结期间 `backlog = 0`、`readError = null`、
    `notifyError = null`、`exited = false`、会话 `running`、每 60 ms 仍在轮询 ——
    "有数据但没搬过来"与"读取一直失败"都不成立，**停顿发生在宿主/pty 的输出路径上**。
    剩下两个待区分的子机制（下一步判据已定）：(a) 后台读取循环从 `count == 0` 的 **EOF 分支**
    `return` 掉了（这条路目前不留任何痕迹）；(b) Shell 写满 pty 管道后阻塞、根本不再产出。
    下一步：给 EOF 分支加记录，并在冻结时采样 Shell 进程（是否存活、CPU 是否还在增长）。

    **机制 ② 虽被证伪，第 366 轮加的两处防护仍然保留**（它们让"处理器异常打死读取循环"
    这一整类故障变得不可能且可观测）：`ReadOutputAsync` 里 `_promptTracker.OnOutput` 与
    `OutputReceived?.Invoke` 各自包 try/catch 并记录 `LastNotifyError`；
    `terminal/read` 下发 `notifyError`，客户端暴露为 `window.__augitTerminalNotifyError`。

    **验证**：`Augit.Shell.Tests` **74/74**、`live-shell` **1013/1013（未执行 0 项）**
    （第 365 轮遗留的全量重跑已完成，客户端改动无回归）、`dotnet build` 0 警告 0 错误。

    **第 367 轮：已定位并修好。** 第 366 轮的进程采样给出了决定性读数 —— 冻结期间
    `outputEnded = false`（读取循环没退出）+ `powershell`/`conhost` 的 CPU **10 秒内零增长**
    （Shell 不是在算，而是**阻塞在写**）。据此定位到裁剪实现：`OnTerminalOutput` 每次追加都调用
    `TrimTerminalBuffer`，而它在 4 MB 缓冲区上执行 `StringBuilder.Remove(0, 超出量)` ——
    **每约 4 KB 追加都要 memmove 整个 4 MB**，是二次成本。洪泛时读取线程长期卡在里面 →
    pty 管道写满 → Shell 阻塞在写 → 终端永久冻结（且客户端一切正常、无任何错误可见）。
    **修法**：只在超过"上限 + 512 KB 余量"时才裁、且一次裁回上限
    （新增 `ShouldTrimTerminalBuffer`，并补 1 条单测）。
    **真机复验（同一探针）**：洪泛后 **1 次轮询 / 6 秒**即读回
    `PS …> Write-Output AFTER-TRIM-SENTINEL` / `AFTER-TRIM-SENTINEL` / 新提示符；
    修复前是 120 秒 × 20 次采样都没有。

16. **未接线兜底的清单式事实（P0 ① 遗留，已产出）** —— 用户要的是"还有哪些入口落进
    `guardUnwiredNavigation()` 兜底"的**有分母**答案，而不是"那两个按钮现在不落了"。
    真机探针 `D:\tmp-augit-cap\unwired-entries-probe.ps1` 在 `main-project` 场景把**全部可见**的
    `a[href$=".html"]` 入口逐个点一遍（每次都按 href 重新查询 —— 接线的入口会重绘场景，
    预先抓的节点会失效，第一版因此丢了 10/16 个入口），结果：

    | 项 | 数量 | 明细 |
    | --- | --- | --- |
    | 去重入口（分母） | **13** | 13 个不同 `href`，0 个"找不到" |
    | 被识别（不落兜底） | **2** | `branches.html`（分支芯片 `dsh`）、`settings.html`（设置） |
    | 落进兜底记录 | **11** | `workspace-open` / `quick-open` / `main-project` / `commit-changes` / `repository-search` / `terminal` / `git-history` / `text-viewer` / `markdown-preview` / `file-history` / `blame` |
    | 其中"点击后四元组信号无变化"（疑似真死候选） | **6** | `workspace-open.html`、`quick-open.html`、`text-viewer.html`、`markdown-preview.html`、`file-history.html`、`blame.html` |

    两个必须写清楚的限定：① **落兜底 ≠ 功能失效** —— 有 5 个（`main-project`/`commit-changes`/
    `repository-search`/`terminal`/`git-history`）点击后确实有变化，说明它们的动作由 target 阶段的
    既有绑定完成，兜底只是"guard 的识别链不认识它"的记录；② 那 6 个"无变化"是**候选**不是结论 ——
    信号里没有 `activeTabId`，因此"点击当前活动标签"（`text-viewer.html` 与
    `markdown-preview.html` 很可能就是编辑器标签）分辨不出来，逐个功能复核留待下一轮。
    机器可读一侧已冻结进 harness（`live-shell` 1013 → **1014/1014**；harness 场景里读到的
    分母与集合与真机同构，每次运行都会打印在断言消息里）：
    `P0① 主场景 .html 入口清单（分母 + 未接线兜底集合）`
    —— 走同一套 walker，断言 `settings.html`/`branches.html` 不在兜底集合里、
    **并注入一个合成入口 `brand-new-scene.html` 必须被判成落兜底**（非空性对照，
    否则"某入口不落兜底"可能只是 walker 没生效）。

17. **工作区芯片与"当前文件"芯片点了没反应（P0 ① 同类，第 369 轮已修）** ——
    第 368 轮的真机清单把 6 个"点击后无变化"的候选交出来，逐个查代码后确认其中两个是
    **和曾经的放大镜/齿轮完全同一类**的死入口：
    `grep -n "titlebar-context\|workspace-chip" web/src/live-data.js` 只命中窗口拖拽的控件选择器，
    **没有任何点击处理**。而视觉稿给它们的 `title` 写明了语义：
    `.top-chip.workspace-chip` → "切换工作区"、`.titlebar-context` → "快速打开文件"。
    修法：在 `guardUnwiredNavigation()` 的识别链里按各自 title 的语义接上
    （工作区芯片 → `openWorkspaceDialog()`；当前文件芯片 → `openSearchOverlay('quick')`），
    并让 harness 的清单断言**直接盯住这两个 href 不再落兜底**。
    验证：`live-shell` **1014/1014（未执行 0 项）**；真机清单读数（13 个入口里
    4 个被识别 / 9 个落兜底）留待下一轮探针复读。
    **第 370 轮已把 4 个候选逐个定性（先重建再复读，避免用旧拷贝的 web 资源得出结论）**：
    真机复读为 **13 个入口 / 4 个被识别（新接的两个芯片已计入）/ 9 个落兜底**，与上一轮预测一致；
    带"节点描述 + 逐键 diff"的探针给出：

    | href | 节点 | 归属 | 点击前后 | 定性 |
    | --- | --- | --- | --- | --- |
    | `main-project` / `commit-changes` / `repository-search` / `terminal` / `git-history` | `a.rail-button` | `.tool-rail` | bodyLength + `layoutSide`/`layoutBottom`/`overlays` **有变化** | 动作由 target 阶段绑定完成；落兜底只是"识别链不认识"的记录 |
    | `text-viewer.html` / `markdown-preview.html` | `a.editor-tab`（`wasActiveTab=false`） | `.editor-tabs` | **无变化** | **视觉稿样例标签**：实时标签的点击绑定是 `.editor-tab[data-tab-id]`（`live-data.js:6736/6765`），而这两条来自视觉稿的 `editorTabs()` 模板（`mockup.js:2352`）**没有 `data-tab-id`** —— 属 `?scene=` 模式的样例内容，不是实时文档标签的缺陷（实时标签切换由 harness 覆盖） |
    | `file-history.html` / `blame.html` | `a.toolbar-button` | `.bottom-tool` | **无变化** | 同上，来自视觉稿的提交详情模板（`mockup.js:3570`），场景模式下没有对应的实时绑定 |

    因此这 4 项**不再列为"疑似真死"**：它们的动作对象在实时路径上由 `data-*` 标识驱动，
    场景模式渲染的是视觉稿样例；如实记成"`?scene=` 模式下点样例标签无反应"这一条口径，
    并保留"实时路径由 harness 断言覆盖"的既有事实。

18. **`diff-boundary` 的真机验收 + 验收脚本两处工具问题（第 371 轮）** —— 该场景此前被排除在
    验收巡检之外（它需要**含改动的工作区** `--diff <path>`，而巡检跑在干净仓库上）。
    本轮用专门 fixture（`D:\tmp-augit-cap\diff-boundary-ws`：提交后改首行与末行、并去掉行尾换行）
    补上这一格，直连 CDP 读到的页面事实：
    `ready=true` / `errors=0` / `scene="diff-boundary"` / 边界提示"再次点击可进入下一个文件" /
    `.diff-filebar` 标题 `sample.txt → sample.txt` / `columns="diff-columns diff-boundary-columns"` /
    2 处被改行已渲染 —— **页面可达且边界态正确**。截图存
    `artifacts/diff-boundary-20260921/accept-diff-boundary.png`（PrintWindow 非白帧 0%）。
    过程中暴露并修掉两个**工具**问题（都不是应用缺陷）：
    ① 通过 `-File` 传 `-ExtraArguments '--diff','sample.txt'` 会被拼成一个 token
    `--diff,sample.txt`，应用因此弹出一个对话框（窗口类 `#32770`）且没有 CDP 目标 ——
    实测两次都失败；改用 `-Command` + `@('--diff','sample.txt')` 数组传参后窗口类恢复为
    `Augit.Shell.Window`。**结论：跨 shell 传数组参数必须用 `-Command` + 数组字面量**。
    ② `capture-surface.ps1` 的就绪判定原来是**单次读取**，对这个场景抓到了
    `PAGE ready=false` 而直接判 `PAGE_NOT_READY`；已改为**有界轮询**（最多 12 次、每次 1 秒），
    改后同一命令 `ATTEMPT 1 printwindowOk=True printwindowWhite=0%` + `SAVED` 成功。
    脚本改动后 `verify-script-encoding.ps1` 仍 PASS（12 个脚本、BOM-less 且纯 ASCII）。

18. **分支弹层的搜索框不获得焦点（§7.10，**已修复**）** —— 第 65 轮补断言时，`§7.10 点分支芯片开非模态弹层且搜索框自动获得焦点` **首跑失败**：弹层确实打开了（`overlay=true`、3 行分支），但 `document.activeElement` 是 **`<A>`（被点击的分支芯片本身）**。根因：`openBranchesPopover()` 把 `liveBranchesPopover()` 生成的覆盖层 append 到宿主后**什么都不聚焦**，而视觉稿的弹层里明明有 `input.search-field`（占位符"搜索分支和操作"）。修法：append 之后聚焦该输入框。复验：同一条断言通过，`live-shell` 1021 → **1026/1026（未执行 0 项）**。

19. **查找条不随字高扩展（§7.2，**已实测，待用户裁决**）** —— 规格要求"查找条按实际字高扩展；修改字体后保留查询、输入选择和焦点"，但实现与视觉稿**共用**一个固定令牌 `--augit-find-height: 42px`（`mockup.css:48`）：harness 两次启动对照实测 `code-font-size` 13 → 42、19 → **42**（输入框 30 → 30）。这是"规格文本 vs 共用基线固定令牌"的冲突，按纪律**停下交用户裁决**：要么改基线（查找条随字高、并给出新的几何证据），要么在规格里把固定 42px 写明。在此之前 harness 用一条**钉住断言**记录当前实现，避免被误写成"已扩展"。

20. **终端行高：配置符合、渲染值不符（§7.16，**已实测，待用户裁决**）** —— 规格写"行高 = 等宽字号 ×1.7（13px→22px）"。第 67 轮同时做了**代码核对**与**真机渲染实测**：应用确实给 xterm 设了 `lineHeight: 1.7`（`live-data.js:634-635`），配置侧与规格一致；但 xterm 渲染出的行框是 **13px → 25px（比率 1.92）**、17px → 34px（比率 2.0），**不等于规格给的 22px**。这属于"规格写死了一个渲染值，而实际渲染由 xterm 的字体度量 + 设备像素取整决定"，按纪律记为**待裁决**：要么把规格改成"配置 1.7、渲染值随引擎度量"，要么调整字号/度量让它落到 22px。harness 目前钉住实测值（13→25、17→34 且单调增长），不写成"已按 1.7 渲染"。

21. **两个"入口在但点了没反应"的按钮（§7.8 / §7.16，**已实测，未修，待口径确认**）** —— 第 68 轮补断言时抓到：① 提交历史工具栏的 `显示提交详情` 点击**无任何效果**（`grep` 证实 `live-data.js` 只有 `commitDetails` 状态、没有它的点击绑定）；② 终端标题栏的 `更多操作` 点击前后 **DOM 签名逐字不变**，与条文"「更多」菜单提供切换配置与外部终端入口"不符。两者都**保留钉住断言**；修复要先确认各自应有的菜单/面板内容，故本轮只记录、不动手。

22. **§7.8「字号增大时各部位按字高扩展、图标不变」本轮未断言（**采集受阻，非实现结论**）** —— 两次等待实时 `scene=git-history` 的 `.history-toolbar` 都超时（先只等选择器；再先等 `__augitGitReady`），说明该场景下工具条要么尚未渲染、要么不可见。**不为凑数改别的选择器**，如实记为未断言；下一步换场景（如 `commit-changes` 的 `.git-side-toolbar`）再试。

23. **项目树 Enter 不执行默认动作（§5，**已实测，未修**）** —— 第 81 轮补断言时抓到：在文件树行上按 Enter，行仍持有焦点、`window.__augitLive.document` 仍为 `null`（没有打开文件），也没有落进"未接线兜底"（它既不是 `.html` 链接、又不是无绑定的 `<button>`，兜底的两条路都不覆盖它）。条文要求"方向键移动、右键/菜单键开菜单、**Enter 执行默认动作**"，前 3 项早有断言、只有 Enter 没实现。已加**钉住断言** `§5 缺口钉住：树 Enter 当前不执行默认动作`（`live-shell` 1038/1038 通过，不把缺口写成红灯、也不写成通过）。修复需要确认"默认动作"对目录行/文件行分别是什么，故本轮只记录。

### 3.3 本阶段新增接线（原为未覆盖项）

| 项 | 原状 | 现状 | 证据 |
| --- | --- | --- | --- |
| Reset（§7.11 / §10.4 / §9.3） | 基础设施有 `ResetAsync`，但桥接层没有 `git/reset`、实时界面没有入口，只有 `--scene reset` 能渲染（产品规格第 121/127 行要求 reset 可用） | 桥接新增 `git/reset`；Git 菜单新增「Reset 当前分支…」；共享对话框负责模式/影响/进行态/键盘循环，实时层接 `git/reset` 与 `write/cancel`；影响说明显示真实已跟踪改动数 | harness 13 条 Reset 断言 + 1 条长按期间请求数断言；负向验证：把实时委派改成 `false &&` 后同一用例如实失败（`§9.3 Reset 进行中禁用重复触发并给出取消入口`） |

### 3.4 待用户裁决 / 待口径确认清单（2026-09-21，按发现顺序）

> 这一节只收**我停下不动手**的问题：规格与基线冲突、或条文要求但实现没有、且**修复方式取决于产品意图**。
> 每条给出：现象 → 可核对证据 → 两个（或三个）可选方案 → 影响面。**在你拍板前，harness 一律用"钉住断言"记录当前实现**，
> 既不写红灯、也不写成通过。

| # | 现象（一句话） | 可核对证据 | 方案 A | 方案 B | 影响面 |
| --- | --- | --- | --- | --- | --- |
| 19 | **查找条不随字高扩展**：规格要求"按实际字高扩展"，实现是固定值 | `--augit-find-height: 42px`（`mockup.css:48`，视觉稿与实时外壳**共用**）；harness 两次启动对照：`code-font-size` 13 → 查找条 **42**/输入框 30，19 → **仍是 42/30** | **改基线**：查找条随字高并在 §1.3 补新的几何证据（要动 `mockup.css` 与视觉稿，属基线变更） | **改规格**：把"按实际字高扩展"写成"固定 42px"，并在 §2 标注该条按新口径核对 | 文档查找条 + 视觉稿基线；无后端影响 |
| 20 | **终端行高：配置符合、渲染不符**：规格写"×1.7（13px→22px）"，渲染是 25px | 代码 `lineHeight: 1.7`（`live-data.js:634-635`）；harness 实测行框 **13px→25（1.92）**、17px→34（2.0） | **改规格**：写成"配置 `lineHeight: 1.7`，渲染值随 xterm 字体度量与设备像素取整" | **改实现/度量**：调整 xterm 度量让 13px 落到 22px（可能需改字号或 padding，需再量） | 终端视觉；规格文本 |
| 21 | **两个入口点了没反应**：提交历史工具栏 `显示提交详情`、终端标题栏 `更多操作` | 前者 `aria-label` 在但无点击绑定（`grep` 只有 `commitDetails` 状态）；后者点击前后 **DOM 签名逐字不变** | **接线**：按产品意图给两者各自的菜单/面板内容（需要你说明"更多"里应有哪些项） | **从界面移除**：若产品不做这些动作，则删掉入口并在规格里注明 | 提交历史 + 终端标题栏；两处 harness 钉住断言需相应改写 |
| 23 | **项目树 Enter 不执行默认动作**：条文要求 Enter 执行默认动作，实测无效 | 行保持焦点、`__augitLive.document` 仍 `null`、也没落进"未接线兜底"；第 81 轮钉住断言 | **接线**："文件行 = 打开该文件；目录行 = 展开/折叠"（需你确认这个映射） | **改规格**：删掉 Enter 这条要求（明确不提供） | 项目树键盘操作；§5 的这条从"部分"变"是/未覆盖" |
| 22 | *(非裁决，仅记录)* §7.8"工具栏随字号扩展"本轮**没能断言** | 两次等待实时 `scene=git-history` 的 `.history-toolbar` 超时（先只等选择器、再先等 `__augitGitReady`） | 换场景（如 `commit-changes` 的 `.git-side-toolbar`）再试 | — | 无（采集方法问题，非实现问题） |

| 24 | **PyCharm 侧剩余采集被"界面残留状态"挡住**（IDE 本身没卡） | 截图上编辑器区停着残留的 "Search Everywhere" 浮层（输入框内容 `otate`）＋标签栏留着第 412 轮误触打开的无关标签；`pycharm64` 1 个进程、`Responding=True`、`IsHungAppWindow=False`（CPU 7022.9 s、WS ≈2.9 GB） | **你在 PyCharm 里按几次 `Esc` 关掉残留浮层、顺手关掉多余标签**（最省事，不影响我其它工作） | **允许我重启 PyCharm**（不丢代码；会丢未保存的编辑器缓冲区与工具窗布局） | 只影响 ⑦⑧ 里 `blame`/`file-history`/`repository-search` 等 RECOVERABLE 页面的采集；在此之前我暂停向 IDE 发按键（避免再误触） |
**另外两条"已裁决但需保持"的口径**（供对照，不需再答）：
- **§7.17 设置冲突**：按规格 + PyCharm 结构落地（外观=主题+界面字体/字号；文件查看=正文字体+等宽字体/字号+默认换行；Git；终端；导航真正切页、保留草稿、搜索过滤）—— 已实施。
- **对照主题 = A（Islands Light）**：不修改你的 IDE 设置；dark 对照从未做，已如实标"未覆盖"。

