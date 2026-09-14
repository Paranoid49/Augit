using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;
using Augit.Infrastructure.Terminal;

namespace Augit.App;

internal sealed class NativeTerminalPanel : IDisposable
{
    private static int HeaderHeight => NativeTheme.ContentHeight(38, 12);
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Action<string> _setStatus;
    private nint _titleLabel;
    private nint _sessionLabel;
    private nint _sessionCloseButton;
    private nint _moreButton;
    private nint _closeButton;
    private TerminalWebViewHost? _webView;
    private ConPtyTerminalSession? _session;
    private TerminalSessionLease? _sessionLease;
    private bool _disposed;
    private bool _terminalPageReady;
    private readonly Queue<string> _pendingTerminalOutput = [];
    private int _pendingTerminalOutputLength;
    private (int X, int Y, int Width, int Height)? _bounds;
    private NativeToolTip? _toolTip;

    internal NativeTerminalPanel(
        nint parent,
        int hideCommand,
        int closeSessionCommand,
        int moreCommand,
        Action<string> setStatus)
    {
        _setStatus = setStatus;
        _synchronizationContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _titleLabel = CreateControl(parent, NativeMethods.StaticClass, UiText.Terminal, 401, NativeMethods.StaticOwnerDraw);
        _sessionLabel = CreateControl(parent, NativeMethods.StaticClass, string.Empty, 402, NativeMethods.StaticOwnerDraw);
        _sessionCloseButton = CreateControl(
            parent,
            NativeMethods.ButtonClass,
            string.Empty,
            closeSessionCommand,
            NativeMethods.ButtonPushButton);
        _moreButton = CreateControl(
            parent,
            NativeMethods.ButtonClass,
            string.Empty,
            moreCommand,
            NativeMethods.ButtonPushButton);
        _closeButton = CreateControl(
            parent,
            NativeMethods.ButtonClass,
            string.Empty,
            hideCommand,
            NativeMethods.ButtonPushButton);
        _toolTip = new NativeToolTip(parent);
        _toolTip.Add(_sessionLabel, UiText.TerminalShell);
        _toolTip.Add(_sessionCloseButton, UiText.CloseTerminal);
        _toolTip.Add(_moreButton, UiText.MoreActions);
        _toolTip.Add(_closeButton, UiText.HideTerminal);
    }

    internal bool HasForegroundProcess => _session?.HasForegroundProcess == true;

    internal bool IsRunning => _session?.IsRunning == true;

    internal int BrowserProcessId => _webView?.BrowserProcessId ?? 0;

    internal int ShellProcessId => _session?.ProcessId ?? 0;

    internal static int HeaderHeightForTest => HeaderHeight;

    internal bool IsVisible => _titleLabel != 0 && NativeMethods.IsWindowVisible(_titleLabel);

    internal nint Handle => _titleLabel;

    internal nint MoreButtonHandle => _moreButton;

    internal bool SessionCloseActionCreatedForTest => _sessionCloseButton != 0;

    internal bool HideActionCreatedForTest => _closeButton != 0;

    internal bool LoadingActionHasFocus => !_disposed && NativeMethods.GetFocus() == _sessionCloseButton;

