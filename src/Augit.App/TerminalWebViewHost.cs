using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Augit.Infrastructure.Settings;
using Microsoft.Web.WebView2.Core;

namespace Augit.App;

internal sealed class TerminalWebViewHost : IDisposable
{
    private const string VirtualHostName = "augit-terminal.local";
    private static readonly object LoaderGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static nint _loaderModule;
    private readonly Action<string> _inputReceived;
    private readonly Action<int, int> _sizeChanged;
    private readonly TaskCompletionSource<(int Columns, int Rows)> _readyCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private string? _userDataFolder;
    private bool _allowInitialNavigation;
    private bool _disposed;
    private bool _initializing;
    private bool _visible = true;
    private Rectangle _bounds;
    private int _browserProcessId;

    internal TerminalWebViewHost(
        nint parent,
        Action<string> inputReceived,
        Action<int, int> sizeChanged)
    {
        ArgumentNullException.ThrowIfNull(inputReceived);
        ArgumentNullException.ThrowIfNull(sizeChanged);
        _inputReceived = inputReceived;
        _sizeChanged = sizeChanged;
        Handle = NativeMethods.CreateWindow(
            0,
            NativeMethods.StaticClass,
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleClipChildren
                | NativeMethods.WindowStyleClipSiblings,
            0,
            0,
            0,
            0,
            parent,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.TerminalHostCreateFailed);
        }
    }

    internal nint Handle { get; private set; }

    internal int BrowserProcessId => _browserProcessId;

    internal (int Columns, int Rows) InitialSize { get; private set; } = (80, 24);

    internal static string CreateConfigurationMessage(ApplicationSettings settings)
    {
        TerminalAppearance appearance = CreateTerminalAppearance(NativeTheme.IsDark(settings.Theme));
        return JsonSerializer.Serialize(new
        {
            type = "configure",
            fontFamily = $"'{NativeFontResolver.ResolveMonospace(settings.MonospaceFontFamily).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}', Consolas, monospace",
            fontSize = Math.Clamp(settings.FontSize, 9, 40),
            lineHeight = 1.7,
            theme = appearance.Theme,
        }, JsonOptions);
    }

    internal static (string Background, string Foreground, string Cursor, string SelectionBackground, string ScrollbarThumb, string ScrollbarThumbHover)
        TerminalColorsForTest(bool dark)
    {
        TerminalTheme theme = CreateTerminalAppearance(dark).Theme;
        return (theme.Background, theme.Foreground, theme.Cursor, theme.SelectionBackground, theme.ScrollbarThumb, theme.ScrollbarThumbHover);
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        if (_disposed)
        {
            return;
        }

        int safeWidth = Math.Max(0, width);
        int safeHeight = Math.Max(0, height);
        _bounds = new Rectangle(x, y, safeWidth, safeHeight);
        _ = NativeMethods.MoveWindow(Handle, x, y, safeWidth, safeHeight, true);
        if (_controller is not null)
        {
            _controller.Bounds = new Rectangle(0, 0, safeWidth, safeHeight);
            _controller.NotifyParentWindowPositionChanged();
            // 只有原生布局给出实际可见尺寸后，网页才允许调整行列，避免首屏提示符被微小初始视口截断。
            _webView?.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "layout",
                visible = safeWidth > 0 && safeHeight > 0,
            }));
        }
    }

    internal void SetVisible(bool visible)
    {
        if (_disposed)
        {
            return;
        }

        _visible = visible;
        _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        if (_controller is not null)
        {
            _controller.IsVisible = visible;
        }
    }

    internal void Write(string output)
    {
        if (_disposed || _webView is null || output.Length == 0)
        {
            return;
        }

        _webView.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "output", data = output }));
    }

    internal void ApplyAppearance(ApplicationSettings settings)
    {
        if (_disposed || _webView is null)
        {
            return;
        }

        TerminalAppearance appearance = CreateTerminalAppearance(NativeTheme.IsDark(settings.Theme));
        if (_controller is not null)
        {
            _controller.DefaultBackgroundColor = ToDrawingColor(appearance.BackgroundColor);
        }

        _webView.PostWebMessageAsJson(CreateConfigurationMessage(settings));
    }

    internal void Focus()
    {
        if (_disposed || _controller is null || _webView is null)
        {
            return;
        }

        _controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        _webView.PostWebMessageAsJson("{\"type\":\"focus\"}");
    }

    internal bool ContainsWindow(nint window)
    {
        return !_disposed
            && Handle != 0
            && (window == Handle || NativeMethods.IsChild(Handle, window));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _readyCompletion.TrySetCanceled();
        // WebView2 的环境/控制器创建不能中途取消；持有对象到回调完成后再清理，不能丢弃晚到资源。
        if (!_initializing) ReleaseResources();
    }

    private void ReleaseResources()
    {
        _allowInitialNavigation = false;
        int browserProcessId = BrowserProcessId;
        if (_webView is not null)
        {
            _webView.WebMessageReceived -= OnWebMessageReceived;
            _webView.NavigationStarting -= OnNavigationStarting;
            _webView.NewWindowRequested -= OnNewWindowRequested;
            try
            {
                _webView.Stop();
            }
            catch (InvalidOperationException)
            {
            }
        }

        _controller?.Close();
        _webView = null;
        _controller = null;
        _environment = null;
        StopBrowserProcess(browserProcessId);
        _browserProcessId = 0;
        DeleteUserDataFolder();
        nint handle = Handle;
        Handle = 0;
        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    internal async Task InitializeAsync(ApplicationSettings settings, Func<string, Task>? checkpoint = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _initializing = true;
        try
        {
            await InitializeCoreAsync(settings, checkpoint);
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            _initializing = false;
            if (_disposed) ReleaseResources();
        }
    }

    private async Task InitializeCoreAsync(ApplicationSettings settings, Func<string, Task>? checkpoint)
    {
        string assetsDirectory = Path.Combine(AppContext.BaseDirectory, "terminal");
        if (!File.Exists(Path.Combine(assetsDirectory, "index.html"))
            || !File.Exists(Path.Combine(assetsDirectory, "xterm.js"))
            || !File.Exists(Path.Combine(assetsDirectory, "xterm.css"))
            || !File.Exists(Path.Combine(assetsDirectory, "addon-fit.js"))
            || !File.Exists(Path.Combine(assetsDirectory, "terminal.js")))
        {
            throw new FileNotFoundException(UiText.TerminalAssetsMissing);
        }

        EnsureLoaderLoaded();
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Augit",
            "WebView2",
            "Terminal",
            $"Session-{Environment.ProcessId}-{Guid.NewGuid():N}");
        _environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
        _browserProcessId = _environment.GetProcessInfos()
            .FirstOrDefault(process => process.Kind == CoreWebView2ProcessKind.Browser)?.ProcessId ?? 0;
        if (checkpoint is not null) await checkpoint("environment");
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller = await _environment.CreateCoreWebView2ControllerAsync(Handle);
        _webView = _controller.CoreWebView2;
        _browserProcessId = checked((int)_webView.BrowserProcessId);
        if (checkpoint is not null) await checkpoint("controller");
        ObjectDisposedException.ThrowIf(_disposed, this);
        ApplyVisualAuditRasterizationScale(_controller);
        _controller.IsVisible = _visible;
        _controller.Bounds = new Rectangle(0, 0, _bounds.Width, _bounds.Height);
        _webView.Settings.AreDefaultContextMenusEnabled = false;
        _webView.Settings.AreDevToolsEnabled = false;
        _webView.Settings.IsStatusBarEnabled = false;
        _webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
        _webView.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            assetsDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
        _webView.WebMessageReceived += OnWebMessageReceived;
        _webView.NavigationStarting += OnNavigationStarting;
        _webView.NewWindowRequested += OnNewWindowRequested;
        TerminalAppearance appearance = CreateTerminalAppearance(NativeTheme.IsDark(settings.Theme));
        _controller.DefaultBackgroundColor = ToDrawingColor(appearance.BackgroundColor);
        await _webView.AddScriptToExecuteOnDocumentCreatedAsync(
            $"window.__augitTerminalConfiguration = {CreateConfigurationMessage(settings)};");
        if (checkpoint is not null) await checkpoint("document");
        ObjectDisposedException.ThrowIf(_disposed, this);
        _allowInitialNavigation = true;
        _webView.Navigate($"https://{VirtualHostName}/index.html");
        InitialSize = await _readyCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetBounds(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height);
        ApplyAppearance(settings);
    }

    private static string ToCssColor(uint color)
    {
        byte red = (byte)(color & 0xFF);
        byte green = (byte)((color >> 8) & 0xFF);
        byte blue = (byte)((color >> 16) & 0xFF);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static Color ToDrawingColor(uint color)
    {
        return Color.FromArgb(
            (int)(color & 0xFF),
            (int)((color >> 8) & 0xFF),
            (int)((color >> 16) & 0xFF));
    }

    private static TerminalAppearance CreateTerminalAppearance(bool dark)
    {
        NativeThemePalette palette = NativeTheme.Palette(dark);
        return new(
            palette.Panel,
            new(
                ToCssColor(palette.Panel),
                ToCssColor(palette.Text),
                ToCssColor(palette.Text),
                ToCssColor(palette.AccentSoft),
                ToCssColor(palette.Faint),
                ToCssColor(palette.Muted)));
    }

    private static void EnsureLoaderLoaded()
    {
        lock (LoaderGate)
        {
            if (_loaderModule != 0)
            {
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "native", "WebView2Loader.dll");
            _loaderModule = NativeMethods.LoadLibrary(path);
            if (_loaderModule == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.WebViewLoaderMissing);
            }
        }
    }

    private static void ApplyVisualAuditRasterizationScale(CoreWebView2Controller controller)
    {
        if (!NativeTheme.VisualAuditDpiOverrideActiveForTest)
        {
            return;
        }

        // 终端视觉审计与原生工具窗口使用同一目标 DPI，正常运行仍由 WebView2 跟随显示器。
        controller.ShouldDetectMonitorScaleChanges = false;
        controller.RasterizationScale = NativeTheme.EmbeddedContentRasterizationScaleForTest;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            using JsonDocument message = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            JsonElement root = message.RootElement;
            if (!root.TryGetProperty("type", out JsonElement typeElement))
            {
                return;
            }

            string? type = typeElement.GetString();
            if (type == "input"
                && root.TryGetProperty("data", out JsonElement dataElement)
                && dataElement.GetString() is string data
                && data.Length <= 1024 * 1024)
            {
                _inputReceived(data);
                return;
            }

            if (type is "ready" or "resize"
                && TryReadSize(root, out int columns, out int rows))
            {
                if (type == "ready")
                {
                    _readyCompletion.TrySetResult((columns, rows));
                }

                _sizeChanged(columns, rows);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static bool TryReadSize(JsonElement root, out int columns, out int rows)
    {
        columns = 0;
        rows = 0;
        return root.TryGetProperty("columns", out JsonElement columnsElement)
            && root.TryGetProperty("rows", out JsonElement rowsElement)
            && columnsElement.TryGetInt32(out columns)
            && rowsElement.TryGetInt32(out rows)
            && columns is >= 2 and <= 500
            && rows is >= 1 and <= 300;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (_allowInitialNavigation
            && eventArgs.Uri.Equals($"https://{VirtualHostName}/index.html", StringComparison.OrdinalIgnoreCase))
        {
            _allowInitialNavigation = false;
            return;
        }

        if (eventArgs.Uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _allowInitialNavigation = false;
        eventArgs.Cancel = true;
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
    }

    private static void StopBrowserProcess(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.WaitForExit(500))
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(5000);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
        }
    }

    private void DeleteUserDataFolder()
    {
        string? path = _userDataFolder;
        _userDataFolder = null;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 19)
                {
                    return;
                }

                Thread.Sleep(50);
            }
        }
    }
}

internal sealed record TerminalAppearance(uint BackgroundColor, TerminalTheme Theme);

internal sealed record TerminalTheme(
    string Background,
    string Foreground,
    string Cursor,
    string SelectionBackground,
    string ScrollbarThumb,
    string ScrollbarThumbHover);
