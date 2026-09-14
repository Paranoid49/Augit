using Augit.Core.Git;

namespace Augit.Core.Tests;

[TestClass]
public sealed class GitFileSelectionTests
{
    [TestMethod]
    public void 首次出现的已跟踪改动默认勾选且未跟踪文件默认不勾选()
    {
        GitFileSelection selection = new();
        GitChangedFile tracked = CreateFile("tracked.txt");
        GitChangedFile untracked = CreateFile(
            "new.txt",
            group: GitChangeGroup.UnversionedFiles,
            kind: GitChangeKind.Untracked);

        selection.Reconcile([tracked, untracked]);

        Assert.IsTrue(selection.IsSelected("tracked.txt"));
        Assert.IsFalse(selection.IsSelected("new.txt"));
    }

    [TestMethod]
    public void 文件再次变化时保留用户取消的勾选()
    {
        GitFileSelection selection = new();
        GitChangedFile original = CreateFile("tracked.txt");
        selection.Reconcile([original]);
        selection.SetSelected(original.RelativePath, false);

        GitChangedFile changedAgain = CreateFile("tracked.txt", hasWorkingTreeChanges: true);
        selection.Reconcile([changedAgain]);

        Assert.IsFalse(selection.IsSelected("tracked.txt"));
    }

    [TestMethod]
    public void 文件离开改动列表后清除勾选()
    {
        GitFileSelection selection = new();
        selection.SetSelected("removed.txt", true);

        selection.Reconcile([CreateFile("remaining.txt")]);

        Assert.IsFalse(selection.IsSelected("removed.txt"));
        Assert.IsTrue(selection.IsSelected("remaining.txt"));
    }

    [TestMethod]
    public void 分组全选只影响给定文件()
    {
        GitFileSelection selection = new();
        GitChangedFile first = CreateFile("first.txt");
        GitChangedFile second = CreateFile("second.txt");
        selection.SetSelected("outside.txt", true);

        selection.SetGroupSelected([first, second], true);
        selection.SetGroupSelected([first], false);

        Assert.IsFalse(selection.IsSelected("first.txt"));
        Assert.IsTrue(selection.IsSelected("second.txt"));
        Assert.IsTrue(selection.IsSelected("outside.txt"));
    }

    private static GitChangedFile CreateFile(
        string path,
        bool hasWorkingTreeChanges = false,
        GitChangeGroup group = GitChangeGroup.Changes,
        GitChangeKind kind = GitChangeKind.Modified)
    {
        return new(
            path,
            null,
            group,
            kind,
            true,
            hasWorkingTreeChanges);
    }
}