    internal void SetBounds(int x, int y, int width, int height)
    {
        if (_disposed)
        {
            return;
        }

        int safeWidth = Math.Max(0, width);
        int safeHeight = Math.Max(0, height);
        _bounds = (x, y, width, height);
        int controlHeight = NativeTheme.ContentHeight(24, 4);
        int controlTop = y + Math.Max(0, (HeaderHeight - controlHeight) / 2);
        int titleWidth = Math.Max(NativeTheme.Scale(54), MeasureHeaderText(UiText.Terminal, NativeTheme.UiMediumFont) + NativeTheme.Scale(8));
        int sessionLeft = titleWidth + NativeTheme.Scale(12);
        int sessionWidth = Math.Min(Math.Max(NativeTheme.Scale(124),
            MeasureHeaderText(NativeMethods.GetWindowTextValue(_sessionLabel), NativeTheme.UiFont) + NativeTheme.Scale(16)),
            Math.Max(0, safeWidth - sessionLeft - NativeTheme.Scale(96)));
        int iconTop = y + Math.Max(0, (HeaderHeight - NativeTheme.Scale(24)) / 2);
        _ = NativeMethods.MoveWindow(
            _titleLabel,
            x + NativeTheme.Scale(8),
            controlTop,
            titleWidth,
            controlHeight,
            true);
        _ = NativeMethods.MoveWindow(
            _sessionLabel,
            x + sessionLeft,
            controlTop,
            sessionWidth,
            controlHeight,
            true);
        _ = NativeMethods.MoveWindow(
            _sessionCloseButton,
            x + sessionLeft + sessionWidth + NativeTheme.Scale(2),
            iconTop,
            NativeTheme.Scale(24),
            NativeTheme.Scale(24),
            true);
        _ = NativeMethods.MoveWindow(
            _moreButton,
            x + Math.Max(0, safeWidth - NativeTheme.Scale(68)),
            iconTop,
            NativeTheme.Scale(28),
            NativeTheme.Scale(24),
            true);
        _ = NativeMethods.MoveWindow(
            _closeButton,
            x + Math.Max(0, safeWidth - NativeTheme.Scale(36)),
            iconTop,
            NativeTheme.Scale(28),
            NativeTheme.Scale(24),
            true);
        _webView?.SetBounds(x, y + HeaderHeight, safeWidth, Math.Max(0, safeHeight - HeaderHeight));
    }

    private int MeasureHeaderText(string text, nint font)
    {
        nint dc = NativeMethods.GetDeviceContext(_titleLabel);
        if (dc == 0) return 0;
        nint previous = NativeMethods.SelectObject(dc, font);
        try
        {
            NativeMethods.Rectangle measured = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref measured,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return measured.Right - measured.Left;
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(_titleLabel, dc); }
    }

    internal bool ContainsHeaderWindow(nint window) => window != 0
        && (window == _sessionCloseButton || window == _moreButton || window == _closeButton);

    internal bool HandleHeaderTabNavigation(nint focus, bool backwards) => NativeFocusNavigation.MoveWithinRegion(
        [_sessionCloseButton, _moreButton, _closeButton], focus, backwards);

    internal void SetVisible(bool visible)
    {
        if (_disposed)
        {
            return;
        }

        int command = visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide;
        _ = NativeMethods.ShowWindow(_titleLabel, command);
        _ = NativeMethods.ShowWindow(_sessionLabel, command);
        _ = NativeMethods.ShowWindow(_sessionCloseButton, command);
        _ = NativeMethods.ShowWindow(_moreButton, command);
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
        _toolTip?.ApplyAppearance(dark);
        foreach (nint control in new[] { _titleLabel, _sessionLabel, _sessionCloseButton, _moreButton, _closeButton })
            _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, (nuint)NativeTheme.UiFont, 1);
        NativeTheme.ApplyToControl(_titleLabel, dark);
        NativeTheme.ApplyToControl(_sessionLabel, dark);
        NativeTheme.ApplyToControl(_sessionCloseButton, dark);
        NativeTheme.ApplyToControl(_moreButton, dark);
        NativeTheme.ApplyToControl(_closeButton, dark);
        _webView?.ApplyAppearance(settings);
        if (_bounds is { } bounds) SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    internal void Focus()
    {
        if (_disposed) return;
        if (_session is null) _ = NativeMethods.SetFocus(_sessionCloseButton);
        else _webView?.Focus();
    }

    internal bool ContainsWindow(nint window)
    {
        return _webView?.ContainsWindow(window) == true;
    }

    internal Task WriteInputForTestAsync(string input)
    {
        return WriteInputAsync(input);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _toolTip?.Dispose();
        _toolTip = null;
        if (_session is not null)
        {
            _session.OutputReceived -= OnOutputReceived;
            _session.Exited -= OnExited;
            _session.Dispose();
            _session = null;
        }
        _terminalPageReady = false;
        _pendingTerminalOutput.Clear();
        _pendingTerminalOutputLength = 0;

        _sessionLease?.Dispose();
        _sessionLease = null;
        _webView?.Dispose();
        _webView = null;
        DestroyControl(ref _titleLabel);
        DestroyControl(ref _sessionLabel);
        DestroyControl(ref _sessionCloseButton);
        DestroyControl(ref _moreButton);
        DestroyControl(ref _closeButton);
    }

