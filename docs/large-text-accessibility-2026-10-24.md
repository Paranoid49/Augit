# 大文件可访问性修订：从全文单节点到窗口层（2026-10-24）

> 结论：此前“全文 `pre[role=document]` + 窗口行 aria-hidden”的方案在**可访问树真正开启**时
> 不可用：2.8 MB 文本单节点首次布局实测约 **100 s**。本批改为**当前窗口行可读 + 全文行数摘要**，
> 视觉窗口不再 `aria-hidden`；全文仍由逻辑模型服务选择、复制、Find、Go to Line。
> **全量文本的可访问树仍未达成**，这是未闭合缺口，不写成通过。

## 1. 触发

`tools/audit/large-text-uia.cjs` 用 `--force-renderer-accessibility` 打开 WebView2 可访问树
（模拟屏幕阅读器；只影响可访问树，不改 DOM/窗口），在真实外壳打开 80,000 行 / 2.86 MB 文本，
用 UIA `FindAll(Descendants)` 测量。

## 2. 原实现（已撤销）

- 窗口行 `aria-hidden=true`，另建 `pre[role=document]`，在空闲任务写入全文 3,348,893 字符。
- 无强制 accessibility 时，写入+首次布局 558 ms；强制 accessibility 后：

| 观测 | 值 |
| --- | --- |
| `accessibleAt`（写入后） | 713.9 ms（performance.now） |
| `accessibleLaidOutAt` / `data-accessible-text=ready` 可观测 | **约 102 s** |
| UIA 主窗全量枚举 | 1,381 ms，105 个后代，`namedChars` 3,228,556 |
| UIA 命中全文节点 | Document Name 只显示摘要；大字符串进入 Name，辅助技术读取成本高 |

也就是说：DOM 文本布局被可访问树构建拖到约 100 秒，期间脚本的 `waitForFunction` 也才在
解冻后返回；这不是“0.56 s 一次性的可接受成本”。

- 只把全文写入单个 `aria-label`：ready 降到 1.3 s，但 Chromium 把 Name **截断在 10,000 字符**，
  全文不可读。
- 按 128 行分块写 625 个 `role=group` + `aria-label`：16.7 s 后仍 `data-accessible-text` 未 ready，
  UIA 看不到任何块；构建可访问 Name 的成本仍不可接受。

以上三种路径都实测过，不把“窗口内可访问”当作未经验证的默认，也不把截断的 10k 字符串
当作全文可访问。

## 3. 现实现

- `code[role=document]`：`aria-label="Read-only content, 80,000 lines"`。
- 视觉窗口 `.code-lines`：`aria-hidden=false`；行号仍 `aria-hidden`，行文本可被辅助技术读取；
  屏幕阅读器能读**当前窗口**的内容与行号顺序。
- 摘要 `pre.virtual-text-accessible[role=document]`：只保留
  `Read-only content, 80,000 lines`，不再写全文。
- 全文字符串、`lineStarts`、选择/复制/Find/Go to Line 均留在逻辑层，不受影响。
- 摘要节点在挂载时同步 ready（`data-accessible-text=ready`），不再有空闲任务和 100 s 布局。

## 4. 复测

真实外壳、80,000 行、强制 accessibility：

| 指标 | 值 |
| --- | --- |
| 窗口行节点 | 33 个可见行（全文 80,000） |
| 挂载 | 456.4 ms（performance.now） |
| 摘要 ready | 469.3 ms（`accessibleWaitMs` 1,355 ms，含 CDP 往返/等待） |
| UIA 主窗全量枚举 | **168 ms**，138 个后代，113 个有名元素，`namedChars` 2,241 |
| UIA 渲染子窗全量枚举 | **43.8 ms**，122 个后代 |
| 可访问文本节点 | 48 个 `ControlType.Text`（当前窗口行），无 3 MB 单节点 |
| 全文命中 | 无（不再声称全文可访问） |

`tools/audit/live-shell.spec.cjs --large-text-only`：**22/22 通过**，包含 2,000 行与 80,000 行的
窗口层可读、行数摘要、行号对应、选择/复制/Find/Go to Line；`accessibleMs2000` 16 ms、
`accessibleMs80000` 22 ms。

## 5. 边界与未完成

- 只测 `--force-renderer-accessibility` + UIA `FindAll`，**不是**真实 Narrator/NVDA 会话，
  也没有屏幕阅读器逐行朗读审计。
- **全量文本的可访问树未达成**：当前只能读窗口行 + 总行数；屏幕阅读器要滚动到其他区域
  才能读那里的行。全文逻辑层仍完整，但“全文可访问”仍是缺口，不能写成完成。
- 未测 22.75 MB 预览的 UIA、深色、窄窗、键盘 Tab 顺序和焦点。
- UIA 枚举快于历史“20–30 秒未结束”的结果，只说明当前窗口层 + 摘要方案下枚举不再爆炸；
  不能反推旧实现已被证明为唯一原因，也不能把 168 ms 当作所有辅助技术的读取时延。

## 6. 复验

```powershell
node tools/audit/large-text-uia.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/large-text-uia-2026-10-24h
node tools/audit/live-shell.spec.cjs <playwright> --large-text-only
```

- 报告/截图：`artifacts/large-text-uia-2026-10-24h/`（`uia.json`、`large-text-uia.json`）。
- 实例 PID 20176 正常关闭；临时 80,000 行仓库删除；全局设置 SHA256 仍为 `8840E775…462CF`。
