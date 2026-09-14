using System.Diagnostics;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeGitDiffInteractionTests
{
    [TestMethod]
    public Task Changes右键菜单不改变当前文件选择且显示Diff作用于菜单目标() => RunScenarioAsync(async (window, panel) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        Assert.AreEqual("a.txt", window.GitSelectedChangedFilePathForTest);
        panel.SetCommitMessageForTest("fix: 保留菜单外的提交草稿");
        int checkedCount = panel.SelectedFileCountForTest;

        Assert.IsTrue(panel.ShowChangesContextMenuForHost("b.txt"));
        Assert.AreEqual("a.txt", window.GitSelectedChangedFilePathForTest,
            "打开右键菜单不能改变 Changes 当前文件选择。");
        Assert.IsNotNull(panel.ContextMenuForTest);

        NativeContextMenu menu = panel.ContextMenuForTest!;
        _ = NativeMethods.PostMessage(menu.Handle, NativeMethods.WindowMessageKeyDown,
            NativeMethods.VirtualKeyDown, 0);
        _ = NativeMethods.PostMessage(menu.Handle, NativeMethods.WindowMessageKeyDown,
            NativeMethods.VirtualKeyEnter, 0);
        await WaitUntilAsync(() => window.GitSelectedChangedFilePathForTest == "b.txt" && !panel.DiffLoadingForTest);
        Assert.AreEqual("b.txt", window.GitSelectedChangedFilePathForTest,
            "明确执行菜单中的显示 Diff 后才切换到菜单目标。");
        Assert.AreEqual((nint)0, menu.Handle);
        Assert.AreEqual(checkedCount, panel.SelectedFileCountForTest);
        Assert.AreEqual("fix: 保留菜单外的提交草稿", panel.CommitMessageForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 菜单队列Esc只关闭弹层并保留正文查找和提交上下文(bool toolbarPopup) => RunScenarioAsync(async (window, panel) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        window.ShowActiveDocumentFindForTest("内容");
        panel.SetCommitMessageForTest("fix: 弹层取消保留草稿");
        int requests = window.GitDiffRequestCountForTest;
        int layouts = window.LayoutInvocationCountForTest;
        int checkedCount = panel.SelectedFileCountForTest;
        string? selected = window.GitSelectedChangedFilePathForTest;
        nint originalFocus = NativeMethods.GetFocus();
        NativeToolbarPopup? toolbar = null;
        try
        {
            nint popup;
            if (toolbarPopup)
            {
                Assert.IsTrue(NativeMethods.GetWindowRectangle(panel.Handle, out NativeMethods.Rectangle anchor));
                toolbar = new(panel.Handle, anchor,
                    [new("取消测试动作", () => Assert.Fail("Esc 不能执行工具动作。"),
                        (dc, rectangle, color) => NativeTheme.DrawNavigationIcon(dc, rectangle, NativeNavigationIcon.Search, color), true)], false);
                popup = toolbar.Handle;
            }
            else
            {
                Assert.IsTrue(panel.ShowChangesContextMenuForHost("b.txt"));
                popup = panel.ContextMenuForTest!.Handle;
            }

            nint focus = NativeMethods.GetFocus();
            Assert.IsTrue(NativeFocusNavigation.ContainsWindow(popup, focus));
            nint foregroundBeforeClose = NativeMethods.GetForegroundWindow();
            // 投递到真实主循环，不能用 SendMessage 绕过主窗口的快捷键预处理。
            Assert.IsTrue(NativeMethods.PostMessage(focus, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0));
            await WaitUntilAsync(() => !NativeMethods.IsWindow(popup));
            Assert.IsTrue(window.ActiveDocumentFindOverlayVisibleForTest, "Esc 只能关闭最上层，不能关闭正文中的查找。");
            nint foregroundAfterClose = NativeMethods.GetForegroundWindow();
            bool foregroundWasOwned = foregroundBeforeClose == popup || foregroundBeforeClose == window.Handle
                || NativeMethods.GetAncestor(foregroundBeforeClose, NativeMethods.GetAncestorRoot)
                    == NativeMethods.GetAncestor(window.Handle, NativeMethods.GetAncestorRoot);
            if (foregroundWasOwned)
            {
                Assert.AreEqual(originalFocus, NativeMethods.GetFocus(), "应用在前台时取消弹层应恢复打开前焦点。");
            }
            else
            {
                Assert.AreEqual(foregroundBeforeClose, foregroundAfterClose, "应用不在前台时不能抢回外部前台窗口。");
            }
            Assert.IsTrue(window.ActiveDocumentVisibleForTest);
            Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.AreEqual(checkedCount, panel.SelectedFileCountForTest);
            Assert.AreEqual(selected, window.GitSelectedChangedFilePathForTest);
            Assert.AreEqual("fix: 弹层取消保留草稿", panel.CommitMessageForTest);
        }
        finally
        {
            toolbar?.Dispose();
            panel.ContextMenuForTest?.Dispose();
        }
    });

    [TestMethod]
    public Task 分支弹层队列Tab和Esc不穿透正文查找() => RunScenarioAsync(async (window, panel) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        window.ShowActiveDocumentFindForTest("内容");
        int requests = window.GitDiffRequestCountForTest;
        Assert.IsTrue(await window.ShowBranchPopupForTestAsync());
        NativeBranchPopup popup = window.BranchPopupForTest!;
        Assert.IsTrue(popup.SearchHasFocusForTest);
        Assert.IsTrue(NativeMethods.PostMessage(NativeMethods.GetFocus(), NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyTab, 0));
        await WaitUntilAsync(() => popup.ReferencesListHasFocusForTest);
        Assert.IsTrue(NativeMethods.PostMessage(NativeMethods.GetFocus(), NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0));
        await WaitUntilAsync(() => window.BranchPopupForTest is null);
        Assert.IsTrue(window.ActiveDocumentFindOverlayVisibleForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
    });

    [TestMethod]
    public Task 状态栏跟随工作区比较且后台改选不覆盖普通文件() => RunScenarioAsync(async (window, panel) =>
    {
        string ordinary = Path.Combine(window.WorkspaceRoot!, "c.txt");
        await window.OpenDocumentForTestAsync(ordinary);
        NativeStatusBarTests.AssertFields(window, "UTF-8", "LF", "只读");
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        Assert.AreEqual(Path.Combine(window.WorkspaceRoot!, "a.txt"), window.StatusPathForTest);
        NativeStatusBarTests.AssertFields(window, "只读");
        Assert.IsTrue(await window.ClickGitFileForTestAsync("b.txt"));
        Assert.AreEqual(Path.Combine(window.WorkspaceRoot!, "b.txt"), window.StatusPathForTest);
        await window.OpenDocumentForTestAsync(ordinary);
        Assert.IsTrue(await window.ClickGitFileForTestAsync("a.txt"));
        Assert.AreEqual(ordinary, window.StatusPathForTest);
        NativeStatusBarTests.AssertFields(window, "UTF-8", "LF", "只读");
        Assert.IsTrue(await window.SelectGitFileForTestAsync("b.txt"));
        window.CloseActiveTabForTest();
        Assert.AreEqual(ordinary, window.StatusPathForTest);
        NativeStatusBarTests.AssertFields(window, "UTF-8", "LF", "只读");
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 普通文件晚到不得覆盖工作区Diff或提交草稿(bool existingDiff) => RunScenarioAsync(async (window, panel) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "a.txt"));
        if (existingDiff)
        {
            window.ShowGitForTest();
            Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        }
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = barrier.Task;
        Task pending = window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "b.txt"));
        try
        {
            window.ShowGitForTest();
            Assert.IsTrue(await window.SelectGitFileForTestAsync("c.txt"));
            panel.SetCommitMessageForTest("fix: 保留当前提交草稿");
            int checkedCount = panel.SelectedFileCountForTest;
            int requests = window.GitDiffRequestCountForTest;
            int layouts = window.LayoutInvocationCountForTest;
            NativeGitDiffGeometrySnapshot geometry = window.GitDiffGeometryForTest;
            nint focus = NativeMethods.GetFocus();
            window.SetStatusForTest(UiText.PathCopied);
            barrier.SetResult();
            await pending;
            Assert.IsTrue(window.GitDiffVisibleForTest);
            Assert.IsFalse(window.ActiveDocumentVisibleForTest);
            Assert.IsFalse(window.EmptyDocumentVisibleForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
            Assert.AreEqual(checkedCount, panel.SelectedFileCountForTest);
            Assert.AreEqual("fix: 保留当前提交草稿", panel.CommitMessageForTest);
            Assert.AreEqual("c.txt", window.GitSelectedChangedFilePathForTest);
            Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        }
        finally
        {
            barrier.TrySetResult();
            await pending;
        }
    });

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    public Task 工作区Diff前台关闭后台普通标签保持正文草稿与复选(int documentCount, bool closeButton) => RunScenarioAsync(async (window, panel) =>
    {
        for (int index = 0; index < documentCount; index++)
            await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, index == 0 ? "a.txt" : "b.txt"));
        window.ShowGitForTest();
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        panel.SetCommitMessageForTest("fix: 关闭标签保留草稿");
        int checkedCount = panel.SelectedFileCountForTest;
        int requests = window.GitDiffRequestCountForTest;
        int layouts = window.LayoutInvocationCountForTest;
        NativeGitDiffGeometrySnapshot geometry = window.GitDiffGeometryForTest;
        nint editor = panel.DiffEditorHandleForTest;
        _ = NativeMethods.SetFocus(editor);
        nint focus = NativeMethods.GetFocus();

        Assert.IsTrue(closeButton ? window.ClickDocumentTabCloseForTest(documentCount - 1)
            : window.MiddleClickDocumentTabForTest(documentCount - 1));

        Assert.IsTrue(window.GitDiffVisibleForTest);
        Assert.IsFalse(window.ActiveDocumentVisibleForTest);
        Assert.IsFalse(window.EmptyDocumentVisibleForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
        Assert.AreEqual(checkedCount, panel.SelectedFileCountForTest);
        Assert.AreEqual("fix: 关闭标签保留草稿", panel.CommitMessageForTest);
        Assert.AreEqual("a.txt", window.GitSelectedChangedFilePathForTest);
        Assert.AreEqual(documentCount, window.VisibleEditorTabCountForTest);
        window.CloseActiveTabForTest();
        Assert.IsFalse(window.GitDiffVisibleForTest);
        Assert.AreEqual(documentCount == 1, window.EmptyDocumentVisibleForTest);
        Assert.AreEqual(documentCount > 1, window.ActiveDocumentVisibleForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 工作区分段按钮按键切换保持焦点草稿和选择(bool useEnter) => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        panel.SetCommitMessageForTest("fix: 保留提交草稿");
        int selected = panel.SelectedFileCountForTest;
        int requests = window.GitDiffRequestCountForTest;
        int layouts = window.LayoutInvocationCountForTest;
        NativeGitDiffGeometrySnapshot geometry = window.GitDiffGeometryForTest;
        nint[] controls = panel.DiffModeToolbarHandlesForTest;
        NativeDiffToolbarAssertions.AssertGroup(panel.DiffHandleForTest, panel.DiffModeGroupBoundsForTest, controls[1], controls[2]);
        int key = useEnter ? NativeMethods.VirtualKeyEnter : NativeMethods.VirtualKeySpace;
        void Press(nint target, int value)
        {
            _ = NativeMethods.PostMessage(target, NativeMethods.WindowMessageKeyDown, unchecked((nuint)value), 0);
            _ = NativeMethods.PostMessage(target, 0x0101, unchecked((nuint)value), 0);
        }
        _ = NativeMethods.SetFocus(controls[0]);
        foreach (nint next in controls.Skip(1))
        {
            Press(NativeMethods.GetFocus(), NativeMethods.VirtualKeyTab);
            await WaitUntilAsync(() => NativeMethods.GetFocus() == next);
        }
        for (int index = 2; index >= 0; index--)
        {
            Assert.IsTrue(window.HandleTabNavigationForTest(backwards: true));
            Assert.AreEqual(controls[index], NativeMethods.GetFocus());
        }
        for (int mode = 0; mode < 2; mode++)
        {
            bool sideBySide = mode == 1;
            nint button = controls[sideBySide ? 1 : 2];
            _ = NativeMethods.SetFocus(button);
            Press(button, key);
            await WaitUntilAsync(() => panel.DiffUsesSideBySideForTest == sideBySide && !panel.DiffLoadingForTest);
            Assert.AreEqual(button, NativeMethods.GetFocus(), "模式重排不能将焦点交给正文或丢失焦点。");
            Assert.AreEqual(sideBySide, panel.DiffFileHeaderSideBySideForTest);
            NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(panel.DiffFileHeaderHandleForTest, panel.DiffEditorHandleForTest, sideBySide);
            Press(button, key);
            await Task.Delay(30);
            Assert.IsFalse(panel.DiffLoadingForTest, "再次激活已选模式不能进入加载。");
        }
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
        _ = NativeMethods.SetFocus(controls[0]);
        Press(controls[0], key);
        await WaitUntilAsync(() => window.GitDiffRequestCountForTest == requests + 1 && !panel.DiffLoadingForTest);
        Assert.AreEqual(controls[0], NativeMethods.GetFocus(), "忽略空白查询结束不能丢失按钮焦点。");
        Assert.AreEqual("a.txt", window.GitSelectedChangedFilePathForTest);
        Assert.AreEqual(selected, panel.SelectedFileCountForTest);
        Assert.AreEqual("fix: 保留提交草稿", panel.CommitMessageForTest);
        Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
    });

    [TestMethod]
    public Task 模式重排期间用户转移焦点后不得被旧任务抢回() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.DiffRenderBarrierForTest = barrier.Task;
        nint button = panel.DiffModeToolbarHandlesForTest[2];
        _ = NativeMethods.SetFocus(button);
        _ = NativeMethods.PostMessage(button, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        _ = NativeMethods.PostMessage(button, 0x0101, NativeMethods.VirtualKeyEnter, 0);
        await WaitUntilAsync(() => panel.DiffLoadingForTest);
        Assert.IsTrue(panel.DiffFileHeaderSideBySideForTest, "模式排版期间保留原文件信息布局。");
        _ = NativeMethods.SetFocus(panel.Handle);
        barrier.SetResult();
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.IsTrue(window.GitChangesListHasFocusForTest);
        Assert.IsFalse(panel.DiffUsesSideBySideForTest);
        Assert.IsFalse(panel.DiffFileHeaderSideBySideForTest);
        NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(panel.DiffFileHeaderHandleForTest, panel.DiffEditorHandleForTest, false);
    });

    [TestMethod]
    public Task Diff加载与完成只更新局部且保留其他操作状态() => RunScenarioAsync(async (window, panel) =>
    {
        window.SetStatusForTest(UiText.PathCopied);
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest, "首次打开 Diff 不应覆盖已有状态。");
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        Task<bool> opening = window.SelectGitFileForTestAsync("b.txt");
        NativeGitDiffGeometrySnapshot geometry = window.GitDiffGeometryForTest;
        int layouts = window.LayoutInvocationCountForTest;
        int panelLayouts = window.GitPanelLayoutInvocationCountForTest;
        await WaitUntilAsync(() => panel.DiffTitleForTest.Contains(UiText.GeneratingDiff, StringComparison.Ordinal));
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest, "慢查询的加载反馈也不能接管全局状态。");
        Assert.IsTrue(panel.DiffLoadingForTest);
        window.SetStatusForTest(UiText.FindLocated);
        service.Complete("b.txt");
        Assert.IsTrue(await opening);
        Assert.AreEqual(UiText.FindLocated, window.StatusTextForTest, "加载期间的新状态必须保留。");
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
        Assert.IsFalse(panel.DiffLoadingForTest);
        Assert.IsFalse(panel.DiffLoadingNoticeVisibleForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 b.txt");
        Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(panelLayouts, window.GitPanelLayoutInvocationCountForTest);
        int requests = window.GitDiffRequestCountForTest;
        Assert.IsTrue(await window.ClickGitFileForTestAsync("b.txt"));
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
        window.RequestGitRefreshForTest(Path.Combine(window.WorkspaceRoot!, "b.txt"));
        await WaitUntilAsync(() => !window.GitRefreshingForTest && !panel.DiffLoadingForTest);
        Assert.AreEqual(UiText.FindLocated, window.StatusTextForTest, "后台刷新当前 Diff 同样不能覆盖其他操作状态。");
    });

    [TestMethod]
    [DataRow(GitDiffContentStatus.Binary)]
    [DataRow(GitDiffContentStatus.SideTooLarge)]
    [DataRow(GitDiffContentStatus.OutputTooLarge)]
    [DataRow(GitDiffContentStatus.Ready)]
    public Task Diff摘要完成后保持可见并且可以返回普通差异(GitDiffContentStatus contentStatus) =>
        RunScenarioAsync(async (window, panel) =>
        {
            ControlledDiffService service = new();
            panel.SetDiffServiceForTest(service);
            Task<bool> opening = window.SelectGitFileForTestAsync("a.txt");
            await WaitUntilAsync(() => panel.DiffLoadingNoticeVisibleForTest);
            service.Complete("a.txt", GitDiffResult.Success(new(contentStatus, "a.txt", null, 100, 200,
                contentStatus == GitDiffContentStatus.Binary ? "Binary files a/a.txt and b/a.txt differ\n" : string.Empty)));
            Assert.IsTrue(await opening);
            Assert.IsFalse(panel.DiffLoadingForTest);
            Assert.IsTrue(panel.DiffLoadingNoticeVisibleForTest, "请求完成不能把最终摘要当加载层隐藏。");
            string expected = contentStatus switch
            {
                GitDiffContentStatus.Binary => UiText.BinaryDiffSummary,
                GitDiffContentStatus.SideTooLarge => UiText.DiffSideTooLarge,
                GitDiffContentStatus.OutputTooLarge => UiText.DiffOutputTooLarge,
                _ => UiText.NoTextDiff,
            };
            StringAssert.Contains(panel.DiffLoadingNoticeTextForTest, expected);
            Assert.AreEqual("a.txt", panel.DiffTitleForTest);
            int requests = window.GitDiffRequestCountForTest;
            Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
            Assert.AreEqual(requests, window.GitDiffRequestCountForTest, "重复打开摘要不应重复查询。");
            await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
            Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
            Assert.IsTrue(panel.DiffLoadingNoticeVisibleForTest, "从普通文件返回缓存的 Diff 摘要后不能显示空白。");
            Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
            Task<bool> next = window.SelectGitFileForTestAsync("b.txt");
            service.Complete("b.txt");
            Assert.IsTrue(await next);
            Assert.IsFalse(panel.DiffLoadingNoticeVisibleForTest);
            StringAssert.Contains(panel.DiffTextForTest, "内容 b.txt");
        });

    [TestMethod]
    public Task 慢Diff失败后清除生成标题并保留错误与列表上下文() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        Task<bool> opening = window.SelectGitFileForTestAsync("b.txt");
        await WaitUntilAsync(() => panel.DiffTitleForTest.Contains(UiText.GeneratingDiff, StringComparison.Ordinal));
        const string failure = "无法读取当前文件，请刷新后重试。";
        service.Complete("b.txt", GitDiffResult.Failure(GitOperationFailureKind.CommandFailed, failure));
        Assert.IsTrue(await opening);
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
        Assert.IsFalse(panel.DiffLoadingForTest);
        Assert.IsTrue(panel.DiffLoadingNoticeVisibleForTest);
        Assert.AreEqual(failure, panel.DiffLoadingNoticeTextForTest);
        Assert.AreEqual("b.txt", window.GitSelectedChangedFilePathForTest);
        Assert.IsTrue(window.GitPanelVisibleForTest);
        Assert.IsTrue(window.GitDiffVisibleForTest);
    });

    [TestMethod]
    public Task 慢Diff双击立即打开唯一标签且后续单击持续跟随() => RunScenarioAsync(async (window, panel) =>
    {
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        panel.SetCommitMessageForTest("fix: 保留提交草稿");
        Assert.IsTrue(window.ClickGitFileCheckboxForTest("a.txt"));
        int selectedCount = window.GitSelectedFileCountForTest;
        Assert.IsTrue(window.DoubleClickGitFileForTest("a.txt"));
        Assert.AreEqual(1, window.WorkspaceGitDiffTabCountForTest, "双击打开唯一标签不能等待 Git 返回。");
        Assert.AreEqual("a.txt", window.PreviewGitDiffPathForTest);
        int layouts = window.LayoutInvocationCountForTest;
        int panelLayouts = window.GitPanelLayoutInvocationCountForTest;
        NativeGitDiffGeometrySnapshot geometry = window.GitDiffGeometryForTest;
        Assert.IsTrue(await window.ClickGitFileForTestAsync("b.txt"));
        await WaitUntilAsync(() => string.Equals(
            window.PreviewGitDiffPathForTest,
            "b.txt",
            StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("b.txt", window.PreviewGitDiffPathForTest);
        Assert.IsTrue(await window.ClickGitFileForTestAsync("c.txt"));
        Assert.AreEqual("c.txt", window.PreviewGitDiffPathForTest);
        service.Complete("c.txt");
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        // 模拟取消来不及生效，旧查询晚于新查询返回。
        service.Complete("b.txt");
        service.Complete("a.txt");
        await WaitUntilAsync(() => service.CompletedContinuations == 3);
        Assert.AreEqual(1, window.WorkspaceGitDiffTabCountForTest);
        Assert.AreEqual("c.txt", window.PreviewGitDiffPathForTest);
        Assert.AreEqual("c.txt", panel.DiffTitleForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 c.txt");
        Assert.AreEqual(selectedCount, window.GitSelectedFileCountForTest);
        Assert.AreEqual("fix: 保留提交草稿", panel.CommitMessageForTest);
        Assert.IsTrue(window.GitChangesListHasFocusForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest, "单击切换路径不等于编辑区显隐变化，不应重排主窗口。");
        Assert.AreEqual(panelLayouts, window.GitPanelLayoutInvocationCountForTest);
        Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
        Task<bool> reopening = window.SelectGitFileForTestAsync("a.txt");
        Assert.IsTrue(await reopening);
        Assert.AreEqual("a.txt", window.PreviewGitDiffPathForTest, "重新打开文件继续更新唯一比较标签。");
        Assert.AreEqual(1, window.WorkspaceGitDiffTabCountForTest);
    });

    [TestMethod]
    public Task 关闭主窗口中的比较标签释放正文且取消未完成排版() => RunScenarioAsync(async (window, panel) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        window.ShowHistoryComparisonForTest(GitComparisonResult.Success(new(
            GitDiffContentStatus.Ready, "HEAD~1", "HEAD", "甲.txt", "@@ -0,0 +1 @@\n+甲\n")));
        NativeGitComparisonView view = window.ComparisonViewForTest!;
        Assert.IsNotNull(view);
        await WaitUntilAsync(() => !view.LoadingForTest);
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        view.RenderBarrierForTest = barrier.Task;
        view.ClickModeForTest(false);
        await WaitUntilAsync(() => view.RenderBarrierForTest is null);
        window.CloseActiveTabForTest();
        Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
        Assert.IsFalse(view.HasDocument);
        Assert.IsFalse(view.LoadingForTest);
        Assert.AreEqual(1, window.VisibleEditorTabCountForTest);
        foreach (nint editor in view.TextHandlesForTest)
        {
            Assert.AreEqual(0, NativeMethods.SendMessage(editor, 2183, 0, 0));
        }
        barrier.SetResult();
        await Task.Yield();
        Assert.IsFalse(view.HasDocument);
        Assert.AreEqual(1, window.VisibleEditorTabCountForTest);
        window.ShowHistoryComparisonForTest(GitComparisonResult.Success(new(
            GitDiffContentStatus.Ready, "HEAD~1", "HEAD", "乙.txt", "@@ -0,0 +1 @@\n+乙\n")));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
        StringAssert.Contains(view.BodyTextForTest, "乙");
    });

    [TestMethod]
    public Task 历史比较激活时单击Changes只更新后台Diff() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        window.ShowHistoryComparisonForTest(GitComparisonResult.Success(new(
            GitDiffContentStatus.Binary, "HEAD~1", "HEAD", "history.bin", null)));
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
        string comparisonTitle = window.ReferenceComparisonFileBarForTest;
        int layouts = window.LayoutInvocationCountForTest;
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        Assert.IsTrue(await window.ClickGitFileForTestAsync("b.txt"));
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest, "单击选择不能隐藏当前历史比较。");
        Assert.IsFalse(window.GitDiffVisibleForTest);
        service.Complete("b.txt");
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
        Assert.IsFalse(window.GitDiffVisibleForTest);
        Assert.AreEqual(comparisonTitle, window.ReferenceComparisonFileBarForTest);
        Assert.AreEqual("b.txt", window.PreviewGitDiffPathForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.IsTrue(await window.EnterGitFileForTestAsync("b.txt"));
        Assert.IsTrue(window.GitDiffVisibleForTest);
        Assert.IsFalse(window.ReferenceComparisonVisibleForTest, "只有明确打开 Diff 才切换编辑区。");
    });

    [TestMethod]
    public Task 新Diff仍在查询时旧正文渲染不能抢先显示() => RunScenarioAsync(async (window, panel) =>
    {
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        TaskCompletionSource renderBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.DiffRenderBarrierForTest = renderBarrier.Task;
        Task<bool> first = window.SelectGitFileForTestAsync("a.txt");
        service.Complete("a.txt");
        await WaitUntilAsync(() => panel.DiffRenderBarrierForTest is null);
        Assert.IsTrue(panel.DiffLoadingForTest, "正文尚未渲染时不能宣称请求完成。");
        Task<bool> next = window.SelectGitFileForTestAsync("b.txt");
        renderBarrier.SetResult();
        await first;
        Assert.IsTrue(panel.DiffLoadingForTest);
        Assert.IsFalse(panel.DiffTextForTest.Contains("内容 a.txt", StringComparison.Ordinal), "新选择等待期间不能显示旧查询正文。");
        service.Complete("b.txt");
        Assert.IsTrue(await next);
        Assert.IsFalse(panel.DiffLoadingForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 b.txt");
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
    });

    [TestMethod]
    public Task 两种Diff模式按相同变更块计数并跳转到各自正文位置() => RunScenarioAsync(async (window, panel) =>
    {
        const string patch = """
            @@ -1,6 +1,7 @@
             上文
            -旧甲
            -旧乙
            +新甲
            +新乙
            +新丙
             中间未变
            -删除项
             下文未变
            +最后新增
            """;
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        Task<bool> opening = window.SelectGitFileForTestAsync("a.txt");
        service.Complete("a.txt", GitDiffResult.Success(new(GitDiffContentStatus.Ready, "a.txt", null, 50, 70, patch)));
        Assert.IsTrue(await opening);
        int requests = window.GitDiffRequestCountForTest;
        int layouts = window.LayoutInvocationCountForTest;
        bool[] modes = [true, false];
        foreach (bool sideBySide in modes)
        {
            panel.ClickDiffModeForTest(sideBySide);
            await WaitUntilAsync(() => !panel.DiffLoadingForTest);
            Assert.AreEqual("3 处差异，0 个已包含", panel.DiffChangeSummaryForTest);
            // 首次向前导航应到最后一处；连续替换的删除和新增只经过一次。
            panel.ClickDiffChangeForTest(-1);
            Assert.AreEqual(sideBySide ? 8 : 10, CurrentDiffLine(panel));
            panel.ClickDiffChangeForTest(-1);
            Assert.AreEqual(sideBySide ? 6 : 8, CurrentDiffLine(panel));
            panel.ClickDiffChangeForTest(-1);
            Assert.AreEqual(2, CurrentDiffLine(panel));
            panel.ClickDiffChangeForTest(1);
            Assert.AreEqual(sideBySide ? 6 : 8, CurrentDiffLine(panel));
        }
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
    });

    private static int CurrentDiffLine(NativeGitPanel panel)
    {
        nint editor = panel.DiffEditorHandleForTest;
        nint position = NativeMethods.SendMessage(editor, 2008, 0, 0);
        return checked((int)NativeMethods.SendMessage(editor, 2166, unchecked((nuint)position), 0)) + 1;
    }

    [TestMethod]
    public Task 文件箭头同步列表与查询路径且列表首尾不循环() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("b.txt"));
        foreach (var (direction, path) in new[] { (1, "c.txt"), (1, "c.txt"), (-1, "b.txt"), (-1, "a.txt"), (-1, "a.txt") })
        {
            panel.ClickDiffFileForTest(direction);
            await WaitUntilAsync(() => !panel.DiffLoadingForTest);
            Assert.AreEqual(path, window.GitSelectedChangedFilePathForTest);
            Assert.AreEqual(path, panel.DiffTitleForTest);
            StringAssert.Contains(panel.DiffTextForTest, $"内容 {path}");
        }
    });

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task 差异边界再次操作才跨文件且反向落到最后一块(bool sideBySide) => RunScenarioAsync(async (window, panel) =>
    {
        const string patch = "@@ -1,3 +1,3 @@\n-旧首\n+新首\n 中间\n-旧尾\n+新尾\n";
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        Task<bool> opening = window.SelectGitFileForTestAsync("b.txt");
        service.Complete("b.txt", GitDiffResult.Success(new(GitDiffContentStatus.Ready, "b.txt", null, 30, 30, patch)));
        Assert.IsTrue(await opening);
        panel.ClickDiffModeForTest(sideBySide);
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        panel.SetCommitMessageForTest("fix: 跨文件保留草稿");
        Assert.IsTrue(window.ClickGitFileCheckboxForTest("b.txt"));
        int selectedCount = window.GitSelectedFileCountForTest;
        int requests = window.GitDiffRequestCountForTest;
        int layouts = window.LayoutInvocationCountForTest;
        int panelLayouts = window.GitPanelLayoutInvocationCountForTest;
        NativeGitDiffGeometrySnapshot geometry = window.GitDiffGeometryForTest;
        panel.ClickDiffChangeForTest(-1);
        int lastLine = CurrentDiffLine(panel);
        nint focus = NativeMethods.GetFocus();
        panel.ClickDiffChangeForTest(1);
        Assert.IsTrue(panel.DiffBoundaryHintVisibleForTest);
        Assert.AreEqual(UiText.DiffNextFileBoundary, panel.DiffBoundaryHintTextForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus(), "边界提示不能抢焦点。");
        Assert.AreEqual(lastLine, CurrentDiffLine(panel), "越过最后一块时不能循环回文件开头。");
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest, "第一次边界输入只提示，不读取相邻文件。");
        panel.ClickDiffChangeForTest(1);
        Assert.AreEqual("c.txt", window.GitSelectedChangedFilePathForTest);
        Assert.IsFalse(panel.DiffBoundaryHintVisibleForTest);
        Assert.AreEqual("3/3 个文件", panel.DiffFileSummaryForTest);
        service.Complete("c.txt");
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.AreEqual(1, CurrentDiffLine(panel));
        panel.ClickDiffChangeForTest(-1);
        Assert.AreEqual("c.txt", panel.DiffTitleForTest);
        Assert.AreEqual(UiText.DiffPreviousFileBoundary, panel.DiffBoundaryHintTextForTest);
        panel.ClickDiffChangeForTest(-1);
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
        Assert.AreEqual(lastLine, CurrentDiffLine(panel), "从后一个文件返回时定位上一文件最后一块。");
        Assert.AreEqual(selectedCount, window.GitSelectedFileCountForTest);
        Assert.AreEqual("fix: 跨文件保留草稿", panel.CommitMessageForTest);
        Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(panelLayouts, window.GitPanelLayoutInvocationCountForTest);
    });

    [TestMethod]
    public Task 跨文件慢查询的连续导航与改选丢弃旧结果() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        panel.ClickDiffChangeForTest(1);
        panel.ClickDiffChangeForTest(1);
        panel.ClickDiffChangeForTest(1);
        Assert.AreEqual("b.txt", window.GitSelectedChangedFilePathForTest);
        int requests = window.GitDiffRequestCountForTest;
        for (int i = 0; i < 5; i++)
        {
            panel.ClickDiffChangeForTest(1);
            panel.ClickDiffChangeForTest(-1);
        }
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
        Assert.AreEqual("b.txt", window.GitSelectedChangedFilePathForTest);
        Assert.IsTrue(await window.ClickGitFileForTestAsync("c.txt"));
        service.Complete("c.txt");
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        service.Complete("b.txt");
        await WaitUntilAsync(() => service.CompletedContinuations == 2);
        Assert.AreEqual("c.txt", panel.DiffTitleForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 c.txt");
        Assert.IsFalse(panel.DiffBoundaryHintVisibleForTest);
        Assert.IsTrue(window.GitChangesListHasFocusForTest);
    });

    [TestMethod]
    public Task 边界提示在取消模式切换隐藏和关闭后不能继续生效() => RunScenarioAsync(async (window, panel) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        Assert.IsTrue(await window.SelectGitFileForTestAsync("b.txt"));
        void ShowBoundary()
        {
            panel.ClickDiffChangeForTest(1);
            panel.ClickDiffChangeForTest(1);
            Assert.IsTrue(panel.DiffBoundaryHintVisibleForTest);
        }
        ShowBoundary();
        nint focus = NativeMethods.GetFocus();
        Assert.IsTrue(window.HandleApplicationShortcutForTest(NativeMethods.VirtualKeyEscape));
        Assert.IsFalse(panel.DiffBoundaryHintVisibleForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        panel.ClickDiffChangeForTest(1);
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
        Assert.IsTrue(panel.DiffBoundaryHintVisibleForTest, "Esc 后下一次点击必须重新提示。");
        _ = NativeMethods.PostMessage(panel.DiffEditorHandleForTest, NativeMethods.WindowMessageMouseWheel, 0, 0);
        await WaitUntilAsync(() => !panel.DiffBoundaryHintVisibleForTest);
        panel.ClickDiffChangeForTest(1);
        Assert.IsTrue(panel.DiffBoundaryHintVisibleForTest, "滚动后必须重新提示，不能沿用上一次跨文件意图。");
        panel.ClickDiffModeForTest(false);
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.IsFalse(panel.DiffBoundaryHintVisibleForTest);
        ShowBoundary();
        await window.OpenDocumentForTestAsync(Path.Combine(window.WorkspaceRoot!, "c.txt"));
        Assert.IsFalse(panel.DiffBoundaryHintVisibleForTest);
        Assert.IsTrue(await window.SelectGitFileForTestAsync("b.txt"));
        panel.ClickDiffChangeForTest(1);
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
        Assert.IsTrue(panel.DiffBoundaryHintVisibleForTest);
        window.CloseActiveTabForTest();
        Assert.IsFalse(panel.DiffBoundaryHintVisibleForTest);
        Assert.IsTrue(await window.SelectGitFileForTestAsync("b.txt"));
        ShowBoundary();
        nint hint = panel.DiffBoundaryHintHandleForTest;
        window.Close();
        Assert.IsFalse(NativeMethods.IsWindow(hint), "关闭窗口必须销毁边界提示控件。");
    });

    [TestMethod]
    public Task 跨文件查询期间用户转移焦点后完成不得抢回箭头() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        _ = NativeMethods.SetFocus(panel.DiffChangeButtonForTest(1));
        panel.ClickDiffChangeForTest(1);
        panel.ClickDiffChangeForTest(1);
        panel.ClickDiffChangeForTest(1);
        Assert.IsTrue(panel.DiffLoadingForTest);
        Assert.IsTrue(await window.ClickGitFileForTestAsync("b.txt"));
        await WaitUntilAsync(() => window.GitChangesListHasFocusForTest);
        service.Complete("b.txt");
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
        Assert.IsTrue(window.GitChangesListHasFocusForTest, "异步定位与导航焦点恢复都不能覆盖用户的新焦点。");
    });

    [TestMethod]
    public Task 文件列表首尾的差异导航保持当前文件() => RunScenarioAsync(async (window, panel) =>
    {
        foreach (var (path, direction, text) in new[]
        {
            ("a.txt", -1, UiText.DiffFirstFileBoundary),
            ("c.txt", 1, UiText.DiffLastFileBoundary),
        })
        {
            Assert.IsTrue(await window.SelectGitFileForTestAsync(path));
            int requests = window.GitDiffRequestCountForTest;
            for (int i = 0; i < 4; i++)
            {
                panel.ClickDiffChangeForTest(direction);
            }
            Assert.AreEqual(path, panel.DiffTitleForTest);
            Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
            Assert.AreEqual(text, panel.DiffBoundaryHintTextForTest);
            Assert.IsTrue(panel.DiffBoundaryHintVisibleForTest);
        }
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 原生按钮键盘输入跨文件并且Esc仅关闭边界提示(bool useEnter) => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("b.txt"));
        nint arrow = panel.DiffChangeButtonForTest(1);
        _ = NativeMethods.SetFocus(arrow);
        nint foreground = NativeMethods.GetForegroundWindow();
        int key = useEnter ? NativeMethods.VirtualKeyEnter : NativeMethods.VirtualKeySpace;
        void PressKey(int value)
        {
            _ = NativeMethods.PostMessage(arrow, NativeMethods.WindowMessageKeyDown, unchecked((nuint)value), 0);
            _ = NativeMethods.PostMessage(arrow, 0x0101, unchecked((nuint)value), 0);
        }
        PressKey(key);
        await Task.Delay(30);
        Assert.IsFalse(panel.DiffBoundaryHintVisibleForTest);
        PressKey(key);
        await WaitUntilAsync(() => panel.DiffBoundaryHintVisibleForTest);
        Assert.AreEqual(arrow, NativeMethods.GetFocus());
        PressKey(NativeMethods.VirtualKeyEscape);
        await WaitUntilAsync(() => !panel.DiffBoundaryHintVisibleForTest);
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
        PressKey(key);
        await WaitUntilAsync(() => panel.DiffBoundaryHintVisibleForTest);
        PressKey(key);
        await WaitUntilAsync(() => panel.DiffTitleForTest == "c.txt" && !panel.DiffLoadingForTest);
        Assert.IsFalse(panel.DiffBoundaryHintVisibleForTest);
        Assert.AreEqual(foreground, NativeMethods.GetForegroundWindow(), "后台测试窗口不能抢占前台应用。");
        if (foreground == window.Handle)
        {
            Assert.AreEqual(arrow, NativeMethods.GetFocus());
        }
        else
        {
            Assert.AreEqual((nint)0, NativeMethods.GetFocus(), "窗口不在前台时不能强制恢复导航焦点。");
        }
        Assert.IsTrue(window.GitDiffVisibleForTest);
    });

    [TestMethod]
    public Task Diff正文变化和焦点通知不能触发相同编号的工具栏命令() => RunScenarioAsync(async (window, panel) =>
    {
        panel.SetCommitMessageForTest("fix: 保留正在输入的提交草稿");
        int requests = window.GitDiffRequestCountForTest;
        Assert.IsTrue(await window.SelectGitFileForTestAsync("b.txt"));
        Assert.AreEqual("b.txt", window.GitSelectedChangedFilePathForTest);
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 b.txt");
        Assert.AreEqual(requests + 1, window.GitDiffRequestCountForTest, "填充新侧正文不能误触发上一个文件。");

        nint editor = panel.DiffEditorHandleForTest;
        _ = NativeMethods.SetFocus(editor);
        Assert.AreEqual(editor, NativeMethods.GetFocus());
        Assert.AreEqual("b.txt", window.GitSelectedChangedFilePathForTest, "正文获取焦点不能执行工具栏导航。");
        Assert.AreEqual(requests + 1, window.GitDiffRequestCountForTest);
        Assert.AreEqual("fix: 保留正在输入的提交草稿", panel.CommitMessageForTest);

        panel.ClickDiffModeForTest(sideBySide: false);
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.IsTrue(NativeMethods.IsWindowEnabled(window.Handle), "填充单栏正文不能误打开设置模态框。");
        Assert.AreEqual("b.txt", panel.DiffTitleForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 b.txt");
        Assert.AreEqual(requests + 1, window.GitDiffRequestCountForTest);
    });

    [TestMethod]
    public Task Diff显示模式切换复用数据且重复点击不重新渲染() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        int requests = window.GitDiffRequestCountForTest;
        int layouts = window.LayoutInvocationCountForTest;
        panel.ClickDiffModeForTest(sideBySide: true);
        Assert.IsFalse(panel.DiffLoadingForTest, "重复点击当前模式不应再次进入加载。");
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
        panel.ClickDiffModeForTest(sideBySide: false);
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 a.txt");
        Assert.IsFalse(panel.DiffUsesSideBySideForTest);
        panel.ClickDiffModeForTest(sideBySide: true);
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.IsTrue(panel.DiffUsesSideBySideForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 a.txt");
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest, "只改变显示布局不需要重新读取 Git。");
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
    });

    [TestMethod]
    public Task 新文件查询中切换模式不能重新查询或显示旧文件() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        Task<bool> opening = window.SelectGitFileForTestAsync("b.txt");
        int requests = window.GitDiffRequestCountForTest;
        panel.ClickDiffModeForTest(sideBySide: false);
        Assert.IsTrue(panel.DiffLoadingForTest);
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest, "查询期间切换模式不能创建第二个 Git 请求。");
        service.Complete("b.txt");
        Assert.IsTrue(await opening);
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.IsFalse(panel.DiffUsesSideBySideForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 b.txt");
        Assert.IsFalse(panel.DiffTextForTest.Contains("内容 a.txt", StringComparison.Ordinal));
    });

    [TestMethod]
    public Task 渲染期间切换模式必须等最终模式完成后才结束加载() => RunScenarioAsync(async (window, panel) =>
    {
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        TaskCompletionSource firstRender = new(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.DiffRenderBarrierForTest = firstRender.Task;
        Task<bool> opening = window.SelectGitFileForTestAsync("a.txt");
        service.Complete("a.txt");
        await WaitUntilAsync(() => panel.DiffRenderBarrierForTest is null);
        TaskCompletionSource latestRender = new(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.DiffRenderBarrierForTest = latestRender.Task;
        panel.ClickDiffModeForTest(sideBySide: false);
        firstRender.SetResult();
        await WaitUntilAsync(() => panel.DiffRenderBarrierForTest is null);
        Assert.IsTrue(panel.DiffLoadingForTest, "旧布局渲染结束不能冒充用户最后选择的布局已完成。");
        Assert.IsFalse(opening.IsCompleted);
        latestRender.SetResult();
        Assert.IsTrue(await opening);
        Assert.IsFalse(panel.DiffLoadingForTest);
        Assert.IsFalse(panel.DiffUsesSideBySideForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 a.txt");
        Assert.AreEqual(1, window.GitDiffRequestCountForTest);
    });

    [TestMethod]
    public Task 关闭正在排版的Diff后旧任务不能恢复标签且缓存被释放() => RunScenarioAsync(async (window, panel) =>
    {
        string documentPath = Path.Combine(window.WorkspaceRoot!, "c.txt");
        await window.OpenDocumentForTestAsync(documentPath);
        window.ShowGitForTest();
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        Assert.IsGreaterThan(0, panel.CachedDiffPatchLengthForTest);
        TaskCompletionSource renderBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.DiffRenderBarrierForTest = renderBarrier.Task;
        panel.ClickDiffModeForTest(sideBySide: false);
        await WaitUntilAsync(() => panel.DiffRenderBarrierForTest is null);
        Assert.IsTrue(panel.DiffLoadingForTest);
        window.CloseActiveTabForTest();
        renderBarrier.SetResult();
        await WaitUntilAsync(() => !panel.DiffLoadingForTest);
        Assert.AreEqual(0, panel.CachedDiffPatchLengthForTest);
        Assert.AreEqual(string.Empty, panel.DiffTextForTest);
        Assert.IsFalse(window.GitDiffVisibleForTest);
        Assert.IsNull(window.PreviewGitDiffPathForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
        Assert.AreEqual(documentPath, window.ActiveDocumentPathForTest);
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        StringAssert.Contains(panel.DiffTextForTest, "内容 a.txt");
        Assert.IsFalse(panel.DiffUsesSideBySideForTest);
        Assert.IsGreaterThan(0, panel.CachedDiffPatchLengthForTest);
        window.CloseActiveTabForTest();
        Assert.AreEqual(0, panel.CachedDiffPatchLengthForTest);
    });

    [TestMethod]
    public Task 查询期间返回已显示文件应取消新查询并复用原结果() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        ControlledDiffService service = new();
        panel.SetDiffServiceForTest(service);
        Task<bool> pending = window.SelectGitFileForTestAsync("b.txt");
        int requests = window.GitDiffRequestCountForTest;
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
        Assert.IsFalse(panel.DiffLoadingForTest);
        service.Complete("b.txt");
        await pending;
        Assert.AreEqual("a.txt", panel.DiffTitleForTest);
        StringAssert.Contains(panel.DiffTextForTest, "内容 a.txt");
    });

    private static async Task RunScenarioAsync(Func<MainWindow, NativeGitPanel, Task> scenario)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(null);
        Assert.IsTrue(runtime.IsAvailable, runtime.UnavailableReason);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(workspace);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        foreach (string name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            await File.WriteAllTextAsync(Path.Combine(workspace, name), $"内容 {name}\n");
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? activeWindow = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            try
            {
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
                Volatile.Write(ref activeWindow, window);
                window.Show();
                async Task VerifyAndCloseAsync()
                {
                    try
                    {
                        Assert.IsTrue(await window.OpenWorkspaceAsync(workspace));
                        window.ShowGitForTest();
                        await WaitUntilAsync(() => window.GitChangedFileCountForTest == 3 && !window.GitRefreshingForTest);
                        Assert.IsNotNull(window.GitPanelForTest);
                        await scenario(window, window.GitPanelForTest);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        window.Close();
                    }
                }
                window.Post(() => _ = VerifyAndCloseAsync());
                _ = MainWindow.RunMessageLoop();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                Volatile.Write(ref activeWindow, null);
                if (failure is null)
                {
                    completion.TrySetResult();
                }
                else
                {
                    completion.TrySetException(failure);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref activeWindow);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "Diff 交互测试窗口没有退出。");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsLessThan(5000, elapsed.ElapsedMilliseconds, "Diff 交互状态等待超时。");
            await Task.Delay(10);
        }
    }

    private sealed class ControlledDiffService : IGitDiffService
    {
        private readonly Dictionary<string, TaskCompletionSource<GitDiffResult>> _results = new(StringComparer.OrdinalIgnoreCase);

        internal int CompletedContinuations { get; private set; }

        public async Task<GitDiffResult> CreateAsync(
            GitRepositorySnapshot repository,
            GitChangedFile changedFile,
            GitDiffOptions options,
            CancellationToken cancellationToken = default)
        {
            // 故意忽略取消，让真实窗口路径验证晚到结果不会覆盖新选择。
            if (!_results.TryGetValue(changedFile.RelativePath, out TaskCompletionSource<GitDiffResult>? pending))
            {
                pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _results.Add(changedFile.RelativePath, pending);
            }
            GitDiffResult result = await pending.Task;
            CompletedContinuations++;
            return result;
        }

        internal void Complete(string path, GitDiffResult? result = null)
        {
            Assert.IsTrue(_results.TryGetValue(path, out TaskCompletionSource<GitDiffResult>? pending));
            pending.SetResult(result ?? GitDiffResult.Success(new(
                GitDiffContentStatus.Ready, path, null, 0, 20,
                $"diff --git a/{path} b/{path}\n--- /dev/null\n+++ b/{path}\n@@ -0,0 +1 @@\n+内容 {path}\n")));
        }

        internal bool HasPending(string path) => _results.ContainsKey(path);
    }
}
