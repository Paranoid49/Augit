using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellModalOperationsTests
{
    private static readonly int[] StartedOrder = [1];
    private static readonly int[] FinishedOrder = [1, 2, 3];
    private static readonly string[] ReleasedResources = ["流", "窗口", "所有者"];
    [TestMethod]
    public async Task 并发呈现按顺序执行且故障后释放队列()
    {
        using ShellModalOperations queue = new();
        TaskCompletionSource capture = new();
        List<int> order = [];
        Task first = queue.RunAsync(async _ => { order.Add(1); await capture.Task; order.Add(2); });
        Task second = queue.RunAsync(_ => { order.Add(3); return Task.CompletedTask; });
        CollectionAssert.AreEqual(StartedOrder, order);
        capture.SetResult();
        await Task.WhenAll(first, second);
        CollectionAssert.AreEqual(FinishedOrder, order);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => queue.RunAsync(_ => throw new InvalidOperationException()));
        await queue.RunAsync(_ => { order.Add(4); return Task.CompletedTask; });
        Assert.AreEqual(4, order[^1]);
    }

    [TestMethod]
    public async Task 关闭立即淘汰捕获中及排队请求且允许后续打开()
    {
        using ShellModalOperations queue = new();
        TaskCompletionSource capture = new();
        bool oldPresented = false, queuedPresented = false, newPresented = false;
        Task first = queue.RunAsync(async generation => { await capture.Task; oldPresented = queue.IsCurrent(generation); });
        Task second = queue.RunAsync(_ => { queuedPresented = true; return Task.CompletedTask; });
        queue.Invalidate();
        Task third = queue.RunAsync(_ => { newPresented = true; return Task.CompletedTask; });
        capture.SetResult();
        await Task.WhenAll(first, second, third);
        Assert.IsFalse(oldPresented);
        Assert.IsFalse(queuedPresented);
        Assert.IsTrue(newPresented);
    }

    [TestMethod]
    public void 重挂及快照关闭失败仍释放窗口流并恢复所有者()
    {
        List<string> released = [];
        InvalidOperationException first = new("控制器已关闭");
        InvalidOperationException actual = Assert.ThrowsExactly<InvalidOperationException>(() => ShellModalCleanup.Run(
            () => throw first,
            () => ShellModalCleanup.Run(() => throw new IOException("快照已关闭"), () => released.Add("流")),
            () => released.Add("窗口"),
            () => released.Add("所有者")));
        Assert.AreSame(first, actual);
        CollectionAssert.AreEqual(ReleasedResources, released);
    }

    [TestMethod]
    public async Task 返回父表面不等待旧捕获且旧排队呈现失效()
    {
        using ShellModalOperations queue = new();
        TaskCompletionSource capture = new();
        bool stale = false, queued = false, restored = false;
        Task first = queue.RunAsync(async generation => { await capture.Task; stale = queue.IsCurrent(generation); });
        Task second = queue.RunAsync(_ => { queued = true; return Task.CompletedTask; });
        await queue.SupersedeAsync(_ => { restored = true; return Task.CompletedTask; });
        Assert.IsTrue(restored);
        Assert.IsFalse(first.IsCompleted);
        capture.SetResult();
        await Task.WhenAll(first, second);
        Assert.IsFalse(stale);
        Assert.IsFalse(queued);
    }

    [TestMethod]
    public async Task 释放宿主时等待者安全退出且拒绝后续请求()
    {
        using ShellModalOperations queue = new();
        TaskCompletionSource capture = new();
        bool presented = false;
        Task first = queue.RunAsync(async _ => await capture.Task);
        Task second = queue.RunAsync(_ => { presented = true; return Task.CompletedTask; });
        queue.Dispose();
        capture.SetResult();
        await Task.WhenAll(first, second);
        Assert.IsFalse(presented);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => queue.RunAsync(_ => Task.CompletedTask));
    }
}
