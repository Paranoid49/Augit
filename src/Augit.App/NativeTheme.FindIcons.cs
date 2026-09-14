namespace Augit.App;

internal enum NativeFindOptionIcon { MatchCase, WholeWord, RegularExpression }

internal static partial class NativeTheme
{
    // 查找开关沿用 Aa、带边界的 ab 和正则符号语义，绘制固定 16px 图形，不依赖用户字体。
    internal static bool DrawFindOptionIcon(nint deviceContext, NativeMethods.Rectangle rectangle, NativeFindOptionIcon icon, uint color)
    {
        float x = (rectangle.Left + rectangle.Right) / 2f;
        float y = (rectangle.Top + rectangle.Bottom) / 2f;
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine L(float x1, float y1, float x2, float y2) =>
            new(x + (x1 - 8) * u, y + (y1 - 8) * u, x + (x2 - 8) * u, y + (y2 - 8) * u);
        NativeGdiPlusDrawing.StrokeEllipse E(float left, float top, float width, float height) =>
            new(x + (left - 8) * u, y + (top - 8) * u, width * u, height * u);
        return icon switch
        {
            NativeFindOptionIcon.MatchCase => NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, u,
                [L(1.5f, 12, 4.5f, 4), L(4.5f, 4, 7.5f, 12), L(2.5f, 9, 6.5f, 9), L(13.5f, 7.5f, 13.5f, 12)],
                [E(9, 7.5f, 4.5f, 4.5f)], []),
            NativeFindOptionIcon.WholeWord => NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, u,
                [L(3, 3, 1, 3), L(1, 3, 1, 13), L(1, 13, 3, 13), L(13, 3, 15, 3), L(15, 3, 15, 13), L(15, 13, 13, 13),
                    L(7, 7.5f, 7, 11.5f), L(9, 4.5f, 9, 11.5f)],
                [E(3.5f, 7.5f, 3.5f, 4), E(9, 7.5f, 3.5f, 4)], []),
            NativeFindOptionIcon.RegularExpression => NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, u,
                [L(10.5f, 3, 10.5f, 10), L(7.5f, 4.75f, 13.5f, 8.25f), L(7.5f, 8.25f, 13.5f, 4.75f)],
                [E(2.5f, 11.5f, 1.5f, 1.5f)], []),
            _ => false,
        };
    }
}
