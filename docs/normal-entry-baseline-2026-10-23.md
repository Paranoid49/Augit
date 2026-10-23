# 正常启动与构建身份基线（2026-10-23）

> 本批目标的第一项：先核实正常启动实例与当前构建产物一致，并记录可复验的身份、配置、
> 窗口几何和进程归属。本文不是界面验收结论，也不把 Debug 构建写成发布版性能结论。

## 1. 构建与启动

- 构建命令（强制重建，禁止增量误用旧试验 DLL）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1 `
  -Workspace D:/github/Augit/.tmp/entry-2026-10-23/workspace `
  -SettingsFile D:/github/Augit/.tmp/entry-2026-10-23/settings.json -PrepareOnly
```

- 结果：`Augit.Core` / `Augit.Infrastructure` / `Augit.Shell` 构建成功，**0 警告 0 错误**，耗时 11.55 s。
- 启动命令（刚重建、产物已核对，使用 `-SkipBuild`）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1 `
  -Workspace D:/github/Augit/.tmp/entry-2026-10-23/workspace `
  -SettingsFile D:/github/Augit/.tmp/entry-2026-10-23/settings.json -SkipBuild
```

- 实际 PID **432**；父进程 27596（启动器 PowerShell）；正常入口，无 `--dpi`、无场景参数、无 CDP。
- 正常关闭：向主窗口发送 `WM_CLOSE`（`PostMessage(hwnd, 0x0010)`），进程退出；2 s 后复查
  **无 Augit 与 WebView2 子进程残留**。未使用 `taskkill`。
- 隔离设置文件仅是本批副本：全局 `%LOCALAPPDATA%\Augit\settings.json` 的 SHA256 批前/批后均为
  `8840E7754E244F67B266E300AAB0142F7907E2C30FE6D0DB772C77F9962462CF`，未被覆盖。

## 2. 身份

| 项 | 值 |
| --- | --- |
| exe | `src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe` |
| exe SHA256 | `698448106AEAD9B8A27142EC51BAF29024B6B010E45582271159065251D09814` |
| dll | 同目录 `Augit.dll` |
| dll SHA256 | `5C8D63282FFEE34AAA4257D94FC31662171557EA1966ECF2100CCCB44892F787` |
| 进程实际加载模块 | `Augit.dll`，SHA256 与磁盘 dll 一致 |
| ProductVersion | `0.1.0+183ce3837bef148d6ca9ba35a91776314bbdb5ea`（exe 与 dll 相同） |
| FileVersion | `0.1.0.0` |
| WebView2 Runtime | `155.0.4283.62` |
| web 根 | `D:\github\Augit\web` |
| web 文件数 | 27 |
| web 树 SHA256（相对路径 + 每文件 SHA256 排序拼接） | `ce60102b9f7e7cd10fe72ebbcc10a2411583a47b6a436b51b24f94c3e45ebfa8` |
| `index.html` SHA256 | `260A4DAB98A11C3C00ACB3581A3AE6376525F2711333680A4882D273D22E2367` |
| `mockup.js` SHA256 | `DDE29E4641DCA91A78111F02C74654E3D7A07005CC9622565672AB065357431B` |
| `mockup.css` SHA256 | `7FD6E954BA26D52E43972DF6415AFCE8FDE0FC55A70AA7260CAA08E0C2653356` |
| `live-data.js` SHA256 | `1C2FEB1AE34180AC76AB6575654A2951B63FB697922863AAD64882EBBA24DDA9` |
| `large-text-view.js` SHA256 | `471C4F168E5AF21E4D81D97F599B2C85491CBB381B845ED45332132169FCDB8F` |

ProductVersion 内嵌的提交号与 HEAD `183ce3837bef148d6ca9ba35a91776314bbdb5ea` 一致；不是用旧产物冒充新源码。

## 3. 设置、字体、主题、DPI 与窗口

