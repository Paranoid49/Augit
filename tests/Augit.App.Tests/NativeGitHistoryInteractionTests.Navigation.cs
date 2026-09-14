using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 首次从文件树打开文件历史返回后补齐尚未读取的日志(bool logTab) => RunAsync(async (window, history, service) =>
    {
        Assert.IsTrue(history.FileHistoryModeForTest);
        Assert.AreEqual("a.txt", history.FileFilterForTest);
        await WaitUntilAsync(() => history.FileHistoryComparisonForTest is { HasDocument: true, IsBusy: false });
        string? document = window.ActiveDocumentPathForTest;
        int layouts = window.LayoutInvocationCountForTest;
        nint list = history.HistoryListHandleForTest;
        if (logTab)
        {
            nint tab = HistoryChild(history.Handle, 60);
            _ = NativeMethods.SendMessage(tab, NativeMethods.WindowMessageLeftButtonDown, 1, (nint)(5 | 5 << 16));
            _ = NativeMethods.SendMessage(tab, NativeMethods.WindowMessageLeftButtonUp, 0, (nint)(5 | 5 << 16));
        }
        else _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 37), 0x00F5, 0, 0);
        await WaitUntilAsync(() => !history.FileHistoryModeForTest && !history.OperationRunningForTest && history.EntryCount == 2);
        Assert.IsNull(history.FileFilterForTest);
        Assert.AreEqual(1, service.PageReads, "原日志未加载时必须补查一次，不能把初始空快照当作结果。");
        Assert.AreEqual(list, NativeMethods.GetFocus());
        Assert.AreEqual(list, history.HistoryListHandleForTest);
        Assert.AreEqual(document, window.ActiveDocumentPathForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.IsFalse(history.FileHistoryEditorVisibleForTest);
    }, fileHistoryFirst: true);

    [TestMethod]
    public Task 文件历史返回真正已加载的空日志不重复查询() => RunAsync(async (window, history, service) =>
    {
        service.Entries = [];
        window.RequestHistoryRefreshForTest();
        await WaitUntilAsync(() => history.EntryCount == 0 && !history.OperationRunningForTest);
        service.CompleteDiffImmediately = true;
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => !history.OperationRunningForTest);
        int reads = service.PageReads;
        _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 37), 0x00F5, 0, 0);
        Assert.AreEqual(0, history.EntryCount);
        Assert.IsFalse(history.OperationRunningForTest);
        Assert.AreEqual(reads, service.PageReads);
        Assert.AreEqual(history.HistoryListHandleForTest, NativeMethods.GetFocus());
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 文件历史返回恢复已有路径筛选草稿文件选择与滚动(bool sameFiles) => RunAsync(async (window, history, service) =>
    {
        GitHistoryEntry[] entries = CreatePagedHistory()[..12];
        GitCommitChangedFile[] files = Enumerable.Range(0, 45)
            .Select(index => new GitCommitChangedFile(GitChangeKind.Modified, $"folder/item{index:00}.txt", null)).ToArray();
        service.Entries = entries;
        service.CompleteDiffImmediately = true;
        service.ReadPage = (request, _) => Task.FromResult(HistoryPage(
            request.Filter?.FilePath is null or "folder" ? entries : [entries[^1]], request.Page));
        service.ReadDetails = hash => Task.FromResult(GitCommitDetailsResult.Success(new(
            entries.First(entry => entry.FullHash == hash), "", sameFiles || hash == entries[0].FullHash
                ? files : [new(GitChangeKind.Added, "other.txt", null)])));
        history.SelectFilterKindForTest(6);
        nint input = HistoryChild(history.Handle, 63);
        _ = NativeMethods.SetWindowText(input, "folder");
        _ = NativeMethods.SendMessage(history.Handle, NativeMethods.WindowMessageCommand, 13, 0);
        await WaitUntilAsync(() => history.EntryCount == entries.Length && !history.OperationRunningForTest);
        ClickRow(history.HistoryListHandleForTest, 0);
        await WaitUntilAsync(() => history.CommitDetailsLoadedForTest && history.ChangedFileCountForTest == files.Length);
        history.SelectFilterKindForTest(0);
        _ = NativeMethods.SetWindowText(input, "尚未执行的筛选");
        nint list = history.FilesListHandleForTest;
        _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxSetTopIndex, 21, 0);
        ClickRow(list, 22);
        int selected = history.FileListSelectedIndexForTest, top = history.FileListTopIndexForTest;
        Assert.IsGreaterThan(0, top);
        int layouts = window.LayoutInvocationCountForTest;
        string? document = window.ActiveDocumentPathForTest;
        // 窄栏会隐藏文字动作，使用真实变化文件菜单进入文件历史。
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageContextMenu, (nuint)list, -1);
        NativeContextMenu menu = history.ContextMenuForTest!;
        Assert.IsNotNull(menu);
        _ = NativeMethods.SendMessage(menu.Handle, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyDown, 0);
        _ = NativeMethods.SendMessage(menu.Handle, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyDown, 0);
        _ = NativeMethods.SendMessage(menu.Handle, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        Assert.IsTrue(history.FileHistoryModeForTest);
        await WaitUntilAsync(() => history.FileHistoryModeForTest && history.EntryCount == 1
            && !history.OperationRunningForTest && service.Calls.Count == 1);
        Assert.AreEqual(history.HistoryListHandleForTest, NativeMethods.GetFocus());
        _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 36), 0x00F5, 0, 0);
        Assert.IsFalse(history.DetailsShownForTest);
        int reads = service.PageReads;
        _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 37), 0x00F5, 0, 0);
        Assert.IsFalse(history.FileHistoryModeForTest);
        Assert.AreEqual("folder", history.FileFilterForTest);
        Assert.AreEqual("尚未执行的筛选", NativeMethods.GetWindowTextValue(input));
        Assert.AreEqual(entries[0].FullHash, history.SelectedCommitHashForTest);
        Assert.AreEqual(selected, history.FileListSelectedIndexForTest);
        Assert.AreEqual(top, history.FileListTopIndexForTest);
        Assert.IsTrue(history.DetailsShownForTest);
        Assert.AreEqual(history.HistoryListHandleForTest, NativeMethods.GetFocus());
        Assert.AreEqual(reads, service.PageReads, "已有快照返回不需要重新查询 Git。");
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest, "底部导航不得重新布局主窗口。");
        Assert.AreEqual(document, window.ActiveDocumentPathForTest);
    });

    [TestMethod]
    public Task 文件历史同快照刷新和返回不重建列表或重复预览() => RunAsync(async (window, history, service) =>
    {
        service.CompleteDiffImmediately = true;
        int resets = history.HistoryListResetCountForTest, builds = history.CommitGraphBuildCountForTest;
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => history.FileHistoryModeForTest && !history.OperationRunningForTest && service.Calls.Count == 1);
        NativeGitComparisonView view = history.FileHistoryComparisonForTest!;
        await WaitUntilAsync(() => view.HasDocument && !view.LoadingForTest);
        nint editor = view.TextHandlesForTest[3];
        Assert.AreNotEqual((nint)0, editor);
        _ = NativeMethods.SendMessage(editor, 2160, 3, 8);
        nint anchor = NativeMethods.SendMessage(editor, 2009, 0, 0);
        nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0);
        int reads = service.PageReads;
        _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 10), 0x00F5, 0, 0);
        await WaitUntilAsync(() => service.PageReads > reads && !history.OperationRunningForTest);
        Assert.HasCount(1, service.Calls);
        Assert.AreEqual(anchor, NativeMethods.SendMessage(editor, 2009, 0, 0));
        Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
        _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 37), 0x00F5, 0, 0);
        Assert.AreEqual(resets, history.HistoryListResetCountForTest);
        Assert.AreEqual(builds, history.CommitGraphBuildCountForTest);
        Assert.AreEqual(string.Empty, history.FileHistoryEditorTextForTest, "退出后释放隐藏预览的正文。");
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 文件历史改选等待详情时旧预览立即失效(bool failure) => RunAsync(async (window, history, service) =>
    {
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => service.Calls.Count == 1 && !history.OperationRunningForTest);
        ControlledHistory.Call old = service.Calls[0];
        TaskCompletionSource<GitCommitDetailsResult> details = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.PendingDetails.Add(details);
        service.ReadDetails = _ => details.Task;
        ClickRow(history.HistoryListHandleForTest, 1);
        Assert.IsTrue(old.Token.IsCancellationRequested, "不能等到新提交详情返回才取消旧预览。");
        string before = history.FileHistoryEditorTextForTest;
        nint focus = NativeMethods.GetFocus();
        old.Complete(failure);
        await Task.Delay(60);
        Assert.AreEqual(before, history.FileHistoryEditorTextForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        details.SetResult(GitCommitDetailsResult.Failure(GitOperationFailureKind.CommandFailed, "新提交读取失败"));
        await WaitUntilAsync(() => history.FileHistoryEditorTextForTest == "新提交读取失败");
    });

    [TestMethod]
    [DataRow("返回")]
    [DataRow("隐藏")]
    [DataRow("销毁")]
    public Task 文件历史退出后取消预览且晚到结果不改变上下文(string action) => RunAsync(async (window, history, service) =>
    {
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => service.Calls.Count == 1 && !history.OperationRunningForTest);
        ControlledHistory.Call old = service.Calls[0];
        if (action == "返回") _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 37), 0x00F5, 0, 0);
        else if (action == "隐藏") window.ToggleHistoryForTest();
        else history.Dispose();
        Assert.IsTrue(old.Token.IsCancellationRequested);
        string? selected = history.SelectedCommitHashForTest;
        string text = history.FileHistoryEditorTextForTest;
        nint focus = NativeMethods.GetFocus();
        old.Complete();
        await Task.Delay(60);
        Assert.AreEqual(selected, history.SelectedCommitHashForTest);
        Assert.AreEqual(text, history.FileHistoryEditorTextForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        if (action == "隐藏")
        {
            window.ToggleHistoryForTest();
            await WaitUntilAsync(() => service.Calls.Count == 2);
            service.Calls[1].Complete();
            await WaitUntilAsync(() => history.FileHistoryEditorTextForTest.Contains("内容 a.txt", StringComparison.Ordinal));
        }
    });

    [TestMethod]
    public Task 文件历史返回时重新加载进入前尚未完成的提交详情() => RunAsync(async (window, history, service) =>
    {
        TaskCompletionSource<GitCommitDetailsResult> old = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.PendingDetails.Add(old);
        service.ReadDetails = _ => old.Task;
        ClickRow(history.HistoryListHandleForTest, 1);
        string? commit = history.SelectedCommitHashForTest;
        Assert.IsFalse(history.CommitDetailsLoadedForTest);
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => !history.OperationRunningForTest);
        service.ReadDetails = null;
        _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 37), 0x00F5, 0, 0);
        await WaitUntilAsync(() => history.CommitDetailsLoadedForTest);
        Assert.AreEqual(commit, history.SelectedCommitHashForTest);
        Assert.AreEqual(3, history.ChangedFileCountForTest);
        old.SetResult(GitCommitDetailsResult.Failure(GitOperationFailureKind.CommandFailed, "过期详情失败"));
        await Task.Delay(60);
        Assert.IsTrue(history.CommitDetailsLoadedForTest);
        Assert.AreEqual(3, history.ChangedFileCountForTest);
    });

    [TestMethod]
    public Task 文件历史失败后重复选择可重试且进行中重复选择不重复查询() => RunAsync(async (window, history, service) =>
    {
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => service.Calls.Count == 1 && !history.OperationRunningForTest);
        for (int index = 0; index < 4; index++) ClickRow(history.HistoryListHandleForTest, 0);
        Assert.HasCount(1, service.Calls);
        service.Calls[0].Complete(failure: true);
        await WaitUntilAsync(() => history.FileHistoryEditorTextForTest == "旧查询失败");
        ClickRow(history.HistoryListHandleForTest, 0);
        await WaitUntilAsync(() => service.Calls.Count == 2);
        service.Calls[1].Complete();
        await WaitUntilAsync(() => history.FileHistoryEditorTextForTest.Contains("内容 a.txt", StringComparison.Ordinal));
        Assert.AreEqual(history.HistoryListHandleForTest, NativeMethods.GetFocus());
    });

    [TestMethod]
    public Task 文件历史定位Blame提交恢复日志布局并取消旧预览() => RunAsync(async (window, history, service) =>
    {
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        await WaitUntilAsync(() => service.Calls.Count == 1 && !history.OperationRunningForTest);
        string commit = history.SelectedCommitHashForTest!;
        await history.LocateCommitAsync(commit);
        Assert.IsFalse(history.FileHistoryModeForTest);
        Assert.IsFalse(history.FileHistoryEditorVisibleForTest);
        Assert.IsNull(history.FileFilterForTest);
        Assert.AreEqual(commit, history.SelectedCommitHashForTest);
        Assert.AreEqual(history.HistoryListHandleForTest, NativeMethods.GetFocus());
        Assert.IsTrue(service.Calls[0].Token.IsCancellationRequested);
        service.Calls[0].Complete();
        await Task.Delay(60);
        Assert.AreEqual(string.Empty, history.FileHistoryEditorTextForTest);
        Assert.IsFalse(history.FileHistoryModeForTest);
    });
}
