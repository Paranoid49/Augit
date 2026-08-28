using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Interop;
using Microsoft.Web.WebView2.Core;

namespace Augit.App;

internal sealed class MarkdownWebViewHost : IDisposable
{
    private static readonly object LoaderGate = new();
    private static nint _loaderModule;
    private readonly Action<string, string?> _openLinkedFile;
    private readonly Action<string> _setStatus;
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.MarkdownHostCreateFailed);
        }
    }

    internal nint Handle { get; private set; }

    internal int BrowserProcessId => checked((int)(_webView?.BrowserProcessId ?? 0));

    internal static async Task<MarkdownWebViewHost> CreateAsync(
        nint parent,
        string html,
        Action<string, string?> openLinkedFile,
        Action<string> setStatus)
    {
        ArgumentNullException.ThrowIfNull(html);
        MarkdownWebViewHost host = new(parent, openLinkedFile, setStatus);
        try
        {
            await host.InitializeAsync(html);
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

    internal async Task NavigateToAnchorAsync(string anchor)
    {
        if (_webView is null || string.IsNullOrWhiteSpace(anchor))
        {
            return;
        }

        string escaped = System.Text.Json.JsonSerializer.Serialize(anchor);
        _ = await _webView.ExecuteScriptAsync($"location.hash = {escaped};");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _allowInitialNavigation = false;
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

    private async Task InitializeAsync(string html)
    {
        EnsureLoaderLoaded();
        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Augit",
            "WebView2",
            "Markdown");
        _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
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
        _webView.NavigationStarting += OnNavigationStarting;
        _webView.NewWindowRequested += OnNewWindowRequested;
        _allowInitialNavigation = true;
        _webView.NavigateToString(html);
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
}
