namespace Augit.Infrastructure.Files;

public sealed class WorkspaceFileWatcher : IDisposable
{
    // 合并窗口只用于吸收同一次保存产生的重复事件；20ms 仍能合并连续事件，
    // 同时把外部文件变化到状态读取的固定等待从 50ms 降到 20ms（500ms 刷新目标）。
    private static readonly TimeSpan MergeDelay = TimeSpan.FromMilliseconds(20);
    private readonly object _gate = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _gitMetadataRoot;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private bool _disposed;

    public WorkspaceFileWatcher(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        _gitMetadataRoot = Path.Combine(fullWorkspaceRoot, ".git");
        _timer = new(Flush, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher = new(fullWorkspaceRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
    }

    public event EventHandler<FileChangeBatchEventArgs>? Changed;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Deleted -= OnChanged;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _pendingPaths.Clear();
        }

        _watcher.Dispose();
        _timer.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs eventArgs)
    {
        Queue(eventArgs.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs eventArgs)
    {
        Queue(eventArgs.OldFullPath);
        Queue(eventArgs.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs eventArgs)
    {
        Queue(string.Empty);
    }

    private void Queue(string path)
    {
        if (IsGitMetadataPath(path))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingPaths.Add(path);
            _timer.Change(MergeDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private bool IsGitMetadataPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            return fullPath.Equals(_gitMetadataRoot, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(
                    _gitMetadataRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Flush(object? state)
    {
        string[] paths;
        lock (_gate)
        {
            if (_disposed || _pendingPaths.Count == 0)
            {
                return;
            }

            paths = [.. _pendingPaths];
            _pendingPaths.Clear();
        }

        Changed?.Invoke(this, new(paths));
    }
}

public sealed class FileChangeBatchEventArgs(IReadOnlyList<string> paths) : EventArgs
{
    public IReadOnlyList<string> Paths { get; } = paths;
}
