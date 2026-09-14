using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed partial class NativeGitHistoryPanel : IDisposable
{
    private const string WindowClassName = "Augit.GitHistoryPanel.Native";
    private static int HeaderHeight => NativeTheme.ContentHeight(38, 14);
    private static int HeaderTextHeight => NativeTheme.ContentHeight(24, 4);
    private const int HeaderTitleInset = 12;
    private const int HeaderTitleTabGap = 11;
    private const int HeaderTabTextInset = 11;
    private const int HeaderTabGap = 4;
    private static int FilterRowHeight => NativeTheme.ContentHeight(36, 14);
    private static int FileHistoryToolbarHeight => NativeTheme.ContentHeight(39, 12);
    private static int ContentTop => HeaderHeight + FilterRowHeight;
    private static int SideToolbarWidth => NativeTheme.Scale(38);
    private static int BranchRowHeight => NativeTheme.ContentHeight(24, 4);
    private static int HistoryRowHeight => NativeTheme.ContentHeight(26, 6);
    private static int FileRowHeight => NativeTheme.ContentHeight(24, 4);
    private const int HistoryListIdentifier = 1;
    private const int FilesListIdentifier = 2;
    private const string RootFileGroupKey = ".";
    private const int BranchesListIdentifier = 3;
    private const int FilterEditIdentifier = 63;
    private const int BranchFilterEditIdentifier = 64;
    private const int EditNotificationChanged = 0x0300;
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
    private const int CommandMoreActions = 22;
    private const int CommandClosePanel = 23;
    private const int CommandChooseFilterKind = 24;
    private const int CommandBack = 25;
    private const int CommandCreateReference = 26;
    private const int CommandDeleteReference = 27;
    private const int CommandSearch = 28;
    private const int CommandLocateHead = 29;
    private const int CommandFilterAuthor = 30;
    private const int CommandFilterDate = 31;
    private const int CommandFilterPath = 32;
    private const int CommandCreateStash = 33;
    private const int CommandManageStashes = 34;
    private const int CommandResetCurrentBranch = 35;
    private const int CommandToggleDetails = 36;
    private const int CommandFileHistoryClear = 37;
    private const int CommandFileHistoryEdit = 38;
    private const int CommandFileHistoryExpand = 39;
    private const int CommandFileHistoryUnified = 40;
    private const int CommandFileHistorySplit = 41;
    private const int CommandFileHistorySettings = 42;
    private const int CommandFilterOverflow = 43;
    private const int CommandToolbarOverflow = 44;
    private const int FileHistoryListIdentifier = 4;
    private const int MenuCopyCommitHash = 200;
    private const int MenuCherryPick = 201;
    private const int MenuCompareWithWorkspace = 202;
    private const int MenuResetCurrentBranch = 203;
    private const int MenuRevertCommit = 204;
    private const int MenuCreateBranch = 205;
    private const int MenuCreateTag = 206;
    private const int FilterMenuCommandBase = 1000;
    private const nuint BranchesListSubclassIdentifier = 1;
    private const nuint HistoryListSubclassIdentifier = 2;
    private const nuint FilesListSubclassIdentifier = 3;
    private const uint WindowMessageRefresh = NativeMethods.WindowMessageApp + 30;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitHistoryPanel> Instances = [];
    private static readonly Dictionary<nint, NativeGitHistoryPanel> BranchesListInstances = [];
    private static readonly Dictionary<nint, NativeGitHistoryPanel> HistoryListInstances = [];
    private static readonly Dictionary<nint, NativeGitHistoryPanel> FilesListInstances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly NativeMethods.SubclassProcedure BranchesListProcedure = HandleBranchesListMessage;
    private static readonly NativeMethods.SubclassProcedure HistoryListProcedure = HandleHistoryListMessage;
    private static readonly NativeMethods.SubclassProcedure FilesListProcedure = HandleFilesListMessage;
    private static readonly string[] FilterKindLabels =
    [
        UiText.FilterMessage,
        UiText.FilterHash,
        UiText.FilterAuthor,
        UiText.FilterSince,
        UiText.FilterUntil,
        UiText.FilterBranch,
        UiText.FilterFile,
    ];
    private static bool _classRegistered;

    private sealed record HistoryContextSnapshot(
        GitHistoryFilter Filter,
        int FilterKindIndex,
        int Page,
        IReadOnlyList<GitHistoryEntry> Entries,
        bool HasPreviousPage,
        bool HasNextPage,
        string? SelectedCommitHash,
        GitCommitDetails? ActiveDetails,
        IReadOnlyList<GitCommitChangedFile> Files,
        IReadOnlyCollection<string> CollapsedFileGroups,
        FileListViewSnapshot FileListView,
        string FilterDraft,
        bool ShowDetails,
        int HistoryTopIndex,
        int HistoryHorizontalPosition,
        int DetailsScrollPosition,
        bool HasLoadedPage);

    private sealed record FileListViewSnapshot(
        string? SelectedPath,
        string? SelectedGroupKey,
        int SelectedIndex,
        string? TopAnchor,
        int TopIndex);

    private readonly string _workspaceRoot;
    private ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly Func<string, IReadOnlyList<GitBlameLine>, CancellationToken, Task> _showBlame;
    private readonly Action<GitComparisonResult> _showComparison;
    private readonly Func<GitComparisonDocument, Func<CancellationToken, Task<GitComparisonResult>>, bool, CancellationToken, Task> _openFileComparison;
    private readonly Action _activateFileComparison;
    private readonly Action<string, string> _updateFollowedComparisonNotice;
    private readonly Action _closePanel;
    private readonly List<GitHistoryEntry> _entries = [];
    private NativeCommitGraph _commitGraph = new([], 1);
    private int _commitGraphBuildCount;
    private int _historyRowExtent;
    private bool _updatingHistoryExtent;
    private readonly List<GitCommitChangedFile> _files = [];
    private readonly List<FileTreeRow> _fileRows = [];
    private readonly List<BranchRow> _branchRows = [];
    private readonly HashSet<string> _collapsedBranchSections = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedFileGroups = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Task _initializationTask;
    private NativeToolTip? _toolTip;
    private GitRuntimeInfo? _runtime;
    private GitRepositorySnapshot? _repository;
    private IGitHistoryService? _historyService;
    private GitReferenceService? _referenceService;
    private GitWorkspaceStateService? _stateService;
    private GitWorktreeService? _worktreeService;
    private GitOperationService? _operationService;
    private GitConflictService? _conflictService;
    private GitMetadataWatcher? _metadataWatcher;
    private GitReferenceSnapshot _referenceSnapshot = new([], []);
    private GitHistoryFilter _filter = new();
    private GitCommitDetails? _activeDetails;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _fileDiffCancellation;
    private CancellationTokenSource? _blameCancellation;
    private int _blameRequestVersion;
    private string? _blameRequestedPath;
    private (string Commit, string Path)? _fileDiffRequestKey;
    private Task? _fileDiffRequestTask;
    private bool _showFileDiffCancel;
    private nint _titleLabel;
    private nint _pageLabel;
    private nint _backButton;
    private nint _createReferenceButton;
    private nint _deleteReferenceButton;
    private nint _searchButton;
    private nint _locateHeadButton;
    private nint _toolbarOverflowButton;
    private NativeToolbarPopup? _toolbarPopup;
    private nint _refreshButton;
    private nint _previousButton;
    private nint _nextButton;
    private nint _filterKindButton;
    private nint _filterAuthorButton;
    private nint _filterDateButton;
    private nint _filterPathButton;
    private nint _filterOverflowButton;
    private NativeHistoryFilterLayout _filterLayout;
    private bool _rowTextMetricsDirty = true;
    private nint _rowTextFont;
    private int _rowReferenceWidth;
    private int _rowAuthorWidth;
    private int _rowFullDateWidth;
    private int _rowCompactDateWidth;
    private int _fileHistoryAuthorWidth;
    private int _fileHistoryDateWidth;
    private nint _filterEdit;
    private nint _branchFilterEdit;
    private nint _branchesList;
    private nint _filterSearchButton;
    private nint _toggleDetailsButton;
    private nint _fileHistoryTab;
    private nint _fileHistoryBranchLabel;
    private nint _fileHistoryClearButton;
    private NativeGitComparisonView? _fileHistoryComparison;
    private nint _applyFilterButton;
    private nint _clearFilterButton;
    private nint _compareButton;
    private nint _referencesButton;
    private nint _stateButton;
    private nint _worktreesButton;
    private nint _moreActionsButton;
    private nint _closePanelButton;
    private nint _cancelButton;
    private nint _historyList;
    private nint _metadataLabel;
    private nint _detailsBodyFont;
    private nint _filesList;
    private nint _detailsTabLabel;
    private nint _fileHistoryButton;
    private nint _blameButton;
    private nint _controlBrush;
    private NativeContextMenu? _contextMenu;
    private int _page;
    private bool _hasPreviousPage;
    private bool _hasNextPage;
    private bool _operationRunning;
    private bool _operationIsComparison;
    private bool _operationIsHistoryQuery;
    private bool _refreshPending;
    private bool _refreshQueued;
    private bool _updatingHistoryList;
    private bool _updatingBranchesList;
    private bool _appendNextPage;
    private bool _hasLoadedHistoryPage;
    private int? _requestedHistoryPage;
    private bool _showDetails = true;
    private bool _fileHistoryMode;
    private int _fileHistoryPreviewVersion;
    private CancellationTokenSource? _fileHistoryPreviewCancellation;
    private (string Commit, string Path)? _fileHistoryPreviewKey;
    private int _refreshVersion;
    private int _filterKindIndex;
    private int _detailsVersion;
    private int _operationVersion;
    private int _fileDiffRequestVersion;
    private int _commitDetailsRequestCount;
    private int _historyListResetCount;
    private int _historyListDeltaCount;
    private int _branchListResetCount;
    private int _branchListDeltaCount;
    private int _fileListResetCount;
    private int _fileListDeltaCount;
    private string? _selectedCommitHash;
    private bool _followFileComparison;
    private string? _detailsLoadingHash;
    // 详情区使用分层绘制，控件文本仅保留给辅助技术和自动化读取。
    private string? _detailsTitle;
    private string? _detailsMeta;
    private string? _detailsReferences;
    private string? _detailsBody;
    private bool _detailsMessageCentered;
    private Task? _refreshTask;
    private Task _lastFileActivationForTest = Task.CompletedTask;
    private NativeMethods.Rectangle _filterEditFrame;
    private NativeMethods.Rectangle _branchFilterEditFrame;
    private bool _hasBounds;
    private (int X, int Y, int Width, int Height) _bounds;
    private bool _disposed;
    private HistoryContextSnapshot? _historyContextBeforeFileFilter;
    private FileListViewSnapshot? _pendingFileListView;

    internal NativeGitHistoryPanel(
        nint parent,
        string workspaceRoot,
        ApplicationSettings settings,
        Action<string> setStatus,
        Func<string, IReadOnlyList<GitBlameLine>, CancellationToken, Task> showBlame,
        Action<GitComparisonResult> showComparison,
        Func<GitComparisonDocument, Func<CancellationToken, Task<GitComparisonResult>>, bool, CancellationToken, Task> openFileComparison,
        Action activateFileComparison,
        Action<string, string> updateFollowedComparisonNotice,
        Action closePanel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(showBlame);
        ArgumentNullException.ThrowIfNull(showComparison);
        ArgumentNullException.ThrowIfNull(openFileComparison);
        ArgumentNullException.ThrowIfNull(activateFileComparison);
        ArgumentNullException.ThrowIfNull(updateFollowedComparisonNotice);
        ArgumentNullException.ThrowIfNull(closePanel);
        _workspaceRoot = workspaceRoot;
        _settings = settings;
        _setStatus = setStatus;
        _showBlame = showBlame;
        _showComparison = showComparison;
        _openFileComparison = openFileComparison;
        _activateFileComparison = activateFileComparison;
        _updateFollowedComparisonNotice = updateFollowedComparisonNotice;
        _closePanel = closePanel;
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
        CreateToolTips();
        ApplyAppearance();
        ApplyFilterCueBanners();
        Layout();
        _initializationTask = InitializeAsync();
    }

    internal nint Handle { get; private set; }

    internal int EntryCount => _entries.Count;

    internal bool IsRuntimeAvailable => _runtime?.IsAvailable == true;

    internal bool HistoryListHasFocusForTest => NativeMethods.GetFocus() == _historyList;

    internal bool BranchFilterHasFocusForTest => NativeMethods.GetFocus() == _branchFilterEdit;

    internal bool BranchesListHasFocusForTest => NativeMethods.GetFocus() == _branchesList;

    internal bool FilterHasFocusForTest => NativeMethods.GetFocus() == _filterEdit;

    internal GitRepositoryKind? RepositoryKind => _repository?.Kind;

    internal int CurrentPageForTest => _page;

    internal bool HasNextPageForTest => _hasNextPage;

    internal int ChangedFileCountForTest => _files.Count;

    internal int CommitDetailsRequestCountForTest => _commitDetailsRequestCount;

    internal nint FilesListHandleForTest => _filesList;

    internal nint HistoryListHandleForTest => _historyList;

    internal int FileListTopIndexForTest => checked((int)NativeMethods.SendMessage(
        _filesList,
        NativeMethods.ListBoxGetTopIndex,
        0,
        0));

    internal int FileListSelectedIndexForTest => GetListSelection(_filesList);

    internal NativeCommitGraph CommitGraphForTest => _commitGraph;

    internal int CommitGraphBuildCountForTest => _commitGraphBuildCount;

    internal Task LastFileActivationForTest => _lastFileActivationForTest;

    internal nint CancelButtonForTest => _cancelButton;

    internal void SetHistoryServiceForTest(IGitHistoryService service) => _historyService = service;

    internal int HistoryListTopIndexForTest => checked((int)NativeMethods.SendMessage(
        _historyList,
        NativeMethods.ListBoxGetTopIndex,
        0,
        0));

    internal string? HistoryListTopHashForTest
    {
        get
        {
            int topIndex = HistoryListTopIndexForTest;
            return topIndex >= 0 && topIndex < _entries.Count
                ? _entries[topIndex].FullHash
                : null;
        }
    }

    internal int HistoryListResetCountForTest => _historyListResetCount;

    internal int HistoryListDeltaCountForTest => _historyListDeltaCount;

    internal int FileTreeRowCountForTest => _fileRows.Count;

    internal IReadOnlyList<string> FileTreeLabelsForTest => _fileRows.Select(row => row.Label).ToArray();

    internal int FileListResetCountForTest => _fileListResetCount;

    internal int FileListDeltaCountForTest => _fileListDeltaCount;

    internal static IReadOnlyList<string> BuildFileTreeLabelsForTest(
        IReadOnlyList<GitCommitChangedFile> files)
    {
        return BuildFileTreeRows(files.ToList(), new(StringComparer.OrdinalIgnoreCase))
            .Select(row => row.Label)
            .ToArray();
    }

    internal static (bool CanApply, int Prefix, int RemovedCount, int AddedCount)
        FileTreeDeltaForTest(
            IReadOnlyList<GitCommitChangedFile> files,
            IReadOnlySet<string> previousCollapsedGroups,
            IReadOnlySet<string> currentCollapsedGroups)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(previousCollapsedGroups);
        ArgumentNullException.ThrowIfNull(currentCollapsedGroups);
        List<FileTreeRow> previous = BuildFileTreeRows(
            files.ToList(),
            new(previousCollapsedGroups, StringComparer.OrdinalIgnoreCase));
        List<FileTreeRow> current = BuildFileTreeRows(
            files.ToList(),
            new(currentCollapsedGroups, StringComparer.OrdinalIgnoreCase));
        bool canApply = TryComputeFileListDelta(
            previous,
            current,
            out int prefix,
            out int removedCount,
            out int addedCount);
        return (canApply, prefix, removedCount, addedCount);
    }

    internal bool DetailsSurfaceVisibleForTest => _metadataLabel != 0
        && NativeMethods.IsWindowVisible(_metadataLabel);

    internal bool DetailsShownForTest => _showDetails;

    internal bool FileHistoryModeForTest => _fileHistoryMode;

    internal bool FileHistoryEditorVisibleForTest => _fileHistoryComparison?.IsVisible == true;

    internal bool FileHistoryEditorReadOnlyForTest => _fileHistoryComparison?.IsReadOnly == true;

    internal NativeGitComparisonView? FileHistoryComparisonForTest => _fileHistoryComparison;

    internal string FileHistoryEditorTextForTest => _fileHistoryComparison?.BodyTextForTest ?? string.Empty;

    internal bool FileHistoryToolbarCreatedForTest => _fileHistoryBranchLabel != 0
        && _fileHistoryClearButton != 0 && _fileHistoryComparison?.ToolbarToolTipsCreatedForTest == true;

    internal bool FileHistoryRightToolbarVisibleForTest => _fileHistoryComparison?.IsVisible == true
        && Enumerable.Range(0, 7).All(index => NativeMethods.IsWindowVisible(_fileHistoryComparison.ToolbarButtonForTest(index)));

    internal bool FilterUtilityButtonsVisibleForTest => _toggleDetailsButton != 0
        && _filterSearchButton != 0
        && NativeMethods.IsWindowVisible(_toggleDetailsButton)
        && NativeMethods.IsWindowVisible(_filterSearchButton);

    internal bool ToggleDetailsForTest()
    {
        if (_toggleDetailsButton == 0)
        {
            return false;
        }

        HandleCommand(unchecked((nuint)CommandToggleDetails));
        return true;
    }

    private void EnsureFileHistoryComparison()
    {
        if (_fileHistoryComparison is not null)
        {
            return;
        }

        _fileHistoryComparison = new NativeGitComparisonView(
            Handle,
            _settings,
            _setStatus,
            ReloadFileHistoryComparisonAsync);
        _fileHistoryComparison.SetVisible(false);
    }

    internal bool FilesListScrollBarVisibleForTest => _filesList != 0
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
                _filesList,
                NativeMethods.WindowLongStyle).ToInt64())
            & NativeMethods.WindowStyleVerticalScroll) != 0;

    internal static bool ShouldShowFilesListScrollBarForTest(int fileCount, int availableHeight)
    {
        return ShouldShowFilesListScrollBar(fileCount, availableHeight);
    }

    internal static (int FilesTop, int FilesHeight, int ActionsTop, int DetailsTop, int DetailsHeight)
        CalculateDetailsLayoutForTest(int height)
    {
        return CalculateDetailsLayout(height, showActions: true);
    }

    internal static (int FilesTop, int FilesHeight, int ActionsTop, int DetailsTop, int DetailsHeight)
        CalculateCompactDetailsLayoutForTest(int height)
    {
        return CalculateDetailsLayout(height, showActions: false);
    }

    internal static string FormatHistoryMetaForTest(GitHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return FormatHistoryMeta(entry);
    }

    internal static (uint Panel, uint Selection) HistoryRowColorsForTest(bool dark, bool hasFocus = true)
    {
        return HistoryRowColors(dark, hasFocus);
    }

    internal string? FileFilterForTest => _filter.FilePath;

    internal string? BranchFilterForTest => _filter.Branch;

    internal bool OperationRunningForTest => _operationRunning;

    internal bool CancelOperationForHost()
    {
        if (!_operationRunning || _operationCancellation is null)
        {
            if (_fileDiffCancellation is null)
            {
                return false;
            }
            CancelPendingFileDiff();
            return true;
        }

        _operationCancellation.Cancel();
        return true;
    }

    internal string? SelectedCommitHashForTest => _selectedCommitHash;
    internal string? SelectedFileRelativePath => GetSelectedFile()?.RelativePath;

    internal bool CommitDetailsLoadedForTest => _activeDetails is not null;

    internal static bool ShouldReuseCommitDetailsRequestForTest(
        string? selectedCommitHash,
        string selectedFullHash,
        string? detailsLoadingHash,
        string? detailsLoadedHash)
    {
        return ShouldReuseCommitDetailsRequest(
            selectedCommitHash,
            selectedFullHash,
            detailsLoadingHash,
            detailsLoadedHash);
    }

    internal static (int SelectedIndex, int TopIndex) ResolveFileTreeViewForTest(
        IReadOnlyList<GitCommitChangedFile> files,
        string? selectedPath,
        string? selectedGroupKey,
        int previousSelectedIndex,
        string? topAnchor,
        int previousTopIndex)
    {
        ArgumentNullException.ThrowIfNull(files);
        List<FileTreeRow> rows = BuildFileTreeRows(
            files.ToList(),
            new(StringComparer.OrdinalIgnoreCase));
        return (
            ResolveFileTreeSelectionIndex(
                rows,
                selectedPath,
                selectedGroupKey,
                previousSelectedIndex),
            ResolveFileTreeTopIndex(rows, previousTopIndex, topAnchor));
    }

    internal static bool HistoryEntriesEqualForTest(
        IReadOnlyList<GitHistoryEntry> left,
        IReadOnlyList<GitHistoryEntry> right)
    {
        return HistoryEntriesEqual(left, right);
    }

    internal static bool ReferenceSnapshotsEqualForTest(
        GitReferenceSnapshot left,
        GitReferenceSnapshot right)
    {
        return ReferenceSnapshotsEqual(left, right);
    }

    internal IReadOnlyList<string> BranchRowsForTest => _branchRows.Select(row => row.Text).ToArray();

    internal int BranchListResetCountForTest => _branchListResetCount;

    internal int BranchListDeltaCountForTest => _branchListDeltaCount;

    internal bool ToggleFileGroupForTest(string groupKey)
    {
        int index = _fileRows.FindIndex(row => row.Group && row.GroupKey.Equals(
            groupKey,
            StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        bool before = _collapsedFileGroups.Contains(groupKey);
        ToggleFileGroup(_fileRows[index]);
        return _collapsedFileGroups.Contains(groupKey) != before;
    }

    internal bool ClickBranchSectionForTest(string title)
    {
        int index = _branchRows.FindIndex(row => row.Group && row.Text.Equals(title, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        NativeMethods.Rectangle rectangle = default;
        if (NativeMethods.SendMessage(
                _branchesList,
                NativeMethods.ListBoxGetItemRectangle,
                unchecked((nuint)index),
                ref rectangle) == unchecked((nint)(-1)))
        {
            return false;
        }

        bool before = _collapsedBranchSections.Contains(title);
        int x = rectangle.Left + NativeTheme.Scale(14);
        int y = (rectangle.Top + rectangle.Bottom) / 2;
        nint point = unchecked((nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF)));
        _ = NativeMethods.SendMessage(_branchesList, NativeMethods.WindowMessageLeftButtonDown, 1, point);
        _ = NativeMethods.SendMessage(_branchesList, NativeMethods.WindowMessageLeftButtonUp, 0, point);
        return _collapsedBranchSections.Contains(title) != before;
    }

    internal bool DoubleClickBranchSectionForTest(string title)
    {
        return SendBranchSectionInputForTest(title, NativeMethods.WindowMessageLeftButtonDoubleClick);
    }

    internal bool ToggleBranchSectionWithKeyForTest(string title, int virtualKey)
    {
        int index = _branchRows.FindIndex(row => row.Group && row.Text.Equals(title, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        bool before = _collapsedBranchSections.Contains(title);
        _ = NativeMethods.SendMessage(
            _branchesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        _ = NativeMethods.SendMessage(
            _branchesList,
            NativeMethods.WindowMessageKeyDown,
            unchecked((nuint)virtualKey),
            0);
        return _collapsedBranchSections.Contains(title) != before;
    }

    private bool SendBranchSectionInputForTest(string title, uint message)
    {
        int index = _branchRows.FindIndex(row => row.Group && row.Text.Equals(title, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        NativeMethods.Rectangle rectangle = default;
        if (NativeMethods.SendMessage(
                _branchesList,
                NativeMethods.ListBoxGetItemRectangle,
                unchecked((nuint)index),
                ref rectangle) == unchecked((nint)(-1)))
        {
            return false;
        }

        bool before = _collapsedBranchSections.Contains(title);
        int x = rectangle.Left + NativeTheme.Scale(38);
        int y = (rectangle.Top + rectangle.Bottom) / 2;
        nint point = unchecked((nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF)));
        _ = NativeMethods.SendMessage(_branchesList, message, 1, point);
        return _collapsedBranchSections.Contains(title) != before;
    }

    internal static GitHistoryFilter? UpdateFilterForTest(
        GitHistoryFilter current,
        int filterKindIndex,
        string value)
    {
        return UpdateFilter(current, filterKindIndex, value);
    }

    internal void SetBranchFilterForTest(string value)
    {
        _ = NativeMethods.SetWindowText(_branchFilterEdit, value);
        PopulateBranches(_referenceSnapshot);
    }

    internal bool ManagementButtonsCreatedForTest => _referencesButton != 0
        && _stateButton != 0
        && _worktreesButton != 0;

    internal bool SideToolbarCreatedForTest => _backButton != 0
        && _createReferenceButton != 0
        && _deleteReferenceButton != 0
        && _refreshButton != 0
        && _searchButton != 0
        && _compareButton != 0
        && _locateHeadButton != 0;

    internal static int SideToolbarWidthForTest => SideToolbarWidth;

    internal static float SideToolbarIconStrokeWidthForTest => NativeTheme.Scale(1.5f);

    internal static (
        int Back,
        int CreateReference,
        int DeleteReference,
        int Refresh,
        int Search,
        int Compare,
        int LocateHead) CalculateSideToolbarButtonTopsForTest()
    {
        int firstTop = HeaderHeight + NativeTheme.Scale(4);
        int firstActionTop = ContentTop + NativeTheme.Scale(13);
        int actionStep = NativeTheme.Scale(30);
        return (
            firstTop,
            firstActionTop,
            firstActionTop + actionStep,
            firstActionTop + actionStep * 2,
            firstActionTop + actionStep * 3,
            firstActionTop + actionStep * 4,
            firstActionTop + actionStep * 5);
    }

    internal static (int BranchWidth, int LogWidth, int DetailsWidth) GetColumnWidthsForTest(int width)
    {
        return GetColumnWidths(width);
    }

    internal static NativeHistoryFilterLayout GetFilterControlLayoutForTest(int logWidth, int labelWidth)
    {
        return CalculateFilterControlLayout(logWidth, labelWidth);
    }

    internal bool FilterOverflowVisibleForTest => NativeMethods.IsWindowVisible(_filterOverflowButton);

    internal static (int VisibleCount, int OverflowTop) CalculateSideToolbarLayout(int height)
    {
        var tops = CalculateSideToolbarButtonTopsForTest();
        int[] positions = [tops.Back, tops.CreateReference, tops.DeleteReference, tops.Refresh, tops.Search, tops.Compare, tops.LocateHead];
        int fitting = positions.Count(top => top + NativeTheme.Scale(28) <= height - NativeTheme.Scale(4));
        if (fitting == positions.Length) return (fitting, -1);
        if (fitting == 0) return (0, -1);
        // 返回留在原位，极短区域只收紧箭头前的留白，不绘制被底边裁切的命中区。
        int visible = Math.Max(1, fitting - 1);
        int overflowTop = Math.Min(positions[visible], height - NativeTheme.Scale(32));
        return (visible, overflowTop >= positions[visible - 1] + NativeTheme.Scale(30) ? overflowTop : -1);
    }

    internal bool ToolbarOverflowVisibleForTest => NativeMethods.IsWindowVisible(_toolbarOverflowButton);
    internal NativeToolbarPopup? ToolbarPopupForTest => _toolbarPopup;
    internal void ShowToolbarOverflowForTest() => ShowToolbarOverflow();
    internal IReadOnlyList<nint> VisibleSideToolbarButtonsForTest => GetSideToolbarButtons().Select(button => button.Control)
        .Append(_toolbarOverflowButton).Where(NativeMethods.IsWindowVisible).ToArray();

    internal nint FilterOverflowMenuForTest => _contextMenu?.Handle ?? 0;

    internal int FilterKindIndexForTest => _filterKindIndex;

    internal void ShowFilterOverflowForTest() => ShowFilterOverflow();

    internal void SelectFilterKindForTest(int index) => SelectFilterKind(index);

    internal (int X, int Y, int Width, int Height) BoundsForTest => _bounds;

    internal static NativeHistoryRowTextLayout GetHistoryRowTextLayoutForTest(
        int rowWidth, int referenceWidth, int authorWidth, int fullDateWidth, int compactDateWidth)
    {
        return CalculateHistoryRowTextLayout(rowWidth, referenceWidth, authorWidth, fullDateWidth, compactDateWidth);
    }

    internal bool BranchListUsesOwnerDrawForTest => _branchesList != 0
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
                _branchesList,
                NativeMethods.WindowLongStyle).ToInt64())
            & NativeMethods.ListBoxOwnerDrawFixed) == NativeMethods.ListBoxOwnerDrawFixed;

    internal bool FilterKindUsesOwnerDrawForTest => _filterKindButton != 0
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
                _filterKindButton,
                NativeMethods.WindowLongStyle).ToInt64())
            & NativeMethods.ButtonOwnerDraw) == NativeMethods.ButtonOwnerDraw;

    internal bool FilterToolbarMatchesSpecForTest => new[]
        {
            _filterKindButton,
            _filterAuthorButton,
            _filterDateButton,
            _filterPathButton,
        }.All(control => control != 0 && (NativeMethods.IsWindowVisible(control) || FilterOverflowVisibleForTest))
        && !NativeMethods.IsWindowVisible(_previousButton)
        && !NativeMethods.IsWindowVisible(_nextButton)
        && !NativeMethods.IsWindowVisible(_applyFilterButton)
        && !NativeMethods.IsWindowVisible(_clearFilterButton)
        && NativeMethods.GetWindowTextValue(_filterKindButton) == UiText.FilterBranch
        && NativeMethods.GetWindowTextValue(_filterAuthorButton) == UiText.FilterUser
        && NativeMethods.GetWindowTextValue(_filterDateButton) == UiText.FilterDate
        && NativeMethods.GetWindowTextValue(_filterPathButton) == UiText.FilterPath;

    internal bool ToolbarIsCompactForTest => _moreActionsButton != 0
        && NativeMethods.IsWindowVisible(_moreActionsButton)
        && _closePanelButton != 0
        && NativeMethods.IsWindowVisible(_closePanelButton)
        && !NativeMethods.IsWindowVisible(_referencesButton)
        && !NativeMethods.IsWindowVisible(_stateButton)
        && !NativeMethods.IsWindowVisible(_worktreesButton);

    internal bool CommitDetailsLabelCreatedForTest => _detailsTabLabel != 0
        && NativeMethods.GetWindowTextValue(_detailsTabLabel) == UiText.CommitDetails;

    internal static IReadOnlyList<string> HistoryContextMenuLabelsForTest =>
    [
        UiText.CopyCommitHash,
        UiText.CherryPick,
        UiText.CompareWithWorkspace,
        UiText.ResetCurrentBranchHere,
        UiText.RevertCommit,
        UiText.NewBranch,
        UiText.NewTag,
    ];

    internal NativeContextMenu? ContextMenuForTest => _contextMenu;

    internal bool HistoryListUsesIdeaInputForTest
    {
        get
        {
            lock (InstancesGate)
            {
                return _historyList != 0
                    && HistoryListInstances.TryGetValue(_historyList, out NativeGitHistoryPanel? panel)
                    && ReferenceEquals(panel, this);
            }
        }
    }

    internal bool FilesListUsesIdeaActivationForTest
    {
        get
        {
            lock (InstancesGate)
            {
                return _filesList != 0
                    && FilesListInstances.TryGetValue(_filesList, out NativeGitHistoryPanel? panel)
                    && ReferenceEquals(panel, this);
            }
        }
    }

    internal async Task NextPageForTestAsync()
    {
        await RequestNextPageAsync();
    }

    internal async Task ScrollHistoryToBottomForTestAsync()
    {
        if (_entries.Count > 0)
        {
            _ = NativeMethods.SendMessage(
                _historyList,
                NativeMethods.ListBoxSetTopIndex,
                unchecked((nuint)(_entries.Count - 1)),
                0);
        }

        RequestNextPageWhenAtBottom();
        if (_refreshTask is { } refreshTask)
        {
            await refreshTask;
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

    internal bool ShowHistoryContextMenuForHost()
    {
        return ShowHistoryContextMenu(unchecked((nint)(-1)));
    }

    internal void ReturnFromHistoryForTest()
    {
        ReturnFromHistory();
    }

    /// <summary>
    /// 只模拟单击变化文件。历史详情中的单击只能改变选中行，不能提前请求或打开提交 Diff。
    /// </summary>
    internal Task SelectCommitFileForTestAsync(int index)
    {
        if (index < 0 || index >= _files.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int rowIndex = FindFileTreeRowIndex(_files[index].RelativePath);
        if (rowIndex < 0)
        {
            throw new InvalidOperationException("提交文件当前处于折叠目录中。");
        }

        _ = NativeMethods.SendMessage(
            _filesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)rowIndex),
            0);
        return Task.CompletedTask;
    }

    internal async Task ActivateCommitFileWithEnterForTestAsync(int index)
    {
        if (index < 0 || index >= _files.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int rowIndex = FindFileTreeRowIndex(_files[index].RelativePath);
        if (rowIndex < 0)
        {
            throw new InvalidOperationException("提交文件当前处于折叠目录中。");
        }

        _ = NativeMethods.SendMessage(
            _filesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)rowIndex),
            0);
        _lastFileActivationForTest = Task.CompletedTask;
        _ = NativeMethods.SendMessage(
            _filesList,
            NativeMethods.WindowMessageKeyDown,
            NativeMethods.VirtualKeyEnter,
            0);
        await _lastFileActivationForTest;
    }

    internal async Task SelectBranchForTestAsync(string name)
    {
        int index = _branchRows.FindIndex(row =>
            !row.Group && row.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(name));
        }

        _ = NativeMethods.SendMessage(
            _branchesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        ApplyBranchFilter(_branchRows[index]);
        await RefreshPageAsync();
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        if (Handle == 0)
        {
            return;
        }

        (int X, int Y, int Width, int Height) next = (x, y, Math.Max(0, width), Math.Max(0, height));
        if (!ShouldApplyBoundsForTest(_hasBounds, _bounds, next))
        {
            return;
        }

        _hasBounds = true;
        _bounds = next;
        // Git 局部状态刷新不应重复搬动历史工具窗口，尺寸变化仍由 WM_SIZE 完成内部布局。
        _ = NativeMethods.MoveWindow(Handle, next.X, next.Y, next.Width, next.Height, true);
    }

    internal static bool ShouldApplyBoundsForTest(
        bool hasBounds,
        (int X, int Y, int Width, int Height) current,
        (int X, int Y, int Width, int Height) next)
    {
        return !hasBounds || current != next;
    }

    internal void SetVisible(bool visible)
    {
        if (!visible)
        {
            _toolbarPopup?.Dispose();
            CancelPendingBlame();
            CancelPendingFileDiff();
            CancelFileHistoryPreview(keepLoaded: true);
        }
        if (Handle != 0)
        {
            _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
        if (visible && _fileHistoryMode && _activeDetails is { } details)
        {
            _ = LoadFileHistoryPreviewAsync(details.Commit.FullHash);
        }
    }

    internal void SetFileFilter(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        CancelPendingFileDiff();
        if (!_fileHistoryMode)
        {
            _historyContextBeforeFileFilter = CaptureHistoryContext();
        }

        InvalidateHistoryQuery();

        _fileHistoryMode = true;
        CancelFileHistoryPreview();
        EnsureFileHistoryComparison();
        _filter = new(FilePath: relativePath.Replace('\\', '/'));
        _page = 0;
        SelectFilterKind(6);
        _ = NativeMethods.SetWindowText(_filterEdit, _filter.FilePath!);
        _ = NativeMethods.SetWindowText(_fileHistoryTab, FormatFileHistoryTab(_filter.FilePath));
        _ = NativeMethods.SetWindowText(_fileHistoryBranchLabel, FormatFileHistoryBranchLabel());
        _fileHistoryComparison?.ShowMessage(UiText.HistoryLoading);
        Layout();
        _ = NativeMethods.SetFocus(_historyList);
        RequestRefresh();
    }

    internal async Task LocateCommitAsync(string commitHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitHash);
        await _initializationTask;
        if (_disposed || _historyService is null || _repository is null)
        {
            return;
        }

        ExitFileHistoryView();
        _historyContextBeforeFileFilter = null;
        _filter = new(Hash: commitHash);
        InvalidateHistoryQuery();
        _page = 0;
        SelectFilterKind(1);
        _ = NativeMethods.SetWindowText(_filterEdit, commitHash);
        Layout();
        _ = NativeMethods.SetFocus(_historyList);
        await RefreshPageAsync();
        // 文件历史的旧页仍在收尾时，本次请求会排在其后；只等待当前定位对应的查询。
        while (!_disposed && _filter.Hash == commitHash && _refreshTask is { IsCompleted: false } pending)
            await pending;
        if (_disposed || _filter.Hash != commitHash) return;
        int index = _entries.FindIndex(
            entry => entry.FullHash.Equals(commitHash, StringComparison.OrdinalIgnoreCase)
                || entry.ShortHash.Equals(commitHash, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        _ = NativeMethods.SendMessage(
            _historyList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        await LoadSelectedCommitAsync();
    }

    internal async Task ShowBlameForPathAsync(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        CancelPendingBlame();
        int version = _blameRequestVersion;
        _blameRequestedPath = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));
        await _initializationTask;
        if (_disposed || version != _blameRequestVersion) return;
        if (_historyService is null || _repository is null)
        {
            ShowError(UiText.HistoryUnavailable);
            return;
        }

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _blameCancellation = cancellation;
        try
        {
            GitBlameResult result = await _historyService.ReadBlameAsync(
                _repository, relativePath, cancellationToken: cancellation.Token);
            if (_disposed || version != _blameRequestVersion || cancellation.IsCancellationRequested) return;
            if (!result.IsSuccess || result.Lines is null)
            {
                ShowError(result.ErrorMessage ?? UiText.HistoryUnavailable);
                return;
            }
            // 同一取消源覆盖 Git 查询与正文读取，关闭历史或发起新查询也能使第二阶段失效。
            await _showBlame(relativePath, result.Lines, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_blameCancellation, cancellation)) _blameCancellation = null;
            if (version == _blameRequestVersion) _blameRequestedPath = null;
        }
    }

    internal void CancelPendingBlame(string? closingPath = null)
    {
        if (closingPath is not null && !string.Equals(closingPath, _blameRequestedPath, StringComparison.OrdinalIgnoreCase)) return;
        _blameRequestVersion++;
        _blameCancellation?.Cancel();
        _blameCancellation = null;
        _blameRequestedPath = null;
    }

    internal void RequestRefresh()
    {
        if (_operationRunning)
        {
            _refreshPending = true;
            return;
        }

        if (!_disposed && _historyService is not null && _repository is not null)
        {
            _ = RefreshPageAsync();
        }
    }

    private void InvalidateHistoryQuery()
    {
        CancelPendingBlame();
        _refreshVersion++;
        _detailsVersion++;
        _detailsLoadingHash = null;
        _pendingFileListView = null;
        _appendNextPage = false;
        _requestedHistoryPage = null;
        _hasLoadedHistoryPage = false;
        if (_operationIsHistoryQuery) _operationCancellation?.Cancel();
    }

    internal bool HandleShortcut(NativeMethods.Message message)
    {
        if (_fileHistoryComparison?.HandleShortcut(message) == true) return true;
        if (_disposed
            || message.MessageId != NativeMethods.WindowMessageKeyDown
            || unchecked((int)message.WordParameter) != NativeMethods.VirtualKeyEnter
            || message.Window != NativeMethods.GetFocus()
            || !NativeMethods.IsWindowVisible(message.Window)
            || !NativeMethods.IsWindowEnabled(message.Window)
            || NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) < 0
            || NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0
            || NativeMethods.GetKeyState(0x12) < 0)
        {
            return false;
        }

        if (message.Window == _filterEdit)
        {
            ApplyFilter();
            return true;
        }
        if (IsToolbarButton(message.Window))
        {
            _ = NativeMethods.SendMessage(message.Window, 0x00F5, 0, 0);
            return true;
        }
        return false;
    }

    private bool IsToolbarButton(nint control) => control == _backButton
        || control == _createReferenceButton || control == _deleteReferenceButton
        || control == _refreshButton || control == _searchButton || control == _compareButton
        || control == _locateHeadButton || control == _toolbarOverflowButton
        || control == _filterKindButton || control == _filterAuthorButton || control == _filterDateButton
        || control == _filterPathButton || control == _filterOverflowButton
        || control == _toggleDetailsButton || control == _filterSearchButton
        || control == _fileHistoryClearButton || control == _fileHistoryButton || control == _blameButton
        || control == _moreActionsButton || control == _closePanelButton || control == _cancelButton;

    internal bool ContainsWindow(nint window)
    {
        return NativeFocusNavigation.ContainsWindow(Handle, window);
    }

    internal bool HandleTabNavigation(bool backwards)
    {
        if (_fileHistoryMode)
        {
            return NativeFocusNavigation.MoveWithinRegion(
                [_fileHistoryClearButton, _refreshButton, _compareButton, _filterSearchButton,
                    _toggleDetailsButton, _historyList, .. _fileHistoryComparison?.FocusTargets ?? [],
                    _moreActionsButton, _closePanelButton], NativeMethods.GetFocus(), backwards);
        }
        return NativeFocusNavigation.MoveWithinRegion(
            [
                _branchFilterEdit,
                _branchesList,
                _filterEdit,
                _filterKindButton,
                _filterAuthorButton,
                _filterDateButton,
                _filterPathButton,
                _filterOverflowButton,
                _toggleDetailsButton,
                _filterSearchButton,
                _historyList,
                _filesList,
                _fileHistoryButton,
                _blameButton,
                _metadataLabel,
                _backButton,
                _createReferenceButton,
                _deleteReferenceButton,
                _refreshButton,
                _searchButton,
                _compareButton,
                _locateHeadButton,
                _toolbarOverflowButton,
                _moreActionsButton,
                _closePanelButton,
            ],
            NativeMethods.GetFocus(),
            backwards);
    }

    internal void ApplyAppearance(ApplicationSettings? settings = null)
    {
        if (settings is not null) _settings = settings;
        _toolbarPopup?.Dispose();
        _rowTextMetricsDirty = true;
        if (Handle == 0)
        {
            return;
        }

        var listPositions = new[] { _branchesList, _historyList, _filesList }.Where(control => control != 0)
            .Select(control => (Control: control, Top: NativeMethods.SendMessage(control, NativeMethods.ListBoxGetTopIndex, 0, 0))).ToArray();
        int historyHorizontal = NativeMethods.GetScrollPosition(_historyList, 0);
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(Handle, dark);
        foreach (nint control in Controls)
        {
            NativeTheme.ApplyToControl(control, dark);
        }
        ApplyListRowHeights();
        _toolTip?.ApplyAppearance(dark);
        _fileHistoryComparison?.ApplyAppearance(_settings);
        if (_detailsBodyFont != 0)
        {
            _ = NativeMethods.DeleteObject(_detailsBodyFont);
        }
        _detailsBodyFont = NativeTheme.CreateOwnedUiFont(
            NativeFontResolver.ResolveMonospace(_settings.MonospaceFontFamily),
            _settings.FontSize,
            400);
        _detailsLayoutDirty = true;
        ApplyFilterCueBanners();

        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(dark).Panel);
        Layout();
        foreach (var position in listPositions)
            if (position.Top >= 0)
                _ = NativeMethods.SendMessage(position.Control, NativeMethods.ListBoxSetTopIndex, unchecked((nuint)position.Top), 0);
        _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageHorizontalScroll,
            unchecked((nuint)((historyHorizontal << 16) | 4)), 0);
        _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
    }

    private void ApplyListRowHeights()
    {
        // 字号改变后，所有 owner-draw 列表必须同步扩大行高，避免只刷新字体而裁切文字。
        if (_branchesList != 0)
            _ = NativeMethods.SendMessage(_branchesList, NativeMethods.ListBoxSetItemHeight, 0, BranchRowHeight);
        if (_historyList != 0)
            _ = NativeMethods.SendMessage(_historyList, NativeMethods.ListBoxSetItemHeight, 0, HistoryRowHeight);
        if (_filesList != 0)
            _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxSetItemHeight, 0, FileRowHeight);
    }

    private void ApplyFilterCueBanners()
    {
        // 原生编辑框切换 Explorer 主题后可能清除水印，主题应用完成后必须重新写入。
        if (_filterEdit != 0)
        {
            _ = NativeMethods.SendMessage(
                _filterEdit,
                NativeMethods.EditSetCueBanner,
                0,
                UiText.HistoryTextOrHash);
        }

        if (_branchFilterEdit != 0)
        {
            _ = NativeMethods.SendMessage(
                _branchFilterEdit,
                NativeMethods.EditSetCueBanner,
                0,
                UiText.BranchOrTag);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _toolbarPopup?.Dispose();
        _contextMenu?.Dispose();
        _contextMenu = null;
        _toolTip?.Dispose();
        _toolTip = null;
        CancelPendingFileDiff();
        CancelPendingBlame();
        _lifetimeCancellation.Cancel();
        CancelFileHistoryPreview();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _fileHistoryComparison?.Dispose();
        _fileHistoryComparison = null;
        if (_branchesList != 0)
        {
            _ = NativeMethods.RemoveWindowSubclass(
                _branchesList,
                BranchesListProcedure,
                BranchesListSubclassIdentifier);
            lock (InstancesGate)
            {
                BranchesListInstances.Remove(_branchesList);
            }
        }
        if (_historyList != 0)
        {
            _ = NativeMethods.RemoveWindowSubclass(
                _historyList,
                HistoryListProcedure,
                HistoryListSubclassIdentifier);
            lock (InstancesGate)
            {
                HistoryListInstances.Remove(_historyList);
            }
        }
        if (_filesList != 0)
        {
            _ = NativeMethods.RemoveWindowSubclass(
                _filesList,
                FilesListProcedure,
                FilesListSubclassIdentifier);
            lock (InstancesGate)
            {
                FilesListInstances.Remove(_filesList);
            }
        }
        DetachDetailsScrolling();
        DisposeMetadataWatcher();
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }
        if (_detailsBodyFont != 0)
        {
            _ = NativeMethods.DeleteObject(_detailsBodyFont);
            _detailsBodyFont = 0;
        }
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
        _titleLabel,
        _pageLabel,
        _fileHistoryTab,
        _backButton,
        _createReferenceButton,
        _deleteReferenceButton,
        _refreshButton,
        _searchButton,
        _locateHeadButton,
        _toolbarOverflowButton,
        _previousButton,
        _nextButton,
        _filterKindButton,
        _filterAuthorButton,
        _filterDateButton,
        _filterPathButton,
        _filterOverflowButton,
        _filterEdit,
        _branchFilterEdit,
        _filterSearchButton,
        _toggleDetailsButton,
        _fileHistoryBranchLabel,
        _fileHistoryClearButton,
        _branchesList,
        _applyFilterButton,
        _clearFilterButton,
        _compareButton,
        _referencesButton,
        _stateButton,
        _worktreesButton,
        _moreActionsButton,
        _closePanelButton,
        _cancelButton,
        _historyList,
        _metadataLabel,
        _filesList,
        _detailsTabLabel,
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
            case NativeMethods.WindowMessageDrawItem:
                return instance.DrawControl(longParameter) ? 1 : 0;
            case NativeMethods.WindowMessageEraseBackground:
                return instance.PaintBackground(unchecked((nint)wordParameter));
            case NativeMethods.WindowMessageControlColorEdit:
            case NativeMethods.WindowMessageControlColorListBox:
            case NativeMethods.WindowMessageControlColorButton:
                return instance.ApplyControlColor(unchecked((nint)wordParameter));
            case WindowMessageRefresh:
                instance.RequestRefresh();
                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private static nint HandleBranchesListMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        NativeGitHistoryPanel? instance;
        lock (InstancesGate)
        {
            BranchesListInstances.TryGetValue(window, out instance);
        }

        if (instance is not null
            && instance.TryHandleBranchesListInput(message, wordParameter, longParameter))
        {
            return 0;
        }

        nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (instance is not null
            && message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
        {
            _ = NativeMethods.InvalidateRectangle(window, 0, true);
        }
        return result;
    }

    private static nint HandleHistoryListMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        NativeGitHistoryPanel? instance;
        lock (InstancesGate)
        {
            HistoryListInstances.TryGetValue(window, out instance);
        }

        if (instance is not null
            && message == NativeMethods.WindowMessageContextMenu
            && instance.ShowHistoryContextMenu(longParameter))
        {
            return 0;
        }

        nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (instance is not null
            && message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
        {
            _ = NativeMethods.InvalidateRectangle(window, 0, true);
        }
        if (instance is not null && (message is NativeMethods.WindowMessageVerticalScroll or NativeMethods.WindowMessageMouseWheel
            || message == NativeMethods.WindowMessageKeyDown && unchecked((int)wordParameter) is
                NativeMethods.VirtualKeyDown or NativeMethods.VirtualKeyUp or 0x21 or 0x22 or 0x23 or 0x24))
        {
            instance.RequestNextPageWhenAtBottom();
        }
        if (instance is not null && message == NativeMethods.WindowMessageSize)
            instance.UpdateHistoryRowExtent();

        return result;
    }

    private static nint HandleFilesListMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        NativeGitHistoryPanel? instance;
        lock (InstancesGate)
        {
            FilesListInstances.TryGetValue(window, out instance);
        }

        if (instance is not null
            && instance.TryHandleFilesListInput(message, wordParameter, longParameter))
        {
            return 0;
        }

        nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (instance is not null
            && message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
        {
            _ = NativeMethods.InvalidateRectangle(window, 0, true);
        }

        return result;
    }

    private bool TryHandleFilesListInput(uint message, nuint wordParameter, nint longParameter)
    {
        if (message == NativeMethods.WindowMessageContextMenu)
            return ShowFilesContextMenu(longParameter);
        if (message == NativeMethods.WindowMessageKeyDown
            && unchecked((int)wordParameter) is NativeMethods.VirtualKeySpace or NativeMethods.VirtualKeyEnter)
        {
            FileTreeRow? selected = GetSelectedFileRow();
            if (selected?.Group == true)
            {
                ToggleFileGroup(selected);
                return true;
            }

            if (unchecked((int)wordParameter) == NativeMethods.VirtualKeyEnter
                && selected?.File is not null)
            {
                ActivateSelectedFileDiff();
                return true;
            }

            return false;
        }

        if (message is not (NativeMethods.WindowMessageLeftButtonDown
            or NativeMethods.WindowMessageLeftButtonDoubleClick))
        {
            return false;
        }

        nuint hit = unchecked((nuint)NativeMethods.SendMessage(
            _filesList,
            NativeMethods.ListBoxItemFromPoint,
            0,
            longParameter));
        int index = NativeMethods.LowWord(hit);
        if (NativeMethods.HighWord(hit) != 0
            || index < 0
            || index >= _fileRows.Count
            || !_fileRows[index].Group)
        {
            return false;
        }

        NativeMethods.Rectangle rectangle = default;
        if (NativeMethods.SendMessage(
                _filesList,
                NativeMethods.ListBoxGetItemRectangle,
                unchecked((nuint)index),
                ref rectangle) == unchecked((nint)(-1)))
        {
            return false;
        }

        int x = unchecked((short)NativeMethods.LowWord(unchecked((nuint)longParameter)));
        int y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)longParameter)));
        int chevronLeft = rectangle.Left + NativeTheme.Scale(10 + _fileRows[index].Indent * 18);
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        bool chevronHit = x >= chevronLeft - NativeTheme.Scale(4)
            && x <= chevronLeft + NativeTheme.Scale(12)
            && y >= centerY - NativeTheme.Scale(8)
            && y <= centerY + NativeTheme.Scale(8);
        if (message == NativeMethods.WindowMessageLeftButtonDown && !chevronHit)
        {
            return false;
        }

        if (message == NativeMethods.WindowMessageLeftButtonDoubleClick || chevronHit)
        {
            ToggleFileGroup(_fileRows[index]);
            return true;
        }

        return false;
    }

    private bool ShowHistoryContextMenu(nint longParameter)
    {
        int x = unchecked((short)NativeMethods.LowWord(unchecked((nuint)longParameter)));
        int y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)longParameter)));
        int index = -1;
        if (x == -1 && y == -1)
        {
            index = GetListSelection(_historyList);
            if (!TryGetListItemScreenPoint(_historyList, index, out x, out y))
            {
                return true;
            }
        }
        else
        {
            NativeMethods.Point point = new() { X = x, Y = y };
            if (!NativeMethods.ScreenToClient(_historyList, ref point))
            {
                return true;
            }

            nuint hit = unchecked((nuint)NativeMethods.SendMessage(
                _historyList,
                NativeMethods.ListBoxItemFromPoint,
                0,
                PackPoint(point.X, point.Y)));
            if (NativeMethods.HighWord(hit) != 0)
            {
                return true;
            }

            index = NativeMethods.LowWord(hit);
        }

        if (index < 0 || index >= _entries.Count)
        {
            return true;
        }

        _ = NativeMethods.SendMessage(
            _historyList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        GitHistoryEntry entry = _entries[index];
        _contextMenu?.Dispose();
        bool busy = _operationRunning;
        _contextMenu = NativeContextMenu.Show(
            Handle,
            x,
            y,
            [
                new(UiText.CopyCommitHash, NativeContextMenuIcon.Copy, () => HandleHistoryContextCommand(MenuCopyCommitHash, entry)),
                new(UiText.CherryPick, NativeContextMenuIcon.CherryPick, () => HandleHistoryContextCommand(MenuCherryPick, entry), !busy),
                null,
                new(UiText.CompareWithWorkspace, NativeContextMenuIcon.None, () => HandleHistoryContextCommand(MenuCompareWithWorkspace, entry)),
                new(UiText.ResetCurrentBranchHere, NativeContextMenuIcon.Reset, () => HandleHistoryContextCommand(MenuResetCurrentBranch, entry), !busy),
                new(UiText.RevertCommit, NativeContextMenuIcon.None, () => HandleHistoryContextCommand(MenuRevertCommit, entry), !busy),
                null,
                new(UiText.NewBranch, NativeContextMenuIcon.None, () => HandleHistoryContextCommand(MenuCreateBranch, entry), !busy),
                new(UiText.NewTag, NativeContextMenuIcon.None, () => HandleHistoryContextCommand(MenuCreateTag, entry), !busy),
            ],
            NativeTheme.IsDark(_settings.Theme));

        return true;
    }

    private static bool TryGetListItemScreenPoint(nint list, int index, out int x, out int y)
    {
        x = 0;
        y = 0;
        NativeMethods.Rectangle rectangle = default;
        if (index < 0
            || NativeMethods.SendMessage(
                list,
                NativeMethods.ListBoxGetItemRectangle,
                unchecked((nuint)index),
                ref rectangle) == unchecked((nint)(-1)))
        {
            return false;
        }

        NativeMethods.Point point = new()
        {
            X = rectangle.Left + NativeTheme.Scale(24),
            Y = Math.Max(rectangle.Top, rectangle.Bottom - 1),
        };
        if (!NativeMethods.ClientToScreen(list, ref point))
        {
            return false;
        }

        x = point.X;
        y = point.Y;
        return true;
    }

    private static nint PackPoint(int x, int y)
    {
        return unchecked((nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF)));
    }

    private void HandleHistoryContextCommand(uint command, GitHistoryEntry entry)
    {
        switch (command)
        {
            case MenuCopyCommitHash:
                _setStatus(NativeClipboard.TrySetText(Handle, entry.FullHash)
                    ? UiText.PathCopied
                    : UiText.ClipboardUnavailable);
                break;
            case MenuCherryPick:
                _ = StartHistoryOperationAsync(GitAdvancedOperationKind.CherryPick, entry.FullHash);
                break;
            case MenuCompareWithWorkspace:
                _ = CompareCommitWithWorkspaceAsync(entry.FullHash);
                break;
            case MenuResetCurrentBranch:
                ShowReset(entry.FullHash);
                break;
            case MenuRevertCommit:
                _ = StartHistoryOperationAsync(GitAdvancedOperationKind.Revert, entry.FullHash);
                break;
            case MenuCreateBranch:
                ShowReferenceManagement(entry.FullHash);
                break;
            case MenuCreateTag:
                ShowReferenceManagement(entry.FullHash);
                break;
        }
    }

    private bool TryHandleBranchesListInput(uint message, nuint wordParameter, nint longParameter)
    {
        if (message == NativeMethods.WindowMessageKeyDown
            && unchecked((int)wordParameter) is NativeMethods.VirtualKeySpace or NativeMethods.VirtualKeyEnter)
        {
            int selectedIndex = GetListSelection(_branchesList);
            if (selectedIndex >= 0
                && selectedIndex < _branchRows.Count
                && _branchRows[selectedIndex].Group)
            {
                ToggleBranchSection(_branchRows[selectedIndex].Text);
                return true;
            }
            return false;
        }

        if (message is not (NativeMethods.WindowMessageLeftButtonDown
            or NativeMethods.WindowMessageLeftButtonDoubleClick))
        {
            return false;
        }

        nuint hit = unchecked((nuint)NativeMethods.SendMessage(
            _branchesList,
            NativeMethods.ListBoxItemFromPoint,
            0,
            longParameter));
        int index = NativeMethods.LowWord(hit);
        if (NativeMethods.HighWord(hit) != 0
            || index < 0
            || index >= _branchRows.Count
            || !_branchRows[index].Group)
        {
            return false;
        }

        NativeMethods.Rectangle rectangle = default;
        if (NativeMethods.SendMessage(
                _branchesList,
                NativeMethods.ListBoxGetItemRectangle,
                unchecked((nuint)index),
                ref rectangle) == unchecked((nint)(-1)))
        {
            return false;
        }

        int x = unchecked((short)NativeMethods.LowWord(unchecked((nuint)longParameter)));
        int y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)longParameter)));
        int chevronLeft = rectangle.Left + NativeTheme.Scale(10);
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        bool chevronHit = x >= chevronLeft - NativeTheme.Scale(4)
            && x <= chevronLeft + NativeTheme.Scale(12)
            && y >= centerY - NativeTheme.Scale(8)
            && y <= centerY + NativeTheme.Scale(8);
        if (message == NativeMethods.WindowMessageLeftButtonDown)
        {
            if (!chevronHit)
            {
                return false;
            }

            ToggleBranchSection(_branchRows[index].Text);
            return true;
        }

        if (chevronHit)
        {
            return true;
        }

        ToggleBranchSection(_branchRows[index].Text);
        return true;
    }

    private nint ApplyControlColor(nint deviceContext)
    {
        if (deviceContext == 0 || _controlBrush == 0)
        {
            return 0;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint background = palette.Panel;
        uint text = palette.Text;
        _ = NativeMethods.SetBackgroundColor(deviceContext, background);
        _ = NativeMethods.SetTextColor(deviceContext, text);
        return _controlBrush;
    }

    private void CreateControls()
    {
        _titleLabel = CreateControl(NativeMethods.StaticClass, "Git", 62, NativeMethods.StaticOwnerDraw);
        _pageLabel = CreateControl(NativeMethods.StaticClass, UiText.GitLog, 60,
            NativeMethods.StaticOwnerDraw | NativeMethods.StaticNotify);
        _fileHistoryTab = CreateControl(NativeMethods.StaticClass, string.Empty, 66, NativeMethods.StaticOwnerDraw);
        _backButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandBack, NativeMethods.ButtonPushButton);
        _createReferenceButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandCreateReference,
            NativeMethods.ButtonPushButton);
        _deleteReferenceButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandDeleteReference,
            NativeMethods.ButtonPushButton);
        _refreshButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandRefresh, NativeMethods.ButtonPushButton);
        _searchButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandSearch, NativeMethods.ButtonPushButton);
        _locateHeadButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandLocateHead,
            NativeMethods.ButtonPushButton);
        _previousButton = CreateControl(NativeMethods.ButtonClass, "‹", CommandPreviousPage, NativeMethods.ButtonPushButton);
        _toolbarOverflowButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandToolbarOverflow, NativeMethods.ButtonPushButton);
        _nextButton = CreateControl(NativeMethods.ButtonClass, "›", CommandNextPage, NativeMethods.ButtonPushButton);
        _filterKindButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.FilterBranch,
            CommandChooseFilterKind,
            NativeMethods.ButtonPushButton);
        _filterAuthorButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.FilterUser,
            CommandFilterAuthor,
            NativeMethods.ButtonPushButton);
        _filterDateButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.FilterDate,
            CommandFilterDate,
            NativeMethods.ButtonPushButton);
        _filterPathButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.FilterPath,
            CommandFilterPath,
            NativeMethods.ButtonPushButton);
        _filterOverflowButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandFilterOverflow,
            NativeMethods.ButtonPushButton);
        _filterEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            FilterEditIdentifier,
            NativeMethods.EditAutoHorizontalScroll);
        _ = NativeMethods.SendMessage(
            _filterEdit,
            NativeMethods.EditSetMargins,
            NativeMethods.EditMarginLeftRight,
            unchecked((nint)0x00020002));
        _ = NativeMethods.SendMessage(
            _filterEdit,
            NativeMethods.EditSetCueBanner,
            1,
            UiText.HistoryTextOrHash);
        _branchFilterEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            BranchFilterEditIdentifier,
            NativeMethods.EditAutoHorizontalScroll);
        _ = NativeMethods.SendMessage(
            _branchFilterEdit,
            NativeMethods.EditSetMargins,
            NativeMethods.EditMarginLeftRight,
            unchecked((nint)0x00020002));
        _ = NativeMethods.SendMessage(
            _branchFilterEdit,
            NativeMethods.EditSetCueBanner,
            1,
            UiText.BranchOrTag);
        _toggleDetailsButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandToggleDetails,
            NativeMethods.ButtonPushButton);
        _filterSearchButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandSearch,
            NativeMethods.ButtonPushButton);
        _fileHistoryBranchLabel = CreateControl(
            NativeMethods.StaticClass,
            "分支: HEAD",
            67,
            NativeMethods.StaticOwnerDraw);
        _fileHistoryClearButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandFileHistoryClear,
            NativeMethods.ButtonPushButton);
        _branchesList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            BranchesListIdentifier,
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight);
        _ = NativeMethods.SendMessage(_branchesList, NativeMethods.ListBoxSetItemHeight, 0, BranchRowHeight);
        lock (InstancesGate)
        {
            BranchesListInstances.Add(_branchesList, this);
        }
        if (!NativeMethods.SetWindowSubclass(
                _branchesList,
                BranchesListProcedure,
                BranchesListSubclassIdentifier,
                0))
        {
            lock (InstancesGate)
            {
                BranchesListInstances.Remove(_branchesList);
            }
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.HistoryBranchesListSubclassFailed);
        }
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
            string.Empty,
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
        _moreActionsButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandMoreActions,
            NativeMethods.ButtonPushButton);
        _closePanelButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandClosePanel,
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
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.WindowStyleHorizontalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight);
        _ = NativeMethods.SendMessage(_historyList, NativeMethods.ListBoxSetItemHeight, 0, HistoryRowHeight);
        lock (InstancesGate)
        {
            HistoryListInstances.Add(_historyList, this);
        }
        if (!NativeMethods.SetWindowSubclass(
                _historyList,
                HistoryListProcedure,
                HistoryListSubclassIdentifier,
                0))
        {
            lock (InstancesGate)
            {
                HistoryListInstances.Remove(_historyList);
            }
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.HistoryControlCreateFailed);
        }
        _metadataLabel = CreateControl(
            NativeMethods.StaticClass,
            UiText.SelectCommit,
            61,
            NativeMethods.StaticOwnerDraw | NativeMethods.WindowStyleTabStop | 0x00000100);
        AttachDetailsScrolling();
        _filesList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            FilesListIdentifier,
            NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight);
        _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxSetItemHeight, 0, FileRowHeight);
        lock (InstancesGate)
        {
            FilesListInstances.Add(_filesList, this);
        }
        if (!NativeMethods.SetWindowSubclass(
                _filesList,
                FilesListProcedure,
                FilesListSubclassIdentifier,
                0))
        {
            lock (InstancesGate)
            {
                FilesListInstances.Remove(_filesList);
            }
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.HistoryControlCreateFailed);
        }
        _detailsTabLabel = CreateControl(
            NativeMethods.StaticClass,
            UiText.CommitDetails,
            65,
            NativeMethods.StaticOwnerDraw);
        _fileHistoryButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.FileHistory,
            CommandFileHistory,
            NativeMethods.ButtonPushButton);
        _blameButton = CreateControl(NativeMethods.ButtonClass, UiText.Blame, CommandBlame, NativeMethods.ButtonPushButton);
        _ = NativeMethods.ShowWindow(_referencesButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_stateButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_worktreesButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_previousButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_nextButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_applyFilterButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_clearFilterButton, NativeMethods.ShowNormal);
        SetHistoryControlsEnabled(false);
    }

    private void CreateToolTips()
    {
        _toolTip = new NativeToolTip(Handle);
        _toolTip.Add(_backButton, UiText.ReturnFromGitHistory);
        _toolTip.Add(_createReferenceButton, UiText.CreateReference);
        _toolTip.Add(_deleteReferenceButton, UiText.DeleteReference);
        _toolTip.Add(_refreshButton, UiText.Refresh);
        _toolTip.Add(_searchButton, UiText.SearchGitHistory);
        _toolTip.Add(_compareButton, UiText.CompareReferences);
        _toolTip.Add(_locateHeadButton, UiText.LocateHead);
        _toolTip.Add(_toolbarOverflowButton, UiText.MoreHistoryTools);
        _toolTip.Add(_previousButton, UiText.PreviousPage);
        _toolTip.Add(_nextButton, UiText.NextPage);
        _toolTip.Add(_moreActionsButton, UiText.MoreActions);
        _toolTip.Add(_closePanelButton, UiText.Close);
        _toolTip.Add(_filterKindButton, UiText.FilterBranch);
        _toolTip.Add(_filterAuthorButton, UiText.FilterAuthor);
        _toolTip.Add(_filterDateButton, UiText.FilterDate);
        _toolTip.Add(_filterPathButton, UiText.FilterFile);
        _toolTip.Add(_filterOverflowButton, UiText.MoreHistoryFilters);
        _toolTip.Add(_filterSearchButton, UiText.HistorySearch);
        _toolTip.Add(_toggleDetailsButton, UiText.HideCommitDetails);
        _toolTip.Add(_fileHistoryClearButton, UiText.ClearFileHistory);
    }

    private nint CreateControl(string className, string text, int identifier, uint specificStyle)
    {
        uint controlStyle = className.Equals(NativeMethods.ButtonClass, StringComparison.Ordinal)
            && specificStyle == NativeMethods.ButtonPushButton
            ? NativeMethods.ButtonOwnerDraw
            : specificStyle;
        bool acceptsFocus = className.Equals(NativeMethods.ButtonClass, StringComparison.Ordinal)
            || className.Equals(NativeMethods.EditClass, StringComparison.Ordinal)
            || className.Equals(NativeMethods.ListBoxClass, StringComparison.Ordinal)
            || className.Equals(NativeMethods.ComboBoxClass, StringComparison.Ordinal);
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | (acceptsFocus ? NativeMethods.WindowStyleTabStop : 0)
                | controlStyle,
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

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
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
            _historyService = new GitHistoryService(_runtime);
            _referenceService = new(_runtime);
            _stateService = new(_runtime);
            _worktreeService = new(_runtime);
            _operationService = new(_runtime);
            _conflictService = new(_runtime);
            _metadataWatcher = new(_repository);
            _metadataWatcher.Changed += OnGitMetadataChanged;
            SetHistoryControlsEnabled(true);
            PopulateBranches(_referenceSnapshot);
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
        if (command == 60 && notification == 0 && _fileHistoryMode)
        {
            ReturnFromHistory();
            return;
        }
        if (command == HistoryListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            if (!_updatingHistoryList)
            {
                _ = LoadSelectedCommitAsync();
            }
            return;
        }

        if (command == FilesListIdentifier
            && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            if (_fileDiffRequestKey is { } key
                && !string.Equals(GetSelectedFile()?.RelativePath, key.Path, StringComparison.OrdinalIgnoreCase))
            {
                CancelPendingFileDiff();
            }
            if (_followFileComparison && !_fileHistoryMode)
                _ = LoadSelectedFileDiffAsync(activate: false);
            return;
        }

        if (command == FilesListIdentifier
            && notification == NativeMethods.ListBoxNotificationDoubleClick)
        {
            ActivateSelectedFileDiff();
            return;
        }

        if (command == BranchesListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            if (_updatingBranchesList)
            {
                return;
            }

            int index = GetListSelection(_branchesList);
            if (index >= 0 && index < _branchRows.Count)
            {
                if (!_branchRows[index].Group)
                {
                    ApplyBranchFilter(_branchRows[index]);
                    RequestRefresh();
                }
            }
            return;
        }

        if (command == BranchFilterEditIdentifier && notification == EditNotificationChanged)
        {
            PopulateBranches(_referenceSnapshot);
            return;
        }

        switch (command)
        {
            case CommandBack:
                ReturnFromHistory();
                break;
            case CommandCreateReference:
            case CommandDeleteReference:
                ShowReferenceManagement();
                break;
            case CommandRefresh:
                RequestRefresh();
                break;
            case CommandSearch:
                _ = NativeMethods.SetFocus(_fileHistoryMode ? _historyList : _filterEdit);
                break;
            case CommandToggleDetails:
                _showDetails = !_showDetails;
                if (_fileHistoryMode && !_showDetails) CancelFileHistoryPreview(keepLoaded: true);
                _toolTip?.Update(
                    _toggleDetailsButton,
                    _showDetails ? UiText.HideCommitDetails : UiText.ShowCommitDetails);
                Layout();
                if (_fileHistoryMode && _showDetails && _activeDetails is { } details)
                    _ = LoadFileHistoryPreviewAsync(details.Commit.FullHash);
                _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
                break;
            case CommandLocateHead:
                LocateHead();
                break;
            case CommandFilterAuthor:
                SelectFilterKind(2);
                break;
            case CommandFilterDate:
                ShowDateFilterMenu();
                break;
            case CommandFilterPath:
                SelectFilterKind(6);
                break;
            case CommandFilterOverflow:
                ShowFilterOverflow();
                break;
            case CommandToolbarOverflow:
                ShowToolbarOverflow();
                break;
            case CommandPreviousPage:
                if (_hasPreviousPage)
                {
                    _requestedHistoryPage = Math.Max(0, _page - 1);
                    RequestRefresh();
                }
                break;
            case CommandNextPage:
                _ = RequestNextPageAsync();
                break;
            case CommandApplyFilter:
                ApplyFilter();
                break;
            case CommandClearFilter:
                InvalidateHistoryQuery();
                _filter = new();
                _page = 0;
                _ = NativeMethods.SetWindowText(_filterEdit, string.Empty);
                RequestRefresh();
                break;
            case CommandChooseFilterKind:
                SelectFilterKind(5);
                break;
            case CommandCompare:
                _ = CompareReferencesAsync();
                break;
            case CommandReferences:
                ShowReferenceManagement();
                break;
            case CommandLocalState:
                ShowStashManagement();
                break;
            case CommandCreateStash:
                ShowCreateStash();
                break;
            case CommandManageStashes:
                ShowStashManagement();
                break;
            case CommandResetCurrentBranch:
                ShowReset();
                break;
            case CommandWorktrees:
                ShowWorktreeManagement();
                break;
            case CommandMoreActions:
                ShowManagementMenu();
                break;
            case CommandClosePanel:
                _closePanel();
                break;
            case CommandFileHistory:
                ShowFileHistory();
                break;
            case CommandFileHistoryClear:
                ReturnFromHistory();
                break;
            case CommandBlame:
                _ = ShowBlameAsync();
                break;
            case CommandCancel:
                _ = CancelOperationForHost();
                break;
        }
    }

    private void ReturnFromHistory()
    {
        CancelPendingFileDiff();
        if (!string.IsNullOrWhiteSpace(_filter.FilePath))
        {
            ExitFileHistoryView();
            if (_historyContextBeforeFileFilter is HistoryContextSnapshot snapshot)
            {
                RestoreHistoryContext(snapshot);
                _historyContextBeforeFileFilter = null;
                return;
            }

            _filter = _filter with { FilePath = null };
            InvalidateHistoryQuery();
            _page = 0;
            SelectFilterKind(0);
            _ = NativeMethods.SetWindowText(_fileHistoryTab, string.Empty);
            RequestRefresh();
            Layout();
            _ = NativeMethods.SetFocus(_historyList);
            return;
        }

        _closePanel();
    }

    private HistoryContextSnapshot CaptureHistoryContext()
    {
        int historyTopIndex = checked((int)NativeMethods.SendMessage(
            _historyList,
            NativeMethods.ListBoxGetTopIndex,
            0,
            0));
        return new(
            _filter,
            _filterKindIndex,
            _page,
            _entries.ToArray(),
            _hasPreviousPage,
            _hasNextPage,
            _selectedCommitHash,
            _activeDetails,
            _files.ToArray(),
            _collapsedFileGroups.ToArray(),
            _pendingFileListView ?? CaptureFileListView(),
            NativeMethods.GetWindowTextValue(_filterEdit),
            _showDetails,
            historyTopIndex,
            NativeMethods.GetScrollPosition(_historyList, 0),
            _detailsScrollPosition,
            _hasLoadedHistoryPage);
    }

    private void RestoreHistoryContext(HistoryContextSnapshot snapshot)
    {
        // 文件历史返回必须恢复进入前的 Git 历史上下文，不能把第二页或当前选中提交丢掉。
        InvalidateHistoryQuery();
        _refreshPending = false;
        _refreshQueued = false;
        _appendNextPage = false;
        _filter = snapshot.Filter;
        _filterKindIndex = snapshot.FilterKindIndex;
        _page = snapshot.Page;
        _hasPreviousPage = snapshot.HasPreviousPage;
        _hasNextPage = snapshot.HasNextPage;
        _hasLoadedHistoryPage = snapshot.HasLoadedPage;
        _selectedCommitHash = snapshot.SelectedCommitHash;
        _detailsLoadingHash = null;
        _activeDetails = snapshot.ActiveDetails;
        _showDetails = snapshot.ShowDetails;
        _toolTip?.Update(_toggleDetailsButton, _showDetails ? UiText.HideCommitDetails : UiText.ShowCommitDetails);
        _files.Clear();
        _files.AddRange(snapshot.Files);
        _collapsedFileGroups.Clear();
        foreach (string group in snapshot.CollapsedFileGroups)
        {
            _collapsedFileGroups.Add(group);
        }

        SelectFilterKind(_filterKindIndex);
        _ = NativeMethods.SetWindowText(_filterEdit, snapshot.FilterDraft);
        _updatingHistoryList = true;
        _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageSetRedraw, 0, 0);
        try
        {
            if (!HistoryEntriesEqual(_entries, snapshot.Entries)
                && !TryApplyHistoryListDelta(snapshot.Entries, snapshot.SelectedCommitHash))
            {
                _entries.Clear();
                _entries.AddRange(snapshot.Entries);
                RebuildHistoryGraph();
                _historyListResetCount++;
                _ = NativeMethods.SendMessage(_historyList, NativeMethods.ListBoxResetContent, 0, 0);
                foreach (GitHistoryEntry entry in _entries)
                {
                    _ = NativeMethods.SendMessage(_historyList, NativeMethods.ListBoxAddString,
                        0, FormatHistoryListEntry(entry));
                }
            }

            int selectedIndex = _selectedCommitHash is null
                ? -1
                : _entries.FindIndex(entry => entry.FullHash.Equals(
                    _selectedCommitHash,
                    StringComparison.OrdinalIgnoreCase));
            _ = NativeMethods.SendMessage(
                _historyList,
                NativeMethods.ListBoxSetCurrentSelection,
                unchecked((nuint)selectedIndex),
                0);
        }
        finally
        {
            _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageSetRedraw, 1, 0);
            _updatingHistoryList = false;
        }

        UpdateHistoryRowExtent();
        _ = NativeMethods.InvalidateRectangle(_historyList, 0, true);

        // 先恢复普通历史的几何，再恢复滚动；文件历史的列表宽度会错误地夹住原横向位置。
        Layout();

        if (_entries.Count > 0)
        {
            _ = NativeMethods.SendMessage(
                _historyList,
                NativeMethods.ListBoxSetTopIndex,
                unchecked((nuint)Math.Clamp(snapshot.HistoryTopIndex, 0, _entries.Count - 1)),
                0);
        }

        _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageHorizontalScroll,
            unchecked((nuint)((snapshot.HistoryHorizontalPosition << 16) | 4)), 0);

        PopulateFileRows(preserveSelection: false, snapshot.FileListView);

        if (_activeDetails is not null)
        {
            RenderCommitDetails(_activeDetails);
            ScrollDetails(snapshot.DetailsScrollPosition);
        }
        else
        {
            SetMetadataMessage(UiText.SelectCommit);
        }

        _ = NativeMethods.EnableWindow(_previousButton, _hasPreviousPage);
        _ = NativeMethods.EnableWindow(_nextButton, _hasNextPage);
        _setStatus(snapshot.HasLoadedPage ? UiText.HistoryReady : UiText.HistoryLoading);
        _ = NativeMethods.SetFocus(_historyList);
        if (!snapshot.HasLoadedPage)
        {
            // 从文件树首次进入时，原日志可能从未加载；空快照不能充当已完成的空结果。
            RequestRefresh();
            return;
        }
        if (_activeDetails is null && _selectedCommitHash is not null)
        {
            _pendingFileListView = snapshot.FileListView;
            _ = LoadSelectedCommitAsync();
        }
    }

    private void LocateHead()
    {
        int index = _branchRows.FindIndex(row => !row.Group && row.Accent);
        if (index < 0)
        {
            return;
        }

        _ = NativeMethods.SendMessage(
            _branchesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        ApplyBranchFilter(_branchRows[index]);
        RequestRefresh();
    }

    private void ShowFilterKindMenu()
    {
        nint anchor = NativeMethods.IsWindowVisible(_filterKindButton) ? _filterKindButton : _filterOverflowButton;
        if (!NativeMethods.GetWindowRectangle(anchor, out NativeMethods.Rectangle button))
        {
            return;
        }

        _contextMenu?.Dispose();
        List<NativeContextMenuItem?> items = [];
        for (int index = 0; index < FilterKindLabels.Length; index++)
        {
            int selectedIndex = index;
            items.Add(new(
                FilterKindLabels[index],
                NativeContextMenuIcon.Search,
                () => SelectFilterKind(selectedIndex),
                Checked: index == _filterKindIndex));
        }

        _contextMenu = NativeContextMenu.Show(
            Handle,
            button.Left,
            button.Bottom,
            items,
            NativeTheme.IsDark(_settings.Theme));
    }

    private void SelectFilterKind(int index)
    {
        _filterKindIndex = Math.Clamp(index, 0, FilterKindLabels.Length - 1);
        _ = NativeMethods.SetWindowText(_filterEdit, GetFilterValue(_filter, _filterKindIndex));
        foreach (nint button in new[] { _filterKindButton, _filterAuthorButton, _filterDateButton, _filterPathButton })
        {
            _ = NativeMethods.InvalidateRectangle(button, 0, true);
        }
        _ = NativeMethods.SetFocus(_filterEdit);
    }

    private void ShowDateFilterMenu()
    {
        nint anchor = NativeMethods.IsWindowVisible(_filterDateButton) ? _filterDateButton : _filterOverflowButton;
        if (!NativeMethods.GetWindowRectangle(anchor, out NativeMethods.Rectangle button))
        {
            return;
        }

        _contextMenu?.Dispose();
        _contextMenu = NativeContextMenu.Show(
            Handle,
            button.Left,
            button.Bottom,
            [
                new(UiText.FilterSince, NativeContextMenuIcon.Search, () => SelectFilterKind(3), Checked: _filterKindIndex == 3),
                new(UiText.FilterUntil, NativeContextMenuIcon.Search, () => SelectFilterKind(4), Checked: _filterKindIndex == 4),
            ],
            NativeTheme.IsDark(_settings.Theme));
    }

    private (nint Control, int Command, string Label)[] GetFilterButtons() =>
    [
        (_filterKindButton, CommandChooseFilterKind, UiText.FilterBranch),
        (_filterAuthorButton, CommandFilterAuthor, UiText.FilterUser),
        (_filterDateButton, CommandFilterDate, UiText.FilterDate),
        (_filterPathButton, CommandFilterPath, UiText.FilterPath),
    ];

    private void ShowFilterOverflow()
    {
        if (_fileHistoryMode || !NativeMethods.IsWindowVisible(_filterOverflowButton)
            || !NativeMethods.GetWindowRectangle(_filterOverflowButton, out NativeMethods.Rectangle anchor)) return;
        _contextMenu?.Dispose();
        List<NativeContextMenuItem?> items = [];
        foreach (var button in GetFilterButtons().Skip(_filterLayout.VisibleFilterCount))
        {
            int command = button.Command;
            items.Add(new(button.Label, NativeContextMenuIcon.Search,
                () => HandleCommand(unchecked((nuint)command)),
                Enabled: NativeMethods.IsWindowEnabled(button.Control),
                Checked: IsFilterKindActive(command, _filterKindIndex)));
        }
        _contextMenu = NativeContextMenu.Show(Handle, anchor.Left, anchor.Bottom, items, NativeTheme.IsDark(_settings.Theme));
    }

    private (nint Control, int Command, string Label)[] GetSideToolbarButtons() =>
    [
        (_backButton, CommandBack, UiText.ReturnFromGitHistory),
        (_createReferenceButton, CommandCreateReference, UiText.CreateReference),
        (_deleteReferenceButton, CommandDeleteReference, UiText.DeleteReference),
        (_refreshButton, CommandRefresh, UiText.Refresh),
        (_searchButton, CommandSearch, UiText.SearchGitHistory),
        (_compareButton, CommandCompare, UiText.CompareReferences),
        (_locateHeadButton, CommandLocateHead, UiText.LocateHead),
    ];

    private void ShowToolbarOverflow()
    {
        if (_fileHistoryMode || !NativeMethods.IsWindowVisible(_toolbarOverflowButton)
            || !NativeMethods.GetWindowRectangle(_toolbarOverflowButton, out NativeMethods.Rectangle anchor)) return;
        _toolbarPopup?.Dispose();
        _contextMenu?.Dispose();
        // PyCharm 的溢出弹层横向展示整组动作，返回按钮仍留在原工具栏。
        NativeToolbarPopupItem[] items = GetSideToolbarButtons().Skip(1).Select(button => new NativeToolbarPopupItem(
            button.Label, () => HandleCommand(unchecked((nuint)button.Command)),
            (dc, rectangle, color) => DrawHeaderIcon(dc, button.Command, (rectangle.Left + rectangle.Right) / 2, (rectangle.Top + rectangle.Bottom) / 2, color),
            NativeMethods.IsWindowEnabled(button.Control))).ToArray();
        _toolbarPopup = new(Handle, anchor, items, NativeTheme.IsDark(_settings.Theme));
    }

    private void ShowManagementMenu()
    {
        if (!NativeMethods.GetWindowRectangle(_moreActionsButton, out NativeMethods.Rectangle button))
        {
            return;
        }

        _contextMenu?.Dispose();
        _contextMenu = NativeContextMenu.Show(
            Handle,
            button.Right - NativeTheme.Scale(180),
            button.Bottom,
            [
                new(UiText.PreviousPage, NativeContextMenuIcon.Open, () => HandleCommand(CommandPreviousPage), Enabled: _hasPreviousPage),
                new(UiText.NextPage, NativeContextMenuIcon.Open, () => HandleCommand(CommandNextPage), Enabled: _hasNextPage),
                null,
                new(UiText.CompareReferences, NativeContextMenuIcon.Compare, () => HandleCommand(CommandCompare)),
                new(UiText.ManageReferences, NativeContextMenuIcon.BranchPlus, () => HandleCommand(CommandReferences)),
                new(UiText.CreateStashMenu, NativeContextMenuIcon.Open, () => HandleCommand(CommandCreateStash)),
                new(UiText.ManageStashes, NativeContextMenuIcon.Open, () => HandleCommand(CommandManageStashes)),
                new(UiText.ResetCurrentBranch, NativeContextMenuIcon.Reset, () => HandleCommand(CommandResetCurrentBranch)),
                new(UiText.ManageWorktrees, NativeContextMenuIcon.Open, () => HandleCommand(CommandWorktrees)),
            ],
            NativeTheme.IsDark(_settings.Theme));
    }

    private void ApplyFilter()
    {
        string value = NativeMethods.GetWindowTextValue(_filterEdit).Trim();
        GitHistoryFilter? filter = _filterKindIndex == 0
            ? UpdateTextOrHashFilter(_filter, value)
            : UpdateFilter(_filter, _filterKindIndex, value);
        if (filter is null)
        {
            ShowError(UiText.InvalidDateFilter);
            return;
        }

        _filter = filter;
        InvalidateHistoryQuery();
        _page = 0;
        RequestRefresh();
    }

    internal static GitHistoryFilter UpdateTextOrHashFilterForTest(GitHistoryFilter current, string value)
    {
        return UpdateTextOrHashFilter(current, value);
    }

    private static GitHistoryFilter UpdateTextOrHashFilter(GitHistoryFilter current, string value)
    {
        ArgumentNullException.ThrowIfNull(current);
        string? normalized = NullIfEmpty(value.Trim());
        bool hash = normalized is not null
            && normalized.Length is >= 4 and <= 40
            && normalized.All(character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F');
        return hash
            ? current with { Message = null, Hash = normalized }
            : current with { Message = normalized, Hash = null };
    }

    private static GitHistoryFilter? UpdateFilter(
        GitHistoryFilter current,
        int filterKindIndex,
        string value)
    {
        ArgumentNullException.ThrowIfNull(current);
        string? normalized = NullIfEmpty(value.Trim());
        return filterKindIndex switch
        {
            0 => current with { Message = normalized, Hash = null },
            1 => current with { Message = null, Hash = normalized },
            2 => current with { Author = normalized },
            3 => normalized is null
                ? current with { Since = null }
                : TryParseDate(normalized, out DateTimeOffset since)
                    ? current with { Since = since }
                    : null,
            4 => normalized is null
                ? current with { Until = null }
                : TryParseDate(normalized, out DateTimeOffset until)
                    ? current with { Until = until }
                    : null,
            5 => current with { Branch = normalized },
            6 => current with { FilePath = normalized?.Replace('\\', '/') },
            _ => current,
        };
    }

    private static string GetFilterValue(GitHistoryFilter filter, int filterKindIndex)
    {
        return filterKindIndex switch
        {
            0 => filter.Message ?? string.Empty,
            1 => filter.Hash ?? string.Empty,
            2 => filter.Author ?? string.Empty,
            3 => filter.Since?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            4 => filter.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            5 => filter.Branch ?? string.Empty,
            6 => filter.FilePath ?? string.Empty,
            _ => string.Empty,
        };
    }

    private async Task RequestNextPageAsync()
    {
        if (_disposed || _operationRunning || !_hasNextPage)
        {
            return;
        }

        _appendNextPage = true;
        await RefreshPageAsync();
    }

    private void RequestNextPageWhenAtBottom()
    {
        if (_disposed || _operationRunning || !_hasNextPage || _entries.Count == 0
            || !NativeMethods.GetClientRectangle(_historyList, out NativeMethods.Rectangle client))
        {
            return;
        }

        NativeMethods.Rectangle last = default;
        if (NativeMethods.SendMessage(
                _historyList,
                NativeMethods.ListBoxGetItemRectangle,
                unchecked((nuint)(_entries.Count - 1)),
                ref last) == unchecked((nint)(-1)))
        {
            return;
        }

        if (last.Bottom <= client.Bottom + HistoryRowHeight)
        {
            _ = RequestNextPageAsync();
        }
    }

    private Task RefreshPageAsync()
    {
        if (_refreshTask is { IsCompleted: false })
        {
            _refreshQueued = true;
            return _refreshTask;
        }

        _refreshTask = RefreshPageCoreAsync();
        return _refreshTask;
    }

    private async Task RefreshPageCoreAsync()
    {
        if (_disposed || _historyService is null || _repository is null)
        {
            return;
        }

        int version = ++_refreshVersion;
        bool appendPage = _appendNextPage;
        int requestedPage = _requestedHistoryPage ?? (appendPage ? _page + 1 : _page);
        GitHistoryFilter requestedFilter = _filter;
        _appendNextPage = false;
        _requestedHistoryPage = null;
        int operationVersion = BeginOperation(UiText.HistoryLoading, historyQuery: true);
        CancellationToken queryToken = CurrentOperationToken;
        try
        {
            Task<GitHistoryResult> historyTask = ReadHistoryForRefreshAsync(appendPage, requestedPage, requestedFilter, queryToken);
            Task<GitReferenceResult>? referencesTask = _referenceService?.ReadAsync(
                _repository,
                queryToken);
            GitHistoryResult result = await historyTask;
            GitReferenceResult? references = referencesTask is null ? null : await referencesTask;
            if (_disposed || version != _refreshVersion || queryToken.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess || result.Page is null)
            {
                ShowError(result.ErrorMessage ?? UiText.HistoryUnavailable);
                return;
            }

            string? selectedHash = GetSelectedCommitHash();
            bool appendEntries = appendPage && _entries.Count > 0;
            HashSet<string> existingHashes = appendEntries
                ? _entries.Select(entry => entry.FullHash).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
            List<GitHistoryEntry> appendedEntries = appendEntries
                ? result.Page.Entries.Where(entry => existingHashes.Add(entry.FullHash)).ToList() : [];
            bool entriesChanged = appendEntries ? appendedEntries.Count > 0 : !HistoryEntriesEqual(_entries, result.Page.Entries);
            if (entriesChanged)
            {
                int topIndex = HistoryListTopIndexForTest;
                string? topHash = HistoryListTopHashForTest;
                int horizontalPosition = NativeMethods.GetScrollPosition(_historyList, 0);
                _updatingHistoryList = true;
                _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageSetRedraw, 0, 0);
                try
                {
                    string? selectedHashForList = selectedHash;
                    IReadOnlyList<GitHistoryEntry> entriesToRender;
                    if (!appendEntries)
                    {
                        if (TryApplyHistoryListDelta(result.Page.Entries, selectedHashForList))
                        {
                            entriesToRender = [];
                        }
                        else
                        {
                            _entries.Clear();
                            _entries.AddRange(result.Page.Entries);
                            RebuildHistoryGraph();
                            _historyListResetCount++;
                            _ = NativeMethods.SendMessage(_historyList, NativeMethods.ListBoxResetContent, 0, 0);
                            entriesToRender = _entries;
                        }
                    }
                    else
                    {
                        _entries.AddRange(appendedEntries);
                        RebuildHistoryGraph();
                        entriesToRender = appendedEntries;
                    }

                    foreach (GitHistoryEntry entry in entriesToRender)
                    {
                        _ = NativeMethods.SendMessage(
                            _historyList,
                            NativeMethods.ListBoxAddString,
                            0,
                            FormatHistoryListEntry(entry));
                    }

                    if (!appendEntries)
                    {
                        int selectedIndex = selectedHash is null
                            ? -1
                            : _entries.FindIndex(entry => entry.FullHash.Equals(
                                selectedHash,
                                StringComparison.OrdinalIgnoreCase));
                        if (selectedIndex >= 0)
                        {
                            _ = NativeMethods.SendMessage(
                                _historyList,
                                NativeMethods.ListBoxSetCurrentSelection,
                                unchecked((nuint)selectedIndex),
                                0);
                        }
                    }
                }
                finally
                {
                    UpdateHistoryRowExtent();
                    int restoredTop = ResolveHistoryTopIndex(_entries, topIndex, topHash);
                    if (restoredTop >= 0)
                        _ = NativeMethods.SendMessage(_historyList, NativeMethods.ListBoxSetTopIndex, unchecked((nuint)restoredTop), 0);
                    _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageHorizontalScroll,
                        unchecked((nuint)((horizontalPosition << 16) | 4)), 0);
                    _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageSetRedraw, 1, 0);
                    _updatingHistoryList = false;
                    _ = NativeMethods.InvalidateRectangle(_historyList, 0, true);
                }
            }
            _page = result.Page.Page;
            _hasPreviousPage = result.Page.HasPreviousPage;
            _hasNextPage = result.Page.HasNextPage;
            _hasLoadedHistoryPage = true;
            if (entriesChanged) UpdateHistoryRowExtent();
            bool selectedCommitStillExists = selectedHash is not null
                && _entries.Any(entry => entry.FullHash.Equals(
                    selectedHash,
                    StringComparison.OrdinalIgnoreCase));
            bool selectFirstFileHistoryCommit = _fileHistoryMode
                && _entries.Count > 0
                && !selectedCommitStillExists;
            if (selectedCommitStillExists)
            {
                _selectedCommitHash = selectedHash;
            }
            else
            {
                // 当前提交已经从刷新后的历史中消失，必须让尚未完成的详情结果失效。
                CancelPendingFileDiff();
                CancelFileHistoryPreview();
                if (_fileHistoryMode) _fileHistoryComparison?.ShowMessage(UiText.FileHistoryNoContent);
                _detailsVersion++;
                _detailsLoadingHash = null;
                _selectedCommitHash = null;
                _activeDetails = null;
                _files.Clear();
                _fileRows.Clear();
                _ = NativeMethods.SendMessage(_filesList, NativeMethods.WindowMessageSetRedraw, 0, 0);
                try
                {
                    _fileListResetCount++;
                    _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxResetContent, 0, 0);
                }
                finally
                {
                    _ = NativeMethods.SendMessage(_filesList, NativeMethods.WindowMessageSetRedraw, 1, 0);
                }
                _ = NativeMethods.InvalidateRectangle(_filesList, 0, true);
                UpdateFilesListScrollBar(0);
                SetMetadataMessage(UiText.SelectCommit);
            }
            if (selectFirstFileHistoryCommit)
            {
                _selectedCommitHash = null;
                _ = NativeMethods.SendMessage(
                    _historyList,
                    NativeMethods.ListBoxSetCurrentSelection,
                    0,
                    0);
            }
            if (references is { IsSuccess: true, Snapshot: not null }
                && !ReferenceSnapshotsEqual(_referenceSnapshot, references.Snapshot))
            {
                _referenceSnapshot = references.Snapshot;
                PopulateBranches(_referenceSnapshot);
            }

            _ = NativeMethods.SetWindowText(
                _pageLabel,
                UiText.GitLog);
            if (_fileHistoryMode)
            {
                _ = NativeMethods.SetWindowText(
                    _fileHistoryTab,
                    FormatFileHistoryTab(_filter.FilePath));
                _ = NativeMethods.SetWindowText(
                    _fileHistoryBranchLabel,
                    FormatFileHistoryBranchLabel());
                if (selectFirstFileHistoryCommit)
                {
                    _ = LoadSelectedCommitAsync();
                }
                else if (selectedCommitStillExists && selectedHash is not null)
                {
                    if (_activeDetails is null
                        || !_activeDetails.Commit.FullHash.Equals(
                            selectedHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = LoadSelectedCommitAsync();
                    }
                    else
                    {
                        _ = LoadFileHistoryPreviewAsync(selectedHash);
                    }
                }
            }
            _setStatus(UiText.HistoryReady);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation(operationVersion);
            _refreshTask = null;
            if (_refreshQueued && !_disposed)
            {
                _refreshQueued = false;
                _ = RefreshPageAsync();
            }
        }
    }

    private async Task<GitHistoryResult> ReadHistoryForRefreshAsync(
        bool appendPage, int requestedPage, GitHistoryFilter filter, CancellationToken token)
    {
        if (appendPage || requestedPage == 0 || _historyService is null || _repository is null)
        {
            return await _historyService!.ReadPageAsync(
                _repository!,
                new(requestedPage, 100, filter),
                token);
        }

        // 已经加载过后续页时，刷新必须重建从第 0 页到当前页的上下文，不能只用当前页覆盖列表。
        GitHistoryResult first = await _historyService.ReadPageAsync(
            _repository,
            new(0, 100, filter),
            token);
        if (!first.IsSuccess || first.Page is null)
        {
            return first;
        }

        List<GitHistoryEntry> entries = first.Page.Entries.ToList();
        HashSet<string> hashes = entries.Select(entry => entry.FullHash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        GitHistoryPage lastPage = first.Page;
        for (int page = 1; page <= requestedPage && lastPage.HasNextPage; page++)
        {
            token.ThrowIfCancellationRequested();
            GitHistoryResult next = await _historyService.ReadPageAsync(
                _repository,
                new(page, 100, filter),
                token);
            if (!next.IsSuccess || next.Page is null)
            {
                return next;
            }

            entries.AddRange(next.Page.Entries.Where(entry => hashes.Add(entry.FullHash)));
            lastPage = next.Page;
        }

        return GitHistoryResult.Success(new(
            lastPage.Page,
            lastPage.PageSize,
            lastPage.HasPreviousPage,
            lastPage.HasNextPage,
            entries));
    }

    private void PopulateBranches(GitReferenceSnapshot snapshot)
    {
        string? selectedBranch = GetSelectedBranchRowText();
        int topIndex = checked((int)NativeMethods.SendMessage(
            _branchesList,
            NativeMethods.ListBoxGetTopIndex,
            0,
            0));
        BranchRow? topRow = topIndex > 0 && topIndex < _branchRows.Count
            ? _branchRows[topIndex]
            : null;
        string query = NativeMethods.GetWindowTextValue(_branchFilterEdit).Trim();
        GitBranchInfo? current = snapshot.Branches.FirstOrDefault(branch => branch.IsCurrent);
        List<BranchRow> nextRows = BuildBranchRows(snapshot, query, _collapsedBranchSections);
        if (_branchRows.SequenceEqual(nextRows))
        {
            return;
        }

        _updatingBranchesList = true;
        _ = NativeMethods.SendMessage(_branchesList, NativeMethods.WindowMessageSetRedraw, 0, 0);
        try
        {
            if (TryComputeBranchListDelta(
                    _branchRows,
                    nextRows,
                    out int prefix,
                    out int removedCount,
                    out int addedCount))
            {
                for (int index = removedCount - 1; index >= 0; index--)
                {
                    _ = NativeMethods.SendMessage(
                        _branchesList,
                        NativeMethods.ListBoxDeleteString,
                        unchecked((nuint)(prefix + index)),
                        0);
                }

                for (int index = 0; index < addedCount; index++)
                {
                    _ = NativeMethods.SendMessage(
                        _branchesList,
                        NativeMethods.ListBoxInsertString,
                        unchecked((nuint)(prefix + index)),
                        nextRows[prefix + index].Text);
                }

                _branchRows.RemoveRange(prefix, removedCount);
                _branchRows.InsertRange(prefix, nextRows.Skip(prefix).Take(addedCount));
                _branchListDeltaCount++;
            }
            else
            {
                _branchRows.Clear();
                _branchRows.AddRange(nextRows);
                _branchListResetCount++;
                _ = NativeMethods.SendMessage(_branchesList, NativeMethods.ListBoxResetContent, 0, 0);
                foreach (BranchRow row in _branchRows)
                {
                    _ = NativeMethods.SendMessage(
                        _branchesList,
                        NativeMethods.ListBoxAddString,
                        0,
                        row.Text);
                }
            }

            int selectedIndex = FindBranchSelectionIndex(
                _branchRows,
                selectedBranch,
                current?.Name);
            if (selectedIndex >= 0)
            {
                _ = NativeMethods.SendMessage(
                    _branchesList,
                    NativeMethods.ListBoxSetCurrentSelection,
                    unchecked((nuint)selectedIndex),
                    0);
            }

            int restoredTopIndex = ResolveBranchTopIndex(_branchRows, topIndex, topRow);
            if (restoredTopIndex >= 0)
            {
                _ = NativeMethods.SendMessage(
                    _branchesList,
                    NativeMethods.ListBoxSetTopIndex,
                    unchecked((nuint)restoredTopIndex),
                    0);
            }

        }
        finally
        {
            _ = NativeMethods.SendMessage(_branchesList, NativeMethods.WindowMessageSetRedraw, 1, 0);
            _updatingBranchesList = false;
        }

        _ = NativeMethods.InvalidateRectangle(_branchesList, 0, true);
    }

    private static List<BranchRow> BuildBranchRows(
        GitReferenceSnapshot snapshot,
        string query,
        HashSet<string> collapsedSections)
    {
        List<BranchRow> rows = [];
        GitBranchInfo? current = snapshot.Branches.FirstOrDefault(branch => branch.IsCurrent);
        if (query.Length == 0
            || current?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) == true
            || "HEAD（当前分支）".Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            rows.Add(new("HEAD（当前分支）", 0, false, false));
        }

        AddBranchSection(
            rows,
            "本地",
            snapshot.Branches
                .Where(branch => !branch.IsRemote)
                .OrderBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
                .Select(branch => new BranchRow(branch.Name, 1, false, branch.IsCurrent)),
            query,
            collapsedSections);
        AddBranchSection(
            rows,
            "远程",
            snapshot.Branches
                .Where(branch => branch.IsRemote)
                .OrderBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
                .Select(branch => new BranchRow(branch.Name, 1, false, false)),
            query,
            collapsedSections);
        AddBranchSection(
            rows,
            "标签",
            snapshot.Tags
                .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                .Select(tag => new BranchRow(tag.Name, 1, false, false)),
            query,
            collapsedSections);
        return rows;
    }

    private string? GetSelectedBranchRowText()
    {
        int index = GetListSelection(_branchesList);
        return index >= 0 && index < _branchRows.Count && !_branchRows[index].Group
            ? _branchRows[index].Text
            : null;
    }

    private static int FindBranchSelectionIndex(
        IReadOnlyList<BranchRow> rows,
        string? selectedBranch,
        string? currentBranch)
    {
        if (!string.IsNullOrWhiteSpace(selectedBranch))
        {
            for (int index = 0; index < rows.Count; index++)
            {
                BranchRow row = rows[index];
                if (!row.Group && row.Text.Equals(selectedBranch, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(currentBranch))
        {
            for (int index = 0; index < rows.Count; index++)
            {
                BranchRow row = rows[index];
                if (!row.Group && row.Text.Equals(currentBranch, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        return -1;
    }

    internal static int FindBranchSelectionIndexForTest(
        IReadOnlyList<(string Text, bool Group)> rows,
        string? selectedBranch,
        string? currentBranch)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!string.IsNullOrWhiteSpace(selectedBranch))
        {
            for (int index = 0; index < rows.Count; index++)
            {
                if (!rows[index].Group
                    && rows[index].Text.Equals(selectedBranch, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(currentBranch))
        {
            for (int index = 0; index < rows.Count; index++)
            {
                if (!rows[index].Group
                    && rows[index].Text.Equals(currentBranch, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static void AddBranchSection(
        List<BranchRow> destination,
        string title,
        IEnumerable<BranchRow> source,
        string query,
        HashSet<string> collapsedSections)
    {
        BranchRow[] rows = source
            .Where(row => query.Length == 0 || row.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        destination.Add(new(title, 0, true, false));
        if (query.Length == 0 && collapsedSections.Contains(title))
        {
            return;
        }

        destination.AddRange(rows);
    }

    internal static (bool CanApply, int Prefix, int RemovedCount, int AddedCount)
        BranchListDeltaForTest(
            IReadOnlyList<(string Text, int Indent, bool Group, bool Accent)> previous,
            IReadOnlyList<(string Text, int Indent, bool Group, bool Accent)> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        BranchRow[] previousRows = previous
            .Select(row => new BranchRow(row.Text, row.Indent, row.Group, row.Accent))
            .ToArray();
        BranchRow[] currentRows = current
            .Select(row => new BranchRow(row.Text, row.Indent, row.Group, row.Accent))
            .ToArray();
        bool canApply = TryComputeBranchListDelta(
            previousRows,
            currentRows,
            out int prefix,
            out int removedCount,
            out int addedCount);
        return (canApply, prefix, removedCount, addedCount);
    }

    private static bool TryComputeBranchListDelta(
        IReadOnlyList<BranchRow> previous,
        IReadOnlyList<BranchRow> current,
        out int prefix,
        out int removedCount,
        out int addedCount)
    {
        prefix = 0;
        removedCount = 0;
        addedCount = 0;
        while (prefix < previous.Count
            && prefix < current.Count
            && previous[prefix] == current[prefix])
        {
            prefix++;
        }

        int previousSuffix = previous.Count - 1;
        int currentSuffix = current.Count - 1;
        while (previousSuffix >= prefix
            && currentSuffix >= prefix
            && previous[previousSuffix] == current[currentSuffix])
        {
            previousSuffix--;
            currentSuffix--;
        }

        removedCount = previousSuffix - prefix + 1;
        addedCount = currentSuffix - prefix + 1;
        return removedCount > 0 || addedCount > 0;
    }

    private static int ResolveBranchTopIndex(
        IReadOnlyList<BranchRow> rows,
        int previousTopIndex,
        BranchRow? previousTopRow)
    {
        if (rows.Count == 0)
        {
            return -1;
        }

        if (previousTopIndex <= 0)
        {
            return 0;
        }

        if (previousTopRow is not null)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                if (rows[index] == previousTopRow)
                {
                    return index;
                }
            }
        }

        return Math.Clamp(previousTopIndex, 0, rows.Count - 1);
    }

    private static List<FileTreeRow> BuildFileTreeRows(
        List<GitCommitChangedFile> files,
        HashSet<string> collapsedGroups)
    {
        List<FileTreeRow> rows =
        [
            new($"{files.Count} 个文件", null, 0, true, RootFileGroupKey, files.Count),
        ];
        if (files.Count == 0 || collapsedGroups.Contains(RootFileGroupKey))
        {
            return rows;
        }

        FileTreeNode root = new(string.Empty, string.Empty);
        foreach (GitCommitChangedFile file in files)
        {
            string normalizedPath = file.RelativePath.Replace('\\', '/').Trim('/');
            string[] parts = normalizedPath.Length == 0
                ? [file.RelativePath]
                : normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            FileTreeNode node = root;
            for (int index = 0; index < parts.Length - 1; index++)
            {
                string part = parts[index];
                string path = node.PathKey.Length == 0
                    ? part
                    : $"{node.PathKey}/{part}";
                if (!node.Directories.TryGetValue(part, out FileTreeNode? child))
                {
                    child = new(part, path);
                    node.Directories.Add(part, child);
                }

                node = child;
            }

            node.Files.Add(file);
        }

        AppendFileTreeRows(root, rows, 1, collapsedGroups);
        return rows;
    }

    private static void AppendFileTreeRows(
        FileTreeNode node,
        ICollection<FileTreeRow> rows,
        int indent,
        IReadOnlySet<string> collapsedGroups)
    {
        foreach (FileTreeNode child in node.Directories.Values.OrderBy(
            child => child.Name,
            StringComparer.OrdinalIgnoreCase))
        {
            int fileCount = CountFiles(child);
            rows.Add(new(
                $"{child.Name} {fileCount} 个文件",
                null,
                indent,
                true,
                child.PathKey,
                fileCount));
            if (!collapsedGroups.Contains(child.PathKey))
            {
                AppendFileTreeRows(child, rows, indent + 1, collapsedGroups);
            }
        }

        foreach (GitCommitChangedFile file in node.Files.OrderBy(
            file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase))
        {
            string normalizedPath = file.RelativePath.Replace('\\', '/').Trim('/');
            string fileName = normalizedPath.LastIndexOf('/') is int separator && separator >= 0
                ? normalizedPath[(separator + 1)..]
                : normalizedPath;
            if (fileName.Length == 0)
            {
                fileName = file.RelativePath;
            }

            string original = file.OriginalRelativePath is null
                ? string.Empty
                : $" ← {file.OriginalRelativePath}";
            rows.Add(new(
                $"{fileName}{original}",
                file,
                indent,
                false,
                string.Empty,
                1));
        }
    }

    private static int CountFiles(FileTreeNode node)
    {
        return node.Files.Count + node.Directories.Values.Sum(CountFiles);
    }

    private void ToggleFileGroup(FileTreeRow row)
    {
        if (!row.Group)
        {
            return;
        }

        if (!_collapsedFileGroups.Add(row.GroupKey))
        {
            _collapsedFileGroups.Remove(row.GroupKey);
        }

        string? selectedPath = GetSelectedFile()?.RelativePath;
        PopulateFileRows(preserveSelection: false);
        if (selectedPath is not null)
        {
            int selectedIndex = FindFileTreeRowIndex(selectedPath);
            if (selectedIndex >= 0)
            {
                _ = NativeMethods.SendMessage(
                    _filesList,
                    NativeMethods.ListBoxSetCurrentSelection,
                    unchecked((nuint)selectedIndex),
                    0);
            }
        }
    }

    private void ToggleBranchSection(string title)
    {
        if (!_collapsedBranchSections.Add(title))
        {
            _collapsedBranchSections.Remove(title);
        }
        PopulateBranches(_referenceSnapshot);
        int index = _branchRows.FindIndex(row => row.Group && row.Text.Equals(title, StringComparison.Ordinal));
        if (index >= 0)
        {
            _ = NativeMethods.SendMessage(
                _branchesList,
                NativeMethods.ListBoxSetCurrentSelection,
                unchecked((nuint)index),
                0);
        }
    }

    private void ApplyBranchFilter(BranchRow row)
    {
        if (row.Group)
        {
            return;
        }

        string? branch = row.Text.StartsWith("HEAD（", StringComparison.Ordinal)
            ? _referenceSnapshot.Branches.FirstOrDefault(reference => reference.IsCurrent)?.Name
            : row.Text;
        if (string.IsNullOrWhiteSpace(branch))
        {
            return;
        }

        _filter = _filter with { Branch = branch, Hash = null };
        InvalidateHistoryQuery();
        _page = 0;
        SelectFilterKind(5);
    }

    private async Task LoadSelectedCommitAsync()
    {
        int index = GetListSelection(_historyList);
        if (index < 0 || index >= _entries.Count || _historyService is null || _repository is null)
        {
            return;
        }

        GitHistoryEntry selected = _entries[index];
        if (ShouldReuseCommitDetailsRequest(
                _selectedCommitHash,
                selected.FullHash,
                _detailsLoadingHash,
                _activeDetails?.Commit.FullHash))
        {
            if (_fileHistoryMode && _activeDetails?.Commit.FullHash == selected.FullHash)
                await LoadFileHistoryPreviewAsync(selected.FullHash);
            return;
        }

        int version = ++_detailsVersion;
        CancelFileHistoryPreview();
        // 提交上下文改变后，尚未完成的变化文件 Diff 不能再回写中央编辑区。
        CancelPendingFileDiff();
        _selectedCommitHash = selected.FullHash;
        _detailsLoadingHash = selected.FullHash;
        if (_followFileComparison && !_fileHistoryMode)
            _updateFollowedComparisonNotice(selected.FullHash, UiText.HistoryLoading);
        if (_activeDetails is null
            || !_activeDetails.Commit.FullHash.Equals(
                selected.FullHash,
                StringComparison.OrdinalIgnoreCase))
        {
            // 详情和变化文件必须与当前选中的提交成对切换，避免异步加载期间显示旧提交内容。
            _pendingFileListView ??= CaptureFileListView();
            _activeDetails = null;
            _files.Clear();
            PopulateFileRows(preserveSelection: false);
            SetMetadataMessage(UiText.HistoryLoading);
        }
        _commitDetailsRequestCount++;
        GitCommitDetailsResult result;
        try
        {
            result = await _historyService.ReadCommitAsync(
                _repository,
                selected.FullHash,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (version == _detailsVersion
                && _detailsLoadingHash is not null
                && _detailsLoadingHash.Equals(selected.FullHash, StringComparison.OrdinalIgnoreCase))
            {
                _detailsLoadingHash = null;
                if (_followFileComparison && !_fileHistoryMode)
                    _updateFollowedComparisonNotice(selected.FullHash, UiText.ComparisonCancelled);
            }

            return;
        }
        if (_disposed || version != _detailsVersion)
        {
            return;
        }

        _detailsLoadingHash = null;

        if (!result.IsSuccess || result.Details is null)
        {
            ShowError(result.ErrorMessage ?? UiText.HistoryUnavailable);
            if (_followFileComparison && !_fileHistoryMode)
                _updateFollowedComparisonNotice(selected.FullHash, result.ErrorMessage ?? UiText.HistoryUnavailable);
            if (_fileHistoryMode) _fileHistoryComparison?.SetResult(
                GitComparisonResult.Failure(result.FailureKind, result.ErrorMessage ?? UiText.HistoryUnavailable));
            return;
        }

        _activeDetails = result.Details;
        _files.Clear();
        _files.AddRange(result.Details.Files);
        FileListViewSnapshot? restoreView = _pendingFileListView;
        _pendingFileListView = null;
        PopulateFileRows(preserveSelection: false, restoreView);
        RenderCommitDetails(result.Details);
        if (_followFileComparison && !_fileHistoryMode)
        {
            if (GetSelectedFile() is null)
            {
                int firstFile = _fileRows.FindIndex(row => row.File is not null);
                if (firstFile >= 0)
                    _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxSetCurrentSelection,
                        (nuint)firstFile, 0);
            }
            await LoadSelectedFileDiffAsync(activate: false);
            if (GetSelectedFile() is null)
                _updateFollowedComparisonNotice(selected.FullHash, UiText.SelectCommitFile);
        }
        if (_fileHistoryMode)
        {
            await LoadFileHistoryPreviewAsync(selected.FullHash);
        }
    }

    private void RenderCommitDetails(GitCommitDetails details)
    {
        GitHistoryEntry commit = details.Commit;
        string refs = commit.References.Count == 0
            ? "无"
            : string.Join(", ", commit.References.Select(reference => reference.Name));
        string meta = string.Join(
            " · ",
            new[]
            {
                commit.ShortHash,
                commit.AuthorName,
                commit.AuthorDate.LocalDateTime.ToString("yyyy/M/d HH:mm:ss", CultureInfo.InvariantCulture),
            });
        string? references = commit.References.Count == 0 ? null : $"引用：{refs}";
        string body = details.Body;
        // Git 的 %B 包含标题；标题已单独展示，正文不重复同一行。
        if (body.StartsWith(commit.Subject, StringComparison.Ordinal)
            && (body.Length == commit.Subject.Length || body[commit.Subject.Length] is '\r' or '\n'))
            body = body[commit.Subject.Length..].TrimStart('\r', '\n');
        if (_detailsTitle == commit.Subject && _detailsMeta == meta
            && _detailsReferences == references && _detailsBody == body) return;
        _detailsTitle = commit.Subject;
        _detailsMeta = meta;
        _detailsReferences = references;
        _detailsBody = body;
        _detailsMessageCentered = false;
        _ = NativeMethods.SetWindowText(
            _metadataLabel,
            string.Join(
                Environment.NewLine,
                new[]
                {
                    _detailsTitle,
                    _detailsMeta,
                    _detailsReferences,
                    string.IsNullOrWhiteSpace(_detailsBody) ? string.Empty : _detailsBody,
                }));
        ResetDetailsLayout();
    }

    private void SetMetadataMessage(string message)
    {
        _detailsTitle = null;
        _detailsMeta = null;
        _detailsReferences = null;
        _detailsBody = null;
        _detailsMessageCentered = ShouldCenterMetadata(message);
        _ = NativeMethods.SetWindowText(_metadataLabel, message);
        ResetDetailsLayout();
    }

    private static bool ShouldReuseCommitDetailsRequest(
        string? selectedCommitHash,
        string selectedFullHash,
        string? detailsLoadingHash,
        string? detailsLoadedHash)
    {
        return selectedCommitHash is not null
            && selectedCommitHash.Equals(selectedFullHash, StringComparison.OrdinalIgnoreCase)
            && ((detailsLoadedHash is not null
                    && detailsLoadedHash.Equals(selectedFullHash, StringComparison.OrdinalIgnoreCase))
                || (detailsLoadingHash is not null
                    && detailsLoadingHash.Equals(selectedFullHash, StringComparison.OrdinalIgnoreCase)));
    }

    private Task LoadSelectedFileDiffAsync(bool activate = true)
    {
        FileTreeRow? row = GetSelectedFileRow();
        if (_disposed || _operationRunning
            || row?.File is not GitCommitChangedFile file
            || _historyService is null
            || _repository is null
            || _activeDetails is null
            || _selectedCommitHash is null
            || !_activeDetails.Commit.FullHash.Equals(
                _selectedCommitHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (activate) _followFileComparison = true;
        else if (!_followFileComparison || _fileHistoryMode) return Task.CompletedTask;
        string commitHash = _activeDetails.Commit.FullHash;
        string relativePath = file.RelativePath;
        if (_fileDiffRequestKey == (commitHash, relativePath)
            && _fileDiffRequestTask is { IsCompleted: false } pending)
        {
            // 后台跟随已发出同键查询时，Enter 只激活现有比较，不再查询或取消它。
            if (activate) _activateFileComparison();
            return pending;
        }

        CancelPendingFileDiff();
        _fileDiffRequestKey = (commitHash, relativePath);
        _fileDiffCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _ = ShowFileDiffCancelAsync(_fileDiffRequestVersion, _fileDiffCancellation.Token);
        _fileDiffRequestTask = ReadFileDiffAsync(
            _historyService, _repository, commitHash, relativePath, _fileDiffRequestVersion, activate, _fileDiffCancellation);
        return _fileDiffRequestTask;
    }

    private async Task ReadFileDiffAsync(
        IGitHistoryService service,
        GitRepositorySnapshot repository,
        string commitHash,
        string relativePath,
        int requestVersion,
        bool activate,
        CancellationTokenSource cancellation)
    {
        using (cancellation)
        {
            try
            {
                async Task<GitComparisonResult> LoadCurrentFileAsync(CancellationToken token)
                {
                    GitComparisonResult result = await service.ReadCommitFileDiffAsync(repository, commitHash, relativePath, cancellationToken: token);
                    token.ThrowIfCancellationRequested();
                    if (_disposed || requestVersion != _fileDiffRequestVersion
                        || _selectedCommitHash != commitHash || _activeDetails?.Commit.FullHash != commitHash
                        || GetSelectedFile()?.RelativePath != relativePath)
                    {
                        throw new OperationCanceledException(token);
                    }
                    return result;
                }
                await _openFileComparison(
                    new(GitDiffContentStatus.Ready, $"{commitHash}^", commitHash, relativePath, null),
                    LoadCurrentFileAsync,
                    activate,
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (requestVersion == _fileDiffRequestVersion)
                {
                    _fileDiffCancellation = null;
                    _fileDiffRequestKey = null;
                    _fileDiffRequestTask = null;
                    _showFileDiffCancel = false;
                    UpdateCancelButton();
                }
            }
        }
    }

    private void CancelPendingFileDiff()
    {
        ++_fileDiffRequestVersion;
        CancellationTokenSource? cancellation = _fileDiffCancellation;
        _fileDiffCancellation = null;
        _fileDiffRequestKey = null;
        _fileDiffRequestTask = null;
        _showFileDiffCancel = false;
        cancellation?.Cancel();
        UpdateCancelButton();
    }

    private async Task ShowFileDiffCancelAsync(int version, CancellationToken token)
    {
        try
        {
            await Task.Delay(150, token);
            if (!_disposed && version == _fileDiffRequestVersion && _fileDiffCancellation is not null)
            {
                _showFileDiffCancel = true;
                UpdateCancelButton();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateCancelButton()
    {
        if (_disposed || _cancelButton == 0)
        {
            return;
        }
        _ = NativeMethods.SetWindowText(_cancelButton,
            _operationRunning && !_operationIsComparison ? UiText.CancelOperation : UiText.CancelComparison);
        bool visible = _operationRunning || _showFileDiffCancel;
        if (!visible && NativeMethods.GetFocus() == _cancelButton)
        {
            nint target = _files.Count > 0 ? _filesList : _historyList;
            if (NativeMethods.IsWindowEnabled(target))
            {
                _ = NativeMethods.SetFocus(target);
            }
        }
        _ = NativeMethods.ShowWindow(_cancelButton, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
    }

    internal void CancelPendingComparison()
    {
        StopFollowingFileComparison();
        if (_operationIsComparison)
        {
            _operationCancellation?.Cancel();
        }
    }

    internal void StopFollowingFileComparison()
    {
        _followFileComparison = false;
        CancelPendingFileDiff();
    }

    private void ActivateSelectedFileDiff()
    {
        _lastFileActivationForTest = LoadSelectedFileDiffAsync();
        _ = _lastFileActivationForTest;
    }

    private async Task CompareReferencesAsync()
    {
        if (_historyService is null || _repository is null)
        {
            return;
        }

        string? baseRevision = NativeTextPrompt.Show(
            Handle,
            UiText.CompareReferences,
            UiText.ComparisonBasePrompt,
            NativeTheme.IsDark(_settings.Theme));
        if (string.IsNullOrWhiteSpace(baseRevision))
        {
            return;
        }

        string? targetRevision = NativeTextPrompt.Show(
            Handle,
            UiText.CompareReferences,
            UiText.ComparisonTargetPrompt,
            NativeTheme.IsDark(_settings.Theme));
        if (targetRevision is null)
        {
            return;
        }

        string? path = NativeTextPrompt.Show(
            Handle,
            UiText.CompareReferences,
            UiText.ComparisonPathPrompt,
            NativeTheme.IsDark(_settings.Theme));
        if (path is null)
        {
            return;
        }

        int operationVersion = BeginOperation(UiText.GeneratingDiff, comparison: true);
        CancellationToken token = CurrentOperationToken;
        try
        {
            GitComparisonResult result = await _historyService.CompareAsync(
                _repository,
                new(baseRevision.Trim(), NullIfEmpty(targetRevision), NullIfEmpty(path)),
                token);
            if (!_disposed && !token.IsCancellationRequested && operationVersion == _operationVersion)
            {
                ShowComparison(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation(operationVersion);
        }
    }

    private async Task CompareCommitWithWorkspaceAsync(string commitHash, string? relativePath = null)
    {
        if (_historyService is null || _repository is null)
        {
            return;
        }

        int operationVersion = BeginOperation(UiText.GeneratingDiff, comparison: true);
        CancellationToken token = CurrentOperationToken;
        try
        {
            GitComparisonResult result = await _historyService.CompareAsync(
                _repository,
                new(commitHash, null, NullIfEmpty(relativePath)),
                token);
            if (!_disposed && !token.IsCancellationRequested && operationVersion == _operationVersion)
            {
                ShowComparison(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation(operationVersion);
        }
    }

    internal Task CompareRevisionForHostAsync(string revision)
    {
        return CompareRevisionForHostAsync(revision, null);
    }

    internal Task CompareRevisionForHostAsync(string revision, string? relativePath)
    {
        return CompareCommitWithWorkspaceAsync(revision, relativePath);
    }

    internal async Task<GitComparisonResult> ReloadComparisonForHostAsync(
        GitComparisonDocument document,
        bool ignoreWhitespace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_historyService is null || _repository is null)
        {
            return GitComparisonResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                UiText.HistoryUnavailable);
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeCancellation.Token);
        return await _historyService.CompareAsync(
            _repository,
            new(
                document.BaseRevision,
                document.TargetRevision,
                document.RelativePath,
                ignoreWhitespace),
            linked.Token);
    }

    internal void ShowWorktreeForHost()
    {
        ShowWorktreeManagement();
    }

    private async Task StartHistoryOperationAsync(
        GitAdvancedOperationKind kind,
        string commitHash)
    {
        if (_operationService is null || _conflictService is null || _repository is null || _operationRunning)
        {
            return;
        }

        CancelPendingFileDiff();
        int operationVersion = BeginOperation($"正在执行 {kind}…");
        bool openConflictFlow = false;
        try
        {
            GitAdvancedOperationResult result = await _operationService.StartAsync(
                _repository,
                new(kind, commitHash),
                CurrentOperationToken);
            openConflictFlow = result.Session?.HasConflicts == true;
            if (openConflictFlow)
            {
                _setStatus(result.ErrorMessage ?? "Git 操作产生冲突，请继续处理冲突文件。");
            }
            else if (!result.IsSuccess)
            {
                ShowError(result.ErrorMessage ?? UiText.GitOperationFailed);
            }
            else
            {
                _setStatus(UiText.GitOperationCompleted);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation(operationVersion);
            if (openConflictFlow)
            {
                _ = NativeGitOperationDialog.Show(
                    GetDialogOwner(),
                    _repository,
                    _operationService,
                    _conflictService,
                    _settings,
                    _setStatus);
            }
            RequestRefresh();
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

    private static string FormatFileHistoryTab(string? relativePath)
    {
        string path = relativePath?.Replace('\\', '/').Trim('/') ?? string.Empty;
        int separator = path.LastIndexOf('/');
        string fileName = separator >= 0 ? path[(separator + 1)..] : path;
        return fileName.Length == 0 ? UiText.FileHistory : $"历史: {fileName}";
    }

    private string FormatFileHistoryBranchLabel()
    {
        string? branch = _referenceSnapshot.Branches
            .FirstOrDefault(reference => reference.IsCurrent)
            ?.Name;
        return string.IsNullOrWhiteSpace(branch) ? "分支: HEAD" : $"分支: {branch}";
    }

    private async Task LoadFileHistoryPreviewAsync(string commitHash)
    {
        if (_disposed || !_fileHistoryMode || !_showDetails
            || _historyService is null || _repository is null
            || string.IsNullOrWhiteSpace(_filter.FilePath)
            || _fileHistoryComparison is null || !NativeMethods.IsWindowVisible(Handle)
            || _selectedCommitHash != commitHash)
        {
            return;
        }

        var key = (commitHash, _filter.FilePath);
        if (_fileHistoryPreviewKey == key && !_fileHistoryComparison.HasFailed) return;
        CancelFileHistoryPreview();
        int version = ++_fileHistoryPreviewVersion;
        _fileHistoryPreviewKey = key;
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _fileHistoryPreviewCancellation = cancellation;
        NativeGitComparisonView view = _fileHistoryComparison;
        IGitHistoryService service = _historyService;
        GitRepositorySnapshot repository = _repository;
        try
        {
            // 文件历史的父版本由 Git show 处理，根提交也能与空内容比较。
            await view.LoadAsync(new(GitDiffContentStatus.Ready, commitHash + "^", commitHash, key.FilePath, null),
                token => service.ReadCommitFileDiffAsync(repository, commitHash, key.FilePath, cancellationToken: token),
                cancellation.Token);
            if (IsFileHistoryPreviewCurrent(version) && view.HasFailed) _fileHistoryPreviewKey = null;
        }
        finally
        {
            if (ReferenceEquals(_fileHistoryPreviewCancellation, cancellation))
                _fileHistoryPreviewCancellation = null;
        }
    }

    private Task<GitComparisonResult> ReloadFileHistoryComparisonAsync(
        GitComparisonDocument document, bool ignoreWhitespace, CancellationToken cancellationToken)
    {
        if (_disposed || !_fileHistoryMode || !_showDetails || !NativeMethods.IsWindowVisible(Handle)
            || _historyService is null || _repository is null
            || document.TargetRevision != _selectedCommitHash || document.RelativePath != _filter.FilePath)
        {
            return Task.FromResult(GitComparisonResult.Failure(GitOperationFailureKind.Cancelled, UiText.ComparisonCancelled));
        }
        return _historyService.ReadCommitFileDiffAsync(_repository, document.TargetRevision!,
            document.RelativePath!, ignoreWhitespace, cancellationToken);
    }

    private bool IsFileHistoryPreviewCurrent(int version) => !_disposed && _fileHistoryMode
        && version == _fileHistoryPreviewVersion && _fileHistoryComparison is not null
        && _fileHistoryPreviewKey == (_selectedCommitHash, _filter.FilePath)
        && NativeMethods.IsWindowVisible(Handle);

    private void CancelFileHistoryPreview(bool keepLoaded = false)
    {
        _fileHistoryPreviewVersion++;
        if (_fileHistoryPreviewCancellation is not null || _fileHistoryComparison?.IsBusy == true || !keepLoaded)
            _fileHistoryPreviewKey = null;
        // 取消源由发起查询的方法释放，避免服务尚未退出时提前释放其令牌。
        _fileHistoryComparison?.CancelPendingWork();
        _fileHistoryPreviewCancellation?.Cancel();
        _fileHistoryPreviewCancellation = null;
    }

    private void ExitFileHistoryView()
    {
        _fileHistoryMode = false;
        CancelFileHistoryPreview();
        _fileHistoryComparison?.Clear();
    }

    private async Task ShowBlameAsync()
    {
        GitCommitChangedFile? file = GetSelectedFile();
        if (file is null)
        {
            ShowError(UiText.SelectCommitFile);
            return;
        }

        await ShowBlameForPathAsync(file.RelativePath);
    }

    private void ShowReferenceManagement(string? initialTarget = null)
    {
        if (_repository is null || _referenceService is null)
        {
            return;
        }

        NativeGitReferenceDialog.Show(
            GetDialogOwner(),
            _repository,
            _referenceService,
            _settings,
            _setStatus,
            initialTarget);
        RequestRefresh();
    }

    private void ShowCreateStash()
    {
        if (_repository is null || _stateService is null)
        {
            return;
        }

        string? currentBranch = _referenceSnapshot.Branches.FirstOrDefault(branch => branch.IsCurrent)?.Name;
        _ = NativeStashDialog.Show(
            GetDialogOwner(),
            _repository,
            _stateService,
            _settings,
            _setStatus,
            currentBranch);
        RequestRefresh();
    }

    private void ShowStashManagement()
    {
        if (_repository is null || _stateService is null)
        {
            return;
        }

        string? currentBranch = _referenceSnapshot.Branches.FirstOrDefault(branch => branch.IsCurrent)?.Name;
        NativeStashManagerDialog.Show(
            GetDialogOwner(),
            _repository,
            _stateService,
            _settings,
            _setStatus,
            currentBranch);
        RequestRefresh();
    }

    private void ShowReset(string? initialTarget = null)
    {
        if (_repository is null || _stateService is null)
        {
            return;
        }

        _ = NativeResetDialog.Show(
            GetDialogOwner(),
            _repository,
            _stateService,
            _settings,
            _setStatus,
            initialTarget);
        RequestRefresh();
    }

    private void ShowWorktreeManagement()
    {
        if (_repository is null || _worktreeService is null)
        {
            return;
        }

        NativeWorktreeManagerDialog.Show(
            GetDialogOwner(),
            _repository,
            _worktreeService,
            _settings,
            _setStatus);
        RequestRefresh();
    }

    private nint GetDialogOwner()
    {
        nint owner = NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot);
        return owner == 0 ? Handle : owner;
    }

    internal void ShowWorktreesForHost()
    {
        ShowWorktreeManagement();
    }

    private void ShowComparison(GitComparisonResult result)
    {
        if (!result.IsSuccess || result.Document is null)
        {
            ShowError(result.ErrorMessage ?? UiText.GenerateDiffFailed);
            return;
        }

        _showComparison(result);
    }

    private int BeginOperation(string status, bool comparison = false, bool historyQuery = false)
    {
        if (comparison)
        {
            _followFileComparison = false;
            CancelPendingFileDiff();
        }
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        int version = ++_operationVersion;
        _operationRunning = true;
        _operationIsComparison = comparison;
        _operationIsHistoryQuery = historyQuery;
        SetHistoryControlsEnabled(false);
        UpdateCancelButton();
        if (!comparison)
        {
            _setStatus(status);
        }
        return version;
    }

    private void EndOperation(int version)
    {
        if (_disposed || version != _operationVersion)
        {
            return;
        }

        _operationRunning = false;
        _operationIsComparison = false;
        _operationIsHistoryQuery = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        SetHistoryControlsEnabled(_historyService is not null);
        UpdateCancelButton();
        _ = NativeMethods.EnableWindow(_previousButton, _hasPreviousPage);
        _ = NativeMethods.EnableWindow(_nextButton, _hasNextPage);
        bool refreshPending = _refreshPending;
        _refreshPending = false;
        if (refreshPending)
        {
            RequestRefresh();
        }
    }

    private CancellationToken CurrentOperationToken =>
        _operationCancellation?.Token ?? _lifetimeCancellation.Token;

    private void SetHistoryControlsEnabled(bool enabled)
    {
        foreach (nint control in Controls.Where(
            control => control != _cancelButton
                && control != _closePanelButton
                && control != _titleLabel
                && control != _pageLabel
                && control != _metadataLabel))
        {
            bool historyNavigation = _operationIsHistoryQuery && (control == _historyList || control == _filesList
                || control == _backButton || control == _fileHistoryClearButton || control == _toggleDetailsButton);
            _ = NativeMethods.EnableWindow(control, enabled && !_operationRunning || historyNavigation);
        }
    }

    private GitCommitChangedFile? GetSelectedFile()
    {
        return GetSelectedFileRow()?.File;
    }

    private FileTreeRow? GetSelectedFileRow()
    {
        int index = GetListSelection(_filesList);
        return index >= 0 && index < _fileRows.Count ? _fileRows[index] : null;
    }

    private int FindFileTreeRowIndex(string relativePath)
    {
        return _fileRows.FindIndex(row => row.File?.RelativePath.Equals(
            relativePath,
            StringComparison.OrdinalIgnoreCase) == true);
    }

    private FileListViewSnapshot CaptureFileListView()
    {
        int selectedIndex = GetListSelection(_filesList);
        FileTreeRow? selectedRow = selectedIndex >= 0 && selectedIndex < _fileRows.Count
            ? _fileRows[selectedIndex]
            : null;
        int topIndex = checked((int)NativeMethods.SendMessage(
            _filesList,
            NativeMethods.ListBoxGetTopIndex,
            0,
            0));
        string? topAnchor = topIndex >= 0 && topIndex < _fileRows.Count
            ? FileTreeRowAnchor(_fileRows[topIndex])
            : null;
        return new(
            selectedRow?.File?.RelativePath,
            selectedRow is null
                ? null
                : selectedRow.Group
                    ? selectedRow.GroupKey
                    : FileDirectoryKey(selectedRow.File!.RelativePath),
            selectedIndex,
            topAnchor,
            topIndex);
    }

    private void PopulateFileRows(
        bool preserveSelection = true,
        FileListViewSnapshot? restoreView = null)
    {
        FileListViewSnapshot currentView = restoreView
            ?? (preserveSelection
                ? CaptureFileListView()
                : new(null, null, GetListSelection(_filesList), null,
                    checked((int)NativeMethods.SendMessage(
                        _filesList,
                        NativeMethods.ListBoxGetTopIndex,
                        0,
                        0))));
        List<FileTreeRow> nextRows = BuildFileTreeRows(_files, _collapsedFileGroups);
        if (_fileRows.SequenceEqual(nextRows))
        {
            if (restoreView is not null) RestoreFileListView(currentView);
            return;
        }

        _ = NativeMethods.SendMessage(_filesList, NativeMethods.WindowMessageSetRedraw, 0, 0);
        try
        {
            if (TryComputeFileListDelta(
                    _fileRows,
                    nextRows,
                    out int prefix,
                    out int removedCount,
                    out int addedCount))
            {
                for (int index = removedCount - 1; index >= 0; index--)
                {
                    _ = NativeMethods.SendMessage(
                        _filesList,
                        NativeMethods.ListBoxDeleteString,
                        unchecked((nuint)(prefix + index)),
                        0);
                }

                for (int index = 0; index < addedCount; index++)
                {
                    _ = NativeMethods.SendMessage(
                        _filesList,
                        NativeMethods.ListBoxInsertString,
                        unchecked((nuint)(prefix + index)),
                        FormatFileTreeAccessibleLabel(nextRows[prefix + index]));
                }

                _fileRows.RemoveRange(prefix, removedCount);
                _fileRows.InsertRange(prefix, nextRows.Skip(prefix).Take(addedCount));
                _fileListDeltaCount++;
            }
            else
            {
                _fileRows.Clear();
                _fileRows.AddRange(nextRows);
                _fileListResetCount++;
                _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxResetContent, 0, 0);
                foreach (FileTreeRow row in _fileRows)
                {
                    _ = NativeMethods.SendMessage(
                        _filesList,
                        NativeMethods.ListBoxAddString,
                        0,
                        FormatFileTreeAccessibleLabel(row));
                }
            }

            RestoreFileListView(currentView);
        }
        finally
        {
            _ = NativeMethods.SendMessage(_filesList, NativeMethods.WindowMessageSetRedraw, 1, 0);
        }

        _ = NativeMethods.InvalidateRectangle(_filesList, 0, true);
    }

    private void RestoreFileListView(FileListViewSnapshot view)
    {
        int selectedIndex = ResolveFileTreeSelectionIndex(
            _fileRows, view.SelectedPath, view.SelectedGroupKey, view.SelectedIndex);
        _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)selectedIndex), 0);
        int top = ResolveFileTreeTopIndex(_fileRows, view.TopIndex, view.TopAnchor);
        if (top >= 0)
            _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxSetTopIndex, (nuint)top, 0);
    }

    private static string FormatFileTreeAccessibleLabel(FileTreeRow row)
    {
        return row.File is { } file ? $"{StatusSymbol(file.Kind)} {row.Label}" : row.Label;
    }

    private static bool TryComputeFileListDelta(
        IReadOnlyList<FileTreeRow> previous,
        IReadOnlyList<FileTreeRow> current,
        out int prefix,
        out int removedCount,
        out int addedCount)
    {
        prefix = 0;
        removedCount = 0;
        addedCount = 0;
        if (previous.Count == 0 || current.Count == 0)
        {
            return false;
        }

        while (prefix < previous.Count
            && prefix < current.Count
            && previous[prefix] == current[prefix])
        {
            prefix++;
        }

        int previousSuffix = previous.Count - 1;
        int currentSuffix = current.Count - 1;
        while (previousSuffix >= prefix
            && currentSuffix >= prefix
            && previous[previousSuffix] == current[currentSuffix])
        {
            previousSuffix--;
            currentSuffix--;
        }

        removedCount = previousSuffix - prefix + 1;
        addedCount = currentSuffix - prefix + 1;
        return removedCount > 0 || addedCount > 0;
    }

    private static int ResolveFileTreeSelectionIndex(
        IReadOnlyList<FileTreeRow> rows,
        string? selectedPath,
        string? selectedGroupKey,
        int previousIndex)
    {
        if (rows.Count == 0)
        {
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            for (int index = 0; index < rows.Count; index++)
            {
                if (rows[index].File?.RelativePath.Equals(
                        selectedPath,
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    return index;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedGroupKey))
        {
            int nearestIndex = -1;
            int nearestDistance = int.MaxValue;
            for (int index = 0; index < rows.Count; index++)
            {
                FileTreeRow row = rows[index];
                bool sameGroup = row.Group
                    ? row.GroupKey.Equals(selectedGroupKey, StringComparison.OrdinalIgnoreCase)
                    : FileDirectoryKey(row.File!.RelativePath).Equals(
                        selectedGroupKey,
                        StringComparison.OrdinalIgnoreCase);
                if (!sameGroup)
                {
                    continue;
                }

                int distance = Math.Abs(index - previousIndex);
                if (distance < nearestDistance)
                {
                    nearestIndex = index;
                    nearestDistance = distance;
                }
            }

            if (nearestIndex >= 0)
            {
                return nearestIndex;
            }
        }

        return previousIndex < 0 ? -1 : Math.Clamp(previousIndex, 0, rows.Count - 1);
    }

    private static int ResolveFileTreeTopIndex(
        IReadOnlyList<FileTreeRow> rows,
        int previousTopIndex,
        string? previousTopAnchor)
    {
        if (rows.Count == 0)
        {
            return -1;
        }

        if (previousTopIndex <= 0)
        {
            return 0;
        }

        if (previousTopAnchor is not null)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                if (FileTreeRowAnchor(rows[index]).Equals(
                    previousTopAnchor,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        return Math.Clamp(previousTopIndex, 0, rows.Count - 1);
    }

    private static string FileTreeRowAnchor(FileTreeRow row)
    {
        return row.Group
            ? $"G:{row.GroupKey}"
            : $"F:{row.File!.RelativePath.Replace('\\', '/')}";
    }

    private static string FileDirectoryKey(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').Trim('/');
        int separator = normalized.LastIndexOf('/');
        return separator < 0 ? RootFileGroupKey : normalized[..separator];
    }

    private string? GetSelectedCommitHash()
    {
        int index = GetListSelection(_historyList);
        if (index >= 0 && index < _entries.Count)
        {
            return _entries[index].FullHash;
        }

        return _selectedCommitHash;
    }

    private static bool HistoryEntriesEqual(
        IReadOnlyList<GitHistoryEntry> left,
        IReadOnlyList<GitHistoryEntry> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!HistoryEntryEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    internal static (bool CanApply, int Prefix, int RemovedCount, int AddedCount)
        HistoryListDeltaForTest(
            IReadOnlyList<GitHistoryEntry> previous,
            IReadOnlyList<GitHistoryEntry> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        bool canApply = TryComputeHistoryListDelta(
            previous,
            current,
            out int prefix,
            out int removedCount,
            out int addedCount);
        return (canApply, prefix, removedCount, addedCount);
    }

    private static bool HistoryEntryEqual(GitHistoryEntry first, GitHistoryEntry second)
    {
        if (!first.Graph.Equals(second.Graph, StringComparison.Ordinal)
            || !first.FullHash.Equals(second.FullHash, StringComparison.OrdinalIgnoreCase)
            || !first.ShortHash.Equals(second.ShortHash, StringComparison.OrdinalIgnoreCase)
            || !first.AuthorName.Equals(second.AuthorName, StringComparison.Ordinal)
            || !first.AuthorEmail.Equals(second.AuthorEmail, StringComparison.Ordinal)
            || first.AuthorDate != second.AuthorDate
            || !first.Subject.Equals(second.Subject, StringComparison.Ordinal)
            || !first.ParentHashes.SequenceEqual(second.ParentHashes, StringComparer.OrdinalIgnoreCase)
            || first.References.Count != second.References.Count)
        {
            return false;
        }

        for (int referenceIndex = 0; referenceIndex < first.References.Count; referenceIndex++)
        {
            GitReferenceInfo firstReference = first.References[referenceIndex];
            GitReferenceInfo secondReference = second.References[referenceIndex];
            if (firstReference.Kind != secondReference.Kind
                || firstReference.IsHead != secondReference.IsHead
                || !firstReference.Name.Equals(secondReference.Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryComputeHistoryListDelta(
        IReadOnlyList<GitHistoryEntry> previous,
        IReadOnlyList<GitHistoryEntry> current,
        out int prefix,
        out int removedCount,
        out int addedCount)
    {
        prefix = 0;
        removedCount = 0;
        addedCount = 0;
        if (previous.Count == 0 || current.Count == 0)
        {
            return false;
        }

        while (prefix < previous.Count
            && prefix < current.Count
            && HistoryEntryEqual(previous[prefix], current[prefix]))
        {
            prefix++;
        }

        int previousSuffix = previous.Count - 1;
        int currentSuffix = current.Count - 1;
        while (previousSuffix >= prefix
            && currentSuffix >= prefix
            && HistoryEntryEqual(previous[previousSuffix], current[currentSuffix]))
        {
            previousSuffix--;
            currentSuffix--;
        }

        removedCount = previousSuffix - prefix + 1;
        addedCount = currentSuffix - prefix + 1;
        return removedCount > 0 || addedCount > 0;
    }

    private bool TryApplyHistoryListDelta(
        IReadOnlyList<GitHistoryEntry> nextEntries,
        string? selectedHash)
    {
        if (!TryComputeHistoryListDelta(
                _entries,
                nextEntries,
                out int prefix,
                out int removedCount,
                out int addedCount))
        {
            return false;
        }

        int topIndex = checked((int)NativeMethods.SendMessage(
            _historyList,
            NativeMethods.ListBoxGetTopIndex,
            0,
            0));
        string? topHash = topIndex > 0 && topIndex < _entries.Count
            ? _entries[topIndex].FullHash
            : null;
        _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageSetRedraw, 0, 0);
        try
        {
            for (int index = removedCount - 1; index >= 0; index--)
            {
                _ = NativeMethods.SendMessage(
                    _historyList,
                    NativeMethods.ListBoxDeleteString,
                    unchecked((nuint)(prefix + index)),
                    0);
            }

            for (int index = 0; index < addedCount; index++)
            {
                _ = NativeMethods.SendMessage(
                    _historyList,
                    NativeMethods.ListBoxInsertString,
                    unchecked((nuint)(prefix + index)),
                    FormatHistoryListEntry(nextEntries[prefix + index]));
            }

            _entries.RemoveRange(prefix, removedCount);
            _entries.InsertRange(prefix, nextEntries.Skip(prefix).Take(addedCount));
            RebuildHistoryGraph();
        }
        finally
        {
            _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageSetRedraw, 1, 0);
        }

        int selectedIndex = selectedHash is null
            ? -1
            : _entries.FindIndex(entry => entry.FullHash.Equals(
                selectedHash,
                StringComparison.OrdinalIgnoreCase));
        if (selectedIndex >= 0)
        {
            _ = NativeMethods.SendMessage(
                _historyList,
                NativeMethods.ListBoxSetCurrentSelection,
                unchecked((nuint)selectedIndex),
                0);
        }

        int restoredTopIndex = ResolveHistoryTopIndex(_entries, topIndex, topHash);
        if (restoredTopIndex >= 0)
        {
            _ = NativeMethods.SendMessage(
                _historyList,
                NativeMethods.ListBoxSetTopIndex,
                unchecked((nuint)restoredTopIndex),
                0);
        }

        _ = NativeMethods.InvalidateRectangle(_historyList, 0, true);
        _historyListDeltaCount++;
        return true;
    }

    internal static int ResolveHistoryTopIndexForTest(
        IReadOnlyList<GitHistoryEntry> entries,
        int previousTopIndex,
        string? previousTopHash)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return ResolveHistoryTopIndex(entries, previousTopIndex, previousTopHash);
    }

    private static int ResolveHistoryTopIndex(
        IReadOnlyList<GitHistoryEntry> entries,
        int previousTopIndex,
        string? previousTopHash)
    {
        if (entries.Count == 0)
        {
            return -1;
        }

        if (previousTopIndex <= 0)
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(previousTopHash))
        {
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].FullHash.Equals(
                        previousTopHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        return Math.Clamp(previousTopIndex, 0, entries.Count - 1);
    }

    private static string FormatHistoryListEntry(GitHistoryEntry entry)
    {
        string decorations = entry.References.Count == 0
            ? string.Empty
            : $"  [{string.Join(", ", entry.References.Select(reference => reference.Name))}]";
        string graph = entry.Graph.Length == 0 ? "*" : entry.Graph;
        return $"{graph} {entry.ShortHash}  {entry.Subject}{decorations}";
    }

    private static bool ReferenceSnapshotsEqual(
        GitReferenceSnapshot left,
        GitReferenceSnapshot right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Branches.Count != right.Branches.Count || left.Tags.Count != right.Tags.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Branches.Count; index++)
        {
            GitBranchInfo first = left.Branches[index];
            GitBranchInfo second = right.Branches[index];
            if (!first.Name.Equals(second.Name, StringComparison.Ordinal)
                || !first.FullName.Equals(second.FullName, StringComparison.Ordinal)
                || first.IsRemote != second.IsRemote
                || first.IsCurrent != second.IsCurrent
                || !string.Equals(first.Upstream, second.Upstream, StringComparison.Ordinal)
                || !first.CommitHash.Equals(second.CommitHash, StringComparison.OrdinalIgnoreCase)
                || !first.Subject.Equals(second.Subject, StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (int index = 0; index < left.Tags.Count; index++)
        {
            GitTagInfo first = left.Tags[index];
            GitTagInfo second = right.Tags[index];
            if (!first.Name.Equals(second.Name, StringComparison.Ordinal)
                || !first.CommitHash.Equals(second.CommitHash, StringComparison.OrdinalIgnoreCase)
                || first.IsAnnotated != second.IsAnnotated
                || !string.Equals(first.Message, second.Message, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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
        if (_metadataLabel != 0)
        {
            SetMetadataMessage(message);
        }
    }

    private nint PaintBackground(nint deviceContext)
    {
        if (deviceContext == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle rectangle))
        {
            return 0;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint panel = palette.Panel;
        uint separator = palette.Border;
        uint control = palette.Panel;
        uint controlBorder = palette.BorderStrong;
        Fill(deviceContext, rectangle, panel);
        int width = Math.Max(0, rectangle.Right - rectangle.Left);
        if (_fileHistoryMode)
        {
            NativeMethods.Rectangle fileHistoryHeaderSeparator = new()
            {
                Left = 0,
                Top = HeaderHeight - 1,
                Right = rectangle.Right,
                Bottom = HeaderHeight,
            };
            Fill(deviceContext, fileHistoryHeaderSeparator, separator);
            int detailWidth = _showDetails
                ? Math.Clamp(
                    (int)Math.Round(width * 0.42d),
                    Math.Min(NativeTheme.Scale(360), width),
                    Math.Min(NativeTheme.Scale(520), width))
                : 0;
            int detailLeft = Math.Max(0, width - detailWidth);
            NativeMethods.Rectangle fileHistoryToolbarSeparator = new()
            {
                Left = 0,
                Top = HeaderHeight + FileHistoryToolbarHeight - 1,
                Right = rectangle.Right,
                Bottom = HeaderHeight + FileHistoryToolbarHeight,
            };
            Fill(deviceContext, fileHistoryToolbarSeparator, separator);
            if (_showDetails && detailWidth > 0)
            {
                NativeMethods.Rectangle detailSeparator = new()
                {
                    Left = detailLeft - 1,
                    Top = HeaderHeight,
                    Right = detailLeft,
                    Bottom = rectangle.Bottom,
                };
                Fill(deviceContext, detailSeparator, separator);
            }
            return 1;
        }

        int contentLeft = SideToolbarWidth;
        (int branchWidth, int logWidth, int detailsWidth) = GetColumnWidths(Math.Max(0, width - contentLeft));
        if (!_showDetails)
        {
            logWidth += detailsWidth;
            detailsWidth = 0;
        }
        NativeMethods.Rectangle headerSeparator = new()
        {
            Left = 0,
            Top = HeaderHeight - 1,
            Right = rectangle.Right,
            Bottom = HeaderHeight,
        };
        Fill(deviceContext, headerSeparator, separator);
        NativeMethods.Rectangle filterSeparator = new()
        {
            Left = 0,
            Top = ContentTop - 1,
            Right = rectangle.Right,
            Bottom = ContentTop,
        };
        Fill(deviceContext, filterSeparator, separator);
        NativeMethods.Rectangle toolbarSeparator = new()
        {
            Left = contentLeft - 1,
            Top = HeaderHeight,
            Right = contentLeft,
            Bottom = rectangle.Bottom,
        };
        Fill(deviceContext, toolbarSeparator, separator);
        NativeMethods.Rectangle sideToolbarGroupSeparator = new()
        {
            Left = NativeTheme.Scale(7),
            Top = ContentTop + NativeTheme.Scale(1),
            Right = Math.Max(NativeTheme.Scale(7), contentLeft - NativeTheme.Scale(7)),
            Bottom = ContentTop + NativeTheme.Scale(2),
        };
        Fill(deviceContext, sideToolbarGroupSeparator, separator);
        NativeMethods.Rectangle branchSeparator = new()
        {
            Left = contentLeft + branchWidth,
            Top = HeaderHeight,
            Right = contentLeft + branchWidth + 1,
            Bottom = rectangle.Bottom,
        };
        Fill(deviceContext, branchSeparator, separator);
        if (_showDetails)
        {
            NativeMethods.Rectangle detailsSeparator = new()
            {
                Left = contentLeft + branchWidth + logWidth,
                Top = HeaderHeight,
                Right = contentLeft + branchWidth + logWidth + 1,
                Bottom = rectangle.Bottom,
            };
            Fill(deviceContext, detailsSeparator, separator);
        }
        PaintRoundedInput(deviceContext, _branchFilterEditFrame, controlBorder, control);
        PaintRoundedInput(deviceContext, _filterEditFrame, controlBorder, control);
        foreach (NativeMethods.Rectangle frame in new[] { _branchFilterEditFrame, _filterEditFrame })
        {
            NativeMethods.Rectangle icon = new()
            {
                Left = frame.Left + NativeTheme.Scale(6),
                Right = frame.Left + NativeTheme.Scale(22),
                Top = (frame.Top + frame.Bottom) / 2 - NativeTheme.Scale(8),
                Bottom = (frame.Top + frame.Bottom) / 2 + NativeTheme.Scale(8),
            };
            _ = NativeTheme.DrawNavigationIcon(deviceContext, icon, NativeNavigationIcon.Search, palette.Muted);
        }
        return 1;
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        if (item.ControlIdentifier == BranchesListIdentifier)
        {
            return DrawBranchItem(item);
        }
        if (item.ControlIdentifier == HistoryListIdentifier)
        {
            return DrawHistoryItem(item);
        }
        if (item.ControlIdentifier == FilesListIdentifier)
        {
            return DrawFileItem(item);
        }
        if (item.ControlIdentifier == 61)
        {
            return DrawCommitDetailsItem(item);
        }
        if (item.ControlIdentifier is CommandBack
            or CommandCreateReference
            or CommandDeleteReference
            or CommandRefresh
            or CommandSearch
            or CommandCompare
            or CommandLocateHead
            or CommandToggleDetails
            or CommandFilterOverflow
            or CommandToolbarOverflow
            or CommandPreviousPage
            or CommandNextPage
            or CommandFileHistoryClear
            or CommandFileHistoryEdit
            or CommandFileHistoryExpand
            or CommandFileHistoryUnified
            or CommandFileHistorySplit
            or CommandFileHistorySettings
            or CommandMoreActions
            or CommandClosePanel)
        {
            return DrawHeaderToolButton(item);
        }
        if (item.ControlIdentifier is CommandChooseFilterKind or CommandFilterAuthor or CommandFilterDate or CommandFilterPath)
        {
            return DrawFilterKindButton(item);
        }
        if (item.ControlIdentifier is not (60 or 61 or 62 or 65 or 66 or 67))
        {
            return NativeTheme.DrawFlatButton(parameter, NativeTheme.IsDark(_settings.Theme));
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint panel = palette.Panel;
        uint text = palette.Text;
        uint muted = palette.Muted;
        Fill(item.DeviceContext, item.ItemRectangle, panel);
        bool activeTab = item.ControlIdentifier == 60
            ? !_fileHistoryMode
            : item.ControlIdentifier == 66 && _fileHistoryMode;
        if (activeTab)
        {
            NativeMethods.Rectangle selectedTab = item.ItemRectangle;
            (uint tabBorder, uint tabFill, _) = NativeTheme.SelectedToolTabColors(dark);
            FillRounded(
                item.DeviceContext,
                selectedTab,
                tabBorder,
                NativeTheme.Scale(10));
            int tabInset = Math.Max(1, NativeTheme.Scale(1));
            selectedTab.Left += tabInset;
            selectedTab.Top += tabInset;
            selectedTab.Right -= tabInset;
            selectedTab.Bottom -= tabInset;
            FillRounded(
                item.DeviceContext,
                selectedTab,
                tabFill,
                NativeTheme.Scale(8));
        }
        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        // 标题已经由布局定位到 12px；标签的 10px 内距与 1px 边框只计算一次。
        if (item.ControlIdentifier != 62)
        {
            bool headerTab = item.ControlIdentifier is 60 or 66;
            textRectangle.Left += NativeTheme.Scale(headerTab ? HeaderTabTextInset : item.ControlIdentifier == 61 ? 8 : 7);
            textRectangle.Right -= NativeTheme.Scale(headerTab ? HeaderTabTextInset : 6);
        }
        nint font = item.ControlIdentifier is 62 or 65 or 67
            ? NativeTheme.UiMediumFont
            : NativeTheme.UiFont;
        nint previousFont = NativeMethods.SelectObject(item.DeviceContext, font);
        _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(
            item.DeviceContext,
            text);
        string label = NativeMethods.GetWindowTextValue(item.Control);
        bool centeredEmptyMetadata = item.ControlIdentifier == 61 && ShouldCenterMetadata(label);
        uint format = NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis;
        if (centeredEmptyMetadata)
        {
            format |= NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextCenter;
        }
        else if (item.ControlIdentifier == 61)
        {
            format |= NativeMethods.DrawTextWordBreak;
            textRectangle.Top += NativeTheme.Scale(6);
        }
        else
        {
            format |= NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextVerticalCenter;
        }

        _ = NativeMethods.DrawText(item.DeviceContext, label, label.Length, ref textRectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previousFont);
        }

        return true;
    }

    private bool DrawCommitDetailsItem(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);

        NativeMethods.Rectangle content = item.ItemRectangle;
        int inset = NativeTheme.Scale(10);
        content.Left += inset;
        content.Right -= inset;
        content.Top += NativeTheme.Scale(8);
        content.Bottom -= NativeTheme.Scale(8);
        if (content.Right <= content.Left || content.Bottom <= content.Top)
        {
            return true;
        }

        if (_detailsTitle is null)
        {
            DrawDetailsText(
                item.DeviceContext,
                NativeMethods.GetWindowTextValue(item.Control),
                content,
                palette.Muted,
                NativeTheme.UiFont,
                _detailsMessageCentered,
                wordBreak: !_detailsMessageCentered);
            return true;
        }

        var textLayout = CalculateCommitDetailsTextLayout(content.Top - _detailsScrollPosition,
            content.Bottom, !string.IsNullOrWhiteSpace(_detailsReferences));
        int titleTop = textLayout.TitleTop, metaTop = textLayout.MetaTop,
            referencesTop = textLayout.ReferencesTop, bodyTop = textLayout.BodyTop;
        NativeMethods.Rectangle title = new()
        {
            Left = content.Left,
            Top = titleTop,
            Right = content.Right,
            Bottom = metaTop,
        };
        NativeMethods.Rectangle meta = new()
        {
            Left = content.Left,
            Top = metaTop,
            Right = content.Right,
            Bottom = referencesTop,
        };
        NativeMethods.Rectangle references = new()
        {
            Left = content.Left,
            Top = referencesTop,
            Right = content.Right,
            Bottom = bodyTop,
        };
        DrawDetailsText(item.DeviceContext, _detailsTitle, title, palette.Text, NativeTheme.UiMediumFont, false, true);
        DrawDetailsText(item.DeviceContext, _detailsMeta ?? string.Empty, meta, palette.Muted, NativeTheme.UiFont, false, false);
        if (!string.IsNullOrWhiteSpace(_detailsReferences))
        {
            DrawDetailsText(item.DeviceContext, _detailsReferences, references, palette.Muted, NativeTheme.UiFont, false, false);
        }

        DrawDetailsBody(item.DeviceContext, item.ItemRectangle, palette.Text);

        return true;
    }

    private static void DrawDetailsText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font,
        bool centered,
        bool wordBreak)
    {
        if (rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
        {
            return;
        }

        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextNoPrefix;
        if (wordBreak)
        {
            format |= NativeMethods.DrawTextWordBreak;
        }
        else
        {
            format |= NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextEndEllipsis;
        }

        if (centered)
        {
            format |= NativeMethods.DrawTextCenter;
        }

        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private static (int TitleTop, int MetaTop, int ReferencesTop, int BodyTop, int BodyHeight)
        CalculateCommitDetailsTextLayout(int top, int bottom, bool hasReferences)
    {
        int titleTop = top;
        // 详情标题沿用视觉稿的自然换行，窄栏最多稳定容纳两行。
        int metaTop = titleTop + Math.Max(NativeTheme.Scale(40), NativeTheme.UiLineHeight * 2);
        int referencesTop = metaTop + NativeTheme.ContentHeight(20, 0);
        int bodyTop = referencesTop
            + (hasReferences ? NativeTheme.ContentHeight(20, 0) : 0)
            + NativeTheme.Scale(8);
        int bodyHeight = Math.Max(0, bottom - bodyTop);
        return (titleTop, metaTop, referencesTop, bodyTop, bodyHeight);
    }

    internal static bool ShouldCenterMetadataForTest(string text)
    {
        return ShouldCenterMetadata(text);
    }

    internal static (int TitleTop, int MetaTop, int ReferencesTop, int BodyTop, int BodyHeight)
        CalculateCommitDetailsTextLayoutForTest(int top, int bottom, bool hasReferences)
    {
        return CalculateCommitDetailsTextLayout(top, bottom, hasReferences);
    }

    private static bool ShouldCenterMetadata(string text)
    {
        return text.Equals(UiText.SelectCommit, StringComparison.Ordinal);
    }

    private bool DrawFilterKindButton(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint text = palette.Text;
        bool active = IsFilterKindActive(unchecked((int)item.ControlIdentifier), _filterKindIndex);
        bool hovered = (item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0;
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        if (active || hovered)
        {
            FillRounded(
                item.DeviceContext,
                item.ItemRectangle,
                active ? palette.AccentSoft : palette.Hover,
                NativeTheme.Scale(5));
        }

        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        textRectangle.Left += NativeTheme.Scale(6);
        textRectangle.Right -= NativeTheme.Scale(18);
        DrawText(
            item.DeviceContext,
            NativeMethods.GetWindowTextValue(item.Control),
            textRectangle,
            active ? palette.Accent : text,
            NativeTheme.UiFont,
            centered: false);
        NativeMethods.Rectangle arrow = item.ItemRectangle;
        arrow.Left = arrow.Right - NativeTheme.Scale(16);
        arrow.Right -= NativeTheme.Scale(4);
        _ = NativeTheme.DrawChevronIcon(item.DeviceContext, arrow, expanded: true,
            active ? palette.Accent : palette.Muted);
        NativeTheme.DrawToolbarFocus(item, palette);

        return true;
    }

    internal static bool IsFilterKindActiveForTest(int controlIdentifier, int filterKindIndex)
    {
        return IsFilterKindActive(controlIdentifier, filterKindIndex);
    }

    private static bool IsFilterKindActive(int controlIdentifier, int filterKindIndex)
    {
        return controlIdentifier switch
        {
            CommandChooseFilterKind => filterKindIndex == 5,
            CommandFilterAuthor => filterKindIndex == 2,
            CommandFilterDate => filterKindIndex is 3 or 4,
            CommandFilterPath => filterKindIndex == 6,
            _ => false,
        };
    }

    private bool DrawHeaderToolButton(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint panel = palette.Panel;
        uint hover = palette.Hover;
        uint color = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0
            ? palette.Faint
            : palette.Muted;
        Fill(item.DeviceContext, item.ItemRectangle, panel);
        if ((item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0)
        {
            FillRounded(item.DeviceContext, item.ItemRectangle, hover, NativeTheme.Scale(5));
        }

        int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        if (item.ControlIdentifier == CommandMoreActions)
        {
            DrawEllipsis(item.DeviceContext, centerX, centerY, color);
        }
        else if (!DrawHeaderIcon(
                item.DeviceContext,
                unchecked((int)item.ControlIdentifier),
                centerX,
                centerY,
                color))
        {
            DrawHeaderIconFallback(
                item.DeviceContext,
                unchecked((int)item.ControlIdentifier),
                centerX,
                centerY,
                color);
        }
        NativeTheme.DrawToolbarFocus(item, palette);
        return true;
    }

    private static void DrawHeaderIconFallback(nint deviceContext, int command, int centerX, int centerY, uint color)
    {
        if (command == CommandFileHistorySettings)
        {
            _ = DrawHeaderIcon(deviceContext, command, centerX, centerY, color);
            return;
        }
        nint pen = NativeMethods.CreatePen(
            NativeMethods.PenStyleSolid,
            Math.Max(1, (int)Math.Round(SideToolbarIconStrokeWidthForTest)),
            color);
        if (pen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
        int x = centerX;
        int y = centerY;
        if (command == CommandBack)
        {
            _ = NativeMethods.MoveTo(deviceContext, x + NativeTheme.Scale(4), y - NativeTheme.Scale(6), 0);
            _ = NativeMethods.LineTo(deviceContext, x - NativeTheme.Scale(2), y);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(4), y + NativeTheme.Scale(6));
        }
        else if (command == CommandCreateReference)
        {
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(6), y, 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(6), y);
            _ = NativeMethods.MoveTo(deviceContext, x, y - NativeTheme.Scale(6), 0);
            _ = NativeMethods.LineTo(deviceContext, x, y + NativeTheme.Scale(6));
        }
        else if (command == CommandDeleteReference)
        {
            _ = NativeMethods.DrawRectangle(
                deviceContext,
                x - NativeTheme.Scale(5),
                y - NativeTheme.Scale(3),
                x + NativeTheme.Scale(5),
                y + NativeTheme.Scale(7));
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(7), y - NativeTheme.Scale(5), 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(7), y - NativeTheme.Scale(5));
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(3), y - NativeTheme.Scale(7), 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(3), y - NativeTheme.Scale(7));
        }
        else if (command == CommandRefresh)
        {
            _ = NativeMethods.DrawEllipse(
                deviceContext,
                x - NativeTheme.Scale(6),
                y - NativeTheme.Scale(6),
                x + NativeTheme.Scale(6),
                y + NativeTheme.Scale(6));
            _ = NativeMethods.MoveTo(deviceContext, x + NativeTheme.Scale(6), y - NativeTheme.Scale(5), 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(1), y - NativeTheme.Scale(6));
            _ = NativeMethods.MoveTo(deviceContext, x + NativeTheme.Scale(6), y - NativeTheme.Scale(5), 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(6), y);
        }
        else if (command == CommandSearch)
        {
            _ = NativeMethods.DrawEllipse(
                deviceContext,
                x - NativeTheme.Scale(6),
                y - NativeTheme.Scale(6),
                x + NativeTheme.Scale(3),
                y + NativeTheme.Scale(3));
            _ = NativeMethods.MoveTo(deviceContext, x + NativeTheme.Scale(2), y + NativeTheme.Scale(2), 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(7), y + NativeTheme.Scale(7));
        }
        else if (command == CommandToggleDetails)
        {
            _ = NativeMethods.DrawEllipse(
                deviceContext,
                x - NativeTheme.Scale(8),
                y - NativeTheme.Scale(5),
                x + NativeTheme.Scale(8),
                y + NativeTheme.Scale(5));
            _ = NativeMethods.DrawEllipse(
                deviceContext,
                x - NativeTheme.Scale(2),
                y - NativeTheme.Scale(2),
                x + NativeTheme.Scale(2),
                y + NativeTheme.Scale(2));
        }
        else if (command == CommandCompare)
        {
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(7), y - NativeTheme.Scale(4), 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(5), y - NativeTheme.Scale(4));
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(2), y - NativeTheme.Scale(7));
            _ = NativeMethods.MoveTo(deviceContext, x + NativeTheme.Scale(7), y + NativeTheme.Scale(4), 0);
            _ = NativeMethods.LineTo(deviceContext, x - NativeTheme.Scale(5), y + NativeTheme.Scale(4));
            _ = NativeMethods.LineTo(deviceContext, x - NativeTheme.Scale(2), y + NativeTheme.Scale(7));
        }
        else if (command == CommandLocateHead)
        {
            _ = NativeMethods.DrawEllipse(
                deviceContext,
                x - NativeTheme.Scale(5),
                y - NativeTheme.Scale(5),
                x + NativeTheme.Scale(5),
                y + NativeTheme.Scale(5));
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(8), y, 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(8), y);
            _ = NativeMethods.MoveTo(deviceContext, x, y - NativeTheme.Scale(8), 0);
            _ = NativeMethods.LineTo(deviceContext, x, y + NativeTheme.Scale(8));
        }
        else if (command == CommandClosePanel)
        {
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(5), y, 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(5), y);
        }
        else if (command == CommandFileHistoryEdit)
        {
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(5), y + NativeTheme.Scale(5), 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(4), y - NativeTheme.Scale(4));
            _ = NativeMethods.MoveTo(deviceContext, x + NativeTheme.Scale(3), y - NativeTheme.Scale(5), 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(6), y - NativeTheme.Scale(2));
        }
        else if (command == CommandFileHistoryExpand)
        {
            _ = NativeMethods.MoveTo(deviceContext, x, y - NativeTheme.Scale(7), 0);
            _ = NativeMethods.LineTo(deviceContext, x, y + NativeTheme.Scale(7));
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(4), y - NativeTheme.Scale(3), 0);
            _ = NativeMethods.LineTo(deviceContext, x, y - NativeTheme.Scale(7));
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(4), y - NativeTheme.Scale(3));
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(4), y + NativeTheme.Scale(3), 0);
            _ = NativeMethods.LineTo(deviceContext, x, y + NativeTheme.Scale(7));
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(4), y + NativeTheme.Scale(3));
        }
        else if (command is CommandFileHistoryUnified or CommandFileHistorySplit)
        {
            _ = NativeMethods.DrawRectangle(
                deviceContext,
                x - NativeTheme.Scale(7),
                y - NativeTheme.Scale(6),
                x + NativeTheme.Scale(7),
                y + NativeTheme.Scale(6));
            if (command == CommandFileHistorySplit)
            {
                _ = NativeMethods.MoveTo(deviceContext, x, y - NativeTheme.Scale(6), 0);
                _ = NativeMethods.LineTo(deviceContext, x, y + NativeTheme.Scale(6));
            }
        }
        else if (command == CommandFileHistoryClear)
        {
            _ = NativeMethods.MoveTo(deviceContext, x - NativeTheme.Scale(5), y - NativeTheme.Scale(5), 0);
            _ = NativeMethods.LineTo(deviceContext, x + NativeTheme.Scale(5), y + NativeTheme.Scale(5));
            _ = NativeMethods.MoveTo(deviceContext, x + NativeTheme.Scale(5), y - NativeTheme.Scale(5), 0);
            _ = NativeMethods.LineTo(deviceContext, x - NativeTheme.Scale(5), y + NativeTheme.Scale(5));
        }
        else if (command is CommandPreviousPage or CommandNextPage)
        {
            int direction = command == CommandPreviousPage ? -1 : 1;
            _ = NativeMethods.MoveTo(
                deviceContext,
                x - direction * NativeTheme.Scale(2),
                y - NativeTheme.Scale(4),
                0);
            _ = NativeMethods.LineTo(deviceContext, x + direction * NativeTheme.Scale(2), y);
            _ = NativeMethods.LineTo(
                deviceContext,
                x - direction * NativeTheme.Scale(2),
                y + NativeTheme.Scale(4));
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
    }

    private static bool DrawHeaderIcon(nint deviceContext, int command, int centerX, int centerY, uint color)
    {
        if (command is CommandFilterOverflow or CommandToolbarOverflow)
        {
            NativeMethods.Rectangle arrow = new()
            {
                Left = centerX - NativeTheme.Scale(8),
                Right = centerX + NativeTheme.Scale(8),
                Top = centerY - NativeTheme.Scale(8),
                Bottom = centerY + NativeTheme.Scale(8),
            };
            return NativeTheme.DrawNavigationIcon(deviceContext, arrow, NativeNavigationIcon.Right, color);
        }
        float x = centerX;
        float y = centerY;
        float unit = SideToolbarIconStrokeWidthForTest;
        if (command == CommandBack)
        {
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines =
            [
                new(x + NativeTheme.Scale(4), y - NativeTheme.Scale(6), x - NativeTheme.Scale(2), y),
                new(x - NativeTheme.Scale(2), y, x + NativeTheme.Scale(4), y + NativeTheme.Scale(6)),
            ];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, [], []);
        }

        if (command == CommandCreateReference)
        {
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines =
            [
                new(x - NativeTheme.Scale(6), y, x + NativeTheme.Scale(6), y),
                new(x, y - NativeTheme.Scale(6), x, y + NativeTheme.Scale(6)),
            ];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, [], []);
        }

        if (command == CommandDeleteReference)
        {
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines =
            [
                new(x - NativeTheme.Scale(7), y - NativeTheme.Scale(5), x + NativeTheme.Scale(7), y - NativeTheme.Scale(5)),
                new(x - NativeTheme.Scale(3), y - NativeTheme.Scale(7), x + NativeTheme.Scale(3), y - NativeTheme.Scale(7)),
            ];
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeRectangle> rectangles =
            [
                new(x - NativeTheme.Scale(5), y - NativeTheme.Scale(3), NativeTheme.Scale(10), NativeTheme.Scale(10)),
            ];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, [], rectangles);
        }

        if (command == CommandRefresh)
        {
            return NativeTheme.DrawRefreshIcon(deviceContext, centerX, centerY, color);
        }

        if (command == CommandSearch)
        {
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines =
            [
                new(x + NativeTheme.Scale(2), y + NativeTheme.Scale(2), x + NativeTheme.Scale(7), y + NativeTheme.Scale(7)),
            ];
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeEllipse> ellipses =
            [
                new(x - NativeTheme.Scale(6), y - NativeTheme.Scale(6), NativeTheme.Scale(9), NativeTheme.Scale(9)),
            ];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, ellipses, []);
        }

        if (command == CommandToggleDetails)
        {
            return NativeTheme.DrawPreviewIcon(deviceContext, centerX, centerY, color);
        }

        if (command == CommandCompare)
        {
            return NativeTheme.DrawCompareIcon(deviceContext, centerX, centerY, color);
        }

        if (command == CommandLocateHead)
        {
            return NativeTheme.DrawLocateIcon(deviceContext, centerX, centerY, color);
        }

        if (command == CommandClosePanel)
        {
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines =
            [
                new(x - NativeTheme.Scale(5), y, x + NativeTheme.Scale(5), y),
            ];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, [], []);
        }

        if (command == CommandFileHistoryEdit)
        {
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines =
            [
                new(x - NativeTheme.Scale(5), y + NativeTheme.Scale(5), x + NativeTheme.Scale(4), y - NativeTheme.Scale(4)),
                new(x + NativeTheme.Scale(3), y - NativeTheme.Scale(5), x + NativeTheme.Scale(6), y - NativeTheme.Scale(2)),
            ];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, [], []);
        }

        if (command == CommandFileHistoryExpand)
        {
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines =
            [
                new(x, y - NativeTheme.Scale(7), x, y + NativeTheme.Scale(7)),
                new(x - NativeTheme.Scale(4), y - NativeTheme.Scale(3), x, y - NativeTheme.Scale(7)),
                new(x, y - NativeTheme.Scale(7), x + NativeTheme.Scale(4), y - NativeTheme.Scale(3)),
                new(x - NativeTheme.Scale(4), y + NativeTheme.Scale(3), x, y + NativeTheme.Scale(7)),
                new(x, y + NativeTheme.Scale(7), x + NativeTheme.Scale(4), y + NativeTheme.Scale(3)),
            ];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, [], []);
        }

        if (command is CommandFileHistoryUnified or CommandFileHistorySplit)
        {
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeRectangle> rectangles =
            [new(x - NativeTheme.Scale(7), y - NativeTheme.Scale(6), NativeTheme.Scale(14), NativeTheme.Scale(12))];
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines = command == CommandFileHistorySplit
                ? [new(x, y - NativeTheme.Scale(6), x, y + NativeTheme.Scale(6))]
                : [];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, [], rectangles);
        }

        if (command == CommandFileHistorySettings)
        {
            NativeMethods.Rectangle rectangle = new()
            {
                Left = (int)(x - NativeTheme.Scale(8)),
                Top = (int)(y - NativeTheme.Scale(8)),
                Right = (int)(x + NativeTheme.Scale(8)),
                Bottom = (int)(y + NativeTheme.Scale(8)),
            };
            return NativeTheme.DrawSettingsIcon(deviceContext, rectangle, color);
        }

        if (command == CommandFileHistoryClear)
        {
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines =
            [
                new(x - NativeTheme.Scale(5), y - NativeTheme.Scale(5), x + NativeTheme.Scale(5), y + NativeTheme.Scale(5)),
                new(x + NativeTheme.Scale(5), y - NativeTheme.Scale(5), x - NativeTheme.Scale(5), y + NativeTheme.Scale(5)),
            ];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, [], []);
        }

        if (command is CommandPreviousPage or CommandNextPage)
        {
            int direction = command == CommandPreviousPage ? -1 : 1;
            ReadOnlySpan<NativeGdiPlusDrawing.StrokeLine> lines =
            [
                new(x - direction * NativeTheme.Scale(2), y - NativeTheme.Scale(4), x + direction * NativeTheme.Scale(2), y),
                new(x + direction * NativeTheme.Scale(2), y, x - direction * NativeTheme.Scale(2), y + NativeTheme.Scale(4)),
            ];
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, unit, lines, [], []);
        }

        return false;
    }

    private static void DrawEllipsis(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawMoreIcon(deviceContext, centerX, centerY, color);
    }

    private bool DrawBranchItem(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint panel = palette.Panel;
        uint text = palette.Text;
        uint muted = palette.Muted;
        uint selection = NativeTheme.SelectionColor(palette, NativeMethods.GetFocus() == _branchesList);
        Fill(item.DeviceContext, item.ItemRectangle, panel);
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= _branchRows.Count)
        {
            return true;
        }

        BranchRow row = _branchRows[index];
        if ((item.ItemState & NativeMethods.OwnerDrawSelected) != 0)
        {
            Fill(item.DeviceContext, item.ItemRectangle, selection);
        }
        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        textRectangle.Left += NativeTheme.Scale(10 + row.Indent * 22);
        if (row.Group)
        {
            DrawChevron(
                item.DeviceContext,
                textRectangle.Left,
                (textRectangle.Top + textRectangle.Bottom) / 2,
                !_collapsedBranchSections.Contains(row.Text),
                muted);
            textRectangle.Left += NativeTheme.Scale(16);
        }
        else if (row.Indent > 0)
        {
            NativeMethods.Rectangle tag = new()
            {
                Left = textRectangle.Left,
                Top = textRectangle.Top + NativeTheme.Scale(5),
                Right = textRectangle.Left + NativeTheme.Scale(16),
                Bottom = textRectangle.Bottom - NativeTheme.Scale(5),
            };
            _ = NativeTheme.DrawGitReferenceIcon(
                item.DeviceContext,
                tag,
                dark,
                (item.ItemState & NativeMethods.OwnerDrawSelected) != 0 ? selection : panel,
                filled: true);
            textRectangle.Left += NativeTheme.Scale(18);
        }
        DrawText(
            item.DeviceContext,
            row.Text,
            textRectangle,
            row.Accent ? text : row.Group ? text : muted,
            row.Group ? NativeTheme.UiMediumFont : NativeTheme.UiFont,
            centered: false);
        return true;
    }

    private bool DrawHistoryItem(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        (uint panel, uint selection) = HistoryRowColors(dark, NativeMethods.GetFocus() == _historyList);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint text = palette.Text;
        uint muted = palette.Muted;
        Fill(item.DeviceContext, item.ItemRectangle, (item.ItemState & NativeMethods.OwnerDrawSelected) != 0 ? selection : panel);
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= _entries.Count)
        {
            return true;
        }

        GitHistoryEntry entry = _entries[index];
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        NativeMethods.Rectangle rowRectangle = item.ItemRectangle;
        // ListBox 已按横向偏移平移绘图上下文；这里继续使用内容坐标，不能再次扣除滚动值。
        rowRectangle.Left = 0;
        rowRectangle.Right = rowRectangle.Left + _historyRowExtent;
        if (_fileHistoryMode)
        {
            DrawFileHistoryRow(
                item.DeviceContext,
                rowRectangle,
                entry,
                text,
                _fileHistoryAuthorWidth,
                _fileHistoryDateWidth);
            return true;
        }
        _commitGraph.DrawRow(item.DeviceContext, rowRectangle, index, dark,
            (item.ItemState & NativeMethods.OwnerDrawSelected) != 0 ? selection : panel);
        int rowWidth = Math.Max(0, rowRectangle.Right - rowRectangle.Left);
        EnsureHistoryRowTextMetrics(item.DeviceContext);
        NativeHistoryRowTextLayout layout = CalculateHistoryRowTextLayout(
            rowWidth - _commitGraph.Width + NativeTheme.Scale(29), _rowReferenceWidth, _rowAuthorWidth, _rowFullDateWidth, _rowCompactDateWidth);
        NativeMethods.Rectangle subjectRectangle = rowRectangle;
        subjectRectangle.Left += _commitGraph.Width;
        subjectRectangle.Right = Math.Min(
            subjectRectangle.Right,
            subjectRectangle.Left + layout.SubjectWidth);
        DrawText(item.DeviceContext, entry.Subject, subjectRectangle, text, NativeTheme.UiFont, centered: false);
        NativeMethods.Rectangle metadata = rowRectangle;
        metadata.Right -= NativeTheme.Scale(8);
        metadata.Left = metadata.Right - layout.DateWidth;
        DrawText(item.DeviceContext, FormatHistoryDate(entry, layout.CompactDate), metadata, muted, NativeTheme.UiFont, centered: false);
        if (layout.AuthorWidth > 0)
        {
            metadata.Right = metadata.Left - NativeTheme.Scale(8);
            metadata.Left = metadata.Right - layout.AuthorWidth;
            DrawText(item.DeviceContext, entry.AuthorName, metadata, muted, NativeTheme.UiFont, centered: false);
        }
        if (layout.ReferenceWidth > 0 && entry.References.Count > 0)
        {
            metadata.Right = metadata.Left - NativeTheme.Scale(8);
            metadata.Left = metadata.Right - layout.ReferenceWidth;
            NativeMethods.Rectangle tag = new()
            {
                Left = metadata.Left,
                Top = centerY - NativeTheme.Scale(8),
                Right = metadata.Left + NativeTheme.Scale(16),
                Bottom = centerY + NativeTheme.Scale(8),
            };
            _ = NativeTheme.DrawGitReferenceIcon(item.DeviceContext, tag, dark,
                (item.ItemState & NativeMethods.OwnerDrawSelected) != 0 ? selection : panel, filled: false);
            metadata.Left += NativeTheme.Scale(20);
            DrawText(item.DeviceContext, FormatReferenceNames(entry), metadata, muted, NativeTheme.UiFont, centered: false);
        }
        return true;
    }

    private static (uint Panel, uint Selection) HistoryRowColors(bool dark, bool hasFocus)
    {
        NativeThemePalette palette = NativeTheme.Palette(dark);
        return (palette.Panel, hasFocus ? palette.AccentSoft : palette.HistorySelectionInactive);
    }

    private static string FormatHistoryMeta(GitHistoryEntry entry)
    {
        string reference = FormatReferenceNames(entry);
        return string.Join(
            "  ",
            new[] { reference, entry.AuthorName, FormatHistoryDate(entry, compact: false) }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatReferenceNames(GitHistoryEntry entry) =>
        string.Join(" · ", entry.References.Select(reference => reference.Name));

    private static string FormatHistoryDate(GitHistoryEntry entry, bool compact)
    {
        string format = compact
            ? entry.AuthorDate.LocalDateTime.Date == DateTime.Today ? "HH:mm" : "MM-dd"
            : "yyyy/M/d HH:mm";
        return entry.AuthorDate.LocalDateTime.ToString(format, CultureInfo.InvariantCulture);
    }

    private void EnsureHistoryRowTextMetrics(nint deviceContext)
    {
        if (!_rowTextMetricsDirty && _rowTextFont == NativeTheme.UiFont) return;
        _rowTextFont = NativeTheme.UiFont;
        _rowTextMetricsDirty = false;
        _rowReferenceWidth = 0;
        _rowAuthorWidth = 0;
        _rowFullDateWidth = 0;
        _rowCompactDateWidth = 0;
        _fileHistoryAuthorWidth = NativeTheme.Scale(130);
        _fileHistoryDateWidth = NativeTheme.Scale(90);
        // 每次快照或字体变化只度量一次，共享列宽使作者与日期在所有可见行中对齐。
        foreach (GitHistoryEntry entry in _entries)
        {
            int authorWidth = MeasureTextWidth(deviceContext, entry.AuthorName);
            int fullDateWidth = MeasureTextWidth(deviceContext, FormatHistoryDate(entry, compact: false));
            int referenceWidth = MeasureTextWidth(deviceContext, FormatReferenceNames(entry));
            _rowAuthorWidth = Math.Max(_rowAuthorWidth, authorWidth);
            _fileHistoryAuthorWidth = Math.Max(_fileHistoryAuthorWidth, authorWidth + NativeTheme.Scale(4));
            _fileHistoryDateWidth = Math.Max(_fileHistoryDateWidth, fullDateWidth + NativeTheme.Scale(4));
            if (entry.References.Count > 0)
                _rowReferenceWidth = Math.Max(_rowReferenceWidth, NativeTheme.Scale(20) + referenceWidth);
            _rowFullDateWidth = Math.Max(_rowFullDateWidth, fullDateWidth);
            _rowCompactDateWidth = Math.Max(_rowCompactDateWidth, MeasureTextWidth(deviceContext, FormatHistoryDate(entry, compact: true)));
        }
        _rowAuthorWidth = Math.Min(_rowAuthorWidth, NativeTheme.Scale(96));
        _rowReferenceWidth = Math.Min(_rowReferenceWidth, NativeTheme.Scale(128));
    }

    private void RebuildHistoryGraph()
    {
        _commitGraph = NativeCommitGraph.Build(_entries);
        _commitGraphBuildCount++;
        _rowTextMetricsDirty = true;
        UpdateHistoryRowExtent();
    }

    private void UpdateHistoryRowExtent()
    {
        if (_historyList == 0 || _updatingHistoryExtent) return;
        _updatingHistoryExtent = true;
        nint dc = NativeMethods.GetDeviceContext(_historyList);
        try
        {
            EnsureHistoryRowTextMetrics(dc);
            for (int pass = 0; pass < 3; pass++)
            {
                _ = NativeMethods.GetClientRectangle(_historyList, out NativeMethods.Rectangle client);
                _historyRowExtent = _fileHistoryMode
                    ? CalculateFileHistoryRowExtent(
                        client.Right,
                        _fileHistoryAuthorWidth,
                        _fileHistoryDateWidth)
                    : CalculateHistoryRowExtent(client.Right, _commitGraph.Width, _rowAuthorWidth, _rowCompactDateWidth);
                int extent = _historyRowExtent > client.Right ? _historyRowExtent : 0;
                if (NativeMethods.SendMessage(_historyList, NativeMethods.ListBoxGetHorizontalExtent, 0, 0) == extent)
                {
                    if (extent == 0 && NativeMethods.GetScrollPosition(_historyList, 0) != 0)
                    {
                        // ListBox 清空横向范围后不会自动归零，窗口放宽时必须主动收回旧偏移。
                        _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageHorizontalScroll, 6, 0);
                    }
                    break;
                }
                _ = NativeMethods.SendMessage(_historyList, NativeMethods.ListBoxSetHorizontalExtent, unchecked((nuint)extent), 0);
                if (extent == 0)
                {
                    _ = NativeMethods.SendMessage(_historyList, NativeMethods.WindowMessageHorizontalScroll, 6, 0);
                }
            }
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(_historyList, dc);
            _updatingHistoryExtent = false;
        }
    }

    internal static int CalculateHistoryRowExtent(int viewportWidth, int graphWidth, int authorWidth, int compactDateWidth)
    {
        int minimum = graphWidth + NativeTheme.Scale(120 + 8);
        if (authorWidth > 0) minimum += NativeTheme.Scale(8) + Math.Min(authorWidth, NativeTheme.Scale(48));
        if (compactDateWidth > 0) minimum += NativeTheme.Scale(8) + compactDateWidth;
        return Math.Max(viewportWidth, minimum);
    }

    internal static int CalculateFileHistoryRowExtent(int viewportWidth, int authorWidth, int dateWidth)
    {
        // 文件历史标题至少保留 120px；作者、日期列按实际度量但不低于视觉稿基线。
        int minimum = NativeTheme.Scale(20 + 120);
        minimum += Math.Max(0, authorWidth) + Math.Max(0, dateWidth);
        return Math.Max(viewportWidth, minimum);
    }

    private static void DrawFileHistoryRow(
        nint deviceContext,
        NativeMethods.Rectangle row,
        GitHistoryEntry entry,
        uint color,
        int authorWidth,
        int dateWidth)
    {
        // 文件历史沿用视觉稿的作者、日期、标题三列，不套用日志的提交图及右侧元数据布局。
        // 列宽由当前字体实际度量得到，各列共用边界，分数 DPI 下不会独立取整而相互覆盖。
        NativeMethods.Rectangle column = row;
        column.Left = NativeTheme.Scale(10);
        column.Right = column.Left + Math.Max(0, authorWidth);
        DrawText(deviceContext, entry.AuthorName, column, color, NativeTheme.UiFont, centered: false);
        column.Left = column.Right;
        column.Right = column.Left + Math.Max(0, dateWidth);
        DrawText(deviceContext, FormatHistoryDate(entry, compact: false), column, color, NativeTheme.UiFont, centered: false);
        column.Left = column.Right;
        column.Right = row.Right - NativeTheme.Scale(10);
        DrawText(deviceContext, entry.Subject, column, color, NativeTheme.UiFont, centered: false);
    }

    private static NativeHistoryRowTextLayout CalculateHistoryRowTextLayout(
        int rowWidth, int referenceWidth, int authorWidth, int fullDateWidth, int compactDateWidth)
    {
        int gap = NativeTheme.Scale(8);
        int available = Math.Max(0, rowWidth - NativeTheme.Scale(29 + 8));
        int metadataBudget = Math.Max(0, available - Math.Min(available, NativeTheme.Scale(120)) - gap);
        bool compact = fullDateWidth + Math.Min(authorWidth, NativeTheme.Scale(48)) + gap > metadataBudget;
        int date = Math.Min(metadataBudget, compact ? compactDateWidth : fullDateWidth);
        int author = Math.Min(authorWidth, Math.Max(0, metadataBudget - date - gap));
        int referenceBudget = Math.Max(0, metadataBudget - date - (author > 0 ? author + gap : 0) - gap);
        int reference = referenceBudget >= Math.Min(referenceWidth, NativeTheme.Scale(64))
            ? Math.Min(referenceWidth, referenceBudget) : 0;
        int columns = (date > 0 ? 1 : 0) + (author > 0 ? 1 : 0) + (reference > 0 ? 1 : 0);
        int metadata = date + author + reference + Math.Max(0, columns - 1) * gap;
        return new(Math.Max(0, available - metadata - (columns > 0 ? gap : 0)), reference, author, date, compact);
    }

    private bool DrawFileItem(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint panel = palette.Panel;
        uint text = palette.Text;
        uint muted = palette.Muted;
        uint selection = NativeTheme.SelectionColor(palette, NativeMethods.GetFocus() == _filesList);
        Fill(item.DeviceContext, item.ItemRectangle, (item.ItemState & NativeMethods.OwnerDrawSelected) != 0 ? selection : panel);
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= _fileRows.Count)
        {
            return true;
        }

        FileTreeRow row = _fileRows[index];
        if (row.Group)
        {
            int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
            int iconLeft = item.ItemRectangle.Left + NativeTheme.Scale(10 + row.Indent * 18);
            DrawChevron(
                item.DeviceContext,
                iconLeft,
                centerY,
                !_collapsedFileGroups.Contains(row.GroupKey),
                muted);
            _ = NativeTheme.DrawFolderIcon(item.DeviceContext, new()
            {
                Left = iconLeft + NativeTheme.Scale(16),
                Right = iconLeft + NativeTheme.Scale(32),
                Top = centerY - NativeTheme.Scale(8),
                Bottom = centerY + NativeTheme.Scale(8),
            }, dark, workspaceRoot: false,
                background: (item.ItemState & NativeMethods.OwnerDrawSelected) != 0 ? selection : panel);
            NativeMethods.Rectangle groupText = item.ItemRectangle;
            groupText.Left = iconLeft + NativeTheme.Scale(35);
            groupText.Right -= NativeTheme.Scale(6);
            DrawText(
                item.DeviceContext,
                row.Label,
                groupText,
                text,
                NativeTheme.UiMediumFont,
                centered: false);
            return true;
        }

        if (row.File is not GitCommitChangedFile file)
        {
            return true;
        }

        uint statusColor = NativeGitStatusPalette.Resolve(file.Kind, dark);
        NativeMethods.Rectangle icon = item.ItemRectangle;
        icon.Left += NativeTheme.Scale(7 + row.Indent * 18);
        icon.Right = icon.Left + NativeTheme.Scale(16);
        _ = NativeTheme.DrawFileTypeIcon(item.DeviceContext, icon, file.RelativePath, dark);
        NativeMethods.Rectangle path = item.ItemRectangle;
        path.Left += NativeTheme.Scale(31 + row.Indent * 18);
        path.Right -= NativeTheme.Scale(6);
        DrawText(item.DeviceContext, row.Label, path, statusColor, NativeTheme.UiFont, centered: false);
        return true;
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font,
        bool centered)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextSingleLine
            | NativeMethods.DrawTextVerticalCenter
            | NativeMethods.DrawTextNoPrefix
            | NativeMethods.DrawTextEndEllipsis;
        if (centered)
        {
            format |= NativeMethods.DrawTextCenter;
        }
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private static void DrawChevron(nint deviceContext, int left, int centerY, bool expanded, uint color)
    {
        _ = NativeTheme.DrawChevronIcon(deviceContext, new()
        {
            Left = left,
            Right = left + NativeTheme.Scale(8),
            Top = centerY - NativeTheme.Scale(4),
            Bottom = centerY + NativeTheme.Scale(4),
        }, expanded, color);
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

    private static void PaintRoundedInput(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint border,
        uint fill)
    {
        if (rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
        {
            return;
        }

        FillRounded(deviceContext, rectangle, border, NativeTheme.Scale(6));
        NativeMethods.Rectangle inner = rectangle;
        inner.Left++;
        inner.Top++;
        inner.Right--;
        inner.Bottom--;
        FillRounded(deviceContext, inner, fill, NativeTheme.Scale(5));
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }

    private void Layout()
    {
        _toolbarPopup?.Dispose();
        nint toolbarFocus = NativeMethods.GetFocus();
        if (Handle == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        if (_fileHistoryMode)
        {
            LayoutFileHistory(width, height);
            return;
        }

        foreach (nint control in new[]
        {
            _backButton,
            _createReferenceButton,
            _deleteReferenceButton,
            _branchesList,
            _branchFilterEdit,
            _filterEdit,
            _filterKindButton,
            _filterAuthorButton,
            _filterDateButton,
            _filterPathButton,
            _historyList,
            _filesList,
            _metadataLabel,
            _detailsTabLabel,
            _fileHistoryButton,
            _blameButton,
        })
        {
            _ = NativeMethods.ShowWindow(control, NativeMethods.ShowNormal);
        }
        _ = NativeMethods.ShowWindow(_fileHistoryTab, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_fileHistoryBranchLabel, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_fileHistoryClearButton, NativeMethods.ShowHide);
        _fileHistoryComparison?.SetVisible(false);

        int contentLeft = SideToolbarWidth;
        int contentWidth = Math.Max(0, width - contentLeft);
        (int branchWidth, int logWidth, int detailsWidth) = GetColumnWidths(contentWidth);
        if (!_showDetails)
        {
            logWidth += detailsWidth;
            detailsWidth = 0;
        }
        int detailsLeft = contentLeft + branchWidth + logWidth + 1;

        int navigationLeft = LayoutHistoryHeader(width) + NativeTheme.Scale(2);
        int headerControlTop = Math.Max(0, (HeaderHeight - NativeTheme.Scale(24)) / 2);
        Move(
            _previousButton,
            navigationLeft,
            headerControlTop,
            NativeTheme.Scale(28),
            NativeTheme.Scale(24));
        Move(
            _nextButton,
            navigationLeft + NativeTheme.Scale(31),
            headerControlTop,
            NativeTheme.Scale(28),
            NativeTheme.Scale(24));
        int cancelWidth = MeasureControlWidth(_cancelButton, 88, 12);
        int managementLeft = Math.Max(navigationLeft + NativeTheme.Scale(96), width - NativeTheme.Scale(80) - cancelWidth);
        Move(
            _moreActionsButton,
            Math.Max(0, width - NativeTheme.Scale(68)),
            headerControlTop,
            NativeTheme.Scale(28),
            NativeTheme.Scale(24));
        Move(
            _closePanelButton,
            Math.Max(0, width - NativeTheme.Scale(36)),
            headerControlTop,
            NativeTheme.Scale(28),
            NativeTheme.Scale(24));
        Move(
            _cancelButton,
            managementLeft,
            (HeaderHeight - HeaderTextHeight) / 2,
            cancelWidth,
            HeaderTextHeight);

        int sideButtonLeft = Math.Max(0, (SideToolbarWidth - NativeTheme.Scale(28)) / 2);
        var sideButtonTops = CalculateSideToolbarButtonTopsForTest();
        Move(_backButton, sideButtonLeft, sideButtonTops.Back, NativeTheme.Scale(28), NativeTheme.Scale(28));
        Move(
            _createReferenceButton,
            sideButtonLeft,
            sideButtonTops.CreateReference,
            NativeTheme.Scale(28),
            NativeTheme.Scale(28));
        Move(
            _deleteReferenceButton,
            sideButtonLeft,
            sideButtonTops.DeleteReference,
            NativeTheme.Scale(28),
            NativeTheme.Scale(28));
        Move(
            _refreshButton,
            sideButtonLeft,
            sideButtonTops.Refresh,
            NativeTheme.Scale(28),
            NativeTheme.Scale(28));
        Move(
            _searchButton,
            sideButtonLeft,
            sideButtonTops.Search,
            NativeTheme.Scale(28),
            NativeTheme.Scale(28));
        Move(
            _compareButton,
            sideButtonLeft,
            sideButtonTops.Compare,
            NativeTheme.Scale(28),
            NativeTheme.Scale(28));
        Move(
            _locateHeadButton,
            sideButtonLeft,
            sideButtonTops.LocateHead,
            NativeTheme.Scale(28),
            NativeTheme.Scale(28));

        var toolbarLayout = CalculateSideToolbarLayout(height);
        var toolbarButtons = GetSideToolbarButtons();
        for (int index = 0; index < toolbarButtons.Length; index++)
            _ = NativeMethods.ShowWindow(toolbarButtons[index].Control, index < toolbarLayout.VisibleCount ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        Move(_toolbarOverflowButton, sideButtonLeft, toolbarLayout.OverflowTop, NativeTheme.Scale(28), NativeTheme.Scale(28));
        _ = NativeMethods.ShowWindow(_toolbarOverflowButton, toolbarLayout.OverflowTop >= 0 ? NativeMethods.ShowNormal : NativeMethods.ShowHide);

        int filterControlHeight = NativeTheme.ContentHeight(25, 2);
        int filterControlTop = HeaderHeight + (int)Math.Round((FilterRowHeight - filterControlHeight) / 2d);
        _branchFilterEditFrame = new()
        {
            Left = contentLeft + NativeTheme.Scale(7),
            Top = filterControlTop,
            Right = Math.Max(
                contentLeft + NativeTheme.Scale(7),
                contentLeft + branchWidth - NativeTheme.Scale(7)),
            Bottom = filterControlTop + filterControlHeight,
        };
        Move(
            _branchFilterEdit,
            _branchFilterEditFrame.Left + NativeTheme.Scale(28),
            _branchFilterEditFrame.Top + NativeTheme.Scale(1),
            Math.Max(0, _branchFilterEditFrame.Right - _branchFilterEditFrame.Left - NativeTheme.Scale(34)),
            Math.Max(0, filterControlHeight - NativeTheme.Scale(2)));
        Move(
            _branchesList,
            contentLeft + NativeTheme.Scale(1),
            ContentTop,
            Math.Max(0, branchWidth - NativeTheme.Scale(1)),
            Math.Max(0, height - ContentTop - NativeTheme.Scale(1)));

        int filterLeft = contentLeft + branchWidth + NativeTheme.Scale(8);
        var filterButtons = GetFilterButtons();
        nint filterDc = NativeMethods.GetDeviceContext(Handle);
        try
        {
            int labelWidth = filterButtons.Max(button => MeasureTextWidth(filterDc, button.Label));
            _filterLayout = CalculateFilterControlLayout(logWidth, labelWidth);
        }
        finally { _ = NativeMethods.ReleaseDeviceContext(Handle, filterDc); }
        int filterWidth = _filterLayout.SearchWidth;
        _filterEditFrame = new()
        {
            Left = filterLeft,
            Top = filterControlTop,
            Right = filterLeft + filterWidth,
            Bottom = filterControlTop + filterControlHeight,
        };
        Move(
            _filterEdit,
            filterLeft + NativeTheme.Scale(28),
            filterControlTop + NativeTheme.Scale(1),
            Math.Max(0, filterWidth - NativeTheme.Scale(34)),
            Math.Max(0, filterControlHeight - NativeTheme.Scale(2)));
        int filterButtonLeft = filterLeft + filterWidth + NativeTheme.Scale(4);
        for (int index = 0; index < filterButtons.Length; index++)
        {
            nint control = filterButtons[index].Control;
            Move(control, filterButtonLeft + index * (_filterLayout.ButtonWidth + _filterLayout.Gap),
                filterControlTop, _filterLayout.ButtonWidth, filterControlHeight);
            _ = NativeMethods.ShowWindow(control, index < _filterLayout.VisibleFilterCount ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
        Move(_filterOverflowButton, contentLeft + branchWidth + _filterLayout.OverflowLeft,
            filterControlTop, NativeTheme.Scale(28), filterControlHeight);
        _ = NativeMethods.ShowWindow(_filterOverflowButton,
            _filterLayout.VisibleFilterCount < filterButtons.Length ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        int utilityLeft = contentLeft + branchWidth + _filterLayout.UtilitiesLeft;
        Move(_toggleDetailsButton, utilityLeft, filterControlTop, NativeTheme.Scale(28), filterControlHeight);
        Move(_filterSearchButton, utilityLeft + NativeTheme.Scale(32), filterControlTop, NativeTheme.Scale(28), filterControlHeight);
        _ = NativeMethods.ShowWindow(_toggleDetailsButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_filterSearchButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_applyFilterButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_clearFilterButton, NativeMethods.ShowHide);
        Move(
            _historyList,
            contentLeft + branchWidth + NativeTheme.Scale(1),
            ContentTop,
            Math.Max(0, logWidth - NativeTheme.Scale(1)),
            Math.Max(0, height - ContentTop - NativeTheme.Scale(1)));
        UpdateHistoryRowExtent();

        int detailsContentWidth = Math.Max(0, detailsWidth - NativeTheme.Scale(2));
        int detailsTabWidth = MeasureControlWidth(_detailsTabLabel, 84, 13);
        int historyActionWidth = MeasureControlWidth(_fileHistoryButton, 82, 16);
        int blameActionWidth = MeasureControlWidth(_blameButton, 66, 16);
        bool showDetailActions = _showDetails
            && detailsWidth >= Math.Max(NativeTheme.Scale(280),
                detailsTabWidth + historyActionWidth + blameActionWidth + NativeTheme.Scale(30))
            && height - ContentTop >= FileRowHeight + HeaderTextHeight + NativeTheme.UiLineHeight;
        nint detailsActionFocus = NativeMethods.GetFocus();
        _ = NativeMethods.ShowWindow(
            _filesList,
            _showDetails ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(
            _metadataLabel,
            _showDetails ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(
            _detailsTabLabel,
            showDetailActions ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(
            _fileHistoryButton,
            showDetailActions ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(
            _blameButton,
            showDetailActions ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        (int filesTop, int filesHeight, int fileButtonsTop, int metadataTop, int metadataHeight) =
            CalculateDetailsLayout(height, showDetailActions);
        int fileButtonsHeight = metadataTop - fileButtonsTop;
        if (!showDetailActions && _showDetails
            && (detailsActionFocus == _fileHistoryButton || detailsActionFocus == _blameButton))
            _ = NativeMethods.SetFocus(_filesList);
        Move(_filesList, detailsLeft + NativeTheme.Scale(1), filesTop, detailsContentWidth, filesHeight);
        UpdateFilesListScrollBar(filesHeight);
        Move(
            _detailsTabLabel,
            detailsLeft + NativeTheme.Scale(7),
            fileButtonsTop,
            detailsTabWidth,
            fileButtonsHeight);
        int detailsActionsRight = detailsLeft + detailsWidth - NativeTheme.Scale(7);
        Move(
            _fileHistoryButton,
            detailsActionsRight - blameActionWidth - NativeTheme.Scale(6) - historyActionWidth,
            fileButtonsTop,
            historyActionWidth,
            fileButtonsHeight);
        Move(
            _blameButton,
            detailsActionsRight - blameActionWidth,
            fileButtonsTop,
            blameActionWidth,
            fileButtonsHeight);
        Move(
            _metadataLabel,
            detailsLeft + NativeTheme.Scale(1),
            metadataTop,
            detailsContentWidth,
            metadataHeight);
        UpdateDetailsScrollRange();
        RestoreToolbarFocusAfterLayout(toolbarFocus);
    }

    private void RestoreToolbarFocusAfterLayout(nint previousFocus)
    {
        if (previousFocus == 0 || !NativeMethods.IsWindowVisible(Handle)
            || NativeMethods.IsWindowVisible(previousFocus)) return;
        nint[] side = GetSideToolbarButtons().Select(button => button.Control).ToArray();
        nint[] filters = GetFilterButtons().Select(button => button.Control).ToArray();
        nint overflow;
        nint[] group;
        if (previousFocus == _toolbarOverflowButton || side.Contains(previousFocus))
        {
            overflow = _toolbarOverflowButton;
            group = side;
        }
        else if (previousFocus == _filterOverflowButton || filters.Contains(previousFocus))
        {
            overflow = _filterOverflowButton;
            group = filters;
        }
        else return;

        bool Available(nint control) => NativeMethods.IsWindowVisible(control) && NativeMethods.IsWindowEnabled(control);
        // 缩小时交给收纳箭头，放大后交给同组最后一个可见动作，不遗留不可见焦点。
        nint target = Available(overflow) ? overflow : group.LastOrDefault(Available);
        if (target != 0) _ = NativeMethods.SetFocus(target);
    }

    private void LayoutFileHistory(int width, int height)
    {
        _ = NativeMethods.ShowWindow(_toolbarOverflowButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_filterOverflowButton, NativeMethods.ShowHide);
        EnsureFileHistoryComparison();
        int tabLeft = LayoutHistoryHeader(width);
        int headerControlTop = Math.Max(0, (HeaderHeight - HeaderTextHeight) / 2);
        string tab = FormatFileHistoryTab(_filter.FilePath);
        _ = NativeMethods.SetWindowText(_fileHistoryTab, tab);
        int tabWidth = Math.Clamp(
            Math.Min(NativeTheme.Scale(280), MeasureControlWidth(_fileHistoryTab, 0, 2 * HeaderTabTextInset)),
            0,
            Math.Max(0, width - tabLeft - NativeTheme.Scale(76)));
        Move(
            _fileHistoryTab,
            tabLeft,
            headerControlTop,
            tabWidth,
            HeaderTextHeight);
        _ = NativeMethods.ShowWindow(_pageLabel, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_fileHistoryTab, NativeMethods.ShowNormal);
        _ = NativeMethods.SetWindowText(_fileHistoryTab, tab);

        foreach (nint control in new[]
        {
            _backButton,
            _createReferenceButton,
            _deleteReferenceButton,
            _searchButton,
            _locateHeadButton,
            _previousButton,
            _nextButton,
            _filterKindButton,
            _filterAuthorButton,
            _filterDateButton,
            _filterPathButton,
            _filterEdit,
            _branchFilterEdit,
            _branchesList,
            _applyFilterButton,
            _clearFilterButton,
            _filesList,
            _metadataLabel,
            _detailsTabLabel,
            _fileHistoryButton,
            _blameButton,
        })
        {
            _ = NativeMethods.ShowWindow(control, NativeMethods.ShowHide);
        }

        int headerIconTop = (HeaderHeight - NativeTheme.Scale(24)) / 2;
        Move(_moreActionsButton, Math.Max(0, width - NativeTheme.Scale(68)), headerIconTop, NativeTheme.Scale(28), NativeTheme.Scale(24));
        Move(_closePanelButton, Math.Max(0, width - NativeTheme.Scale(36)), headerIconTop, NativeTheme.Scale(28), NativeTheme.Scale(24));
        _ = NativeMethods.ShowWindow(_moreActionsButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_closePanelButton, NativeMethods.ShowNormal);

        int contentTop = HeaderHeight;
        int toolbarTop = contentTop;
        int toolbarHeight = FileHistoryToolbarHeight;
        int detailWidth = _showDetails
            ? Math.Clamp(
                (int)Math.Round(width * 0.42d),
                Math.Min(NativeTheme.Scale(360), width),
                Math.Min(NativeTheme.Scale(520), width))
            : 0;
        int listWidth = Math.Max(0, width - detailWidth);
        int detailLeft = listWidth;
        int contentTopAfterToolbar = contentTop + toolbarHeight;
        int contentHeight = Math.Max(0, height - contentTopAfterToolbar);

        int branchLabelHeight = NativeTheme.ContentHeight(27, 0);
        int branchLabelWidth = Math.Min(MeasureControlWidth(_fileHistoryBranchLabel, 92, 8), Math.Max(0, listWidth - NativeTheme.Scale(188)));
        int fileActionLeft = NativeTheme.Scale(12) + branchLabelWidth;
        int fileActionTop = toolbarTop + (toolbarHeight - NativeTheme.Scale(28)) / 2;
        Move(_fileHistoryBranchLabel, NativeTheme.Scale(8), toolbarTop + (toolbarHeight - branchLabelHeight) / 2, branchLabelWidth, branchLabelHeight);
        Move(_fileHistoryClearButton, fileActionLeft, fileActionTop, NativeTheme.Scale(28), NativeTheme.Scale(28));
        Move(_refreshButton, fileActionLeft + NativeTheme.Scale(38), fileActionTop, NativeTheme.Scale(28), NativeTheme.Scale(28));
        Move(_compareButton, fileActionLeft + NativeTheme.Scale(72), fileActionTop, NativeTheme.Scale(28), NativeTheme.Scale(28));
        Move(_filterSearchButton, fileActionLeft + NativeTheme.Scale(106), fileActionTop, NativeTheme.Scale(28), NativeTheme.Scale(28));
        Move(_toggleDetailsButton, fileActionLeft + NativeTheme.Scale(140), fileActionTop, NativeTheme.Scale(28), NativeTheme.Scale(28));
        _ = NativeMethods.ShowWindow(_fileHistoryBranchLabel, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_fileHistoryClearButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_refreshButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_compareButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_filterSearchButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_toggleDetailsButton, NativeMethods.ShowNormal);

        Move(_historyList, 0, contentTopAfterToolbar, listWidth, contentHeight);
        UpdateHistoryRowExtent();
        _ = NativeMethods.ShowWindow(_historyList, NativeMethods.ShowNormal);


        if (_fileHistoryComparison is not null)
        {
            _fileHistoryComparison.SetBounds(
                detailLeft,
                contentTop,
                detailWidth,
                Math.Max(0, height - contentTop));
            _fileHistoryComparison.SetVisible(_showDetails && detailWidth > 0);
        }

        _ = NativeMethods.ShowWindow(_blameButton, NativeMethods.ShowHide);
        UpdateCancelButton();
    }

    private static (int FilesTop, int FilesHeight, int ActionsTop, int DetailsTop, int DetailsHeight)
        CalculateDetailsLayout(int height, bool showActions)
    {
        int detailsContentHeight = Math.Max(0, height - ContentTop);
        int filesHeight = Math.Max(
            Math.Min(HistoryRowHeight, detailsContentHeight),
            (int)Math.Round(detailsContentHeight * 0.56d));
        int actionsTop = ContentTop + filesHeight;
        int detailsTop = actionsTop + (showActions ? HeaderTextHeight : 0);
        int detailsHeight = Math.Max(0, height - detailsTop - NativeTheme.Scale(1));
        return (ContentTop, filesHeight, actionsTop, detailsTop, detailsHeight);
    }

    private static (int BranchWidth, int LogWidth, int DetailsWidth) GetColumnWidths(int width)
    {
        if (width < NativeTheme.Scale(800))
        {
            int safeWidth = Math.Max(0, width);
            int minimumLogWidth = Math.Min(NativeTheme.Scale(240), safeWidth);
            int availableSideWidth = Math.Max(0, safeWidth - minimumLogWidth);
            int compactBranchWidth = Math.Clamp(
                (int)Math.Round(safeWidth * 0.29d),
                Math.Min(NativeTheme.Scale(160), safeWidth),
                Math.Min(NativeTheme.Scale(180), safeWidth));
            int compactDetailsWidth = Math.Clamp(
                (int)Math.Round(safeWidth * 0.32d),
                Math.Min(NativeTheme.Scale(190), safeWidth),
                Math.Min(NativeTheme.Scale(210), safeWidth));
            int compactSideWidth = compactBranchWidth + compactDetailsWidth;
            if (compactSideWidth > availableSideWidth)
            {
                compactBranchWidth = compactSideWidth == 0
                    ? 0
                    : (int)Math.Round(availableSideWidth * compactBranchWidth / (double)compactSideWidth);
                compactDetailsWidth = availableSideWidth - compactBranchWidth;
            }

            int compactLogWidth = safeWidth - compactBranchWidth - compactDetailsWidth;
            return (compactBranchWidth, compactLogWidth, compactDetailsWidth);
        }

        int branchWidth = Math.Clamp(
            (int)Math.Round(width * 0.20),
            NativeTheme.Scale(230),
            NativeTheme.Scale(285));
        int detailsWidth = Math.Clamp(
            (int)Math.Round(width * 0.29),
            NativeTheme.Scale(280),
            NativeTheme.Scale(390));
        int logWidth = Math.Max(0, width - branchWidth - detailsWidth);
        return (branchWidth, logWidth, detailsWidth);
    }

    private static NativeHistoryFilterLayout CalculateFilterControlLayout(int logWidth, int labelWidth)
    {
        int inset = NativeTheme.Scale(8);
        int gap = NativeTheme.Scale(4);
        int buttonWidth = Math.Max(NativeTheme.Scale(48), (int)Math.Ceiling((labelWidth + NativeTheme.Scale(24)) / (double)gap) * gap);
        int utilityLeft = Math.Max(inset, logWidth - inset - NativeTheme.Scale(60));
        int available = Math.Max(0, utilityLeft - inset);
        int visibleCount = 4;
        int slotsWidth;
        // PyCharm 的窄栏保留搜索框与右侧工具，容不下的筛选动作进入右箭头菜单。
        while (true)
        {
            slotsWidth = visibleCount * (buttonWidth + gap)
                + (visibleCount < 4 ? NativeTheme.Scale(28) + gap : 0);
            if (visibleCount == 0 || available - slotsWidth - gap >= NativeTheme.Scale(110)) break;
            visibleCount--;
        }
        int searchWidth = Math.Clamp(available - slotsWidth - gap, 0, NativeTheme.Scale(230));
        int overflowLeft = inset + searchWidth + gap + visibleCount * (buttonWidth + gap);
        return new(searchWidth, buttonWidth, gap, visibleCount, overflowLeft, utilityLeft);
    }

    private int LayoutHistoryHeader(int width)
    {
        int titleWidth = MeasureControlWidth(_titleLabel, 0, 0);
        int tabWidth = MeasureControlWidth(_pageLabel, 0, 2 * HeaderTabTextInset);
        int top = (HeaderHeight - HeaderTextHeight) / 2;
        int tabLeft = NativeTheme.Scale(HeaderTitleInset) + titleWidth + NativeTheme.Scale(HeaderTitleTabGap);
        int availableTabWidth = Math.Min(tabWidth, Math.Max(0, width - tabLeft - NativeTheme.Scale(76)));
        Move(_titleLabel, NativeTheme.Scale(HeaderTitleInset), top, titleWidth, HeaderTextHeight);
        Move(_pageLabel, tabLeft, top, availableTabWidth, HeaderTextHeight);
        return tabLeft + availableTabWidth + NativeTheme.Scale(HeaderTabGap);
    }

    private int MeasureControlWidth(nint control, int minimum, int padding)
    {
        nint dc = NativeMethods.GetDeviceContext(Handle);
        nint previous = NativeMethods.SelectObject(dc, control == _titleLabel || control == _fileHistoryBranchLabel
            ? NativeTheme.UiMediumFont : NativeTheme.UiFont);
        try
        {
            string label = NativeMethods.GetWindowTextValue(control);
            NativeMethods.Rectangle bounds = default;
            _ = NativeMethods.DrawText(dc, label, label.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return Math.Max(NativeTheme.Scale(minimum), bounds.Right + NativeTheme.Scale(padding));
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(Handle, dc);
        }
    }

    private static int MeasureTextWidth(nint deviceContext, string value)
    {
        nint previous = NativeMethods.SelectObject(deviceContext, NativeTheme.UiFont);
        NativeMethods.Rectangle rectangle = new();
        _ = NativeMethods.DrawText(deviceContext, value, value.Length, ref rectangle,
            NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextCalculateRectangle);
        if (previous != 0) _ = NativeMethods.SelectObject(deviceContext, previous);
        return Math.Max(0, rectangle.Right - rectangle.Left);
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private void UpdateFilesListScrollBar(int? availableHeight = null)
    {
        if (_filesList == 0)
        {
            return;
        }

        int height = availableHeight ?? (NativeMethods.GetClientRectangle(
            _filesList,
            out NativeMethods.Rectangle rectangle)
                ? Math.Max(0, rectangle.Bottom - rectangle.Top)
                : 0);
        bool visible = ShouldShowFilesListScrollBar(_fileRows.Count, height);
        long style = NativeMethods.GetWindowLongPointer(_filesList, NativeMethods.WindowLongStyle).ToInt64();
        bool currentlyVisible = (style & NativeMethods.WindowStyleVerticalScroll) != 0;
        if (visible == currentlyVisible)
        {
            return;
        }

        long updatedStyle = visible
            ? style | NativeMethods.WindowStyleVerticalScroll
            : style & ~(long)NativeMethods.WindowStyleVerticalScroll;
        _ = NativeMethods.SetWindowLongPointer(
            _filesList,
            NativeMethods.WindowLongStyle,
            unchecked((nint)updatedStyle));
        _ = NativeMethods.SetWindowPosition(
            _filesList,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SetWindowPositionNoMove
                | NativeMethods.SetWindowPositionNoSize
                | NativeMethods.SetWindowPositionNoZOrder
                | NativeMethods.SetWindowPositionNoActivate
                | NativeMethods.SetWindowPositionFrameChanged);
    }

    private static bool ShouldShowFilesListScrollBar(int fileCount, int availableHeight)
    {
        return availableHeight > 0
            && (long)Math.Max(0, fileCount) * FileRowHeight > availableHeight;
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

    private static string? NullIfEmpty(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }

    private sealed class FileTreeNode(string name, string pathKey)
    {
        public string Name { get; } = name;

        public string PathKey { get; } = pathKey;

        public List<GitCommitChangedFile> Files { get; } = [];

        public Dictionary<string, FileTreeNode> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record FileTreeRow(
        string Label,
        GitCommitChangedFile? File,
        int Indent,
        bool Group,
        string GroupKey,
        int FileCount);

    private sealed record BranchRow(string Text, int Indent, bool Group, bool Accent);
}

internal readonly record struct NativeHistoryFilterLayout(
    int SearchWidth, int ButtonWidth, int Gap, int VisibleFilterCount, int OverflowLeft, int UtilitiesLeft);

internal readonly record struct NativeHistoryRowTextLayout(
    int SubjectWidth, int ReferenceWidth, int AuthorWidth, int DateWidth, bool CompactDate);
