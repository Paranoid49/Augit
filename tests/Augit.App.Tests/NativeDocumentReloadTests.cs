using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Documents;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeDocumentReloadTests
{
    [TestMethod]
    [DataRow(DocumentKind.Text, false, false)]
    [DataRow(DocumentKind.Text, false, true)]
    [DataRow(DocumentKind.Json, false, false)]
    [DataRow(DocumentKind.Json, false, true)]
    [DataRow(DocumentKind.Json, true, false)]
    [DataRow(DocumentKind.Json, true, true)]
    public void 外部正文更新保留原生控件选择方向滚动与查找输入(DocumentKind kind, bool original, bool findFocused)
    {
        using ViewScope scope = new(kind);
        NativeDocumentView view = scope.View;
        if (original) Click(view, 1);
        nint editor = Child(view.Handle, view.IsShowingAlternative ? 101 : 100);
        _ = NativeMethods.SetFocus(editor);
        int lineStart = (int)NativeMethods.SendMessage(editor, 2167, 60, 0);
        _ = NativeMethods.SendMessage(editor, 2160, (nuint)(lineStart + 48), lineStart + 5);
        _ = NativeMethods.SendMessage(editor, 2613, 45, 0);
        _ = NativeMethods.SendMessage(editor, 2397, 60, 0);
        if (findFocused) view.ShowFindForTest("标记");
        var before = Capture(editor);
        nint focus = NativeMethods.GetFocus();
        bool alternative = view.IsShowingAlternative;
        DocumentReadResult update = scope.Result with { Text = scope.Result.Text!.Replace("key159", "changed159", StringComparison.Ordinal) };

        view.ReloadAsync(update).GetAwaiter().GetResult();

        Assert.AreEqual(editor, Child(view.Handle, alternative ? 101 : 100), "同类型文件更新不能重建正文控件。");
        Assert.AreEqual(alternative, view.IsShowingAlternative, "JSON 更新必须保留用户选择的原文或格式化模式。");
        Assert.AreEqual(before, Capture(editor), "选择方向、横向滚动和顶部阅读位置必须保持。");
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.AreEqual(update.Text, view.CurrentText);
        Assert.AreEqual(alternative ? JsonDisplayFormatter.Format(update.Text!).DisplayText : update.Text, ReadText(editor));
        Assert.IsTrue(view.IsTextReadOnly);
        if (findFocused)
        {
            Assert.IsTrue(view.FindOverlayVisibleForTest);
            Assert.AreEqual("标记", NativeMethods.GetWindowTextValue(focus));
            Assert.AreEqual(UiText.FindResults(160), view.FindStatusForTest);
        }
    }

    [TestMethod]
    public void 更新后继续查找从原位置开始且结果数反映新正文()
    {
        using ViewScope scope = new(DocumentKind.Text, "标记 first\n标记 second\n标记 third\n");
        NativeDocumentView view = scope.View;
        view.ShowFindForTest("标记");
        Click(view, 10);
        Click(view, 10);
        DocumentReadResult update = scope.Result with { Text = scope.Result.Text + "标记 fourth\n" };
        view.ReloadAsync(update).GetAwaiter().GetResult();
        Assert.AreEqual("3/4", view.FindStatusForTest);
        Click(view, 10);
        nint editor = Child(view.Handle, 100);
        int expected = Encoding.UTF8.GetByteCount("标记 first\n标记 second\n标记 third\n");
        Assert.AreEqual((nint)expected, NativeMethods.SendMessage(editor, 2143, 0, 0), "刷新后下一项不能从首项重来。");
    }

    [TestMethod]
    public void JSON变为无效时切回最新原文并保留查找输入()
    {
        using ViewScope scope = new(DocumentKind.Json);
        NativeDocumentView view = scope.View;
        view.ShowFindForTest("标记");
        nint find = NativeMethods.GetFocus();
        nint original = Child(view.Handle, 100);
        view.ReloadAsync(scope.Result with { Text = "{\"标记\":}" }).GetAwaiter().GetResult();
        Assert.IsFalse(view.IsShowingAlternative);
        Assert.AreEqual(original, Child(view.Handle, 100));
        Assert.IsTrue(NativeMethods.IsWindowVisible(original));
        Assert.AreEqual(find, NativeMethods.GetFocus());
        Assert.AreEqual(UiText.FindResults(1), view.FindStatusForTest);
        StringAssert.Contains(view.ReadStatusText, "JSON 格式错误");
        view.ReloadAsync(scope.Result).GetAwaiter().GetResult();
        Assert.IsFalse(view.IsShowingAlternative, "修复后不自动切走用户正在看的原文。");
        Assert.AreEqual("JSON", view.ReadStatusText);
    }

    [TestMethod]
    [DataRow(DocumentReadStatus.Missing)]
    [DataRow(DocumentReadStatus.InvalidUtf8)]
    [DataRow(DocumentReadStatus.TextTooLarge)]
    public void 文件失效或越界仍清理旧正文并显示真实结果(DocumentReadStatus status)
    {
        using ViewScope scope = new(DocumentKind.Text);
        nint editor = Child(scope.View.Handle, 100);
        scope.View.ShowFindForTest("标记");
        scope.View.ReloadAsync(scope.Result with { Status = status, Text = null, Message = "文件当前不可预览" }).GetAwaiter().GetResult();
        Assert.IsFalse(NativeMethods.IsWindow(editor));
        Assert.AreEqual(status, scope.View.Status);
        Assert.IsNull(scope.View.CurrentText);
        Assert.IsFalse(scope.View.FindOverlayVisibleForTest);
        Assert.AreEqual("文件当前不可预览", scope.View.ReadStatusText);
        scope.View.ReloadAsync(scope.Result).GetAwaiter().GetResult();
        Assert.AreEqual(DocumentReadStatus.TextReady, scope.View.Status);
        Assert.AreNotEqual((nint)0, Child(scope.View.Handle, 100));
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void Scintilla更新保持选择方向并将端点限制在UTF8字符边界(int offset)
    {
        using ViewScope scope = new(DocumentKind.Text);
        using ScintillaControl editor = new(scope.Owner, 501);
        editor.SetBounds(0, 0, 400, 220);
        editor.SetTextContent("abcdef");
        _ = NativeMethods.SendMessage(editor.Handle, 2160, 6, offset);
        editor.SetTextContent("😀");
        Assert.AreEqual((nint)4, NativeMethods.SendMessage(editor.Handle, 2009, 0, 0), "反向选择的锚点需保留在文件末尾。");
        Assert.AreEqual((nint)(offset == 4 ? 4 : 0), NativeMethods.SendMessage(editor.Handle, 2008, 0, 0));
        Assert.IsTrue(editor.IsReadOnly);
    }

    [TestMethod]
    public void 原位更新保留换行和空白符设置且短文件收敛到有效位置()
    {
        using ViewScope scope = new(DocumentKind.Text);
        Click(scope.View, 6);
        Click(scope.View, 7);
        nint editor = Child(scope.View.Handle, 100);
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor, 2269, 0, 0), "换行按钮必须在刷新前已实际启用。");
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor, 2020, 0, 0), "空白符按钮必须在刷新前已实际启用。");
        _ = NativeMethods.SendMessage(editor, 2160, 9000, 8000);
        _ = NativeMethods.SendMessage(editor, 2613, 70, 0);
        scope.View.ReloadAsync(scope.Result with { Text = "短\n文" }).GetAwaiter().GetResult();
        Assert.AreEqual(editor, Child(scope.View.Handle, 100));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor, 2269, 0, 0));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor, 2020, 0, 0));
        Assert.AreEqual((nint)7, NativeMethods.SendMessage(editor, 2008, 0, 0));
        Assert.AreEqual((nint)7, NativeMethods.SendMessage(editor, 2009, 0, 0));
        Assert.IsLessThanOrEqualTo((nint)1, NativeMethods.SendMessage(editor, 2152, 0, 0));
    }

    [TestMethod]
    public void 查找开关由真实按钮切换且外部更新后继续生效()
    {
        using ViewScope scope = new(DocumentKind.Text, "cat Cat catalog");
        NativeDocumentView view = scope.View;
        view.ShowFindForTest("cat");
        Assert.AreEqual(UiText.FindResults(3), view.FindStatusForTest);
        Click(view, 12);
        Assert.AreEqual(UiText.FindResults(2), view.FindStatusForTest);
        Click(view, 13);
        Assert.AreEqual(UiText.FindResults(1), view.FindStatusForTest);
        view.ShowFindForTest("c.t");
        Assert.AreEqual(UiText.FindResults(0), view.FindStatusForTest);
        Click(view, 14);
        Assert.AreEqual(UiText.FindResults(1), view.FindStatusForTest);
        view.ReloadAsync(scope.Result with { Text = "cat Cat catalog cut" }).GetAwaiter().GetResult();
        NativeFindTestPump.Wait(view);
        Assert.AreEqual(UiText.FindResults(2), view.FindStatusForTest);
        Click(view, 12);
        Assert.AreEqual(UiText.FindResults(3), view.FindStatusForTest);
        Click(view, 14);
        Assert.AreEqual(UiText.FindResults(0), view.FindStatusForTest);
    }

    [TestMethod]
    public void 后台JSON更新为错误原文不得激活隐藏文档或抢走其他控件焦点()
    {
        using ViewScope scope = new(DocumentKind.Json);
        scope.View.SetVisible(false);
        nint other = NativeMethods.CreateWindow(0, NativeMethods.EditClass, "保留输入",
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible, 0, 0, 200, 30,
            scope.Owner, 900, NativeMethods.GetModuleHandle(null), 0);
        _ = NativeMethods.SetFocus(other);
        scope.View.ReloadAsync(scope.Result with { Text = "{" }).GetAwaiter().GetResult();
        Assert.IsFalse(NativeMethods.IsWindowVisible(scope.View.Handle));
        Assert.AreEqual(other, NativeMethods.GetFocus());
        Assert.AreEqual("保留输入", NativeMethods.GetWindowTextValue(other));
        Assert.IsFalse(scope.View.IsShowingAlternative);
    }

    [TestMethod]
    public void 格式化JSON正在取得焦点时无效更新将焦点交给最新原文()
    {
        using ViewScope scope = new(DocumentKind.Json);
        _ = NativeMethods.SetFocus(Child(scope.View.Handle, 101));
        scope.View.ReloadAsync(scope.Result with { Text = "{" }).GetAwaiter().GetResult();
        Assert.AreEqual(Child(scope.View.Handle, 100), NativeMethods.GetFocus());
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    public void 刷新清理过期Blame但继续复用正文控件()
    {
        using ViewScope scope = new(DocumentKind.Text);
        GitBlameLine blame = new(1, new string('a', 40), "测试作者", "test@example.invalid",
            DateTimeOffset.UnixEpoch, "测试提交", "sample.txt", "原文");
        Assert.IsTrue(scope.View.ShowBlame([blame], _ => Assert.Fail("刷新不能打开提交")));
        nint editor = Child(scope.View.Handle, 100);
        Assert.IsTrue(scope.View.IsShowingBlame);
        Assert.IsTrue(scope.View.BlameToolbarVisibleForTest);
        Assert.AreEqual("1 行归属", scope.View.BlameCountTextForTest);
        Assert.IsFalse(NativeMethods.IsWindowVisible(Child(scope.View.Handle, 30)));
        _ = NativeMethods.SendMessage(scope.View.BlameCloseButtonForTest, 0x00F5, 0, 0);
        Assert.IsFalse(scope.View.IsShowingBlame);
        Assert.IsFalse(scope.View.BlameToolbarVisibleForTest);
        Assert.AreEqual(editor, NativeMethods.GetFocus());
        Assert.IsTrue(scope.View.IsTextReadOnly);
        Assert.IsTrue(scope.View.ShowBlame([blame], _ => Assert.Fail("刷新不能打开提交")));
        scope.View.ReloadAsync(scope.Result).GetAwaiter().GetResult();
        Assert.AreEqual(editor, Child(scope.View.Handle, 100));
        Assert.IsFalse(scope.View.IsShowingBlame);
        Assert.IsFalse(scope.View.BlameToolbarVisibleForTest, "刷新清除归属时必须同时恢复普通工具栏。");
        Assert.IsTrue(NativeMethods.IsWindowVisible(Child(scope.View.Handle, 30)));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor, 2253, 0, 0));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor, 2241, 0, 0));
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor, 2247, 0, 0));
    }

    [TestMethod]
    public void 真实文件监听更新保持标签正文焦点和阅读位置()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string path = Path.Combine(workspace, "read.txt");
        string original = CreateText(DocumentKind.Text);
        File.WriteAllText(path, original);
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
                    try
                    {
                        Assert.IsTrue(await window.OpenWorkspaceAsync(workspace));
                        await window.OpenDocumentForTestAsync(path);
                        nint editor = NativeMethods.GetFocus();
                        _ = NativeMethods.SendMessage(editor, 2160, 5000, 4900);
                        _ = NativeMethods.SendMessage(editor, 2613, 45, 0);
                        _ = NativeMethods.SendMessage(editor, 2397, 60, 0);
                        var before = Capture(editor);
                        int layouts = window.LayoutInvocationCountForTest;
                        string updated = original.Replace("key159", "changed159", StringComparison.Ordinal);
                        await File.WriteAllTextAsync(path, updated);
                        Stopwatch timeout = Stopwatch.StartNew();
                        while (window.ActiveDocumentText != updated)
                        {
                            Assert.IsLessThan(5000, timeout.ElapsedMilliseconds, "外部更新未到达正文。");
                            await Task.Delay(10);
                        }
                        Assert.IsTrue(NativeMethods.IsWindow(editor), "文件监听刷新不能销毁正在阅读的控件。");
                        Assert.AreEqual(editor, NativeMethods.GetFocus());
                        Assert.AreEqual(before, Capture(editor));
                        Assert.AreEqual(1, window.OpenDocumentCount);
                        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
                        Assert.IsTrue(window.ActiveDocumentIsReadOnly);
                        Assert.AreEqual(updated, ReadText(editor));
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
        try { completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "外部文件更新测试窗口没有退出。");
        }
    }

    [TestMethod]
    public void JSON格式化查找连续定位复用结果且外部变化重新统计()
    {
        using ViewScope scope = new(DocumentKind.Json, "{\"a\":\"cat\",\"b\":\"cat\"}");
        NativeDocumentView view = scope.View;
        view.ShowFindForTest("cat");
        Assert.AreEqual(UiText.FindResults(2), view.FindStatusForTest);
        int scans = view.FindStatusScanCountForTest;
        Click(view, 10);
        Click(view, 10);
        Click(view, 9);
        Assert.AreEqual(scans, view.FindStatusScanCountForTest, "格式化正文未变时不能因重新格式化而反复扫描。");
        view.ReloadAsync(scope.Result with { Text = "{\"a\":\"cat\",\"b\":\"cat\",\"c\":\"cat\"}" }).GetAwaiter().GetResult();
        Assert.AreEqual("2/3", view.FindStatusForTest);
        Assert.AreEqual(scans + 1, view.FindStatusScanCountForTest);
        Click(view, 10);
        Assert.AreEqual(scans + 1, view.FindStatusScanCountForTest);
        Assert.IsTrue(view.IsTextReadOnly);
    }

    [TestMethod]
    public void JSON显示模式切换立即更新当前可见正文的查找结果()
    {
        using ViewScope scope = new(DocumentKind.Json, "{\"a\":1,\"b\":2}");
        NativeDocumentView view = scope.View;
        view.ShowFindForTest("  ");
        Assert.AreEqual(UiText.FindResults(2), view.FindStatusForTest);
        Click(view, 10);
        Click(view, 1);
        Assert.IsFalse(view.IsShowingAlternative);
        Assert.AreEqual(UiText.FindResults(0), view.FindStatusForTest);
        Click(view, 2);
        Assert.IsTrue(view.IsShowingAlternative);
        Assert.AreEqual(UiText.FindResults(2), view.FindStatusForTest);
        nint formatted = Child(view.Handle, 101);
        string displayed = ReadText(formatted);
        int first = displayed.IndexOf("  ", StringComparison.Ordinal);
        Assert.AreEqual((nint)first, NativeMethods.SendMessage(formatted, 2009, 0, 0), "不同显示文本不能沿用上一模式的匹配位置。");
        Click(view, 2);
        Click(view, 10);
        Assert.AreEqual((nint)displayed.IndexOf("  ", first + 2, StringComparison.Ordinal),
            NativeMethods.SendMessage(formatted, 2009, 0, 0), "重复点击当前显示模式不能让查找重新开始。");
        Assert.AreEqual(scope.Result.Text, view.CurrentText);
        Assert.IsTrue(view.IsTextReadOnly);
    }

    private static (nint Anchor, nint Caret, nint FirstLine, nint XOffset) Capture(nint editor) =>
        (NativeMethods.SendMessage(editor, 2009, 0, 0), NativeMethods.SendMessage(editor, 2008, 0, 0),
            NativeMethods.SendMessage(editor, 2152, 0, 0), NativeMethods.SendMessage(editor, 2398, 0, 0));

    private static nint Child(nint parent, int identifier)
    {
        for (nint child = NativeMethods.GetWindowSibling(parent, 5); child != 0; child = NativeMethods.GetWindowSibling(child, 2))
            if (NativeMethods.GetWindowLongPointer(child, -12) == identifier) return child;
        return 0;
    }

    private static string ReadText(nint editor)
    {
        int length = checked((int)NativeMethods.SendMessage(editor, 2183, 0, 0));
        nint buffer = Marshal.AllocCoTaskMem(length + 1);
        try
        {
            _ = NativeMethods.SendMessage(editor, 2182, (nuint)(length + 1), buffer);
            return Marshal.PtrToStringUTF8(buffer, length) ?? string.Empty;
        }
        finally { Marshal.FreeCoTaskMem(buffer); }
    }

    private static void Click(NativeDocumentView view, int identifier)
    {
        nint button = Child(view.Handle, identifier);
        Assert.AreNotEqual((nint)0, button);
        _ = NativeMethods.SendMessage(button, 0x00F5, 0, 0);
        NativeFindTestPump.Wait(view);
    }

    private static string CreateText(DocumentKind kind)
    {
        string[] lines = Enumerable.Range(0, 160).Select(index => $"  \"key{index:D3}\": \"{new string('x', 160)} 标记\"").ToArray();
        return kind == DocumentKind.Json ? "{\n" + string.Join(",\n", lines) + "\n}" : string.Join('\n', lines);
    }

    private sealed class ViewScope : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new();
        internal ViewScope(DocumentKind kind, string? text = null)
        {
            string path = _temporary.GetPath(kind == DocumentKind.Json ? "sample.json" : "sample.txt");
            string content = text ?? CreateText(kind);
            Result = new(DocumentReadStatus.TextReady, path, path, new(kind, kind == DocumentKind.Json ? "JSON" : "UTF-8 文本"),
                Encoding.UTF8.GetByteCount(content), content, null, null, string.Empty);
            Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, 760, 520,
                0, 0, NativeMethods.GetModuleHandle(null), 0);
            View = new(Owner, _temporary.FullPath, Result, new(), _ => { }, (_, _, _) => { });
            View.SetBounds(0, 0, 740, 500);
        }
        internal nint Owner { get; }
        internal NativeDocumentView View { get; }
        internal DocumentReadResult Result { get; }
        public void Dispose()
        {
            View.Dispose();
            View.FindWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _ = NativeMethods.DestroyWindow(Owner);
            _temporary.Dispose();
        }
    }
}
