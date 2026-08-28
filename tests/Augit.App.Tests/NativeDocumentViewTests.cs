namespace Augit.App.Tests;

[TestClass]
public sealed class NativeDocumentViewTests
{
    [TestMethod]
    public void WebView2缺失提示使用微软Https下载入口()
    {
        Uri uri = RuntimeDependencyPrompt.WebView2DownloadUri;

        Assert.AreEqual(Uri.UriSchemeHttps, uri.Scheme);
        Assert.AreEqual("developer.microsoft.com", uri.Host);
    }

    [TestMethod]
    public void 当前文件查找支持全字匹配和循环定位()
    {
        (int Start, int Length)? first = NativeDocumentView.FindMatch(
            "cat scatter cat",
            "cat",
            0,
            backwards: false,
            matchCase: true,
            wholeWord: true,
            regularExpression: false);
        (int Start, int Length)? wrapped = NativeDocumentView.FindMatch(
            "cat scatter cat",
            "cat",
            15,
            backwards: false,
            matchCase: true,
            wholeWord: true,
            regularExpression: false);

        Assert.AreEqual((0, 3), first);
        Assert.AreEqual((0, 3), wrapped);
    }

    [TestMethod]
    public void 当前文件查找支持反向正则表达式()
    {
        (int Start, int Length)? match = NativeDocumentView.FindMatch(
            "a1 a2 a3",
            "a\\d",
            8,
            backwards: true,
            matchCase: true,
            wholeWord: false,
            regularExpression: true);

        Assert.AreEqual((6, 2), match);
    }

    [TestMethod]
    public void Markdown预览只允许初始页和About内部导航()
    {
        Assert.IsTrue(MarkdownWebViewHost.ShouldAllowNavigation("about:blank", initialNavigation: true));
        Assert.IsTrue(MarkdownWebViewHost.ShouldAllowNavigation("about:blank#标题", initialNavigation: false));
        Assert.IsFalse(MarkdownWebViewHost.ShouldAllowNavigation("https://example.com", initialNavigation: false));
        Assert.IsFalse(MarkdownWebViewHost.ShouldAllowNavigation("file:///C:/outside.txt", initialNavigation: false));
    }
}
