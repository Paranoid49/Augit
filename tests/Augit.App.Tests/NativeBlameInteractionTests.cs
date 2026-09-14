using System.Globalization;
using System.Text;
using Augit.Core.Documents;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeBlameInteractionTests
{
    [TestMethod]
    [DataRow(DocumentKind.Text)]
    [DataRow(DocumentKind.Json)]
    [DataRow(DocumentKind.Markdown)]
    public void 查找输入的焦点和文字变化不能误触关闭归属(DocumentKind kind)
    {
        using ViewScope scope = new(kind);
        scope.Show();
        scope.View.ShowFindForTest("内容");
        nint find = scope.Child(20);
        Assert.AreEqual(find, NativeMethods.GetFocus());
        Assert.IsTrue(scope.View.IsShowingBlame, "查找输入获得焦点时不能执行关闭 Blame。");
        _ = NativeMethods.SetWindowText(find, "第2行");
        NativeFindTestPump.Wait(scope.View);
        Assert.IsTrue(scope.View.BlameToolbarVisibleForTest);
        Assert.AreNotEqual(find, scope.View.BlameCloseButtonForTest, "控件标识不可重用。");
        scope.View.HideFind();
        Assert.IsTrue(scope.View.IsShowingBlame);
        Assert.AreEqual(scope.Editor, NativeMethods.GetFocus());
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    [DataRow(DocumentKind.Text, false)]
    [DataRow(DocumentKind.Text, true)]
    [DataRow(DocumentKind.Json, false)]
    [DataRow(DocumentKind.Json, true)]
    [DataRow(DocumentKind.Markdown, false)]
    [DataRow(DocumentKind.Markdown, true)]
    public void 关闭归属的键盘路径保留正文阅读位置并恢复对应工具栏(DocumentKind kind, bool space)
    {
        using ViewScope scope = new(kind);
        scope.Show();
        Assert.IsFalse(scope.View.ModeSegmentPaintedByParentForTest);
        Assert.IsFalse(NativeMethods.IsWindowVisible(scope.Child(30)));
        _ = NativeMethods.SendMessage(scope.Editor, 2160, 145, 109);
        _ = NativeMethods.SendMessage(scope.Editor, 2613, 32, 0);
        var before = scope.Capture();
        _ = NativeMethods.SetFocus(scope.Editor);
        Assert.IsTrue(scope.View.HandleTabNavigation(true, scope.Owner));
        nint close = scope.View.BlameCloseButtonForTest;
        Assert.AreEqual(close, NativeMethods.GetFocus(), "反向 Tab 应到关闭按钮，不能跳过 Blame 工具栏。");
        if (space)
        {
            _ = NativeMethods.SendMessage(close, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeySpace, 0);
            _ = NativeMethods.SendMessage(close, 0x0101, NativeMethods.VirtualKeySpace, 0);
        }
        else
        {
            Assert.IsTrue(scope.View.HandleToolbarShortcut(new()
            {
                Window = close,
                MessageId = NativeMethods.WindowMessageKeyDown,
                WordParameter = NativeMethods.VirtualKeyEnter,
            }));
        }
        Assert.IsFalse(scope.View.IsShowingBlame);
        Assert.IsFalse(scope.View.BlameToolbarVisibleForTest);
        Assert.AreEqual(scope.Editor, NativeMethods.GetFocus());
        Assert.AreEqual(before, scope.Capture());
        Assert.AreEqual(scope.Result.Text, scope.View.CurrentText);
        Assert.IsTrue(scope.View.IsTextReadOnly);
        Assert.IsTrue(NativeMethods.IsWindowVisible(scope.Child(30)));
        Assert.AreEqual(kind != DocumentKind.Text, scope.View.ModeSegmentPaintedByParentForTest);
        Assert.IsTrue(kind == DocumentKind.Text
            ? scope.View.TextToolbarWithinClientBoundsForTest : scope.View.ModeToolbarWithinClientBoundsForTest);
        Assert.IsFalse(scope.View.IsShowingAlternative, "关闭归属保持当前正在阅读的原文。");
        scope.Show();
        Assert.IsTrue(scope.View.IsShowingBlame, "关闭后允许再次打开归属。");
    }

    [TestMethod]
    [DataRow(DocumentKind.Text, false)]
    [DataRow(DocumentKind.Json, false)]
    [DataRow(DocumentKind.Json, true)]
    [DataRow(DocumentKind.Markdown, false)]
    public void 外部更新退出归属但不抢查找焦点或残留点击映射(DocumentKind kind, bool invalid)
    {
        using ViewScope scope = new(kind);
        scope.Show();
        scope.View.ShowFindForTest("内容");
        nint find = NativeMethods.GetFocus();
        DocumentReadResult next = scope.Result with
        {
            Text = invalid ? "{\"未完成\":" : scope.Result.Text!.Replace("第1行", "已更新", StringComparison.Ordinal),
        };
        scope.View.ReloadAsync(next).GetAwaiter().GetResult();
        NativeFindTestPump.Wait(scope.View);
        Assert.IsFalse(scope.View.IsShowingBlame);
        Assert.IsFalse(scope.View.BlameToolbarVisibleForTest);
        Assert.AreEqual(find, NativeMethods.GetFocus());
        Assert.AreEqual("内容", NativeMethods.GetWindowTextValue(find));
        Assert.IsTrue(scope.View.IsTextReadOnly);
        Assert.AreEqual(next.Text, scope.View.CurrentText);
        if (kind == DocumentKind.Json)
        {
            Assert.IsTrue(NativeMethods.IsWindowVisible(scope.Child(2)), "格式化按钮恢复显示，无效时保持禁用。");
            Assert.AreEqual(!invalid, NativeMethods.IsWindowEnabled(scope.Child(2)));
        }
        scope.ClickMargin(0);
        Assert.IsEmpty(scope.Opened, "归属清除后的行号点击不得复用旧提交映射。");
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 真实边栏点击以UTF8位置定位对应提交且不混淆正文和行号(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        using ViewScope scope = new(DocumentKind.Text);
        scope.Show();
        foreach (int index in new[] { 0, 2, 7, 61 })
        {
            _ = NativeMethods.SendMessage(scope.Editor, 2613, (nuint)Math.Max(0, index - 2), 0);
            scope.ClickMargin(index);
            Assert.HasCount(1, scope.Opened);
            Assert.AreEqual(scope.Lines[index].CommitHash, scope.Opened[0], $"点击第 {index + 1} 行必须定位该行提交。");
            Assert.AreEqual(index, scope.SelectedBlameLineForTest);
            scope.Opened.Clear();
        }
        int blameWidth = (int)NativeMethods.SendMessage(scope.Editor, 2243, 0, 0);
        scope.ClickMargin(61, blameWidth + 3);
        Assert.IsEmpty(scope.Opened, "普通行号栏不能触发归属动作。");
        int numberWidth = (int)NativeMethods.SendMessage(scope.Editor, 2243, 1, 0);
        scope.ClickMargin(61, blameWidth + numberWidth + 6);
        Assert.IsEmpty(scope.Opened, "正文点击不能触发归属动作。");
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Light")]
    public void 归属工具栏随字体布局且数量紧邻关闭入口(int dpi, string theme)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        try
        {
            using ViewScope scope = new(DocumentKind.Text);
            scope.Show();
            foreach (int size in new[] { 13, 40, 19 })
            {
                NativeTheme.ConfigureUiTypography(previousFamily, size);
                scope.View.ApplyAppearance(new() { Theme = theme, TextFontSize = size });
                scope.View.SetBounds(0, 0, NativeTheme.Scale(660), NativeTheme.Scale(480));
                Assert.IsTrue(scope.View.BlameToolbarWithinClientBoundsForTest);
                Assert.IsTrue(scope.View.DocumentToolTipsCreatedForTest);
                Assert.AreEqual("100 行归属", scope.View.BlameCountTextForTest);
                Assert.IsTrue(NativeMethods.GetWindowRectangle(scope.Child(37), out var count));
                Assert.IsTrue(NativeMethods.GetWindowRectangle(scope.View.BlameCloseButtonForTest, out var close));
                Assert.AreEqual(NativeTheme.Scale(4), close.Left - count.Right);
                Assert.AreEqual(NativeTheme.Scale(28), close.Right - close.Left);
                Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, count.Bottom - count.Top);
            }
        }
        finally { NativeTheme.ConfigureUiTypography(previousFamily, previousSize); }
    }

    private sealed class ViewScope : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new();
        internal ViewScope(DocumentKind kind)
        {
            string content = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"内容😀 第{i}行"));
            if (kind == DocumentKind.Json) content = "[\n" + string.Join(",\n", content.Split('\n').Select(s => $"\"{s}\"")) + "\n]";
            string path = _temporary.GetPath("sample.txt");
            Result = new(DocumentReadStatus.TextReady, path, path, new(kind, kind.ToString()),
                Encoding.UTF8.GetByteCount(content), content, null, null, string.Empty);
            Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0,
                NativeTheme.Scale(760), NativeTheme.Scale(520), 0, 0, NativeMethods.GetModuleHandle(null), 0);
            View = new(Owner, _temporary.FullPath, Result, new(), _ => { }, (_, _, _) => { });
            View.SetBounds(0, 0, NativeTheme.Scale(740), NativeTheme.Scale(500));
            Lines = Enumerable.Range(1, 100).Select(i => new GitBlameLine(i, i.ToString("x40", CultureInfo.InvariantCulture),
                "测试作者", "test@example.invalid", DateTimeOffset.UnixEpoch, "测试提交", "sample.txt", "内容")).ToArray();
        }
        internal nint Owner { get; }
        internal NativeDocumentView View { get; }
        internal DocumentReadResult Result { get; }
        internal GitBlameLine[] Lines { get; }
        internal List<string> Opened { get; } = [];
        internal int SelectedBlameLineForTest => View.SelectedBlameLineForTest;
        internal nint Editor => Child(100);
        internal void Show() => Assert.IsTrue(View.ShowBlame(Lines, Opened.Add));
        internal nint Child(int identifier)
        {
            for (nint child = NativeMethods.GetWindowSibling(View.Handle, 5); child != 0; child = NativeMethods.GetWindowSibling(child, 2))
                if (NativeMethods.GetWindowLongPointer(child, -12) == identifier) return child;
            Assert.Fail($"缺少控件 {identifier}。");
            return 0;
        }
        internal (nint Anchor, nint Caret, nint FirstLine) Capture() =>
            (NativeMethods.SendMessage(Editor, 2009, 0, 0), NativeMethods.SendMessage(Editor, 2008, 0, 0),
                NativeMethods.SendMessage(Editor, 2152, 0, 0));
        internal void ClickMargin(int line, int x = 4)
        {
            nint position = NativeMethods.SendMessage(Editor, 2167, (nuint)line, 0);
            int y = (int)NativeMethods.SendMessage(Editor, 2165, 0, position)
                + (int)NativeMethods.SendMessage(Editor, 2279, 0, 0) / 2;
            nint point = (nint)((y << 16) | x);
            _ = NativeMethods.SendMessage(Editor, NativeMethods.WindowMessageLeftButtonDown, 1, point);
            _ = NativeMethods.SendMessage(Editor, NativeMethods.WindowMessageLeftButtonUp, 0, point);
        }
        public void Dispose()
        {
            View.Dispose();
            View.FindWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _ = NativeMethods.DestroyWindow(Owner);
            _temporary.Dispose();
        }
    }
}
