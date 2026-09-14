using System.IO;
using System.Text.RegularExpressions;

namespace Augit.App.Tests;

[TestClass]
public sealed class MarkdownPreviewRendererTests
{
    [TestMethod]
    public async Task Markdown正文按Idea预览从左侧展开并收紧首标题顶部留白()
    {
        string html = await MarkdownPreviewRenderer.RenderAsync(
            "# 标题",
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), "readme.md"),
            darkTheme: false);

        Assert.Contains("body { margin: 0; padding: 24px 36px 40px; background:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("max-width: 980px", html, StringComparison.Ordinal);
        Assert.Contains("font: 13px/1.62", html, StringComparison.Ordinal);
        Assert.Contains("h1 { margin: 0 0 28px; font-size: 29px; font-weight: 600; }", html, StringComparison.Ordinal);
        Assert.Contains("h2 { margin: 26px 0 13px; font-size: 21px; font-weight: 600; }", html, StringComparison.Ordinal);
        Assert.Contains("::-webkit-scrollbar { width: 10px; height: 10px; }", html, StringComparison.Ordinal);
        Assert.DoesNotContain("margin: 0 auto", html, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task 原生Html按普通文本显示()
    {
        using TemporaryDirectory temporary = new();
        string documentPath = temporary.GetPath("README.md");
        File.WriteAllText(documentPath, string.Empty);

        string html = await MarkdownPreviewRenderer.RenderAsync(
            "<script>alert('x')</script>",
            temporary.FullPath,
            documentPath,
            false);

        StringAssert.Contains(html, "&lt;script&gt;");
        Assert.IsFalse(html.Contains("<script>", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task 预览正文和代码块字号可以独立设置()
    {
        string html = await MarkdownPreviewRenderer.RenderAsync(
            "正文\n\n```\n代码\n```",
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), "readme.md"),
            false,
            "Segoe UI",
            "Consolas",
            13,
            monospaceFontSize: 19);

        Assert.Contains("font: 13px/1.62 'Segoe UI'", html, StringComparison.Ordinal);
        Assert.Contains("font-family: 'Consolas', Consolas, monospace; font-size: 19px", html, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task 工作区相对链接改写为只读导航地址()
    {
        using TemporaryDirectory temporary = new();
        string documentPath = temporary.GetPath("README.md");
        string targetPath = temporary.GetPath("guide.md");
        File.WriteAllText(documentPath, string.Empty);
        File.WriteAllText(targetPath, "# 指南");

        string html = await MarkdownPreviewRenderer.RenderAsync(
            "[指南](guide.md#章节)",
            temporary.FullPath,
            documentPath,
            false);
        Match match = Regex.Match(html, "href=\"(?<uri>https://augit\\.local/[^\"]+)\"");
        bool decoded = Uri.TryCreate(match.Groups["uri"].Value, UriKind.Absolute, out Uri? uri)
            && MarkdownPreviewRenderer.TryDecodeAugitLink(uri, out string? decodedPath, out string? anchor)
            && decodedPath == targetPath
            && anchor == "章节";

        Assert.IsTrue(decoded);
    }

    [TestMethod]
    public async Task 越界链接和Gif图片均被阻止()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string documentPath = Path.Combine(workspace, "README.md");
        File.WriteAllText(documentPath, string.Empty);
        File.WriteAllText(temporary.GetPath("outside.txt"), "外部");
        File.WriteAllBytes(Path.Combine(workspace, "animation.gif"), "GIF89a"u8.ToArray());

        string html = await MarkdownPreviewRenderer.RenderAsync(
            "[外部](../outside.txt)\n\n![动画](animation.gif)",
            workspace,
            documentPath,
            false);

        StringAssert.Contains(html, "外部（链接已阻止");
        Assert.DoesNotContain("href=\"#\"", html, StringComparison.Ordinal);
        StringAssert.Contains(html, "已阻止图片");
        Assert.IsFalse(html.Contains("data:image/gif", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task 无效字体回退到已安装字体且不能注入预览样式()
    {
        using TemporaryDirectory temporary = new();
        string documentPath = temporary.GetPath("README.md");
        File.WriteAllText(documentPath, string.Empty);

        string html = await MarkdownPreviewRenderer.RenderAsync(
            "正文",
            temporary.FullPath,
            documentPath,
            false,
            "正文'字体",
            "等宽\\字体",
            16);

        StringAssert.Contains(html, "16px/1.62 'Microsoft YaHei UI'");
        StringAssert.Contains(html, $"'{NativeFontResolver.ResolveMonospace("等宽\\字体")}'");
        Assert.DoesNotContain("正文'字体", html, StringComparison.Ordinal);
        Assert.DoesNotContain("等宽\\字体", html, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task 代码围栏与行内代码的Html只转义一次()
    {
        using TemporaryDirectory temporary = new();
        string html = await MarkdownPreviewRenderer.RenderAsync(
            "```html\n<div>示例</div>\n```\n\n`<em>文字</em>`\n\n<!-- 注释 -->",
            temporary.FullPath, temporary.GetPath("README.md"), false);
        StringAssert.Contains(html, "&lt;div&gt;示例&lt;/div&gt;");
        StringAssert.Contains(html, "<code>&lt;em&gt;文字&lt;/em&gt;</code>");
        Assert.DoesNotContain("&amp;lt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<!-- 注释 -->", html, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task 转义井号的真实文件名和中文锚点分别解码()
    {
        using TemporaryDirectory temporary = new();
        string target = temporary.GetPath("guide#part.md");
        File.WriteAllText(target, "# 章节");
        string html = await MarkdownPreviewRenderer.RenderAsync("[**指南**](guide%23part.md#%E7%AB%A0%E8%8A%82)",
            temporary.FullPath, temporary.GetPath("README.md"), false);
        Match link = Regex.Match(html, "href=\"(?<uri>https://augit\\.local/[^\"]+)\"");
        Assert.IsTrue(MarkdownPreviewRenderer.TryDecodeAugitLink(new Uri(link.Groups["uri"].Value), out string? path, out string? anchor));
        Assert.AreEqual(target, path);
        Assert.AreEqual("章节", anchor);
        StringAssert.Contains(html, "<strong>指南</strong>");
    }

    [TestMethod]
    [DataRow("missing.md")]
    [DataRow("unknown:blocked")]
    [DataRow("file:///C:/outside.txt")]
    [DataRow("bad%00path.md")]
    public async Task 无效链接保留标签并局部说明原因(string source)
    {
        using TemporaryDirectory temporary = new();
        string html = await MarkdownPreviewRenderer.RenderAsync($"[**不可用**]({source})\n\n后文继续显示",
            temporary.FullPath, temporary.GetPath("README.md"), false);
        StringAssert.Contains(html, "<strong>不可用</strong>（链接已阻止");
        StringAssert.Contains(html, "后文继续显示");
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task 合法锚点和外部协议保持且图片错误不影响后文()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllBytes(temporary.GetPath("broken.png"), "无效图片"u8.ToArray());
        string html = await MarkdownPreviewRenderer.RenderAsync(
            "[页内](#章节) [官网](https://example.com) [邮件](mailto:test@example.com)\n\n![损坏](broken.png)\n\n后文",
            temporary.FullPath, temporary.GetPath("README.md"), false);
        StringAssert.Contains(html, "href=\"#");
        StringAssert.Contains(html, "href=\"https://example.com\"");
        StringAssert.Contains(html, "href=\"mailto:test@example.com\"");
        StringAssert.Contains(html, "已阻止图片");
        StringAssert.Contains(html, "后文");
    }

    [TestMethod]
    public async Task 合法本地和远程图片带独立反馈容器且不阻塞正文()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("README.md");
        File.WriteAllBytes(temporary.GetPath("ok.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAMAAAABCAYAAAAb4BS0AAAAEklEQVR4nGP4z8DAAMQN/4EUABt0BH3r3fEnAAAAAElFTkSuQmCC"));

        string html = await MarkdownPreviewRenderer.RenderAsync(
            "正文先显示\n\n![本地](ok.png)\n\n![远程](https://example.invalid/image.png)\n\n后文继续显示",
            temporary.FullPath, path, false);

        Assert.AreEqual(2, Regex.Count(html, "class=\"markdown-image\""));
        Assert.AreEqual(2, Regex.Count(html, "class=\"markdown-image-feedback\""));
        StringAssert.Contains(html, "data:image/png;base64,");
        StringAssert.Contains(html, "https://example.invalid/image.png");
        StringAssert.Contains(html, "正文先显示");
        StringAssert.Contains(html, "后文继续显示");
        StringAssert.Contains(html, ".markdown-image[data-state='ready'] .markdown-image-feedback");
        StringAssert.Contains(html, ".markdown-image:not([data-state='ready']) img");
    }

    [TestMethod]
    public async Task 被独占的本地图片只产生局部提示()
    {
        using TemporaryDirectory temporary = new();
        string picture = temporary.GetPath("locked.png");
        using FileStream held = new(picture, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        string html = await MarkdownPreviewRenderer.RenderAsync("![被占用](locked.png)\n\n后文",
            temporary.FullPath, temporary.GetPath("README.md"), false);
        StringAssert.Contains(html, "已阻止图片");
        StringAssert.Contains(html, "后文");
    }

    [TestMethod]
    public async Task 已取消的渲染直接结束()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => MarkdownPreviewRenderer.RenderAsync("# 不渲染",
            Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "README.md"), false, cancellation.Token));
    }

    [TestMethod]
    public async Task 中文标题生成可供链接定位的唯一标识()
    {
        using TemporaryDirectory temporary = new();
        string html = await MarkdownPreviewRenderer.RenderAsync("# 章节\n\n## 章节\n\n## 1. 产品定位",
            temporary.FullPath, temporary.GetPath("README.md"), false);
        StringAssert.Contains(html, "id=\"章节\"");
        StringAssert.Contains(html, "id=\"章节-1\"");
        StringAssert.Contains(html, "id=\"1-产品定位\"");
    }
}
