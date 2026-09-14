using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeCloneDialog
{
    private int _headerHeight, _footerHeight, _fieldHeight, _labelWidth, _cancelWidth, _cloneWidth;
    private int _shallowWidth, _depthWidth, _suffixWidth, _offset, _maximum, _wheel, _noticeWidth, _noticeHeight;
    private bool _layingOut;
    private string _notice = string.Empty;
    private NativeMethods.Rectangle _versionFrame, _sourceFrame, _destinationFrame, _depthFrame;
    private (int Width, int Height, int Diameter) _windowRegion, _comboRegion;

    private void ApplyAppearance()
    {
        NativeTheme.ApplyToWindow(_handle, _dark);
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        foreach (nint control in new[] { _body, _versionCombo, _sourceEdit, _destinationEdit, _shallowCheck,
            _depthEdit, _depthSuffixLabel, _cloneButton, _cancelButton, _headerCloseButton, _noticeLabel }.Concat(_labels))
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
            _ = NativeMethods.DrawText(dc, value, value.Length, ref bounds, NativeMethods.DrawTextCalculateRectangle
                | NativeMethods.DrawTextNoPrefix | (width > 0 ? NativeMethods.DrawTextWordBreak : NativeMethods.DrawTextSingleLine));
            return (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
        finally { _ = NativeMethods.SelectObject(dc, old); _ = NativeMethods.ReleaseDeviceContext(_handle, dc); }
    }

    private void MeasureLayout()
    {
        int line = NativeTheme.UiLineHeight;
        _headerHeight = Math.Max(S(HeaderHeight), line + S(16));
        _fieldHeight = Math.Max(S(30), line + S(10));
        _footerHeight = S(FooterHeight) + _fieldHeight - S(30);
        _labelWidth = Math.Max(S(110), _labels.Max(label => MeasureText(NativeMethods.GetWindowTextValue(label)).Width));
        _cancelWidth = Math.Max(S(53), Math.Max(MeasureText(UiText.Cancel).Width, MeasureText(UiText.CancelOperation).Width) + S(24));
        _cloneWidth = Math.Max(S(52), Math.Max(MeasureText(UiText.Clone).Width, MeasureText(UiText.Cloning).Width) + S(24));
        _shallowWidth = Math.Max(S(180), MeasureText(UiText.ShallowClone).Width + S(23));
        _depthWidth = Math.Max(S(58), MeasureText("1000").Width + S(16));
        _suffixWidth = Math.Max(S(80), MeasureText(UiText.CloneCommitUnit).Width);
        _ = NativeMethods.SendMessage(_versionCombo, NativeMethods.ComboBoxSetItemHeight, unchecked((nuint)(-1)), _fieldHeight - S(6));
        _ = NativeMethods.SendMessage(_versionCombo, NativeMethods.ComboBoxSetItemHeight, 0, _fieldHeight - S(6));
    }

    private int ShallowTop => S(15) + 3 * (_fieldHeight + S(13)) + S(1);
    private bool WrapDepth(int fieldWidth) => _shallowWidth + S(10) + _depthWidth + S(10) + _suffixWidth > fieldWidth;
    // 默认整窗按同一边界缩放；分数 DPI 的舍入余量留在正文底部，避免初始状态多出滚动条。
    private int BodyHeight(int fieldWidth) => S(DialogHeight) - S(HeaderHeight) - S(FooterHeight) + 4 * (_fieldHeight - S(30))
        + (WrapDepth(fieldWidth) ? _fieldHeight + S(8) : 0);

    private void ResizeToContent()
    {
        if (!NativeMethods.GetWindowRectangle(_owner, out var owner)) return;
        int width = Math.Min(Math.Max(S(DialogWidth), _labelWidth + S(44) + _shallowWidth),
            Math.Max(1, owner.Right - owner.Left - S(90)));
        int height = Math.Min(_headerHeight + BodyHeight(width - S(46) - _labelWidth) + _footerHeight,
            Math.Max(1, owner.Bottom - owner.Top - S(40)));
        _ = NativeMethods.SetWindowPosition(_handle, 0, owner.Left + (owner.Right - owner.Left - width) / 2,
            owner.Top + (owner.Bottom - owner.Top - height) / 2, width, height,
            NativeMethods.SetWindowPositionNoActivate | NativeMethods.SetWindowPositionNoZOrder);
        Layout();
    }

    private void Layout()
    {
        if (_body == 0 || _headerHeight == 0 || !NativeMethods.GetClientRectangle(_handle, out var client)) return;
        UpdateRoundedRegion(_handle, client.Right, client.Bottom, S(18), ref _windowRegion);
        int footer = client.Bottom - _footerHeight;
        Move(_body, S(1), _headerHeight + S(1), client.Right - S(2), Math.Max(0, footer - _headerHeight - S(1)));
        Move(_cancelButton, client.Right - S(14) - _cloneWidth - S(8) - _cancelWidth,
            footer + (_footerHeight - _fieldHeight) / 2, _cancelWidth, _fieldHeight);
        Move(_cloneButton, client.Right - S(14) - _cloneWidth, footer + (_footerHeight - _fieldHeight) / 2, _cloneWidth, _fieldHeight);
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
            for (int pass = 0; pass < 3; pass++)
            {
                int width = Math.Max(1, client.Right - S(34));
                if (_notice.Length > 0 && _noticeWidth != width)
                {
                    _noticeHeight = Math.Max(NativeTheme.UiLineHeight, MeasureText(_notice, width).Height);
                    _noticeWidth = width;
                }
                int bodyHeight = BodyHeight(client.Right - S(44) - _labelWidth);
                int content = bodyHeight - S(1) + (_notice.Length > 0 ? _noticeHeight + S(8) : 0);
                _maximum = Math.Max(0, content - client.Bottom);
                _offset = Math.Clamp(_offset, 0, _maximum);
                ScrollInfo scroll = new()
                {
                    Size = (uint)Marshal.SizeOf<ScrollInfo>(),
                    Mask = 7,
                    Maximum = Math.Max(0, content - 1),
                    Page = (uint)Math.Max(0, client.Bottom),
                    Position = _offset,
                };
                _ = SetScrollInfo(_body, 1, ref scroll, true);
                _ = NativeMethods.GetClientRectangle(_body, out var next);
                if (next.Right == client.Right) break;
                client = next;
            }
            int left = S(17), fieldLeft = left + _labelWidth + S(10), right = client.Right - S(17);
            int fieldWidth = Math.Max(0, right - fieldLeft), line = NativeTheme.UiLineHeight;
            nint[] controls = [_versionCombo, _sourceEdit, _destinationEdit];
            var frames = new NativeMethods.Rectangle[3];
            for (int index = 0; index < controls.Length; index++)
            {
                int top = S(15) + index * (_fieldHeight + S(13)) - _offset;
                Move(_labels[index], left, top + (_fieldHeight - line) / 2, _labelWidth, line);
                frames[index] = MoveInput(controls[index], fieldLeft, top, fieldWidth);
            }
            (_versionFrame, _sourceFrame, _destinationFrame) = (frames[0], frames[1], frames[2]);
            int shallowTop = ShallowTop - _offset;
            bool wrapped = WrapDepth(fieldWidth);
            Move(_shallowCheck, fieldLeft, shallowTop, Math.Min(fieldWidth, _shallowWidth), _fieldHeight);
            int depthLeft = wrapped ? fieldLeft : fieldLeft + _shallowWidth + S(10);
            int depthTop = wrapped ? shallowTop + _fieldHeight + S(8) : shallowTop;
            _depthFrame = MoveInput(_depthEdit, depthLeft, depthTop, _depthWidth);
            Move(_depthSuffixLabel, depthLeft + _depthWidth + S(10), depthTop + (_fieldHeight - line) / 2,
                Math.Min(_suffixWidth, Math.Max(0, right - depthLeft - _depthWidth - S(10))), line);
            Move(_noticeLabel, left, BodyHeight(fieldWidth) - S(8) - _offset,
                right - left, _notice.Length == 0 ? 0 : _noticeHeight);
            _ = NativeMethods.ShowWindow(_noticeLabel, _notice.Length == 0 ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
            _ = NativeMethods.InvalidateRectangle(_body, 0, false);
        }
        finally { _layingOut = false; }
    }

    private NativeMethods.Rectangle MoveInput(nint control, int left, int top, int width)
    {
        NativeMethods.Rectangle frame = new() { Left = left, Top = top, Right = left + width, Bottom = top + _fieldHeight };
        if (control == _versionCombo)
        {
            int height = _fieldHeight - S(2);
            Move(control, left + S(1), top + S(1), width - S(2), height);
            // ComboBox 会按系统非客户区尺寸自行定高；只传 MoveWindow 高度会盖住父容器的下边框。
            if (NativeMethods.GetWindowRectangle(control, out var bounds) && bounds.Bottom - bounds.Top != height)
            {
                int selection = (int)NativeMethods.SendMessage(control, 0x0154, unchecked((nuint)(-1)), 0);
                _ = NativeMethods.SendMessage(control, NativeMethods.ComboBoxSetItemHeight, unchecked((nuint)(-1)),
                    Math.Max(1, selection + height - (bounds.Bottom - bounds.Top)));
                Move(control, left + S(1), top + S(1), width - S(2), height);
            }
            // 完整矩形的组合框会覆盖父容器输入框的抗锯齿内角；仅裁切收起态 HWND，列表不变。
            UpdateRoundedRegion(control, width - S(2), height, S(8), ref _comboRegion);
        }
        else Move(control, left + S(8), top + (_fieldHeight - NativeTheme.UiLineHeight) / 2, width - S(16), NativeTheme.UiLineHeight);
        return frame;
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
            _ = NativeMethods.RemoveWindowSubclass(window, _inputProcedure, id);
            return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        }
        if ((window == _sourceEdit || window == _destinationEdit || window == _depthEdit) && message is 0x010D or 0x010E)
            _composing = message == 0x010D;
        if ((window == _shallowCheck || window == _depthSuffixLabel)
            && message is NativeMethods.WindowMessagePaint or 0x0318)
        {
            if (message == 0x0318) PaintFormControl(window, unchecked((nint)word));
            else
            {
                nint dc = NativeMethods.BeginPaint(window, out var paint);
                try { PaintFormControl(window, dc); }
                finally { _ = NativeMethods.EndPaint(window, ref paint); }
            }
            return 0;
        }
        if (message == NativeMethods.WindowMessageMouseWheel)
        {
            _wheel += unchecked((short)NativeMethods.HighWord(word));
            int steps = _wheel / 120; _wheel %= 120;
            SetOffset(_offset - steps * _fieldHeight * 3); return 0;
        }
        if (window != _body)
        {
            nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
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
                foreach (var (frame, control) in InputFrames())
                    if (x >= frame.Left && x < frame.Right && y >= frame.Top && y < frame.Bottom && NativeMethods.IsWindowEnabled(control))
                    { _ = NativeMethods.SetFocus(control); return 0; }
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

    private (NativeMethods.Rectangle Frame, nint Control)[] InputFrames() =>
        [(_versionFrame, _versionCombo), (_sourceFrame, _sourceEdit), (_destinationFrame, _destinationEdit), (_depthFrame, _depthEdit)];

    private nint ControlColor(nint dc)
    {
        var palette = NativeTheme.Palette(_dark);
        _ = NativeMethods.SetBackgroundColor(dc, palette.Panel);
        _ = NativeMethods.SetTextColor(dc, palette.Text);
        _ = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        return _controlBrush;
    }

    private nint PaintWindow()
    {
        nint dc = NativeMethods.BeginPaint(_handle, out var paint);
        try
        {
            _ = NativeMethods.GetClientRectangle(_handle, out var rect);
            var palette = NativeTheme.Palette(_dark);
            NativeTheme.FillRounded(dc, rect, palette.BorderStrong, S(18));
            NativeTheme.FillRounded(dc, new() { Left = S(1), Top = S(1), Right = rect.Right - S(1), Bottom = rect.Bottom - S(1) }, palette.Panel, S(16));
            NativeTheme.Fill(dc, new() { Top = _headerHeight, Right = rect.Right, Bottom = _headerHeight + S(1) }, palette.Border);
            NativeTheme.Fill(dc, new() { Top = rect.Bottom - _footerHeight, Right = rect.Right, Bottom = rect.Bottom - _footerHeight + S(1) }, palette.Border);
            DrawText(dc, UiText.CloneRepository, new() { Left = S(13), Top = S(1), Right = rect.Right - S(54), Bottom = _headerHeight }, palette.Text, NativeTheme.UiMediumFont);
        }
        finally { _ = NativeMethods.EndPaint(_handle, ref paint); }
        return 0;
    }

    private void PaintBody()
    {
        nint dc = NativeMethods.BeginPaint(_body, out var paint);
        try
        {
            _ = NativeMethods.GetClientRectangle(_body, out var rect);
            var palette = NativeTheme.Palette(_dark);
            NativeTheme.Fill(dc, rect, palette.Panel);
            foreach (var (frame, control) in InputFrames())
            {
                NativeTheme.FillRounded(dc, frame, NativeMethods.GetFocus() == control ? palette.Accent : palette.Border, S(10));
                var inner = frame;
                inner.Left += S(1); inner.Top += S(1); inner.Right -= S(1); inner.Bottom -= S(1);
                NativeTheme.FillRounded(dc, inner, palette.Panel, S(8));
            }
        }
        finally { _ = NativeMethods.EndPaint(_body, ref paint); }
    }

    private void PaintFormControl(nint window, nint dc)
    {
        if (dc == 0) return;
        _ = NativeMethods.GetClientRectangle(window, out var rect);
        var palette = NativeTheme.Palette(_dark);
        NativeTheme.Fill(dc, rect, palette.Panel);
        bool enabled = NativeMethods.IsWindowEnabled(window);
        // 禁用的单位文字也按主题绘制，避免系统 STATIC 的白色浮雕效果。
        if (window == _depthSuffixLabel)
        {
            DrawText(dc, UiText.CloneCommitUnit, rect, enabled ? palette.Text : palette.Faint, NativeTheme.UiFont);
            return;
        }
        bool value = NativeMethods.SendMessage(_shallowCheck, NativeMethods.ButtonMessageGetCheck, 0, 0) == 1;
        _ = NativeTheme.DrawCheckbox(dc, S(1), rect.Bottom / 2, value ? NativeCheckboxState.Checked : NativeCheckboxState.Unchecked, _dark, enabled);
        rect.Left += S(23);
        DrawText(dc, UiText.ShallowClone, rect, enabled ? palette.Text : palette.Faint, NativeTheme.UiFont);
        if (NativeMethods.GetFocus() == _shallowCheck) DrawFocus(dc, rect, palette.Accent);
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0) return false;
        var item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        if (item.Control == _versionCombo) return NativeComboBoxTheme.DrawItem(parameter, ["Git"], _dark);
        _ = NativeTheme.DrawFlatButton(parameter, _dark, emphasized: item.Control == _cloneButton, outlined: item.Control == _cancelButton);
        if (item.Control == _headerCloseButton)
            _ = NativeTheme.DrawTabCloseIcon(item.DeviceContext, (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2,
                (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2, NativeTheme.Palette(_dark).Muted);
        if ((item.ItemState & NativeMethods.OwnerDrawFocus) != 0)
            DrawFocus(item.DeviceContext, item.ItemRectangle, item.Control == _cloneButton ? 0xFFFFFF : NativeTheme.Palette(_dark).Accent);
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

    private static void UpdateRoundedRegion(nint window, int width, int height, int diameter,
        ref (int Width, int Height, int Diameter) previous)
    {
        var next = (width, height, diameter);
        if (window == 0 || width <= 0 || height <= 0 || previous == next) return;
        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, width + 1, height + 1, diameter, diameter);
        if (region == 0) return;
        // 成功后区域归 Windows 所有；替换及窗口销毁时由系统释放。失败时仍由本方法释放。
        if (NativeMethods.SetWindowRegion(window, region, true) == 0) _ = NativeMethods.DeleteObject(region);
        else previous = next;
    }

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
}
