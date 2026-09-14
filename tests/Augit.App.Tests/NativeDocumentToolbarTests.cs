using System.Runtime.InteropServices;
using Augit.Core.Documents;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeDocumentToolbarTests
{
    [TestMethod]
    public void 普通文本按可见顺序Tab且Enter与Space能连续切换开关()
    {
        using ViewScope scope = new(DocumentKind.Text);
        int[] order = [6, 7, 4, 5, 100];
        _ = NativeMethods.SetFocus(scope.Child(order[0]));
        foreach (int id in order.Skip(1))
        {
            Assert.IsTrue(scope.View.HandleTabNavigation(false, 0));
            Assert.AreEqual(scope.Child(id), NativeMethods.GetFocus());
        }
        foreach (int id in order.Reverse().Skip(1))
        {
            Assert.IsTrue(scope.View.HandleTabNavigation(true, 0));
            Assert.AreEqual(scope.Child(id), NativeMethods.GetFocus());
        }
        Assert.IsTrue(scope.Enter(6));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(scope.Child(100), 2269, 0, 0));
        Assert.AreEqual(scope.Child(6), NativeMethods.GetFocus());
        scope.Space(6);
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(scope.Child(100), 2269, 0, 0));
        Assert.IsTrue(scope.Enter(7));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(scope.Child(100), 2020, 0, 0));
        Assert.IsTrue(scope.Enter(4));
        Assert.AreEqual(scope.Child(20), NativeMethods.GetFocus(), "搜索动作应进入输入框。");
        Assert.IsFalse(scope.Enter(20), "不能截获输入框的 Enter。");
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    public void JSON模式按键只替换显示并保留按钮焦点和正文句柄()
    {
        using ViewScope scope = new(DocumentKind.Json);
        nint original = scope.Child(100), formatted = scope.Child(101);
        Assert.IsTrue(scope.Enter(1));
        Assert.IsFalse(scope.View.IsShowingAlternative);
        Assert.AreEqual(scope.Child(1), NativeMethods.GetFocus());
        Assert.IsTrue(scope.View.HandleTabNavigation(false, 0));
        Assert.AreEqual(scope.Child(2), NativeMethods.GetFocus());
        Assert.IsTrue(scope.Enter(2));
        Assert.IsTrue(scope.View.IsShowingAlternative);
        Assert.AreEqual(scope.Child(2), NativeMethods.GetFocus());
        Assert.IsTrue(scope.View.HandleTabNavigation(false, 0));
        Assert.AreEqual(scope.Child(15), NativeMethods.GetFocus());
        Assert.AreEqual(original, scope.Child(100));
        Assert.AreEqual(formatted, scope.Child(101));
        scope.View.ReloadAsync(scope.Result with { Text = "{" }).GetAwaiter().GetResult();
        Assert.IsFalse(scope.Enter(2), "无效 JSON 禁用格式化后不能由 Enter 激活。");
        Assert.IsFalse(scope.View.IsShowingAlternative);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 图片文字随字体度量并保留完整动作和焦点(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        using ViewScope scope = new(DocumentKind.Png);
        StringAssert.Contains(NativeMethods.GetWindowTextValue(scope.Child(32)), "1 × 1 · 测试文件 · 1 B");
        foreach (int size in new[] { 13, 40, 19 })
        {
            scope.Appearance(size);
            // 宽图片尺寸用于验证字宽，不为布局检查分配大位图。
            _ = NativeMethods.SetWindowText(scope.Child(32), "1920 × 1200");
            scope.View.SetBounds(0, 0, NativeTheme.Scale(656 + size), NativeTheme.Scale(490));
            var dimensions = Bounds(scope.Child(32));
            var zoom = Bounds(scope.Child(33));
            Assert.IsGreaterThanOrEqualTo(Measure("1920 × 1200", NativeTheme.UiFont).Width, dimensions.Right - dimensions.Left);
            Assert.IsGreaterThanOrEqualTo(Measure("800%", NativeTheme.UiMediumFont).Width, zoom.Right - zoom.Left);
            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, dimensions.Bottom - dimensions.Top);
            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, zoom.Bottom - zoom.Top);
            int previousRight = 0;
            foreach (int id in new[] { 16, 33, 17, 18 })
            {
                var bounds = Bounds(scope.Child(id));
                Assert.IsGreaterThanOrEqualTo(previousRight + NativeTheme.Scale(4), bounds.Left);
                previousRight = bounds.Right;
            }
            var info = Bounds(scope.Child(32));
            Assert.IsGreaterThanOrEqualTo(previousRight + NativeTheme.Scale(4), info.Left);
            Assert.IsGreaterThanOrEqualTo(info.Left + NativeTheme.Scale(4), info.Right);
            foreach (int id in new[] { 16, 17, 18 })
            {
                var bounds = Bounds(scope.Child(id));
                Assert.AreEqual(NativeTheme.Scale(28), bounds.Bottom - bounds.Top);
                Assert.AreEqual(NativeTheme.Scale(28), bounds.Right - bounds.Left);
            }
            Assert.IsTrue(scope.View.ImageToolbarWithinClientBoundsForTest);
        }
        int before = scope.View.ImageZoomPercentageForTest;
        Assert.IsTrue(scope.Enter(17));
        Assert.IsGreaterThan(before, scope.View.ImageZoomPercentageForTest);
        Assert.AreEqual(scope.Child(17), NativeMethods.GetFocus());
        Assert.IsTrue(scope.View.HandleTabNavigation(false, 0));
        Assert.AreEqual(scope.Child(18), NativeMethods.GetFocus());
        Assert.IsTrue(scope.Enter(18));
        Assert.IsTrue(scope.View.ImageFitToAreaForTest);
        Assert.AreEqual(scope.Child(18), NativeMethods.GetFocus());
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 图片工具栏窄正文先收缩信息且所有动作不越界(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        using ViewScope scope = new(DocumentKind.Png);
        int width = NativeTheme.Scale(190);
        scope.View.SetBounds(0, 0, width, NativeTheme.Scale(240));

        Assert.IsTrue(scope.View.ImageToolbarWithinClientBoundsForTest,
            "窄正文时图片工具栏的尺寸信息必须先收缩，不能把控件推到客户区外。");
        foreach (int id in new[] { 16, 17, 18 })
        {
            NativeMethods.Rectangle bounds = Bounds(scope.Child(id));
            Assert.IsLessThanOrEqualTo(width, bounds.Right);
            Assert.AreEqual(NativeTheme.Scale(28), bounds.Right - bounds.Left);
        }

        NativeMethods.Rectangle zoom = Bounds(scope.Child(33));
        Assert.IsLessThanOrEqualTo(width, zoom.Right);
        Assert.IsGreaterThanOrEqualTo(0, zoom.Left);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 目标路径与JSON错误连续换行且正文和查找不重叠(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        using ViewScope scope = new(DocumentKind.Json, linked: true);
        nint editor = scope.Child(100);
        scope.View.ShowFindForTest("sdk");
        nint focus = NativeMethods.GetFocus();
        foreach (int size in new[] { 13, 40, 13 })
        {
            scope.Appearance(size);
            scope.View.SetBounds(0, 0, NativeTheme.Scale(640), NativeTheme.Scale(700));
            var toolbar = Bounds(scope.Child(30));
            var target = Bounds(scope.Child(34));
            var error = Bounds(scope.Child(19));
            var content = Bounds(editor);
            var find = Bounds(scope.Child(35));
            Assert.AreEqual(toolbar.Bottom, target.Top);
            Assert.AreEqual(target.Bottom, error.Top);
            Assert.AreEqual(find.Bottom, content.Top);
            Assert.AreEqual(error.Bottom, find.Top);
            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight + NativeTheme.Scale(16), target.Bottom - target.Top);
            int measured = MeasureWrapped(scope.Child(34), target.Right - target.Left - NativeTheme.Scale(16));
            Assert.AreEqual(measured + NativeTheme.Scale(16), target.Bottom - target.Top);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.AreEqual(editor, scope.Child(100));
            StringAssert.Contains(NativeMethods.GetWindowTextValue(scope.Child(34)), scope.Result.ResolvedPath);
        }
    }

    private static NativeMethods.Rectangle Bounds(nint window)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(window, out var bounds));
        return bounds;
    }

    private static (int Width, int Height) Measure(string text, nint font)
    {
        nint dc = NativeMethods.GetDeviceContext(0);
        nint previous = NativeMethods.SelectObject(dc, font);
        try
        {
            NativeMethods.Rectangle bounds = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return (bounds.Right, bounds.Bottom);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(0, dc); }
    }

    private static int MeasureWrapped(nint control, int width)
    {
        nint dc = NativeMethods.GetDeviceContext(control);
        nint previous = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        try
        {
            NativeMethods.Rectangle bounds = new() { Right = width };
            string text = NativeMethods.GetWindowTextValue(control);
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextWordBreak | NativeMethods.DrawTextNoPrefix);
            return bounds.Bottom;
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(control, dc); }
    }

    private sealed class ViewScope : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new();
        private readonly string _font = NativeTheme.UiFontFamilyForTest;
        private readonly double _size = NativeTheme.UiFontSizeForTest;
        private readonly nint _owner;
        private readonly string _theme;
        internal ViewScope(DocumentKind kind, bool linked = false, bool dark = false)
        {
            _theme = dark ? "Dark" : "Light";
            NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", 13);
            string path = _temporary.GetPath(kind == DocumentKind.Png ? "sample.png" : "sample.txt");
            if (kind == DocumentKind.Png)
                File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="));
            string content = kind == DocumentKind.Json ? linked ? "{\"sdk\":}" : "{\"sdk\":1}" : "readonly\ntext";
            string resolved = linked ? _temporary.GetPath("外部目标目录与较长文件名称/配置与示例文件/global.json") : path;
            Result = new(kind == DocumentKind.Png ? DocumentReadStatus.ImageReady : DocumentReadStatus.TextReady,
                path, resolved, new(kind, "测试文件"), 1, kind == DocumentKind.Png ? null : content, null, null, string.Empty);
            _owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, NativeTheme.Scale(900), NativeTheme.Scale(800),
                0, 0, NativeMethods.GetModuleHandle(null), 0);
            View = new(_owner, _temporary.FullPath, Result, new() { Theme = _theme }, _ => { }, (_, _, _) => Assert.Fail("不应打开其他文件。"));
            View.SetBounds(0, 0, NativeTheme.Scale(740), NativeTheme.Scale(700));
            if (kind == DocumentKind.Png) NativeImageTestPump.Wait(View);
        }
        internal NativeDocumentView View { get; }
        internal DocumentReadResult Result { get; }
        internal nint Child(int id) => GetDlgItem(View.Handle, id);
        internal bool Enter(int id)
        {
            _ = NativeMethods.SetFocus(Child(id));
            return View.HandleToolbarShortcut(new()
            {
                Window = Child(id),
                MessageId = NativeMethods.WindowMessageKeyDown,
                WordParameter = NativeMethods.VirtualKeyEnter,
            });
        }
        internal void Space(int id)
        {
            _ = NativeMethods.SetFocus(Child(id));
            _ = NativeMethods.SendMessage(Child(id), NativeMethods.WindowMessageKeyDown, 32, 0);
            _ = NativeMethods.SendMessage(Child(id), 0x0101, 32, 0);
        }
        internal void Appearance(int size)
        {
            NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
            View.ApplyAppearance(new ApplicationSettings() { TextFontSize = size, Theme = _theme });
        }
        public void Dispose()
        {
            View.Dispose();
            View.ImageWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            View.FindWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _ = NativeMethods.DestroyWindow(_owner);
            _temporary.Dispose();
            NativeTheme.ConfigureUiTypography(_font, _size);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetDlgItem(nint window, int id);
}
