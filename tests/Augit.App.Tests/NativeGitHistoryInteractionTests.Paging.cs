using System.Runtime.InteropServices;

using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 历史分页失败或取消后重试同页且读取期间继续浏览(bool cancel) => RunAsync(async (window, history, service) =>
    {
        GitHistoryEntry[] entries = CreatePagedHistory();
        service.Entries = entries;
        service.ReadPage = (request, _) => Task.FromResult(HistoryPage(entries, request.Page));
        window.RequestHistoryRefreshForTest();
        await WaitUntilAsync(() => history.EntryCount == 100 && !history.OperationRunningForTest);
        TaskCompletionSource<GitHistoryResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.PendingPages.Add(pending);
        CancellationToken queryToken = default;
        service.ReadPage = (request, token) => { queryToken = token; return pending.Task; };
        nint list = history.HistoryListHandleForTest;
        int resets = history.HistoryListResetCountForTest;
        ScrollToHistoryEnd(list);
        await WaitUntilAsync(() => history.OperationRunningForTest && service.PageRequests[^1].Page == 1);
        Assert.AreEqual(0, history.CurrentPageForTest, "成功接纳结果前不能递增页码。");
        Assert.IsTrue(NativeMethods.IsWindowEnabled(list), "后台加载不应禁止浏览已有提交。");
        int top = history.HistoryListTopIndexForTest;
        ClickRow(list, top);
        await WaitUntilAsync(() => history.SelectedCommitHashForTest == entries[top].FullHash && history.CommitDetailsLoadedForTest);
        string? selected = history.SelectedCommitHashForTest;
        int reads = service.PageReads;
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyRight, 0);
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageHorizontalScroll, 7, 0);
        Assert.AreEqual(reads, service.PageReads);
        selected = history.SelectedCommitHashForTest;
        if (cancel)
        {
            Assert.IsTrue(history.CancelOperationForHost());
            Assert.IsTrue(queryToken.IsCancellationRequested);
            // 取消后服务仍返回成功，界面也不能接纳这份晚到结果。
            pending.SetResult(HistoryPage(entries, 1));
        }
        else pending.SetResult(GitHistoryResult.Failure(GitOperationFailureKind.CommandFailed, "分页查询失败"));
        await WaitUntilAsync(() => !history.OperationRunningForTest);
        Assert.AreEqual(100, history.EntryCount);
        Assert.AreEqual(0, history.CurrentPageForTest);
        Assert.AreEqual(selected, history.SelectedCommitHashForTest);
        service.ReadPage = (request, _) => Task.FromResult(HistoryPage(entries, request.Page));
        ScrollToHistoryEnd(list);
        await WaitUntilAsync(() => history.EntryCount == entries.Length && !history.OperationRunningForTest);
        Assert.AreEqual(1, service.PageRequests[^1].Page);
        Assert.AreEqual(1, history.CurrentPageForTest);
        Assert.AreEqual(top, history.HistoryListTopIndexForTest);
        Assert.AreEqual(selected, history.SelectedCommitHashForTest);
        Assert.AreEqual(resets, history.HistoryListResetCountForTest);
    });

    [TestMethod]
    public Task 历史分页重复结果不重绘图形且前页变短后不查询不存在的页() => RunAsync(async (window, history, service) =>
    {
        GitHistoryEntry[] entries = CreatePagedHistory();
        service.Entries = entries;
        service.ReadPage = (request, _) => Task.FromResult(HistoryPage(entries, request.Page));
        window.RequestHistoryRefreshForTest();
        await WaitUntilAsync(() => history.EntryCount == 100 && !history.OperationRunningForTest);
        int builds = history.CommitGraphBuildCountForTest;
        service.ReadPage = (request, _) => Task.FromResult(GitHistoryResult.Success(new(1, 100, true, true, entries[..100])));
        ScrollToHistoryEnd(history.HistoryListHandleForTest);
        await WaitUntilAsync(() => history.CurrentPageForTest == 1 && !history.OperationRunningForTest);
        Assert.AreEqual(100, history.EntryCount);
        Assert.AreEqual(builds, history.CommitGraphBuildCountForTest);
        service.ReadPage = (request, _) => Task.FromResult(HistoryPage(entries[..20], request.Page));
        int reads = service.PageReads;
        window.RequestHistoryRefreshForTest();
        await WaitUntilAsync(() => history.EntryCount == 20 && !history.OperationRunningForTest);
        Assert.AreEqual(reads + 1, service.PageReads, "第零页已到结尾时不应继续读取旧的后续页。");
        Assert.AreEqual(0, history.CurrentPageForTest);
        Assert.IsFalse(history.HasNextPageForTest);
    });

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 历史分页后进入文件历史并返回恢复纵横位置且忽略旧查询(int dpi, string theme)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            GitHistoryEntry[] entries = CreatePagedHistory(wide: true);
            service.Entries = entries;
            service.CompleteDiffImmediately = true;
            service.ReadPage = (request, _) => Task.FromResult(HistoryPage(entries, request.Page));
            Assert.IsTrue(NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(1024), NativeTheme.Scale(760), true));
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => history.EntryCount == 100 && !history.OperationRunningForTest);
            nint list = history.HistoryListHandleForTest;
            ScrollToHistoryEnd(list);
            await WaitUntilAsync(() => history.EntryCount == entries.Length && !history.OperationRunningForTest);
            int row = history.HistoryListTopIndexForTest;
            ClickRow(list, row);
            await WaitUntilAsync(() => history.SelectedCommitHashForTest == entries[row].FullHash && history.CommitDetailsLoadedForTest);
            _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageHorizontalScroll, 7, 0);
            int horizontal = NativeMethods.GetScrollPosition(list, 0);
            Assert.IsGreaterThan(0, horizontal);
            AssertHistoryTextVisible(list, row, theme);
            string? top = history.HistoryListTopHashForTest, selected = history.SelectedCommitHashForTest;
            service.ReadPage = (request, _) => Task.FromResult(HistoryPage([entries[^1]], request.Page));
            window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
            await WaitUntilAsync(() => history.FileHistoryModeForTest && history.EntryCount == 1 && !history.OperationRunningForTest);
            TaskCompletionSource<GitHistoryResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            service.PendingPages.Add(pending);
            CancellationToken staleToken = default;
            service.ReadPage = (_, token) => { staleToken = token; return pending.Task; };
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => history.OperationRunningForTest);
            // 使用原生按钮消息走真正的清除文件历史路径。
            nint clear = HistoryChild(history.Handle, 37);
            Assert.IsTrue(NativeMethods.IsWindowEnabled(clear));
            _ = NativeMethods.SetFocus(clear);
            _ = NativeMethods.SendMessage(clear, 0x00F5, 0, 0);
            Assert.IsTrue(staleToken.IsCancellationRequested);
            Assert.IsFalse(history.FileHistoryModeForTest);
            Assert.AreEqual(entries.Length, history.EntryCount);
            Assert.AreEqual(1, history.CurrentPageForTest);
            Assert.AreEqual(horizontal, NativeMethods.GetScrollPosition(list, 0));
            Assert.AreEqual(top, history.HistoryListTopHashForTest);
            Assert.AreEqual(selected, history.SelectedCommitHashForTest);
            Assert.AreEqual(list, NativeMethods.GetFocus(), "清除入口隐藏后焦点应回到可操作的历史列表。");
            AssertHistoryTextVisible(list, row, theme);
            pending.SetResult(HistoryPage([entries[^1]], 0));
            await WaitUntilAsync(() => !history.OperationRunningForTest);
            Assert.AreEqual(entries.Length, history.EntryCount);
            Assert.AreEqual(horizontal, NativeMethods.GetScrollPosition(list, 0));
            Assert.AreEqual(top, history.HistoryListTopHashForTest);
        }, theme);
    }

    [TestMethod]
    public Task 历史分页切换文件筛选后旧页不得覆盖新上下文() => RunAsync(async (window, history, service) =>
    {
        GitHistoryEntry[] entries = CreatePagedHistory();
        service.Entries = entries;
        service.CompleteDiffImmediately = true;
        service.ReadPage = (request, _) => Task.FromResult(HistoryPage(entries, request.Page));
        window.RequestHistoryRefreshForTest();
        await WaitUntilAsync(() => history.EntryCount == 100 && !history.OperationRunningForTest);
        TaskCompletionSource<GitHistoryResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.PendingPages.Add(pending);
        service.ReadPage = (request, _) => request.Filter?.FilePath is null
            ? pending.Task : Task.FromResult(HistoryPage([entries[^1]], request.Page));
        ScrollToHistoryEnd(history.HistoryListHandleForTest);
        await WaitUntilAsync(() => history.OperationRunningForTest);
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        pending.SetResult(HistoryPage(entries, 1));
        await WaitUntilAsync(() => history.EntryCount == 1 && !history.OperationRunningForTest);
        Assert.IsTrue(history.FileHistoryModeForTest);
        Assert.AreEqual(0, history.CurrentPageForTest);
        Assert.AreEqual("a.txt", service.PageRequests[^1].Filter!.FilePath);
        Assert.AreEqual(0, service.PageRequests[^1].Page);
        Assert.AreEqual(entries[^1].FullHash, history.SelectedCommitHashForTest);
    });

    [TestMethod]
    public Task 历史分页追加多轨图保持已有节点及分页前的可见连接() => RunAsync(async (window, history, service) =>
    {
        GitHistoryEntry[] entries = CreatePagedHistory(wide: true);
        service.Entries = entries;
        service.ReadPage = (request, _) => Task.FromResult(HistoryPage(entries, request.Page));
        window.RequestHistoryRefreshForTest();
        await WaitUntilAsync(() => history.EntryCount == 100 && !history.OperationRunningForTest);
        NativeCommitGraph before = history.CommitGraphForTest;
        ScrollToHistoryEnd(history.HistoryListHandleForTest);
        await WaitUntilAsync(() => history.EntryCount == entries.Length && !history.OperationRunningForTest);
        NativeCommitGraph after = history.CommitGraphForTest;
        Assert.AreEqual(12, before.ColumnCount);
        Assert.AreEqual(before.ColumnCount, after.ColumnCount);
        for (int index = 1; index < 99; index++)
        {
            Assert.AreEqual(before.Rows[index].Column, after.Rows[index].Column);
            Assert.AreEqual(before.Rows[index].Color, after.Rows[index].Color);
            // 原有主线保持；新页首次提供的其他父关系可以补入新的真实连线。
            foreach (NativeCommitGraphSegment segment in before.Rows[index].Segments.Where(segment => !segment.Dashed))
                Assert.Contains(segment, after.Rows[index].Segments);
        }
        Assert.IsFalse(after.Rows.SelectMany(row => row.Segments).Any(segment => segment.Dashed));
    });

    private static void ScrollToHistoryEnd(nint list) =>
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageVerticalScroll, 7, 0);

    [TestMethod]
    public Task 历史分页返回原上下文后晚到详情不能覆盖已恢复的文件列表() => RunAsync(async (window, history, service) =>
    {
        string? selected = history.SelectedCommitHashForTest;
        int fileCount = history.ChangedFileCountForTest;
        int previousRequests = history.CommitDetailsRequestCountForTest;
        GitHistoryEntry entry = CreatePagedHistory()[^1];
        service.ReadPage = (request, _) => Task.FromResult(HistoryPage([entry], request.Page));
        TaskCompletionSource<GitCommitDetailsResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.PendingDetails.Add(pending);
        service.ReadDetails = _ => pending.Task;
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => history.CommitDetailsRequestCountForTest > previousRequests && !history.OperationRunningForTest);
        nint clear = HistoryChild(history.Handle, 37);
        _ = NativeMethods.SendMessage(clear, 0x00F5, 0, 0);
        Assert.IsFalse(history.FileHistoryModeForTest);
        Assert.AreEqual(selected, history.SelectedCommitHashForTest);
        Assert.AreEqual(fileCount, history.ChangedFileCountForTest);
        pending.SetResult(GitCommitDetailsResult.Success(new(entry, "迟到的文件历史详情", [new(GitChangeKind.Added, "stale.txt", null)])));
        // 完成源使用异步继续；经过消息循环，确认回调确实有机会触达恢复后的界面。
        await Task.Delay(60);
        Assert.AreEqual(selected, history.SelectedCommitHashForTest);
        Assert.AreEqual(fileCount, history.ChangedFileCountForTest);
        Assert.IsFalse(history.FileTreeLabelsForTest.Any(label => label.Contains("stale.txt", StringComparison.Ordinal)));
    });

    [TestMethod]
    public Task 历史分页多轨窗口缩放只调整滚动范围不重查或重建图形() => RunAsync(async (window, history, service) =>
    {
        GitHistoryEntry[] entries = CreatePagedHistory(wide: true);
        service.Entries = entries;
        service.ReadPage = (request, _) => Task.FromResult(HistoryPage(entries, request.Page));
        window.RequestHistoryRefreshForTest();
        await WaitUntilAsync(() => history.EntryCount == 100 && !history.OperationRunningForTest);
        nint list = history.HistoryListHandleForTest;
        _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(1024), NativeTheme.Scale(760), true);
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageHorizontalScroll, 7, 0);
        Assert.IsGreaterThan(0, NativeMethods.GetScrollPosition(list, 0));
        int builds = history.CommitGraphBuildCountForTest, reads = service.PageReads;
        _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(1800), NativeTheme.Scale(900), true);
        Assert.AreEqual(0, NativeMethods.GetScrollPosition(list, 0));
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(list, NativeMethods.ListBoxGetHorizontalExtent, 0, 0));
        _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(1024), NativeTheme.Scale(760), true);
        Assert.IsGreaterThan((nint)0, NativeMethods.SendMessage(list, NativeMethods.ListBoxGetHorizontalExtent, 0, 0));
        Assert.AreEqual(builds, history.CommitGraphBuildCountForTest);
        Assert.AreEqual(reads, service.PageReads);
    });

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint HistoryChild(nint window, int identifier);

    [DllImport("user32.dll", EntryPoint = "PrintWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintHistoryWindow(nint window, nint dc, uint flags);

    [DllImport("gdi32.dll", EntryPoint = "GdiFlush")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushHistoryCapture();

    private void AssertHistoryTextVisible(nint list, int rowIndex, string theme)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(list, out NativeMethods.Rectangle client));
        NativeMethods.Rectangle row = default;
        _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxGetItemRectangle, unchecked((nuint)rowIndex), ref row);
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
        nint old = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            Assert.IsTrue(PrintHistoryWindow(list, dc, 3));
            Assert.IsTrue(FlushHistoryCapture());
            byte[] pixels = new byte[client.Right * client.Bottom * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(theme));
            int subjectPixels = 0, metadataPixels = 0;
            // 直接读取位图的客户区像素，不受绘图上下文残留的坐标变换影响。
            for (int y = Math.Max(0, row.Top); y < Math.Min(client.Bottom, row.Bottom); y++)
                for (int x = NativeTheme.Scale(8); x < client.Right - NativeTheme.Scale(8); x++)
                {
                    int offset = (y * client.Right + x) * 4;
                    uint pixel = (uint)(pixels[offset + 2] | (pixels[offset + 1] << 8) | (pixels[offset] << 16));
                    if (x < client.Right / 2 && NearTextColor(pixel, palette.Text)) subjectPixels++;
                    if (x >= client.Right / 2 && NearTextColor(pixel, palette.Muted)) metadataPixels++;
                }
            TestContext.WriteLine($"历史像素：{theme}，{client.Right}×{client.Bottom}，行 {rowIndex} ({row.Top},{row.Bottom})，标题={subjectPixels}，元数据={metadataPixels}。");
            SaveHistoryCapture(client.Right, client.Bottom, pixels);
            Assert.IsGreaterThan(15, subjectPixels, "滚到右侧后左半区域必须仍有提交标题。");
            Assert.IsGreaterThan(15, metadataPixels, "滚到右侧后右半区域必须仍有作者和日期。");
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, old);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }

    private void SaveHistoryCapture(int width, int height, byte[] pixels)
    {
        string path = Path.Combine(TestContext.TestResultsDirectory!, $"history-scroll-{Guid.NewGuid():N}.bmp");
        using (BinaryWriter writer = new(File.Create(path)))
        {
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
            writer.Write(new byte[16]);
            writer.Write(pixels);
        }
        TestContext.AddResultFile(path);
    }

    private static bool NearTextColor(uint pixel, uint color) =>
        Math.Abs((int)(pixel & 255) - (int)(color & 255)) <= 24
        && Math.Abs((int)((pixel >> 8) & 255) - (int)((color >> 8) & 255)) <= 24
        && Math.Abs((int)((pixel >> 16) & 255) - (int)((color >> 16) & 255)) <= 24;

    private static GitHistoryResult HistoryPage(GitHistoryEntry[] entries, int page) =>
        GitHistoryResult.Success(new(page, 100, page > 0, entries.Length > (page + 1) * 100, entries.Skip(page * 100).Take(100).ToArray()));

    private static GitHistoryEntry[] CreatePagedHistory(bool wide = false)
    {
        GitHistoryEntry Entry(string hash, params string[] parents) =>
            new("", hash, hash, parents, "作者", "author@example.invalid", DateTimeOffset.UnixEpoch, $"feat: {hash}", []);
        GitHistoryEntry[] linear = Enumerable.Range(0, 120)
            .Select(index => index == 119 ? Entry($"commit-{index}") : Entry($"commit-{index}", $"commit-{index + 1}")).ToArray();
        if (!wide) return linear;
        string[] branches = Enumerable.Range(0, 11).Select(index => $"branch-{index}").ToArray();
        return [Entry("merge", ["commit-0", .. branches]), .. linear[..119],
            .. branches.Select(hash => Entry(hash, "commit-119")), linear[^1]];
    }
}
