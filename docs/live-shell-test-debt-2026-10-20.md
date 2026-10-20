# live-shell 测试债务：等宽字体控件移除后的过期期望（2026-10-20）

本文件登记 2026-10-20 运行完整 `live-shell.spec.cjs` 时发现、且**与本批 Diff 加载改动无关**的既有测试债务。目的不是把失败改成通过，而是让后续批次能按产品裁决逐段恢复套件。

## 1. 运行方式与基线

```powershell
node tools/audit/live-shell.spec.cjs D:/github/universe-genesis-sandbox/node_modules/playwright-core
```

- 2026-10-20 在干净 HEAD `83650e1` 的临时 worktree 复跑，得到同一处失败 ⇒ 不是本批改动引入。
- 临时 worktree 已删除，未改参考仓库或用户配置。

## 2. 已修正的两处过期期望

| 位置 | 旧期望 | 现行为与依据 |
| --- | --- | --- |
| `live-shell.spec.cjs` 提交详情等待 | `window.__augitCommitLoaded === "aaa1111"` | `live-data.js` 已按注释「刷新判据使用完整提交身份」写 `commit.fullHash`，桩数据为 `full-head-hash`；改为等完整哈希。 |
| `live-shell.spec.cjs` Settings/File View | 查找 `[data-setting="codeFontSize"]` | 2026-10-15 用户裁决固定 JetBrains Mono 13 并移除等宽字体/字号控件；改为断言 `[data-fixed-code-font]`=`JetBrains Mono`、`[data-fixed-code-size]`=`13`，且无编辑控件。 |

## 3. 仍然阻塞套件的过期假设

以下条目仍按“等宽字体/字号可编辑、可改终端字号”的旧设计断言，必须按 2026-10-15 裁决重写，不能靠放宽断言通过：

- `live-shell.spec.cjs` §“改等宽字号会作用到正文但不影响界面字号”“正文行高跟着等宽字号走”“越界字号与未知主题被忽略”：当前写入 `codeFontSize` 后 UI 仍为 13，行高 18px；应按固定 13 断言，并验证 `monospaceFontFamily` 的写入被忽略/回退。
- “启动时就按已保存的字体设置渲染”“内置终端跟随等宽字体与字号”“已打开的终端随等宽设置变化”：`code-font-size=17` 启动后仍为 13、终端字体固定 JetBrains Mono；应按固定值重写。
- 第 6760 行附近、第 9310 行附近、第 27800–27960 行附近仍有 `[data-setting="codeFontSize"]`、`monospaceFontFamily`、搜索命中 `file-view` 等旧结构；需逐段按当前简化 Settings 重写。
- 这些段落未改，套件仍在第一次遇到它们时失败；**本批不宣称 live-shell 通过**。

## 4. 2026-10-20 晚间补充核对（未完成重写）

在尝试重写字体段落时做了两项实机/源码核对，供下一轮直接使用：

- **宿主写入契约**（隔离设置文件实测）：`settings/write` 写入 `fontSize:16` 与 `monospaceFontFamily:"Courier New"`、`codeFontSize:17` 后，磁盘保存为 `textFontSize:16`、`fontSize:13`、`monospaceFontFamily:"JetBrains Mono"`；读取返回 `codeFontSize:13`、`monospaceFontFamily:JetBrains Mono`。
- **正文行高**：真实正常入口在 JetBrains Mono 13 下 `--code-line-height` 为 **22px**（`design-system.md` §5.1：`ceil((ceil(13×1.02)+ceil(13×0.3))×1.2)=22`），终端基础行格才是 18px。旧测试断言的 18px 是桩默认 `Cascadia Mono` 走 canvas 度量的结果，不是固定 13 的生产值。
- 旧字体段落还测量了已随冲突编辑器移除的 `.conflict-block`（当前源码中不存在，只有 `.conflict-blocked`），应删除该测量而不是给它补样式。
- 因此重写字体段落需要同时：把桩的 `settings/write` 与 `settings/read` 对齐宿主固定值、把桩默认等宽字体改为 `JetBrains Mono`、按 22px 断言正文行高、删除死类测量，并同步其后 6760/9310/27800 附近的旧结构。本轮尝试后在提交前回退了测试改动，未留下半改状态；套件仍未通过该段落。

## 5. 下一步

1. 按 §4 的契约一次性重写字体段落与桩归一化（固定 13/JetBrains、正文行高 22、终端行格 18、无编辑控件）。
2. 重新运行完整套件，处理其后可能暴露的其他过期断言。
3. 套件恢复后，把它作为 Diff/设置等公共状态改动的回归门；在恢复前，用 `mockup-scenes`、`verify-ux-*` 与定向单元测试作为本批证据。
