using System.Runtime.InteropServices;

namespace Augit.App.Tests;

public sealed partial class NativeConflictResolverInteractionTests
{
    private static readonly int[] FrameChildIds = [30, 31, 32, 33, 34];
    private static readonly int[] FrameDividerIds = [30, 31];

    [TestMethod]
    [DataRow("Light", 96, 13)]
    [DataRow("Dark", 96, 13)]
    [DataRow("Light", 120, 13)]
    [DataRow("Dark", 120, 13)]
    [DataRow("Light", 144, 13)]
    [DataRow("Dark", 144, 13)]
    [DataRow("Light", 96, 40)]
    [DataRow("Dark", 96, 40)]
    [DataRow("Light", 120, 40)]
    [DataRow("Dark", 120, 40)]
    [DataRow("Light", 144, 40)]
    [DataRow("Dark", 144, 40)]
    public Task 冲突边框分隔线连续且子控件重绘不覆盖边界(string theme, int dpi, int size) =>
        RunAsync(async (dialog, service) =>
        {
            await Task.Delay(100);
            var window = Bounds(dialog.HandleForTest);
            var firstLabel = Bounds(Item(dialog, 30));
            var body = Bounds(dialog.ViewportsForTest[0].Handle);
            var notice = Bounds(Item(dialog, 33));
            int border = NativeTheme.Scale(1), inset = NativeTheme.Scale(17);
            int header = Math.Max(NativeTheme.Scale(45), NativeTheme.UiLineHeight + NativeTheme.Scale(18));
            int[] innerRows = [firstLabel.Top - window.Top - border, body.Top - window.Top - border, body.Bottom - window.Top];
            int footer = notice.Top - window.Top - border;
            var palette = NativeTheme.Palette(theme == "Dark");
            int parses = dialog.ResultParseCountForTest;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    foreach (int id in FrameChildIds)
                    {
                        _ = NativeMethods.InvalidateRectangle(Item(dialog, id), 0, false);
                        _ = NativeMethods.UpdateWindow(Item(dialog, id));
                    }
                }
                FramePixels pixels = FramePixels.Capture(dialog.HandleForTest);
                if (pass == 0 && Environment.GetEnvironmentVariable("AUGIT_VISUAL_EVIDENCE") is { Length: > 0 } directory)
                    pixels.Save(Path.Combine(directory, $"conflict-frame-{theme.ToLowerInvariant()}-{dpi}-{size}.bmp"));
                pixels.AssertHorizontal(header, border, pixels.Width - border, border, palette.Border);
                pixels.AssertHorizontal(footer, border, pixels.Width - border, border, palette.Border);
                foreach (int y in innerRows)
                {
                    pixels.AssertHorizontal(y, inset, pixels.Width - inset, border, palette.Border);
                    Assert.AreEqual(palette.Panel, pixels.At(NativeTheme.Scale(5), y), "正文分隔线不能伸进外侧留白。");
                    Assert.AreEqual(palette.Panel, pixels.At(pixels.Width - NativeTheme.Scale(5), y));
                }
                foreach (int id in FrameDividerIds)
                {
                    int x = Bounds(Item(dialog, id)).Right - window.Left;
                    for (int y = firstLabel.Top - window.Top; y < body.Bottom - window.Top; y++)
                        Assert.AreEqual(palette.Border, pixels.At(x, y), $"第 {id - 29} 条竖向分隔线在 {y} 断开。");
                }
                Assert.AreEqual(palette.BorderStrong, pixels.At(0, pixels.Height / 2));
                Assert.AreEqual(palette.BorderStrong, pixels.At(pixels.Width - 1, pixels.Height / 2));
                Assert.AreEqual(palette.BorderStrong, pixels.At(pixels.Width / 2, 0));
                Assert.AreEqual(palette.BorderStrong, pixels.At(pixels.Width / 2, pixels.Height - 1));
            }
            Assert.AreEqual(parses, dialog.ResultParseCountForTest);
            Assert.IsFalse(dialog.ResultIsDirtyForTest);
            Assert.IsEmpty(service.Saves);
        }, theme: theme, dpi: dpi, size: size, codeSize: 13, unresolved: true,
            relativePath: "NativeGitPanel.cs", width: 1440, height: 900, physicalPixels: true);

    private sealed record FramePixels(int Width, int Height, byte[] Data)
    {
        internal uint At(int x, int y)
        {
            Assert.IsTrue(x >= 0 && x < Width && y >= 0 && y < Height);
            int index = (y * Width + x) * 4;
            return (uint)(Data[index + 2] | Data[index + 1] << 8 | Data[index] << 16);
        }

        internal void AssertHorizontal(int top, int left, int right, int height, uint color)
        {
            for (int y = top; y < top + height; y++)
                for (int x = left; x < right; x++)
                    Assert.AreEqual(color, At(x, y), $"水平分隔线在 {x},{y} 缺失或被子控件覆盖。");
        }

        internal static FramePixels Capture(nint window)
        {
            _ = NativeMethods.RedrawWindow(window, 0, 0, NativeMethods.RedrawInvalidate | NativeMethods.RedrawUpdateNow | 0x80);
            _ = NativeMethods.GetClientRectangle(window, out var bounds);
            nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
            NativeMethods.BitmapInfo info = new()
            {
                Header = new()
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                    Width = bounds.Right,
                    Height = -bounds.Bottom,
                    Planes = 1,
                    BitCount = 32
                },
            };
            nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out nint bits, 0, 0);
            nint previous = NativeMethods.SelectObject(dc, bitmap);
            try
            {
                Assert.AreNotEqual((nint)0, bitmap);
                Assert.IsTrue(PrintFrame(window, dc, 2));
                Assert.IsTrue(FlushFrame());
                byte[] pixels = new byte[checked(bounds.Right * bounds.Bottom * 4)];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                return new(bounds.Right, bounds.Bottom, pixels);
            }
            finally
            {
                _ = NativeMethods.SelectObject(dc, previous);
                _ = NativeMethods.DeleteObject(bitmap);
                _ = NativeMethods.DeleteDeviceContext(dc);
            }
        }

        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using BinaryWriter writer = new(File.Create(path));
            writer.Write((ushort)0x4D42);
            writer.Write(54 + Data.Length);
            writer.Write(0); writer.Write(54); writer.Write(40);
            writer.Write(Width); writer.Write(-Height);
            writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(0); writer.Write(Data.Length);
            for (int index = 0; index < 4; index++) writer.Write(0);
            writer.Write(Data);
        }

        [DllImport("user32.dll", EntryPoint = "PrintWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintFrame(nint window, nint dc, uint flags);
        [DllImport("gdi32.dll", EntryPoint = "GdiFlush")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFrame();
    }
}
