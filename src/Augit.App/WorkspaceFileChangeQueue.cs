using Augit.Core.Files;

namespace Augit.App;

/// <summary>
/// 工作区文件事件的线程安全合并队列。
/// </summary>
internal sealed class WorkspaceFileChangeQueue
{
    private readonly object _gate = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private long _version;
    private bool _requiresFullRefresh;
    private bool _dispatchPosted;
    private bool _processing;
    private bool _disposed;

    /// <summary>
    /// 合并一批事件，并返回调用方是否需要投递一次 UI 调度消息。
    /// </summary>
    internal bool Enqueue(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (paths.Count == 0)
            {
                _requiresFullRefresh = true;
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    // FileSystemWatcher 出错时使用空路径表示无法判断具体文件，必须执行全量读取。
                    _requiresFullRefresh = true;
                    continue;
                }

                _pendingPaths.Add(path);
            }

            _version++;
            if (_dispatchPosted || _processing)
            {
                return false;
            }

            _dispatchPosted = true;
            return true;
        }
    }

    /// <summary>
    /// 开始执行一个调度循环的首个批次。
    /// </summary>
    internal bool TryBeginDrain(out WorkspaceFileChangeBatch batch)
    {
        lock (_gate)
        {
            batch = default;
            if (_disposed || _processing)
            {
                return false;
            }

            _dispatchPosted = false;
            if (!HasPendingChanges())
            {
                return false;
            }

            _processing = true;
            batch = TakePendingBatch();
            return true;
        }
    }

    /// <summary>
    /// 在同一执行循环内取出下一批事件；没有下一批时不改变执行状态。
    /// </summary>
    internal bool TryTakeNext(out WorkspaceFileChangeBatch batch)
    {
        lock (_gate)
        {
            batch = default;
            if (_disposed || !HasPendingChanges())
            {
                return false;
            }

            batch = TakePendingBatch();
            return true;
        }
    }

    /// <summary>
    /// 结束执行循环，并在结束前又收到事件时请求重新调度。
    /// </summary>
    internal bool EndDrain()
    {
        lock (_gate)
        {
            _processing = false;
            if (_disposed || !HasPendingChanges() || _dispatchPosted)
            {
                return false;
            }

            _dispatchPosted = true;
            return true;
        }
    }

    /// <summary>
    /// 使当前批次失效并清掉旧工作区留下的待处理事件。
    /// </summary>
    internal void Invalidate()
    {
        lock (_gate)
        {
            _version++;
            _pendingPaths.Clear();
            _requiresFullRefresh = false;
        }
    }

    /// <summary>
    /// 判断异步结果是否仍属于当前工作区事件序列。
    /// </summary>
    internal bool IsCurrent(long version)
    {
        lock (_gate)
        {
            return !_disposed && version == _version;
        }
    }

    internal void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _version++;
            _pendingPaths.Clear();
            _requiresFullRefresh = false;
            _dispatchPosted = false;
        }
    }

    private bool HasPendingChanges()
    {
        return _requiresFullRefresh || _pendingPaths.Count > 0;
    }

    private WorkspaceFileChangeBatch TakePendingBatch()
    {
        bool requiresFullRefresh = _requiresFullRefresh;
        IReadOnlyList<string> paths = requiresFullRefresh
            ? Array.Empty<string>()
            : _pendingPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        _pendingPaths.Clear();
        _requiresFullRefresh = false;
        return new(_version, requiresFullRefresh, paths);
    }
}

/// <summary>
/// 已从工作区事件队列取出的稳定批次。
/// </summary>
internal readonly record struct WorkspaceFileChangeBatch(
    long Version,
    bool RequiresFullRefresh,
    IReadOnlyList<string> Paths)
{
    /// <summary>
    /// 返回需要重新读取子项快照的目录；全量批次从工作区根节点开始恢复展开状态。
    /// </summary>
    internal IReadOnlyList<string> GetTreeRefreshDirectories(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (RequiresFullRefresh)
        {
            return [workspaceRoot];
        }

        return Paths
            .Select(TryGetDirectoryName)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path.Length)
            .ToArray();
    }

    private static string? TryGetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// 项目树目录快照的比较规则。
/// </summary>
internal static class WorkspaceTreeRefreshPolicy
{
    internal static bool EntriesEqual(
        IReadOnlyList<WorkspaceTreeEntry> current,
        IReadOnlyList<WorkspaceEntry> incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        if (current.Count != incoming.Count)
        {
            return false;
        }

        // 增量插入后，托管索引的登记顺序不等于树上的自然排序；按路径比较结构身份。
        Dictionary<string, WorkspaceTreeEntry> indexed = current.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
        foreach (WorkspaceEntry right in incoming)
        {
            if (!indexed.Remove(right.Name, out WorkspaceTreeEntry left)
                || !left.Name.Equals(right.Name, StringComparison.Ordinal)
                || !left.FullPath.Equals(right.FullPath, StringComparison.OrdinalIgnoreCase)
                || left.IsDirectory != right.IsDirectory
                || left.CanExpand != right.CanExpand)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// 项目树中会影响行结构的最小目录项快照。
/// </summary>
internal readonly record struct WorkspaceTreeEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    bool CanExpand);
