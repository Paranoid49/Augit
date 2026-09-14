using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeWorkspaceOpenDialogTests
{
    private static readonly string[] ExpectedRecentDirectories =
    [
        "C:\\Work\\Augit",
        "C:\\One",
        "C:\\Two",
        "C:\\Three",
        "C:\\Four",
        "C:\\Five",
        "C:\\Six",
        "C:\\Seven",
        "C:\\Eight",
        "C:\\Nine",
    ];

    private static readonly int[] ExpectedTabOrder = [10, 11, 13, 12, 14];

    [TestMethod]
    public void 打开工作区窗口尺寸和固定区域符合视觉规格()
    {
        (int width, int height, int headerHeight, int footerHeight) =
            NativeWorkspaceOpenDialog.LogicalLayoutForTest;

        Assert.AreEqual(930, width);
        Assert.AreEqual(480, height);
        Assert.AreEqual(45, headerHeight);
        Assert.AreEqual(53, footerHeight);
        Assert.AreEqual(
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            NativeWorkspaceOpenDialog.WindowStyleForTest);
        Assert.AreEqual(
            0u,
            NativeWorkspaceOpenDialog.WindowStyleForTest
                & (NativeMethods.WindowStyleCaption | NativeMethods.WindowStyleSystemMenu),
            "打开工作区窗口不得同时显示系统标题栏和自绘标题栏。");
        Assert.AreEqual("打开工作区", NativeWorkspaceOpenDialog.TitleForTest);
    }

    [TestMethod]
    public void 非最近目录打开方式隐藏最近目录列表()
    {
        Assert.IsTrue(NativeWorkspaceOpenDialog.CategoryShowsRecentListForTest(0));
        Assert.IsFalse(NativeWorkspaceOpenDialog.CategoryShowsRecentListForTest(1));
        Assert.IsFalse(NativeWorkspaceOpenDialog.CategoryShowsRecentListForTest(2));
    }

    [TestMethod]
    public void 打开方式分类顺序与视觉稿一致()
    {
        CollectionAssert.AreEqual(
            new[] { UiText.RecentWorkspaces, "选择目录…", "克隆仓库…" },
            NativeWorkspaceOpenDialog.CategoryLabelsForTest.ToArray());
    }

    [TestMethod]
    public void 键盘顺序只包含交互控件并符合视觉顺序()
    {
        CollectionAssert.AreEqual(
            ExpectedTabOrder,
            NativeWorkspaceOpenDialog.TabOrderForTest.ToArray());
    }

    [TestMethod]
    public void 最近目录会忽略空值并按大小写不敏感去重最多保留十项()
    {
        string[] paths =
        [
            "C:\\Work\\Augit",
            "",
            "c:\\work\\augit",
            "C:\\One",
            "C:\\Two",
            "C:\\Three",
            "C:\\Four",
            "C:\\Five",
            "C:\\Six",
            "C:\\Seven",
            "C:\\Eight",
            "C:\\Nine",
            "C:\\Ten",
            "C:\\Eleven",
        ];
        ApplicationSettings settings = new() { RecentWorkspaces = paths };

        CollectionAssert.AreEqual(
            ExpectedRecentDirectories,
            NativeWorkspaceOpenDialog.RecentDirectoriesForTest(settings).ToArray());
    }

    [TestMethod]
    public void 无最近目录时仍返回稳定的空集合()
    {
        ApplicationSettings settings = new() { RecentWorkspaces = [" ", ""] };

        Assert.IsEmpty(NativeWorkspaceOpenDialog.RecentDirectoriesForTest(settings));
    }
}
