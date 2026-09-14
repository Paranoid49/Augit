namespace Augit.App;

internal static partial class NativeTheme
{
    // 尺寸取自 Diff 视觉稿；按共同边界缩放，避免分数 DPI 累加误差。
    internal static NativeMethods.Rectangle DiffModeGroupBounds(int settingsLeft, int toolbarHeight)
    {
        int top = (toolbarHeight - Scale(31)) / 2;
        return new()
        {
            Left = settingsLeft - Scale(88),
            Top = top,
            Right = settingsLeft - Scale(88) + Scale(81),
            Bottom = top + Scale(31)
        };
    }

    internal static NativeMethods.Rectangle DiffModeButtonBounds(NativeMethods.Rectangle group, bool sideBySide) => new()
    {
        Left = group.Left + Scale(sideBySide ? 2 : 41),
        Right = group.Left + Scale(sideBySide ? 40 : 79),
        Top = group.Top + Scale(2),
        Bottom = group.Top + Scale(29),
    };

    internal static uint DiffModeGroupColor(bool dark) => dark ? Palette(true).PanelMuted : Rgb(244, 245, 247);

    internal static void DrawDiffModeGroup(nint deviceContext, NativeMethods.Rectangle bounds, bool dark)
    {
        if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return;
        _ = NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, bounds, Palette(dark).BorderStrong, Scale(12));
        bounds.Left += Scale(1);
        bounds.Top += Scale(1);
        bounds.Right -= Scale(1);
        bounds.Bottom -= Scale(1);
        _ = NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, bounds, DiffModeGroupColor(dark), Scale(10));
    }

    internal static bool DrawDiffToolbarMode(
        NativeMethods.DrawItem item, NativeDiffModeIcon mode, bool selected, bool dark)
    {
        NativeThemePalette palette = Palette(dark);
        bool grouped = mode != NativeDiffModeIcon.IgnoreWhitespace;
        nint brush = NativeMethods.CreateSolidBrush(grouped ? DiffModeGroupColor(dark) : palette.Panel);
        if (brush != 0)
        {
            NativeMethods.Rectangle rectangle = item.ItemRectangle;
            _ = NativeMethods.FillRectangle(item.DeviceContext, ref rectangle, brush);
            _ = NativeMethods.DeleteObject(brush);
        }
        if (selected)
        {
            _ = NativeGdiPlusDrawing.FillRoundedRectangle(item.DeviceContext, item.ItemRectangle,
                grouped ? palette.Panel : palette.AccentSoft, Scale(10));
        }
        else if ((item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0)
        {
            _ = NativeGdiPlusDrawing.FillRoundedRectangle(item.DeviceContext, item.ItemRectangle, palette.Hover, Scale(10));
        }
        uint color = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0
            ? palette.Faint : selected ? grouped ? palette.Text : palette.Accent : palette.Muted;
        bool drawn = DrawDiffModeIcon(item.DeviceContext, item.ItemRectangle, mode, color);
        DrawToolbarFocus(item, palette);
        return drawn;
    }

    internal static void DrawToolbarFocus(NativeMethods.DrawItem item, NativeThemePalette palette)
    {
        if ((item.ItemState & NativeMethods.OwnerDrawFocus) == 0) return;
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, Scale(1)), palette.Accent);
        if (pen == 0) return;
        nint previousPen = NativeMethods.SelectObject(item.DeviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(item.DeviceContext, NativeMethods.GetStockObject(NativeMethods.NullBrush));
        _ = NativeMethods.DrawRectangle(item.DeviceContext,
            item.ItemRectangle.Left + Scale(2), item.ItemRectangle.Top + Scale(2),
            item.ItemRectangle.Right - Scale(3), item.ItemRectangle.Bottom - Scale(3));
        if (previousPen != 0) _ = NativeMethods.SelectObject(item.DeviceContext, previousPen);
        if (previousBrush != 0) _ = NativeMethods.SelectObject(item.DeviceContext, previousBrush);
        _ = NativeMethods.DeleteObject(pen);
    }
}
