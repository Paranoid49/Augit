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

    /// <summary>
    /// 规格 §9.2「多个文件系统与 `.git` 事件必须合并」的文件系统一侧：
    /// 合并窗口（`WorkspaceFileWatcher.MergeDelay` = 50 毫秒）内的多个文件事件必须落在同一批里，
    /// 同一路径的重复事件在批内去重。
    /// </summary>
    [TestMethod]
    public async Task 多个文件事件合并成一批且同路径去重()
    {
        using TemporaryDirectory temporary = new();
        List<IReadOnlyList<string>> batches = [];
        using WorkspaceFileWatcher watcher = new(temporary.FullPath);
        watcher.Changed += (_, eventArgs) =>
        {
            lock (batches)
            {
                batches.Add(eventArgs.Paths);
            }
        };
        string[] expected =
        [
            temporary.GetPath("a.txt"),
            temporary.GetPath("b.txt"),
            temporary.GetPath("c.txt"),
        ];

        // 三个文件一起写（事件几乎同时到达），其中 `a.txt` 写两次 ⇒ 同一条路径出现两个事件。
        // `a.txt` 的两次写必须**串行**：`File.WriteAllTextAsync` 以 `FileShare.Read` 打开文件，
        // 同一路径并发写会抛 `IOException: because it is being used by another process`
        // （全量套件并行执行时必现）。三个写入者仍互相并发，因此四个事件都落在同一个合并窗口内。
        Task[] writes =
        [
            WriteTwiceAsync(expected[0]),
            File.WriteAllTextAsync(expected[1], "变化"),
            File.WriteAllTextAsync(expected[2], "变化"),
        ];
        await Task.WhenAll(writes);
        await Task.Delay(700);

        IReadOnlyList<string>? first;
        lock (batches)
        {
            first = batches.Count > 0 ? batches[0] : null;
        }

        Assert.IsNotNull(first, "应至少收到一次合并通知。");
        // 一批里同时含三个**不同**路径、且 `a.txt` 的两个事件只占一格 ⇒ 多个事件被合并且同路径去重。
        Assert.HasCount(3, first!);
        foreach (string path in expected)
        {
            Assert.IsTrue(first!.Contains(path, StringComparer.OrdinalIgnoreCase), path);
        }

        // 任何一个批次内部都不得出现重复路径（后续窗口也一样）。
        lock (batches)
        {
            foreach (IReadOnlyList<string> batch in batches)
            {
                Assert.HasCount(batch.Distinct(StringComparer.OrdinalIgnoreCase).Count(), batch);
            }
        }
    }

    /// <summary>
    /// 对同一路径连续写两次，制造"同一路径的两个文件系统事件"。
    /// 两次写**串行**执行：同一路径并发写会因文件句柄独占而失败，串行仍能在合并窗口内产生两个事件。
    /// </summary>
    private static async Task WriteTwiceAsync(string path)
    {
        await File.WriteAllTextAsync(path, "变化");
        await File.WriteAllTextAsync(path, "再改");
    }
}
