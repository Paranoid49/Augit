# 视觉审计工具

本目录的脚本用于在 Windows 上采集 Augit 界面的**真实像素**证据。`docs/design-system.md` 规定：
`PrintWindow` 只能证明结构与范围，屏幕字体、抗锯齿和字宽验收必须来自未被遮挡的屏幕截图。

## capture-surface.ps1

启动任意被测程序，等待主窗口出现，按需把窗口提到前台，采集窗口矩形范围的屏幕像素。
采集后会统计中心区域的纯白占比；占比超过阈值说明窗口被其它前台程序遮挡，会重试。

```powershell
# 采集视觉审计宿主的某个场景
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\capture-surface.ps1 `
  -Exe   path\to\Augit.App.VisualAuditHost.exe `
  -Settings path\to\settings.json -Workspace path\to\workspace `
  -Surface main-project -Out artifacts\main.bmp -Dpi 96

# 采集任意程序（例如新的 WebView2 外壳）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\capture-surface.ps1 `
  -Exe path\to\Augit.Shell.exe -Out artifacts\shell.png `
  -Arguments '--scene','main-project','--theme','dark'
```

可选参数：`-WorkDir`（工作目录）、`-TimeoutSec`、`-Attempts`、`-SettleMs`、`-MaxWhitePercent`。

### 行为说明

- 若重试后仍被遮挡，脚本回退到 `PrintWindow(PW_RENDERFULLCONTENT)` 并输出
  `PRINTWINDOW_FALLBACK`。该回退不受遮挡影响；对 WebView2 承载的界面会返回真实合成结果，
  但对纯 GDI 自绘界面仍然只是结构证据，不能当作屏幕字形证据。
- 全部尝试失败时输出 `OCCLUDED_OR_INVALID` 并以退出码 4 结束，不会写入伪造的成功截图。

## shell-capture.ps1

构建并采集 WebView2 外壳的单个场景，内部先结束残留的 `Augit.Shell` 进程，避免可执行文件被占用。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\shell-capture.ps1 `
  -Scene main-project -Theme dark
```

## 实现陷阱（已踩过，勿重复）

- **PowerShell 5.1 按 ANSI 读取 `.ps1`**：脚本内出现任何非 ASCII 字符都会导致解析失败。本目录脚本
  的注释与字符串全部使用 ASCII。
- **`[ref]` 多参数不可靠**：PowerShell 只正确封送 `GetWindowRect(IntPtr, ref int, ref int, ref int, ref int)`
  的第一个引用参数，其余静默为 0，导致窗口矩形恒为 `-80x0`。必须使用 `out RECT` 结构体重载。
- **DPI 感知**：脚本必须声明 Per-Monitor-V2（`SetProcessDpiAwarenessContext(-4)`），否则
  `GetWindowRect` 返回虚拟化坐标，截屏会落在错误像素上。
- **窗口枚举不要过滤尺寸**：Win32 窗口在构造期间会短暂上报 `-46x0` 一类退化矩形。应先按
  进程取 `MainWindowHandle`，拿不到时再枚举可见顶层窗口并轮询到尺寸稳定。
- **`CreateWindowEx` 之后不能立即采集**：WebView2 的控件创建依赖消息循环泵消息，
  必须在消息循环开始后才发起（宿主用自定义消息投递）。
- **`RegisterClassExW` 返回 `ERROR_INVALID_PARAMETER`(87)**：`WNDCLASSEXW` 必须包含末尾的
  `hIconSm` 字段；x64 上该结构为 80 字节。字段缺失会被判为非法参数。

## verify-ui-assets.ps1

校验运行时界面资源（`web/src`）与视觉稿（`docs/ux-mockups`）的同名文件逐字节一致。
视觉稿是设计基线，两者共用同一套 CSS 与场景脚本；任何漂移都会让「视觉稿即产品代码」失效。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\verify-ui-assets.ps1
```

修改视觉稿后同步：

```powershell
copy docs\ux-mockups\mockup.js        web\src\mockup.js
copy docs\ux-mockups\mockup.css       web\src\mockup.css
copy docs\ux-mockups\current-find.js  web\src\current-find.js
copy docs\ux-mockups\image-preview.js web\src\image-preview.js
```

## click-window.ps1

在被测窗口的客户区内按**逻辑像素**坐标点击，用于验证真实交互（悬停、展开、标签切换）。
脚本按窗口 DPI（`GetDpiForWindow`）换算物理坐标，因此与网页层的 CSS 坐标一致。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit\click-window.ps1 `
  -X 60 -Y 188 -Out artifacts\click-expand.png
```

参数：`-ProcessName`（默认 `Augit.Shell`）、`-AfterMs`（点击后等待，默认 1500）。
