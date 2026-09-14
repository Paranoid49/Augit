using System.Text;
using Augit.Core.Documents;
using Augit.Infrastructure.Files;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class ReadOnlyDocumentServiceTests
{
    [TestMethod]
    public async Task 读取带Bom的Utf8且不修改文件()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("说明.md");
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# 标题")];
        await File.WriteAllBytesAsync(path, original);

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);

        Assert.AreEqual(DocumentReadStatus.TextReady, result.Status);
        Assert.AreEqual(DocumentKind.Markdown, result.Classification.Kind);
        Assert.AreEqual("# 标题", result.Text);
        Assert.AreEqual(DocumentLineEndings.None, result.LineEndings);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    public async Task 非法Utf8显示稳定错误且不返回正文()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("invalid.txt");
        await File.WriteAllBytesAsync(path, [0xC3, 0x28]);

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);

        Assert.AreEqual(DocumentReadStatus.InvalidUtf8, result.Status);
        Assert.AreEqual("文件不是有效的 UTF-8。", result.Message);
        Assert.IsNull(result.Text);
        Assert.AreEqual(DocumentLineEndings.Unknown, result.LineEndings);
    }

    [TestMethod]
    public async Task Gif只返回二进制摘要()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("animation.gif");
        await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("GIF89a"));

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);

        Assert.AreEqual(DocumentReadStatus.BinarySummary, result.Status);
        Assert.AreEqual(DocumentKind.Gif, result.Classification.Kind);
        Assert.AreEqual(DocumentLineEndings.Unknown, result.LineEndings);
    }

    [TestMethod]
    [DataRow("\r\n", DocumentLineEndings.CrLf)]
    [DataRow("\r", DocumentLineEndings.Cr)]
    [DataRow("\n", DocumentLineEndings.Lf)]
    [DataRow("\r\n\n", DocumentLineEndings.Mixed)]
    public async Task 换行格式由完整读取结果携带且识别首部之外的换行(string ending, DocumentLineEndings expected)
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("format.json");
        string content = new string(' ', 65536) + ending + "{\"示例\":1}";
        await File.WriteAllTextAsync(path, content);
        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);
        Assert.AreEqual(DocumentReadStatus.TextReady, result.Status);
        Assert.AreEqual(expected, result.LineEndings);
        Assert.AreEqual(content, result.Text);
        Assert.AreEqual(content, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task 读取受限Png尺寸但不读取为文本()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("image.png");
        byte[] content =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x03,
        ];
        await File.WriteAllBytesAsync(path, content);

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);

        Assert.AreEqual(DocumentReadStatus.ImageReady, result.Status);
        Assert.AreEqual(2, result.PixelWidth);
        Assert.AreEqual(3, result.PixelHeight);
        Assert.IsNull(result.Text);
    }

    [TestMethod]
    public async Task 超过十兆的有效文本不读取正文()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("large.txt");
        byte[] block = Enumerable.Repeat((byte)'a', 1024 * 1024).ToArray();
        await using (FileStream stream = File.Create(path))
        {
            for (int index = 0; index < 10; index++)
            {
                await stream.WriteAsync(block);
            }

            stream.WriteByte((byte)'a');
        }

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);

        Assert.AreEqual(DocumentReadStatus.TextTooLarge, result.Status);
        Assert.IsNull(result.Text);
    }

    [TestMethod]
    public async Task 超过二千五百万像素的图片不进入预览()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("large.png");
        byte[] content =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x13, 0x89, 0x00, 0x00, 0x13, 0x88,
        ];
        await File.WriteAllBytesAsync(path, content);

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);

        Assert.AreEqual(DocumentReadStatus.ImageTooLarge, result.Status);
        Assert.AreEqual(5001, result.PixelWidth);
        Assert.AreEqual(5000, result.PixelHeight);
    }

    [TestMethod]
    public async Task 超过二十五兆的图片在解码前停止()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("huge.png");
        byte[] header =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        ];
        await using (FileStream stream = File.Create(path))
        {
            await stream.WriteAsync(header);
            stream.SetLength(DocumentLimits.MaximumImageBytes + 1);
        }

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);

        Assert.AreEqual(DocumentReadStatus.ImageTooLarge, result.Status);
        Assert.IsNull(result.PixelWidth);
    }

    [TestMethod]
    public async Task 拒绝直接请求工作区外文件()
    {
        using TemporaryDirectory workspace = new();
        using TemporaryDirectory outside = new();
        string path = outside.GetPath("outside.txt");
        await File.WriteAllTextAsync(path, "外部");

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(workspace.FullPath, path);

        Assert.AreEqual(DocumentReadStatus.UnsafeTarget, result.Status);
        Assert.IsNull(result.Text);
    }
}
