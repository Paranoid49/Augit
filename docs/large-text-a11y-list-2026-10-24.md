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

## 3. 自动化

- `live-large-text` 扩展了既有可访问性断言（不增加断言数）：
  - `linesRole=list`；
  - list label 精确 `Read-only lines, 2,000 total`；
  - 首行 `role=listitem`、`aria-posinset=1`、`aria-setsize=2000`；
  - 仍要求 `a11yLabel="Read-only content, 2,000 lines"`、`a11yLength=0`。
- 定向运行：**live-large-text 22/22 通过**；
- 改动后完整 `live-shell` **2371/2371、退出码 0**。

## 4. 仍不是“全文可访问树”

- 离屏 1,999 / 79,999 / 22,657 行不在可访问树中；屏幕阅读器无法一次性顺序遍历全文。
- 当前语义是“已知全文长度、当前窗口行的绝对位置”，等价于标准虚拟化列表；
  不等于把全文文本作为单个可访问节点。
- 全量 `pre` 方案仍因约 100 s 可访问布局代价不可接受；本批没有回退到全量 DOM，
  也没有把 `aria-label` 截断当成全文可访问。
- 页面级配对仍 50、像素仍 0。

## 5. 复验

```powershell
node tools/audit/live-shell.spec.cjs <playwright> --large-text-only
node tools/audit/large-text-normal-entry.cjs <playwright> artifacts/large-text-a11y-rows-2026-10-24
```
