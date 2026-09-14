using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeBranchPopupTests
{
    [TestMethod]
    public void 分支弹层按视觉稿排列快捷动作和引用分组()
    {
        GitReferenceSnapshot snapshot = CreateSnapshot();

        IReadOnlyList<NativeBranchPopupRow> rows = NativeBranchPopup.BuildRowsForTest(snapshot, string.Empty);

        CollectionAssert.AreEqual(
            new[]
            {
                UiText.UpdateProject,
                UiText.CommitEllipsis,
                UiText.PushEllipsis,
                UiText.CreateBranchEllipsis,
                UiText.CheckoutTagOrRevision,
                UiText.LocalReferences,
                "main",
                "feature",
                UiText.RemoteReferences,
                "origin/main",
                UiText.TagReferences,
                "v1.0",
            },
            rows.Select(row => row.Label).ToArray());
        Assert.IsTrue(rows.Single(row => row.Label == "main").Branch!.IsCurrent);
    }

    [TestMethod]
    public void 搜索同时筛选操作分支和标签且不保留空分组()
    {
        GitReferenceSnapshot snapshot = CreateSnapshot();

        IReadOnlyList<NativeBranchPopupRow> branchRows = NativeBranchPopup.BuildRowsForTest(snapshot, "feature");
        IReadOnlyList<NativeBranchPopupRow> actionRows = NativeBranchPopup.BuildRowsForTest(snapshot, "推送");
        IReadOnlyList<NativeBranchPopupRow> emptyRows = NativeBranchPopup.BuildRowsForTest(snapshot, "不存在");

        CollectionAssert.AreEqual(
            new[] { UiText.LocalReferences, "feature" },
            branchRows.Select(row => row.Label).ToArray());
        CollectionAssert.AreEqual(
            new[] { UiText.PushEllipsis },
            actionRows.Select(row => row.Label).ToArray());
        CollectionAssert.AreEqual(
            new[] { UiText.NoMatchingBranchesOrActions },
            emptyRows.Select(row => row.Label).ToArray());
    }

    [TestMethod]
    public void 当前分支二级动作与视觉稿一致且不会显示检出或删除()
    {
        NativeBranchPopupRow current = NativeBranchPopup.BuildRowsForTest(CreateSnapshot(), string.Empty)
            .Single(row => row.Label == "main");

        IReadOnlyList<NativeBranchPopupRow> actions = NativeBranchPopup.BuildActionsForTest(current);

        CollectionAssert.AreEqual(
            new[]
            {
                "从 main 新建分支…",
                UiText.CompareWithWorkspace,
                UiText.CreateWorktreeEllipsis,
                UiText.PushEllipsis,
                UiText.RenameEllipsis,
            },
            actions.Select(row => row.Label).ToArray());
        Assert.IsFalse(actions.Any(row => row.Request?.Command == NativeBranchPopupCommand.CheckoutBranch));
        Assert.IsFalse(actions.Any(row => row.Request?.Command == NativeBranchPopupCommand.DeleteReference));
    }

    [TestMethod]
    public void 远程分支与标签显示各自可用的二级动作()
    {
        IReadOnlyList<NativeBranchPopupRow> rows = NativeBranchPopup.BuildRowsForTest(CreateSnapshot(), string.Empty);
        IReadOnlyList<NativeBranchPopupRow> remoteActions = NativeBranchPopup.BuildActionsForTest(
            rows.Single(row => row.Label == "origin/main"));
        IReadOnlyList<NativeBranchPopupRow> tagActions = NativeBranchPopup.BuildActionsForTest(
            rows.Single(row => row.Label == "v1.0"));

        Assert.IsTrue(remoteActions.Any(
            row => row.Request?.Command == NativeBranchPopupCommand.CreateTrackingBranch));
        Assert.IsFalse(remoteActions.Any(
            row => row.Request?.Command == NativeBranchPopupCommand.RenameReference));
        Assert.IsTrue(tagActions.Any(
            row => row.Request?.Command == NativeBranchPopupCommand.CheckoutRevision));
        Assert.IsTrue(tagActions.Any(
            row => row.Request?.Command == NativeBranchPopupCommand.DeleteReference));
    }

    [TestMethod]
    public void 弹层尺寸按视觉稿密度限制并随内容收缩()
    {
        Assert.AreEqual(NativeTheme.Scale(338), NativeBranchPopup.MainPopupWidthForTest);
        Assert.AreEqual(
            NativeTheme.Scale(176),
            NativeBranchPopup.CalculateMainPopupHeightForTest(0));
        Assert.AreEqual(
            NativeTheme.Scale(264),
            NativeBranchPopup.CalculateMainPopupHeightForTest(7));
        Assert.AreEqual(
            NativeTheme.Scale(306),
            NativeBranchPopup.CalculateMainPopupHeightForTest(20));
        Assert.AreEqual(
            NativeTheme.Scale(166),
            NativeBranchPopup.CalculateActionPopupHeightForTest(5));
    }

    private static GitReferenceSnapshot CreateSnapshot()
    {
        return new(
            [
                new("feature", "refs/heads/feature", false, false, null, "b", "feature"),
                new("main", "refs/heads/main", false, true, "origin/main", "a", "main"),
                new("origin/main", "refs/remotes/origin/main", true, false, null, "a", "main"),
            ],
            [new("v1.0", "a", false, null)]);
    }
}
