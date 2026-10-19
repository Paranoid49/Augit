# Smart Checkout 页面级同态配对与影响说明修复（2026-10-19）

## 1. 结论

用同一隔离夹具构造“工作区有本地改动、目标分支会覆盖该文件”的覆盖风险，两端都打开了 **Git Checkout Problem / Smart Checkout** 对话框：

- 实际：标题 `Git Checkout Problem`，受影响文件 `app.py`，说明本地改动会被检出覆盖、Augit 会临时 stash 并在检出后恢复；按钮 `Don't Checkout` / `Smart Checkout`。
- 参考：标题 `Git Checkout Problem`，`1 file` 树中的 `app.py`，说明 PyCharm 会 shelve 改动、检出后 unshelve；按钮 `Force Checkout` / `Smart Checkout` / `Don't Checkout`。
- 两端同一语义状态（覆盖风险确认），登记为**页面级同态状态配对（已知差异，非像素合格）**。页面级配对 **48 → 49**，像素合格仍 **0**。
- 发现并修复一处实际侧缺陷：影响说明复用 `.commit-meta` 的单行 `white-space: nowrap`，在对话框右缘被截成 `... and resto`，没有显示 restore 影响。现改为 `normal` 换行，正常入口复验完整显示。

## 2. 夹具与身份

- 隔离夹具：`D:/github/Augit/.tmp/smart-checkout-2026-10-19/fixture`，仅在该目录执行 Git 写操作。
- 基线提交 `95a97ee test: main baseline`；分支 `feature` 提交 `0eb7e2d test: feature change`，修改 `app.py` 末行。
- 回到 `main` 后本地修改 `app.py`（同一文件），形成覆盖风险；`git status` 为 `M app.py`。
- 实际：正常入口 PID 23024（首次）、14576（修复后）、20156（动作验证）、5980（最终身份复验）；2880×1716、DPI 168、浅色。
- 参考：PyCharm 2026.2.1 / build PY-262.9437.214，PID 3152；同一夹具。
- 实际构建 `ProductVersion=0.1.0+da572899…ce5aa`；未启用 CDP。

## 3. 页面证据

- 修复前：`actual/actual-smart-checkout.png` 中说明止于 `... and resto`，是本次发现的截断缺陷。
- 修复后：`actual/actual-smart-checkout-fixed.png` 完整显示两行：
  `Local changes to these files would be overwritten by checkout. Augit can stash them, check out, and restore them afterward.`
- 最终身份复验：注释修正后的 CSS 哈希 `A654FB57…444C` 已由正常入口 PID 5980 加载，`actual/actual-smart-checkout-final-identity.png` 仍完整显示同一说明。
- 参考：`reference/reference-smart-checkout-max.png`，完整显示：
  `Your local changes to the following files would be overwritten by Checkout. PyCharm can shelve the changes, Checkout and unshelve them after that.`

## 4. 动作验证

- 实际取消：点击 `Don't Checkout` 后对话框关闭，分支仍 `main`，`git status` 仍 `M app.py`，`git stash list` 为空。
- 参考取消：点击 `Don't Checkout` 后分支仍 `main`，`M app.py` 保留，无临时 stash。
- 实际 Smart Checkout：把本地改动改为在 `app.py` 顶部添加 `# local note`（与 feature 末行改动不冲突），点击 `Smart Checkout` 后：
  - 分支切到 `feature`；
  - `app.py` 同时包含 `# local note` 与 `# feature branch`；
  - `git stash list` 为空，工作区仍有 `M app.py`（本地改动被恢复）。
  - 证据：`actual/actual-action-after.png`、`actual/actual-action-file.png`。
- 参考 Smart Checkout 的“确认后执行”本批没有执行；参考只验证了取消 no-op。页面配对是确认对话框状态，不把实际动作成功外推为参考动作实测。
- 默认焦点未在本批取得可复核的键盘证据：向主窗发送 Enter 没有命中对话框，随后改用鼠标取消。代码与既有自动化测试仍声明默认焦点在取消，但本次不把它算作新验收项。

## 5. 已知差异

| 项目 | 实际 | 参考 | 处理 |
| --- | --- | --- | --- |
| 按钮 | `Don't Checkout` / `Smart Checkout` | `Force Checkout` / `Smart Checkout` / `Don't Checkout` | Force Checkout 是已批准不实现的破坏性能力，登记差异 |
| 说明文案 | Augit can stash / check out / restore | PyCharm can shelve / Checkout / unshelve | 产品命名差异 |
| 受影响文件 | 单行 `app.py` 路径框 | `1 file` 树 + `app.py` | 结构差异 |
| 尺寸 | 约 632 CSS 宽 | 约 513 CSS 宽 | 几何差异，未做像素合格结论 |
| 操作进度 | 无独立进度条 | 状态栏有 Checking out 进度 | 参考额外结构，不新增产品能力 |

## 6. 修复与测试

- `web/src/mockup.js` 与 `docs/ux-mockups/mockup.js`：说明段落增加 `smart-checkout-impact` 类。
- `web/src/mockup.css` 与 `docs/ux-mockups/mockup.css`：`.smart-checkout-dialog .smart-checkout-impact { white-space: normal; overflow-wrap: anywhere; }`，保留 `.commit-meta` 在提交区的单行规则。
- 新增 `tools/test-smart-checkout-copy.mjs`：**5/5 PASS**（完整文案、类名、换行规则、`.commit-meta` 未被改坏、两份资产字节一致）。
- `tools/audit/verify-ui-assets.ps1`：**PASS**；`node --check` 两份 `mockup.js` 通过。
- 正常入口重新构建并实采修复后对话框；不是只改视觉稿。

## 7. 计数

- 页面级同态状态配对：**48 → 49**（新增 Smart Checkout；不是重命名已有页面）。
- 像素合格整页：仍 **0**。
- 本页已知差异保留，不宣称严格同态或像素合格。

## 8. 证据

- 实际修复前：`artifacts/pixel-followup-2026-10-19/smart-checkout-closure/actual/actual-smart-checkout.png`（`CB6E2275…977F3`）。
- 实际修复后：`actual/actual-smart-checkout-fixed.png`（`9899E342…6995`）。
- 最终身份复验：`actual/actual-smart-checkout-final-identity.png`（`AA76931F…4A28`）。
- 实际动作后：`actual/actual-action-after.png`（`B7699816…04A0F`）、`actual/actual-action-file.png`（`53FC745B…8840`）。
- 参考：`reference/reference-smart-checkout-max.png`（`F115F6AF…C28`）。
- 清单：`artifacts/pixel-followup-2026-10-19/smart-checkout-closure/evidence-manifest.json`。

## 9. 清理

- 实际 PID 23024 / 14576 / 20156 / 5980、参考 PID 3152 均正常关闭。
- 临时夹具已删除；参考 `trusted-paths.xml`、`recentProjects.xml`、`window.state.xml` 均从启动前快照恢复并逐文件哈希一致。
- 全局 `%LOCALAPPDATA%/Augit/settings.json` 仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-167.json`。
