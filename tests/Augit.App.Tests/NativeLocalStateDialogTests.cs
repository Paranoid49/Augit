using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeLocalStateDialogTests
{
    [TestMethod]
    public void 创建Stash使用紧凑表单并包含规格字段()
    {
        (int width, int height, int header, int footer) = NativeStashDialog.LogicalLayoutForTest;

        Assert.AreEqual(620, width);
        Assert.AreEqual(323, height);
        Assert.AreEqual(45, header);
        Assert.AreEqual(53, footer);
        string[] expectedLabels = ["Git 根目录", "当前分支", "消息", "保留索引状态"];
        int[] expectedTabOrder = [19, 20, 21, 11, 10, 12];
        CollectionAssert.AreEqual(expectedLabels, NativeStashDialog.FieldLabelsForTest.ToArray());
        CollectionAssert.AreEqual(expectedTabOrder, NativeStashDialog.TabOrderForTest.ToArray());
        Assert.AreEqual(0x0213u, NativeStashDialog.RootControlStyleForTest);
        Assert.AreEqual(
            NativeMethods.EditMultiline | NativeMethods.EditWantReturn,
            NativeStashDialog.MessageControlStyleForTest);
        Assert.AreEqual(
            0u,
            NativeStashDialog.MessageControlStyleForTest & NativeMethods.WindowStyleBorder);
    }

    [TestMethod]
    public void Stash管理使用宽双栏布局()
    {
        (int width, int height, int header, int footer, int sidebar) =
            NativeStashManagerDialog.LogicalLayoutForTest;

        Assert.AreEqual(930, width);
        Assert.AreEqual(411, height);
        Assert.AreEqual(45, header);
        Assert.AreEqual(53, footer);
        Assert.AreEqual(260, sidebar);

        int[] expectedTabOrder = [1, 2, 13, 14, 15, 16, 18, 19, 10, 11, 12];
        string[] expectedActions = ["应用", "弹出", "查看内容", "删除"];
        CollectionAssert.AreEqual(expectedTabOrder, NativeStashManagerDialog.TabOrderForTest.ToArray());
        CollectionAssert.AreEqual(expectedActions, NativeStashManagerDialog.ActionLabelsForTest.ToArray());

        GitStashInfo stash = new(
            "stash@{0}",
            "abcdef1234567890",
            "1234567890abcdef",
            new DateTimeOffset(2026, 8, 31, 10, 30, 0, TimeSpan.FromHours(8)),
            "On main: 工作区切换前",
            "main",
            "工作区切换前");
        Assert.AreEqual("stash@{0} 工作区切换前", NativeStashManagerDialog.FormatListEntryForTest(stash));
        Assert.AreEqual("stash@{0} · 工作区切换前", NativeStashManagerDialog.FormatDetailTitleForTest(stash));
        Assert.StartsWith("main · abcdef1 · ", NativeStashManagerDialog.FormatDetailMetadataForTest(stash, null));
        Assert.AreEqual("包含 3 个文件", UiText.StashChangedFileCount(3));
    }

    [TestMethod]
    public void Reset紧凑窗口解释三种模式()
    {
        (int width, int height, int header, int footer) = NativeResetDialog.LogicalLayoutForTest;

        Assert.AreEqual(620, width);
        Assert.AreEqual(288, height);
        Assert.AreEqual(45, header);
        Assert.AreEqual(53, footer);
        string[] expectedLabels = ["目标提交", "模式"];
        int[] expectedTabOrder = [20, 21, 11, 10, 12];
        string[] expectedModeDescriptions =
        [
            "Soft · 仅移动 HEAD",
            "Mixed · 同时重置索引",
            "Hard · 重置索引和工作区",
        ];
        CollectionAssert.AreEqual(expectedLabels, NativeResetDialog.FieldLabelsForTest.ToArray());
        CollectionAssert.AreEqual(expectedTabOrder, NativeResetDialog.TabOrderForTest.ToArray());
        Assert.AreEqual(0x0213u, NativeResetDialog.ModeControlStyleForTest);
        CollectionAssert.AreEqual(
            expectedModeDescriptions,
            NativeResetDialog.ModeDescriptionsForTest.ToArray());

        Assert.AreEqual(
            (UiText.ResetSoftImpact, UiText.ResetSoftNote, UiText.RunReset, false),
            NativeResetDialog.ModePresentationForTest(GitResetMode.Soft));
        Assert.AreEqual(
            (UiText.ResetMixedImpact, UiText.ResetMixedNote, UiText.RunReset, false),
            NativeResetDialog.ModePresentationForTest(GitResetMode.Mixed));
        Assert.AreEqual(
            ("将丢失 35 个已跟踪文件的本地改动", UiText.ResetHardNote, UiText.ConfirmResetHardAction, true),
            NativeResetDialog.ModePresentationForTest(GitResetMode.Hard, 35));
    }

    [TestMethod]
    public void Rollback确认窗口使用宽布局并保留风险说明()
    {
        (int width, int height, int header, int footer) = NativeRollbackDialog.LogicalLayoutForTest;

        Assert.AreEqual(930, width);
        Assert.AreEqual(430, height);
        Assert.AreEqual(45, header);
        Assert.AreEqual(53, footer);
        CollectionAssert.AreEqual(
            new[]
            {
                UiText.RollbackWarningTitle,
                UiText.RollbackWarningDetail,
                UiText.RollbackRecycleNotice,
            },
            NativeRollbackDialog.WarningLabelsForTest.ToArray());
    }

    [TestMethod]
    public void 冲突操作窗口使用宽布局且底部动作按状态互斥()
    {
        (int width, int height, int header, int footer) =
            NativeGitOperationDialog.LogicalLayoutForTest;

        Assert.AreEqual((930, 640, 45, 53), (width, height, header, footer));
        Assert.AreEqual(300, NativeGitOperationDialog.ConflictDialogHeightForTest);
        Assert.AreEqual(
            (true, false, true, false, false, false),
            NativeGitOperationDialog.FooterVisibilityForTest(
                operationRunning: false,
                canContinue: false,
                canSkip: false,
                canAbort: false));
        Assert.AreEqual(
            (false, false, false, true, true, true),
            NativeGitOperationDialog.FooterVisibilityForTest(
                operationRunning: false,
                canContinue: false,
                canSkip: true,
                canAbort: true,
                keepBlockedContinueVisible: true));
        Assert.AreEqual(
            (false, true, false, false, false, false),
            NativeGitOperationDialog.FooterVisibilityForTest(
                operationRunning: true,
                canContinue: true,
                canSkip: true,
                canAbort: true));
    }
}
