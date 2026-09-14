using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeCommitGraphTests
{
    private static readonly int[] MergeColumns = [0, 1, 0, 0];
    private static readonly int[] MergeParentColumns = [0, 1];
    [TestMethod]
    public void 线性历史只连接真实父提交且根节点下方没有连线()
    {
        NativeCommitGraph graph = NativeCommitGraph.Build([Entry("c", "b"), Entry("b", "a"), Entry("a")]);
        Assert.AreEqual(1, graph.ColumnCount);
        Assert.IsTrue(graph.Rows.All(row => row.Column == 0));
        Assert.IsFalse(graph.Rows[0].Segments.Any(segment => segment.FromY == 0));
        Assert.IsTrue(graph.Rows[1].Segments.Any(segment => segment.FromY == 0));
        Assert.IsFalse(graph.Rows[2].Segments.Any(segment => segment.ToY > 0.5f));
    }

    [TestMethod]
    public void 合并提交分叉后沿各自轨道回到共同父提交()
    {
        NativeCommitGraph graph = NativeCommitGraph.Build([
            Entry("merge", "main", "branch"), Entry("branch", "root"), Entry("main", "root"), Entry("root"),
        ]);
        Assert.AreEqual(2, graph.ColumnCount);
        CollectionAssert.AreEqual(MergeColumns, graph.Rows.Select(row => row.Column).ToArray());
        CollectionAssert.AreEquivalent(MergeParentColumns, graph.Rows[0].Segments.Select(segment => segment.ToColumn).ToArray());
        Assert.AreNotEqual(graph.Rows[1].Color, graph.Rows[2].Color);
        Assert.IsTrue(graph.Rows[2].Segments.Any(segment => segment.FromColumn == 1 && segment.ToColumn == 0));
        Assert.HasCount(1, graph.Rows[3].Segments);
    }

    [TestMethod]
    public void 筛选掉的父提交不得错误连接到下一条结果()
    {
        NativeCommitGraph graph = NativeCommitGraph.Build([Entry("selected", "hidden"), Entry("unrelated")]);
        NativeCommitGraphSegment segment = graph.Rows[0].Segments.Single();
        Assert.IsTrue(segment.Dashed);
        Assert.AreEqual(0.5f, segment.FromY);
        Assert.AreEqual(0.92f, segment.ToY);
        Assert.IsEmpty(graph.Rows[1].Segments);
        Assert.AreEqual(1, graph.ColumnCount);
    }

    [TestMethod]
    public void 多轨图宽度按十六像素轨道间距增长()
    {
        NativeCommitGraph graph = NativeCommitGraph.Build([
            Entry("merge", "main", "branch"), Entry("branch", "root"), Entry("main", "root"), Entry("root"),
        ]);

        Assert.AreEqual(NativeTheme.Scale(45), graph.Width);
    }

    [TestMethod]
    public void 浅色主轨颜色使用PyCharm参考截图采样值()
    {
        Assert.AreEqual(Rgb(71, 161, 179), NativeTheme.GitGraphColor(0, dark: false));
    }

    [TestMethod]
    public void 虚线延续从段起点到终点绘制而不是固定在行中部()
    {
        NativeCommitGraphSegment segment = new(1, 0.2f, 0, 0.9f, 0, Dashed: true);
        NativeGdiPlusDrawing.StrokeLine[] lines = NativeCommitGraph.BuildDashedLinesForTest(
            segment,
            fromX: 31,
            toX: 15,
            top: 100,
            height: 27);

        Assert.HasCount(2, lines);
        Assert.AreEqual(100 + 0.2f * 27, lines[0].StartY, 0.01f);
        Assert.AreEqual(100 + 0.9f * 27, lines[1].EndY, 0.01f);
        Assert.IsGreaterThan(lines[0].EndX, lines[0].StartX);
        Assert.IsGreaterThan(lines[1].EndX, lines[1].StartX);
    }

    [TestMethod]
    public void 追加历史页后将末尾延续标记接到真实父提交()
    {
        NativeCommitGraph first = NativeCommitGraph.Build([Entry("c", "b")]);
        Assert.IsTrue(first.Rows[0].Segments.Single().Dashed);
        NativeCommitGraph appended = NativeCommitGraph.Build([Entry("c", "b"), Entry("b")]);
        Assert.IsFalse(appended.Rows[0].Segments.Single().Dashed);
        Assert.AreEqual(0, appended.Rows[1].Segments.Single().FromY);
    }

    [TestMethod]
    public void 多父提交和不相关根节点保持边界合法并识别HEAD()
    {
        GitHistoryEntry head = Entry("octopus", "a", "b", "c") with
        {
            References = [new(GitReferenceKind.LocalBranch, "main", IsHead: true)],
        };
        NativeCommitGraph graph = NativeCommitGraph.Build([head, Entry("c"), Entry("b"), Entry("a"), Entry("unrelated")]);
        Assert.AreEqual(3, graph.ColumnCount);
        Assert.IsTrue(graph.Rows[0].IsHead);
        Assert.IsEmpty(graph.Rows[^1].Segments);
        foreach (NativeCommitGraphSegment segment in graph.Rows.SelectMany(row => row.Segments))
        {
            Assert.IsGreaterThanOrEqualTo(0, segment.FromColumn);
            Assert.IsGreaterThanOrEqualTo(0, segment.ToColumn);
            Assert.IsLessThan(graph.ColumnCount, segment.FromColumn);
            Assert.IsLessThan(graph.ColumnCount, segment.ToColumn);
        }
    }

    private static GitHistoryEntry Entry(string hash, params string[] parents) =>
        new("", hash, hash, parents, "作者", "author@example.com", DateTimeOffset.UnixEpoch, hash, []);

    [TestMethod]
    public void 合并缺失的父关系不会被同列实线盖住()
    {
        NativeCommitGraph graph = NativeCommitGraph.Build([Entry("merge", "main", "hidden"), Entry("main")]);
        NativeCommitGraphSegment solid = graph.Rows[0].Segments.Single(segment => !segment.Dashed);
        NativeCommitGraphSegment missing = graph.Rows[0].Segments.Single(segment => segment.Dashed);
        Assert.AreNotEqual(solid.ToColumn, missing.ToColumn);
        Assert.IsLessThan(1f, missing.ToY);
        Assert.AreEqual(2, graph.ColumnCount);
        Assert.AreEqual(solid.ToColumn, graph.Rows[1].Column);
    }

    [TestMethod]
    public void 第一父缺失时第二父保持次轨颜色且追加后不变色()
    {
        GitHistoryEntry[] entries = [Entry("merge", "main", "branch"), Entry("branch", "root"), Entry("main", "root"), Entry("root")];
        NativeCommitGraph first = NativeCommitGraph.Build(entries[..2]);
        NativeCommitGraph full = NativeCommitGraph.Build(entries);
        Assert.AreNotEqual(first.Rows[0].Color, first.Rows[1].Color);
        Assert.AreEqual(first.Rows[1].Color, full.Rows[1].Color);
        Assert.IsTrue(first.Rows[0].Segments.Any(segment => segment.Dashed));
        Assert.IsFalse(full.Rows.SelectMany(row => row.Segments).Any(segment => segment.Dashed));
    }

    [TestMethod]
    public void 十二条分支的实线在每个行边界连续且最后收敛到共同根()
    {
        string[] branches = Enumerable.Range(0, 12).Select(index => $"branch-{index}").ToArray();
        GitHistoryEntry[] entries = [Entry("merge", branches), .. branches.Select(hash => Entry(hash, "root")), Entry("root")];
        NativeCommitGraph graph = NativeCommitGraph.Build(entries);
        Assert.AreEqual(12, graph.ColumnCount);
        for (int index = 0; index < graph.Rows.Count - 1; index++)
        {
            var outgoing = graph.Rows[index].Segments.Where(segment => segment.ToY == 1)
                .Select(segment => (segment.ToColumn, segment.Color)).Distinct().ToArray();
            var incoming = graph.Rows[index + 1].Segments.Where(segment => segment.FromY == 0)
                .Select(segment => (segment.FromColumn, segment.Color)).Distinct().ToArray();
            CollectionAssert.AreEquivalent(outgoing, incoming, $"第 {index} 行边界出现断线或错误颜色。");
        }
        Assert.IsFalse(graph.Rows[^1].Segments.Any(segment => segment.ToY > 0.5f));
    }

    [TestMethod]
    public void 多个隐藏父各自保留短分叉且不连接无关根()
    {
        NativeCommitGraph graph = NativeCommitGraph.Build([Entry("merge", "a", "b", "c"), Entry("root")]);
        Assert.HasCount(3, graph.Rows[0].Segments);
        Assert.AreEqual(3, graph.Rows[0].Segments.Select(segment => segment.ToColumn).Distinct().Count());
        Assert.IsTrue(graph.Rows[0].Segments.All(segment => segment.Dashed && segment.ToY < 1));
        Assert.IsEmpty(graph.Rows[1].Segments);
    }

    private static uint Rgb(byte red, byte green, byte blue) =>
        red | ((uint)green << 8) | ((uint)blue << 16);
}
