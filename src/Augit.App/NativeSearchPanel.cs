using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Search;
using Augit.Infrastructure.Search;

namespace Augit.App;

internal sealed partial class NativeSearchPanel : IDisposable
{
    private const string WindowClassName = "Augit.SearchPanel.Native";
    private const int SearchEditIdentifier = 1;
    private const int ResultsIdentifier = 2;
    private const int CommandClose = 3;
    private const int CommandMatchCase = 4;
    private const int CommandWholeWord = 5;
    private const int CommandRegularExpression = 6;
    private const int CommandIncludeIgnored = 7;
    private const int PanelCornerRadius = 9;
    private const int SearchFieldBorder = 2;
    private const int EditNotificationChanged = 0x0300;
    private const nuint SearchEditSubclassIdentifier = 1;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeSearchPanel> Instances = [];
    private static readonly Dictionary<nint, NativeSearchPanel> SearchEditInstances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly NativeMethods.SubclassProcedure SearchEditProcedure = HandleSearchEditMessage;
    private static bool _classRegistered;
    private readonly nint _parent;
    private readonly string _workspaceRoot;
    private readonly WorkspaceSearchMode _mode;
    private readonly RipgrepSearchService _searchService;
    private readonly Action<string, int?> _previewResult;
    private readonly Action<string, int?> _openResult;
    private readonly Action _close;
    private readonly Action<string> _setStatus;
    private readonly Action _requestParentLayout;
    private readonly Func<bool> _isDark;
    private readonly List<SearchResultEntry> _results = [];
    private nint _titleLabel;
    private nint _shortcutLabel;
    private nint _searchEdit;
    private nint _matchCaseButton;
    private nint _wholeWordButton;
    private nint _regularExpressionButton;
    private nint _includeIgnoredButton;
    private nint _closeButton;
    private nint _resultList;
    private nint _noticeLabel;
    private nint _controlBrush;
    private NativeToolTip? _toolTip;
    private NativeDialogActionButtons? _optionStates;
    private int _searchVersion;
    private int _completedSearchVersion;
    private int _resultListDeltaCount;
    private bool _suppressSelectionPreview;
    private bool _roundedRegionApplied;
    private bool _matchCase;
    private bool _wholeWord;
    private bool _regularExpression;
    private bool _includeIgnored;
    private bool _disposed;

    internal NativeSearchPanel(
        nint parent,
        string workspaceRoot,
        WorkspaceSearchMode mode,
        Action<string, int?> previewResult,
        Action<string, int?> openResult,
        Action close,
        Action<string> setStatus,
        Action requestParentLayout,
        Func<bool> isDark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(previewResult);
        ArgumentNullException.ThrowIfNull(openResult);
        ArgumentNullException.ThrowIfNull(close);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(requestParentLayout);
        ArgumentNullException.ThrowIfNull(isDark);
        _parent = parent;
        _workspaceRoot = workspaceRoot;
        _mode = mode;
        _previewResult = previewResult;
        _openResult = openResult;
        _close = close;
        _setStatus = setStatus;
        _requestParentLayout = requestParentLayout;
        _isDark = isDark;
        _searchService = new(Path.Combine(AppContext.BaseDirectory, "tools", "rg.exe"));
        EnsureWindowClass();
        Handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStylePopup
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleClipChildren,
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.SearchPanelCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(Handle, this);
        }

