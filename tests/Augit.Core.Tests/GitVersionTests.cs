using Augit.Core.Git;

namespace Augit.Core.Tests;

[TestClass]
public sealed class GitVersionTests
{
    [TestMethod]
    [DataRow("git version 2.40.0", 2, 40, 0)]
    [DataRow("git version 2.45.1.windows.1", 2, 45, 1)]
    [DataRow("GIT VERSION 3.0.12.preview", 3, 0, 12)]
    public void 解析Git版本并忽略发行后缀(string output, int major, int minor, int patch)
    {
        bool parsed = GitVersion.TryParse(output, out GitVersion version);

        Assert.IsTrue(parsed);
        Assert.AreEqual(new GitVersion(major, minor, patch), version);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("2.45.1")]
    [DataRow("git version 2.45")]
    [DataRow("git version x.45.1")]
    public void 拒绝无效Git版本输出(string? output)
    {
        Assert.IsFalse(GitVersion.TryParse(output, out _));
    }

    [TestMethod]
    public void 最低支持版本为二点四十()
    {
        Assert.IsFalse(new GitVersion(2, 39, 9).IsSupported);
        Assert.IsTrue(new GitVersion(2, 40, 0).IsSupported);
        Assert.IsTrue(new GitVersion(2, 40, 1).IsSupported);
        Assert.IsTrue(new GitVersion(3, 0, 0).IsSupported);
    }
}
