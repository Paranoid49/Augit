using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeGitReferenceDialog : IDisposable
{
    private const string WindowClassName = "Augit.GitReferenceDialog.Native";
    private const int BranchListIdentifier = 1;
    private const int TagListIdentifier = 2;
    private const int CommandCreateBranch = 10;
    private const int CommandSwitchBranch = 11;
    private const int CommandRenameBranch = 12;
    private const int CommandDeleteBranch = 13;
    private const int CommandCreateTrackingBranch = 14;
    private const int CommandSetTracking = 15;
    private const int CommandUnsetTracking = 16;
    private const int CommandCreateTag = 17;
    private const int CommandDeleteTag = 18;
    private const int CommandPushTag = 19;
    private const int CommandDeleteRemoteTag = 20;
    private const int CommandRefresh = 21;
    private const int CommandCancel = 22;
    private const int CommandClose = 23;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitReferenceDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly GitReferenceService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly List<GitBranchInfo> _branches = [];
    private readonly List<GitTagInfo> _tags = [];
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private nint _branchList;
    private nint _tagList;
    private nint _nameEdit;
    private nint _targetEdit;
    private nint _remoteEdit;
    private nint _messageEdit;
    private nint _forceDeleteCheck;
    private nint _createBranchButton;
    private nint _switchBranchButton;
    private nint _renameBranchButton;
    private nint _deleteBranchButton;
    private nint _createTrackingButton;
    private nint _setTrackingButton;
    private nint _unsetTrackingButton;
    private nint _createTagButton;
    private nint _deleteTagButton;
    private nint _pushTagButton;
    private nint _deleteRemoteTagButton;
    private nint _refreshButton;
    private nint _cancelButton;
    private nint _closeButton;
    private nint _noticeLabel;
    private bool _operationRunning;
    private bool _closed;

    private NativeGitReferenceDialog(
        nint owner,
        GitRepositorySnapshot repository,
        GitReferenceService service,
        ApplicationSettings settings,
        Action<string> setStatus,
        string? initialTarget)
    {
        _owner = owner;
        _repository = repository;
        _service = service;
        _settings = settings;
        _setStatus = setStatus;
        EnsureWindowClass();
        int x = NativeMethods.UseDefault;
        int y = NativeMethods.UseDefault;
        if (NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle ownerRectangle))
        {
            x = ownerRectangle.Left + Math.Max(0, ((ownerRectangle.Right - ownerRectangle.Left) - 980) / 2);
            y = ownerRectangle.Top + Math.Max(0, ((ownerRectangle.Bottom - ownerRectangle.Top) - 620) / 2);
        }

        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.ReferenceManagement,
            NativeMethods.WindowStylePopup
                | NativeMethods.WindowStyleCaption
                | NativeMethods.WindowStyleSystemMenu
                | NativeMethods.WindowStyleThickFrame,
            x,
            y,
            980,
            620,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ReferenceDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        if (!string.IsNullOrWhiteSpace(initialTarget))
        {
            _ = NativeMethods.SetWindowText(_targetEdit, initialTarget);
        }
        ApplyAppearance();
    }

    internal static void Show(
        nint owner,
        GitRepositorySnapshot repository,
        GitReferenceService service,
        ApplicationSettings settings,
        Action<string> setStatus,
        string? initialTarget = null)
    {
        using NativeGitReferenceDialog dialog = new(
            owner,
            repository,
            service,
            settings,
            setStatus,
            initialTarget);
        dialog.Run();
    }

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        Close(force: true);
        GC.SuppressFinalize(this);
    }

    private void Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, NativeTheme.IsDark(_settings.Theme));
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        // 引用列表是窗口左侧的第一个交互区域，打开后直接把焦点交给它。
        _ = NativeMethods.SetFocus(_branchList);
        _ = LoadAsync();
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
                    Close(force: true);
                    continue;
                }

                _ = NativeMethods.TranslateMessage(ref message);
                _ = NativeMethods.DispatchMessage(ref message);
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
    /// 按引用管理窗口从列表、字段到动作栏的视觉顺序循环移动焦点。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [
                _branchList,
                _tagList,
                _nameEdit,
                _targetEdit,
                _remoteEdit,
                _messageEdit,
                _forceDeleteCheck,
                _createBranchButton,
                _switchBranchButton,
                _renameBranchButton,
                _deleteBranchButton,
                _createTrackingButton,
                _setTrackingButton,
                _unsetTrackingButton,
                _createTagButton,
                _deleteTagButton,
                _pushTagButton,
                _deleteRemoteTagButton,
                _refreshButton,
                _cancelButton,
                _closeButton,
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
                throw new Win32Exception(error, UiText.ReferenceDialogClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeGitReferenceDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message == NativeMethods.WindowMessageSize)
        {
            instance.Layout();
            return 0;
        }

        if (message == NativeMethods.WindowMessageCommand)
        {
            instance.HandleCommand(wordParameter);
            return 0;
        }

        if (message == NativeMethods.WindowMessageClose)
        {
            instance.Close();
            return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        CreateControl(NativeMethods.StaticClass, UiText.Branches, 0, NativeMethods.StaticLeft, 12, 10, 300, 22);
        _branchList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            BranchListIdentifier,
            NativeMethods.WindowStyleBorder | NativeMethods.WindowStyleVerticalScroll | NativeMethods.ListBoxNotify,
            12,
            34,
            360,
            300);
        CreateControl(NativeMethods.StaticClass, UiText.Tags, 0, NativeMethods.StaticLeft, 386, 10, 260, 22);
        _tagList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            TagListIdentifier,
            NativeMethods.WindowStyleBorder | NativeMethods.WindowStyleVerticalScroll | NativeMethods.ListBoxNotify,
            386,
            34,
            250,
            300);
        CreateControl(NativeMethods.StaticClass, UiText.ReferenceName, 0, NativeMethods.StaticLeft, 654, 20, 290, 22);
        _nameEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            30,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            654,
            44,
            290,
            26);
        CreateControl(NativeMethods.StaticClass, UiText.ReferenceTarget, 0, NativeMethods.StaticLeft, 654, 78, 290, 22);
        _targetEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            31,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            654,
            102,
            290,
            26);
        CreateControl(NativeMethods.StaticClass, UiText.RemoteName, 0, NativeMethods.StaticLeft, 654, 136, 290, 22);
        _remoteEdit = CreateControl(
            NativeMethods.EditClass,
            "origin",
            32,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            654,
            160,
            290,
            26);
        CreateControl(NativeMethods.StaticClass, UiText.TagMessage, 0, NativeMethods.StaticLeft, 654, 194, 290, 22);
        _messageEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            33,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            654,
            218,
            290,
            26);
        _forceDeleteCheck = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ForceDeleteBranch,
            34,
            NativeMethods.ButtonAutoCheckbox,
            654,
            254,
            290,
            24);
        _createBranchButton = CreateButton(UiText.CreateBranch, CommandCreateBranch, 12, 350, 104);
        _switchBranchButton = CreateButton(UiText.SwitchBranch, CommandSwitchBranch, 122, 350, 104);
        _renameBranchButton = CreateButton(UiText.RenameBranch, CommandRenameBranch, 232, 350, 104);
        _deleteBranchButton = CreateButton(UiText.DeleteBranch, CommandDeleteBranch, 12, 384, 104);
        _createTrackingButton = CreateButton(UiText.CreateTrackingBranch, CommandCreateTrackingBranch, 122, 384, 214);
        _setTrackingButton = CreateButton(UiText.SetTracking, CommandSetTracking, 12, 418, 104);
        _unsetTrackingButton = CreateButton(UiText.UnsetTracking, CommandUnsetTracking, 122, 418, 104);
        _createTagButton = CreateButton(UiText.CreateTag, CommandCreateTag, 386, 350, 112);
        _deleteTagButton = CreateButton(UiText.DeleteLocalTag, CommandDeleteTag, 504, 350, 132);
        _pushTagButton = CreateButton(UiText.PushTag, CommandPushTag, 386, 384, 112);
        _deleteRemoteTagButton = CreateButton(UiText.DeleteRemoteTag, CommandDeleteRemoteTag, 504, 384, 132);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 40, NativeMethods.StaticLeft, 12, 470, 930, 42);
        _refreshButton = CreateButton(UiText.Refresh, CommandRefresh, 590, 530, 74);
        _cancelButton = CreateButton(UiText.CancelOperation, CommandCancel, 670, 530, 94);
        _closeButton = CreateButton(UiText.Close, CommandClose, 870, 530, 74);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
        Layout();
    }

    private nint CreateButton(string text, int identifier, int x, int y, int width)
    {
        return CreateControl(NativeMethods.ButtonClass, text, identifier, NativeMethods.ButtonPushButton, x, y, width, 28);
    }

    private nint CreateControl(
        string className,
        string text,
        int identifier,
        uint specificStyle,
        int x,
        int y,
        int width,
        int height)
    {
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleTabStop | specificStyle,
            x,
            y,
            width,
            height,
            _handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ReferenceDialogControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return control;
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == BranchListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            PopulateSelectedBranch();
            return;
        }

        if (command == TagListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            PopulateSelectedTag();
            return;
        }

        switch (command)
        {
            case CommandCreateBranch:
                _ = RunActionAsync(token => _service.CreateBranchAsync(
                    _repository,
                    NameText,
                    NullIfEmpty(TargetText),
                    token));
                break;
            case CommandSwitchBranch:
                if (TryGetSelectedLocalBranch(out GitBranchInfo? switchBranch))
                {
                    _ = RunActionAsync(token => _service.SwitchBranchAsync(_repository, switchBranch!.Name, token));
                }
                break;
            case CommandRenameBranch:
                if (TryGetSelectedLocalBranch(out GitBranchInfo? renameBranch))
                {
                    _ = RunActionAsync(token => _service.RenameBranchAsync(
                        _repository,
                        renameBranch!.Name,
                        NameText,
                        token));
                }
                break;
            case CommandDeleteBranch:
                DeleteSelectedBranch();
                break;
            case CommandCreateTrackingBranch:
                _ = RunActionAsync(token => _service.CreateTrackingBranchAsync(
                    _repository,
                    NameText,
                    TargetText,
                    token));
                break;
            case CommandSetTracking:
                if (TryGetSelectedLocalBranch(out GitBranchInfo? trackingBranch))
                {
                    _ = RunActionAsync(token => _service.SetTrackingAsync(
                        _repository,
                        trackingBranch!.Name,
                        TargetText,
                        token));
                }
                break;
            case CommandUnsetTracking:
                if (TryGetSelectedLocalBranch(out GitBranchInfo? untrackBranch))
                {
                    _ = RunActionAsync(token => _service.UnsetTrackingAsync(
                        _repository,
                        untrackBranch!.Name,
                        token));
                }
                break;
            case CommandCreateTag:
                _ = RunActionAsync(token => _service.CreateTagAsync(
                    _repository,
                    NameText,
                    NullIfEmpty(TargetText),
                    NullIfEmpty(MessageText),
                    token));
                break;
            case CommandDeleteTag:
                DeleteSelectedTag(remote: false);
                break;
            case CommandPushTag:
                if (TryGetSelectedTag(out GitTagInfo? pushTag))
                {
                    _ = RunActionAsync(token => _service.PushTagAsync(
                        _repository,
                        RemoteText,
                        pushTag!.Name,
                        token));
                }
                break;
            case CommandDeleteRemoteTag:
                DeleteSelectedTag(remote: true);
                break;
            case CommandRefresh:
                _ = LoadAsync();
                break;
            case CommandCancel:
                _operationCancellation?.Cancel();
                break;
            case CommandClose:
                Close();
                break;
        }
    }

    private async Task LoadAsync()
    {
        if (_operationRunning || _closed)
        {
            return;
        }

        SetNotice("正在读取分支与标签…");
        GitReferenceResult result = await _service.ReadAsync(_repository);
        if (_closed)
        {
            return;
        }

        if (!result.IsSuccess || result.Snapshot is null)
        {
            ShowError(result.ErrorMessage ?? UiText.GitUnavailable);
            return;
        }

        _branches.Clear();
        _branches.AddRange(result.Snapshot.Branches);
        _tags.Clear();
        _tags.AddRange(result.Snapshot.Tags);
        _ = NativeMethods.SendMessage(_branchList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitBranchInfo branch in _branches)
        {
            string current = branch.IsCurrent ? "* " : "  ";
            string remote = branch.IsRemote ? "[远端] " : string.Empty;
            string upstream = branch.Upstream is null ? string.Empty : $" → {branch.Upstream}";
            _ = NativeMethods.SendMessage(
                _branchList,
                NativeMethods.ListBoxAddString,
                0,
                $"{current}{remote}{branch.Name}{upstream}  {branch.Subject}");
        }

        _ = NativeMethods.SendMessage(_tagList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitTagInfo tag in _tags)
        {
            string annotated = tag.IsAnnotated ? "[说明] " : "[轻量] ";
            _ = NativeMethods.SendMessage(
                _tagList,
                NativeMethods.ListBoxAddString,
                0,
                $"{annotated}{tag.Name}  {tag.CommitHash[..Math.Min(10, tag.CommitHash.Length)]}");
        }

        SetNotice($"{_branches.Count} 个分支，{_tags.Count} 个标签");
    }

    private async Task RunActionAsync(Func<CancellationToken, Task<GitActionResult>> action)
    {
        if (_operationRunning || _closed)
        {
            return;
        }

        _operationRunning = true;
        _operationCancellation = new();
        SetControlsEnabled(false);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowNormal);
        SetNotice("正在执行 Git 操作…");
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

            _setStatus(UiText.ReferenceOperationCompleted);
            SetNotice(UiText.ReferenceOperationCompleted);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _operationRunning = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            if (!_closed)
            {
                _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
                SetControlsEnabled(true);
                await LoadAsync();
            }
        }
    }

    private void DeleteSelectedBranch()
    {
        if (!TryGetSelectedLocalBranch(out GitBranchInfo? branch))
        {
            return;
        }

        bool force = IsChecked(_forceDeleteCheck);
        string actionLabel = force ? UiText.ForceDeleteBranch : UiText.DeleteBranch;
        if (!NativeActionConfirmationDialog.Show(
                _handle,
                _settings,
                actionLabel,
                $"分支 {branch!.Name} 将被删除",
                UiText.ConfirmDeleteBranchDetails(branch.Name, force),
                actionLabel,
                danger: true))
        {
            return;
        }

        _ = RunActionAsync(token => _service.DeleteBranchAsync(_repository, branch!.Name, force, token));
    }

    private void DeleteSelectedTag(bool remote)
    {
        if (!TryGetSelectedTag(out GitTagInfo? tag))
        {
            return;
        }

        string warning = UiText.ConfirmDeleteTagDetails(tag!.Name, remote, remote ? RemoteText : null);
        string actionLabel = remote ? UiText.DeleteRemoteTag : UiText.DeleteLocalTag;
        if (!NativeActionConfirmationDialog.Show(
                _handle,
                _settings,
                actionLabel,
                $"标签 {tag!.Name} 将被删除",
                warning,
                actionLabel,
                danger: true))
        {
            return;
        }

        _ = RunActionAsync(token => remote
            ? _service.DeleteRemoteTagAsync(_repository, RemoteText, tag!.Name, token)
            : _service.DeleteLocalTagAsync(_repository, tag!.Name, token));
    }

    private bool TryGetSelectedLocalBranch(out GitBranchInfo? branch)
    {
        int index = GetSelection(_branchList);
        branch = index >= 0 && index < _branches.Count ? _branches[index] : null;
        if (branch is null || branch.IsRemote)
        {
            ShowError(UiText.SelectBranchFirst);
            branch = null;
            return false;
        }

        return true;
    }

    private bool TryGetSelectedTag(out GitTagInfo? tag)
    {
        int index = GetSelection(_tagList);
        tag = index >= 0 && index < _tags.Count ? _tags[index] : null;
        if (tag is null)
        {
            ShowError(UiText.SelectTagFirst);
            return false;
        }

        return true;
    }

    private void PopulateSelectedBranch()
    {
        int index = GetSelection(_branchList);
        if (index < 0 || index >= _branches.Count)
        {
            return;
        }

        GitBranchInfo branch = _branches[index];
        _ = NativeMethods.SetWindowText(_nameEdit, branch.Name);
        _ = NativeMethods.SetWindowText(_targetEdit, branch.Upstream ?? (branch.IsRemote ? branch.Name : string.Empty));
    }

    private void PopulateSelectedTag()
    {
        int index = GetSelection(_tagList);
        if (index < 0 || index >= _tags.Count)
        {
            return;
        }

        GitTagInfo tag = _tags[index];
        _ = NativeMethods.SetWindowText(_nameEdit, tag.Name);
        _ = NativeMethods.SetWindowText(_targetEdit, tag.CommitHash);
        _ = NativeMethods.SetWindowText(_messageEdit, tag.Message ?? string.Empty);
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _branchList,
            _tagList,
            _nameEdit,
            _targetEdit,
            _remoteEdit,
            _messageEdit,
            _forceDeleteCheck,
            _createBranchButton,
            _switchBranchButton,
            _renameBranchButton,
            _deleteBranchButton,
            _createTrackingButton,
            _setTrackingButton,
            _unsetTrackingButton,
            _createTagButton,
            _deleteTagButton,
            _pushTagButton,
            _deleteRemoteTagButton,
            _refreshButton,
            _closeButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }
    }

    private void ApplyAppearance()
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, dark);
        foreach (nint control in new[]
        {
            _branchList,
            _tagList,
            _nameEdit,
            _targetEdit,
            _remoteEdit,
            _messageEdit,
            _forceDeleteCheck,
            _createBranchButton,
            _switchBranchButton,
            _renameBranchButton,
            _deleteBranchButton,
            _createTrackingButton,
            _setTrackingButton,
            _unsetTrackingButton,
            _createTagButton,
            _deleteTagButton,
            _pushTagButton,
            _deleteRemoteTagButton,
            _refreshButton,
            _cancelButton,
            _closeButton,
            _noticeLabel,
        })
        {
            NativeTheme.ApplyToControl(control, dark);
        }
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle rectangle))
        {
            return;
        }

        int width = Math.Max(0, rectangle.Right - rectangle.Left);
        int height = Math.Max(0, rectangle.Bottom - rectangle.Top);
        _ = NativeMethods.MoveWindow(_noticeLabel, 12, Math.Max(460, height - 94), Math.Max(0, width - 36), 42, true);
        _ = NativeMethods.MoveWindow(_refreshButton, Math.Max(12, width - 390), Math.Max(500, height - 48), 74, 28, true);
        _ = NativeMethods.MoveWindow(_cancelButton, Math.Max(92, width - 310), Math.Max(500, height - 48), 94, 28, true);
        _ = NativeMethods.MoveWindow(_closeButton, Math.Max(172, width - 90), Math.Max(500, height - 48), 74, 28, true);
    }

    private void SetNotice(string text)
    {
        _ = NativeMethods.SetWindowText(_noticeLabel, text);
    }

    private void ShowError(string message)
    {
        SetNotice(message);
        _setStatus(message);
    }

    private void Close(bool force = false)
    {
        if (_closed)
        {
            return;
        }

        if (_operationRunning && !force)
        {
            _operationCancellation?.Cancel();
            return;
        }

        _closed = true;
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

    private string NameText => NativeMethods.GetWindowTextValue(_nameEdit).Trim();

    private string TargetText => NativeMethods.GetWindowTextValue(_targetEdit).Trim();

    private string RemoteText => NativeMethods.GetWindowTextValue(_remoteEdit).Trim();

    private string MessageText => NativeMethods.GetWindowTextValue(_messageEdit).Trim();

    private static int GetSelection(nint list)
    {
        return checked((int)NativeMethods.SendMessage(list, NativeMethods.ListBoxGetCurrentSelection, 0, 0));
    }

    private static bool IsChecked(nint button)
    {
        return NativeMethods.SendMessage(button, NativeMethods.ButtonMessageGetCheck, 0, 0)
            == (nint)NativeMethods.ButtonChecked;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
