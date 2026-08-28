using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeGitOperationDialog : IDisposable
{
    private const string WindowClassName = "Augit.GitOperationDialog.Native";
    private const int ConflictListIdentifier = 1;
    private const int CommandStart = 10;
    private const int CommandSmartCheckout = 11;
    private const int CommandContinue = 12;
    private const int CommandSkip = 13;
    private const int CommandAbort = 14;
    private const int CommandResolve = 15;
    private const int CommandAcceptYours = 16;
    private const int CommandAcceptTheirs = 17;
    private const int CommandExternal = 18;
    private const int CommandRefresh = 19;
    private const int CommandCancel = 20;
    private const int CommandClose = 21;
    private const uint WindowMessageRefresh = NativeMethods.WindowMessageApp + 44;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitOperationDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly GitOperationService _operationService;
    private readonly GitConflictService _conflictService;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly List<GitConflictFileInfo> _conflicts = [];
    private readonly GitMetadataWatcher _metadataWatcher;
    private CancellationTokenSource? _operationCancellation;
    private GitOperationSession? _session;
    private nint _handle;
    private nint _kindLabel;
    private nint _targetLabel;
    private nint _conflictsLabel;
    private nint _kindCombo;
    private nint _targetEdit;
    private nint _startButton;
    private nint _smartCheckoutButton;
    private nint _sessionLabel;
    private nint _conflictList;
    private nint _resolveButton;
    private nint _acceptYoursButton;
    private nint _acceptTheirsButton;
    private nint _externalButton;
    private nint _continueButton;
    private nint _skipButton;
    private nint _abortButton;
    private nint _refreshButton;
    private nint _cancelButton;
    private nint _closeButton;
    private nint _noticeLabel;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _operationRunning;
    private bool _changed;
    private bool _closed;

    internal NativeGitOperationDialog(
        nint owner,
        GitRepositorySnapshot repository,
        GitOperationService operationService,
        GitConflictService conflictService,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(operationService);
        ArgumentNullException.ThrowIfNull(conflictService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setStatus);
        _owner = owner;
        _repository = repository;
        _operationService = operationService;
        _conflictService = conflictService;
        _settings = settings;
        _setStatus = setStatus;
        EnsureWindowClass();
        (int x, int y) = Center(owner, 980, 650);
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.GitOperationManagement,
            NativeMethods.WindowStylePopup
                | NativeMethods.WindowStyleCaption
                | NativeMethods.WindowStyleSystemMenu
                | NativeMethods.WindowStyleThickFrame,
            x,
            y,
            980,
            650,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitOperationDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        ApplyAppearance();
        _metadataWatcher = new(repository);
        _metadataWatcher.Changed += OnGitMetadataChanged;
        Layout();
        _ = LoadAsync();
    }

    internal static bool Show(
        nint owner,
        GitRepositorySnapshot repository,
        GitOperationService operationService,
        GitConflictService conflictService,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        using NativeGitOperationDialog dialog = new(
            owner,
            repository,
            operationService,
            conflictService,
            settings,
            setStatus);
        dialog.Run();
        return dialog._changed;
    }

    internal int ConflictCountForTest => _conflicts.Count;

    internal GitOperationKind? OperationKindForTest => _session?.Kind;

    internal nint HandleForTest => _handle;

    internal async Task RefreshForTestAsync()
    {
        while (_refreshing)
        {
            await Task.Delay(10);
        }

        await LoadAsync();
    }

    internal void CloseForTest()
    {
        Close(force: true);
    }

    public void Dispose()
    {
        _metadataWatcher.Changed -= OnGitMetadataChanged;
        _metadataWatcher.Dispose();
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
                throw new Win32Exception(error, UiText.GitOperationDialogClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeGitOperationDialog? instance;
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
            case WindowMessageRefresh:
                instance.RequestRefresh();
                return 0;
            case NativeMethods.WindowMessageClose:
                instance.Close();
                return 0;
            default:
                return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }
    }

    private void CreateControls()
    {
        _kindLabel = CreateControl(NativeMethods.StaticClass, UiText.OperationKind, 0, NativeMethods.StaticLeft);
        _kindCombo = CreateControl(
            NativeMethods.ComboBoxClass,
            string.Empty,
            30,
            NativeMethods.ComboBoxDropDownList | NativeMethods.WindowStyleVerticalScroll);
        foreach (string item in new[] { "Merge", "Rebase", "Cherry-pick", "Revert" })
        {
            _ = NativeMethods.SendMessage(_kindCombo, NativeMethods.ComboBoxAddString, 0, item);
        }

        _ = NativeMethods.SendMessage(_kindCombo, NativeMethods.ComboBoxSetCurrentSelection, 0, 0);
        _targetLabel = CreateControl(NativeMethods.StaticClass, UiText.OperationTarget, 0, NativeMethods.StaticLeft);
        _targetEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            31,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll);
        _startButton = CreateButton(UiText.StartOperation, CommandStart);
        _smartCheckoutButton = CreateButton(UiText.SmartCheckout, CommandSmartCheckout);
        _sessionLabel = CreateControl(NativeMethods.StaticClass, UiText.NoOperation, 0, NativeMethods.StaticLeft);
        _conflictsLabel = CreateControl(NativeMethods.StaticClass, UiText.ConflictFiles, 0, NativeMethods.StaticLeft);
        _conflictList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            ConflictListIdentifier,
            NativeMethods.WindowStyleBorder
                | NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxNoIntegralHeight);
        _resolveButton = CreateButton(UiText.ResolveConflict, CommandResolve);
        _acceptYoursButton = CreateButton(UiText.AcceptYours, CommandAcceptYours);
        _acceptTheirsButton = CreateButton(UiText.AcceptTheirs, CommandAcceptTheirs);
        _externalButton = CreateButton(UiText.OpenConflictExternally, CommandExternal);
        _continueButton = CreateButton(UiText.ContinueOperation, CommandContinue);
        _skipButton = CreateButton(UiText.SkipOperation, CommandSkip);
        _abortButton = CreateButton(UiText.AbortOperation, CommandAbort);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft);
        _refreshButton = CreateButton(UiText.Refresh, CommandRefresh);
        _cancelButton = CreateButton(UiText.CancelOperation, CommandCancel);
        _closeButton = CreateButton(UiText.Close, CommandClose);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
        UpdateActionControls();
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
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitOperationDialogControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return control;
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == ConflictListIdentifier)
        {
            if (notification == NativeMethods.ListBoxNotificationSelectionChanged)
            {
                UpdateConflictControls();
            }
            else if (notification == NativeMethods.ListBoxNotificationDoubleClick)
            {
                _ = OpenResolverAsync();
            }

            return;
        }

        switch (command)
        {
            case CommandStart:
                _ = RunOperationAsync(
                    token => _operationService.StartAsync(
                        _repository,
                        new(GetSelectedOperationKind(), TargetText),
                        token),
                    "正在执行 Git 操作…");
                break;
            case CommandSmartCheckout:
                _ = RunOperationAsync(
                    token => _operationService.SmartCheckoutAsync(_repository, TargetText, token),
                    "正在执行 Smart Checkout…");
                break;
            case CommandContinue:
                RunSessionAction(GitOperationAction.Continue);
                break;
            case CommandSkip:
                RunSessionAction(GitOperationAction.Skip);
                break;
            case CommandAbort:
                RunSessionAction(GitOperationAction.Abort);
                break;
            case CommandResolve:
                _ = OpenResolverAsync();
                break;
            case CommandAcceptYours:
                _ = AcceptSideAsync(GitConflictSide.Yours);
                break;
            case CommandAcceptTheirs:
                _ = AcceptSideAsync(GitConflictSide.Theirs);
                break;
            case CommandExternal:
                OpenExternally();
                break;
            case CommandRefresh:
                RequestRefresh();
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
        if (_closed || _operationRunning || _refreshing)
        {
            _refreshPending = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshPending = false;
                SetNotice("正在读取 Git 操作状态…");
                GitAdvancedOperationResult result = await _operationService.InspectAsync(_repository);
                if (_closed)
                {
                    return;
                }

                if (!result.IsSuccess || result.Session is null)
                {
                    ShowError(result.ErrorMessage ?? UiText.GitUnavailable);
                    return;
                }

                PopulateSession(result.Session);
                SetNotice("Git 操作状态已刷新。");
            }
            while (_refreshPending && !_operationRunning);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void PopulateSession(GitOperationSession session)
    {
        string? selectedPath = SelectedConflict?.RelativePath;
        _session = session;
        _conflicts.Clear();
        _conflicts.AddRange(session.ConflictFiles);
        _ = NativeMethods.SendMessage(_conflictList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitConflictFileInfo conflict in _conflicts)
        {
            string sides = $"Y:{(conflict.HasYours ? "有" : "无")} T:{(conflict.HasTheirs ? "有" : "无")}";
            _ = NativeMethods.SendMessage(
                _conflictList,
                NativeMethods.ListBoxAddString,
                0,
                $"{conflict.RelativePath}  [{sides}]");
        }

        if (selectedPath is not null)
        {
            int index = _conflicts.FindIndex(conflict =>
                conflict.RelativePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _ = NativeMethods.SendMessage(
                    _conflictList,
                    NativeMethods.ListBoxSetCurrentSelection,
                    unchecked((nuint)index),
                    0);
            }
        }

        string operation = OperationName(session.Kind);
        string branch = session.CurrentBranch ?? "Detached HEAD / 无提交";
        string state = session.IsInProgress ? "进行中" : "无进行中操作";
        string conflicts = session.HasConflicts ? $"{session.ConflictFiles.Count} 个冲突" : "无冲突";
        string actions = string.Join(" / ", new[]
        {
            session.CanContinue ? UiText.ContinueOperation : null,
            session.CanSkip ? UiText.SkipOperation : null,
            session.CanAbort ? UiText.AbortOperation : null,
        }.Where(action => action is not null));
        _ = NativeMethods.SetWindowText(
            _sessionLabel,
            $"分支：{branch}\n操作：{operation}（{state}）\n状态：{conflicts}\n可用动作：{(actions.Length == 0 ? "无" : actions)}");
        UpdateActionControls();
        UpdateConflictControls();
    }

    private async Task RunOperationAsync(
        Func<CancellationToken, Task<GitAdvancedOperationResult>> operation,
        string notice)
    {
        if (!BeginOperation(notice))
        {
            return;
        }

        try
        {
            GitAdvancedOperationResult result = await operation(CurrentOperationToken);
            if (_closed)
            {
                return;
            }

            if (result.Session is not null)
            {
                PopulateSession(result.Session);
            }

            _changed = true;
            if (!result.IsSuccess)
            {
                string message = result.Session?.HasConflicts == true
                    ? $"{result.ErrorMessage}\n\nGit 已保留真实冲突状态，请从冲突文件列表继续处理。"
                    : result.ErrorMessage ?? UiText.GitUnavailable;
                ShowError(message);
            }
            else
            {
                SetNotice("Git 操作已完成。");
                _setStatus("Git 操作已完成。");
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation();
            RequestRefresh();
        }
    }

    private void RunSessionAction(GitOperationAction action)
    {
        if (_session is null || !_session.Supports(action))
        {
            return;
        }

        string impact = action switch
        {
            GitOperationAction.Continue => $"继续 {OperationName(_session.Kind)}，Git 将使用已解决结果完成当前步骤。",
            GitOperationAction.Skip => $"跳过 {OperationName(_session.Kind)} 的当前提交，该提交的变化不会进入结果。",
            GitOperationAction.Abort => $"中止 {OperationName(_session.Kind)}，Git 将恢复到该操作开始前保证的状态。",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        if (NativeMethods.MessageBox(
            _handle,
            impact,
            UiText.AppName,
            NativeMethods.MessageBoxOkCancel
                | (action == GitOperationAction.Continue ? 0 : NativeMethods.MessageBoxIconWarning))
            != NativeMethods.DialogResultOk)
        {
            return;
        }

        _ = RunOperationAsync(
            token => _operationService.ExecuteActionAsync(_repository, action, token),
            $"正在{ActionName(action)} {OperationName(_session.Kind)}…");
    }

    private async Task OpenResolverAsync()
    {
        GitConflictFileInfo? selected = SelectedConflict;
        if (selected is null || _operationRunning)
        {
            ShowError(UiText.SelectConflictFile);
            return;
        }

        SetNotice("正在读取冲突三侧内容…");
        GitConflictLoadResult loaded = await _conflictService.LoadAsync(_repository, selected.RelativePath);
        if (_closed)
        {
            return;
        }

        if (!loaded.IsSuccess || loaded.Document is null)
        {
            ShowError(loaded.ErrorMessage ?? UiText.GitUnavailable);
            return;
        }

        if (loaded.Document.ContentKind != GitConflictContentKind.Text)
        {
            ShowError(UiText.ConflictTextUnavailable);
            return;
        }

        bool saved = NativeConflictResolverDialog.Show(
            _handle,
            _repository,
            _conflictService,
            loaded.Document,
            _settings,
            _setStatus);
        _changed |= saved;
        RequestRefresh();
    }

    private async Task AcceptSideAsync(GitConflictSide side)
    {
        GitConflictFileInfo? selected = SelectedConflict;
        if (selected is null || _operationRunning)
        {
            ShowError(UiText.SelectConflictFile);
            return;
        }

        string sideName = side == GitConflictSide.Yours ? UiText.AcceptYours : UiText.AcceptTheirs;
        if (NativeMethods.MessageBox(
            _handle,
            $"文件：{selected.RelativePath}\n处理：{sideName}\n\n所选整侧将写入工作区并由 Git 标记为已解决，确定继续吗？",
            UiText.AppName,
            NativeMethods.MessageBoxOkCancel | NativeMethods.MessageBoxIconWarning) != NativeMethods.DialogResultOk)
        {
            return;
        }

        if (!BeginOperation($"正在对 {selected.RelativePath} 执行 {sideName}…"))
        {
            return;
        }

        try
        {
            GitConflictMutationResult result = await _conflictService.AcceptSideAsync(
                _repository,
                selected.RelativePath,
                side,
                CurrentOperationToken);
            if (!result.IsSuccess)
            {
                ShowError(result.ErrorMessage ?? UiText.GitUnavailable);
                return;
            }

            _changed = true;
            PopulateSession(result.Session!);
            SetNotice($"{selected.RelativePath} 已由 Git 标记为已解决。");
        }
        finally
        {
            EndOperation();
            RequestRefresh();
        }
    }

    private void OpenExternally()
    {
        GitConflictFileInfo? selected = SelectedConflict;
        if (selected is null || _repository.RepositoryRoot is null)
        {
            ShowError(UiText.SelectConflictFile);
            return;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            _repository.RepositoryRoot,
            selected.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(fullPath))
        {
            ShowError("冲突结果文件当前不存在，请先接受存在的一侧或使用其他外部 Git 工具处理。");
            return;
        }

        ExternalLaunchResult result = ExternalProgramLauncher.OpenWithDefaultApplication(fullPath);
        if (!result.IsSuccess)
        {
            ShowError(result.ErrorMessage ?? UiText.ExternalProgramFailed);
        }
        else
        {
            SetNotice("已使用系统关联的外部工具打开冲突文件，保存或标记后会自动同步。");
        }
    }

    private bool BeginOperation(string notice)
    {
        if (_operationRunning || _closed)
        {
            return false;
        }

        _operationRunning = true;
        _operationCancellation = new();
        SetControlsEnabled(false);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowNormal);
        SetNotice(notice);
        return true;
    }

    private void EndOperation()
    {
        _operationRunning = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        if (!_closed)
        {
            _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
            SetControlsEnabled(true);
            UpdateActionControls();
            UpdateConflictControls();
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _kindLabel,
            _targetLabel,
            _conflictsLabel,
            _kindCombo,
            _kindLabel,
            _targetLabel,
            _conflictsLabel,
            _targetEdit,
            _startButton,
            _smartCheckoutButton,
            _conflictList,
            _resolveButton,
            _acceptYoursButton,
            _acceptTheirsButton,
            _externalButton,
            _continueButton,
            _skipButton,
            _abortButton,
            _refreshButton,
            _closeButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }
    }

    private void UpdateActionControls()
    {
        if (_operationRunning)
        {
            return;
        }

        _ = NativeMethods.EnableWindow(_continueButton, _session?.CanContinue == true);
        _ = NativeMethods.EnableWindow(_skipButton, _session?.CanSkip == true);
        _ = NativeMethods.EnableWindow(_abortButton, _session?.CanAbort == true);
        bool canStart = _session is not { IsInProgress: true } && _session is not { HasConflicts: true };
        _ = NativeMethods.EnableWindow(_startButton, canStart);
        _ = NativeMethods.EnableWindow(_smartCheckoutButton, canStart);
    }

    private void UpdateConflictControls()
    {
        if (_operationRunning)
        {
            return;
        }

        bool selected = SelectedConflict is not null;
        _ = NativeMethods.EnableWindow(_resolveButton, selected);
        _ = NativeMethods.EnableWindow(_acceptYoursButton, selected);
        _ = NativeMethods.EnableWindow(_acceptTheirsButton, selected);
        _ = NativeMethods.EnableWindow(_externalButton, selected);
    }

    private void RequestRefresh()
    {
        if (_closed)
        {
            return;
        }

        if (_operationRunning || _refreshing)
        {
            _refreshPending = true;
            return;
        }

        _ = LoadAsync();
    }

    private void OnGitMetadataChanged(object? sender, EventArgs eventArgs)
    {
        if (!_closed && _handle != 0)
        {
            _ = NativeMethods.PostMessage(_handle, WindowMessageRefresh, 0, 0);
        }
    }

    private void ApplyAppearance()
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, dark);
        foreach (nint control in new[]
        {
            _kindCombo,
            _targetEdit,
            _startButton,
            _smartCheckoutButton,
            _sessionLabel,
            _conflictList,
            _resolveButton,
            _acceptYoursButton,
            _acceptTheirsButton,
            _externalButton,
            _continueButton,
            _skipButton,
            _abortButton,
            _noticeLabel,
            _refreshButton,
            _cancelButton,
            _closeButton,
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
        Move(_kindLabel, 12, 12, 50, 22);
        Move(_kindCombo, 66, 8, 150, 200);
        Move(_targetLabel, 230, 12, 176, 22);
        Move(_targetEdit, 412, 8, Math.Max(180, width - 678), 26);
        Move(_startButton, Math.Max(598, width - 258), 8, 72, 28);
        Move(_smartCheckoutButton, Math.Max(676, width - 180), 8, 132, 28);
        Move(_sessionLabel, 12, 48, Math.Max(0, width - 24), 78);
        Move(_conflictsLabel, 12, 134, Math.Max(0, width - 24), 22);
        int listHeight = Math.Max(120, height - 322);
        Move(_conflictList, 12, 158, Math.Max(0, width - 24), listHeight);
        int conflictActionsTop = 164 + listHeight;
        Move(_resolveButton, 12, conflictActionsTop, 138, 28);
        Move(_acceptYoursButton, 156, conflictActionsTop, 112, 28);
        Move(_acceptTheirsButton, 274, conflictActionsTop, 112, 28);
        Move(_externalButton, 392, conflictActionsTop, 122, 28);
        int sessionActionsTop = conflictActionsTop + 38;
        Move(_continueButton, 12, sessionActionsTop, 100, 28);
        Move(_skipButton, 118, sessionActionsTop, 88, 28);
        Move(_abortButton, 212, sessionActionsTop, 88, 28);
        Move(_noticeLabel, 314, sessionActionsTop + 4, Math.Max(0, width - 326), 42);
        int bottom = Math.Max(8, height - 42);
        Move(_refreshButton, Math.Max(12, width - 286), bottom, 72, 28);
        Move(_cancelButton, Math.Max(90, width - 208), bottom, 94, 28);
        Move(_closeButton, Math.Max(190, width - 108), bottom, 80, 28);
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
        _ = NativeMethods.MessageBox(_handle, message, UiText.AppName, NativeMethods.MessageBoxIconWarning);
    }

    private void SetNotice(string message)
    {
        _ = NativeMethods.SetWindowText(_noticeLabel, message);
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

    private string TargetText => NativeMethods.GetWindowTextValue(_targetEdit).Trim();

    private GitConflictFileInfo? SelectedConflict
    {
        get
        {
            int index = checked((int)NativeMethods.SendMessage(
                _conflictList,
                NativeMethods.ListBoxGetCurrentSelection,
                0,
                0));
            return index >= 0 && index < _conflicts.Count ? _conflicts[index] : null;
        }
    }

    private CancellationToken CurrentOperationToken => _operationCancellation?.Token ?? CancellationToken.None;

    private GitAdvancedOperationKind GetSelectedOperationKind()
    {
        int selection = checked((int)NativeMethods.SendMessage(
            _kindCombo,
            NativeMethods.ComboBoxGetCurrentSelection,
            0,
            0));
        return selection switch
        {
            1 => GitAdvancedOperationKind.Rebase,
            2 => GitAdvancedOperationKind.CherryPick,
            3 => GitAdvancedOperationKind.Revert,
            _ => GitAdvancedOperationKind.Merge,
        };
    }

    private static string OperationName(GitOperationKind kind)
    {
        return kind switch
        {
            GitOperationKind.Merge => "Merge",
            GitOperationKind.Rebase => "Rebase",
            GitOperationKind.CherryPick => "Cherry-pick",
            GitOperationKind.Revert => "Revert",
            GitOperationKind.SmartCheckout => "Smart Checkout",
            GitOperationKind.Bisect => "Bisect（只识别，不提供操作）",
            _ => "无",
        };
    }

    private static string ActionName(GitOperationAction action)
    {
        return action switch
        {
            GitOperationAction.Continue => "继续",
            GitOperationAction.Skip => "跳过",
            GitOperationAction.Abort => "中止",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
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
}
