namespace Augit.App;

internal enum NativeDocumentModeIcon
{
    Source,
    Split,
    Preview,
    Formatted,
}

internal static partial class NativeTheme
{
    // 浅色值来自参考工具栏采样；深色值沿用 New UI 中性表面，仍需深色样本复核。
    internal static uint DocumentModeSelectedBackground(bool dark) => dark ? 0x004A4543u : 0x00DFDFDFu;

    internal static uint DocumentModeIconColor(bool dark) => dark ? 0x00D6D0CEu : 0x007E706Cu;

    // 168 DPI 的 Markdown 工具栏样本：四行原文、左文右框、圆角图片预览。
    // 保留 16px 网格内的留白，不把笔画拉满按钮，也不随文字字号改变图形。
    internal static bool DrawDocumentModeIcon(
        nint deviceContext,
        int centerX,
        int centerY,
        NativeDocumentModeIcon mode,
        uint color)
    {
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine L(float x1, float y1, float x2, float y2) =>
            new(centerX + (x1 - 8) * u, centerY + (y1 - 8) * u,
                centerX + (x2 - 8) * u, centerY + (y2 - 8) * u);
        NativeGdiPlusDrawing.StrokeArc A(float x, float y, float diameter, float start) =>
            new(centerX + (x - 8) * u, centerY + (y - 8) * u,
                diameter * u, diameter * u, start, 90);
        NativeGdiPlusDrawing.StrokeEllipse E(float x, float y, float diameter) =>
            new(centerX + (x - 8) * u, centerY + (y - 8) * u, diameter * u, diameter * u);
        NativeGdiPlusDrawing.StrokeBezier B(
            float startX,
            float startY,
            float control1X,
            float control1Y,
            float control2X,
            float control2Y,
            float endX,
            float endY) =>
            new(
                centerX + (startX - 8) * u,
                centerY + (startY - 8) * u,
                centerX + (control1X - 8) * u,
                centerY + (control1Y - 8) * u,
                centerX + (control2X - 8) * u,
                centerY + (control2Y - 8) * u,
                centerX + (endX - 8) * u,
                centerY + (endY - 8) * u);

        NativeGdiPlusDrawing.StrokeLine[] lines;
        NativeGdiPlusDrawing.StrokeArc[] arcs = [];
        NativeGdiPlusDrawing.StrokeEllipse[] ellipses = [];
        NativeGdiPlusDrawing.StrokeBezier[] beziers = [];
        switch (mode)
        {
            case NativeDocumentModeIcon.Source:
                lines = [L(3, 3.5f, 13, 3.5f), L(3, 6.5f, 13, 6.5f),
                    L(3, 9.5f, 13, 9.5f), L(3, 12.5f, 13, 12.5f)];
                break;
            case NativeDocumentModeIcon.Split:
                lines = [L(1.5f, 3.5f, 6, 3.5f), L(1.5f, 6.5f, 6, 6.5f),
                    L(1.5f, 9.5f, 6, 9.5f), L(1.5f, 12.5f, 6, 12.5f),
                    L(10, 2.5f, 13, 2.5f), L(14.5f, 4, 14.5f, 12),
                    L(13, 13.5f, 10, 13.5f), L(8.5f, 12, 8.5f, 4)];
                arcs = [A(8.5f, 2.5f, 3, 180), A(11.5f, 2.5f, 3, 270),
                    A(11.5f, 10.5f, 3, 0), A(8.5f, 10.5f, 3, 90)];
                break;
            case NativeDocumentModeIcon.Preview:
                lines = [L(4.5f, 2.5f, 11.5f, 2.5f), L(13.5f, 4.5f, 13.5f, 11.5f),
                    L(11.5f, 13.5f, 4.5f, 13.5f), L(2.5f, 11.5f, 2.5f, 4.5f),
                    L(2.5f, 9.5f, 4.5f, 7.5f), L(4.5f, 7.5f, 10.5f, 13.5f)];
                arcs = [A(2.5f, 2.5f, 4, 180), A(9.5f, 2.5f, 4, 270),
                    A(9.5f, 9.5f, 4, 0), A(2.5f, 9.5f, 4, 90)];
                ellipses = [E(8.5f, 4.5f, 3)];
                break;
            case NativeDocumentModeIcon.Formatted:
                // JSON 格式化图标使用结构化文本的花括号，避免复用 Markdown 图片预览图形。
                lines = [];
                beziers = [
                    B(6.5f, 2.5f, 4.5f, 2.5f, 4.5f, 4.5f, 4.5f, 6f),
                    B(4.5f, 6f, 4.5f, 7.2f, 3.5f, 7.2f, 3.5f, 8f),
                    B(3.5f, 8f, 3.5f, 8.8f, 4.5f, 8.8f, 4.5f, 10f),
                    B(4.5f, 10f, 4.5f, 11.5f, 4.5f, 13.5f, 6.5f, 13.5f),
                    B(9.5f, 2.5f, 11.5f, 2.5f, 11.5f, 4.5f, 11.5f, 6f),
                    B(11.5f, 6f, 11.5f, 7.2f, 12.5f, 7.2f, 12.5f, 8f),
                    B(12.5f, 8f, 12.5f, 8.8f, 11.5f, 8.8f, 11.5f, 10f),
                    B(11.5f, 10f, 11.5f, 11.5f, 11.5f, 13.5f, 9.5f, 13.5f),
                ];
                break;
            default:
                return false;
        }
        return NativeGdiPlusDrawing.StrokeShapes(
            deviceContext,
            color,
            Scale(1f),
            lines,
            ellipses,
            [],
            arcs,
            beziers);
    }
}
