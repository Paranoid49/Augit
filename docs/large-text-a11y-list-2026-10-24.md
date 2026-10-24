# 大文件窗口层的可访问列表语义（2026-10-24）

> 结论：窗口层不再只有“总行数”摘要。`.code-lines` 现在是 `role=list`，带全文总数；
> 每个可见 `.code-line` 是 `role=listitem`，带 `aria-posinset` 与 `aria-setsize`。
> 屏幕阅读器可以读到“第 X 行 / 共 N 行”，而不是把窗口当成未知长度的一小段。
> **全文可访问树仍未完成**：离屏行仍不在树中；本项是虚拟化列表语义，不是全文 DOM。

## 1. 改动

`web/src/large-text-view.js`：

- `.code-lines`：
  - `role="list"`；
  - `aria-label="Read-only lines, 80,000 total"`（按实际行数）；
- 每个 `.code-line`：
  - `role="listitem"`；
  - `aria-posinset="1"`…（可见行的绝对行号）；
  - `aria-setsize="80000"`（全文行数）；
- 行号本身仍 `aria-hidden="true"`，避免屏幕阅读器重复读数字；
- 未新增全量 `pre`，未改变窗口层节点数。

## 2. 正常入口结果

| 样本 | 全文行数 | 可见首行 role | posinset | setsize | list label |
| --- | ---: | --- | ---: | ---: | --- |
| small | 2,000 | listitem | 1 | 2,000 | `Read-only lines, 2,000 total` |
| mid | 80,000 | listitem | 1 | 80,000 | `Read-only lines, 80,000 total` |
| huge 预览 | 22,658 | listitem | 1 | 22,658 | `Read-only lines, 22,658 total` |

- 实例 PID 3456 正常关闭；
- 报告：`artifacts/large-text-a11y-rows-2026-10-24/large-text-normal-entry.json`。

### 2.1 键盘行导航

窗口层现在可聚焦（`tabindex=0`），并支持：

- `ArrowDown` / `ArrowUp`：逐行移动活动行，滚动到该行；
- `Home` / `End`：跳到首/末行；
- `Shift+ArrowUp/Down` / `Shift+Home/End`：按行扩展/收缩全文选择；
- `aria-activedescendant="virtual-line-N"`；
- 活动行 `aria-current="true"`，行号仍 `aria-hidden`。

正常入口 2,000 行样本：

- 点击正文后 `ArrowDown` 两次再 `End`；
- `activeLine=1999`；
- `aria-activedescendant=virtual-line-2000`；
- `aria-current` 行 id 同为 `virtual-line-2000`；
- 文本 `line 002000 NEEDLE_LAST`；
- `scrollTop=43,474.9`，说明键盘可到达未显示末尾；
- 随后 `Home` 复位，再 `Shift+ArrowDown` 两次：
  - 选择 `start=0`、`end=290`；
  - 选中前两行文本（`line 000001 …\nline 000002 …`）；
  - `activeLine=2`；
- 随后 Find 3/3、GoTo 2000、末尾均继续通过；
- 实例 PID 21012 正常关闭；
- 报告：`artifacts/large-text-shift-select-2026-10-24/large-text-normal-entry.json`。

## 3. 自动化

- `live-large-text` 扩展既有可访问性断言：
  - `linesRole=list`、list label、首行 `listitem/posinset/setsize`；
  - `ArrowDown` 两次后 `activeLine=2`、`aria-activedescendant=virtual-line-3`、
    `aria-current` 同一行；
  - 仍要求 `a11yLabel`、`a11yLength=0`。
- 定向运行：**live-large-text 22/22 通过**；
- 改动后完整 `live-shell` **2371/2371、退出码 0**。

## 4. 仍不是“全文可访问树”

- 当前语义是“已知全文长度 + 键盘/活动行可到达未显示行”，比纯摘要更接近虚拟化列表；
- 但离屏行仍不同时在可访问树中，屏幕阅读器不能一次顺序遍历全文；
- 全量 `pre` 方案仍因约 100 s 可访问布局代价不可接受；没有把 `aria-label` 截断当成全文。
- 页面级配对仍 50、像素仍 0。

## 5. 复验

```powershell
node tools/audit/live-shell.spec.cjs <playwright> --large-text-only
node tools/audit/large-text-normal-entry.cjs <playwright> artifacts/large-text-a11y-rows-2026-10-24
```
