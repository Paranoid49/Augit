using System.Runtime.InteropServices;
using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 文件历史实际绘制作者日期标题并保留选择和局部滚动(int dpi, string theme)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            GitHistoryEntry entry = new("", new string('a', 40), "aaaaaaa", [], "", "",
                DateTimeOffset.UnixEpoch, "feat: 文件历史标题", []);
            service.Entries = [entry];
            service.CompleteDiffImmediately = true;
            _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(1180), NativeTheme.Scale(900), true);
            string? activeFile = window.ActiveDocumentPathForTest;
            window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
            await WaitUntilAsync(() => history.EntryCount == 1 && !history.OperationRunningForTest
                && history.FileHistoryComparisonForTest is { HasDocument: true, IsBusy: false });
            nint list = history.HistoryListHandleForTest;
            _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageHorizontalScroll, 6, 0);
            _ = NativeMethods.SetFocus(list);
            var emptyAuthor = ReadFileHistoryColumnPixels(list, theme);
            Assert.AreEqual(0, emptyAuthor.Author, "作者为空时首列应留空，不能画提交图或提交标题。");
            Assert.IsGreaterThan(15, emptyAuthor.Date, "日期必须在作者之后的独立列。");
            Assert.IsGreaterThan(15, emptyAuthor.Subject, "提交标题必须在日期之后。");

            service.Entries = [entry with { AuthorName = "I49" }];
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => !history.OperationRunningForTest && history.HistoryListDeltaCountForTest > 0);
            Assert.IsGreaterThan(10, ReadFileHistoryColumnPixels(list, theme).Author, "新增作者文字必须画在首列。");
            int reads = service.Calls.Count;
            int layouts = window.LayoutInvocationCountForTest;
            string? selected = history.SelectedCommitHashForTest;
            history.SetBounds(0, 0, NativeTheme.Scale(650), NativeTheme.Scale(300));
            Assert.IsTrue(NativeMethods.GetClientRectangle(list, out var narrow));
            int extent = (int)NativeMethods.SendMessage(list, NativeMethods.ListBoxGetHorizontalExtent, 0, 0);
            Assert.IsGreaterThan(narrow.Right, extent);
            Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(360), extent);
            _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageHorizontalScroll, 7, 0);
            Assert.IsGreaterThan(0, NativeMethods.GetScrollPosition(list, 0));
            history.SetBounds(0, 0, NativeTheme.Scale(1200), NativeTheme.Scale(300));
            Assert.AreEqual(0, NativeMethods.GetScrollPosition(list, 0), "恢复宽栏后必须收回无效偏移。");
            Assert.AreEqual(selected, history.SelectedCommitHashForTest);
            Assert.HasCount(reads, service.Calls);
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.AreEqual(activeFile, window.ActiveDocumentPathForTest);
            Assert.AreEqual(list, history.HistoryListHandleForTest);
        }, theme, preciseDpi: true);
    }

    private static (int Author, int Date, int Subject) ReadFileHistoryColumnPixels(nint list, string theme)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(list, out var client));
        NativeMethods.Rectangle row = default;
        _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxGetItemRectangle, 0, ref row);
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = client.Right,
                Height = -client.Bottom,
                Planes = 1,
                BitCount = 32,
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out nint bits, 0, 0);
        nint old = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            Assert.IsTrue(PrintHistoryWindow(list, dc, 3));
            Assert.IsTrue(FlushHistoryCapture());
            byte[] pixels = new byte[client.Right * client.Bottom * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            uint background = NativeTheme.Palette(NativeTheme.IsDark(theme)).AccentSoft;
            int author = 0, date = 0, subject = 0;
            for (int y = row.Top + NativeTheme.Scale(2); y < Math.Min(row.Bottom - NativeTheme.Scale(2), client.Bottom); y++)
            {
                for (int x = 0; x < client.Right - NativeTheme.Scale(10); x++)
                {
                    int offset = (y * client.Right + x) * 4;
                    uint pixel = (uint)(pixels[offset + 2] | pixels[offset + 1] << 8 | pixels[offset] << 16);
                    if (NearTextColor(pixel, background)) continue;
                    if (x < NativeTheme.Scale(140)) author++;
                    else if (x < NativeTheme.Scale(230)) date++;
                    else subject++;
                }
            }
            return (author, date, subject);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, old);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }
}
