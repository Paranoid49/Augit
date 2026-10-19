# 终端 ANSI 蓝改为参考 `#0060AF`（2026-10-19）

## 1. 根因

xterm 的 256 色路径把 ANSI 索引 12（`38;5;12`）映射到 `theme.brightBlue`，不是独立的硬编码表。检查 `web/vendor/xterm/xterm.js`：

```text
t.ansi[12]=v(e.brightBlue,o.DEFAULT_ANSI_COLORS[12])
```

而 `web/src/terminal-palette.js` 的浅色 `brightBlue` 是 `#2D61F0`，参考是 `#0060AF`。此前的 `#298ADE` 是 ClearType/命令语法抗锯齿后的采样值，不是 BLUE 输出核心色；因此之前“调色板无效”的判断不成立。

## 2. 改动

- `web/src/terminal-palette.js`：浅色 `brightBlue` `#2D61F0` → `#0060AF`。
- `tools/audit/terminal-palette.test.cjs`：期望值同步为 `#0060AF`，2/2 通过。
- `terminal-palette.js` 不在 `docs/ux-mockups/` 中，不需要镜像；UI 资产校验仍通过。

## 3. 正常入口前后对比

同一命令、同一终端、浅色主题：

```powershell
Write-Host "BLUE" -ForegroundColor Blue
```

裁切 BLUE 输出行统计核心像素：

| 版本 | 核心色 | 计数 |
| --- | --- | --- |
| 改前 `#2D61F0` | `(45,97,240)` | 46 |
| 改后 `#0060AF` | `(0,96,175)` | 46 |

`#298ADE` 只作为抗锯齿/命令语法像素存在，不再是输出核心色。

## 4. 证据

- `artifacts/pixel-followup-2026-10-19/ansi-palette/before.png`（改前）
- `.../blue2.png`（改后）
- `terminal-palette.test.cjs` 2/2
- 正常入口 PID 16888（改后）、11208（改前），均无 CDP；终端真实点击/输入。

## 5. 边界

- 只验证浅色 `Blue` 输出；深色亮蓝、其他 ANSI 索引未重新逐色验收；
- 不增加页面级配对计数；页面级仍 **50**、像素仍 **0**；
- 未提交、未推送。

## 6. 清理

- 无残留 Augit/PyCharm；临时 fixtures 删除；全局设置仍 `B8868855…DC3D`；
- `cleanup-check-204.json`。
