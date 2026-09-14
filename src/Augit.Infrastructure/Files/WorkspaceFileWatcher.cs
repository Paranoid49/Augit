namespace Augit.Infrastructure.Files;

public sealed class WorkspaceFileWatcher : IDisposable
{
    private static readonly TimeSpan MergeDelay = TimeSpan.FromMilliseconds(50);
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
