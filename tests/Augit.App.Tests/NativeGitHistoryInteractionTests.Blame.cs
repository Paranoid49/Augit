using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    [TestMethod]
    [DataRow("继续读取")]
    [DataRow("关闭历史")]
    [DataRow("关闭后重开历史")]
    [DataRow("切换文件历史")]
    [DataRow("切换文件")]
    [DataRow("关闭目标文件")]
    [DataRow("比较后返回")]
    [DataRow("切换工作区")]
    public Task Blame查询结束后正文等待仍受整条请求生命周期约束(string action) => RunAsync(async (window, history, service) =>
    {
        TaskCompletionSource bodyReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        service.ReadBlame = (_, cancellation) =>
        {
            token = cancellation;
            return Task.FromResult(GitBlameResult.Success([BlameLine("a.txt")]));
        };
        window.DocumentReadBarrierForTest = bodyReady.Task;
        Task pending = history.ShowBlameForPathAsync("a.txt");
        try
        {
            Assert.IsFalse(pending.IsCompleted, "Git 已结束但正文尚未读完，必须实际进入第二阶段。");
            Assert.IsFalse(token.IsCancellationRequested, "归属自己打开目标文件不能取消自身。");
            window.DocumentReadBarrierForTest = null;
            switch (action)
            {
                case "关闭历史":
                case "关闭后重开历史":
                    window.ToggleHistoryForTest();
                    if (action == "关闭后重开历史") window.ToggleHistoryForTest();
                    break;
                case "切换文件历史":
                    service.CompleteDiffImmediately = true;
                    window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "b.txt"));
                    await WaitUntilAsync(() => history.FileHistoryComparisonForTest is { IsBusy: false, HasDocument: true });
                    break;
                case "切换文件":
                    await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
                    break;
                case "关闭目标文件":
                    window.CloseActiveTabForTest();
                    break;
                case "比较后返回":
                    window.ShowHistoryComparisonForTest(GitComparisonResult.Success(new(GitDiffContentStatus.Ready,
                        "main", "HEAD", "a.txt", "@@ -1 +1 @@\n-旧\n+新\n")));
                    await WaitUntilAsync(() => !window.ComparisonViewForTest!.LoadingForTest);
                    window.CloseActiveTabForTest();
                    break;
                case "切换工作区":
                    Assert.IsTrue(await window.OpenWorkspaceAsync(Path.GetDirectoryName(window.WorkspaceRoot!)!));
                    break;
            }
            Assert.AreEqual(action != "继续读取", token.IsCancellationRequested);
            int tabs = window.OpenDocumentCount;
            window.SetStatusForTest("保留后续操作");
            bodyReady.SetResult();
            await pending;
            Assert.AreEqual(tabs, window.OpenDocumentCount, "过期结果不能重新打开已关闭的文件。");
            Assert.AreEqual(action == "继续读取", window.ActiveDocumentIsShowingBlameForTest);
            if (action != "继续读取") Assert.AreEqual("保留后续操作", window.StatusTextForTest);
        }
        finally
        {
            window.DocumentReadBarrierForTest = null;
            bodyReady.TrySetResult();
            await pending;
        }
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Blame等待正文时新查询成功或失败均淘汰旧归属(bool nextFails) => RunAsync(async (window, history, service) =>
    {
        TaskCompletionSource bodyReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        service.ReadBlame = (path, cancellation) =>
        {
            if (path == "a.txt") oldToken = cancellation;
            return Task.FromResult(nextFails && path == "b.txt"
                ? GitBlameResult.Failure(GitOperationFailureKind.CommandFailed, "本次查询失败")
                : GitBlameResult.Success([BlameLine(path)]));
        };
        window.DocumentReadBarrierForTest = bodyReady.Task;
        Task old = history.ShowBlameForPathAsync("a.txt");
        try
        {
            Assert.IsFalse(old.IsCompleted);
            Assert.IsFalse(oldToken.IsCancellationRequested);
            window.DocumentReadBarrierForTest = null;
            await history.ShowBlameForPathAsync("b.txt");
            Assert.IsTrue(oldToken.IsCancellationRequested);
            string status = window.StatusTextForTest;
            bodyReady.SetResult();
            await old;
            Assert.AreEqual(!nextFails, window.ActiveDocumentIsShowingBlameForTest);
            Assert.AreEqual(status, window.StatusTextForTest);
            if (!nextFails) Assert.AreEqual(Path.Combine(window.WorkspaceRoot!, "b.txt"), window.ActiveDocumentPathForTest);
        }
        finally
        {
            window.DocumentReadBarrierForTest = null;
            bodyReady.TrySetResult();
            await old;
        }
    });

    private static GitBlameLine BlameLine(string path) => new(1, new string('a', 40),
        "测试作者", "test@example.invalid", DateTimeOffset.UnixEpoch, "提交", path, "内容");

    [TestMethod]
    [DataRow("切换文件", false)]
    [DataRow("关闭文件", true)]
    [DataRow("关闭历史", false)]
    [DataRow("切换文件", true)]
    [DataRow("打开比较", false)]
    public Task Blame慢查询在用户离开后取消且晚到结果不重开文档(string action, bool failed) => RunAsync(async (window, history, service) =>
    {
        TaskCompletionSource<GitBlameResult> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        service.ReadBlame = (_, cancellation) => { token = cancellation; return result.Task; };
        Task pending = history.ShowBlameForPathAsync("a.txt");
        try
        {
            Assert.IsFalse(pending.IsCompleted);
            if (action == "切换文件") await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "b.txt"));
            else if (action == "关闭文件") window.CloseActiveTabForTest();
            else if (action == "打开比较")
            {
                window.ShowHistoryComparisonForTest(GitComparisonResult.Success(new(GitDiffContentStatus.Ready,
                    "main", "HEAD", "a.txt", "@@ -1 +1 @@\n-旧\n+新\n")));
                await WaitUntilAsync(() => !window.ComparisonViewForTest!.LoadingForTest);
            }
            else window.ToggleHistoryForTest();
            Assert.IsTrue(token.IsCancellationRequested);
            string? path = window.ActiveDocumentPathForTest;
            nint focus = NativeMethods.GetFocus();
            int tabs = window.OpenDocumentCount;
            window.SetStatusForTest("保留新上下文");
            result.SetResult(failed ? GitBlameResult.Failure(GitOperationFailureKind.CommandFailed, "过期错误")
                : GitBlameResult.Success([]));
            await pending;
            Assert.AreEqual(path, window.ActiveDocumentPathForTest);
            Assert.AreEqual(tabs, window.OpenDocumentCount);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.IsFalse(window.ActiveDocumentIsShowingBlameForTest);
            Assert.AreEqual("保留新上下文", window.StatusTextForTest);
            Assert.AreEqual(action == "打开比较", window.ReferenceComparisonVisibleForTest);
        }
        finally { result.TrySetCanceled(); await pending; }
    });

    [TestMethod]
    public Task 连续Blame查询只展示最后一个文件且保留文件历史上下文() => RunAsync(async (window, history, service) =>
    {
        service.CompleteDiffImmediately = true;
        window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "b.txt"));
        await WaitUntilAsync(() => history.FileHistoryComparisonForTest is { IsBusy: false, HasDocument: true });
        TaskCompletionSource<GitBlameResult> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<GitBlameResult> second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        service.ReadBlame = (path, token) => { if (path == "a.txt") oldToken = token; return path == "a.txt" ? first.Task : second.Task; };
        Task old = history.ShowBlameForPathAsync("a.txt");
        Task current = history.ShowBlameForPathAsync("b.txt");
        try
        {
            Assert.IsTrue(oldToken.IsCancellationRequested);
            second.SetResult(GitBlameResult.Success([new(1, history.SelectedCommitHashForTest!, "作者", "test@example.invalid",
                DateTimeOffset.UnixEpoch, "提交", "b.txt", "内容")]));
            await current;
            Assert.IsTrue(window.ActiveDocumentIsShowingBlameForTest);
            Assert.AreEqual(Path.Combine(window.WorkspaceRoot!, "b.txt"), window.ActiveDocumentPathForTest);
            Assert.IsTrue(history.FileHistoryModeForTest);
            Assert.AreEqual("b.txt", history.FileFilterForTest);
            first.SetResult(GitBlameResult.Failure(GitOperationFailureKind.CommandFailed, "过期错误"));
            await old;
            Assert.IsTrue(window.ActiveDocumentIsShowingBlameForTest);
            Assert.AreEqual(Path.Combine(window.WorkspaceRoot!, "b.txt"), window.ActiveDocumentPathForTest);
            Assert.DoesNotContain("过期错误", window.StatusTextForTest);
        }
        finally
        {
            first.TrySetCanceled(); second.TrySetCanceled();
            await Task.WhenAll(old, current);
        }
    });
}
