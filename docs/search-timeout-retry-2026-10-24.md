# 搜索超时后 Enter 重试的正常入口证据（2026-10-24）

> 结论：真实外壳中，rg 被受控挂起后 15 秒超时保留已完成结果，超时说明可见；
> 按 Enter 用**同一查询**重跑，说明清除并得到完整结果。此前该路径只有 live-shell 宿主桩证据。
> 受控挂起是诊断条件，不是自然慢盘结论；页面级像素仍未验收。

## 1. 方法

- 夹具：隔离仓库 `tree/` 下 **80,000 个 64B 小文件**（不是 250k/1M），每 250 个文件一个
  `needle_retry_token`，共约 320 个命中；`tree/` 初始化嵌套 `.git`，父仓库只显示 `?? tree/`。
- 构建：`tools/start-augit.ps1 -SkipBuild`，当前 exe ProductVersion `0.1.0+faeb0a1…`；
  隔离设置从当前全局配置复制并清空恢复标签。
- 真实入口：主窗临时置 topmost 后，Win32 单击内容，`keybd_event` 发 `Ctrl+Shift+F`；
  `SendKeys` 输入 `needle_retry_token`；超时后 `keybd_event` 发 Enter。
- 受控停顿：`tools/audit/suspend-first-rg.ps1` 在查询发出后轮询本机 `rg`；
  第一次搜索的 rg 在约 0.94 s 时被 `NtSuspendProcess` 挂起并保持 17 s，宿主 15 s 超时后结束它。
  CDP 只读 DOM/状态并截图，不注入搜索状态。
- 超时后等待 2 s 记录界面，再发 Enter；重试的 rg 不被挂起。

## 2. 结果

| 事件 | 时间/结果 |
| --- | --- |
| 输入 `needle_retry_token` | 18:12:44.025 UTC |
| rg 被挂起 | 18:12:44.967（查询后 0.94 s），PID 31324，`NtSuspendProcess=0` |
| 超时状态 | `live.search.timedOut=true`，`cancelled=false`，`running=false` |
| 超时说明 | `Search timed out; completed results are preserved.` |
| 保留结果 | **45** 条已完成结果（重试后完整 320 条） |
| 按 Enter 重试 | 18:13:01.451 UTC |
| 重试结果 | **320** 条，查询仍为 `needle_retry_token`，`timedOut=false`，说明清除 |
| 实例 | PID 25248；正常关闭，`pidAlive=false` |

截图：

- 超时：`artifacts/search-timeout-retry-2026-10-24c/timeout.png`
  （保留 10 条可见结果，底部超时说明，输入框仍为原查询）；
- 重试：`artifacts/search-timeout-retry-2026-10-24c/retry.png`
  （同样列表、底部无超时说明；实际 DOM 320 条）。
- 报告：`artifacts/search-timeout-retry-2026-10-24c/search-timeout-retry.json`、
  `suspend-result.json`。

## 2.1 非默认选项保留（Words + Regex）

同一夹具、同一查询再跑一次，在输入前打开 **Words** 与 **Regex**（同时尝试 Match case，
但该按钮的点击仍未生效，最终 `case=false`；因此本节证明 Words + Regex）：

| 状态 | 查询 | options | 结果 |
| --- | --- | --- | --- |
| 超时 | `needle_retry_token` | `case=false, words=true, regex=true` | 48 条已完成 |
| Enter 重试 | 同上 | 仍 `case=false, words=true, regex=true` | **320** 条；`timedOut=false`，说明清除 |

- 截图：`artifacts/search-timeout-retry-options-all-2026-10-24/timeout.png`、`retry.png`；
- 报告：`search-timeout-retry.json`，`pressed` 在超时和重试两态均为 `words=true, regex=true`；
- 实例 PID 27104 正常关闭；80k 夹具删除。
- 边界：Match case 的点击仍未生效（连续两次/等待后仍 `false`），不把它写成已通过；
  该点击竞态本身未定位，不作为产品缺陷结论。

## 3. 行为核对

- 超时后 **Enter 不打开当前首条结果**，而是重跑同一查询与同一 options；重试后 Enter
  才会打开结果（本批未再按 Enter 打开标签，避免把重试验收混成打开动作）。
- 重试清除 `timedOut`、`cancelled`、`notice`；结果数量从 45 增至 320。
- 第一次搜索的 rg 被挂起后，宿主在 15 s 超时结束它；挂起助手在 17 s 后退出，
  第二次搜索不受影响。
- 超时期间浮层没有关闭，输入值未丢；重试沿用同一输入。

## 4. 边界

- 慢搜索由**挂起真实 rg** 制造，模拟不可响应文件系统；不是自然网络/慢盘性能结论。
- 夹具是 80,000 个小文件，受控且已全部删除；没有再次制造 250k/1M 文件。
- 只测浅色、当前 DPI、默认 options；未测深色、窄窗、正则/忽略组合，也未采参考整页像素。
- 超时说明是产品现有英文文案；未与参考实机同状态比对。
- 未验证超时后除 Enter 外的点击/关闭路径；`Esc` 取消已有历史正常入口证据。

## 5. 复验

```powershell
node tools/audit/search-timeout-retry.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/search-timeout-retry-2026-10-24c
```

- 脚本生成临时 80k 文件夹具、构建/启动真实外壳、真实快捷键输入、挂起第一次 rg、
  记录超时/重试并删除夹具；实例用 `CloseMainWindow` 正常退出。
- 本批无残留 Augit/rg、无监听端口；全局设置 SHA256 仍为 `8840E775…462CF`。
