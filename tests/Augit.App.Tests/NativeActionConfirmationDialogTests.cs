namespace Augit.App.Tests;

[TestClass]
public sealed class NativeActionConfirmationDialogTests
{
    private static readonly string[] ExpectedLabels =
    [
        "初始化 Git 仓库",
        "当前目录不是 Git 仓库",
        "初始化会创建 .git 元数据，不会提交或修改现有文件。",
        "初始化仓库",
        UiText.Cancel,
    ];

    [TestMethod]
    public void 确认窗口尺寸和固定区域符合视觉规格()
    {
        (int width, int height, int headerHeight, int footerHeight) =
            NativeActionConfirmationDialog.LogicalLayoutForTest;

        Assert.AreEqual(620, width);
        Assert.AreEqual(300, height);
        Assert.AreEqual(45, headerHeight);
        Assert.AreEqual(53, footerHeight);
    }

    [TestMethod]
    public void 确认窗口保留标题说明和动作文本()
    {
        CollectionAssert.AreEqual(
            ExpectedLabels,
            NativeActionConfirmationDialog.LabelsForTest(
                ExpectedLabels[0],
                ExpectedLabels[1],
                ExpectedLabels[2],
                ExpectedLabels[3]).ToArray());
    }

    [TestMethod]
    public void 初始化仓库确认窗口显示继续仅浏览动作()
    {
        string[] expected =
        [
            ExpectedLabels[0],
            ExpectedLabels[1],
            ExpectedLabels[2],
            ExpectedLabels[3],
            UiText.ContinueBrowse,
        ];
        CollectionAssert.AreEqual(
            expected,
            NativeActionConfirmationDialog.LabelsForTest(
                ExpectedLabels[0],
                ExpectedLabels[1],
                ExpectedLabels[2],
                ExpectedLabels[3],
                UiText.ContinueBrowse).ToArray());
    }

    [TestMethod]
    public void 分支覆盖错误才进入SmartCheckout确认()
    {
        Assert.IsTrue(MainWindow.IsBranchOverwriteRiskForTest("error: Your local changes would be overwritten by checkout"));
        Assert.IsTrue(MainWindow.IsBranchOverwriteRiskForTest("本地改动会被覆盖"));
        Assert.IsFalse(MainWindow.IsBranchOverwriteRiskForTest("fatal: invalid reference"));
        Assert.IsFalse(MainWindow.IsBranchOverwriteRiskForTest(null));
    }

    [TestMethod]
    public void 高级操作SmartCheckout影响说明包含分支和文件数量()
    {
        string detail = NativeGitOperationDialog.BuildSmartCheckoutImpactForTest(
            "main",
            "feature/ux",
            3,
            2);

        StringAssert.Contains(detail, UiText.SmartCheckoutWarningDetail);
        StringAssert.Contains(detail, "当前分支：main");
        StringAssert.Contains(detail, "目标分支：feature/ux");
        StringAssert.Contains(detail, "将暂存：3 个已跟踪文件和 2 个未跟踪文件");
    }

    [TestMethod]
    public void SmartCheckout说明分离警告和影响字段()
    {
        string detail = NativeGitOperationDialog.BuildSmartCheckoutImpactForTest(
            "main",
            "feature/ux",
            3,
            2);

        (string warning, IReadOnlyList<(string Label, string Value)> fields) =
            NativeActionConfirmationDialog.WarningLayoutForTest(detail);

        Assert.AreEqual(UiText.SmartCheckoutWarningDetail, warning);
        CollectionAssert.AreEqual(
            new[]
            {
                ("当前分支", "main"),
                ("目标分支", "feature/ux"),
                ("将暂存", "3 个已跟踪文件和 2 个未跟踪文件"),
            },
            fields.ToArray());
    }

    [TestMethod]
    public void 删除和移除确认详情先显示警告再显示目标字段()
    {
        (string stashWarning, IReadOnlyList<(string Label, string Value)> stashFields) =
            NativeActionConfirmationDialog.WarningLayoutForTest(
                UiText.ConfirmDeleteStashDetails("stash@{0}"));
        Assert.AreEqual(UiText.ConfirmDeleteStash, stashWarning);
        CollectionAssert.AreEqual(
            new[] { ("Stash", "stash@{0}") },
            stashFields.ToArray());

        (string worktreeWarning, IReadOnlyList<(string Label, string Value)> worktreeFields) =
            NativeActionConfirmationDialog.WarningLayoutForTest(
                UiText.ConfirmRemoveWorktreeDetails("D:\\worktree", "feature/ux"));
        Assert.AreEqual(UiText.ConfirmRemoveWorktree, worktreeWarning);
        CollectionAssert.AreEqual(
            new[] { ("路径", "D:\\worktree"), ("分支", "feature/ux") },
            worktreeFields.ToArray());
    }
}
