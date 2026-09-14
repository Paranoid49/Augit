using System.Runtime.InteropServices;

namespace Augit.App.Tests;

internal static class NativeDiffBackgroundAssertions
{
    internal static void AssertLine(nint editor, int line, int markerMask, uint expectedColor, TestContext? context = null)
    {
        Assert.AreEqual((nint)markerMask, NativeMethods.SendMessage(editor, 2046, (nuint)line, 0),
            $"第 {line + 1} 行的增删底色标记错误。");
        _ = NativeMethods.RedrawWindow(editor, 0, 0, NativeMethods.RedrawInvalidate | NativeMethods.RedrawUpdateNow);
        _ = NativeMethods.UpdateWindow(NativeMethods.GetParent(editor));
        _ = NativeMethods.UpdateWindow(editor);
        Assert.IsTrue(NativeMethods.GetClientRectangle(editor, out var client));
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = client.Right,
                Height = -client.Bottom,
                Planes = 1,
                BitCount = 32
            }
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out nint bits, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            Assert.AreNotEqual((nint)0, bitmap);
            Assert.IsTrue(PrintWindow(editor, dc, 3));
            Assert.IsTrue(FlushCapture());
            nint position = NativeMethods.SendMessage(editor, 2167, (nuint)line, 0);
            int top = (int)NativeMethods.SendMessage(editor, 2165, 0, position);
            int height = (int)NativeMethods.SendMessage(editor, 2279, (nuint)line, 0);
            // 文字右侧空白也必须有底色；字词高亮本身不能满足这个断言。
            int x = client.Right - NativeTheme.Scale(32), y = top + height / 2;
            Assert.IsTrue(x > 0 && y >= 0 && y < client.Bottom);
            uint actual = NativeMethods.GetPixel(dc, x, y);
            if (actual != expectedColor && context?.TestResultsDirectory is { } directory)
            {
                byte[] pixels = new byte[checked(client.Right * client.Bottom * 4)];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"diff-background-{Guid.NewGuid():N}.bmp");
                SaveCapture(path, client.Right, client.Bottom, pixels);
                context.AddResultFile(path);
            }
            Assert.AreEqual(expectedColor, actual,
                $"第 {line + 1} 行没有覆盖正文右侧空白。采样 {x},{y}，行顶 {top}，行高 {height}，客户区 {client.Right}×{client.Bottom}，标记层 {NativeMethods.SendMessage(editor, 2734, 0, 0)}。");
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }

    [DllImport("user32.dll", EntryPoint = "PrintWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint deviceContext, uint flags);

    private static void SaveCapture(string path, int width, int height, byte[] pixels)
    {
        using BinaryWriter writer = new(File.Create(path));
        writer.Write((ushort)0x4D42);
        writer.Write(54 + pixels.Length);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixels.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(pixels);
    }

    [DllImport("gdi32.dll", EntryPoint = "GdiFlush")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushCapture();
}
