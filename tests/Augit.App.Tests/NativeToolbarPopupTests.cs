using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeToolbarPopupTests
{
    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 溢出工具栏横向排列并保留禁用项名称和键盘操作(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        using TemporaryDirectory temporary = new();
        using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
        window.Show();
        Assert.IsTrue(NativeMethods.GetWindowRectangle(window.Handle, out NativeMethods.Rectangle owner));
        int invoked = -1;
        NativeToolbarPopupItem[] items = Enumerable.Range(0, 6).Select(index => new NativeToolbarPopupItem(
            $"动作{index}", () => invoked = index,
            (dc, rectangle, color) => NativeTheme.DrawNavigationIcon(dc, rectangle, NativeNavigationIcon.Search, color),
            index != 1)).ToArray();
        using NativeToolbarPopup popup = new(window.Handle, new() { Left = owner.Left + 40, Top = owner.Bottom - 8 }, items, dark: true);
        Assert.AreEqual(6, popup.ToolTipCountForTest);
        Assert.IsTrue(NativeMethods.GetWindowRectangle(popup.Handle, out NativeMethods.Rectangle bounds));
        Assert.IsLessThanOrEqualTo(owner.Bottom, bounds.Bottom);
        Assert.AreEqual(NativeTheme.Scale(36), bounds.Bottom - bounds.Top);
        nint[] buttons = popup.ButtonsForTest.ToArray();
        int previousLeft = 0;
        for (int index = 0; index < buttons.Length; index++)
        {
            Assert.IsTrue(NativeMethods.GetWindowRectangle(buttons[index], out NativeMethods.Rectangle rectangle));
            Assert.AreEqual(bounds.Top + NativeTheme.Scale(4), rectangle.Top);
            Assert.AreEqual(NativeTheme.Scale(28), rectangle.Right - rectangle.Left);
            if (index > 0) Assert.AreEqual(NativeTheme.Scale(32), rectangle.Left - previousLeft);
            previousLeft = rectangle.Left;
            Assert.AreEqual($"动作{index}", NativeAccessibility.GetNameForTest(buttons[index]));
        }
        Assert.IsFalse(NativeMethods.IsWindowEnabled(buttons[1]));
        Assert.AreEqual(buttons[0], NativeMethods.GetFocus());
        _ = NativeMethods.SendMessage(buttons[0], NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyRight, 0);
        Assert.AreEqual(buttons[2], NativeMethods.GetFocus());
        _ = NativeMethods.SendMessage(buttons[2], NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        Assert.AreEqual(2, invoked);
        Assert.AreEqual((nint)0, popup.Handle);
        Assert.IsFalse(NativeMethods.IsWindow(buttons[0]));

        using NativeToolbarPopup cancelled = new(window.Handle, owner, items, dark: false);
        nint cancelledHandle = cancelled.Handle;
        _ = NativeMethods.SendMessage(cancelled.ButtonsForTest[0], NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
        Assert.IsFalse(NativeMethods.IsWindow(cancelledHandle));
        Assert.AreEqual(2, invoked, "Esc 不得触发工具动作");
    }
}
