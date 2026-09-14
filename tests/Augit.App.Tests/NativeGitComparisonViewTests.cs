using Augit.Core.Git;
namespace Augit.App.Tests;

[TestClass]
public sealed class NativeGitComparisonViewTests
{
    [TestMethod]
    public void 引用比较文件栏显示双方引用和路径()
    {
        string text = NativeGitComparisonView.BuildFileBarTextForTest(
            CreateDocument(GitDiffContentStatus.Ready));

        Assert.Contains("HEAD~1", text, StringComparison.Ordinal);
        Assert.Contains("工作区", text, StringComparison.Ordinal);
        Assert.Contains("docs/中文文件.txt", text, StringComparison.Ordinal);
    }

    [TestMethod]
    public void 长哈希的比较标题优先保留文件名并缩短双方引用()
    {
        var document = CreateDocument(GitDiffContentStatus.Ready) with
        {
            BaseRevision = "0123456789abcdef0123456789abcdef01234567",
            TargetRevision = "fedcba9876543210fedcba9876543210fedcba98",
        };
        NativeGitComparisonCaption caption = NativeGitComparisonCaption.Create(document);

        Assert.AreEqual("比较: 中文文件.txt", caption.FileTitle);
        Assert.AreEqual("01234567", caption.BaseRevision);
        Assert.AreEqual("fedcba98", caption.TargetRevision);
        StringAssert.Contains(caption.Title, "中文文件.txt");
        StringAssert.Contains(caption.Title, "01234567");
        StringAssert.Contains(caption.Title, "fedcba98");
    }

    [TestMethod]
    public void 命名引用保持完整且祖先后缀保留()
    {
        Assert.AreEqual("main", NativeGitComparisonCaption.ShortenRevision("main"));
        Assert.AreEqual("aaaaaaaa^2", NativeGitComparisonCaption.ShortenRevision(new string('a', 64) + "^2"));
        Assert.AreEqual("feature/中文分支", NativeGitComparisonCaption.ShortenRevision("feature/中文分支"));
        Assert.AreEqual(new string('z', 40), NativeGitComparisonCaption.ShortenRevision(new string('z', 40)));
        Assert.AreEqual(
            "01234567~2",
            NativeGitComparisonCaption.ShortenRevision("0123456789abcdef0123456789abcdef01234567~2"));
    }

    [TestMethod]
    public void 引用比较文件栏与正文严格位于视觉稿高度之后()
    {
        Assert.AreEqual(NativeTheme.Scale(39), NativeGitComparisonView.ToolbarHeightForTest);
        Assert.AreEqual(NativeTheme.Scale(31), NativeGitComparisonView.FileBarHeightForTest);
        Assert.AreEqual(
            NativeTheme.Scale(70),
            NativeGitComparisonView.ContentTopForTest);
        Assert.AreEqual(NativeTheme.Scale(84), NativeGitComparisonView.GutterWidthForTest);
    }

    [TestMethod]
    public void 双栏正文把两侧行号固定到独立中间栏()
    {
        var rendered = NativeGitComparisonView.RenderSideBySideForTest(
            "@@ -1 +1 @@\n-旧内容\n+新内容\n");

        Assert.AreEqual("旧内容", rendered.OldText.TrimEnd('\r', '\n'));
        Assert.AreEqual("新内容", rendered.NewText.TrimEnd('\r', '\n'));
        Assert.Contains("1", rendered.GutterText, StringComparison.Ordinal);
        Assert.DoesNotContain("1", rendered.OldText, StringComparison.Ordinal);
        Assert.DoesNotContain("1", rendered.NewText, StringComparison.Ordinal);
        Assert.HasCount(1, rendered.ChangedLines);
        Assert.AreEqual(1, rendered.ChangedLines[0]);
    }

    [TestMethod]
    public void 引用比较两种布局的变更块数量一致且保留补丁段边界()
    {
        const string patch = "@@ -1,2 +1,3 @@\n-甲\n-乙\n+丙\n+丁\n+戊\n@@ -100 +101 @@\n-旧尾行\n+新尾行\n";
        int[] expectedUnified = [1, 6];
        int[] expectedSideBySide = [1, 4];
        CollectionAssert.AreEqual(expectedUnified, NativeGitComparisonView.RenderUnifiedChangeStartsForTest(patch).ToArray());
        CollectionAssert.AreEqual(expectedSideBySide, NativeGitComparisonView.RenderSideBySideForTest(patch).ChangedLines.ToArray());
    }

    [TestMethod]
    [DataRow(GitDiffContentStatus.Binary)]
    [DataRow(GitDiffContentStatus.SideTooLarge)]
    [DataRow(GitDiffContentStatus.OutputTooLarge)]
    public void 引用比较摘要状态具有局部说明(GitDiffContentStatus status)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(NativeGitComparisonView.ResolveNoticeForTest(status)));
    }

    private static GitComparisonDocument CreateDocument(GitDiffContentStatus status)
    {
        string? patch = status == GitDiffContentStatus.Ready
            ? "@@ -1 +1 @@\n-旧内容\n+新内容\n"
            : null;
        return new(
            status,
            "HEAD~1",
            null,
            "docs/中文文件.txt",
            patch);
    }

}
