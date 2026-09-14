using Augit.Core.Git;

namespace Augit.App;

internal sealed partial class NativeConflictResolverDialog
{
    private CancellationTokenSource? _reloadCancellation;
    private long _externalChangeVersion;

    private void CancelExternalReload()
    {
        CancellationTokenSource? pending = _reloadCancellation;
        _reloadCancellation = null;
        Interlocked.Exchange(ref _externalChangePending, 0);
        pending?.Cancel();
        // 取消源由发起读取的方法释放，保存和关闭不等待外部读取退出。
    }

    private bool CanApplyReload(CancellationTokenSource request) =>
        !_closed && !_saving && !_saveInProgress && ReferenceEquals(_reloadCancellation, request);

    private async Task HandleExternalChangeAsync()
    {
        if (_closed || _saving || _saveInProgress || _document is null || _reloadCancellation is not null) return;
        using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _reloadCancellation = request;
        long observedVersion = Interlocked.Read(ref _externalChangeVersion);
        try
        {
            while (CanApplyReload(request))
            {
                observedVersion = Interlocked.Read(ref _externalChangeVersion);
                GitConflictLoadResult loaded = await _service.ReloadWorkingFileAsync(_repository, _document, request.Token);
                if (!CanApplyReload(request)) return;
                // 读取或确认期间再次写入时，旧结果不覆盖当前正文，继续读取最新磁盘事实。
                if (observedVersion != Interlocked.Read(ref _externalChangeVersion)) continue;
                if (!loaded.IsSuccess || loaded.Document is null)
                {
                    SetNotice(loaded.ErrorMessage ?? UiText.GitUnavailable);
                    return;
                }
                if (loaded.Document.FileVersion == _document.FileVersion) return;

                bool keepCurrent = IsResultDirty && !NativeActionConfirmationDialog.Show(
                    _handle, _settings, "重新载入冲突文件", "外部内容已经改变",
                    UiText.ExternalConflictChanged, "重新载入外部内容", danger: true, cancelLabel: "保留当前内容");
                if (!CanApplyReload(request)) return;
                if (observedVersion != Interlocked.Read(ref _externalChangeVersion)) continue;

                if (keepCurrent)
                {
                    // 保留正文控件、撤销栈、选区与滚动，只更新用户已确认的磁盘版本。
                    _document = loaded.Document;
                    _resultDiffersFromDisk = true;
                    CancelPresentation();
                    _resultPresentationPending = true;
                    RefreshResultPresentation();
                }
                else
                {
                    ApplyDocument(loaded.Document, replaceResult: true);
                }
                _resultPresentationPending = _presentationJob is not null;
                _resultChangeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                string notice = keepCurrent
                    ? "已保留当前未保存内容，后续保存将覆盖刚确认的外部版本。"
                    : "已重新载入外部冲突文件内容。";
                if (_resultPresentationPending) _presentationCompletionNotice = notice;
                SetNotice(notice);
                return;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (CanApplyReload(request) && observedVersion == Interlocked.Read(ref _externalChangeVersion)) SetNotice(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_reloadCancellation, request))
            {
                _reloadCancellation = null;
                Interlocked.Exchange(ref _externalChangePending, 0);
                if (!_closed && observedVersion != Interlocked.Read(ref _externalChangeVersion)
                    && Interlocked.Exchange(ref _externalChangePending, 1) == 0)
                {
                    _ = NativeMethods.PostMessage(_handle, WindowMessageExternalChange, 0, 0);
                }
            }
        }
    }
}
