using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed partial class NativeCloneDialog : IDisposable
{
    private const string WindowClassName = "Augit.CloneDialog.Native";
    private const int DialogWidth = 930;
    // 视觉稿宽版 Clone 对话框由 45 像素标题栏、190 像素表单区和 53 像素操作栏组成。
    private const int DialogHeight = 289;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int CommandClone = 11;
    private const int CommandCancel = 12;
    private const int CommandClose = 13;
    private const int CommandShallow = 14;
    private const uint OperationCompleted = NativeMethods.WindowMessageApp + 71;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeCloneDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly IGitRepositoryService _repositoryService;
    private readonly object _operationGate = new();
    private GitRepositoryOperationResult? _pendingResult;
    private Task _operationTask = Task.CompletedTask;
    private readonly NativeMethods.SubclassProcedure _inputProcedure;
    private readonly List<nint> _labels = [];
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private nint _versionCombo;
    private nint _sourceEdit;
    private nint _destinationEdit;
    private nint _body;
    private nint _shallowCheck;
    private nint _depthEdit;
    private nint _depthSuffixLabel;
    private nint _cloneButton;
    private nint _cancelButton;
    private nint _headerCloseButton;
    private nint _noticeLabel;
    private nint _controlBrush;
    private NativeToolTip? _toolTip;
    private bool _dark;
    private bool _operationRunning;
    private bool _closed;
    private bool _composing;
    private int? _quitCode;
    private string? _result;

    internal NativeCloneDialog(nint owner, IGitRepositoryService repositoryService, ApplicationSettings settings)
    {
        _owner = NativeMethods.GetAncestor(owner, NativeMethods.GetAncestorRoot);
        if (_owner == 0) _owner = owner;
        _repositoryService = repositoryService;
        _dark = NativeTheme.IsDark(settings.Theme);
        _inputProcedure = HandleBodyMessage;
        EnsureWindowClass();
        (int x, int y) = Center(owner, S(DialogWidth), S(DialogHeight));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.CloneRepository,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            x,
            y,
            S(DialogWidth),
            S(DialogHeight),
            _owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.CloneWindowCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        try { CreateControls(); ApplyAppearance(); MeasureLayout(); ResizeToContent(); }
        catch { Dispose(); throw; }
    }

    internal static string? Show(nint owner, GitRuntimeInfo runtime, ApplicationSettings settings)
    {
        NativeCloneDialog dialog = new(owner, new GitRepositoryService(runtime), settings);
        try { return dialog.Run(); }
        finally
        {
            dialog.Dispose();
            if (dialog._quitCode is { } code) NativeMethods.PostQuitMessage(code);
        }
    }

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);

    internal static IReadOnlyList<string> FieldLabelsForTest =>
        [UiText.VersionControl, UiText.CloneSource, UiText.CloneDestination, UiText.ShallowClone];

    internal static uint VersionControlStyleForTest => NativeComboBoxTheme.ControlStyle;

    internal static uint InputControlStyleForTest => NativeMethods.EditAutoHorizontalScroll;

    internal static bool TryParseDepthForTest(bool shallowClone, string text, out int? depth)
    {
        depth = null;
        if (!shallowClone)
        {
            return true;
        }

        if (!int.TryParse(
                text.Trim(),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int value)
            || value <= 0)
        {
            return false;
        }

        depth = value;
        return true;
    }

    internal string? Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        bool ownerEnabled = NativeMethods.IsWindowEnabled(_owner);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_sourceEdit);
        try
        {
            while (!_closed)
            {
                int status = NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0);
                if (status <= 0)
                {
                    if (status == 0) _quitCode = unchecked((int)message.WordParameter);
                    break;
                }
                if (!_composing && message.MessageId == NativeMethods.WindowMessageKeyDown
                    && (message.Window == _handle || NativeMethods.IsChild(_handle, message.Window)))
                {
                    int key = unchecked((int)message.WordParameter);
                    bool comboOpen = NativeMethods.SendMessage(_versionCombo, 0x0157, 0, 0) != 0;
                    if (key == NativeMethods.VirtualKeyTab)
                    { MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0); continue; }
                    if (!comboOpen && key == NativeMethods.VirtualKeyEscape)
                    { CancelOrClose(); continue; }
                    if (!comboOpen && key == NativeMethods.VirtualKeyEnter)
                    {
                        nint focused = NativeMethods.GetFocus();
                        if (focused == _cancelButton || focused == _headerCloseButton) CancelOrClose();
                        else StartClone();
                        continue;
                    }
                }
                if (_composing || !NativeMethods.IsDialogMessage(_handle, ref message))
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
        }

        return _result;
    }

    /// <summary>
    /// 按 Clone 表单的视觉顺序循环移动焦点。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [
                _versionCombo,
                _sourceEdit,
                _destinationEdit,
                _shallowCheck,
                _depthEdit,
                _cancelButton,
                _cloneButton,
                _headerCloseButton,
            ],
            NativeMethods.GetFocus(),
            backwards);
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
                throw new Win32Exception(error, UiText.CloneWindowClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeCloneDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message is NativeMethods.WindowMessageControlColorEdit
            or NativeMethods.WindowMessageControlColorListBox
            or NativeMethods.WindowMessageControlColorButton
            or NativeMethods.WindowMessageControlColorStatic)
        {
            return instance.ControlColor(unchecked((nint)wordParameter));
        }

        return message switch
        {
            NativeMethods.WindowMessagePaint => instance.PaintWindow(),
            NativeMethods.WindowMessageEraseBackground => 1,
            NativeMethods.WindowMessageNonClientHitTest => instance.HitTest(),
            NativeMethods.WindowMessageDrawItem => instance.DrawControl(longParameter) ? 1 : 0,
            NativeMethods.WindowMessageSize => instance.LayoutMessage(),
            NativeMethods.WindowMessageCommand => instance.CommandMessage(wordParameter, longParameter),
            NativeMethods.WindowMessageClose => instance.CloseMessage(),
            OperationCompleted => instance.CompleteOperation(),
            0x0082 => instance.DestroyedMessage(),
            _ => NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter),
        };
    }

    private void CreateControls()
    {
        _body = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleClipChildren | NativeMethods.WindowStyleVerticalScroll,
            0, 0, 0, 0, _handle, 18, NativeMethods.GetModuleHandle(null), 0);
        if (_body == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.CloneControlCreateFailed);
        CreateLabel(UiText.VersionControl);
        _versionCombo = CreateControl(
            NativeMethods.ComboBoxClass,
            string.Empty,
            20,
            NativeComboBoxTheme.ControlStyle);
        _ = NativeMethods.SendMessage(_versionCombo, NativeMethods.ComboBoxAddString, 0, "Git");
        _ = NativeMethods.SendMessage(_versionCombo, NativeMethods.ComboBoxSetCurrentSelection, 0, 0);
        if (!NativeComboBoxTheme.Register(_versionCombo, () => _dark, parentDrawsFrame: true))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.CloneControlCreateFailed);
        }
        CreateLabel(UiText.CloneSource);
        _sourceEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            21,
            NativeMethods.EditAutoHorizontalScroll);
        _ = NativeMethods.SendMessage(_sourceEdit, NativeMethods.EditSetCueBanner, 1, UiText.CloneSourcePlaceholder);
        CreateLabel(UiText.CloneDestination);
        _destinationEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            22,
            NativeMethods.EditAutoHorizontalScroll);
        _ = NativeMethods.SendMessage(_destinationEdit, NativeMethods.EditSetCueBanner, 1, UiText.CloneDestinationPlaceholder);
        _shallowCheck = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ShallowClone,
            CommandShallow,
            NativeMethods.ButtonAutoCheckbox);
        _depthEdit = CreateControl(
            NativeMethods.EditClass,
            "1",
            23,
            NativeMethods.EditAutoHorizontalScroll | NativeMethods.EditNumber);
        _depthSuffixLabel = CreateControl(NativeMethods.StaticClass, UiText.CloneCommitUnit, 25, NativeMethods.StaticLeft);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 24, NativeMethods.StaticLeft);
        _cancelButton = CreateControl(NativeMethods.ButtonClass, UiText.Cancel, CommandCancel, NativeMethods.ButtonOwnerDraw);
        _cloneButton = CreateControl(NativeMethods.ButtonClass, UiText.Clone, CommandClone, NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandClose, NativeMethods.ButtonOwnerDraw);
        foreach (nint input in new[] { _body, _versionCombo, _sourceEdit, _destinationEdit, _depthEdit, _depthSuffixLabel, _shallowCheck })
            if (!NativeMethods.SetWindowSubclass(input, _inputProcedure, 2, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.CloneControlCreateFailed);
        _ = NativeAccessibility.SetName(_sourceEdit, UiText.CloneSource);
        _ = NativeAccessibility.SetName(_destinationEdit, UiText.CloneDestination);
        _ = NativeAccessibility.SetName(_depthEdit, UiText.ShallowClone);
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        _toolTip.Add(_noticeLabel, string.Empty);
        UpdateDepthState();
        Layout();
    }

    private void CreateLabel(string text)
    {
        _labels.Add(CreateControl(NativeMethods.StaticClass, text, 30 + _labels.Count, NativeMethods.StaticLeft));
    }

    private nint CreateControl(string className, string text, int identifier, uint style)
    {
        uint commonStyle = NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible;
        if (!className.Equals(NativeMethods.StaticClass, StringComparison.Ordinal))
        {
            commonStyle |= NativeMethods.WindowStyleTabStop;
        }

        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            commonStyle | style,
            0,
            0,
            0,
            0,
            identifier is CommandCancel or CommandClone or CommandClose ? _handle : _body,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.CloneControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        if (className.Equals(NativeMethods.EditClass, StringComparison.Ordinal))
        {
            _ = NativeMethods.SendMessage(
                control,
                NativeMethods.EditSetMargins,
                NativeMethods.EditMarginLeftRight,
                unchecked((nint)0x00080008));
        }

        return control;
    }

    private nint CommandMessage(nuint wordParameter, nint source)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (notification != 0 || source == 0 || !NativeMethods.IsWindowEnabled(source)) return 0;
        if (command == CommandShallow && notification == 0)
        {
            UpdateDepthState();
            return 0;
        }

        switch (command)
        {
            case CommandClone:
                StartClone();
                break;
            case CommandCancel:
            case CommandClose:
                CancelOrClose();
                break;
        }

        return 0;
    }

    private void UpdateDepthState()
    {
        bool enabled = !_operationRunning && IsShallowCloneSelected;
        _ = NativeMethods.EnableWindow(_depthEdit, enabled);
        _ = NativeMethods.EnableWindow(_depthSuffixLabel, enabled);
        _ = NativeMethods.InvalidateRectangle(_depthEdit, 0, true);
        _ = NativeMethods.InvalidateRectangle(_depthSuffixLabel, 0, true);
    }

    private bool IsShallowCloneSelected => NativeMethods.SendMessage(
        _shallowCheck,
        NativeMethods.ButtonMessageGetCheck,
        0,
        0) == unchecked((nint)NativeMethods.ButtonChecked);

    private void StartClone()
    {
        if (_closed || _operationRunning)
        {
            return;
        }

        string source = NativeMethods.GetWindowTextValue(_sourceEdit).Trim();
        string destination = NativeMethods.GetWindowTextValue(_destinationEdit).Trim();
        if (source.Length == 0 || destination.Length == 0)
        {
            ShowError(UiText.CloneFieldsRequired);
            _ = NativeMethods.SetFocus(source.Length == 0 ? _sourceEdit : _destinationEdit);
            return;
        }

        if (!TryParseDepthForTest(
                IsShallowCloneSelected,
                NativeMethods.GetWindowTextValue(_depthEdit),
                out int? depth))
        {
            ShowError(UiText.CloneDepthInvalid);
            _ = NativeMethods.SetFocus(_depthEdit);
            return;
        }

        CancellationTokenSource cancellation = new();
        _operationCancellation = cancellation;
        _operationRunning = true;
        SetOperationControlsEnabled(false);
        _ = NativeMethods.EnableWindow(_cancelButton, true);
        _ = NativeMethods.SetWindowText(_cancelButton, UiText.CancelOperation);
        _ = NativeMethods.SetWindowText(_cloneButton, UiText.Cloning);
        _ = NativeMethods.InvalidateRectangle(_cloneButton, 0, true);
        _ = NativeMethods.SetFocus(_cancelButton);
        SetNotice(UiText.Cloning);
        _operationTask = ExecuteCloneAsync(source, destination, depth, cancellation);
    }

    private async Task ExecuteCloneAsync(string source, string destination, int? depth, CancellationTokenSource cancellation)
    {
        GitRepositoryOperationResult result;
        try
        {
            result = await _repositoryService.CloneAsync(source, destination, depth, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = GitRepositoryOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled);
        }
        catch (Exception)
        {
            result = GitRepositoryOperationResult.Failure(GitOperationFailureKind.CommandFailed, UiText.CloneFailed);
        }
        // Win32 模态循环不提供同步上下文；后台仅投递结果，窗口线程接纳并绘制。
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

    private nint CompleteOperation()
    {
        GitRepositoryOperationResult? result;
        lock (_operationGate)
        {
            result = _pendingResult;
            if (_closed || result is null) return 0;
            _pendingResult = null;
            if (_operationCancellation?.IsCancellationRequested == true)
                result = GitRepositoryOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled, result.Repository);
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
        _operationRunning = false;
        if (result.IsSuccess && result.Repository is not null)
        {
            _result = result.Repository.WorkspacePath;
            SetNotice(UiText.CloneCompleted);
            Close(force: true);
            return 0;
        }
        SetOperationControlsEnabled(true);
        _ = NativeMethods.SetWindowText(_cancelButton, UiText.Cancel);
        _ = NativeMethods.SetWindowText(_cloneButton, UiText.Clone);
        _ = NativeMethods.InvalidateRectangle(_cloneButton, 0, true);
        UpdateDepthState();
        ShowError(result.ErrorMessage ?? UiText.CloneFailed);
        return 0;
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _versionCombo,
            _sourceEdit,
            _destinationEdit,
            _shallowCheck,
            _depthEdit,
            _depthSuffixLabel,
            _cloneButton,
            _headerCloseButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }
    }

    private void CancelOrClose()
    {
        if (_operationRunning)
        {
            _operationCancellation?.Cancel();
        }
        else
        {
            Close(force: true);
        }
    }

    private nint LayoutMessage() { Layout(); return 0; }

    private nint HitTest()
    {
        if (!NativeMethods.GetCursorPosition(out var point) || !NativeMethods.ScreenToClient(_handle, ref point)
            || !NativeMethods.GetClientRectangle(_handle, out var client)) return NativeMethods.HitTestClient;
        return point.Y < _headerHeight && point.X < client.Right - S(54) ? NativeMethods.HitTestCaption : NativeMethods.HitTestClient;
    }

    private nint CloseMessage()
    {
        CancelOrClose();
        return 0;
    }

    private void ShowError(string message) => SetNotice(message);

    private void Close(bool force)
    {
        nint handle;
        lock (_operationGate)
        {
            if (_closed || (!force && _operationRunning)) return;
            _closed = true;
            _operationCancellation?.Cancel();
            // 服务未结束时由后台收尾释放，销毁窗口后不接纳晚到的成功或失败。
            if (_pendingResult is not null) _operationCancellation?.Dispose();
            _operationCancellation = null;
            _pendingResult = null;
            handle = _handle;
            _handle = 0;
        }
        NativeMethods.WakeWindowMessageLoop(handle);
        NativeComboBoxTheme.Unregister(_versionCombo);
        foreach (nint input in new[] { _body, _versionCombo, _sourceEdit, _destinationEdit, _depthEdit, _depthSuffixLabel, _shallowCheck })
            if (input != 0) _ = NativeMethods.RemoveWindowSubclass(input, _inputProcedure, 2);
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    public void Dispose()
    {
        _toolTip?.Dispose();
        _toolTip = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        Close(force: true);
        GC.SuppressFinalize(this);
    }

    private nint DestroyedMessage()
    {
        Dispose();
        return 0;
    }

    internal nint HandleForTest => _handle;
    internal nint NoticeForTest => _noticeLabel;
    internal nint BodyForTest => _body;
    internal Task OperationTaskForTest => _operationTask;
    internal bool RunningForTest => _operationRunning;
    internal static int InstanceCountForTest { get { lock (InstancesGate) return Instances.Count; } }

    private static (int X, int Y) Center(nint owner, int width, int height)
    {
        if (!NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle rectangle))
        {
            return (NativeMethods.UseDefault, NativeMethods.UseDefault);
        }

        return (rectangle.Left + Math.Max(0, ((rectangle.Right - rectangle.Left) - width) / 2), rectangle.Top + Math.Max(0, ((rectangle.Bottom - rectangle.Top) - height) / 2));
    }

    private static int S(int pixels) => NativeTheme.Scale(pixels);

}
