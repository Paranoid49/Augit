using Augit.Infrastructure.Updates;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class SemanticVersionTests
{
    [TestMethod]
    public void 解析预发布与构建元数据()
    {
        Assert.IsTrue(SemanticVersion.TryParse("1.0.0-rc.11+abc123", out SemanticVersion? version));
        Assert.IsNotNull(version);
        Assert.AreEqual(1, version.Major);
        Assert.AreEqual(0, version.Minor);
        Assert.AreEqual(0, version.Patch);
        Assert.IsTrue(version.IsPrerelease);
        Assert.AreEqual("1.0.0-rc.11", version.ToString());
    }

    [TestMethod]
    public void 预发布按数字和字典序比较且稳定版更高()
    {
        Assert.IsTrue(Try("1.0.0-rc.11", out SemanticVersion? rc11));
        Assert.IsTrue(Try("1.0.0-rc.12", out SemanticVersion? rc12));
        Assert.IsTrue(Try("1.0.0-rc.2", out SemanticVersion? rc2));
        Assert.IsTrue(Try("1.0.0", out SemanticVersion? stable));

        Assert.IsTrue(rc11 < rc12);
        Assert.IsTrue(rc2 < rc11);
        Assert.IsTrue(rc12 < stable);
    }

    [TestMethod]
    public void 拒绝无效版本()
    {
        foreach (string value in new[] { "", "1.0", "1.0.0.0", "1.0.0-", "1.0.0-rc..1", "v1.0.0" })
        {
            Assert.IsFalse(SemanticVersion.TryParse(value, out _), value);
        }
    }

    private static bool Try(string value, out SemanticVersion? version)
        => SemanticVersion.TryParse(value, out version);
}
