using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Augit.Infrastructure.Settings;
using Microsoft.Web.WebView2.Core;

namespace Augit.Shell;

/// <summary>
/// 原生 Win32 外壳窗口：只负责窗口框架、尺寸与 WebView2 控件的承载，界面内容全部由网页层渲染。
/// </summary>
internal sealed class ShellWindow : IDisposable
{
    private const string WindowClassName = "Augit.Shell.Window";
    private const string DefaultVirtualHost = "augit.local";
    // 视觉稿自绘标题栏（含窗口按钮），所以窗口本身不带 caption：
    // WS_OVERLAPPEDWINDOW 去掉 WS_CAPTION 后仍是可缩放的重叠窗口（最大化按工作区、任务栏图标保留），
    // 客户区顶到窗口边由 WM_NCCALCSIZE 处理，拖动/缩放由 WM_NCHITTEST 处理。
    private const int WsCaption = 0x00C00000;
    private const int WsFramelessWindow = 0x00CF0000 & ~WsCaption;

    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmNclButtonDown = 0x00A1;
    // 网页层发起的标题栏拖动：先回响应，再进入系统移动循环（见 HandleWindowCommand）。
    private const uint StartDragMessage = 0x0400 + 5;
    private const uint StartResizeMessage = 0x0400 + 6;
    private const int HtCaption = 2;
    private const int SwShow = 5;
    private const int SwShowMaximized = 3;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const int MonitorDefaultToNearest = 2;
    // 双击判定用的系统度量：双击时间与双击矩形的宽高。
    private const int SystemMetricsCxDoubleClick = 36;
    private const int SystemMetricsCyDoubleClick = 37;
    // SetWindowPos 的重算框架标志：让系统在窗口登记之后再发一次 WM_NCCALCSIZE。
    private const int SwpNosize = 0x0001;
    private const int SwpNomove = 0x0002;
    private const int SwpNozorder = 0x0004;
    private const int SwpNoactivate = 0x0010;
    private const int SwpFramechanged = 0x0020;
    private const int SystemMetricsXVirtualScreen = 76;
    private const int SystemMetricsYVirtualScreen = 77;
    private const int SystemMetricsCxVirtualScreen = 78;
    private const int SystemMetricsCyVirtualScreen = 79;
    private const uint WmSize = 0x0005;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmDpichanged = 0x02E0;
    private const uint WmGetMinMaxInfo = 0x0024;

    private const int IdiApplication = 32512;
    private const uint SpiGetWorkArea = 0x0030;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint InitializeMessage = 0x0400 + 1;
    private const uint ReplyMessage = 0x0400 + 3;

    private static readonly Dictionary<nint, ShellWindow> LiveWindows = [];
    private static readonly Lock LoaderGate = new();
    private static nint _loaderModule;
    private static readonly WindowProcedure Procedure = OnWindowMessage;
    private static bool _classRegistered;
    private static bool _messageLoopRunning;

    private readonly ShellOptions _options;
    private readonly WindowPlacementSettings _savedPlacement;
    private readonly bool _persistPlacement;
    private readonly nint _instance;
    private nint _window;
    private int _effectiveDpi;
    private bool _startupMaximized;
    // 上一次推给网页层的最大化状态；只在真的变化时推送，避免每次 WM_SIZE 都跨进程发消息。
    private bool? _reportedMaximized;
    // 标题栏双击判定：上一次标题栏按下的时刻与位置。
    private long _lastDragTick;
    private ShellPoint _lastDragPoint;
    // 待执行的边缘缩放命中码（网页层报告 -> 消息队列 -> 系统缩放循环）。
    private int _pendingResizeCode;
    private Rect _workArea;
    private bool _hasWorkArea;
    private readonly ShellBridge _bridge;
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private bool _disposed;
    private readonly Queue<string> _pendingReplies = new();

