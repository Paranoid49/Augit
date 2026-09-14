using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeGitDiffInteractionTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task 关闭后台工作区Diff终止跟随且保留前台和Changes(bool closeButton, bool loading) => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        window.ShowGitForTest();
        ControlledDiffService? service = null;
        if (loading)
        {
            service = new();
            panel.SetDiffServiceForTest(service);
            Assert.IsTrue(await window.ClickGitFileForTestAsync("b.txt"));
            await WaitUntilAsync(() => service.HasPending("b.txt"));
        }
        string selected = window.GitSelectedChangedFilePathForTest!;
        Assert.IsTrue(window.ClickGitFileCheckboxForTest("a.txt"));
        panel.SetCommitMessageForTest("fix: 保留后台关闭时的上下文");
        _ = NativeMethods.SetFocus(panel.CommitMessageHandleForTest);
        int layouts = window.LayoutInvocationCountForTest;
        int panelLayouts = panel.LayoutInvocationCountForTest;
        int requests = panel.DiffRequestCountForTest;
        var geometry = window.GitDiffGeometryForTest;
        Assert.IsTrue(window.CloseComparisonTabForTest(false, closeButton, whilePressed: () =>
            Assert.IsTrue(panel.CommitMessageHasFocusForTest, "关闭叉按下时不能抢焦点。")));
        Assert.IsTrue(panel.CommitMessageHasFocusForTest);
        Assert.AreEqual(selected, window.GitSelectedChangedFilePathForTest);
        Assert.AreEqual(1, panel.SelectedFileCountForTest);
        Assert.AreEqual("fix: 保留后台关闭时的上下文", panel.CommitMessageForTest);
        Assert.IsNull(window.PreviewGitDiffPathForTest);
        Assert.IsFalse(panel.DiffLoadingForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
        Assert.AreEqual(string.Empty, panel.DiffTextForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(panelLayouts, panel.LayoutInvocationCountForTest);
        Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
        if (service is not null)
        {
            service.Complete("b.txt");
            await WaitUntilAsync(() => service.CompletedContinuations == 1);
        }
        Assert.IsTrue(await window.ClickGitFileForTestAsync("a.txt"));
        window.RequestGitRefreshForTest();
        await WaitUntilAsync(() => !panel.RefreshingForTest);
        Assert.AreEqual(requests, panel.DiffRequestCountForTest, "关闭后单击与无变化刷新不得重开比较。");
        Assert.IsNull(window.PreviewGitDiffPathForTest);
        Assert.AreEqual(string.Empty, panel.DiffTextForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
        Assert.AreEqual(window.OpenDocumentCount, window.VisibleEditorTabCountForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 关闭前台工作区Diff保留列表选择且重新打开成功(bool closeButton) => RunScenarioAsync(async (window, panel) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        int selected = SelectedChangeIndex(panel);
        Assert.IsTrue(window.CloseComparisonTabForTest(false, closeButton));
        Assert.AreEqual(selected, SelectedChangeIndex(panel), "关闭比较不能重置 Changes 到分组行。");
        Assert.AreEqual("a.txt", window.GitSelectedChangedFilePathForTest);
        Assert.AreEqual(string.Empty, panel.DiffTextForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
        Assert.IsTrue(await window.EnterGitFileForTestAsync("a.txt"));
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.IsTrue(window.GitDiffVisibleForTest);
        StringAssert.Contains(panel.DiffTextForTest, "a.txt");
    });

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task 关闭后台引用比较不重排或中断前台视图(bool closeButton, bool workspaceFront) => RunScenarioAsync(async (window, panel) =>
    {
        window.ShowHistoryComparisonForTest(GitComparisonResult.Success(new(
            GitDiffContentStatus.Ready, "HEAD~1", "HEAD", "历史.txt", "@@ -0,0 +1 @@\n+历史正文\n")));
        NativeGitComparisonView comparison = window.ComparisonViewForTest!;
        await WaitUntilAsync(() => !comparison.LoadingForTest);
        if (workspaceFront) Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        else await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        window.ShowGitForTest();
        _ = NativeMethods.SetFocus(panel.CommitMessageHandleForTest);
        int layouts = window.LayoutInvocationCountForTest;
        string diff = panel.DiffTextForTest;
        Assert.IsTrue(window.CloseComparisonTabForTest(true, closeButton, whilePressed: () =>
            Assert.IsTrue(panel.CommitMessageHasFocusForTest)));
        Assert.IsTrue(panel.CommitMessageHasFocusForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.IsFalse(comparison.HasDocument);
        Assert.AreEqual(diff, panel.DiffTextForTest);
        Assert.AreEqual(workspaceFront, window.GitDiffVisibleForTest);
        Assert.AreEqual(!workspaceFront, window.ActiveDocumentVisibleForTest);
    });

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task 比较关闭叉移出或丢失捕获不关闭且不抢焦点(bool reference, bool cancelCapture) => RunScenarioAsync(async (window, panel) =>
    {
        if (reference)
            window.ShowHistoryComparisonForTest(GitComparisonResult.Success(new(
                GitDiffContentStatus.Binary, "HEAD~1", "HEAD", "历史.bin", null)));
        else Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        window.ShowGitForTest();
        _ = NativeMethods.SetFocus(panel.CommitMessageHandleForTest);
        int count = window.VisibleEditorTabCountForTest;
        Assert.IsFalse(window.CloseComparisonTabForTest(reference, true, cancelCapture: cancelCapture,
            releaseOutside: !cancelCapture, whilePressed: () => Assert.IsTrue(panel.CommitMessageHasFocusForTest)));
        Assert.IsTrue(panel.CommitMessageHasFocusForTest);
        Assert.AreEqual(count, window.VisibleEditorTabCountForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
    });

    [TestMethod]
    public Task 双击后改选只有一个后台Diff并能关闭解除跟随() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(window.DoubleClickGitFileForTest("a.txt"));
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.IsTrue(await window.SelectGitFileForTestAsync("b.txt"));
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        int requests = panel.DiffRequestCountForTest;
        string text = panel.DiffTextForTest;
        Assert.IsFalse(window.CloseComparisonTabForTest(false, false, "a.txt"));
        Assert.AreEqual(text, panel.DiffTextForTest);
        Assert.AreEqual(1, window.WorkspaceGitDiffTabCountForTest);
        Assert.IsTrue(window.CloseComparisonTabForTest(false, false, "b.txt"));
        Assert.IsNull(window.PreviewGitDiffPathForTest);
        Assert.IsTrue(await window.ClickGitFileForTestAsync("c.txt"));
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.AreEqual(requests, panel.DiffRequestCountForTest);
        Assert.IsNull(window.PreviewGitDiffPathForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
    });
}
