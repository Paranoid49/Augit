using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Documents;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeDocumentFindInteractionTests
{
    private static readonly int[] FindButtons = [12, 13, 14, 9, 10, 11];

    [TestMethod]
    [DataRow(96, 13, "Light")]
    [DataRow(96, 40, "Dark")]
    [DataRow(120, 13, "Dark")]
    [DataRow(120, 40, "Light")]
    [DataRow(144, 13, "Light")]
    [DataRow(144, 40, "Dark")]
    public void 查找开关和窗口收放保持正文起点与实际控件边界一致(int dpi, int size, string theme)
    {
        using ViewScope scope = new("cat xx cat", dpi, size, theme);
        NativeDocumentView view = scope.View;
        nint editor = Child(view.Handle, 100);
        int initialTop = BodyTop();
        Assert.AreEqual(initialTop, view.ContentTopForTest);
        for (int cycle = 0; cycle < 2; cycle++)
        {
            nint edit = scope.Open("cat");
            Assert.AreEqual(initialTop + NativeDocumentView.FindOverlayHeightForTest, BodyTop());
            Assert.AreEqual(BodyTop(), view.ContentTopForTest);
            Assert.AreEqual("1/2", view.FindStatusForTest);
            view.SetBounds(0, 0, NativeTheme.Scale(cycle == 0 ? 700 : 900), NativeTheme.Scale(500));
            Assert.AreEqual(BodyTop(), view.ContentTopForTest);
            Assert.AreEqual(edit, NativeMethods.GetFocus());
            Key(edit, NativeMethods.VirtualKeyEscape);
            Assert.AreEqual(initialTop, BodyTop());
            Assert.AreEqual(BodyTop(), view.ContentTopForTest);
            Assert.AreEqual(editor, NativeMethods.GetFocus());
            Assert.IsTrue(view.IsTextReadOnly);
        }

        int BodyTop()
        {
            Assert.IsTrue(NativeMethods.GetWindowRectangle(editor, out var rectangle));
            NativeMethods.Point point = new() { X = rectangle.Left, Y = rectangle.Top };
            Assert.IsTrue(NativeMethods.ScreenToClient(view.Handle, ref point));
            return point.Y;
        }
    }

    [TestMethod]
    public void 输入即定位并高亮其他匹配且导航复用结果()
    {
        using ViewScope scope = new("cat xx cat yy cat");
        nint edit = scope.Open("cat");
        nint editor = Child(scope.View.Handle, 100);
        Assert.AreEqual("1/3", scope.View.FindStatusForTest);
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor, 2009, 0, 0));
        int scans = scope.View.FindStatusScanCountForTest;
        Key(edit, NativeMethods.VirtualKeyEnter);
        Assert.AreEqual("2/3", scope.View.FindStatusForTest);
        Assert.AreEqual(scans, scope.View.FindStatusScanCountForTest);
        Key(edit, NativeMethods.VirtualKeyEscape);
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor, 2507, 1, 7));
        scope.View.ShowFind();
        Assert.AreEqual("2/3", scope.View.FindStatusForTest);
        Assert.AreEqual((nint)7, NativeMethods.SendMessage(editor, 2009, 0, 0));
    }

    [TestMethod]
    [DataRow("K", "k", false)]
    [DataRow("²cat", "cat", true)]
    [DataRow("catⅣ", "cat", true)]
    public void 零项普通查找不会移动正文选择(string source, string query, bool whole)
    {
        using ViewScope scope = new(source);
        nint edit = scope.Open(query);
        if (whole) Click(scope.View, 13);
        nint editor = Child(scope.View.Handle, 100);
        _ = NativeMethods.SendMessage(editor, 2160, 0, Encoding.UTF8.GetByteCount(source));
        nint anchor = NativeMethods.SendMessage(editor, 2009, 0, 0);
        nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0);
        Assert.AreEqual(UiText.FindResults(0), scope.View.FindStatusForTest);
        foreach (int command in new[] { 10, 9 })
        {
            Click(scope.View, command);
            Assert.AreEqual(anchor, NativeMethods.SendMessage(editor, 2009, 0, 0));
            Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
            Assert.AreEqual(Child(scope.View.Handle, command), NativeMethods.GetFocus());
        }
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 关闭重开保留当前匹配并继续下一项(bool regex)
    {
        using ViewScope scope = new("cat xx cat yy cat");
        nint edit = scope.Open("cat");
        if (regex) Click(scope.View, 14);
        _ = NativeMethods.SetFocus(edit);
        Key(edit, NativeMethods.VirtualKeyEnter);
        NativeFindTestPump.Wait(scope.View);
        nint editor = Child(scope.View.Handle, 100);
        Assert.AreEqual((nint)7, NativeMethods.SendMessage(editor, 2009, 0, 0));
        Assert.AreEqual("2/3", scope.View.FindStatusForTest);
        Key(edit, NativeMethods.VirtualKeyEscape);
        Assert.AreEqual(editor, NativeMethods.GetFocus());
        scope.View.ShowFind();
        Assert.AreEqual(edit, NativeMethods.GetFocus());
        Assert.AreEqual("cat", NativeMethods.GetWindowTextValue(edit));
        Key(edit, NativeMethods.VirtualKeyEnter);
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual((nint)14, NativeMethods.SendMessage(editor, 2009, 0, 0));
        Assert.AreEqual("3/3", scope.View.FindStatusForTest);
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    public void 慢查找不占用输入线程且同一请求不重复扫描()
    {
        using ViewScope scope = new(new string('a', 120_000) + " cat");
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int uiThread = Environment.CurrentManagedThreadId;
        int workerThread = uiThread;
        scope.View.FindWorkBarrierForTest = async token =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            entered.TrySetResult();
            await release.Task;
        };
        try
        {
            scope.View.ShowFindForTest("cat");
            entered.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Assert.AreNotEqual(uiThread, workerThread);
            Assert.AreEqual(string.Empty, scope.View.FindStatusForTest);
            int scans = scope.View.FindStatusScanCountForTest;
            scope.View.ShowFindForTest("cat");
            Assert.AreEqual(scans, scope.View.FindStatusScanCountForTest, "进行中的相同查询也必须去重。");
            nint edit = Child(scope.View.Handle, 20);
            _ = NativeMethods.SetWindowText(edit, "missing");
            Assert.AreEqual("missing", NativeMethods.GetWindowTextValue(edit));
            _ = NativeMethods.SetWindowText(edit, "cat");
            long deadline = Environment.TickCount64 + 2_000;
            while (scope.View.FindStatusForTest != UiText.Searching && Environment.TickCount64 < deadline)
            {
                NativeFindTestPump.Dispatch(scope.View.Handle);
                Thread.Sleep(1);
            }
            Assert.AreEqual(UiText.Searching, scope.View.FindStatusForTest);
            Assert.AreEqual(edit, NativeMethods.GetFocus());
            release.TrySetResult();
            NativeFindTestPump.Wait(scope.View);
            Assert.AreEqual(UiText.FindResults(1), scope.View.FindStatusForTest);
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    [DataRow("cat")]
    [DataRow("[")]
    public void 已投递的旧计数或错误不得覆盖新查询(string initial)
    {
        using ViewScope scope = new("cat cat");
        scope.View.ShowFind();
        Click(scope.View, 14);
        scope.View.ShowFindForTest(initial);
        scope.View.FindWorkersForTest.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        // 保留尚未分发的旧完成消息，再触发新查询，验证界面不接受过期结果。
        scope.View.ShowFindForTest("missing");
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual(UiText.FindResults(0), scope.View.FindStatusForTest);
        Assert.AreEqual("missing", NativeMethods.GetWindowTextValue(Child(scope.View.Handle, 20)));
    }

    [TestMethod]
    [DataRow("close")]
    [DataRow("hide")]
    [DataRow("dispose")]
    public void 关闭隐藏或销毁时取消计数和定位且晚到任务不改变正文(string action)
    {
        using ViewScope scope = new("cat cat cat");
        scope.View.ShowFind();
        Click(scope.View, 14);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.View.FindWorkBarrierForTest = async token => { entered.TrySetResult(); await release.Task; };
        try
        {
            scope.View.ShowFindForTest("cat");
            nint edit = Child(scope.View.Handle, 20);
            Key(edit, NativeMethods.VirtualKeyEnter);
            entered.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            nint editor = Child(scope.View.Handle, 100);
            nint before = NativeMethods.SendMessage(editor, 2008, 0, 0);
            if (action == "close") Key(edit, NativeMethods.VirtualKeyEscape);
            else if (action == "hide") scope.View.SetVisible(false);
            else scope.View.Dispose();
            Assert.IsFalse(scope.View.FindBusyForTest);
            nint focus = NativeMethods.GetFocus();
            release.TrySetResult();
            NativeFindTestPump.Wait(scope.View);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            if (action != "dispose")
            {
                Assert.AreEqual(before, NativeMethods.SendMessage(editor, 2008, 0, 0));
                Assert.AreEqual("cat", NativeMethods.GetWindowTextValue(edit));
                if (action == "hide") scope.View.SetVisible(true); else scope.View.ShowFind();
                NativeFindTestPump.Wait(scope.View);
                Assert.AreEqual(UiText.FindResults(3), scope.View.FindStatusForTest);
            }
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    public void 后台正则连续导航按输入顺序定位且查询改变取消旧方向()
    {
        using ViewScope scope = new("cat x cat x cat");
        scope.View.ShowFind();
        Click(scope.View, 14);
        nint edit = scope.Open("c.t");
        Key(edit, NativeMethods.VirtualKeyEnter);
        Key(edit, NativeMethods.VirtualKeyEnter);
        Key(edit, NativeMethods.VirtualKeyEnter);
        nint previous = Child(scope.View.Handle, 9);
        _ = NativeMethods.SendMessage(previous, 0x00F5, 0, 0);
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual((nint)12, NativeMethods.SendMessage(Child(scope.View.Handle, 100), 2009, 0, 0));
        Assert.AreEqual(previous, NativeMethods.GetFocus());
        _ = NativeMethods.SetFocus(edit);
        Key(edit, NativeMethods.VirtualKeyEnter);
        _ = NativeMethods.SetWindowText(edit, "x");
        Key(edit, NativeMethods.VirtualKeyEnter);
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual((nint)10, NativeMethods.SendMessage(Child(scope.View.Handle, 100), 2009, 0, 0));
        Assert.AreEqual("2/2", scope.View.FindStatusForTest);
    }

    [TestMethod]
    public void 短文本复杂正则也在后台超时且Esc可以立即关闭()
    {
        using ViewScope scope = new(new string('a', 32) + "!");
        scope.View.ShowFind();
        Click(scope.View, 14);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        scope.View.ShowFindForTest("(a+)+$");
        Assert.IsTrue(scope.View.FindBusyForTest, "短文本不能作为正则安全同步执行的依据。");
        Assert.IsLessThan(150d, stopwatch.Elapsed.TotalMilliseconds, "输入不能等待 250ms 正则超时。");
        nint edit = Child(scope.View.Handle, 20);
        Key(edit, NativeMethods.VirtualKeyEnter);
        stopwatch.Restart();
        Key(edit, NativeMethods.VirtualKeyEscape);
        Assert.IsLessThan(150d, stopwatch.Elapsed.TotalMilliseconds, "Esc 不能等待工作线程结束。");
        Assert.IsFalse(scope.View.FindOverlayVisibleForTest);
        Assert.IsFalse(scope.View.FindBusyForTest);
        NativeFindTestPump.Wait(scope.View);
        scope.View.ShowFind();
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual(UiText.FindTimedOut, scope.View.FindStatusForTest);
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    public void 外部正文更新取消旧查找并按新正文统计()
    {
        using ViewScope scope = new(new string('a', 110_000) + " cat");
        scope.View.ShowFindForTest("cat");
        scope.View.FindWorkersForTest.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        scope.View.ReloadAsync(scope.Result with { Text = "cat cat cat" }).GetAwaiter().GetResult();
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual(UiText.FindResults(3), scope.View.FindStatusForTest);
    }

    [TestMethod]
    [DataRow(96, 13, "Light")]
    [DataRow(96, 40, "Dark")]
    [DataRow(120, 40, "Light")]
    [DataRow(144, 40, "Dark")]
    public void 输入焦点边框随焦点更新且边缘点击保留查询选择(int dpi, int size, string theme)
    {
        using ViewScope scope = new("cat cat", dpi, size, theme);
        nint edit = scope.Open("cat");
        nint overlay = Child(scope.View.Handle, 35);
        var bar = Bounds(overlay);
        var input = Bounds(edit);
        int frameHeight = NativeTheme.ContentHeight(30, 4);
        int frameLeft = NativeTheme.Scale(7);
        int frameTop = (bar.Height - frameHeight) / 2;
        int frameRight = Bounds(Child(scope.View.Handle, 12)).X - bar.X - NativeTheme.Scale(3);
        NativeThemePalette palette = NativeTheme.Palette(theme == "Dark");
        int x = (frameLeft + frameRight) / 2;
        Assert.AreEqual(palette.Accent, OverlayPixel(scope.View, x, frameTop), "输入聚焦时边框必须变蓝。");
        Assert.AreEqual(bar.X + frameLeft + NativeTheme.Scale(8), input.X);
        Assert.AreEqual(bar.Y + (bar.Height - input.Height) / 2, input.Y);
        Assert.IsGreaterThanOrEqualTo(Measure("国Ag").Height, input.Height);
        _ = NativeMethods.SendMessage(edit, 0x00B1, 1, 2);
        _ = ValidateRectangle(overlay, 0);
        _ = NativeMethods.SetFocus(Child(scope.View.Handle, 12));
        Assert.IsTrue(GetUpdateRectangle(overlay, 0, false), "焦点离开必须触发边框重绘。");
        Assert.AreEqual(palette.Border, OverlayPixel(scope.View, x, frameTop));
        _ = ValidateRectangle(overlay, 0);
        int clickX = frameLeft + NativeTheme.Scale(2);
        int clickY = frameTop + frameHeight / 2;
        _ = NativeMethods.SendMessage(overlay, NativeMethods.WindowMessageLeftButtonDown, 1, (nint)(clickX | clickY << 16));
        Assert.AreEqual(edit, NativeMethods.GetFocus(), "输入框内边距也必须能够点击聚焦。");
        Assert.IsTrue(GetUpdateRectangle(overlay, 0, false));
        Assert.AreEqual("cat", NativeMethods.GetWindowTextValue(edit));
        Assert.AreEqual((nint)(1 | 2 << 16), NativeMethods.SendMessage(edit, 0x00B0, 0, 0));
        Assert.AreEqual(input, Bounds(edit), "焦点切换不能移动输入内容。");
        Assert.AreEqual(palette.Accent, OverlayPixel(scope.View, x, frameTop));
        _ = NativeMethods.SetFocus(Child(scope.View.Handle, 12));
        _ = NativeMethods.SendMessage(overlay, NativeMethods.WindowMessageLeftButtonDown, 1, (nint)(1 | 1 << 16));
        Assert.AreEqual(Child(scope.View.Handle, 12), NativeMethods.GetFocus(), "浮层空白不能冒充输入框。");
    }

    [TestMethod]
    public void 连续定位复用结果计数而查询和开关变化重新统计()
    {
        using ViewScope scope = new("cat CAT cat");
        nint edit = scope.Open("cat");
        int scans = scope.View.FindStatusScanCountForTest;
        Click(scope.View, 10);
        Click(scope.View, 10);
        Click(scope.View, 9);
        Assert.AreEqual(scans, scope.View.FindStatusScanCountForTest);
        Assert.AreEqual("2/3", scope.View.FindStatusForTest);
        Click(scope.View, 12);
        Assert.AreEqual(scans + 1, scope.View.FindStatusScanCountForTest);
        Assert.AreEqual(UiText.FindResults(2), scope.View.FindStatusForTest);
        _ = NativeMethods.SetWindowText(edit, "CAT");
        Assert.AreEqual(scans + 2, scope.View.FindStatusScanCountForTest);
        Assert.AreEqual(UiText.FindResults(1), scope.View.FindStatusForTest);
    }

    [TestMethod]
    public async Task 主窗口消息循环保留查找焦点并将组词按键交回输入法()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("find.txt");
        await File.WriteAllTextAsync(path, "cat x cat");
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            try
            {
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
                Volatile.Write(ref active, window);
                window.Show();
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();
                async Task VerifyAsync()
                {
                    try
                    {
                        await window.OpenWorkspaceAsync(temporary.FullPath);
                        await window.OpenDocumentForTestAsync(path);
                        Assert.IsTrue(window.HandleApplicationShortcutForTest('F', control: true));
                        nint edit = NativeMethods.GetFocus();
                        nint editor = Child(NativeMethods.GetParent(edit), 100);
                        Assert.AreNotEqual((nint)0, editor);
                        _ = NativeMethods.SetWindowText(edit, "cat");
                        _ = NativeMethods.PostMessage(edit, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
                        await FlushAsync();
                        Assert.AreEqual(edit, NativeMethods.GetFocus());
                        Assert.AreEqual((nint)9, NativeMethods.SendMessage(editor, 2008, 0, 0));
                        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
                        _ = NativeMethods.PostMessage(edit, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
                        await FlushAsync();
                        Assert.IsTrue(NativeMethods.IsWindowVisible(edit));
                        Assert.AreEqual(edit, NativeMethods.GetFocus());
                        _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
                        _ = NativeMethods.PostMessage(edit, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyTab, 0);
                        await FlushAsync();
                        nint option = NativeMethods.GetFocus();
                        Assert.AreEqual(Child(NativeMethods.GetParent(edit), 12), option);
                        _ = NativeMethods.PostMessage(option, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
                        await FlushAsync();
                        Assert.AreEqual(option, NativeMethods.GetFocus());
                        _ = NativeMethods.PostMessage(option, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
                        await FlushAsync();
                        Assert.IsFalse(NativeMethods.IsWindowVisible(edit));
                        Assert.AreEqual(editor, NativeMethods.GetFocus());
                        Assert.IsTrue(window.ActiveDocumentIsReadOnly);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { window.Close(); }
                }
                static async Task FlushAsync()
                {
                    // 分发队列会在同一批次继续执行新回调，先返回消息循环处理已投递的键盘消息。
                    await Task.Delay(30);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Volatile.Write(ref active, null);
                if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "查找流程测试窗口没有退出。");
        }
    }

    [TestMethod]
    [DataRow(60_000)]
    [DataRow(2_000_000)]
    public async Task 大文档查找计数在后台完成且切换标签不会被旧结果抢回(int repetitions)
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("large-find.txt");
        await File.WriteAllTextAsync(path, string.Concat(Enumerable.Repeat("cat ", repetitions)));
        string other = temporary.GetPath("other.txt");
        await File.WriteAllTextAsync(other, "其他文件");
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            try
            {
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
                Volatile.Write(ref active, window);
                window.Show();
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();

                async Task VerifyAsync()
                {
                    TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    NativeDocumentView? view = null;
                    try
                    {
                        await window.OpenWorkspaceAsync(temporary.FullPath);
                        await window.OpenDocumentForTestAsync(path);
                        Assert.IsTrue(window.HandleApplicationShortcutForTest('F', control: true));
                        nint edit = NativeMethods.GetFocus();
                        _ = NativeMethods.SetWindowText(edit, "cat");
                        Assert.AreEqual(string.Empty, window.ActiveDocumentFindStatusForTest, "快速查找不能立即闪现加载提示。");
                        _ = NativeMethods.SetWindowText(edit, "ca");
                        _ = NativeMethods.SetWindowText(edit, "cat");
                        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                        while (window.ActiveDocumentFindStatusForTest != UiText.FindResults(repetitions) && DateTime.UtcNow < deadline)
                        {
                            await Task.Delay(20);
                        }

                        Assert.AreEqual(UiText.FindResults(repetitions), window.ActiveDocumentFindStatusForTest);
                        Assert.AreEqual(edit, NativeMethods.GetFocus(), "后台计数完成不能抢走查找输入焦点。");
                        view = window.ActiveDocumentViewForTest!;
                        view.FindWorkBarrierForTest = async token => { entered.TrySetResult(); await release.Task; };
                        int layouts = window.LayoutInvocationCountForTest;
                        _ = NativeMethods.SetWindowText(edit, "missing");
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                        _ = NativeMethods.PostMessage(edit, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
                        await Task.Delay(25);
                        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest, "查找不能重新布局主窗口。");
                        await window.OpenDocumentForTestAsync(other);
                        nint focus = NativeMethods.GetFocus();
                        Assert.IsFalse(view.FindBusyForTest, "切换普通标签必须取消隐藏文档的任务。");
                        release.TrySetResult();
                        await view.FindWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3));
                        await Task.Delay(25);
                        Assert.AreEqual(other, window.ActiveDocumentPathForTest);
                        Assert.AreEqual(focus, NativeMethods.GetFocus(),
                            $"晚到查询不能改变当前焦点；主窗口前台={NativeMethods.GetForegroundWindow() == window.Handle}，原焦点有效={NativeMethods.IsWindow(focus)}，原焦点可见={NativeMethods.IsWindowVisible(focus)}。");
                        Assert.IsTrue(await window.LeftClickDocumentTabForTestAsync(0));
                        deadline = DateTime.UtcNow.AddSeconds(3);
                        while (view.FindBusyForTest && DateTime.UtcNow < deadline) await Task.Delay(10);
                        Assert.AreEqual(UiText.FindResults(0), view.FindStatusForTest);
                        Assert.AreEqual("missing", NativeMethods.GetWindowTextValue(edit));
                    }
                    catch (Exception exception) { failure = exception; }
                    finally
                    {
                        release.TrySetResult();
                        view?.HideFind();
                        if (view is not null) await view.FindWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3));
                        window.Close();
                    }
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Volatile.Write(ref active, null);
                if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "大文档查找测试窗口没有退出。");
        }
    }

    [TestMethod]
    public void 前后切换不重复当前匹配且按钮Enter保持键盘上下文()
    {
        using ViewScope scope = new("cat x cat");
        scope.Open("cat");
        Click(scope.View, 10);
        Click(scope.View, 10);
        nint previous = Child(scope.View.Handle, 9);
        _ = NativeMethods.SetFocus(previous);
        Assert.IsTrue(scope.View.HandleFindShortcut(new() { Window = previous, MessageId = NativeMethods.WindowMessageKeyDown, WordParameter = NativeMethods.VirtualKeyEnter }));
        Assert.AreEqual(previous, NativeMethods.GetFocus());
        Assert.AreEqual((nint)6, NativeMethods.SendMessage(Child(scope.View.Handle, 100), 2009, 0, 0));
        Assert.IsTrue(scope.View.HandleFindTabNavigation(backwards: false));
        Assert.AreEqual(Child(scope.View.Handle, 10), NativeMethods.GetFocus());
        Assert.IsTrue(scope.View.HandleFindTabNavigation(backwards: true));
        Assert.AreEqual(previous, NativeMethods.GetFocus());
    }

    [TestMethod]
    public void 零宽正则在文本首尾仍能循环前进和后退()
    {
        using ViewScope scope = new("a b a");
        nint edit = scope.Open("(?=a)|$");
        Click(scope.View, 14);
        foreach (int position in new[] { 4, 5, 0, 4 })
        {
            _ = NativeMethods.SetFocus(edit);
            Key(edit, NativeMethods.VirtualKeyEnter);
            NativeFindTestPump.Wait(scope.View);
            Assert.AreEqual((nint)position, NativeMethods.SendMessage(Child(scope.View.Handle, 100), 2008, 0, 0));
        }
        foreach (int position in new[] { 0, 5, 4, 0 })
        {
            Click(scope.View, 9);
            Assert.AreEqual((nint)position, NativeMethods.SendMessage(Child(scope.View.Handle, 100), 2008, 0, 0));
        }
    }

    [TestMethod]
    public void 输入框连续Enter查找保留焦点且Esc返回正文()
    {
        using ViewScope scope = new("中文 cat 其他 cat 结尾 cat");
        nint editor = Child(scope.View.Handle, 100);
        nint edit = scope.Open("cat");
        foreach (int start in new[] { 10, 17, 3 })
        {
            Key(NativeMethods.GetFocus(), NativeMethods.VirtualKeyEnter);
            Assert.AreEqual(edit, NativeMethods.GetFocus(), "查找后输入框必须仍可接收下一次 Enter。");
            int byteStart = Encoding.UTF8.GetByteCount(scope.Source.AsSpan(0, start));
            Assert.AreEqual((nint)byteStart, NativeMethods.SendMessage(editor, 2009, 0, 0));
        }
        Assert.IsTrue(scope.View.IsTextReadOnly);
        Key(edit, NativeMethods.VirtualKeyEscape);
        Assert.IsFalse(scope.View.FindOverlayVisibleForTest);
        Assert.AreEqual(editor, NativeMethods.GetFocus());
    }

    [TestMethod]
    public void 输入法组词时Enter不查找Esc不关闭()
    {
        using ViewScope scope = new("中文 cat cat");
        nint editor = Child(scope.View.Handle, 100);
        nint edit = scope.Open("cat");
        nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0);
        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
        Key(edit, NativeMethods.VirtualKeyEnter);
        Key(edit, NativeMethods.VirtualKeyEscape);
        Assert.IsTrue(scope.View.FindOverlayVisibleForTest);
        Assert.AreEqual(edit, NativeMethods.GetFocus());
        Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
        _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
        Key(edit, NativeMethods.VirtualKeyEnter);
        Assert.IsGreaterThan((nint)0, NativeMethods.SendMessage(editor, 2008, 0, 0));
        Assert.AreEqual(edit, NativeMethods.GetFocus());
    }

    [TestMethod]
    [DataRow(96, 13, "Light")]
    [DataRow(96, 40, "Dark")]
    [DataRow(120, 40, "Light")]
    [DataRow(144, 40, "Dark")]
    public void 搜索文字完整显示且图标保持固定尺寸(int dpi, int size, string theme)
    {
        using ViewScope scope = new("cat cat", dpi, size, theme);
        nint edit = scope.Open("cat");
        int height = Measure("国Ag").Height;
        Assert.IsGreaterThanOrEqualTo(height, Bounds(edit).Height);
        Assert.IsGreaterThanOrEqualTo(height, Bounds(Child(scope.View.Handle, 36)).Height);
        foreach (int identifier in FindButtons)
        {
            var bounds = Bounds(Child(scope.View.Handle, identifier));
            Assert.AreEqual(NativeTheme.Scale(28), bounds.Width);
            Assert.AreEqual(NativeTheme.Scale(28), bounds.Height);
        }
        Click(scope.View, 14);
        _ = NativeMethods.SetWindowText(edit, "[");
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual(UiText.InvalidRegularExpression, scope.View.FindStatusForTest);
        Assert.IsGreaterThanOrEqualTo(Measure(UiText.InvalidRegularExpression).Width + NativeTheme.Scale(4),
            Bounds(Child(scope.View.Handle, 36)).Width, "有空间时必须完整显示错误原因。");
        Assert.IsTrue(scope.View.FindOverlayWithinClientBoundsForTest);
        int errorWidth = Bounds(Child(scope.View.Handle, 35)).Width;
        _ = NativeMethods.SetWindowText(edit, string.Empty);
        Assert.AreEqual(errorWidth, Bounds(Child(scope.View.Handle, 35)).Width,
            "正文顶部查找条始终占满正文宽度。");
    }

    [TestMethod]
    public void 改字号保留输入选择且查找条推动正文下移()
    {
        using ViewScope scope = new(string.Join('\n', Enumerable.Repeat("cat 中文", 100)));
        nint editor = Child(scope.View.Handle, 100);
        var before = Bounds(editor);
        nint edit = scope.Open("cat");
        Assert.AreEqual(before.Y + Bounds(Child(scope.View.Handle, 35)).Height, Bounds(editor).Y);
        _ = NativeMethods.SendMessage(edit, 0x00B1, 1, 2);
        NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", 40);
        scope.View.ApplyAppearance(new() { Theme = "Light", TextFontSize = 40, FontSize = 13 });
        Assert.AreEqual(edit, NativeMethods.GetFocus());
        Assert.AreEqual("cat", NativeMethods.GetWindowTextValue(edit));
        Assert.AreEqual((nint)(1 | 2 << 16), NativeMethods.SendMessage(edit, 0x00B0, 0, 0));
        Assert.AreEqual(editor, Child(scope.View.Handle, 100));
        Assert.IsGreaterThan(before.Y, Bounds(editor).Y);
        Assert.IsGreaterThanOrEqualTo(Measure("国Ag").Height, Bounds(edit).Height);
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    private static void Key(nint target, int key) => _ = NativeMethods.SendMessage(target, NativeMethods.WindowMessageKeyDown, unchecked((nuint)key), 0);

    private static uint OverlayPixel(NativeDocumentView view, int x, int y)
    {
        nint overlay = Child(view.Handle, 35);
        var bounds = Bounds(overlay);
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = bounds.Width,
                Height = -bounds.Height,
                Planes = 1,
                BitCount = 32
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.DrawItem>());
        try
        {
            NativeMethods.DrawItem item = new()
            {
                Control = overlay,
                ControlIdentifier = 35,
                DeviceContext = dc,
                ItemRectangle = new() { Right = bounds.Width, Bottom = bounds.Height }
            };
            Marshal.StructureToPtr(item, buffer, false);
            _ = NativeMethods.SendMessage(view.Handle, NativeMethods.WindowMessageDrawItem, 35, buffer);
            return NativeMethods.GetPixel(dc, x, y);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }
    private static void Click(NativeDocumentView view, int command)
    {
        _ = NativeMethods.SendMessage(Child(view.Handle, command), 0x00F5, 0, 0);
        NativeFindTestPump.Wait(view);
    }
    private static (int X, int Y, int Width, int Height) Bounds(nint control)
    {
        Assert.AreNotEqual((nint)0, control);
        Assert.IsTrue(NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle bounds));
        return (bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }
    private static (int Width, int Height) Measure(string text)
    {
        nint dc = NativeMethods.GetDeviceContext(0);
        nint previous = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        try
        {
            NativeMethods.Rectangle bounds = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return (bounds.Right, bounds.Bottom);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(0, dc); }
    }

    private sealed class ViewScope : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new();
        private readonly IDisposable _scale;
        private readonly string _previousFamily = NativeTheme.UiFontFamilyForTest;
        private readonly double _previousSize = NativeTheme.UiFontSizeForTest;
        internal ViewScope(string source, int dpi = 96, int size = 13, string theme = "Light")
        {
            _scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
            NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
            Source = source;
            string path = _temporary.GetPath("find.txt");
            Result = new(DocumentReadStatus.TextReady, path, path, new(DocumentKind.Text, "文本"),
                Encoding.UTF8.GetByteCount(source), source, null, null, string.Empty);
            Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, NativeTheme.Scale(920), NativeTheme.Scale(520),
                0, 0, NativeMethods.GetModuleHandle(null), 0);
            View = new(Owner, _temporary.FullPath, Result, new() { Theme = theme, FontSize = 13, TextFontSize = size }, _ => { }, (_, _, _) => { });
            View.SetBounds(0, 0, NativeTheme.Scale(900), NativeTheme.Scale(500));
        }
        internal string Source { get; }
        internal DocumentReadResult Result { get; }
        internal nint Owner { get; }
        internal NativeDocumentView View { get; }
        internal nint Open(string query)
        {
            Click(View, 4);
            nint edit = Child(View.Handle, 20);
            Assert.AreEqual(edit, NativeMethods.GetFocus());
            _ = NativeMethods.SetWindowText(edit, query);
            NativeFindTestPump.Wait(View);
            return edit;
        }
        public void Dispose()
        {
            View.Dispose();
            View.FindWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _ = NativeMethods.DestroyWindow(Owner);
            NativeTheme.ConfigureUiTypography(_previousFamily, _previousSize);
            _scale.Dispose();
            _temporary.Dispose();
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint Child(nint window, int identifier);

    [DllImport("user32.dll", EntryPoint = "ValidateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ValidateRectangle(nint window, nint rectangle);

    [DllImport("user32.dll", EntryPoint = "GetUpdateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUpdateRectangle(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
}
