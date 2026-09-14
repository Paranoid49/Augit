namespace Augit.App;

internal sealed partial class NativeWorktreeManagerDialog
{
    private NativeDialogBody? _bodyView;
    private NativeDialogActionButtons? _buttons;
    private int _headerHeight, _footerHeight, _toolbarHeight, _fieldHeight, _buttonHeight, _labelWidth;
    private int _closeWidth, _cancelWidth, _sidebarBoundary, _layoutWidth, _bodyContentHeight;
    private int _noticeTop, _noticeHeight;
    private bool _layoutDirty = true;
    private string _notice = string.Empty;
    private readonly NativeMethods.Rectangle[] _rows = new NativeMethods.Rectangle[3];
    private readonly NativeMethods.Rectangle[] _inputFrames = new NativeMethods.Rectangle[3];
    private readonly Dictionary<nint, NativeMethods.Rectangle> _actionBounds = [];

    internal nint HandleForTest => _handle;
    internal nint DetailPanelForTest => _bodyView?.Handle ?? 0;
    internal nint NoticeForTest => _noticeLabel;
    internal nint HoveredForTest => _buttons?.Hovered ?? 0;
    internal IReadOnlyList<nint> LabelsForTest => _creatingNew ? _createLabels : _detailLabels;
    internal IReadOnlyList<nint> ValuesForTest => [_pathValue, _statusLabel, _terminalLabel];

    private int MeasureButton(nint button, int minimum) => Math.Max(S(minimum),
        NativeDialogBody.Measure(_handle, NativeMethods.GetWindowTextValue(button)).Width + S(24));

    private void MeasureLayout()
    {
        int line = NativeTheme.UiLineHeight;
        _headerHeight = Math.Max(S(HeaderHeight), line + S(16));
        _fieldHeight = Math.Max(S(30), line + S(10));
        _buttonHeight = Math.Max(S(31), line + S(10));
        _footerHeight = Math.Max(S(FooterHeight), _buttonHeight + S(22));
        _toolbarHeight = Math.Max(S(61), line + S(28));
        _labelWidth = Math.Max(S(110), _detailLabels.Concat(_createLabels)
            .Max(label => NativeDialogBody.Measure(_handle, NativeMethods.GetWindowTextValue(label)).Width));
        _closeWidth = MeasureButton(_closeButton, 54);
        _cancelWidth = MeasureButton(_cancelOperationButton, 100);
        _ = NativeMethods.SendMessage(_worktreeList, NativeMethods.ListBoxSetItemHeight, 0, Math.Max(S(27), line + S(8)));
        _layoutDirty = true;
    }

    private void ResizeToContent()
    {
        if (!NativeMethods.GetWindowRectangle(_owner, out var owner)) return;
        int width = Math.Min(S(DialogWidth), Math.Max(1, owner.Right - owner.Left - S(90)));
        int body = S(190) + Math.Max(0, NativeTheme.UiLineHeight - S(31))
            + 3 * Math.Max(0, NativeTheme.UiLineHeight - S(24)) + _buttonHeight - S(31);
        int natural = S(DialogHeight) + _headerHeight - S(HeaderHeight) + _footerHeight - S(FooterHeight)
            + _toolbarHeight - S(61) + body - S(190);
        int height = Math.Min(natural, Math.Max(1, owner.Bottom - owner.Top - S(40)));
        _ = NativeMethods.SetWindowPosition(_handle, 0, owner.Left + (owner.Right - owner.Left - width) / 2,
            owner.Top + (owner.Bottom - owner.Top - height) / 2, width, height,
            NativeMethods.SetWindowPositionNoActivate | NativeMethods.SetWindowPositionNoZOrder);
        Layout();
    }

