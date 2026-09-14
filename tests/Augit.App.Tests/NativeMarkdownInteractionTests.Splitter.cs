namespace Augit.App.Tests;

public sealed partial class NativeMarkdownInteractionTests
{
    private static readonly string[] SplitterCancelReasons = ["取消消息", "Esc", "捕获转移", "原文", "预览", "隐藏", "禁用"];

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public Task 对照拖动保留按下偏移且越界与重复移动不跳动或重复布局(int dpi) => RunAsync(async (window, workspace) =>
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "sample.md"));
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        await view.ShowMarkdownSplitForTestAsync();
        view.SetBounds(0, 0, NativeTheme.Scale(1000), NativeTheme.Scale(500));
        view.ShowFindForTest("段落");
        nint source = Child(view.Handle, 100);
        nint focus = NativeMethods.GetFocus();
        var reading = SourceState(source);
        using SplitterLayoutProbe sourceProbe = new(source);
        using SplitterLayoutProbe toolbarProbe = new(Child(view.Handle, 1));
        using SplitterLayoutProbe findProbe = new(Child(view.Handle, 20));
        int before = view.MarkdownSplitSourceWidthForTest;
        int x = before + NativeDocumentView.MarkdownSplitterWidthForTest - 1;
        int y = view.ContentTopForTest + 20;
        int minimum = NativeTheme.Scale(240);
        int maximum = NativeTheme.Scale(1000) - NativeDocumentView.MarkdownSplitterWidthForTest - minimum;
        try
        {
            SplitterMessage(view, NativeMethods.WindowMessageLeftButtonDown, x, y);
            Assert.AreEqual(view.Handle, NativeMethods.GetCapture());
            Assert.AreEqual(before, view.MarkdownSplitSourceWidthForTest, "按下不能使分隔线跳到鼠标中心。");
            SplitterMessage(view, NativeMethods.WindowMessageMouseMove, x, y);
            Assert.AreEqual(0, sourceProbe.BoundsChanges);
            int delta = NativeTheme.Scale(30);
            SplitterMessage(view, NativeMethods.WindowMessageMouseMove, x + delta, y);
            Assert.AreEqual(before + delta, view.MarkdownSplitSourceWidthForTest);
            Assert.IsGreaterThan(0, sourceProbe.BoundsChanges);
            RepeatWithoutLayout(x + delta);
            SplitterMessage(view, NativeMethods.WindowMessageMouseMove, -25, y);
            Assert.AreEqual(minimum, view.MarkdownSplitSourceWidthForTest, "负坐标不能被解码成最右侧。");
            RepeatWithoutLayout(-100);
            SplitterMessage(view, NativeMethods.WindowMessageMouseMove, NativeTheme.Scale(1000) + 25, y);
            Assert.AreEqual(maximum, view.MarkdownSplitSourceWidthForTest);
            RepeatWithoutLayout(NativeTheme.Scale(1000) + 100);
            SplitterMessage(view, NativeMethods.WindowMessageLeftButtonUp, x, y);
            Assert.AreEqual(before, view.MarkdownSplitSourceWidthForTest, "释放位置也必须作为最终位置接纳。");
            Assert.AreNotEqual(view.Handle, NativeMethods.GetCapture());
            SplitterMessage(view, NativeMethods.WindowMessageMouseMove, -25, y);
            Assert.AreEqual(before, view.MarkdownSplitSourceWidthForTest);
            Assert.AreEqual(0, toolbarProbe.BoundsChanges, "拖动只重排正文，不重排工具栏。");
            Assert.AreEqual(0, findProbe.BoundsChanges, "拖动不能重新排布或抢占查找输入框。");
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.AreEqual(reading, SourceState(source));
            Assert.IsTrue(view.IsTextReadOnly);
        }
        finally { view.TryCancelMarkdownSplitterDrag(); }

        void RepeatWithoutLayout(int atX)
        {
            int count = sourceProbe.BoundsChanges;
            int width = view.MarkdownSplitSourceWidthForTest;
            for (int i = 0; i < 20; i++) SplitterMessage(view, NativeMethods.WindowMessageMouseMove, atX, y);
            Assert.AreEqual(count, sourceProbe.BoundsChanges, "同一实际宽度不应重复布局。");
            Assert.AreEqual(width, view.MarkdownSplitSourceWidthForTest);
        }
    });

    [TestMethod]
    public Task 对照拖动在取消切换隐藏失去捕获和关闭后停止且不影响下一次拖动() => RunAsync(async (window, workspace) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "sample.md"));
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        await view.ShowMarkdownSplitForTestAsync();
        int y = view.ContentTopForTest + 20;
        try
        {
            foreach (string reason in SplitterCancelReasons)
            {
                int atX = Begin();
                SplitterMessage(view, NativeMethods.WindowMessageMouseMove, atX + 10, y);
                int width = view.MarkdownSplitSourceWidthForTest;
                switch (reason)
                {
                    case "取消消息": _ = NativeMethods.SendMessage(view.Handle, NativeMethods.WindowMessageCancelMode, 0, 0); break;
                    case "Esc":
                        view.ShowFindForTest("段落");
                        nint edit = NativeMethods.GetFocus();
                        _ = NativeMethods.PostMessage(edit, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
                        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                        while (NativeMethods.GetCapture() == view.Handle && DateTime.UtcNow < deadline) await Task.Delay(10);
                        Assert.IsTrue(view.FindOverlayVisibleForTest, "第一次 Esc 只结束拖动，不能同时收起查找条。");
                        view.HideFind();
                        break;
                    case "捕获转移": _ = NativeMethods.SetCapture(Child(view.Handle, 100)); break;
                    case "原文": Click(view, 1); break;
                    case "预览": await view.ShowMarkdownPreviewForTestAsync(); break;
                    case "隐藏": view.SetVisible(false); break;
                    case "禁用": _ = NativeMethods.EnableWindow(view.Handle, false); break;
                }
                Assert.AreNotEqual(view.Handle, NativeMethods.GetCapture(), reason);
                SplitterMessage(view, NativeMethods.WindowMessageMouseMove, -50, y);
                SplitterMessage(view, NativeMethods.WindowMessageLeftButtonUp, -50, y);
                Assert.AreEqual(width, view.MarkdownSplitSourceWidthForTest, reason);
                if (reason is "隐藏" or "禁用")
                {
                    SplitterMessage(view, NativeMethods.WindowMessageLeftButtonDown, width + 2, y);
                    Assert.AreNotEqual(view.Handle, NativeMethods.GetCapture(), "隐藏或禁用视图拒绝旧按下消息。");
                }
                if (reason == "捕获转移")
                {
                    Assert.AreEqual(Child(view.Handle, 100), NativeMethods.GetCapture(), "旧拖动不能释放新控件的捕获。");
                    _ = NativeMethods.ReleaseCapture();
                }
                _ = NativeMethods.EnableWindow(view.Handle, true);
                view.SetVisible(true);
                await view.ShowMarkdownSplitForTestAsync();
                Assert.AreEqual(width, view.MarkdownSplitSourceWidthForTest, "恢复对照保留已显示比例。");
            }
            _ = Begin();
            nint handle = view.Handle;
            window.CloseActiveTabForTest();
            Assert.IsFalse(NativeMethods.IsWindow(handle));
            Assert.AreNotEqual(handle, NativeMethods.GetCapture());
        }
        finally
        {
            view.TryCancelMarkdownSplitterDrag();
            if (view.Handle != 0) { _ = NativeMethods.EnableWindow(view.Handle, true); view.SetVisible(true); }
        }

        int Begin()
        {
            int atX = view.MarkdownSplitSourceWidthForTest + NativeDocumentView.MarkdownSplitterWidthForTest / 2;
            SplitterMessage(view, NativeMethods.WindowMessageLeftButtonDown, atX, y);
            Assert.AreEqual(view.Handle, NativeMethods.GetCapture());
            return atX;
        }
    });

    [TestMethod]
    public Task 对照拖动期间缩到不足两栏宽度不会异常且恢复原比例() => RunAsync(async (window, workspace) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "sample.md"));
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        await view.ShowMarkdownSplitForTestAsync();
        int width = NativeTheme.Scale(1000);
        int height = NativeTheme.Scale(500);
        view.SetBounds(0, 0, width, height);
        int before = view.MarkdownSplitSourceWidthForTest;
        int x = before + NativeDocumentView.MarkdownSplitterWidthForTest / 2;
        int y = view.ContentTopForTest + 20;
        try
        {
            SplitterMessage(view, NativeMethods.WindowMessageLeftButtonDown, x, y);
            foreach (int narrow in new[] { 0, 1, NativeDocumentView.MarkdownSplitterWidthForTest,
                NativeDocumentView.MarkdownSplitterWidthForTest + 1, NativeDocumentView.MarkdownSplitterWidthForTest + NativeTheme.Scale(480) - 1 })
            {
                view.SetBounds(0, 0, narrow, height);
                int constrained = view.MarkdownSplitSourceWidthForTest;
                SplitterMessage(view, NativeMethods.WindowMessageMouseMove, -25, y);
                Assert.AreEqual(constrained, view.MarkdownSplitSourceWidthForTest);
                SplitterMessage(view, NativeMethods.WindowMessageMouseMove, width + 25, y);
                Assert.AreEqual(constrained, view.MarkdownSplitSourceWidthForTest);
            }
            view.SetBounds(0, 0, width, height);
            Assert.AreEqual(before, view.MarkdownSplitSourceWidthForTest);
            SplitterMessage(view, NativeMethods.WindowMessageLeftButtonUp, x, y);
            Assert.AreNotEqual(view.Handle, NativeMethods.GetCapture());
        }
        finally { view.TryCancelMarkdownSplitterDrag(); }
    });

    private static void SplitterMessage(NativeDocumentView view, uint message, int x, int y) =>
        _ = NativeMethods.SendMessage(view.Handle, message, message == NativeMethods.WindowMessageLeftButtonUp ? 0u : 1u,
            unchecked((nint)((ushort)x | (uint)(ushort)y << 16)));

    private sealed class SplitterLayoutProbe : IDisposable
    {
        private readonly nint _window;
        private readonly NativeMethods.SubclassProcedure _procedure;
        internal int BoundsChanges { get; private set; }

        internal SplitterLayoutProbe(nint window)
        {
            Assert.AreNotEqual((nint)0, window);
            _window = window;
            _procedure = (handle, message, word, parameter, _, _) =>
            {
                if (message == 0x0047) BoundsChanges++; // WM_WINDOWPOSCHANGED：记录实际控件布局，而非测试专用计数。
                return NativeMethods.DefaultSubclassProcedure(handle, message, word, parameter);
            };
            Assert.IsTrue(NativeMethods.SetWindowSubclass(_window, _procedure, 79, 0));
        }

        public void Dispose()
        {
            _ = NativeMethods.RemoveWindowSubclass(_window, _procedure, 79);
            GC.KeepAlive(_procedure);
        }
    }
}
