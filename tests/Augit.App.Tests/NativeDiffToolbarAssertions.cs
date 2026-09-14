namespace Augit.App.Tests;

internal static class NativeDiffToolbarAssertions
{
    internal static void AssertGroup(nint parent, NativeMethods.Rectangle group, nint sideBySide, nint unified)
    {
        Assert.AreEqual(NativeTheme.Scale(81), group.Right - group.Left);
        Assert.AreEqual(NativeTheme.Scale(31), group.Bottom - group.Top);
        NativeMethods.Point origin = new();
        Assert.IsTrue(NativeMethods.ClientToScreen(parent, ref origin));
        foreach ((nint control, int left, int right) in new[] { (sideBySide, 2, 40), (unified, 41, 79) })
        {
            Assert.IsTrue(NativeMethods.GetWindowRectangle(control, out var rectangle));
            Assert.AreEqual(origin.X + group.Left + NativeTheme.Scale(left), rectangle.Left);
            Assert.AreEqual(origin.X + group.Left + NativeTheme.Scale(right), rectangle.Right);
            Assert.AreEqual(origin.Y + group.Top + NativeTheme.Scale(2), rectangle.Top);
            Assert.AreEqual(origin.Y + group.Top + NativeTheme.Scale(29), rectangle.Bottom);
            Assert.AreEqual(NativeMethods.ButtonOwnerDraw,
                unchecked((uint)NativeMethods.GetWindowLongPointer(control, NativeMethods.WindowLongStyle).ToInt64())
                    & NativeMethods.ButtonOwnerDraw);
        }
    }
}
