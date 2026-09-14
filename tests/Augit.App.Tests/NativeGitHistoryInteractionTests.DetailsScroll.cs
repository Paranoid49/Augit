using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    [TestMethod]
    [DataRow(false, 8, true)]
    [DataRow(true, 8, true)]
    [DataRow(false, 9, true)]
    [DataRow(false, 0, false)]
    [DataRow(false, 6, false)]
    [DataRow(false, -1, false)]
    public async Task 详情排版中的末尾请求只由后续导航改变(bool keyboardEnd, int followingCommand, bool followEnd)
    {
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride(96);
        await RunAsync(async (window, history, service) =>
        {
            await PrepareScrollableDetailsAsync(window, history, service);
            nint details = HistoryChild(history.Handle, 61);
            string? selected = history.SelectedCommitHashForTest;
            int reads = history.CommitDetailsRequestCountForTest;
            TaskCompletionSource resume = PauseDetailsReflow(window, history);
            Task pending = history.DetailsLayoutTaskForTest;
            try
            {
                _ = NativeMethods.SetFocus(details);
                _ = NativeMethods.SendMessage(details,
                    keyboardEnd ? NativeMethods.WindowMessageKeyDown : NativeMethods.WindowMessageVerticalScroll,
                    keyboardEnd ? 0x23u : 7u, 0);
                int oldEnd = history.DetailsScrollPositionForTest;
                Assert.IsGreaterThan(0, oldEnd, "排版暂停时仍应保留此前正文和滚动范围。");
                if (followingCommand == -1)
                    _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageMouseWheel, 120u << 16, 0);
                else
                    _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageVerticalScroll, (nuint)followingCommand, 0);
                int requestedPosition = history.DetailsScrollPositionForTest;
                if (followEnd) Assert.AreEqual(oldEnd, requestedPosition, "非导航通知不能立即移动详情。");
                else Assert.IsLessThan(oldEnd, requestedPosition, "真实导航必须能覆盖先前的末尾请求。");
                int layouts = history.DetailsLayoutCountForTest;
                resume.SetResult();
                await pending.WaitAsync(TimeSpan.FromSeconds(3));
                await WaitUntilAsync(() => !history.DetailsLayoutPendingForTest);
                DetailsTestScrollInfo scroll = ReadDetailsScrollInfo(details);
                int end = Math.Max(0, scroll.Maximum - (int)scroll.Page + 1);
                Assert.IsGreaterThan(oldEnd, end, "增大正文字号后，真实滚动范围必须扩大。");
                Assert.AreEqual(followEnd ? end : requestedPosition, history.DetailsScrollPositionForTest,
                    "完成排版后必须执行最后一次有效导航，不能把结束通知当作新导航。");
                Assert.AreEqual(history.DetailsScrollPositionForTest, scroll.Position);
                Assert.AreEqual(details, NativeMethods.GetFocus());
                Assert.AreEqual(selected, history.SelectedCommitHashForTest);
                Assert.AreEqual(reads, history.CommitDetailsRequestCountForTest);
                Assert.AreEqual(layouts, history.DetailsLayoutCountForTest, "滚动通知不应重新排版正文。");
                if (followEnd) AssertDetailsPixels(history, details, "Light");
            }
            finally
            {
                resume.TrySetResult();
                await pending.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }, "Light", preciseDpi: true);
    }

    [TestMethod]
    public async Task 详情滚动结束通知不丢失高精度滚轮余量()
    {
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride(96);
        await RunAsync(async (window, history, service) =>
        {
            await PrepareScrollableDetailsAsync(window, history, service);
            nint details = HistoryChild(history.Handle, 61);
            _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x24, 0);
            int pixelsPerNotch = NativeTheme.UiLineHeight * 3;
            Assert.IsLessThan(120, pixelsPerNotch, "该场景需要单次小增量不足一个像素。");
            for (int index = 0; index < 120; index++)
            {
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageMouseWheel,
                    unchecked((nuint)((uint)(ushort)-1 << 16)), 0);
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageVerticalScroll, 8, 0);
            }
            Assert.AreEqual(pixelsPerNotch, history.DetailsScrollPositionForTest,
                "结束通知不能清零滚轮余量，120 次微小增量应等于一个完整刻度。");
        }, preciseDpi: true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task 详情末尾请求在隐藏后恢复但切换提交后失效(bool changeCommit)
    {
        await RunAsync(async (window, history, service) =>
        {
            await PrepareScrollableDetailsAsync(window, history, service);
            nint details = HistoryChild(history.Handle, 61);
            TaskCompletionSource resume = PauseDetailsReflow(window, history);
            Task oldLayout = history.DetailsLayoutTaskForTest;
            try
            {
                _ = NativeMethods.SendMessage(details, NativeMethods.WindowMessageKeyDown, 0x23, 0);
                if (changeCommit)
                {
                    ClickRow(history.HistoryListHandleForTest, 1);
                    await WaitUntilAsync(() => history.DetailsBodyForTest == "新的提交说明。");
                    Assert.AreEqual(0, history.DetailsScrollPositionForTest);
                }
                else
                {
                    Assert.IsTrue(history.ToggleDetailsForTest());
                    Assert.IsFalse(NativeMethods.IsWindowVisible(details));
                    Assert.IsTrue(history.ToggleDetailsForTest());
                }
                resume.SetResult();
                await oldLayout.WaitAsync(TimeSpan.FromSeconds(3));
                await WaitUntilAsync(() => !history.DetailsLayoutPendingForTest);
                DetailsTestScrollInfo scroll = ReadDetailsScrollInfo(details);
                int end = Math.Max(0, scroll.Maximum - (int)scroll.Page + 1);
                Assert.AreEqual(changeCommit ? 0 : end, history.DetailsScrollPositionForTest);
                Assert.IsNull(history.DetailsLayoutErrorForTest);
                if (changeCommit) Assert.AreEqual("新的提交说明。", history.DetailsBodyForTest);
                else Assert.IsGreaterThan(0, end);
            }
            finally
            {
                resume.TrySetResult();
                await oldLayout.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }, preciseDpi: true);
    }

    [TestMethod]
    public async Task 主窗口关闭时待排版的详情末尾请求不重新登记控件()
    {
        int registrations = NativeGitHistoryPanel.DetailsRegistrationCountForTest;
        Task pending = Task.CompletedTask;
        NativeGitHistoryPanel? closedHistory = null;
        await RunAsync(async (window, history, service) =>
        {
            await PrepareScrollableDetailsAsync(window, history, service);
            TaskCompletionSource resume = PauseDetailsReflow(window, history);
            pending = history.DetailsLayoutTaskForTest;
            try
            {
                closedHistory = history;
                _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 61), NativeMethods.WindowMessageKeyDown, 0x23, 0);
                window.Close();
                Assert.IsTrue(window.DestroyedForTest);
                Assert.AreEqual(registrations, NativeGitHistoryPanel.DetailsRegistrationCountForTest);
            }
            finally { resume.TrySetResult(); }
        }, preciseDpi: true);
        await pending.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(registrations, NativeGitHistoryPanel.DetailsRegistrationCountForTest);
        Assert.AreEqual(string.Empty, closedHistory!.DetailsBodyForTest);
    }

    private static async Task PrepareScrollableDetailsAsync(MainWindow window, NativeGitHistoryPanel history, ControlledHistory service)
    {
        var entries = CreatePagedHistory()[..2];
        string body = string.Join('\n', Enumerable.Range(0, 100).Select(index => $"第 {index:D3} 段：详情排版中仍可操作滚动条。"))
            + "\n最后一段必须保持可见。";
        service.Entries = entries;
        service.ReadDetails = hash => Task.FromResult(GitCommitDetailsResult.Success(new(
            entries.First(entry => entry.FullHash == hash), hash == entries[0].FullHash ? body : "新的提交说明。",
            [new(GitChangeKind.Modified, "a.txt", null)])));
        window.RequestHistoryRefreshForTest();
        await WaitUntilAsync(() => !history.OperationRunningForTest && history.EntryCount == 2);
        ClickRow(history.HistoryListHandleForTest, 0);
        await WaitUntilAsync(() => history.DetailsBodyForTest == body);
        window.ResizeBottomPanelForTest(NativeTheme.Scale(550));
        await WaitUntilAsync(() => !history.DetailsLayoutPendingForTest);
        await history.DetailsLayoutTaskForTest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNull(history.DetailsLayoutErrorForTest);
    }

    private static TaskCompletionSource PauseDetailsReflow(MainWindow window, NativeGitHistoryPanel history)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // 只暂停测试中的前置任务，稳定模拟旧内容可读、新排版未完成；不依赖机器速度或大文件延迟。
        typeof(NativeGitHistoryPanel).GetField("_detailsLayoutTask", flags)!.SetValue(history, resume.Task);
        try
        {
            ApplicationSettings original = (ApplicationSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
            typeof(MainWindow).GetMethod("ApplyConfirmedSettings", flags)!.Invoke(window,
                [original, original with { FontSize = 19 }]);
            Assert.IsTrue(history.DetailsLayoutPendingForTest);
            return resume;
        }
        catch
        {
            resume.TrySetResult();
            throw;
        }
    }

    private static DetailsTestScrollInfo ReadDetailsScrollInfo(nint details)
    {
        DetailsTestScrollInfo info = new() { Size = (uint)Marshal.SizeOf<DetailsTestScrollInfo>(), Mask = 7 };
        Assert.IsTrue(GetDetailsTestScrollInfo(details, 1, ref info));
        return info;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DetailsTestScrollInfo
    {
        internal uint Size, Mask;
        internal int Minimum, Maximum;
        internal uint Page;
        internal int Position, TrackPosition;
    }

    [DllImport("user32.dll", EntryPoint = "GetScrollInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDetailsTestScrollInfo(nint window, int bar, ref DetailsTestScrollInfo info);
}
