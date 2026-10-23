# 终端 ANSI 16 色逐行核心色验证（2026-10-24）

> 结论：真实外壳 Terminal 分两批输出 SGR 30–37 / 90–97；浅色与深色各 16 行的
> **核心字形像素颜色全部等于 `terminal-palette.js` 的参考值**（32/32）。
> 这是调色板实现验证，不是参考 PyCharm 截图逐像素配对。

## 1. 方法

- 临时 Git 仓库 + 隔离设置；浅色运行一次、深色运行一次，均 `-SkipBuild` 正常启动。
- 真实点击 Terminal 轨道、点击正文，剪贴板粘贴以下命令并 Enter：
  ```powershell
  $e=[char]27; 30..37 | ForEach-Object { Write-Host ($e+"["+$_+"mC"+$_) }
  $e=[char]27; 90..97 | ForEach-Object { Write-Host ($e+"["+$_+"mC"+$_) }
  ```
  两批之间 `Clear-Host`，每批 8 行 `C30`…`C37` / `C90`…`C97`。
- 每批 CDP 读取 `.xterm-rows` 行几何并截图；行文本精确匹配 `C\d+`。
- `tools/audit/analyze-terminal-ansi.py` 按 CSS 行矩形 ×1.75 DPR 裁切，统计每行出现次数最多的
  精确调色板色；与 `terminal-palette.js` 浅/深参考值对照。

## 2. 结果

浅色（`ansi-normal.png`）与深色（`ansi-dark-normal.png` / `ansi-dark-bright.png`）均
**16/16 命中**；`terminal-ansi-colors-analysis.json` 的 `lightMatches=16`、`darkMatches=16`。

| 行 | 浅色参考 / 核心像素数 | 深色参考 / 核心像素数 |
| --- | --- | --- |
| C30 | `#000000` / 74 | `#000000` / 74 |
| C31 | `#CE0505` / 87 | `#F0524F` / 84 |
| C32 | `#067D17` / 75 | `#5C962C` / 75 |
| C33 | `#B28C00` / 78 | `#A68A0D` / 78 |
| C34 | `#063FDB` / 65 | `#3993D4` / 64 |
| C35 | `#B309B3` / 77 | `#A771BF` / 77 |
| C36 | `#028E8E` / 72 | `#00A3A3` / 72 |
| C37 | `#929292` / 71 | `#808080` / 71 |
| C90 | `#656565` / 69 | `#595959` / 69 |
| C91 | `#FF1616` / 101 | `#FF4050` / 81 |
| C92 | `#16B42C` / 72 | `#4FC414` / 74 |
| C93 | `#ECC32C` / 76 | `#E5BF00` / 76 |
| C94 | `#0060AF` / 62 | `#1FB0FF` / 90 |
| C95 | `#E617E6` / 77 | `#ED7EED` / 74 |
| C96 | `#15C1C1` / 71 | `#00E5E5` / 72 |
| C97 | `#C9C9C9` / 71 | `#FFFFFF` / 72 |

行高 18 CSS；截图尺寸 2511×1593（1435×910 CSS ×1.75）。C97 深色白字在深色底上仍能
取到 72 个精确白像素。

## 3. 证据与清理

- 浅色：`artifacts/terminal-ansi-colors-2026-10-24/ansi-normal.png`、
  `ansi-bright.png`、`terminal-ansi-colors.json`，PID 17820。
- 深色：`artifacts/terminal-ansi-colors-dark-2026-10-24/ansi-dark-normal.png`、
  `ansi-dark-bright.png`、`terminal-ansi-colors.json`，PID 28388。
- 分析：`artifacts/terminal-ansi-colors-analysis.json`（32 条逐行记录）。
- 两个实例均正常关闭；临时仓库删除；无 Augit/终端子进程残留。

## 4. 边界

- 只验证 **SGR 30–37/90–97** 的 16 个 ANSI 索引，不是 256 色/24-bit；未验证背景色、
  粗体/暗淡、选区色、光标色。
- 只验证浅/深当前 DPI 和默认字体；抗锯齿混合像素存在，但每行都取到了参考核心色。
- 参考值来自 `terminal-palette.js`（已由参考源文档推导），**不是** PyCharm 整页截图；
  页面级配对仍 50、像素仍 0，不写成参考像素通过。

## 5. 复验

```powershell
node tools/audit/terminal-ansi-colors.cjs <playwright> artifacts/terminal-ansi-colors-2026-10-24
node tools/audit/terminal-ansi-colors.cjs <playwright> artifacts/terminal-ansi-colors-dark-2026-10-24 --dark
python tools/audit/analyze-terminal-ansi.py artifacts/terminal-ansi-colors-2026-10-24 artifacts/terminal-ansi-colors-dark-2026-10-24 artifacts/terminal-ansi-colors-analysis.json
```
