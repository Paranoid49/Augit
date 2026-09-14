using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed partial class NativeStashDialog : IDisposable
{
    private const string WindowClassName = "Augit.StashDialog.Native";
    private const int DialogWidth = 620, DialogHeight = 323, HeaderHeight = 45, FooterHeight = 53;
    private const int CommandCreate = 10, CommandCancel = 11, CommandClose = 12;
    private const int ControlRoot = 19, ControlMessage = 20, ControlKeepIndex = 21;
    private const uint OperationCompleted = NativeMethods.WindowMessageApp + 70;
    private static readonly object ClassGate = new(), InstancesGate = new();
    private static readonly Dictionary<nint, NativeStashDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly IGitWorkspaceStateService _service;
    private readonly Action<string> _setStatus;
    private readonly string _branch;
    private readonly bool _dark;
    private readonly object _operationGate = new();
    private readonly NativeMethods.SubclassProcedure _bodyProcedure;
    private readonly List<nint> _labels = [];
    private CancellationTokenSource? _operationCancellation;
    private GitActionResult? _pendingResult;
    private Task _operationTask = Task.CompletedTask;
    private nint _handle, _body, _rootCombo, _branchValueLabel, _messageEdit, _keepIndexCheck;
    private nint _createButton, _cancelButton, _headerCloseButton, _noticeLabel, _controlBrush;
    private NativeToolTip? _toolTip;
    private bool _operationRunning, _changed, _closed, _composing;
    private int? _quitCode;

    internal NativeStashDialog(nint owner, GitRepositorySnapshot repository, IGitWorkspaceStateService service,
        ApplicationSettings settings, Action<string> setStatus, string? currentBranch = null)
    {
        _owner = NativeMethods.GetAncestor(owner, NativeMethods.GetAncestorRoot);
        if (_owner == 0) _owner = owner;
        _repository = repository;
        _service = service;
        _setStatus = setStatus;
        _dark = NativeTheme.IsDark(settings.Theme);
        _branch = string.IsNullOrWhiteSpace(currentBranch) ? "Detached HEAD" : currentBranch;
        _bodyProcedure = HandleBodyMessage;
        EnsureWindowClass();
        _handle = NativeMethods.CreateWindow(0, WindowClassName, UiText.CreateStashTitle,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            0, 0, 1, 1, _owner, 0, NativeMethods.GetModuleHandle(null), 0);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.StashDialogCreateFailed);
        lock (InstancesGate) Instances.Add(_handle, this);
        try
        {
            CreateControls();
            ApplyAppearance();
            MeasureLayout();
            ResizeToContent();
        }
        catch { Dispose(); throw; }
    }

    internal static bool Show(nint owner, GitRepositorySnapshot repository, IGitWorkspaceStateService service,
        ApplicationSettings settings, Action<string> setStatus, string? currentBranch = null)
    {
        NativeStashDialog dialog = new(owner, repository, service, settings, setStatus, currentBranch);
        try { return dialog.Run(); }
        finally
        {
            dialog.Dispose();
            if (dialog._quitCode is { } code) NativeMethods.PostQuitMessage(code);
        }
    }

    internal bool Run()
    {
        using NativeModalFocusScope focus = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        bool ownerEnabled = NativeMethods.IsWindowEnabled(_owner);
        _ = NativeMethods.EnableWindow(_owner, false);
        try
        {
            _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
            _ = NativeMethods.UpdateWindow(_handle);
            _ = NativeMethods.SetFocus(_rootCombo);
            while (!_closed)
            {
                int status = NativeMethods.GetMessage(out var message, 0, 0, 0);
                if (status <= 0)
                {
                    if (status == 0) _quitCode = unchecked((int)message.WordParameter);
                    break;
                }
                if (HandleKey(message)) continue;
                // 输入法组词期间交给原输入窗口，不让 IsDialogMessage 把 Esc/Tab 当作对话框动作。
                if (_composing || !NativeMethods.IsDialogMessage(_handle, ref message))
                {
                    _ = NativeMethods.TranslateMessage(ref message);
                    _ = NativeMethods.DispatchMessage(ref message);
                }
            }
        }
        finally
        {
            Dispose();
            if (NativeMethods.IsWindow(_owner))
            {
                _ = NativeMethods.EnableWindow(_owner, ownerEnabled);
                if (ownerEnabled) _ = NativeMethods.SetForegroundWindow(_owner);
            }
            focus.Restore();
        }
        return _changed;
    }

    private bool HandleKey(NativeMethods.Message message)
    {
        if (message.MessageId != NativeMethods.WindowMessageKeyDown || _composing
            || (message.Window != _handle && !NativeMethods.IsChild(_handle, message.Window))) return false;
        int key = unchecked((int)message.WordParameter);
        bool comboOpen = NativeMethods.SendMessage(_rootCombo, 0x0157, 0, 0) != 0;
        if (comboOpen && key is NativeMethods.VirtualKeyEscape or NativeMethods.VirtualKeyEnter) return false;
        if (key == NativeMethods.VirtualKeyTab)
        {
            NativeFocusNavigation.MoveWithinRegion([_rootCombo, _messageEdit, _keepIndexCheck, _cancelButton, _createButton, _headerCloseButton],
                NativeMethods.GetFocus(), NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
            return true;
        }
        if (key == NativeMethods.VirtualKeyEscape) { RequestClose(); return true; }
        nint focused = NativeMethods.GetFocus();
        if (key == NativeMethods.VirtualKeyEnter && (focused == _cancelButton || focused == _createButton || focused == _headerCloseButton))
        {
            if (NativeMethods.IsWindowEnabled(focused)) _ = NativeMethods.SendMessage(focused, 0x00F5, 0, 0);
            return true;
        }
        return false;
    }

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered) return;
            NativeMethods.WindowClass windowClass = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                ClassName = WindowClassName,
            };
            if (NativeMethods.RegisterClass(ref windowClass) == 0 && Marshal.GetLastWin32Error() != NativeMethods.ErrorClassAlreadyExists)
                throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.StashDialogClassRegisterFailed);
            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint word, nint parameter)
    {
        NativeStashDialog? dialog;
        lock (InstancesGate) Instances.TryGetValue(window, out dialog);
        if (dialog is null) return NativeMethods.DefaultWindowProcedure(window, message, word, parameter);
        switch (message)
        {
            case NativeMethods.WindowMessagePaint: dialog.PaintWindow(); return 0;
            case NativeMethods.WindowMessageEraseBackground: return 1;
            case NativeMethods.WindowMessageSize: dialog.Layout(); return 0;
            case NativeMethods.WindowMessageDrawItem: return dialog.DrawControl(parameter) ? 1 : 0;
            case NativeMethods.WindowMessageControlColorEdit:
            case NativeMethods.WindowMessageControlColorListBox:
            case NativeMethods.WindowMessageControlColorButton:
            case NativeMethods.WindowMessageControlColorStatic:
                return dialog.ControlColor(unchecked((nint)word));
            case NativeMethods.WindowMessageCommand: dialog.Command(word, parameter); return 0;
            case OperationCompleted: dialog.CompleteOperation(); return 0;
            case NativeMethods.WindowMessageClose: dialog.RequestClose(); return 0;
            case 0x0082: dialog.Dispose(); break; // 宿主提前销毁也解除注册和取消旧结果。
            case NativeMethods.WindowMessageNonClientHitTest:
                if (NativeMethods.GetCursorPosition(out var point) && NativeMethods.ScreenToClient(window, ref point)
                    && NativeMethods.GetClientRectangle(window, out var bounds)
                    && point.Y < dialog._headerHeight && point.X < bounds.Right - S(54)) return NativeMethods.HitTestCaption;
                return NativeMethods.HitTestClient;
        }
        return NativeMethods.DefaultWindowProcedure(window, message, word, parameter);
    }

    private void CreateControls()
    {
        _body = CreateControl(_handle, NativeMethods.StaticClass, string.Empty, 18,
            NativeMethods.WindowStyleClipChildren | NativeMethods.WindowStyleVerticalScroll);
        AddSubclass(_body);
        foreach (string label in new[] { UiText.GitRoot, UiText.CurrentBranchLabel, UiText.StashMessage })
            _labels.Add(CreateControl(_body, NativeMethods.StaticClass, label, 30 + _labels.Count, 0));
        _rootCombo = CreateControl(_body, NativeMethods.ComboBoxClass, string.Empty, ControlRoot, RootControlStyleForTest);
        _ = NativeMethods.SendMessage(_rootCombo, NativeMethods.ComboBoxAddString, 0, _repository.RepositoryRoot ?? _repository.WorkspacePath);
        _ = NativeMethods.SendMessage(_rootCombo, NativeMethods.ComboBoxSetCurrentSelection, 0, 0);
        if (!NativeComboBoxTheme.Register(_rootCombo, () => _dark, parentDrawsFrame: true))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.StashDialogControlCreateFailed);
        _branchValueLabel = CreateControl(_body, NativeMethods.StaticClass, _branch, 22, 0x00004000); // SS_ENDELLIPSIS。
        _messageEdit = CreateControl(_body, NativeMethods.EditClass, string.Empty, ControlMessage,
            MessageControlStyleForTest | NativeMethods.EditAutoVerticalScroll | NativeMethods.WindowStyleVerticalScroll);
        _keepIndexCheck = CreateControl(_body, NativeMethods.ButtonClass, UiText.KeepIndexState, ControlKeepIndex, NativeMethods.ButtonAutoCheckbox);
        _noticeLabel = CreateControl(_body, NativeMethods.StaticClass, string.Empty, 23, 0);
        _cancelButton = CreateControl(_handle, NativeMethods.ButtonClass, UiText.Cancel, CommandCancel, NativeMethods.ButtonOwnerDraw);
        _createButton = CreateControl(_handle, NativeMethods.ButtonClass, UiText.CreateStashAction, CommandCreate, NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(_handle, NativeMethods.ButtonClass, string.Empty, CommandClose, NativeMethods.ButtonOwnerDraw);
        foreach (nint field in new[] { _rootCombo, _messageEdit, _keepIndexCheck }) AddSubclass(field);
        _ = NativeAccessibility.SetName(_rootCombo, UiText.GitRoot);
        _ = NativeAccessibility.SetName(_messageEdit, UiText.StashMessage);
        _ = NativeAccessibility.SetName(_headerCloseButton, UiText.Close);
        _toolTip = new(_handle);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        _toolTip.Add(_rootCombo, _repository.RepositoryRoot ?? _repository.WorkspacePath);
        _toolTip.Add(_branchValueLabel, _branch);
        _toolTip.Add(_noticeLabel, string.Empty);
    }

    private static nint CreateControl(nint parent, string className, string text, int id, uint style)
    {
        uint tab = className == NativeMethods.StaticClass ? 0 : NativeMethods.WindowStyleTabStop;
        nint control = NativeMethods.CreateWindow(0, className, text,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | tab | style,
            0, 0, 0, 0, parent, id, NativeMethods.GetModuleHandle(null), 0);
        if (control == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.StashDialogControlCreateFailed);
        return control;
    }

    private void AddSubclass(nint control)
    {
        if (!NativeMethods.SetWindowSubclass(control, _bodyProcedure, 2, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.StashDialogControlCreateFailed);
    }

    private void Command(nuint word, nint source)
    {
        if (source == _messageEdit && NativeMethods.HighWord(word) == 0x0300) UpdateMessageScroll(); // EN_CHANGE。
        if (NativeMethods.HighWord(word) != 0 || source == 0 || !NativeMethods.IsWindowEnabled(source)) return;
        int id = NativeMethods.LowWord(word);
        if (id == CommandCreate && source == _createButton) StartOperation();
        else if ((id == CommandCancel && source == _cancelButton) || (id == CommandClose && source == _headerCloseButton)) RequestClose();
    }

    private void StartOperation()
    {
        if (_closed || _operationRunning) return;
        string message = NativeMethods.GetWindowTextValue(_messageEdit).Trim();
        bool keep = NativeMethods.SendMessage(_keepIndexCheck, NativeMethods.ButtonMessageGetCheck, 0, 0) == (nint)NativeMethods.ButtonChecked;
        _operationRunning = true;
        CancellationTokenSource cancellation = new();
        _operationCancellation = cancellation;
        SetOperationControls(false);
        _ = NativeMethods.SetWindowText(_cancelButton, UiText.CancelOperation);
        _ = NativeMethods.SetWindowText(_createButton, UiText.CreatingStash);
        _ = NativeMethods.SetFocus(_cancelButton);
        SetNotice(UiText.CreatingStash);
        _operationTask = ExecuteAsync(message.Length == 0 ? null : message, keep, cancellation);
    }

    private async Task ExecuteAsync(string? message, bool keep, CancellationTokenSource cancellation)
    {
        GitActionResult result;
        try
        {
            result = await _service.StashWithOptionsAsync(_repository, message, false, keep, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { result = GitActionResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled); }
        catch (Exception) { result = GitActionResult.Failure(GitOperationFailureKind.CommandFailed, UiText.StashOperationFailed); }
        // 后台仅投递结果，原生窗口和状态提示必须在窗口所属线程更新。
        lock (_operationGate)
        {
            if (!_closed && ReferenceEquals(_operationCancellation, cancellation))
            {
                _pendingResult = result;
                _ = NativeMethods.PostMessage(_handle, OperationCompleted, 0, 0);
            }
            else cancellation.Dispose();
        }
    }

    private void CompleteOperation()
    {
        GitActionResult? result;
        lock (_operationGate)
        {
            result = _pendingResult;
            if (_closed || result is null) return;
            _pendingResult = null;
            if (_operationCancellation?.IsCancellationRequested == true)
                result = GitActionResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled, result.ActualStatus);
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
        _operationRunning = false;
        if (result.IsSuccess)
        {
            _changed = true;
            _setStatus(UiText.StashCreated);
            Dispose();
            return;
        }
        SetOperationControls(true);
        _ = NativeMethods.SetWindowText(_cancelButton, UiText.Cancel);
        _ = NativeMethods.SetWindowText(_createButton, UiText.CreateStashAction);
        string notice = result.ErrorMessage ?? UiText.StashOperationFailed;
        SetNotice(notice);
        _setStatus(notice);
    }

    private void SetOperationControls(bool enabled)
    {
        foreach (nint control in new[] { _rootCombo, _messageEdit, _keepIndexCheck, _createButton, _headerCloseButton })
            _ = NativeMethods.EnableWindow(control, enabled);
    }

    private void RequestClose()
    {
        if (_operationRunning) _operationCancellation?.Cancel();
        else Dispose();
    }

    public void Dispose()
    {
        nint handle;
        lock (_operationGate)
        {
            if (_closed) return;
            _closed = true;
            handle = _handle;
            _handle = 0;
            _operationCancellation?.Cancel();
            // 尚在服务中的取消源由异步方法释放，避免销毁后注册取消回调失败。
            if (_pendingResult is not null) _operationCancellation?.Dispose();
            _pendingResult = null;
            _operationCancellation = null;
        }
        NativeMethods.WakeWindowMessageLoop(handle);
        lock (InstancesGate) Instances.Remove(handle);
        NativeComboBoxTheme.Unregister(_rootCombo);
        foreach (nint control in new[] { _body, _rootCombo, _messageEdit, _keepIndexCheck })
            if (control != 0) _ = NativeMethods.RemoveWindowSubclass(control, _bodyProcedure, 2);
        _toolTip?.Dispose(); _toolTip = null;
        if (NativeMethods.IsWindow(handle)) _ = NativeMethods.DestroyWindow(handle);
        if (_controlBrush != 0) { _ = NativeMethods.DeleteObject(_controlBrush); _controlBrush = 0; }
        GC.SuppressFinalize(this);
    }

    internal nint HandleForTest => _handle;
    internal nint BodyForTest => _body;
    internal Task OperationTaskForTest => _operationTask;
    internal bool RunningForTest => _operationRunning;
    internal static int InstanceCountForTest { get { lock (InstancesGate) return Instances.Count; } }
    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest => (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);
    internal static IReadOnlyList<string> FieldLabelsForTest => [UiText.GitRoot, UiText.CurrentBranchLabel, UiText.StashMessage, UiText.KeepIndexState];
    internal static IReadOnlyList<int> TabOrderForTest => [ControlRoot, ControlMessage, ControlKeepIndex, CommandCancel, CommandCreate, CommandClose];
    internal static uint RootControlStyleForTest => NativeComboBoxTheme.ControlStyle;
    internal static uint MessageControlStyleForTest => NativeMethods.EditMultiline | NativeMethods.EditWantReturn;
    private static int S(int value) => NativeTheme.Scale(value);
}
