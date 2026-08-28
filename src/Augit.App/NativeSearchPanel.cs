using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Search;
using Augit.Infrastructure.Search;

namespace Augit.App;

internal sealed class NativeSearchPanel : IDisposable
{
    private const string WindowClassName = "Augit.SearchPanel.Native";
    private const int SearchEditIdentifier = 1;
    private const int ResultsIdentifier = 2;
    private const int CommandClose = 3;
    private const int CommandMatchCase = 4;
    private const int CommandWholeWord = 5;
    private const int CommandRegularExpression = 6;
    private const int CommandIncludeIgnored = 7;
    private const int EditNotificationChanged = 0x0300;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeSearchPanel> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly string _workspaceRoot;
    private readonly WorkspaceSearchMode _mode;
    private readonly RipgrepSearchService _searchService;
    private readonly Action<string, int?> _openResult;
    private readonly Action _close;
    private readonly Action<string> _setStatus;
    private readonly List<SearchResultEntry> _results = [];
    private nint _titleLabel;
    private nint _searchEdit;
    private nint _matchCaseButton;
    private nint _wholeWordButton;
    private nint _regularExpressionButton;
    private nint _includeIgnoredButton;
    private nint _closeButton;
    private nint _resultList;
    private nint _noticeLabel;
    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;
    private bool _disposed;

    internal NativeSearchPanel(
        nint parent,
        string workspaceRoot,
        WorkspaceSearchMode mode,
        Action<string, int?> openResult,
        Action close,
        Action<string> setStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(openResult);
        ArgumentNullException.ThrowIfNull(close);
        ArgumentNullException.ThrowIfNull(setStatus);
        _workspaceRoot = workspaceRoot;
        _mode = mode;
        _openResult = openResult;
        _close = close;
        _setStatus = setStatus;
        _searchService = new(Path.Combine(AppContext.BaseDirectory, "tools", "rg.exe"));
        EnsureWindowClass();
        Handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleBorder
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.SearchPanelCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(Handle, this);
        }

