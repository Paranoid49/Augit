# live-shell 测试债务：等宽字体控件移除后的过期期望（2026-10-20）

本文件登记 2026-10-20 运行完整 `live-shell.spec.cjs` 时发现、且**与本批 Diff 加载改动无关**的既有测试债务。目的不是把失败改成通过，而是让后续批次能按产品裁决逐段恢复套件。

## 1. 运行方式与基线

```powershell
node tools/audit/live-shell.spec.cjs D:/github/universe-genesis-sandbox/node_modules/playwright-core
```

- 2026-10-20 在干净 HEAD `83650e1` 的临时 worktree 复跑，得到同一处失败 ⇒ 不是本批改动引入。
- 临时 worktree 已删除，未改参考仓库或用户配置。

## 2. 已修正的过期期望

| 位置 | 旧期望 | 现行为与依据 |
| --- | --- | --- |
| 提交详情等待 | `window.__augitCommitLoaded === "aaa1111"` | `live-data.js` 已按注释「刷新判据使用完整提交身份」写 `commit.fullHash`，桩数据为 `full-head-hash`；改为等完整哈希。 |
| Settings/File View | 查找 `[data-setting="codeFontSize"]` | 2026-10-15 用户裁决固定 JetBrains Mono 13 并移除等宽字体/字号控件；改为断言 `[data-fixed-code-font]`=`JetBrains Mono`、`[data-fixed-code-size]`=`13`，且无编辑控件。 |
| 字体段落（界面/等宽字号、行高、字族、终端） | 等宽字号可改 17/22、行高随 17 增长、字族可改 Courier New、终端跟随 22、越界后仍保留 17 | 桩的 `settings/write` 对齐 `ShellBridge.WriteSettingsAsync`：任何写入后 `monospaceFontFamily=JetBrains Mono`、`codeFontSize=13`；正文行高断言改为固定 13 的 **22px**（`ceil((ceil(13×1.02)+ceil(13×0.3))×1.2)`）；终端断言固定 13/JetBrains Mono；删除已随冲突编辑器移除的 `.conflict-block` 死类测量。 |

## 3. 当前阻塞与剩余债务

- 2026-10-20 已修正：完整哈希、File View 固定等宽、字体段落、紧凑输入窗口 Tab（产品缺陷，见[修复记录](compact-dialog-tab-fix-2026-10-20.md)）、Stash 创建框的 Windows 按钮顺序与 `.stash-check-row` 结构、Stash 限高视口（300px，创建框自然高度约 331px；420px 已不构成约束）、终端启动标签（固定 `Local · Starting…`，Shell 名在 title/aria）、删除依赖已移除“等宽字号控件”的横向范围重算断言。
- 本批继续修正：
  - 第 9727 行完整哈希期望已确认通过；第 27800–27960 行的 Settings 段落按“只读固定等宽”重写（结构、草稿、保存、搜索），并给只读固定行补 `data-settings-search` 搜索索引（[记录](settings-search-fixed-code-2026-10-20.md)）。
  - 空名称紧凑窗口 Tab 断言改为跳过禁用 OK（`field,cancel,close,field`）；4 次 Tab 后焦点在 Cancel，Shift+Tab 反向回输入框。
  - Ctrl+G 用 `data-compact-dialog` 属性判标题（header 首个 span 的 innerText 在该场景读到 “A”，不能作为标题判据）。
  - 终端 Esc 用例先打开文档；同时修复产品缺陷：`focusEditorFromTerminal()` 会选中 Markdown 预览模式下隐藏的 `.markdown-source`，`focus()` 空操作导致 Esc 被吞但焦点不动；改为只选可见正文（[记录](terminal-escape-focus-fix-2026-10-20.md)）。
  - 删除 r69 中三栏编辑器移除后残留的 `panes` 引用；Blame 场景补 `blame=docs/notes.txt` 参数并对缺失关闭按钮做守卫。
  - 终端标题行断言补上 `largeActions ? icon-hit+8`：32px 界面字号下放大动作区使 token=85，旧公式只算 `max(38,h+12)=53`。
  - 差异导航 fixture 现为纯修改（`changed` 行，两栏各 1 行）：变更单元格期望由 4 改为 2，块数仍为 1。
  - 日志右键 “New Tag…” 标题按权威 `commit.asString()` 用**短哈希**（产品改为 `openTagDialog(short, full)`，建标签仍用完整哈希）；字段文案按权威 `Enter the name of new tag`、空白校验按英文提示断言。
  - **正常入口实测补漏**：设置搜索虽然切到 File View，但未命中的分类行因 `display:flex` 盖掉 `[hidden]` 仍然可见；补 CSS 并新增一条“未命中分类行计算样式为 none”的断言（check 计数 1231→1232）。
- **本轮最后状态**：`run-81` 复跑中；引用比较无差异基线页已能打开，但该用例的 `text` 同样用 `replace(/\s+/g,'')` 压掉空格（日志 `Thesereferencesha...`），整句 `includes` 仍失败；与最终说明一致改为压成单空格 + trim。待复跑确认。
- 套件在第一个未处理断言处即停止；**本批不宣称 live-shell 通过**。

## 4. 重写依据（已用于字体段落）

- **宿主写入契约**（隔离设置文件实测）：`settings/write` 写入 `fontSize:16` 与 `monospaceFontFamily:"Courier New"`、`codeFontSize:17` 后，磁盘保存为 `textFontSize:16`、`fontSize:13`、`monospaceFontFamily:"JetBrains Mono"`；读取返回 `codeFontSize:13`、`monospaceFontFamily:JetBrains Mono`。
- **正文行高**：真实正常入口在 JetBrains Mono 13 下 `--code-line-height` 为 **22px**（`design-system.md` §5.1），终端基础行格才是 18px。旧测试断言的 18px 是桩默认 `Cascadia Mono` 走 canvas 度量的结果。
- 字体度量在 `document.fonts` 完成后异步更新，测试在保存后等待行高稳定为 22px，而不是立即读取。

## 5. 下一步

1. 复跑 `run-25`，继续处理后续第一个失败；终端标题行公式已按 `mockup.js:234` 的实际实现补上 largeActions 分支。
2. 逐段处理其余旧结构（尤其仍按已移除控件/场景缺参数写的断言）。
3. 套件恢复后，把它作为 Diff/设置等公共状态改动的回归门；在恢复前，用 `mockup-scenes`、`verify-ux-*` 与定向单元测试作为本批证据。
