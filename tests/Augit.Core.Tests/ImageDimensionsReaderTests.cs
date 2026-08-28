using Augit.Core.Documents;

namespace Augit.Core.Tests;

[TestClass]
public sealed class ImageDimensionsReaderTests
{
    [TestMethod]
    public void 读取Png尺寸且避免像素乘法溢出()
    {
        byte[] content =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x13, 0x88, 0x00, 0x00, 0x13, 0x88,
        ];

        bool success = ImageDimensionsReader.TryRead(DocumentKind.Png, content, out ImageDimensions dimensions);

        Assert.IsTrue(success);
        Assert.AreEqual(5000, dimensions.Width);
        Assert.AreEqual(25_000_000L, dimensions.PixelCount);
    }

    [TestMethod]
    public void 拒绝无效Bmp尺寸()
    {
        byte[] content = new byte[26];
        content[0] = 0x42;
        content[1] = 0x4D;

        bool success = ImageDimensionsReader.TryRead(DocumentKind.Bmp, content, out _);

        Assert.IsFalse(success);
    }
}