    public ShellWindow(ShellOptions options, WindowPlacementSettings? savedPlacement = null)
    {
        _options = options;
        _savedPlacement = savedPlacement ?? new();
        // 审计与视觉对照会显式指定场景、DPI 或尺寸；这些运行不得把审计窗口写进用户设置。
        _persistPlacement = options is
        { Width: null, Height: null, Dpi: null, PixelExact: false, NoSessionRestore: false, Scene: null or "" };
        _bridge = new ShellBridge(options.WorkspaceRoot, Notify);
        // 窗口命令（自绘窗口按钮、标题栏拖动）只有窗口能做：桥接层不认识 HWND。
        _bridge.WindowCommandHandler = HandleWindowCommand;
        _instance = GetModuleHandle(null);
        _effectiveDpi = ShellWindowSizing.ResolveEffectiveDpi(
            options.Dpi,
            options.PixelExact,
            (int)GetDpiForSystem());
        RegisterWindowClass();
        _window = CreateWindowInstance();
        if (_window == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建外壳窗口失败。");
        }

        LiveWindows[_window] = this;

        // 关键：CreateWindowEx 内部会先发一次 WM_NCCALCSIZE，那时窗口还没登记，
        // 只能由默认过程按 caption + 边框算出非客户区，原生标题栏与按钮就是这么出现的
        // （实测 GWL_STYLE 带回 WS_CAPTION、客户区上边距 52 物理像素）。
        // SWP_FRAMECHANGED 会让系统在窗口已经登记之后再算一次框架，这次才落到我们的规则上。
        _ = SetWindowPos(
            _window,
            0,
            0,
            0,
            0,
            0,
            SwpFramechanged | SwpNomove | SwpNosize | SwpNozorder | SwpNoactivate);
        // WebView2 的控件创建必须在消息循环开始之后进行：CreateCoreWebView2ControllerAsync
        // 依赖 Shell 嵌入式浏览器在 UI 线程上泵消息，若在 Main 里等待会直接死锁。
        if (!PostMessage(_window, InitializeMessage, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "投递界面初始化消息失败。");
        }
    }

    /// <summary>原生窗口句柄；供单实例登记与外部诊断使用。</summary>
    public nint Handle => _window;

    public void Show()
    {
        // 上次退出时是最大化，就按最大化显示；尺寸来自设置的还原矩形。
        ShowWindow(_window, _startupMaximized ? SwShowMaximized : SwShow);
    }

    public static int RunMessageLoop()
    {
        _messageLoopRunning = true;
        while (GetMessage(out NativeMessage message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        _messageLoopRunning = false;
        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller?.Close();
        _controller = null;
        if (_window != 0)
        {
            LiveWindows.Remove(_window);
            DestroyWindow(_window);
            _window = 0;
        }
    }

    private static void RegisterWindowClass()
    {
        if (_classRegistered)
        {
            return;
        }

        WindowClass windowClass = new()
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Style = 0x0002 | 0x0001,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = GetModuleHandle(null),
            Cursor = LoadCursor(0, IdiApplication),
            ClassName = WindowClassName,
        };
        ushort atom = RegisterClassEx(ref windowClass);
        int error = Marshal.GetLastWin32Error();
        if (atom == 0 && error != ErrorClassAlreadyExists)
        {
            throw new Win32Exception(error, "注册外壳窗口类失败。");
        }

        _classRegistered = true;
    }

    /// <summary>
    /// 清理历史会话目录。只删除本应用创建的 Session-* 目录；
    /// 仍被其它实例占用的目录删不掉，忽略即可，不影响本次启动。
    /// </summary>
    private static void CleanStaleSessions(string root, string keep)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(root, "Session-*"))
        {
            if (string.Equals(directory, keep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 目录正被其它实例使用；下次启动再试。
            }
        }
    }

    private nint CreateWindowInstance()
    {
        // 窗口按生效 DPI 放大，使界面拿到与视觉稿一致的逻辑视口；再收敛到工作区，
        // 避免 175%/200% 缩放的笔记本屏幕上窗口比屏幕还大。
        _hasWorkArea = SystemParametersInfo(SpiGetWorkArea, 0, ref _workArea, 0);
        PhysicalPlacement placement = ShellWindowSizing.ResolveStartupPlacement(
            new SavedPlacement(
                _savedPlacement.Left,
                _savedPlacement.Top,
                _savedPlacement.Width,
                _savedPlacement.Height,
                _savedPlacement.IsMaximized),
            _options.Width,
            _options.Height,
            _effectiveDpi,
            WorkWidth,
            WorkHeight,
            new VirtualScreen(
                GetSystemMetrics(SystemMetricsXVirtualScreen),
                GetSystemMetrics(SystemMetricsYVirtualScreen),
                GetSystemMetrics(SystemMetricsCxVirtualScreen),
                GetSystemMetrics(SystemMetricsCyVirtualScreen)));
        _startupMaximized = placement.IsMaximized;

        return CreateWindowEx(
            0,
            WindowClassName,
            "Augit",
            WsFramelessWindow,
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height,
            0,
            0,
            _instance,
            0);
    }

