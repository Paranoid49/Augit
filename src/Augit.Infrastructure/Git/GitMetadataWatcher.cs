using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitMetadataWatcher : IDisposable
{
    private static readonly TimeSpan MergeDelay = TimeSpan.FromMilliseconds(50);
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _timer;
    private readonly Action<string>? _stage;
    private bool _disposed;
    private bool _changePending;
    private bool _batchObserved;

    public GitMetadataWatcher(GitRepositorySnapshot repository, Action<string>? stage = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _stage = stage;
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
        bool firstEvent;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _changePending = true;
            firstEvent = !_batchObserved;
            _batchObserved = true;
            _timer.Change(MergeDelay, Timeout.InfiniteTimeSpan);
        }

        if (firstEvent)
        {
            _stage?.Invoke("git-watch-received");
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
            _batchObserved = false;
        }

        _stage?.Invoke("git-watch-merged");
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
