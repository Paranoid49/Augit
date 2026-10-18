namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellFailureTextTests
{
    [TestMethod]
    public void 本地化异常不会直接显示到宿主错误窗口()
    {
        InvalidOperationException exception = new("中文异常和内部路径");
        foreach (bool interfaceOnly in new[] { false, true })
        {
            string text = ShellFailureText.Startup(exception, interfaceOnly);
            StringAssert.Contains(text, nameof(InvalidOperationException));
            StringAssert.Contains(text, "WebView2");
            StringAssert.Contains(text, $"0x{exception.HResult:X8}");
            Assert.IsFalse(text.Contains(exception.Message, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void 导航错误保留原始目录而呈现英文说明()
    {
        const string directory = @"D:\中文目录\web";
        string text = ShellFailureText.Navigation("FileNotFound", directory);
        StringAssert.Contains(text, directory);
        StringAssert.Contains(text, "FileNotFound");
        StringAssert.StartsWith(text, "The interface resources");
        StringAssert.Contains(ShellFailureText.Renderer("RenderProcessExited", "Crashed"), "Close and reopen");
    }
}
