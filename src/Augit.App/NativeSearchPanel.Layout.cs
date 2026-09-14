namespace Augit.App;

internal sealed partial class NativeSearchPanel
{
    private static int S(int value) => NativeTheme.Scale(value);
    private static int ResultRowHeight => NativeTheme.ContentHeight(32, 8);
    private static int SearchFieldHeight => NativeTheme.ContentHeight(31, 8);
    private (string Family, double Size, int Dpi, bool Dark)? _appearanceKey;
    private (int Width, string Family, double Size, int Dpi, string Notice)? _measurementKey;
    private SearchLayout _measurement;

    private readonly record struct SearchLayout(int HeaderHeight, int FieldTop, int OptionsTop,
        int OptionsLeft, int IgnoredTop, int IgnoredLeft, int IgnoredWidth, int NoticeHeight)
    {
        internal int ListTop => FieldTop + SearchFieldHeight + S(13);
    }

    internal int ResultRowHeightForTest => (int)NativeMethods.SendMessage(_resultList, 0x01A1, 0, 0);
    internal nint HoveredOptionForTest => _optionStates?.Hovered ?? 0;
    internal bool OptionHasTooltipForTest(nint option) => _toolTip?.ContainsForTest(option) == true;

    // 高度计算显式接收目标宽度，避免换行使用上一次窗口宽度而引起浮层二次跳动。
    internal int PreferredHeight(int width)
    {
        SearchLayout layout = MeasureLayout(width);
        if (_results.Count == 0 && !HasNotice) return layout.FieldTop + SearchFieldHeight + S(8);
        int notice = HasNotice ? Math.Max(S(32), Math.Min(layout.NoticeHeight, NativeTheme.UiLineHeight * 3) + S(12)) : S(5);
        return layout.ListTop + Math.Min(_results.Count, 10) * ResultRowHeight + notice;
    }

    private SearchLayout MeasureLayout(int width)
    {
        string notice = HasNotice ? NoticeTextForTest : string.Empty;
        var key = (width, NativeTheme.UiFontFamilyForTest, NativeTheme.UiFontSizeForTest, NativeTheme.ActiveDpiForTest, notice);
        if (_measurementKey == key) return _measurement;
        int line = NativeTheme.UiLineHeight, inset = S(10), gap = S(8);
        int content = Math.Max(1, width - 2 * inset);
        int header = Math.Max(S(41), line + S(20));
        int optionHeight = NativeTheme.ContentHeight(28, 8);
        int compactWidth = S(28) * 3 + S(4) * 2;
        int ignoredWidth = Math.Min(content, MeasureText(UiText.IncludeIgnoredFiles).Width + S(22));
        int optionsWidth = compactWidth + gap + ignoredWidth;
        int titleWidth = MeasureText(_mode == WorkspaceSearchMode.Text ? UiText.WorkspaceTextSearch : UiText.QuickOpenFiles).Width;
        int optionsTop = (header - optionHeight) / 2, optionsLeft = width - inset - optionsWidth;
        int ignoredLeft = optionsLeft + compactWidth + gap, ignoredTop = optionsTop, fieldTop = header;
        if (_mode == WorkspaceSearchMode.Text && titleWidth + gap + optionsWidth > content)
        {
            optionsLeft = inset;
            optionsTop = header;
            ignoredLeft = inset + compactWidth + gap;
            ignoredTop = optionsTop;
            if (optionsWidth > content)
            {
                ignoredLeft = inset;
                ignoredTop += optionHeight + gap;
            }
            fieldTop = ignoredTop + optionHeight + gap;
        }
        if (_mode == WorkspaceSearchMode.FileNames && titleWidth + gap + MeasureText("Ctrl+P").Width > content)
            fieldTop += line + S(4);
        // 滚动条预算预先扣除，显示长错误时不触发宽度反复变化。
        int noticeWidth = Math.Max(1, content - S(22));
        _measurement = new(header, fieldTop, optionsTop, optionsLeft, ignoredTop, ignoredLeft, ignoredWidth,
            notice.Length == 0 ? 0 : Math.Max(line, MeasureText(notice, noticeWidth).Height));
        _measurementKey = key;
        return _measurement;
    }

