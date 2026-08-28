using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Files;
using Augit.Core.Git;
using Augit.Infrastructure.Files;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;
using Microsoft.Web.WebView2.Core;

namespace Augit.App;

internal sealed class MainWindow : IDisposable
{
    private const string WindowClassName = "Augit.MainWindow.Native";
    private const int MinimumWidth = 820;
    private const int MinimumHeight = 520;
    private const int ToolbarHeight = 42;
    private const int ActivityBarWidth = 42;
    private const int TreePanelWidth = 278;
    private const int TabHeight = 32;
    private const int StatusHeight = 24;
    private const int TerminalPanelHeight = 280;
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
    private const int MenuCopyPath = 201;
    private const int MenuRevealInExplorer = 202;
    private const int MenuOpenTerminal = 203;
    private const int MenuRefresh = 204;
    private const int MenuFileHistory = 205;
    private const uint TreeViewActionExpand = 0x0002;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, MainWindow> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly SettingsStore _settingsStore;
    private readonly ConcurrentQueue<Action> _dispatchQueue = new();
    private readonly Dictionary<int, TreeNodeState> _treeNodes = [];
    private readonly List<DocumentTabState> _documents = [];
    private ApplicationSettings _settings;
    private WorkspaceInstanceCoordinator? _instanceCoordinator;
    private WorkspaceInstanceCoordinator? _pendingOwnedCoordinator;
    private WorkspaceFileWatcher? _fileWatcher;
    private NativeSearchPanel? _searchPanel;
    private NativeGitPanel? _gitPanel;
    private NativeGitHistoryPanel? _historyPanel;
    private NativeTerminalPanel? _terminalPanel;
    private string? _workspaceRoot;
    private nint _handle;
    private nint _openFolderButton;
    private nint _refreshButton;
    private nint _cloneButton;
    private nint _recentWorkspacesButton;
    private nint _quickOpenButton;
    private nint _searchButton;
    private nint _settingsButton;
    private nint _filesButton;
    private nint _gitButton;
    private nint _historyButton;
    private nint _terminalButton;
    private nint _fileTree;
    private nint _documentTabs;
    private nint _emptyDocumentLabel;
    private nint _workspaceLabel;
    private nint _statusBar;
    private int _nextTreeNodeId = 1;
    private int _treeGeneration;
    private int _activeDocumentIndex = -1;
    private bool _shown;
    private bool _showingGitPanel;
    private bool _showingHistoryPanel;
    private bool _saved;
    private bool _openingTerminal;
    private bool _disposed;

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
        _pendingOwnedCoordinator = ownedCoordinator;
        EnsureWindowClass();
        InitializeCommonControls();

