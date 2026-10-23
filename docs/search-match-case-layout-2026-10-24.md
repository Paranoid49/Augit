# 搜索浮层首次打开时选项重叠的修复（2026-10-24）

> 结论：正常入口首次打开搜索浮层时，三个选项按钮和 Include ignored files 没有获得 inline
> `left/top`，绝对定位在**同一原点**（实测中心 x≈367.9）；checkbox 覆盖 Match case，
> 鼠标点击点到 checkbox。修复为打开浮层和每次区域重绘后重新量 header；修复后按钮
> x≈832.9/864.9/896.9，ignored x≈932.9，Match case 可被真实鼠标切换。

## 1. 根因

- `measureSearchOverlay()` 只由页面初始化/字体预览触发；由 `openSearchOverlay()` 新建的
  `.search-overlay.live-overlay` 没有调用它。
- `.search-option` 与 `.search-ignored` 的 CSS 都是 `position:absolute`；没有 inline
  `left/top` 时全部落在同一静态位置。
- 诊断报告：`artifacts/search-match-case-probe-2026-10-24/`：
  - Match case `/ Words / Regex` 的 rect 均为 `x=353.857, y=151.643, 28×28`；
  - 三者中心 `elementFromPoint` 都是 **INPUT `Include ignored files`**；
  - `locator.click()` 也因此被 Playwright 判为“被 checkbox 拦截”。

这解释了此前搜索超时重试中 Match case 点击不切换、Words/Regex 偶尔切换的竞态：
不是按钮逻辑，而是选项叠在一起。

## 2. 修复

- `live-data.js` 的 `openSearchOverlay()`：append/bind/focus 后调用
  `window.measureSearchOverlay()`。
- `rebindAfterRender()`：若存在 `.search-overlay`，重新量一次；区域替换后仍保持位置。
- `live-shell` 的“全仓搜索三个开关”断言扩展为：
  - 三个按钮 `x` 互不相同；
  - 每个中心命中的是按钮自身或其后代；
  - `search-ignored` 与三个按钮矩形不重叠。
- `tools/audit/search-match-case-probe.cjs` 作为可复验诊断：记录 rect、`elementFromPoint`、
  DOM click 与 DPI-aware OS click 的 `aria-pressed`。

## 3. 修复后正常入口

真实外壳、CDP 打开搜索（仅诊断输入），800ms 后：

| 控件 | x（CSS） | 中心命中 |
| --- | --- | --- |
| Match case | 832.9 | svg 后代 |
| Words | 864.9 | svg 后代 |
| Regex | 896.9 | path 后代 |
| Include ignored files | 932.9 | label |

- DOM click：`Match case` `false → true`；
- DPI-aware OS click：`true → false`（真实鼠标切换成功）；
- 实例 PID 8348 正常关闭。
- 报告：`artifacts/search-match-case-probe-2026-10-24c/search-match-case-probe.json`。

## 3.1 自动化\n\n- `live-shell` 的选项布局断言扩展后，完整套件 **2371/2371 通过、退出码 0**。\n- 断言要求三个按钮 x 互异、每个中心命中自身/后代、ignored 不与按钮矩形重叠。\n\n## 4. 边界

- 只测 1435×910 CSS 视口、深色、默认字号；窄窗/大字号需重新确认。
- 诊断使用 CDP 发送 `Ctrl+Shift+F` 和 DOM click，只用于定位；OS click 是真实 Win32。
- 修复是布局测量，不是参考像素；选项颜色/最终位置仍无 PyCharm 逐像素配对。
- `search-match-case-probe.cjs` 的 `locator.click` 在修复前会超时，脚本已改为 DOM/OS 两条路径；
  该失败是诊断证据，不是产品行为。

## 5. 复验

```powershell
node tools/audit/search-match-case-probe.cjs <playwright> artifacts/search-match-case-probe-2026-10-24c
node tools/audit/live-shell.spec.cjs <playwright>
```
