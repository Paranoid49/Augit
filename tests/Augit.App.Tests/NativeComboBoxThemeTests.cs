using System.Runtime.InteropServices;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeComboBoxThemeTests
{
    private static readonly string[] Labels = ["Soft · 仅移动 HEAD", "Mixed · 同时重置索引", "Hard · 重置索引和工作区"];
    private static readonly bool[] ParentFrameCases = [false, true];

    [TestMethod]
    [DataRow(false, 96, 13)]
    [DataRow(true, 96, 13)]
    [DataRow(false, 120, 13)]
    [DataRow(true, 120, 13)]
    [DataRow(false, 144, 13)]
    [DataRow(true, 144, 13)]
    [DataRow(false, 96, 40)]
    [DataRow(true, 96, 40)]
    [DataRow(false, 120, 40)]
    [DataRow(true, 120, 40)]
    [DataRow(false, 144, 40)]
    [DataRow(true, 144, 40)]
    public async Task 收起表面主题一致且保留选择焦点和销毁行为(bool dark, int dpi, int size)
    {
        await OnStaAsync(() =>
        {
            using IDisposable audit = NativeTheme.PushVisualAuditDpiOverride(dpi);
            NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
            int before = NativeComboBoxTheme.RegisteredCountForTest;
            nint owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "选择框专项测试",
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
                20, 20, S(900), S(360), 0, 0, NativeMethods.GetModuleHandle(null), 0);
            var palette = NativeTheme.Palette(dark);
            nint brush = NativeMethods.CreateSolidBrush(palette.Panel);
            NativeMethods.SubclassProcedure procedure = (window, message, word, parameter, id, data) =>
            {
                if (message == NativeMethods.WindowMessageDrawItem)
                    return NativeComboBoxTheme.DrawItem(parameter, Labels, dark) ? 1 : 0;
                if (message is NativeMethods.WindowMessageControlColorListBox or NativeMethods.WindowMessageControlColorStatic)
                {
                    _ = NativeMethods.SetTextColor((nint)word, palette.Text);
                    _ = NativeMethods.SetBackgroundColor((nint)word, palette.Panel);
                    return brush;
                }
                return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
            };
            try
            {
                Assert.AreNotEqual((nint)0, owner);
                Assert.IsTrue(NativeMethods.SetWindowSubclass(owner, procedure, 1, 0));
                foreach (bool parentFrame in ParentFrameCases)
                {
                    nint combo = NativeMethods.CreateWindow(0, NativeMethods.ComboBoxClass, string.Empty,
                        NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleTabStop | NativeComboBoxTheme.ControlStyle,
                        S(20), S(20), S(760), S(200), owner, 20, NativeMethods.GetModuleHandle(null), 0);
                    Assert.AreNotEqual((nint)0, combo);
                    NativeTheme.ApplyToControl(combo, dark);
                    foreach (string label in Labels) _ = NativeMethods.SendMessage(combo, NativeMethods.ComboBoxAddString, 0, label);
                    _ = NativeMethods.SendMessage(combo, NativeMethods.ComboBoxSetCurrentSelection, 2, 0);
                    int logicalHeight = Math.Max(30, (int)Math.Ceiling(NativeTheme.UiLineHeight * 96d / dpi) + 10);
                    Assert.IsTrue(NativeComboBoxTheme.Register(combo, () => dark, logicalHeight, parentFrame));
                    Assert.IsFalse(NativeComboBoxTheme.Register(combo, () => dark), "重复登记不能覆盖生命周期状态。");
                    var baseline = Capture(combo);
                    Assert.AreEqual(Labels[2], NativeComboBoxTheme.LabelForTest(combo));
                    var text = NativeComboBoxTheme.TextBoundsForTest(combo);
                    Assert.IsGreaterThanOrEqualTo(NativeDialogBody.Measure(combo, Labels[2]).Width, text.Right - text.Left);
                    AssertSurface(baseline, palette, parentFrame, focused: false);
                    _ = NativeMethods.SetFocus(combo);
                    AssertSurface(Capture(combo), palette, parentFrame, focused: true);
                    _ = NativeMethods.SendMessage(combo, 0x014F, 1, 0);
                    Assert.AreEqual((nint)1, NativeMethods.SendMessage(combo, 0x0157, 0, 0));
                    _ = NativeMethods.SendMessage(combo, 0x014F, 0, 0);
                    Assert.AreEqual((nint)0, NativeMethods.SendMessage(combo, 0x0157, 0, 0));
                    _ = NativeMethods.SendMessage(combo, NativeMethods.WindowMessageKeyDown, 0x26, 0);
                    Assert.AreEqual((nint)1, NativeMethods.SendMessage(combo, NativeMethods.ComboBoxGetCurrentSelection, 0, 0));
                    var changed = Capture(combo);
                    Assert.AreEqual(Labels[1], NativeComboBoxTheme.LabelForTest(combo));
                    Assert.IsFalse(baseline.Pixels.SequenceEqual(changed.Pixels), "选项变化必须反映到真实绘制。");
                    _ = NativeMethods.EnableWindow(combo, false);
                    var disabled = Capture(combo);
                    AssertSurface(disabled, palette, parentFrame, focused: false);
                    Assert.IsTrue(disabled.Pixels.Contains(palette.Faint), "禁用文字仍可见且使用主题弱化色。");
                    Assert.AreEqual(baseline.Width, disabled.Width);
                    Assert.AreEqual(baseline.Height, disabled.Height);
                    _ = NativeMethods.EnableWindow(combo, true);
                    _ = NativeMethods.SendMessage(combo, 0x014B, 0, 0);
                    _ = NativeMethods.SendMessage(combo, NativeMethods.ComboBoxAddString, 0, "替换后的选项");
                    _ = NativeMethods.SendMessage(combo, NativeMethods.ComboBoxSetCurrentSelection, 0, 0);
                    _ = Capture(combo);
                    Assert.AreEqual("替换后的选项", NativeComboBoxTheme.LabelForTest(combo));
                    Assert.IsTrue(NativeMethods.DestroyWindow(combo));
                    Assert.AreEqual(before, NativeComboBoxTheme.RegisteredCountForTest, "销毁必须自动解除主题登记。");
                }
            }
            finally
            {
                if (NativeMethods.IsWindow(owner)) _ = NativeMethods.DestroyWindow(owner);
                _ = NativeMethods.DeleteObject(brush);
                GC.KeepAlive(procedure);
            }
            Assert.AreEqual(before, NativeComboBoxTheme.RegisteredCountForTest);
        });
    }

    private static void AssertSurface(Snapshot shot, NativeThemePalette palette, bool parentFrame, bool focused)
    {
        if (parentFrame)
        {
            for (int x = 0; x < shot.Width; x++)
            {
                Assert.AreEqual(palette.Panel, shot.At(x, 0), "父表单已有外框，选择框不能再画系统边框。");
                Assert.AreEqual(palette.Panel, shot.At(x, shot.Height - 1));
            }
            for (int y = 0; y < shot.Height; y++)
            {
                Assert.AreEqual(palette.Panel, shot.At(0, y));
                Assert.AreEqual(palette.Panel, shot.At(shot.Width - 1, y));
            }
        }
        else
        {
            uint expected = focused ? palette.Accent : palette.Border;
            Assert.IsTrue(shot.Pixels.Contains(expected), "独立选择框的主题边界／焦点环必须可见。");
            Assert.AreEqual(palette.Panel, shot.At(0, 0), "圆角外部仍使用父面板背景。");
        }
        Assert.AreEqual(palette.Panel, shot.At(shot.Width - S(32), shot.Height / 2), "箭头左侧不保留系统白底或选中底。");
    }

    private sealed record Snapshot(int Width, int Height, uint[] Pixels)
    {
        internal uint At(int x, int y) => Pixels[y * Width + x];
    }

    private static Snapshot Capture(nint window)
    {
        _ = NativeMethods.InvalidateRectangle(window, 0, false); _ = NativeMethods.UpdateWindow(window);
        _ = NativeMethods.GetClientRectangle(window, out var rect);
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new() { Header = new() { Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(), Width = rect.Right, Height = -rect.Bottom, Planes = 1, BitCount = 32 } };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out nint bits, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            Assert.IsTrue(PrintWindow(window, dc, 2));
            int[] raw = new int[rect.Right * rect.Bottom]; Marshal.Copy(bits, raw, 0, raw.Length);
            uint[] pixels = raw.Select(value => (uint)((value & 255) << 16 | value & 0xFF00 | (value >> 16) & 255)).ToArray();
            return new(rect.Right, rect.Bottom, pixels);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.DeleteObject(bitmap); _ = NativeMethods.DeleteDeviceContext(dc); }
    }

    private static async Task OnStaAsync(Action action)
    {
        TaskCompletionSource complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            string family = NativeTheme.UiFontFamilyForTest; double size = NativeTheme.UiFontSizeForTest;
            nint awareness = SetThreadDpiAwarenessContext(-4);
            try { action(); complete.SetResult(); }
            catch (Exception error) { complete.SetException(error); }
            finally { NativeTheme.ConfigureUiTypography(family, size); _ = SetThreadDpiAwarenessContext(awareness); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await complete.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(3)), "测试线程必须退出。"); }
    }

    private static int S(int value) => NativeTheme.Scale(value);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint value);
}
