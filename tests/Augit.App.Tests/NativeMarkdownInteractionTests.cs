using System.Diagnostics;
using System.Text.Json;
using Augit.Core.Documents;
using Augit.Infrastructure.Files;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeMarkdownInteractionTests
{
    [TestMethod]
    public Task Markdown查找条不属于对照分隔线的拖动区域() => RunAsync(async (window, workspace) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "sample.md"));
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        await view.ShowMarkdownSplitForTestAsync();
        nint source = Child(view.Handle, 100);
        MarkdownWebViewHost preview = view.MarkdownHostForTest!;
        int beforeFind = BodyTop(source);
        view.ShowFindForTest("段落");
        int bodyTop = BodyTop(source);
        Assert.AreEqual(beforeFind + NativeDocumentView.FindOverlayHeightForTest, bodyTop);
        Assert.AreEqual(bodyTop, BodyTop(preview.Handle));
        Assert.AreEqual(bodyTop, view.ContentTopForTest);

        int splitWidth = view.MarkdownSplitSourceWidthForTest;
        int x = splitWidth + NativeDocumentView.MarkdownSplitterWidthForTest / 2;
        Assert.IsFalse(PressSplitter(x, bodyTop - 1), "查找条底边不能捕获对照分隔线拖动。");
        Assert.AreEqual(splitWidth, view.MarkdownSplitSourceWidthForTest);
        Assert.IsTrue(PressSplitter(x, bodyTop), "正文起点必须仍可拖动对照分隔线。");
        Assert.IsTrue(view.DragMarkdownSplitterForTest(NativeTheme.Scale(30)));
        Assert.IsTrue(view.FindOverlayVisibleForTest);
        Assert.AreEqual(bodyTop, BodyTop(source));
        Assert.AreEqual(bodyTop, BodyTop(preview.Handle));
        view.HideFind();
        Assert.AreEqual(beforeFind, BodyTop(source));
        Assert.AreEqual(beforeFind, BodyTop(preview.Handle));
        Assert.IsTrue(PressSplitter(view.MarkdownSplitSourceWidthForTest
            + NativeDocumentView.MarkdownSplitterWidthForTest / 2, beforeFind));
        Assert.IsTrue(view.IsTextReadOnly);

        bool PressSplitter(int clientX, int clientY)
        {
            nint point = (nint)((clientY << 16) | (clientX & 0xffff));
            try
            {
                _ = NativeMethods.SendMessage(view.Handle, NativeMethods.WindowMessageLeftButtonDown, 1, point);
                return NativeMethods.GetCapture() == view.Handle;
            }
            finally
            {
                _ = NativeMethods.SendMessage(view.Handle, NativeMethods.WindowMessageLeftButtonUp, 0, point);
                Assert.AreNotEqual(view.Handle, NativeMethods.GetCapture(), "测试拖动结束后必须释放捕获。");
            }
        }

        int BodyTop(nint control)
        {
            Assert.IsTrue(NativeMethods.GetWindowRectangle(control, out var rectangle));
            NativeMethods.Point point = new() { X = rectangle.Left, Y = rectangle.Top };
            Assert.IsTrue(NativeMethods.ScreenToClient(view.Handle, ref point));
            return point.Y;
        }
    });

    [TestMethod]
    public Task 远程图片失败只显示局部反馈且正文继续可读() => RunAsync(async (window, workspace) =>
    {
        string path = Path.Combine(workspace, "remote.md");
        await File.WriteAllTextAsync(path,
            "# 图片反馈\n\n正文先显示。\n\n![无法访问](http://127.0.0.1:1/augit-image.png)\n\n后文继续显示。\n");
        await window.OpenDocumentForTestAsync(path);
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        await view.ShowMarkdownPreviewForTestAsync();
        MarkdownWebViewHost host = view.MarkdownHostForTest!;
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        string state;
        do
        {
            state = await host.ReadPageForTestAsync(
                "document.querySelector('.markdown-image').dataset.state");
            if (state == "\"failure\"") break;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        Assert.AreEqual("\"failure\"", state);
        string text = await host.ReadPageForTestAsync("document.body.textContent");
        StringAssert.Contains(text, "正文先显示");
        StringAssert.Contains(text, "图片加载失败：无法访问");
        StringAssert.Contains(text, "后文继续显示");
        Assert.AreEqual("\"none\"", await host.ReadPageForTestAsync(
            "getComputedStyle(document.querySelector('.markdown-image img')).display"));
    });

    [TestMethod]
    public Task 三模式切换与外部更新复用正文和浏览器并保持阅读上下文() => RunAsync(async (window, workspace) =>
    {
        string path = Path.Combine(workspace, "sample.md");
        await window.OpenDocumentForTestAsync(path);
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        await view.ShowMarkdownSplitForTestAsync();
        Assert.IsTrue(view.IsMarkdownPreviewReady, view.MarkdownPreviewError);
        MarkdownWebViewHost host = view.MarkdownHostForTest!;
        int pid = host.BrowserProcessId;
        string folder = host.UserDataFolderForTest!;
        nint editor = Child(view.Handle, 100);
        nint selectionEnd = NativeMethods.SendMessage(editor, 2167, 8, 0);
        nint selectionStart = NativeMethods.SendMessage(editor, 2167, 3, 0);
        _ = NativeMethods.SendMessage(editor, 2160, (nuint)selectionEnd, selectionStart);
        _ = NativeMethods.SendMessage(editor, 2613, 20, 0);
        var sourceBefore = SourceState(editor);
        await host.ReadPageForTestAsync("scrollTo(0, 500)");
        double previewBefore = await ScrollAsync(host);
        Assert.IsGreaterThan(0d, previewBefore);
        await view.ShowMarkdownPreviewForTestAsync();
        Assert.AreSame(host, view.MarkdownHostForTest);
        Click(view, 1);
        Assert.IsFalse(view.IsMarkdownPreviewReady);
        Assert.IsFalse(NativeMethods.IsWindowVisible(host.Handle));
        Assert.AreEqual(sourceBefore, SourceState(editor));
        await view.ShowMarkdownSplitForTestAsync();
        Assert.AreSame(host, view.MarkdownHostForTest);
        Assert.AreEqual(previewBefore, await ScrollAsync(host), 1d);
        Assert.AreEqual(sourceBefore, SourceState(editor));
        Assert.AreEqual(pid, host.BrowserProcessId);
        Assert.IsTrue(view.DragMarkdownSplitterForTest(30));
        int splitWidth = view.MarkdownSplitSourceWidthForTest;
        await view.ShowMarkdownPreviewForTestAsync();
        await view.ShowMarkdownSplitForTestAsync();
        Assert.AreEqual(splitWidth, view.MarkdownSplitSourceWidthForTest);

        view.ShowFindForTest("段落");
        var afterFind = SourceState(editor);
        Assert.AreNotEqual(sourceBefore, afterFind, "输入查询应自动定位，不能沿用查找前的位置。");
        nint focus = NativeMethods.GetFocus();
        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(workspace, path);
        await view.ReloadAsync(result with { Text = result.Text + "\n更新的末段\n" });
        Assert.AreSame(host, view.MarkdownHostForTest);
        Assert.AreEqual(editor, Child(view.Handle, 100));
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.AreEqual(afterFind, SourceState(editor), "外部刷新应保持当前查找后的阅读位置。");
        Assert.AreEqual(previewBefore, await ScrollAsync(host), 1d);
        StringAssert.Contains(await host.ReadPageForTestAsync("document.body.textContent"), "更新的末段");
        Assert.IsTrue(view.IsTextReadOnly);

        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "other.txt"));
        Assert.IsFalse(NativeMethods.IsWindowVisible(host.Handle));
        await window.OpenDocumentForTestAsync(path);
        Assert.AreSame(view, window.ActiveDocumentViewForTest);
        Assert.AreSame(host, view.MarkdownHostForTest);
        window.CloseActiveTabForTest();
        await view.MarkdownWorkForTest;
        Assert.IsFalse(NativeMethods.IsWindow(host.Handle));
        Assert.IsFalse(ProcessRunning(pid), "关闭文件必须结束对应浏览器进程。");
        Assert.IsFalse(Directory.Exists(folder), "关闭文件必须清理预览会话目录。");
    });

    [TestMethod]
    public Task 慢刷新保留旧预览并丢弃过期结果且错误可以重试() => RunAsync(async (window, workspace) =>
    {
        string path = Path.Combine(workspace, "sample.md");
        await window.OpenDocumentForTestAsync(path);
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        await view.ShowMarkdownSplitForTestAsync();
        MarkdownWebViewHost host = view.MarkdownHostForTest!;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int renderCalls = 0;
        view.MarkdownRendererForTest = async (text, token) =>
        {
            renderCalls++;
            if (text.Contains("过期", StringComparison.Ordinal)) { entered.TrySetResult(); await release.Task; }
            return await MarkdownPreviewRenderer.RenderAsync(text, workspace, path, false, token);
        };
        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(workspace, path);
        Task old = view.ReloadAsync(result with { Text = "# 过期" });
        try
        {
            await entered.Task;
            await view.ReloadAsync(result with { Text = "# 过期" });
            Assert.AreEqual(1, renderCalls, "重复文件快照不能取消并重启正在进行的预览。");
            // 延迟反馈阈值是本场景的被测行为；旧正文在阈值后仍必须可见。
            await Task.Delay(180);
            Assert.IsTrue(view.IsMarkdownPreviewLoading);
            StringAssert.Contains(await host.ReadPageForTestAsync("document.body.textContent"), "段落");
            nint status = Child(view.Handle, 31);
            Assert.AreNotEqual((nint)0, status);
            _ = NativeMethods.GetWindowRectangle(status, out var statusBounds);
            _ = NativeMethods.GetWindowRectangle(host.Handle, out var previewBounds);
            Assert.IsLessThan(previewBounds.Bottom - previewBounds.Top, statusBounds.Bottom - statusBounds.Top);
            Task latest = view.ReloadAsync(result with { Text = "# 最新" });
            release.TrySetResult();
            await Task.WhenAll(old, latest);
            Assert.IsTrue(view.IsMarkdownPreviewReady, view.MarkdownPreviewError);
            Assert.AreSame(host, view.MarkdownHostForTest);
            StringAssert.Contains(await host.ReadPageForTestAsync("document.body.textContent"), "最新");
            Assert.DoesNotContain("过期", await host.ReadPageForTestAsync("document.body.textContent"), StringComparison.Ordinal);

            view.MarkdownRendererForTest = (_, _) => throw new IOException("测试资源读取失败");
            await view.ReloadAsync(result with { Text = "# 重试" });
            Assert.AreEqual("测试资源读取失败", view.MarkdownPreviewError);
            StringAssert.Contains(await host.ReadPageForTestAsync("document.body.textContent"), "最新");
            view.MarkdownRendererForTest = null;
            await view.ShowMarkdownSplitForTestAsync();
            Assert.IsTrue(view.IsMarkdownPreviewReady, view.MarkdownPreviewError);
            StringAssert.Contains(await host.ReadPageForTestAsync("document.body.textContent"), "重试");
        }
        finally { release.TrySetResult(); await old; }
    });

    [TestMethod]
    public Task 隐藏标签取消在途工作且更新与主题变化不创建后台预览() => RunAsync(async (window, workspace) =>
    {
        string path = Path.Combine(workspace, "sample.md");
        await window.OpenDocumentForTestAsync(path);
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        await view.ShowMarkdownPreviewForTestAsync();
        MarkdownWebViewHost host = view.MarkdownHostForTest!;
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool cancelled = false;
        int calls = 0;
        view.MarkdownRendererForTest = async (_, token) =>
        {
            calls++;
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return "";
        };
        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(workspace, path);
        Task pending = view.ReloadAsync(result with { Text = "# 后台旧任务" });
        await entered.Task;
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "other.txt"));
        await pending;
        Assert.IsTrue(cancelled);
        window.SetStatusForTest("前台提示");
        await view.ReloadAsync(result with { Text = "# 后台最新" });
        view.ApplyAppearance(new ApplicationSettings { Theme = "dark" });
        Assert.AreEqual(1, calls);
        Assert.AreEqual("前台提示", window.StatusTextForTest);
        Assert.IsFalse(NativeMethods.IsWindowVisible(host.Handle));
        view.MarkdownRendererForTest = null;
        await window.OpenDocumentForTestAsync(path);
        await view.MarkdownWorkForTest;
        Assert.IsTrue(view.IsMarkdownPreviewReady, view.MarkdownPreviewError);
        Assert.AreSame(host, view.MarkdownHostForTest);
        StringAssert.Contains(await host.ReadPageForTestAsync("document.body.textContent"), "后台最新");
    });

    [TestMethod]
    public Task 关闭正在更新的Markdown标签取消任务并释放旧浏览器() => RunAsync(async (window, workspace) =>
    {
        string path = Path.Combine(workspace, "sample.md");
        await window.OpenDocumentForTestAsync(path);
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        await view.ShowMarkdownPreviewForTestAsync();
        MarkdownWebViewHost host = view.MarkdownHostForTest!;
        int pid = host.BrowserProcessId;
        string folder = host.UserDataFolderForTest!;
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool cancelled = false;
        view.MarkdownRendererForTest = async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return "";
        };
        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(workspace, path);
        Task pending = view.ReloadAsync(result with { Text = "# 即将关闭" });
        await entered.Task;
        window.CloseActiveTabForTest();
        await pending;
        Assert.IsTrue(cancelled);
        Assert.IsFalse(ProcessRunning(pid));
        Assert.IsFalse(Directory.Exists(folder));
    });

    [TestMethod]
    public Task Markdown相对链接打开只读目标并定位章节且返回保留原预览() => RunAsync(async (window, workspace) =>
    {
        string path = Path.Combine(workspace, "sample.md");
        string linked = Path.Combine(workspace, "linked.md");
        await window.OpenDocumentForTestAsync(path);
        NativeDocumentView source = window.ActiveDocumentViewForTest!;
        await source.ShowMarkdownPreviewForTestAsync();
        MarkdownWebViewHost host = source.MarkdownHostForTest!;
        await host.ReadPageForTestAsync("scrollTo(0, 500)");
        double before = await ScrollAsync(host);
        await host.ReadPageForTestAsync("document.querySelector('a').click()");
        Stopwatch timeout = Stopwatch.StartNew();
        while (window.ActiveDocumentPathForTest != linked || !window.ActiveMarkdownPreviewReady)
        {
            Assert.IsLessThan(10000, timeout.ElapsedMilliseconds, "相对链接未完成只读文件与锚点导航。");
            await Task.Delay(10);
        }
        NativeDocumentView target = window.ActiveDocumentViewForTest!;
        Assert.IsTrue(target.IsTextReadOnly);
        Assert.IsGreaterThan(0d, await ScrollAsync(target.MarkdownHostForTest!),
            await target.MarkdownHostForTest!.ReadPageForTestAsync("JSON.stringify({hash:location.hash,ids:[...document.querySelectorAll('[id]')].map(e=>e.id),height:document.body.scrollHeight,viewport:innerHeight})"));
        await window.OpenDocumentForTestAsync(path);
        Assert.AreSame(source, window.ActiveDocumentViewForTest);
        Assert.AreSame(host, source.MarkdownHostForTest);
        Assert.AreEqual(before, await ScrollAsync(host), 1d);
        source.ApplyAppearance(new ApplicationSettings { Theme = "Dark" });
        await source.MarkdownWorkForTest;
        Assert.AreEqual("\"rgb(30, 31, 34)\"", await host.ReadPageForTestAsync("getComputedStyle(document.body).backgroundColor"));
        Assert.AreEqual(before, await ScrollAsync(host), 1d);
    });

    private static void Click(NativeDocumentView view, int command) =>
        _ = NativeMethods.SendMessage(view.Handle, NativeMethods.WindowMessageCommand, (nuint)command, 0);

    private static nint Child(nint parent, int identifier)
    {
        for (nint child = NativeMethods.GetWindowSibling(parent, 5); child != 0; child = NativeMethods.GetWindowSibling(child, 2))
            if (NativeMethods.GetWindowLongPointer(child, -12) == identifier) return child;
        return 0;
    }

    private static (nint Anchor, nint Caret, nint Top) SourceState(nint editor) =>
        (NativeMethods.SendMessage(editor, 2009, 0, 0), NativeMethods.SendMessage(editor, 2008, 0, 0), NativeMethods.SendMessage(editor, 2152, 0, 0));

    private static async Task<double> ScrollAsync(MarkdownWebViewHost host) =>
        JsonSerializer.Deserialize<double>(await host.ReadPageForTestAsync("scrollY"));

    private static bool ProcessRunning(int pid)
    {
        try { using Process process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task RunAsync(Func<MainWindow, string, Task> scenario)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "sample.md"), string.Join('\n', Enumerable.Range(1, 100).Select(i => $"## 第 {i} 段落\n\n阅读位置测试。\n")) + "\n[指南](linked.md#章节)\n");
        await File.WriteAllTextAsync(Path.Combine(workspace, "linked.md"), string.Concat(Enumerable.Repeat("段落\n\n", 80)) + "\n# 章节\n\n目标章节\n");
        await File.WriteAllTextAsync(Path.Combine(workspace, "other.txt"), "保持前台");
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
                async Task VerifyAsync()
                {
                    try { Assert.IsTrue(await window.OpenWorkspaceAsync(workspace)); await scenario(window, workspace); }
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
                if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(40)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "Markdown 测试窗口没有退出。");
        }
    }
}
