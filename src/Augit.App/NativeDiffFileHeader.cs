namespace Augit.App;

internal static class NativeDiffFileHeader
{
    internal static int Height(bool sideBySide)
    {
        int line = NativeTheme.UiLineHeight;
        int sideBySideHeight = Math.Max(NativeTheme.Scale(31), line + NativeTheme.Scale(12));
        int row = Math.Max(NativeTheme.Scale(24), line + NativeTheme.Scale(4));
        int unifiedHeight = Math.Max(NativeTheme.Scale(55), row * 2 + NativeTheme.Scale(7));
        return sideBySide ? sideBySideHeight : unifiedHeight;
    }

    // 单栏上下两行；双栏目标与右正文起点对齐。尺寸为现有视觉稿的逻辑像素适配。
    internal static NativeDiffFileHeaderLayout Calculate(
        NativeMethods.Rectangle bounds, bool sideBySide, int sourceWidth, int? gutterWidth = null)
    {
        int width = Math.Max(0, bounds.Right - bounds.Left);
        int inset = NativeTheme.Scale(8);
        int icon = NativeTheme.Scale(12);
        int firstTop = bounds.Top;
        int firstBottom = bounds.Bottom;
        int targetLeft = bounds.Left;
        int targetTop = bounds.Top;
        int targetBottom = bounds.Bottom;
        int sourceRight = bounds.Right;
        if (sideBySide)
        {
            int body = Math.Max(0, width - NativeTheme.Scale(2));
            int gutter = Math.Min(body, gutterWidth ?? NativeTheme.Scale(84));
            targetLeft += NativeTheme.Scale(1) + (body - gutter) / 2 + gutter;
            sourceRight = targetLeft - gutter;
        }
        else
        {
            int row = Math.Max(NativeTheme.Scale(24), NativeTheme.UiLineHeight + NativeTheme.Scale(4));
            firstTop += NativeTheme.Scale(3);
            firstBottom = firstTop + row;
            targetTop = firstBottom;
            targetBottom = targetTop + row;
        }
        NativeMethods.Rectangle source = new() { Left = bounds.Left + NativeTheme.Scale(28), Top = firstTop, Bottom = firstBottom };
        int available = Math.Max(0, sourceRight - inset - source.Left);
        source.Right = source.Left + Math.Min(Math.Max(0, sourceWidth), available / 2);
        NativeMethods.Rectangle path = new()
        {
            Left = Math.Min(sourceRight - inset, source.Right + inset),
            Right = Math.Max(sourceRight - inset, source.Right),
            Top = firstTop,
            Bottom = firstBottom
        };
        NativeMethods.Rectangle target = new()
        {
            Left = targetLeft + NativeTheme.Scale(28),
            Right = Math.Max(targetLeft + NativeTheme.Scale(28), bounds.Right - inset),
            Top = targetTop,
            Bottom = targetBottom
        };
        NativeMethods.Rectangle IconAt(int left, int top, int bottom) => new()
        {
            Left = left + inset,
            Right = left + inset + icon,
            Top = (top + bottom - icon) / 2,
            Bottom = (top + bottom - icon) / 2 + icon,
        };
        return new(source, target, path, IconAt(bounds.Left, firstTop, firstBottom), IconAt(targetLeft, targetTop, targetBottom));
    }

    internal static void Draw(nint dc, NativeMethods.Rectangle bounds, bool sideBySide,
        string source, string target, string path, NativeThemePalette palette, int? gutterWidth = null)
    {
        nint oldFont = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        _ = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        NativeMethods.Rectangle measured = new();
        _ = NativeMethods.DrawText(dc, source, source.Length, ref measured,
            NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextCalculateRectangle);
        NativeDiffFileHeaderLayout layout = Calculate(bounds, sideBySide, measured.Right - measured.Left, gutterWidth);
        DrawText(source, layout.Source, palette.Text);
        DrawText(target, layout.Target, palette.Text);
        DrawText(path, layout.Path, palette.Muted);
        DrawLock(layout.SourceIcon);
        DrawLock(layout.TargetIcon);
        if (oldFont != 0) _ = NativeMethods.SelectObject(dc, oldFont);

        void DrawText(string value, NativeMethods.Rectangle rectangle, uint color)
        {
            _ = NativeMethods.SetTextColor(dc, color);
            _ = NativeMethods.DrawText(dc, value, value.Length, ref rectangle,
                NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextVerticalCenter
                    | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis);
        }
        void DrawLock(NativeMethods.Rectangle rectangle)
        {
            float u = NativeTheme.Scale(1f), x = rectangle.Left, y = rectangle.Top;
            _ = NativeGdiPlusDrawing.StrokeShapes(dc, palette.Muted, u,
                [new(x + 3 * u, y + 5 * u, x + 3 * u, y + 3 * u),
                    new(x + 9 * u, y + 3 * u, x + 9 * u, y + 5 * u)], [],
                [new(x + u, y + 5 * u, 10 * u, 7 * u)],
                [new(x + 3 * u, y, 6 * u, 6 * u, 180, 180)]);
        }
    }
}

internal readonly record struct NativeDiffFileHeaderLayout(
    NativeMethods.Rectangle Source, NativeMethods.Rectangle Target, NativeMethods.Rectangle Path,
    NativeMethods.Rectangle SourceIcon, NativeMethods.Rectangle TargetIcon);