        WindowPlacementSettings placement = settings.Window;
        int x = IsFinite(placement.Left) ? (int)Math.Round(placement.Left!.Value) : NativeMethods.UseDefault;
        int y = IsFinite(placement.Top) ? (int)Math.Round(placement.Top!.Value) : NativeMethods.UseDefault;
        int width = Math.Max(MinimumWidth, ToFiniteDimension(placement.Width, 1180));
        int height = Math.Max(MinimumHeight, ToFiniteDimension(placement.Height, 760));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.AppName,
            NativeMethods.WindowStyleOverlappedWindow | NativeMethods.WindowStyleClipChildren,
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

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        SynchronizationContext.SetSynchronizationContext(new WindowSynchronizationContext(this));
        CreateControls();
        ApplyAppearance();
        Layout();
        SetStatus(UiText.NoWorkspace);
        if (!string.IsNullOrWhiteSpace(requestedWorkspace))
        {
            Post(() => _ = ownedCoordinator is null
                ? OpenWorkspaceAsync(requestedWorkspace, restoreState: true)
                : AttachOwnedWorkspaceAsync(requestedWorkspace, restoreState: true));
        }
        else if (ownedCoordinator is not null)
        {
            ownedCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _pendingOwnedCoordinator = null;
        }
    }

    internal nint Handle => _handle;

    internal bool IsVisible => _handle != 0 && NativeMethods.IsWindowVisible(_handle);

    internal string? WorkspaceRoot => _workspaceRoot;

    internal int OpenDocumentCount => _documents.Count;

    internal bool ActiveMarkdownPreviewReady => ActiveDocument?.IsMarkdownPreviewReady == true;

    internal string? ActiveMarkdownPreviewError => ActiveDocument?.MarkdownPreviewError;

    internal int ActiveMarkdownBrowserProcessId => ActiveDocument?.MarkdownBrowserProcessId ?? 0;

    internal bool ActiveImagePreviewReady => ActiveDocument?.IsImagePreviewReady == true;

    internal string? ActiveDocumentText => ActiveDocument?.CurrentText;

    internal bool ActiveDocumentIsReadOnly => ActiveDocument?.IsTextReadOnly != false;

    internal Augit.Core.Documents.DocumentReadStatus? ActiveDocumentStatus => ActiveDocument?.Status;

    internal Augit.Core.Documents.DocumentKind? ActiveDocumentKind => ActiveDocument?.Kind;

    internal bool ActiveDocumentIsShowingAlternative => ActiveDocument?.IsShowingAlternative == true;

    internal Task<int> ShowActiveMarkdownPreviewForTestAsync()
    {
        return ActiveDocument?.ShowMarkdownPreviewForTestAsync() ?? Task.FromResult(0);
    }

    internal int CloseActiveMarkdownPreviewForTest()
    {
        return ActiveDocument?.CloseMarkdownPreviewForTest() ?? 0;
    }

    internal int SearchResultCountForTest => _searchPanel?.ResultCount ?? 0;

    internal int GitChangedFileCountForTest => _gitPanel?.ChangedFileCount ?? 0;

    internal bool GitRuntimeAvailableForTest => _gitPanel?.IsRuntimeAvailable == true;

    internal GitRepositoryKind? GitRepositoryKindForTest => _gitPanel?.RepositoryKind;

    internal int HistoryEntryCountForTest => _historyPanel?.EntryCount ?? 0;

    internal bool HistoryRuntimeAvailableForTest => _historyPanel?.IsRuntimeAvailable == true;

    internal GitRepositoryKind? HistoryRepositoryKindForTest => _historyPanel?.RepositoryKind;

    internal NativeGitHistoryPanel? HistoryPanelForTest => _historyPanel;

    internal bool RollbackButtonCreatedForTest => _gitPanel?.RollbackButtonCreatedForTest == true;

    internal bool AdvancedOperationsButtonCreatedForTest => _gitPanel?.AdvancedOperationsButtonCreatedForTest == true;

    internal bool TerminalCreatedForTest => _terminalPanel is not null;

    internal bool TerminalRunningForTest => _terminalPanel?.IsRunning == true;

    internal int TerminalBrowserProcessIdForTest => _terminalPanel?.BrowserProcessId ?? 0;

    internal int TerminalShellProcessIdForTest => _terminalPanel?.ShellProcessId ?? 0;

    internal void ShowGitForTest()
    {
        ShowGitChangesPanel();
    }

    internal void ShowHistoryForTest()
    {
        ShowHistoryPanel();
    }

    internal void ShowFileHistoryForTest(string path)
    {
        ShowFileHistory(path);
    }

    internal Task<bool> SelectGitFileForTestAsync(string relativePath)
    {
        return _gitPanel?.SelectFileForTestAsync(relativePath) ?? Task.FromResult(false);
    }

    internal Task RollbackSelectedForTestAsync()
    {
        return _gitPanel?.RollbackSelectedForTestAsync() ?? Task.CompletedTask;
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

    internal Task<bool> OpenTerminalForTestAsync()
    {
        return OpenTerminalAsync();
    }

    internal bool CloseTerminalForTest()
    {
        return CloseTerminal(requireConfirmation: false);
    }

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int command = _settings.Window.IsMaximized ? NativeMethods.ShowMaximized : NativeMethods.ShowNormal;
        _ = NativeMethods.ShowWindow(_handle, command);
        _ = NativeMethods.UpdateWindow(_handle);
        _shown = true;
    }

    internal void Activate()
    {
        if (_handle != 0)
        {
            _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
            _ = NativeMethods.SetForegroundWindow(_handle);
        }
    }

    internal void Close()
    {
        if (_handle != 0 && NativeMethods.IsWindow(_handle))
        {
            if (!NativeMethods.DestroyWindow(_handle))
            {
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

        _disposed = true;
        Close();
        ReleaseWorkspaceResources();
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
        ReleaseWorkspaceResources();
        _instanceCoordinator = coordinator;
        _instanceCoordinator.ActivationRequested += OnActivationRequested;
        _workspaceRoot = fullPath;
        _settings = _settings with
        {
            LastWorkspace = fullPath,
            RecentWorkspaces = BuildRecentWorkspaces(fullPath, _settings.RecentWorkspaces),
        };
        _saved = false;
        _fileWatcher = new(fullPath);
        _fileWatcher.Changed += OnWorkspaceFilesChanged;
        _ = NativeMethods.SetWindowText(_handle, $"{Path.GetFileName(fullPath)} — {UiText.AppName}");
        InitializeTree(fullPath);
        SetStatus(UiText.WorkspaceOpened);
        TreeNodeState root = _treeNodes.Values.Single(node => node.ParentId == 0);
        await LoadTreeNodeAsync(root);
        root.IsExpanded = true;
        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewExpand,
            NativeMethods.TreeViewExpandItem,
            root.ItemHandle);

        if (restoreState && restoredWorkspace?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            await RestoreWorkspaceStateAsync();
        }

        return true;
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
                Background = NativeMethods.GetSystemColorBrush(NativeMethods.ColorWindow),
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

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
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
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
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

    private void CreateControls()
    {
        _openFolderButton = CreateControl(NativeMethods.ButtonClass, UiText.OpenFolder, CommandOpenFolder, NativeMethods.ButtonPushButton);
        _cloneButton = CreateControl(NativeMethods.ButtonClass, UiText.Clone, CommandClone, NativeMethods.ButtonPushButton);
        _recentWorkspacesButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.RecentWorkspaces,
            CommandRecentWorkspaces,
            NativeMethods.ButtonPushButton);
        _refreshButton = CreateControl(NativeMethods.ButtonClass, UiText.Refresh, CommandRefresh, NativeMethods.ButtonPushButton);
        _quickOpenButton = CreateControl(NativeMethods.ButtonClass, UiText.QuickOpen, CommandQuickOpen, NativeMethods.ButtonPushButton);
        _searchButton = CreateControl(NativeMethods.ButtonClass, UiText.SearchWorkspace, CommandWorkspaceSearch, NativeMethods.ButtonPushButton);
        _settingsButton = CreateControl(NativeMethods.ButtonClass, UiText.Settings, CommandSettings, NativeMethods.ButtonPushButton);
        _filesButton = CreateControl(NativeMethods.ButtonClass, UiText.FilesSymbol, CommandFiles, NativeMethods.ButtonPushButton);
        _gitButton = CreateControl(NativeMethods.ButtonClass, UiText.GitSymbol, CommandGitChanges, NativeMethods.ButtonPushButton);
        _historyButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.HistorySymbol,
            CommandHistory,
            NativeMethods.ButtonPushButton);
        _terminalButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.TerminalSymbol,
            CommandTerminal,
            NativeMethods.ButtonPushButton);
        _fileTree = CreateControl(
            NativeMethods.TreeViewClass,
            string.Empty,
            301,
            NativeMethods.TreeViewHasButtons
                | NativeMethods.TreeViewHasLines
                | NativeMethods.TreeViewLinesAtRoot
                | NativeMethods.TreeViewShowSelectionAlways
                | NativeMethods.WindowStyleBorder
                | NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.WindowStyleHorizontalScroll);
        _ = NativeMethods.SendMessage(
            _fileTree,
            NativeMethods.TreeViewSetExtendedStyle,
            NativeMethods.TreeViewDoubleBuffer,
            unchecked((nint)NativeMethods.TreeViewDoubleBuffer));
        _documentTabs = CreateControl(
            NativeMethods.TabControlClass,
            string.Empty,
            302,
            NativeMethods.TabControlFocusNever | NativeMethods.WindowStyleClipSiblings);
        _emptyDocumentLabel = CreateControl(
            NativeMethods.StaticClass,
            UiText.NoDocument,
            303,
            NativeMethods.StaticCenter | NativeMethods.StaticCenterImage);
        _workspaceLabel = CreateControl(
            NativeMethods.StaticClass,
            UiText.NoWorkspace,
            304,
            NativeMethods.StaticCenter | NativeMethods.StaticCenterImage);
        _statusBar = CreateControl(NativeMethods.StatusBarClass, string.Empty, 305, 0);
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return control;
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        Move(_openFolderButton, 8, 7, 86, 28);
        Move(_cloneButton, 100, 7, 58, 28);
        Move(_recentWorkspacesButton, 164, 7, 82, 28);
        Move(_refreshButton, 252, 7, 62, 28);
        Move(_quickOpenButton, 320, 7, 82, 28);
        Move(_searchButton, 408, 7, 86, 28);
        Move(_settingsButton, Math.Max(500, width - 96), 7, 88, 28);
        int contentHeight = Math.Max(0, height - ToolbarHeight - StatusHeight);
        int terminalHeight = _terminalPanel is null
            ? 0
            : Math.Min(TerminalPanelHeight, Math.Max(180, contentHeight / 2));
        int upperContentHeight = Math.Max(0, contentHeight - terminalHeight);
        Move(_filesButton, 4, ToolbarHeight + 6, 34, 34);
        Move(_gitButton, 4, ToolbarHeight + 46, 34, 34);
        Move(_historyButton, 4, ToolbarHeight + 86, 34, 34);
        Move(_terminalButton, 4, ToolbarHeight + 126, 34, 34);
        Move(_fileTree, ActivityBarWidth, ToolbarHeight, TreePanelWidth, upperContentHeight);
        int documentLeft = ActivityBarWidth + TreePanelWidth;
        int documentWidth = Math.Max(0, width - documentLeft);
        Move(_documentTabs, documentLeft, ToolbarHeight, documentWidth, TabHeight);
        int documentTop = ToolbarHeight + TabHeight;
        int documentHeight = Math.Max(0, upperContentHeight - TabHeight);
        Move(_emptyDocumentLabel, documentLeft, documentTop, documentWidth, documentHeight);
        Move(_workspaceLabel, ActivityBarWidth, ToolbarHeight, Math.Max(0, width - ActivityBarWidth), upperContentHeight);
        foreach (DocumentTabState document in _documents)
        {
            document.View.SetBounds(documentLeft, documentTop, documentWidth, documentHeight);
        }
        _searchPanel?.SetBounds(
            documentLeft + 36,
            ToolbarHeight + 28,
            Math.Max(320, documentWidth - 72),
            Math.Max(260, upperContentHeight - 56));
        _gitPanel?.SetBounds(
            ActivityBarWidth,
            ToolbarHeight,
            Math.Max(0, width - ActivityBarWidth),
            upperContentHeight);
        _historyPanel?.SetBounds(
            ActivityBarWidth,
            ToolbarHeight,
            Math.Max(0, width - ActivityBarWidth),
            upperContentHeight);
        _terminalPanel?.SetBounds(
            ActivityBarWidth,
            ToolbarHeight + upperContentHeight,
            Math.Max(0, width - ActivityBarWidth),
            terminalHeight);

        Move(_statusBar, 0, Math.Max(0, height - StatusHeight), width, StatusHeight);
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private void HandleCommand(nuint wordParameter)
    {
        switch (NativeMethods.LowWord(wordParameter))
        {
            case CommandOpenFolder:
                OpenFolderFromDialog();
                break;
            case CommandClone:
                ShowCloneDialog();
                break;
            case CommandRefresh:
                _ = RefreshWorkspaceAsync();
                break;
            case CommandQuickOpen:
                ShowQuickOpen();
                break;
            case CommandWorkspaceSearch:
                ShowWorkspaceSearch();
                break;
            case CommandSettings:
                ShowAppearanceSettings();
                break;
            case CommandFiles:
                ShowFilesPanel();
                break;
            case CommandGitChanges:
                ShowGitChangesPanel();
                break;
            case CommandHistory:
                ShowHistoryPanel();
                break;
            case CommandRecentWorkspaces:
                ShowRecentWorkspaces();
                break;
            case CommandTerminal:
                if (_terminalPanel is null)
                {
                    _ = OpenTerminalAsync();
                }
                else
                {
                    _terminalPanel.Focus();
                }
                break;
            case CommandCloseTerminal:
                _ = CloseTerminal(requireConfirmation: true);
                break;
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
                    if (!node.IsDirectory)
                    {
                        _ = OpenDocumentAsync(node.FullPath);
                    }

                    return 0;
                }
            }

            if (header.Code == NativeMethods.TreeViewNotificationRightClick)
            {
                ShowTreeContextMenu();
                return 0;
            }
        }

        if (header.WindowFrom == _documentTabs && header.Code == NativeMethods.TabControlNotificationSelectionChanged)
        {
            int index = checked((int)NativeMethods.SendMessage(
                _documentTabs,
                NativeMethods.TabControlGetCurrentSelection,
                0,
                0));
            SelectDocument(index);
        }

        return 0;
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
        ShowFilesPanel();
    }

    private void InsertTreeNode(TreeNodeState node, nint parentItem)
    {
        NativeMethods.TreeViewInsert insert = new()
        {
            Parent = parentItem,
            InsertAfter = NativeMethods.TreeViewInsertLast,
            Item = new()
            {
                Mask = NativeMethods.TreeViewItemText | NativeMethods.TreeViewItemParameter,
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

    private void InsertPlaceholder(nint parentItem)
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
        _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewInsertItem, 0, ref insert);
    }

    private async Task LoadTreeNodeAsync(TreeNodeState node, bool force = false)
    {
        if (!node.CanExpand || node.IsLoading || (node.IsLoaded && !force) || _workspaceRoot is null)
        {
            return;
        }

        node.IsLoading = true;
        int generation = _treeGeneration;
        IReadOnlyList<WorkspaceEntry> entries;
        try
        {
            entries = await Task.Run(() => WorkspaceDirectoryService.EnumerateChildren(node.FullPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            node.IsLoading = false;
            SetStatus(UiText.DirectoryReadFailed);
            return;
        }

        node.IsLoading = false;
        if (generation != _treeGeneration || _disposed || !_treeNodes.ContainsKey(node.Id))
        {
            return;
        }

        RemoveTreeDescendants(node);
        DeleteTreeChildren(node.ItemHandle);
        foreach (WorkspaceEntry entry in entries)
        {
            TreeNodeState child = new(
                _nextTreeNodeId++,
                entry.Name,
                entry.FullPath,
                entry.IsDirectory,
                entry.CanExpand,
                node.Id);
            InsertTreeNode(child, node.ItemHandle);
            if (child.CanExpand)
            {
                InsertPlaceholder(child.ItemHandle);
            }
        }

        node.IsLoaded = true;
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

    private void DeleteTreeChildren(nint parentItem)
    {
        while (true)
        {
            nint child = NativeMethods.SendMessage(
                _fileTree,
                NativeMethods.TreeViewGetNextItem,
                NativeMethods.TreeViewChild,
                parentItem);
            if (child == 0)
            {
                break;
            }

            _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewDeleteItem, 0, child);
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

    private async Task RestoreWorkspaceStateAsync()
    {
        foreach (string directory in WorkspacePathRules.NormalizeExpandedDirectories(
            _workspaceRoot!,
            _settings.ExpandedDirectories).OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar)))
        {
            await ExpandTreePathAsync(directory);
        }

        foreach (string path in _settings.OpenFiles.Where(File.Exists).Take(50))
        {
            if (WorkspacePathRules.IsWithin(_workspaceRoot!, path))
            {
                await OpenDocumentAsync(path);
            }
        }
    }

    private async Task ExpandTreePathAsync(string path)
    {
        if (_workspaceRoot is null || !WorkspacePathRules.IsWithin(_workspaceRoot, path))
        {
            return;
        }

        TreeNodeState? current = _treeNodes.Values.FirstOrDefault(node => node.ParentId == 0);
        if (current is null)
        {
            return;
        }

        if (current.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
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
    }

    private async Task OpenDocumentAsync(string path, int? lineNumber = null, string? anchor = null)
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        ShowFilesPanel();

        string fullPath = Path.GetFullPath(path);
        int existingIndex = _documents.FindIndex(
            document => document.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            SelectDocument(existingIndex);
            if (lineNumber is not null)
            {
                _documents[existingIndex].View.GoToLine(lineNumber.Value);
            }

            if (!string.IsNullOrEmpty(anchor))
            {
                await _documents[existingIndex].View.NavigateToAnchorAsync(anchor);
            }

            return;
        }

        SetStatus(UiText.ReadingFile);
        Augit.Core.Documents.DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(_workspaceRoot, fullPath);
        if (_disposed || _workspaceRoot is null)
        {
            return;
        }

        NativeDocumentView view = new(
            _handle,
            _workspaceRoot,
            result,
            _settings,
            SetStatus,
            (linkedPath, linkedLine, linkedAnchor) => _ = OpenDocumentAsync(linkedPath, linkedLine, linkedAnchor));
        view.SetVisible(false);
        DocumentTabState state = new(fullPath, view);
        _documents.Add(state);
        string title = Path.GetFileName(fullPath);
        NativeMethods.TabItem item = new()
        {
            Mask = NativeMethods.TabItemText,
            Text = title,
            TextMaximum = title.Length,
        };
        int index = _documents.Count - 1;
        _ = NativeMethods.SendMessage(
            _documentTabs,
            NativeMethods.TabControlInsertItem,
            unchecked((nuint)index),
            ref item);
        SelectDocument(index);
        if (lineNumber is not null)
        {
            view.GoToLine(lineNumber.Value);
        }
        if (!string.IsNullOrEmpty(anchor))
        {
            await view.NavigateToAnchorAsync(anchor);
        }

        _saved = false;
        SetStatus(result.Message.Length == 0 ? result.Classification.TypeName : result.Message);
    }

    private void SelectDocument(int index)
    {
        if (index < 0 || index >= _documents.Count)
        {
            return;
        }

        for (int current = 0; current < _documents.Count; current++)
        {
            _documents[current].View.SetVisible(current == index);
        }

        _activeDocumentIndex = index;
        _ = NativeMethods.SendMessage(
            _documentTabs,
            NativeMethods.TabControlSetCurrentSelection,
            unchecked((nuint)index),
            0);
        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowHide);
        _ = NativeMethods.SetFocus(_documents[index].View.Handle);
    }

    private void CloseActiveDocument()
    {
        if (_activeDocumentIndex < 0 || _activeDocumentIndex >= _documents.Count)
        {
            return;
        }

        int closedIndex = _activeDocumentIndex;
        DocumentTabState document = _documents[closedIndex];
        _documents.RemoveAt(closedIndex);
        document.View.Dispose();
        _ = NativeMethods.SendMessage(
            _documentTabs,
            NativeMethods.TabControlDeleteItem,
            unchecked((nuint)closedIndex),
            0);
        if (_documents.Count == 0)
        {
            _activeDocumentIndex = -1;
            _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowNormal);
        }
        else
        {
            SelectDocument(Math.Min(closedIndex, _documents.Count - 1));
        }

        _saved = false;
    }

    private async Task RefreshWorkspaceAsync()
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        string[] expanded = _treeNodes.Values
            .Where(node => node.IsExpanded && node.IsDirectory)
            .Select(node => node.FullPath)
            .ToArray();
        string? selectedPath = GetSelectedTreeNode()?.FullPath;
        string? firstVisiblePath = GetFirstVisibleTreeNode()?.FullPath;
        InitializeTree(_workspaceRoot);
        TreeNodeState root = _treeNodes.Values.Single(node => node.ParentId == 0);
        await LoadTreeNodeAsync(root);
        foreach (string path in expanded.OrderBy(path => path.Length))
        {
            await ExpandTreePathAsync(path);
        }
        RestoreTreePosition(selectedPath, firstVisiblePath);

        foreach (DocumentTabState document in _documents.ToArray())
        {
            Augit.Core.Documents.DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(
                _workspaceRoot,
                document.Path);
            await document.View.ReloadAsync(result);
        }

        SetStatus(UiText.TreeRefreshed);
    }

    private void ShowTreeContextMenu()
    {
        TreeNodeState? node = GetSelectedTreeNode();
        if (node is null || !NativeMethods.GetCursorPosition(out NativeMethods.Point point))
        {
            return;
        }

        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MenuString, MenuCopyPath, UiText.CopyPath);
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MenuString, MenuRevealInExplorer, UiText.RevealInExplorer);
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MenuString, MenuOpenTerminal, UiText.OpenExternalTerminal);
            if (!node.IsDirectory)
            {
                _ = NativeMethods.AppendMenu(menu, NativeMethods.MenuString, MenuFileHistory, UiText.FileHistory);
            }
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MenuSeparator, 0, null);
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MenuString, MenuRefresh, UiText.Refresh);
            uint command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TrackPopupReturnCommand | NativeMethods.TrackPopupRightButton,
                point.X,
                point.Y,
                _handle,
                0);
            HandleTreeMenuCommand(command, node);
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
        }
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

        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            for (int index = 0; index < recent.Length; index++)
            {
                _ = NativeMethods.AppendMenu(
                    menu,
                    NativeMethods.MenuString,
                    unchecked((nuint)(500 + index)),
                    recent[index].Replace("&", "&&", StringComparison.Ordinal));
            }

            if (!NativeMethods.GetWindowRectangle(_recentWorkspacesButton, out NativeMethods.Rectangle button))
            {
                return;
            }

            uint command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TrackPopupReturnCommand | NativeMethods.TrackPopupRightButton,
                button.Left,
                button.Bottom,
                _handle,
                0);
            int selectedIndex = checked((int)command) - 500;
            if (selectedIndex >= 0 && selectedIndex < recent.Length)
            {
                _ = OpenWorkspaceAsync(recent[selectedIndex]);
            }
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
        }
    }

    private async Task<bool> OpenTerminalAsync()
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return false;
        }

        if (_terminalPanel is not null)
        {
            _terminalPanel.Focus();
            return true;
        }

        if (_openingTerminal)
        {
            return false;
        }

        _openingTerminal = true;
        SetStatus(UiText.TerminalLoading);
        try
        {
            NativeTerminalPanel panel = await NativeTerminalPanel.CreateAsync(
                _handle,
                CommandCloseTerminal,
                _workspaceRoot,
                _settings,
                SetStatus);
            if (_disposed || _workspaceRoot is null)
            {
                panel.Dispose();
                return false;
            }

            _terminalPanel = panel;
            _terminalPanel.SetVisible(true);
            Layout();
            _terminalPanel.Focus();
            SetStatus(UiText.TerminalReady);
            return true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            RuntimeDependencyPrompt.ShowWebView2Missing(_handle);
            SetStatus(UiText.TerminalStartFailed);
            return false;
        }
        catch (Exception exception) when (exception is COMException
            or Win32Exception
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException)
        {
            SetStatus(exception.Message);
            _ = NativeMethods.MessageBox(
                _handle,
                exception.Message,
                UiText.AppName,
                NativeMethods.MessageBoxIconWarning);
            return false;
        }
        finally
        {
            _openingTerminal = false;
        }
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
            && NativeMethods.MessageBox(
                _handle,
                UiText.ConfirmCloseRunningTerminal,
                UiText.AppName,
                NativeMethods.MessageBoxYesNo | NativeMethods.MessageBoxIconWarning) != NativeMethods.DialogResultYes)
        {
            return false;
        }

        _terminalPanel = null;
        panel.Dispose();
        Layout();
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

    private void ShowFilesPanel()
    {
        _showingGitPanel = false;
        _showingHistoryPanel = false;
        _gitPanel?.SetVisible(false);
        _historyPanel?.SetVisible(false);
        bool hasWorkspace = _workspaceRoot is not null;
        _ = NativeMethods.ShowWindow(_workspaceLabel, hasWorkspace ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
        _ = NativeMethods.ShowWindow(_fileTree, hasWorkspace ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_documentTabs, hasWorkspace ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(
            _emptyDocumentLabel,
            hasWorkspace && _documents.Count == 0 ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        for (int index = 0; index < _documents.Count; index++)
        {
            _documents[index].View.SetVisible(hasWorkspace && index == _activeDocumentIndex);
        }

        if (hasWorkspace)
        {
            _ = NativeMethods.SetFocus(_fileTree);
        }
    }

    private void ShowGitChangesPanel()
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        CloseSearch();
        _showingGitPanel = true;
        _showingHistoryPanel = false;
        _historyPanel?.SetVisible(false);
        _gitPanel ??= new NativeGitPanel(
            _handle,
            _workspaceRoot,
            _settings,
            SetStatus,
            path => _ = OpenDocumentAsync(path));
        _ = NativeMethods.ShowWindow(_workspaceLabel, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_fileTree, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_documentTabs, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowHide);
        foreach (DocumentTabState document in _documents)
        {
            document.View.SetVisible(false);
        }

        _gitPanel.SetVisible(true);
        Layout();
        _ = NativeMethods.SetFocus(_gitPanel.Handle);
    }

    private void ShowHistoryPanel()
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        CloseSearch();
        _showingGitPanel = false;
        _showingHistoryPanel = true;
        _gitPanel?.SetVisible(false);
        _historyPanel ??= new NativeGitHistoryPanel(
            _handle,
            _workspaceRoot,
            _settings,
            SetStatus);
        _ = NativeMethods.ShowWindow(_workspaceLabel, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_fileTree, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_documentTabs, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_emptyDocumentLabel, NativeMethods.ShowHide);
        foreach (DocumentTabState document in _documents)
        {
            document.View.SetVisible(false);
        }

        _historyPanel.SetVisible(true);
        Layout();
        _ = NativeMethods.SetFocus(_historyPanel.Handle);
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

    private async void ShowCloneDialog()
    {
        try
        {
            GitRuntimeInfo runtime = await new Augit.Infrastructure.Git.GitExecutableLocator()
                .ResolveAsync(_settings.GitExecutablePath);
            if (!runtime.IsAvailable)
            {
                SetStatus(runtime.UnavailableReason ?? UiText.GitUnavailable);
                _ = NativeMethods.MessageBox(
                    _handle,
                    runtime.UnavailableReason ?? UiText.GitUnavailable,
                    UiText.AppName,
                    NativeMethods.MessageBoxIconWarning);
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
        }
    }

    private void ShowSearch(WorkspaceSearchMode mode)
    {
        if (_workspaceRoot is null)
        {
            SetStatus(UiText.OpenWorkspaceFirst);
            return;
        }

        ShowFilesPanel();

        CloseSearch();
        _searchPanel = new(
            _handle,
            _workspaceRoot,
            mode,
            (path, line) => _ = OpenDocumentAsync(path, line),
            CloseSearch,
            SetStatus);
        Layout();
        _searchPanel.FocusSearchBox();
    }

    private void CloseSearch()
    {
        NativeSearchPanel? panel = _searchPanel;
        _searchPanel = null;
        panel?.Dispose();
        if (ActiveDocument is not null)
        {
            _ = NativeMethods.SetFocus(ActiveDocument.Handle);
        }
    }

    private void ShowAppearanceSettings()
    {
        ApplicationSettings? updated = NativeAppearanceDialog.Show(_handle, _settings);
        if (updated is null)
        {
            return;
        }

        _settings = updated;
        _saved = false;
        bool reopenGitPanel = _showingGitPanel;
        bool reopenHistoryPanel = _showingHistoryPanel;
        _gitPanel?.Dispose();
        _gitPanel = null;
        _historyPanel?.Dispose();
        _historyPanel = null;
        _terminalPanel?.ApplyAppearance(_settings);
        ApplyAppearance();
        if (reopenGitPanel)
        {
            ShowGitChangesPanel();
        }
        else if (reopenHistoryPanel)
        {
            ShowHistoryPanel();
        }

        SetStatus(UiText.SettingsApplied);
    }

    private void ApplyAppearance()
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, dark);
        foreach (nint control in new[]
        {
            _openFolderButton,
            _cloneButton,
            _recentWorkspacesButton,
            _refreshButton,
            _quickOpenButton,
            _searchButton,
            _settingsButton,
            _filesButton,
            _gitButton,
            _historyButton,
            _terminalButton,
            _fileTree,
            _documentTabs,
            _emptyDocumentLabel,
            _workspaceLabel,
            _statusBar,
        })
        {
            NativeTheme.ApplyToControl(control, dark);
        }

        foreach (DocumentTabState document in _documents)
        {
            document.View.ApplyAppearance(_settings);
        }

        _gitPanel?.ApplyAppearance();
        _historyPanel?.ApplyAppearance();
        _terminalPanel?.ApplyAppearance(_settings);

        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private bool HandleShortcut(NativeMethods.Message message)
    {
        if (message.MessageId != NativeMethods.WindowMessageKeyDown)
        {
            return false;
        }

        if (_terminalPanel?.ContainsWindow(message.Window) == true)
        {
            return false;
        }

        int key = unchecked((int)message.WordParameter);
        bool control = NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) < 0;
        bool shift = NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0;
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
        Post(() => _ = HandleWorkspaceFilesChangedAsync(eventArgs.Paths));
    }

    private async Task HandleWorkspaceFilesChangedAsync(IReadOnlyList<string> paths)
    {
        if (_workspaceRoot is null)
        {
            return;
        }

        _gitPanel?.RequestRefresh();
        _historyPanel?.RequestRefresh();

        HashSet<string> changed = paths.Where(path => !string.IsNullOrEmpty(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] expanded = _treeNodes.Values
            .Where(node => node.IsExpanded && node.IsDirectory)
            .Select(node => node.FullPath)
            .ToArray();
        string? selectedPath = GetSelectedTreeNode()?.FullPath;
        string? firstVisiblePath = GetFirstVisibleTreeNode()?.FullPath;
        foreach (DocumentTabState document in _documents.ToArray())
        {
            if (paths.Count == 0 || changed.Contains(document.Path))
            {
                Augit.Core.Documents.DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(
                    _workspaceRoot,
                    document.Path);
                await document.View.ReloadAsync(result);
            }
        }

        string[] parents = changed
            .Select(Path.GetDirectoryName)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string parentPath in parents.OrderByDescending(path => path.Length))
        {
            TreeNodeState? node = _treeNodes.Values.FirstOrDefault(
                candidate => candidate.IsLoaded && candidate.FullPath.Equals(parentPath, StringComparison.OrdinalIgnoreCase));
            if (node is not null)
            {
                await LoadTreeNodeAsync(node, force: true);
            }
        }

        foreach (string expandedPath in expanded.OrderBy(path => path.Length))
        {
            await ExpandTreePathAsync(expandedPath);
        }
        RestoreTreePosition(selectedPath, firstVisiblePath);

        SetStatus(UiText.ExternalChangesSynchronized);
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
        if (_statusBar != 0)
        {
            _ = NativeMethods.SendMessage(_statusBar, NativeMethods.StatusBarSetText, 0, text);
        }
    }

    private void OnDestroyed(nint window)
    {
        CaptureWindowPlacement(window);
        SaveSettings();
        ReleaseWorkspaceResources();
        lock (InstancesGate)
        {
            Instances.Remove(window);
        }

        _handle = 0;
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
                Width = width,
                Height = height,
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
            OpenFiles = _documents.Select(document => document.Path).Take(50).ToArray(),
            ExpandedDirectories = _treeNodes.Values
                .Where(node => node.IsDirectory && node.IsExpanded)
                .Select(node => node.FullPath)
                .Take(50)
                .ToArray(),
        };
        _settingsStore.SaveAsync(_settings).GetAwaiter().GetResult();
    }

    private void ReleaseWorkspaceResources()
    {
        _terminalPanel?.Dispose();
        _terminalPanel = null;
        CloseSearch();
        _gitPanel?.Dispose();
        _gitPanel = null;
        _historyPanel?.Dispose();
        _historyPanel = null;
        _showingGitPanel = false;
        _showingHistoryPanel = false;
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

        if (_instanceCoordinator is not null)
        {
            _instanceCoordinator.ActivationRequested -= OnActivationRequested;
            _instanceCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _instanceCoordinator = null;
        }

        foreach (DocumentTabState document in _documents)
        {
            document.View.Dispose();
        }

        _documents.Clear();
        _activeDocumentIndex = -1;
        _treeNodes.Clear();
        _treeGeneration++;
    }

    private static int ToFiniteDimension(double value, int fallback)
    {
        return double.IsFinite(value) && value > 0 ? (int)Math.Round(value) : fallback;
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

    private sealed record DocumentTabState(string Path, NativeDocumentView View);

    private sealed class WindowSynchronizationContext(MainWindow window) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            window.Post(() => callback(state));
        }
    }
}
