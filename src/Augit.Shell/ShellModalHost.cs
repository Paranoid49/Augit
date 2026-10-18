using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Augit.Shell;

/// <summary>
/// 同一网页会话裁切到独立的所有者窗口，主窗口以临时截图保留上下文。
/// 表单、监听及桥接始终留在原WebView，不序列化或复制可执行DOM。
/// </summary>
internal sealed class ShellModalHost : IDisposable
{
    private const string ClassName = "Augit.Shell.Modal";
    private static readonly Dictionary<nint, ShellModalHost> Hosts = [];
    private static readonly WindowProcedure Procedure = ProcessMessage;
    private static bool _registered;
    private readonly nint _owner;
    private readonly CoreWebView2Controller _live;
    private readonly CoreWebView2Environment _environment;
    private readonly Action _cancel;
    private readonly Action _scaleChanged;
    private readonly bool _fixedScale;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly List<Surface> _surfaces = [];
    private readonly ShellModalOperations _operations = new();
    private Snapshot? _background;
    private Snapshot? _pendingBackground;
    private ShellSnapshotNavigation? _pendingCoverNavigation;
    private Snapshot? _captureCover;
    private nint _captureCoverWindow;
    private bool _disposed;
    private bool _closing;
    private int _snapshotRevision;
    private double _coverCaptureMilliseconds, _coverControllerMilliseconds, _coverNavigationMilliseconds, _coverReadyMilliseconds;
    private int _viewportWidth;
    private int _viewportHeight;
    private double _scale;

    internal ShellModalHost(nint owner, CoreWebView2Controller live, CoreWebView2Environment environment, Action cancel, Action scaleChanged, bool fixedScale)
    {
        _owner = owner;
        _live = live;
        _environment = environment;
        _cancel = cancel;
        _scaleChanged = scaleChanged;
        _fixedScale = fixedScale;
    }

    internal bool Active => _surfaces.Count > 0;

    internal Task PresentAsync(string key, string title, IReadOnlySet<string> ancestors, WindowFrameRect clip, double scale, int viewportWidth, int viewportHeight)
    {
        VerifyUiThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        // 返回已存在的父表面只同步重挂，不等待已被关闭子窗的异步截图。
        int existing = _surfaces.FindIndex(surface => surface.Key == key);
        if (existing >= 0 && existing < _surfaces.Count - 1)
            return _operations.SupersedeAsync(generation => PresentCoreAsync(key, title, ancestors, clip, scale, viewportWidth, viewportHeight, generation));
        return _operations.RunAsync(generation => PresentCoreAsync(key, title, ancestors, clip, scale, viewportWidth, viewportHeight, generation));
    }

