# Settings 深色首内容与关闭路径实测（2026-10-22）

本文补齐 `remaining-inputs-2026-10-19.md` 中 Settings 的“深色、关闭各路径、全窗可见证据”缺口，并纠正 `pixel-pair-protocol-2026-10-22.md` 与既有 presented 采集方法中的一处 DWM 属性错误。结论只覆盖 Settings 生命周期与首内容，不是 Settings 页面像素验收。

## 1. 结论

1. 深色主题下 3 个正常入口周期的点击→presented 为 **533.7 / 492.4 / 538.3 ms**；原生 `created`→`presented` 为 431.3 / 398.4 / 449.1 ms，与浅色历史 495–585 ms 同一量级。
2. 3 个周期的 presented 截图与 +60 ms 截图 SHA256 全部相同（`D7A60C11…40E7563`），内容为完整的深色 Appearance 页；3 次快速点击仍只有 **1** 个设置窗口。
3. 关闭三条路径均生效：Cancel 点击→消失 99.8 ms、OK 126.2 ms、标题关闭 115.4 ms；原生 `close-start`→`destroyed` 分别为 44.6 / 58.4 / 48.8 ms。数值包含 30 ms 鼠标按住与轮询开销，不是纯宿主耗时。
4. 60 fps Desktop Duplication 录像中，设置首帧与稳定帧 **逐像素相同**（同一 SHA256），前一帧仍是主窗内容，中间没有空白/白色/半成品帧；因此本批深色打开未观察到可见闪帧。
5. `CapturePreviewAsync` 本批 79.5–116.8 ms，未触发 500 ms 超时；未复现 2904 ms 慢样本。
6. **采集方法纠正**：查询 DWM 是否 cloaked 必须用 `DWMWA_CLOAKED`（属性 **14**）。属性 13 是只写的 `DWMWA_CLOAK`，查询返回 `E_INVALIDARG (0x80070057)` 且输出值保持 0；按属性 13 判定会把“已 ShowWindow、仍未撤 cloak”误当成 presented，截图抓到的是所有者主窗内容。上一轮首组配对与本文第一轮误判均由此产生，已用属性 14 重测。

## 2. 构建与运行身份

| 项目 | 值 |
| --- | --- |
| HEAD | `82d8625347ac8e3664335aab214e1f14348b36b3`（`origin/dsh` 同） |
| ProductVersion | `0.1.0+82d8625347ac8e3664335aab214e1f14348b36b3` |
| `Augit.exe` SHA256 | `3D1E368F8AA719839E426CEA8BE93E5B5C776982492F2079360B45DEF9204C63` |
| `Augit.dll` SHA256 | `5138D94B66ECD9A29BFD9AD32D511E1AA28BE23F964B9C0E1FC13B9D912020C9` |
| `mockup.js` / `mockup.css` | `FD3A760E…962AB9` / `E17E108A…12C9E0` |
| `live-data.js` / `bridge.js` | `9454F15E…1347E` / `6EFF9F71…3BF52` |
| 启动 | `powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1 -Workspace D:/github/Augit -SettingsFile D:/github/Augit/.tmp/settings-normal-entry-2026-10-22.json` |
| 隔离配置 | 从本轮全局设置逐字节复制后启动；在设置页把 Theme 改为 Dark 并 Apply，只写隔离副本 |
| 主题 / 字体 | Dark / Microsoft YaHei UI 12 / JetBrains Mono 13 |
| 桌面与窗口 | 单屏 2880×1800，DPI 168（scale 1.75）；最大化，外框 2904×1740 @ (-12,-12)，客户区 2880×1716 |
| 实例 | PID 25648（第一轮，属性 13）、PID 20984（属性 14 重测）；均正常关闭 |

## 3. 方法

