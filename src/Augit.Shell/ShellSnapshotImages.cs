namespace Augit.Shell;

/// <summary>按图片版本保存响应；迟到请求不得读到新版本或已释放的流。</summary>
internal sealed class ShellSnapshotImages(byte[] initial) : IDisposable
{
    private readonly Dictionary<long, byte[]> _images = new() { [0] = initial };
    private readonly List<(long Revision, MemoryStream Stream)> _responses = [];
    private bool _disposed;
    private byte[]? _pending;
    private readonly Dictionary<long, TaskCompletionSource<bool>> _loadedWaiters = [];
    internal long Revision { get; private set; }
    internal long LoadedRevision { get; private set; } = -1;
    internal string Hash { get; private set; } = HashImage(initial);
    internal long Bytes => _images.Values.Sum(image => (long)image.Length) + (_pending?.Length ?? 0);

    /// <summary>最多保留显示图、在途图和最新待发送图，加载较慢时合并中间版本。</summary>
    internal long? Offer(byte[] image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (HashImage(image) == Hash) { _pending = null; return null; }
        if (LoadedRevision < Revision) { _pending = image; return null; }
        return Add(image);
    }

    internal long? TakePending()
    {
        if (_disposed || _pending is null || LoadedRevision < Revision) return null;
        byte[] image = _pending;
        _pending = null;
        return Add(image);
    }

    internal long Add(byte[] image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string hash = HashImage(image);
        if (hash == Hash) return Revision;
        _images.Add(++Revision, image);
        Hash = hash;
        return Revision;
    }

    internal MemoryStream? Open(long revision)
    {
        if (_disposed || !_images.TryGetValue(revision, out byte[]? image)) return null;
        // 每次响应独立游标，但共享只读字节数组，不复制PNG。
        MemoryStream stream = new(image, writable: false);
        _responses.Add((revision, stream));
        return stream;
    }

    // 回调在窗口线程完成；不能异步派发续体到线程池后访问WebView2。
    internal Task<bool> WaitForLoadedAsync(long revision)
    {
        if (_disposed || revision < LoadedRevision) return Task.FromResult(false);
        if (revision == LoadedRevision) return Task.FromResult(true);
        if (!_loadedWaiters.TryGetValue(revision, out TaskCompletionSource<bool>? waiter))
            _loadedWaiters.Add(revision, waiter = new());
        return waiter.Task;
    }

    internal void FailLoading()
    {
        TaskCompletionSource<bool>[] waiters = [.. _loadedWaiters.Values];
        _loadedWaiters.Clear();
        foreach (TaskCompletionSource<bool> waiter in waiters) waiter.TrySetResult(false);
    }

    internal void Loaded(long revision)
    {
        if (_disposed || revision != Revision) return;
        LoadedRevision = revision;
        foreach (long old in _images.Keys.Where(old => old < revision).ToArray()) _images.Remove(old);
        foreach (var response in _responses.Where(response => response.Revision < revision).ToArray())
        {
            response.Stream.Dispose();
            _responses.Remove(response);
        }
        // 先完成清理并移除登记，再允许窗口线程续体重入关闭或下一轮刷新。
        var completed = _loadedWaiters.Where(entry => entry.Key <= revision).ToArray();
        foreach (var entry in completed) _loadedWaiters.Remove(entry.Key);
        foreach (var entry in completed) entry.Value.TrySetResult(entry.Key == revision);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var response in _responses) response.Stream.Dispose();
        _responses.Clear();
        _images.Clear();
        _pending = null;
        FailLoading();
    }

    private static string HashImage(byte[] image) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image));
}
