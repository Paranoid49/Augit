using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed partial class NativeGitPanel : IDisposable
{
    private const string WindowClassName = "Augit.GitPanel.Native";
    private static int LeftHeaderHeight => NativeTheme.ContentHeight(39, 10);
    // 视觉稿中提交工具栏为 36 逻辑像素；列表从工具栏下方保留 2 像素内边距。
    private static int LeftToolsHeight => NativeTheme.ContentHeight(36, 8);
    private static int LeftToolsBottomGap => NativeTheme.Scale(2);
    private static int LeftContentTop => LeftHeaderHeight + LeftToolsHeight;
    private static int ChangesRowHeight => NativeTheme.ContentHeight(27, 6);
    private const int ChangeRowHorizontalInset = 7;
    private const int ChangeRowContentInset = 7;
    private const int ChangeRowSlotWidth = 16;
    private const int ChangeRowColumnGap = 7;
    private const int ChangeCheckboxSize = 15;
    private const int ChangeChevronGlyphWidth = 8;
    private static int MinimumChangesListHeight => NativeTheme.Scale(155);
    private static int MinimumCommitSectionHeight => NativeTheme.Scale(190);
    // 提交区占标题行之后可用高度的 42%，与视觉稿的 commit-box 网格行一致。
    private const double CommitSectionHeightRatio = 0.42d;
    private const int DiffLoadingThresholdMilliseconds = 150;
    private static int DiffToolbarHeight => Math.Max(NativeTheme.Scale(39), NativeTheme.UiLineHeight + NativeTheme.Scale(12));
    private int DiffFileBarHeight => NativeDiffFileHeader.Height(_diffHeaderSideBySide);
    private int DiffContentTop => DiffToolbarHeight + DiffFileBarHeight;
    private static int DiffGutterWidth => NativeTheme.Scale(84);
    private const int ChangesListIdentifier = 1;
    private const int CommandRefresh = 10;
    private const int CommandInitialize = 11;
    private const int CommandFetch = 12;
    private const int CommandPull = 13;
    private const int CommandPush = 14;
    private const int CommandRemotes = 15;
    private const int CommandCancelOperation = 16;
    private const int CommandUnified = 20;
    private const int CommandSideBySide = 21;
    private const int CommandIgnoreWhitespace = 22;
    private const int CommandAmend = 24;
    private const int CommandCommit = 25;
    private const int CommandCommitAndPush = 26;
    private const int CommandPreviousChange = 27;
    private const int CommandNextChange = 28;
    private const int CommandModifiedPreview = 29;
    private const int CommandRollback = 30;
    private const int CommandAdvancedOperations = 31;
    private const int CommandMoreActions = 32;
    private const int CommandShowDiff = 35;
    private const int CommandExpandAll = 36;
    private const int CommandPreview = 37;
    private const int CommandRollbackToolbar = 38;
    private const int CommandLastCommit = 39;
    private const int CommandCommitSettings = 40;
    private const int CommandPreviousFile = 43;
    private const int CommandNextFile = 44;
    private const int CommandDiffSearch = 45;
    private const int CommandDiffSettings = 46;
    private const int CommandContextShowDiff = 50;
    private const int CommandContextRollback = 51;
    private const int CommandContextFileHistory = 52;
    private const int CommandContextBlame = 53;
    private const int CommandContextCopyPath = 54;
    private const int CommandContextReveal = 55;
    private const int CommandClosePanel = 33;
    private const int CommandChoosePullMode = 34;
    private const int EmptyChangesTitleIdentifier = 64;
    private const int EmptyChangesSubtitleIdentifier = 65;
    private const int DiffFileSummaryIdentifier = 66;
    private const int DiffChangeSummaryIdentifier = 67;
    private const int DiffLoadingIdentifier = 68;
    private const int CommitChangeCountIdentifier = 69;
    private const int PullModeMenuCommandBase = 1100;
    private const nuint ChangesListSubclassIdentifier = 1;
    private const uint WindowMessageRefresh = NativeMethods.WindowMessageApp + 20;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeGitPanel> Instances = [];
    private static readonly Dictionary<nint, NativeGitPanel> ChangesListInstances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly NativeMethods.SubclassProcedure ChangesListProcedure = HandleChangesListMessage;
    private static readonly int[] DiffLoadingSkeletonLineFractions = [72, 46, 87, 61, 78, 39, 69, 54, 82, 48, 64, 43];
    private static readonly string[] PullModeLabels =
    [
        UiText.PullRepositoryConfigured,
        UiText.PullMerge,
        UiText.PullRebase,
        UiText.PullFastForwardOnly,
    ];
    private static bool _classRegistered;
    private readonly string _workspaceRoot;
    private ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly Action<string> _openModifiedPreview;
    private readonly Action<string> _openFileHistory;
    private readonly Func<string, Task> _openBlame;
    private readonly Action _openHistory;
    private readonly Action _openSettings;
    private readonly Action<bool, string?, bool> _setDiffVisible;
    private readonly Action _refreshChrome;
    private readonly Action<GitStatusSnapshot?> _statusUpdated;
    private readonly Action _closePanel;
    private readonly Action<string> _gitUnavailable;
    private readonly List<ChangeListEntry> _entries = [];
    private readonly HashSet<GitChangeGroup> _collapsedGroups = [];
    private readonly HashSet<string> _changedWorkspacePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly GitFileSelection _selection = new();
    private readonly List<int> _changeLines = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private NativeToolTip? _toolTip;
    private NativeToolTip? _diffToolTip;
    private NativeContextMenu? _contextMenu;
    private GitRuntimeInfo? _runtime;
    private GitRepositorySnapshot? _repository;
    private GitStatusSnapshot? _status;
    private GitRepositoryService? _repositoryService;
    private GitStatusService? _statusService;
    private IGitDiffService? _diffService;
    private IGitCommitService? _commitService;
    private GitRemoteService? _remoteService;
    private GitWorkspaceStateService? _workspaceStateService;
    private GitOperationService? _advancedOperationService;
    private GitConflictService? _conflictService;
    private GitMetadataWatcher? _metadataWatcher;
    private GitDiffDocument? _activeDiff;
    private GitChangedFile? _activeChangedFile;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _diffCancellation;
    private CancellationTokenSource? _diffLoadingIndicatorCancellation;
    private ScintillaControl? _unifiedDiff;
    private ScintillaControl? _oldDiff;
    private ScintillaControl? _diffGutter;
    private ScintillaControl? _newDiff;
    private nint _panelTitle;
    private nint _repositoryLabel;
    private nint _initializeButton;
    private nint _refreshButton;
    private nint _moreActionsButton;
    private nint _closePanelButton;
    private nint _fetchButton;
    private nint _pullButton;
    private nint _pullModeButton;
    private nint _pushButton;
    private nint _remotesButton;
    private nint _advancedOperationsButton;
    private nint _cancelButton;
    private nint _rollbackToolbarButton;
    private nint _showDiffToolbarButton;
    private nint _expandAllToolbarButton;
    private nint _previewToolbarButton;
    private nint _changesList;
    private nint _emptyChangesTitle;
    private nint _emptyChangesSubtitle;
    private nint _unifiedButton;
    private nint _sideBySideButton;
    private nint _ignoreWhitespaceButton;
    private nint _previousChangeButton;
    private nint _nextChangeButton;
    private nint _modifiedPreviewButton;
    private nint _rollbackButton;
    private nint _commitLabel;
    private nint _diffTitle;
    private nint _diffFileSummary;
    private nint _diffChangeSummary;
    private nint _diffLoadingNotice;
    private nint _previousFileButton;
    private nint _nextFileButton;
    private nint _diffSearchButton;
    private nint _diffSettingsButton;
    private nint _commitEdit;
    private nint _amendButton;
    private nint _lastCommitButton;
    private nint _commitChangeCount;
    private nint _commitSettingsButton;
    private nint _commitButton;
    private nint _commitAndPushButton;
    private nint _controlBrush;
    private nint _diffHandle;
    private NativeMethods.Rectangle _diffModeGroupBounds;
    private bool _diffHeaderSideBySide = true;
    private bool _sideBySide = true;
    private bool _diffVisible;
    private string? _publishedDiffPath;
    private bool _ignoreWhitespace;
    private bool _amend;
    private bool _amendLoading;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _refreshActiveDiffPending;
    private bool _updatingChangesList;
    private bool _operationRunning;
    private int _changesPopulateCount;
    private int _changesListResetCount;
    private int _changesListDeltaCount;
    private int _diffRequestCount;
    private int _diffPresentationNotificationCount;
    private int _layoutInvocationCount;
    private int _diffVersion;
    private int _renderVersion;
    private int _pullModeIndex;
    private int _amendRequestVersion;
    private string? _loadingDiffPath;
    private bool _loadingDiffIgnoreWhitespace;
    private bool _activeDiffIgnoreWhitespace;
    private bool _activeDiffSideBySide;
    private string? _loadingDiffFingerprint;
    private string? _activeDiffFingerprint;
    private int _changeNavigationIndex = -1;
    private NativeMethods.Rectangle _commitEditFrame;
    private string _unifiedRenderedText = string.Empty;
    private string _oldRenderedText = string.Empty;
    private string _diffGutterRenderedText = string.Empty;
    private int _diffGutterWidth = DiffGutterWidth;
    private string _newRenderedText = string.Empty;
    private bool _synchronizingDiffScroll;
    private bool _suppressDiffNotifications;
    private string? _selectedFilePathIntent;
    private string? _commitDraftBeforeAmend;
    private string _commitErrorText = string.Empty;
    private Task _lastCommitCommandForTest = Task.CompletedTask;
    private Task _lastCommitToolbarCommandForTest = Task.CompletedTask;
    private Task _lastAmendCommandForTest = Task.CompletedTask;
    private bool _skipRollbackConfirmationForTest;
    private bool _holdDiffLoadingForTest;
    private int _delayNextDiffForTestMilliseconds;
    private bool _hasBounds;
    private bool _settingBounds;
    private bool _diffShownOnce;
    private bool _hasLayoutMetrics;
    private int _layoutPanelWidth;
    private int _layoutPanelHeight;
    private int _layoutDiffWidth;
    private int _layoutDiffHeight;
    private int _layoutScale;
    private NativeMethods.Rectangle _panelBounds;
    private NativeMethods.Rectangle _diffBounds;
    private bool _disposed;

    internal NativeGitPanel(
        nint parent,
        string workspaceRoot,
        ApplicationSettings settings,
        Action<string> setStatus,
        Action<string> openModifiedPreview,
        Action<string> openFileHistory,
        Func<string, Task> openBlame,
        Action openHistory,
        Action openSettings,
        Action<bool, string?, bool> setDiffVisible,
        Action refreshChrome,
        Action<GitStatusSnapshot?> statusUpdated,
        Action closePanel,
        Action<string> gitUnavailable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(openModifiedPreview);
        ArgumentNullException.ThrowIfNull(openFileHistory);
        ArgumentNullException.ThrowIfNull(openBlame);
        ArgumentNullException.ThrowIfNull(openHistory);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(setDiffVisible);
        ArgumentNullException.ThrowIfNull(refreshChrome);
        ArgumentNullException.ThrowIfNull(statusUpdated);
        ArgumentNullException.ThrowIfNull(closePanel);
        ArgumentNullException.ThrowIfNull(gitUnavailable);
        _workspaceRoot = workspaceRoot;
        _settings = settings;
        _setStatus = setStatus;
        _openModifiedPreview = openModifiedPreview;
        _openFileHistory = openFileHistory;
        _openBlame = openBlame;
        _openHistory = openHistory;
        _openSettings = openSettings;
        _setDiffVisible = setDiffVisible;
        _refreshChrome = refreshChrome;
        _statusUpdated = statusUpdated;
        _closePanel = closePanel;
        _gitUnavailable = gitUnavailable;
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

        _diffHandle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStyleChild
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
        if (_diffHandle == 0)
        {
            lock (InstancesGate)
            {
                Instances.Remove(Handle);
            }
            _ = NativeMethods.DestroyWindow(Handle);
            Handle = 0;
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitPanelCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_diffHandle, this);
        }

        _suppressDiffNotifications = true;
        try
        {
            CreateControls();
            CreateToolTips();
            ApplyAppearance();
            Layout();
        }
        finally
        {
            _suppressDiffNotifications = false;
        }
        _ = InitializeAsync();
    }

    internal nint Handle { get; private set; }

    internal nint DiffHandleForTest => _diffHandle;

    internal nint DiffFileHeaderHandleForTest => _diffTitle;

    internal bool DiffFileHeaderSideBySideForTest => _diffHeaderSideBySide;

    internal NativeMethods.Rectangle DiffModeGroupBoundsForTest => _diffModeGroupBounds;

    internal nint[] DiffModeToolbarHandlesForTest => [_ignoreWhitespaceButton, _sideBySideButton, _unifiedButton, _diffSettingsButton];

    internal int ChangedFileCount => _status?.Files.Count ?? 0;

    internal bool CancelOperationForHost()
    {
        if (!_operationRunning || _operationCancellation is null)
        {
            return false;
        }

        _operationCancellation.Cancel();
        return true;
    }

    internal int SelectedFileCountForTest => _selection.SelectedPaths.Count;

    internal NativeCheckboxState GroupCheckStateForTest(GitChangeGroup group) =>
        ResolveGroupCheckState(_status?.Files ?? [], _selection, group);

    internal string? SelectedChangedFilePathForTest => GetSelectedChangedFilePath();

    internal int ChangeEntryIndexForTest(GitChangeGroup group, string? relativePath = null)
    {
        return _entries.FindIndex(entry => entry.Group == group
            && (relativePath is null
                ? entry.File is null
                : entry.File?.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true));
    }

    internal string ChangeListDisplayTextForTest(int index)
    {
        return GetListBoxItemText(index);
    }

    internal nint ChangesListHandleForTest => _changesList;

    internal nint CommitMessageHandleForTest => _commitEdit;

    internal nint CommitFeedbackHandleForTest => _commitLabel;

    internal string CommitErrorForTest => _commitErrorText;

    internal bool OperationRunningForTest => _operationRunning;

    internal bool CommitActionEnabledForTest => _commitButton != 0
        && NativeMethods.IsWindowEnabled(_commitButton);

    internal bool IsRuntimeAvailable => _runtime?.IsAvailable == true;

    internal GitRepositoryKind? RepositoryKind => _repository?.Kind;

    internal bool RollbackButtonCreatedForTest => _rollbackButton != 0;

    internal bool AdvancedOperationsButtonCreatedForTest => _advancedOperationsButton != 0;

    internal bool CommitPanelTitleCreatedForTest => _panelTitle != 0
        && NativeMethods.GetWindowTextValue(_panelTitle) == UiText.CommitPanel;

    internal bool HeaderActionsCreatedForTest => _moreActionsButton != 0
        && _closePanelButton != 0
        && NativeMethods.IsWindowVisible(_moreActionsButton)
        && NativeMethods.IsWindowVisible(_closePanelButton);

    internal bool CommitToolbarActionsCreatedForTest => new[]
    {
        _refreshButton,
        _rollbackToolbarButton,
        _showDiffToolbarButton,
        _expandAllToolbarButton,
        _previewToolbarButton,
    }.All(control => control != 0 && NativeMethods.IsWindowVisible(control));

    internal bool CommitToolbarToolTipsCreatedForTest => _toolTip is not null
        && new[]
        {
            _refreshButton,
            _rollbackToolbarButton,
            _showDiffToolbarButton,
            _expandAllToolbarButton,
            _previewToolbarButton,
        }.All(_toolTip.ContainsForTest);

    internal (bool Refresh, bool Rollback, bool ShowDiff, bool ExpandAll, bool Preview)
        CommitToolbarEnabledStateForTest =>
        (
            NativeMethods.IsWindowEnabled(_refreshButton),
            NativeMethods.IsWindowEnabled(_rollbackToolbarButton),
            NativeMethods.IsWindowEnabled(_showDiffToolbarButton),
            NativeMethods.IsWindowEnabled(_expandAllToolbarButton),
            NativeMethods.IsWindowEnabled(_previewToolbarButton)
        );

    internal bool CommitActionsVisibleForTest => NativeMethods.GetWindowRectangle(
            Handle,
            out NativeMethods.Rectangle panel)
        && NativeMethods.GetWindowRectangle(_commitButton, out NativeMethods.Rectangle commit)
        && NativeMethods.GetWindowRectangle(_commitAndPushButton, out NativeMethods.Rectangle commitAndPush)
        && commit.Bottom <= panel.Bottom
        && commitAndPush.Bottom <= panel.Bottom;

    internal int CommitAndPushButtonWidthForTest => NativeMethods.GetWindowRectangle(
            _commitAndPushButton,
            out NativeMethods.Rectangle rectangle)
        ? rectangle.Right - rectangle.Left
        : 0;

    internal (int CommitWidth, int CommitPushWidth, int CommitHeight, int CommitPushHeight)
        CommitActionGeometryForTest
    {
        get
        {
            bool commitAvailable = NativeMethods.GetWindowRectangle(
                _commitButton,
                out NativeMethods.Rectangle commit);
            bool commitPushAvailable = NativeMethods.GetWindowRectangle(
                _commitAndPushButton,
                out NativeMethods.Rectangle commitPush);
            return (
                commitAvailable ? commit.Right - commit.Left : 0,
                commitPushAvailable ? commitPush.Right - commitPush.Left : 0,
                commitAvailable ? commit.Bottom - commit.Top : 0,
                commitPushAvailable ? commitPush.Bottom - commitPush.Top : 0);
        }
    }

    internal static (int CommitWidth, int CommitPushWidth, int CommitHeight)
        CommitActionDimensionsForTest =>
        (NativeTheme.Scale(53), NativeTheme.Scale(102), NativeTheme.Scale(30));

    internal bool RemoteActionsUseOverflowForTest => new[]
    {
        _fetchButton,
        _pullButton,
        _pullModeButton,
        _pushButton,
        _remotesButton,
        _advancedOperationsButton,
    }.All(control => control != 0 && !NativeMethods.IsWindowVisible(control));

    internal bool DiffToolbarActionsCreatedForTest => new[]
    {
        _previousFileButton,
        _nextFileButton,
        _previousChangeButton,
        _nextChangeButton,
        _diffSearchButton,
        _unifiedButton,
        _sideBySideButton,
        _ignoreWhitespaceButton,
        _diffSettingsButton,
    }.All(control => control != 0);

    internal bool DiffSettingsButtonUsesIconForTest => _diffSettingsButton != 0
        && NativeMethods.GetWindowTextValue(_diffSettingsButton).Length == 0
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
                _diffSettingsButton,
                NativeMethods.WindowLongStyle).ToInt64())
            & NativeMethods.ButtonOwnerDraw) == NativeMethods.ButtonOwnerDraw;

    internal bool DiffSurfaceOrderForTest => NativeMethods.GetWindowRectangle(
            _previousFileButton,
            out NativeMethods.Rectangle toolbar)
        && NativeMethods.GetWindowRectangle(_diffTitle, out NativeMethods.Rectangle fileBar)
        && NativeMethods.GetWindowRectangle(_unifiedDiff?.Handle ?? 0, out NativeMethods.Rectangle content)
        && toolbar.Top < fileBar.Top
        && fileBar.Top < content.Top;

    internal bool PullModeUsesOwnerDrawForTest => _pullModeButton != 0
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
                _pullModeButton,
                NativeMethods.WindowLongStyle).ToInt64())
            & NativeMethods.ButtonOwnerDraw) == NativeMethods.ButtonOwnerDraw;

    internal bool ChangesListUsesOwnerDrawForTest => _changesList != 0
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
                _changesList,
                NativeMethods.WindowLongStyle).ToInt64())
            & NativeMethods.ListBoxOwnerDrawFixed) == NativeMethods.ListBoxOwnerDrawFixed;

    internal bool ChangesListUsesIdeaInputForTest
    {
        get
        {
            lock (InstancesGate)
            {
                return _changesList != 0
                    && ChangesListInstances.TryGetValue(_changesList, out NativeGitPanel? panel)
                    && ReferenceEquals(panel, this);
            }
        }
    }

    internal static IReadOnlyList<string> ChangesContextMenuLabelsForTest =>
    [
        UiText.ShowDiff,
        UiText.Rollback + "…",
        UiText.FileHistory,
        UiText.Blame,
        UiText.CopyPath,
        UiText.RevealInExplorer,
    ];

    internal int VisibleChangeEntryCountForTest => _entries.Count;

    internal NativeContextMenu? ContextMenuForTest => _contextMenu;

    internal bool CommitMessageHidesPermanentScrollBarForTest => _commitEdit != 0
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
                _commitEdit,
                NativeMethods.WindowLongStyle).ToInt64())
            & NativeMethods.WindowStyleVerticalScroll) == 0;

    internal bool DiffUsesSingleMarginForTest => _unifiedDiff?.MarginCount == 1
        && _oldDiff?.MarginCount == 1
        && _diffGutter?.MarginCount == 1
        && _newDiff?.MarginCount == 1;

    internal bool DiffUsesSideBySideForTest => _sideBySide
        && _oldDiff is not null
        && _diffGutter is not null
        && _newDiff is not null
        && NativeMethods.IsWindowVisible(_oldDiff.Handle)
        && NativeMethods.IsWindowVisible(_diffGutter.Handle)
        && NativeMethods.IsWindowVisible(_newDiff.Handle)
        && (_unifiedDiff is null || !NativeMethods.IsWindowVisible(_unifiedDiff.Handle));

    internal bool DiffUsesCentralGutterForTest => _diffGutter is not null
        && _diffGutter.MarginCount == 1
        && _diffGutterRenderedText.Length > 0;

    internal static int DiffGutterWidthForTest => DiffGutterWidth;

    internal nint[] DiffTextHandlesForTest => [_unifiedDiff!.Handle, _oldDiff!.Handle, _diffGutter!.Handle, _newDiff!.Handle];

    internal string DiffGutterTextForTest => _diffGutterRenderedText;

    internal bool RefreshingForTest => _refreshing;

    internal bool DiffLoadingForTest => _loadingDiffPath is not null
        || _diffLoadingIndicatorCancellation is not null;

    internal bool DiffLoadingNoticeVisibleForTest => _diffLoadingNotice != 0
        && NativeMethods.IsWindowVisible(_diffLoadingNotice);

    internal string DiffLoadingNoticeTextForTest => _diffLoadingNotice == 0
        ? string.Empty
        : NativeMethods.GetWindowTextValue(_diffLoadingNotice);

    internal string DiffChangeSummaryForTest => _diffChangeSummary == 0
        ? string.Empty
        : NativeMethods.GetWindowTextValue(_diffChangeSummary);

    internal string DiffTitleForTest => _diffTitle == 0
        ? string.Empty
        : NativeMethods.GetWindowTextValue(_diffTitle);

    internal string DiffTextForTest => (_sideBySide ? _newDiff : _unifiedDiff)?.GetTextContent() ?? string.Empty;

    internal nint DiffEditorHandleForTest => (_sideBySide ? _newDiff : _unifiedDiff)?.Handle ?? 0;

    internal int CachedDiffPatchLengthForTest => _activeDiff?.UnifiedPatch?.Length ?? 0;

    internal Task? DiffRenderBarrierForTest { get; set; }

    internal void ClickDiffModeForTest(bool sideBySide)
    {
        _ = NativeMethods.SendMessage(
            _diffHandle,
            NativeMethods.WindowMessageCommand,
            unchecked((nuint)(sideBySide ? CommandSideBySide : CommandUnified)),
            sideBySide ? _sideBySideButton : _unifiedButton);
    }

    internal void ClickDiffChangeForTest(int direction)
    {
        _ = NativeMethods.SendMessage(
            _diffHandle,
            NativeMethods.WindowMessageCommand,
            unchecked((nuint)(direction < 0 ? CommandPreviousChange : CommandNextChange)),
            direction < 0 ? _previousChangeButton : _nextChangeButton);
    }

    internal void SetDiffServiceForTest(IGitDiffService service)
    {
        _diffService = service;
    }

    internal void ClickDiffFileForTest(int direction)
    {
        _ = NativeMethods.SendMessage(
            _diffHandle,
            NativeMethods.WindowMessageCommand,
            unchecked((nuint)(direction < 0 ? CommandPreviousFile : CommandNextFile)),
            direction < 0 ? _previousFileButton : _nextFileButton);
    }

    internal int ChangesPopulateCountForTest => _changesPopulateCount;

    internal int ChangesListResetCountForTest => _changesListResetCount;

    internal int ChangesListDeltaCountForTest => _changesListDeltaCount;

    internal static int ChangesRowHeightForTest => ChangesRowHeight;

    internal static NativeCommitLayoutMetrics CommitLayoutMetricsForTest => new(
        LogicalPixels(LeftHeaderHeight),
        LogicalPixels(LeftToolsHeight),
        LogicalPixels(LeftToolsBottomGap),
        LogicalPixels(MinimumChangesListHeight),
        LogicalPixels(MinimumCommitSectionHeight),
        CommitSectionHeightRatio);

    internal static NativeChangeListRowLayout ChangeListRowLayoutForTest(bool isGroup)
    {
        return CalculateChangeListRowLayout(0, isGroup);
    }

    internal static NativeChangeListTextColumns ChangeListTextColumnsForTest(
        int logicalAvailableWidth,
        int logicalFileNameWidth,
        int logicalDirectoryWidth)
    {
        NativeChangeListTextColumns columns = CalculateChangeListTextColumns(
            NativeTheme.Scale(logicalAvailableWidth),
            NativeTheme.Scale(logicalFileNameWidth),
            NativeTheme.Scale(logicalDirectoryWidth));
        return new(
            LogicalPixels(columns.FileNameWidth),
            LogicalPixels(columns.Gap),
            LogicalPixels(columns.DirectoryWidth));
    }

    internal static int CalculateCommitTopForTest(int logicalHeight)
    {
        return (int)Math.Round(NativeTheme.Unscale(CalculateCommitTop(NativeTheme.Scale(logicalHeight))));
    }

    private static int LogicalPixels(int value)
    {
        return (int)Math.Round(NativeTheme.Unscale(value));
    }

    internal int DiffRequestCountForTest => _diffRequestCount;

    internal int DiffPresentationNotificationCountForTest => _diffPresentationNotificationCount;


    internal int LayoutInvocationCountForTest => _layoutInvocationCount;

    internal bool ChangesListHasFocusForTest => NativeMethods.GetFocus() == _changesList;

    internal bool AmendActionHasFocusForTest => NativeMethods.GetFocus() == _amendButton;

    internal bool CommitMessageHasFocusForTest => NativeMethods.GetFocus() == _commitEdit;

    internal bool AmendActionEnabledForTest => _amendButton != 0
        && NativeMethods.IsWindowEnabled(_amendButton);

    internal bool AmendSelectedForTest => _amend;

    internal bool LastCommitActionVisibleForTest => _lastCommitButton != 0
        && NativeMethods.IsWindowVisible(_lastCommitButton)
        && NativeMethods.IsWindowEnabled(_lastCommitButton);

    internal bool CommitSettingsActionCreatedForTest => _commitSettingsButton != 0
        && NativeMethods.IsWindowVisible(_commitSettingsButton)
        && _toolTip?.ContainsForTest(_commitSettingsButton) == true;

    internal bool CommitSupplementalControlsWithinBoundsForTest
    {
        get
        {
            if (!NativeMethods.GetWindowRectangle(Handle, out NativeMethods.Rectangle panel))
            {
                return false;
            }

            foreach (nint control in new[]
            {
                _amendButton,
                _lastCommitButton,
                _commitChangeCount,
                _commitSettingsButton,
            })
            {
                if (!NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle bounds)
                    || bounds.Left < panel.Left
                    || bounds.Top < panel.Top
                    || bounds.Right > panel.Right
                    || bounds.Bottom > panel.Bottom)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal string CommitMessageForTest => _commitEdit == 0
        ? string.Empty
        : NativeMethods.GetWindowTextValue(_commitEdit);

    internal string CommitChangeCountForTest => _commitChangeCount == 0
        ? string.Empty
        : NativeMethods.GetWindowTextValue(_commitChangeCount);

    internal void SetCommitMessageForTest(string message)
    {
        _ = NativeMethods.SetWindowText(_commitEdit, message ?? string.Empty);
    }

    internal void SetCommitServiceForTest(IGitCommitService service) => _commitService = service;

    internal Task ClickCommitForTestAsync(bool pushAfterCommit = false)
    {
        _lastCommitCommandForTest = Task.CompletedTask;
        nint button = pushAfterCommit ? _commitAndPushButton : _commitButton;
        _ = NativeMethods.SendMessage(button, ButtonPerformClick, 0, 0);
        return _lastCommitCommandForTest;
    }

    internal async Task<bool> ToggleAmendForTestAsync()
    {
        if (_amendButton == 0 || !NativeMethods.IsWindowEnabled(_amendButton))
        {
            return false;
        }

        _lastAmendCommandForTest = Task.CompletedTask;
        _ = NativeMethods.SendMessage(
            Handle,
            NativeMethods.WindowMessageCommand,
            unchecked((nuint)CommandAmend),
            _amendButton);
        await _lastAmendCommandForTest;
        return true;
    }

    internal static (uint Background, uint Text) CommitMessagePlaceholderColorsForTest(bool dark)
    {
        return CommitMessagePlaceholderColors(dark);
    }

    internal static bool IsControlColorMessageForTest(uint message)
    {
        return message is NativeMethods.WindowMessageControlColorEdit
            or NativeMethods.WindowMessageControlColorListBox
            or NativeMethods.WindowMessageControlColorButton
            or NativeMethods.WindowMessageControlColorStatic;
    }

    internal async Task<bool> SelectFileForTestAsync(string relativePath)
    {
        int index = _entries.FindIndex(
            entry => entry.File?.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true);
        if (index < 0)
        {
            return false;
        }

        _selectedFilePathIntent = relativePath;
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        await ShowSelectedDiffAsync(activatePresentation: true);
        return _activeChangedFile?.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true;
    }

    internal Task<bool> ShowFileDiffForHostAsync(string relativePath)
    {
        return SelectFileForTestAsync(relativePath);
    }

    internal bool SelectFileRowForTest(string relativePath)
    {
        int index = FindFileEntryIndex(relativePath);
        if (index < 0)
        {
            return false;
        }

        _selectedFilePathIntent = relativePath;
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        UpdateCommitToolbarActionsEnabled();
        return true;
    }

    internal async Task<bool> ClickFileForTestAsync(string relativePath)
    {
        int index = FindFileEntryIndex(relativePath);
        if (index < 0 || !TryGetChangesListItemRectangle(index, out NativeMethods.Rectangle rectangle))
        {
            return false;
        }

        NativeChangeListRowLayout layout = CalculateChangeListRowLayout(rectangle.Left, isGroup: false);
        nint point = PackPoint(
            layout.TextLeft + NativeTheme.Scale(12),
            (rectangle.Top + rectangle.Bottom) / 2);
        if (!NativeMethods.PostMessage(_changesList, NativeMethods.WindowMessageLeftButtonDown, 1, point)
            || !NativeMethods.PostMessage(_changesList, NativeMethods.WindowMessageLeftButtonUp, 0, point))
        {
            return false;
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (GetSelectedEntryIndex() == index
                && GetSelectedChangedFilePath()?.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

    internal void DelayNextDiffForTest(int milliseconds)
    {
        _delayNextDiffForTestMilliseconds = Math.Max(0, milliseconds);
    }

    internal bool ShowDiffLoadingForHost(string relativePath)
    {
        int index = FindFileEntryIndex(relativePath);
        if (index < 0)
        {
            return false;
        }

        _holdDiffLoadingForTest = true;
        _selectedFilePathIntent = relativePath;
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        // 视觉审计必须越过已有 Diff 缓存，稳定停留在真实的局部加载状态。
        _ = ShowSelectedDiffAsync(forceReload: true, activatePresentation: true);
        return true;
    }

    internal async Task<bool> InvokeCommitToolbarActionForTestAsync(NativeCommitToolbarAction action)
    {
        (int command, nint control) = action switch
        {
            NativeCommitToolbarAction.Refresh => (CommandRefresh, _refreshButton),
            NativeCommitToolbarAction.Rollback => (CommandRollbackToolbar, _rollbackToolbarButton),
            NativeCommitToolbarAction.ShowDiff => (CommandShowDiff, _showDiffToolbarButton),
            NativeCommitToolbarAction.ExpandAll => (CommandExpandAll, _expandAllToolbarButton),
            NativeCommitToolbarAction.Preview => (CommandPreview, _previewToolbarButton),
            _ => (0, 0),
        };
        if (command == 0 || control == 0 || !NativeMethods.IsWindowEnabled(control))
        {
            return false;
        }

        _lastCommitToolbarCommandForTest = Task.CompletedTask;
        _skipRollbackConfirmationForTest = action == NativeCommitToolbarAction.Rollback;
        try
        {
            _ = NativeMethods.SendMessage(
                Handle,
                NativeMethods.WindowMessageCommand,
                unchecked((nuint)command),
                control);
            await _lastCommitToolbarCommandForTest;
            return true;
        }
        finally
        {
            _skipRollbackConfirmationForTest = false;
        }
    }

    internal Task RunPullForHostAsync()
    {
        return RunRemoteOperationAsync(
            service => service.PullAsync(_repository!, GetPullMode(), CurrentOperationToken),
            UiText.PullCompleted);
    }

    internal Task RunPushForHostAsync(string? localReference = null)
    {
        ShowPush(localReference);
        return Task.CompletedTask;
    }

    internal void ShowEmptyStateForHost()
    {
        if (_repository is null || _repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return;
        }

        _status = new GitStatusSnapshot(
            _status?.CurrentBranch,
            _status?.IsDetached ?? false,
            [],
            _status?.HeadCommit);
        _selection.Reconcile([]);
        PopulateChanges([]);
        _activeChangedFile = null;
        _activeDiff = null;
        _activeDiffFingerprint = null;
        _selectedFilePathIntent = null;
        _diffVisible = false;
        _ = NativeMethods.ShowWindow(_diffHandle, NativeMethods.ShowHide);
        ShowDiffNotice(UiText.NoGitChanges);
        UpdateEmptyChangesState();
        UpdateCommitActionsEnabled();
        UpdateCommitToolbarActionsEnabled();
    }

    internal void ShowPlainDirectoryStateForHost()
    {
        _repository = GitRepositorySnapshot.PlainDirectory(_workspaceRoot);
        _status = null;
        _selection.Reconcile([]);
        PopulateChanges([]);
        _activeChangedFile = null;
        _activeDiff = null;
        _activeDiffFingerprint = null;
        _selectedFilePathIntent = null;
        _diffVisible = false;
        _ = NativeMethods.ShowWindow(_diffHandle, NativeMethods.ShowHide);
        _ = NativeMethods.SetWindowText(_repositoryLabel, UiText.PlainDirectoryGitStatus);
        _ = NativeMethods.ShowWindow(_initializeButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_refreshButton, NativeMethods.ShowHide);
        SetGitControlsEnabled(false, allowInitialize: true);
        ShowDiffNotice(UiText.PlainDirectoryGitNotice);
    }

    internal bool ShowInitializeConfirmationForHost()
    {
        return ConfirmRepositoryInitialization();
    }

    internal bool ShowChangesContextMenuForHost(string relativePath)
    {
        int index = FindFileEntryIndex(relativePath);
        if (index < 0 || !TryGetChangesListItemRectangle(index, out NativeMethods.Rectangle rectangle))
        {
            return false;
        }

        NativeMethods.Point point = new()
        {
            X = rectangle.Left + NativeTheme.Scale(32),
            Y = (rectangle.Top + rectangle.Bottom) / 2,
        };
        if (!NativeMethods.ClientToScreen(_changesList, ref point))
        {
            return false;
        }

        ShowChangesContextMenu(index, point.X, point.Y);
        return true;
    }

    internal void ShowRemotesForHost()
    {
        ShowRemotes();
    }

    internal void ShowCreateStashForHost()
    {
        if (_repository is null || _workspaceStateService is null)
        {
            return;
        }

        _ = NativeStashDialog.Show(
            NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot),
            _repository,
            _workspaceStateService,
            _settings,
            _setStatus,
            _status?.CurrentBranch);
        RequestRefresh();
    }

    internal void ShowStashManagerForHost()
    {
        if (_repository is null || _workspaceStateService is null)
        {
            return;
        }

        NativeStashManagerDialog.Show(
            NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot),
            _repository,
            _workspaceStateService,
            _settings,
            _setStatus,
            _status?.CurrentBranch);
        RequestRefresh();
    }

    internal void ShowResetForHost(string initialTarget = "HEAD")
    {
        if (_repository is null || _workspaceStateService is null)
        {
            return;
        }

        _ = NativeResetDialog.Show(
            NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot),
            _repository,
            _workspaceStateService,
            _settings,
            _setStatus,
            initialTarget,
            _status?.Changes.Count);
        RequestRefresh();
    }

    internal void ShowAdvancedOperationsForHost()
    {
        ShowAdvancedOperations();
    }

    internal bool ClickFileCheckboxForTest(string relativePath)
    {
        int index = FindFileEntryIndex(relativePath);
        if (index < 0 || !TryGetChangesListItemRectangle(index, out NativeMethods.Rectangle rectangle))
        {
            return false;
        }

        bool before = _selection.IsSelected(relativePath);
        NativeChangeListRowLayout layout = CalculateChangeListRowLayout(rectangle.Left, isGroup: false);
        int x = layout.CheckboxLeft + layout.CheckboxSize / 2;
        int y = (rectangle.Top + rectangle.Bottom) / 2;
        nint point = PackPoint(x, y);
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageLeftButtonDown, 1, point);
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageLeftButtonUp, 0, point);
        return _selection.IsSelected(relativePath) != before;
    }

    internal bool ToggleFileWithSpaceForTest(string relativePath)
    {
        int index = FindFileEntryIndex(relativePath);
        if (index < 0)
        {
            return false;
        }

        bool before = _selection.IsSelected(relativePath);
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.WindowMessageKeyDown,
            NativeMethods.VirtualKeySpace,
            0);
        return _selection.IsSelected(relativePath) != before;
    }

    internal bool DoubleClickFileForTest(string relativePath)
    {
        int index = FindFileEntryIndex(relativePath);
        if (index < 0 || !TryGetChangesListItemRectangle(index, out NativeMethods.Rectangle rectangle))
        {
            return false;
        }

        NativeChangeListRowLayout layout = CalculateChangeListRowLayout(rectangle.Left, isGroup: false);
        int x = layout.TextLeft + NativeTheme.Scale(12);
        int y = (rectangle.Top + rectangle.Bottom) / 2;
        nint point = PackPoint(x, y);
        // 按真实 ListBox 消息顺序发送一次完整双击，覆盖原生选择通知和双击通知。
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageLeftButtonDown, 1, point);
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageLeftButtonUp, 0, point);
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageLeftButtonDoubleClick, 1, point);
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageLeftButtonUp, 0, point);
        return true;
    }

    internal async Task<bool> EnterFileForTestAsync(string relativePath)
    {
        int index = FindFileEntryIndex(relativePath);
        if (index < 0)
        {
            return false;
        }

        _selectedFilePathIntent = relativePath;
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.WindowMessageKeyDown,
            NativeMethods.VirtualKeyEnter,
            0);

        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (_diffVisible
                && _activeChangedFile?.RelativePath.Equals(
                    relativePath,
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

    internal bool ToggleGroupCollapsedForTest(GitChangeGroup group)
    {
        int index = _entries.FindIndex(entry => entry.File is null && entry.Group == group);
        if (index < 0)
        {
            return false;
        }

        bool before = _collapsedGroups.Contains(group);
        ToggleGroupCollapsed(group);
        return _collapsedGroups.Contains(group) != before;
    }

    internal bool ClickGroupChevronForTest(GitChangeGroup group)
    {
        NativeChangeListRowLayout layout = CalculateChangeListRowLayout(0, isGroup: true);
        int chevronCenter = layout.ChevronLeft + NativeTheme.Scale(ChangeChevronGlyphWidth / 2);
        bool result = SendGroupPointerInputForTest(group, NativeMethods.WindowMessageLeftButtonDown, chevronCenter);
        return result;
    }

    internal bool DoubleClickGroupTitleForTest(GitChangeGroup group)
    {
        NativeChangeListRowLayout layout = CalculateChangeListRowLayout(0, isGroup: true);
        return SendGroupPointerInputForTest(
            group,
            NativeMethods.WindowMessageLeftButtonDoubleClick,
            layout.TextLeft + NativeTheme.Scale(12));
    }

    internal bool ToggleGroupWithEnterForTest(GitChangeGroup group)
    {
        int index = _entries.FindIndex(entry => entry.File is null && entry.Group == group);
        if (index < 0)
        {
            return false;
        }

        bool before = _collapsedGroups.Contains(group);
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.WindowMessageKeyDown,
            NativeMethods.VirtualKeyEnter,
            0);
        return _collapsedGroups.Contains(group) != before;
    }

    private bool SendGroupPointerInputForTest(GitChangeGroup group, uint message, int xOffset)
    {
        int index = _entries.FindIndex(entry => entry.File is null && entry.Group == group);
        if (index < 0 || !TryGetChangesListItemRectangle(index, out NativeMethods.Rectangle rectangle))
        {
            return false;
        }

        bool before = _collapsedGroups.Contains(group);
        nint point = PackPoint(rectangle.Left + xOffset, (rectangle.Top + rectangle.Bottom) / 2);
        _ = NativeMethods.SendMessage(_changesList, message, 1, point);
        return _collapsedGroups.Contains(group) != before;
    }

    internal Task RollbackSelectedForTestAsync()
    {
        return RollbackSelectedAsync(requireConfirmation: false);
    }

    internal Task ShowRollbackConfirmationForHostAsync()
    {
        return RollbackSelectedAsync(requireConfirmation: true, executeAfterConfirmation: false);
    }

    internal void SetBounds(
        int panelX,
        int panelY,
        int panelWidth,
        int panelHeight,
        int diffX,
        int diffY,
        int diffWidth,
        int diffHeight)
    {
        if (Handle == 0)
        {
            return;
        }

        NativeMethods.Rectangle panelBounds = new()
        {
            Left = panelX,
            Top = panelY,
            Right = panelX + Math.Max(0, panelWidth),
            Bottom = panelY + Math.Max(0, panelHeight),
        };
        NativeMethods.Rectangle diffBounds = new()
        {
            Left = diffX,
            Top = diffY,
            Right = diffX + Math.Max(0, diffWidth),
            Bottom = diffY + Math.Max(0, diffHeight),
        };
        if (_hasBounds
            && _panelBounds.Left == panelBounds.Left
            && _panelBounds.Top == panelBounds.Top
            && _panelBounds.Right == panelBounds.Right
            && _panelBounds.Bottom == panelBounds.Bottom
            && _diffBounds.Left == diffBounds.Left
            && _diffBounds.Top == diffBounds.Top
            && _diffBounds.Right == diffBounds.Right
            && _diffBounds.Bottom == diffBounds.Bottom)
        {
            return;
        }

        bool sizeChanged = !_hasBounds
            || _panelBounds.Right - _panelBounds.Left != panelBounds.Right - panelBounds.Left
            || _panelBounds.Bottom - _panelBounds.Top != panelBounds.Bottom - panelBounds.Top
            || _diffBounds.Right - _diffBounds.Left != diffBounds.Right - diffBounds.Left
            || _diffBounds.Bottom - _diffBounds.Top != diffBounds.Bottom - diffBounds.Top;
        _hasBounds = true;
        _panelBounds = panelBounds;
        _diffBounds = diffBounds;

        // 两个同级窗口必须完成批量移动后再读取最终客户区，避免连续 WM_SIZE 重复搬动全部子控件。
        _settingBounds = true;
        try
        {
            _ = NativeMethods.MoveWindow(
                Handle,
                panelX,
                panelY,
                Math.Max(0, panelWidth),
                Math.Max(0, panelHeight),
                false);
            _ = NativeMethods.MoveWindow(
                _diffHandle,
                diffX,
                diffY,
                Math.Max(0, diffWidth),
                Math.Max(0, diffHeight),
                false);
        }
        finally
        {
            _settingBounds = false;
        }

        if (sizeChanged)
        {
            Layout();
        }

        // MoveWindow 的批量移动关闭了重绘，结束后必须覆盖新位置上的旧兄弟窗口像素。
        uint redraw = NativeMethods.RedrawInvalidate
            | NativeMethods.RedrawEraseBackground
            | NativeMethods.RedrawAllChildren;
        _ = NativeMethods.RedrawWindow(Handle, 0, 0, redraw);
        _ = NativeMethods.RedrawWindow(_diffHandle, 0, 0, redraw);
    }

    internal void SetVisible(bool visible)
    {
        SetVisible(visible, visible && _diffVisible);
    }

    internal bool ContainsWindow(nint window)
    {
        return NativeFocusNavigation.ContainsWindow(Handle, window)
            || NativeFocusNavigation.ContainsWindow(_diffHandle, window);
    }

    internal bool HandleTabNavigation(bool backwards)
    {
        nint focus = NativeMethods.GetFocus();
        if (NativeFocusNavigation.ContainsWindow(_diffHandle, focus))
        {
            return NativeFocusNavigation.MoveWithinRegion(
                [
                    _previousChangeButton,
                    _nextChangeButton,
                    _diffSearchButton,
                    _previousFileButton,
                    _nextFileButton,
                    _ignoreWhitespaceButton,
                    _sideBySideButton,
                    _unifiedButton,
                    _diffSettingsButton,
                    _unifiedDiff?.Handle ?? 0,
                    _oldDiff?.Handle ?? 0,
                    _newDiff?.Handle ?? 0,
                ],
                focus,
                backwards);
        }

        return NativeFocusNavigation.MoveWithinRegion(
            [
                _changesList,
                _amendButton,
                _commitEdit,
                _lastCommitButton,
                _commitButton,
                _commitAndPushButton,
                _commitSettingsButton,
                _refreshButton,
                _rollbackToolbarButton,
                _showDiffToolbarButton,
                _expandAllToolbarButton,
                _previewToolbarButton,
                _moreActionsButton,
                _closePanelButton,
            ],
            focus,
            backwards);
    }

    internal void SetVisible(bool panelVisible, bool diffVisible)
    {
        if (!panelVisible) ClearChangesHover();
        if (!diffVisible)
        {
            DismissDiffBoundaryHint();
        }
        if (Handle != 0)
        {
            _ = NativeMethods.ShowWindow(Handle, panelVisible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(
                _diffHandle,
                diffVisible && _diffVisible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
    }

    internal void ShowOverview()
    {
        _holdDiffLoadingForTest = false;
        InvalidateDiffRequest();
        _activeChangedFile = null;
        _selectedFilePathIntent = null;
        _diffVisible = false;
        _ = NativeMethods.ShowWindow(_diffHandle, NativeMethods.ShowHide);
        ReleaseDiffContent();
        int firstGroupIndex = _entries.FindIndex(entry => entry.File is null);
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetCurrentSelection,
            firstGroupIndex >= 0 ? unchecked((nuint)firstGroupIndex) : unchecked((nuint)(-1)),
            0);
        SetDiffHeaderText(UiText.SelectGitFile);
        _ = NativeMethods.ShowWindow(_modifiedPreviewButton, NativeMethods.ShowHide);
        UpdateCommitToolbarActionsEnabled();
        PublishDiffPresentation(false, null);
    }

    internal void HideDiffForHost()
    {
        _holdDiffLoadingForTest = false;
        InvalidateDiffRequest();
        if (!_diffVisible)
        {
            return;
        }

        // 切换到普通文件时只隐藏 Diff 控件，保留其结果、选中路径和加载缓存。
        // 这样 Changes 列表后续单击可以后台更新同一临时标签，重新激活标签时也无需重建上下文。
        _ = NativeMethods.ShowWindow(_diffHandle, NativeMethods.ShowHide);
        _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
    }

    internal void CloseDiffForHost(string relativePath)
    {
        if (!string.Equals(_publishedDiffPath, relativePath, StringComparison.OrdinalIgnoreCase)) return;

        // 关闭后台标签结束预览会话，但不改变 Changes 的选择、勾选、草稿或焦点。
        _holdDiffLoadingForTest = false;
        InvalidateDiffRequest();
        _diffVisible = false;
        _publishedDiffPath = null;
        _activeChangedFile = null;
        _ = NativeMethods.ShowWindow(_diffHandle, NativeMethods.ShowHide);
        ReleaseDiffContent();
        SetDiffHeaderText(UiText.SelectGitFile);
        UpdateCommitToolbarActionsEnabled();
    }

    internal void RequestRefresh(
        IReadOnlyList<string>? changedPaths = null,
        bool refreshActiveDiff = false)
    {
        if (_disposed)
        {
            return;
        }

        if (changedPaths is not null)
        {
            foreach (string path in changedPaths)
            {
                string? relativePath = NormalizeWorkspaceChangedPath(path);
                if (relativePath is not null)
                {
                    _changedWorkspacePaths.Add(relativePath);
                }
            }
        }

        _refreshActiveDiffPending |= refreshActiveDiff;
        _refreshPending = true;
        _ = RefreshStatusAsync();
    }

    internal void RequestMetadataRefreshForTest()
    {
        _ = NativeMethods.SendMessage(Handle, WindowMessageRefresh, 0, 0);
    }

    internal void ShowFind()
    {
        string? query = NativeTextPrompt.Show(
            NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot),
            UiText.Find,
            UiText.DiffSearchPrompt,
            NativeTheme.IsDark(_settings.Theme));
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

    internal void ApplyAppearance(ApplicationSettings? settings = null)
    {
        if (settings is not null) _settings = settings;
        if (Handle == 0)
        {
            return;
        }

        int firstVisible = unchecked((int)NativeMethods.SendMessage(_changesList, NativeMethods.ListBoxGetTopIndex, 0, 0));
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(Handle, dark);
        DismissDiffBoundaryHint();
        NativeTheme.ApplyToWindow(_diffHandle, dark);
        foreach (nint control in Controls)
        {
            NativeTheme.ApplyToControl(control, dark);
        }
        _toolTip?.ApplyAppearance(dark);
        _diffToolTip?.ApplyAppearance(dark);
        if (dark)
        {
            _ = NativeMethods.SetWindowTheme(_ignoreWhitespaceButton, string.Empty, string.Empty);
            _ = NativeMethods.SetWindowTheme(_amendButton, string.Empty, string.Empty);
        }

        _unifiedDiff?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        _oldDiff?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        _diffGutter?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark, mutedText: true);
        _newDiff?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        ApplyDiffTextPadding();
        _ = UpdateDiffGutterWidth();
        NativeTheme.ApplyDiffLineColors(_unifiedDiff, _oldDiff, _newDiff, dark);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(dark).Panel);
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.ListBoxSetItemHeight, 0, ChangesRowHeight);
        MeasureCommitTypography();
        Layout(force: true);
        if (firstVisible >= 0)
            _ = NativeMethods.SendMessage(_changesList, NativeMethods.ListBoxSetTopIndex, unchecked((nuint)firstVisible), 0);
        _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
        _ = NativeMethods.InvalidateRectangle(_diffHandle, 0, true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearChangesHover();
        _contextMenu?.Dispose();
        _contextMenu = null;
        _toolTip?.Dispose();
        _toolTip = null;
        _diffToolTip?.Dispose();
        _diffToolTip = null;
        _lifetimeCancellation.Cancel();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _diffCancellation?.Cancel();
        _diffCancellation?.Dispose();
        _diffCancellation = null;
        CancelDiffLoadingIndicator();
        ReleaseDiffContent();
        if (_changesList != 0)
        {
            _ = NativeMethods.RemoveWindowSubclass(
                _changesList,
                ChangesListProcedure,
                ChangesListSubclassIdentifier);
            lock (InstancesGate)
            {
                ChangesListInstances.Remove(_changesList);
            }
        }
        DisposeMetadataWatcher();
        _unifiedDiff?.Dispose();
        _oldDiff?.Dispose();
        _diffGutter?.Dispose();
        _newDiff?.Dispose();
        _unifiedDiff = null;
        _oldDiff = null;
        _diffGutter = null;
        _newDiff = null;
        DestroyControl(ref _emptyChangesTitle);
        DestroyControl(ref _emptyChangesSubtitle);
        DestroyControl(ref _diffLoadingNotice);
        DestroyControl(ref _diffBoundaryHint);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }
        _lifetimeCancellation.Dispose();
        nint handle = Handle;
        nint diffHandle = _diffHandle;
        Handle = 0;
        _diffHandle = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
            Instances.Remove(diffHandle);
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
        if (diffHandle != 0 && NativeMethods.IsWindow(diffHandle))
        {
            _ = NativeMethods.DestroyWindow(diffHandle);
        }
    }

    private IReadOnlyList<nint> Controls =>
    [
        _panelTitle,
        _repositoryLabel,
        _initializeButton,
        _refreshButton,
        _moreActionsButton,
        _closePanelButton,
        _fetchButton,
        _pullButton,
        _pullModeButton,
        _pushButton,
        _remotesButton,
        _advancedOperationsButton,
        _cancelButton,
        _rollbackToolbarButton,
        _showDiffToolbarButton,
        _expandAllToolbarButton,
        _previewToolbarButton,
        _changesList,
        _emptyChangesTitle,
        _emptyChangesSubtitle,
        _previousFileButton,
        _nextFileButton,
        _diffSearchButton,
        _diffSettingsButton,
        _unifiedButton,
        _sideBySideButton,
        _ignoreWhitespaceButton,
        _previousChangeButton,
        _nextChangeButton,
        _modifiedPreviewButton,
        _rollbackButton,
        _commitLabel,
        _diffTitle,
        _diffFileSummary,
        _diffChangeSummary,
        _diffLoadingNotice,
        _diffBoundaryHint,
        _commitEdit,
        _amendButton,
        _lastCommitButton,
        _commitChangeCount,
        _commitSettingsButton,
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

        if (IsControlColorMessageForTest(message))
        {
            return instance.ApplyControlColor(unchecked((nint)wordParameter));
        }

        switch (message)
        {
            case NativeMethods.WindowMessageSize:
                if (!instance._settingBounds)
                {
                    instance.Layout();
                }
                return 0;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter, longParameter);
                return 0;
            case NativeMethods.WindowMessageDrawItem:
                return instance.DrawControl(longParameter) ? 1 : 0;
            case NativeMethods.WindowMessageNotify:
                if (!instance._suppressDiffNotifications)
                {
                    instance.SynchronizeDiffScroll(longParameter);
                }
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return instance.PaintBackground(window, unchecked((nint)wordParameter));
            case NativeMethods.WindowMessageSetFocus:
                _ = NativeMethods.SetFocus(instance._changesList);
                return 0;
            case WindowMessageRefresh:
                instance.RequestRefresh();
                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private static nint HandleChangesListMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        NativeGitPanel? instance;
        lock (InstancesGate)
        {
            ChangesListInstances.TryGetValue(window, out instance);
        }

        if (instance is not null
            && instance.TryHandleChangesListInput(message, wordParameter, longParameter, out nint result))
        {
            instance.HandleChangesHoverMessage(message, wordParameter, longParameter);
            return result;
        }

        nint defaultResult = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        instance?.HandleChangesHoverMessage(message, wordParameter, longParameter);
        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            _ = NativeMethods.RemoveWindowSubclass(window, ChangesListProcedure, ChangesListSubclassIdentifier);
            lock (InstancesGate) ChangesListInstances.Remove(window);
        }
        if (instance is not null
            && message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
        {
            _ = NativeMethods.InvalidateRectangle(window, 0, true);
        }

        return defaultResult;
    }

    private bool TryHandleChangesListInput(
        uint message,
        nuint wordParameter,
        nint longParameter,
        out nint result)
    {
        result = 0;
        if (message == NativeMethods.WindowMessageKeyDown
            && unchecked((int)wordParameter) == NativeMethods.VirtualKeySpace)
        {
            ToggleSelectedEntry();
            return true;
        }

        if (message == NativeMethods.WindowMessageContextMenu)
        {
            if (!TryGetContextMenuPoint(longParameter, out int x, out int y)
                || !TryGetChangeEntryHitFromScreenPoint(x, y, out int contextIndex)
                || contextIndex < 0
                || contextIndex >= _entries.Count
                || _entries[contextIndex].File is null)
            {
                return true;
            }

            ShowChangesContextMenu(contextIndex, x, y);
            return true;
        }

        if (message == NativeMethods.WindowMessageKeyDown
            && unchecked((int)wordParameter) == NativeMethods.VirtualKeyEnter)
        {
            int selectedIndex = GetSelectedEntryIndex();
            if (selectedIndex >= 0
                && selectedIndex < _entries.Count
                && _entries[selectedIndex].File is null)
            {
                ToggleGroupCollapsed(_entries[selectedIndex].Group);
            }
            else
            {
                // Enter 与双击都打开并激活唯一跟随 Changes 的 Diff。
                _ = OpenSelectedDiffAsync();
            }
            return true;
        }

        if (message is not (NativeMethods.WindowMessageLeftButtonDown
            or NativeMethods.WindowMessageLeftButtonDoubleClick))
        {
            return false;
        }

        if (TryGetGroupChevronHit(longParameter, out int groupIndex))
        {
            if (message == NativeMethods.WindowMessageLeftButtonDown)
            {
                ToggleGroupCollapsed(_entries[groupIndex].Group);
            }
            return true;
        }
        if (TryGetCheckboxHit(longParameter, out int index))
        {
            if (message == NativeMethods.WindowMessageLeftButtonDoubleClick)
            {
                return true;
            }

            ToggleEntryAt(index);
            return true;
        }

        if (message == NativeMethods.WindowMessageLeftButtonDoubleClick
            && TryGetChangeEntryHit(
                longParameter,
                out int clickedGroupIndex,
                out _,
                out _,
                out _)
            && _entries[clickedGroupIndex].File is null)
        {
            ToggleGroupCollapsed(_entries[clickedGroupIndex].Group);
            return true;
        }

        return false;
    }

    private bool TryGetCheckboxHit(nint longParameter, out int index)
    {
        if (!TryGetChangeEntryHit(
                longParameter,
                out index,
                out NativeMethods.Rectangle rectangle,
                out int x,
                out int y))
        {
            return false;
        }

        NativeChangeListRowLayout layout = CalculateChangeListRowLayout(
            rectangle.Left,
            _entries[index].File is null);
        int checkboxLeft = layout.CheckboxLeft;
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        return x >= checkboxLeft - NativeTheme.Scale(3)
            && x <= checkboxLeft + layout.CheckboxSize + NativeTheme.Scale(3)
            && y >= centerY - NativeTheme.Scale(9)
            && y <= centerY + NativeTheme.Scale(9);
    }

    private bool TryGetGroupChevronHit(nint longParameter, out int index)
    {
        if (!TryGetChangeEntryHit(
                longParameter,
                out index,
                out NativeMethods.Rectangle rectangle,
                out int x,
                out int y)
            || _entries[index].File is not null)
        {
            return false;
        }

        NativeChangeListRowLayout layout = CalculateChangeListRowLayout(rectangle.Left, isGroup: true);
        int left = layout.ChevronLeft;
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        // 命中区覆盖箭头所在的完整槽位，并允许用户点在箭头左侧的留白上。
        // 这样在不同 DPI 和 ListBox 行内边距下，点击视觉上的分组入口不会落空。
        return x >= left - NativeTheme.Scale(5)
            && x <= left + NativeTheme.Scale(12)
            && y >= centerY - NativeTheme.Scale(8)
            && y <= centerY + NativeTheme.Scale(8);
    }

    private bool TryGetChangeEntryHit(
        nint longParameter,
        out int index,
        out NativeMethods.Rectangle rectangle,
        out int x,
        out int y)
    {
        index = -1;
        rectangle = default;
        x = 0;
        y = 0;
        nuint hit = unchecked((nuint)NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxItemFromPoint,
            0,
            longParameter));
        if (NativeMethods.HighWord(hit) != 0)
        {
            return false;
        }

        index = NativeMethods.LowWord(hit);
        if (index < 0
            || index >= _entries.Count
            || !TryGetChangesListItemRectangle(index, out rectangle))
        {
            index = -1;
            return false;
        }

        x = unchecked((short)NativeMethods.LowWord(unchecked((nuint)longParameter)));
        y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)longParameter)));
        return true;
    }

    private bool TryGetChangeEntryHitFromScreenPoint(int screenX, int screenY, out int index)
    {
        index = -1;
        NativeMethods.Point point = new() { X = screenX, Y = screenY };
        if (!NativeMethods.ScreenToClient(_changesList, ref point))
        {
            return false;
        }

        return TryGetChangeEntryHit(PackPoint(point.X, point.Y), out index, out _, out _, out _);
    }

    private bool TryGetContextMenuPoint(nint longParameter, out int x, out int y)
    {
        x = unchecked((short)NativeMethods.LowWord(unchecked((nuint)longParameter)));
        y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)longParameter)));
        if (x != -1 || y != -1)
        {
            return true;
        }

        int selectedIndex = GetSelectedEntryIndex();
        if (!TryGetChangesListItemRectangle(selectedIndex, out NativeMethods.Rectangle rectangle))
        {
            return false;
        }

        NativeMethods.Point point = new()
        {
            X = rectangle.Left + NativeTheme.Scale(32),
            Y = Math.Max(rectangle.Top, rectangle.Bottom - 1),
        };
        if (!NativeMethods.ClientToScreen(_changesList, ref point))
        {
            return false;
        }

        x = point.X;
        y = point.Y;
        return true;
    }

    private void ShowChangesContextMenu(int index, int screenX, int screenY)
    {
        if (index < 0 || index >= _entries.Count || _entries[index].File is not { } file)
        {
            return;
        }

        _contextMenu?.Dispose();
        bool busy = _operationRunning;
        _contextMenu = NativeContextMenu.Show(
            Handle,
            screenX,
            screenY,
            [
                new(UiText.ShowDiff, NativeContextMenuIcon.Compare, () => HandleContextCommand(CommandContextShowDiff, file)),
                new(UiText.Rollback + "…", NativeContextMenuIcon.Reset, () => HandleContextCommand(CommandContextRollback, file), !busy),
                null,
                new(UiText.FileHistory, NativeContextMenuIcon.History, () => HandleContextCommand(CommandContextFileHistory, file)),
                new(UiText.Blame, NativeContextMenuIcon.Blame, () => HandleContextCommand(CommandContextBlame, file)),
                null,
                new(UiText.CopyPath, NativeContextMenuIcon.Copy, () => HandleContextCommand(CommandContextCopyPath, file)),
                new(UiText.RevealInExplorer, NativeContextMenuIcon.Open, () => HandleContextCommand(CommandContextReveal, file)),
            ],
            NativeTheme.IsDark(_settings.Theme));
    }

    private void HandleContextCommand(uint command, GitChangedFile file)
    {
        switch (command)
        {
            case CommandContextShowDiff:
                _ = OpenContextDiffAsync(file);
                break;
            case CommandContextRollback:
                _ = RollbackSelectedAsync(file);
                break;
            case CommandContextFileHistory:
                _openFileHistory(file.RelativePath);
                break;
            case CommandContextBlame:
                _ = _openBlame(file.RelativePath);
                break;
            case CommandContextCopyPath:
                string fullPath = Path.Combine(
                    _repository?.RepositoryRoot ?? _workspaceRoot,
                    file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                _setStatus(NativeClipboard.TrySetText(Handle, Path.GetFullPath(fullPath))
                    ? UiText.PathCopied
                    : UiText.ClipboardUnavailable);
                break;
            case CommandContextReveal:
                string path = Path.GetFullPath(Path.Combine(
                    _repository?.RepositoryRoot ?? _workspaceRoot,
                    file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                ExternalLaunchResult result = ExternalProgramLauncher.RevealInExplorer(path);
                if (!result.IsSuccess)
                {
                    _setStatus(result.ErrorMessage ?? UiText.ExternalProgramFailed);
                }
                break;
        }
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
        _panelTitle = CreateControl(NativeMethods.StaticClass, UiText.CommitPanel, 60, NativeMethods.StaticOwnerDraw);
        _repositoryLabel = CreateControl(NativeMethods.StaticClass, UiText.GitRefreshing, 61, NativeMethods.StaticOwnerDraw);
        _initializeButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.InitializeRepository,
            CommandInitialize,
            NativeMethods.ButtonPushButton);
        _refreshButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandRefresh, NativeMethods.ButtonPushButton);
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
        _fetchButton = CreateDiffControl(NativeMethods.ButtonClass, UiText.Fetch, CommandFetch, NativeMethods.ButtonPushButton);
        _pullButton = CreateDiffControl(NativeMethods.ButtonClass, UiText.Pull, CommandPull, NativeMethods.ButtonPushButton);
        _pullModeButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            PullModeLabels[0],
            CommandChoosePullMode,
            NativeMethods.ButtonPushButton);
        _pushButton = CreateDiffControl(NativeMethods.ButtonClass, UiText.Push, CommandPush, NativeMethods.ButtonPushButton);
        _remotesButton = CreateDiffControl(NativeMethods.ButtonClass, UiText.Remotes, CommandRemotes, NativeMethods.ButtonPushButton);
        _advancedOperationsButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            UiText.AdvancedGitOperations,
            CommandAdvancedOperations,
            NativeMethods.ButtonPushButton);
        _cancelButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CancelOperation,
            CommandCancelOperation,
            NativeMethods.ButtonPushButton);
        _rollbackToolbarButton = CreateControl(
            NativeMethods.ButtonClass,
            "↶",
            CommandRollbackToolbar,
            NativeMethods.ButtonPushButton);
        _showDiffToolbarButton = CreateControl(
            NativeMethods.ButtonClass,
            "⇄",
            CommandShowDiff,
            NativeMethods.ButtonPushButton);
        _expandAllToolbarButton = CreateControl(
            NativeMethods.ButtonClass,
            "⇵",
            CommandExpandAll,
            NativeMethods.ButtonPushButton);
        _previewToolbarButton = CreateControl(
            NativeMethods.ButtonClass,
            "◉",
            CommandPreview,
            NativeMethods.ButtonPushButton);
        _changesList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            ChangesListIdentifier,
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight);
        _emptyChangesTitle = CreateControl(
            NativeMethods.StaticClass,
            "没有待提交的更改",
            EmptyChangesTitleIdentifier,
            NativeMethods.StaticOwnerDraw);
        _emptyChangesSubtitle = CreateControl(
            NativeMethods.StaticClass,
            "工作区与 HEAD 一致。",
            EmptyChangesSubtitleIdentifier,
            NativeMethods.StaticOwnerDraw);
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetItemHeight,
            0,
            ChangesRowHeight);
        lock (InstancesGate)
        {
            ChangesListInstances.Add(_changesList, this);
        }
        if (!NativeMethods.SetWindowSubclass(
                _changesList,
                ChangesListProcedure,
                ChangesListSubclassIdentifier,
                0))
        {
            lock (InstancesGate)
            {
                ChangesListInstances.Remove(_changesList);
            }
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitChangesListSubclassFailed);
        }
        _previousFileButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandPreviousFile,
            NativeMethods.ButtonPushButton);
        _nextFileButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandNextFile,
            NativeMethods.ButtonPushButton);
        _diffSearchButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandDiffSearch,
            NativeMethods.ButtonPushButton);
        _diffSettingsButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandDiffSettings,
            NativeMethods.ButtonPushButton);
        _unifiedButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            UiText.UnifiedDiff,
            CommandUnified,
            NativeMethods.ButtonPushButton);
        _sideBySideButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            UiText.SideBySideDiff,
            CommandSideBySide,
            NativeMethods.ButtonPushButton);
        _ignoreWhitespaceButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            UiText.IgnoreWhitespace,
            CommandIgnoreWhitespace,
            NativeMethods.ButtonPushButton);
        _previousChangeButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandPreviousChange,
            NativeMethods.ButtonPushButton);
        _nextChangeButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandNextChange,
            NativeMethods.ButtonPushButton);
        _modifiedPreviewButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            UiText.ModifiedPreview,
            CommandModifiedPreview,
            NativeMethods.ButtonPushButton);
        _rollbackButton = CreateDiffControl(
            NativeMethods.ButtonClass,
            UiText.Rollback,
            CommandRollback,
            NativeMethods.ButtonPushButton);
        _commitLabel = CreateControl(NativeMethods.StaticClass, UiText.CommitMessage, 62, NativeMethods.StaticOwnerDraw);
        _diffTitle = CreateDiffControl(NativeMethods.StaticClass, UiText.SelectGitFile, 63, NativeMethods.StaticOwnerDraw);
        _diffFileSummary = CreateDiffControl(
            NativeMethods.StaticClass,
            string.Empty,
            DiffFileSummaryIdentifier,
            NativeMethods.StaticOwnerDraw);
        _diffChangeSummary = CreateDiffControl(
            NativeMethods.StaticClass,
            string.Empty,
            DiffChangeSummaryIdentifier,
            NativeMethods.StaticOwnerDraw);
        _diffLoadingNotice = CreateDiffControl(
            NativeMethods.StaticClass,
            string.Empty,
            DiffLoadingIdentifier,
            NativeMethods.StaticOwnerDraw);
        _commitEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            31,
            NativeMethods.EditMultiline
                | NativeMethods.EditAutoVerticalScroll
                | NativeMethods.EditWantReturn);
        _ = NativeMethods.SendMessage(
            _commitEdit,
            NativeMethods.EditSetMargins,
            NativeMethods.EditMarginLeftRight,
            unchecked((nint)0x00080008));
        _ = NativeMethods.SendMessage(_commitEdit, NativeMethods.EditSetCueBanner, 1, UiText.CommitMessage);
        _amendButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.AmendLastCommit,
            CommandAmend,
            NativeMethods.ButtonPushButton);
        _lastCommitButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.LastCommit,
            CommandLastCommit,
            NativeMethods.ButtonPushButton);
        _commitChangeCount = CreateControl(
            NativeMethods.StaticClass,
            string.Empty,
            CommitChangeCountIdentifier,
            NativeMethods.StaticOwnerDraw);
        _commitSettingsButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandCommitSettings,
            NativeMethods.ButtonPushButton);
        _commitButton = CreateControl(NativeMethods.ButtonClass, UiText.Commit, CommandCommit, NativeMethods.ButtonPushButton);
        _commitAndPushButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CommitAndPush,
            CommandCommitAndPush,
            NativeMethods.ButtonPushButton);
        _unifiedDiff = new(_diffHandle, 40);
        _oldDiff = new(_diffHandle, 41);
        _diffGutter = new(_diffHandle, 42);
        _newDiff = new(_diffHandle, 43);
        _diffBoundaryHint = CreateDiffControl(
            NativeMethods.StaticClass, string.Empty, DiffBoundaryHintIdentifier, NativeMethods.StaticOwnerDraw);
        _ = NativeMethods.ShowWindow(_diffBoundaryHint, NativeMethods.ShowHide);
        _unifiedDiff.SetLineNumbersVisible(false);
        _oldDiff.SetLineNumbersVisible(false);
        _diffGutter.SetLineNumbersVisible(false);
        _newDiff.SetLineNumbersVisible(false);
        _unifiedDiff.SetWordWrap(false);
        _oldDiff.SetWordWrap(false);
        _diffGutter.SetWordWrap(false);
        _newDiff.SetWordWrap(false);
        _diffGutter.SetScrollBarsVisible(horizontal: false, vertical: false);
        _oldDiff.SetVisible(false);
        _diffGutter.SetVisible(false);
        _newDiff.SetVisible(false);
        _ = NativeMethods.ShowWindow(_initializeButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_cancelButton, NativeMethods.ShowHide);
        foreach (nint control in new[]
        {
            _fetchButton,
            _pullButton,
            _pullModeButton,
            _pushButton,
            _remotesButton,
            _advancedOperationsButton,
        })
        {
            _ = NativeMethods.ShowWindow(control, NativeMethods.ShowHide);
        }

        _ = NativeMethods.ShowWindow(_modifiedPreviewButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_rollbackButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_lastCommitButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_commitChangeCount, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_commitSettingsButton, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_diffLoadingNotice, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_emptyChangesTitle, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_emptyChangesSubtitle, NativeMethods.ShowHide);
    }

    private void CreateToolTips()
    {
        _toolTip = new NativeToolTip(Handle);
        _toolTip.Add(_refreshButton, UiText.Refresh);
        _toolTip.Add(_moreActionsButton, UiText.MoreActions);
        _toolTip.Add(_closePanelButton, UiText.Close);
        _toolTip.Add(_rollbackToolbarButton, UiText.Rollback);
        _toolTip.Add(_showDiffToolbarButton, UiText.ShowDiff);
        _toolTip.Add(_expandAllToolbarButton, UiText.ExpandAll);
        _toolTip.Add(_previewToolbarButton, UiText.Preview);
        _toolTip.Add(_amendButton, UiText.AmendUnavailable);
        _toolTip.Add(_lastCommitButton, UiText.LastCommit);
        _toolTip.Add(_commitChangeCount, UiText.CommitSelectionCount);
        _toolTip.Add(_commitButton, UiText.Commit);
        _toolTip.Add(_commitAndPushButton, UiText.CommitAndPush);
        _toolTip.Add(_commitSettingsButton, UiText.CommitSettings);
        _toolTip.Add(_commitLabel, UiText.CommitMessage);
        _diffToolTip = new NativeToolTip(_diffHandle);
        _diffToolTip.Add(_previousChangeButton, UiText.PreviousChange);
        _diffToolTip.Add(_nextChangeButton, UiText.NextChange);
        _diffToolTip.Add(_previousFileButton, UiText.PreviousFile);
        _diffToolTip.Add(_nextFileButton, UiText.NextFile);
        _diffToolTip.Add(_diffSearchButton, UiText.Find);
        _diffToolTip.Add(_diffSettingsButton, UiText.Settings);
        _diffToolTip.Add(_ignoreWhitespaceButton, UiText.IgnoreWhitespace);
        _diffToolTip.Add(_unifiedButton, UiText.UnifiedDiff);
        _diffToolTip.Add(_sideBySideButton, UiText.SideBySideDiff);
        _diffToolTip.Add(_diffTitle, $"HEAD → {UiText.CurrentVersion}");
    }

    private nint CreateControl(string className, string text, int identifier, uint specificStyle)
    {
        return CreateControl(Handle, className, text, identifier, specificStyle);
    }

    private nint CreateDiffControl(string className, string text, int identifier, uint specificStyle)
    {
        return CreateControl(_diffHandle, className, text, identifier, specificStyle);
    }

    private static nint CreateControl(
        nint parent,
        string className,
        string text,
        int identifier,
        uint specificStyle)
    {
        uint controlStyle = className.Equals(NativeMethods.ButtonClass, StringComparison.Ordinal)
            && specificStyle == NativeMethods.ButtonPushButton
            ? NativeMethods.ButtonOwnerDraw
            : className.Equals(NativeMethods.ButtonClass, StringComparison.Ordinal)
                ? specificStyle | NativeMethods.ButtonFlat
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
            parent,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.GitControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
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
            string reason = _runtime.UnavailableReason ?? UiText.GitUnavailable;
            _ = NativeMethods.SetWindowText(
                _repositoryLabel,
                reason);
            SetGitControlsEnabled(false);
            ShowDiffNotice(reason);
            _setStatus(reason);
            _gitUnavailable(reason);
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
        _amend = false;
        _amendLoading = false;
        _commitDraftBeforeAmend = null;
        _amendRequestVersion++;
        _ = NativeMethods.SetWindowText(_commitEdit, string.Empty);
        _ = NativeMethods.InvalidateRectangle(_amendButton, 0, true);
        _statusUpdated(null);
        _selection.Reconcile([]);
        PopulateChanges([]);
        UpdateEmptyChangesState();
        if (repository.Kind == GitRepositoryKind.PlainDirectory)
        {
            _ = NativeMethods.SetWindowText(_repositoryLabel, UiText.PlainDirectoryGitStatus);
            _ = NativeMethods.ShowWindow(_initializeButton, NativeMethods.ShowNormal);
            _ = NativeMethods.ShowWindow(_refreshButton, NativeMethods.ShowHide);
            SetGitControlsEnabled(false, allowInitialize: true);
            ShowDiffNotice(UiText.PlainDirectoryGitNotice);
            return;
        }

        if (repository.Kind == GitRepositoryKind.BareRepository)
        {
            _ = NativeMethods.SetWindowText(_repositoryLabel, UiText.BareRepositoryGitStatus);
            _ = NativeMethods.ShowWindow(_initializeButton, NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(_refreshButton, NativeMethods.ShowNormal);
            SetGitControlsEnabled(false);
            ShowDiffNotice(UiText.BareRepositoryGitNotice);
            return;
        }

        _ = NativeMethods.ShowWindow(_initializeButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_refreshButton, NativeMethods.ShowNormal);
        _statusService = new(_runtime!);
        _diffService = new GitDiffService(_runtime!);
        _commitService = new GitCommitService(_runtime!);
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
                bool refreshActiveDiff = _refreshActiveDiffPending;
                _refreshActiveDiffPending = false;
                string[] changedWorkspacePaths = [.. _changedWorkspacePaths];
                _changedWorkspacePaths.Clear();
                bool initialLoad = _status is null;
                if (initialLoad)
                {
                    _setStatus(UiText.GitRefreshing);
                }

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

                IReadOnlyList<GitChangedFile>? previousFiles = _status?.Files;
                GitStatusSnapshot? previousStatus = _status;
                string? selectedPath = GetSelectedChangedFilePath();
                _status = result.Snapshot;
                _selection.Reconcile(_status.Files);
                UpdateCommitSummary();
                bool statusChanged = ShouldRefreshChrome(previousStatus, _status);
                bool filesChanged = !StatusFilesEquivalent(previousFiles, _status.Files);
                if (statusChanged)
                {
                    _statusUpdated(_status);
                }
                bool reloadActiveDiff = ShouldReloadActiveDiff(
                    previousFiles,
                    _status.Files,
                    selectedPath,
                    changedWorkspacePaths,
                    refreshActiveDiff);
                if (!reloadActiveDiff
                    && selectedPath is not null
                    && _status.Files.FirstOrDefault(
                        file => file.RelativePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                        is { } selectedFile)
                {
                    // 改选后的旧正文仍可见，但不再代表当前查询的输入版本。
                    // 优先核对当前加载请求，避免无关的元数据通知反复取消同一次加载。
                    string? fingerprint = IsSameDiffRequest(
                        _loadingDiffPath, _loadingDiffIgnoreWhitespace, selectedPath, _ignoreWhitespace)
                        ? _loadingDiffFingerprint
                        : IsSameDiffRequest(
                            _activeDiff?.RelativePath, _activeDiffIgnoreWhitespace, selectedPath, _ignoreWhitespace)
                            ? _activeDiffFingerprint
                            : null;
                    reloadActiveDiff = fingerprint is not null
                        && !string.Equals(fingerprint, GetDiffInputFingerprint(selectedFile), StringComparison.Ordinal);
                }
                if (filesChanged)
                {
                    PopulateChanges(_status.Files);
                }
                else
                {
                    UpdateCommitActionsEnabled();
                }
                if (statusChanged)
                {
                    _ = NativeMethods.SetWindowText(_repositoryLabel, string.Empty);
                    _refreshChrome();
                }
                if (_status.Files.Count == 0)
                {
                    _activeChangedFile = null;
                    _activeDiff = null;
                    SetDiffVisible(false);
                    ShowDiffNotice(UiText.NoGitChanges);
                }
                else if (_diffVisible
                    && ShouldShowSelectedDiffAfterRefresh(
                        initialLoad,
                        selectedPath,
                        GetSelectedChangedFilePath(),
                        reloadActiveDiff))
                {
                    // Diff 自行处理取消、版本及局部反馈；不能占住状态刷新循环，
                    // 否则慢查询期间新文件、复选集合与后续磁盘通知都要等正文完成。
                    _ = ShowSelectedDiffAsync(forceReload: reloadActiveDiff);
                }

                if (initialLoad)
                {
                    _setStatus(UiText.GitReady);
                }
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
        _changesPopulateCount++;
        if (TryUpdateChangesInPlace(files))
        {
            UpdateChangesHover();
            UpdateEmptyChangesState();
            UpdateCommitActionsEnabled();
            UpdateCommitToolbarActionsEnabled();
            return;
        }

        if (TryApplyChangesListDelta(files))
        {
            UpdateChangesHover();
            UpdateEmptyChangesState();
            UpdateCommitActionsEnabled();
            UpdateCommitToolbarActionsEnabled();
            return;
        }

        _updatingChangesList = true;
        try
        {
            int topIndex = checked((int)NativeMethods.SendMessage(
                _changesList,
                NativeMethods.ListBoxGetTopIndex,
                0,
                0));
            int selectedIndex = GetSelectedEntryIndex();
            string? selectedPath = _selectedFilePathIntent
                ?? (selectedIndex >= 0 && selectedIndex < _entries.Count
                    ? _entries[selectedIndex].File?.RelativePath
                    : _activeChangedFile?.RelativePath);
            _entries.Clear();
            _changesListResetCount++;
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
                    if (topIndex >= 0)
                    {
                        _ = NativeMethods.SendMessage(
                            _changesList,
                            NativeMethods.ListBoxSetTopIndex,
                            unchecked((nuint)Math.Min(topIndex, Math.Max(0, _entries.Count - 1))),
                            0);
                    }
                    _selectedFilePathIntent = selectedPath;
                    return;
                }
            }

            int firstGroupIndex = _entries.FindIndex(entry => entry.File is null);
            if (firstGroupIndex >= 0)
            {
                _ = NativeMethods.SendMessage(
                    _changesList,
                    NativeMethods.ListBoxSetCurrentSelection,
                    unchecked((nuint)firstGroupIndex),
                    0);
                _selectedFilePathIntent = null;
            }
        }
        finally
        {
            _updatingChangesList = false;
            UpdateChangesHover();
            UpdateEmptyChangesState();
            UpdateCommitActionsEnabled();
            UpdateCommitToolbarActionsEnabled();
        }
    }

    private bool TryApplyChangesListDelta(IReadOnlyList<GitChangedFile> files)
    {
        List<ChangeListEntry> nextEntries = BuildChangeListEntries(files);
        if (_entries.Count == 0 || nextEntries.Count == 0)
        {
            return false;
        }

        if (!TryComputeChangesListDelta(
                _entries,
                nextEntries,
                out int prefix,
                out int oldCount,
                out int newCount))
        {
            return false;
        }

        int topIndex = checked((int)NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxGetTopIndex,
            0,
            0));
        int selectedIndex = GetSelectedEntryIndex();
        string? selectedPath = GetSelectedChangedFilePath();
        ChangeListEntry? selectedEntry = selectedIndex >= 0 && selectedIndex < _entries.Count
            ? _entries[selectedIndex]
            : null;
        string? selectedGroup = selectedEntry?.Group.ToString();
        string? topAnchor = topIndex >= 0 && topIndex < _entries.Count
            ? ChangeListEntryAnchor(_entries[topIndex])
            : null;
        _updatingChangesList = true;
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageSetRedraw, 0, 0);
        try
        {
            for (int index = oldCount - 1; index >= 0; index--)
            {
                _ = NativeMethods.SendMessage(
                    _changesList,
                    NativeMethods.ListBoxDeleteString,
                    unchecked((nuint)(prefix + index)),
                    0);
            }

            for (int index = 0; index < newCount; index++)
            {
                ChangeListEntry entry = nextEntries[prefix + index];
                _ = NativeMethods.SendMessage(
                    _changesList,
                    NativeMethods.ListBoxInsertString,
                    unchecked((nuint)(prefix + index)),
                    EntryDisplayText(entry));
            }

            _entries.Clear();
            _entries.AddRange(nextEntries);
            UpdateChangesGroupStrings(nextEntries);
            RestoreListSelection(
                selectedPath,
                selectedIndex,
                topIndex,
                selectedGroup,
                topAnchor);
            _changesListDeltaCount++;
        }
        finally
        {
            _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageSetRedraw, 1, 0);
            _updatingChangesList = false;
        }

        _ = NativeMethods.InvalidateRectangle(_changesList, 0, true);
        return true;
    }

    internal static (bool CanApply, int Prefix, int RemovedCount, int AddedCount)
        ChangesListDeltaForTest(
            IReadOnlyList<GitChangedFile> previous,
            IReadOnlyList<GitChangedFile> current,
            IReadOnlySet<GitChangeGroup>? collapsedGroups = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        HashSet<GitChangeGroup> collapsed = collapsedGroups is null
            ? []
            : [.. collapsedGroups];
        List<ChangeListEntry> previousEntries = BuildChangeListEntries(previous, collapsed);
        List<ChangeListEntry> currentEntries = BuildChangeListEntries(current, collapsed);
        bool canApply = TryComputeChangesListDelta(
            previousEntries,
            currentEntries,
            out int prefix,
            out int removedCount,
            out int addedCount);
        return (canApply, prefix, removedCount, addedCount);
    }

    private static bool TryComputeChangesListDelta(
        IReadOnlyList<ChangeListEntry> previous,
        IReadOnlyList<ChangeListEntry> current,
        out int prefix,
        out int removedCount,
        out int addedCount)
    {
        prefix = 0;
        removedCount = 0;
        addedCount = 0;
        while (prefix < previous.Count
            && prefix < current.Count
            && SameChangeListEntry(previous[prefix], current[prefix]))
        {
            prefix++;
        }

        int previousSuffix = previous.Count - 1;
        int currentSuffix = current.Count - 1;
        while (previousSuffix >= prefix
            && currentSuffix >= prefix
            && SameChangeListEntry(previous[previousSuffix], current[currentSuffix]))
        {
            previousSuffix--;
            currentSuffix--;
        }

        removedCount = previousSuffix - prefix + 1;
        addedCount = currentSuffix - prefix + 1;
        return removedCount > 0 || addedCount > 0;
    }

    private static string EntryDisplayText(ChangeListEntry entry)
    {
        if (entry.File is null)
        {
            return UiText.GitGroup(
                entry.Group == GitChangeGroup.Changes ? UiText.Changes : UiText.UnversionedFiles,
                entry.GroupFileCount);
        }

        GitChangedFile file = entry.File;
        string staged = file.HasStagedChanges && file.HasWorkingTreeChanges
            ? UiText.StagedAndModified
            : file.HasStagedChanges ? UiText.Staged : string.Empty;
        string rename = file.OriginalRelativePath is null ? string.Empty : $" ← {file.OriginalRelativePath}";
        return $"{file.RelativePath}{rename}{staged}";
    }

    private bool TryUpdateChangesInPlace(IReadOnlyList<GitChangedFile> files)
    {
        List<ChangeListEntry> nextEntries = BuildChangeListEntries(files);
        if (nextEntries.Count != _entries.Count)
        {
            return false;
        }

        for (int index = 0; index < nextEntries.Count; index++)
        {
            if (!SameChangeListEntry(_entries[index], nextEntries[index]))
            {
                return false;
            }
        }

        for (int index = 0; index < nextEntries.Count; index++)
        {
            _entries[index] = nextEntries[index];
        }

        // 文件行只按路径复用；分组计数变化只替换两个分组行的原生文字，
        // 不把整个列表升级为删除/插入操作。
        UpdateChangesGroupStrings(nextEntries);
        _ = NativeMethods.InvalidateRectangle(_changesList, 0, true);
        return true;
    }

    internal static bool CanUpdateChangesInPlaceForTest(
        IReadOnlyList<GitChangedFile> previous,
        IReadOnlyList<GitChangedFile> current,
        IReadOnlySet<GitChangeGroup>? collapsedGroups = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        HashSet<GitChangeGroup> collapsed = collapsedGroups is null
            ? []
            : [.. collapsedGroups];
        List<ChangeListEntry> previousEntries = BuildChangeListEntries(previous, collapsed);
        List<ChangeListEntry> currentEntries = BuildChangeListEntries(current, collapsed);
        return previousEntries.Count == currentEntries.Count
            && previousEntries.Zip(currentEntries).All(pair => SameChangeListEntry(pair.First, pair.Second));
    }

    private List<ChangeListEntry> BuildChangeListEntries(IReadOnlyList<GitChangedFile> files)
    {
        return BuildChangeListEntries(files, _collapsedGroups);
    }

    private static List<ChangeListEntry> BuildChangeListEntries(
        IReadOnlyList<GitChangedFile> files,
        HashSet<GitChangeGroup> collapsedGroups)
    {
        List<ChangeListEntry> entries = [];
        foreach (GitChangeGroup group in Enum.GetValues<GitChangeGroup>())
        {
            GitChangedFile[] groupFiles = files.Where(file => file.Group == group).ToArray();
            entries.Add(new(group, null, groupFiles.Length));
            if (!collapsedGroups.Contains(group))
            {
                entries.AddRange(groupFiles.Select(file => new ChangeListEntry(group, file)));
            }
        }

        return entries;
    }

    private static bool SameChangeListEntry(ChangeListEntry left, ChangeListEntry right)
    {
        if (left.Group != right.Group || (left.File is null) != (right.File is null))
        {
            return false;
        }

        if (left.File is null)
        {
            return right.File is null;
        }

        if (right.File is null)
        {
            return false;
        }

        return left.File.RelativePath.Equals(right.File.RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateChangesGroupStrings(IReadOnlyList<ChangeListEntry> entries)
    {
        if (_changesList == 0 || entries.Count == 0)
        {
            return;
        }

        int topIndex = checked((int)NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxGetTopIndex,
            0,
            0));
        int selectedIndex = GetSelectedEntryIndex();
        string? selectedPath = GetSelectedChangedFilePath();
        ChangeListEntry? selectedEntry = selectedIndex >= 0 && selectedIndex < _entries.Count
            ? _entries[selectedIndex]
            : null;
        string? selectedGroup = selectedEntry?.Group.ToString();
        string? topAnchor = topIndex >= 0 && topIndex < _entries.Count
            ? ChangeListEntryAnchor(_entries[topIndex])
            : null;
        bool changed = false;
        bool wasUpdating = _updatingChangesList;
        _updatingChangesList = true;
        _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageSetRedraw, 0, 0);
        try
        {
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].File is not null)
                {
                    continue;
                }

                string desired = EntryDisplayText(entries[index]);
                if (string.Equals(GetListBoxItemText(index), desired, StringComparison.Ordinal))
                {
                    continue;
                }

                _ = NativeMethods.SendMessage(
                    _changesList,
                    NativeMethods.ListBoxDeleteString,
                    unchecked((nuint)index),
                    0);
                _ = NativeMethods.SendMessage(
                    _changesList,
                    NativeMethods.ListBoxInsertString,
                    unchecked((nuint)index),
                    desired);
                changed = true;
            }
        }
        finally
        {
            _ = NativeMethods.SendMessage(_changesList, NativeMethods.WindowMessageSetRedraw, 1, 0);
            _updatingChangesList = wasUpdating;
        }

        bool restoreWasUpdating = _updatingChangesList;
        _updatingChangesList = true;
        try
        {
            RestoreListSelection(selectedPath, selectedIndex, topIndex, selectedGroup, topAnchor);
        }
        finally
        {
            _updatingChangesList = restoreWasUpdating;
        }
        if (changed)
        {
            _ = NativeMethods.InvalidateRectangle(_changesList, 0, true);
        }
    }

    private string GetListBoxItemText(int index)
    {
        if (index < 0 || _changesList == 0)
        {
            return string.Empty;
        }

        int length = checked((int)NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxGetTextLength,
            unchecked((nuint)index),
            0));
        if (length <= 0)
        {
            return string.Empty;
        }

        char[] text = new char[length + 1];
        nint copied = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxGetText,
            unchecked((nuint)index),
            text);
        int copiedLength = copied == unchecked((nint)(-1))
            ? 0
            : Math.Min(length, checked((int)copied));
        return new string(text, 0, copiedLength);
    }

    private void UpdateEmptyChangesState()
    {
        bool showEmpty = ShouldShowEmptyChangesStateForTest(
            _repository?.Kind,
            _status?.Files.Count ?? -1);
        bool listChanged = SetControlVisible(_changesList, !showEmpty);
        bool titleChanged = SetControlVisible(_emptyChangesTitle, showEmpty);
        bool subtitleChanged = SetControlVisible(_emptyChangesSubtitle, showEmpty && _emptyCommitSubtitleFits);
        if (listChanged || titleChanged || subtitleChanged)
        {
            _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
        }
    }

    private static bool SetControlVisible(nint control, bool visible)
    {
        if (control == 0)
        {
            return false;
        }

        bool current = NativeMethods.IsWindowVisible(control);
        if (current == visible)
        {
            return false;
        }

        _ = NativeMethods.ShowWindow(control, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        return true;
    }

    internal static bool StatusFilesEquivalentForTest(
        IReadOnlyList<GitChangedFile>? previous,
        IReadOnlyList<GitChangedFile> current)
    {
        return StatusFilesEquivalent(previous, current);
    }

    internal static bool StatusSnapshotsEquivalentForTest(
        GitStatusSnapshot? previous,
        GitStatusSnapshot current)
    {
        return StatusSnapshotsEquivalent(previous, current);
    }

    internal static bool ShouldRefreshChromeForTest(
        GitStatusSnapshot? previous,
        GitStatusSnapshot current)
    {
        return ShouldRefreshChrome(previous, current);
    }

    private static bool ShouldRefreshChrome(
        GitStatusSnapshot? previous,
        GitStatusSnapshot current)
    {
        return !StatusSnapshotsEquivalent(previous, current);
    }

    private static bool StatusSnapshotsEquivalent(
        GitStatusSnapshot? previous,
        GitStatusSnapshot current)
    {
        return previous is not null
            && string.Equals(previous.CurrentBranch, current.CurrentBranch, StringComparison.Ordinal)
            && previous.IsDetached == current.IsDetached
            && string.Equals(previous.HeadCommit, current.HeadCommit, StringComparison.Ordinal)
            && StatusFilesEquivalent(previous.Files, current.Files);
    }

    internal static bool ShouldShowEmptyChangesStateForTest(GitRepositoryKind? kind, int fileCount)
    {
        return kind == GitRepositoryKind.WorkingTree && fileCount == 0;
    }

    private static bool StatusFilesEquivalent(
        IReadOnlyList<GitChangedFile>? previous,
        IReadOnlyList<GitChangedFile> current)
    {
        if (previous is null || previous.Count != current.Count)
        {
            return false;
        }

        for (int index = 0; index < previous.Count; index++)
        {
            GitChangedFile left = previous[index];
            GitChangedFile right = current[index];
            if (!left.RelativePath.Equals(right.RelativePath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    left.OriginalRelativePath,
                    right.OriginalRelativePath,
                    StringComparison.OrdinalIgnoreCase)
                || left.Group != right.Group
                || left.Kind != right.Kind
                || left.HasStagedChanges != right.HasStagedChanges
                || left.HasWorkingTreeChanges != right.HasWorkingTreeChanges)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool ShouldReloadActiveDiffForTest(
        IReadOnlyList<GitChangedFile>? previous,
        IReadOnlyList<GitChangedFile> current,
        string? selectedPath,
        IReadOnlyCollection<string> changedWorkspacePaths,
        bool force)
    {
        return ShouldReloadActiveDiff(previous, current, selectedPath, changedWorkspacePaths, force);
    }

    private static bool ShouldReloadActiveDiff(
        IReadOnlyList<GitChangedFile>? previous,
        IReadOnlyList<GitChangedFile> current,
        string? selectedPath,
        IReadOnlyCollection<string> changedWorkspacePaths,
        bool force)
    {
        if (force)
        {
            return true;
        }

        if (string.IsNullOrEmpty(selectedPath))
        {
            return false;
        }

        if (changedWorkspacePaths.Any(
                path => path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        GitChangedFile? previousFile = previous?.FirstOrDefault(
            file => file.RelativePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        GitChangedFile? currentFile = current.FirstOrDefault(
            file => file.RelativePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        return !ChangedFilesEquivalent(previousFile, currentFile);
    }

    private static bool ShouldShowSelectedDiffAfterRefresh(
        bool initialLoad,
        string? previousSelectedPath,
        string? currentSelectedPath,
        bool reloadActiveDiff)
    {
        if (reloadActiveDiff)
        {
            return true;
        }

        if (initialLoad)
        {
            return false;
        }

        // 列表更新后选中项被删除或被重新对齐时，需要同步清理或显示新的 Diff。
        return !string.Equals(
            previousSelectedPath,
            currentSelectedPath,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldShowSelectedDiffAfterRefreshForTest(
        bool initialLoad,
        string? previousSelectedPath,
        string? currentSelectedPath,
        bool reloadActiveDiff)
    {
        return ShouldShowSelectedDiffAfterRefresh(
            initialLoad,
            previousSelectedPath,
            currentSelectedPath,
            reloadActiveDiff);
    }

    private static bool ChangedFilesEquivalent(GitChangedFile? left, GitChangedFile? right)
    {
        return left is null && right is null
            || left is not null
                && right is not null
                && left.RelativePath.Equals(right.RelativePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    left.OriginalRelativePath,
                    right.OriginalRelativePath,
                    StringComparison.OrdinalIgnoreCase)
                && left.Group == right.Group
                && left.Kind == right.Kind
                && left.HasStagedChanges == right.HasStagedChanges
                && left.HasWorkingTreeChanges == right.HasWorkingTreeChanges;
    }

    private void AddGroup(
        GitChangeGroup group,
        string title,
        IReadOnlyList<GitChangedFile> files)
    {
        GitChangedFile[] groupFiles = files.Where(file => file.Group == group).ToArray();
        AddEntry(new(group, null, groupFiles.Length), UiText.GitGroup(title, groupFiles.Length));
        if (_collapsedGroups.Contains(group))
        {
            return;
        }
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

    private void HandleCommand(nuint wordParameter, nint source = 0)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (source == _commitEdit && notification == NativeMethods.EditNotificationChanged)
        {
            SetCommitError(string.Empty);
            return;
        }
        if (command == ChangesListIdentifier)
        {
            if (notification == NativeMethods.ListBoxNotificationSelectionChanged
                && !_updatingChangesList)
            {
                UpdateSelectedFilePathIntent();
                // 首次单击只改变选中项。已有 Diff 标签时才在后台或原位更新正文，
                // 不因普通列表选择抢占当前编辑区。
                if (_diffVisible)
                {
                    _ = ShowSelectedDiffAsync();
                }
            }
            else if (notification == NativeMethods.ListBoxNotificationDoubleClick)
            {
                int index = GetSelectedEntryIndex();
                if (index >= 0 && index < _entries.Count && _entries[index].File is null)
                {
                    ToggleGroupCollapsed(_entries[index].Group);
                }
                else
                {
                    _ = OpenSelectedDiffAsync();
                }
            }

            return;
        }

        // Scintilla 和输入框也会发送 WM_COMMAND 内容、焦点通知，不能当作按钮点击。
        if (notification != 0)
        {
            return;
        }

        switch (command)
        {
            case CommandRefresh:
            case CommandShowDiff:
            case CommandExpandAll:
            case CommandPreview:
            case CommandRollbackToolbar:
                _lastCommitToolbarCommandForTest = ExecuteCommitToolbarCommandAsync(
                    command,
                    requireRollbackConfirmation: !_skipRollbackConfirmationForTest);
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
            case CommandChoosePullMode:
                ShowPullModeMenu();
                break;
            case CommandPush:
                ShowPush();
                break;
            case CommandRemotes:
                ShowRemotes();
                break;
            case CommandAdvancedOperations:
                ShowAdvancedOperations();
                break;
            case CommandMoreActions:
                ShowOverviewActionsMenu();
                break;
            case CommandClosePanel:
                _closePanel();
                break;
            case CommandCancelOperation:
                _operationCancellation?.Cancel();
                break;
            case CommandUnified:
                ChangeDiffMode(sideBySide: false);
                break;
            case CommandSideBySide:
                ChangeDiffMode(sideBySide: true);
                break;
            case CommandIgnoreWhitespace:
                _ignoreWhitespace = !_ignoreWhitespace;
                _ = NativeMethods.InvalidateRectangle(_ignoreWhitespaceButton, 0, true);
                _ = ShowSelectedDiffAsync(activatePresentation: true);
                break;
            case CommandAmend:
                _lastAmendCommandForTest = ToggleAmendAsync();
                break;
            case CommandLastCommit:
                _openHistory();
                break;
            case CommandCommitSettings:
                _openSettings();
                break;
            case CommandPreviousChange:
                NavigateChange(-1);
                break;
            case CommandNextChange:
                NavigateChange(1);
                break;
            case CommandPreviousFile:
                NavigateFile(-1);
                break;
            case CommandNextFile:
                NavigateFile(1);
                break;
            case CommandDiffSearch:
                ShowFind();
                break;
            case CommandDiffSettings:
                ShowDiffSettingsMenu();
                break;
            case CommandModifiedPreview:
                OpenModifiedPreview();
                break;
            case CommandRollback:
                _ = RollbackSelectedAsync();
                break;
            case CommandCommit:
                _lastCommitCommandForTest = CommitAsync(pushAfterCommit: false);
                break;
            case CommandCommitAndPush:
                _lastCommitCommandForTest = CommitAsync(pushAfterCommit: true);
                break;
        }
    }

    private Task ExecuteCommitToolbarCommandAsync(int command, bool requireRollbackConfirmation)
    {
        switch (command)
        {
            case CommandRefresh:
                RequestRefresh();
                return Task.CompletedTask;
            case CommandShowDiff:
                return ShowSelectedDiffAsync(activatePresentation: true);
            case CommandExpandAll:
                _collapsedGroups.Clear();
                PopulateChanges(_status?.Files ?? []);
                return Task.CompletedTask;
            case CommandPreview:
                OpenModifiedPreview();
                return Task.CompletedTask;
            case CommandRollbackToolbar:
                return RollbackSelectedAsync(requireConfirmation: requireRollbackConfirmation);
            default:
                return Task.CompletedTask;
        }
    }

    private void ShowPullModeMenu()
    {
        if (!NativeMethods.GetWindowRectangle(_pullModeButton, out NativeMethods.Rectangle button))
        {
            return;
        }

        _contextMenu?.Dispose();
        List<NativeContextMenuItem?> items = [];
        for (int index = 0; index < PullModeLabels.Length; index++)
        {
            int selectedIndex = index;
            items.Add(new(
                PullModeLabels[index],
                NativeContextMenuIcon.Refresh,
                () =>
                {
                    _pullModeIndex = selectedIndex;
                    _ = NativeMethods.SetWindowText(_pullModeButton, PullModeLabels[selectedIndex]);
                    _ = NativeMethods.InvalidateRectangle(_pullModeButton, 0, true);
                },
                Checked: index == _pullModeIndex));
        }

        _contextMenu = NativeContextMenu.Show(
            Handle,
            button.Left,
            button.Bottom,
            items,
            NativeTheme.IsDark(_settings.Theme));
    }

    private void ShowOverviewActionsMenu()
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
                new(UiText.Fetch, NativeContextMenuIcon.Refresh, () => HandleCommand(CommandFetch)),
                new(UiText.Pull, NativeContextMenuIcon.Open, () => HandleCommand(CommandPull)),
                new(UiText.Push, NativeContextMenuIcon.Open, () => HandleCommand(CommandPush)),
                null,
                new(UiText.Remotes, NativeContextMenuIcon.Open, () => HandleCommand(CommandRemotes)),
                new(UiText.AdvancedGitOperations, NativeContextMenuIcon.Open, () => HandleCommand(CommandAdvancedOperations)),
            ],
            NativeTheme.IsDark(_settings.Theme));
    }

    private void ShowDiffSettingsMenu()
    {
        if (!NativeMethods.GetWindowRectangle(_diffSettingsButton, out NativeMethods.Rectangle button))
        {
            return;
        }

        _contextMenu?.Dispose();
        _contextMenu = NativeContextMenu.Show(
            _diffHandle,
            button.Right - NativeTheme.Scale(180),
            button.Bottom,
            [
                new(
                    UiText.IgnoreWhitespace,
                    NativeContextMenuIcon.Whitespace,
                    () => HandleCommand(CommandIgnoreWhitespace),
                    Checked: _ignoreWhitespace),
                new(
                    UiText.ModifiedPreview,
                    NativeContextMenuIcon.Open,
                    () => HandleCommand(CommandModifiedPreview),
                    Enabled: _activeDiff is not null),
            ],
            NativeTheme.IsDark(_settings.Theme));
    }

    private async Task InitializeRepositoryAsync()
    {
        if (_repositoryService is null || _repository is null || _operationRunning)
        {
            return;
        }

        if (!ConfirmRepositoryInitialization())
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

    private bool ConfirmRepositoryInitialization()
    {
        if (_repository?.Kind != GitRepositoryKind.PlainDirectory)
        {
            return false;
        }

        nint owner = NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot);
        return NativeActionConfirmationDialog.Show(
            owner,
            _settings,
            UiText.InitializeGitRepositoryTitle,
            $"{_workspaceRoot} 不是 Git 仓库",
            UiText.InitializeGitRepositoryDetail,
            UiText.InitializeRepository,
            cancelLabel: UiText.ContinueBrowse);
    }

    private async Task ShowSelectedDiffAsync(
        bool forceReload = false,
        bool activatePresentation = false,
        int entryDirection = 0)
    {
        int selectedIndex = _selectedFilePathIntent is { Length: > 0 } requestedPath
            ? FindFileEntryIndex(requestedPath)
            : GetSelectedEntryIndex();
        if (selectedIndex < 0 && _selectedFilePathIntent is not null)
        {
            _selectedFilePathIntent = null;
            selectedIndex = GetSelectedEntryIndex();
        }
        if (selectedIndex < 0 || selectedIndex >= _entries.Count || _entries[selectedIndex].File is null)
        {
            InvalidateDiffRequest();
            _activeChangedFile = null;
            _activeDiff = null;
            _activeDiffFingerprint = null;
            SetDiffHeaderText(UiText.SelectGitFile);
            UpdateDiffToolbarSummary();
            _ = NativeMethods.ShowWindow(_modifiedPreviewButton, NativeMethods.ShowHide);
            SetDiffVisible(false);
            UpdateCommitToolbarActionsEnabled();
            return;
        }

        // 异步状态刷新可能重建列表；以路径意图重新对齐原生选中项，避免视觉高亮落到邻近文件。
        if (GetSelectedEntryIndex() != selectedIndex)
        {
            _updatingChangesList = true;
            try
            {
                _ = NativeMethods.SendMessage(
                    _changesList,
                    NativeMethods.ListBoxSetCurrentSelection,
                    unchecked((nuint)selectedIndex),
                    0);
            }
            finally
            {
                _updatingChangesList = false;
            }
        }

        GitChangedFile file = _entries[selectedIndex].File!;
        if (!activatePresentation && !_diffVisible)
        {
            // 没有已打开的 Diff 时，列表选择和导航只保留选中状态，不能在后台偷偷创建正文。
            return;
        }

        int statusFileIndex = GetStatusFileIndex(file.RelativePath);
        UpdateDiffToolbarSummary(statusFileIndex < 0 ? null : statusFileIndex + 1);
        UpdateCommitToolbarActionsEnabled();
        string fingerprint = GetDiffInputFingerprint(file);
        bool sameActiveRequest = IsSameDiffRequest(
            _activeDiff?.RelativePath,
            _activeDiffIgnoreWhitespace,
            file.RelativePath,
            _ignoreWhitespace)
            && string.Equals(_activeDiffFingerprint, fingerprint, StringComparison.Ordinal);
        bool sameLoadingRequest = IsSameDiffRequest(
            _loadingDiffPath,
            _loadingDiffIgnoreWhitespace,
            file.RelativePath,
            _ignoreWhitespace)
            && string.Equals(_loadingDiffFingerprint, fingerprint, StringComparison.Ordinal);
        bool presentationChanged = !_diffVisible
            || !string.Equals(_publishedDiffPath, file.RelativePath, StringComparison.OrdinalIgnoreCase);
        _activeChangedFile = file;
        if ((presentationChanged && (activatePresentation || _diffVisible))
            || activatePresentation)
        {
            SetDiffVisible(true, activatePresentation);
            SetDiffHeaderText(file.RelativePath);
            bool supportsModifiedPreview = SupportsModifiedPreview(file);
            _ = NativeMethods.ShowWindow(
                _modifiedPreviewButton,
                supportsModifiedPreview ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
        // 强制审计/刷新必须能够越过正在加载的同一请求；普通重复选择仍复用现有请求。
        bool reuseActiveDiff = !forceReload && sameActiveRequest && _activeDiff is not null;
        if (!forceReload && sameLoadingRequest)
        {
            return;
        }
        if (reuseActiveDiff && _loadingDiffPath is null && _activeDiffSideBySide == _sideBySide)
        {
            if (_activeDiff!.Status != GitDiffContentStatus.Ready
                || string.IsNullOrEmpty(_activeDiff.UnifiedPatch))
            {
                // 隐藏正文时可能撤销了提示层，返回摘要标签时需要恢复说明。
                _ = await RenderActiveDiffAsync(++_renderVersion);
            }
            return;
        }
        if (!presentationChanged)
        {
            // 同一路径的重新加载只修正可能遗留的加载标题，不能重绘标签或重设子窗口显隐。
            SetDiffHeaderText(file.RelativePath);
        }
        if (_diffService is null || _repository is null)
        {
            return;
        }

        _diffCancellation?.Cancel();
        _diffCancellation?.Dispose();
        _diffCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        DismissDiffBoundaryHint();
        CancelDiffLoadingIndicator();
        int version = ++_diffVersion;
        // 新选择立即使旧正文渲染失效，不能等待新的 Git 查询返回后才撤销旧渲染。
        _renderVersion++;
        nint navigationFocus = NativeMethods.GetFocus();
        _loadingDiffPath = file.RelativePath;
        _loadingDiffIgnoreWhitespace = _ignoreWhitespace;
        _loadingDiffFingerprint = fingerprint;
        UpdateDiffToolbarSummary();
        _diffLoadingIndicatorCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _ = ShowDiffLoadingIndicatorAsync(
            version,
            file.RelativePath,
            _diffLoadingIndicatorCancellation.Token);
        if (_holdDiffLoadingForTest)
        {
            _diffRequestCount++;
            return;
        }
        try
        {
            if (!reuseActiveDiff)
            {
                _diffRequestCount++;
                int testDelay = Interlocked.Exchange(ref _delayNextDiffForTestMilliseconds, 0);
                if (testDelay > 0)
                {
                    await Task.Delay(testDelay, _diffCancellation.Token);
                }

                GitDiffResult result = await _diffService.CreateAsync(
                    _repository,
                    file,
                    new(_ignoreWhitespace),
                    _diffCancellation.Token);

                if (!CanApplyDiffResult(version, _diffVersion, _disposed))
                {
                    return;
                }

                if (!result.IsSuccess || result.Document is null)
                {
                    ShowDiffNotice(result.ErrorMessage ?? UiText.GenerateDiffFailed);
                    return;
                }

                _activeDiff = result.Document;
                _activeDiffIgnoreWhitespace = _ignoreWhitespace;
            }

            _activeDiffFingerprint = null;
            bool rendered;
            do
            {
                rendered = await RenderActiveDiffAsync(++_renderVersion);
            }
            while (!rendered && CanApplyDiffResult(version, _diffVersion, _disposed));
            if (rendered && CanApplyDiffResult(version, _diffVersion, _disposed))
            {
                _activeDiffFingerprint = fingerprint;
                _activeDiffSideBySide = _sideBySide;
                if (entryDirection != 0 && _changeLines.Count > 0)
                {
                    MoveToChange(entryDirection < 0 ? _changeLines.Count - 1 : 0);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 请求被替换或窗口关闭时，旧任务不再修改当前显示。
        }
        finally
        {
            if (CanApplyDiffResult(version, _diffVersion, _disposed))
            {
                _loadingDiffPath = null;
                _loadingDiffFingerprint = null;
                _holdDiffLoadingForTest = false;
                StopDiffLoadingIndicator();
                SetDiffHeaderText(file.RelativePath);
                UpdateDiffToolbarSummary();
                if ((navigationFocus == _previousChangeButton || navigationFocus == _nextChangeButton)
                    && NativeMethods.GetFocus() == 0
                    && NativeMethods.IsWindowVisible(_diffHandle)
                    && NativeMethods.IsWindowEnabled(navigationFocus)
                    && NativeMethods.GetForegroundWindow() == NativeMethods.GetAncestor(_diffHandle, NativeMethods.GetAncestorRoot))
                {
                    // 禁用有焦点的按钮会清空 Win32 焦点；仅在用户没有转到其他控件或应用时恢复。
                    _ = NativeMethods.SetFocus(navigationFocus);
                }
            }
        }
    }

    private async Task ShowDiffLoadingIndicatorAsync(
        int version,
        string relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DiffLoadingThresholdMilliseconds, cancellationToken);
            if (_disposed || version != _diffVersion || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (_activeDiff is null || _holdDiffLoadingForTest)
            {
                ShowDiffLoadingNotice(relativePath);
            }
            else
            {
                // 有旧结果时保留原位内容，加载反馈只属于当前 Diff，不接管全局状态栏。
                SetDiffHeaderText($"{relativePath} · {UiText.GeneratingDiff}");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelDiffLoadingIndicator()
    {
        StopDiffLoadingIndicator();
        HideDiffLoadingNotice();
    }

    private void StopDiffLoadingIndicator()
    {
        _diffLoadingIndicatorCancellation?.Cancel();
        _diffLoadingIndicatorCancellation?.Dispose();
        _diffLoadingIndicatorCancellation = null;
    }

    private void ShowDiffLoadingNotice(string relativePath)
    {
        if (_diffLoadingNotice == 0)
        {
            return;
        }

        string fileName = Path.GetFileName(relativePath);
        _ = NativeMethods.SetWindowText(
            _diffLoadingNotice,
            $"{UiText.LoadingDiff} {fileName} 的差异");
        _unifiedRenderedText = string.Empty;
        _oldRenderedText = string.Empty;
        _diffGutterRenderedText = string.Empty;
        _newRenderedText = string.Empty;
        SetChangeLines([]);
        _unifiedDiff?.SetVisible(false);
        _oldDiff?.SetVisible(false);
        _diffGutter?.SetVisible(false);
        _newDiff?.SetVisible(false);
        _ = NativeMethods.ShowWindow(_diffLoadingNotice, NativeMethods.ShowNormal);
        // 加载状态与 Diff 正文共用既有边界，只切换局部控件，不能重排 Commit 面板。
    }

    private void HideDiffLoadingNotice()
    {
        if (_diffLoadingNotice != 0)
        {
            _ = NativeMethods.ShowWindow(_diffLoadingNotice, NativeMethods.ShowHide);
        }
    }

    private void InvalidateDiffRequest()
    {
        DismissDiffBoundaryHint();
        _diffCancellation?.Cancel();
        _diffCancellation?.Dispose();
        _diffCancellation = null;
        CancelDiffLoadingIndicator();
        _diffVersion++;
        _renderVersion++;
        _loadingDiffPath = null;
        _loadingDiffFingerprint = null;
    }

    internal static int DiffLoadingThresholdMillisecondsForTest => DiffLoadingThresholdMilliseconds;

    internal static bool CanApplyDiffResultForTest(
        int resultVersion,
        int currentVersion,
        bool disposed)
    {
        return CanApplyDiffResult(resultVersion, currentVersion, disposed);
    }

    private static bool CanApplyDiffResult(int resultVersion, int currentVersion, bool disposed)
    {
        return !disposed && resultVersion == currentVersion;
    }

    internal static bool IsSameDiffRequestForTest(
        string? currentPath,
        bool currentIgnoreWhitespace,
        string nextPath,
        bool nextIgnoreWhitespace)
    {
        return IsSameDiffRequest(
            currentPath,
            currentIgnoreWhitespace,
            nextPath,
            nextIgnoreWhitespace);
    }

    internal static int FindAdjacentFileIndexForTest(
        int count,
        int selectedIndex,
        int direction,
        Func<int, bool> isFile)
    {
        ArgumentNullException.ThrowIfNull(isFile);
        if (selectedIndex < 0 || selectedIndex >= count || direction == 0)
        {
            return -1;
        }

        int step = Math.Sign(direction);
        for (int index = selectedIndex + step; index >= 0 && index < count; index += step)
        {
            if (isFile(index))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSameDiffRequest(
        string? currentPath,
        bool currentIgnoreWhitespace,
        string nextPath,
        bool nextIgnoreWhitespace)
    {
        return currentPath?.Equals(nextPath, StringComparison.OrdinalIgnoreCase) == true
            && currentIgnoreWhitespace == nextIgnoreWhitespace;
    }

    private void ChangeDiffMode(bool sideBySide)
    {
        if (_sideBySide == sideBySide)
        {
            return;
        }

        _sideBySide = sideBySide;
        DismissDiffBoundaryHint();
        InvalidateDiffModeButtons();
        // 尚在查询或排版的请求会按最后选择的模式完成；不并发渲染上一个文件。
        if (_loadingDiffPath is null)
        {
            _ = ShowSelectedDiffAsync();
        }
    }

    private async Task<bool> RenderActiveDiffAsync(int renderVersion)
    {
        GitDiffDocument? document = _activeDiff;
        if (document is null)
        {
            ShowDiffNotice(UiText.SelectGitFile);
            return true;
        }

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
                clearActiveDiff: false);
            return true;
        }

        string patch = document.UnifiedPatch ?? string.Empty;
        if (patch.Length == 0)
        {
            ShowDiffNotice(UiText.NoTextDiff, clearActiveDiff: false);
            return true;
        }

        bool renderSideBySide = _sideBySide;
        CancellationToken cancellationToken = _diffCancellation?.Token ?? _lifetimeCancellation.Token;
        try
        {
            if (DiffRenderBarrierForTest is { } renderBarrier)
            {
                DiffRenderBarrierForTest = null;
                await renderBarrier.WaitAsync(cancellationToken);
            }

            if (!renderSideBySide)
            {
                var rendered = await Task.Run(
                    () => BuildUnified(GitUnifiedDiffParser.Parse(patch)),
                    cancellationToken);
                if (!CanApplyRender(renderVersion, document, renderSideBySide))
                {
                    return false;
                }

                _unifiedRenderedText = rendered.Text;
                _oldRenderedText = string.Empty;
                _diffGutterRenderedText = string.Empty;
                _newRenderedText = string.Empty;
                SetChangeLines(rendered.ChangedLines);
                _suppressDiffNotifications = true;
                try
                {
                    _unifiedDiff!.SetTextContent(rendered.Text);
                }
                finally
                {
                    _suppressDiffNotifications = false;
                }
                bool IsCurrent() => CanApplyRender(renderVersion, document, renderSideBySide);
                var removed = NativeTheme.DiffLineBackground(added: false, NativeTheme.IsDark(_settings.Theme));
                var added = NativeTheme.DiffLineBackground(added: true, NativeTheme.IsDark(_settings.Theme));
                if (!await _unifiedDiff.SetLineBackgroundsAsync(0, rendered.Text, rendered.RemovedHighlights,
                        removed.Red, removed.Green, removed.Blue, IsCurrent, cancellationToken)
                    || !await _unifiedDiff.SetLineBackgroundsAsync(1, rendered.Text, rendered.AddedHighlights,
                        added.Red, added.Green, added.Blue, IsCurrent, cancellationToken)
                    || !await _unifiedDiff.SetDiffStylesAsync(rendered.Text, rendered.LineNumberStyles,
                        rendered.RemovedMarkerStyles, rendered.AddedMarkerStyles, IsCurrent, cancellationToken)
                    || !await _unifiedDiff.SetHighlightsAsync(0, rendered.Text, rendered.RemovedHighlights,
                        219, 88, 96, IsCurrent, cancellationToken)
                    || !await _unifiedDiff.SetHighlightsAsync(1, rendered.Text, rendered.AddedHighlights,
                        76, 175, 80, IsCurrent, cancellationToken))
                {
                    return false;
                }
                CancelDiffLoadingIndicator();
                NativeTheme.ApplyDiffLineColors(_unifiedDiff, _oldDiff, _newDiff, NativeTheme.IsDark(_settings.Theme));
                ApplyDiffHeaderMode(renderSideBySide);
                _oldDiff!.SetVisible(false);
                _diffGutter!.SetVisible(false);
                _newDiff!.SetVisible(false);
                _unifiedDiff!.SetVisible(true);
                return true;
            }

            var sideRendered = await Task.Run(
                () =>
                {
                    IReadOnlyList<GitSideBySideRow> rows = GitUnifiedDiffParser.ToSideBySide(
                        GitUnifiedDiffParser.Parse(patch));
                    GitSideBySideRow[] visibleRows = rows.Where(IsVisibleDiffRow).ToArray();
                    int lineNumberWidth = CalculateLineNumberColumnWidth(
                        visibleRows.SelectMany(static row => new[] { row.OldLineNumber, row.NewLineNumber }));
                    return (
                        Old: BuildSide(visibleRows, oldSide: true),
                        Gutter: BuildGutter(visibleRows, lineNumberWidth),
                        New: BuildSide(visibleRows, oldSide: false),
                        ChangedLines: GitDiffNavigation.GetChangeStartLines(rows.Select(row => row.Kind)));
                },
                cancellationToken);
            if (!CanApplyRender(renderVersion, document, renderSideBySide))
            {
                return false;
            }

            _unifiedRenderedText = string.Empty;
            _oldRenderedText = sideRendered.Old.Text;
            _diffGutterRenderedText = sideRendered.Gutter.Text;
            _newRenderedText = sideRendered.New.Text;
            SetChangeLines(sideRendered.ChangedLines);
            _suppressDiffNotifications = true;
            try
            {
                _oldDiff!.SetTextContent(sideRendered.Old.Text);
                _diffGutter!.SetTextContent(sideRendered.Gutter.Text);
                _newDiff!.SetTextContent(sideRendered.New.Text);
            }
            finally
            {
                _suppressDiffNotifications = false;
            }
            bool IsCurrentSide() => CanApplyRender(renderVersion, document, renderSideBySide);
            bool dark = NativeTheme.IsDark(_settings.Theme);
            (int removedRed, int removedGreen, int removedBlue) = NativeTheme.DiffLineBackground(added: false, dark);
            (int addedRed, int addedGreen, int addedBlue) = NativeTheme.DiffLineBackground(added: true, dark);
            if (!await _diffGutter.SetDiffStylesAsync(sideRendered.Gutter.Text, sideRendered.Gutter.LineNumberStyles,
                    [], [], IsCurrentSide, cancellationToken)
                || !await _oldDiff.SetLineBackgroundsAsync(0, sideRendered.Old.Text, sideRendered.Old.LineBackgrounds,
                    removedRed, removedGreen, removedBlue, IsCurrentSide, cancellationToken)
                || !await _newDiff.SetLineBackgroundsAsync(0, sideRendered.New.Text, sideRendered.New.LineBackgrounds,
                    addedRed, addedGreen, addedBlue, IsCurrentSide, cancellationToken)
                || !await _oldDiff.SetHighlightsAsync(0, sideRendered.Old.Text, sideRendered.Old.Highlights,
                    219, 88, 96, IsCurrentSide, cancellationToken)
                || !await _newDiff.SetHighlightsAsync(0, sideRendered.New.Text, sideRendered.New.Highlights,
                    76, 175, 80, IsCurrentSide, cancellationToken))
            {
                return false;
            }
            CancelDiffLoadingIndicator();
            NativeTheme.ApplyDiffLineColors(_unifiedDiff, _oldDiff, _newDiff, NativeTheme.IsDark(_settings.Theme));
            bool gutterChanged = UpdateDiffGutterWidth();
            ApplyDiffHeaderMode(renderSideBySide, gutterChanged);
            _unifiedDiff!.SetVisible(false);
            _oldDiff!.SetVisible(true);
            _diffGutter!.SetVisible(true);
            _newDiff!.SetVisible(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private bool CanApplyRender(int renderVersion, GitDiffDocument document, bool renderedSideBySide)
    {
        return !_disposed
            && renderVersion == _renderVersion
            && ReferenceEquals(_activeDiff, document)
            && _sideBySide == renderedSideBySide;
    }

    private void ReleaseDiffContent()
    {
        _activeDiff = null;
        _activeDiffFingerprint = null;
        _unifiedRenderedText = string.Empty;
        _oldRenderedText = string.Empty;
        _newRenderedText = string.Empty;
        _diffGutterRenderedText = string.Empty;
        _changeLines.Clear();
        if (_disposed)
        {
            return;
        }

        _suppressDiffNotifications = true;
        try
        {
            _unifiedDiff?.SetTextContent(string.Empty);
            _oldDiff?.SetTextContent(string.Empty);
            _newDiff?.SetTextContent(string.Empty);
            _diffGutter?.SetTextContent(string.Empty);
        }
        finally
        {
            _suppressDiffNotifications = false;
        }
    }

    private static (
        string Text,
        List<(int Start, int Length)> RemovedHighlights,
        List<(int Start, int Length)> AddedHighlights,
        List<(int Start, int Length)> LineNumberStyles,
        List<(int Start, int Length)> RemovedMarkerStyles,
        List<(int Start, int Length)> AddedMarkerStyles,
        List<int> ChangedLines) BuildUnified(IReadOnlyList<GitDiffLine> lines)
    {
        GitDiffLine[] visibleLines = lines.Where(IsVisibleDiffLine).ToArray();
        int lineNumberWidth = CalculateLineNumberColumnWidth(
            visibleLines.SelectMany(static line => new[] { line.OldLineNumber, line.NewLineNumber }));
        StringBuilder builder = new();
        List<(int Start, int Length)> removed = [];
        List<(int Start, int Length)> added = [];
        List<(int Start, int Length)> lineNumbers = [];
        List<(int Start, int Length)> removedMarkers = [];
        List<(int Start, int Length)> addedMarkers = [];
        List<int> changedLines = GitDiffNavigation.GetChangeStartLines(lines.Select(line => line.Kind));
        for (int index = 0; index < visibleLines.Length; index++)
        {
            GitDiffLine line = visibleLines[index];
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
            builder.Append(oldNumber.PadLeft(lineNumberWidth))
                .Append(' ')
                .Append(newNumber.PadLeft(lineNumberWidth))
                .Append(' ')
                .Append(marker)
                .Append(' ')
                .Append(line.Text);
            int markerPosition = lineStart + (lineNumberWidth * 2) + 2;
            lineNumbers.Add((lineStart, (lineNumberWidth * 2) + 2));

            int lineLength = builder.Length - lineStart;
            if (line.Kind == GitDiffLineKind.Removed)
            {
                removed.Add((lineStart, lineLength));
                removedMarkers.Add((markerPosition, 1));
            }
            else if (line.Kind == GitDiffLineKind.Added)
            {
                added.Add((lineStart, lineLength));
                addedMarkers.Add((markerPosition, 1));
            }

            builder.AppendLine();
        }

        return (builder.ToString(), removed, added, lineNumbers, removedMarkers, addedMarkers, changedLines);
    }

    private static (
        string Text,
        List<(int Start, int Length)> Highlights,
        List<(int Start, int Length)> LineBackgrounds) BuildSide(
        IReadOnlyList<GitSideBySideRow> rows,
        bool oldSide)
    {
        StringBuilder builder = new();
        List<(int Start, int Length)> highlights = [];
        List<(int Start, int Length)> lineBackgrounds = [];
        foreach (GitSideBySideRow row in rows)
        {
            string? text = oldSide ? row.OldText : row.NewText;
            IReadOnlyList<GitTextSpan> changes = oldSide ? row.OldChanges : row.NewChanges;
            int lineStart = builder.Length;
            if (text is not null)
            {
                builder.Append(text);
                bool changedSide = oldSide
                    ? row.Kind is GitDiffLineKind.Removed or GitDiffLineKind.Modified
                    : row.Kind is GitDiffLineKind.Added or GitDiffLineKind.Modified;
                if (changedSide)
                {
                    // 至少覆盖一个字符，确保空行变更也能映射到当前 Scintilla 行。
                    lineBackgrounds.Add((lineStart, Math.Max(1, text.Length)));
                }

                foreach (GitTextSpan span in changes)
                {
                    highlights.Add((lineStart + span.Start, span.Length));
                }
            }

            builder.AppendLine();
        }

        return (builder.ToString(), highlights, lineBackgrounds);
    }

    private static (
        string Text,
        List<(int Start, int Length)> LineNumberStyles) BuildGutter(
        IReadOnlyList<GitSideBySideRow> rows,
        int lineNumberWidth)
    {
        StringBuilder builder = new();
        List<(int Start, int Length)> lineNumbers = [];
        foreach (GitSideBySideRow row in rows)
        {
            int rowStart = builder.Length;
            string oldNumber = row.OldLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            string newNumber = row.NewLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            builder.Append(oldNumber.PadLeft(lineNumberWidth))
                .Append("    ")
                .Append(newNumber.PadLeft(lineNumberWidth))
                .AppendLine();
            lineNumbers.Add((rowStart, builder.Length - rowStart - NewLineLength));
        }

        return (builder.ToString(), lineNumbers);
    }

    private static int NewLineLength => Environment.NewLine.Length;

    internal static string BuildUnifiedTextForTest(IReadOnlyList<GitDiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return BuildUnified(lines).Text;
    }

    internal static int CalculateLineNumberColumnWidthForTest(IReadOnlyList<GitDiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return CalculateLineNumberColumnWidth(
            lines.Where(IsVisibleDiffLine)
                .SelectMany(static line => new[] { line.OldLineNumber, line.NewLineNumber }));
    }

    internal static (string OldText, string GutterText, string NewText) BuildSideBySideTextForTest(string patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        GitSideBySideRow[] rows = GitUnifiedDiffParser.ToSideBySide(GitUnifiedDiffParser.Parse(patch))
            .Where(IsVisibleDiffRow)
            .ToArray();
        int lineNumberWidth = CalculateLineNumberColumnWidth(
            rows.SelectMany(static row => new[] { row.OldLineNumber, row.NewLineNumber }));
        return (
            BuildSide(rows, oldSide: true).Text,
            BuildGutter(rows, lineNumberWidth).Text,
            BuildSide(rows, oldSide: false).Text);
    }

    internal static (
        IReadOnlyList<(int Start, int Length)> OldLineBackgrounds,
        IReadOnlyList<(int Start, int Length)> NewLineBackgrounds) BuildSideBySideLineBackgroundsForTest(
        string patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        GitSideBySideRow[] rows = GitUnifiedDiffParser.ToSideBySide(GitUnifiedDiffParser.Parse(patch))
            .Where(IsVisibleDiffRow)
            .ToArray();
        return (
            BuildSide(rows, oldSide: true).LineBackgrounds,
            BuildSide(rows, oldSide: false).LineBackgrounds);
    }

    internal static (
        IReadOnlyList<(int Start, int Length)> LineNumbers,
        IReadOnlyList<(int Start, int Length)> RemovedMarkers,
        IReadOnlyList<(int Start, int Length)> AddedMarkers) BuildUnifiedStyleRangesForTest(
            IReadOnlyList<GitDiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var rendered = BuildUnified(lines);
        return (rendered.LineNumberStyles, rendered.RemovedMarkerStyles, rendered.AddedMarkerStyles);
    }

    private static bool IsVisibleDiffLine(GitDiffLine line)
    {
        return line.Kind is GitDiffLineKind.Context
            or GitDiffLineKind.Removed
            or GitDiffLineKind.Added
            or GitDiffLineKind.Modified;
    }

    private static bool IsVisibleDiffRow(GitSideBySideRow row)
    {
        return row.Kind is GitDiffLineKind.Context
            or GitDiffLineKind.Removed
            or GitDiffLineKind.Added
            or GitDiffLineKind.Modified;
    }

    private static int CalculateLineNumberColumnWidth(IEnumerable<int?> lineNumbers)
    {
        int largest = lineNumbers.Where(static number => number.HasValue)
            .Select(static number => number!.Value)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(3, largest.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
    }

    private void ShowDiffNotice(
        string notice,
        bool clearActiveDiff = true)
    {
        // 最终摘要与加载提示复用控件；停止计时后展示摘要，不能在请求收尾时再隐藏它。
        StopDiffLoadingIndicator();
        ApplyDiffHeaderMode(_sideBySide);
        if (clearActiveDiff)
        {
            _activeDiff = null;
            _activeDiffFingerprint = null;
            if (_activeChangedFile is null)
            {
                SetDiffHeaderText(UiText.SelectGitFile);
            }
        }

        _renderVersion++;

        _unifiedRenderedText = notice;
        _oldRenderedText = string.Empty;
        _diffGutterRenderedText = string.Empty;
        _newRenderedText = string.Empty;
        SetChangeLines([]);

        _oldDiff?.SetVisible(false);
        _diffGutter?.SetVisible(false);
        _newDiff?.SetVisible(false);
        _unifiedDiff?.SetVisible(false);
        if (_diffLoadingNotice != 0)
        {
            _ = NativeMethods.SetWindowText(_diffLoadingNotice, notice);
            _ = NativeMethods.ShowWindow(_diffLoadingNotice, NativeMethods.ShowNormal);
        }
        // 空态、错误态和操作结果使用轻量提示层，不重写 Scintilla 正文并触发同步通知。
    }

    private void SetDiffVisible(bool visible, bool activatePresentation = true)
    {
        if (!visible)
        {
            DismissDiffBoundaryHint();
        }
        string? relativePath = visible ? _activeChangedFile?.RelativePath : null;
        if (_diffVisible == visible)
        {
            if (visible
                && (activatePresentation
                    || !string.Equals(
                        _publishedDiffPath,
                        relativePath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                PublishDiffPresentation(true, relativePath, activatePresentation);
            }
            return;
        }

        _diffVisible = visible;
        PublishDiffPresentation(visible, relativePath, activatePresentation);
        _ = NativeMethods.ShowWindow(_diffHandle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        if (visible && !_diffShownOnce)
        {
            _diffShownOnce = true;
            Layout(force: true);
        }
        _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
        _ = NativeMethods.InvalidateRectangle(_diffHandle, 0, true);
    }

    private void PublishDiffPresentation(
        bool visible,
        string? relativePath,
        bool activatePresentation = true)
    {
        _publishedDiffPath = visible ? relativePath : null;
        _diffPresentationNotificationCount++;
        _setDiffVisible(visible, relativePath, activatePresentation);
    }

    private void ToggleSelectedEntry()
    {
        int index = GetSelectedEntryIndex();
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        ToggleEntryAt(index);
    }

    private void ToggleEntryAt(int index)
    {
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
        // 提交复选不属于列表选择；只重绘文件和所属组的复选状态，保持选择意图与视口。
        InvalidateChangeEntry(index);
        InvalidateChangeEntry(_entries.FindIndex(item => item.File is null && item.Group == entry.Group));
        UpdateCommitActionsEnabled();
    }

    private void InvalidateChangeEntry(int index)
    {
        if (TryGetChangesListItemRectangle(index, out NativeMethods.Rectangle rectangle))
        {
            _ = NativeMethods.InvalidateRectangle(_changesList, ref rectangle, false);
        }
    }

    private int FindFileEntryIndex(string relativePath)
    {
        return _entries.FindIndex(
            entry => entry.File?.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true);
    }

    private bool TryGetChangesListItemRectangle(int index, out NativeMethods.Rectangle rectangle)
    {
        rectangle = default;
        return _changesList != 0
            && index >= 0
            && NativeMethods.SendMessage(
                _changesList,
                NativeMethods.ListBoxGetItemRectangle,
                unchecked((nuint)index),
                ref rectangle) != unchecked((nint)(-1));
    }

    private static nint PackPoint(int x, int y)
    {
        return unchecked((nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF)));
    }

    private Task OpenSelectedDiffAsync()
    {
        return ShowSelectedDiffAsync(activatePresentation: true);
    }

    private async Task OpenContextDiffAsync(GitChangedFile file)
    {
        int index = FindFileEntryIndex(file.RelativePath);
        if (index < 0)
        {
            return;
        }

        // 菜单打开时不改变选择；只有用户明确执行“显示 Diff”才切换到菜单目标。
        _selectedFilePathIntent = file.RelativePath;
        _updatingChangesList = true;
        try
        {
            _ = NativeMethods.SendMessage(
                _changesList,
                NativeMethods.ListBoxSetCurrentSelection,
                unchecked((nuint)index),
                0);
        }
        finally
        {
            _updatingChangesList = false;
        }

        UpdateCommitToolbarActionsEnabled();
        await ShowSelectedDiffAsync(activatePresentation: true);
    }

    private void OpenWorkspaceFile(GitChangedFile file)
    {
        if (_repository?.RepositoryRoot is null)
        {
            return;
        }

        string path = Path.GetFullPath(Path.Combine(
            _repository.RepositoryRoot,
            file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(path))
        {
            _openModifiedPreview(path);
        }
    }

    private void ToggleGroup(GitChangeGroup group)
    {
        GitChangedFile[] files = (_status?.Files ?? [])
            .Where(file => file.Group == group)
            .ToArray();
        bool select = files.Any(file => !_selection.IsSelected(file.RelativePath));
        _selection.SetGroupSelected(files, select);
        // 整组复选会改变多行，合并成一次视口重绘，不改列表内容或滚动位置。
        _ = NativeMethods.InvalidateRectangle(_changesList, 0, false);
        UpdateCommitActionsEnabled();
    }

    private void ToggleGroupCollapsed(GitChangeGroup group)
    {
        if (!_collapsedGroups.Add(group))
        {
            _collapsedGroups.Remove(group);
        }
        PopulateChanges(_status?.Files ?? []);
    }

    private void RestoreListSelection(
        string? relativePath,
        int fallbackIndex = -1,
        int topIndex = -1,
        string? selectedGroup = null,
        string? topAnchor = null)
    {
        int index = relativePath is null
            ? -1
            : _entries.FindIndex(
                entry => entry.File?.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true);
        if (index < 0 && selectedGroup is not null && _entries.Count > 0)
        {
            int nearestIndex = -1;
            int nearestDistance = int.MaxValue;
            for (int candidate = 0; candidate < _entries.Count; candidate++)
            {
                if (!string.Equals(
                        _entries[candidate].Group.ToString(),
                        selectedGroup,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                int distance = Math.Abs(candidate - fallbackIndex);
                if (distance < nearestDistance)
                {
                    nearestIndex = candidate;
                    nearestDistance = distance;
                }
            }

            index = nearestIndex;
        }
        if (index < 0 && fallbackIndex >= 0 && _entries.Count > 0)
        {
            index = Math.Min(fallbackIndex, _entries.Count - 1);
        }

        if (index >= 0)
        {
            _ = NativeMethods.SendMessage(
                _changesList,
                NativeMethods.ListBoxSetCurrentSelection,
                unchecked((nuint)index),
                0);
        }

        if (_entries.Count > 0 && topAnchor is not null)
        {
            int anchoredIndex = _entries.FindIndex(
                entry => ChangeListEntryAnchor(entry).Equals(
                    topAnchor,
                    StringComparison.OrdinalIgnoreCase));
            if (anchoredIndex >= 0)
            {
                topIndex = anchoredIndex;
            }
        }

        if (topIndex >= 0 && _entries.Count > 0)
        {
            _ = NativeMethods.SendMessage(
                _changesList,
                NativeMethods.ListBoxSetTopIndex,
                unchecked((nuint)Math.Min(topIndex, _entries.Count - 1)),
                0);
        }

        _selectedFilePathIntent = index >= 0 && index < _entries.Count
            ? _entries[index].File?.RelativePath
            : null;
    }

    private static string ChangeListEntryAnchor(ChangeListEntry entry)
    {
        return entry.File is { } file
            ? $"F:{file.RelativePath.Replace('\\', '/')}"
            : $"G:{entry.Group}";
    }

    private async Task CommitAsync(bool pushAfterCommit)
    {
        if (_disposed || _commitService is null || _repository is null || _operationRunning)
        {
            return;
        }

        bool openPush = false;
        bool failed = false;
        // 只恢复本次操作暂时清空的焦点；用户换控件或切换应用后不抢回输入。
        nint foregroundAtStart = NativeMethods.GetForegroundWindow();
        SetCommitError(string.Empty);
        BeginOperation(UiText.Committing);
        nint focusAfterDisabling = NativeMethods.GetFocus();
        try
        {
            GitCommitRequest request = new(
                _selection.SelectedPaths.ToArray(),
                NativeMethods.GetWindowTextValue(_commitEdit),
                _amend);
            GitCommitResult commit = await _commitService.CommitAsync(
                _repository,
                request,
                CurrentOperationToken);
            if (_disposed)
            {
                return;
            }
            if (!commit.IsSuccess)
            {
                failed = true;
                SetCommitError(commit.ErrorMessage ?? UiText.CommitFailed);
                _setStatus(_commitErrorText);
                return;
            }

            _selection.Reconcile(commit.ActualStatus?.Files ?? []);
            _amend = false;
            _amendLoading = false;
            _commitDraftBeforeAmend = null;
            _amendRequestVersion++;
            _ = NativeMethods.InvalidateRectangle(_amendButton, 0, true);
            if (!pushAfterCommit)
            {
                _ = NativeMethods.SetWindowText(_commitEdit, string.Empty);
                _setStatus(UiText.CommitCompleted);
                _refreshPending = true;
            }
            else
            {
                _ = NativeMethods.SetWindowText(_commitEdit, string.Empty);
                _setStatus(UiText.CommitCompleted);
                _refreshPending = true;
                openPush = true;
            }
        }
        finally
        {
            if (!_disposed)
            {
                bool restoreMessageFocus = failed
                    && NativeMethods.GetFocus() == focusAfterDisabling
                    && NativeMethods.IsWindowVisible(Handle)
                    && NativeMethods.GetForegroundWindow() == foregroundAtStart;
                EndOperation();
                if (restoreMessageFocus)
                {
                    _ = NativeMethods.SetFocus(_commitEdit);
                }
                RequestRefresh();
            }
        }

        if (openPush)
        {
            ShowPush();
        }
    }

    private async Task ToggleAmendAsync()
    {
        if (_commitService is null || _repository?.Kind != GitRepositoryKind.WorkingTree || _operationRunning)
        {
            return;
        }

        if (_amend)
        {
            _amend = false;
            _amendLoading = false;
            _amendRequestVersion++;
            _ = NativeMethods.SetWindowText(_commitEdit, _commitDraftBeforeAmend ?? string.Empty);
            _commitDraftBeforeAmend = null;
            _ = NativeMethods.InvalidateRectangle(_amendButton, 0, true);
            UpdateCommitActionsEnabled();
            return;
        }

        if (string.IsNullOrWhiteSpace(_status?.HeadCommit))
        {
            ShowOperationError(UiText.AmendUnavailable);
            return;
        }

        _commitDraftBeforeAmend = NativeMethods.GetWindowTextValue(_commitEdit);
        _amendLoading = true;
        int requestVersion = ++_amendRequestVersion;
        _ = NativeMethods.EnableWindow(_amendButton, false);
        _ = NativeMethods.InvalidateRectangle(_amendButton, 0, true);
        _setStatus(UiText.ReadingLastCommit);
        try
        {
            GitCommitMessageResult result = await _commitService.ReadLastCommitMessageAsync(
                _repository,
                _lifetimeCancellation.Token);
            if (_disposed || requestVersion != _amendRequestVersion)
            {
                return;
            }

            if (!result.IsSuccess || result.Message is null)
            {
                _commitDraftBeforeAmend = null;
                ShowOperationError(result.ErrorMessage ?? UiText.LastCommitReadFailed);
                return;
            }

            _amend = true;
            _ = NativeMethods.SetWindowText(_commitEdit, result.Message);
            _setStatus(UiText.LastCommitLoaded);
        }
        catch (OperationCanceledException) when (_disposed || _lifetimeCancellation.IsCancellationRequested)
        {
            // 窗口销毁时取消读取，不向已关闭的界面写入状态。
        }
        catch (Exception exception)
        {
            if (!_disposed && requestVersion == _amendRequestVersion)
            {
                _commitDraftBeforeAmend = null;
                ShowOperationError(string.IsNullOrWhiteSpace(exception.Message)
                    ? UiText.LastCommitReadFailed
                    : exception.Message);
            }
        }
        finally
        {
            if (!_disposed && requestVersion == _amendRequestVersion)
            {
                _amendLoading = false;
                _ = NativeMethods.InvalidateRectangle(_amendButton, 0, true);
                UpdateCommitActionsEnabled();
            }
        }
    }

    private void ShowPush(string? localReference = null)
    {
        if (_repository is null || _remoteService is null || _operationRunning)
        {
            return;
        }

        nint owner = NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot);
        NativePushDialog.Show(
            owner,
            _repository,
            _remoteService,
            _settings,
            _setStatus,
            localReference);
        RequestRefresh();
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

    private void OpenModifiedPreview()
    {
        if (_activeChangedFile is null || _repository?.RepositoryRoot is null)
        {
            return;
        }

        OpenWorkspaceFile(_activeChangedFile);
    }

    private async Task RollbackSelectedAsync(
        GitChangedFile? targetFile = null,
        bool requireConfirmation = true,
        bool executeAfterConfirmation = true)
    {
        GitChangedFile? file = targetFile;
        if (file is null)
        {
            int selectedIndex = GetSelectedEntryIndex();
            file = selectedIndex >= 0 && selectedIndex < _entries.Count
                ? _entries[selectedIndex].File
                : null;
        }

        if (file is null
            || _workspaceStateService is null
            || _diffService is null
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

        if (targetFile is not null)
        {
            await OpenContextDiffAsync(file);
        }
        else
        {
            await ShowSelectedDiffAsync(activatePresentation: true);
        }

        if (_disposed
            || _activeChangedFile?.RelativePath.Equals(file.RelativePath, StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        GitDiffResult preview;
        try
        {
            preview = await _diffService.CreateAsync(
                _repository,
                file,
                new(_ignoreWhitespace),
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!preview.IsSuccess || preview.Document is null)
        {
            ShowOperationError(preview.ErrorMessage ?? UiText.GenerateDiffFailed);
            return;
        }

        if (requireConfirmation
            && !NativeRollbackDialog.Show(
                 NativeMethods.GetAncestor(Handle, NativeMethods.GetAncestorRoot),
                _repository,
                file,
                preview.Document,
                _diffService,
                _settings,
                _setStatus))
        {
            return;
        }


        if (!executeAfterConfirmation)
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
        DismissDiffBoundaryHint();
        _changeLines.Clear();
        _changeLines.AddRange(lines.Distinct().Order());
        _changeNavigationIndex = -1;
        UpdateDiffToolbarSummary();
    }

    private int GetStatusFileIndex(string relativePath)
    {
        if (_status is null)
        {
            return -1;
        }

        for (int index = 0; index < _status.Files.Count; index++)
        {
            if (_status.Files[index].RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void NavigateChange(int direction)
    {
        if (_changeLines.Count == 0 || _loadingDiffPath is not null || _operationRunning || direction == 0)
        {
            return;
        }

        direction = Math.Sign(direction);
        int next = _changeNavigationIndex < 0
            ? direction < 0 ? _changeLines.Count - 1 : 0
            : _changeNavigationIndex + direction;
        if (next >= 0 && next < _changeLines.Count)
        {
            DismissDiffBoundaryHint();
            MoveToChange(next);
            return;
        }

        int targetIndex = FindAdjacentDiffFileIndex(direction);
        string? targetPath = targetIndex >= 0 ? _entries[targetIndex].File?.RelativePath : null;
        if (targetPath is not null && _diffBoundaryDirection == direction
            && string.Equals(_diffBoundaryTargetPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            NavigateFile(direction, landAtBoundary: true);
            return;
        }
        ShowDiffBoundaryHint(direction, targetPath);
    }

    private void MoveToChange(int index)
    {
        _changeNavigationIndex = index;
        int line = _changeLines[_changeNavigationIndex];
        if (_sideBySide)
        {
            _oldDiff?.GoToLine(line, focus: false);
            _diffGutter?.GoToLine(line, focus: false);
            _newDiff?.GoToLine(line, focus: false);
        }
        else
        {
            _unifiedDiff?.GoToLine(line, focus: false);
        }
    }

    private void SynchronizeDiffScroll(nint notificationPointer)
    {
        if (_synchronizingDiffScroll || !_sideBySide)
        {
            return;
        }

        ScintillaControl? source = _oldDiff?.IsUpdateUiNotification(notificationPointer) == true
            ? _oldDiff
            : _newDiff?.IsUpdateUiNotification(notificationPointer) == true
                ? _newDiff
                : _diffGutter?.IsUpdateUiNotification(notificationPointer) == true
                    ? _diffGutter
                    : null;
        if (source is null)
        {
            return;
        }

        _synchronizingDiffScroll = true;
        try
        {
            int firstVisibleLine = source.FirstVisibleLine;
            _oldDiff?.SetFirstVisibleLine(firstVisibleLine);
            _diffGutter?.SetFirstVisibleLine(firstVisibleLine);
            _newDiff?.SetFirstVisibleLine(firstVisibleLine);
        }
        finally
        {
            _synchronizingDiffScroll = false;
        }
    }

    private int FindAdjacentDiffFileIndex(int direction)
    {
        int selectedIndex = GetSelectedEntryIndex();
        return FindAdjacentFileIndexForTest(
            _entries.Count,
            selectedIndex,
            direction,
            index => _entries[index].File is not null);
    }

    private void NavigateFile(int direction, bool landAtBoundary = false)
    {
        DismissDiffBoundaryHint();
        int targetIndex = FindAdjacentDiffFileIndex(direction);
        if (targetIndex < 0)
        {
            return;
        }

        _selectedFilePathIntent = _entries[targetIndex].File!.RelativePath;
        _ = NativeMethods.SendMessage(
            _changesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)targetIndex),
            0);
        UpdateCommitToolbarActionsEnabled();
        _ = ShowSelectedDiffAsync(entryDirection: landAtBoundary ? direction : 0);
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
            _pullModeButton,
            _pushButton,
            _remotesButton,
            _advancedOperationsButton,
            _rollbackToolbarButton,
            _showDiffToolbarButton,
            _expandAllToolbarButton,
            _previewToolbarButton,
            _changesList,
            _commitEdit,
            _amendButton,
            _lastCommitButton,
            _commitSettingsButton,
            _commitButton,
            _commitAndPushButton,
            _previousFileButton,
            _nextFileButton,
            _diffSearchButton,
            _diffSettingsButton,
            _unifiedButton,
            _sideBySideButton,
            _ignoreWhitespaceButton,
            _previousChangeButton,
            _nextChangeButton,
            _modifiedPreviewButton,
            _rollbackButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }

        UpdateCommitActionsEnabled(enabled);
        UpdateCommitToolbarActionsEnabled(enabled);
    }

    private void UpdateCommitActionsEnabled(bool? controlsEnabled = null)
    {
        bool enabled = controlsEnabled
            ?? (_repository?.Kind == GitRepositoryKind.WorkingTree && !_operationRunning);
        bool canAmend = enabled
            && !_amendLoading
            && (_amend || !string.IsNullOrWhiteSpace(_status?.HeadCommit));
        bool canCommit = enabled && (_selection.SelectedPaths.Count > 0 || _amend);
        _ = NativeMethods.EnableWindow(_amendButton, canAmend);
        _ = NativeMethods.EnableWindow(
            _lastCommitButton,
            enabled && !string.IsNullOrWhiteSpace(_status?.HeadCommit));
        _ = NativeMethods.EnableWindow(_commitSettingsButton, !_operationRunning);
        _ = NativeMethods.EnableWindow(_commitButton, canCommit);
        _ = NativeMethods.EnableWindow(_commitAndPushButton, canCommit);
        _toolTip?.Update(
            _amendButton,
            canAmend ? UiText.AmendLastCommit : UiText.AmendUnavailable);
        UpdateCommitSummary();
    }

    private void UpdateCommitSummary()
    {
        int changeCount = _selection.SelectedPaths.Count;
        string summary = FormatCommitChangeCountForTest(changeCount);
        if (!string.Equals(
                NativeMethods.GetWindowTextValue(_commitChangeCount),
                summary,
                StringComparison.Ordinal))
        {
            _ = NativeMethods.SetWindowText(_commitChangeCount, summary);
            _toolTip?.Update(_commitChangeCount, summary.Length == 0 ? UiText.CommitSelectionCount : summary);
            _ = NativeMethods.InvalidateRectangle(_commitChangeCount, 0, true);
        }

        bool showLastCommit = changeCount > 0 && !string.IsNullOrWhiteSpace(_status?.HeadCommit);
        bool lastCommitVisible = _lastCommitButton != 0 && NativeMethods.IsWindowVisible(_lastCommitButton);
        if (showLastCommit != lastCommitVisible)
        {
            _ = NativeMethods.ShowWindow(
                _lastCommitButton,
                showLastCommit ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
    }

    internal static string FormatCommitChangeCountForTest(int changeCount)
    {
        return changeCount > 0 ? $"{changeCount} modified" : string.Empty;
    }

    private void UpdateCommitToolbarActionsEnabled(bool? controlsEnabled = null)
    {
        bool enabled = controlsEnabled
            ?? (_repository?.Kind == GitRepositoryKind.WorkingTree && !_operationRunning);
        int selectedIndex = GetSelectedEntryIndexForIntent();
        GitChangedFile? selectedFile = selectedIndex >= 0 && selectedIndex < _entries.Count
            ? _entries[selectedIndex].File
            : null;
        bool hasSelectedFile = enabled && selectedFile is not null;
        _ = NativeMethods.EnableWindow(_rollbackToolbarButton, hasSelectedFile);
        _ = NativeMethods.EnableWindow(_showDiffToolbarButton, hasSelectedFile);
        _ = NativeMethods.EnableWindow(_expandAllToolbarButton, enabled && _collapsedGroups.Count > 0);
        _ = NativeMethods.EnableWindow(
            _previewToolbarButton,
            hasSelectedFile && SupportsModifiedPreview(selectedFile!));
    }

    private bool SupportsModifiedPreview(GitChangedFile file)
    {
        string extension = Path.GetExtension(file.RelativePath);
        if ((!extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            || _repository?.RepositoryRoot is null)
        {
            return false;
        }

        string previewPath = Path.GetFullPath(Path.Combine(
            _repository.RepositoryRoot,
            file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(previewPath);
    }

    private GitPullMode GetPullMode()
    {
        return _pullModeIndex switch
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

    private int GetSelectedEntryIndexForIntent()
    {
        if (_selectedFilePathIntent is { Length: > 0 } intent)
        {
            int intendedIndex = FindFileEntryIndex(intent);
            if (intendedIndex >= 0)
            {
                return intendedIndex;
            }
        }

        return GetSelectedEntryIndex();
    }

    private string? GetSelectedChangedFilePath()
    {
        if (_selectedFilePathIntent is { Length: > 0 } intent
            && _entries.Any(entry => entry.File?.RelativePath.Equals(intent, StringComparison.OrdinalIgnoreCase) == true))
        {
            return intent;
        }

        int selectedIndex = GetSelectedEntryIndex();
        return selectedIndex >= 0 && selectedIndex < _entries.Count
            ? _entries[selectedIndex].File?.RelativePath
            : _activeChangedFile?.RelativePath;
    }

    private void UpdateSelectedFilePathIntent()
    {
        int selectedIndex = GetSelectedEntryIndex();
        _selectedFilePathIntent = selectedIndex >= 0
            && selectedIndex < _entries.Count
            && _entries[selectedIndex].File is { } file
            ? file.RelativePath
            : null;
    }

    private string? NormalizeWorkspaceChangedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string fullPath = Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(path, _workspaceRoot);
            string relativePath = Path.GetRelativePath(_workspaceRoot, fullPath).Replace('\\', '/');
            return relativePath.Equals("..", StringComparison.Ordinal)
                || relativePath.StartsWith("../", StringComparison.Ordinal)
                ? null
                : relativePath;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string GetDiffInputFingerprint(GitChangedFile file)
    {
        string root = _repository?.RepositoryRoot ?? _workspaceRoot;
        string oldSideFingerprint = file.Kind is GitChangeKind.Added or GitChangeKind.Untracked
            ? "no-old-side"
            : _status?.HeadCommit ?? "unborn-head";
        try
        {
            string fullPath = Path.GetFullPath(Path.Combine(
                root,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            FileInfo info = new(fullPath);
            return info.Exists
                ? $"{oldSideFingerprint}:{info.Length}:{info.LastWriteTimeUtc.Ticks}:{(int)info.Attributes}"
                : $"{oldSideFingerprint}:missing";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return $"{oldSideFingerprint}:unavailable";
        }
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
        // 普通 Git 失败只在当前工具窗显示，避免打断用户在其他区域的上下文。
        if (_repositoryLabel != 0)
        {
            _ = NativeMethods.SetWindowText(_repositoryLabel, message);
            _ = NativeMethods.InvalidateRectangle(_repositoryLabel, 0, true);
        }
    }

    private void SetCommitError(string message)
    {
        if (_commitErrorText == message || _disposed)
        {
            return;
        }

        _commitErrorText = message;
        string caption = message.Length == 0 ? UiText.CommitMessage : message;
        _ = NativeMethods.SetWindowText(_commitLabel, caption.ReplaceLineEndings(" "));
        _toolTip?.Update(_commitLabel, caption);
        _ = NativeMethods.InvalidateRectangle(_commitLabel, 0, true);
    }

    private nint PaintBackground(nint window, nint deviceContext)
    {
        if (deviceContext == 0 || !NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle rectangle))
        {
            return 0;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint panel = palette.Panel;
        uint separator = palette.Border;
        uint control = palette.Panel;
        uint controlBorder = palette.BorderStrong;
        // 完整提交面板先清理外框底色，再画抗锯齿圆角；先填白底会把圆角完全盖住。
        Fill(deviceContext, rectangle, window == Handle ? palette.Chrome : panel);
        FillRounded(
            deviceContext,
            rectangle,
            panel,
            NativeTheme.Scale(18));
        if (window == Handle)
        {
            NativeMethods.Rectangle leftHeaderLine = new()
            {
                Left = 0,
                Top = LeftHeaderHeight - 1,
                Right = rectangle.Right,
                Bottom = LeftHeaderHeight,
            };
            Fill(deviceContext, leftHeaderLine, separator);
            NativeMethods.Rectangle leftToolsLine = new()
            {
                Left = 0,
                Top = LeftContentTop - 1,
                Right = rectangle.Right,
                Bottom = LeftContentTop,
            };
            Fill(deviceContext, leftToolsLine, separator);
            PaintRoundedInput(deviceContext, _commitEditFrame, controlBorder, control);
            return 1;
        }

        NativeMethods.Rectangle diffToolbarLine = new()
        {
            Left = 0,
            Top = DiffToolbarHeight - NativeTheme.Scale(1),
            Right = rectangle.Right,
            Bottom = DiffToolbarHeight,
        };
        Fill(deviceContext, diffToolbarLine, separator);
        NativeMethods.Rectangle diffFileBarLine = new()
        {
            Left = 0,
            Top = DiffContentTop - NativeTheme.Scale(1),
            Right = rectangle.Right,
            Bottom = DiffContentTop,
        };
        Fill(deviceContext, diffFileBarLine, separator);
        NativeTheme.DrawDiffModeGroup(deviceContext, _diffModeGroupBounds, dark);
        return 1;
    }

    private void SetDiffHeaderText(string text)
    {
        _ = NativeMethods.SetWindowText(_diffTitle, text);
        _diffToolTip?.Update(_diffTitle, $"HEAD → {UiText.CurrentVersion} {text}");
    }

    private void DrawDiffFileBar(NativeMethods.DrawItem item, uint panel)
    {
        Fill(item.DeviceContext, item.ItemRectangle, panel);
        NativeDiffFileHeader.Draw(item.DeviceContext, item.ItemRectangle, _diffHeaderSideBySide,
            "HEAD", UiText.CurrentVersion, NativeMethods.GetWindowTextValue(item.Control),
            NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme)), _diffGutterWidth);
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        if (item.ControlIdentifier == ChangesListIdentifier)
        {
            return DrawChangeListItem(item);
        }

        if (item.ControlIdentifier is EmptyChangesTitleIdentifier or EmptyChangesSubtitleIdentifier)
        {
            bool dark = NativeTheme.IsDark(_settings.Theme);
            NativeThemePalette palette = NativeTheme.Palette(dark);
            Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
            DrawText(
                item.DeviceContext,
                NativeMethods.GetWindowTextValue(item.Control),
                item.ItemRectangle,
                item.ControlIdentifier == EmptyChangesTitleIdentifier ? palette.Text : palette.Muted,
                centered: true,
                fontWeight: item.ControlIdentifier == EmptyChangesTitleIdentifier
                    ? NativeTheme.UiMediumFont
                    : NativeTheme.UiFont);
            return true;
        }

        if (item.ControlIdentifier == CommitChangeCountIdentifier)
        {
            bool dark = NativeTheme.IsDark(_settings.Theme);
            NativeThemePalette palette = NativeTheme.Palette(dark);
            Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
            NativeMethods.Rectangle textRectangle = item.ItemRectangle;
            textRectangle.Right -= NativeTheme.Scale(2);
            nint previousFont = NativeMethods.SelectObject(item.DeviceContext, NativeTheme.UiFont);
            _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
            _ = NativeMethods.SetTextColor(item.DeviceContext, palette.Accent);
            string text = NativeMethods.GetWindowTextValue(item.Control);
            _ = NativeMethods.DrawText(
                item.DeviceContext,
                text,
                text.Length,
                ref textRectangle,
                NativeMethods.DrawTextRight
                    | NativeMethods.DrawTextVerticalCenter
                    | NativeMethods.DrawTextSingleLine
                    | NativeMethods.DrawTextNoPrefix
                    | NativeMethods.DrawTextEndEllipsis);
            if (previousFont != 0)
            {
                _ = NativeMethods.SelectObject(item.DeviceContext, previousFont);
            }
            return true;
        }

        if (item.ControlIdentifier is 60 or 61 or 62 or 63)
        {
            bool dark = NativeTheme.IsDark(_settings.Theme);
            NativeThemePalette palette = NativeTheme.Palette(dark);
            uint panel = palette.Panel;
            uint text = palette.Text;
            uint muted = palette.Muted;
            if (item.ControlIdentifier == 63)
            {
                DrawDiffFileBar(item, panel);
                return true;
            }

            (uint placeholderBackground, uint placeholderText) = CommitMessagePlaceholderColors(dark);
            Fill(
                item.DeviceContext,
                item.ItemRectangle,
                item.ControlIdentifier == 62 ? placeholderBackground : panel);
            NativeMethods.Rectangle textRectangle = item.ItemRectangle;
            textRectangle.Left += NativeTheme.Scale(2);
            nint labelFont = item.ControlIdentifier == 60 ? NativeTheme.UiMediumFont : NativeTheme.UiFont;
            nint previousFont = NativeMethods.SelectObject(item.DeviceContext, labelFont);
            _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
            _ = NativeMethods.SetTextColor(
                item.DeviceContext,
                item.ControlIdentifier switch
                {
                    61 => muted,
                    62 => _commitErrorText.Length == 0 ? placeholderText : palette.Danger,
                    _ => text,
                });
            string label = NativeMethods.GetWindowTextValue(item.Control);
            uint format = NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextEndEllipsis;
            _ = NativeMethods.DrawText(item.DeviceContext, label, label.Length, ref textRectangle, format);
            if (previousFont != 0)
            {
                _ = NativeMethods.SelectObject(item.DeviceContext, previousFont);
            }

            return true;
        }

        if (item.ControlIdentifier is DiffFileSummaryIdentifier or DiffChangeSummaryIdentifier)
        {
            bool dark = NativeTheme.IsDark(_settings.Theme);
            NativeThemePalette palette = NativeTheme.Palette(dark);
            Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
            DrawText(
                item.DeviceContext,
                NativeMethods.GetWindowTextValue(item.Control),
                item.ItemRectangle,
                item.ControlIdentifier == DiffFileSummaryIdentifier ? palette.Accent : palette.Muted,
                centered: false,
                fontWeight: NativeTheme.UiFont);
            return true;
        }

        if (item.ControlIdentifier == DiffLoadingIdentifier)
        {
            return DrawDiffLoadingNotice(item, NativeTheme.IsDark(_settings.Theme));
        }

        if (item.ControlIdentifier == DiffBoundaryHintIdentifier)
        {
            return DrawDiffBoundaryHint(item, NativeTheme.IsDark(_settings.Theme));
        }

        if (item.ControlIdentifier == CommandDiffSettings)
        {
            return DrawDiffSettingsButton(item, NativeTheme.IsDark(_settings.Theme));
        }

        if (item.ControlIdentifier == CommandCommitSettings)
        {
            return DrawDiffSettingsButton(item, NativeTheme.IsDark(_settings.Theme));
        }

        if (item.ControlIdentifier == CommandLastCommit)
        {
            return DrawLastCommitButton(item);
        }

        if (item.ControlIdentifier is CommandUnified or CommandSideBySide or CommandIgnoreWhitespace)
        {
            return DrawDiffModeButton(item);
        }

        if (item.ControlIdentifier == CommandAmend)
        {
            return DrawCheckboxButton(item);
        }

        if (item.ControlIdentifier == CommandChoosePullMode)
        {
            return DrawPullModeButton(item);
        }

        if (item.ControlIdentifier is CommandRefresh
            or CommandShowDiff
            or CommandExpandAll
            or CommandPreview
            or CommandRollbackToolbar
            or CommandPreviousFile
            or CommandNextFile
            or CommandDiffSearch
            or CommandPreviousChange
            or CommandNextChange)
        {
            return DrawSelectionToolbarButton(item);
        }

        if (item.ControlIdentifier is CommandMoreActions or CommandClosePanel)
        {
            bool dark = NativeTheme.IsDark(_settings.Theme);
            NativeThemePalette palette = NativeTheme.Palette(dark);
            Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
            if ((item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0)
            {
                FillRounded(item.DeviceContext, item.ItemRectangle, palette.Hover, NativeTheme.Scale(5));
            }

            int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
            int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
            if (item.ControlIdentifier == CommandMoreActions)
            {
                DrawEllipsis(item.DeviceContext, centerX, centerY, palette.Muted);
            }
            else
            {
                _ = NativeTheme.DrawHideIcon(item.DeviceContext, centerX, centerY, palette.Muted);
            }

            return true;
        }

        bool selected = item.ControlIdentifier switch
        {
            CommandUnified => !_sideBySide,
            CommandSideBySide => _sideBySide,
            _ => false,
        };
        return NativeTheme.DrawFlatButton(
            parameter,
            NativeTheme.IsDark(_settings.Theme),
            emphasized: item.ControlIdentifier == CommandCommit,
            selected: selected,
            outlined: item.ControlIdentifier == CommandCommitAndPush);
    }

    private static (uint Background, uint Text) CommitMessagePlaceholderColors(bool dark)
    {
        NativeThemePalette palette = NativeTheme.Palette(dark);
        return (palette.Panel, palette.Muted);
    }

    internal static string FormatDiffFileSummaryForTest(int selectedIndex, int totalCount)
    {
        if (selectedIndex < 1 || totalCount < 1 || selectedIndex > totalCount)
        {
            return string.Empty;
        }

        return $"{selectedIndex}/{totalCount} 个文件";
    }

    internal static string FormatDiffChangeSummaryForTest(
        int changeCount,
        int includedCount = 0,
        bool isLoading = false)
    {
        if (isLoading)
        {
            return UiText.CalculatingDiff;
        }

        int safeChanges = Math.Max(0, changeCount);
        int safeIncluded = Math.Clamp(includedCount, 0, safeChanges);
        return $"{safeChanges} 处差异，{safeIncluded} 个已包含";
    }

    private void UpdateDiffToolbarSummary(int? selectedFileIndex = null)
    {
        int totalCount = _status?.Files.Count ?? 0;
        int selectedIndex = selectedFileIndex ?? GetActiveDiffFileIndex();
        string fileSummary = FormatDiffFileSummaryForTest(selectedIndex, totalCount);
        string changeSummary = FormatDiffChangeSummaryForTest(
            _changeLines.Count,
            isLoading: _loadingDiffPath is not null);
        _ = NativeMethods.SetWindowText(_diffFileSummary, fileSummary);
        _ = NativeMethods.SetWindowText(_diffChangeSummary, changeSummary);
        _ = NativeMethods.InvalidateRectangle(_diffFileSummary, 0, true);
        _ = NativeMethods.InvalidateRectangle(_diffChangeSummary, 0, true);
        bool canNavigate = !_operationRunning && _loadingDiffPath is null && _changeLines.Count > 0;
        _ = NativeMethods.EnableWindow(_previousChangeButton, canNavigate);
        _ = NativeMethods.EnableWindow(_nextChangeButton, canNavigate);
    }

    private int GetActiveDiffFileIndex()
    {
        if (_activeChangedFile is null || _status is null)
        {
            return -1;
        }

        int index = -1;
        for (int candidate = 0; candidate < _status.Files.Count; candidate++)
        {
            if (_status.Files[candidate].RelativePath.Equals(
                    _activeChangedFile.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
                break;
            }
        }
        return index < 0 ? -1 : index + 1;
    }

    private bool DrawPullModeButton(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint control = palette.Panel;
        uint controlBorder = palette.BorderStrong;
        uint text = palette.Text;
        uint hover = palette.Hover;
        PaintRoundedInput(
            item.DeviceContext,
            item.ItemRectangle,
            controlBorder,
            (item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0 ? hover : control);

        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        textRectangle.Left += NativeTheme.Scale(8);
        textRectangle.Right -= NativeTheme.Scale(24);
        DrawText(
            item.DeviceContext,
            NativeMethods.GetWindowTextValue(item.Control),
            textRectangle,
            text,
            centered: false,
            fontWeight: NativeTheme.UiFont);

        int centerX = item.ItemRectangle.Right - NativeTheme.Scale(12);
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, NativeTheme.Scale(1), text);
        if (pen != 0)
        {
            nint previous = NativeMethods.SelectObject(item.DeviceContext, pen);
            _ = NativeMethods.MoveTo(item.DeviceContext, centerX - NativeTheme.Scale(3), centerY - NativeTheme.Scale(1), 0);
            _ = NativeMethods.LineTo(item.DeviceContext, centerX, centerY + NativeTheme.Scale(2));
            _ = NativeMethods.LineTo(item.DeviceContext, centerX + NativeTheme.Scale(3), centerY - NativeTheme.Scale(1));
            if (previous != 0)
            {
                _ = NativeMethods.SelectObject(item.DeviceContext, previous);
            }
            _ = NativeMethods.DeleteObject(pen);
        }

        return true;
    }

    private bool DrawSelectionToolbarButton(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        if ((item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0)
        {
            FillRounded(item.DeviceContext, item.ItemRectangle, palette.Hover, NativeTheme.Scale(5));
        }

        uint color = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0
            ? palette.Faint
            : palette.Muted;
        int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        switch (item.ControlIdentifier)
        {
            case CommandRefresh:
                DrawRefreshIcon(item.DeviceContext, centerX, centerY, color);
                break;
            case CommandShowDiff:
                DrawCompareIcon(item.DeviceContext, centerX, centerY, color);
                break;
            case CommandExpandAll:
                DrawExpandAllIcon(item.DeviceContext, centerX, centerY, color);
                break;
            case CommandPreview:
                DrawPreviewIcon(item.DeviceContext, centerX, centerY, color);
                break;
            case CommandRollbackToolbar:
                DrawRollbackIcon(item.DeviceContext, centerX, centerY, color);
                break;
            case CommandPreviousFile:
                _ = NativeTheme.DrawNavigationIcon(item.DeviceContext, item.ItemRectangle, NativeNavigationIcon.Left, color);
                break;
            case CommandNextFile:
                _ = NativeTheme.DrawNavigationIcon(item.DeviceContext, item.ItemRectangle, NativeNavigationIcon.Right, color);
                break;
            case CommandPreviousChange:
                _ = NativeTheme.DrawNavigationIcon(item.DeviceContext, item.ItemRectangle, NativeNavigationIcon.Up, color);
                break;
            case CommandNextChange:
                _ = NativeTheme.DrawNavigationIcon(item.DeviceContext, item.ItemRectangle, NativeNavigationIcon.Down, color);
                break;
            case CommandDiffSearch:
                _ = NativeTheme.DrawNavigationIcon(item.DeviceContext, item.ItemRectangle, NativeNavigationIcon.Search, color);
                break;
        }
        NativeTheme.DrawToolbarFocus(item, palette);
        return true;
    }

    private static void DrawRefreshIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawRefreshIcon(deviceContext, centerX, centerY, color);
    }

    private static void DrawRollbackIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawRollbackIcon(deviceContext, centerX, centerY, color);
    }

    private static void DrawCompareIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawCompareIcon(deviceContext, centerX, centerY, color);
    }

    private static void DrawExpandAllIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawTrayArrowIcon(deviceContext, centerX, centerY, color);
    }

    private static void DrawPreviewIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawPreviewIcon(deviceContext, centerX, centerY, color);
    }

    private static void DrawSelectionBoxIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, NativeTheme.Scale(1), color);
        if (pen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
        DrawOutlineRectangle(
            deviceContext,
            centerX - NativeTheme.Scale(7),
            centerY - NativeTheme.Scale(7),
            centerX + NativeTheme.Scale(7),
            centerY + NativeTheme.Scale(7));
        _ = NativeMethods.MoveTo(deviceContext, centerX - NativeTheme.Scale(4), centerY, 0);
        _ = NativeMethods.LineTo(deviceContext, centerX - NativeTheme.Scale(1), centerY + NativeTheme.Scale(3));
        _ = NativeMethods.LineTo(deviceContext, centerX + NativeTheme.Scale(5), centerY - NativeTheme.Scale(4));
        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
    }

    private static void DrawEllipsis(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawMoreIcon(deviceContext, centerX, centerY, color);
    }

    private static void DrawDocumentSelectionIcon(
        nint deviceContext,
        int centerX,
        int centerY,
        uint color,
        bool showPlus)
    {
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, NativeTheme.Scale(1), color);
        if (pen == 0)
        {
            return;
        }

        int left = centerX - NativeTheme.Scale(7);
        int top = centerY - NativeTheme.Scale(8);
        int right = centerX + NativeTheme.Scale(6);
        int bottom = centerY + NativeTheme.Scale(8);
        int fold = NativeTheme.Scale(4);
        nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
        _ = NativeMethods.MoveTo(deviceContext, left, top, 0);
        _ = NativeMethods.LineTo(deviceContext, right - fold, top);
        _ = NativeMethods.LineTo(deviceContext, right, top + fold);
        _ = NativeMethods.LineTo(deviceContext, right, bottom);
        _ = NativeMethods.LineTo(deviceContext, left, bottom);
        _ = NativeMethods.LineTo(deviceContext, left, top);
        _ = NativeMethods.MoveTo(deviceContext, right - fold, top, 0);
        _ = NativeMethods.LineTo(deviceContext, right - fold, top + fold);
        _ = NativeMethods.LineTo(deviceContext, right, top + fold);

        if (showPlus)
        {
            _ = NativeMethods.MoveTo(deviceContext, centerX - NativeTheme.Scale(3), centerY + NativeTheme.Scale(2), 0);
            _ = NativeMethods.LineTo(deviceContext, centerX + NativeTheme.Scale(3), centerY + NativeTheme.Scale(2));
            _ = NativeMethods.MoveTo(deviceContext, centerX, centerY - NativeTheme.Scale(1), 0);
            _ = NativeMethods.LineTo(deviceContext, centerX, centerY + NativeTheme.Scale(5));
        }
        else
        {
            _ = NativeMethods.MoveTo(deviceContext, centerX - NativeTheme.Scale(4), centerY + NativeTheme.Scale(1), 0);
            _ = NativeMethods.LineTo(deviceContext, centerX - NativeTheme.Scale(1), centerY + NativeTheme.Scale(4));
            _ = NativeMethods.LineTo(deviceContext, centerX + NativeTheme.Scale(4), centerY - NativeTheme.Scale(2));
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
    }

    private static void DrawOutlineRectangle(nint deviceContext, int left, int top, int right, int bottom)
    {
        _ = NativeMethods.MoveTo(deviceContext, left, top, 0);
        _ = NativeMethods.LineTo(deviceContext, right, top);
        _ = NativeMethods.LineTo(deviceContext, right, bottom);
        _ = NativeMethods.LineTo(deviceContext, left, bottom);
        _ = NativeMethods.LineTo(deviceContext, left, top);
    }

    private bool DrawCheckboxButton(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint panel = palette.Panel;
        uint text = palette.Text;
        uint muted = palette.Muted;
        bool isChecked = item.ControlIdentifier == CommandIgnoreWhitespace
            ? _ignoreWhitespace
            : _amend;
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
        Fill(item.DeviceContext, item.ItemRectangle, panel);
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        _ = NativeTheme.DrawCheckbox(
            item.DeviceContext,
            item.ItemRectangle.Left + NativeTheme.Scale(1),
            centerY,
            isChecked ? NativeCheckboxState.Checked : NativeCheckboxState.Unchecked,
            dark,
            enabled: !disabled);
        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        textRectangle.Left += NativeTheme.Scale(21);
        DrawText(
            item.DeviceContext,
            NativeMethods.GetWindowTextValue(item.Control),
            textRectangle,
            disabled ? muted : text,
            centered: false,
            fontWeight: NativeTheme.UiFont);
        return true;
    }

    private bool DrawDiffModeButton(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        bool selected = item.ControlIdentifier switch
        {
            CommandUnified => !_sideBySide,
            CommandSideBySide => _sideBySide,
            _ => _ignoreWhitespace,
        };
        return NativeTheme.DrawDiffToolbarMode(item,
            item.ControlIdentifier switch
            {
                CommandUnified => NativeDiffModeIcon.Unified,
                CommandSideBySide => NativeDiffModeIcon.SideBySide,
                _ => NativeDiffModeIcon.IgnoreWhitespace,
            }, selected, dark);
    }

    private static bool DrawDiffSettingsButton(NativeMethods.DrawItem item, bool dark)
    {
        NativeThemePalette palette = NativeTheme.Palette(dark);
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        if ((item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0)
        {
            FillRounded(item.DeviceContext, item.ItemRectangle, palette.Hover, NativeTheme.Scale(5));
        }

        uint color = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0
            ? palette.Faint
            : palette.Muted;
        _ = NativeTheme.DrawSettingsIcon(item.DeviceContext, item.ItemRectangle, color);
        NativeTheme.DrawToolbarFocus(item, palette);
        return true;
    }

    private bool DrawLastCommitButton(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        if ((item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0)
        {
            FillRounded(item.DeviceContext, item.ItemRectangle, palette.Hover, NativeTheme.Scale(4));
        }

        uint color = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0
            ? palette.Faint
            : palette.Accent;
        NativeMethods.Rectangle labelRectangle = item.ItemRectangle;
        bool iconOnly = labelRectangle.Right - labelRectangle.Left < _lastCommitTextWidth;
        if (!iconOnly)
        {
            labelRectangle.Right -= NativeTheme.Scale(18);
            DrawText(item.DeviceContext, NativeMethods.GetWindowTextValue(item.Control), labelRectangle,
                color, centered: false, fontWeight: NativeTheme.UiFont);
        }
        NativeMethods.Rectangle iconRectangle = item.ItemRectangle;
        if (!iconOnly) iconRectangle.Left = Math.Max(iconRectangle.Left, iconRectangle.Right - NativeTheme.Scale(18));
        _ = NativeTheme.DrawHistoryIcon(item.DeviceContext, iconRectangle, color);
        return true;
    }

    internal static bool DrawDiffLoadingNotice(NativeMethods.DrawItem item, bool dark)
    {
        NativeThemePalette palette = NativeTheme.Palette(dark);
        DrawDiffLoadingSkeleton(item.DeviceContext, item.ItemRectangle, palette);

        int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int centerY = item.ItemRectangle.Top + NativeTheme.Scale(31);
        int textOffset = NativeTheme.Scale(14);
        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        textRectangle.Left = centerX - NativeTheme.Scale(112) + textOffset;
        textRectangle.Right = centerX + NativeTheme.Scale(112);
        textRectangle.Top = Math.Max(item.ItemRectangle.Top, centerY - NativeTheme.Scale(14));
        textRectangle.Bottom = Math.Min(item.ItemRectangle.Bottom, centerY + NativeTheme.Scale(14));
        DrawText(
            item.DeviceContext,
            NativeMethods.GetWindowTextValue(item.Control),
            textRectangle,
            palette.Muted,
            centered: false,
            fontWeight: NativeTheme.UiFont);
        DrawLoadingMark(
            item.DeviceContext,
            centerX - NativeTheme.Scale(112),
            centerY,
            palette.Accent,
            palette.BorderStrong);
        return true;
    }

    private static void DrawDiffLoadingSkeleton(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        NativeThemePalette palette)
    {
        NativeDiffLoadingSkeletonLayout layout = CalculateDiffLoadingSkeletonLayout(
            Math.Max(0, rectangle.Right - rectangle.Left),
            Math.Max(0, rectangle.Bottom - rectangle.Top));
        Fill(deviceContext, rectangle, palette.PanelMuted);
        if (layout.LeftWidth <= 0 || layout.RightWidth <= 0 || layout.LineCount <= 0)
        {
            return;
        }

        NativeMethods.Rectangle leftPane = rectangle;
        leftPane.Right = leftPane.Left + layout.LeftWidth;
        Fill(deviceContext, leftPane, palette.Panel);
        NativeMethods.Rectangle gutter = rectangle;
        gutter.Left = leftPane.Right;
        gutter.Right = gutter.Left + layout.GutterWidth;
        Fill(deviceContext, gutter, palette.PanelMuted);
        NativeMethods.Rectangle rightPane = rectangle;
        rightPane.Left = gutter.Right;
        Fill(deviceContext, rightPane, palette.Panel);

        Fill(deviceContext, new()
        {
            Left = gutter.Left,
            Top = rectangle.Top,
            Right = Math.Min(gutter.Right, gutter.Left + NativeTheme.Scale(1)),
            Bottom = rectangle.Bottom,
        }, palette.Border);
        Fill(deviceContext, new()
        {
            Left = Math.Max(gutter.Left, gutter.Right - NativeTheme.Scale(1)),
            Top = rectangle.Top,
            Right = gutter.Right,
            Bottom = rectangle.Bottom,
        }, palette.Border);

        int lineTop = rectangle.Top + layout.FirstLineTop;
        int leftInset = NativeTheme.Scale(14);
        int rightInset = NativeTheme.Scale(14);
        for (int index = 0; index < layout.LineCount; index++)
        {
            int y = lineTop + index * layout.LineHeight;
            DrawDiffLoadingSkeletonLine(
                deviceContext,
                leftPane,
                y,
                leftInset,
                rightInset,
                DiffLoadingSkeletonLineFractions[index % DiffLoadingSkeletonLineFractions.Length],
                palette.Hover);
            DrawDiffLoadingSkeletonLine(
                deviceContext,
                rightPane,
                y,
                rightInset,
                leftInset,
                DiffLoadingSkeletonLineFractions[(index + 3) % DiffLoadingSkeletonLineFractions.Length],
                palette.Hover);

            int gutterLineWidth = Math.Max(NativeTheme.Scale(9), layout.GutterWidth / 6);
            int gutterY = y + NativeTheme.Scale(3);
            Fill(deviceContext, new()
            {
                Left = gutter.Left + NativeTheme.Scale(12),
                Top = gutterY,
                Right = gutter.Left + NativeTheme.Scale(12) + gutterLineWidth,
                Bottom = gutterY + NativeTheme.Scale(3),
            }, palette.BorderStrong);
            Fill(deviceContext, new()
            {
                Left = gutter.Right - NativeTheme.Scale(12) - gutterLineWidth,
                Top = gutterY,
                Right = gutter.Right - NativeTheme.Scale(12),
                Bottom = gutterY + NativeTheme.Scale(3),
            }, palette.BorderStrong);
        }
    }

    private static void DrawDiffLoadingSkeletonLine(
        nint deviceContext,
        NativeMethods.Rectangle pane,
        int top,
        int leadingInset,
        int trailingInset,
        int fraction,
        uint color)
    {
        int availableWidth = pane.Right - pane.Left - leadingInset - trailingInset;
        if (availableWidth <= 0)
        {
            return;
        }

        int width = Math.Max(NativeTheme.Scale(28), availableWidth * fraction / 100);
        Fill(deviceContext, new()
        {
            Left = pane.Left + leadingInset,
            Top = top,
            Right = Math.Min(pane.Right - trailingInset, pane.Left + leadingInset + width),
            Bottom = top + NativeTheme.Scale(8),
        }, color);
    }

    internal static NativeDiffLoadingSkeletonLayout DiffLoadingSkeletonLayoutForTest(int width, int height)
    {
        return CalculateDiffLoadingSkeletonLayout(width, height);
    }

    private static NativeDiffLoadingSkeletonLayout CalculateDiffLoadingSkeletonLayout(int width, int height)
    {
        int availableWidth = Math.Max(0, width);
        int gutterWidth = Math.Min(NativeTheme.Scale(84), availableWidth / 3);
        int leftWidth = Math.Max(0, (availableWidth - gutterWidth) / 2);
        int rightWidth = Math.Max(0, availableWidth - gutterWidth - leftWidth);
        int firstLineTop = NativeTheme.Scale(62);
        int lineHeight = NativeTheme.Scale(20);
        int availableLineHeight = Math.Max(0, height - firstLineTop - NativeTheme.Scale(12));
        int lineCount = Math.Clamp(availableLineHeight / Math.Max(1, lineHeight), 0, 12);
        return new(gutterWidth, leftWidth, rightWidth, firstLineTop, lineHeight, lineCount);
    }

    private static void DrawLoadingMark(
        nint deviceContext,
        int centerX,
        int centerY,
        uint accent,
        uint border)
    {
        nint ringPen = NativeMethods.CreatePen(
            NativeMethods.PenStyleSolid,
            NativeTheme.Scale(2),
            border);
        if (ringPen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(deviceContext, ringPen);
        nint previousBrush = NativeMethods.SelectObject(
            deviceContext,
            NativeMethods.GetStockObject(NativeMethods.NullBrush));
        int radius = NativeTheme.Scale(7);
        _ = NativeMethods.DrawEllipse(
            deviceContext,
            centerX - radius,
            centerY - radius,
            centerX + radius + 1,
            centerY + radius + 1);
        if (previousBrush != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);
        }
        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }
        _ = NativeMethods.DeleteObject(ringPen);

        nint accentPen = NativeMethods.CreatePen(
            NativeMethods.PenStyleSolid,
            NativeTheme.Scale(2),
            accent);
        if (accentPen == 0)
        {
            return;
        }

        previousPen = NativeMethods.SelectObject(deviceContext, accentPen);
        _ = NativeMethods.MoveTo(
            deviceContext,
            centerX,
            centerY - radius,
            0);
        _ = NativeMethods.LineTo(
            deviceContext,
            centerX + NativeTheme.Scale(4),
            centerY - NativeTheme.Scale(5));
        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }
        _ = NativeMethods.DeleteObject(accentPen);
    }

    private bool DrawChangeListItem(NativeMethods.DrawItem item)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint panel = palette.Panel;
        uint text = palette.Text;
        uint muted = palette.Muted;
        uint selection = NativeTheme.SelectionColor(palette, NativeMethods.GetFocus() == _changesList);
        Fill(item.DeviceContext, item.ItemRectangle, panel);
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= _entries.Count)
        {
            return true;
        }

        ChangeListEntry entry = _entries[index];
        bool selected = (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        NativeChangeListRowLayout layout = CalculateChangeListRowLayout(
            item.ItemRectangle.Left,
            entry.File is null);
        NativeMethods.Rectangle row = item.ItemRectangle;
        row.Left = layout.RowLeft;
        row.Right -= layout.RowRightInset;
        if (selected)
        {
            FillRounded(item.DeviceContext, row, selection, NativeTheme.Scale(5));
        }
        else if (index == _hoveredChangeIndex)
        {
            FillRounded(item.DeviceContext, row, palette.Hover, NativeTheme.Scale(5));
        }

        int centerY = (row.Top + row.Bottom) / 2;
        if (entry.File is null)
        {
            DrawChevron(
                item.DeviceContext,
                layout.ChevronLeft,
                centerY,
                !_collapsedGroups.Contains(entry.Group),
                muted);
            _ = NativeTheme.DrawCheckbox(
                item.DeviceContext,
                layout.CheckboxLeft,
                centerY,
                ResolveGroupCheckState(_status?.Files ?? [], _selection, entry.Group),
                dark);
            int count = _status?.Files.Count(candidate => candidate.Group == entry.Group) ?? 0;
            NativeMethods.Rectangle groupText = row;
            groupText.Left = layout.TextLeft;
            string groupName = entry.Group == GitChangeGroup.Changes ? UiText.Changes : UiText.UnversionedFiles;
            DrawText(item.DeviceContext, groupName, groupText, text, centered: false, fontWeight: NativeTheme.UiMediumFont);
            int nameWidth = MeasureTextWidth(item.DeviceContext, groupName, NativeTheme.UiMediumFont);
            groupText.Left += nameWidth + NativeTheme.Scale(8);
            DrawText(
                item.DeviceContext,
                $"{count} 个文件",
                groupText,
                muted,
                centered: false,
                fontWeight: NativeTheme.UiFont);
            return true;
        }

        GitChangedFile file = entry.File;
        uint statusColor = NativeGitStatusPalette.Resolve(file.Kind, dark);
        bool isChecked = _selection.IsSelected(file.RelativePath);
        _ = NativeTheme.DrawCheckbox(
            item.DeviceContext,
            layout.CheckboxLeft,
            centerY,
            isChecked ? NativeCheckboxState.Checked : NativeCheckboxState.Unchecked,
            dark);
        _ = DrawChangedFileIcon(
            item.DeviceContext,
            layout.FileIconCenter,
            centerY,
            file.RelativePath,
            NativeTheme.FileTypeIconColor(file.RelativePath, dark));

        string fileName = Path.GetFileName(file.RelativePath);
        string? directory = Path.GetDirectoryName(file.RelativePath)?.Replace('\\', '/');
        NativeMethods.Rectangle nameRectangle = row;
        nameRectangle.Left = layout.TextLeft;
        nameRectangle.Right -= NativeTheme.Scale(8);
        NativeChangeListTextColumns columns = CalculateChangeListTextColumns(
            Math.Max(0, nameRectangle.Right - nameRectangle.Left),
            MeasureTextWidth(item.DeviceContext, fileName, NativeTheme.UiFont),
            string.IsNullOrEmpty(directory) ? 0 : MeasureTextWidth(item.DeviceContext, directory, NativeTheme.UiFont));
        NativeMethods.Rectangle fileRectangle = nameRectangle;
        fileRectangle.Right = Math.Min(
            nameRectangle.Right,
            fileRectangle.Left + columns.FileNameWidth);
        DrawText(item.DeviceContext, fileName, fileRectangle, statusColor, centered: false, fontWeight: NativeTheme.UiFont);
        if (!string.IsNullOrEmpty(directory) && columns.DirectoryWidth > 0)
        {
            NativeMethods.Rectangle directoryRectangle = nameRectangle;
            directoryRectangle.Left = fileRectangle.Right + columns.Gap;
            DrawText(item.DeviceContext, directory, directoryRectangle, muted, centered: false, fontWeight: NativeTheme.UiFont);
        }

        return true;
    }

    private static NativeChangeListTextColumns CalculateChangeListTextColumns(
        int availableWidth,
        int measuredFileNameWidth,
        int measuredDirectoryWidth)
    {
        int gap = NativeTheme.Scale(8);
        int minimumFileNameWidth = Math.Min(Math.Max(0, measuredFileNameWidth), NativeTheme.Scale(56));
        int minimumDirectoryWidth = Math.Min(Math.Max(0, measuredDirectoryWidth), NativeTheme.Scale(56));
        if (minimumDirectoryWidth == 0 || availableWidth < minimumFileNameWidth + gap + minimumDirectoryWidth)
        {
            return new(Math.Max(0, availableWidth), 0, 0);
        }

        // 文件名按实际字宽优先分配，短目录不占固定比例；不足以辨认两列时只显示文件名。
        int contentWidth = availableWidth - gap;
        int fileNameWidth = Math.Clamp(measuredFileNameWidth, minimumFileNameWidth, contentWidth - minimumDirectoryWidth);
        return new(
            fileNameWidth,
            gap,
            Math.Max(0, contentWidth - fileNameWidth));
    }

    private static NativeChangeListRowLayout CalculateChangeListRowLayout(int itemLeft, bool isGroup)
    {
        int firstSlotLeft = ChangeRowHorizontalInset + ChangeRowContentInset;
        int checkboxLeft = isGroup
            ? firstSlotLeft + ChangeRowSlotWidth + ChangeRowColumnGap
            : firstSlotLeft;
        int fileIconCenter = firstSlotLeft
            + ChangeCheckboxSize
            + ChangeRowColumnGap
            + ChangeRowSlotWidth / 2;
        int textLeft = firstSlotLeft
            + ChangeRowSlotWidth
            + ChangeRowColumnGap
            + ChangeCheckboxSize
            + 8;
        int chevronLeft = firstSlotLeft + (ChangeRowSlotWidth - ChangeChevronGlyphWidth) / 2;
        return new(
            itemLeft + NativeTheme.Scale(ChangeRowHorizontalInset),
            NativeTheme.Scale(ChangeRowHorizontalInset),
            itemLeft + NativeTheme.Scale(chevronLeft),
            itemLeft + NativeTheme.Scale(checkboxLeft),
            itemLeft + NativeTheme.Scale(fileIconCenter),
            itemLeft + NativeTheme.Scale(textLeft),
            NativeTheme.Scale(ChangeCheckboxSize));
    }

    internal static bool DrawChangedFileIcon(
        nint deviceContext,
        int centerX,
        int centerY,
        string fileName,
        uint color)
    {
        NativeMethods.Rectangle rectangle = new()
        {
            Left = centerX - NativeTheme.Scale(8),
            Top = centerY - NativeTheme.Scale(8),
            Right = centerX + NativeTheme.Scale(8),
            Bottom = centerY + NativeTheme.Scale(8),
        };
        return NativeTheme.DrawFileTypeIcon(deviceContext, rectangle, fileName, color);
    }

    internal static NativeCheckboxState ResolveGroupCheckState(
        IReadOnlyList<GitChangedFile> files,
        GitFileSelection selection,
        GitChangeGroup group)
    {
        bool anySelected = false, anyUnselected = false;
        // 根据完整状态快照判断，折叠分组后可见行不能改变复选状态。
        foreach (GitChangedFile file in files)
        {
            if (file.Group != group) continue;
            if (selection.IsSelected(file.RelativePath)) anySelected = true;
            else anyUnselected = true;
            if (anySelected && anyUnselected) return NativeCheckboxState.Mixed;
        }
        return anySelected ? NativeCheckboxState.Checked : NativeCheckboxState.Unchecked;
    }

    private static void DrawChevron(nint deviceContext, int left, int centerY, bool expanded, uint color)
    {
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, NativeTheme.Scale(1), color);
        if (pen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
        if (expanded)
        {
            _ = NativeMethods.MoveTo(deviceContext, left, centerY - NativeTheme.Scale(2), 0);
            _ = NativeMethods.LineTo(deviceContext, left + NativeTheme.Scale(4), centerY + NativeTheme.Scale(2));
            _ = NativeMethods.LineTo(deviceContext, left + NativeTheme.Scale(8), centerY - NativeTheme.Scale(2));
        }
        else
        {
            _ = NativeMethods.MoveTo(deviceContext, left + NativeTheme.Scale(2), centerY - NativeTheme.Scale(4), 0);
            _ = NativeMethods.LineTo(deviceContext, left + NativeTheme.Scale(6), centerY);
            _ = NativeMethods.LineTo(deviceContext, left + NativeTheme.Scale(2), centerY + NativeTheme.Scale(4));
        }
        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }
        _ = NativeMethods.DeleteObject(pen);
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

        FillRounded(deviceContext, rectangle, border, 6);
        NativeMethods.Rectangle inner = rectangle;
        inner.Left++;
        inner.Top++;
        inner.Right--;
        inner.Bottom--;
        FillRounded(deviceContext, inner, fill, 5);
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        bool centered,
        nint fontWeight)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, fontWeight);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextVerticalCenter
            | NativeMethods.DrawTextSingleLine
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

    private static int MeasureTextWidth(nint deviceContext, string text, nint font)
    {
        if (deviceContext == 0 || string.IsNullOrEmpty(text))
        {
            return 0;
        }

        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        NativeMethods.Rectangle rectangle = new() { Right = NativeTheme.Scale(1000), Bottom = NativeTheme.Scale(30) };
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextCalculateRectangle);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }

        return Math.Max(0, rectangle.Right - rectangle.Left);
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }

    private void InvalidateDiffModeButtons()
    {
        _ = NativeMethods.InvalidateRectangle(_unifiedButton, 0, true);
        _ = NativeMethods.InvalidateRectangle(_sideBySideButton, 0, true);
    }

    private void Layout(bool force = false)
    {
        if (Handle == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int leftWidth = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        int diffWidth = NativeMethods.GetClientRectangle(_diffHandle, out NativeMethods.Rectangle diffClient)
            ? Math.Max(0, diffClient.Right - diffClient.Left)
            : 0;
        int diffSurfaceHeight = Math.Max(0, diffClient.Bottom - diffClient.Top);
        int scale = NativeTheme.Scale(100);
        if (!force
            && _hasLayoutMetrics
            && _layoutPanelWidth == leftWidth
            && _layoutPanelHeight == height
            && _layoutDiffWidth == diffWidth
            && _layoutDiffHeight == diffSurfaceHeight
            && _layoutScale == scale)
        {
            return;
        }

        _hasLayoutMetrics = true;
        _layoutPanelWidth = leftWidth;
        _layoutPanelHeight = height;
        _layoutDiffWidth = diffWidth;
        _layoutDiffHeight = diffSurfaceHeight;
        _layoutScale = scale;
        _layoutInvocationCount++;

        int headerTextHeight = NativeTheme.ContentHeight(30, 4);
        int leftHeaderControlTop = Math.Max(0, (LeftHeaderHeight - headerTextHeight) / 2);
        int headerIconTop = Math.Max(0, (LeftHeaderHeight - NativeTheme.Scale(27)) / 2);
        int leftToolsControlTop = LeftHeaderHeight + Math.Max(0, (LeftToolsHeight - NativeTheme.Scale(27)) / 2);
        int titleWidth = Math.Min(_commitTitleTextWidth, Math.Max(0, leftWidth - NativeTheme.Scale(82)));
        Move(_panelTitle, NativeTheme.Scale(10), leftHeaderControlTop, titleWidth, headerTextHeight);
        Move(
            _repositoryLabel,
            NativeTheme.Scale(12) + titleWidth,
            leftHeaderControlTop,
            Math.Max(0, leftWidth - titleWidth - NativeTheme.Scale(82)),
            headerTextHeight);
        Move(
            _moreActionsButton,
            Math.Max(0, leftWidth - NativeTheme.Scale(68)),
            headerIconTop,
            NativeTheme.Scale(27),
            NativeTheme.Scale(27));
        Move(
            _closePanelButton,
            Math.Max(0, leftWidth - NativeTheme.Scale(36)),
            headerIconTop,
            NativeTheme.Scale(27),
            NativeTheme.Scale(27));
        Move(
            _cancelButton,
            Math.Max(NativeTheme.Scale(166), leftWidth - NativeTheme.Scale(96)),
            LeftHeaderHeight + (LeftToolsHeight - NativeTheme.ContentHeight(27, 4)) / 2,
            Math.Max(0, Math.Min(NativeTheme.Scale(88), leftWidth - NativeTheme.Scale(174))),
            NativeTheme.ContentHeight(27, 4));
        Move(_initializeButton, NativeTheme.Scale(8), LeftHeaderHeight + (LeftToolsHeight - NativeTheme.ContentHeight(27, 4)) / 2,
            Math.Max(0, leftWidth - NativeTheme.Scale(16)), NativeTheme.ContentHeight(27, 4));
        Move(_refreshButton, NativeTheme.Scale(8), leftToolsControlTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        Move(_rollbackToolbarButton, NativeTheme.Scale(39), leftToolsControlTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        Move(_showDiffToolbarButton, NativeTheme.Scale(70), leftToolsControlTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        Move(_expandAllToolbarButton, NativeTheme.Scale(101), leftToolsControlTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        Move(_previewToolbarButton, NativeTheme.Scale(132), leftToolsControlTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        LayoutCommitSection(leftWidth, height);

        int diffToolsTop = Math.Max(0, (DiffToolbarHeight - NativeTheme.Scale(27)) / 2);
        Move(_previousChangeButton, NativeTheme.Scale(8), diffToolsTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        Move(_nextChangeButton, NativeTheme.Scale(39), diffToolsTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        Move(_diffSearchButton, NativeTheme.Scale(74), diffToolsTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        Move(_previousFileButton, NativeTheme.Scale(109), diffToolsTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        int diffTextHeight = NativeTheme.ContentHeight(27, 4);
        Move(_diffFileSummary, NativeTheme.Scale(140), (DiffToolbarHeight - diffTextHeight) / 2, NativeTheme.Scale(84), diffTextHeight);
        Move(_nextFileButton, NativeTheme.Scale(228), diffToolsTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        int settingsLeft = Math.Max(NativeTheme.Scale(310), diffWidth - NativeTheme.Scale(35));
        Move(_diffSettingsButton, settingsLeft, diffToolsTop, NativeTheme.Scale(27), NativeTheme.Scale(27));
        _diffModeGroupBounds = NativeTheme.DiffModeGroupBounds(settingsLeft, DiffToolbarHeight);
        NativeMethods.Rectangle unified = NativeTheme.DiffModeButtonBounds(_diffModeGroupBounds, sideBySide: false);
        NativeMethods.Rectangle sideBySide = NativeTheme.DiffModeButtonBounds(_diffModeGroupBounds, sideBySide: true);
        Move(_unifiedButton, unified.Left, unified.Top, unified.Right - unified.Left, unified.Bottom - unified.Top);
        Move(_sideBySideButton, sideBySide.Left, sideBySide.Top, sideBySide.Right - sideBySide.Left, sideBySide.Bottom - sideBySide.Top);
        int ignoreLeft = _diffModeGroupBounds.Left - NativeTheme.Scale(34);
        Move(
            _ignoreWhitespaceButton,
            ignoreLeft,
            diffToolsTop,
            NativeTheme.Scale(27),
            NativeTheme.Scale(27));
        Move(
            _diffChangeSummary,
            Math.Max(
                NativeTheme.Scale(284),
                ignoreLeft - NativeTheme.Scale(190)),
            (DiffToolbarHeight - diffTextHeight) / 2,
            Math.Max(
                0,
                ignoreLeft - NativeTheme.Scale(6)
                    - Math.Max(NativeTheme.Scale(284), ignoreLeft - NativeTheme.Scale(190))),
            diffTextHeight);
        LayoutDiffContent();
    }

    private void ApplyDiffHeaderMode(bool sideBySide, bool layoutChanged = false)
    {
        if (_diffHeaderSideBySide == sideBySide && !layoutChanged) return;
        _diffHeaderSideBySide = sideBySide;
        LayoutDiffContent();
        _ = NativeMethods.InvalidateRectangle(_diffTitle, 0, true);
        _ = NativeMethods.InvalidateRectangle(_diffHandle, 0, true);
    }

    private void LayoutDiffContent()
    {
        if (_diffHandle == 0 || !NativeMethods.GetClientRectangle(_diffHandle, out NativeMethods.Rectangle surface)) return;
        int diffWidth = Math.Max(0, surface.Right - surface.Left);
        int diffSurfaceHeight = Math.Max(0, surface.Bottom - surface.Top);
        Move(_diffTitle, 0, DiffToolbarHeight, diffWidth, DiffFileBarHeight);
        int diffTop = DiffContentTop;
        int diffContentHeight = Math.Max(0, diffSurfaceHeight - diffTop - 1);
        int bodyTop = diffTop;
        int bodyHeight = diffContentHeight;
        _unifiedDiff?.SetBounds(NativeTheme.Scale(1), bodyTop, Math.Max(0, diffWidth - NativeTheme.Scale(2)), bodyHeight);
        int availableDiffWidth = Math.Max(0, diffWidth - NativeTheme.Scale(2));
        int gutterWidth = Math.Min(_diffGutterWidth, availableDiffWidth);
        int halfWidth = Math.Max(0, (availableDiffWidth - gutterWidth) / 2);
        _oldDiff?.SetBounds(NativeTheme.Scale(1), bodyTop, halfWidth, bodyHeight);
        _diffGutter?.SetBounds(
            NativeTheme.Scale(1) + halfWidth,
            bodyTop,
            gutterWidth,
            bodyHeight);
        _newDiff?.SetBounds(
            NativeTheme.Scale(1) + halfWidth + gutterWidth,
            bodyTop,
            Math.Max(0, availableDiffWidth - halfWidth - gutterWidth),
            bodyHeight);
        Move(
            _diffLoadingNotice,
            NativeTheme.Scale(1),
            diffTop,
            Math.Max(0, diffWidth - NativeTheme.Scale(2)),
            diffContentHeight);
        DismissDiffBoundaryHint();

        ApplyRoundedRegion(
            _unifiedDiff?.Handle ?? 0,
            Math.Max(0, diffWidth - NativeTheme.Scale(2)),
            diffContentHeight,
            NativeTheme.Scale(9));
        ApplyRoundedRegion(_oldDiff?.Handle ?? 0, halfWidth, diffContentHeight, NativeTheme.Scale(9));
        ApplyRoundedRegion(_diffGutter?.Handle ?? 0, gutterWidth, diffContentHeight, NativeTheme.Scale(2));
        ApplyRoundedRegion(
            _newDiff?.Handle ?? 0,
            Math.Max(0, availableDiffWidth - halfWidth - gutterWidth),
            diffContentHeight,
            NativeTheme.Scale(9));
    }

    private void ApplyDiffTextPadding()
    {
        int bodyPadding = NativeTheme.Scale(13);
        _unifiedDiff?.SetTextPadding(bodyPadding, bodyPadding);
        _oldDiff?.SetTextPadding(bodyPadding, bodyPadding);
        _newDiff?.SetTextPadding(bodyPadding, bodyPadding);
        int gutterPadding = NativeTheme.Scale(7);
        _diffGutter?.SetTextPadding(gutterPadding, gutterPadding);
    }

    private bool UpdateDiffGutterWidth()
    {
        // 行号已经按最长位数补齐，只度量第一行；字号变化不能重新解析整份补丁。
        int end = _diffGutterRenderedText.IndexOfAny(['\r', '\n']);
        string sample = end < 0 ? "999    999" : _diffGutterRenderedText[..end];
        int width = Math.Max(DiffGutterWidth, (_diffGutter?.MeasureTextWidth(sample) ?? 0) + NativeTheme.Scale(14));
        if (width == _diffGutterWidth) return false;
        _diffGutterWidth = width;
        return true;
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private static int CalculateCommitTop(int height)
    {
        int availableAfterHeader = Math.Max(0, height - LeftHeaderHeight);
        int desiredCommitHeight = Math.Max(
            MinimumCommitSectionHeight,
            (int)Math.Round(availableAfterHeader * CommitSectionHeightRatio));
        int maximumCommitHeight = Math.Max(
            0,
            height - LeftContentTop - MinimumChangesListHeight);
        int commitHeight = Math.Min(desiredCommitHeight, maximumCommitHeight);
        return Math.Max(LeftContentTop, height - commitHeight);
    }

    private static void DestroyControl(ref nint control)
    {
        nint handle = control;
        control = 0;
        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    private static void ApplyRoundedRegion(nint window, int width, int height, int radius)
    {
        if (window == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, width + 1, height + 1, radius, radius);
        if (region == 0)
        {
            return;
        }

        if (NativeMethods.SetWindowRegion(window, region, true) == 0)
        {
            _ = NativeMethods.DeleteObject(region);
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

    private sealed record ChangeListEntry(
        GitChangeGroup Group,
        GitChangedFile? File,
        int GroupFileCount = 0);
}

internal readonly record struct NativeChangeListRowLayout(
    int RowLeft,
    int RowRightInset,
    int ChevronLeft,
    int CheckboxLeft,
    int FileIconCenter,
    int TextLeft,
    int CheckboxSize);

internal readonly record struct NativeChangeListTextColumns(
    int FileNameWidth,
    int Gap,
    int DirectoryWidth);

internal readonly record struct NativeCommitLayoutMetrics(
    int HeaderHeight,
    int ToolbarHeight,
    int ToolbarBottomGap,
    int MinimumChangesHeight,
    int MinimumCommitHeight,
    double CommitHeightRatio);

internal readonly record struct NativeDiffLoadingSkeletonLayout(
    int GutterWidth,
    int LeftWidth,
    int RightWidth,
    int FirstLineTop,
    int LineHeight,
    int LineCount);

internal enum NativeCommitToolbarAction
{
    Refresh,
    Rollback,
    ShowDiff,
    ExpandAll,
    Preview,
}
