using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeRemoteDialog
{
    private readonly NativeMethods.SubclassProcedure _detailProcedure;
    private nint _detailPanel;
    private int _headerHeight, _footerHeight, _toolbarHeight, _textHeight, _fieldHeight, _buttonHeight, _labelWidth;
    private int _sidebarBoundary, _detailOffset, _detailMaximum, _wheelRemainder;
    private int _saveWidth, _deleteWidth, _closeWidth, _cancelWidth, _noticeMeasureWidth, _noticeMeasureHeight;
    private bool _layingOutDetails;
    private string _notice = string.Empty;
    private readonly NativeMethods.Rectangle[] _inputFrames = new NativeMethods.Rectangle[3];

    internal nint HandleForTest => _handle;
    internal nint DetailPanelForTest => _detailPanel;

    private void MeasureLayout()
    {
        _textHeight = NativeTheme.UiLineHeight;
        _headerHeight = Math.Max(S(HeaderHeight), _textHeight + S(16));
        _fieldHeight = Math.Max(S(30), _textHeight + S(12));
        _buttonHeight = Math.Max(S(28), _textHeight + S(12));
        _footerHeight = Math.Max(S(FooterHeight), _buttonHeight + S(25));
        _toolbarHeight = Math.Max(S(61), _textHeight + S(28));
        _labelWidth = Math.Max(S(110), FieldLabelsForTest.Max(label => MeasureText(label).Width));
        _saveWidth = MeasureButton(UiText.Save, 78);
        _deleteWidth = MeasureButton(UiText.Delete, 78);
        _closeWidth = MeasureButton(UiText.Close, 78);
        _cancelWidth = MeasureButton(UiText.CancelOperation, 100);
        _noticeMeasureWidth = 0;
    }

    private (int Width, int Height) MeasureText(string text, int width = 0)
    {
        nint dc = NativeMethods.GetDeviceContext(_handle);
        nint previous = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        try
        {
            NativeMethods.Rectangle bounds = new() { Right = width };
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextNoPrefix
                    | (width > 0 ? NativeMethods.DrawTextWordBreak : NativeMethods.DrawTextSingleLine));
            return (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(_handle, dc);
        }
    }

    private int MeasureButton(string text, int minimum) => Math.Max(S(minimum), MeasureText(text).Width + S(24));

    private void ResizeToContent()
    {
        if (!NativeMethods.GetWindowRectangle(_owner, out var owner)) return;
        int width = Math.Min(S(DialogWidth), Math.Max(1, owner.Right - owner.Left - S(90)));
        int content = S(14) + Math.Max(S(30), _textHeight) + S(6)
            + 3 * _fieldHeight + S(28) + S(16) + _buttonHeight + S(16);
        int height = Math.Min(Math.Max(S(DialogHeight), _headerHeight + _toolbarHeight + content + _footerHeight),
            Math.Max(1, owner.Bottom - owner.Top - S(88)));
        _ = NativeMethods.SetWindowPosition(_handle, 0,
            owner.Left + (owner.Right - owner.Left - width) / 2,
            owner.Top + (owner.Bottom - owner.Top - height) / 2, width, height,
            NativeMethods.SetWindowPositionNoActivate | NativeMethods.SetWindowPositionNoZOrder);
        Layout();
    }

    private void LayoutDetails()
    {
        if (_layingOutDetails || _detailPanel == 0 || !NativeMethods.GetClientRectangle(_detailPanel, out var client)) return;
        _layingOutDetails = true;
        try
        {
            int titleHeight = Math.Max(S(30), _textHeight);
            int row = S(14) + titleHeight + S(6);
            int actions = row + 3 * _fieldHeight + S(28) + S(16);
            int noticeTop = actions + _buttonHeight + S(12);
            int contentWidth = Math.Max(1, client.Right - S(36));
            int noticeHeight = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                if (_notice.Length != 0 && _noticeMeasureWidth != contentWidth)
                {
                    _noticeMeasureHeight = Math.Max(_textHeight, MeasureText(_notice, contentWidth).Height);
                    _noticeMeasureWidth = contentWidth;
                }
                noticeHeight = _notice.Length == 0 ? 0 : _noticeMeasureHeight;
                int contentHeight = _notice.Length == 0 ? actions + _buttonHeight + S(16) : noticeTop + noticeHeight + S(16);
                _detailMaximum = Math.Max(0, contentHeight - client.Bottom);
                _detailOffset = Math.Clamp(_detailOffset, 0, _detailMaximum);
                ScrollInfo scroll = new()
                {
                    Size = (uint)Marshal.SizeOf<ScrollInfo>(),
                    Mask = 7,
                    Maximum = Math.Max(0, contentHeight - 1),
                    Page = (uint)Math.Max(0, client.Bottom),
                    Position = _detailOffset,
                };
                _ = SetScrollInformation(_detailPanel, 1, ref scroll, true);
                // 滚动条出现后按最终客户区重新计算换行，防止错误说明末行被裁切。
                _ = NativeMethods.GetClientRectangle(_detailPanel, out client);
                int finalWidth = Math.Max(1, client.Right - S(36));
                if (finalWidth == contentWidth) break;
                contentWidth = finalWidth;
            }
            contentWidth = Math.Max(1, client.Right - S(36));
            Move(_detailTitle, S(18), S(14) - _detailOffset, contentWidth, titleHeight);
            nint[] inputs = [_nameEdit, _fetchUrlEdit, _pushUrlEdit];
            int fieldLeft = S(18) + _labelWidth + S(10);
            for (int index = 0; index < inputs.Length; index++)
            {
                int top = row + index * (_fieldHeight + S(14)) - _detailOffset;
                Move(_labels[index], S(18), top + (_fieldHeight - _textHeight) / 2, _labelWidth, _textHeight);
                _inputFrames[index] = new() { Left = fieldLeft, Top = top, Right = client.Right - S(18), Bottom = top + _fieldHeight };
                Move(inputs[index], fieldLeft + S(8), top + (_fieldHeight - _textHeight) / 2,
                    Math.Max(1, client.Right - S(34) - fieldLeft), _textHeight);
            }
            Move(_saveButton, client.Right - S(18) - _saveWidth, actions - _detailOffset, _saveWidth, _buttonHeight);
            Move(_deleteButton, client.Right - S(26) - _saveWidth - _deleteWidth, actions - _detailOffset, _deleteWidth, _buttonHeight);
            Move(_noticeLabel, S(18), noticeTop - _detailOffset, contentWidth, noticeHeight);
            _ = NativeMethods.ShowWindow(_noticeLabel, _notice.Length == 0 ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
            _ = NativeMethods.InvalidateRectangle(_detailPanel, 0, false);
        }
        finally { _layingOutDetails = false; }
    }

    private void SetNotice(string text)
    {
        if (_notice == text) return;
        _notice = text;
        _noticeMeasureWidth = 0;
        _ = NativeMethods.SetWindowText(_noticeLabel, text);
        _toolTip?.Update(_noticeLabel, text);
        LayoutDetails();
    }

    private void EnsureDetailVisible(nint control)
    {
        if (_layingOutDetails || !NativeMethods.IsChild(_detailPanel, control)
            || !NativeMethods.GetWindowRectangle(control, out var bounds)
            || !NativeMethods.GetClientRectangle(_detailPanel, out var client)) return;
        NativeMethods.Point point = new() { X = bounds.Left, Y = bounds.Top };
        _ = NativeMethods.ScreenToClient(_detailPanel, ref point);
        int top = point.Y - S(8), bottom = point.Y + bounds.Bottom - bounds.Top + S(8);
        if (top < 0) SetDetailOffset(_detailOffset + top);
        else if (bottom > client.Bottom) SetDetailOffset(_detailOffset + bottom - client.Bottom);
    }

    private void SetDetailOffset(int offset)
    {
        int next = Math.Clamp(offset, 0, _detailMaximum);
        if (next == _detailOffset) return;
        _detailOffset = next;
        LayoutDetails();
    }

    private nint HandleDetailMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        if (message == 0x0082)
        {
            _ = NativeMethods.RemoveWindowSubclass(window, _detailProcedure, id);
            return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        }
        if (message == NativeMethods.WindowMessageMouseWheel)
        {
            _wheelRemainder += unchecked((short)NativeMethods.HighWord(word));
            int steps = _wheelRemainder / 120;
            _wheelRemainder %= 120;
            SetDetailOffset(_detailOffset - steps * Math.Max(S(27), _textHeight + S(8)) * 3);
            return 0;
        }
        if (window != _detailPanel)
        {
            nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
            if (message == NativeMethods.WindowMessageSetFocus) EnsureDetailVisible(window);
            if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
                _ = NativeMethods.InvalidateRectangle(_detailPanel, 0, false);
            return result;
        }
        switch (message)
        {
            case NativeMethods.WindowMessageSize:
                LayoutDetails(); return 0;
            case NativeMethods.WindowMessageEraseBackground: return 1;
            case NativeMethods.WindowMessagePaint:
                PaintDetails(); return 0;
            case NativeMethods.WindowMessageLeftButtonDown:
                int x = unchecked((short)NativeMethods.LowWord(unchecked((nuint)parameter)));
                int y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)parameter)));
                nint[] inputs = [_nameEdit, _fetchUrlEdit, _pushUrlEdit];
                for (int index = 0; index < inputs.Length; index++)
                {
                    var frame = _inputFrames[index];
                    if (x >= frame.Left && x < frame.Right && y >= frame.Top && y < frame.Bottom)
                    {
                        _ = NativeMethods.SetFocus(inputs[index]);
                        return 0;
                    }
                }
                break;
            case NativeMethods.WindowMessageVerticalScroll:
                _ = NativeMethods.GetClientRectangle(_detailPanel, out var client);
                ScrollInfo scroll = new() { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x10 };
                _ = GetScrollInformation(_detailPanel, 1, ref scroll);
                int next = NativeMethods.LowWord(word) switch
                {
                    0 => _detailOffset - _fieldHeight,
                    1 => _detailOffset + _fieldHeight,
                    2 => _detailOffset - client.Bottom,
                    3 => _detailOffset + client.Bottom,
                    4 or 5 => scroll.TrackPosition,
                    6 => 0,
                    7 => _detailMaximum,
                    _ => _detailOffset,
                };
                SetDetailOffset(next); return 0;
            case NativeMethods.WindowMessageCommand:
            case NativeMethods.WindowMessageDrawItem:
            case NativeMethods.WindowMessageControlColorEdit:
            case NativeMethods.WindowMessageControlColorStatic:
            case NativeMethods.WindowMessageControlColorButton:
                return NativeMethods.SendMessage(_handle, message, word, parameter);
        }
        return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
    }

    private void PaintDetails()
    {
        nint dc = NativeMethods.BeginPaint(_detailPanel, out var paint);
        try
        {
            NativeThemePalette palette = NativeTheme.Palette(_dark);
            _ = NativeMethods.GetClientRectangle(_detailPanel, out var client);
            NativeTheme.Fill(dc, client, palette.Panel);
            nint[] inputs = [_nameEdit, _fetchUrlEdit, _pushUrlEdit];
            for (int index = 0; index < inputs.Length; index++)
            {
                var bounds = _inputFrames[index];
                NativeTheme.FillRounded(dc, bounds, NativeMethods.GetFocus() == inputs[index] ? palette.Accent : palette.BorderStrong, S(10));
                int border = Math.Max(1, S(1));
                bounds.Left += border; bounds.Top += border; bounds.Right -= border; bounds.Bottom -= border;
                NativeTheme.FillRounded(dc, bounds, palette.Panel, S(8));
            }
        }
        finally { _ = NativeMethods.EndPaint(_detailPanel, ref paint); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint Size, Mask;
        public int Minimum, Maximum;
        public uint Page;
        public int Position, TrackPosition;
    }

    [DllImport("user32.dll", EntryPoint = "SetScrollInfo")]
    private static extern int SetScrollInformation(nint window, int bar, ref ScrollInfo info, bool redraw);

    [DllImport("user32.dll", EntryPoint = "GetScrollInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollInformation(nint window, int bar, ref ScrollInfo info);
}
