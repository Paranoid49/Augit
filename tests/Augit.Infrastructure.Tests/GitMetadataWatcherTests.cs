using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitMetadataWatcherTests
{
    [TestMethod]
    public async Task 索引变化在五百毫秒内合并通知()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await File.WriteAllTextAsync(temporary.GetPath("changed.txt"), "变化\n");
        using GitMetadataWatcher watcher = new(initialized.Repository!);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => completion.TrySetResult();
        long started = Environment.TickCount64;

        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "changed.txt");
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.IsLessThanOrEqualTo(500L, Environment.TickCount64 - started);
    }
}
