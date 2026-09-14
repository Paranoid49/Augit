using Augit.Core.Git;

namespace Augit.App;

internal sealed partial class NativeWorktreeManagerDialog
{
    private const uint WorkCompleted = NativeMethods.WindowMessageApp + 79;
    private readonly object _workGate = new();
    private readonly WorkSlot _readWork = new(), _inspectWork = new(), _writeWork = new();
    private static readonly NativeMethods.SubclassProcedure InputProcedure = HandleInputMessage;
    private bool _composing;
    private int? _quitCode;

    private sealed class WorkSlot
    {
        internal CancellationTokenSource? Cancellation;
        internal Action? Pending;
        internal Task Task = Task.CompletedTask;
    }

    internal Task WorkForTest => Task.WhenAll(_readWork.Task, _inspectWork.Task, _writeWork.Task);
    internal bool BusyForTest => _operationRunning || _readWork.Cancellation is not null || _inspectWork.Cancellation is not null;
    internal static int InstanceCountForTest { get { lock (InstancesGate) return Instances.Count; } }

    private Task StartWork<T>(WorkSlot slot, Func<CancellationToken, Task<T>> execute, Action<T> accept, Func<Exception, T> failure)
    {
        CancelWork(slot);
        if (_closed) return Task.CompletedTask;
        CancellationTokenSource cancellation = new();
        lock (_workGate) slot.Cancellation = cancellation;
        return slot.Task = ExecuteAsync();

        async Task ExecuteAsync()
        {
            T result;
            try { result = await execute(cancellation.Token).ConfigureAwait(false); }
            catch (Exception exception) { result = failure(exception); }
            lock (_workGate)
            {
                if (!_closed && ReferenceEquals(slot.Cancellation, cancellation))
                {
                    slot.Pending = () => accept(result);
                    _ = NativeMethods.PostMessage(_handle, WorkCompleted, 0, 0);
                }
                else cancellation.Dispose();
            }
        }
    }

    private void CancelWork(WorkSlot slot)
    {
        lock (_workGate)
        {
            slot.Cancellation?.Cancel();
            if (slot.Pending is not null) slot.Cancellation?.Dispose();
            slot.Cancellation = null;
            slot.Pending = null;
        }
    }

    private nint CompleteWork()
    {
        foreach (WorkSlot slot in new[] { _readWork, _inspectWork, _writeWork })
        {
            Action? apply;
            lock (_workGate)
            {
                if (_closed) break;
                apply = slot.Pending;
                if (apply is null) continue;
                slot.Pending = null;
                slot.Cancellation?.Dispose();
                slot.Cancellation = null;
            }
            apply();
        }
        return 0;
    }

    private static GitOperationFailureKind FailureKind(Exception exception) => exception is OperationCanceledException
        ? GitOperationFailureKind.Cancelled : GitOperationFailureKind.CommandFailed;

    private static string FailureMessage(Exception exception) => exception is OperationCanceledException
        ? UiText.OperationCancelled : "Worktree 操作失败，请检查仓库状态后重试。";

    private void RequestCancellation()
    {
        lock (_workGate)
        {
            if (!_operationRunning || _writeWork.Cancellation is not { IsCancellationRequested: false } cancellation) return;
            cancellation.Cancel();
        }
        _ = NativeMethods.EnableWindow(_cancelOperationButton, false);
        SetNotice("正在取消 Worktree 操作，等待 Git 停止…");
    }

    private static nint HandleInputMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        NativeWorktreeManagerDialog? dialog;
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
        if (key == NativeMethods.VirtualKeyTab) { MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0); return true; }
        if (key == NativeMethods.VirtualKeyEscape) { Close(); return true; }
        if (key != NativeMethods.VirtualKeyEnter || NativeMethods.GetKeyState(0x12) < 0) return false;
        if ((message.LongParameter.ToInt64() & (1L << 30)) != 0) return true;
        nint focus = NativeMethods.GetFocus();
        nint target = focus == _pathEdit || focus == _branchEdit || focus == _newBranchEdit ? _createButton : focus;
        nint[] buttons = [_addButton, _deleteToolbarButton, _refreshButton, _openButton, _newButton,
            _createButton, _removeButton, _cancelOperationButton, _closeButton, _headerCloseButton];
        if (!buttons.Contains(target)) return false;
        if (NativeMethods.IsWindowEnabled(target) && NativeMethods.IsWindowVisible(target))
            _ = NativeMethods.SendMessage(target, 0x00F5, 0, 0);
        return true;
    }

    private nint DestroyMessage(nint window, uint message, nuint word, nint parameter)
    {
        Dispose();
        return NativeMethods.DefaultWindowProcedure(window, message, word, parameter);
    }
}
