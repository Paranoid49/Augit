using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeStashDialog
{
    private int _headerHeight, _footerHeight, _fieldHeight, _buttonHeight, _messageHeight, _branchHeight;
    private int _labelWidth, _cancelWidth, _createWidth, _bodyHeight, _offset, _maximum, _wheel;
    private bool _layingOut;
    private bool _updatingMessageScroll;
    private string _notice = string.Empty;
    private int _noticeWidth, _noticeHeight;
    private NativeMethods.Rectangle _rootFrame, _messageFrame;

    private void ApplyAppearance()
    {
        NativeTheme.ApplyToWindow(_handle, _dark);
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        foreach (nint control in new[] { _body, _rootCombo, _branchValueLabel, _messageEdit, _keepIndexCheck,
            _createButton, _cancelButton, _headerCloseButton, _noticeLabel }.Concat(_labels))
            NativeTheme.ApplyToControl(control, _dark);
        _toolTip?.ApplyAppearance(_dark);
    }

    private (int Width, int Height) MeasureText(string value, int width = 0)
    {
        nint dc = NativeMethods.GetDeviceContext(_handle);
        nint old = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        try
        {
            NativeMethods.Rectangle bounds = new() { Right = width };
            _ = NativeMethods.DrawText(dc, value, value.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextNoPrefix
                | (width > 0 ? NativeMethods.DrawTextWordBreak : NativeMethods.DrawTextSingleLine));
            return (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
        finally { _ = NativeMethods.SelectObject(dc, old); _ = NativeMethods.ReleaseDeviceContext(_handle, dc); }
    }

    private void MeasureLayout()
    {
        int line = NativeTheme.UiLineHeight;
        _headerHeight = Math.Max(S(HeaderHeight), line + S(16));
        _fieldHeight = Math.Max(S(30), line + S(10));
        _branchHeight = Math.Max(S(24), line);
        _buttonHeight = Math.Max(S(30), line + S(10));
        _messageHeight = Math.Max(S(78), line * 3 + S(12));
        _footerHeight = S(FooterHeight) + _buttonHeight - S(30);
        _labelWidth = Math.Max(S(110), _labels.Max(label => MeasureText(NativeMethods.GetWindowTextValue(label)).Width));
        // 进行态的文字宽度一次预留，点击创建后按钮不跳动。
        _cancelWidth = Math.Max(S(54), Math.Max(MeasureText(UiText.Cancel).Width, MeasureText(UiText.CancelOperation).Width) + S(24));
        _createWidth = Math.Max(S(90), Math.Max(MeasureText(UiText.CreateStashAction).Width, MeasureText(UiText.CreatingStash).Width) + S(24));
        // 以整窗共同边界换算默认高度，分数 DPI 的舍入余量留在正文底部。
        _bodyHeight = S(DialogHeight) - S(HeaderHeight) - S(FooterHeight)
            + _fieldHeight - S(30) + _branchHeight - S(24) + _messageHeight - S(78) + _buttonHeight - S(30);
        // 下拉选择行与弹出列表行都必须容纳真实字体。
        _ = NativeMethods.SendMessage(_rootCombo, NativeMethods.ComboBoxSetItemHeight, unchecked((nuint)(-1)), _fieldHeight - S(6));
        _ = NativeMethods.SendMessage(_rootCombo, NativeMethods.ComboBoxSetItemHeight, 0, _fieldHeight - S(6));
    }

    private void ResizeToContent()
    {
        if (!NativeMethods.GetWindowRectangle(_owner, out var owner)) return;
        int naturalWidth = Math.Max(S(DialogWidth), Math.Max(_cancelWidth + _createWidth + S(44),
            _labelWidth + S(69) + MeasureText(UiText.KeepIndexState).Width));
        int width = Math.Min(naturalWidth, Math.Max(1, owner.Right - owner.Left - S(90)));
        int height = Math.Min(_headerHeight + _bodyHeight + _footerHeight, Math.Max(1, owner.Bottom - owner.Top - S(40)));
        _ = NativeMethods.SetWindowPosition(_handle, 0, owner.Left + (owner.Right - owner.Left - width) / 2,
            owner.Top + (owner.Bottom - owner.Top - height) / 2, width, height,
            NativeMethods.SetWindowPositionNoActivate | NativeMethods.SetWindowPositionNoZOrder);
        Layout();
    }

    private void Layout()
    {
        if (_body == 0 || _headerHeight == 0 || !NativeMethods.GetClientRectangle(_handle, out var client)) return;
        int footer = client.Bottom - _footerHeight;
        Move(_body, S(1), _headerHeight + S(1), client.Right - S(2), Math.Max(0, footer - _headerHeight - S(1)));
        Move(_cancelButton, client.Right - S(18) - _createWidth - S(8) - _cancelWidth,
            footer + (_footerHeight - _buttonHeight) / 2, _cancelWidth, _buttonHeight);
        Move(_createButton, client.Right - S(18) - _createWidth, footer + (_footerHeight - _buttonHeight) / 2, _createWidth, _buttonHeight);
        Move(_headerCloseButton, client.Right - S(45), (_headerHeight - S(31)) / 2, S(32), S(31));
        LayoutBody();
        _ = NativeMethods.InvalidateRectangle(_handle, 0, false);
    }

    private void LayoutBody()
    {
        if (_layingOut || _closed || _headerHeight == 0 || !NativeMethods.GetClientRectangle(_body, out var client)) return;
        _layingOut = true;
        try
        {
            int content = _bodyHeight;
            for (int pass = 0; pass < 2; pass++)
            {
                int width = Math.Max(1, client.Right - S(34));
                if (_notice.Length > 0 && _noticeWidth != width)
                {
                    _noticeHeight = Math.Max(NativeTheme.UiLineHeight, MeasureText(_notice, width).Height);
                    _noticeWidth = width;
                }
                content = _bodyHeight - S(1) + (_notice.Length > 0 ? _noticeHeight + S(8) : 0);
                _maximum = Math.Max(0, content - client.Bottom);
                _offset = Math.Clamp(_offset, 0, _maximum);
                ScrollInfo scroll = new()
                {
                    Size = (uint)Marshal.SizeOf<ScrollInfo>(),
                    Mask = 7,
                    Maximum = Math.Max(0, content - 1),
                    Page = (uint)Math.Max(0, client.Bottom),
                    Position = _offset
                };
                _ = SetScrollInfo(_body, 1, ref scroll, true);
                _ = NativeMethods.GetClientRectangle(_body, out var next);
                if (next.Right == client.Right) break;
                client = next;
            }
            int left = S(17), fieldLeft = left + _labelWidth + S(10), right = client.Right - S(17);
            int rootTop = S(15) - _offset, branchTop = rootTop + _fieldHeight + S(13);
            int messageTop = branchTop + _branchHeight + S(5), checkTop = messageTop + _messageHeight + S(13);
            int line = NativeTheme.UiLineHeight;
            Move(_labels[0], left, rootTop + (_fieldHeight - line) / 2, _labelWidth, line);
            Move(_labels[1], left, branchTop + (_branchHeight - line) / 2, _labelWidth, line);
            Move(_labels[2], left, messageTop + (_messageHeight - line) / 2, _labelWidth, line);
            _rootFrame = new() { Left = fieldLeft, Top = rootTop, Right = right, Bottom = rootTop + _fieldHeight };
            _messageFrame = new() { Left = fieldLeft, Top = messageTop, Right = right, Bottom = messageTop + _messageHeight };
            Move(_rootCombo, fieldLeft + S(1), rootTop + S(1), right - fieldLeft - S(2), _fieldHeight - S(2));
            Move(_branchValueLabel, fieldLeft, branchTop + (_branchHeight - line) / 2, right - fieldLeft, line);
            Move(_messageEdit, fieldLeft + S(8), messageTop + S(6), right - fieldLeft - S(16), _messageHeight - S(12));
            UpdateMessageScroll();
            Move(_keepIndexCheck, fieldLeft, checkTop, right - fieldLeft, _buttonHeight);
            Move(_noticeLabel, left, _bodyHeight - S(9) - _offset, right - left, _notice.Length == 0 ? 0 : _noticeHeight);
            _ = NativeMethods.ShowWindow(_noticeLabel, _notice.Length == 0 ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
            _ = NativeMethods.InvalidateRectangle(_body, 0, false);
        }
        finally { _layingOut = false; }
    }

    private void SetNotice(string notice)
    {
        if (_notice == notice) return;
        _notice = notice; _noticeWidth = 0;
        _ = NativeMethods.SetWindowText(_noticeLabel, notice);
        _toolTip?.Update(_noticeLabel, notice);
        LayoutBody();
        if (notice.Length != 0) EnsureVisible(_noticeLabel);
    }

    private void UpdateMessageScroll()
    {
        if (_updatingMessageScroll || _messageEdit == 0 || !NativeMethods.GetClientRectangle(_messageEdit, out var bounds)) return;
        _updatingMessageScroll = true;
        try
        {
            int lines = (int)NativeMethods.SendMessage(_messageEdit, 0x00BA, 0, 0); // EM_GETLINECOUNT 包含自动折行。
            bool needed = lines > Math.Max(1, bounds.Bottom / Math.Max(1, NativeTheme.UiLineHeight));
            bool visible = (NativeMethods.GetWindowLongPointer(_messageEdit, NativeMethods.WindowLongStyle).ToInt64()
                & NativeMethods.WindowStyleVerticalScroll) != 0;
            if (visible != needed) _ = ShowScrollBar(_messageEdit, 1, needed);
        }
        finally { _updatingMessageScroll = false; }
    }

    private void SetOffset(int value)
    {
        int next = Math.Clamp(value, 0, _maximum);
        if (_offset == next) return;
        _offset = next; LayoutBody();
    }

    private void EnsureVisible(nint control)
    {
        if (_layingOut || !NativeMethods.GetWindowRectangle(control, out var rect)
            || !NativeMethods.GetClientRectangle(_body, out var client)) return;
        NativeMethods.Point point = new() { X = rect.Left, Y = rect.Top };
        _ = NativeMethods.ScreenToClient(_body, ref point);
        int height = rect.Bottom - rect.Top;
        if (point.Y < 0 || height >= client.Bottom) SetOffset(_offset + point.Y);
        else if (point.Y + height > client.Bottom) SetOffset(_offset + point.Y + height - client.Bottom);
    }

    private nint HandleBodyMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        if (message == 0x0082)
        {
            _ = NativeMethods.RemoveWindowSubclass(window, _bodyProcedure, id);
            return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        }
        if (window == _messageEdit && message is 0x010D or 0x010E) _composing = message == 0x010D;
        if (window == _keepIndexCheck && message is NativeMethods.WindowMessagePaint or 0x0318)
        {
            if (message == 0x0318) PaintCheckbox(unchecked((nint)word));
            else
            {
                nint dc = NativeMethods.BeginPaint(window, out var paint);
                try { PaintCheckbox(dc); }
                finally { _ = NativeMethods.EndPaint(window, ref paint); }
            }
            return 0;
        }
        // 多行消息自身消费滚轮；表单空白、下拉框和复选框滚动外层正文。
        if (message == NativeMethods.WindowMessageMouseWheel && window != _messageEdit)
        {
            _wheel += unchecked((short)NativeMethods.HighWord(word));
            int steps = _wheel / 120; _wheel %= 120;
            SetOffset(_offset - steps * _fieldHeight * 3); return 0;
        }
        if (window != _body)
        {
            nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
            // 多行 EDIT 的 WM_SETTEXT 不发送 EN_CHANGE，赋值完成后也要同步滚动条。
            if (window == _messageEdit && message == 0x000C) UpdateMessageScroll();
            if (message == NativeMethods.WindowMessageSetFocus) EnsureVisible(window);
            if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
            {
                _ = NativeMethods.InvalidateRectangle(_body, 0, false);
                _ = NativeMethods.InvalidateRectangle(window, 0, false);
            }
            return result;
        }
        switch (message)
        {
            case NativeMethods.WindowMessageSize: LayoutBody(); return 0;
            case NativeMethods.WindowMessageEraseBackground: return 1;
            case NativeMethods.WindowMessagePaint: PaintBody(); return 0;
            case NativeMethods.WindowMessageVerticalScroll:
                _ = NativeMethods.GetClientRectangle(_body, out var client);
                ScrollInfo scroll = new() { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x10 };
                _ = GetScrollInfo(_body, 1, ref scroll);
                SetOffset(NativeMethods.LowWord(word) switch
                {
                    0 => _offset - _fieldHeight,
                    1 => _offset + _fieldHeight,
                    2 => _offset - client.Bottom,
                    3 => _offset + client.Bottom,
                    4 or 5 => scroll.TrackPosition,
                    6 => 0,
                    7 => _maximum,
                    _ => _offset,
                }); return 0;
            case NativeMethods.WindowMessageLeftButtonDown:
                int x = unchecked((short)NativeMethods.LowWord(unchecked((nuint)parameter)));
                int y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)parameter)));
                if (x >= _messageFrame.Left && x < _messageFrame.Right && y >= _messageFrame.Top && y < _messageFrame.Bottom)
                { if (NativeMethods.IsWindowEnabled(_messageEdit)) _ = NativeMethods.SetFocus(_messageEdit); return 0; }
                break;
            case NativeMethods.WindowMessageCommand:
            case NativeMethods.WindowMessageDrawItem:
            case NativeMethods.WindowMessageControlColorEdit:
            case NativeMethods.WindowMessageControlColorListBox:
            case NativeMethods.WindowMessageControlColorStatic:
            case NativeMethods.WindowMessageControlColorButton:
                return NativeMethods.SendMessage(_handle, message, word, parameter);
        }
        return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
    }

    private nint ControlColor(nint dc)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        _ = NativeMethods.SetBackgroundColor(dc, palette.Panel);
        _ = NativeMethods.SetTextColor(dc, palette.Text);
        _ = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        return _controlBrush;
    }

    private void PaintWindow()
    {
        nint dc = NativeMethods.BeginPaint(_handle, out var paint);
        try
        {
            _ = NativeMethods.GetClientRectangle(_handle, out var rect);
            var palette = NativeTheme.Palette(_dark);
            NativeTheme.Fill(dc, rect, palette.BorderStrong);
            NativeTheme.Fill(dc, new() { Left = S(1), Top = S(1), Right = rect.Right - S(1), Bottom = rect.Bottom - S(1) }, palette.Panel);
            NativeTheme.Fill(dc, new() { Left = 0, Top = _headerHeight, Right = rect.Right, Bottom = _headerHeight + S(1) }, palette.Border);
            NativeTheme.Fill(dc, new() { Left = 0, Top = rect.Bottom - _footerHeight, Right = rect.Right, Bottom = rect.Bottom - _footerHeight + S(1) }, palette.Border);
            DrawText(dc, UiText.CreateStashTitle, new() { Left = S(26), Top = S(1), Right = rect.Right - S(54), Bottom = _headerHeight }, palette.Text, NativeTheme.UiMediumFont);
        }
        finally { _ = NativeMethods.EndPaint(_handle, ref paint); }
    }

    private void PaintBody()
    {
        nint dc = NativeMethods.BeginPaint(_body, out var paint);
        try
        {
            _ = NativeMethods.GetClientRectangle(_body, out var rect);
            var palette = NativeTheme.Palette(_dark);
            NativeTheme.Fill(dc, rect, palette.Panel);
            foreach (var (frame, control) in new[] { (_rootFrame, _rootCombo), (_messageFrame, _messageEdit) })
            {
                NativeTheme.FillRounded(dc, frame, NativeMethods.GetFocus() == control ? palette.Accent : palette.Border, S(10));
                var inner = frame;
                inner.Left += S(1); inner.Top += S(1); inner.Right -= S(1); inner.Bottom -= S(1);
                NativeTheme.FillRounded(dc, inner, palette.Panel, S(8));
            }
        }
        finally { _ = NativeMethods.EndPaint(_body, ref paint); }
    }

    private void PaintCheckbox(nint dc)
    {
        if (dc == 0) return;
        _ = NativeMethods.GetClientRectangle(_keepIndexCheck, out var rect);
        var palette = NativeTheme.Palette(_dark);
        NativeTheme.Fill(dc, rect, palette.Panel);
        bool enabled = NativeMethods.IsWindowEnabled(_keepIndexCheck);
        bool value = NativeMethods.SendMessage(_keepIndexCheck, NativeMethods.ButtonMessageGetCheck, 0, 0) == (nint)NativeMethods.ButtonChecked;
        _ = NativeTheme.DrawCheckbox(dc, S(1), rect.Bottom / 2, value ? NativeCheckboxState.Checked : NativeCheckboxState.Unchecked, _dark, enabled);
        rect.Left += S(23);
        DrawText(dc, UiText.KeepIndexState, rect, enabled ? palette.Text : palette.Faint, NativeTheme.UiFont);
        if (NativeMethods.GetFocus() == _keepIndexCheck) DrawFocus(dc, rect, palette.Accent);
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0) return false;
        var item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        if (item.Control == _rootCombo)
        {
            var palette = NativeTheme.Palette(_dark);
            bool dropped = NativeMethods.SendMessage(_rootCombo, 0x0157, 0, 0) != 0;
            bool selected = dropped && (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
            NativeTheme.Fill(item.DeviceContext, item.ItemRectangle, selected ? palette.AccentSoft : palette.Panel);
            var text = item.ItemRectangle;
            text.Left += S(8); text.Right -= S(8);
            DrawText(item.DeviceContext, _repository.RepositoryRoot ?? _repository.WorkspacePath, text,
                NativeMethods.IsWindowEnabled(_rootCombo) ? palette.Text : palette.Faint, NativeTheme.UiFont);
            return true;
        }
        _ = NativeTheme.DrawFlatButton(parameter, _dark, emphasized: item.Control == _createButton, outlined: item.Control == _cancelButton);
        if (item.Control == _headerCloseButton)
            _ = NativeTheme.DrawTabCloseIcon(item.DeviceContext, (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2,
                (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2, NativeTheme.Palette(_dark).Muted);
        if ((item.ItemState & NativeMethods.OwnerDrawFocus) != 0)
            DrawFocus(item.DeviceContext, item.ItemRectangle, item.Control == _createButton ? 0xFFFFFF : NativeTheme.Palette(_dark).Accent);
        return true;
    }

    private static void DrawFocus(nint dc, NativeMethods.Rectangle rect, uint color)
    {
        int inset = S(3);
        _ = NativeGdiPlusDrawing.StrokeShapes(dc, color, Math.Max(1, S(1)), [], [],
            [new(rect.Left + inset, rect.Top + inset, Math.Max(0, rect.Right - rect.Left - inset * 2), Math.Max(0, rect.Bottom - rect.Top - inset * 2))], []);
    }

    private static void DrawText(nint dc, string text, NativeMethods.Rectangle rect, uint color, nint font)
    {
        nint previous = NativeMethods.SelectObject(dc, font);
        _ = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(dc, color);
        _ = NativeMethods.DrawText(dc, text, text.Length, ref rect, NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextVerticalCenter
            | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis);
        _ = NativeMethods.SelectObject(dc, previous);
    }

    private static void Move(nint control, int x, int y, int width, int height) =>
        _ = NativeMethods.MoveWindow(control, x, y, Math.Max(0, width), Math.Max(0, height), true);

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint Size, Mask;
        public int Minimum, Maximum;
        public uint Page;
        public int Position, TrackPosition;
    }
    [DllImport("user32.dll")] private static extern int SetScrollInfo(nint window, int bar, ref ScrollInfo info, bool redraw);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetScrollInfo(nint window, int bar, ref ScrollInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowScrollBar(nint window, int bar, bool show);
}
