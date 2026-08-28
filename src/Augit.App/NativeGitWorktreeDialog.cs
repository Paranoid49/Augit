using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeGitWorktreeDialog : IDisposable
{
    private const string WindowClassName = "Augit.GitWorktreeDialog.Native";
    private const int WorktreeListIdentifier = 1;
    private const int CommandCreateAndOpen = 10;
    private const int CommandOpen = 11;
    private const int CommandRemove = 12;
    private const int CommandRefresh = 13;
    private const int CommandCancel = 14;
    private const int CommandClose = 15;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitWorktreeDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly GitWorktreeService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly List<GitWorktreeInfo> _worktrees = [];
    private readonly List<nint> _controls = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private nint _worktreeList;
    private nint _pathEdit;
    private nint _sourceBranchEdit;
    private nint _newBranchEdit;
    private nint _createAndOpenButton;
    private nint _openButton;
    private nint _removeButton;
    private nint _refreshButton;
    private nint _cancelButton;
    private nint _closeButton;
    private nint _noticeLabel;
    private bool _operationRunning;
    private bool _closed;

    private NativeGitWorktreeDialog(
        nint owner,
        GitRepositorySnapshot repository,
        GitWorktreeService service,
        ApplicationSettings settings,
        Action<string> setStatus)
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
            x = ownerRectangle.Left + Math.Max(0, ((ownerRectangle.Right - ownerRectangle.Left) - 860) / 2);
            y = ownerRectangle.Top + Math.Max(0, ((ownerRectangle.Bottom - ownerRectangle.Top) - 520) / 2);
        }

        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.WorktreeManagement,
            NativeMethods.WindowStylePopup
                | NativeMethods.WindowStyleCaption
                | NativeMethods.WindowStyleSystemMenu
                | NativeMethods.WindowStyleThickFrame,
            x,
            y,
            860,
            520,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.WorktreeDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        ApplyAppearance();
        Layout();
    }

    internal static void Show(
        nint owner,
        GitRepositorySnapshot repository,
        GitWorktreeService service,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        using NativeGitWorktreeDialog dialog = new(owner, repository, service, settings, setStatus);
        dialog.Run();
    }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        Close(force: true);
        _lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Run()
    {
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = LoadAsync();
        try
        {
            while (!_closed && NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0) > 0)
            {
                _ = NativeMethods.TranslateMessage(ref message);
                _ = NativeMethods.DispatchMessage(ref message);
            }
        }
        finally
        {
            _ = NativeMethods.EnableWindow(_owner, true);
            _ = NativeMethods.SetForegroundWindow(_owner);
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
        NativeGitWorktreeDialog? instance;
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
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
            case NativeMethods.WindowMessageClose:
                instance.Close();
                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        CreateControl(NativeMethods.StaticClass, UiText.Worktrees, 0, NativeMethods.StaticLeft);
        _worktreeList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            WorktreeListIdentifier,
            NativeMethods.WindowStyleBorder
                | NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.WindowStyleHorizontalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxNoIntegralHeight);
        CreateControl(NativeMethods.StaticClass, UiText.WorktreePath, 0, NativeMethods.StaticLeft);
        _pathEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            30,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll);
        CreateControl(NativeMethods.StaticClass, UiText.WorktreeSourceBranch, 0, NativeMethods.StaticLeft);
        _sourceBranchEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            31,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll);
        CreateControl(NativeMethods.StaticClass, UiText.WorktreeNewBranch, 0, NativeMethods.StaticLeft);
        _newBranchEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            32,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll);
        _createAndOpenButton = CreateButton(UiText.CreateAndOpenWorktree, CommandCreateAndOpen);
        _openButton = CreateButton(UiText.OpenWorktree, CommandOpen);
        _removeButton = CreateButton(UiText.RemoveWorktree, CommandRemove);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 40, NativeMethods.StaticLeft);
        _refreshButton = CreateButton(UiText.Refresh, CommandRefresh);
        _cancelButton = CreateButton(UiText.CancelOperation, CommandCancel);
        _closeButton = CreateButton(UiText.Close, CommandClose);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
    }

    private nint CreateButton(string text, int identifier)
    {
        return CreateControl(NativeMethods.ButtonClass, text, identifier, NativeMethods.ButtonPushButton);
    }

    private nint CreateControl(string className, string text, int identifier, uint specificStyle)
    {
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleTabStop | specificStyle,
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.WorktreeDialogControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        _controls.Add(control);
        return control;
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == WorktreeListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            PopulateSelectedWorktree();
            return;
        }

        switch (command)
        {
            case CommandCreateAndOpen:
                _ = CreateAndOpenAsync();
                break;
            case CommandOpen:
                OpenSelectedWorktree();
                break;
            case CommandRemove:
                RemoveSelectedWorktree();
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

        SetNotice(UiText.ReadingWorktrees);
        try
        {
            GitWorktreeListResult result = await _service.ReadAsync(_repository, _lifetimeCancellation.Token);
            if (_closed)
            {
                return;
            }

            if (!result.IsSuccess || result.Worktrees is null)
            {
                ShowError(result.ErrorMessage ?? UiText.GitUnavailable);
                return;
            }

            _worktrees.Clear();
            _worktrees.AddRange(result.Worktrees);
            _ = NativeMethods.SendMessage(_worktreeList, NativeMethods.ListBoxResetContent, 0, 0);
            foreach (GitWorktreeInfo worktree in _worktrees)
            {
                string current = worktree.IsCurrent ? "* " : "  ";
                string branch = worktree.IsBare
                    ? "[Bare]"
                    : worktree.IsDetached ? "[Detached HEAD]" : worktree.Branch ?? "[无分支]";
                string state = worktree.IsLocked
                    ? $"  [已锁定：{worktree.LockReason ?? "未说明原因"}]"
                    : worktree.IsPrunable ? $"  [可清理：{worktree.PruneReason ?? "未说明原因"}]" : string.Empty;
                _ = NativeMethods.SendMessage(
                    _worktreeList,
                    NativeMethods.ListBoxAddString,
                    0,
                    $"{current}{branch}  {worktree.Path}{state}");
            }

            SetNotice(UiText.WorktreeCount(_worktrees.Count));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task CreateAndOpenAsync()
    {
        string path = PathText;
        return RunActionAsync(
            token => _service.CreateAsync(
                _repository,
                path,
                SourceBranchText,
                NullIfEmpty(NewBranchText),
                token),
            path);
    }

    private void OpenSelectedWorktree()
    {
        if (TryGetSelectedWorktree(out GitWorktreeInfo? worktree))
        {
            OpenWorkspace(worktree!.Path);
        }
    }

    private void RemoveSelectedWorktree()
    {
        if (!TryGetSelectedWorktree(out GitWorktreeInfo? worktree))
        {
            return;
        }

        if (NativeMethods.MessageBox(
            _handle,
            UiText.ConfirmRemoveWorktreeDetails(worktree!.Path, worktree.Branch),
            UiText.AppName,
            NativeMethods.MessageBoxOkCancel | NativeMethods.MessageBoxIconWarning) != NativeMethods.DialogResultOk)
        {
            return;
        }

        _ = RunActionAsync(token => _service.RemoveAsync(_repository, worktree.Path, token));
    }

    private async Task RunActionAsync(
        Func<CancellationToken, Task<GitActionResult>> action,
        string? openPathAfterSuccess = null)
    {
        if (_operationRunning || _closed)
        {
            return;
        }

        _operationRunning = true;
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        SetControlsEnabled(false);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowNormal);
        SetNotice(UiText.RunningWorktreeOperation);
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

            _setStatus(UiText.WorktreeOperationCompleted);
            SetNotice(UiText.WorktreeOperationCompleted);
            if (openPathAfterSuccess is not null)
            {
                OpenWorkspace(openPathAfterSuccess);
            }
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

    private void OpenWorkspace(string path)
    {
        if (!Directory.Exists(path))
        {
            ShowError(UiText.WorktreeDirectoryMissing);
            return;
        }

        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            ShowError(UiText.AugitExecutableUnavailable);
            return;
        }

        ExternalLaunchResult launched = ExternalProgramLauncher.OpenAugitWorkspace(executablePath, path);
        if (!launched.IsSuccess)
        {
            ShowError(launched.ErrorMessage ?? UiText.ExternalProgramFailed);
            return;
        }

        _setStatus(UiText.WorktreeOpened);
        SetNotice(UiText.WorktreeOpened);
    }

    private bool TryGetSelectedWorktree(out GitWorktreeInfo? worktree)
    {
        int index = checked((int)NativeMethods.SendMessage(
            _worktreeList,
            NativeMethods.ListBoxGetCurrentSelection,
            0,
            0));
        worktree = index >= 0 && index < _worktrees.Count ? _worktrees[index] : null;
        if (worktree is null)
        {
            ShowError(UiText.SelectWorktreeFirst);
            return false;
        }

        return true;
    }

    private void PopulateSelectedWorktree()
    {
        int index = checked((int)NativeMethods.SendMessage(
            _worktreeList,
            NativeMethods.ListBoxGetCurrentSelection,
            0,
            0));
        if (index < 0 || index >= _worktrees.Count)
        {
            return;
        }

        GitWorktreeInfo worktree = _worktrees[index];
        _ = NativeMethods.SetWindowText(_pathEdit, worktree!.Path);
        _ = NativeMethods.SetWindowText(_sourceBranchEdit, worktree.Branch ?? worktree.CommitHash ?? string.Empty);
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _worktreeList,
            _pathEdit,
            _sourceBranchEdit,
            _newBranchEdit,
            _createAndOpenButton,
            _openButton,
            _removeButton,
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
        foreach (nint control in _controls)
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
        nint worktreesLabel = _controls[0];
        nint pathLabel = _controls[2];
        nint sourceLabel = _controls[4];
        nint newBranchLabel = _controls[6];
        int rightWidth = Math.Min(300, Math.Max(240, width / 3));
        int rightLeft = Math.Max(280, width - rightWidth - 14);
        Move(worktreesLabel, 14, 12, Math.Max(0, rightLeft - 28), 22);
        Move(_worktreeList, 14, 36, Math.Max(0, rightLeft - 28), Math.Max(180, height - 126));
        Move(pathLabel, rightLeft, 20, rightWidth, 22);
        Move(_pathEdit, rightLeft, 44, rightWidth, 26);
        Move(sourceLabel, rightLeft, 82, rightWidth, 22);
        Move(_sourceBranchEdit, rightLeft, 106, rightWidth, 26);
        Move(newBranchLabel, rightLeft, 144, rightWidth, 22);
        Move(_newBranchEdit, rightLeft, 168, rightWidth, 26);
        Move(_createAndOpenButton, rightLeft, 210, 124, 28);
        Move(_openButton, rightLeft + 130, 210, 72, 28);
        Move(_removeButton, rightLeft + 208, 210, 88, 28);
        Move(_noticeLabel, rightLeft, 254, rightWidth, Math.Max(42, height - 340));
        Move(_refreshButton, Math.Max(14, width - 280), Math.Max(260, height - 48), 74, 28);
        Move(_cancelButton, Math.Max(94, width - 200), Math.Max(260, height - 48), 94, 28);
        Move(_closeButton, Math.Max(174, width - 94), Math.Max(260, height - 48), 80, 28);
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private void SetNotice(string text)
    {
        _ = NativeMethods.SetWindowText(_noticeLabel, text);
    }

    private void ShowError(string message)
    {
        SetNotice(message);
        _setStatus(message);
        _ = NativeMethods.MessageBox(_handle, message, UiText.AppName, NativeMethods.MessageBoxIconWarning);
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

    private string PathText => NativeMethods.GetWindowTextValue(_pathEdit).Trim();

    private string SourceBranchText => NativeMethods.GetWindowTextValue(_sourceBranchEdit).Trim();

    private string NewBranchText => NativeMethods.GetWindowTextValue(_newBranchEdit).Trim();

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
