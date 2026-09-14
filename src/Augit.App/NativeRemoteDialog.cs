using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed partial class NativeRemoteDialog : IDisposable
{
    private const string WindowClassName = "Augit.RemoteDialog.Native";
    private const int DialogWidth = 930;
    private const int DialogHeight = 407;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int SidebarWidth = 260;
    private const int RemoteListIdentifier = 1;
    private const int CommandAdd = 10;
    private const int CommandSave = 11;
    private const int CommandDelete = 12;
    private const int CommandRefresh = 13;
    private const int CommandCancelOperation = 14;
    private const int CommandClose = 15;
    private const int CommandDeleteToolbar = 16;
    private const int CommandHeaderClose = 17;
    private const int ControlName = 20;
    private const int ControlFetchUrl = 21;
    private const int ControlPushUrl = 22;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeRemoteDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly IGitRemoteService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly List<GitRemoteInfo> _remotes = [];
    private readonly List<nint> _labels = [];
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private nint _remoteList;
    private nint _nameEdit;
    private nint _fetchUrlEdit;
    private nint _pushUrlEdit;
    private nint _detailTitle;
    private nint _addButton;
    private nint _deleteToolbarButton;
    private nint _saveButton;
    private nint _deleteButton;
    private nint _refreshButton;
    private nint _cancelOperationButton;
    private nint _closeButton;
    private nint _headerCloseButton;
    private nint _noticeLabel;
    private nint _controlBrush;
    private nint _headingFont;
    private NativeToolTip? _toolTip;
    private bool _dark;
    private bool _operationRunning;
    private bool _closed;

    internal NativeRemoteDialog(
        nint owner,
        GitRepositorySnapshot repository,
        IGitRemoteService service,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        _owner = owner;
        _repository = repository;
        _service = service;
        _settings = settings;
        _setStatus = setStatus;
        _detailProcedure = HandleDetailMessage;
        EnsureWindowClass();
        _headingFont = NativeTheme.CreateOwnedUiFont(settings.TextFontFamily, settings.UiFontSize, 600);
        MeasureLayout();
        int width = S(DialogWidth);
        int height = S(DialogHeight);
        int x = NativeMethods.UseDefault;
        int y = NativeMethods.UseDefault;
        if (NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle ownerRectangle))
        {
            x = ownerRectangle.Left + Math.Max(0, ((ownerRectangle.Right - ownerRectangle.Left) - width) / 2);
            y = ownerRectangle.Top + Math.Max(0, ((ownerRectangle.Bottom - ownerRectangle.Top) - height) / 2);
        }

        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.RemoteManagement,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            x,
            y,
            width,
            height,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.RemoteDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        try
        {
            CreateControls();
            ApplyAppearance();
            ResizeToContent();
            _ = LoadRemotesAsync();
        }
        catch { Dispose(); throw; }
    }

    internal static void Show(
        nint owner,
        GitRepositorySnapshot repository,
        IGitRemoteService service,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        NativeRemoteDialog dialog = new(owner, repository, service, settings, setStatus);
        try { dialog.Run(); }
        finally
        {
            dialog.Dispose();
            if (dialog._quitCode is { } code) NativeMethods.PostQuitMessage(code);
        }
    }

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight, int SidebarWidth) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight, SidebarWidth);

    internal static IReadOnlyList<string> FieldLabelsForTest =>
        [UiText.RemoteName, UiText.RemoteFetchUrl, UiText.RemotePushUrl];

    internal static IReadOnlyList<int> ExistingRemoteTabOrderForTest =>
        [RemoteListIdentifier, ControlName, ControlFetchUrl, ControlPushUrl, CommandDelete, CommandSave, CommandClose, CommandHeaderClose, CommandAdd, CommandDeleteToolbar, CommandRefresh];

    internal static IReadOnlyList<int> NewRemoteTabOrderForTest =>
        [ControlName, ControlFetchUrl, ControlPushUrl, CommandSave, CommandClose, CommandHeaderClose, CommandAdd, CommandRefresh, RemoteListIdentifier];

    internal static string DisplayPushUrlForTest(GitRemoteInfo remote) => remote.PushUrl;

    internal static IReadOnlyList<GitRemoteInfo> OrderRemotesForDisplayForTest(
        IEnumerable<GitRemoteInfo> remotes)
    {
        return remotes
            .OrderBy(
                remote => remote.Name.Equals("origin", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(remote => remote.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(remote => remote.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private void Run()
    {
        if (_closed) return;
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        bool ownerEnabled = NativeMethods.IsWindowEnabled(_owner);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_remoteList);
        try
        {
            while (!_closed)
            {
                int state = NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0);
                if (state <= 0)
                {
                    if (state == 0) _quitCode = unchecked((int)message.WordParameter);
                    break;
                }
                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && NativeFocusNavigation.ContainsWindow(_handle, message.Window)
                    && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyTab)
                {
                    MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
                    continue;
                }

                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && NativeFocusNavigation.ContainsWindow(_handle, message.Window)
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
            if (NativeMethods.IsWindow(_owner))
            {
                _ = NativeMethods.EnableWindow(_owner, ownerEnabled);
                focusScope.Restore();
            }
            Dispose();
        }
    }

    internal void MoveFocus(bool backwards)
    {
        bool hasSelection = SelectedRemoteIndex >= 0 && SelectedRemoteIndex < _remotes.Count;
        nint[] controls = hasSelection
            ? [_remoteList, _nameEdit, _fetchUrlEdit, _pushUrlEdit, _deleteButton, _saveButton, _cancelOperationButton, _closeButton, _headerCloseButton, _addButton, _deleteToolbarButton, _refreshButton]
            : [_nameEdit, _fetchUrlEdit, _pushUrlEdit, _saveButton, _cancelOperationButton, _closeButton, _headerCloseButton, _addButton, _refreshButton, _remoteList];
        nint current = NativeMethods.GetFocus();
        int currentIndex = Array.IndexOf(controls, current);
        if (currentIndex < 0)
        {
            currentIndex = backwards ? 0 : -1;
        }

        int direction = backwards ? -1 : 1;
        for (int offset = 1; offset <= controls.Length; offset++)
        {
            int index = (currentIndex + (direction * offset) + controls.Length) % controls.Length;
            nint candidate = controls[index];
            if (candidate != 0
                && NativeMethods.IsWindowVisible(candidate)
                && NativeMethods.IsWindowEnabled(candidate))
            {
                EnsureDetailVisible(candidate);
                _ = NativeMethods.SetFocus(candidate);
                return;
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
                throw new Win32Exception(error, UiText.RemoteDialogClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeRemoteDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message == NativeMethods.WindowMessagePaint)
        {
            instance.PaintWindow();
            return 0;
        }

        if (message == NativeMethods.WindowMessageEraseBackground)
        {
            return 1;
        }

        if (message == NativeMethods.WindowMessageNonClientHitTest)
        {
            return instance.HitTest();
        }

        if (message == NativeMethods.WindowMessageDrawItem)
        {
            return instance.DrawControl(longParameter) ? 1 : 0;
        }

        if (message is NativeMethods.WindowMessageControlColorEdit
            or NativeMethods.WindowMessageControlColorListBox
            or NativeMethods.WindowMessageControlColorButton
            or NativeMethods.WindowMessageControlColorStatic)
        {
            return instance.ApplyControlColor(unchecked((nint)wordParameter));
        }

        if (message == NativeMethods.WindowMessageSize)
        {
            instance.Layout();
            return 0;
        }

        if (message == NativeMethods.WindowMessageDpiChanged)
        {
            _ = NativeTheme.UpdateDpiForWindow(window);
            if (instance._headingFont != 0) _ = NativeMethods.DeleteObject(instance._headingFont);
            instance._headingFont = NativeTheme.CreateOwnedUiFont(instance._settings.TextFontFamily, instance._settings.UiFontSize, 600);
            instance.MeasureLayout();
            instance.ApplyAppearance();
            instance.ResizeToContent();
            return 0;
        }

        if (message == NativeMethods.WindowMessageCommand)
        {
            instance.HandleCommand(wordParameter);
            return 0;
        }

        if (message == NativeMethods.WindowMessageClose)
        {
            instance.Close();
            return 0;
        }

        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            instance.Dispose();
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }
        if (message == ReadCompleted) return instance.CompleteRead();
        if (message == WriteCompleted) return instance.CompleteWrite();

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        _detailPanel = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleClipChildren | NativeMethods.WindowStyleVerticalScroll,
            0, 0, 1, 1, _handle, 30, NativeMethods.GetModuleHandle(null), 0);
        if (_detailPanel == 0 || !NativeMethods.SetWindowSubclass(_detailPanel, _detailProcedure, 1, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.RemoteDialogControlCreateFailed);
        _addButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandAdd,
            NativeMethods.ButtonOwnerDraw,
            16,
            HeaderHeight + 10,
            28,
            28);
        _deleteToolbarButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandDeleteToolbar,
            NativeMethods.ButtonOwnerDraw,
            50,
            HeaderHeight + 10,
            28,
            28);
        _refreshButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandRefresh,
            NativeMethods.ButtonOwnerDraw,
            84,
            HeaderHeight + 10,
            28,
            28);
        _remoteList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            RemoteListIdentifier,
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxNoIntegralHeight
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings,
            16,
            HeaderHeight + 48,
            SidebarWidth - 28,
            DialogHeight - HeaderHeight - FooterHeight - 64);
        _ = NativeMethods.SendMessage(
            _remoteList,
            NativeMethods.ListBoxSetItemHeight,
            0,
            unchecked((nint)S(27)));
        _detailTitle = CreateControl(
            NativeMethods.StaticClass,
            UiText.NoRemotes,
            0,
            NativeMethods.StaticLeft,
            SidebarWidth + 32,
            HeaderHeight + 60,
            DialogWidth - SidebarWidth - 64,
            30);
        CreateLabel(UiText.RemoteName, SidebarWidth + 32, HeaderHeight + 108);
        _nameEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            ControlName,
            NativeMethods.EditAutoHorizontalScroll,
            SidebarWidth + 152,
            HeaderHeight + 100,
            DialogWidth - SidebarWidth - 184,
            30);
        CreateLabel(UiText.RemoteFetchUrl, SidebarWidth + 32, HeaderHeight + 152);
        _fetchUrlEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            ControlFetchUrl,
            NativeMethods.EditAutoHorizontalScroll,
            SidebarWidth + 152,
            HeaderHeight + 144,
            DialogWidth - SidebarWidth - 184,
            30);
        CreateLabel(UiText.RemotePushUrl, SidebarWidth + 32, HeaderHeight + 196);
        _pushUrlEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            ControlPushUrl,
            NativeMethods.EditAutoHorizontalScroll,
            SidebarWidth + 152,
            HeaderHeight + 188,
            DialogWidth - SidebarWidth - 184,
            30);
        _deleteButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Delete,
            CommandDelete,
            NativeMethods.ButtonOwnerDraw,
            DialogWidth - 196,
            HeaderHeight + 240,
            78,
            28);
        _saveButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Save,
            CommandSave,
            NativeMethods.ButtonOwnerDraw,
            DialogWidth - 110,
            HeaderHeight + 240,
            78,
            28);
        _cancelOperationButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CancelOperation,
            CommandCancelOperation,
            NativeMethods.ButtonOwnerDraw,
            DialogWidth - 294,
            DialogHeight - FooterHeight - 40,
            100,
            28);
        _closeButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Close,
            CommandClose,
            NativeMethods.ButtonOwnerDraw,
            DialogWidth - 100,
            DialogHeight - FooterHeight - 40,
            78,
            28);
        _headerCloseButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CloseSymbol,
            CommandHeaderClose,
            NativeMethods.ButtonOwnerDraw,
            DialogWidth - 45,
            7,
            32,
            31);
        _noticeLabel = CreateControl(
            NativeMethods.StaticClass,
            string.Empty,
            0,
            NativeMethods.StaticLeft,
            SidebarWidth + 32,
            HeaderHeight + 285,
            DialogWidth - SidebarWidth - 64,
            70);
        _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowHide);
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_addButton, UiText.Add);
        _toolTip.Add(_deleteToolbarButton, UiText.Delete);
        _toolTip.Add(_refreshButton, UiText.Refresh);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        _toolTip.Add(_noticeLabel, string.Empty);
        _ = NativeAccessibility.SetName(_nameEdit, UiText.RemoteName);
        _ = NativeAccessibility.SetName(_fetchUrlEdit, UiText.RemoteFetchUrl);
        _ = NativeAccessibility.SetName(_pushUrlEdit, UiText.RemotePushUrl);
        Layout();
    }

    private void CreateLabel(string text, int x, int y)
    {
        _labels.Add(CreateControl(
            NativeMethods.StaticClass,
            text,
            0,
            NativeMethods.StaticLeft,
            x,
            y,
            110,
            22));
    }

    private nint CreateControl(
        string className,
        string text,
        int identifier,
        uint specificStyle,
        int x,
        int y,
        int width,
        int height)
    {
        uint commonStyle = NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible;
        if (!className.Equals(NativeMethods.StaticClass, StringComparison.Ordinal))
        {
            commonStyle |= NativeMethods.WindowStyleTabStop;
        }

        bool detail = identifier is ControlName or ControlFetchUrl or ControlPushUrl or CommandSave or CommandDelete
            || className.Equals(NativeMethods.StaticClass, StringComparison.Ordinal);
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            commonStyle | specificStyle,
            S(x),
            S(y),
            S(width),
            S(height),
            detail ? _detailPanel : _handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.RemoteDialogControlCreateFailed);
        }

        nint font = NativeTheme.UiFont;
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        NativeTheme.ApplyToControl(control, NativeTheme.IsDark(_settings.Theme));
        if (detail && !NativeMethods.SetWindowSubclass(control, _detailProcedure, 1, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.RemoteDialogControlCreateFailed);
        return control;
    }

    private void HandleCommand(nuint wordParameter)
    {
        if (_closed) return;
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (_operationRunning && command is not CommandCancelOperation and not CommandClose and not CommandHeaderClose) return;
        if (command is ControlName or ControlFetchUrl or ControlPushUrl && notification == NativeMethods.EditNotificationChanged)
        {
            // 输入优先于尚未接纳的刷新结果，不能用旧读取覆盖草稿。
            CancelRead();
            return;
        }
        if (command == RemoteListIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            CancelRead();
            PopulateSelectedRemote();
            return;
        }
        if (notification != 0) return;

        switch (command)
        {
            case CommandAdd:
                PrepareNewRemote();
                break;
            case CommandSave:
                _ = SaveAsync();
                break;
            case CommandDeleteToolbar:
            case CommandDelete:
                _ = DeleteAsync();
                break;
            case CommandRefresh:
                _ = LoadRemotesAsync();
                break;
            case CommandCancelOperation:
                RequestCancellation();
                break;
            case CommandClose:
            case CommandHeaderClose:
                Close();
                break;
        }
    }

    private Task LoadRemotesAsync()
    {
        if (_closed || _operationRunning || _readCancellation is not null)
        {
            return Task.CompletedTask;
        }
        CancellationTokenSource cancellation = new();
        _readCancellation = cancellation;
        SetNotice(UiText.ReadingRemotes);
        return _readTask = ExecuteReadAsync(cancellation);
    }

    private Task AddAsync()
    {
        string name = NativeMethods.GetWindowTextValue(_nameEdit).Trim();
        string fetch = NativeMethods.GetWindowTextValue(_fetchUrlEdit).Trim();
        string? push = NullIfWhiteSpace(NativeMethods.GetWindowTextValue(_pushUrlEdit));
        return StartWrite(token => _service.AddRemoteAsync(_repository, name, fetch, push, token));
    }

    private Task SaveAsync()
    {
        int index = SelectedRemoteIndex;
        if (index < 0 || index >= _remotes.Count)
        {
            return AddAsync();
        }

        string current = _remotes[index].Name;
        string name = NativeMethods.GetWindowTextValue(_nameEdit).Trim();
        string fetch = NativeMethods.GetWindowTextValue(_fetchUrlEdit).Trim();
        string? push = NullIfWhiteSpace(NativeMethods.GetWindowTextValue(_pushUrlEdit));
        return StartWrite(token => _service.UpdateRemoteAsync(_repository, current, name, fetch, push, token));
    }

    private Task DeleteAsync()
    {
        int index = SelectedRemoteIndex;
        if (index < 0 || index >= _remotes.Count)
        {
            ShowError(UiText.SelectRemoteFirst);
            return Task.CompletedTask;
        }

        string remoteName = _remotes[index].Name;
        if (!NativeActionConfirmationDialog.Show(
                _handle,
                _settings,
                "删除远端",
                $"远端 {remoteName} 将从当前仓库移除",
                UiText.ConfirmDeleteRemoteDetails(remoteName),
                "删除远端",
                danger: true))
        {
            return Task.CompletedTask;
        }
        return StartWrite(token => _service.DeleteRemoteAsync(_repository, remoteName, token));
    }

    private void CompleteOperation(GitRemoteOperationResult result)
    {
        if (_closed)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            ShowError(result.ErrorMessage ?? UiText.RemoteOperationFailed);
        }
        else
        {
            _setStatus(UiText.RemoteOperationCompleted);
            SetNotice(UiText.RemoteOperationCompleted);
        }

        if (result.ActualRemotes is not null)
        {
            ShowRemotes(result.ActualRemotes, preserveFields: !result.IsSuccess);
        }
    }

    private void ShowRemotes(IReadOnlyList<GitRemoteInfo> remotes, bool preserveFields = false)
    {
        string? selectedName = null;
        int selectedIndex = SelectedRemoteIndex;
        if (selectedIndex >= 0 && selectedIndex < _remotes.Count)
        {
            selectedName = _remotes[selectedIndex].Name;
        }

        IReadOnlyList<GitRemoteInfo> orderedRemotes = OrderRemotesForDisplayForTest(remotes);
        _remotes.Clear();
        _remotes.AddRange(orderedRemotes);
        _ = NativeMethods.SendMessage(_remoteList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitRemoteInfo remote in orderedRemotes)
        {
            _ = NativeMethods.SendMessage(
                _remoteList,
                NativeMethods.ListBoxAddString,
                0,
                remote.Name);
        }

        if (_remotes.Count == 0)
        {
            if (!preserveFields) PrepareNewRemote(focus: false);
            return;
        }

        int nextIndex = selectedName is null
            ? preserveFields ? -1 : 0
            : _remotes.FindIndex(remote => remote.Name.Equals(selectedName, StringComparison.Ordinal));
        if (nextIndex < 0 && !preserveFields)
        {
            nextIndex = 0;
        }

        _ = NativeMethods.SendMessage(
            _remoteList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)nextIndex),
            0);
        if (!preserveFields) PopulateSelectedRemote();
    }

    private void PrepareNewRemote(bool focus = true)
    {
        CancelRead();
        SetDetailOffset(0);
        _ = NativeMethods.SendMessage(
            _remoteList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)(nint)(-1)),
            0);
        _ = NativeMethods.SetWindowText(_detailTitle, UiText.NewRemote);
        _ = NativeMethods.SetWindowText(_nameEdit, string.Empty);
        _ = NativeMethods.SetWindowText(_fetchUrlEdit, string.Empty);
        _ = NativeMethods.SetWindowText(_pushUrlEdit, string.Empty);
        _ = NativeMethods.EnableWindow(_deleteButton, false);
        _ = NativeMethods.EnableWindow(_deleteToolbarButton, false);
        if (focus) _ = NativeMethods.SetFocus(_nameEdit);
    }

    private void PopulateSelectedRemote()
    {
        int index = SelectedRemoteIndex;
        if (index < 0 || index >= _remotes.Count)
        {
            return;
        }

        GitRemoteInfo remote = _remotes[index];
        SetDetailOffset(0);
        _ = NativeMethods.SetWindowText(_detailTitle, remote.Name);
        _ = NativeMethods.SetWindowText(_nameEdit, remote.Name);
        _ = NativeMethods.SetWindowText(_fetchUrlEdit, remote.FetchUrl);
        _ = NativeMethods.SetWindowText(_pushUrlEdit, DisplayPushUrlForTest(remote));
        _ = NativeMethods.EnableWindow(_deleteButton, true);
        _ = NativeMethods.EnableWindow(_deleteToolbarButton, true);
    }

    private int SelectedRemoteIndex => checked((int)NativeMethods.SendMessage(
        _remoteList,
        NativeMethods.ListBoxGetCurrentSelection,
        0,
        0));

    private bool BeginOperation()
    {
        if (_closed || _operationRunning)
        {
            return false;
        }
        CancelRead();
        _operationCancellation?.Dispose();
        _operationCancellation = new();
        _operationRunning = true;
        SetOperationControlsEnabled(false);
        _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowNormal);
        SetNotice(UiText.UpdatingRemotes);
        return true;
    }

    private void EndOperation()
    {
        _operationRunning = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        if (!_closed)
        {
            SetOperationControlsEnabled(true);
            UpdateSelectionActionsEnabled();
            _ = NativeMethods.ShowWindow(_cancelOperationButton, NativeMethods.ShowHide);
        }
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _remoteList,
            _nameEdit,
            _fetchUrlEdit,
            _pushUrlEdit,
            _detailTitle,
            _addButton,
            _deleteToolbarButton,
            _saveButton,
            _deleteButton,
            _refreshButton,
            _closeButton,
            _headerCloseButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }
    }

    private void UpdateSelectionActionsEnabled()
    {
        bool hasSelection = SelectedRemoteIndex >= 0 && SelectedRemoteIndex < _remotes.Count;
        _ = NativeMethods.EnableWindow(_deleteButton, hasSelection && !_operationRunning);
        _ = NativeMethods.EnableWindow(_deleteToolbarButton, hasSelection && !_operationRunning);
    }

    private void ShowError(string message)
    {
        if (_closed)
        {
            return;
        }

        _setStatus(message);
        SetNotice(message);
        EnsureDetailVisible(_noticeLabel);
    }

    private void ApplyAppearance()
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        _dark = dark;
        NativeTheme.ApplyToWindow(_handle, dark);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }

        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(dark).Panel);
        NativeTheme.ApplyToControl(_detailPanel, dark);
        foreach (nint control in new[]
        {
            _remoteList,
            _nameEdit,
            _fetchUrlEdit,
            _pushUrlEdit,
            _detailTitle,
            _addButton,
            _deleteToolbarButton,
            _saveButton,
            _deleteButton,
            _refreshButton,
            _cancelOperationButton,
            _closeButton,
            _noticeLabel,
            _headerCloseButton,
        }.Concat(_labels))
        {
            NativeTheme.ApplyToControl(control, dark);
            _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 0);
            _ = NativeMethods.InvalidateRectangle(control, 0, true);
        }

        _toolTip?.ApplyAppearance(dark);
        if (_headingFont != 0)
        {
            _ = NativeMethods.SendMessage(
                _detailTitle,
                NativeMethods.WindowMessageSetFont,
                unchecked((nuint)_headingFont),
                1);
        }

        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        _sidebarBoundary = S(16) + Math.Min(S(SidebarWidth), Math.Max(S(180), width / 3));
        int contentTop = _headerHeight + _toolbarHeight;
        int toolbarY = _headerHeight + (_toolbarHeight - S(28)) / 2;
        Move(_addButton, S(16), toolbarY, S(28), S(28));
        Move(_deleteToolbarButton, S(50), toolbarY, S(28), S(28));
        Move(_refreshButton, S(84), toolbarY, S(28), S(28));
        Move(_remoteList, S(16), contentTop, _sidebarBoundary - S(24), Math.Max(0, height - contentTop - _footerHeight - S(4)));
        _ = NativeMethods.SendMessage(_remoteList, NativeMethods.ListBoxSetItemHeight, 0, Math.Max(S(27), _textHeight + S(8)));
        Move(_detailPanel, _sidebarBoundary + S(1), contentTop,
            Math.Max(0, width - _sidebarBoundary - S(17)), Math.Max(0, height - contentTop - _footerHeight));
        int footerY = height - _footerHeight + (_footerHeight - _buttonHeight) / 2;
        Move(_closeButton, width - S(22) - _closeWidth, footerY, _closeWidth, _buttonHeight);
        Move(_cancelOperationButton, width - S(30) - _closeWidth - _cancelWidth, footerY, _cancelWidth, _buttonHeight);
        Move(_headerCloseButton, width - S(45), (_headerHeight - S(31)) / 2, S(32), S(31));
        LayoutDetails();
        _ = NativeMethods.InvalidateRectangle(_handle, 0, false);
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
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
        int identifier = unchecked((int)item.ControlIdentifier);
        if (identifier == RemoteListIdentifier)
        {
            return DrawRemoteListItem(item);
        }

        return identifier switch
        {
            CommandSave => NativeTheme.DrawFlatButton(parameter, _dark, emphasized: true),
            CommandAdd or CommandDeleteToolbar or CommandRefresh => DrawToolbarButton(parameter, identifier),
            CommandHeaderClose => NativeTheme.DrawFlatButton(parameter, _dark),
            CommandDelete or CommandCancelOperation or CommandClose => NativeTheme.DrawFlatButton(parameter, _dark, outlined: true),
            _ => false,
        };
    }

    private bool DrawToolbarButton(nint parameter, int identifier)
    {
        NativeManagementToolbarIcon icon = identifier switch
        {
            CommandAdd => NativeManagementToolbarIcon.Add,
            CommandDeleteToolbar => NativeManagementToolbarIcon.Delete,
            _ => NativeManagementToolbarIcon.Refresh,
        };
        return NativeTheme.DrawManagementToolbarButton(parameter, _dark, icon);
    }

    private bool DrawRemoteListItem(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        bool selected = (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        NativeMethods.Rectangle rectangle = item.ItemRectangle;
        Fill(item.DeviceContext, rectangle, palette.Panel);
        if (selected)
        {
            NativeMethods.Rectangle selection = rectangle;
            selection.Left += S(3);
            selection.Top += S(1);
            selection.Right -= S(3);
            selection.Bottom -= S(1);
            FillRounded(item.DeviceContext, selection, palette.AccentSoft, S(5));
        }
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= _remotes.Count)
        {
            return true;
        }

        NativeMethods.Rectangle iconRectangle = rectangle;
        iconRectangle.Left += S(9);
        iconRectangle.Right = iconRectangle.Left + S(18);
        _ = NativeTheme.DrawRemoteIcon(item.DeviceContext, iconRectangle, palette.Muted);
        rectangle.Left += S(31);
        rectangle.Right -= S(7);
        DrawText(item.DeviceContext, _remotes[index].Name, rectangle, palette.Text, NativeTheme.UiFont);
        return true;
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
            if (!NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
            {
                return;
            }

            NativeThemePalette palette = NativeTheme.Palette(_dark);
            Fill(deviceContext, client, palette.BorderStrong);
            int border = Math.Max(1, S(1));
            Fill(deviceContext, new() { Left = border, Top = border, Right = client.Right - border, Bottom = client.Bottom - border }, palette.Panel);
            int header = _headerHeight;
            int footer = _footerHeight;
            int sidebar = _sidebarBoundary;
            Fill(deviceContext, new NativeMethods.Rectangle
            {
                Left = sidebar,
                Top = header + _toolbarHeight,
                Right = sidebar + S(1),
                Bottom = client.Bottom - footer,
            }, palette.Border);
            Fill(deviceContext, new NativeMethods.Rectangle
            {
                Left = 0,
                Top = header,
                Right = client.Right,
                Bottom = header + S(1),
            }, palette.Border);
            Fill(deviceContext, new NativeMethods.Rectangle
            {
                Left = 0,
                Top = client.Bottom - footer,
                Right = client.Right,
                Bottom = client.Bottom - footer + S(1),
            }, palette.Border);
            Fill(deviceContext, new NativeMethods.Rectangle
            {
                Left = S(120),
                Top = header + (_toolbarHeight - S(18)) / 2,
                Right = S(121),
                Bottom = header + (_toolbarHeight + S(18)) / 2,
            }, palette.Border);
            NativeMethods.Rectangle title = new()
            {
                Left = S(26),
                Top = 0,
                Right = Math.Max(S(26), client.Right - S(60)),
                Bottom = header,
            };
            DrawText(deviceContext, UiText.RemoteManagement, title, palette.Text, NativeTheme.UiMediumFont);
            NativeMethods.Rectangle toolbarTitle = new()
            {
                Left = S(133),
                Top = header,
                Right = Math.Max(S(133), client.Right - S(12)),
                Bottom = header + _toolbarHeight,
            };
            DrawText(deviceContext, UiText.RemoteManagement, toolbarTitle, palette.Text, NativeTheme.UiMediumFont);
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }
    }

    private nint HitTest()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point)
            || !NativeMethods.ScreenToClient(_handle, ref point))
        {
            return NativeMethods.HitTestClient;
        }

        _ = NativeMethods.GetClientRectangle(_handle, out var client);
        return point.Y < _headerHeight && point.X < client.Right - S(54)
            ? NativeMethods.HitTestCaption
            : NativeMethods.HitTestClient;
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
        if (NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, rectangle, color, radius))
        {
            return;
        }

        Fill(deviceContext, rectangle, color);
    }

    private static void DrawText(nint deviceContext, string text, NativeMethods.Rectangle rectangle, uint color, nint font)
    {
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        nint previousFont = font == 0 ? 0 : NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private static int S(int logicalPixels)
    {
        return NativeTheme.Scale(logicalPixels);
    }

    private void Close(bool force = false)
    {
        if (_closed)
        {
            return;
        }

        if (_operationRunning && !force)
        {
            RequestCancellation();
            return;
        }
        nint handle;
        lock (_workGate)
        {
            _closed = true;
            _operationCancellation?.Cancel();
            if (_pendingWrite is not null) _operationCancellation?.Dispose();
            _pendingWrite = null;
            _operationCancellation = null;
            CancelRead();
            handle = _handle;
            _handle = 0;
        }
        NativeMethods.WakeWindowMessageLoop(handle);
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        _toolTip?.Dispose();
        _toolTip = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        if (_headingFont != 0)
        {
            _ = NativeMethods.DeleteObject(_headingFont);
            _headingFont = 0;
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    public void Dispose()
    {
        Close(force: true);
        GC.SuppressFinalize(this);
    }

    private static string? NullIfWhiteSpace(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
