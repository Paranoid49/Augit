using System.Runtime.ExceptionServices;

namespace Augit.Shell;

/// <summary>异步呈现串行执行；关闭立即使正在等待和正在捕获的旧请求失效。</summary>
internal sealed class ShellModalOperations : IDisposable
{
    private readonly Queue<TaskCompletionSource> _waiters = [];
    private long _generation;
    private bool _running;
    private bool _disposed;

    internal bool IsCurrent(long generation) => generation == _generation;
    internal void Invalidate() => _generation++;

    /// <summary>同步恢复已有父表面，不排在旧捕获后；调用方不得创建异步资源。</summary>
    internal Task SupersedeAsync(Func<long, Task> restore)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Invalidate();
        return restore(_generation);
    }

    internal async Task RunAsync(Func<long, Task> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long generation = _generation;
        if (_running)
        {
            // Shell的原生消息循环没有托管SynchronizationContext。同步唤醒让等待者
            // 留在完成WebView2捕获的UI线程，不能用SemaphoreSlim把它送到线程池。
            TaskCompletionSource waiter = new();
            _waiters.Enqueue(waiter);
            await waiter.Task;
        }
        else _running = true;
        try
        {
            if (IsCurrent(generation)) await action(generation);
        }
        finally
        {
            if (_waiters.TryDequeue(out TaskCompletionSource? next)) next.SetResult();
            else _running = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Invalidate();
    }
}

/// <summary>释放一步失败仍执行剩余步骤，最后保留首个故障及其调用栈。</summary>
internal static class ShellModalCleanup
{
    internal static void Run(params Action[] actions)
    {
        ExceptionDispatchInfo? failure = null;
        foreach (Action action in actions)
        {
            try { action(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        failure?.Throw();
    }
}