    /// <summary>
    /// 命中测试（规格：视觉稿自绘标题栏与边框）。
    ///
    /// 判定规则全部在 <see cref="ShellWindowFrame"/> 里并有单元测试：边缘给缩放码，
    /// 其余一律 HtClient——标题栏左侧是主菜单、工作区与分支等真实控件，返回 HTCAPTION
    /// 会让系统在按下瞬间进入移动循环，这些控件永远收不到点击。拖动由网页层发起
    /// （网页知道按在哪个元素上），窗口再用 WM_NCLBUTTONDOWN/HTCAPTION 交给系统，
    /// 因此贴边、双击最大化、从最大化拖出还原都保持原生行为。
    /// </summary>
    private nint HitTest(nint lParam)
    {
        int x = unchecked((short)(long)lParam);
        int y = unchecked((short)((long)lParam >> 16));
        if (!GetWindowRect(_window, out Rect rect))
        {
            return ShellWindowFrame.HtClient;
        }

        return ShellWindowFrame.HitTest(
            x,
            y,
            new WindowFrameRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            _effectiveDpi,
            IsMaximized());
    }

    /// <summary>
    /// 没有非客户区：客户区铺满整个窗口（标题栏与边框都由视觉稿自绘）。
    /// 最大化时把客户区钉回显示器工作区，否则系统给最大化窗口外扩的一圈边框
    /// 会把界面推到屏幕外，标题栏右侧的窗口按钮被裁掉。
    /// </summary>
    private nint AdjustMaximizedClient(nint lParam, nint wParam)
    {
        // 只有 NCCALCSIZE_PARAMS 形式（wParam 为真）才带三个矩形可供改写。
        if (wParam == 0 || lParam == 0 || !IsMaximized() || !TryGetMonitorWorkArea(out WindowFrameRect work))
        {
            return 0;
        }

        NcCalcSizeParams parameters = Marshal.PtrToStructure<NcCalcSizeParams>(lParam);
        WindowFrameRect monitor = new(
            parameters.Window.Left,
            parameters.Window.Top,
            parameters.Window.Right,
            parameters.Window.Bottom);
        WindowFrameRect client = ShellWindowFrame.MaximizedClientRect(monitor, work);
        parameters.Window = new Rect
        {
            Left = client.Left,
            Top = client.Top,
            Right = client.Right,
            Bottom = client.Bottom,
        };
        Marshal.StructureToPtr(parameters, lParam, fDeleteOld: false);
        return 0;
    }

    private bool TryGetMonitorWorkArea(out WindowFrameRect work)
    {
        work = default;
        nint monitor = MonitorFromWindow(_window, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return false;
        }

        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        work = new WindowFrameRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        return true;
    }

    private bool IsMaximized() => _window != 0 && IsZoomed(_window);

