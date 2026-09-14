namespace Augit.App;

internal static partial class NativeTheme
{
    // 菜单和其他动作入口共用 16px 图形，调用者传入的区域只决定中心，不拉伸路径。
    internal static bool DrawMenuActionIcon(
        nint deviceContext,
        NativeContextMenuIcon icon,
        NativeMethods.Rectangle rectangle,
        uint color)
    {
        int centerX = (rectangle.Left + rectangle.Right) / 2;
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        switch (icon)
        {
            case NativeContextMenuIcon.None:
                return true;
            case NativeContextMenuIcon.Search:
                return DrawNavigationIcon(deviceContext, rectangle, NativeNavigationIcon.Search, color);
            case NativeContextMenuIcon.Refresh:
                return DrawRefreshIcon(deviceContext, centerX, centerY, color);
            case NativeContextMenuIcon.Compare:
                return DrawCompareIcon(deviceContext, centerX, centerY, color);
            case NativeContextMenuIcon.Reset:
            case NativeContextMenuIcon.Revert:
                return DrawRollbackIcon(deviceContext, centerX, centerY, color);
            case NativeContextMenuIcon.History:
                return DrawHistoryIcon(deviceContext, rectangle, color);
            case NativeContextMenuIcon.Locate:
                return DrawLocateIcon(deviceContext, centerX, centerY, color);
            case NativeContextMenuIcon.Terminal:
                return DrawToolWindowIcon(deviceContext, rectangle, NativeToolWindowIcon.Terminal, color);
            case NativeContextMenuIcon.Settings:
                return DrawSettingsIcon(deviceContext, rectangle, color);
            case NativeContextMenuIcon.Close:
                return DrawTabCloseIcon(deviceContext, centerX, centerY, color);
            case NativeContextMenuIcon.Rename:
                return DrawRenameIcon(deviceContext, centerX, centerY, color);
            case NativeContextMenuIcon.Clone:
                return DrawCloneIcon(deviceContext, centerX, centerY, color);
            case NativeContextMenuIcon.Conflict:
                return DrawConflictIcon(deviceContext, centerX, centerY, color);
        }

        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine L(float x1, float y1, float x2, float y2) =>
            new(centerX + x1 * u, centerY + y1 * u, centerX + x2 * u, centerY + y2 * u);
        NativeGdiPlusDrawing.StrokeEllipse E(float x, float y, float width, float height) =>
            new(centerX + x * u, centerY + y * u, width * u, height * u);
        NativeGdiPlusDrawing.StrokeArc A(float x, float y, float size, float start, float sweep) =>
            new(centerX + x * u, centerY + y * u, size * u, size * u, start, sweep);
        NativeGdiPlusDrawing.StrokeBezier B(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4) =>
            new(centerX + x1 * u, centerY + y1 * u, centerX + x2 * u, centerY + y2 * u,
                centerX + x3 * u, centerY + y3 * u, centerX + x4 * u, centerY + y4 * u);

        NativeGdiPlusDrawing.StrokeLine[] lines = [];
        NativeGdiPlusDrawing.StrokeEllipse[] ellipses = [];
        NativeGdiPlusDrawing.StrokeArc[] arcs = [];
        NativeGdiPlusDrawing.StrokeBezier[] beziers = [];
        float strokeWidth = 1.5f;
        switch (icon)
        {
            case NativeContextMenuIcon.Copy:
                // 168 DPI 屏幕样本：双页细轮廓、前页三条短线，页框宽度小于通用工具图标。
                strokeWidth = 1f;
                lines = [L(-3, -6, 4, -6), L(6, -4, 6, 3), L(-4, -4, 1, -4),
                    L(3, -2, 3, 5), L(1, 7, -4, 7), L(-6, 5, -6, -2),
                    L(-3, -1, 0, -1), L(-3, 1.5f, 0, 1.5f), L(-3, 4, 0, 4)];
                arcs = [A(2, -6, 4, -90, 90), A(-6, -4, 4, 180, 90), A(-1, -4, 4, -90, 90),
                    A(-1, 3, 4, 0, 90), A(-6, 3, 4, 90, 90)];
                break;
            case NativeContextMenuIcon.CherryPick:
                ellipses = [E(-7, 1, 5, 5), E(1, 2, 5, 5)];
                beziers = [B(-4.5f, 1, -4, -2, 2, -2, 2, -6), B(3.5f, 2, 5, -2, 2, -3, 2, -6),
                    B(2, -6, -1, -6, -2, -5, -2, -3)];
                break;
            case NativeContextMenuIcon.BranchPlus:
                ellipses = [E(-6, -7, 4, 4), E(-6, 3, 4, 4), E(2, -7, 4, 4)];
                lines = [L(-4, -3, -4, 3), L(4, 1, 4, 7), L(1, 4, 7, 4)];
                beziers = [B(-4, 2, -4, -2, 4, 1, 4, -3)];
                break;
            case NativeContextMenuIcon.Tag:
                lines = [L(-7, -5, 0, -5), L(0, -5, 7, 2), L(7, 2, 1, 7),
                    L(1, 7, -7, 0), L(-7, 0, -7, -5)];
                ellipses = [E(-4.5f, -2.5f, 2, 2)];
                break;
            case NativeContextMenuIcon.Open:
                lines = [L(-6, -4, -1, -4), L(-6, -4, -6, 6), L(-6, 6, 4, 6),
                    L(4, 6, 4, 1), L(-1, 1, 6, -6), L(1, -6, 6, -6), L(6, -6, 6, -1)];
                break;
            case NativeContextMenuIcon.Blame:
                lines = [L(-6, -5, -6, 5), L(-6, -5, -3, -5), L(-6, 0, -3, 0), L(-6, 5, -3, 5),
                    L(0, -5, 6, -5), L(0, 0, 6, 0), L(0, 5, 6, 5)];
                break;
            case NativeContextMenuIcon.Wrap:
                lines = [L(-6, -5, 6, -5), L(-6, -1, 3, -1), L(3, 5, -1, 5),
                    L(-1, 5, 1, 3), L(-1, 5, 1, 7), L(-6, 5, -4, 5)];
                arcs = [A(0, -1, 6, -90, 180)];
                break;
            case NativeContextMenuIcon.Whitespace:
                ellipses = [E(-4.5f, -.5f, 1, 1), E(-.5f, -.5f, 1, 1), E(3.5f, -.5f, 1, 1)];
                break;
            default:
                return false;
        }
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(strokeWidth), lines, ellipses, [], arcs, beziers);
    }

    // 分支重命名使用细线铅笔，克隆使用带 Git 节点的文件夹，冲突使用折角文件与感叹号。
    // 三种图形均固定在 16px 网格内，不随菜单行高或界面字号拉伸。
    internal static bool DrawRenameIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine[] lines =
        [
            new(centerX - 5 * u, centerY + 5 * u, centerX - 3 * u, centerY - 4 * u),
            new(centerX - 3 * u, centerY - 4 * u, centerX + 5 * u, centerY + 4 * u),
            new(centerX + 5 * u, centerY + 4 * u, centerX - 5 * u, centerY + 4 * u),
            new(centerX - 4 * u, centerY + 2 * u, centerX - 1 * u, centerY + 5 * u),
        ];
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f), lines, [], []);
    }

    internal static bool DrawCloneIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine[] lines =
        [
            new(centerX - 7 * u, centerY - 3 * u, centerX - 1 * u, centerY - 3 * u),
            new(centerX - 1 * u, centerY - 3 * u, centerX + 1 * u, centerY - 1 * u),
            new(centerX + 1 * u, centerY - 1 * u, centerX + 7 * u, centerY - 1 * u),
            new(centerX + 7 * u, centerY - 1 * u, centerX + 7 * u, centerY + 6 * u),
            new(centerX + 7 * u, centerY + 6 * u, centerX - 7 * u, centerY + 6 * u),
            new(centerX - 7 * u, centerY + 6 * u, centerX - 7 * u, centerY - 3 * u),
            new(centerX - 4 * u, centerY + 1 * u, centerX + 2 * u, centerY + 1 * u),
            new(centerX - 1 * u, centerY - 2 * u, centerX - 1 * u, centerY + 4 * u),
        ];
        NativeGdiPlusDrawing.StrokeEllipse[] ellipses =
        [
            new(centerX - 2.5f * u, centerY - 1.5f * u, 3 * u, 3 * u),
            new(centerX + 2.5f * u, centerY + 3.5f * u, 3 * u, 3 * u),
        ];
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.25f), lines, ellipses, []);
    }

    internal static bool DrawConflictIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine[] lines =
        [
            new(centerX - 6 * u, centerY - 7 * u, centerX + 2 * u, centerY - 7 * u),
            new(centerX + 2 * u, centerY - 7 * u, centerX + 6 * u, centerY - 3 * u),
            new(centerX + 6 * u, centerY - 3 * u, centerX + 6 * u, centerY + 7 * u),
            new(centerX + 6 * u, centerY + 7 * u, centerX - 6 * u, centerY + 7 * u),
            new(centerX - 6 * u, centerY + 7 * u, centerX - 6 * u, centerY - 7 * u),
            new(centerX + 2 * u, centerY - 7 * u, centerX + 2 * u, centerY - 3 * u),
            new(centerX + 2 * u, centerY - 3 * u, centerX + 6 * u, centerY - 3 * u),
            new(centerX, centerY - 1 * u, centerX, centerY + 3 * u),
        ];
        NativeGdiPlusDrawing.StrokeEllipse[] ellipses =
        [new(centerX - .75f * u, centerY + 4.5f * u, 1.5f * u, 1.5f * u)];
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.25f), lines, ellipses, []);
    }

    internal static bool DrawMenuCheckmark(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        float x = (rectangle.Left + rectangle.Right) / 2f;
        float y = (rectangle.Top + rectangle.Bottom) / 2f;
        float u = Scale(1f);
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f),
            [new(x - 5 * u, y, x - u, y + 4 * u), new(x - u, y + 4 * u, x + 6 * u, y - 4 * u)], [], []);
    }
}
