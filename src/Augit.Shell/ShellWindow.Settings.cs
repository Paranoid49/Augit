using System.Runtime.InteropServices;
using System.Text.Json;
using Augit.Infrastructure.Settings;
using Microsoft.Web.WebView2.Core;

namespace Augit.Shell;

// 设置拥有独立控制器，但复用主窗口桥接和设置存储；不启动第二套工作区监听。
internal sealed partial class ShellWindow
{
    private readonly ShellWindow? _settingsOwner;
    private readonly SettingsStageTrace _settingsStageTrace = new();
    private void TraceSettingsStage(string stage)
    {
        if (_settingsOwner is not null) _settingsStageTrace.Record(stage);
    }
    private void ExportSettingsStages()
    {
        if (_settingsOwner is not null)
            _ = _settingsStageTrace.ExportAsync(Environment.GetEnvironmentVariable("AUGIT_SETTINGS_TRACE_DIRECTORY"));
    }
    private ShellWindow? _settingsChild;
    private bool _settingsClosing;
    private bool _settingsReady;
    private bool _settingsPrepared;
    private bool _settingsTransitionsDisabled;
    private readonly System.Diagnostics.Stopwatch _settingsStartupClock = System.Diagnostics.Stopwatch.StartNew();
    private long _settingsControllerStartedMilliseconds;
    private long _settingsControllerReadyMilliseconds;
    private long _settingsScriptRegistrationStartedMilliseconds;
    private long _settingsScriptRegistrationReadyMilliseconds;
    private readonly int _settingsCreationThread = Environment.CurrentManagedThreadId;
    private int _settingsControllerReadyThread;
    private int _settingsScriptReadyThread;
    private int _settingsRenderedThread;
    private long _settingsNavigationStartedMilliseconds;
    private long _settingsPreparedMilliseconds;
    private long _settingsPrepareStartedMilliseconds;
    private long _settingsCloakedMilliseconds;
    private long _settingsBoundsSyncedMilliseconds;
    private long _settingsControllerVisibleMilliseconds;
    private long _settingsWindowShownMilliseconds;
    private long _settingsRenderedMilliseconds;
    private long _settingsPresentedMilliseconds;
    private bool _settingsWriteInFlight;
    private string _settingsPage = "appearance";
    private long _settingsRevision;
    private string? _settingsThemeMode;
    private const uint SettingsStartupTimeoutMessage = 0x0400 + 8;
    private const uint SettingsPresentMessage = 0x0400 + 9;
    private const uint SettingsSurfaceThemeMessage = 0x0400 + 10;
    private const uint SettingsRevealMessage = 0x0400 + 11;
    private const uint SettingsPrepareMessage = 0x0400 + 12;
    private long _settingsPrepareRequest;
    private long _settingsPresentRequest;
    private Timer? _settingsStartupTimeout;

