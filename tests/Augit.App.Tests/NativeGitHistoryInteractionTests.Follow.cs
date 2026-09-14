using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    [TestMethod]
    [DataRow("Light", 96)]
    [DataRow("Dark", 144)]
    public async Task 历史比较跟随文件和提交且普通文档前台不抢占并在关闭后解除(string theme, int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            ClickFile(history, "a.txt");
            Assert.HasCount(0, service.Calls, "首次单击不能创建比较。");
            Task opening = OpenFile(history, "a.txt");
            service.Calls[0].Complete();
            await opening;
            NativeGitComparisonView view = window.ComparisonViewForTest!;
            await WaitUntilAsync(() => !view.LoadingForTest);
            int layouts = window.LayoutInvocationCountForTest;
            int resets = history.HistoryListResetCountForTest;

            ClickFile(history, "b.txt");
            Assert.HasCount(2, service.Calls);
            nint focus = NativeMethods.GetFocus();
            service.Calls[1].Complete();
            await WaitUntilAsync(() => !view.LoadingForTest);
            Assert.AreSame(view, window.ComparisonViewForTest);
            StringAssert.Contains(view.BodyTextForTest, "b.txt");
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);

            await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
            ClickFile(history, "a.txt");
            focus = NativeMethods.GetFocus();
            Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
            service.Calls[2].Complete();
            await WaitUntilAsync(() => !view.LoadingForTest);
            Assert.IsTrue(window.ActiveDocumentVisibleForTest);
            Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus());

            string? previous = history.SelectedCommitHashForTest;
            ClickRow(history.HistoryListHandleForTest, 1);
            await WaitUntilAsync(() => service.Calls.Count == 4);
            Assert.AreNotEqual(previous, service.Calls[3].Target);
            Assert.AreEqual(history.SelectedCommitHashForTest, service.Calls[3].Target);
            StringAssert.Contains(view.FileBarTextForTest, "a.txt");
            service.Calls[3].Complete();
            await WaitUntilAsync(() => !view.LoadingForTest);
            Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
            Assert.AreEqual(resets, history.HistoryListResetCountForTest);

            await OpenFile(history, "a.txt");
            Assert.HasCount(4, service.Calls, "返回最新比较复用已有内容。");
            Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
            window.CloseActiveTabForTest();
            ClickFile(history, "b.txt");
            ClickRow(history.HistoryListHandleForTest, 0);
            await WaitUntilAsync(() => history.SelectedCommitHashForTest == previous && history.CommitDetailsLoadedForTest);
            Assert.HasCount(4, service.Calls, "关闭后提交和文件单击均不得重开比较。");
            Assert.IsFalse(view.HasDocument);
            Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
        }, theme, preciseDpi: true);
    }

    [TestMethod]
    public Task 历史后台跟随可直接激活且旧文件失败不能覆盖当前文件() => RunAsync(async (window, history, service) =>
    {
        Task opening = OpenFile(history, "a.txt");
        service.Calls[0].Complete();
        await opening;
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        await WaitUntilAsync(() => !view.LoadingForTest);
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        ClickFile(history, "b.txt");
        ClickFile(history, "c.txt");
        Task activate = OpenFile(history, "c.txt");
        Assert.HasCount(3, service.Calls);
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
        Assert.IsTrue(service.Calls[1].Token.IsCancellationRequested);
        service.Calls[2].Complete();
        await activate;
        await WaitUntilAsync(() => !view.LoadingForTest);
        service.Calls[1].Complete(failure: true);
        await Task.Delay(30);
        StringAssert.Contains(view.BodyTextForTest, "c.txt");
        StringAssert.Contains(view.FileBarTextForTest, "c.txt");
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 历史跟随提交无文件或失败时清除上一文件正文(bool failure) => RunAsync(async (window, history, service) =>
    {
        Task opening = OpenFile(history, "a.txt");
        service.Calls[0].Complete();
        await opening;
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        await WaitUntilAsync(() => !view.LoadingForTest);
        service.ReadDetails = hash => Task.FromResult(failure
            ? GitCommitDetailsResult.Failure(GitOperationFailureKind.CommandFailed, "无法读取提交")
            : GitCommitDetailsResult.Success(new(
                new("", hash, hash[..8], [], "作者", "test@example.invalid", DateTimeOffset.UnixEpoch, "空提交", []), "", [])));
        ClickRow(history.HistoryListHandleForTest, 1);
        await WaitUntilAsync(() => view.BodyTextForTest == (failure ? "无法读取提交" : UiText.SelectCommitFile));
        Assert.AreEqual(0, view.ChangedLineCountForTest);
        Assert.IsFalse(view.NavigationEnabledForTest);
        Assert.HasCount(1, service.Calls);
    });
}
