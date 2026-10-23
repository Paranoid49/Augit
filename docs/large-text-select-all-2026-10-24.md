# 大文件窗口层的全文全选复制（2026-10-24）

> 结论：80,000 行 / 2,959,963 字节样本在窗口层只挂 33 行可见 DOM，但
> `Ctrl+A` 选中 **全文模型**，`Ctrl+C` 后 Windows 剪贴板长度和 SHA256 与磁盘
> `mid.txt` 完全一致。这是全文逻辑层选择/复制的直接证据，不是“只复制当前窗口”。

## 1. 方法

- 真实 `start-augit.ps1 -SkipBuild`，临时 Git 仓库 + 隔离设置。
- 项目树双击 `mid.txt`（80,000 行、2,959,963 字节）。
- 等待 `.editor-content .code-view[data-accessible-text="ready"]`，读取：
  - `controller.lineCount=80000`；
  - 可见 `.code-line` 仅 **33** 行；
  - 全文 `textLength=2959963`。
- 真实点击正文，`Ctrl+A` 后 `Ctrl+C`；PowerShell 读取 Windows 剪贴板，
  计算 UTF-8 字节数与 SHA256。
- 期望值由 Node 对磁盘 `mid.txt` 计算。

## 2. 结果

| 项 | 值 |
| --- | --- |
| 全文行数 | 80,000 |
| 可见窗口行 | 33 |
| 剪贴板字节 | **2,959,963** |
| 磁盘字节 | 2,959,963 |
| 剪贴板 SHA256 | `72a18ac885f90cd22bdbf6514536c132a51b633b298b7c14bd46a5061c9d7827` |
| 磁盘 SHA256 | 同上 |
| `lengthMatches` / `hashMatches` | **true / true** |
| Find in File | 3/3，定位第 80,000 行 |
| Go to Line | 第 80,000 行，对话框关闭 |
| 实例 | PID 16128 正常关闭，`pidAlive=false` |

- 报告：`artifacts/large-text-select-all-2026-10-24b/large-text-normal-entry.json`
- 同一正常入口仍验证窗口末尾 `lastLine=80000`、rowHeight=22、Find 定位未显示行。

### 2.1 22.75 MB 预览补充

同一脚本对 22.75 MiB `huge.txt`（宿主 2.5 MB TextPreview，22,658 行）执行：

- Ctrl+A 后剪贴板 **2,560,000 字节**，与 `opened.textLength` 完全一致；
- 预览 30 行可见时仍全选到 2.5 MB 预览全文；
- 实例 PID 27432 正常关闭。

这不是 22.75 MB 原文的全选；宿主只返回前 2.5 MB，复制语义以预览文本为边界。

## 3. 意义

- 之前的窗口层已经用 `text` + 行索引做 Find/Go to Line；本批补充了**选择/复制也走全文
  模型**：选区 anchor=0、focus=text.length，复制事件按全文 slice，而不是可见行拼接。
- 这排除了“窗口化后选择只能限制当前窗口”的错误终点；仍不能据此宣称可访问性全文枚举
  已完成（`a11yLength=0`，只有 `aria-label="Read-only content, 80,000 lines"`）。

## 4. 边界

- 用 `Ctrl+A` 触发全文选择，不是从第一行拖到第 80,000 行的真实鼠标拖动；跨窗口拖动
  仍缺少逐段自动滚动证据。
- 只测 80,000 行 / 2.96 MB 与 22.75 MB 的 2.5 MB TextPreview；未对 22.75 MB 原文做全选。
- 剪贴板只比对长度/SHA256，不验证富文本 MIME、复制耗时或大剪贴板对其他应用的副作用。
- 未采参考 PyCharm 全选复制像素；页面级配对仍 50、像素仍 0。

## 5. 复验

```powershell
node tools/audit/large-text-normal-entry.cjs <playwright> artifacts/large-text-select-all-2026-10-24b
```
