namespace Augit.Shell;

/// <summary>
/// 进行中的 Git 写操作（规格 §9.3：进行中禁用重复触发、显示取消和当前动作；
/// 取消后**等待本机 Git 停止**，再读取真实仓库状态）。
///
/// 网页层只认"取消已受理"这一件事：真正的停止由这里触发写命令的取消令牌，
/// 命令自己返回（成功/失败/已取消）才算停稳。跟踪器把"当前写操作"收敛到一处，
/// 避免每条写命令各自维护取消状态。
/// </summary>
internal sealed class WriteOperationTracker
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _current;

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
        lock (_gate)
        {
            previous = _current;
            _current = source;
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
                }
            }

            source.Dispose();
        }
    }

    /// <summary>请求取消当前写操作；没有在途操作时返回 false（界面据此说明"没有可取消的操作"）。</summary>
    public bool Cancel()
    {
        CancellationTokenSource? source;
        lock (_gate)
        {
            source = _current;
        }

        if (source is null)
        {
            return false;
        }

        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // 刚好结束：按"没有可取消的操作"处理，不要向上抛。
            return false;
        }
    }
}
