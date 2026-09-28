namespace Augit.Shell.Tests;

/// <summary>
/// 写操作跟踪器（规格 §9.3）：进行中要能被取消，取消要真的作用到命令的取消令牌，
/// 并且只有命令自己返回之后才算"停稳"——界面据此决定何时解除"取消中"。
/// </summary>
[TestClass]
public sealed class WriteOperationTrackerTests
{
    [TestMethod]
    public async Task 取消会触发命令的取消令牌并标记为已取消()
    {
        WriteOperationTracker tracker = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool cancelledFlag = false;

        Task<string?> running = tracker.RunAsync(
            async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return "never";
            },
            flag => cancelledFlag = flag,
            CancellationToken.None);

        await started.Task;
        Assert.IsTrue(tracker.IsRunning, "命令在途时应登记为进行中。");
        Assert.IsTrue(await tracker.CancelAsync(), "在途时应能受理取消。");
        Assert.IsNull(await running, "被取消的命令不应返回结果。");
        Assert.IsTrue(cancelledFlag, "取消后应标记为已取消。");
        Assert.IsFalse(tracker.IsRunning, "命令返回后不应再登记为进行中。");
    }

    /// <summary>
    /// 规格 §9.3：取消后保持进行态，**等本机 Git 停止**（命令自己返回）才算停稳。
    /// 这里让命令吞掉取消令牌后继续跑一段，验证 `CancelAsync()` 不会提前回话、`IsRunning`
    /// 也不会提前转假 —— 界面因此不会在进程还在跑的时候解除"取消中"。
    /// </summary>
    [TestMethod]
    public async Task 取消后保持进行态直到命令自己返回()
    {
        WriteOperationTracker tracker = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool stopped = false;

        Task<string?> running = tracker.RunAsync(
            async token =>
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    // 取消令牌已触发，但命令还没返回（本机 Git 仍在收尾）。
                }

                await release.Task;
                stopped = true;
                // 真实命令在停稳之后才观察取消并如实抛出（而不是"取消后还返回成功"）。
                token.ThrowIfCancellationRequested();
                return "stopped";
            },
            _ => { },
            CancellationToken.None);

        await started.Task;
        Task<bool> cancelling = tracker.CancelAsync();
        await Task.Delay(120);
        Assert.IsFalse(cancelling.IsCompleted, "命令还没返回时取消不得提前回话。");
        Assert.IsTrue(tracker.IsRunning, "命令还没返回时仍应保持进行态。");
        Assert.IsFalse(stopped);

        release.SetResult();
        Assert.IsTrue(await cancelling, "在途时应能受理取消。");
        Assert.IsTrue(stopped, "取消回话时必须已经停稳。");
        Assert.IsNull(await running, "被取消的命令不应返回结果。");
        Assert.IsFalse(tracker.IsRunning, "命令返回后不应再登记为进行中。");
    }

    [TestMethod]
    public async Task 命令正常返回时不标记取消()
    {
        WriteOperationTracker tracker = new();
        bool cancelledFlag = true;

        string? result = await tracker.RunAsync(
            _ => Task.FromResult<string?>("完成"),
            flag => cancelledFlag = flag,
            CancellationToken.None);

        Assert.AreEqual("完成", result);
        Assert.IsFalse(cancelledFlag);
        Assert.IsFalse(tracker.IsRunning);
    }

    [TestMethod]
    public async Task 没有在途操作时取消返回false()
    {
        WriteOperationTracker tracker = new();
        Assert.IsFalse(await tracker.CancelAsync(), "没有在途操作时必须如实返回 false，界面据此说明。");
    }

    [TestMethod]
    public async Task 写操作结束后登记被清理且可再次使用()
    {
        // UI 在写操作期间会禁用重复触发（§9.3），因此跟踪器只需保证
        // "结束后不留陈旧的登记"——否则下一次取消会命中一个早已结束的操作。
        WriteOperationTracker tracker = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string?> first = tracker.RunAsync(
            async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return "never";
            },
            _ => { },
            CancellationToken.None);
        await started.Task;
        Assert.IsTrue(await tracker.CancelAsync());
        Assert.IsNull(await first);
        Assert.IsFalse(tracker.IsRunning, "结束后不得留下陈旧的登记。");
        Assert.IsFalse(await tracker.CancelAsync(), "结束之后取消必须如实返回 false。");

        // 可再次使用：登记与取消都不受影响。
        TaskCompletionSource secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string?> second = tracker.RunAsync(
            async token =>
            {
                secondStarted.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return "never";
            },
            _ => { },
            CancellationToken.None);
        await secondStarted.Task;
        Assert.IsTrue(await tracker.CancelAsync());
        Assert.IsNull(await second);
        Assert.IsFalse(tracker.IsRunning);
    }
}
