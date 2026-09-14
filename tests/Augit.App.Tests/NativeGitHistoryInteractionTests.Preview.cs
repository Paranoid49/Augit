using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    [TestMethod]
    [DataRow("Light", 96)]
    [DataRow("Dark", 144)]
    public async Task 文件历史完整比较连续导航切换与返回保留主窗口(string theme, int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            string? document = window.ActiveDocumentPathForTest;
            string? selected = history.SelectedCommitHashForTest;
            window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
            await WaitUntilAsync(() => service.Calls.Count == 1);
            NativeGitComparisonView view = history.FileHistoryComparisonForTest!;
            int layouts = window.LayoutInvocationCountForTest;
            CompletePreview(service.Calls[0]);
            await WaitUntilAsync(() => !view.IsBusy && view.ChangedLineCountForTest == 2);
            Assert.IsTrue(view.IsReadOnly);
            Assert.AreEqual("2 处差异", view.ChangeSummaryForTest);
            Assert.DoesNotContain("@@", view.BodyTextForTest);
            Assert.IsTrue(view.FileBarTextForTest.Contains("a.txt", StringComparison.Ordinal));
            Assert.AreEqual(history.HistoryListHandleForTest, NativeMethods.GetFocus());
            nint arrow = view.ToolbarButtonForTest(1);
            _ = NativeMethods.SetFocus(arrow);
            PostKey(arrow, NativeMethods.VirtualKeyEnter);
            await Task.Delay(30);
            nint first = NativeMethods.SendMessage(view.TextHandlesForTest[3], 2008, 0, 0);
            PostKey(arrow, NativeMethods.VirtualKeyEnter);
            await WaitUntilAsync(() => NativeMethods.SendMessage(view.TextHandlesForTest[3], 2008, 0, 0) > first);
            Assert.AreEqual(arrow, NativeMethods.GetFocus());
            view.ClickModeForTest(false);
            await WaitUntilAsync(() => !view.IsBusy && !view.UsesSideBySideForTest);
            Assert.AreEqual(2, view.ChangedLineCountForTest);
            Assert.Contains("旧一", view.BodyTextForTest);
            Assert.Contains("新一", view.BodyTextForTest);
            Assert.HasCount(1, service.Calls, "显示模式切换复用补丁，不重复查 Git。");
            foreach (int index in ComparisonToolbarOrder)
            {
                _ = NativeMethods.SetFocus(view.ToolbarButtonForTest(index));
                Assert.IsTrue(window.HandleTabNavigationForTest());
                Assert.IsTrue(history.ContainsWindow(NativeMethods.GetFocus()));
            }
            nint log = HistoryChild(history.Handle, 60);
            _ = NativeMethods.SendMessage(log, NativeMethods.WindowMessageLeftButtonDown, 1, (nint)(5 | (5 << 16)));
            _ = NativeMethods.SendMessage(log, NativeMethods.WindowMessageLeftButtonUp, 0, (nint)(5 | (5 << 16)));
            Assert.IsFalse(view.IsVisible);
            Assert.IsFalse(view.HasDocument);
            Assert.AreEqual(string.Empty, view.BodyTextForTest);
            Assert.AreEqual(selected, history.SelectedCommitHashForTest);
            Assert.AreEqual(document, window.ActiveDocumentPathForTest);
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        }, theme, preciseDpi: true);
    }

    [TestMethod]
    [DataRow("改选", false)]
    [DataRow("隐藏", true)]
    [DataRow("收起详情", false)]
    [DataRow("返回", true)]
    public Task 文件历史忽略空白旧查询取消后不能覆盖新上下文(string action, bool failure) => RunAsync(async (window, history, service) =>
    {
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => service.Calls.Count == 1);
        NativeGitComparisonView view = history.FileHistoryComparisonForTest!;
        CompletePreview(service.Calls[0]);
        await WaitUntilAsync(() => !view.IsBusy && view.ChangedLineCountForTest == 2);
        view.ClickIgnoreWhitespaceForTest();
        await WaitUntilAsync(() => service.Calls.Count == 2);
        ControlledHistory.Call old = service.Calls[1];
        Assert.IsTrue(old.IgnoreWhitespace);
        if (action == "改选") ClickRow(history.HistoryListHandleForTest, 1);
        else if (action == "隐藏") window.ToggleHistoryForTest();
        else if (action == "收起详情") history.ToggleDetailsForTest();
        else _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 37), 0x00F5, 0, 0);
        Assert.IsTrue(old.Token.IsCancellationRequested);
        string before = view.BodyTextForTest;
        old.Complete(failure);
        await Task.Delay(60);
        Assert.AreEqual(before, view.BodyTextForTest);
        if (action == "隐藏") window.ToggleHistoryForTest();
        if (action == "收起详情") history.ToggleDetailsForTest();
        if (action != "返回")
        {
            await WaitUntilAsync(() => service.Calls.Count == 3);
            CompletePreview(service.Calls[2]);
            await WaitUntilAsync(() => !view.IsBusy && view.ChangedLineCountForTest == 2);
            Assert.IsTrue(view.IsVisible);
        }
    });

    [TestMethod]
    public Task 文件历史退出取消尚未完成的排版() => RunAsync(async (window, history, service) =>
    {
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => service.Calls.Count == 1);
        NativeGitComparisonView view = history.FileHistoryComparisonForTest!;
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        view.RenderBarrierForTest = barrier.Task;
        CompletePreview(service.Calls[0]);
        await WaitUntilAsync(() => view.RenderCountForTest == 1);
        _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 37), 0x00F5, 0, 0);
        barrier.SetResult();
        await Task.Delay(60);
        Assert.IsFalse(view.HasDocument);
        Assert.IsFalse(view.IsVisible);
        Assert.AreEqual(string.Empty, view.BodyTextForTest);
        Assert.AreEqual(0, view.ChangedLineCountForTest);
    });

    private static void CompletePreview(ControlledHistory.Call call)
    {
        string patch = "@@ -1,32 +1,32 @@\n-旧一\n+新一\n"
            + string.Join('\n', Enumerable.Range(2, 30).Select(index => $" 上下文 {index}"))
            + "\n-旧二\n+新二\n";
        call.Completion.SetResult(GitComparisonResult.Success(new(
            GitDiffContentStatus.Ready, call.Base, call.Target, call.Path, patch)));
    }
}
