namespace Augit.App.Tests;

[TestClass]
public sealed class NativePushDialogTests
{
    [TestMethod]
    public void Push使用视觉稿规定的宽双栏结构()
    {
        (int width, int height, int header, int footer) = NativePushDialog.LogicalLayoutForTest;

        Assert.AreEqual((930, 494, 45, 53), (width, height, header, footer));
        Assert.AreEqual((17, 15, 260, 365), NativePushDialog.ContentLayoutForTest);
        CollectionAssert.AreEqual(
            new[] { UiText.PushTags, UiText.AllTags },
            NativePushDialog.DisabledTagOptionsForTest.ToArray());
    }

    [TestMethod]
    public void 无远端时按视觉稿增加推送标签行高度()
    {
        Assert.AreEqual(494, NativePushDialog.DialogHeightForStateForTest(false));
        Assert.AreEqual(524, NativePushDialog.DialogHeightForStateForTest(true));
    }

    [TestMethod]
    public void 无远端引用行将定义远端紧跟分支摘要()
    {
        Assert.AreEqual((16, 70, 72), NativePushDialog.NoRemoteInlineActionLayoutForTest);
    }

    [TestMethod]
    public void 大字号时标题按钮和底部操作区按实际字高扩展()
    {
        Assert.AreEqual((45, 53, 27, 28, 494, 524), NativePushDialog.CalculateAdaptiveMetricsForTest(13));

        (int header, int footer, int row, int button, int dialog, int noRemote) =
            NativePushDialog.CalculateAdaptiveMetricsForTest(40);

        Assert.IsGreaterThan(45, header);
        Assert.IsGreaterThan(53, footer);
        Assert.IsGreaterThan(27, row);
        Assert.IsGreaterThan(28, button);
        Assert.IsGreaterThan(494, dialog);
        Assert.IsGreaterThan(dialog, noRemote);
    }
}
