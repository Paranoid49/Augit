using Augit.Core.Files;

namespace Augit.Core.Tests;

[TestClass]
public sealed class NaturalNameComparerTests
{
    private static readonly string[] ExpectedNames = ["file1", "File2", "file10"];

    [TestMethod]
    public void 不区分大小写并按数字值排序()
    {
        List<string> names = ["file10", "File2", "file1"];

        names.Sort(NaturalNameComparer.Instance);

        CollectionAssert.AreEqual(ExpectedNames, names);
    }
}
