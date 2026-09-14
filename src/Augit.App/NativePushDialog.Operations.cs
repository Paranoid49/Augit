using Augit.Core.Git;

namespace Augit.App;

internal sealed partial class NativePushDialog
{
    private const uint PreviewCompleted = NativeMethods.WindowMessageApp + 73;
    private const uint PushCompleted = NativeMethods.WindowMessageApp + 74;
    private readonly object _workGate = new();
    private CancellationTokenSource? _previewCancellation;
    private GitPushPreviewResult? _pendingPreview;
    private GitRemoteOperationResult? _pendingPush;
    private Task _previewTask = Task.CompletedTask;
    private Task _pushTask = Task.CompletedTask;
    private int? _quitCode;

    internal nint HandleForTest => _handle;
    internal bool RunningForTest => _operationRunning;
    internal bool PreviewCompletedForTest => _previewLoadCompleted;
    internal Task WorkForTest => Task.WhenAll(_previewTask, _pushTask);
    internal string NoticeForTest => _notice;
    internal string SummaryTextForTest => _summaryText;
    internal static int InstanceCountForTest
    {
        get { lock (InstancesGate) return Instances.Count; }
    }


    private void StartPreview()
    {
        if (_closed || _operationRunning || _previewCancellation is not null) return;
        _restoreFocusAfterPreview = NativeMethods.GetFocus() == _defineRemoteButton;
        _previewLoadCompleted = false;
        _preview = null;
        _ = NativeMethods.EnableWindow(_pushButton, false);
        _ = NativeMethods.EnableWindow(_defineRemoteButton, false);
        SetNotice(UiText.ReadingPushPreview);
        CancellationTokenSource cancellation = new();
        _previewCancellation = cancellation;
        _previewTask = ExecutePreviewAsync(cancellation);
    }

    private async Task ExecutePreviewAsync(CancellationTokenSource cancellation)
    {
        GitPushPreviewResult result;
        try { result = await _service.ReadPushPreviewAsync(_repository, _localReference, cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { result = GitPushPreviewResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled); }
        catch (Exception) { result = GitPushPreviewResult.Failure(GitOperationFailureKind.CommandFailed, "无法读取待推送提交。"); }
        // 模态消息循环不保证有同步上下文，后台只投递结果，不读写窗口。
        lock (_workGate)
        {
            if (!_closed && ReferenceEquals(_previewCancellation, cancellation))
            {
                _pendingPreview = result;
                _ = NativeMethods.PostMessage(_handle, PreviewCompleted, 0, 0);
            }
            else cancellation.Dispose();
        }
    }

    private nint CompletePreview()
    {
        GitPushPreviewResult? result;
        lock (_workGate)
        {
            result = _pendingPreview;
            if (_closed || result is null) return 0;
            _pendingPreview = null;
            _previewCancellation?.Dispose();
            _previewCancellation = null;
        }
        _ = NativeMethods.EnableWindow(_defineRemoteButton, true);
        ApplyPreview(result);
        if (_restoreFocusAfterPreview)
        {
            _restoreFocusAfterPreview = false;
            // Define remote 可能因新结果被隐藏；不能把焦点留在已禁用按钮上。
            if (NativeMethods.GetFocus() == 0)
                _ = NativeMethods.SetFocus(
                    NativeMethods.IsWindowVisible(_defineRemoteButton) && NativeMethods.IsWindowEnabled(_defineRemoteButton)
                        ? _defineRemoteButton
                        : _commitList);
        }
        return 0;
    }

    private void StartPush()
    {
        if (_closed || _operationRunning || !_previewLoadCompleted || _preview is not { } preview || _commits.Count == 0) return;
        CancellationTokenSource cancellation = new();
        _operationCancellation = cancellation;
        _operationRunning = true;
        SetOperationControlsEnabled(false);
        _ = NativeMethods.SetWindowText(_cancelButton, UiText.CancelOperation);
        _ = NativeMethods.SetWindowText(_pushButton, UiText.Pushing);
        _ = NativeMethods.SetFocus(_cancelButton);
        SetNotice(UiText.Pushing);
        _pushTask = ExecutePushAsync(preview, cancellation);
    }

    private async Task ExecutePushAsync(GitPushPreview preview, CancellationTokenSource cancellation)
    {
        GitRemoteOperationResult result;
        try
        {
            result = await _service.PushRefAsync(_repository, preview.RemoteName, preview.LocalReference,
                preview.RemoteReference, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { result = GitRemoteOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled); }
        catch (Exception) { result = GitRemoteOperationResult.Failure(GitOperationFailureKind.CommandFailed, UiText.PushFailed); }
        lock (_workGate)
        {
            if (!_closed && ReferenceEquals(_operationCancellation, cancellation))
            {
                _pendingPush = result;
                _ = NativeMethods.PostMessage(_handle, PushCompleted, 0, 0);
            }
            else cancellation.Dispose();
        }
    }

    private nint CompletePush()
    {
        GitRemoteOperationResult? result;
        lock (_workGate)
        {
            result = _pendingPush;
            if (_closed || result is null) return 0;
            _pendingPush = null;
            if (_operationCancellation?.IsCancellationRequested == true)
                result = GitRemoteOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled, result.ActualRemotes, result.ActualStatus);
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
        _operationRunning = false;
        if (result.IsSuccess)
        {
            _setStatus(UiText.PushCompleted);
            Close();
            return 0;
        }
        SetOperationControlsEnabled(true);
        _ = NativeMethods.SetWindowText(_cancelButton, UiText.Cancel);
        _ = NativeMethods.SetWindowText(_pushButton, UiText.Push);
        SetNotice(result.ErrorMessage ?? UiText.PushFailed, error: result.FailureKind != GitOperationFailureKind.Cancelled);
        return 0;
    }

    private void RequestCancellation()
    {
        if (!_operationRunning || _operationCancellation?.IsCancellationRequested != false) return;
        _operationCancellation.Cancel();
        SetNotice("正在取消推送，等待 Git 停止…");
    }

    private nint DestroyedMessage()
    {
        // 外部销毁窗口时保留原句柄交给 Close 收尾，以便唤醒独立模态消息循环。
        Dispose();
        return 0;
    }
}
