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

    private TerminalWebViewHost(
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

    internal int BrowserProcessId => checked((int)(_webView?.BrowserProcessId ?? 0));

    internal (int Columns, int Rows) InitialSize { get; private set; } = (80, 24);

    internal static async Task<TerminalWebViewHost> CreateAsync(
        nint parent,
        ApplicationSettings settings,
        Action<string> inputReceived,
        Action<int, int> sizeChanged)
    {
        TerminalWebViewHost host = new(parent, inputReceived, sizeChanged);
        try
        {
            await host.InitializeAsync(settings);
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        if (_disposed)
        {
            return;
        }

        int safeWidth = Math.Max(0, width);
        int safeHeight = Math.Max(0, height);
        _ = NativeMethods.MoveWindow(Handle, x, y, safeWidth, safeHeight, true);
        if (_controller is not null)
        {
            _controller.Bounds = new Rectangle(0, 0, safeWidth, safeHeight);
            _controller.NotifyParentWindowPositionChanged();
        }
    }

    internal void SetVisible(bool visible)
    {
        if (_disposed)
        {
            return;
        }

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

        bool dark = NativeTheme.IsDark(settings.Theme);
        object theme = dark
            ? new
            {
                background = "#1e1f22",
                foreground = "#d8d8d8",
                cursor = "#d8d8d8",
                selectionBackground = "#355a7a",
            }
            : new
            {
                background = "#ffffff",
                foreground = "#202124",
                cursor = "#202124",
                selectionBackground = "#b7d8f5",
            };
        _webView.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "configure",
            fontFamily = $"{settings.MonospaceFontFamily}, Consolas, monospace",
            fontSize = Math.Clamp(settings.FontSize, 9, 40),
            theme,
        }));
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
        DeleteUserDataFolder();
        nint handle = Handle;
        Handle = 0;
        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    private async Task InitializeAsync(ApplicationSettings settings)
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
        _controller = await _environment.CreateCoreWebView2ControllerAsync(Handle);
        if (_disposed)
        {
            return;
        }

        _webView = _controller.CoreWebView2;
        _controller.IsVisible = true;
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
        _allowInitialNavigation = true;
        _webView.Navigate($"https://{VirtualHostName}/index.html");
        InitialSize = await _readyCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        ApplyAppearance(settings);
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