    internal async Task InitializeAsync(string workspaceRoot, ApplicationSettings settings, Func<string, Task>? checkpoint = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TerminalLaunchResult launchResult = TerminalShellResolver.Resolve(settings, workspaceRoot);
        if (!launchResult.IsSuccess || launchResult.LaunchInfo is null)
        {
            throw new InvalidOperationException(launchResult.ErrorMessage ?? UiText.TerminalStartFailed);
        }

        _ = NativeMethods.SetWindowText(
            _sessionLabel,
            UiText.TerminalSessionLoading(launchResult.LaunchInfo.DisplayName));
        _toolTip?.Update(_sessionLabel, UiText.TerminalSessionLoading(launchResult.LaunchInfo.DisplayName));
        ApplyAppearance(settings);
        TerminalSessionRegistry registry = new();
        _sessionLease = registry.TryAcquire(workspaceRoot)
            ?? throw new InvalidOperationException(UiText.TerminalSessionAlreadyActive);
        if (checkpoint is not null) await checkpoint("panel");
        ObjectDisposedException.ThrowIf(_disposed, this);
        _webView = new TerminalWebViewHost(
            NativeMethods.GetParent(_titleLabel),
            data => _ = WriteInputAsync(data),
            Resize);
        _webView.SetVisible(IsVisible);
        if (_bounds is { } bounds) SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);

        // Shell 只需要一个初始尺寸，先在 WebView2 导航前创建并接管；页面 ready 前的输出由
        // 本面板暂存，等页面可接收消息后一次性补发，避免用户等待浏览器初始化才看到提示符。
        ConPtyTerminalSession session = await Task.Run(() => ConPtyTerminalSession.Start(
            launchResult.LaunchInfo,
            workspaceRoot,
            80,
            24)).ConfigureAwait(true);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _session = session;
        _session.OutputReceived += OnOutputReceived;
        _session.Exited += OnExited;
        try
        {
            await _webView.InitializeAsync(settings, checkpoint);
            ObjectDisposedException.ThrowIf(_disposed, this);
            (int columns, int rows) = _webView.InitialSize;
            // WebView2 ready 前 Resize 可能尚未送达；这里用最终列行补一次，避免提示符换行错位。
            if (columns >= 2 && rows >= 1)
            {
                _session.Resize(columns, rows);
            }
            _terminalPageReady = true;
            foreach (string output in _pendingTerminalOutput)
                _webView.Write(output);
            _pendingTerminalOutput.Clear();
            _pendingTerminalOutputLength = 0;
            _ = NativeMethods.SetWindowText(_sessionLabel, launchResult.LaunchInfo.DisplayName);
            _toolTip?.Update(_sessionLabel, launchResult.LaunchInfo.DisplayName);
            ApplyAppearance(settings);
        }
        catch
        {
            // 页面或宿主初始化失败时，必须接住并释放已接管的会话，不能遗留后台进程。
            _session.OutputReceived -= OnOutputReceived;
            _session.Exited -= OnExited;
            _session.Dispose();
            _session = null;
            throw;
        }
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
            _synchronizationContext.Post(_ =>
            {
                if (!_disposed) _setStatus(UiText.TerminalInputFailed);
            }, null);
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
        _synchronizationContext.Post(_ =>
        {
            if (_disposed || output.Length == 0) return;
            if (_terminalPageReady)
            {
                _webView?.Write(output);
                return;
            }

            _pendingTerminalOutput.Enqueue(output);
            _pendingTerminalOutputLength += output.Length;
            while (_pendingTerminalOutputLength > 1024 * 1024
                && _pendingTerminalOutput.TryDequeue(out string? removed))
                _pendingTerminalOutputLength -= removed.Length;
        }, null);
    }

    private void OnExited(object? sender, int exitCode)
    {
        _synchronizationContext.Post(_ =>
        {
            if (_disposed || !ReferenceEquals(sender, _session)) return;
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
        bool button = className.Equals(NativeMethods.ButtonClass, StringComparison.Ordinal);
        uint controlStyle = button ? NativeMethods.ButtonOwnerDraw : specificStyle;
        uint tabStopStyle = button ? NativeMethods.WindowStyleTabStop : 0;
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | tabStopStyle
                | controlStyle,
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

        nint font = NativeTheme.UiFont;
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        if (button) MainWindow.AttachFrameButton(parent, control);
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
