using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellSnapshotPreparationTests
{
    [TestMethod]
    public async Task 背景图片就绪前不能交接且过期只释放一次()
    {
        foreach (bool current in new[] { true, false })
        {
            TaskCompletionSource<bool> ready = new();
            int released = 0;
            Task<bool> work = ShellSnapshotPreparation.WaitForImageAsync(ready.Task,
                () => current, () => released++);
            Assert.IsFalse(work.IsCompleted);
            ready.SetResult(true);
            Assert.AreEqual(current, await work);
            Assert.AreEqual(current ? 0 : 1, released);
        }
    }

    [TestMethod]
    public async Task 背景加载失败释放资源而不交接()
    {
        int released = 0;
        Task<bool> work = ShellSnapshotPreparation.WaitForImageAsync(Task.FromResult(false),
            () => true, () => released++);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await work);
        Assert.AreEqual(1, released);
    }

    [TestMethod]
    public async Task 已关闭背景加载失败不恢复窗口()
    {
        int released = 0;
        Assert.IsFalse(await ShellSnapshotPreparation.WaitForImageAsync(Task.FromResult(false),
            () => false, () => released++));
        Assert.AreEqual(1, released);
    }

    [TestMethod]
    public async Task 等待首图期间关闭后不会交付背景()
    {
        TaskCompletionSource<bool> ready = new();
        bool current = true;
        int released = 0;
        Task<bool> work = ShellSnapshotPreparation.WaitForImageAsync(ready.Task,
            () => current, () => released++);
        current = false;
        ready.SetResult(true);
        Assert.IsFalse(await work);
        Assert.AreEqual(1, released);
    }

    [TestMethod]
    public async Task 捕获期间初始化隐藏文档且过期后释放完整资源()
    {
        TaskCompletionSource capture = new(), navigation = new();
        object controller = new();
        bool navigationStarted = false, current = true;
        int released = 0;
        async Task<object> CreateAsync()
        {
            navigationStarted = true;
            await navigation.Task;
            return controller;
        }
        Task<object?> work = ShellSnapshotPreparation.RunAsync(() => capture.Task, CreateAsync,
            () => current, resource => { Assert.AreSame(controller, resource); released++; });
        Assert.IsTrue(navigationStarted);
        capture.SetResult();
        Assert.IsFalse(work.IsCompleted);
        current = false;
        navigation.SetResult();
        Assert.IsNull(await work);
        Assert.AreEqual(1, released);
    }

    [TestMethod]
    public async Task 关闭销毁父窗的创建失败只在过期时忽略()
    {
        foreach (bool current in new[] { false, true })
        {
            TaskCompletionSource capture = new();
            Task<object?> work = ShellSnapshotPreparation.RunAsync<object>(
                () => capture.Task, () => throw new InvalidOperationException("父窗已销毁"),
                () => current, _ => Assert.Fail(),
                isObsoleteFailure: exception => exception is InvalidOperationException);
            Assert.IsFalse(work.IsCompleted);
            capture.SetResult();
            if (current) await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await work);
            else Assert.IsNull(await work);
        }
    }

    [TestMethod]
    public async Task 过期仍保留未识别错误且捕获失败释放已创建资源()
    {
        int released = 0;
        Task<object?> work = ShellSnapshotPreparation.RunAsync(
            () => throw new InvalidOperationException("已关闭"), () => Task.FromResult(new object()),
            () => false, _ => released++, isObsoleteFailure: exception => exception is InvalidOperationException);
        Assert.IsNull(await work);
        Assert.AreEqual(1, released);
        await Assert.ThrowsExactlyAsync<IOException>(async () => await ShellSnapshotPreparation.RunAsync<object>(
            () => Task.CompletedTask, () => throw new IOException("非生命周期错误"),
            () => false, _ => Assert.Fail(), isObsoleteFailure: exception => exception is InvalidOperationException));
    }

    [TestMethod]
    public async Task 可见背景仍先捕获且过期时不创建控制器()
    {
        foreach (bool current in new[] { false, true })
        {
            TaskCompletionSource capture = new();
            bool created = false;
            Task<object?> work = ShellSnapshotPreparation.RunAsync(
                () => capture.Task, () => { created = true; return Task.FromResult(new object()); },
                () => current, _ => Assert.Fail(), parallel: false);
            Assert.IsFalse(created);
            capture.SetResult();
            object? result = await work;
            Assert.AreEqual(current, created);
            Assert.AreEqual(current, result is not null);
        }
    }

    [TestMethod]
    public async Task 两步并行启动但必须全部完成才交付()
    {
        TaskCompletionSource capture = new();
        TaskCompletionSource<object> create = new();
        bool captureStarted = false, createStarted = false;
        int released = 0;
        Task<object?> work = ShellSnapshotPreparation.RunAsync(
            () => { captureStarted = true; return capture.Task; },
            () => { createStarted = true; return create.Task; }, () => true, _ => released++);
        Assert.IsTrue(captureStarted && createStarted);
        object resource = new();
        create.SetResult(resource);
        Assert.IsFalse(work.IsCompleted);
        capture.SetResult();
        Assert.AreSame(resource, await work);
        Assert.AreEqual(0, released);
    }

    [TestMethod]
    public async Task 捕获同步失败仍等待创建并释放一次()
    {
        TaskCompletionSource<object> create = new();
        int released = 0;
        Task<object?> work = ShellSnapshotPreparation.RunAsync(
            () => throw new IOException("捕获失败"), () => create.Task, () => true, _ => released++);
        Assert.IsFalse(work.IsCompleted);
        create.SetResult(new object());
        await Assert.ThrowsExactlyAsync<IOException>(async () => await work);
        Assert.AreEqual(1, released);
    }

    [TestMethod]
    public async Task 创建同步失败仍等待捕获完成()
    {
        TaskCompletionSource capture = new();
        Task<object?> work = ShellSnapshotPreparation.RunAsync<object>(
            () => capture.Task, () => throw new IOException("创建失败"), () => true, _ => Assert.Fail());
        Assert.IsFalse(work.IsCompleted);
        capture.SetResult();
        await Assert.ThrowsExactlyAsync<IOException>(async () => await work);
    }

    [TestMethod]
    public async Task 关闭淘汰准备中的资源且取消捕获也释放()
    {
        foreach (bool cancel in new[] { false, true })
        {
            TaskCompletionSource capture = new();
            int released = 0;
            Task<object?> work = ShellSnapshotPreparation.RunAsync(
                () => capture.Task, () => Task.FromResult(new object()), () => false, _ => released++);
            if (cancel)
            {
                capture.SetCanceled();
                await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await work);
            }
            else
            {
                capture.SetResult();
                Assert.IsNull(await work);
            }
            Assert.AreEqual(1, released);
        }
    }
}
