# 大文本软换行边界：80,000 行开启/关闭（2026-10-24）

> 结论：软换行仍按已登记边界退回**完整行 DOM**。80,000 行（约 2.9–3.0MB）开启软换行时：
> 页面级夹具构建 **3.621s**、真实外壳 **5.230s**，生成 **80,000** 个 `.code-line`；
> 滚动到末行、Find、Go to Line、全选、关闭标签均可用；关闭软换行后恢复窗口层（约 31 行节点）。
> 这是一次性多秒卡顿和大量 DOM，不是数据错误或持续无响应；登记为可披露性能限制，不写成窗口层通过。

## 1. 页面级夹具（live-shell large-text）

```powershell
$env:AUGIT_BROWSER_EXECUTABLE='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
node tools/audit/live-shell.spec.cjs <playwright> --large-text-only
```

`--large-text-only` 使用 Edge 作为 Playwright 的 Chromium 可执行文件（本机 Playwright 缓存版本与
驱动期望不一致），不改产品入口。本批新增 80,000 行软换行断言，结果：

| 指标 | 值 |
| --- | ---: |
| 构建耗时 | **3,621ms** |
| 行节点 | 80,000 |
| 滚动到末行 | 通过 |
| Find `normal line 79999` | `1/1`，行 79999 |
| Go to Line | 行 40000 |
| 全选长度 | 10,308,893 字符（全文） |
| 关闭软换行后 | 窗口层恢复，行节点 <120 |
| 关闭标签后 | 行节点 <120 |
| 断言 | **30/30 通过** |

## 2. 真实外壳（large-text-normal-entry）

```powershell
node tools/audit/large-text-normal-entry.cjs <playwright> `
  artifacts/large-text-wrap-normal-entry-2026-10-24
```

2,000 行、80,000 行（2.96MB）与 22.75MiB 预览三个样本均通过；80,000 行样本新增软换行步骤：

| 指标 | 值 |
| --- | ---: |
| 构建耗时 | **5,230ms** |
| 行节点 | 80,000 |
| 滚到末行 | 第 80000 行 `line 080000 NEEDLE_LAST` |
| 关闭软换行后 | 窗口层恢复，31 个行节点 |
| 样本失败项 | 0 |
| 实例关闭 | `CloseMainWindow`，`pidAlive=false` |

原始 JSON：`artifacts/large-text-wrap-normal-entry-2026-10-24/large-text-normal-entry.json`。
live-shell 日志：`artifacts/large-text-wrap-2026-10-24/run.log`（`live-large-text-wrap metrics`）。

## 3. 解释与边界

- 计数、选择、Find、Go to Line 的**逻辑层**仍在全文 `text + lineStarts`；软换行只改变视觉层实现。
- 退回完整 DOM 的原因：换行后行高可变，当前窗口层按统一行高定位，无法在不改变语义的前提下
  直接复用。修复需要可变行高的虚拟化与高度索引，属于后续架构工作，不在 RC 前扩大范围。
- 实测影响：约 3MB/80,000 行文件开启软换行会有一次约 3.6–5.2s 的同步构建，并常驻 80,000 个行节点；
  本批未测长时间连续滚动帧率、内存峰值和 10MB 上限附近文本。
- 关闭软换行、关闭标签后窗口层/节点回收通过；未发现崩溃、错误内容或操作不可完成。
- 可访问性边界不变：离屏全文不在可访问树，当前只读窗口行 + 全文行数；不恢复约 100 秒布局的全文 `pre`。
