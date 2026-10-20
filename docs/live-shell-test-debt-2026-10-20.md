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

- 2026-10-20 已修正：完整哈希、File View 固定等宽、字体段落、紧凑输入窗口 Tab（产品缺陷，见[修复记录](compact-dialog-tab-fix-2026-10-20.md)）、Stash 创建框的 Windows 按钮顺序与 `.stash-check-row` 结构。
- **当前最先阻塞**：`live-shell.spec.cjs` 约第 5794 行「Stash 创建框限高时只滚表单、标题与底栏固定」失败，观察值 `{dialogTop:45, dialogBottom:376, viewportHeight:420, bodyScrollable:false, bodyScrollTop:0, header/footer 稳定}`；在 420px 视口下表单尚未溢出，未判定是测试前提过期还是限高行为缺陷，需要单独核对。
- 第 6760 行附近、第 9310 行附近、第 27800–27960 行附近仍有 `[data-setting="codeFontSize"]`、`monospaceFontFamily`、搜索命中 `file-view` 等旧结构；需逐段按当前简化 Settings 重写。
- 套件在第一个未处理断言处即停止；**本批不宣称 live-shell 通过**。

## 4. 重写依据（已用于字体段落）

- **宿主写入契约**（隔离设置文件实测）：`settings/write` 写入 `fontSize:16` 与 `monospaceFontFamily:"Courier New"`、`codeFontSize:17` 后，磁盘保存为 `textFontSize:16`、`fontSize:13`、`monospaceFontFamily:"JetBrains Mono"`；读取返回 `codeFontSize:13`、`monospaceFontFamily:JetBrains Mono`。
- **正文行高**：真实正常入口在 JetBrains Mono 13 下 `--code-line-height` 为 **22px**（`design-system.md` §5.1），终端基础行格才是 18px。旧测试断言的 18px 是桩默认 `Cascadia Mono` 走 canvas 度量的结果。
- 字体度量在 `document.fonts` 完成后异步更新，测试在保存后等待行高稳定为 22px，而不是立即读取。

## 5. 下一步

1. 核对并处理「对话框内 Tab 顺序」失败：先看产品焦点规则与真实正常入口，再决定改桩还是报缺陷。
2. 逐段处理 6760/9310/27800 附近的旧 Settings 结构。
3. 套件恢复后，把它作为 Diff/设置等公共状态改动的回归门；在恢复前，用 `mockup-scenes`、`verify-ux-*` 与定向单元测试作为本批证据。
