namespace Augit.Shell;

/// <summary>并行准备图片和控制器；两步均结束后才交付，失败或过期时释放已创建资源。</summary>
internal static class ShellSnapshotPreparation
{
    // 图片尚未解码时不能移走所有者的真实界面；关闭或加载失败仍释放临时资源。
    internal static async Task<bool> WaitForImageAsync(Task<bool> ready, Func<bool> isCurrent,
        Action release)
    {
        bool loaded;
        try { loaded = await ready; }
        catch
        {
            release();
            if (!isCurrent()) return false;
            throw;
        }
        if (!isCurrent()) { release(); return false; }
        if (loaded) return true;
        release();
        throw new InvalidOperationException("模态背景图片加载失败。");
    }

    internal static async Task<T?> RunAsync<T>(Func<Task> capture, Func<Task<T>> create,
        Func<bool> isCurrent, Action<T> release, bool parallel = true,
        Func<Exception, bool>? isObsoleteFailure = null) where T : class
    {
        // 包装同步抛错，使另一已启动操作仍被等待，不遗留流写入或控制器。
        async Task CaptureAsync() => await capture();
        async Task<T> CreateAsync() => await create();
        Task captureTask = CaptureAsync();
        // 普通背景的父窗可见，必须先捕获再创建，避免控制器初始底色进入原图。
        if (!parallel)
        {
            await captureTask;
            if (!isCurrent()) return null;
        }
        Task<T> resourceTask = CreateAsync();
        // 两步已经同时启动；逐一等待，保证失败时另一操作也结束后才释放资源。
        try { await captureTask; await resourceTask; }
        catch (Exception exception)
        {
            try { await resourceTask; } catch { /* 创建失败时没有可释放控制器，保留原始异常。 */ }
            if (resourceTask.IsCompletedSuccessfully) release(resourceTask.Result);
            // 关闭可能先销毁父窗；只忽略调用方明确识别的过期生命周期错误。
            if (!isCurrent() && isObsoleteFailure?.Invoke(exception) == true) return null;
            throw;
        }
        T resource = resourceTask.Result;
        if (isCurrent()) return resource;
        release(resource);
        return null;
    }
}