    private async Task PresentCoreAsync(string key, string title, IReadOnlySet<string> ancestors, WindowFrameRect clip, double scale, int viewportWidth, int viewportHeight, long generation)
    {
        if (_disposed) return;
        // 区域重绘可能替换表单节点；已不属于当前DOM链的窗口不能留作新的所有者。
        while (Active && _surfaces[^1].Key != key && !ancestors.Contains(_surfaces[^1].Key)) Pop();
        int existing = _surfaces.FindIndex(surface => surface.Key == key);
        if (existing >= 0)
        {
            ReleaseCaptureCover();
            while (_surfaces.Count > existing + 1) Pop();
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            _scale = scale;
            Surface current = _surfaces[^1];
            current.Scale = scale;
            if (current.Clip != clip)
            {
                current.Clip = clip;
                SetWindowPos(current.Window, 0, 0, 0, clip.Right - clip.Left, clip.Bottom - clip.Top, 0x0002 | 0x0004);
            }
            SyncLiveBounds();
            return;
        }

        RegisterClass();
        ReleaseCaptureCover();
        if (!Active)
        {
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            _scale = scale;
        }

        nint parent = Active ? _surfaces[^1].Window : _owner;
        Snapshot? snapshot = await CreateSnapshotAsync(parent, generation);
        if (_disposed || !_operations.IsCurrent(generation)) { snapshot?.Close(); return; }
        if (snapshot is null) return;
        _pendingBackground = snapshot;
        try
        {
            if (!await ShellSnapshotPreparation.WaitForImageAsync(snapshot.Ready,
                () => !_disposed && _operations.IsCurrent(generation), snapshot.Close)) return;
        }
        finally { if (ReferenceEquals(_pendingBackground, snapshot)) _pendingBackground = null; }
        // 网页裁切相对客户区；最大化窗口的外框含屏幕外边距，不能作为原点。
        Point ownerOrigin = default;
        if (!ClientToScreen(_owner, ref ownerOrigin))
        {
            snapshot.Close();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "读取模态所有者客户区原点失败。");
        }
        WindowFrameRect placement = new(ownerOrigin.X + clip.Left, ownerOrigin.Y + clip.Top, ownerOrigin.X + clip.Right, ownerOrigin.Y + clip.Bottom);
        if (Active)
        {
            Surface previous = _surfaces[^1];
            GetWindowRect(previous.Window, out NativeRect previousRect);
            placement = new(previousRect.Left + clip.Left - previous.Clip.Left, previousRect.Top + clip.Top - previous.Clip.Top,
                previousRect.Left + clip.Right - previous.Clip.Left, previousRect.Top + clip.Bottom - previous.Clip.Top);
        }
        placement = FitToScreen(parent, placement);
        nint window = CreateWindowEx(0, ClassName, title, 0x00C80000, placement.Left, placement.Top,
            placement.Right - placement.Left, placement.Bottom - placement.Top, parent, 0, GetModuleHandle(null), 0);
        if (window == 0) { snapshot?.Close(); throw new Win32Exception(Marshal.GetLastWin32Error(), "创建模态窗口失败。"); }
        if (Active) _surfaces[^1].Snapshot = snapshot;
        else _background = snapshot;
        Hosts[window] = this;
        _surfaces.Add(new(key, window, clip, scale));
        // 保留系统边框和DWM合成阴影，客户区与网页表面重合。
        SetWindowPos(window, 0, 0, 0, 0, 0, 0x0020 | 0x0001 | 0x0002 | 0x0004);
        Margins margins = new() { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        _ = DwmExtendFrameIntoClientArea(window, ref margins);
        try
        {
            _live.ParentWindow = window;
            SyncLiveBounds();
            EnableWindow(parent, false);
            ShowWindow(window, 5);
            _live.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }
        catch
        {
            Pop();
            throw;
        }
    }

