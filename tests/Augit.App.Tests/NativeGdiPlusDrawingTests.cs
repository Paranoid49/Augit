namespace Augit.App.Tests;

[TestClass]
public sealed class NativeGdiPlusDrawingTests
{
    [TestMethod]
    public void 闭合填充路径拒绝不匹配缓冲区和无效设备上下文()
    {
        NativeGdiPlusDrawing.FillPoint[] points = [new(1, 1), new(9, 1), new(5, 9)];
        Assert.IsFalse(NativeGdiPlusDrawing.FillAndStrokePath(0, 0, 0, 1, points, [0, 1, 129]));
        Assert.IsFalse(NativeGdiPlusDrawing.FillAndStrokePath(1, 0, 0, 1, points, [0, 1]));
        Assert.IsFalse(NativeGdiPlusDrawing.FillAndStrokePath(1, 0, 0, 1, [], []));
    }

    [TestMethod]
    public void 圆角直径不会越过控件短边()
    {
        Assert.AreEqual(12, NativeGdiPlusDrawing.NormalizeCornerDiameterForTest(30, 12, 24));
        Assert.AreEqual(6, NativeGdiPlusDrawing.NormalizeCornerDiameterForTest(30, 12, 6));
        Assert.AreEqual(1, NativeGdiPlusDrawing.NormalizeCornerDiameterForTest(30, 12, 0));
    }

    [TestMethod]
    public void Win32颜色会按GDI加约定转换为不透明Argb()
    {
        const uint blueColorRef = 0x00F07435;

        uint argb = NativeGdiPlusDrawing.ToArgbForTest(blueColorRef);

        Assert.AreEqual(0xFF3574F0u, argb);
    }

    [TestMethod]
    public void 无效设备上下文不会尝试绘制()
    {
        NativeMethods.Rectangle rectangle = new() { Left = 0, Top = 0, Right = 20, Bottom = 20 };

        Assert.IsFalse(NativeGdiPlusDrawing.FillRoundedRectangle(0, rectangle, 0x00FFFFFF, 12));
    }

    [TestMethod]
    public void 无效设备上下文不会尝试绘制抗锯齿图标()
    {
        NativeGdiPlusDrawing.StrokeLine[] lines =
        [
            new(1, 1, 8, 8),
        ];

        Assert.IsFalse(NativeGdiPlusDrawing.StrokeShapes(
            0,
            0x00FFFFFF,
            1.5f,
            lines,
            [],
            []));
    }

    [TestMethod]
    public void 空图形不会初始化绘制资源()
    {
        Assert.IsFalse(NativeGdiPlusDrawing.StrokeShapes(
            1,
            0x00FFFFFF,
            1.5f,
            [],
            [],
            []));
    }

    [TestMethod]
    public void 文件类型图标按扩展名选择PyCharm对应几何符号()
    {
        Assert.AreEqual("markdown", NativeTheme.FileTypeIconKindForTest("README.md"));
        Assert.AreEqual("csharp", NativeTheme.FileTypeIconKindForTest("MainWindow.cs"));
        Assert.AreEqual("markup", NativeTheme.FileTypeIconKindForTest("index.html"));
        Assert.AreEqual("structured", NativeTheme.FileTypeIconKindForTest("settings.json"));
        Assert.AreEqual("image", NativeTheme.FileTypeIconKindForTest("preview.bmp"));
        Assert.AreEqual("file", NativeTheme.FileTypeIconKindForTest("LICENSE.txt"));
        Assert.IsFalse(NativeTheme.FileTypeIconUsesPageFrameForTest("README.md"));
        Assert.IsTrue(NativeTheme.FileTypeIconUsesPageFrameForTest("LICENSE.txt"));
    }
}
