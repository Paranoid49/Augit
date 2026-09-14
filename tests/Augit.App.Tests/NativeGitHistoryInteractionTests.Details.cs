using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    [TestMethod]
    public async Task 超长详情排版期间切换提交不阻塞输入且旧结果不能覆盖新选择()
    {
        await RunAsync(async (window, history, service) =>
        {
            var entries = CreatePagedHistory()[..2];
            string large = new('a', 20 * 1024 * 1024);
            service.Entries = entries;
            service.ReadDetails = hash => Task.FromResult(GitCommitDetailsResult.Success(new(
                entries.First(entry => entry.FullHash == hash), hash == entries[0].FullHash ? large : "最新提交的说明。",
                [new(GitChangeKind.Modified, "a.txt", null)])));
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => !history.OperationRunningForTest && history.EntryCount == 2);
            ClickRow(history.HistoryListHandleForTest, 0);
            await WaitUntilAsync(() => history.DetailsBodyForTest == large);
            Assert.IsTrue(history.DetailsLayoutPendingForTest);
            Task oldLayout = history.DetailsLayoutTaskForTest;
            bool messageHandled = false;
            Stopwatch watch = Stopwatch.StartNew();
            window.Post(() => messageHandled = true);
            await WaitUntilAsync(() => messageHandled);
            TestContext.WriteLine($"20 MB 详情排版时界面消息响应：{watch.Elapsed.TotalMilliseconds:F2}ms。");
            Assert.IsTrue(history.DetailsLayoutPendingForTest, "消息必须在全文排版结束前得到处理。");
            ClickRow(history.HistoryListHandleForTest, 1);
            await WaitUntilAsync(() => history.DetailsBodyForTest == "最新提交的说明。" && !history.DetailsLayoutPendingForTest);
            await oldLayout.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(entries[1].FullHash, history.SelectedCommitHashForTest);
            Assert.AreEqual("最新提交的说明。", history.DetailsBodyForTest);
            Assert.IsNull(history.DetailsLayoutErrorForTest);
            Assert.AreEqual(0, history.DetailsScrollPositionForTest);
            ClickRow(history.HistoryListHandleForTest, 0);
            await WaitUntilAsync(() => history.DetailsBodyForTest == large);
            Stopwatch layoutWait = Stopwatch.StartNew();
            while (history.DetailsLayoutPendingForTest)
            {
                Assert.IsLessThan(15000, layoutWait.ElapsedMilliseconds, "完整排版未结束。");
                await history.DetailsLayoutTaskForTest.WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(10);
            }
            Assert.IsNull(history.DetailsLayoutErrorForTest);
            nint details = HistoryChild(history.Handle, 61);
            _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x23, 0);
            Assert.IsGreaterThan(100000, history.DetailsScrollPositionForTest);
            int layouts = history.DetailsLayoutCountForTest;
            watch.Restart();
            for (int index = 0; index < 30; index++)
            {
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x21, 0);
                _ = NativeMethods.UpdateWindow(details);
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x22, 0);
                _ = NativeMethods.UpdateWindow(details);
                Assert.IsGreaterThan(0, history.DetailsLastDrawnCharactersForTest);
                Assert.IsLessThan(16384, history.DetailsLastDrawnCharactersForTest, "滚动不得把整个 20 MB 段落传给绘制函数。");
            }
            TestContext.WriteLine($"20 MB 详情末尾连续 60 帧滚动：{watch.Elapsed.TotalMilliseconds:F2}ms。");
            Assert.AreEqual(layouts, history.DetailsLayoutCountForTest);
            ClickRow(history.HistoryListHandleForTest, 1);
            await WaitUntilAsync(() => history.DetailsBodyForTest == "最新提交的说明。" && !history.DetailsLayoutPendingForTest);
            ClickRow(history.HistoryListHandleForTest, 0);
            await WaitUntilAsync(() => history.DetailsBodyForTest == large);
            Task closingLayout = history.DetailsLayoutTaskForTest;
            history.Dispose();
            await closingLayout.WaitAsync(TimeSpan.FromSeconds(3));
        }, preciseDpi: true);
    }

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 提交详情长正文可滚到末尾且刷新收放字号不破坏上下文(int dpi, string theme)
    {
        int registrations = NativeGitHistoryPanel.DetailsRegistrationCountForTest;
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            string body = string.Join('\n', Enumerable.Range(0, 200)
                .Select(index => $"第 {index:D3} 段：这是一段完整提交说明，用于检查窄栏自然换行。")) + "\n最后一段必须可见。";
            var entries = CreatePagedHistory()[..2];
            service.Entries = entries;
            service.ReadDetails = hash => Task.FromResult(GitCommitDetailsResult.Success(new(
                entries.First(entry => entry.FullHash == hash), body, [new(GitChangeKind.Modified, "a.txt", null)])));
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => !history.OperationRunningForTest && history.EntryCount == 2);
            ClickRow(history.HistoryListHandleForTest, 0);
            await WaitUntilAsync(() => history.CommitDetailsLoadedForTest && history.DetailsBodyForTest == body);
            window.ResizeBottomPanelForTest(NativeTheme.Scale(550));
            await WaitUntilAsync(() => !history.DetailsLayoutPendingForTest);
            Assert.IsNull(history.DetailsLayoutErrorForTest);
            nint details = HistoryChild(history.Handle, 61);
            string? selected = history.SelectedCommitHashForTest;
            int requests = history.CommitDetailsRequestCountForTest;
            int builds = history.CommitGraphBuildCountForTest;
            Assert.AreEqual(body, history.DetailsBodyForTest, "不能在 1200 字符处截断正文。");
            _ = NativeMethods.SetFocus(details);
            _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x23, 0);
            Assert.IsGreaterThan(NativeTheme.Scale(1200), history.DetailsScrollPositionForTest);
            AssertDetailsPixels(history, details, theme);
            int end = history.DetailsScrollPositionForTest;
            int layouts = history.DetailsLayoutCountForTest;
            Stopwatch watch = Stopwatch.StartNew();
            for (int index = 0; index < 30; index++)
            {
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x21, 0);
                _ = NativeMethods.UpdateWindow(details);
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x22, 0);
                _ = NativeMethods.UpdateWindow(details);
            }
            TestContext.WriteLine($"详情 60 帧滚动绘制：{watch.Elapsed.TotalMilliseconds:F2}ms。");
            Assert.AreEqual(layouts, history.DetailsLayoutCountForTest, "滚动绘制不能重复排版全文。");
            Assert.AreEqual(end, history.DetailsScrollPositionForTest);
            Assert.AreEqual(details, NativeMethods.GetFocus());
            Assert.IsTrue(history.HandleTabNavigation(backwards: false));
            Assert.AreNotEqual(details, NativeMethods.GetFocus(), "详情不能困住 Tab 焦点。");
            history.ToggleDetailsForTest();
            _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageMouseWheel, (nuint)(120 << 16), 0);
            history.ToggleDetailsForTest();
            Assert.AreEqual(end, history.DetailsScrollPositionForTest);
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => !history.OperationRunningForTest);
            Assert.AreEqual(end, history.DetailsScrollPositionForTest);
            Assert.AreEqual(requests, history.CommitDetailsRequestCountForTest);
            Assert.AreEqual(builds, history.CommitGraphBuildCountForTest);

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo settings = typeof(MainWindow).GetField("_settings", flags)!;
            ApplicationSettings original = (ApplicationSettings)settings.GetValue(window)!;
            try
            {
                typeof(MainWindow).GetMethod("ApplyConfirmedSettings", flags)!.Invoke(window,
                    [original, original with { TextFontSize = 40 }]);
                Assert.AreEqual(details, HistoryChild(history.Handle, 61));
                Assert.AreEqual(selected, history.SelectedCommitHashForTest);
                await WaitUntilAsync(() => !history.DetailsLayoutPendingForTest);
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x23, 0);
                AssertDetailsPixels(history, details, theme);
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x24, 0);
                Assert.AreEqual(0, history.DetailsScrollPositionForTest);
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageMouseWheel,
                    unchecked((nuint)((uint)(ushort)-30 << 16)), 0);
                Assert.IsGreaterThan(0, history.DetailsScrollPositionForTest, "高精度滚轮的小增量必须可用。");
            }
            finally
            {
                settings.SetValue(window, original);
                typeof(MainWindow).GetMethod("ApplyAppearance", flags)!.Invoke(window, null);
            }
            service.ReadDetails = hash => Task.FromResult(GitCommitDetailsResult.Failure(GitOperationFailureKind.CommandFailed, "详情读取失败"));
            ClickRow(history.HistoryListHandleForTest, 1);
            await WaitUntilAsync(() => NativeMethods.GetWindowTextValue(details) == "详情读取失败");
            Assert.AreEqual(0, history.DetailsScrollPositionForTest);
            Assert.AreEqual(string.Empty, history.DetailsBodyForTest);
            Assert.AreEqual(0u, unchecked((uint)NativeMethods.GetWindowLongPointer(details, NativeMethods.WindowLongStyle))
                & NativeMethods.WindowStyleVerticalScroll);
        }, theme, preciseDpi: true);
        Assert.AreEqual(registrations, NativeGitHistoryPanel.DetailsRegistrationCountForTest);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public async Task 提交详情文字动作放不下时可从文件菜单进入并返回原滚动位置(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            Assert.AreEqual(string.Empty, history.DetailsBodyForTest, "真实 Git 的 %B 标题不应在正文重复。");
            string body = string.Join('\n', Enumerable.Repeat("文件历史返回后仍停留在这里。", 100));
            service.ReadDetails = hash => Task.FromResult(GitCommitDetailsResult.Success(new(
                new("", hash, hash[..7], [], "作者", "author@example.invalid", DateTimeOffset.UnixEpoch,
                    "test: 详情返回", []), $"test: 详情返回\n\n{body}", [new(GitChangeKind.Modified, "a.txt", null)])));
            ClickRow(history.HistoryListHandleForTest, 1);
            await WaitUntilAsync(() => history.CommitDetailsLoadedForTest && history.DetailsBodyForTest == body);
            _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(1900), NativeTheme.Scale(1000), true);
            window.ResizeBottomPanelForTest(NativeTheme.Scale(500));
            window.HideProjectForTest();
            nint historyButton = HistoryChild(history.Handle, 19), blameButton = HistoryChild(history.Handle, 20);
            Assert.IsTrue(NativeMethods.IsWindowVisible(historyButton),
                $"窗口={HistoryControlBounds(window.Handle).Right - HistoryControlBounds(window.Handle).Left}，详情={HistoryControlBounds(HistoryChild(history.Handle, 61)).Right - HistoryControlBounds(HistoryChild(history.Handle, 61)).Left}，DPI={NativeTheme.ActiveDpiForTest}，字号={NativeTheme.UiFontSizeForTest}。");
            var historyBounds = HistoryControlBounds(historyButton);
            var blameBounds = HistoryControlBounds(blameButton);
            Assert.IsGreaterThanOrEqualTo(MeasureHistoryText(UiText.FileHistory, NativeTheme.UiFont).Width + NativeTheme.Scale(12),
                historyBounds.Right - historyBounds.Left);
            Assert.IsLessThanOrEqualTo(blameBounds.Left, historyBounds.Right);
            await history.SelectCommitFileForTestAsync(0);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo settings = typeof(MainWindow).GetField("_settings", flags)!;
            ApplicationSettings original = (ApplicationSettings)settings.GetValue(window)!;
            try
            {
                _ = NativeMethods.SetFocus(historyButton);
                typeof(MainWindow).GetMethod("ApplyConfirmedSettings", flags)!.Invoke(window,
                    [original, original with { TextFontSize = 40 }]);
                Assert.IsFalse(NativeMethods.IsWindowVisible(historyButton));
                Assert.IsFalse(NativeMethods.IsWindowVisible(blameButton));
                await WaitUntilAsync(() => !history.DetailsLayoutPendingForTest);
                nint files = HistoryChild(history.Handle, 2), details = HistoryChild(history.Handle, 61);
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x23, 0);
                int position = history.DetailsScrollPositionForTest;
                Assert.IsGreaterThan(0, position);
                string? selected = history.SelectedCommitHashForTest;
                _ = NativeMethods.SendMessage(files, NativeMethods.WindowMessageContextMenu, (nuint)files, -1);
                NativeContextMenu menu = history.ContextMenuForTest!;
                CollectionAssert.AreEqual(new[] { UiText.ShowDiff, UiText.FileHistory, UiText.Blame },
                    menu.VisualItemsForTest.Select(item => item.Label).ToArray());
                Assert.HasCount(0, service.Calls, "打开菜单不得打开 Diff。");
                service.CompleteDiffImmediately = true;
                _ = NativeMethods.SendMessage(menu.Handle, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyDown, 0);
                _ = NativeMethods.SendMessage(menu.Handle, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyDown, 0);
                _ = NativeMethods.SendMessage(menu.Handle, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
                await WaitUntilAsync(() => history.FileHistoryModeForTest && !history.OperationRunningForTest);
                history.ReturnFromHistoryForTest();
                Assert.IsFalse(history.FileHistoryModeForTest);
                await WaitUntilAsync(() => !history.DetailsLayoutPendingForTest);
                Assert.AreEqual(selected, history.SelectedCommitHashForTest);
                Assert.AreEqual(position, history.DetailsScrollPositionForTest);
            }
            finally
            {
                settings.SetValue(window, original);
                typeof(MainWindow).GetMethod("ApplyAppearance", flags)!.Invoke(window, null);
            }
        }, preciseDpi: true);
    }

    private void AssertDetailsPixels(NativeGitHistoryPanel history, nint details, string theme)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(details, out var client));
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
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out nint bits, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            Assert.IsTrue(PrintHistoryWindow(details, dc, 3));
            Assert.IsTrue(FlushHistoryCapture());
            byte[] pixels = new byte[client.Right * client.Bottom * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            SaveHistoryCapture(client.Right, client.Bottom, pixels);
            // 独立整段排版作为可见正文模板，校验分块后的断行与末尾文字，而不只检查滚动值。
            NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(theme));
            nint brush = NativeMethods.CreateSolidBrush(palette.Panel);
            _ = NativeMethods.FillRectangle(dc, ref client, brush);
            _ = NativeMethods.DeleteObject(brush);
            nint font = (nint)typeof(NativeGitHistoryPanel).GetField("_detailsBodyFont", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(history)!;
            nint oldFont = NativeMethods.SelectObject(dc, font);
            try
            {
                string body = history.DetailsBodyForTest;
                GCHandle pinned = GCHandle.Alloc(body, GCHandleType.Pinned);
                try
                {
                    using NativeGdiPlusDrawing.WrappedTextSession text = new(dc);
                    var full = text.Measure(pinned.AddrOfPinnedObject(), 0, body.Length,
                        client.Right - NativeTheme.Scale(20), 0, 10000);
                    Assert.AreEqual(body.Length, full.Length);
                    text.Draw(pinned.AddrOfPinnedObject(), full, NativeTheme.Scale(10),
                        client.Bottom - NativeTheme.Scale(8) - MathF.Ceiling(full.Height), palette.Text);
                }
                finally { pinned.Free(); }
                Assert.IsTrue(FlushHistoryCapture());
                byte[] expected = new byte[pixels.Length];
                Marshal.Copy(bits, expected, 0, expected.Length);
                int ink = 0, matched = 0;
                for (int offset = 0; offset < expected.Length; offset += 4)
                {
                    uint color = (uint)(expected[offset + 2] | expected[offset + 1] << 8 | expected[offset] << 16);
                    if (NearTextColor(color, palette.Panel)) continue;
                    ink++;
                    uint actual = (uint)(pixels[offset + 2] | pixels[offset + 1] << 8 | pixels[offset] << 16);
                    if (NearTextColor(actual, color)) matched++;
                }
                Assert.IsGreaterThan(20, ink);
                Assert.IsGreaterThanOrEqualTo(ink * 0.95, matched, $"末段模板匹配 {matched}/{ink}，不能只验证滚动位置。");
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

    [DllImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext")]
    private static extern nint SetDetailsThreadDpiContext(nint context);
}
