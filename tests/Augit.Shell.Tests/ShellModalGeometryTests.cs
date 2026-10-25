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

    [TestMethod]
    public void 设置窗居中后按工作区右边界回退()
    {
        // 与缺陷 D1 同值：Owner 在 4200,200 时中心位置会把 1040x672 的设置窗推到 5310，
        // 必须整体回退到工作区右边界，而不是只裁宽度。
        Assert.AreEqual(
            new WindowFrameRect(3440, 244, 4480, 916),
            ShellModalGeometry.CenterAndFit(
                new WindowFrameRect(4200, 200, 5380, 960),
                1040,
                672,
                new WindowFrameRect(2560, 0, 4480, 1040)));
    }

    [TestMethod]
    public void 设置窗居中后按工作区下边界回退()
    {
        Assert.AreEqual(
            new WindowFrameRect(2730, 368, 3770, 1040),
            ShellModalGeometry.CenterAndFit(
                new WindowFrameRect(2660, 780, 3840, 1540),
                1040,
                672,
                new WindowFrameRect(2560, 0, 4480, 1040)));
    }

    [TestMethod]
    public void 子窗大于工作区时收缩到工作区()
    {
        Assert.AreEqual(
            new WindowFrameRect(0, 0, 1024, 640),
            ShellModalGeometry.CenterAndFit(
                new WindowFrameRect(100, 100, 200, 200),
                2000,
                900,
                new WindowFrameRect(0, 0, 1024, 640)));
    }

    [TestMethod]
    public void 子窗尺寸非法时拒绝()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ShellModalGeometry.CenterAndFit(
            new WindowFrameRect(0, 0, 800, 600), 0, 300, new WindowFrameRect(0, 0, 1024, 640)));
        Assert.ThrowsExactly<ArgumentException>(() => ShellModalGeometry.CenterAndFit(
            new WindowFrameRect(0, 0, 800, 600), 400, 0, new WindowFrameRect(0, 0, 1024, 640)));
    }
}
