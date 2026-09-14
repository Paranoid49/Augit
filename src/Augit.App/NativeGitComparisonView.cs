using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

/// <summary>
/// 编辑区中的引用比较视图。
/// 负责局部查询反馈与比较结果呈现，不直接拥有底部 Git 历史的选择状态。
/// </summary>
internal sealed class NativeGitComparisonView : IDisposable
{
    private const string WindowClassName = "Augit.GitComparisonView.Native";
    private static int ToolbarHeight => Math.Max(NativeTheme.Scale(39), NativeTheme.UiLineHeight + NativeTheme.Scale(12));
    private int FileBarHeight => NativeDiffFileHeader.Height(_headerSideBySide);
    private int ContentTop => ToolbarHeight + FileBarHeight;
    private static int GutterWidth => NativeTheme.Scale(84);
    private const int CommandPreviousChange = 10;
    private const int CommandNextChange = 11;
    private const int CommandSearch = 12;
    private const int CommandUnified = 13;
    private const int CommandSideBySide = 14;
    private const int CommandIgnoreWhitespace = 15;
    private const int CommandSettings = 16;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitComparisonView> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly CompositeFormat ChangeCountFormat = CompositeFormat.Parse(UiText.DiffChangeCount);
    private static bool _classRegistered;

    private ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly Func<GitComparisonDocument, bool, CancellationToken, Task<GitComparisonResult>> _reload;
    private readonly List<int> _changedLines = [];
    private readonly nint[] _controls = new nint[7];
    private ScintillaControl? _unified;
    private ScintillaControl? _oldSide;
    private ScintillaControl? _gutter;
    private ScintillaControl? _newSide;
    private NativeToolTip? _toolTip;
    private NativeContextMenu? _contextMenu;
    private nint _fileBar;
    private NativeMethods.Rectangle _modeGroupBounds;
    private nint _statusLabel;
    private nint _changeSummary;
    private nint _noticeBody;
    private nint _controlBrush;
    private GitComparisonDocument? _document;
    private string _unifiedText = string.Empty;
    private string _oldText = string.Empty;
    private string _gutterText = string.Empty;
    private string _newText = string.Empty;
    private string _baseRevisionText = string.Empty;
    private string _pathText = string.Empty;
    private string _targetRevisionText = string.Empty;
    private bool _sideBySide = true;
    private bool _headerSideBySide = true;
    private bool _ignoreWhitespace;
    private bool _documentIgnoreWhitespace;
    private bool _queryPending;
    private bool _loading;
    private bool _noticeLoading;
    private bool _hasText;
    private bool _failed;
    private bool _displayedSideBySide;
    private int _requestVersion;
    private CancellationTokenSource? _workCancellation;
    private CancellationTokenSource? _loadingCancellation;
    private int _changeNavigationIndex = -1;
    private int _renderVersion;
    private bool _synchronizingScroll;
    private int _gutterWidth = GutterWidth;
    private bool _disposed;

