namespace Augit.App;

internal sealed partial class NativeConflictResolverDialog
{
    private int _headerHeight, _contentHeaderHeight, _columnHeaderHeight, _contentFooterHeight, _footerHeight;
    private int _buttonHeight, _acceptWidth, _cancelWidth, _saveWidth, _backWidth, _navigationWidth, _countWidth;
    private bool _wrapActions, _compactHeader, _splitColumnHeaders;
    private int _titleWidth, _fileTitleWidth;
    private string _fileCaption = string.Empty;

    private int MeasureUiText(string text, int minimum, int padding, bool medium = false)
    {
        nint dc = NativeMethods.GetDeviceContext(_handle);
        nint previous = NativeMethods.SelectObject(dc, medium ? NativeTheme.UiMediumFont : NativeTheme.UiFont);
        try
        {
            NativeMethods.Rectangle bounds = default;
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return Math.Max(S(minimum), bounds.Right + S(padding));
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(_handle, dc);
        }
    }

    private void MeasureLayout()
    {
        int line = NativeTheme.UiLineHeight;
        _buttonHeight = Math.Max(S(28), line + S(8));
        _headerHeight = Math.Max(S(HeaderHeight), line + S(18));
        _contentHeaderHeight = Math.Max(S(ContentHeaderHeight), _buttonHeight + S(14));
        _columnHeaderHeight = Math.Max(S(ColumnHeaderHeight), line + S(12));
        _footerHeight = Math.Max(S(FooterHeight), _buttonHeight + S(25));
        _acceptWidth = MeasureUiText(UiText.AcceptBothBlock, 112, 24);
        _cancelWidth = MeasureUiText(UiText.Cancel, 80, 24);
        _saveWidth = MeasureUiText(UiText.SaveAndResolve, 164, 24);
        _backWidth = MeasureUiText(UiText.BackToConflictList, 126, 24);
        _navigationWidth = Math.Max(MeasureUiText(UiText.PreviousChange, 78, 24), MeasureUiText(UiText.NextChange, 78, 24));
        _countWidth = MeasureUiText($"{_remainingBlockCount} 个未处理冲突", 0, 8);
        _titleWidth = MeasureUiText(UiText.ResolveConflictDialogTitle, 0, 0, medium: true);
        int available = S(_dialogWidth - 34);
        _fileCaption = $"{UiText.ResolveConflictDialogTitle} · {GetDisplayFileName(_document!.RelativePath)}";
        _fileTitleWidth = MeasureUiText(_fileCaption, 0, 8, medium: true);
        (int side, int middle, _) = CalculateColumnWidths(available - S(2));
        _splitColumnHeaders = MeasureUiText(_document.YoursLabel, 0, 12) > side
            || MeasureUiText($"{UiText.FinalResult} · 可编辑", 0, 12) > middle
            || MeasureUiText(_document.TheirsLabel, 0, 12) > side;
        if (_splitColumnHeaders) _columnHeaderHeight = Math.Max(_columnHeaderHeight, line * 2 + S(12));
        _wrapActions = _acceptWidth * 3 + _cancelWidth + _saveWidth + S(40) > S(_dialogWidth - 34);
        _contentFooterHeight = _buttonHeight * (_wrapActions ? 2 : 1) + S(_wrapActions ? 28 : 20);
        int desiredHeight = S(DialogHeight - HeaderHeight - ContentHeaderHeight - ColumnHeaderHeight - ContentFooterHeight - FooterHeight)
            + _headerHeight + _contentHeaderHeight + _columnHeaderHeight + _contentFooterHeight + _footerHeight;
        int height = NativeMethods.GetWindowRectangle(_layoutOwner, out NativeMethods.Rectangle owner)
            ? Math.Min(desiredHeight, Math.Max(S(300), owner.Bottom - owner.Top - S(40))) : desiredHeight;
        (int x, int y) = Center(_layoutOwner, S(_dialogWidth), height);
        _ = NativeMethods.SetWindowPosition(_handle, 0, x, y, S(_dialogWidth), height,
            NativeMethods.SetWindowPositionNoActivate | NativeMethods.SetWindowPositionNoZOrder);
        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, S(_dialogWidth) + 1, height + 1, S(18), S(18));
        if (region != 0 && NativeMethods.SetWindowRegion(_handle, region, true) == 0)
            _ = NativeMethods.DeleteObject(region);
    }

    private NativeMethods.Rectangle HeaderTitleBounds(int width) => new()
    {
        Left = _compactHeader ? S(26) + _titleWidth : S(17),
        Top = S(1) + (_compactHeader ? 0 : _headerHeight),
        Right = _compactHeader ? width - S(55) : Math.Max(S(17), CountBounds(width).Left - S(8)),
        Bottom = _compactHeader ? _headerHeight : _headerHeight + _contentHeaderHeight,
    };

    private void LayoutFileTitle(int width)
    {
        _compactHeader = _fileTitleWidth + _countWidth + 2 * (_navigationWidth + S(8)) > width - S(34);
        string caption = _compactHeader ? GetDisplayFileName(_document!.RelativePath) : _fileCaption;
        if (NativeMethods.GetWindowTextValue(_fileTitleLabel) != caption)
            _ = NativeMethods.SetWindowText(_fileTitleLabel, caption);
        NativeMethods.Rectangle title = HeaderTitleBounds(width);
        Move(_fileTitleLabel, title.Left, title.Top, Math.Max(0, title.Right - title.Left), title.Bottom - title.Top);
    }

    private NativeMethods.Rectangle CountBounds(int width) => new()
    {
        Left = _compactHeader ? S(17) : Math.Max(S(17), width - S(17) - 2 * (_navigationWidth + S(8)) - _countWidth),
        Top = S(1) + _headerHeight,
        Right = width - S(17) - 2 * (_navigationWidth + S(8)),
        Bottom = _headerHeight + _contentHeaderHeight,
    };

    private static (string Role, string Detail) SplitColumnTitle(string text)
    {
        int separator = text.IndexOf(" · ", StringComparison.Ordinal);
        if (separator >= 0) return (text[..separator], text[(separator + 3)..]);
        separator = text.IndexOf(' ');
        return separator < 0 ? (text, string.Empty) : (text[..separator], text[(separator + 1)..]);
    }

    private void DrawColumnTitle(nint dc, string text, NativeMethods.Rectangle bounds, uint color)
    {
        if (!_splitColumnHeaders)
        {
            DrawText(dc, text, bounds, color, NativeTheme.UiFont, centered: true);
            return;
        }
        (string role, string detail) = SplitColumnTitle(text);
        int line = NativeTheme.UiLineHeight;
        bounds.Top += (bounds.Bottom - bounds.Top - 2 * line) / 2;
        bounds.Bottom = bounds.Top + line;
        DrawText(dc, role, bounds, color, NativeTheme.UiFont, centered: true);
        bounds.Top += line;
        bounds.Bottom += line;
        DrawText(dc, detail, bounds, color, NativeTheme.UiFont, centered: true);
    }

    internal nint FileTitleHandleForTest => _fileTitleLabel;
    internal bool CompactHeaderForTest => _compactHeader;
    internal bool SplitColumnHeadersForTest => _splitColumnHeaders;
    internal NativeMethods.Rectangle CountBoundsForTest => CountBounds(S(_dialogWidth));
}
