using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ScintillaDecorationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 展示留白只改变行间距离且清除和换正文释放留白(bool dark)
    {
        nint owner = CreateOwner();
        try
        {
            using ScintillaControl editor = new(owner, 1);
            const string source = "甲🙂\n乙\n";
            editor.ApplyAppearance("Cascadia Mono", 13, dark);
            editor.SetTextContent(source);
            editor.MarkSaved();
            editor.SelectUtf8Range(1, 2, source);
            nint anchor = NativeMethods.SendMessage(editor.Handle, 2009, 0, 0);
            nint caret = NativeMethods.SendMessage(editor.Handle, 2008, 0, 0);
            nint position = NativeMethods.SendMessage(editor.Handle, 2167, 1, 0);
            int height = (int)NativeMethods.SendMessage(editor.Handle, 2279, 0, 0);
            editor.SetDisplayGapAfterLine(0, 3);
            Assert.AreEqual((nint)3, NativeMethods.SendMessage(editor.Handle, 2546, 0, 0));
            Assert.AreEqual(height * 4, (int)NativeMethods.SendMessage(editor.Handle, 2165, 0, position));
            Assert.AreEqual(source, editor.GetTextContent());
            Assert.AreEqual(anchor, NativeMethods.SendMessage(editor.Handle, 2009, 0, 0));
            Assert.AreEqual(caret, NativeMethods.SendMessage(editor.Handle, 2008, 0, 0));
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2174, 0, 0));
            Assert.IsTrue(editor.IsReadOnly);
            Assert.IsFalse(editor.IsModified);
            editor.ClearDisplayGaps();
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2546, 0, 0));
            Assert.AreEqual(height, (int)NativeMethods.SendMessage(editor.Handle, 2165, 0, position));
            editor.SetDisplayGapAfterLine(0, 1);
            editor.SetTextContent(source);
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2546, 0, 0));
        }
        finally { _ = NativeMethods.DestroyWindow(owner); }
    }

    [TestMethod]
    public void 替换正文后旧行标记不会合并到新首行()
    {
        nint owner = CreateOwner();
        try
        {
            using ScintillaControl editor = new(owner, 1);
            const string before = "上下文\n删除\n新增";
            editor.SetTextContent(before);
            editor.SetLineBackgrounds(0, before, [(4, 2)], 247, 215, 215);
            editor.SetLineBackgrounds(1, before, [(7, 2)], 201, 238, 207);
            editor.SetTextContent("新文档");
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2046, 0, 0));
            Assert.IsTrue(editor.IsReadOnly);
        }
        finally { _ = NativeMethods.DestroyWindow(owner); }
    }

    [TestMethod]
    public void 隐藏冲突标记行不改变正文且替换正文会恢复可见行()
    {
        nint owner = CreateOwner();
        try
        {
            using ScintillaControl editor = new(owner, 1);
            const string source = "<<<<<<< HEAD\n左侧\n=======\n右侧\n>>>>>>> feature\n";
            editor.SetTextContent(source);
            editor.SetHiddenLines([0, 2, 4]);
            Assert.IsFalse(editor.IsLineVisibleForTest(0));
            Assert.IsTrue(editor.IsLineVisibleForTest(1));
            Assert.IsFalse(editor.IsLineVisibleForTest(2));
            Assert.IsTrue(editor.IsLineVisibleForTest(3));
            Assert.IsFalse(editor.IsLineVisibleForTest(4));
            Assert.AreEqual(source, editor.GetTextContent());
            editor.SetTextContent("新正文\n");
            Assert.IsTrue(editor.IsLineVisibleForTest(0));
            Assert.AreEqual("新正文\n", editor.GetTextContent());
        }
        finally { _ = NativeMethods.DestroyWindow(owner); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 差异装饰按UTF8字节定位且保持正文选择与只读状态(bool dark)
    {
        nint owner = CreateOwner();
        try
        {
            using ScintillaControl editor = new(owner, 1);
            using ScintillaControl other = new(owner, 2);
            const string source = "甲🙂\n-乙\n+丙";
            editor.ApplyAppearance("Cascadia Mono", 13, dark);
            editor.SetTextContent(source);
            other.SetTextContent(source);
            editor.SelectUtf8Range(1, 3, source);
            int firstLine = editor.FirstVisibleLine;
            editor.SetDiffStyles(source, [(0, 1)], [(4, 1)], [(7, 1)]);
            editor.SetHighlights(0, source, [(1, 2), (8, 1)], 76, 175, 80);
            editor.SetLineBackgrounds(0, source, [(4, 2)], 247, 215, 215);

            // 使用原生查询读取最终样式，不能只断言托管缓存或格式化中间结果。
            Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor.Handle, 2010, 0, 0));
            Assert.AreEqual((nint)2, NativeMethods.SendMessage(editor.Handle, 2010, 8, 0));
            Assert.AreEqual((nint)3, NativeMethods.SendMessage(editor.Handle, 2010, 13, 0));
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2010, 9, 0));
            foreach (int position in new[] { 3, 4, 5, 6, 14, 15, 16 })
                Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor.Handle, 2507, 0, position));
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2507, 0, 7));
            Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor.Handle, 2046, 1, 0));
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2046, 2, 0));
            Assert.AreEqual((nint)3, NativeMethods.SendMessage(editor.Handle, 2143, 0, 0));
            Assert.AreEqual((nint)7, NativeMethods.SendMessage(editor.Handle, 2145, 0, 0));
            Assert.AreEqual(firstLine, editor.FirstVisibleLine);
            Assert.AreEqual(source, editor.GetTextContent());
            Assert.IsTrue(editor.IsReadOnly);
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(other.Handle, 2010, 0, 0));
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(other.Handle, 2507, 0, 3));
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(other.Handle, 2046, 1, 0));

            editor.SetHighlights(0, source, [], 76, 175, 80);
            editor.SetLineBackgrounds(0, source, [], 247, 215, 215);
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2507, 0, 3));
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2046, 1, 0));
        }
        finally { _ = NativeMethods.DestroyWindow(owner); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 销毁控件或父窗口后装饰拒绝访问失效原生上下文(bool destroyParentFirst)
    {
        nint owner = CreateOwner();
        using ScintillaControl editor = new(owner, 1);
        try
        {
            editor.SetTextContent("正文");
            if (destroyParentFirst) { _ = NativeMethods.DestroyWindow(owner); }
            else { editor.Dispose(); }
            Assert.Throws<ObjectDisposedException>(() => editor.SetDiffStyles("正文", [(0, 1)], [], []));
            Assert.Throws<ObjectDisposedException>(() => editor.SetHighlights(0, "正文", [(0, 1)], 0, 0, 0));
            Assert.Throws<ObjectDisposedException>(() => editor.SetLineBackgrounds(0, "正文", [(0, 1)], 0, 0, 0));
        }
        finally
        {
            if (NativeMethods.IsWindow(owner)) { _ = NativeMethods.DestroyWindow(owner); }
        }
    }

    [TestMethod]
    public Task 跨线程装饰通过窗口消息同步且不误用直接接口() => RunAsync(async (window, editor) =>
    {
        editor.SetTextContent("正文");
        uint ownerThread = NativeMethods.GetCurrentThreadId();
        await Task.Run(() =>
        {
            Assert.AreNotEqual(ownerThread, NativeMethods.GetCurrentThreadId());
            editor.SetDiffStyles("正文", [(0, 1)], [], []);
            editor.SetHighlights(0, "正文", [(1, 1)], 76, 175, 80);
            editor.SetLineBackgrounds(0, "正文", [(0, 1)], 201, 238, 207);
        });
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor.Handle, 2010, 0, 0));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor.Handle, 2507, 0, 3));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor.Handle, 2046, 0, 0));
        Assert.AreEqual("正文", editor.GetTextContent());
        Assert.IsTrue(editor.IsReadOnly);
    });

    [TestMethod]
    [DataRow("样式", "换正文")]
    [DataRow("高亮", "换正文")]
    [DataRow("底色", "换正文")]
    [DataRow("样式", "取消")]
    [DataRow("高亮", "取消")]
    [DataRow("底色", "取消")]
    [DataRow("样式", "销毁")]
    [DataRow("高亮", "销毁")]
    [DataRow("底色", "销毁")]
    [DataRow("样式", "过期")]
    [DataRow("高亮", "过期")]
    [DataRow("底色", "过期")]
    public Task 分批装饰让出消息循环后拒绝继续写入失效内容(string decoration, string invalidation) => RunAsync(async (window, editor) =>
    {
        string source = string.Concat(Enumerable.Repeat("字\n", 100_000));
        (int Start, int Length)[] ranges = Enumerable.Range(0, 100_000).Select(i => (i * 2, 1)).ToArray();
        editor.SetTextContent(source);
        using CancellationTokenSource stop = new();
        bool current = true;
        Task<bool> pending = decoration switch
        {
            "样式" => editor.SetDiffStylesAsync(source, ranges, [], [], () => current, stop.Token),
            "高亮" => editor.SetHighlightsAsync(0, source, ranges, 76, 175, 80, () => current, stop.Token),
            _ => editor.SetLineBackgroundsAsync(0, source, ranges, 201, 238, 207, () => current, stop.Token),
        };
        Assert.IsFalse(pending.IsCompleted, "大量绘制必须让出消息循环，不能一次同步完成。");
        bool inputHandled = false;
        window.Post(() =>
        {
            inputHandled = true;
            switch (invalidation)
            {
                case "换正文": editor.SetTextContent("新内容"); break;
                case "取消": stop.Cancel(); break;
                case "销毁": editor.Dispose(); break;
                case "过期": current = false; break;
            }
        });
        if (invalidation == "取消")
        {
            try { Assert.IsFalse(await pending); }
            catch (OperationCanceledException) { Assert.IsTrue(stop.IsCancellationRequested); }
        }
        else { Assert.IsFalse(await pending); }
        Assert.IsTrue(inputHandled);
        if (invalidation == "换正文")
        {
            Assert.AreEqual("新内容", editor.GetTextContent());
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2010, 0, 0));
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(editor.Handle, 2507, 0, 0));
        }
    });

    [TestMethod]
    public Task 分批与同步装饰具有相同字节样式高亮和整行底色() => RunAsync(async (window, editor) =>
    {
        const string source = "1 -中文🙂\n2 +正文\n";
        using ScintillaControl reference = new(window.Handle, 13002);
        editor.SetTextContent(source);
        reference.SetTextContent(source);
        reference.SetDiffStyles(source, [(0, 2), (9, 2)], [(2, 1)], [(11, 1)]);
        reference.SetHighlights(0, source, [(3, 4)], 219, 88, 96);
        reference.SetLineBackgrounds(0, source, [(9, 5)], 201, 238, 207);
        Assert.IsTrue(await editor.SetDiffStylesAsync(source, [(0, 2), (9, 2)], [(2, 1)], [(11, 1)], () => true, default));
        Assert.IsTrue(await editor.SetHighlightsAsync(0, source, [(3, 4)], 219, 88, 96, () => true, default));
        Assert.IsTrue(await editor.SetLineBackgroundsAsync(0, source, [(9, 5)], 201, 238, 207, () => true, default));
        for (int i = 0; i < System.Text.Encoding.UTF8.GetByteCount(source); i++)
        {
            Assert.AreEqual(NativeMethods.SendMessage(reference.Handle, 2010, (nuint)i, 0),
                NativeMethods.SendMessage(editor.Handle, 2010, (nuint)i, 0));
            Assert.AreEqual(NativeMethods.SendMessage(reference.Handle, 2507, 0, i),
                NativeMethods.SendMessage(editor.Handle, 2507, 0, i));
        }
        for (int i = 0; i < 3; i++)
            Assert.AreEqual(NativeMethods.SendMessage(reference.Handle, 2046, (nuint)i, 0),
                NativeMethods.SendMessage(editor.Handle, 2046, (nuint)i, 0));
        Assert.IsTrue(editor.IsReadOnly);
        Assert.AreEqual(source, editor.GetTextContent());
    });

    private static async Task RunAsync(Func<MainWindow, ScintillaControl, Task> scenario)
    {
        using TemporaryDirectory temporary = new();
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
                using ScintillaControl editor = new(window.Handle, 13001);
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();

                async Task VerifyAsync()
                {
                    try
                    {
                        await scenario(window, editor);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { window.Close(); }
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Volatile.Write(ref active, null);
                if (failure is null) { completion.TrySetResult(); }
                else { completion.TrySetException(failure); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "装饰测试消息循环未退出。");
        }
    }

    private static nint CreateOwner()
    {
        nint owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStylePopup, 0, 0, 400, 300, 0, 0, NativeMethods.GetModuleHandle(null), 0);
        Assert.AreNotEqual((nint)0, owner);
        return owner;
    }
}