    private (int Width, int Height) MeasureText(string text, int width = 0)
    {
        nint dc = NativeMethods.GetDeviceContext(Handle);
        nint previous = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        try
        {
            NativeMethods.Rectangle bounds = new() { Right = width };
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds, NativeMethods.DrawTextCalculateRectangle
                | NativeMethods.DrawTextNoPrefix | (width > 0 ? NativeMethods.DrawTextWordBreak : NativeMethods.DrawTextSingleLine));
            return (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(Handle, dc); }
    }

    private void Layout()
    {
        if (_searchEdit == 0 || Handle == 0 || !NativeMethods.GetClientRectangle(Handle, out var client)) return;
        bool dark = _isDark();
        var appearance = (NativeTheme.UiFontFamilyForTest, NativeTheme.UiFontSizeForTest, NativeTheme.ActiveDpiForTest, dark);
        if (_appearanceKey != appearance)
        {
            foreach (nint control in new[] { _titleLabel, _shortcutLabel, _searchEdit, _matchCaseButton,
                _wholeWordButton, _regularExpressionButton, _includeIgnoredButton, _resultList, _noticeLabel })
                NativeTheme.ApplyToControl(control, dark);
            _ = NativeMethods.SendMessage(_resultList, NativeMethods.ListBoxSetItemHeight, 0, ResultRowHeight);
            if (_controlBrush != 0) _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
            _toolTip?.ApplyAppearance(dark);
            _appearanceKey = appearance;
        }
        int width = client.Right, height = client.Bottom, inset = S(10), line = NativeTheme.UiLineHeight;
        SearchLayout layout = MeasureLayout(width);
        int shortcutWidth = MeasureText("Ctrl+P").Width;
        bool shortcutBelow = _mode == WorkspaceSearchMode.FileNames && layout.FieldTop > layout.HeaderHeight;
        int titleRight = _mode == WorkspaceSearchMode.Text
            ? layout.OptionsTop < layout.HeaderHeight ? layout.OptionsLeft - S(8) : width - inset
            : shortcutBelow ? width - inset : width - inset - shortcutWidth - S(8);
        Move(_titleLabel, inset, (layout.HeaderHeight - line) / 2, titleRight - inset, line);
        Move(_shortcutLabel, width - inset - shortcutWidth,
            shortcutBelow ? layout.HeaderHeight : (layout.HeaderHeight - line) / 2, shortcutWidth, line);
        _ = NativeMethods.ShowWindow(_shortcutLabel, _mode == WorkspaceSearchMode.FileNames ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_closeButton, NativeMethods.ShowHide);
        int optionHeight = NativeTheme.ContentHeight(28, 8);
        Move(_matchCaseButton, layout.OptionsLeft, layout.OptionsTop, S(28), optionHeight);
        Move(_wholeWordButton, layout.OptionsLeft + S(32), layout.OptionsTop, S(28), optionHeight);
        Move(_regularExpressionButton, layout.OptionsLeft + S(64), layout.OptionsTop, S(28), optionHeight);
        Move(_includeIgnoredButton, layout.IgnoredLeft, layout.IgnoredTop, layout.IgnoredWidth, optionHeight);
        int editHeight = line + S(2);
        Move(_searchEdit, inset + S(10), layout.FieldTop + (SearchFieldHeight - editHeight) / 2,
            width - inset * 2 - S(20), editHeight);
        bool hasResults = _results.Count > 0;
        bool hasNotice = HasNotice;
        int available = Math.Max(0, height - layout.ListTop);
        int noticeArea = hasNotice ? Math.Min(Math.Max(S(32), Math.Min(layout.NoticeHeight, line * 3) + S(12)),
            Math.Max(0, available - (hasResults ? ResultRowHeight : 0))) : S(5);
        int listHeight = hasResults ? Math.Min(_results.Count * ResultRowHeight, Math.Max(0, available - noticeArea)) : 0;
        _ = NativeMethods.ShowWindow(_resultList, hasResults ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_noticeLabel, hasNotice ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        Move(_resultList, inset, layout.ListTop, width - inset * 2, listHeight);
        Move(_noticeLabel, inset, layout.ListTop + listHeight + S(6), width - inset * 2, Math.Max(0, noticeArea - S(12)));
        _ = NativeMethods.ShowScrollBar(_noticeLabel, 1, layout.NoticeHeight > noticeArea - S(12));
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }
}