- 正常入口，无 `scene`、无 CDP、无页面注入；所有输入为真实 `SendInput` 鼠标/键盘。
- 设置入口坐标取主窗 UIA `Settings` 超链接的实测矩形中心（物理 `2664,38`），不猜坐标。
- presented 判定：枚举本实例顶层 `Settings — Augit` 窗口，要求 `IsWindowVisible` 且 `DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED=14) == 0`。
- 判到 presented 后立即用 `CopyFromScreen` 抓设置窗口 DWM 扩展边界；60 ms 后再抓一张，比较 SHA256。
- 另用 ffmpeg `ddagrab`（Desktop Duplication，60 fps，区域 530,193–2350,1523）录一次深色打开，导出 1.2–4.2 s 的 179 帧逐帧比较。
- 原生阶段由 `AUGIT_SETTINGS_TRACE_DIRECTORY` 导出；周期与阶段文件成对保存。

命令（本轮实际使用）：

```powershell
$env:AUGIT_SETTINGS_TRACE_DIRECTORY='D:/github/Augit/artifacts/settings-dark-2026-10-22/trace-presented'
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1 `
  -Workspace D:/github/Augit `
  -SettingsFile D:/github/Augit/.tmp/settings-normal-entry-2026-10-22.json `
  -SkipBuild