    private async Task<Snapshot?> CreateSnapshotAsync(nint parent, long generation, bool captureCover = false)
    {
        // 捕获开始时固定逻辑布局，跨屏期间快照只跟随它所属窗口的DPI。
        double captureScale = _scale;
        WindowFrameRect? clip = Active ? _surfaces[^1].Clip : null;
        string imageScale = captureScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using MemoryStream image = new();
        double captureMilliseconds = 0, controllerMilliseconds = 0, navigationMilliseconds = 0;
        async Task CaptureAsync()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (captureCover)
                image.Write(await ShellSurfaceCapture.CaptureAsync(_live.CoreWebView2.CallDevToolsProtocolMethodAsync));
            else
                await _live.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            captureMilliseconds = timer.Elapsed.TotalMilliseconds;
        }
        async Task<CoreWebView2Controller> CreateAsync()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            CoreWebView2Controller created = await _environment.CreateCoreWebView2ControllerAsync(parent);
            controllerMilliseconds = timer.Elapsed.TotalMilliseconds;
            try
            {
                if (_disposed || !_operations.IsCurrent(generation)) throw new InvalidOperationException("快照创建已过期。");
                // 背景首图就绪前透出仍在原窗的业务界面，避免空白控制器覆盖整窗。
                created.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                // 隐藏子窗的空页导航与PNG捕获并行；图片定尺后才装入，不提前显示。
                if (captureCover)
                {
                    created.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                    created.ShouldDetectMonitorScaleChanges = false;
                    created.RasterizationScale = captureScale;
                    var navigationTimer = System.Diagnostics.Stopwatch.StartNew();
                    ShellSnapshotNavigation documentReady = new();
                    _pendingCoverNavigation = documentReady;
                    void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args) => documentReady.Complete(args.IsSuccess);
                    created.CoreWebView2.NavigationCompleted += NavigationCompleted;
                    try
                    {
                        created.CoreWebView2.NavigateToString($"<!doctype html><html><body style='margin:0;overflow:hidden;pointer-events:none'><script>window.snapshotCaptureScale={imageScale};{ShellSnapshotPresentation.ImageSizingScript}</script></body></html>");
                        if (!await documentReady.Ready) throw new InvalidOperationException("捕获遮盖文档加载失败或已关闭。");
                    }
                    finally
                    {
                        if (ReferenceEquals(_pendingCoverNavigation, documentReady)) _pendingCoverNavigation = null;
                        created.CoreWebView2.NavigationCompleted -= NavigationCompleted;
                    }
                    navigationMilliseconds = navigationTimer.Elapsed.TotalMilliseconds;
                }
            }
            catch { created.Close(); throw; }
            return created;
        }
        // 仅隐藏遮盖子窗可以提前创建；可见背景仍先捕获，避免初始底色进入原图。
        CoreWebView2Controller? controller = await ShellSnapshotPreparation.RunAsync(
            CaptureAsync, CreateAsync, () => !_disposed && _operations.IsCurrent(generation),
            created => created.Close(), parallel: captureCover,
            isObsoleteFailure: exception => exception is COMException or InvalidOperationException);
        if (controller is null) return null;
        byte[] initialImage = image.ToArray();
        Snapshot snapshot = new(controller, initialImage);
        snapshot.CaptureMilliseconds = captureMilliseconds;
        snapshot.ControllerMilliseconds = controllerMilliseconds;
        snapshot.NavigationMilliseconds = navigationMilliseconds;
        try
        {
            // 遮盖首帧尚未合成图片时透出下层真实表单，不能显示WebView默认白底。
            if (captureCover) controller.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            controller.ShouldDetectMonitorScaleChanges = false;
            controller.RasterizationScale = captureScale;
            controller.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            controller.CoreWebView2.Settings.AreDevToolsEnabled = false;
            controller.CoreWebView2.Settings.IsStatusBarEnabled = false;
            controller.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            snapshot.Sync(parent, captureScale);
            double left = clip?.Left / captureScale ?? 0;
            double top = clip?.Top / captureScale ?? 0;
            string offsetX = left.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string offsetY = top.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string initialImageSize = ShellSnapshotPresentation.InitialImageSize(initialImage, captureScale);
            // PNG通过内存响应加载，避免高DPI截图超过NavigateToString的2MB上限。
            controller.CoreWebView2.AddWebResourceRequestedFilter("https://snapshot.augit.local/image.png*", CoreWebView2WebResourceContext.Image);
            controller.CoreWebView2.WebResourceRequested += (_, request) =>
            {
                string version = new Uri(request.Request.Uri).Query.TrimStart('?');
                long revision = version.Length == 0 ? 0 : version.StartsWith("v=", StringComparison.Ordinal) && long.TryParse(version.AsSpan(2), out long parsed) ? parsed : -1;
                MemoryStream? response = snapshot.OpenImage(revision);
                request.Response = _environment.CreateWebResourceResponse(response, response is null ? 404 : 200,
                    response is null ? "Not Found" : "OK", "Content-Type: image/png\r\nCache-Control: no-store");
            };
            controller.CoreWebView2.WebMessageReceived += (_, message) => _ = snapshot.ImageLoadedAsync(message.TryGetWebMessageAsString());
            controller.CoreWebView2.NavigationCompleted += (_, args) => { if (!args.IsSuccess) snapshot.FailLoading(); };
            if (captureCover)
            {
                string markup = $"<img data-revision='0' onerror=\"chrome.webview.postMessage('failed')\" onload=\"{ShellSnapshotPresentation.ImageLoadedScript}\" style='position:absolute;{initialImageSize}left:-{offsetX}px;top:-{offsetY}px' src='https://snapshot.augit.local/image.png'>";
                await controller.CoreWebView2.ExecuteScriptAsync($"document.body.innerHTML={System.Text.Json.JsonSerializer.Serialize(markup)};");
            }
            else controller.CoreWebView2.NavigateToString($"<!doctype html><html><body style='margin:0;overflow:hidden;pointer-events:none'><script>window.snapshotCaptureScale={imageScale};{ShellSnapshotPresentation.ImageSizingScript}</script><img data-revision='0' onerror=\"chrome.webview.postMessage('failed')\" onload=\"fitSnapshotImage(this);requestAnimationFrame(()=>requestAnimationFrame(()=>chrome.webview.postMessage('loaded:'+this.dataset.revision)))\" style='position:absolute;{initialImageSize}left:-{offsetX}px;top:-{offsetY}px' src='https://snapshot.augit.local/image.png'></body></html>");
            return snapshot;
        }
        catch
        {
            snapshot.Close();
            throw;
        }
    }

    internal void Close()
    {
        VerifyUiThread();
        _operations.Invalidate();
        if (_closing) return;
        _closing = true;
        List<Action> cleanup = [];
        cleanup.Add(ReleaseCaptureCover);
        // 在窗口线程完成等待，不能由计时器线程释放WebView2控制器。
        cleanup.Add(() => { Snapshot? pending = _pendingBackground; _pendingBackground = null; pending?.Close(); });
        for (int index = _surfaces.Count; index > 0; index--) cleanup.Add(Pop);
        cleanup.Add(() => { Snapshot? background = _background; _background = null; background?.Close(); });
        cleanup.Add(() => EnableWindow(_owner, true));
        try { ShellModalCleanup.Run([.. cleanup]); }
        finally { _closing = false; }
    }

    internal Task RefreshSnapshotAsync(int depth)
    {
        VerifyUiThread();
        return _operations.RunAsync(async generation =>
        {
            if (_disposed || !Active || depth >= _surfaces.Count) return;
            Snapshot? snapshot = depth == 0 ? _background : _surfaces[depth - 1].Snapshot;
            if (snapshot is null) return;
            // 同一业务表面直接返回PNG；不创建第二份DOM，也不更改宿主裁切或缩放。
            byte[] png = await ShellSurfaceCapture.CaptureAsync(_live.CoreWebView2.CallDevToolsProtocolMethodAsync);
            if (_disposed || !_operations.IsCurrent(generation) || depth >= _surfaces.Count
                || !ReferenceEquals(snapshot, depth == 0 ? _background : _surfaces[depth - 1].Snapshot)) return;
            bool changed = await snapshot.ReplaceAsync(png);
            if (changed && !_disposed && _operations.IsCurrent(generation)
                && depth < _surfaces.Count && ReferenceEquals(snapshot, depth == 0 ? _background : _surfaces[depth - 1].Snapshot)) _snapshotRevision++;
        });
    }

    /// <summary>背景捕获前先覆盖当前表单原图；图片确认呈现后网页才允许隐藏表单。</summary>
    internal Task CoverCaptureAsync()
    {
        VerifyUiThread();
        return _operations.RunAsync(async generation =>
        {
            if (_disposed || !Active) return;
            if (_captureCover is { } cached)
            {
                var captureTimer = System.Diagnostics.Stopwatch.StartNew();
                byte[] image = await ShellSurfaceCapture.CaptureAsync(_live.CoreWebView2.CallDevToolsProtocolMethodAsync);
                if (_disposed || !_operations.IsCurrent(generation) || !ReferenceEquals(cached, _captureCover)) return;
                _coverCaptureMilliseconds = captureTimer.Elapsed.TotalMilliseconds;
                _coverControllerMilliseconds = 0;
                _coverNavigationMilliseconds = 0;
                var loadTimer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await cached.ReplaceCoverAsync(image);
                    if (_disposed || !_operations.IsCurrent(generation) || !ReferenceEquals(cached, _captureCover)) return;
                    cached.SetVisible(true);
                    ShowCaptureCoverWindow();
                    await cached.ShowCoverAsync();
                    Marshal.ThrowExceptionForHR(DwmFlush());
                    _coverReadyMilliseconds = loadTimer.Elapsed.TotalMilliseconds;
                }
                catch { ReleaseCaptureCover(); throw; }
                return;
            }
            // 两个控制器直接共享父窗不能保证前后顺序；独立子窗明确承载遮盖层。
            nint parent = _surfaces[^1].Window;
            if (!GetClientRect(parent, out NativeRect bounds)) throw new Win32Exception("读取遮盖客户区失败。");
            // 复用无背景刷的外壳窗口类，避免Static控件在图片前先绘制系统灰底。
            _captureCoverWindow = CreateWindowEx(0x08000020, ClassName, "", 0x40000000 | 0x04000000,
                0, 0, bounds.Right, bounds.Bottom, parent, 0, GetModuleHandle(null), 0);
            if (_captureCoverWindow == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "创建捕获遮盖子窗失败。");
            // 初始隐藏且置底，图片就绪后再显式呈现，避免创建阶段抢先盖住表单。
            SetWindowPos(_captureCoverWindow, 1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            Snapshot? cover;
            try { cover = await CreateSnapshotAsync(_captureCoverWindow, generation, captureCover: true); }
            catch { ReleaseCaptureCover(); throw; }
            if (cover is null) { ReleaseCaptureCover(); return; }
            if (_disposed || !_operations.IsCurrent(generation)) { cover.Close(); return; }
            _captureCover = cover;
            _coverCaptureMilliseconds = cover.CaptureMilliseconds;
            _coverControllerMilliseconds = cover.ControllerMilliseconds;
            _coverNavigationMilliseconds = cover.NavigationMilliseconds;
            var readyTimer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!await cover.Ready) throw new InvalidOperationException("模态捕获遮盖加载失败。");
                if (_disposed || !_operations.IsCurrent(generation) || !ReferenceEquals(cover, _captureCover)) return;
                ShowCaptureCoverWindow();
                await cover.ShowCoverAsync();
                // 网页帧回调不代表桌面合成已呈现；先完成DWM队列再允许隐藏真实表单。
                Marshal.ThrowExceptionForHR(DwmFlush());
                _coverReadyMilliseconds = readyTimer.Elapsed.TotalMilliseconds;
            }
            catch { ReleaseCaptureCover(); throw; }
        });
    }

    internal void ReleaseCaptureCover()
    {
        VerifyUiThread();
        ShellSnapshotNavigation? navigation = _pendingCoverNavigation;
        _pendingCoverNavigation = null;
        Snapshot? cover = _captureCover;
        _captureCover = null;
        nint window = _captureCoverWindow;
        _captureCoverWindow = 0;
        // 先解除全部宿主引用，再同步取消等待；续体可能重入释放路径。
        try { cover?.Close(); }
        finally
        {
            try { if (window != 0) DestroyWindow(window); }
            finally { navigation?.Cancel(); }
        }
    }

    private void ShowCaptureCoverWindow()
    {
        if (_captureCoverWindow == 0) throw new InvalidOperationException("捕获遮盖子窗不存在。");
        // HWND_TOP和SWP_NOACTIVATE保证遮盖位于实时控制器上方而不改变键盘焦点。
        if (!SetWindowPos(_captureCoverWindow, 0, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "呈现捕获遮盖子窗失败。");
    }

    internal void HideCaptureCover()
    {
        VerifyUiThread();
        _captureCover?.SetVisible(false);
        if (_captureCoverWindow != 0) ShowWindow(_captureCoverWindow, 0);
    }

    private void Pop()
    {
        if (!Active) return;
        Surface current = _surfaces[^1];
        _surfaces.RemoveAt(_surfaces.Count - 1);
        nint parent = Active ? _surfaces[^1].Window : _owner;
        Snapshot? parentSnapshot = Active ? _surfaces[^1].Snapshot : _background;
        if (Active) _surfaces[^1].Snapshot = null;
        else _background = null;
        ShellModalCleanup.Run(
            ReleaseCaptureCover,
            () =>
            {
                _live.ParentWindow = parent;
                if (Active)
                {
                    Surface previous = _surfaces[^1];
                    RescaleViewport(previous.Scale);
                    _live.RasterizationScale = _scale;
                    SetWindowPos(parent, 0, 0, 0, previous.Clip.Right - previous.Clip.Left, previous.Clip.Bottom - previous.Clip.Top, 0x0002 | 0x0004);
                }
                SyncLiveBounds();
            },
            () => parentSnapshot?.Close(),
            () => current.Snapshot?.Close(),
            () => { Hosts.Remove(current.Window); DestroyWindow(current.Window); },
            () => { EnableWindow(parent, true); SetForegroundWindow(parent); },
            () => _live.MoveFocus(CoreWebView2MoveFocusReason.Programmatic));
    }

    private void SyncLiveBounds()
    {
        if (Active)
        {
            WindowFrameRect clip = _surfaces[^1].Clip;
            _live.Bounds = new(-clip.Left, -clip.Top, _viewportWidth, _viewportHeight);
        }
        else if (GetClientRect(_owner, out NativeRect rect))
        {
            if (!_fixedScale && GetDpiForWindow(_owner) is > 0 and var dpi) _live.RasterizationScale = dpi / 96.0;
            _live.Bounds = new(0, 0, rect.Right, rect.Bottom);
        }
    }

    internal void BeginDrag()
    {
        if (!Active) return;
        int anchor = GetCursorPos(out Point cursor) ? (cursor.Y << 16) | (cursor.X & 0xFFFF) : 0;
        ReleaseCapture();
        SendMessage(_surfaces[^1].Window, 0x00A1, 2, anchor);
    }

    internal object State()
    {
        nint window = Active ? _surfaces[^1].Window : 0;
        GetWindowRect(window, out NativeRect rect);
        return new
        {
            active = Active,
            depth = _surfaces.Count,
            owner = (long)_owner,
            window = (long)window,
            key = Active ? _surfaces[^1].Key : null,
            ownerEnabled = IsWindowEnabled(_owner),
            visible = IsWindowVisible(window),
            ownerWindow = (long)GetWindow(window, 4),
            parentWindow = (long)_live.ParentWindow,
            liveBounds = new { x = _live.Bounds.X, y = _live.Bounds.Y, width = _live.Bounds.Width, height = _live.Bounds.Height },
            snapshotCount = (_background is null ? 0 : 1) + _surfaces.Count(surface => surface.Snapshot is not null),
            snapshotRevision = _snapshotRevision,
            captureCoverReady = _captureCover is { Visible: true, LoadedRevision: >= 0 },
            captureCoverBytes = _captureCover?.StreamBytes ?? 0,
            captureCoverWindow = (long)_captureCoverWindow,
            captureCoverWindowVisible = _captureCoverWindow != 0 && IsWindowVisible(_captureCoverWindow),
            // 记录实际兄弟窗口顺序，不能用图片已加载代替遮盖确实位于最上层。
            captureCoverTopmostChild = _captureCoverWindow != 0 && GetWindow(window, 5) == _captureCoverWindow,
            liveControllerVisible = _live.IsVisible,
            captureCoverTiming = new { captureMilliseconds = _coverCaptureMilliseconds, controllerMilliseconds = _coverControllerMilliseconds, navigationMilliseconds = _coverNavigationMilliseconds, readyMilliseconds = _coverReadyMilliseconds },
            backgroundHash = _background?.Hash,
            backgroundLoadedRevision = _background?.LoadedRevision,
            backgroundImageRevision = _background?.ImageRevision,
            snapshotStreamBytes = (_background?.StreamBytes ?? 0) + _surfaces.Sum(surface => surface.Snapshot?.StreamBytes ?? 0),
            rect = new { x = rect.Left, y = rect.Top, width = rect.Right - rect.Left, height = rect.Bottom - rect.Top }
        };
    }

    public void Dispose()
    {
        VerifyUiThread();
        if (_disposed) return;
        _disposed = true;
        // Win32消息过程不能接收托管异常；清理全部执行后仅记录退出故障。
        try { Close(); }
        catch (Exception exception) { System.Diagnostics.Trace.TraceError("关闭模态宿主失败：{0}", exception); }
        finally { _operations.Dispose(); }
    }

    private void VerifyUiThread()
    {
        if (Environment.CurrentManagedThreadId != _uiThread) throw new InvalidOperationException("模态宿主只能在窗口线程调用。");
    }

    internal void SyncOwnerSnapshot()
    {
        try
        {
            double scale = !_fixedScale && GetDpiForWindow(_owner) is > 0 and var dpi ? dpi / 96.0 : _scale;
            _background?.Sync(_owner, scale);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("同步所有者快照失败：{0}", exception);
            Dispose();
        }
    }

    private void RescaleViewport(double scale)
    {
        _viewportWidth = (int)Math.Round(_viewportWidth * scale / _scale);
        _viewportHeight = (int)Math.Round(_viewportHeight * scale / _scale);
        _scale = scale;
    }

    private void OnDpiChanged(nint window, double scale, nint lParam)
    {
        ReleaseCaptureCover();
        Surface? surface = _surfaces.Find(item => item.Window == window);
        if (surface is null || scale <= 0) return;
        surface.Clip = ShellModalGeometry.Rescale(surface.Clip, surface.Scale, scale);
        surface.Scale = scale;
        NativeRect suggested = Marshal.PtrToStructure<NativeRect>(lParam);
        WindowFrameRect fitted = FitToScreen(window, new(suggested.Left, suggested.Top, suggested.Right, suggested.Bottom));
        SetWindowPos(window, 0, fitted.Left, fitted.Top, fitted.Right - fitted.Left, fitted.Bottom - fitted.Top, 0x0004);
        surface.Snapshot?.Sync(window, scale);
        if (surface != _surfaces[^1]) return;
        _operations.Invalidate();
        RescaleViewport(scale);
        _live.RasterizationScale = scale;
        SyncLiveBounds();
        _scaleChanged();
    }

    private static WindowFrameRect FitToScreen(nint window, WindowFrameRect rect)
    {
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(window, 2), ref info)
            ? ShellModalGeometry.Fit(rect, new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom)) : rect;
    }

    private static nint ProcessMessage(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == 0x0083) return 0;
        if (Hosts.TryGetValue(window, out ShellModalHost? host))
        {
            try
            {
                if (message == 0x0010) { host._cancel(); return 0; }
                if (message == 0x0084) return 1;
                if (message == 0x02E0 && !host._fixedScale && lParam != 0)
                {
                    host.OnDpiChanged(window, ((long)wParam & 0xFFFF) / 96.0, lParam);
                    return 0;
                }
                if (message == 0x0005 && host._surfaces.Find(surface => surface.Window == window) is { } resized)
                    resized.Snapshot?.Sync(window, resized.Scale);
                if (message == 0x0003 && host.Active && host._surfaces[^1].Window == window) host._live.NotifyParentWindowPositionChanged();
                if (message == 0x0232 && GetWindowRect(window, out NativeRect rect))
                {
                    WindowFrameRect fitted = FitToScreen(window, new(rect.Left, rect.Top, rect.Right, rect.Bottom));
                    SetWindowPos(window, 0, fitted.Left, fitted.Top, fitted.Right - fitted.Left, fitted.Bottom - fitted.Top, 0x0004);
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("模态窗口消息处理失败：{0}", exception);
                host.Dispose();
                return 0;
            }
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private static void RegisterClass()
    {
        if (_registered) return;
        WindowClass windowClass = new()
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            // DWM已负责系统阴影；CS_DROPSHADOW会额外叠加右下4像素深色投影。
            Style = 0,
            Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = GetModuleHandle(null),
            ClassName = ClassName
        };
        if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "注册模态窗口失败。");
        _registered = true;
    }

    private sealed class Surface(string key, nint window, WindowFrameRect clip, double scale)
    {
        internal string Key { get; } = key;
        internal nint Window { get; } = window;
        internal WindowFrameRect Clip { get; set; } = clip;
        internal double Scale { get; set; } = scale;
        internal Snapshot? Snapshot { get; set; }
    }

    private sealed class Snapshot(CoreWebView2Controller controller, byte[] png) : IDisposable
    {
        private bool _closed;
        internal double CaptureMilliseconds { get; set; }
        internal double ControllerMilliseconds { get; set; }
        internal double NavigationMilliseconds { get; set; }
        private readonly TaskCompletionSource<bool> _ready = new();
        private TaskCompletionSource<bool>? _replacementReady;
        private TaskCompletionSource<bool>? _presentationReady;
        private long _replacementRevision;
        internal bool Visible { get; private set; } = true;
        internal void SetVisible(bool visible)
        {
            if (_closed) return;
            controller.IsVisible = visible;
            Visible = visible;
        }
        internal Task<bool> Ready => _ready.Task;
        internal async Task ShowCoverAsync()
        {
            if (_closed) return;
            TaskCompletionSource<bool> presentation = new();
            _presentationReady = presentation;
            try
            {
                SetVisible(true);
                await controller.CoreWebView2.ExecuteScriptAsync(ShellSnapshotPresentation.Script);
                if (!await presentation.Task) throw new InvalidOperationException("捕获遮盖呈现失败。");
            }
            finally { _presentationReady = null; }
        }
        // 初始背景与遮盖均等待Ready；失败值交给各自生命周期处理。
        internal void FailLoading()
        {
            _images.FailLoading();
            _ready.TrySetResult(false);
            _replacementReady?.TrySetResult(false);
            _presentationReady?.TrySetResult(false);
        }
        private readonly ShellSnapshotImages _images = new(png);
        internal long LoadedRevision => _images.LoadedRevision;
        internal long ImageRevision => _images.Revision;
        internal long StreamBytes => _images.Bytes;
        internal string Hash => _images.Hash;
        internal MemoryStream? OpenImage(long revision) => _images.Open(revision);
        internal async Task ReplaceCoverAsync(byte[] image)
        {
            if (_closed) return;
            long revision = _images.Add(image);
            if (revision == _images.LoadedRevision) return;
            _replacementRevision = revision;
            TaskCompletionSource<bool> replacement = new();
            _replacementReady = replacement;
            try
            {
                await LoadImageAsync(revision);
                if (!await replacement.Task) throw new InvalidOperationException("更新捕获遮盖失败。");
            }
            finally { _replacementReady = null; }
        }
        internal async Task<bool> ReplaceAsync(byte[] image)
        {
            if (_closed) return false;
            string previousHash = _images.Hash;
            // 宿主操作已串行；同图但上次加载失败时仍须重试，不能把在途合并当作完成。
            long revision = _images.Add(image);
            if (revision != _images.LoadedRevision)
            {
                Task<bool> loaded = _images.WaitForLoadedAsync(revision);
                try { await LoadImageAsync(revision); }
                catch { _images.FailLoading(); throw; }
                // ExecuteScript完成只说明设置了src；收到对应onload前不能恢复表单并撤盖。
                if (!await loaded && !_closed) throw new InvalidOperationException("模态背景图片加载失败。");
            }
            return !_closed && previousHash != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image));
        }
        private async Task LoadImageAsync(long revision)
        {
            if (_closed) return;
            // 新图加载完再替换旧图；每个onload绑定自己的版本，迟到事件不会误报最新图。
            await controller.CoreWebView2.ExecuteScriptAsync($"(()=>{{const previous=document.querySelector('img');const next=previous.cloneNode(false);next.dataset.revision='{revision}';next.onload=()=>{{next.onload=null;next.onerror=null;if({revision}!==window.latestRevision)return;fitSnapshotImage(next);previous.onload=null;previous.onerror=null;previous.replaceWith(next);previous.removeAttribute('src');chrome.webview.postMessage('loaded:{revision}');}};next.onerror=()=>{{next.onload=null;next.onerror=null;chrome.webview.postMessage('failed');}};window.latestRevision={revision};next.src='https://snapshot.augit.local/image.png?v={revision}';}})()");
        }
        internal async Task ImageLoadedAsync(string message)
        {
            if (message == "presented")
            {
                if (!_closed) _presentationReady?.TrySetResult(true);
                return;
            }
            if (message == "failed") { FailLoading(); return; }
            if (_closed || !message.StartsWith("loaded:", StringComparison.Ordinal) || !long.TryParse(message.AsSpan(7), out long revision)) return;
            _images.Loaded(revision);
            _ready.TrySetResult(true);
            if (revision == _replacementRevision) _replacementReady?.TrySetResult(true);
            if (_images.TakePending() is not { } next) return;
            try { await LoadImageAsync(next); }
            catch (Exception exception) when (_closed || exception is COMException or InvalidOperationException)
            {
                System.Diagnostics.Trace.TraceError("更新模态背景失败：{0}", exception);
            }
        }
        internal void Sync(nint window, double scale)
        {
            if (_closed) return;
            controller.RasterizationScale = scale;
            if (GetClientRect(window, out NativeRect rect)) controller.Bounds = new(0, 0, rect.Right, rect.Bottom);
        }
        internal void Close()
        {
            if (_closed) return;
            _closed = true;
            // 同步完成续体以保留窗口线程；先从宿主解除引用，避免重入再次释放。
            try { controller.Close(); }
            finally
            {
                _images.Dispose();
                _ready.TrySetResult(true);
                _replacementReady?.TrySetResult(true);
                _presentationReady?.TrySetResult(true);
            }
        }
        public void Dispose() => Close();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style; public nint Procedure; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background; public string? MenuName, ClassName; public nint SmallIcon;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)] private static extern nint CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int width, int height, nint owner, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint window, bool enable);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, int flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")] private static extern nint GetModuleHandle(string? name);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
