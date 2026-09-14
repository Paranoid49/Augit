using Augit.Core.Git;

namespace Augit.App;

internal sealed partial class NativeRemoteDialog
{
    private const uint ReadCompleted = NativeMethods.WindowMessageApp + 75;
    private const uint WriteCompleted = NativeMethods.WindowMessageApp + 76;
    private readonly object _workGate = new();
    private CancellationTokenSource? _readCancellation;
    private GitRemoteListResult? _pendingRead;
    private GitRemoteOperationResult? _pendingWrite;
    private Task _readTask = Task.CompletedTask, _writeTask = Task.CompletedTask;
    private int? _quitCode;

    internal Task WorkForTest => Task.WhenAll(_readTask, _writeTask);
    internal bool BusyForTest => _operationRunning || _readCancellation is not null;
    internal nint RemoteListForTest => _remoteList;
    internal string NoticeForTest => _notice;
    internal static int InstanceCountForTest { get { lock (InstancesGate) return Instances.Count; } }
    internal static NativeRemoteDialog? FindForTest(nint handle)
    {
        lock (InstancesGate) return Instances.GetValueOrDefault(handle);
    }
    internal static NativeRemoteDialog? FindForOwnerForTest(nint owner)
    {
        lock (InstancesGate) return Instances.Values.FirstOrDefault(dialog => dialog._owner == owner);
    }

    private async Task ExecuteReadAsync(CancellationTokenSource cancellation)
    {
        GitRemoteListResult result;
        try { result = await _service.ReadRemotesAsync(_repository, cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { result = GitRemoteListResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled); }
        catch (Exception) { result = GitRemoteListResult.Failure(GitOperationFailureKind.CommandFailed, UiText.ReadRemotesFailed); }
        lock (_workGate)
        {
            if (!_closed && ReferenceEquals(_readCancellation, cancellation))
            {
                _pendingRead = result;
                _ = NativeMethods.PostMessage(_handle, ReadCompleted, 0, 0);
            }
            else cancellation.Dispose();
        }
    }

    private nint CompleteRead()
    {
        GitRemoteListResult? result;
        lock (_workGate)
        {
            result = _pendingRead;
            if (_closed || result is null) return 0;
            _pendingRead = null;
            _readCancellation?.Dispose();
            _readCancellation = null;
        }
        if (!result.IsSuccess) ShowError(result.ErrorMessage ?? UiText.ReadRemotesFailed);
        else
        {
            ShowRemotes(result.Remotes!);
            SetNotice(string.Empty);
        }
        return 0;
    }

    private void CancelRead()
    {
        lock (_workGate)
        {
            _readCancellation?.Cancel();
            if (_pendingRead is not null) _readCancellation?.Dispose();
            _readCancellation = null;
            _pendingRead = null;
        }
        if (!_closed && _notice == UiText.ReadingRemotes) SetNotice(string.Empty);
    }

    private Task StartWrite(Func<CancellationToken, Task<GitRemoteOperationResult>> operation)
    {
        if (!BeginOperation()) return Task.CompletedTask;
        return _writeTask = ExecuteWriteAsync(operation, _operationCancellation!);
    }

    private async Task ExecuteWriteAsync(Func<CancellationToken, Task<GitRemoteOperationResult>> operation, CancellationTokenSource cancellation)
    {
        GitRemoteOperationResult result;
        try { result = await operation(cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { result = GitRemoteOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled); }
        catch (Exception) { result = GitRemoteOperationResult.Failure(GitOperationFailureKind.CommandFailed, UiText.RemoteOperationFailed); }
        // 后台结束不依赖主窗口仍存在；只有当前对话框接纳身份匹配的结果。
        lock (_workGate)
        {
            if (!_closed && ReferenceEquals(_operationCancellation, cancellation))
            {
                _pendingWrite = result;
                _ = NativeMethods.PostMessage(_handle, WriteCompleted, 0, 0);
            }
            else cancellation.Dispose();
        }
    }

    private nint CompleteWrite()
    {
        GitRemoteOperationResult? result;
        lock (_workGate)
        {
            result = _pendingWrite;
            if (_closed || result is null) return 0;
            _pendingWrite = null;
            if (_operationCancellation?.IsCancellationRequested == true)
                result = GitRemoteOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled, result.ActualRemotes, result.ActualStatus);
        }
        try { CompleteOperation(result); }
        finally { EndOperation(); }
        return 0;
    }

    private void RequestCancellation()
    {
        if (!_operationRunning || _operationCancellation?.IsCancellationRequested != false) return;
        _operationCancellation.Cancel();
        SetNotice("正在取消远端操作，等待 Git 停止…");
    }
}
