using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeGitPanel : IDisposable
{
    private const string WindowClassName = "Augit.GitPanel.Native";
    private const int ChangesListIdentifier = 1;
    private const int CommandRefresh = 10;
    private const int CommandInitialize = 11;
    private const int CommandFetch = 12;
    private const int CommandPull = 13;
    private const int CommandPush = 14;
    private const int CommandRemotes = 15;
    private const int CommandCancelOperation = 16;
    private const int CommandToggleSelection = 17;
    private const int CommandSelectChanges = 18;
    private const int CommandSelectUnversioned = 19;
    private const int CommandUnified = 20;
    private const int CommandSideBySide = 21;
    private const int CommandIgnoreWhitespace = 22;
    private const int CommandCopyDiffCommand = 23;
    private const int CommandAmend = 24;
    private const int CommandCommit = 25;
    private const int CommandCommitAndPush = 26;
    private const int CommandPreviousChange = 27;
    private const int CommandNextChange = 28;
    private const int CommandModifiedPreview = 29;
    private const int CommandRollback = 30;
    private const int CommandAdvancedOperations = 31;
    private const uint WindowMessageRefresh = NativeMethods.WindowMessageApp + 20;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitPanel> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly string _workspaceRoot;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly Action<string> _openModifiedPreview;
    private readonly List<ChangeListEntry> _entries = [];
    private readonly GitFileSelection _selection = new();
    private readonly List<int> _changeLines = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private GitRuntimeInfo? _runtime;
    private GitRepositorySnapshot? _repository;
    private GitStatusSnapshot? _status;
    private GitRepositoryService? _repositoryService;
    private GitStatusService? _statusService;
    private GitDiffService? _diffService;
    private GitCommitService? _commitService;
    private GitRemoteService? _remoteService;
    private GitWorkspaceStateService? _workspaceStateService;
    private GitOperationService? _advancedOperationService;
    private GitConflictService? _conflictService;
    private GitMetadataWatcher? _metadataWatcher;
    private GitDiffDocument? _activeDiff;
    private GitChangedFile? _activeChangedFile;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _diffCancellation;
    private ScintillaControl? _unifiedDiff;
    private ScintillaControl? _oldDiff;
    private ScintillaControl? _newDiff;
    private nint _repositoryLabel;
    private nint _initializeButton;
    private nint _refreshButton;
    private nint _fetchButton;
    private nint _pullButton;
    private nint _pullModeCombo;
    private nint _pushButton;
    private nint _remotesButton;
    private nint _advancedOperationsButton;
    private nint _cancelButton;
    private nint _toggleSelectionButton;
    private nint _selectChangesButton;
    private nint _selectUnversionedButton;
    private nint _changesList;
    private nint _unifiedButton;
    private nint _sideBySideButton;
    private nint _ignoreWhitespaceButton;
    private nint _copyCommandButton;
    private nint _previousChangeButton;
    private nint _nextChangeButton;
    private nint _modifiedPreviewButton;
    private nint _rollbackButton;
    private nint _commitLabel;
    private nint _commitEdit;
    private nint _amendButton;
    private nint _commitButton;
    private nint _commitAndPushButton;
    private bool _sideBySide;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _operationRunning;
    private int _diffVersion;
    private int _renderVersion;
    private int _changeNavigationIndex = -1;
    private string _unifiedRenderedText = string.Empty;
    private string _oldRenderedText = string.Empty;
    private string _newRenderedText = string.Empty;
    private bool _disposed;

    internal NativeGitPanel(
        nint parent,
        string workspaceRoot,
        ApplicationSettings settings,
        Action<string> setStatus,
        Action<string> openModifiedPreview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(openModifiedPreview);
        _workspaceRoot = workspaceRoot;
        _settings = settings;
        _setStatus = setStatus;
        _openModifiedPreview = openModifiedPreview;
        EnsureWindowClass();
        Handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitPanelCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(Handle, this);
        }

        CreateControls();
        ApplyAppearance();
        Layout();
        ShowDiffNotice(UiText.GitRefreshing);
        _ = InitializeAsync();
    }

    internal nint Handle { get; private set; }

    internal int ChangedFileCount => _status?.Files.Count ?? 0;

    internal bool IsRuntimeAvailable => _runtime?.IsAvailable == true;

    internal GitRepositoryKind? RepositoryKind => _repository?.Kind;

    internal bool RollbackButtonCreatedForTest => _rollbackButton != 0;

    internal bool AdvancedOperationsButtonCreatedForTest => _advancedOperationsButton != 0;

    internal async Task<bool> SelectFileForTestAsync(string relativePath)
    {
        int index = _entries.FindIndex(
            entry => entry.File?.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true);
        if (index < 0)
        {
            return false;
        }

        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        await ShowSelectedDiffAsync();
        return _activeChangedFile?.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true;
    }

    internal Task RollbackSelectedForTestAsync()
    {
        return RollbackSelectedAsync(requireConfirmation: false);
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

    internal void RequestRefresh()
    {
        if (_disposed)
        {
            return;
        }

        _refreshPending = true;
        _ = RefreshStatusAsync();
    }

    internal void ShowFind()
    {
        string? query = NativeTextPrompt.Show(
            NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot),
            UiText.Find,
            UiText.DiffSearchPrompt);
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        if (!_sideBySide)
        {
            int index = _unifiedRenderedText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                _unifiedDiff?.SelectUtf8Range(index, index + query.Length, _unifiedRenderedText);
                _setStatus(UiText.FindLocated);
                return;
            }
        }
        else
        {
            int oldIndex = _oldRenderedText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (oldIndex >= 0)
            {
                _oldDiff?.SelectUtf8Range(oldIndex, oldIndex + query.Length, _oldRenderedText);
                _setStatus(UiText.FindLocated);
                return;
            }

            int newIndex = _newRenderedText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (newIndex >= 0)
            {
                _newDiff?.SelectUtf8Range(newIndex, newIndex + query.Length, _newRenderedText);
                _setStatus(UiText.FindLocated);
                return;
            }
        }

        _setStatus(UiText.FindNotFound);
    }

    internal void ApplyAppearance()
    {
        if (Handle == 0)
        {
            return;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(Handle, dark);
        foreach (nint control in Controls)
        {
            NativeTheme.ApplyToControl(control, dark);
        }

        _unifiedDiff?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        _oldDiff?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        _newDiff?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _diffCancellation?.Cancel();
        _diffCancellation?.Dispose();
        _diffCancellation = null;
        DisposeMetadataWatcher();
        _unifiedDiff?.Dispose();
        _oldDiff?.Dispose();
        _newDiff?.Dispose();
        _unifiedDiff = null;
        _oldDiff = null;
        _newDiff = null;
        _lifetimeCancellation.Dispose();
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

    private IReadOnlyList<nint> Controls =>
    [
        _repositoryLabel,
        _initializeButton,
        _refreshButton,
        _fetchButton,
        _pullButton,
        _pullModeCombo,
        _pushButton,
        _remotesButton,
        _advancedOperationsButton,
        _cancelButton,
        _toggleSelectionButton,
        _selectChangesButton,
        _selectUnversionedButton,
        _changesList,
        _unifiedButton,
        _sideBySideButton,
        _ignoreWhitespaceButton,
        _copyCommandButton,
        _previousChangeButton,
        _nextChangeButton,
        _modifiedPreviewButton,
        _rollbackButton,
        _commitLabel,
        _commitEdit,
        _amendButton,
        _commitButton,
        _commitAndPushButton,
    ];

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
                throw new Win32Exception(error, UiText.GitPanelClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeGitPanel? instance;
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
            case NativeMethods.WindowMessageSetFocus:
                _ = NativeMethods.SetFocus(instance._changesList);
                return 0;
            case WindowMessageRefresh:
                instance.RequestRefresh();
                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        _repositoryLabel = CreateControl(NativeMethods.StaticClass, UiText.GitRefreshing, 0, NativeMethods.StaticLeft);
        _initializeButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.InitializeRepository,
            CommandInitialize,
            NativeMethods.ButtonPushButton);
        _refreshButton = CreateControl(NativeMethods.ButtonClass, UiText.Refresh, CommandRefresh, NativeMethods.ButtonPushButton);
        _fetchButton = CreateControl(NativeMethods.ButtonClass, UiText.Fetch, CommandFetch, NativeMethods.ButtonPushButton);
        _pullButton = CreateControl(NativeMethods.ButtonClass, UiText.Pull, CommandPull, NativeMethods.ButtonPushButton);
        _pullModeCombo = CreateControl(
            NativeMethods.ComboBoxClass,
            string.Empty,
            30,
            NativeMethods.ComboBoxDropDownList);
        foreach (string item in new[]
        {
            UiText.PullRepositoryConfigured,
            UiText.PullMerge,
            UiText.PullRebase,
            UiText.PullFastForwardOnly,
        })
        {
            _ = NativeMethods.SendMessage(_pullModeCombo, NativeMethods.ComboBoxAddString, 0, item);
        }

        _ = NativeMethods.SendMessage(_pullModeCombo, NativeMethods.ComboBoxSetCurrentSelection, 0, 0);
        _pushButton = CreateControl(NativeMethods.ButtonClass, UiText.Push, CommandPush, NativeMethods.ButtonPushButton);
        _remotesButton = CreateControl(NativeMethods.ButtonClass, UiText.Remotes, CommandRemotes, NativeMethods.ButtonPushButton);
        _advancedOperationsButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.AdvancedGitOperations,
            CommandAdvancedOperations,
            NativeMethods.ButtonPushButton);
        _cancelButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CancelOperation,
            CommandCancelOperation,
            NativeMethods.ButtonPushButton);
        _toggleSelectionButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ToggleSelection,
            CommandToggleSelection,
            NativeMethods.ButtonPushButton);
        _selectChangesButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.SelectAllChanges,
            CommandSelectChanges,
            NativeMethods.ButtonPushButton);
        _selectUnversionedButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.SelectAllUnversioned,
            CommandSelectUnversioned,
            NativeMethods.ButtonPushButton);
        _changesList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            ChangesListIdentifier,
            NativeMethods.WindowStyleBorder
                | NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxNoIntegralHeight);
        _unifiedButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.UnifiedDiff,
            CommandUnified,
            NativeMethods.ButtonPushButton);
        _sideBySideButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.SideBySideDiff,
            CommandSideBySide,
            NativeMethods.ButtonPushButton);
        _ignoreWhitespaceButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.IgnoreWhitespace,
            CommandIgnoreWhitespace,
            NativeMethods.ButtonAutoCheckbox);
        _copyCommandButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CopyGitCommand,
            CommandCopyDiffCommand,
            NativeMethods.ButtonPushButton);
        _previousChangeButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.PreviousChange,
            CommandPreviousChange,
            NativeMethods.ButtonPushButton);
        _nextChangeButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.NextChange,
            CommandNextChange,
            NativeMethods.ButtonPushButton);
        _modifiedPreviewButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ModifiedPreview,
            CommandModifiedPreview,
            NativeMethods.ButtonPushButton);
        _rollbackButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Rollback,
            CommandRollback,
            NativeMethods.ButtonPushButton);
        _commitLabel = CreateControl(NativeMethods.StaticClass, UiText.CommitMessage, 0, NativeMethods.StaticLeft);
        _commitEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            31,
            NativeMethods.WindowStyleBorder
                | NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.EditMultiline
                | NativeMethods.EditAutoVerticalScroll
                | NativeMethods.EditWantReturn);
        _amendButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.AmendLastCommit,
            CommandAmend,
            NativeMethods.ButtonAutoCheckbox);
        _commitButton = CreateControl(NativeMethods.ButtonClass, UiText.Commit, CommandCommit, NativeMethods.ButtonPushButton);
        _commitAndPushButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CommitAndPush,
            CommandCommitAndPush,
            NativeMethods.ButtonPushButton);
        _unifiedDiff = new(Handle, 40);
        _oldDiff = new(Handle, 41);
        _newDiff = new(Handle, 42);
        _unifiedDiff.SetLineNumbersVisible(false);
        _oldDiff.SetLineNumbersVisible(false);
        _newDiff.SetLineNumbersVisible(false);
        _oldDiff.SetVisible(false);
        _newDiff.SetVisible(false);
        _ = NativeMethods.ShowWindow(_initializeButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_copyCommandButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_modifiedPreviewButton, NativeMethods.ShowHide);
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
            Handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return control;
    }

    private async Task InitializeAsync()
    {
        _runtime = await new GitExecutableLocator().ResolveAsync(
            _settings.GitExecutablePath,
            _lifetimeCancellation.Token);
        if (_disposed)
        {
            return;
        }

        if (!_runtime.IsAvailable)
        {
            _ = NativeMethods.SetWindowText(
                _repositoryLabel,
                _runtime.UnavailableReason ?? UiText.GitUnavailable);
            SetGitControlsEnabled(false);
            ShowDiffNotice(_runtime.UnavailableReason ?? UiText.GitUnavailable);
            _setStatus(_runtime.UnavailableReason ?? UiText.GitUnavailable);
            return;
        }

        _repositoryService = new(_runtime);
        GitRepositoryOperationResult inspected = await _repositoryService.InspectAsync(
            _workspaceRoot,
            _lifetimeCancellation.Token);
        if (!inspected.IsSuccess || inspected.Repository is null)
        {
            ShowOperationError(inspected.ErrorMessage ?? UiText.GitUnavailable);
            return;
        }

        ConfigureRepository(inspected.Repository);
        RequestRefresh();
    }

    private void ConfigureRepository(GitRepositorySnapshot repository)
    {
        DisposeMetadataWatcher();
        _repository = repository;
        _status = null;
        _selection.Reconcile([]);
        PopulateChanges([]);
        if (repository.Kind == GitRepositoryKind.PlainDirectory)
        {
            _ = NativeMethods.SetWindowText(_repositoryLabel, UiText.PlainDirectoryGitStatus);
            _ = NativeMethods.ShowWindow(_initializeButton, NativeMethods.ShowNormal);
            SetGitControlsEnabled(false, allowInitialize: true);
            ShowDiffNotice(UiText.PlainDirectoryGitNotice);
            return;
        }

        if (repository.Kind == GitRepositoryKind.BareRepository)
        {
            _ = NativeMethods.SetWindowText(_repositoryLabel, UiText.BareRepositoryGitStatus);
            _ = NativeMethods.ShowWindow(_initializeButton, NativeMethods.ShowHide);
            SetGitControlsEnabled(false);
            ShowDiffNotice(UiText.BareRepositoryGitNotice);
            return;
        }

        _ = NativeMethods.ShowWindow(_initializeButton, NativeMethods.ShowHide);
        _statusService = new(_runtime!);
        _diffService = new(_runtime!);
        _commitService = new(_runtime!);
        _remoteService = new(_runtime!);
        _workspaceStateService = new(_runtime!);
        _advancedOperationService = new(_runtime!);
        _conflictService = new(_runtime!, _advancedOperationService);
        _metadataWatcher = new(repository);
        _metadataWatcher.Changed += OnGitMetadataChanged;
        SetGitControlsEnabled(true);
    }

    private async Task RefreshStatusAsync()
    {
        if (_disposed || _refreshing || _operationRunning || _statusService is null || _repository is null)
        {
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshPending = false;
                _setStatus(UiText.GitRefreshing);
                GitStatusResult result = await _statusService.ReadAsync(
                    _repository,
                    _lifetimeCancellation.Token);
                if (_disposed)
                {
                    return;
                }

                if (!result.IsSuccess || result.Snapshot is null)
                {
                    ShowOperationError(result.ErrorMessage ?? UiText.GitUnavailable);
                    return;
                }

                _status = result.Snapshot;
                _selection.Reconcile(_status.Files);
                PopulateChanges(_status.Files);
                _ = NativeMethods.SetWindowText(
                    _repositoryLabel,
                    UiText.GitBranchStatus(
                        _status.CurrentBranch,
                        _status.IsDetached,
                        _status.Changes.Count,
                        _status.UnversionedFiles.Count));
                if (_status.Files.Count == 0)
                {
                    _activeChangedFile = null;
                    _activeDiff = null;
                    ShowDiffNotice(UiText.NoGitChanges);
                }

                _setStatus(UiText.GitReady);
            }
            while (_refreshPending && !_operationRunning);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void PopulateChanges(IReadOnlyList<GitChangedFile> files)
    {
        int selectedIndex = GetSelectedEntryIndex();
        string? selectedPath = selectedIndex >= 0 && selectedIndex < _entries.Count
            ? _entries[selectedIndex].File?.RelativePath
            : _activeChangedFile?.RelativePath;
        _entries.Clear();
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.ListBoxResetContent, 0, 0);
        AddGroup(GitChangeGroup.Changes, UiText.Changes, files);
        AddGroup(GitChangeGroup.UnversionedFiles, UiText.UnversionedFiles, files);
        if (selectedPath is not null)
        {
            int restoredIndex = _entries.FindIndex(
                entry => entry.File?.RelativePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase) == true);
            if (restoredIndex >= 0)
            {
                _ = NativeMethods.SendMessage(
                    _changesList,
                    NativeMethods.ListBoxSetCurrentSelection,
                    unchecked((nuint)restoredIndex),
                    0);
            }
        }
    }

    private void AddGroup(
        GitChangeGroup group,
        string title,
        IReadOnlyList<GitChangedFile> files)
    {
        GitChangedFile[] groupFiles = files.Where(file => file.Group == group).ToArray();
        AddEntry(new(group, null), UiText.GitGroup(title, groupFiles.Length));
        foreach (GitChangedFile file in groupFiles)
        {
            string check = _selection.IsSelected(file.RelativePath) ? "☑" : "☐";
            string staged = file.HasStagedChanges && file.HasWorkingTreeChanges
                ? UiText.StagedAndModified
                : file.HasStagedChanges ? UiText.Staged : string.Empty;
            string rename = file.OriginalRelativePath is null ? string.Empty : $" ← {file.OriginalRelativePath}";
            AddEntry(new(group, file), $"  {check} {StatusSymbol(file.Kind)}  {file.RelativePath}{rename}{staged}");
        }
    }

    private void AddEntry(ChangeListEntry entry, string displayText)
    {
        _entries.Add(entry);
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.ListBoxAddString, 0, displayText);
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == ChangesListIdentifier)
        {
            if (notification == NativeMethods.ListBoxNotificationSelectionChanged)
            {
                _ = ShowSelectedDiffAsync();
            }
            else if (notification == NativeMethods.ListBoxNotificationDoubleClick)
            {
                ToggleSelectedEntry();
            }

            return;
        }

        switch (command)
        {
            case CommandRefresh:
                RequestRefresh();
                break;
            case CommandInitialize:
                _ = InitializeRepositoryAsync();
                break;
            case CommandFetch:
                _ = RunRemoteOperationAsync(
                    service => service.FetchAsync(_repository!, cancellationToken: CurrentOperationToken),
                    UiText.FetchCompleted);
                break;
            case CommandPull:
                _ = RunRemoteOperationAsync(
                    service => service.PullAsync(_repository!, GetPullMode(), CurrentOperationToken),
                    UiText.PullCompleted);
                break;
            case CommandPush:
                _ = RunRemoteOperationAsync(
                    service => service.PushAsync(_repository!, cancellationToken: CurrentOperationToken),
                    UiText.PushCompleted);
                break;
            case CommandRemotes:
                ShowRemotes();
                break;
            case CommandAdvancedOperations:
                ShowAdvancedOperations();
                break;
            case CommandCancelOperation:
                _operationCancellation?.Cancel();
                break;
            case CommandToggleSelection:
                ToggleSelectedEntry();
                break;
            case CommandSelectChanges:
                ToggleGroup(GitChangeGroup.Changes);
                break;
            case CommandSelectUnversioned:
                ToggleGroup(GitChangeGroup.UnversionedFiles);
                break;
            case CommandUnified:
                _sideBySide = false;
                RenderActiveDiff();
                break;
            case CommandSideBySide:
                _sideBySide = true;
                RenderActiveDiff();
                break;
            case CommandIgnoreWhitespace:
                _ = ShowSelectedDiffAsync();
                break;
            case CommandCopyDiffCommand:
                CopyActiveDiffCommand();
                break;
            case CommandPreviousChange:
                NavigateChange(-1);
                break;
            case CommandNextChange:
                NavigateChange(1);
                break;
            case CommandModifiedPreview:
                OpenModifiedPreview();
                break;
            case CommandRollback:
                _ = RollbackSelectedAsync();
                break;
            case CommandCommit:
                _ = CommitAsync(pushAfterCommit: false);
                break;
            case CommandCommitAndPush:
                _ = CommitAsync(pushAfterCommit: true);
                break;
        }
    }

    private async Task InitializeRepositoryAsync()
    {
        if (_repositoryService is null || _repository is null || _operationRunning)
        {
            return;
        }

        BeginOperation(UiText.InitializingRepository);
        try
        {
            GitRepositoryOperationResult result = await _repositoryService.InitializeAsync(
                _workspaceRoot,
                CurrentOperationToken);
            if (result.IsSuccess && result.Repository is not null)
            {
                ConfigureRepository(result.Repository);
                _setStatus(UiText.RepositoryInitialized);
                RequestRefresh();
            }
            else
            {
                ShowOperationError(result.ErrorMessage ?? UiText.InitializeRepositoryFailed);
            }
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ShowSelectedDiffAsync()
    {
        int selectedIndex = GetSelectedEntryIndex();
        if (selectedIndex < 0 || selectedIndex >= _entries.Count || _entries[selectedIndex].File is null)
        {
            _activeChangedFile = null;
            _ = NativeMethods.ShowWindow(_modifiedPreviewButton, NativeMethods.ShowHide);
            return;
        }

        GitChangedFile file = _entries[selectedIndex].File!;
        _activeChangedFile = file;
        string extension = Path.GetExtension(file.RelativePath);
        bool supportsModifiedPreview = extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
        if (supportsModifiedPreview && _repository?.RepositoryRoot is not null)
        {
            string previewPath = Path.GetFullPath(Path.Combine(
                _repository.RepositoryRoot,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            supportsModifiedPreview = File.Exists(previewPath);
        }
        _ = NativeMethods.ShowWindow(
            _modifiedPreviewButton,
            supportsModifiedPreview ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        if (_diffService is null || _repository is null)
        {
            return;
        }

        _diffCancellation?.Cancel();
        _diffCancellation?.Dispose();
        _diffCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        int version = ++_diffVersion;
        ShowDiffNotice(UiText.GeneratingDiff);
        GitDiffResult result = await _diffService.CreateAsync(
            _repository,
            file,
            new(IsChecked(_ignoreWhitespaceButton)),
            _diffCancellation.Token);
        if (_disposed || version != _diffVersion)
        {
            return;
        }

        if (!result.IsSuccess || result.Document is null)
        {
            _activeDiff = null;
            ShowDiffNotice(result.ErrorMessage ?? UiText.GenerateDiffFailed);
            return;
        }

        _activeDiff = result.Document;
        await RenderActiveDiffAsync(++_renderVersion);
    }

    private void RenderActiveDiff()
    {
        _ = RenderActiveDiffAsync(++_renderVersion);
    }

    private async Task RenderActiveDiffAsync(int renderVersion)
    {
        GitDiffDocument? document = _activeDiff;
        if (document is null)
        {
            ShowDiffNotice(UiText.SelectGitFile);
            return;
        }

        _ = NativeMethods.ShowWindow(
            _copyCommandButton,
            document.CopyableCommand is null ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
        if (document.Status != GitDiffContentStatus.Ready)
        {
            string notice = document.Status switch
            {
                GitDiffContentStatus.Binary => UiText.BinaryDiffSummary,
                GitDiffContentStatus.SideTooLarge => UiText.DiffSideTooLarge,
                GitDiffContentStatus.OutputTooLarge => UiText.DiffOutputTooLarge,
                _ => UiText.NoTextDiff,
            };
            ShowDiffNotice(
                UiText.GitDiffSizes(notice, FormatSize(document.OldSize), FormatSize(document.NewSize)),
                clearActiveDiff: false,
                hideCopyCommand: false);
            return;
        }

        if (document.UnifiedPatch is null)
        {
            _ = ShowSelectedDiffAsync();
            return;
        }

        string patch = document.UnifiedPatch;
        if (patch.Length == 0)
        {
            ShowDiffNotice(UiText.NoTextDiff);
            return;
        }

        bool renderSideBySide = _sideBySide;
        try
        {
            if (!renderSideBySide)
            {
                var rendered = await Task.Run(
                    () => BuildUnified(GitUnifiedDiffParser.Parse(patch)),
                    _lifetimeCancellation.Token);
                if (!CanApplyRender(renderVersion, document, renderSideBySide))
                {
                    return;
                }

                _oldDiff!.SetVisible(false);
                _newDiff!.SetVisible(false);
                _unifiedDiff!.SetVisible(true);
                _unifiedRenderedText = rendered.Text;
                _oldRenderedText = string.Empty;
                _newRenderedText = string.Empty;
                SetChangeLines(rendered.ChangedLines);
                _unifiedDiff.SetTextContent(rendered.Text);
                _unifiedDiff.SetHighlights(0, rendered.Text, rendered.RemovedHighlights, 219, 88, 96);
                _unifiedDiff.SetHighlights(1, rendered.Text, rendered.AddedHighlights, 76, 175, 80);
                _activeDiff = document with { UnifiedPatch = null };
                return;
            }

            var sideRendered = await Task.Run(
                () =>
                {
                    IReadOnlyList<GitSideBySideRow> rows = GitUnifiedDiffParser.ToSideBySide(
                        GitUnifiedDiffParser.Parse(patch));
                    return (
                        Old: BuildSide(rows, oldSide: true),
                        New: BuildSide(rows, oldSide: false),
                        ChangedLines: rows
                            .Select((row, index) => (row, index))
                            .Where(item => item.row.Kind is GitDiffLineKind.Added
                                or GitDiffLineKind.Removed
                                or GitDiffLineKind.Modified)
                            .Select(item => item.index + 1)
                            .ToArray());
                },
                _lifetimeCancellation.Token);
            if (!CanApplyRender(renderVersion, document, renderSideBySide))
            {
                return;
            }

            _unifiedRenderedText = string.Empty;
            _oldRenderedText = sideRendered.Old.Text;
            _newRenderedText = sideRendered.New.Text;
            SetChangeLines(sideRendered.ChangedLines);
            _unifiedDiff!.SetVisible(false);
            _oldDiff!.SetVisible(true);
            _newDiff!.SetVisible(true);
            _oldDiff.SetTextContent(sideRendered.Old.Text);
            _newDiff.SetTextContent(sideRendered.New.Text);
            _oldDiff.SetHighlights(0, sideRendered.Old.Text, sideRendered.Old.Highlights, 219, 88, 96);
            _newDiff.SetHighlights(0, sideRendered.New.Text, sideRendered.New.Highlights, 76, 175, 80);
            _activeDiff = document with { UnifiedPatch = null };
            Layout();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool CanApplyRender(int renderVersion, GitDiffDocument document, bool renderedSideBySide)
    {
        return !_disposed
            && renderVersion == _renderVersion
            && ReferenceEquals(_activeDiff, document)
            && _sideBySide == renderedSideBySide;
    }

    private static (
        string Text,
        List<(int Start, int Length)> RemovedHighlights,
        List<(int Start, int Length)> AddedHighlights,
        List<int> ChangedLines) BuildUnified(IReadOnlyList<GitDiffLine> lines)
    {
        StringBuilder builder = new();
        List<(int Start, int Length)> removed = [];
        List<(int Start, int Length)> added = [];
        List<int> changedLines = [];
        for (int index = 0; index < lines.Count; index++)
        {
            GitDiffLine line = lines[index];
            int lineStart = builder.Length;
            string oldNumber = line.OldLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            string newNumber = line.NewLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            char marker = line.Kind switch
            {
                GitDiffLineKind.Removed => '-',
                GitDiffLineKind.Added => '+',
                GitDiffLineKind.Context => ' ',
                _ => ' ',
            };
            if (line.Kind is GitDiffLineKind.Metadata or GitDiffLineKind.HunkHeader or GitDiffLineKind.NoNewlineMarker)
            {
                builder.Append("               ").Append(line.Text);
            }
            else
            {
                builder.Append(oldNumber.PadLeft(6))
                    .Append(' ')
                    .Append(newNumber.PadLeft(6))
                    .Append(' ')
                    .Append(marker)
                    .Append(' ')
                    .Append(line.Text);
            }

            int lineLength = builder.Length - lineStart;
            if (line.Kind == GitDiffLineKind.Removed)
            {
                removed.Add((lineStart, lineLength));
                changedLines.Add(index + 1);
            }
            else if (line.Kind == GitDiffLineKind.Added)
            {
                added.Add((lineStart, lineLength));
                changedLines.Add(index + 1);
            }

            builder.AppendLine();
        }

        return (builder.ToString(), removed, added, changedLines);
    }

    private static (string Text, List<(int Start, int Length)> Highlights) BuildSide(
        IReadOnlyList<GitSideBySideRow> rows,
        bool oldSide)
    {
        StringBuilder builder = new();
        List<(int Start, int Length)> highlights = [];
        foreach (GitSideBySideRow row in rows)
        {
            string? text = oldSide ? row.OldText : row.NewText;
            IReadOnlyList<GitTextSpan> changes = oldSide ? row.OldChanges : row.NewChanges;
            int? lineNumber = oldSide ? row.OldLineNumber : row.NewLineNumber;
            string number = lineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            builder.Append(number.PadLeft(6)).Append("  ");
            int lineStart = builder.Length;
            if (text is not null)
            {
                builder.Append(text);
                foreach (GitTextSpan span in changes)
                {
                    highlights.Add((lineStart + span.Start, span.Length));
                }
            }

            builder.AppendLine();
        }

        return (builder.ToString(), highlights);
    }

    private void ShowDiffNotice(
        string notice,
        bool clearActiveDiff = true,
        bool hideCopyCommand = true)
    {
        if (clearActiveDiff)
        {
            _activeDiff = null;
        }

        _renderVersion++;

        _unifiedRenderedText = notice;
        _oldRenderedText = string.Empty;
        _newRenderedText = string.Empty;
        SetChangeLines([]);

        _oldDiff?.SetVisible(false);
        _newDiff?.SetVisible(false);
        _unifiedDiff?.SetVisible(true);
        _unifiedDiff?.SetTextContent(notice);
        if (hideCopyCommand)
        {
            _ = NativeMethods.ShowWindow(_copyCommandButton, NativeMethods.ShowHide);
        }

        Layout();
    }

    private void ToggleSelectedEntry()
    {
        int index = GetSelectedEntryIndex();
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        ChangeListEntry entry = _entries[index];
        if (entry.File is null)
        {
            ToggleGroup(entry.Group);
            return;
        }

        bool selected = !_selection.IsSelected(entry.File.RelativePath);
        _selection.SetSelected(entry.File.RelativePath, selected);
        PopulateChanges(_status?.Files ?? []);
        RestoreListSelection(entry.File.RelativePath);
    }

    private void ToggleGroup(GitChangeGroup group)
    {
        GitChangedFile[] files = (_status?.Files ?? [])
            .Where(file => file.Group == group)
            .ToArray();
        bool select = files.Any(file => !_selection.IsSelected(file.RelativePath));
        _selection.SetGroupSelected(files, select);
        PopulateChanges(_status?.Files ?? []);
    }

    private void RestoreListSelection(string relativePath)
    {
        int index = _entries.FindIndex(
            entry => entry.File?.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true);
        if (index >= 0)
        {
            _ = NativeMethods.SendMessage(
                _changesList,
                NativeMethods.ListBoxSetCurrentSelection,
                unchecked((nuint)index),
                0);
        }
    }

    private async Task CommitAsync(bool pushAfterCommit)
    {
        if (_commitService is null || _repository is null || _operationRunning)
        {
            return;
        }

        BeginOperation(pushAfterCommit ? UiText.CommittingAndPushing : UiText.Committing);
        try
        {
            GitCommitRequest request = new(
                _selection.SelectedPaths.ToArray(),
                NativeMethods.GetWindowTextValue(_commitEdit),
                IsChecked(_amendButton));
            GitCommitResult commit = await _commitService.CommitAsync(
                _repository,
                request,
                CurrentOperationToken);
            if (!commit.IsSuccess)
            {
                ShowOperationError(commit.ErrorMessage ?? UiText.CommitFailed);
                return;
            }

            _selection.Reconcile(commit.ActualStatus?.Files ?? []);
            if (!pushAfterCommit)
            {
                _ = NativeMethods.SetWindowText(_commitEdit, string.Empty);
                _setStatus(UiText.CommitCompleted);
                _refreshPending = true;
                return;
            }

            GitRemoteOperationResult push = await _remoteService!.PushAsync(
                _repository,
                cancellationToken: CurrentOperationToken);
            if (!push.IsSuccess)
            {
                ShowOperationError(UiText.CommitCompletedPushFailed(push.ErrorMessage));
                _refreshPending = true;
                return;
            }

            _ = NativeMethods.SetWindowText(_commitEdit, string.Empty);
            _setStatus(UiText.CommitAndPushCompleted);
            _refreshPending = true;
        }
        finally
        {
            EndOperation();
            RequestRefresh();
        }
    }

    private async Task RunRemoteOperationAsync(
        Func<GitRemoteService, Task<GitRemoteOperationResult>> operation,
        string successMessage)
    {
        if (_remoteService is null || _repository is null || _operationRunning)
        {
            return;
        }

        BeginOperation(UiText.GitRemoteOperationRunning);
        try
        {
            GitRemoteOperationResult result = await operation(_remoteService);
            if (result.IsSuccess)
            {
                _setStatus(successMessage);
            }
            else
            {
                ShowOperationError(result.ErrorMessage ?? UiText.GitRemoteOperationFailed);
            }
        }
        finally
        {
            EndOperation();
            RequestRefresh();
        }
    }

    private void ShowRemotes()
    {
        if (_runtime is null || _repository is null || _remoteService is null || _operationRunning)
        {
            return;
        }

        nint owner = NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot);
        NativeRemoteDialog.Show(
            owner,
            _repository,
            _remoteService,
            _status?.CurrentBranch,
            _settings,
            _setStatus);
        RequestRefresh();
    }

    private void ShowAdvancedOperations()
    {
        if (_repository is null
            || _advancedOperationService is null
            || _conflictService is null
            || _operationRunning)
        {
            return;
        }

        nint owner = NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot);
        bool changed = NativeGitOperationDialog.Show(
            owner,
            _repository,
            _advancedOperationService,
            _conflictService,
            _settings,
            _setStatus);
        if (changed)
        {
            _refreshPending = true;
        }

        RequestRefresh();
    }

    private void CopyActiveDiffCommand()
    {
        if (_activeDiff?.CopyableCommand is not { } command)
        {
            return;
        }

        _setStatus(NativeClipboard.TrySetText(Handle, command) ? UiText.PathCopied : UiText.ClipboardUnavailable);
    }

    private void OpenModifiedPreview()
    {
        if (_activeChangedFile is null || _repository?.RepositoryRoot is null)
        {
            return;
        }

        string path = Path.GetFullPath(Path.Combine(
            _repository.RepositoryRoot,
            _activeChangedFile.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(path))
        {
            _openModifiedPreview(path);
        }
    }

    private async Task RollbackSelectedAsync(bool requireConfirmation = true)
    {
        int selectedIndex = GetSelectedEntryIndex();
        if (selectedIndex < 0
            || selectedIndex >= _entries.Count
            || _entries[selectedIndex].File is not { } file
            || _workspaceStateService is null
            || _repository is null
            || _operationRunning)
        {
            ShowOperationError(UiText.SelectRollbackFile);
            return;
        }

        if (file.Kind == GitChangeKind.Unmerged)
        {
            ShowOperationError(UiText.RollbackConflictUnavailable);
            return;
        }

        await ShowSelectedDiffAsync();
        if (_disposed
            || _activeChangedFile?.RelativePath.Equals(file.RelativePath, StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        GitDiffDocument? diff = _activeDiff;
        bool recycle = file.Group == GitChangeGroup.UnversionedFiles
            || file.Kind is GitChangeKind.Added or GitChangeKind.Copied;
        string warning = UiText.ConfirmRollbackDetails(
            file.RelativePath,
            FormatSize(diff?.OldSize ?? 0),
            FormatSize(diff?.NewSize ?? 0),
            recycle);
        if (requireConfirmation
            && NativeMethods.MessageBox(
                Handle,
                warning,
                UiText.AppName,
                NativeMethods.MessageBoxOkCancel | NativeMethods.MessageBoxIconWarning) != NativeMethods.DialogResultOk)
        {
            return;
        }

        BeginOperation(UiText.RollingBack);
        try
        {
            GitActionResult result = await _workspaceStateService.RollbackAsync(
                _repository,
                file,
                CurrentOperationToken);
            if (!result.IsSuccess)
            {
                ShowOperationError(result.ErrorMessage ?? UiText.GitUnavailable);
                return;
            }

            _selection.Reconcile(result.ActualStatus?.Files ?? []);
            _setStatus(UiText.RollbackCompleted);
            ShowDiffNotice(UiText.RollbackCompleted);
        }
        finally
        {
            EndOperation();
            RequestRefresh();
        }
    }

    private void SetChangeLines(IEnumerable<int> lines)
    {
        _changeLines.Clear();
        _changeLines.AddRange(lines.Distinct().Order());
        _changeNavigationIndex = -1;
    }

    private void NavigateChange(int direction)
    {
        if (_changeLines.Count == 0)
        {
            return;
        }

        _changeNavigationIndex = direction > 0
            ? (_changeNavigationIndex + 1 + _changeLines.Count) % _changeLines.Count
            : (_changeNavigationIndex - 1 + _changeLines.Count) % _changeLines.Count;
        int line = _changeLines[_changeNavigationIndex];
        if (_sideBySide)
        {
            _oldDiff?.GoToLine(line);
            _newDiff?.GoToLine(line);
        }
        else
        {
            _unifiedDiff?.GoToLine(line);
        }
    }

    private void BeginOperation(string status)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _operationRunning = true;
        SetOperationControlsEnabled(false);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowNormal);
        _setStatus(status);
    }

    private void EndOperation()
    {
        _operationRunning = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
        bool workingTree = _repository?.Kind == GitRepositoryKind.WorkingTree;
        SetOperationControlsEnabled(workingTree);
        _ = NativeMethods.EnableWindow(
            _initializeButton,
            _repository?.Kind == GitRepositoryKind.PlainDirectory);
        _ = NativeMethods.EnableWindow(_refreshButton, _repository is not null);
    }

    private CancellationToken CurrentOperationToken => _operationCancellation?.Token ?? CancellationToken.None;

    private void SetGitControlsEnabled(bool enabled, bool allowInitialize = false)
    {
        SetOperationControlsEnabled(enabled);
        _ = NativeMethods.EnableWindow(_initializeButton, allowInitialize);
        _ = NativeMethods.EnableWindow(_refreshButton, enabled || allowInitialize);
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _fetchButton,
            _initializeButton,
            _refreshButton,
            _pullButton,
            _pullModeCombo,
            _pushButton,
            _remotesButton,
            _advancedOperationsButton,
            _toggleSelectionButton,
            _selectChangesButton,
            _selectUnversionedButton,
            _changesList,
            _commitEdit,
            _amendButton,
            _commitButton,
            _commitAndPushButton,
            _unifiedButton,
            _sideBySideButton,
            _ignoreWhitespaceButton,
            _copyCommandButton,
            _previousChangeButton,
            _nextChangeButton,
            _modifiedPreviewButton,
            _rollbackButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }
    }

    private GitPullMode GetPullMode()
    {
        int selection = checked((int)NativeMethods.SendMessage(
            _pullModeCombo,
            NativeMethods.ComboBoxGetCurrentSelection,
            0,
            0));
        return selection switch
        {
            1 => GitPullMode.Merge,
            2 => GitPullMode.Rebase,
            3 => GitPullMode.FastForwardOnly,
            _ => GitPullMode.RepositoryConfigured,
        };
    }

    private int GetSelectedEntryIndex()
    {
        return checked((int)NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxGetCurrentSelection,
            0,
            0));
    }

    private void OnGitMetadataChanged(object? sender, EventArgs eventArgs)
    {
        if (!_disposed && Handle != 0)
        {
            _ = NativeMethods.PostMessage(Handle, WindowMessageRefresh, 0, 0);
        }
    }

    private void DisposeMetadataWatcher()
    {
        if (_metadataWatcher is null)
        {
            return;
        }

        _metadataWatcher.Changed -= OnGitMetadataChanged;
        _metadataWatcher.Dispose();
        _metadataWatcher = null;
    }

    private void ShowOperationError(string message)
    {
        _setStatus(message);
        _ = NativeMethods.MessageBox(Handle, message, UiText.AppName, NativeMethods.MessageBoxIconWarning);
    }

    private void Layout()
    {
        if (Handle == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        const int LeftWidth = 360;
        int leftWidth = Math.Min(LeftWidth, Math.Max(280, width / 2));
        Move(_initializeButton, 8, 7, 104, 28);
        Move(_refreshButton, 118, 7, 58, 28);
        Move(_fetchButton, 182, 7, 58, 28);
        Move(_pullButton, 246, 7, 52, 28);
        Move(_pullModeCombo, 304, 7, 128, 180);
        Move(_pushButton, 438, 7, 54, 28);
        Move(_remotesButton, 498, 7, 70, 28);
        Move(_advancedOperationsButton, 574, 7, 88, 28);
        Move(_cancelButton, 668, 7, 86, 28);
        Move(_repositoryLabel, 8, 43, Math.Max(0, leftWidth - 16), 24);
        Move(_toggleSelectionButton, 8, 70, 100, 26);
        Move(_selectChangesButton, 112, 70, 112, 26);
        Move(_selectUnversionedButton, 228, 70, 124, 26);
        int commitTop = Math.Max(250, height - 190);
        Move(_changesList, 8, 102, Math.Max(0, leftWidth - 16), Math.Max(0, commitTop - 110));
        Move(_commitLabel, 8, commitTop + 6, Math.Max(0, leftWidth - 16), 22);
        Move(_commitEdit, 8, commitTop + 30, Math.Max(0, leftWidth - 16), 90);
        Move(_amendButton, 8, commitTop + 126, 142, 24);
        Move(_commitButton, 8, commitTop + 154, 92, 28);
        Move(_commitAndPushButton, 106, commitTop + 154, 134, 28);

        int diffLeft = leftWidth + 8;
        int diffWidth = Math.Max(0, width - diffLeft - 8);
        Move(_unifiedButton, diffLeft, 43, 58, 26);
        Move(_sideBySideButton, diffLeft + 64, 43, 58, 26);
        Move(_ignoreWhitespaceButton, diffLeft + 128, 43, 96, 26);
        Move(_copyCommandButton, diffLeft + 230, 43, 110, 26);
        Move(_modifiedPreviewButton, diffLeft, 72, 104, 26);
        Move(_previousChangeButton, diffLeft + 110, 72, 64, 26);
        Move(_nextChangeButton, diffLeft + 180, 72, 64, 26);
        Move(_rollbackButton, diffLeft + 250, 72, 82, 26);
        int diffTop = 104;
        int diffHeight = Math.Max(0, height - diffTop - 8);
        _unifiedDiff?.SetBounds(diffLeft, diffTop, diffWidth, diffHeight);
        int halfWidth = Math.Max(0, (diffWidth - 4) / 2);
        _oldDiff?.SetBounds(diffLeft, diffTop, halfWidth, diffHeight);
        _newDiff?.SetBounds(diffLeft + halfWidth + 4, diffTop, Math.Max(0, diffWidth - halfWidth - 4), diffHeight);
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private static bool IsChecked(nint button)
    {
        return NativeMethods.SendMessage(button, NativeMethods.ButtonMessageGetCheck, 0, 0)
            == (nint)NativeMethods.ButtonChecked;
    }

    private static string StatusSymbol(GitChangeKind kind)
    {
        return kind switch
        {
            GitChangeKind.Added => "A",
            GitChangeKind.Deleted => "D",
            GitChangeKind.Renamed => "R",
            GitChangeKind.Copied => "C",
            GitChangeKind.TypeChanged => "T",
            GitChangeKind.Unmerged => "U",
            GitChangeKind.Untracked => "?",
            _ => "M",
        };
    }

    private static string FormatSize(long bytes)
    {
        return bytes < 1024
            ? $"{bytes} B"
            : bytes < 1024 * 1024
                ? $"{bytes / 1024d:F1} KB"
                : $"{bytes / (1024d * 1024d):F1} MB";
    }

    private sealed record ChangeListEntry(GitChangeGroup Group, GitChangedFile? File);
}