    /// <summary>
    /// 窗口命令：视觉稿自绘的最小化/最大化/关闭按钮与标题栏拖动。
    ///
    /// 由窗口自己实现而不是放进 ShellBridge：桥接层只有工作区与 Git 能力，不认识 HWND。
    /// 无 caption 之后这三个按钮是用户唯一能关闭/最小化窗口的入口，必须真的可用。
    /// </summary>
    private object? HandleWindowCommand(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "window/query":
                return WindowState();

            case "window/drag":
                // 不能就地进入移动循环：SendMessage(WM_NCLBUTTONDOWN) 会一直阻塞到松手，
                // 网页层那次 invoke 只能等拖动结束才拿到响应（长拖动直接超时）。
                // 投递到消息队列即可，鼠标此刻仍然按着，移动循环照样从按下点开始。
                StartWindowDragOrToggleMaximize();
                return WindowState();
            case "window/minimize":
                _ = ShowWindow(_window, SwMinimize);
                return WindowState();

            // 网页层报告的边缘缩放：与拖动同路，用 WM_NCLBUTTONDOWN + HT 码交给系统。
            case "window/resize":
                StartWindowResize(parameters);
                return WindowState();

            case "window/maximize":
                bool maximize = !IsMaximized();
                _ = ShowWindow(_window, maximize ? SwShowMaximized : SwRestore);
                // 立即回报目标状态：状态栏与标题栏图标要马上跟着变，
                // 而 WM_SIZE 的实际状态由 NotifyWindowState 在系统调整完成后兜底纠正。
                return new { maximized = maximize, minimized = false };

            case "window/close":
                _ = PostMessage(_window, WmClose, 0, 0);
                return WindowState();

            default:
                throw new BridgeValidationException($"未知的窗口方法：{method}");
        }
    }

    /// <summary>当前窗口状态；网页层用它初始化最大化的图标与悬停说明。</summary>
    private object WindowState() => new { maximized = IsMaximized(), minimized = IsIconic(_window) };

    /// <summary>
    /// 标题栏空白处按下：双击切换最大化，否则开始系统移动循环。
    ///
    /// 窗口没有 caption，输入层不会为标题栏产生 WM_NCLBUTTONDBLCLK，系统的移动循环也拿不到
    /// 双击消息（实测：真实双击标题栏空白处，窗口不会最大化），所以双击必须自己判定。
    /// </summary>
    private void StartWindowDragOrToggleMaximize()
    {
        if (_window == 0)
        {
            return;
        }

        // 与原生 caption 一样带上按下点：移动循环用它计算光标在窗口内的偏移。
        int anchor = 0;
        ShellPoint cursor = default;
        if (GetCursorPos(out cursor))
        {
            anchor = (cursor.Y << 16) | (cursor.X & 0xFFFF);
        }

        long now = Environment.TickCount64;
        bool doubleClick = ShellWindowFrame.IsCaptionDoubleClick(
            _lastDragTick,
            _lastDragPoint.X,
            _lastDragPoint.Y,
            now,
            cursor.X,
            cursor.Y,
            (int)GetDoubleClickTime(),
            GetSystemMetrics(SystemMetricsCxDoubleClick) / 2,
            GetSystemMetrics(SystemMetricsCyDoubleClick) / 2);
        if (doubleClick)
        {
            // 与原生 caption 一致：在第二次按下时就切换最大化/还原，第三次按下重新算单击。
            _lastDragTick = 0;
            _lastDragPoint = default;
            _ = ShowWindow(_window, IsMaximized() ? SwRestore : SwShowMaximized);
            NotifyWindowState();
            return;
        }

        _lastDragTick = now;
        _lastDragPoint = cursor;
        if (!PostMessage(_window, StartDragMessage, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "发起窗口拖动失败。");
        }
    }

    /// <summary>系统移动循环入口（消息队列回调，见 StartWindowDragOrToggleMaximize）。</summary>
    private void BeginPendingDrag()
    {
        if (_window == 0)
        {
            return;
        }

        int anchor = 0;
        if (GetCursorPos(out ShellPoint cursor))
        {
            anchor = (cursor.Y << 16) | (cursor.X & 0xFFFF);
        }

        _ = ReleaseCapture();
        _ = SendMessage(_window, WmNclButtonDown, HtCaption, anchor);
    }

    /// <summary>
    /// 网页层报告的边缘按下：记录边后投递，由系统按原生缩放循环处理。
    ///
    /// 同样不能就地 SendMessage：缩放循环会阻塞到松手，网页层那次 invoke 得等到拖动结束。
    /// </summary>
    private void StartWindowResize(JsonElement parameters)
    {
        if (_window == 0 || IsMaximized())
        {
            // 最大化时不提供边缘缩放（与命中测试同一规则）。
            return;
        }

        string? edge = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("edge", out JsonElement element)
            && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        int code = ShellWindowFrame.ResizeCode(edge);
        if (code == 0)
        {
            return;
        }

        _pendingResizeCode = code;
        if (!PostMessage(_window, StartResizeMessage, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "发起窗口缩放失败。");
        }
    }

    /// <summary>系统缩放循环入口（消息队列回调，见 StartWindowResize）。</summary>
    private void BeginPendingResize()
    {
        int code = _pendingResizeCode;
        _pendingResizeCode = 0;
        if (_window == 0 || code == 0)
        {
            return;
        }

        int anchor = 0;
        if (GetCursorPos(out ShellPoint cursor))
        {
            anchor = (cursor.Y << 16) | (cursor.X & 0xFFFF);
        }

        _ = ReleaseCapture();
        _ = SendMessage(_window, WmNclButtonDown, code, anchor);
    }

    /// <summary>窗口状态变化时通知网页层（用户用快捷键、双击、贴边或拖拽改变状态时也要同步）。</summary>
    private void NotifyWindowState()
    {
        bool maximized = IsMaximized();
        if (_reportedMaximized == maximized)
        {
            return;
        }

        _reportedMaximized = maximized;
        Notify("window/state", new { maximized, minimized = IsIconic(_window) });
    }

    private int WorkWidth => _hasWorkArea ? _workArea.Right - _workArea.Left : 0;

    private int WorkHeight => _hasWorkArea ? _workArea.Bottom - _workArea.Top : 0;

    private static nint OnWindowMessage(nint window, uint message, nint wParam, nint lParam)
    {
        if (!LiveWindows.TryGetValue(window, out ShellWindow? shell))
        {
            return DefWindowProc(window, message, wParam, lParam);
        }

        switch (message)
        {
            // 没有非客户区：客户区铺满整个窗口（标题栏与边框都由视觉稿自绘）。
            case WmNcCalcSize:
                return shell.AdjustMaximizedClient(lParam, wParam);

            // 拖动与边缘缩放：窗口没有 caption 后，这些必须由窗口自己判定，
            // 否则标题栏拖不动、边框也拉不动。
            case WmNcHitTest:
                return shell.HitTest(lParam);

            // 网页层在标题栏空白处按下后投递过来：此时才进入系统移动循环。
            case StartDragMessage:
                shell.BeginPendingDrag();
                return 0;

            // 网页层在窗口边缘按下后投递过来：此时才进入系统缩放循环。
            case StartResizeMessage:
                shell.BeginPendingResize();
                return 0;

            case InitializeMessage:
                _ = shell.InitializeWebViewAsync();
                return 0;
            case ReplyMessage:
                shell.PostBridgeReplies();
                return 0;
            case WmSize:
                shell.SyncBounds();
                shell.NotifyWindowState();
                return 0;
            case WmDpichanged:
                shell.SyncBounds();
                return shell.OnDpiChanged(message, wParam, lParam);
            case WmGetMinMaxInfo:
                shell.ApplyMinimumSize(lParam);
                return 0;
            case WmClose:
                shell.SavePlacement();
                DestroyWindow(window);
                return 0;
            case WmDestroy:
                LiveWindows.Remove(window);
                shell._bridge.Dispose();
                shell._window = 0;
                if (_messageLoopRunning && LiveWindows.Count == 0)
                {
                    PostQuitMessage(0);
                }

                return 0;
            default:
                return DefWindowProc(window, message, wParam, lParam);
        }
    }

    /// <summary>
    /// WebView2 的原生加载器随包分发在 <c>native</c> 目录，不在应用根目录，
    /// 必须先显式加载，否则 CoreWebView2Environment.CreateAsync 会抛 DllNotFoundException。
    /// </summary>
    private static void EnsureLoaderLoaded()
    {
        lock (LoaderGate)
        {
            if (_loaderModule != 0)
            {
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "native", "WebView2Loader.dll");
            _loaderModule = LoadLibrary(path);
            if (_loaderModule == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"加载 WebView2 原生加载器失败：{path}");
            }
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            EnsureLoaderLoaded();
            string shellDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Augit",
                "WebView2",
                "Shell");
            // 固定会话目录：早期实现按进程号建目录，每次运行都新增一份，
            // 长期使用会堆积成 GB 级的残留（实测 115 个目录 / 1.4 GB）。
            string userDataFolder = Path.Combine(shellDataRoot, "Session");
            CleanStaleSessions(shellDataRoot, userDataFolder);
            CoreWebView2EnvironmentOptions environmentOptions = new();
            if (_options.BrowserArguments is { Length: > 0 } browserArguments)
            {
                // 诊断用：直接传入 Chromium 参数，用于对比渲染与内存行为。
                // 注意：实测 WebView2 会覆盖部分 Chromium 开关（例如 --disable-gpu
                // 传入后 GPU 进程仍然存在），因此该入口只适合排查，不保证生效。
                environmentOptions.AdditionalBrowserArguments = browserArguments;
            }

            _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, environmentOptions);
            _controller = await _environment.CreateCoreWebView2ControllerAsync(_window);
            if (_disposed)
            {
                _controller?.Close();
                return;
            }

            CoreWebView2 webView = _controller.CoreWebView2;
            webView.Settings.AreDefaultContextMenusEnabled = false;
            webView.Settings.IsStatusBarEnabled = false;
            webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
            webView.Settings.AreDevToolsEnabled = true;
            await webView.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__augitErrors=[];"
                + "addEventListener('error',e=>window.__augitErrors.push('error:'+(e.message||'')+' @'+(e.filename||'')+':'+(e.lineno||0)));"
                + "addEventListener('unhandledrejection',e=>window.__augitErrors.push('reject:'+String(e.reason&&e.reason.message||e.reason)));");
            webView.SetVirtualHostNameToFolderMapping(
                DefaultVirtualHost,
                _options.WebRoot,
                CoreWebView2HostResourceAccessKind.Allow);
            webView.NavigationCompleted += OnNavigationCompleted;
            webView.ProcessFailed += OnProcessFailed;
            webView.WebMessageReceived += OnWebMessageReceived;
            // 网页标题同步到窗口标题，便于任务栏与外部工具识别当前场景。
            webView.DocumentTitleChanged += OnDocumentTitleChanged;
            ApplyRasterizationScale();
            SyncBounds();
            webView.Navigate($"https://{DefaultVirtualHost}/index.html{BuildQuery()}");
        }
        catch (Exception exception)
        {
            _ = MessageBox(_window, exception.ToString(), "Augit 界面加载失败", 0x00000010);
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {

        if (eventArgs.IsSuccess || _disposed)
        {
            return;
        }

        _ = MessageBox(
            _window,
            $"界面资源加载失败：{eventArgs.WebErrorStatus}\n资源目录：{_options.WebRoot}",
            "Augit",
            0x00000010);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        _ = MessageBox(
            _window,
            $"界面渲染进程异常退出：{eventArgs.ProcessFailedKind}（{eventArgs.Reason}）",
            "Augit",
            0x00000010);
    }

    private string BuildQuery()
    {
        List<string> parts = [];
        if (_options.Scene is { Length: > 0 } scene)
        {
            parts.Add($"scene={Uri.EscapeDataString(scene)}");
        }

        if (_options.Theme is { Length: > 0 } theme)
        {
            parts.Add($"theme={Uri.EscapeDataString(theme)}");
        }

        if (_options.OpenDocument is { Length: > 0 } document)
        {
            parts.Add($"open={Uri.EscapeDataString(document)}");
        }

        if (_options.BlameDocument is { Length: > 0 } blame)
        {
            parts.Add($"blame={Uri.EscapeDataString(blame)}");
        }

        if (_options.FileHistoryDocument is { Length: > 0 } fileHistory)
        {
            parts.Add($"file-history={Uri.EscapeDataString(fileHistory)}");
        }

        if (_options.ConflictDocument is { Length: > 0 } conflict)
        {
            parts.Add($"conflict={Uri.EscapeDataString(conflict)}");
        }

        if (_options.DiffDocument is { Length: > 0 } diff)
        {
            parts.Add($"diff={Uri.EscapeDataString(diff)}");
        }

        if (_options.NoSessionRestore)
        {
            parts.Add("no-session-restore=1");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    /// <summary>
    /// 像素对照模式：把 WebView2 的栅格化比例固定为 1，使一个 CSS 像素对应一个物理像素，
    /// 从而能与 HTML 视觉稿的截图逐像素比较。正常运行保持跟随显示器缩放。
    /// </summary>
    /// <summary>
    /// 处理网页层请求。
    /// 关键约束：桥接方法可能是异步的，await 之后延续不一定回到 UI 线程；
    /// 而 PostWebMessageAsJson 只能在 UI 线程调用，否则抛
    /// "CoreWebView2 members can only be accessed from the UI thread"。
    /// 因此响应经自定义窗口消息投回 UI 线程后再发送。
    /// </summary>
    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (sender is not CoreWebView2 || _disposed)
        {
            return;
        }

        string response = await _bridge.HandleAsync(eventArgs.WebMessageAsJson, CancellationToken.None);
        if (_disposed || _window == 0)
        {
            return;
        }

        // 响应字符串放在托管队列里，只用消息做唤醒，避免跨线程封送字符串。
        lock (_pendingReplies)
        {
            _pendingReplies.Enqueue(response);
        }

        _ = PostMessage(_window, ReplyMessage, 0, 0);
    }

    /// <summary>
    /// 向网页层推送一个事件。复用回复队列：事件不带 id，
    /// 网页层按 id 匹配请求，因此两者不会互相干扰。
    /// 可从任意线程调用。
    /// </summary>
    internal void Notify(string kind, object payload)
    {
        if (_disposed || _window == 0)
        {
            return;
        }

        string json = JsonSerializer.Serialize(new { @event = kind, payload });
        lock (_pendingReplies)
        {
            _pendingReplies.Enqueue(json);
        }

        _ = PostMessage(_window, ReplyMessage, 0, 0);
    }

    /// <summary>在 UI 线程上把桥接响应发回网页层。</summary>
    private void PostBridgeReplies()
    {
        while (true)
        {
            string response;
            lock (_pendingReplies)
            {
                if (_pendingReplies.Count == 0)
                {
                    return;
                }

                response = _pendingReplies.Dequeue();
            }

            if (_disposed || _controller?.CoreWebView2 is not { } webView)
            {
                return;
            }

            webView.PostWebMessageAsJson(response);
        }
    }

    /// <summary>把网页标题同步到窗口标题，便于任务栏识别当前场景。</summary>
    private void OnDocumentTitleChanged(object? sender, object eventArgs)
    {
        // 该事件的参数类型没有公开的强类型成员，只能按属性名取值。
        string? title = eventArgs?.GetType().GetProperty("Title")?.GetValue(eventArgs) as string;
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        _ = SetWindowText(_window, title);
    }

    /// <summary>
    /// 设定 WebView2 的栅格化比例。
    /// 审计用 --dpi 覆盖逻辑 DPI 时把比例固定为 dpi/96，使界面按真实字宽字高排布；
    /// --pixel-exact 则固定为 1，用于一个 CSS 像素对应一个物理像素的对照。
    /// 两者都关闭监视器缩放跟随，避免外接显示器时比例被改写。
    /// 该覆盖只作用于本进程，不修改注册表或系统显示设置。
    /// </summary>
    private void ApplyRasterizationScale()
    {
        if (_controller is null)
        {
            return;
        }

        if (_options.Dpi is { } dpi)
        {
            _controller.ShouldDetectMonitorScaleChanges = false;
            _controller.RasterizationScale = dpi / 96.0;
            return;
        }

        if (_options.PixelExact)
        {
            _controller.ShouldDetectMonitorScaleChanges = false;
            _controller.RasterizationScale = 1.0;
        }
    }

    /// <summary>
    /// 限制窗口最小尺寸，避免拖到布局容不下的尺寸。
    /// MinTrackSize 使用物理像素，因此按生效 DPI（显式覆盖或系统 DPI）换算。
    /// </summary>
    private void ApplyMinimumSize(nint lParam)
    {
        if (lParam == 0)
        {
            return;
        }

        MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        PhysicalSize minimum = ShellWindowSizing.MinimumWindow(_effectiveDpi, WorkWidth, WorkHeight);
        info.MinTrackSize = new ShellPoint
        {
            X = minimum.Width,
            Y = minimum.Height,
        };
        Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
    }

    /// <summary>
    /// 把当前窗口摆放写回设置（§6.6「已恢复窗口尺寸不得被默认值覆盖」）。
    /// 物理像素换算成逻辑单位保存，显示器 DPI 变化后尺寸仍然正确；
    /// 只取还原矩形，最大化/最小化状态单独记录，还原时才能得到正确尺寸。
    /// 审计运行（显式场景、DPI 或尺寸）不写设置，避免污染用户窗口。
    /// </summary>
    private void SavePlacement()
    {
        if (!_persistPlacement || _window == 0)
        {
            return;
        }

        WindowPlacement placement = new() { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(_window, ref placement))
        {
            return;
        }

        Rect bounds = placement.NormalPosition;
        int physicalWidth = bounds.Right - bounds.Left;
        int physicalHeight = bounds.Bottom - bounds.Top;
        // 尺寸装不下布局时不写入：一次异常的小窗口不应该毁掉上一次保存的可用尺寸
        // （退役的旧原生界面也是这个行为）。
        PhysicalSize minimum = ShellWindowSizing.MinimumWindow(_effectiveDpi, WorkWidth, WorkHeight);
        if (physicalWidth < minimum.Width || physicalHeight < minimum.Height)
        {
            return;
        }

        WindowPlacementSettings saved = new()
        {
            Left = ShellWindowSizing.ToLogical(bounds.Left, _effectiveDpi),
            Top = ShellWindowSizing.ToLogical(bounds.Top, _effectiveDpi),
            Width = ShellWindowSizing.ToLogical(physicalWidth, _effectiveDpi),
            Height = ShellWindowSizing.ToLogical(physicalHeight, _effectiveDpi),
            IsMaximized = placement.ShowCommand == SwShowMaximized,
        };

        try
        {
            // 读改写：设置文件同时存主题、最近工作区、打开的文件，不能整体覆盖。
            SettingsStore store = new();
            ApplicationSettings current = store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            store.SaveAsync(current with { Window = saved }, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 关闭阶段任何写盘失败都不影响退出（这里是窗口过程，抛出去会在退出时弹错误框），
            // 下次启动沿用上一次保存的位置。
        }
    }

    /// <summary>
    /// 显示器 DPI 变化时更新生效 DPI。显式 <c>--dpi</c> 与 <c>--pixel-exact</c> 已经把
    /// WebView2 的栅格化比例固定住并关闭了监视器缩放跟随，此时不得改写。
    /// 同时交给默认过程按系统建议的矩形调整窗口，否则换到更高 DPI 的显示器后
    /// 逻辑视口会再次小于布局下限。
    /// </summary>
    private nint OnDpiChanged(uint message, nint wParam, nint lParam)
    {
        if (_options.Dpi is null && !_options.PixelExact && _window != 0)
        {
            int dpi = (int)GetDpiForWindow(_window);
            if (dpi > 0)
            {
                _effectiveDpi = dpi;
            }
        }

        return DefWindowProc(_window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public ShellPoint Reserved;
        public ShellPoint MaxSize;
        public ShellPoint MaxPosition;
        public ShellPoint MinTrackSize;
        public ShellPoint MaxTrackSize;
    }

    // NCCALCSIZE_PARAMS：前三个矩形依次是「建议的新窗口矩形」「变化前的窗口矩形」「变化前的客户区矩形」。
    [StructLayout(LayoutKind.Sequential)]
    private struct NcCalcSizeParams
    {
        public Rect Window;
        public Rect Before;
        public Rect After;
        public ShellPoint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out ShellPoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private void SyncBounds()
    {
        if (_controller is null || _window == 0)
        {
            return;
        }

        if (!GetClientRect(_window, out Rect bounds))
        {
            return;
        }

        _controller.Bounds = new System.Drawing.Rectangle(
            0,
            0,
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out NativeMessage message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref Rect value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowTextW")]
    private static extern bool SetWindowText(nint window, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadCursorW")]
    private static extern nint LoadCursor(nint instance, int cursor);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW", SetLastError = true)]
    private static extern nint LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint window, string text, string caption, uint type);

    // WINDOWPLACEMENT 必须带 Length 字段，否则 GetWindowPlacement 返回失败。
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCommand;
        public ShellPoint MinimumPosition;
        public ShellPoint MaximumPosition;
        public Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    // WNDCLASSEXW 的字段顺序必须完整（含末尾的 hIconSm），否则 RegisterClassExW 返回
    // ERROR_INVALID_PARAMETER。x64 上该结构为 80 字节。
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);
}
