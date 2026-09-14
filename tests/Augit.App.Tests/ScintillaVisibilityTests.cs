using System.Runtime.InteropServices;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ScintillaVisibilityTests
{
    [TestMethod]
    public void 滚动条空角绘制主题颜色并在窗口销毁时释放登记()
    {
        nint owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStylePopup, 0, 0, 400, 300, 0, 0, NativeMethods.GetModuleHandle(null), 0);
        nint editor = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleHorizontalScroll | NativeMethods.WindowStyleVerticalScroll,
            0, 0, 300, 180, owner, 1, NativeMethods.GetModuleHandle(null), 0);
        Assert.AreNotEqual((nint)0, owner);
        Assert.AreNotEqual((nint)0, editor);
        try
        {
            Assert.IsTrue(NativeMethods.GetWindowRectangle(editor, out NativeMethods.Rectangle bounds));
            Assert.IsTrue(NativeMethods.GetClientRectangle(editor, out NativeMethods.Rectangle client));
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            Assert.IsGreaterThan(client.Right, width);
            Assert.IsGreaterThan(client.Bottom, height);
            nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
            NativeMethods.BitmapInfo info = new()
            {
                Header = new()
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                },
            };
            nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
            nint previous = NativeMethods.SelectObject(dc, bitmap);
            try
            {
                bool[] themes = [true, false, true];
                foreach (bool dark in themes)
                {
                    uint expected = NativeTheme.Palette(dark).Panel;
                    NativeScrollBarCornerTheme.Apply(editor, expected);
                    Assert.IsTrue(NativeScrollBarCornerTheme.IsRegisteredForTest(editor));
                    _ = NativeMethods.SendMessage(editor, 0x0317, unchecked((nuint)dc), 0x000E);
                    Assert.AreEqual(expected, NativeMethods.GetPixel(dc,
                        (client.Right + width) / 2, (client.Bottom + height) / 2));
                }
            }
            finally
            {
                _ = NativeMethods.SelectObject(dc, previous);
                _ = NativeMethods.DeleteObject(bitmap);
                _ = NativeMethods.DeleteDeviceContext(dc);
            }
        }
        finally
        {
            _ = NativeMethods.DestroyWindow(owner);
        }
        Assert.IsFalse(NativeScrollBarCornerTheme.IsRegisteredForTest(editor));
    }

    [TestMethod]
    public void 主题切换后重复显隐正文保留文本只读和阅读位置()
    {
        nint owner = NativeMethods.CreateWindow(
            0,
            NativeMethods.StaticClass,
            string.Empty,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
            0,
            0,
            420,
            310,
            0,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        Assert.AreNotEqual((nint)0, owner);
        try
        {
            using ScintillaControl editor = new(owner, 1);
            editor.SetBounds(0, 0, 400, 280);
            string text = string.Join('\n', Enumerable.Repeat(new string('x', 200), 100));
            bool[] themes = [true, false, true];
            foreach (bool dark in themes)
            {
                editor.SetVisible(false);
                editor.ApplyAppearance("Cascadia Mono", 13, dark);
                editor.SetTextContent(text);
                editor.GoToLine(40);
                int firstLine = editor.FirstVisibleLine;
                editor.SetVisible(true);
                Assert.IsTrue(NativeMethods.IsWindowVisible(editor.Handle));
                editor.SetVisible(true);
                Assert.IsTrue(editor.IsReadOnly);
                Assert.AreEqual(text, editor.GetTextContent());
                Assert.AreEqual(firstLine, editor.FirstVisibleLine);
            }
        }
        finally
        {
            _ = NativeMethods.DestroyWindow(owner);
        }
    }
}
