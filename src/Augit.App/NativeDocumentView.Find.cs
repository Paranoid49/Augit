using System.Text.RegularExpressions;

namespace Augit.App;

internal sealed partial class NativeDocumentView
{
    private const uint FindDispatchMessage = NativeMethods.WindowMessageApp + 31;
    private readonly object _findDispatchGate = new();
    private readonly Queue<Action> _findActions = new();
    private readonly Queue<bool> _findDirections = new();
    private FindJob? _findCountJob;
    private Task _findCountWorker = Task.CompletedTask;
    private CancellationTokenSource? _findHighlightCancellation;
    private Task _findHighlightWorker = Task.CompletedTask;
    private string? _findCompositionQuery;
    private bool _findCompositionPreservePosition;

    internal bool FindBusyForTest => _findCountJob is not null || !_findHighlightWorker.IsCompleted;
    internal Task FindWorkersForTest => _findCountWorker;
    internal Func<CancellationToken, Task>? FindWorkBarrierForTest { get; set; }

    private sealed record FindQuery(string Source, string Query, bool Case, bool Whole, bool Regex)
    {
        // 小型普通文本可直接完成；正则的复杂度不由文本长度决定，始终后台执行。
        internal bool Background => Regex || Source.Length >= 100_000 || Query.Length >= 1_024;
        internal bool SameAs(FindQuery other) => ReferenceEquals(Source, other.Source)
            && Query == other.Query && Case == other.Case && Whole == other.Whole && Regex == other.Regex;
    }

    private sealed class FindJob(FindQuery query) : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private bool _disposed;
        internal FindQuery Query { get; } = query;

        // Token 在创建任务时读取一次；取消与释放共用锁，避免工作线程与关闭窗口竞争释放。
        internal CancellationToken GetToken() { lock (_gate) return _cancellation.Token; }
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

    private FindQuery CurrentFindQuery() => new(ActiveText, NativeMethods.GetWindowTextValue(_findEdit),
        IsChecked(_matchCaseButton), IsChecked(_wholeWordButton), IsChecked(_regularExpressionButton));

    private bool IsCurrentFindJob(FindJob job) => !_disposed && _findVisible && !_findComposing && NativeMethods.IsWindowVisible(Handle)
        && ReferenceEquals(job, _findCountJob)
        && job.Query.SameAs(CurrentFindQuery());

    private void BeginFindComposition()
    {
        if (_disposed || !_findVisible || _findComposing || !NativeMethods.IsWindowVisible(Handle)) return;
        _findComposing = true;
        _findCompositionQuery = NativeMethods.GetWindowTextValue(_findEdit);
        _findCompositionPreservePosition = false;
        // 保留已完成查询，取消未完成扫描和方向队列；取消组词后能复用原匹配位置。
        CancelFindWork();
    }

    private void ResetFindComposition()
    {
        _findComposing = false;
        _findCompositionQuery = null;
        _findCompositionPreservePosition = false;
    }

    private void CompleteFindComposition(bool preservePosition)
    {
        if (!_findComposing) return;
        bool changed = _findCompositionQuery != NativeMethods.GetWindowTextValue(_findEdit);
        bool keepPosition = preservePosition || (_findCompositionPreservePosition && !changed);
        ResetFindComposition();
        if (changed)
        {
            _findPosition = 0;
            _lastFindMatch = null;
        }
        UpdateFindStatus(keepPosition);
    }

    private void HandleFindInputChanged()
    {
        if (_findComposing)
        {
            SetFindStatus(string.Empty);
            return;
        }
        string query = NativeMethods.GetWindowTextValue(_findEdit);
        // 输入法结束后可再次发送相同文字通知；保留匹配位置供后续外部刷新和导航复用。
        if (query != _findCountCache?.Query && query != _findCountJob?.Query.Query)
        {
            _findPosition = 0;
            _lastFindMatch = null;
        }
        UpdateFindStatus();
    }

    private void CancelFindWork()
    {
        _findCountJob?.Cancel();
        _findHighlightCancellation?.Cancel();
        _findHighlightsVisible = false;
        _findCountJob = null;
        _findDirections.Clear();
        lock (_findDispatchGate) _findActions.Clear();
    }

