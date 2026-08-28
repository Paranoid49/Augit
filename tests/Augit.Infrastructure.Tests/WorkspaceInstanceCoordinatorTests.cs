using System.Diagnostics;
using Augit.Infrastructure.Interop;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class WorkspaceInstanceCoordinatorTests
{
    [TestMethod]
    public async Task 同一目录的第二协调器通知首个实例并退出()
    {
        using TemporaryDirectory temporary = new();
        await using WorkspaceInstanceCoordinator owner = new(temporary.FullPath);
        TaskCompletionSource activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.ActivationRequested += (_, _) => activation.TrySetResult();

        bool firstResult = await owner.TryBecomeOwnerAsync();
        await using WorkspaceInstanceCoordinator second = new(temporary.FullPath);
        Stopwatch stopwatch = Stopwatch.StartNew();
        bool secondResult = await second.TryBecomeOwnerAsync();
        await activation.Task.WaitAsync(TimeSpan.FromSeconds(1));
        stopwatch.Stop();

        Assert.IsTrue(firstResult);
        Assert.IsFalse(secondResult);
        Assert.IsLessThanOrEqualTo(1000L, stopwatch.ElapsedMilliseconds);
    }
}
