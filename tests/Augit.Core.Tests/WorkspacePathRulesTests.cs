using Augit.Core.Files;

namespace Augit.Core.Tests;

[TestClass]
public sealed class WorkspacePathRulesTests
{
    [TestMethod]
    public void 拒绝仅共享前缀的相邻目录()
    {
        string root = Path.Combine(Path.GetTempPath(), "AugitRoot");
        string sibling = root + "Elsewhere";

        Assert.IsFalse(WorkspacePathRules.IsWithin(root, sibling));
    }

    [TestMethod]
    public void 去重过滤越界并限制为五十项()
    {
        string root = Path.Combine(Path.GetTempPath(), "AugitRoot");
        IEnumerable<string> paths = Enumerable.Range(0, 60).Select(index => Path.Combine(root, index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        IReadOnlyList<string> result = WorkspacePathRules.NormalizeExpandedDirectories(root, paths.Append(Path.GetTempPath()));

        Assert.HasCount(50, result);
        Assert.IsTrue(result.All(path => WorkspacePathRules.IsWithin(root, path)));
    }
}