    private void Layout()
    {
        if (_bodyView is null || _headerHeight == 0 || !NativeMethods.GetClientRectangle(_handle, out var client)) return;
        int sidebar = Math.Min(S(SidebarWidth), Math.Max(S(180), client.Right / 3));
        _sidebarBoundary = S(16) + sidebar;
        int contentTop = _headerHeight + _toolbarHeight;
        int footerTop = client.Bottom - _footerHeight;
        int bodyHeight = Math.Max(0, footerTop - contentTop - S(16));
        Move(_addButton, S(16), _headerHeight + (_toolbarHeight - S(28)) / 2, S(28), S(28));
        Move(_deleteToolbarButton, S(50), _headerHeight + (_toolbarHeight - S(28)) / 2, S(28), S(28));
        Move(_refreshButton, S(84), _headerHeight + (_toolbarHeight - S(28)) / 2, S(28), S(28));
        Move(_worktreeList, S(16), contentTop, sidebar - S(8), bodyHeight);
        Move(_bodyView.Handle, _sidebarBoundary + S(1), contentTop,
            client.Right - _sidebarBoundary - S(17), bodyHeight);
        int actionTop = footerTop + (_footerHeight - _buttonHeight) / 2;
        Move(_closeButton, client.Right - S(17) - _closeWidth, actionTop, _closeWidth, _buttonHeight);
        Move(_cancelOperationButton, client.Right - S(25) - _closeWidth - _cancelWidth, actionTop, _cancelWidth, _buttonHeight);
        Move(_headerCloseButton, client.Right - S(45), (_headerHeight - S(31)) / 2, S(32), S(31));
        _bodyView.Relayout();
        _ = NativeMethods.InvalidateRectangle(_handle, 0, false);
    }

    private void RelayoutDetails()
    {
        _layoutDirty = true;
        if (_headerHeight == 0) return;
        foreach (nint control in new[] { _detailTitle, _pathValue, _statusLabel, _terminalLabel, _noticeLabel })
            _toolTip?.Update(control, NativeMethods.GetWindowTextValue(control));
        _bodyView?.Relayout();
    }

    private int MeasureBody(int width)
    {
        if (_headerHeight == 0) return 0;
        if (!_layoutDirty && _layoutWidth == width) return _bodyContentHeight;
        _layoutDirty = false;
        _layoutWidth = width;
        int line = NativeTheme.UiLineHeight;
        int left = S(18), right = Math.Max(left + 1, width - S(18));
        int valueLeft = left + _labelWidth + S(10);
        int valueWidth = Math.Max(1, right - valueLeft);
        int top = S(10) + Math.Max(S(31), line) + S(8);
        nint[] values = [_pathValue, _statusLabel, _terminalLabel];
        for (int index = 0; index < 3; index++)
        {
            // 详情值使用单行省略号；不按 DrawText 的自动换行高度预留空白。
            int height = _creatingNew ? _fieldHeight : Math.Max(S(24), line);
            _rows[index] = new() { Left = valueLeft, Right = right, Top = top, Bottom = top + height };
            top += height + S(_creatingNew ? 13 : 6);
        }
        top += S(_creatingNew ? -1 : 6);
        int x = left;
        _actionBounds.Clear();
        nint[] actions = _creatingNew ? [_createButton] : [_openButton, _newButton, _removeButton];
        foreach (nint action in actions)
        {
            int minimum = action == _openButton ? 96 : action == _newButton ? 132 : action == _createButton ? 124 : 78;
            int buttonWidth = MeasureButton(action, minimum);
            if (x != left && x + buttonWidth > right) { x = left; top += _buttonHeight + S(8); }
            _actionBounds[action] = new() { Left = x, Right = x + buttonWidth, Top = top, Bottom = top + _buttonHeight };
            x += buttonWidth + S(8);
        }
        _noticeTop = top + _buttonHeight + S(12);
        _noticeHeight = _notice.Length == 0 ? 0 : Math.Max(line, NativeDialogBody.Measure(_handle, _notice, right - left).Height);
        _bodyContentHeight = _notice.Length == 0 ? top + _buttonHeight + S(14) : _noticeTop + _noticeHeight + S(14);
        return _bodyContentHeight;
    }

