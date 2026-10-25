# rc.2 纯 OS 键盘 NVDA 会话（2026-10-25）

> 结论：在最终 `1.0.0-rc.2` 便携包上完成真实 **NVDA 2026.2 AMD64** 纯 OS 键盘会话。
> 小文件 5 行全部读出；2,001 行窗口化文本用 PageDown 到达第 **1504 / 1523** 行，
> 用 End/Up 到达第 **2001 / 2000** 行，NVDA 均播报行号、正文和“2001 之 N 项”；
> 80,001 行样本到达第 **61 / 80000** 行并播报行号/正文。**未发现新的焦点、行号、
> 活动行或播报语义缺陷**。离屏全文仍不在可访问树（80k 样本只有 30 个窗口行），
> 不能连续朗读全文；这是已披露的虚拟化能力边界，不是本轮新缺陷。

## 1. 工具与目标

- 目标：`artifacts/release-1.0.0-rc.2/final/Augit-1.0.0-rc.2-win-x64-portable.zip`，
  ProductVersion `1.0.0-rc.2+8643b9e176f0b739806c44c480a915df9796d9e9`；
  SHA256 `e339da490611d7c0ea2e4b87bddd33f9cb840eb2c76333be9fc5ab4a207bb5fc`。
- NVDA：**2026.2 AMD64**，便携运行时（`nvda_noUIAccess.exe` ProductVersion 2026.2,
  FileVersion 2026.2.0.57664）；启动日志 `Starting NVDA version 2026.2 AMD64`。
- Windows 11 25H2 `10.0.26200.9457`；单显示器 2880×1800 物理 / 175% DPI；隔离设置与 profile。
- 输入：`keybd_event` 的**真实 OS 键盘**；方向/翻页/Home/End 使用 `EXTENDEDKEY`，
  避免 NumLock 关闭时被解释为小键盘。NVDA 睡眠模式（`NVDA+Shift+S`）让页面接收导航键，
  关闭睡眠模式后读取当前活动行。
- **没有用 CDP/Playwright 注入被测键盘**。CDP 仅用于读取 DOM 几何、活动行、
  `aria-activedescendant` 和只读键到达记录；不参与输入。OS 鼠标只用于点击正文/对话框，
  不是键盘输入的替代。

## 2. 小文件

夹具 `small.txt`（5 行）。OS 点击正文后：

```
NVDA+Tab: small.txt Read-only content 区域 已聚焦
NVDA+Down: alpha line onebeta line twogamma line threedelta line fourepsilon line five
```

即 5 行全部进入语音输出；日志时间 19:58:24.318。

## 3. 2,001 行窗口化文本

`big2k.txt` 324,061 B，2,001 行（末尾空行）。从第 3 行开始，睡眠模式 PageDown：

| 操作 | 活动行（0 基） | aria-activedescendant | scrollTop | NVDA 播报 |
| --- | ---: | --- | ---: | --- |
| PageDown 至目标 | 1503 | `virtual-line-1504` | 32,628 | `1504: line 1504 ... 当前，2001 之 1504 项` |
| 继续 PageDown | 1522 | `virtual-line-1523` | 33,046.3 | `1523: line 1523 ... 当前，2001 之 1523 项` |
| End | 2000 | `virtual-line-2001` | 43,562.3 | `2001: ... 当前，2001 之 2001 项`（空行） |
| Up | 1999 | `virtual-line-2000` | 43,562.3 | `2000: line 2000 ... needle line 2000，当前，2001 之 2000 项` |

这证明：未显示行可由键盘到达；活动行、行号、正文和全文位置同时正确；
继续滚动后的下一行仍是同一语义。没有恢复全文 `pre`。

## 4. 80,001 行抽样

`big80k.txt` 9,748,894 B（低于 10 MB），80,001 行（末尾空行）；页面
`mountedAt=1019.5ms`，初始 DOM 30 行，UIA 大文本列表 30 个子项、总数 80,001。

| 操作 | 活动行 | 结果 |
| --- | ---: | --- |
| NVDA+Tab | — | `Read-only content, 80,001 lines 文档 已聚焦 只读` |
| PageDown ×3 | 60 | `61: line 000061 ... 当前，80001 之 61 项` |
| End | 80,000 | `80001: ... 当前，80001 之 80001 项`（空行） |
| Up | 79,999 | `80000: line 080000 ... needle 80000，当前，80001 之 80000 项` |

只做代表性导航/抽样，没有逐字朗读 8 万行；不把 30 个窗口行写成全文可访问。

## 5. 结论与边界

- **已解决**：纯 OS 键盘下的行号/活动行/正文播报，以及 80,000 行代表性导航。
- **未解决**：可访问树仍只有窗口行；屏幕阅读器不能一次连续朗读 80,001 行全文。
  替代操作：逐页/PageDown、当前行读取、Find、Go to Line、Ctrl+A/复制全文。
- 不恢复全文 `pre[role=document]`：历史实测在可访问树开启时约 100 秒布局阻塞。
- 未测 Narrator 完整会话、JAWS/Braille；未测深色、窄窗、其他 DPI；Narrator 仅确认系统
  `Narrator.exe` 存在，没有把“可启动”写成“已验证”。

## 6. 进程、端口与清理

- 2k：app PID 3504，CDP 9458（只读）；小文件：app PID 2520，CDP 9459；80k：app PID 5776，CDP 9460；
  NVDA PID 3164（helper 子进程）。
- 三个 app 均通过 `WM_CLOSE` 关闭；NVDA/helper 停止；端口无监听；测试 profile/临时设置隔离，
  未改全局设置。已提取并保留日志、截图、证据；NVDA 运行时在取证后删除。
- 证据：`artifacts/accessibility-oskeyboard-2026-10-25/nvda-osk-debug.log`、
  `nvda-osk-evidence.txt`、`big2k-osk-line2000.png`、`big80k-osk-line80000-full.png`、
  `send-oskeys.ps1`。

## 7. 对正式 1.0 的影响

- 不阻塞 rc.2 候选；纯 OS 键盘读屏不再列为未验证。
- 正式 1.0 仍需用户接受“不能连续朗读全文”的边界，或另立 1.0 后“可访问全文浏览”工程。
- 不把本次 NVDA 通过写成 Narrator/JAWS/Braille 全部通过。