```

## 4. 结果

### 4.1 打开与 presented

| 周期 | 点击→presented | `created`→`presented` | `window/settings-present` 消息 | `CapturePreviewAsync` |
| --- | ---: | ---: | ---: | ---: |
| Cancel 收尾 | 533.7 ms | 431.3 ms | 304.1 ms | 83.8 ms |
| OK 收尾 | 492.4 ms | 398.4 ms | 261.1 ms | 79.5 ms |
| 标题关闭收尾 | 538.3 ms | 449.1 ms | 275.2 ms | 82.3 ms |
| 3 次快速点击 | 600.6 ms | 480.1 ms | 329.4 ms | 116.8 ms |

- 三个周期的 presented 图、+60 ms 图、快速点击图 SHA256 全部为 `D7A60C1125D4A6DB2562CD365042F187F41E9D1D879E8BBAB0CF6BB0B40E7563`。
- 快速点击间隔约 100 ms（47.1 / 249.7 / 440.2 ms），完成后只找到 1 个设置窗口。
- 本批未触发 500 ms capture 超时，未复现 2904 ms 慢样本。

### 4.2 关闭

| 关闭路径 | 点击→窗口消失 | `close-start`→`destroyed` | 残留设置窗 |
| --- | ---: | ---: | ---: |
| Cancel | 99.8 ms | 44.6 ms | 0 |
| OK | 126.2 ms | 58.4 ms | 0 |
| 标题栏关闭 | 115.4 ms | 48.8 ms | 0 |

点击→消失从鼠标按下计时，含脚本 30 ms 按住与 2 ms 轮询；与原生 destroy 的差值主要是输入与轮询开销。

### 4.3 60 fps 首内容

179 帧逐帧比较结果：

- 第 133 帧（以 0 起算，对应 `f0134.png`）仍是主窗内容；第 134 帧（`f0135.png`）已经是完整设置页。
- `f0135` 与稳定帧 `f0150` 的 SHA256 相同（`542070C9…57A210`），逐像素差 0。
- 相邻帧之间没有空白、纯白、纯黑或只画了部分控件的中间帧。

因此本批深色打开的桌面可见过渡是“主窗内容 → 完整设置页”的单帧切换。60 fps 采样仍可能漏掉短于 16.7 ms 的中间帧，但结合 presented 后立即 Screen 抓图也已是完整页，可把该风险压到很低；这不等于所有机器、所有主题、所有负载下永不出闪。

### 4.4 采集方法反例

第一轮按属性 13 查询时：

- 3 个周期误报 presented 为 333.5–429.7 ms（实际那是 `ShowWindow` 后、撤 cloak 前的可见时刻）。
- presented 截图抓到的是所有者主窗内容，+60 ms 才是设置页；两次哈希不同。
- 同一个错误会让“presented 首帧完整”的旧结论失去依据：旧采集若用属性 13，所谓 presented 可能只是窗口可见，不是桌面已呈现设置内容。

## 5. 本轮核对的规范问题

### 5.1 侧栏 699 vs 703 物理 px

- 实测同一行：两端项目树左缘都在物理 x=72，即 rail 37 + 4 gap = 41 CSS × 1.75 ≈ 71.75。
- `side-panel-default=360` 是左侧工具窗口轨道宽度，**不含** 37 px rail、两侧 4 px gap 或表面边框；`design-system` §4 的 360 口径成立。
- 主网格为 `37 + 4 + 360 + 4 + 编辑区`（CSS）。Augit 树白色内容止于 699、参考止于 703，差 4 物理 px（2.3 CSS）；该差值可能与参考侧栏保存宽度、1 px 边框或滚动条留白有关，量级不足以证明 360 token 错误。要确认需读取参考侧栏的保存宽度与边框口径，不能拿白色内容边缘当分隔位置。
- 结论：不改 token；后续若仍需收窄，先取参考的侧栏保存宽度与边框口径，而不是直接改 360。

### 5.2 标题栏中部 “Current File”

- 参考 PyCharm 2026.2.1 的标题栏中部是 **Run 配置选择器**，不是文件路径入口。点击后菜单为 `Current File` 与 `Edit Configurations…`，右侧还有 Run/Debug 图标。
- 打开 `app.py` 文本编辑器后标题栏仍显示 `Current File`，说明它不随当前文件变化。
- Run 配置/AI 等是参考独有能力，Augit 不新增；Augit 在该位置显示当前文件名，属于产品信息，不应为了凑图加一个没有功能的 `Current File` 下拉。
- 结论：把该区域登记为参考独有功能差异；`pixel-pair-protocol` 第 4.3/4.4 节把它写成“文件上下文语义差异”不准确，已更正。证据：`artifacts/pixel-rulings-2026-10-22/pycharm-run-widget-menu.png`、`pycharm-apppy-titlebar.png`。

### 5.3 仍需产品裁决的三项

1. **Markdown 预览字体/字号**：参考插件 CSS 为正文 14 px `Helvetica, Arial, freesans, sans-serif`，`pre` `0.85em`、`code` `0.9em`；`product-spec.md` §7 与 `design-system.md` §5.3 要求预览正文跟随界面字号、代码跟随等宽字号。两者不能同时满足。候选实现已验证但未提交。
2. **Push 失败呈现**：参考关闭对话框并在右下显示 `Push failed` balloon（错误正文 + `Show details in console`）；`ux-spec.md` §7.12 要求失败保留对话框、列表与焦点并允许重试。需要确认是否采用“关闭对话框 + balloon + 点击动作重新打开保留重试”的兼容方案。
3. **文件状态表达**：参考 Project Files 视图只用文件名颜色；`ux-spec.md` 第 171 行要求同时显示状态文字或文件状态标记，Augit 现有 `M` 徽标因此与参考不同。需要确认是改为仅颜色，还是保留徽标并登记为已批准差异。

在裁决前不提交对应产品改动；其余已授权工作继续。

## 6. 清理与边界

- 两个实例均正常关闭（PID 25648、20984）；关闭后本实例 `msedgewebview2` 子进程不存在，按父进程核对未误算 SearchHost/GameViewer 的 WebView2。
- 全局 `%LOCALAPPDATA%\Augit\settings.json` 哈希始终为 `8840E7754E244F67B266E300AAB0142F7907E2C30FE6D0DB772C77F9962462CF`，最后写入时间仍是 2026-10-22 04:58，本轮未回写。
- 隔离配置与 trace 留在本机 `.tmp/` 与 `artifacts/settings-dark-2026-10-22/`（Git 忽略）。
- 仅覆盖深色、单屏 2880×1800/DPI 168、最大化、3 个周期 + 1 次快速点击；未覆盖跨屏 DPI、Windows 10、2904 ms 样本、capture 超时分支、长时间重复开关和全窗录屏。
- 本批是生命周期/首内容证据，不改变页面级同态配对 50、整窗像素 0 的计数，也不把 Settings 简化页记作像素通过。
