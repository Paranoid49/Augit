using System.ComponentModel;
using Augit.Core.Search;

namespace Augit.App;

internal sealed partial class NativeSearchPanel
{
    private const uint SearchDispatchMessage = NativeMethods.WindowMessageApp + 35;
    private readonly object _searchDispatchGate = new();
    private readonly Queue<Action> _searchActions = new();
    private SearchJob? _searchJob;
    private Task _searchWorker = Task.CompletedTask;
    private bool _searchComposing;

    internal Task SearchWorkerForTest => _searchWorker;
    internal Func<SearchOptions, CancellationToken, Task>? SearchBarrierForTest { get; set; }
    internal int SearchStartCountForTest => Volatile.Read(ref _searchStartCount);
    internal int ResultThreadForTest { get; private set; }
    internal string NoticeTextForTest => NativeMethods.GetWindowTextValue(_noticeLabel);

    private sealed class SearchJob(int version, SearchOptions options) : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _source = new();
        private Task? _cancellation;
        private bool _disposed;
        internal int Version { get; } = version;
        internal SearchOptions Options { get; } = options;
        internal CancellationToken Token => _source.Token;
        // 取消立即使令牌失效，结束进程的回调交给线程池，窗口线程不等进程退出。
        internal Task CancelAsync()
        {
            lock (_gate) return _disposed ? Task.CompletedTask : _cancellation ??= _source.CancelAsync();
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _source.Dispose();
            }
        }
    }

    internal bool IsComposingKey(NativeMethods.Message message) => _searchComposing
        && message.Window == _searchEdit && message.MessageId == NativeMethods.WindowMessageKeyDown
        && (int)message.WordParameter is NativeMethods.VirtualKeyEnter or NativeMethods.VirtualKeyEscape or NativeMethods.VirtualKeyTab;

    private void CancelSearchWork()
    {
        ++_searchVersion;
        if (_searchJob is { } job) _ = job.CancelAsync();
        _searchJob = null;
        lock (_searchDispatchGate) _searchActions.Clear();
    }

    private void QueueSearch(bool immediate)
    {
        if (_disposed || _searchComposing) return;
        // 在输入线程一次性读取请求；工作线程不读取窗口，也不依赖调用方安装同步上下文。
        SearchOptions options = new(NativeMethods.GetWindowTextValue(_searchEdit),
            _matchCase, _wholeWord, _regularExpression, _includeIgnored);
        if (_searchJob?.Options == options) return;
        CancelSearchWork();
        if (options.IsEmpty)
        {
            ShowResults([], null);
            _completedSearchVersion = _searchVersion;
            return;
        }
        SearchJob job = new(_searchVersion, options);
        _searchJob = job;
        CancellationToken token = job.Token;
        Task previous = _searchWorker;
        Func<SearchOptions, CancellationToken, Task>? barrier = SearchBarrierForTest;
        // 同一浮层至多一个 rg 正在运行；旧进程完全退出后才启动最新仍有效的请求。
        _searchWorker = Task.Run(async () =>
        {
            Task progress = ShowSearchProgressAsync(job, token);
            try
            {
                await previous.ConfigureAwait(false);
                if (!immediate) await Task.Delay(150, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (barrier is not null) await barrier(options, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _searchStartCount);
                SearchResultEntry[] entries;
                string? notice;
                bool notifyStatus = false;
                if (_mode == WorkspaceSearchMode.FileNames)
                {
                    FileSearchResultSet result = await _searchService.SearchFilesWithStatusAsync(_workspaceRoot, options.Query, token).ConfigureAwait(false);
                    entries = result.Matches.Select(file => new SearchResultEntry(file.RelativePath, null, file.RelativePath, null)).ToArray();
                    notice = result.Notice ?? UiText.FileCount(result.Matches.Count);
                    notifyStatus = result.Notice is not null;
                }
                else
                {
                    TextSearchResult result = await _searchService.SearchTextAsync(_workspaceRoot, options, token).ConfigureAwait(false);
                    entries = result.Matches.Select(match => new SearchResultEntry(match.RelativePath, match.LineNumber,
                        $"{match.RelativePath}:{match.LineNumber}:{match.ColumnNumber}  {match.LineText}", match.LineText)).ToArray();
                    notice = result.Notice ?? (result.IsCancelled ? UiText.OperationCancelled : UiText.SearchResultCount(entries.Length));
                }
                token.ThrowIfCancellationRequested();
                PostSearchAction(job, () =>
                {
                    CompleteSearch(job, entries, notice);
                    if (notifyStatus) _setStatus(notice!);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
            {
                PostSearchAction(job, () =>
                {
                    CompleteSearch(job, [], UiText.SearchComponentFailed);
                    _setStatus(exception.Message);
                });
            }
            finally
            {
                await job.CancelAsync().ConfigureAwait(false);
                await progress.ConfigureAwait(false);
                job.Dispose();
            }
        });
    }

    private int _searchStartCount;

    private async Task ShowSearchProgressAsync(SearchJob job, CancellationToken token)
    {
        try
        {
            await Task.Delay(150, token).ConfigureAwait(false);
            PostSearchAction(job, () =>
            {
                if (_completedSearchVersion == job.Version) return;
                ShowResults(_results, UiText.Searching);
            });
        }
        catch (OperationCanceledException) { }
    }

    private void CompleteSearch(SearchJob job, IReadOnlyList<SearchResultEntry> results, string? notice)
    {
        ResultThreadForTest = Environment.CurrentManagedThreadId;
        _completedSearchVersion = job.Version;
        _searchJob = null;
        ShowResults(results, notice);
    }

    private void PostSearchAction(SearchJob job, Action action)
    {
        lock (_searchDispatchGate)
        {
            if (_disposed || job.Version != _searchVersion) return;
            _searchActions.Enqueue(() =>
            {
                if (!_disposed && job.Version == _searchVersion && NativeMethods.IsWindow(Handle)) action();
            });
            if (!NativeMethods.PostMessage(Handle, SearchDispatchMessage, 0, 0)) _searchActions.Clear();
        }
    }

    private void DrainSearchActions()
    {
        while (true)
        {
            Action action;
            lock (_searchDispatchGate)
            {
                if (!_searchActions.TryDequeue(out action!)) return;
            }
            action();
        }
    }
}
