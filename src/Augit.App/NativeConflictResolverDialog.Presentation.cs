using System.Diagnostics;
using Augit.Core.Git;

namespace Augit.App;

internal sealed partial class NativeConflictResolverDialog
{
    private readonly object _presentationGate = new();
    private PresentationJob? _presentationJob;
    private PreparedPresentation? _queuedPresentation;
    private Task _presentationWorker = Task.CompletedTask;
    private System.Threading.Timer? _presentationTimer;
    private IEnumerator<(int Side, int Line, int Count, bool Gap)>? _decorationChunks;
    private bool _backgroundPresentation;
    private string? _presentationCompletionNotice;

    internal Func<CancellationToken, Task>? PresentationBarrierForTest { get; set; }
    internal int PresentationThreadForTest { get; private set; }
    internal int PresentationSlicesForTest { get; private set; }

    private sealed class PresentationJob : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _source = new();
        private bool _disposed;
        internal PresentationJob() => Token = _source.Token;
        internal CancellationToken Token { get; }
        internal void Cancel()
        {
            lock (_gate) { if (!_disposed) _source.Cancel(); }
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

    private sealed record PreparedPresentation(PresentationJob Job, string Yours, string Text, string Theirs,
        IReadOnlyList<GitConflictBlock> Blocks, List<ConflictViewBlock> ViewBlocks, List<int> HiddenLines, DisplayAlignment Alignment,
        bool AlignCurrent,
        Exception? Error = null);

    private bool NeedsBackgroundPresentation(string text) => text.Length >= 100_000
        || (_document?.YoursText?.Length ?? 0) >= 100_000 || (_document?.TheirsText?.Length ?? 0) >= 100_000;

    private bool TryGetResultSnapshot(out string text, out IReadOnlyList<GitConflictBlock> blocks)
    {
        if (_parsedResultText is not null)
        {
            text = _parsedResultText;
            blocks = _parsedResultBlocks;
            return true;
        }
        text = string.Empty;
        blocks = [];
        if (_presentationJob is not null) return false;
        text = _result?.GetTextContent() ?? string.Empty;
        if (NeedsBackgroundPresentation(text))
        {
            QueuePresentation(text, alignCurrent: false);
            return false;
        }
        (text, blocks) = GetResultSnapshot(text);
        return true;
    }

    private void QueuePresentation(string text, bool alignCurrent)
    {
        if (_closed || _saveInProgress || _document is null) return;
        CancelPresentation();
        PresentationJob job = new();
        lock (_presentationGate) _presentationJob = job;
        _backgroundPresentation = true;
        _resultPresentationPending = true;
        _resultChangeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        SetConflictActionsEnabled(false);
        SetNotice("正在更新冲突块…");
        string yours = _document.YoursText ?? "此侧不存在。";
        string theirs = _document.TheirsText ?? "此侧不存在。";
        // 只有原始加载正文可以复用服务端的解析块，接受或手工编辑后的正文必须重新解析。
        IReadOnlyList<GitConflictBlock>? known = ReferenceEquals(text, _document.ResultText) ? _document.Blocks : null;
        Task previous = _presentationWorker;
        Func<CancellationToken, Task>? barrier = PresentationBarrierForTest;
        _presentationWorker = Task.Run(async () =>
        {
            try
            {
                // 每个窗口只运行一份解析；连续输入只让最新未取消版本进入计算。
                await previous.ConfigureAwait(false);
                job.Token.ThrowIfCancellationRequested();
                if (barrier is not null) await barrier(job.Token).ConfigureAwait(false);
                job.Token.ThrowIfCancellationRequested();
                IReadOnlyList<GitConflictBlock> blocks = known ?? GitConflictText.Parse(text, job.Token);
                List<int> hiddenLines = GetHiddenConflictMarkerLines(text, blocks, job.Token);
                List<ConflictViewBlock> viewBlocks = BuildConflictViewBlocks(yours, text, theirs, blocks, hiddenLines, job.Token);
                DisplayAlignment alignment = BuildDisplayAlignment(viewBlocks, hiddenLines, job.Token);
                PostPresentation(new(job, yours, text, theirs, blocks, viewBlocks, hiddenLines, alignment, alignCurrent));
            }
            catch (OperationCanceledException) when (job.Token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                PostPresentation(new(job, yours, text, theirs, [], [], [], new(false, []), alignCurrent, exception));
            }
            finally { job.Dispose(); }
        });
    }

    private void PostPresentation(PreparedPresentation presentation)
    {
        lock (_presentationGate)
        {
            if (_closed || _presentationJob != presentation.Job || presentation.Job.Token.IsCancellationRequested) return;
            _queuedPresentation = presentation;
            if (!NativeMethods.PostMessage(_handle, WindowMessagePresentationReady, 0, 0)) _queuedPresentation = null;
        }
    }

    private void ApplyQueuedPresentation()
    {
        PreparedPresentation? ready;
        lock (_presentationGate) ready = _queuedPresentation;
        if (_closed || _saveInProgress || ready is null || ready.Job != _presentationJob) return;
        PresentationThreadForTest = Environment.CurrentManagedThreadId;
        if (ready.Error is not null)
        {
            CancelPresentation();
            _resultPresentationPending = false;
            ShowError($"冲突块更新失败：{ready.Error.Message}");
            // 接受和导航仍禁用；保存入口可以重新触发最新正文的检查。
            _ = NativeMethods.EnableWindow(_saveButton, true);
            return;
        }
        if (_decorationChunks is null)
        {
            _viewBlocks = ready.ViewBlocks;
            BeginDisplayAlignment(ready.Alignment);
            _result!.SetHiddenLines(ready.HiddenLines);
            _yours!.ClearBackgroundLines(0);
            _result!.ClearBackgroundLines(0);
            _theirs!.ClearBackgroundLines(0);
            UpdateConflictColors();
            _decorationChunks = BackgroundChunks(ready.ViewBlocks, ready.Alignment).GetEnumerator();
        }

        long started = Stopwatch.GetTimestamp();
        PresentationSlicesForTest++;
        // 单片预算保持在一帧以内；适当扩大预算，减少十万行正文下的消息往返，避免大正文完成时间过长。
        const double sliceBudgetMilliseconds = 8;
        while (_decorationChunks.MoveNext())
        {
            (int side, int line, int count, bool gap) = _decorationChunks.Current;
            ScintillaControl control = side == 0 ? _yours! : side == 1 ? _result! : _theirs!;
            if (gap) ApplyDisplayGap(new(side, line, count));
            else control.AddBackgroundLines(0, line, count);
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds < sliceBudgetMilliseconds) continue;
            _presentationTimer ??= new(_ =>
            {
                lock (_presentationGate)
                    if (!_closed && _presentationJob is not null)
                        _ = NativeMethods.PostMessage(_handle, WindowMessagePresentationReady, 0, 0);
            });
            _presentationTimer.Change(0, Timeout.Infinite);
            return;
        }

        _decorationChunks.Dispose();
        _decorationChunks = null;
        lock (_presentationGate) { _queuedPresentation = null; _presentationJob = null; }
        _parsedResultText = _presentedResultText = ready.Text;
        _presentedYoursText = ready.Yours;
        _presentedTheirsText = ready.Theirs;
        _parsedResultBlocks = ready.Blocks;
        _resultParseCount++;
        _resultPresentationPending = false;
        RefreshDisplayViewports();
        UpdateConflictColors();
        UpdateConflictCount(ready.Blocks.Count);
        if (_presentationCompletionNotice is { } notice) SetNotice(notice);
        _presentationCompletionNotice = null;
        if (ready.AlignCurrent) AlignCurrentConflictBlock();
        else SynchronizeVerticalScroll(_resultViewport!);
    }

    private static IEnumerable<(int Side, int Line, int Count, bool Gap)> BackgroundChunks(List<ConflictViewBlock> blocks, DisplayAlignment alignment)
    {
        foreach (DisplayGap gap in alignment.Gaps) yield return (gap.Side, gap.Line, gap.Count, true);
        foreach (ConflictViewBlock block in blocks)
        {
            for (int side = 0; side < 3; side++)
            {
                int first = side == 0 ? block.YoursLine : side == 1 ? block.ResultLine : block.TheirsLine;
                int remaining = side == 0 ? block.YoursLineCount : side == 1 ? block.ResultLineCount : block.TheirsLineCount;
                for (int line = first; remaining > 0;)
                {
                    int count = Math.Min(64, remaining);
                    yield return (side, line, count, false);
                    line += count;
                    remaining -= count;
                }
            }
        }
    }

    private void CancelPresentation()
    {
        PresentationJob? job;
        lock (_presentationGate)
        {
            job = _presentationJob;
            _presentationJob = null;
            _queuedPresentation = null;
        }
        job?.Cancel();
        _presentationTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _decorationChunks?.Dispose();
        _decorationChunks = null;
        _presentationCompletionNotice = null;
    }
}
