# 终端行格实测与规范冲突（2026-10-14）

整体整改未完成。本文保留2026-10-14测量历史；用户已于2026-10-15裁决按实机整改，实施见[终端行距整改](pixel-terminal-lineheight-2026-10-15.md)。Windows 10 排除本次验收，跨屏 DPI 保留未验证。

## 实测与规范

`docs/ux-spec.md` §7.16 明定字号13px、行高约22px（字号的1.7倍）。当前 `live-data.js` 将1.7直接传给xterm的lineHeight；该参数放大字体基础行格，不等于字号倍数。175% DPI下深浅主题实际均约29.1667逻辑px，截图连续ANSI行墨迹顶部间隔均为51物理px。当前实现也未达到规范所述22px。

PyCharm 2026.2.1设置实测为Reworked 2025、JetBrains Mono13.0、Line height1.0、Column width1.0。启动横幅两行墨迹范围为原图y=1142–1158、1172–1193；不同字符顶部不能当作基线，不将30/1.75当作精确行格。独立JBR字体基础高度为18逻辑px，是约18px的候选依据，仍需相同文字逐行配对。

已请求用户选择：按参考实机整改并同步规范，或保留规范22px并登记有意差异。根据AGENTS.md规范冲突规则，尚未自行选择解释。

## 证据

- 设置原始PNG：`artifacts/pixel-followup-2026-10-14/terminal-reference-settings/reference.png`，1555×1215、168DPI，SHA256 `ed743a2c2a33ed19c434f3161fa7655d9b434f984807617ceed591a0ce90ab9d`。已目视核对，搜索框指针光晕不影响字体设置区。
- 启动原始PNG：同目录 `terminal-initial.png`，2430×1637、168DPI，SHA256 `f809f210149cc964ef582539b6c0e1214a0e0d00925f631c2fe7d452712037c7`。未在终端界面输入命令，只观察自然启动横幅。
- Augit：`artifacts/pixel-followup-2026-10-14/terminal-measured-grid/palette.json`及深浅PNG。记录公开行列数、实际画布边界、DPI和字体参数；总高除行数含尺寸舍入误差，截图51px行间隔交叉检查。样本直接写xterm，不算Shell输出或双端同态配对。
- 当前安装包 `TerminalUiUtilsKt.applyFontSettings` 只读核查显示行距传给编辑器方案，列距传给字符网格；本地平台 `CharacterGrid.kt` 使用view.lineHeight。未复制或分发参考代码。

## 验证与清理

`live-terminal-palette.cjs`增加画布/行列度量及有效性断言，深浅ANSI16、设置保存/取消和会话节点保留专项通过。JS语法、9个共享资产及git diff --check通过，未跑全量或构建。一次Python默认GBK读取失败，指定UTF-8后完成统计。

四运行时MD5前后不变：mockup.js `DB12568D024253582D1C09B5DA1CD5AE`，mockup.css `439C96E4A5696967DC702CAC9B9A0850`，live-data.js `DB52470B2BAB434DE4E463086A39FA6D`，bridge.js `F297BF8769E249CA0D804F4F4EEEF5A9`。分支dsh、HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`，未回滚、暂存或提交。

```powershell
node tools/audit/live-terminal-palette.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-14/terminal-measured-grid
# 脚本finally自动停止；异常时仅填写脚本打印的本次PID。
taskkill /PID <本次PID> /T /F
```

Augit PID9796、端口55524、临时目录Augit-terminal-colors-DHMQ3K已清理；端口无监听，仅系统TIME_WAIT、OwningProcess=0。参考PID14828及子树通过taskkill /PID 14828 /T /F停止，复查参考路径无残留。参考启动沿用接续记录§7的隔离PYCHARM_PROPERTIES及Start-Process命令，未更改用户配置。正式截图/JSON保留；未重试此前被拒绝的清理。
