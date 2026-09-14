using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Files;
using Augit.Core.Git;
using Augit.Infrastructure.Files;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;
using Microsoft.Web.WebView2.Core;

namespace Augit.App;

internal sealed partial class MainWindow : IDisposable
{
    private const string WindowClassName = "Augit.MainWindow.Native";
    private static int MinimumWidth => S(1024);
    private static int MinimumHeight => S(640);
    private static int ToolbarHeight => NativeTheme.ContentHeight(44, 18);
    private static int ToolbarTextHeight => NativeTheme.ContentHeight(30, 4);
    private static int ActivityBarWidth => S(42);
    private static int TabHeight => NativeTheme.ContentHeight(42, 14);
    private static int StatusHeight => NativeTheme.ContentHeight(22, 2);
    private int _statusFormatWidth;
    private int _statusCancelWidth;
    private const double DefaultBottomPanelHeightRatio = 0.31d;
    // 大字号下工具窗口仍需容纳标题、筛选／工具栏及至少一行正文。
    private static int DefaultBottomPanelMinimumHeight => Math.Max(S(180), NativeTheme.UiLineHeight * 4 + S(80));
    private static int DefaultBottomPanelMaximumHeight => S(305);
    private const int FrameInset = 0;
    private static int CardGap => S(4);
    private static int CardRadius => S(9);
    private static int ActivityBarLeft => S(6);
    // 主区域按视觉稿保留 6px 左侧页边距、42px 全局工具栏和 4px 表面间隙；右侧页边距为 7px。
    private static int MainPanelLeft => ActivityBarLeft + ActivityBarWidth + CardGap;
    private static int MainRightInset => S(7);
    private const int FrameAmbientFadeHeight = 0;
    private const int SurfaceContentInset = 1;
    private const int FileTreeControlIdentifier = 301;
    private const int DocumentTabsControlIdentifier = 302;
    private const int ApplicationIconControlIdentifier = 307;
    private const int OperationNotificationControlIdentifier = 308;
    private const int CommandOpenFolder = 101;
    private const int CommandRefresh = 102;
    private const int CommandQuickOpen = 103;
    private const int CommandWorkspaceSearch = 104;
    private const int CommandSettings = 105;
    private const int CommandFiles = 106;
    private const int CommandGitChanges = 107;
    private const int CommandClone = 108;
    private const int CommandHistory = 109;
    private const int CommandRecentWorkspaces = 110;
    private const int CommandTerminal = 111;
    private const int CommandCloseTerminal = 112;
    private const int CommandMainMenu = 113;
    private const int CommandBranch = 114;
    private const int CommandMinimize = 115;
    private const int CommandMaximize = 116;
    private const int CommandCloseWindow = 117;
    private const int CommandLocateActiveFile = 118;
    private const int CommandCollapseTree = 119;
    private const int CommandTreeOptions = 120;
    private const int CommandHideTerminal = 121;
    private const int CommandCancelBranch = 122;
    private const int CommandHideProject = 123;
    private const int CommandCurrentFile = 124;
    private const int CommandCancelOperationNotification = 125;
    private const int CommandConfigureGit = 126;
    private const int CommandTerminalMore = 127;
    private const int TerminalTitleControlIdentifier = 401;
    private const int TerminalSessionControlIdentifier = 402;
    private const int TerminalSessionCloseControlIdentifier = 403;
    private const int MenuCopyPath = 201;
    private const int MenuRevealInExplorer = 202;
    private const int MenuOpenTerminal = 203;
    private const int MenuRefresh = 204;
    private const int MenuFileHistory = 205;
    private const int MenuCloseDocument = 206;
    private const int MenuCloseOtherDocuments = 207;
    private const int MenuCloseAllDocuments = 208;
    private const int MenuBlame = 209;
    private const int BranchMenuHistory = 301;
    private const int BranchMenuManageReferences = 302;
    private const int BranchMenuFirstLocalBranch = 2000;
    private const int DocumentMenuFirstTab = 3000;
    private const nuint DocumentTabsSubclassIdentifier = 1;
    private const nuint OperationNotificationSubclassIdentifier = 2;
    private const nuint FileTreeSubclassIdentifier = 3;
    private const uint TreeViewActionExpand = 0x0002;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, MainWindow> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly NativeMethods.SubclassProcedure DocumentTabsProcedure = HandleDocumentTabsMessage;
    private static readonly NativeMethods.SubclassProcedure OperationNotificationProcedure = HandleOperationNotificationMessage;
    private static readonly NativeMethods.SubclassProcedure FileTreeProcedure = HandleFileTreeMessage;
    private static bool _classRegistered;
    private readonly SettingsStore _settingsStore;
    private readonly ConcurrentQueue<Action> _dispatchQueue = new();
    private readonly WorkspaceFileChangeQueue _workspaceFileChangeQueue = new();
    private readonly Dictionary<int, TreeNodeState> _treeNodes = [];
    private readonly List<DocumentTabState> _documents = [];
    private ApplicationSettings _settings;
    private WorkspaceInstanceCoordinator? _instanceCoordinator;
    private WorkspaceInstanceCoordinator? _pendingOwnedCoordinator;
    private WorkspaceFileWatcher? _fileWatcher;
    private CancellationTokenSource? _treeGitStatusCancellation;
    private GitRepositorySnapshot? _treeGitRepository;
    private GitStatusService? _treeGitStatusService;
    private NativeGitTreeStatusIndex? _treeGitStatusIndex;
    private GitStatusSnapshot? _treeGitStatusSnapshot;
    private string? _treeGitStatusWorkspaceRoot;
    private NativeSearchPanel? _searchPanel;
    private nint _searchFocusBeforeOpen;
    private NativeGitPanel? _gitPanel;
    private NativeGitHistoryPanel? _historyPanel;
    private NativeGitComparisonView? _comparisonView;
    private NativeBranchPopup? _branchPopup;
    private NativeContextMenu? _contextMenu;
    private NativeTerminalPanel? _terminalPanel;
    private NativeWorkspaceOpenDialog? _workspaceOpenDialog;
    private NativeToolTip? _toolTip;
    private CancellationTokenSource? _operationNotificationDismissal;
    private string? _workspaceRoot;
    private nint _handle;
    private nint _applicationIcon;
    private nint _openFolderButton;
    private nint _cloneButton;
    private nint _recentWorkspacesButton;
    private nint _quickOpenButton;
    private nint _currentFileButton;
    private nint _searchButton;
    private nint _settingsButton;
    private nint _filesButton;
    private nint _gitButton;
    private nint _historyButton;
    private nint _terminalButton;
    private nint _projectHeader;
    private nint _locateActiveFileButton;
    private nint _collapseTreeButton;
    private nint _treeOptionsButton;
    private nint _hideProjectButton;
    private nint _fileTree;
    private nint _documentTabs;
    private nint _emptyDocumentLabel;
    private nint _workspaceLabel;
    private nint _statusBar;
    private nint _cancelBranchButton;
    private nint _operationNotification;
    private nint _operationNotificationCancelButton;
    private nint _operationNotificationActionButton;
    private nint _minimizeButton;
    private nint _maximizeButton;
    private nint _closeButton;
    private string _statusText = UiText.NoWorkspace;
    private int _statusVersion;
    private string _operationNotificationTitle = string.Empty;
    private string _operationNotificationDetail = string.Empty;
    private string _operationNotificationFootnote = string.Empty;
    private OperationNotificationKind _operationNotificationKind;
    private bool _operationNotificationShowConfigureGit;
    private int _operationNotificationVersion;
    private int _nextTreeNodeId = 1;
    private int _treeGeneration;
    private int _activeDocumentIndex = -1;
    private int _firstVisibleDocumentTabIndex;
    private int _hoveredDocumentTabIndex = -1;
    private int _documentSelectionVersion;
    private int _projectPanelWidth;
    private int _bottomPanelHeight;
    private SplitterKind _activeSplitter;
    private NativeMethods.Point _splitterDragStartPoint;
    private int _splitterDragStartSize;
    private bool _shown;
    private bool _showingGitPanel;
    private bool _showingGitDiff;
    private string? _gitDiffRelativePath;
    private string? _previewGitDiffPath;
    private bool _preserveGitDiffTabsOnHide;
    private bool _projectPanelVisible = true;
    private bool _showingHistoryPanel;
    private bool _showingReferenceComparison;
    private GitComparisonDocument? _referenceComparisonDocument;
    private bool _showingTerminalPanel;
    private bool _saved;
    private bool _treeGitStatusRefreshing;
    private bool _treeGitStatusRefreshPending;
    private bool _openingBranchMenu;
    // 汉堡按钮打开的是标题栏内嵌菜单，不创建悬浮菜单卡片。
    private bool _mainMenuOpen;
    private string _branchLabel = "Git";
    // 工作区内所有 Git 入口共享一次运行时探测，避免重复启动 git.exe 造成界面等待。
    private GitRuntimeInfo? _gitRuntime;
    private Task<GitRuntimeInfo>? _gitRuntimeResolution;
    private bool _gitRuntimeUnavailable;
    private string _gitRuntimeUnavailableReason = UiText.GitUnavailable;
    private bool _gitUnavailableNoticeShown;
    private bool _trackingDocumentTabMouseLeave;
    private bool _documentTabPointerPressed;
    private bool _documentTabPointerReleaseInProgress;
    private DocumentTabState? _documentTabCloseTarget;
    private TransientEditorTab? _transientTabCloseTarget;
    private NativeMethods.Point? _pendingDocumentTabClickPoint;
    private int _activationRedrawCount;
    private int _layoutInvocationCount;
    private CancellationTokenSource? _branchOperationCancellation;
    private bool _disposed;
    // 主窗口销毁由 WM_DESTROY 和 IDisposable 两条路径触发；只允许第一条路径释放资源并退出消息循环。
    private bool _destroying;
    private bool _destroyed;

    private enum OperationNotificationKind
    {
        None,
        Progress,
        Information,
        Error,
    }

    internal MainWindow(
        SettingsStore settingsStore,
        ApplicationSettings settings,
        string? requestedWorkspace = null,
        WorkspaceInstanceCoordinator? ownedCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(settings);
        _settingsStore = settingsStore;
        _settings = settings;
        NativeTheme.ConfigureUiTypography(settings.TextFontFamily, settings.UiFontSize);
        // 创建窗口前先读取系统 DPI，保证初始尺寸、布局和已保存面板尺寸使用同一坐标系。
        NativeTheme.UpdateDpiForWindow(0);
        ToolWindowLayoutSettings toolWindows = settings.ToolWindows ?? new();
        _projectPanelWidth = toolWindows.ProjectPanelWidth is { } projectWidth
            ? S((int)Math.Round(projectWidth))
            : 0;
        _bottomPanelHeight = toolWindows.BottomPanelHeight is { } bottomHeight
            ? S((int)Math.Round(bottomHeight))
            : 0;
        _pendingOwnedCoordinator = ownedCoordinator;
        EnsureWindowClass();
        InitializeCommonControls();

        WindowPlacementSettings placement = settings.Window;
        int x = IsFinite(placement.Left) ? (int)Math.Round(placement.Left!.Value) : NativeMethods.UseDefault;
        int y = IsFinite(placement.Top) ? (int)Math.Round(placement.Top!.Value) : NativeMethods.UseDefault;
        int width = Math.Max(MinimumWidth, S(ToFiniteDimension(placement.Width, 1180)));
        int height = Math.Max(MinimumHeight, S(ToFiniteDimension(placement.Height, 760)));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.AppName,
            (NativeMethods.WindowStyleOverlappedWindow & ~NativeMethods.WindowStyleCaption)
                | NativeMethods.WindowStyleClipChildren,
            x,
            y,
            width,
            height,
            0,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.MainWindowCreateFailed);
        }

        NativeTheme.UpdateDpiForWindow(_handle);
        int scaledWidth = Math.Max(MinimumWidth, S(ToFiniteDimension(placement.Width, 1180)));
        int scaledHeight = Math.Max(MinimumHeight, S(ToFiniteDimension(placement.Height, 760)));
        if (scaledWidth != width || scaledHeight != height)
        {
            _ = NativeMethods.SetWindowPosition(
                _handle,
                0,
                0,
                0,
                scaledWidth,
                scaledHeight,
                NativeMethods.SetWindowPositionNoMove
                    | NativeMethods.SetWindowPositionNoZOrder
                    | NativeMethods.SetWindowPositionNoActivate);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        long currentStyle = NativeMethods.GetWindowLongPointer(_handle, NativeMethods.WindowLongStyle).ToInt64();
        _ = NativeMethods.SetWindowLongPointer(
            _handle,
            NativeMethods.WindowLongStyle,
            unchecked((nint)(currentStyle & ~(long)NativeMethods.WindowStyleCaption)));
        _ = NativeMethods.SetWindowPosition(
            _handle,
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

        SynchronizationContext.SetSynchronizationContext(new WindowSynchronizationContext(this));
        CreateControls();
        CreateToolTips();
        ApplyAppearance();
        Layout();
        SetStatus(UiText.NoWorkspace);
        if (!string.IsNullOrWhiteSpace(requestedWorkspace))
        {
            if (ownedCoordinator is null)
            {
                Post(() => _ = OpenWorkspaceAsync(requestedWorkspace, restoreState: true));
            }
            else
            {
                string? restoredWorkspace = _settings.LastWorkspace;
                _pendingOwnedCoordinator = null;
                PrepareWorkspaceShell(requestedWorkspace, ownedCoordinator);
                bool restoreState = restoredWorkspace?.Equals(requestedWorkspace, StringComparison.OrdinalIgnoreCase) == true;
                if (restoreState)
                {
                    PrepareRestoredDocumentTabs(requestedWorkspace);
                }

                Post(() => _ = CompleteWorkspaceAttachAsync(requestedWorkspace, restoreState));
            }
        }
        else if (ownedCoordinator is not null)
        {
            ownedCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _pendingOwnedCoordinator = null;
        }
    }

    internal nint Handle => _handle;

    internal bool DestroyedForTest => _destroyed;

    internal bool IsVisible => _handle != 0 && NativeMethods.IsWindowVisible(_handle);

    internal string? WorkspaceRoot => _workspaceRoot;

    internal int OpenDocumentCount => _documents.Count(document => !document.IsPreview);

    internal int LoadedDocumentCountForTest => _documents.Count(
        document => !document.IsPreview && document.View is not null);

    internal Task? DocumentReadBarrierForTest { get; set; }
    internal NativeImageDecoder? ImageDecoderForTest { get; set; }

    internal bool ActiveDocumentIsPreviewForTest => _activeDocumentIndex >= 0
        && _activeDocumentIndex < _documents.Count
        && _documents[_activeDocumentIndex].IsPreview;

    internal bool ActiveDocumentTabUsesPreviewTypographyForTest => _activeDocumentIndex >= 0
        && _activeDocumentIndex < _documents.Count
        && DocumentTabFont(_documents[_activeDocumentIndex]) == NativeTheme.UiPreviewFont;

    internal bool IsDocumentLoadedForTest(int index)
    {
        return index >= 0 && index < _documents.Count && _documents[index].View is not null;
    }

    internal bool RestoreLayoutUsedLoadingPlaceholderForTest { get; private set; }

    internal bool RestoreTabsPreparedBeforeShowForTest { get; private set; }

    internal bool ActivationRedrawCompletedForTest { get; private set; }

    internal int ActivationRedrawCountForTest => _activationRedrawCount;

    internal int LayoutInvocationCountForTest => _layoutInvocationCount;

    internal bool OperationNotificationVisibleForTest =>
        _operationNotificationKind != OperationNotificationKind.None
        && NativeMethods.IsWindowVisible(_operationNotification);

    internal nint OperationNotificationHandleForTest =>
        OperationNotificationVisibleForTest ? _operationNotification : 0;

    internal bool RenderOperationNotificationForVisualAudit(nint deviceContext)
    {
        if (deviceContext == 0
            || !OperationNotificationVisibleForTest
            || !NativeMethods.GetWindowRectangle(_handle, out NativeMethods.Rectangle owner)
            || !NativeMethods.GetWindowRectangle(_operationNotification, out NativeMethods.Rectangle notification))
        {
            return false;
        }

        notification.Left -= owner.Left;
        notification.Top -= owner.Top;
        notification.Right -= owner.Left;
        notification.Bottom -= owner.Top;
        DrawOperationNotificationCard(deviceContext, notification, fillWindowBackground: false);

        bool dark = NativeTheme.IsDark(_settings.Theme);
        DrawOperationNotificationButtonForVisualAudit(
            deviceContext,
            owner,
            _operationNotificationCancelButton,
            dark,
            outlined: true);
        DrawOperationNotificationButtonForVisualAudit(
            deviceContext,
            owner,
            _operationNotificationActionButton,
            dark,
            outlined: true);
        return true;
    }

    internal string OperationNotificationTitleForTest => _operationNotificationTitle;

    internal string OperationNotificationDetailForTest => _operationNotificationDetail;

    internal string StatusTextForTest => _statusText;

    internal bool OperationNotificationIsProgressForTest =>
        _operationNotificationKind == OperationNotificationKind.Progress;

    internal bool OperationNotificationCancelVisibleForTest =>
        _operationNotificationKind == OperationNotificationKind.Progress
        && NativeMethods.IsWindowVisible(_operationNotificationCancelButton);

    internal bool OperationNotificationActionVisibleForTest =>
        _operationNotificationShowConfigureGit
        && NativeMethods.IsWindowVisible(_operationNotificationActionButton);

    internal bool OperationNotificationCancelParentedToCardForTest =>
        NativeMethods.GetParent(_operationNotificationCancelButton) == _operationNotification;

    internal bool OperationNotificationCancelWithinCardForTest =>
        NativeMethods.GetWindowRectangle(_operationNotification, out NativeMethods.Rectangle card)
        && NativeMethods.GetWindowRectangle(_operationNotificationCancelButton, out NativeMethods.Rectangle cancel)
        && cancel.Left >= card.Left
        && cancel.Top >= card.Top
        && cancel.Right <= card.Right
        && cancel.Bottom <= card.Bottom;

    internal bool OperationNotificationIsErrorForTest =>
        _operationNotificationKind == OperationNotificationKind.Error;

    internal bool GitRuntimeUnavailableForTest => _gitRuntimeUnavailable;

    internal string GitRuntimeUnavailableReasonForTest => _gitRuntimeUnavailableReason;

    internal bool GitUnavailableNoticeVisibleForTest =>
        _gitUnavailableNoticeShown
        && _operationNotificationKind == OperationNotificationKind.Error
        && NativeMethods.IsWindowVisible(_operationNotification);

    internal bool OperationNotificationIsAboveActiveDocumentForTest
    {
        get
        {
            if (_operationNotificationKind == OperationNotificationKind.None
                || _activeDocumentIndex < 0
                || _activeDocumentIndex >= _documents.Count
                || _documents[_activeDocumentIndex].View is not { } activeView)
            {
                return false;
            }

            return IsChildWindowAbove(_operationNotification, activeView.Handle);
        }
    }

    internal bool OperationNotificationIsAboveEmptyDocumentForTest =>
        _operationNotificationKind != OperationNotificationKind.None
        && NativeMethods.IsWindowVisible(_emptyDocumentLabel)
        && IsChildWindowAbove(_operationNotification, _emptyDocumentLabel);

    internal bool GitNavigationDisabledForTest =>
        _gitRuntimeUnavailable
        && !NativeMethods.IsWindowEnabled(_gitButton)
        && !NativeMethods.IsWindowEnabled(_historyButton);

    internal static uint ToolbarColorForTest(int x)
    {
        return ToolbarColorAt(x);
    }

    internal static int CardRadiusForTest => CardRadius;

    internal static int CardGapForTest => CardGap;

    internal static int TreePanelMaximumWidthForTest => S(360);

    internal static int ToolbarHeightForTest => ToolbarHeight;

    internal static int ActivityBarWidthForTest => ActivityBarWidth;

    internal static int ActivityBarLeftForTest => ActivityBarLeft;

    internal static int MainPanelLeftForTest => MainPanelLeft;

    internal static int TabHeightForTest => TabHeight;

    internal static int StatusHeightForTest => StatusHeight;

    internal static int CalculateDefaultBottomPanelHeightForTest(int contentHeight)
    {
        return CalculateDefaultBottomPanelHeight(contentHeight);
    }

    internal static int DefaultBottomPanelMinimumHeightForTest => DefaultBottomPanelMinimumHeight;

    internal static int CalculateDefaultBottomPanelHeightForTest(int contentHeight, int minimumHeight) =>
        CalculateDefaultBottomPanelHeight(contentHeight, minimumHeight);

    internal static uint TreeSelectionColorForTest(bool dark, bool hasFocus = true)
    {
        return NativeTheme.SelectionColor(NativeTheme.Palette(dark), hasFocus);
    }

    internal static uint TreeFileTextColorForTest(bool dark, GitChangeKind? status)
    {
        return ResolveTreeFileTextColor(NativeTheme.Palette(dark), status, dark);
    }

    internal static int FrameAmbientFadeHeightForTest => FrameAmbientFadeHeight;

    internal static int SurfaceContentInsetForTest => SurfaceContentInset;

    internal int ToolTipCountForTest => _toolTip?.CountForTest ?? 0;

    internal bool ToolTipCreatedForTest => _toolTip?.IsCreatedForTest == true;

    internal string CurrentFileContextForTest => NativeMethods.GetWindowTextValue(_currentFileButton);

    internal int ProjectPanelWidthForTest => NativeMethods.GetWindowRectangle(
        _projectHeader,
        out NativeMethods.Rectangle rectangle)
            ? rectangle.Right - rectangle.Left
            : 0;

    internal int BottomPanelHeightForTest
    {
        get
        {
            if ((!_showingHistoryPanel && !_showingTerminalPanel)
                || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle rectangle))
            {
                return 0;
            }

            int contentHeight = Math.Max(
                0,
                rectangle.Bottom - rectangle.Top - ToolbarHeight - StatusHeight);
            return Math.Max(0, GetBottomPanelHeight(contentHeight) - SurfaceContentInset * 2);
        }
    }

    internal void ResizeProjectPanelForTest(int width)
    {
        _projectPanelWidth = width;
        _saved = false;
        Layout();
    }

    internal bool DragProjectSplitterForTest(int delta)
    {
        int before = ProjectPanelWidthForTest;
        int x = MainPanelLeft + before + CardGap / 2;
        int y = ToolbarHeight + S(80);
        DragSplitterForTest(x, y, x + delta, y);
        return ProjectPanelWidthForTest != before;
    }

    internal void ResizeBottomPanelForTest(int height)
    {
        _bottomPanelHeight = height;
        _saved = false;
        Layout();
    }

    internal bool DragBottomSplitterForTest(int delta)
    {
        if (!NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return false;
        }

        int before = BottomPanelHeightForTest;
        int width = Math.Max(0, client.Right - client.Left);
        bool sidePanelVisible = _showingGitPanel || _projectPanelVisible;
        int documentLeft = MainPanelLeft
            + (sidePanelVisible ? GetTreePanelWidth(width) + CardGap : FrameInset);
        int x = documentLeft + S(100);
        int y = client.Bottom - StatusHeight - before - CardGap / 2;
        DragSplitterForTest(x, y, x, y - delta);
        return BottomPanelHeightForTest != before;
    }

    private void DragSplitterForTest(int startX, int startY, int endX, int endY)
    {
        _ = NativeMethods.SendMessage(
            _handle,
            NativeMethods.WindowMessageLeftButtonDown,
            1,
            PackClientPoint(startX, startY));
        _ = NativeMethods.SendMessage(
            _handle,
            NativeMethods.WindowMessageMouseMove,
            1,
            PackClientPoint(endX, endY));
        _ = NativeMethods.SendMessage(
            _handle,
            NativeMethods.WindowMessageLeftButtonUp,
            0,
            PackClientPoint(endX, endY));
    }

    private static nint PackClientPoint(int x, int y)
    {
        return unchecked((nint)((ushort)x | (uint)(ushort)y << 16));
    }

    internal static uint FrameAmbientColorForTest(int x, int y)
    {
        return FrameAmbientColorAt(x, y);
    }

    internal int VisibleDocumentTabCloseCountForTest => Enumerable.Range(0, _documents.Count)
        .Count(ShouldShowDocumentTabClose);

    internal static IReadOnlyList<string> ProjectContextMenuLabelsForTest(bool directory)
    {
        List<string> labels =
        [
            UiText.CopyPath,
            UiText.RevealInExplorer,
            UiText.OpenExternalTerminal,
            UiText.Refresh,
        ];
        if (!directory)
        {
            labels.Add(UiText.FileHistory);
            labels.Add(UiText.Blame);
        }

        return labels;
    }

    internal bool ActiveDocumentTabVisibleForTest => NativeMethods.GetClientRectangle(
        _documentTabs,
        out NativeMethods.Rectangle rectangle)
            && CalculateDocumentTabRectangles(
                    rectangle.Right - rectangle.Left,
                    GetDocumentTabRightReserve())
                .Any(tab => tab.DocumentIndex == _activeDocumentIndex);

    internal bool DocumentTabsUseIdeaMouseInputForTest => _documentTabs != 0;

    internal bool HoverDocumentTabForTest(int index)
    {
        if (!NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client))
        {
            return false;
        }

        DocumentTabLayout? layout = CalculateDocumentTabRectangles(
                client.Right - client.Left,
                GetDocumentTabRightReserve())
            .FirstOrDefault(candidate => candidate.DocumentIndex == index);
        if (layout is null)
        {
            return false;
        }

        NativeMethods.Rectangle rectangle = layout.Value.Rectangle;
        _ = NativeMethods.SendMessage(
            _documentTabs,
            NativeMethods.WindowMessageMouseMove,
            0,
            PackClientPoint(
                (rectangle.Left + rectangle.Right) / 2,
                (rectangle.Top + rectangle.Bottom) / 2));
        return _hoveredDocumentTabIndex == index;
    }

    internal void LeaveDocumentTabsForTest()
    {
        _ = NativeMethods.SendMessage(
            _documentTabs,
            NativeMethods.WindowMessageMouseLeave,
            0,
            0);
    }

    internal bool MiddleClickDocumentTabForTest(int index)
    {
        if (index < 0
            || index >= _documents.Count
            || !NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client))
        {
            return false;
        }

        DocumentTabLayout? layout = CalculateDocumentTabRectangles(
                client.Right - client.Left,
                GetDocumentTabRightReserve())
            .FirstOrDefault(candidate => candidate.DocumentIndex == index);
        if (layout is null)
        {
            return false;
        }

        int previousCount = _documents.Count;
        NativeMethods.Rectangle rectangle = layout.Value.Rectangle;
        _ = NativeMethods.SendMessage(
            _documentTabs,
            NativeMethods.WindowMessageMiddleButtonUp,
            0,
            PackClientPoint(
                (rectangle.Left + rectangle.Right) / 2,
                (rectangle.Top + rectangle.Bottom) / 2));
        return _documents.Count == previousCount - 1;
    }

    internal bool ClickDocumentTabCloseForTest(int index, Action? whilePressed = null,
        bool releaseOutside = false, bool cancelCapture = false)
    {
        if (index < 0 || index >= _documents.Count
            || !NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client))
        {
            return false;
        }
        DocumentTabLayout? layout = CalculateDocumentTabRectangles(
                client.Right - client.Left, GetDocumentTabRightReserve())
            .FirstOrDefault(candidate => candidate.DocumentIndex == index);
        if (layout is null) return false;
        NativeMethods.Rectangle rectangle = layout.Value.Rectangle;
        nint point = PackClientPoint(rectangle.Right - S(14), (rectangle.Top + rectangle.Bottom) / 2);
        int previousCount = _documents.Count;
        _ = NativeMethods.SendMessage(_documentTabs, NativeMethods.WindowMessageMouseMove, 0, point);
        _ = NativeMethods.SendMessage(_documentTabs, NativeMethods.WindowMessageLeftButtonDown, 1, point);
        try
        {
            whilePressed?.Invoke();
            if (cancelCapture) _ = NativeMethods.ReleaseCapture();
            _ = NativeMethods.SendMessage(_documentTabs, NativeMethods.WindowMessageLeftButtonUp,
                0, releaseOutside ? PackClientPoint(-10, -10) : point);
        }
        finally
        {
            if (NativeMethods.GetCapture() == _documentTabs) _ = NativeMethods.ReleaseCapture();
        }
        return _documents.Count == previousCount - 1;
    }

    internal async Task<bool> LeftClickDocumentTabForTestAsync(int index)
    {
        if (index < 0
            || index >= _documents.Count
            || !NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client))
        {
            return false;
        }

        DocumentTabLayout? layout = CalculateDocumentTabRectangles(
                client.Right - client.Left,
                GetDocumentTabRightReserve())
            .FirstOrDefault(candidate => candidate.DocumentIndex == index);
        if (layout is null)
        {
            return false;
        }

        NativeMethods.Rectangle rectangle = layout.Value.Rectangle;
        NativeMethods.Point clientPoint = new()
        {
            X = Math.Min(rectangle.Right - S(32), rectangle.Left + S(18)),
            Y = (rectangle.Top + rectangle.Bottom) / 2,
        };
        if (clientPoint.X <= rectangle.Left)
        {
            clientPoint.X = (rectangle.Left + rectangle.Right) / 2;
        }

        DocumentTabState expected = _documents[index];
        try
        {
            nint packedPoint = PackClientPoint(clientPoint.X, clientPoint.Y);
            _ = NativeMethods.SendMessage(
                _documentTabs,
                NativeMethods.WindowMessageLeftButtonDown,
                1,
                packedPoint);
            _ = NativeMethods.SendMessage(
                _documentTabs,
                NativeMethods.WindowMessageLeftButtonUp,
                0,
                packedPoint);

            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while ((_activeDocumentIndex < 0
                    || _activeDocumentIndex >= _documents.Count
                    || !ReferenceEquals(_documents[_activeDocumentIndex], expected)
                    || expected.View is null)
                && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            return _activeDocumentIndex >= 0
                && _activeDocumentIndex < _documents.Count
                && ReferenceEquals(_documents[_activeDocumentIndex], expected)
                && expected.View is not null;
        }
        finally
        {
            _ = NativeMethods.SendMessage(
                _documentTabs,
                NativeMethods.WindowMessageLeftButtonUp,
                0,
                0);
        }
    }

    internal void CloseOtherDocumentsForTest(int keptIndex)
    {
        CloseOtherDocuments(keptIndex);
    }

    internal void CloseAllDocumentsForTest()
    {
        CloseAllDocuments();
    }

    internal bool HandleApplicationShortcutForTest(int key, bool control = false, bool shift = false)
    {
        return HandleApplicationShortcut(key, control, shift);
    }

    internal bool HandleTabNavigationForTest(bool backwards = false)
    {
        return HandleTabNavigation(backwards);
    }

    internal bool FocusDocumentTabsForTest()
    {
        if (_documentTabs == 0 || !NativeMethods.IsWindow(_documentTabs))
        {
            return false;
        }

        _ = NativeMethods.SetFocus(_documentTabs);
        return NativeMethods.GetFocus() == _documentTabs;
    }

    internal bool HandleDocumentTabsShortcutForTest(int key)
    {
        return HandleDocumentTabsShortcut(key);
    }

    internal bool ProjectTreeHasFocusForTest => NativeMethods.GetFocus() == _fileTree;

    internal bool ProjectLocateActionHasFocusForTest => NativeMethods.GetFocus() == _locateActiveFileButton;

    internal bool ProjectCollapseActionHasFocusForTest => NativeMethods.GetFocus() == _collapseTreeButton;

    internal bool GitChangesListHasFocusForTest => _gitPanel?.ChangesListHasFocusForTest == true;

    internal bool GitAmendActionHasFocusForTest => _gitPanel?.AmendActionHasFocusForTest == true;

    internal bool GitCommitMessageHasFocusForTest => _gitPanel?.CommitMessageHasFocusForTest == true;

    internal bool HistoryBranchFilterHasFocusForTest => _historyPanel?.BranchFilterHasFocusForTest == true;

    internal bool HistoryListHasFocusForTest => _historyPanel?.HistoryListHasFocusForTest == true;

    internal bool HistoryBranchesListHasFocusForTest => _historyPanel?.BranchesListHasFocusForTest == true;

    internal bool HistoryFilterHasFocusForTest => _historyPanel?.FilterHasFocusForTest == true;

    internal bool SearchResultListHasFocusForTest => _searchPanel?.ResultListHasFocusForTest == true;

    internal bool ActiveFindEditHasFocusForTest => ActiveDocument?.FindEditHasFocusForTest == true;

    internal bool ActiveFindMatchCaseHasFocusForTest => ActiveDocument?.FindMatchCaseHasFocusForTest == true;

    internal bool ActiveFindWholeWordHasFocusForTest => ActiveDocument?.FindWholeWordHasFocusForTest == true;

    internal bool ActiveFindPreviousHasFocusForTest => ActiveDocument?.FindPreviousHasFocusForTest == true;

    internal bool ActiveFindNextHasFocusForTest => ActiveDocument?.FindNextHasFocusForTest == true;

    internal bool ActiveFindCloseHasFocusForTest => ActiveDocument?.FindCloseHasFocusForTest == true;

    internal bool ActiveDocumentTabsHasFocusForTest => NativeMethods.GetFocus() == _documentTabs;

    internal bool ActiveWordWrapHasFocusForTest => ActiveDocument?.WordWrapHasFocusForTest == true;

    internal bool ActiveWhitespaceHasFocusForTest => ActiveDocument?.WhitespaceHasFocusForTest == true;

    internal bool ActiveFindButtonHasFocusForTest => ActiveDocument?.FindButtonHasFocusForTest == true;

    internal string ActiveNavigationFocusForTest => ActiveDocument?.NavigationFocusForTest ?? "无活动文档";

    internal string ActiveNavigationControlsStateForTest => ActiveDocument?.NavigationControlsStateForTest ?? "无活动文档";

    internal bool DocumentTabsUseOwnerDrawForTest => _documentTabs != 0
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
                _documentTabs,
                NativeMethods.WindowLongStyle).ToInt64())
            & NativeMethods.ButtonOwnerDraw) == NativeMethods.ButtonOwnerDraw;

    internal bool ActiveMarkdownPreviewReady => ActiveDocument?.IsMarkdownPreviewReady == true;

    internal bool ActiveMarkdownPreviewLoading => ActiveDocument?.IsMarkdownPreviewLoading == true;

    internal string? ActiveMarkdownPreviewError => ActiveDocument?.MarkdownPreviewError;

    internal int ActiveMarkdownBrowserProcessId => ActiveDocument?.MarkdownBrowserProcessId ?? 0;

    internal bool ActiveImagePreviewReady => ActiveDocument?.IsImagePreviewReady == true;

    internal int ActiveImageZoomPercentageForTest => ActiveDocument?.ImageZoomPercentageForTest ?? 0;

    internal bool ActiveImageFitToAreaForTest => ActiveDocument?.ImageFitToAreaForTest == true;

    internal bool ActiveImageToolbarWithinBoundsForTest =>
        ActiveDocument?.ImageToolbarWithinClientBoundsForTest == true;

    internal bool ActiveInfoPageHasSinglePrimaryActionForTest =>
        ActiveDocument?.InfoPageHasSinglePrimaryActionForTest == true;

    internal bool ActiveInfoPageActionWithinBoundsForTest =>
        ActiveDocument?.InfoPageActionWithinBoundsForTest == true;

    internal bool ActiveInfoPageActionHasFocusForTest =>
        ActiveDocument?.InfoPageActionHasFocusForTest == true;

    internal bool ZoomActiveImageInForTest()
    {
        return ActiveDocument?.ZoomImageInForTest() == true;
    }

    internal void FitActiveImageToAreaForTest()
    {
        ActiveDocument?.FitImageToAreaForTest();
    }

    internal string? ActiveDocumentText => ActiveDocument?.CurrentText;

    internal bool ActiveDocumentIsReadOnly => ActiveDocument?.IsTextReadOnly != false;

    internal bool ActiveDocumentIsShowingBlameForTest => ActiveDocument?.IsShowingBlame == true;

    internal Task ShowBlameForTestAsync(string fullPath, IReadOnlyList<GitBlameLine> lines)
    {
        if (_workspaceRoot is null)
        {
            return Task.CompletedTask;
        }

        string relativePath = Path.GetRelativePath(_workspaceRoot, fullPath).Replace('\\', '/');
        return ShowBlameAsync(relativePath, lines);
    }

    internal bool ActiveDocumentHasFocusForTest
    {
        get
        {
            NativeDocumentView? document = ActiveDocument;
            nint focus = NativeMethods.GetFocus();
            return document is not null
                && (focus == document.Handle || NativeMethods.IsChild(document.Handle, focus));
        }
    }

    internal bool ProjectSurfaceHasFocusForTest => ActiveDocument is not null
        ? ActiveDocumentHasFocusForTest
        : NativeMethods.GetFocus() == _fileTree;

    internal bool ActiveDocumentVisibleForTest
    {
        get
        {
            if (ActiveDocument is null || !NativeMethods.IsWindowVisible(ActiveDocument.Handle))
            {
                return false;
            }

            return NativeMethods.GetWindowRectangle(ActiveDocument.Handle, out NativeMethods.Rectangle rectangle)
                && rectangle.Right > rectangle.Left
                && rectangle.Bottom > rectangle.Top;
        }
    }

    internal string? ActiveDocumentPathForTest => ActiveDocument?.Path;

    internal bool EmptyDocumentVisibleForTest => NativeMethods.IsWindowVisible(_emptyDocumentLabel);

    internal string EmptyDocumentTextForTest => NativeMethods.GetWindowTextValue(_emptyDocumentLabel);

    internal string CurrentBranchLabelForTest => NativeMethods.GetWindowTextValue(_recentWorkspacesButton);

    internal Augit.Core.Documents.DocumentReadStatus? ActiveDocumentStatus => ActiveDocument?.Status;

    internal Augit.Core.Documents.DocumentKind? ActiveDocumentKind => ActiveDocument?.Kind;

    internal bool ActiveDocumentIsShowingAlternative => ActiveDocument?.IsShowingAlternative == true;

    internal bool ActiveDocumentModeToolbarWithinBoundsForTest =>
        ActiveDocument?.ModeToolbarWithinClientBoundsForTest == true;

    internal bool ActiveDocumentTextToolbarWithinBoundsForTest =>
        ActiveDocument?.TextToolbarWithinClientBoundsForTest == true;

    internal bool ActiveDocumentFindOverlayVisibleForTest =>
        ActiveDocument?.FindOverlayVisibleForTest == true;

    internal bool ActiveDocumentFindOverlayWithinBoundsForTest =>
        ActiveDocument?.FindOverlayWithinClientBoundsForTest == true;

    internal bool ActiveDocumentToolTipsCreatedForTest =>
        ActiveDocument?.DocumentToolTipsCreatedForTest == true;

    internal int ActiveDocumentContentTopForTest => ActiveDocument?.ContentTopForTest ?? -1;

    internal string? ActiveDocumentFindStatusForTest => ActiveDocument?.FindStatusForTest;
    internal NativeDocumentView? ActiveDocumentViewForTest => ActiveDocument;

    internal void ShowActiveDocumentFindForTest(string query)
    {
        ActiveDocument?.ShowFindForTest(query);
    }

    internal Task<int> ShowActiveMarkdownPreviewForTestAsync()
    {
        return ActiveDocument?.ShowMarkdownPreviewForTestAsync() ?? Task.FromResult(0);
    }

    internal Task<int> ShowActiveMarkdownSplitForTestAsync()
    {
        return ActiveDocument?.ShowMarkdownSplitForTestAsync() ?? Task.FromResult(0);
    }

    internal bool DragActiveMarkdownSplitterForTest(int delta)
    {
        return ActiveDocument?.DragMarkdownSplitterForTest(delta) == true;
    }

    internal int CloseActiveMarkdownPreviewForTest()
    {
        return ActiveDocument?.CloseMarkdownPreviewForTest() ?? 0;
    }

    internal int SearchResultCountForTest => _searchPanel?.ResultCount ?? 0;

    internal bool PreviewFirstSearchResultForTest() =>
        _searchPanel?.PreviewFirstResultWithClickForTest() == true;

    internal bool PreviewSearchResultForTest(int index) =>
        _searchPanel?.PreviewResultWithClickForTest(index) == true;

    internal string? SearchResultPathForTest(int index) => _searchPanel?.ResultPathForTest(index);

    internal int PreviewDocumentCountForTest => _documents.Count(document => document.IsPreview);

    internal bool SearchCompletedForTest => _searchPanel?.SearchCompletedForTest == true;

    internal int SearchResultListDeltaCountForTest => _searchPanel?.ResultListDeltaCountForTest ?? 0;

    internal bool SearchPanelVisibleForTest => _searchPanel is not null
        && NativeMethods.IsWindowVisible(_searchPanel.Handle)
        && NativeMethods.GetWindowRectangle(_searchPanel.Handle, out NativeMethods.Rectangle search)
        && search.Right > search.Left
        && search.Bottom > search.Top;

    internal bool SearchPanelUsesRoundedChromeForTest => _searchPanel?.UsesRoundedChromeForTest == true;

    internal int SearchPanelLogicalWidthForTest => _searchPanel?.LogicalWidthForTest ?? 0;

    internal int SearchPanelPreferredHeightForTest => _searchPanel is { } panel
        ? (int)Math.Round(NativeTheme.Unscale(panel.PreferredHeightForTest))
        : 0;

    internal bool SearchPanelShortcutVisibleForTest => _searchPanel?.ShortcutVisibleForTest == true;

    internal bool SearchPanelCloseButtonVisibleForTest => _searchPanel?.CloseButtonVisibleForTest == true;

    internal bool SearchPanelNoticeVisibleForTest => _searchPanel?.NoticeVisibleForTest == true;

    internal bool SearchPanelUsesSingleLineResultsForTest => _searchPanel?.UsesSingleLineResultsForTest == true;

    internal string SearchPanelPlaceholderForTest => _searchPanel?.PlaceholderForTest ?? string.Empty;

    internal bool SearchPanelUsesFocusedSearchFieldChromeForTest =>
        _searchPanel?.UsesFocusedSearchFieldChromeForTest == true;

    internal bool GitPanelVisibleForTest => _showingGitPanel;

    internal bool GitDiffUsesSeparateWindowForTest => _gitPanel is not null
        && _gitPanel.DiffHandleForTest != 0
        && _gitPanel.DiffHandleForTest != _gitPanel.Handle;

    internal bool DocumentTabsVisibleForTest => _documentTabs != 0
        && NativeMethods.IsWindowVisible(_documentTabs);

    internal nint DocumentTabsHandleForTest => _documentTabs;

    internal bool ProjectNavigationActiveForTest => !_showingGitPanel
        && _searchPanel?.Mode != WorkspaceSearchMode.Text
        && _projectPanelVisible;

    internal bool SearchNavigationActiveForTest => _searchPanel?.Mode == WorkspaceSearchMode.Text;

    internal bool GitNavigationActiveForTest => _showingGitPanel
        && _searchPanel?.Mode != WorkspaceSearchMode.Text;

    internal bool BranchSwitchCancelActionCreatedForTest => _cancelBranchButton != 0;

    internal bool BranchSwitchCancelActionVisibleForTest => _cancelBranchButton != 0
        && NativeMethods.IsWindowVisible(_cancelBranchButton);

    internal NativeBranchPopup? BranchPopupForTest => _branchPopup;

    internal int GitChangedFileCountForTest => _gitPanel?.ChangedFileCount ?? 0;

    internal int GitSelectedFileCountForTest => _gitPanel?.SelectedFileCountForTest ?? 0;

    internal string? GitSelectedChangedFilePathForTest => _gitPanel?.SelectedChangedFilePathForTest;

    internal bool CommitActionEnabledForTest => _gitPanel?.CommitActionEnabledForTest == true;

    internal string GitCommitChangeCountForTest => _gitPanel?.CommitChangeCountForTest ?? string.Empty;

    internal int GitChangesPopulateCountForTest => _gitPanel?.ChangesPopulateCountForTest ?? 0;

    internal int GitChangesListResetCountForTest => _gitPanel?.ChangesListResetCountForTest ?? 0;

    internal int GitChangesListDeltaCountForTest => _gitPanel?.ChangesListDeltaCountForTest ?? 0;

    internal int GitDiffRequestCountForTest => _gitPanel?.DiffRequestCountForTest ?? 0;

    internal int GitDiffPresentationNotificationCountForTest => _gitPanel?.DiffPresentationNotificationCountForTest ?? 0;

    internal int GitPanelLayoutInvocationCountForTest => _gitPanel?.LayoutInvocationCountForTest ?? 0;

    internal NativeGitDiffGeometrySnapshot GitDiffGeometryForTest => new(
        CaptureWindowBoundsForTest(_documentTabs),
        CaptureWindowBoundsForTest(_gitPanel?.Handle ?? 0),
        CaptureWindowBoundsForTest(_gitPanel?.DiffHandleForTest ?? 0));

    internal bool GitRefreshingForTest => _gitPanel?.RefreshingForTest == true;

    internal bool GitRuntimeAvailableForTest => _gitPanel?.IsRuntimeAvailable == true;

    internal GitRepositoryKind? GitRepositoryKindForTest => _gitPanel?.RepositoryKind;

    internal int HistoryEntryCountForTest => _historyPanel?.EntryCount ?? 0;

    internal bool HistoryRuntimeAvailableForTest => _historyPanel?.IsRuntimeAvailable == true;

    internal GitRepositoryKind? HistoryRepositoryKindForTest => _historyPanel?.RepositoryKind;

    internal NativeGitHistoryPanel? HistoryPanelForTest => _historyPanel;

    internal NativeGitPanel? GitPanelForTest => _gitPanel;

    internal bool FileTreeVisibleForTest => _fileTree != 0 && NativeMethods.IsWindowVisible(_fileTree);

    internal nint FileTreeHandleForTest => _fileTree;

    internal bool ProjectPanelVisibleForTest => _projectPanelVisible
        && _projectHeader != 0
        && NativeMethods.IsWindowVisible(_projectHeader)
        && FileTreeVisibleForTest;

    internal int DocumentTabsLeftForTest => NativeMethods.GetWindowRectangle(
        _documentTabs,
        out NativeMethods.Rectangle rectangle)
            ? rectangle.Left
            : -1;

    internal int FileTreeItemHeightForTest => unchecked((int)NativeMethods.SendMessage(
        _fileTree,
        NativeMethods.TreeViewGetItemHeight,
        0,
        0));

    internal GitChangeKind? GetTreeGitStatusForTest(string fullPath)
    {
        return _treeGitStatusIndex?.Resolve(fullPath);
    }

    internal bool HistoryPanelVisibleForTest => _historyPanel is not null
        && NativeMethods.IsWindowVisible(_historyPanel.Handle);

    internal bool HistoryPanelIsBelowDocumentForTest
    {
        get
        {
            return _historyPanel is not null
                && NativeMethods.GetWindowRectangle(_documentTabs, out NativeMethods.Rectangle document)
                && NativeMethods.GetWindowRectangle(_historyPanel.Handle, out NativeMethods.Rectangle history)
                && history.Top >= document.Bottom;
        }
    }

    internal bool BottomPanelStartsAtEditorForTest
    {
        get
        {
            nint bottomPanel = _showingHistoryPanel
                ? _historyPanel?.Handle ?? 0
                : _showingTerminalPanel
                    ? _terminalPanel?.Handle ?? 0
                    : 0;
            return bottomPanel != 0
                && NativeMethods.GetWindowRectangle(_documentTabs, out NativeMethods.Rectangle document)
                && NativeMethods.GetWindowRectangle(bottomPanel, out NativeMethods.Rectangle bottom)
                && bottom.Left
                    - (_showingTerminalPanel ? NativeTheme.Scale(8) : SurfaceContentInset)
                    == document.Left;
        }
    }

    internal bool BottomPanelIsAboveEditorForTest
    {
        get
        {
            nint bottomPanel = _showingHistoryPanel
                ? _historyPanel?.Handle ?? 0
                : _showingTerminalPanel
                    ? _terminalPanel?.Handle ?? 0
                    : 0;
            if (bottomPanel == 0 || _documentTabs == 0)
            {
                return false;
            }

            nint sibling = NativeMethods.GetWindowSibling(
                _documentTabs,
                NativeMethods.WindowGetPrevious);
            while (sibling != 0)
            {
                if (sibling == bottomPanel)
                {
                    return true;
                }

                sibling = NativeMethods.GetWindowSibling(
                    sibling,
                    NativeMethods.WindowGetPrevious);
            }

            return false;
        }
    }

    internal bool ProjectPanelKeepsFullHeightForTest
    {
        get
        {
            return NativeMethods.GetWindowRectangle(_fileTree, out NativeMethods.Rectangle tree)
                && NativeMethods.GetWindowRectangle(_statusBar, out NativeMethods.Rectangle status)
                && tree.Bottom + SurfaceContentInset == status.Top;
        }
    }

    internal bool GitHistoryButtonIsAtBottomForTest
    {
        get
        {
            return NativeMethods.GetWindowTextValue(_historyButton) == UiText.GitSymbol
                && NativeMethods.GetWindowRectangle(_gitButton, out NativeMethods.Rectangle commit)
                && NativeMethods.GetWindowRectangle(_terminalButton, out NativeMethods.Rectangle terminal)
                && NativeMethods.GetWindowRectangle(_historyButton, out NativeMethods.Rectangle history)
                && history.Top > commit.Top
                && history.Top > terminal.Top;
        }
    }

    internal bool RollbackButtonCreatedForTest => _gitPanel?.RollbackButtonCreatedForTest == true;

    internal bool AdvancedOperationsButtonCreatedForTest => _gitPanel?.AdvancedOperationsButtonCreatedForTest == true;

    internal bool CommitPanelTitleCreatedForTest => _gitPanel?.CommitPanelTitleCreatedForTest == true;

    internal bool CommitHeaderActionsCreatedForTest => _gitPanel?.HeaderActionsCreatedForTest == true;

    internal bool CommitToolbarActionsCreatedForTest => _gitPanel?.CommitToolbarActionsCreatedForTest == true;

    internal bool CommitToolbarToolTipsCreatedForTest => _gitPanel?.CommitToolbarToolTipsCreatedForTest == true;

    internal (bool Refresh, bool Rollback, bool ShowDiff, bool ExpandAll, bool Preview)
        CommitToolbarEnabledStateForTest => _gitPanel?.CommitToolbarEnabledStateForTest
            ?? (false, false, false, false, false);

    internal bool ProjectHeaderActionsCreatedForTest => _locateActiveFileButton != 0
        && _collapseTreeButton != 0
        && _treeOptionsButton != 0
        && _hideProjectButton != 0
        && NativeMethods.IsWindowVisible(_locateActiveFileButton)
        && NativeMethods.IsWindowVisible(_collapseTreeButton)
        && NativeMethods.IsWindowVisible(_treeOptionsButton)
        && NativeMethods.IsWindowVisible(_hideProjectButton);

    internal bool TopBarProductHierarchyForTest => _applicationIcon != 0
        && NativeMethods.GetWindowRectangle(_applicationIcon, out NativeMethods.Rectangle applicationIcon)
        && NativeMethods.GetWindowRectangle(_openFolderButton, out NativeMethods.Rectangle mainMenu)
        && NativeMethods.GetWindowRectangle(_cloneButton, out NativeMethods.Rectangle product)
        && NativeMethods.GetWindowRectangle(_recentWorkspacesButton, out NativeMethods.Rectangle branch)
        && applicationIcon.Left < mainMenu.Left
        && mainMenu.Left < product.Left
        && product.Right < branch.Left;

    internal bool TopBarControlsVerticallyAlignedForTest => _applicationIcon != 0
        && NativeMethods.GetWindowRectangle(_applicationIcon, out NativeMethods.Rectangle applicationIcon)
        && NativeMethods.GetWindowRectangle(_openFolderButton, out NativeMethods.Rectangle mainMenu)
        && NativeMethods.GetWindowRectangle(_cloneButton, out NativeMethods.Rectangle product)
        && NativeMethods.GetWindowRectangle(_recentWorkspacesButton, out NativeMethods.Rectangle branch)
        && new[]
            {
                applicationIcon.Top + applicationIcon.Bottom,
                mainMenu.Top + mainMenu.Bottom,
                product.Top + product.Bottom,
                branch.Top + branch.Bottom,
            }
            is int[] centers
        && centers.Max() - centers.Min() <= S(2);

    internal bool MainSurfaceSpacingMatchesForTest => _projectHeader != 0
        && NativeMethods.GetWindowRectangle(_projectHeader, out NativeMethods.Rectangle project)
        && NativeMethods.GetWindowRectangle(_documentTabs, out NativeMethods.Rectangle document)
        && document.Left - project.Right == CardGap;

    internal int ExpandedDirectoryCountForTest => _treeNodes.Values.Count(
        node => node.IsDirectory && node.IsExpanded);

    internal bool CommitActionsVisibleForTest => _gitPanel?.CommitActionsVisibleForTest == true;

    internal int CommitAndPushButtonWidthForTest => _gitPanel?.CommitAndPushButtonWidthForTest ?? 0;

    internal bool GitRemoteActionsUseOverflowForTest => _gitPanel?.RemoteActionsUseOverflowForTest == true;

    internal bool GitDiffToolbarActionsCreatedForTest => _gitPanel?.DiffToolbarActionsCreatedForTest == true;

    internal bool GitDiffSurfaceOrderForTest => _gitPanel?.DiffSurfaceOrderForTest == true;

    internal bool GitDiffSettingsButtonUsesIconForTest => _gitPanel?.DiffSettingsButtonUsesIconForTest == true;

    internal bool GitPullModeUsesOwnerDrawForTest => _gitPanel?.PullModeUsesOwnerDrawForTest == true;

    internal bool ChangesListUsesOwnerDrawForTest => _gitPanel?.ChangesListUsesOwnerDrawForTest == true;

    internal bool ChangesListUsesIdeaInputForTest => _gitPanel?.ChangesListUsesIdeaInputForTest == true;

    internal int VisibleGitChangeEntryCountForTest => _gitPanel?.VisibleChangeEntryCountForTest ?? 0;

    internal bool CommitMessageHidesPermanentScrollBarForTest => _gitPanel?.CommitMessageHidesPermanentScrollBarForTest == true;

    internal bool GitDiffUsesSingleMarginForTest => _gitPanel?.DiffUsesSingleMarginForTest == true;

    internal bool GitDiffUsesSideBySideForTest => _gitPanel?.DiffUsesSideBySideForTest == true;

    internal bool GitDiffUsesCentralGutterForTest => _gitPanel?.DiffUsesCentralGutterForTest == true;

    internal uint? ActiveDocumentTabStatusColorForTest
    {
        get
        {
            if (_activeDocumentIndex < 0 || _activeDocumentIndex >= _documents.Count)
            {
                return null;
            }

            GitChangeKind? status = _treeGitStatusIndex?.Resolve(_documents[_activeDocumentIndex].Path);
            return status is null
                ? null
                : NativeGitStatusPalette.Resolve(status.Value, NativeTheme.IsDark(_settings.Theme));
        }
    }

    internal string? SelectedTreePathForTest => GetSelectedTreeNode()?.FullPath;

    internal bool SelectedTreeNodeLoadedForTest => GetSelectedTreeNode()?.IsLoaded == true;

    internal bool SelectTreePathForTest(string path)
    {
        TreeNodeState? node = _treeNodes.Values.FirstOrDefault(
            candidate => candidate.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            return false;
        }

        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSelectItem,
            NativeMethods.TreeViewCaret,
            node.ItemHandle);
        return true;
    }

    internal async Task<bool> ClickTreePathForTestAsync(string path)
    {
        TreeNodeState? node = _treeNodes.Values.FirstOrDefault(
            candidate => candidate.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (node is null
            || !TryGetTreeItemRectangle(node.ItemHandle, out NativeMethods.Rectangle rectangle)
            || !NativeMethods.GetCursorPosition(out NativeMethods.Point originalCursor))
        {
            return false;
        }

        NativeMethods.Point point = new()
        {
            X = Math.Max(rectangle.Left + S(4), GetTreeChevronLeft(GetTreeLevel(node)) + S(28)),
            Y = (rectangle.Top + rectangle.Bottom) / 2,
        };
        NativeMethods.Point screenPoint = point;
        if (!NativeMethods.ClientToScreen(_fileTree, ref screenPoint)
            || !NativeMethods.SetCursorPosition(screenPoint.X, screenPoint.Y))
        {
            return false;
        }

        try
        {
            nint packedPoint = PackClientPoint(point.X, point.Y);
            if (!NativeMethods.PostMessage(_fileTree, NativeMethods.WindowMessageLeftButtonDown, 1, packedPoint)
                || !NativeMethods.PostMessage(_fileTree, NativeMethods.WindowMessageLeftButtonUp, 0, packedPoint))
            {
                return false;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (GetSelectedTreeNode() != node && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            return GetSelectedTreeNode() == node;
        }
        finally
        {
            _ = NativeMethods.PostMessage(_fileTree, NativeMethods.WindowMessageLeftButtonUp, 0, 0);
            _ = NativeMethods.SetCursorPosition(originalCursor.X, originalCursor.Y);
        }
    }

    internal async Task<bool> ActivateSelectedTreeNodeWithEnterForTestAsync()
    {
        TreeNodeState? node = GetSelectedTreeNode();
        if (node is null)
        {
            return false;
        }

        _ = NativeMethods.SetFocus(_fileTree);
        bool wasExpanded = IsTreeItemExpanded(node.ItemHandle);
        node.IsExpanded = wasExpanded;
        int previousDocumentCount = _documents.Count;
        if (!HandleApplicationShortcut(NativeMethods.VirtualKeyEnter, control: false, shift: false))
        {
            return false;
        }

        if (node.IsDirectory)
        {
            return await WaitForTreeToggleAsync(node, wasExpanded);
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while ((_documents.Count == previousDocumentCount
                || _activeDocumentIndex < 0
                || _activeDocumentIndex >= _documents.Count
                || !_documents[_activeDocumentIndex].Path.Equals(node.FullPath, StringComparison.OrdinalIgnoreCase))
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        return _activeDocumentIndex >= 0
            && _activeDocumentIndex < _documents.Count
            && _documents[_activeDocumentIndex].Path.Equals(node.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    internal Task OpenSelectedTreeNodeForTestAsync()
    {
        TreeNodeState? node = GetSelectedTreeNode();
        return node is null ? Task.CompletedTask : OpenTreeNodeAsync(node);
    }

    internal async Task<bool> ClickSelectedTreeDisclosureForTestAsync()
    {
        TreeNodeState? node = GetSelectedTreeNode();
        if (node is not { IsDirectory: true, CanExpand: true }
            || !TryGetTreeItemRectangle(node.ItemHandle, out NativeMethods.Rectangle rectangle)
            || !NativeMethods.GetCursorPosition(out NativeMethods.Point originalCursor))
        {
            return false;
        }

        NativeMethods.Point point = new()
        {
            X = GetTreeChevronLeft(GetTreeLevel(node)) + S(3),
            Y = (rectangle.Top + rectangle.Bottom) / 2,
        };
        NativeMethods.Point screenPoint = point;
        if (!NativeMethods.ClientToScreen(_fileTree, ref screenPoint)
            || !NativeMethods.SetCursorPosition(screenPoint.X, screenPoint.Y))
        {
            return false;
        }

        try
        {
            nint mousePosition = unchecked((nint)((point.Y << 16) | (point.X & 0xFFFF)));
            bool wasExpanded = IsTreeItemExpanded(node.ItemHandle);
            node.IsExpanded = wasExpanded;
            if (!NativeMethods.PostMessage(_fileTree, NativeMethods.WindowMessageLeftButtonDown, 1, mousePosition)
                || !NativeMethods.PostMessage(_fileTree, NativeMethods.WindowMessageLeftButtonUp, 0, mousePosition))
            {
                return false;
            }

            return await WaitForTreeToggleAsync(node, wasExpanded);
        }
        finally
        {
            _ = NativeMethods.PostMessage(_fileTree, NativeMethods.WindowMessageLeftButtonUp, 0, 0);
            _ = NativeMethods.SetCursorPosition(originalCursor.X, originalCursor.Y);
        }
    }

    internal async Task<bool> DoubleClickSelectedTreeNameForTestAsync()
    {
        TreeNodeState? node = GetSelectedTreeNode();
        if (node is not { IsDirectory: true, CanExpand: true }
            || !TryGetTreeItemRectangle(node.ItemHandle, out NativeMethods.Rectangle rectangle)
            || !NativeMethods.GetCursorPosition(out NativeMethods.Point originalCursor))
        {
            return false;
        }

        NativeMethods.Point point = new()
        {
            X = Math.Max(rectangle.Left + S(4), GetTreeChevronLeft(GetTreeLevel(node)) + S(28)),
            Y = (rectangle.Top + rectangle.Bottom) / 2,
        };
        NativeMethods.Point screenPoint = point;
        if (!NativeMethods.ClientToScreen(_fileTree, ref screenPoint)
            || !NativeMethods.SetCursorPosition(screenPoint.X, screenPoint.Y))
        {
            return false;
        }

        try
        {
            bool wasExpanded = IsTreeItemExpanded(node.ItemHandle);
            node.IsExpanded = wasExpanded;
            HandleTreeDoubleClick(node);
            return await WaitForTreeToggleAsync(node, wasExpanded);
        }
        finally
        {
            _ = NativeMethods.PostMessage(_fileTree, NativeMethods.WindowMessageLeftButtonUp, 0, 0);
            _ = NativeMethods.SetCursorPosition(originalCursor.X, originalCursor.Y);
        }
    }

    internal bool SelectTreeContextTargetForTest(string path)
    {
        TreeNodeState? node = _treeNodes.Values.FirstOrDefault(
            candidate => candidate.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (node is null
            || !TryGetTreeItemRectangle(node.ItemHandle, out NativeMethods.Rectangle rectangle)
            || !NativeMethods.GetCursorPosition(out NativeMethods.Point originalCursor))
        {
            return false;
        }

        NativeMethods.Point point = new()
        {
            X = Math.Max(rectangle.Left + S(4), GetTreeChevronLeft(GetTreeLevel(node)) + S(20)),
            Y = (rectangle.Top + rectangle.Bottom) / 2,
        };
        if (!NativeMethods.ClientToScreen(_fileTree, ref point)
            || !NativeMethods.SetCursorPosition(point.X, point.Y))
        {
            return false;
        }

        try
        {
            TreeNodeState? target = SelectTreeContextTargetAtCursor();
            return target?.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase) == true
                && GetSelectedTreeNode()?.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase) == true;
        }
        finally
        {
            _ = NativeMethods.SetCursorPosition(originalCursor.X, originalCursor.Y);
        }
    }

    internal bool GitDiffVisibleForTest => _showingGitDiff;

    internal bool GitDiffLoadingForTest => _gitPanel?.DiffLoadingForTest == true;

    internal bool GitDiffLoadingNoticeVisibleForTest => _gitPanel?.DiffLoadingNoticeVisibleForTest == true;

    internal string GitDiffLoadingNoticeTextForTest => _gitPanel?.DiffLoadingNoticeTextForTest ?? string.Empty;

    internal string GitDiffChangeSummaryForTest => _gitPanel?.DiffChangeSummaryForTest ?? string.Empty;

    internal int WorkspaceGitDiffTabCountForTest => _previewGitDiffPath is null ? 0 : 1;


    internal string? PreviewGitDiffPathForTest => _previewGitDiffPath;

    internal bool ActiveTransientTabUsesPreviewTypographyForTest => BuildTransientEditorTabs()
        .FirstOrDefault(tab => tab.Active).IsPreview;

    internal string GitDiffPathForTest => _gitDiffRelativePath ?? _previewGitDiffPath ?? string.Empty;

    internal string GitDiffTitleForTest => _gitPanel?.DiffTitleForTest ?? string.Empty;

    internal int VisibleEditorTabCountForTest => _documents.Count + BuildTransientEditorTabs().Count;

    internal void CloseActiveTabForTest()
    {
        CloseActiveDocument();
    }

    internal bool ReferenceComparisonVisibleForTest => _showingReferenceComparison
        && _comparisonView?.IsVisible == true;

    internal string ReferenceComparisonFileBarForTest => _comparisonView?.FileBarTextForTest ?? string.Empty;

    internal void ShowHistoryComparisonForTest(GitComparisonResult result) => ShowHistoryComparison(result);

    internal NativeGitComparisonView? ComparisonViewForTest => _comparisonView;

    internal bool ClickReferenceComparisonTabForTest()
    {
        if (!NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client)) return false;
        int width = client.Right - client.Left;
        foreach (TransientEditorTabLayout layout in GetTransientTabLayouts(
            width, CalculateDocumentTabRectangles(width, GetDocumentTabRightReserve())))
        {
            if (layout.Tab.Kind != TransientEditorTabKind.ReferenceComparison) continue;
            nint point = PackClientPoint(layout.Rectangle.Left + S(25),
                (layout.Rectangle.Top + layout.Rectangle.Bottom) / 2);
            _ = NativeMethods.SendMessage(_documentTabs, NativeMethods.WindowMessageLeftButtonDown, 1, point);
            _ = NativeMethods.SendMessage(_documentTabs, NativeMethods.WindowMessageLeftButtonUp, 0, point);
            return ReferenceComparisonVisibleForTest;
        }
        return false;
    }

    internal bool ReferenceComparisonSettingsButtonUsesIconForTest => _comparisonView?.SettingsButtonUsesIconForTest == true;

    internal bool ReferenceComparisonModeButtonsUseIconsForTest => _comparisonView?.ModeButtonsUseCompactIconsForTest == true;

    internal bool ReferenceComparisonToolbarToolTipsCreatedForTest => _comparisonView?.ToolbarToolTipsCreatedForTest == true;

    internal bool TerminalCreatedForTest => _terminalPanel is not null;

    internal bool TerminalRunningForTest => _terminalPanel?.IsRunning == true;

    internal int TerminalBrowserProcessIdForTest => _terminalPanel?.BrowserProcessId ?? 0;

    internal int TerminalShellProcessIdForTest => _terminalPanel?.ShellProcessId ?? 0;

    internal int TerminalHeaderHeightForTest => _terminalPanel is null ? 0 : NativeTerminalPanel.HeaderHeightForTest;

    internal bool TerminalPanelVisibleForTest => _showingTerminalPanel
        && _terminalPanel?.IsVisible == true;

    internal bool TerminalSessionCloseActionCreatedForTest => _terminalPanel?.SessionCloseActionCreatedForTest == true;

    internal bool TerminalHideActionCreatedForTest => _terminalPanel?.HideActionCreatedForTest == true;

    internal bool TerminalMoreActionCreatedForTest => _terminalPanel?.MoreButtonHandle != 0;

    internal bool TerminalMoreMenuVisibleForTest => _contextMenu is { Handle: not 0 } menu
        && NativeMethods.IsWindowVisible(menu.Handle);

    internal NativeContextMenu? ContextMenuForTest => _contextMenu;

    internal void ShowTerminalMoreMenuForTest()
    {
        ShowTerminalMoreMenu();
    }

    internal void DismissContextMenuForTest()
    {
        _contextMenu?.Dispose();
        _contextMenu = null;
    }

    internal Task LocateActiveFileForTestAsync()
    {
        return LocateActiveFileAsync();
    }

    internal void CollapseProjectTreeForTest()
    {
        CollapseTree();
    }

    internal Task ExpandTreePathForTestAsync(string path)
    {
        return ExpandTreePathAsync(path);
    }

    internal void ShowGitForTest()
    {
        ShowGitChangesPanel();
    }

    internal void ToggleGitForTest()
    {
        ToggleGitChangesPanel();
    }

    internal void ToggleWorkspaceSearchForTest()
    {
        ToggleWorkspaceSearchPanel();
    }

    internal void ShowFilesForTest()
    {
        ShowFilesPanel(focusTree: true);
    }

    internal void KeepTreeRootVisibleForTest()
    {
        EnsureTreeRootVisible();
    }

    internal void ToggleProjectForTest()
    {
        ToggleProjectPanel();
    }

    internal void HideProjectForTest()
    {
        HandleCommand(CommandHideProject);
    }

    internal void ResizeToMinimumForTest()
    {
        _ = NativeMethods.MoveWindow(_handle, 80, 60, MinimumWidth, MinimumHeight, true);
        Layout();
    }

    internal void ShowHistoryForTest()
    {
        ShowHistoryPanel();
    }

    internal void ShowSettingsForTest()
    {
        ShowAppearanceSettings();
    }

    internal void ShowCloneForTest()
    {
        ShowCloneDialog();
    }

    internal void ShowRemotesForTest()
    {
        _gitPanel?.ShowRemotesForHost();
    }

    internal void ShowCreateStashForTest()
    {
        _gitPanel?.ShowCreateStashForHost();
    }

    internal void ShowStashManagerForTest()
    {
        _gitPanel?.ShowStashManagerForHost();
    }

    internal void ShowResetForTest()
    {
        _gitPanel?.ShowResetForHost("HEAD~1");
    }

    internal void ShowPushForTest(string? localReference = null)
    {
        _gitPanel?.RunPushForHostAsync(localReference);
    }

    internal void ShowMainMenuForTest()
    {
        ShowMainMenu();
    }

    internal bool MainMenuOpenForTest => _mainMenuOpen;

    internal IReadOnlyList<string> MainMenuLabelsForTest =>
    [
        NativeMethods.GetWindowTextValue(_cloneButton),
        NativeMethods.GetWindowTextValue(_recentWorkspacesButton),
        NativeMethods.GetWindowTextValue(_currentFileButton),
        NativeMethods.GetWindowTextValue(_quickOpenButton),
        NativeMethods.GetWindowTextValue(_settingsButton),
    ];

    internal bool MainMenuUsesInlineButtonsForTest => _mainMenuOpen
        && new[]
        {
            _cloneButton,
            _recentWorkspacesButton,
            _currentFileButton,
            _quickOpenButton,
            _settingsButton,
        }.All(NativeMethods.IsWindowVisible);

    internal bool ShowWorkspaceOpenForTest()
    {
        ShowWorkspaceOpenDialog();
        return _workspaceOpenDialog is not null;
    }

    internal async Task ShowSmartCheckoutForTestAsync()
    {
        BranchMenuContext? context = await LoadBranchMenuContextAsync(showError: false);
        if (context is null)
        {
            return;
        }

        string branchName = context.Snapshot.Branches
            .FirstOrDefault(branch => !branch.IsRemote && !branch.IsCurrent)
            ?.Name
            ?? "feature/ux";
        _ = await ConfirmSmartCheckoutAsync(context, branchName, CancellationToken.None);
    }

    internal bool ShowProjectContextMenuForTest(string path)
    {
        TreeNodeState? node = _treeNodes.Values.FirstOrDefault(
            candidate => candidate.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (node is null || !TryGetTreeItemRectangle(node.ItemHandle, out NativeMethods.Rectangle rectangle))
        {
            return false;
        }

        NativeMethods.Point point = new()
        {
            X = rectangle.Left + S(28),
            Y = (rectangle.Top + rectangle.Bottom) / 2,
        };
        if (!NativeMethods.ClientToScreen(_fileTree, ref point))
        {
            return false;
        }

        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSelectItem,
            NativeMethods.TreeViewCaret,
            node.ItemHandle);
        ShowTreeContextMenu(node, point.X, point.Y);
        return true;
    }

    internal void ShowCommitEmptyForTest()
    {
        _gitPanel?.ShowEmptyStateForHost();
    }

    internal void ShowRepositoryInitForTest()
    {
        if (_gitPanel is null)
        {
            return;
        }

        _gitPanel.ShowPlainDirectoryStateForHost();
        _ = _gitPanel.ShowInitializeConfirmationForHost();
    }

    internal void SetStatusForTest(string text)
    {
        SetGitStatus(text);
    }

    internal void ShowOperationProgressForTest(string title, string detail)
    {
        ShowOperationProgress(title, detail, canCancel: true);
    }

    internal void ShowOperationResultForTest(string title, string detail, bool error)
    {
        ShowOperationResult(title, detail, error);
    }

    internal void ShowGitUnavailableForTest(string reason)
    {
        _gitUnavailableNoticeShown = false;
        MarkGitRuntimeUnavailable(reason, showNotice: true);
    }

    internal Task WriteTerminalInputForTestAsync(string input)
    {
        return _terminalPanel?.WriteInputForTestAsync(input) ?? Task.CompletedTask;
    }

    internal bool ShowTerminalCloseConfirmationForTest()
    {
        if (_terminalPanel is not { IsRunning: true })
        {
            return false;
        }

        _ = NativeTerminalCloseDialog.Show(
            _handle,
            _settings,
            keep: () =>
            {
                SetStatus(UiText.TerminalReady);
            },
            close: () =>
            {
                CloseTerminal(requireConfirmation: false);
            });
        return true;
    }

    internal void ShowWorktreesForTest()
    {
        _historyPanel?.ShowWorktreesForHost();
    }

    internal void ToggleHistoryForTest()
    {
        ToggleHistoryPanel();
    }

    internal void ShowFileHistoryForTest(string path)
    {
        ShowFileHistory(path);
    }

    internal Task<bool> SelectGitFileForTestAsync(string relativePath)
    {
        return _gitPanel?.SelectFileForTestAsync(relativePath) ?? Task.FromResult(false);
    }

    internal Task<bool> ClickGitFileForTestAsync(string relativePath)
    {
        return _gitPanel?.ClickFileForTestAsync(relativePath) ?? Task.FromResult(false);
    }

    internal void DelayNextGitDiffForTest(int milliseconds)
    {
        _gitPanel?.DelayNextDiffForTest(milliseconds);
    }

    internal bool SelectGitFileRowForTest(string relativePath)
    {
        return _gitPanel?.SelectFileRowForTest(relativePath) == true;
    }

    internal bool ShowGitDiffLoadingForTest(string relativePath)
    {
        return _gitPanel?.ShowDiffLoadingForHost(relativePath) == true;
    }

    internal Task<bool> InvokeCommitToolbarActionForTestAsync(NativeCommitToolbarAction action)
    {
        return _gitPanel?.InvokeCommitToolbarActionForTestAsync(action) ?? Task.FromResult(false);
    }

    internal void RequestGitRefreshForTest(params string[] changedPaths)
    {
        if (changedPaths.Length == 0)
        {
            _gitPanel?.RequestRefresh();
        }
        else
        {
            _gitPanel?.RequestRefresh(changedPaths);
        }
    }

    internal void RequestGitMetadataRefreshForTest()
    {
        _gitPanel?.RequestMetadataRefreshForTest();
    }

    internal void RequestHistoryRefreshForTest()
    {
        _historyPanel?.RequestRefresh();
    }

    internal bool ClickGitFileCheckboxForTest(string relativePath)
    {
        return _gitPanel?.ClickFileCheckboxForTest(relativePath) == true;
    }

    internal bool ToggleGitFileWithSpaceForTest(string relativePath)
    {
        return _gitPanel?.ToggleFileWithSpaceForTest(relativePath) == true;
    }

    internal bool DoubleClickGitFileForTest(string relativePath)
    {
        return _gitPanel?.DoubleClickFileForTest(relativePath) == true;
    }

    internal Task<bool> EnterGitFileForTestAsync(string relativePath)
    {
        return _gitPanel?.EnterFileForTestAsync(relativePath) ?? Task.FromResult(false);
    }

    internal bool ClickGitGroupChevronForTest(GitChangeGroup group)
    {
        return _gitPanel?.ClickGroupChevronForTest(group) == true;
    }

    internal bool DoubleClickGitGroupTitleForTest(GitChangeGroup group)
    {
        return _gitPanel?.DoubleClickGroupTitleForTest(group) == true;
    }

    internal bool ToggleGitGroupWithEnterForTest(GitChangeGroup group)
    {
        return _gitPanel?.ToggleGroupWithEnterForTest(group) == true;
    }

    internal async Task<IReadOnlyList<string>> LoadTopBarBranchesForTestAsync()
    {
        BranchMenuContext? context = await LoadBranchMenuContextAsync(showError: false);
        return context?.Snapshot.Branches
            .Where(branch => !branch.IsRemote)
            .Select(branch => branch.Name)
            .ToArray() ?? [];
    }

    internal async Task<bool> ShowBranchPopupForTestAsync()
    {
        await ShowBranchMenuAsync();
        return _branchPopup is not null && NativeMethods.IsWindowVisible(_branchPopup.Handle);
    }

    internal void CloseBranchPopupForTest()
    {
        _branchPopup?.Dispose();
        _branchPopup = null;
        _contextMenu?.Dispose();
        _contextMenu = null;
    }

    internal async Task<bool> SwitchTopBarBranchForTestAsync(string branchName)
    {
        BranchMenuContext? context = await LoadBranchMenuContextAsync(showError: false);
        return context is not null
            && await SwitchTopBarBranchAsync(context, branchName, showError: false);
    }

    internal Task RollbackSelectedForTestAsync()
    {
        return _gitPanel?.RollbackSelectedForTestAsync() ?? Task.CompletedTask;
    }

    internal Task ShowRollbackConfirmationForTestAsync()
    {
        return _gitPanel?.ShowRollbackConfirmationForHostAsync() ?? Task.CompletedTask;
    }

    internal void ShowSearchForTest(WorkspaceSearchMode mode, string query)
    {
        ShowSearch(mode);
        _searchPanel?.SetQueryForTest(query);
    }

    internal void CloseSearchForTest()
    {
        CloseSearch();
    }

    internal bool OpenFirstSearchResultWithEnterForTest()
    {
        return _searchPanel?.OpenFirstResultWithEnterForTest() == true;
    }

    internal Task<bool> OpenTerminalForTestAsync()
    {
        return OpenTerminalAsync();
    }

    internal Func<string, Task>? TerminalStartupCheckpointForTest { get; set; }

    internal bool CloseTerminalForTest()
    {
        return CloseTerminal(requireConfirmation: false);
    }

    internal void HideTerminalForTest()
    {
        HideTerminalPanel();
    }

    internal void ShowTerminalForTest()
    {
        ShowTerminalPanel();
    }

    internal void ToggleTerminalForTest()
    {
        ToggleTerminalPanel();
    }

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int command = _settings.Window.IsMaximized ? NativeMethods.ShowMaximized : NativeMethods.ShowNormal;
        _ = NativeMethods.ShowWindow(_handle, command);
        _ = NativeMethods.UpdateWindow(_handle);
        _shown = true;
        if (_operationNotificationKind != OperationNotificationKind.None)
        {
            // 工作区可能在主窗口首次显示前完成异步探测，重新提升通知层确保首帧可见。
            Layout();
            EnsureOperationNotificationOnTop(redraw: true);
        }
    }

    internal void Activate()
    {
        if (_handle != 0)
        {
            if (NativeMethods.IsIconic(_handle))
            {
                _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowRestore);
            }

            _ = NativeMethods.SetForegroundWindow(_handle);
            RedrawForActivation();
        }
    }

    private void RedrawForActivation()
    {
        _activationRedrawCount++;
        ActivationRedrawCompletedForTest = NativeMethods.RedrawWindow(
            _handle,
            0,
            0,
            NativeMethods.RedrawInvalidate
                | NativeMethods.RedrawAllChildren
                | NativeMethods.RedrawUpdateNow);
    }

    internal void Close()
    {
        if (_destroying || _destroyed)
        {
            return;
        }

        if (DeferCloseForTerminalStartup()) return;

        if (_handle != 0 && NativeMethods.IsWindow(_handle))
        {
            _destroying = true;
            if (!NativeMethods.DestroyWindow(_handle))
            {
                _destroying = false;
                throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.MainWindowCloseFailed);
            }
        }
    }

    internal static int RunMessageLoop()
    {
        while (NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0) > 0)
        {
            MainWindow? instance = FindInstanceForMessage(message.Window);
            if (instance is not null && instance.HandleShortcut(message))
            {
                continue;
            }

            _ = NativeMethods.TranslateMessage(ref message);
            _ = NativeMethods.DispatchMessage(ref message);
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_terminalStarts.Any(task => !task.IsCompleted))
        {
            _disposeAfterTerminalClose = true;
            Close();
            return;
        }

        _disposed = true;
        _toolTip?.Dispose();
        _toolTip = null;
        Close();
        // DestroyWindow 通常同步触发 WM_DESTROY；若句柄已由宿主提前销毁，则仍需由 Dispose 兜底释放。
        if (!_destroyed)
        {
            ReleaseWorkspaceResources();
        }
        _workspaceFileChangeQueue.Dispose();
        SaveSettings();
        GC.SuppressFinalize(this);
    }

    internal void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_disposed || _handle == 0)
        {
            return;
        }

        _dispatchQueue.Enqueue(action);
        _ = NativeMethods.PostMessage(_handle, NativeMethods.WindowMessageAppDispatch, 0, 0);
    }

    internal async Task<bool> OpenWorkspaceAsync(string path, bool restoreState = false)
    {
        if (_disposed || _terminalClosePending || _destroying || _destroyed) return false;
        WorkspaceValidationResult validation = WorkspaceDirectoryService.ValidateRoot(path);
        if (!validation.IsValid || validation.FullPath is null)
        {
            SetStatus(validation.ErrorMessage ?? UiText.InvalidDirectory);
            return false;
        }

        string fullPath = validation.FullPath;
        if (_workspaceRoot?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (!CloseTerminal(requireConfirmation: true))
        {
            return false;
        }

        WorkspaceInstanceCoordinator coordinator = new(fullPath);
        if (!await coordinator.TryBecomeOwnerAsync())
        {
            await coordinator.DisposeAsync();
            SetStatus(UiText.WorkspaceAlreadyOpen);
            return false;
        }

        return await AttachWorkspaceAsync(fullPath, coordinator, restoreState);
    }

    private async Task<bool> AttachWorkspaceAsync(
        string fullPath,
        WorkspaceInstanceCoordinator coordinator,
        bool restoreState)
    {
        string? restoredWorkspace = _settings.LastWorkspace;
        PrepareWorkspaceShell(fullPath, coordinator);
        await CompleteWorkspaceAttachAsync(
            fullPath,
            restoreState && restoredWorkspace?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true);
        return true;
    }

    private void PrepareWorkspaceShell(string fullPath, WorkspaceInstanceCoordinator coordinator)
    {
        ReleaseWorkspaceResources();
        _instanceCoordinator = coordinator;
        _instanceCoordinator.ActivationRequested += OnActivationRequested;
        _workspaceRoot = fullPath;
        _gitRuntime = null;
        _gitRuntimeResolution = null;
        _gitRuntimeUnavailable = false;
        _gitRuntimeUnavailableReason = UiText.GitUnavailable;
        _gitUnavailableNoticeShown = false;
        _settings = _settings with
        {
            LastWorkspace = fullPath,
            RecentWorkspaces = BuildRecentWorkspaces(fullPath, _settings.RecentWorkspaces),
        };
        _saved = false;
        _fileWatcher = new(fullPath);
        _fileWatcher.Changed += OnWorkspaceFilesChanged;
        _ = NativeMethods.SetWindowText(_handle, $"{Path.GetFileName(fullPath)} — {UiText.AppName}");
        UpdateBranchLabel(fullPath);
        InitializeTree(fullPath);
        CancellationTokenSource treeGitStatusCancellation = new();
        _treeGitStatusCancellation = treeGitStatusCancellation;
        _ = InitializeTreeGitStatusAsync(
            fullPath,
            _treeGeneration,
            treeGitStatusCancellation);
        SetStatus(UiText.WorkspaceOpened);
    }

    private async Task CompleteWorkspaceAttachAsync(string fullPath, bool restoreState)
    {
        if (_workspaceRoot?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        TreeNodeState root = _treeNodes.Values.Single(node => node.ParentId == 0);
        await LoadTreeNodeAsync(root);
        if (_workspaceRoot?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) != true
            || !_treeNodes.ContainsKey(root.Id))
        {
            return;
        }

        root.IsExpanded = true;
        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewExpand,
            NativeMethods.TreeViewExpandItem,
            root.ItemHandle);

        if (restoreState)
        {
            await RestoreWorkspaceStateAsync();
        }
    }

    private void UpdateBranchLabel(string workspaceRoot)
    {
        string label = "Git";
        try
        {
            string dotGitPath = Path.Combine(workspaceRoot, ".git");
            string? gitDirectory = Directory.Exists(dotGitPath) ? dotGitPath : null;
            if (gitDirectory is null && File.Exists(dotGitPath))
            {
                string pointer = File.ReadAllText(dotGitPath).Trim();
                const string prefix = "gitdir:";
                if (pointer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = pointer[prefix.Length..].Trim();
                    gitDirectory = Path.GetFullPath(candidate, workspaceRoot);
                }
            }

            string headPath = gitDirectory is null ? string.Empty : Path.Combine(gitDirectory, "HEAD");
            if (File.Exists(headPath))
            {
                string head = File.ReadAllText(headPath).Trim();
                const string branchPrefix = "ref: refs/heads/";
                label = head.StartsWith(branchPrefix, StringComparison.Ordinal)
                    ? head[branchPrefix.Length..]
                    : head.Length > 12
                        ? head[..12]
                        : head;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            label = "Git";
        }

        if (string.Equals(_branchLabel, label, StringComparison.Ordinal))
        {
            return;
        }

        _branchLabel = label;
        if (!_mainMenuOpen)
        {
            _ = NativeMethods.SetWindowText(_recentWorkspacesButton, label);
            // 只有分支文字实际改变时重新分配入口宽度，重复状态刷新不触发布局。
            Layout();
        }
        _ = NativeMethods.InvalidateRectangle(_recentWorkspacesButton, 0, true);
    }

    private async Task<GitRuntimeInfo> ResolveGitRuntimeAsync(CancellationToken cancellationToken)
    {
        if (_gitRuntime is { } cached)
        {
            return cached;
        }

        Task<GitRuntimeInfo> pending = _gitRuntimeResolution is { } existing
            && !existing.IsCanceled
            && !existing.IsFaulted
            ? existing
            : new GitExecutableLocator().ResolveAsync(
                _settings.GitExecutablePath,
                cancellationToken);
        _gitRuntimeResolution = pending;
        GitRuntimeInfo runtime = await pending;
        if (!_disposed
            && !cancellationToken.IsCancellationRequested
            && ReferenceEquals(_gitRuntimeResolution, pending))
        {
            _gitRuntime = runtime;
        }

        return runtime;
    }

    private async Task InitializeTreeGitStatusAsync(
        string workspaceRoot,
        int treeGeneration,
        CancellationTokenSource lifetime)
    {
        try
        {
            GitRuntimeInfo runtime = await ResolveGitRuntimeAsync(lifetime.Token);
            if (!runtime.IsAvailable)
            {
                if (IsCurrentTreeGitStatusRequest(workspaceRoot, treeGeneration, lifetime))
                {
                    Post(() => MarkGitRuntimeUnavailable(
                        runtime.UnavailableReason ?? UiText.GitUnavailable,
                        showNotice: true));
                }

                return;
            }

            if (!IsCurrentTreeGitStatusRequest(workspaceRoot, treeGeneration, lifetime))
            {
                return;
            }

            GitRepositoryOperationResult inspected = await new GitRepositoryService(runtime).InspectAsync(
                workspaceRoot,
                lifetime.Token);
            if (!inspected.IsSuccess
                || inspected.Repository is not { Kind: GitRepositoryKind.WorkingTree } repository
                || !IsCurrentTreeGitStatusRequest(workspaceRoot, treeGeneration, lifetime))
            {
                return;
            }

            _treeGitRepository = repository;
            _treeGitStatusService = new(runtime);
            await RefreshTreeGitStatusAsync(treeGeneration, lifetime);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private bool IsCurrentTreeGitStatusRequest(
        string workspaceRoot,
        int treeGeneration,
        CancellationTokenSource lifetime)
    {
        return !lifetime.IsCancellationRequested
            && ReferenceEquals(_treeGitStatusCancellation, lifetime)
            && treeGeneration == _treeGeneration
            && _workspaceRoot?.Equals(workspaceRoot, StringComparison.OrdinalIgnoreCase) == true;
    }

    private void RequestTreeGitStatusRefresh()
    {
        if (_treeGitStatusCancellation is { } lifetime)
        {
            _ = RefreshTreeGitStatusAsync(_treeGeneration, lifetime);
        }
    }

    private async Task RefreshTreeGitStatusAsync(
        int treeGeneration,
        CancellationTokenSource lifetime)
    {
        if (_treeGitStatusRefreshing)
        {
            _treeGitStatusRefreshPending = true;
            return;
        }

        GitStatusService? statusService = _treeGitStatusService;
        GitRepositorySnapshot? repository = _treeGitRepository;
        if (statusService is null || repository is null)
        {
            return;
        }

        _treeGitStatusRefreshing = true;
        try
        {
            do
            {
                _treeGitStatusRefreshPending = false;
                GitStatusResult result = await statusService.ReadAsync(repository, lifetime.Token);
                if (!IsCurrentTreeGitStatusRequest(repository.WorkspacePath, treeGeneration, lifetime))
                {
                    return;
                }

                if (result.IsSuccess && result.Snapshot is not null)
                {
                    ApplyTreeGitStatus(result.Snapshot);
                }
            }
            while (_treeGitStatusRefreshPending);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_treeGitStatusCancellation, lifetime))
            {
                _treeGitStatusRefreshing = false;
            }
        }
    }

    private void ApplyTreeGitStatus(GitStatusSnapshot? snapshot)
    {
        if (!ShouldApplyTreeGitStatusForTest(
                _treeGitStatusWorkspaceRoot,
                _treeGitStatusSnapshot,
                _workspaceRoot,
                snapshot))
        {
            return;
        }

        _treeGitStatusWorkspaceRoot = _workspaceRoot;
        _treeGitStatusSnapshot = snapshot;
        _treeGitStatusIndex = snapshot is null || _workspaceRoot is null
            ? null
            : new(_workspaceRoot, snapshot.Files);
        if (_fileTree != 0)
        {
            _ = NativeMethods.InvalidateRectangle(_fileTree, 0, false);
        }

        if (_documentTabs != 0)
        {
            _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
        }
    }

    internal static bool ShouldApplyTreeGitStatusForTest(
        string? previousWorkspaceRoot,
        GitStatusSnapshot? previousSnapshot,
        string? workspaceRoot,
        GitStatusSnapshot? snapshot)
    {
        if (!string.Equals(previousWorkspaceRoot, workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (snapshot is null)
        {
            return previousSnapshot is not null;
        }

        return !NativeGitPanel.StatusSnapshotsEquivalentForTest(previousSnapshot, snapshot);
    }

    private Task<bool> AttachOwnedWorkspaceAsync(string fullPath, bool restoreState)
    {
        WorkspaceInstanceCoordinator coordinator = _pendingOwnedCoordinator
            ?? throw new InvalidOperationException(UiText.StartupWorkspaceOwnershipReleased);
        _pendingOwnedCoordinator = null;
        return AttachWorkspaceAsync(fullPath, coordinator, restoreState);
    }

    internal Task OpenDocumentForTestAsync(string path)
    {
        return OpenDocumentAsync(path);
    }

    private static MainWindow? FindInstanceForMessage(nint messageWindow)
    {
        nint root = messageWindow == 0 ? 0 : NativeMethods.GetAncestor(messageWindow, NativeMethods.GetAncestorRoot);
        lock (InstancesGate)
        {
            if (root != 0 && Instances.TryGetValue(root, out MainWindow? instance))
            {
                return instance;
            }

            return Instances.Count == 1 ? Instances.Values.Single() : null;
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
                Style = NativeMethods.ClassRedrawOnHorizontalChange | NativeMethods.ClassRedrawOnVerticalChange,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                Background = 0,
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, UiText.MainWindowClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static void InitializeCommonControls()
    {
        NativeMethods.CommonControls controls = new()
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.CommonControls>(),
            Classes = NativeMethods.CommonControlsTreeView
                | NativeMethods.CommonControlsBar
                | NativeMethods.CommonControlsTab,
        };
        if (!NativeMethods.InitializeCommonControls(ref controls))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.CommonControlsInitializeFailed);
        }
    }

    private nint HitTestWindow(nint longParameter)
    {
        nint defaultResult = NativeMethods.DefaultWindowProcedure(
            _handle,
            NativeMethods.WindowMessageNonClientHitTest,
            0,
            longParameter);
        if (defaultResult != NativeMethods.HitTestClient
            || !NativeMethods.GetCursorPosition(out NativeMethods.Point point)
            || !NativeMethods.ScreenToClient(_handle, ref point)
            || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return defaultResult;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        if (!NativeMethods.IsZoomed(_handle))
        {
            int resizeBorder = S(4);
            bool left = point.X >= 0 && point.X < resizeBorder;
            bool right = point.X <= width && point.X > width - resizeBorder;
            bool top = point.Y >= 0 && point.Y < resizeBorder;
            bool bottom = point.Y <= height && point.Y > height - resizeBorder;
            if (top && left)
            {
                return NativeMethods.HitTestTopLeft;
            }
            if (top && right)
            {
                return NativeMethods.HitTestTopRight;
            }
            if (bottom && left)
            {
                return NativeMethods.HitTestBottomLeft;
            }
            if (bottom && right)
            {
                return NativeMethods.HitTestBottomRight;
            }
            if (left)
            {
                return NativeMethods.HitTestLeft;
            }
            if (right)
            {
                return NativeMethods.HitTestRight;
            }
            if (top)
            {
                return NativeMethods.HitTestTop;
            }
            if (bottom)
            {
                return NativeMethods.HitTestBottom;
            }
        }

        bool draggableTitleArea = point.Y >= 0
            && point.Y < ToolbarHeight
            && point.X >= S(210)
            && point.X < width - S(170);
        return draggableTitleArea ? NativeMethods.HitTestCaption : defaultResult;
    }

    private void ApplyMinimumMaximumInfo(nint parameter)
    {
        if (parameter == 0)
        {
            return;
        }

        NativeMethods.MinimumMaximumInfo info = Marshal.PtrToStructure<NativeMethods.MinimumMaximumInfo>(parameter);
        info.MinimumTrackSize = new NativeMethods.Point { X = MinimumWidth, Y = MinimumHeight };
        nint monitor = NativeMethods.MonitorFromWindow(_handle, NativeMethods.MonitorDefaultToNearest);
        NativeMethods.MonitorInfo monitorInfo = new()
        {
            Size = unchecked((uint)Marshal.SizeOf<NativeMethods.MonitorInfo>()),
        };
        if (monitor != 0 && NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            info.MaximumPosition = new NativeMethods.Point
            {
                X = monitorInfo.WorkArea.Left - monitorInfo.Monitor.Left,
                Y = monitorInfo.WorkArea.Top - monitorInfo.Monitor.Top,
            };
            info.MaximumSize = new NativeMethods.Point
            {
                X = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left,
                Y = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top,
            };
        }

        Marshal.StructureToPtr(info, parameter, false);
    }

    private SplitterKind HitTestSplitter(NativeMethods.Point point)
    {
        if (_workspaceRoot is null
            || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return SplitterKind.None;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        int contentHeight = Math.Max(0, height - ToolbarHeight - StatusHeight);
        bool bottomPanelVisible = _showingHistoryPanel || _showingTerminalPanel;
        int bottomHeight = bottomPanelVisible ? GetBottomPanelHeight(contentHeight) : 0;
        int bottomGap = bottomHeight > 0 ? CardGap : 0;
        int upperContentHeight = Math.Max(0, contentHeight - bottomHeight - bottomGap);
        int splitterTolerance = S(2);

        bool sidePanelVisible = _showingGitPanel || _projectPanelVisible;
        int treePanelWidth = sidePanelVisible ? GetTreePanelWidth(width) : 0;
        int documentLeft = MainPanelLeft
            + (sidePanelVisible ? treePanelWidth + CardGap : FrameInset);
        if (sidePanelVisible)
        {
            int verticalLeft = MainPanelLeft + treePanelWidth;
            if (point.X >= verticalLeft - splitterTolerance
                && point.X <= verticalLeft + CardGap + splitterTolerance
                && point.Y >= ToolbarHeight
                && point.Y <= ToolbarHeight + contentHeight)
            {
                return SplitterKind.Project;
            }
        }

        if (bottomPanelVisible)
        {
            int horizontalTop = ToolbarHeight + upperContentHeight;
            if (point.Y >= horizontalTop - splitterTolerance
                && point.Y <= horizontalTop + CardGap + splitterTolerance
                && point.X >= documentLeft
                && point.X <= width)
            {
                return SplitterKind.Bottom;
            }
        }

        return SplitterKind.None;
    }

    private bool BeginSplitterDrag(nint longParameter)
    {
        NativeMethods.Point point = DecodeClientPoint(longParameter);
        SplitterKind splitter = HitTestSplitter(point);
        if (splitter == SplitterKind.None || !NativeMethods.IsWindowVisible(_handle) || !NativeMethods.IsWindowEnabled(_handle)
            || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return false;
        }

        // 从实际显示尺寸和按下位置开始计算增量，命中间隙边缘也不产生初始跳动。
        _splitterDragStartPoint = point;
        _splitterDragStartSize = splitter == SplitterKind.Project
            ? GetTreePanelWidth(Math.Max(0, client.Right - client.Left))
            : GetBottomPanelHeight(Math.Max(0, client.Bottom - client.Top - ToolbarHeight - StatusHeight));
        _activeSplitter = splitter;
        _ = NativeMethods.SetCapture(_handle);
        SetSplitterCursor(splitter);
        return true;
    }

    private bool UpdateSplitterDrag(nint longParameter)
    {
        if (_activeSplitter == SplitterKind.None
            || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return false;
        }

        NativeMethods.Point point = DecodeClientPoint(longParameter);
        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        if (_activeSplitter == SplitterKind.Project)
        {
            int next = ClampTreePanelWidth(_splitterDragStartSize + point.X - _splitterDragStartPoint.X, width);
            if (next == GetTreePanelWidth(width)) return true;
            _projectPanelWidth = next;
        }
        else
        {
            int contentHeight = Math.Max(0, height - ToolbarHeight - StatusHeight);
            int next = ClampBottomPanelHeight(_splitterDragStartSize + _splitterDragStartPoint.Y - point.Y, contentHeight);
            if (next == GetBottomPanelHeight(contentHeight)) return true;
            _bottomPanelHeight = next;
        }

        _saved = false;
        Layout();
        SetSplitterCursor(_activeSplitter);
        return true;
    }

    private bool EndSplitterDrag()
    {
        if (_activeSplitter == SplitterKind.None)
        {
            return false;
        }

        _activeSplitter = SplitterKind.None;
        _splitterDragStartSize = 0;
        if (NativeMethods.GetCapture() == _handle)
        {
            _ = NativeMethods.ReleaseCapture();
        }
        return true;
    }

    private bool SetSplitterCursorAtPointer()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point)
            || !NativeMethods.ScreenToClient(_handle, ref point))
        {
            return false;
        }

        SplitterKind splitter = _activeSplitter != SplitterKind.None
            ? _activeSplitter
            : HitTestSplitter(point);
        if (splitter == SplitterKind.None)
        {
            return false;
        }

        SetSplitterCursor(splitter);
        return true;
    }

    private static void SetSplitterCursor(SplitterKind splitter)
    {
        nint cursorName = splitter == SplitterKind.Project
            ? NativeMethods.SizeWestEastCursor
            : NativeMethods.SizeNorthSouthCursor;
        nint cursor = NativeMethods.LoadCursor(0, cursorName);
        if (cursor != 0)
        {
            _ = NativeMethods.SetCursor(cursor);
        }
    }

    private static NativeMethods.Point DecodeClientPoint(nint longParameter)
    {
        nuint packed = unchecked((nuint)longParameter);
        return new()
        {
            X = unchecked((short)NativeMethods.LowWord(packed)),
            Y = unchecked((short)NativeMethods.HighWord(packed)),
        };
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        nint? nonClientResult = ResolveCustomNonClientMessage(message);
        if (nonClientResult is not null)
        {
            return nonClientResult.Value;
        }

        MainWindow? instance;
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
            case NativeMethods.WindowMessageActivate:
                if (NativeMethods.LowWord(wordParameter) == NativeMethods.WindowActivationInactive)
                {
                    instance.ActivationRedrawCompletedForTest = false;
                }
                else
                {
                    instance.RedrawForActivation();
                }
                break;
            case NativeMethods.WindowMessageGetMinimumMaximumInfo:
                instance.ApplyMinimumMaximumInfo(longParameter);
                return 0;
            case NativeMethods.WindowMessageSetCursor:
                if (instance.SetSplitterCursorAtPointer())
                {
                    return 1;
                }
                break;
            case NativeMethods.WindowMessageLeftButtonDown:
                if (instance.BeginSplitterDrag(longParameter))
                {
                    return 0;
                }
                break;
            case NativeMethods.WindowMessageMouseMove:
                if (instance.UpdateSplitterDrag(longParameter))
                {
                    return 0;
                }
                break;
            case NativeMethods.WindowMessageLeftButtonUp:
                _ = instance.UpdateSplitterDrag(longParameter);
                if (instance.EndSplitterDrag())
                {
                    return 0;
                }
                break;
            case NativeMethods.WindowMessageCaptureChanged:
                instance._activeSplitter = SplitterKind.None;
                instance._splitterDragStartSize = 0;
                return 0;
            case NativeMethods.WindowMessageCancelMode:
                _ = instance.EndSplitterDrag();
                break;
            case NativeMethods.WindowMessageShowWindow:
            case NativeMethods.WindowMessageEnable:
                if (wordParameter == 0) _ = instance.EndSplitterDrag();
                break;
            case NativeMethods.WindowMessageMove:
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageDpiChanged:
                _ = instance.EndSplitterDrag();
                if (longParameter != 0)
                {
                    NativeMethods.Rectangle suggested = Marshal.PtrToStructure<NativeMethods.Rectangle>(longParameter);
                    _ = NativeMethods.SetWindowPosition(
                        instance._handle,
                        0,
                        suggested.Left,
                        suggested.Top,
                        suggested.Right - suggested.Left,
                        suggested.Bottom - suggested.Top,
                        NativeMethods.SetWindowPositionNoZOrder
                            | NativeMethods.SetWindowPositionNoActivate);
                }

                if (NativeTheme.UpdateDpiForWindow(instance._handle))
                {
                    instance.ApplyAppearance();
                    instance.Layout();
                }
                return 0;
            case NativeMethods.WindowMessageNonClientHitTest:
                return instance.HitTestWindow(longParameter);
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
            case NativeMethods.WindowMessageDrawItem:
                return instance.DrawControl(longParameter) ? 1 : 0;
            case NativeMethods.WindowMessagePaint:
                instance.PaintWindow();
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessageNotify:
                return instance.HandleNotification(longParameter);
            case NativeMethods.WindowMessageClose:
                if (instance.CloseTerminal(requireConfirmation: true))
                {
                    instance.Close();
                }
                return 0;
            case NativeMethods.WindowMessageDestroy:
                instance.OnDestroyed(window);
                return 0;
            case NativeMethods.WindowMessageAppDispatch:
                instance.DrainDispatchQueue();
                return 0;
            case NativeMethods.WindowMessageAppActivate:
                instance.Activate();
                return 0;
            case NativeMethods.WindowMessageSettingChange:
            case NativeMethods.WindowMessageThemeChanged:
                if (instance._settings.Theme.Equals("System", StringComparison.OrdinalIgnoreCase))
                {
                    instance.ApplyAppearance();
                }

                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    internal static nint? ResolveCustomNonClientMessage(uint message)
    {
        return message switch
        {
            NativeMethods.WindowMessageNonClientCalculateSize => 0,
            NativeMethods.WindowMessageNonClientPaint => 0,
            NativeMethods.WindowMessageNonClientActivate => 1,
            _ => null,
        };
    }

    private void CreateControls()
    {
        _applicationIcon = CreateControl(
            NativeMethods.StaticClass,
            "A",
            ApplicationIconControlIdentifier,
            NativeMethods.StaticOwnerDraw);
        _openFolderButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandMainMenu, NativeMethods.ButtonPushButton);
        _cloneButton = CreateControl(NativeMethods.ButtonClass, UiText.AppName, CommandRecentWorkspaces, NativeMethods.ButtonPushButton);
        _recentWorkspacesButton = CreateControl(NativeMethods.ButtonClass, "Git", CommandBranch, NativeMethods.ButtonPushButton);
        _quickOpenButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandQuickOpen, NativeMethods.ButtonPushButton);
        _currentFileButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CurrentFile,
            CommandCurrentFile,
            NativeMethods.ButtonPushButton);
        _searchButton = CreateControl(NativeMethods.ButtonClass, UiText.SearchSymbol, CommandWorkspaceSearch, NativeMethods.ButtonPushButton);
        _settingsButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandSettings, NativeMethods.ButtonPushButton);
        _filesButton = CreateControl(NativeMethods.ButtonClass, UiText.FilesSymbol, CommandFiles, NativeMethods.ButtonPushButton);
        _gitButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CommitSymbol,
            CommandGitChanges,
            NativeMethods.ButtonPushButton);
        _historyButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.GitSymbol,
            CommandHistory,
            NativeMethods.ButtonPushButton);
        _terminalButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.TerminalSymbol,
            CommandTerminal,
            NativeMethods.ButtonPushButton);
        _minimizeButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandMinimize, NativeMethods.ButtonPushButton);
        _maximizeButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandMaximize, NativeMethods.ButtonPushButton);
        _closeButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandCloseWindow, NativeMethods.ButtonPushButton);
        _projectHeader = CreateControl(
            NativeMethods.StaticClass,
            UiText.Project,
            306,
            NativeMethods.StaticOwnerDraw);
        _locateActiveFileButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandLocateActiveFile,
            NativeMethods.ButtonPushButton);
        _collapseTreeButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandCollapseTree,
            NativeMethods.ButtonPushButton);
        _treeOptionsButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandTreeOptions,
            NativeMethods.ButtonPushButton);
        _hideProjectButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandHideProject,
            NativeMethods.ButtonPushButton);
        _fileTree = CreateControl(
            NativeMethods.TreeViewClass,
            string.Empty,
            FileTreeControlIdentifier,
            NativeMethods.TreeViewHasButtons
                | NativeMethods.TreeViewShowSelectionAlways
                | NativeMethods.TreeViewFullRowSelect
                | NativeMethods.TreeViewNoHorizontalScroll
                | NativeMethods.WindowStyleVerticalScroll);
        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSetExtendedStyle,
            NativeMethods.TreeViewDoubleBuffer,
            unchecked((nint)NativeMethods.TreeViewDoubleBuffer));
        _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewSetIndent, unchecked((nuint)S(16)), 0);
        int treeItemHeight = NativeTheme.ContentHeight(27, 8);
        treeItemHeight += treeItemHeight & 1;
        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSetItemHeight,
            unchecked((nuint)treeItemHeight),
            0);
        _documentTabs = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            DocumentTabsControlIdentifier,
            NativeMethods.ButtonPushButton);
        if (!NativeMethods.SetWindowSubclass(
                _documentTabs,
                DocumentTabsProcedure,
                DocumentTabsSubclassIdentifier,
                0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }
        if (!NativeMethods.SetWindowSubclass(
                _fileTree,
                FileTreeProcedure,
                FileTreeSubclassIdentifier,
                0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }
        _emptyDocumentLabel = CreateControl(
            NativeMethods.StaticClass,
            UiText.NoDocument,
            303,
            NativeMethods.StaticOwnerDraw);
        _workspaceLabel = CreateControl(
            NativeMethods.StaticClass,
            UiText.NoWorkspace,
            304,
            NativeMethods.StaticOwnerDraw);
        _statusBar = CreateControl(NativeMethods.StaticClass, string.Empty, 305, NativeMethods.StaticOwnerDraw);
        _operationNotification = CreateControl(
            NativeMethods.StaticClass,
            string.Empty,
            OperationNotificationControlIdentifier,
            NativeMethods.StaticOwnerDraw);
        if (!NativeMethods.SetWindowSubclass(
                _operationNotification,
                OperationNotificationProcedure,
                OperationNotificationSubclassIdentifier,
                0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }
        _operationNotificationCancelButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Cancel,
            CommandCancelOperationNotification,
            NativeMethods.ButtonFlat,
            _operationNotification);
        _operationNotificationActionButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ConfigureGitExecutable,
            CommandConfigureGit,
            NativeMethods.ButtonFlat,
            _operationNotification);
        _ = NativeMethods.ShowWindow(_operationNotification, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_operationNotificationCancelButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_operationNotificationActionButton, NativeMethods.ShowHide);
        _cancelBranchButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Cancel,
            CommandCancelBranch,
            NativeMethods.ButtonPushButton);
        _ = NativeMethods.ShowWindow(_cancelBranchButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_projectHeader, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_locateActiveFileButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_collapseTreeButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_treeOptionsButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_hideProjectButton, NativeMethods.ShowHide);
    }

    private void CreateToolTips()
    {
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_openFolderButton, UiText.MainMenu);
        _toolTip.Add(_cloneButton, UiText.RecentWorkspaces);
        _toolTip.Add(_recentWorkspacesButton, UiText.CurrentBranch);
        _toolTip.Add(_quickOpenButton, UiText.QuickOpen);
        _toolTip.Add(_currentFileButton, UiText.QuickOpen);
        _toolTip.Add(_settingsButton, UiText.Settings);
        _toolTip.Add(_filesButton, UiText.Project);
        _toolTip.Add(_gitButton, UiText.CommitPanel);
        _toolTip.Add(_searchButton, UiText.SearchWorkspace);
        _toolTip.Add(_terminalButton, UiText.Terminal);
        _toolTip.Add(_historyButton, UiText.History);
        _toolTip.Add(_minimizeButton, UiText.MinimizeWindow);
        _toolTip.Add(_maximizeButton, UiText.MaximizeOrRestoreWindow);
        _toolTip.Add(_closeButton, UiText.CloseWindow);
        _toolTip.Add(_locateActiveFileButton, UiText.LocateActiveFile);
        _toolTip.Add(_collapseTreeButton, UiText.CollapseDirectories);
        _toolTip.Add(_treeOptionsButton, UiText.MoreActions);
        _toolTip.Add(_hideProjectButton, UiText.HideProjectPanel);
        _toolTip.Add(_statusBar, _statusAccessibleText.Length == 0 ? UiText.AppName : _statusAccessibleText);
    }

    private nint CreateControl(
        string className,
        string text,
        int identifier,
        uint specificStyle,
        nint parent = 0)
    {
        bool acceptsFocus = className.Equals(NativeMethods.ButtonClass, StringComparison.Ordinal)
            || className.Equals(NativeMethods.EditClass, StringComparison.Ordinal)
            || className.Equals(NativeMethods.TreeViewClass, StringComparison.Ordinal)
            || className.Equals(NativeMethods.ListBoxClass, StringComparison.Ordinal)
            || className.Equals(NativeMethods.ComboBoxClass, StringComparison.Ordinal);
        uint controlStyle = className.Equals(NativeMethods.ButtonClass, StringComparison.Ordinal)
            && specificStyle == NativeMethods.ButtonPushButton
                ? NativeMethods.ButtonOwnerDraw
            : specificStyle;
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
            parent == 0 ? _handle : parent,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }

        nint font = NativeTheme.UiFont;
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        if (IsFrameButtonCommand((uint)identifier)) AttachFrameButton(_handle, control);
        return control;
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        if ((_activeSplitter == SplitterKind.Project && !_showingGitPanel && !_projectPanelVisible)
            || (_activeSplitter == SplitterKind.Bottom && !_showingHistoryPanel && !_showingTerminalPanel))
            _ = EndSplitterDrag();
        _layoutInvocationCount++;

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        nint deferredPosition = NativeMethods.BeginDeferWindowPosition(28);
        int windowButtonWidth = S(33);
        int windowActionsLeft = Math.Max(S(420), width - windowButtonWidth * 3);
        int textTop = S(7);
        int iconTop = (ToolbarHeight - S(30)) / 2;
        Move(ref deferredPosition, _applicationIcon, S(9), (ToolbarHeight - S(17)) / 2, S(17), S(17));
        Move(ref deferredPosition, _openFolderButton, S(36), iconTop, S(31), S(30));
        if (_mainMenuOpen)
        {
            // 菜单项直接占据标题栏左侧原有入口的位置，避免弹出一张脱离上下文的菜单卡片。
            _ = NativeMethods.SetWindowText(_cloneButton, UiText.MainMenuFile);
            _ = NativeMethods.SetWindowText(_recentWorkspacesButton, UiText.MainMenuView);
            _ = NativeMethods.SetWindowText(_currentFileButton, UiText.MainMenuGit);
            _ = NativeMethods.SetWindowText(_quickOpenButton, UiText.MainMenuTerminal);
            _ = NativeMethods.SetWindowText(_settingsButton, UiText.MainMenuSettings);
            nint menuContext = NativeMethods.GetDeviceContext(_handle);
            try
            {
                int left = S(73);
                foreach ((nint control, int minimum) in new[]
                {
                    (_cloneButton, 46), (_recentWorkspacesButton, 46), (_currentFileButton, 46),
                    (_quickOpenButton, 58), (_settingsButton, 52),
                })
                {
                    int itemWidth = Math.Max(S(minimum), MeasureTextWidth(menuContext,
                        NativeMethods.GetWindowTextValue(control), NativeTheme.UiFont) + S(16));
                    Move(ref deferredPosition, control, left, textTop, itemWidth, ToolbarTextHeight);
                    left += itemWidth;
                }
            }
            finally { _ = NativeMethods.ReleaseDeviceContext(_handle, menuContext); }
        }
        else
        {
            string workspaceLabel = GetWorkspaceDisplayName();
            nint deviceContext = NativeMethods.GetDeviceContext(_handle);
            (int WorkspaceWidth, int BranchLeft, int BranchWidth, int ContextLeft, int ContextWidth) titleLayout;
            try
            {
                titleLayout = MeasureTitleBarLayout(deviceContext, width, workspaceLabel, _branchLabel);
            }
            finally
            {
                _ = NativeMethods.ReleaseDeviceContext(_handle, deviceContext);
            }
            Move(ref deferredPosition, _cloneButton, S(73), textTop, titleLayout.WorkspaceWidth, ToolbarTextHeight);
            Move(ref deferredPosition, _recentWorkspacesButton, titleLayout.BranchLeft, textTop, titleLayout.BranchWidth, ToolbarTextHeight);
            Move(ref deferredPosition, _quickOpenButton, windowActionsLeft - S(68), iconTop, S(31), S(30));
            Move(ref deferredPosition, _settingsButton, windowActionsLeft - S(34), iconTop, S(31), S(30));
            _ = NativeMethods.SetWindowText(_recentWorkspacesButton, _branchLabel);
            _ = NativeMethods.SetWindowText(_cloneButton, workspaceLabel);
            _ = NativeMethods.SetWindowText(_quickOpenButton, string.Empty);
            _ = NativeMethods.SetWindowText(_settingsButton, string.Empty);
            _ = NativeMethods.SetWindowText(_currentFileButton, GetCurrentFileContextText());
            Move(ref deferredPosition, _currentFileButton, titleLayout.ContextLeft, textTop, titleLayout.ContextWidth, ToolbarTextHeight);
        }
        Move(ref deferredPosition, _minimizeButton, windowActionsLeft, 0, windowButtonWidth, ToolbarHeight);
        Move(ref deferredPosition, _maximizeButton, windowActionsLeft + windowButtonWidth, 0, windowButtonWidth, ToolbarHeight);
        Move(ref deferredPosition, _closeButton, windowActionsLeft + windowButtonWidth * 2, 0, windowButtonWidth, ToolbarHeight);
        int contentHeight = Math.Max(0, height - ToolbarHeight - StatusHeight);
        bool bottomPanelVisible = _showingHistoryPanel || _showingTerminalPanel;
        int bottomHeight = bottomPanelVisible
            ? GetBottomPanelHeight(contentHeight)
            : 0;
        int bottomGap = bottomHeight > 0 ? CardGap : 0;
        int upperContentHeight = Math.Max(0, contentHeight - bottomHeight - bottomGap);
        int contentBottom = height - StatusHeight;
        int railButtonLeft = ActivityBarLeft + S(5);
        int railButtonSize = S(32);
        int railButtonStep = S(36);
        Move(ref deferredPosition, _filesButton, railButtonLeft, ToolbarHeight + S(4), railButtonSize, railButtonSize);
        Move(ref deferredPosition, _gitButton, railButtonLeft, ToolbarHeight + S(4) + railButtonStep, railButtonSize, railButtonSize);
        Move(ref deferredPosition, _searchButton, railButtonLeft, ToolbarHeight + S(4) + railButtonStep * 2, railButtonSize, railButtonSize);
        int historyButtonTop = contentBottom - railButtonSize - S(4);
        Move(ref deferredPosition, _terminalButton, railButtonLeft, historyButtonTop - railButtonStep, railButtonSize, railButtonSize);
        Move(ref deferredPosition, _historyButton, railButtonLeft, historyButtonTop, railButtonSize, railButtonSize);
        int panelLeft = MainPanelLeft;
        int panelTop = ToolbarHeight + FrameInset;
        int upperPanelHeight = Math.Max(0, upperContentHeight - FrameInset);
        int sidePanelHeight = Math.Max(0, contentHeight - FrameInset);
        bool sidePanelVisible = _showingGitPanel || _projectPanelVisible;
        int treePanelWidth = sidePanelVisible ? GetTreePanelWidth(width) : 0;
        int projectHeaderHeight = Math.Min(NativeTheme.ContentHeight(39, 10), sidePanelHeight);
        Move(ref deferredPosition, _projectHeader, panelLeft, panelTop, treePanelWidth, projectHeaderHeight);
        int projectActionTop = panelTop + Math.Max(0, (projectHeaderHeight - S(26)) / 2);
        int projectActionLeft = panelLeft + Math.Max(0, treePanelWidth - S(121));
        Move(ref deferredPosition, _locateActiveFileButton, projectActionLeft, projectActionTop, S(26), S(26));
        Move(ref deferredPosition, _collapseTreeButton, projectActionLeft + S(29), projectActionTop, S(26), S(26));
        Move(ref deferredPosition, _treeOptionsButton, projectActionLeft + S(58), projectActionTop, S(26), S(26));
        Move(ref deferredPosition, _hideProjectButton, projectActionLeft + S(87), projectActionTop, S(26), S(26));
        Move(
            ref deferredPosition,
            _fileTree,
            panelLeft + SurfaceContentInset,
            panelTop + projectHeaderHeight,
            Math.Max(0, treePanelWidth - SurfaceContentInset * 2),
            Math.Max(0, sidePanelHeight - projectHeaderHeight - SurfaceContentInset));
        int documentLeft = panelLeft + (sidePanelVisible ? treePanelWidth + CardGap : FrameInset);
        int documentWidth = Math.Max(0, width - documentLeft - MainRightInset);
        Move(ref deferredPosition, _documentTabs, documentLeft, panelTop, documentWidth, TabHeight);
        int documentTop = panelTop + TabHeight;
        int documentHeight = Math.Max(0, upperPanelHeight - TabHeight - 1);
        Move(
            ref deferredPosition,
            _emptyDocumentLabel,
            documentLeft + SurfaceContentInset,
            documentTop,
            Math.Max(0, documentWidth - SurfaceContentInset * 2),
            Math.Max(0, documentHeight - SurfaceContentInset));
        Move(ref deferredPosition, _workspaceLabel, panelLeft, panelTop, Math.Max(0, width - panelLeft - MainRightInset), upperPanelHeight);
        Move(ref deferredPosition, _statusBar, 0, Math.Max(0, height - StatusHeight), width, StatusHeight);
        Move(
            ref deferredPosition,
            _cancelBranchButton,
            Math.Max(0, width - _statusFormatWidth - S(10) - _statusCancelWidth),
            Math.Max(0, height - StatusHeight + S(1)),
            _statusCancelWidth,
            Math.Max(0, StatusHeight - S(2)));
        int notificationWidth = Math.Min(S(395), Math.Max(S(280), width - documentLeft - S(36)));
        int notificationHeight = _operationNotificationKind == OperationNotificationKind.Progress
            || _operationNotificationShowConfigureGit
            ? S(126)
            : S(96);
        int notificationLeft = Math.Max(
            documentLeft + S(8),
            width - MainRightInset - S(18) - notificationWidth);
        int notificationTop = Math.Max(
            documentTop + S(8),
            height - StatusHeight - S(18) - notificationHeight);
        Move(
            ref deferredPosition,
            _operationNotification,
            notificationLeft,
            notificationTop,
            notificationWidth,
            notificationHeight);
        if (deferredPosition != 0)
        {
            _ = NativeMethods.EndDeferWindowPosition(deferredPosition);
        }

        // 取消按钮属于通知卡片的嵌套子窗口，不能混入主窗口子控件的批量布局。
        nint notificationButtonPosition = 0;
        int notificationButtonTop = notificationHeight - S(39);
        bool showCancelButton = NativeMethods.IsWindowVisible(_operationNotificationCancelButton);
        bool showActionButton = NativeMethods.IsWindowVisible(_operationNotificationActionButton);
        int right = notificationWidth - S(14);
        if (showCancelButton)
        {
            Move(
                ref notificationButtonPosition,
                _operationNotificationCancelButton,
                right - S(54),
                notificationButtonTop,
                S(54),
                S(28));
            right -= S(60);
        }

        Move(
            ref notificationButtonPosition,
            _operationNotificationActionButton,
            right - S(112),
            notificationButtonTop,
            S(112),
            S(28));

        if (_operationNotificationKind != OperationNotificationKind.None)
        {
            ApplyRoundedRegion(_operationNotification, notificationWidth, notificationHeight, S(8));
        }

        foreach (DocumentTabState document in _documents)
        {
            if (document.View is not { } view)
            {
                continue;
            }

            int documentContentWidth = Math.Max(0, documentWidth - SurfaceContentInset * 2);
            int documentContentHeight = Math.Max(0, documentHeight - SurfaceContentInset);
            view.SetBounds(
                documentLeft + SurfaceContentInset,
                documentTop,
                documentContentWidth,
                documentContentHeight);
            ApplyRoundedRegion(view.Handle, documentContentWidth, documentContentHeight, CardRadius);
        }
        if (_comparisonView is not null)
        {
            int documentContentWidth = Math.Max(0, documentWidth - SurfaceContentInset * 2);
            int documentContentHeight = Math.Max(0, documentHeight - SurfaceContentInset);
            _comparisonView.SetBounds(
                documentLeft + SurfaceContentInset,
                documentTop,
                documentContentWidth,
                documentContentHeight);
            _comparisonView.SetVisible(_showingReferenceComparison);
            ApplyRoundedRegion(
                _comparisonView.Handle,
                documentContentWidth,
                documentContentHeight,
                CardRadius);
        }
        if (_searchPanel is { } searchPanel)
        {
            int searchWidth = Math.Min(
                S(730),
                Math.Max(S(320), width - S(150)));
            int searchHeight = searchPanel.PreferredHeight(searchWidth);
            int preferredTop = panelTop + S(100);
            int latestTop = panelTop + Math.Max(S(8), upperPanelHeight - searchHeight - S(10));
            int searchTop = Math.Min(preferredTop, latestTop);
            searchPanel.SetBounds(
                Math.Max(0, (width - searchWidth) / 2),
                searchTop,
                searchWidth,
                Math.Min(searchHeight, Math.Max(S(84), upperPanelHeight - Math.Max(0, searchTop - panelTop))));
        }
        _gitPanel?.SetBounds(
            panelLeft,
            panelTop,
            treePanelWidth,
            sidePanelHeight,
            documentLeft,
            documentTop,
            documentWidth,
            documentHeight);
        _historyPanel?.SetBounds(
            documentLeft + SurfaceContentInset,
            ToolbarHeight + upperContentHeight + bottomGap + SurfaceContentInset,
            Math.Max(0, documentWidth - SurfaceContentInset * 2),
            Math.Max(0, bottomHeight - SurfaceContentInset * 2));
        _terminalPanel?.SetBounds(
            documentLeft + SurfaceContentInset,
            ToolbarHeight + upperContentHeight + bottomGap + SurfaceContentInset,
            Math.Max(0, documentWidth - SurfaceContentInset * 2),
            Math.Max(0, bottomHeight - SurfaceContentInset * 2));

        ApplyRoundedRegion(
            _fileTree,
            Math.Max(0, treePanelWidth - SurfaceContentInset * 2),
            Math.Max(0, sidePanelHeight - projectHeaderHeight - SurfaceContentInset),
            CardRadius);
        ApplyRoundedRegion(
            _emptyDocumentLabel,
            Math.Max(0, documentWidth - SurfaceContentInset * 2),
            Math.Max(0, documentHeight - SurfaceContentInset),
            CardRadius);
        if (_historyPanel is not null)
        {
            ApplyRoundedRegion(
                _historyPanel.Handle,
                Math.Max(0, documentWidth - SurfaceContentInset * 2),
                Math.Max(0, bottomHeight - SurfaceContentInset * 2),
                CardRadius);
        }

        // 底部工具窗口与编辑器同级，布局重排后重新置于编辑器前方。
        if (_showingHistoryPanel && _historyPanel is not null)
        {
            RaiseChildWindow(_historyPanel.Handle);
        }
        else if (_showingTerminalPanel && _terminalPanel is not null)
        {
            RaiseChildWindow(_terminalPanel.Handle);
        }

        if (_searchPanel is not null)
        {
            RaiseChildWindow(_searchPanel.Handle);
            // 重新布局可能先在其他兄弟窗口下完成，提升后必须重绘整个浮层覆盖区。
            _ = NativeMethods.InvalidateRectangle(_searchPanel.Handle, 0, true);
        }

        EnsureOperationNotificationOnTop(redraw: false);

        _ = NativeMethods.InvalidateRectangle(_handle, 0, false);
    }

    private static void Move(ref nint deferredPosition, nint window, int x, int y, int width, int height)
    {
        if (window == 0)
        {
            return;
        }

        int safeWidth = Math.Max(0, width);
        int safeHeight = Math.Max(0, height);
        if (deferredPosition != 0)
        {
            nint updatedPosition = NativeMethods.DeferWindowPosition(
                deferredPosition,
                window,
                0,
                x,
                y,
                safeWidth,
                safeHeight,
                NativeMethods.SetWindowPositionNoZOrder | NativeMethods.SetWindowPositionNoActivate);
            if (updatedPosition != 0)
            {
                deferredPosition = updatedPosition;
                return;
            }

            deferredPosition = 0;
        }

        _ = NativeMethods.SetWindowPosition(
            window,
            0,
            x,
            y,
            safeWidth,
            safeHeight,
            NativeMethods.SetWindowPositionNoZOrder | NativeMethods.SetWindowPositionNoActivate);
    }

    private int GetTreePanelWidth(int windowWidth)
    {
        int availableWidth = Math.Max(0, windowWidth - MainPanelLeft - MainRightInset - CardGap);
        int preferred = _projectPanelWidth > 0
            ? _projectPanelWidth
            : GetDefaultTreePanelWidth(windowWidth, availableWidth);
        return ClampTreePanelWidth(preferred, windowWidth);
    }

    private static int ClampTreePanelWidth(int preferred, int windowWidth)
    {
        int availableWidth = Math.Max(0, windowWidth - MainPanelLeft - MainRightInset - CardGap);
        int maximum = Math.Min(TreePanelMaximumWidthForTest, Math.Max(S(300), availableWidth - S(360)));
        return Math.Clamp(preferred, S(300), maximum);
    }

    private static int GetDefaultTreePanelWidth(int windowWidth, int availableWidth)
    {
        // 视觉稿在 1180 逻辑像素及以下使用最小项目窗，避免小窗口挤压正文。
        if (NativeTheme.Unscale(windowWidth) <= 1180)
        {
            return S(300);
        }

        return Math.Max(S(318), (int)Math.Round(availableWidth * 0.225) + S(2));
    }

    internal static int GetDefaultTreePanelWidthForTest(int logicalWindowWidth, int logicalAvailableWidth)
    {
        return (int)Math.Round(
            NativeTheme.Unscale(GetDefaultTreePanelWidth(
                S(logicalWindowWidth),
                S(logicalAvailableWidth))));
    }

    private int GetBottomPanelHeight(int contentHeight)
    {
        int preferred = _bottomPanelHeight > 0
            ? _bottomPanelHeight
            : CalculateDefaultBottomPanelHeight(contentHeight);
        return ClampBottomPanelHeight(preferred, contentHeight);
    }

    private static int ClampBottomPanelHeight(int preferred, int contentHeight)
    {
        int minimum = DefaultBottomPanelMinimumHeight;
        int maximum = Math.Max(minimum, contentHeight - minimum);
        return Math.Clamp(preferred, minimum, maximum);
    }

    private static int CalculateDefaultBottomPanelHeight(int contentHeight) =>
        CalculateDefaultBottomPanelHeight(contentHeight, DefaultBottomPanelMinimumHeight);

    private static int CalculateDefaultBottomPanelHeight(int contentHeight, int minimumHeight)
    {
        int clientHeight = Math.Max(0, contentHeight) + ToolbarHeight + StatusHeight;
        int proportionalHeight = (int)Math.Round(clientHeight * DefaultBottomPanelHeightRatio);
        return Math.Clamp(
            proportionalHeight,
            minimumHeight,
            Math.Max(minimumHeight, DefaultBottomPanelMaximumHeight));
    }

    private static void ApplyRoundedRegion(nint window, int width, int height, int radius)
    {
        if (window == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        // Win32 接收圆角直径；布局令牌登记的是半径。
        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == 0)
        {
            return;
        }

        if (NativeMethods.SetWindowRegion(window, region, true) == 0)
        {
            _ = NativeMethods.DeleteObject(region);
        }
    }

    private nint PaintBackground(nint deviceContext)
    {
        if (deviceContext == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle rectangle))
        {
            return 0;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint frame = palette.Chrome;
        uint panel = palette.Panel;
        uint surfaceEdge = palette.Border;
        nint brush = NativeMethods.CreateSolidBrush(frame);
        if (brush != 0)
        {
            _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
            _ = NativeMethods.DeleteObject(brush);
        }

        if (!dark)
        {
            NativeMethods.Rectangle toolbarRectangle = rectangle;
            toolbarRectangle.Bottom = Math.Min(toolbarRectangle.Bottom, ToolbarHeight);
            FillToolbarGradient(deviceContext, toolbarRectangle, 0);
        }

        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;
        int contentHeight = Math.Max(0, height - ToolbarHeight - StatusHeight);
        bool bottomPanelVisible = _showingHistoryPanel || _showingTerminalPanel;
        int bottomHeight = bottomPanelVisible
            ? GetBottomPanelHeight(contentHeight)
            : 0;
        int upperHeight = Math.Max(0, contentHeight - bottomHeight - (bottomHeight > 0 ? CardGap : 0));
        int panelLeft = MainPanelLeft;
        int panelTop = ToolbarHeight + FrameInset;
        int treePanelWidth = GetTreePanelWidth(width);
        if (_workspaceRoot is not null)
        {
            FillElevatedRoundedSurface(
                deviceContext,
                new NativeMethods.Rectangle
                {
                    Left = panelLeft,
                    Top = panelTop,
                    Right = panelLeft + treePanelWidth,
                    Bottom = panelTop + Math.Max(0, contentHeight - FrameInset),
                },
                panel,
                surfaceEdge,
                CardRadius);
            FillElevatedRoundedSurface(
                deviceContext,
                new NativeMethods.Rectangle
                {
                    Left = panelLeft + treePanelWidth + CardGap,
                    Top = panelTop,
                    Right = width - MainRightInset,
                    Bottom = panelTop + Math.Max(0, upperHeight - FrameInset),
                },
                panel,
                surfaceEdge,
                CardRadius);

            if (bottomPanelVisible)
            {
                FillElevatedRoundedSurface(
                    deviceContext,
                    new NativeMethods.Rectangle
                    {
                        Left = panelLeft + treePanelWidth + CardGap,
                        Top = ToolbarHeight + upperHeight + CardGap,
                        Right = width - MainRightInset,
                        Bottom = height - StatusHeight,
                    },
                    panel,
                    surfaceEdge,
                    CardRadius);
            }
        }

        return 1;
    }

    private void PaintWindow()
    {
        nint deviceContext = NativeMethods.BeginPaint(_handle, out NativeMethods.PaintStructure paint);
        if (deviceContext == 0)
        {
            return;
        }

        try
        {
            _ = PaintBackground(deviceContext);
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }
    }

    private void DrawOperationNotificationCard(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        bool fillWindowBackground)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        if (fillWindowBackground)
        {
            Fill(deviceContext, rectangle, palette.Chrome);
        }

        FillRoundedSurface(
            deviceContext,
            rectangle,
            palette.Panel,
            palette.BorderStrong,
            S(8));
        NativeMethods.Rectangle title = rectangle;
        title.Left += S(14);
        title.Top += S(10);
        title.Right -= S(14);
        title.Bottom = title.Top + S(22);
        DrawText(
            deviceContext,
            _operationNotificationTitle,
            title,
            _operationNotificationKind == OperationNotificationKind.Error
                ? Rgb(216, 77, 77)
                : palette.Text,
            centered: false,
            fontWeight: NativeTheme.UiMediumFont);
        NativeMethods.Rectangle detail = title;
        detail.Top += S(23);
        detail.Bottom = detail.Top + (_operationNotificationKind == OperationNotificationKind.Progress
            ? S(22)
            : S(34));
        if (_operationNotificationKind == OperationNotificationKind.Progress)
        {
            DrawText(
                deviceContext,
                _operationNotificationDetail,
                detail,
                palette.Muted,
                centered: false,
                fontWeight: NativeTheme.UiFont);
        }
        else
        {
            DrawWrappedText(
                deviceContext,
                _operationNotificationDetail,
                detail,
                palette.Muted,
                NativeTheme.UiFont);
        }

        if (_operationNotificationKind == OperationNotificationKind.Progress)
        {
            NativeMethods.Rectangle track = rectangle;
            track.Left += S(14);
            track.Right -= S(14);
            track.Top = rectangle.Top + S(65);
            track.Bottom = track.Top + S(4);
            Fill(deviceContext, track, palette.Border);
            NativeMethods.Rectangle progress = track;
            progress.Right = progress.Left + Math.Max(S(1), (progress.Right - progress.Left) * 68 / 100);
            Fill(deviceContext, progress, palette.Accent);
        }
        else if (!string.IsNullOrWhiteSpace(_operationNotificationFootnote))
        {
            NativeMethods.Rectangle footnote = detail;
            footnote.Top = detail.Top + S(36);
            footnote.Bottom = footnote.Top + S(20);
            DrawText(
                deviceContext,
                _operationNotificationFootnote,
                footnote,
                palette.Muted,
                centered: false,
                fontWeight: NativeTheme.UiSmallFont);
        }
    }

    private static void DrawOperationNotificationButtonForVisualAudit(
        nint deviceContext,
        NativeMethods.Rectangle owner,
        nint button,
        bool dark,
        bool outlined)
    {
        if (button == 0
            || !NativeMethods.IsWindowVisible(button)
            || !NativeMethods.GetWindowRectangle(button, out NativeMethods.Rectangle rectangle))
        {
            return;
        }

        rectangle.Left -= owner.Left;
        rectangle.Top -= owner.Top;
        rectangle.Right -= owner.Left;
        rectangle.Bottom -= owner.Top;
        _ = NativeTheme.DrawFlatButton(
            deviceContext,
            rectangle,
            NativeMethods.GetWindowTextValue(button),
            dark,
            outlined: outlined,
            disabled: !NativeMethods.IsWindowEnabled(button));
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        if (IsFrameButtonCommand(item.ControlIdentifier))
        {
            item.ItemState &= ~NativeMethods.OwnerDrawHotLight;
            if (item.Control == _hoveredFrameButton) item.ItemState |= NativeMethods.OwnerDrawHotLight;
        }
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint frame = palette.Chrome;
        uint panel = palette.Panel;
        uint panelSoft = palette.PanelMuted;
        uint text = palette.Text;
        uint muted = palette.Muted;
        uint selection = palette.Accent;
        uint hover = palette.Hover;
        uint line = palette.Border;
        uint surfaceEdge = palette.Border;

        if (item.ControlIdentifier == ApplicationIconControlIdentifier)
        {
            Fill(item.DeviceContext, item.ItemRectangle, frame);

            DrawApplicationIcon(item.DeviceContext, item.ItemRectangle, selection);
            return true;
        }

        if (item.ControlIdentifier == DocumentTabsControlIdentifier)
        {
            return DrawDocumentTabs(item, frame, panel, panelSoft, text, muted, line, surfaceEdge);
        }

        if (item.ControlIdentifier == 305)
        {
            DrawStatusBar(item, frame, muted);
            return true;
        }

        if (item.ControlIdentifier == OperationNotificationControlIdentifier)
        {
            DrawOperationNotificationCard(item.DeviceContext, item.ItemRectangle, fillWindowBackground: true);
            return true;
        }

        if (item.ControlIdentifier == CommandCancelOperationNotification)
        {
            return NativeTheme.DrawFlatButton(parameter, dark, outlined: true);
        }

        if (item.ControlIdentifier == CommandConfigureGit)
        {
            return NativeTheme.DrawFlatButton(parameter, dark, outlined: true);
        }

        if (item.ControlIdentifier == 306)
        {
            Fill(item.DeviceContext, item.ItemRectangle, frame);
            FillRoundedSurface(item.DeviceContext, item.ItemRectangle, panel, surfaceEdge, CardRadius * 2);
            NativeMethods.Rectangle squaredBottom = item.ItemRectangle;
            squaredBottom.Top += CardRadius;
            Fill(item.DeviceContext, squaredBottom, panel);
            NativeMethods.Rectangle separator = item.ItemRectangle;
            separator.Top = Math.Max(separator.Top, separator.Bottom - S(1));
            Fill(item.DeviceContext, separator, line);
            NativeMethods.Rectangle titleRectangle = item.ItemRectangle;
            titleRectangle.Left += S(8);
            titleRectangle.Right = Math.Max(titleRectangle.Left, titleRectangle.Right - S(141));
            DrawText(
                item.DeviceContext,
                UiText.Project,
                titleRectangle,
                text,
                centered: false,
                fontWeight: NativeTheme.UiMediumFont);
            DrawDownArrow(
                item.DeviceContext,
                Math.Min(titleRectangle.Right, titleRectangle.Left
                    + MeasureTextWidth(item.DeviceContext, UiText.Project, NativeTheme.UiMediumFont)) + S(8),
                (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2,
                text);
            return true;
        }

        if (item.ControlIdentifier is 303 or 304)
        {
            Fill(item.DeviceContext, item.ItemRectangle, panel);
            string message = NativeMethods.GetWindowTextValue(item.Control);
            DrawText(item.DeviceContext, message, item.ItemRectangle, muted, centered: true, fontWeight: NativeTheme.UiFont);
            return true;
        }

        if (item.ControlIdentifier is CommandLocateActiveFile
            or CommandCollapseTree
            or CommandTreeOptions
            or CommandHideProject)
        {
            return DrawProjectToolButton(item, palette);
        }

        if (IsTitleBarCommand(item.ControlIdentifier)) return DrawTitleBarButton(item, palette);

        if (item.ControlIdentifier == TerminalTitleControlIdentifier)
        {
            Fill(item.DeviceContext, item.ItemRectangle, panel);
            NativeMethods.Rectangle titleRectangle = item.ItemRectangle;
            titleRectangle.Left += S(4);
            DrawText(
                item.DeviceContext,
                NativeMethods.GetWindowTextValue(item.Control),
                titleRectangle,
                text,
                centered: false,
                fontWeight: NativeTheme.UiMediumFont);
            return true;
        }

        if (item.ControlIdentifier == TerminalSessionControlIdentifier)
        {
            Fill(item.DeviceContext, item.ItemRectangle, panel);
            NativeMethods.Rectangle tabRectangle = item.ItemRectangle;
            tabRectangle.Left += S(1);
            tabRectangle.Top += S(1);
            tabRectangle.Right -= S(1);
            tabRectangle.Bottom -= S(1);
            (uint tabBorder, uint tabFill, uint tabText) = NativeTheme.SelectedToolTabColors(dark);
            FillRounded(
                item.DeviceContext,
                tabRectangle,
                tabBorder,
                S(5));
            int tabInset = Math.Max(1, S(1));
            tabRectangle.Left += tabInset;
            tabRectangle.Top += tabInset;
            tabRectangle.Right -= tabInset;
            tabRectangle.Bottom -= tabInset;
            FillRounded(
                item.DeviceContext,
                tabRectangle,
                tabFill,
                S(4));
            DrawText(
                item.DeviceContext,
                NativeMethods.GetWindowTextValue(item.Control),
                tabRectangle,
                tabText,
                centered: true,
                fontWeight: NativeTheme.UiFont);
            return true;
        }

        if (item.ControlIdentifier == TerminalSessionCloseControlIdentifier)
        {
            (uint tabBorder, uint tabFill, uint tabText) = NativeTheme.SelectedToolTabColors(dark);
            Fill(item.DeviceContext, item.ItemRectangle, panel);
            NativeMethods.Rectangle closeRectangle = item.ItemRectangle;
            closeRectangle.Left += S(1);
            closeRectangle.Top += S(1);
            closeRectangle.Right -= S(1);
            closeRectangle.Bottom -= S(1);
            FillRounded(item.DeviceContext, closeRectangle, tabBorder, S(5));
            int tabInset = Math.Max(1, S(1));
            closeRectangle.Left += tabInset;
            closeRectangle.Top += tabInset;
            closeRectangle.Right -= tabInset;
            closeRectangle.Bottom -= tabInset;
            FillRounded(
                item.DeviceContext,
                closeRectangle,
                (item.ItemState & NativeMethods.OwnerDrawDisabled) == 0
                    && (item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0
                    ? hover
                    : tabFill,
                S(4));
            bool disabledClose = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
            DrawTerminalToolbarIcon(item.DeviceContext, closeRectangle, TerminalSessionCloseControlIdentifier,
                disabledClose ? palette.Faint : tabText);
            if (!disabledClose) NativeTheme.DrawToolbarFocus(item, palette);
            return true;
        }

        if (item.ControlIdentifier is CommandTerminalMore or CommandHideTerminal)
        {
            Fill(item.DeviceContext, item.ItemRectangle, panel);
            bool disabledTool = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
            bool highlightedTool = !disabledTool && (item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0;
            if (highlightedTool) FillRounded(item.DeviceContext, item.ItemRectangle, hover, S(10));
            DrawTerminalToolbarIcon(item.DeviceContext, item.ItemRectangle, unchecked((int)item.ControlIdentifier),
                disabledTool ? palette.Faint : highlightedTool || (item.ItemState & NativeMethods.OwnerDrawFocus) != 0 ? text : muted);
            if (!disabledTool) NativeTheme.DrawToolbarFocus(item, palette);
            return true;
        }

        bool navigation = item.ControlIdentifier is CommandFiles
            or CommandGitChanges
            or CommandWorkspaceSearch
            or CommandTerminal
            or CommandHistory;
        bool active = item.ControlIdentifier switch
        {
            CommandFiles => !_showingGitPanel
                && _searchPanel?.Mode != WorkspaceSearchMode.Text
                && _projectPanelVisible,
            CommandGitChanges => _showingGitPanel
                && _searchPanel?.Mode != WorkspaceSearchMode.Text,
            CommandWorkspaceSearch => _searchPanel?.Mode == WorkspaceSearchMode.Text,
            CommandTerminal => _showingTerminalPanel,
            CommandHistory => _showingHistoryPanel,
            _ => false,
        };
        bool pressed = (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        bool hot = (item.ItemState & NativeMethods.OwnerDrawHotLight) != 0;
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
        active &= !disabled;
        pressed &= !disabled;
        hot &= !disabled;
        Fill(
            item.DeviceContext,
            item.ItemRectangle,
            item.ControlIdentifier is CommandCloseTerminal or CommandHideTerminal ? panel : frame);
        if (active || pressed || hot)
        {
            FillRounded(
                item.DeviceContext,
                item.ItemRectangle,
                active && navigation ? selection : hover,
                navigation ? S(14) : S(6));
        }

        string label = NativeMethods.GetWindowTextValue(item.Control);
        uint labelColor = disabled
            ? palette.Faint
            : active && navigation
                ? Rgb(255, 255, 255)
                : navigation
                    ? hot || pressed || (item.ItemState & NativeMethods.OwnerDrawFocus) != 0 ? text : muted
                    : text;

        if (navigation)
        {
            DrawNavigationIcon(
                item.DeviceContext,
                item.ItemRectangle,
                unchecked((int)item.ControlIdentifier),
                labelColor);
            if (!disabled) DrawFrameButtonFocus(item, palette, active);
            return true;
        }

        DrawText(
            item.DeviceContext,
            label,
            item.ItemRectangle,
            labelColor,
            centered: true,
            fontWeight: NativeTheme.UiFont);
        return true;
    }

    private static bool IsMainMenuButton(uint controlIdentifier)
    {
        return controlIdentifier is CommandRecentWorkspaces
            or CommandBranch
            or CommandCurrentFile
            or CommandQuickOpen
            or CommandSettings;
    }

    private static void DrawNavigationIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        int command,
        uint color)
    {
        if (command == CommandWorkspaceSearch)
        {
            _ = NativeTheme.DrawNavigationIcon(deviceContext, rectangle, NativeNavigationIcon.Search, color);
            return;
        }

        NativeToolWindowIcon? icon = command switch
        {
            CommandFiles => NativeToolWindowIcon.Project,
            CommandGitChanges => NativeToolWindowIcon.Commit,
            CommandTerminal => NativeToolWindowIcon.Terminal,
            CommandHistory => NativeToolWindowIcon.GitHistory,
            _ => null,
        };
        if (icon is { } toolIcon)
        {
            _ = NativeTheme.DrawToolWindowIcon(deviceContext, rectangle, toolIcon, color);
        }
    }

    private static void DrawTopBarIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        int command,
        uint color)
    {
        int centerX = (rectangle.Left + rectangle.Right) / 2;
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        if (command == CommandSettings)
        {
            _ = NativeTheme.DrawSettingsIcon(deviceContext, rectangle, color);
            return;
        }

        List<NativeGdiPlusDrawing.StrokeLine> lines = [];
        List<NativeGdiPlusDrawing.StrokeEllipse> ellipses = [];
        List<NativeGdiPlusDrawing.StrokeRectangle> rectangles = [];
        switch (command)
        {
            case CommandMainMenu:
                for (int index = -1; index <= 1; index++)
                {
                    float y = centerY + S(5f) * index;
                    lines.Add(new(centerX - S(6f), y, centerX + S(6f), y));
                }

                break;
            case CommandQuickOpen:
                _ = NativeTheme.DrawNavigationIcon(deviceContext, rectangle, NativeNavigationIcon.Search, color);
                return;
            case CommandMinimize:
                lines.Add(new(centerX - S(6f), centerY + S(3f), centerX + S(6f), centerY + S(3f)));
                break;
            case CommandMaximize:
                rectangles.Add(new(
                    centerX - S(5f),
                    centerY - S(5f),
                    S(10f),
                    S(10f)));
                break;
            case CommandCloseWindow:
                lines.Add(new(centerX - S(5f), centerY - S(5f), centerX + S(5f), centerY + S(5f)));
                lines.Add(new(centerX + S(5f), centerY - S(5f), centerX - S(5f), centerY + S(5f)));
                break;
        }

        if (lines.Count == 0 && ellipses.Count == 0 && rectangles.Count == 0)
        {
            return;
        }

        _ = NativeGdiPlusDrawing.StrokeShapes(
            deviceContext,
            color,
            Math.Max(1f, S(1.5f)),
            CollectionsMarshal.AsSpan(lines),
            CollectionsMarshal.AsSpan(ellipses),
            CollectionsMarshal.AsSpan(rectangles));
    }

    private static void DrawTerminalToolbarIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        int command,
        uint color)
    {
        int centerX = (rectangle.Left + rectangle.Right) / 2;
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        if (command == CommandTerminalMore)
        {
            DrawEllipsis(deviceContext, centerX, centerY, color);
            return;
        }

        NativeGdiPlusDrawing.StrokeLine[] lines = command == CommandHideTerminal
            ? [new(centerX - S(6), centerY, centerX + S(6), centerY)]
            : [
                new(centerX - S(5), centerY - S(5), centerX + S(5), centerY + S(5)),
                new(centerX + S(5), centerY - S(5), centerX - S(5), centerY + S(5)),
            ];
        _ = NativeGdiPlusDrawing.StrokeShapes(
            deviceContext,
            color,
            Math.Max(1f, NativeTheme.Scale(1.5f)),
            lines,
            [],
            []);
    }

    private static void Fill(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        nint brush = NativeMethods.CreateSolidBrush(color);
        if (brush == 0)
        {
            return;
        }

        _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
        _ = NativeMethods.DeleteObject(brush);
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

    private static void FillRoundedSurface(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint fill,
        uint edge,
        int radius)
    {
        FillRounded(deviceContext, rectangle, edge, radius);
        NativeMethods.Rectangle inner = rectangle;
        inner.Left++;
        inner.Top++;
        inner.Right--;
        inner.Bottom--;
        if (inner.Right > inner.Left && inner.Bottom > inner.Top)
        {
            FillRounded(deviceContext, inner, fill, Math.Max(2, radius - 2));
        }
    }

    private static void FillElevatedRoundedSurface(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint fill,
        uint edge,
        int radius)
    {
        FillRoundedSurface(deviceContext, rectangle, fill, edge, radius * 2);
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

    private static void DrawWrappedText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextWordBreak
            | NativeMethods.DrawTextNoPrefix
            | NativeMethods.DrawTextEndEllipsis;
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private bool DrawDocumentTabs(
        NativeMethods.DrawItem item,
        uint frame,
        uint panel,
        uint panelSoft,
        uint text,
        uint muted,
        uint line,
        uint surfaceEdge)
    {
        Fill(item.DeviceContext, item.ItemRectangle, frame);
        FillRoundedSurface(item.DeviceContext, item.ItemRectangle, panel, surfaceEdge, CardRadius * 2);
        NativeMethods.Rectangle squaredBottom = item.ItemRectangle;
        squaredBottom.Top += CardRadius;
        Fill(item.DeviceContext, squaredBottom, panel);

        int width = Math.Max(0, item.ItemRectangle.Right - item.ItemRectangle.Left);
        int rightReserve = GetDocumentTabRightReserve();
        List<DocumentTabLayout> tabs = CalculateDocumentTabRectangles(width, rightReserve);
        bool transientActive = _showingReferenceComparison || _showingGitDiff;
        bool dark = NativeTheme.IsDark(_settings.Theme);
        (uint activeTabBorder, uint activeTabFill) = NativeTheme.SelectedDocumentTabColors(dark);
        foreach (DocumentTabLayout layout in tabs)
        {
            int index = layout.DocumentIndex;
            NativeMethods.Rectangle tab = layout.Rectangle;
            bool selected = !transientActive && index == _activeDocumentIndex;
            string path = _documents[index].Path;
            string fileName = Path.GetFileName(path);
            GitChangeKind? status = _treeGitStatusIndex?.Resolve(path);
            uint? statusColor = status is null
                ? null
                : NativeGitStatusPalette.Resolve(status.Value, dark);
            if (selected || index == _hoveredDocumentTabIndex)
            {
                NativeMethods.Rectangle selectedTab = tab;
                selectedTab.Left += S(2);
                selectedTab.Top += S(7);
                selectedTab.Right -= S(2);
                selectedTab.Bottom -= S(7);
                if (selected)
                {
                    FillRoundedSurface(
                        item.DeviceContext,
                        selectedTab,
                        activeTabFill,
                        activeTabBorder,
                        S(6));
                }
                else
                {
                    FillRounded(item.DeviceContext, selectedTab, panelSoft, S(6));
                }
            }
            DrawTreeFileIcon(
                item.DeviceContext,
                tab.Left + S(10),
                (tab.Top + tab.Bottom) / 2,
                fileName,
                dark);

            NativeMethods.Rectangle titleRectangle = tab;
            titleRectangle.Left += S(34);
            titleRectangle.Right -= S(24);
            DrawText(
                item.DeviceContext,
                fileName,
                titleRectangle,
                statusColor ?? (selected ? text : muted),
                centered: false,
                fontWeight: DocumentTabFont(_documents[index]));

            if (ShouldShowDocumentTabClose(index))
            {
                DrawCloseIcon(item.DeviceContext, tab.Right - S(12), (tab.Top + tab.Bottom) / 2, muted);
            }
        }

        foreach (TransientEditorTabLayout transientLayout in GetTransientTabLayouts(width, tabs))
        {
            TransientEditorTab transient = transientLayout.Tab;
            NativeMethods.Rectangle special = transientLayout.Rectangle;
            bool selected = transient.Active;
            NativeMethods.Rectangle selectedTab = special;
            selectedTab.Left += S(2);
            selectedTab.Top += S(7);
            selectedTab.Right -= S(2);
            selectedTab.Bottom -= S(7);
            if (selected)
            {
                FillRoundedSurface(
                    item.DeviceContext,
                    selectedTab,
                    activeTabFill,
                    activeTabBorder,
                    S(6));
            }
            NativeMethods.Rectangle iconRectangle = new()
            {
                Left = special.Left + S(10),
                Top = (special.Top + special.Bottom) / 2 - S(8),
                Right = special.Left + S(26),
                Bottom = (special.Top + special.Bottom) / 2 + S(8),
            };
            NativeMethods.Rectangle titleRectangle = special;
            titleRectangle.Left += S(34);
            titleRectangle.Right -= S(24);
            if (transient.Kind == TransientEditorTabKind.ReferenceComparison)
            {
                _ = NativeTheme.DrawMenuActionIcon(item.DeviceContext, NativeContextMenuIcon.Compare, iconRectangle, muted);
                DrawComparisonTabCaption(item.DeviceContext, titleRectangle, selected ? text : muted);
            }
            else
            {
                DrawNavigationIcon(item.DeviceContext, iconRectangle, CommandGitChanges, muted);
                DrawText(
                    item.DeviceContext,
                    transient.Title,
                    titleRectangle,
                    selected ? text : muted,
                    centered: false,
                    fontWeight: transient.IsPreview ? NativeTheme.UiPreviewFont : NativeTheme.UiFont);
            }
            if (selected)
            {
                DrawCloseIcon(item.DeviceContext, special.Right - S(12), (special.Top + special.Bottom) / 2, muted);
            }
        }

        NativeMethods.Rectangle bottomLine = item.ItemRectangle;
        bottomLine.Top = Math.Max(bottomLine.Top, bottomLine.Bottom - S(1));
        Fill(item.DeviceContext, bottomLine, line);
        DrawEllipsis(item.DeviceContext, Math.Max(S(12), width - S(13)), TabHeight / 2, muted);
        return true;
    }

    private static nint DocumentTabFont(DocumentTabState document)
    {
        return document.IsPreview ? NativeTheme.UiPreviewFont : NativeTheme.UiFont;
    }

    private List<DocumentTabLayout> CalculateDocumentTabRectangles(int width, int? rightReserve = null)
    {
        int reserved = Math.Max(0, rightReserve ?? S(26));
        if (_documents.Count == 0 || width <= reserved)
        {
            return [];
        }

        int available = Math.Max(1, width - reserved);
        int[] desiredWidths = new int[_documents.Count];
        nint deviceContext = NativeMethods.GetDeviceContext(_documentTabs);
        try
        {
            for (int index = 0; index < _documents.Count; index++)
            {
                desiredWidths[index] = MeasureDocumentTabWidth(deviceContext, Path.GetFileName(_documents[index].Path), 110, 215);
            }
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(_documentTabs, deviceContext);
        }
        int desiredTotal = desiredWidths.Sum();
        bool compressed = desiredTotal > available;
        int visibleCount = compressed
            ? Math.Min(_documents.Count, Math.Max(1, available / S(76)))
            : _documents.Count;
        int firstIndex = 0;
        if (compressed)
        {
            int maximumFirstIndex = Math.Max(0, _documents.Count - visibleCount);
            firstIndex = Math.Clamp(_firstVisibleDocumentTabIndex, 0, maximumFirstIndex);
            if (_activeDocumentIndex < firstIndex)
            {
                firstIndex = Math.Max(0, _activeDocumentIndex);
            }
            else if (_activeDocumentIndex >= firstIndex + visibleCount)
            {
                firstIndex = Math.Clamp(
                    _activeDocumentIndex - visibleCount + 1,
                    0,
                    maximumFirstIndex);
            }

            _firstVisibleDocumentTabIndex = firstIndex;
        }
        else
        {
            _firstVisibleDocumentTabIndex = 0;
        }
        int sharedWidth = compressed ? Math.Max(S(76), available / visibleCount) : 0;
        List<DocumentTabLayout> rectangles = new(visibleCount);
        int left = 0;
        for (int index = firstIndex; index < firstIndex + visibleCount; index++)
        {
            int tabWidth = sharedWidth > 0
                ? sharedWidth
                : desiredWidths[index];
            rectangles.Add(new(
                index,
                new()
                {
                    Left = left,
                    Top = 0,
                    Right = Math.Min(available, left + tabWidth),
                    Bottom = TabHeight,
                }));
            left += tabWidth;
            if (left >= available)
            {
                break;
            }
        }

        return rectangles;
    }

    private static void DrawCloseIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawTabCloseIcon(deviceContext, centerX, centerY, color);
    }

    private static void DrawEllipsis(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawMoreIcon(deviceContext, centerX, centerY, color);
    }

    internal static int MeasureDocumentTabWidth(nint deviceContext, string title, int minimum, int maximum)
    {
        // 图标区、图文留白和关闭热区始终预留，悬停不改变文字的省略位置。
        return Math.Clamp(S(58) + MeasureTextWidth(deviceContext, title, NativeTheme.UiFont), S(minimum), S(maximum));
    }

    internal static (int WorkspaceWidth, int BranchLeft, int BranchWidth, int ContextLeft, int ContextWidth)
        MeasureTitleBarLayout(nint deviceContext, int width, string workspaceLabel, string branchLabel)
    {
        // 文字、箭头与内边距分别度量；长工作区与分支名称独立省略，保留右侧窗口操作空间。
        int workspaceWidth = Math.Clamp(MeasureTextWidth(deviceContext, workspaceLabel, NativeTheme.UiFont) + S(56), S(56), S(180));
        int branchWidth = Math.Clamp(MeasureTextWidth(deviceContext, branchLabel, NativeTheme.UiFont) + S(52), S(52), S(180));
        int branchLeft = S(73) + workspaceWidth + S(4);
        int contextWidth = MeasureTextWidth(deviceContext, GetCurrentFileContextText(), NativeTheme.UiFont) + S(28);
        int minimumLeft = branchLeft + branchWidth + S(8);
        int windowActionsLeft = Math.Max(S(420), width - S(33) * 3);
        int contextLeft = Math.Clamp(
            (width - contextWidth) / 2,
            minimumLeft,
            Math.Max(minimumLeft, windowActionsLeft - S(74) - contextWidth));
        return (workspaceWidth, branchLeft, branchWidth, contextLeft, contextWidth);
    }

    private int MeasureTransientTabWidth(string title, int minimum, int maximum)
    {
        nint deviceContext = NativeMethods.GetDeviceContext(_documentTabs);
        try
        {
            return MeasureDocumentTabWidth(deviceContext, title, minimum, maximum);
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(_documentTabs, deviceContext);
        }
    }

    private static int MeasureTextWidth(nint deviceContext, string text, nint font)
    {
        NativeMethods.Rectangle rectangle = new() { Left = 0, Top = 0, Right = 2000, Bottom = 64 };
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }

        return Math.Max(0, rectangle.Right - rectangle.Left);
    }

    private static void DrawDownArrow(nint deviceContext, int centerX, int centerY, uint color)
    {
        _ = NativeTheme.DrawChevronIcon(deviceContext, new()
        {
            Left = centerX - S(8),
            Top = centerY - S(8),
            Right = centerX + S(8),
            Bottom = centerY + S(8),
        }, expanded: true, color);
    }

    private static void DrawBranchButton(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        string label,
        uint color)
    {
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        NativeMethods.Rectangle iconRectangle = new()
        {
            Left = rectangle.Left + S(8),
            Top = centerY - S(8),
            Right = rectangle.Left + S(24),
            Bottom = centerY + S(8),
        };
        _ = NativeTheme.DrawToolWindowIcon(deviceContext, iconRectangle, NativeToolWindowIcon.GitHistory, color);

        NativeMethods.Rectangle textRectangle = rectangle;
        textRectangle.Left += S(32);
        textRectangle.Right = Math.Max(textRectangle.Left, rectangle.Right - S(20));
        DrawText(deviceContext, label, textRectangle, color, centered: false, fontWeight: NativeTheme.UiFont);
        int arrowCenter = CalculateInlineArrowCenter(
            rectangle,
            textRectangle.Left,
            MeasureTextWidth(deviceContext, label, NativeTheme.UiFont));
        DrawDownArrow(deviceContext, arrowCenter, (rectangle.Top + rectangle.Bottom) / 2, color);
    }

    private static void DrawCurrentFileButton(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        string label,
        uint color)
    {
        NativeMethods.Rectangle textRectangle = rectangle;
        textRectangle.Left += S(8);
        textRectangle.Right = Math.Max(textRectangle.Left, rectangle.Right - S(20));
        DrawText(deviceContext, label, textRectangle, color, centered: false, fontWeight: NativeTheme.UiFont);

        int centerX = CalculateInlineArrowCenter(
            rectangle,
            textRectangle.Left,
            MeasureTextWidth(deviceContext, label, NativeTheme.UiFont));
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        DrawDownArrow(deviceContext, centerX, centerY, color);
    }

    private static int CalculateInlineArrowCenter(
        NativeMethods.Rectangle rectangle,
        int textLeft,
        int textWidth)
    {
        // 文字后保留 4px 间隔和半个 8px 箭头；达到宽度上限时，箭头仍处于独立空间。
        int contentCenter = textLeft + textWidth + S(8);
        int rightAlignedCenter = rectangle.Right - S(12);
        return Math.Clamp(contentCenter, rectangle.Left + S(12), rightAlignedCenter);
    }

    private static string GetCurrentFileContextText()
    {
        // 标题栏固定显示“当前文件”入口，具体路径由编辑器面包屑和底部状态栏承担。
        return UiText.CurrentFile;
    }

    private string GetWorkspaceDisplayName()
    {
        return GetWorkspaceDisplayName(_workspaceRoot);
    }

    private static string GetWorkspaceDisplayName(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return UiText.AppName;
        }

        string name = Path.GetFileName(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? UiText.AppName : name;
    }

    internal string WorkspaceDisplayNameForTest => GetWorkspaceDisplayName();

    internal static string ResolveWorkspaceDisplayNameForTest(string? workspaceRoot) => GetWorkspaceDisplayName(workspaceRoot);

    private void UpdateCurrentFileContext()
    {
        UpdateStatusBar();
        if (_currentFileButton == 0 || _mainMenuOpen)
        {
            return;
        }

        _ = NativeMethods.SetWindowText(_currentFileButton, GetCurrentFileContextText());
        _ = NativeMethods.InvalidateRectangle(_currentFileButton, 0, false);
    }

    private static bool DrawProjectToolButton(
        NativeMethods.DrawItem item,
        NativeThemePalette palette)
    {
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
        bool highlighted = !disabled && (item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0;
        uint color = disabled ? palette.Faint : highlighted || (item.ItemState & NativeMethods.OwnerDrawFocus) != 0 ? palette.Text : palette.Muted;
        if (highlighted)
        {
            FillRounded(item.DeviceContext, item.ItemRectangle, palette.Hover, S(10));
        }
        if (!disabled) NativeTheme.DrawToolbarFocus(item, palette);

        int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        if (item.ControlIdentifier == CommandTreeOptions)
        {
            DrawEllipsis(item.DeviceContext, centerX, centerY, color);
            return true;
        }

        if (item.ControlIdentifier == CommandLocateActiveFile)
        {
            _ = NativeTheme.DrawLocateIcon(item.DeviceContext, centerX, centerY, color);
        }
        else if (item.ControlIdentifier == CommandCollapseTree)
        {
            _ = NativeTheme.DrawCollapseIcon(item.DeviceContext, centerX, centerY, color);
        }
        else
        {
            _ = NativeTheme.DrawHideIcon(item.DeviceContext, centerX, centerY, color);
        }
        return true;
    }

    private static void DrawProductButton(
        NativeMethods.DrawItem item,
        string label,
        uint text,
        uint accent)
    {
        NativeMethods.Rectangle logoRectangle = item.ItemRectangle;
        logoRectangle.Left += S(8);
        logoRectangle.Top = (item.ItemRectangle.Top + item.ItemRectangle.Bottom - S(20)) / 2;
        logoRectangle.Right = logoRectangle.Left + S(20);
        logoRectangle.Bottom = logoRectangle.Top + S(20);
        FillRounded(item.DeviceContext, logoRectangle, accent, S(5));
        DrawText(item.DeviceContext, "A", logoRectangle, Rgb(255, 255, 255), centered: true, fontWeight: NativeTheme.BrandFont);

        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        textRectangle.Left += S(36);
        textRectangle.Right = Math.Max(textRectangle.Left, item.ItemRectangle.Right - S(20));
        DrawText(item.DeviceContext, label, textRectangle, text, centered: false, fontWeight: NativeTheme.UiFont);
        int arrowCenter = CalculateInlineArrowCenter(
            item.ItemRectangle,
            textRectangle.Left,
            MeasureTextWidth(item.DeviceContext, label, NativeTheme.UiFont));
        DrawDownArrow(item.DeviceContext, arrowCenter, (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2, text);
    }

    private static string FormatProjectHeaderText()
    {
        return UiText.Project;
    }

    internal static string FormatProjectHeaderTextForTest()
    {
        return FormatProjectHeaderText();
    }

    private static void DrawApplicationIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint accent)
    {
        NativeMethods.Rectangle badge = rectangle;
        badge.Left += S(2);
        badge.Top += S(2);
        badge.Right -= S(2);
        badge.Bottom -= S(2);
        FillRounded(deviceContext, badge, accent, S(5));
        DrawText(
            deviceContext,
            "A",
            rectangle,
            Rgb(255, 255, 255),
            centered: true,
            fontWeight: NativeTheme.SmallBrandFont);
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }

    private int GetToolbarControlLeft(nint control)
    {
        return NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle controlRectangle)
            && NativeMethods.GetWindowRectangle(_handle, out NativeMethods.Rectangle windowRectangle)
                ? controlRectangle.Left - windowRectangle.Left
                : 0;
    }

    private static void FillToolbarGradient(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        int toolbarOffsetX)
    {
        int globalLeft = toolbarOffsetX + rectangle.Left;
        int globalRight = toolbarOffsetX + rectangle.Right;
        int peak = S(115);
        int fadeEnd = S(720);
        Span<int> boundaries = stackalloc int[4];
        int count = 0;
        boundaries[count++] = rectangle.Left;
        if (globalLeft < peak && peak < globalRight)
        {
            boundaries[count++] = rectangle.Left + peak - globalLeft;
        }

        if (globalLeft < fadeEnd && fadeEnd < globalRight)
        {
            boundaries[count++] = rectangle.Left + fadeEnd - globalLeft;
        }

        boundaries[count++] = rectangle.Right;
        for (int index = 0; index < count - 1; index++)
        {
            int left = boundaries[index];
            int right = boundaries[index + 1];
            NativeMethods.Rectangle segment = rectangle;
            segment.Left = left;
            segment.Right = right;
            FillHorizontalGradient(
                deviceContext,
                segment,
                ToolbarColorAt(toolbarOffsetX + left),
                ToolbarColorAt(toolbarOffsetX + right));
        }
    }

    private static void FillFrameAmbientGradient(
        nint deviceContext,
        NativeMethods.Rectangle rectangle)
    {
        if (rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
        {
            return;
        }

        int bandHeight = Math.Max(1, S(8));
        for (int top = rectangle.Top; top < rectangle.Bottom; top += bandHeight)
        {
            NativeMethods.Rectangle band = rectangle;
            band.Top = top;
            band.Bottom = Math.Min(rectangle.Bottom, top + bandHeight);
            int sampleY = band.Top + ((band.Bottom - band.Top) / 2);
            FillFrameAmbientBand(deviceContext, band, sampleY);
        }
    }

    private static void FillFrameAmbientBand(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        int y)
    {
        int peak = S(115);
        int fadeEnd = S(720);
        Span<int> boundaries = stackalloc int[4];
        int count = 0;
        boundaries[count++] = rectangle.Left;
        if (rectangle.Left < peak && peak < rectangle.Right)
        {
            boundaries[count++] = peak;
        }

        if (rectangle.Left < fadeEnd && fadeEnd < rectangle.Right)
        {
            boundaries[count++] = fadeEnd;
        }

        boundaries[count++] = rectangle.Right;
        for (int index = 0; index < count - 1; index++)
        {
            NativeMethods.Rectangle segment = rectangle;
            segment.Left = boundaries[index];
            segment.Right = boundaries[index + 1];
            FillHorizontalGradient(
                deviceContext,
                segment,
                FrameAmbientColorAt(segment.Left, y),
                FrameAmbientColorAt(segment.Right, y));
        }
    }

    private static uint FrameAmbientColorAt(int x, int y)
    {
        _ = x;
        _ = y;
        return NativeTheme.Palette(dark: false).Chrome;
    }

    private static uint ToolbarColorAt(int x)
    {
        NativeThemePalette palette = NativeTheme.Palette(dark: false);
        // PyCharm New UI 在标题栏左侧保留一段很轻的冷蓝环境渐变，
        // 右侧回到中性 Chrome；渐变只影响标题栏，不改变内容面板颜色。
        const int peakStart = 115;
        const int peakEnd = 360;
        const int fadeEnd = 720;
        uint tint = Rgb(211, 229, 245);
        if (x <= peakStart || x >= fadeEnd)
        {
            return palette.Chrome;
        }

        if (x <= peakEnd)
        {
            return Blend(palette.Chrome, tint, (x - peakStart) / (double)(peakEnd - peakStart));
        }

        return Blend(tint, palette.Chrome, (x - peakEnd) / (double)(fadeEnd - peakEnd));
    }

    private static uint Blend(uint start, uint end, double amount)
    {
        int startRed = unchecked((int)(start & 0xFF));
        int startGreen = unchecked((int)((start >> 8) & 0xFF));
        int startBlue = unchecked((int)((start >> 16) & 0xFF));
        int endRed = unchecked((int)(end & 0xFF));
        int endGreen = unchecked((int)((end >> 8) & 0xFF));
        int endBlue = unchecked((int)((end >> 16) & 0xFF));
        byte red = (byte)Math.Round(startRed + ((endRed - startRed) * amount));
        byte green = (byte)Math.Round(startGreen + ((endGreen - startGreen) * amount));
        byte blue = (byte)Math.Round(startBlue + ((endBlue - startBlue) * amount));
        return Rgb(red, green, blue);
    }

    private static void FillHorizontalGradient(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint leftColor,
        uint rightColor)
    {
        if (rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
        {
            return;
        }

        NativeMethods.TriVertex[] vertices =
        [
            CreateGradientVertex(rectangle.Left, rectangle.Top, leftColor),
            CreateGradientVertex(rectangle.Right, rectangle.Bottom, rightColor),
        ];
        NativeMethods.GradientRectangle[] mesh =
        [
            new NativeMethods.GradientRectangle { UpperLeft = 0, LowerRight = 1 },
        ];
        if (!NativeMethods.FillGradient(
                deviceContext,
                vertices,
                unchecked((uint)vertices.Length),
                mesh,
                unchecked((uint)mesh.Length),
                NativeMethods.GradientFillRectangleHorizontal))
        {
            Fill(deviceContext, rectangle, leftColor);
        }
    }

    private static NativeMethods.TriVertex CreateGradientVertex(int x, int y, uint color)
    {
        return new NativeMethods.TriVertex
        {
            X = x,
            Y = y,
            Red = (ushort)((color & 0xFF) << 8),
            Green = (ushort)(((color >> 8) & 0xFF) << 8),
            Blue = (ushort)(((color >> 16) & 0xFF) << 8),
            Alpha = 0,
        };
    }

    private static int S(int logicalPixels)
    {
        return NativeTheme.Scale(logicalPixels);
    }

    private static float S(float logicalPixels)
    {
        return NativeTheme.Scale(logicalPixels);
    }

    private void InvalidateNavigation()
    {
        foreach (nint button in new[] { _filesButton, _gitButton, _searchButton, _terminalButton, _historyButton })
        {
            if (button != 0)
            {
                _ = NativeMethods.InvalidateRectangle(button, 0, true);
            }
        }
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        if (command == DocumentTabsControlIdentifier)
        {
            HandleDocumentTabsClick();
            return;
        }

        if (_mainMenuOpen
            && command is (CommandRecentWorkspaces
                or CommandBranch
                or CommandCurrentFile
                or CommandQuickOpen
                or CommandSettings))
        {
            switch (command)
            {
                case CommandRecentWorkspaces:
                    ShowFileMenu();
                    return;
                case CommandBranch:
                    ShowViewMenu();
                    return;
                case CommandCurrentFile:
                    ShowGitMenu();
                    return;
                case CommandQuickOpen:
                    _mainMenuOpen = false;
                    Layout();
                    UpdateMainMenuToolTips();
                    InvalidateTopBar();
                    ToggleTerminalPanel();
                    return;
                case CommandSettings:
                    _mainMenuOpen = false;
                    Layout();
                    UpdateMainMenuToolTips();
                    InvalidateTopBar();
                    ShowAppearanceSettings();
                    return;
            }
        }

        switch (command)
        {
            case CommandMainMenu:
                ShowMainMenu();
                break;
            case CommandOpenFolder:
                ShowWorkspaceOpenDialog();
                break;
            case CommandClone:
                ShowCloneDialog();
                break;
            case CommandRefresh:
                _ = RefreshWorkspaceAsync();
                break;
            case CommandQuickOpen:
            case CommandCurrentFile:
                ShowQuickOpen();
                break;
            case CommandWorkspaceSearch:
                ToggleWorkspaceSearchPanel();
                break;
            case CommandSettings:
                ShowAppearanceSettings();
                break;
            case CommandFiles:
                ToggleProjectPanel();
                break;
            case CommandGitChanges:
                ToggleGitChangesPanel();
                break;
            case CommandHistory:
                ToggleHistoryPanel();
                break;
            case CommandRecentWorkspaces:
                ShowRecentWorkspaces();
                break;
            case CommandTerminal:
                ToggleTerminalPanel();
                break;
            case CommandHideTerminal:
                HideTerminalPanel();
                break;
            case CommandCloseTerminal:
            case TerminalSessionCloseControlIdentifier:
                _ = CloseTerminal(requireConfirmation: true);
                break;
            case CommandTerminalMore:
                ShowTerminalMoreMenu();
                break;
            case CommandBranch:
                _ = ShowBranchMenuAsync();
                break;
            case CommandMinimize:
                _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowMinimized);
                break;
            case CommandMaximize:
                _ = NativeMethods.ShowWindow(
                    _handle,
                    NativeMethods.IsZoomed(_handle) ? NativeMethods.ShowRestore : NativeMethods.ShowMaximized);
                break;
            case CommandCloseWindow:
                if (CloseTerminal(requireConfirmation: true))
                {
                    Close();
                }
                break;
            case CommandLocateActiveFile:
                _ = LocateActiveFileAsync();
                break;
            case CommandCollapseTree:
                CollapseTree();
                break;
            case CommandTreeOptions:
                ShowTreeOptionsMenu();
                break;
            case CommandHideProject:
                HideProjectPanel();
                break;
            case CommandCancelBranch:
                _branchOperationCancellation?.Cancel();
                break;
            case CommandCancelOperationNotification:
                CancelVisibleGitOperation();
                break;
            case CommandConfigureGit:
                bool wasGitUnavailable = _gitRuntimeUnavailable;
                if (wasGitUnavailable)
                {
                    _gitUnavailableNoticeShown = false;
                }
                HideOperationNotification();
                ShowAppearanceSettings();
                if (wasGitUnavailable && _gitRuntimeUnavailable)
                {
                    ShowGitUnavailableNotice();
                }
                break;
        }
    }

    private void CancelVisibleGitOperation()
    {
        bool cancelled = false;
        if (_branchOperationCancellation is not null)
        {
            _branchOperationCancellation.Cancel();
            cancelled = true;
        }

        cancelled |= _gitPanel?.CancelOperationForHost() == true;
        cancelled |= _historyPanel?.CancelOperationForHost() == true;
        if (cancelled)
        {
            ShowOperationProgress(
                "正在取消 Git 操作…",
                "本机 Git 停止后将重新读取仓库状态。",
                canCancel: false);
        }
    }

    private nint HandleNotification(nint longParameter)
    {
        if (longParameter == 0)
        {
            return 0;
        }

        NativeMethods.NotificationHeader header = Marshal.PtrToStructure<NativeMethods.NotificationHeader>(longParameter);
        if (header.WindowFrom == _fileTree)
        {
            if (header.Code == NativeMethods.NotificationCustomDraw)
            {
                return DrawTreeItem(longParameter);
            }

            if (header.Code is NativeMethods.TreeViewNotificationItemExpanding
                or NativeMethods.TreeViewNotificationSelectionChanged)
            {
                NativeMethods.TreeViewNotification notification = Marshal.PtrToStructure<NativeMethods.TreeViewNotification>(longParameter);
                TreeNodeState? node = GetTreeNode(notification.NewItem.Parameter);
                if (header.Code == NativeMethods.TreeViewNotificationItemExpanding && node is not null)
                {
                    node.IsExpanded = (notification.Action & TreeViewActionExpand) != 0;
                    if (node.IsExpanded)
                    {
                        _ = LoadTreeNodeAsync(node);
                    }

                    return 0;
                }

                if (header.Code == NativeMethods.TreeViewNotificationSelectionChanged && node is not null)
                {
                    return 0;
                }
            }

            if (header.Code == NativeMethods.NotificationClick)
            {
                HandleTreeClick();
                return 0;
            }

            if (header.Code == NativeMethods.NotificationDoubleClick)
            {
                HandleTreeDoubleClick(GetTreeNodeAtCursor(out _));
                return 1;
            }

            if (header.Code == NativeMethods.TreeViewNotificationRightClick)
            {
                ShowTreeContextMenu();
                return 0;
            }
        }

        return 0;
    }

    private nint DrawTreeItem(nint parameter)
    {
        NativeMethods.TreeViewCustomDraw draw = Marshal.PtrToStructure<NativeMethods.TreeViewCustomDraw>(parameter);
        if (draw.CustomDraw.DrawStage == NativeMethods.CustomDrawPrePaint)
        {
            return NativeMethods.CustomDrawNotifyItemDraw;
        }

        if (draw.CustomDraw.DrawStage != NativeMethods.CustomDrawItemPrePaint)
        {
            return NativeMethods.CustomDrawDefault;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        nint selectedItem = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewGetNextItem,
            NativeMethods.TreeViewCaret,
            0);
        bool selected = draw.CustomDraw.ItemSpec == unchecked((nuint)selectedItem);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint panel = palette.Panel;
        uint text = palette.Text;
        uint muted = palette.Muted;
        // 失焦保留当前行，以中性背景区分当前键盘操作区域，不改变选择。
        uint selection = NativeTheme.SelectionColor(palette, NativeMethods.GetFocus() == _fileTree);
        NativeMethods.Rectangle rectangle = draw.CustomDraw.Rectangle;
        // ComCtl32 在项目树滚动或重绘时可能为屏幕外节点发出空矩形通知。
        // 该通知没有可绘制的客户区，必须在计算图标中心前丢弃，避免污染树顶缘。
        if (rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
        {
            return NativeMethods.CustomDrawSkipDefault;
        }
        Fill(draw.CustomDraw.DeviceContext, rectangle, panel);
        bool hovered = draw.CustomDraw.ItemSpec == unchecked((nuint)_hoveredTreeItem);
        uint rowBackground = selected ? selection : hovered ? palette.Hover : panel;
        if (selected || hovered)
        {
            NativeMethods.Rectangle highlight = rectangle;
            highlight.Left = S(3);
            highlight.Right -= S(3);
            FillRounded(draw.CustomDraw.DeviceContext, highlight, rowBackground, S(5));
        }

        int identifier = unchecked((int)draw.CustomDraw.ItemParameter);
        TreeNodeState? node = GetTreeNode(identifier);
        if (node is null)
        {
            NativeMethods.Rectangle placeholder = rectangle;
            placeholder.Left += S(8) + draw.Level * S(13);
            DrawText(draw.CustomDraw.DeviceContext, "…", placeholder, muted, centered: false, fontWeight: NativeTheme.UiFont);
            return NativeMethods.CustomDrawSkipDefault;
        }

        GitChangeKind? gitStatus = node.IsDirectory ? null : _treeGitStatusIndex?.Resolve(node.FullPath);
        // 文件图标保持类型色，文件名按 Git 状态着色，与参考项目树和标签一致。
        uint nodeText = ResolveTreeFileTextColor(palette, gitStatus, dark);
        int left = GetTreeChevronLeft(draw.Level);
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        if (node.CanExpand)
        {
            DrawTreeChevron(draw.CustomDraw.DeviceContext, left, centerY, node.IsExpanded, muted);
        }

        int iconLeft = left + S(12);
        if (node.IsDirectory)
        {
            NativeMethods.Rectangle icon = new()
            {
                Left = iconLeft,
                Top = centerY - S(8),
                Right = iconLeft + S(16),
                Bottom = centerY + S(8),
            };
            _ = NativeTheme.DrawFolderIcon(
                draw.CustomDraw.DeviceContext,
                icon,
                dark,
                workspaceRoot: node.ParentId == 0,
                background: rowBackground);
        }
        else
        {
            DrawTreeFileIcon(
                draw.CustomDraw.DeviceContext,
                iconLeft,
                centerY,
                node.Name,
                dark);
        }

        NativeMethods.Rectangle nameRectangle = rectangle;
        nameRectangle.Left = iconLeft + S(24);
        nameRectangle.Right -= S(6);
        int rootNameRight = node.ParentId == 0
            ? nameRectangle.Left + CalculateRootNameWidth(draw.CustomDraw.DeviceContext, node.Name)
            : 0;
        if (node.ParentId == 0)
        {
            nameRectangle.Right = Math.Min(nameRectangle.Right, rootNameRight);
        }
        DrawText(
            draw.CustomDraw.DeviceContext,
            node.Name,
            nameRectangle,
            nodeText,
            centered: false,
            fontWeight: node.ParentId == 0 ? NativeTheme.UiMediumFont : NativeTheme.UiFont);
        if (node.ParentId == 0)
        {
            NativeMethods.Rectangle pathRectangle = nameRectangle;
            pathRectangle.Left = rootNameRight + S(8);
            pathRectangle.Right = rectangle.Right - S(6);
            // 根路径紧跟实际名称宽度，不能靠右对齐在名称后留下随面板宽度变化的空洞。
            DrawText(draw.CustomDraw.DeviceContext, node.FullPath, pathRectangle, muted, centered: false, fontWeight: NativeTheme.UiFont);
        }

        return NativeMethods.CustomDrawSkipDefault;
    }

    private static uint ResolveTreeFileTextColor(
        NativeThemePalette palette,
        GitChangeKind? status,
        bool dark)
    {
        return status is { } kind
            ? NativeGitStatusPalette.Resolve(kind, dark)
            : palette.Text;
    }

    internal static int CalculateRootNameWidthForTest(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        nint deviceContext = NativeMethods.GetDeviceContext(0);
        try { return CalculateRootNameWidth(deviceContext, name); }
        finally { _ = NativeMethods.ReleaseDeviceContext(0, deviceContext); }
    }

    private static int CalculateRootNameWidth(nint deviceContext, string name)
    {
        return Math.Min(MeasureTextWidth(deviceContext, name, NativeTheme.UiMediumFont), S(180));
    }

    private void HandleTreeClick()
    {
        TreeNodeState? node = GetTreeNodeAtCursor(out NativeMethods.TreeViewHitTestInfo hit);
        if (node is null
            || !node.CanExpand
            || (hit.Flags & NativeMethods.TreeViewHitOnItemButton) != 0)
        {
            return;
        }

        int left = GetTreeChevronLeft(GetTreeLevel(node));
        if (hit.Point.X >= left - S(2) && hit.Point.X <= left + S(8))
        {
            QueueTreeToggle(node);
        }
    }

    private void HandleTreeDoubleClick(TreeNodeState? node)
    {
        if (node is { IsDirectory: true, CanExpand: true })
        {
            ToggleTreeNode(node);
        }
        else if (node is { IsDirectory: false })
        {
            _ = OpenTreeNodeAsync(node);
        }
    }

    private void QueueTreeToggle(TreeNodeState node)
    {
        bool previousState = IsTreeItemExpanded(node.ItemHandle);
        node.IsExpanded = previousState;
        Post(() =>
        {
            TreeNodeState? current = ResolveCurrentTreeNode(node);
            if (current is { IsDirectory: true, CanExpand: true }
                && IsTreeItemExpanded(current.ItemHandle) == previousState)
            {
                ToggleTreeNode(current);
            }
        });
    }

    private TreeNodeState? GetTreeNodeAtCursor(out NativeMethods.TreeViewHitTestInfo hit)
    {
        hit = default;
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point)
            || !NativeMethods.ScreenToClient(_fileTree, ref point))
        {
            return null;
        }

        hit.Point = point;
        _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewHitTest, 0, ref hit);
        return GetTreeNodeFromItem(hit.Item);
    }

    private bool TryGetTreeItemRectangle(nint item, out NativeMethods.Rectangle rectangle)
    {
        rectangle = default;
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.Rectangle>());
        try
        {
            // TVM_GETITEMRECT 在 RECT 首地址内接收 HTREEITEM，x64 下不能借用 32 位的 Left 字段传值。
            Marshal.WriteIntPtr(buffer, item);
            if (NativeMethods.SendMessage(
                    _fileTree,
                    NativeMethods.TreeViewGetItemRectangle,
                    0,
                    buffer) == 0)
            {
                return false;
            }

            rectangle = Marshal.PtrToStructure<NativeMethods.Rectangle>(buffer);
            return rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private async Task<bool> WaitForTreeToggleAsync(TreeNodeState node, bool previousState)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            TreeNodeState? current = ResolveCurrentTreeNode(node);
            if (current is null)
            {
                await Task.Delay(10);
                continue;
            }

            bool nativeState = IsTreeItemExpanded(current.ItemHandle);
            if (nativeState != previousState)
            {
                current.IsExpanded = nativeState;
                return true;
            }

            await Task.Delay(10);
        }

        TreeNodeState? finalNode = ResolveCurrentTreeNode(node);
        return finalNode is not null
            && IsTreeItemExpanded(finalNode.ItemHandle) != previousState;
    }

    private TreeNodeState? ResolveCurrentTreeNode(TreeNodeState node)
    {
        return GetTreeNode(node.Id) == node
            ? node
            : _treeNodes.Values.FirstOrDefault(
                candidate => candidate.FullPath.Equals(node.FullPath, StringComparison.OrdinalIgnoreCase));
    }

    private int GetTreeLevel(TreeNodeState node)
    {
        int level = 0;
        int parentId = node.ParentId;
        while (parentId > 0 && _treeNodes.TryGetValue(parentId, out TreeNodeState? parent))
        {
            level++;
            parentId = parent.ParentId;
        }

        return level;
    }

    private static int GetTreeChevronLeft(int level)
    {
        return S(6) + Math.Max(0, level) * S(16);
    }

    private void ToggleTreeNode(TreeNodeState node)
    {
        if (!node.CanExpand)
        {
            return;
        }

        bool currentState = IsTreeItemExpanded(node.ItemHandle);
        node.IsExpanded = currentState;
        bool expand = !currentState;
        if (IsTreeItemExpanded(node.ItemHandle) != expand)
        {
            _ = NativeMethods.SendMessage(
                _fileTree,
                NativeMethods.TreeViewExpand,
                expand ? NativeMethods.TreeViewExpandItem : NativeMethods.TreeViewCollapseItem,
                node.ItemHandle);
        }

        if (IsTreeItemExpanded(node.ItemHandle) == expand)
        {
            node.IsExpanded = expand;
            if (expand && !node.IsLoaded)
            {
                _ = LoadTreeNodeAsync(node);
            }

            _ = NativeMethods.InvalidateRectangle(_fileTree, 0, false);
        }
    }

    private bool IsTreeItemExpanded(nint itemHandle)
    {
        NativeMethods.TreeViewItem item = new()
        {
            Mask = NativeMethods.TreeViewItemState,
            Item = itemHandle,
            StateMask = NativeMethods.TreeViewStateExpanded,
        };
        return NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewGetItem, 0, ref item) != 0
            && (item.State & NativeMethods.TreeViewStateExpanded) != 0;
    }

    private Task OpenTreeNodeAsync(TreeNodeState node)
    {
        if (node.IsDirectory)
        {
            ToggleTreeNode(node);
            return Task.CompletedTask;
        }

        return OpenDocumentAsync(node.FullPath);
    }

    private static void DrawTreeChevron(nint deviceContext, int left, int centerY, bool expanded, uint color)
    {
        _ = NativeTheme.DrawChevronIcon(deviceContext, new()
        {
            Left = left,
            Right = left + S(8),
            Top = centerY - S(4),
            Bottom = centerY + S(4),
        }, expanded, color);
    }

    private static void DrawTreeFileIcon(
        nint deviceContext,
        int left,
        int centerY,
        string name,
        bool dark)
    {
        NativeMethods.Rectangle rectangle = new()
        {
            Left = left,
            Top = centerY - S(8),
            Right = left + S(16),
            Bottom = centerY + S(8),
        };
        _ = NativeTheme.DrawFileTypeIcon(deviceContext, rectangle, name, dark);
    }

    private void HandleDocumentTabsClick()
    {
        if (_pendingDocumentTabClickPoint is not { } messagePoint)
        {
            return;
        }

        _pendingDocumentTabClickPoint = null;
        HandleDocumentTabsClickAt(messagePoint);
    }

    private void HandleDocumentTabsClickAt(NativeMethods.Point point)
    {

        if (!NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = client.Right - client.Left;
        List<DocumentTabLayout> tabs = CalculateDocumentTabRectangles(width, GetDocumentTabRightReserve());
        foreach (TransientEditorTabLayout transientLayout in GetTransientTabLayouts(width, tabs))
        {
            if (!IsPointInside(point, transientLayout.Rectangle))
            {
                continue;
            }

            if (transientLayout.Tab.Active
                && point.X >= transientLayout.Rectangle.Right - S(28))
            {
                CloseTransientTab(transientLayout.Tab);
            }
            else
            {
                ActivateTransientTab(transientLayout.Tab);
            }
            return;
        }

        foreach (DocumentTabLayout layout in tabs)
        {
            int index = layout.DocumentIndex;
            NativeMethods.Rectangle rectangle = layout.Rectangle;
            if (point.X < rectangle.Left
                || point.X > rectangle.Right
                || point.Y < rectangle.Top
                || point.Y > rectangle.Bottom)
            {
                continue;
            }

            bool closeHit = ShouldShowDocumentTabClose(index)
                && point.X >= rectangle.Right - S(28)
                && point.X <= rectangle.Right;
            if (closeHit)
            {
                CloseDocument(index);
                return;
            }

            SelectDocument(index);
            return;
        }

        if (point.X >= client.Right - S(30))
        {
            ShowDocumentTabsMenu(point);
        }
    }

    private string GetReferenceComparisonTabTitle()
    {
        return NativeGitComparisonCaption.Create(_referenceComparisonDocument).Title;
    }

    private void DrawComparisonTabCaption(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        NativeGitComparisonCaption caption = NativeGitComparisonCaption.Create(_referenceComparisonDocument);
        var widths = MeasureComparisonTabCaption(deviceContext, Math.Max(0, rectangle.Right - rectangle.Left), caption);
        DrawPart(caption.FileTitle, widths.File);
        DrawPart(" · ", widths.Separator);
        DrawPart(caption.BaseRevision, widths.Base);
        DrawPart(" → ", widths.Arrow);
        DrawPart(caption.TargetRevision, widths.Target);

        void DrawPart(string text, int width)
        {
            NativeMethods.Rectangle part = rectangle;
            part.Right = part.Left + width;
            if (width > 0)
            {
                DrawText(deviceContext, text, part, color, centered: false, fontWeight: NativeTheme.UiPreviewFont);
            }
            rectangle.Left = part.Right;
        }
    }

    internal static (int File, int Separator, int Base, int Arrow, int Target) MeasureComparisonTabCaption(
        nint deviceContext, int available, NativeGitComparisonCaption caption)
    {
        available = Math.Max(0, available);
        int separator = Math.Min(available / 10, MeasureTextWidth(deviceContext, " · ", NativeTheme.UiPreviewFont));
        int arrow = Math.Min(available / 8, MeasureTextWidth(deviceContext, " → ", NativeTheme.UiPreviewFont));
        int content = available - separator - arrow;
        int file = MeasureTextWidth(deviceContext, caption.FileTitle, NativeTheme.UiPreviewFont);
        int baseWidth = MeasureTextWidth(deviceContext, caption.BaseRevision, NativeTheme.UiPreviewFont);
        int target = MeasureTextWidth(deviceContext, caption.TargetRevision, NativeTheme.UiPreviewFont);
        if (file + baseWidth + target <= content)
        {
            return (file, separator, baseWidth, arrow, target);
        }

        // 文件名和双方引用独立省略。短内容的空余空间交给其他列，长名称不能挤掉另一侧身份。
        file = Math.Min(file, Math.Max(content * 2 / 5, content - baseWidth - target));
        int references = content - file;
        baseWidth = Math.Min(baseWidth, Math.Max(references / 2, references - target));
        target = Math.Min(target, references - baseWidth);
        return (file, separator, baseWidth, arrow, target);
    }

    private static string GetGitDiffTabTitle(string relativePath)
    {
        return $"提交: {Path.GetFileName(relativePath)}";
    }

    private List<TransientEditorTab> BuildTransientEditorTabs()
    {
        List<TransientEditorTab> tabs = [];
        if (_previewGitDiffPath is { } preview)
        {
            string title = GetGitDiffTabTitle(preview);
            tabs.Add(new(
                TransientEditorTabKind.GitDiff,
                preview,
                title,
                MeasureTransientTabWidth(title, 140, 260),
                _showingGitDiff
                    && _gitDiffRelativePath?.Equals(preview, StringComparison.OrdinalIgnoreCase) == true,
                true));
        }

        if (_referenceComparisonDocument is not null)
        {
            string title = GetReferenceComparisonTabTitle();
            tabs.Add(new(
                TransientEditorTabKind.ReferenceComparison,
                string.Empty,
                title,
                MeasureTransientTabWidth(title, 210, 420),
                _showingReferenceComparison,
                true));
        }

        return tabs;
    }

    private int GetDocumentTabRightReserve()
    {
        return S(26) + BuildTransientEditorTabs().Sum(tab => tab.Width);
    }

    private List<TransientEditorTabLayout> GetTransientTabLayouts(
        int width,
        IReadOnlyList<DocumentTabLayout>? documentTabs = null)
    {
        List<TransientEditorTab> transientTabs = BuildTransientEditorTabs();
        if (transientTabs.Count == 0 || width <= S(26))
        {
            return [];
        }

        IReadOnlyList<DocumentTabLayout> tabs = documentTabs
            ?? CalculateDocumentTabRectangles(width, GetDocumentTabRightReserve());
        int left = tabs.Count == 0 ? 0 : tabs[^1].Rectangle.Right;
        int maximumRight = Math.Max(0, width - S(26));
        List<TransientEditorTabLayout> layouts = [];
        foreach (TransientEditorTab tab in transientTabs)
        {
            int right = Math.Min(maximumRight, left + tab.Width);
            if (right <= left)
            {
                break;
            }

            layouts.Add(new(
                tab,
                new()
                {
                    Left = left,
                    Top = 0,
                    Right = right,
                    Bottom = TabHeight,
                }));
            left = right;
        }

        return layouts;
    }

    private static bool IsPointInside(NativeMethods.Point point, NativeMethods.Rectangle rectangle)
    {
        return point.X >= rectangle.Left
            && point.X <= rectangle.Right
            && point.Y >= rectangle.Top
            && point.Y <= rectangle.Bottom;
    }

    private void ActivateTransientTab(TransientEditorTab tab)
    {
        if (tab.Kind == TransientEditorTabKind.ReferenceComparison)
        {
            ActivateReferenceComparisonTab();
        }
        else
        {
            _ = ActivateGitDiffTabAsync(tab.Key);
        }
    }

    private void CloseTransientTab(TransientEditorTab? requestedTab = null)
    {
        TransientEditorTab? tab = requestedTab;
        if (tab is null)
        {
            foreach (TransientEditorTab candidate in BuildTransientEditorTabs())
            {
                if (candidate.Active)
                {
                    tab = candidate;
                    break;
                }
            }
        }
        if (tab is null)
        {
            return;
        }

        if (tab.Value.Kind == TransientEditorTabKind.ReferenceComparison)
        {
            CloseReferenceComparison();
            return;
        }

        bool active = tab.Value.Active;
        _ = RemoveGitDiffTabState(tab.Value.Key);
        _gitPanel?.CloseDiffForHost(tab.Value.Key);
        if (active)
        {
            _showingGitDiff = false;
            _gitDiffRelativePath = null;
            RestoreActiveDocumentSurface();
        }

        if (active) Layout();
        UpdateCurrentFileContext();
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
    }

    private void DeactivateTransientTab()
    {
        if (_showingReferenceComparison)
        {
            _showingReferenceComparison = false;
            _comparisonView?.SetVisible(false);
        }

        if (_showingGitDiff)
        {
            _preserveGitDiffTabsOnHide = true;
            try
            {
                _gitPanel?.HideDiffForHost();
            }
            finally
            {
                _preserveGitDiffTabsOnHide = false;
            }
            _showingGitDiff = false;
            _gitDiffRelativePath = null;
        }
    }

    private async Task ActivateGitDiffTabAsync(string relativePath)
    {
        if (_disposed || _workspaceRoot is null)
        {
            return;
        }

        if (_showingReferenceComparison)
        {
            _showingReferenceComparison = false;
            _comparisonView?.SetVisible(false);
        }

        if (!_showingGitPanel)
        {
            ShowGitChangesPanel();
        }

        if (_gitPanel is null || !await _gitPanel.ShowFileDiffForHostAsync(relativePath))
        {
            RemoveGitDiffTabState(relativePath);
            Layout();
            _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
        }
    }

    private void ActivateReferenceComparisonTab()
    {
        if (_referenceComparisonDocument is null || _comparisonView is null)
        {
            return;
        }

        AdvanceDocumentSelection();

        if (_showingGitDiff)
        {
            _preserveGitDiffTabsOnHide = true;
            try
            {
                _gitPanel?.HideDiffForHost();
            }
            finally
            {
                _preserveGitDiffTabsOnHide = false;
            }
        }

        _showingGitDiff = false;
        _gitDiffRelativePath = null;
        _showingReferenceComparison = true;
        foreach (DocumentTabState document in _documents)
        {
            document.View?.SetVisible(false);
        }
        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowHide);
        _comparisonView.SetVisible(true);
        Layout();
        UpdateCurrentFileContext();
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
        _ = NativeMethods.SetFocus(_comparisonView.Handle);
    }

    private void RestoreActiveDocumentSurface()
    {
        _comparisonView?.SetVisible(false);
        if (_activeDocumentIndex >= 0 && _activeDocumentIndex < _documents.Count && ActiveDocument is null)
        {
            _ = SelectDocumentAsync(_activeDocumentIndex);
            return;
        }
        if (_documents.Count == 0 || ActiveDocument is null)
        {
            _ = NativeMethods.SetWindowText(_emptyDocumentLabel, UiText.NoDocument);
            _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowNormal);
            return;
        }

        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowHide);
        ActiveDocument.SetVisible(true);
        _ = NativeMethods.SetWindowPosition(
            ActiveDocument.Handle,
            NativeMethods.WindowPositionTop,
            0,
            0,
            0,
            0,
            NativeMethods.SetWindowPositionNoMove
                | NativeMethods.SetWindowPositionNoSize
                | NativeMethods.SetWindowPositionNoActivate
                | NativeMethods.SetWindowPositionShowWindow);
    }

    private static nint HandleDocumentTabsMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = wordParameter;
        _ = subclassIdentifier;
        _ = referenceData;
        MainWindow? instance = FindInstanceForMessage(window);
        if (instance is not null)
        {
            if (message == NativeMethods.WindowMessageLeftButtonDown)
            {
                instance._documentTabPointerPressed = true;
                instance._documentTabCloseTarget = instance.FindDocumentTabCloseTarget(DecodeClientPoint(longParameter));
                instance._transientTabCloseTarget = instance.FindTransientTabCloseTarget(DecodeClientPoint(longParameter));
                if (instance._documentTabCloseTarget is not null || instance._transientTabCloseTarget is not null)
                {
                    // 关闭叉不激活标签栏按钮，避免按下时就把焦点从树或正文夺走。
                    _ = NativeMethods.SetCapture(window);
                    return 0;
                }
            }

            if (message == NativeMethods.WindowMessageMouseMove)
            {
                instance.TrackDocumentTabMouseLeave();
                instance.UpdateDocumentTabHover(DecodeClientPoint(longParameter));
            }

            if (message == NativeMethods.WindowMessageMouseLeave)
            {
                instance._trackingDocumentTabMouseLeave = false;
                instance.ClearDocumentTabHover();
                return 0;
            }

            if (message == NativeMethods.WindowMessageMiddleButtonUp)
            {
                instance.CloseDocumentTabAt(DecodeClientPoint(longParameter));
                return 0;
            }

            if (message == NativeMethods.WindowMessageLeftButtonUp)
            {
                NativeMethods.Point point = DecodeClientPoint(longParameter);
                if (!instance._documentTabPointerPressed)
                {
                    return NativeMethods.DefaultSubclassProcedure(
                        window,
                        message,
                        wordParameter,
                        longParameter);
                }

                instance._documentTabPointerPressed = false;
                if (instance._documentTabCloseTarget is { } closeTarget)
                {
                    bool close = ReferenceEquals(closeTarget, instance.FindDocumentTabCloseTarget(point));
                    instance._documentTabCloseTarget = null;
                    _ = NativeMethods.ReleaseCapture();
                    if (close) instance.CloseDocument(instance._documents.IndexOf(closeTarget));
                    return 0;
                }
                if (instance._transientTabCloseTarget is { } transientTarget)
                {
                    TransientEditorTab? released = instance.FindTransientTabCloseTarget(point);
                    bool close = released is { } current && current.Kind == transientTarget.Kind
                        && string.Equals(current.Key, transientTarget.Key, StringComparison.OrdinalIgnoreCase);
                    instance._transientTabCloseTarget = null;
                    _ = NativeMethods.ReleaseCapture();
                    if (close) instance.CloseTransientTab(released);
                    return 0;
                }
                // 父级 BN_CLICKED 在默认按钮过程内同步发送；暂存消息坐标供其命中标签，
                // 同时让按钮完成按下状态与鼠标捕获的原生收尾。
                instance._pendingDocumentTabClickPoint = point;
                instance._documentTabPointerReleaseInProgress = true;
                nint result;
                try
                {
                    result = NativeMethods.DefaultSubclassProcedure(
                        window,
                        message,
                        wordParameter,
                        longParameter);
                }
                finally
                {
                    instance._documentTabPointerReleaseInProgress = false;
                }

                if (instance._pendingDocumentTabClickPoint is not null)
                {
                    instance._pendingDocumentTabClickPoint = null;
                    instance.HandleDocumentTabsClickAt(point);
                }
                return result;
            }

            if (message == NativeMethods.WindowMessageCaptureChanged)
            {
                instance._documentTabPointerPressed = false;
                instance._documentTabCloseTarget = null;
                instance._transientTabCloseTarget = null;
                if (!instance._documentTabPointerReleaseInProgress)
                {
                    instance._pendingDocumentTabClickPoint = null;
                }
            }

            if (message == NativeMethods.WindowMessageContextMenu)
            {
                instance.ShowDocumentTabContextMenu(longParameter);
                return 0;
            }
        }

        return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
    }

    private static nint HandleFileTreeMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        MainWindow? instance = FindInstanceForMessage(window);
        if (instance is not null && message == NativeMethods.WindowMessageNonClientDestroy)
        {
            instance.ClearTreeHover();
            _ = NativeMethods.RemoveWindowSubclass(window, FileTreeProcedure, FileTreeSubclassIdentifier);
        }
        if (instance is not null && message is NativeMethods.WindowMessageMouseWheel
            or NativeMethods.WindowMessageVerticalScroll or NativeMethods.WindowMessageKeyDown
            or NativeMethods.WindowMessageLeftButtonDown or NativeMethods.TreeViewSelectItem)
            instance._treeInteractionRevision++;
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (instance is not null && message == NativeMethods.TreeViewExpand
            && instance.GetTreeNodeFromItem(longParameter) is { } expandedNode)
            expandedNode.IsExpanded = instance.IsTreeItemExpanded(longParameter);
        instance?.HandleTreeHoverMessage(message, wordParameter, longParameter);
        if (instance is not null
            && message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
        {
            _ = NativeMethods.InvalidateRectangle(window, 0, true);
        }

        return result;
    }

    private static nint HandleOperationNotificationMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = longParameter;
        _ = subclassIdentifier;
        _ = referenceData;
        MainWindow? instance = FindInstanceForMessage(window);
        if (instance is not null && message == NativeMethods.WindowMessageCommand)
        {
            instance.HandleCommand(wordParameter);
            return 0;
        }

        return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
    }

    private void TrackDocumentTabMouseLeave()
    {
        if (_trackingDocumentTabMouseLeave)
        {
            return;
        }

        NativeMethods.TrackMouseEvent tracking = new()
        {
            Size = unchecked((uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>()),
            Flags = NativeMethods.TrackMouseEventLeave,
            Window = _documentTabs,
        };
        _trackingDocumentTabMouseLeave = NativeMethods.TrackMouse(ref tracking);
    }

    private void UpdateDocumentTabHover(NativeMethods.Point point)
    {
        int hoveredIndex = -1;
        if (NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client))
        {
            foreach (DocumentTabLayout layout in CalculateDocumentTabRectangles(
                client.Right - client.Left,
                GetDocumentTabRightReserve()))
            {
                NativeMethods.Rectangle rectangle = layout.Rectangle;
                if (point.X >= rectangle.Left
                    && point.X <= rectangle.Right
                    && point.Y >= rectangle.Top
                    && point.Y <= rectangle.Bottom)
                {
                    hoveredIndex = layout.DocumentIndex;
                    break;
                }
            }
        }

        if (_hoveredDocumentTabIndex == hoveredIndex)
        {
            return;
        }

        _hoveredDocumentTabIndex = hoveredIndex;
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
    }

    private void ClearDocumentTabHover()
    {
        if (_hoveredDocumentTabIndex < 0)
        {
            return;
        }

        _hoveredDocumentTabIndex = -1;
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
    }

    private DocumentTabState? FindDocumentTabCloseTarget(NativeMethods.Point point)
    {
        if (!NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client))
        {
            return null;
        }
        foreach (DocumentTabLayout layout in CalculateDocumentTabRectangles(
            client.Right - client.Left, GetDocumentTabRightReserve()))
        {
            if (IsPointInside(point, layout.Rectangle)
                && ShouldShowDocumentTabClose(layout.DocumentIndex)
                && point.X >= layout.Rectangle.Right - S(28))
            {
                return _documents[layout.DocumentIndex];
            }
        }
        return null;
    }

    private TransientEditorTab? FindTransientTabCloseTarget(NativeMethods.Point point)
    {
        if (!NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client)) return null;
        foreach (TransientEditorTabLayout layout in GetTransientTabLayouts(client.Right - client.Left))
        {
            if (IsPointInside(point, layout.Rectangle) && point.X >= layout.Rectangle.Right - S(28))
                return layout.Tab;
        }
        return null;
    }

    private void CloseDocumentTabAt(NativeMethods.Point point)
    {
        if (!NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = client.Right - client.Left;
        List<DocumentTabLayout> tabs = CalculateDocumentTabRectangles(width, GetDocumentTabRightReserve());
        foreach (TransientEditorTabLayout transientLayout in GetTransientTabLayouts(width, tabs))
        {
            if (IsPointInside(point, transientLayout.Rectangle))
            {
                CloseTransientTab(transientLayout.Tab);
                return;
            }
        }

        foreach (DocumentTabLayout layout in tabs)
        {
            NativeMethods.Rectangle rectangle = layout.Rectangle;
            if (point.X >= rectangle.Left
                && point.X <= rectangle.Right
                && point.Y >= rectangle.Top
                && point.Y <= rectangle.Bottom)
            {
                CloseDocument(layout.DocumentIndex);
                return;
            }
        }
    }

    private void ShowDocumentTabContextMenu(nint longParameter)
    {
        NativeMethods.Point screen = DecodeClientPoint(longParameter);
        if (screen.X == -1 && screen.Y == -1)
        {
            if (!NativeMethods.GetWindowRectangle(_documentTabs, out NativeMethods.Rectangle tabWindow))
            {
                return;
            }
            screen = new() { X = tabWindow.Left + S(16), Y = tabWindow.Bottom };
        }

        NativeMethods.Point client = screen;
        if (!NativeMethods.ScreenToClient(_documentTabs, ref client)
            || !NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle rectangle))
        {
            return;
        }

        List<DocumentTabLayout> tabs = CalculateDocumentTabRectangles(
            rectangle.Right - rectangle.Left,
            GetDocumentTabRightReserve());
        int index = -1;
        foreach (DocumentTabLayout tab in tabs)
        {
            if (client.X >= tab.Rectangle.Left
                && client.X <= tab.Rectangle.Right
                && client.Y >= tab.Rectangle.Top
                && client.Y <= tab.Rectangle.Bottom)
            {
                index = tab.DocumentIndex;
                break;
            }
        }
        if (index < 0 || index >= _documents.Count)
        {
            return;
        }

        SelectDocument(index);
        ShowDocumentCloseMenu(index, screen.X, screen.Y);
    }

    private void ShowDocumentCloseMenu(int index, int x, int y)
    {
        _contextMenu?.Dispose();
        _contextMenu = NativeContextMenu.Show(
            _handle,
            x,
            y,
            [
                new(UiText.Close, NativeContextMenuIcon.Close, () => CloseDocument(index)),
                new(
                    UiText.CloseOtherTabs,
                    NativeContextMenuIcon.Close,
                    () => CloseOtherDocuments(index),
                    Enabled: _documents.Count > 1),
                new(UiText.CloseAllTabs, NativeContextMenuIcon.Close, CloseAllDocuments),
            ],
            NativeTheme.IsDark(_settings.Theme));
    }

    private void ShowDocumentTabsMenu(NativeMethods.Point clientPoint)
    {
        if (_documents.Count == 0)
        {
            return;
        }

        NativeMethods.Point screen = clientPoint;
        if (!NativeMethods.ClientToScreen(_documentTabs, ref screen))
        {
            return;
        }

        _contextMenu?.Dispose();
        List<NativeContextMenuItem?> items = [];
        for (int index = 0; index < _documents.Count; index++)
        {
            int selectedIndex = index;
            items.Add(new(
                Path.GetFileName(_documents[index].Path),
                NativeContextMenuIcon.Open,
                () => SelectDocument(selectedIndex),
                Checked: index == _activeDocumentIndex));
        }

        _contextMenu = NativeContextMenu.Show(
            _handle,
            screen.X,
            screen.Y,
            items,
            NativeTheme.IsDark(_settings.Theme));
    }

    private void CloseOtherDocuments(int keptIndex)
    {
        if (keptIndex < 0 || keptIndex >= _documents.Count)
        {
            return;
        }

        DocumentTabState kept = _documents[keptIndex];
        foreach (DocumentTabState document in _documents.Where(document => !ReferenceEquals(document, kept)).ToArray())
        {
            document.View?.Dispose();
            _documents.Remove(document);
        }
        _activeDocumentIndex = 0;
        _firstVisibleDocumentTabIndex = 0;
        _hoveredDocumentTabIndex = -1;
        AdvanceDocumentSelection();
        Layout();
        SelectDocument(0);
        _saved = false;
    }

    private void CloseAllDocuments()
    {
        foreach (DocumentTabState document in _documents)
        {
            document.View?.Dispose();
        }
        _documents.Clear();
        _activeDocumentIndex = -1;
        _firstVisibleDocumentTabIndex = 0;
        _hoveredDocumentTabIndex = -1;
        AdvanceDocumentSelection();
        _ = NativeMethods.SetWindowText(_emptyDocumentLabel, UiText.NoDocument);
        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowNormal);
        Layout();
        _ = NativeMethods.SetFocus(_fileTree);
        _saved = false;
    }

    private bool ShouldShowDocumentTabClose(int index)
    {
        return index >= 0
            && index < _documents.Count
            && (index == _activeDocumentIndex || index == _hoveredDocumentTabIndex);
    }

    private async void OpenFolderFromDialog()
    {
        try
        {
            string? path = NativeFolderDialog.SelectFolder(_handle);
            if (path is not null)
            {
                await OpenWorkspaceAsync(path);
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            SetStatus(exception.Message);
        }
    }

    private void ShowWorkspaceOpenDialog()
    {
        if (_workspaceOpenDialog is not null)
        {
            _workspaceOpenDialog.FocusForTest();
            return;
        }

        _workspaceOpenDialog = NativeWorkspaceOpenDialog.Show(
            _handle,
            _settings,
            path =>
            {
                _ = OpenWorkspaceAsync(path);
            },
            ShowCloneDialog,
            () => _workspaceOpenDialog = null);
    }

    private void InitializeTree(string workspaceRoot)
    {
        _treeGeneration++;
        _treeNodes.Clear();
        _nextTreeNodeId = 1;
        _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewDeleteItem, 0, NativeMethods.TreeViewInsertRoot);
        TreeNodeState root = new(_nextTreeNodeId++, Path.GetFileName(workspaceRoot), workspaceRoot, true, true, 0);
        InsertTreeNode(root, NativeMethods.TreeViewInsertRoot);
        InsertPlaceholder(root.ItemHandle);
        _ = NativeMethods.ShowWindow(_workspaceLabel, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_fileTree, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_documentTabs, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowNormal);
        ShowFilesPanel(focusTree: true);
    }

    private void InsertTreeNode(TreeNodeState node, nint parentItem, nint? insertAfter = null)
    {
        NativeMethods.TreeViewInsert insert = new()
        {
            Parent = parentItem,
            InsertAfter = insertAfter ?? NativeMethods.TreeViewInsertLast,
            Item = new()
            {
                Mask = NativeMethods.TreeViewItemText
                    | NativeMethods.TreeViewItemParameter,
                Text = node.Name,
                TextMaximum = node.Name.Length,
                Parameter = node.Id,
            },
        };
        node.ItemHandle = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewInsertItem, 0, ref insert);
        if (node.ItemHandle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.TreeNodeInsertFailed);
        }

        _treeNodes.Add(node.Id, node);
    }

    private nint InsertPlaceholder(nint parentItem)
    {
        NativeMethods.TreeViewInsert insert = new()
        {
            Parent = parentItem,
            InsertAfter = NativeMethods.TreeViewInsertLast,
            Item = new()
            {
                Mask = NativeMethods.TreeViewItemText | NativeMethods.TreeViewItemParameter,
                Text = "…",
                TextMaximum = 1,
                Parameter = 0,
            },
        };
        return NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewInsertItem, 0, ref insert);
    }

    private async Task LoadTreeNodeAsync(
        TreeNodeState node,
        bool force = false,
        Func<bool>? canApply = null)
    {
        if (!node.CanExpand || _workspaceRoot is null || canApply?.Invoke() == false)
        {
            return;
        }

        if (node.IsLoading)
        {
            int waitingGeneration = _treeGeneration;
            while (node.IsLoading
                && waitingGeneration == _treeGeneration
                && !_disposed
                && canApply?.Invoke() != false
                && _treeNodes.ContainsKey(node.Id))
            {
                await Task.Delay(10);
            }

            if (!force || !IsCurrentTreeLoad(node, waitingGeneration, canApply))
            {
                return;
            }
        }

        if (node.IsLoaded && !force)
        {
            return;
        }

        node.IsLoading = true;
        int generation = _treeGeneration;
        try
        {
            IReadOnlyList<WorkspaceEntry> entries = await Task.Run(() => WorkspaceDirectoryService.EnumerateChildren(node.FullPath));
            if (!IsCurrentTreeLoad(node, generation, canApply))
                return;

            TreeNodeState[] children = _treeNodes.Values.Where(candidate => candidate.ParentId == node.Id).ToArray();
            if (node.IsLoaded && WorkspaceTreeRefreshPolicy.EntriesEqual(
                children.Select(candidate => new WorkspaceTreeEntry(candidate.Name, candidate.FullPath,
                    candidate.IsDirectory, candidate.CanExpand)).ToArray(), entries))
                return;

            node.IsLoaded = false;
            if (!await ApplyTreeEntriesAsync(node, children, entries, generation, canApply))
                return;
            node.IsLoaded = true;
            if (node.IsExpanded && entries.Count > 0)
                _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewExpand, NativeMethods.TreeViewExpandItem, node.ItemHandle);
            else if (entries.Count == 0)
                node.IsExpanded = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (IsCurrentTreeLoad(node, generation, canApply))
                SetStatus(UiText.DirectoryReadFailed);
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    private void RemoveTreeDescendants(TreeNodeState node)
    {
        HashSet<int> removed = [node.Id];
        bool changed;
        do
        {
            changed = false;
            foreach (TreeNodeState candidate in _treeNodes.Values.ToArray())
            {
                if (candidate.Id != node.Id && removed.Contains(candidate.ParentId) && removed.Add(candidate.Id))
                {
                    changed = true;
                }
            }
        }
        while (changed);

        removed.Remove(node.Id);
        foreach (int identifier in removed)
        {
            _treeNodes.Remove(identifier);
        }
    }

    private TreeNodeState? GetTreeNode(nint parameter)
    {
        int identifier = unchecked((int)parameter);
        return identifier > 0 && _treeNodes.TryGetValue(identifier, out TreeNodeState? node) ? node : null;
    }

    private TreeNodeState? GetSelectedTreeNode()
    {
        nint item = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewGetNextItem,
            NativeMethods.TreeViewCaret,
            0);
        return GetTreeNodeFromItem(item);
    }

    private TreeNodeState? GetFirstVisibleTreeNode()
    {
        nint item = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewGetNextItem,
            NativeMethods.TreeViewFirstVisible,
            0);
        return GetTreeNodeFromItem(item);
    }

    private TreeNodeState? GetTreeNodeFromItem(nint item)
    {
        if (item == 0)
        {
            return null;
        }

        NativeMethods.TreeViewItem treeItem = new()
        {
            Mask = NativeMethods.TreeViewItemParameter,
            Item = item,
        };
        _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewGetItem, 0, ref treeItem);
        return GetTreeNode(treeItem.Parameter);
    }

    private void RestoreTreePosition(string? selectedPath, string? firstVisiblePath)
    {
        if (!string.IsNullOrEmpty(selectedPath))
        {
            TreeNodeState? selected = _treeNodes.Values.FirstOrDefault(
                node => node.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                _ = NativeMethods.SendMessage(
                    _fileTree,
                    NativeMethods.TreeViewSelectItem,
                    NativeMethods.TreeViewCaret,
                    selected.ItemHandle);
            }
        }

        if (!string.IsNullOrEmpty(firstVisiblePath))
        {
            TreeNodeState? firstVisible = _treeNodes.Values.FirstOrDefault(
                node => node.FullPath.Equals(firstVisiblePath, StringComparison.OrdinalIgnoreCase));
            if (firstVisible is not null)
            {
                _ = NativeMethods.SendMessage(
                    _fileTree,
                    NativeMethods.TreeViewSelectItem,
                    NativeMethods.TreeViewFirstVisible,
                    firstVisible.ItemHandle);
            }
        }
    }

    private void EnsureTreeRootVisible()
    {
        if (_fileTree == 0)
        {
            return;
        }

        TreeNodeState? root = _treeNodes.Values.FirstOrDefault(node => node.ParentId == 0);
        if (root is null || root.ItemHandle == 0)
        {
            return;
        }

        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSelectItem,
            NativeMethods.TreeViewFirstVisible,
            root.ItemHandle);
    }

    private async Task RestoreWorkspaceStateAsync()
    {
        string workspaceRoot = _workspaceRoot!;
        int treeGeneration = _treeGeneration;
        PrepareRestoredDocumentTabs(workspaceRoot);
        DocumentTabState? restoredDocument = null;
        int selectionVersion = _documentSelectionVersion;
        bool restoreInput = true;

        if (_documents.Count > 0)
        {
            restoredDocument = _documents[_activeDocumentIndex];
            ShowDocumentLoadingPlaceholder(_activeDocumentIndex, updateStatus: false);
            RestoreLayoutUsedLoadingPlaceholderForTest = NativeMethods.IsWindowVisible(_emptyDocumentLabel)
                && NativeMethods.GetWindowTextValue(_emptyDocumentLabel) == UiText.ReadingFile;
            Layout();
            Task<bool> selection = SelectDocumentAsync(_activeDocumentIndex);
            selectionVersion = _documentSelectionVersion;
            restoreInput = await selection;
        }

        var inputAfterRead = CaptureDocumentInputContext();
        foreach (string directory in WorkspacePathRules.NormalizeExpandedDirectories(
            workspaceRoot,
            _settings.ExpandedDirectories).OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar)))
        {
            if (_disposed || treeGeneration != _treeGeneration) return;
            await ExpandTreePathAsync(directory);
        }

        if (restoreInput && restoredDocument is not null && CanPresentDocument(restoredDocument, selectionVersion))
        {
            string? parentPath = Path.GetDirectoryName(restoredDocument.Path);
            if (!string.IsNullOrEmpty(parentPath))
            {
                await ExpandTreePathAsync(parentPath);
            }
        }

        // 恢复只补齐尚未加载的树节点；用户已操作其他上下文后不得再次激活正文或重选项目树。
        if (!restoreInput || _disposed || treeGeneration != _treeGeneration
            || selectionVersion != _documentSelectionVersion
            || CaptureDocumentInputContext() != inputAfterRead
            || (restoredDocument is not null && !IsActiveOrdinaryDocument(restoredDocument))) return;
        if (restoredDocument is not null) SelectDocumentTreeNode(restoredDocument.Path);
        EnsureTreeRootVisible();
    }

    private void PrepareRestoredDocumentTabs(string workspaceRoot)
    {
        if (_documents.Count > 0)
        {
            return;
        }

        string[] restoredFiles = _settings.OpenFiles
            .Where(File.Exists)
            .Where(path => WorkspacePathRules.IsWithin(workspaceRoot, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .Select(Path.GetFullPath)
            .ToArray();
        foreach (string path in restoredFiles)
        {
            _documents.Add(new(path));
        }

        if (_documents.Count == 0)
        {
            return;
        }

        int restoredActiveIndex = Array.FindIndex(
            restoredFiles,
            path => path.Equals(_settings.ActiveFile, StringComparison.OrdinalIgnoreCase));
        _activeDocumentIndex = restoredActiveIndex >= 0
            ? restoredActiveIndex
            : _documents.Count - 1;
        ShowDocumentLoadingPlaceholder(_activeDocumentIndex, updateStatus: false);
        RestoreTabsPreparedBeforeShowForTest = !_shown;
        Layout();
    }

    private async Task ExpandTreePathAsync(string path)
    {
        if (_workspaceRoot is null || !WorkspacePathRules.IsWithin(_workspaceRoot, path))
        {
            return;
        }

        int generation = _treeGeneration;
        TreeNodeState? current = _treeNodes.Values.FirstOrDefault(node => node.ParentId == 0);
        if (current is null)
        {
            return;
        }

        if (current.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            await LoadTreeNodeAsync(current);
            if (!IsCurrentTreeLoad(current, generation, null)) return;
            current.IsExpanded = true;
            _ = NativeMethods.SendMessage(
                _fileTree,
                NativeMethods.TreeViewExpand,
                NativeMethods.TreeViewExpandItem,
                current.ItemHandle);
            return;
        }

        string relative = Path.GetRelativePath(_workspaceRoot, path);
        string accumulated = _workspaceRoot;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            await LoadTreeNodeAsync(current);
            if (!IsCurrentTreeLoad(current, generation, null)) return;
            accumulated = Path.Combine(accumulated, segment);
            int parentIdentifier = current.Id;
            current = _treeNodes.Values.FirstOrDefault(
                node => node.ParentId == parentIdentifier && node.FullPath.Equals(accumulated, StringComparison.OrdinalIgnoreCase));
            if (current is null || !current.IsDirectory)
            {
                return;
            }

            current.IsExpanded = true;
            _ = NativeMethods.SendMessage(
                _fileTree,
                NativeMethods.TreeViewExpand,
                NativeMethods.TreeViewExpandItem,
                current.ItemHandle);
        }

        if (current is not null)
        {
            await LoadTreeNodeAsync(current);
        }
    }

    private async Task OpenDocumentAsync(
        string path,
        int? lineNumber = null,
        string? anchor = null,
        bool preserveBottomToolWindow = false,
        bool preservePendingBlame = false)
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        // 打开普通文件时只暂时隐藏引用比较；返回比较标签必须保留已加载正文和摘要。
        if (_showingReferenceComparison)
        {
            DeactivateTransientTab();
        }

        if (!preserveBottomToolWindow)
        {
            ShowFilesPanel(showProjectPanel: _projectPanelVisible);
        }

        string fullPath = Path.GetFullPath(path);
        int existingIndex = _documents.FindIndex(
            document => document.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _documents[existingIndex].IsPreview = false;
            await SelectDocumentAsync(existingIndex, lineNumber, anchor, preservePendingBlame);
            return;
        }

        DocumentTabState state = new(fullPath);
        _documents.Add(state);
        int index = _documents.Count - 1;
        ShowDocumentLoadingPlaceholder(index, updateStatus: false);
        Layout();
        await SelectDocumentAsync(index, lineNumber, anchor, preservePendingBlame);

        _saved = false;
    }

    private async Task OpenPreviewDocumentAsync(string path, int? lineNumber)
    {
        if (_workspaceRoot is null)
        {
            return;
        }

        string fullPath = Path.GetFullPath(path);
        int formalIndex = _documents.FindIndex(
            document => !document.IsPreview
                && document.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        int previewIndex = _documents.FindIndex(document => document.IsPreview);
        if (formalIndex >= 0)
        {
            await SelectDocumentAsync(formalIndex, lineNumber);
        }
        else if (previewIndex >= 0)
        {
            DocumentTabState preview = _documents[previewIndex];
            preview.View?.Dispose();
            preview.View = null;
            preview.LoadingTask = null;
            preview.Path = fullPath;
            await SelectDocumentAsync(previewIndex, lineNumber);
        }
        else
        {
            DocumentTabState preview = new(fullPath) { IsPreview = true };
            _documents.Add(preview);
            int index = _documents.Count - 1;
            ShowDocumentLoadingPlaceholder(index, updateStatus: false);
            Layout();
            await SelectDocumentAsync(index, lineNumber);
        }

        if (_searchPanel is not null)
        {
            RaiseChildWindow(_searchPanel.Handle);
            _searchPanel.FocusResultList();
        }
    }

    private void SelectDocument(int index)
    {
        if (index < 0 || index >= _documents.Count)
        {
            return;
        }

        DeactivateTransientTab();

        NativeDocumentView? view = _documents[index].View;
        if (view is null)
        {
            _ = SelectDocumentAsync(index);
            return;
        }

        AdvanceDocumentSelection();
        ShowLoadedDocument(index, view);
        SetStatus(view.ReadStatusText);
    }

    private async Task<bool> SelectDocumentAsync(int index, int? lineNumber = null, string? anchor = null,
        bool preservePendingBlame = false)
    {
        if (index < 0 || index >= _documents.Count)
        {
            return false;
        }

        DeactivateTransientTab();

        DocumentTabState state = _documents[index];
        int selectionVersion = AdvanceDocumentSelection(preservePendingBlame);
        _activeDocumentIndex = index;
        if (state.View is null)
        {
            ShowDocumentLoadingPlaceholder(index, updateStatus: true);
        }
        var inputBeforeRead = CaptureDocumentInputContext();
        nint firstTreeItemBeforeRead = GetFirstVisibleTreeNode()?.ItemHandle ?? 0;
        int statusBeforeRead = _statusVersion;

        NativeDocumentView? view = await EnsureDocumentLoadedAsync(state);
        if (view is null)
        {
            return false;
        }

        if (lineNumber is not null)
        {
            view.GoToLine(lineNumber.Value, focus: false);
        }
        if (!string.IsNullOrEmpty(anchor))
        {
            await view.NavigateToAnchorAsync(anchor);
        }

        int currentIndex = _documents.IndexOf(state);
        if (CanPresentDocument(state, selectionVersion) && currentIndex >= 0)
        {
            bool restoreInput = CaptureDocumentInputContext() == inputBeforeRead
                && (GetFirstVisibleTreeNode()?.ItemHandle ?? 0) == firstTreeItemBeforeRead;
            ShowLoadedDocument(currentIndex, view, restoreInput);
            if (_statusVersion == statusBeforeRead) SetStatus(view.ReadStatusText);
            return restoreInput;
        }
        return false;
    }

    private (nint Focus, nint Foreground, nint TreeSelection) CaptureDocumentInputContext() =>
        (NativeMethods.GetFocus(), NativeMethods.GetForegroundWindow(), GetSelectedTreeNode()?.ItemHandle ?? 0);

    private bool CanPresentDocument(DocumentTabState state, int selectionVersion)
    {
        return selectionVersion == _documentSelectionVersion && IsActiveOrdinaryDocument(state);
    }

    private int AdvanceDocumentSelection(bool preservePendingBlame = false)
    {
        // Blame 自己打开目标正文只推进文档身份；用户后续选择仍取消整条归属请求。
        if (!preservePendingBlame) _historyPanel?.CancelPendingBlame();
        return ++_documentSelectionVersion;
    }

    private bool IsActiveOrdinaryDocument(DocumentTabState state)
    {
        return !_disposed && !_showingGitDiff && !_showingReferenceComparison
            && _activeDocumentIndex >= 0 && _activeDocumentIndex < _documents.Count
            && ReferenceEquals(_documents[_activeDocumentIndex], state);
    }

    private void ShowDocumentLoadingPlaceholder(int index, bool updateStatus)
    {
        _activeDocumentIndex = index;
        UpdateCurrentFileContext();
        foreach (DocumentTabState document in _documents)
        {
            document.View?.SetVisible(false);
        }

        _ = NativeMethods.SetWindowText(_emptyDocumentLabel, UiText.ReadingFile);
        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowNormal);
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
        if (updateStatus)
        {
            SetStatus(UiText.ReadingFile);
        }

        EnsureOperationNotificationOnTop(redraw: true);
    }

    private async Task<NativeDocumentView?> EnsureDocumentLoadedAsync(DocumentTabState state)
    {
        if (state.View is not null)
        {
            return state.View;
        }

        Task<NativeDocumentView?> loadingTask = state.LoadingTask ??= LoadDocumentViewAsync(state);
        try
        {
            return await loadingTask;
        }
        finally
        {
            if (ReferenceEquals(state.LoadingTask, loadingTask) && state.View is null)
            {
                state.LoadingTask = null;
            }
        }
    }

    private async Task<NativeDocumentView?> LoadDocumentViewAsync(DocumentTabState state)
    {
        string? workspaceRoot = _workspaceRoot;
        if (workspaceRoot is null)
        {
            return null;
        }

        string documentPath = state.Path;
        int statusBeforeRead = _statusVersion;
        if (DocumentReadBarrierForTest is { } barrier)
        {
            DocumentReadBarrierForTest = null;
            await barrier;
        }
        Augit.Core.Documents.DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(
            workspaceRoot,
            documentPath);
        if (_disposed
            || _workspaceRoot?.Equals(workspaceRoot, StringComparison.OrdinalIgnoreCase) != true
            || !_documents.Contains(state)
            || !state.Path.Equals(documentPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        NativeDocumentView view = new(
            _handle,
            workspaceRoot,
            result,
            _settings,
            text =>
            {
                if (IsActiveOrdinaryDocument(state)
                    && (state.View is not null || statusBeforeRead == _statusVersion))
                {
                    SetStatus(text);
                }
            },
            (linkedPath, linkedLine, linkedAnchor) => _ = OpenDocumentAsync(linkedPath, linkedLine, linkedAnchor),
            ImageDecoderForTest);
        view.SetVisible(false);
        state.View = view;
        view.ReadResultChanged += () => { if (IsActiveOrdinaryDocument(state)) UpdateStatusBar(); };
        // 文件读取完成只放置自己的控件；标签是否可见及输入焦点由当前选择请求决定。
        if (NativeMethods.GetWindowRectangle(_emptyDocumentLabel, out NativeMethods.Rectangle bounds))
        {
            NativeMethods.Point origin = new() { X = bounds.Left, Y = bounds.Top };
            if (NativeMethods.ScreenToClient(_handle, ref origin))
            {
                int width = Math.Max(0, bounds.Right - bounds.Left);
                int height = Math.Max(0, bounds.Bottom - bounds.Top);
                view.SetBounds(origin.X, origin.Y, width, height);
                ApplyRoundedRegion(view.Handle, width, height, CardRadius);
            }
        }
        await view.ImageLoadCompletion;
        return !_disposed && _documents.Contains(state) && ReferenceEquals(state.View, view) ? view : null;
    }

    private void ShowLoadedDocument(int index, NativeDocumentView view, bool restoreInput = true)
    {
        for (int current = 0; current < _documents.Count; current++)
        {
            _documents[current].View?.SetVisible(current == index);
        }

        _activeDocumentIndex = index;
        UpdateCurrentFileContext();
        if (restoreInput) SelectDocumentTreeNode(_documents[index].Path);

        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowHide);
        _ = NativeMethods.SetWindowText(_emptyDocumentLabel, UiText.NoDocument);
        _ = NativeMethods.SetWindowPosition(
            view.Handle,
            NativeMethods.WindowPositionTop,
            0,
            0,
            0,
            0,
            NativeMethods.SetWindowPositionNoMove
                | NativeMethods.SetWindowPositionNoSize
                | NativeMethods.SetWindowPositionNoActivate
                | NativeMethods.SetWindowPositionShowWindow);
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
        if (restoreInput) _ = NativeMethods.SetFocus(view.Handle);
        EnsureOperationNotificationOnTop(redraw: true);
    }

    private void SelectDocumentTreeNode(string path)
    {
        TreeNodeState? node = _treeNodes.Values.FirstOrDefault(
            node => node.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (node is null) return;
        _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewSelectItem, NativeMethods.TreeViewCaret, node.ItemHandle);
        _ = NativeMethods.InvalidateRectangle(_fileTree, 0, false);
    }

    private void CloseActiveDocument()
    {
        if (_showingReferenceComparison || _showingGitDiff)
        {
            CloseTransientTab();
            return;
        }

        CloseDocument(_activeDocumentIndex);
    }

    private void CloseDocument(int index)
    {
        if (index < 0 || index >= _documents.Count)
        {
            return;
        }

        int closedIndex = index;
        DocumentTabState document = _documents[closedIndex];
        bool closingActiveDocument = closedIndex == _activeDocumentIndex
            && !_showingGitDiff
            && !_showingReferenceComparison;
        _documents.RemoveAt(closedIndex);
        if (closedIndex < _firstVisibleDocumentTabIndex)
        {
            _firstVisibleDocumentTabIndex--;
        }
        _firstVisibleDocumentTabIndex = Math.Clamp(
            _firstVisibleDocumentTabIndex,
            0,
            Math.Max(0, _documents.Count - 1));
        if (_hoveredDocumentTabIndex == closedIndex)
        {
            _hoveredDocumentTabIndex = -1;
        }
        else if (_hoveredDocumentTabIndex > closedIndex)
        {
            _hoveredDocumentTabIndex--;
        }
        // 后台关闭只修正索引；保留前台比较、输入焦点以及正在加载的活动文档请求。
        _activeDocumentIndex = _documents.Count == 0
            ? -1
            : _activeDocumentIndex == closedIndex
                ? Math.Min(closedIndex, _documents.Count - 1)
                : _activeDocumentIndex > closedIndex
                    ? _activeDocumentIndex - 1
                    : _activeDocumentIndex;
        _historyPanel?.CancelPendingBlame(document.Path);
        document.View?.Dispose();
        if (closingActiveDocument)
        {
            AdvanceDocumentSelection();
            if (_activeDocumentIndex < 0)
            {
                _ = NativeMethods.SetWindowText(_emptyDocumentLabel, UiText.NoDocument);
                _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowNormal);
                UpdateCurrentFileContext();
            }
            else
            {
                SelectDocument(_activeDocumentIndex);
            }
        }

        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
        _saved = false;
    }

    private async Task RefreshWorkspaceAsync()
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        string workspaceRoot = _workspaceRoot;
        int generation = _treeGeneration;
        bool IsCurrent() => !_disposed && generation == _treeGeneration && _workspaceRoot == workspaceRoot;
        TreeNodeState[] loadedDirectories = _treeNodes.Values
            .Where(node => node.ParentId == 0 || node.IsDirectory && (node.IsLoaded || node.IsExpanded))
            .OrderBy(node => node.FullPath.Length).ToArray();
        foreach (TreeNodeState node in loadedDirectories)
        {
            if (!IsCurrent()) return;
            if (_treeNodes.TryGetValue(node.Id, out TreeNodeState? current) && ReferenceEquals(current, node))
                await LoadTreeNodeAsync(node, force: true, canApply: IsCurrent);
        }

        foreach (DocumentTabState document in _documents.ToArray())
        {
            if (!IsCurrent()) return;
            if (document.View is not { } view)
            {
                continue;
            }

            Augit.Core.Documents.DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(
                workspaceRoot,
                document.Path);
            if (!IsCurrent()) return;
            if (!_documents.Contains(document) || !ReferenceEquals(document.View, view)) continue;
            await view.ReloadAsync(result);
        }

        if (IsCurrent()) SetStatus(UiText.TreeRefreshed);
    }

    private void ShowMainMenu()
    {
        _mainMenuOpen = !_mainMenuOpen;
        Layout();
        UpdateMainMenuToolTips();
        InvalidateTopBar();
    }

    private void UpdateMainMenuToolTips()
    {
        if (_toolTip is null)
        {
            return;
        }

        if (_mainMenuOpen)
        {
            _toolTip.Update(_cloneButton, UiText.MainMenuFile);
            _toolTip.Update(_recentWorkspacesButton, UiText.MainMenuView);
            _toolTip.Update(_currentFileButton, UiText.MainMenuGit);
            _toolTip.Update(_quickOpenButton, UiText.MainMenuTerminal);
            _toolTip.Update(_settingsButton, UiText.MainMenuSettings);
        }
        else
        {
            _toolTip.Update(_cloneButton, UiText.RecentWorkspaces);
            _toolTip.Update(_recentWorkspacesButton, UiText.CurrentBranch);
            _toolTip.Update(_currentFileButton, UiText.QuickOpen);
            _toolTip.Update(_quickOpenButton, UiText.QuickOpen);
            _toolTip.Update(_settingsButton, UiText.Settings);
        }
    }

    private void InvalidateTopBar()
    {
        foreach (nint control in new[]
        {
            _openFolderButton,
            _cloneButton,
            _recentWorkspacesButton,
            _quickOpenButton,
            _currentFileButton,
            _settingsButton,
        })
        {
            if (control != 0)
            {
                _ = NativeMethods.InvalidateRectangle(control, 0, true);
            }
        }
    }

    private void CloseMainMenu()
    {
        if (!_mainMenuOpen)
        {
            return;
        }

        _mainMenuOpen = false;
        Layout();
        UpdateMainMenuToolTips();
        InvalidateTopBar();
    }

    private void ShowFileMenu()
    {
        CloseMainMenu();
        ShowTopMenuPopup(
            _cloneButton,
            [
                (CommandOpenFolder, UiText.OpenFolder),
                (CommandClone, UiText.Clone),
                (CommandRecentWorkspaces, UiText.RecentWorkspaces),
                (0, string.Empty),
                (CommandRefresh, UiText.Refresh),
                (CommandSettings, UiText.Settings),
            ]);
    }

    private void ShowViewMenu()
    {
        CloseMainMenu();
        ShowTopMenuPopup(
            _recentWorkspacesButton,
            [
                (CommandFiles, UiText.Project),
                (CommandGitChanges, UiText.CommitPanel),
                (CommandWorkspaceSearch, UiText.SearchWorkspace),
                (CommandHistory, UiText.History),
                (CommandTerminal, UiText.Terminal),
                (0, string.Empty),
                (CommandQuickOpen, UiText.QuickOpen),
            ]);
    }

    private void ShowGitMenu()
    {
        CloseMainMenu();
        ShowTopMenuPopup(
            _currentFileButton,
            [
                (CommandGitChanges, UiText.CommitPanel),
                (CommandHistory, UiText.History),
                (CommandBranch, UiText.CurrentBranch),
                (0, string.Empty),
                (CommandRefresh, UiText.Refresh),
            ]);
    }

    private void ShowTopMenuPopup(
        nint anchor,
        IReadOnlyList<(int Command, string Label)> items)
    {
        if (anchor == 0 || !NativeMethods.GetWindowRectangle(anchor, out NativeMethods.Rectangle button))
        {
            return;
        }

        _contextMenu?.Dispose();
        List<NativeContextMenuItem?> menuItems = [];
        foreach ((int command, string label) in items)
        {
            if (command == 0)
            {
                menuItems.Add(null);
                continue;
            }

            int selectedCommand = command;
            menuItems.Add(new(
                label,
                ResolveContextMenuIcon(command),
                () => HandleCommand(unchecked((nuint)selectedCommand))));
        }

        _contextMenu = NativeContextMenu.Show(
            _handle,
            button.Left,
            button.Bottom,
            menuItems,
            NativeTheme.IsDark(_settings.Theme));
    }

    private static NativeContextMenuIcon ResolveContextMenuIcon(int command)
    {
        return command switch
        {
            CommandQuickOpen or CommandWorkspaceSearch => NativeContextMenuIcon.Search,
            CommandRefresh => NativeContextMenuIcon.Refresh,
            CommandBranch => NativeContextMenuIcon.BranchPlus,
            CommandHistory => NativeContextMenuIcon.History,
            CommandTerminal => NativeContextMenuIcon.Terminal,
            CommandSettings => NativeContextMenuIcon.Settings,
            _ => NativeContextMenuIcon.Open,
        };
    }

    private async Task ShowBranchMenuAsync()
    {
        if (_openingBranchMenu)
        {
            return;
        }

        _openingBranchMenu = true;
        try
        {
            BranchMenuContext? context = await LoadBranchMenuContextAsync(showError: true);
            if (context is null || _disposed || _recentWorkspacesButton == 0)
            {
                return;
            }

            if (!NativeMethods.GetWindowRectangle(_recentWorkspacesButton, out NativeMethods.Rectangle button))
            {
                return;
            }

            _branchPopup?.Dispose();
            _branchPopup = new NativeBranchPopup(
                _handle,
                button.Left,
                button.Bottom + S(7),
                context.Snapshot,
                _settings,
                request => HandleBranchPopupRequestAsync(context, request),
                () => _branchPopup = null);
            _branchPopup.Show();
        }
        finally
        {
            _openingBranchMenu = false;
        }
    }

    private async Task HandleBranchPopupRequestAsync(
        BranchMenuContext context,
        NativeBranchPopupRequest request)
    {
        switch (request.Command)
        {
            case NativeBranchPopupCommand.UpdateProject:
                ShowGitChangesPanel();
                if (_gitPanel is not null)
                {
                    await _gitPanel.RunPullForHostAsync();
                }
                break;
            case NativeBranchPopupCommand.Commit:
                ShowGitChangesPanel();
                break;
            case NativeBranchPopupCommand.Push:
                ShowGitChangesPanel();
                if (_gitPanel is not null)
                {
                    await _gitPanel.RunPushForHostAsync();
                }
                break;
            case NativeBranchPopupCommand.CreateBranch:
                NativeGitReferenceDialog.Show(
                    _handle,
                    context.Repository,
                    context.Service,
                    _settings,
                    SetStatus);
                RefreshGitSurfaces();
                break;
            case NativeBranchPopupCommand.CheckoutRevision:
                await CheckoutRevisionFromPopupAsync(context);
                break;
            case NativeBranchPopupCommand.CheckoutBranch:
                if (request.Branch is { IsRemote: false } branch)
                {
                    _ = await SwitchTopBarBranchAsync(context, branch.Name, showError: true);
                }
                break;
            case NativeBranchPopupCommand.CreateTrackingBranch:
                if (request.Branch is { IsRemote: true } remoteBranch)
                {
                    await CreateTrackingBranchFromPopupAsync(context, remoteBranch);
                }
                break;
            case NativeBranchPopupCommand.CreateBranchFromReference:
                await CreateBranchFromPopupAsync(context, request.ReferenceName);
                break;
            case NativeBranchPopupCommand.CompareWithWorkspace:
                ShowHistoryPanel();
                if (_historyPanel is not null && request.ReferenceName is { } comparison)
                {
                    await _historyPanel.CompareRevisionForHostAsync(comparison);
                }
                break;
            case NativeBranchPopupCommand.CreateWorktree:
                ShowHistoryPanel();
                _historyPanel?.ShowWorktreeForHost();
                break;
            case NativeBranchPopupCommand.PushReference:
                ShowGitChangesPanel();
                if (_gitPanel is not null)
                {
                    string? localReference = request.Branch is not null
                        ? $"refs/heads/{request.Branch.Name}"
                        : request.Tag is not null
                            ? $"refs/tags/{request.Tag.Name}"
                            : null;
                    await _gitPanel.RunPushForHostAsync(localReference);
                }
                break;
            case NativeBranchPopupCommand.RenameReference:
                await RenameReferenceFromPopupAsync(context, request.ReferenceName);
                break;
            case NativeBranchPopupCommand.DeleteReference:
                await DeleteReferenceFromPopupAsync(context, request);
                break;
        }
    }

    private async Task CheckoutRevisionFromPopupAsync(BranchMenuContext context)
    {
        string? revision = NativeTextPrompt.Show(
            _handle,
            UiText.CheckoutReference,
            UiText.ComparisonTargetPrompt,
            NativeTheme.IsDark(_settings.Theme));
        if (string.IsNullOrWhiteSpace(revision))
        {
            return;
        }

        GitActionResult result = await context.Service.CheckoutReferenceAsync(
            context.Repository,
            revision.Trim(),
            _treeGitStatusCancellation?.Token ?? CancellationToken.None);
        if (!result.IsSuccess)
        {
            ShowBranchMenuError(result.ErrorMessage ?? UiText.ReferenceOperationFailed, showError: true);
            return;
        }

        UpdateBranchLabel(_workspaceRoot!);
        RefreshGitSurfaces();
        SetStatus(UiText.ReferenceOperationCompleted);
    }

    private async Task CreateTrackingBranchFromPopupAsync(
        BranchMenuContext context,
        GitBranchInfo remoteBranch)
    {
        string? localName = NativeTextPrompt.Show(
            _handle,
            UiText.CreateTrackingBranchEllipsis,
            UiText.ReferenceName,
            NativeTheme.IsDark(_settings.Theme));
        if (string.IsNullOrWhiteSpace(localName))
        {
            return;
        }

        GitActionResult result = await context.Service.CreateTrackingBranchAsync(
            context.Repository,
            localName.Trim(),
            remoteBranch.Name,
            _treeGitStatusCancellation?.Token ?? CancellationToken.None);
        if (!result.IsSuccess)
        {
            ShowBranchMenuError(result.ErrorMessage ?? UiText.ReferenceOperationFailed, showError: true);
            return;
        }

        UpdateBranchLabel(_workspaceRoot!);
        RefreshGitSurfaces();
        SetStatus(UiText.ReferenceOperationCompleted);
    }

    private async Task CreateBranchFromPopupAsync(BranchMenuContext context, string? startPoint)
    {
        NativeGitReferenceDialog.Show(
            _handle,
            context.Repository,
            context.Service,
            _settings,
            SetGitStatus,
            startPoint);
        await Task.CompletedTask;
        RefreshGitSurfaces();
    }

    private async Task RenameReferenceFromPopupAsync(BranchMenuContext context, string? currentName)
    {
        if (string.IsNullOrWhiteSpace(currentName))
        {
            return;
        }

        string? newName = NativeTextPrompt.Show(
            _handle,
            UiText.RenameEllipsis,
            UiText.ReferenceName,
            NativeTheme.IsDark(_settings.Theme));
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        GitActionResult result = await context.Service.RenameBranchAsync(
            context.Repository,
            currentName,
            newName.Trim(),
            _treeGitStatusCancellation?.Token ?? CancellationToken.None);
        if (!result.IsSuccess)
        {
            ShowBranchMenuError(result.ErrorMessage ?? UiText.ReferenceOperationFailed, showError: true);
            return;
        }

        UpdateBranchLabel(_workspaceRoot!);
        RefreshGitSurfaces();
    }

    private async Task DeleteReferenceFromPopupAsync(
        BranchMenuContext context,
        NativeBranchPopupRequest request)
    {
        if (request.Branch is { IsRemote: false } branch)
        {
            if (!NativeActionConfirmationDialog.Show(
                    _handle,
                    _settings,
                    UiText.DeleteBranch,
                    $"分支 {branch.Name} 将被删除",
                    UiText.ConfirmDeleteBranchDetails(branch.Name, force: false),
                    UiText.DeleteBranch,
                    danger: true))
            {
                return;
            }

            GitActionResult result = await context.Service.DeleteBranchAsync(
                context.Repository,
                branch.Name,
                force: false,
                _treeGitStatusCancellation?.Token ?? CancellationToken.None);
            if (!result.IsSuccess)
            {
                ShowBranchMenuError(result.ErrorMessage ?? UiText.ReferenceOperationFailed, showError: true);
                return;
            }

            RefreshGitSurfaces();
        }
        else if (request.Tag is { } tag)
        {
            if (!NativeActionConfirmationDialog.Show(
                    _handle,
                    _settings,
                    UiText.DeleteLocalTag,
                    $"标签 {tag.Name} 将被删除",
                    UiText.ConfirmDeleteTagDetails(tag.Name, remote: false, remoteName: null),
                    UiText.DeleteLocalTag,
                    danger: true))
            {
                return;
            }

            GitActionResult result = await context.Service.DeleteLocalTagAsync(
                context.Repository,
                tag.Name,
                _treeGitStatusCancellation?.Token ?? CancellationToken.None);
            if (!result.IsSuccess)
            {
                ShowBranchMenuError(result.ErrorMessage ?? UiText.ReferenceOperationFailed, showError: true);
                return;
            }

            RefreshGitSurfaces();
        }
    }

    private void RefreshGitSurfaces()
    {
        if (_workspaceRoot is not null)
        {
            UpdateBranchLabel(_workspaceRoot);
        }

        RequestTreeGitStatusRefresh();
        _gitPanel?.RequestRefresh(refreshActiveDiff: true);
        _historyPanel?.RequestRefresh();
    }

    private async Task<BranchMenuContext?> LoadBranchMenuContextAsync(bool showError)
    {
        if (_workspaceRoot is null)
        {
            if (showError)
            {
                SetStatus(UiText.OpenWorkspaceFirst);
            }
            return null;
        }

        CancellationToken cancellationToken = _treeGitStatusCancellation?.Token ?? CancellationToken.None;
        SetStatus(UiText.BranchMenuLoading);
        try
        {
            GitRuntimeInfo runtime = await ResolveGitRuntimeAsync(cancellationToken);
            if (!runtime.IsAvailable)
            {
                ShowBranchMenuError(runtime.UnavailableReason ?? UiText.GitUnavailable, showError);
                return null;
            }

            GitRepositoryOperationResult inspected = await new GitRepositoryService(runtime).InspectAsync(
                _workspaceRoot,
                cancellationToken);
            if (!inspected.IsSuccess || inspected.Repository is not { Kind: GitRepositoryKind.WorkingTree } repository)
            {
                ShowBranchMenuError(inspected.ErrorMessage ?? UiText.PlainDirectoryGitStatus, showError);
                return null;
            }

            GitReferenceService service = new(runtime);
            GitReferenceResult references = await service.ReadAsync(repository, cancellationToken);
            if (!references.IsSuccess || references.Snapshot is null)
            {
                ShowBranchMenuError(references.ErrorMessage ?? UiText.ReferenceListUnavailable, showError);
                return null;
            }

            SetStatus(UiText.GitReady);
            return new(repository, service, references.Snapshot, new GitOperationService(runtime));
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<bool> SwitchTopBarBranchAsync(
        BranchMenuContext context,
        string branchName,
        bool showError)
    {
        if (_branchOperationCancellation is not null)
        {
            return false;
        }

        CancellationToken workspaceToken = _treeGitStatusCancellation?.Token ?? CancellationToken.None;
        CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(workspaceToken);
        _branchOperationCancellation = operation;
        _ = NativeMethods.ShowWindow(_cancelBranchButton, NativeMethods.ShowNormal);
        _ = NativeMethods.InvalidateRectangle(_statusBar, 0, true);
        SetStatus(UiText.SwitchingBranch(branchName));
        try
        {
            GitActionResult result = await context.Service.SwitchBranchAsync(
                context.Repository,
                branchName,
                operation.Token);
            if (!result.IsSuccess)
            {
                if (IsBranchOverwriteRisk(result.ErrorMessage))
                {
                    bool completed = await TrySmartCheckoutAsync(context, branchName, operation.Token);
                    if (completed)
                    {
                        return true;
                    }

                    SetStatus(result.ErrorMessage ?? UiText.ReferenceOperationFailed);
                    return false;
                }

                ShowBranchMenuError(result.ErrorMessage ?? UiText.ReferenceOperationFailed, showError);
                return false;
            }

            if (_workspaceRoot is not null)
            {
                UpdateBranchLabel(_workspaceRoot);
            }
            RequestTreeGitStatusRefresh();
            _gitPanel?.RequestRefresh(refreshActiveDiff: true);
            _historyPanel?.RequestRefresh();
            SetStatus(UiText.ReferenceOperationCompleted);
            return true;
        }
        catch (OperationCanceledException)
        {
            SetStatus(UiText.OperationCancelled);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_branchOperationCancellation, operation))
            {
                _branchOperationCancellation = null;
                _ = NativeMethods.ShowWindow(_cancelBranchButton, NativeMethods.ShowHide);
                _ = NativeMethods.InvalidateRectangle(_statusBar, 0, true);
            }
            operation.Dispose();
        }
    }

    private async Task<bool> TrySmartCheckoutAsync(
        BranchMenuContext context,
        string branchName,
        CancellationToken cancellationToken)
    {
        if (!await ConfirmSmartCheckoutAsync(context, branchName, cancellationToken))
        {
            return false;
        }

        SetStatus(UiText.SmartCheckoutRunning);
        GitAdvancedOperationResult smart = await context.OperationService.SmartCheckoutAsync(
            context.Repository,
            branchName,
            cancellationToken);
        if (!smart.IsSuccess)
        {
            if (smart.Session?.HasConflicts == true)
            {
                ShowBranchMenuError(
                    smart.ErrorMessage ?? "Smart Checkout 恢复时产生冲突，请从冲突流程继续。",
                    showError: false);
                _gitPanel?.ShowAdvancedOperationsForHost();
            }
            else
            {
                ShowBranchMenuError(smart.ErrorMessage ?? UiText.ReferenceOperationFailed, showError: true);
            }

            return false;
        }

        if (_workspaceRoot is not null)
        {
            UpdateBranchLabel(_workspaceRoot);
        }

        RefreshGitSurfaces();
        SetStatus(UiText.ReferenceOperationCompleted);
        return true;
    }

    private async Task<bool> ConfirmSmartCheckoutAsync(
        BranchMenuContext context,
        string branchName,
        CancellationToken cancellationToken)
    {
        GitStatusSnapshot? status = null;
        GitStatusResult statusResult = await new GitStatusService(context.Service.Runtime).ReadAsync(
            context.Repository,
            cancellationToken);
        if (statusResult.IsSuccess)
        {
            status = statusResult.Snapshot;
        }

        int trackedCount = status?.Files.Count(file => file.Group == GitChangeGroup.Changes) ?? 0;
        int untrackedCount = status?.Files.Count(file => file.Group == GitChangeGroup.UnversionedFiles) ?? 0;
        string currentBranch = status?.CurrentBranch ?? "当前分支";
        string detail = string.Join(
            "\n",
            UiText.SmartCheckoutWarningDetail,
            string.Empty,
            $"当前分支：{currentBranch}",
            $"目标分支：{branchName}",
            $"将暂存：{trackedCount} 个已跟踪文件和 {untrackedCount} 个未跟踪文件");
        nint owner = NativeMethods.GetAncestor(_handle, NativeMethods.GetAncestorRoot);
        if (!NativeActionConfirmationDialog.Show(
                owner,
                _settings,
                $"切换到 {branchName}",
                UiText.SmartCheckoutWarningTitle,
                detail,
                UiText.SmartCheckout))
        {
            return false;
        }
        return true;
    }

    internal static bool IsBranchOverwriteRiskForTest(string? errorMessage)
    {
        return IsBranchOverwriteRisk(errorMessage);
    }

    private static bool IsBranchOverwriteRisk(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        return errorMessage.Contains("would be overwritten", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("local changes", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("本地改动", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("覆盖", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowBranchMenuError(string message, bool showError)
    {
        SetStatus(message);
        if (showError)
        {
            ShowOperationResult("分支操作失败", message, error: true);
        }
    }

    private void ShowTreeContextMenu()
    {
        TreeNodeState? node = SelectTreeContextTargetAtCursor();
        if (node is null || !NativeMethods.GetCursorPosition(out NativeMethods.Point point))
        {
            return;
        }

        ShowTreeContextMenu(node, point.X, point.Y);
    }

    private TreeNodeState? SelectTreeContextTargetAtCursor()
    {
        TreeNodeState? node = GetTreeNodeAtCursor(out _);
        if (node is null)
        {
            return null;
        }

        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSelectItem,
            NativeMethods.TreeViewCaret,
            node.ItemHandle);
        _ = NativeMethods.SetFocus(_fileTree);
        return node;
    }

    private void ShowTreeOptionsMenu()
    {
        TreeNodeState? node = GetSelectedTreeNode()
            ?? _treeNodes.Values.FirstOrDefault(candidate => candidate.ParentId == 0);
        if (node is null || !NativeMethods.GetWindowRectangle(_treeOptionsButton, out NativeMethods.Rectangle button))
        {
            return;
        }

        ShowTreeContextMenu(node, button.Left, button.Bottom);
    }

    private void ShowTreeContextMenu(TreeNodeState node, int x, int y)
    {
        _contextMenu?.Dispose();
        List<NativeContextMenuItem?> items =
        [
            new(
                UiText.CopyPath,
                NativeContextMenuIcon.Copy,
                () => HandleTreeMenuCommand(MenuCopyPath, node)),
            new(
                UiText.RevealInExplorer,
                NativeContextMenuIcon.Open,
                () => HandleTreeMenuCommand(MenuRevealInExplorer, node)),
            new(
                UiText.OpenExternalTerminal,
                NativeContextMenuIcon.Terminal,
                () => HandleTreeMenuCommand(MenuOpenTerminal, node)),
            null,
            new(
                UiText.Refresh,
                NativeContextMenuIcon.Refresh,
                () => HandleTreeMenuCommand(MenuRefresh, node)),
        ];
        if (!node.IsDirectory)
        {
            items.Add(new(
                UiText.FileHistory,
                NativeContextMenuIcon.History,
                () => HandleTreeMenuCommand(MenuFileHistory, node)));
            items.Add(new(
                UiText.Blame,
                NativeContextMenuIcon.Blame,
                () => HandleTreeMenuCommand(MenuBlame, node)));
        }

        _contextMenu = NativeContextMenu.Show(
            _handle,
            x,
            y,
            items,
            NativeTheme.IsDark(_settings.Theme));
    }

    private async Task LocateActiveFileAsync()
    {
        if (_workspaceRoot is null || ActiveDocument is null)
        {
            SetStatus(UiText.NoDocument);
            return;
        }

        string? parentPath = Path.GetDirectoryName(ActiveDocument.Path);
        if (!string.IsNullOrEmpty(parentPath))
        {
            await ExpandTreePathAsync(parentPath);
        }

        TreeNodeState? node = _treeNodes.Values.FirstOrDefault(
            candidate => candidate.FullPath.Equals(ActiveDocument.Path, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            return;
        }

        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSelectItem,
            NativeMethods.TreeViewCaret,
            node.ItemHandle);
        _ = NativeMethods.SetFocus(_fileTree);
        _ = NativeMethods.InvalidateRectangle(_fileTree, 0, true);
    }

    private void CollapseTree()
    {
        foreach (TreeNodeState node in _treeNodes.Values
            .Where(candidate => candidate.IsDirectory && candidate.ParentId != 0)
            .OrderByDescending(candidate => candidate.FullPath.Length))
        {
            node.IsExpanded = false;
            _ = NativeMethods.SendMessage(
                _fileTree,
                NativeMethods.TreeViewExpand,
                NativeMethods.TreeViewCollapseItem,
                node.ItemHandle);
        }

        TreeNodeState? root = _treeNodes.Values.FirstOrDefault(node => node.ParentId == 0);
        if (root is not null)
        {
            _ = NativeMethods.SendMessage(
                _fileTree,
                NativeMethods.TreeViewSelectItem,
                NativeMethods.TreeViewFirstVisible,
                root.ItemHandle);
        }

        _saved = false;
    }

    private void HandleTreeMenuCommand(uint command, TreeNodeState node)
    {
        switch (command)
        {
            case MenuCopyPath:
                SetStatus(NativeClipboard.TrySetText(_handle, node.FullPath) ? UiText.PathCopied : UiText.ClipboardUnavailable);
                break;
            case MenuRevealInExplorer:
                ShowLaunchResult(ExternalProgramLauncher.RevealInExplorer(node.FullPath));
                break;
            case MenuOpenTerminal:
                ShowLaunchResult(ExternalProgramLauncher.OpenExternalTerminal(node.FullPath));
                break;
            case MenuRefresh:
                _ = RefreshWorkspaceAsync();
                break;
            case MenuFileHistory:
                if (!node.IsDirectory)
                {
                    ShowFileHistory(node.FullPath);
                }
                break;
            case MenuBlame:
                if (!node.IsDirectory && _workspaceRoot is not null)
                {
                    string relativePath = Path.GetRelativePath(_workspaceRoot, node.FullPath).Replace('\\', '/');
                    _ = ShowBlameForPathAsync(relativePath);
                }
                break;
        }
    }

    private void ShowLaunchResult(ExternalLaunchResult result)
    {
        if (!result.IsSuccess)
        {
            SetStatus(result.ErrorMessage ?? UiText.ExternalProgramFailed);
        }
    }

    private void ShowRecentWorkspaces()
    {
        string[] recent = _settings.RecentWorkspaces
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
        if (recent.Length == 0)
        {
            SetStatus(UiText.NoRecentWorkspaces);
            return;
        }

        if (!NativeMethods.GetWindowRectangle(_cloneButton, out NativeMethods.Rectangle button))
        {
            return;
        }

        _contextMenu?.Dispose();
        List<NativeContextMenuItem?> menuItems = [];
        for (int index = 0; index < recent.Length; index++)
        {
            int selectedIndex = index;
            menuItems.Add(new(
                recent[index],
                NativeContextMenuIcon.History,
                () => _ = OpenWorkspaceAsync(recent[selectedIndex])));
        }

        _contextMenu = NativeContextMenu.Show(
            _handle,
            button.Left,
            button.Bottom,
            menuItems,
            NativeTheme.IsDark(_settings.Theme));
    }

    private Task<bool> OpenTerminalAsync()
    {
        if (_disposed || _terminalClosePending || _destroying || _destroyed) return Task.FromResult(false);
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return Task.FromResult(false);
        }

        if (_terminalPanel is not null)
        {
            ShowTerminalPanel();
            return _terminalOpeningTask ?? Task.FromResult(true);
        }

        Task<bool> opening = StartTerminalAsync(_workspaceRoot);
        _terminalOpeningTask = opening;
        _terminalStarts.Add(opening);
        _ = ObserveTerminalStartAsync(opening);
        return opening;
    }

    private async Task<bool> StartTerminalAsync(string workspaceRoot)
    {
        NativeTerminalPanel? panel = null;
        SetStatus(UiText.TerminalLoading);
        try
        {
            panel = new NativeTerminalPanel(
                _handle,
                CommandHideTerminal,
                TerminalSessionCloseControlIdentifier,
                CommandTerminalMore,
                status =>
                {
                    if (IsCurrent() && _showingTerminalPanel) SetStatus(status);
                });
            _terminalPanel = panel;
            panel.ApplyAppearance(_settings);
            ShowTerminalPanel();
            await panel.InitializeAsync(workspaceRoot, _settings, TerminalStartupCheckpointForTest);
            if (!IsCurrent())
            {
                panel.Dispose();
                return false;
            }

            panel.ApplyAppearance(_settings);
            // 就绪只补全当前面板；用户在等待期间的工具窗口、正文和焦点选择优先。
            if (_showingTerminalPanel)
            {
                if (panel.LoadingActionHasFocus) panel.Focus();
                SetStatus(UiText.TerminalReady);
            }
            return true;
        }
        catch (Exception) when (!IsCurrent())
        {
            panel?.Dispose();
            return false;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            CloseTerminal(requireConfirmation: false);
            RuntimeDependencyPrompt.ShowWebView2Missing(_handle);
            SetStatus(UiText.TerminalStartFailed);
            return false;
        }
        catch (Exception exception) when (exception is COMException
            or Win32Exception
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            CloseTerminal(requireConfirmation: false);
            SetStatus(exception.Message);
            ShowOperationResult(UiText.TerminalStartFailed, exception.Message, error: true);
            return false;
        }
        bool IsCurrent() => !_disposed && !_terminalClosePending && !_destroying && !_destroyed
            && ReferenceEquals(_terminalPanel, panel)
            && string.Equals(_workspaceRoot, workspaceRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowTerminalMoreMenu()
    {
        if (_terminalPanel is null
            || !NativeMethods.GetWindowRectangle(_terminalPanel.MoreButtonHandle, out NativeMethods.Rectangle button))
        {
            return;
        }

        _contextMenu?.Dispose();
        _contextMenu = NativeContextMenu.Show(
            _handle,
            button.Left,
            button.Bottom,
            [
                new(UiText.TerminalShell, NativeContextMenuIcon.Terminal, ShowAppearanceSettings),
                new(
                    UiText.OpenExternalTerminal,
                    NativeContextMenuIcon.Terminal,
                    () =>
                    {
                        if (_workspaceRoot is not null)
                        {
                            ShowLaunchResult(ExternalProgramLauncher.OpenExternalTerminal(_workspaceRoot));
                        }
                    }),
            ],
            NativeTheme.IsDark(_settings.Theme));
    }

    private bool CloseTerminal(bool requireConfirmation)
    {
        NativeTerminalPanel? panel = _terminalPanel;
        if (panel is null)
        {
            return true;
        }

        if (requireConfirmation
            && panel.HasForegroundProcess
            && !NativeTerminalCloseDialog.Show(
                _handle,
                _settings,
                keep: static () => { },
                close: static () => { }))
        {
            return false;
        }

        _contextMenu?.Dispose();
        _contextMenu = null;
        bool wasVisible = _showingTerminalPanel;
        bool restoreHistory = wasVisible && _restoreHistoryAfterTerminal && _historyPanel is not null;
        _terminalPanel = null;
        _terminalOpeningTask = null;
        _showingTerminalPanel = false;
        panel.Dispose();
        if (restoreHistory && !_terminalClosePending) ShowHistoryPanel();
        else
        {
            Layout();
            InvalidateNavigation();
            if (wasVisible) _ = NativeMethods.SetFocus(ActiveDocument?.Handle ?? _fileTree);
        }
        SetStatus(UiText.TerminalClosed);
        return true;
    }

    private void ShowQuickOpen()
    {
        ShowSearch(WorkspaceSearchMode.FileNames);
    }

    private void ShowWorkspaceSearch()
    {
        ShowSearch(WorkspaceSearchMode.Text);
    }

    private void ToggleWorkspaceSearchPanel()
    {
        if (_searchPanel?.Mode == WorkspaceSearchMode.Text)
        {
            CloseSearch();
            return;
        }

        ShowWorkspaceSearch();
    }

    private void ShowFilesPanel(bool focusTree = false, bool showProjectPanel = true)
    {
        CloseSearch();
        bool surfaceChanged = _showingGitPanel;
        _showingGitPanel = false;
        bool projectVisibilityChanged = _projectPanelVisible != showProjectPanel;
        _projectPanelVisible = showProjectPanel;
        if (surfaceChanged)
        {
            _gitPanel?.SetVisible(false, _showingGitDiff);
        }

        bool hasWorkspace = _workspaceRoot is not null;
        bool visibilityChanged = surfaceChanged || projectVisibilityChanged;
        visibilityChanged |= SetControlVisible(_workspaceLabel, !hasWorkspace);
        visibilityChanged |= SetControlVisible(_projectHeader, hasWorkspace && showProjectPanel);
        visibilityChanged |= SetControlVisible(_locateActiveFileButton, hasWorkspace && showProjectPanel);
        visibilityChanged |= SetControlVisible(_collapseTreeButton, hasWorkspace && showProjectPanel);
        visibilityChanged |= SetControlVisible(_treeOptionsButton, hasWorkspace && showProjectPanel);
        visibilityChanged |= SetControlVisible(_hideProjectButton, hasWorkspace && showProjectPanel);
        visibilityChanged |= SetControlVisible(_fileTree, hasWorkspace && showProjectPanel);
        visibilityChanged |= SetControlVisible(_documentTabs, hasWorkspace);
        visibilityChanged |= SetControlVisible(
            _emptyDocumentLabel,
            hasWorkspace
                && _documents.Count == 0
                && !_showingReferenceComparison
                && !_showingGitDiff);
        for (int index = 0; index < _documents.Count; index++)
        {
            if (_documents[index].View is { } view)
            {
                visibilityChanged |= SetControlVisible(
                    view.Handle,
                    hasWorkspace
                        && !_showingReferenceComparison
                        && !_showingGitDiff
                        && index == _activeDocumentIndex);
            }
        }
        if (_comparisonView is not null)
        {
            visibilityChanged |= SetControlVisible(
                _comparisonView.Handle,
                hasWorkspace && _showingReferenceComparison);
        }

        if (focusTree && hasWorkspace && showProjectPanel)
        {
            _ = NativeMethods.SetFocus(_fileTree);
        }

        if (visibilityChanged)
        {
            _ = NativeMethods.InvalidateRectangle(_handle, 0, false);
            InvalidateNavigation();
        }
    }

    private void ToggleProjectPanel()
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        if (_showingGitPanel || _searchPanel is not null || !_projectPanelVisible)
        {
            ShowFilesPanel(focusTree: true);
            Layout();
            return;
        }

        HideProjectPanel();
    }

    private void HideProjectPanel()
    {
        ShowFilesPanel(showProjectPanel: false);
        Layout();
        if (ActiveDocument is not null)
        {
            _ = NativeMethods.SetFocus(ActiveDocument.Handle);
        }
    }

    private static bool SetControlVisible(nint control, bool visible)
    {
        if (control == 0)
        {
            return false;
        }

        long style = NativeMethods.GetWindowLongPointer(control, NativeMethods.WindowLongStyle).ToInt64();
        bool isVisible = (style & NativeMethods.WindowStyleVisible) != 0;
        if (isVisible == visible)
        {
            return false;
        }

        _ = NativeMethods.ShowWindow(control, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        return true;
    }

    private void ShowGitChangesPanel()
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        if (_gitRuntimeUnavailable)
        {
            ShowFilesPanel(focusTree: false, showProjectPanel: _projectPanelVisible);
            ShowGitUnavailableNotice();
            return;
        }

        CloseSearch();
        bool keepDiffContext = _showingGitDiff || HasGitDiffTabState();
        _showingGitPanel = true;
        _gitPanel ??= new NativeGitPanel(
            _handle,
            _workspaceRoot,
            _settings,
            SetGitStatus,
            path => _ = OpenDocumentAsync(path),
            path => ShowFileHistory(Path.Combine(_workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar))),
            path => ShowBlameForPathAsync(path),
            ShowHistoryPanel,
            ShowAppearanceSettings,
            SetGitDiffVisible,
            InvalidateNavigation,
            ApplyTreeGitStatus,
            () => ShowFilesPanel(
                focusTree: _projectPanelVisible,
                showProjectPanel: _projectPanelVisible),
            reason => Post(() => MarkGitRuntimeUnavailable(reason, showNotice: true)));
        if (!keepDiffContext)
        {
            _preserveGitDiffTabsOnHide = true;
            try
            {
                _gitPanel.ShowOverview();
            }
            finally
            {
                _preserveGitDiffTabsOnHide = false;
            }
        }
        _ = NativeMethods.ShowWindow(_workspaceLabel, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_projectHeader, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_locateActiveFileButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_collapseTreeButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_treeOptionsButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_hideProjectButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_fileTree, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_documentTabs, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(
            _emptyDocumentLabel,
            _documents.Count == 0 && !_showingReferenceComparison && !_showingGitDiff
                ? NativeMethods.ShowNormal
                : NativeMethods.ShowHide);
        for (int index = 0; index < _documents.Count; index++)
        {
            _documents[index].View?.SetVisible(
                !_showingReferenceComparison
                    && !_showingGitDiff
                    && index == _activeDocumentIndex);
        }
        _comparisonView?.SetVisible(_showingReferenceComparison);

        _gitPanel.SetVisible(true, _showingGitDiff);
        Layout();
        InvalidateNavigation();
        _ = NativeMethods.SetFocus(_gitPanel.Handle);
    }

    /// <summary>
    /// 记录当前工作区的 Git 运行时不可用状态，并把 Git 入口降级为可解释的禁用状态。
    /// </summary>
    private void MarkGitRuntimeUnavailable(string reason, bool showNotice)
    {
        if (_disposed || _workspaceRoot is null)
        {
            return;
        }

        _gitRuntimeUnavailable = true;
        _gitRuntimeUnavailableReason = string.IsNullOrWhiteSpace(reason)
            ? UiText.GitUnavailable
            : reason.Trim();
        SetStatus(_gitRuntimeUnavailableReason);
        _ = NativeMethods.EnableWindow(_gitButton, false);
        _ = NativeMethods.EnableWindow(_historyButton, false);
        _toolTip?.Update(
            _gitButton,
            $"{UiText.CommitPanel}：{_gitRuntimeUnavailableReason}");
        _toolTip?.Update(
            _historyButton,
            $"{UiText.History}：{_gitRuntimeUnavailableReason}");

        // Git 面板初始化失败时恢复普通文件浏览，保留项目树、标签和当前正文。
        if (_showingGitPanel || _showingHistoryPanel)
        {
            ShowFilesPanel(focusTree: false, showProjectPanel: true);
        }

        if (showNotice)
        {
            ShowGitUnavailableNotice();
        }
        else
        {
            Layout();
            InvalidateNavigation();
        }
    }

    /// <summary>
    /// 首次触发 Git 入口时显示一次局部错误提示，不遮挡文件浏览上下文。
    /// </summary>
    private void ShowGitUnavailableNotice()
    {
        if (_gitUnavailableNoticeShown)
        {
            if (_operationNotificationShowConfigureGit)
            {
                Layout();
                EnsureOperationNotificationOnTop(redraw: true);
            }

            return;
        }

        _gitUnavailableNoticeShown = true;
        string reason = _gitRuntimeUnavailableReason.Trim();
        string detail = reason.EndsWith('。')
            ? $"{reason[..^1]}，文件浏览仍可使用。"
            : $"{reason}，文件浏览仍可使用。";
        ShowOperationNotification(
            "Git 不可用",
            detail,
            string.Empty,
            OperationNotificationKind.Error,
            canCancel: false,
            autoDismiss: false,
            showConfigureGit: true);
    }

    /// <summary>
    /// 应用设置后重新开始 Git 探测，允许用户修正 git.exe 路径而无需重启工作区。
    /// </summary>
    private void RetryGitRuntimeDetection()
    {
        if (_workspaceRoot is null || _disposed)
        {
            return;
        }

        _gitRuntimeUnavailable = false;
        _gitRuntimeUnavailableReason = UiText.GitUnavailable;
        _gitUnavailableNoticeShown = false;
        _gitRuntime = null;
        _gitRuntimeResolution = null;
        if (_operationNotificationShowConfigureGit)
        {
            HideOperationNotification();
        }
        _ = NativeMethods.EnableWindow(_gitButton, true);
        _ = NativeMethods.EnableWindow(_historyButton, true);
        _toolTip?.Update(_gitButton, UiText.CommitPanel);
        _toolTip?.Update(_historyButton, UiText.History);

        _treeGitStatusCancellation?.Cancel();
        _treeGitStatusCancellation?.Dispose();
        CancellationTokenSource lifetime = new();
        _treeGitStatusCancellation = lifetime;
        _treeGitRepository = null;
        _treeGitStatusService = null;
        _treeGitStatusIndex = null;
        _treeGitStatusSnapshot = null;
        _treeGitStatusWorkspaceRoot = null;
        _treeGitStatusRefreshing = false;
        _treeGitStatusRefreshPending = false;
        _ = InitializeTreeGitStatusAsync(_workspaceRoot, _treeGeneration, lifetime);
        Layout();
        InvalidateNavigation();
    }

    private void ToggleGitChangesPanel()
    {
        if (_showingGitPanel)
        {
            ShowFilesPanel(
                focusTree: _projectPanelVisible,
                showProjectPanel: _projectPanelVisible);
            Layout();
            return;
        }

        ShowGitChangesPanel();
    }

    private void SetGitDiffVisible(
        bool visible,
        string? relativePath,
        bool activatePresentation)
    {
        if (_disposed || _gitPanel is null)
        {
            return;
        }

        string? normalizedPath = string.IsNullOrWhiteSpace(relativePath)
            ? null
            : relativePath.Replace('\\', '/');
        bool tabStateChanged = false;
        if (visible && activatePresentation && _showingReferenceComparison)
        {
            _showingReferenceComparison = false;
            _comparisonView?.SetVisible(false);
            tabStateChanged = true;
        }

        bool keepDiffActive = visible && _showingGitDiff;
        if (visible && normalizedPath is not null)
        {
            if (_previewGitDiffPath?.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase) != true)
            {
                // 工作区 Diff 始终复用唯一比较标签，路径仅随 Changes 当前选择更新。
                _previewGitDiffPath = normalizedPath;
                tabStateChanged = true;
            }
        }
        else if (!visible && !_preserveGitDiffTabsOnHide && _gitDiffRelativePath is { } closingPath)
        {
            tabStateChanged = RemoveGitDiffTabState(closingPath);
        }

        bool visibilityChanged = activatePresentation && _showingGitDiff != visible;
        bool pathChanged = activatePresentation
            ? !_gitDiffRelativePath?.Equals(
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase)
                ?? normalizedPath is not null
            : keepDiffActive && !string.Equals(
                _gitDiffRelativePath,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase);
        if (!visibilityChanged && !tabStateChanged && !pathChanged)
        {
            return;
        }

        if (activatePresentation)
        {
            if (visible) AdvanceDocumentSelection();
            _showingGitDiff = visible;
            _gitDiffRelativePath = visible ? normalizedPath : null;
        }
        else if (keepDiffActive)
        {
            _gitDiffRelativePath = normalizedPath;
        }
        UpdateCurrentFileContext();
        if (visibilityChanged)
        {
            _ = NativeMethods.ShowWindow(_documentTabs, NativeMethods.ShowNormal);
            _ = NativeMethods.ShowWindow(
                _emptyDocumentLabel,
                !visible && !_showingReferenceComparison && _documents.Count == 0
                    ? NativeMethods.ShowNormal
                    : NativeMethods.ShowHide);
            for (int index = 0; index < _documents.Count; index++)
            {
                _documents[index].View?.SetVisible(
                    !visible && !_showingReferenceComparison && index == _activeDocumentIndex);
            }
        }

        // 临时 Diff 复用同一个标签槽位，路径变化只需要重绘标签和正文。
        // 只有编辑表面显隐发生变化时才允许重排主窗口，避免单击 Changes 文件时全局闪烁。
        if (visibilityChanged)
        {
            Layout();
        }
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
        if (_gitPanel is not null)
        {
            if (visibilityChanged && _showingGitPanel)
            {
                _ = NativeMethods.SetWindowPosition(
                    _gitPanel.Handle,
                    NativeMethods.WindowPositionTop,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SetWindowPositionNoMove
                        | NativeMethods.SetWindowPositionNoSize
                        | NativeMethods.SetWindowPositionNoActivate
                        | NativeMethods.SetWindowPositionShowWindow);
            }
            if (visibilityChanged && visible)
            {
                _ = NativeMethods.SetWindowPosition(
                    _gitPanel.DiffHandleForTest,
                    NativeMethods.WindowPositionTop,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SetWindowPositionNoMove
                        | NativeMethods.SetWindowPositionNoSize
                        | NativeMethods.SetWindowPositionNoActivate
                        | NativeMethods.SetWindowPositionShowWindow);
            }
        }
    }

    private bool HasGitDiffTabState()
    {
        return _previewGitDiffPath is not null;
    }

    private bool RemoveGitDiffTabState(string relativePath)
    {
        if (_previewGitDiffPath?.Equals(relativePath, StringComparison.OrdinalIgnoreCase) == true)
        {
            _previewGitDiffPath = null;
            return true;
        }

        return false;
    }

    private void ShowHistoryPanel()
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        if (_gitRuntimeUnavailable)
        {
            ShowFilesPanel(focusTree: false, showProjectPanel: _projectPanelVisible);
            ShowGitUnavailableNotice();
            return;
        }

        CloseSearch();
        _showingHistoryPanel = true;
        _showingTerminalPanel = false;
        _historyPanel ??= new NativeGitHistoryPanel(
            _handle,
            _workspaceRoot,
            _settings,
            SetGitStatus,
            ShowBlameAsync,
            ShowHistoryComparison,
            OpenHistoryFileComparisonAsync,
            ActivateReferenceComparisonTab,
            UpdateFollowedHistoryComparisonNotice,
            CloseHistoryPanel);
        _terminalPanel?.SetVisible(false);
        _historyPanel.SetVisible(true);
        Layout();
        RaiseChildWindow(_historyPanel.Handle);
        InvalidateNavigation();
        _ = NativeMethods.SetFocus(_historyPanel.Handle);
    }

    private void ToggleHistoryPanel()
    {
        if (_showingHistoryPanel)
        {
            CloseHistoryPanel();
        }
        else
        {
            ShowHistoryPanel();
        }
    }

    private void CloseHistoryPanel()
    {
        if (!_showingHistoryPanel)
        {
            return;
        }

        _showingHistoryPanel = false;
        _historyPanel?.SetVisible(false);
        Layout();
        InvalidateNavigation();
        if (ActiveDocument is not null)
        {
            _ = NativeMethods.SetFocus(ActiveDocument.Handle);
        }
        else if (_fileTree != 0)
        {
            _ = NativeMethods.SetFocus(_fileTree);
        }
    }

    /// <summary>
    /// 将 Git 历史触发的比较结果放入中央编辑区，底部历史面板只保留原有选择上下文。
    /// </summary>
    private void ShowHistoryComparison(GitComparisonResult result)
    {
        if (_disposed)
        {
            return;
        }

        if (!result.IsSuccess || result.Document is null)
        {
            SetStatus(result.ErrorMessage ?? UiText.GenerateDiffFailed);
            return;
        }

        NativeGitComparisonView view = EnsureHistoryComparisonView();
        _historyPanel?.StopFollowingFileComparison();
        view.SetResult(result);
        PresentHistoryComparison(result.Document);
    }

    private NativeGitComparisonView EnsureHistoryComparisonView()
    {
        return _comparisonView ??= new NativeGitComparisonView(
            _handle,
            _settings,
            SetStatus,
            ReloadHistoryComparisonAsync);
    }

    private void UpdateFollowedHistoryComparisonNotice(string commitHash, string message)
    {
        if (_disposed || _referenceComparisonDocument is null || _comparisonView is null) return;
        string? path = _historyPanel?.SelectedFileRelativePath ?? _referenceComparisonDocument.RelativePath;
        _referenceComparisonDocument = new(GitDiffContentStatus.Ready, $"{commitHash}^", commitHash, path, null);
        _comparisonView.ShowIdentityNotice(_referenceComparisonDocument, message);
        UpdateCurrentFileContext();
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
    }

    private Task OpenHistoryFileComparisonAsync(
        GitComparisonDocument document,
        Func<CancellationToken, Task<GitComparisonResult>> load,
        bool activate,
        CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        NativeGitComparisonView view = EnsureHistoryComparisonView();
        Task query = view.CanReuseDocument(document)
            ? Task.CompletedTask
            : view.LoadAsync(document, load, cancellationToken);
        // 显式打开激活比较；选择跟随只更新已有身份，不改变当前前台和焦点。
        if (activate)
        {
            PresentHistoryComparison(document);
        }
        else
        {
            _referenceComparisonDocument = document with { UnifiedPatch = null };
            UpdateCurrentFileContext();
            _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
        }
        return query;
    }

    private void PresentHistoryComparison(GitComparisonDocument document)
    {
        // 比较页成为当前编辑上下文时，使尚未完成的普通文件打开请求失效。
        AdvanceDocumentSelection();
        bool needsLayout = !_showingReferenceComparison || _referenceComparisonDocument is null;
        DeactivateTransientTab();
        // 主窗口只持有标签身份，正文补丁的容量与释放由比较视图统一管理。
        _referenceComparisonDocument = document with { UnifiedPatch = null };
        _showingReferenceComparison = true;
        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowHide);
        foreach (DocumentTabState tab in _documents)
        {
            tab.View?.SetVisible(false);
        }

        _comparisonView!.SetVisible(true);
        if (needsLayout)
        {
            Layout();
        }
        UpdateCurrentFileContext();
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
        _ = NativeMethods.SetFocus(_comparisonView.Handle);
    }

    private Task<GitComparisonResult> ReloadHistoryComparisonAsync(
        GitComparisonDocument document,
        bool ignoreWhitespace,
        CancellationToken cancellationToken)
    {
        return _historyPanel?.ReloadComparisonForHostAsync(document, ignoreWhitespace, cancellationToken)
            ?? Task.FromResult(GitComparisonResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                UiText.HistoryUnavailable));
    }

    private void CloseReferenceComparison()
    {
        if (_referenceComparisonDocument is null)
        {
            return;
        }

        bool active = _showingReferenceComparison;
        _historyPanel?.CancelPendingComparison();
        _showingReferenceComparison = false;
        _referenceComparisonDocument = null;
        _comparisonView?.Clear();
        if (active)
        {
            RestoreActiveDocumentSurface();
            Layout();
        }

        UpdateCurrentFileContext();
        _ = NativeMethods.InvalidateRectangle(_documentTabs, 0, false);
    }

    private void ShowTerminalPanel()
    {
        if (_terminalPanel is null)
        {
            return;
        }

        if (!_showingTerminalPanel) _restoreHistoryAfterTerminal = _showingHistoryPanel;
        _showingHistoryPanel = false;
        _showingTerminalPanel = true;
        _historyPanel?.SetVisible(false);
        _terminalPanel.SetVisible(true);
        Layout();
        RaiseChildWindow(_terminalPanel.Handle);
        InvalidateNavigation();
        _terminalPanel.Focus();
    }

    /// <summary>
    /// 底部工具窗口与编辑器占用同一父窗口，显示时必须提升到编辑器前面。
    /// </summary>
    private static void RaiseChildWindow(nint window)
    {
        if (window == 0)
        {
            return;
        }

        _ = NativeMethods.SetWindowPosition(
            window,
            NativeMethods.WindowPositionTop,
            0,
            0,
            0,
            0,
            NativeMethods.SetWindowPositionNoMove
                | NativeMethods.SetWindowPositionNoSize
                | NativeMethods.SetWindowPositionNoActivate
                | NativeMethods.SetWindowPositionShowWindow);
    }

    private static bool IsChildWindowAbove(nint overlay, nint siblingWindow)
    {
        if (overlay == 0 || siblingWindow == 0)
        {
            return false;
        }

        nint sibling = NativeMethods.GetWindowSibling(
            siblingWindow,
            NativeMethods.WindowGetPrevious);
        while (sibling != 0)
        {
            if (sibling == overlay)
            {
                return true;
            }

            sibling = NativeMethods.GetWindowSibling(
                sibling,
                NativeMethods.WindowGetPrevious);
        }

        return false;
    }

    /// <summary>
    /// 通知卡片属于主窗口的覆盖层，任何晚创建或重新显示的兄弟窗口都不能盖住它。
    /// </summary>
    private void EnsureOperationNotificationOnTop(bool redraw)
    {
        if (_operationNotificationKind == OperationNotificationKind.None
            || _operationNotification == 0)
        {
            return;
        }

        RaiseChildWindow(_operationNotification);
        if (NativeMethods.IsWindowVisible(_operationNotificationCancelButton))
        {
            RaiseChildWindow(_operationNotificationCancelButton);
        }
        if (NativeMethods.IsWindowVisible(_operationNotificationActionButton))
        {
            RaiseChildWindow(_operationNotificationActionButton);
        }

        if (!redraw)
        {
            return;
        }

        _ = NativeMethods.InvalidateRectangle(_operationNotification, 0, true);
        _ = NativeMethods.UpdateWindow(_operationNotification);
        if (NativeMethods.IsWindowVisible(_operationNotificationCancelButton))
        {
            _ = NativeMethods.InvalidateRectangle(_operationNotificationCancelButton, 0, true);
            _ = NativeMethods.UpdateWindow(_operationNotificationCancelButton);
        }
        if (NativeMethods.IsWindowVisible(_operationNotificationActionButton))
        {
            _ = NativeMethods.InvalidateRectangle(_operationNotificationActionButton, 0, true);
            _ = NativeMethods.UpdateWindow(_operationNotificationActionButton);
        }
    }

    private void ToggleTerminalPanel()
    {
        if (_showingTerminalPanel)
        {
            HideTerminalPanel();
        }
        else if (_terminalPanel is null)
        {
            _ = OpenTerminalAsync();
        }
        else
        {
            ShowTerminalPanel();
        }
    }

    private void HideTerminalPanel()
    {
        if (!_showingTerminalPanel || _terminalPanel is null)
        {
            return;
        }

        _showingTerminalPanel = false;
        _terminalPanel.SetVisible(false);
        Layout();
        InvalidateNavigation();
        if (ActiveDocument is not null)
        {
            _ = NativeMethods.SetFocus(ActiveDocument.Handle);
        }
        else if (_fileTree != 0)
        {
            _ = NativeMethods.SetFocus(_fileTree);
        }
    }

    private void ShowFileHistory(string fullPath)
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        string relativePath = Path.GetRelativePath(_workspaceRoot, fullPath).Replace('\\', '/');
        ShowHistoryPanel();
        _historyPanel?.SetFileFilter(relativePath);
    }

    private async Task ShowBlameAsync(string relativePath, IReadOnlyList<GitBlameLine> lines,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            _workspaceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string workspace = _workspaceRoot;
        Task opening = OpenDocumentAsync(fullPath, preserveBottomToolWindow: true, preservePendingBlame: true);
        int selectionVersion = _documentSelectionVersion;
        await opening;
        if (_disposed || cancellationToken.IsCancellationRequested || _workspaceRoot != workspace
            || selectionVersion != _documentSelectionVersion
            || _showingGitDiff || _showingReferenceComparison
            || ActiveDocument?.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase) != true) return;
        NativeDocumentView? document = ActiveDocument;
        if (document is null || !document.ShowBlame(lines, commitHash => _ = LocateHistoryCommitAsync(commitHash)))
        {
            SetStatus(UiText.HistoryUnavailable);
            return;
        }

        SetStatus($"{UiText.Blame}：{relativePath}");
    }

    private async Task ShowBlameForPathAsync(string relativePath)
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        ShowHistoryPanel();
        if (_historyPanel is not null)
        {
            await _historyPanel.ShowBlameForPathAsync(relativePath);
        }
    }

    private async Task LocateHistoryCommitAsync(string commitHash)
    {
        ShowHistoryPanel();
        if (_historyPanel is not null)
        {
            await _historyPanel.LocateCommitAsync(commitHash);
        }
    }

    private async void ShowCloneDialog()
    {
        try
        {
            GitRuntimeInfo runtime = await ResolveGitRuntimeAsync(
                _treeGitStatusCancellation?.Token ?? CancellationToken.None);
            if (!runtime.IsAvailable)
            {
                string reason = runtime.UnavailableReason ?? UiText.GitUnavailable;
                SetStatus(reason);
                ShowOperationResult(UiText.CloneFailed, reason, error: true);
                return;
            }

            string? clonedPath = NativeCloneDialog.Show(_handle, runtime, _settings);
            if (clonedPath is not null && await OpenWorkspaceAsync(clonedPath))
            {
                SetStatus(UiText.CloneCompleted);
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or IOException)
        {
            SetStatus(exception.Message);
            ShowOperationResult(UiText.CloneFailed, exception.Message, error: true);
        }
    }

    private void ShowSearch(WorkspaceSearchMode mode)
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        if (_searchPanel?.Mode == mode)
        {
            _searchPanel.FocusSearchBox();
            return;
        }

        if (_searchPanel is not null)
        {
            CloseSearch();
        }

        _searchFocusBeforeOpen = NativeMethods.GetFocus();
        _searchPanel = new(
            _handle,
            _workspaceRoot,
            mode,
            (path, line) => _ = OpenPreviewDocumentAsync(path, line),
            (path, line) => _ = OpenDocumentAsync(path, line, preserveBottomToolWindow: true),
            CloseSearch,
            SetStatus,
            Layout,
            () => NativeTheme.IsDark(_settings.Theme));
        Layout();
        // 搜索是覆盖当前工作区的非模态浮层，必须位于编辑器和工具窗口前方。
        RaiseChildWindow(_searchPanel.Handle);
        InvalidateNavigation();
        _searchPanel.FocusSearchBox();
    }

    private void CloseSearch()
    {
        NativeSearchPanel? panel = _searchPanel;
        if (panel is null)
        {
            return;
        }

        _searchPanel = null;
        panel.Dispose();
        InvalidateNavigation();
        nint focus = _searchFocusBeforeOpen;
        _searchFocusBeforeOpen = 0;
        nint ownerRoot = NativeMethods.GetAncestor(_handle, NativeMethods.GetAncestorRoot);
        if (focus != 0
            && NativeMethods.IsWindow(focus)
            && NativeMethods.IsWindowVisible(focus)
            && NativeMethods.IsWindowEnabled(focus)
            && NativeMethods.GetAncestor(focus, NativeMethods.GetAncestorRoot) == ownerRoot)
        {
            _ = NativeMethods.SetFocus(focus);
        }
        else if (ActiveDocument is not null)
        {
            _ = NativeMethods.SetFocus(ActiveDocument.Handle);
        }
    }

    private void ShowAppearanceSettings()
    {
        ApplicationSettings initialSettings = _settings;
        ApplicationSettings? updated = NativeAppearanceDialog.Show(
            _handle,
            _settings,
            preview =>
            {
                _settings = preview;
                ApplyAppearance();
            });
        if (updated is null)
        {
            _settings = initialSettings;
            ApplyAppearance();
            return;
        }

        ApplyConfirmedSettings(initialSettings, updated);
    }

    private void ApplyConfirmedSettings(ApplicationSettings initialSettings, ApplicationSettings updated)
    {
        _settings = updated;
        _saved = false;
        bool gitExecutableChanged = !string.Equals(
            initialSettings.GitExecutablePath,
            updated.GitExecutablePath,
            StringComparison.OrdinalIgnoreCase);
        bool reopenGitPanel = _showingGitPanel;
        bool reopenHistoryPanel = _showingHistoryPanel;
        _branchPopup?.Dispose();
        _branchPopup = null;
        // 字体和主题沿用现有浏览上下文，只有 Git 路径变化需要重新建立服务。
        if (gitExecutableChanged)
        {
            _gitPanel?.Dispose();
            _gitPanel = null;
            _historyPanel?.Dispose();
            _historyPanel = null;
        }
        _terminalPanel?.ApplyAppearance(_settings);
        ApplyAppearance();
        if (gitExecutableChanged || _gitRuntimeUnavailable)
        {
            RetryGitRuntimeDetection();
        }
        if (gitExecutableChanged && reopenGitPanel)
        {
            ShowGitChangesPanel();
        }

        if (gitExecutableChanged && reopenHistoryPanel)
        {
            ShowHistoryPanel();
        }

        SetStatus(UiText.SettingsApplied);
    }

    private void ApplyAppearance()
    {
        nint firstVisible = NativeMethods.SendMessage(_fileTree,
            NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewFirstVisible, 0);
        NativeTheme.ConfigureUiTypography(_settings.TextFontFamily, _settings.UiFontSize);
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, dark);
        foreach (nint control in new[]
        {
            _applicationIcon,
            _openFolderButton,
            _cloneButton,
            _recentWorkspacesButton,
            _quickOpenButton,
            _currentFileButton,
            _searchButton,
            _settingsButton,
            _filesButton,
            _gitButton,
            _historyButton,
            _terminalButton,
            _minimizeButton,
            _maximizeButton,
            _closeButton,
            _projectHeader,
            _locateActiveFileButton,
            _collapseTreeButton,
            _treeOptionsButton,
            _hideProjectButton,
            _fileTree,
            _documentTabs,
            _emptyDocumentLabel,
            _workspaceLabel,
            _statusBar,
            _operationNotification,
            _operationNotificationCancelButton,
            _operationNotificationActionButton,
            _cancelBranchButton,
        })
        {
            NativeTheme.ApplyToControl(control, dark);
        }

        NativeThemePalette palette = NativeTheme.Palette(dark);
        int treeItemHeight = NativeTheme.ContentHeight(27, 8);
        treeItemHeight += treeItemHeight & 1;
        if (unchecked((int)NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewGetItemHeight, 0, 0)) != treeItemHeight)
            _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewSetItemHeight, unchecked((nuint)treeItemHeight), 0);
        nint deviceContext = NativeMethods.GetDeviceContext(_handle);
        try
        {
            _statusCancelWidth = Math.Max(S(70), MeasureTextWidth(deviceContext,
                NativeMethods.GetWindowTextValue(_cancelBranchButton), NativeTheme.UiFont) + S(16));
        }
        finally { _ = NativeMethods.ReleaseDeviceContext(_handle, deviceContext); }
        UpdateStatusBar(forceMeasure: true);
        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSetBackgroundColor,
            0,
            unchecked((nint)palette.Panel));
        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSetTextColor,
            0,
            unchecked((nint)palette.Text));

        foreach (DocumentTabState document in _documents)
        {
            document.View?.ApplyAppearance(_settings);
        }

        _gitPanel?.ApplyAppearance(_settings);
        _historyPanel?.ApplyAppearance(_settings);
        _comparisonView?.ApplyAppearance(_settings);
        _terminalPanel?.ApplyAppearance(_settings);
        _toolTip?.ApplyAppearance(dark);

        Layout();
        if (firstVisible != 0)
            _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewSelectItem, NativeMethods.TreeViewFirstVisible, firstVisible);
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private bool HandleShortcut(NativeMethods.Message message)
    {
        if (_branchPopup?.ContainsWindow(message.Window) == true)
        {
            // 分支弹层只共用区域内 Tab 导航，其他输入由弹层自身处理。
            return message.MessageId == NativeMethods.WindowMessageKeyDown
                && message.WordParameter == NativeMethods.VirtualKeyTab
                && NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) >= 0
                && _branchPopup.HandleTabNavigation(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
        }
        if (!NativeFocusNavigation.ContainsWindow(_handle, message.Window)
            && _searchPanel?.ContainsWindow(message.Window) != true)
        {
            // 菜单、溢出工具栏和其他独立窗口先处理自己的按键，不能穿透到正文或主菜单。
            return false;
        }

        if (message.MessageId == NativeMethods.WindowMessageKeyDown
            && message.WordParameter == NativeMethods.VirtualKeyEscape
            && EndSplitterDrag()) return true;
        if (message.MessageId == NativeMethods.WindowMessageKeyDown
            && message.WordParameter == NativeMethods.VirtualKeyEscape
            && ActiveDocument?.TryCancelMarkdownSplitterDrag() == true) return true;
        if (_comparisonView?.HandleShortcut(message) == true)
        {
            return true;
        }
        if (_gitPanel?.HandleDiffToolbarInput(message) == true)
        {
            return true;
        }
        if (message.MessageId != NativeMethods.WindowMessageKeyDown)
        {
            return false;
        }

        if (_terminalPanel?.ContainsWindow(message.Window) == true)
        {
            return false;
        }

        if (_historyPanel?.HandleShortcut(message) == true)
        {
            return true;
        }

        if (_searchPanel?.IsComposingKey(message) == true) return false;
        if (_searchPanel?.HandleShortcut(message) == true)
        {
            return true;
        }

        if (ActiveDocument?.HandleFindShortcut(message) == true) return true;
        if (ActiveDocument?.HandleJsonErrorShortcut(message) == true) return true;
        if (ActiveDocument?.HandleToolbarShortcut(message) == true) return true;

        int key = unchecked((int)message.WordParameter);
        bool control = NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) < 0;
        bool shift = NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0;
        if (ActiveDocument?.IsFindInputComposing == true
            && key is NativeMethods.VirtualKeyEnter or NativeMethods.VirtualKeyEscape or NativeMethods.VirtualKeyTab)
            return false;
        if (key == NativeMethods.VirtualKeyTab && !control)
        {
            return HandleTabNavigation(shift);
        }

        if (!control
            && NativeMethods.GetFocus() == _documentTabs
            && (key is NativeMethods.VirtualKeyLeft
                or NativeMethods.VirtualKeyRight
                or NativeMethods.VirtualKeyEnter))
        {
            return HandleDocumentTabsShortcut(key);
        }

        return HandleApplicationShortcut(key, control, shift);
    }

    private bool HandleDocumentTabsShortcut(int key)
    {
        if (_documentTabs == 0 || NativeMethods.GetFocus() != _documentTabs)
        {
            return false;
        }

        List<DocumentTabTarget> targets = _documents
            .Select((_, index) => new DocumentTabTarget(index, null))
            .Concat(BuildTransientEditorTabs().Select(tab => new DocumentTabTarget(null, tab)))
            .ToList();
        if (targets.Count == 0)
        {
            return false;
        }

        int currentIndex = targets.FindIndex(target =>
            target.Transient?.Active == true
            || (target.DocumentIndex == _activeDocumentIndex
                && !_showingGitDiff
                && !_showingReferenceComparison));
        if (currentIndex < 0)
        {
            currentIndex = Math.Clamp(_activeDocumentIndex, 0, targets.Count - 1);
        }

        if (key == NativeMethods.VirtualKeyEnter)
        {
            ActivateDocumentTabTarget(targets[currentIndex]);
            return true;
        }

        if (key is not (NativeMethods.VirtualKeyLeft or NativeMethods.VirtualKeyRight))
        {
            return false;
        }

        int nextIndex = key == NativeMethods.VirtualKeyLeft
            ? (currentIndex + targets.Count - 1) % targets.Count
            : (currentIndex + 1) % targets.Count;
        ActivateDocumentTabTarget(targets[nextIndex]);
        return true;
    }

    private void ActivateDocumentTabTarget(DocumentTabTarget target)
    {
        bool restoreTabFocus = NativeMethods.GetFocus() == _documentTabs;
        if (target.DocumentIndex is int documentIndex)
        {
            SelectDocument(documentIndex);
            if (restoreTabFocus)
            {
                _ = NativeMethods.SetFocus(_documentTabs);
            }
            return;
        }

        if (target.Transient is { } transient)
        {
            if (transient.Kind == TransientEditorTabKind.ReferenceComparison)
            {
                ActivateReferenceComparisonTab();
                if (restoreTabFocus)
                {
                    _ = NativeMethods.SetFocus(_documentTabs);
                }
            }
            else
            {
                _ = ActivateGitDiffTabFromKeyboardAsync(transient.Key, restoreTabFocus);
            }
        }
    }

    private async Task ActivateGitDiffTabFromKeyboardAsync(string relativePath, bool restoreTabFocus)
    {
        await ActivateGitDiffTabAsync(relativePath);
        if (restoreTabFocus
            && !_disposed
            && _documentTabs != 0
            && NativeMethods.IsWindow(_documentTabs))
        {
            _ = NativeMethods.SetFocus(_documentTabs);
        }
    }

    private bool HandleTabNavigation(bool backwards)
    {
        nint focus = NativeMethods.GetFocus();
        if (_terminalPanel?.ContainsHeaderWindow(focus) == true)
            return _terminalPanel.HandleHeaderTabNavigation(focus, backwards);
        if (_branchPopup?.ContainsWindow(focus) == true)
        {
            return _branchPopup.HandleTabNavigation(backwards);
        }

        if (_searchPanel?.ContainsWindow(focus) == true)
        {
            return _searchPanel.HandleTabNavigation(backwards);
        }

        if (ActiveDocument?.ContainsFindWindow(focus) == true)
        {
            return ActiveDocument.HandleFindTabNavigation(backwards);
        }

        if (_showingReferenceComparison && _comparisonView is { IsVisible: true } comparison
            && (focus == _documentTabs || comparison.ContainsWindow(focus)))
        {
            return comparison.HandleTabNavigation(backwards, _documentTabs);
        }

        if (focus == _documentTabs)
        {
            return ActiveDocument?.FocusNavigationControl(backwards) == true;
        }

        if (ActiveDocument?.ContainsWindow(focus) == true)
        {
            return ActiveDocument.HandleTabNavigation(backwards, _documentTabs);
        }

        if (_workspaceOpenDialog is not null)
        {
            return false;
        }

        if (_gitPanel?.ContainsWindow(focus) == true)
        {
            return _gitPanel.HandleTabNavigation(backwards);
        }

        if (_historyPanel?.ContainsWindow(focus) == true)
        {
            return _historyPanel.HandleTabNavigation(backwards);
        }

        if (IsProjectRegionWindow(focus))
        {
            return NativeFocusNavigation.MoveWithinRegion(
                [
                    _fileTree,
                    _locateActiveFileButton,
                    _collapseTreeButton,
                    _treeOptionsButton,
                    _hideProjectButton,
                ],
                focus,
                backwards);
        }

        if (IsActivityRegionWindow(focus))
        {
            return NativeFocusNavigation.MoveWithinRegion(
                [_filesButton, _gitButton, _searchButton, _terminalButton, _historyButton],
                focus,
                backwards);
        }

        if (IsTopBarWindow(focus))
        {
            return NativeFocusNavigation.MoveWithinRegion(
                [
                    _openFolderButton,
                    _cloneButton,
                    _recentWorkspacesButton,
                    _currentFileButton,
                    _quickOpenButton,
                    _settingsButton,
                ],
                focus,
                backwards);
        }

        if (IsEditorRegionWindow(focus))
        {
            return NativeFocusNavigation.MoveWithinRegion(
                [_documentTabs, GetActiveEditorSurfaceHandle()],
                focus,
                backwards);
        }

        return false;
    }

    private bool IsProjectRegionWindow(nint window)
    {
        return window == _fileTree
            || window == _locateActiveFileButton
            || window == _collapseTreeButton
            || window == _treeOptionsButton
            || window == _hideProjectButton;
    }

    private bool IsActivityRegionWindow(nint window)
    {
        return window == _filesButton
            || window == _gitButton
            || window == _searchButton
            || window == _terminalButton
            || window == _historyButton;
    }

    private bool IsTopBarWindow(nint window)
    {
        return window == _openFolderButton
            || window == _cloneButton
            || window == _recentWorkspacesButton
            || window == _currentFileButton
            || window == _quickOpenButton
            || window == _settingsButton;
    }

    private bool IsEditorRegionWindow(nint window)
    {
        nint activeSurface = GetActiveEditorSurfaceHandle();
        return window == _documentTabs
            || NativeFocusNavigation.ContainsWindow(activeSurface, window);
    }

    private nint GetActiveEditorSurfaceHandle()
    {
        if (_showingGitDiff && _gitPanel is not null)
        {
            return _gitPanel.DiffHandleForTest;
        }

        if (_showingReferenceComparison && _comparisonView is not null)
        {
            return _comparisonView.Handle;
        }

        return ActiveDocument?.Handle ?? 0;
    }

    private bool HandleApplicationShortcut(int key, bool control, bool shift)
    {
        if (control && key is ('P' or 'p'))
        {
            ShowQuickOpen();
            return true;
        }

        if (control && shift && key is ('F' or 'f'))
        {
            ShowWorkspaceSearch();
            return true;
        }

        if (control && !shift && key is ('F' or 'f') && _showingGitPanel && _gitPanel is not null)
        {
            _gitPanel.ShowFind();
            return true;
        }

        if (control && !shift && key is ('F' or 'f') && ActiveDocument is not null)
        {
            ActiveDocument.ShowFind();
            return true;
        }

        if (control && key is ('G' or 'g') && ActiveDocument is not null)
        {
            ActiveDocument.ShowGoToLine();
            return true;
        }

        if (control && key is ('W' or 'w'))
        {
            CloseActiveDocument();
            return true;
        }

        if (key == NativeMethods.VirtualKeyEnter && NativeMethods.GetFocus() == _fileTree)
        {
            TreeNodeState? node = GetSelectedTreeNode();
            if (node is not null)
            {
                _ = OpenTreeNodeAsync(node);
            }

            return node is not null;
        }

        if (key == NativeMethods.VirtualKeyF5)
        {
            _ = RefreshWorkspaceAsync();
            return true;
        }

        if (key == NativeMethods.VirtualKeyEscape && _searchPanel is not null)
        {
            CloseSearch();
            return true;
        }

        if (key == NativeMethods.VirtualKeyEscape && _mainMenuOpen)
        {
            CloseMainMenu();
            return true;
        }

        if (key == NativeMethods.VirtualKeyEscape && _showingGitDiff
            && _gitPanel?.DismissDiffBoundaryHint() == true)
        {
            return true;
        }

        if (key == NativeMethods.VirtualKeyEscape && ActiveDocument is not null)
        {
            ActiveDocument.HideFind();
            return true;
        }

        return false;
    }

    private NativeDocumentView? ActiveDocument => _activeDocumentIndex >= 0 && _activeDocumentIndex < _documents.Count
        ? _documents[_activeDocumentIndex].View
        : null;

    private void OnWorkspaceFilesChanged(object? sender, FileChangeBatchEventArgs eventArgs)
    {
        if (!ReferenceEquals(sender, _fileWatcher))
        {
            return;
        }

        if (_workspaceFileChangeQueue.Enqueue(eventArgs.Paths))
        {
            Post(() => _ = DrainWorkspaceFileChangesAsync());
        }
    }

    private async Task DrainWorkspaceFileChangesAsync()
    {
        if (!_workspaceFileChangeQueue.TryBeginDrain(out WorkspaceFileChangeBatch batch))
        {
            return;
        }

        try
        {
            do
            {
                await HandleWorkspaceFilesChangedAsync(batch);
            }
            while (_workspaceFileChangeQueue.TryTakeNext(out batch));
        }
        finally
        {
            if (_workspaceFileChangeQueue.EndDrain())
            {
                Post(() => _ = DrainWorkspaceFileChangesAsync());
            }
        }
    }

    private async Task HandleWorkspaceFilesChangedAsync(WorkspaceFileChangeBatch batch)
    {
        string? workspaceRoot = _workspaceRoot;
        int treeGeneration = _treeGeneration;
        if (workspaceRoot is null
            || !IsCurrentWorkspaceFileChange(batch.Version, workspaceRoot, treeGeneration))
        {
            return;
        }

        if (_gitPanel is null)
        {
            RequestTreeGitStatusRefresh();
        }
        else
        {
            _gitPanel.RequestRefresh(
                batch.RequiresFullRefresh ? null : batch.Paths,
                refreshActiveDiff: batch.RequiresFullRefresh);
        }

        HashSet<string> changed = batch.Paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (DocumentTabState document in _documents.ToArray())
        {
            if (document.View is { } view
                && (batch.RequiresFullRefresh || changed.Contains(document.Path))
                && IsCurrentWorkspaceFileChange(batch.Version, workspaceRoot, treeGeneration))
            {
                Augit.Core.Documents.DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(
                    workspaceRoot,
                    document.Path);
                if (!IsCurrentWorkspaceFileChange(batch.Version, workspaceRoot, treeGeneration)
                    || !_documents.Contains(document)
                    || !ReferenceEquals(document.View, view))
                {
                    return;
                }

                await view.ReloadAsync(result);
                if (!IsCurrentWorkspaceFileChange(batch.Version, workspaceRoot, treeGeneration))
                {
                    return;
                }
            }
        }

        foreach (string parentPath in batch.GetTreeRefreshDirectories(workspaceRoot))
        {
            if (!IsCurrentWorkspaceFileChange(batch.Version, workspaceRoot, treeGeneration))
            {
                return;
            }

            TreeNodeState? node = _treeNodes.Values.FirstOrDefault(
                candidate => (candidate.IsLoaded || candidate.IsExpanded || candidate.IsLoading)
                    && candidate.FullPath.Equals(parentPath, StringComparison.OrdinalIgnoreCase));
            if (node is not null)
            {
                await LoadTreeNodeAsync(
                    node,
                    force: true,
                    canApply: () => IsCurrentWorkspaceFileChange(
                        batch.Version,
                        workspaceRoot,
                        treeGeneration));
            }
        }

        if (!IsCurrentWorkspaceFileChange(batch.Version, workspaceRoot, treeGeneration))
        {
            return;
        }

        SetStatus(UiText.ExternalChangesSynchronized);
    }

    private bool IsCurrentWorkspaceFileChange(long version, string workspaceRoot, int treeGeneration)
    {
        return !_disposed
            && _workspaceFileChangeQueue.IsCurrent(version)
            && _workspaceRoot?.Equals(workspaceRoot, StringComparison.OrdinalIgnoreCase) == true
            && _treeGeneration == treeGeneration;
    }

    private void OnActivationRequested(object? sender, EventArgs eventArgs)
    {
        _ = NativeMethods.PostMessage(_handle, NativeMethods.WindowMessageAppActivate, 0, 0);
    }

    private void DrainDispatchQueue()
    {
        while (_dispatchQueue.TryDequeue(out Action? action))
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message);
            }
        }
    }

    private void SetStatus(string text)
    {
        _statusVersion++;
        _statusText = text;
        _statusIsFileType = text == ActiveDocument?.TypeName;
        UpdateStatusBar();
    }

    private void SetGitStatus(string text)
    {
        SetStatus(text);
        (string Title, string Detail, bool IsProgress, bool IsError, bool CanCancel)? feedback =
            ResolveOperationFeedback(text);
        if (feedback is not { } resolved)
        {
            return;
        }

        if (resolved.IsProgress)
        {
            ShowOperationProgress(resolved.Title, resolved.Detail, resolved.CanCancel);
        }
        else
        {
            ShowOperationResult(resolved.Title, resolved.Detail, resolved.IsError);
        }
    }

    internal static (string Title, string Detail, bool IsProgress, bool IsError, bool CanCancel)?
        ResolveOperationFeedbackForTest(string text)
    {
        return ResolveOperationFeedback(text);
    }

    private static (string Title, string Detail, bool IsProgress, bool IsError, bool CanCancel)?
        ResolveOperationFeedback(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string normalized = text.Trim();
        bool progress = normalized.StartsWith("正在执行", StringComparison.Ordinal)
            || normalized.StartsWith("正在提交", StringComparison.Ordinal)
            || normalized.StartsWith("正在推送", StringComparison.Ordinal)
            || normalized.StartsWith("正在初始化", StringComparison.Ordinal)
            || normalized.StartsWith("正在 Rollback", StringComparison.Ordinal)
            || normalized.StartsWith("正在切换", StringComparison.Ordinal);
        if (progress)
        {
            string detail = normalized.Equals(UiText.SmartCheckoutRunning, StringComparison.Ordinal)
                ? "正在恢复临时 Stash。"
                : "正在等待本机 Git 完成。";
            return (normalized, detail, true, false, true);
        }

        bool error = normalized.Contains("失败", StringComparison.Ordinal)
            || normalized.Contains("无法", StringComparison.Ordinal)
            || normalized.Contains("不可用", StringComparison.Ordinal)
            || normalized.Contains("未配置", StringComparison.Ordinal)
            || normalized.Contains("产生冲突", StringComparison.Ordinal);
        bool completed = normalized.Contains("已完成", StringComparison.Ordinal)
            || normalized.Contains("已初始化", StringComparison.Ordinal)
            || normalized.Contains("已取消", StringComparison.Ordinal);
        if (!error && !completed)
        {
            return null;
        }

        int separator = normalized.IndexOf('：');
        string title = separator > 0 ? normalized[..separator].TrimEnd('。') : normalized.TrimEnd('。');
        string detailText = separator > 0
            ? normalized[(separator + 1)..].Trim()
            : error
                ? "相关工具窗口和用户输入保持不变。"
                : "已读取最新仓库状态。";
        return (title, detailText, false, error, false);
    }

    private void ShowOperationProgress(string title, string detail, bool canCancel)
    {
        SetStatus(title);
        ShowOperationNotification(
            title,
            detail,
            string.Empty,
            OperationNotificationKind.Progress,
            canCancel,
            autoDismiss: false);
    }

    private void ShowOperationResult(string title, string detail, bool error)
    {
        SetStatus(title);
        ShowOperationNotification(
            title,
            detail,
            "关闭提示后不保留命令输出或操作历史。",
            error ? OperationNotificationKind.Error : OperationNotificationKind.Information,
            canCancel: false,
            autoDismiss: true);
    }

    private void ShowOperationNotification(
        string title,
        string detail,
        string footnote,
        OperationNotificationKind kind,
        bool canCancel,
        bool autoDismiss,
        bool showConfigureGit = false)
    {
        _operationNotificationDismissal?.Cancel();
        _operationNotificationDismissal?.Dispose();
        _operationNotificationDismissal = null;
        _operationNotificationTitle = title;
        _operationNotificationDetail = detail;
        _operationNotificationFootnote = footnote;
        _operationNotificationKind = kind;
        _operationNotificationShowConfigureGit = showConfigureGit;
        int version = ++_operationNotificationVersion;
        _ = NativeMethods.ShowWindow(_operationNotification, NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(
            _operationNotificationCancelButton,
            canCancel ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(
            _operationNotificationActionButton,
            showConfigureGit ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        Layout();
        EnsureOperationNotificationOnTop(redraw: true);
        if (!autoDismiss)
        {
            return;
        }

        _operationNotificationDismissal = new CancellationTokenSource();
        _ = DismissOperationNotificationAfterDelayAsync(
            version,
            _operationNotificationDismissal.Token);
    }

    private async Task DismissOperationNotificationAfterDelayAsync(
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_disposed && version == _operationNotificationVersion)
        {
            HideOperationNotification();
        }
    }

    private void HideOperationNotification()
    {
        _operationNotificationDismissal?.Cancel();
        _operationNotificationDismissal?.Dispose();
        _operationNotificationDismissal = null;
        _operationNotificationKind = OperationNotificationKind.None;
        _operationNotificationShowConfigureGit = false;
        _operationNotificationVersion++;
        _ = NativeMethods.ShowWindow(_operationNotificationCancelButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_operationNotificationActionButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_operationNotification, NativeMethods.ShowHide);
    }

    private void OnDestroyed(nint window)
    {
        if (_destroyed)
        {
            return;
        }

        _destroying = true;
        ClearFrameButtonHover(_hoveredFrameButton);
        CaptureWindowPlacement(window);
        SaveSettings();
        ReleaseWorkspaceResources();
        lock (InstancesGate)
        {
            Instances.Remove(window);
        }

        _handle = 0;
        _destroyed = true;
        NativeMethods.PostQuitMessage(0);
    }

    private void CaptureWindowPlacement(nint window)
    {
        NativeMethods.WindowPlacement placement = new()
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.WindowPlacement>(),
        };
        if (!_shown || !NativeMethods.GetWindowPlacement(window, ref placement))
        {
            return;
        }

        NativeMethods.Rectangle rectangle = placement.NormalPosition;
        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;
        if (width < MinimumWidth || height < MinimumHeight)
        {
            return;
        }

        _settings = _settings with
        {
            Window = new()
            {
                Left = rectangle.Left,
                Top = rectangle.Top,
                Width = NativeTheme.Unscale(width),
                Height = NativeTheme.Unscale(height),
                IsMaximized = NativeMethods.IsZoomed(window),
            },
        };
    }

    private void SaveSettings()
    {
        if (_saved)
        {
            return;
        }

        _saved = true;
        _settings = _settings with
        {
            LastWorkspace = _workspaceRoot ?? _settings.LastWorkspace,
            OpenFiles = _documents
                .Where(document => !document.IsPreview)
                .Select(document => document.Path)
                .Take(50)
                .ToArray(),
            ActiveFile = _activeDocumentIndex >= 0 && _activeDocumentIndex < _documents.Count
                && !_documents[_activeDocumentIndex].IsPreview
                ? _documents[_activeDocumentIndex].Path
                : null,
            ExpandedDirectories = _treeNodes.Values
                .Where(node => node.IsDirectory && node.IsExpanded)
                .Select(node => node.FullPath)
                .Take(50)
                .ToArray(),
            ToolWindows = new()
            {
                ProjectPanelWidth = _projectPanelWidth > 0
                    ? NativeTheme.Unscale(_projectPanelWidth)
                    : null,
                BottomPanelHeight = _bottomPanelHeight > 0
                    ? NativeTheme.Unscale(_bottomPanelHeight)
                    : null,
            },
        };
        _settingsStore.SaveAsync(_settings).GetAwaiter().GetResult();
    }

    private void ReleaseWorkspaceResources()
    {
        // 先让正在等待磁盘读取的旧批次失效，避免切换工作区后结果回写到新界面。
        _workspaceFileChangeQueue.Invalidate();
        _branchPopup?.Dispose();
        _branchPopup = null;
        _branchOperationCancellation?.Cancel();
        _branchOperationCancellation?.Dispose();
        _branchOperationCancellation = null;
        if (_cancelBranchButton != 0)
        {
            _ = NativeMethods.ShowWindow(_cancelBranchButton, NativeMethods.ShowHide);
        }
        _terminalPanel?.Dispose();
        _terminalPanel = null;
        _terminalOpeningTask = null;
        _restoreHistoryAfterTerminal = false;
        _workspaceOpenDialog?.Dispose();
        _workspaceOpenDialog = null;
        CloseSearch();
        _gitPanel?.Dispose();
        _gitPanel = null;
        _historyPanel?.Dispose();
        _historyPanel = null;
        _comparisonView?.Dispose();
        _comparisonView = null;
        _showingReferenceComparison = false;
        _referenceComparisonDocument = null;
        _showingGitPanel = false;
        _showingGitDiff = false;
        _gitDiffRelativePath = null;
        _previewGitDiffPath = null;
        _projectPanelVisible = true;
        _showingHistoryPanel = false;
        _showingTerminalPanel = false;
        _treeGitStatusCancellation?.Cancel();
        _treeGitStatusCancellation?.Dispose();
        _treeGitStatusCancellation = null;
        _gitRuntime = null;
        _gitRuntimeResolution = null;
        _treeGitRepository = null;
        _treeGitStatusService = null;
        _treeGitStatusIndex = null;
        _treeGitStatusSnapshot = null;
        _treeGitStatusWorkspaceRoot = null;
        _treeGitStatusRefreshing = false;
        _treeGitStatusRefreshPending = false;
        if (_pendingOwnedCoordinator is not null)
        {
            _pendingOwnedCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _pendingOwnedCoordinator = null;
        }

        if (_fileWatcher is not null)
        {
            _fileWatcher.Changed -= OnWorkspaceFilesChanged;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }

        // watcher 的回调可能与 Dispose 并发，第二次失效用于丢弃竞态窗口内补入的旧路径。
        _workspaceFileChangeQueue.Invalidate();

        if (_instanceCoordinator is not null)
        {
            _instanceCoordinator.ActivationRequested -= OnActivationRequested;
            _instanceCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _instanceCoordinator = null;
        }

        foreach (DocumentTabState document in _documents)
        {
            document.View?.Dispose();
        }

        AdvanceDocumentSelection();
        _documents.Clear();
        _activeDocumentIndex = -1;
        _firstVisibleDocumentTabIndex = 0;
        _treeNodes.Clear();
        _treeGeneration++;
    }

    private static int ToFiniteDimension(double value, int fallback)
    {
        return double.IsFinite(value) && value > 0 ? (int)Math.Round(value) : fallback;
    }

    private static NativeWindowBounds CaptureWindowBoundsForTest(nint window)
    {
        return window != 0 && NativeMethods.GetWindowRectangle(window, out NativeMethods.Rectangle rectangle)
            ? new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom)
            : default;
    }

    private static string[] BuildRecentWorkspaces(string current, IReadOnlyList<string> existing)
    {
        return existing
            .Prepend(current)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
    }

    private static bool IsFinite(double? value)
    {
        return value is not null && double.IsFinite(value.Value);
    }

    private sealed class TreeNodeState(
        int id,
        string name,
        string fullPath,
        bool isDirectory,
        bool canExpand,
        int parentId)
    {
        internal int Id { get; } = id;

        internal string Name { get; } = name;

        internal string FullPath { get; } = fullPath;

        internal bool IsDirectory { get; } = isDirectory;

        internal bool CanExpand { get; } = canExpand;

        internal int ParentId { get; } = parentId;

        internal nint ItemHandle { get; set; }

        internal bool IsLoaded { get; set; }

        internal bool IsLoading { get; set; }

        internal bool IsExpanded { get; set; }
    }

    private sealed record BranchMenuContext(
        GitRepositorySnapshot Repository,
        GitReferenceService Service,
        GitReferenceSnapshot Snapshot,
        GitOperationService OperationService);

    private enum SplitterKind
    {
        None,
        Project,
        Bottom,
    }

    private readonly record struct DocumentTabLayout(
        int DocumentIndex,
        NativeMethods.Rectangle Rectangle);

    private enum TransientEditorTabKind
    {
        GitDiff,
        ReferenceComparison,
    }

    private readonly record struct TransientEditorTab(
        TransientEditorTabKind Kind,
        string Key,
        string Title,
        int Width,
        bool Active,
        bool IsPreview = false);

    private readonly record struct DocumentTabTarget(
        int? DocumentIndex,
        TransientEditorTab? Transient);

    private readonly record struct TransientEditorTabLayout(
        TransientEditorTab Tab,
        NativeMethods.Rectangle Rectangle);

    private sealed class DocumentTabState(string path)
    {
        internal string Path { get; set; } = path;

        internal bool IsPreview { get; set; }

        internal NativeDocumentView? View { get; set; }

        internal Task<NativeDocumentView?>? LoadingTask { get; set; }
    }

    private sealed class WindowSynchronizationContext(MainWindow window) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            window.Post(() => callback(state));
        }
    }
}

internal readonly record struct NativeWindowBounds(int Left, int Top, int Right, int Bottom)
{
    internal bool IsValid => Right > Left && Bottom > Top;
}

internal readonly record struct NativeGitDiffGeometrySnapshot(
    NativeWindowBounds DocumentTabs,
    NativeWindowBounds CommitPanel,
    NativeWindowBounds DiffSurface)
{
    internal bool IsValid => DocumentTabs.IsValid && CommitPanel.IsValid && DiffSurface.IsValid;
}
