using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed partial class NativeWorktreeManagerDialog : IDisposable
{
    private const string WindowClassName = "Augit.WorktreeManagerDialog.Native";
    private const int DialogWidth = 930;
    private const int DialogHeight = 365;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int SidebarWidth = 260;
    private const int ListIdentifier = 1;
    private const int CommandAdd = 10;
    private const int CommandDeleteToolbar = 11;
    private const int CommandRefresh = 12;
    private const int CommandOpen = 13;
    private const int CommandCreate = 14;
    private const int CommandRemove = 15;
    private const int CommandCancelOperation = 16;
    private const int CommandClose = 17;
    private const int CommandHeaderClose = 18;
    private const int CommandNew = 19;
    private const int ControlPath = 20;
    private const int ControlSourceBranch = 21;
    private const int ControlNewBranch = 22;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeWorktreeManagerDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly IGitWorktreeService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly List<GitWorktreeInfo> _worktrees = [];
    private readonly List<nint> _detailLabels = [];
    private readonly List<nint> _createLabels = [];
    private nint _handle;
    private nint _worktreeList;
    private nint _detailTitle;
    private nint _pathValue;
    private nint _pathEdit;
    private nint _branchEdit;
    private nint _newBranchEdit;
    private nint _statusLabel;
    private nint _terminalLabel;
    private nint _addButton;
    private nint _deleteToolbarButton;
    private nint _refreshButton;
    private nint _openButton;
    private nint _newButton;
    private nint _createButton;
    private nint _removeButton;
    private nint _cancelOperationButton;
    private nint _closeButton;
    private nint _headerCloseButton;
    private nint _noticeLabel;
    private nint _controlBrush;
    private nint _headingFont;
    private NativeToolTip? _toolTip;
    private bool _dark;
    private bool _operationRunning;
    private bool _creatingNew;
    private bool _selectedCanRemove;
    private bool _closed;

    internal NativeWorktreeManagerDialog(
        nint owner,
        GitRepositorySnapshot repository,
        IGitWorktreeService service,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        _owner = owner;
        _repository = repository;
        _service = service;
        _settings = settings;
        _setStatus = setStatus;
        EnsureWindowClass();
        _headingFont = NativeTheme.CreateOwnedUiFont(settings.TextFontFamily, settings.UiFontSize, 600);
        (int x, int y) = Center(owner, S(DialogWidth), S(DialogHeight));
        _handle = NativeMethods.CreateWindow(0, WindowClassName, UiText.WorktreeManagement, NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren, x, y, S(DialogWidth), S(DialogHeight), owner, 0, NativeMethods.GetModuleHandle(null), 0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.WorktreeDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        MeasureLayout();
        ApplyAppearance();
        ResizeToContent();
        _ = LoadAsync();
    }

    internal static void Show(nint owner, GitRepositorySnapshot repository, IGitWorktreeService service, ApplicationSettings settings, Action<string> setStatus)
    {
        using NativeWorktreeManagerDialog dialog = new(owner, repository, service, settings, setStatus);
        dialog.Run();
    }

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight, int SidebarWidth) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight, SidebarWidth);

    internal static string WindowClassNameForTest => WindowClassName;

    internal static IReadOnlyList<string> DetailLabelsForTest =>
        [UiText.WorktreePath, UiText.WorktreeStatus, UiText.WorktreeTerminalSessions];

    internal static IReadOnlyList<string> CreateLabelsForTest =>
        [UiText.WorktreeTargetPath, UiText.WorktreeSourceBranch, UiText.WorktreeNewBranch];

    internal static IReadOnlyList<string> DetailActionLabelsForTest =>
        [UiText.OpenWorktree, UiText.NewWorktreeAction, UiText.RemoveWorktree];

    internal static IReadOnlyList<int> DetailTabOrderForTest =>
        [ListIdentifier, CommandOpen, CommandNew, CommandRemove, CommandClose, CommandHeaderClose, CommandAdd, CommandDeleteToolbar, CommandRefresh];

    internal static IReadOnlyList<int> CreateTabOrderForTest =>
        [ControlPath, ControlSourceBranch, ControlNewBranch, CommandCreate, CommandClose, CommandHeaderClose, CommandAdd, CommandRefresh, ListIdentifier];

    internal static string FormatListEntryForTest(GitWorktreeInfo worktree)
    {
        string branch = worktree.IsBare
            ? "Bare"
            : worktree.IsDetached
                ? "Detached HEAD"
                : worktree.Branch ?? "无分支";
        return $"{branch} · {FormatDisplayPathForTest(worktree.Path)}";
    }

    internal static string FormatDisplayPathForTest(string path) => path.Replace('/', '\\');

    internal static int ChooseSelectionIndexForTest(
        IReadOnlyList<GitWorktreeInfo> worktrees,
        string? selectedPath)
    {
        ArgumentNullException.ThrowIfNull(worktrees);
        if (worktrees.Count == 0)
        {
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            int existing = worktrees.ToList().FindIndex(
                worktree => worktree.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                return existing;
            }
        }

        int linked = worktrees.ToList().FindIndex(worktree => !worktree.IsCurrent);
        return linked >= 0 ? linked : 0;
    }

    internal static (string Status, string Terminal, bool CanRemove) RemovalPresentationForTest(
        GitWorktreeInfo worktree,
        GitWorktreeRemovalReadiness readiness)
    {
        string status = readiness.CanRemove
            ? UiText.WorktreeCleanStatus
            : readiness.Reason
                ?? (readiness.IsClean ? "暂不可安全移除" : "存在本地改动或未跟踪文件");
        string terminal = readiness.HasActiveTerminal
            ? UiText.WorktreeTerminalActive
            : UiText.WorktreeTerminalInactive;
        return (status, terminal, readiness.CanRemove && !worktree.IsCurrent && !worktree.IsLocked);
    }

    private void Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        bool ownerEnabled = NativeMethods.IsWindowEnabled(_owner);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_worktreeList);
        try
        {
            while (!_closed)
            {
                int status = NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0);
                if (status <= 0)
                {
                    if (status == 0) _quitCode = unchecked((int)message.WordParameter);
                    break;
                }
                if (HandleKey(message)) continue;
                if (_composing || !NativeMethods.IsDialogMessage(_handle, ref message))
                {
                    _ = NativeMethods.TranslateMessage(ref message);
                    _ = NativeMethods.DispatchMessage(ref message);
                }
            }
        }
        finally
        {
            Dispose();
            if (NativeMethods.IsWindow(_owner))
            {
                _ = NativeMethods.EnableWindow(_owner, ownerEnabled);
                if (ownerEnabled) _ = NativeMethods.SetForegroundWindow(_owner);
            }
            focusScope.Restore();
        }
    }

    internal void MoveFocus(bool backwards)
    {
        nint[] controls = _operationRunning ? [_cancelOperationButton] : _creatingNew
            ? [_pathEdit, _branchEdit, _newBranchEdit, _createButton, _closeButton, _headerCloseButton, _addButton, _refreshButton, _worktreeList]
            : [_worktreeList, _openButton, _newButton, _removeButton, _closeButton, _headerCloseButton, _addButton, _deleteToolbarButton, _refreshButton];
        nint current = NativeMethods.GetFocus();
        int currentIndex = Array.IndexOf(controls, current);
        int direction = backwards ? -1 : 1;
        for (int offset = 1; offset <= controls.Length; offset++)
        {
            int index = (currentIndex + (direction * offset) + controls.Length) % controls.Length;
            nint candidate = controls[index];
            if (candidate != 0
                && NativeMethods.IsWindowVisible(candidate)
                && NativeMethods.IsWindowEnabled(candidate))
            {
                _ = NativeMethods.SetFocus(candidate);
                return;
            }
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
                throw new Win32Exception(error, UiText.WorktreeDialogClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeWorktreeManagerDialog? instance;
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
            return instance.ApplyControlColor(unchecked((nint)wordParameter), longParameter);
        }

        return message switch
        {
            NativeMethods.WindowMessagePaint => instance.PaintWindow(),
            NativeMethods.WindowMessageEraseBackground => 1,
            NativeMethods.WindowMessageNonClientHitTest => instance.HitTest(),
            NativeMethods.WindowMessageSize => instance.LayoutMessage(),
            NativeMethods.WindowMessageCommand => instance.CommandMessage(wordParameter, longParameter),
            NativeMethods.WindowMessageClose => instance.CloseMessage(),
            WorkCompleted => instance.CompleteWork(),
            NativeMethods.WindowMessageNonClientDestroy => instance.DestroyMessage(window, message, wordParameter, longParameter),
            _ => NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter),
        };
    }

    private void CreateControls()
    {
        _bodyView = new(_handle, MeasureBody, LayoutBody, PaintBody, FocusInputFrame);
        _addButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandAdd, NativeMethods.ButtonOwnerDraw);
        _deleteToolbarButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandDeleteToolbar, NativeMethods.ButtonOwnerDraw);
        _refreshButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandRefresh, NativeMethods.ButtonOwnerDraw);
        _worktreeList = CreateControl(NativeMethods.ListBoxClass, string.Empty, ListIdentifier, NativeMethods.WindowStyleVerticalScroll | NativeMethods.ListBoxNotify | NativeMethods.ListBoxNoIntegralHeight | NativeMethods.ListBoxOwnerDrawFixed | NativeMethods.ListBoxHasStrings);
        _ = NativeMethods.SendMessage(_worktreeList, NativeMethods.ListBoxSetItemHeight, 0, unchecked((nint)S(27)));
        _detailTitle = CreateControl(NativeMethods.StaticClass, UiText.Worktrees, 0, NativeMethods.StaticLeft);
        _ = NativeMethods.SendMessage(_detailTitle, NativeMethods.WindowMessageSetFont, unchecked((nuint)_headingFont), 1);
        CreateDetailLabel(UiText.WorktreePath);
        CreateDetailLabel(UiText.WorktreeStatus);
        CreateDetailLabel(UiText.WorktreeTerminalSessions);
        _pathValue = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft | NativeMethods.StaticEndEllipsis);
        _statusLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft | NativeMethods.StaticEndEllipsis);
        _terminalLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft | NativeMethods.StaticEndEllipsis);
        CreateCreateLabel(UiText.WorktreeTargetPath);
        _pathEdit = CreateControl(NativeMethods.EditClass, string.Empty, ControlPath, NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll);
        CreateCreateLabel(UiText.WorktreeSourceBranch);
        _branchEdit = CreateControl(NativeMethods.EditClass, string.Empty, ControlSourceBranch, NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll);
        CreateCreateLabel(UiText.WorktreeNewBranch);
        _newBranchEdit = CreateControl(NativeMethods.EditClass, string.Empty, ControlNewBranch, NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll);
        _openButton = CreateControl(NativeMethods.ButtonClass, UiText.OpenWorktree, CommandOpen, NativeMethods.ButtonOwnerDraw);
        _newButton = CreateControl(NativeMethods.ButtonClass, UiText.NewWorktreeAction, CommandNew, NativeMethods.ButtonOwnerDraw);
        _createButton = CreateControl(NativeMethods.ButtonClass, UiText.CreateAndOpenWorktree, CommandCreate, NativeMethods.ButtonOwnerDraw);
        _removeButton = CreateControl(NativeMethods.ButtonClass, UiText.RemoveWorktree, CommandRemove, NativeMethods.ButtonOwnerDraw);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft);
        _cancelOperationButton = CreateControl(NativeMethods.ButtonClass, UiText.CancelOperation, CommandCancelOperation, NativeMethods.ButtonOwnerDraw);
        _closeButton = CreateControl(NativeMethods.ButtonClass, UiText.Close, CommandClose, NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandHeaderClose, NativeMethods.ButtonOwnerDraw);
        _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowHide);
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_addButton, UiText.Add);
        _toolTip.Add(_deleteToolbarButton, UiText.Delete);
        _toolTip.Add(_refreshButton, UiText.Refresh);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        foreach (nint control in new[] { _detailTitle, _pathValue, _statusLabel, _terminalLabel, _noticeLabel,
            _openButton, _newButton, _createButton, _removeButton }) _toolTip.Add(control, NativeMethods.GetWindowTextValue(control));
        _buttons = new(_handle, _addButton, _deleteToolbarButton, _refreshButton, _openButton, _newButton,
            _createButton, _removeButton, _cancelOperationButton, _closeButton, _headerCloseButton);
        foreach (nint input in new[] { _pathEdit, _branchEdit, _newBranchEdit })
            _ = NativeMethods.SetWindowSubclass(input, InputProcedure, 78, unchecked((nuint)_handle));
        SetCreateControlsVisible(false);
        Layout();
    }

    private void CreateDetailLabel(string text)
    {
        _detailLabels.Add(CreateControl(NativeMethods.StaticClass, text, 0, NativeMethods.StaticLeft));
    }

    private void CreateCreateLabel(string text)
    {
        _createLabels.Add(CreateControl(NativeMethods.StaticClass, text, 0, NativeMethods.StaticLeft));
    }

    private nint CreateControl(string className, string text, int identifier, uint style)
    {
        uint commonStyle = NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible;
        if (!className.Equals(NativeMethods.StaticClass, StringComparison.Ordinal))
        {
            commonStyle |= NativeMethods.WindowStyleTabStop;
        }

        bool detail = className == NativeMethods.StaticClass || className == NativeMethods.EditClass
            || identifier is CommandOpen or CommandNew or CommandCreate or CommandRemove;
        if (className == NativeMethods.EditClass) style &= ~NativeMethods.WindowStyleBorder;
        nint control = NativeMethods.CreateWindow(0, className, text, commonStyle | style, 0, 0, 0, 0,
            detail ? _bodyView!.Handle : _handle, identifier, NativeMethods.GetModuleHandle(null), 0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.WorktreeDialogControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        if (detail) _bodyView!.Register(control, scrollWheel: true);
        return control;
    }

    private nint CommandMessage(nuint wordParameter, nint source)
    {
        if (_closed || source == 0 || !NativeMethods.IsWindowEnabled(source)) return 0;
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == ListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            PopulateSelected();
            return 0;
        }
        if (notification != 0) return 0;
        switch (command)
        {
            case CommandAdd:
            case CommandNew:
                PrepareNew();
                break;
            case CommandDeleteToolbar:
            case CommandRemove:
                _ = RemoveAsync();
                break;
            case CommandRefresh:
                _ = LoadAsync();
                break;
            case CommandOpen:
                OpenSelected();
                break;
            case CommandCreate:
                _ = CreateAsync();
                break;
            case CommandCancelOperation:
                RequestCancellation();
                break;
            case CommandClose:
            case CommandHeaderClose:
                Close();
                break;
        }

        return 0;
    }

    private Task LoadAsync()
    {
        if (_closed || _operationRunning || _readWork.Cancellation is not null) return Task.CompletedTask;
        SetNotice(UiText.ReadingWorktrees);
        return StartWork(_readWork, token => _service.ReadAsync(_repository, token), result =>
        {
            if (!result.IsSuccess || result.Worktrees is null)
            {
                ShowError(result.ErrorMessage ?? UiText.GitUnavailable);
                return;
            }
            string? selected = SelectedWorktree?.Path;
            _worktrees.Clear();
            _worktrees.AddRange(result.Worktrees);
            _ = NativeMethods.SendMessage(_worktreeList, NativeMethods.ListBoxResetContent, 0, 0);
            foreach (GitWorktreeInfo worktree in _worktrees)
                _ = NativeMethods.SendMessage(_worktreeList, NativeMethods.ListBoxAddString, 0, FormatListEntryForTest(worktree));
            if (_worktrees.Count == 0)
            {
                PrepareNew();
                SetNotice(UiText.NoWorktrees);
                return;
            }
            int index = ChooseSelectionIndexForTest(_worktrees, selected);
            _ = NativeMethods.SendMessage(_worktreeList, NativeMethods.ListBoxSetCurrentSelection, unchecked((nuint)index), 0);
            PopulateSelected();
            SetNotice(string.Empty);
        }, exception => GitWorktreeListResult.Failure(FailureKind(exception), FailureMessage(exception)));
    }

    private void PrepareNew()
    {
        if (_operationRunning) return;
        CancelWork(_readWork);
        CancelWork(_inspectWork);
        _creatingNew = true;
        _selectedCanRemove = false;
        _ = NativeMethods.SendMessage(_worktreeList, NativeMethods.ListBoxSetCurrentSelection, unchecked((nuint)(nint)(-1)), 0);
        _ = NativeMethods.SetWindowText(_detailTitle, UiText.NewWorktree);
        _ = NativeMethods.SetWindowText(_pathEdit, string.Empty);
        _ = NativeMethods.SetWindowText(_branchEdit, "HEAD");
        _ = NativeMethods.SetWindowText(_newBranchEdit, string.Empty);
        SetCreateControlsVisible(true);
        SetNotice(UiText.WorktreeCreatePrompt);
        _ = NativeMethods.SetFocus(_pathEdit);
    }

    private void PopulateSelected()
    {
        GitWorktreeInfo? worktree = SelectedWorktree;
        if (worktree is null)
        {
            return;
        }

        CancelWork(_inspectWork);
        _creatingNew = false;
        _selectedCanRemove = false;
        SetCreateControlsVisible(false);
        _ = NativeMethods.SetWindowText(_detailTitle, worktree.Branch ?? UiText.Worktrees);
        _ = NativeMethods.SetWindowText(_pathValue, FormatDisplayPathForTest(worktree.Path));
        _ = NativeMethods.SetWindowText(_statusLabel, worktree.IsCurrent ? UiText.WorktreeCurrentStatus : worktree.IsLocked ? $"已锁定：{worktree.LockReason ?? "未说明原因"}" : UiText.WorktreeCheckingStatus);
        _ = NativeMethods.SetWindowText(_terminalLabel, UiText.WorktreeTerminalChecking);
        _ = NativeMethods.EnableWindow(_openButton, !worktree.IsCurrent);
        _ = NativeMethods.EnableWindow(_removeButton, false);
        _ = NativeMethods.EnableWindow(_deleteToolbarButton, false);
        SetNotice(string.Empty);
        RelayoutDetails();
        _ = InspectReadinessAsync(worktree);
    }

    private Task InspectReadinessAsync(GitWorktreeInfo worktree)
    {
        return StartWork(_inspectWork, token => _service.InspectRemovalReadinessAsync(_repository, worktree.Path, token), result =>
        {
            if (!ReferenceEquals(SelectedWorktree, worktree) || _creatingNew) return;
            if (!result.IsSuccess || result.Readiness is null)
            {
                _ = NativeMethods.SetWindowText(_statusLabel, result.ErrorMessage ?? UiText.GitUnavailable);
                _ = NativeMethods.SetWindowText(_terminalLabel, UiText.WorktreeTerminalInactive);
                _selectedCanRemove = false;
            }
            else
            {
                var presentation = RemovalPresentationForTest(worktree, result.Readiness);
                _selectedCanRemove = presentation.CanRemove;
                _ = NativeMethods.SetWindowText(_statusLabel, presentation.Status);
                _ = NativeMethods.SetWindowText(_terminalLabel, presentation.Terminal);
            }
            _ = NativeMethods.EnableWindow(_removeButton, _selectedCanRemove && !_operationRunning);
            _ = NativeMethods.EnableWindow(_deleteToolbarButton, _selectedCanRemove && !_operationRunning);
            RelayoutDetails();
        }, exception => GitWorktreeRemovalReadinessResult.Failure(FailureKind(exception), FailureMessage(exception)));
    }

    private void SetCreateControlsVisible(bool visible)
    {
        int createCommand = visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide;
        int detailCommand = visible ? NativeMethods.ShowHide : NativeMethods.ShowNormal;
        foreach (nint control in _createLabels.Append(_pathEdit).Append(_branchEdit).Append(_newBranchEdit).Append(_createButton))
        {
            _ = NativeMethods.ShowWindow(control, createCommand);
        }

        foreach (nint control in _detailLabels.Append(_pathValue).Append(_statusLabel).Append(_terminalLabel).Append(_openButton).Append(_newButton).Append(_removeButton))
        {
            _ = NativeMethods.ShowWindow(control, detailCommand);
        }

        _ = NativeMethods.EnableWindow(_deleteToolbarButton, !visible && _selectedCanRemove && !_operationRunning);
        RelayoutDetails();
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private async Task CreateAsync()
    {
        if (!_creatingNew)
        {
            return;
        }

        string path = NativeMethods.GetWindowTextValue(_pathEdit).Trim();
        string branch = NativeMethods.GetWindowTextValue(_branchEdit).Trim();
        string newBranch = NativeMethods.GetWindowTextValue(_newBranchEdit).Trim();
        if (path.Length == 0 || branch.Length == 0)
        {
            ShowError(UiText.WorktreeFieldsRequired);
            _ = NativeMethods.SetFocus(path.Length == 0 ? _pathEdit : _branchEdit);
            return;
        }

        await RunActionAsync(token => _service.CreateAsync(_repository, path, branch, newBranch.Length == 0 ? null : newBranch, token), path);
    }

    private async Task RemoveAsync()
    {
        GitWorktreeInfo? worktree = SelectedWorktree;
        if (worktree is null || !_selectedCanRemove)
        {
            ShowError(UiText.SelectWorktreeFirst);
            return;
        }

        if (!NativeActionConfirmationDialog.Show(
                _handle,
                _settings,
                UiText.RemoveWorktreeTitle,
                UiText.RemoveWorktreeHeading,
                UiText.ConfirmRemoveWorktreeDetails(worktree.Path, worktree.Branch),
                UiText.RemoveWorktreeTitle,
                danger: true))
        {
            return;
        }

        await RunActionAsync(token => _service.RemoveAsync(_repository, worktree.Path, token));
    }

    private Task RunActionAsync(Func<CancellationToken, Task<GitActionResult>> action, string? openPath = null)
    {
        if (_operationRunning || _closed) return Task.CompletedTask;
        CancelWork(_readWork);
        CancelWork(_inspectWork);
        _operationRunning = true;
        SetOperationControlsEnabled(false);
        _ = NativeMethods.EnableWindow(_cancelOperationButton, true);
        _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowNormal);
        _ = NativeMethods.SetFocus(_cancelOperationButton);
        SetNotice(UiText.RunningWorktreeOperation);
        return StartWork(_writeWork, action, result =>
        {
            _operationRunning = false;
            SetOperationControlsEnabled(true);
            _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowHide);
            if (!result.IsSuccess)
            {
                ShowError(result.ErrorMessage ?? UiText.GitUnavailable);
                _ = NativeMethods.SetFocus(_creatingNew ? _pathEdit : _worktreeList);
                return;
            }
            _setStatus(UiText.WorktreeOperationCompleted);
            SetNotice(UiText.WorktreeOperationCompleted);
            if (openPath is not null && Directory.Exists(openPath) && Environment.ProcessPath is { } executable)
            {
                ExternalLaunchResult launch = ExternalProgramLauncher.OpenAugitWorkspace(executable, openPath);
                if (!launch.IsSuccess) ShowError(launch.ErrorMessage ?? UiText.ExternalProgramFailed);
            }
            _ = LoadAsync();
        }, exception => GitActionResult.Failure(FailureKind(exception), FailureMessage(exception)));
    }

    private void OpenSelected()
    {
        if (SelectedWorktree is not { } worktree || worktree.IsCurrent)
        {
            return;
        }

        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            ShowError(UiText.AugitExecutableUnavailable);
            return;
        }

        ExternalLaunchResult result = ExternalProgramLauncher.OpenAugitWorkspace(executable, worktree.Path);
        if (!result.IsSuccess)
        {
            ShowError(result.ErrorMessage ?? UiText.ExternalProgramFailed);
        }
    }

    private GitWorktreeInfo? SelectedWorktree
    {
        get
        {
            int index = checked((int)NativeMethods.SendMessage(_worktreeList, NativeMethods.ListBoxGetCurrentSelection, 0, 0));
            return index >= 0 && index < _worktrees.Count ? _worktrees[index] : null;
        }
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        foreach (nint control in new[] { _worktreeList, _addButton, _refreshButton, _closeButton, _headerCloseButton })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }

        foreach (nint control in new[] { _pathEdit, _branchEdit, _newBranchEdit, _createButton })
        {
            _ = NativeMethods.EnableWindow(control, enabled && _creatingNew);
        }

        GitWorktreeInfo? selected = SelectedWorktree;
        _ = NativeMethods.EnableWindow(_openButton, enabled && !_creatingNew && selected is { IsCurrent: false });
        _ = NativeMethods.EnableWindow(_newButton, enabled && !_creatingNew);
        _ = NativeMethods.EnableWindow(_removeButton, enabled && !_creatingNew && _selectedCanRemove);
        _ = NativeMethods.EnableWindow(_deleteToolbarButton, enabled && !_creatingNew && _selectedCanRemove);
    }

    private void ApplyAppearance()
    {
        _dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, _dark);
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        foreach (nint control in new[] { _worktreeList, _detailTitle, _pathValue, _pathEdit, _branchEdit, _newBranchEdit, _statusLabel, _terminalLabel, _addButton, _deleteToolbarButton, _refreshButton, _openButton, _newButton, _createButton, _removeButton, _cancelOperationButton, _closeButton, _headerCloseButton, _noticeLabel }.Concat(_detailLabels).Concat(_createLabels))
        {
            NativeTheme.ApplyToControl(control, _dark);
        }

        _toolTip?.ApplyAppearance(_dark);
        NativeTheme.ApplyToControl(_bodyView!.Handle, _dark);
    }

    private nint ApplyControlColor(nint deviceContext, nint control)
    {
        if (deviceContext == 0 || _controlBrush == 0)
        {
            return 0;
        }

        NativeThemePalette palette = NativeTheme.Palette(_dark);
        _ = NativeMethods.SetBackgroundColor(deviceContext, palette.Panel);
        uint text = control == _statusLabel && _selectedCanRemove ? palette.Success : palette.Text;
        _ = NativeMethods.SetTextColor(deviceContext, text);
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
        if (identifier == ListIdentifier)
        {
            return DrawListItem(item);
        }

        if (identifier is CommandAdd or CommandDeleteToolbar or CommandRefresh) return DrawToolbarButton(item, identifier);
        return _buttons?.Draw(item, _dark, identifier switch
        {
            CommandOpen or CommandCreate => NativeDialogActionButtons.Style.Primary,
            CommandRemove => NativeDialogActionButtons.Style.Danger,
            CommandHeaderClose => NativeDialogActionButtons.Style.Close,
            _ => NativeDialogActionButtons.Style.Secondary,
        }) == true;
    }

    private bool DrawToolbarButton(NativeMethods.DrawItem item, int identifier)
    {
        _buttons?.Draw(item, _dark, NativeDialogActionButtons.Style.Icon);
        var palette = NativeTheme.Palette(_dark);
        uint color = NativeMethods.IsWindowEnabled(item.Control) ? palette.Muted : palette.Faint;
        int x = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int y = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        if (identifier == CommandRefresh) return NativeTheme.DrawRefreshIcon(item.DeviceContext, x, y, color);
        NativeGdiPlusDrawing.StrokeLine[] lines = identifier == CommandAdd
            ? [new(x - S(6), y, x + S(6), y), new(x, y - S(6), x, y + S(6))]
            : [new(x - S(7), y - S(5), x + S(7), y - S(5)), new(x - S(3), y - S(7), x + S(3), y - S(7))];
        return NativeGdiPlusDrawing.StrokeShapes(item.DeviceContext, color, NativeTheme.Scale(1.5f), lines, [],
            identifier == CommandAdd ? [] : [new(x - S(5), y - S(3), S(10), S(10))]);
    }

    private bool DrawListItem(NativeMethods.DrawItem item)
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
        if (index >= 0 && index < _worktrees.Count)
        {
            DrawWorktreeIcon(item.DeviceContext, rectangle, palette.Muted);
            NativeMethods.Rectangle text = rectangle;
            text.Left += S(35);
            text.Right -= S(7);
            DrawText(item.DeviceContext, FormatListEntryForTest(_worktrees[index]), text, palette.Text, NativeTheme.UiFont);
        }

        return true;
    }

    private static void DrawWorktreeIcon(
        nint deviceContext,
        NativeMethods.Rectangle row,
        uint color)
    {
        int left = row.Left + S(11);
        int top = ((row.Top + row.Bottom) / 2) - S(7);
        int right = left + S(15);
        int bottom = top + S(13);
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, S(1)), color);
        if (pen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(deviceContext, NativeMethods.GetStockObject(NativeMethods.NullBrush));
        _ = NativeMethods.MoveTo(deviceContext, left, top + S(3), 0);
        _ = NativeMethods.LineTo(deviceContext, left + S(5), top + S(3));
        _ = NativeMethods.LineTo(deviceContext, left + S(7), top + S(1));
        _ = NativeMethods.LineTo(deviceContext, right, top + S(1));
        _ = NativeMethods.LineTo(deviceContext, right, bottom);
        _ = NativeMethods.LineTo(deviceContext, left, bottom);
        _ = NativeMethods.LineTo(deviceContext, left, top + S(3));

        int branchX = left + S(6);
        _ = NativeMethods.DrawEllipse(deviceContext, branchX - S(1), top + S(5), branchX + S(2), top + S(8));
        _ = NativeMethods.DrawEllipse(deviceContext, right - S(5), top + S(5), right - S(2), top + S(8));
        _ = NativeMethods.DrawEllipse(deviceContext, right - S(5), bottom - S(4), right - S(2), bottom - S(1));
        _ = NativeMethods.MoveTo(deviceContext, branchX + S(1), top + S(7), 0);
        _ = NativeMethods.LineTo(deviceContext, right - S(4), top + S(7));
        _ = NativeMethods.LineTo(deviceContext, right - S(4), bottom - S(4));

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


    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
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

        _ = NativeMethods.GetClientRectangle(_handle, out var client);
        return point.Y < _headerHeight && point.X < client.Right - S(54) ? NativeMethods.HitTestCaption : NativeMethods.HitTestClient;
    }

    private void SetNotice(string message)
    {
        if (_notice == message) return;
        _notice = message;
        _ = NativeMethods.SetWindowText(_noticeLabel, message);
        RelayoutDetails();
    }

    private void ShowError(string message)
    {
        SetNotice(message);
        _bodyView?.EnsureVisible(_noticeLabel);
        _setStatus(message);
    }

    private void Close()
    {
        if (_operationRunning) RequestCancellation();
        else Dispose();
    }

    public void Dispose()
    {
        if (_closed) return;
        nint handle;
        lock (_workGate)
        {
            _closed = true;
            handle = _handle;
            _handle = 0;
            CancelWork(_readWork);
            CancelWork(_inspectWork);
            CancelWork(_writeWork);
        }
        lock (InstancesGate) Instances.Remove(handle);
        _buttons?.Dispose();
        _bodyView?.Dispose();
        _toolTip?.Dispose();
        _toolTip = null;
        foreach (nint input in new[] { _pathEdit, _branchEdit, _newBranchEdit })
            if (NativeMethods.IsWindow(input)) _ = NativeMethods.RemoveWindowSubclass(input, InputProcedure, 78);
        if (_controlBrush != 0) { _ = NativeMethods.DeleteObject(_controlBrush); _controlBrush = 0; }
        if (_headingFont != 0) { _ = NativeMethods.DeleteObject(_headingFont); _headingFont = 0; }
        NativeMethods.WakeWindowMessageLoop(handle);
        if (handle != 0 && NativeMethods.IsWindow(handle)) _ = NativeMethods.DestroyWindow(handle);
        if (_quitCode is { } quit) { _quitCode = null; NativeMethods.PostQuitMessage(quit); }
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


    private static int S(int pixels) => NativeTheme.Scale(pixels);
}