    private Task RunFindJobAsync(FindJob job, Task previous, Func<CancellationToken, Action> compute)
    {
        CancellationToken token = job.GetToken();
        Func<CancellationToken, Task>? barrier = FindWorkBarrierForTest;
        // 每种任务只有一个扫描在运行；排队的旧查询在开始计算前检查取消，不并行扫描旧正文。
        return Task.Run(async () =>
        {
            Task progress = RevealFindProgressAsync(job, token);
            try
            {
                await previous.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (barrier is not null) await barrier(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Action apply = compute(token);
                token.ThrowIfCancellationRequested();
                PostFindAction(job, apply);
            }
            catch (OperationCanceledException) { }
            catch (RegexMatchTimeoutException) { PostFindError(job, UiText.FindTimedOut); }
            catch (TimeoutException) { PostFindError(job, UiText.FindTimedOut); }
            catch (ArgumentException) { PostFindError(job, UiText.InvalidRegularExpression); }
            finally
            {
                job.Cancel();
                await progress.ConfigureAwait(false);
                job.Dispose();
            }
        });
    }

    private async Task RevealFindProgressAsync(FindJob job, CancellationToken token)
    {
        try
        {
            await Task.Delay(150, token).ConfigureAwait(false);
            PostFindAction(job, () => SetFindStatus(UiText.Searching));
        }
        catch (OperationCanceledException) { }
    }

    private void PostFindError(FindJob job, string message) => PostFindAction(job, () =>
    {
        CancelFindWork();
        FindQuery query = job.Query;
        _findCountCache = (query.Source, query.Query, query.Case, query.Whole, query.Regex, message);
        SetFindStatus(message);
    });

    private void CompleteFindQuery(FindQuery query, List<(int Start, int Length)> matches,
        int? previousMatchStart = null, bool preservePosition = false)
    {
        _findMatches = matches;
        _findMatchIndex = matches.Count == 0 ? -1 : 0;
        if (previousMatchStart is int start && matches.Count > 0)
            _findMatchIndex = Math.Max(0, matches.FindLastIndex(match => match.Start <= start));
        string text = matches.Count == 0 ? "0/0" : $"{_findMatchIndex + 1}/{matches.Count}";
        _findCountCache = (query.Source, query.Query, query.Case, query.Whole, query.Regex, text);
        ApplyFindHighlights(query.Source);
        if (_findMatchIndex >= 0)
        {
            if (!preservePosition) ApplyFindMatch(query.Source, matches[_findMatchIndex], false);
            else
            {
                // 外部刷新只重算匹配，不能覆盖用户正在阅读的选择与滚动。
                _lastFindMatch = matches[_findMatchIndex];
                ActiveEditor?.SetHighlights(2, query.Source, [_lastFindMatch.Value], 42, 111, 219);
            }
        }
        SetFindStatus(text);
        while (_findDirections.TryDequeue(out bool backwards)) NavigateFindResult(backwards);
    }

    private void NavigateFindResult(bool backwards)
    {
        if (_findMatches.Count == 0) return;
        _findMatchIndex = (_findMatchIndex + (backwards ? -1 : 1) + _findMatches.Count) % _findMatches.Count;
        FindQuery query = CurrentFindQuery();
        ApplyFindMatch(query.Source, _findMatches[_findMatchIndex], backwards);
        string text = $"{_findMatchIndex + 1}/{_findMatches.Count}";
        _findCountCache = (query.Source, query.Query, query.Case, query.Whole, query.Regex, text);
        SetFindStatus(text);
    }

    private void ApplyFindHighlights(string source)
    {
        if (ActiveEditor is not { } editor) return;
        _findHighlightCancellation?.Cancel();
        CancellationTokenSource cancellation = new();
        _findHighlightCancellation = cancellation;
        _findHighlightsVisible = true;
        _findHighlightWorker = PaintFindMatchesAsync(editor, source, _findMatches, cancellation);
    }

    private async Task PaintFindMatchesAsync(ScintillaControl editor, string source,
        IReadOnlyList<(int Start, int Length)> matches, CancellationTokenSource cancellation)
    {
        try
        {
            // 分时标记，不在输入消息中一次写入成千上万条高亮；新查询、隐藏和关闭都取消旧批次。
            await editor.SetHighlightsAsync(1, source, matches, 96, 165, 250,
                () => !_disposed && _findVisible && NativeMethods.IsWindowVisible(Handle)
                    && ReferenceEquals(_findMatches, matches) && ReferenceEquals(ActiveEditor, editor),
                cancellation.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_findHighlightCancellation, cancellation)) _findHighlightCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ClearFindHighlights()
    {
        _findHighlightCancellation?.Cancel();
        if (ActiveEditor is null) return;
        ActiveEditor.SetHighlights(1, ActiveText, [], 96, 165, 250);
        ActiveEditor.SetHighlights(2, ActiveText, [], 42, 111, 219);
        _findHighlightsVisible = false;
    }

    private void PostFindAction(FindJob job, Action action)
    {
        lock (_findDispatchGate)
        {
            if (_disposed) return;
            _findActions.Enqueue(() => { if (IsCurrentFindJob(job)) action(); });
            if (!NativeMethods.PostMessage(Handle, FindDispatchMessage, 0, 0)) _findActions.Clear();
        }
    }

    private void DrainFindActions()
    {
        while (true)
        {
            Action action;
            lock (_findDispatchGate)
            {
                if (!_findActions.TryDequeue(out action!)) return;
            }
            action();
        }
    }

    private void ApplyFindMatch(string source, (int Start, int Length)? match, bool backwards)
    {
        _lastFindMatch = match;
        if (match is null) { _findPosition = backwards ? source.Length : 0; return; }
        ActiveEditor?.SelectUtf8Range(match.Value.Start, match.Value.Start + match.Value.Length, source, focus: false);
        ActiveEditor?.SetHighlights(2, source, [match.Value], 42, 111, 219);
        _findPosition = backwards ? match.Value.Start : match.Value.Start + Math.Max(1, match.Value.Length);
    }

    private static (int Start, int Length)? FindLiteralMatch(string source, string query, int position,
        bool backwards, bool matchCase, bool wholeWord, CancellationToken cancellationToken)
    {
        StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int start = backwards && position < 0 ? source.Length
            : !backwards && position > source.Length ? 0 : Math.Clamp(position, 0, source.Length);
        long deadline = Environment.TickCount64 + 250;
        for (int pass = 0; pass < 2; pass++)
        {
            int boundary = pass == 0 ? start : backwards ? source.Length : 0;
            while (backwards ? boundary >= query.Length : boundary <= source.Length - query.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Environment.TickCount64 >= deadline) throw new TimeoutException(UiText.FindTimedOut);
                int index = backwards
                    ? source.AsSpan(0, boundary).LastIndexOf(query.AsSpan(), comparison)
                    : source.IndexOf(query, boundary, comparison);
                if (index < 0) break;
                int end = index + query.Length;
                if (!wholeWord || ((index == 0 || !IsFindWordCharacter(source[index - 1]))
                    && (end == source.Length || !IsFindWordCharacter(source[end]))))
                    return (index, query.Length);
                boundary = backwards ? end - 1 : index + 1;
            }
        }
        return null;
    }
}
