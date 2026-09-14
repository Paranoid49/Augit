using System.Diagnostics;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeGitComparisonInteractionTests
{
    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public Task 比较内边距和长行号随等宽字体调整且不重新加载(int dpi, string theme) => RunAsync(async (window, view, reload) =>
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        view.SetBounds(0, NativeTheme.Scale(100), NativeTheme.Scale(640), NativeTheme.Scale(400));
        view.ApplyAppearance(new() { Theme = theme });
        string patch = "@@ -99990,100 +99990,100 @@\n-旧正文\n+新正文\n"
            + string.Join('\n', Enumerable.Range(1, 99).Select(index => $" 上下文 {index}")) + "\n";
        view.SetResult(GitComparisonResult.Success(new(GitDiffContentStatus.Ready, "HEAD~1", "HEAD", "长行号.txt", patch)));
        await WaitUntilAsync(() => !view.LoadingForTest);
        int renders = view.RenderCountForTest;
        string body = view.BodyTextForTest;
        nint[] handles = view.TextHandlesForTest;
        _ = NativeMethods.SetFocus(view.ToolbarButtonForTest(2));
        foreach (int size in ComparisonCodeSizes)
        {
            view.ApplyAppearance(new() { Theme = theme, FontSize = size });
            foreach (int index in ComparisonBodyIndexes)
            {
                int padding = NativeTheme.Scale(index == 2 ? 7 : 13);
                Assert.AreEqual(padding, (int)NativeMethods.SendMessage(handles[index], 2156, 0, 0));
                Assert.AreEqual(padding, (int)NativeMethods.SendMessage(handles[index], 2158, 0, 0));
                Assert.AreEqual(padding, (int)NativeMethods.SendMessage(handles[index], 2164, 0, 0), "首字符须从真实内边距开始绘制。");
            }
            Assert.IsTrue(NativeMethods.GetClientRectangle(handles[2], out var gutter));
            int end = view.GutterTextForTest.IndexOfAny(['\r', '\n']);
            int lastX = (int)NativeMethods.SendMessage(handles[2], 2164, 0, end);
            Assert.IsLessThanOrEqualTo(gutter.Right, lastX + NativeTheme.Scale(7), "较长行号不得被固定中栏裁切。");
            NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(view.FileBarHandleForTest, handles[3], true, topPadding: 8);
            Assert.AreEqual(view.ToolbarButtonForTest(2), NativeMethods.GetFocus());
            Assert.AreEqual(body, view.BodyTextForTest);
            Assert.AreEqual(renders, view.RenderCountForTest);
            Assert.IsEmpty(reload.Calls);
        }
        view.ClickModeForTest(false);
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual(NativeTheme.Scale(13), (int)NativeMethods.SendMessage(handles[0], 2164, 0, 0));
        NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(view.FileBarHandleForTest, handles[0], false, topPadding: 8);
    });

    private static readonly int[] ComparisonCodeSizes = [40, 19, 13];
    private static readonly int[] ComparisonBodyIndexes = [1, 2, 3];

    [TestMethod]
    [DataRow(96, false, 640)]
    [DataRow(96, true, 960)]
    [DataRow(120, false, 640)]
    [DataRow(120, true, 960)]
    [DataRow(144, false, 640)]
    [DataRow(144, true, 960)]
    public Task 比较工具栏计数位于右侧操作之前且单双栏顺序一致(int dpi, bool sideBySide, int width) => RunAsync(async (window, view, reload) =>
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        view.ApplyAppearance();
        view.SetBounds(0, NativeTheme.Scale(100), NativeTheme.Scale(width), NativeTheme.Scale(400));
        view.SetResult(Ready("计数.txt"));
        view.ClickModeForTest(sideBySide);
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual((NativeTheme.Scale(13), NativeTheme.Scale(13)), view.TextPaddingForTest,
            "比较正文必须保留视觉稿规定的左右内边距。");
        Assert.AreEqual("1 处差异", view.ChangeSummaryForTest);
        Assert.IsTrue(NativeMethods.GetWindowRectangle(view.Handle, out var container));
        nint[] ordered = [view.ToolbarButtonForTest(0), view.ToolbarButtonForTest(1), view.ToolbarButtonForTest(2),
            view.ChangeSummaryHandleForTest, view.ToolbarButtonForTest(5), view.ToolbarButtonForTest(4),
            view.ToolbarButtonForTest(3), view.ToolbarButtonForTest(6)];
        int previousRight = container.Left;
        foreach (nint control in ordered)
        {
            Assert.IsTrue(NativeMethods.GetWindowRectangle(control, out var rectangle));
            Assert.IsTrue(rectangle.Left >= previousRight && rectangle.Right <= container.Right,
                "工具栏顺序错误、按钮重叠或越过比较区域。");
            Assert.IsGreaterThan(rectangle.Left, rectangle.Right);
            previousRight = rectangle.Right;
        }
        Assert.AreEqual(0L, NativeMethods.GetWindowLongPointer(view.ChangeSummaryHandleForTest,
            NativeMethods.WindowLongStyle).ToInt64() & NativeMethods.WindowStyleTabStop);
        Assert.AreEqual(NativeTheme.UiFont, NativeMethods.SendMessage(view.ChangeSummaryHandleForTest, 0x0031, 0, 0));
        NativeDiffToolbarAssertions.AssertGroup(view.Handle, view.ModeGroupBoundsForTest,
            view.ToolbarButtonForTest(4), view.ToolbarButtonForTest(3));
        Assert.AreEqual(sideBySide, view.FileHeaderSideBySideForTest);
        NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(view.FileBarHandleForTest,
            view.TextHandlesForTest[sideBySide ? 3 : 0], sideBySide, topPadding: 8);
    });

    [TestMethod]
    public Task 比较计数只属于有效正文并在慢查询失败取消及关闭时清除() => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Ready("计数.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual("1 处差异", view.ChangeSummaryForTest);
        view.ClickIgnoreWhitespaceForTest();
        Assert.AreEqual(string.Empty, view.ChangeSummaryForTest);
        await WaitUntilAsync(() => view.LoadingTextForTest.Length > 0);
        Assert.IsFalse(NativeMethods.IsWindowVisible(view.ChangeSummaryHandleForTest));
        reload.Calls[0].Completion.SetResult(Failure());
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual(string.Empty, view.ChangeSummaryForTest);
        view.SetResult(Ready("重试.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual("1 处差异", view.ChangeSummaryForTest);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<GitComparisonResult> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task pending = view.LoadAsync(Ready("取消.txt").Document!, _ => response.Task, cancellation.Token);
        cancellation.Cancel();
        await pending;
        response.SetResult(Ready("已取消的旧结果.txt"));
        Assert.AreEqual(string.Empty, view.ChangeSummaryForTest);
        view.Clear();
        Assert.AreEqual(string.Empty, view.ChangeSummaryForTest);
    });

    [TestMethod]
    public Task 失败说明使用界面字体并在重试后释放局部提示焦点() => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Failure());
        nint notice = view.NoticeHandleForTest;
        Assert.IsTrue(NativeMethods.IsWindowVisible(notice));
        Assert.AreEqual("无法读取比较内容。", NativeMethods.GetWindowTextValue(notice));
        Assert.IsTrue(view.TextHandlesForTest.All(handle => !NativeMethods.IsWindowVisible(handle)));
        Assert.AreEqual(NativeTheme.UiFont, NativeMethods.SendMessage(notice, 0x0031, 0, 0));
        _ = NativeMethods.SetFocus(notice);
        int layouts = view.LayoutCountForTest;
        view.SetResult(Ready("重试.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsFalse(NativeMethods.IsWindowVisible(notice));
        Assert.IsTrue(view.TextHandlesForTest.Contains(NativeMethods.GetFocus()));
        Assert.AreEqual(layouts, view.LayoutCountForTest);
        view.Clear();
        Assert.AreEqual(string.Empty, NativeMethods.GetWindowTextValue(notice));
    });

    [TestMethod]
    public Task 显示缩写不会改变重查参数且文件栏保留完整身份() => RunAsync(async (window, view, reload) =>
    {
        const string hash = "0123456789abcdef0123456789abcdef01234567";
        GitComparisonDocument document = new(GitDiffContentStatus.Ready, hash + "^", hash,
            "docs/很长的比较文件名.txt", "@@ -1 +1 @@\n-旧内容\n+新内容\n");
        view.SetResult(GitComparisonResult.Success(document));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual(("01234567^", "01234567"), view.DisplayedRevisionsForTest);
        StringAssert.Contains(view.FileBarTextForTest, hash + "^");
        StringAssert.Contains(view.FileBarTextForTest, document.RelativePath!);
        Assert.IsTrue(view.FileBarToolTipCreatedForTest);
        view.ClickIgnoreWhitespaceForTest();
        Assert.AreEqual(document, reload.Calls[0].Document);
        reload.Calls[0].Completion.SetResult(GitComparisonResult.Success(document));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual(("01234567^", "01234567"), view.DisplayedRevisionsForTest);
    });

    [TestMethod]
    [DataRow(GitDiffContentStatus.Binary)]
    [DataRow(GitDiffContentStatus.SideTooLarge)]
    [DataRow(GitDiffContentStatus.OutputTooLarge)]
    [DataRow(GitDiffContentStatus.Ready)]
    public Task 正文切换摘要后清除导航与隐藏正文(GitDiffContentStatus status) => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Ready("甲.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual(1, view.ChangedLineCountForTest);
        Assert.IsTrue(view.NavigationEnabledForTest);
        view.ClickChangeForTest(1);
        view.SetResult(GitComparisonResult.Success(new(status, "HEAD~1", "HEAD", "摘要.bin", null)));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual(0, view.ChangedLineCountForTest);
        Assert.IsFalse(view.NavigationEnabledForTest);
        string summary = view.BodyTextForTest;
        string globalStatus = window.StatusTextForTest;
        view.ClickChangeForTest(1);
        view.ClickChangeForTest(-1);
        Assert.AreEqual(summary, view.BodyTextForTest);
        Assert.AreEqual(globalStatus, window.StatusTextForTest);
        foreach (nint text in view.TextHandlesForTest.Skip(1))
        {
            Assert.AreEqual(0, NativeMethods.SendMessage(text, 2183, 0, 0));
        }
    });

    [TestMethod]
    public Task 重复点击当前模式保持阅读位置且切换模式不查询Git() => RunAsync(async (window, view, reload) =>
    {
        GitComparisonResult result = Ready("长文件.txt", 100);
        view.SetResult(result);
        await WaitUntilAsync(() => !view.LoadingForTest);
        nint body = view.TextHandlesForTest[3];
        _ = NativeMethods.SendMessage(body, 2024, 70, 0);
        nint position = NativeMethods.SendMessage(body, 2143, 0, 0);
        nint top = NativeMethods.SendMessage(body, 2152, 0, 0);
        int renders = view.RenderCountForTest;
        int layouts = view.LayoutCountForTest;
        view.ClickModeForTest(true);
        view.SetResult(result);
        Assert.IsFalse(view.LoadingForTest);
        Assert.AreEqual(renders, view.RenderCountForTest);
        Assert.AreEqual(top, NativeMethods.SendMessage(body, 2152, 0, 0));
        Assert.AreEqual(position, NativeMethods.SendMessage(body, 2143, 0, 0));
        view.ClickModeForTest(false);
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsFalse(view.UsesSideBySideForTest);
        StringAssert.Contains(view.BodyTextForTest, "长文件.txt");
        Assert.IsEmpty(reload.Calls);
        Assert.AreEqual(layouts, view.LayoutCountForTest, "正文模式切换只能重排文件信息与正文，不能重排工具栏。");
    });

    [TestMethod]
    public Task 排版期间切换模式以最终模式完成且保留其他全局提示() => RunAsync(async (window, view, reload) =>
    {
        TaskCompletionSource oldRender = new(TaskCreationOptions.RunContinuationsAsynchronously);
        view.RenderBarrierForTest = oldRender.Task;
        view.SetResult(Ready("甲.txt"));
        await WaitUntilAsync(() => view.RenderBarrierForTest is null);
        TaskCompletionSource finalRender = new(TaskCreationOptions.RunContinuationsAsynchronously);
        view.RenderBarrierForTest = finalRender.Task;
        view.ClickModeForTest(false);
        await WaitUntilAsync(() => view.RenderBarrierForTest is null);
        await WaitUntilAsync(() => view.LoadingTextForTest.Length > 0);
        oldRender.SetResult();
        window.SetStatusForTest(UiText.PathCopied);
        Assert.IsTrue(view.LoadingForTest);
        finalRender.SetResult();
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsFalse(view.UsesSideBySideForTest);
        StringAssert.Contains(view.BodyTextForTest, "甲.txt");
        Assert.AreEqual(string.Empty, view.LoadingTextForTest);
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
    });

    [TestMethod]
    public Task 忽略空白查询期间切换模式不重复查询并按最终模式显示() => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Ready("甲.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        int layouts = view.LayoutCountForTest;
        view.ClickIgnoreWhitespaceForTest();
        view.ClickModeForTest(false);
        view.ClickModeForTest(true);
        view.ClickModeForTest(false);
        Assert.HasCount(1, reload.Calls);
        Assert.IsTrue(view.LoadingForTest);
        Assert.IsTrue(view.IgnoreWhitespaceForTest);
        Assert.IsFalse(view.NavigationEnabledForTest);
        Assert.IsTrue(view.FileHeaderSideBySideForTest, "旧正文保留期间文件信息不能先跳到新模式。");
        reload.Calls[0].Completion.SetResult(Ready("过滤结果.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsFalse(view.UsesSideBySideForTest);
        StringAssert.Contains(view.BodyTextForTest, "过滤结果.txt");
        Assert.IsFalse(view.FileHeaderSideBySideForTest);
        NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(view.FileBarHandleForTest, view.TextHandlesForTest[0], false, topPadding: 8);
        Assert.IsTrue(view.NavigationEnabledForTest);
        Assert.AreEqual(layouts, view.LayoutCountForTest);
    });

    [TestMethod]
    public Task 比较文件信息在失败后仍可切换且不重查或抢焦点() => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Ready("文件.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        view.ClickIgnoreWhitespaceForTest();
        view.ClickModeForTest(false);
        Assert.IsTrue(view.FileHeaderSideBySideForTest);
        reload.Calls[0].Completion.SetResult(Failure());
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsFalse(view.FileHeaderSideBySideForTest);
        NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(view.FileBarHandleForTest, view.NoticeHandleForTest, false);
        nint button = view.ToolbarButtonForTest(4);
        _ = NativeMethods.SetFocus(button);
        view.ClickModeForTest(true);
        Assert.IsTrue(view.FileHeaderSideBySideForTest);
        Assert.AreEqual(button, NativeMethods.GetFocus());
        Assert.HasCount(1, reload.Calls);
        NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(view.FileBarHandleForTest, view.NoticeHandleForTest, true);
    });

    [TestMethod]
    public Task 连续切换空白选项按最后意图处理且忽略晚到结果() => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Ready("甲.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        view.ClickIgnoreWhitespaceForTest();
        view.ClickIgnoreWhitespaceForTest();
        Assert.HasCount(2, reload.Calls);
        Assert.IsTrue(reload.Calls[0].IgnoreWhitespace);
        Assert.IsFalse(reload.Calls[1].IgnoreWhitespace);
        Assert.IsTrue(reload.Calls[0].Token.IsCancellationRequested);
        reload.Calls[1].Completion.SetResult(Ready("最终.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        reload.Calls[0].Completion.SetResult(Ready("过期.txt"));
        await WaitUntilAsync(() => reload.Returned == 2);
        Assert.IsFalse(view.IgnoreWhitespaceForTest);
        StringAssert.Contains(view.BodyTextForTest, "最终.txt");
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 新比较到达后旧空白查询不得覆盖正文或提示(bool oldFailure) => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Ready("甲.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        view.ClickIgnoreWhitespaceForTest();
        view.SetResult(Ready("乙.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsTrue(reload.Calls[0].Token.IsCancellationRequested);
        window.SetStatusForTest(UiText.PathCopied);
        reload.Calls[0].Completion.SetResult(oldFailure ? Failure() : Ready("过期.txt"));
        await WaitUntilAsync(() => reload.Returned == 1);
        StringAssert.Contains(view.FileBarTextForTest, "乙.txt");
        StringAssert.Contains(view.BodyTextForTest, "乙.txt");
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        Assert.IsFalse(view.IgnoreWhitespaceForTest);
    });

    [TestMethod]
    public Task 慢查询失败只在当前比较显示原因且清除旧导航() => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Ready("甲.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        window.SetStatusForTest(UiText.PathCopied);
        view.ClickIgnoreWhitespaceForTest();
        await WaitUntilAsync(() => view.LoadingTextForTest.Length > 0);
        reload.Calls[0].Completion.SetResult(Failure());
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual("无法读取比较内容。", view.BodyTextForTest);
        Assert.AreEqual(string.Empty, view.LoadingTextForTest);
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        Assert.IsFalse(view.NavigationEnabledForTest);
        Assert.IsFalse(view.IgnoreWhitespaceForTest);
        view.ClickModeForTest(false);
        Assert.AreEqual("无法读取比较内容。", view.BodyTextForTest, "仅切换布局不能把旧补丁冒充查询成功。");
        view.ClickIgnoreWhitespaceForTest();
        reload.Calls[1].Completion.SetResult(Ready("重试成功.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.IsTrue(view.NavigationEnabledForTest);
    });

    [TestMethod]
    public Task 新失败使旧排版失效而不会重新显示旧正文() => RunAsync(async (window, view, reload) =>
    {
        TaskCompletionSource render = new(TaskCreationOptions.RunContinuationsAsynchronously);
        view.RenderBarrierForTest = render.Task;
        view.SetResult(Ready("过期.txt"));
        await WaitUntilAsync(() => view.RenderBarrierForTest is null);
        view.SetResult(Failure());
        render.SetResult();
        await Task.Yield();
        Assert.AreEqual("无法读取比较内容。", view.BodyTextForTest);
        Assert.IsFalse(view.LoadingForTest);
        Assert.IsFalse(view.NavigationEnabledForTest);
    });

    [TestMethod]
    public Task 销毁比较窗口后晚到查询不访问原生控件或全局状态() => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Ready("甲.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        view.ClickIgnoreWhitespaceForTest();
        nint[] handles = view.TextHandlesForTest;
        view.Dispose();
        Assert.IsTrue(reload.Calls[0].Token.IsCancellationRequested);
        window.SetStatusForTest(UiText.PathCopied);
        reload.Calls[0].Completion.SetResult(Failure());
        await WaitUntilAsync(() => reload.Returned == 1);
        Assert.IsFalse(view.HasDocument);
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        Assert.IsTrue(handles.All(handle => !NativeMethods.IsWindow(handle)));
    });

    [TestMethod]
    public Task 一万行引用比较渲染与模式切换期间窗口消息保持响应() => RunAsync(async (window, view, reload) =>
    {
        using CancellationTokenSource stop = new();
        Task<(int Timeouts, double Maximum)> probe = Task.Run(async () =>
        {
            int timeouts = 0;
            double maximum = 0;
            while (!stop.IsCancellationRequested)
            {
                Stopwatch response = Stopwatch.StartNew();
                nint received = SendMessageTimeout(window.Handle, 0, 0, 0, 2, 250, out _);
                maximum = Math.Max(maximum, response.Elapsed.TotalMilliseconds);
                if (received == 0) { timeouts++; }
                await Task.Delay(10);
            }
            return (timeouts, maximum);
        });
        try
        {
            view.SetResult(Ready("一万行.txt", 10_000));
            await WaitUntilAsync(() => !view.LoadingForTest);
            view.ClickModeForTest(false);
            await WaitUntilAsync(() => !view.LoadingForTest);
            Assert.AreEqual(1, view.ChangedLineCountForTest);
        }
        finally
        {
            stop.Cancel();
            (int timeouts, double maximum) = await probe;
            Console.WriteLine($"引用比较一万行正文与模式切换：最大消息响应 {maximum:F2} 毫秒，超时 {timeouts} 次。");
            Assert.AreEqual(0, timeouts, "引用比较渲染不应阻塞主窗口消息超过 250 毫秒。");
        }
    });

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wordParameter,
        nint longParameter, uint flags, uint timeout, out nuint result);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 大比较装饰过程中关闭或切换模式不会让旧时间片恢复正文(bool close) => RunAsync(async (window, view, reload) =>
    {
        view.ClickModeForTest(false);
        view.SetResult(Ready("大文件.txt", 100_000));
        nint unified = view.TextHandlesForTest[0];
        await WaitUntilAsync(() => NativeMethods.SendMessage(unified, 2183, 0, 0) != 0);
        Assert.IsTrue(view.LoadingForTest, "应在正文已写入、装饰仍分批执行时触发用户动作。");
        if (close) { view.Clear(); }
        else { view.ClickModeForTest(true); }
        await WaitUntilAsync(() => !view.LoadingForTest);
        if (close)
        {
            Assert.IsFalse(view.HasDocument);
            Assert.AreEqual(string.Empty, view.BodyTextForTest);
            foreach (nint handle in view.TextHandlesForTest)
                Assert.AreEqual((nint)0, NativeMethods.SendMessage(handle, 2183, 0, 0));
        }
        else
        {
            Assert.IsTrue(view.UsesSideBySideForTest);
            StringAssert.Contains(view.BodyTextForTest, "大文件.txt");
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(unified, 2183, 0, 0));
            Assert.AreEqual(1, view.ChangedLineCountForTest);
        }
    });

    [TestMethod]
    public Task 关闭比较释放补丁正文并使晚到查询失效() => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(Ready("甲.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        view.ClickIgnoreWhitespaceForTest();
        view.Clear();
        Assert.IsTrue(reload.Calls[0].Token.IsCancellationRequested);
        Assert.IsFalse(view.HasDocument);
        Assert.IsFalse(view.LoadingForTest);
        Assert.AreEqual(string.Empty, view.BodyTextForTest);
        foreach (nint text in view.TextHandlesForTest)
        {
            Assert.AreEqual(0, NativeMethods.SendMessage(text, 2183, 0, 0));
        }
        reload.Calls[0].Completion.SetResult(Ready("晚到.txt"));
        await WaitUntilAsync(() => reload.Returned == 1);
        Assert.IsFalse(view.HasDocument);
        Assert.IsFalse(view.IsVisible);
        view.SetResult(Ready("重新打开.txt"));
        view.SetVisible(true);
        await WaitUntilAsync(() => !view.LoadingForTest);
        StringAssert.Contains(view.BodyTextForTest, "重新打开.txt");
    });

    private static GitComparisonResult Ready(string path, int lines = 1) => GitComparisonResult.Success(new(
        GitDiffContentStatus.Ready, "HEAD~1", "HEAD", path,
        $"@@ -0,0 +1,{lines} @@\n" + string.Join('\n', Enumerable.Repeat($"+内容 {path}", lines)) + "\n"));

    private static GitComparisonResult Failure() => GitComparisonResult.Failure(
        GitOperationFailureKind.CommandFailed, "无法读取比较内容。");

    private static async Task RunAsync(Func<MainWindow, NativeGitComparisonView, ControlledReload, Task> scenario, bool preciseDpi = false)
    {
        using TemporaryDirectory temporary = new();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            nint previousDpi = preciseDpi ? SetComparisonThreadDpiContext(-4) : 0;
            nint comparisonOwner = 0;
            try
            {
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
                Volatile.Write(ref active, window);
                window.Show();
                ControlledReload reload = new();
                if (preciseDpi)
                {
                    // 像素测试使用独立窗口，避免主窗口的异步布局遮挡比较正文。
                    comparisonOwner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "比较底色测试",
                        NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
                        0, 0, 800, 650, window.Handle, 0, NativeMethods.GetModuleHandle(null), 0);
                    Assert.AreNotEqual((nint)0, comparisonOwner);
                }
                using NativeGitComparisonView view = new(comparisonOwner != 0 ? comparisonOwner : window.Handle,
                    new(), window.SetStatusForTest, reload.LoadAsync);
                view.SetBounds(0, 100, 800, 500);
                view.SetVisible(true);
                async Task VerifyAsync()
                {
                    try { await scenario(window, view, reload); }
                    catch (Exception exception) { failure = exception; }
                    finally { window.Close(); }
                }
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                if (comparisonOwner != 0) _ = NativeMethods.DestroyWindow(comparisonOwner);
                if (previousDpi != 0) _ = SetComparisonThreadDpiContext(previousDpi);
                Volatile.Write(ref active, null);
                if (failure is null) { completion.TrySetResult(); }
                else { completion.TrySetException(failure); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "引用比较测试窗口没有退出。");
        }
    }

    [DllImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext")]
    private static extern nint SetComparisonThreadDpiContext(nint context);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsLessThan(5000, timeout.ElapsedMilliseconds, "引用比较状态等待超时。");
            await Task.Delay(10);
        }
    }

    private sealed class ControlledReload
    {
        internal sealed record Call(bool IgnoreWhitespace, TaskCompletionSource<GitComparisonResult> Completion, GitComparisonDocument Document, CancellationToken Token);
        internal List<Call> Calls { get; } = [];
        internal int Returned { get; private set; }

        internal async Task<GitComparisonResult> LoadAsync(GitComparisonDocument document, bool ignoreWhitespace, CancellationToken token)
        {
            // 故意允许已取消的服务晚到，验证界面自身不会接纳过期结果。
            TaskCompletionSource<GitComparisonResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Calls.Add(new(ignoreWhitespace, completion, document, token));
            GitComparisonResult result = await completion.Task;
            Returned++;
            return result;
        }
    }
}
