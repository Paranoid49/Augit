using System.Runtime.InteropServices;
using System.Text;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ScintillaViewportTests
{
    [TestMethod]
    [DataRow(96, 13, 22)]
    [DataRow(120, 13, 28)]
    [DataRow(144, 13, 33)]
    [DataRow(96, 26, 44)]
    [DataRow(96, 40, 68)]
    public void 正文与行号的实际行高遵循视觉稿(int dpi, int size, int expected)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        using ViewScope scope = new();
        scope.Editor.ApplyAppearance("Cascadia Mono", size, false);
        scope.Editor.SetTextContent("first\n中文 second\nthird");
        Assert.AreEqual((nint)expected, NativeMethods.SendMessage(scope.Editor.Handle, 2279, 0, 0));
        scope.Paint();
        int y0 = (int)NativeMethods.SendMessage(scope.Editor.Handle, 2165, 0, 0);
        int y1 = (int)NativeMethods.SendMessage(scope.Editor.Handle, 2165, 0, 6);
        Assert.AreEqual(expected, y1 - y0, "检查真实行位置，不能只检查配置值。");
    }

    [TestMethod]
    public void 短文本和空文件不保留横向滚动条()
    {
        using ViewScope scope = new();
        foreach (string text in new[] { "", "short\n短文本", "{\n  \"a\": 1\n}" })
        {
            scope.Editor.SetTextContent(text);
            scope.Paint();
            Assert.IsFalse(scope.HorizontalBar, $"短文件不应产生空滚动范围：{text}");
        }
    }

    [TestMethod]
    public void 长行可滚动且窗口扩宽后不显示无用滚动条()
    {
        using ViewScope scope = new();
        scope.Editor.SetTextContent(new string('中', 80) + "\tend");
        scope.Paint();
        Assert.IsTrue(scope.HorizontalBar, $"width={NativeMethods.SendMessage(scope.Editor.Handle, 2275, 0, 0)}, tracking={NativeMethods.SendMessage(scope.Editor.Handle, 2517, 0, 0)}");
        int tracked = (int)NativeMethods.SendMessage(scope.Editor.Handle, 2275, 0, 0);
        Assert.IsGreaterThan(600, tracked);
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2397, 120, 0);
        Assert.AreEqual((nint)120, NativeMethods.SendMessage(scope.Editor.Handle, 2398, 0, 0));
        scope.Editor.SetBounds(0, 0, tracked + 100, 280);
        scope.Paint();
        Assert.IsFalse(scope.HorizontalBar);
        scope.Editor.SetBounds(0, 0, 400, 280);
        scope.Paint();
        Assert.IsTrue(scope.HorizontalBar);
    }

    [TestMethod]
    public void 长行外部更新保持阅读位置且改短后收回滚动范围()
    {
        using ViewScope scope = new();
        string text = string.Join('\n', Enumerable.Repeat(new string('x', 160), 90));
        scope.Editor.SetTextContent(text);
        scope.Paint();
        _ = NativeMethods.SetFocus(scope.Editor.Handle);
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2160, 6000, 5900);
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2613, 35, 0);
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2397, 120, 0);
        scope.Paint();
        var before = scope.Position;
        scope.Editor.SetTextContent(text + "\nchanged");
        scope.Paint();
        Assert.AreEqual(before, scope.Position);
        Assert.AreEqual(scope.Editor.Handle, NativeMethods.GetFocus());
        scope.Editor.SetTextContent("short");
        scope.Paint();
        Assert.IsFalse(scope.HorizontalBar);
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(scope.Editor.Handle, 2398, 0, 0));
        Assert.IsTrue(scope.Editor.IsReadOnly);
    }

    [TestMethod]
    public void 开关换行后长行仍能完整查看且禁用的比较行号滚动条不重现()
    {
        using ViewScope scope = new();
        scope.Editor.SetTextContent(new string('x', 180));
        scope.Editor.SetWordWrap(true);
        scope.Paint();
        Assert.IsFalse(scope.HorizontalBar);
        scope.Editor.SetWordWrap(false);
        scope.Paint();
        Assert.IsTrue(scope.HorizontalBar);
        scope.Editor.SetScrollBarsVisible(false, false);
        scope.Editor.ApplyAppearance("Consolas", 20, true);
        scope.Editor.SetTextContent(new string('中', 300));
        scope.Paint();
        Assert.IsFalse(scope.HorizontalBar);
    }

    [TestMethod]
    public void 稳定正文重复绘制不扩大滚动范围或改变阅读位置()
    {
        using ViewScope scope = new();
        scope.Editor.SetTextContent(new string('x', 400));
        scope.Paint();
        nint width = NativeMethods.SendMessage(scope.Editor.Handle, 2275, 0, 0);
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2397, 80, 0);
        var before = scope.Position;
        for (int i = 0; i < 20; i++) scope.Paint();
        Assert.AreEqual(width, NativeMethods.SendMessage(scope.Editor.Handle, 2275, 0, 0));
        Assert.AreEqual(before, scope.Position);
    }

    [TestMethod]
    public void 未显示的长行到达后才扩展滚动范围()
    {
        using ViewScope scope = new();
        scope.Editor.SetTextContent(string.Join('\n', Enumerable.Repeat("short", 80)) + "\n" + new string('中', 300));
        scope.Paint();
        Assert.IsFalse(scope.HorizontalBar, "不能为首屏外正文先度量整个文档。");
        scope.Editor.GoToLine(81, focus: false);
        scope.Paint();
        Assert.IsTrue(scope.HorizontalBar);
        Assert.IsGreaterThan((nint)2000, NativeMethods.SendMessage(scope.Editor.Handle, 2275, 0, 0));
    }

    [TestMethod]
    public void 隐藏正文更新与字体改变后显现不抢焦点()
    {
        using ViewScope scope = new();
        scope.Editor.SetTextContent(new string('中', 100));
        scope.Paint();
        scope.Editor.SetVisible(false);
        _ = NativeMethods.SetFocus(NativeMethods.GetParent(scope.Editor.Handle));
        nint focus = NativeMethods.GetFocus();
        scope.Editor.SetTextContent("short");
        scope.Editor.ApplyAppearance("Consolas", 26, true);
        scope.Editor.SetVisible(true);
        scope.Paint();
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.IsFalse(scope.HorizontalBar);
        Assert.AreEqual("short", scope.Editor.GetTextContent());
    }

    [TestMethod]
    public void 原生窗口提前销毁仍清理滚动登记()
    {
        int before = ScintillaControl.ViewportRegistrationCountForTest;
        using (ViewScope scope = new())
        {
            Assert.AreEqual(before + 1, ScintillaControl.ViewportRegistrationCountForTest);
            _ = NativeMethods.DestroyWindow(scope.Editor.Handle);
            Assert.AreEqual(before, ScintillaControl.ViewportRegistrationCountForTest);
        }
        Assert.AreEqual(before, ScintillaControl.ViewportRegistrationCountForTest);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 可撤销块替换保持未改正文的选区方向与双向滚动(bool selectionInside)
    {
        using ViewScope scope = new();
        string prefix = string.Concat(Enumerable.Repeat(new string('中', 90) + "😀\n", 60));
        const string block = "旧的冲突😀内容\n第二行\n";
        const string replacement = "新的😀结果\n";
        string suffix = string.Concat(Enumerable.Repeat(new string('后', 90) + "😀\n", 60));
        string original = prefix + block + suffix;
        scope.Editor.SetTextContent(original);
        scope.Editor.SetEditable(true);
        scope.Editor.MarkSaved();
        int start = Encoding.UTF8.GetByteCount(prefix);
        int length = Encoding.UTF8.GetByteCount(block);
        int replacementLength = Encoding.UTF8.GetByteCount(replacement);
        int anchor = selectionInside ? start + 9 : start + length + 15;
        int caret = selectionInside ? start + 3 : start + length + 3;
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2160, (nuint)anchor, caret);
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2613, 20, 0);
        scope.Paint();
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2397, 120, 0);
        scope.Paint();
        var before = scope.Position;
        nint focus = NativeMethods.GetFocus();
        scope.Editor.ReplaceEditableRange(start, length, replacement);
        scope.Paint();
        Assert.AreEqual(prefix + replacement + suffix, scope.Editor.GetTextContent());
        Assert.IsFalse(scope.Editor.IsReadOnly);
        Assert.IsTrue(scope.Editor.IsModified);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.AreEqual(before.FirstLine, scope.Position.FirstLine);
        Assert.AreEqual(before.Offset, scope.Position.Offset);
        Assert.AreEqual((nint)(selectionInside ? start + 6 : anchor - length + replacementLength), scope.Position.Anchor,
            "旧块内的字节位置不能落在新 emoji 的中间；块后选区仍指向同一段正文。");
        Assert.AreEqual((nint)(selectionInside ? start + 3 : caret - length + replacementLength), scope.Position.Caret);
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2176, 0, 0);
        Assert.AreEqual(original, scope.Editor.GetTextContent());
        Assert.IsFalse(scope.Editor.IsModified);
        _ = NativeMethods.SendMessage(scope.Editor.Handle, 2011, 0, 0);
        Assert.AreEqual(prefix + replacement + suffix, scope.Editor.GetTextContent());
    }

    [TestMethod]
    public void 块编辑拒绝只读正文和非法字节边界且不留下撤销()
    {
        using ViewScope scope = new();
        const string original = "中文😀\n";
        scope.Editor.SetTextContent(original);
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Editor.ReplaceEditableRange(0, 3, "改"));
        scope.Editor.SetEditable(true);
        scope.Editor.MarkSaved();
        Assert.ThrowsExactly<ArgumentException>(() => scope.Editor.ReplaceEditableRange(1, 2, "改"));
        Assert.ThrowsExactly<ArgumentException>(() => scope.Editor.ReplaceEditableRange(0, 4, "改"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => scope.Editor.ReplaceEditableRange(0, 100, "改"));
        Assert.AreEqual(original, scope.Editor.GetTextContent());
        Assert.IsFalse(scope.Editor.IsModified);
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(scope.Editor.Handle, 2174, 0, 0));
    }

    private sealed class ViewScope : IDisposable
    {
        private readonly nint _owner;
        internal ViewScope()
        {
            _owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, 1600, 400,
                0, 0, NativeMethods.GetModuleHandle(null), 0);
            Editor = new(_owner, 1);
            Editor.ApplyAppearance("Cascadia Mono", 13, false);
            Editor.SetBounds(0, 0, 400, 280);
        }
        internal ScintillaControl Editor { get; }
        internal bool HorizontalBar => (NativeMethods.GetWindowLongPointer(Editor.Handle, NativeMethods.WindowLongStyle)
            & (nint)NativeMethods.WindowStyleHorizontalScroll) != 0;
        internal (nint Anchor, nint Caret, nint FirstLine, nint Offset) Position =>
            (NativeMethods.SendMessage(Editor.Handle, 2009, 0, 0), NativeMethods.SendMessage(Editor.Handle, 2008, 0, 0),
                NativeMethods.SendMessage(Editor.Handle, 2152, 0, 0), NativeMethods.SendMessage(Editor.Handle, 2398, 0, 0));
        internal void Paint()
        {
            for (int index = 0; index < 3; index++)
            {
                _ = NativeMethods.InvalidateRectangle(Editor.Handle, 0, false);
                _ = UpdateWindow(Editor.Handle);
                NativeFindTestPump.Dispatch(Editor.Handle);
            }
        }
        public void Dispose()
        {
            Editor.Dispose();
            _ = NativeMethods.DestroyWindow(_owner);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateWindow(nint window);
}