    private void OpenSettingsWindow(string page)
    {
        if (_settingsChild is { } existing)
        {
            if (existing._settingsReady) SetForegroundWindow(existing._window);
            return;
        }
        GetWindowRect(_window, out Rect bounds);
        double scale = _controller?.RasterizationScale ?? _effectiveDpi / 96.0;
        int width = Math.Min(1040, Math.Max(320, (int)((bounds.Right - bounds.Left) / scale) - 80));
        int height = Math.Min(760, Math.Max(240, (int)((bounds.Bottom - bounds.Top) / scale) - 88));
        ShellOptions options = _options with
        {
            Scene = null,
            OpenDocument = null,
            BlameDocument = null,
            FileHistoryDocument = null,
            ConflictDocument = null,
            DiffDocument = null,
            Width = width,
            Height = height,
            Theme = _notifiedTheme ?? _options.Theme,
            NoSessionRestore = true
        };
        ShellWindow child = new(options, settingsOwner: this);
        _settingsChild = child;
        // 设置开关不得以系统缩放淡出留下半透明表单；只作用于本次子窗，不修改系统全局动画。
        int disableTransitions = 1;
        int transitionResult = DwmSetWindowAttribute(child._window, 3, ref disableTransitions, sizeof(int));
        if (transitionResult < 0)
        {
            child.Dispose();
            Marshal.ThrowExceptionForHR(transitionResult);
        }
        child._settingsTransitionsDisabled = true;
        nint childHandle = child._window;
        child._settingsStartupTimeout = new Timer(_ => PostMessage(childHandle, SettingsStartupTimeoutMessage, 0, 0), null, 20000, Timeout.Infinite);
        child._settingsPage = page;
        GetWindowRect(child._window, out Rect childBounds);
        int childWidth = childBounds.Right - childBounds.Left;
        int childHeight = childBounds.Bottom - childBounds.Top;
        // 独立设置窗不是模态，但也必须像模态一样夹到 Owner 所在显示器的工作区；
        // 只按中心摆放会在靠近屏幕右/下边缘时把设置内容推到屏外。
        int left = bounds.Left + ((bounds.Right - bounds.Left) - childWidth) / 2;
        int top = bounds.Top + ((bounds.Bottom - bounds.Top) - childHeight) / 2;
        WindowFrameRect placement = new(left, top, left + childWidth, top + childHeight);
        if (TryGetMonitorWorkArea(out WindowFrameRect work))
        {
            placement = ShellModalGeometry.CenterAndFit(
                new WindowFrameRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
                childWidth,
                childHeight,
                work);
        }

        SetWindowPos(child._window, 0, placement.Left, placement.Top,
            placement.Right - placement.Left, placement.Bottom - placement.Top, SwpNozorder | SwpNoactivate);
        SetWindowText(child._window, "Settings — Augit");
        SettingsMargins margins = new() { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        _ = DwmExtendFrameIntoClientArea(child._window, ref margins);
    }

    private void ReleaseSettingsOwner()
    {
        if (_settingsOwner is not { } owner || !ReferenceEquals(owner._settingsChild, this)) return;
        TraceSettingsStage("owner-release-start");
        owner._settingsChild = null;
        if (!owner._disposed && owner._window != 0)
        {
            // 取消与异常关闭都恢复已提交主题；不能让预览底色遗留到下次开窗。
            owner.SetSettingsSurfaceTheme(ShellTheme.ResolveMode(owner._settingsThemeMode ?? owner._options.ThemeMode, ShellSystemTheme.DetectDark()));
            EnableWindow(owner._window, true);
            TraceSettingsStage("owner-enabled");
            // 先撤下设置的合成画面，再切换焦点；否则活动阴影会在仍可见的子窗上消失一帧。
            // 所有者已启用，隐藏失败的回退路径也不会把焦点交给其它应用。
            if (_window != 0)
            {
                int cloak = 1;
                if (DwmSetWindowAttribute(_window, 13, ref cloak, sizeof(int)) < 0) ShowWindow(_window, 0);
                TraceSettingsStage("close-cloaked");
                _ = DwmFlush();
                TraceSettingsStage("close-flushed");
            }
            SetForegroundWindow(owner._window);
            TraceSettingsStage("owner-activated");
            owner.Notify("settings/closed", new { revision = owner._settingsRevision });
        }
    }

    private void FailSettingsInitialization(Exception exception)
    {
        // 关闭后到达的初始化或捕获异常属于旧窗口，不能影响后来重开的设置。
        if (_disposed || _settingsOwner?._disposed == true) return;
        _settingsOwner?.Notify("settings/failed", new { reason = "Settings could not be opened (" + exception.GetType().Name + "). Please retry." });
        Dispose();
    }

    private void QueueSettingsReply(long id, object? result, string? error = null)
    {
        if (_disposed || _window == 0) return;
        lock (_pendingReplies) _pendingReplies.Enqueue(JsonSerializer.Serialize(new { id, result, error }));
        PostMessage(_window, ReplyMessage, 0, 0);
    }

    // 独立设置页面只获得设置所需能力，不能恢复标签、启动终端或操作仓库。
    internal static bool IsSettingsPageMethod(string method) => method is
        "settings/read" or "settings/write" or "settings/fonts" or "git/detect";

    internal static string SettingsTraceMethod(string method) => IsSettingsPageMethod(method) || method is
        "window/settings-ready" or "window/settings-present" or "window/settings-close"
        or "window/settings-drag" or "window/settings-failed" or "window/settings-preview"
        ? method : "unknown-settings-message";

    private async Task<bool> TryHandleSettingsMessageAsync(string json)
    {
        using JsonDocument envelope = JsonDocument.Parse(json);
        using JsonDocument? nested = envelope.RootElement.ValueKind == JsonValueKind.String
            ? JsonDocument.Parse(envelope.RootElement.GetString()!) : null;
        JsonElement root = nested?.RootElement ?? envelope.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("method", out JsonElement methodValue)
            || methodValue.ValueKind != JsonValueKind.String || !root.TryGetProperty("id", out JsonElement idValue)
            || idValue.ValueKind != JsonValueKind.Number || !idValue.TryGetInt64(out long id)) return false;
        string method = methodValue.GetString() ?? "";
        JsonElement parameters = root.TryGetProperty("params", out JsonElement value) ? value : default;
        if (_settingsOwner is null && !method.StartsWith("window/settings-", StringComparison.Ordinal)) return false;
        try
        {
            if (_settingsOwner is null)
            {
                if (method == "window/settings-open")
                {
                    string page = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("page", out JsonElement p) ? p.GetString() ?? "appearance" : "appearance";
                    OpenSettingsWindow(page);
                }
                else if (method != "window/settings-state") throw new InvalidOperationException();
                QueueSettingsReply(id, new
                {
                    active = _settingsChild is not null,
                    ready = _settingsChild?._settingsReady ?? false,
                    window = (long)(_settingsChild?._window ?? 0),
                    visible = _settingsChild?._controller?.IsVisible ?? false,
                    saving = _settingsWriteInFlight,
                    revision = _settingsRevision,
                    surfaceColor = SettingsSurfaceColor(),
                    presentation = _settingsChild?.SettingsPresentationState()
                });
                return true;
            }
            // 只保留已知协议名，未知消息字符串不写入诊断。
            TraceSettingsStage(SettingsTraceMethod(method));
            ShellWindow owner = _settingsOwner;
            switch (method)
            {
                case "window/settings-ready":
                    if (_settingsClosing || _disposed) return true;
                    if (_controller is null) throw new InvalidOperationException();
                    _settingsPrepareRequest = id;
                    PostMessage(_window, SettingsPrepareMessage, 0, 0);
                    break;
                case "window/settings-present":
                    if (_settingsClosing || _disposed) return true;
                    if (!_settingsPrepared) throw new InvalidOperationException();
                    _settingsPresentRequest = id;
                    PostMessage(_window, SettingsPresentMessage, 0, 0);
                    break;
                case "window/settings-close":
                    _settingsClosing = true;
                    QueueSettingsReply(id, new { closed = true });
                    PostMessage(_window, WmClose, 0, 0);
                    break;
                case "window/settings-drag":
                    PostMessage(_window, StartDragMessage, 0, 0);
                    QueueSettingsReply(id, new { });
                    break;
                case "window/settings-failed":
                    FailSettingsInitialization(new InvalidOperationException("设置页面初始化失败。"));
                    break;
                case "window/settings-preview":
                    string theme = parameters.GetProperty("theme").GetString() ?? "light";
                    if (theme is not ("dark" or "light")) throw new ArgumentException("主题参数无效。");
                    SetSettingsSurfaceTheme(theme);
                    owner.SetSettingsSurfaceTheme(theme);
                    owner.Notify("settings/preview", new { theme });
                    QueueSettingsReply(id, new { });
                    break;
                default:
                    if (!IsSettingsPageMethod(method)) throw new InvalidOperationException();
                    bool writes = method == "settings/write";
                    if (writes && owner._settingsWriteInFlight) throw new InvalidOperationException();
                    if (writes) owner._settingsWriteInFlight = true;
                    try
                    {
                        string response = await _bridge.HandleAsync(json, CancellationToken.None);
                        using JsonDocument reply = JsonDocument.Parse(response);
                        if (writes && reply.RootElement.TryGetProperty("result", out JsonElement result)
                            && result.ValueKind == JsonValueKind.Object && result.TryGetProperty("saved", out JsonElement saved) && saved.ValueKind == JsonValueKind.True)
                        {
                            owner._settingsThemeMode = result.GetProperty("theme").GetString();
                            string committedTheme = ShellTheme.ResolveMode(owner._settingsThemeMode ?? owner._options.ThemeMode, ShellSystemTheme.DetectDark());
                            owner.SetSettingsSurfaceTheme(committedTheme);
                            if (!_disposed) SetSettingsSurfaceTheme(committedTheme);
                            owner.Notify("settings/committed", new { revision = ++owner._settingsRevision });
                        }
                        if (!_disposed && _window != 0)
                        {
                            lock (_pendingReplies) _pendingReplies.Enqueue(response);
                            PostMessage(_window, ReplyMessage, 0, 0);
                        }
                    }
                    finally
                    {
                        if (writes)
                        {
                            owner._settingsWriteInFlight = false;
                            owner._settingsChild?.Notify("settings/write-idle", new { revision = owner._settingsRevision });
                        }
                    }
                    break;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or COMException)
        {
            QueueSettingsReply(id, null, "设置窗口操作失败，请重试。");
        }
        return true;
    }

    // 首次显示也须退出WebView回调；ShowWindow会同步派发尺寸与激活消息，不能在COM回调内重入。
    private void PrepareSettingsWindow()
    {
        if (_disposed || _settingsClosing || _settingsOwner is not { } owner || owner._disposed) return;
        try
        {
            if (_controller is null) throw new InvalidOperationException();
            _settingsPrepareStartedMilliseconds = _settingsStartupClock.ElapsedMilliseconds;
            TraceSettingsStage("prepare-start");
            int cloak = 1;
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(_window, 13, ref cloak, sizeof(int)));
            _settingsCloakedMilliseconds = _settingsStartupClock.ElapsedMilliseconds;
            SyncBounds();
            _settingsBoundsSyncedMilliseconds = _settingsStartupClock.ElapsedMilliseconds;
            TraceSettingsStage("controller-visible-start");
            _controller.IsVisible = true;
            TraceSettingsStage("controller-visible-end");
            _settingsControllerVisibleMilliseconds = _settingsStartupClock.ElapsedMilliseconds;
            ShowWindow(_window, 4); // 不抢焦点地启动真实合成，显示前仍允许主窗口响应。
            _settingsWindowShownMilliseconds = _settingsStartupClock.ElapsedMilliseconds;
            _controller.NotifyParentWindowPositionChanged();
            _settingsPrepared = true;
            _settingsPreparedMilliseconds = _settingsStartupClock.ElapsedMilliseconds;
            TraceSettingsStage("prepared");
            QueueSettingsReply(_settingsPrepareRequest, new { ready = true, page = _settingsPage, saving = owner._settingsWriteInFlight });
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            FailSettingsInitialization(exception);
        }
    }

