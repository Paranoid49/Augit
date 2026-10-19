# 终端状态栏高度与顶边修复（2026-10-19）

## 1. 实测差异

同一 2880×1716 / DPI 168 / 浅色终端：

| 项目 | 参考 | 修复前 | 修复后 |
| --- | --- | --- | --- |
| 状态栏上缘 | 1661 | 1668 | **1661** |
| 1661 像素 | `#F1F2F4` 1px 顶边 | `#E9EAEE` | **`#F1F2F4`** |
| 1662 像素 | `#E9EAEE` | `#E9EAEE` | **`#E9EAEE`** |
| 终端白色正文底 | 1660 | 1667 | **1660** |

## 2. 实现

- `mockup.js` 同时写 `--augit-status-height` 与基准 `--augit-status-height-base`。
- 终端打开时状态栏高 28→32 CSS：
  ```css
  .augit-window:has(.terminal-tool) {
    --augit-status-height: calc(var(--augit-status-height-base, 28px) + 4px);
  }
  ```
- 顶边取 **1 设备px**：
  ```css
  .augit-window:has(.terminal-tool) .statusbar {
    border-top: calc(1px / var(--augit-dpr, 1)) solid var(--augit-terminal-panel-border, #f1f2f4);
  }
  ```
  `--augit-hairline` 在 175% 下按 `round(scale)=2` 设备px 计算，会得到 2 物理px；这里直接除以 DPR，实测正好 1 物理px。
- 主窗/文本状态栏不受影响；隐藏终端后回到 1668。

## 3. 正常入口验证

- 高度阶段：PID 3620，真实鼠标点击 Terminal；状态栏上缘 1661。
- 顶边阶段：PID 452，同一路径；像素采样：
  ```text
  1660 #FFFFFF
  1661 #F1F2F4
  1662 #E9EAEE
  ```
  与参考逐像素一致。
- 证据：
  - `terminal-status-fixed-normal.png`（`9E7ABCA4…CACA`）
  - `terminal-status-border-1px.png`（`terminal-status-border/`，最终顶边）
- 隐藏终端截图仍为 `terminal-hidden-normal.png`，上缘 1668。

### 深色

深色参考在状态栏顶边前还有 1 物理px 工具窗底 `#191A1C`，浅色没有。新增：

```css
body[data-theme="dark"] .terminal-tool {
  border-bottom: calc(1px / var(--augit-dpr, 1)) solid var(--augit-panel);
}
```

正常入口 PID 4672、真实点击 Terminal，采样：

```text
1659 #1E1F22   （参考 #1C1D20，单像素抗锯齿级差）
1660 #191A1C   （参考 #191A1C）
1661 #212326   （参考 #212326）
1662 #26282C   （参考 #26282C）
```

深色状态栏边界已对齐；不再只是几何诊断。

## 4. 验证与身份

- `tools/test-terminal-status-height.mjs`：**7/7 PASS**。
- `verify-ui-assets.ps1` PASS；`node --check` 通过。
- 新 `mockup.js` `DA93667A…3C59`；新 `mockup.css` `F53F53D8…1EDF`；`live-data.js` 仍 `12D57E34…1B3D`。
- 页面级同态配对仍 **50**；像素合格整页仍 **0**。终端 ANSI 蓝、深色首行、右侧栏 42 CSS 仍未闭环；深色首行/整页未重采。

## 5. 清理

- PID 3620、452、4672 正常关闭；临时夹具删除；全局设置仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-181.json`（高度阶段）、`cleanup-check-182.json`（浅色顶边）、`cleanup-check-183.json`（深色底边最终）。
