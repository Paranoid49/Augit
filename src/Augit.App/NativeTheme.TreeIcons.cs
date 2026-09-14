namespace Augit.App;

internal static partial class NativeTheme
{
    internal static (uint Outline, uint Fill) FolderIconColors(bool dark) => dark
        ? (Rgb(134, 138, 145), Rgb(67, 69, 74))
        : (Rgb(108, 112, 126), Rgb(235, 236, 240));

    internal static uint GitReferenceIconColor(bool dark) => dark ? Rgb(242, 184, 70) : Rgb(255, 175, 15);

    internal static bool DrawFolderIcon(nint dc, NativeMethods.Rectangle rectangle, bool dark, bool workspaceRoot, uint background)
    {
        float u = Scale(1f);
        float x = (rectangle.Left + rectangle.Right) / 2f - 8 * u;
        float y = (rectangle.Top + rectangle.Bottom) / 2f - 8 * u;
        NativeGdiPlusDrawing.FillPoint P(float px, float py) => new(x + px * u, y + py * u);
        // 展开前后共用闭合圆角轮廓，填充与描边共享曲线，避免用直线切角冒充圆角。
        ReadOnlySpan<NativeGdiPlusDrawing.FillPoint> points =
        [P(3, 2.5f), P(5.5f, 2.5f), P(6, 2.5f), P(6.3f, 2.7f), P(6.6f, 3),
         P(7.9f, 4.3f), P(8.2f, 4.6f), P(8.5f, 4.5f), P(9, 4.5f), P(13, 4.5f),
         P(13.83f, 4.5f), P(14.5f, 5.17f), P(14.5f, 6), P(14.5f, 12),
         P(14.5f, 12.83f), P(13.83f, 13.5f), P(13, 13.5f), P(3, 13.5f),
         P(2.17f, 13.5f), P(1.5f, 12.83f), P(1.5f, 12), P(1.5f, 4),
         P(1.5f, 3.17f), P(2.17f, 2.5f), P(3, 2.5f)];
        // GDI+ 类型：0 起点，1 直线，3 三次曲线点，末端 0x80 闭合。
        ReadOnlySpan<byte> types = [0, 1, 3, 3, 3, 1, 3, 3, 3, 1, 3, 3, 3, 1, 3, 3, 3, 1, 3, 3, 3, 1, 3, 3, 3 | 0x80];
        (uint outline, uint fill) = FolderIconColors(dark);
        if (!NativeGdiPlusDrawing.FillAndStrokePath(dc, fill, outline, u, points, types)) return false;
        if (!workspaceRoot) return true;

        // 工作区角标与目录内容无关；周围留出实际行背景，选中时不出现白色补丁。
        NativeMethods.Rectangle badge = new()
        {
            Left = (int)Math.Round(x + 9 * u),
            Top = (int)Math.Round(y + 9 * u),
            Right = (int)Math.Round(x + 16 * u),
            Bottom = (int)Math.Round(y + 16 * u),
        };
        NativeMethods.Rectangle halo = badge;
        halo.Left -= Scale(1); halo.Top -= Scale(1); halo.Right += Scale(1); halo.Bottom += Scale(1);
        if (!NativeGdiPlusDrawing.FillRoundedRectangle(dc, halo, background, Scale(5))) return false;
        if (!NativeGdiPlusDrawing.FillRoundedRectangle(dc, badge, dark ? Rgb(84, 138, 247) : Rgb(53, 116, 240), Scale(4))) return false;
        badge.Left += Scale(1); badge.Top += Scale(1); badge.Right -= Scale(1); badge.Bottom -= Scale(1);
        return NativeGdiPlusDrawing.FillRoundedRectangle(dc, badge, dark ? Rgb(37, 63, 97) : Rgb(231, 239, 253), Scale(2));
    }

    internal static bool DrawGitReferenceIcon(nint dc, NativeMethods.Rectangle rectangle, bool dark, uint background, bool filled)
    {
        float u = Scale(1f);
        float x = (rectangle.Left + rectangle.Right) / 2f - 8 * u;
        float y = (rectangle.Top + rectangle.Bottom) / 2f - 8 * u;
        NativeGdiPlusDrawing.FillPoint P(float px, float py) => new(x + px * u, y + py * u);
        // 斜向标签与右上圆孔构成两个闭合子路径；交替填充让孔保留真实行背景。
        ReadOnlySpan<NativeGdiPlusDrawing.FillPoint> points =
        [P(8.6f, 1.5f), P(13.5f, 1.5f), P(14.05f, 1.5f), P(14.5f, 1.95f), P(14.5f, 2.5f),
         P(14.5f, 7.4f), P(7.9f, 14), P(7.51f, 14.39f), P(6.89f, 14.39f), P(6.5f, 14),
         P(2, 9.5f), P(1.61f, 9.11f), P(1.61f, 8.49f), P(2, 8.1f), P(8.6f, 1.5f),
         P(12, 5), P(12, 5.83f), P(11.33f, 6.5f), P(10.5f, 6.5f),
         P(9.67f, 6.5f), P(9, 5.83f), P(9, 5), P(9, 4.17f), P(9.67f, 3.5f), P(10.5f, 3.5f),
         P(11.33f, 3.5f), P(12, 4.17f), P(12, 5)];
        ReadOnlySpan<byte> types = [0, 1, 3, 3, 3, 1, 1, 3, 3, 3, 1, 3, 3, 3, 1 | 0x80,
            0, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 | 0x80];
        uint color = GitReferenceIconColor(dark);
        return NativeGdiPlusDrawing.FillAndStrokePath(dc, filled ? color : background, color, filled ? 0 : u, points, types);
    }
}
