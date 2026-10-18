# 终端标签与当前 Shell（2026-10-18）

## 问题与裁决

正常入口选择 Custom shell 后，终端标题曾显示中文 `自定义终端`。用户 2026-10-18 裁决：

- 可见标签跟随 PyCharm 参考显示 **`Local`**；
- 当前 Shell 放在悬停 `title` 中（本批为 `Custom terminal`）；
- `aria-label` 同时描述 `Local terminal: <当前 Shell>`。

该裁决解决 `ux-spec.md` §7.16“标题栏显示当前 Shell”与参考 `Local` 的冲突，不修改 Shell 启动命令和终端输出。

## 修复

- `web/src/ui-text.js` 的 `"自定义终端": "Custom terminal"` 继续用于当前 Shell 的英文名。
- `web/src/live-data.js`：
  - `window.__augitTerminalShell = uiMessage(started.displayName);`
  - `applyTerminalStartState()` 可见文本固定为 `Local`（启动中为 `Local · Starting…`）；
  - `title` 写当前 Shell（启动中附 `· Starting…`）；`aria-label` 写 `Local terminal: <Shell>`。
- `web/src/mockup.js` 的 `terminalTool()` 同步为 `Local` + `title="Windows PowerShell"`；移除 `measureTerminalHeaders()` 中 `label.title = label.textContent` 的覆盖，否则 tooltip 会退回 `Local`。
- `docs/ux-mockups/mockup.js` 与 `web/src/mockup.js` 字节一致。

## 正常入口证据

- 启动：`tools/start-augit.ps1` 强制非增量构建，临时设置副本，`terminalShell=Custom`、自定义启动命令为真实 PowerShell 八行输出；工作区为参考 fixture。
- 实际 PID 19284、端口 60293，附 CDP；DPI168、DPR1.75，窗口 2880×1716。
- 实测 `terminalLabel="Local"`、`terminalTitle="Custom terminal"`，两者均无 CJK。
- 面板 304.2→327 CSS（真实拖动 +23）；八行输出；无页面错误。
- `CloseMainWindow` 成功，端口关闭，临时目录删除。
- 证据：`artifacts/pixel-followup-2026-10-18/terminal-local-hover-3/`（`terminal-label.json`、`geometry.json`、`actual-panel.png`、`cleanup.json`）。

## 回归

- `tools/audit/terminal-matched-panel.cjs`：正常入口断言 `Local`、tooltip 当前 Shell、无 CJK；通过。
- `tools/audit/ui-text.test.cjs`：`uiMessage('自定义终端') === 'Custom terminal'` 及两处 `live-data.js` 经 `uiMessage` 的静态断言仍通过。
- `verify-ui-assets.ps1` 通过；`node --check` 通过；`DOC_CLAIMS_OK`；`git diff --check` 通过。
- 当前 `web/src/live-data.js` MD5 `D8D4998D4FC7C82433873C38E3E8A2FE`，SHA256 `B4719F2F947D68434F2185BBC2505DF611592CCBCFBEA279A3849ECA9B37B45B`。
- 终端的 14 项定向测试属于上一轮修复记录，本轮未重新执行，不能作为本轮全量通过声明。

## 边界

- 终端面板宽度、底部状态栏、标签数量/内容、光标和焦点仍未与 PyCharm 完整同态。
- 参考窗口右缘约在物理 x=2810（含约 39 CSS 右侧通知/AI 栏），实际窗口内容延伸到 x≈2870；面板宽度差约 40 CSS 主要来自参考独有的右侧栏。用户已选择“提供同态参考”继续整页对照，待参考到位后再判定。
- 命令块分隔和块间留白继续按用户已批准的有意差异保留。
- 该截图不是完整终端整页配对，不增加 54 页合格数。
