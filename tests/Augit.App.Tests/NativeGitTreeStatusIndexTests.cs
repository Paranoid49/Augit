using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeGitTreeStatusIndexTests
{
    [TestMethod]
    public void 项目树按工作区相对路径解析Git状态()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "Augit-Tree-Status");
        NativeGitTreeStatusIndex index = new(
            workspace,
            [
                new(
                    "docs/readme.md",
                    null,
                    GitChangeGroup.UnversionedFiles,
                    GitChangeKind.Untracked,
                    HasStagedChanges: false,
                    HasWorkingTreeChanges: true),
            ]);

        GitChangeKind? status = index.Resolve(Path.Combine(workspace, "docs", "readme.md"));

        Assert.AreEqual(GitChangeKind.Untracked, status);
        Assert.IsNull(index.Resolve(Path.Combine(workspace, "docs", "other.md")));
        Assert.IsNull(index.Resolve(Path.Combine(workspace, "..", "outside.md")));
    }
}
