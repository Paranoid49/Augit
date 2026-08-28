using System.IO;
using System.Text.RegularExpressions;

namespace Augit.App.Tests;

[TestClass]
public sealed class MarkdownPreviewRendererTests
{
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

        StringAssert.Contains(html, "href=\"#\"");
        StringAssert.Contains(html, "已阻止图片");
        Assert.IsFalse(html.Contains("data:image/gif", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task 用户字体设置经过转义后用于预览样式()
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

        StringAssert.Contains(html, "16px/1.65 '正文\\'字体'");
        StringAssert.Contains(html, "'等宽\\\\字体'");
    }
}
