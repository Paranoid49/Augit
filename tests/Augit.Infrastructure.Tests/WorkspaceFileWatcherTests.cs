using Augit.Infrastructure.Files;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class WorkspaceFileWatcherTests
{
    [TestMethod]
    public async Task 外部变化在五百毫秒内合并通知()
    {
        using TemporaryDirectory temporary = new();
        using WorkspaceFileWatcher watcher = new(temporary.FullPath);
        TaskCompletionSource<IReadOnlyList<string>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, eventArgs) => completion.TrySetResult(eventArgs.Paths);
        string changedPath = temporary.GetPath("changed.txt");
        long started = Environment.TickCount64;

        await File.WriteAllTextAsync(changedPath, "变化");
        IReadOnlyList<string> paths = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.IsLessThanOrEqualTo(500L, Environment.TickCount64 - started);
        Assert.IsTrue(paths.Contains(changedPath, StringComparer.OrdinalIgnoreCase));
    }
}
