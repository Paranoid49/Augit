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
    private const int DialogWidth = 930;
    private const int DialogHeight = 640;
    private const int ConflictDialogHeight = 300;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
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
    private const int CommandHeaderClose = 22;
    private const int KindComboIdentifier = 30;
    private const uint WindowMessageRefresh = NativeMethods.WindowMessageApp + 44;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitOperationDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly string[] OperationLabels = ["Merge", "Rebase", "Cherry-pick", "Revert"];
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
    private nint _headerCloseButton;
    private nint _noticeLabel;
    private nint _controlBrush;
    private NativeToolTip? _toolTip;
    private bool _dark;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _operationRunning;
    private bool _changed;
    private bool _closed;
    private bool? _compactConflictLayout;
    private bool _showFallbackConflictActions;

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
        (int x, int y) = Center(owner, S(DialogWidth), S(DialogHeight));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.GitOperationManagement,
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitOperationDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        CreateToolTips();
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

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);

    internal static string WindowClassNameForTest => WindowClassName;

    internal static int ConflictDialogHeightForTest => ConflictDialogHeight;

    internal static string ConflictSessionContextForTest(GitOperationSession session) =>
        ConflictSessionContext(session);

    internal static (
        bool Refresh,
        bool Cancel,
        bool Close,
        bool Continue,
        bool Skip,
        bool Abort) FooterVisibilityForTest(
            bool operationRunning,
            bool canContinue,
            bool canSkip,
            bool canAbort,
            bool keepBlockedContinueVisible = false) =>
        GetFooterVisibility(
            operationRunning,
            canContinue || keepBlockedContinueVisible,
            canSkip,
            canAbort);

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
        _ = NativeMethods.SetFocus(_kindCombo);
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
    /// 按 Git 操作窗口的视觉顺序循环移动焦点，当前状态不可用的动作会被跳过。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [
                _kindCombo,
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
                _cancelButton,
                _closeButton,
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
            case NativeMethods.WindowMessagePaint:
                return instance.PaintWindow();
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessageNonClientHitTest:
                return instance.HitTest();
            case NativeMethods.WindowMessageDrawItem:
                return instance.DrawControl(longParameter) ? 1 : 0;
            case NativeMethods.WindowMessageControlColorButton:
            case NativeMethods.WindowMessageControlColorStatic:
            case NativeMethods.WindowMessageControlColorEdit:
            case NativeMethods.WindowMessageControlColorListBox:
                return instance.ApplyControlColor(unchecked((nint)wordParameter));
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
            KindComboIdentifier,
            NativeComboBoxTheme.ControlStyle);
        foreach (string item in OperationLabels)
        {
            _ = NativeMethods.SendMessage(_kindCombo, NativeMethods.ComboBoxAddString, 0, item);
        }

        _ = NativeMethods.SendMessage(_kindCombo, NativeMethods.ComboBoxSetCurrentSelection, 0, 0);
        if (!NativeComboBoxTheme.Register(_kindCombo, () => _dark))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitOperationDialogControlCreateFailed);
        }
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
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight);
        _ = NativeMethods.SendMessage(
            _conflictList,
            NativeMethods.ListBoxSetItemHeight,
            0,
            S(30));
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
        _headerCloseButton = CreateButton(UiText.CloseSymbol, CommandHeaderClose);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
        UpdateActionControls();
    }

    private nint CreateButton(string text, int identifier)
    {
        return CreateControl(NativeMethods.ButtonClass, text, identifier, NativeMethods.ButtonOwnerDraw);
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

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        return control;
    }

    private void CreateToolTips()
    {
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_startButton, UiText.StartOperation);
        _toolTip.Add(_smartCheckoutButton, UiText.SmartCheckout);
        _toolTip.Add(_resolveButton, UiText.ResolveConflict);
        _toolTip.Add(_acceptYoursButton, UiText.AcceptYours);
        _toolTip.Add(_acceptTheirsButton, UiText.AcceptTheirs);
        _toolTip.Add(_externalButton, UiText.OpenConflictExternally);
        _toolTip.Add(_continueButton, UiText.ContinueOperation);
        _toolTip.Add(_skipButton, UiText.SkipOperation);
        _toolTip.Add(_abortButton, UiText.AbortOperation);
        _toolTip.Add(_refreshButton, UiText.Refresh);
        _toolTip.Add(_cancelButton, UiText.CancelOperation);
        _toolTip.Add(_closeButton, UiText.Close);
        _toolTip.Add(_headerCloseButton, UiText.Close);
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
                _ = OpenResolverAsync();
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
                _ = ConfirmAndRunSmartCheckoutAsync();
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
            case CommandHeaderClose:
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
                SetNotice(result.Session.HasConflicts
                    ? $"还有 {result.Session.ConflictFiles.Count} 个冲突未解决，解决后可继续当前操作。"
                    : "Git 操作状态已刷新。");
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
        _showFallbackConflictActions = false;
        _conflicts.Clear();
        _conflicts.AddRange(session.ConflictFiles);
        _ = NativeMethods.SendMessage(_conflictList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitConflictFileInfo conflict in _conflicts)
        {
            _ = NativeMethods.SendMessage(
                _conflictList,
                NativeMethods.ListBoxAddString,
                0,
                conflict.RelativePath);
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
        else if (_conflicts.Count > 0)
        {
            _ = NativeMethods.SendMessage(
                _conflictList,
                NativeMethods.ListBoxSetCurrentSelection,
                0,
                0);
        }

        string operation = OperationName(session.Kind);
        string branch = session.CurrentBranch ?? "Detached HEAD / 无提交";
        if (session.HasConflicts)
        {
            _ = NativeMethods.SetWindowText(
                _conflictsLabel,
                $"{session.ConflictFiles.Count} 个冲突文件");
            _ = NativeMethods.SetWindowText(
                _sessionLabel,
                ConflictSessionContext(session));
            _ = NativeMethods.SetWindowText(_continueButton, $"Continue {operation}");
            _ = NativeMethods.SetWindowText(_abortButton, $"Abort {operation}");
        }
        else
        {
            string state = session.IsInProgress ? "进行中" : "无进行中操作";
            string conflicts = session.HasConflicts ? $"{session.ConflictFiles.Count} 个冲突" : "无冲突";
            string actions = string.Join(" / ", new[]
            {
                session.CanContinue ? UiText.ContinueOperation : null,
                session.CanSkip ? UiText.SkipOperation : null,
                session.CanAbort ? UiText.AbortOperation : null,
            }.Where(action => action is not null));
            _ = NativeMethods.SetWindowText(_conflictsLabel, UiText.ConflictFiles);
            _ = NativeMethods.SetWindowText(
                _sessionLabel,
                $"分支：{branch}\n操作：{operation}（{state}）\n状态：{conflicts}\n可用动作：{(actions.Length == 0 ? "无" : actions)}");
            _ = NativeMethods.SetWindowText(_continueButton, UiText.ContinueOperation);
            _ = NativeMethods.SetWindowText(_abortButton, UiText.AbortOperation);
        }

        ApplySessionLayoutMode();
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

    private async Task ConfirmAndRunSmartCheckoutAsync()
    {
        if (_closed || _operationRunning)
        {
            return;
        }

        string targetBranch = TargetText;
        if (string.IsNullOrWhiteSpace(targetBranch))
        {
            ShowError("请输入目标本地分支。");
            return;
        }

        GitAdvancedOperationResult inspected = await _operationService.InspectAsync(_repository);
        if (_closed)
        {
            return;
        }

        if (!inspected.IsSuccess)
        {
            ShowError(inspected.ErrorMessage ?? UiText.GitUnavailable);
            return;
        }

        GitStatusSnapshot? status = inspected.ActualStatus;
        string currentBranch = status?.CurrentBranch ?? "Detached HEAD / 无提交";
        int trackedCount = status?.Changes.Count ?? 0;
        int untrackedCount = status?.UnversionedFiles.Count ?? 0;
        string detail = BuildSmartCheckoutImpact(
            currentBranch,
            targetBranch,
            trackedCount,
            untrackedCount);
        if (!NativeActionConfirmationDialog.Show(
                _handle,
                _settings,
                $"切换到 {targetBranch}",
                UiText.SmartCheckoutWarningTitle,
                detail,
                UiText.SmartCheckout))
        {
            return;
        }

        _ = RunOperationAsync(
            token => _operationService.SmartCheckoutAsync(_repository, targetBranch, token),
            UiText.SmartCheckoutRunning);
    }

    internal static string BuildSmartCheckoutImpactForTest(
        string currentBranch,
        string targetBranch,
        int trackedCount,
        int untrackedCount)
    {
        return BuildSmartCheckoutImpact(
            currentBranch,
            targetBranch,
            trackedCount,
            untrackedCount);
    }

    private static string BuildSmartCheckoutImpact(
        string currentBranch,
        string targetBranch,
        int trackedCount,
        int untrackedCount)
    {
        return string.Join(
            "\n",
            UiText.SmartCheckoutWarningDetail,
            string.Empty,
            $"当前分支：{currentBranch}",
            $"目标分支：{targetBranch}",
            $"将暂存：{trackedCount} 个已跟踪文件和 {untrackedCount} 个未跟踪文件");
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
        string actionName = ActionName(action);
        string operationName = OperationName(_session.Kind);
        if (!NativeActionConfirmationDialog.Show(
                _handle,
                _settings,
                $"{actionName} {operationName}",
                $"确认{actionName}当前 {operationName}",
                impact,
                $"{actionName} {operationName}",
                danger: action != GitOperationAction.Continue))
        {
            return;
        }

        _ = RunOperationAsync(
            token => _operationService.ExecuteActionAsync(_repository, action, token),
            $"正在{actionName} {operationName}…");
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
            _showFallbackConflictActions = true;
            UpdateConflictControls();
            Layout();
            SetNotice(UiText.ConflictTextUnavailable);
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
        string impact = $"所选整侧将写入工作区并由 Git 标记为已解决，确定继续吗？\n\n"
            + $"文件：{selected.RelativePath}\n处理：{sideName}";
        if (!NativeActionConfirmationDialog.Show(
                _handle,
                _settings,
                sideName,
                $"使用 {sideName} 解决整个文件",
                impact,
                sideName,
                danger: true))
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
        UpdateFooterControls();
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
            SetControlsEnabled(true);
            UpdateActionControls();
            UpdateConflictControls();
            UpdateFooterControls();
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
            _headerCloseButton,
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
        UpdateFooterControls();
    }

    private void UpdateConflictControls()
    {
        if (_operationRunning)
        {
            return;
        }

        bool selected = SelectedConflict is not null;
        bool fallback = selected && _showFallbackConflictActions;
        SetVisible(_resolveButton, !IsConflictSession);
        SetVisible(_acceptYoursButton, !IsConflictSession || fallback);
        SetVisible(_acceptTheirsButton, !IsConflictSession || fallback);
        SetVisible(_externalButton, !IsConflictSession || fallback);
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
        _dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, _dark);
        foreach (nint control in new[]
        {
            _kindLabel,
            _targetLabel,
            _conflictsLabel,
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
            _headerCloseButton,
        })
        {
            NativeTheme.ApplyToControl(control, _dark);
        }

        _toolTip?.ApplyAppearance(_dark);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }

        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
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
        if (item.ControlIdentifier == ConflictListIdentifier)
        {
            return DrawConflictListItem(item);
        }

        return unchecked((int)item.ControlIdentifier) switch
        {
            KindComboIdentifier => NativeComboBoxTheme.DrawItem(parameter, OperationLabels, _dark),
            CommandStart or CommandSmartCheckout or CommandContinue =>
                NativeTheme.DrawFlatButton(parameter, _dark, emphasized: true),
            CommandAbort => NativeTheme.DrawFlatButton(parameter, _dark, outlined: true),
            CommandClose => NativeTheme.DrawFlatButton(parameter, _dark, outlined: true),
            CommandHeaderClose => NativeTheme.DrawFlatButton(parameter, _dark),
            CommandSkip or CommandResolve or CommandAcceptYours or CommandAcceptTheirs
                or CommandExternal or CommandRefresh or CommandCancel =>
                NativeTheme.DrawFlatButton(parameter, _dark, outlined: true),
            _ => false,
        };
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
            Fill(
                deviceContext,
                new()
                {
                    Left = 0,
                    Top = S(HeaderHeight),
                    Right = client.Right,
                    Bottom = S(HeaderHeight + 1),
                },
                palette.Border);
            Fill(
                deviceContext,
                new()
                {
                    Left = 0,
                    Top = client.Bottom - S(FooterHeight),
                    Right = client.Right,
                    Bottom = client.Bottom - S(FooterHeight - 1),
                },
                palette.Border);
            DrawText(
                deviceContext,
                DialogTitle,
                new()
                {
                    Left = S(17),
                    Top = S(8),
                    Right = client.Right - S(52),
                    Bottom = S(38),
                },
                palette.Text,
                NativeTheme.UiMediumFont);
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

        return point.Y < S(HeaderHeight)
            ? NativeMethods.HitTestCaption
            : NativeMethods.HitTestClient;
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle rectangle))
        {
            return;
        }

        int width = Math.Max(0, rectangle.Right - rectangle.Left);
        int height = Math.Max(0, rectangle.Bottom - rectangle.Top);
        int bodyTop = S(HeaderHeight);
        int footerTop = height - S(FooterHeight);
        bool conflictSession = IsConflictSession;
        int listTop;
        if (conflictSession)
        {
            Move(_conflictsLabel, S(17), bodyTop + S(10), S(230), S(32));
            Move(
                _sessionLabel,
                Math.Max(S(254), width - S(360)),
                bodyTop + S(10),
                S(343),
                S(32));
            listTop = bodyTop + S(48);
        }
        else
        {
            Move(_kindLabel, S(17), bodyTop + S(17), S(48), S(28));
            Move(_kindCombo, S(70), bodyTop + S(13), S(148), S(220));
            Move(_targetLabel, S(232), bodyTop + S(17), S(176), S(28));
            Move(_targetEdit, S(408), bodyTop + S(13), Math.Max(S(180), width - S(676)), S(30));
            Move(_startButton, Math.Max(S(598), width - S(258)), bodyTop + S(14), S(72), S(28));
            Move(_smartCheckoutButton, Math.Max(S(676), width - S(180)), bodyTop + S(14), S(132), S(28));
            Move(_sessionLabel, S(29), bodyTop + S(65), Math.Max(0, width - S(58)), S(76));
            Move(_conflictsLabel, S(17), bodyTop + S(151), Math.Max(0, width - S(34)), S(28));
            listTop = bodyTop + S(180);
        }

        bool showConflictActions = !conflictSession || _showFallbackConflictActions;
        int conflictActionsTop = showConflictActions ? footerTop - S(50) : footerTop;
        Move(
            _conflictList,
            S(17),
            listTop,
            Math.Max(0, width - S(34)),
            Math.Max(S(70), conflictActionsTop - listTop - S(8)));
        Move(_resolveButton, S(17), conflictActionsTop, S(138), S(28));
        Move(_acceptYoursButton, S(163), conflictActionsTop, S(112), S(28));
        Move(_acceptTheirsButton, S(283), conflictActionsTop, S(112), S(28));
        Move(_externalButton, S(403), conflictActionsTop, S(122), S(28));
        int right = width - S(17);
        LayoutFooterButton(_continueButton, ref right, S(118), footerTop);
        LayoutFooterButton(_skipButton, ref right, S(80), footerTop);
        LayoutFooterButton(_abortButton, ref right, S(118), footerTop);
        LayoutFooterButton(_cancelButton, ref right, S(88), footerTop);
        LayoutFooterButton(_closeButton, ref right, S(78), footerTop);
        LayoutFooterButton(_refreshButton, ref right, S(78), footerTop);
        Move(_noticeLabel, S(17), footerTop + S(12), Math.Max(0, right - S(29)), S(28));
        Move(_headerCloseButton, Math.Max(S(17), width - S(45)), S(7), S(32), S(31));
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private static void LayoutFooterButton(nint control, ref int right, int width, int footerTop)
    {
        if (control == 0 || !NativeMethods.IsWindowVisible(control))
        {
            return;
        }

        right -= width;
        Move(control, right, footerTop + S(12), width, S(28));
        right -= S(8);
    }

    private void UpdateFooterControls()
    {
        (bool refresh, bool cancel, bool close, bool continueAction, bool skip, bool abort) =
            GetFooterVisibility(
                _operationRunning,
                _session?.CanContinue == true || ShouldKeepContinueVisible(_session),
                _session?.CanSkip == true,
                _session?.CanAbort == true);
        SetVisible(_refreshButton, refresh);
        SetVisible(_cancelButton, cancel);
        SetVisible(_closeButton, close);
        SetVisible(_continueButton, continueAction);
        SetVisible(_skipButton, skip);
        SetVisible(_abortButton, abort);
        Layout();
    }

    private static (
        bool Refresh,
        bool Cancel,
        bool Close,
        bool Continue,
        bool Skip,
        bool Abort) GetFooterVisibility(
            bool operationRunning,
            bool canContinue,
            bool canSkip,
            bool canAbort)
    {
        if (operationRunning)
        {
            return (false, true, false, false, false, false);
        }

        bool hasSessionActions = canContinue || canSkip || canAbort;
        return (
            !hasSessionActions,
            false,
            !hasSessionActions,
            canContinue,
            canSkip,
            canAbort);
    }

    private static void SetVisible(nint control, bool visible)
    {
        if (control != 0)
        {
            _ = NativeMethods.ShowWindow(control, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
    }

    private void ApplySessionLayoutMode()
    {
        bool compact = IsConflictSession;
        bool showOperationForm = !compact;
        foreach (nint control in new[]
        {
            _kindLabel,
            _kindCombo,
            _targetLabel,
            _targetEdit,
            _startButton,
            _smartCheckoutButton,
        })
        {
            SetVisible(control, showOperationForm);
        }

        UpdateConflictControls();

        if (_compactConflictLayout != compact)
        {
            _compactConflictLayout = compact;
            int targetHeight = S(compact ? ConflictDialogHeight : DialogHeight);
            int targetWidth = S(DialogWidth);
            (int x, int y) = Center(_owner, targetWidth, targetHeight);
            _ = NativeMethods.MoveWindow(_handle, x, y, targetWidth, targetHeight, true);
        }

        Layout();
    }

    private bool DrawConflictListItem(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= _conflicts.Count)
        {
            return true;
        }

        NativeMethods.Rectangle row = item.ItemRectangle;
        row.Left += S(5);
        row.Right -= S(5);
        if ((item.ItemState & NativeMethods.OwnerDrawSelected) != 0)
        {
            FillRounded(item.DeviceContext, row, palette.AccentSoft, S(5));
        }

        GitConflictFileInfo conflict = _conflicts[index];
        uint conflictColor = _dark ? 0x005F5FF0u : 0x004C4CD3u;
        NativeMethods.Rectangle marker = row;
        marker.Left += S(8);
        marker.Right = marker.Left + S(18);
        NativeTheme.DrawConflictIcon(
            item.DeviceContext,
            (marker.Left + marker.Right) / 2,
            (marker.Top + marker.Bottom) / 2,
            conflictColor);

        string fileName = Path.GetFileName(conflict.RelativePath);
        string directory = Path.GetDirectoryName(conflict.RelativePath)?.Replace('\\', '/') ?? string.Empty;
        NativeMethods.Rectangle name = row;
        name.Left += S(31);
        name.Right -= S(150);
        DrawText(item.DeviceContext, fileName, name, palette.Text, NativeTheme.UiFont);
        NativeMethods.Rectangle detail = row;
        detail.Left = Math.Max(detail.Left, detail.Right - S(142));
        detail.Right -= S(8);
        DrawText(
            item.DeviceContext,
            directory.Length == 0 ? "内容冲突" : $"{directory}  ·  内容冲突",
            detail,
            palette.Muted,
            NativeTheme.UiFont);
        return true;
    }

    private static void FillRounded(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint color,
        int radius)
    {
        if (NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, rectangle, color, radius))
        {
            return;
        }

        nint brush = NativeMethods.CreateSolidBrush(color);
        nint region = NativeMethods.CreateRoundRectangleRegion(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right + 1,
            rectangle.Bottom + 1,
            radius,
            radius);
        if (brush != 0 && region != 0)
        {
            _ = NativeMethods.FillRegion(deviceContext, region, brush);
        }

        if (region != 0)
        {
            _ = NativeMethods.DeleteObject(region);
        }

        if (brush != 0)
        {
            _ = NativeMethods.DeleteObject(brush);
        }
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
        NativeComboBoxTheme.Unregister(_kindCombo);
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

    private string DialogTitle => _session is { HasConflicts: true } session
        ? $"{OperationName(session.Kind)} 冲突"
        : UiText.GitOperationManagement;

    private bool IsConflictSession => _session is { HasConflicts: true };

    private static bool ShouldKeepContinueVisible(GitOperationSession? session)
    {
        return session is
        {
            IsInProgress: true,
            HasConflicts: true,
            Kind: GitOperationKind.Merge
                or GitOperationKind.Rebase
                or GitOperationKind.CherryPick
                or GitOperationKind.Revert,
        };
    }

    private static string ConflictSessionContext(GitOperationSession session)
    {
        if (session.CurrentStep is int currentStep && session.TotalSteps is int totalSteps)
        {
            return $"当前步骤 {currentStep}/{totalSteps}";
        }

        string branch = session.CurrentBranch ?? "Detached HEAD / 无提交";
        return $"{OperationName(session.Kind)} 进行中  ·  当前分支 {branch}";
    }

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
        nint font)
    {
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        nint previous = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextEndEllipsis);
        if (previous != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previous);
        }
    }

    private static int S(int pixels) => NativeTheme.Scale(pixels);
}