- 隔离配置：`.tmp/entry-2026-10-23/settings.json`，内容与全局配置逐字节相同（SHA256 `8840E775…462CF`）。
- 主题 `Light`；界面字体 `Microsoft YaHei UI`、界面字号 12；等宽字体 `JetBrains Mono`、字号 13；
  `appearanceVersion=1`。
- 随包字体：`web/vendor/jetbrains-mono/JetBrainsMono-Regular.woff2` 等四个 WOFF2 与 `OFL.txt` 由
  `verify-ui-assets.ps1` 与视觉稿副本逐对核对通过；本批未另行采样真实字形栅格，不能把配置值写成字形验收。
- DPI **168**（缩放 **1.75**）。
- 窗口 `GetWindowRect`：left -7、top -7、**1659×994** 物理像素；`GetClientRect`：**1645×980**；
  最大化；`DwmGetWindowAttribute(DWMWA_CLOAKED=14)` 返回 **0**；捕获时前台窗口为本窗口。
- 进程树：PID 432（Augit，Working Set 约 70.9 MB）＋ 6 个 WebView2 进程（浏览器进程 28276 及其
  28700/15676/4292/30008/23668 子进程），合计 Working Set 约 **626.2 MB**。这是**诊断值**：
  WebView2 使用用户默认 profile，且不是 Release 发布版，不能当作正式内存验收或历史 540–560 MB 的直接对照。

## 4. 刷新首内容证据的边界

同日已有[60fps 全窗刷新首内容测量](refresh-60fps-2026-10-23.md)：浅色 4 轮编辑区首内容
**380.0 / 406.7 / 287.0 / 259.3 ms**，深色 2 轮 **360.3 / 320.3 ms**，6 轮全屏均无全白/全黑帧。
该证据采于 HEAD `d26f226`（PID 30628），**不是本批 `183ce38` 构建**。

本批之后已在当前构建上补采：2026-10-24 的[外部修改 → 编辑区首内容重新测量](refresh-content-onset-2026-10-24.md)
用 ProductVersion `0.1.0+faeb0a1…`、真实鼠标双击入口、60fps gdigrab 采集了 13 轮：
浅色 **611 / 276 / 259 / 254 / 552 / 241 / 273 / 294 / 624 / 259 ms**、
深色 **275 / 293 / 263 ms**。其中 **611 / 552 / 624 ms 三次超过 500 ms**，不能把该目标写成连续通过。
同轮阶段诊断 20 轮 DOM 变化 153–194 ms、`document/read` 3–11 ms、`git/status` 88–120 ms，
没有出现 >500 ms；ddagrab 5 轮 223–274 ms。慢样本在无 CDP 原始 gdigrab 中出现，
但尚不能归因到文件监听、合成、DWM 或采集路径，详见该文 §6。
旧 `d26f226` 的 380.0/406.7/287.0/259.3 ms 保留作历史对照，不与本构建样本混算。

## 5. 证据文件与复验

- `identity.json`：`.tmp/entry-2026-10-23/identity.json`（本批未跟踪临时目录，不入库）。
- 构建日志、启动日志、检查脚本：同目录 `build.log` / `launch.log` / `inspect.ps1` / `close.ps1`。
- 复验：重新执行上面的构建与启动命令，按 `inspect.ps1` 重取 exe/dll/web/窗口/进程树；
  全局设置 SHA256 必须仍为 `8840E775…462CF`。
- 本批临时工作区曾含 `sample.txt`、`mid.txt`（80,000 行 / 2,868,894 B）和 `huge.txt`
  （22.75 MiB / 23,855,178 B），用于下一批大文件正常入口；这批大文件夹具在测量后已删除，
  只保留 `sample.txt`，目录不属于仓库内容。
  上述“下一批”已在同日晚完成：真实外壳 2.96 MB/80,000 行与 22.75 MiB 预览窗口层正常入口
  见[大文件预览虚拟化原型 §6](large-text-virtualization-2026-10-23.md#6-真实外壳正常入口2026-10-24)；
  基线实例只用于身份与窗口几何，不与那份行为测量混写。
