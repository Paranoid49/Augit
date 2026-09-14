using System.Runtime.InteropServices;
using Augit.Core.Documents;

namespace Augit.App;

internal sealed partial class NativeDocumentView
{
    private const uint ImageDispatchMessage = NativeMethods.WindowMessageApp + 32;
    private readonly object _imageGate = new();
    private readonly NativeImageDecoder _imageDecoder;
    private ImageLoadJob? _imageJob;
    private Task _imageWorkers = Task.CompletedTask;
    internal Task ImageLoadCompletion => _imageJob?.Completion.Task ?? Task.CompletedTask;
    internal Task ImageWorkersForTest => Task.WhenAll(_imageWorkers, _imageView?.SampleWorkersForTest ?? Task.CompletedTask);

    private void PreserveImageWorkers()
    {
        if (_imageView is not null) _imageWorkers = Task.WhenAll(_imageWorkers, _imageView.SampleWorkersForTest);
    }
    internal bool ImageLoadingForTest => _imageJob is not null;

    private sealed class ImageLoadJob : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private bool _disposed;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal WicBitmap? Bitmap { get; set; }
        internal bool Complete { get; set; }
        internal bool ShowProgress { get; set; }
        internal CancellationToken Token { get { lock (_gate) return _cancellation.Token; } }
        internal void Cancel() { lock (_gate) { if (!_disposed) _cancellation.Cancel(); } }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _cancellation.Dispose();
            }
        }
    }

    private bool TryReloadImage(DocumentReadResult result)
    {
        if (_imageView is null || result.Status != DocumentReadStatus.ImageReady
            || _result.Classification.Kind != result.Classification.Kind
            || !_result.RequestedPath.Equals(result.RequestedPath, StringComparison.OrdinalIgnoreCase)
            || !_result.ResolvedPath.Equals(result.ResolvedPath, StringComparison.OrdinalIgnoreCase)) return false;
        _result = result;
        ReadStatusText = result.Message.Length == 0 ? result.Classification.TypeName : result.Message;
        BeginImageLoad();
        return true;
    }

    private void BeginImageLoad()
    {
        CancelImageLoad();
        _imageView?.SetLoading(false);
        ImageLoadJob job = new();
        lock (_imageGate) _imageJob = job;
        // 图片结果不携带内容；相同路径、大小及读取记录也必须重新解码。
        Task worker = LoadImageAsync(job, _result.ResolvedPath);
        _imageWorkers = _imageWorkers.IsCompleted ? worker : Task.WhenAll(_imageWorkers, worker);
    }

    private async Task LoadImageAsync(ImageLoadJob job, string path)
    {
        CancellationToken token = job.Token;
        Task progress = RevealImageProgressAsync(job, token);
        WicBitmap? bitmap = null;
        try
        {
            bitmap = await _imageDecoder.LoadAsync(path, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException or InvalidOperationException) { }
        finally
        {
            job.Cancel();
            await progress.ConfigureAwait(false);
            lock (_imageGate)
            {
                if (!_disposed && ReferenceEquals(_imageJob, job))
                {
                    job.Bitmap = bitmap;
                    bitmap = null;
                    job.Complete = true;
                    if (!NativeMethods.PostMessage(Handle, ImageDispatchMessage, 0, 0)) CancelImageLoad();
                }
            }
            bitmap?.Dispose();
            job.Dispose();
        }
    }

    private async Task RevealImageProgressAsync(ImageLoadJob job, CancellationToken token)
    {
        try
        {
            await Task.Delay(150, token).ConfigureAwait(false);
            lock (_imageGate)
            {
                if (_disposed || !ReferenceEquals(_imageJob, job)) return;
                job.ShowProgress = true;
                _ = NativeMethods.PostMessage(Handle, ImageDispatchMessage, 0, 0);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyImageLoad()
    {
        ImageLoadJob? job;
        WicBitmap? bitmap;
        lock (_imageGate)
        {
            job = _imageJob;
            if (_disposed || job is null) return;
            if (!job.Complete)
            {
                if (job.ShowProgress) _imageView?.SetLoading(true);
                return;
            }
            _imageJob = null;
            bitmap = job.Bitmap;
            job.Bitmap = null;
        }
        try
        {
            if (bitmap is null)
            {
                DisposeContentControls();
                ReadStatusText = UiText.ImageDecodeFailed;
                ShowSummary(ReadStatusText);
                _infoView?.ApplyAppearance(NativeTheme.IsDark(_settings.Theme));
            }
            else
            {
                _imageView!.SetBitmap(bitmap, preserveView: true);
                bitmap = null;
                _imageView.SetLoading(false);
                _ = NativeMethods.SetWindowText(_imageSizeLabel,
                    $"{_imageView.BitmapWidth} × {_imageView.BitmapHeight} · {_result.Classification.TypeName} · {FormatFileSize(_result.FileSize)}");
                _imageToolTip?.Update(_imageSizeLabel, NativeMethods.GetWindowTextValue(_imageSizeLabel));
            }
            Layout();
        }
        finally
        {
            bitmap?.Dispose();
            job.Completion.TrySetResult();
        }
    }

    private void CancelImageLoad()
    {
        lock (_imageGate)
        {
            if (_imageJob is not { } job) return;
            _imageJob = null;
            job.Cancel();
            job.Bitmap?.Dispose();
            job.Bitmap = null;
            job.Completion.TrySetResult();
        }
    }
}
