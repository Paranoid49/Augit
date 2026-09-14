namespace Augit.App;

internal sealed partial class NativeImageView
{
    private const uint ResampleMessage = NativeMethods.WindowMessageApp + 1;
    private static readonly SemaphoreSlim ResampleGate = new(1, 1);
    private static readonly object BackgroundGate = new();
    private static readonly ManualResetEventSlim BackgroundIdle = new(true);
    private static int _backgroundCount;
    private readonly object _sampleGate = new();
    private SampleJob? _sampleJob;
    private WicBitmap? _sample;
    private SampleRequest? _sampleRequest;
    private Task _sampleWorkers = Task.CompletedTask;

    internal Task SampleWorkersForTest => _sampleWorkers;
    internal bool SamplingForTest { get { lock (_sampleGate) return _sampleJob is { Done: false } or { Ready: not null }; } }
    internal bool HasSmoothSampleForTest => _sample is not null;

    private sealed record SampleRequest(WicBitmap Source, int X, int Y, int Width, int Height, int ViewWidth, int ViewHeight);
    private sealed class SampleJob(SampleRequest request)
    {
        internal SampleRequest Request { get; } = request;
        internal IDisposable Lease { get; } = request.Source.Retain();
        internal CancellationTokenSource Cancellation { get; } = new();
        internal bool Done { get; set; }
        internal WicBitmap? Ready { get; set; }
    }

    private SampleRequest? CurrentSampleRequest()
    {
        if (_bitmap is null || !NativeMethods.IsWindowVisible(Handle)
            || !NativeMethods.GetClientRectangle(Handle, out var client) || client.Right <= 0 || client.Bottom <= 0) return null;
        var bounds = CurrentImageBounds;
        if (bounds.Width >= _bitmap.Width && bounds.Height >= _bitmap.Height) return null;
        return new(_bitmap, bounds.X, bounds.Y, bounds.Width, bounds.Height, client.Right, client.Bottom);
    }

    private bool TryDrawSample(nint dc)
    {
        SampleRequest? request = CurrentSampleRequest();
        if (request is null) { CancelSampling(); return false; }
        if (_sampleRequest != request || _sample is null)
        {
            lock (_sampleGate)
            {
                if (_sampleJob?.Request == request) return false;
                CancelSampling();
                SampleJob job = new(request);
                _sampleJob = job;
                lock (BackgroundGate) { _backgroundCount++; BackgroundIdle.Reset(); }
                Task worker = RunSamplingAsync(job);
                _sampleWorkers = _sampleWorkers.IsCompleted ? worker : Task.WhenAll(_sampleWorkers, worker);
            }
            return false;
        }
        nint memory = NativeMethods.CreateCompatibleDeviceContext(dc);
        if (memory == 0) return false;
        nint previous = NativeMethods.SelectObject(memory, _sample.Handle);
        try
        {
            return AlphaBlend(dc, Math.Max(0, request.X), Math.Max(0, request.Y), _sample.Width, _sample.Height,
                memory, 0, 0, _sample.Width, _sample.Height, new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 });
        }
        finally
        {
            _ = NativeMethods.SelectObject(memory, previous);
            _ = NativeMethods.DeleteDeviceContext(memory);
        }
    }

    private Task RunSamplingAsync(SampleJob job) => Task.Run(async () =>
    {
        WicBitmap? result = null;
        CancellationToken token = job.Cancellation.Token;
        try
        {
            await ResampleGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                SampleRequest request = job.Request;
                result = NativeGdiPlusDrawing.CreateReducedImage(request.Source, request.X, request.Y,
                    request.Width, request.Height, request.ViewWidth, request.ViewHeight);
            }
            finally { ResampleGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
        finally
        {
            job.Lease.Dispose();
            lock (_sampleGate)
            {
                job.Done = true;
                job.Cancellation.Dispose();
                if (!_disposed && ReferenceEquals(_sampleJob, job))
                {
                    job.Ready = result;
                    result = null;
                    if (!NativeMethods.PostMessage(Handle, ResampleMessage, 0, 0)) CancelSampling();
                }
            }
            result?.Dispose();
            lock (BackgroundGate) { if (--_backgroundCount == 0) BackgroundIdle.Set(); }
        }
    });

    internal static bool WaitForBackgroundSampling() => BackgroundIdle.Wait(TimeSpan.FromSeconds(5));

    private void ApplySample()
    {
        lock (_sampleGate)
        {
            if (_disposed || _sampleJob is not { Done: true, Ready: not null } job) return;
            if (job.Request != CurrentSampleRequest()) { CancelSampling(); return; }
            _sample?.Dispose();
            _sample = job.Ready;
            job.Ready = null;
            _sampleRequest = job.Request;
        }
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    private void CancelSampling()
    {
        lock (_sampleGate)
        {
            if (_sampleJob is { } job)
            {
                _sampleJob = null;
                if (!job.Done) job.Cancellation.Cancel();
                job.Ready?.Dispose();
                job.Ready = null;
            }
            _sample?.Dispose();
            _sample = null;
            _sampleRequest = null;
        }
    }
}
