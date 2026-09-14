using Augit.Core.Git;

namespace Augit.Core.Tests;

[TestClass]
public sealed class GitDiffNavigationTests
{
    [TestMethod]
    public void 连续替换只计一处且两种布局定位各自正文()
    {
        const string patch = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1,4 +1,5 @@
             上文
            -旧甲
            -旧乙
            +新甲
            +新乙
            +新丙
             中间未变
            -末尾旧值
            +末尾新值
            """;
        IReadOnlyList<GitDiffLine> lines = GitUnifiedDiffParser.Parse(patch);
        int[] expectedUnified = [2, 8];
        int[] expectedSideBySide = [2, 6];
        CollectionAssert.AreEqual(expectedUnified, GitDiffNavigation.GetChangeStartLines(lines.Select(line => line.Kind)));
        CollectionAssert.AreEqual(expectedSideBySide, GitDiffNavigation.GetChangeStartLines(
            GitUnifiedDiffParser.ToSideBySide(lines).Select(row => row.Kind)));
    }

    [TestMethod]
    public void 无上下文补丁段也保留两个独立导航目标()
    {
        const string patch = """
            @@ -1 +1 @@
            -旧首行
            +新首行
            @@ -100 +100 @@
            -旧尾行
            +新尾行
            """;
        IReadOnlyList<GitDiffLine> lines = GitUnifiedDiffParser.Parse(patch);
        int[] expectedUnified = [1, 3];
        int[] expectedSideBySide = [1, 2];
        CollectionAssert.AreEqual(expectedUnified, GitDiffNavigation.GetChangeStartLines(lines.Select(line => line.Kind)));
        CollectionAssert.AreEqual(expectedSideBySide, GitDiffNavigation.GetChangeStartLines(
            GitUnifiedDiffParser.ToSideBySide(lines).Select(row => row.Kind)));
    }

    [TestMethod]
    public void 文件末尾无换行标记不拆开替换()
    {
        GitDiffLineKind[] kinds =
        [
            GitDiffLineKind.HunkHeader,
            GitDiffLineKind.Removed,
            GitDiffLineKind.NoNewlineMarker,
            GitDiffLineKind.Added,
            GitDiffLineKind.NoNewlineMarker,
        ];
        int[] expected = [1];
        CollectionAssert.AreEqual(expected, GitDiffNavigation.GetChangeStartLines(kinds));
    }

    [TestMethod]
    public void 纯新增删除与空行改动按连续块导航()
    {
        GitDiffLineKind[] kinds =
        [
            GitDiffLineKind.Added, GitDiffLineKind.Added,
            GitDiffLineKind.Context,
            GitDiffLineKind.Removed, GitDiffLineKind.Removed,
            GitDiffLineKind.Context,
            GitDiffLineKind.Modified,
        ];
        int[] expected = [1, 4, 7];
        CollectionAssert.AreEqual(expected, GitDiffNavigation.GetChangeStartLines(kinds));
        Assert.IsEmpty(GitDiffNavigation.GetChangeStartLines([]));
        Assert.IsEmpty(GitDiffNavigation.GetChangeStartLines([GitDiffLineKind.Metadata, GitDiffLineKind.Context]));
    }
}
