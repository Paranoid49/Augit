using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeGitLocalStateDialog : IDisposable
{
    private const string WindowClassName = "Augit.GitLocalStateDialog.Native";
    private const int StashListIdentifier = 1;
    private const int CommandStash = 10;
    private const int CommandStashUntracked = 11;
    private const int CommandApply = 12;
    private const int CommandPop = 13;
    private const int CommandView = 14;
    private const int CommandResetSoft = 15;
    private const int CommandResetMixed = 16;
    private const int CommandResetHard = 17;
    private const int CommandRefresh = 18;
    private const int CommandCancel = 19;
    private const int CommandClose = 20;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitLocalStateDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly GitWorkspaceStateService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly List<GitStashInfo> _stashes = [];
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private nint _stashList;
    private nint _messageEdit;
    private nint _resetTargetEdit;
    private nint _stashButton;
    private nint _stashUntrackedButton;
    private nint _applyButton;
    private nint _popButton;
    private nint _viewButton;
    private nint _resetSoftButton;
    private nint _resetMixedButton;
    private nint _resetHardButton;
    private nint _refreshButton;
    private nint _cancelButton;
    private nint _closeButton;
    private nint _noticeLabel;
    private bool _operationRunning;
    private bool _closed;

    private NativeGitLocalStateDialog(
        nint owner,
        GitRepositorySnapshot repository,
        GitWorkspaceStateService service,
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
            x = ownerRectangle.Left + Math.Max(0, ((ownerRectangle.Right - ownerRectangle.Left) - 760) / 2);
            y = ownerRectangle.Top + Math.Max(0, ((ownerRectangle.Bottom - ownerRectangle.Top) - 480) / 2);
        }

        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.LocalStateManagement,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleCaption | NativeMethods.WindowStyleSystemMenu,
            x,
            y,
            760,
            480,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.LocalStateDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        ApplyAppearance();
    }

    internal static void Show(
        nint owner,
        GitRepositorySnapshot repository,
        GitWorkspaceStateService service,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        using NativeGitLocalStateDialog dialog = new(owner, repository, service, settings, setStatus);
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
                throw new Win32Exception(error, UiText.LocalStateDialogClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeGitLocalStateDialog? instance;
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

    private void CreateControls()
    {
        CreateControl(NativeMethods.StaticClass, UiText.Stashes, 0, NativeMethods.StaticLeft, 14, 12, 380, 22);
        _stashList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            StashListIdentifier,
            NativeMethods.WindowStyleBorder | NativeMethods.WindowStyleVerticalScroll | NativeMethods.ListBoxNotify,
            14,
            36,
            430,
            250);
        CreateControl(NativeMethods.StaticClass, UiText.StashMessage, 0, NativeMethods.StaticLeft, 462, 20, 260, 22);
        _messageEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            30,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            462,
            44,
            260,
            26);
        _stashButton = CreateButton(UiText.CreateStash, CommandStash, 462, 82, 118);
        _stashUntrackedButton = CreateButton(UiText.CreateStashWithUntracked, CommandStashUntracked, 586, 82, 136);
        _viewButton = CreateButton(UiText.ViewStash, CommandView, 462, 124, 84);
        _applyButton = CreateButton(UiText.ApplyStash, CommandApply, 552, 124, 104);
        _popButton = CreateButton(UiText.PopStash, CommandPop, 662, 124, 104);
        CreateControl(NativeMethods.StaticClass, UiText.ResetTarget, 0, NativeMethods.StaticLeft, 462, 180, 260, 22);
        _resetTargetEdit = CreateControl(
            NativeMethods.EditClass,
            "HEAD",
            31,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            462,
            204,
            260,
            26);
        _resetSoftButton = CreateButton(UiText.ResetSoft, CommandResetSoft, 462, 242, 96);
        _resetMixedButton = CreateButton(UiText.ResetMixed, CommandResetMixed, 564, 242, 104);
        _resetHardButton = CreateButton(UiText.ResetHard, CommandResetHard, 674, 242, 96);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 40, NativeMethods.StaticLeft, 14, 306, 708, 52);
        _refreshButton = CreateButton(UiText.Refresh, CommandRefresh, 416, 390, 74);
        _cancelButton = CreateButton(UiText.CancelOperation, CommandCancel, 496, 390, 94);
        _closeButton = CreateButton(UiText.Close, CommandClose, 648, 390, 74);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.LocalStateDialogControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return control;
    }

    private void HandleCommand(nuint wordParameter)
    {
        switch (NativeMethods.LowWord(wordParameter))
        {
            case CommandStash:
                _ = RunActionAsync(token => _service.StashAsync(_repository, NullIfEmpty(MessageText), false, token));
                break;
            case CommandStashUntracked:
                _ = RunActionAsync(token => _service.StashAsync(_repository, NullIfEmpty(MessageText), true, token));
                break;
            case CommandApply:
                if (TryGetSelectedStash(out GitStashInfo? apply))
                {
                    _ = RunActionAsync(token => _service.UnstashAsync(_repository, apply!.Reference, true, token));
                }
                break;
            case CommandPop:
                if (TryGetSelectedStash(out GitStashInfo? pop))
                {
                    _ = RunActionAsync(token => _service.UnstashAsync(_repository, pop!.Reference, false, token));
                }
                break;
            case CommandView:
                _ = ViewSelectedStashAsync();
                break;
            case CommandResetSoft:
                Reset(GitResetMode.Soft, UiText.ConfirmResetSoft);
                break;
            case CommandResetMixed:
                Reset(GitResetMode.Mixed, UiText.ConfirmResetMixed);
                break;
            case CommandResetHard:
                Reset(GitResetMode.Hard, UiText.ConfirmResetHard);
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

        SetNotice("正在读取 Stash…");
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

        _stashes.Clear();
        _stashes.AddRange(result.Stashes);
        _ = NativeMethods.SendMessage(_stashList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitStashInfo stash in _stashes)
        {
            _ = NativeMethods.SendMessage(
                _stashList,
                NativeMethods.ListBoxAddString,
                0,
                $"{stash.Reference}  {stash.Date.LocalDateTime:g}  {stash.Subject}");
        }

        SetNotice($"{_stashes.Count} 个 Stash");
    }

    private async Task ViewSelectedStashAsync()
    {
        if (!TryGetSelectedStash(out GitStashInfo? stash))
        {
            return;
        }

        GitStashContentResult result = await _service.ReadStashContentAsync(_repository, stash!.Reference);
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
        if (result.Document.CopyableCommand is not null)
        {
            content = string.Concat(content, Environment.NewLine, Environment.NewLine, result.Document.CopyableCommand);
        }

        NativeGitTextDialog.Show(_handle, $"{UiText.ViewStash} — {stash.Reference}", content, _settings);
    }

    private void Reset(GitResetMode mode, string warning)
    {
        if (NativeMethods.MessageBox(
            _handle,
            UiText.ConfirmResetDetails(ResetTargetText, warning),
            UiText.AppName,
            NativeMethods.MessageBoxOkCancel | NativeMethods.MessageBoxIconWarning) != 1)
        {
            return;
        }

        _ = RunActionAsync(token => _service.ResetAsync(_repository, ResetTargetText, mode, token));
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

            _setStatus(UiText.LocalStateOperationCompleted);
            SetNotice(UiText.LocalStateOperationCompleted);
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

    private bool TryGetSelectedStash(out GitStashInfo? stash)
    {
        int index = checked((int)NativeMethods.SendMessage(
            _stashList,
            NativeMethods.ListBoxGetCurrentSelection,
            0,
            0));
        stash = index >= 0 && index < _stashes.Count ? _stashes[index] : null;
        if (stash is null)
        {
            ShowError(UiText.SelectStashFirst);
            return false;
        }

        return true;
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _stashList,
            _messageEdit,
            _resetTargetEdit,
            _stashButton,
            _stashUntrackedButton,
            _applyButton,
            _popButton,
            _viewButton,
            _resetSoftButton,
            _resetMixedButton,
            _resetHardButton,
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
            _stashList,
            _messageEdit,
            _resetTargetEdit,
            _stashButton,
            _stashUntrackedButton,
            _applyButton,
            _popButton,
            _viewButton,
            _resetSoftButton,
            _resetMixedButton,
            _resetHardButton,
            _refreshButton,
            _cancelButton,
            _closeButton,
            _noticeLabel,
        })
        {
            NativeTheme.ApplyToControl(control, dark);
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

    private string MessageText => NativeMethods.GetWindowTextValue(_messageEdit).Trim();

    private string ResetTargetText => NativeMethods.GetWindowTextValue(_resetTargetEdit).Trim();

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
