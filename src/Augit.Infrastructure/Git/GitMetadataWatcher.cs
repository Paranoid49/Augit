using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitMetadataWatcher : IDisposable
{
    private static readonly TimeSpan MergeDelay = TimeSpan.FromMilliseconds(50);
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _timer;
    private bool _disposed;
    private bool _changePending;

    public GitMetadataWatcher(GitRepositorySnapshot repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _timer = new(Flush, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        IEnumerable<string> directories = new[] { repository.GitDirectory, repository.GitCommonDirectory }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories)
        {
            FileSystemWatcher watcher = new(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _changePending = false;
            foreach (FileSystemWatcher watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnChanged;
                watcher.Created -= OnChanged;
                watcher.Deleted -= OnChanged;
                watcher.Renamed -= OnChanged;
                watcher.Error -= OnError;
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        _timer.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs eventArgs)
    {
        QueueChange();
    }

    private void OnError(object sender, ErrorEventArgs eventArgs)
    {
        QueueChange();
    }

    private void QueueChange()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _changePending = true;
            _timer.Change(MergeDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush(object? state)
    {
        lock (_gate)
        {
            if (_disposed || !_changePending)
            {
                return;
            }

            _changePending = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
