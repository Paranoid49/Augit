namespace Augit.App.Tests;

[TestClass]
public sealed class NativeCloneDialogTests
{
    [TestMethod]
    public void Clone使用无平台侧栏的宽版Git表单()
    {
        (int width, int height, int header, int footer) = NativeCloneDialog.LogicalLayoutForTest;

        Assert.AreEqual(930, width);
        Assert.AreEqual(289, height);
        Assert.AreEqual(45, header);
        Assert.AreEqual(53, footer);
        CollectionAssert.AreEqual(
            new[] { UiText.VersionControl, UiText.CloneSource, UiText.CloneDestination, UiText.ShallowClone },
            NativeCloneDialog.FieldLabelsForTest.ToArray());
    }

    [TestMethod]
    public void 未启用浅克隆时忽略深度输入()
    {
        bool valid = NativeCloneDialog.TryParseDepthForTest(false, string.Empty, out int? depth);

        Assert.IsTrue(valid);
        Assert.IsNull(depth);
    }

    [TestMethod]
    public void 版本控制下拉框使用统一深色自绘样式()
    {
        Assert.AreEqual(NativeComboBoxTheme.ControlStyle, NativeCloneDialog.VersionControlStyleForTest);
    }

    [TestMethod]
    public void Clone输入框不依赖系统粗边框而使用统一输入框外壳()
    {
        Assert.AreEqual(
            NativeMethods.EditAutoHorizontalScroll,
            NativeCloneDialog.InputControlStyleForTest);
        Assert.AreEqual(
            0u,
            NativeCloneDialog.InputControlStyleForTest & NativeMethods.WindowStyleBorder);
    }

    [TestMethod]
    public void 启用浅克隆时只接受正整数深度()
    {
        Assert.IsTrue(NativeCloneDialog.TryParseDepthForTest(true, " 12 ", out int? depth));
        Assert.AreEqual(12, depth);
        Assert.IsFalse(NativeCloneDialog.TryParseDepthForTest(true, "0", out _));
        Assert.IsFalse(NativeCloneDialog.TryParseDepthForTest(true, "-1", out _));
        Assert.IsFalse(NativeCloneDialog.TryParseDepthForTest(true, "1.5", out _));
        Assert.IsFalse(NativeCloneDialog.TryParseDepthForTest(true, string.Empty, out _));
    }
}
