namespace Augit.Shell;

/// <summary>模态裁切只接受主会话内的有限矩形，屏幕位置按最近显示器工作区夹边。</summary>
internal static class ShellModalGeometry
{
    internal static WindowFrameRect Clip(double x, double y, double width, double height, int viewportWidth, int viewportHeight, double scale)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            || !double.IsFinite(scale) || scale <= 0 || width <= 0 || height <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            throw new ArgumentException("模态窗口矩形无效。");
        }

        int left = (int)Math.Clamp(Math.Round(x * scale), 0, viewportWidth - 1);
        int top = (int)Math.Clamp(Math.Round(y * scale), 0, viewportHeight - 1);
        int right = (int)Math.Clamp(Math.Round((x + width) * scale), left + 1, viewportWidth);
        int bottom = (int)Math.Clamp(Math.Round((y + height) * scale), top + 1, viewportHeight);
        return new(left, top, right, bottom);
    }

    internal static WindowFrameRect Fit(WindowFrameRect rect, WindowFrameRect work)
    {
        int width = Math.Min(rect.Right - rect.Left, work.Right - work.Left);
        int height = Math.Min(rect.Bottom - rect.Top, work.Bottom - work.Top);
        int left = Math.Clamp(rect.Left, work.Left, work.Right - width);
        int top = Math.Clamp(rect.Top, work.Top, work.Bottom - height);
        return new(left, top, left + width, top + height);
    }

    /// <summary>
    /// 把独立子窗（例如 Settings）以 Owner 为中心摆放后，整体夹到 Owner 所在显示器的工作区。
    /// 子窗比工作区还大时由 Fit 收缩到工作区；不改变 Owner 的摆法。
    /// </summary>
    internal static WindowFrameRect CenterAndFit(WindowFrameRect owner, int width, int height, WindowFrameRect work)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("子窗口尺寸无效。");
        }

        int left = owner.Left + ((owner.Right - owner.Left) - width) / 2;
        int top = owner.Top + ((owner.Bottom - owner.Top) - height) / 2;
        return Fit(new WindowFrameRect(left, top, left + width, top + height), work);
    }

    internal static WindowFrameRect Rescale(WindowFrameRect rect, double previous, double current)
    {
        if (!double.IsFinite(previous) || !double.IsFinite(current) || previous <= 0 || current <= 0)
            throw new ArgumentException("模态窗口缩放无效。");
        int Edge(int value) => checked((int)Math.Round(value * current / previous));
        return new(Edge(rect.Left), Edge(rect.Top), Edge(rect.Right), Edge(rect.Bottom));
    }
}
