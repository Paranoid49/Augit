using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Augit.Shell;

/// <summary>
/// 原生 Win32 外壳窗口：只负责窗口框架、尺寸与 WebView2 控件的承载，界面内容全部由网页层渲染。
/// </summary>
internal sealed class ShellWindow : IDisposable
{
    private const string WindowClassName = "Augit.Shell.Window";
    private const string DefaultVirtualHost = "augit.local";
    private const int WsOverlappedWindow = 0x00CF0000;
    private const int SwShow = 5;
    private const uint WmSize = 0x0005;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmDpichanged = 0x02E0;
    private const int IdiApplication = 32512;
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
    private readonly nint _instance;
    private nint _window;
    private readonly ShellBridge _bridge;
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private bool _disposed;
    private readonly Queue<string> _pendingReplies = new();

    public ShellWindow(ShellOptions options)
    {
        _options = options;
        _bridge = new ShellBridge(options.WorkspaceRoot);
        _instance = GetModuleHandle(null);
        RegisterWindowClass();
        _window = CreateWindowInstance();
        if (_window == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建外壳窗口失败。");
        }

        LiveWindows[_window] = this;
        // WebView2 的控件创建必须在消息循环开始之后进行：CreateCoreWebView2ControllerAsync
        // 依赖 Shell 嵌入式浏览器在 UI 线程上泵消息，若在 Main 里等待会直接死锁。
        if (!PostMessage(_window, InitializeMessage, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "投递界面初始化消息失败。");
        }
    }

    public void Show()
    {
        ShowWindow(_window, SwShow);
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
        return CreateWindowEx(
            0,
            WindowClassName,
            "Augit",
            WsOverlappedWindow,
            80,
            80,
            _options.Width ?? 1180,
            _options.Height ?? 760,
            0,
            0,
            _instance,
            0);
    }

    private static nint OnWindowMessage(nint window, uint message, nint wParam, nint lParam)
    {
        if (!LiveWindows.TryGetValue(window, out ShellWindow? shell))
        {
            return DefWindowProc(window, message, wParam, lParam);
        }

        switch (message)
        {
            case InitializeMessage:
                _ = shell.InitializeWebViewAsync();
                return 0;
            case ReplyMessage:
                shell.PostBridgeReplies();
                return 0;
            case WmSize:
            case WmDpichanged:
                shell.SyncBounds();
                return 0;
            case WmClose:
                DestroyWindow(window);
                return 0;
            case WmDestroy:
                LiveWindows.Remove(window);
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
            _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
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

    private void ApplyRasterizationScale()
    {
        if (_controller is null)
        {
            return;
        }

        if (_options.PixelExact)
        {
            _controller.ShouldDetectMonitorScaleChanges = false;
            _controller.RasterizationScale = 1.0;
        }
    }

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

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadCursorW")]
    private static extern nint LoadCursor(nint instance, int cursor);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW", SetLastError = true)]
    private static extern nint LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint window, string text, string caption, uint type);

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
