# 终端 ANSI 亮蓝映射尝试与回退（2026-10-19）

## 1. 起因

CDP 诊断包装 `Terminal.prototype.write` 后，PowerShell：

```powershell
Write-Host "ANSI-BLUE" -ForegroundColor Blue
```

的宿主写入末尾出现：

```text
ESC[38;5;12mANSI-BLUE ESC[m
```

即 **256 色索引 12**。据此尝试在 `terminal/read` 写入 xterm 前，把
`38;5;12` 映射为参考实测蓝 `#0060AF`（24-bit `38;2;0;96;175`）。

## 2. 尝试

- 新增 `web/src/terminal-ansi.js`，正则替换 `38;5;12`；
- `live-data.js` 的 `terminalInstance.write(chunk.data)` 改为 `remapAnsiBrightBlue(chunk.data)`；
- 新增 7/7 单测，覆盖简单/组合 SGR、34/94、索引 4、背景 48、空值。

## 3. 结果：回退

正常入口重建后再次执行同一命令，截图中 `ANSI-BLUE` 输出仍是 **`#298ADE`**，映射未生效；提示符路径仍为 `#0060AF`。

可能原因（未区分）：

1. WebView2 缓存仍加载旧 `live-data.js`；
2. 新实例实际发出的是 **24-bit `#298ADE`**，不是 `38;5;12`；
3. 映射模块虽在构建产物中，但未进入实际写入路径。

因此**不能把该映射算作修复**。已回退：

- 删除 `web/src/terminal-ansi.js` 与单测；
- `live-data.js` 恢复 `terminalInstance.write(chunk.data)`；
- `live-data.js` 哈希回到 `12D57E34…1B3D`。

## 4. 下次所需

- 在**同一正常入口实例**内捕获 xterm 实际收到的原始字节（或清空 WebView2 缓存后对比）；
- 确认是 256 索引 12 还是 24-bit 颜色，再决定是否映射；
- 不要仅凭一次 CDP wrapper 的 38;5;12 证据直接改产品。

## 5. 状态与清理

- 未留下产品代码改动；页面级仍 **50**、像素仍 **0**。
- 终端 ANSI 蓝仍未闭环。
- 实际 PID 21020、13420 已结束（13420 因锁定构建产物使用 taskkill）；临时夹具删除；全局设置仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-186.json`。
