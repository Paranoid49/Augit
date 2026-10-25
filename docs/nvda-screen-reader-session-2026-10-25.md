# 大文本真实屏幕阅读器会话（NVDA 2026.2，2026-10-25）

> 结论：使用真实 **NVDA 2026.2** 读取 RC 便携包，完成小文件与 2,000 行窗口化大文本的会话。
> 发现并修复了一个**语义缺陷**：`aria-activedescendant` 变化时 NVDA 只播报“列表项，当前，2001 之 165 项”，
> 不读行文本。修复后 NVDA 播报“当前 line 0016 …”，并可用 `End`/`Up` 到达未显示的末行与倒数第二行。
> 剩余限制是虚拟化本身的能力边界：可访问树只有当前窗口的 25 行，2,001 行中的离屏行不在树中，
> 不能用“连续朗读/浏览模式”一次读完全文；需要逐页导航、Find 或 Go to Line。

## 1. 测试工具与环境

- NVDA 2026.2（官方安装包 `nvda_2026.2.exe`，SHA256
  `F3F8D29974A88D687B3C4809BE192219EC579C5BDABCDA5AAF53635288BCA824`），便携运行；
  测试结束已删除运行时，仅保留日志与截图。
- 屏幕阅读器输出用 **NVDA Speech Viewer** 与 `--debug-logging` 日志交叉记录；日志中
  `IO - speech.speech.speak` 行是实际送入语音合成器的文本。
- 被测程序：`artifacts/release-1.0.0-rc.1/final2-verify/portable/Augit/Augit.exe`（rc.1 便携包）
  加工作区修复后的 `large-text-view.js`；修复已进入最终 `1.0.0-rc.2` 包
  （`artifacts/release-1.0.0-rc.2/final/`，便携烟测 PASS）。隔离设置与隔离工作区；窗口 2880×1800、175% DPI。
- 说明：文件打开与翻页按键由 CDP/Playwright 驱动页面（OS 级按键在 NVDA 浏览模式下会被 NVDA
  消费）；**NVDA 进程、可访问树、焦点事件和语音日志都是真实的**，不是手工伪造的 ARIA 断言。
  这是本会话的输入方式边界，仍需在正式发布前补一次纯 OS 键盘会话。

## 2. 小文件

`small.txt`（5 行）。NVDA 日志（14:06:19）：

```
Speaking ['small.txt Read-only content', '分组', 'alpha line one\nbeta line two\ngamma line three\ndelta line four\nepsilon line five ']
```

NVDA+Tab：`small.txt Read-only content 分组 已聚焦`。小文件全文可被一次读出。

## 3. 2,000 行窗口化大文本

`big2k.txt`：2,001 行（末尾空行）、324,061 字节，含第 1/1000/1999/2000 行 `needle`。

### 3.1 初始窗口

```
Read-only lines, 2,001 total 列表 26 项
line 0001 ... needle line 1
line 0002 ...
...
line 0026 ...
```

即 NVDA 明确报告“总数 2,001、当前只有 26 项”；窗口行被完整读出，离屏行不在可访问树中。

### 3.2 修复前

用 PageDown 把活动行从第 16/31/…/165 行推进时，NVDA 只播报：

```
列表项目 当前，2001 之 165 项
```

没有行号或正文。DOM 的 `aria-activedescendant` 指向的行有 `role=listitem` 和 UIA `Name`，
但 NVDA 的 ChromeVBuf 在该事件上只取角色/位置；给行加 `aria-label` 后同一会话开始读出
`165line 0165 ...`。同时 `activateLine()` 先设置 `aria-activedescendant`、后 `render(true)`，
事件可能在行文本建立前发出。

### 3.3 修复后

- `ensureRow()` 为每个渲染行设置 `aria-label="<行号>: <行文本>"`（超长行截断到 2,000 字符）；
- `activateLine()` 先 `render(true)` 建立目标行，再设置 `aria-activedescendant`；
- 自动化：`live-large-text.cjs` 断言活动行与全部渲染行都有名称，且内容为“行号 + 正文”。

修复后的 NVDA 日志：

```
Read-only lines, 2,001 total 列表 26 项 当前 line 0016 ...
当前 line 0031 ...
当前 line 0046 ...
当前 line 0061 ...
当前 line 0076 ...
当前 line 2000 ... needle line 2000
```

`End` 到第 2001 行（空行）时播报“当前”；`Up` 到第 2000 行时播报
`当前 line 2000 ... needle line 2000`。即未显示的行可以通过键盘到达，且行号与正文都被读出。

## 4. 实际影响与替代操作

- **可以完成**：读取当前窗口内全部可见行；逐行/逐页导航并听到行号与正文；跳到末行；
  用 Find/Go to Line 定位离屏命中；用 Ctrl+A/复制取得全文逻辑内容（另有既有专项证据）。
- **不能完成**：像普通文档那样用“连续朗读/浏览模式”从第 1 行一次读到第 2,001 行；
  可访问树没有离屏行（UIA 列表 25 个子项，vs 总数 2,001）。
- 对依赖连续全文朗读的用户，这是实际限制；对只需要定位、逐页阅读和复制的用户，现有替代操作可用。
- 不恢复全文 `pre[role=document]`：既有实测在可访问树开启时约 100 秒布局阻塞。

## 5. 是否影响正式 1.0

- 本次修复的是**语义缺陷**（活动行不读正文），已有自动化回归，必须进入最终 RC 重新构建，
  不能带着缺陷发布。
- 离屏全文不在可访问树是**虚拟化能力边界**，不是本次修复能消除的；建议作为正式 1.0 的
  已披露限制，并在 1.0 后评估“可访问的全文浏览/连续朗读”方案（例如按需分块、用户触发的
  离屏文本视图），而不是恢复全量 DOM。
- 若产品承诺要求“屏幕阅读器连续全文朗读”，则该承诺与当前虚拟化方案冲突，需要用户裁决；
  在现有规格下，键盘可达、行号/正文可读、Find/Go to Line/复制可用，已满足“可访问性”的最低可用集合。

## 6. 证据

- `artifacts/nvda-session-2026-10-25/nvda-debug.log`：完整 NVDA debug 日志。
- `.../nvda-evidence-final.txt`：筛出的语音/焦点行。
- `.../nvda-big2k-line2000-final.png`：第 2000 行活动态截图。
- `.../settings.json`、`.../workspace/`：隔离设置与夹具。
- `tools/audit/live-large-text.cjs`：新增活动行名称断言；本批 `--large-text-only` **30/30** 通过。
- NVDA 安装包哈希见 §1；测试运行时已删除（两个代理 DLL 被子进程短暂锁定，剩余约 259KB，
  待持有进程退出后可删除，不属于产品文件）。

## 7. 边界

- 只测 NVDA 2026.2 + ChromeVBuf；未测 Narrator、JAWS、Braille。
- 只测 2,000 行；未用 NVDA 读 80,000 行/近 10MB 样本。
- 输入由 CDP 驱动；未完成纯 OS 键盘的同一会话。
- 未测深色、窄窗、其他 DPI；未测语音速率/语言切换。
