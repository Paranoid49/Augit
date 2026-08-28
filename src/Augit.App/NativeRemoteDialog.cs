using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeRemoteDialog : IDisposable
{
    private const string WindowClassName = "Augit.RemoteDialog.Native";
    private const int RemoteListIdentifier = 1;
    private const int CommandAdd = 10;
    private const int CommandSave = 11;
    private const int CommandDelete = 12;
    private const int CommandRefresh = 13;
    private const int CommandSetTracking = 14;
    private const int CommandCancelOperation = 15;
    private const int CommandClose = 16;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeRemoteDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly GitRemoteService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly List<GitRemoteInfo> _remotes = [];
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private nint _remoteList;
    private nint _nameEdit;
    private nint _fetchUrlEdit;
    private nint _pushUrlEdit;
    private nint _localBranchEdit;
    private nint _remoteBranchEdit;
    private nint _addButton;
    private nint _saveButton;
    private nint _deleteButton;
    private nint _refreshButton;
    private nint _trackingButton;
    private nint _cancelOperationButton;
    private nint _closeButton;
    private nint _noticeLabel;
    private bool _operationRunning;
    private bool _closed;

    private NativeRemoteDialog(
        nint owner,
        GitRepositorySnapshot repository,
        GitRemoteService service,
        string? currentBranch,
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
            x = ownerRectangle.Left + Math.Max(0, ((ownerRectangle.Right - ownerRectangle.Left) - 720) / 2);
            y = ownerRectangle.Top + Math.Max(0, ((ownerRectangle.Bottom - ownerRectangle.Top) - 450) / 2);
        }

        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.RemoteManagement,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleCaption | NativeMethods.WindowStyleSystemMenu,
            x,
            y,
            720,
            450,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.RemoteDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls(currentBranch);
        ApplyAppearance();
        _ = LoadRemotesAsync();
    }

    internal static void Show(
        nint owner,
        GitRepositorySnapshot repository,
        GitRemoteService service,
        string? currentBranch,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        NativeRemoteDialog dialog = new(owner, repository, service, currentBranch, settings, setStatus);
        dialog.Run();
    }

    private void Run()
    {
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
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
            Dispose();
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
                throw new Win32Exception(error, UiText.RemoteDialogClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeRemoteDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
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

    private void CreateControls(string? currentBranch)
    {
        _remoteList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            RemoteListIdentifier,
            NativeMethods.WindowStyleBorder
                | NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxNoIntegralHeight,
            16,
            18,
            214,
            310);
        CreateControl(NativeMethods.StaticClass, UiText.RemoteName, 0, NativeMethods.StaticLeft, 250, 20, 150, 22);
        _nameEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            20,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            410,
            18,
            270,
            26);
        CreateControl(NativeMethods.StaticClass, UiText.RemoteFetchUrl, 0, NativeMethods.StaticLeft, 250, 62, 150, 22);
        _fetchUrlEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            21,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            410,
            60,
            270,
            26);
        CreateControl(NativeMethods.StaticClass, UiText.RemotePushUrl, 0, NativeMethods.StaticLeft, 250, 104, 150, 38);
        _pushUrlEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            22,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            410,
            106,
            270,
            26);
        _addButton = CreateControl(NativeMethods.ButtonClass, UiText.Add, CommandAdd, NativeMethods.ButtonPushButton, 410, 146, 74, 28);
        _saveButton = CreateControl(NativeMethods.ButtonClass, UiText.Save, CommandSave, NativeMethods.ButtonPushButton, 490, 146, 74, 28);
        _deleteButton = CreateControl(NativeMethods.ButtonClass, UiText.Delete, CommandDelete, NativeMethods.ButtonPushButton, 570, 146, 74, 28);
        CreateControl(NativeMethods.StaticClass, UiText.LocalBranch, 0, NativeMethods.StaticLeft, 250, 204, 150, 22);
        _localBranchEdit = CreateControl(
            NativeMethods.EditClass,
            currentBranch ?? string.Empty,
            23,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            410,
            202,
            270,
            26);
        CreateControl(NativeMethods.StaticClass, UiText.RemoteBranch, 0, NativeMethods.StaticLeft, 250, 246, 150, 22);
        _remoteBranchEdit = CreateControl(
            NativeMethods.EditClass,
            currentBranch ?? string.Empty,
            24,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            410,
            244,
            270,
            26);
        _trackingButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.SetTracking,
            CommandSetTracking,
            NativeMethods.ButtonPushButton,
            410,
            282,
            104,
            28);
        _refreshButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Refresh,
            CommandRefresh,
            NativeMethods.ButtonPushButton,
            16,
            336,
            74,
            28);
        _cancelOperationButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CancelOperation,
            CommandCancelOperation,
            NativeMethods.ButtonPushButton,
            96,
            336,
            92,
            28);
        _closeButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Close,
            CommandClose,
            NativeMethods.ButtonPushButton,
            606,
            350,
            74,
            28);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft, 250, 320, 330, 56);
        _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowHide);
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
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | specificStyle,
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.RemoteDialogControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        NativeTheme.ApplyToControl(control, NativeTheme.IsDark(_settings.Theme));
        return control;
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == RemoteListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            PopulateSelectedRemote();
            return;
        }

        switch (command)
        {
            case CommandAdd:
                _ = AddAsync();
                break;
            case CommandSave:
                _ = SaveAsync();
                break;
            case CommandDelete:
                _ = DeleteAsync();
                break;
            case CommandRefresh:
                _ = LoadRemotesAsync();
                break;
            case CommandSetTracking:
                _ = SetTrackingAsync();
                break;
            case CommandCancelOperation:
                _operationCancellation?.Cancel();
                break;
            case CommandClose:
                Close();
                break;
        }
    }

    private async Task LoadRemotesAsync()
    {
        if (_closed || _operationRunning)
        {
            return;
        }

        _ = NativeMethods.SetWindowText(_noticeLabel, UiText.ReadingRemotes);
        GitRemoteListResult result = await _service.ReadRemotesAsync(_repository);
        if (_closed)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            ShowError(result.ErrorMessage ?? UiText.ReadRemotesFailed);
            return;
        }

        ShowRemotes(result.Remotes!);
        _ = NativeMethods.SetWindowText(_noticeLabel, UiText.RemoteCount(result.Remotes!.Count));
    }

    private async Task AddAsync()
    {
        if (!BeginOperation())
        {
            return;
        }

        try
        {
            GitRemoteOperationResult result = await _service.AddRemoteAsync(
                _repository,
                NativeMethods.GetWindowTextValue(_nameEdit).Trim(),
                NativeMethods.GetWindowTextValue(_fetchUrlEdit).Trim(),
                NullIfWhiteSpace(NativeMethods.GetWindowTextValue(_pushUrlEdit)),
                CurrentOperationToken);
            CompleteOperation(result);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task SaveAsync()
    {
        int index = SelectedRemoteIndex;
        if (index < 0 || index >= _remotes.Count)
        {
            ShowError(UiText.SelectRemoteFirst);
            return;
        }

        if (!BeginOperation())
        {
            return;
        }

        try
        {
            GitRemoteOperationResult result = await _service.UpdateRemoteAsync(
                _repository,
                _remotes[index].Name,
                NativeMethods.GetWindowTextValue(_nameEdit).Trim(),
                NativeMethods.GetWindowTextValue(_fetchUrlEdit).Trim(),
                NullIfWhiteSpace(NativeMethods.GetWindowTextValue(_pushUrlEdit)),
                CurrentOperationToken);
            CompleteOperation(result);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task DeleteAsync()
    {
        int index = SelectedRemoteIndex;
        if (index < 0 || index >= _remotes.Count)
        {
            ShowError(UiText.SelectRemoteFirst);
            return;
        }

        int confirmation = NativeMethods.MessageBox(
            _handle,
            UiText.ConfirmDeleteRemote(_remotes[index].Name),
            UiText.AppName,
            NativeMethods.MessageBoxOkCancel | NativeMethods.MessageBoxIconWarning);
        if (confirmation != NativeMethods.DialogResultOk || !BeginOperation())
        {
            return;
        }

        try
        {
            GitRemoteOperationResult result = await _service.DeleteRemoteAsync(
                _repository,
                _remotes[index].Name,
                CurrentOperationToken);
            CompleteOperation(result);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task SetTrackingAsync()
    {
        if (!BeginOperation())
        {
            return;
        }

        try
        {
            GitRemoteOperationResult result = await _service.SetTrackingAsync(
                _repository,
                NativeMethods.GetWindowTextValue(_localBranchEdit).Trim(),
                NativeMethods.GetWindowTextValue(_nameEdit).Trim(),
                NativeMethods.GetWindowTextValue(_remoteBranchEdit).Trim(),
                CurrentOperationToken);
            CompleteOperation(result);
        }
        finally
        {
            EndOperation();
        }
    }

    private void CompleteOperation(GitRemoteOperationResult result)
    {
        if (_closed)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            ShowError(result.ErrorMessage ?? UiText.RemoteOperationFailed);
        }
        else
        {
            _setStatus(UiText.RemoteOperationCompleted);
            _ = NativeMethods.SetWindowText(_noticeLabel, UiText.RemoteOperationCompleted);
        }

        if (result.ActualRemotes is not null)
        {
            ShowRemotes(result.ActualRemotes);
        }
    }

    private void ShowRemotes(IReadOnlyList<GitRemoteInfo> remotes)
    {
        _remotes.Clear();
        _remotes.AddRange(remotes);
        _ = NativeMethods.SendMessage(_remoteList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitRemoteInfo remote in remotes)
        {
            _ = NativeMethods.SendMessage(
                _remoteList,
                NativeMethods.ListBoxAddString,
                0,
                $"{remote.Name}    {remote.FetchUrl}");
        }
    }

    private void PopulateSelectedRemote()
    {
        int index = SelectedRemoteIndex;
        if (index < 0 || index >= _remotes.Count)
        {
            return;
        }

        GitRemoteInfo remote = _remotes[index];
        _ = NativeMethods.SetWindowText(_nameEdit, remote.Name);
        _ = NativeMethods.SetWindowText(_fetchUrlEdit, remote.FetchUrl);
        _ = NativeMethods.SetWindowText(
            _pushUrlEdit,
            remote.PushUrl.Equals(remote.FetchUrl, StringComparison.Ordinal) ? string.Empty : remote.PushUrl);
    }

    private int SelectedRemoteIndex => checked((int)NativeMethods.SendMessage(
        _remoteList,
        NativeMethods.ListBoxGetCurrentSelection,
        0,
        0));

    private bool BeginOperation()
    {
        if (_closed || _operationRunning)
        {
            return false;
        }

        _operationCancellation?.Dispose();
        _operationCancellation = new();
        _operationRunning = true;
        SetOperationControlsEnabled(false);
        _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowNormal);
        _ = NativeMethods.SetWindowText(_noticeLabel, UiText.UpdatingRemotes);
        return true;
    }

    private void EndOperation()
    {
        _operationRunning = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        if (!_closed)
        {
            SetOperationControlsEnabled(true);
            _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowHide);
        }
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _remoteList,
            _nameEdit,
            _fetchUrlEdit,
            _pushUrlEdit,
            _localBranchEdit,
            _remoteBranchEdit,
            _addButton,
            _saveButton,
            _deleteButton,
            _refreshButton,
            _trackingButton,
            _closeButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }
    }

    private CancellationToken CurrentOperationToken => _operationCancellation?.Token ?? CancellationToken.None;

    private void ShowError(string message)
    {
        if (_closed)
        {
            return;
        }

        _setStatus(message);
        _ = NativeMethods.SetWindowText(_noticeLabel, message);
        _ = NativeMethods.MessageBox(_handle, message, UiText.AppName, NativeMethods.MessageBoxIconWarning);
    }

    private void ApplyAppearance()
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, dark);
        foreach (nint control in new[]
        {
            _remoteList,
            _nameEdit,
            _fetchUrlEdit,
            _pushUrlEdit,
            _localBranchEdit,
            _remoteBranchEdit,
            _addButton,
            _saveButton,
            _deleteButton,
            _refreshButton,
            _trackingButton,
            _cancelOperationButton,
            _closeButton,
            _noticeLabel,
        })
        {
            NativeTheme.ApplyToControl(control, dark);
        }
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
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

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    private static string? NullIfWhiteSpace(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
