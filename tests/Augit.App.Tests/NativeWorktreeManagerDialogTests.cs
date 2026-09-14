using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeWorktreeManagerDialogTests
{
    private static readonly string[] ExpectedDetailLabels = ["路径", "状态", "终端会话"];
    private static readonly string[] ExpectedCreateLabels = ["目标路径", "来源分支", "新分支（可选）"];
    private static readonly string[] ExpectedDetailActions = ["打开窗口", "新建 Worktree", "移除…"];
    private static readonly int[] ExpectedDetailTabOrder = [1, 13, 19, 15, 17, 18, 10, 11, 12];
    private static readonly int[] ExpectedCreateTabOrder = [20, 21, 22, 14, 17, 18, 10, 12, 1];

    [TestMethod]
    public void Worktree管理使用与远端一致的宽双栏结构()
    {
        (int width, int height, int header, int footer, int sidebar) =
            NativeWorktreeManagerDialog.LogicalLayoutForTest;

        Assert.AreEqual((930, 365, 45, 53, 260), (width, height, header, footer, sidebar));
    }

    [TestMethod]
    public void Worktree详情与新建表单使用互不混淆的字段()
    {
        CollectionAssert.AreEqual(
            ExpectedDetailLabels,
            NativeWorktreeManagerDialog.DetailLabelsForTest.ToArray());
        CollectionAssert.AreEqual(
            ExpectedCreateLabels,
            NativeWorktreeManagerDialog.CreateLabelsForTest.ToArray());
        CollectionAssert.AreEqual(
            ExpectedDetailActions,
            NativeWorktreeManagerDialog.DetailActionLabelsForTest.ToArray());
    }

    [TestMethod]
    public void Worktree列表同时显示分支和路径()
    {
        GitWorktreeInfo worktree = CreateWorktree("D:/github/Augit-ux", "feature/ux");

        Assert.AreEqual(
            "feature/ux · D:\\github\\Augit-ux",
            NativeWorktreeManagerDialog.FormatListEntryForTest(worktree));
    }

    [TestMethod]
    public void 首次打开优先查看可操作的链接Worktree并保留已有选择()
    {
        GitWorktreeInfo current = CreateWorktree("D:\\github\\Augit", "main") with { IsCurrent = true };
        GitWorktreeInfo linked = CreateWorktree("D:\\github\\Augit-ux", "feature/ux");
        GitWorktreeInfo[] worktrees = [current, linked];

        Assert.AreEqual(1, NativeWorktreeManagerDialog.ChooseSelectionIndexForTest(worktrees, null));
        Assert.AreEqual(0, NativeWorktreeManagerDialog.ChooseSelectionIndexForTest(worktrees, current.Path));
    }

    [TestMethod]
    public void Worktree详情和新建模式具有完整键盘循环()
    {
        CollectionAssert.AreEqual(
            ExpectedDetailTabOrder,
            NativeWorktreeManagerDialog.DetailTabOrderForTest.ToArray());
        CollectionAssert.AreEqual(
            ExpectedCreateTabOrder,
            NativeWorktreeManagerDialog.CreateTabOrderForTest.ToArray());
    }

    [TestMethod]
    public void 只有干净且没有终端占用的非当前Worktree允许移除()
    {
        GitWorktreeInfo linked = CreateWorktree("D:\\github\\Augit-ux", "feature/ux");
        GitWorktreeRemovalReadiness clean = new(true, true, false, null);
        GitWorktreeRemovalReadiness occupied = new(false, true, true, "存在运行中的内置终端会话。");
        GitWorktreeInfo current = linked with { IsCurrent = true };

        Assert.AreEqual(
            ("干净，可安全移除", "无运行中的内置终端", true),
            NativeWorktreeManagerDialog.RemovalPresentationForTest(linked, clean));
        Assert.AreEqual(
            ("存在运行中的内置终端会话。", "有运行中的内置终端", false),
            NativeWorktreeManagerDialog.RemovalPresentationForTest(linked, occupied));
        Assert.IsFalse(NativeWorktreeManagerDialog.RemovalPresentationForTest(current, clean).CanRemove);
    }

    private static GitWorktreeInfo CreateWorktree(string path, string branch) =>
        new(
            path,
            "0123456789abcdef",
            branch,
            false,
            false,
            false,
            null,
            false,
            null,
            false);
}
