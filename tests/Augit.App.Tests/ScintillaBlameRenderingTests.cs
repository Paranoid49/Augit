using System.Globalization;
using System.Runtime.InteropServices;
using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ScintillaBlameRenderingTests
{
    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public void 归属三列实际绘制随字号滚动且释放缓存(int dpi, bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        nint owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStylePopup, 0, 0, NativeTheme.Scale(740), NativeTheme.Scale(420),
            0, 0, NativeMethods.GetModuleHandle(null), 0);
        try
        {
            using ScintillaControl editor = new(owner, 1);
            _ = NativeMethods.SetWindowPosition(editor.Handle, 0, 0, 0,
                NativeTheme.Scale(720), NativeTheme.Scale(400), NativeMethods.SetWindowPositionNoActivate);
            editor.SetTextContent(string.Join('\n', Enumerable.Range(1, 100).Select(i => $"只读正文 {i}")));
            GitBlameLine[] lines = Enumerable.Range(1, 100).Select(i => new GitBlameLine(i,
                i.ToString("x40", CultureInfo.InvariantCulture), "I49长作者", "test@example.invalid",
                new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero), "说明", "a.txt", "正文")).ToArray();
            foreach (int size in new[] { 13, 40, 13 })
            {
                editor.ApplyAppearance("Consolas", size, dark);
                editor.SetBlame(lines);
                Assert.AreEqual(("2026/8/28", "I49长作者", "1"), editor.BlameRowForTest(0));
                Assert.IsTrue(editor.IsReadOnly);
                _ = NativeMethods.SendMessage(editor.Handle, 2613, 0, 0);
                int rowHeight = (int)NativeMethods.SendMessage(editor.Handle, 2279, 0, 0);
                using Capture before = new(editor.Handle);
                NativeThemePalette palette = NativeTheme.Palette(dark);
                int padding = NativeTheme.Scale(6), gap = NativeTheme.Scale(5);
                int dateEnd = padding + editor.BlameDateWidthForTest;
                int numberStart = editor.BlameWidthForTest - padding - editor.BlameNumberWidthForTest;
                Assert.AreEqual(editor.BlameAuthorWidthForTest, numberStart - gap - (dateEnd + gap),
                    "分列取整不能挤占已度量的作者宽度。");
                Assert.IsTrue(before.HasInk(padding, dateEnd, rowHeight, palette.Panel), "日期列必须实际绘制。");
                Assert.IsTrue(before.HasInk(dateEnd + gap, numberStart - gap, rowHeight, palette.Panel), "长作者须在作者列省略，不能覆盖日期或行号。");
                Assert.IsTrue(before.HasInk(numberStart, editor.BlameWidthForTest - padding, rowHeight, palette.Panel), "归属行号列必须实际绘制。");
                int allocations = editor.BlameBufferAllocationsForTest;
                byte[] firstNumber = before.Region(numberStart, editor.BlameWidthForTest - padding, rowHeight);
                for (int top = 12; top <= 60; top += 12)
                {
                    _ = NativeMethods.SendMessage(editor.Handle, 2613, (nuint)top, 0);
                    using Capture after = new(editor.Handle);
                    Assert.IsFalse(firstNumber.SequenceEqual(after.Region(numberStart, editor.BlameWidthForTest - padding, rowHeight)),
                        "纵向滚动后必须绘制当前文档行的归属，不能停留在首行。");
                    Assert.AreEqual(allocations, editor.BlameBufferAllocationsForTest, "滚动不得重新分配边栏位图。");
                }
                Assert.IsTrue(editor.BlameDrawingAllocatedForTest);
                editor.ClearBlame();
                Assert.IsFalse(editor.BlameDrawingAllocatedForTest);
                Assert.AreEqual(1, editor.MarginCount);
                Assert.IsTrue(editor.IsReadOnly);
            }
            editor.SetBlame(lines);
            _ = NativeMethods.DestroyWindow(owner);
            owner = 0;
            Assert.IsFalse(editor.BlameDrawingAllocatedForTest, "宿主提前销毁也必须释放归属字体和绘制缓存。");
        }
        finally { if (owner != 0) _ = NativeMethods.DestroyWindow(owner); }
    }

    private sealed class Capture : IDisposable
    {
        private readonly nint _dc, _bitmap, _previous;
        private readonly byte[] _pixels;
        private readonly int _width;
        internal Capture(nint window)
        {
            Assert.IsTrue(NativeMethods.GetClientRectangle(window, out var client));
            _width = client.Right;
            _dc = NativeMethods.CreateCompatibleDeviceContext(0);
            NativeMethods.BitmapInfo info = new()
            {
                Header = new()
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                    Width = _width,
                    Height = -client.Bottom,
                    Planes = 1,
                    BitCount = 32
                },
            };
            _bitmap = NativeMethods.CreateDeviceIndependentBitmap(_dc, ref info, 0, out nint bits, 0, 0);
            _previous = NativeMethods.SelectObject(_dc, _bitmap);
            _ = NativeMethods.SendMessage(window, 0x0318, unchecked((nuint)_dc), 0x000C);
            _ = Flush();
            _pixels = new byte[_width * client.Bottom * 4];
            Marshal.Copy(bits, _pixels, 0, _pixels.Length);
        }
        internal bool HasInk(int left, int right, int height, uint background)
        {
            for (int y = 0; y < height; y++) for (int x = left; x < right; x++)
            {
                int index = (y * _width + x) * 4;
                uint color = (uint)(_pixels[index + 2] | _pixels[index + 1] << 8 | _pixels[index] << 16);
                if (color != background) return true;
            }
            return false;
        }
        internal byte[] Region(int left, int right, int height) => Enumerable.Range(0, height)
            .SelectMany(y => _pixels.AsSpan((y * _width + left) * 4, (right - left) * 4).ToArray()).ToArray();
        public void Dispose()
        {
            _ = NativeMethods.SelectObject(_dc, _previous);
            _ = NativeMethods.DeleteObject(_bitmap);
            _ = NativeMethods.DeleteDeviceContext(_dc);
        }
        [DllImport("gdi32.dll", EntryPoint = "GdiFlush")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Flush();
    }
}
