using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellModalGeometryTests
{
    [TestMethod]
    public void 模态按实际Dpi裁切并保留右下边界()
    {
        Assert.AreEqual(new WindowFrameRect(175, 88, 875, 438), ShellModalGeometry.Clip(100, 50, 400, 200, 2000, 1200, 1.75));
        Assert.AreEqual(new WindowFrameRect(0, 0, 2000, 1200), ShellModalGeometry.Clip(-10, -10, 5000, 5000, 2000, 1200, 1.75));
    }

    [TestMethod]
    public void 适屏支持负坐标显示器并保持窗口大小()
    {
        Assert.AreEqual(new WindowFrameRect(-900, 500, -100, 1000), ShellModalGeometry.Fit(new(-700, 700, 100, 1200), new(-1920, 0, -100, 1000)));
        Assert.AreEqual(new WindowFrameRect(-1920, 0, -100, 1000), ShellModalGeometry.Fit(new(-4000, -3000, 5000, 5000), new(-1920, 0, -100, 1000)));
    }

    [TestMethod]
    public void 非有限或空矩形必须拒绝()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ShellModalGeometry.Clip(double.NaN, 0, 300, 200, 800, 600, 1));
        Assert.ThrowsExactly<ArgumentException>(() => ShellModalGeometry.Clip(0, 0, double.PositiveInfinity, 200, 800, 600, 1));
        Assert.ThrowsExactly<ArgumentException>(() => ShellModalGeometry.Clip(0, 0, 0, 200, 800, 600, 1));
    }

    [TestMethod]
    public void 跨屏缩放保留逻辑裁切并支持负坐标()
    {
        Assert.AreEqual(new WindowFrameRect(-150, 75, 600, 375), ShellModalGeometry.Rescale(new(-175, 88, 700, 438), 1.75, 1.5));
        Assert.ThrowsExactly<ArgumentException>(() => ShellModalGeometry.Rescale(new(0, 0, 400, 200), 0, 1.5));
    }
}
