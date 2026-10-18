using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellSnapshotImagesTests
{
    [TestMethod]
    public async Task 加载失败后的相同图片重试必须重新等待()
    {
        using ShellSnapshotImages images = new([1]);
        images.Loaded(0);
        long revision = images.Add([2]);
        Task<bool> failed = images.WaitForLoadedAsync(revision);
        images.FailLoading();
        Assert.IsFalse(await failed);
        long retryRevision = images.Add([2]);
        Assert.AreEqual(revision, retryRevision);
        Assert.AreNotEqual(images.LoadedRevision, retryRevision);
        Task<bool> retry = images.WaitForLoadedAsync(retryRevision);
        Assert.IsFalse(retry.IsCompleted);
        images.Loaded(retryRevision);
        Assert.IsTrue(await retry);
    }

    [TestMethod]
    public async Task 图片加载续体同步关闭后不再交付积压图片()
    {
        using ShellSnapshotImages images = new([1]);
        images.Loaded(0);
        long revision = images.Offer([2])!.Value;
        Assert.IsNull(images.Offer([3]));
        int callbackThread = 0;
        async Task CloseAfterLoadedAsync()
        {
            Assert.IsTrue(await images.WaitForLoadedAsync(revision));
            callbackThread = Environment.CurrentManagedThreadId;
            images.Dispose();
        }
        Task closing = CloseAfterLoadedAsync();
        int completingThread = Environment.CurrentManagedThreadId;
        images.Loaded(revision);
        Assert.IsTrue(closing.IsCompletedSuccessfully);
        Assert.AreEqual(completingThread, callbackThread);
        Assert.AreEqual(0L, images.Bytes);
        Assert.IsNull(images.TakePending());
        Assert.IsNull(images.Open(revision));
        await closing;
    }

    [TestMethod]
    public async Task 失败续体可同步关闭而不重复枚举等待字典()
    {
        using ShellSnapshotImages images = new([1]);
        async Task CloseAfterFailureAsync()
        {
            Assert.IsFalse(await images.WaitForLoadedAsync(0));
            images.Dispose();
        }
        Task closing = CloseAfterFailureAsync();
        images.FailLoading();
        Assert.IsTrue(closing.IsCompletedSuccessfully);
        Assert.AreEqual(0L, images.Bytes);
        Assert.IsNull(images.TakePending());
        await closing;
    }

    [TestMethod]
    public async Task 脚本已提交但图片未加载时刷新不得完成()
    {
        using ShellSnapshotImages images = new([1]);
        images.Loaded(0);
        long revision = images.Offer([2])!.Value;
        Task<bool> waiting = images.WaitForLoadedAsync(revision);
        Assert.IsFalse(waiting.IsCompleted);
        images.Loaded(0);
        Assert.IsFalse(waiting.IsCompleted);
        images.Loaded(revision);
        Assert.IsTrue(await waiting);
        Assert.IsTrue(await images.WaitForLoadedAsync(revision));
    }

    [TestMethod]
    public async Task 加载失败或关闭同步结束等待且不报告成功()
    {
        foreach (bool close in new[] { true, false })
        {
            using ShellSnapshotImages images = new([1]);
            Task<bool> waiting = images.WaitForLoadedAsync(0);
            if (close) images.Dispose(); else images.FailLoading();
            Assert.IsTrue(waiting.IsCompleted);
            Assert.IsFalse(await waiting);
        }
    }

    [TestMethod]
    public async Task 更新版本不会让旧等待误报成功()
    {
        using ShellSnapshotImages images = new([1]);
        Task<bool> old = images.WaitForLoadedAsync(0);
        long revision = images.Add([2]);
        images.Loaded(revision);
        Assert.IsFalse(await old);
        Assert.IsTrue(await images.WaitForLoadedAsync(revision));
    }

    [TestMethod]
    public void 迟到旧请求保持版本且重复请求游标独立()
    {
        using ShellSnapshotImages images = new([1, 2]);
        using MemoryStream first = images.Open(0)!;
        Assert.AreEqual(1, first.ReadByte());
        long revision = images.Add([3, 4, 5]);
        using MemoryStream old = images.Open(0)!;
        using MemoryStream current = images.Open(revision)!;
        Assert.AreEqual(1, old.ReadByte());
        Assert.AreEqual(3, current.ReadByte());
        Assert.AreEqual(5L, images.Bytes);
        images.Loaded(0);
        Assert.AreEqual(-1L, images.LoadedRevision);
        Assert.IsTrue(first.CanRead);
        images.Loaded(revision);
        Assert.IsFalse(first.CanRead);
        Assert.IsNull(images.Open(0));
        Assert.AreEqual(3L, images.Bytes);
        Assert.IsTrue(current.CanRead);
    }

    [TestMethod]
    public void 连续未加载时只保留最新待发送图且关闭清空()
    {
        using ShellSnapshotImages images = new([1]);
        for (int index = 0; index < 100; index++) Assert.IsNull(images.Offer([(byte)(index + 2), 3]));
        Assert.AreEqual(3L, images.Bytes);
        Assert.AreEqual(0L, images.Revision);
        images.Loaded(0);
        Assert.AreEqual(1L, images.TakePending());
        using MemoryStream newest = images.Open(1)!;
        Assert.AreEqual(101, newest.ReadByte());
        images.Offer([7, 8, 9]);
        images.Offer([8, 9]);
        Assert.AreEqual(5L, images.Bytes);
        images.Loaded(1);
        Assert.AreEqual(2L, images.TakePending());
        Assert.IsNull(images.Offer([9]));
        images.Dispose();
        Assert.AreEqual(0L, images.Bytes);
        Assert.IsNull(images.TakePending());
    }

    [TestMethod]
    public void 相同PNG不生成版本或积压图片()
    {
        using ShellSnapshotImages images = new([1, 2]);
        Assert.AreEqual(0L, images.Add([1, 2]));
        Assert.AreEqual(2L, images.Bytes);
        Assert.AreEqual(1L, images.Add([3]));
        images.Loaded(1);
        Assert.AreEqual(1L, images.Add([3]));
        Assert.AreEqual(1L, images.Bytes);
    }

    [TestMethod]
    public void 迟到加载不能释放更新版本且关闭清理所有响应()
    {
        ShellSnapshotImages images = new([1]);
        MemoryStream initial = images.Open(0)!;
        long one = images.Add([2, 3]);
        MemoryStream pending = images.Open(one)!;
        long two = images.Add([4, 5, 6]);
        images.Loaded(one);
        Assert.AreEqual(6L, images.Bytes);
        images.Loaded(two);
        Assert.IsFalse(initial.CanRead);
        Assert.IsFalse(pending.CanRead);
        MemoryStream latest = images.Open(two)!;
        images.Dispose();
        Assert.IsFalse(latest.CanRead);
        Assert.AreEqual(0L, images.Bytes);
        Assert.IsNull(images.Open(two));
        images.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => images.Add([7]));
    }
}
