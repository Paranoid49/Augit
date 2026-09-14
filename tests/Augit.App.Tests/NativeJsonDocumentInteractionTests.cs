using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Documents;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeJsonDocumentInteractionTests
{
    [TestMethod]
    public void 有效JSON默认格式化且双向切换不改磁盘()
    {
        using ViewScope scope = new("{\"后\":1,\"前\":2}");
        Assert.IsTrue(scope.View.IsShowingAlternative);
        Assert.AreEqual("{\n  \"后\": 1,\n  \"前\": 2\n}", ReadText(scope.Child(101)).ReplaceLineEndings("\n"));
        Assert.IsFalse(NativeMethods.IsWindowVisible(scope.Child(19)));
        scope.Click(1);
        Assert.IsFalse(scope.View.IsShowingAlternative);
        Assert.AreEqual(scope.Result.Text, ReadText(scope.Child(100)));
        scope.Click(2);
        Assert.IsTrue(scope.View.IsShowingAlternative);
        Assert.AreEqual(scope.Result.Text, File.ReadAllText(scope.Result.RequestedPath));
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    public void 格式错误显示可定位提示且格式化禁用()
    {
        using ViewScope scope = new("{\n  \"中文\":}\n");
        Assert.IsFalse(scope.View.IsShowingAlternative);
        Assert.IsTrue(NativeMethods.IsWindowVisible(scope.Child(19)), "错误必须在正文顶部可见。");
        StringAssert.Contains(NativeMethods.GetWindowTextValue(scope.Child(19)), "第 2 行，第 8 列");
        Assert.IsFalse(NativeMethods.IsWindowEnabled(scope.Child(2)));
        scope.Click(2);
        Assert.IsFalse(scope.View.IsShowingAlternative, "无效格式化不能用原文冒充格式化结果。");
        scope.Click(19);
        Assert.AreEqual(scope.Child(100), NativeMethods.GetFocus());
        Assert.AreEqual((nint)2, NativeMethods.SendMessage(scope.Child(100), 2008, 0, 0));
        Assert.AreEqual(scope.Result.Text, File.ReadAllText(scope.Result.RequestedPath));
    }

    [TestMethod]
    public void 外部修复隐藏错误条并保持正文控件及原文模式()
    {
        using ViewScope scope = new("{\n  \"中文\":}\n");
        nint original = scope.Child(100), formatted = scope.Child(101), error = scope.Child(19);
        scope.Click(19);
        scope.Reload("{\n  \"中文\": 1\n}");
        Assert.AreEqual(original, scope.Child(100));
        Assert.AreEqual(formatted, scope.Child(101));
        Assert.IsFalse(NativeMethods.IsWindowVisible(error));
        Assert.IsTrue(NativeMethods.IsWindowEnabled(scope.Child(2)));
        Assert.IsFalse(scope.View.IsShowingAlternative);
        Assert.AreEqual(original, NativeMethods.GetFocus());
        Assert.AreEqual("JSON", scope.View.ReadStatusText);
        scope.Click(2);
        Assert.IsTrue(scope.View.IsShowingAlternative);
    }

    [TestMethod]
    public void 外部变更更新错误行且查找条位于错误条和正文之间()
    {
        using ViewScope scope = new("{\"a\":}");
        scope.View.ShowFindForTest("中文");
        nint focus = NativeMethods.GetFocus();
        scope.Reload("{\n\n  \"中文\":}\n");
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        StringAssert.Contains(NativeMethods.GetWindowTextValue(scope.Child(19)), "第 3 行，第 8 列");
        Assert.IsTrue(NativeMethods.GetWindowRectangle(scope.Child(19), out var error));
        Assert.IsTrue(NativeMethods.GetWindowRectangle(scope.Child(35), out var find));
        Assert.IsTrue(NativeMethods.GetWindowRectangle(scope.Child(100), out var editor));
        Assert.AreEqual(error.Bottom, find.Top);
        Assert.AreEqual(find.Bottom, editor.Top);
        scope.Click(19);
        Assert.AreEqual((nint)3, NativeMethods.SendMessage(scope.Child(100), 2008, 0, 0));
        Assert.AreEqual("中文", NativeMethods.GetWindowTextValue(focus));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 错误入口可由Tab及按键到达且修复后不留下隐藏焦点(bool space)
    {
        using ViewScope scope = new("{\n  \"a\":}\n");
        _ = NativeMethods.SetFocus(scope.Child(15));
        Assert.IsTrue(scope.View.HandleTabNavigation(false, 0));
        Assert.AreEqual(scope.Child(19), NativeMethods.GetFocus());
        if (space)
        {
            _ = NativeMethods.SendMessage(scope.Child(19), NativeMethods.WindowMessageKeyDown, 32, 0);
            _ = NativeMethods.SendMessage(scope.Child(19), 0x0101, 32, 0);
        }
        else
        {
            Assert.IsTrue(scope.View.HandleJsonErrorShortcut(new()
            {
                Window = scope.Child(19),
                MessageId = NativeMethods.WindowMessageKeyDown,
                WordParameter = NativeMethods.VirtualKeyEnter,
            }));
        }
        Assert.AreEqual(scope.Child(100), NativeMethods.GetFocus());
        Assert.AreEqual((nint)2, NativeMethods.SendMessage(scope.Child(100), 2008, 0, 0));
        _ = NativeMethods.SetFocus(scope.Child(19));
        scope.Reload("{}");
        Assert.AreEqual(scope.Child(100), NativeMethods.GetFocus());
    }

    [TestMethod]
    public void 文件改为普通文本时移除错误入口且下一次JSON仍能格式化()
    {
        using ViewScope scope = new("{\"a\":}");
        nint error = scope.Child(19);
        scope.View.ReloadAsync(scope.Result with
        {
            Classification = new(DocumentKind.Text, "UTF-8 文本"),
            Text = "普通正文",
        }).GetAwaiter().GetResult();
        Assert.IsFalse(NativeMethods.IsWindow(error));
        Assert.AreEqual((nint)0, scope.Child(19));
        scope.Reload("{}");
        Assert.IsTrue(scope.View.IsShowingAlternative);
        Assert.IsTrue(NativeMethods.IsWindowEnabled(scope.Child(2)));
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 错误条随字号换行且不裁切或重建正文(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string family = NativeTheme.UiFontFamilyForTest;
        double size = NativeTheme.UiFontSizeForTest;
        try
        {
            using ViewScope scope = new("{\n  \"中文\":}\n");
            nint original = scope.Child(100);
            foreach (int fontSize in new[] { 13, 40 })
            {
                NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", fontSize);
                scope.View.ApplyAppearance(new() { TextFontSize = fontSize });
                scope.View.SetBounds(0, 0, NativeTheme.Scale(400), NativeTheme.Scale(500));
                _ = NativeMethods.GetWindowRectangle(scope.Child(19), out var error);
                _ = NativeMethods.GetWindowRectangle(original, out var editor);
                Assert.AreEqual(error.Bottom, editor.Top);
                Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight + NativeTheme.Scale(16), error.Bottom - error.Top);
                if (fontSize == 40) Assert.IsGreaterThan(NativeTheme.UiLineHeight + NativeTheme.Scale(16), error.Bottom - error.Top);
                Assert.AreEqual(original, scope.Child(100));
            }
        }
        finally { NativeTheme.ConfigureUiTypography(family, size); }
    }

    [TestMethod]
    public void 极窄正文中的JSON错误条仍保持有效矩形并推动正文()
    {
        using ViewScope scope = new("{\n  \"中文\":}\n");
        int width = NativeTheme.Scale(140);
        scope.View.SetBounds(0, 0, width, NativeTheme.Scale(320));

        Assert.IsTrue(NativeMethods.GetWindowRectangle(scope.Child(19), out var error));
        Assert.IsTrue(NativeMethods.GetWindowRectangle(scope.Child(100), out var editor));
        Assert.IsGreaterThanOrEqualTo(0, error.Right - error.Left);
        Assert.IsGreaterThanOrEqualTo(0, error.Bottom - error.Top);
        Assert.AreEqual(error.Bottom, editor.Top);
    }

    private static string ReadText(nint editor)
    {
        int length = (int)NativeMethods.SendMessage(editor, 2183, 0, 0);
        nint buffer = Marshal.AllocCoTaskMem(length + 1);
        try
        {
            _ = NativeMethods.SendMessage(editor, 2182, (nuint)(length + 1), buffer);
            return Marshal.PtrToStringUTF8(buffer, length) ?? string.Empty;
        }
        finally { Marshal.FreeCoTaskMem(buffer); }
    }

    private sealed class ViewScope : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new();
        private readonly nint _owner;
        internal ViewScope(string content)
        {
            string path = _temporary.GetPath("sample.json");
            File.WriteAllText(path, content, new UTF8Encoding(false));
            Result = new(DocumentReadStatus.TextReady, path, path, new(DocumentKind.Json, "JSON"),
                Encoding.UTF8.GetByteCount(content), content, null, null, string.Empty);
            _owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, 760, 520,
                0, 0, NativeMethods.GetModuleHandle(null), 0);
            View = new(_owner, _temporary.FullPath, Result, new(), _ => { }, (_, _, _) => Assert.Fail("不能打开其他文件。"));
            View.SetBounds(0, 0, 740, 500);
        }
        internal DocumentReadResult Result { get; }
        internal NativeDocumentView View { get; }
        internal nint Child(int id)
        {
            for (nint child = NativeMethods.GetWindowSibling(View.Handle, 5); child != 0; child = NativeMethods.GetWindowSibling(child, 2))
                if (NativeMethods.GetWindowLongPointer(child, -12) == id) return child;
            return 0;
        }
        internal void Click(int id)
        {
            Assert.AreNotEqual((nint)0, Child(id));
            _ = NativeMethods.SendMessage(Child(id), 0x00F5, 0, 0);
            NativeFindTestPump.Wait(View);
        }
        internal void Reload(string text) => View.ReloadAsync(Result with { Text = text }).GetAwaiter().GetResult();
        public void Dispose()
        {
            View.Dispose();
            View.FindWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _ = NativeMethods.DestroyWindow(_owner);
            _temporary.Dispose();
        }
    }
}
