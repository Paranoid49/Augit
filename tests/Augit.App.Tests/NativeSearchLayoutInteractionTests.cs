using System.Runtime.InteropServices;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeSearchLayoutInteractionTests
{
    private static readonly int[] OptionIds = [4, 5, 6, 7];
    public static IEnumerable<object[]> LayoutCases()
    {
        foreach (bool text in new[] { false, true })
            foreach (int dpi in new[] { 96, 120, 144 })
                foreach (int size in new[] { 13, 40 })
                    foreach (int width in new[] { 320, 730 })
                        yield return [text, dpi, size, width];
    }

    [TestMethod]
    [DynamicData(nameof(LayoutCases))]
    public void 搜索空态结果和长说明随真实字高排布且不抢焦点(bool text, int dpi, int size, int width)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string family = NativeTheme.UiFontFamilyForTest; double originalSize = NativeTheme.UiFontSizeForTest;
        NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
        try
        {
            using Scope scope = new(text, width);
            int emptyHeight = Bounds(scope.Panel.Handle).Bottom - Bounds(scope.Panel.Handle).Top;
            if (size == 13) Assert.AreEqual(S(80), emptyHeight);
            scope.AssertLayout(text);
            scope.Panel.SetQueryForTest("alpha"); scope.Wait();
            Assert.AreEqual(3, scope.Panel.ResultCount);
            Assert.AreEqual(scope.Edit, NativeMethods.GetFocus());
            Assert.IsGreaterThan(emptyHeight, Bounds(scope.Panel.Handle).Bottom - Bounds(scope.Panel.Handle).Top);
            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight + S(8), scope.Panel.ResultRowHeightForTest);
            scope.AssertLayout(text);
            if (text)
            {
                int searches = scope.Panel.SearchStartCountForTest;
                string longReason = string.Join("\r\n", Enumerable.Repeat("搜索表达式无效，请检查括号后重试。", 12));
                _ = NativeMethods.SetWindowText(scope.Notice, longReason); scope.Layout();
                scope.AssertLayout(true);
                Assert.AreEqual(longReason, scope.Panel.NoticeTextForTest);
                uint style = unchecked((uint)NativeMethods.GetWindowLongPointer(scope.Notice, NativeMethods.WindowLongStyle));
                Assert.AreNotEqual(0u, style & NativeMethods.EditReadOnly);
                Assert.AreNotEqual(0u, style & NativeMethods.WindowStyleVerticalScroll);
                _ = NativeMethods.SendMessage(scope.Notice, NativeMethods.WindowMessageVerticalScroll, 7, 0);
                Assert.IsGreaterThan((nint)0, NativeMethods.SendMessage(scope.Notice, 0x00CE, 0, 0), "长原因必须能够滚到后续行。");
                Assert.AreEqual(searches, scope.Panel.SearchStartCountForTest, "布局和阅读错误不能重启查询。");
                Assert.AreEqual(scope.Edit, NativeMethods.GetFocus());
            }
            scope.Panel.SetQueryForTest(string.Empty); scope.Wait();
            Assert.AreEqual(emptyHeight, Bounds(scope.Panel.Handle).Bottom - Bounds(scope.Panel.Handle).Top);
            Assert.IsFalse(scope.Panel.NoticeVisibleForTest);
        }
        finally { NativeTheme.ConfigureUiTypography(family, originalSize); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 搜索开关真实绘制选中悬停禁用并保留提示和查询(bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(96);
        using Scope scope = new(true, 730, dark);
        var palette = NativeTheme.Palette(dark);
        foreach (int id in new[] { 4, 5, 6 })
        {
            nint button = Child(scope.Panel.Handle, id);
            Assert.IsTrue(scope.Panel.OptionHasTooltipForTest(button));
            Assert.AreNotEqual(string.Empty, NativeMethods.GetWindowTextValue(button));
            Assert.AreEqual(palette.Panel, Pixel(button, 5, 5));
            _ = NativeMethods.SendMessage(button, 0x00F5, 0, 0);
            Assert.AreEqual(palette.AccentSoft, Pixel(button, 5, 5), "开关启用必须有选中底色。");
            _ = NativeMethods.SendMessage(button, 0x00F5, 0, 0);
            _ = NativeMethods.SendMessage(button, NativeMethods.WindowMessageMouseMove, 0, (nint)((8 << 16) | 8));
            Assert.AreEqual(button, scope.Panel.HoveredOptionForTest);
            Assert.AreEqual(palette.Hover, Pixel(button, 5, 5));
            _ = NativeMethods.EnableWindow(button, false);
            Assert.AreEqual((nint)0, scope.Panel.HoveredOptionForTest);
            Assert.AreEqual(palette.Panel, Pixel(button, 5, 5));
            _ = NativeMethods.EnableWindow(button, true);
        }
        int before = scope.Panel.SearchStartCountForTest;
        scope.Panel.FocusSearchBox();
        Assert.IsTrue(scope.Panel.HandleTabNavigation(false));
        Assert.AreEqual(Child(scope.Panel.Handle, 4), NativeMethods.GetFocus());
        Assert.IsTrue(scope.Panel.HandleTabNavigation(true));
        Assert.AreEqual(scope.Edit, NativeMethods.GetFocus());
        Assert.AreEqual(before, scope.Panel.SearchStartCountForTest);
    }

    private sealed class Scope : IDisposable
    {
        private readonly TemporaryDirectory _workspace = new();
        private readonly int _width;
        private readonly nint _owner;
        internal NativeSearchPanel Panel { get; }
        internal nint Edit => Child(Panel.Handle, 1);
        internal nint Notice => Child(Panel.Handle, 8);
        internal Scope(bool text, int width, bool dark = false)
        {
            _width = S(width);
            foreach (int index in new[] { 1, 2, 3 }) File.WriteAllText(_workspace.GetPath($"alpha{index}.txt"), "alpha");
            _owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, S(1000), S(900),
                0, 0, NativeMethods.GetModuleHandle(null), 0);
            Panel = new(_owner, _workspace.FullPath, text ? WorkspaceSearchMode.Text : WorkspaceSearchMode.FileNames,
                (_, _) => { }, (_, _) => { }, () => { }, _ => { }, Layout, () => dark);
            Layout();
        }
        internal void Layout()
        {
            if (Panel is not null) Panel.SetBounds(S(10), S(10), _width, Math.Min(S(600), Panel.PreferredHeight(_width)));
        }
        internal void AssertLayout(bool text)
        {
            var panel = Bounds(Panel.Handle);
            var edit = Bounds(Edit);
            Contains(panel, edit);
            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, edit.Bottom - edit.Top);
            if (text)
            {
                var options = OptionIds.Select(id => Bounds(Child(Panel.Handle, id))).ToArray();
                foreach (var option in options) { Contains(panel, option); Assert.IsLessThan(edit.Top, option.Bottom); }
                for (int i = 1; i < options.Length; i++)
                    Assert.IsTrue(options[i].Left >= options[i - 1].Right || options[i].Top >= options[i - 1].Bottom);
                Assert.IsGreaterThanOrEqualTo(NativeDialogBody.Measure(Panel.Handle, UiText.IncludeIgnoredFiles).Width + S(22),
                    options[3].Right - options[3].Left);
            }
            if (Panel.ResultCount > 0)
            {
                var list = Bounds(Child(Panel.Handle, 2)); Contains(panel, list);
                Assert.IsGreaterThanOrEqualTo(edit.Bottom, list.Top);
                Assert.IsGreaterThanOrEqualTo(Panel.ResultRowHeightForTest, list.Bottom - list.Top);
                if (Panel.NoticeVisibleForTest) Assert.IsGreaterThanOrEqualTo(list.Bottom, Bounds(Notice).Top);
            }
            if (Panel.NoticeVisibleForTest) Assert.IsTrue(Panel.NoticeWithinBoundsForTest);
        }
        internal void Wait()
        {
            long deadline = Environment.TickCount64 + 5000;
            while (!Panel.SearchCompletedForTest || !Panel.SearchWorkerForTest.IsCompleted)
            {
                NativeFindTestPump.Dispatch(Panel.Handle);
                if (Environment.TickCount64 > deadline) Assert.Fail("搜索布局测试超时。");
                Thread.Sleep(1);
            }
        }
        public void Dispose()
        {
            Panel.Dispose();
            Panel.SearchWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _ = NativeMethods.DestroyWindow(_owner);
            _workspace.Dispose();
        }
    }

    private static uint Pixel(nint window, int x, int y)
    {
        _ = NativeMethods.GetClientRectangle(window, out var bounds);
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new() { Header = new() { Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(), Width = bounds.Right, Height = -bounds.Bottom, Planes = 1, BitCount = 32 } };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        nint old = NativeMethods.SelectObject(dc, bitmap);
        try { Assert.IsTrue(PrintWindow(window, dc, 2)); return NativeMethods.GetPixel(dc, x, y); }
        finally { _ = NativeMethods.SelectObject(dc, old); _ = NativeMethods.DeleteObject(bitmap); _ = NativeMethods.DeleteDeviceContext(dc); }
    }
    private static NativeMethods.Rectangle Bounds(nint handle) { Assert.IsTrue(NativeMethods.GetWindowRectangle(handle, out var r)); return r; }
    private static void Contains(NativeMethods.Rectangle parent, NativeMethods.Rectangle child) =>
        Assert.IsTrue(child.Left >= parent.Left && child.Right <= parent.Right && child.Top >= parent.Top && child.Bottom <= parent.Bottom, "搜索控件越界。");
    private static int S(int value) => NativeTheme.Scale(value);
    [DllImport("user32.dll", EntryPoint = "GetDlgItem")] private static extern nint Child(nint window, int id);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(nint window, nint dc, uint flags);
}
