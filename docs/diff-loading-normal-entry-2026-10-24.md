# 首次 Diff 加载态的正常入口修复与证据（2026-10-24）

> 结论：正常入口首次打开一个大 Diff 时，加载期间正文**原本是空白**，头部只有“Generating diff…”；
> `Loading...` 与不定进度条只在“已有旧 diff”时出现。本批修复了首次请求分支，并用真实外壳截图验证。
> 页面级像素配对仍 0，不把本文写成 Diff 加载态像素通过。

## 1. 问题

正常入口、7.08 MB / 100,000 行文本、`git/diff` 请求耗时 **2.87 s**：

- 点击后文件标题行出现 `Generating diff…`，正文区域纯白；
- 没有 `Loading...`、没有 `.diff-progress` 不定进度条；
- 请求返回后才出现“Diff truncated / Showing the first 2000 rows”和正文。

修复前桌面截图：`artifacts/diff-loading-normal-entry-2026-10-24c/loading-250.png`、`loading-700.png`
（两张正文均为空白）。

根因：`web/src/mockup.js` 的 `liveDiffView()` 先判断 `if (!diff)`；首次请求尚未返回时
`live.diff` 为 `null`，该分支返回“HEAD / Workspace + 空双栏”，即使 `live.diffLoading` 已为真。
加载分支（`Loading...` + `diffLoadingProgress()`）位于 `if (!diff)` 之后，因此首次加载永远不显示。
已有的 live-shell 断言覆盖的是“已有旧 diff → 新请求”的局部加载，不会暴露首次请求。

## 2. 修复

`liveDiffView()` 的 `!diff` 分支增加：

```js
if (live.diffLoading) {
  return `<div class="diff-layout" data-augit-loading="true">` 
    + liveDiffToolbar({ arrowsDisabled: true, busy: true, summary: diffLoadingProgress() })
    + liveConflictNotice(path) + diffFileHeader("HEAD", "Workspace", path, path)
    + `<div class="diff-columns diff-loading-columns" ...><div class="diff-loading-message" role="status">Loading...</div></div></div>`;
}
```

仍保留无加载时的空布局，不显示示例 Diff，不改变工具栏顺序、取消能力或文件栏双方引用。

自动化：`tools/audit/diff-initial-loading.test.cjs` 扩展为对 `diffLoading=true` 断言
`data-augit-loading`、`diff-loading-message`、`Loading...` 和进度条槽位；`diffLoading=false`
不得出现加载正文。**2/2 通过**。

## 3. 正常入口复验

夹具：`large.txt` 7,078,899 B / 100,000 行（每 5 行改一处），`image.bin` 1 MiB 二进制；
真实外壳、隔离设置、无 CDP 注入业务状态；项目树真实鼠标双击 Commit 轨后，在 Changes 列表
真实双击 `large.txt`。CDP 仅读 DOM/桥接阶段与截图。

| 事件 | 时间 |
| --- | --- |
| 鼠标按下（click_utc） | 17:10:45.172 UTC |
| `git/diff` 请求开始 | +275 ms |
| Loading... / `.diff-progress` 出现 | **+457 ms** |
| `git/diff` 返回 | +3,148 ms |
| 加载正文消失、Diff 正文出现 | +3,897 ms |
| 请求耗时 | **2,873 ms** |
| 正文行节点 | 3,990（显示 first 2000 rows 截断说明） |

- 修复后 250 ms / 700 ms 桌面截图：`artifacts/diff-loading-normal-entry-2026-10-24d/loading-250.png`、
  `loading-700.png`；可见工具栏右侧蓝色不定进度条与正文居中 `Loading...`，文件栏仍显示
  `Generating diff…`。
- 加载完成截图：`diff-loaded.png`；正文有 `Diff truncated` 说明和 2000 行内容。
- `live.diffLoading` 在请求返回时清除，进度条和 `Loading...` 同时消失；无残留空白页。
- 二进制 `image.bin` 双击后显示：
  `Text diff is unavailable for this file (Binary). ...`，截图 `binary.png`；
  不进入文本 Diff、不创建可编辑正文。

## 4. 边界

- 这是首次 Loading 态的行为/截图证据，不是参考实机整页像素配对；参考 PyCharm 的加载态
  仍未采，页面级配对仍 50、像素 0。
- 只测浅色、当前 DPI、最大化窗口；未测深色、窄窗、历史比较/引用比较首次加载、
  取消后重试、真实失败重试。
- 夹具的 `git/diff` 请求 2.87 s 是本地大文件样本，不是慢盘/网络结论；加载提示在 +457 ms
  出现，说明点击后到请求开始本身约 275 ms，不能写成“150 ms 后必现”。
- 桌面截图期间窗口临时 topmost，避免被 Edge 遮挡；输入是真实 Win32 双击，不是 CDP 点击。
- 未证明加载进度条动画速度、宽度与参考像素一致。

## 5. 复验

```powershell
node tools/audit/diff-loading-normal-entry.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/diff-loading-normal-entry-2026-10-24d
node --test tools/audit/diff-initial-loading.test.cjs
```

- 报告：`artifacts/diff-loading-normal-entry-2026-10-24d/diff-loading.json`（PID、点击 UTC、
  Loading 出现/消失、`git/diff` 起止、正文行数、二进制摘要）。
- 截图：`loading-250.png` / `loading-700.png` / `diff-loaded.png` / `binary.png`。
- 实例 PID 28432 由脚本 `CloseMainWindow` 正常关闭；无残留 Augit、无监听端口，
  临时仓库删除；隔离设置只写入临时副本，全局设置 SHA256 仍为 `8840E775…462CF`。
