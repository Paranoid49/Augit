using Augit.Core.Git;

namespace Augit.Core.Tests;

[TestClass]
public sealed class GitDiffParserTests
{
    private const string Patch = """
        diff --git a/file.txt b/file.txt
        --- a/file.txt
        +++ b/file.txt
        @@ -1,2 +1,2 @@
         same
        -old value
        +new value
        """;

    [TestMethod]
    public void 统一Diff解析准确保留两侧行号()
    {
        IReadOnlyList<GitDiffLine> lines = GitUnifiedDiffParser.Parse(Patch);

        GitDiffLine context = lines.Single(line => line.Kind == GitDiffLineKind.Context);
        GitDiffLine removed = lines.Single(line => line.Kind == GitDiffLineKind.Removed);
        GitDiffLine added = lines.Single(line => line.Kind == GitDiffLineKind.Added);
        Assert.AreEqual(1, context.OldLineNumber);
        Assert.AreEqual(1, context.NewLineNumber);
        Assert.AreEqual(2, removed.OldLineNumber);
        Assert.IsNull(removed.NewLineNumber);
        Assert.IsNull(added.OldLineNumber);
        Assert.AreEqual(2, added.NewLineNumber);
    }

    [TestMethod]
    public void 双栏Diff配对相邻删除和新增并标记字词变化()
    {
        IReadOnlyList<GitSideBySideRow> rows = GitUnifiedDiffParser.ToSideBySide(
            GitUnifiedDiffParser.Parse(Patch));

        GitSideBySideRow changed = rows.Single(row => row.Kind == GitDiffLineKind.Modified);
        Assert.AreEqual("old value", changed.OldText);
        Assert.AreEqual("new value", changed.NewText);
        Assert.AreEqual(new GitTextSpan(0, 3), changed.OldChanges.Single());
        Assert.AreEqual(new GitTextSpan(0, 3), changed.NewChanges.Single());
    }

    [TestMethod]
    public void 中文字词变化定位到实际改变字符()
    {
        (IReadOnlyList<GitTextSpan> oldChanges, IReadOnlyList<GitTextSpan> newChanges) = GitWordDiff.FindChanges(
            "你好世界",
            "你好天地");

        Assert.AreEqual(new GitTextSpan(2, 2), oldChanges.Single());
        Assert.AreEqual(new GitTextSpan(2, 2), newChanges.Single());
    }
}
