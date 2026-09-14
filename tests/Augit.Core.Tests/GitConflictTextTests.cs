using Augit.Core.Git;

namespace Augit.Core.Tests;

[TestClass]
public sealed class GitConflictTextTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string Conflict = """
        before
        <<<<<<< HEAD
        当前内容
        ||||||| base
        共同祖先
        =======
        合入内容
        >>>>>>> feature
        after
        """;

    [TestMethod]
    public void 冲突块解析保留两侧祖先和边界()
    {
        GitConflictBlock block = GitConflictText.Parse(Conflict).Single();

        Assert.AreEqual("当前内容\n", block.YoursText);
        Assert.AreEqual("共同祖先\n", block.AncestorText);
        Assert.AreEqual("合入内容\n", block.TheirsText);
        Assert.AreEqual("<<<<<<< HEAD", Conflict.Substring(block.Start, 12));
    }

    [TestMethod]
    [DataRow(GitConflictBlockChoice.Yours, "当前内容\n")]
    [DataRow(GitConflictBlockChoice.Theirs, "合入内容\n")]
    [DataRow(GitConflictBlockChoice.Both, "当前内容\n合入内容\n")]
    public void 接受任一侧或两侧后不再保留冲突标记(
        GitConflictBlockChoice choice,
        string expectedMiddle)
    {
        bool resolved = GitConflictText.TryResolveBlock(Conflict, 0, choice, out string? result);

        Assert.IsTrue(resolved);
        Assert.AreEqual($"before\n{expectedMiddle}after", result);
        Assert.IsEmpty(GitConflictText.Parse(result!));
    }

    [TestMethod]
    public void 多个冲突块按当前文本重新编号()
    {
        string text = string.Concat(Conflict, "\n", Conflict);

        Assert.IsTrue(GitConflictText.TryResolveBlock(text, 0, GitConflictBlockChoice.Yours, out string? once));
        Assert.HasCount(1, GitConflictText.Parse(once!));
        Assert.IsTrue(GitConflictText.TryResolveBlock(once!, 0, GitConflictBlockChoice.Theirs, out string? twice));
        Assert.IsEmpty(GitConflictText.Parse(twice!));
    }

    [TestMethod]
    public void 不完整标记不会被误判为可解决冲突块()
    {
        const string malformed = "<<<<<<< HEAD\n当前内容\n=======\n缺少结束标记\n";

        Assert.IsEmpty(GitConflictText.Parse(malformed));
        Assert.IsFalse(GitConflictText.TryResolveBlock(
            malformed,
            0,
            GitConflictBlockChoice.Yours,
            out string? result));
        Assert.IsNull(result);
    }

    [TestMethod]
    public void 已取消的解析不返回正常结果()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => GitConflictText.Parse(Conflict, cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => GitConflictText.Parse(string.Empty, cancellation.Token));
    }

    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void 长行之后的末尾冲突保留完整边界和换行(string newline)
    {
        string prefix = new string('a', 2_000_000) + newline;
        string conflict = $"<<<<<<< HEAD{newline}左😀{newline}======={newline}右{newline}>>>>>>> feature";
        string text = prefix + conflict;
        IReadOnlyList<GitConflictBlock> blocks = GitConflictText.Parse(text);

        Assert.HasCount(1, blocks);
        Assert.AreEqual(prefix.Length, blocks[0].Start);
        Assert.AreEqual(conflict.Length, blocks[0].Length);
        Assert.AreEqual($"左😀{newline}", blocks[0].YoursText);
        Assert.AreEqual($"右{newline}", blocks[0].TheirsText);
        Assert.IsNull(blocks[0].AncestorText);
        Assert.IsTrue(GitConflictText.TryResolveBlock(text, 0, GitConflictBlockChoice.Both, out string? result));
        Assert.AreEqual($"{prefix}左😀{newline}右{newline}", result);
    }

    [TestMethod]
    [DataRow("<<<<<<< unfinished\nleft\n")]
    [DataRow("<<<<<<< unfinished\nleft\n=======\nright\n")]
    [DataRow("<<<<<<< unfinished\nleft\n||||||| base\nancestor\n")]
    public void 新起始标记舍弃未闭合外层而保留完整内层(string prefix)
    {
        const string complete = "<<<<<<< HEAD\n左\n=======\n右\n>>>>>>> branch\n";
        string text = prefix + complete + ">>>>>>> orphan\n";
        GitConflictBlock block = GitConflictText.Parse(text).Single();

        Assert.AreEqual(prefix.Length, block.Start);
        Assert.AreEqual(complete.Length, block.Length);
        Assert.AreEqual("左\n", block.YoursText);
        Assert.AreEqual("右\n", block.TheirsText);
        Assert.IsNull(block.AncestorText);
        Assert.IsTrue(GitConflictText.TryResolveBlock(text, 0, GitConflictBlockChoice.Theirs, out string? result));
        Assert.AreEqual(prefix + "右\n>>>>>>> orphan\n", result);
    }

    [TestMethod]
    [DataRow(" <<<<<<< HEAD\n左\n=======\n右\n>>>>>>> branch")]
    [DataRow("<<<<<<<< HEAD\n左\n=======\n右\n>>>>>>> branch")]
    [DataRow("<<<<<<<\tHEAD\n左\n=======\n右\n>>>>>>> branch")]
    [DataRow("<<<<<<< HEAD\n左\n======= extra\n右\n>>>>>>> branch")]
    [DataRow("<<<<<<< HEAD\n左\n=======\n右\n>>>>>>>> branch")]
    public void 相似正文不被误认为完整冲突标记(string text)
    {
        Assert.IsEmpty(GitConflictText.Parse(text));
    }

    [TestMethod]
    public void 空侧和空祖先仍可接受且保留块外内容()
    {
        const string text = "before\n<<<<<<<\n|||||||\n=======\n>>>>>>>\nafter";
        GitConflictBlock block = GitConflictText.Parse(text).Single();

        Assert.AreEqual(string.Empty, block.YoursText);
        Assert.AreEqual(string.Empty, block.AncestorText);
        Assert.AreEqual(string.Empty, block.TheirsText);
        Assert.IsTrue(GitConflictText.TryResolveBlock(text, 0, GitConflictBlockChoice.Both, out string? result));
        Assert.AreEqual("before\nafter", result);
    }

    [TestMethod]
    public void 千万空行的已解决文本解析不按行数分配内存()
    {
        string text = new('\n', 10 * 1024 * 1024);
        _ = GitConflictText.Parse("warmup\n");
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        IReadOnlyList<GitConflictBlock> blocks = GitConflictText.Parse(text);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

        Assert.IsEmpty(blocks);
        // 输入分配不计入解析开销；防止恢复为每行创建字符串和对象的大文件路径。
        Assert.IsLessThan(64 * 1024, allocated, $"无冲突文本额外分配了 {allocated} 字节。");
        TestContext.WriteLine($"10 MiB 换行文本：解析额外分配 {allocated} 字节，耗时 {elapsed.TotalMilliseconds:F2}ms；不代表界面响应验收。");
    }
}
