namespace Augit.App;

internal static partial class NativeTheme
{
    internal static void DrawImageActionIcon(nint dc, int centerX, int centerY, bool fit, bool zoomIn, uint color)
    {
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine L(float x1, float y1, float x2, float y2) =>
            new(centerX + (x1 - 8) * u, centerY + (y1 - 8) * u, centerX + (x2 - 8) * u, centerY + (y2 - 8) * u);
        if (fit)
        {
            _ = NativeGdiPlusDrawing.StrokeShapes(dc, color, u,
                [L(2, 6, 2, 2), L(2, 2, 6, 2), L(10, 2, 14, 2), L(14, 2, 14, 6),
                 L(2, 10, 2, 14), L(2, 14, 6, 14), L(10, 14, 14, 14), L(14, 14, 14, 10)], [], [], [], []);
            return;
        }
        NativeGdiPlusDrawing.StrokeLine[] lines = zoomIn
            ? [L(10.5f, 10.5f, 14, 14), L(4.5f, 7, 9.5f, 7), L(7, 4.5f, 7, 9.5f)]
            : [L(10.5f, 10.5f, 14, 14), L(4.5f, 7, 9.5f, 7)];
        _ = NativeGdiPlusDrawing.StrokeShapes(dc, color, u, lines,
            [new(centerX - 5.5f * u, centerY - 5.5f * u, 9 * u, 9 * u)], [], [], []);
    }
}
