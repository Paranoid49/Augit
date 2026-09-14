namespace Augit.App;

internal sealed partial class NativeDocumentView
{
    internal bool HandleToolbarShortcut(NativeMethods.Message message)
    {
        nint focus = NativeMethods.GetFocus();
        if (message.MessageId != NativeMethods.WindowMessageKeyDown
            || message.WordParameter != NativeMethods.VirtualKeyEnter || message.Window != focus
            || !IsFocusable(focus)
            || (!_toolbarControls.Contains(focus) && focus != _imageZoomOutButton
                && focus != _imageZoomInButton && focus != _imageFitButton
                && focus != _blameCloseButton)) return false;
        _ = NativeMethods.SendMessage(focus, 0x00F5, 0, 0);
        return true;
    }

    private int MeasureToolbarText(string text, nint font)
    {
        nint dc = NativeMethods.GetDeviceContext(Handle);
        if (dc == 0) return 0;
        nint previous = NativeMethods.SelectObject(dc, font);
        try
        {
            NativeMethods.Rectangle rectangle = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref rectangle,
                NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextCalculateRectangle);
            return rectangle.Right - rectangle.Left;
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(Handle, dc);
        }
    }

    private void LayoutImageToolbar(int width)
    {
        int button = FindButtonSize;
        int gap = NativeTheme.Scale(4);
        int left = NativeTheme.Scale(9);
        int right = NativeTheme.Scale(8);
        int labelHeight = Math.Max(button, NativeTheme.UiLineHeight);
        int zoomWidth = Math.Max(NativeTheme.Scale(48), MeasureToolbarText("800%", NativeTheme.UiMediumFont) + gap * 2);
        int measuredSizeWidth = MeasureToolbarText(NativeMethods.GetWindowTextValue(_imageSizeLabel), NativeTheme.UiFont) + gap * 2;
        // 先为四个左侧控件和它们之间的间距留出空间，剩余宽度再分给百分比与信息。
        // 不能使用排布后的 left 作为下限，否则窄正文会把右侧信息标签推到客户区外。
        int fixedActionWidth = left + button * 3 + gap * 4;
        int flexibleWidth = Math.Max(0, width - right - fixedActionWidth);
        zoomWidth = Math.Min(zoomWidth, flexibleWidth);
        int sizeWidth = Math.Min(measuredSizeWidth, Math.Max(0, flexibleWidth - zoomWidth));
        // PyCharm 图片工具栏：缩放动作靠左，尺寸/类型/大小信息靠右。
        left = Math.Max(left, NativeTheme.Scale(9));
        int actionEnd = left + button + gap + zoomWidth + gap + button + gap + button;
        int infoLeft = Math.Max(actionEnd + gap, width - right - sizeWidth);
        foreach ((nint control, int controlWidth, int controlHeight) in new[]
        {
            (_imageZoomOutButton, button, button),
            (_imageZoomLabel, zoomWidth, labelHeight),
            (_imageZoomInButton, button, button),
            (_imageFitButton, button, button),
        })
        {
            Move(control, left, (ToolbarHeight - controlHeight) / 2, controlWidth, controlHeight);
            left += controlWidth + gap;
        }
        // 没有剩余空间时将信息标签收缩为零宽；悬停说明仍保留，动作控件不会越界。
        Move(_imageSizeLabel, sizeWidth == 0 ? width : infoLeft, (ToolbarHeight - labelHeight) / 2, sizeWidth, labelHeight);
    }

    private int MeasureDocumentNotice(nint control, int width, int minimumHeight)
    {
        nint dc = NativeMethods.GetDeviceContext(Handle);
        if (dc == 0) return NativeTheme.ContentHeight(minimumHeight, 16);
        nint previous = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        try
        {
            NativeMethods.Rectangle rectangle = new() { Right = Math.Max(1, width - NativeTheme.Scale(16)) };
            string text = NativeMethods.GetWindowTextValue(control);
            _ = NativeMethods.DrawText(dc, text, text.Length, ref rectangle,
                NativeMethods.DrawTextWordBreak | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextCalculateRectangle);
            return Math.Max(NativeTheme.Scale(minimumHeight), rectangle.Bottom + NativeTheme.Scale(16));
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(Handle, dc);
        }
    }

    private void DrawTargetNotice(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme));
        FillJsonRectangle(item, item.ItemRectangle, palette.Border);
        NativeMethods.Rectangle inside = item.ItemRectangle;
        inside.Bottom--;
        inside.Right = Math.Max(inside.Left, inside.Right);
        inside.Bottom = Math.Max(inside.Top, inside.Bottom);
        FillJsonRectangle(item, inside, palette.PanelMuted);
        int padding = NativeTheme.Scale(8);
        inside.Left += padding;
        inside.Right = Math.Max(inside.Left, inside.Right - padding);
        inside.Top += padding;
        inside.Bottom = Math.Max(inside.Top, inside.Bottom - padding);
        nint previous = NativeMethods.SelectObject(item.DeviceContext, NativeTheme.UiFont);
        _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(item.DeviceContext, palette.Text);
        string text = NativeMethods.GetWindowTextValue(item.Control);
        _ = NativeMethods.DrawText(item.DeviceContext, text, text.Length, ref inside,
            NativeMethods.DrawTextWordBreak | NativeMethods.DrawTextNoPrefix);
        _ = NativeMethods.SelectObject(item.DeviceContext, previous);
    }
}
