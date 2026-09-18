namespace Augit.Shell;

/// <summary>
/// 无 caption 外壳窗口的边框与命中测试规则。
///
/// 视觉稿在标题栏里自绘了窗口按钮，所以窗口不能再带 Win32 caption：否则右上角会同时出现
/// 原生按钮与自绘按钮（同一处两套关闭按钮，实测截图可见）。
///
/// 去掉 caption 后，拖动、双击最大化与边缘缩放必须由窗口自己判定，规则如下：
/// <list type="bullet">
/// <item>边缘 8 逻辑像素按生效 DPI 换算后返回对应的缩放码，与原生边框的抓取宽度一致；</item>
/// <item>标题栏**不**返回 <c>HTCAPTION</c>：视觉稿的标题栏左侧是主菜单、工作区、分支等真实控件，
/// 一旦整条返回 caption，系统会在按下时直接进入移动循环，这些控件永远收不到点击（点了没反应）；</item>
/// <item>拖动改由网页层在标题栏空白处发起（网页知道哪个元素被按下），窗口收到后仍用
/// <c>WM_NCLBUTTONDOWN/HTCAPTION</c> 交给系统，因此拖动、贴边、双击最大化、拖拽还原全部保持原生行为；</item>
/// <item>最大化时不提供边缘缩放：此时窗口贴屏幕边缘，否则屏幕边界会出现缩放指针并且一拖就变成缩放。</item>
/// </list>
/// </summary>
internal static class ShellWindowFrame
{
    internal const int HtClient = 1;

    internal const int HtCaption = 2;

    internal const int HtLeft = 10;

    internal const int HtRight = 11;

    internal const int HtTop = 12;

    internal const int HtTopLeft = 13;

    internal const int HtTopRight = 14;

    internal const int HtBottom = 15;

    internal const int HtBottomLeft = 16;

    internal const int HtBottomRight = 17;

    /// <summary>边缘缩放抓取宽度，逻辑像素（与原生可缩放边框的手感一致）。</summary>
    internal const int ResizeBorderLogical = 8;

    /// <summary>高缩放下的下限：抓取区不能小于 4 物理像素，否则边界几乎点不到。</summary>
    internal const int MinimumResizeBorder = 4;

    /// <summary>缩放抓取宽度换算成物理像素。</summary>
    internal static int ResolveResizeBorder(int effectiveDpi)
        => Math.Max(
            MinimumResizeBorder,
            ShellWindowSizing.ScaleToPhysical(ResizeBorderLogical, effectiveDpi));

    /// <summary>
    /// 命中测试。坐标与 <paramref name="window"/> 都是物理像素、屏幕坐标；
    /// <paramref name="maximized"/> 为真时不返回缩放码。
    /// </summary>
    internal static int HitTest(int x, int y, WindowFrameRect window, int effectiveDpi, bool maximized)
    {
        if (maximized || window.Right <= window.Left || window.Bottom <= window.Top)
        {
            return HtClient;
        }

        int border = ResolveResizeBorder(effectiveDpi);
        bool onLeft = x < window.Left + border;
        bool onRight = x >= window.Right - border;
        bool onTop = y < window.Top + border;
        bool onBottom = y >= window.Bottom - border;

        if (onTop && onLeft)
        {
            return HtTopLeft;
        }

        if (onTop && onRight)
        {
            return HtTopRight;
        }

        if (onBottom && onLeft)
        {
            return HtBottomLeft;
        }

        if (onBottom && onRight)
        {
            return HtBottomRight;
        }

        if (onLeft)
        {
            return HtLeft;
        }

        if (onRight)
        {
            return HtRight;
        }

        if (onTop)
        {
            return HtTop;
        }

        return onBottom ? HtBottom : HtClient;
    }

    /// <summary>
    /// 最大化时的客户区矩形。
    ///
    /// 可缩放窗口被系统最大化时，窗口矩形会向工作区外扩一圈边框宽度，让框内的客户区正好等于
    /// 工作区。窗口没有 caption、客户区铺满整个窗口后，这圈外扩就变成把界面推到屏幕外，
    /// 右侧与底部的标题栏按钮被裁掉；因此最大化时把客户区显式钉回工作区。
    /// </summary>
    internal static WindowFrameRect MaximizedClientRect(WindowFrameRect monitor, WindowFrameRect workArea)
        => workArea.Right > workArea.Left && workArea.Bottom > workArea.Top ? workArea : monitor;

    /// <summary>
    /// 网页层报告的缩放边 → 命中码。
    ///
    /// 边缘缩放不能只靠窗口的 WM_NCHITTEST：WebView2 的子窗口铺满客户区，鼠标落在边缘时
    /// 系统把命中测试交给子窗口（HTCLIENT），窗口的判定根本不会被问到，边框就拉不动
    /// （实测：在左边界内 3 像素处真实按下并左移 80 像素，窗口尺寸完全不变）。
    /// 因此与拖动同路：网页知道按在哪里，由它回报边，窗口再用
    /// WM_NCLBUTTONDOWN + 对应的 HT 码交给系统，缩放手感仍然是原生的。
    /// </summary>
    internal static int ResizeCode(string? edge)
        => edge?.ToLowerInvariant() switch
        {
            "left" => HtLeft,
            "right" => HtRight,
            "top" => HtTop,
            "bottom" => HtBottom,
            "topleft" => HtTopLeft,
            "topright" => HtTopRight,
            "bottomleft" => HtBottomLeft,
            "bottomright" => HtBottomRight,
            _ => 0,
        };

    /// <summary>
    /// 标题栏空白处的第二次按下是否算双击。
    ///
    /// 窗口没有 caption 时输入层不会跟踪"标题栏双击"，系统的移动循环也拿不到双击消息，
    /// 双击最大化必须自己判断。判定沿用系统口径：两次按下在双击时间内、且位移不超过
    /// 双击矩形的半宽半高（超过就是两次独立拖动，不是双击）。
    /// </summary>
    internal static bool IsCaptionDoubleClick(
        long previousTick,
        int previousX,
        int previousY,
        long currentTick,
        int currentX,
        int currentY,
        int doubleClickTime,
        int toleranceX,
        int toleranceY)
    {
        if (previousTick <= 0)
        {
            return false;
        }

        long elapsed = currentTick - previousTick;
        if (elapsed < 0 || elapsed > doubleClickTime)
        {
            return false;
        }

        return Math.Abs(currentX - previousX) <= Math.Max(1, toleranceX)
            && Math.Abs(currentY - previousY) <= Math.Max(1, toleranceY);
    }
}

/// <summary>窗口或显示器矩形的物理像素边界。</summary>
internal readonly record struct WindowFrameRect(int Left, int Top, int Right, int Bottom);
