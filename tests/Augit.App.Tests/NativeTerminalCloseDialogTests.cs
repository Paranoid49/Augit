namespace Augit.App.Tests;

[TestClass]
public sealed class NativeTerminalCloseDialogTests
{
    [TestMethod]
    public void 关闭终端确认窗口使用统一无标题栏模态尺寸()
    {
        (int width, int height, int headerHeight, int footerHeight) =
            NativeTerminalCloseDialog.LogicalLayoutForTest;

        Assert.AreEqual(620, width);
        Assert.AreEqual(300, height);
        Assert.AreEqual(45, headerHeight);
        Assert.AreEqual(53, footerHeight);
        Assert.IsFalse(NativeTerminalCloseDialog.UsesSystemCaptionForTest);
        Assert.AreEqual("Augit.TerminalCloseDialog.Native", NativeTerminalCloseDialog.WindowClassNameForTest);
    }

    [TestMethod]
    public void 关闭按钮宽度足以显示完整危险动作并保持右对齐()
    {
        Assert.AreEqual((92, 122, 12, 8), NativeTerminalCloseDialog.FooterButtonLayoutForTest);
    }
}
