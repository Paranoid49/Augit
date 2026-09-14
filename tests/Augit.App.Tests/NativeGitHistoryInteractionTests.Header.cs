using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    private static readonly int[] HeaderFontSizes = [13, 40];

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 历史标题和文件标签按统一内距绘制且返回不改变位置(int dpi, string theme)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            service.CompleteDiffImmediately = true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            ApplicationSettings original = (ApplicationSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
            foreach (int size in HeaderFontSizes)
            {
                typeof(MainWindow).GetMethod("ApplyConfirmedSettings", flags)!.Invoke(window,
                    [original, original with { TextFontSize = size }]);
                string? selected = history.SelectedCommitHashForTest;
                nint list = history.HistoryListHandleForTest;
                int reads = service.PageReads;
                foreach (int width in HistoryWindowWidths)
                {
                    _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(width), NativeTheme.Scale(900), true);
                    AssertHistoryHeader(history, theme);
                }
                Assert.AreEqual(reads, service.PageReads, "标题布局不能重新查询 Git。");
                var logBefore = HistoryControlBounds(HistoryChild(history.Handle, 60));
                window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
                await WaitUntilAsync(() => history.FileHistoryModeForTest && !history.OperationRunningForTest);
                AssertHistoryHeader(history, theme);
                Assert.AreEqual(logBefore, HistoryControlBounds(HistoryChild(history.Handle, 60)), "进入文件历史不能移动日志标签。");
                foreach (int width in HistoryWindowWidths)
                {
                    _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(width), NativeTheme.Scale(900), true);
                    AssertHistoryHeader(history, theme);
                }
                // 点击已有日志标签返回，不借用测试专用状态恢复入口。
                _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 60), NativeMethods.WindowMessageLeftButtonDown, 1, 0);
                _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 60), NativeMethods.WindowMessageLeftButtonUp, 0, 0);
                await WaitUntilAsync(() => !history.FileHistoryModeForTest && !history.OperationRunningForTest);
                AssertHistoryHeader(history, theme);
                Assert.AreEqual(list, history.HistoryListHandleForTest);
                Assert.AreEqual(selected, history.SelectedCommitHashForTest);
            }
        }, theme, preciseDpi: true);
    }

    private void AssertHistoryHeader(NativeGitHistoryPanel history, string theme)
    {
        var panel = HistoryControlBounds(history.Handle);
        nint titleControl = HistoryChild(history.Handle, 62), logControl = HistoryChild(history.Handle, 60);
        var title = HistoryControlBounds(titleControl);
        var log = HistoryControlBounds(logControl);
        Assert.AreEqual(NativeTheme.Scale(12), title.Left - panel.Left);
        Assert.AreEqual(MeasureHistoryText("Git", NativeTheme.UiMediumFont).Width, title.Right - title.Left);
        Assert.AreEqual(NativeTheme.Scale(11), log.Left - title.Right);
        Assert.AreEqual(MeasureHistoryText("日志", NativeTheme.UiFont).Width + NativeTheme.Scale(22), log.Right - log.Left);
        Assert.AreEqual(title.Top, log.Top);
        Assert.AreEqual(title.Bottom, log.Bottom);
        AssertHeaderTextPixels(titleControl, theme, title: true, active: false);
        AssertHeaderTextPixels(logControl, theme, title: false, active: !history.FileHistoryModeForTest);
        if (history.FileHistoryModeForTest)
        {
            nint fileControl = HistoryChild(history.Handle, 66);
            var file = HistoryControlBounds(fileControl);
            Assert.AreEqual(NativeTheme.Scale(4), file.Left - log.Right);
            int desired = Math.Min(NativeTheme.Scale(280), MeasureHistoryText(NativeMethods.GetWindowTextValue(fileControl), NativeTheme.UiFont).Width + NativeTheme.Scale(22));
            Assert.AreEqual(Math.Min(desired, Math.Max(0, panel.Right - file.Left - NativeTheme.Scale(76))), file.Right - file.Left);
            Assert.AreEqual(log.Top, file.Top);
            Assert.AreEqual(log.Bottom, file.Bottom);
            Assert.IsLessThanOrEqualTo(HistoryControlBounds(HistoryChild(history.Handle, 22)).Left, file.Right);
            AssertHeaderTextPixels(fileControl, theme, title: false, active: true);
        }
    }

    private void AssertHeaderTextPixels(nint control, string theme, bool title, bool active)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(control, out var bounds));
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = bounds.Right,
                Height = -bounds.Bottom,
                Planes = 1,
                BitCount = 32,
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out nint bits, 0, 0);
        Assert.AreNotEqual((nint)0, bitmap);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            Assert.IsTrue(PrintHistoryWindow(control, dc, 3));
            Assert.IsTrue(FlushHistoryCapture());
            byte[] actual = new byte[bounds.Right * bounds.Bottom * 4];
            Marshal.Copy(bits, actual, 0, actual.Length);
            bool dark = NativeTheme.IsDark(theme);
            NativeThemePalette palette = NativeTheme.Palette(dark);
            var colors = NativeTheme.SelectedToolTabColors(dark);
            if (active)
                Assert.AreEqual(colors.Border, NativeMethods.GetPixel(dc, bounds.Right / 2, 0), "标签外框不能再缩进一圈。");
            nint brush = NativeMethods.CreateSolidBrush(active ? colors.Fill : palette.Panel);
            _ = NativeMethods.FillRectangle(dc, ref bounds, brush);
            _ = NativeMethods.DeleteObject(brush);
            nint oldFont = NativeMethods.SelectObject(dc, title ? NativeTheme.UiMediumFont : NativeTheme.UiFont);
            try
            {
                NativeMethods.Rectangle text = bounds;
                if (!title) { text.Left += NativeTheme.Scale(11); text.Right -= NativeTheme.Scale(11); }
                _ = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
                _ = NativeMethods.SetTextColor(dc, palette.Text);
                string label = NativeMethods.GetWindowTextValue(control);
                _ = NativeMethods.DrawText(dc, label, label.Length, ref text, NativeMethods.DrawTextSingleLine
                    | NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis);
                Assert.IsTrue(FlushHistoryCapture());
                byte[] expected = new byte[actual.Length];
                Marshal.Copy(bits, expected, 0, expected.Length);
                int ink = 0, matched = 0;
                for (int offset = 0; offset < expected.Length; offset += 4)
                {
                    uint color = (uint)(expected[offset + 2] | expected[offset + 1] << 8 | expected[offset] << 16);
                    if (!NearTextColor(color, palette.Text)) continue;
                    ink++;
                    uint pixel = (uint)(actual[offset + 2] | actual[offset + 1] << 8 | actual[offset] << 16);
                    if (NearTextColor(pixel, color)) matched++;
                }
                if (ink < 10 || matched < ink * 0.95) SaveHistoryCapture(bounds.Right, bounds.Bottom, actual);
                Assert.IsGreaterThanOrEqualTo(10, ink);
                Assert.IsGreaterThanOrEqualTo(ink * 0.95, matched, $"{label} 的文字起点或绘制宽度与布局不一致：{matched}/{ink}。");
            }
            finally { _ = NativeMethods.SelectObject(dc, oldFont); }
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }
}
