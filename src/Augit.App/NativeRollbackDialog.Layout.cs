namespace Augit.App;

internal sealed partial class NativeRollbackDialog
{
    private NativeDialogBody? _bodyView;
    private int _headerExtent, _footerExtent, _buttonExtent, _cancelWidth, _confirmWidth;
    private int _warningHeight, _titleHeight, _detailHeight, _recycleHeight, _noticeHeight, _compareTop;
    private int _contentHeight;
    private (int Width, int Line, string Notice)? _bodyMeasureKey;
    private string _noticeText = string.Empty;

    internal nint BodyForTest => _bodyView?.Handle ?? 0;
    internal nint NoticeForTest => _noticeLabel;
    internal int BodyMeasureCountForTest { get; private set; }
    internal int WarningHeightForTest => _warningHeight;

    // 保留比较工具栏、两行文件信息及四行等宽正文；受限时只滚动窗口正文。
    private static int MinimumComparisonHeight => Math.Max(S(214),
        Math.Max(S(39), NativeTheme.UiLineHeight + S(12)) + NativeDiffFileHeader.Height(false) + S(96));

    private void ResizeToTypography()
    {
        int line = NativeTheme.UiLineHeight;
        _headerExtent = Math.Max(S(HeaderHeight), line + S(16));
        _buttonExtent = Math.Max(S(30), line + S(10));
        _footerExtent = S(FooterHeight) + _buttonExtent - S(30);
        _cancelWidth = Math.Max(S(78), NativeDialogBody.Measure(_handle, UiText.Cancel).Width + S(26));
        _confirmWidth = Math.Max(S(116), NativeDialogBody.Measure(_handle, UiText.ConfirmRollbackAction).Width + S(26));
        if (!NativeMethods.GetWindowRectangle(_owner, out var owner)) return;
        int width = Math.Min(Math.Max(S(DialogWidth), _cancelWidth + _confirmWidth + S(42)),
            Math.Max(1, owner.Right - owner.Left - S(90)));
        int height = Math.Min(Math.Max(S(DialogHeight), _headerExtent + MeasureBody(width - S(2)) + _footerExtent + S(1)),
            Math.Max(1, owner.Bottom - owner.Top - S(40)));
        _ = NativeMethods.SetWindowPosition(_handle, 0,
            owner.Left + (owner.Right - owner.Left - width) / 2,
            owner.Top + (owner.Bottom - owner.Top - height) / 2, width, height,
            NativeMethods.SetWindowPositionNoActivate | NativeMethods.SetWindowPositionNoZOrder);
        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, width + 1, height + 1, S(18), S(18));
        if (region != 0 && NativeMethods.SetWindowRegion(_handle, region, true) == 0) _ = NativeMethods.DeleteObject(region);
        Layout();
    }

    private int MeasureBody(int width)
    {
        int line = NativeTheme.UiLineHeight;
        var key = (width, line, _noticeText);
        if (_bodyMeasureKey == key) return _contentHeight;
        _bodyMeasureKey = key;
        BodyMeasureCountForTest++;
        int textWidth = Math.Max(1, width - S(58));
        _titleHeight = Math.Max(line, NativeDialogBody.Measure(_handle, UiText.RollbackWarningTitle, textWidth, true).Height);
        _detailHeight = Math.Max(line, NativeDialogBody.Measure(_handle, UiText.RollbackWarningDetail, textWidth).Height);
        _recycleHeight = _recycle ? Math.Max(line, NativeDialogBody.Measure(_handle, UiText.RollbackRecycleNotice, textWidth).Height) + S(4) : 0;
        _warningHeight = Math.Max(S(81), _titleHeight + _detailHeight + _recycleHeight + S(16));
        _noticeHeight = _noticeText.Length == 0 ? 0 : Math.Max(line, NativeDialogBody.Measure(_handle, _noticeText, Math.Max(1, width - S(34))).Height);
        _compareTop = S(15) + _warningHeight + S(14);
        _contentHeight = _compareTop + MinimumComparisonHeight + S(8) + (_noticeHeight == 0 ? 0 : _noticeHeight + S(8));
        return _contentHeight;
    }

    private void LayoutBody(int width, int height, int offset)
    {
        int noticeSpace = _noticeHeight == 0 ? 0 : _noticeHeight + S(8);
        int compareHeight = Math.Max(MinimumComparisonHeight, height - _compareTop - S(8) - noticeSpace);
        _comparison?.SetBounds(S(17), _compareTop - offset, Math.Max(0, width - S(34)), compareHeight);
        // 比较的滚轮保持其自身正文语义；焦点进入被裁切的工具或文本时才滚入视口。
        foreach (nint control in _comparison?.FocusTargets ?? [])
            if (control != 0) _bodyView?.Register(control);
        Move(_noticeLabel, S(17), _compareTop + compareHeight + S(8) - offset,
            Math.Max(0, width - S(34)), _noticeHeight);
        _ = NativeMethods.ShowWindow(_noticeLabel, _noticeHeight == 0 ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
    }

    private void PaintBody(nint dc)
    {
        if (dc == 0 || _bodyView is null || !NativeMethods.GetClientRectangle(_bodyView.Handle, out var client)) return;
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        Fill(dc, client, palette.Panel);
        int top = S(15) - _bodyView.Offset;
        NativeTheme.FillRounded(dc,
            new() { Left = S(17), Top = top, Right = client.Right - S(17), Bottom = top + _warningHeight },
            _dark ? Rgb(75, 45, 45) : Rgb(247, 215, 215), S(10));
        int textTop = top + S(6);
        PaintWarningText(dc, UiText.RollbackWarningTitle, client.Right, textTop, _titleHeight, palette.Text, true);
        textTop += _titleHeight + S(4);
        PaintWarningText(dc, UiText.RollbackWarningDetail, client.Right, textTop, _detailHeight, palette.Muted);
        if (_recycle)
            PaintWarningText(dc, UiText.RollbackRecycleNotice, client.Right, textTop + _detailHeight + S(4), _recycleHeight - S(4), palette.Muted);
    }

    private static void PaintWarningText(nint dc, string text, int width, int top, int height, uint color, bool medium = false)
    {
        NativeMethods.Rectangle bounds = new() { Left = S(29), Top = top, Right = width - S(29), Bottom = top + height };
        nint previous = NativeMethods.SelectObject(dc, medium ? NativeTheme.UiMediumFont : NativeTheme.UiFont);
        _ = NativeMethods.SetTextColor(dc, color);
        _ = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds, NativeMethods.DrawTextWordBreak | NativeMethods.DrawTextNoPrefix);
        _ = NativeMethods.SelectObject(dc, previous);
    }
}
