namespace Augit.App.Tests;

[TestClass]
public sealed class WorkspaceFileChangeQueueTests
{
    private static readonly string[] FirstBatchPaths = ["a.txt", "b.txt"];
    private static readonly string[] SecondBatchPaths = ["second.txt"];

    [TestMethod]
    public void 连续事件只请求一次调度并合并路径()
    {
        WorkspaceFileChangeQueue queue = new();

        Assert.IsTrue(queue.Enqueue(["a.txt"]));
        Assert.IsFalse(queue.Enqueue(["b.txt", "a.txt"]));
        Assert.IsTrue(queue.TryBeginDrain(out WorkspaceFileChangeBatch batch));

        CollectionAssert.AreEqual(FirstBatchPaths, batch.Paths.ToArray());
        Assert.IsFalse(batch.RequiresFullRefresh);
        Assert.IsFalse(queue.TryTakeNext(out _));
        Assert.IsFalse(queue.EndDrain());
    }

    [TestMethod]
    public void 执行期间的新事件进入同一循环且不重复调度()
    {
        WorkspaceFileChangeQueue queue = new();
        Assert.IsTrue(queue.Enqueue(["first.txt"]));
        Assert.IsTrue(queue.TryBeginDrain(out WorkspaceFileChangeBatch first));

        Assert.IsFalse(queue.Enqueue(["second.txt"]));
        Assert.IsTrue(queue.TryTakeNext(out WorkspaceFileChangeBatch second));
        Assert.AreNotEqual(first.Version, second.Version);
        CollectionAssert.AreEqual(SecondBatchPaths, second.Paths.ToArray());
        Assert.IsFalse(queue.EndDrain());
    }

    [TestMethod]
    public void 新事件到达后旧批次版本失效()
    {
        WorkspaceFileChangeQueue queue = new();
        Assert.IsTrue(queue.Enqueue(["old.txt"]));
        Assert.IsTrue(queue.TryBeginDrain(out WorkspaceFileChangeBatch oldBatch));

        Assert.IsFalse(queue.Enqueue(["new.txt"]));
        Assert.IsFalse(queue.IsCurrent(oldBatch.Version));
        Assert.IsTrue(queue.TryTakeNext(out WorkspaceFileChangeBatch newBatch));
        Assert.IsTrue(queue.IsCurrent(newBatch.Version));
        Assert.IsFalse(queue.EndDrain());
    }

    [TestMethod]
    public void 空路径事件要求全量刷新()
    {
        WorkspaceFileChangeQueue queue = new();
        string workspaceRoot = Path.GetFullPath("workspace");

        Assert.IsTrue(queue.Enqueue([string.Empty]));
        Assert.IsTrue(queue.TryBeginDrain(out WorkspaceFileChangeBatch batch));
        Assert.IsTrue(batch.RequiresFullRefresh);
        Assert.IsEmpty(batch.Paths);
        CollectionAssert.AreEqual(
            new List<string> { workspaceRoot },
            batch.GetTreeRefreshDirectories(workspaceRoot).ToArray());
        Assert.IsFalse(queue.EndDrain());
    }

    [TestMethod]
    public void 普通路径只刷新直接父目录并去重()
    {
        string workspaceRoot = Path.GetFullPath("workspace");
        string firstDirectory = Path.Combine(workspaceRoot, "docs");
        string secondDirectory = Path.Combine(firstDirectory, "nested");
        WorkspaceFileChangeBatch batch = new(
            1,
            false,
            [
                Path.Combine(firstDirectory, "a.txt"),
                Path.Combine(firstDirectory, "b.txt"),
                Path.Combine(secondDirectory, "c.txt"),
            ]);

        CollectionAssert.AreEqual(
            new List<string> { secondDirectory, firstDirectory },
            batch.GetTreeRefreshDirectories(workspaceRoot).ToArray());
    }

    [TestMethod]
    public void 相同目录快照不会要求重建项目树()
    {
        string root = Path.GetFullPath("workspace");
        string child = Path.Combine(root, "docs");
        WorkspaceTreeEntry[] current =
        [
            new("docs", child, true, true),
        ];
        Augit.Core.Files.WorkspaceEntry[] incoming =
        [
            new("docs", child.ToUpperInvariant(), true, false, true),
        ];

        Assert.IsTrue(WorkspaceTreeRefreshPolicy.EntriesEqual(current, incoming));
        Assert.IsFalse(WorkspaceTreeRefreshPolicy.EntriesEqual(
            current,
            [new("docs", child, true, false, false)]));
    }

    [TestMethod]
    public void 增量登记顺序不同但目录结构相同时仍复用快照()
    {
        string root = Path.GetFullPath("workspace");
        WorkspaceTreeEntry[] current =
        [new("b.txt", Path.Combine(root, "b.txt"), false, false), new("a.txt", Path.Combine(root, "a.txt"), false, false)];
        Augit.Core.Files.WorkspaceEntry[] incoming =
        [new("a.txt", Path.Combine(root, "a.txt"), false, false, false), new("b.txt", Path.Combine(root, "b.txt"), false, false, false)];
        Assert.IsTrue(WorkspaceTreeRefreshPolicy.EntriesEqual(current, incoming));
        Assert.IsFalse(WorkspaceTreeRefreshPolicy.EntriesEqual(current, [incoming[0], incoming[1] with { Name = "B.txt" }]));
    }

    [TestMethod]
    public void 目录快照区分大小写名称且重复条目不能掩盖缺失项()
    {
        string root = Path.GetFullPath("workspace");
        WorkspaceTreeEntry[] current =
        [new("A.txt", Path.Combine(root, "A.txt"), false, false), new("a.txt", Path.Combine(root, "a.txt"), false, false)];
        Augit.Core.Files.WorkspaceEntry[] incoming =
        [new("a.txt", Path.Combine(root, "a.txt"), false, false, false), new("A.txt", Path.Combine(root, "A.txt"), false, false, false)];
        Assert.IsTrue(WorkspaceTreeRefreshPolicy.EntriesEqual(current, incoming));
        Assert.IsFalse(WorkspaceTreeRefreshPolicy.EntriesEqual(current, [incoming[0], incoming[0]]));
    }

    [TestMethod]
    public void 工作区失效会丢弃待处理事件并使旧版本失效()
    {
        WorkspaceFileChangeQueue queue = new();
        Assert.IsTrue(queue.Enqueue(["old.txt"]));
        Assert.IsTrue(queue.TryBeginDrain(out WorkspaceFileChangeBatch oldBatch));

        queue.Invalidate();

        Assert.IsFalse(queue.IsCurrent(oldBatch.Version));
        Assert.IsFalse(queue.TryTakeNext(out _));
        Assert.IsFalse(queue.EndDrain());
    }
}
