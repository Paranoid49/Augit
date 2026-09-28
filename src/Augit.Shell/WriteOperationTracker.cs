namespace Augit.Shell;

/// <summary>
/// 进行中的 Git 写操作（规格 §9.3：进行中禁用重复触发、显示取消和当前动作；
/// 取消后**等待本机 Git 停止**，再读取真实仓库状态）。
///
/// 取消分两步：先触发写命令的取消令牌，再**等命令自己返回**（成功/失败/已取消）才算停稳，
/// `write/cancel` 的应答因此就是"本机 Git 已停止"的信号 —— 网页层收到它才解除"取消中"
/// 并重新读取真实仓库状态（此前只看"取消已受理"，进程还在跑就可能读到中间状态）。
/// 跟踪器把"当前写操作"收敛到一处，避免每条写命令各自维护取消状态。
/// </summary>
internal sealed class WriteOperationTracker
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _current;
    private TaskCompletionSource? _currentFinished;

    /// <summary>是否真的有写操作在途（供取消命令判断"有没有可取消的东西"）。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _current is not null;
            }
        }
    }

    /// <summary>
    /// 执行一次写操作：把它的取消令牌与外部令牌串起来，并登记为"当前写操作"。
    /// 已取消时返回 <c>null</c> 并对 <paramref name="cancelled"/> 置真，由调用方决定怎么回话。
    /// </summary>
    public async Task<T?> RunAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Action<bool> cancelled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous;
        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            previous = _current;
            _current = source;
            _currentFinished = finished;
        }

        previous?.Dispose();
        try
        {
            T result = await action(source.Token).ConfigureAwait(false);
            cancelled(false);
            return result;
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            cancelled(true);
            return default;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, source))
                {
                    _current = null;
                    _currentFinished = null;
                }
            }

            source.Dispose();
            // 命令自己返回（本机 Git 已停稳）之后才唤醒等待者：取消回话据此判断"停稳"。
            finished.TrySetResult();
        }
    }

    /// <summary>
    /// 请求取消当前写操作，并**等命令自己返回**（本机 Git 停稳）再回话（规格 §9.3：
    /// 界面在"取消中"保持进行态，等停止后按最新事实恢复）。
    /// 没有在途操作时立即返回 false（界面据此说明"没有可取消的操作"）。
    /// </summary>
    public async Task<bool> CancelAsync()
    {
        CancellationTokenSource? source;
        Task? finished;
        lock (_gate)
        {
            source = _current;
            finished = _currentFinished?.Task;
        }

        if (source is null)
        {
            return false;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 刚好结束：按"没有可取消的操作"处理，不要向上抛。
            return false;
        }

        if (finished is not null)
        {
            await finished.ConfigureAwait(false);
        }

        return true;
    }
}
