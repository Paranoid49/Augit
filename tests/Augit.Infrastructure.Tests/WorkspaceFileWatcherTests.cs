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

    [TestMethod]
    public async Task Git元数据变化不进入普通工作区刷新批次()
    {
        using TemporaryDirectory temporary = new();
        string gitDirectory = temporary.GetPath(".git");
        Directory.CreateDirectory(gitDirectory);
        using WorkspaceFileWatcher watcher = new(temporary.FullPath);
        TaskCompletionSource<IReadOnlyList<string>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, eventArgs) => completion.TrySetResult(eventArgs.Paths);

        await File.WriteAllTextAsync(Path.Combine(gitDirectory, "index"), "元数据");
        Task first = await Task.WhenAny(completion.Task, Task.Delay(250));
        Assert.AreNotSame(completion.Task, first, ".git 变化不应触发普通文件刷新。");

        string changedPath = temporary.GetPath("changed.txt");
        await File.WriteAllTextAsync(changedPath, "正文");
        IReadOnlyList<string> paths = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.HasCount(1, paths);
        Assert.AreEqual(changedPath, paths[0], true);
    }
}
