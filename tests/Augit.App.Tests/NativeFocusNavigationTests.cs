namespace Augit.App.Tests;

[TestClass]
public sealed class NativeFocusNavigationTests
{
    [TestMethod]
    public void 正向焦点循环在区域末尾回到开头()
    {
        Assert.AreEqual(0, NativeFocusNavigation.ResolveNextIndexForTest(4, 3, backwards: false));
    }

    [TestMethod]
    public void 反向焦点循环在区域开头回到末尾()
    {
        Assert.AreEqual(3, NativeFocusNavigation.ResolveNextIndexForTest(4, 0, backwards: true));
    }

    [TestMethod]
    public void 当前焦点不在区域时按方向进入对应边界()
    {
        Assert.AreEqual(0, NativeFocusNavigation.ResolveNextIndexForTest(4, -1, backwards: false));
        Assert.AreEqual(3, NativeFocusNavigation.ResolveNextIndexForTest(4, -1, backwards: true));
    }
}