        CreateControls();
        Layout();
        _ = NativeMethods.SetFocus(_searchEdit);
        QueueSearch(immediate: true);
    }

    internal nint Handle { get; private set; }

    internal WorkspaceSearchMode Mode => _mode;

    internal int ResultCount => _results.Count;

    internal int LogicalWidthForTest
    {
        get
        {
            if (Handle == 0 || !NativeMethods.GetWindowRectangle(Handle, out NativeMethods.Rectangle rectangle))
            {
                return 0;
            }

            return (int)Math.Round(NativeTheme.Unscale(rectangle.Right - rectangle.Left));
        }
    }

    internal bool ShortcutVisibleForTest => _mode == WorkspaceSearchMode.FileNames
        && NativeMethods.IsWindowVisible(_shortcutLabel);

    internal bool ResultListHasFocusForTest => NativeMethods.GetFocus() == _resultList;

    internal bool CloseButtonVisibleForTest => NativeMethods.IsWindowVisible(_closeButton);

    internal bool NoticeVisibleForTest => NativeMethods.IsWindowVisible(_noticeLabel);

    internal bool UsesSingleLineResultsForTest => _mode == WorkspaceSearchMode.FileNames;

    internal string PlaceholderForTest => _mode == WorkspaceSearchMode.FileNames
        ? UiText.QuickOpenPlaceholder
        : string.Empty;

    internal bool UsesFocusedSearchFieldChromeForTest
    {
        get
        {
            uint style = unchecked((uint)NativeMethods.GetWindowLongPointer(
                _searchEdit,
                NativeMethods.WindowLongStyle));
            return (style & NativeMethods.WindowStyleBorder) == 0
                && NativeMethods.GetFocus() == _searchEdit;
        }
    }

    internal int PreferredHeightForTest => PreferredHeight(
        NativeMethods.GetClientRectangle(Handle, out var bounds) && bounds.Right > 0
            ? bounds.Right : NativeTheme.Scale(730));

    private bool HasCurrentResults => !_disposed && _searchVersion > 0 && _completedSearchVersion == _searchVersion;

    private bool HasNotice => _mode == WorkspaceSearchMode.Text
        && !string.IsNullOrWhiteSpace(NativeMethods.GetWindowTextValue(_noticeLabel));

    internal bool NoticeWithinBoundsForTest => NativeMethods.GetWindowRectangle(Handle, out var panel)
        && NativeMethods.GetWindowRectangle(_noticeLabel, out var notice)
        && notice.Left >= panel.Left && notice.Right <= panel.Right
        && notice.Top >= panel.Top && notice.Bottom <= panel.Bottom;

    internal bool SearchCompletedForTest => HasCurrentResults;

    internal int ResultListDeltaCountForTest => _resultListDeltaCount;

    internal bool ContainsWindow(nint window)
    {
        return NativeFocusNavigation.ContainsWindow(Handle, window);
    }

    internal bool HandleTabNavigation(bool backwards)
    {
        if (_searchComposing) return false;
        return NativeFocusNavigation.MoveWithinRegion(
            [
                _searchEdit,
                _matchCaseButton,
                _wholeWordButton,
                _regularExpressionButton,
                _includeIgnoredButton,
                _resultList,
                _noticeLabel,
            ],
            NativeMethods.GetFocus(),
            backwards);
    }

    internal bool UsesRoundedChromeForTest => _roundedRegionApplied
        && (unchecked((uint)NativeMethods.GetWindowLongPointer(
            Handle,
            NativeMethods.WindowLongStyle)) & NativeMethods.WindowStyleBorder) == 0;

    internal bool OpenFirstResultWithEnterForTest()
    {
        if (_results.Count == 0)
        {
            return false;
        }

        _ = NativeMethods.SetFocus(_searchEdit);
        return NativeMethods.PostMessage(
            _searchEdit,
            NativeMethods.WindowMessageKeyDown,
            NativeMethods.VirtualKeyEnter,
            0);
    }

    internal bool PreviewFirstResultWithClickForTest()
    {
        return PreviewResultWithClickForTest(0);
    }

    internal bool PreviewResultWithClickForTest(int index)
    {
        if (index < 0 || index >= _results.Count)
        {
            return false;
        }

        NativeMethods.Rectangle rectangle = default;
        if (NativeMethods.SendMessage(
                _resultList,
                NativeMethods.ListBoxGetItemRectangle,
                unchecked((nuint)index),
                ref rectangle) == unchecked((nint)(-1)))
        {
            return false;
        }

        _ = NativeMethods.SendMessage(
            _resultList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)(-1)),
            0);
        int x = rectangle.Left + NativeTheme.Scale(24);
        int y = (rectangle.Top + rectangle.Bottom) / 2;
        nint point = unchecked((nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF)));
        _ = NativeMethods.SetFocus(_resultList);
        _ = NativeMethods.SendMessage(
            _resultList,
            NativeMethods.WindowMessageLeftButtonDown,
            1,
            point);
        _ = NativeMethods.SendMessage(
            _resultList,
            NativeMethods.WindowMessageLeftButtonUp,
            0,
            point);
        return true;
    }

    internal string? ResultPathForTest(int index)
    {
        return index >= 0 && index < _results.Count
            ? Path.Combine(_workspaceRoot, _results[index].RelativePath)
            : null;
    }

    internal void FocusResultList()
    {
        _ = NativeMethods.SetFocus(_resultList);
    }

    internal bool HandleShortcut(NativeMethods.Message message)
    {
        if (_disposed || _searchComposing || message.MessageId != NativeMethods.WindowMessageKeyDown)
        {
            return false;
        }

        int key = unchecked((int)message.WordParameter);
        if (key == NativeMethods.VirtualKeyEnter
            && message.Window is var window
            && (window == _searchEdit || window == _resultList))
        {
            SelectFirstResultIfNeeded();
            OpenSelectedResult();
            return true;
        }

        if (key == NativeMethods.VirtualKeyDown && message.Window == _searchEdit && _results.Count > 0)
        {
            SelectFirstResultIfNeeded();
            _ = NativeMethods.SetFocus(_resultList);
            return true;
        }

        if (key == NativeMethods.VirtualKeyUp && message.Window == _resultList)
        {
            int index = GetSelectedResultIndex();
            if (index <= 0)
            {
                _ = NativeMethods.SetFocus(_searchEdit);
                return true;
            }
        }

        return false;
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        int safeWidth = Math.Max(0, width);
        int safeHeight = Math.Max(0, height);
        NativeMethods.Point screenPoint = new() { X = x, Y = y };
        if (!NativeMethods.ClientToScreen(_parent, ref screenPoint))
        {
            screenPoint.X = x;
            screenPoint.Y = y;
        }

        _ = NativeMethods.MoveWindow(Handle, screenPoint.X, screenPoint.Y, safeWidth, safeHeight, true);
        ApplyRoundedRegion(safeWidth, safeHeight);
        Layout();
    }

    internal void FocusSearchBox()
    {
        _ = NativeMethods.SetFocus(_searchEdit);
    }

    internal void SetQueryForTest(string query)
    {
        _ = NativeMethods.SetWindowText(_searchEdit, query);
        QueueSearch(immediate: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelSearchWork();
        _optionStates?.Dispose();
        _toolTip?.Dispose();
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }
        nint handle = Handle;
        Handle = 0;
        if (handle != 0)
        {
            lock (InstancesGate)
            {
                Instances.Remove(handle);
            }

            if (_searchEdit != 0)
            {
                _ = NativeMethods.RemoveWindowSubclass(
                    _searchEdit,
                    SearchEditProcedure,
                    SearchEditSubclassIdentifier);
                lock (InstancesGate)
                {
                    SearchEditInstances.Remove(_searchEdit);
                }
            }

            if (NativeMethods.IsWindow(handle))
            {
                _ = NativeMethods.DestroyWindow(handle);
            }
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
                Style = NativeMethods.ClassRedrawOnHorizontalChange
                    | NativeMethods.ClassRedrawOnVerticalChange
                    | NativeMethods.ClassDropShadow,
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
                throw new Win32Exception(error, UiText.SearchPanelClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeSearchPanel? instance;
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
            case SearchDispatchMessage:
                instance.DrainSearchActions();
                return 0;
            case NativeMethods.WindowMessageDestroy:
                instance.CancelSearchWork();
                break;
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
            case NativeMethods.WindowMessageSetFocus:
                _ = NativeMethods.SetFocus(instance._searchEdit);
                return 0;
            case NativeMethods.WindowMessagePaint:
                instance.Paint();
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessageDrawItem:
                return instance.DrawItem(longParameter) ? 1 : 0;
            case NativeMethods.WindowMessageControlColorEdit:
            case NativeMethods.WindowMessageControlColorListBox:
            case NativeMethods.WindowMessageControlColorStatic:
                return instance.ApplyControlColor(unchecked((nint)wordParameter), longParameter);
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private static nint HandleSearchEditMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        NativeSearchPanel? instance;
        lock (InstancesGate)
        {
            SearchEditInstances.TryGetValue(window, out instance);
        }

        if (instance is not null && message == 0x010D)
        {
            instance._searchComposing = true;
            instance.CancelSearchWork();
        }

        nint result = NativeMethods.DefaultSubclassProcedure(
            window,
            message,
            wordParameter,
            longParameter);
        if (instance is not null && message == 0x010E)
        {
            instance._searchComposing = false;
            instance.QueueSearch(immediate: false);
        }
        if (instance is not null
            && message == NativeMethods.WindowMessagePaint
            && instance.ShouldDrawPlaceholder())
        {
            instance.PaintPlaceholder();
        }

        if (instance is not null
            && message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
        {
            _ = NativeMethods.InvalidateRectangle(instance.Handle, 0, false);
        }

        return result;
    }

    private void CreateControls()
    {
        _titleLabel = CreateControl(
            NativeMethods.StaticClass,
            _mode == WorkspaceSearchMode.FileNames ? UiText.QuickOpenFiles : UiText.WorkspaceTextSearch,
            0,
            NativeMethods.StaticLeft);
        _shortcutLabel = CreateControl(
            NativeMethods.StaticClass,
            _mode == WorkspaceSearchMode.FileNames ? "Ctrl+P" : string.Empty,
            0,
            NativeMethods.StaticRight);
        _searchEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            SearchEditIdentifier,
            NativeMethods.EditAutoHorizontalScroll);
        _matchCaseButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.MatchCase,
            CommandMatchCase,
            NativeMethods.ButtonOwnerDraw);
        _wholeWordButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.MatchWholeWord,
            CommandWholeWord,
            NativeMethods.ButtonOwnerDraw);
        _regularExpressionButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.RegularExpression,
            CommandRegularExpression,
            NativeMethods.ButtonOwnerDraw);
        _includeIgnoredButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.IncludeIgnoredFiles,
            CommandIncludeIgnored,
            NativeMethods.ButtonOwnerDraw);
        _closeButton = CreateControl(NativeMethods.ButtonClass, UiText.CloseSymbol, CommandClose, NativeMethods.ButtonPushButton);
        _resultList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            ResultsIdentifier,
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight);
        _ = NativeMethods.SendMessage(
            _resultList,
            NativeMethods.ListBoxSetItemHeight,
            0,
            ResultRowHeight);
        // 状态是只读说明；长原因换行，受宿主高度限制时可在原区域滚动阅读。
        _noticeLabel = CreateControl(NativeMethods.EditClass, string.Empty, 8,
            NativeMethods.EditMultiline | NativeMethods.EditReadOnly | NativeMethods.EditAutoVerticalScroll
                | NativeMethods.WindowStyleVerticalScroll);
        bool textMode = _mode == WorkspaceSearchMode.Text;
        foreach (nint option in new[] { _matchCaseButton, _wholeWordButton, _regularExpressionButton, _includeIgnoredButton })
        {
            _ = NativeMethods.ShowWindow(option, textMode ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }

        bool dark = _isDark();
        NativeTheme.ApplyToWindow(Handle, dark);
        foreach (nint control in new[]
        {
            _titleLabel,
            _shortcutLabel,
            _searchEdit,
            _matchCaseButton,
            _wholeWordButton,
            _regularExpressionButton,
            _includeIgnoredButton,
            _closeButton,
            _resultList,
            _noticeLabel,
        })
        {
            NativeTheme.ApplyToControl(control, dark);
        }
        _toolTip = new(Handle);
        foreach (nint option in new[] { _matchCaseButton, _wholeWordButton, _regularExpressionButton, _includeIgnoredButton })
            _toolTip.Add(option, NativeMethods.GetWindowTextValue(option));
        _toolTip.ApplyAppearance(dark);
        _optionStates = new(Handle, _matchCaseButton, _wholeWordButton, _regularExpressionButton, _includeIgnoredButton);
        _ = NativeAccessibility.SetName(_searchEdit, "搜索内容");
        _ = NativeAccessibility.SetName(_noticeLabel, "搜索状态，可滚动阅读");

        // 两种搜索都需要组词状态与输入焦点处理。
        {
            if (!NativeMethods.SetWindowSubclass(
                _searchEdit,
                SearchEditProcedure,
                SearchEditSubclassIdentifier,
                0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    UiText.SearchControlSubclassFailed);
            }

            lock (InstancesGate)
            {
                SearchEditInstances[_searchEdit] = this;
            }
        }
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.SearchControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return control;
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == SearchEditIdentifier && notification == EditNotificationChanged)
        {
            _ = NativeMethods.InvalidateRectangle(_searchEdit, 0, true);
            QueueSearch(immediate: false);
            return;
        }

        if (command == ResultsIdentifier
            && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            if (!_suppressSelectionPreview)
            {
                PreviewSelectedResult();
            }
            return;
        }

        if (command == ResultsIdentifier && notification == NativeMethods.ListBoxNotificationDoubleClick)
        {
            OpenSelectedResult();
            return;
        }

        if (command == CommandClose)
        {
            _close();
            return;
        }

        if (command is CommandMatchCase or CommandWholeWord or CommandRegularExpression or CommandIncludeIgnored)
        {
            ToggleOption(command);
            QueueSearch(immediate: true);
        }
    }

    private void ToggleOption(int command)
    {
        nint handle = command switch
        {
            CommandMatchCase => _matchCaseButton,
            CommandWholeWord => _wholeWordButton,
            CommandRegularExpression => _regularExpressionButton,
            CommandIncludeIgnored => _includeIgnoredButton,
            _ => 0,
        };
        if (handle == 0)
        {
            return;
        }

        switch (command)
        {
            case CommandMatchCase:
                _matchCase = !_matchCase;
                break;
            case CommandWholeWord:
                _wholeWord = !_wholeWord;
                break;
            case CommandRegularExpression:
                _regularExpression = !_regularExpression;
                break;
            case CommandIncludeIgnored:
                _includeIgnored = !_includeIgnored;
                break;
        }
        _ = NativeMethods.InvalidateRectangle(handle, 0, true);
    }

    private void ShowResults(IReadOnlyList<SearchResultEntry> results, string? notice)
    {
        int selectedIndex = GetSelectedResultIndex();
        SearchResultIdentity? selected = selectedIndex >= 0 && selectedIndex < _results.Count
            ? SearchResultIdentity.From(_results[selectedIndex])
            : null;
        int topIndex = checked((int)NativeMethods.SendMessage(
            _resultList,
            NativeMethods.ListBoxGetTopIndex,
            0,
            0));
        SearchResultIdentity? top = topIndex >= 0 && topIndex < _results.Count
            ? SearchResultIdentity.From(_results[topIndex])
            : null;
        (int prefix, int removedCount, int addedCount) = ComputeResultDelta(_results, results);
        bool resultsChanged = removedCount > 0 || addedCount > 0;

        if (resultsChanged)
        {
            _suppressSelectionPreview = true;
            _ = NativeMethods.SendMessage(_resultList, NativeMethods.WindowMessageSetRedraw, 0, 0);
            try
            {
                for (int index = removedCount - 1; index >= 0; index--)
                {
                    _ = NativeMethods.SendMessage(
                        _resultList,
                        NativeMethods.ListBoxDeleteString,
                        unchecked((nuint)(prefix + index)),
                        0);
                }

                for (int index = 0; index < addedCount; index++)
                {
                    SearchResultEntry result = results[prefix + index];
                    _ = NativeMethods.SendMessage(
                        _resultList,
                        NativeMethods.ListBoxInsertString,
                        unchecked((nuint)(prefix + index)),
                        result.DisplayText);
                }

                _results.RemoveRange(prefix, removedCount);
                _results.InsertRange(prefix, results.Skip(prefix).Take(addedCount));
                _resultListDeltaCount++;

                int restoredSelection = FindResultIndex(_results, selected);
                if (restoredSelection < 0 && _results.Count > 0)
                {
                    restoredSelection = 0;
                }

                _ = NativeMethods.SendMessage(
                    _resultList,
                    NativeMethods.ListBoxSetCurrentSelection,
                    restoredSelection < 0 ? unchecked((nuint)(-1)) : unchecked((nuint)restoredSelection),
                    0);

                int restoredTop = FindResultIndex(_results, top);
                if (restoredTop < 0 && _results.Count > 0)
                {
                    restoredTop = Math.Clamp(topIndex, 0, _results.Count - 1);
                }
                if (restoredTop >= 0)
                {
                    _ = NativeMethods.SendMessage(
                        _resultList,
                        NativeMethods.ListBoxSetTopIndex,
                        unchecked((nuint)restoredTop),
                        0);
                }
            }
            finally
            {
                _ = NativeMethods.SendMessage(_resultList, NativeMethods.WindowMessageSetRedraw, 1, 0);
                _suppressSelectionPreview = false;
            }

            _ = NativeMethods.InvalidateRectangle(_resultList, 0, true);
        }

        string nextNotice = notice ?? string.Empty;
        bool noticeChanged = !string.Equals(
            NativeMethods.GetWindowTextValue(_noticeLabel),
            nextNotice,
            StringComparison.Ordinal);
        if (noticeChanged)
        {
            _ = NativeMethods.SetWindowText(_noticeLabel, nextNotice);
        }
        if (resultsChanged || noticeChanged)
        {
            _requestParentLayout();
        }
    }

    internal static (int Prefix, int RemovedCount, int AddedCount) ResultDeltaForTest(
        IReadOnlyList<string> previous,
        IReadOnlyList<string> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        SearchResultEntry[] oldEntries = previous
            .Select(path => new SearchResultEntry(path, null, path, null))
            .ToArray();
        SearchResultEntry[] newEntries = current
            .Select(path => new SearchResultEntry(path, null, path, null))
            .ToArray();
        return ComputeResultDelta(oldEntries, newEntries);
    }

    private static (int Prefix, int RemovedCount, int AddedCount) ComputeResultDelta(
        IReadOnlyList<SearchResultEntry> previous,
        IReadOnlyList<SearchResultEntry> current)
    {
        int prefix = 0;
        while (prefix < previous.Count
            && prefix < current.Count
            && SearchResultEqual(previous[prefix], current[prefix]))
        {
            prefix++;
        }

        int previousSuffix = previous.Count - 1;
        int currentSuffix = current.Count - 1;
        while (previousSuffix >= prefix
            && currentSuffix >= prefix
            && SearchResultEqual(previous[previousSuffix], current[currentSuffix]))
        {
            previousSuffix--;
            currentSuffix--;
        }

        return (
            prefix,
            previousSuffix - prefix + 1,
            currentSuffix - prefix + 1);
    }

    private static bool SearchResultEqual(SearchResultEntry left, SearchResultEntry right)
    {
        return left.RelativePath.Equals(right.RelativePath, StringComparison.OrdinalIgnoreCase)
            && left.LineNumber == right.LineNumber
            && string.Equals(left.DisplayText, right.DisplayText, StringComparison.Ordinal)
            && string.Equals(left.MatchText, right.MatchText, StringComparison.Ordinal);
    }

    private static int FindResultIndex(
        IReadOnlyList<SearchResultEntry> results,
        SearchResultIdentity? identity)
    {
        if (identity is null)
        {
            return -1;
        }

        for (int index = 0; index < results.Count; index++)
        {
            SearchResultIdentity candidate = SearchResultIdentity.From(results[index]);
            if (identity.Value.LineNumber == candidate.LineNumber
                && identity.Value.RelativePath.Equals(
                    candidate.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void OpenSelectedResult()
    {
        if (_searchComposing || !HasCurrentResults) return;
        int index = GetSelectedResultIndex();
        if (index < 0 || index >= _results.Count)
        {
            return;
        }

        SearchResultEntry result = _results[index];
        _openResult(Path.Combine(_workspaceRoot, result.RelativePath), result.LineNumber);
        _close();
    }

    private void PreviewSelectedResult()
    {
        if (_searchComposing || !HasCurrentResults) return;
        int index = GetSelectedResultIndex();
        if (index < 0 || index >= _results.Count)
        {
            return;
        }

        SearchResultEntry result = _results[index];
        _previewResult(Path.Combine(_workspaceRoot, result.RelativePath), result.LineNumber);
    }

    private int GetSelectedResultIndex()
    {
        return checked((int)NativeMethods.SendMessage(
            _resultList,
            NativeMethods.ListBoxGetCurrentSelection,
            0,
            0));
    }

    private void SelectFirstResultIfNeeded()
    {
        if (_results.Count > 0 && GetSelectedResultIndex() < 0)
        {
            _ = NativeMethods.SendMessage(
                _resultList,
                NativeMethods.ListBoxSetCurrentSelection,
                0,
                0);
        }
    }

    private nint ApplyControlColor(nint deviceContext, nint control)
    {
        NativeThemePalette palette = NativeTheme.Palette(_isDark());
        if (_controlBrush == 0)
        {
            _controlBrush = NativeMethods.CreateSolidBrush(palette.Panel);
        }

        _ = NativeMethods.SetTextColor(deviceContext, control == _noticeLabel || control == _shortcutLabel ? palette.Muted : palette.Text);
        _ = NativeMethods.SetBackgroundColor(deviceContext, palette.Panel);
        return _controlBrush;
    }

    private void Paint()
    {
        nint deviceContext = NativeMethods.BeginPaint(Handle, out NativeMethods.PaintStructure paint);
        if (deviceContext == 0)
        {
            return;
        }

        try
        {
            NativeThemePalette palette = NativeTheme.Palette(_isDark());
            if (NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle rectangle))
            {
                int radius = NativeTheme.Scale(PanelCornerRadius);
                FillRounded(deviceContext, rectangle, palette.BorderStrong, radius);
                NativeMethods.Rectangle inner = rectangle;
                int border = Math.Max(1, NativeTheme.Scale(1));
                inner.Left += border;
                inner.Top += border;
                inner.Right -= border;
                inner.Bottom -= border;
                FillRounded(deviceContext, inner, palette.Panel, Math.Max(1, radius - border));
                PaintSearchFieldChrome(deviceContext, rectangle.Right - rectangle.Left, palette);
            }
        }
        finally
        {
            _ = NativeMethods.EndPaint(Handle, ref paint);
        }
    }

    private bool ShouldDrawPlaceholder()
    {
        return !_disposed
            && _mode == WorkspaceSearchMode.FileNames
            && NativeMethods.GetWindowTextValue(_searchEdit).Length == 0;
    }

    private void PaintSearchFieldChrome(
        nint deviceContext,
        int panelWidth,
        NativeThemePalette palette)
    {
        int inset = NativeTheme.Scale(10);
        NativeMethods.Rectangle field = new()
        {
            Left = inset,
            Top = MeasureLayout(panelWidth).FieldTop,
            Right = Math.Max(inset, panelWidth - inset),
            Bottom = MeasureLayout(panelWidth).FieldTop + SearchFieldHeight,
        };
        uint border = NativeMethods.GetFocus() == _searchEdit
            ? palette.Accent
            : palette.BorderStrong;
        FillRounded(deviceContext, field, border, NativeTheme.Scale(5));
        int borderWidth = NativeTheme.Scale(SearchFieldBorder);
        field.Left += borderWidth;
        field.Top += borderWidth;
        field.Right -= borderWidth;
        field.Bottom -= borderWidth;
        FillRounded(deviceContext, field, palette.Panel, NativeTheme.Scale(3));
    }

    private void PaintPlaceholder()
    {
        if (!ShouldDrawPlaceholder()
            || !NativeMethods.GetClientRectangle(_searchEdit, out NativeMethods.Rectangle client))
        {
            return;
        }

        nint deviceContext = NativeMethods.GetDeviceContext(_searchEdit);
        if (deviceContext == 0)
        {
            return;
        }

        try
        {
            NativeMethods.Rectangle textRectangle = new()
            {
                Left = 0,
                Top = 0,
                Right = client.Right,
                Bottom = client.Bottom,
            };
            nint previousFont = NativeMethods.SelectObject(deviceContext, NativeTheme.UiFont);
            _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
            _ = NativeMethods.SetTextColor(deviceContext, NativeTheme.Palette(_isDark()).Muted);
            _ = NativeMethods.DrawText(
                deviceContext,
                UiText.QuickOpenPlaceholder,
                UiText.QuickOpenPlaceholder.Length,
                ref textRectangle,
                NativeMethods.DrawTextVerticalCenter
                    | NativeMethods.DrawTextSingleLine
                    | NativeMethods.DrawTextNoPrefix
                    | NativeMethods.DrawTextEndEllipsis);
            if (previousFont != 0)
            {
                _ = NativeMethods.SelectObject(deviceContext, previousFont);
            }
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(_searchEdit, deviceContext);
        }
    }

    private bool DrawItem(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        if (item.ControlIdentifier is CommandMatchCase or CommandWholeWord or CommandRegularExpression or CommandIncludeIgnored)
        {
            return DrawOptionItem(item);
        }
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= _results.Count)
        {
            return true;
        }

        bool dark = _isDark();
        NativeThemePalette palette = NativeTheme.Palette(dark);
        bool selected = (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        if (selected)
        {
            NativeMethods.Rectangle selection = item.ItemRectangle;
            selection.Left += NativeTheme.Scale(2);
            selection.Right -= NativeTheme.Scale(2);
            FillRounded(item.DeviceContext, selection, palette.AccentSoft, NativeTheme.Scale(5));
        }
        SearchResultEntry result = _results[index];
        string fileName = Path.GetFileName(result.RelativePath);
        string directory = Path.GetDirectoryName(result.RelativePath)?.Replace('\\', '/') ?? string.Empty;
        NativeMethods.Rectangle typeRectangle = item.ItemRectangle;
        typeRectangle.Left += NativeTheme.Scale(18);
        typeRectangle.Right = typeRectangle.Left + NativeTheme.Scale(16);
        _ = NativeTheme.DrawFileTypeIcon(item.DeviceContext, typeRectangle, result.RelativePath, dark);

        int contentLeft = item.ItemRectangle.Left + NativeTheme.Scale(42);
        int contentRight = item.ItemRectangle.Right - NativeTheme.Scale(10);
        int directoryWidth = Math.Min(MeasureTextWidth(item.DeviceContext, directory, NativeTheme.UiFont),
            Math.Max(0, contentRight - contentLeft) / 3);
        int directoryLeft = Math.Max(contentLeft, contentRight - directoryWidth);
        NativeMethods.Rectangle pathRectangle = item.ItemRectangle;
        pathRectangle.Left = directoryLeft;
        pathRectangle.Right = contentRight;
        DrawTextRight(item.DeviceContext, directory, pathRectangle, palette.Muted, NativeTheme.UiFont);

        int flexibleRight = Math.Max(contentLeft, directoryLeft - NativeTheme.Scale(10));
        NativeMethods.Rectangle fileRectangle = item.ItemRectangle;
        fileRectangle.Left = contentLeft;
        fileRectangle.Right = flexibleRight;

        if (_mode == WorkspaceSearchMode.Text && result.LineNumber is { } lineNumber)
        {
            int fileWidth = Math.Min(
                Math.Max(0, flexibleRight - contentLeft) / 2,
                MeasureTextWidth(item.DeviceContext, fileName, NativeTheme.UiFont) + NativeTheme.Scale(8));
            fileRectangle.Right = Math.Min(flexibleRight, contentLeft + fileWidth);
            DrawText(item.DeviceContext, fileName, fileRectangle, palette.Text, NativeTheme.UiFont);

            NativeMethods.Rectangle matchRectangle = item.ItemRectangle;
            matchRectangle.Left = Math.Min(
                flexibleRight,
                fileRectangle.Right + NativeTheme.Scale(8));
            matchRectangle.Right = flexibleRight;
            string match = result.MatchText is { Length: > 0 } text
                ? $"{lineNumber}: {text}"
                : lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DrawText(item.DeviceContext, match, matchRectangle, palette.Muted, NativeTheme.UiFont);
        }
        else
        {
            DrawText(item.DeviceContext, fileName, fileRectangle, palette.Text, NativeTheme.UiFont);
        }
        return true;
    }

    private bool DrawOptionItem(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_isDark());
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
        bool hot = !disabled && (_optionStates?.Hovered == item.Control
            || (item.ItemState & (NativeMethods.OwnerDrawHotLight | NativeMethods.OwnerDrawSelected)) != 0);
        bool focused = !disabled && NativeMethods.GetFocus() == item.Control;
        bool compact = item.ControlIdentifier is CommandMatchCase or CommandWholeWord or CommandRegularExpression;
        bool selected = !disabled && compact && IsOptionChecked(unchecked((int)item.ControlIdentifier));
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        if (hot || selected)
        {
            FillRounded(item.DeviceContext, item.ItemRectangle, selected ? palette.AccentSoft : palette.Hover, NativeTheme.Scale(4));
        }

        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        if (compact)
        {
            NativeFindOptionIcon icon = item.ControlIdentifier switch
            {
                CommandMatchCase => NativeFindOptionIcon.MatchCase,
                CommandWholeWord => NativeFindOptionIcon.WholeWord,
                _ => NativeFindOptionIcon.RegularExpression,
            };
            _ = NativeTheme.DrawFindOptionIcon(item.DeviceContext, item.ItemRectangle, icon,
                disabled ? palette.Faint : selected ? palette.Accent : palette.Text);
        }
        else
        {
            NativeCheckboxState state = IsOptionChecked(unchecked((int)item.ControlIdentifier))
                ? NativeCheckboxState.Checked
                : NativeCheckboxState.Unchecked;
            _ = NativeTheme.DrawCheckbox(
                item.DeviceContext,
                item.ItemRectangle.Left + NativeTheme.Scale(2),
                centerY,
                state,
                _isDark(),
                enabled: !disabled);
            NativeMethods.Rectangle text = item.ItemRectangle;
            text.Left += NativeTheme.Scale(22);
            DrawText(
                item.DeviceContext,
                NativeMethods.GetWindowTextValue(item.Control),
                text,
                disabled ? palette.Muted : palette.Text,
                NativeTheme.UiFont);
        }
        if (focused)
        {
            DrawFocusRectangle(item.DeviceContext, item.ItemRectangle, palette.Accent);
        }
        return true;
    }

    private static void DrawFocusRectangle(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, NativeTheme.Scale(1)), color);
        if (pen == 0) return;
        nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(deviceContext, NativeMethods.GetStockObject(NativeMethods.NullBrush));
        _ = NativeMethods.DrawRectangle(
            deviceContext,
            rectangle.Left + NativeTheme.Scale(1),
            rectangle.Top + NativeTheme.Scale(1),
            rectangle.Right - NativeTheme.Scale(1),
            rectangle.Bottom - NativeTheme.Scale(1));
        if (previousBrush != 0) _ = NativeMethods.SelectObject(deviceContext, previousBrush);
        if (previousPen != 0) _ = NativeMethods.SelectObject(deviceContext, previousPen);
        _ = NativeMethods.DeleteObject(pen);
    }

    private bool IsOptionChecked(int control)
    {
        return control switch
        {
            CommandMatchCase => _matchCase,
            CommandWholeWord => _wholeWord,
            CommandRegularExpression => _regularExpression,
            CommandIncludeIgnored => _includeIgnored,
            _ => false,
        };
    }

    private static int MeasureTextWidth(nint deviceContext, string text, nint font)
    {
        NativeMethods.Rectangle rectangle = new() { Right = 2000, Bottom = 64 };
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextCalculateRectangle
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }

        return Math.Max(0, rectangle.Right - rectangle.Left);
    }

    private static void DrawTextRight(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextRight
            | NativeMethods.DrawTextVerticalCenter
            | NativeMethods.DrawTextSingleLine
            | NativeMethods.DrawTextNoPrefix
            | NativeMethods.DrawTextEndEllipsis;
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
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

    private static void FillRounded(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint color,
        int radius)
    {
        if (rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
        {
            return;
        }

        nint brush = NativeMethods.CreateSolidBrush(color);
        nint region = NativeMethods.CreateRoundRectangleRegion(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right + 1,
            rectangle.Bottom + 1,
            Math.Max(1, radius * 2),
            Math.Max(1, radius * 2));
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

    private void ApplyRoundedRegion(int width, int height)
    {
        _roundedRegionApplied = false;
        if (Handle == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        int radius = NativeTheme.Scale(PanelCornerRadius);
        nint region = NativeMethods.CreateRoundRectangleRegion(
            0,
            0,
            width + 1,
            height + 1,
            Math.Max(1, radius * 2),
            Math.Max(1, radius * 2));
        if (region == 0)
        {
            return;
        }

        _roundedRegionApplied = NativeMethods.SetWindowRegion(Handle, region, true) != 0;
        if (!_roundedRegionApplied)
        {
            _ = NativeMethods.DeleteObject(region);
        }
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextVerticalCenter
            | NativeMethods.DrawTextSingleLine
            | NativeMethods.DrawTextNoPrefix
            | NativeMethods.DrawTextEndEllipsis;
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
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
        return NativeMethods.SendMessage(button, NativeMethods.ButtonMessageGetCheck, 0, 0) == (nint)NativeMethods.ButtonChecked;
    }

    private sealed record SearchResultEntry(
        string RelativePath,
        int? LineNumber,
        string DisplayText,
        string? MatchText);

    private readonly record struct SearchResultIdentity(string RelativePath, int? LineNumber)
    {
        internal static SearchResultIdentity From(SearchResultEntry result)
        {
            return new(result.RelativePath, result.LineNumber);
        }
    }
}
