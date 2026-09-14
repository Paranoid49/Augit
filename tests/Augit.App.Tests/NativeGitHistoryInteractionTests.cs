using System.Diagnostics;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeGitHistoryInteractionTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly int[] ComparisonToolbarOrder = [0, 1, 2, 5, 4, 3, 6];
    private static readonly int[] ComparisonUnavailableToolbarOrder = [5, 4, 3, 6];

    [TestMethod]
    [DataRow(96)]
    [DataRow(144)]
    public async Task 多轨历史横向滚动和重复选择不查询或重建提交图(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            string[] branches = Enumerable.Range(0, 12).Select(index => $"branch-{index}").ToArray();
            GitHistoryEntry Entry(string hash, params string[] parents) =>
                new("", hash, hash, parents, "作者", "author@example.invalid", DateTimeOffset.UnixEpoch, $"feat: {hash}", []);
            service.Entries = [Entry("merge", branches), .. branches.Select(hash => Entry(hash, "root")), Entry("root")];
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => history.EntryCount == 14 && !history.OperationRunningForTest);
            nint list = history.HistoryListHandleForTest;
            Assert.IsTrue(NativeMethods.SetWindowPosition(list, 0, 0, 0, NativeTheme.Scale(250), NativeTheme.Scale(135),
                NativeMethods.SetWindowPositionNoMove | NativeMethods.SetWindowPositionNoZOrder | NativeMethods.SetWindowPositionNoActivate));
            Assert.AreEqual(12, history.CommitGraphForTest.ColumnCount);
            nint extent = NativeMethods.SendMessage(list, NativeMethods.ListBoxGetHorizontalExtent, 0, 0);
            Assert.IsGreaterThan((nint)NativeTheme.Scale(400), extent);
            int builds = history.CommitGraphBuildCountForTest, reads = service.PageReads;
            int layouts = window.LayoutInvocationCountForTest, resets = history.HistoryListResetCountForTest;
            _ = NativeMethods.SendMessage(list, 0x0114, 7, 0);
            int offset = NativeMethods.GetScrollPosition(list, 0);
            Assert.IsGreaterThan(0, offset, "拖动横向滚动条必须能到达被图形挤出的文字列。");
            ClickRow(list, 1);
            await WaitUntilAsync(() => history.CommitDetailsLoadedForTest);
            for (int index = 0; index < 10; index++)
            {
                ClickRow(list, 1);
                _ = NativeMethods.InvalidateRectangle(list, 0, true);
                _ = NativeMethods.UpdateWindow(list);
            }
            Assert.AreEqual(offset, NativeMethods.GetScrollPosition(list, 0));
            Assert.AreEqual(builds, history.CommitGraphBuildCountForTest);
            Assert.AreEqual(reads, service.PageReads);
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.AreEqual(resets, history.HistoryListResetCountForTest);
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => service.PageReads > reads && !history.OperationRunningForTest);
            Assert.AreEqual(builds, history.CommitGraphBuildCountForTest, "相同快照不能重算图形。");
            Assert.AreEqual(offset, NativeMethods.GetScrollPosition(list, 0));
            service.Entries = [Entry("merge", "root"), Entry("root")];
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => history.EntryCount == 2 && !history.OperationRunningForTest);
            Assert.AreEqual(1, history.CommitGraphForTest.ColumnCount);
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(list, NativeMethods.ListBoxGetHorizontalExtent, 0, 0));
            Assert.AreEqual(0, NativeMethods.GetScrollPosition(list, 0), "收敛为单轨后不能遗留越界横向偏移。");
        });
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task 比较差异箭头可连续键盘操作且不抢焦点或覆盖全局状态(bool sideBySide, bool useEnter) => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        ControlledHistory.Call call = service.Calls[0];
        string patch = "@@ -1,32 +1,32 @@\n-旧一\n+新一\n"
            + string.Join('\n', Enumerable.Range(2, 30).Select(index => $" 上下文 {index}"))
            + "\n-旧二\n+新二\n";
        call.Completion.SetResult(GitComparisonResult.Success(new(
            GitDiffContentStatus.Ready, call.Base, call.Target, call.Path, patch)));
        await pending;
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        view.ClickModeForTest(sideBySide);
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual(2, view.ChangedLineCountForTest);
        window.SetStatusForTest(UiText.PathCopied);
        int layouts = window.LayoutInvocationCountForTest;
        int resets = history.FileListResetCountForTest;
        nint arrow = view.ToolbarButtonForTest(1);
        nint body = view.TextHandlesForTest[sideBySide ? 3 : 0];
        _ = NativeMethods.SetFocus(arrow);
        int key = useEnter ? NativeMethods.VirtualKeyEnter : NativeMethods.VirtualKeySpace;
        PostKey(arrow, key);
        await Task.Delay(40);
        Assert.AreEqual(arrow, NativeMethods.GetFocus(), "导航后应继续接受同一按钮上的键盘操作。");
        nint firstPosition = NativeMethods.SendMessage(body, 2008, 0, 0);
        PostKey(arrow, key);
        await WaitUntilAsync(() => NativeMethods.SendMessage(body, 2008, 0, 0) > firstPosition);
        Assert.AreEqual(arrow, NativeMethods.GetFocus());
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(resets, history.FileListResetCountForTest);
        Assert.HasCount(1, service.Calls);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 比较标签的Tab按工具栏视觉顺序进入正文再返回标签(bool sideBySide) => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        service.Calls[0].Complete();
        await pending;
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        view.ClickModeForTest(sideBySide);
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsTrue(window.FocusDocumentTabsForTest());
        foreach (int index in ComparisonToolbarOrder)
        {
            nint previous = NativeMethods.GetFocus();
            PostKey(previous, NativeMethods.VirtualKeyTab);
            await WaitUntilAsync(() => NativeMethods.GetFocus() != previous);
            Assert.AreEqual(view.ToolbarButtonForTest(index), NativeMethods.GetFocus());
        }
        nint[] bodies = sideBySide ? [view.TextHandlesForTest[1], view.TextHandlesForTest[3]] : [view.TextHandlesForTest[0]];
        foreach (nint body in bodies)
        {
            Assert.IsTrue(window.HandleTabNavigationForTest());
            Assert.AreEqual(body, NativeMethods.GetFocus(), "行号栏和隐藏正文不能进入 Tab 顺序。");
        }
        Assert.IsTrue(window.HandleTabNavigationForTest());
        Assert.IsTrue(window.ActiveDocumentTabsHasFocusForTest);
        foreach (nint body in bodies.Reverse())
        {
            Assert.IsTrue(window.HandleTabNavigationForTest(backwards: true));
            Assert.AreEqual(body, NativeMethods.GetFocus());
        }
        Assert.IsTrue(window.HandleTabNavigationForTest(backwards: true));
        Assert.AreEqual(view.ToolbarButtonForTest(6), NativeMethods.GetFocus());
    });

    [TestMethod]
    public Task 比较首次加载和失败时Tab跳过禁用按钮且可返回标签() => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        for (int state = 0; state < 2; state++)
        {
            Assert.IsTrue(window.FocusDocumentTabsForTest());
            foreach (int index in ComparisonUnavailableToolbarOrder)
            {
                Assert.IsTrue(window.HandleTabNavigationForTest());
                Assert.AreEqual(view.ToolbarButtonForTest(index), NativeMethods.GetFocus());
            }
            Assert.IsTrue(window.HandleTabNavigationForTest());
            Assert.AreEqual(view.NoticeHandleForTest, NativeMethods.GetFocus());
            Assert.IsTrue(window.HandleTabNavigationForTest());
            Assert.IsTrue(window.ActiveDocumentTabsHasFocusForTest);
            if (state == 0)
            {
                service.Calls[0].Complete(failure: true);
                await pending;
            }
        }
    });

    private static void PostKey(nint target, int key)
    {
        _ = NativeMethods.PostMessage(target, NativeMethods.WindowMessageKeyDown, unchecked((nuint)key), 0);
        _ = NativeMethods.PostMessage(target, 0x0101, unchecked((nuint)key), 0);
    }

    [TestMethod]
    public Task 比较模式按钮的Enter和空格复用操作且异步完成不抢历史焦点() => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        service.Calls[0].Complete();
        await pending;
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        await WaitUntilAsync(() => !view.LoadingForTest);
        TaskCompletionSource render = new(TaskCreationOptions.RunContinuationsAsynchronously);
        view.RenderBarrierForTest = render.Task;
        nint unified = view.ToolbarButtonForTest(3);
        _ = NativeMethods.SetFocus(unified);
        PostKey(unified, NativeMethods.VirtualKeyEnter);
        await WaitUntilAsync(() => !view.UsesSideBySideForTest && view.RenderBarrierForTest is null);
        _ = NativeMethods.SetFocus(history.FilesListHandleForTest);
        render.SetResult();
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual(history.FilesListHandleForTest, NativeMethods.GetFocus());
        nint sideBySide = view.ToolbarButtonForTest(4);
        _ = NativeMethods.SetFocus(sideBySide);
        PostKey(sideBySide, NativeMethods.VirtualKeySpace);
        await WaitUntilAsync(() => view.UsesSideBySideForTest && !view.LoadingForTest);
        Assert.AreEqual(sideBySide, NativeMethods.GetFocus());
        Assert.HasCount(1, service.Calls);
        Assert.AreEqual("1 处差异", view.ChangeSummaryForTest);
    });

    [TestMethod]
    public Task 首次打开立即显示标签且慢查询只更新局部不抢回焦点() => RunAsync(async (window, history, service) =>
    {
        window.SetStatusForTest(UiText.PathCopied);
        Task pending = OpenFile(history, "a.txt");
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest, "Git 返回前必须已打开比较标签。");
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        Assert.IsTrue(view.LoadingForTest);
        Assert.AreEqual(string.Empty, view.LoadingTextForTest, "首次输入不能立即闪烁加载提示。");
        StringAssert.Contains(view.FileBarTextForTest, "a.txt");
        Assert.IsFalse(view.NavigationEnabledForTest);
        int layouts = window.LayoutInvocationCountForTest;
        int viewLayouts = view.LayoutCountForTest;
        int resets = history.FileListResetCountForTest;
        _ = NativeMethods.SetFocus(history.FilesListHandleForTest);
        nint focus = NativeMethods.GetFocus();
        await WaitUntilAsync(() => view.LoadingTextForTest == UiText.GeneratingDiff
            && NativeMethods.IsWindowVisible(history.CancelButtonForTest));
        Assert.IsTrue(NativeMethods.IsWindowVisible(view.NoticeHandleForTest));
        Assert.AreEqual(UiText.GeneratingDiff, NativeMethods.GetWindowTextValue(view.NoticeHandleForTest));
        Assert.IsTrue(view.TextHandlesForTest.All(handle => !NativeMethods.IsWindowVisible(handle)),
            "首次加载不能显示空白代码控件与滚动条。");
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.IsTrue(NativeMethods.IsWindowEnabled(history.FilesListHandleForTest));
        service.Calls[0].Complete();
        await pending;
        await WaitUntilAsync(() => !view.LoadingForTest);
        StringAssert.Contains(view.BodyTextForTest, "a.txt");
        Assert.AreEqual(string.Empty, view.LoadingTextForTest);
        Assert.IsFalse(NativeMethods.IsWindowVisible(view.NoticeHandleForTest));
        Assert.IsFalse(NativeMethods.IsWindowVisible(history.CancelButtonForTest));
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(viewLayouts, view.LayoutCountForTest);
        Assert.AreEqual(resets, history.FileListResetCountForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 历史文件查询失败保留标签和局部原因且再次打开能重试(bool unexpectedException) => RunAsync(async (window, history, service) =>
    {
        window.SetStatusForTest(UiText.PathCopied);
        Task pending = OpenFile(history, "a.txt");
        await WaitUntilAsync(() => window.ComparisonViewForTest!.LoadingTextForTest == UiText.GeneratingDiff);
        if (unexpectedException)
        {
            service.Calls[0].Completion.SetException(new InvalidOperationException("不应直接显示的内部异常"));
        }
        else
        {
            service.Calls[0].Complete(failure: true);
        }
        await pending;
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
        Assert.AreEqual(unexpectedException ? UiText.GenerateDiffFailed : "旧查询失败", view.BodyTextForTest);
        Assert.IsFalse(view.LoadingForTest);
        Assert.IsFalse(view.NavigationEnabledForTest);
        Assert.AreEqual(0, view.ChangedLineCountForTest);
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        Assert.IsTrue(history.CommitDetailsLoadedForTest);
        view.ClickModeForTest(sideBySide: false);
        Assert.IsFalse(view.LoadingForTest, "失败后切换布局不能显示一份伪成功正文。");
        Task retry = OpenFile(history, "a.txt");
        Assert.HasCount(2, service.Calls);
        service.Calls[1].Complete();
        await retry;
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsFalse(view.UsesSideBySideForTest);
        StringAssert.Contains(view.BodyTextForTest, "a.txt");
    });

    [TestMethod]
    [DataRow(GitDiffContentStatus.Binary)]
    [DataRow(GitDiffContentStatus.SideTooLarge)]
    [DataRow(GitDiffContentStatus.OutputTooLarge)]
    [DataRow(GitDiffContentStatus.Ready)]
    public Task 首次历史文件摘要完成后保留说明并复用查询结果(GitDiffContentStatus status) => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        ControlledHistory.Call call = service.Calls[0];
        call.Completion.SetResult(GitComparisonResult.Success(new(status, call.Base, call.Target, call.Path, null)));
        await pending;
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        await WaitUntilAsync(() => !view.LoadingForTest);
        string expected = status switch
        {
            GitDiffContentStatus.Binary => UiText.BinaryDiffSummary,
            GitDiffContentStatus.SideTooLarge => UiText.DiffSideTooLarge,
            GitDiffContentStatus.OutputTooLarge => UiText.DiffOutputTooLarge,
            _ => UiText.NoTextDiff,
        };
        StringAssert.Contains(view.BodyTextForTest, expected);
        Assert.IsFalse(view.NavigationEnabledForTest);
        int renders = view.RenderCountForTest;
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        Assert.IsTrue(view.HasDocument, "打开普通文件不能清空比较内容。");
        Task reopened = OpenFile(history, "a.txt");
        Assert.HasCount(1, service.Calls, "返回已有摘要不应再次查询 Git。");
        await reopened;
        Assert.AreEqual(renders, view.RenderCountForTest);
        StringAssert.Contains(view.BodyTextForTest, expected);
    });

    [TestMethod]
    public Task 历史查询中回到普通文件后结果只在后台完成比较() => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        string path = Path.Combine(window.WorkspaceRoot!, "c.txt");
        await window.OpenDocumentForTestAsync(path);
        Assert.IsFalse(service.Calls[0].Token.IsCancellationRequested, "暂时查看普通文件不应关闭比较。");
        nint focus = NativeMethods.GetFocus();
        service.Calls[0].Complete();
        await pending;
        await WaitUntilAsync(() => window.ComparisonViewForTest?.LoadingForTest == false);
        Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
        Assert.AreEqual(path, window.ActiveDocumentPathForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Task reopened = OpenFile(history, "a.txt");
        Assert.HasCount(1, service.Calls);
        await reopened;
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
    });

    [TestMethod]
    public Task 慢历史文件可局部取消且晚到结果不能覆盖取消说明() => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        await WaitUntilAsync(() => NativeMethods.IsWindowVisible(history.CancelButtonForTest));
        _ = NativeMethods.SetFocus(history.CancelButtonForTest);
        _ = NativeMethods.SendMessage(history.CancelButtonForTest, 0x00F5, 0, 0);
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(service.Calls[0].Completion.Task.IsCompleted, "取消反馈不能等待不配合的查询返回。");
        Assert.IsTrue(service.Calls[0].Token.IsCancellationRequested);
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
        Assert.AreEqual(UiText.ComparisonCancelled, window.ComparisonViewForTest!.BodyTextForTest);
        Assert.IsFalse(window.ComparisonViewForTest.LoadingForTest);
        Assert.AreEqual(history.FilesListHandleForTest, NativeMethods.GetFocus(), "取消按钮消失后焦点应回到历史变化文件。");
        service.Calls[0].Complete();
        await Task.Yield();
        Assert.AreEqual(UiText.ComparisonCancelled, window.ComparisonViewForTest.BodyTextForTest);
    });

    [TestMethod]
    public Task 初次历史查询中切换模式只按最后布局排版并等待渲染完成() => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        view.ClickModeForTest(sideBySide: false);
        Assert.HasCount(1, service.Calls);
        Assert.IsTrue(view.LoadingForTest);
        TaskCompletionSource render = new(TaskCreationOptions.RunContinuationsAsynchronously);
        view.RenderBarrierForTest = render.Task;
        service.Calls[0].Complete();
        await pending;
        await WaitUntilAsync(() => view.RenderBarrierForTest is null);
        Assert.IsTrue(view.LoadingForTest, "查询完成时正文尚未排版，不能结束加载。");
        render.SetResult();
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsFalse(view.UsesSideBySideForTest);
        StringAssert.Contains(view.BodyTextForTest, "a.txt");
        Assert.HasCount(1, service.Calls);
    });

    [TestMethod]
    public Task 首次历史Diff加载中关闭标签即取消查询且不被晚到结果恢复() => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
        window.CloseActiveTabForTest();
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
        Assert.IsFalse(window.ComparisonViewForTest!.HasDocument);
        service.Calls[0].Complete();
        await Task.Yield();
        Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
        Assert.AreEqual(string.Empty, window.ComparisonViewForTest.BodyTextForTest);
    });

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public Task 连续打开历史文件后晚到成功或失败不能覆盖最后一次激活(bool oldFailure, bool doubleClick) => RunAsync(async (window, history, service) =>
    {
        window.SetStatusForTest(UiText.PathCopied);
        Task first = OpenFile(history, "a.txt", doubleClick);
        Task second = OpenFile(history, "b.txt", doubleClick);
        Assert.HasCount(2, service.Calls);
        int resets = history.HistoryListResetCountForTest;
        int fileResets = history.FileListResetCountForTest;
        string? selectedCommit = history.SelectedCommitHashForTest;
        service.Calls[1].Complete();
        await second;
        await WaitUntilAsync(() => window.ComparisonViewForTest?.LoadingForTest == false);
        string body = window.ComparisonViewForTest!.BodyTextForTest;
        StringAssert.Contains(body, "b.txt");
        nint focus = NativeMethods.GetFocus();
        int layouts = window.LayoutInvocationCountForTest;
        service.Calls[0].Complete(oldFailure);
        await first;
        Assert.AreEqual(body, window.ComparisonViewForTest.BodyTextForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus(), "旧结果不能抢回焦点。");
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        Assert.AreEqual(selectedCommit, history.SelectedCommitHashForTest);
        Assert.AreEqual(resets, history.HistoryListResetCountForTest);
        Assert.AreEqual(fileResets, history.FileListResetCountForTest);
        Assert.IsTrue(service.Calls[0].Token.IsCancellationRequested);
    });

    [TestMethod]
    public Task 同一历史文件连续Enter只保留一个待完成查询() => RunAsync(async (window, history, service) =>
    {
        Task first = OpenFile(history, "a.txt");
        for (int index = 0; index < 10; index++)
        {
            _ = NativeMethods.SendMessage(history.FilesListHandleForTest, NativeMethods.WindowMessageKeyDown,
                NativeMethods.VirtualKeyEnter, 0);
        }
        Assert.HasCount(1, service.Calls);
        service.Calls[0].Complete();
        await first;
        await history.LastFileActivationForTest;
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
    });

    [TestMethod]
    public Task 相同历史快照刷新不取消正在打开的不可变提交文件() => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        int resets = history.HistoryListResetCountForTest;
        window.RequestHistoryRefreshForTest();
        await WaitUntilAsync(() => !history.OperationRunningForTest);
        Assert.AreEqual(resets, history.HistoryListResetCountForTest);
        Assert.IsFalse(service.Calls[0].Token.IsCancellationRequested);
        service.Calls[0].Complete();
        await pending;
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
    });

    [TestMethod]
    [DataRow("选择其他文件")]
    [DataRow("切换提交")]
    [DataRow("折叠历史")]
    [DataRow("关闭比较")]
    [DataRow("销毁面板")]
    public Task 历史上下文退出后旧文件查询不得重新打开比较(string action) => RunAsync(async (window, history, service) =>
    {
        Task existing = OpenFile(history, "a.txt");
        service.Calls[0].Complete();
        await existing;
        await WaitUntilAsync(() => window.ComparisonViewForTest?.LoadingForTest == false);
        Task pending = OpenFile(history, "b.txt");
        switch (action)
        {
            case "选择其他文件":
                ClickFile(history, "c.txt");
                ClickFile(history, "b.txt");
                break;
            case "切换提交":
                string? previous = history.SelectedCommitHashForTest;
                ClickRow(history.HistoryListHandleForTest, 1);
                await WaitUntilAsync(() => history.SelectedCommitHashForTest != previous && history.CommitDetailsLoadedForTest);
                break;
            case "折叠历史":
                window.ToggleHistoryForTest();
                break;
            case "关闭比较":
                window.CloseActiveTabForTest();
                break;
            case "销毁面板":
                history.Dispose();
                break;
        }
        bool wasVisible = window.ReferenceComparisonVisibleForTest;
        string body = window.ComparisonViewForTest!.BodyTextForTest;
        nint focus = NativeMethods.GetFocus();
        service.Calls[1].Complete();
        await pending;
        Assert.AreEqual(wasVisible, window.ReferenceComparisonVisibleForTest);
        Assert.AreEqual(body, window.ComparisonViewForTest.BodyTextForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.IsTrue(service.Calls[1].Token.IsCancellationRequested);
        if (action == "关闭比较")
        {
            Assert.IsFalse(window.ComparisonViewForTest.HasDocument);
            Task reopened = OpenFile(history, "b.txt");
            Assert.HasCount(3, service.Calls);
            service.Calls[2].Complete();
            await reopened;
            Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
        }
    });

    [TestMethod]
    public Task 服务抛出取消后文件激活正常结束且允许重新打开() => RunAsync(async (window, history, service) =>
    {
        Task pending = OpenFile(history, "a.txt");
        service.Calls[0].Completion.SetCanceled();
        await pending;
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
        Assert.AreEqual(UiText.ComparisonCancelled, window.ComparisonViewForTest!.BodyTextForTest);
        Task reopened = OpenFile(history, "a.txt");
        service.Calls[1].Complete();
        await reopened;
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
    });

    [TestMethod]
    public Task 新引用比较取代待完成的历史文件查询() => RunAsync(async (window, history, service) =>
    {
        Task file = OpenFile(history, "a.txt");
        Task comparison = history.CompareRevisionForHostAsync("HEAD", "b.txt");
        Assert.HasCount(2, service.Calls);
        service.Calls[1].Complete();
        await comparison;
        await WaitUntilAsync(() => window.ComparisonViewForTest?.LoadingForTest == false);
        string body = window.ComparisonViewForTest!.BodyTextForTest;
        service.Calls[0].Complete();
        await file;
        Assert.AreEqual(body, window.ComparisonViewForTest.BodyTextForTest);
        StringAssert.Contains(body, "b.txt");
        Assert.IsTrue(service.Calls[0].Token.IsCancellationRequested);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 取消引用比较后服务晚到结果不能显示或报错(bool failure) => RunAsync(async (window, history, service) =>
    {
        window.SetStatusForTest(UiText.PathCopied);
        Task pending = history.CompareRevisionForHostAsync("HEAD", "b.txt");
        _ = NativeMethods.SendMessage(history.CancelButtonForTest, 0x00F5, 0, 0);
        Assert.IsTrue(service.Calls[0].Token.IsCancellationRequested);
        service.Calls[0].Complete(failure);
        await pending;
        Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        Assert.IsFalse(history.OperationRunningForTest);
        Assert.IsFalse(NativeMethods.IsWindowVisible(history.CancelButtonForTest));
        Assert.IsTrue(NativeMethods.IsWindowEnabled(history.HistoryListHandleForTest));
    });

    [TestMethod]
    public Task 关闭既有比较标签也取消历史面板发起的下一份引用查询() => RunAsync(async (window, history, service) =>
    {
        Task existing = OpenFile(history, "a.txt");
        service.Calls[0].Complete();
        await existing;
        Task pending = history.CompareRevisionForHostAsync("HEAD", "b.txt");
        window.CloseActiveTabForTest();
        service.Calls[1].Complete();
        await pending;
        Assert.IsTrue(service.Calls[1].Token.IsCancellationRequested);
        Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
        Assert.IsFalse(window.ComparisonViewForTest!.HasDocument);
    });

    private static Task OpenFile(NativeGitHistoryPanel history, string path, bool doubleClick = false)
    {
        ClickFile(history, path);
        if (doubleClick)
        {
            int row = history.FileTreeLabelsForTest.ToList().FindIndex(label => label.EndsWith(path, StringComparison.Ordinal));
            nint point = RowPoint(history.FilesListHandleForTest, row);
            _ = NativeMethods.SendMessage(history.FilesListHandleForTest, NativeMethods.WindowMessageLeftButtonDoubleClick, 1, point);
            _ = NativeMethods.SendMessage(history.FilesListHandleForTest, NativeMethods.WindowMessageLeftButtonUp, 0, point);
        }
        else
        {
            _ = NativeMethods.SendMessage(history.FilesListHandleForTest, NativeMethods.WindowMessageKeyDown,
                NativeMethods.VirtualKeyEnter, 0);
        }
        return history.LastFileActivationForTest;
    }

    private static void ClickFile(NativeGitHistoryPanel history, string path)
    {
        int row = history.FileTreeLabelsForTest.ToList().FindIndex(label => label.EndsWith(path, StringComparison.Ordinal));
        Assert.IsGreaterThanOrEqualTo(0, row);
        ClickRow(history.FilesListHandleForTest, row);
    }

    private static void ClickRow(nint list, int index)
    {
        nint point = RowPoint(list, index);
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageLeftButtonDown, 1, point);
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageLeftButtonUp, 0, point);
    }

    private static nint RowPoint(nint list, int index)
    {
        NativeMethods.Rectangle rectangle = default;
        Assert.AreNotEqual((nint)(-1), NativeMethods.SendMessage(list, NativeMethods.ListBoxGetItemRectangle,
            unchecked((nuint)index), ref rectangle));
        return (nint)((rectangle.Left + 70) | (((rectangle.Top + rectangle.Bottom) / 2) << 16));
    }

    private static async Task RunAsync(Func<MainWindow, NativeGitHistoryPanel, ControlledHistory, Task> scenario,
        string theme = "System", bool preciseDpi = false, bool fileHistoryFirst = false)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(null);
        Assert.IsTrue(runtime.IsAvailable, runtime.UnavailableReason);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(workspace);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        for (int version = 0; version < 2; version++)
        {
            foreach (string name in new[] { "a.txt", "b.txt", "c.txt" })
            {
                await File.WriteAllTextAsync(Path.Combine(workspace, name), $"内容 {version} {name}\n");
            }
            await RunGitAsync(runtime, workspace, "add", "--", "a.txt", "b.txt", "c.txt");
            await RunGitAsync(runtime, workspace, "-c", "user.name=Augit Tests", "-c", "user.email=augit@example.invalid",
                "-c", "core.hooksPath=NUL", "-c", "commit.gpgSign=false", "commit", "-m", $"test: history {version}");
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            nint previousDpiContext = preciseDpi ? SetDetailsThreadDpiContext(-4) : 0;
            try
            {
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new() { Theme = theme });
                Volatile.Write(ref active, window);
                window.Show();
                async Task VerifyAsync()
                {
                    ControlledHistory service = new(new GitHistoryService(runtime));
                    try
                    {
                        Assert.IsTrue(await window.OpenWorkspaceAsync(workspace));
                        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "c.txt"));
                        if (fileHistoryFirst) window.ShowFileHistoryForTest(Path.Combine(workspace, "a.txt"));
                        else window.ShowHistoryForTest();
                        await WaitUntilAsync(() => window.HistoryPanelForTest is
                        {
                            OperationRunningForTest: false, EntryCount: 2
                        });
                        NativeGitHistoryPanel history = window.HistoryPanelForTest!;
                        ClickRow(history.HistoryListHandleForTest, 0);
                        await WaitUntilAsync(() => history.CommitDetailsLoadedForTest && history.ChangedFileCountForTest == 3);
                        history.SetHistoryServiceForTest(service);
                        await scenario(window, history, service);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally
                    {
                        try
                        {
                            window.HistoryPanelForTest?.CancelPendingComparison();
                            foreach (TaskCompletionSource<GitHistoryResult> page in service.PendingPages) { page.TrySetCanceled(); }
                            foreach (TaskCompletionSource<GitCommitDetailsResult> detail in service.PendingDetails) { detail.TrySetCanceled(); }
                            foreach (ControlledHistory.Call call in service.Calls) { call.Completion.TrySetCanceled(); }
                            if (window.HistoryPanelForTest is { } history)
                            {
                                await history.LastFileActivationForTest;
                            }
                        }
                        catch (Exception exception) { failure ??= exception; }
                        finally { window.Close(); }
                    }
                }
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                if (previousDpiContext != 0) _ = SetDetailsThreadDpiContext(previousDpiContext);
                Volatile.Write(ref active, null);
                if (failure is null) { completion.TrySetResult(); }
                else { completion.TrySetException(failure); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(25)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "历史交互测试窗口没有退出。");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsLessThan(5000, timeout.ElapsedMilliseconds, "历史交互状态等待超时。");
            await Task.Delay(10);
        }
    }

    private static async Task RunGitAsync(GitRuntimeInfo runtime, string workspace, params string[] arguments)
    {
        ProcessStartInfo info = new(runtime.ExecutablePath!)
        {
            WorkingDirectory = workspace,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments) { info.ArgumentList.Add(argument); }
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("无法启动测试 Git。");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        Assert.AreEqual(0, process.ExitCode, (await error) + (await output));
    }

    private sealed class ControlledHistory(IGitHistoryService inner) : IGitHistoryService
    {
        internal sealed record Call(string Base, string? Target, string? Path,
            TaskCompletionSource<GitComparisonResult> Completion, CancellationToken Token, bool IgnoreWhitespace = false)
        {
            internal void Complete(bool failure = false) => Completion.SetResult(failure
                ? GitComparisonResult.Failure(GitOperationFailureKind.CommandFailed, "旧查询失败")
                : GitComparisonResult.Success(new(GitDiffContentStatus.Ready, Base, Target, Path,
                    $"@@ -0,0 +1 @@\n+内容 {Path}\n")));
        }

        internal List<Call> Calls { get; } = [];
        internal IReadOnlyList<GitHistoryEntry>? Entries { get; set; }
        internal int PageReads { get; private set; }
        internal List<GitHistoryRequest> PageRequests { get; } = [];
        internal Func<GitHistoryRequest, CancellationToken, Task<GitHistoryResult>>? ReadPage { get; set; }
        internal bool CompleteDiffImmediately { get; set; }
        internal List<TaskCompletionSource<GitHistoryResult>> PendingPages { get; } = [];
        internal List<TaskCompletionSource<GitCommitDetailsResult>> PendingDetails { get; } = [];
        internal Func<string, Task<GitCommitDetailsResult>>? ReadDetails { get; set; }
        internal Func<string, CancellationToken, Task<GitBlameResult>>? ReadBlame { get; set; }

        private Task<GitComparisonResult> Add(string basis, string? target, string? path, CancellationToken token, bool ignoreWhitespace = false)
        {
            // 故意允许被取消的服务返回成功或失败，检验真实输入路径对晚到结果的保护。
            Call call = new(basis, target, path, new(TaskCreationOptions.RunContinuationsAsynchronously), token, ignoreWhitespace);
            Calls.Add(call);
            if (CompleteDiffImmediately) call.Complete();
            return call.Completion.Task;
        }

        public Task<GitComparisonResult> ReadCommitFileDiffAsync(GitRepositorySnapshot repository,
            string revision, string relativePath, bool ignoreWhitespace = false, CancellationToken cancellationToken = default) =>
            Add($"{revision}^", revision, relativePath, cancellationToken, ignoreWhitespace);

        public Task<GitComparisonResult> CompareAsync(GitRepositorySnapshot repository,
            GitComparisonRequest request, CancellationToken cancellationToken = default) =>
            Add(request.BaseRevision, request.TargetRevision, request.RelativePath, cancellationToken);

        public Task<GitHistoryResult> ReadPageAsync(GitRepositorySnapshot repository,
            GitHistoryRequest request, CancellationToken cancellationToken = default)
        {
            PageReads++;
            PageRequests.Add(request);
            if (ReadPage is not null) return ReadPage(request, cancellationToken);
            return Entries is null ? inner.ReadPageAsync(repository, request, cancellationToken)
                : Task.FromResult(GitHistoryResult.Success(new(request.Page, request.PageSize, false, false, Entries)));
        }

        public Task<GitCommitDetailsResult> ReadCommitAsync(GitRepositorySnapshot repository,
            string revision, CancellationToken cancellationToken = default) =>
            ReadDetails is not null ? ReadDetails(revision) : Entries?.FirstOrDefault(entry => entry.FullHash == revision) is { } entry
                ? Task.FromResult(GitCommitDetailsResult.Success(new(entry, "", [])))
                : inner.ReadCommitAsync(repository, revision, cancellationToken);

        public Task<GitBlameResult> ReadBlameAsync(GitRepositorySnapshot repository,
            string relativePath, string? revision = null, CancellationToken cancellationToken = default) =>
            ReadBlame?.Invoke(relativePath, cancellationToken) ?? inner.ReadBlameAsync(repository, relativePath, revision, cancellationToken);
    }
}
