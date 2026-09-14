using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeGitDiffInteractionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 改选慢Diff期间无变化状态通知复用当前请求且不重排(bool background) => RunScenarioAsync(async (window, panel) =>
    {
        // 两个文件必须有不同内容版本，否则错误地比较旧正文标识也可能侥幸通过。
        await File.AppendAllTextAsync(Path.Combine(window.WorkspaceRoot!, "b.txt"), "第二个文件具有不同长度\n");
        window.RequestGitRefreshForTest("b.txt");
        await WaitUntilAsync(() => !panel.RefreshingForTest);
        Assert.IsTrue(await window.EnterGitFileForTestAsync("a.txt"));
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        if (background)
        {
            await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
            window.ShowGitForTest();
        }
        panel.SetCommitMessageForTest("fix: 元数据通知不能打断比较");
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        Assert.IsTrue(await window.ClickGitFileForTestAsync("b.txt"));
        await WaitUntilAsync(() => service.HasPending("b.txt"));
        int requests = panel.DiffRequestCountForTest;
        int layouts = window.LayoutInvocationCountForTest;
        int panelLayouts = panel.LayoutInvocationCountForTest;
        int presentations = panel.DiffPresentationNotificationCountForTest;
        int populated = panel.ChangesPopulateCountForTest;
        var geometry = window.GitDiffGeometryForTest;
        _ = NativeMethods.SetFocus(panel.CommitMessageHandleForTest);
        try
        {
            for (int index = 0; index < 4; index++)
            {
                window.RequestGitMetadataRefreshForTest();
                await WaitUntilAsync(() => !panel.RefreshingForTest);
                Assert.AreEqual(requests, panel.DiffRequestCountForTest, "无关元数据通知不能重新请求正在加载的文件。");
                Assert.AreEqual(populated, panel.ChangesPopulateCountForTest);
                Assert.IsTrue(panel.DiffLoadingForTest);
            }
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.AreEqual(panelLayouts, panel.LayoutInvocationCountForTest);
            Assert.AreEqual(presentations, panel.DiffPresentationNotificationCountForTest);
            Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
            Assert.IsTrue(panel.CommitMessageHasFocusForTest);
            Assert.AreEqual("fix: 元数据通知不能打断比较", panel.CommitMessageForTest);
            Assert.AreEqual(!background, window.GitDiffVisibleForTest);
        }
        finally
        {
            service.Complete("b.txt");
        }
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 b.txt");
        Assert.IsTrue(panel.CommitMessageHasFocusForTest);
        Assert.AreEqual(!background, window.GitDiffVisibleForTest);
    });

    [TestMethod]
    public Task 外部刷新触发慢Diff后状态循环仍接纳新增文件() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.EnterGitFileForTestAsync("a.txt"));
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        window.RequestGitRefreshForTest("a.txt");
        await WaitUntilAsync(() => service.HasPending("a.txt"));
        int requests = panel.DiffRequestCountForTest;
        try
        {
            await WaitUntilAsync(() => !panel.RefreshingForTest);
            Assert.IsTrue(panel.DiffLoadingForTest, "状态完成不等于正文完成。");
            await File.WriteAllTextAsync(Path.Combine(window.WorkspaceRoot!, "d.txt"), "正文未就绪时新增\n");
            window.RequestGitRefreshForTest("d.txt");
            await WaitUntilAsync(() => panel.ChangedFileCount == 4 && !panel.RefreshingForTest);
            Assert.IsTrue(panel.DiffLoadingForTest);
            Assert.AreEqual(requests, panel.DiffRequestCountForTest);
            Assert.AreEqual("a.txt", panel.SelectedChangedFilePathForTest);
        }
        finally
        {
            service.Complete("a.txt");
        }
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
    });

    [TestMethod]
    public Task Changes分组计数同步底层列表文字且增量刷新不重建控件() => RunScenarioAsync(async (window, panel) =>
    {
        int unversionedGroup = panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles);
        Assert.IsGreaterThanOrEqualTo(0, unversionedGroup);
        StringAssert.Contains(panel.ChangeListDisplayTextForTest(unversionedGroup), "3");

        int resetCount = panel.ChangesListResetCountForTest;
        await File.WriteAllTextAsync(Path.Combine(window.WorkspaceRoot!, "d.txt"), "新增文件\n");
        window.RequestGitRefreshForTest("d.txt");
        await WaitUntilAsync(() => panel.ChangedFileCount == 4 && !panel.RefreshingForTest);

        unversionedGroup = panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles);
        StringAssert.Contains(panel.ChangeListDisplayTextForTest(unversionedGroup), "4");
        Assert.AreEqual(resetCount, panel.ChangesListResetCountForTest);
    });

    [TestMethod]
    [DataRow("未打开")]
    [DataRow("前台")]
    [DataRow("后台")]
    public Task 复选与空格只改变提交集合且无变化刷新不切换Diff(string state) => RunScenarioAsync(async (window, panel) =>
    {
        if (state != "未打开")
        {
            Assert.IsTrue(await window.EnterGitFileForTestAsync("a.txt"));
            await WaitUntilAsync(() => !panel.DiffLoadingForTest && panel.DiffTextForTest.Length > 0);
        }
        if (state != "前台")
        {
            await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
            window.ShowGitForTest();
        }
        Assert.IsTrue(await window.ClickGitFileForTestAsync("a.txt"));
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        panel.SetCommitMessageForTest("fix: 保留完整提交草稿");
        _ = NativeMethods.SetFocus(panel.CommitMessageHandleForTest);
        var geometry = window.GitDiffGeometryForTest;
        int layouts = panel.LayoutInvocationCountForTest;
        int populate = panel.ChangesPopulateCountForTest;
        int requests = panel.DiffRequestCountForTest;
        int presentations = panel.DiffPresentationNotificationCountForTest;
        string diffText = panel.DiffTextForTest;
        int selectedIndex = SelectedChangeIndex(panel);
        int topIndex = ChangesTopIndex(panel);

        for (int iteration = 0; iteration < 4; iteration++)
        {
            Assert.IsTrue(window.ClickGitFileCheckboxForTest("b.txt"));
            Assert.AreEqual(iteration % 2 == 0 ? 1 : 0, panel.SelectedFileCountForTest);
            Assert.AreEqual(selectedIndex, SelectedChangeIndex(panel), "勾选其他文件不能改选当前行。");
            Assert.AreEqual("a.txt", window.GitSelectedChangedFilePathForTest, "可见行与路径意图必须同时保持。");
            Assert.IsTrue(panel.CommitMessageHasFocusForTest, "勾选不能打断提交草稿输入。");
            Assert.AreEqual(topIndex, ChangesTopIndex(panel));
        }

        // 经主窗口消息循环处理按键，包含 TranslateMessage 产生的字符消息。
        _ = NativeMethods.SetFocus(panel.ChangesListHandleForTest);
        _ = NativeMethods.PostMessage(panel.ChangesListHandleForTest, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeySpace, 0);
        _ = NativeMethods.PostMessage(panel.ChangesListHandleForTest, 0x0101, NativeMethods.VirtualKeySpace, 0);
        await WaitUntilAsync(() => panel.SelectedFileCountForTest == 1);
        Assert.AreEqual("a.txt", window.GitSelectedChangedFilePathForTest);
        Assert.AreEqual(NativeCheckboxState.Mixed, panel.GroupCheckStateForTest(GitChangeGroup.UnversionedFiles));
        window.RequestGitRefreshForTest();
        await WaitUntilAsync(() => !window.GitRefreshingForTest);
        Assert.AreEqual(1, panel.SelectedFileCountForTest);
        Assert.AreEqual("fix: 保留完整提交草稿", panel.CommitMessageForTest);
        Assert.AreEqual(requests, panel.DiffRequestCountForTest);
        Assert.AreEqual(presentations, panel.DiffPresentationNotificationCountForTest);
        Assert.AreEqual(diffText, panel.DiffTextForTest);
        Assert.AreEqual(populate, panel.ChangesPopulateCountForTest, "复选与无变化刷新不重新填充列表。");
        Assert.AreEqual(layouts, panel.LayoutInvocationCountForTest);
        Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
        Assert.AreEqual(state == "前台", window.GitDiffVisibleForTest);
    });

    [TestMethod]
    public Task 分组复选折叠和双击复选保持选择Diff与草稿() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.EnterGitFileForTestAsync("a.txt"));
        Assert.IsTrue(window.ClickGitFileCheckboxForTest("a.txt"));
        panel.SetCommitMessageForTest("fix: 分组操作保留上下文");
        int selected = SelectedChangeIndex(panel);
        int requests = panel.DiffRequestCountForTest;
        int populate = panel.ChangesPopulateCountForTest;

        ClickChangesCheckbox(panel, panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles), isGroup: true);
        Assert.AreEqual(3, panel.SelectedFileCountForTest);
        Assert.AreEqual(NativeCheckboxState.Checked, panel.GroupCheckStateForTest(GitChangeGroup.UnversionedFiles));
        Assert.AreEqual(selected, SelectedChangeIndex(panel));
        ClickChangesCheckbox(panel, panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles), isGroup: true);
        Assert.AreEqual(0, panel.SelectedFileCountForTest);
        Assert.AreEqual(selected, SelectedChangeIndex(panel));
        ClickChangesCheckbox(panel, panel.ChangeEntryIndexForTest(GitChangeGroup.Changes), isGroup: true);
        Assert.AreEqual(0, panel.SelectedFileCountForTest, "空 Changes 组不能改变另一组的勾选。");
        Assert.AreEqual(selected, SelectedChangeIndex(panel));
        ClickChangesCheckbox(panel, panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles, "b.txt"), isGroup: false, doubleClick: true);
        Assert.AreEqual(1, panel.SelectedFileCountForTest, "双击复选框只切换一次，不打开或固定标签。");
        Assert.AreEqual(1, window.WorkspaceGitDiffTabCountForTest,
            "双击复选框不能改变已有唯一比较会话。");
        Assert.AreEqual(selected, SelectedChangeIndex(panel));
        Assert.AreEqual(populate, panel.ChangesPopulateCountForTest);

        Assert.IsTrue(window.ClickGitGroupChevronForTest(GitChangeGroup.UnversionedFiles));
        int collapsedSelection = SelectedChangeIndex(panel);
        Assert.AreEqual(NativeCheckboxState.Mixed, panel.GroupCheckStateForTest(GitChangeGroup.UnversionedFiles));
        ClickChangesCheckbox(panel, panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles), isGroup: true);
        Assert.AreEqual(3, panel.SelectedFileCountForTest, "折叠分组仍应勾选组内全部文件。");
        Assert.AreEqual(collapsedSelection, SelectedChangeIndex(panel));
        Assert.AreEqual(2, panel.VisibleChangeEntryCountForTest);
        Assert.IsTrue(window.ClickGitGroupChevronForTest(GitChangeGroup.UnversionedFiles));
        Assert.AreEqual(5, panel.VisibleChangeEntryCountForTest);
        Assert.AreEqual(requests, panel.DiffRequestCountForTest);
        Assert.AreEqual("a.txt", panel.DiffTitleForTest);
        Assert.AreEqual("fix: 分组操作保留上下文", panel.CommitMessageForTest);
    });

    [TestMethod]
    public Task 滚动后勾选其他文件不滚回选中行且外部增量保留勾选() => RunScenarioAsync(async (window, panel) =>
    {
        for (int index = 0; index < 40; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(window.WorkspaceRoot!, $"x{index:D2}.txt"), "新增文件\n");
        }
        window.RequestGitRefreshForTest();
        await WaitUntilAsync(() => panel.ChangedFileCount == 43 && !panel.RefreshingForTest);
        Assert.IsTrue(await window.EnterGitFileForTestAsync("a.txt"));
        _ = NativeMethods.SendMessage(panel.ChangesListHandleForTest, NativeMethods.ListBoxSetTopIndex, 3, 0);
        Assert.AreEqual(3, ChangesTopIndex(panel));
        int requests = panel.DiffRequestCountForTest;
        Assert.IsTrue(window.ClickGitFileCheckboxForTest("b.txt"));
        Assert.AreEqual(3, ChangesTopIndex(panel), "勾选可见行不能滚回视口外的当前文件。");
        Assert.AreEqual("a.txt", window.GitSelectedChangedFilePathForTest);
        await File.AppendAllTextAsync(Path.Combine(window.WorkspaceRoot!, "b.txt"), "外部更新\n");
        await File.WriteAllTextAsync(Path.Combine(window.WorkspaceRoot!, "z.txt"), "末尾新增\n");
        window.RequestGitRefreshForTest("b.txt", "z.txt");
        await WaitUntilAsync(() => panel.ChangedFileCount == 44 && !panel.RefreshingForTest);
        Assert.AreEqual(3, ChangesTopIndex(panel));
        Assert.AreEqual(1, panel.SelectedFileCountForTest);
        Assert.AreEqual("a.txt", window.GitSelectedChangedFilePathForTest);
        Assert.AreEqual(requests, panel.DiffRequestCountForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 提交空信息校验在输入区提示并保留复选与正文(bool pushAfterCommit) => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        Assert.IsTrue(window.ClickGitFileCheckboxForTest("b.txt"));
        _ = NativeMethods.SetFocus(panel.CommitMessageHandleForTest);
        var geometry = window.GitDiffGeometryForTest;
        int requests = panel.DiffRequestCountForTest;
        await panel.ClickCommitForTestAsync(pushAfterCommit);
        await WaitUntilAsync(() => !panel.RefreshingForTest);
        Assert.AreEqual("提交信息不能为空。", panel.CommitErrorForTest);
        Assert.AreEqual(panel.CommitErrorForTest, NativeMethods.GetWindowTextValue(panel.CommitFeedbackHandleForTest));
        Assert.IsTrue(panel.CommitMessageHasFocusForTest);
        Assert.IsTrue(panel.CommitActionEnabledForTest);
        Assert.AreEqual(1, panel.SelectedFileCountForTest);
        Assert.AreEqual("a.txt", panel.DiffTitleForTest);
        Assert.AreEqual(requests, panel.DiffRequestCountForTest);
        Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
        Assert.IsTrue(NativeMethods.GetWindowRectangle(panel.CommitFeedbackHandleForTest, out var feedback));
        Assert.IsTrue(NativeMethods.GetWindowRectangle(panel.CommitMessageHandleForTest, out var edit));
        Assert.IsTrue(feedback.Bottom <= edit.Top && edit.Top - feedback.Bottom <= NativeTheme.Scale(4));

        // EM_REPLACESEL 走真实 Edit 通知，不直接调用清除错误的实现。
        _ = NativeMethods.SendMessage(panel.CommitMessageHandleForTest, 0x00C2, 1, "fix: 补充提交信息");
        Assert.AreEqual(string.Empty, panel.CommitErrorForTest);
        Assert.AreEqual(UiText.CommitMessage, NativeMethods.GetWindowTextValue(panel.CommitFeedbackHandleForTest));
        Assert.AreEqual("fix: 补充提交信息", panel.CommitMessageForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 慢提交失败保留草稿且不抢回已转移的焦点(bool moveFocus) => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        Assert.IsTrue(window.ClickGitFileCheckboxForTest("b.txt"));
        panel.SetCommitMessageForTest("fix: 保留失败草稿");
        ControlledCommitService service = new();
        panel.SetCommitServiceForTest(service);
        _ = NativeMethods.SetFocus(panel.CommitMessageHandleForTest);
        Task commit = panel.ClickCommitForTestAsync();
        Assert.IsTrue(panel.OperationRunningForTest);
        Assert.IsFalse(panel.CommitActionEnabledForTest);
        if (moveFocus)
        {
            _ = NativeMethods.SetFocus(panel.DiffEditorHandleForTest);
        }
        const string error = "commit-msg hook 拒绝提交。\n请检查仓库提交规则。";
        service.Complete(GitCommitResult.Failure(GitOperationFailureKind.CommandFailed, error));
        await commit;
        await WaitUntilAsync(() => !panel.RefreshingForTest);
        Assert.AreEqual(error, panel.CommitErrorForTest);
        Assert.AreEqual(error.ReplaceLineEndings(" "), NativeMethods.GetWindowTextValue(panel.CommitFeedbackHandleForTest));
        Assert.AreEqual("fix: 保留失败草稿", panel.CommitMessageForTest);
        Assert.AreEqual(1, panel.SelectedFileCountForTest);
        Assert.AreEqual(moveFocus ? panel.DiffEditorHandleForTest : panel.CommitMessageHandleForTest, NativeMethods.GetFocus());
        Assert.IsFalse(panel.OperationRunningForTest);
        Assert.IsTrue(panel.CommitActionEnabledForTest);
        Assert.AreEqual("b.txt", service.Request!.SelectedRelativePaths.Single());
        Assert.AreEqual("fix: 保留失败草稿", service.Request.Message);

        window.RequestGitRefreshForTest();
        await WaitUntilAsync(() => !panel.RefreshingForTest);
        Assert.AreEqual(error, panel.CommitErrorForTest, "状态刷新不能抹掉提交失败原因。");
    });

    private static int SelectedChangeIndex(NativeGitPanel panel) =>
        checked((int)NativeMethods.SendMessage(panel.ChangesListHandleForTest, NativeMethods.ListBoxGetCurrentSelection, 0, 0));

    private static int ChangesTopIndex(NativeGitPanel panel) =>
        checked((int)NativeMethods.SendMessage(panel.ChangesListHandleForTest, NativeMethods.ListBoxGetTopIndex, 0, 0));

    private static void ClickChangesCheckbox(NativeGitPanel panel, int index, bool isGroup, bool doubleClick = false)
    {
        NativeMethods.Rectangle rectangle = default;
        Assert.AreNotEqual((nint)(-1), NativeMethods.SendMessage(panel.ChangesListHandleForTest,
            NativeMethods.ListBoxGetItemRectangle, unchecked((nuint)index), ref rectangle));
        var layout = NativeGitPanel.ChangeListRowLayoutForTest(isGroup);
        int x = rectangle.Left + layout.CheckboxLeft + layout.CheckboxSize / 2;
        int y = (rectangle.Top + rectangle.Bottom) / 2;
        nint point = (nint)((y << 16) | (x & 0xFFFF));
        _ = NativeMethods.SendMessage(panel.ChangesListHandleForTest, NativeMethods.WindowMessageLeftButtonDown, 1, point);
        _ = NativeMethods.SendMessage(panel.ChangesListHandleForTest, NativeMethods.WindowMessageLeftButtonUp, 0, point);
        if (doubleClick)
        {
            _ = NativeMethods.SendMessage(panel.ChangesListHandleForTest, NativeMethods.WindowMessageLeftButtonDoubleClick, 1, point);
            _ = NativeMethods.SendMessage(panel.ChangesListHandleForTest, NativeMethods.WindowMessageLeftButtonUp, 0, point);
        }
    }

    private sealed class ControlledCommitService : IGitCommitService
    {
        private readonly TaskCompletionSource<GitCommitResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal GitCommitRequest? Request { get; private set; }

        public Task<GitCommitResult> CommitAsync(GitRepositorySnapshot repository, GitCommitRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return _completion.Task;
        }

        internal void Complete(GitCommitResult result) => _completion.SetResult(result);

        public Task<GitCommitMessageResult> ReadLastCommitMessageAsync(GitRepositorySnapshot repository, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("此场景不应读取上一次提交。");

        public Task<GitCommitPolicyResult> ReadPolicyAsync(GitRepositorySnapshot repository, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("界面不应直接读取提交规则。");
    }
}
