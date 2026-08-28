using Augit.Core.Git;

namespace Augit.Core.Tests;

[TestClass]
public sealed class GitConflictTextTests
{
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
}
