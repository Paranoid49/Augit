using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitMetadataWatcherTests
{
    // 观察器的合并延迟是 50 毫秒（GitMetadataWatcher.MergeDelay）。
    // 这里只验证「观察 + 合并」这一层：直接改写 .git 下的元数据文件触发事件，
    // 不通过启动 git 进程制造变化。
    //
    // 原实现用 `git add` 触发，并把 500 毫秒的断言套在整段代码上——
    // 其中包含 git 子进程的创建与退出。那部分耗时随机器负载波动
    // （整套并行运行时实测 735 毫秒），与被测的合并行为无关，
    // 因此该断言会偶发失败。现在把被测对象与进程启动开销分开计量。
    private const int DebounceMilliseconds = 50;

    // 合并延迟之外只留调度余量：观察器回调还需等待定时器与线程池调度。
    private const int SchedulingAllowanceMilliseconds = 250;

    [TestMethod]
    public async Task 元数据变化在合并延迟加调度余量内通知()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);

        string? gitDirectory = initialized.Repository?.GitDirectory;
        Assert.IsNotNull(gitDirectory, "初始化后应返回 Git 目录。");
        // git init 之后就已存在的元数据文件；改写它即代表 Git 元数据发生变化。
        string headPath = Path.Combine(gitDirectory, "HEAD");
        Assert.IsTrue(File.Exists(headPath), "初始化后应存在 .git/HEAD。");

        using GitMetadataWatcher watcher = new(initialized.Repository!);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => completion.TrySetResult();

        // 触发一次真实的文件系统改动：改写索引文件内容。
        // 先等一小段时间，确保 FileSystemWatcher 已完成内部注册。
        await Task.Delay(DebounceMilliseconds * 2);
        long started = Environment.TickCount64;
        await File.AppendAllTextAsync(headPath, Environment.NewLine);
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
        long elapsed = Environment.TickCount64 - started;

        Assert.IsLessThanOrEqualTo(
            DebounceMilliseconds + SchedulingAllowanceMilliseconds,
            elapsed,
            $"观察器合并通知耗时 {elapsed} 毫秒，超出合并延迟加调度余量。");
    }
}
