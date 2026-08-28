using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;
using Augit.Infrastructure.Terminal;

namespace Augit.App;

internal sealed class NativeTerminalPanel : IDisposable
{
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Action<string> _setStatus;
    private nint _titleLabel;
    private nint _closeButton;
    private TerminalWebViewHost? _webView;
    private ConPtyTerminalSession? _session;
    private TerminalSessionLease? _sessionLease;
    private bool _disposed;

    private NativeTerminalPanel(nint parent, int closeCommand, Action<string> setStatus)
    {
        _setStatus = setStatus;
        _synchronizationContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _titleLabel = CreateControl(parent, NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft);
        _closeButton = CreateControl(
            parent,
            NativeMethods.ButtonClass,
            UiText.CloseTerminal,
            closeCommand,
            NativeMethods.ButtonPushButton);
    }

    internal bool HasForegroundProcess => _session?.HasForegroundProcess == true;

    internal bool IsRunning => _session?.IsRunning == true;

    internal int BrowserProcessId => _webView?.BrowserProcessId ?? 0;

    internal int ShellProcessId => _session?.ProcessId ?? 0;

    internal static async Task<NativeTerminalPanel> CreateAsync(
        nint parent,
        int closeCommand,
        string workspaceRoot,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        NativeTerminalPanel panel = new(parent, closeCommand, setStatus);
        try
        {
            await panel.InitializeAsync(workspaceRoot, settings);
            return panel;
        }
        catch
        {
            panel.Dispose();
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
        _ = NativeMethods.MoveWindow(_titleLabel, x + 8, y + 3, Math.Max(0, safeWidth - 102), 25, true);
        _ = NativeMethods.MoveWindow(_closeButton, x + Math.Max(0, safeWidth - 88), y + 2, 80, 26, true);
        _webView?.SetBounds(x, y + 30, safeWidth, Math.Max(0, safeHeight - 30));
    }

    internal void SetVisible(bool visible)
    {
        if (_disposed)
        {
            return;
        }

        int command = visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide;
        _ = NativeMethods.ShowWindow(_titleLabel, command);
        _ = NativeMethods.ShowWindow(_closeButton, command);
        _webView?.SetVisible(visible);
    }

    internal void ApplyAppearance(ApplicationSettings settings)
    {
        if (_disposed)
        {
            return;
        }

        bool dark = NativeTheme.IsDark(settings.Theme);
        NativeTheme.ApplyToControl(_titleLabel, dark);
        NativeTheme.ApplyToControl(_closeButton, dark);
        _webView?.ApplyAppearance(settings);
    }

    internal void Focus()
    {
        _webView?.Focus();
    }

    internal bool ContainsWindow(nint window)
    {
        return _webView?.ContainsWindow(window) == true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_session is not null)
        {
            _session.OutputReceived -= OnOutputReceived;
            _session.Exited -= OnExited;
            _session.Dispose();
            _session = null;
        }

        _sessionLease?.Dispose();
        _sessionLease = null;
        _webView?.Dispose();
        _webView = null;
        DestroyControl(ref _titleLabel);
        DestroyControl(ref _closeButton);
    }

    private async Task InitializeAsync(string workspaceRoot, ApplicationSettings settings)
    {
        TerminalLaunchResult launchResult = TerminalShellResolver.Resolve(settings, workspaceRoot);
        if (!launchResult.IsSuccess || launchResult.LaunchInfo is null)
        {
            throw new InvalidOperationException(launchResult.ErrorMessage ?? UiText.TerminalStartFailed);
        }

        _ = NativeMethods.SetWindowText(
            _titleLabel,
            UiText.TerminalTitle(launchResult.LaunchInfo.DisplayName, workspaceRoot));
        TerminalSessionRegistry registry = new();
        _sessionLease = registry.TryAcquire(workspaceRoot)
            ?? throw new InvalidOperationException(UiText.TerminalSessionAlreadyActive);
        _webView = await TerminalWebViewHost.CreateAsync(
            NativeMethods.GetParent(_titleLabel),
            settings,
            data => _ = WriteInputAsync(data),
            Resize);
        (int columns, int rows) = _webView.InitialSize;
        _session = ConPtyTerminalSession.Start(
            launchResult.LaunchInfo,
            workspaceRoot,
            columns,
            rows);
        _session.OutputReceived += OnOutputReceived;
        _session.Exited += OnExited;
        ApplyAppearance(settings);
    }

    private async Task WriteInputAsync(string data)
    {
        ConPtyTerminalSession? session = _session;
        if (_disposed || session is null || !session.IsRunning)
        {
            return;
        }

        try
        {
            await session.WriteAsync(data);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            _synchronizationContext.Post(_ => _setStatus(UiText.TerminalInputFailed), null);
        }
    }

    private void Resize(int columns, int rows)
    {
        try
        {
            _session?.Resize(columns, rows);
        }
        catch (Exception exception) when (exception is COMException or ObjectDisposedException)
        {
            _setStatus(UiText.TerminalResizeFailed);
        }
    }

    private void OnOutputReceived(object? sender, string output)
    {
        _synchronizationContext.Post(_ => _webView?.Write(output), null);
    }

    private void OnExited(object? sender, int exitCode)
    {
        _synchronizationContext.Post(_ =>
        {
            _sessionLease?.Dispose();
            _sessionLease = null;
            _setStatus(UiText.TerminalExited(exitCode));
        }, null);
    }

    private static nint CreateControl(
        nint parent,
        string className,
        string text,
        int identifier,
        uint specificStyle)
    {
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | specificStyle,
            0,
            0,
            0,
            0,
            parent,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.TerminalControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return control;
    }

    private static void DestroyControl(ref nint control)
    {
        nint handle = control;
        control = 0;
        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }
}
