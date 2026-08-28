using Augit.Core.Git;

namespace Augit.Core.Tests;

[TestClass]
public sealed class GitFileSelectionTests
{
    [TestMethod]
    public void 文件再次变化时保留勾选且新文件默认不勾选()
    {
        GitFileSelection selection = new();
        GitChangedFile original = CreateFile("tracked.txt");
        selection.SetSelected(original.RelativePath, true);

        GitChangedFile changedAgain = CreateFile("tracked.txt", hasWorkingTreeChanges: true);
        GitChangedFile newFile = CreateFile("new.txt");
        selection.Reconcile([changedAgain, newFile]);

        Assert.IsTrue(selection.IsSelected("tracked.txt"));
        Assert.IsFalse(selection.IsSelected("new.txt"));
    }

    [TestMethod]
    public void 文件离开改动列表后清除勾选()
    {
        GitFileSelection selection = new();
        selection.SetSelected("removed.txt", true);

        selection.Reconcile([CreateFile("remaining.txt")]);

        Assert.IsEmpty(selection.SelectedPaths);
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

    private static GitChangedFile CreateFile(string path, bool hasWorkingTreeChanges = false)
    {
        return new(
            path,
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Modified,
            true,
            hasWorkingTreeChanges);
    }
}
