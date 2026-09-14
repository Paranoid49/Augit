using System.Runtime.InteropServices;

namespace Augit.App;

internal static partial class NativeGdiPlusDrawing
{
    private const int GdiPlusOk = 0;
    private const int FillModeAlternate = 0;
    private const int SmoothingModeAntiAlias = 4;
    private const int PixelOffsetModeHalf = 4;
    private const int UnitPixel = 2;
    private const int LineCapRound = 2;
    private const int LineJoinRound = 2;
    private static readonly object StartupGate = new();
    private static nint _token;
    private static bool _startupAttempted;

    internal readonly record struct ColoredStrokeLine(StrokeLine Line, uint Color);

    internal static bool StrokeColoredLines(nint deviceContext, float width, ReadOnlySpan<ColoredStrokeLine> lines)
    {
        if (deviceContext == 0 || width <= 0 || lines.IsEmpty || !EnsureStarted()) return false;
        if (GdipCreateFromHDC(deviceContext, out nint graphics) != GdiPlusOk || graphics == 0) return false;
        Span<nint> pens = stackalloc nint[4];
        Span<uint> colors = stackalloc uint[4];
        int count = 0;
        try
        {
            _ = GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);
            _ = GdipSetPixelOffsetMode(graphics, PixelOffsetModeHalf);
            foreach (ColoredStrokeLine item in lines)
            {
                int index = colors[..count].IndexOf(item.Color);
                if (index < 0)
                {
                    if (count == pens.Length) return false;
                    if (GdipCreatePen1(ToArgb(item.Color), width, UnitPixel, out nint pen) != GdiPlusOk || pen == 0) return false;
                    index = count++;
                    colors[index] = item.Color;
                    pens[index] = pen;
                    _ = GdipSetPenStartCap(pen, LineCapRound);
                    _ = GdipSetPenEndCap(pen, LineCapRound);
                    _ = GdipSetPenLineJoin(pen, LineJoinRound);
                }
                // 复用四种画笔，但严格保留输入顺序，避免交叉点和虚线端点的覆盖颜色改变。
                StrokeLine line = item.Line;
                if (GdipDrawLine(graphics, pens[index], line.StartX, line.StartY, line.EndX, line.EndY) != GdiPlusOk) return false;
            }
            return true;
        }
        finally
        {
            for (int index = 0; index < count; index++) _ = GdipDeletePen(pens[index]);
            _ = GdipDeleteGraphics(graphics);
        }
    }

    internal static bool FillRoundedRectangle(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint color,
        int cornerDiameter)
    {
        if (deviceContext == 0
            || rectangle.Right <= rectangle.Left
            || rectangle.Bottom <= rectangle.Top
            || !EnsureStarted())
        {
            return false;
        }

        if (GdipCreateFromHDC(deviceContext, out nint graphics) != GdiPlusOk || graphics == 0)
        {
            return false;
        }

        nint path = 0;
        nint brush = 0;
        try
        {
            _ = GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);
            _ = GdipSetPixelOffsetMode(graphics, PixelOffsetModeHalf);
            if (GdipCreatePath(FillModeAlternate, out path) != GdiPlusOk || path == 0)
            {
                return false;
            }

            int width = rectangle.Right - rectangle.Left;
            int height = rectangle.Bottom - rectangle.Top;
            int diameter = NormalizeCornerDiameter(width, height, cornerDiameter);
            if (!AddRoundedRectanglePath(path, rectangle.Left, rectangle.Top, width, height, diameter))
            {
                return false;
            }

            if (GdipCreateSolidFill(ToArgb(color), out brush) != GdiPlusOk || brush == 0)
            {
                return false;
            }

            return GdipFillPath(graphics, brush, path) == GdiPlusOk;
        }
        finally
        {
            if (brush != 0)
            {
                _ = GdipDeleteBrush(brush);
            }

            if (path != 0)
            {
                _ = GdipDeletePath(path);
            }

            _ = GdipDeleteGraphics(graphics);
        }
    }

    internal static bool StrokeShapes(
        nint deviceContext,
        uint color,
        float width,
        ReadOnlySpan<StrokeLine> lines,
        ReadOnlySpan<StrokeEllipse> ellipses,
        ReadOnlySpan<StrokeRectangle> rectangles,
        ReadOnlySpan<StrokeArc> arcs = default,
        ReadOnlySpan<StrokeBezier> beziers = default)
    {
        if (deviceContext == 0
            || width <= 0
            || (lines.IsEmpty && ellipses.IsEmpty && rectangles.IsEmpty && arcs.IsEmpty && beziers.IsEmpty)
            || !EnsureStarted())
        {
            return false;
        }

        if (GdipCreateFromHDC(deviceContext, out nint graphics) != GdiPlusOk || graphics == 0)
        {
            return false;
        }

        nint pen = 0;
        try
        {
            _ = GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);
            _ = GdipSetPixelOffsetMode(graphics, PixelOffsetModeHalf);
            if (GdipCreatePen1(ToArgb(color), width, UnitPixel, out pen) != GdiPlusOk || pen == 0)
            {
                return false;
            }

            _ = GdipSetPenStartCap(pen, LineCapRound);
            _ = GdipSetPenEndCap(pen, LineCapRound);
            _ = GdipSetPenLineJoin(pen, LineJoinRound);
            foreach (StrokeLine line in lines)
            {
                if (GdipDrawLine(graphics, pen, line.StartX, line.StartY, line.EndX, line.EndY) != GdiPlusOk)
                {
                    return false;
                }
            }

            foreach (StrokeEllipse ellipse in ellipses)
            {
                if (GdipDrawEllipse(graphics, pen, ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height) != GdiPlusOk)
                {
                    return false;
                }
            }

            foreach (StrokeRectangle rectangle in rectangles)
            {
                if (GdipDrawRectangle(graphics, pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height) != GdiPlusOk)
                {
                    return false;
                }
            }

            foreach (StrokeArc arc in arcs)
            {
                if (GdipDrawArc(graphics, pen, arc.X, arc.Y, arc.Width, arc.Height, arc.StartAngle, arc.SweepAngle) != GdiPlusOk)
                {
                    return false;
                }
            }

            foreach (StrokeBezier curve in beziers)
            {
                if (GdipDrawBezier(graphics, pen, curve.StartX, curve.StartY,
                    curve.Control1X, curve.Control1Y, curve.Control2X, curve.Control2Y,
                    curve.EndX, curve.EndY) != GdiPlusOk)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            if (pen != 0)
            {
                _ = GdipDeletePen(pen);
            }

            _ = GdipDeleteGraphics(graphics);
        }
    }

    internal static bool FillPolygon(nint deviceContext, uint color, ReadOnlySpan<FillPoint> points)
    {
        if (deviceContext == 0 || points.Length < 3 || !EnsureStarted())
        {
            return false;
        }
        if (GdipCreateFromHDC(deviceContext, out nint graphics) != GdiPlusOk || graphics == 0)
        {
            return false;
        }
        nint brush = 0;
        try
        {
            _ = GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);
            _ = GdipSetPixelOffsetMode(graphics, PixelOffsetModeHalf);
            if (GdipCreateSolidFill(ToArgb(color), out brush) != GdiPlusOk || brush == 0)
            {
                return false;
            }
            return GdipFillPolygon(graphics, brush, in MemoryMarshal.GetReference(points), points.Length, FillModeAlternate) == GdiPlusOk;
        }
        finally
        {
            if (brush != 0) _ = GdipDeleteBrush(brush);
            _ = GdipDeleteGraphics(graphics);
        }
    }

    internal static bool FillAndStrokePath(
        nint deviceContext,
        uint fill,
        uint outline,
        float outlineWidth,
        ReadOnlySpan<FillPoint> points,
        ReadOnlySpan<byte> types)
    {
        // 点和类型必须一一对应，避免本机路径创建读取超过托管缓冲区。
        if (deviceContext == 0 || points.Length < 3 || points.Length != types.Length
            || outlineWidth < 0 || !EnsureStarted()) return false;
        if (GdipCreateFromHDC(deviceContext, out nint graphics) != GdiPlusOk || graphics == 0) return false;

        nint path = 0, brush = 0, pen = 0;
        try
        {
            _ = GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);
            _ = GdipSetPixelOffsetMode(graphics, PixelOffsetModeHalf);
            if (GdipCreatePath2(in MemoryMarshal.GetReference(points), in MemoryMarshal.GetReference(types),
                    points.Length, FillModeAlternate, out path) != GdiPlusOk || path == 0) return false;
            if (GdipCreateSolidFill(ToArgb(fill), out brush) != GdiPlusOk || brush == 0) return false;
            if (GdipFillPath(graphics, brush, path) != GdiPlusOk) return false;
            if (outlineWidth == 0) return true;
            if (GdipCreatePen1(ToArgb(outline), outlineWidth, UnitPixel, out pen) != GdiPlusOk || pen == 0) return false;
            _ = GdipSetPenLineJoin(pen, LineJoinRound);
            return GdipDrawPath(graphics, pen, path) == GdiPlusOk;
        }
        finally
        {
            if (pen != 0) _ = GdipDeletePen(pen);
            if (brush != 0) _ = GdipDeleteBrush(brush);
            if (path != 0) _ = GdipDeletePath(path);
            _ = GdipDeleteGraphics(graphics);
        }
    }

    internal static int NormalizeCornerDiameterForTest(int width, int height, int requestedDiameter)
    {
        return NormalizeCornerDiameter(width, height, requestedDiameter);
    }

    internal static uint ToArgbForTest(uint color)
    {
        return ToArgb(color);
    }

    internal readonly record struct StrokeLine(float StartX, float StartY, float EndX, float EndY);

    internal readonly record struct StrokeEllipse(float X, float Y, float Width, float Height);

    internal readonly record struct StrokeRectangle(float X, float Y, float Width, float Height);

    internal readonly record struct StrokeArc(float X, float Y, float Width, float Height, float StartAngle, float SweepAngle);

    internal readonly record struct StrokeBezier(
        float StartX, float StartY, float Control1X, float Control1Y,
        float Control2X, float Control2Y, float EndX, float EndY);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct FillPoint(float X, float Y);

    private static bool EnsureStarted()
    {
        lock (StartupGate)
        {
            if (_startupAttempted)
            {
                return _token != 0;
            }

            GdiPlusStartupInput input = new()
            {
                GdiPlusVersion = 1,
            };
            _startupAttempted = true;
            if (GdiplusStartup(out _token, ref input, 0) != GdiPlusOk)
            {
                _token = 0;
                return false;
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
            return true;
        }
    }

    private static void Shutdown()
    {
        // 主窗口已关闭后，等待仍在退出的采样；超时交由进程回收，不能关闭它正在调用的 GDI+。
        if (!NativeImageView.WaitForBackgroundSampling()) return;
        lock (StartupGate)
        {
            if (_token == 0)
            {
                return;
            }

            GdiplusShutdown(_token);
            _token = 0;
        }
    }

    private static int NormalizeCornerDiameter(int width, int height, int requestedDiameter)
    {
        return Math.Clamp(requestedDiameter, 1, Math.Max(1, Math.Min(width, height)));
    }

    private static bool AddRoundedRectanglePath(
        nint path,
        int left,
        int top,
        int width,
        int height,
        int diameter)
    {
        float x = left;
        float y = top;
        float w = width;
        float h = height;
        float d = diameter;
        return GdipAddPathArc(path, x, y, d, d, 180, 90) == GdiPlusOk
            && GdipAddPathArc(path, x + w - d, y, d, d, 270, 90) == GdiPlusOk
            && GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90) == GdiPlusOk
            && GdipAddPathArc(path, x, y + h - d, d, d, 90, 90) == GdiPlusOk
            && GdipClosePathFigure(path) == GdiPlusOk;
    }

    private static uint ToArgb(uint color)
    {
        uint red = color & 0xFF;
        uint green = (color >> 8) & 0xFF;
        uint blue = (color >> 16) & 0xFF;
        return 0xFF000000 | red << 16 | green << 8 | blue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiPlusStartupInput
    {
        internal uint GdiPlusVersion;
        internal nint DebugEventCallback;
        internal int SuppressBackgroundThread;
        internal int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdiplusStartup(
        out nint token,
        ref GdiPlusStartupInput input,
        nint output);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern void GdiplusShutdown(nint token);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipCreateFromHDC(nint deviceContext, out nint graphics);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDeleteGraphics(nint graphics);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipSetSmoothingMode(nint graphics, int smoothingMode);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipSetPixelOffsetMode(nint graphics, int pixelOffsetMode);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipCreatePath(int fillMode, out nint path);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipCreatePath2(in FillPoint points, in byte types, int count, int fillMode, out nint path);


    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipAddPathArc(
        nint path,
        float x,
        float y,
        float width,
        float height,
        float startAngle,
        float sweepAngle);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipClosePathFigure(nint path);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDeletePath(nint path);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipCreateSolidFill(uint color, out nint brush);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDeleteBrush(nint brush);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipFillPath(nint graphics, nint brush, nint path);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDrawPath(nint graphics, nint pen, nint path);


    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipFillPolygon(nint graphics, nint brush, in FillPoint points, int count, int fillMode);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipCreatePen1(uint color, float width, int unit, out nint pen);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDeletePen(nint pen);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipSetPenStartCap(nint pen, int startCap);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipSetPenEndCap(nint pen, int endCap);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipSetPenLineJoin(nint pen, int lineJoin);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDrawLine(
        nint graphics,
        nint pen,
        float startX,
        float startY,
        float endX,
        float endY);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDrawEllipse(
        nint graphics,
        nint pen,
        float x,
        float y,
        float width,
        float height);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDrawArc(
        nint graphics,
        nint pen,
        float x,
        float y,
        float width,
        float height,
        float startAngle,
        float sweepAngle);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDrawBezier(
        nint graphics, nint pen, float startX, float startY,
        float control1X, float control1Y, float control2X, float control2Y, float endX, float endY);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDrawRectangle(
        nint graphics,
        nint pen,
        float x,
        float y,
        float width,
        float height);
}
