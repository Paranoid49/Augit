using System.Runtime.InteropServices;
using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeCloneDialogInteractionTests
{
    private static readonly int[] FormIds = [20, 21, 22, 14, 23, 25, 30, 31, 32];
    private static readonly int[] FooterIds = [11, 12];
    public TestContext TestContext { get; set; } = null!;

    public static IEnumerable<object[]> 布局状态矩阵()
    {
        foreach (int dpi in new[] { 96, 120, 144 })
            foreach (bool dark in new[] { false, true })
                foreach (int size in new[] { 13, 40 })
                    foreach (int width in new[] { 1024, 1440 })
                        yield return [dark, dpi, size, width];
    }

    [TestMethod]
    [DynamicData(nameof(布局状态矩阵))]
    public async Task 字号布局与长错误滚动保持固定底栏并可重试(bool dark, int dpi, int size, int width)
    {
        using Session session = new(dark, dpi, size, width, width == 1024 ? 640 : 900);
        NativeMethods.Rectangle window = default, clone = default, cancel = default, close = default;
        await session.InvokeAsync(() =>
        {
            window = Bounds(session.Dialog.HandleForTest);
            Contains(Bounds(session.Owner), window);
            nint body = session.Dialog.BodyForTest;
            Assert.AreEqual(0, NativeMethods.GetScrollPosition(body, 1));
            foreach (int id in FormIds)
            {
                var bounds = Bounds(session.Item(id));
                Contains(Bounds(body), bounds);
                Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, bounds.Bottom - bounds.Top, $"控件 {id} 裁字。");
            }
            Assert.IsLessThanOrEqualTo(Bounds(session.Item(21)).Top, Bounds(session.Item(20)).Bottom);
            Assert.IsLessThanOrEqualTo(Bounds(session.Item(22)).Top, Bounds(session.Item(21)).Bottom);
            var shallow = Bounds(session.Item(14)); var depth = Bounds(session.Item(23));
            Assert.IsTrue(depth.Left >= shallow.Right || depth.Top >= shallow.Bottom, "浅克隆勾选与深度不能重叠。");
            clone = Bounds(session.Item(11)); cancel = Bounds(session.Item(12)); close = Bounds(session.Item(13));
            var version = Bounds(session.Item(20));
            Assert.AreEqual(clone.Bottom - clone.Top - NativeTheme.Scale(2), version.Bottom - version.Top,
                "下拉框的系统实际高度不能盖住父表单外框。");
            foreach (int id in FooterIds)
            {
                var bounds = Bounds(session.Item(id)); Contains(window, bounds);
                Assert.IsGreaterThanOrEqualTo(Bounds(body).Bottom, bounds.Top);
                AssertButtonTextFits(session.Item(id), id == 11 ? UiText.Cloning : UiText.CancelOperation);
            }
            Assert.IsLessThanOrEqualTo(clone.Left, cancel.Right);
            Assert.IsLessThanOrEqualTo(Bounds(body).Top, close.Bottom);
            if (size == 13)
            {
                Assert.AreEqual(NativeTheme.Scale(930), window.Right - window.Left);
                Assert.AreEqual(NativeTheme.Scale(289), window.Bottom - window.Top);
                _ = NativeMethods.SendMessage(body, NativeMethods.WindowMessageVerticalScroll, 7, 0);
                Assert.AreEqual(0, NativeMethods.GetScrollPosition(body, 1), "初始表单不能多出舍入滚动条。");
            }
            Assert.IsFalse(NativeMethods.IsWindowEnabled(session.Item(23)));
            AssertSuffixPalette(session.Item(25), dark);
            AssertComboSurface(session.Item(20), dark);
            _ = NativeMethods.SetFocus(session.Item(21));
            if (dpi == 96 && width == 1024) SaveDialogCapture(session.Dialog.HandleForTest, $"clone-initial-{dark}-{size}");
            Fill(session); Click(session.Item(14));
            _ = NativeMethods.SetWindowText(session.Item(23), "7");
            Click(session.Item(11));
            Assert.AreEqual(window, Bounds(session.Dialog.HandleForTest));
            Assert.AreEqual(clone, Bounds(session.Item(11))); Assert.AreEqual(cancel, Bounds(session.Item(12)));
        });
        string error = string.Join('\n', Enumerable.Repeat("服务器暂时不可达，请检查仓库地址或网络后重试。", 12));
        session.Service.Complete(GitRepositoryOperationResult.Failure(GitOperationFailureKind.CommandFailed, error));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            nint body = session.Dialog.BodyForTest;
            Assert.AreEqual(error, Notice(session));
            Assert.AreEqual(window, Bounds(session.Dialog.HandleForTest));
            Assert.AreEqual(clone, Bounds(session.Item(11))); Assert.AreEqual(cancel, Bounds(session.Item(12))); Assert.AreEqual(close, Bounds(session.Item(13)));
            _ = NativeMethods.SendMessage(body, NativeMethods.WindowMessageVerticalScroll, 7, 0);
            Assert.IsGreaterThan(0, NativeMethods.GetScrollPosition(body, 1));
            Assert.IsLessThanOrEqualTo(Bounds(body).Bottom, Bounds(session.Item(24)).Bottom);
            if (dpi == 96 && width == 1024) SaveDialogCapture(session.Dialog.HandleForTest, $"clone-error-{dark}-{size}");
            _ = NativeMethods.SetFocus(session.Item(21));
            Contains(Bounds(body), Bounds(session.Item(21)));
            _ = NativeMethods.SendMessage(body, NativeMethods.WindowMessageVerticalScroll, 6, 0);
            _ = NativeMethods.SendMessage(body, NativeMethods.WindowMessageMouseWheel, unchecked((nuint)(ushort)-120) << 16, 0);
            Assert.IsGreaterThan(0, NativeMethods.GetScrollPosition(body, 1));
            Assert.AreEqual(" https://example.invalid/team/repo.git ", NativeMethods.GetWindowTextValue(session.Item(21)));
            Assert.AreEqual("7", NativeMethods.GetWindowTextValue(session.Item(23)));
            Assert.AreEqual((nint)1, NativeMethods.SendMessage(session.Item(14), NativeMethods.ButtonMessageGetCheck, 0, 0));
            Assert.IsTrue(NativeMethods.IsWindowEnabled(session.Item(23)));
            session.Service.Prepare(); Click(session.Item(11));
            Assert.AreEqual(2, session.Service.Calls);
            Assert.AreEqual(clone, Bounds(session.Item(11))); Assert.AreEqual(cancel, Bounds(session.Item(12)));
        });
        session.Service.Complete(Success());
        Assert.AreEqual(@"D:\clone-result", await session.ClosedAsync());
        session.AssertRestored();
    }

    private static NativeMethods.Rectangle Bounds(nint window)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(window, out var bounds)); return bounds;
    }

    private static void Contains(NativeMethods.Rectangle parent, NativeMethods.Rectangle child)
    {
        Assert.IsTrue(child.Left >= parent.Left && child.Top >= parent.Top && child.Right <= parent.Right && child.Bottom <= parent.Bottom,
            $"控件越界：父 {parent.Left},{parent.Top},{parent.Right},{parent.Bottom}；子 {child.Left},{child.Top},{child.Right},{child.Bottom}");
    }

    private static void AssertButtonTextFits(nint control, string text)
    {
        nint dc = NativeMethods.GetDeviceContext(control);
        nint previous = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        try
        {
            NativeMethods.Rectangle size = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref size, NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine);
            var bounds = Bounds(control);
            Assert.IsGreaterThanOrEqualTo(size.Right + NativeTheme.Scale(16), bounds.Right - bounds.Left);
            Assert.IsGreaterThanOrEqualTo(size.Bottom, bounds.Bottom - bounds.Top);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(control, dc); }
    }

    private static void AssertSuffixPalette(nint control, bool dark)
    {
        _ = NativeMethods.GetClientRectangle(control, out var rect);
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new() { Header = new() { Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(), Width = rect.Right, Height = -rect.Bottom, Planes = 1, BitCount = 32 } };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            _ = NativeMethods.SendMessage(control, 0x0318, unchecked((nuint)dc), 4);
            var palette = NativeTheme.Palette(dark);
            int ink = 0;
            for (int y = 0; y < rect.Bottom; y++)
                for (int x = 0; x < rect.Right; x++)
                {
                    uint color = NativeMethods.GetPixel(dc, x, y);
                    if (color == palette.Faint) ink++;
                    for (int shift = 0; shift < 24; shift += 8)
                    {
                        uint channel = (color >> shift) & 255, a = (palette.Panel >> shift) & 255, b = (palette.Faint >> shift) & 255;
                        Assert.IsTrue(channel >= Math.Min(a, b) && channel <= Math.Max(a, b), "禁用单位文字不能混入系统浮雕色。");
                    }
                }
            Assert.IsGreaterThan(0, ink, "禁用单位文字仍须可见。");
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.DeleteObject(bitmap); _ = NativeMethods.DeleteDeviceContext(dc); }
    }

    private static void AssertComboSurface(nint control, bool dark)
    {
        // 箭头旁单个像素不足以发现白边；收起、展开再收起及禁用都检查整个客户区周边。
        foreach (int state in new[] { 0, 1, 2 })
        {
            if (state == 1)
            {
                _ = NativeMethods.SendMessage(control, 0x014F, 1, 0);
                Assert.AreEqual((nint)1, NativeMethods.SendMessage(control, 0x0157, 0, 0));
                _ = NativeMethods.SendMessage(control, 0x014F, 0, 0);
                Assert.AreEqual((nint)0, NativeMethods.SendMessage(control, 0x0157, 0, 0));
            }
            if (state == 2) _ = NativeMethods.EnableWindow(control, false);
            _ = NativeMethods.InvalidateRectangle(control, 0, true); _ = NativeMethods.UpdateWindow(control);
            _ = NativeMethods.GetClientRectangle(control, out var rect);
            nint dc = NativeMethods.GetDeviceContext(control);
            try
            {
                uint expected = NativeTheme.Palette(dark).Panel;
                for (int x = 0; x < rect.Right; x++)
                {
                    Assert.AreEqual(expected, NativeMethods.GetPixel(dc, x, 0), $"状态 {state} 上边界仍含系统边框。");
                    Assert.AreEqual(expected, NativeMethods.GetPixel(dc, x, rect.Bottom - 1), $"状态 {state} 下边界仍含系统边框。");
                }
                for (int y = 0; y < rect.Bottom; y++)
                {
                    Assert.AreEqual(expected, NativeMethods.GetPixel(dc, 0, y), $"状态 {state} 左边界仍含系统边框。");
                    Assert.AreEqual(expected, NativeMethods.GetPixel(dc, rect.Right - 1, y), $"状态 {state} 右边界仍含系统边框。");
                }
            }
            finally
            {
                _ = NativeMethods.ReleaseDeviceContext(control, dc);
                if (state == 2) _ = NativeMethods.EnableWindow(control, true);
            }
        }
    }

    private void SaveDialogCapture(nint window, string name)
    {
        if (TestContext.TestResultsDirectory is not { } directory) return;
        _ = NativeMethods.GetClientRectangle(window, out var rect);
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new() { Header = new() { Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(), Width = rect.Right, Height = -rect.Bottom, Planes = 1, BitCount = 32 } };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out nint bits, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            Assert.IsTrue(PrintWindow(window, dc, 3)); Assert.IsTrue(GdiFlush());
            byte[] pixels = new byte[checked(rect.Right * rect.Bottom * 4)]; Marshal.Copy(bits, pixels, 0, pixels.Length);
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, name + ".bmp");
            using (BinaryWriter writer = new(File.Create(file)))
            {
                writer.Write((ushort)0x4D42); writer.Write(54 + pixels.Length); writer.Write(0); writer.Write(54);
                writer.Write(40); writer.Write(rect.Right); writer.Write(-rect.Bottom); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(0); writer.Write(pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(pixels);
            }
            TestContext.AddResultFile(file);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.DeleteObject(bitmap); _ = NativeMethods.DeleteDeviceContext(dc); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
}
