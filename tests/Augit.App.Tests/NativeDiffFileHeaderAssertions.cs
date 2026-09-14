namespace Augit.App.Tests;

internal static class NativeDiffFileHeaderAssertions
{
    internal static void AssertBodyBelowHeader(nint header, nint body, bool sideBySide, int topPadding = 0)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(header, out var heading));
        Assert.IsTrue(NativeMethods.GetWindowRectangle(body, out var content));
        int row = Math.Max(NativeTheme.Scale(24), NativeTheme.UiLineHeight + NativeTheme.Scale(4));
        int expected = sideBySide ? Math.Max(NativeTheme.Scale(31), NativeTheme.UiLineHeight + NativeTheme.Scale(12))
            : Math.Max(NativeTheme.Scale(55), row * 2 + NativeTheme.Scale(7));
        Assert.AreEqual(expected, heading.Bottom - heading.Top);
        Assert.AreEqual(heading.Bottom + NativeTheme.Scale(topPadding), content.Top, "文件信息与正文间距必须符合当前比较布局。");
        Assert.AreEqual(0L, NativeMethods.GetWindowLongPointer(header, NativeMethods.WindowLongStyle).ToInt64() & NativeMethods.WindowStyleTabStop);
    }
}
