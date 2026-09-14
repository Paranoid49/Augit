using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Interop;
using Microsoft.Web.WebView2.Core;

namespace Augit.App;

internal sealed partial class MarkdownWebViewHost : IDisposable
{
    private static readonly object LoaderGate = new();
    private static readonly object BrowserProcessGate = new();
    private static readonly SemaphoreSlim BrowserCreationGate = new(1, 1);
    private static readonly Dictionary<int, int> BrowserProcessReferences = [];
    private static nint _loaderModule;
    private readonly Action<string, string?> _openLinkedFile;
    private readonly Action<string> _setStatus;
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private string? _userDataFolder;
    private int _browserProcessId;
    private bool _allowInitialNavigation;
    private bool _disposed;

    private MarkdownWebViewHost(nint parent, Action<string, string?> openLinkedFile, Action<string> setStatus)
    {
        ArgumentNullException.ThrowIfNull(openLinkedFile);
        ArgumentNullException.ThrowIfNull(setStatus);
        _openLinkedFile = openLinkedFile;
        _setStatus = setStatus;
        Handle = NativeMethods.CreateWindow(
            0,
            NativeMethods.StaticClass,
            string.Empty,
            NativeMethods.WindowStyleChild
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.MarkdownHostCreateFailed);
        }
    }

    internal nint Handle { get; private set; }

    internal int BrowserProcessId => _browserProcessId;

    internal static async Task<MarkdownWebViewHost> CreateAsync(
        nint parent,
        string html,
        Action<string, string?> openLinkedFile,
        Action<string> setStatus,
        Action<CoreWebView2, CoreWebView2Environment>? configureForTest = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        await BrowserCreationGate.WaitAsync(cancellationToken);
        try
        {
            MarkdownWebViewHost host = new(parent, openLinkedFile, setStatus);
            try
            {
                await host.InitializeAsync(html, configureForTest, cancellationToken);
                return host;
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }
        finally
        {
            BrowserCreationGate.Release();
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

    internal async Task NavigateToAnchorAsync(string anchor)
    {
        if (_webView is null || string.IsNullOrWhiteSpace(anchor))
        {
            return;
        }

        string escaped = System.Text.Json.JsonSerializer.Serialize(anchor);
        _ = await _webView.ExecuteScriptAsync($"location.hash = {escaped};");
    }

    internal async Task UpdateContentAsync(string html, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed || _webView is null, this);
        // 仅替换自行生成的受控正文与样式，不导航、不注册宿主对象，保留浏览器和阅读位置。
        string encoded = System.Text.Json.JsonSerializer.Serialize(html);
        _ = await _webView.ExecuteScriptAsync($$"""
            (() => {
              const x = scrollX, y = scrollY;
              const page = new DOMParser().parseFromString({{encoded}}, 'text/html');
              document.head.querySelector('style').textContent = page.head.querySelector('style').textContent;
              document.body.replaceChildren(...page.body.childNodes);
              window.augitRefreshImages?.();
              scrollTo(x, y);
            })();
            """);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal Task<string> ReadPageForTestAsync(string expression) =>
        _webView?.ExecuteScriptAsync(expression) ?? Task.FromResult("null");

    internal string? UserDataFolderForTest => _userDataFolder;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _allowInitialNavigation = false;
        int browserProcessId = _browserProcessId;
        _browserProcessId = 0;
        if (_webView is not null)
        {
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
        ReleaseBrowserProcess(browserProcessId);
        DeleteUserDataFolder();
        nint handle = Handle;
        Handle = 0;
        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    internal static bool ShouldAllowNavigation(string uri, bool initialNavigation)
    {
        if (initialNavigation)
        {
            return true;
        }

        return Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) && parsed.Scheme == "about";
    }

    private async Task InitializeAsync(string html,
        Action<CoreWebView2, CoreWebView2Environment>? configureForTest, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLoaderLoaded();
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Augit",
            "WebView2",
            "Markdown",
            $"Session-{Environment.ProcessId}-{Guid.NewGuid():N}");
        _environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
        cancellationToken.ThrowIfCancellationRequested();
        _controller = await _environment.CreateCoreWebView2ControllerAsync(Handle);
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        _webView = _controller.CoreWebView2;
        ApplyVisualAuditRasterizationScale(_controller);
        _browserProcessId = checked((int)_webView.BrowserProcessId);
        RegisterBrowserProcess(_browserProcessId);
        _controller.IsVisible = false;
        _webView.Settings.AreDefaultContextMenusEnabled = false;
        _webView.Settings.AreDevToolsEnabled = false;
        _webView.Settings.IsStatusBarEnabled = false;
        _webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
        _webView.NavigationStarting += OnNavigationStarting;
        _webView.NewWindowRequested += OnNewWindowRequested;
        // 正文 DOM 就绪即可显示；远程图片继续独立加载，不能拖延整份文档的首次呈现。
        await _webView.AddScriptToExecuteOnDocumentCreatedAsync(ImageFeedbackScript);
        cancellationToken.ThrowIfCancellationRequested();
        configureForTest?.Invoke(_webView, _environment);
        TaskCompletionSource navigation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void CompleteDocument(object? sender, CoreWebView2DOMContentLoadedEventArgs eventArgs)
        {
            navigation.TrySetResult();
        }
        void CompleteInitialNavigation(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
        {
            if (!eventArgs.IsSuccess)
                navigation.TrySetException(new InvalidOperationException(UiText.MarkdownPreviewNavigationFailed));
        }

        _webView.DOMContentLoaded += CompleteDocument;
        _webView.NavigationCompleted += CompleteInitialNavigation;
        try
        {
            _allowInitialNavigation = true;
            _webView.NavigateToString(html);
            await navigation.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
        }
        finally
        {
            _webView.DOMContentLoaded -= CompleteDocument;
            _webView.NavigationCompleted -= CompleteInitialNavigation;
        }
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

        // 审计覆盖只在独立宿主进程内生效，避免 WebView2 继续采用测试机器的真实缩放。
        controller.ShouldDetectMonitorScaleChanges = false;
        controller.RasterizationScale = NativeTheme.EmbeddedContentRasterizationScaleForTest;
    }

    private static void RegisterBrowserProcess(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        lock (BrowserProcessGate)
        {
            BrowserProcessReferences.TryGetValue(processId, out int count);
            BrowserProcessReferences[processId] = count + 1;
        }
    }

    private static void ReleaseBrowserProcess(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        bool stopProcess;
        lock (BrowserProcessGate)
        {
            if (!BrowserProcessReferences.TryGetValue(processId, out int count) || count <= 1)
            {
                BrowserProcessReferences.Remove(processId);
                stopProcess = true;
            }
            else
            {
                BrowserProcessReferences[processId] = count - 1;
                stopProcess = false;
            }
        }

        if (!stopProcess)
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

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (ShouldAllowNavigation(eventArgs.Uri, _allowInitialNavigation))
        {
            _allowInitialNavigation = false;
            return;
        }

        _allowInitialNavigation = false;
        eventArgs.Cancel = true;
        if (!Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        if (MarkdownPreviewRenderer.TryDecodeAugitLink(uri, out string? path, out string? anchor) && path is not null)
        {
            _openLinkedFile(path, anchor);
        }
        else if (uri.Scheme is "http" or "https" or "mailto")
        {
            ShowLaunchResult(ExternalProgramLauncher.OpenUriWithDefaultApplication(uri));
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "mailto")
        {
            ShowLaunchResult(ExternalProgramLauncher.OpenUriWithDefaultApplication(uri));
        }
    }

    private void ShowLaunchResult(ExternalLaunchResult result)
    {
        if (!result.IsSuccess)
        {
            _setStatus(result.ErrorMessage ?? UiText.ExternalProgramFailed);
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

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // WebView2 可能延迟释放文件句柄，残留目录由下次启动时清理。
        }
        catch (UnauthorizedAccessException)
        {
            // 无法删除缓存不影响窗口关闭，避免把释放流程升级为用户可见错误。
        }
    }
}
