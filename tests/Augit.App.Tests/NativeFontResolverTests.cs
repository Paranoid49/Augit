using System.Runtime.InteropServices;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeFontResolverTests
{
    private static readonly string[] MonospaceFallbacks = ["Cascadia Mono", "Consolas", "Courier New"];

    [TestMethod]
    public void 缺失字体不会伪装成已安装字体且正文明确回退到等宽字体()
    {
        const string missing = "Augit Missing Font 7938";
        Assert.IsFalse(NativeFontResolver.IsInstalled(missing));
        Assert.AreEqual("Consolas", NativeFontResolver.ResolveMonospace("Consolas"));
        string resolved = NativeFontResolver.ResolveMonospace(missing);
        Assert.IsTrue(NativeFontResolver.IsInstalled(resolved));
        CollectionAssert.Contains(MonospaceFallbacks, resolved);
        Assert.AreEqual("Microsoft YaHei UI", NativeFontResolver.ResolveInterface(missing));
        Assert.AreEqual("Segoe UI", NativeFontResolver.ResolveInterface(" Segoe UI "));
    }

    [TestMethod]
    public void Scintilla实际使用回退字体且拉丁字符保持等宽()
    {
        nint owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStylePopup, 0, 0, 400, 300, 0, 0, NativeMethods.GetModuleHandle(null), 0);
        try
        {
            using ScintillaControl editor = new(owner, 1);
            editor.ApplyAppearance("Augit Missing Font 7938", 13, false);
            nint fontBuffer = Marshal.AllocCoTaskMem(128);
            try
            {
                _ = NativeMethods.SendMessage(editor.Handle, 2486, 32, fontBuffer);
                Assert.AreEqual(NativeFontResolver.ResolveMonospace("Augit Missing Font 7938"), Marshal.PtrToStringUTF8(fontBuffer));
                _ = NativeMethods.SendMessage(editor.Handle, 2761, 0, fontBuffer);
                Assert.AreEqual("zh-CN", Marshal.PtrToStringUTF8(fontBuffer));
            }
            finally { Marshal.FreeCoTaskMem(fontBuffer); }
            Assert.AreEqual(Measure(editor.Handle, "iiiiiiii"), Measure(editor.Handle, "WWWWWWWW"));
            editor.SetTextContent("中文正文 ABC 123");
            Assert.IsTrue(editor.IsReadOnly);
            Assert.AreEqual("中文正文 ABC 123", editor.GetTextContent());
        }
        finally { _ = NativeMethods.DestroyWindow(owner); }
    }

    private static nint Measure(nint editor, string text)
    {
        nint buffer = Marshal.StringToCoTaskMemUTF8(text);
        try { return NativeMethods.SendMessage(editor, 2276, 32, buffer); }
        finally { Marshal.FreeCoTaskMem(buffer); }
    }
}