    internal NativeGitComparisonView(
        nint parent,
        ApplicationSettings settings,
        Action<string> setStatus,
        Func<GitComparisonDocument, bool, CancellationToken, Task<GitComparisonResult>> reload)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(reload);
        _settings = settings;
        _setStatus = setStatus;
        _reload = reload;
        EnsureWindowClass();
        Handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitTextWindowCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(Handle, this);
        }

        CreateControls();
        ApplyAppearance();
        Layout();
        SetNotice(UiText.SelectCommitFile);
        SetVisible(false);
    }

    internal nint Handle { get; private set; }

    internal bool IsVisible => Handle != 0 && NativeMethods.IsWindowVisible(Handle);

    internal bool HasDocument => _document is not null;

    internal bool IsReadOnly => _unified?.IsReadOnly == true && _oldSide?.IsReadOnly == true
        && _gutter?.IsReadOnly == true && _newSide?.IsReadOnly == true;

    internal bool HasFailed => _failed;

    internal bool IsBusy => _loading || _queryPending;

    // 工具窗口暂时隐藏或改选时中止旧查询与排版，已完成的正文和阅读位置继续保留。
    internal void CancelPendingWork()
    {
        if (_disposed) return;
        bool pending = IsBusy;
        InvalidatePendingWork();
        _contextMenu?.Dispose();
        _contextMenu = null;
        if (pending) _failed = true;
    }

    internal bool UsesSideBySideForTest => _sideBySide;

    internal string FileBarTextForTest => NativeMethods.GetWindowTextValue(_fileBar);

    internal (string Base, string Target) DisplayedRevisionsForTest => (_baseRevisionText, _targetRevisionText);

    internal bool FileBarToolTipCreatedForTest => _toolTip?.ContainsForTest(_fileBar) == true;

    internal bool SettingsButtonUsesIconForTest => _controls[6] != 0
        && NativeMethods.GetWindowTextValue(_controls[6]).Length == 0
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
                _controls[6],
                NativeMethods.WindowLongStyle).ToInt64())
            & NativeMethods.ButtonOwnerDraw) == NativeMethods.ButtonOwnerDraw;

    internal NativeMethods.Rectangle ModeGroupBoundsForTest => _modeGroupBounds;

    internal bool ModeButtonsUseCompactIconsForTest => Enumerable.Range(3, 3)
        .All(index => _controls[index] != 0
            && NativeMethods.GetWindowTextValue(_controls[index]).Length == 0
            && NativeMethods.GetWindowRectangle(_controls[index], out NativeMethods.Rectangle rectangle)
            && Math.Abs(rectangle.Right - rectangle.Left - NativeTheme.Scale(index == 5 ? 27 : 38)) <= 1);

    internal bool ToolbarToolTipsCreatedForTest => _toolTip is not null
        && _controls.All(_toolTip.ContainsForTest);

    internal static int ToolbarHeightForTest => ToolbarHeight;

    internal static int FileBarHeightForTest => NativeDiffFileHeader.Height(true);

    internal static int ContentTopForTest => ToolbarHeight + FileBarHeightForTest;

    internal nint FileBarHandleForTest => _fileBar;

    internal bool FileHeaderSideBySideForTest => _headerSideBySide;

    internal static int GutterWidthForTest => GutterWidth;

    internal string GutterTextForTest => _gutterText;

    internal int ChangedLineCountForTest => _changedLines.Count;

    internal bool LoadingForTest => _loading;

    internal int RenderCountForTest { get; private set; }

    internal int LayoutCountForTest { get; private set; }

    internal Task? RenderBarrierForTest { get; set; }

    internal string BodyTextForTest => _hasText && _displayedSideBySide ? _newText : _unifiedText;

    internal string LoadingTextForTest => NativeMethods.GetWindowTextValue(_statusLabel);

    internal nint NoticeHandleForTest => _noticeBody;

    internal bool IgnoreWhitespaceForTest => _ignoreWhitespace;

    internal bool NavigationEnabledForTest => NativeMethods.IsWindowEnabled(_controls[0]);

    internal nint ToolbarButtonForTest(int index) => _controls[index];

    internal nint ChangeSummaryHandleForTest => _changeSummary;

    internal string ChangeSummaryForTest => NativeMethods.GetWindowTextValue(_changeSummary);

    internal nint[] TextHandlesForTest => [_unified!.Handle, _oldSide!.Handle, _gutter!.Handle, _newSide!.Handle];

    internal (int Left, int Right) TextPaddingForTest => _newSide?.TextPaddingForTest ?? (0, 0);

    internal void ClickChangeForTest(int direction) => ClickControlForTest(direction < 0 ? 0 : 1);

    internal void ClickModeForTest(bool sideBySide) => ClickControlForTest(sideBySide ? 4 : 3);

    internal void ClickIgnoreWhitespaceForTest() => ClickControlForTest(5);

    private void ClickControlForTest(int index) =>
        _ = NativeMethods.SendMessage(_controls[index], 0x00F5, 0, 0);

    internal static string BuildFileBarTextForTest(GitComparisonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string target = string.IsNullOrWhiteSpace(document.TargetRevision)
            ? "工作区"
            : document.TargetRevision!;
        string path = string.IsNullOrWhiteSpace(document.RelativePath)
            ? "全部文件"
            : document.RelativePath!;
        return $"{document.BaseRevision}  →  {target}    {path}";
    }

    internal static (
        string OldText,
        string GutterText,
        string NewText,
        IReadOnlyList<int> ChangedLines) RenderSideBySideForTest(string patch)
    {
        var rendered = RenderSideBySide(patch);
        return (
            rendered.OldText,
            rendered.GutterText,
            rendered.NewText,
            rendered.ChangedLines);
    }

    internal static string ResolveNoticeForTest(GitDiffContentStatus status)
    {
        return ResolveNotice(status);
    }

    internal static IReadOnlyList<int> RenderUnifiedChangeStartsForTest(string patch)
    {
        return RenderUnified(patch).ChangedLines;
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        if (Handle == 0)
        {
            return;
        }

        _ = NativeMethods.MoveWindow(Handle, x, y, Math.Max(0, width), Math.Max(0, height), true);
        Layout();
    }

    internal void SetVisible(bool visible)
    {
        if (Handle != 0)
        {
            _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
    }

    internal bool ContainsWindow(nint window) => NativeFocusNavigation.ContainsWindow(Handle, window);

    internal nint[] FocusTargets => [_controls[0], _controls[1], _controls[2], _controls[5], _controls[4],
        _controls[3], _controls[6], _unified?.Handle ?? 0, _oldSide?.Handle ?? 0,
        _newSide?.Handle ?? 0, _noticeBody];

    internal bool HandleTabNavigation(bool backwards, nint documentTabs) =>
        NativeFocusNavigation.MoveWithinRegion([documentTabs, .. FocusTargets], NativeMethods.GetFocus(), backwards);

    internal bool HandleShortcut(NativeMethods.Message message)
    {
        if (_disposed || !IsVisible || message.MessageId != NativeMethods.WindowMessageKeyDown
            || unchecked((int)message.WordParameter) != NativeMethods.VirtualKeyEnter
            || !_controls.Contains(message.Window) || NativeMethods.GetFocus() != message.Window
            || !NativeMethods.IsWindowEnabled(message.Window)
            || NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) < 0
            || NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0
            || NativeMethods.GetKeyState(0x12) < 0)
        {
            return false;
        }
        _ = NativeMethods.SendMessage(message.Window, 0x00F5, 0, 0);
        return true;
    }

    internal void SetResult(GitComparisonResult result)
    {
        if (_disposed)
        {
            return;
        }

        if (result.IsSuccess && result.Document is not null && result.Document == _document
            && !_loading && !_ignoreWhitespace && !_failed)
        {
            return;
        }
        InvalidatePendingWork();
        _ignoreWhitespace = false;
        _documentIgnoreWhitespace = false;
        _failed = false;
        InvalidateModeButtons();
        if (!result.IsSuccess || result.Document is null)
        {
            _document = null;
            _failed = true;
            SetNotice(result.ErrorMessage ?? UiText.GenerateDiffFailed);
            return;
        }

        SetDocumentIdentity(result.Document);
        RenderCurrent();
    }

    internal bool CanReuseDocument(GitComparisonDocument document) =>
        !_disposed && !_loading && !_failed && _document is not null
        && _document.BaseRevision == document.BaseRevision
        && _document.TargetRevision == document.TargetRevision
        && _document.RelativePath == document.RelativePath;

    internal void ShowIdentityNotice(GitComparisonDocument document, string message)
    {
        if (_disposed) return;
        InvalidatePendingWork();
        SetDocumentIdentity(document);
        _failed = true;
        SetNotice(message);
    }

    internal async Task LoadAsync(
        GitComparisonDocument document,
        Func<CancellationToken, Task<GitComparisonResult>> load,
        CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        InvalidatePendingWork();
        SetDocumentIdentity(document);
        _ignoreWhitespace = false;
        _documentIgnoreWhitespace = false;
        _failed = false;
        _queryPending = true;
        InvalidateModeButtons();
        if (!_hasText)
        {
            SetNotice(string.Empty);
        }

        int version = _requestVersion;
        _workCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource queryCancellation = _workCancellation;
        CancellationToken token = queryCancellation.Token;
        BeginLoading();
        try
        {
            GitComparisonResult result = await load(token).WaitAsync(token);
            if (_disposed || version != _requestVersion || token.IsCancellationRequested)
            {
                return;
            }

            _queryPending = false;
            if (!result.IsSuccess || result.Document is null)
            {
                _failed = true;
                SetNotice(result.ErrorMessage ?? UiText.GenerateDiffFailed);
                return;
            }

            SetDocumentIdentity(result.Document);
            RenderCurrent();
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && version == _requestVersion)
            {
                _queryPending = false;
                _failed = true;
                SetNotice(UiText.ComparisonCancelled);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_disposed && version == _requestVersion)
            {
                _queryPending = false;
                _failed = true;
                SetNotice(UiText.GenerateDiffFailed);
            }
        }
        finally
        {
            if (ReferenceEquals(_workCancellation, queryCancellation))
            {
                _workCancellation = null;
                queryCancellation.Dispose();
            }
        }
    }

    private void SetDocumentIdentity(GitComparisonDocument document)
    {
        _document = document;
        _baseRevisionText = NativeGitComparisonCaption.ShortenRevision(document.BaseRevision);
        _targetRevisionText = string.IsNullOrWhiteSpace(document.TargetRevision)
            ? "工作区"
            : NativeGitComparisonCaption.ShortenRevision(document.TargetRevision!);
        _pathText = string.IsNullOrWhiteSpace(document.RelativePath)
            ? "全部文件"
            : document.RelativePath!;
        _ = NativeMethods.SetWindowText(_fileBar, BuildFileBarTextForTest(document));
        _toolTip?.Update(_fileBar, BuildFileBarTextForTest(document));
    }

    internal void Clear()
    {
        ShowMessage(string.Empty);
        SetVisible(false);
    }

    internal void ShowMessage(string text)
    {
        if (_disposed) return;
        InvalidatePendingWork();
        _document = null;
        _baseRevisionText = _pathText = _targetRevisionText = string.Empty;
        _ = NativeMethods.SetWindowText(_fileBar, string.Empty);
        SetNotice(text);
    }

    internal void ApplyAppearance(ApplicationSettings? settings = null)
    {
        if (settings is not null) _settings = settings;
        if (Handle == 0)
        {
            return;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(Handle, dark);
        foreach (nint control in _controls)
        {
            NativeTheme.ApplyToControl(control, dark);
        }
        NativeTheme.ApplyToControl(_fileBar, dark);
        NativeTheme.ApplyToControl(_statusLabel, dark);
        NativeTheme.ApplyToControl(_changeSummary, dark);
        NativeTheme.ApplyToControl(_noticeBody, dark);
        _unified?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        _oldSide?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        _gutter?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark, mutedText: true);
        _newSide?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        NativeTheme.ApplyDiffLineColors(_unified, _oldSide, _newSide, dark);
        ApplyComparisonTextPadding();
        UpdateGutterWidth();
        Layout();
        _toolTip?.ApplyAppearance(dark);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(dark).Panel);
        _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        InvalidatePendingWork();
        _document = null;
        _unifiedText = _oldText = _gutterText = _newText = string.Empty;
        _changedLines.Clear();
        _toolTip?.Dispose();
        _toolTip = null;
        _contextMenu?.Dispose();
        _contextMenu = null;
        _unified?.Dispose();
        _oldSide?.Dispose();
        _gutter?.Dispose();
        _newSide?.Dispose();
        _unified = null;
        _oldSide = null;
        _gutter = null;
        _newSide = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }
        nint handle = Handle;
        Handle = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }
        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            NativeMethods.WindowClass windowClass = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                Style = NativeMethods.ClassRedrawOnHorizontalChange | NativeMethods.ClassRedrawOnVerticalChange,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                Background = NativeMethods.GetSystemColorBrush(NativeMethods.ColorWindow),
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, UiText.GitTextWindowClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeGitComparisonView? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        switch (message)
        {
            case NativeMethods.WindowMessageSetFocus:
                _ = NativeMethods.SetFocus(instance._hasText
                    ? instance._displayedSideBySide ? instance._newSide!.Handle : instance._unified!.Handle
                    : instance._noticeBody);
                return 0;
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
            case NativeMethods.WindowMessageDrawItem:
                return instance.DrawControl(longParameter) ? 1 : 0;
            case NativeMethods.WindowMessageNotify:
                instance.SynchronizeVerticalScroll(longParameter);
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return instance.PaintBackground(unchecked((nint)wordParameter));
            case NativeMethods.WindowMessageControlColorButton:
            case NativeMethods.WindowMessageControlColorStatic:
                return instance.ApplyControlColor(unchecked((nint)wordParameter));
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        _controls[0] = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandPreviousChange);
        _controls[1] = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandNextChange);
        _controls[2] = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandSearch);
        _controls[3] = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandUnified);
        _controls[4] = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandSideBySide);
        _controls[5] = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandIgnoreWhitespace);
        _controls[6] = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandSettings);
        _fileBar = CreateControl(NativeMethods.StaticClass, string.Empty, 40);
        _statusLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 41);
        _noticeBody = CreateControl(NativeMethods.StaticClass, string.Empty, 42);
        _changeSummary = CreateControl(NativeMethods.StaticClass, string.Empty, 43);
        _unified = new(Handle, 50);
        _oldSide = new(Handle, 51);
        _gutter = new(Handle, 52);
        _newSide = new(Handle, 53);
        _unified.SetWordWrap(false);
        _oldSide.SetWordWrap(false);
        _gutter.SetWordWrap(false);
        _newSide.SetWordWrap(false);
        _unified.SetLineNumbersVisible(false);
        _oldSide.SetLineNumbersVisible(false);
        _gutter.SetLineNumbersVisible(false);
        _newSide.SetLineNumbersVisible(false);
        _gutter.SetScrollBarsVisible(horizontal: false, vertical: false);
        _oldSide.SetVisible(false);
        _gutter.SetVisible(false);
        _newSide.SetVisible(false);
        _toolTip = new NativeToolTip(Handle);
        _toolTip.Add(_controls[0], UiText.PreviousChange);
        _toolTip.Add(_controls[1], UiText.NextChange);
        _toolTip.Add(_controls[2], UiText.Find);
        _toolTip.Add(_controls[3], UiText.UnifiedDiff);
        _toolTip.Add(_controls[4], UiText.SideBySideDiff);
        _toolTip.Add(_controls[5], UiText.IgnoreWhitespace);
        _toolTip.Add(_controls[6], UiText.Settings);
        _toolTip.Add(_fileBar, UiText.GeneratingDiff);
    }

    private nint CreateControl(string className, string text, int identifier)
    {
        uint specificStyle = className.Equals(NativeMethods.ButtonClass, StringComparison.Ordinal)
            ? NativeMethods.ButtonOwnerDraw
            : NativeMethods.StaticOwnerDraw;
        if (identifier == 40)
        {
            // SS_NOTIFY 让文件栏收到鼠标消息，以显示包含完整引用和路径的悬停说明。
            specificStyle |= 0x0100;
        }
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | (className == NativeMethods.ButtonClass || identifier == 42 ? NativeMethods.WindowStyleTabStop : 0)
                | specificStyle,
            0,
            0,
            0,
            0,
            Handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitTextWindowCreateFailed);
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        return control;
    }

    private void HandleCommand(nuint wordParameter)
    {
        if (NativeMethods.HighWord(wordParameter) != 0 || _disposed)
        {
            return;
        }
        int command = NativeMethods.LowWord(wordParameter);
        switch (command)
        {
            case CommandUnified:
                ChangeMode(false);
                break;
            case CommandSideBySide:
                ChangeMode(true);
                break;
            case CommandIgnoreWhitespace:
                _ = ToggleIgnoreWhitespaceAsync();
                break;
            case CommandPreviousChange:
                NavigateChange(-1);
                break;
            case CommandNextChange:
                NavigateChange(1);
                break;
            case CommandSearch:
                ShowSearch();
                break;
            case CommandSettings:
                ShowSettingsMenu();
                break;
        }
    }

    private async Task RenderAsync(int version, GitComparisonDocument document, CancellationToken cancellationToken)
    {
        try
        {
            RenderCountForTest++;
            bool sideBySide = _sideBySide;
            if (RenderBarrierForTest is { } barrier)
            {
                RenderBarrierForTest = null;
                await barrier.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (document.Status != GitDiffContentStatus.Ready || string.IsNullOrEmpty(document.UnifiedPatch))
            {
                string notice = ResolveNotice(document.Status);
                if (version == _renderVersion && !_disposed)
                {
                    SetNotice(notice);
                }
                return;
            }

            string patch = document.UnifiedPatch;
            bool IsCurrent() => version == _renderVersion && !_disposed;
            bool restoreBodyFocus;
            if (sideBySide)
            {
                var rendered = await Task.Run(
                    () => RenderSideBySide(patch),
                    cancellationToken);
                if (version != _renderVersion || _disposed)
                {
                    return;
                }

                restoreBodyFocus = HasBodyFocus();
                _oldText = rendered.OldText;
                _gutterText = rendered.GutterText;
                _newText = rendered.NewText;
                _oldSide!.SetTextContent(rendered.OldText);
                _gutter!.SetTextContent(rendered.GutterText);
                _newSide!.SetTextContent(rendered.NewText);
                var removed = NativeTheme.DiffLineBackground(added: false, NativeTheme.IsDark(_settings.Theme));
                var added = NativeTheme.DiffLineBackground(added: true, NativeTheme.IsDark(_settings.Theme));
                if (!await _oldSide.SetLineBackgroundsAsync(0, rendered.OldText, rendered.OldBackgrounds,
                        removed.Red, removed.Green, removed.Blue, IsCurrent, cancellationToken)
                    || !await _newSide.SetLineBackgroundsAsync(0, rendered.NewText, rendered.NewBackgrounds,
                        added.Red, added.Green, added.Blue, IsCurrent, cancellationToken)
                    || !await _oldSide.SetHighlightsAsync(0, rendered.OldText, rendered.OldHighlights,
                        219, 88, 96, IsCurrent, cancellationToken)
                    || !await _newSide.SetHighlightsAsync(0, rendered.NewText, rendered.NewHighlights,
                        76, 175, 80, IsCurrent, cancellationToken))
                {
                    return;
                }
                restoreBodyFocus = HasBodyFocus();
                UpdateGutterWidth();
                ApplyFileHeaderMode(sideBySide);
                LayoutContent();
                _oldSide.SetVisible(true);
                _gutter.SetVisible(true);
                _newSide.SetVisible(true);
                _unified!.SetVisible(false);
                _unified.SetTextContent(string.Empty);
                _unifiedText = string.Empty;
                _changedLines.Clear();
                _changedLines.AddRange(rendered.ChangedLines);
            }
            else
            {
                var rendered = await Task.Run(
                    () => RenderUnified(patch),
                    cancellationToken);
                if (version != _renderVersion || _disposed)
                {
                    return;
                }

                restoreBodyFocus = HasBodyFocus();
                _unifiedText = rendered.Text;
                _unified!.SetTextContent(rendered.Text);
                var removed = NativeTheme.DiffLineBackground(added: false, NativeTheme.IsDark(_settings.Theme));
                var added = NativeTheme.DiffLineBackground(added: true, NativeTheme.IsDark(_settings.Theme));
                if (!await _unified.SetLineBackgroundsAsync(0, rendered.Text, rendered.RemovedHighlights,
                        removed.Red, removed.Green, removed.Blue, IsCurrent, cancellationToken)
                    || !await _unified.SetLineBackgroundsAsync(1, rendered.Text, rendered.AddedHighlights,
                        added.Red, added.Green, added.Blue, IsCurrent, cancellationToken)
                    || !await _unified.SetDiffStylesAsync(rendered.Text, rendered.LineNumbers,
                        rendered.RemovedMarkers, rendered.AddedMarkers, IsCurrent, cancellationToken)
                    || !await _unified.SetHighlightsAsync(0, rendered.Text, rendered.RemovedHighlights,
                        219, 88, 96, IsCurrent, cancellationToken)
                    || !await _unified.SetHighlightsAsync(1, rendered.Text, rendered.AddedHighlights,
                        76, 175, 80, IsCurrent, cancellationToken))
                {
                    return;
                }
                restoreBodyFocus = HasBodyFocus();
                ApplyFileHeaderMode(sideBySide);
                _unified.SetVisible(true);
                _oldSide!.SetVisible(false);
                _gutter!.SetVisible(false);
                _newSide!.SetVisible(false);
                _oldSide.SetTextContent(string.Empty);
                _gutter.SetTextContent(string.Empty);
                _newSide.SetTextContent(string.Empty);
                _oldText = _gutterText = _newText = string.Empty;
                _changedLines.Clear();
                _changedLines.AddRange(rendered.ChangedLines);
            }

            // 分批装饰期间可能切换主题，最终帧使用此时的颜色且不重新查询或排版。
            NativeTheme.ApplyDiffLineColors(_unified, _oldSide, _newSide, NativeTheme.IsDark(_settings.Theme));
            _hasText = true;
            _ = NativeMethods.ShowWindow(_noticeBody, NativeMethods.ShowHide);
            _displayedSideBySide = sideBySide;
            _changeNavigationIndex = -1;
            if (restoreBodyFocus)
            {
                _ = NativeMethods.SetFocus(sideBySide ? _newSide!.Handle : _unified!.Handle);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == _renderVersion && !_disposed)
            {
                _failed = true;
                SetNotice(UiText.GenerateDiffFailed);
            }
        }
        finally
        {
            if (version == _renderVersion && !_disposed)
            {
                FinishLoading();
            }
        }
    }

    private void RenderCurrent()
    {
        if (_document is not null && !_queryPending && !_disposed && !_failed)
        {
            CancelWork();
            _workCancellation = new();
            BeginLoading();
            _ = RenderAsync(_renderVersion, _document, _workCancellation.Token);
        }
    }

    private bool HasBodyFocus()
    {
        nint focused = NativeMethods.GetFocus();
        return focused != 0 && (focused == _unified?.Handle || focused == _oldSide?.Handle
            || focused == _gutter?.Handle || focused == _newSide?.Handle || focused == _noticeBody);
    }

    private void ChangeMode(bool sideBySide)
    {
        if (_sideBySide == sideBySide)
        {
            return;
        }
        _sideBySide = sideBySide;
        if (!_hasText) ApplyFileHeaderMode(sideBySide);
        InvalidateModeButtons();
        RenderCurrent();
    }

    private void CancelWork()
    {
        _renderVersion++;
        _workCancellation?.Cancel();
        _workCancellation?.Dispose();
        _workCancellation = null;
    }

    private void InvalidatePendingWork()
    {
        _requestVersion++;
        CancelWork();
        _queryPending = false;
        FinishLoading();
    }

    private void BeginLoading()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        UpdateNavigation();
        _loadingCancellation = new();
        _ = ShowLoadingAsync(_loadingCancellation.Token);
    }

    private async Task ShowLoadingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken);
            if (!_disposed && _loading && !cancellationToken.IsCancellationRequested)
            {
                _ = NativeMethods.SetWindowText(_statusLabel, UiText.GeneratingDiff);
                if (!_hasText && _unifiedText.Length == 0)
                {
                    _noticeLoading = true;
                    _ = NativeMethods.SetWindowText(_noticeBody, UiText.GeneratingDiff);
                    _ = NativeMethods.InvalidateRectangle(_noticeBody, 0, false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void FinishLoading()
    {
        _loading = false;
        _noticeLoading = false;
        _loadingCancellation?.Cancel();
        _loadingCancellation?.Dispose();
        _loadingCancellation = null;
        if (!_disposed && _statusLabel != 0)
        {
            _ = NativeMethods.SetWindowText(_statusLabel, string.Empty);
            if (_noticeBody != 0)
            {
                _ = NativeMethods.SetWindowText(_noticeBody, _hasText ? string.Empty : _unifiedText);
                _ = NativeMethods.InvalidateRectangle(_noticeBody, 0, false);
            }
            UpdateNavigation();
        }
    }

    private void UpdateNavigation()
    {
        bool canRead = !_loading && _hasText;
        _ = NativeMethods.EnableWindow(_controls[0], canRead && _changedLines.Count > 0);
        _ = NativeMethods.EnableWindow(_controls[1], canRead && _changedLines.Count > 0);
        _ = NativeMethods.EnableWindow(_controls[2], canRead);
        _ = NativeMethods.SetWindowText(_changeSummary,
            canRead ? string.Format(CultureInfo.CurrentCulture, ChangeCountFormat, _changedLines.Count) : string.Empty);
        _ = NativeMethods.ShowWindow(_changeSummary, canRead ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_statusLabel, canRead ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
        _ = NativeMethods.InvalidateRectangle(_changeSummary, 0, false);
    }

    private void InvalidateModeButtons()
    {
        _ = NativeMethods.InvalidateRectangle(_controls[3], 0, true);
        _ = NativeMethods.InvalidateRectangle(_controls[4], 0, true);
        _ = NativeMethods.InvalidateRectangle(_controls[5], 0, true);
    }

    private void NavigateChange(int direction)
    {
        if (_loading || !_hasText || _changedLines.Count == 0)
        {
            return;
        }

        _changeNavigationIndex = _changeNavigationIndex < 0
            ? direction < 0 ? _changedLines.Count - 1 : 0
            : (_changeNavigationIndex + Math.Sign(direction) + _changedLines.Count) % _changedLines.Count;
        int line = _changedLines[_changeNavigationIndex];
        if (_displayedSideBySide)
        {
            _oldSide?.GoToLine(line, focus: false);
            _gutter?.GoToLine(line, focus: false);
            _newSide?.GoToLine(line, focus: false);
        }
        else
        {
            _unified?.GoToLine(line, focus: false);
        }
    }

    private void ShowSearch()
    {
        if (_loading || !_hasText)
        {
            return;
        }
        string? query = NativeTextPrompt.Show(
            Handle,
            UiText.Find,
            UiText.DiffSearchPrompt,
            NativeTheme.IsDark(_settings.Theme));
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        string source = _sideBySide ? _newText : _unifiedText;
        int position = source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (position < 0)
        {
            _setStatus(UiText.DiffSearchNoResults);
            return;
        }

        ScintillaControl? target = _sideBySide ? _newSide : _unified;
        target?.SelectUtf8Range(position, position + query.Length, source);
    }

    private async Task ToggleIgnoreWhitespaceAsync()
    {
        if (_document is null || _disposed)
        {
            return;
        }

        bool next = !_ignoreWhitespace;
        GitComparisonDocument document = _document;
        InvalidatePendingWork();
        int version = _requestVersion;
        _queryPending = true;
        _ignoreWhitespace = next;
        InvalidateModeButtons();
        _workCancellation = new();
        BeginLoading();
        try
        {
            GitComparisonResult result = await _reload(document, next, _workCancellation.Token);
            if (_disposed || version != _requestVersion)
            {
                return;
            }
            _queryPending = false;
            if (!result.IsSuccess || result.Document is null)
            {
                _failed = true;
                _ignoreWhitespace = _documentIgnoreWhitespace;
                InvalidateModeButtons();
                SetNotice(result.ErrorMessage ?? UiText.GenerateDiffFailed);
                return;
            }
            _document = result.Document;
            _documentIgnoreWhitespace = next;
            _failed = false;
            RenderCurrent();
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && version == _requestVersion)
            {
                _queryPending = false;
                _ignoreWhitespace = _documentIgnoreWhitespace;
                InvalidateModeButtons();
                FinishLoading();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_disposed && version == _requestVersion)
            {
                _queryPending = false;
                _ignoreWhitespace = _documentIgnoreWhitespace;
                InvalidateModeButtons();
                _failed = true;
                SetNotice(UiText.GenerateDiffFailed);
            }
        }
    }

    private void ShowSettingsMenu()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point))
        {
            return;
        }

        _contextMenu?.Dispose();
        _contextMenu = NativeContextMenu.Show(
            Handle,
            point.X,
            point.Y,
            [
                new(
                    UiText.IgnoreWhitespace,
                    NativeContextMenuIcon.Whitespace,
                    () => _ = ToggleIgnoreWhitespaceAsync(),
                    Checked: _ignoreWhitespace),
            ],
            NativeTheme.IsDark(_settings.Theme));
    }

    private void SetNotice(string text)
    {
        bool restoreBodyFocus = HasBodyFocus();
        // 摘要、空态和失败页没有正文变更块，不能保留上一份 Diff 的导航状态。
        _changedLines.Clear();
        _changeNavigationIndex = -1;
        _hasText = false;
        ApplyFileHeaderMode(_sideBySide);
        _unifiedText = text;
        _oldText = string.Empty;
        _gutterText = string.Empty;
        _newText = string.Empty;
        _unified!.SetTextContent(string.Empty);
        _oldSide!.SetTextContent(string.Empty);
        _gutter!.SetTextContent(string.Empty);
        _newSide!.SetTextContent(string.Empty);
        _unified.SetVisible(false);
        _oldSide!.SetVisible(false);
        _gutter!.SetVisible(false);
        _newSide!.SetVisible(false);
        _ = NativeMethods.SetWindowText(_noticeBody, text);
        _ = NativeMethods.ShowWindow(_noticeBody, NativeMethods.ShowNormal);
        if (restoreBodyFocus)
        {
            _ = NativeMethods.SetFocus(_noticeBody);
        }
        FinishLoading();
    }

    private void Layout()
    {
        if (Handle == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return;
        }
        LayoutCountForTest++;

        int width = Math.Max(0, client.Right - client.Left);
        int buttonTop = Math.Max(0, (ToolbarHeight - NativeTheme.Scale(27)) / 2);
        int settingsLeft = Math.Max(NativeTheme.Scale(330), width - NativeTheme.Scale(35));
        _modeGroupBounds = NativeTheme.DiffModeGroupBounds(settingsLeft, ToolbarHeight);
        NativeMethods.Rectangle unified = NativeTheme.DiffModeButtonBounds(_modeGroupBounds, sideBySide: false);
        NativeMethods.Rectangle sideBySide = NativeTheme.DiffModeButtonBounds(_modeGroupBounds, sideBySide: true);
        int ignoreWhitespaceLeft = _modeGroupBounds.Left - NativeTheme.Scale(34);
        int[] lefts =
        [
            NativeTheme.Scale(8),
            NativeTheme.Scale(39),
            NativeTheme.Scale(74),
            unified.Left,
            sideBySide.Left,
            ignoreWhitespaceLeft,
            settingsLeft,
        ];
        int[] widths =
        [
            NativeTheme.Scale(27),
            NativeTheme.Scale(27),
            NativeTheme.Scale(27),
            unified.Right - unified.Left,
            sideBySide.Right - sideBySide.Left,
            NativeTheme.Scale(27),
            NativeTheme.Scale(27),
        ];
        for (int index = 0; index < _controls.Length; index++)
        {
            bool mode = index is 3 or 4;
            Move(_controls[index], lefts[index], mode ? unified.Top : buttonTop, widths[index],
                mode ? unified.Bottom - unified.Top : NativeTheme.Scale(27));
        }

        int textHeight = NativeTheme.ContentHeight(27, 4);
        Move(
            _statusLabel,
            NativeTheme.Scale(108),
            (ToolbarHeight - textHeight) / 2,
            Math.Max(0, ignoreWhitespaceLeft - NativeTheme.Scale(116)),
            textHeight);
        Move(_changeSummary, NativeTheme.Scale(108), (ToolbarHeight - textHeight) / 2,
            Math.Max(0, ignoreWhitespaceLeft - NativeTheme.Scale(116)), textHeight);
        LayoutContent();
    }

    private void ApplyFileHeaderMode(bool sideBySide)
    {
        if (_headerSideBySide == sideBySide) return;
        _headerSideBySide = sideBySide;
        LayoutContent();
        _ = NativeMethods.InvalidateRectangle(_fileBar, 0, true);
    }

    private void LayoutContent()
    {
        if (Handle == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client)) return;
        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        Move(_fileBar, 0, ToolbarHeight, width, FileBarHeight);
        int contentTop = ContentTop;
        int contentHeight = Math.Max(0, height - contentTop - NativeTheme.Scale(1));
        Move(_noticeBody, NativeTheme.Scale(1), contentTop, Math.Max(0, width - NativeTheme.Scale(2)), contentHeight);
        int availableWidth = Math.Max(0, width - NativeTheme.Scale(2));
        int gutterWidth = Math.Min(_gutterWidth, availableWidth);
        int halfWidth = Math.Max(0, (availableWidth - gutterWidth) / 2);
        int topPadding = Math.Min(NativeTheme.Scale(8), contentHeight);
        int bodyTop = contentTop + topPadding;
        int bodyHeight = Math.Max(0, contentHeight - topPadding);
        _unified?.SetBounds(NativeTheme.Scale(1), bodyTop, Math.Max(0, width - NativeTheme.Scale(2)), bodyHeight);
        _oldSide?.SetBounds(NativeTheme.Scale(1), bodyTop, halfWidth, bodyHeight);
        _gutter?.SetBounds(NativeTheme.Scale(1) + halfWidth, bodyTop, gutterWidth, bodyHeight);
        _newSide?.SetBounds(
            NativeTheme.Scale(1) + halfWidth + gutterWidth,
            bodyTop,
            Math.Max(0, availableWidth - halfWidth - gutterWidth),
            bodyHeight);
    }

    private void ApplyComparisonTextPadding()
    {
        int bodyPadding = NativeTheme.Scale(13);
        int gutterPadding = NativeTheme.Scale(7);
        _unified?.SetTextPadding(bodyPadding, bodyPadding);
        _oldSide?.SetTextPadding(bodyPadding, bodyPadding);
        _gutter?.SetTextPadding(gutterPadding, gutterPadding);
        _newSide?.SetTextPadding(bodyPadding, bodyPadding);
    }

    private void UpdateGutterWidth()
    {
        // 每行行号使用同一位数；只度量一行，不在字体或窗口变化时重新解析补丁。
        int end = _gutterText.IndexOfAny(['\r', '\n']);
        string sample = end < 0 ? "999    999" : _gutterText[..end];
        _gutterWidth = Math.Max(GutterWidth, (_gutter?.MeasureTextWidth(sample) ?? 0) + NativeTheme.Scale(14));
        _ = NativeMethods.InvalidateRectangle(_fileBar, 0, false);
    }

    private void SynchronizeVerticalScroll(nint notificationPointer)
    {
        if (_synchronizingScroll || !_hasText || !_displayedSideBySide)
        {
            return;
        }

        ScintillaControl? source = _oldSide?.IsUpdateUiNotification(notificationPointer) == true
            ? _oldSide
            : _newSide?.IsUpdateUiNotification(notificationPointer) == true
                ? _newSide
                : null;
        if (source is null)
        {
            return;
        }

        _synchronizingScroll = true;
        try
        {
            int firstVisibleLine = source.FirstVisibleLine;
            _oldSide?.SetFirstVisibleLine(firstVisibleLine);
            _gutter?.SetFirstVisibleLine(firstVisibleLine);
            _newSide?.SetFirstVisibleLine(firstVisibleLine);
        }
        finally
        {
            _synchronizingScroll = false;
        }
    }

    private nint PaintBackground(nint deviceContext)
    {
        if (deviceContext == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle rectangle))
        {
            return 1;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        nint brush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(dark).Panel);
        if (brush != 0)
        {
            _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
            _ = NativeMethods.DeleteObject(brush);
        }
        NativeTheme.DrawDiffModeGroup(deviceContext, _modeGroupBounds, dark);
        return 1;
    }

    private nint ApplyControlColor(nint deviceContext)
    {
        if (_controlBrush != 0)
        {
            _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
            _ = NativeMethods.SetTextColor(deviceContext, NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme)).Text);
            return _controlBrush;
        }
        return NativeMethods.GetSystemColorBrush(NativeMethods.ColorWindow);
    }

    private bool DrawControl(nint parameter)
    {
        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        if (item.DeviceContext == 0)
        {
            return true;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint background = palette.Panel;
        uint text = palette.Text;
        uint selected = palette.AccentSoft;
        Fill(item.DeviceContext, item.ItemRectangle, background);
        if (item.Control == _noticeBody)
        {
            if (_noticeLoading)
            {
                return NativeGitPanel.DrawDiffLoadingNotice(item, dark);
            }
            DrawComparisonNotice(item, palette);
            return true;
        }
        if ((item.ItemState & NativeMethods.OwnerDrawSelected) != 0)
        {
            FillRounded(item.DeviceContext, item.ItemRectangle, selected, NativeTheme.Scale(5));
        }

        if (item.Control == _fileBar)
        {
            DrawFileBar(item.DeviceContext, item.ItemRectangle, palette);
            return true;
        }

        if (item.Control == _changeSummary)
        {
            DrawText(item.DeviceContext, NativeMethods.GetWindowTextValue(_changeSummary), item.ItemRectangle,
                palette.Muted, NativeTheme.UiFont, centered: false, rightAligned: true);
            return true;
        }

        if (item.ControlIdentifier == CommandSettings)
        {
            DrawSettingsButton(item, palette);
            NativeTheme.DrawToolbarFocus(item, palette);
            return true;
        }

        if (item.ControlIdentifier is CommandPreviousChange or CommandNextChange or CommandSearch)
        {
            DrawNavigationButton(item, palette);
            NativeTheme.DrawToolbarFocus(item, palette);
            return true;
        }

        if (item.ControlIdentifier is CommandUnified or CommandSideBySide or CommandIgnoreWhitespace)
        {
            DrawModeButton(item);
            return true;
        }

        string label = NativeMethods.GetWindowTextValue(item.Control);
        NativeMethods.Rectangle rectangle = item.ItemRectangle;
        rectangle.Left += NativeTheme.Scale(3);
        rectangle.Right -= NativeTheme.Scale(3);
        DrawText(item.DeviceContext, label, rectangle, text, NativeTheme.UiFont);
        return true;
    }

    private void DrawComparisonNotice(NativeMethods.DrawItem item, NativeThemePalette palette)
    {
        string notice = NativeMethods.GetWindowTextValue(_noticeBody);
        if (notice.Length == 0)
        {
            return;
        }
        NativeMethods.Rectangle text = item.ItemRectangle;
        text.Left += NativeTheme.Scale(48);
        text.Right -= NativeTheme.Scale(24);
        text.Top += NativeTheme.Scale(28);
        text.Bottom -= NativeTheme.Scale(16);
        nint font = NativeMethods.SelectObject(item.DeviceContext, NativeTheme.UiFont);
        _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(item.DeviceContext, palette.Text);
        _ = NativeMethods.DrawText(item.DeviceContext, notice, notice.Length, ref text,
            NativeMethods.DrawTextWordBreak | NativeMethods.DrawTextNoPrefix);
        if (font != 0) _ = NativeMethods.SelectObject(item.DeviceContext, font);
        float u = NativeTheme.Scale(1f);
        float x = item.ItemRectangle.Left + 32 * u;
        float y = item.ItemRectangle.Top + 36 * u;
        uint color = _failed && notice != UiText.ComparisonCancelled ? palette.Danger : palette.Muted;
        _ = NativeGdiPlusDrawing.StrokeShapes(item.DeviceContext, color, u,
            [new(x, y - 3 * u, x, y + u), new(x, y + 3 * u, x, y + 3.5f * u)],
            [new(x - 7 * u, y - 7 * u, 14 * u, 14 * u)], []);
    }

    private static void DrawNavigationButton(
        NativeMethods.DrawItem item,
        NativeThemePalette palette)
    {
        if ((item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0)
        {
            FillRounded(item.DeviceContext, item.ItemRectangle, palette.Hover, NativeTheme.Scale(5));
        }

        uint color = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0
            ? palette.Faint
            : palette.Muted;
        NativeNavigationIcon icon = item.ControlIdentifier switch
        {
            CommandPreviousChange => NativeNavigationIcon.Up,
            CommandNextChange => NativeNavigationIcon.Down,
            _ => NativeNavigationIcon.Search,
        };
        _ = NativeTheme.DrawNavigationIcon(
            item.DeviceContext,
            item.ItemRectangle,
            icon,
            color);
    }

    private static void DrawSettingsButton(
        NativeMethods.DrawItem item,
        NativeThemePalette palette)
    {
        if ((item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0)
        {
            FillRounded(item.DeviceContext, item.ItemRectangle, palette.Hover, NativeTheme.Scale(5));
        }

        uint color = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0
            ? palette.Faint
            : palette.Muted;
        _ = NativeTheme.DrawSettingsIcon(item.DeviceContext, item.ItemRectangle, color);
    }

    private void DrawModeButton(NativeMethods.DrawItem item)
    {
        bool selected = item.ControlIdentifier switch
        {
            CommandUnified => !_sideBySide,
            CommandSideBySide => _sideBySide,
            _ => _ignoreWhitespace,
        };
        _ = NativeTheme.DrawDiffToolbarMode(item,
            item.ControlIdentifier switch
            {
                CommandUnified => NativeDiffModeIcon.Unified,
                CommandSideBySide => NativeDiffModeIcon.SideBySide,
                _ => NativeDiffModeIcon.IgnoreWhitespace,
            }, selected, NativeTheme.IsDark(_settings.Theme));
    }

    private void DrawFileBar(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        NativeThemePalette palette)
    {
        NativeDiffFileHeader.Draw(deviceContext, rectangle, _headerSideBySide,
            _baseRevisionText, _targetRevisionText, _pathText, palette, _gutterWidth);
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font,
        bool centered = true,
        bool rightAligned = false)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextSingleLine
            | NativeMethods.DrawTextVerticalCenter
            | NativeMethods.DrawTextNoPrefix
            | NativeMethods.DrawTextEndEllipsis;
        if (centered)
        {
            format |= NativeMethods.DrawTextCenter;
        }
        else if (rightAligned)
        {
            format |= 0x0002;
        }
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private static void Fill(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        nint brush = NativeMethods.CreateSolidBrush(color);
        if (brush != 0)
        {
            _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private static void FillRounded(nint deviceContext, NativeMethods.Rectangle rectangle, uint color, int radius)
    {
        if (NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, rectangle, color, radius))
        {
            return;
        }

        Fill(deviceContext, rectangle, color);
    }

    private static (
        string Text,
        List<(int Start, int Length)> RemovedHighlights,
        List<(int Start, int Length)> AddedHighlights,
        List<(int Start, int Length)> LineNumbers,
        List<(int Start, int Length)> RemovedMarkers,
        List<(int Start, int Length)> AddedMarkers,
        List<int> ChangedLines) RenderUnified(string patch)
    {
        IReadOnlyList<GitDiffLine> parsedLines = GitUnifiedDiffParser.Parse(patch);
        GitDiffLine[] lines = parsedLines.Where(IsVisibleLine).ToArray();
        int width = CalculateLineNumberWidth(lines.SelectMany(line => new[] { line.OldLineNumber, line.NewLineNumber }));
        StringBuilder builder = new();
        List<(int Start, int Length)> removed = [];
        List<(int Start, int Length)> added = [];
        List<(int Start, int Length)> numbers = [];
        List<(int Start, int Length)> removedMarkers = [];
        List<(int Start, int Length)> addedMarkers = [];
        List<int> changed = GitDiffNavigation.GetChangeStartLines(parsedLines.Select(line => line.Kind));
        for (int index = 0; index < lines.Length; index++)
        {
            GitDiffLine line = lines[index];
            int start = builder.Length;
            string oldNumber = line.OldLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            string newNumber = line.NewLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            char marker = line.Kind switch
            {
                GitDiffLineKind.Removed => '-',
                GitDiffLineKind.Added => '+',
                _ => ' ',
            };
            builder.Append(oldNumber.PadLeft(width)).Append(' ')
                .Append(newNumber.PadLeft(width)).Append(' ')
                .Append(marker).Append(' ').Append(line.Text).AppendLine();
            numbers.Add((start, (width * 2) + 2));
            int length = builder.Length - start;
            int markerStart = start + width * 2 + 2;
            if (line.Kind == GitDiffLineKind.Removed)
            {
                removed.Add((start, length));
                removedMarkers.Add((markerStart, 1));
            }
            else if (line.Kind == GitDiffLineKind.Added)
            {
                added.Add((start, length));
                addedMarkers.Add((markerStart, 1));
            }
        }
        return (builder.ToString(), removed, added, numbers, removedMarkers, addedMarkers, changed);
    }

    private static (
        string OldText,
        string GutterText,
        string NewText,
        List<(int Start, int Length)> OldHighlights,
        List<(int Start, int Length)> NewHighlights,
        List<(int Start, int Length)> OldBackgrounds,
        List<(int Start, int Length)> NewBackgrounds,
        List<int> ChangedLines) RenderSideBySide(string patch)
    {
        IReadOnlyList<GitSideBySideRow> parsedRows = GitUnifiedDiffParser.ToSideBySide(GitUnifiedDiffParser.Parse(patch));
        GitSideBySideRow[] rows = parsedRows.Where(row => IsVisibleLineKind(row.Kind)).ToArray();
        int width = CalculateLineNumberWidth(rows.SelectMany(row => new[] { row.OldLineNumber, row.NewLineNumber }));
        StringBuilder oldText = new();
        StringBuilder gutterText = new();
        StringBuilder newText = new();
        List<(int Start, int Length)> oldHighlights = [];
        List<(int Start, int Length)> newHighlights = [];
        List<(int Start, int Length)> oldBackgrounds = [];
        List<(int Start, int Length)> newBackgrounds = [];
        List<int> changed = GitDiffNavigation.GetChangeStartLines(parsedRows.Select(row => row.Kind));
        for (int index = 0; index < rows.Length; index++)
        {
            GitSideBySideRow row = rows[index];
            if (row.OldText is not null && row.Kind is GitDiffLineKind.Removed or GitDiffLineKind.Modified)
                oldBackgrounds.Add((oldText.Length, Math.Max(1, row.OldText.Length)));
            if (row.NewText is not null && row.Kind is GitDiffLineKind.Added or GitDiffLineKind.Modified)
                newBackgrounds.Add((newText.Length, Math.Max(1, row.NewText.Length)));
            AppendSide(oldText, row.OldText, row.OldChanges, oldHighlights);
            gutterText.Append((row.OldLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).PadLeft(width))
                .Append("    ")
                .Append((row.NewLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).PadLeft(width))
                .AppendLine();
            AppendSide(newText, row.NewText, row.NewChanges, newHighlights);
        }
        return (
            oldText.ToString(),
            gutterText.ToString(),
            newText.ToString(),
            oldHighlights,
            newHighlights,
            oldBackgrounds,
            newBackgrounds,
            changed);
    }

    private static void AppendSide(
        StringBuilder builder,
        string? text,
        IReadOnlyList<GitTextSpan> changes,
        List<(int Start, int Length)> highlights)
    {
        int textStart = builder.Length;
        if (text is not null)
        {
            builder.Append(text);
            foreach (GitTextSpan span in changes)
            {
                highlights.Add((textStart + span.Start, span.Length));
            }
        }
        builder.AppendLine();
    }

    private static int CalculateLineNumberWidth(IEnumerable<int?> numbers)
    {
        int largest = numbers.Where(number => number.HasValue)
            .Select(number => number!.Value)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(3, largest.ToString(CultureInfo.InvariantCulture).Length);
    }

    private static bool IsVisibleLine(GitDiffLine line)
    {
        return IsVisibleLineKind(line.Kind);
    }

    private static bool IsVisibleLineKind(GitDiffLineKind kind)
    {
        return kind is GitDiffLineKind.Context
            or GitDiffLineKind.Removed
            or GitDiffLineKind.Added
            or GitDiffLineKind.Modified;
    }

    private static string ResolveNotice(GitDiffContentStatus status)
    {
        return status switch
        {
            GitDiffContentStatus.Binary => UiText.BinaryDiffSummary,
            GitDiffContentStatus.SideTooLarge => UiText.DiffSideTooLarge,
            GitDiffContentStatus.OutputTooLarge => UiText.DiffOutputTooLarge,
            _ => UiText.NoTextDiff,
        };
    }
}
