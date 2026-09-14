using System.Diagnostics;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeDocumentTabInteractionTests
{
    private static readonly string[] FileNames = ["a.txt", "b.txt", "c.txt"];

    [TestMethod]
    public Task 图片缩放滚动切出返回保持视口并在关闭后释放控件() => RunAsync(async (window, workspace) =>
    {
        int decodes = 0;
        window.ImageDecoderForTest = new(path => { Interlocked.Increment(ref decodes); return WicBitmapLoader.Load(path); });
        string text = Path.Combine(workspace, "a.txt"), picture = Path.Combine(workspace, "sample.png");
        await window.OpenDocumentForTestAsync(text);
        nint editor = NativeMethods.GetFocus();
        _ = NativeMethods.SendMessage(editor, 2024, 60, 0);
        _ = NativeMethods.SendMessage(editor, 2613, 50, 0);
        nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0), firstLine = NativeMethods.SendMessage(editor, 2152, 0, 0);
        await window.OpenDocumentForTestAsync(picture);
        NativeDocumentView document = window.ActiveDocumentViewForTest!;
        NativeImageView image = (NativeImageView)typeof(NativeDocumentView).GetField("_imageView",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(document)!;
        while (image.ZoomPercentage < 150) image.ZoomIn();
        _ = NativeMethods.SendMessage(image.Handle, NativeMethods.WindowMessageMouseWheel, unchecked((nuint)(-120 << 16)), 0);
        _ = NativeMethods.SendMessage(image.Handle, NativeMethods.WindowMessageMouseHorizontalWheel, (nuint)(120 << 16), 0);
        var bounds = image.CurrentImageBounds;
        int zoom = image.ZoomPercentage;
        await window.OpenDocumentForTestAsync(text);
        Assert.AreEqual(firstLine, NativeMethods.SendMessage(editor, 2152, 0, 0));
        Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
        _ = NativeMethods.SendMessage(image.Handle, NativeMethods.WindowMessageMouseWheel, (nuint)(120 << 16), 0);
        Assert.AreEqual(bounds, image.CurrentImageBounds, "后台图片不能接受晚到输入。");
        await window.OpenDocumentForTestAsync(picture);
        Assert.AreSame(document, window.ActiveDocumentViewForTest);
        Assert.AreEqual(1, decodes, "返回已打开图片不重新解码。");
        Assert.AreEqual(zoom, image.ZoomPercentage);
        Assert.AreEqual(bounds, image.CurrentImageBounds);
        Assert.AreEqual(picture, window.StatusPathForTest);
        image.FitToArea();
        Assert.IsTrue(image.IsFitToArea);
        nint imageHandle = image.Handle;
        window.CloseActiveTabForTest();
        await document.ImageWorkersForTest;
        Assert.IsFalse(NativeMethods.IsWindow(imageHandle));
        Assert.AreEqual(text, window.ActiveDocumentPathForTest);
        Assert.AreEqual(firstLine, NativeMethods.SendMessage(editor, 2152, 0, 0));
        Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
    }, imageSample: true);

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public Task 关闭后台标签保持当前正文及树或查找框上下文(bool closeAfterActive, bool findFocused, bool closeButton) =>
        RunAsync(async (window, workspace) =>
        {
            string background = Path.Combine(workspace, "a.txt");
            string current = Path.Combine(workspace, "b.txt");
            await window.OpenDocumentForTestAsync(background);
            nint closedEditor = NativeMethods.GetFocus();
            await window.OpenDocumentForTestAsync(current);
            nint editor = NativeMethods.GetFocus();
            Assert.IsGreaterThan((nint)0, NativeMethods.SendMessage(editor, 2183, 0, 0), "前置焦点必须是正文控件。");
            if (closeAfterActive)
            {
                await window.OpenDocumentForTestAsync(Path.Combine(workspace, "c.txt"));
                closedEditor = NativeMethods.GetFocus();
                Assert.IsTrue(await window.LeftClickDocumentTabForTestAsync(1));
            }
            _ = NativeMethods.SendMessage(editor, 2024, 60, 0);
            _ = NativeMethods.SendMessage(editor, 2613, 50, 0);
            if (findFocused)
            {
                window.ShowActiveDocumentFindForTest("未匹配的查找内容");
                Assert.IsTrue(window.ActiveFindEditHasFocusForTest);
            }
            else
            {
                Assert.IsTrue(await window.ClickTreePathForTestAsync(background));
                Assert.IsTrue(window.ProjectTreeHasFocusForTest);
            }
            nint focus = NativeMethods.GetFocus();
            nint firstLine = NativeMethods.SendMessage(editor, 2152, 0, 0);
            nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0);
            string? selection = window.SelectedTreePathForTest;
            int layouts = window.LayoutInvocationCountForTest;
            int count = window.OpenDocumentCount;

            int closedIndex = closeAfterActive ? 2 : 0;
            Assert.IsTrue(closeButton ? window.ClickDocumentTabCloseForTest(closedIndex,
                () => Assert.AreEqual(focus, NativeMethods.GetFocus(), "关闭叉按下阶段也必须保持原焦点。"))
                : window.MiddleClickDocumentTabForTest(closedIndex));

            Assert.AreEqual(current, window.ActiveDocumentPathForTest);
            Assert.IsTrue(window.ActiveDocumentVisibleForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus(), "关闭后台标签不得夺取当前输入焦点。");
            Assert.AreEqual(selection, window.SelectedTreePathForTest, "关闭后台标签不得重选项目树。");
            Assert.AreEqual(firstLine, NativeMethods.SendMessage(editor, 2152, 0, 0));
            Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
            Assert.AreEqual(findFocused, window.ActiveDocumentFindOverlayVisibleForTest);
            if (findFocused) Assert.AreEqual("未匹配的查找内容", NativeMethods.GetWindowTextValue(focus));
            Assert.AreEqual(count - 1, window.OpenDocumentCount);
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.IsFalse(NativeMethods.IsWindow(closedEditor), "关闭文件必须释放其正文控件。");
        });

    [TestMethod]
    [DataRow(1, false, false)]
    [DataRow(2, false, false)]
    [DataRow(1, true, false)]
    [DataRow(2, true, false)]
    [DataRow(1, false, true)]
    [DataRow(2, false, true)]
    [DataRow(1, true, true)]
    [DataRow(2, true, true)]
    public Task 比较前台关闭普通标签包括最后一个不覆盖比较或取消查询(int documentCount, bool loading, bool closeButton) =>
        RunAsync(async (window, workspace) =>
        {
            for (int index = 0; index < documentCount; index++)
                await window.OpenDocumentForTestAsync(Path.Combine(workspace, index == 0 ? "a.txt" : "b.txt"));
            window.ShowHistoryComparisonForTest(Ready());
            NativeGitComparisonView comparison = window.ComparisonViewForTest!;
            await WaitUntilAsync(() => !comparison.LoadingForTest);
            TaskCompletionSource<GitComparisonResult> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? pending = null;
            CancellationToken requestToken = default;
            try
            {
                if (loading)
                {
                    pending = comparison.LoadAsync(Ready().Document!, token =>
                    {
                        requestToken = token;
                        return result.Task;
                    }, CancellationToken.None);
                    Assert.IsTrue(comparison.LoadingForTest);
                }
                nint editor = comparison.TextHandlesForTest.First(NativeMethods.IsWindowVisible);
                _ = NativeMethods.SetFocus(editor);
                nint focus = NativeMethods.GetFocus();
                string body = comparison.BodyTextForTest;
                string fileBar = window.ReferenceComparisonFileBarForTest;
                int layouts = window.LayoutInvocationCountForTest;

                Assert.IsTrue(closeButton ? window.ClickDocumentTabCloseForTest(documentCount - 1)
                    : window.MiddleClickDocumentTabForTest(documentCount - 1));

                Assert.IsTrue(window.ReferenceComparisonVisibleForTest, "关闭后台普通文件不能切走比较。");
                Assert.IsFalse(window.ActiveDocumentVisibleForTest);
                Assert.IsFalse(window.EmptyDocumentVisibleForTest, "关闭最后一个普通文件不能用空白页覆盖比较。");
                Assert.AreEqual(focus, NativeMethods.GetFocus());
                Assert.AreEqual(body, comparison.BodyTextForTest);
                Assert.AreEqual(fileBar, window.ReferenceComparisonFileBarForTest);
                Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
                Assert.AreEqual(documentCount, window.VisibleEditorTabCountForTest);
                Assert.IsFalse(requestToken.IsCancellationRequested);
                if (pending is not null)
                {
                    result.SetResult(Ready());
                    await pending;
                    await WaitUntilAsync(() => !comparison.LoadingForTest);
                    Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
                    Assert.IsFalse(comparison.LoadingForTest);
                }
                window.CloseActiveTabForTest();
                Assert.IsFalse(window.ReferenceComparisonVisibleForTest);
                Assert.AreEqual(documentCount == 1, window.EmptyDocumentVisibleForTest);
                Assert.AreEqual(documentCount > 1, window.ActiveDocumentVisibleForTest);
                if (documentCount > 1) Assert.AreEqual(Path.Combine(workspace, "a.txt"), window.ActiveDocumentPathForTest);
            }
            finally
            {
                result.TrySetResult(Ready());
                if (pending is not null) await pending;
            }
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 关闭当前普通标签选择相邻文件且最后一个关闭后显示空页(bool closeButton) => RunAsync(async (window, workspace) =>
    {
        foreach (string name in FileNames)
            await window.OpenDocumentForTestAsync(Path.Combine(workspace, name));
        Assert.IsTrue(await window.LeftClickDocumentTabForTestAsync(1));
        Assert.IsTrue(closeButton ? window.ClickDocumentTabCloseForTest(1) : window.MiddleClickDocumentTabForTest(1));
        Assert.AreEqual(Path.Combine(workspace, "c.txt"), window.ActiveDocumentPathForTest);
        Assert.IsTrue(window.ActiveDocumentVisibleForTest);
        window.CloseActiveTabForTest();
        Assert.AreEqual(Path.Combine(workspace, "a.txt"), window.ActiveDocumentPathForTest);
        window.CloseActiveTabForTest();
        Assert.AreEqual(0, window.VisibleEditorTabCountForTest);
        Assert.IsNull(window.ActiveDocumentPathForTest);
        Assert.IsTrue(window.EmptyDocumentVisibleForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 关闭叉按下后移出或取消捕获不关闭文件也不污染下一次点击(bool cancelCapture) =>
        RunAsync(async (window, workspace) =>
        {
            await window.OpenDocumentForTestAsync(Path.Combine(workspace, "a.txt"));
            await window.OpenDocumentForTestAsync(Path.Combine(workspace, "b.txt"));
            nint focus = NativeMethods.GetFocus();
            Assert.IsFalse(window.ClickDocumentTabCloseForTest(0,
                () => Assert.AreEqual(focus, NativeMethods.GetFocus()),
                releaseOutside: !cancelCapture, cancelCapture: cancelCapture));
            Assert.AreEqual(2, window.OpenDocumentCount);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.AreEqual((nint)0, NativeMethods.GetCapture());
            Assert.IsTrue(window.ClickDocumentTabCloseForTest(0));
            Assert.AreEqual(1, window.OpenDocumentCount);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
        });

    private static GitComparisonResult Ready() => GitComparisonResult.Success(new(
        GitDiffContentStatus.Ready, "HEAD~1", "HEAD", "b.txt", "@@ -1 +1 @@\n-旧正文\n+新正文\n"));

    [TestMethod]
    [DataRow(false, "b.txt")]
    [DataRow(false, "missing.txt")]
    [DataRow(false, "invalid.json")]
    [DataRow(true, "b.txt")]
    [DataRow(true, "missing.txt")]
    [DataRow(true, "invalid.json")]
    public Task 普通文件晚到结果不得遮住新建或重新激活的比较(bool existingComparison, string fileName) =>
        RunAsync(async (window, workspace) =>
        {
            await window.OpenDocumentForTestAsync(Path.Combine(workspace, "a.txt"));
            if (existingComparison)
            {
                window.ShowHistoryComparisonForTest(Ready());
                await WaitUntilAsync(() => !window.ComparisonViewForTest!.LoadingForTest);
            }
            TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
            window.DocumentReadBarrierForTest = barrier.Task;
            string path = Path.Combine(workspace, fileName);
            Task pending = window.OpenDocumentForTestAsync(path);
            try
            {
                Assert.IsFalse(pending.IsCompleted, "前置必须停在真实文件读取之前。");
                if (existingComparison) Assert.IsTrue(window.ClickReferenceComparisonTabForTest());
                else window.ShowHistoryComparisonForTest(Ready());
                NativeGitComparisonView comparison = window.ComparisonViewForTest!;
                await WaitUntilAsync(() => !comparison.LoadingForTest);
                nint focus = NativeMethods.GetFocus();
                string body = comparison.BodyTextForTest;
                int layouts = window.LayoutInvocationCountForTest;
                window.SetStatusForTest(UiText.PathCopied);

                barrier.SetResult();
                await pending;

                Assert.IsTrue(window.ReferenceComparisonVisibleForTest);
                Assert.IsFalse(window.ActiveDocumentVisibleForTest, "普通文件晚到后不得盖在比较正文上。");
                Assert.IsFalse(window.EmptyDocumentVisibleForTest);
                Assert.AreEqual(focus, NativeMethods.GetFocus());
                Assert.AreEqual(body, comparison.BodyTextForTest);
                Assert.AreEqual(layouts, window.LayoutInvocationCountForTest, "文件就绪只能布局自己的视图。");
                Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
                Assert.AreEqual(2, window.LoadedDocumentCountForTest);
                Assert.IsTrue(await window.LeftClickDocumentTabForTestAsync(1));
                Assert.AreEqual(path, window.ActiveDocumentPathForTest);
                Assert.IsTrue(window.ActiveDocumentVisibleForTest);
                Assert.AreEqual(2, window.LoadedDocumentCountForTest);
            }
            finally
            {
                barrier.TrySetResult();
                await pending;
            }
        });

    [TestMethod]
    public Task 普通文件乱序完成保留最后选择的文件查找框和局部状态() => RunAsync(async (window, workspace) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "a.txt"));
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = barrier.Task;
        Task pending = window.OpenDocumentForTestAsync(Path.Combine(workspace, "b.txt"));
        try
        {
            string current = Path.Combine(workspace, "c.txt");
            await window.OpenDocumentForTestAsync(current);
            window.ShowActiveDocumentFindForTest("保留输入");
            nint focus = NativeMethods.GetFocus();
            int layouts = window.LayoutInvocationCountForTest;
            window.SetStatusForTest(UiText.PathCopied);
            barrier.SetResult();
            await pending;
            Assert.AreEqual(current, window.ActiveDocumentPathForTest);
            Assert.IsTrue(window.ActiveDocumentVisibleForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.AreEqual("保留输入", NativeMethods.GetWindowTextValue(focus));
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        }
        finally
        {
            barrier.TrySetResult();
            await pending;
        }
    });

    [TestMethod]
    public Task 文件读取期间单击其他树行只保留新选择和焦点() => RunAsync(async (window, workspace) =>
    {
        string requested = Path.Combine(workspace, "b.txt");
        string selected = Path.Combine(workspace, "c.txt");
        Assert.IsTrue(await window.ClickTreePathForTestAsync(requested));
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = barrier.Task;
        Task<bool> pending = window.ActivateSelectedTreeNodeWithEnterForTestAsync();
        try
        {
            await WaitUntilAsync(() => window.DocumentReadBarrierForTest is null);
            Assert.IsTrue(await window.ClickTreePathForTestAsync(selected));
            nint focus = NativeMethods.GetFocus();
            int layouts = window.LayoutInvocationCountForTest;
            barrier.SetResult();
            Assert.IsTrue(await pending);
            await WaitUntilAsync(() => window.ActiveDocumentVisibleForTest);
            Assert.AreEqual(requested, window.ActiveDocumentPathForTest);
            Assert.AreEqual(selected, window.SelectedTreePathForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.IsTrue(window.ProjectTreeHasFocusForTest);
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.AreEqual(1, window.LoadedDocumentCountForTest);
        }
        finally
        {
            barrier.TrySetResult();
            await pending;
        }
    });

    [TestMethod]
    public Task 文件读取期间滚动项目树保持视口和焦点() => RunAsync(async (window, workspace) =>
    {
        string requested = Path.Combine(workspace, "b.txt");
        Assert.IsTrue(await window.ClickTreePathForTestAsync(requested));
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = barrier.Task;
        Task pending = window.OpenSelectedTreeNodeForTestAsync();
        nint tree = window.FileTreeHandleForTest;
        try
        {
            nint firstBefore = NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewFirstVisible, 0);
            _ = NativeMethods.SendMessage(tree, NativeMethods.WindowMessageVerticalScroll, 7, 0);
            nint firstAfter = NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewFirstVisible, 0);
            Assert.AreNotEqual(firstBefore, firstAfter, "前置必须实际滚动项目树。");
            Assert.AreEqual(requested, window.SelectedTreePathForTest);
            nint focus = NativeMethods.GetFocus();
            barrier.SetResult();
            await pending;
            Assert.IsTrue(window.ActiveDocumentVisibleForTest);
            Assert.AreEqual(firstAfter, NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewFirstVisible, 0));
            Assert.AreEqual(focus, NativeMethods.GetFocus());
        }
        finally
        {
            barrier.TrySetResult();
            await pending;
        }
    }, extraFiles: 80);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 文件读取期间关闭前面的后台标签仍按文档身份显示(bool closeButton) => RunAsync(async (window, workspace) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "a.txt"));
        string requested = Path.Combine(workspace, "b.txt");
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = barrier.Task;
        Task pending = window.OpenDocumentForTestAsync(requested);
        try
        {
            Assert.IsTrue(closeButton ? window.ClickDocumentTabCloseForTest(0) : window.MiddleClickDocumentTabForTest(0));
            barrier.SetResult();
            await pending;
            Assert.AreEqual(1, window.OpenDocumentCount);
            Assert.AreEqual(1, window.LoadedDocumentCountForTest);
            Assert.AreEqual(requested, window.ActiveDocumentPathForTest);
            Assert.IsTrue(window.ActiveDocumentVisibleForTest);
            Assert.IsFalse(window.EmptyDocumentVisibleForTest);
        }
        finally
        {
            barrier.TrySetResult();
            await pending;
        }
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 关闭尚在读取的文件后晚到结果不创建视图或恢复标签(bool hasOtherDocument) => RunAsync(async (window, workspace) =>
    {
        if (hasOtherDocument) await window.OpenDocumentForTestAsync(Path.Combine(workspace, "a.txt"));
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = barrier.Task;
        Task pending = window.OpenDocumentForTestAsync(Path.Combine(workspace, "b.txt"));
        try
        {
            Assert.IsTrue(window.ClickDocumentTabCloseForTest(hasOtherDocument ? 1 : 0));
            nint focus = NativeMethods.GetFocus();
            int layouts = window.LayoutInvocationCountForTest;
            window.SetStatusForTest(UiText.PathCopied);
            barrier.SetResult();
            await pending;
            Assert.AreEqual(hasOtherDocument ? 1 : 0, window.OpenDocumentCount);
            Assert.AreEqual(hasOtherDocument ? 1 : 0, window.LoadedDocumentCountForTest);
            Assert.AreEqual(hasOtherDocument, window.ActiveDocumentVisibleForTest);
            Assert.AreEqual(!hasOtherDocument, window.EmptyDocumentVisibleForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        }
        finally
        {
            barrier.TrySetResult();
            await pending;
        }
    });

    [TestMethod]
    public Task 关闭比较返回尚在读取的文件保持读取占位() => RunAsync(async (window, workspace) =>
    {
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = barrier.Task;
        Task pending = window.OpenDocumentForTestAsync(Path.Combine(workspace, "b.txt"));
        try
        {
            window.ShowHistoryComparisonForTest(Ready());
            await WaitUntilAsync(() => !window.ComparisonViewForTest!.LoadingForTest);
            window.CloseActiveTabForTest();
            Assert.IsTrue(window.EmptyDocumentVisibleForTest);
            Assert.AreEqual(UiText.ReadingFile, window.EmptyDocumentTextForTest);
            barrier.SetResult();
            await pending;
            await WaitUntilAsync(() => window.ActiveDocumentVisibleForTest);
            Assert.AreEqual(Path.Combine(workspace, "b.txt"), window.ActiveDocumentPathForTest);
            Assert.IsTrue(window.ActiveDocumentVisibleForTest);
            Assert.IsFalse(window.EmptyDocumentVisibleForTest);
        }
        finally
        {
            barrier.TrySetResult();
            await pending;
        }
    });

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task 恢复文件读取期间的新操作不被恢复收尾覆盖(int action) => RunAsync(async (window, workspace) =>
    {
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = barrier.Task;
        Task<bool> pending = window.OpenWorkspaceAsync(workspace, restoreState: true);
        try
        {
            await WaitUntilAsync(() => window.DocumentReadBarrierForTest is null);
            if (action == 0)
            {
                window.ShowHistoryComparisonForTest(Ready());
                await WaitUntilAsync(() => !window.ComparisonViewForTest!.LoadingForTest);
            }
            else if (action == 1)
            {
                Assert.IsTrue(await window.ClickTreePathForTestAsync(Path.Combine(workspace, "c.txt")));
            }
            else
            {
                await window.OpenDocumentForTestAsync(Path.Combine(workspace, "c.txt"));
                window.ShowActiveDocumentFindForTest("保留恢复期间的输入");
            }
            nint focus = NativeMethods.GetFocus();
            string? selected = window.SelectedTreePathForTest;
            window.SetStatusForTest(UiText.PathCopied);
            barrier.SetResult();
            Assert.IsTrue(await pending);
            Assert.AreEqual(action == 0, window.ReferenceComparisonVisibleForTest);
            Assert.AreEqual(action != 0, window.ActiveDocumentVisibleForTest);
            if (action != 0)
                Assert.AreEqual(Path.Combine(workspace, action == 1 ? "b.txt" : "c.txt"), window.ActiveDocumentPathForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus(), "恢复收尾不得重新激活正文。");
            Assert.AreEqual(selected, window.SelectedTreePathForTest);
            Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
            Assert.AreEqual(3, window.OpenDocumentCount);
            Assert.AreEqual(action == 2 ? 2 : 1, window.LoadedDocumentCountForTest);
            if (action == 2) Assert.AreEqual("保留恢复期间的输入", NativeMethods.GetWindowTextValue(focus));
        }
        finally
        {
            barrier.TrySetResult();
            await pending;
        }
    }, restoreState: true);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 图片解码期间可切到其他文件查找且隐藏或关闭后的结果不抢占(bool close) => RunAsync(async (window, workspace) =>
    {
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WicBitmap? decoded = null;
        window.ImageDecoderForTest = new(path =>
        {
            decoded = WicBitmapLoader.Load(path);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) { decoded.Dispose(); throw new InvalidOperationException("图片解码等待超时。"); }
            return decoded;
        });
        Task pending = window.OpenDocumentForTestAsync(Path.Combine(workspace, "sample.png"));
        NativeDocumentView? imageView = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            imageView = window.ActiveDocumentViewForTest!;
            Assert.IsNotNull(imageView);
            await window.OpenDocumentForTestAsync(Path.Combine(workspace, "b.txt"));
            window.ShowActiveDocumentFindForTest("保留图片加载期间的输入");
            int layouts = window.LayoutInvocationCountForTest;
            nint focus = NativeMethods.GetFocus();
            Assert.IsTrue(window.ActiveFindEditHasFocusForTest);
            window.SetStatusForTest(UiText.PathCopied);
            if (close)
            {
                Assert.IsTrue(window.MiddleClickDocumentTabForTest(0));
                await pending.WaitAsync(TimeSpan.FromSeconds(3));
            }
            release.Set();
            await pending;
            await imageView.ImageWorkersForTest;
            Assert.AreEqual(Path.Combine(workspace, "b.txt"), window.ActiveDocumentPathForTest);
            Assert.IsTrue(window.ActiveDocumentVisibleForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.AreEqual("保留图片加载期间的输入", NativeMethods.GetWindowTextValue(focus));
            Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            if (close) Assert.AreEqual(0, decoded!.Handle);
            else Assert.IsTrue(imageView.IsImagePreviewReady);
        }
        finally
        {
            release.Set();
            await pending;
            if (imageView is not null) await imageView.ImageWorkersForTest;
        }
    }, imageSample: true);

    private static async Task RunAsync(Func<MainWindow, string, Task> scenario, bool restoreState = false, int extraFiles = 0, bool imageSample = false)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        if (imageSample) File.Copy(NativeImagePreviewTests.SamplePath(), Path.Combine(workspace, "sample.png"));
        foreach (string name in FileNames)
            await File.WriteAllTextAsync(Path.Combine(workspace, name),
                string.Join('\n', Enumerable.Range(1, 160).Select(line => $"第 {line} 行：检查文件标签上下文保持。")));
        await File.WriteAllTextAsync(Path.Combine(workspace, "invalid.json"), "{\"未完成\":}");
        for (int index = 0; index < extraFiles; index++)
            await File.WriteAllTextAsync(Path.Combine(workspace, $"extra-{index:D3}.txt"), "项目树滚动样本");
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            try
            {
                ApplicationSettings settings = restoreState ? new()
                {
                    LastWorkspace = workspace,
                    OpenFiles = FileNames.Select(name => Path.Combine(workspace, name)).ToArray(),
                    ActiveFile = Path.Combine(workspace, "b.txt"),
                    ExpandedDirectories = [workspace],
                } : new();
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), settings);
                Volatile.Write(ref active, window);
                window.Show();
                async Task VerifyAsync()
                {
                    try
                    {
                        if (!restoreState) Assert.IsTrue(await window.OpenWorkspaceAsync(workspace));
                        await scenario(window, workspace);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { window.Close(); }
                }
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Volatile.Write(ref active, null);
                if (failure is null) completion.TrySetResult();
                else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "文件标签交互测试窗口没有退出。");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsLessThan(5000, timeout.ElapsedMilliseconds, "文件标签状态等待超时。");
            await Task.Delay(10);
        }
    }
}
