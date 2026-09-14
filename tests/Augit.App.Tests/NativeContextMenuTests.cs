using Augit.App;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeContextMenuTests
{
    [TestMethod]
    public void 菜单高度按视觉稿的行高和分隔间距计算()
    {
        int expected = NativeTheme.Scale(8) * 2 + 7 * NativeTheme.Scale(30) + 2 * NativeTheme.Scale(13);

        Assert.AreEqual(expected, NativeContextMenu.CalculateHeightForTest(7, 2));
        Assert.AreEqual(NativeTheme.Scale(200), NativeContextMenu.MinimumPopupWidthForTest);
        Assert.AreEqual(NativeTheme.Scale(30), NativeContextMenu.ItemHeightForTest);
        Assert.AreEqual(NativeTheme.Scale(13), NativeContextMenu.SeparatorHeightForTest);
    }

    [TestMethod]
    public void 菜单宽度按最长文字计算并受范围约束()
    {
        nint deviceContext = NativeMethods.GetDeviceContext(0);
        try
        {
            NativeContextMenuItem[] shortItems =
            [new("刷新", NativeContextMenuIcon.Refresh, static () => { })];
            NativeContextMenuItem[] longItems =
            [new("在资源管理器中定位当前文件", NativeContextMenuIcon.Open, static () => { })];
            int shortWidth = NativeContextMenu.MeasurePopupWidth(deviceContext, shortItems);
            int longWidth = NativeContextMenu.MeasurePopupWidth(deviceContext, longItems);
            Assert.AreEqual(NativeTheme.Scale(200), shortWidth);
            Assert.IsGreaterThan(shortWidth, longWidth);
            Assert.IsLessThanOrEqualTo(NativeTheme.Scale(420), longWidth);
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(0, deviceContext);
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 点击分隔线和外侧留白不执行上次选中命令且底部留白不被缩放误差侵占(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        nint owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "菜单测试宿主",
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
            20, 20, NativeTheme.Scale(800), NativeTheme.Scale(600), 0, 0, NativeMethods.GetModuleHandle(null), 0);
        Assert.AreNotEqual(0, owner);
        try
        {
            int invoked = 0;
            using NativeContextMenu menu = NativeContextMenu.Show(owner, 50, 50,
                [new("复制路径", NativeContextMenuIcon.Copy, () => invoked++), null,
                 new("刷新", NativeContextMenuIcon.Refresh, () => invoked++, Enabled: false)], dark: false);
            nint menuHandle = menu.Handle;
            Assert.AreEqual(menuHandle, NativeMethods.GetFocus(), "打开菜单后键盘焦点必须进入菜单窗口。");
            Assert.IsTrue(NativeMethods.GetClientRectangle(menuHandle, out NativeMethods.Rectangle client));
            int rowEnd = NativeTheme.Scale(8) + NativeTheme.Scale(30) * 2 + NativeTheme.Scale(13);
            Assert.AreEqual(NativeTheme.Scale(8), client.Bottom - rowEnd);
            _ = NativeMethods.SendMessage(menuHandle, NativeMethods.WindowMessageKeyDown, (nuint)NativeMethods.VirtualKeyDown, 0);
            foreach (int x in new[] { 1, client.Right - 1 })
            {
                _ = NativeMethods.SendMessage(menuHandle, NativeMethods.WindowMessageLeftButtonUp, 0,
                    unchecked((nint)((NativeTheme.Scale(20) << 16) | x)));
                Assert.AreEqual(0, invoked, "菜单左右外侧留白不能触发动作。");
            }
            foreach (int y in new[] { NativeTheme.Scale(3), NativeTheme.Scale(8) + NativeTheme.Scale(30) + NativeTheme.Scale(6),
                NativeTheme.Scale(8) + NativeTheme.Scale(30) + NativeTheme.Scale(13) + NativeTheme.Scale(15), client.Bottom - 1 })
            {
                _ = NativeMethods.SendMessage(menuHandle, NativeMethods.WindowMessageLeftButtonUp, 0,
                    unchecked((nint)((y << 16) | NativeTheme.Scale(20))));
                Assert.AreEqual(0, invoked, "分隔线、禁用行和边缘不能执行当前键盘选中项。");
                Assert.IsTrue(NativeMethods.IsWindow(menuHandle));
            }
            _ = NativeMethods.SendMessage(menuHandle, NativeMethods.WindowMessageKeyDown, (nuint)NativeMethods.VirtualKeyEnter, 0);
            Assert.AreEqual(1, invoked);
            Assert.IsFalse(NativeMethods.IsWindow(menuHandle));
        }
        finally
        {
            _ = NativeMethods.DestroyWindow(owner);
        }
    }

    [TestMethod]
    public void 键盘移动跳过分隔项和禁用项并循环()
    {
        bool[] enabled = [true, false, true, true, false];
        bool[] separators = [false, true, false, false, true];

        Assert.AreEqual(
            2,
            NativeContextMenu.FindNextSelectableIndexForTest(enabled, separators, 0, forwards: true));
        Assert.AreEqual(
            3,
            NativeContextMenu.FindNextSelectableIndexForTest(enabled, separators, 2, forwards: true));
        Assert.AreEqual(
            0,
            NativeContextMenu.FindNextSelectableIndexForTest(enabled, separators, 2, forwards: false));
    }

    [TestMethod]
    public void 子窗口所有者的菜单位置按顶层工作区约束而不是子窗口边界()
    {
        Assert.AreEqual(
            600,
            NativeContextMenu.CalculatePopupTopForTest(0, 1000, 900, 300));
        Assert.AreEqual(
            50,
            NativeContextMenu.CalculatePopupTopForTest(0, 1000, 50, 300));
        Assert.AreEqual(
            750,
            NativeContextMenu.CalculatePopupTopForTest(100, 1000, 950, 200));
    }
}