        CreateControls();
        Layout();
        _ = NativeMethods.SetFocus(_searchEdit);
        if (_mode == WorkspaceSearchMode.FileNames)
        {
            QueueSearch(immediate: true);
        }
    }

    internal nint Handle { get; private set; }

    internal int ResultCount => _results.Count;

    internal void SetBounds(int x, int y, int width, int height)
    {
        _ = NativeMethods.MoveWindow(Handle, x, y, Math.Max(0, width), Math.Max(0, height), true);
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
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        nint handle = Handle;
        Handle = 0;
        if (handle != 0)
        {
            lock (InstancesGate)
            {
                Instances.Remove(handle);
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
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
            case NativeMethods.WindowMessageSetFocus:
                _ = NativeMethods.SetFocus(instance._searchEdit);
                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        _titleLabel = CreateControl(
            NativeMethods.StaticClass,
            _mode == WorkspaceSearchMode.FileNames ? UiText.QuickOpenFiles : UiText.WorkspaceTextSearch,
            0,
            NativeMethods.StaticLeft);
        _searchEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            SearchEditIdentifier,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll);
        _matchCaseButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.MatchCase,
            CommandMatchCase,
            NativeMethods.ButtonAutoCheckbox);
        _wholeWordButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.MatchWholeWord,
            CommandWholeWord,
            NativeMethods.ButtonAutoCheckbox);
        _regularExpressionButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.RegularExpression,
            CommandRegularExpression,
            NativeMethods.ButtonAutoCheckbox);
        _includeIgnoredButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.IncludeIgnoredFiles,
            CommandIncludeIgnored,
            NativeMethods.ButtonAutoCheckbox);
        _closeButton = CreateControl(NativeMethods.ButtonClass, UiText.CloseSymbol, CommandClose, NativeMethods.ButtonPushButton);
        _resultList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            ResultsIdentifier,
            NativeMethods.WindowStyleBorder
                | NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxNoIntegralHeight);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft);
        bool textMode = _mode == WorkspaceSearchMode.Text;
        foreach (nint option in new[] { _matchCaseButton, _wholeWordButton, _regularExpressionButton, _includeIgnoredButton })
        {
            _ = NativeMethods.ShowWindow(option, textMode ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
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
            QueueSearch(immediate: false);
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
            QueueSearch(immediate: true);
        }
    }

    private void QueueSearch(bool immediate)
    {
        if (_disposed)
        {
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new();
        int version = ++_searchVersion;
        _ = RunSearchAsync(version, immediate, _searchCancellation.Token);
    }

    private async Task RunSearchAsync(int version, bool immediate, CancellationToken cancellationToken)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(150, cancellationToken);
            }

            string query = NativeMethods.GetWindowTextValue(_searchEdit);
            if (_mode == WorkspaceSearchMode.Text && query.Length == 0)
            {
                ShowResults([], null);
                return;
            }

            _ = NativeMethods.SetWindowText(_noticeLabel, UiText.Searching);
            if (_mode == WorkspaceSearchMode.FileNames)
            {
                IReadOnlyList<FileSearchResult> files = await _searchService.SearchFilesAsync(
                    _workspaceRoot,
                    query,
                    cancellationToken);
                if (version != _searchVersion || _disposed)
                {
                    return;
                }

                ShowResults(
                    files.Select(file => new SearchResultEntry(file.RelativePath, null, file.RelativePath)).ToArray(),
                    UiText.FileCount(files.Count));
            }
            else
            {
                SearchOptions options = new(
                    query,
                    IsChecked(_matchCaseButton),
                    IsChecked(_wholeWordButton),
                    IsChecked(_regularExpressionButton),
                    IsChecked(_includeIgnoredButton));
                TextSearchResult result = await _searchService.SearchTextAsync(_workspaceRoot, options, cancellationToken);
                if (version != _searchVersion || _disposed)
                {
                    return;
                }

                SearchResultEntry[] entries = result.Matches
                    .Select(match => new SearchResultEntry(
                        match.RelativePath,
                        match.LineNumber,
                        $"{match.RelativePath}:{match.LineNumber}:{match.ColumnNumber}  {match.LineText}"))
                    .ToArray();
                ShowResults(entries, result.Notice ?? UiText.SearchResultCount(entries.Length));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            if (!_disposed && version == _searchVersion)
            {
                ShowResults([], UiText.SearchComponentFailed);
                _setStatus(exception.Message);
            }
        }
    }

    private void ShowResults(IReadOnlyList<SearchResultEntry> results, string? notice)
    {
        _results.Clear();
        _results.AddRange(results);
        _ = NativeMethods.SendMessage(_resultList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (SearchResultEntry result in results)
        {
            _ = NativeMethods.SendMessage(_resultList, NativeMethods.ListBoxAddString, 0, result.DisplayText);
        }

        _ = NativeMethods.SetWindowText(_noticeLabel, notice ?? string.Empty);
    }

    private void OpenSelectedResult()
    {
        int index = checked((int)NativeMethods.SendMessage(
            _resultList,
            NativeMethods.ListBoxGetCurrentSelection,
            0,
            0));
        if (index < 0 || index >= _results.Count)
        {
            return;
        }

        SearchResultEntry result = _results[index];
        _openResult(Path.Combine(_workspaceRoot, result.RelativePath), result.LineNumber);
        _close();
    }

    private void Layout()
    {
        if (Handle == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        Move(_titleLabel, 14, 12, Math.Max(0, width - 64), 22);
        Move(_closeButton, Math.Max(0, width - 42), 8, 30, 28);
        Move(_searchEdit, 14, 42, Math.Max(0, width - 28), 28);
        int listTop = 78;
        if (_mode == WorkspaceSearchMode.Text)
        {
            Move(_matchCaseButton, 14, 76, 88, 24);
            Move(_wholeWordButton, 104, 76, 82, 24);
            Move(_regularExpressionButton, 188, 76, 96, 24);
            Move(_includeIgnoredButton, 286, 76, 112, 24);
            listTop = 106;
        }

        Move(_resultList, 14, listTop, Math.Max(0, width - 28), Math.Max(0, height - listTop - 38));
        Move(_noticeLabel, 14, Math.Max(listTop, height - 28), Math.Max(0, width - 28), 20);
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

    private sealed record SearchResultEntry(string RelativePath, int? LineNumber, string DisplayText);
}
