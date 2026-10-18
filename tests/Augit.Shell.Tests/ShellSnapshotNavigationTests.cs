using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellSnapshotNavigationTests
{
    [TestMethod]
    public async Task 父窗关闭取消导航且迟到成功不能恢复()
    {
        ShellSnapshotNavigation navigation = new();
        Assert.IsFalse(navigation.Ready.IsCompleted);
        navigation.Cancel();
        navigation.Complete(true);
        Assert.IsFalse(await navigation.Ready);
    }

    [TestMethod]
    public async Task 取消导航同步释放队列且保留调用线程()
    {
        using ShellModalOperations queue = new();
        ShellSnapshotNavigation navigation = new();
        int thread = Environment.CurrentManagedThreadId, resumed = 0;
        bool opened = false;
        Task old = queue.RunAsync(async _ => { await navigation.Ready; resumed = Environment.CurrentManagedThreadId; });
        queue.Invalidate();
        Task next = queue.RunAsync(_ => { opened = true; return Task.CompletedTask; });
        navigation.Cancel();
        Assert.IsTrue(old.IsCompleted && next.IsCompleted);
        Assert.IsTrue(opened);
        Assert.AreEqual(thread, resumed);
        await Task.WhenAll(old, next);
    }
}
