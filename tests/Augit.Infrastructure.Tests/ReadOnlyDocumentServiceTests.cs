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
    public async Task 超过内容加载上限的文本给前二千五百KB的只读预览()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("large.txt");
        byte[] block = Enumerable.Repeat((byte)'a', 1024 * 1024).ToArray();
        await using (FileStream stream = File.Create(path))
        {
            // 内容加载上限 20 MB（权威 `idea.max.content.load.filesize`）之上再多 1 字节。
            for (int index = 0; index < 20; index++)
            {
                await stream.WriteAsync(block);
            }

            await stream.WriteAsync("TAIL"u8.ToArray());
        }

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);

        // 权威 `LargeFileEditorProvider` ＋ `LargeFileNotificationProvider`：超限仍给**只读预览**，
        // 预览长度是 `getPreviewLimit(extension)`（默认 2500 KB），不是整页拒绝。
        Assert.AreEqual(DocumentReadStatus.TextPreview, result.Status);
        Assert.AreEqual(DocumentLimits.DefaultPreviewBytes, result.PreviewBytes);
        Assert.IsNotNull(result.Text);
        Assert.AreEqual(DocumentLimits.DefaultPreviewBytes, (long)Encoding.UTF8.GetByteCount(result.Text!));
        Assert.DoesNotContain("TAIL", result.Text!, StringComparison.Ordinal);
        StringAssert.Contains(result.Message, "只读预览");
        Assert.AreEqual(DocumentLimits.DefaultContentLoadBytes + 4, result.FileSize);
    }

    [TestMethod]
    public async Task 预览截断落在完整字符边界上()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("boundary.txt");
        const int filler = (int)DocumentLimits.DefaultPreviewBytes - 1;
        byte[] head = Enumerable.Repeat((byte)'a', filler).ToArray();
        await using (FileStream stream = File.Create(path))
        {
            await stream.WriteAsync(head);
            // 三字节字符正好跨在预览上限上：前 1 字节落在预览内，后 2 字节在预览外。
            await stream.WriteAsync("中"u8.ToArray());
            long remaining = DocumentLimits.DefaultContentLoadBytes + 1 - filler - 3;
            byte[] tail = new byte[remaining];
            Array.Fill(tail, (byte)'b');
            await stream.WriteAsync(tail);
        }

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, path);

        Assert.AreEqual(DocumentReadStatus.TextPreview, result.Status);
        Assert.IsNotNull(result.Text);
        // 截断回退到字符边界：正文比预览字节数少 1（少了那个不完整字符的首字节）。
        Assert.AreEqual(filler, (long)Encoding.UTF8.GetByteCount(result.Text!));
        Assert.DoesNotContain('\uFFFD', result.Text!);
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
