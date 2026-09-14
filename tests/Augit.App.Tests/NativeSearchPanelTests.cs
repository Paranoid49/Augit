namespace Augit.App.Tests;

[TestClass]
public sealed class NativeSearchPanelTests
{
    [TestMethod]
    public void 搜索结果增量只替换公共前后缀之间的连续区间()
    {
        (int prefix, int removed, int added) = NativeSearchPanel.ResultDeltaForTest(
            ["src/a.cs", "src/b.cs", "src/c.cs", "src/d.cs"],
            ["src/a.cs", "src/x.cs", "src/y.cs", "src/d.cs"]);

        Assert.AreEqual(1, prefix);
        Assert.AreEqual(2, removed);
        Assert.AreEqual(2, added);
    }

    [TestMethod]
    public void 搜索结果增量覆盖首次填充清空和完全相同快照()
    {
        Assert.AreEqual(
            (0, 0, 2),
            NativeSearchPanel.ResultDeltaForTest([], ["a.txt", "b.txt"]));
        Assert.AreEqual(
            (0, 2, 0),
            NativeSearchPanel.ResultDeltaForTest(["a.txt", "b.txt"], []));
        Assert.AreEqual(
            (2, 0, 0),
            NativeSearchPanel.ResultDeltaForTest(["a.txt", "b.txt"], ["a.txt", "b.txt"]));
    }
}
