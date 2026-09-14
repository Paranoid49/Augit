using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeStashManagerDialog : IDisposable
{
    private const string WindowClassName = "Augit.StashManagerDialog.Native";
    private const int DialogWidth = 930;
    private const int DialogHeight = 411;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int SidebarWidth = 260;
    private const int StashListIdentifier = 1;
    private const int FileListIdentifier = 2;
    private const int CommandAdd = 10;
    private const int CommandDeleteToolbar = 11;
    private const int CommandRefresh = 12;
    private const int CommandApply = 13;
    private const int CommandPop = 14;
    private const int CommandView = 15;
    private const int CommandDelete = 16;
    private const int CommandCancelOperation = 17;
    private const int CommandClose = 18;
    private const int CommandHeaderClose = 19;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeStashManagerDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly GitWorkspaceStateService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly string? _currentBranch;
    private readonly List<GitStashInfo> _stashes = [];
    private readonly List<string> _fileRows = [];
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private nint _stashList;
    private nint _detailTitle;
    private nint _detailMeta;
    private nint _fileHeading;
    private nint _fileList;
    private nint _addButton;
    private nint _deleteToolbarButton;
    private nint _refreshButton;
    private nint _applyButton;
    private nint _popButton;
    private nint _viewButton;
    private nint _deleteButton;
    private nint _cancelOperationButton;
    private nint _closeButton;
    private nint _headerCloseButton;
    private nint _noticeLabel;
    private nint _controlBrush;
    private nint _headingFont;
    private NativeToolTip? _toolTip;
    private NativeDialogBody? _bodyView;
    private NativeDialogActionButtons? _buttons;
    private int _headerHeight, _footerHeight, _toolbarHeight, _buttonHeight;
    private int _sidebarBoundary, _contentWidth, _bodyContentHeight;
    private string _notice = string.Empty;
    private int _detailVersion;
    private bool _dark;
    private bool _operationRunning;
    private bool _closed;

    private NativeStashManagerDialog(
        nint owner,
        GitRepositorySnapshot repository,
        GitWorkspaceStateService service,
        ApplicationSettings settings,
        Action<string> setStatus,
        string? currentBranch)
    {
        _owner = owner;
        _repository = repository;
        _service = service;
        _settings = settings;
        _setStatus = setStatus;
        _currentBranch = currentBranch;
        EnsureWindowClass();
        _headingFont = NativeTheme.CreateOwnedUiFont(settings.TextFontFamily, settings.UiFontSize, 600);
        (int x, int y) = Center(owner, S(DialogWidth), S(DialogHeight));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.StashManagement,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            x,
            y,
            S(DialogWidth),
            S(DialogHeight),
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.StashManagerCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        try
        {
            _bodyView = new NativeDialogBody(_handle, MeasureBody, LayoutBody, PaintBody);
            CreateControls();
            ApplyAppearance();
            MeasureLayout();
            ResizeToContent();
        }
        catch
        {
            Dispose();
            throw;
        }
        _ = LoadAsync();
    }

    internal static void Show(
        nint owner,
        GitRepositorySnapshot repository,
        GitWorkspaceStateService service,
        ApplicationSettings settings,
        Action<string> setStatus,
        string? currentBranch = null)
    {
        using NativeStashManagerDialog dialog = new(owner, repository, service, settings, setStatus, currentBranch);
        dialog.Run();
    }

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight, int SidebarWidth) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight, SidebarWidth);

    internal int StashCountForTest => _stashes.Count;

    internal bool ManagementLayoutForTest => _stashList != 0 && _fileList != 0 && _detailTitle != 0;

    internal static IReadOnlyList<int> TabOrderForTest =>
        [
            StashListIdentifier,
            FileListIdentifier,
            CommandApply,
            CommandPop,
            CommandView,
            CommandDelete,
            CommandClose,
            CommandHeaderClose,
            CommandAdd,
            CommandDeleteToolbar,
            CommandRefresh,
        ];

    internal static IReadOnlyList<string> ActionLabelsForTest =>
        [UiText.ApplyStash, UiText.PopStash, UiText.ViewStash, UiText.DeleteStash];

    internal static string FormatListEntryForTest(GitStashInfo stash) =>
        $"{stash.Reference} {stash.Message}";

    internal static string FormatDetailTitleForTest(GitStashInfo stash) =>
        $"{stash.Reference} · {stash.Message}";

    internal static string FormatDetailMetadataForTest(GitStashInfo stash, string? currentBranch)
    {
        string branch = stash.Branch.Length == 0 ? currentBranch ?? "当前分支" : stash.Branch;
        string shortHash = stash.CommitHash.Length > 7 ? stash.CommitHash[..7] : stash.CommitHash;
        return $"{branch} · {shortHash} · {stash.Date.LocalDateTime:g}";
    }

    private void Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_stashList);
        try
        {
            while (!_closed && NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0) > 0)
            {
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

    /// <summary>
    /// 按视觉稿顺序在 Stash 列表、详情和操作区内循环移动焦点。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [
                _stashList,
                _fileList,
                _applyButton,
                _popButton,
                _viewButton,
                _deleteButton,
                _closeButton,
                _headerCloseButton,
                _addButton,
                _deleteToolbarButton,
                _refreshButton,
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
                throw new Win32Exception(error, UiText.StashManagerClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeStashManagerDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message == NativeMethods.WindowMessageDrawItem)
        {
            return instance.DrawControl(longParameter) ? 1 : 0;
        }

        if (message is NativeMethods.WindowMessageControlColorEdit
            or NativeMethods.WindowMessageControlColorListBox
            or NativeMethods.WindowMessageControlColorButton
            or NativeMethods.WindowMessageControlColorStatic)
        {
            return instance.ApplyControlColor(unchecked((nint)wordParameter));
        }

        return message switch
        {
            NativeMethods.WindowMessagePaint => instance.PaintWindow(),
            NativeMethods.WindowMessageEraseBackground => 1,
            NativeMethods.WindowMessageNonClientHitTest => instance.HitTest(),
            NativeMethods.WindowMessageSize => instance.LayoutMessage(),
            NativeMethods.WindowMessageCommand => instance.CommandMessage(wordParameter),
            NativeMethods.WindowMessageClose => instance.CloseMessage(),
            _ => NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter),
        };
    }

    private void CreateControls()
    {
        _addButton = CreateControl(NativeMethods.ButtonClass, UiText.AddSymbol, CommandAdd, NativeMethods.ButtonOwnerDraw);
        _deleteToolbarButton = CreateControl(NativeMethods.ButtonClass, UiText.DeleteSymbol, CommandDeleteToolbar, NativeMethods.ButtonOwnerDraw);
        _refreshButton = CreateControl(NativeMethods.ButtonClass, UiText.RefreshSymbol, CommandRefresh, NativeMethods.ButtonOwnerDraw);
        _stashList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            StashListIdentifier,
            NativeMethods.WindowStyleVerticalScroll
            | NativeMethods.ListBoxNotify
            | NativeMethods.ListBoxNoIntegralHeight
            | NativeMethods.ListBoxOwnerDrawFixed
            | NativeMethods.ListBoxHasStrings);
        _ = NativeMethods.SendMessage(_stashList, NativeMethods.ListBoxSetItemHeight, 0, unchecked((nint)S(30)));
        _detailTitle = CreateControl(NativeMethods.StaticClass, UiText.NoStashes, 0, NativeMethods.StaticLeft, _bodyView!.Handle);
        _detailMeta = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft, _bodyView.Handle);
        _fileHeading = CreateControl(NativeMethods.StaticClass, UiText.StashChangedFiles, 0, NativeMethods.StaticLeft, _bodyView.Handle);
        _fileList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            FileListIdentifier,
            NativeMethods.WindowStyleVerticalScroll
            | NativeMethods.ListBoxNoIntegralHeight
            | NativeMethods.ListBoxOwnerDrawFixed
            | NativeMethods.ListBoxHasStrings,
            _bodyView.Handle);
        _ = NativeMethods.SendMessage(_fileList, NativeMethods.ListBoxSetItemHeight, 0, unchecked((nint)S(28)));
        _applyButton = CreateControl(NativeMethods.ButtonClass, UiText.ApplyStash, CommandApply, NativeMethods.ButtonOwnerDraw, _bodyView.Handle);
        _popButton = CreateControl(NativeMethods.ButtonClass, UiText.PopStash, CommandPop, NativeMethods.ButtonOwnerDraw, _bodyView.Handle);
        _viewButton = CreateControl(NativeMethods.ButtonClass, UiText.ViewStash, CommandView, NativeMethods.ButtonOwnerDraw, _bodyView.Handle);
        _deleteButton = CreateControl(NativeMethods.ButtonClass, UiText.DeleteStash, CommandDelete, NativeMethods.ButtonOwnerDraw, _bodyView.Handle);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft, _bodyView.Handle);
        _cancelOperationButton = CreateControl(NativeMethods.ButtonClass, UiText.CancelOperation, CommandCancelOperation, NativeMethods.ButtonOwnerDraw);
        _closeButton = CreateControl(NativeMethods.ButtonClass, UiText.Close, CommandClose, NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(NativeMethods.ButtonClass, UiText.CloseSymbol, CommandHeaderClose, NativeMethods.ButtonOwnerDraw);
        _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowHide);
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_addButton, UiText.Add);
        _toolTip.Add(_deleteToolbarButton, UiText.Delete);
        _toolTip.Add(_refreshButton, UiText.Refresh);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        foreach (nint control in new[] { _detailTitle, _detailMeta, _fileHeading, _fileList, _applyButton, _popButton, _viewButton, _deleteButton, _noticeLabel })
        {
            _bodyView.Register(control, control == _fileList);
        }
        _buttons = new NativeDialogActionButtons(_handle, _addButton, _deleteToolbarButton, _refreshButton,
            _applyButton, _popButton, _viewButton, _deleteButton, _cancelOperationButton, _closeButton, _headerCloseButton);
        Layout();
    }

    private nint CreateControl(string className, string text, int identifier, uint style, nint parent = 0)
    {
        uint commonStyle = NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible;
        if (!className.Equals(NativeMethods.StaticClass, StringComparison.Ordinal))
        {
            commonStyle |= NativeMethods.WindowStyleTabStop;
        }

        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            commonStyle | style,
            0,
            0,
            0,
            0,
            parent == 0 ? _handle : parent,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.StashManagerControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        return control;
    }

    private nint CommandMessage(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == StashListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            PopulateSelected();
            return 0;
        }

        HandleCommand(command);
        return 0;
    }

    private void HandleCommand(int command)
    {
        switch (command)
        {
            case CommandAdd:
                if (NativeStashDialog.Show(_handle, _repository, _service, _settings, _setStatus, _currentBranch))
                {
                    _ = LoadAsync();
                }
                break;
            case CommandDeleteToolbar:
            case CommandDelete:
                _ = DeleteAsync();
                break;
            case CommandRefresh:
                _ = LoadAsync();
                break;
            case CommandApply:
                _ = UnstashAsync(keepStash: true);
                break;
            case CommandPop:
                _ = UnstashAsync(keepStash: false);
                break;
            case CommandView:
                _ = ViewAsync();
                break;
            case CommandCancelOperation:
                _operationCancellation?.Cancel();
                break;
            case CommandClose:
            case CommandHeaderClose:
                Close();
                break;
        }
    }

    private async Task LoadAsync()
    {
        if (_closed || _operationRunning)
        {
            return;
        }

        SetNotice(UiText.ReadingStashes);
        GitStashListResult result = await _service.ReadStashesAsync(_repository);
        if (_closed)
        {
            return;
        }

        if (!result.IsSuccess || result.Stashes is null)
        {
            ShowError(result.ErrorMessage ?? UiText.GitUnavailable);
            return;
        }

        string? selected = SelectedStash?.Reference;
        _stashes.Clear();
        _stashes.AddRange(result.Stashes);
        _ = NativeMethods.SendMessage(_stashList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitStashInfo stash in _stashes)
        {
            _ = NativeMethods.SendMessage(_stashList, NativeMethods.ListBoxAddString, 0, FormatListEntryForTest(stash));
        }

        if (_stashes.Count == 0)
        {
            ClearDetails();
            SetNotice(UiText.NoStashes);
            return;
        }

        int index = selected is null ? 0 : _stashes.FindIndex(stash => stash.Reference.Equals(selected, StringComparison.Ordinal));
        if (index < 0)
        {
            index = 0;
        }

        _ = NativeMethods.SendMessage(_stashList, NativeMethods.ListBoxSetCurrentSelection, unchecked((nuint)index), 0);
        PopulateSelected();
        SetNotice(string.Empty);
    }

    private async Task UnstashAsync(bool keepStash)
    {
        GitStashInfo? stash = SelectedStash;
        if (stash is null)
        {
            ShowError(UiText.SelectStashFirst);
            return;
        }

        await RunActionAsync(
            token => _service.UnstashAsync(_repository, stash.Reference, keepStash, token),
            keepStash ? UiText.ApplyStash : UiText.PopStash);
    }

    private async Task DeleteAsync()
    {
        GitStashInfo? stash = SelectedStash;
        if (stash is null)
        {
            ShowError(UiText.SelectStashFirst);
            return;
        }

        if (!NativeActionConfirmationDialog.Show(
                _handle,
                _settings,
                UiText.DeleteStashTitle,
                UiText.DeleteStashHeading,
                UiText.ConfirmDeleteStashDetails(stash.Reference),
                UiText.DeleteStashTitle,
                danger: true))
        {
            return;
        }

        await RunActionAsync(
            token => _service.DeleteStashAsync(_repository, stash.Reference, token),
            UiText.DeleteStash);
    }

    private async Task ViewAsync()
    {
        GitStashInfo? stash = SelectedStash;
        if (stash is null)
        {
            ShowError(UiText.SelectStashFirst);
            return;
        }

        SetNotice(UiText.ReadingStashDetails);
        GitStashContentResult result = await _service.ReadStashContentAsync(_repository, stash.Reference);
        if (_closed)
        {
            return;
        }

        if (!result.IsSuccess || result.Document is null)
        {
            ShowError(result.ErrorMessage ?? UiText.GitUnavailable);
            return;
        }

        string content = result.Document.Status switch
        {
            GitDiffContentStatus.Ready => result.Document.UnifiedPatch ?? UiText.NoTextDiff,
            GitDiffContentStatus.Binary => UiText.BinaryDiffSummary,
            GitDiffContentStatus.OutputTooLarge => UiText.DiffOutputTooLarge,
            _ => UiText.NoTextDiff,
        };
        NativeGitTextDialog.Show(_handle, $"{UiText.ViewStash} · {stash.Reference}", content, _settings);
        SetNotice(UiText.StashOperationCompleted);
    }

    private async Task RunActionAsync(Func<CancellationToken, Task<GitActionResult>> action, string operation)
    {
        if (_operationRunning || _closed)
        {
            return;
        }

        _operationRunning = true;
        _operationCancellation = new();
        SetOperationControlsEnabled(false);
        _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowNormal);
        SetNotice($"正在执行 {operation}…");
        try
        {
            GitActionResult result = await action(_operationCancellation.Token);
            if (_closed)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                ShowError(result.ErrorMessage ?? UiText.GitUnavailable);
                return;
            }

            _setStatus(UiText.StashOperationCompleted);
            SetNotice(UiText.StashOperationCompleted);
        }
        catch (OperationCanceledException)
        {
            SetNotice(UiText.OperationCancelled);
        }
        finally
        {
            _operationRunning = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            if (!_closed)
            {
                SetOperationControlsEnabled(true);
                _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowHide);
                await LoadAsync();
            }
        }
    }

    private void PopulateSelected()
    {
        GitStashInfo? stash = SelectedStash;
        if (stash is null)
        {
            ClearDetails();
            return;
        }

        _ = NativeMethods.SetWindowText(_detailTitle, FormatDetailTitleForTest(stash));
        _ = NativeMethods.SetWindowText(_detailMeta, FormatDetailMetadataForTest(stash, _currentBranch));
        _ = NativeMethods.SetWindowText(_fileHeading, UiText.StashChangedFiles);
        SetFileRows([UiText.ReadingStashDetails]);
        _ = NativeMethods.EnableWindow(_applyButton, !_operationRunning);
        _ = NativeMethods.EnableWindow(_popButton, !_operationRunning);
        _ = NativeMethods.EnableWindow(_viewButton, !_operationRunning);
        _ = NativeMethods.EnableWindow(_deleteButton, !_operationRunning);
        int version = ++_detailVersion;
        _ = LoadStashFilesAsync(stash, version);
    }

    private async Task LoadStashFilesAsync(GitStashInfo stash, int version)
    {
        GitStashContentResult result = await _service.ReadStashContentAsync(_repository, stash.Reference);
        if (_closed || version != _detailVersion || !ReferenceEquals(SelectedStash, stash))
        {
            return;
        }

        if (!result.IsSuccess || result.Document is null)
        {
            _ = NativeMethods.SetWindowText(_fileHeading, UiText.StashChangedFiles);
            SetFileRows([result.ErrorMessage ?? UiText.GitUnavailable]);
            return;
        }

        string[] files = ExtractChangedFiles(result.Document.UnifiedPatch);
        if (files.Length == 0)
        {
            _ = NativeMethods.SetWindowText(_fileHeading, UiText.StashChangedFileCount(0));
            SetFileRows([UiText.NoTextDiff]);
            return;
        }

        _ = NativeMethods.SetWindowText(_fileHeading, UiText.StashChangedFileCount(files.Length));
        SetFileRows(files);
    }

    private static string[] ExtractChangedFiles(string? patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            return [];
        }

        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in patch.Split('\n'))
        {
            if (!line.StartsWith("diff --git a/", StringComparison.Ordinal))
            {
                continue;
            }

            int separator = line.IndexOf(" b/", StringComparison.Ordinal);
            if (separator > 12)
            {
                files.Add(line[(separator + 3)..].TrimEnd('\r'));
            }
        }

        return files.ToArray();
    }

    private void ClearDetails()
    {
        _detailVersion++;
        _ = NativeMethods.SetWindowText(_detailTitle, UiText.NoStashes);
        _ = NativeMethods.SetWindowText(_detailMeta, string.Empty);
        _ = NativeMethods.SetWindowText(_fileHeading, UiText.StashChangedFiles);
        SetFileRows([]);
        foreach (nint button in new[] { _applyButton, _popButton, _viewButton, _deleteButton })
        {
            _ = NativeMethods.EnableWindow(button, false);
        }
    }

    private GitStashInfo? SelectedStash
    {
        get
        {
            int index = checked((int)NativeMethods.SendMessage(_stashList, NativeMethods.ListBoxGetCurrentSelection, 0, 0));
            return index >= 0 && index < _stashes.Count ? _stashes[index] : null;
        }
    }

    private void SetFileRows(IEnumerable<string> rows)
    {
        _fileRows.Clear();
        _fileRows.AddRange(rows);
        _ = NativeMethods.SendMessage(_fileList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (string row in _fileRows)
        {
            _ = NativeMethods.SendMessage(_fileList, NativeMethods.ListBoxAddString, 0, row);
        }

        _bodyView?.Relayout();
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _stashList,
            _fileList,
            _addButton,
            _deleteToolbarButton,
            _refreshButton,
            _applyButton,
            _popButton,
            _viewButton,
            _deleteButton,
            _closeButton,
            _headerCloseButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }
    }

    private void ApplyAppearance()
    {
        _dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, _dark);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }

        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        foreach (nint control in new[]
        {
            _stashList,
            _detailTitle,
            _detailMeta,
            _fileHeading,
            _fileList,
            _addButton,
            _deleteToolbarButton,
            _refreshButton,
            _applyButton,
            _popButton,
            _viewButton,
            _deleteButton,
            _cancelOperationButton,
            _closeButton,
            _headerCloseButton,
            _noticeLabel,
        })
        {
            NativeTheme.ApplyToControl(control, _dark);
            _ = NativeMethods.InvalidateRectangle(control, 0, true);
        }

        if (_headingFont != 0)
        {
            _ = NativeMethods.SendMessage(_detailTitle, NativeMethods.WindowMessageSetFont, unchecked((nuint)_headingFont), 1);
        }

        _toolTip?.ApplyAppearance(_dark);
        _bodyView?.Relayout();
    }

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
        int identifier = unchecked((int)item.ControlIdentifier);
        if (identifier == StashListIdentifier)
        {
            return DrawStashListItem(item);
        }

        if (identifier == FileListIdentifier)
        {
            return DrawFileListItem(item);
        }

        if (_buttons is null) return false;
        return identifier switch
        {
            CommandAdd or CommandDeleteToolbar or CommandRefresh or CommandHeaderClose => _buttons.Draw(item, _dark, NativeDialogActionButtons.Style.Icon),
            CommandApply => _buttons.Draw(item, _dark, NativeDialogActionButtons.Style.Primary),
            CommandDelete => _buttons.Draw(item, _dark, NativeDialogActionButtons.Style.Danger),
            CommandView or CommandPop => _buttons.Draw(item, _dark, NativeDialogActionButtons.Style.Secondary),
            CommandCancelOperation or CommandClose => _buttons.Draw(item, _dark, NativeDialogActionButtons.Style.Secondary),
            _ => false,
        };
    }

    private bool DrawStashListItem(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        NativeMethods.Rectangle rectangle = item.ItemRectangle;
        Fill(item.DeviceContext, rectangle, palette.Panel);
        if ((item.ItemState & NativeMethods.OwnerDrawSelected) != 0)
        {
            NativeMethods.Rectangle selected = rectangle;
            selected.Left += S(3);
            selected.Right -= S(3);
            selected.Top += S(1);
            selected.Bottom -= S(1);
            FillRounded(item.DeviceContext, selected, palette.AccentSoft, S(5));
        }

        int index = unchecked((int)item.ItemIdentifier);
        if (index >= 0 && index < _stashes.Count)
        {
            DrawOutlineIcon(item.DeviceContext, rectangle, archive: true, palette.Muted);
            NativeMethods.Rectangle text = rectangle;
            text.Left += S(35);
            text.Right -= S(7);
            DrawText(
                item.DeviceContext,
                FormatListEntryForTest(_stashes[index]),
                text,
                palette.Text,
                NativeTheme.UiFont);
        }

        return true;
    }

    private bool DrawFileListItem(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        NativeMethods.Rectangle rectangle = item.ItemRectangle;
        Fill(item.DeviceContext, rectangle, palette.Panel);
        int index = unchecked((int)item.ItemIdentifier);
        if (index >= 0 && index < _fileRows.Count)
        {
            DrawOutlineIcon(item.DeviceContext, rectangle, archive: false, palette.Muted);
            NativeMethods.Rectangle text = rectangle;
            text.Left += S(34);
            text.Right -= S(7);
            DrawText(item.DeviceContext, _fileRows[index], text, palette.Text, NativeTheme.UiFont);
        }

        return true;
    }

    private static void DrawOutlineIcon(
        nint deviceContext,
        NativeMethods.Rectangle row,
        bool archive,
        uint color)
    {
        int left = row.Left + S(12);
        int top = ((row.Top + row.Bottom) / 2) - S(6);
        int right = left + S(12);
        int bottom = top + S(12);
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, S(1)), color);
        if (pen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(
            deviceContext,
            NativeMethods.GetStockObject(NativeMethods.NullBrush));
        if (archive)
        {
            _ = NativeMethods.DrawRectangle(deviceContext, left, top + S(3), right, bottom);
            _ = NativeMethods.MoveTo(deviceContext, left, top, 0);
            _ = NativeMethods.LineTo(deviceContext, right, top);
            _ = NativeMethods.LineTo(deviceContext, right, top + S(3));
            _ = NativeMethods.LineTo(deviceContext, left, top + S(3));
            _ = NativeMethods.LineTo(deviceContext, left, top);
            _ = NativeMethods.MoveTo(deviceContext, left + S(4), top + S(6), 0);
            _ = NativeMethods.LineTo(deviceContext, right - S(4), top + S(6));
        }
        else
        {
            _ = NativeMethods.DrawRectangle(deviceContext, left + S(1), top, right - S(1), bottom);
        }

        if (previousBrush != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
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
            NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client);
            NativeThemePalette palette = NativeTheme.Palette(_dark);
            Fill(deviceContext, client, palette.Panel);
            int footer = client.Bottom - _footerHeight;
            Fill(deviceContext, new() { Left = _sidebarBoundary, Top = _headerHeight + _toolbarHeight, Right = _sidebarBoundary + S(1), Bottom = footer }, palette.Border);
            Fill(deviceContext, new() { Top = _headerHeight, Right = client.Right, Bottom = _headerHeight + S(1) }, palette.Border);
            Fill(deviceContext, new() { Top = footer, Right = client.Right, Bottom = footer + S(1) }, palette.Border);
            int toolbarCenter = _headerHeight + _toolbarHeight / 2;
            Fill(deviceContext, new() { Left = S(120), Top = toolbarCenter - S(9), Right = S(121), Bottom = toolbarCenter + S(9) }, palette.Border);
            DrawText(deviceContext, UiText.StashManagement, new() { Left = S(26), Right = client.Right - S(60), Bottom = _headerHeight }, palette.Text, NativeTheme.UiMediumFont);
            DrawText(deviceContext, UiText.StashManagement, new() { Left = S(125), Top = _headerHeight, Right = client.Right - S(17), Bottom = _headerHeight + _toolbarHeight }, palette.Text, NativeTheme.UiMediumFont);
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }

        return 0;
    }

    private void Layout()
    {
        if (_bodyView is null || _headerHeight == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = client.Right;
        int height = client.Bottom;
        int sidebar = Math.Min(S(SidebarWidth), Math.Max(S(220), width / 3));
        int sidebarLeft = S(16);
        _sidebarBoundary = sidebarLeft + sidebar;
        int contentTop = _headerHeight + _toolbarHeight;
        int footer = client.Bottom - _footerHeight;
        int bodyHeight = Math.Max(0, footer - contentTop - S(16));
        Move(_addButton, S(16), _headerHeight + (_toolbarHeight - S(28)) / 2, S(28), S(28));
        Move(_deleteToolbarButton, S(50), _headerHeight + (_toolbarHeight - S(28)) / 2, S(28), S(28));
        Move(_refreshButton, S(84), _headerHeight + (_toolbarHeight - S(28)) / 2, S(28), S(28));
        Move(_stashList, sidebarLeft, contentTop, Math.Max(S(150), sidebar - S(8)), bodyHeight);
        Move(_bodyView.Handle, _sidebarBoundary + S(1), contentTop, Math.Max(S(1), width - _sidebarBoundary - S(17)), bodyHeight);
        int actionTop = footer + (_footerHeight - _buttonHeight) / 2;
        int closeWidth = Math.Max(S(78), NativeDialogBody.Measure(_handle, UiText.Close).Width + S(24));
        int cancelWidth = Math.Max(S(100), NativeDialogBody.Measure(_handle, UiText.CancelOperation).Width + S(24));
        Move(_closeButton, width - S(17) - closeWidth, actionTop, closeWidth, _buttonHeight);
        Move(_cancelOperationButton, width - S(25) - closeWidth - cancelWidth, actionTop, cancelWidth, _buttonHeight);
        Move(_headerCloseButton, width - S(45), (_headerHeight - S(31)) / 2, S(32), S(31));
        _bodyView.Relayout();
        _ = NativeMethods.InvalidateRectangle(_handle, 0, false);
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private void MeasureLayout()
    {
        int line = NativeTheme.UiLineHeight;
        _headerHeight = Math.Max(S(HeaderHeight), line + S(16));
        _buttonHeight = Math.Max(S(28), line + S(8));
        _footerHeight = Math.Max(S(FooterHeight), _buttonHeight + S(25));
        _toolbarHeight = Math.Max(S(61), line + S(28));
        _ = NativeMethods.SendMessage(_stashList, NativeMethods.ListBoxSetItemHeight, 0, Math.Max(S(27), line + S(8)));
        _ = NativeMethods.SendMessage(_fileList, NativeMethods.ListBoxSetItemHeight, 0, Math.Max(S(27), line + S(6)));
    }

    private void ResizeToContent()
    {
        if (!NativeMethods.GetWindowRectangle(_owner, out var owner)) return;
        int width = Math.Min(S(DialogWidth), Math.Max(1, owner.Right - owner.Left - S(90)));
        int height = Math.Min(Math.Max(S(DialogHeight), _headerHeight + _toolbarHeight + S(220) + _footerHeight),
            Math.Max(1, owner.Bottom - owner.Top - S(88)));
        _ = NativeMethods.SetWindowPosition(_handle, 0,
            owner.Left + (owner.Right - owner.Left - width) / 2,
            owner.Top + (owner.Bottom - owner.Top - height) / 2,
            width, height,
            NativeMethods.SetWindowPositionNoActivate | NativeMethods.SetWindowPositionNoZOrder);
        Layout();
    }

    private int MeasureBody(int width)
    {
        int line = NativeTheme.UiLineHeight;
        int inner = Math.Max(S(1), width - S(36));
        int top = S(14) + Math.Max(line, S(30)) + S(6);
        top += Math.Max(line, S(24)) + S(6);
        int actionWidth = Math.Max(S(78), NativeDialogBody.Measure(_handle, UiText.ApplyStash).Width + S(24));
        int popWidth = Math.Max(S(78), NativeDialogBody.Measure(_handle, UiText.PopStash).Width + S(24));
        int viewWidth = Math.Max(S(96), NativeDialogBody.Measure(_handle, UiText.ViewStash).Width + S(24));
        int deleteWidth = Math.Max(S(78), NativeDialogBody.Measure(_handle, UiText.DeleteStash).Width + S(24));
        int actionsWidth = actionWidth + popWidth + viewWidth + deleteWidth + S(24);
        top += actionsWidth > inner ? _buttonHeight * 2 + S(8) : _buttonHeight;
        top += S(14) + Math.Max(line, S(24)) + S(6);
        int rows = Math.Max(1, _fileRows.Count);
        top += Math.Max(S(60), rows * Math.Max(S(27), line + S(6)) + S(8));
        if (_notice.Length != 0)
        {
            top += S(12) + Math.Max(line, NativeDialogBody.Measure(_handle, _notice, inner).Height);
        }
        _bodyContentHeight = top + S(16);
        _contentWidth = width;
        return _bodyContentHeight;
    }

    private void LayoutBody(int width, int height, int offset)
    {
        int line = NativeTheme.UiLineHeight;
        int inner = Math.Max(S(1), width - S(36));
        int left = S(18);
        int top = S(14);
        Move(_detailTitle, left, top - offset, inner, Math.Max(line, S(30)));
        top += Math.Max(line, S(30)) + S(6);
        Move(_detailMeta, left, top - offset, inner, Math.Max(line, S(24)));
        top += Math.Max(line, S(24)) + S(8);
        int[] widths =
        [
            Math.Max(S(78), NativeDialogBody.Measure(_handle, UiText.ApplyStash).Width + S(24)),
            Math.Max(S(78), NativeDialogBody.Measure(_handle, UiText.PopStash).Width + S(24)),
            Math.Max(S(96), NativeDialogBody.Measure(_handle, UiText.ViewStash).Width + S(24)),
            Math.Max(S(78), NativeDialogBody.Measure(_handle, UiText.DeleteStash).Width + S(24)),
        ];
        nint[] buttons = [_applyButton, _popButton, _viewButton, _deleteButton];
        int x = left;
        for (int index = 0; index < buttons.Length; index++)
        {
            if (x != left && x + widths[index] > width - left)
            {
                x = left;
                top += _buttonHeight + S(8);
            }
            Move(buttons[index], x, top - offset, widths[index], _buttonHeight);
            x += widths[index] + S(8);
        }
        top += _buttonHeight + S(14);
        Move(_fileHeading, left, top - offset, inner, Math.Max(line, S(24)));
        top += Math.Max(line, S(24)) + S(6);
        int fileHeight = Math.Max(S(60), height - Math.Max(0, top - offset) - S(16));
        Move(_fileList, left, top - offset, inner, fileHeight);
        top += fileHeight + S(12);
        int noticeHeight = _notice.Length == 0 ? 0 : Math.Max(line, NativeDialogBody.Measure(_handle, _notice, inner).Height);
        Move(_noticeLabel, left, top - offset, inner, noticeHeight);
        _ = NativeMethods.ShowWindow(_noticeLabel, noticeHeight == 0 ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
        _ = NativeMethods.InvalidateRectangle(_bodyView!.Handle, 0, false);
    }

    private void PaintBody(nint deviceContext)
    {
        if (_bodyView is null || !NativeMethods.GetClientRectangle(_bodyView.Handle, out var client)) return;
        Fill(deviceContext, client, NativeTheme.Palette(_dark).Panel);
    }

    private nint LayoutMessage()
    {
        Layout();
        return 0;
    }

    private nint CloseMessage()
    {
        Close();
        return 0;
    }

    private nint HitTest()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point) || !NativeMethods.ScreenToClient(_handle, ref point))
        {
            return NativeMethods.HitTestClient;
        }

        return point.Y < S(HeaderHeight) && point.X < S(DialogWidth - 54) ? NativeMethods.HitTestCaption : NativeMethods.HitTestClient;
    }

    private void SetNotice(string message)
    {
        _notice = message;
        _ = NativeMethods.SetWindowText(_noticeLabel, message);
        _toolTip?.Update(_noticeLabel, message);
        _bodyView?.Relayout();
    }

    private void ShowError(string message)
    {
        SetNotice(message);
        _setStatus(message);
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        if (_operationRunning)
        {
            _operationCancellation?.Cancel();
            return;
        }

        _closed = true;
        _operationCancellation?.Cancel();
        nint handle = _handle;
        NativeMethods.WakeWindowMessageLoop(handle);
        _handle = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        _toolTip?.Dispose();
        _toolTip = null;
        _buttons?.Dispose();
        _buttons = null;
        _bodyView?.Dispose();
        _bodyView = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        if (_headingFont != 0)
        {
            _ = NativeMethods.DeleteObject(_headingFont);
            _headingFont = 0;
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    private static (int X, int Y) Center(nint owner, int width, int height)
    {
        if (!NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle rectangle))
        {
            return (NativeMethods.UseDefault, NativeMethods.UseDefault);
        }

        return (rectangle.Left + Math.Max(0, ((rectangle.Right - rectangle.Left) - width) / 2), rectangle.Top + Math.Max(0, ((rectangle.Bottom - rectangle.Top) - height) / 2));
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
        if (!NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, rectangle, color, radius))
        {
            Fill(deviceContext, rectangle, color);
        }
    }

    private static void DrawText(nint deviceContext, string text, NativeMethods.Rectangle rectangle, uint color, nint font)
    {
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        nint previous = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis);
        if (previous != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previous);
        }
    }

    private static void DrawHelpIcon(nint deviceContext, int footerTop, uint color)
    {
        NativeMethods.Rectangle circle = new()
        {
            Left = S(15),
            Top = footerTop + S(17),
            Right = S(29),
            Bottom = footerTop + S(31),
        };
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, S(1)), color);
        nint previousPen = pen == 0 ? 0 : NativeMethods.SelectObject(deviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(
            deviceContext,
            NativeMethods.GetStockObject(NativeMethods.NullBrush));
        _ = NativeMethods.DrawEllipse(deviceContext, circle.Left, circle.Top, circle.Right, circle.Bottom);
        if (previousBrush != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }

        if (pen != 0)
        {
            _ = NativeMethods.DeleteObject(pen);
        }

        DrawText(deviceContext, "?", circle, color, NativeTheme.UiSmallFont);
    }

    private static int S(int pixels) => NativeTheme.Scale(pixels);
}
