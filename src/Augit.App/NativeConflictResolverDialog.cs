using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed partial class NativeConflictResolverDialog : IDisposable
{
    private const string WindowClassName = "Augit.ConflictResolver.Native";
    private const int DialogWidth = 1040;
    // 三栏冲突视觉稿在推荐窗口中高度约 467 逻辑像素，底部返回列表区仍固定保留。
    private const int DialogHeight = 467;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int ContentHeaderHeight = 42;
    private const int ColumnHeaderHeight = 36;
    private const int ContentFooterHeight = 48;
    private const int CommandAcceptYours = 1;
    private const int CommandAcceptTheirs = 2;
    private const int CommandAcceptBoth = 3;
    private const int CommandPrevious = 4;
    private const int CommandNext = 5;
    private const int CommandSave = 6;
    private const int CommandClose = 7;
    private const int CommandHeaderClose = 8;
    private const int CommandBack = 9;
    private const uint WindowMessageExternalChange = NativeMethods.WindowMessageApp + 45;
    private const uint WindowMessageResultChanged = NativeMethods.WindowMessageApp + 46;
    private const uint WindowMessagePresentationReady = NativeMethods.WindowMessageApp + 48;
    private const uint WindowMessageVisualAuditForceClose = NativeMethods.WindowMessageApp + 47;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeConflictResolverDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly nint _layoutOwner;
    private readonly int _dialogWidth;
    private readonly GitRepositorySnapshot _repository;
    private readonly IGitConflictService _service;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly string _fullPath;
    private GitConflictDocument? _document;
    private FileSystemWatcher? _watcher;
    private nint _handle;
    private nint _yoursLabel;
    private nint _resultLabel;
    private nint _theirsLabel;
    private nint _acceptYoursButton;
    private nint _acceptTheirsButton;
    private nint _acceptBothButton;
    private nint _previousButton;
    private nint _nextButton;
    private nint _saveButton;
    private nint _closeButton;
    private nint _headerCloseButton;
    private nint _backButton;
    private nint _noticeLabel;
    private nint _fileTitleLabel;
    private nint _controlBrush;
    private NativeToolTip? _toolTip;
    private ScintillaControl? _yours;
    private ScintillaControl? _result;
    private ScintillaControl? _theirs;
    private NativeConflictTextViewport? _yoursViewport, _resultViewport, _theirsViewport;
    private System.Threading.Timer? _resultChangeTimer;
    private List<ConflictViewBlock> _viewBlocks = [];
    private string? _parsedResultText;
    private IReadOnlyList<GitConflictBlock> _parsedResultBlocks = [];
    private int _resultParseCount;
    private string? _presentedResultText;
    private string? _presentedYoursText;
    private string? _presentedTheirsText;
    private bool _resultDiffersFromDisk;
    private bool _saving;
    private bool _saveInProgress;
    private bool _closed;
    private bool _disposed;
    private bool _dark;
    private bool _synchronizingScroll;
    private bool _resultPresentationPending;
    private int _blockIndex;
    private int _externalChangePending;
    private int _remainingBlockCount;

    internal NativeConflictResolverDialog(
        nint owner,
        GitRepositorySnapshot repository,
        IGitConflictService service,
        GitConflictDocument document,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setStatus);
        if (document.ContentKind != GitConflictContentKind.Text
            || document.ResultText is null
            || document.FileVersion is null
            || repository.RepositoryRoot is null)
        {
            throw new ArgumentException(UiText.ConflictTextUnavailable, nameof(document));
        }

        _owner = owner;
        _layoutOwner = NativeMethods.GetAncestor(owner, NativeMethods.GetAncestorRootOwner);
        if (_layoutOwner == 0)
        {
            _layoutOwner = owner;
        }

        _dialogWidth = ResolveDialogWidth(_layoutOwner);
        _repository = repository;
        _service = service;
        _document = document;
        _settings = settings;
        _setStatus = setStatus;
        _fullPath = Path.GetFullPath(Path.Combine(
            repository.RepositoryRoot,
            document.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureWindowClass();
        (int x, int y) = Center(_layoutOwner, S(_dialogWidth), S(DialogHeight));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            $"{UiText.ConflictResolver} - {document.RelativePath}",
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            x,
            y,
            S(_dialogWidth),
            S(DialogHeight),
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ConflictResolverCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        CreateToolTips();
        ApplyDocument(document, replaceResult: true);
        ApplyAppearance();
        MeasureLayout();
        StartWatcher();
        Layout();
        _resultChangeTimer = new(OnResultChangeTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    internal static bool Show(
        nint owner,
        GitRepositorySnapshot repository,
        IGitConflictService service,
        GitConflictDocument document,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        using NativeConflictResolverDialog dialog = new(
            owner,
            repository,
            service,
            document,
            settings,
            setStatus);
        dialog.Run();
        return dialog._saving;
    }

    internal nint HandleForTest => _handle;

    internal static string WindowClassNameForTest => WindowClassName;

    internal static uint VisualAuditForceCloseMessageForTest => WindowMessageVisualAuditForceClose;

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);

    internal static (int ContentHeaderHeight, int ColumnHeaderHeight, int ContentFooterHeight)
        LogicalContentLayoutForTest =>
        (ContentHeaderHeight, ColumnHeaderHeight, ContentFooterHeight);

    internal static (int Left, int Middle, int Right) CalculateColumnWidthsForTest(int available) =>
        CalculateColumnWidths(available);

    internal static int CalculateDialogWidthForTest(int availableLogicalWidth) =>
        CalculateDialogWidth(Math.Max(1, availableLogicalWidth));

    internal static IReadOnlyList<(int YoursLine, int ResultLine, int TheirsLine)>
        CalculateBlockLineAlignmentForTest(string yours, string result, string theirs) =>
        BuildConflictViewBlocks(yours, result, theirs)
            .Select(block => (block.YoursLine, block.ResultLine, block.TheirsLine))
            .ToArray();

    internal static IReadOnlyList<(int VirtualStart, int VirtualLength)>
        CalculateVirtualConflictRowsForTest(string yours, string result, string theirs) =>
        BuildConflictViewBlocks(yours, result, theirs)
            .Select(block => (block.VirtualStart, block.VirtualLength))
            .ToArray();

    internal static (int VirtualLine, int YoursLine, int ResultLine, int TheirsLine)
        MapConflictScrollLineForTest(string yours, string result, string theirs, int side, int line)
    {
        List<ConflictViewBlock> blocks = BuildConflictViewBlocks(yours, result, theirs);
        int virtualLine = MapSideLineToVirtual(blocks, side, line);
        return (virtualLine,
            MapVirtualLineToSide(blocks, virtualLine, 0),
            MapVirtualLineToSide(blocks, virtualLine, 1),
            MapVirtualLineToSide(blocks, virtualLine, 2));
    }

    internal static IReadOnlyList<(int YoursStart, int YoursLength, int TheirsStart, int TheirsLength)>
        CalculateBlockRangesForTest(string yours, string result, string theirs) =>
        BuildConflictViewBlocks(yours, result, theirs)
            .Select(block => (block.YoursStart, block.YoursLength, block.TheirsStart, block.TheirsLength))
            .ToArray();

    internal Task SaveForTestAsync() => SaveAsync();
    internal Task ReloadForTestAsync() => HandleExternalChangeAsync();
    internal void NotifyExternalChangeForTest() => OnExternalFileChanged(this, new(WatcherChangeTypes.Changed, Path.GetDirectoryName(_fullPath)!, Path.GetFileName(_fullPath)));
    internal bool SaveInProgressForTest => _saveInProgress;
    internal bool SavedForTest => _saving;
    internal nint ResultHandleForTest => _result?.Handle ?? 0;

    // 仅供原生交互回归读取三栏的实际滚动位置，不参与产品布局或保存逻辑。
    internal (int Yours, int Result, int Theirs) FirstVisibleLinesForTest =>
        (_yoursViewport?.FirstVisibleLine ?? 0, _resultViewport?.FirstVisibleLine ?? 0, _theirsViewport?.FirstVisibleLine ?? 0);

    internal NativeConflictTextViewport[] ViewportsForTest => [_yoursViewport!, _resultViewport!, _theirsViewport!];

    internal nint YoursHandleForTest => _yours?.Handle ?? 0;

    internal nint TheirsHandleForTest => _theirs?.Handle ?? 0;

    internal bool ResultLineVisibleForTest(int line) => _result?.IsLineVisibleForTest(line) == true;
    internal string NoticeForTest => NativeMethods.GetWindowTextValue(_noticeLabel);
    internal bool ResultPresentationPendingForTest => _resultPresentationPending;
    internal Task PresentationWorkerForTest => _presentationWorker;
    internal int ResultParseCountForTest => _resultParseCount;
    internal bool ResultIsDirtyForTest => IsResultDirty;


    internal bool YoursIsReadOnlyForTest => _yours?.IsReadOnly == true;

    internal bool ResultIsReadOnlyForTest => _result?.IsReadOnly != false;

    internal bool TheirsIsReadOnlyForTest => _theirs?.IsReadOnly == true;

    internal int LogicalWindowWidthForTest => NativeMethods.GetWindowRectangle(
        _handle,
        out NativeMethods.Rectangle rectangle)
        ? (int)Math.Round(NativeTheme.Unscale(rectangle.Right - rectangle.Left))
        : 0;

    internal (bool Yours, bool Result, bool Theirs) LineNumbersVisibleForTest =>
        (_yours?.LineNumbersVisibleForTest == true,
            _result?.LineNumbersVisibleForTest == true,
            _theirs?.LineNumbersVisibleForTest == true);

    internal static string DisplayFileNameForTest(string relativePath) => GetDisplayFileName(relativePath);

    internal string? ResultTextForTest => _result?.GetTextContent();

    internal void CloseForTest()
    {
        Close(force: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close(force: true);
        _lifetimeCancellation.Dispose();
        _resultChangeTimer?.Dispose();
        _resultChangeTimer = null;
        _presentationTimer?.Dispose();
        _presentationTimer = null;
        _watcher?.Dispose();
        _watcher = null;
        _yoursViewport?.Dispose();
        _resultViewport?.Dispose();
        _theirsViewport?.Dispose();
        _yoursViewport = _resultViewport = _theirsViewport = null;
        _yours = null;
        _result = null;
        _theirs = null;
        _parsedResultText = _presentedResultText = _presentedYoursText = _presentedTheirsText = null;
        _parsedResultBlocks = [];
        _viewBlocks = [];
        _document = null;
        _document = null;
        _toolTip?.Dispose();
        _toolTip = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }
        Close(force: true);
        GC.SuppressFinalize(this);
    }

    private void Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        // 冲突导航按钮没有可用项时，从中央最终结果区开始，保证打开窗口后焦点始终可见。
        nint initialFocus = NativeMethods.IsWindowEnabled(_previousButton)
            ? _previousButton
            : NativeMethods.IsWindowEnabled(_nextButton)
                ? _nextButton
                : _result?.Handle ?? _backButton;
        _ = NativeMethods.SetFocus(initialFocus);
        try
        {
            while (!_closed && NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0) > 0)
            {
                if (HandleResultEditShortcut(message)) continue;
                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyTab)
                {
                    MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
                    continue;
                }

                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyEscape)
                {
                    Close();
                    continue;
                }

                if (!NativeMethods.IsDialogMessage(_handle, ref message))
                {
                    _ = NativeMethods.TranslateMessage(ref message);
                    _ = NativeMethods.DispatchMessage(ref message);
                }
            }
        }
        finally
        {
            _ = NativeMethods.EnableWindow(_owner, true);
            _ = NativeMethods.SetForegroundWindow(_owner);
            focusScope.Restore();
        }
    }

    internal bool HandleResultEditShortcut(NativeMethods.Message message)
    {
        if (_closed || _result is null || message.MessageId != NativeMethods.WindowMessageKeyDown
            || message.Window != _result.Handle || NativeMethods.GetFocus() != _result.Handle
            || NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) >= 0
            || NativeMethods.GetKeyState(0x12) < 0) return false;
        int key = unchecked((int)message.WordParameter);
        bool shift = NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0;
        if (key != 'Z' && (key != 'Y' || shift)) return false;
        // 在 TranslateMessage 之前消费快捷键，避免 Ctrl+Shift+Z 被转成正文控制字符。
        if (!_saveInProgress && !_result.IsReadOnly)
        {
            const uint Undo = 2176;
            const uint Redo = 2011;
            _ = NativeMethods.SendMessage(_result.Handle, key == 'Y' || shift ? Redo : Undo, 0, 0);
        }
        return true;
    }

    /// <summary>
    /// 按三栏解决器的视觉顺序循环移动焦点。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [
                _previousButton,
                _nextButton,
                _yours?.Handle ?? 0,
                _result?.Handle ?? 0,
                _theirs?.Handle ?? 0,
                _acceptYoursButton,
                _acceptBothButton,
                _acceptTheirsButton,
                _closeButton,
                _saveButton,
                _backButton,
                _headerCloseButton,
            ],
            NativeMethods.GetFocus(),
            backwards);
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
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                Background = NativeMethods.GetSystemColorBrush(NativeMethods.ColorButtonFace),
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, UiText.ConflictResolverClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeConflictResolverDialog? instance;
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
            case NativeMethods.WindowMessagePaint:
                return instance.PaintWindow();
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessageShowWindow or NativeMethods.WindowMessageEnable when wordParameter == 0:
                instance.ClearActionHover(instance._hoveredActionButton);
                break;
            case NativeMethods.WindowMessageNonClientDestroy:
                instance.ClearActionHover(instance._hoveredActionButton);
                break;
            case NativeMethods.WindowMessageNonClientHitTest:
                return instance.HitTest();
            case NativeMethods.WindowMessageDrawItem:
                return instance.DrawControl(longParameter) ? 1 : 0;
            case NativeMethods.WindowMessageControlColorButton:
            case NativeMethods.WindowMessageControlColorStatic:
            case NativeMethods.WindowMessageControlColorEdit:
                return instance.ApplyControlColor(unchecked((nint)wordParameter));
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(NativeMethods.LowWord(wordParameter));
                return 0;
            case NativeMethods.WindowMessageNotify:
                instance.HandleNotification(longParameter);
                return 0;
            case WindowMessageExternalChange:
                _ = instance.HandleExternalChangeAsync();
                return 0;
            case WindowMessageResultChanged:
                instance.RefreshResultPresentation();
                return 0;
            case WindowMessagePresentationReady:
                instance.ApplyQueuedPresentation();
                return 0;
            case WindowMessageVisualAuditForceClose:
                instance.Close(force: true);
                return 0;
            case NativeMethods.WindowMessageClose:
                instance.Close();
                return 0;
            case NativeMethods.WindowMessageKeyDown when wordParameter == NativeMethods.VirtualKeyEscape:
                instance.Close();
                return 0;
            default:
                return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }
        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        uint columnLabelStyle = NativeMethods.StaticOwnerDraw;
        _yoursLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 30, columnLabelStyle);
        _resultLabel = CreateControl(NativeMethods.StaticClass, $"{UiText.FinalResult} · 可编辑", 31, columnLabelStyle);
        _theirsLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 32, columnLabelStyle);
        _acceptYoursButton = CreateButton(UiText.AcceptLeftBlock, CommandAcceptYours);
        _acceptTheirsButton = CreateButton(UiText.AcceptRightBlock, CommandAcceptTheirs);
        _acceptBothButton = CreateButton(UiText.AcceptBothBlock, CommandAcceptBoth);
        _previousButton = CreateButton(UiText.PreviousChange, CommandPrevious);
        _nextButton = CreateButton(UiText.NextChange, CommandNext);
        _saveButton = CreateButton(UiText.SaveAndResolve, CommandSave);
        _closeButton = CreateButton(UiText.Cancel, CommandClose);
        _headerCloseButton = CreateButton(string.Empty, CommandHeaderClose);
        _backButton = CreateButton(UiText.BackToConflictList, CommandBack);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 33, NativeMethods.StaticOwnerDraw);
        _fileTitleLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 34, NativeMethods.StaticOwnerDraw);
        _yoursViewport = new(_handle, 20);
        _resultViewport = new(_handle, 21);
        _theirsViewport = new(_handle, 22);
        _yours = _yoursViewport.Editor;
        _result = _resultViewport.Editor;
        _theirs = _theirsViewport.Editor;
        _yoursViewport.ScrollChanged = () => SynchronizeVerticalScroll(_yoursViewport);
        _resultViewport.ScrollChanged = () => SynchronizeVerticalScroll(_resultViewport);
        _theirsViewport.ScrollChanged = () => SynchronizeVerticalScroll(_theirsViewport);
        _yours.SetLineNumbersVisible(false);
        _result.SetLineNumbersVisible(false);
        _theirs.SetLineNumbersVisible(false);
        _result.SetEditable(true);
    }

    private nint CreateButton(string text, int identifier)
    {
        nint button = CreateControl(NativeMethods.ButtonClass, text, identifier, NativeMethods.ButtonOwnerDraw);
        if (!NativeMethods.SetWindowSubclass(button, ActionButtonProcedure, ActionButtonSubclassIdentifier, (nuint)_handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ConflictResolverControlCreateFailed);
        return button;
    }

    private nint CreateControl(string className, string text, int identifier, uint specificStyle)
    {
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | (className == NativeMethods.StaticClass ? 0 : NativeMethods.WindowStyleTabStop)
                | specificStyle,
            0,
            0,
            0,
            0,
            _handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ConflictResolverControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        return control;
    }

    private void CreateToolTips()
    {
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_acceptYoursButton, UiText.AcceptLeftBlock);
        _toolTip.Add(_acceptTheirsButton, UiText.AcceptRightBlock);
        _toolTip.Add(_acceptBothButton, UiText.AcceptBothBlock);
        _toolTip.Add(_previousButton, UiText.PreviousChange);
        _toolTip.Add(_nextButton, UiText.NextChange);
        _toolTip.Add(_saveButton, UiText.SaveAndResolve);
        _toolTip.Add(_closeButton, UiText.Cancel);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        _toolTip.Add(_backButton, UiText.BackToConflictList);
        _toolTip.Add(_yoursLabel, _document!.YoursLabel);
        _toolTip.Add(_resultLabel, $"{UiText.FinalResult} · 可编辑");
        _toolTip.Add(_theirsLabel, _document.TheirsLabel);
        _toolTip.Add(_noticeLabel, string.Empty);
        _toolTip.Add(_fileTitleLabel, $"{UiText.ResolveConflictDialogTitle} · {_document.RelativePath}");
    }

    private void HandleCommand(int command)
    {
        if (_closed || _saveInProgress) return;
        switch (command)
        {
            case CommandAcceptYours:
                AcceptBlock(GitConflictBlockChoice.Yours);
                break;
            case CommandAcceptTheirs:
                AcceptBlock(GitConflictBlockChoice.Theirs);
                break;
            case CommandAcceptBoth:
                AcceptBlock(GitConflictBlockChoice.Both);
                break;
            case CommandPrevious:
                MoveBlock(-1);
                break;
            case CommandNext:
                MoveBlock(1);
                break;
            case CommandSave:
                _ = SaveAsync();
                break;
            case CommandClose:
            case CommandHeaderClose:
            case CommandBack:
                Close();
                break;
        }
    }

    private void AcceptBlock(GitConflictBlockChoice choice)
    {
        if (_result is null)
        {
            return;
        }

        if (!TryGetResultSnapshot(out string text, out IReadOnlyList<GitConflictBlock> blocks)) return;
        if (blocks.Count == 0)
        {
            UpdateBlockState(text, alignCurrent: true);
            return;
        }

        _blockIndex = Math.Clamp(_blockIndex, 0, blocks.Count - 1);
        GitConflictBlock block = blocks[_blockIndex];
        string replacement = block.GetResolvedText(choice);
        _result.ReplaceEditableRange(Encoding.UTF8.GetByteCount(text.AsSpan(0, block.Start)),
            Encoding.UTF8.GetByteCount(text.AsSpan(block.Start, block.Length)), replacement);
        string resolved = string.Concat(text.AsSpan(0, block.Start), replacement, text.AsSpan(block.Start + block.Length));
        InvalidateResultSnapshot();
        _resultPresentationPending = false;
        _resultChangeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        UpdateBlockState(resolved, alignCurrent: true);
    }

    private void MoveBlock(int direction)
    {
        if (_result is null)
        {
            return;
        }

        if (!TryGetResultSnapshot(out string text, out IReadOnlyList<GitConflictBlock> blocks)) return;
        if (blocks.Count == 0)
        {
            _blockIndex = 0;
            UpdateBlockState(text, alignCurrent: true);
            return;
        }

        _blockIndex = (_blockIndex + direction + blocks.Count) % blocks.Count;
        UpdateBlockState(text, alignCurrent: true);
    }

    private async Task SaveAsync()
    {
        if (_closed || _saving || _saveInProgress || _document is null || _result is null)
        {
            return;
        }

        if (!TryGetResultSnapshot(out string text, out IReadOnlyList<GitConflictBlock> blocks)) return;
        if (blocks.Count > 0)
        {
            ShowError(UiText.UnresolvedConflictBlocks);
            return;
        }

        _saveInProgress = true;
        CancelExternalReload();
        CancelPresentation();
        _resultPresentationPending = false;
        _resultChangeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        SetControlsEnabled(false);
        SetNotice(UiText.ConflictApplying);
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            GitConflictMutationResult saved = await _service.SaveResolvedAsync(
                _repository,
                new(
                    _document.RelativePath,
                    text,
                    _document.FileVersion!,
                    _document.Operation), _lifetimeCancellation.Token);
            if (_closed) return;
            if (!saved.IsSuccess)
            {
                ShowError(saved.ErrorMessage ?? UiText.GitUnavailable);
                return;
            }

            _saving = true;
            _result.MarkSaved();
            _resultDiffersFromDisk = false;
            _setStatus(UiText.ConflictResolved);
            Close(force: true);
        }
        catch (OperationCanceledException) when (_closed || _lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_closed) ShowError(exception.Message);
        }
        finally
        {
            _saveInProgress = false;
            if (!_closed)
            {
                SetControlsEnabled(true);
                StartWatcher();
            }
        }
    }

    private void ApplyDocument(GitConflictDocument document, bool replaceResult)
    {
        // SetTextContent 会清除行标记；即使重载返回相同字符串，也必须重新应用装饰。
        _presentedResultText = null;
        _document = document;
        _ = NativeMethods.SetWindowText(_yoursLabel, document.YoursLabel);
        _ = NativeMethods.SetWindowText(_resultLabel, $"{UiText.FinalResult} · 可编辑");
        _ = NativeMethods.SetWindowText(_theirsLabel, document.TheirsLabel);
        _toolTip?.Update(_yoursLabel, document.YoursLabel);
        _toolTip?.Update(_theirsLabel, document.TheirsLabel);
        _yours?.SetTextContent(document.YoursText ?? "此侧不存在。");
        _theirs?.SetTextContent(document.TheirsText ?? "此侧不存在。");
        if (replaceResult && _result is not null)
        {
            InvalidateResultSnapshot();
            _result.SetTextContent(document.ResultText ?? string.Empty);
            _result.SetEditable(true);
            _result.MarkSaved();
            _resultDiffersFromDisk = false;
            _blockIndex = 0;
        }

        UpdateBlockState(replaceResult ? document.ResultText : null, alignCurrent: true);
    }

    private (string Text, IReadOnlyList<GitConflictBlock> Blocks) GetResultSnapshot(string? knownText = null)
    {
        if (_parsedResultText is null)
        {
            _parsedResultText = knownText ?? _result?.GetTextContent() ?? string.Empty;
            _parsedResultBlocks = GitConflictText.Parse(_parsedResultText);
            _resultParseCount++;
        }
        return (_parsedResultText, _parsedResultBlocks);
    }

    private void InvalidateResultSnapshot()
    {
        CancelPresentation();
        _parsedResultText = null;
        _parsedResultBlocks = [];
    }

    private void UpdateBlockState(
        string? knownText = null,
        bool alignCurrent = false)
    {
        knownText ??= _parsedResultText ?? _result?.GetTextContent() ?? string.Empty;
        if (NeedsBackgroundPresentation(knownText)
            && !ReferenceEquals(_parsedResultText, knownText))
        {
            QueuePresentation(knownText, alignCurrent);
            return;
        }
        (string text, IReadOnlyList<GitConflictBlock> blocks) = GetResultSnapshot(knownText);
        _resultPresentationPending = false;
        _backgroundPresentation = NeedsBackgroundPresentation(text);
        UpdateConflictCount(blocks.Count);
        ApplyConflictPresentation(text, alignCurrent, blocks);
    }

    private void UpdateConflictCount(int count)
    {
        _remainingBlockCount = count;
        if (_navigationWidth > 0)
        {
            int countWidth = MeasureUiText($"{count} 个未处理冲突", 0, 8);
            if (_countWidth != countWidth)
            {
                _countWidth = countWidth;
                // 冲突数量位数改变时只重新分配标题，正文、选择和滚动不变。
                LayoutFileTitle(S(_dialogWidth));
            }
        }
        if (count == 0)
        {
            _blockIndex = 0;
            SetNotice("没有剩余冲突块，可以人工检查后保存。 ");
        }
        else
        {
            _blockIndex = Math.Clamp(_blockIndex, 0, count - 1);
            SetNotice($"未处理冲突块：{count}，当前位置：{_blockIndex + 1}");
        }

        SetConflictActionsEnabled(true);
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private void ApplyConflictPresentation(
        string resultText,
        bool alignCurrent,
        IReadOnlyList<GitConflictBlock>? parsedBlocks = null,
        bool refreshColors = false)
    {
        if (_document is null || _yours is null || _result is null || _theirs is null)
        {
            _viewBlocks = [];
            return;
        }

        string yoursText = _document.YoursText ?? "此侧不存在。";
        string theirsText = _document.TheirsText ?? "此侧不存在。";
        if (ReferenceEquals(_presentedResultText, resultText)
            && ReferenceEquals(_presentedYoursText, yoursText)
            && ReferenceEquals(_presentedTheirsText, theirsText))
        {
            if (refreshColors) UpdateConflictColors();
            if (alignCurrent) AlignCurrentConflictBlock();
            return;
        }
        IReadOnlyList<GitConflictBlock> blocks = parsedBlocks ?? _parsedResultBlocks;
        List<int> hiddenLines = GetHiddenConflictMarkerLines(resultText, blocks);
        _viewBlocks = BuildConflictViewBlocks(yoursText, resultText, theirsText, blocks, hiddenLines: hiddenLines);
        _synchronizingScroll = true;
        try
        {
            DisplayAlignment alignment = BuildDisplayAlignment(_viewBlocks, hiddenLines);
            BeginDisplayAlignment(alignment);
            _result.SetHiddenLines(hiddenLines);
            foreach (DisplayGap gap in alignment.Gaps) ApplyDisplayGap(gap);
        }
        finally { _synchronizingScroll = false; }
        IReadOnlyList<(int Start, int Length)> yoursRanges = _viewBlocks
            .Where(block => block.YoursLength > 0)
            .Select(block => (block.YoursStart, block.YoursLength))
            .ToArray();
        IReadOnlyList<(int Start, int Length)> resultRanges = _viewBlocks
            .Where(block => block.ResultLength > 0)
            .Select(block => (block.ResultStart, block.ResultLength))
            .ToArray();
        IReadOnlyList<(int Start, int Length)> theirsRanges = _viewBlocks
            .Where(block => block.TheirsLength > 0)
            .Select(block => (block.TheirsStart, block.TheirsLength))
            .ToArray();
        (int red, int green, int blue) sideColor = _dark
            ? (82, 50, 52)
            : (247, 215, 215);
        (int red, int green, int blue) resultColor = _dark
            ? (43, 63, 89)
            : (220, 233, 252);
        _yours.SetLineBackgrounds(0, yoursText, yoursRanges, sideColor.red, sideColor.green, sideColor.blue);
        _result.SetLineBackgrounds(0, resultText, resultRanges, resultColor.red, resultColor.green, resultColor.blue);
        _theirs.SetLineBackgrounds(0, theirsText, theirsRanges, sideColor.red, sideColor.green, sideColor.blue);
        RefreshDisplayViewports();
        _presentedResultText = resultText;
        _presentedYoursText = yoursText;
        _presentedTheirsText = theirsText;
        if (alignCurrent)
        {
            AlignCurrentConflictBlock();
        }
        else
        {
            // 撤销和编辑可能在装饰待更新期间移动结果区；展示网格就绪后恢复三栏同步。
            SynchronizeVerticalScroll(_resultViewport!);
        }
    }

    private void UpdateConflictColors()
    {
        (int red, int green, int blue) side = _dark ? (82, 50, 52) : (247, 215, 215);
        (int red, int green, int blue) result = _dark ? (43, 63, 89) : (220, 233, 252);
        _yours?.SetLineBackgroundColor(0, side.red, side.green, side.blue);
        _result?.SetLineBackgroundColor(0, result.red, result.green, result.blue);
        _theirs?.SetLineBackgroundColor(0, side.red, side.green, side.blue);
    }

    private void AlignCurrentConflictBlock()
    {
        if (_viewBlocks.Count == 0)
        {
            return;
        }

        ConflictViewBlock block = _viewBlocks[Math.Clamp(_blockIndex, 0, _viewBlocks.Count - 1)];
        const int ContextLines = 2;
        _synchronizingScroll = true;
        try
        {
            _yoursViewport?.SetFirstVisibleLine(Math.Max(0, (_displayGridAligned ? block.VirtualStart : block.YoursLine) - ContextLines));
            _resultViewport?.SetFirstVisibleLine(Math.Max(0, (_displayGridAligned ? block.VirtualStart : block.ResultDisplayLine) - ContextLines));
            _theirsViewport?.SetFirstVisibleLine(Math.Max(0, (_displayGridAligned ? block.VirtualStart : block.TheirsLine) - ContextLines));
        }
        finally { _synchronizingScroll = false; }
    }

    private void StartWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        string? directory = Path.GetDirectoryName(_fullPath);
        string fileName = Path.GetFileName(_fullPath);
        if (directory is null || fileName.Length == 0 || !Directory.Exists(directory))
        {
            return;
        }

        _watcher = new(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };
        _watcher.Changed += OnExternalFileChanged;
        _watcher.Created += OnExternalFileChanged;
        _watcher.Deleted += OnExternalFileChanged;
        _watcher.Renamed += OnExternalFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnExternalFileChanged(object sender, FileSystemEventArgs eventArgs)
    {
        if (_closed || _saving || _saveInProgress || _handle == 0)
        {
            return;
        }

        Interlocked.Increment(ref _externalChangeVersion);
        if (Interlocked.Exchange(ref _externalChangePending, 1) == 0)
        {
            _ = NativeMethods.PostMessage(_handle, WindowMessageExternalChange, 0, 0);
        }
    }

    private void HandleNotification(nint notificationPointer)
    {
        if (_result?.IsModifiedNotification(notificationPointer) == true)
        {
            InvalidateResultSnapshot();
            _resultPresentationPending = true;
            if (_backgroundPresentation) SetConflictActionsEnabled(false);
            _resultChangeTimer?.Change(120, Timeout.Infinite);
        }

    }

    private void OnResultChangeTimer(object? state)
    {
        nint handle = _handle;
        if (!_closed && handle != 0)
        {
            _ = NativeMethods.PostMessage(handle, WindowMessageResultChanged, 0, 0);
        }
    }

    private void RefreshResultPresentation()
    {
        if (_closed || _saveInProgress || _result is null || !_resultPresentationPending || _presentationJob is not null)
        {
            return;
        }

        string text = _result.GetTextContent();
        if (NeedsBackgroundPresentation(text))
        {
            QueuePresentation(text, alignCurrent: false);
            return;
        }
        _resultPresentationPending = false;
        UpdateBlockState(text);
    }

    private void SetConflictActionsEnabled(bool enabled)
    {
        bool state = enabled && !_saveInProgress && _remainingBlockCount > 0;
        foreach (nint control in new[] { _acceptYoursButton, _acceptTheirsButton, _acceptBothButton, _previousButton, _nextButton })
            _ = NativeMethods.EnableWindow(control, state);
        _ = NativeMethods.EnableWindow(_saveButton, enabled && !_saveInProgress);
    }


    private void SynchronizeVerticalScroll(NativeConflictTextViewport source)
    {
        if (_synchronizingScroll || _resultPresentationPending || _viewBlocks.Count == 0)
        {
            return;
        }

        int sourceSide = source == _yoursViewport ? 0 : source == _resultViewport ? 1 : 2;
        int virtualLine = _displayGridAligned ? source.FirstVisibleLine
            : MapSideLineToVirtual(_viewBlocks, sourceSide, source.FirstVisibleLine);
        _synchronizingScroll = true;
        try
        {
            // 不反写滚动来源，避免短侧钳制或间隙取整使用户正在阅读的栏跳动。
            if (source != _yoursViewport) _yoursViewport?.SetFirstVisibleLine(_displayGridAligned ? virtualLine : MapVirtualLineToSide(_viewBlocks, virtualLine, 0));
            if (source != _resultViewport) _resultViewport?.SetFirstVisibleLine(_displayGridAligned ? virtualLine : MapVirtualLineToSide(_viewBlocks, virtualLine, 1));
            if (source != _theirsViewport) _theirsViewport?.SetFirstVisibleLine(_displayGridAligned ? virtualLine : MapVirtualLineToSide(_viewBlocks, virtualLine, 2));
        }
        finally
        {
            _synchronizingScroll = false;
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        enabled &= !_saveInProgress;
        bool resolveActions = enabled && _remainingBlockCount > 0 && !(_backgroundPresentation && _resultPresentationPending);
        foreach (nint control in new[]
        {
            _acceptYoursButton,
            _acceptTheirsButton,
            _acceptBothButton,
            _previousButton,
            _nextButton,
            _saveButton,
            _closeButton,
            _headerCloseButton,
            _backButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }

        foreach (nint control in new[]
        {
            _acceptYoursButton,
            _acceptTheirsButton,
            _acceptBothButton,
            _previousButton,
            _nextButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, resolveActions);
        }

        _result?.SetEditable(enabled);
    }

    private void ApplyAppearance()
    {
        _dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, _dark);
        foreach (nint control in new[]
        {
            _yoursLabel,
            _resultLabel,
            _theirsLabel,
            _acceptYoursButton,
            _acceptTheirsButton,
            _acceptBothButton,
            _previousButton,
            _nextButton,
            _saveButton,
            _closeButton,
            _headerCloseButton,
            _backButton,
            _noticeLabel,
            _fileTitleLabel,
        })
        {
            NativeTheme.ApplyToControl(control, _dark);
        }

        _yours?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, _dark);
        _result?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, _dark);
        _theirs?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, _dark);
        _yours?.SetTextPadding(S(12), S(12));
        _result?.SetTextPadding(S(12), S(12));
        _theirs?.SetTextPadding(S(12), S(12));
        _yoursViewport?.ApplyAppearance(_dark);
        _resultViewport?.ApplyAppearance(_dark);
        _theirsViewport?.ApplyAppearance(_dark);
        _toolTip?.ApplyAppearance(_dark);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }

        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        if (!_resultPresentationPending)
        {
            (string resultText, IReadOnlyList<GitConflictBlock> blocks) = GetResultSnapshot();
            ApplyConflictPresentation(resultText, alignCurrent: false, parsedBlocks: blocks, refreshColors: true);
        }
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle rectangle))
        {
            return;
        }

        int width = Math.Max(0, rectangle.Right - rectangle.Left);
        int height = Math.Max(0, rectangle.Bottom - rectangle.Top);
        if (_headerHeight == 0) return;
        int border = Math.Max(1, S(1));
        int outerFooterTop = height - border - _footerHeight;
        int contentFooterTop = outerFooterTop - _contentFooterHeight;
        int columnHeaderTop = border + _headerHeight + _contentHeaderHeight;
        int columnTop = columnHeaderTop + _columnHeaderHeight;
        int contentHeight = Math.Max(0, contentFooterTop - columnTop);
        int gap = S(1);
        int available = Math.Max(0, width - S(34) - gap * 2);
        (int leftWidth, int middleWidth, _) = CalculateColumnWidths(available);
        int left = S(17);
        int middle = left + leftWidth + gap;
        int right = middle + middleWidth + gap;
        Move(_yoursLabel, left, columnHeaderTop, leftWidth, _columnHeaderHeight);
        Move(_resultLabel, middle, columnHeaderTop, middleWidth, _columnHeaderHeight);
        Move(_theirsLabel, right, columnHeaderTop, Math.Max(0, width - right - S(17)), _columnHeaderHeight);
        _yoursViewport?.SetBounds(left, columnTop, leftWidth, contentHeight);
        _resultViewport?.SetBounds(middle, columnTop, middleWidth, contentHeight);
        _theirsViewport?.SetBounds(right, columnTop, Math.Max(0, width - right - S(17)), contentHeight);

        int actionTop = contentFooterTop + border + S(10);
        Move(_acceptYoursButton, S(17), actionTop, _acceptWidth, _buttonHeight);
        Move(_acceptBothButton, S(17) + _acceptWidth + S(8), actionTop, _acceptWidth, _buttonHeight);
        Move(_acceptTheirsButton, S(17) + 2 * (_acceptWidth + S(8)), actionTop, _acceptWidth, _buttonHeight);
        int saveTop = actionTop + (_wrapActions ? _buttonHeight + S(8) : 0);
        Move(_saveButton, width - S(17) - _saveWidth, saveTop, _saveWidth, _buttonHeight);
        Move(_closeButton, width - S(25) - _saveWidth - _cancelWidth, saveTop, _cancelWidth, _buttonHeight);
        Move(_noticeLabel, S(17), outerFooterTop + border, Math.Max(0, width - S(42) - _backWidth), _footerHeight - border);
        int navTop = border + _headerHeight + (_contentHeaderHeight - border - _buttonHeight) / 2;
        LayoutFileTitle(width);
        Move(_previousButton, width - S(25) - 2 * _navigationWidth, navTop, _navigationWidth, _buttonHeight);
        Move(_nextButton, width - S(17) - _navigationWidth, navTop, _navigationWidth, _buttonHeight);
        Move(_headerCloseButton, width - S(44), border + (_headerHeight - border - S(31)) / 2, S(32), S(31));
        Move(_backButton, width - S(17) - _backWidth, outerFooterTop + border + (_footerHeight - border - _buttonHeight) / 2,
            _backWidth, _buttonHeight);
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private static (int Left, int Middle, int Right) CalculateColumnWidths(int available)
    {
        // 与稿中的 CSS 网格一致：前两栏的分隔线属于其轨道，按累计边界取整避免误差都堆在中央。
        int border = Math.Max(1, S(1));
        int total = Math.Max(0, available) + 2 * border;
        int first = (int)Math.Round(total / 3.08d, MidpointRounding.AwayFromZero);
        int second = (int)Math.Round(total * 2.08d / 3.08d, MidpointRounding.AwayFromZero);
        return (Math.Max(0, first - border), Math.Max(0, second - first - border), Math.Max(0, total - second));
    }

    private static List<ConflictViewBlock> BuildConflictViewBlocks(
        string yours,
        string result,
        string theirs,
        IReadOnlyList<GitConflictBlock>? parsedBlocks = null,
        List<int>? hiddenLines = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(yours);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(theirs);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<GitConflictBlock> blocks = parsedBlocks ?? GitConflictText.Parse(result, cancellationToken);
        List<ConflictViewBlock> viewBlocks = new(blocks.Count);
        if (blocks.Count == 0) return viewBlocks;
        NormalizedText normalizedYours = NormalizeForMatching(yours);
        NormalizedText normalizedTheirs = NormalizeForMatching(theirs);
        LineCursor yoursLines = new(yours);
        LineCursor resultLines = new(result);
        LineCursor theirsLines = new(theirs);
        int yoursCursor = 0;
        int theirsCursor = 0;
        int previousResultEndPosition = 0;
        foreach (GitConflictBlock block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string context = result[previousResultEndPosition..block.Start].Replace("\r\n", "\n", StringComparison.Ordinal);
            previousResultEndPosition = block.Start + block.Length;
            (int yoursStart, int yoursLength, int nextYoursCursor) = FindSideBlock(
                normalizedYours,
                block.YoursText,
                yoursCursor,
                context);
            (int theirsStart, int theirsLength, int nextTheirsCursor) = FindSideBlock(
                normalizedTheirs,
                block.TheirsText,
                theirsCursor,
                context);
            yoursCursor = nextYoursCursor;
            theirsCursor = nextTheirsCursor;
            int yoursLine = yoursLines.GetLine(yoursStart);
            int resultLine = resultLines.GetLine(block.Start);
            int theirsLine = theirsLines.GetLine(theirsStart);
            int yoursLineCount = yoursLines.GetCoveredLineCount(yoursStart, yoursLength, yoursLine);
            int resultLineCount = resultLines.GetCoveredLineCount(block.Start, block.Length, resultLine);
            int theirsLineCount = theirsLines.GetCoveredLineCount(theirsStart, theirsLength, theirsLine);
            viewBlocks.Add(new(
                yoursStart,
                yoursLength,
                block.Start,
                block.Length,
                theirsStart,
                theirsLength,
                yoursLine,
                resultLine,
                theirsLine,
                yoursLineCount,
                resultLineCount,
                theirsLineCount));
        }
        hiddenLines ??= GetHiddenConflictMarkerLines(result, blocks, cancellationToken);
        int hiddenCursor = 0;
        int virtualCursor = 0;
        int previousYoursEnd = 0, previousResultEnd = 0, previousTheirsEnd = 0;
        for (int index = 0; index < viewBlocks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConflictViewBlock block = viewBlocks[index];
            while (hiddenCursor < hiddenLines.Count && hiddenLines[hiddenCursor] < block.ResultLine) hiddenCursor++;
            int hiddenBefore = hiddenCursor;
            while (hiddenCursor < hiddenLines.Count && hiddenLines[hiddenCursor] < block.ResultLine + block.ResultLineCount) hiddenCursor++;
            block = block with
            {
                ResultDisplayLine = block.ResultLine - hiddenBefore,
                ResultDisplayLineCount = block.ResultLineCount - (hiddenCursor - hiddenBefore),
            };
            // 首个可见行接口使用显示行；隐藏标记仍保留原文位置，仅从滚动区间中扣除。
            int contextLength = Math.Max(0, Math.Max(block.YoursLine - previousYoursEnd,
                Math.Max(block.ResultDisplayLine - previousResultEnd, block.TheirsLine - previousTheirsEnd)));
            int virtualStart = virtualCursor + contextLength;
            int virtualLength = Math.Max(block.YoursLineCount,
                Math.Max(block.ResultDisplayLineCount, block.TheirsLineCount));
            viewBlocks[index] = block with { VirtualStart = virtualStart, VirtualLength = virtualLength };
            virtualCursor = virtualStart + Math.Max(1, virtualLength);
            previousYoursEnd = block.YoursLine + block.YoursLineCount;
            previousResultEnd = block.ResultDisplayLine + block.ResultDisplayLineCount;
            previousTheirsEnd = block.TheirsLine + block.TheirsLineCount;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return viewBlocks;
    }

    private static List<int> GetHiddenConflictMarkerLines(
        string result,
        IReadOnlyList<GitConflictBlock> blocks,
        CancellationToken cancellationToken = default)
    {
        List<int> hidden = [];
        int scanPosition = 0;
        int line = 0;
        foreach (GitConflictBlock block in blocks)
        {
            int position = Math.Clamp(block.Start, 0, result.Length);
            int end = Math.Clamp(block.Start + block.Length, position, result.Length);
            line += CountNewlines(result, scanPosition, position);
            while (position < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int newline = result.IndexOf('\n', position, end - position);
                int contentEnd = newline < 0 ? end : newline;
                if (contentEnd > position && result[contentEnd - 1] == '\r') contentEnd--;
                ReadOnlySpan<char> content = result.AsSpan(position, contentEnd - position);
                if (content.StartsWith("<<<<<<<", StringComparison.Ordinal)
                    || content.StartsWith("|||||||", StringComparison.Ordinal)
                    || content.StartsWith("=======", StringComparison.Ordinal)
                    || content.StartsWith(">>>>>>>", StringComparison.Ordinal))
                {
                    hidden.Add(line);
                }
                if (newline < 0) break;
                position = newline + 1;
                line++;
            }
            scanPosition = end;
        }
        return hidden;
    }

    private static int CountNewlines(string text, int start, int end)
    {
        int count = 0;
        for (int index = Math.Clamp(start, 0, text.Length); index < Math.Clamp(end, 0, text.Length); index++)
            if (text[index] == '\n') count++;
        return count;
    }

    private static int MapSideLineToVirtual(
        IReadOnlyList<ConflictViewBlock> blocks,
        int side,
        int line)
    {
        if (blocks.Count == 0) return Math.Max(0, line);
        ConflictViewBlock first = blocks[0];
        int firstSideLine = GetSideLine(first, side);
        if (line < firstSideLine)
        {
            return Math.Max(0, first.VirtualStart - (firstSideLine - line));
        }

        int previousVirtualEnd = first.VirtualStart;
        int previousSideEnd = firstSideLine;
        foreach (ConflictViewBlock block in blocks)
        {
            int sideLine = GetSideLine(block, side);
            int sideCount = GetSideLineCount(block, side);
            if (sideCount > 0 && line >= sideLine && line < sideLine + sideCount)
            {
                return block.VirtualStart + line - sideLine;
            }
            if (line < sideLine)
            {
                int sideGap = Math.Max(1, sideLine - previousSideEnd);
                int virtualGap = Math.Max(1, block.VirtualStart - previousVirtualEnd);
                return previousVirtualEnd + (int)Math.Min(virtualGap - 1, (long)(line - previousSideEnd) * virtualGap / sideGap);
            }
            previousVirtualEnd = block.VirtualStart + Math.Max(1, block.VirtualLength);
            previousSideEnd = sideLine + sideCount;
        }

        return previousVirtualEnd + Math.Max(0, line - previousSideEnd);
    }

    private static int MapVirtualLineToSide(
        IReadOnlyList<ConflictViewBlock> blocks,
        int virtualLine,
        int side)
    {
        if (blocks.Count == 0) return Math.Max(0, virtualLine);
        ConflictViewBlock first = blocks[0];
        if (virtualLine < first.VirtualStart)
        {
            return Math.Max(0, GetSideLine(first, side) - (first.VirtualStart - virtualLine));
        }

        int previousVirtualEnd = first.VirtualStart;
        int previousSideEnd = GetSideLine(first, side);
        foreach (ConflictViewBlock block in blocks)
        {
            if (virtualLine < block.VirtualStart)
            {
                int virtualGap = Math.Max(1, block.VirtualStart - previousVirtualEnd);
                int sideGap = Math.Max(0, GetSideLine(block, side) - previousSideEnd);
                return previousSideEnd + (int)((long)(virtualLine - previousVirtualEnd) * sideGap / virtualGap);
            }
            int virtualLength = Math.Max(1, block.VirtualLength);
            if (virtualLine < block.VirtualStart + virtualLength)
            {
                int sideLine = GetSideLine(block, side);
                int sideCount = GetSideLineCount(block, side);
                return sideCount == 0
                    ? sideLine
                    : sideLine + Math.Min(sideCount - 1, virtualLine - block.VirtualStart);
            }

            int nextVirtual = block.VirtualStart + virtualLength;
            int sideEnd = GetSideLine(block, side) + GetSideLineCount(block, side);
            previousVirtualEnd = nextVirtual;
            previousSideEnd = sideEnd;
        }

        return Math.Max(0, previousSideEnd + virtualLine - previousVirtualEnd);
    }

    private static int GetSideLine(ConflictViewBlock block, int side) => side switch
    {
        0 => block.YoursLine,
        1 => block.ResultDisplayLine,
        _ => block.TheirsLine,
    };

    private static int GetSideLineCount(ConflictViewBlock block, int side) => side switch
    {
        0 => block.YoursLineCount,
        1 => block.ResultDisplayLineCount,
        _ => block.TheirsLineCount,
    };

    private static (int Start, int Length, int NextCursor) FindSideBlock(
        NormalizedText source,
        string fragment,
        int cursor,
        string precedingContext)
    {
        int safeCursor = Math.Clamp(cursor, 0, source.Text.Length);
        if (precedingContext.Length > 0)
        {
            int contextStart = source.Text.IndexOf(precedingContext, safeCursor, StringComparison.Ordinal);
            if (contextStart >= 0) safeCursor = contextStart + precedingContext.Length;
        }
        string normalizedFragment = fragment.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalizedFragment.Length == 0)
        {
            int emptyPosition = source.MapPosition(safeCursor);
            return (emptyPosition, 0, safeCursor);
        }

        int start = source.Text.IndexOf(normalizedFragment, safeCursor, StringComparison.Ordinal);
        int end = start + normalizedFragment.Length;
        if (start < 0)
        {
            int missingPosition = source.MapPosition(safeCursor);
            return (missingPosition, 0, safeCursor);
        }
        int originalStart = source.MapPosition(start);
        int originalEnd = source.MapPosition(end);
        return (originalStart, originalEnd - originalStart, end);
    }

    private static NormalizedText NormalizeForMatching(string source)
    {
        return new(source, source.Replace("\r\n", "\n", StringComparison.Ordinal));
    }


    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private void ShowError(string message)
    {
        SetNotice(message);
        _setStatus(message);
    }

    private void SetNotice(string message)
    {
        _ = NativeMethods.SetWindowText(_noticeLabel, message);
        _toolTip?.Update(_noticeLabel, message);
    }

    private void Close(bool force = false)
    {
        if (_closed)
        {
            return;
        }

        if (!force && _saveInProgress)
        {
            SetNotice(UiText.ConflictApplying);
            return;
        }

        if (!force
            && IsResultDirty
            && !NativeActionConfirmationDialog.Show(
                _handle,
                _settings,
                "关闭冲突解决器",
                "最终结果有未保存修改",
                "关闭后将丢弃最终结果区中的未保存修改。",
                "丢弃修改并关闭",
                danger: true,
                cancelLabel: "继续解决"))
        {
            return;
        }

        _closed = true;
        ClearActionHover(_hoveredActionButton);
        CancelExternalReload();
        CancelPresentation();
        _presentationTimer?.Dispose();
        _presentationTimer = null;
        _lifetimeCancellation.Cancel();
        _resultChangeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _watcher?.Dispose();
        _watcher = null;
        nint handle = _handle;
        NativeMethods.WakeWindowMessageLoop(handle);
        _handle = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    // 接受与手工编辑共用控件保存点；保留外部版本后的差异不随撤销回到旧保存点而消失。
    private bool IsResultDirty => _resultDiffersFromDisk || _result?.IsModified == true;

    private nint ApplyControlColor(nint deviceContext)
    {
        if (deviceContext == 0 || _controlBrush == 0)
        {
            return 0;
        }

        NativeThemePalette palette = NativeTheme.Palette(_dark);
        _ = NativeMethods.SetBackgroundColor(deviceContext, palette.Panel);
        _ = NativeMethods.SetTextColor(deviceContext, palette.Text);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        return _controlBrush;
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        if (item.ControlIdentifier is >= CommandAcceptYours and <= CommandBack)
            return DrawActionButton(item);
        if (item.ControlIdentifier is >= 30 and <= 34)
        {
            NativeThemePalette palette = NativeTheme.Palette(_dark);
            Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
            NativeMethods.Rectangle bounds = item.ItemRectangle;
            int border = Math.Max(1, S(1));
            if (item.ControlIdentifier is >= 30 and <= 32)
            {
                Fill(item.DeviceContext, new() { Left = bounds.Left, Top = bounds.Bottom - border, Right = bounds.Right, Bottom = bounds.Bottom }, palette.Border);
                bounds.Bottom -= border;
            }
            string text = NativeMethods.GetWindowTextValue(item.Control);
            if (item.ControlIdentifier == 34)
                DrawText(item.DeviceContext, text, bounds, palette.Text, NativeTheme.UiMediumFont);
            else
            {
                bounds.Left += S(6);
                bounds.Right -= S(6);
                if (item.ControlIdentifier == 33)
                    DrawText(item.DeviceContext, text, bounds, palette.Muted, NativeTheme.UiFont);
                else DrawColumnTitle(item.DeviceContext, text, bounds, palette.Muted);
            }
            return true;
        }
        return false;
    }

    private nint PaintWindow()
    {
        nint deviceContext = NativeMethods.BeginPaint(_handle, out NativeMethods.PaintStructure paint);
        if (deviceContext == 0)
        {
            return 0;
        }

        try
        {
            if (!NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
            {
                return 0;
            }

            NativeThemePalette palette = NativeTheme.Palette(_dark);
            Fill(deviceContext, client, palette.Panel);
            NativeTheme.FillRounded(deviceContext, client, palette.BorderStrong, S(18));
            int border = Math.Max(1, S(1));
            NativeTheme.FillRounded(deviceContext, new()
            {
                Left = border,
                Top = border,
                Right = client.Right - border,
                Bottom = client.Bottom - border,
            }, palette.Panel, S(16));
            int contentHeaderBottom = border + _headerHeight + _contentHeaderHeight;
            int outerFooterTop = client.Bottom - border - _footerHeight;
            int contentFooterTop = outerFooterTop - _contentFooterHeight;
            Fill(deviceContext, new() { Left = border, Top = _headerHeight, Right = client.Right - border, Bottom = _headerHeight + border }, palette.Border);
            Fill(deviceContext, new() { Left = S(17), Top = contentHeaderBottom - border, Right = client.Right - S(17), Bottom = contentHeaderBottom }, palette.Border);
            Fill(deviceContext, new() { Left = S(17), Top = contentFooterTop, Right = client.Right - S(17), Bottom = contentFooterTop + border }, palette.Border);
            Fill(deviceContext, new() { Left = border, Top = outerFooterTop, Right = client.Right - border, Bottom = outerFooterTop + border }, palette.Border);
            int gap = S(1);
            int available = Math.Max(0, client.Right - S(34) - gap * 2);
            (int leftWidth, int middleWidth, _) = CalculateColumnWidths(available);
            int middle = S(17) + leftWidth + gap;
            int right = middle + middleWidth + gap;
            Fill(deviceContext, new() { Left = middle - gap, Top = contentHeaderBottom, Right = middle, Bottom = contentFooterTop }, palette.Border);
            Fill(deviceContext, new() { Left = right - gap, Top = contentHeaderBottom, Right = right, Bottom = contentFooterTop }, palette.Border);
            DrawText(
                deviceContext,
                UiText.ResolveConflictDialogTitle,
                new() { Left = border + S(13), Top = border, Right = _compactHeader ? border + S(13) + _titleWidth : client.Right - S(55), Bottom = _headerHeight },
                palette.Text,
                NativeTheme.UiMediumFont);
            DrawText(
                deviceContext,
                $"{_remainingBlockCount} 个未处理冲突",
                CountBounds(client.Right),
                palette.Muted,
                NativeTheme.UiFont);
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }

        return 0;
    }

    private nint HitTest()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point)
            || !NativeMethods.ScreenToClient(_handle, ref point))
        {
            return NativeMethods.HitTestClient;
        }

        return point.Y < _headerHeight
            ? NativeMethods.HitTestCaption
            : NativeMethods.HitTestClient;
    }

    private static (int X, int Y) Center(nint owner, int width, int height)
    {
        if (!NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle rectangle))
        {
            return (NativeMethods.UseDefault, NativeMethods.UseDefault);
        }

        return (
            rectangle.Left + Math.Max(0, ((rectangle.Right - rectangle.Left) - width) / 2),
            rectangle.Top + Math.Max(0, ((rectangle.Bottom - rectangle.Top) - height) / 2));
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

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font,
        bool centered = false)
    {
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        nint previous = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            (centered ? NativeMethods.DrawTextCenter : 0) | NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextEndEllipsis);
        if (previous != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previous);
        }
    }

    private static int S(int pixels) => NativeTheme.Scale(pixels);

    private static int ResolveDialogWidth(nint owner)
    {
        if (!NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle rectangle))
        {
            return DialogWidth;
        }

        int physicalWidth = Math.Max(1, rectangle.Right - rectangle.Left);
        int availableLogicalWidth = Math.Max(1, (int)Math.Floor(NativeTheme.Unscale(physicalWidth)));
        return CalculateDialogWidth(availableLogicalWidth);
    }

    private static int CalculateDialogWidth(int availableLogicalWidth)
    {
        return Math.Min(DialogWidth, Math.Max(1, availableLogicalWidth - 80));
    }

    private static string GetDisplayFileName(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        int separator = relativePath.LastIndexOfAny(['/', '\\']);
        return separator >= 0 && separator + 1 < relativePath.Length
            ? relativePath[(separator + 1)..]
            : relativePath;
    }

    private readonly record struct ConflictViewBlock(
        int YoursStart,
        int YoursLength,
        int ResultStart,
        int ResultLength,
        int TheirsStart,
        int TheirsLength,
        int YoursLine,
        int ResultLine,
        int TheirsLine,
        int YoursLineCount,
        int ResultLineCount,
        int TheirsLineCount)
    {
        internal int ResultDisplayLine { get; init; }
        internal int ResultDisplayLineCount { get; init; }
        internal int VirtualStart { get; init; }
        internal int VirtualLength { get; init; }
    }

    private sealed class NormalizedText(string original, string normalized)
    {
        private int _originalCursor;
        private int _normalizedCursor;
        internal string Text { get; } = normalized;

        internal int MapPosition(int normalizedPosition)
        {
            int position = Math.Clamp(normalizedPosition, 0, Text.Length);
            if (original.Length == Text.Length) return position;

            // 冲突块按原文顺序匹配；位置映射只向前走，不为每个字符或换行保存索引。
            while (_normalizedCursor < position)
            {
                SkipCarriageReturn();
                _originalCursor++;
                _normalizedCursor++;
            }
            SkipCarriageReturn();
            return _originalCursor;
        }

        private void SkipCarriageReturn()
        {
            if (_originalCursor + 1 < original.Length && original[_originalCursor] == '\r'
                && original[_originalCursor + 1] == '\n') _originalCursor++;
        }
    }

    private sealed class LineCursor(string text)
    {
        private int _position;
        private int _line;

        internal int GetLine(int position)
        {
            int target = Math.Clamp(position, _position, text.Length);
            _line += text.AsSpan(_position, target - _position).Count('\n');
            _position = target;
            return _line;
        }

        internal int GetCoveredLineCount(int start, int length, int startLine)
        {
            if (length <= 0) return 0;
            int endLine = GetLine(start + length);
            return Math.Max(1, endLine - startLine);
        }
    }
}
