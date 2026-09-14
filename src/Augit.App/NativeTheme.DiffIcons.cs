namespace Augit.App;

internal enum NativeDiffModeIcon
{
    Unified,
    SideBySide,
    IgnoreWhitespace,
}

internal static partial class NativeTheme
{
    // 单双栏依据 PyCharm 可见圆角框复原；空白符号是 Augit 既有开关的适配。
    // 工作区 Diff 和历史/引用比较共用 16px 网格，不依赖字体或设备上下文中的画刷。
    internal static bool DrawDiffModeIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        NativeDiffModeIcon mode,
        uint color)
    {
        float centerX = (rectangle.Left + rectangle.Right) / 2f;
        float centerY = (rectangle.Top + rectangle.Bottom) / 2f;
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine L(float x1, float y1, float x2, float y2) =>
            new(centerX + (x1 - 8) * u, centerY + (y1 - 8) * u,
                centerX + (x2 - 8) * u, centerY + (y2 - 8) * u);
        NativeGdiPlusDrawing.StrokeArc A(float x, float y, float diameter, float start, float sweep = 90) =>
            new(centerX + (x - 8) * u, centerY + (y - 8) * u,
                diameter * u, diameter * u, start, sweep);

        if (mode == NativeDiffModeIcon.IgnoreWhitespace)
        {
            return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, u,
                [L(9, 2.5f, 9, 13.5f), L(12, 2.5f, 12, 13.5f), L(6, 2.5f, 14, 2.5f), L(6, 9.5f, 9, 9.5f)],
                [], [], [A(2.5f, 2.5f, 7, 90, 180)]);
        }
        if (mode is not NativeDiffModeIcon.Unified and not NativeDiffModeIcon.SideBySide)
        {
            return false;
        }

        NativeGdiPlusDrawing.StrokeLine[] lines = mode == NativeDiffModeIcon.SideBySide
            ? [L(4.5f, 2.5f, 11.5f, 2.5f), L(13.5f, 4.5f, 13.5f, 11.5f),
                L(11.5f, 13.5f, 4.5f, 13.5f), L(2.5f, 11.5f, 2.5f, 4.5f), L(8, 2.5f, 8, 13.5f)]
            : [L(4.5f, 2.5f, 11.5f, 2.5f), L(13.5f, 4.5f, 13.5f, 11.5f),
                L(11.5f, 13.5f, 4.5f, 13.5f), L(2.5f, 11.5f, 2.5f, 4.5f)];
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, u, lines, [], [],
            [A(2.5f, 2.5f, 4, 180), A(9.5f, 2.5f, 4, 270),
                A(9.5f, 9.5f, 4, 0), A(2.5f, 9.5f, 4, 90)]);
    }
}
