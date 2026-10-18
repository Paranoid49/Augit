# 保留范围验收状态（2026-10-18）

本文只汇总当前可复验证据和未闭环项，不把局部通过当作整体完成。详细原始记录见各链接文档。

## 1. 正常入口

| 项目 | 状态 | 证据 |
| --- | --- | --- |
| 强制非增量构建 | 通过 | `tools/start-augit.ps1` 每次输出编译日志；`normal-entry-acceptance-2026-10-18.md` |
| 构建身份 | 已记录 | `Augit.dll` SHA256 `159123F29DEF5DAF6EFE66A9D30F430D6CD22CF22912CB0527BD0BF23EA8559D`；`live-data.js` SHA256 `B4719F2F947D68434F2185BBC2505DF611592CCBCFBEA279A3849ECA9B37B45B` |
| 普通冒烟 | 通过 | 3/3；PID 13060/22396/28268 |
| 设置正常入口 30 窗 | 通过 | 深/浅/映射各 10 窗；原生 339–497ms、捕获等待 73.6–101.1ms、原生关闭 24.6–56.8ms；`settings-capture-timeout-2026-10-17.md` |
| 刷新 | 部分通过 | 轻量严格 460.9/339.6/352.8/359.5ms；全窗首轮 651.6ms、首内容 731.7ms；`refresh-500ms-acceptance-2026-10-17.md` |

## 2. 设置

### 已通过

- CDP 30 窗：Cancel/Save/OK/Esc/标题关闭、主题取消、持久化失败重试、Apply/重开、嵌套关闭。
- 无 CDP 11 次有效开窗：原生 599.2–747.5ms，捕获等待 92.1–139.9ms，未触发 500ms 超时。
- 无 CDP 7 次正常关闭：
  - Cancel 4 次：浅色 59.1ms、深色 72.7ms、同一进程重复 67.9/55.8ms；
  - Save/OK 54.8ms；
  - 标题 WM_CLOSE 86.7ms；
  - Esc（`keybd_event`）48.3ms。
- 无 CDP 重复两次录像 `foreground=true`；其余关闭录像前景 false，只作输入/时序证据。
- 失败结论已纠正：所谓“940×560 窄窗”是审计侧 DPI 二次换算错误，实际窗口 2880×1716 最大化。

### 未闭环

- **不同 DPI/跨屏**未验证。
- **2904ms 慢样本**未复现，`CapturePreviewAsync` 500ms 超时分支未被真实慢样本触发；超时后提前揭示风险未验证。
- 深色白闪只在若干样本未检出，不能外推全场景。

证据：`settings-no-cdp-2026-10-18.md`、`settings-capture-stall-2026-10-18.md`。

## 3. 刷新

- `WorkspaceFileWatcher.MergeDelay=20ms`。
- 全主窗 GDI 录像四轮：严格撤盖 651.6/421.7/402.5/390.4ms；首个新内容帧 731.7/494.7/462.5/480.2ms。
- 录像约 20.4fps，60–90ms 是采样上界，真实滞后约 11–90ms；第 1 轮即使扣一帧仍 >500ms。
- 轻量无录像严格 460.9/339.6/352.8/359.5ms，但无全窗视频。
- 外部分支变化和冲突同步已有低于 500ms 样本；全场景透明帧/白帧/光标/工具提示未闭环。

证据：`refresh-content-onset-2026-10-18.md`、`refresh-external-visible-2026-10-17.md`。

## 4. 界面英文

- 正常入口浅色/深色各 20 状态，共 40 个状态扫描，CJK 发现数 0；两轮负向对照均检出 1 项。
- 覆盖 main/text/changes/diff/terminal、settings 四页、settings 保存失败、go-to-line、stash-create、stash-manager、worktree-manager、New Worktree 空字段、无远程 fetch 失败、push、remote、Define Remote 空名称/空 URL。
- 补充独立异常态：新建分支空/空白名称显示 `Name cannot be empty.` / `Name cannot contain whitespace.`，新建标签空名称显示 `Name cannot be empty.`（PID20524/端口64786），见[名称校验](branch-create-validation-2026-10-18.md)。
- 冲突列表/只读 Diff：真实 merge 冲突夹具，PID17344/端口62807，可见正文/title/aria CJK 0；只读 Diff 无三栏/保存入口。
- Rebase 冲突 Skip/Abort：PID5140/端口50564，动作与正文英文 CJK 0；Skip 后 rebase 完成、工作区干净、正常关窗。
- Merge 冲突 Continue：PID3172/端口52119，外部解决后 Continue 可用、CJK 0；点击后正常关窗。
- Merge 冲突 Abort：PID19348/端口59750，Abort 可用、CJK 0；点击后正常关窗。
- 未覆盖 Reset/Clone/操作结果等错误与危险确认状态。

证据：`ui-english-audit-2026-10-18.md`。

## 5. 保留页面双端对照

- **合格整页配对：0**。
- 跳转行：字段 200×34、按钮 78×34、蓝色边界 1px；标题/标签/品牌图标仍为产品差异。
- 编辑页：6.082% 是共同区域最大通道差≥16 的比例，不是“还原度 93.918%”；参考有约 39 CSS 右栏，实际无右栏；+63px 与 36 CSS 文档工具栏×1.75 对应。
- 终端：八行墨迹顶差 0–1px、步长 31/32 一致、真实拖动面板；标签已按用户裁决显示 `Local`、悬停当前 Shell（`Custom terminal`）。面板宽度/状态栏/完整内容仍待同态参考。
- 54 页及全部模态子状态仍未按模块完成；页面数不是全状态分母。用户 2026-10-18 已选择“提供同态参考”，待参考输入后继续整页配对。

证据：`editor-page-partial-2026-10-17.md`、`pixel-pair-inventory-2026-10-14.md`、`terminal-matched-panel-2026-10-17.md`、`goto-line-current-pair-2026-10-18.md`。

## 6. 阴影

- 系统阴影有效四边定量：实际有阴影，左右接近参考，顶部/底部偏弱。
- 旧“无阴影”结论撤回；无收益的 DWM/自绘实验不重复。
- 深色、非活动态、完整四角未闭环。
- “阴影差异已接受”缺少明确用户批准依据，只能登记为已知差异，不能据此验收或扩大豁免。

证据：`shadow-reference-settings-2026-10-17.md`、`shadow-difference-registration-2026-10-18.md`。

## 7. 需要用户裁决或输入

1. **终端标签**：已裁决 `Local` + 悬停当前 Shell，并已实现/验证；该冲突关闭。
2. **54 页双端参考**：用户已选择提供同态参考（无右栏、同项目面板宽度、同工具栏/标签/选中项）；待参考输入。
3. **阴影**：用户已选择登记为已知差异，不宣称验收。
4. **跨屏 DPI**：提供第二显示器/可切缩放环境，或接受本轮不验证。
5. **2904ms 慢样本**：提供原始复现操作/日志，或接受超时分支仍未验证。

## 8. 清理

- `cleanup-check-31.json`：68 个本批 PID 全部不存在（PID25584 的复用 `svchost` 也已退出）。无 `Augit-no-cdp-*`/`Augit-ui-english-*`/`Augit-branch-validation-*`/`Augit-conflict-readonly-*`/`Augit-conflict-skip-*`/`Augit-conflict-continue-*`/`Augit-conflict-abort-*` 等本批临时目录，无活动 `Augit`；历史 `Augit-terminal-reference-*` 未删除。
- 未触碰用户 PyCharm PID 22768；未删除 `.tmp/` 或其他历史目录。