    private void LayoutBody(int width, int height, int offset)
    {
        if (_headerHeight == 0) return;
        _ = MeasureBody(width);
        int line = NativeTheme.UiLineHeight;
        Move(_detailTitle, S(18), S(10) - offset, width - S(36), Math.Max(S(31), line));
        nint[] values = [_pathValue, _statusLabel, _terminalLabel];
        nint[] inputs = [_pathEdit, _branchEdit, _newBranchEdit];
        for (int index = 0; index < 3; index++)
        {
            var row = _rows[index];
            int top = row.Top - offset;
            Move(_detailLabels[index], S(18), top, _labelWidth, line);
            Move(values[index], row.Left, top, row.Right - row.Left, row.Bottom - row.Top);
            Move(_createLabels[index], S(18), top + (_fieldHeight - line) / 2, _labelWidth, line);
            _inputFrames[index] = new() { Left = row.Left, Right = row.Right, Top = top, Bottom = top + _fieldHeight };
            Move(inputs[index], row.Left + S(8), top + (_fieldHeight - line) / 2,
                row.Right - row.Left - S(16), line);
        }
        foreach (var (button, bounds) in _actionBounds)
        {
            int top = bounds.Top - offset;
            // 不让下一行按钮只露出一条边；获得焦点时 NativeDialogBody 会滚入完整按钮。
            if (top >= 0 && top < height && top + _buttonHeight > height) top = height + S(1);
            Move(button, bounds.Left, top, bounds.Right - bounds.Left, _buttonHeight);
        }
        Move(_noticeLabel, S(18), _noticeTop - offset, width - S(36), _noticeHeight);
        _ = NativeMethods.ShowWindow(_noticeLabel, _notice.Length == 0 ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
    }

    private bool FocusInputFrame(int x, int y)
    {
        if (!_creatingNew || _operationRunning) return false;
        nint[] inputs = [_pathEdit, _branchEdit, _newBranchEdit];
        for (int index = 0; index < inputs.Length; index++)
        {
            var bounds = _inputFrames[index];
            if (x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom)
            {
                _ = NativeMethods.SetFocus(inputs[index]);
                return true;
            }
        }
        return false;
    }

    private void PaintBody(nint dc)
    {
        _ = NativeMethods.GetClientRectangle(_bodyView!.Handle, out var client);
        var palette = NativeTheme.Palette(_dark);
        Fill(dc, client, palette.Panel);
        if (!_creatingNew) return;
        nint[] inputs = [_pathEdit, _branchEdit, _newBranchEdit];
        for (int index = 0; index < inputs.Length; index++)
        {
            var bounds = _inputFrames[index];
            NativeTheme.FillRounded(dc, bounds, NativeMethods.GetFocus() == inputs[index] ? palette.Accent : palette.BorderStrong, S(10));
            bounds.Left += S(1); bounds.Top += S(1); bounds.Right -= S(1); bounds.Bottom -= S(1);
            NativeTheme.FillRounded(dc, bounds, palette.Panel, S(8));
        }
    }

    private nint PaintWindow()
    {
        nint dc = NativeMethods.BeginPaint(_handle, out var paint);
        if (dc == 0) return 0;
        try
        {
            _ = NativeMethods.GetClientRectangle(_handle, out var client);
            var palette = NativeTheme.Palette(_dark);
            Fill(dc, client, palette.Panel);
            int footer = client.Bottom - _footerHeight;
            Fill(dc, new() { Left = _sidebarBoundary, Top = _headerHeight + _toolbarHeight, Right = _sidebarBoundary + S(1), Bottom = footer - S(16) }, palette.Border);
            Fill(dc, new() { Top = _headerHeight, Right = client.Right, Bottom = _headerHeight + S(1) }, palette.Border);
            Fill(dc, new() { Top = footer, Right = client.Right, Bottom = footer + S(1) }, palette.Border);
            int toolbarCenter = _headerHeight + _toolbarHeight / 2;
            Fill(dc, new() { Left = S(120), Top = toolbarCenter - S(9), Right = S(121), Bottom = toolbarCenter + S(9) }, palette.Border);
            DrawText(dc, UiText.WorktreeManagement, new() { Left = S(26), Right = client.Right - S(60), Bottom = _headerHeight }, palette.Text, NativeTheme.UiMediumFont);
            DrawText(dc, UiText.WorktreeManagement, new() { Left = S(125), Top = _headerHeight, Right = client.Right - S(17), Bottom = _headerHeight + _toolbarHeight }, palette.Text, NativeTheme.UiMediumFont);
        }
        finally { _ = NativeMethods.EndPaint(_handle, ref paint); }
        return 0;
    }
}