    // 原生激活退出WebView回调后由窗口消息循环执行，避免重入和跨线程窗口操作。
    private async Task PresentSettingsWindowAsync()
    {
        if (_disposed || _settingsClosing || _settingsOwner is not { } owner || owner._disposed) return;
        try
        {
            EnableWindow(owner._window, false);
            TraceSettingsStage("activation-start");
            Show();
            SetForegroundWindow(_window);
            _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
            _ = DwmFlush();
            TraceSettingsStage("capture-start");
            // 显示、激活及焦点切换都可能使合成表面失效；必须在这些操作之后等待真实渲染。
            // 只捕获设置自身并丢弃结果，不显示截图、不复制主界面。
            Task capture = _controller!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, Stream.Null);
            // 分开记录COM调用返回与异步完成；正常捕获约 68–150ms，历史慢样本曾等待约 2479ms。
            // 以 500ms 上限避免捕获把设置窗呈现拖到秒级；超时后仍继续揭示，晚到的任务只观察异常。
            TraceSettingsStage("capture-issued");
            if (!await WaitForCaptureAsync(capture, TimeSpan.FromMilliseconds(500)))
            {
                TraceSettingsStage("capture-timeout");
                _ = capture.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
            if (_disposed || _settingsClosing || owner._disposed) return;
            _settingsRenderedMilliseconds = _settingsStartupClock.ElapsedMilliseconds;
            _settingsRenderedThread = Environment.CurrentManagedThreadId;
            TraceSettingsStage("capture-end");
            PostMessage(_window, SettingsRevealMessage, 0, 0);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            FailSettingsInitialization(exception);
        }
    }

