# 搜索浮层在 20px 界面字号下的布局（2026-10-24）

> 结论：`textFontSize=20` 时搜索浮层重新量 header，Match case / Words / Regex 均为
> 49×49，Include ignored files 宽 217；四个控件互不覆盖，中心命中各自控件，真实 OS
> 点击仍能切换 Match case。此前的 28px 选项只在默认 13px 下成立。

## 1. 方法

- 隔离设置从当前全局副本创建，写入：
  - `fontSize=20`；
  - `textFontSize=20`；
  - `codeFontSize=20`。
- 真实外壳打开搜索浮层；CDP 读取控件矩形、中心 `elementFromPoint`、`aria-pressed`；
  先 DOM click 再 DPI-aware OS click。
- 只用于诊断输入：搜索通过 CDP `Ctrl+Shift+F` 打开；其余为产品正常入口。

## 2. 结果

| 控件 | x（CSS） | y | 宽 | 中心命中 |
| --- | ---: | ---: | ---: | --- |
| Match case | 685.9 | 149.1 | **49** | svg 后代 |
| Words | 741.9 | 149.1 | 49 | svg 后代 |
| Regex | 797.9 | 149.1 | 49 | path 后代 |
| Include ignored files | 854.9 | 149.1 | 217 | label |

- 浮层约 730 宽，ignored 右缘约 1071.9，仍在浮层内；
- 四项 x 互异，无重叠；
- DOM click：Match case `false → true`；
- DPI-aware OS click：`true → false`；
- 实例 PID 20912 正常关闭，`pidAlive=false`。

## 3. 证据

- 截图/报告：`artifacts/search-font20b-2026-10-24/`；
- 诊断脚本：`tools/audit/search-match-case-probe.cjs --large-font`（设置 20px）；
- 与 13px 的结果对比：

| 项 | 13px | 20px |
| --- | ---: | ---: |
| 选项宽/高 | 28 | 49 |
| ignored 宽 | 139 | 217 |
| 是否重叠 | 否 | 否 |

## 4. 边界

- 只验证 20px 一种大字号；未覆盖 32/40px，也未覆盖窄窗 + 大字号组合；
- 这是 DOM 几何/命中证据，不是参考 PyCharm 搜索浮层像素；
- 页面级配对仍 50、像素仍 0；
- `--large-font` 名称是诊断模式，不代表产品提供该字号开关。

## 5. 复验

```powershell
node tools/audit/search-match-case-probe.cjs <playwright> artifacts/search-font20b-2026-10-24 --large-font
```
