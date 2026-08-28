using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeGitHistoryPanel : IDisposable
{
    private const string WindowClassName = "Augit.GitHistoryPanel.Native";
    private const int HistoryListIdentifier = 1;
    private const int FilesListIdentifier = 2;
    private const int CommandRefresh = 10;
    private const int CommandPreviousPage = 11;
    private const int CommandNextPage = 12;
    private const int CommandApplyFilter = 13;
    private const int CommandClearFilter = 14;
    private const int CommandCompare = 15;
    private const int CommandReferences = 16;
    private const int CommandLocalState = 17;
    private const int CommandWorktrees = 18;
    private const int CommandFileHistory = 19;
    private const int CommandBlame = 20;
    private const int CommandCancel = 21;
    private const uint WindowMessageRefresh = NativeMethods.WindowMessageApp + 30;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitHistoryPanel> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly string _workspaceRoot;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly List<GitHistoryEntry> _entries = [];
    private readonly List<GitCommitChangedFile> _files = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private GitRuntimeInfo? _runtime;
    private GitRepositorySnapshot? _repository;
    private GitHistoryService? _historyService;
    private GitReferenceService? _referenceService;
    private GitWorkspaceStateService? _stateService;
    private GitWorktreeService? _worktreeService;
    private GitMetadataWatcher? _metadataWatcher;
    private GitHistoryFilter _filter = new();
    private GitCommitDetails? _activeDetails;
    private CancellationTokenSource? _operationCancellation;
    private ScintillaControl? _diff;
    private nint _pageLabel;
    private nint _refreshButton;
    private nint _previousButton;
    private nint _nextButton;
    private nint _filterCombo;
    private nint _filterEdit;
    private nint _applyFilterButton;
    private nint _clearFilterButton;
    private nint _compareButton;
    private nint _referencesButton;
    private nint _stateButton;
    private nint _worktreesButton;
    private nint _cancelButton;
    private nint _historyList;
    private nint _metadataLabel;
    private nint _filesList;
    private nint _fileHistoryButton;
    private nint _blameButton;
    private int _page;
    private bool _hasPreviousPage;
    private bool _hasNextPage;
    private bool _operationRunning;
    private int _refreshVersion;
    private int _detailsVersion;
    private int _operationVersion;
    private string _displayedDiffText = string.Empty;
    private bool _disposed;

    internal NativeGitHistoryPanel(
        nint parent,
        string workspaceRoot,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setStatus);
        _workspaceRoot = workspaceRoot;
        _settings = settings;
        _setStatus = setStatus;
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.HistoryPanelCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(Handle, this);
        }

        CreateControls();
        ApplyAppearance();
        Layout();
        SetDiffText(UiText.HistoryLoading);
        _ = InitializeAsync();
    }

    internal nint Handle { get; private set; }

    internal int EntryCount => _entries.Count;

    internal bool IsRuntimeAvailable => _runtime?.IsAvailable == true;

    internal GitRepositoryKind? RepositoryKind => _repository?.Kind;

    internal int CurrentPageForTest => _page;

    internal bool HasNextPageForTest => _hasNextPage;

    internal int ChangedFileCountForTest => _files.Count;

    internal string DisplayedDiffTextForTest => _displayedDiffText;

    internal string? FileFilterForTest => _filter.FilePath;

    internal bool OperationRunningForTest => _operationRunning;

    internal bool ManagementButtonsCreatedForTest => _referencesButton != 0
        && _stateButton != 0
        && _worktreesButton != 0;

    internal async Task NextPageForTestAsync()
    {
        if (_hasNextPage)
        {
            _page++;
            await RefreshPageAsync();
        }
    }

    internal async Task SelectCommitForTestAsync(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _ = NativeMethods.SendMessage(
            _historyList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        await LoadSelectedCommitAsync();
    }

    internal async Task SelectCommitFileForTestAsync(int index)
    {
        if (index < 0 || index >= _files.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _ = NativeMethods.SendMessage(
            _filesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        await LoadSelectedFileDiffAsync();
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        if (Handle != 0)
        {
            _ = NativeMethods.MoveWindow(Handle, x, y, Math.Max(0, width), Math.Max(0, height), true);
            Layout();
        }
    }

    internal void SetVisible(bool visible)
    {
        if (Handle != 0)
        {
            _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
    }

    internal void SetFileFilter(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        _filter = new(FilePath: relativePath.Replace('\\', '/'));
        _page = 0;
        _ = NativeMethods.SendMessage(_filterCombo, NativeMethods.ComboBoxSetCurrentSelection, 6, 0);
        _ = NativeMethods.SetWindowText(_filterEdit, _filter.FilePath!);
        RequestRefresh();
    }

    internal void RequestRefresh()
    {
        if (!_disposed && _historyService is not null && _repository is not null)
        {
            _ = RefreshPageAsync();
        }
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

        _diff?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
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
        DisposeMetadataWatcher();
        _diff?.Dispose();
        _diff = null;
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
        _pageLabel,
        _refreshButton,
        _previousButton,
        _nextButton,
        _filterCombo,
        _filterEdit,
        _applyFilterButton,
        _clearFilterButton,
        _compareButton,
        _referencesButton,
        _stateButton,
        _worktreesButton,
        _cancelButton,
        _historyList,
        _metadataLabel,
        _filesList,
        _fileHistoryButton,
        _blameButton,
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
                throw new Win32Exception(error, UiText.HistoryPanelClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeGitHistoryPanel? instance;
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
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        _pageLabel = CreateControl(NativeMethods.StaticClass, UiText.HistoryLoading, 0, NativeMethods.StaticLeft);
        _refreshButton = CreateControl(NativeMethods.ButtonClass, UiText.Refresh, CommandRefresh, NativeMethods.ButtonPushButton);
        _previousButton = CreateControl(NativeMethods.ButtonClass, UiText.PreviousPage, CommandPreviousPage, NativeMethods.ButtonPushButton);
        _nextButton = CreateControl(NativeMethods.ButtonClass, UiText.NextPage, CommandNextPage, NativeMethods.ButtonPushButton);
        _filterCombo = CreateControl(
            NativeMethods.ComboBoxClass,
            string.Empty,
            30,
            NativeMethods.ComboBoxDropDownList);
        foreach (string item in new[]
        {
            UiText.FilterMessage,
            UiText.FilterHash,
            UiText.FilterAuthor,
            UiText.FilterSince,
            UiText.FilterUntil,
            UiText.FilterBranch,
            UiText.FilterFile,
        })
        {
            _ = NativeMethods.SendMessage(_filterCombo, NativeMethods.ComboBoxAddString, 0, item);
        }

        _ = NativeMethods.SendMessage(_filterCombo, NativeMethods.ComboBoxSetCurrentSelection, 0, 0);
        _filterEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            31,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll);
        _applyFilterButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ApplyFilter,
            CommandApplyFilter,
            NativeMethods.ButtonPushButton);
        _clearFilterButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ClearFilter,
            CommandClearFilter,
            NativeMethods.ButtonPushButton);
        _compareButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CompareReferences,
            CommandCompare,
            NativeMethods.ButtonPushButton);
        _referencesButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ManageReferences,
            CommandReferences,
            NativeMethods.ButtonPushButton);
        _stateButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ManageLocalState,
            CommandLocalState,
            NativeMethods.ButtonPushButton);
        _worktreesButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ManageWorktrees,
            CommandWorktrees,
            NativeMethods.ButtonPushButton);
        _cancelButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CancelOperation,
            CommandCancel,
            NativeMethods.ButtonPushButton);
        _historyList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            HistoryListIdentifier,
            NativeMethods.WindowStyleBorder
                | NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.WindowStyleHorizontalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxNoIntegralHeight);
        _metadataLabel = CreateControl(
            NativeMethods.StaticClass,
            UiText.SelectCommit,
            40,
            NativeMethods.StaticLeft);
        _filesList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            FilesListIdentifier,
            NativeMethods.WindowStyleBorder
                | NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxNoIntegralHeight);
        _fileHistoryButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.FileHistory,
            CommandFileHistory,
            NativeMethods.ButtonPushButton);
        _blameButton = CreateControl(NativeMethods.ButtonClass, UiText.Blame, CommandBlame, NativeMethods.ButtonPushButton);
        _diff = new(Handle, 50);
        _diff.SetWordWrap(false);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
        SetHistoryControlsEnabled(false);
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.HistoryControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return control;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _runtime = await new GitExecutableLocator()
                .ResolveAsync(_settings.GitExecutablePath, _lifetimeCancellation.Token);
            if (_disposed)
            {
                return;
            }

            if (!_runtime.IsAvailable)
            {
                ShowError(_runtime.UnavailableReason ?? UiText.GitUnavailable);
                return;
            }

            GitRepositoryOperationResult inspected = await new GitRepositoryService(_runtime)
                .InspectAsync(_workspaceRoot, _lifetimeCancellation.Token);
            if (_disposed)
            {
                return;
            }

            if (!inspected.IsSuccess || inspected.Repository?.Kind != GitRepositoryKind.WorkingTree)
            {
                ShowError(inspected.ErrorMessage ?? UiText.HistoryUnavailable);
                return;
            }

            _repository = inspected.Repository;
            _historyService = new(_runtime);
            _referenceService = new(_runtime);
            _stateService = new(_runtime);
            _worktreeService = new(_runtime);
            _metadataWatcher = new(_repository);
            _metadataWatcher.Changed += OnGitMetadataChanged;
            SetHistoryControlsEnabled(true);
            await RefreshPageAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == HistoryListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            _ = LoadSelectedCommitAsync();
            return;
        }

        if (command == FilesListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            _ = LoadSelectedFileDiffAsync();
            return;
        }

        switch (command)
        {
            case CommandRefresh:
                RequestRefresh();
                break;
            case CommandPreviousPage:
                if (_hasPreviousPage)
                {
                    _page--;
                    RequestRefresh();
                }
                break;
            case CommandNextPage:
                if (_hasNextPage)
                {
                    _page++;
                    RequestRefresh();
                }
                break;
            case CommandApplyFilter:
                ApplyFilter();
                break;
            case CommandClearFilter:
                _filter = new();
                _page = 0;
                _ = NativeMethods.SetWindowText(_filterEdit, string.Empty);
                RequestRefresh();
                break;
            case CommandCompare:
                _ = CompareReferencesAsync();
                break;
            case CommandReferences:
                ShowReferenceManagement();
                break;
            case CommandLocalState:
                ShowLocalStateManagement();
                break;
            case CommandWorktrees:
                ShowWorktreeManagement();
                break;
            case CommandFileHistory:
                ShowFileHistory();
                break;
            case CommandBlame:
                _ = ShowBlameAsync();
                break;
            case CommandCancel:
                _operationCancellation?.Cancel();
                break;
        }
    }

    private void ApplyFilter()
    {
        string value = NativeMethods.GetWindowTextValue(_filterEdit).Trim();
        int selected = checked((int)NativeMethods.SendMessage(
            _filterCombo,
            NativeMethods.ComboBoxGetCurrentSelection,
            0,
            0));
        GitHistoryFilter? filter = selected switch
        {
            0 => new(Message: NullIfEmpty(value)),
            1 => new(Hash: NullIfEmpty(value)),
            2 => new(Author: NullIfEmpty(value)),
            3 => TryParseDate(value, out DateTimeOffset since) ? new(Since: since) : null,
            4 => TryParseDate(value, out DateTimeOffset until) ? new(Until: until) : null,
            5 => new(Branch: NullIfEmpty(value)),
            6 => new(FilePath: NullIfEmpty(value)),
            _ => new(),
        };
        if (filter is null)
        {
            ShowError(UiText.InvalidDateFilter);
            return;
        }

        _filter = filter;
        _page = 0;
        RequestRefresh();
    }

    private async Task RefreshPageAsync()
    {
        if (_disposed || _historyService is null || _repository is null)
        {
            return;
        }

        int version = ++_refreshVersion;
        int operationVersion = BeginOperation(UiText.HistoryLoading);
        try
        {
            GitHistoryResult result = await _historyService.ReadPageAsync(
                _repository,
                new(_page, 100, _filter),
                CurrentOperationToken);
            if (_disposed || version != _refreshVersion)
            {
                return;
            }

            if (!result.IsSuccess || result.Page is null)
            {
                ShowError(result.ErrorMessage ?? UiText.HistoryUnavailable);
                return;
            }

            _entries.Clear();
            _entries.AddRange(result.Page.Entries);
            _hasPreviousPage = result.Page.HasPreviousPage;
            _hasNextPage = result.Page.HasNextPage;
            _activeDetails = null;
            _files.Clear();
            _ = NativeMethods.SendMessage(_historyList, NativeMethods.ListBoxResetContent, 0, 0);
            _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxResetContent, 0, 0);
            foreach (GitHistoryEntry entry in _entries)
            {
                string decorations = entry.References.Count == 0
                    ? string.Empty
                    : $"  [{string.Join(", ", entry.References.Select(reference => reference.Name))}]";
                string graph = entry.Graph.Length == 0 ? "*" : entry.Graph;
                _ = NativeMethods.SendMessage(
                    _historyList,
                    NativeMethods.ListBoxAddString,
                    0,
                    $"{graph} {entry.ShortHash}  {entry.Subject}{decorations}");
            }

            _ = NativeMethods.SetWindowText(
                _pageLabel,
                $"{UiText.History}  第 {_page + 1} 页  {_entries.Count} 条");
            _ = NativeMethods.SetWindowText(_metadataLabel, UiText.SelectCommit);
            SetDiffText(_entries.Count == 0 ? UiText.HistoryUnavailable : UiText.SelectCommitFile);
            _setStatus(UiText.HistoryReady);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation(operationVersion);
        }
    }

    private async Task LoadSelectedCommitAsync()
    {
        int index = GetListSelection(_historyList);
        if (index < 0 || index >= _entries.Count || _historyService is null || _repository is null)
        {
            return;
        }

        int version = ++_detailsVersion;
        GitHistoryEntry selected = _entries[index];
        GitCommitDetailsResult result = await _historyService.ReadCommitAsync(
            _repository,
            selected.FullHash,
            _lifetimeCancellation.Token);
        if (_disposed || version != _detailsVersion)
        {
            return;
        }

        if (!result.IsSuccess || result.Details is null)
        {
            ShowError(result.ErrorMessage ?? UiText.HistoryUnavailable);
            return;
        }

        _activeDetails = result.Details;
        _files.Clear();
        _files.AddRange(result.Details.Files);
        _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitCommitChangedFile file in _files)
        {
            string original = file.OriginalRelativePath is null ? string.Empty : $" ← {file.OriginalRelativePath}";
            _ = NativeMethods.SendMessage(
                _filesList,
                NativeMethods.ListBoxAddString,
                0,
                $"{StatusSymbol(file.Kind)}  {file.RelativePath}{original}");
        }

        GitHistoryEntry commit = result.Details.Commit;
        string refs = commit.References.Count == 0
            ? "无"
            : string.Join(", ", commit.References.Select(reference => reference.Name));
        string body = result.Details.Body.Length > 1200
            ? string.Concat(result.Details.Body.AsSpan(0, 1200), "…")
            : result.Details.Body;
        _ = NativeMethods.SetWindowText(
            _metadataLabel,
            $"{commit.Subject}\n{commit.FullHash}\n{commit.AuthorName} <{commit.AuthorEmail}>\n{commit.AuthorDate.LocalDateTime:G}\n引用：{refs}\n\n{body}");
        SetDiffText(UiText.SelectCommitFile);
    }

    private async Task LoadSelectedFileDiffAsync()
    {
        int index = GetListSelection(_filesList);
        if (index < 0
            || index >= _files.Count
            || _historyService is null
            || _repository is null
            || _activeDetails is null)
        {
            return;
        }

        GitCommitChangedFile file = _files[index];
        GitComparisonResult result = await _historyService.ReadCommitFileDiffAsync(
            _repository,
            _activeDetails.Commit.FullHash,
            file.RelativePath,
            _lifetimeCancellation.Token);
        ShowComparison(result);
    }

    private async Task CompareReferencesAsync()
    {
        if (_historyService is null || _repository is null)
        {
            return;
        }

        string? baseRevision = NativeTextPrompt.Show(Handle, UiText.CompareReferences, UiText.ComparisonBasePrompt);
        if (string.IsNullOrWhiteSpace(baseRevision))
        {
            return;
        }

        string? targetRevision = NativeTextPrompt.Show(Handle, UiText.CompareReferences, UiText.ComparisonTargetPrompt);
        if (targetRevision is null)
        {
            return;
        }

        string? path = NativeTextPrompt.Show(Handle, UiText.CompareReferences, UiText.ComparisonPathPrompt);
        if (path is null)
        {
            return;
        }

        int operationVersion = BeginOperation(UiText.GeneratingDiff);
        try
        {
            GitComparisonResult result = await _historyService.CompareAsync(
                _repository,
                new(baseRevision.Trim(), NullIfEmpty(targetRevision), NullIfEmpty(path)),
                CurrentOperationToken);
            ShowComparison(result);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation(operationVersion);
        }
    }

    private void ShowFileHistory()
    {
        GitCommitChangedFile? file = GetSelectedFile();
        if (file is null)
        {
            ShowError(UiText.SelectCommitFile);
            return;
        }

        SetFileFilter(file.RelativePath);
    }

    private async Task ShowBlameAsync()
    {
        GitCommitChangedFile? file = GetSelectedFile();
        if (file is null || _historyService is null || _repository is null)
        {
            ShowError(UiText.SelectCommitFile);
            return;
        }

        GitBlameResult result = await _historyService.ReadBlameAsync(
            _repository,
            file.RelativePath,
            cancellationToken: _lifetimeCancellation.Token);
        if (!result.IsSuccess || result.Lines is null)
        {
            ShowError(result.ErrorMessage ?? UiText.HistoryUnavailable);
            return;
        }

        StringBuilder text = new();
        foreach (GitBlameLine line in result.Lines)
        {
            text.Append(line.LineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(6))
                .Append("  ")
                .Append(line.CommitHash.AsSpan(0, Math.Min(10, line.CommitHash.Length)))
                .Append("  ")
                .Append(line.AuthorName)
                .Append("  ")
                .AppendLine(line.Content);
        }

        NativeGitTextDialog.Show(Handle, $"{UiText.Blame} — {file.RelativePath}", text.ToString(), _settings);
    }

    private void ShowReferenceManagement()
    {
        if (_repository is null || _referenceService is null)
        {
            return;
        }

        NativeGitReferenceDialog.Show(Handle, _repository, _referenceService, _settings, _setStatus);
        RequestRefresh();
    }

    private void ShowLocalStateManagement()
    {
        if (_repository is null || _stateService is null)
        {
            return;
        }

        NativeGitLocalStateDialog.Show(Handle, _repository, _stateService, _settings, _setStatus);
        RequestRefresh();
    }

    private void ShowWorktreeManagement()
    {
        if (_repository is null || _worktreeService is null)
        {
            return;
        }

        NativeGitWorktreeDialog.Show(Handle, _repository, _worktreeService, _settings, _setStatus);
        RequestRefresh();
    }

    private void ShowComparison(GitComparisonResult result)
    {
        if (!result.IsSuccess || result.Document is null)
        {
            ShowError(result.ErrorMessage ?? UiText.GenerateDiffFailed);
            return;
        }

        string text = result.Document.Status switch
        {
            GitDiffContentStatus.Ready => result.Document.UnifiedPatch ?? UiText.NoTextDiff,
            GitDiffContentStatus.Binary => UiText.BinaryDiffSummary,
            GitDiffContentStatus.SideTooLarge => UiText.DiffSideTooLarge,
            GitDiffContentStatus.OutputTooLarge => UiText.DiffOutputTooLarge,
            _ => UiText.NoTextDiff,
        };
        if (result.Document.CopyableCommand is not null)
        {
            text = string.Concat(text, Environment.NewLine, Environment.NewLine, result.Document.CopyableCommand);
        }

        SetDiffText(text);
    }

    private int BeginOperation(string status)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        int version = ++_operationVersion;
        _operationRunning = true;
        SetHistoryControlsEnabled(false);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowNormal);
        _setStatus(status);
        return version;
    }

    private void EndOperation(int version)
    {
        if (version != _operationVersion)
        {
            return;
        }

        _operationRunning = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
        SetHistoryControlsEnabled(_historyService is not null);
        _ = NativeMethods.EnableWindow(_previousButton, _hasPreviousPage);
        _ = NativeMethods.EnableWindow(_nextButton, _hasNextPage);
    }

    private CancellationToken CurrentOperationToken =>
        _operationCancellation?.Token ?? _lifetimeCancellation.Token;

    private void SetHistoryControlsEnabled(bool enabled)
    {
        foreach (nint control in Controls.Where(control => control != _cancelButton && control != _pageLabel && control != _metadataLabel))
        {
            _ = NativeMethods.EnableWindow(control, enabled && !_operationRunning);
        }
    }

    private void SetDiffText(string text)
    {
        _displayedDiffText = text;
        _diff?.SetTextContent(text);
    }

    private GitCommitChangedFile? GetSelectedFile()
    {
        int index = GetListSelection(_filesList);
        return index >= 0 && index < _files.Count ? _files[index] : null;
    }

    private static int GetListSelection(nint list)
    {
        return checked((int)NativeMethods.SendMessage(list, NativeMethods.ListBoxGetCurrentSelection, 0, 0));
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

    private void ShowError(string message)
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
        Move(_refreshButton, 8, 7, 58, 28);
        Move(_previousButton, 72, 7, 66, 28);
        Move(_nextButton, 144, 7, 66, 28);
        Move(_filterCombo, 216, 7, 94, 240);
        Move(_filterEdit, 316, 7, 180, 28);
        Move(_applyFilterButton, 502, 7, 76, 28);
        Move(_clearFilterButton, 584, 7, 76, 28);
        Move(_cancelButton, 666, 7, 84, 28);
        Move(_pageLabel, 8, 41, Math.Max(0, width - 16), 22);
        Move(_compareButton, 8, 68, 88, 28);
        Move(_referencesButton, 102, 68, 96, 28);
        Move(_stateButton, 204, 68, 116, 28);
        Move(_worktreesButton, 326, 68, 88, 28);
        int leftWidth = Math.Min(440, Math.Max(320, width / 2));
        Move(_historyList, 8, 102, Math.Max(0, leftWidth - 16), Math.Max(0, height - 110));
        int rightLeft = leftWidth + 8;
        int rightWidth = Math.Max(0, width - rightLeft - 8);
        int metadataHeight = Math.Min(150, Math.Max(100, height / 5));
        Move(_metadataLabel, rightLeft, 102, rightWidth, metadataHeight);
        int filesTop = 108 + metadataHeight;
        int filesHeight = Math.Min(170, Math.Max(110, height / 4));
        Move(_filesList, rightLeft, filesTop, rightWidth, filesHeight);
        Move(_fileHistoryButton, rightLeft, filesTop + filesHeight + 6, 82, 26);
        Move(_blameButton, rightLeft + 88, filesTop + filesHeight + 6, 66, 26);
        int diffTop = filesTop + filesHeight + 38;
        _diff?.SetBounds(rightLeft, diffTop, rightWidth, Math.Max(0, height - diffTop - 8));
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
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
            _ => "M",
        };
    }

    private static bool TryParseDate(string value, out DateTimeOffset date)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out date)
            || DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date);
    }

    private static string? NullIfEmpty(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