    // 渲染完成后仅撤下DWM遮蔽，不再显示、改尺寸或改变控制器焦点。
    private void RevealSettingsWindow()
    {
        if (_disposed || _settingsClosing || _settingsOwner is not { } owner || owner._disposed) return;
        try
        {
            // 设置自身的捕获完成不等同桌面合成提交；先等待当前合成批次，再揭示真实窗口。
            Marshal.ThrowExceptionForHR(DwmFlush());
            TraceSettingsStage("uncloak-start");
            int uncloak = 0;
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(_window, 13, ref uncloak, sizeof(int)));
            _settingsReady = true;
            _settingsPresentedMilliseconds = _settingsStartupClock.ElapsedMilliseconds;
            TraceSettingsStage("presented");
            _settingsStartupTimeout?.Dispose();
            QueueSettingsReply(_settingsPresentRequest, new { ready = true, page = _settingsPage, saving = owner._settingsWriteInFlight });
            ExportSettingsStages();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            FailSettingsInitialization(exception);
        }
    }

    // 网页主题、原生表面和下一次设置开窗必须使用同一生效值。
    private void SetSettingsSurfaceTheme(string theme)
    {
        if (_disposed || _window == 0) return;
        _notifiedTheme = ShellTheme.ResolveMode(theme, null);
        // 保存完成可能在线程池续接；WebView COM 属性只能由原生 UI 消息处理。
        PostMessage(_window, SettingsSurfaceThemeMessage, 0, 0);
    }

    private string? SettingsSurfaceColor()
    {
        if (_controller is null) return null;
        System.Drawing.Color color = _controller.DefaultBackgroundColor;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private object SettingsPresentationState()
    {
        GetWindowRect(_window, out Rect bounds);
        return new
        {
            x = bounds.Left,
            y = bounds.Top,
            width = bounds.Right - bounds.Left,
            height = bounds.Bottom - bounds.Top,
            transitionsDisabledRequestAccepted = _settingsTransitionsDisabled,
            surfaceColor = SettingsSurfaceColor(),
            initialTheme = ShellTheme.QueryValue(_options.Theme ?? "Light"),
            controllerStartedMilliseconds = _settingsControllerStartedMilliseconds,
            controllerReadyMilliseconds = _settingsControllerReadyMilliseconds,
            scriptRegistrationStartedMilliseconds = _settingsScriptRegistrationStartedMilliseconds,
            scriptRegistrationReadyMilliseconds = _settingsScriptRegistrationReadyMilliseconds,
            creationThread = _settingsCreationThread,
            controllerReadyThread = _settingsControllerReadyThread,
            scriptReadyThread = _settingsScriptReadyThread,
            renderedThread = _settingsRenderedThread,
            navigationStartedMilliseconds = _settingsNavigationStartedMilliseconds,
            prepareStartedMilliseconds = _settingsPrepareStartedMilliseconds,
            cloakedMilliseconds = _settingsCloakedMilliseconds,
            boundsSyncedMilliseconds = _settingsBoundsSyncedMilliseconds,
            controllerVisibleMilliseconds = _settingsControllerVisibleMilliseconds,
            windowShownMilliseconds = _settingsWindowShownMilliseconds,
            preparedMilliseconds = _settingsPreparedMilliseconds,
            renderedMilliseconds = _settingsRenderedMilliseconds,
            presentedMilliseconds = _settingsPresentedMilliseconds
        };
    }

    // 捕获只用于确认设置自身表面已可呈现；设置内容在 settings-ready 前已就绪，
    // 因此等待上限到达后应继续揭示，而不是让一次慢捕获把开窗拖到秒级。
    internal static async Task<bool> WaitForCaptureAsync(Task capture, TimeSpan timeout)
    {
        try
        {
            await capture.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SettingsMargins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint window, ref SettingsMargins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint window, bool enable);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);

    // 设置子窗的擦除底色必须跟随主题表面色；WebView2 过渡期间短暂露出原生面时不能是白色。
    private void PaintSettingsBackground(nint hdc)
    {
        if (_controller is null || _window == 0) return;
        GetClientRect(_window, out Rect bounds);
        System.Drawing.Color color = _controller.DefaultBackgroundColor;
        uint colorRef = (uint)(color.R | color.G << 8 | color.B << 16);
        nint brush = CreateSolidBrush(colorRef);
        if (brush == 0) return;
        try
        {
            _ = FillRect(hdc, ref bounds, brush);
        }
        finally
        {
            DeleteObject(brush);
        }
    }
}
