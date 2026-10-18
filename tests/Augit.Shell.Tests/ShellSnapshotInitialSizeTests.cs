using System.Buffers.Binary;
using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellSnapshotInitialSizeTests
{
    private static byte[] Header(int width, int height)
    {
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        return png;
    }

    [TestMethod]
    public void 首图使用真实PNG物理宽高且不按视口取整()
    {
        Assert.AreEqual("width:1400px;height:800px;", ShellSnapshotPresentation.InitialImageSize(Header(2450, 1400), 1.75));
        Assert.AreEqual("width:1401.142857142857px;height:931.4285714285714px;",
            ShellSnapshotPresentation.InitialImageSize(Header(2452, 1630), 1.75));
    }

    [TestMethod]
    public void 无效头和尺寸比例不得进入首图布局()
    {
        foreach (double scale in new[] { 0, -1, double.NaN, double.PositiveInfinity })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ShellSnapshotPresentation.InitialImageSize(Header(10, 10), scale));
        foreach (byte[] png in new[] { Array.Empty<byte>(), Header(0, 1), Header(1, -1), new byte[24] })
            Assert.ThrowsExactly<ArgumentException>(() => ShellSnapshotPresentation.InitialImageSize(png, 1));
        byte[] wrongChunk = Header(10, 10);
        wrongChunk[12] = 0;
        Assert.ThrowsExactly<ArgumentException>(() => ShellSnapshotPresentation.InitialImageSize(wrongChunk, 1));
    }
}
