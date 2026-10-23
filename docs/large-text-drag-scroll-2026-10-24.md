# 大文件窗口层的跨窗口拖动与自动滚动（2026-10-24）

> 结论：80,000 行窗口层从正文内开始拖动，把指针停在正文下方 1.2 秒，正文自动向下
> 滚动到 `scrollTop=3271.4`（约 148 行），释放后复制得到 **6,352 字节**，明显超过
> 33 行可见窗口。拖选不再受当前可见窗口限制。

## 1. 方法

- 真实 `start-augit.ps1 -SkipBuild`，80,000 行 / 2,959,963 字节 `mid.txt` 正常入口。
- 先验证 `Ctrl+A` 全选复制与磁盘 SHA256 完全一致（2,959,963 字节，
  `72a18ac8…c9d7827`）。
- 把正文 `scrollTop` 归零；指针从正文第一行内按下，拖到正文底部下方 40 CSS px；
  保持 1.2 秒，让 `autoScrollStep()` 运行；释放后 `Ctrl+C`。
- 读取拖动中的 `scrollTop`、窗口行数和剪贴板长度。

## 2. 结果

| 项 | 值 |
| --- | --- |
| 拖动中 `scrollTop` | **3271.4** CSS（约 148 行） |
| 拖动中窗口行 | 42（含 overscan） |
| 释放后剪贴板 | **6,352 字节** |
| 可见窗口行 | 33 |
| 是否超过当前窗口 | **true** |
| 实例 | PID 29424 正常关闭，`pidAlive=false` |

报告：`artifacts/large-text-drag-scroll-2026-10-24/large-text-normal-entry.json`。

### 2.1 Win32 真实鼠标复跑

同一路径再用 `SetCursorPos` + `mouse_event`（真实 Win32 输入）执行，先
`ShowWindow`/`BringWindowToTop`/`SetWindowPos(HWND_TOPMOST)`/`SetForegroundWindow`：

| 项 | 值 |
| --- | --- |
| 屏幕起点 → 终点 | `1040,318 → 1040,1286` |
| 拖动中 `scrollTop` | **7056.6**（约 320 行） |
| 拖动中窗口行 | 40 |
| 释放后剪贴板 | **12,642 字节** |
| 实例 | PID 18696 正常关闭，`pidAlive=false` |

- 报告：`artifacts/large-text-os-drag-2026-10-24b/large-text-normal-entry.json`。
- 第一次 OS 拖动只设 `SetForegroundWindow`，前台锁未生效，`scrollTop=0` 且复制到
  上一次 CDP 选区（6,315 字节）；加 topmost 后通过。这是取证前台问题，不是产品缺陷。


## 3. 意义

- 之前只证明 `Ctrl+A` 全文选择/复制；本批证明**指针拖动触发自动滚动并扩大选区**。
- 滚动期间选区 anchor 仍在首行，focus 随自动滚动推进；复制内容来自全文模型，
  不是把可见 33 行拼接后复制。
- 这仍不是“拖到第 80,000 行末尾”的完整长距离拖动；只覆盖 1.2 秒自动滚动约 148 行。

## 4. 边界

- 指针输入同时覆盖 Playwright/CDP 拖动与真实 Win32 `mouse_event` 拖动；窗口、文档、
  WebView2 和产品代码是真实正常入口。
- `scrollTop`/剪贴板长度是行为证据，不是参考 PyCharm 拖选像素或逐帧计时。
- 只测 80,000 行 / 2.96 MB；22.75 MB 预览未做长距离拖动。
- 未验证反向跨窗口拖动、拖到末尾后的精确选区、拖选期间 Find/GoTo 并发。
- 全文可访问树仍未完成；`a11yLength=0`、只有行数摘要标签。

## 5. 复验

```powershell
node tools/audit/large-text-normal-entry.cjs <playwright> artifacts/large-text-drag-scroll-2026-10-24
```
