# 终端 ANSI 背景色逐行验证（2026-10-24）

> 结论：真实终端输出 `41m`/`44m` 背景色的 `B41`/`B44` 两行，浅色和深色截图的核心
> 背景像素分别精确等于 `terminal-palette.js` 的 red/blue 值。背景色不再只由前景色
> 推断。

## 1. 方法

- 真实外壳 Terminal，分浅色、深色两次运行。
- 命令：
  ```powershell
  $e=[char]27; Write-Host ($e+"[41mB41"+$e+"[0m"); Write-Host ($e+"[44mB44"+$e+"[0m")
  ```
- 截图后按 `.xterm-rows` 行矩形裁切，取该行出现次数最多的精确调色板色（背景覆盖字符
  单元，面积大于前景字形）。
- 实例：浅色 PID 17960、深色 PID 4644，均正常关闭。

## 2. 结果

| 行 | 浅色背景 / 像素数 | 深色背景 / 像素数 |
| --- | --- | --- |
| B41（红底） | `#CE0505` / 974 | `#F0524F` / 959 |
| B44（蓝底） | `#063FDB` / 962 | `#3993D4` / 948 |

- 两行都以终端基色（浅色 `#FFFFFF`、深色 `#1E1F22`）为大面积底色，调色板背景只覆盖
  字符单元；裁切统计中目标色仍是最常见的非基色。
- 截图：
  - `artifacts/terminal-background-light-2026-10-24/ansi-background.png`
  - `artifacts/terminal-background-dark-2026-10-24/ansi-dark-background.png`
- 报告：各自目录的 `terminal-ansi-colors.json`。

## 3. 边界

- 只验证 SGR 41/44 两种背景色；未覆盖其余 14 个背景索引、反显、粗体/暗淡、选区背景。
- 未验证终端光标色；未采参考 PyCharm 背景像素。
- 背景色是调色板实现验证，不是整页参考像素；页面级配对仍 50、像素仍 0。

## 4. 复验

```powershell
node tools/audit/terminal-ansi-colors.cjs <playwright> artifacts/terminal-background-light-2026-10-24 --background
node tools/audit/terminal-ansi-colors.cjs <playwright> artifacts/terminal-background-dark-2026-10-24 --background --dark
```
