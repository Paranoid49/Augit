using Augit.Core.Git;

namespace Augit.App;

internal sealed partial class NativeResetDialog
{
    private const uint OperationCompleted = NativeMethods.WindowMessageApp + 74;
    private static readonly NativeMethods.SubclassProcedure InputProcedure = HandleInputMessage;
    private readonly object _operationGate = new();
    private GitActionResult? _pendingResult;
    private Task _operationTask = Task.CompletedTask;
    private bool _composing;
    private int? _quitCode;

    internal nint HandleForTest => _handle;
    internal Task OperationTaskForTest => _operationTask;
    internal bool RunningForTest => _operationRunning;
    internal nint HoveredButtonForTest => _buttons?.Hovered ?? 0;

    private static nint HandleInputMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        NativeResetDialog? dialog;
        lock (InstancesGate) Instances.TryGetValue((nint)data, out dialog);
        if (message is 0x010D or 0x010E && dialog is not null) dialog._composing = message == 0x010D;
        if (message == NativeMethods.WindowMessageNonClientDestroy)
            _ = NativeMethods.RemoveWindowSubclass(window, InputProcedure, id);
        return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
    }

    internal bool HandleKey(NativeMethods.Message message)
    {
        if (_closed || _composing || message.MessageId != NativeMethods.WindowMessageKeyDown
            || !NativeFocusNavigation.ContainsWindow(_handle, message.Window)) return false;
        int key = unchecked((int)message.WordParameter);
        if (NativeMethods.SendMessage(_modeCombo, 0x0157, 0, 0) != 0
            && key is NativeMethods.VirtualKeyEnter or NativeMethods.VirtualKeyEscape) return false;
        if (key == NativeMethods.VirtualKeyTab)
        {
            MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
            return true;
        }
        if (key == NativeMethods.VirtualKeyEscape) { Close(); return true; }
        if (key != NativeMethods.VirtualKeyEnter || NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) < 0
            || NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0 || NativeMethods.GetKeyState(0x12) < 0) return false;
        if ((message.LongParameter.ToInt64() & (1L << 30)) != 0) return true;
        nint focus = NativeMethods.GetFocus();
        nint target = focus == _targetEdit || focus == _modeCombo ? _runButton : focus;
        if (target != _runButton && target != _cancelButton && target != _headerCloseButton) return false;
        if (NativeMethods.IsWindowEnabled(target) && NativeMethods.IsWindowVisible(target))
            _ = NativeMethods.SendMessage(target, 0x00F5, 0, 0);
        return true;
    }

    private async Task ExecuteResetAsync(string target, GitResetMode mode, CancellationTokenSource cancellation)
    {
        GitActionResult result;
        try { result = await _service.ResetAsync(_repository, target, mode, cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { result = GitActionResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled); }
        catch (Exception) { result = GitActionResult.Failure(GitOperationFailureKind.CommandFailed, UiText.ResetOperationFailed); }
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
        GitActionResult? result;
        lock (_operationGate)
        {
            result = _pendingResult;
            if (_closed || result is null) return 0;
            _pendingResult = null;
            // 已完成的真实 Git 结果优先，不能把成功写操作伪装成回滚或取消。
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
        _operationRunning = false;
        if (result.IsSuccess)
        {
            _changed = true;
            _setStatus(UiText.ResetCompleted);
            Dispose();
        }
        else
        {
            SetOperationControlsEnabled(true);
            _ = NativeMethods.SetWindowText(_cancelButton, UiText.Cancel);
            UpdateModePresentation();
            ShowError(result.ErrorMessage ?? UiText.ResetOperationFailed);
        }
        return 0;
    }

    private nint DestroyMessage(nint window, uint message, nuint word, nint parameter)
    {
        Dispose();
        return NativeMethods.DefaultWindowProcedure(window, message, word, parameter);
    }
}
