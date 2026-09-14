using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeCommitDetailsLayoutTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("中文、标点和自然换行：这是一段带（括号）的完整说明。", "\n")]
    [DataRow("Alpha beta gamma delta epsilon zeta; preserve spaces and wrapping.", "\r\n")]
    [DataRow("  缩进\t文字 e\u0301 🧑‍💻 😀 最后一行。", "\n")]
    [DataRow("abcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghij", "\n")]
    [DataRow("مرحبا بالعالم هذا وصف التغيير الذي يحافظ على ترتيب الحروف.", "\n")]
    [DataRow("", "\r\n")]
    [DataRow("  \t  ", "\n")]
    public void 分块排版与完整排版画面一致且没有丢字(string sample, string newline)
    {
        string body = string.Join(newline, Enumerable.Repeat(sample, 17));
        const int width = 260, height = 1600;
        NativeCommitDetailsLayout layout = NativeCommitDetailsLayout.Create(body, "Consolas", 13, width, 0, 0, default);
        int consumed = 0;
        foreach (var block in layout.Blocks)
        {
            Assert.AreEqual(consumed, block.Start);
            Assert.IsGreaterThan(0, block.Length);
            consumed += block.Length;
            Assert.IsFalse(consumed < body.Length && char.IsLowSurrogate(body[consumed]));
        }
        Assert.AreEqual(body.Length, consumed);
        byte[] complete = Render(body, width, height, null);
        byte[] chunked = Render(body, width, height, layout);
        int differences = 0;
        List<int> rows = [];
        for (int index = 0; index < complete.Length; index++)
            if (complete[index] != chunked[index])
            {
                differences++;
                int row = index / (width * 4);
                if (rows.Count == 0 || rows[^1] != row) rows.Add(row);
            }
        TestContext.WriteLine($"分块：{string.Join("; ", layout.Blocks.Select(block => $"{block.Start}+{block.Length}@{block.Top:F6}/{block.Height:F6}"))}");
        Assert.AreEqual(0, differences, $"分块不能改变断行、缩进、空行或文字的垂直位置。差异行：{string.Join(',', rows)}。");
    }

    [TestMethod]
    public void 二十兆单段说明可以取消并完整排版到末尾()
    {
        string body = new('a', 20 * 1024 * 1024);
        using CancellationTokenSource cancelled = new();
        int previews = 0;
        Assert.ThrowsExactly<OperationCanceledException>(() => NativeCommitDetailsLayout.Create(body, "Consolas", 13,
            320, 0, 8, cancelled.Token, _ => { previews++; cancelled.Cancel(); }));
        Assert.AreEqual(1, previews);
        Stopwatch watch = Stopwatch.StartNew();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        NativeCommitDetailsLayout layout = NativeCommitDetailsLayout.Create(body, "Consolas", 13, 320, 0, 8, timeout.Token);
        TestContext.WriteLine($"20 MB 单段排版：{watch.Elapsed.TotalMilliseconds:F2}ms，{layout.Blocks.Length} 块。");
        Assert.AreEqual(body.Length, layout.Blocks[^1].Start + layout.Blocks[^1].Length);
        Assert.AreEqual((int)Math.Ceiling(layout.Blocks.Sum(block => (double)block.Height)) + 8, layout.Height,
            "长正文的累计高度不能因单精度累加而漂移。");
        Assert.IsGreaterThan(10000, layout.Height);
    }

    private static byte[] Render(string body, int width, int height, NativeCommitDetailsLayout? layout)
    {
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out nint bits, 0, 0);
        nint previousBitmap = NativeMethods.SelectObject(dc, bitmap);
        nint font = NativeMethods.CreateFont(-13, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Consolas");
        nint previousFont = NativeMethods.SelectObject(dc, font);
        GCHandle pinned = GCHandle.Alloc(body, GCHandleType.Pinned);
        try
        {
            Marshal.Copy(new byte[width * height * 4], 0, bits, width * height * 4);
            using NativeGdiPlusDrawing.WrappedTextSession text = new(dc);
            var blocks = layout?.Blocks ?? [text.Measure(pinned.AddrOfPinnedObject(), 0, body.Length, width, 0, 100)];
            foreach (var block in blocks) text.Draw(pinned.AddrOfPinnedObject(), block, 0, (float)block.Top, 0xffffff);
            _ = GdiFlush();
            byte[] pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            pinned.Free();
            _ = NativeMethods.SelectObject(dc, previousFont);
            _ = NativeMethods.SelectObject(dc, previousBitmap);
            _ = NativeMethods.DeleteObject(font);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();
}
